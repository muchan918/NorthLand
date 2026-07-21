using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 자원 종류별 <b>생산량 배율</b> 레지스트리(순수 C#). 영토 패시브 효과(생산량 +X%)가 여기에 등록되고,
/// <see cref="ManagementController"/>가 정산(<see cref="ResourceProductionSource.Produce"/>)·예상치 표시 시 조회한다.<br/>
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
