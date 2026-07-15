/// 마우스 호버에 반응하는 오브젝트가 구현한다. ISelectable(클릭 선택)과 병렬 개념 —
/// 한 오브젝트가 둘 다 구현할 수 있다. MouseManager가 Idle 상태에서 커서 밑 IHoverable을
/// 추적해 OnHoverChanged로 통지하고, 툴팁 UI가 그 시점에 아래 내용을 pull해 표시한다.
public interface IHoverable
{
    /// 호버 시점에 표시할 툴팁 내용을 반환한다. 매 호버마다 호출되므로
    /// 동적 값(버프 레벨·현재 생산량 등)도 그 순간 계산해 넘길 수 있다.
    /// null이면 툴팁을 표시하지 않는다(예: 색 변경만 하는 호버 대상, 이슈 #67).
    TooltipContent? GetTooltipContent();

    /// 이 대상이 호버 대상이 됐을 때 MouseManager.SetHover가 호출한다(#67 호버 하이라이트,
    /// Docs/Core/MouseManager.md §8). 하이라이트 등 연출은 여기서 처리.
    void OnHoverEnter();

    /// 이 대상이 더 이상 호버 대상이 아니게 됐을 때 MouseManager.SetHover가 호출한다
    /// (다음 대상으로 바뀌기 직전, 또는 커서가 벗어나 null이 될 때).
    void OnHoverExit();
}
