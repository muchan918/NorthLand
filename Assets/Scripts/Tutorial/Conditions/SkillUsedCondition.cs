using System;
using UnityEngine;

// 스킬을 한 번 시전하면 충족된다. 적을 맞혔는지는 보지 않는다 —
// 조준 실패로 단계가 막히지 않게 하려는 것이 목적이다.
// SkillManager는 CastAt이 성공했을 때만 ImpactResolved를 발행하므로(충전 소모까지 끝난 시점)
// "실제로 시전했다"가 그대로 판정된다.
//
// 맞힌 적까지 요구하려면 context.HitTargets.Count를 보면 된다 — SkillCastContext가 이미 들고 있다.
//
// ⚠ 클래스 이름을 바꾸면 [SerializeReference]로 저장된 기존 스텝 데이터가 깨진다.
[Serializable]
public class SkillUsedCondition : TutorialCondition
{
    [Tooltip("이 단계에서 시전해야 하는 횟수.")]
    [Min(1)]
    [SerializeField]
    private int count = 1;

    private SkillManager _skill;

    // 조건 객체는 에셋에 저장되므로 이전 플레이의 값이 남는다 — Begin에서 반드시 0으로 되돌린다.
    private int _casted;

    public override void Begin(TutorialContext context)
    {
        _casted = 0;

        // SkillManager는 static Instance를 갖고 있어 Context를 거치지 않는다.
        _skill = SkillManager.Instance;

        if (_skill == null)
        {
            Debug.LogWarning($"[{nameof(SkillUsedCondition)}] SkillManager가 없어 이 단계를 넘어갈 수 없다.");

            return;
        }

        _skill.ImpactResolved += OnImpactResolved;
    }

    public override void End()
    {
        if (_skill != null)
        {
            _skill.ImpactResolved -= OnImpactResolved;
            _skill = null;
        }
    }

    private void OnImpactResolved(SkillCastContext context)
    {
        _casted++;

        if (_casted >= count)
        {
            Fire();
        }
    }
}
