using UnityEngine;

/// ISelectable 동작 확인용 테스트 스크립트.
/// 콜라이더와 레이어가 있는 오브젝트에 붙이면, 클릭으로 선택됐을 때 색이 바뀌고 정보 UI가 뜬다. (요구사항 ②)
public class SelectableTest : MonoBehaviour, ISelectable
{
    [SerializeField] string _testInfo = "Test1";
    [SerializeField] Color _selectedColor = Color.yellow;

    private Renderer _renderer;
    private Color _originalColor;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _originalColor = _renderer.material.color;
    }

    public void Initialize(string info, Color selectedColor)
    {
        _testInfo = info;
        _selectedColor = selectedColor;
    }

    // 패널이 없는 테스트 씬(정본 GameScene 밖)에서도 이 헬퍼만으로 예외가 나지 않게 가드한다.
    // `?.`가 아니라 Unity 오버로드 ==인 이유는 #554와 같다 — 순수 참조 검사는 파괴를 못 잡는다.
    public void OnSelected()
    {
        _renderer.material.color = _selectedColor;
        if (TowerInfoUI.Instance != null) TowerInfoUI.Instance.ShowInfo(_testInfo);
        Debug.Log($"{name} 선택됨", this);
    }

    public void OnDeselected()
    {
        _renderer.material.color = _originalColor;
        if (TowerInfoUI.Instance != null) TowerInfoUI.Instance.HideInfo();
        Debug.Log($"{name} 선택 해제됨", this);
    }
}
