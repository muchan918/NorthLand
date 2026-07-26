using System;
using System.Collections.Generic;
using NorthLand.Combat;

/// 타워 합성(#183)에서 선택된 재료 타워의 순서 있는 집합. 순수 C# — 씬 오브젝트가 아니라
/// TowerMergeCoordinator가 인스턴스를 소유한다(컨벤션 §6: MonoBehaviour 얇게, 로직 순수 C#).
/// #195가 임시로 쓰던 TowerWallet("지갑" 어감이 자원 보관이라 부적합)을 대체하는 이음매 —
/// 실행부(TowerFusionController)·매칭(TowerFusionMatcher)이 이 집합의 Towers를 소비한다.
/// (네이밍: 코드는 합성을 향후 Merge로 통합 예정 — 신규 클래스는 Merge 접두로 시작. Docs/Core/TowerMerge.md §3)
public class TowerMergeGroup
{
    private readonly List<Tower> _towers = new();

    /// 선택된 순서대로의 재료 타워(등록 순서 = 선택 순서).
    public IReadOnlyList<Tower> Towers => _towers;

    public int Count => _towers.Count;

    /// 집합이 바뀔 때(Add/Remove/Clear/Prune 성공 시) 발행. 코디네이터가 구독해 패널·하이라이트를 갱신한다.
    /// 합성 실행부가 재료를 소모하며 Remove를 부를 때도 이 이벤트로 UI가 자동 갱신된다(단일 통지 경로).
    public event Action OnChanged;

    public bool Contains(Tower tower) => tower != null && _towers.Contains(tower);

    /// 집합 끝에 추가(중복·null 무시). 추가됐으면 true.
    public bool Add(Tower tower)
    {
        if (tower == null || _towers.Contains(tower)) return false;
        _towers.Add(tower);
        OnChanged?.Invoke();
        return true;
    }

    /// 집합에서 제거(나머지 순서 유지). 제거됐으면 true.
    public bool Remove(Tower tower)
    {
        if (!_towers.Remove(tower)) return false;
        OnChanged?.Invoke();
        return true;
    }

    /// 전체 비움. 하나라도 있었으면 true.
    public bool Clear()
    {
        if (_towers.Count == 0) return false;
        _towers.Clear();
        OnChanged?.Invoke();
        return true;
    }

    /// 집합을 tower 하나로 원자 교체(전부 비우고 하나만). OnChanged 1회 — 평클릭 단일화가 Clear+Add로
    /// 통지를 두 번 쏘던 것을 대체(불필요한 패널 재빌드·GC 방지). 이미 그 단일이면 no-op(false).
    public bool SetSingle(Tower tower)
    {
        if (tower == null) return Clear();
        if (_towers.Count == 1 && _towers[0] == tower) return false;
        _towers.Clear();
        _towers.Add(tower);
        OnChanged?.Invoke();
        return true;
    }

    /// 죽은 항목을 주입된 판정(isDead)으로 제거한다. 하나라도 지우면 OnChanged. (WL-076b)
    /// 판정을 인자로 받는 이유: 파괴 완료 후에야 Unity 가짜 null(t==null)이 되지만, Tower.OnDisable→
    /// ActiveChanged 시점엔 아직 null이 아니다. 그 시점을 잡으려면 Tower.Active 멤버십 등 도메인 판정이
    /// 필요한데, 그 지식은 소유자(코디네이터)에게 있으므로 이 순수 홀더는 판정을 위임받아 실행만 한다.
    public bool Prune(System.Predicate<Tower> isDead)
    {
        if (isDead == null || _towers.RemoveAll(isDead) == 0) return false;
        OnChanged?.Invoke();
        return true;
    }
}
