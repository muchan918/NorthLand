using TMPro;
using UnityEngine;

/// 타워 정보 패널 UI. 나중에 UIManager가 관리할 것을 고려해 싱글톤으로 접근한다.
/// (주의) 이 오브젝트는 씬에서 '활성' 상태로 둬야 Awake가 실행되어 Instance가 등록된다.
///        숨김 처리는 Awake에서 하므로, 인스펙터에서 미리 꺼두지 말 것.
///
/// 표시 구성: 이름 · 역할 한 줄 · 설명 / 스탯 블록 / 합성 후보 블록.
/// BuildingInfoUI·StorePanelUI와 같은 pull 방식(호출부가 데이터를 주고 뷰는 그리기만 한다).
public class TowerInfoUI : MonoBehaviour
{
    public static TowerInfoUI Instance { get; private set; }

    [Header("제목 / 설명")]
    [SerializeField] TextMeshProUGUI _nameText;
    [Tooltip("역할 한 줄 요약 (TowerData.RoleKey)")]
    [SerializeField] TextMeshProUGUI _roleText;
    [SerializeField] TextMeshProUGUI _descriptionText;

    [Header("스탯")]
    [Tooltip("스탯 블록 전체. 스탯 문자열이 비면(오라 전용 타워 등) 통째로 숨긴다.")]
    [SerializeField] GameObject _statsContainer;
    [SerializeField] TextMeshProUGUI _statsText;

    [Header("합성 후보 (자리만 — 채우는 로직은 미구현)")]
    [Tooltip("합성 후보 블록 전체. Content에 행이 하나라도 붙어야 표시된다.")]
    [SerializeField] GameObject _mergeContainer;
    [Tooltip("후보 행이 생성될 부모. TowerRecipe를 훑는 로직이 붙으면 여기에 행을 채운다.")]
    [SerializeField] Transform _mergeContent;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        HideInfo(); // Instance 등록 후 숨기므로 안전
    }

    /// <summary>
    /// 타워 메타데이터로 패널을 채운다(정본 경로).<br/>
    /// <paramref name="statsText"/>는 이미 조합된 평문(공격력/사거리 등 SO 수치) — 숫자값이라 로컬라이즈 대상이 아니다.
    /// </summary>
    // 로컬라이즈는 지속형 패널에 LocalizationHelper.Get을 쓰는 방식이라 로케일 변경 자동 갱신이 안 되는 한계가 있다
    // (BuildingInfoUI와 동일한 트레이드오프, #153 — 필요 시 후속으로 LocalizeStringEvent로 함께 교체).
    public void ShowInfo(TowerData data, string statsText = null)
    {
        if (data == null)
        {
            HideInfo();
            return;
        }

        SetText(_nameText, L(data.NameKey));
        SetText(_roleText, L(data.RoleKey));
        SetText(_descriptionText, L(data.DescriptionKey));
        ApplyStats(statsText);
        RefreshMergeVisibility();
        gameObject.SetActive(true);
    }

    /// <summary>설명만 아는 호출부(테스트 헬퍼 등)를 위한 축약 경로. 이름·역할은 비워 둔다.</summary>
    public void ShowInfo(string descriptionKey, string statsText = null)
    {
        SetText(_nameText, string.Empty);
        SetText(_roleText, string.Empty);
        SetText(_descriptionText, L(descriptionKey));
        ApplyStats(statsText);
        RefreshMergeVisibility();
        gameObject.SetActive(true);
    }

    public void HideInfo()
    {
        SetText(_nameText, string.Empty);
        SetText(_roleText, string.Empty);
        SetText(_descriptionText, string.Empty);
        SetText(_statsText, string.Empty);
        gameObject.SetActive(false);
    }

    // 스탯이 없는 타워(오라 전용 등 DescribeStats가 빈 경우)는 블록을 통째로 숨겨 빈 박스가 남지 않게 한다.
    private void ApplyStats(string statsText)
    {
        SetText(_statsText, statsText);
        if (_statsContainer != null)
        {
            _statsContainer.SetActive(!string.IsNullOrEmpty(statsText));
        }
    }

    // 후보 행이 채워졌을 때만 블록을 띄운다. 후보 로직이 붙어 _mergeContent에 행을 넣는 순간 자동으로 나타나므로,
    // 그때 이 뷰를 고칠 필요가 없다(자리와 배선만 미리 잡아둔 이유).
    private void RefreshMergeVisibility()
    {
        if (_mergeContainer != null)
        {
            _mergeContainer.SetActive(_mergeContent != null && _mergeContent.childCount > 0);
        }
    }

    private static void SetText(TextMeshProUGUI label, string value)
    {
        if (label != null)
        {
            label.text = value ?? string.Empty;
        }
    }

    private static string L(string key) =>
        string.IsNullOrEmpty(key) ? string.Empty : LocalizationHelper.Get(LocalizationHelper.k_TowersTable, key);
}
