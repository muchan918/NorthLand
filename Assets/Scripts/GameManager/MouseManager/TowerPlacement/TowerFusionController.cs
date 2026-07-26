using System.Collections.Generic;
using UnityEngine;
using NorthLand.Combat;

/// 타워 합성(#195) 실행부. 후보 버튼 클릭 → 그룹 재료 매칭 → 코스트 확인 →
/// TowerPlacer 고스트 배치 시작 → 배치 확정 시 재료 소모.
/// 선택 집합(TowerMergeGroup)은 TowerMergeCoordinator가 소유하며, 실행 시 인자로 넘겨받는다(#183).
/// (구 임시 홀더 TowerWallet + 테스트 단일 레시피 경로는 #183 인계로 폐기됨.)
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
    public void TryFuse(TowerRecipe recipe, TowerMergeGroup group)
    {
        if (recipe == null) { Debug.LogError("[TowerFusion] recipe가 지정되지 않았습니다."); return; }
        if (recipe.Result == null) { Debug.LogError("[TowerFusion] recipe.Result가 비어 있습니다."); return; }
        if (group == null || _placer == null)
        {
            Debug.LogError("[TowerFusion] group/placer가 연결되지 않았습니다.");
            return;
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
            return;
        }

        // 3. 포함 매칭
        if (!TowerFusionMatcher.TryResolve(towerIds, required, out var consumeIndices))
        {
            Debug.Log("[TowerFusion] 재료가 부족해 합성할 수 없습니다.");
            return;
        }

        // 4. 코스트 확인 (관리 시스템이 있을 때만)
        if (_management != null && !_management.CanAfford(recipe.ExtraCost))
        {
            Debug.Log("[TowerFusion] 합성 코스트가 부족합니다.");
            return;
        }

        // 소모 대상 타워 확정
        var toConsume = new List<Tower>(consumeIndices.Count);
        foreach (int i in consumeIndices) toConsume.Add(groupTowers[i]);

        // 결과 타워의 런타임 Data 방어 채움(패널 경로를 안 거치므로).
        if (recipe.Result.Data == null)
            recipe.Result.Data = DataTableManager.Get<TowerTable>("TowerTable")?.Get(recipe.Result.TowerID);

        // 5. 배치 시작. 확정(고스트→타일)되면 ExtraCost 차감(TowerPlacer) 후 재료 소모.
        _placer.BeginTowerPlacement(recipe.Result, recipe.ExtraCost, () => ConsumeMaterials(group, toConsume));
    }

    private void ConsumeMaterials(TowerMergeGroup group, List<Tower> towers)
    {
        foreach (var t in towers)
        {
            if (t == null) continue;
            group.Remove(t);       // OnChanged 발행 → 코디네이터가 패널·하이라이트 갱신
            Destroy(t.gameObject); // OnDisable에서 Tower.Active 자동 해제
        }
    }
}
