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

    [System.Serializable]
    public class ProductionFields
    {
        public int BaseAmountPerVillager;

        // 나무꾼의 집/광산/농지처럼 ResourceTable 자원을 생산하는 경우만 채운다.
        public ResourceAsset OutputResource;

        // 훈련장처럼 병사를 생산하는 경우 true. 병사는 화폐성 자원이 아니라
        // ResourceTable에 넣지 않기로 했음 (전투 스탯·부활 등 별도 생명주기).
        // TODO: SoldierAsset이 생기면 이 bool 대신 참조 필드로 교체.
        public bool ProducesSoldier;
    }

    [System.Serializable]
    public class SkillFields
    {
        public ResourceAsset InputResource;
    }
}
