using CombatSpace;
using UnityEngine;

namespace NorthLand.Combat
{
    // 배치 시 확정된 타일 버프를 보관하고, 같은 GameObject의 Tower 원장에 밀어 넣는다.
    // 스탯 합성은 여기서 하지 않는다 — 규칙은 TowerStats.Evaluate 단일 출처다(WL-081).
    public sealed class TowerTileBuff : MonoBehaviour
    {
        public TileBuffCalculationResult Result { get; private set; }
            = new TileBuffCalculationResult();

        public void Initialize(TileBuffCalculationResult result)
        {
            Result = result ?? new TileBuffCalculationResult();

            // 푸시 모델: 타워 스탯 게터가 이 컴포넌트를 되읽지 않으므로 여기서 원장에 반영한다.
            // Tower의 원장은 필드 초기화자로 준비돼 있어 Awake/Start 순서와 무관하게 안전하다
            // (TowerPlacer가 Instantiate 직후 AddComponent→Initialize하는 시점에도 성립).
            if (TryGetComponent(out Tower tower))
            {
                tower.ApplyTileBuff(Result);
            }
        }

        public float GetValue(
            TileBuffStat stat,
            TileModifierMode mode)
        {
            return Result.GetValue(stat, mode);
        }
    }
}