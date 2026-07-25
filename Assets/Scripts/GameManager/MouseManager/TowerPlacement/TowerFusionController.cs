using System.Collections.Generic;
using UnityEngine;
using NorthLand.Combat;

/// 타워 합성(#195) 실행부. 후보 버튼 onClick → 지갑 재료 매칭 → 코스트 확인 →
/// TowerPlacer 고스트 배치 시작 → 배치 확정 시 재료 소모.
/// 지금은 테스트용으로 단일 레시피를 인스펙터에서 지정하고 버튼 1개로 실행한다(선택 UI는 #183, 타 담당).
public class TowerFusionController : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private TowerWallet _wallet;
    [SerializeField] private TowerPlacer _placer;
    [SerializeField] private ManagementController _management; // 옵션. 없으면 무료(코스트 무시).

    [Header("테스트 레시피")]
    [SerializeField] private TowerRecipe _recipe;

    private void Awake()
    {
        if (_management == null) _management = FindFirstObjectByType<ManagementController>();
    }

    /// 합성 버튼 onClick에 연결. 인스펙터에 지정한 _recipe로 합성을 시도한다.
    public void TryFuseSelected() => TryFuse(_recipe);

    public void TryFuse(TowerRecipe recipe)
    {
        if (recipe == null) { Debug.LogError("[TowerFusion] recipe가 지정되지 않았습니다."); return; }
        if (recipe.Result == null) { Debug.LogError("[TowerFusion] recipe.Result가 비어 있습니다."); return; }
        if (_wallet == null || _placer == null)
        {
            Debug.LogError("[TowerFusion] wallet/placer가 연결되지 않았습니다.");
            return;
        }

        // 1. 지갑 타워 → TowerID 목록 (null/파괴/SO 없음 제외)
        var walletTowers = new List<Tower>();
        var walletIds = new List<string>();
        foreach (var t in _wallet.Towers)
        {
            if (t == null || t.Asset == null) continue;
            walletTowers.Add(t);
            walletIds.Add(t.Asset.TowerID);
        }

        // 2. 레시피 재료 → (TowerID, 개수) 집계
        var required = BuildRequired(recipe);
        if (required.Count == 0)
        {
            Debug.LogWarning("[TowerFusion] 레시피에 유효한 재료가 없습니다.");
            return;
        }

        // 3. 포함 매칭
        if (!TowerFusionMatcher.TryResolve(walletIds, required, out var consumeIndices))
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
        foreach (int i in consumeIndices) toConsume.Add(walletTowers[i]);

        // 결과 타워의 런타임 Data 방어 채움(패널 경로를 안 거치므로).
        if (recipe.Result.Data == null)
            recipe.Result.Data = DataTableManager.Get<TowerTable>("TowerTable")?.Get(recipe.Result.TowerID);

        // 5. 배치 시작. 확정(고스트→타일)되면 ExtraCost 차감(TowerPlacer) 후 재료 소모.
        _placer.BeginTowerPlacement(recipe.Result, recipe.ExtraCost, () => ConsumeMaterials(toConsume));
    }

    // MaterialEntry 목록을 (TowerID, 개수)로 집계. 같은 타워가 여러 엔트리로 나뉘어도 합산한다.
    private static List<(string id, int count)> BuildRequired(TowerRecipe recipe)
    {
        var map = new Dictionary<string, int>();
        if (recipe.Materials != null)
        {
            foreach (var m in recipe.Materials)
            {
                if (m == null || m.Tower == null || m.Count <= 0) continue;
                string id = m.Tower.TowerID;
                map.TryGetValue(id, out int cur);
                map[id] = cur + m.Count;
            }
        }

        var list = new List<(string, int)>(map.Count);
        foreach (var kv in map) list.Add((kv.Key, kv.Value));
        return list;
    }

    private void ConsumeMaterials(List<Tower> towers)
    {
        foreach (var t in towers)
        {
            if (t == null) continue;
            _wallet.Remove(t);
            Destroy(t.gameObject); // OnDisable에서 Tower.Active 자동 해제
        }
    }
}
