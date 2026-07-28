# 보스 몬스터 설계

- 관련 이슈: 미생성 (이 문서를 근거로 신규 생성 예정)
- 구현 위치: `Assets/Scripts/CombatSystem/Enemy/AI/Nodes/` (예정)
- 노드 레퍼런스: `Docs/Monster/Boss/BossNodeReference.md`
- 이 문서는 **구현 전 합의된 설계**를 기록한 것이다. 현재 코드는 한 줄도 작성되지 않았다. 구현이 들어가면 실제 코드에 맞춰 이 문서를 갱신하고, 미확정 항목은 [미확정 / TODO](#미확정--todo)에 모아둔다.

> `Assets/Scripts/CombatSystem/Enemy/Boss/`의 중간보스 노드 4종(`BossHealSelfAction` / `BossHpBelowCondition` / `BossRampSpeedMultiplierAction` / `BossSetSpeedMultiplierAction`)과 `MidBossBehavior.asset`은 이 보스와 무관하다. **재사용하지 않고 참조하지도 않는다.** 이 보스의 리프 노드는 전부 신규 작성한다.

## 개요

프로토타입의 최종 보스. Unity Behavior(`com.unity.behavior` 1.0.16) 그래프로 행동을 구성하며, 그래프는 `EnemyAsset.Boss.BehaviorTree`에 지정해 `Enemy.Awake`가 `BehaviorGraphAgent`에 주입한다(`Assets/Scripts/CombatSystem/Enemy/Enemy.cs:79-91`).

일반 몬스터와 달리 보스는 정해진 웨이포인트 경로를 그대로 따라가되, 경로 위에서 **속도와 방어력을 스스로 조절**하고 **주변 타워를 약화**시키며 **자신이 살아있는 동안 잡몹 유입을 유지**한다. 경로를 이탈하거나 임의 지점으로 순간이동하지 않는다.

## 설계 목표

- **경로 위에서만 논다.** 보스는 `MonsterMove`의 웨이포인트 경로를 벗어나지 않는다. 전투맵은 경로 주변 반경 5칸만 타일이 존재하는 리본 형태라, 직선 돌진은 발밑이 빈 허공을 가로지른다.
- **모든 패턴에 파훼법이 있다.** 파훼 수단은 타워 종류 선택, 타워 배치 위치, 잡몹 처리 우선순위 중 하나 이상이어야 한다. "딜을 더 넣는다"는 파훼법으로 치지 않는다.
- **리프 노드는 보스에 종속되지 않는다.** 노드는 베이스 컴포넌트 `EnemyAgent`를 Blackboard로 주입받아 동작하고, 보스별 차이는 코드가 아니라 **BT 그래프와 Blackboard 값**으로 흡수한다. 2호 보스는 C# 없이 그래프 조립만으로 나올 수 있어야 한다. 상세 규약은 노드 레퍼런스 문서를 따른다.
- **노드는 기존 클래스에 직접 닿지 않는다.** `EnemyAgent`가 `Enemy` / `MonsterMove` / `Animator`에 대한 파사드 역할을 하고, 노드는 그 경계 너머를 알지 못한다. 기존 전투 코드의 변경이 노드로 번지지 않게 하기 위함이다.

## 보스의 목적

웨이브 내내 유지되던 "잡몹을 처리한다"는 단일 과제에, **처리 순서의 딜레마**를 얹는 것이 이 보스의 역할이다.

보스의 두 패턴(방어 태세 / 마력 봉인)은 모두 **주변 잡몹 수**를 발동 조건으로 삼고, 잡몹은 보스가 살아있는 동안 계속 유입된다. 따라서 플레이어는 다음 중 하나를 골라야 한다.

- **잡몹부터 정리한다** — 보스의 두 패턴을 봉인하지만, 보스 본체는 그동안 본진에 가까워진다.
- **보스부터 집중한다** — 유입이 멈추지만, 그 사이 보스는 방어 태세로 피해를 줄이고 타워를 봉인한다.

이 선택 때문에 광역·연쇄 타워(다수 처리)와 단일 고화력 타워(보스 처리)의 배분이 의미를 갖고, 이동속도 감소 타워는 돌진 패턴의 전용 대응 수단이 된다.

## 행동 패턴

패턴에 쓰이는 노드 이름은 전부 `Docs/Monster/Boss/BossNodeReference.md`에 정의된 것이다.

### P1. 본진 돌진

본진 일정 거리 안에 들어오면 준비 동작 후 경로를 따라 가속해 본진에 충돌한다. 충돌 피해는 **충돌 시점의 실효 이동속도에 비례**한다.

- **발동 조건**: 본진까지 거리 < 임계값. 보스당 1회만 발동한다(래치).
- **진행**:
  1. 제자리 정지 + 준비 모션 재생 — 예고 구간
  2. 경로를 따라 가속. 패턴 속도 배수를 상한까지 올린다
  3. 본진 충돌 지점 도달 → 피해 = 계수 × 실효 이동속도
  4. 패턴 종료. 이동 소유권을 놓으면 `Enemy.Update`가 이어받아 정지 후 근접 공격으로 전환된다
- **파훼법** (3중):
  - 돌진 구간(본진 인근 경로)에 **이동속도 감소 타워**를 배치한다. 실효 속도가 낮아지면 충돌 피해가 그대로 줄어든다
  - 준비 모션 동안 보스가 정지하므로 감전·버프 스킬을 꽂기 좋다
  - 충돌 직후 보스가 본진 앞에 고정되므로 화력 집중 구간이 생긴다
- **사용 노드**: `EnemyResolveTargetAction` · `EnemyPatternGateCondition` · `EnemyMarkPatternUsedAction` · `EnemyDistanceToTargetBelowCondition` · `EnemyPlayAnimationAction` · `EnemyHoldPositionAction` · `EnemyAccelerateAction` · `EnemyImpactTargetAction`

> 피해 계산의 입력은 `Enemy.SpeedMultiplier`가 아니라 **`MonsterMove`의 실효 속도**여야 한다. 배수를 읽으면 감속 디버프가 반영되지 않아 "슬로우로 파훼"가 성립하지 않는다.

### P2. 방어 태세

뒤쪽에 잡몹이 충분히 모이면 속도를 크게 낮추고 받는 피해를 줄인다. 정지가 아니라 매우 느린 이동이다.

- **발동 조건**: 보스 뒤쪽(진행 방향 반대) 반경 안에 아군 잡몹이 N체 이상
- **진행**: 패턴 속도 배수를 최저치로, 받는 피해 배수를 감소치로 설정한다. 짧은 지속시간으로 반복 갱신하며, 조건이 풀리면 다음 사이클에 기본 진군으로 복귀한다
- **파훼법**: 보스 뒤쪽 잡몹을 정리하면 조건이 풀린다. 보스는 다시 빨라지지만 방어력을 잃는다 — 속도와 방어력 중 무엇을 먼저 깰지가 플레이어의 판단이 된다
- **사용 노드**: `EnemyUnitsInRangeCondition` · `EnemySetSpeedFactorAction` · `EnemySetDamageTakenFactorAction`

> 속도 배수에는 **하한 클램프가 필수**다. 크롤 배수에 감속 디버프가 곱해지면 0에 수렴하는데, 현재 `MonsterMove.SetMoveSpeed`는 0 이하를 받으면 `fallbackMoveSpeed`(3)로 되돌려 오히려 빨라진다(`Assets/Scripts/Monster/MonsterMoveMent/MonsterMove.cs:41-50`).

### P3. 마력 봉인

앞쪽에 타워가 밀집해 있고 그 구간으로 잡몹이 진입하고 있으면, 범위 내 타워의 공격력과 공격속도를 일정 시간 떨어뜨린다.

- **발동 조건**: 보스 앞쪽(진행 방향) 반경 안에 타워 N개 이상 **그리고** 아군 잡몹 M체 이상. 쿨다운 있음
- **진행**: 예고 범위를 표시한 뒤, 범위 내 타워에 `Tower.ApplyBuff(sourceId, damageMul < 1, attackSpeedMul < 1, duration)`을 건다. 타워를 파괴하지는 않는다
- **파훼법**: 타워를 분산 배치하면 한 번에 걸리는 수가 줄어든다. 플레이어 버프 스킬은 같은 소스 합산 구조라 봉인을 부분 상쇄한다
- **사용 노드**: `EnemyUnitsInRangeCondition` · `EnemyPatternGateCondition` · `EnemyMarkPatternUsedAction` · `EnemyShowTelegraphCircleAction` · `EnemyApplyTowerDebuffAction`

이 패턴은 **신규 런타임 훅이 필요 없다.** `Tower.ApplyBuff`가 접근 제어 없이 열려 있고 소스별 합산 구조라 배율 1 미만을 넘기면 그대로 디버프가 된다(`Assets/Scripts/CombatSystem/Tower/Tower.cs:226-268`).

알려진 제약 두 가지:

- 공격 간격이 `Mathf.Max(finalSpeedMultiplier, 0.01f)`로 클램프되어 **완전 봉인은 불가능**하다. 최대 감속까지만 된다.
- `AuraTower`는 `Tower.Active`에 등록되지 않으므로 **봉인 대상에서 제외**된다. haste / poison 계열과, 마법 타워로 구현될 경우 이동속도 감소 타워도 포함이다. 결과적으로 봉인 중에도 감속은 살아남아 P1 파훼 수단이 유지된다.

### P4. 지속 소환

보스가 살아있는 동안 스폰 지점에서 잡몹이 계속 유입된다.

- **발동 조건**: 없음. 보스 생존 중 상시 (BT 병렬 브랜치)
- **진행**: 일정 간격마다 스폰 지점에 잡몹을 추가 투입한다. 동시 생존 수 상한을 둔다
- **정지**: 보스 사망 시 `Enemy.Die`가 `behaviorAgent.enabled = false`로 그래프 틱을 멈추므로 **자동으로 멈춘다.** 별도 배선이 필요 없다
- **파훼법**: 보스를 죽이는 것이 유입을 멈추는 유일한 방법이다
- **사용 노드**: `EnemySpawnMinionsAction`

> 소환체는 반드시 `MonsterSpawn`의 `monsterParent` 자식으로 넣어야 한다. 웨이브 클리어 판정이 `monsterParent.childCount == 0`이라(`Assets/Scripts/Monster/MonsterSpawn/MonsterSpawn.cs:230`), 밖에 두면 보스 사망 즉시 웨이브가 종료되면서 잡몹이 남는다. 안에 두면 "보스를 죽여야 물결이 멎는다"가 성립해 최종 웨이브 승리 조건과 맞물린다.

## 패턴 간 상호작용

소환된 잡몹이 스폰 지점에서 나오므로, 별도 배선 없이 패턴이 체인으로 이어진다.

```text
보스가 잡몹보다 앞서 진행
-> 뒤쪽에 잡몹 축적
-> P2 방어 태세 발동 (크롤 + 피해 감소)
-> 느려진 보스를 잡몹이 추월
-> 앞쪽 잡몹 증가, 타워 밀집 구간 진입
-> P3 마력 봉인 발동
-> 봉인 구간을 잡몹이 돌파
-> 보스는 본진 접근 -> P1 돌진
```

P1과 P2는 같은 패턴 속도 배수 축을 쓰므로 동시에 성립할 수 없다. BT의 Selector 우선순위로 P1을 위에 두어 상호 배타를 보장한다.

## BT 그래프 구조

```text
Root: Run In Parallel
├─ Repeat (Forever)
│   └─ Selector
│       ├─ Sequence  [게이트 통과? && 본진까지 거리 < D]  -> P1 본진 돌진
│       ├─ Sequence  [앞쪽 타워 >= N && 앞쪽 잡몹 >= M && 쿨다운] -> P3 마력 봉인
│       ├─ Sequence  [뒤쪽 잡몹 >= K]                     -> P2 방어 태세
│       └─ Sequence  기본 진군 (패턴 배수 1 복귀 + 짧은 대기)
└─ Repeat (Forever)
    └─ Sequence  [대기(간격) -> P4 잡몹 소환]
```

`Repeat` / `Selector` / `Run In Parallel` / `Wait` 는 Unity Behavior 내장 노드를 그대로 쓴다.

## 선행 작업 (신규 훅)

패턴 구현 전에 열어야 하는 지점이다. P3를 제외한 모든 패턴이 여기에 의존한다.

먼저 베이스 컴포넌트 `EnemyAgent`를 만든다. BT 리프 노드는 전부 `EnemyAgent`만 참조한다. 노드가 요구하는 능력의 전체 목록은 노드 레퍼런스 문서의 「EnemyAgent가 노출해야 하는 것」에 있다.

`EnemyAgent`는 `Enemy`를 상속하지 않고 **같은 오브젝트에 나란히 부착한다**(병존). `Enemy`는 다른 시스템 소유이고 노드를 잡몹에도 재사용해야 하므로, 상속으로 묶지 않고 접점을 아래 표의 항목으로 한정한다. 보스별 고유 능력이 필요하면 `EnemyAgent`를 상속한 파생 컴포넌트를 쓴다 — 노드는 `EnemyAgent` 타입으로 받으므로 파생 타입도 그대로 들어간다.

그 다음 `EnemyAgent`가 능력을 제공할 수 있도록 기존 클래스에 진입점을 연다.

| 훅 | 대상 | 용도 | 비고 |
|---|---|---|---|
| 이동속도 다축 합성 + 하한 클램프 | `MonsterMove` | P1 파훼, P2 | **이동속도 감소 타워 담당자와 공유 계약** — 아래 참조 |
| 실효 이동속도 노출 | `MonsterMove` | P1 충돌 피해 계산 | 배수가 아니라 최종 속도를 읽어야 한다 |
| BT 이동 소유권 플래그 | `Enemy` | P1 준비 동작 중 정지 | `Update`의 `movement.IsStopped = hasTarget` 매 프레임 덮어쓰기를 차단해야 한다 (`Enemy.cs:166-169`) |
| 받는 피해 배수 | `Enemy.TakeDamage` | P2 | 현재 감쇠·방어·무적 지점이 전혀 없다 (`Enemy.cs:187-197`) |
| 공개 스폰 API | `MonsterSpawn` | P4 | `SpawnPrefab` / `SpawnGroupAsync`가 private. `monsterParent` 자식 + 경로 부여 |
| 보스 전용 AnimatorController | 보스 프리팹 | P1 준비 모션 | 신규 제작 |

애니메이션은 `EnemyAgent`가 `Animator`를 직접 들면 되므로 `MonsterAnimation` 수정이 필요 없다. 현재 `MonsterAnimation`은 `IsMove` / `IsAttack` / `IsDie` Bool 3개만 노출하며 임의 클립을 재생할 수단이 없다.

### 이동속도 합성 계약

이동속도 감소 타워와 보스 돌진 가속은 **같은 속도 값을 놓고 경쟁해야** 파훼가 성립한다. 현재 `Enemy.SetSpeedMultiplier`는 소스 구분 없는 단일 필드 덮어쓰기라, 어느 한쪽이 다른 쪽을 지운다(`Enemy.cs:131-135`).

```text
최종 이동속도 = Stat.MoveSpeed
              × 패턴 배수        (BT 노드가 소유. 돌진 가속 / 방어 태세 크롤)
              × Π 디버프 배수    (소스별 곱산. 이동속도 감소 타워 등)
              (하한 클램프)
```

두 축이 곱해지면 "가속하는 보스를 감속 타워가 끌어내린다"가 별도 밸런싱 없이 성립한다. 이 구조를 누가 어느 PR에서 넣을지는 미확정이다.

## 스탯·수치 authoring 위치

이 문서는 수치를 담지 않는다. 실제 값은 아래 두 곳에서 관리한다.

| 구분 | 위치 |
|---|---|
| 보스 기본 스탯 (MaxHp / MoveSpeed / AttackDamage / AttackRange / AttackInterval) | `Assets/Resources/ScriptableObjects/Enemies/<EnemyID>.asset` 의 `Boss.Stat` — 인스펙터 직접 입력. CSV 경유 아님 |
| 패턴 수치 (거리 임계, 가속도, 배수, 지속시간, 잡몹 수 임계, 소환 간격) | BT 그래프 에셋의 **Blackboard 변수** |

패턴 수치는 노드 입력에 인라인으로 박지 않고 Blackboard 변수로 올린다. 노드 입력에 직접 넣으면 그래프를 열어야만 수치를 볼 수 있고 보스별 그래프 공유가 막힌다 (WL-094와 같은 축).

## 미확정 / TODO

- [ ] **보스 이름·컨셉 미정.** 몬스터 테마 자체가 GDD §8에서 미확정이다. 이름이 정해지면 이 문서를 보스 이름으로 개명한다 — 보스가 늘어나면 보스마다 설계 문서를 1본씩 둔다. 노드 대장(`BossNodeReference.md`)만 보스 공용이다.
- [ ] **웨이브 배치 미정.** 보스를 웨이브 10 또는 14에 넣을지, 기존 중간보스를 앞 웨이브로 당길지 결정되지 않았다. 현재 중간보스(`ogre_king`)가 최종 웨이브 7을 점유하고 있고, 최종 웨이브는 `WaveCompletionCoordinator`가 보상을 건너뛰고 `TriggerVictory()`를 호출하는 자리다 (WL-096). SUNJIN 조율 필요.
- [x] **HP 기반 에스컬레이션은 도입하지 않는다.** 4개 패턴을 전부 상황 트리거(본진까지 거리 / 주변 잡몹 수 / 타워 밀집)로 유지한다. 보스 HP 구간에 따라 압박이 강해지는 장치는 두지 않는다.
- [ ] **이동속도 합성 계약의 소유권.** 감속 타워 담당자와 공동 작업할지, 보스 쪽에서 골격을 먼저 넣을지.
- [ ] **이동속도 감소 타워가 `Tower` 계열인지 `AuraTower` 계열인지 확인 필요.** `AuraTower`면 P3 마력 봉인 대상에서 제외된다. 현재 `slow_tower.asset`은 Magic/Debuff 타입이나 전 필드가 비어 있어 판단할 수 없다.
- [ ] **패턴 수치 일체 미정.** 밸런싱은 구현 후 플레이 검증으로 잡는다.
- [ ] 보스 전용 HP UI / 등장 연출 도입 여부. 현재 프로젝트에 경고 배너 UI가 없고 카메라에 연출 API가 없다.
- [ ] 게임 배속(`Time.timeScale`)과 패턴 타이밍의 관계. BT의 `Wait`과 노드의 `Time.deltaTime`이 전부 스케일드 타임이라 2배속에서 패턴 쿨다운도 2배 빨라진다.

## 참고

- `Docs/Monster/Boss/BossNodeReference.md` — 리프 노드 정의와 작성 규약
- `Docs/Monster/MonsterMovement.md` — 경로·이동 시스템
- `Docs/Monster/MonsterAnimation.md` — 애니메이션 FSM
- `Docs/GDD.md` §3.4 승패 조건 / §4.2 밤 페이즈 / §8 미확정(스테이지·보스 구성)
- `Docs/Review/WatchList.md` — WL-094(보스 수치 authoring 위치), WL-096(중간보스의 최종 웨이브 점유), WL-038(destroyDelay와 웨이브 판정)
