using TMPro;
using UnityEngine;

/// 타워 정보 패널 UI. 나중에 UIManager가 관리할 것을 고려해 싱글톤으로 접근한다.
/// (주의) 이 오브젝트는 씬에서 '활성' 상태로 둬야 Awake가 실행되어 Instance가 등록된다.
///        숨김 처리는 Awake에서 하므로, 인스펙터에서 미리 꺼두지 말 것.
public class TowerInfoUI : MonoBehaviour
{
    public static TowerInfoUI Instance { get; private set; }

    [SerializeField] TextMeshProUGUI _towerInfoText;

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

    // descriptionKey는 로컬라이즈 키(TowerData.DescriptionKey 등)를 받는다. BuildingInfoUI와 동일 패턴(#153) —
    // 지속형 패널에 LocalizationHelper.Get을 쓰는 건 로케일 변경 자동 갱신이 안 되는 한계가 있음을 인지하고
    // 미러링(BuildingInfoUI와 동일한 트레이드오프, 필요 시 후속으로 LocalizeStringEvent로 함께 교체).
    // statsText는 이미 조합된 평문(공격력/사거리 등 SO 수치) — 숫자값이라 로컬라이즈 대상이 아니므로 그대로 이어붙인다.
    public void ShowInfo(string descriptionKey, string statsText = null)
    {
        string text = LocalizationHelper.Get(LocalizationHelper.k_TowersTable, descriptionKey);
        if (!string.IsNullOrEmpty(statsText))
        {
            text += "\n\n" + statsText;
        }
        _towerInfoText.text = text;
        gameObject.SetActive(true);
    }

    public void HideInfo()
    {
        _towerInfoText.text = string.Empty;
        gameObject.SetActive(false);
    }
}
