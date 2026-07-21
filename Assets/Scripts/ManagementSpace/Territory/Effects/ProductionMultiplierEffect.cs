using UnityEngine;

/// <summary>
/// 패시브 효과 — 지정 자원의 생산량을 영구히 +X% 한다(예: 나무 생산량 +15%).<br/>
/// 확보 시 <see cref="TerritoryEffectContext.AddProductionMultiplier"/>로 <see cref="ProductionModifiers"/>에
/// 배율을 곱셈 누적 등록한다. 이후 <see cref="ManagementController"/>가 정산·예상치 계산 시 이 배율을 반영한다.
/// </summary>
[CreateAssetMenu(fileName = "ProductionMultiplierEffect", menuName = "Scriptable Objects/Territory/Production Multiplier Effect")]
public class ProductionMultiplierEffect : TerritoryEffect
{
    [Tooltip("생산 배율을 올릴 자원 종류")]
    [SerializeField] ResourceKind _kind = ResourceKind.Wood;

    [Tooltip("생산량 증가율(%). 예: 15 = +15%. 수치는 이 SO에서 직접 authoring한다.")]
    [SerializeField, Min(0)] float _percent = 10f;

    /// <summary>생산 배율(1.0 기준). +15%면 1.15.</summary>
    public float Multiplier => 1f + _percent / 100f;

    public ResourceKind Kind => _kind;

    public override void Apply(in TerritoryEffectContext ctx)
    {
        ctx.AddProductionMultiplier(_kind, Multiplier);
    }
}
