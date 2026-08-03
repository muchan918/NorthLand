using System.Collections.Generic;
using NorthLand.Combat;
using UnityEngine;

/// 아웃라인 표시 상태의 종류. 배타 토글이 아니라 **독립 플래그**다 — 서로 다른 주체(호버/단일 선택/그룹 선택/
/// 합성 프리뷰)가 같은 대상에 동시에 걸 수 있고, 최종 색은 우선순위로 결정된다(#213 §4).
public enum OutlineKind
{
    Hover,          // MouseManager 호버
    Selected,       // MouseManager 단일 선택
    GroupSelected,  // TowerMergeCoordinator 그룹 선택(합성 재료)
    MergePreview,   // 합성 후보 버튼 호버 시 "실제로 소모될 재료"
}

/// 대상 오브젝트에 아웃라인을 켜고 끄는 컴포넌트(#213, Docs/Core/InteractionOutline.md).
///
/// 표시 방식은 **스크린 스페이스 실루엣**이다(2026-08-03 이행, Docs/Rendering/WIP-OutlineMigration.md).
/// 이 컴포넌트는 아무것도 생성하지 않는다 — "이 렌더러들을 이 슬롯으로"만 `InteractionOutlineRegistry`에
/// 등록하고, `InteractionOutlineFeature`가 마스크 RT에 그린 뒤 dilate 해 링을 뽑아 합성한다.
/// 그래서 **부품 수와 무관하게 오브젝트 전체 실루엣 하나**가 나온다.
///
/// 이전 방식(인버티드 헐 shell)에서 사라진 것들:
///  - 대상 렌더러마다 자식 shell GameObject를 만들던 비용(첫 호버 스파이크 포함)
///  - 렌더러 512개 상한 — 이제 상한이 없다
///  - 스무스 노멀 프리베이크 의존 — 지오메트리를 부풀리지 않으므로 필요 없다
///  - `OutlineShell` 레이어와 URP 렌더러 마스크 3곳(Opaque/Transparent/Prepass) 설정 의존
///  - 스킨드 메시 블렌드셰이프를 매 프레임 shell로 복사하던 LateUpdate
///
/// 사용법(멱등 — 토글 API를 만들지 말 것. 훅 호출이 비대칭이면 상태가 어긋난다):
///     OutlineHighlight.GetOrAdd(go).Set(OutlineKind.Hover, true);
///
/// 색·두께는 이제 이 컴포넌트가 아니라 **렌더러 피처의 인스펙터**에 있다
/// (`PC_Renderer`/`Mobile_Renderer` → Interaction Outline). 아트가 코드 수정 없이 만질 수 있다.
[DisallowMultipleComponent]
public class OutlineHighlight : MonoBehaviour
{
    // 셸 시절 자식 오브젝트 이름. 정본 씬에 저장된 잔재가 남아 있을 수 있어 수집에서 계속 배제한다.
    private const string k_LegacyShellName = "OutlineShell";

    private enum Slot { Hover, Select, MergePreview }

    private readonly bool[] _flags = new bool[4];
    private readonly List<Renderer> _sources = new();
    private bool _collected;

    /// 대상 GameObject의 컴포넌트를 가져오거나(없으면) 붙인다. 타워 마커처럼 런타임 부착 경로에서도 쓰려면
    /// 인스펙터 배선 없이 얻을 수 있어야 한다.
    public static OutlineHighlight GetOrAdd(GameObject go)
    {
        if (go == null) return null;
        return go.TryGetComponent(out OutlineHighlight existing) ? existing : go.AddComponent<OutlineHighlight>();
    }

    /// <summary>
    /// 셸 시절의 전역 아웃라인 폭 API. **스크린 스페이스 실루엣에서는 아무 일도 하지 않는다.**
    /// 두께가 스크린 픽셀 단위라 줌에 따라 보정할 필요가 없어졌다 — 오브젝트를 삼키던 문제 자체가 없다.
    /// 호출부(`OutlineInteractionDriver`)를 건드리지 않기 위해 시그니처만 남겼다.
    /// 두께는 렌더러 피처의 `Thickness`(px)로 조정한다.
    /// </summary>
    public static void SetWidth(float width)
    {
        // 의도적으로 비어 있다. 지우면 드라이버가 컴파일되지 않으므로 이행 정리 시 함께 제거한다.
    }

    /// 상태 플래그를 켜고 끈다(멱등). 최종 색은 MergePreview > (Selected | GroupSelected) > Hover.
    public void Set(OutlineKind kind, bool on)
    {
        int index = (int)kind;
        if (_flags[index] == on) return;

        _flags[index] = on;
        Apply();
    }

    private void OnDisable()
    {
        // 비활성화된 대상이 마스크에 남으면 유령 실루엣이 된다.
        InteractionOutlineRegistry.Clear(this);
    }

    private void OnEnable()
    {
        // 다시 켜졌을 때 플래그가 남아 있으면 복구한다(비활성 중 Set이 들어왔을 수도 있다).
        Apply();
    }

    private void OnDestroy()
    {
        InteractionOutlineRegistry.Clear(this);
    }

    private void Apply()
    {
        if (!isActiveAndEnabled || !TryResolveSlot(out Slot slot))
        {
            InteractionOutlineRegistry.Clear(this);
            return;
        }

        EnsureSources();

        if (_sources.Count == 0)
        {
            return;
        }

        InteractionOutlineRegistry.Set(this, _sources, ToRegistrySlot(slot));
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

    private static InteractionOutlineRegistry.Slot ToRegistrySlot(Slot slot) => slot switch
    {
        Slot.MergePreview => InteractionOutlineRegistry.Slot.MergePreview,
        Slot.Select => InteractionOutlineRegistry.Slot.Selected,
        _ => InteractionOutlineRegistry.Slot.Hover,
    };

    private void EnsureSources()
    {
        // 시각물이 런타임에 교체되는 대상(영지 노드 회오리→섬, 프리팹 교체 등)은 1회 수집으로 고정하면
        // 교체 뒤 죽은 참조만 남아 **아웃라인이 조용히 사라진다** → 죽은 렌더러를 감지하면 다시 수집한다.
        if (_collected && !HasDeadSource())
        {
            return;
        }

        _collected = true; // 실패해도 매번 재시도하지 않는다(경고 스팸 방지)
        _sources.Clear();

        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (IsEligible(r)) _sources.Add(r);
        }

        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (IsEligible(r)) _sources.Add(r);
        }

        if (_sources.Count == 0)
        {
            Debug.LogWarning($"[아웃라인] {name}: 아웃라인을 걸 렌더러(MeshRenderer/SkinnedMeshRenderer)가 없습니다.", this);
        }
    }

    private bool HasDeadSource()
    {
        foreach (var r in _sources)
        {
            if (r == null) return true;
        }
        return false;
    }

    private static bool IsEligible(Renderer r)
    {
        if (r == null) return false;
        if (r.gameObject.name == k_LegacyShellName) return false; // 셸 시절 잔재

        // 사거리 원은 타워 자식으로 생성된다(Tower.cs / AuraTower.cs) — 원판에 테두리가 생기면 안 된다.
        // Fill 자식이 MeshRenderer라 타입 필터로는 걸러지지 않으므로 조상으로 판정한다.
        if (r.GetComponentInParent<RangeCircle>() != null) return false;

        return HasDrawableMesh(r);
    }

    private static bool HasDrawableMesh(Renderer r)
    {
        if (r is SkinnedMeshRenderer smr)
        {
            return smr.sharedMesh != null && smr.sharedMesh.vertexCount > 0;
        }

        return r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null && mf.sharedMesh.vertexCount > 0;
    }
}
