namespace NorthLand.Combat
{
    /// 이 피해가 어떤 사건에서 나왔는지.
    ///
    /// **소리 전용 플래그가 아니라 사건 종류로 둔 것이 의도다.** 자폭은 이미 피해 코어 안에서
    /// 다른 축을 여럿 갖고 있다 — 웨이브 HP 배율을 곱하지 않고(`Enemy.Detonate`), `Killed`를
    /// 발행하지 않으며(`Enemy.SelfDestruct`), 규약 ④ 예산의 입력이다(`CombatBalance.md`).
    /// 그 구분이 지금까지 **가해자 쪽에만 있고 피해자 쪽에는 없었다.**
    ///
    /// 지금 유일한 소비처는 본진 피격음(`PlayerBase.PlayHitSfx`, `AudioManager.md` §6.4)이다.
    public enum DamageKind
    {
        /// 평타·투사체·빔·스킬 등 일반 공격. 기본값이라 기존 호출부가 전부 여기로 들어온다.
        Attack,

        /// 자폭병이 터지며 준 피해(`Enemy.Detonate`). 가해자가 자기 자리에서 폭발 연출을
        /// 이미 냈다는 뜻이라, 피해자 쪽 피격 연출은 겹치지 않게 생략한다.
        SelfDestruct,

        /// 돌진해 들이받아 준 피해(`EnemyImpactTargetAction`). 속도에 비례하는 단발 대타격이라
        /// 평타와 크기가 자릿수로 다르다 — `tank`의 P1은 `speed × 3.75`에 `MinSpeed 10` 게이트라
        /// **최소 37.5**(본진 HP 200의 18.75%)다.
        ///
        /// ⚠ **`BossImpact`가 아니라 `Impact`인 것이 의도다.** 축은 「누가 때렸나」가 아니라
        /// 「어떤 사건인가」다 — `EnemyImpactTargetAction`은 보스 전용 노드가 아니라 충돌 피해
        /// 노드라서, 돌진하는 일반 몹이 생기면 그쪽도 같은 사건을 낸다. 이름에 `Boss`를 박으면
        /// 그날 이름과 실제가 어긋난 채로 동작한다(`EnemyType`이 역할을 겸직해 생긴 WL-207과 같은 형태).
        Impact,
    }

    public struct DamageInfo
    {
        public float Amount;
        public IAttacker Source;
        public DamageKind Kind;

        // kind는 선택 인자다 — 자폭 외 9개 호출부가 전부 Attack이라, 기본값을 두는 편이
        // "새 공격 경로를 추가한 사람이 종류를 잘못 고를" 여지를 없앤다.
        public DamageInfo(float amount, IAttacker source, DamageKind kind = DamageKind.Attack)
        {
            Amount = amount;
            Source = source;
            Kind = kind;
        }
    }
}
