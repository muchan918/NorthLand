using System.Collections.Generic;

/// <summary>
/// **재료 타워 → 그 타워를 재료로 쓰는 레시피들**의 색인. 합성 패널이 쓰는
/// "선택 집합 → 만들 수 있는 레시피"(<see cref="TowerFusionMatcher.CanFuse"/>)의 <b>역방향</b>이며,
/// 타워 하나를 클릭했을 때 "이걸로 무엇이 되는가"를 정보 패널에 띄우기 위한 것이다.
/// 명세: <c>Docs/Core/TowerMerge.md</c> §8.5.
/// <br/>
/// 조회 방향이 셋으로 늘었다는 점을 유의: 선택 집합→레시피(합성 패널) / 결과→재료(도감
/// <see cref="NorthLand.UI.FusionTowerCodexUI"/>) / <b>재료→결과(여기)</b>.
/// <br/>
/// <b>재료 집계를 자체 구현하지 않는다</b> — <see cref="TowerFusionMatcher.BuildRequired"/>를 쓴다.
/// 같은 레시피를 두 벌의 규칙으로 읽으면 "정보 패널엔 상위 타워로 뜨는데 실제로는 재료로 안 걸리는"
/// 어긋남이 생긴다. 후보 버튼과 실행부가 매칭 규칙을 공유하는 것과 같은 이유다(명세 §6 단일 출처).
/// </summary>
public static class TowerMergeTargetIndex
{
    // 반환용 빈 목록. 매 조회마다 새 배열을 만들지 않는다(타워를 클릭할 때마다 도는 경로).
    private static readonly TowerRecipe[] k_None = new TowerRecipe[0];

    private static Dictionary<string, List<TowerRecipe>> _byMaterial;

    /// <summary>
    /// <paramref name="materialTowerId"/>를 재료로 쓰는 레시피 목록. 없으면 빈 목록(null 아님).
    /// 순서는 카탈로그 적재 순서 그대로다 — <b>표시 순서는 뷰가 정한다</b>(§8.5: 등급 다음 표시 이름).
    /// 이름 정렬은 로케일에 따라 달라져서, 로케일을 모르는 이 색인이 미리 정해두면 언어를 바꿀 때 어긋난다.
    /// </summary>
    public static IReadOnlyList<TowerRecipe> RecipesUsing(string materialTowerId)
    {
        if (string.IsNullOrEmpty(materialTowerId)) return k_None;

        Build();
        return _byMaterial.TryGetValue(materialTowerId, out List<TowerRecipe> list) ? list : k_None;
    }

    /// 카탈로그는 런타임에 바뀌지 않으므로 1회 구축 후 캐시한다(<see cref="TowerRecipeCatalog.All"/>과 같은 규약).
    /// 에디터에서 레시피 SO를 고친 경우는 도메인 리로드가 이 static을 날려 자동으로 다시 구축된다.
    private static void Build()
    {
        if (_byMaterial != null) return;
        _byMaterial = new Dictionary<string, List<TowerRecipe>>();

        foreach (TowerRecipe recipe in TowerRecipeCatalog.All)
        {
            // Result가 없는 레시피는 걸러낸다 — 소비처가 결과 타워의 아이콘·이름으로 행을 그리므로
            // 그릴 것이 없다. 저작 실수이고, 합성 실행 자체도 결과 없이는 성립하지 않는다.
            if (recipe == null || recipe.Result == null) continue;

            // BuildRequired가 (TowerID, 개수)로 합산해 주므로, 한 재료를 여러 엔트리로 나눠 적은
            // 레시피도 같은 재료에 중복 등록되지 않는다.
            foreach ((string id, int count) in TowerFusionMatcher.BuildRequired(recipe))
            {
                if (string.IsNullOrEmpty(id) || count <= 0) continue;

                if (!_byMaterial.TryGetValue(id, out List<TowerRecipe> list))
                {
                    list = new List<TowerRecipe>();
                    _byMaterial[id] = list;
                }
                list.Add(recipe);
            }
        }
    }
}
