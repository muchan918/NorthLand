using UnityEngine;
using UnityEngine.SceneManagement;

/// 호버=노랑 / 단일 선택=초록 아웃라인을 구동하는 유일한 주체(#213, Docs/Core/InteractionOutline.md §5.1).
///
/// 각 대상의 `IHoverable`/`ISelectable` 훅 안에서 켜지 않고 **MouseManager 이벤트를 구독**하는 이유:
/// 두 이벤트는 항상 "현재 대상"을 실어 오고 이 드라이버가 이전 대상을 들고 있으므로 **대칭이 구조적으로
/// 보장**된다. 덕분에 (1) `Tower`·`BuildingInfo`·`AuraTower`·`TerritoryNodeView`를 한 줄도 수정하지 않고,
/// (2) WL-087(코디네이터 RefreshPanel이 count==1에서 OnSelected만 부르고 대칭 OnDeselected가 없음)에
/// 걸리지 않으며, (3) `MouseManager.Select`는 낮/밤 게이트가 없어 **밤에도 초록이 뜬다**(사거리 원·정보
/// 패널과 피드백이 일치).
///
/// 그룹 초록(합성 재료)과 핑크 프리뷰는 이 드라이버가 아니라 `TowerMergeCoordinator`가 구동한다 —
/// 집합의 소유·게이팅 권한이 코디네이터에 있기 때문(§5.2·§5.3).
///
/// 씬 파일을 건드리지 않도록 런타임에 스스로 부팅한다(정본 씬 병합 규칙 `Docs/Core/SceneWorkflow.md`와
/// 충돌 방지 — 타워 마커 `TowerGroupSelectable`이 런타임 부착인 것과 같은 계보). 그래서 튜닝값은
/// 인스펙터가 아니라 아래 상수다(`RangeCircle` 선례).
[DisallowMultipleComponent]
public class OutlineInteractionDriver : MonoBehaviour
{
    // 폭 = k_WidthNumerator / orthographicSize (§3.4). 게임 줌 범위(70~300)에서 size 70 → 0.5, 300 → 0.117.
    private const float k_WidthNumerator = 35f;
    private const float k_MinWidth = 0.12f;
    private const float k_MaxWidth = 0.7f;
    private const float k_PerspectiveWidth = 0.5f; // 원근 카메라(에디터 프리뷰 등)에서는 줌 스케일이 무의미

    private static OutlineInteractionDriver s_instance;

    private OutlineHighlight _hovered;
    private OutlineHighlight _selected;
    private Camera _camera;
    private bool _subscribed;
    private bool _warnedNoMouseManager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_instance != null) return;

        var go = new GameObject(nameof(OutlineInteractionDriver));
        s_instance = go.AddComponent<OutlineInteractionDriver>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Destroy(gameObject);
            return;
        }
        s_instance = this;
    }

    private void Start()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        TrySubscribe();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;

        // MouseManager는 DontDestroyOnLoad라 이쪽보다 오래 살 수 있다 → 반드시 해제.
        var mm = MouseManager.Instance;
        if (mm != null && _subscribed)
        {
            mm.OnHoverChanged -= HandleHoverChanged;
            mm.OnSelectionChanged -= HandleSelectionChanged;
        }

        if (s_instance == this) s_instance = null;
    }

    private void LateUpdate()
    {
        // MouseManager가 뒤늦게(다른 씬에서) 등장할 수 있어 붙을 때까지 확인한다 — null 체크 1회라 비용은 없다.
        if (!_subscribed) TrySubscribe();

        UpdateWidth();
    }

    private void TrySubscribe()
    {
        if (_subscribed) return;

        var mm = MouseManager.Instance;
        if (mm == null)
        {
            if (!_warnedNoMouseManager)
            {
                _warnedNoMouseManager = true;
                Debug.LogWarning("[아웃라인] MouseManager가 아직 없어 호버·선택 아웃라인이 대기 중입니다.");
            }
            return;
        }

        mm.OnHoverChanged += HandleHoverChanged;
        mm.OnSelectionChanged += HandleSelectionChanged;
        _subscribed = true;
    }

    // 씬이 바뀌면 이전 대상은 이미 파괴됐다. MouseManager도 같은 시점에 알림 없이 필드만 리셋하므로
    // (WL-033) 해제 이벤트가 오지 않는다 → 이쪽 참조도 직접 비운다.
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _hovered = null;
        _selected = null;
        _camera = null;
    }

    private void HandleHoverChanged(IHoverable hoverable)
    {
        Swap(ref _hovered, Resolve(hoverable), OutlineKind.Hover);
    }

    private void HandleSelectionChanged(ISelectable selectable)
    {
        Swap(ref _selected, Resolve(selectable), OutlineKind.Selected);
    }

    private static void Swap(ref OutlineHighlight current, OutlineHighlight next, OutlineKind kind)
    {
        if (ReferenceEquals(current, next)) return;

        if (current != null) current.Set(kind, false); // 파괴된 대상은 Unity의 가짜 null로 걸러진다
        current = next;
        if (current != null) current.Set(kind, true);
    }

    // 대상 GameObject 결정: 기본은 히트한 컴포넌트의 GameObject지만, IOutlineTargetProvider가 있으면
    // 그쪽이 지정한 대상을 쓴다(영지 노드는 상태에 따라 시각물이 회오리↔섬으로 갈린다, §5.4).
    private static OutlineHighlight Resolve(object target)
    {
        var component = target as Component;
        if (component == null) return null; // 해제(null) 또는 MonoBehaviour가 아닌 구현

        GameObject go = component.gameObject;
        if (go.TryGetComponent(out IOutlineTargetProvider provider)) go = provider.OutlineTarget;

        return go == null ? null : OutlineHighlight.GetOrAdd(go);
    }

    private void UpdateWidth()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return;

        float width = _camera.orthographic
            ? Mathf.Clamp(k_WidthNumerator / Mathf.Max(0.001f, _camera.orthographicSize), k_MinWidth, k_MaxWidth)
            : k_PerspectiveWidth;

        OutlineHighlight.SetWidth(width); // 같은 값이면 내부에서 무시된다
    }
}
