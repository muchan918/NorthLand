using UnityEngine;

/// ISelectable 동작 확인용 테스트 스크립트.
/// 콜라이더와 레이어가 있는 오브젝트에 붙이면, 클릭으로 선택됐을 때 색이 바뀐다. (요구사항 ②)
public class SelectableTest : MonoBehaviour, ISelectable
{
    [SerializeField] string _testInfo = "Test1";
    [SerializeField] Color _selectedColor = Color.yellow;
    [SerializeField] TowerInfoPanel _infoPanel;

    private Renderer _renderer;
    private Color _originalColor;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _originalColor = _renderer.material.color;

        if (_infoPanel == null)
        {
            _infoPanel = FindFirstObjectByType<TowerInfoPanel>();
        }
    }

    public void OnSelected()
    {
        _renderer.material.color = _selectedColor;
        _infoPanel.ShowInfo(_testInfo);
        Debug.Log($"{name} 선택됨", this);
    }

    public void OnDeselected()
    {
        _renderer.material.color = _originalColor;
        _infoPanel.HideInfo();
        Debug.Log($"{name} 선택 해제됨", this);
    }
}
