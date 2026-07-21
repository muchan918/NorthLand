using UnityEngine;

/// <summary>
/// 즉시 효과 — 확보 시 지정 자원을 정해진 수량만큼 1회 지급한다(예: 나무 20, 마나석 3).<br/>
/// 마나석(<see cref="ResourceKind.Mana"/>) 지급도 이 효과로 표현한다 — 영토 확장은 마나석의 정당한 원천(팀 계약 #3).
/// </summary>
[CreateAssetMenu(fileName = "GrantResourceEffect", menuName = "Scriptable Objects/Territory/Grant Resource Effect")]
public class GrantResourceEffect : TerritoryEffect
{
    [Tooltip("지급할 자원 종류")]
    [SerializeField] ResourceKind _kind = ResourceKind.Wood;

    [Tooltip("지급 수량(0 이상). 수치는 이 SO에서 직접 authoring한다(CSV 아님).")]
    [SerializeField, Min(0)] int _amount = 10;

    public override void Apply(in TerritoryEffectContext ctx)
    {
        if (ctx.Wallet == null)
        {
            Debug.LogError($"[영토] 지갑이 없어 자원({_kind} +{_amount})을 지급할 수 없습니다.", this);
            return;
        }

        ctx.Wallet.Add(_kind, _amount);
    }
}
