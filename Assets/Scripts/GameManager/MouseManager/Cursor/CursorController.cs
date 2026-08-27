using System.Collections.Generic;
using NorthLand.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 상호작용 상태에 따라 마우스 커서 그림을 갈아 끼우는 <b>유일한 주체</b>.
///
/// <see cref="OutlineInteractionDriver"/>와 같은 계보다 — <c>MouseManager</c>는 "무엇을 호버했는지 /
/// 버튼이 눌렸는지 / 어떤 모드인지"만 통지하고, 그것을 그림으로 바꾸는 일은 전부 여기서 한다
/// (<c>Docs/Core/MouseManager.md</c> §1 원칙 2·3 — 매니저는 도메인도 연출도 소유하지 않는다).
/// 대상별 커서(건물=문, 타워=돋보기, 주민=손)는 대상이 <see cref="ICursorHint"/>로 스스로 답하므로
/// 이 클래스도 그 타입들을 모른다.
///
/// 씬 파일을 건드리지 않도록 런타임에 스스로 부팅한다(<c>Docs/Core/SceneWorkflow.md</c>의 정본 씬
/// 병합 규칙과 충돌 방지 — <see cref="OutlineInteractionDriver"/>가 같은 이유로 같은 방식을 쓴다).
/// 그래서 텍스처는 인스펙터가 아니라 Resources의 <see cref="CursorSet"/> SO 하나가 들고 있다
/// (<c>SfxBank</c> 선례).
///
/// 기본은 <b>하드웨어 커서</b>다(<c>Cursor.SetCursor</c> + <c>CursorMode.Auto</c>) — OS가 그리므로
/// 프레임 드랍과 무관하게 지연이 없다. 그리는 주체는 뱅크의 <see cref="CursorSet.CursorMode"/>로
/// 바꿀 수 있고, <b>커서 그림의 크기 제한이 거기서 갈린다</b>(그쪽 주석 참고).
///
/// 어느 모드든 <b>프레임 애니메이션도, 커서에 붙는 파티클도 불가능하다</b> — 클릭 연출을 그림 밖으로
/// 확장하려면 별도 이펙트 시스템이 필요하다(이번 범위 밖).
/// </summary>
[DisallowMultipleComponent]
public class CursorController : MonoBehaviour
{
    // Resources 루트 기준 경로. 에셋을 옮기면 여기도 함께 고친다(다른 Resources 소비처와 같은 규약).
    private const string k_SetPath = "ScriptableObjects/CursorSet";

    private static CursorController s_instance;

    private CursorSet _set;
    private bool _setLoadAttempted;

    // ── 상태 입력 3종 ──────────────────────────────────────────────
    // 이 셋을 합쳐 한 종류로 접는다(Resolve). 각각 다른 통지로 들어오므로 따로 들고 있어야 한다.
    private CursorKind _hoverKind = CursorKind.Default;
    private bool _pressed;
    private MouseManager.Mode _mode = MouseManager.Mode.Idle;

    // 커서가 UI 위인가. **커서 그림은 이것으로 바뀌지 않는다** — UI 위라고 포인터가 달라지면 오히려
    // 헷갈린다는 판단이다. 오직 아래 「숨김」의 예외 하나에만 쓴다.
    //
    // **이것만 통지가 아니라 폴링이다** — MouseManager는 이 값을 이벤트로 내보내지 않고(내부 판정용으로만
    // 쓴다), 커서 밑 UI는 마우스를 움직이지 않아도 패널이 열리고 닫히면서 바뀐다.
    // EventSystem 호출 1회/프레임이라 비용은 무시할 수준이다.
    private bool _overUI;

    // 커서가 **누를 수 있는** UI(버튼·토글 등) 위인가. _overUI와 달리 이쪽은 그림을 바꾼다 —
    // 반응하는 UI에서만 커서가 달라지는 것이 곧 "여기는 누를 수 있다"는 신호이기 때문이다.
    private bool _overUIButton;

    // 우버튼을 쥐고 화면을 끌어 옮기는 중인가. 카메라가 소유한 상태를 그대로 읽는다 —
    // 우버튼을 직접 폴링하면 "배치 취소용 우클릭"까지 팬으로 오인한다.
    private bool _cameraPanning;

    // 카메라 팬 상태의 출처. 싱글톤이 아니라 씬마다 찾아 캐시한다(WL-002 — 싱글톤을 늘리지 않는다).
    private CameraController2 _camera;

    // 현재 종류가 "커서를 숨긴다"로 선언돼 있는가(뱅크의 Entry.Hidden — 배치·조준처럼 고스트가 커서를
    // 대신하는 상태). 실제 숨김은 UI 위인지에 따라 갈리므로 선언과 적용을 따로 든다.
    private bool _kindWantsHidden;

    // UI 레이캐스트용 재사용 버퍼. 커서가 UI 위일 때만 도는 경로지만, 매 프레임이라 할당하지 않는다.
    private static readonly List<RaycastResult> s_uiHits = new();
    private PointerEventData _uiPointer;
    private EventSystem _uiPointerOwner;

    // 마지막으로 실제 적용한 그림(종류 + 눌림 여부). **바뀔 때만** SetCursor를 부르기 위한 것이다 —
    // 하드웨어 커서 교체는 OS 호출이라 매 프레임 때리면 플랫폼에 따라 깜빡이거나 눈에 띄는 비용이 된다.
    // 어떤 종류와도 같지 않은 값에서 시작해 첫 적용이 반드시 한 번 나가게 한다.
    private CursorKind _appliedKind = (CursorKind)(-1);
    private bool _appliedPressed;

    // 지금 커서를 보이게 두고 있는가. Cursor.visible을 중복 대입하지 않으려고 기억한다.
    private bool _cursorVisible = true;

    private bool _subscribed;
    private bool _warnedNoMouseManager;

    // CPU 접근 불가 텍스처를 이미 경고한 대상. 상태가 오갈 때마다 같은 줄로 콘솔을 덮지 않으려고 기억한다.
    private readonly HashSet<Texture2D> _warnedTextures = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_instance != null) return;

        var go = new GameObject(nameof(CursorController));
        s_instance = go.AddComponent<CursorController>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Destroy(this);
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
            mm.OnPointerPressedChanged -= HandlePressedChanged;
            mm.OnModeChanged -= HandleModeChanged;
        }

        if (s_instance == this)
        {
            s_instance = null;

            // 커서 그림과 표시 여부는 씬·플레이 세션과 무관하게 남는다. 에디터에서 플레이를 멈췄는데
            // 커스텀 커서가 그대로거나 **아예 안 보이면** "왜 안 돌아오지"로 시간을 버린다.
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            Cursor.visible = true;
        }
    }

    private void LateUpdate()
    {
        // MouseManager가 뒤늦게(다른 씬에서) 등장할 수 있어 붙을 때까지 확인한다 — null 체크 1회라 비용은 없다.
        if (!_subscribed) TrySubscribe();

        // 아래 셋은 통지가 없어 매 프레임 직접 본다(각 필드 주석 참고).
        bool overUI = IsPointerOverUI();
        bool overUIButton = overUI && IsPointerOverUIButton(); // UI 밖이면 레이캐스트조차 하지 않는다
        bool panning = IsCameraPanning();

        if (overUI == _overUI && overUIButton == _overUIButton && panning == _cameraPanning) return;

        bool visibilityOnly = overUIButton == _overUIButton && panning == _cameraPanning;
        _overUI = overUI;
        _overUIButton = overUIButton;
        _cameraPanning = panning;

        // _overUI만 바뀌었다면 종류는 그대로다 — 그림을 다시 세팅할 필요 없이 숨김만 갱신한다.
        if (visibilityOnly) UpdateVisibility();
        else Apply();
    }

    /// <summary>
    /// 커서가 <b>누를 수 있는</b> UI 위인가. 맨 앞에 잡히는 UI만 본다 — 실제로 클릭될 대상이 그것이고,
    /// 뒤에 가려진 버튼까지 세면 반응하지 않는 곳에서 커서가 바뀐다.
    ///
    /// <see cref="Selectable"/>을 기준으로 삼으므로 버튼뿐 아니라 토글·슬라이더·입력칸도 포함되고,
    /// <c>interactable</c>이 꺼진 것은 제외된다(누를 수 없는데 누를 수 있다고 알리지 않는다).
    ///
    /// ⚠ 이 판정만 <b>새 레이캐스트</b>다(<c>IsPointerOverGameObject</c>는 입력 모듈이 캐시한 결과를 읽는다).
    /// 그래서 호출부가 "UI 위일 때만" 부르도록 게이트를 건다.
    /// </summary>
    private bool IsPointerOverUIButton()
    {
        var es = EventSystem.current;
        if (es == null) return false;

        if (_uiPointer == null || _uiPointerOwner != es)
        {
            _uiPointer = new PointerEventData(es);
            _uiPointerOwner = es;
        }

        var mm = MouseManager.Instance;
        if (mm == null) return false;
        _uiPointer.position = mm.PointerPosition;

        s_uiHits.Clear();
        es.RaycastAll(_uiPointer, s_uiHits);
        if (s_uiHits.Count == 0) return false;

        // 그래픽은 버튼 자신일 수도, 자식 라벨·아이콘일 수도 있다 → 부모까지 훑는다.
        var selectable = s_uiHits[0].gameObject.GetComponentInParent<Selectable>();
        return selectable != null && selectable.IsInteractable();
    }

    /// 우드래그 팬 여부는 카메라가 소유한 상태를 그대로 읽는다. 우버튼을 직접 폴링하면 배치·조준을
    /// 취소하는 우클릭까지 팬으로 오인하고, "UI 위에서 시작한 우클릭은 팬이 아니다"라는 카메라 쪽
    /// 규칙도 여기서 다시 구현해야 한다.
    private bool IsCameraPanning()
    {
        if (_camera == null) _camera = FindFirstObjectByType<CameraController2>();
        return _camera != null && _camera.IsDragging;
    }

    /// <summary>
    /// 커서 표시/숨김을 <c>Cursor.visible</c>에 반영한다. 실제로 바뀔 때만 대입한다.
    ///
    /// ⚠ <b>숨기는 상태여도 UI 위에서는 되살린다.</b> 배치·조준 중에도 취소 버튼이나 패널을 눌러야
    /// 하는데, 그 위에서 커서가 없으면 겨냥할 수가 없다. 고스트는 UI 위에서 어차피 표시되지 않으므로
    /// (<c>MouseManager.UpdatePlacement</c>가 <c>overUI</c>면 갱신을 멈춘다) 커서를 대신할 것도 없다.
    ///
    /// ⚠ <c>Cursor.visible</c>은 전역 값이다. 다른 시스템이 이 값을 건드리기 시작하면 마지막에 쓴 쪽이
    /// 이긴다 — 커서 표시/숨김은 이 클래스가 단독으로 소유한다는 전제다.
    /// </summary>
    private void UpdateVisibility()
    {
        bool visible = !_kindWantsHidden || _overUI;
        if (visible == _cursorVisible) return;

        _cursorVisible = visible;
        Cursor.visible = visible;
    }

    private static bool IsPointerOverUI()
        => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    private void TrySubscribe()
    {
        if (_subscribed) return;

        var mm = MouseManager.Instance;
        if (mm == null)
        {
            // 타이틀·로딩 씬에는 MouseManager가 없는 것이 정상이다(아웃라인 드라이버와 같은 판단).
            if (GameSceneManager.IsGameplayScene && !_warnedNoMouseManager)
            {
                _warnedNoMouseManager = true;
                Debug.LogWarning("[커서] MouseManager가 아직 없어 상태별 커서가 대기 중입니다.");
            }

            return;
        }

        mm.OnHoverChanged += HandleHoverChanged;
        mm.OnPointerPressedChanged += HandlePressedChanged;
        mm.OnModeChanged += HandleModeChanged;
        _subscribed = true;

        // 이벤트는 **변화가 있을 때만** 오므로, 구독 시점의 현재 상태는 직접 읽어 와야 한다.
        // (배치 중에 씬이 로드되는 등으로 구독이 늦어지면 첫 통지가 올 때까지 커서가 어긋난 채 남는다)
        _mode = mm.CurrentMode;
        Apply();
    }

    // 씬이 바뀌면 이전 호버 대상은 이미 파괴됐다. MouseManager도 같은 시점에 알림 없이 필드만
    // 리셋하므로(WL-033) 해제 통지가 오지 않는다 → 이쪽 상태도 직접 되돌린다.
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _hoverKind = CursorKind.Default;
        _pressed = false;
        _overUI = false;
        _overUIButton = false;
        _cameraPanning = false;
        _camera = null;        // 카메라는 씬마다 다른 인스턴스다 — 이전 씬 것을 들고 있으면 안 된다
        _uiPointer = null;     // EventSystem도 마찬가지
        _uiPointerOwner = null;
        _mode = MouseManager.Instance != null ? MouseManager.Instance.CurrentMode : MouseManager.Mode.Idle;

        // 경고 latch는 **씬 단위**로 푼다(아웃라인 드라이버와 같은 이유 — 로딩 씬 한 번으로 소진되면
        // 정작 게임 씬에서 진짜 누락됐을 때 아무 신호도 남지 않는다).
        _warnedNoMouseManager = false;

        Apply();
    }

    // ── 통지 수신 ──────────────────────────────────────────────────

    private void HandleHoverChanged(IHoverable hoverable)
    {
        _hoverKind = ResolveHoverKind(hoverable);
        Apply();
    }

    private void HandlePressedChanged(bool pressed)
    {
        _pressed = pressed;
        Apply();
    }

    private void HandleModeChanged(MouseManager.Mode mode)
    {
        _mode = mode;
        Apply();
    }

    /// <summary>
    /// 호버 대상이 지정한 커서. 지정이 없으면(= <see cref="ICursorHint"/> 미구현) 기본 커서다.
    ///
    /// 대상 GameObject에서 찾는 이유는 <see cref="OutlineInteractionDriver"/>가
    /// <see cref="IOutlineKindFilter"/>를 찾는 방식과 같다 — 대개 <see cref="IHoverable"/>과 같은
    /// 컴포넌트지만, 굳이 그 사실에 기대지 않아도 되는 자리다.
    /// </summary>
    private static CursorKind ResolveHoverKind(IHoverable hoverable)
    {
        var component = hoverable as Component;
        if (component == null) return CursorKind.Default; // 해제(null) 또는 MonoBehaviour가 아닌 구현

        return component.gameObject.TryGetComponent(out ICursorHint hint)
            ? hint.HoverCursor
            : CursorKind.Default;
    }

    // ── 상태 → 그림 ────────────────────────────────────────────────

    /// <summary>
    /// 겹치는 상태를 하나로 접는다. 우선순위는
    /// <c>카메라 팬 &gt; 누를 수 있는 UI &gt; 배치·조준 &gt; 유닛 끌기 &gt; 호버 &gt; 기본</c>.
    ///
    /// <b>팬이 최우선인 이유</b>: 화면을 끌고 있는 동안에는 커서 밑에 무엇이 있든 지금 하는 일은 팬이다.
    /// 끌다가 커서가 UI나 건물 위를 지나갈 때 그림이 바뀌면 손을 놓친 것처럼 보인다.
    ///
    /// <b>UI 버튼이 그다음인 이유</b>: 배치 중이라도 커서가 버튼 위면 겨냥하는 것은 그 버튼이다.
    /// 덕분에 「커서 숨김」이 버튼을 가리는 일도 없다 — 숨김은 <c>Placing</c> 칸의 속성인데
    /// 버튼 위에서는 종류가 아예 <c>UIButton</c>으로 바뀐다.
    ///
    /// 모드가 호버를 이기는 이유: 고스트를 들고 있거나 조준 중일 때는 "지금 무엇을 하는 중인가"가
    /// "무엇을 가리키고 있나"보다 중요하고, 애초에 그 모드들에서는 MouseManager가 호버 추적을 끈다.
    ///
    /// ⚠ <b>눌림은 이 경쟁에 참여하지 않는다.</b> 종류가 아니라 각 종류의 변형이기 때문이다
    /// (<c>CursorKind</c> 주석) — 건물 위에서 누르면 "눌림 커서"가 아니라 <b>열린 문</b>이 나와야 한다.
    /// 그래서 눌림 여부는 <see cref="Apply"/>가 종류와 <b>함께</b> 뱅크에 넘긴다.
    /// <c>BoxSelect</c>에 별도 그림이 없는 것도 같은 이유다 — 빈 땅(<c>Default</c>)을 쥐고 있는 상태이므로
    /// 기본 칸의 눌림 변형으로 자연히 떨어진다.
    /// </summary>
    private CursorKind Resolve()
    {
        if (_cameraPanning) return CursorKind.CameraPan;

        // ⚠ 여기서 보는 것은 _overUIButton이지 _overUI가 아니다. 반응하지 않는 UI(패널 배경·라벨)에서는
        //   그림이 바뀌지 않는다 — "UI에 올라가면 무조건 바뀜"은 넣었다가 걷어낸 버전이고, 바뀌는 것
        //   자체가 "여기는 누를 수 있다"는 신호여야 한다. _overUI는 숨김 예외에만 쓴다.
        if (_overUIButton) return CursorKind.UIButton;

        switch (_mode)
        {
            case MouseManager.Mode.Placement: return CursorKind.Placing;
            case MouseManager.Mode.SkillTargeting: return CursorKind.SkillAiming;
            case MouseManager.Mode.UnitDrag: return CursorKind.ResidentDrag;
        }

        return _hoverKind;
    }

    private void Apply()
    {
        CursorKind kind = Resolve();
        if (kind == _appliedKind && _pressed == _appliedPressed) return;
        _appliedKind = kind;
        _appliedPressed = _pressed;

        CursorSet set = Set;

        // 뱅크가 없는 것은 오류가 아니라 "아직 아트가 없음"이다 — 조용히 OS 기본 커서를 쓴다.
        if (set == null)
        {
            _kindWantsHidden = false;
            UpdateVisibility();
            return;
        }

        _kindWantsHidden = set.IsHidden(kind);
        UpdateVisibility();

        // ⚠ **숨기는 종류여도 그림은 계속 세팅한다.** 커서가 UI 위로 올라가 되살아날 때(UpdateVisibility의
        //   예외) 직전 상태의 그림이 그대로 남아 있으면 배치 중인데 문·돋보기 커서가 뜬다.
        //   숨김 칸은 그림이 비어 있으므로 아래 폴백을 타고 Default가 세팅된다.
        //
        // 칸이 비면 기본 칸으로, 기본 칸도 비면 OS 기본 커서로 내려간다.
        // (한 칸 안에서 눌림 → 평상시로 내려가는 폴백은 뱅크가 처리한다)
        if (!set.TryGet(kind, _pressed, out Texture2D texture, out Vector2 hotspot) &&
            !set.TryGet(CursorKind.Default, _pressed, out texture, out hotspot))
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            return;
        }

        if (!IsUsable(texture))
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            return;
        }

        // 그리는 주체(하드웨어/소프트웨어)는 뱅크가 정한다 — 크기 제한이 거기서 갈리므로
        // 아트 규격과 함께 봐야 하는 값이다(CursorSet.CursorMode 주석).
        Cursor.SetCursor(texture, hotspot, set.CursorMode);
    }

    /// <summary>
    /// 하드웨어 커서는 픽셀을 CPU에서 읽어 OS에 넘긴다. 압축돼 있거나 Read/Write가 꺼진 텍스처를 넘기면
    /// Unity가 <c>"not CPU accessible"</c> 에러를 뱉고 기본 화살표로 폴백하는데, 그 메시지만 보고
    /// 임포트 설정을 떠올리기는 어렵다 → 고칠 방법까지 적어 미리 걸러 준다.
    /// </summary>
    private bool IsUsable(Texture2D texture)
    {
        if (texture == null) return false;
        if (texture.isReadable) return true;

        if (_warnedTextures.Add(texture))
        {
            Debug.LogWarning(
                $"[커서] '{texture.name}'({texture.format})는 CPU에서 읽을 수 없어 커서로 쓸 수 없습니다. " +
                "임포트 설정에서 Texture Type을 Cursor로 바꾸세요(비압축 RGBA32 + Read/Write가 함께 켜집니다).",
                texture);
        }

        return false;
    }

    private CursorSet Set
    {
        get
        {
            // ⚠ `_set != null`을 조건에 더하지 말 것. 에셋이 없는 프로젝트에서는 영영 null이라
            //   상태가 바뀔 때마다 Resources.Load를 반복하게 된다(`Sfx.Bank`와 같은 관용구).
            if (_setLoadAttempted) return _set;

            _setLoadAttempted = true;
            _set = Resources.Load<CursorSet>(k_SetPath);
            return _set;
        }
    }
}
