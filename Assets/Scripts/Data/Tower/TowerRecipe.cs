using System.Collections.Generic;
using UnityEngine;

// 타워 합성 족보(#194). 재료 타워들을 소진해 결과 타워를 만든다.
// CSV 파이프라인을 타지 않고 인스펙터 손입력으로만 정의한다 — 재료/결과가 TowerAsset 참조 중심이라
// ID 문자열 resolve보다 직접 드래그가 자연스럽기 때문. 결과(Result)는 평범한 TowerAsset이라
// 합성 실행부는 검증·소진 후 기존 TowerPlacer 경로로 그대로 배치하면 된다.
[CreateAssetMenu(fileName = "TowerRecipe", menuName = "Scriptable Objects/TowerRecipe")]
public class TowerRecipe : ScriptableObject
{
    public List<MaterialEntry> Materials;   // 재료: 타워 종류별 필요 개수(multiset)
    public TowerAsset Result;               // 결과 타워
    public List<ResourceCost> ExtraCost;    // 합성 추가 비용(자원/마나석)

    [Tooltip("재료가 갖고 있던 효과의 '종류'를 결과 타워가 물려받는다. 수치는 계승하지 않는다 — " +
             "결과 SO의 Effects에 미리 적힌 수치 중 재료가 가져온 종류만 켜진다.")]
    public bool InheritEffects;             // 계승 여부는 레시피가 정한다(#274 Phase 5)

    // 전역 일반명(경영 자재 등과 충돌 위험 — 단일 Assembly-CSharp, WL-062 축)을 피해 TowerRecipe 안에 중첩한다.
    [System.Serializable]
    public class MaterialEntry
    {
        public TowerAsset Tower;
        public int Count;
    }

#if UNITY_EDITOR
    /// 저작 실수를 저장 시점에 잡는다(#274 Phase 5, TowerRedesign.md §9.5).
    /// TowerAsset.OnValidate와 같은 축 — **예외도 경고도 없이 조용히 아무 일도 안 일어나는** 조합이라,
    /// 없으면 합성해보기 전까지 알 수 없다(WL-001의 lightning_tower 전 필드 0과 같은 무증상 패턴).
    private void OnValidate()
    {
        // 재료 구성 검사가 **먼저** 돈다. 아래 조기 return 안쪽에 두면 `InheritEffects`가 꺼진
        // 레시피(현재 대다수)는 저장 시 아무 검사도 받지 않는다 — #478에서 실제로 그랬다.
        ValidateComposition();

        if (!InheritEffects || Result == null || Materials == null) return;

        // 결과 SO가 정의한 효과 종류
        var defined = new HashSet<NorthLand.Combat.EffectKind>();
        if (Result.Effects != null)
        {
            foreach (var effect in Result.Effects)
            {
                if (effect != null) defined.Add(effect.Kind);
            }
        }

        // 재료가 낼 수 있는데 결과 SO에 정의되지 않은 종류 → 계승해도 켤 수치가 없어 조용히 사라진다.
        var missing = new List<string>();
        int materialKinds = 0;
        foreach (var entry in Materials)
        {
            if (entry?.Tower?.Effects == null) continue;

            foreach (var effect in entry.Tower.Effects)
            {
                if (effect == null) continue;
                materialKinds++;
                if (defined.Contains(effect.Kind)) continue;
                string line = $"{effect.Kind}({entry.Tower.TowerID})";
                if (!missing.Contains(line)) missing.Add(line);
            }
        }

        // 계승을 켰는데 재료가 아무 효과도 안 낸다 → 결과 타워는 SO에 적힌 효과가 **전부 꺼진 채** 나온다.
        // 저작자는 보통 그 반대를 의도하므로(그럴 거면 InheritEffects를 끄면 된다) 여기서 잡는다.
        if (materialKinds == 0)
        {
            Debug.LogWarning(
                $"[TowerRecipe] '{name}': InheritEffects가 켜져 있는데 재료 타워가 내는 효과가 하나도 " +
                $"없습니다 — 결과 타워 '{Result.TowerID}'의 Effects가 전부 꺼진 채 배치됩니다. " +
                "효과를 가진 재료를 넣거나 InheritEffects를 끄세요.", this);
        }

        if (missing.Count > 0)
        {
            Debug.LogWarning(
                $"[TowerRecipe] '{name}': 재료가 내는 효과 {string.Join(", ", missing)}가 결과 타워 " +
                $"'{Result.TowerID}'의 Effects에 정의돼 있지 않습니다. 계승해도 켤 수치가 없어 " +
                "그 효과는 조용히 사라집니다 — 결과 SO에 해당 효과를 추가하거나 InheritEffects를 끄세요.", this);
        }

        if (defined.Count == 0)
        {
            Debug.LogWarning(
                $"[TowerRecipe] '{name}': InheritEffects가 켜져 있는데 결과 타워 '{Result.TowerID}'의 " +
                "Effects가 비어 있습니다 — 계승이 아무 효과도 내지 않습니다.", this);
        }
    }

    /// 계승 여부와 무관한 재료 구성 검사.
    ///
    /// <para>왜 별도 메서드인가: 위 계승 검사는 `!InheritEffects`면 통째로 건너뛰는데, 재료 구성은
    /// 계승과 아무 상관이 없다. 그 사각지대에서 재료 오저작이 **무증상으로 통과한다** — 증상은
    /// 합성 후보 툴팁(`TowerTooltipView.BuildMaterialLines`)에 잘못된 수량이 뜨는 것뿐이라
    /// 저작자가 아니면 알아채기 어렵다. #478의 `Recipe_SodaCannonTower`가 실제 사례로,
    /// 문서·PR 본문이 전부 "소다×1 + 캐논×1"인데 에셋은 소다×2였고 리뷰까지 가서야 드러났다.</para>
    ///
    /// <para>여기서 잡는 것은 **어느 값이 맞는지 알 수 없어도 틀렸다고 단정할 수 있는 것**만이다.
    /// "소다×2가 의도인가"는 코드가 알 수 없으므로 검사 대상이 아니다.</para>
    void ValidateComposition()
    {
        if (Result == null)
            Debug.LogWarning($"[TowerRecipe] '{name}': Result가 비어 있습니다 — 합성 결과가 없는 레시피입니다.", this);

        if (Materials == null || Materials.Count == 0)
        {
            Debug.LogWarning($"[TowerRecipe] '{name}': 재료가 하나도 없습니다 — 재료 없는 레시피는 저작 실수입니다.", this);
            return;
        }

        for (int i = 0; i < Materials.Count; i++)
        {
            MaterialEntry entry = Materials[i];

            if (entry == null || entry.Tower == null)
            {
                Debug.LogWarning($"[TowerRecipe] '{name}': Materials[{i}]의 Tower가 비어 있습니다.", this);
                continue;
            }

            if (entry.Count < 1)
            {
                Debug.LogWarning(
                    $"[TowerRecipe] '{name}': 재료 '{entry.Tower.TowerID}'의 Count가 {entry.Count}입니다 — " +
                    "1 이상이어야 합니다(0이면 그 재료를 요구하지 않는 것과 같습니다).", this);
            }

            // 자기 자신을 재료로 요구하면 결과를 만들려면 결과가 이미 있어야 한다.
            if (Result != null && entry.Tower == Result)
            {
                Debug.LogWarning(
                    $"[TowerRecipe] '{name}': 결과 타워 '{Result.TowerID}'가 자기 자신의 재료로 " +
                    "지정돼 있습니다 — 만들 수 없는 레시피입니다.", this);
            }

            // 같은 타워가 두 엔트리로 갈리면 집계(`TowerFusionMatcher.BuildRequired`)에서는 합쳐지지만
            // 저작 화면에서는 수량이 어디에 적혔는지 흩어져, 한쪽만 고치는 실수가 생긴다.
            for (int j = 0; j < i; j++)
            {
                if (Materials[j]?.Tower == entry.Tower)
                {
                    Debug.LogWarning(
                        $"[TowerRecipe] '{name}': 재료 '{entry.Tower.TowerID}'가 Materials[{j}]·[{i}] " +
                        "두 엔트리로 갈려 있습니다 — 한 엔트리의 Count로 합치세요.", this);
                    break;
                }
            }
        }
    }
#endif
}
