using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BuildingAsset", menuName = "Scriptable Objects/BuildingAsset")]
public class BuildingAsset : ScriptableObject
{
    public string BuildingID;

    // BuildingAssetEditor가 Play 이전 편집 모드에서 필드 그룹을 골라 보여줘야 하므로
    // ResourceAsset과 달리 Data 캐시가 아닌 일반 필드로 노출한다.
    public BuildingType BuildingType;

    [HideInInspector]
    public BuildingData Data;

    public List<ResourceCost> Cost;

    public ProductionFields Production;
    public SkillFields Skill;

#if UNITY_EDITOR
    // 에디터 authoring 가드: 업그레이드 주민당량이 base(레벨0)부터 단조 증가하지 않으면 경고한다.
    // AmountPerVillager가 절대값이라 실수로 낮게/비단조로 적으면 업그레이드가 조용히 생산을 떨어뜨릴 수 있다(수치=SO, WL-015 축).
    private void OnValidate()
    {
        if (BuildingType != BuildingType.Production || Production == null || Production.UpgradeLevels == null)
        {
            return;
        }

        int prev = Production.BaseAmountPerVillager;
        for (int i = 0; i < Production.UpgradeLevels.Count; i++)
        {
            int cur = Production.UpgradeLevels[i].AmountPerVillager;
            if (cur <= prev)
            {
                Debug.LogWarning($"[BuildingAsset] {BuildingID}: 업그레이드 Lv{i + 1} 주민당량({cur})이 이전 단계({prev}) 이하 — 업그레이드가 생산을 늘리지 않습니다.", this);
            }
            prev = cur;
        }
    }
#endif

    [System.Serializable]
    public class ProductionFields
    {
        // 레벨 0(미업그레이드) 주민당 생산량. 업그레이드하면 UpgradeLevels의 값으로 올라간다.
        public int BaseAmountPerVillager;

        // 나무꾼의 집/광산/농장처럼 ResourceTable 자원을 생산하는 경우만 채운다.
        public ResourceAsset OutputResource;

        // 건물 업그레이드 레벨 테이블. index i = 레벨 (i+1). 비어 있으면 업그레이드 불가(최대 레벨 = Count).
        // 수치(비용·주민당량)는 이 SO에 직접 authoring한다(밸런싱 TBD — 영토 효과·타워 스탯 선례, WL-015).
        public List<UpgradeLevel> UpgradeLevels = new List<UpgradeLevel>();
    }

    // 생산 건물 업그레이드 한 단계. AmountPerVillager는 누적 델타가 아니라 그 레벨의 절대 주민당량.
    [System.Serializable]
    public class UpgradeLevel
    {
        // 이 레벨에 도달하기 위해 소모하는 비용(ManagementController.TrySpend 게이트웨이 경유).
        public List<ResourceCost> Cost = new List<ResourceCost>();

        // 이 레벨에서의 주민당 생산량(절대값).
        public int AmountPerVillager;
    }

    [System.Serializable]
    public class SkillFields
    {
        public ResourceAsset InputResource;
    }
}
