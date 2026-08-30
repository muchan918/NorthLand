using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;   // 프로젝트는 신규 Input System 사용
using UnityEngine.SceneManagement;
using CombatSpace;               // CombatMapTileView / CombatTileType (전투 타일 판정)
using NorthLand.Core;

/// 클릭으로 선택 가능한 배치물(타워·건물 등)이 구현한다. (요구사항 ②)
public class MouseManager : MonoBehaviour
{
    /// 상호작용 모드. **공개인 이유는 통지(`OnModeChanged`)와 짝이기 때문**이다 — 커서 그림처럼
    /// "지금 무엇을 하는 중인가"에 반응하는 연출이 모드를 이름으로 받아야 한다. 구독자가 이 값으로
    /// 모드를 **바꿀 수는 없다**(전환 창구는 여전히 private `SetMode` 하나뿐).
    public enum Mode
    {
        Idle,
        BoxSelect,      // Idle의 하위 상태 — 배치·조준 중에는 진입할 수 없다(#261)
        UnitDrag,       // BoxSelect와 형제. 누른 순간 IDragHandle을 잡았으면 이쪽으로 갈린다(§5.4)
        Placement,
        SkillTargeting
    }

    public static MouseManager Instance { get; private set; }

    public event Action<ISelectable> OnSelectionChanged;
    // Shift(추가 선택 키) + IGroupSelectable 대상 클릭 시 발행(토글). 그룹 선택 집합은 TowerMergeCoordinator가
    // 소유하고, MouseManager는 마커 유무만 알 뿐 대상 타입(타워)은 모른다(입력 단일 창구·제네릭 유지).
    // 이 경로에서는 단일 _selected를 건드리지 않는다.
    public event Action<IGroupSelectable> OnGroupSelectToggled;
    // 평클릭(추가키 없음)·빈 곳 클릭 시 해석된 대상(ISelectable 또는 null)을 **중복 제거 없이 항상** 발행한다.
    // OnSelectionChanged는 _selected 변화만(deduped) 통지하므로, Shift로만 선택한 상태(_selected==null)에서
    // 빈 곳 클릭의 해제 신호가 `Select(null)`의 조기 반환에 삼켜지는 문제가 있었다(WL-085). 그룹 선택
    // 코디네이터는 이 이벤트로 집합을 리셋(타워면 단일화)/해제한다. 단일 선택(_selected) 상태는 안 바꾼다.
    public event Action<ISelectable> OnPrimarySelect;
    // 커서 밑 호버 대상이 바뀔 때만 통지(없으면 null). 툴팁 UI가 구독해 표시/숨김을 결정한다.
    public event Action<IHoverable> OnHoverChanged;

    /// 좌버튼의 눌림 상태가 **바뀔 때만** 통지(true=눌림). 커서 그림처럼 "누르고 있는 동안"에
    /// 반응하는 연출을 위한 것이다 — 클릭 확정(`OnPrimarySelect`)과는 다른 신호다.
    ///
    /// ⚠ **모드·UI를 가리지 않는 순수 입력 통지다.** UI 버튼 위에서 눌러도, 배치 중에 눌러도 나간다.
    ///   "그래서 무엇을 할지"는 구독자가 정한다(원칙 2와 같은 이유로 여기서 걸러 주지 않는다).
    ///
    /// ⚠ 판정은 `wasPressedThisFrame`이 아니라 **`isPressed` 변화**다. 포커스 상실·디바이스 리셋으로
    ///   뗀 프레임이 통째로 유실될 수 있고(BoxSelect가 같은 이유로 `isPressed`를 쓴다 — WL-143),
    ///   그러면 뗌 통지가 새서 구독자가 "눌린 그림"에 영구히 갇힌다.
    public event Action<bool> OnPointerPressedChanged;

    /// 상호작용 모드가 **바뀔 때만** 통지. `SetMode`가 유일한 발행처이므로 전환 경로가 몇 개든
    /// 정확히 1회씩 나간다(그 이음매를 만든 이유가 그것이다 — WL-143 주석 참고).
    ///
    /// 구독 시점의 현재 값은 이벤트로 오지 않는다 → `CurrentMode`를 직접 읽어 초기 상태를 맞출 것.
    public event Action<Mode> OnModeChanged;

    /// 배치·조준 중 커서 밑에 **대상 표면이 있는가**가 바뀔 때만 통지(= 고스트·인디케이터가 지금 그려지는가).
    ///
    /// 판정 자체는 두 모드가 다르다 — 배치는 `_placementMask` 히트, 조준은 그 위의 전투 타일까지다.
    /// 그럼에도 같은 이름으로 나가는 것은 **소비처가 알고 싶은 것이 하나**이기 때문이다:
    /// "지금 커서를 대신할 것이 화면에 있는가". 커서 그림이 이 값으로 「숨김」과 「안 됨」을 가른다
    /// (`CursorFeedback.md` §2 — 타일 밖에서까지 커서를 숨기면 대신할 것 없이 화면이 비어 버린다).
    ///
    /// ⚠ **"놓을 수 있는가"가 아니다.** 점유·도로처럼 `CanPlaceAt`이 거절하는 칸에서도 고스트는 그려지므로
    ///   이 값은 참이다. 유효·무효 구분은 고스트 쪽 몫이다(`UpdatePlacement`의 TODO).
    ///
    /// ⚠ **「대상 표면」을 정하는 것은 매니저가 든 전역 마스크 하나(`_placementMask`)다** — 요청별이 아니다.
    ///   정본 씬의 값은 레이어 3 `Tile` 하나뿐이라 실질적으로 **전투 타일 전용**이고, 경영 지면은 애초에
    ///   콜라이더가 없으며(WL-210) Terrain도 Default 레이어라(WL-047) 이 마스크에 걸리지 않는다.
    ///   지금은 그 우연 덕에 "경영 공간 위 = 「안 됨」"이 공짜로 나오지만, 경영 공간 배치(GDD §5.2 건물
    ///   건설 #27)가 같은 마스크로 열리면 표면 히트가 아예 없어 **배치 내내 이 값이 false**가 되고 커서가
    ///   「안 됨」에 고정된다. 증상은 커서에 뜨는데 원인은 마스크다 — 마스크 소유를 요청별로 옮길지는
    ///   그 착수 PR에서 정한다(WL-215).
    ///
    /// 배치·조준이 아닌 모드에서는 항상 false다 — `SetMode`가 벗어나는 길목에서 내린다.
    /// `OnModeChanged`와 같은 규약으로, 구독 시점의 현재 값은 `IsOverTargetSurface`를 직접 읽는다.
    ///
    /// ⚠ **`PlacementRequest.OnSurfaceHoverChanged`와 같은 사실을 나르지만 역할이 다르다.** 이쪽은 전역
    ///   통지(소비처=커서)고 그쪽은 그 요청의 미리보기 전용이다. 둘 다 `SetPlacementPreviewVisible`
    ///   한 자리에서 나가며 **이 이벤트가 먼저**다 — 그쪽은 "상태가 바뀔 때만"이라 조기 반환에 걸리는데
    ///   이쪽은 그 앞이라 걸리지 않는다(아래 그 메서드의 주석). 순서를 뒤집지 말 것.
    public event Action<bool> OnTargetSurfaceChanged;

    // ── 드래그 사각형 선택(#261) 3단계 통지 ──────────────────────────
    // MouseManager는 사각형에 무엇이 걸렸는지만 알리고 집합은 들지 않는다. 순서 있는 집합의 소유·해석은
    // 도메인 코디네이터(타워=TowerMergeCoordinator)가 계속 담당한다.
    /// 드래그 시작. 인자는 추가 선택(Shift) 여부 — 구독자가 기준 집합을 스냅샷할 시점이다.
    public event Action<bool> OnBoxSelectBegin;
    /// 사각형 내용이 바뀔 때마다 발행. **사각형에 들어온 순서**가 그대로 보존된 목록이다.
    public event Action<IReadOnlyList<IGroupSelectable>> OnBoxSelectUpdate;
    /// 드래그 종료(버튼 뗌). 구독자가 유예했던 갱신을 여기서 한 번 처리한다.
    public event Action OnBoxSelectEnd;

    // ── 유닛 끌기(#341 후속, Resident.md §8) 2단계 통지 ─────────────
    // 사각형 선택과 같은 규약이다 — 매니저는 "무엇을 집었다 / 어디에 놓았다"만 알리고, 들린 대상의 상태·
    // 배치 판정은 도메인 코디네이터(주민 = ResidentDragCoordinator)가 소유한다.
    /// 끌기 시작. 인자는 **누른 순간 커서 밑에 있던** 끌 수 있는 대상이다(§5.4).
    public event Action<IDragHandle> OnUnitDragBegin;
    /// 끌기 종료(버튼 뗌 — §8.4가 정한 **유일한 종료점**). 인자는 놓는 순간 커서 밑에 있던 GameObject(없으면 null).
    ///
    /// 해석하지 않고 그대로 넘긴다. "생산 건물인가"는 매니저가 답할 수 있는 질문이 아니다(원칙 2).
    ///
    /// ⚠ **시작이 있으면 종료는 반드시 온다.** 배치·조준 전환이나 씬 로드로 모드가 끊겨도 `SetMode`가
    ///   `null`을 실어 발행한다 — 안 그러면 구독자가 들고 있던 주민이 영영 화면에 안 돌아온다.
    ///
    /// ⚠ **놓은 자리에 연출을 띄우게 되면 좌표를 여기서 함께 실어 보낼 것.** 대상의 `transform.position`으로
    ///   되짚으면 안 된다 — `CandyLand`의 건물 `Obj_*`는 피벗이 콜라이더에서 24~69유닛 떨어져 있고
    ///   `magic_lab`·`farm`·`castle` **셋은 피벗이 아예 같은 좌표**라 건물 구분조차 안 된다(실제로 밟은
    ///   함정이다 — `Docs/ManagementArea/Resident.md` §11.15). 필요한 값은 `ResolveDropTarget`의 히트 지점이고,
    ///   `PlacementRequest.OnConfirmed(hit, pos)`가 좌표를 함께 넘기는 것과 같은 이유다.
    ///   지금은 쓰는 곳이 없어 싣지 않는다.
    public event Action<GameObject> OnUnitDragEnd;

    /// 드래그 중인 사각형의 스크린 좌표 영역. 사각형 UI가 매 프레임 읽어 간다(IsBoxSelecting이 true일 때만 유효).
    public Rect BoxSelectScreenRect { get; private set; }
    public bool IsBoxSelecting => _mode == Mode.BoxSelect;
    /// 현재 상호작용 모드. `OnModeChanged`를 구독한 쪽이 **초기 상태를 맞출 때** 쓴다
    /// (이벤트는 변화만 싣는다). 읽기 전용 — 전환은 이 클래스의 진입점들을 통한다.
    public Mode CurrentMode => _mode;
    /// 배치 고스트를 들고 있는가. 되돌리기 버튼(#281)이 "먼저 이 고스트부터 치운다"를 판정하는 데 쓴다.
    public bool IsPlacing => _mode == Mode.Placement;
    /// 배치·조준 중 커서가 대상 표면 위인가(`OnTargetSurfaceChanged`의 현재 값). 구독자가 **초기 상태를
    /// 맞출 때** 쓴다 — 이벤트는 변화만 싣는다. 그 외 모드에서는 항상 false.
    ///
    /// ⚠ **범위는 배치·조준 두 모드뿐이다.** 이름이 범용이라 유닛 끌기의 드롭 판정까지 덮는 것처럼 읽히지만
    ///   그렇지 않다 — 세 번째 모드에 주려면 `SetMode`의 내리는 조건부터 함께 늘린다(그쪽 주석).
    public bool IsOverTargetSurface { get; private set; }
    // 현재 포인터 화면 좌표. 다른 시스템(툴팁 등)이 Mouse.current를 직접 읽지 않고 여기서 얻는다(입력 단일 창구 계약).
    public Vector2 PointerPosition { get; private set; }

    /// 입력 판정이 실제로 쓰는 카메라. **`Camera.main`과 같다는 보장이 없다** — `SetCamera`로 갈아 끼울 수
    /// 있고, 지금 둘이 같은 것은 씬 로드마다 `Camera.main`을 넣어 주기 때문이지 계약이라서가 아니다.
    ///
    /// 커서에서 광선을 쏘는 다른 시스템은 이 값을 써야 한다. `Camera.main`을 따로 보면 그 시스템과
    /// 드롭·선택 판정이 **서로 다른 카메라 기준**이 되어, 보고 있던 대상과 판정되는 대상이 갈린다.
    public Camera ActiveCamera => _camera;

    private Mode _mode = Mode.Idle;

    [SerializeField] Camera _camera;

    [Header("Raycast Layers")]
    // 선택 후보 레이어(타워/건물...). 최종 선택 여부는 ISelectable 유무로 판정하므로, 레이어는 굵은 필터일 뿐.
    [SerializeField] LayerMask _selectableMask;
    // 배치 표면 레이어(바닥/그리드). 고스트가 이 위에 올라간다.
    [SerializeField] LayerMask _placementMask;

    private ISelectable _selected;
    private IHoverable _hovered;
    private PlacementRequest _request;
    private SkillTargetRequest _skillRequest;
    private GameObject _ghost;

    // 지금 커서가 배치 표면 위인가(= 고스트와 요청 측 미리보기가 보이는가). 상태가 **바뀔 때만**
    // 요청에 알리려고 기억한다 — 매 프레임 통지하면 요청 측이 멱등성을 지켜야 하는 부담이 생긴다.
    private bool _placementPreviewVisible;

    // ── 좌클릭 제스처 상태 ──────────────────────────────────────────
    // 누른 순간에는 클릭인지 드래그인지 알 수 없으므로(#261), 선택 확정을 **뗄 때**로 미루고
    // 누를 때는 시작점만 기록한다. UI 위에서 시작한 제스처는 아예 채택하지 않는다(_pressActive=false).
    private bool _pressActive;
    // `OnPointerPressedChanged`가 실어 보낸 마지막 값. 제스처 채택 여부(_pressActive)와 **별개**다 —
    // 이쪽은 UI 위·모드와 무관하게 "물리적으로 버튼이 눌려 있는가"만 본다.
    private bool _pointerPressed;
    private Vector2 _pressScreenPos;
    private bool _pressAdditive; // 추가 선택 키(Shift)는 **누른 시점** 상태로 고정 — 도중에 눌렀다 떼도 안 바뀐다
    // 누른 순간 커서 밑에 있던 끌 수 있는 대상(없으면 null). 임계 거리를 넘을 때 사각형/유닛 끌기를 가르는
    // 유일한 기준이다(§5.4). **뗄 때 다시 재지 않는다** — 커서가 그새 다른 것 위로 옮겨 갔을 수 있다.
    private IDragHandle _pressDragHandle;

    // ── 유닛 끌기 상태 ─────────────────────────────────────────────
    private IDragHandle _dragHandle;   // 끌고 있는 대상
    private GameObject _dropTarget;    // 놓는 순간 커서 밑에 있던 것. ExitUnitDrag가 실어 보내고 비운다

    // 드롭 대상 탐색용 버퍼. 끌고 있는 동안 **매 프레임** 도는 경로라(조준 표시) 할당하지 않는다.
    private readonly RaycastHit[] _dropHits = new RaycastHit[32];
    private bool _warnedDropHitsFull;

    // ── 드래그 사각형 선택 상태(#261) ───────────────────────────────
    [Header("Box Select")]
    [Tooltip("이 픽셀 거리를 넘게 움직이면 클릭이 아니라 드래그로 본다")]
    [SerializeField] float _dragThreshold = 8f;

    private Vector2 _boxAnchor;   // 누른 지점(스크린) — 사각형의 고정 모서리
    private bool _boxAdditive;

    // 사각형에 걸린 대상을 **들어온 순서대로** 쌓는다. 빠지면 지우고, 다시 들어오면 맨 뒤로 붙는다.
    // 재판정에 투영 기준점이 필요해 Entry(마커+Transform)째로 들고, 통지용 목록은 변경 시에만 다시 만든다.
    private readonly List<GroupSelectableRegistry.Entry> _boxHits = new();
    private readonly List<IGroupSelectable> _boxTargets = new();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += HandleSceneLoaded;

            // 최초 부트 씬은 sceneLoaded가 이미 지나간 뒤라 한 번 직접 연결한다. 인스펙터에 배선된
            // 카메라는 존중하고, 비어 있을 때만 채워 직접 Play하는 개인/테스트 씬도 살린다.
            if (_camera == null)
            {
                Scene activeScene = SceneManager.GetActiveScene();
                Camera initialCamera = activeScene.name == GameSceneManager.GameSceneName
                    ? FindMainCameraInScene(activeScene) ?? Camera.main
                    : Camera.main;

                SetCamera(initialCamera);
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            // ⚠ 이 오브젝트는 `DontDestroyOnLoad`(Awake `:197`)라 **씬 전환으로는 죽지 않는다.**
            // 그래서 이 해제가 막는 것은 씬 언로드가 아니라 앱 종료·도메인 리로드 비활성 상태의
            // 플레이 중지다. 그때 해제하지 않으면 static Instance가 파괴된 컴포넌트를 계속 가리키고,
            // 소비처의 `Instance?.`(순수 참조 검사)가 그대로 통과해 MissingReferenceException이 난다
            // — #554의 TowerInfoUI와 같은 실패 모드이나, 그쪽은 씬 싱글톤이라 씬 전환에서 났다.
            Instance = null;
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == GameSceneManager.GameSceneName)
        {
            // LoadingScene과 GameScene이 잠시 함께 존재한다. 이때 Camera.main을 쓰면 로딩 카메라가
            // 선택될 수 있으므로, 방금 로드된 GameScene의 계층에서 입력용 카메라를 직접 찾는다.
            SetCamera(FindMainCameraInScene(scene));
        }
        else if (mode == LoadSceneMode.Single)
        {
            // 타이틀/로딩 씬으로 완전히 전환됐으면 파괴 예정인 이전 게임 카메라를 보관하지 않는다.
            ClearCamera();
        }

        // 이전 씬의 선택/호버 대상은 이미 파괴됐을 수 있다. ISelectable/IHoverable은 인터페이스 타입이라
        // Unity의 파괴 감지(오버로드된 ==)가 이 타입으로는 걸리지 않으므로, 알림 호출(OnDeselected 등) 없이
        // 필드만 직접 리셋한다(WL-033) — _selected?.OnDeselected()를 거치면 죽은 참조를 그대로 호출해 터진다.
        _selected = null;
        _hovered = null;
        ResetGesture();
        CancelPlacement();
        CancelSkillTargeting();
    }

    private static Camera FindMainCameraInScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return null;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
            {
                if (camera.isActiveAndEnabled && camera.CompareTag("MainCamera"))
                {
                    return camera;
                }
            }
        }

        return null;
    }

    private void Update()
    {
        var screenPos = Mouse.current.position.ReadValue();
        PointerPosition = screenPos;
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // 모드 분기보다 **먼저** 본다. 어느 모드에 있든 버튼 눌림은 같은 사실이고, 모드 분기 안에
        // 흩어 두면 배치·조준처럼 좌버튼을 다르게 해석하는 경로에서 통지가 빠진다.
        UpdatePointerPressed();

        switch (_mode)
        {
            case Mode.Idle: UpdateIdle(screenPos, overUI); break;
            case Mode.BoxSelect: UpdateBoxSelect(screenPos); break;
            case Mode.UnitDrag: UpdateUnitDrag(screenPos, overUI); break;
            case Mode.Placement: UpdatePlacement(screenPos, overUI); break;
            case Mode.SkillTargeting: UpdateSkillTargeting(screenPos, overUI); break;
        }
    }

    /// 좌버튼 눌림 상태의 변화를 1회씩 통지한다(`OnPointerPressedChanged`).
    ///
    /// `wasPressedThisFrame`/`wasReleasedThisFrame`이 아니라 `isPressed`를 매 프레임 비교하는 이유는
    /// 이벤트 주석에 적어 둔 그대로다 — 뗀 프레임이 유실돼도 다음 프레임에 스스로 복구된다.
    private void UpdatePointerPressed()
    {
        bool pressed = Mouse.current.leftButton.isPressed;
        if (pressed == _pointerPressed) return;

        _pointerPressed = pressed;
        OnPointerPressedChanged?.Invoke(pressed);
    }

    public void SetCamera(Camera cam)
    {
        if (cam == null)
        {
            Debug.LogWarning(
                $"[MouseManager] '{SceneManager.GetActiveScene().name}' 씬에서 " +
                "MainCamera 태그가 붙은 카메라를 찾지 못했습니다.");
        }
        _camera = cam;
    }

    private void ClearCamera()
    {
        _camera = null;
    }

    /// **모드 전환의 유일한 창구.** `_mode`에 직접 대입하지 말 것.
    ///
    /// BoxSelect를 벗어날 때 종료 통지(`OnBoxSelectEnd`)를 반드시 1회 발행하기 위한 이음매다. 예전에는
    /// `ResetGesture`가 그 역할을 했는데, `CancelPlacement`/`CancelSkillTargeting`이 `_mode = Idle`을 **먼저**
    /// 대입해 버려서 뒤따르는 `ResetGesture`의 `_mode == BoxSelect` 검사가 항상 거짓이었다(WL-143).
    /// 그러면 구독자의 유예 플래그가 켜진 채 남아 우측 패널이 영구 정지한다. 전환 지점을 여기 하나로 모으면
    /// 호출 순서와 무관하게 통지가 보장된다 — `PhasePanelSwitcher`처럼 Cancel을 직접 부르는 경로도 포함.
    ///
    /// **유닛 끌기도 같은 이음매를 쓴다.** 그쪽은 통지를 놓쳤을 때의 대가가 더 크다 — 들린 주민은 화면에서
    /// 감춰져 있으므로(Resident.md §8), 종료 통지가 새면 그 주민이 영영 돌아오지 않는다.
    private void SetMode(Mode next)
    {
        if (_mode == Mode.BoxSelect && next != Mode.BoxSelect) ExitBoxSelect();
        if (_mode == Mode.UnitDrag && next != Mode.UnitDrag) ExitUnitDrag();

        // 같은 모드로의 재진입은 통지하지 않는다. `CancelInteractions`처럼 Cancel을 연달아 부르는
        // 경로가 `SetMode(Idle)`을 두 번 때리는데(각 Cancel이 자기 몫으로 부른다), 그대로 흘리면
        // 구독자가 "모드가 바뀌었다"를 실제보다 여러 번 본다. 위 Exit 두 줄은 애초에 모드가
        // 다를 때만 걸리므로 이 조기 반환이 그것들을 건너뛰는 경우는 없다.
        if (_mode == next) return;

        _mode = next;

        // 배치·조준을 벗어나면 고스트·인디케이터가 사라진다 → 「대상 표면 위」도 함께 내린다.
        // 각 Cancel에 흩어 두지 않는 이유는 위 두 Exit와 같다 — 되돌리는 경로가 여럿이라 하나만 빠져도
        // 커서가 숨은 채 남는다. **`OnModeChanged`보다 앞**이라야 구독자가 모드 통지를 받는 시점에
        // 두 값이 어긋나 있지 않다.
        //
        // ⚠ **이 순서의 대가는 알고 고른 것이다.** 여기서 `_mode`는 이미 next인데 구독자가 아는 모드는
        //   아직 이전 것이라, 배치를 끝낼 때 커서가 중간 종류(「안 됨」)로 한 번 갱신됐다가 곧바로 모드
        //   통지로 다시 갱신된다(`Cursor.SetCursor` 1회 추가). **같은 프레임이라 화면에는 드러나지 않고**,
        //   순서를 뒤집으면 구독자가 모드 통지 시점에 어긋난 값을 보는 쪽이 남는다 — 그쪽이 더 비싸다.
        //
        // ⚠ **세 번째 모드에 「대상 표면」을 주려면 이 조건도 함께 늘린다.** 여기를 빼먹고 `CursorController`
        //   쪽에만 갈래를 추가하면 그 모드는 값을 내려 줄 사람이 없어 **모든 프레임이 「안 됨」**이 된다.
        if (next != Mode.Placement && next != Mode.SkillTargeting) SetOverTargetSurface(false);

        OnModeChanged?.Invoke(next);
    }

    /// <summary>
    /// 진행 중인 포인터 상호작용과 선택 상태를 정본 순서로 모두 취소한다.
    /// </summary>
    public void CancelInteractions()
    {
        CancelPlacement();
        CancelSkillTargeting();
        ClearHover();
        ClearSelection();
    }

    // ── 외부 진입점 ────────────────────────────────────────────────
    public void BeginPlacement(PlacementRequest request)
    {
        CancelInteractions();
        ResetGesture();   // 버튼을 누른 채 배치가 시작됐다면 그 제스처는 버린다
        _request = request;
        // 회전은 요청이 주는 그리드 기준축을 따른다 — 회전된 맵에서 고스트가 타일과 각이 맞아야 한다.
        _ghost = Instantiate(request.GhostPrefab, Vector3.zero, request.GhostRotation);

        // ⚠ **비활성으로 생성한다.** 배치 버튼은 UI 위에 있어서 클릭한 프레임의 커서는 타일 위가 아니고,
        //   UpdatePlacement는 레이가 빗나가면 위치를 갱신하지 않는다 — 활성으로 두면 커서가 타일에 닿기
        //   전까지 고스트가 **월드 원점에 그대로 보인다**(1프레임이 아니라 그 사이 내내).
        //   첫 유효 위치를 잡은 뒤에 UpdatePlacement가 켠다.
        _ghost.SetActive(false);

        // 고스트와 같은 이유로 요청 측 미리보기도 숨김에서 시작한다(첫 스냅 전에는 놓을 자리가 없다).
        _placementPreviewVisible = false;

        // ⚠ **여기서 「대상 표면 위」를 미리 참으로 맞추지 말 것.** 커서가 이미 타일 위인 채로 배치가 열리면
        //   (지금은 진입점이 전부 UI 버튼이라 안 걸리지만, 단축키로 열면) 진입 프레임에 「안 됨」이 한 번
        //   스치는 것이 눈에 걸려 그렇게 고치고 싶어진다. 그러면 커서는 숨는데 고스트는 아직 비활성이라
        //   **화면에 아무것도 남지 않는다** — `Hidden`을 처음 켰다가 되돌렸던 바로 그 상태다
        //   (`CursorFeedback.md` 「커서 숨김」).
        //
        //   요청 측 미리보기까지 함께 켜는 것도 답이 아니다: `TowerPlacer`는 사거리 원·셀 하이라이트를
        //   **`BeginPlacement`가 돌아온 뒤에** 만들므로(그쪽 `StartPlacement`의 순서 주석), 여기서
        //   `Snap`·`OnSurfaceHoverChanged`를 부르면 아직 없는 프리뷰에 대고 부르는 셈이다.
        //
        //   진입 프레임에는 정말로 그려지는 것이 없으므로 커서를 **보이게** 두는 지금 동작이 규칙대로다
        //   (「대신할 것이 없으면 커서를 보여준다」). 첫 `UpdatePlacement`가 다음 프레임에 바로잡는다.
        SetMode(Mode.Placement);
    }

    public void CancelPlacement()
    {
        _request?.OnEnded?.Invoke(); // 배치 종료(취소/확정 복귀) 시 요청이 만든 프리뷰 등을 정리할 수 있게 통지
        if (_ghost != null) Destroy(_ghost);
        _ghost = null;
        _request = null;
        SetMode(Mode.Idle);
    }

    // 스킬 타겟팅(#103): 그리드 스냅·점유 검증이 필요 없는 PlacementRequest의 경량 버전.
    // 클릭한 위치를 그대로 SkillTargetRequest.OnConfirmed로 넘긴다(요구사항: SystemMap §4 계약).
    public void BeginSkillTargeting(SkillTargetRequest request)
    {
        if (!TutorialInputGate.Allows(TutorialAction.UseSkill)) return;

        CancelPlacement();
        CancelSkillTargeting();
        ResetGesture();
        ClearHover(); // 타겟팅 중에는 툴팁을 띄우지 않는다
        _skillRequest = request;
        _ghost = Instantiate(request.GhostPrefab);

        // ⚠ **배치와 같은 이유로 비활성에서 시작한다**(`BeginPlacement`의 같은 줄 참고). 스킬 버튼도 UI 위라
        //   클릭한 프레임의 커서는 타일 위가 아니고, `UpdateSkillTargeting`은 타일을 못 맞히면 위치를
        //   갱신하지 않는다 — 프리팹이 활성인 채로 들어오므로(`SkillGhost.prefab`) 그대로 두면 첫 갱신까지
        //   **인디케이터가 월드 원점에 보인다**. 배치 쪽에만 있던 방어라 이쪽은 빠져 있었다.
        //
        //   커서가 걸린 뒤로는 빠져 있으면 안 되는 줄이다: 「대상 표면 위」는 진입 프레임에 false인데
        //   인디케이터는 켜져 있어, "이 값 = 지금 그려지는가"라는 `SetOverTargetSurface`의 전제가
        //   그 한 프레임 깨진다(원점에 인디케이터가 보이는데 커서는 「안 됨」).
        _ghost.SetActive(false);

        SetMode(Mode.SkillTargeting);
    }

    public void CancelSkillTargeting()
    {
        _skillRequest?.OnEnded?.Invoke();
        if (_ghost != null) Destroy(_ghost);
        _ghost = null;
        _skillRequest = null;
        SetMode(Mode.Idle);
    }

    // ── Idle: 선택 (요구사항 ②) ────────────────────────────────────
    private void UpdateIdle(Vector2 screenPos, bool overUI)
    {
        UpdateHover(screenPos, overUI);

        var left = Mouse.current.leftButton;

        // 누를 때: 시작점만 기록하고 확정은 미룬다. 이 시점엔 클릭인지 드래그인지 알 수 없다(#261).
        if (left.wasPressedThisFrame)
        {
            _pressActive = !overUI; // UI 위에서 시작한 제스처는 채택하지 않는다
            _pressScreenPos = screenPos;
            _pressAdditive = IsAdditivePressed();
            _pressDragHandle = ResolveDragHandle(screenPos, overUI);

            // 같은 프레임에 뗌까지 함께 보고되는 경우(저프레임·히칭)를 여기서 소화한다. 그냥 return하면
            // 다음 프레임엔 wasReleasedThisFrame이 false·isPressed도 false라 아래 방어가 제스처를 버려
            // **클릭이 통째로 유실된다**(WL-144). 이동 거리가 0이므로 판정은 자동으로 클릭이다.
            if (!left.wasReleasedThisFrame) return;
        }

        if (!_pressActive) return;

        // 뗄 때 확정 = 클릭.
        if (left.wasReleasedThisFrame)
        {
            _pressActive = false;
            if (!overUI) CommitClick(screenPos, _pressAdditive);
            return;
        }

        // 방어: 다른 모드를 거쳐 돌아오는 등으로 뗀 프레임을 놓쳤으면 제스처를 버린다.
        if (!left.isPressed)
        {
            _pressActive = false;
            return;
        }

        // 임계 거리를 넘으면 드래그로 승격(#261). **여기가 §5.4의 분기 지점이다** — 누를 때 무엇을 잡았는지로
        // 유닛 끌기와 사각형 선택이 배타적으로 갈린다. 판정 근거는 누른 시점에 이미 고정돼 있다.
        if ((screenPos - _pressScreenPos).sqrMagnitude >= _dragThreshold * _dragThreshold)
        {
            if (_pressDragHandle != null) BeginUnitDrag();
            else if (TutorialInputGate.Allows(TutorialAction.SelectResident)
                     || TutorialInputGate.Allows(TutorialAction.SelectPlacedTower))
            {
                BeginBoxSelect(screenPos);
            }
        }
    }

    /// 누른 지점의 끌 수 있는 대상. UI 위에서 시작한 제스처는 애초에 채택하지 않으므로 함께 걸러낸다.
    private IDragHandle ResolveDragHandle(Vector2 screenPos, bool overUI)
    {
        if (!TutorialInputGate.Allows(TutorialAction.DragResident)) return null;
        if (overUI || !RaycastMask(screenPos, _selectableMask, out var hit)) return null;

        hit.collider.TryGetComponent(out IDragHandle handle);
        return handle;
    }

    /// 임계 미만으로 움직인 좌클릭의 확정 처리 — 기존 단일 선택 / Shift 토글 규칙 그대로다.
    /// 확정 시점만 누를 때 → 뗄 때로 옮겼고, 판정 내용은 바뀌지 않았다.
    private void CommitClick(Vector2 screenPos, bool additive)
    {
        bool hitSelectable = RaycastMask(screenPos, _selectableMask, out var hit);

        if (TutorialInputGate.IsRestricted
            && (!hitSelectable || !AllowsTutorialSelection(hit.collider)))
        {
            // 제한 중에는 허용되지 않은 대상을 새로 고르지 않지만, 평범한 빈 공간 클릭의
            // "현재 선택 해제"까지 삼키면 패널·아웃라인이 영구히 남는다. Shift는 추가 선택
            // 제스처이므로 기존 집합을 유지하고, 일반 클릭만 기존 선택을 비운다.
            if (!additive)
            {
                Select(null);
                OnPrimarySelect?.Invoke(null);
            }
            return;
        }

        if (additive)
        {
            // Shift 추가 선택: 그룹 선택 가능(IGroupSelectable 마커) 대상만 토글 통지.
            // 건물·영지 노드·빈 곳 등 마커 없는 대상은 무시(집합 불변 — _selected도 유지).
            if (hitSelectable && hit.collider.TryGetComponent(out IGroupSelectable grp))
            {
                // 토글이 실제로 일어나는 경우에만, 그룹 경로로 넘어가기 전에 단일 선택을 먼저 비운다.
                // 단일 선택의 부수 표시(사거리 원 + 인포 패널)는 대상의 OnDeselected로만 꺼지는데, 이 경로에서
                // _selected를 그대로 두면 아무도 그걸 부르지 않아 **합성 패널 위에 직전 타워의 사거리 원이 잔존**한다
                // (코디네이터 RefreshPanel은 TowerInfoUI만 내릴 수 있고 남의 사거리 원은 모른다 — WL-087 계열).
                // 이후 표시는 집합 크기가 결정한다: 1개면 코디네이터가 그 타워의 OnSelected를 재호출해 복구,
                // 2개 이상이면 합성 패널만. 초록 아웃라인은 GroupSelected 플래그가 이어받으므로 끊기지 않는다.
                // 순서 주의: 토글 뒤에 비우면 count==1로 복귀할 때 코디네이터가 켠 인포·원을 도로 끈다.
                Select(null);
                OnGroupSelectToggled?.Invoke(grp);
            }
            return;
        }

        // 평클릭: 기존 단일 선택(중복 제거 유지 — 기존 구독자용) + 그룹용 OnPrimarySelect를 항상 발행한다.
        // OnPrimarySelect는 _selected 중복 제거와 무관하게 발행되므로, "이미 _selected인 타워 재클릭=단일화"·
        // "빈 곳 클릭=해제"가 항상 코디네이터에 전달된다(WL-085).
        ISelectable picked = null;
        if (hitSelectable) hit.collider.TryGetComponent(out picked);
        Select(picked);
        OnPrimarySelect?.Invoke(picked);
    }

    // 추가 선택 키(Shift) 판정은 입력 단일 창구인 MouseManager가 소유한다(계약 #1).
    private static bool IsAdditivePressed()
    {
        return Keyboard.current != null &&
               (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
    }

    /// 진행 중인 좌클릭 제스처를 버린다. 배치·조준으로 모드가 바뀌거나 씬이 갈릴 때, 누른 상태만 남아
    /// 돌아온 뒤 엉뚱한 클릭으로 확정되는 것을 막는다.
    /// (드래그 중이었다면 모드 이탈은 `SetMode`가 책임진다 — 여기서 중복 처리하지 않는다)
    private void ResetGesture()
    {
        _pressActive = false;
        _pressDragHandle = null;   // 남겨 두면 다음 제스처가 엉뚱하게 유닛 끌기로 갈린다
    }

    // ── UnitDrag: 유닛 끌기 (Resident.md §8) ───────────────────────
    private void BeginUnitDrag()
    {
        SetMode(Mode.UnitDrag);
        _pressActive = false;
        _dragHandle = _pressDragHandle;
        _dropTarget = null;

        // 집어 든 그 대상이 지금 호버 중이다. 곧 화면에서 사라지므로 여기서 내리지 않으면 **노란 테두리가
        // 주인 없이 남고**, 되돌아올 때 `OutlineHighlight.OnEnable`이 그대로 복원한다.
        // 끌고 있는 동안의 호버는 `UpdateUnitDrag`가 다음 프레임부터 드롭 대상 규칙으로 다시 세운다.
        ClearHover();

        // 단일 선택의 부수 표시(사거리 원·인포 패널·초록 아웃라인)는 대상의 OnDeselected로만 꺼진다.
        // 집어 올리는 순간 대상이 화면에서 사라지므로, 여기서 비우지 않으면 그 표시가 주인 없이 남는다
        // — BeginBoxSelect와 같은 이유(WL-087 계열).
        Select(null);

        OnUnitDragBegin?.Invoke(_dragHandle);
    }

    private void UpdateUnitDrag(Vector2 screenPos, bool overUI)
    {
        // **끌고 있는 동안에도 커서 밑 대상을 추적한다.** 안 그러면 어느 건물을 겨누고 있는지 화면에
        // 아무 표시가 없어 조준 자체가 성립하지 않는다(들린 주민도 안 보이므로 단서가 0이 된다).
        //
        // 요점은 **드롭과 같은 규칙으로 찾는 것**이다 — 노란 테두리가 뜬 그 대상이 곧 주민을 받는 대상이라야
        // 표시가 약속이 된다. 평상시 호버(`UpdateHover`)를 그대로 쓰면 앞을 지나가는 주민이 노랗게 떠서
        // "주민에게 주민을 떨어뜨리는" 그림이 되고, 실제 드롭 대상과도 어긋난다.
        //
        // UI 위에서는 대상이 없는 것으로 친다. 패널 뒤의 건물이 레이에 걸리므로, 걸러 내지 않으면
        // **경영 패널 위에서 손을 뗐을 때 안 보이는 건물에 주민이 들어간다.**
        GameObject target = overUI ? null : ResolveDropTarget(screenPos);

        IHoverable hoverable = null;
        if (target != null) target.TryGetComponent(out hoverable);
        SetHover(hoverable);

        // 놓는 순간이 유일한 종료점이다(§8.4). 우클릭·Esc는 끌기를 끊지 않는다 — 우클릭은 그 사이에도
        // 카메라 이동으로 계속 동작하고(CameraController2.MoveDrag), 취소 제스처는 두지 않는다.
        //
        // wasReleasedThisFrame이 아니라 isPressed로 판정하는 이유는 BoxSelect와 같다(WL-143): 포커스 상실 등으로
        // 뗀 프레임을 놓치면 이 모드에 영구 고착되고, 그러면 들린 주민이 화면에 영영 안 돌아온다.
        if (Mouse.current.leftButton.isPressed) return;

        // 방금 위에서 잰 것을 그대로 쓴다 — **보고 있던 대상과 놓이는 대상이 같아야** 한다.
        _dropTarget = target;

        SetMode(Mode.Idle); // 통지는 SetMode → ExitUnitDrag가 맡는다
    }

    /// 놓는 지점의 대상. **끌 수 있는 것(`IDragHandle`)은 건너뛰고 그 뒤를 본다.**
    ///
    /// 끌고 온 것을 또 다른 끌 수 있는 것 위에 놓는 데는 의미가 없는데, `Physics.Raycast`는 가장 가까운
    /// 하나만 주므로 그냥 쓰면 **건물 앞에 선 주민 하나가 레이를 막는다.** 증상이 "가끔 배치가 안 된다"라
    /// 원인을 짚기 어려운 종류다 — 드롭 대상 레이어에 주민과 건물이 함께 올라와 있는 이상 구조적으로 생긴다.
    ///
    /// 끌고 있는 동안 **매 프레임** 도는 경로라(조준 표시가 여기 붙는다) 버퍼를 쓴다. 대신 버퍼가 차면
    /// 경고를 남긴다 — 조용히 잘리면 위와 똑같은 증상으로 돌아온다.
    private GameObject ResolveDropTarget(Vector2 screenPos)
    {
        if (_camera == null) return null;

        int count = Physics.RaycastNonAlloc(
            _camera.ScreenPointToRay(screenPos), _dropHits, Mathf.Infinity, _selectableMask);

        if (count == _dropHits.Length && !_warnedDropHitsFull)
        {
            _warnedDropHitsFull = true;
            Debug.LogWarning($"[MouseManager] 드롭 대상 후보가 버퍼({_dropHits.Length})를 채웠습니다. " +
                             "더 먼 대상이 잘려 드롭이 어긋날 수 있습니다.");
        }

        GameObject nearest = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            if (_dropHits[i].distance >= nearestDistance) continue;
            if (_dropHits[i].collider.TryGetComponent(out IDragHandle _)) continue;

            nearest = _dropHits[i].collider.gameObject;
            nearestDistance = _dropHits[i].distance;
        }

        return nearest;
    }

    /// UnitDrag를 벗어날 때의 정리·통지. **`SetMode`만 호출한다**(직접 부르면 모드가 어긋난다).
    private void ExitUnitDrag()
    {
        GameObject target = _dropTarget;

        _dragHandle = null;
        _dropTarget = null;

        OnUnitDragEnd?.Invoke(target);
    }

    // ── BoxSelect: 드래그 사각형 선택 (#261) ───────────────────────
    private void BeginBoxSelect(Vector2 screenPos)
    {
        SetMode(Mode.BoxSelect);
        _pressActive = false;
        _boxAnchor = _pressScreenPos;   // 앵커는 커서가 아니라 **누른 지점**이다
        _boxAdditive = _pressAdditive;
        _boxHits.Clear();
        _boxTargets.Clear();
        BoxSelectScreenRect = MakeRect(_boxAnchor, screenPos);

        ClearHover(); // 드래그 중에는 툴팁·호버 하이라이트를 띄우지 않는다(배치 모드와 같은 규칙)

        // 단일 선택의 부수 표시(사거리 원 + 인포 패널)는 대상의 OnDeselected로만 꺼진다. 그룹 경로로
        // 넘어가기 전에 비우지 않으면 아무도 그걸 부르지 않아 잔존한다 — Shift 클릭 경로와 같은 이유(WL-087 계열).
        Select(null);

        OnBoxSelectBegin?.Invoke(_boxAdditive);
        // 빈 목록으로 1회 발행 — 비추가 드래그면 이 시점에 기존 집합이 즉시 비워진다.
        OnBoxSelectUpdate?.Invoke(_boxTargets);
    }

    private void UpdateBoxSelect(Vector2 screenPos)
    {
        // 커서가 UI 위를 지나가도 드래그는 계속된다 — 채택 여부는 누른 시점에 이미 결정됐다.
        BoxSelectScreenRect = MakeRect(_boxAnchor, screenPos);

        if (RefreshBoxHits()) OnBoxSelectUpdate?.Invoke(_boxTargets);

        // wasReleasedThisFrame이 아니라 isPressed로 판정한다. 포커스 상실·디바이스 리셋 등으로 뗀 프레임을
        // 놓치면 이 모드에 **영구 고착**되고, 그러면 UpdateIdle이 아예 돌지 않아 모든 클릭·호버가 죽는다.
        // 상태를 보고 나가면 뗀 순간을 놓쳐도 다음 프레임에 반드시 빠져나온다(WL-143).
        if (!Mouse.current.leftButton.isPressed) EndBoxSelect();
    }

    private void EndBoxSelect() => SetMode(Mode.Idle); // 정리·통지는 SetMode → ExitBoxSelect가 맡는다

    /// BoxSelect를 벗어날 때의 정리·통지. **`SetMode`만 호출한다**(직접 부르면 모드가 어긋난다).
    private void ExitBoxSelect()
    {
        BoxSelectScreenRect = default;
        _boxHits.Clear();
        _boxTargets.Clear();
        OnBoxSelectEnd?.Invoke();
    }

    /// 사각형 내용을 갱신한다. 바뀌었으면 true.
    /// 빠진 것을 먼저 지우고 새로 들어온 것을 **끝에 붙여**, 사각형에 들어온 순서가 그대로 집합 순서가 된다.
    private bool RefreshBoxHits()
    {
        var rect = BoxSelectScreenRect;
        bool changed = false;

        for (int i = _boxHits.Count - 1; i >= 0; i--)
        {
            var e = _boxHits[i];
            if (e.Anchor != null && IsInsideBox(e.Anchor.position, rect)) continue;
            _boxHits.RemoveAt(i); // 사각형에서 빠졌거나 그새 파괴됐다
            changed = true;
        }

        var entries = GroupSelectableRegistry.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.Anchor == null) continue;              // 파괴 대기 중인 항목
            if (!IsInsideBox(e.Anchor.position, rect)) continue;
            if (ContainsTarget(e.Target)) continue;      // 이미 담겨 있으면 순서를 유지한다

            _boxHits.Add(e);
            changed = true;
        }

        if (!changed) return false;

        // 통지용 목록은 내용이 바뀔 때만 다시 만든다(드래그 내내 매 프레임 새로 만들지 않는다).
        _boxTargets.Clear();
        for (int i = 0; i < _boxHits.Count; i++) _boxTargets.Add(_boxHits[i].Target);
        return true;
    }

    private bool ContainsTarget(IGroupSelectable target)
    {
        for (int i = 0; i < _boxHits.Count; i++)
        {
            if (ReferenceEquals(_boxHits[i].Target, target)) return true;
        }
        return false;
    }

    /// 월드 좌표를 스크린으로 투영해 사각형 포함 여부를 본다.
    /// 가림(지형·건물 뒤)은 따지지 않는다 — 후보마다 레이캐스트를 쏘지 않기 위한 의도된 단순화(RTS 표준).
    private bool IsInsideBox(Vector3 worldPos, Rect rect)
    {
        if (_camera == null) return false;

        Vector3 sp = _camera.WorldToScreenPoint(worldPos);
        if (sp.z <= 0f) return false; // 카메라 뒤 — 좌표가 뒤집혀 엉뚱하게 걸린다

        return rect.Contains(new Vector2(sp.x, sp.y));
    }

    private static Rect MakeRect(Vector2 a, Vector2 b)
    {
        return Rect.MinMaxRect(
            Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
            Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    // ── Idle: 호버 (툴팁) ─────────────────────────────────────────
    // 커서 밑 IHoverable을 추적해 바뀔 때만 통지. 표시 여부·연출은 구독자(툴팁 UI) 책임.
    // 호버 대상 레이어는 선택 후보와 같다고 보고 _selectableMask를 재사용(최종 판정은 IHoverable 유무).
    private void UpdateHover(Vector2 screenPos, bool overUI)
    {
        IHoverable next = null;
        if (!overUI && RaycastMask(screenPos, _selectableMask, out var hit))
        {
            // 제한 중에는 현재 단계가 선택을 허용한 대상만 호버와 아웃라인을 통과시킨다.
            // 건물 단계에서는 BuildingTooltipSource가 OnHoverChanged로 전달되어 기존 노란 아웃라인이 뜬다.
            if (!TutorialInputGate.IsRestricted || AllowsTutorialSelection(hit.collider))
            {
                hit.collider.TryGetComponent(out next); // 없으면 next는 null
            }
        }

        SetHover(next);
    }

    private static bool AllowsTutorialSelection(Collider collider)
    {
        if (collider == null)
        {
            return false;
        }

        return collider.TryGetComponent(out ITutorialSelectionGate gated)
            && TutorialInputGate.Allows(gated.SelectionAction);
    }

    private void SetHover(IHoverable next)
    {
        if (ReferenceEquals(_hovered, next)) return;
        if (IsAlive(_hovered)) _hovered.OnHoverExit();
        _hovered = next;
        _hovered?.OnHoverEnter();
        OnHoverChanged?.Invoke(_hovered);
    }

    private void ClearHover() => SetHover(null);

    /// 단일 선택 + 그룹 선택을 함께 비우는 **선택 해제의 유일한 창구**. 배치 시작·페이즈 전환이 공유한다.
    /// 두 신호를 함께 보내는 이유는 선택에 딸린 표시가 정보 패널 하나가 아니라 사거리 원·아웃라인·합성 패널까지
    /// 퍼져 있고, 그 소유자도 대상 자신(ISelectable 훅)과 코디네이터(그룹)로 나뉘어 있기 때문이다.
    /// OnPrimarySelect는 중복 제거를 타지 않으므로 Shift로만 선택한 상태(_selected==null)에서도 그룹이 풀린다(WL-085).
    ///
    /// ⚠️ 선택 상태를 "표시만" 내리고 _selected를 남기면 그 대상은 **재클릭해도 다시 뜨지 않는다**
    /// (Select의 `_selected == next` 중복 제거가 삼킨다). 표시를 내려야 하는 곳은 반드시 이 메서드를 쓸 것.
    public void ClearSelection()
    {
        Select(null);
        OnPrimarySelect?.Invoke(null);
    }

    /// 코드에서 대상을 선택한다. 클릭과 같은 경로를 타므로 이전 선택 해제·패널 정리가 그대로 유지된다.
    /// 패널 UI를 직접 켜면 선택 상태가 어긋난다 — 반드시 이 메서드를 쓸 것.
    ///
    /// 클릭 경로는 Idle에서만 돈다(CommitClick ← UpdateIdle). 코드 선택은 그 전제 밖이라
    /// 진행 중이던 배치·조준을 여기서 접는다 — BeginPlacement가 반대 방향으로 하는 것과 같은 규칙이다.
    /// 그래서 _selected는 항상 null에서 시작하고, **같은 대상을 다시 넘겨도 중복 제거에 삼켜지지 않는다.**
    public void SelectExternally(ISelectable target)
    {
        CancelInteractions();

        Select(target);
        OnPrimarySelect?.Invoke(target);
    }

    private void Select(ISelectable next)
    {
        if (_selected == next) return;
        if (IsAlive(_selected)) _selected.OnDeselected();
        _selected = next;
        _selected?.OnSelected();
        OnSelectionChanged?.Invoke(_selected);
    }

    // 이전 대상이 그새 파괴됐을 수 있다(합성 소모·철거·사망). ISelectable/IHoverable은 **인터페이스** 타입이라
    // Unity의 파괴 감지(오버로드된 ==)를 타지 않고, C#의 `?.`는 순수 참조 null 검사라 죽은 대상도 통과시킨다
    // → 그대로 호출하면 대상 내부에서 MissingReferenceException이 난다(합성 후 타워 재선택 시 실제 발생).
    // HandleSceneLoaded가 필드만 리셋하는 것과 같은 계열의 함정(WL-033) — 통지 전에 생존을 확인한다.
    private static bool IsAlive(object target)
    {
        if (target == null) return false;
        if (target is Component component) return component != null; // Unity 오버로드 == 파괴 감지
        return true;
    }

    // ── Placement: 배치 (요구사항 ①) ──────────────────────────────
    private void UpdatePlacement(Vector2 screenPos, bool overUI)
    {
        // 우클릭로 취소
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            CancelPlacement();
            return;
        }

        // 배치 표면을 못 맞히면(UI 위·타일 밖·하늘) 고스트를 숨긴다 — 위치를 갱신하지 않은 채 켜 두면
        // 마지막 유효 위치나 생성 시점의 원점에 남는다. 숨김 판정을 위치 갱신과 같은 자리에 두어
        // "갱신 없이 보이는" 상태가 만들어질 수 없게 한다.
        if (!RaycastMask(screenPos, _placementMask, out var hit))
        {
            // 고스트와 **요청 측 미리보기를 함께** 내린다. 예전에는 고스트만 껐고, 그 결과 풋프린트
            // 하이라이트와 사거리 원이 마지막 타일에 남았다 — 아래 return 때문에 요청 측은 이 프레임에
            // 아무 호출도 받지 못한다(Snap은 표면을 맞혔을 때만 돈다).
            SetPlacementPreviewVisible(false);

            // 맵 밖·하늘 클릭도 거절이다. 여기서 조용히 빠지면 "배치 모드인데 눌러도 아무 반응이 없는"
            // 구간이 화면 대부분을 차지한다 — 무효 타일 클릭과 플레이어가 겪는 일이 같으므로 같이 낸다.
            if (!overUI && Mouse.current.leftButton.wasPressedThisFrame) _request.OnRejected?.Invoke();
            return;
        }

        Vector3 pos = _request.Snap != null ? _request.Snap(hit) : hit.point; // 스냅은 요청이 결정(그리드 스냅)
        _ghost.transform.position = pos;
        _request.OnGhostPositionUpdated?.Invoke(_ghost);

        // 위치를 잡은 뒤에 켠다(원점 노출 방지). 요청 측 미리보기도 같은 이유로 Snap 뒤에 켠다 —
        // Snap이 이미 이번 프레임 위치로 갱신해 두었으므로 한 프레임도 옛 자리에 보이지 않는다.
        SetPlacementPreviewVisible(true);

        bool valid = _request.CanPlaceAt(hit);
        // TODO(하이라이트/연출 미확정): 고스트를 유효=초록/무효=빨강 등으로 표시

        if (!overUI && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (!TutorialInputGate.Allows(TutorialAction.PlaceTower))
            {
                return;
            }

            // UI 위 클릭은 여기 오지 않는다 — 그건 버튼을 누른 것이지 배치를 시도한 것이 아니다(그쪽은 클릭음).
            if (!valid)
            {
                _request.OnRejected?.Invoke();
            }
            else
            {
                _request.OnConfirmed(hit, pos);
                if (!_request.KeepPlacingAfterConfirm) CancelPlacement();
            }
        }
    }

    /// 고스트와 요청 측 미리보기의 표시를 한 번에 맞춘다. **둘을 따로 켜고 끄면 반드시 어긋난다** —
    /// 실제로 고스트만 숨기던 시절 풋프린트 하이라이트와 사거리 원이 마지막 타일에 잔류했다.
    /// 상태가 바뀔 때만 요청에 통지한다.
    private void SetPlacementPreviewVisible(bool visible)
    {
        if (_ghost != null && _ghost.activeSelf != visible)
        {
            _ghost.SetActive(visible);
        }

        // 커서 쪽 통지는 아래 조기 반환보다 **앞**이다. `_placementPreviewVisible`은 `BeginPlacement`가
        // 통지 없이 직접 false로 놓는 자리가 있어(요청이 아직 새것이라 알릴 대상이 없다) 두 값이 어긋날 수
        // 있는데, 그때 이 통지까지 함께 삼켜지면 커서가 이전 세션 상태에 갇힌다.
        SetOverTargetSurface(visible);

        if (_placementPreviewVisible == visible)
        {
            return;
        }

        _placementPreviewVisible = visible;
        _request?.OnSurfaceHoverChanged?.Invoke(visible);
    }

    /// 조준 인디케이터의 표시를 맞춘다 — 배치 쪽 `SetPlacementPreviewVisible`과 짝이며, 둘 다
    /// 「대상 표면 위인가」를 같은 창구로 흘려보낸다. 조준에는 요청 측 미리보기가 없어 이쪽이 더 짧다.
    private void SetSkillIndicatorVisible(bool visible)
    {
        if (_ghost != null && _ghost.activeSelf != visible)
        {
            _ghost.SetActive(visible);
        }

        SetOverTargetSurface(visible);
    }

    /// 「대상 표면 위인가」의 **유일한 갱신 창구**. 바뀔 때만 통지한다.
    /// 배치와 조준이 각자 통지하면 모드를 벗어날 때 내리는 것을 한쪽이 잊는다(그 대가는 조준을 끝냈는데
    /// 커서가 숨은 채 남는 것이다) — 그래서 `SetMode`가 같은 창구로 함께 내린다.
    ///
    /// **지키는 불변식은 하나다: 이 값 == 「고스트가 지금 켜져 있는가」.** 커서를 숨겨도 되는 근거가 전부
    /// 거기서 나오므로, 이 값을 고스트와 따로 움직이는 코드를 만들지 말 것 — 위 두 `Set*Visible`이
    /// `SetActive`와 이 통지를 **한 자리에서 함께** 하는 이유이고, 두 `Begin*`이 고스트를 비활성으로
    /// 만들어 두는 이유이기도 하다(진입 프레임에도 둘이 같은 말을 하도록).
    private void SetOverTargetSurface(bool over)
    {
        if (IsOverTargetSurface == over) return;

        IsOverTargetSurface = over;
        OnTargetSurfaceChanged?.Invoke(over);
    }

    // ── SkillTargeting: 스킬 범위 지정 (요구사항 ③, #103) ─────────
    // 전투 타일 위이기만 하면(종류 무관) 시전 가능하다. 고스트는 전투 타일 위에서만 표시하고,
    // 타일 밖(빈 칸·틈·맵 밖)에서는 숨긴다. 고스트를 실제 히트 표면(hit)에 붙이므로
    // 도로처럼 낮게 모델링된 타일 위에서도 표면에 자연스럽게 앉는다.
    private void UpdateSkillTargeting(Vector2 screenPos, bool overUI)
    {
        // 우클릭 로 취소
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            CancelSkillTargeting();
            return;
        }

        CombatMapTileView tile = null;
        if (RaycastMask(screenPos, _placementMask, out var hit))
            tile = hit.collider.GetComponentInParent<CombatMapTileView>();

        // 전투 타일이 아니면(빈 칸·타일 사이 틈·맵 밖) 인디케이터를 숨긴다.
        if (tile == null)
        {
            SetSkillIndicatorVisible(false);
            return;
        }

        SetSkillIndicatorVisible(true);
        Ray ray = _camera.ScreenPointToRay(screenPos);
        Vector3 pos = _skillRequest.Snap != null ? _skillRequest.Snap(ray, hit) : hit.point;
        _ghost.transform.position = pos;

        // 전투 타일 위이면 시전한다. 타일 밖은 위에서 이미 고스트를 숨기고 return 했다.
        if (!overUI && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (!TutorialInputGate.Allows(TutorialAction.UseSkill))
            {
                CancelSkillTargeting();
                return;
            }

            _skillRequest.OnConfirmed(pos);
            CancelSkillTargeting(); // 한 번 시전하면 조준 모드 종료(연속 시전 불필요)
        }
    }

    // ── 레이캐스트 ────────────────────────────────────────────────
    // 그리드 스냅은 각 배치물이 PlacementRequest.Snap으로 제공한다(매니저는 배치 규칙을 모른다).
    private bool RaycastMask(Vector2 screenPos, LayerMask mask, out RaycastHit hit)
    {
        hit = default;
        if (_camera == null)
        {
            return false;
        }

        var ray = _camera.ScreenPointToRay(screenPos);
        return Physics.Raycast(ray, out hit, Mathf.Infinity, mask);
    }
}
