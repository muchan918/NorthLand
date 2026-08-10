using System;
using System.Collections.Generic;

namespace NorthLand.Core
{

    public enum MapArea
    {
        CombatMap = 0,
        StartMap = 1
    }

    /// <summary>
    /// 현재 진행 중인 한 판의 전체 저장 데이터.
    /// 저장 항목은 기능을 구현할 때 영역별로 추가한다.
    /// </summary>
    [Serializable]
    public sealed class RunData
    {
        public RunSeedData SeedData = new();
        public ProgressSaveData Progress = new();
        public ManagementSaveData Management = new();
        public TerritorySaveData Territory = new();
        public List<TowerSaveData> Towers = new();
        public List<RewardEffectSaveData> RewardEffects = new();
        public BaseSaveData PlayerBase = new();
    }
    public sealed class ProgressSaveData
    {
        public int WaveCount;
        public DayNightManager.Phase Phase;
    }

    public sealed class ManagementSaveData
    {
        public List<ResourceSaveData> Resources = new();
        public List<ProductionBuildingSaveData> ProductionBuildings = new();
        public List<UpgradeBuildingSaveData> UpgradeBuildings = new();
        public int BonusVillagers;
    }

    public sealed class ResourceSaveData
    {
        public ResourceKind Kind;
        public int Amount;
    }

    public sealed class ProductionBuildingSaveData
    {
        public string BuildingId;
        public int Level;
        public int Villagers;
    }
    public sealed class UpgradeBuildingSaveData
    {
        public string BuildingId;
        public int Level;
    }
    public sealed class TerritorySaveData
    {
        public List<int> OwnedNodeIds = new();
    }
    public sealed class TowerSaveData
    {
        public string TowerId;

        // 타워가 배치된 맵 영역
        public MapArea MapArea = MapArea.CombatMap;

        // 자동 생성 배틀맵에서 사용
        public int CellX;
        public int CellZ;

        // 스타트맵에서 사용
        public string StartTileId;
    }
    public sealed class RewardEffectSaveData
    {
        public WaveRewardType Type;
        public int Level;
    }
    public sealed class BaseSaveData
    {
        public float CurrentHp;
    }
}