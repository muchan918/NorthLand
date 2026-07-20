using System.Collections;
using UnityEngine;

/// <summary>
/// 영토 노드의 상태→비주얼 스왑 전담 컴포넌트(#93).<br/>
/// <see cref="TerritoryNodeView"/>와 같은 프리팹 루트에 붙으면 뷰가 색상 경로 대신 이쪽에 위임한다:<br/>
/// - 본진: 아무것도 스폰하지 않음(본진 비주얼은 씬에 별도 구현)<br/>
/// - Selectable: 소용돌이(<see cref="VortexVisual"/>)<br/>
/// - Owned: Mountain 프리팹(노드 Id 기반 결정적 선택) — 확보 직후엔 소용돌이 소멸 → 솟아오르는 연출<br/>
/// 내부 Kind 캐시로 같은 상태의 Refresh 재진입(호버·OnChanged·낮 시작)에는 재스폰하지 않는다.
/// </summary>
public class TerritoryNodeStateVisual : MonoBehaviour
{
    private enum Kind { None, Vortex, Mountain }

    [Header("보유(Owned) 영토")]
    [Tooltip("노드가 확보되면 나타날 땅 프리팹 후보 (@NorthLand Mountain_01~06). 노드 Id % 개수로 결정적 선택.")]
    [SerializeField] GameObject[] _mountainPrefabs;

    [Tooltip("Mountain 프리팹 저작 스케일에 곱할 보정 배율")]
    [SerializeField] float _mountainScale = 1f;

    [Tooltip("산의 로컬 Y 오프셋 — 노드(물 위)보다 산 밑동을 해저(y=0)에 앉히는 용도")]
    [SerializeField] float _mountainYOffset = 0f;

    [Header("선택 가능(Selectable) 영토 — 소용돌이")]
    [Tooltip("소용돌이 머티리얼(URP/Unlit Transparent 권장). 비우면 Sprites/Default 폴백.")]
    [SerializeField] Material _vortexMaterial;

    [Tooltip("소용돌이 지름(월드 유닛)")]
    [SerializeField] float _vortexDiameter = 60f;

    [Tooltip("소용돌이 기본 색")]
    [SerializeField] Color _vortexColor = new(0.35f, 0.75f, 0.95f, 0.85f);

    [Tooltip("호버 시 색 (#67 호버 하이라이트 계승)")]
    [SerializeField] Color _vortexHoverColor = new(0.6f, 0.95f, 1f, 1f);

    [Tooltip("오늘 확장 소진 시 색 (기존 회색 노드 시맨틱 계승)")]
    [SerializeField] Color _vortexDimColor = new(0.55f, 0.55f, 0.55f, 0.5f);

    [Tooltip("소용돌이 회전 속도 (도/초)")]
    [SerializeField] float _vortexSpinSpeed = 60f;

    [Header("확보 연출")]
    [Tooltip("소용돌이가 스핀업하며 사라지는 시간(초)")]
    [SerializeField] float _vanishDuration = 0.25f;

    [Tooltip("땅이 솟아오르는 시간(초)")]
    [SerializeField] float _popDuration = 0.5f;

    private Kind _kind = Kind.None;
    private GameObject _current;
    private VortexVisual _vortex;
    private bool _isAnimating;

    /// <summary>
    /// 뷰의 Refresh에서 호출된다. Locked(비활성)는 뷰가 SetActive(false)로 처리하므로 여기 오지 않는다.
    /// playClaimFx는 이번 Refresh가 확보 직후(OnNodeClaimed)임을 뜻하며 1회성 연출을 튼다.
    /// </summary>
    public void Apply(TerritoryState state, bool isHome, int nodeId, bool hovered, bool canClaimNow, bool playClaimFx)
    {
        if (_isAnimating)
        {
            return; // 연출 코루틴이 끝나면 최종 상태(Mountain)와 일치한다
        }

        if (isHome)
        {
            SetKind(Kind.None, nodeId);
            return;
        }

        if (state == TerritoryState.Selectable)
        {
            SetKind(Kind.Vortex, nodeId);
            _vortex.SetClaimable(canClaimNow);
            _vortex.SetHighlight(hovered);
            return;
        }

        if (state == TerritoryState.Owned)
        {
            if (playClaimFx && _kind == Kind.Vortex)
            {
                StartCoroutine(ClaimSequence(nodeId));
            }
            else
            {
                SetKind(Kind.Mountain, nodeId); // 재로드 등 이벤트 없는 Owned는 연출 없이 완성 상태
            }
        }
    }

    private void SetKind(Kind kind, int nodeId)
    {
        if (_kind == kind)
        {
            return;
        }

        if (_current != null)
        {
            Destroy(_current);
            _current = null;
            _vortex = null;
        }

        _kind = kind;
        switch (kind)
        {
            case Kind.Vortex:
                _current = new GameObject("Vortex");
                _current.transform.SetParent(transform, false);
                _vortex = _current.AddComponent<VortexVisual>();
                _vortex.Init(_vortexMaterial, _vortexDiameter, _vortexColor, _vortexHoverColor, _vortexDimColor, _vortexSpinSpeed);
                break;
            case Kind.Mountain:
                _current = SpawnMountain(nodeId);
                break;
        }
    }

    // 확보 연출: 소용돌이 스핀업+축소 → 파괴 → 땅이 오버슈트하며 솟아오름.
    private IEnumerator ClaimSequence(int nodeId)
    {
        _isAnimating = true;

        yield return _vortex.VanishRoutine(_vanishDuration);
        Destroy(_current);
        _vortex = null;

        _kind = Kind.Mountain;
        _current = SpawnMountain(nodeId);
        if (_current != null)
        {
            var t = _current.transform;
            Vector3 finalScale = t.localScale;
            float elapsed = 0f;
            while (elapsed < _popDuration)
            {
                elapsed += Time.deltaTime;
                float p = Mathf.Clamp01(elapsed / _popDuration);
                t.localScale = finalScale * Mathf.Max(0.01f, EaseOutBack(p));
                yield return null;
            }
            t.localScale = finalScale;
        }

        _isAnimating = false;
    }

    private GameObject SpawnMountain(int nodeId)
    {
        if (_mountainPrefabs == null || _mountainPrefabs.Length == 0)
        {
            Debug.LogError("[영토] Mountain 프리팹이 할당되지 않았습니다.", this);
            return null;
        }

        var prefab = _mountainPrefabs[Mathf.Abs(nodeId) % _mountainPrefabs.Length];
        if (prefab == null)
        {
            Debug.LogError($"[영토] Mountain 프리팹 슬롯이 비어 있습니다: {Mathf.Abs(nodeId) % _mountainPrefabs.Length}", this);
            return null;
        }

        var go = Instantiate(prefab, transform);
        // 프리팹 루트에 씬 배치 좌표가 베이크되어 있어(예: x=-554) 위치·회전만 리셋하고
        // 스케일은 저작값(5배)을 유지한다 — 그래프 간격이 산 크기에 맞춰 확대되어 있음(#93).
        go.transform.localPosition = new Vector3(0f, _mountainYOffset, 0f);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale *= _mountainScale;

        // Sweet_Land의 BlendShapeAnimator는 무한 루프(솟았다 꺼졌다)라 산이 계속 출렁인다.
        // Destroy는 프레임 끝에 실행되므로 enabled=false를 먼저 걸어 Start 코루틴 진입을 막는다.
        foreach (var animator in go.GetComponentsInChildren<ithappy.BlendShapeAnimator>(true))
        {
            animator.enabled = false;
            Destroy(animator);
        }

        // 저작된 형태(가중치 0)로 고정 — 애니메이터가 남긴 중간 가중치 방지.
        foreach (var skinned in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skinned.sharedMesh != null && skinned.sharedMesh.blendShapeCount > 0)
            {
                skinned.SetBlendShapeWeight(0, 0f);
            }
        }

        // 클릭 판정은 노드 루트 SphereCollider 전용(MouseManager가 부모 미탐색) —
        // 산의 MeshCollider가 다른 시스템의 레이캐스트를 가로채지 않게 전부 끈다.
        foreach (var collider in go.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        return go;
    }

    // ease-out-back: 1을 살짝 넘겼다가(오버슈트 ~1.1) 안착 — "뽁 튀어나오는" 느낌.
    private static float EaseOutBack(float p)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float q = p - 1f;
        return 1f + c3 * q * q * q + c1 * q * q;
    }
}
