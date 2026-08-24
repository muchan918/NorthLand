using System;
using NorthLand.Combat;
using UnityEngine;

// 전투 그리드에 타워가 놓이면 충족된다.
// 배치를 취소해도 기회가 소모되지 않는다 — 카운트가 늘지 않았을 뿐이므로 몇 번이든 다시 시도할 수 있다.
//
// ⚠ 클래스 이름을 바꾸면 [SerializeReference]로 저장된 기존 스텝 데이터가 깨진다.
[Serializable]
public class TowerPlacedCondition : TutorialCondition
{
    [Tooltip("이 단계에서 새로 놓아야 하는 타워 수.")]
    [Min(1)]
    [SerializeField]
    private int count = 1;

    [Tooltip("특정 타워만 세려면 지정한다. 비우면 아무 타워나 센다.")]
    [SerializeField]
    private TowerAsset targetTower;

    // 시작 시점의 타워 수. 이 값보다 count만큼 늘어나면 충족이다.
    // Tower.ActiveChanged는 철거·합성·되돌리기로도 발화하므로 '증가'만 본다.
    private int _baseline;

    public override void Begin(TutorialContext context)
    {
        // 조건 객체는 에셋에 저장되므로 이전 플레이의 값이 남아 있다 — 반드시 여기서 다시 잡는다.
        _baseline = CountMatching();

        Tower.ActiveChanged += OnActiveChanged;

        // Begin 시점에 이미 조건이 성립할 수 있다(count가 0 이하로 잘못 설정된 경우 등).
        OnActiveChanged();
    }

    public override void End()
    {
        Tower.ActiveChanged -= OnActiveChanged;
    }

    private void OnActiveChanged()
    {
        if (CountMatching() - _baseline >= count)
        {
            Fire();
        }
    }

    // baseline과 현재 개수가 **같은 필터를 통과해야** 증가분이 맞는다 —
    // 한쪽만 걸러내면 다른 타워를 놓았을 때 차이가 음수로 벌어지거나 즉시 충족된다.
    private int CountMatching()
    {
        if (targetTower == null)
        {
            return Tower.Active.Count;
        }

        int matched = 0;

        for (int i = 0; i < Tower.Active.Count; i++)
        {
            Tower tower = Tower.Active[i];

            if (tower != null && tower.Asset == targetTower)
            {
                matched++;
            }
        }

        return matched;
    }
}
