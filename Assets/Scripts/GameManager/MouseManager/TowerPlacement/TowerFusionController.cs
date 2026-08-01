using System.Collections.Generic;
using UnityEngine;
using NorthLand.Combat;

/// 타워 합성(#195) 실행부. 후보 버튼 클릭 → 그룹 재료 매칭 → 코스트 확인 →
/// **재료 즉시 소모(커맨드 Execute)** → TowerPlacer 고스트 배치 시작 →
/// 확정이면 커맨드 Commit(진짜 파괴), 취소면 Undo(원복).
/// 선택 집합(TowerMergeGroup)은 TowerMergeCoordinator가 소유하며, 실행 시 인자로 넘겨받는다(#183).
/// (구 임시 홀더 TowerWallet + 테스트 단일 레시피 경로는 #183 인계로 폐기됨.)
///
/// 소모를 **배치보다 앞으로** 옮긴 것이 #263의 전부다. 예전에는 확정 시점에 파괴해서 재료가 점유한
/// 타일에 결과를 놓을 수 없었다(WL-077a) — 재료가 확정 후에야 사라져 타일이 그때까지 잠겨 있었다.
public class TowerFusionController : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private TowerPlacer _placer;
    [SerializeField] private ManagementController _management; // 옵션. 없으면 무료(코스트 무시).

    private void Awake()
    {
        if (_management == null) _management = FindFirstObjectByType<ManagementController>();
    }

    /// 후보 버튼 onClick → 코디네이터(RequestMerge)를 거쳐 호출된다. group은 현재 선택 집합.
    /// onEnded는 배치 세션이 확정/취소 어느 쪽으로든 끝날 때 1회(코디네이터의 핑크 고정 해제용).
    /// **반환값 = 결과 타워 배치가 실제로 시작됐는가.** false면 onEnded도 오지 않으므로, 호출부가
    /// "배치 동안 유지"할 상태를 걸어두면 안 된다(재료·코스트 부족으로 조용히 반려되는 경로가 있다).
    public bool TryFuse(TowerRecipe recipe, TowerMergeGroup group, System.Action onEnded = null)
    {
        if (recipe == null) { Debug.LogError("[TowerFusion] recipe가 지정되지 않았습니다."); return false; }
        if (recipe.Result == null) { Debug.LogError("[TowerFusion] recipe.Result가 비어 있습니다."); return false; }
        if (group == null || _placer == null)
        {
            Debug.LogError("[TowerFusion] group/placer가 연결되지 않았습니다.");
            return false;
        }

        // 1. 그룹 타워 → TowerID 목록 (null/파괴/SO 없음 제외)
        var groupTowers = new List<Tower>();
        var towerIds = new List<string>();
        foreach (var t in group.Towers)
        {
            if (t == null || t.Asset == null) continue;
            groupTowers.Add(t);
            towerIds.Add(t.Asset.TowerID);
        }

        // 2. 레시피 재료 → (TowerID, 개수) 집계 (후보 버튼 활성 판정과 동일 규칙 — 단일 출처)
        var required = TowerFusionMatcher.BuildRequired(recipe);
        if (required.Count == 0)
        {
            Debug.LogWarning("[TowerFusion] 레시피에 유효한 재료가 없습니다.");
            return false;
        }

        // 3. 포함 매칭
        if (!TowerFusionMatcher.TryResolve(towerIds, required, out var consumeIndices))
        {
            Debug.Log("[TowerFusion] 재료가 부족해 합성할 수 없습니다.");
            return false;
        }

        // 4. 코스트 확인 (관리 시스템이 있을 때만)
        if (_management != null && !_management.CanAfford(recipe.ExtraCost))
        {
            Debug.Log("[TowerFusion] 합성 코스트가 부족합니다.");
            return false;
        }

        // 소모 대상 타워 확정
        var toConsume = new List<Tower>(consumeIndices.Count);
        foreach (int i in consumeIndices) toConsume.Add(groupTowers[i]);

        // 결과 타워의 런타임 Data 방어 채움(패널 경로를 안 거치므로).
        if (recipe.Result.Data == null)
            recipe.Result.Data = DataTableManager.Get<TowerTable>("TowerTable")?.Get(recipe.Result.TowerID);

        // 5. 재료를 **먼저** 소모한다(#263). 배치가 시작되기 전에 자리를 비워야 재료가 있던 타일에
        //    결과를 놓을 수 있다 — 이 순서가 커맨드를 도입한 이유 그 자체다.
        //    선택 집합에서 빼는 일은 하지 않는다: 비활성화 → Tower.Active 이탈 → ActiveChanged →
        //    코디네이터의 Prune이 이미 담당한다(구 ConsumeMaterials의 group.Remove가 하던 몫).
        var command = new TowerMergeCommand(toConsume);
        if (!command.Execute())
        {
            Debug.LogError("[TowerFusion] 재료 소모에 실패해 합성을 중단합니다.");
            return false;
        }

        // 6. 배치 시작. 확정되면 Commit(진짜 파괴), 세션이 취소로 끝나면 Undo(원복).
        //    종료 통지는 확정/취소를 구분하지 않으므로 판단은 커맨드가 자기 상태로 한다 — 확정 뒤의
        //    Undo는 무시되므로 두 콜백을 다 걸어도 안전하다.
        bool started = _placer.BeginTowerPlacement(
            recipe.Result,
            recipe.ExtraCost,
            command.Commit,
            () => { command.Undo(); onEnded?.Invoke(); });

        // 배치를 열지 못했으면 방금 소모한 재료를 즉시 되돌린다. 이 경로에서는 종료 통지도 오지 않으므로
        // 여기서 되돌리지 않으면 재료만 사라진 채 아무 일도 일어나지 않는다.
        if (!started) command.Undo();

        return started;
    }
}
