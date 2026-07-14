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

    // TODO: 문자열 대신 실제 타워 데이터 객체를 받아서 표시하도록 변경
    public void ShowInfo(string info)
    {
        _towerInfoText.text = info;
        gameObject.SetActive(true);
    }

    public void HideInfo()
    {
        _towerInfoText.text = string.Empty;
        gameObject.SetActive(false);
    }
}
