using System;
using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    // 반경 안의 아군 공격 타워를 강화하는 오라. **폴링하지 않는다** — 타워는 스스로 움직이지 않으므로
    // 대상 집합이 바뀌는 순간은 "타워가 추가/제거될 때"뿐이고, 그것이 곧 Tower.ActiveChanged다(#164).
    //
    // 페이즈 게이팅이 없다(ActivePhase.Always): 배치 즉시 효과가 걸려야 낮 정보 패널에 버프된 스탯이 보인다.
    [Serializable]
    public sealed class BuffAuraAction : TowerAction
    {
        [NonSerialized] TowerAsset.BuffAuraFields aura;

        // 이 오라가 실제로 버프를 부여한 타워들. 범위에서 빠진 대상의 버프를 걷어내기 위해 추적한다 —
        // 예전 구현은 부여만 하고 해제 경로가 없어서, 반경이 줄어들면 유령 버프가 남았다.
        [NonSerialized] List<Tower> buffed;
        [NonSerialized] List<Tower> targetScratch;
        [NonSerialized] List<TowerStatModifier> modifierScratch;

        // 대상 집합이 바뀌었으니 다음 Tick에서 다시 계산하라는 표시.
        [NonSerialized] bool dirty;

        public override TowerActivePhase ActivePhase => TowerActivePhase.Always;

        // 디버프 오라와 같은 원장 축(AuraRadius)을 쓴다. 기본값만 SO의 BuffAura.Radius로 다르다.
        // 이 축이 공격 사거리와 갈려 있는 이유는 DebuffAuraAction.Radius 주석에 있다.
        public float Radius =>
            aura == null ? 0f : Owner.Stats.Evaluate(TowerStat.AuraRadius, aura.Radius);

        // 선택 사거리 원은 오라 반경을 그린다(디버프 오라와 동일 규칙).
        public override float DisplayRange => Radius;

        protected override void OnInitialize(TowerAsset asset)
        {
            aura = asset.BuffAura;

            buffed ??= new List<Tower>();
            targetScratch ??= new List<Tower>();
            modifierScratch ??= new List<TowerStatModifier>(2);

            // 구독은 Initialize↔Dispose **대칭 쌍**이다. 액션은 MonoBehaviour가 아니라 OnDestroy가 없지만,
            // Tower.OnDisable이 Dispose를 부르고 Unity가 파괴 전에 OnDisable을 먼저 호출하므로
            // static 이벤트 누수 경로가 닫힌다(SystemMap F7). 예전 MonoBehaviour 시절에는
            // Initialize에서 걸고 OnDestroy에서 푸는 비대칭이라 생명주기 규약의 유일한 예외였다.
            Tower.ActiveChanged -= MarkDirty;   // 재초기화(재활성화) 시 중복 구독 방지
            Tower.ActiveChanged += MarkDirty;

            // 배치 즉시 반영한다 — 낮 정보 패널이 이 프레임에 버프된 스탯을 보여야 한다.
            Reapply();
        }

        public override void Dispose()
        {
            Tower.ActiveChanged -= MarkDirty;
            RemoveFromAll();
        }

        // 이벤트는 **플래그만 세운다.** 실행은 Tick에서 한다:
        //  · 재진입 차단 — 예전에는 Reapply가 ActiveChanged 핸들러 안에서 직접 돌았다.
        //  · 집단 변경이 프레임당 1회로 접힘 — 합성으로 재료 3개가 동시에 비활성화되면 이벤트가 3번
        //    발화하는데, 예전에는 Reapply도 3번 돌았다(드래그 선택도 마찬가지).
        void MarkDirty() => dirty = true;

        public override void Tick(float deltaTime)
        {
            if (!dirty) return;
            dirty = false;
            Reapply();
        }

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
                if (!targetScratch.Contains(tower)) tower.RemoveBuff(SourceId);
            }

            buffed.Clear();
            for (int i = 0; i < targetScratch.Count; i++)
            {
                Tower tower = targetScratch[i];

                // duration<=0 → 지속형(이 오라가 사라질 때 Dispose에서 해제. 밤 게이팅 없음).
                tower.Stats.Apply(SourceId, modifierScratch, 0f, Time.time);
                buffed.Add(tower);
            }
        }

        void CollectTargets()
        {
            targetScratch.Clear();

            float sqrRadius = Radius * Radius;
            Vector3 origin = Origin.position;

            List<Tower> towers = Tower.Active;
            for (int i = 0; i < towers.Count; i++)
            {
                Tower tower = towers[i];
                if (tower == null || tower == Owner) continue;          // 자기 자신 제외
                if (tower.Faction != Owner.Faction) continue;           // 버프는 아군만

                // 공격 액션이 있는 타워만 대상으로 한다. 구상 타입이 아니라 **능력**으로 판정하므로
                // 모든 타워가 같은 Tower 타입인 채로도 의도가 그대로 유지된다.
                //
                // 오라 타워를 제외하는 이유가 "효과가 없어서"만은 아니다: 오라 반경이 사거리 축을 공유하므로,
                // 버프 오라끼리 서로를 버프하면 A가 B의 반경을 넓히고 넓어진 B가 다시 A를 덮는
                // 순서 의존 피드백이 생긴다. 능력 판정이 그 고리를 구조적으로 끊는다.
                if (!tower.Has<AttackAction>()) continue;

                if ((tower.transform.position - origin).sqrMagnitude > sqrRadius) continue;

                targetScratch.Add(tower);
            }
        }

        void RemoveFromAll()
        {
            if (buffed == null) return;

            for (int i = 0; i < buffed.Count; i++)
            {
                if (buffed[i] != null) buffed[i].RemoveBuff(SourceId);
            }
            buffed.Clear();
        }

        // 반경(원장 축 AuraRadius — 버프 타일이 바꾼다) + 이 오라가 남에게 거는 스탯 변화(#536).
        public override void DescribeStatRows(List<TowerStatRowData> into)
        {
            if (aura == null) return;

            into.Add(TowerStatRowData.Stat(TowerStatsFormatter.k_AuraRadiusKey, aura.Radius, Radius));

            if (aura.Modifiers == null) return;

            for (int i = 0; i < aura.Modifiers.Count; i++)
            {
                AttackAction.AddIf(into, TowerStatsFormatter.ModifierRow(aura.Modifiers[i]));
            }
        }

#if UNITY_EDITOR
        public override void DrawGizmos()
        {
            if (aura == null) return;
            UnityEditor.Handles.color = new Color(0.3f, 0.7f, 1f);
            UnityEditor.Handles.DrawWireDisc(Origin.position, Vector3.up, Radius);
        }
#endif
    }
}
