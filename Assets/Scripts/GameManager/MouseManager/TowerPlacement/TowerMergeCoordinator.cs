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
    [SerializeField] TowerFusionController _controller;
    [SerializeField] GameObject _mergePanel; // 2개 이상 선택 시 켜지는 합성 패널 루트

    // 선택 재료 집합 — 코디네이터가 유일하게 소유하는 순수 C# 홀더(씬 오브젝트 아님). 실행부에 인자로 넘긴다.
    private readonly TowerMergeGroup _group = new();

    // 현재 하이라이트된 타워(진입 경로 무관 diff용). 파괴된 참조는 RefreshHighlight에서 안전 정리.
    private readonly HashSet<Tower> _highlighted = new();

    // 후보 버튼 호버 프리뷰로 핑크가 켜진 타워(#213 §5.3). 그룹 하이라이트와 같은 diff 패턴.
    //
    // 핑크는 **호버 동안만** 켜진다. 예전에는 버튼을 클릭한 뒤 배치가 끝날 때까지 고정하는 잠금
    // (_previewCommitted)이 있었는데, 클릭 순간 커서가 버튼을 벗어나면서 "무엇이 소모되는지"가
    // 사라지는 것을 막기 위한 것이었다. #263이 소모를 클릭 시점으로 앞당기면서 **칠할 대상 자체가
    // 그 순간 사라지므로** 잠금이 의미를 잃었다 — 무엇이 소모됐는지는 재료가 없어진 자리가 말해준다.
    private readonly HashSet<Tower> _previewed = new();

    // RefreshPanel이 인포를 띄워준 타워(= OnSelected를 직접 호출한 대상). 대칭 OnDeselected를 부를 상대를
    // 기억해 두는 1개짜리 diff 슬롯 — _highlighted HashSet과 같은 패턴의 축소판이다(WL-087).
    // 이게 없으면 OnSelected에 묶인 부수 표시(정보 패널 + **사거리 원**)에서 원만 잔존한다:
    // RefreshPanel은 TowerInfoUI.HideInfo()밖에 모르고, Shift 경로는 MouseManager._selected를 안 건드려
    // 나중의 Select(null)도 그 타워의 OnDeselected를 부르지 못하기 때문.
    private Tower _infoShownFor;

    // ── 드래그 사각형 선택(#261) ──────────────────────────────────────
    // 드래그 중에는 하이라이트만 실시간으로 따라가고 **패널 갱신은 유예**한다. 사각형을 넓히다가 2개를
    // 넘나들면 합성 패널이 열렸다 닫혔다 하고, 후보 버튼을 매 프레임 재생성해 GC까지 튄다.
    // 유예분은 드래그 종료 시 한 번에 처리한다.
    private bool _boxDragging;
    private readonly List<Tower> _dragBase = new();   // 드래그 시작 시점의 기준 집합(Shift면 유지, 아니면 빈 집합)
    private readonly List<Tower> _dragResult = new(); // 기준 + 사각형 내용, 매 갱신마다 재구성

    // ── 파사드 (패널 뷰가 쓰는 것) ────────────────────────────────────
    public IReadOnlyList<Tower> SelectedTowers => _group.Towers;
    /// 선택 집합이 바뀔 때 발행(패널 뷰가 구독해 리스트·후보 버튼을 갱신).
    public event Action OnGroupChanged;
    public bool CanMerge(TowerRecipe recipe) => TowerFusionMatcher.CanFuse(_group.Towers, recipe);

    /// 후보 버튼 **호버** → 이 진입점(#213 §5.3). 그 레시피가 실제로 소모할 재료 타워만 핑크로 바꾼다.
    /// 포함 매칭이라 선택 집합에 여분 타워가 있을 수 있으므로 **소모 대상만** 칠하고 여분은 초록을 유지한다.
    /// 판정은 실행부와 같은 출처(TowerFusionMatcher)를 쓰고, 인덱스가 어긋나지 않도록
    /// TowerFusionController.TryFuse와 **똑같이** null/Asset 없는 타워를 걸러낸 리스트로 맞춘다.
    public void PreviewMerge(TowerRecipe recipe)
    {
        ApplyPreview(ResolveConsumeTargets(recipe)); // 매칭 불가(null)면 그대로 해제 — 버튼이 꺼져 있어야 정상
    }

    /// 이 레시피가 현재 집합에서 **실제로 소모할** 타워들. 매칭 불가·밤이면 null.
    private HashSet<Tower> ResolveConsumeTargets(TowerRecipe recipe)
    {
        if (recipe == null || !IsDay) return null;

        var towers = new List<Tower>();
        var towerIds = new List<string>();
        foreach (var t in _group.Towers)
        {
            if (t == null || t.Asset == null) continue;
            towers.Add(t);
            towerIds.Add(t.Asset.TowerID);
        }

        var required = TowerFusionMatcher.BuildRequired(recipe);
        if (required.Count == 0 || !TowerFusionMatcher.TryResolve(towerIds, required, out var consumeIndices))
            return null;

        var next = new HashSet<Tower>();
        foreach (int i in consumeIndices) next.Add(towers[i]);
        return next;
    }

    /// 커서가 후보 버튼을 벗어남·버튼 비활성·집합 변경·밤 전환 시 프리뷰를 해제한다(잔존 방지).
    public void ClearMergePreview() => ApplyPreview(null);

    private void ApplyPreview(HashSet<Tower> next)
    {
        // 빠진 대상만 끈다(남는 대상은 아래에서 다시 true — Set은 멱등이라 무해).
        foreach (var t in _previewed)
        {
            if (t == null) continue;
            if (next == null || !next.Contains(t)) SetPreview(t, false);
        }
        _previewed.Clear();

        if (next == null) return;

        foreach (var t in next)
        {
            SetPreview(t, true);
            _previewed.Add(t);
        }
    }

    private static void SetPreview(Tower tower, bool on)
    {
        if (tower == null) return;
        OutlineHighlight.GetOrAdd(tower.gameObject).Set(OutlineKind.MergePreview, on);
    }

    /// 후보 버튼 onClick → 이 진입점. 현재 선택 집합으로 합성 실행을 시도한다(밤엔 무시).
    public void RequestMerge(TowerRecipe recipe)
    {
        if (recipe == null) return;
        if (!IsDay) return; // 방어(패널도 밤엔 숨김)
        if (_controller == null) { Debug.LogError("[TowerMerge] TowerFusionController가 연결되지 않았습니다."); return; }

        // 소모 대상을 미리 스냅샷해 둘 필요가 없다(#263). 예전에는 TryFuse가 배치를 시작하며 선택 집합을
        // 비우기 전에 "무엇을 칠할지"를 확보해야 했지만, 이제 그 재료들은 클릭과 동시에 사라진다.
        _controller.TryFuse(recipe, _group);
    }

    // ── 수명주기 ──────────────────────────────────────────────────────
    private void Start()
    {
        // 다른 싱글톤의 Awake가 끝난 뒤 구독(PhasePanelSwitcher와 동일 패턴).
        var mm = MouseManager.Instance;
        if (mm != null)
        {
            mm.OnPrimarySelect += HandlePrimarySelect;
            mm.OnGroupSelectToggled += HandleGroupToggle;
            mm.OnBoxSelectBegin += HandleBoxSelectBegin;
            mm.OnBoxSelectUpdate += HandleBoxSelectUpdate;
            mm.OnBoxSelectEnd += HandleBoxSelectEnd;
        }
        else
        {
            Debug.LogWarning("[TowerMerge] MouseManager가 씬에 없어 합성 선택이 비활성입니다.");
        }

        if (DayNightManager.Instance != null)
        {
            DayNightManager.Instance.OnDayToNight += HandleDayToNight;
        }

        Tower.ActiveChanged += HandleActiveChanged; // 외부 파괴(철거·사망) 시 stale 방어(WL-076b)
        _group.OnChanged += HandleGroupChanged;

        if (_mergePanel != null)
        {
            _mergePanel.SetActive(false); // 초기 숨김
        }
    }

    private void OnDestroy()
    {
        // MouseManager/Tower는 씬보다 오래 살 수 있어(DontDestroyOnLoad/static) 반드시 해제(F7).
        var mm = MouseManager.Instance;
        if (mm != null)
        {
            mm.OnPrimarySelect -= HandlePrimarySelect;
            mm.OnGroupSelectToggled -= HandleGroupToggle;
            mm.OnBoxSelectBegin -= HandleBoxSelectBegin;
            mm.OnBoxSelectUpdate -= HandleBoxSelectUpdate;
            mm.OnBoxSelectEnd -= HandleBoxSelectEnd;
        }

        if (DayNightManager.Instance != null)
        {
            DayNightManager.Instance.OnDayToNight -= HandleDayToNight;
        }
        Tower.ActiveChanged -= HandleActiveChanged;
        _group.OnChanged -= HandleGroupChanged;
    }

    // ── 입력 핸들러 ───────────────────────────────────────────────────
    // 평클릭(추가키 없음)·Esc·빈 곳: MouseManager.OnPrimarySelect가 _selected 중복 제거와 무관하게 항상 발행.
    // 타워면 그 타워로 단일 리셋(SetSingle=원자 1회 통지), 그 외(건물/빈 곳/null)면 집합 해제. 밤엔 무시.
    private void HandlePrimarySelect(ISelectable sel)
    {
        if (!IsDay) return;
        if (sel is Tower tower) _group.SetSingle(tower);
        else _group.Clear();
    }

    // Shift+마커: 집합 토글(있으면 제거, 없으면 끝에 추가). 밤엔 무시.
    private void HandleGroupToggle(IGroupSelectable grp)
    {
        if (!IsDay) return;

        // 마커는 도메인 중립(Tower를 노출하지 않음) — 타워 해석은 도메인을 아는 코디네이터가 캐스팅으로 한다.
        Tower t = grp is TowerGroupSelectable tgs ? tgs.Tower : null;
        if (t == null) return;

        if (_group.Contains(t))
        {
            _group.Remove(t);
        }
        else
        {
            _group.Add(t);
        }
    }

    // ── 드래그 사각형 선택(#261) ──────────────────────────────────────
    // 시작: 기준 집합을 스냅샷한다. Shift면 기존 집합을 유지한 채 합집합으로 더하고(추가), 아니면 교체다.
    // Shift 클릭이 **토글**인 것과 달리 Shift 드래그는 **합집합**이다(의도된 비대칭, 명세 §4.1).
    private void HandleBoxSelectBegin(bool additive)
    {
        if (!IsDay) return;

        _boxDragging = true;
        _dragBase.Clear();
        if (additive) _dragBase.AddRange(_group.Towers);
    }

    // 갱신: 기준 + 사각형 내용(들어온 순서)으로 집합을 원자 교체. 안 바뀐 프레임은 SetAll이 no-op으로 흡수한다.
    private void HandleBoxSelectUpdate(IReadOnlyList<IGroupSelectable> hits)
    {
        if (!IsDay || !_boxDragging) return;

        _dragResult.Clear();

        // 기준 집합은 드래그 **시작 시점**의 사본이라 그새 사라진 타워(합성 소모·철거·사망)가 섞일 수 있다.
        // 그대로 되넣으면 (1) Prune이 방금 지운 타워가 되살아나고 (2) TowerMergeGroup.SameAs가 정규화 전
        // 리스트와 비교하는 탓에 매 갱신마다 OnChanged가 헛발행된다(WL-145). 판정 출처는 HandleActiveChanged와 동일.
        foreach (var t in _dragBase)
        {
            if (t == null || !Tower.Active.Contains(t)) continue;
            _dragResult.Add(t);
        }

        for (int i = 0; i < hits.Count; i++)
        {
            // 마커는 도메인 중립이라 타워 해석은 여기서 한다(Shift 토글 경로와 같은 규칙).
            Tower t = hits[i] is TowerGroupSelectable tgs ? tgs.Tower : null;
            if (t == null || _dragResult.Contains(t)) continue;
            _dragResult.Add(t);
        }

        _group.SetAll(_dragResult);
    }

    // 종료: 유예했던 패널·후보 버튼 갱신을 여기서 한 번 처리한다.
    private void HandleBoxSelectEnd()
    {
        if (!_boxDragging) return;

        _boxDragging = false;
        _dragBase.Clear();
        _dragResult.Clear();

        RefreshPanel();
        OnGroupChanged?.Invoke();
    }

    // 밤 진입: 선택 집합만 리셋. 진행 중 배치 취소(F5)는 페이즈 취소 책임 일원화로 PhasePanelSwitcher.ShowNight가
    // 담당한다(낮 진입 스킬 조준 취소와 대칭) — 코디네이터는 전역 CancelPlacement를 더 이상 호출하지 않는다.
    private void HandleDayToNight()
    {
        // 드래그 도중 밤이 되면 이후 통지가 IsDay 게이팅에 막혀 종료 신호를 못 받는다 → 유예 상태를 직접 푼다.
        _boxDragging = false;
        _dragBase.Clear();
        _dragResult.Clear();
        ClearMergePreview(); // 패널이 닫히면 OnPointerExit가 오지 않을 수 있다
        _group.Clear();
    }

    // 타워가 씬에서 빠지면(철거·사망·합성 소모) 죽은 참조 정리. Tower.OnDisable→ActiveChanged 시점엔 아직
    // Unity 가짜 null이 아니라(파괴 완료 후부터 null) t==null만으론 못 거른다 → 이미 Active.Remove가 끝난
    // Tower.Active 멤버십으로 판정한다. 실제로 지우면 OnChanged→갱신 연쇄.
    private void HandleActiveChanged() => _group.Prune(t => t == null || !Tower.Active.Contains(t));

    // 집합 변경 단일 통지 경로(선택 토글/리셋/소모/Prune 전부 여기로 수렴).
    private void HandleGroupChanged()
    {
        ClearMergePreview(); // 집합이 바뀌면 프리뷰의 근거(소모 대상)가 사라진다
        RefreshHighlight();  // 하이라이트는 드래그 중에도 실시간 — 지금 무엇이 잡히는지가 보여야 한다

        if (_boxDragging) return; // 패널·후보 버튼은 유예, 드래그 종료 시 한 번에(HandleBoxSelectEnd)

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

            if (_highlighted.Add(t))
            {
                GetMarker(t)?.OnGroupSelected();
            }
        }
        // 집합에서 빠졌거나 파괴된 타워는 하이라이트 off + 추적 해제.
        _highlighted.RemoveWhere(t =>
        {
            if (t != null && _group.Contains(t)) return false; // 유지
            if (t != null)
            {
                GetMarker(t)?.OnGroupDeselected();
            }
            return true;
        });
    }

    // 우측 패널 단일 권위(F1). 0=둘 다 숨김 / 1=인포(멤버 OnSelected 재사용) / 2개↑=합성 패널.
    // OnSelected/OnDeselected를 **쌍으로** 호출한다 — 이 훅에는 정보 패널만이 아니라 사거리 원(#192)도
    // 묶여 있어서, 켜기만 하고 끄지 않으면 합성 패널 위에 직전 타워의 초록 원이 남는다(WL-087).
    private void RefreshPanel()
    {
        int count = _group.Count;
        Tower single = count == 1 ? _group.Towers[0] : null;

        // 인포 대상이 바뀌거나 사라지면 이전 대상을 먼저 내린다(정보 패널 + 사거리 원).
        // 파괴된 참조는 Unity 오버로드 ==로 걸러 MissingReferenceException을 피한다(WL-033 계열).
        if (_infoShownFor != null && _infoShownFor != single) _infoShownFor.OnDeselected();
        _infoShownFor = single;

        if (count >= 2)
        {
            TowerInfoUI.Instance?.HideInfo();
            if (_mergePanel != null) _mergePanel.SetActive(true);
        }
        else
        {
            if (_mergePanel != null) _mergePanel.SetActive(false);
            if (single != null)
            {
                // 2→1 복귀 시 단일 선택 OnSelected가 재발화되지 않으므로 스위처가 명시적으로 인포를 표시.
                // 표시는 idempotent라 평클릭 경로(MouseManager가 이미 OnSelected 호출)와 겹쳐도 무해.
                single.OnSelected();
            }
            else // count == 0 (또는 멤버가 파괴돼 표시할 게 없음)
            {
                TowerInfoUI.Instance?.HideInfo();
            }
        }
    }

    private static IGroupSelectable GetMarker(Tower t)
    {
        return t.TryGetComponent(out IGroupSelectable g) ? g : null;
    }

    private static bool IsDay => (
        DayNightManager.Instance == null ||
        DayNightManager.Instance.CurrentPhase == DayNightManager.Phase.Day
    );
}
