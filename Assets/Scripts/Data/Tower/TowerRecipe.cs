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

    // 전역 일반명(경영 자재 등과 충돌 위험 — 단일 Assembly-CSharp, WL-062 축)을 피해 TowerRecipe 안에 중첩한다.
    [System.Serializable]
    public class MaterialEntry
    {
        public TowerAsset Tower;
        public int Count;
    }
}
