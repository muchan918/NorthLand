using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 낮 종료(낮→밤) 확인 팝업(#219). 강제 게이팅을 해제한 대신, [다음(낮 종료)] 클릭 시
/// 낮 프로세스 조건(① 오늘 영토 미확장, ② 유휴 주민 존재)이 하나라도 미충족이면 이 팝업으로 안내한다.
/// <list type="bullet">
/// <item>[계속] → 조건 미충족이어도 밤으로 진행(<see cref="ManagementController.EndDay"/>).</item>
/// <item>[취소] → 팝업만 닫고 낮 유지.</item>
/// <item>두 조건 모두 충족이면 팝업 없이 바로 밤으로 진행.</item>
/// </list>
/// 표시 전용 씬 스코프 싱글톤 — <see cref="NorthLand.UI.ResultUIManager"/>와 동일한 패턴
/// (패널 참조 + SetActive + 버튼 onClick). "언제 넘어갈지"의 판정 데이터는 컨트롤러가 소유하고,
/// 이 컴포넌트는 그 데이터를 읽어 팝업을 띄우거나 곧장 종료할 뿐이다.
/// </summary>
public class ManagementEndDayConfirmPopup : MonoBehaviour
{
    // 로컬라이제이션 키(#219) — 값은 NorthLand_default String Table(ko/ja/en)에 있다.
    private const string k_KeyProceed = "game.btn.proceed";
    private const string k_KeyCancel = "game.btn.cancel";
    private const string k_KeyNoTerritory = "game.management.confirm_end_day.no_territory";
    private const string k_KeyIdleVillagers = "game.management.confirm_end_day.idle_villagers"; // 스마트 스트링 {0}=유휴 수
    private const string k_KeyQuestion = "game.management.confirm_end_day.question";

    public static ManagementEndDayConfirmPopup Instance { get; private set; }

    [Tooltip("팝업 루트 오브젝트 — 표시/숨김 토글 대상.")]
    [SerializeField] GameObject _panel;

    [Tooltip("미충족 조건 안내 문구를 표시할 텍스트.")]
    [SerializeField] TMP_Text _messageText;

    [Tooltip("[계속] — 조건 미충족이어도 밤으로 진행.")]
    [SerializeField] Button _proceedButton;

    [Tooltip("[취소] — 팝업을 닫고 낮 유지.")]
    [SerializeField] Button _cancelButton;

    private ManagementController _controller;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        SetActiveSafe(false); // 시작 시 숨김 — 요청 시에만 노출.
    }

    private void Start()
    {
        if (_proceedButton != null)
        {
            _proceedButton.onClick.RemoveAllListeners();
            _proceedButton.onClick.AddListener(HandleProceed);
        }
        if (_cancelButton != null)
        {
            _cancelButton.onClick.RemoveAllListeners();
            _cancelButton.onClick.AddListener(Hide);
        }

        if(DayNightManager.Instance != null)
        {
            DayNightManager.Instance.OnDayToNight += Hide;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// 낮 종료 요청 단일 진입점(#219) — 모든 뷰는 이 한 줄만 호출한다.
    /// 팝업이 씬에 있으면 확인 팝업 경유, 없으면(배선 누락) 경고를 남기고 곧장 EndDay 폴백
    /// — 확인 우회를 로그로 드러내 무증상 분기를 막는다.
    /// </summary>
    public static void Request(ManagementController controller)
    {
        if (controller == null)
        {
            return;
        }
        if (Instance != null)
        {
            Instance.RequestEndDay(controller);
            return;
        }
        Debug.LogWarning("[경영] 낮 종료 확인 팝업이 씬에 없어 확인 없이 바로 종료합니다. 팝업 배선을 확인하세요.");
        controller.EndDay();
    }

    /// <summary>
    /// 조건을 점검해 팝업을 띄우거나(미충족) 바로 종료한다(충족). 외부 진입은 <see cref="Request"/>로만.
    /// 두 조건이 모두 충족이면 팝업 없이 바로 <see cref="ManagementController.EndDay"/>,
    /// 하나라도 미충족이면 미충족 항목을 담아 팝업을 띄운다.
    /// </summary>
    private void RequestEndDay(ManagementController controller)
    {
        if (controller == null || !controller.IsDay)
        {
            return;
        }

        _controller = controller;

        string warnings = BuildWarnings(controller);
        if (string.IsNullOrEmpty(warnings))
        {
            controller.EndDay(); // 조건 충족 — 확인 없이 진행.
            return;
        }

        if (_messageText != null)
        {
            _messageText.text = warnings;
        }
        RefreshButtonLabels(); // 열릴 때마다 현재 로케일로 pull → 언어 전환 즉시 반영
        SetActiveSafe(true);
    }

    // 미충족 조건을 줄바꿈으로 이어 붙인다(#219). 둘 다 충족이면 빈 문자열(=팝업 없이 진행).
    // 문구는 NorthLand_default String Table에서 현재 로케일로 pull한다(하드코딩 없음).
    private static string BuildWarnings(ManagementController controller)
    {
        var sb = new StringBuilder();

        if (!controller.HasExpandedTerritory)
        {
            sb.AppendLine(LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, k_KeyNoTerritory));
        }
        if (controller.HasIdleVillagers)
        {
            int idle = controller.MaxVillagers - controller.AssignedTotal;
            sb.AppendLine(LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, k_KeyIdleVillagers, idle));
        }

        if (sb.Length == 0)
        {
            return string.Empty;
        }

        sb.Append('\n').Append(LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, k_KeyQuestion));
        return sb.ToString();
    }

    // [계속]/[취소] 버튼 라벨을 현재 로케일로 갱신한다. 각 버튼의 자식 TMP_Text를 대상으로 한다.
    private void RefreshButtonLabels()
    {
        SetButtonLabel(_proceedButton, k_KeyProceed);
        SetButtonLabel(_cancelButton, k_KeyCancel);
    }

    private static void SetButtonLabel(Button button, string key)
    {
        if (button == null)
        {
            return;
        }
        TMP_Text label = button.GetComponentInChildren<TMP_Text>();
        if (label != null)
        {
            label.text = LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, key);
        }
    }

    private void HandleProceed()
    {
        Hide();
        if (_controller != null)
        {
            _controller.EndDay();
        }
    }

    // 팝업을 닫는다([취소] 및 진행 후 공통). 낮 상태는 그대로 유지된다.
    private void Hide() => SetActiveSafe(false);

    // 참조 미할당 시 NullRef 대신 경고만 남기고 넘어간다(씬 배선 누락 방어 — ResultUIManager와 동일).
    private void SetActiveSafe(bool active)
    {
        if (_panel == null)
        {
            Debug.LogWarning("[경영] 낮 종료 확인 팝업 패널 참조가 비어 있습니다. 인스펙터 배선을 확인하세요.");
            return;
        }
        _panel.SetActive(active);
    }
}
