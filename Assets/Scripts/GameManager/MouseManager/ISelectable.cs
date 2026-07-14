public interface ISelectable
{
    void OnSelected();     // 선택됨: 자기 정보 패널 열기 등
    void OnDeselected();   // 선택 해제됨: 패널 닫기 등
}