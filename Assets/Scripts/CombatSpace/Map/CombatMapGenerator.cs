using System.Collections.Generic;
using UnityEngine;

namespace CombatSpace
{
    // 전투맵 생성 과정 전체를 관리
    public sealed class CombatMapGenerator : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField]
        private CombatMapGenerationSettings settings;

        [Header("Seed")]
        [SerializeField]
        private int seed = 12345;

        [Header("Generation Retry")]
        [SerializeField]
        [Min(1)]
        private int maxGenerationAttempts = 5;

        [SerializeField]
        private List<int> validatedSeeds =
            new List<int>
            {
                1,
                2,
                3,
                4,
                5
            };
        [Header("Stability Test")]
        [SerializeField]
        private int stabilityTestStartSeed = 1;

        [SerializeField]
        [Min(1)]
        private int stabilityTestCount = 100;
        private readonly WaypointGenerator waypointGenerator =
            new WaypointGenerator();

        private readonly WaypointOrderer waypointOrderer =
            new WaypointOrderer();

        private readonly RouteGenerator routeGenerator =
            new RouteGenerator();

        private readonly RouteValidator routeValidator =
            new RouteValidator();

        private readonly GrassGenerator grassGenerator =
            new GrassGenerator();

        private readonly GrassEroder grassEroder =
            new GrassEroder();

        private readonly WaterGenerator waterGenerator =
            new WaterGenerator();

        public CombatMapData CurrentMap
        {
            get;
            private set;
        }

        public int UsedSeed
        {
            get;
            private set;
        }

        public bool LastGenerationSucceeded
        {
            get;
            private set;
        }

        public string LastGenerationError
        {
            get;
            private set;
        }

        [ContextMenu("Generate Map")]
        public void Generate()
        {
            TryGenerate();
        }

        // 재시도를 포함한 전체 맵 생성
        public bool TryGenerate()
        {
            CurrentMap = null;
            LastGenerationSucceeded = false;
            LastGenerationError = null;

            if (settings == null)
            {
                LastGenerationError =
                    "맵 생성 설정이 지정되지 않았습니다.";

                Debug.LogError(
                    LastGenerationError,
                    this);

                return false;
            }

            if (!settings.Validate(
                    out string settingsError))
            {
                LastGenerationError =
                    $"맵 생성 설정 오류: {settingsError}";

                Debug.LogError(
                    LastGenerationError,
                    this);

                return false;
            }

            HashSet<int> attemptedSeeds =
                new HashSet<int>();

            // 기본 시드부터 순서대로 재시도
            for (int attempt = 0;
                 attempt < maxGenerationAttempts;
                 attempt++)
            {
                int generationSeed =
                    unchecked(seed + attempt);

                attemptedSeeds.Add(generationSeed);

                if (TryGenerateWithSeed(
                        generationSeed,
                        out CombatMapData generatedMap,
                        out string errorMessage))
                {
                    CompleteGeneration(
                        generatedMap,
                        generationSeed,
                        attempt + 1,
                        false);

                    return true;
                }

                LastGenerationError =
                    errorMessage;
            }

            // 일반 재시도가 모두 실패하면
            // 검증된 예비 시드 사용
            if (TryGenerateWithValidatedSeeds(
                    attemptedSeeds,
                    out CombatMapData fallbackMap,
                    out int fallbackSeed,
                    out string fallbackError))
            {
                CompleteGeneration(
                    fallbackMap,
                    fallbackSeed,
                    maxGenerationAttempts,
                    true);

                return true;
            }

            LastGenerationError =
                "모든 맵 생성 시도가 실패했습니다.\n" +
                $"마지막 오류: " +
                $"{fallbackError ?? LastGenerationError}";

            Debug.LogError(
                LastGenerationError,
                this);

            return false;
        }

        // 하나의 시드로 맵 생성 시도
        private bool TryGenerateWithSeed(
            int generationSeed,
            out CombatMapData generatedMap,
            out string errorMessage)
        {
            generatedMap = null;
            errorMessage = null;

            System.Random random =
                new System.Random(generationSeed);

            CombatMapData candidateMap =
                new CombatMapData(
                    settings.Width,
                    settings.Height,
                    generationSeed,
                    settings.RouteStartPosition);

            if (!waypointGenerator.Generate(
                    candidateMap,
                    settings,
                    random))
            {
                errorMessage =
                    "웨이포인트 생성 실패";

                return false;
            }

            if (!waypointOrderer.Order(
                    candidateMap))
            {
                errorMessage =
                    "웨이포인트 순서 결정 실패";

                return false;
            }

            if (!routeGenerator.Generate(
                    candidateMap,
                    settings,
                    random))
            {
                errorMessage =
                    "Road 경로 생성 실패";

                return false;
            }

            if (!routeValidator.Validate(
                    candidateMap,
                    out string routeError))
            {
                errorMessage =
                    $"Road 검증 실패: {routeError}";

                return false;
            }

            if (!grassGenerator.Generate(
                    candidateMap,
                    settings))
            {
                errorMessage =
                    "Grass 생성 실패";

                return false;
            }

            if (!grassEroder.Erode(
                    candidateMap,
                    settings,
                    random))
            {
                errorMessage =
                    "Grass 침식 실패";

                return false;
            }

            if (!waterGenerator.Generate(
                    candidateMap,
                    settings,
                    random))
            {
                errorMessage =
                    "Water 생성 실패";

                return false;
            }

            if (!routeValidator.Validate(
                    candidateMap,
                    out string finalRouteError))
            {
                errorMessage =
                    $"최종 Road 검증 실패: " +
                    $"{finalRouteError}";

                return false;
            }

            generatedMap = candidateMap;

            return true;
        }

        // 검증된 예비 시드를 결정적인 순서로 시도
        private bool TryGenerateWithValidatedSeeds(
            HashSet<int> attemptedSeeds,
            out CombatMapData generatedMap,
            out int successfulSeed,
            out string errorMessage)
        {
            generatedMap = null;
            successfulSeed = 0;
            errorMessage = null;

            if (validatedSeeds == null ||
                validatedSeeds.Count == 0)
            {
                errorMessage =
                    "등록된 검증 시드가 없습니다.";

                return false;
            }

            // 같은 기본 시드에서는
            // 같은 예비 시드부터 검사
            System.Random fallbackRandom =
                new System.Random(
                    unchecked(seed ^ 1597463007));

            int startIndex =
                fallbackRandom.Next(
                    validatedSeeds.Count);

            for (int offset = 0;
                 offset < validatedSeeds.Count;
                 offset++)
            {
                int index =
                    (startIndex + offset) %
                    validatedSeeds.Count;

                int fallbackSeed =
                    validatedSeeds[index];

                if (attemptedSeeds.Contains(
                        fallbackSeed))
                {
                    continue;
                }

                if (TryGenerateWithSeed(
                        fallbackSeed,
                        out generatedMap,
                        out errorMessage))
                {
                    successfulSeed =
                        fallbackSeed;

                    return true;
                }
            }

            return false;
        }

        private void CompleteGeneration(
            CombatMapData generatedMap,
            int generationSeed,
            int attemptCount,
            bool usedFallbackSeed)
        {
            CurrentMap = generatedMap;
            UsedSeed = generationSeed;
            LastGenerationSucceeded = true;
            LastGenerationError = null;

            if (usedFallbackSeed)
            {
                Debug.LogWarning(
                    "기본 시드 생성에 실패하여 " +
                    $"검증된 예비 시드 " +
                    $"{generationSeed}를 사용했습니다.",
                    this);
            }
            else if (generationSeed != seed)
            {
                Debug.LogWarning(
                    $"기본 시드 {seed} 생성에 실패하여 " +
                    $"시드 {generationSeed}를 사용했습니다.",
                    this);
            }

            PrintGenerationResult(
                attemptCount,
                usedFallbackSeed);
        }

        private void PrintGenerationResult(
            int attemptCount,
            bool usedFallbackSeed)
        {
            int roadCount =
                CountTiles(
                    CombatTileType.Road);

            int grassCount =
                CountTiles(
                    CombatTileType.Grass);

            int waterCount =
                CountTiles(
                    CombatTileType.Water);

            Debug.Log(
                "전투맵 생성 완료\n" +
                $"요청 Seed: {seed}\n" +
                $"사용 Seed: {UsedSeed}\n" +
                $"생성 시도: {attemptCount}회\n" +
                $"예비 Seed 사용: " +
                $"{usedFallbackSeed}\n" +
                $"Waypoints: " +
                $"{CurrentMap.MajorWaypoints.Count}개\n" +
                $"Road: {roadCount}칸\n" +
                $"Grass: {grassCount}칸\n" +
                $"Water: {waterCount}칸",
                this);
        }

        private int CountTiles(
            CombatTileType tileType)
        {
            int count = 0;

            for (int x = 0;
                 x < CurrentMap.Width;
                 x++)
            {
                for (int y = 0;
                     y < CurrentMap.Height;
                     y++)
                {
                    Vector2Int position =
                        new Vector2Int(x, y);

                    if (CurrentMap
                            .GetTile(position)
                            .Type == tileType)
                    {
                        count++;
                    }
                }
            }

            return count;
        }
        [ContextMenu("Run Stability Test")]
        public void RunStabilityTest()
        {
            if (settings == null)
            {
                Debug.LogError(
                    "맵 생성 설정이 지정되지 않았습니다.",
                    this);

                return;
            }

            if (!settings.Validate(
                    out string settingsError))
            {
                Debug.LogError(
                    $"맵 생성 설정 오류: {settingsError}",
                    this);

                return;
            }

            if (stabilityTestCount < 1)
            {
                Debug.LogError(
                    "안정성 테스트 개수는 1 이상이어야 합니다.",
                    this);

                return;
            }

            int successCount = 0;
            int failureCount = 0;

            int minimumRoadCount = int.MaxValue;
            int maximumRoadCount = 0;

            long totalRoadCount = 0;
            long totalGrassCount = 0;
            long totalWaterCount = 0;

            List<int> successfulSeedExamples =
                new List<int>();

            System.Diagnostics.Stopwatch stopwatch =
                System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0;
                 i < stabilityTestCount;
                 i++)
            {
                int testSeed =
                    unchecked(
                        stabilityTestStartSeed + i);

                bool success =
                    TryGenerateWithSeed(
                        testSeed,
                        out CombatMapData generatedMap,
                        out string errorMessage);

                if (!success)
                {
                    failureCount++;

                    // 실패가 너무 많아도 Console이 도배되지 않도록
                    // 처음 10개만 개별 출력
                    if (failureCount <= 10)
                    {
                        Debug.LogWarning(
                            $"안정성 테스트 실패\n" +
                            $"Seed: {testSeed}\n" +
                            $"원인: {errorMessage}",
                            this);
                    }

                    continue;
                }

                successCount++;

                int roadCount =
                    generatedMap.EnemyRoute.Count;

                int grassCount =
                    CountTiles(
                        generatedMap,
                        CombatTileType.Grass);

                int waterCount =
                    CountTiles(
                        generatedMap,
                        CombatTileType.Water);

                minimumRoadCount =
                    Mathf.Min(
                        minimumRoadCount,
                        roadCount);

                maximumRoadCount =
                    Mathf.Max(
                        maximumRoadCount,
                        roadCount);

                totalRoadCount += roadCount;
                totalGrassCount += grassCount;
                totalWaterCount += waterCount;

                if (successfulSeedExamples.Count < 5)
                {
                    successfulSeedExamples.Add(
                        testSeed);
                }
            }

            stopwatch.Stop();

            if (successCount == 0)
            {
                minimumRoadCount = 0;
            }

            float successRate =
                stabilityTestCount > 0
                    ? successCount /
                      (float)stabilityTestCount *
                      100f
                    : 0f;

            double averageRoadCount =
                successCount > 0
                    ? totalRoadCount /
                      (double)successCount
                    : 0d;

            double averageGrassCount =
                successCount > 0
                    ? totalGrassCount /
                      (double)successCount
                    : 0d;

            double averageWaterCount =
                successCount > 0
                    ? totalWaterCount /
                      (double)successCount
                    : 0d;

            double averageMilliseconds =
                stabilityTestCount > 0
                    ? stopwatch.Elapsed.TotalMilliseconds /
                      stabilityTestCount
                    : 0d;

            string seedExamples =
                successfulSeedExamples.Count > 0
                    ? string.Join(
                        ", ",
                        successfulSeedExamples)
                    : "없음";

            Debug.Log(
                "안정성 테스트 완료\n" +
                $"테스트 시드: " +
                $"{stabilityTestCount}개\n" +
                $"성공: {successCount}개\n" +
                $"실패: {failureCount}개\n" +
                $"성공률: {successRate:F1}%\n" +
                $"Road 최소: {minimumRoadCount}칸\n" +
                $"Road 최대: {maximumRoadCount}칸\n" +
                $"Road 평균: {averageRoadCount:F1}칸\n" +
                $"Grass 평균: {averageGrassCount:F1}칸\n" +
                $"Water 평균: {averageWaterCount:F1}칸\n" +
                $"전체 시간: " +
                $"{stopwatch.Elapsed.TotalMilliseconds:F0}ms\n" +
                $"맵당 평균: " +
                $"{averageMilliseconds:F1}ms\n" +
                $"검증 통과 시드 예시: " +
                $"{seedExamples}",
                this);
        }
        private int CountTiles(
    CombatMapData map,
    CombatTileType tileType)
        {
            int count = 0;

            for (int x = 0;
                 x < map.Width;
                 x++)
            {
                for (int y = 0;
                     y < map.Height;
                     y++)
                {
                    Vector2Int position =
                        new Vector2Int(x, y);

                    if (map.GetTile(position).Type ==
                        tileType)
                    {
                        count++;
                    }
                }
            }

            return count;
        }
    }
}