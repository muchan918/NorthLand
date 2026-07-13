/// <summary>
/// 영토 노드의 상태 (GDD §6.3 노란색/회색/숨김).<br/>
/// 전이는 <see cref="TerritoryGraph"/>만 수행하며 Locked → Selectable → Owned 단방향이다.
/// </summary>
public enum TerritoryState
{
    /// <summary>미공개. 선택 불가, 플레이어에게 숨김. (기본값)</summary>
    Locked = 0,

    /// <summary>프론티어(GDD 회색). 보유 영토에 인접해 선택 가능.</summary>
    Selectable = 1,

    /// <summary>보유(GDD 노란색). 본진은 최초부터 Owned.</summary>
    Owned = 2,
}
