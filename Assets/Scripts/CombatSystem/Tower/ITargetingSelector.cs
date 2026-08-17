namespace NorthLand.Combat
{
    /// 인게임에서 타워의 조준 방식을 바꾸는 창구(#387).
    ///
    /// 정보 패널(`TowerInfoUI`)이 `Tower`를 통째로 알지 않도록 **이 둘만** 노출한다 —
    /// 뷰가 알아야 하는 것은 "지금 뭐라고 표시할지"와 "옆으로 넘겨라"뿐이다. 패널은
    /// `호출부가 데이터를 주고 뷰는 그리기만 한다`는 pull 방식으로 서 있고, 조작 하나 때문에
    /// 그 규칙이 무너지면 다음 조작이 곧바로 뒤따른다.
    public interface ITargetingSelector
    {
        /// 지금 조준 방식의 표시명.
        string TargetingName { get; }

        /// 목록에서 `step`칸(±1) 옮긴 방식으로 바꾼다.
        void CycleTargeting(int step);
    }
}
