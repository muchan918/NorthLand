using System;
using NorthLand.Combat;
using UnityEngine;

// 현재 존재하는 타워가 최소 개수에 도달하면 충족된다.
// TowerPlacedCondition과 달리 단계 진입 전부터 있던 타워도 포함하므로
// "아처 타워를 최소 3개 보유"처럼 상태 자체가 목표인 단계에 사용한다.
[Serializable]
public class TowerCountCondition : TutorialCondition
{
    [Min(1)]
    [SerializeField]
    private int minimumCount = 1;

    [Tooltip("특정 타워만 세려면 지정한다. 비우면 아무 타워나 센다.")]
    [SerializeField]
    private TowerAsset targetTower;

    public override void Begin(TutorialContext context)
    {
        Tower.ActiveChanged += Check;
        Check();
    }

    public override void End()
    {
        Tower.ActiveChanged -= Check;
    }

    private void Check()
    {
        int count = 0;

        for (int i = 0; i < Tower.Active.Count; i++)
        {
            Tower tower = Tower.Active[i];

            if (tower != null && (targetTower == null || tower.Asset == targetTower))
            {
                count++;
            }
        }

        if (count >= minimumCount)
        {
            Fire();
        }
    }
}
