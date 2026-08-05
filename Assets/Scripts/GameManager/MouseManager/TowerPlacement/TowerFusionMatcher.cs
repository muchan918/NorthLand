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

    /// 재료들이 결과 타워에 물려주는 **효과 종류**를 계산한다(#274 Phase 5, TowerRedesign.md §9).
    ///
    /// **툴팁(TowerMergeCandidateHover)과 실행부(TowerFusionController)가 반드시 이 하나를 공유한다** —
    /// 규칙을 두 벌로 구현하면 "물려받는다고 표시됐는데 실제로는 안 걸리는" 어긋남이 생긴다.
    /// BuildRequired가 후보 버튼과 실행부의 매칭 규칙을 단일 출처로 두는 것과 같은 이유다.
    ///
    /// 계승되는 것은 **종류뿐이고 수치는 결과 SO가 적는다.** 수치까지 물려받으면 "같은 효과가 겹칠 때
    /// max냐 합산이냐"를 정해야 하는데, 합산은 같은 타워를 계속 합성하면 무한 스택이 된다.
    ///
    /// ⚠ **반환 null과 빈 집합은 뜻이 다르다** — 이 구분이 fail-open을 막는다.
    ///
    ///   null      계승 자체를 안 하는 레시피(`InheritEffects == false`) → 필터 없음 = SO 효과 전부 활성
    ///   빈 집합    계승은 켰는데 물려줄 종류가 0개 → **전부 비활성**
    ///
    /// 예전에는 둘 다 null이라, 계승을 켠 레시피에 효과 없는 재료(archer×3 등)를 넣으면
    /// `Tower.IsEffectActive`의 "activeKinds == null = 전부 활성" 규약과 겹쳐 결과 타워가 SO의
    /// `Effects`를 **전부 켠 채** 배치됐다 — 의도의 정반대이고, 툴팁엔 `Inherit:` 줄이 안 떠서
    /// 표시와 실제가 어긋났다(PR #278 리뷰).
    public static HashSet<EffectKind> ResolveInheritedKinds(TowerRecipe recipe, IReadOnlyList<Tower> materials)
    {
        // 여기서만 null을 낸다 — "이 레시피는 계승 개념이 없다".
        if (recipe == null || !recipe.InheritEffects) return null;

        // 아래부터는 계승을 켠 레시피다. 무엇도 못 물려주면 **빈 집합**(= 전부 off)이지 null이 아니다.
        if (recipe.Result == null || materials == null) return new HashSet<EffectKind>();

        // 재료가 **실제로 활성인** 종류만 모은다 — SO가 아니라 인스턴스에게 묻는다.
        // 다단 합성(A+B→C, C+D→E)에서 C의 SO를 읽으면 꺼져 있는 효과까지 잡힌다(§9.2 ②).
        var kinds = new HashSet<EffectKind>();
        for (int i = 0; i < materials.Count; i++)
        {
            Tower material = materials[i];
            if (material == null) continue;
            foreach (EffectKind kind in material.ActiveEffectKinds) kinds.Add(kind);
        }

        // 결과 SO에 **정의된** 종류만 남긴다. 정의되지 않은 종류는 켤 수단(수치)이 없어 무의미하고,
        // 그런 저작 실수는 TowerRecipe.OnValidate가 별도로 경고한다(§9.5).
        var defined = new HashSet<EffectKind>();
        if (recipe.Result.Effects != null)
        {
            foreach (HitEffect effect in recipe.Result.Effects)
            {
                if (effect != null) defined.Add(effect.Kind);
            }
        }
        kinds.IntersectWith(defined);

        // 교집합이 비어도 빈 집합을 그대로 낸다 — null로 바꾸면 "전부 활성"으로 뒤집힌다.
        return kinds;
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
