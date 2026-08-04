# 타일 기반 맵 설계 가이드

큰 그리드와 블록(7×7 타일 단위)으로 맵을 구성할 때 적용하는 규칙을 정리한 문서. `Scripts` 폴더의 실제 구현(`StageBuilder`, `StageRouteSettings`, `StageMapRouteGenerator`, `StageConnectionManager`, `StageTilePathBuilder`, `PathGenerator`, `PathSquareValidator`, `StageRoadTracker`, `LavaGenerator`, `StageMapSpawner`)을 기준으로 작성했다.

> 코드가 바뀌면 이 문서도 함께 갱신해서, 문서와 실제 구현이 어긋나지 않게 유지한다.

> **현재 정본 주의**: 이 문서는 구 `CombatSpace/MapBuilder` 경로를 설명한다. 정본 `GameScene`의 전투맵은 `CombatSpace/Map`의 `CombatMapGenerator`를 사용하며, Run 마스터 시드에서 파생된 `CombatMap` 시드를 외부 주입받는다. 구 MapBuilder의 전역 `UnityEngine.Random` 경로는 WL-008 잔여 정리 대상이다.

## 1. 기본 단위

| 용어 | 정의 |
| --- | --- |
| 블록 | 큰 그리드의 칸 1개. 7×7 타일로 구성된다 (`MapSize = 7`) |
| 연결점 | 블록 경계에서 인접 블록으로 드나들 수 있는 지점. `StageBuilder.waypoints`에 방향별로 정의 (`StageWaypoint`) |
| 중심점 | 블록 내부 경로가 반드시 경유하는 지점 후보. `StageBuilder.centerPoints`에 5개 정의 — 중앙 `(3,3)`과 그 상하좌우 `(4,3)`, `(2,3)`, `(3,4)`, `(3,2)` |
| 용암 타일 | 경로 밖 영역에 배치되는 위험 타일 |

## 2. 경로(루트) 생성 규칙 — `StageMapRouteGenerator` / `StageRouteSettings`

- 전체 블록 경로는 시작 블록 `(0,0)`에서 출발한다.
- 그리드 범위는 `minMapX`~`maxMapX`, `minMapY`~`maxMapY`로 제한된다 (기본값: X `-6`~`-1`, Y `-3`~`3`).
- 매 단계마다 현재 블록에서 상/하/좌/우로 이동 가능한(그리드 범위 안이고 아직 지나지 않은) 블록 후보 중 하나를 무작위로 선택해 경로에 추가한다.
- 전체 블록 수가 `maxMapCount`(기본값 30블록)에 도달할 때까지 이 과정을 반복한다.
- 이동 가능한 후보가 없어 막히면 처음부터 다시 시도하며, 최대 `routeGenerateTryCount`(기본값 500)번까지 재시도한다.

## 3. 블록 간 연결 규칙 — `StageConnectionManager`

- 블록 경계마다 방향별(위/아래/좌/우) 연결점이 미리 정의되어 있다 (7×7 기준 각 변에 5개, 예: 위쪽은 `x=1~5, y=0`).
- 다음 블록으로 나갈 때, **들어온 방향과 같은 방향으로는 다시 나갈 수 없다** (같은 변으로 재진입 금지).
- 이미 경로가 지나간 블록 방향은 후보에서 제외한다.
- 나가는 연결점은 목표 방향에 있는 연결점 후보 중 무작위로 선택한다.
- 다음 블록의 시작점은, 나간 연결점과 좌표가 대칭되는 반대편 연결점으로 정해진다. 대칭되는 지점이 없으면 해당 변의 끝 좌표로 보정한다.

## 4. 블록 내부 경로 생성 규칙 — `PathGenerator` / `PathSquareValidator` / `StageTilePathBuilder`

- 블록 내부 경로는 **시작점 → 중심점 → 도착점** 순서로 생성한다.
- 중심점은 후보 5개(중앙 및 상하좌우 인접 지점) 중 블록마다 무작위로 하나 선택한다.
- 경로는 X축/Y축으로 한 칸씩 이동하며, 목표 쪽으로 가까워지는 후보 중에서 2×2 정사각형을 만들지 않는 후보를 무작위로 선택해 이어간다 (`PathSquareValidator`).
- 2×2 정사각형 회피 검사는 현재 블록 안에서만 하지 않고, `StageRoadTracker`가 지금까지 생성된 모든 블록의 도로 타일을 전역 좌표로 누적해 놓은 것까지 함께 확인한다 — 그래서 인접한 블록끼리도 도로가 뭉쳐서 정사각형을 이루지 않는다.
- 모든 후보가 정사각형을 만들게 되는 경우에는 실패로 처리하지 않고, 후보 중 하나를 그대로 사용해 경로를 이어간다 (완전히 막히는 상황을 막기 위한 예외 처리).
- 경로 생성 자체가 막히면(이동할 후보가 전혀 없는 경우) 최대 `maxPathGenerateTryCount`(기본값 100)번까지 재시도한다.
- 마지막 블록에서는 도착점 없이 시작점 → 중심점까지만 경로를 만들고, 그 중심점 위치에 `finalCenterObjectOffset`(기본값 `(0,1,0)`, 즉 1칸 위)만큼 띄워서 최종 목표 오브젝트(`finalCenterObject`)를 배치한다.
- 블록 생성이 끝나면 해당 블록의 경로를 `StageRoadTracker.AddPath`로 전역 좌표에 누적해서, 다음 블록의 정사각형 회피 검사에 반영한다.

## 5. 위험 타일(용암) 배치 규칙 — `LavaGenerator`

- 경로 타일과 경로에 바로 인접한 타일은 제외한다.
- 남은 타일 후보 중에서 무작위로 뽑아 배치한다.
- 배치 개수는 블록마다 9~12개 사이에서 무작위로 결정된다.

## 6. 표기 규칙

| 표시 | 의미 |
| --- | --- |
| 파란 격자선 | 블록 경계 (블록 1칸 = 7×7 타일) |

## 7. 신규 맵 만들 때 체크리스트

- [ ] 큰 그리드 범위 결정 (`minMapX`/`maxMapX`/`minMapY`/`maxMapY`)
- [ ] 전체 경로 길이(`maxMapCount`)와 재시도 횟수(`routeGenerateTryCount`) 확인
- [ ] 블록 경계에 방향별 연결점 배치 — 연결점 없는 경계는 통과 불가
- [ ] 블록 내부 중심점 후보 배치 (`centerPoints`)
- [ ] 용암 타일 개수 범위(`minLavaCount`/`maxLavaCount`) 확인
- [ ] 최종 목표 오브젝트 배치 오프셋(`finalCenterObjectOffset`) 확인

## 8. 정본 전투맵 시드 계약

- `RunBootstrapper`가 마스터 시드에서 `CombatMap` 태그로 요청 시드를 파생한다.
- 운영 경로는 `CombatMapInitializer.InitializeCombatMap(seed)` → `CombatMapGenerator.TryGenerate(seed)`다.
- 생성기는 `new System.Random(generationSeed)` 하나를 웨이포인트·경로·침식·물·버프 타일 생성기에 주입한다. 전역 `UnityEngine.Random`을 소비하지 않는다.
- 일반 재시도는 `RequestedSeed + attempt`, 검증된 예비 시드의 시작 위치는 `RequestedSeed`에서 결정한다.
- 최종 성공값은 `UsedSeed`와 `CombatMapData.Seed`에 기록한다. 세이브 복원 시에는 저장된 `CombatMapUsedSeed`를 우선 사용한다.
- Inspector의 `debugSeed`와 자동 시작 옵션은 ContextMenu/단독 테스트 전용이며 운영 주입 시드를 덮어쓰지 않는다.
- Play 검증(2026-08-04): 동일 마스터 시드에서 경로·지형·버프 타일이 동일하고, 일반 새 게임에서는 매 Run 결과가 달라진다.
