using System.Collections.Generic;

namespace NorthLand.Combat
{
    /// <summary>
    /// 정보 패널이 스탯 행을 <b>당겨오는</b> 창구(#536). <see cref="Tower"/>가 구현한다.
    ///
    /// <para><b>왜 push가 아니라 pull인가</b>: 램프 스택처럼 전투 중에 계속 변하는 값을 즉시 반영하려면
    /// 누군가는 매 프레임 확인해야 한다. 호출부가 매 프레임 행을 만들어 밀면 문자열 조립 비용이
    /// 프레임마다 발생하는데, 실제로 값이 바뀌는 순간은 드물다. 그래서 <see cref="StatsVersion"/>만
    /// 매 프레임 비교하고 <b>달라졌을 때만</b> <see cref="BuildStatRows"/>를 부른다.</para>
    ///
    /// <para><b>왜 <c>Tower</c>가 아니라 좁은 계약인가</b>: <see cref="ITargetingSelector"/>와 같은 이유다 —
    /// 뷰가 타워의 전체 표면을 알기 시작하면 pull 방식이 무너진다. 패널이 이 인터페이스만 붙들면
    /// 파괴된 타워를 계속 만지는 경로도 <c>HideInfo</c>에서 참조를 놓는 것만으로 닫힌다.</para>
    /// </summary>
    public interface ITowerStatRowSource
    {
        /// 원장이 바뀔 때마다 오르는 값(<see cref="TowerStats.Version"/>). 패널은 이 값의 변화만 본다.
        int StatsVersion { get; }

        /// 지금 상태의 행을 <paramref name="into"/>에 담는다. 호출부가 리스트를 비우고 넘긴다.
        void BuildStatRows(List<TowerStatRowData> into);
    }
}
