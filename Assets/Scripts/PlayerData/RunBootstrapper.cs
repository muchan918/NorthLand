using UnityEngine;
using CombatSpace;

namespace NorthLand.Core
{
    /// <summary>
    /// 한 판의 시드를 가장 먼저 확정하고
    /// 각 시스템 초기화 순서를 관리한다.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class RunBootstrapper :MonoBehaviour
    {
        [Header("Seed Override")]

        [Tooltip("활성화하면 랜덤 시드 대신 아래 마스터 시드를 사용합니다.")]
        [SerializeField]
        private bool useMasterSeedOverride;

        [SerializeField]
        private int masterSeedOverride = 12345;

        private readonly RunSeedContext seedContext = new RunSeedContext();

        public RunSeedContext SeedContext => seedContext;

        public RunSeedData SeedData => seedContext.Data;

        private PlayerData playerData = new PlayerData();

        public PlayerData PlayerData => playerData;

        [Header("Systems")]
        [SerializeField]
        private CombatMapInitializer combatMapInitializer;

        [SerializeField]
        private TerritoryController territoryController;

        public int MasterSeed => seedContext.MasterSeed;

        private void Start()
        {
            InitializeRunSeed();
            InitializeTerritory();
            InitializeCombatMap();
        }

        [ContextMenu("Initialize Run Seed")]
        public void InitializeRunSeed()
        {
            if (seedContext.IsInitialized)
            {
                Debug.LogWarning("[RunSeed] 이미 초기화됐습니다.",this);

                return;
            }

            RunSeedData seedData;

            if (useMasterSeedOverride)
            {
                // 에디터 테스트값이 최우선
                seedData = seedContext.CreateRun(masterSeedOverride);
            }
            else if (GameSceneManager.Instance != null &&GameSceneManager.Instance.TryConsumePendingMasterSeed(out int pendingSeed))
            {
                // 플레이어가 타이틀에서 입력한 시드
                seedData = seedContext.CreateRun(pendingSeed);
            }
            else
            {
                // 일반 새 게임
                seedData = seedContext.CreateRandomRun();
            }

            playerData.CurrentRun.HasActiveRun = true;

            playerData.CurrentRun.SeedData = seedData;

            Debug.Log($"[RunSeed] Run 시드 초기화 완료 Master: {seedData.MasterSeed}\n" +
                $"CombatMap Requested: {seedData.CombatMapRequestedSeed}\n" +
                $"Territory Requested: {seedData.TerritoryRequestedSeed}\n" +
                $"Version: {seedData.SeedVersion}",this);
        }

        private void InitializeCombatMap()
        {
            if (combatMapInitializer == null)
            {
                Debug.LogError("[RunSeed] CombatMapInitializer가 연결되지 않았습니다.",this);

                return;
            }

            RunSeedData seedData = seedContext.Data;

            bool initialized =combatMapInitializer.InitializeCombatMap(seedData.CombatMapRequestedSeed);

            if (!initialized)
            {
                Debug.LogError("[RunSeed] 전투맵 초기화에 실패했습니다.",this);

                return;
            }

            seedContext.RecordCombatMapUsedSeed(combatMapInitializer.UsedSeed);

            Debug.Log($"[RunSeed] 전투맵 시드 기록 완료 요청: {seedData.CombatMapRequestedSeed}사용: {seedData.CombatMapUsedSeed}",this);
        }
        private void InitializeTerritory()
        {
            if (territoryController == null)
            {
                Debug.LogError("[RunSeed] TerritoryController가 연결되지 않았습니다.",this);

                return;
            }

            RunSeedData seedData = seedContext.Data;

            bool initialized =territoryController.Initialize(seedData.TerritoryRequestedSeed);

            if (!initialized)
            {
                Debug.LogError("[RunSeed] 영토 초기화에 실패했습니다.",this);

                return;
            }

            seedContext.RecordTerritoryUsedSeed(territoryController.UsedSeed);

            Debug.Log($"[RunSeed] 영토 시드 기록 완료 요청:{seedData.TerritoryRequestedSeed}\n" +
                $"사용: {seedData.TerritoryUsedSeed}",this);
        }
    }
}