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
/// 표시 전용 컴포넌트(비-싱글톤) — 이 팝업은 뷰(경영 패널·NightAction 패널)가 각자
/// <c>[SerializeField]</c>로 직접 들고 호출한다. "언제 넘어갈지"의 판정 데이터는 컨트롤러가
/// 소유하고, 이 컴포넌트는 그 데이터를 읽어 팝업을 띄우거나 곧장 종료할 뿐이다.<br/>
/// <br/>
/// 배선 시점 규칙: 자기완결적 배선(버튼 리스너 등)은 <see cref="Awake"/>에서 — 스크립트
/// 자신의 GameObject가 항상 활성이라는 전제만 있으면 다른 오브젝트 초기화 순서와 무관하게 안전하다.
/// 다른 매니저(<see cref="DayNightManager"/>)에 의존하는 구독은 <c>Start</c>에서 — Unity는
/// <c>Awake→OnEnable</c>을 오브젝트 단위로 묶어 실행하므로 <c>OnEnable</c>은 다른 오브젝트의
/// <c>Awake</c>보다 앞설 수 있다(이 씬에서 실측: 그 시점 <c>DayNightManager.Instance</c>가 null).
/// <c>Start</c>는 모든 <c>Awake</c> 이후이므로 안전하며, 해제는 <c>OnDestroy</c>로 대칭을 맞춘다.
/// 실행 순서를 강제하는 장치는 이 저장소에 없으니, 구독 실패 시 경고를 남겨 즉시 드러낸다.
/// </summary>
public class ManagementEndDayConfirmPopup : MonoBehaviour
{
    // 로컬라이제이션 키(#219) — 값은 NorthLand_default String Table(ko/ja/en)에 있다.
    private const string k_KeyProceed = "game.btn.proceed";
    private const string k_KeyCancel = "game.btn.cancel";
    private const string k_KeyNoTerritory = "game.management.confirm_end_day.no_territory";
    private const string k_KeyIdleVillagers = "game.management.confirm_end_day.idle_villagers"; // 스마트 스트링 {0}=유휴 수
    private const string k_KeyQuestion = "game.management.confirm_end_day.question";

    [Tooltip("팝업 루트 오브젝트 — 표시/숨김 토글 대상. 이 스크립트가 얹힌 오브젝트와 달라야 한다" +
             "(같으면 최초 SetActive(false) 직후 Awake/OnEnable이 돌지 않아 배선이 통째로 누락된다).")]
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
        SetActiveSafe(false); // 시작 시 숨김 — 요청 시에만 노출.

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
    }

    // DayNightManager 의존 구독은 Start에서 — 클래스 주석 참고(OnEnable은 같은 오브젝트의 Awake
    // 직후에 불려 다른 오브젝트의 Awake보다 앞설 수 있어, 실측에서 Instance가 아직 null이었다).
    private void Start()
    {
        if (DayNightManager.Instance != null)
        {
            DayNightManager.Instance.OnDayToNight += Hide;
        }
        else
        {
            // 구독 실패가 조용히 지나가면 "가끔 밤에 팝업이 안 닫힌다"로만 드러난다 — 여기서 즉시 알린다.
            Debug.LogWarning("[경영] DayNightManager를 찾지 못해 밤 전환 자동 닫기가 비활성화됩니다.");
        }
    }

    private void OnDestroy()
    {
        if (DayNightManager.Instance != null)
        {
            DayNightManager.Instance.OnDayToNight -= Hide;
        }
    }

    /// <summary>
    /// 낮 종료 요청 진입점(#219) — 뷰가 인스펙터로 연결한 이 팝업 인스턴스에 직접 호출한다.
    /// 두 조건이 모두 충족이면 팝업 없이 바로 <see cref="ManagementController.EndDay"/>,
    /// 하나라도 미충족이면 미충족 항목을 담아 팝업을 띄운다.
    /// </summary>
    public void Request(ManagementController controller)
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
