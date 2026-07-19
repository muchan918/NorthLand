using System;
using UnityEngine;

namespace CombatSpace
{
    // 라운드에 따라 맵 공개 범위와 몬스터 스폰 위치를 관리
    public sealed class CombatMapRevealController : MonoBehaviour
    {
        [SerializeField]
        private CombatMapGenerator mapGenerator;

        [Header("Round")]
        [SerializeField]
        [Min(1)]
        private int totalRounds = 30;

        [SerializeField]
        [Min(0)]
        private int previewRounds = 2;

        [Header("Debug")]
        [SerializeField]
        [Min(0)]
        private int testRound = 1;

        // 각 타일이 어느 Road 인덱스와 함께 공개되는지 저장
        private int[,] tileRevealIndices;

        // 타일별 공개 여부
        public CombatMapRevealData RevealData
        {
            get;
            private set;
        }

        public int CurrentRound
        {
            get;
            private set;
        }

        // 마지막으로 공개된 Road 인덱스
        public int LastRevealedRouteIndex
        {
            get;
            private set;
        } = -1;

        // 몬스터가 스폰될 Road 인덱스
        public int CurrentSpawnRouteIndex =>
            LastRevealedRouteIndex;

        // 몬스터가 스폰될 그리드 좌표
        public Vector2Int CurrentSpawnPosition
        {
            get;
            private set;
        }

        public bool HasSpawnPosition =>
            LastRevealedRouteIndex >= 0;

        // 타일 하나가 처음 공개됐을 때 알림
        public event Action<Vector2Int> TileRevealed;

        // 전체 공개 상태가 변경됐을 때 알림
        public event Action RevealChanged;

        // 공개 데이터 초기화
        [ContextMenu("Initialize Reveal")]
        public void InitializeReveal()
        {
            if (!TryGetMap(out CombatMapData map))
            {
                return;
            }

            if (map.EnemyRoute.Count == 0)
            {
                Debug.LogError(
                    "생성된 Road 경로가 없습니다.",
                    this);

                return;
            }

            RevealData =
                new CombatMapRevealData(
                    map.Width,
                    map.Height);

            // 모든 타일을 가장 가까운 Road에 소속시킴
            tileRevealIndices =
                BuildTileRevealIndices(map);

            CurrentRound = 0;
            LastRevealedRouteIndex = -1;
            CurrentSpawnPosition = default;

            // 0라운드 기준 초기 범위 공개
            RevealForRound(0);

            Debug.Log(
                "맵 공개 데이터 초기화 완료\n" +
                $"RouteIndex " +
                $"{LastRevealedRouteIndex}까지 공개\n" +
                $"몬스터 스폰 좌표: " +
                $"{CurrentSpawnPosition}",
                this);
        }

        // 현재 라운드에 맞는 범위 공개
        public void RevealForRound(
            int currentRound)
        {
            if (!TryGetMap(out CombatMapData map))
            {
                return;
            }

            if (RevealData == null ||
                tileRevealIndices == null)
            {
                Debug.LogError(
                    "먼저 공개 데이터를 초기화해야 합니다.",
                    this);

                return;
            }

            if (map.EnemyRoute.Count == 0)
            {
                Debug.LogError(
                    "공개할 Road 경로가 없습니다.",
                    this);

                return;
            }

            CurrentRound =
                Mathf.Clamp(
                    currentRound,
                    0,
                    totalRounds);

            // 현재 라운드보다 일정 라운드 앞까지 미리 공개
            int revealRound =
                Mathf.Clamp(
                    CurrentRound + previewRounds,
                    0,
                    totalRounds);

            float progress =
                revealRound /
                (float)totalRounds;

            int targetRouteIndex =
                Mathf.FloorToInt(
                    progress *
                    (map.EnemyRoute.Count - 1));

            // 해당 Road 인덱스까지 소속된 타일 공개
            RevealTilesToRouteIndex(
                map,
                targetRouteIndex);

            LastRevealedRouteIndex =
                Mathf.Max(
                    LastRevealedRouteIndex,
                    targetRouteIndex);

            // 마지막 공개 Road를 스폰 좌표로 사용
            CurrentSpawnPosition =
                map.EnemyRoute[
                    LastRevealedRouteIndex];

            // 실제 타일 표시를 갱신하도록 알림
            RevealChanged?.Invoke();
        }

        // 각 타일을 가장 가까운 Road 인덱스에 배정
        private int[,] BuildTileRevealIndices(
            CombatMapData map)
        {
            int[,] result =
                new int[
                    map.Width,
                    map.Height];

            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    Vector2Int position =
                        new Vector2Int(x, y);

                    CombatTileData tile =
                        map.GetTile(position);

                    // Empty는 공개 대상이 아님
                    if (tile.Type ==
                        CombatTileType.Empty)
                    {
                        result[x, y] = -1;
                        continue;
                    }

                    // Road는 자신의 RouteIndex 사용
                    if (tile.IsRoad)
                    {
                        result[x, y] =
                            tile.RouteIndex;

                        continue;
                    }

                    // Grass와 Water는
                    // 가장 가까운 Road 인덱스를 사용
                    result[x, y] =
                        FindNearestRouteIndex(
                            map,
                            position);
                }
            }

            return result;
        }

        // 해당 타일에서 가장 가까운 Road 인덱스 검색
        private int FindNearestRouteIndex(
            CombatMapData map,
            Vector2Int position)
        {
            int nearestRouteIndex = -1;
            int nearestDistance =
                int.MaxValue;

            for (int i = 0;
                 i < map.EnemyRoute.Count;
                 i++)
            {
                Vector2Int roadPosition =
                    map.EnemyRoute[i];

                int distance =
                    (roadPosition - position)
                    .sqrMagnitude;

                if (distance >= nearestDistance)
                {
                    continue;
                }

                nearestDistance = distance;
                nearestRouteIndex = i;
            }

            return nearestRouteIndex;
        }

        // 목표 Road 인덱스까지 소속된 타일 공개
        private void RevealTilesToRouteIndex(
            CombatMapData map,
            int targetRouteIndex)
        {
            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    int revealIndex =
                        tileRevealIndices[x, y];

                    if (revealIndex < 0 ||
                        revealIndex >
                        targetRouteIndex)
                    {
                        continue;
                    }

                    Vector2Int position =
                        new Vector2Int(x, y);

                    // 처음 공개된 타일만 이벤트 호출
                    if (RevealData.Reveal(position))
                    {
                        TileRevealed?.Invoke(position);
                    }
                }
            }
        }

        // 몬스터 시스템에서 스폰 좌표 요청
        public bool TryGetSpawnPosition(
            out Vector2Int spawnPosition)
        {
            spawnPosition =
                CurrentSpawnPosition;

            return HasSpawnPosition;
        }

        // Inspector의 Test Round 값으로 공개 테스트
        [ContextMenu("Reveal Test Round")]
        private void RevealTestRound()
        {
            if (RevealData == null)
            {
                Debug.LogError(
                    "먼저 Initialize Reveal을 실행하세요.",
                    this);

                return;
            }

            RevealForRound(testRound);

            Debug.Log(
                $"Round {CurrentRound} 공개 완료\n" +
                $"RouteIndex " +
                $"{LastRevealedRouteIndex}까지 공개\n" +
                $"몬스터 스폰 좌표: " +
                $"{CurrentSpawnPosition}\n" +
                $"공개 타일: " +
                $"{RevealData.RevealedTileCount}개",
                this);
        }

        private bool TryGetMap(
            out CombatMapData map)
        {
            map = null;

            if (mapGenerator == null ||
                mapGenerator.CurrentMap == null)
            {
                Debug.LogError(
                    "먼저 전투맵을 생성해야 합니다.",
                    this);

                return false;
            }

            map =
                mapGenerator.CurrentMap;

            return true;
        }
        [ContextMenu("Validate Reveal State")]
        public void ValidateRevealState()
        {
            if (!TryGetMap(out CombatMapData map))
            {
                return;
            }

            if (RevealData == null ||
                tileRevealIndices == null)
            {
                Debug.LogError(
                    "먼저 공개 데이터를 초기화해야 합니다.",
                    this);

                return;
            }

            if (LastRevealedRouteIndex < 0 ||
                LastRevealedRouteIndex >=
                map.EnemyRoute.Count)
            {
                Debug.LogError(
                    "마지막 공개 Road 인덱스가 잘못됐습니다.",
                    this);

                return;
            }

            int expectedRevealedCount = 0;
            int missingRevealCount = 0;
            int earlyRevealCount = 0;

            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    int revealIndex =
                        tileRevealIndices[x, y];

                    if (revealIndex < 0)
                    {
                        continue;
                    }

                    Vector2Int position =
                        new Vector2Int(x, y);

                    bool shouldBeRevealed =
                        revealIndex <=
                        LastRevealedRouteIndex;

                    bool isRevealed =
                        RevealData.IsRevealed(position);

                    if (shouldBeRevealed)
                    {
                        expectedRevealedCount++;

                        if (!isRevealed)
                        {
                            missingRevealCount++;
                        }
                    }
                    else if (isRevealed)
                    {
                        earlyRevealCount++;
                    }
                }
            }

            Vector2Int expectedSpawnPosition =
                map.EnemyRoute[
                    LastRevealedRouteIndex];

            bool spawnPositionMatches =
                CurrentSpawnPosition ==
                expectedSpawnPosition;

            bool finalRoundComplete =
                CurrentRound < totalRounds ||
                LastRevealedRouteIndex ==
                map.EnemyRoute.Count - 1;

            bool isValid =
                missingRevealCount == 0 &&
                earlyRevealCount == 0 &&
                spawnPositionMatches &&
                finalRoundComplete &&
                RevealData.RevealedTileCount ==
                expectedRevealedCount;

            if (!isValid)
            {
                Debug.LogError(
                    "맵 공개 상태 검증 실패\n" +
                    $"현재 라운드: " +
                    $"{CurrentRound}/{totalRounds}\n" +
                    $"마지막 RouteIndex: " +
                    $"{LastRevealedRouteIndex}\n" +
                    $"예상 공개 타일: " +
                    $"{expectedRevealedCount}개\n" +
                    $"실제 공개 타일: " +
                    $"{RevealData.RevealedTileCount}개\n" +
                    $"공개 누락: " +
                    $"{missingRevealCount}개\n" +
                    $"미래 타일 조기 공개: " +
                    $"{earlyRevealCount}개\n" +
                    $"스폰 좌표 일치: " +
                    $"{spawnPositionMatches}\n" +
                    $"최종 라운드 전체 공개: " +
                    $"{finalRoundComplete}",
                    this);

                return;
            }

            Debug.Log(
                "맵 공개 상태 검증 완료\n" +
                $"현재 라운드: " +
                $"{CurrentRound}/{totalRounds}\n" +
                $"마지막 RouteIndex: " +
                $"{LastRevealedRouteIndex}\n" +
                $"공개 타일: " +
                $"{RevealData.RevealedTileCount}개\n" +
                $"몬스터 스폰 좌표: " +
                $"{CurrentSpawnPosition}",
                this);
        }
    }
}