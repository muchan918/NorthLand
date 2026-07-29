using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    // 반경 안의 아군 공격 타워를 강화하는 오라. **폴링하지 않는다** — 타워는 스스로 움직이지 않으므로
    // 대상 집합이 바뀌는 순간은 "타워가 추가/제거될 때"뿐이고, 그것이 곧 Tower.ActiveChanged다(#164).
    //
    // 페이즈 게이팅이 없다(ActivePhase.Always): 배치 즉시 효과가 걸려야 낮 정보 패널에 버프된 스탯이 보인다.
    [AddComponentMenu("")]   // 런타임 조립 전용
    [DisallowMultipleComponent]
    public sealed class BuffAuraBehaviour : MonoBehaviour, ITowerBehaviour
    {
        Tower owner;
        TowerAsset.BuffAuraFields aura;
        int sourceId;

        // 이 오라가 실제로 버프를 부여한 타워들. 범위에서 빠진 대상의 버프를 걷어내기 위해 추적한다 —
        // 예전 구현은 부여만 하고 해제 경로가 없어서, 반경이 줄어들면 유령 버프가 남았다.
        readonly List<Tower> buffed = new List<Tower>();
        readonly List<Tower> targetScratch = new List<Tower>();
        readonly List<TowerStatModifier> modifierScratch = new List<TowerStatModifier>(2);

        public TowerActivePhase ActivePhase => TowerActivePhase.Always;

        // 디버프 오라와 같은 원장 축(사거리)을 쓴다. 기본값만 SO의 MagicRadius로 다르다.
        public float Radius =>
            aura == null ? 0f : owner.Stats.Evaluate(TowerStat.AttackRange, aura.Radius);

        public void Initialize(in TowerBuildContext context)
        {
            owner = context.Owner;
            aura = context.Asset.Magic?.BuffAura;

            // 인스턴스별 소스키 — 같은 종류 버프 타워를 여러 개 지으면 각각 별개 소스로 합산 중첩된다.
            sourceId = GetInstanceID();

            Tower.ActiveChanged -= Reapply;   // 재초기화(재활성화) 시 중복 구독 방지
            Tower.ActiveChanged += Reapply;

            Reapply();
        }

        public void Dispose()
        {
            Tower.ActiveChanged -= Reapply;
            RemoveFromAll();
        }

        // 이 행동은 Unity 생명주기 콜백을 쓰지 않는다는 규약의 **유일한 예외**다.
        // Tower.ActiveChanged가 static 이벤트라, 구독을 남긴 채 파괴되면 죽은 대상을 계속 호출해
        // MissingReferenceException이 난다(SystemMap F7 — static 이벤트는 구독 해제가 구독자 책임).
        // 초기화가 아니라 정리만 하므로 순서 의존을 만들지 않는다.
        void OnDestroy() => Tower.ActiveChanged -= Reapply;

        // 대상 집합이 이벤트로 갱신되므로 매 프레임 할 일이 없다.
        public void Tick(float deltaTime) { }

        void Reapply()
        {
            if (aura == null) return;

            AuraModifiers.ConvertBuffModifiers(aura.Modifiers, modifierScratch);

            // 실제 강화가 없으면(모든 축이 배율 1) 아무도 버프하지 않는다 — 부여했던 것도 회수한다.
            if (modifierScratch.Count == 0)
            {
                RemoveFromAll();
                return;
            }

            CollectTargets();

            // 범위에서 빠진(또는 파괴된) 대상의 버프를 걷어낸다.
            for (int i = 0; i < buffed.Count; i++)
            {
                Tower tower = buffed[i];
                if (tower == null) continue;
                if (!targetScratch.Contains(tower)) tower.RemoveBuff(sourceId);
            }

            buffed.Clear();
            for (int i = 0; i < targetScratch.Count; i++)
            {
                Tower tower = targetScratch[i];

                // duration<=0 → 지속형(이 오라가 사라질 때 Dispose에서 해제. 밤 게이팅 없음).
                tower.Stats.Apply(sourceId, modifierScratch, 0f, Time.time);
                buffed.Add(tower);
            }
        }

        void CollectTargets()
        {
            targetScratch.Clear();

            float sqrRadius = Radius * Radius;
            Vector3 origin = transform.position;

            List<Tower> towers = Tower.Active;
            for (int i = 0; i < towers.Count; i++)
            {
                Tower tower = towers[i];
                if (tower == null || tower == owner) continue;          // 자기 자신 제외
                if (tower.Faction != owner.Faction) continue;           // 버프는 아군만

                // 공격 행동이 있는 타워만 대상으로 한다. 구상 타입이 아니라 **능력**으로 판정하므로
                // 모든 타워가 같은 Tower 타입이 된 뒤에도 의도가 그대로 유지된다.
                //
                // 오라 타워를 제외하는 이유가 "효과가 없어서"만은 아니다: 오라 반경이 사거리 축을 공유하므로,
                // 버프 오라끼리 서로를 버프하면 A가 B의 반경을 넓히고 넓어진 B가 다시 A를 덮는
                // 순서 의존 피드백이 생긴다. 능력 판정이 그 고리를 구조적으로 끊는다.
                if (!tower.Has<AttackBehaviour>()) continue;

                if ((tower.transform.position - origin).sqrMagnitude > sqrRadius) continue;

                targetScratch.Add(tower);
            }
        }

        void RemoveFromAll()
        {
            for (int i = 0; i < buffed.Count; i++)
            {
                if (buffed[i] != null) buffed[i].RemoveBuff(sourceId);
            }
            buffed.Clear();
        }

        // 정보 패널에 이 오라가 기여할 줄. 반경 + 부여하는 스탯 변화.
        public string DescribeStats()
        {
            if (aura == null) return null;

            string text = TowerStatsFormatter.BuildRangeLine(Radius);
            if (aura.Modifiers == null) return text;

            for (int i = 0; i < aura.Modifiers.Count; i++)
            {
                string line = TowerStatsFormatter.BuildModifierLine(aura.Modifiers[i]);
                if (!string.IsNullOrEmpty(line)) text += $"\n{line}";
            }

            return text;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (aura == null) return;
            UnityEditor.Handles.color = new Color(0.3f, 0.7f, 1f);
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, Radius);
        }
#endif
    }
}
