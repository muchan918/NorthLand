using TMPro;
using UnityEngine;

// 경영 공간 전용 건물 정보 패널. TowerInfoUI(전투 공간)와 동일한 구조를 갖되
// 별도 싱글톤으로 둔다 — 두 공간은 독립 관리 대상이라 UI도 공유하지 않는다.
// (주의) 이 오브젝트는 씬에서 '활성' 상태로 둬야 Awake가 실행되어 Instance가 등록된다.
//        숨김 처리는 Awake에서 하므로, 인스펙터에서 미리 꺼두지 말 것.
public class BuildingInfoUI : MonoBehaviour
{
    public static BuildingInfoUI Instance { get; private set; }

    [SerializeField] private TextMeshProUGUI _buildingInfoText;

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

    public void ShowInfo(string info)
    {
        _buildingInfoText.text = info;
        gameObject.SetActive(true);
    }

    public void HideInfo()
    {
        _buildingInfoText.text = string.Empty;
        gameObject.SetActive(false);
    }
}
