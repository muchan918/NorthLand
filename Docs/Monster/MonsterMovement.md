# 몬스터 이동 시스템

## 개요

Issue #15의 몬스터 이동 시스템은 스테이지 생성 결과로 만들어진 길을 몬스터가 순서대로 따라가게 하는 구조이다.

현재 구현은 다음 흐름을 가진다.

```text
StageBuilder
-> StageMonsterRouteTracker
-> MonsterSpawn
-> MonsterMove
```

몬스터는 스테이지의 끝 지점에서 생성되고, 누적된 길 경로를 역순으로 받아 본진 방향으로 이동한다.

## 주요 컴포넌트

### StageBuilder

스테이지 맵과 길을 생성하는 주체이다.

관련 역할:

- 현재 맵 청크의 길 `path` 생성
- `StageMonsterRouteTracker`에 누적 경로 추가
- `MonsterSpawn`에 전체 경로 전달
- 현재 청크의 마지막 길 좌표를 스폰 지점으로 설정
- 라운드 시작 시 `MonsterSpawn.StartRound()` 호출

주요 흐름:

```csharp
monsterRouteTracker.AddPath(currentMapOffset, path);
UpdateMonsterRoute();
UpdateMonsterSpawnPoint();
```

`StageBuilder.MonsterRoute`는 현재 누적된 몬스터 이동 경로를 읽기 위한 공개 API이다.

```csharp
public IReadOnlyList<Vector3> MonsterRoute
```

### StageMonsterRouteTracker

맵 로컬 좌표를 월드 좌표로 변환하고, 몬스터가 따라갈 순서 있는 경로를 누적한다.

관련 역할:

- `mapOffset + localPath`를 월드 좌표로 변환
- 여러 맵 청크의 길을 하나의 순서 있는 경로로 누적
- 직전 좌표와 같은 좌표는 중복 추가하지 않음
- 스폰 지점이나 최종 지점 계산에 필요한 `GetWorldPosition()` 제공

현재 파일 위치:

```text
Assets/Scripts/Monster/MonsterMoveMent/StageMonsterRouteTracker.cs
```

주의:

현재 `Route`는 `List<Vector3>`로 노출된다.

```csharp
public List<Vector3> Route => route;
```

외부 수정 가능성을 막으려면 추후 `IReadOnlyList<Vector3>`로 바꾸는 것이 좋다.

### MonsterSpawn

실제 몬스터를 생성하고, 생성된 몬스터의 `MonsterMove`에 경로를 주입한다.

관련 역할:

- `SetSpawnPoint()`로 스폰 위치 저장
- `SetRoute()`로 누적 경로 저장
- `StartRound()`로 라운드별 스폰 시작
- `SpawnRoundAsync()` / `SpawnGroupAsync()`로 시간차 스폰 처리
- 생성 직후 `MonsterMove.SetRoute()` 호출

현재 몬스터는 누적 경로를 그대로 쓰지 않고 역순으로 받는다.

```csharp
private List<Vector3> GetSpawnRoute()
{
    spawnRoute.Clear();

    for (int i = route.Count - 1; i >= 0; i--)
    {
        spawnRoute.Add(route[i]);
    }

    return spawnRoute;
}
```

이유:

```text
StageBuilder는 길의 끝 지점을 몬스터 스폰 위치로 잡는다.
따라서 몬스터는 누적 경로를 역순으로 따라가야 본진 방향으로 이동한다.
```

### MonsterMove

몬스터의 실제 위치 이동만 담당한다.

관련 역할:

- `SetRoute()`로 이동 경로 주입
- `Update()`에서 현재 목표 지점을 향해 `Vector3.MoveTowards()` 이동
- 목표 지점 도착 시 다음 인덱스로 이동
- `SetMoveEnabled()`로 이동 가능 여부 제어

현재 `MonsterMove`는 애니메이션을 직접 제어하지 않는다.  
이동/공격/사망 상태에 따른 애니메이션 전환은 `MonsterStateMachine`이 담당한다.

주요 API:

```csharp
public bool HasRouteRemaining => currentRouteIndex < route.Count;
public bool CanMove => canMove;
public void SetMoveEnabled(bool enabled)
```

경로가 끝났을 때 `MonsterMove`는 오브젝트를 삭제하지 않는다.  
삭제는 `MonsterStateMachine`의 `Death` 상태에서 처리한다.

## 동작 흐름

```text
StageBuilder.GenerateNextStage()
-> 현재 맵 청크 길 생성
-> StageMonsterRouteTracker.AddPath()
-> MonsterSpawn.SetRoute()
-> MonsterSpawn.SetSpawnPoint()
-> MonsterSpawn.StartRound()
-> MonsterSpawn.SpawnPrefab()
-> MonsterMove.SetRoute(GetSpawnRoute())
-> MonsterMove.Update()
-> MoveTowards로 경로 이동
```

## Issue #15 완료 기준 대조

| 완료 기준 | 현재 상태 | 근거 |
|---|---|---|
| 몬스터가 스폰 후 지정된 웨이포인트를 순서대로 따라간다 | 충족 | `MonsterMove`가 `route[currentRouteIndex]`를 순서대로 따라감 |
| 경로 끝에 도달한다 | 부분 충족 | 경로 인덱스 끝까지 이동 가능. 본진 HP 감소 등 도착 효과는 별도 시스템 필요 |
| 실제 몬스터 데이터/스폰 시스템과 교체 확인 | 진행 중 | `MonsterSpawnWaveProvider`, `MonsterSpawnTable`, `MonsterSpawn`과 연결되어 있으나 실제 프리팹/전투 데이터 검증은 추가 확인 필요 |

## 남은 확인 사항

- 경로 끝 도착 시 본진 피해 처리
- 실제 몬스터 프리팹에 `MonsterMove`, `MonsterAnimation`, `MonsterStateMachine`이 모두 붙어 있는지
- `StageMonsterRouteTracker.Route`를 읽기 전용으로 바꿀지 여부
- `StageMonsterRouteTracker` 파일 위치를 `CombatSpace/MapBuilder`로 옮길지 여부
- `MonsterSpawnWaveProvider`의 프리팹 매핑 누락 로그 확인
