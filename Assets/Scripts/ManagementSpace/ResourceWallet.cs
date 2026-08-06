using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 경영 시스템의 자원 상태 저장소이자 단일 게이트웨이.<br/>
/// 4종 자원(<see cref="ResourceKind"/>)의 현재 보유량을 런타임 상태로 보관하고,
/// 획득/소비/질의만 담당한다. "왜 벌고 쓰는가"(생산 규칙·비용 규칙)는 경계 밖 시스템의 책임이다.<br/>
/// <br/>
/// (Docs/ManagementArea/Resources.md — 팀 계약 #3·#6)
/// </summary>
public class ResourceWallet
{
    private readonly Dictionary<ResourceKind, int> _balances = new Dictionary<ResourceKind, int>();

    /// <summary>보유량이 바뀔 때 발행된다. (자원 종류, 변경 후 값)</summary>
    public event Action<ResourceKind, int> OnChanged;

    public ResourceWallet()
    {
        // 모든 자원 종류를 0으로 초기화해 이후 질의에서 키 부재를 신경 쓰지 않게 한다.
        foreach (ResourceKind kind in Enum.GetValues(typeof(ResourceKind)))
        {
            _balances[kind] = 0;
        }
    }

    /// <summary>현재 보유량.</summary>
    public int Get(ResourceKind kind) => _balances[kind];

    /// <summary>해당 비용을 감당 가능한지 판정한다. (amount는 0 이상 가정)</summary>
    public bool CanAfford(ResourceKind kind, int amount) => _balances[kind] >= amount;

    /// <summary>자원을 획득한다.</summary>
    public void Add(ResourceKind kind, int amount)
    {
        if (amount < 0)
        {
            Debug.LogError($"자원 획득량은 0 또는 음수일 수 없습니다: {kind} {amount}");
            return;
        }

        if (amount == 0)
        {
            return;
        }

        _balances[kind] += amount;
        OnChanged?.Invoke(kind, _balances[kind]);
    }

    /// <summary>자원을 차감한다.<br/> 보유량이 부족하면 차감하지 않고 false를 반환한다.</summary>
    public bool TrySpend(ResourceKind kind, int amount)
    {
        if (amount < 0)
        {
            Debug.LogError($"자원 소비량은 음수일 수 없습니다: {kind} {amount}");
            return false;
        }

        if (amount == 0)
        {
            return true;
        }

        if (!CanAfford(kind, amount))
        {
            Debug.LogError($"자원 부족: {kind} 보유 {_balances[kind]} < 요청 {amount}");
            return false;
        }

        _balances[kind] -= amount;
        OnChanged?.Invoke(kind, _balances[kind]);
        return true;
    }

    /// <summary>
    /// 자원 보유량을 지정한 절대값으로 변경한다.
    /// 음수는 허용하지 않는다.
    /// </summary>
    public bool TrySet(ResourceKind kind, int amount)
    {
        if (amount < 0)
        {
            Debug.LogError($"자원 보유량은 음수일 수 없습니다: {kind} {amount}");

            return false;
        }

        if (_balances[kind] == amount)
            return true;

        _balances[kind] = amount;
        OnChanged?.Invoke(kind, amount);

        return true;
    }
}
