using System.Collections.Generic;
using NorthLand.Combat;

/// 타워 합성(#195) 재료 매칭의 순수 로직. 씬/MonoBehaviour에 의존하지 않아 EditMode 테스트가 가능하다.
/// 매칭 규칙은 포함 매칭(#183/#194): 선택 개수 ≥ 필요 개수면 성립, 여분은 허용하고 소모하지 않는다.
public static class TowerFusionMatcher
{
    /// <summary>
    /// 지갑 타워들의 TowerID 목록에서 required(TowerID별 필요 개수)를 충족하는지 판정한다.
    /// 성공 시 소모할 타워의 인덱스 목록(정확히 필요 개수만큼)을 반환한다.
    /// 하나라도 부족하면 false를 반환하고 <paramref name="consumeIndices"/>는 null이 된다.
    /// </summary>
    public static bool TryResolve(
        IReadOnlyList<string> walletTowerIds,
        IReadOnlyList<(string id, int count)> required,
        out List<int> consumeIndices)
    {
        consumeIndices = new List<int>();
        if (walletTowerIds == null || required == null)
        {
            consumeIndices = null;
            return false;
        }

        var used = new bool[walletTowerIds.Count];
        foreach (var (id, count) in required)
        {
            if (string.IsNullOrEmpty(id) || count <= 0) continue; // 무의미한 요구는 무시

            int remaining = count;
            for (int i = 0; i < walletTowerIds.Count && remaining > 0; i++)
            {
                if (used[i]) continue;
                if (walletTowerIds[i] == id)
                {
                    used[i] = true;
                    consumeIndices.Add(i);
                    remaining--;
                }
            }

            if (remaining > 0)
            {
                consumeIndices = null;
                return false;
            }
        }

        return true;
    }

    /// 레시피 재료를 (TowerID, 개수)로 집계한다(같은 타워가 여러 엔트리로 나뉘어도 합산, 무효 엔트리 무시).
    /// 후보 버튼(#183)과 실행부(TowerFusionController)가 같은 규칙을 공유하도록 이 함수를 단일 출처로 쓴다.
    public static List<(string id, int count)> BuildRequired(TowerRecipe recipe)
    {
        var map = new Dictionary<string, int>();
        if (recipe != null && recipe.Materials != null)
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

    /// 지갑 타워들로 이 레시피를 합성할 수 있는지 판정한다(포함 매칭). 후보 버튼(#183) 활성 판정용.
    public static bool CanFuse(IReadOnlyList<Tower> walletTowers, TowerRecipe recipe)
    {
        if (walletTowers == null || recipe == null) return false;

        var ids = new List<string>(walletTowers.Count);
        foreach (var t in walletTowers)
        {
            if (t == null || t.Asset == null) continue;
            ids.Add(t.Asset.TowerID);
        }

        var required = BuildRequired(recipe);
        if (required.Count == 0) return false;
        return TryResolve(ids, required, out _);
    }
}
