# 몬스터 이동 시스템 (Issue #15: 몬스터 이동 시스템 - 웨이포인트)

## 1. 이슈 원문

**기능 설명**
스폰된 몬스터가 정해진 웨이포인트 경로를 따라 이동하는 시스템 구현.

**상세 내용**
- 웨이포인트 경로 데이터 정의
- 경로를 따라가는 이동 로직
- 더미로 시작 가능: 더미 몬스터 + 씬에 수동으로 배치한 웨이포인트 몇 개로 이동 로직을 먼저 검증할 수 있다.

**완료 기준**
1. 몬스터가 스폰 후 지정된 웨이포인트를 순서대로 따라간다
2. 경로 끝(본진)에 도달한다
3. (더미 몬스터/웨이포인트로 시작했다면) 실제 몬스터 데이터([#26](https://github.com/muchan918/NorthLand/issues/26))·스폰 시스템([#14](https://github.com/muchan918/NorthLand/issues/14)) 결과물로 교체해 확인했다

## 2. 개요

몬스터가 스테이지 타일맵 위에 생성된 경로(웨이포인트)를 따라 이동하다가 종착점(본진)에서 사라지는 기능. 신규로 `MonsterMove`, `StageMonsterRouteTracker` 두 클래스를 추가했고, 기존 `StageBuilder`가 스테이지를 생성할 때 이 둘을 이용해 몬스터 스폰 쪽(`MonsterSpawn`)에 경로/스폰 위치를 넘겨주는 구조.

## 3. 구성 요소

### MonsterMove (신규)
몬스터 오브젝트에 붙는 `MonoBehaviour`.

- `SetRoute(List<Vector3> routePoints)`: 외부(스폰 로직)에서 월드 좌표 경로를 주입받는다. 내부 `route` 리스트를 갱신하고 인덱스를 0으로 리셋한 뒤, 이미 도달 거리 안에 들어와 있는 선두 포인트들은 `SkipReachedPoints()`로 건너뛴다.
- `Update()`: 매 프레임 `Vector3.MoveTowards`로 현재 목표 지점(`route[currentRouteIndex]`)을 향해 `moveSpeed`만큼 이동. `arriveDistance` 이내로 도달하면 다음 인덱스로 넘어간다.
- 경로를 다 소진하면(`currentRouteIndex >= route.Count`) 오브젝트를 `Destroy` — 종착점(본진 방향 마지막 지점)에 도달하면 몬스터가 사라진다. 데미지 처리는 이 클래스에 없음.

### StageMonsterRouteTracker (신규)
`MonoBehaviour`가 아닌 순수 C# 클래스. `StageBuilder`가 맵 청크를 생성할 때마다 몬스터가 지나갈 월드 좌표 경로를 누적 관리한다.

- 생성자에서 `mapSize`, `tileSize`, 기준이 되는 `parent`(Transform, `battlespace`)를 받는다.
- `AddPath(Vector2Int mapOffset, List<Vector2Int> localPath)`: 맵 오프셋 기준 로컬 그리드 좌표를 월드 좌표로 변환해 `route`에 순서대로(베이스 → 외곽 방향) 추가. 직전 좌표와 동일하면 중복 추가 안 함.
- `GetWorldPosition`: 특정 맵 오프셋/로컬 좌표 하나를 월드 좌표로 변환(스폰 지점 계산용).
- `Route` (get only): 지금까지 누적된 전체 경로.
- `Clear()`: 스테이지 리셋 시 경로 초기화.

### StageBuilder (기존, 경로 생성 주체)
스테이지(타일 맵) 생성을 총괄하는 `MonoBehaviour`. 관련 부분:

- `Awake()`에서 `monsterRouteTracker = new StageMonsterRouteTracker(MapSize, TileSize, battlespace)` 생성.
- `GenerateNextStage()`가 새 맵 청크의 타일 경로(`path`)를 만들면:
  1. `monsterRouteTracker.AddPath(currentMapOffset, path)` — 누적 경로에 이번 청크 경로 추가
  2. `UpdateMonsterRoute()` → `monsterSpawn.SetRoute(monsterRouteTracker.Route)` — 누적 월드 경로 전체를 `MonsterSpawn`에 전달
  3. `UpdateMonsterSpawnPoint()` → 이번 청크 경로의 마지막 지점을 월드 좌표로 변환해 `monsterSpawn.SetSpawnPoint(worldPosition, Quaternion.identity)` 호출
  4. `startMonsterRound`가 true면(N키 등으로 트리거 시) `monsterSpawn.StartRound(currentMapCount)` 호출
- `ResetStage()` 시 `monsterRouteTracker.Clear()`도 함께 호출.

### MonsterSpawnWaveProvider (기존)
이동 경로를 주는 클래스가 아니라 **라운드별 스폰 구성(어떤 몬스터를 몇 마리, 어떤 딜레이/간격으로)** 을 제공하는 클래스.

- `monsterPrefabs`(`MonsterId` ↔ `GameObject`)와 `spawnTableName`을 인스펙터에서 설정.
- `Awake()` → `Load()`: `spawnTable.Load(spawnTableName)`로 테이블 로드, `monsterPrefabs`를 `Dictionary<string, GameObject>`로 변환.
- `TryGetWave(int round, out List<MonsterSpawnEntry> entries)`: 해당 라운드의 `MonsterSpawnData`(MonsterId, Count, StartDelay, SpawnInterval)를 가져와 실제 프리팹과 묶어 반환.

### MonsterSpawn (기존, 실제 스폰 주체)
몬스터를 실제로 스폰하고 `MonsterMove`에 경로를 꽂아주는 클래스.

- `SetSpawnPoint`/`SetRoute`: `StageBuilder`가 호출해주는 스폰 위치·누적 경로 저장.
- `StartRound(round)`: `DayNightManager`가 낮이면 스폰 안 함. `waveProvider.TryGetWave(round, ...)`로 스폰 구성을 가져와 `SpawnRoundAsync` 실행(UniTask 기반, 취소 가능).
- `SpawnRoundAsync`/`SpawnGroupAsync`: `StartDelay` 순 정렬 후 대기, 그룹 내 `SpawnInterval` 간격으로 순차 `Instantiate`.
- `SpawnPrefab`: 스폰 위치는 `generatedSpawnPosition` 우선(없으면 `fallbackSpawnPoint`). 생성된 몬스터의 `MonsterMove.SetRoute(GetSpawnRoute())` 호출.
- **`GetSpawnRoute()`가 핵심**: 누적 경로(베이스→외곽)를 **역순으로 뒤집어** 반환 → 몬스터는 외곽 스폰 지점에서 시작해 본진 방향으로 이동.
- `OnDisable`/`OnDestroy`/재시작 시 `CancellationTokenSource`로 스폰 태스크 취소.

## 4. 동작 흐름

```
StageBuilder.GenerateNextStage()
  └─ 타일 경로(path) 생성
  └─ StageMonsterRouteTracker.AddPath(경로 누적, 베이스→외곽 순)
  └─ MonsterSpawn.SetRoute(누적 경로 전체)
  └─ MonsterSpawn.SetSpawnPoint(이번 청크 종점 = 가장 바깥쪽 지점)
  └─ (N키 등으로 라운드 시작 시) MonsterSpawn.StartRound(round)
       └─ DayNightManager가 낮이면 스폰 안 함
       └─ MonsterSpawnWaveProvider.TryGetWave(round) → 라운드별 스폰 구성 조회
       └─ SpawnRoundAsync: StartDelay 순 대기 → SpawnGroupAsync: SpawnInterval 간격 Instantiate
       └─ SpawnPrefab: 스폰 위치에 Instantiate → MonsterMove.SetRoute(역순 경로)
            └─ MonsterMove.Update(): 외곽 → 베이스 방향으로 MoveTowards 이동
            └─ 경로 소진 시 Destroy(gameObject)
```

## 5. Issue #15 완료 기준 대조

| 완료 기준 | 결과 | 근거 |
|---|---|---|
| 1. 웨이포인트를 순서대로 따라간다 | ✅ 충족 | `MonsterMove.Update()`가 누적 경로를 인덱스 순서대로 `MoveTowards`로 이동. 경로 자체는 `StageWaypoint`(Top/Left/Bottom/Right) 정의 + `StageMonsterRouteTracker`가 스테이지 생성마다 누적한 월드 좌표. |
| 2. 경로 끝(본진)에 도달한다 | ✅ 충족(문구 기준) | `MonsterSpawn.GetSpawnRoute()`가 경로를 역순으로 넘겨 외곽→본진 방향 이동, 도달 시 `Destroy`. 본진 피해/이펙트는 이 기준에 명시돼 있지 않아 범위 밖으로 판단. |
| 3. 더미 → 실제 데이터/스폰 시스템 교체 확인 | #72 에서 같이 진행 | 처음부터 더미가 아니라 `MonsterSpawnWaveProvider`(실제 `MonsterSpawnTable`)와 `MonsterSpawn`(실제 UniTask 스폰 로직)을 사용 중. 다음 작업(근접 몬스터 프리팹 적용)에서 실제 몬스터 프리팹을 끼워 넣으면서 #26·#14 결과물 교체 확인까지 같이 진행할 예정. |

**결론**: 이슈 #15 기준으로 어긴 사항 없음. 1·2번 충족, 3번은 다음 작업(몬스터 프리팹 적용)에서 함께 확인 예정 — 이번 PR 범위에서는 보류.

## 6. 확인이 필요한 부분

- **#26(몬스터 데이터 설계)·#14(몬스터 스폰 시스템) 완료 여부**: 완료 기준 3번은 다음 작업(근접 몬스터 프리팹 적용)에서 실제 몬스터 프리팹 교체와 함께 확인 예정.
- **본진 도달 시 데미지 처리**: `MonsterMove`는 도달 시 `Destroy`만 함. 본진 HP 감소 등은 다른 이슈(본진 체력 시스템 등) 소관인지 확인 필요.
- **Enemy.cs FSM과의 연동**: `MonsterMove`에 이동 정지 API가 없어, 추후 `Enemy.cs`를 Idle/Move/Attack/Death FSM으로 개편할 때 Attack 상태에서 이동을 멈추는 처리(`SetPaused` 훅 또는 컴포넌트 `enabled` 토글)를 추가해야 함.
- **MonsterSpawnTable / MonsterSpawnData / MonsterSpawnEntry**: 원본 코드 미확인(CSV 기반 EnemyTable류로 추정). 필요 시 추가 문서화 가능.
- **DayNightManager 의존성**: 밤에만 스폰되도록 하드코딩돼 있어, 테스트 시 낮/밤 상태를 맞춰야 재현 가능.