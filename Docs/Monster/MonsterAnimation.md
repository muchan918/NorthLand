# 일반 몬스터 애니메이션/FSM 적용 현황

## 개요

일반 몬스터의 이동, 공격, 사망 애니메이션을 연결하기 위해 `MonsterMove`, `MonsterAnimation`, `MonsterStateMachine` 구조를 추가했다.

현재 작업은 몬스터가 경로를 따라 이동하고, 상태에 맞는 애니메이션을 재생할 수 있도록 FSM 기반 구조를 정리한 것이다.

## 현재 구현된 구조

### MonsterMove

몬스터의 실제 경로 이동을 담당한다.

역할:

- 스폰 시 `MonsterSpawn`으로부터 이동 경로를 받음
- `Vector3.MoveTowards`로 현재 목표 지점을 향해 이동
- 목표 지점에 도착하면 다음 경로 인덱스로 진행
- `SetMoveEnabled(bool)`로 이동 가능 여부 제어

제공 API:

```csharp
public bool HasRouteRemaining
public bool CanMove
public void SetMoveEnabled(bool enabled)
public void SetRoute(List<Vector3> routePoints)
```

`MonsterMove`는 애니메이션을 직접 제어하지 않는다.

### MonsterAnimation

Animator 파라미터만 변경한다.

역할:

- Move 애니메이션 bool 설정
- Attack 애니메이션 bool 설정
- Death 애니메이션 bool 설정

제공 API:

```csharp
SetMoveAnimation(bool isMoving)
SetAttackAnimation(bool isAttacking)
PlayDeathAnimation()
```

Animator 파라미터:

```text
IsMove   : Bool
IsAttack : Bool
IsDie    : Bool
```

`MonsterAnimation`은 `MonsterMove`를 직접 참조하지 않는다.

### MonsterStateMachine

일반 몬스터 상태를 관리한다.

상태:

```csharp
public enum MonsterState
{
    Idle,
    Move,
    Attack,
    Death
}
```

역할:

- 현재 상태 보관
- 상태 진입/종료 처리
- Move 상태에서 이동 활성화 및 Walk 애니메이션 재생
- Attack 상태에서 이동 정지 및 Attack 애니메이션 재생
- Death 상태에서 이동 정지, Death 애니메이션 재생, 지연 Destroy 처리

주요 API:

```csharp
public void ChangeState(MonsterState nextState)
```

Death 처리:

```csharp
monsterMove?.SetMoveEnabled(false);
monsterAnimation?.PlayDeathAnimation();
Destroy(gameObject, destroyDelay);
```

## 현재 동작 흐름

### 이동

```text
MonsterSpawn
-> MonsterMove.SetRoute()
-> MonsterMove.Update()
-> 경로를 따라 MoveTowards 이동
-> MonsterStateMachine이 Move 상태 감지
-> MonsterAnimation.SetMoveAnimation(true)
```

### 공격

공격 상태는 `MonsterStateMachine.ChangeState(MonsterState.Attack)`으로 진입한다.

```text
Attack 상태 진입
-> MonsterMove.SetMoveEnabled(false)
-> MonsterAnimation.SetMoveAnimation(false)
-> MonsterAnimation.SetAttackAnimation(true)
```

공격이 끝나면 반드시 다른 상태로 전환해야 한다.

```csharp
monsterStateMachine.ChangeState(MonsterState.Move);
```

또는:

```csharp
monsterStateMachine.ChangeState(MonsterState.Idle);
```

### 사망

```text
Death 상태 진입
-> 이동 정지
-> Death 애니메이션 재생
-> destroyDelay 후 Destroy
```

## 현재 완료된 부분

- 몬스터 경로 이동용 `MonsterMove` 구현
- 누적 경로를 몬스터에게 전달하는 스폰 흐름 구성
- `MonsterAnimation`으로 Animator 파라미터 제어
- `MonsterStateMachine` 추가
- `Idle / Move / Attack / Death` 상태 정의
- Move 상태에서 이동 활성화 및 Walk 애니메이션 연결
- Attack 상태에서 이동 정지 및 Attack 애니메이션 연결
- Death 상태에서 Death 애니메이션 후 지연 Destroy 연결
- 공격 중 실제 위치 이동이 계속되는 문제를 `SetMoveEnabled(false)` 구조로 해결
- `MonsterAnimation`과 `MonsterMove`의 직접 의존 제거

## Animator 설정

Animator Controller에는 다음 상태가 필요하다.

```text
Idle
Walk
Attack
Death
```

권장 구조:

```text
Entry -> Idle

Idle <-> Walk

Any State -> Attack
Attack -> Idle

Any State -> Death
```

파라미터:

```text
IsMove   Bool
IsAttack Bool
IsDie    Bool
```

권장 Transition:

```text
Idle -> Walk
Condition: IsMove == true
Has Exit Time: Off

Walk -> Idle
Condition: IsMove == false
Has Exit Time: Off

Any State -> Attack
Condition: IsAttack == true
Has Exit Time: Off

Attack -> Idle
Condition: IsAttack == false
Has Exit Time: On

Any State -> Death
Condition: IsDie == true
Has Exit Time: Off
```

공격을 반복해야 한다면 Attack 애니메이션 클립의 `Loop Time`을 켠다.

## 완료 기준 대조

| 완료 기준 | 현재 상태 |
|---|---|
| `MonsterStateMachine`으로 Idle/Move/Attack/Death 상태 관리 | 구현됨 |
| Animator 상태가 FSM 상태와 매핑 | 구현됨 |
| 공격 시 Attack 애니메이션 재생 | 구현됨 |
| 사망 시 Death 애니메이션 재생 | 구현됨 |
| Move 상태에서 Walk 애니메이션 재생 | 구현됨 |
| 공격 중 실제 이동 정지 | 구현됨 |
| 사망 애니메이션 후 지연 Destroy | 구현됨 |

## 현재 결론

이번 작업으로 몬스터 애니메이션과 이동 제어의 기본 구조는 정리되었다.

```text
MonsterStateMachine
-> 상태 전환 담당

MonsterMove
-> 실제 이동 담당

MonsterAnimation
-> Animator 파라미터 변경 담당
```
