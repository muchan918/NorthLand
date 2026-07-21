using UnityEngine;

/// <summary>
/// 즉시 효과 — 확보 시 주민 수를 늘린다(GDD §5.1 "영토 확장으로 주민 풀 증가").<br/>
/// ⚠ 주민 시스템이 아직 없어 실제 반영은 <see cref="TerritoryEffectContext.GainResidents"/> 심에서 보류된다(§6.1·§9 placeholder).
/// 효과 SO 자체는 완성형이며, 주민 시스템 도입 시 심 구현만 채우면 바로 동작한다.
/// </summary>
[CreateAssetMenu(fileName = "GainResidentEffect", menuName = "Scriptable Objects/Territory/Gain Resident Effect")]
public class GainResidentEffect : TerritoryEffect
{
    [Tooltip("증가시킬 주민 수(1 이상). 수치는 이 SO에서 직접 authoring한다.")]
    [SerializeField, Min(1)] int _count = 1;

    public override void Apply(in TerritoryEffectContext ctx)
    {
        ctx.GainResidents(_count);
    }
}
