using TMPro;
using UnityEngine;

/// 타워 정보 패널 UI를 담당하는 스크립트. (요구사항 ③)
/// TODO: 타워 데이터를 다루는 객체가 생기면 그 객체를 참조해서 정보를 표시하도록 수정 필요.
public class TowerInfoPanel : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI _towerInfoText;

    private void Awake()
    {
        gameObject.SetActive(false);
    }

    // TODO: 타워 데이터 객체를 받아서 정보를 표시하도록 수정 필요.
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