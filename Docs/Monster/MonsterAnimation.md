# 몬스터 애니메이션/FSM 시스템

## 개요

몬스터의 이동, 공격, 사망 애니메이션은 `MonsterAnimation`과 `MonsterStateMachine`이 함께 관리한다.

현재 책임은 다음처럼 나뉜다.

```text
MonsterStateMachine
-> 상태 결정과 상태 진입/종료 처리

MonsterMove
-> 실제 경로 이동

MonsterAnimation
-> Animator 파라미터 변경

Animator
-> 실제 애니메이션 클립 재생
```

## 주요 컴포넌트

### MonsterStateMachine

몬스터 상태를 관리한다.

현재 상태:

```csharp
public enum MonsterState
{
    Idle,
    Move,
    Attack,
    Death
}
```

상태 전환 API:

```csharp
public void ChangeState(MonsterState nextState)
```

상태별 처리:

```text
Idle
-> 이동 비활성화
-> Move 애니메이션 Off
-> Attack 애니메이션 Off

Move
-> Attack 애니메이션 Off
-> 이동 활성화
-> Move 애니메이션 On

Attack
-> 이동 비활성화
-> Move 애니메이션 Off
-> Attack 애니메이션 On

Death
-> 이동 비활성화
-> Death 애니메이션 On
-> destroyDelay 이후 Destroy
```

현재 `Update()`에서는 공격/사망 상태가 아닐 때 `MonsterMove`의 상태를 보고 자동으로 Idle/Move를 맞춘다.

```csharp
if (monsterMove != null && monsterMove.CanMove && monsterMove.HasRouteRemaining)
{
    ChangeState(MonsterState.Move);
    return;
}

ChangeState(MonsterState.Idle);
```

에디터 테스트 입력:

```text
F: Move 상태
G: Attack 상태 토글
H: Death 상태
```

### MonsterMove

실제 위치 이동을 담당한다.

`MonsterMove`는 Animator 파라미터를 직접 바꾸지 않는다.

제공 정보:

```csharp
public bool HasRouteRemaining => currentRouteIndex < route.Count;
public bool CanMove => canMove;
```

이동 제어:

```csharp
public void SetMoveEnabled(bool enabled)
{
    canMove = enabled;
}
```

공격 상태에서는 `MonsterStateMachine`이 `SetMoveEnabled(false)`를 호출해 실제 위치 이동을 멈춘다.

### MonsterAnimation

Animator 파라미터만 변경한다.

제공 API:

```csharp
SetMoveAnimation(bool isMoving)
SetAttackAnimation(bool isAttacking)
PlayDeathAnimation()
```

`MonsterAnimation`은 `MonsterMove`를 직접 참조하지 않는다.  
이동 정지, 공격 시작, 사망 삭제 같은 상태 처리 책임은 `MonsterStateMachine`에 있다.

## Animator 파라미터

### IsMove

타입: `Bool`

```text
true  = Walk
false = Idle
```

### IsAttack

타입: `Bool`

```text
true  = Attack
false = Idle 또는 Move 가능 상태
```

### IsDie

타입: `Bool`

```text
true = Die
```

사망 후에는 다시 `false`로 돌리지 않는다.

## Animator 구조

```text
Entry
 ↓
Idle

Idle <-> Walk

Any State -> Attack
Attack -> Idle

Any State -> Die
```

## Transition 권장 설정

### Idle -> Walk

```text
Condition: IsMove == true
Has Exit Time: Off
```

### Walk -> Idle

```text
Condition: IsMove == false
Has Exit Time: Off
```

### Any State -> Attack

```text
Condition: IsAttack == true
Has Exit Time: Off
```

### Attack -> Idle

```text
Condition: IsAttack == false
Has Exit Time: On
```

공격 애니메이션을 반복해야 한다면 Attack 클립의 `Loop Time`을 켠다.

### Any State -> Die

```text
Condition: IsDie == true
Has Exit Time: Off
```

## 사용 예시

### 이동 상태로 전환

```csharp
monsterStateMachine.ChangeState(MonsterState.Move);
```

결과:

```text
MonsterMove.SetMoveEnabled(true)
MonsterAnimation.SetMoveAnimation(true)
```

### 공격 상태로 전환

```csharp
monsterStateMachine.ChangeState(MonsterState.Attack);
```

결과:

```text
MonsterMove.SetMoveEnabled(false)
MonsterAnimation.SetMoveAnimation(false)
MonsterAnimation.SetAttackAnimation(true)
```

### 공격 종료

```csharp
monsterStateMachine.ChangeState(MonsterState.Move);
```

또는 공격 대상이 사라졌다면:

```csharp
monsterStateMachine.ChangeState(MonsterState.Idle);
```

### 사망 상태로 전환

```csharp
monsterStateMachine.ChangeState(MonsterState.Death);
```

결과:

```text
MonsterMove.SetMoveEnabled(false)
MonsterAnimation.PlayDeathAnimation()
Destroy(gameObject, destroyDelay)
```

## 주의사항

공격 상태로 들어가면 `MonsterStateMachine.Update()`는 자동 Move/Idle 전환을 하지 않는다.

```csharp
if (currentState == MonsterState.Attack || currentState == MonsterState.Death)
{
    return;
}
```

따라서 공격이 끝났을 때는 반드시 다른 상태로 전환해야 한다.

```csharp
monsterStateMachine.ChangeState(MonsterState.Move);
```

또는:

```csharp
monsterStateMachine.ChangeState(MonsterState.Idle);
```

`MonsterAnimation.SetAttackAnimation(false)`만 직접 호출하면 FSM 상태는 여전히 `Attack`일 수 있다.  
상태 변경은 `MonsterStateMachine.ChangeState()`를 통해 처리하는 것이 안전하다.

## 프리팹 구성 체크리스트

몬스터 프리팹 또는 몬스터 루트 오브젝트에 다음 컴포넌트가 필요하다.

```text
MonsterMove
MonsterAnimation
MonsterStateMachine
Animator
```

`MonsterStateMachine`은 `MonsterMove`와 `MonsterAnimation`을 자식에서 자동으로 찾는다.

```csharp
monsterMove = GetComponentInChildren<MonsterMove>();
monsterAnimation = GetComponentInChildren<MonsterAnimation>();
```

Animator의 `Apply Root Motion`은 꺼두는 것이 좋다.  
켜져 있으면 공격 애니메이션 클립이 실제 위치를 움직일 수 있다.
