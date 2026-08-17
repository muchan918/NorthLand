namespace NorthLand.Combat
{
    /// 조준 정책(`TargetingPolicy`)이 읽는 표적 부가 정보 — 체력과 경로 진행도(#387).
    ///
    /// `IDamageable`에 얹지 않고 분리한 이유: 그쪽은 **맞을 수 있는 모든 것**의 계약이라
    /// 본진(`PlayerBase`)·아군 병사(`Soldier`)도 구현한다. "종점까지 남은 경로 길이"는 그들에게
    /// 의미가 없어서, 넣으면 뜻 없는 값을 반환하는 구현이 강제로 늘어난다.
    /// 그래서 **경로를 따라 오는 적만** 이 인터페이스를 구현하고, 스캔은 후보마다 `as`로 물어본다.
    public interface ITargetProfile
    {
        float CurrentHp { get; }

        float MaxHp { get; }

        /// 경로 종점(본진)까지 남은 **경로를 따라 잰** 길이. "앞선 적/뒤처진 적" 조준의 판정 근거다.
        ///
        /// ⚠ **경로를 모르면 `float.NaN`이다**(경로 이동 컴포넌트가 없는 대상). 0이나 무한대로 대신하면
        /// "가장 앞" 또는 "가장 뒤"로 **항상** 뽑혀 버려서, 순위를 못 매긴다는 사실 자체가 사라진다.
        /// 정책은 NaN을 보면 순위 매기기를 포기하고(`float.NegativeInfinity`) 최근접 폴백에 맡긴다.
        float RemainingRouteDistance { get; }
    }
}
