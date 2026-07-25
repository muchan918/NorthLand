using System;
using System.Collections.Generic;
using UnityEngine;
using NorthLand.Combat;

/// 타워 합성(#195)의 재료 후보를 담는 임시 홀더.
/// 지금은 선택 UI(#183, 타 담당) 대신 인스펙터에 씬 타워를 드래그해 채운다.
/// 나중에 실제 선택 시스템이 이 리스트를 채우도록 교체하면 합성 실행부(TowerFusionController)는 그대로 둔다.
public class TowerWallet : MonoBehaviour
{
    [Header("합성 재료 (씬 타워를 드래그)")]
    [SerializeField] private List<Tower> _towers = new List<Tower>();

    public IReadOnlyList<Tower> Towers => _towers;

    /// 지갑 내용(재료 선택)이 바뀔 때 발행. 후보 버튼(#183)이 활성 상태를 갱신하는 데 구독한다.
    public event Action OnChanged;

    public void Add(Tower tower)
    {
        if (tower == null || _towers.Contains(tower)) return;
        _towers.Add(tower);
        OnChanged?.Invoke();
    }

    public bool Remove(Tower tower)
    {
        if (!_towers.Remove(tower)) return false;
        OnChanged?.Invoke();
        return true;
    }

    public void Clear()
    {
        if (_towers.Count == 0) return;
        _towers.Clear();
        OnChanged?.Invoke();
    }
}
