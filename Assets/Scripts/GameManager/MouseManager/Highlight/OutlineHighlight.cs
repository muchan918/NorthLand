using System.Collections.Generic;
using NorthLand.Combat;
using UnityEngine;
using UnityEngine.Rendering;

/// 아웃라인 표시 상태의 종류. 배타 토글이 아니라 **독립 플래그**다 — 서로 다른 주체(호버/단일 선택/그룹 선택/
/// 합성 프리뷰)가 같은 대상에 동시에 걸 수 있고, 최종 색은 우선순위로 결정된다(#213 §4).
public enum OutlineKind
{
    Hover,          // MouseManager 호버
    Selected,       // MouseManager 단일 선택
    GroupSelected,  // TowerMergeCoordinator 그룹 선택(합성 재료)
    MergePreview,   // 합성 후보 버튼 호버 시 "실제로 소모될 재료"
}

/// 대상 오브젝트에 아웃라인을 켜고 끄는 컴포넌트(#213, Docs/Core/InteractionOutline.md §3~§6).
///
/// 표시 방식은 **shell**: 대상의 렌더러마다 같은 메시를 쓰는 자식 렌더러를 만들고 거기에만 FlatKit
/// 아웃라인 머티리얼을 물린다. 원본 머티리얼·프리팹은 건드리지 않으므로 URP Lit·FBX 내장·FlatKit 어느
/// 머티리얼이든 동일하게 동작한다. shell은 `OutlineShell` 레이어에 두고, URP 렌더러의 Opaque/Transparent
/// Layer Mask에서 그 레이어를 빼두었기 때문에 **본체 패스는 그려지지 않고 아웃라인 패스만** 나온다.
///
/// 사용법(멱등 — 토글 API를 만들지 말 것. 훅 호출이 비대칭이면 상태가 어긋난다):
///     OutlineHighlight.GetOrAdd(go).Set(OutlineKind.Hover, true);
///
/// 파괴 정리가 없는 이유: 런타임에 새로 만드는 오브젝트는 shell GameObject뿐이고 그건 대상의 자식이라
/// 함께 파괴된다. 메시는 공유 에셋(원본 또는 스무스 사본), 머티리얼·프록시 메시는 static 공유물이다.
/// 즉 `RangeCircle`(PR#115 리뷰)처럼 OnDestroy에서 Mesh/Material을 파괴할 대상이 **없다** —
/// 대신 "대상별 인스턴스를 만들지 않는다"는 규칙을 깨지 말 것.
[DisallowMultipleComponent]
public class OutlineHighlight : MonoBehaviour
{
    // ── 임시 색(아트 TBD). 교체 지점은 이 3줄이 전부다(#213 완료 기준) ──────────────
    private static readonly Color k_HoverColor = new(1f, 0.92f, 0.2f);          // 노랑: 호버
    private static readonly Color k_SelectColor = new(0.25f, 1f, 0.35f);        // 초록: 선택(단일·그룹 공용)
    private static readonly Color k_MergePreviewColor = new(1f, 0.35f, 0.75f);  // 핑크: 합성 소모 예정

    private const string k_ShaderName = "FlatKit/Stylized Surface";
    private const string k_ShellLayerName = "OutlineShell";
    private const string k_ShellName = "OutlineShell";

    // 안전판. 실측 최대는 Castle(441 MeshRenderer + 21 Skinned = 462)이라 그건 통과시킨다 — shell은 전부 같은
    // 머티리얼이라 SRP Batcher로 묶이고, 비용은 "호버 중인 그 오브젝트 하나를 한 번 더 그리는 것"이다.
    // 이 상한을 넘는 대상은 아웃라인을 생략하고 경고만 남긴다(저폴리 실루엣 프록시는 아트 작업 — #138/#148).
    private const int k_MaxShellRenderers = 512;
    private const float k_DefaultWidth = 0.5f;

    // 셰이더 프로퍼티/키워드. 값의 근거는 §3.4(게임 카메라가 직교라는 전제).
    private const string k_PropEnabled = "_OutlineEnabled";
    private const string k_PropColor = "_OutlineColor";
    private const string k_PropWidth = "_OutlineWidth";
    private const string k_PropDistanceImpact = "_CameraDistanceImpact";
    private const string k_PropDepthOffset = "_OutlineDepthOffset";
    private const string k_KeywordOutline = "DR_OUTLINE_ON";
    private const string k_KeywordSmoothNormals = "DR_OUTLINE_SMOOTH_NORMALS";

    private enum Slot { Hover = 0, Select = 1, MergePreview = 2 }

    // [Slot, 스무스 노멀 사용 여부] 공유 머티리얼. 대상마다 만들지 않으므로 shell끼리 SRP Batcher로 묶인다.
    private static readonly Material[,] s_materials = new Material[3, 2];
    private static float s_width = k_DefaultWidth;
    private static int s_shellLayer = -2; // -2 = 미조회, -1 = 레이어 없음(경고 1회)

    private readonly bool[] _flags = new bool[4];
    private readonly List<Shell> _shells = new();
    private readonly List<SkinnedPair> _skinned = new();
    private bool _built;
    private bool _visible;
    private Slot _slot;

    private readonly struct Shell
    {
        public readonly Renderer Renderer;
        public readonly bool SmoothNormals; // 메시에 uv3(평균 노멀)이 있는가 — 없으면 키워드 없는 머티리얼을 써야 한다

        public Shell(Renderer renderer, bool smoothNormals)
        {
            Renderer = renderer;
            SmoothNormals = smoothNormals;
        }
    }

    private readonly struct SkinnedPair
    {
        public readonly SkinnedMeshRenderer Source;
        public readonly SkinnedMeshRenderer Shell;

        public SkinnedPair(SkinnedMeshRenderer source, SkinnedMeshRenderer shell)
        {
            Source = source;
            Shell = shell;
        }
    }

    /// 대상 GameObject의 컴포넌트를 가져오거나(없으면) 붙인다. 타워 마커처럼 런타임 부착 경로에서도 쓰려면
    /// 인스펙터 배선 없이 얻을 수 있어야 한다.
    public static OutlineHighlight GetOrAdd(GameObject go)
    {
        if (go == null) return null;
        return go.TryGetComponent(out OutlineHighlight existing) ? existing : go.AddComponent<OutlineHighlight>();
    }

    /// 전역 아웃라인 폭. 직교 카메라라 화면 비율이 고정돼, 줌아웃 시 고정 폭을 쓰면 오브젝트를 삼킨다
    /// → 드라이버가 `orthographicSize`에 반비례로 갱신한다(§3.4).
    public static void SetWidth(float width)
    {
        if (Mathf.Approximately(s_width, width)) return;
        s_width = width;

        foreach (var m in s_materials)
        {
            if (m != null) m.SetFloat(k_PropWidth, width);
        }
    }

    /// 상태 플래그를 켜고 끈다(멱등). 최종 색은 MergePreview > (Selected | GroupSelected) > Hover.
    public void Set(OutlineKind kind, bool on)
    {
        int index = (int)kind;
        if (_flags[index] == on) return;

        _flags[index] = on;
        Apply();
    }

    // 원본 스킨드 메시의 블렌드셰이프 가중치를 shell로 복사한다. 켜져 있는 동안만 돈다(영지 산이
    // BlendShapeAnimator로 움직이므로 복사를 빠뜨리면 아웃라인 형태가 원본과 어긋난다).
    private void LateUpdate()
    {
        if (!_visible || _skinned.Count == 0) return;

        foreach (var pair in _skinned)
        {
            if (pair.Source == null || pair.Shell == null) continue;

            int count = Mathf.Min(BlendShapeCount(pair.Source), BlendShapeCount(pair.Shell));
            for (int i = 0; i < count; i++)
            {
                pair.Shell.SetBlendShapeWeight(i, pair.Source.GetBlendShapeWeight(i));
            }
        }
    }

    private static int BlendShapeCount(SkinnedMeshRenderer smr) =>
        smr.sharedMesh != null ? smr.sharedMesh.blendShapeCount : 0;

    private void Apply()
    {
        if (!TryResolveSlot(out var slot))
        {
            SetVisible(false);
            return;
        }

        EnsureShells();
        if (_shells.Count == 0) return;

        if (!_visible || _slot != slot)
        {
            _slot = slot;
            foreach (var shell in _shells)
            {
                if (shell.Renderer != null) shell.Renderer.sharedMaterial = GetSharedMaterial(slot, shell.SmoothNormals);
            }
        }

        SetVisible(true);
    }

    // 우선순위: 합성 프리뷰(핑크)가 선택(초록)을 덮고, 선택이 호버(노랑)를 덮는다.
    // 선택된 대상에 커서를 올려도 노랑으로 밀리지 않아야 한다(#213 완료 기준).
    private bool TryResolveSlot(out Slot slot)
    {
        if (_flags[(int)OutlineKind.MergePreview]) { slot = Slot.MergePreview; return true; }
        if (_flags[(int)OutlineKind.Selected] || _flags[(int)OutlineKind.GroupSelected]) { slot = Slot.Select; return true; }
        if (_flags[(int)OutlineKind.Hover]) { slot = Slot.Hover; return true; }

        slot = Slot.Hover;
        return false;
    }

    // shell GameObject는 파괴하지 않고 렌더러만 끈다 — 호버는 초당 여러 번 바뀐다.
    private void SetVisible(bool on)
    {
        if (_visible == on) return;
        _visible = on;

        foreach (var shell in _shells)
        {
            if (shell.Renderer != null) shell.Renderer.enabled = on;
        }
    }

    private void EnsureShells()
    {
        if (_built) return;
        _built = true; // 실패해도 매번 재시도하지 않는다(경고 스팸 방지)

        int layer = ShellLayer;
        if (layer < 0) return;

        var sources = new List<Renderer>();
        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (IsEligible(r)) sources.Add(r);
        }
        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (IsEligible(r)) sources.Add(r);
        }

        if (sources.Count == 0)
        {
            Debug.LogWarning($"[아웃라인] {name}: 아웃라인을 걸 렌더러(MeshRenderer/SkinnedMeshRenderer)가 없습니다.", this);
            return;
        }

        if (sources.Count > k_MaxShellRenderers)
        {
            // 조용한 누락 금지 — 왜 아웃라인이 안 나오는지 남긴다.
            Debug.LogWarning($"[아웃라인] {name}: 렌더러가 {sources.Count}개로 상한({k_MaxShellRenderers})을 넘어 " +
                             "아웃라인을 생략합니다. 저폴리 실루엣 프록시가 필요합니다(#138/#148).", this);
            return;
        }

        foreach (var r in sources) CreateShell(r, layer);
    }

    private static bool IsEligible(Renderer r)
    {
        if (r == null) return false;
        if (r.gameObject.name == k_ShellName) return false; // 우리가 만든 shell(재수집 방지)

        // 사거리 원은 타워 자식으로 생성된다(Tower.cs / AuraTower.cs) — 원판에 테두리가 생기면 안 된다.
        // Fill 자식이 MeshRenderer라 타입 필터로는 걸러지지 않으므로 조상으로 판정한다.
        if (r.GetComponentInParent<RangeCircle>() != null) return false;

        return SourceMesh(r) != null;
    }

    private static Mesh SourceMesh(Renderer r)
    {
        if (r is SkinnedMeshRenderer smr)
        {
            return smr.sharedMesh != null && smr.sharedMesh.vertexCount > 0 ? smr.sharedMesh : null;
        }

        if (r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null && mf.sharedMesh.vertexCount > 0)
        {
            return mf.sharedMesh;
        }
        return null;
    }

    private void CreateShell(Renderer src, int layer)
    {
        var source = SourceMesh(src);
        // 스무스 노멀 사본으로 바꿔 끼운다. 없으면 원본이 그대로 돌아온다(아웃라인이 끊겨 보이지만 동작 유지, §6.4).
        var mesh = OutlineSmoothMeshRegistry.Instance != null
            ? OutlineSmoothMeshRegistry.Instance.Resolve(source)
            : source;
        if (mesh == null) return;

        var go = new GameObject(k_ShellName) { layer = layer };
        go.transform.SetParent(src.transform, false); // 로컬 TRS identity → 원본과 정확히 겹친다

        Renderer shellRenderer;
        if (src is SkinnedMeshRenderer srcSkinned)
        {
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = srcSkinned.bones; // 본 배열을 공유해 원본과 같은 포즈로 움직인다
            smr.rootBone = srcSkinned.rootBone;
            smr.updateWhenOffscreen = srcSkinned.updateWhenOffscreen;
            shellRenderer = smr;

            if (mesh.blendShapeCount > 0) _skinned.Add(new SkinnedPair(srcSkinned, smr));
        }
        else
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            shellRenderer = go.AddComponent<MeshRenderer>();
        }

        // 콜라이더를 만들지 않는다 — 선택/호버 레이캐스트를 구조적으로 방해할 수 없게 한다.
        bool smooth = HasSmoothNormals(mesh);
        shellRenderer.sharedMaterial = GetSharedMaterial(_slot, smooth);
        shellRenderer.shadowCastingMode = ShadowCastingMode.Off;
        shellRenderer.receiveShadows = false;
        shellRenderer.enabled = _visible;

        _shells.Add(new Shell(shellRenderer, smooth));
    }

    private static bool HasSmoothNormals(Mesh mesh) =>
        mesh != null && mesh.HasVertexAttribute(VertexAttribute.TexCoord2); // uv3 = 평균 노멀(베이크 결과)

    private static int ShellLayer
    {
        get
        {
            if (s_shellLayer != -2) return s_shellLayer;

            s_shellLayer = LayerMask.NameToLayer(k_ShellLayerName);
            if (s_shellLayer < 0)
            {
                Debug.LogError($"[아웃라인] 레이어 '{k_ShellLayerName}'가 없습니다. " +
                               "ProjectSettings의 레이어와 URP 렌더러 설정이 커밋과 어긋났는지 확인하세요(문서 §7).");
            }
            return s_shellLayer;
        }
    }

    private static Material GetSharedMaterial(Slot slot, bool smoothNormals)
    {
        int s = (int)slot;
        int n = smoothNormals ? 1 : 0;
        if (s_materials[s, n] != null) return s_materials[s, n];

        var shader = Shader.Find(k_ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[아웃라인] 셰이더를 찾지 못했습니다: {k_ShaderName}");
            return null;
        }

        var mat = new Material(shader) { name = $"OutlineShell_{slot}{(smoothNormals ? "_Smooth" : "")}" };
        mat.SetFloat(k_PropEnabled, 1f);
        mat.EnableKeyword(k_KeywordOutline);
        // uv3가 없는 메시에 스무스 노멀 키워드를 켜면 노멀이 0이 되어 아웃라인이 깨진다 → 메시에 맞춰 켠다.
        if (smoothNormals) mat.EnableKeyword(k_KeywordSmoothNormals);
        mat.SetColor(k_PropColor, SlotColor(slot));
        mat.SetFloat(k_PropWidth, s_width);
        mat.SetFloat(k_PropDistanceImpact, 1f); // 직교는 clipPos.w가 1로 고정 — 거리 항을 상수로 만든다
        mat.SetFloat(k_PropDepthOffset, 0f);    // 직교에서 0이 아니면 헐 전체가 깊이 테스트에서 탈락한다

        s_materials[s, n] = mat;
        return mat;
    }

    private static Color SlotColor(Slot slot) => slot switch
    {
        Slot.MergePreview => k_MergePreviewColor,
        Slot.Select => k_SelectColor,
        _ => k_HoverColor,
    };
}
