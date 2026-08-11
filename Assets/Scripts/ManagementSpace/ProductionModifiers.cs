using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 자원 종류별 <b>생산량 배율</b> 레지스트리(순수 C#). 패시브 생산 효과(생산량 +X%)가 여기에 등록되고,
/// <see cref="ManagementController"/>가 정산(<see cref="ResourceProductionSource.Produce"/>)·예상치 표시 시 조회한다.<br/>
/// 영토 패시브 효과가 유일한 등록처였으나 영토 시스템 제거(#337)로 현재 등록처는 없다 —
/// 생산 규칙 확장 심으로 그대로 남겨 둔다(기본 배율 1.0이라 동작에는 영향 없음).<br/>
/// <br/>
/// ⚠ <b>다음 소비자(보상 시스템 등)가 이 심을 쓰려면 두 가지를 함께 해야 한다.</b> 지금은
/// <see cref="ManagementController"/>가 인스턴스를 <c>private</c>으로만 들고 있어 <b>등록처가 0인 게 아니라
/// 등록 자체가 불가능한</b> 상태다.<br/>
/// ① <c>ManagementController</c>에 <c>RegisterProductionMultiplier(kind, multiplier)</c> 같은 public 창구를 연다.<br/>
/// ② <b>세이브 복원 경로에서 재등록한다</b>(<c>TryRestoreRewardEffects</c> 이후). 이 레지스트리는
///    <c>BuildModel</c>에서 런마다 새로 만들어지고 배율은 세이브에 없다 — 재등록을 빠뜨리면 보상 레벨은
///    복원되는데 효과만 사라져 "이어하기 했더니 생산량이 다르다"가 된다.<br/>
/// <br/>
/// 스택 규칙: <b>곱셈 누적</b>. 같은 자원에 +10%가 두 번 등록되면 1.1 × 1.1 = 1.21배가 된다(순서 무관).
/// 기본 배율은 1.0(효과 없음). 런(run)마다 <see cref="ManagementController"/>가 새로 만들어 초기화한다.<br/>
/// <br/>
/// 이 레지스트리는 "생산 규칙"의 확장 심일 뿐, 자원 잔액(<see cref="ResourceWallet"/>)과는 분리돼 있다.
/// </summary>
public class ProductionModifiers
{
    private readonly Dictionary<ResourceKind, float> _multipliers = new();

    /// <summary>해당 자원의 현재 생산 배율. 등록된 게 없으면 1.0.</summary>
    public float GetMultiplier(ResourceKind kind) =>
        _multipliers.TryGetValue(kind, out float m) ? m : 1f;

    /// <summary>배율을 곱셈으로 누적한다. (예: 1.1을 넣으면 기존 배율에 ×1.1)</summary>
    public void AddMultiplier(ResourceKind kind, float multiplier)
    {
        if (multiplier <= 0f)
        {
            Debug.LogError($"[생산배율] 0 이하 배율은 등록할 수 없습니다: {kind} ×{multiplier}");
            return;
        }

        _multipliers[kind] = GetMultiplier(kind) * multiplier;
    }
}
