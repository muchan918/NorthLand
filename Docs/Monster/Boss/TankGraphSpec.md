# Tank(임시명) BT 그래프 배선 스펙

- 관련 이슈: #235
- 그래프 에셋: `Assets/Behavior/TankBehavior.asset` (신규 — Project 우클릭 → Create → Behavior → Behavior Graph)
- 보스 에셋: `Assets/Resources/ScriptableObjects/Enemies/tank.asset`
- 보스 프리팹: `Assets/Prefabs/Monster/Tank.prefab`
- 설계 근거: `BossDesign.md` 「행동 패턴」 / 노드 정의: `BossNodeReference.md`

이 문서는 **그래프 에디터에서 손으로 배선할 때 보는 스펙**이다. 노드 이름은 에디터 검색창에 뜨는 표시 이름 그대로 적었다(패키지 `NodeRegistry`에서 뽑음). 배선이 끝나면 실제 구조에 맞춰 이 문서를 갱신한다.

> **Step 1은 애니메이터 없이 검증한다.** 보스 프리팹이 캡슐이고 `Animator`가 없어서 `Enemy Play Animation`은 **실패를 반환**한다(P1 시퀀스가 거기서 끊긴다). 그래서 아래 P1에 애니메이션 노드가 없다. 애니메이터 작업 시 3번 자리에 삽입한다.

## 사용하는 내장 노드

| 표시 이름 | 카테고리 | 비고 |
|---|---|---|
| `Run In Parallel` | Flow/Parallel Execution | **인스펙터 Mode 드롭다운**으로 동작이 바뀐다. `Default`(전부 완료) / `Until Any Complete`(하나라도 끝나면 종료) |
| `Repeat` | Flow | 인스펙터 Mode = `Forever` |
| `Try In Order` | Flow | Selector. 자식이 실패하면 다음 자식으로 |
| `Sequence` | Flow | |
| `Conditional Guard (Action)` | Action/Conditional | 조건 리스트를 담는 **리프 액션**. 조건 통과=성공, 실패=실패. `Requires All Conditions` 토글로 AND/OR |
| `Wait (Seconds)` | Action/Delay | |

`Conditional Guard (Modifier)`와 `Run In Parallel Until Any Completes`는 검색창에 뜨지 않는다(패키지가 숨김). 위 표의 것으로만 만든다.

## 그래프 구조

```text
[On Start]
└─ Run In Parallel                       [Mode: Default]
   │
   ├─ Repeat                             [Mode: Forever]        ── 패턴 선택 루프
   │  └─ Sequence
   │     ├─ Enemy Resolve Target                                (본진을 Target에 기록)
   │     └─ Try In Order
   │        │
   │        ├─ Sequence                                         ── P1 본진 돌진
   │        │  ├─ Conditional Guard (Action)   [Requires All ✔]
   │        │  │     · Enemy Pattern Gate
   │        │  │     · Enemy Distance To Target Below
   │        │  └─ Run In Parallel             [Mode: Until Any Complete]
   │        │     ├─ Enemy Show Telegraph Circle                (디버그: 빨강)
   │        │     └─ Sequence
   │        │        ├─ Enemy Mark Pattern Used
   │        │        ├─ Enemy Hold Position                     (예고 — 제자리 정지)
   │        │        ├─ Enemy Accelerate                        (경로 위 가속)
   │        │        └─ Enemy Impact Target                     (충돌 피해)
   │        │
   │        ├─ Sequence                                         ── P3 마력 봉인
   │        │  ├─ Conditional Guard (Action)   [Requires All ✔]
   │        │  │     · Enemy Pattern Gate
   │        │  │     · Enemy Units In Range   (Tower / Forward)
   │        │  │     · Enemy Units In Range   (Ally  / Forward)
   │        │  └─ Run In Parallel             [Mode: Until Any Complete]
   │        │     ├─ Enemy Show Telegraph Circle                (디버그: 보라)
   │        │     └─ Sequence
   │        │        ├─ Enemy Mark Pattern Used
   │        │        ├─ Enemy Show Telegraph Circle             (실제 예고: 노랑)
   │        │        └─ Enemy Apply Tower Debuff
   │        │
   │        ├─ Sequence                                         ── P2 방어 태세
   │        │  ├─ Conditional Guard (Action)
   │        │  │     · Enemy Units In Range   (Ally / Backward)
   │        │  └─ Run In Parallel             [Mode: Until Any Complete]
   │        │     ├─ Enemy Show Telegraph Circle                (디버그: 파랑)
   │        │     └─ Run In Parallel          [Mode: Default]
   │        │        ├─ Enemy Set Speed Factor                  (크롤)
   │        │        └─ Enemy Set Damage Taken Factor           (피해 감소)
   │        │
   │        └─ Run In Parallel               [Mode: Until Any Complete]   ── 기본 진군
   │           ├─ Enemy Show Telegraph Circle                   (디버그: 초록)
   │           └─ Sequence
   │              ├─ Enemy Set Speed Factor                     (배수 1 복귀)
   │              └─ Wait (Seconds)
   │
   └─ Repeat                             [Mode: Forever]        ── P4 지속 소환
      └─ Sequence
         ├─ Wait (Seconds)
         └─ Enemy Spawn Minions
```

### 구조상 반드시 지켜야 하는 것

- **`Conditional Guard`는 `Run In Parallel` 밖에 둔다.** 안에 넣으면 조건이 실패할 때마다 디버그 서클이 1프레임 깜빡인다(매 틱 반복 → 서클이 번쩍인다).
- **기본 진군 브랜치는 필수다.** `Enemy Accelerate`가 도달 성공 시 속도 배수를 원복하지 않으므로(뒤이은 충돌 피해가 실효 속도를 읽어야 한다), 배수 1 복귀는 이 브랜치가 담당한다. 없으면 돌진 배수가 고착된다.
- **기본 진군은 조건 없이 항상 성공해야 한다.** `Try In Order`의 마지막 자식이므로, 실패하면 Selector 전체가 실패한다.
- **모든 노드의 `Agent` 입력은 Blackboard의 `Self`에 연결한다.** `Self`는 그래프 기본 제공 `GameObject` 변수이며, 패키지가 `GameObject` → `Component` 자동 캐스팅을 해준다.
- **`Target`은 `Enemy Resolve Target`이 쓰고 P1이 읽는다.** 세 노드(`Distance To Target Below` / `Accelerate` / `Impact Target`)가 같은 `Target` 변수를 가리켜야 한다.

## Blackboard 변수

`Self`는 기본 제공이므로 새로 만들지 않는다. 아래를 추가한다.

### 공통

| 변수 | 타입 | 값 | 용도 |
|---|---|---|---|
| `Target` | GameObject | (비움) | `Enemy Resolve Target` 출력 |

### P1 본진 돌진

| 변수 | 타입 | 값 | 비고 |
|---|---|---|---|
| `P1_Key` | String | `dash` | 게이트 ↔ 기록 노드가 같은 값을 써야 한다 |
| `P1_Gate` | Float | `-1` | **음수 = 1회 한정.** 0이면 무제한이 되어 경고가 뜬다 |
| `P1_TriggerDistance` | Float | `30` | 본진까지 이 거리 미만이면 발동 |
| `P1_HoldDuration` | Float | `1.5` | 예고(제자리 정지) 길이 |
| `P1_MaxFactor` | Float | `3` | 속도 배수 상한 → 실효 36 |
| `P1_AccelPerSecond` | Float | `2` | 1→3까지 약 1초 |
| `P1_ArriveDistance` | Float | `5` | **`AttackRange`(3)보다 크게.** 아래 「경로 끝 파괴」 참조 |
| `P1_DamagePerSpeedUnit` | Float | `1.5` | 실효 36 × 1.5 = 54 피해 |
| `P1_MinSpeed` | Float | `15` | 기본 속도 12보다 높게 — 감속되면 피해 0 |
| `P1_MaxDuration` | Float | `15` | 돌진 상한. **0이면 영구 Running 위험** |

### P2 방어 태세

| 변수 | 타입 | 값 | 비고 |
|---|---|---|---|
| `P2_BackRadius` | Float | `15` | 뒤쪽 판정 반경 |
| `P2_MinAllies` | Int | `3` | 이 수 이상이면 발동 |
| `P2_SpeedFactor` | Float | `0.15` | 크롤. 하한 클램프(0.15) 때문에 완전 정지는 안 된다 |
| `P2_DamageTakenFactor` | Float | `0.4` | 받는 피해 40% |
| `P2_Duration` | Float | `1.5` | **짧게 유지.** 길면 조건이 풀린 뒤에도 크롤이 이어진다(비선점 Selector) |

### P3 마력 봉인

| 변수 | 타입 | 값 | 비고 |
|---|---|---|---|
| `P3_Key` | String | `seal` | |
| `P3_Cooldown` | Float | `15` | **0이면 무제한 발동 + 경고** |
| `P3_ForwardRadius` | Float | `18` | 앞쪽 판정 반경 |
| `P3_MinTowers` | Int | `2` | 공격 타워만 집계(오라 타워 제외) |
| `P3_MinAllies` | Int | `2` | |
| `P3_TelegraphDuration` | Float | `0.5` | **짧게.** 예고 원이 보스를 따라 움직여 길면 범위가 어긋난다 |
| `P3_SealRadius` | Float | `18` | 예고 원과 봉인 반경을 같은 값으로 |
| `P3_DamageMul` | Float | `0.5` | 1 미만이면 디버프 |
| `P3_AttackSpeedMul` | Float | `0.5` | 완전 봉인은 불가(0.01 클램프) |
| `P3_SealDuration` | Float | `6` | **0이면 해제 불가 영구 디버프 → 노드가 실패로 막는다** |
| `P3_TelegraphFill` | Color | 노랑 α≈0.15 | |
| `P3_TelegraphLine` | Color | 노랑 α≈0.9 | |

### P4 지속 소환

| 변수 | 타입 | 값 | 비고 |
|---|---|---|---|
| `P4_Interval` | Float | `6` | 소환 간격 |
| `P4_Prefab` | GameObject | `Blue_Grummy.prefab` | `Assets/Imported/@NorthLand/Prefabs/Monster/` |
| `P4_Count` | Int | `2` | 1회 투입 수 |
| `P4_MaxAlive` | Int | `15` | **0이면 상한 없음 + 경고.** 보스 자신과 사망 연출 중인 몬스터도 집계에 포함된다 |

### 기본 진군

| 변수 | 타입 | 값 |
|---|---|---|
| `March_SpeedFactor` | Float | `1` |
| `March_Wait` | Float | `0.5` |

### 디버그 서클 (Step 1 전용 — 애니메이터 붙이면 제거)

| 변수 | 타입 | 값 |
|---|---|---|
| `Dbg_Radius` | Float | `8` |
| `Dbg_Duration` | Float | `999` |

**디버그 서클의 색은 Blackboard로 올리지 않고 노드 입력에 직접 넣는다.** 임시 스캐폴딩이라 Blackboard 변수를 10개 늘릴 값이 아니다. `Until Any Complete` 부모가 형제(패턴 본체)의 완료와 함께 서클 노드를 중단시키고, 서클의 `OnEnd`가 원을 파괴한다 — `Duration`은 패턴보다 길기만 하면 된다.

| 패턴 | Fill (α≈0.15) | Outline (α≈0.9) |
|---|---|---|
| P1 돌진 | 빨강 | 빨강 |
| P3 봉인 | 보라 | 보라 |
| P2 방어 | 파랑 | 파랑 |
| 기본 진군 | 초록 | 초록 |

P3는 서클이 **두 개** 겹친다(디버그 보라 + 실제 예고 노랑). 색을 확실히 다르게 둔 이유다.

## 노드별 입력 배선

`Agent` = `Self` 는 전부 공통이므로 생략한다.

| 노드 | 입력 |
|---|---|
| `Enemy Resolve Target` | `TargetKind` = `PlayerBase` · `SearchRadius` = `0` · `Target` → `Target` |
| P1 `Enemy Pattern Gate` | `Key` = `P1_Key` · `CooldownSeconds` = `P1_Gate` |
| P1 `Enemy Distance To Target Below` | `Target` = `Target` · `Distance` = `P1_TriggerDistance` |
| P1 `Enemy Mark Pattern Used` | `Key` = `P1_Key` |
| P1 `Enemy Hold Position` | `Duration` = `P1_HoldDuration` |
| P1 `Enemy Accelerate` | `Target` = `Target` · `MaxFactor` = `P1_MaxFactor` · `AccelPerSecond` = `P1_AccelPerSecond` · `ArriveDistance` = `P1_ArriveDistance` · `MaxDuration` = `P1_MaxDuration` |
| P1 `Enemy Impact Target` | `Target` = `Target` · `DamagePerSpeedUnit` = `P1_DamagePerSpeedUnit` · `MinSpeed` = `P1_MinSpeed` |
| P3 `Enemy Pattern Gate` | `Key` = `P3_Key` · `CooldownSeconds` = `P3_Cooldown` |
| P3 `Enemy Units In Range` (타워) | `Filter` = `Tower` · `Direction` = `Forward` · `Radius` = `P3_ForwardRadius` · `MinCount` = `P3_MinTowers` |
| P3 `Enemy Units In Range` (잡몹) | `Filter` = `Ally` · `Direction` = `Forward` · `Radius` = `P3_ForwardRadius` · `MinCount` = `P3_MinAllies` |
| P3 `Enemy Mark Pattern Used` | `Key` = `P3_Key` |
| P3 `Enemy Show Telegraph Circle` (예고) | `Radius` = `P3_SealRadius` · `Duration` = `P3_TelegraphDuration` · `FillColor` = `P3_TelegraphFill` · `OutlineColor` = `P3_TelegraphLine` |
| P3 `Enemy Apply Tower Debuff` | `Radius` = `P3_SealRadius` · `DamageMultiplier` = `P3_DamageMul` · `AttackSpeedMultiplier` = `P3_AttackSpeedMul` · `Duration` = `P3_SealDuration` |
| P2 `Enemy Units In Range` | `Filter` = `Ally` · `Direction` = `Backward` · `Radius` = `P2_BackRadius` · `MinCount` = `P2_MinAllies` |
| P2 `Enemy Set Speed Factor` | `Factor` = `P2_SpeedFactor` · `Duration` = `P2_Duration` |
| P2 `Enemy Set Damage Taken Factor` | `Factor` = `P2_DamageTakenFactor` · `Duration` = `P2_Duration` |
| 진군 `Enemy Set Speed Factor` | `Factor` = `March_SpeedFactor` · `Duration` = `0` (원복하지 않음 — 의도) |
| 진군 `Wait (Seconds)` | `Duration` = `March_Wait` |
| P4 `Wait (Seconds)` | `Duration` = `P4_Interval` |
| P4 `Enemy Spawn Minions` | `Prefab` = `P4_Prefab` · `Count` = `P4_Count` · `MaxAlive` = `P4_MaxAlive` |
| 디버그 서클 4개 | `Radius` = `Dbg_Radius` · `Duration` = `Dbg_Duration` · 색은 인라인 |

## 배선 후 할 일

1. 그래프 에셋을 저장한다.
2. `tank.asset`의 `Boss > BehaviorTree` 칸에 **그래프 에셋을 펼쳐서 안에 있는 `BehaviorGraph` 서브에셋**을 드래그한다(에셋 자체가 아니라 서브에셋이다).
3. 알려주면 내가 Play 검증을 돌린다.

## 검증 계획

웨이브 SO를 건드리지 않는다. 밤이 시작되면 경로가 설정되므로, 그 뒤 `MonsterSpawn.SpawnMonster(Tank.prefab)`로 보스만 따로 투입한다 — 웨이브 스폰과 같은 경로라 `monsterParent` 자식 + 경로 + 스포너 주입이 모두 성립한다.

| 확인 항목 | 기대 |
|---|---|
| 기본 진군 | 초록 서클, 경로를 따라 이동 |
| P2 발동/해제 | 뒤쪽 잡몹 3체 이상에서 파랑으로 전환, 정리하면 초록 복귀 |
| P3 발동 | 앞쪽에 타워 2개 + 잡몹 2체에서 보라 + 노랑 예고, 타워 공격 간격이 늘어남 |
| P1 발동 | 본진 30 이내에서 빨강, 정지 후 가속, 충돌 피해 |
| **P1 후 보스 생존** | 충돌 후 사라지지 않고 근접 공격으로 전환 (아래 참조) |
| P4 | 6초마다 잡몹 2체 유입, 보스 사망 시 정지 |
| 감속 파훼 | `exec`로 `AddSpeedDebuff`를 걸고 충돌 피해가 줄어드는지 |

### 경로 끝 파괴 — 반드시 볼 것

`MonsterMove.RouteCompleted` → `Enemy.HandleRouteCompleted` → **`Destroy(gameObject)`**. 평소에는 `Enemy.Update`가 `AttackRange`에서 멈춰 경로 끝에 닿지 않는데, **P1 돌진 중에는 이동 소유권이 바로 그 정지를 막는다.** `P1_ArriveDistance`가 작으면 보스가 마지막 웨이포인트를 지나쳐 충돌 피해도 없이 사라진다. `AttackRange`(3)보다 크게(`5`) 잡은 이유다.

### 감속 타워 파훼는 대체 검증한다

이동속도 감소 타워가 저장소에 아직 없다(`slow_tower.asset` 전 필드 공백). `unity-cli exec`로 `IMovementAgent.AddSpeedDebuff`를 직접 걸어 실효 속도와 충돌 피해가 함께 줄어드는지 확인한다. 계약 검증으로는 충분하며, 타워 기반 검증은 타워 구현 후로 미룬다.
