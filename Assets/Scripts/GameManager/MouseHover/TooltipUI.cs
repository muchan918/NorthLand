using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// 커서를 따라다니는 범용 호버 툴팁 뷰. 표시할 내용(TooltipContent)만 받아 그리는 얇은 뷰이며,
/// 게임 로직은 갖지 않는다. MouseManager.OnHoverChanged를 구독해 대상이 생기면 표시, 사라지면 숨긴다.
///
/// (임시 싱글톤) TowerInfoUI/BuildingInfoUI와 같은 계보 — 나중에 UIManager가 도입되면
/// 이 뷰를 그 아래로 옮기고 접근점(Instance)만 UIManager 경유로 교체하면 된다.
/// 그때 마이그레이션이 쉽도록 책임을 "표시 + 커서 추적"으로만 최소화해 둔다.
/// (주의) 루트 오브젝트는 씬에서 '활성'으로 둬야 Awake가 실행되어 Instance가 등록된다.
///        숨김은 자식 _panel 토글로 처리하므로 루트를 미리 꺼두지 말 것.
public class TooltipUI : MonoBehaviour
{
    public static TooltipUI Instance { get; private set; }

    [Header("표시 대상")]
    [SerializeField] GameObject _panel;        // 보임/숨김 토글 대상 (루트가 아니라 이 자식)
    [SerializeField] RectTransform _panelRect; // 커서를 따라 이동시킬 RectTransform (pivot 좌상단 권장)
    [SerializeField] Canvas _canvas;           // 스케일 팩터 계산용 (Screen Space - Overlay)

    [Header("내용 슬롯")]
    [SerializeField] Image _headerBackground;
    [SerializeField] TextMeshProUGUI _headerText;
    [SerializeField] Image _bodyBackground;
    [SerializeField] TextMeshProUGUI _bodyText;

    [Header("배치")]
    [SerializeField] Vector2 _cursorOffset = new(16f, -16f);

    private MouseManager _mouse;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (_panel == null || _panelRect == null || _headerText == null || _bodyText == null)
        {
            Debug.LogError("TooltipUI: 필수 슬롯(_panel/_panelRect/_headerText/_bodyText) 미할당", this);
            enabled = false; // Start의 OnHoverChanged 구독도 건너뛰어 Show 경로 자체가 막힘
            return; // Hide() 호출 전에 빠져 첫 프레임 NRE 방지
        }
        Hide();
    }

    private void Start()
    {
        // MouseManager.Awake는 모든 Start 이전에 끝나므로 여기서 Instance가 준비돼 있다.
        _mouse = MouseManager.Instance;
        if (_mouse == null)
        {
            Debug.LogWarning("TooltipUI: MouseManager가 씬에 없어 호버 툴팁이 동작하지 않는다.", this);
            return;
        }
        _mouse.OnHoverChanged += OnHoverChanged;
        _mouse.OnSelectionChanged += OnSelectionChanged;
    }

    private void OnDestroy()
    {
        if (_mouse != null)
        {
            _mouse.OnHoverChanged -= OnHoverChanged;
            _mouse.OnSelectionChanged -= OnSelectionChanged;
        }
        if (Instance == this) Instance = null;
    }

    private void OnHoverChanged(IHoverable hoverable)
    {
        if (hoverable == null) Hide();
        else Show(hoverable.GetTooltipContent());
    }

    // 클릭으로 뭔가 선택되면(호버 대상이 여전히 같은 오브젝트라도) 툴팁을 접고 상세 패널에 자리를
    // 내준다 — 건물 Hover 툴팁→Click 패널 전환(이슈 #67)에 필요.
    private void OnSelectionChanged(ISelectable selected)
    {
        if (selected != null) Hide();
    }

    public void Show(TooltipContent content)
    {
        _headerText.text = content.Header;
        _bodyText.text = content.Body;
        if (_headerBackground != null) _headerBackground.color = content.HeaderColor;
        if (_bodyBackground != null) _bodyBackground.color = content.BackgroundColor;
        _panel.SetActive(true);
        FollowCursor(); // 표시되는 첫 프레임부터 커서에 붙어 나오도록
    }

    public void Hide()
    {
        _panel.SetActive(false);
    }

    private void LateUpdate()
    {
        if (_panel.activeSelf) FollowCursor();
    }

    // 커서 위치(MouseManager 경유 — Mouse.current 직접 폴링 금지, 입력 단일 창구 계약)를 따라가되
    // 패널이 화면 밖으로 나가지 않게 clamp. _panelRect pivot이 좌상단(0,1)이라고 가정한다.
    private void FollowCursor()
    {
        if (_mouse == null) return;

        Vector2 pos = _mouse.PointerPosition + _cursorOffset;
        float scale = _canvas != null ? _canvas.scaleFactor : 1f;
        Vector2 size = _panelRect.rect.size * scale;

        pos.x = Mathf.Clamp(pos.x, 0f, Mathf.Max(0f, Screen.width - size.x));
        pos.y = Mathf.Clamp(pos.y, size.y, Screen.height);
        _panelRect.position = pos;
    }
}
