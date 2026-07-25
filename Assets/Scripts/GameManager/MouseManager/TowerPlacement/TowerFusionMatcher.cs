using System.Collections.Generic;

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
}
