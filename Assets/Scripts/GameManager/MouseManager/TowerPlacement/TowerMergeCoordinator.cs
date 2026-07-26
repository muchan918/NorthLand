using System;
using System.Collections.Generic;
using UnityEngine;
using NorthLand.Combat;

/// 타워 합성 선택의 두뇌(#183). MouseManager 입력을 받아 선택 재료 집합(TowerMergeGroup)을 소유·관리하고,
/// 낮 게이팅·밤/파괴 리셋·월드 하이라이트·우측 패널 스왑(인포↔합성)을 담당하며, 합성 실행을 오케스트레이션한다.
/// 패널 뷰(TowerMergePanelView)는 이 코디네이터만 참조한다(파사드). Docs/Core/TowerMerge.md §7·§8·§10.
///
/// MouseManager는 마커(IGroupSelectable) 유무만 알고 "타워"를 모른다 — 도메인 지식은 여기 코디네이터가 가진다.
public class TowerMergeCoordinator : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private TowerFusionController _controller;
    [SerializeField] private GameObject _mergePanel; // 2개 이상 선택 시 켜지는 합성 패널 루트

    // 선택 재료 집합 — 코디네이터가 유일하게 소유하는 순수 C# 홀더(씬 오브젝트 아님). 실행부에 인자로 넘긴다.
    private readonly TowerMergeGroup _group = new();

    // 현재 하이라이트된 타워(진입 경로 무관 diff용). 파괴된 참조는 RefreshHighlight에서 안전 정리.
    private readonly HashSet<Tower> _highlighted = new();

    // ── 파사드 (패널 뷰가 쓰는 것) ────────────────────────────────────
    public IReadOnlyList<Tower> SelectedTowers => _group.Towers;
    /// 선택 집합이 바뀔 때 발행(패널 뷰가 구독해 리스트·후보 버튼을 갱신).
    public event Action OnGroupChanged;
    public bool CanMerge(TowerRecipe recipe) => TowerFusionMatcher.CanFuse(_group.Towers, recipe);

    /// 후보 버튼 onClick → 이 진입점. 현재 선택 집합으로 합성 실행을 시도한다(밤엔 무시).
    public void RequestMerge(TowerRecipe recipe)
    {
        if (recipe == null) return;
        if (!IsDay) return; // 방어(패널도 밤엔 숨김)
        if (_controller == null) { Debug.LogError("[TowerMerge] TowerFusionController가 연결되지 않았습니다."); return; }
        _controller.TryFuse(recipe, _group);
    }

    // ── 수명주기 ──────────────────────────────────────────────────────
    private void Start()
    {
        // 다른 싱글톤의 Awake가 끝난 뒤 구독(PhasePanelSwitcher와 동일 패턴).
        var mm = MouseManager.Instance;
        if (mm != null)
        {
            mm.OnSelectionChanged += HandleSelectionChanged;
            mm.OnGroupSelectToggled += HandleGroupToggle;
        }
        else Debug.LogWarning("[TowerMerge] MouseManager가 씬에 없어 합성 선택이 비활성입니다.");

        if (DayNightManager.Instance != null)
            DayNightManager.Instance.OnDayToNight += HandleDayToNight;

        Tower.ActiveChanged += HandleActiveChanged; // 외부 파괴(철거·사망) 시 stale 방어(WL-076b)
        _group.OnChanged += HandleGroupChanged;

        if (_mergePanel != null) _mergePanel.SetActive(false); // 초기 숨김
    }

    private void OnDestroy()
    {
        // MouseManager/Tower는 씬보다 오래 살 수 있어(DontDestroyOnLoad/static) 반드시 해제(F7).
        var mm = MouseManager.Instance;
        if (mm != null)
        {
            mm.OnSelectionChanged -= HandleSelectionChanged;
            mm.OnGroupSelectToggled -= HandleGroupToggle;
        }
        if (DayNightManager.Instance != null)
            DayNightManager.Instance.OnDayToNight -= HandleDayToNight;
        Tower.ActiveChanged -= HandleActiveChanged;
        _group.OnChanged -= HandleGroupChanged;
    }

    // ── 입력 핸들러 ───────────────────────────────────────────────────
    // 평클릭: 타워면 그 타워로 단일 리셋, 그 외(건물/빈 곳)면 집합 해제. 밤엔 무시.
    private void HandleSelectionChanged(ISelectable sel)
    {
        if (!IsDay) return;
        if (sel is Tower tower)
        {
            _group.Clear();
            _group.Add(tower);
        }
        else
        {
            _group.Clear();
        }
    }

    // Shift+마커: 집합 토글(있으면 제거, 없으면 끝에 추가). 밤엔 무시.
    private void HandleGroupToggle(IGroupSelectable grp)
    {
        if (!IsDay) return;
        Tower t = grp?.Tower;
        if (t == null) return;
        if (_group.Contains(t)) _group.Remove(t);
        else _group.Add(t);
    }

    // 밤 진입: 집합 리셋 + 진행 중이던 합성 고스트 배치도 취소(F5 — 확정 시 재료 파괴 방지).
    private void HandleDayToNight()
    {
        _group.Clear();
        MouseManager.Instance?.CancelPlacement();
    }

    // 타워가 씬에서 빠지면(철거·사망·합성 소모) 죽은 참조 정리. Prune이 실제로 지우면 OnChanged→갱신 연쇄.
    private void HandleActiveChanged() => _group.Prune();

    // 집합 변경 단일 통지 경로(선택 토글/리셋/소모/Prune 전부 여기로 수렴).
    private void HandleGroupChanged()
    {
        RefreshHighlight();
        RefreshPanel();
        OnGroupChanged?.Invoke();
    }

    // ── 하이라이트 / 패널 ─────────────────────────────────────────────
    private void RefreshHighlight()
    {
        // 새로 들어온 타워는 하이라이트 on(마커 훅). 파괴된 참조는 건너뛴다.
        foreach (var t in _group.Towers)
        {
            if (t == null) continue;
            if (_highlighted.Add(t)) GetMarker(t)?.OnGroupSelected();
        }
        // 집합에서 빠졌거나 파괴된 타워는 하이라이트 off + 추적 해제.
        _highlighted.RemoveWhere(t =>
        {
            if (t != null && _group.Contains(t)) return false; // 유지
            if (t != null) GetMarker(t)?.OnGroupDeselected();
            return true;
        });
    }

    // 우측 패널 단일 권위(F1). 0=둘 다 숨김 / 1=인포(멤버 OnSelected 재사용) / 2개↑=합성 패널.
    private void RefreshPanel()
    {
        int count = _group.Count;
        if (count >= 2)
        {
            TowerInfoUI.Instance?.HideInfo();
            if (_mergePanel != null) _mergePanel.SetActive(true);
        }
        else
        {
            if (_mergePanel != null) _mergePanel.SetActive(false);
            if (count == 1)
            {
                // 2→1 복귀 시 단일 선택 OnSelected가 재발화되지 않으므로 스위처가 명시적으로 인포를 표시.
                // 표시는 idempotent라 평클릭 경로(MouseManager가 이미 OnSelected 호출)와 겹쳐도 무해.
                var t = _group.Towers[0];
                if (t != null) t.OnSelected();
            }
            else // count == 0
            {
                TowerInfoUI.Instance?.HideInfo();
            }
        }
    }

    private static IGroupSelectable GetMarker(Tower t)
        => t.TryGetComponent(out IGroupSelectable g) ? g : null;

    private static bool IsDay =>
        DayNightManager.Instance == null ||
        DayNightManager.Instance.CurrentPhase == DayNightManager.Phase.Day;
}
