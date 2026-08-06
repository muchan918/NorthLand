using System;
using CombatSpace;
using UnityEngine;

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

        public RunData RunData => seedContext.RunData;

        public RunSeedData SeedData => seedContext.Data;

        [Header("Systems")]
        [SerializeField]
        private CombatMapInitializer combatMapInitializer;

        [SerializeField]
        private TerritoryController territoryController;

        public int MasterSeed => seedContext.MasterSeed;

        /// <summary>
        /// 이어하기에서 읽은 RunData를 월드 생성 전에 주입한다.
        /// 이후 Start가 저장된 최종 사용 시드로 영토와 전투 맵을 생성한다.
        /// </summary>
        public bool TryPrepareRestore(RunData savedRunData)
        {
            if (seedContext.IsInitialized)
            {
                Debug.LogError("[Load] Run 시드가 이미 초기화되어 복원 데이터를 주입할 수 없습니다.",this);

                return false;
            }

            try
            {
                seedContext.Restore(savedRunData);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Load] Run 시드 복원 준비에 실패했습니다: {exception.Message}",this);

                return false;
            }
        }

        private void Start()
        {
            if (!seedContext.IsInitialized)
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

            // 신규 게임은 UsedSeed가 아직 0이므로 RequestedSeed 사용.
            // 이어하기는 저장된 최종 UsedSeed 사용.
            int generationSeed =seedData.CombatMapUsedSeed != 0? seedData.CombatMapUsedSeed: seedData.CombatMapRequestedSeed;

            bool initialized =combatMapInitializer.InitializeCombatMap(generationSeed);

            if (!initialized)
            {
                Debug.LogError("[RunSeed] 전투맵 초기화에 실패했습니다.",this);

                return;
            }

            seedContext.RecordCombatMapUsedSeed(combatMapInitializer.UsedSeed);

            Debug.Log($"[RunSeed] 전투맵 시드 기록 완료 요청: {seedData.CombatMapRequestedSeed} " +
                $"생성 입력: {generationSeed} 최종 사용: {seedData.CombatMapUsedSeed}",this);
        }
        private void InitializeTerritory()
        {
            if (territoryController == null)
            {
                Debug.LogError("[RunSeed] TerritoryController가 연결되지 않았습니다.",this);

                return;
            }

            RunSeedData seedData = seedContext.Data;

            // 신규 게임은 UsedSeed가 0이므로 RequestedSeed를 사용하고,
            // 이어하기는 저장된 최종 UsedSeed를 사용한다.
            int territorySeed = seedData.TerritoryUsedSeed != 0? seedData.TerritoryUsedSeed: seedData.TerritoryRequestedSeed;

            bool initialized =territoryController.Initialize(territorySeed);

            if (!initialized)
            {
                Debug.LogError("[RunSeed] 영토 초기화에 실패했습니다.",this);

                return;
            }

            seedContext.RecordTerritoryUsedSeed(territoryController.UsedSeed);

            Debug.Log($"[RunSeed] 영토 시드 기록 완료 요청:{seedData.TerritoryRequestedSeed} 사용: {seedData.TerritoryUsedSeed}",this);
        }
    } 
}