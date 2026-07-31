using UnityEngine;

namespace NorthLand.Combat
{
    // 이 행동이 도는 페이즈. 게이팅은 각 행동이 아니라 호스트(Tower)가 이 값을 보고 대신 처리한다 —
    // 행동마다 DayNightManager를 직접 폴링하면 페이즈 규칙이 조용히 갈라진다(WL-044가 정확히 그 사고였다:
    // Tower는 밤 게이팅이 있는데 AuraTower의 버프 경로엔 없어서 낮에도 상시 동작).
    public enum TowerActivePhase
    {
        // 밤에만 동작. 공격·디버프 오라 — 전투 행위.
        NightOnly,

        // 페이즈 무관. 버프 오라 — 배치 즉시(낮 포함) 효과가 보여야 낮 정보 패널의 스탯이 맞는다(#164).
        Always,
    }

    // 조립 시점에 행동에 넘기는 값 묶음.
    //
    // Asset(SO)만으로 부족한 이유: firePoint는 프리팹 계층 안의 Transform 참조이고 enemyLayerMask는
    // 프리팹별 배선이라 ScriptableObject에 담을 수 없다. 런타임 AddComponent로 붙는 행동은
    // 직렬화 필드를 가질 수 없으므로 이 컨텍스트가 유일한 주입 경로다.
    public readonly struct TowerBuildContext
    {
        public readonly Tower Owner;
        public readonly TowerAsset Asset;
        public readonly LayerMask EnemyLayerMask;
        public readonly Transform FirePoint;

        public TowerBuildContext(Tower owner, TowerAsset asset, LayerMask enemyLayerMask, Transform firePoint)
        {
            Owner = owner;
            Asset = asset;
            EnemyLayerMask = enemyLayerMask;
            FirePoint = firePoint;
        }
    }

    // 타워의 행동 한 조각. 타워가 "무엇을 하는 물건인지"는 전부 이 구현체들이 가지며,
    // Tower는 정체성(SO/진영)·스탯 원장·선택 표현·레지스트리만 소유한다.
    //
    // 규약(어기면 초기화 순서 버그가 되돌아온다):
    //  · Awake/OnEnable/Start에서 아무것도 하지 않는다. 초기화는 Initialize 한 곳에서만.
    //  · Update를 스스로 돌지 않는다. 호스트가 게이팅 후 Tick으로 구동한다.
    //  · Dispose는 호스트 비활성화(철거·풀 반환) 시 호출된다 — 외부에 남긴 상태를 여기서 걷어낸다.
    public interface ITowerBehaviour
    {
        TowerActivePhase ActivePhase { get; }

        void Initialize(in TowerBuildContext context);

        void Tick(float deltaTime);

        void Dispose();

        /// 선택 시 바닥에 그릴 사거리 원의 반경. 이 행동이 "닿는 거리" 개념을 갖지 않으면 0.
        ///
        /// 호스트가 `AttackRange`로 대신 그릴 수 없는 이유: 그 값은 공격 행동에서만 나오므로
        /// 오라 전용 타워에서 0이 되어 원이 사라진다(#192 회귀). 표시 반경은 행동마다 근거가
        /// 다르므로(공격=사거리, 오라=오라 반경) `DescribeStats`와 같은 "행동이 자기 표시를 안다" 규약을 따른다.
        float DisplayRange { get; }

        /// 정보 패널에 이 행동이 기여할 설명 줄. 없으면 null.
        /// 호스트는 조각을 모아 붙이기만 하고 "무엇을 보여줄지"는 각 행동이 안다 —
        /// 예전에는 Tower와 AuraTower가 각자 자기 버전의 스탯 텍스트를 조립했다(WL-079).
        string DescribeStats();
    }

    // 공격 능력을 가진 행동의 공통 계약. **전달 방식**(투사체=AttackBehaviour / 히트스캔=HitscanAttackBehaviour)이
    // 달라도 "이 타워는 공격하는가"를 묻는 소비처는 구현체를 알 필요가 없다(#252).
    //
    // 이 인터페이스 없이 구상 클래스로 능력을 판정하면, 전달 방식을 하나 늘릴 때마다 판정하는 모든 곳이
    // 조용히 그 타워를 제외한다 — 버프 오라 대상 선정, 보스 마력 봉인 대상 선정, Tower의 IAttacker 4개가
    // 전부 그 대상이었다. 컴파일 에러가 아니라 "버프가 안 걸리는 타워"로 나타나므로 발견이 늦다.
    public interface IAttackBehaviour : ITowerBehaviour
    {
        float Damage { get; }

        float Range { get; }

        float Interval { get; }

        bool TryAttack(IDamageable target);
    }
}
