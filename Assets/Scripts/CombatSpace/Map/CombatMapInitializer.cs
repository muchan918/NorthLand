using UnityEngine;

namespace CombatSpace
{
    // 전투맵 데이터 생성, 타일 생성, 초기 공개를 순서대로 실행
    public sealed class CombatMapInitializer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private CombatMapGenerator mapGenerator;

        [SerializeField]
        private CombatMapTileSpawner tileSpawner;

        [SerializeField]
        private CombatMapRevealController revealController;

        [Header("Runtime")]
        [SerializeField]
        private bool initializeOnStart;


        [ContextMenu("Initialize Combat Map")]
        public void InitializeCombatMap()
        {
            InitializeCombatMapInternal(null);
        }


        public int UsedSeed =>mapGenerator != null? mapGenerator.UsedSeed: 0;

        private void Start()
        {
            if (initializeOnStart)
            {
                InitializeCombatMap();
            }
        }

        public bool InitializeCombatMap(int requestedSeed)
        {
            return InitializeCombatMapInternal(requestedSeed);
        }

        private bool InitializeCombatMapInternal(
    int? requestedSeed)
        {
            if (!ValidateReferences())
            {
                return false;
            }

            bool generated =requestedSeed.HasValue? mapGenerator.TryGenerate(requestedSeed.Value): mapGenerator.TryGenerate();

            if (!generated)
            {
                Debug.LogError("전투맵 데이터 생성에 실패했습니다.\n" +mapGenerator.LastGenerationError,this);

                return false;
            }

            tileSpawner.SpawnTiles();

            if (tileSpawner.SpawnedTileCount == 0)
            {
                Debug.LogError("타일 GameObject 생성에 실패했습니다.",this);

                return false;
            }

            revealController.InitializeReveal();

            if (revealController.RevealData == null)
            {
                Debug.LogError("맵 공개 데이터 초기화에 실패했습니다.",this);

                return false;
            }

            Debug.Log(
                "전투맵 초기화 완료\n" +
                $"요청 Seed: " +
                $"{mapGenerator.RequestedSeed}\n" +
                $"사용 Seed: " +
                $"{mapGenerator.UsedSeed}\n" +
                $"타일: " +
                $"{tileSpawner.SpawnedTileCount}개\n" +
                $"초기 공개 타일: " +
                $"{revealController.RevealData.RevealedTileCount}개",
                this
            );

            return true;
        }

        private bool ValidateReferences()
        {
            if (mapGenerator == null)
            {
                Debug.LogError(
                    "Map Generator가 지정되지 않았습니다.",
                    this);

                return false;
            }

            if (tileSpawner == null)
            {
                Debug.LogError(
                    "Tile Spawner가 지정되지 않았습니다.",
                    this);

                return false;
            }

            if (revealController == null)
            {
                Debug.LogError(
                    "Reveal Controller가 지정되지 않았습니다.",
                    this);

                return false;
            }

            return true;
        }
    }
}