/// 마우스 호버에 반응하는 오브젝트가 구현한다. ISelectable(클릭 선택)과 병렬 개념 —
/// 한 오브젝트가 둘 다 구현할 수 있다. MouseManager가 Idle 상태에서 커서 밑 IHoverable을
/// 추적해 OnHoverChanged로 통지하고, 툴팁 UI가 그 시점에 아래 내용을 pull해 표시한다.
public interface IHoverable
{
    /// 호버 시점에 표시할 툴팁 내용을 반환한다. 매 호버마다 호출되므로
    /// 동적 값(버프 레벨·현재 생산량 등)도 그 순간 계산해 넘길 수 있다.
    TooltipContent GetTooltipContent();

    // TODO(TBD): 호버 하이라이트(OnHoverEnter/OnHoverExit)가 필요해지면 여기에 추가한다.
    //            지금은 툴팁 표시만 담당한다. (Docs/Core/MouseManager.md §8 호버 하이라이트 TODO)
}
