# 몬스터 이동 시스템

## 개요

몬스터 이동 시스템은 맵 생성 결과로 만들어진 순서 있는 경로를 `MonsterSpawn`에 전달하고, 스폰된 몬스터의 이동 에이전트가 해당 경로를 따라 본진 방향으로 이동하게 하는 구조이다.

Issue #209부터 몬스터의 공격 유형과 이동 유형을 분리한다.

- 공격 유형: `EnemyType`
  - `Melee`
  - `Ranged`
  - `Boss`
- 이동 유형: `MovementMode`
  - `Ground`
  - `Flying`

따라서 근접 공격을 사용하는 몬스터도 `MovementMode.Flying`을 사용하면 공중 이동이 가능하고, 이후 비행 보스도 같은 이동 유형을 사용할 수 있다.

현재 이동 흐름은 다음과 같다.

```text
맵 경로 생성
→ MonsterSpawn.SetRoute()
→ MonsterSpawn.GetSpawnRoute()
→ IRouteMovementAgent.SetRoute()
→ MonsterMove 또는 FlyingMonsterMove
```

몬스터는 경로의 끝 지점에서 생성되고, 누적된 경로를 역순으로 받아 본진 방향으로 이동한다.

---

## 이동 인터페이스

### IMovementAgent

`IMovementAgent`는 경로 형태와 관계없는 공통 이동속도 및 정지 계약이다.

파일 위치:

```text
Assets/Scripts/CombatSystem/IMovementAgent.cs
```

주요 API:

```csharp
public interface IMovementAgent
{
    bool IsStopped { get; set; }

    void SetMoveSpeed(float moveSpeed);

    float EffectiveMoveSpeed { get; }

    float PatternSpeedFactor { get; set; }

    void AddSpeedDebuff(int sourceId, float factor);

    void RemoveSpeedDebuff(int sourceId);
}
```

이 인터페이스는 다음 시스템이 사용한다.

- `Enemy`: 타겟 발견 시 이동 정지
- `EnemyAgent`: 보스 행동 트리의 이동속도 제어
- 감속 타워: 소스별 이동속도 디버프 적용
- `MonsterMove`: 지상 이동
- `FlyingMonsterMove`: 공중 이동

완전 정지는 이동속도 배수를 0으로 만드는 대신 `IsStopped`로 표현한다.

이동속도 계산에는 하한값이 있기 때문에 패턴 배수나 디버프 배수를 0으로 설정해도 완전히 정지하지 않는다. 이는 감속 효과로 몬스터가 영구 정지해 웨이브가 종료되지 않는 상황을 방지하기 위한 규칙이다.

### IRouteMovementAgent

`IRouteMovementAgent`는 `IMovementAgent`에 경로 추종 기능을 추가한 인터페이스이다.

```csharp
public interface IRouteMovementAgent : IMovementAgent
{
    bool HasRouteRemaining { get; }

    event Action RouteCompleted;

    void SetRoute(IReadOnlyList<Vector3> routePoints);

    void SetMoveEnabled(bool enabled);
}
```

구현체:

- `MonsterMove`: 모든 웨이포인트를 따라가는 지상 이동
- `FlyingMonsterMove`: 일부 웨이포인트를 선택해 직선으로 이동하는 공중 이동

`Enemy`, `MonsterSpawn`, `MonsterStateMachine`은 구체 클래스인 `MonsterMove`를 직접 참조하지 않고 `IRouteMovementAgent`를 사용한다.

따라서 새로운 이동 방식이 추가되더라도 이 인터페이스를 구현하면 기존 스폰·전투·상태 머신 흐름을 재사용할 수 있다.

---

## 이동속도 합성

### MoveSpeedComposer

지상과 공중 이동의 이동속도 계산은 순수 C# 클래스인 `MoveSpeedComposer`가 담당한다.

파일 위치:

```text
Assets/Scripts/CombatSystem/MoveSpeedComposer.cs
```

최종 이동속도 계산식:

```text
최종 이동속도
= max(
    minMoveSpeed,
    기준 이동속도 × 패턴 이동속도 배수
    × max(0.5, 모든 디버프 이동속도 배수)
  )
```

감속 디버프는 감속 전 속도의 50% 아래로 내리지 못하고, 최종 결과에는
`minMoveSpeed` 절대 하한도 적용된다. 따라서 패턴 배수가 0이어도 완전히 정지하지 않는다.

기본 설정:

```text
fallbackMoveSpeed = 3
minMoveSpeed = 0.15
patternSpeedFactor = 1
```

속도 축의 역할:

- 기준 이동속도
  - `EnemyAsset`의 `MoveSpeed`
  - `Enemy`가 `SetMoveSpeed()`로 주입
- 패턴 배수
  - 보스 돌진 가속 또는 방어 태세 감속
- 디버프 배수
  - 이동속도 감소 타워 등 외부 효과
  - `sourceId`별로 저장
  - 같은 `sourceId`를 다시 적용하면 기존 값을 갱신
  - 서로 다른 디버프는 곱산

`MonsterMove`와 `FlyingMonsterMove`는 속도 계산을 직접 구현하지 않고 `MoveSpeedComposer`에 위임한다.

따라서 속도 합성 규칙이 변경되더라도 지상·공중 이동 코드를 각각 수정할 필요가 없다.

---

## 경로 공급

### StageBuilder 경로

기존 `StageBuilder` 경로에서는 `StageMonsterRouteTracker`가 맵 로컬 좌표를 월드 좌표로 변환하고 순서 있는 경로를 누적한다.

주요 흐름:

```text
StageBuilder
→ StageMonsterRouteTracker
→ MonsterSpawn.SetRoute()
```

`StageBuilder`의 주요 역할:

- 현재 맵 청크의 길 생성
- `StageMonsterRouteTracker`에 누적 경로 추가
- `MonsterSpawn`에 전체 경로 전달
- 마지막 길 좌표를 몬스터 스폰 지점으로 설정
- 라운드 시작 시 `MonsterSpawn.StartRound()` 호출

공개 경로:

```csharp
public IReadOnlyList<Vector3> MonsterRoute
```

### CombatMap 경로

현재 절차적 전투 맵에서는 `CombatMapTileSpawner.CurrentWorldEnemyRoute`가 월드 좌표 기준 적 이동 경로를 제공한다.

`CombatMapMonsterConnector`는 생성된 경로와 스폰 위치를 `MonsterSpawn`에 전달한다.

주요 흐름:

```text
CombatMapTileSpawner.CurrentWorldEnemyRoute
→ CombatMapMonsterConnector
→ MonsterSpawn.SetRoute()
→ MonsterSpawn.SetSpawnPoint()
```

경로 공급자가 달라도 `MonsterSpawn` 이후의 이동 흐름은 동일하다.

---

## MonsterSpawn

`MonsterSpawn`은 몬스터를 생성하고 이동 에이전트에 경로를 주입한다.

파일 위치:

```text
Assets/Scripts/Monster/MonsterSpawn/MonsterSpawn.cs
```

주요 역할:

- `SetSpawnPoint()`로 스폰 위치 저장
- `SetRoute()`로 전체 경로 저장
- `StartRound()`로 라운드별 스폰 시작
- `SpawnRoundAsync()`와 `SpawnGroupAsync()`로 시간차 스폰
- 프리팹에서 `Enemy`와 `IRouteMovementAgent` 검색
- 생성된 이동 에이전트에 역순 경로 전달
- 데이터 이동 유형과 실제 이동 컴포넌트의 일치 여부 검증

몬스터는 맵 경로의 끝 지점에서 생성되므로 경로를 역순으로 전달한다.

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

스폰된 몬스터의 이동 컴포넌트는 구체 타입으로 찾지 않는다.

```csharp
IRouteMovementAgent routeMovement =
    monster.GetComponentInChildren<IRouteMovementAgent>();
```

필수 구성:

```text
Enemy
+ IRouteMovementAgent 구현 컴포넌트
```

지상 몬스터 예시:

```text
Enemy
+ MonsterMove
```

공중 몬스터 예시:

```text
Enemy
+ FlyingMonsterMove
```

`MovementMode.Flying`인데 `FlyingMonsterMove`가 연결되지 않은 경우 스폰 시 오류를 출력하고 해당 오브젝트를 제거한다.

이 검증은 다음과 같은 잘못된 구성을 조기에 발견하기 위한 것이다.

```text
EnemyAsset.MovementMode = Flying
프리팹 이동 컴포넌트 = MonsterMove
```

---

## 지상 이동

### MonsterMove

`MonsterMove`는 전달받은 모든 경로 지점을 순서대로 따라간다.

파일 위치:

```text
Assets/Scripts/Monster/MonsterMoveMent/MonsterMove.cs
```

주요 역할:

- `SetRoute()`로 경로 저장
- 현재 경로 인덱스 관리
- `Vector3.MoveTowards()`로 다음 지점을 향해 이동
- 현재 지점 도착 시 다음 지점으로 전환
- 수평 이동 방향으로 즉시 회전
- 경로 종료 시 `RouteCompleted` 이벤트 발행
- `SetMoveEnabled()`로 상태 머신의 이동 허용 여부 적용
- 실제 이동속도는 `MoveSpeedComposer`에서 조회

지상 이동은 경로의 모든 웨이포인트를 사용한다.

```text
경로: 0 → 1 → 2 → 3 → 4 → 마지막 지점
이동: 0 → 1 → 2 → 3 → 4 → 마지막 지점
```

지상 몬스터는 길의 모서리를 그대로 따라 이동한다.

---

## 공중 이동

### FlyingMonsterMove

`FlyingMonsterMove`는 전달받은 지상 경로를 공중 이동 지점 생성에 재사용한다.

파일 위치:

```text
Assets/Scripts/Monster/MonsterMoveMent/FlyingMonsterMove.cs
```

공중 몬스터는 모든 웨이포인트를 그대로 따라가지 않는다.

`waypointStep` 간격으로 지점을 선택하고 각 지점에 `altitude`를 더한 뒤, 선택된 지점 사이를 직선으로 이동한다.

예시:

```text
원본 경로:
0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10

waypointStep = 5인 공중 경로:
0 → 5 → 10
```

이 방식으로 공중 몬스터는 지상 경로의 모서리를 그대로 돌지 않고 일부 구간을 직선으로 가로지른다.

이것은 공중 몬스터가 지상 유닛과 다른 이동 형태를 갖도록 하기 위한 의도된 동작이다.

주요 설정:

```text
altitude
- 원본 경로 지점에 추가할 비행 고도

waypointStep
- 원본 경로에서 몇 번째 지점마다 공중 목적지로 사용할지 결정

turnSpeed
- 진행 방향으로 회전할 때 사용하는 보간 속도

arriveDistance
- 현재 목적지에 도착했다고 판단하는 거리
```

마지막 경로 지점은 `waypointStep`으로 정확히 선택되지 않더라도 반드시 공중 경로에 포함한다.

```csharp
int finalIndex = routePoints.Count - 1;

if (lastAddedIndex != finalIndex)
{
    route.Add(ApplyAltitude(routePoints[finalIndex]));
}
```

각 경로 지점에는 고도 오프셋을 적용한다.

```csharp
private Vector3 ApplyAltitude(Vector3 point)
{
    point.y += altitude;
    return point;
}
```

공중 몬스터는 지면에서 생성된 뒤 상승하지 않는다.

경로가 전달되면 첫 번째 공중 지점으로 위치를 옮겨 처음부터 비행 고도에서 시작한다.

```csharp
transform.position = route[0];
```

회전은 수직 차이를 제외하고 수평 이동 방향으로 계산한다.

```csharp
Vector3 lookDirection = direction;
lookDirection.y = 0f;
```

그다음 `Quaternion.Slerp()`를 사용해 진행 방향으로 부드럽게 회전한다.

공중 이동의 실제 위치 변경에는 지상 이동과 동일한 합성 속도를 사용한다.

```csharp
transform.position = Vector3.MoveTowards(
    transform.position,
    targetPosition,
    EffectiveMoveSpeed * Time.deltaTime
);
```

---

## 이동과 전투 연결

### Enemy

`Enemy`는 `IRouteMovementAgent`를 찾아 전투 상태에 따라 이동을 제어한다.

```csharp
routeMovement =
    GetComponentInChildren<IRouteMovementAgent>();
```

타겟을 발견하면:

```text
IsStopped = true
→ 이동 정지
→ MonsterStateMachine.Attack
→ 공격 애니메이션 실행
→ 공격 주기에 따라 피해 적용
```

타겟을 찾지 못하면:

```text
IsStopped = false
→ MonsterStateMachine.Move
→ 경로 이동 계속
```

`Enemy.MovementMode`는 외부 시스템이 해당 몬스터의 이동 유형을 확인하는 공개 창구이다.

```csharp
public MovementMode MovementMode =>
    data != null
        ? data.MovementMode
        : global::MovementMode.Ground;
```

후속 안티에어 타겟팅은 이동 컴포넌트의 구체 타입을 검사하지 않고 이 접근자를 사용한다.

현재는 별도의 공중 물리 레이어를 사용하지 않는다. 공중 몬스터도 기존 `Enemy` 레이어를 사용하므로 기존 타워가 공격할 수 있다.

### MonsterStateMachine

`MonsterStateMachine`은 이동 에이전트와 애니메이션 상태를 함께 제어한다.

상태별 동작:

```text
Idle
→ SetMoveEnabled(false)
→ 이동 애니메이션 끄기
→ 공격 애니메이션 끄기

Move
→ SetMoveEnabled(true)
→ 이동 애니메이션 켜기
→ 공격 애니메이션 끄기

Attack
→ SetMoveEnabled(false)
→ 이동 애니메이션 끄기
→ 공격 애니메이션 켜기

Death
→ IsStopped = true
→ SetMoveEnabled(false)
→ 사망 애니메이션 재생
→ destroyDelay 후 오브젝트 제거
```

지상과 공중 몬스터는 같은 상태 머신을 사용한다.

공중 몬스터는 사망할 때 지상으로 떨어지지 않고 현재 공중 위치에서 사망 애니메이션을 재생한 뒤 제거된다.

---

## 경로 완료

`MonsterMove`와 `FlyingMonsterMove`는 경로가 끝나면 직접 오브젝트를 제거하지 않고 `RouteCompleted` 이벤트를 발행한다.

`Enemy`는 이 이벤트를 구독한다.

```text
IRouteMovementAgent.RouteCompleted
→ Enemy.HandleRouteCompleted()
```

정상적인 경우 몬스터는 마지막 지점에 도착하기 전에 본진을 공격 대상으로 발견하고 정지한다.

공중 몬스터가 본진을 발견하지 못한 상태로 마지막 경로 지점에 도착하면 경고를 출력하고 오브젝트를 제거한다.

```text
공중 경로 완료
→ 본진 미발견 경고
→ 몬스터 제거
→ monsterParent.childCount 감소
```

공중 몬스터의 `AttackRange`는 비행 고도에서도 본진을 발견할 수 있을 만큼 커야 한다.

현재 박쥐는 기존 근접 공격 흐름을 사용한다.

```text
본진 발견
→ 비행 정지
→ 공격 애니메이션
→ 즉시 근접 피해 적용
→ 공격 주기마다 반복
```

---

## 예시 공중 몬스터

### Flying Bat

예시 공중 몬스터는 박쥐 프리팹과 데이터 에셋으로 구성한다.

메인 저장소 데이터:

```text
Assets/Resources/ScriptableObjects/Enemies/flying_bat.asset
```

Imported 저장소 프리팹:

```text
Assets/Imported/NorthLand-Imported/@NorthLand/Prefabs/Monster/Flying_Bat.prefab
```

필수 구성:

```text
Enemy
FlyingMonsterMove
MonsterStateMachine
MonsterAnimation
Animator
Collider
```

데이터 설정:

```text
EnemyType = Melee
MovementMode = Flying
```

박쥐는 기존 `Enemy` 레이어를 사용한다.

따라서 현재 타워는 지상·공중 구분 없이 박쥐를 공격할 수 있다. 공중 몬스터만 공격하거나 공중 몬스터를 제외하는 안티에어 타겟팅은 별도 이슈에서 구현한다.

---

## 전체 동작 흐름

```text
맵 경로 생성
→ MonsterSpawn.SetRoute()
→ MonsterSpawn.SetSpawnPoint()
→ MonsterSpawn.StartRound()
→ MonsterSpawn.SpawnPrefab()
→ Enemy + IRouteMovementAgent 검사
→ MovementMode와 이동 컴포넌트 일치 검사
→ IRouteMovementAgent.SetRoute(GetSpawnRoute())
→ MonsterMove 또는 FlyingMonsterMove 이동
→ Enemy가 본진 탐색
→ 본진 발견 시 이동 정지 및 공격
→ 사망 시 공중/지상 현재 위치에서 사망 애니메이션
→ destroyDelay 후 제거
```

공중 몬스터가 본진을 찾지 못하고 경로를 완료한 경우:

```text
FlyingMonsterMove.RouteCompleted
→ Enemy.HandleRouteCompleted()
→ 경고 출력
→ 오브젝트 제거
```

---

## Issue #15 완료 기준 대조

| 완료 기준 | 현재 상태 | 근거 |
|---|---|---|
| 몬스터가 스폰 후 지정된 웨이포인트를 순서대로 따라간다 | 충족 | `MonsterMove`가 모든 경로 지점을 순서대로 따라간다 |
| 경로 끝에 도달한다 | 충족 | `IRouteMovementAgent.RouteCompleted`로 경로 완료를 통지한다 |
| 실제 몬스터 데이터 및 스폰 시스템과 연결된다 | 충족 | `MonsterSpawn`이 `Enemy`와 `IRouteMovementAgent`를 검사하고 경로를 주입한다 |

## Issue #209 완료 기준 대조

| 완료 기준 | 현재 상태 | 근거 |
|---|---|---|
| 공중 몬스터 유형 데이터 구분 | 충족 | `EnemyAsset.MovementMode`에 `Ground/Flying` 정의 |
| 공중 이동 에이전트 구현 | 충족 | `FlyingMonsterMove`가 `IRouteMovementAgent` 구현 |
| 지정 지점을 향해 일정 고도로 이동 | 충족 | 경로 샘플링 후 각 지점에 `altitude` 적용 |
| 지상 경로를 그대로 추종하지 않음 | 충족 | 모든 지점을 따라가지 않고 선택된 지점 사이를 직선 이동 |
| MonsterSpawn에서 공중 몬스터 스폰 가능 | 충족 | 구체 `MonsterMove` 대신 `IRouteMovementAgent`로 경로 주입 |
| 본진 도달 및 공격 흐름 정합 | 충족 | `Enemy`가 본진 탐색 후 정지·공격 |
| 웨이브 클리어 흐름 정합 | 충족 | 사망 또는 경로 완료 제거 후 `monsterParent.childCount` 감소 |
| 예시 공중 몬스터 1종 검증 | 충족 | `Flying_Bat` 프리팹과 `flying_bat` 데이터 에셋 |
| 안티에어 타겟팅 분리 | 범위 밖 | 현재 기존 Enemy 레이어를 사용하며 후속 이슈에서 구분 |

---

## 주의 사항

- `MovementMode`는 공중·지상 판별의 데이터 정본이다.
- 외부 시스템은 `FlyingMonsterMove` 컴포넌트 존재 여부 대신 `Enemy.MovementMode`를 사용한다.
- `MovementMode.Flying` 프리팹에는 반드시 `FlyingMonsterMove`가 있어야 한다.
- 지상·공중 이동 모두 속도 계산을 `MoveSpeedComposer`에 위임한다.
- 완전 정지는 속도 배수가 아니라 `IsStopped`로 처리한다.
- 공중 몬스터의 고도와 공격 사거리를 함께 확인해야 한다.
- `waypointStep`이 커질수록 경로 모서리를 더 크게 가로지르고 본진 도달 시간이 짧아질 수 있다.
- `FlyingMonsterMove`가 자식 오브젝트에 연결되면 첫 공중 지점으로 이동할 때 자식만 움직일 수 있으므로 현재 프리팹에서는 루트에 배치한다.
- Imported 저장소의 프리팹과 애니메이션 변경은 메인 저장소와 별도로 커밋하고 동기화해야 한다.
- 새로운 이동 구현을 추가할 때는 `IRouteMovementAgent`를 구현하고 `MonsterSpawn`의 구성 검증 규칙도 함께 확장한다.
