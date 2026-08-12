# Tank(임시명) BT 그래프 배선 스펙

- 관련 이슈: #235
- 그래프 에셋: `Assets/Behavior/TankBossBehavior.asset`
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
| `Conditional Guard` | — | 조건 리스트를 담고 자식을 게이팅한다. 조건 실패 시 Failure를 반환해 상위 `Try In Order`가 다음 브랜치로 넘어간다. `Requires All Conditions` 토글로 AND/OR |
| `Wait (Seconds)` | Action/Delay | |

`Conditional Guard`에는 두 변종이 있다. 검색창에 뜨는 것은 `Action/Conditional`의 **Action 변종**(리프, 자식 없음)이고, 실제 그래프는 자식을 감싸는 **Modifier 변종**(`ConditionalGuardModifier`)을 쓴다 — 검색에는 안 뜨지만 조건을 노드 위로 끌어다 놓는 식으로 만들어진다. 둘 다 "조건 통과 시 통과, 실패 시 Failure"라 Selector 브랜치 게이팅으로는 동등하다. Modifier 쪽이 자식을 직접 감싸 구조가 한 단계 얕다.

`Run In Parallel Until Any Completes`도 검색창에 뜨지 않는다 — `Run In Parallel`을 놓고 인스펙터 Mode를 `Until Any Complete`로 바꾼다.

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
   │        │  ├─ Conditional Guard            [Requires All ✔]
   │        │  │     · Enemy Pattern Gate      (P1_Gate -1 = 1회 한정)
   │        │  │     · Enemy Distance To Target Below
   │        │  └─ Sequence
   │        │     ├─ Enemy Mark Pattern Used
   │        │     ├─ Enemy Play Animation                       ChargeWindup · Layer 0
   │        │     ├─ Enemy Hold Position                        (예고 — 제자리 정지)
   │        │     ├─ Enemy Accelerate                           (경로 위 가속 → 모션은 블렌드 트리가 자동)
   │        │     └─ Enemy Impact Target                        (충돌 피해)
   │        │
   │        ├─ Sequence                                         ── P4a 게이트 개방 (1회 · 정지)
   │        │  ├─ Conditional Guard            [Requires All ✔]
   │        │  │     · Enemy Field Cleared     (래치 — 한 번 참이면 계속 참)
   │        │  │     · Enemy Pattern Gate      (P4_GateOnce -1 = 1회 한정)
   │        │  └─ Sequence
   │        │     ├─ Enemy Mark Pattern Used
   │        │     └─ Run In Parallel           [Mode: Default]
   │        │        ├─ Enemy Play Animation                    Summon · Layer 1 · WaitForEnd
   │        │        └─ Enemy Hold Position                     (제자리 정지 — VFX 여유분)
   │        │
   │        ├─ Sequence                                         ── P3 마력 봉인
   │        │  ├─ Conditional Guard            [Requires All ✔]
   │        │  │     · Enemy Pattern Gate      (P3_Cooldown)
   │        │  │     · Enemy Units In Range   (Tower / Forward / P3_Radius)
   │        │  │     · Enemy Units In Range   (Ally  / Forward / P3_Radius)
   │        │  └─ Sequence
   │        │     ├─ Enemy Log                                  ("타워 봉인 시작")
   │        │     ├─ Enemy Mark Pattern Used
   │        │     ├─ Enemy Play Animation                       TowerSeal · Layer 1
   │        │     ├─ Enemy Show Telegraph Circle                (예고: 노랑)
   │        │     └─ Enemy Apply Tower Debuff                   (봉인 목록 → P3_SealedTowers + 콘솔)
   │        │
   │        ├─ Sequence                                         ── P2 방어 태세
   │        │  ├─ Conditional Guard            [Requires All ✔]
   │        │  │     · Enemy Units In Range   (Ally  / Backward / P2_Radius)
   │        │  │     · Enemy Units In Range   (Tower / Forward  / P2_Radius)
   │        │  │     · Enemy Pattern Gate      (P2_Cooldown — 없으면 영구 유지)
   │        │  └─ Run In Parallel             [Mode: Default]
   │        │     ├─ Enemy Mark Pattern Used                    (쿨다운 기록)
   │        │     ├─ Enemy Set Speed Factor                     (크롤)
   │        │     ├─ Enemy Set Animator Bool                    IsGuarding = true · Duration = P2_Duration
   │        │     └─ Enemy Set Damage Taken Factor              (피해 감소)
   │        │
   │        └─ Sequence                                         ── 기본 진군
   │           ├─ Enemy Set Animator Bool                       IsGuarding = false (보험 — 멱등)
   │           ├─ Enemy Set Speed Factor                        (배수 1 복귀)
   │           └─ Wait (Seconds)
   │
   │
   └─ Sequence                                                  ── P4b 잡몹 유입 (무한 · 정지 없음)
      ├─ Enemy Wait Until Field Cleared        (필드에 보스만 남을 때까지 대기)
      └─ Repeat                          [Mode: Forever]
         └─ Sequence
            ├─ Wait (Seconds)
            └─ Enemy Spawn Minions
```

**P4는 두 조각이다.** 멈추는 조각(P4a 게이트 개방)만 패턴 Selector로 올리고, 멈추지 않는 조각(P4b 유입)은 병렬 브랜치에 남긴다 — 정지는 이동 소유권으로만 표현할 수 있고 소유권은 단일 플래그라, 병렬 브랜치에서 잡으면 진행 중인 P1 돌진이 제어를 빼앗긴다(`BossDesign.md` P4 절).

- **P4a**는 `EnemyFieldClearedCondition`(래치 조건) + `EnemyPatternGateCondition`(쿨다운 `-1` = 1회 한정) 조합으로 판당 한 번만 통과한다. Selector 브랜치에는 Running을 유지하는 대기 노드를 넣을 수 없다 — 그 브랜치가 트리를 붙잡아 다른 패턴이 전부 멎는다.
- **P4b**는 반대로 Running 유지형이 맞다. 대기 노드 뒤에 `Repeat (Forever)`를 두면 시퀀스가 대기 노드로 되돌아오지 않아 구조가 래치를 보장한다.

**Selector 우선순위는 P1 → P4a → P3 → P2 → 기본 진군이다.** P4a는 1회뿐이라 다른 패턴을 굶길 일이 없고, 낮게 두면 P2·P3가 반복 발동하는 동안 개방 연출이 계속 밀린다. P3가 P2보다 위인 이유는 아래 「P2와 P3의 트리거 중첩」 참조.

### P2와 P3의 트리거 중첩

**두 패턴 모두 「앞쪽 타워 수」를 조건으로 삼는다**(둘 다 `MinTowers` 2). 실측 튜닝 중 추가된 조건이며, 의도는 "타워 밀집 구간에 진입할 때 방어 태세를 갖춘다"다.

겹치는 구간에서는 **Selector 순서가 승자를 정한다** — P3가 위에 있으므로 P3 쿨다운이 차 있으면 봉인이 먼저 나가고, 쿨다운 중이면 P2가 뽑힌다. 두 패턴이 번갈아 도는 형태가 되며, 이는 Selector의 비선점 성질상 자동으로 상호 배타다.

반경이 다른 점에 유의한다 — `P3_Radius` 50 > `P2_Radius` 30이라 **P3가 먼저 발동하는 거리에서 P2는 아직 조건을 만족하지 않는다.** 보스가 접근하며 봉인 → 근접 후 방어 태세 순으로 이어진다.

## ⚠ 감속 파훼 불변식 — 수치를 튜닝할 때 반드시 지킬 것

#232는 "모든 패턴에 파훼법이 있다"를 설계 축으로 못박았다. P1의 파훼 수단은 이동속도 감소 타워이고, 그것이 성립하려면 **감속 n중첩에서 실효 속도가 `P1_MinSpeed` 아래로 떨어져야** 한다.

```text
Boss.Stat.MoveSpeed × P1_MaxFactor × slowFactor^n  <  P1_MinSpeed
```

**감속은 소스별 곱산으로 합성된다**(`MoveSpeedComposer`, #233) — 감속 타워 n개면 `slowFactor^n`이다.
같은 종류 타워도 각각 별개 소스로 잡힌다(#164 리팩토링에서 소스키를 인스턴스별로 채번하도록 수정).

현재 값 — `Boss.Stat.MoveSpeed 4.8` × `MaxFactor 7` = **33.6**, **감속 타워 `choco_tower` = MoveSpeed −20%(배율 0.8)**, `MinSpeed 10`, 피해 계수 `3.75`:

| 감속 중첩 | 실효 속도 | vs `MinSpeed 10` | 충돌 피해 (`×3.75`) |
|---|---|---|---|
| 0 | 33.6 | 초과 | 126.0 (성문 HP 1000의 **12.6%**) |
| 1 | 26.88 | 초과 | 100.8 |
| 2 | 21.50 | 초과 | 80.6 |
| 3 | 17.20 | 초과 | 64.5 |
| 4 | 13.76 | 초과 | 51.6 |
| 5 | 11.01 | 초과 | 41.3 |
| 6 | 8.81 | **미달** | **0 — 완전 파훼** |

> **`TileSize` 스케일 조정에서 누락됐던 두 값을 복원했다(2026-08-11).**
>
> `Boss.Stat.MoveSpeed`가 12 → 4.8(×0.4)로 내려갈 때 `P1_MinSpeed`(25)와 `P1_DamagePerSpeedUnit`(1.5)이
> 함께 조정되지 않아, 파훼 문턱이 6중첩 → **2중첩**으로 떨어지고(감속 타워 하나로 돌진이 사실상 무력화)
> 충돌 피해도 성문 HP의 12.6% → 5.0%로 줄어 있었다.
>
> | 변수 | 이전 | 현재 | 근거 |
> |---|---|---|---|
> | `P1_MinSpeed` | 25 | **10** | ×0.4 — 속도 단위라 `MoveSpeed`와 같은 비율 |
> | `P1_DamagePerSpeedUnit` | 1.5 | **3.75** | ×2.5 — **속도 입력이 ×0.4로 줄었으므로 계수를 곱해 상쇄** |
>
> 계수를 "배율류라 스케일 대상이 아니다"로 분류했던 것이 누락의 원인이다. `DamagePerSpeedUnit`은
> **속도당 피해**라 속도 단위가 바뀌면 같이 움직여야 한다. 두 값 복원으로 파훼 문턱(6중첩)과
> 충돌 피해(성문 HP의 12.6%)가 스케일 변경 이전과 정확히 같아졌다.
>
> ⚠ **한 블랙보드 값이 에셋 안에 네 벌로 직렬화되며 자동 동기화되지 않는다.**
>
> | 사본 | 오브젝트 |
> |---|---|
> | 컴파일된 그래프의 노드 | `BehaviorGraph`(런타임 실행 대상) |
> | 노드 FieldModel의 `LinkedVariable` 대상 | `BehaviorAuthoringGraph`(**에디터에서 다시 빌드할 때의 소스**) |
> | 런타임 블랙보드 | `RuntimeBlackboardAsset` |
> | 저작 블랙보드 | `BehaviorBlackboardAuthoringAsset` |
>
> **블랙보드만 고치면 게임 동작이 바뀌지 않는다** — 컴파일된 노드가 빌드 시점 복사본을 들고 있고
> 런타임 바인딩이 블랙보드를 다시 읽지 않는다(`BehaviorGraphAgent.Init` 후 실측). 반대로 컴파일된
> 노드만 고치면 에디터에서 그래프를 열어 저장하는 순간 `BehaviorAuthoringGraph` 사본에서 되읽어
> **되돌아간다.**
>
> 실제로 `P1_MinSpeed`는 WL-122에서 15 → 25로 올릴 때 네 벌 중 두 벌만 갱신돼 **오래 어긋나 있었다**
> (2026-08-11 전수 조사로 발견, 네 벌 모두 정렬 완료). **값 변경은 Behavior 에디터에서 하고,
> 스크립트로 건드렸다면 네 벌을 전수 확인할 것.**

**`MaxFactor`를 올리거나 `MinSpeed`를 내리거나 감속 수치를 약하게 하면 이 부등식이 깨진다.**
실제로 `MaxFactor`를 3→7로 올렸을 때 2중첩 파훼가 무력화됐고(WL-122) `MinSpeed`를 15→25로 올려 복원했다.
**파훼 가능성은 밸런스가 아니라 설계 불변식이다** — 밸런싱으로 수치를 만질 때 이 표를 다시 계산할 것.
`Boss.Stat.MoveSpeed`는 그래프 밖(`Assets/Resources/ScriptableObjects/Enemies/tank.asset`)에 있어서
그래프만 보고 튜닝하면 이 표가 조용히 어긋난다.

> ⚠ **미결**: 감속을 −40%→−20%로 낮추면서 파훼에 필요한 타워 수가 **3개 → 6개**로 늘었다.
> 6개는 프로토타입 밸런싱 기준으로 과할 수 있다. 조정 후보 3가지 —
> ① 감속 수치를 −30%(배율 0.7) 이상으로 강화
> ② `P1_MinSpeed`를 위 계산보다 높게 잡아 파훼 문턱을 낮춤
> ③ 감속 합성을 곱산에서 **합산**으로 전환(타워 버프와 같은 방식 → `1 − 0.2n`).
> 단 ③은 `MoveSpeedComposer`가 보스 패턴 배수와 공유하는 인프라라 파급이 크고, 5중첩에서 배율 0(완전정지)에
> 도달해 스턴과 구분이 사라진다. **①·② 중 선택을 권한다.**

### 구조상 반드시 지켜야 하는 것

- **`Conditional Guard`는 `Run In Parallel` 밖에 둔다.** 조건 실패가 매 틱 반복되므로 안에 넣으면 자식 노드가 1프레임씩 깜빡 실행된다(디버그 서클 시절 서클이 번쩍이는 증상으로 드러났다).
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
| `P1_TriggerDistance` | Float | `100` | 본진까지 이 거리 미만이면 발동 |
| `P1_HoldDuration` | Float | `1.5` | 예고(제자리 정지) 길이 |
| `P1_MaxFactor` | Float | `7` | 속도 배수 상한 → 실효 **33.6**(`MoveSpeed 4.8` 기준). **파훼 불변식 참조** |
| `P1_AccelPerSecond` | Float | `3` | 1→7까지 2초 |
| `P1_ArriveDistance` | Float | `5` | **`AttackRange`(2.4)보다 크게.** 아래 「경로 끝 파괴」 참조 — 이 값으로 보스 생존 확인됨 |
| `P1_DamagePerSpeedUnit` | Float | `3.75` | 실효 33.6 × 3.75 = **126** 피해 (성문 HP 1000의 12.6%). **속도당 피해라 `MoveSpeed` 스케일과 반비례로 움직인다** |
| `P1_MinSpeed` | Float | `10` | 이 속도 미만이면 피해 0. **파훼 불변식이 이 값에 걸린다** — 감속 6중첩 |
| `P1_MaxDuration` | Float | `15` | 돌진 상한. **0이면 영구 Running 위험** |

### P2 방어 태세

| 변수 | 타입 | 값 | 비고 |
|---|---|---|---|
| `P2_Key` | String | `guard` | 쿨다운 식별자. `Enemy Mark Pattern Used`와 짝 |
| `P2_Cooldown` | Float | `6` | **재발동 금지 구간. 없으면 방어 태세가 영구히 유지된다** — 조건이 참인 동안 Selector가 브랜치를 끝나는 즉시 다시 뽑는다. `0`이면 제한 없음(경고 1회) |
| `P2_Radius` | Float | `30` | 판정 반경. **뒤쪽 아군 질의와 앞쪽 타워 질의가 이 값을 공유한다** |
| `P2_MinAllies` | Int | `2` | 뒤쪽(진행 방향 반대) 아군 잡몹 수 |
| `P2_MinTowers` | Int | `2` | 앞쪽 공격 타워 수. 실측 튜닝에서 추가된 조건 — "타워 밀집 구간에 진입할 때 방어 태세를 갖춘다" |
| `P2_SpeedFactor` | Float | `0.24` | 크롤. 하한 클램프(0.15) 때문에 완전 정지는 안 된다 |
| `P2_DamageTakenFactor` | Float | `0.4` | 받는 피해 40% |
| `P2_Duration` | Float | `3` | 유지 시간. **가드 Bool의 `Duration`과 같은 값으로 둘 것** — 어긋나면 자세와 효과의 구간이 다르다 |

방어 태세 가동률 = `P2_Duration / P2_Cooldown`. 현재 값은 3/6 = **50%**다. 쿨다운은 **마지막 발동 시점**부터 재므로 `P2_Cooldown`이 `P2_Duration`보다 커야 의미가 있다.

### P3 마력 봉인

| 변수 | 타입 | 값 | 비고 |
|---|---|---|---|
| `P3_Key` | String | `seal` | |
| `P3_Cooldown` | Float | `10` | **0이면 무제한 발동 + 경고** |
| `P3_Radius` | Float | `50` | 앞쪽 판정 반경. **타워 질의와 아군 질의가 이 값을 공유한다.** `P2_Radius`(30)보다 커서 P3가 더 먼 거리에서 먼저 발동한다 |
| `P3_MinTowers` | Int | `2` | 공격 타워만 집계(오라 타워 제외) |
| `P3_MinAllies` | Int | `1` | 앞쪽 아군 잡몹 수 |
| `P3_TelegraphDuration` | Float | `0.5` | **짧게.** 예고 원이 보스를 따라 움직여 길면 범위가 어긋난다 |
| `P3_SealRadius` | Float | `36` | 예고 원과 봉인 반경을 같은 값으로. **트리거 반경(`P3_Radius` 50)보다 작다** — 조건을 만족시킨 타워 일부가 봉인 범위 밖일 수 있다 |
| `P3_DamageMul` | Float | `0.5` | 1 미만이면 디버프 |
| `P3_AttackSpeedMul` | Float | `0.5` | 완전 봉인은 불가(0.01 클램프) |
| `P3_SealDuration` | Float | `4` | **0이면 해제 불가 영구 디버프 → 노드가 실패로 막는다** |
| `P3_TelegraphFill` | Color | 노랑 α≈0.15 | |
| `P3_TelegraphLine` | Color | 노랑 α≈0.9 | |
| `P3_SealedTowers` | GameObject List | (런타임 기록) | **출력.** 실제로 봉인된 타워가 기록된다. 봉인 VFX 노드가 생기면 이 변수를 입력으로 받는다 — 반경·카테고리 필터를 통과한 집합은 이 노드만 알고 있다 |

### P4 지속 소환

**P4a 게이트 개방 (1회 · 정지)**

| 변수 | 타입 | 값 | 비고 |
|---|---|---|---|
| `P4_GateAliveCount` | Int | `1` | 게이트 통과 임계값. **집계에 보스 자신이 포함되므로 "보스만 남았다"는 0이 아니라 1이다.** 씬에 직접 배치한 테스트 보스는 `monsterParent` 자식이 아니라 `0`. **0 이하 + 스폰된 보스면 게이트가 영구히 닫힌다(경고 1회)** |
| `P4_Key` | String | `summonGate` | 개방 래치 식별자. `Enemy Mark Pattern Used`와 짝 |
| `P4_GateOnce` | Float | **`-1`** | 1회 한정. **0이면 개방 연출이 매 사이클 반복되고 보스가 계속 멈춘다** |
| `P4_SummonHold` | Float | `3` | 개방 연출 중 정지 시간. **VFX 여유분** — 소환 모션이 약 2.5초라 그보다 길게 |

**P4b 잡몹 유입 (무한 · 정지 없음)**

| 변수 | 타입 | 값 | 비고 |
|---|---|---|---|
| `P4_Interval` | Float | `1` | 유입 간격. **소환 모션(약 2.5초)보다 짧아 개방 연출이 끝나기 전에 첫 잡몹이 나오는데, 이것이 의도다** — 보스가 손을 들어올리는 동안 게이트에서 잡몹이 쏟아지기 시작한다. 두 조각이 독립적으로 게이트를 판정하기 때문에 가능한 연출이다 |
| `P4_Prefab` | GameObject | `Yellow_Grummy.prefab` | `Assets/Imported/@NorthLand/Prefabs/Monster/` |
| `P4_Count` | Int | `1` | 1회 투입 수 |
| `P4_MaxAlive` | Int | `30` | **0이면 상한 없음 + 경고.** 보스 자신과 사망 연출 중인 몬스터도 집계에 포함된다 |

`P4_GateAliveCount`는 두 조각이 **같은 값을 공유한다** — 어긋나면 한쪽만 열린다.

폐기된 변수: `P4_SummonTrigger` / `P4_SummonLayer` / `P4_SummonMaxWait`(애니메이터 값은 노드 입력에 인라인) · `Anim_*`(같은 이유). `Dbg_Radius` / `Dbg_Duration`은 남아 있으나 참조하는 노드가 없다.

### 기본 진군

| 변수 | 타입 | 값 |
|---|---|---|
| `March_SpeedFactor` | Float | `1` |
| `March_Wait` | Float | `0.5` |

### 애니메이션 (공용)

파라미터 이름을 Blackboard로 올린다 — 컨트롤러를 갈아끼울 때 그래프를 열어 노드마다 찾아다니지 않기 위해서다.

애니메이터 파라미터 이름과 레이어 번호는 **노드 입력에 인라인**으로 넣는다(현재 그래프 기준). 값은 아래와 같다.

| 자리 | 값 |
|---|---|
| 상체 마스크 레이어 인덱스 | `1` — **0이면 루프 중인 걷기를 읽어 재생 대기가 즉시 끝난다** |
| P1 준비 모션 Trigger | `ChargeWindup` |
| P3 봉인 Trigger | `TowerSeal` |
| P4 소환 Trigger | `Summon` |
| P2 가드 Bool | `IsGuarding` |

⚠ **지속 상태를 Trigger 쌍으로 만들지 말 것.** Unity Trigger는 소비되지 않으면 켜진 채로 남아, 해제 트리거가 장전됐다가 다음 진입을 즉시 취소한다. 상세는 `BossDesign.md` 「1회성은 Trigger, 지속 상태는 Bool」.

⚠ **이동 모션은 그래프가 관여하지 않는다.** `MoveSpeed` / `MoveCadence`는 `BossLocomotionBlend`가 실효 이동속도에서 계산해 흘린다. 걷기·달리기·전력질주 선택은 `Move` 상태의 블렌드 트리 임계값(Animator 창)에서 조정한다 — 돌진용 Bool 플래그는 없다.

### 패턴 발동 로그 (디버그)

`Enemy Log`는 **P3 한 곳**에만 남아 있다(`Message` = `타워 봉인 시작`, 인라인 문자열).

```
[보스 패턴] 42.18s · 타워 봉인 시작
```

뒤이은 `Enemy Apply Tower Debuff`가 **봉인된 타워 목록까지 따로 남긴다**(0건도 남긴다 — 분산 배치 파훼가 통한 것인지 필터가 잘못된 것인지 구분해야 하므로).

나머지 패턴에는 로그가 없다. 모션·파티클로 발동이 눈에 보이게 된 뒤 걷어냈다 — 가드는 자세가, 돌진·개방은 정지와 이펙트가 곧 표시다. 필요하면 같은 방식으로 다시 넣는다.

기본 진군 브랜치에는 넣지 않는다 — `March_Wait`(0.5초)마다 돌아 콘솔이 잠긴다.

### 디버그 서클 — 제거됨

패턴마다 있던 디버그 서클 4개(빨강/보라/파랑/초록)와 그것을 감싸던 `Run In Parallel [Until Any Complete]` 4개를 **모두 걷어냈다.** 모션과 파티클이 붙어 발동이 눈에 보이게 되면서 역할이 끝났고, 보스를 정본 웨이브에 편성하기 전에 지워야 하는 스캐폴딩이었다(노드 60개 → 51개).

**남아 있는 서클은 P3의 실제 예고 원 하나뿐이다** — 이건 디버그가 아니라 플레이어에게 봉인 범위를 알리는 게임 요소다.

`Dbg_Radius`(8) / `Dbg_Duration`(999) 변수는 블랙보드에 남아 있으나 **더 이상 아무 노드도 참조하지 않는다.** 서클을 다시 붙일 일이 없으면 지워도 된다.

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
| P3 `Enemy Units In Range` (타워) | `Filter` = `Tower` · `Direction` = `Forward` · `Radius` = `P3_Radius` · `MinCount` = `P3_MinTowers` |
| P3 `Enemy Units In Range` (잡몹) | `Filter` = `Ally` · `Direction` = `Forward` · `Radius` = `P3_Radius` · `MinCount` = `P3_MinAllies` |
| P3 `Enemy Mark Pattern Used` | `Key` = `P3_Key` |
| P3 `Enemy Show Telegraph Circle` (예고) | `Radius` = `P3_SealRadius` · `Duration` = `P3_TelegraphDuration` · `FillColor` = `P3_TelegraphFill` · `OutlineColor` = `P3_TelegraphLine` |
| P3 `Enemy Apply Tower Debuff` | `Radius` = `P3_SealRadius` · `DamageMultiplier` = `P3_DamageMul` · `AttackSpeedMultiplier` = `P3_AttackSpeedMul` · `Duration` = `P3_SealDuration` · `SealedTowers` → `P3_SealedTowers` |
| P2 `Enemy Pattern Gate` | `Key` = `P2_Key` · `CooldownSeconds` = `P2_Cooldown` |
| P2 `Enemy Units In Range` (잡몹) | `Filter` = `Ally` · `Direction` = `Backward` · `Radius` = `P2_Radius` · `MinCount` = `P2_MinAllies` |
| P2 `Enemy Units In Range` (타워) | `Filter` = `Tower` · `Direction` = `Forward` · `Radius` = `P2_Radius` · `MinCount` = `P2_MinTowers` |
| P2 `Enemy Mark Pattern Used` | `Key` = `P2_Key` |
| P2 `Enemy Set Speed Factor` | `Factor` = `P2_SpeedFactor` · `Duration` = `P2_Duration` |
| P2 `Enemy Set Damage Taken Factor` | `Factor` = `P2_DamageTakenFactor` · `Duration` = `P2_Duration` |
| P2 `Enemy Log` | `Message` = `가드 올림` (인라인) |
| P3 `Enemy Log` | `Message` = `타워 봉인 시작` (인라인) |
| P1 `Enemy Play Animation` (준비) | `Trigger` = `ChargeWindup` · `Layer` = `0` · `WaitForEnd` = `false` · `MaxWaitSeconds` = `3` |
| P3 `Enemy Play Animation` (봉인) | `Trigger` = `TowerSeal` · `Layer` = `1` · `WaitForEnd` = `false` · `MaxWaitSeconds` = `3` |
| P2 `Enemy Set Animator Bool` | `Parameter` = `IsGuarding` · `Value` = `true` · **`Duration` = `P2_Duration`** |
| 진군 `Enemy Set Animator Bool` | `Parameter` = `IsGuarding` · `Value` = `false` · `Duration` = `0` (보험) |
| 진군 `Enemy Set Speed Factor` | `Factor` = `March_SpeedFactor` · `Duration` = `0` (원복하지 않음 — 의도) |
| 진군 `Wait (Seconds)` | `Duration` = `March_Wait` |
| P4a `Enemy Field Cleared` | `MaxAliveCount` = `P4_GateAliveCount` |
| P4a `Enemy Pattern Gate` | `Key` = `P4_Key` · `CooldownSeconds` = `P4_GateOnce` (**`-1`**) |
| P4a `Enemy Mark Pattern Used` | `Key` = `P4_Key` |
| P4a `Enemy Hold Position` | `Duration` = `P4_SummonHold` |
| P4a `Enemy Play Animation` (개방) | `Trigger` = `Summon` · `Layer` = `1` · `WaitForEnd` = `true` · `MaxWaitSeconds` = `5` |
| P4b `Enemy Wait Until Field Cleared` | `MaxAliveCount` = `P4_GateAliveCount` |
| P4b `Wait (Seconds)` | `Duration` = `P4_Interval` |
| P4b `Enemy Spawn Minions` | `Prefab` = `P4_Prefab` · `Count` = `P4_Count` · `MaxAlive` = `P4_MaxAlive` |


⚠ **가드가 풀리려면 두 장치가 모두 필요하다.**
① P2 `Enemy Set Animator Bool`의 `Duration`(= `P2_Duration`) — `OnEnd`가 정상 종료와 중단 모두를 지나므로 무슨 일이 있어도 자세가 내려간다.
② `P2_Cooldown` — 조건이 계속 참인 동안 Selector가 브랜치를 끝나는 즉시 다시 뽑기 때문에, 쿨다운이 없으면 ①의 원복 직후 같은 프레임에 다시 켜져 **끊김 없는 무한 가드**가 된다. 실제로 ①만 있는 상태에서 재현됐다.

## 검증 결과

### 2차 (보스 모델 + AnimatorController + 게이트형 P4)

| 항목 | 결과 |
|---|---|
| 기본 이동 = 걷기 | ✅ 블렌드 트리(`MoveSpeed`) — 돌진 구간에서만 전력질주로 넘어간다 |
| P1 준비 모션 → 돌진 | ✅ `ChargeWindup` 후 정지, 가속하며 모션이 자동 전환 |
| P2 방어 태세 | ✅ 쿨다운 도입 후 가드가 정상 해제된다. 로그 `가드 올림`이 `P2_Cooldown`(6초) 간격으로 찍힌다 |
| P3 마력 봉인 | ✅ 로그에 봉인된 타워 목록이 남는다(0건도 남는다) |
| P4a 게이트 개방 | ✅ 필드가 비면 1회만 정지 + 소환 모션 |
| P4b 잡몹 유입 | ✅ 개방 후 `P4_Interval` 간격으로 무한 유입 |
| 공격 모션 ↔ 공격 속도 | ✅ `AttackCadence`로 1회 재생 = `AttackInterval`(2.5초) 실측 일치 |
| 상체/하체 분리 | ✅ 걸으면서 가드·봉인·소환. 상체 레이어 weight 자동 페이드 |
| **P1 충돌 후 보스 생존** | ✅ `P1_ArriveDistance` 5에서 경로 끝 `RouteCompleted → Destroy`를 회피하고 근접 공격으로 전환한다 |
| 프리팹 스케일·콜라이더 | ✅ 모델 ×6(키 약 11.7) · `CapsuleCollider` height 10 / center.y 5 · `HitPosition` y 8 · `MonsterHealthBar` y 13 |
| 패턴 VFX | ✅ 레이어별로 파티클이 모션과 함께 재생된다(`BossDesign.md` 「패턴 VFX 계약」) |
| 디버그 서클 제거 | ✅ 4개 모두 제거(노드 60 → 51). 남은 서클은 P3 예고 원 하나뿐이며 그건 게임 요소다 |
| 웨이브 편성 | ✅ 웨이브 7에 보스 1체 |
| **감속 파훼(인게임)** | ❌ **미검증** — 수치상으로는 6중첩 파훼가 복원됐다(「감속 파훼 불변식」) |

**수치는 실측 관측으로 조정한 값이며 밸런싱 전까지 이대로 간다.**

### 1차 (#235, 캡슐 몸체 · AnimatorController 없음)

| 항목 | 결과 |
|---|---|
| 기본 진군 | ✅ 초록 서클, 배수 1 |
| P2 방어 태세 | ✅ 뒤쪽 아군 3체 → 파랑 서클 + 배수 0.15 + 피해배수 0.4. 잡몹 제거 시 기본 진군 복귀 |
| P3 마력 봉인 | ✅ 앞쪽 타워 4 + 아군 3 → 보라(디버그)+노랑(예고) 서클 → 타워 `AttackInterval` 1 → 2 (배율 0.5). 진군 복귀 후에도 `P3_SealDuration`간 유지 — 만료를 노드가 아니라 `Tower`가 소유하는 설계대로다 |
| P1 본진 돌진 | ✅ 본진 근처에서 발동 |
| P4 지속 소환 | ✅ 간격마다 잡몹 유입 (게이트 도입 이전 구조) |
| 감속 파훼 | ✅ 아래 별도 표 (계약 검증) |

### 감속 파훼 (대체 검증)

최초 검증 시점에는 이동속도 감소 타워가 저장소에 없어(`slow_tower.asset` 전 필드 공백) `IMovementAgent.AddSpeedDebuff`를 직접 걸어 계약만 확인했다.

| 상태 | 실효 속도 | 충돌 피해(계수 1.5) |
|---|---|---|
| 기본 | 12 | 18 |
| 돌진 배수 7 | 84 | 126 |
| + 감속 0.5 | 42 | 63 |
| + 감속 0.5 하나 더 | 21 | **0** (`P1_MinSpeed` 25 미달) |

패턴 배수는 유지된 채 실효 속도만 줄어든다 — **두 축이 서로를 지우지 않는다.**

> **갱신(2026-07-29, #164 리팩토링)**: 위 표는 감속 배율 0.5를 가정한 계약 검증이다. 실제 감속 타워
> `choco_tower`가 구현되면서 수치가 확정됐으므로(**MoveSpeed −20% = 배율 0.8**) 파훼에 필요한 중첩 수는
> 「감속 파훼 불변식」 절의 표(6중첩)를 따른다.
>
> 또한 그 시점까지 **감속 중첩이 아예 동작하지 않았다**: `AuraTower`가 상태 효과 키를 `TowerID` 해시로
> 채번해 같은 종류 감속 타워 여러 개가 대상의 `StatusEffectHandler`에서 한 슬롯을 공유했고, 결과적으로
> 배율이 1중첩에 고정돼 **P1 파훼가 원천 불가**였다. 소스키를 인스턴스별로 바꿔 해소했으며
> `MoveSpeedComposer` 실측으로 곱산 중첩을 확인했다. 여전히 **인게임(플레이 모드) 타워 기반 검증은 미완**이다.

### P2/P3를 인위적으로 만들 때의 함정 3개

조건이 "주변에 뭐가 있느냐"라서 상황을 만들어야 하는데, 그 과정에서 다음에 걸린다.

1. **보스가 경로 216 유닛을 18초에 주파한다.** `exec` 몇 번 하는 사이 본진에 도착한다. `MonsterMove.enabled = false`로 얼려도 **BT는 계속 돈다** — P2/P3는 이동이 아니라 주변 상황이 조건이라 정지 상태로도 유효하다.
2. **`monsterParent`를 비우면 안 된다.** `childCount == 0`이 웨이브 클리어 트리거라(`MonsterSpawn.cs:230`) 보상 흐름이 돌고 **보스까지 정리된다.** 파괴하지 말고 멀리 격리한다.
3. **P4 소환체가 성문을 파괴하면 GameOver**가 되고, `Enemy.Update`가 `behaviorAgent.enabled = false`로 BT를 꺼서 배수·서클이 그 상태로 얼어붙는다. 검증 중에는 테스트 대상 외 몬스터를 계속 동결해야 한다.

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
