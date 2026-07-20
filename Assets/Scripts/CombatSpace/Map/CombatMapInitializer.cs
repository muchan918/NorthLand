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

        private void Start()
        {
            if (initializeOnStart)
            {
                InitializeCombatMap();
            }
        }

        [ContextMenu("Initialize Combat Map")]
        public void InitializeCombatMap()
        {
            if (!ValidateReferences())
            {
                return;
            }

            // 1. 재시도를 포함한 논리 맵 생성
            if (!mapGenerator.TryGenerate())
            {
                Debug.LogError(
                    "전투맵 데이터 생성에 실패했습니다.\n" +
                    mapGenerator.LastGenerationError,
                    this);

                return;
            }

            // 2. 실제 타일 GameObject 생성
            tileSpawner.SpawnTiles();

            if (tileSpawner.SpawnedTileCount == 0)
            {
                Debug.LogError(
                    "타일 GameObject 생성에 실패했습니다.",
                    this);

                return;
            }

            // 3. 0라운드 기준 초기 범위 공개
            revealController.InitializeReveal();

            if (revealController.RevealData == null)
            {
                Debug.LogError(
                    "맵 공개 데이터 초기화에 실패했습니다.",
                    this);

                return;
            }

            Debug.Log(
                "전투맵 초기화 완료\n" +
                $"Seed: " +
                $"{mapGenerator.CurrentMap.Seed}\n" +
                $"타일: " +
                $"{tileSpawner.SpawnedTileCount}개\n" +
                $"초기 공개 타일: " +
                $"{revealController.RevealData.RevealedTileCount}개\n" +
                $"초기 스폰 좌표: " +
                $"{revealController.CurrentSpawnPosition}",
                this);
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