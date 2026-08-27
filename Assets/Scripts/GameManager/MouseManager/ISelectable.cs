public interface ISelectable
{
    void OnSelected();     // 선택됨: 자기 정보 패널 열기 등
    void OnDeselected();   // 선택 해제됨: 패널 닫기 등
}

// 튜토리얼 제한 중 선택 가능한 대상이 자기 행동 분류를 선언한다.
// MouseManager는 주민·건물·타워 같은 구체 타입을 모르고 이 계약만 읽는다.
public interface ITutorialSelectionGate
{
    TutorialAction SelectionAction { get; }
}
