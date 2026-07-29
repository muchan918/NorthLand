# 보스 몬스터 설계 — Tank(임시명)

> **이름은 임시다.** 몬스터 테마가 GDD §8에서 미확정이라 프로토타입 동안 `Tank`로 부른다. 파일명은 바꾸지 않았다 — 임시명으로 개명하면 정식 이름이 정해질 때 또 옮겨야 하고, 그 사이 문서 링크가 두 번 깨진다. 정식 이름이 확정되면 그때 보스 이름으로 개명한다.

- 관련 이슈: #232(상위) / #233(기반, 완료) / #234(리프 노드 세트, 완료) / #235(패턴 그래프·에셋, 진행 중)
- 구현 위치: 노드 `Assets/Scripts/CombatSystem/Enemy/AI/Nodes/` · 보조 타입 `Assets/Scripts/CombatSystem/Enemy/AI/`
- 그래프 `Assets/Behavior/TankBossBehavior.asset` · 프리팹 `Assets/Prefabs/Monster/Tank.prefab` · 스탯 `Assets/Resources/ScriptableObjects/Enemies/tank.asset`
- 노드 레퍼런스: `Docs/Monster/Boss/BossNodeReference.md` · 그래프 배선·검증: `Docs/Monster/Boss/TankGraphSpec.md`
- **패턴 4종이 Play에서 동작하는 것을 확인했다**(#235). 다만 두 가지가 남아 있다 — ① 보스 몸체가 **캡슐**이고 `AnimatorController`가 없어 P1 준비 모션이 그래프에서 빠져 있다 ② P1 충돌 후 보스 생존(경로 끝 파괴 회피)이 미검증이다. 수치는 전부 placeholder이며 밸런싱 전이다. 검증 상세는 `TankGraphSpec.md` 「검증 결과」.

> `Assets/Scripts/CombatSystem/Enemy/MiniBoss/`의 중간보스 노드 4종(`BossHealSelfAction` / `BossHpBelowCondition` / `BossRampSpeedMultiplierAction` / `BossSetSpeedMultiplierAction`)과 `MidBossBehavior.asset`은 이 보스와 무관하다. **재사용하지 않고 참조하지도 않는다.** 이 보스의 리프 노드는 전부 신규 작성한다.

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

> 피해 계산의 입력은 `Enemy.SpeedMultiplier`가 아니라 **`MonsterMove`의 실효 속도**여야 한다. 배수를 읽으면 감속 디버프가 반영되지 않아 "슬로우로 파훼"가 성립하지 않는다. → 구현에서는 `EnemyImpactTargetAction`이 `EnemyAgent.EffectiveMoveSpeed`를 읽는다.
>
> 이 때문에 `EnemyAccelerateAction`의 원복이 비대칭이다: 소유권은 항상 반납하지만 속도 배수는 **도달 실패로 끝난 경우에만** 되돌린다. 도달 성공 시 원복하면 바로 뒤의 충돌 피해가 평상시 속도를 읽는다. 성공 후 배수 1 복귀는 아래 그래프 구조의 기본 진군 브랜치가 담당한다 — **그래프에 기본 진군 브랜치가 없으면 돌진 배수가 고착된다.**
>
> P1은 이동 소유권 중에 근접 평타를 내지 않는다(`Enemy.MovementOwnedByBehavior`가 타겟 통지까지 막는다). 충돌 피해가 그 역할을 대신하고, 소유권을 반납하는 4단계에서 `Enemy.Update`가 근접 공격을 이어받는다.

### P2. 방어 태세

뒤쪽에 잡몹이 충분히 모이면 속도를 크게 낮추고 받는 피해를 줄인다. 정지가 아니라 매우 느린 이동이다.

- **발동 조건**: 보스 뒤쪽(진행 방향 반대) 반경 안에 아군 잡몹이 N체 이상
- **진행**: 패턴 속도 배수를 최저치로, 받는 피해 배수를 감소치로 설정한다. 짧은 지속시간으로 반복 갱신하며, 조건이 풀리면 다음 사이클에 기본 진군으로 복귀한다
- **파훼법**: 보스 뒤쪽 잡몹을 정리하면 조건이 풀린다. 보스는 다시 빨라지지만 방어력을 잃는다 — 속도와 방어력 중 무엇을 먼저 깰지가 플레이어의 판단이 된다
- **사용 노드**: `EnemyUnitsInRangeCondition` · `EnemySetSpeedFactorAction` · `EnemySetDamageTakenFactorAction`

> **해소(#233)**: 하한 클램프가 들어갔다. `MonsterMove.minMoveSpeed`(직렬화 필드, 기본 0.15)가 합성 결과의 하한이고, `SetMoveSpeed`는 이제 기준 속도만 받는다 — 크롤 배수가 `fallbackMoveSpeed`(3)로 되돌아 오히려 빨라지던 경로는 사라졌다. 대신 **완전 정지가 속도 축으로 불가능**해졌다(의도) — 정지는 `EnemyAgent.MovementStopped`(이동 소유권 축)로만 표현한다. 감속 타워로 몬스터를 영구 정지시켜 웨이브를 소프트락하는 경로도 함께 막혔다.

### P3. 마력 봉인

앞쪽에 타워가 밀집해 있고 그 구간으로 잡몹이 진입하고 있으면, 범위 내 타워의 공격력과 공격속도를 일정 시간 떨어뜨린다.

- **발동 조건**: 보스 앞쪽(진행 방향) 반경 안에 타워 N개 이상 **그리고** 아군 잡몹 M체 이상. 쿨다운 있음
- **진행**: 예고 범위를 표시한 뒤, 범위 내 타워에 `Tower.ApplyBuff(sourceId, damageMul < 1, attackSpeedMul < 1, duration)`을 건다. 타워를 파괴하지는 않는다
- **⚠ 이 패턴은 보스를 멈추지 않는다.** BT 노드가 Running이어도 이동은 `MonsterMove`가 계속 구동한다. 예고 원이 보스를 따라 움직여 예고 범위와 실제 봉인 범위가 어긋난다. **프로토타입은 이 드리프트를 수용하고 예고 시간을 짧게(0.5초 수준) 잡아 덮는다 — 정식 대응은 미확정(TBD)**이며, 플레이에서 체감되면 시전 중 정지 또는 예고 원 월드 고정으로 전환한다(`BossNodeReference.md` 「미확정 / TODO」에 선택지 표)
- **파훼법**: 타워를 분산 배치하면 한 번에 걸리는 수가 줄어든다. 플레이어 버프 스킬은 같은 소스 합산 구조라 봉인을 부분 상쇄한다
- **사용 노드**: `EnemyUnitsInRangeCondition` · `EnemyPatternGateCondition` · `EnemyMarkPatternUsedAction` · `EnemyShowTelegraphCircleAction` · `EnemyApplyTowerDebuffAction`

이 패턴은 **신규 런타임 훅이 필요 없다.** `Tower.ApplyBuff`가 접근 제어 없이 열려 있고 소스별 합산 구조라 배율 1 미만을 넘기면 그대로 디버프가 된다(`Assets/Scripts/CombatSystem/Tower/Tower.cs:226-268`).

알려진 제약 두 가지:

- 공격 간격이 `Mathf.Max(finalSpeedMultiplier, 0.01f)`로 클램프되어 **완전 봉인은 불가능**하다. 최대 감속까지만 된다.
- **오라·유틸 계열(Magic 타입, 공격 스탯 없음) 타워는 봉인 대상에서 제외된다.** haste / poison 계열과 이동속도 감소 타워가 여기 속한다. 결과적으로 봉인 중에도 감속은 살아남아 P1 파훼 수단이 유지된다.

  이 제외를 **`Tower.Active` 등록 여부가 아니라 카테고리로 판정한다**(`EnemyNodeQuery.IsAttackTower` = `Tower.AttackInterval > 0`). 현재 `AuraTower`는 `MonoBehaviour` 직접 파생이라 `Tower.Active`에 아예 없어 어느 방식이든 결과가 같지만, **`AuraTower : Tower` 리팩토링이 예정돼 있어** 등록 여부로 판정하면 그때 이동속도 감소 타워가 봉인 대상에 들어오고 위 설계 의도가 조용히 뒤집힌다. 카테고리 판정은 리팩토링 전후로 거동이 같다.

  같은 판정을 P3 **발동 조건의 타워 수 집계에도** 적용한다(`EnemyUnitFilter.Tower`). 트리거와 봉인 대상 집합이 어긋나면 "봉인해도 아무것도 안 걸리는 오라 타워 뭉치"에 P3가 발동한다.

### P4. 지속 소환

보스가 살아있는 동안 스폰 지점에서 잡몹이 계속 유입된다.

- **발동 조건**: 없음. 보스 생존 중 상시 (BT 병렬 브랜치)
- **진행**: 일정 간격마다 스폰 지점에 잡몹을 추가 투입한다. 동시 생존 수 상한을 둔다
- **정지**: 보스 사망 시 `Enemy.Die`가 `behaviorAgent.enabled = false`로 그래프 틱을 멈추므로 **자동으로 멈춘다.** 별도 배선이 필요 없다
- **파훼법**: 보스를 죽이는 것이 유입을 멈추는 유일한 방법이다
- **사용 노드**: `EnemySpawnMinionsAction`

> 소환체는 반드시 `MonsterSpawn`의 `monsterParent` 자식으로 넣어야 한다. 웨이브 클리어 판정이 `monsterParent.childCount == 0`이라, 밖에 두면 보스 사망 즉시 웨이브가 종료되면서 잡몹이 남는다. 안에 두면 "보스를 죽여야 물결이 멎는다"가 성립해 최종 웨이브 승리 조건과 맞물린다. → 구현에서는 `MonsterSpawn.SpawnMonster`가 웨이브 스폰과 같은 경로를 타므로 자동으로 충족된다.
>
> 동시 생존 상한(`MaxAlive`)의 집계는 `monsterParent.childCount`다. **보스 자신과 사망 연출 중인 몬스터(`destroyDelay` 2초)가 포함**되므로 실효 상한이 의도보다 빡빡해진다 — 수치를 정할 때 감안하거나 #235 Play 검증에서 보정한다(WL-038과 같은 축).

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

P1과 P2는 같은 패턴 속도 배수 축을 쓰므로 동시에 성립할 수 없다. **상호 배타를 보장하는 것은 우선순위가 아니라 Selector의 비선점 성질이다** — 한 브랜치에 진입하면 그 브랜치가 끝날 때까지 트리가 잔류하므로 어느 순간에도 한 패턴만 돈다. 우선순위는 "이전 브랜치가 끝나고 다시 평가하는 시점에 누가 뽑히는지"만 정한다.

그래서 각 패턴 브랜치의 길이가 반응성을 결정한다. 조건이 풀려도 진행 중인 브랜치는 끝까지 간다 — P2를 짧은 지속시간으로 반복 갱신하는 설계가 이 성질에 대한 대응이다. 반대로 **끝나지 않는 브랜치는 패턴 Selector 전체를 영구 봉인**하며, 이때 P4는 별도 병렬 브랜치라 계속 돌아 겉보기로는 정상 동작처럼 보인다. 자세한 내용과 선점이 필요할 때 쓰는 `Priority Abort`는 `BossNodeReference.md` 「실행 모델」 참조.

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

## 선행 작업 (신규 훅) — 완료(#233)

패턴 구현 전에 열어야 하는 지점이다. P3를 제외한 모든 패턴이 여기에 의존한다. **AnimatorController를 제외하고 전부 구현됐다.** 실제 시그니처와 상태는 `Docs/Monster/Boss/BossNodeReference.md` 「기존 시스템에 필요한 변경」 표를 정본으로 본다.

먼저 베이스 컴포넌트 `EnemyAgent`를 만든다. BT 리프 노드는 전부 `EnemyAgent`만 참조한다. 노드가 요구하는 능력의 전체 목록은 노드 레퍼런스 문서의 「EnemyAgent가 노출해야 하는 것」에 있다.

`EnemyAgent`는 `Enemy`를 상속하지 않고 **같은 오브젝트에 나란히 부착한다**(병존). `Enemy`는 다른 시스템 소유이고 노드를 잡몹에도 재사용해야 하므로, 상속으로 묶지 않고 접점을 아래 표의 항목으로 한정한다. 보스별 고유 능력이 필요하면 `EnemyAgent`를 상속한 파생 컴포넌트를 쓴다 — 노드는 `EnemyAgent` 타입으로 받으므로 파생 타입도 그대로 들어간다.

그 다음 `EnemyAgent`가 능력을 제공할 수 있도록 기존 클래스에 진입점을 연다.

| 훅 | 대상 | 용도 | 상태 |
|---|---|---|---|
| 이동속도 다축 합성 + 하한 클램프 | `MonsterMove` + `IMovementAgent` | P1 파훼, P2 | 완료. **이동속도 감소 타워 담당자와 공유 계약** — 아래 참조. 계약은 구체 타입이 아니라 `IMovementAgent`에 올렸다 |
| 실효 이동속도 노출 | `MonsterMove` | P1 충돌 피해 계산 | 완료 — `IMovementAgent.EffectiveMoveSpeed` |
| BT 이동 소유권 플래그 | `Enemy` | P1 준비 동작 중 정지, 돌진 중 전진 유지 | 완료 — `Enemy.MovementOwnedByBehavior`. `IsStopped`뿐 아니라 `SetHasTarget`까지 차단한다(설계에 없던 보강, P1 절 참조) |
| 받는 피해 배수 | `Enemy.TakeDamage` | P2 | 완료 — `Enemy.DamageTakenFactor` |
| 공개 스폰 API | `MonsterSpawn` | P4 | 완료 — `SpawnMonster` / `AliveMonsterCount` + 스폰 시점 스포너 주입(정적 싱글톤 미사용) |
| 보스 전용 AnimatorController | 보스 프리팹 | P1 준비 모션 | **미착수(#235)**. 노드 쪽 준비는 끝났다(`EnemyPlayAnimationAction`, `normalizedTime` 폴링) |

애니메이션은 `EnemyAgent`가 `Animator`를 직접 들면 되므로 `MonsterAnimation` 수정이 필요 없다. 현재 `MonsterAnimation`은 `IsMove` / `IsAttack` / `IsDie` Bool 3개만 노출하며 임의 클립을 재생할 수단이 없다.

### 이동속도 합성 계약

이동속도 감소 타워와 보스 돌진 가속은 **같은 속도 값을 놓고 경쟁해야** 파훼가 성립한다. 현재 `Enemy.SetSpeedMultiplier`는 소스 구분 없는 단일 필드 덮어쓰기라, 어느 한쪽이 다른 쪽을 지운다(`Enemy.cs:131-135`).

```text
최종 이동속도 = Stat.MoveSpeed
              × 패턴 배수        (BT 노드가 소유. 돌진 가속 / 방어 태세 크롤)
              × Π 디버프 배수    (소스별 곱산. 이동속도 감소 타워 등)
              (하한 클램프)
```

두 축이 곱해지면 "가속하는 보스를 감속 타워가 끌어내린다"가 별도 밸런싱 없이 성립한다.

**#233에서 보스 쪽이 골격을 넣었다.** 감속 타워는 `IMovementAgent`(구현체 `MonsterMove`)의 아래 창구를 쓰면 된다 — 구체 타입에 묶이지 않는다.

```csharp
movementAgent.AddSpeedDebuff(sourceId, 0.5f);   // 소스별 곱산 중첩, 같은 sourceId는 갱신만
movementAgent.RemoveSpeedDebuff(sourceId);      // 해제는 이 창구로만(시간 만료 없음)
```

`sourceId` 채번은 `Tower.ApplyBuff`와 같은 관례를 따른다(인스턴스별이면 `GetInstanceID`, 종류별이면 고정 문자열/TowerID 해시). 자동 만료가 없으므로 **타워가 해제 책임을 진다** — 밤 종료·철거·비활성화 시 `RemoveSpeedDebuff`를 부르지 않으면 감속이 고착된다.

Play 검증으로 두 축이 서로를 지우지 않는 것을 확인했다: 기준 10 × 패턴 3 × 감속 0.5 × 0.5 = 7.5이며, 패턴 축을 매 프레임 다시 써도 감속이 살아남는다.

## 스탯·수치 authoring 위치

이 문서는 수치를 담지 않는다. 실제 값은 아래 두 곳에서 관리한다.

| 구분 | 위치 |
|---|---|
| 보스 기본 스탯 (MaxHp / MoveSpeed / AttackDamage / AttackRange / AttackInterval) | `Assets/Resources/ScriptableObjects/Enemies/<EnemyID>.asset` 의 `Boss.Stat` — 인스펙터 직접 입력. CSV 경유 아님 |
| 패턴 수치 (거리 임계, 가속도, 배수, 지속시간, 잡몹 수 임계, 소환 간격) | BT 그래프 에셋의 **Blackboard 변수** |
| 이동속도 하한 | `MonsterMove.minMoveSpeed` (프리팹 인스펙터, 기본 0.15) — 몬스터 공통이라 보스 그래프가 아니다 |
| 반경 질의 레이어 마스크 | `EnemyAgent.unitLayerMask` (프리팹 인스펙터) — `LayerMask`가 Blackboard 지원 타입이 아니다 |

패턴 수치는 노드 입력에 인라인으로 박지 않고 Blackboard 변수로 올린다. 노드 입력에 직접 넣으면 그래프를 열어야만 수치를 볼 수 있고 보스별 그래프 공유가 막힌다 (WL-094와 같은 축).

`float` / `int` / `bool` / `string` / `Color` / `GameObject`(프리팹 에셋 포함) / `[BlackboardEnum]` enum은 Blackboard 변수로 올릴 수 있다. `LayerMask`는 안 된다 — 위 표의 예외가 그 이유다.

## 미확정 / TODO

- [ ] **보스 이름·컨셉 — 프로토타입은 임시명 `Tank`로 간다.** 몬스터 테마 자체가 GDD §8에서 미확정이다. 정식 이름이 정해지면 이 문서를 보스 이름으로 개명한다 — 보스가 늘어나면 보스마다 설계 문서를 1본씩 둔다. 노드 대장(`BossNodeReference.md`)만 보스 공용이다.
- [x] **웨이브 배치는 프로토타입에서 조정하지 않는다.** 패턴 검증이 목적이라 임의 웨이브에 `Count: 1`로 넣고 동작만 본다. 웨이브 10/14 편성이나 중간보스 이동은 프로토타입 이후 과제이며, 최종 웨이브 점유 문제(WL-096, `WaveCompletionCoordinator`가 최종 웨이브에서 보상을 건너뛰고 `TriggerVictory()` 호출)도 이 이슈에서 다루지 않는다. SUNJIN 조율 불필요.
- [x] **HP 기반 에스컬레이션은 도입하지 않는다.** 4개 패턴을 전부 상황 트리거(본진까지 거리 / 주변 잡몹 수 / 타워 밀집)로 유지한다. 보스 HP 구간에 따라 압박이 강해지는 장치는 두지 않는다.
- [x] **이동속도 합성 계약의 소유권 — 보스 쪽에서 골격을 먼저 넣었다**(#233). 감속 타워는 `IMovementAgent.AddSpeedDebuff` / `RemoveSpeedDebuff`를 얹으면 된다. 저장소에 감속 타워 코드가 아직 없어 충돌 대상이 없었다(`slow_tower.asset`은 전 필드가 비어 있음). **해제 책임은 타워 쪽** — 자동 만료가 없다.
- [x] **이동속도 감소 타워는 `AuraTower` 계열이다** — P3 마력 봉인 대상에서 제외된다(설계 의도대로). 단 **`AuraTower`를 `Tower` 상속으로 바꾸는 대규모 리팩토링이 예정**돼 있어 상속 구조는 확정이 아니다. 그래서 P3의 대상 판정을 `Tower.Active` 등록 여부가 아니라 공격 스탯 보유 여부로 두어 **리팩토링 결과에 불변**으로 만들었다(P3 절 참조). 리팩토링 담당자가 확인할 것: `AuraTower`가 `Tower`를 상속해도 데이터가 Magic 타입(공격 스탯 없음)으로 남는지 — 남지 않으면 봉인 대상에 들어와 P1 파훼 수단이 사라진다.
- [ ] **패턴 수치 일체 미정.** 밸런싱은 구현 후 플레이 검증으로 잡는다. 단 P3 예고 `Duration`은 밸런싱과 별개로 **짧게(0.5초 수준) 잡아야 한다** — 예고 원 드리프트를 덮는 프로토타입 대응이 이 값에 의존한다(P3 절).
- [x] **패턴 런타임 동작 검증됨(#235).** P1~P4 + 기본 진군이 그래프로 동작한다. 상세는 `TankGraphSpec.md` 「검증 결과」.
- [ ] **AnimatorController 미착수.** 보스 몸체가 캡슐이고 `Animator`가 없어 P1 준비 모션(`EnemyPlayAnimationAction`)이 그래프에서 빠져 있다 — 노드가 `Animator` 없으면 Failure를 반환해 P1 시퀀스가 끊기기 때문이다. #235 완료 기준 중 이 항목은 미충족이다. 모델·클립 선정은 별건으로 진행한다. 팩(`Assets/Imported/KSJ/Monsters Ultimate Pack 01`)이 **전부 Generic rig**라 외부 휴머노이드 리타게팅은 불가하고 같은 팩 클립만 쓸 수 있다.
- [ ] **P1 충돌 후 보스 생존 미검증.** 돌진 중에는 이동 소유권이 `Enemy.Update`의 정지를 막으므로, `P1_ArriveDistance`가 작으면 보스가 경로 끝 웨이포인트를 지나쳐 `RouteCompleted → Destroy`로 충돌 피해도 없이 사라진다. 현재 5(`AttackRange` 3보다 크게)로 잡았으나 실제 통과 여부는 확인하지 못했다.
- [ ] **패턴 임계값이 절대 월드 거리라 맵 시드에 노출된다.** 전투맵은 `TileSize 15`에 변 70타일(=1050유닛)이고 경로 웨이포인트를 8~12개 새로 뽑는다. `P1_TriggerDistance`(100)는 본진까지의 **직선 거리**라, 경로가 감기는 시드에서는 스폰 직후에도 100 아래일 수 있어 보스가 출발점에서 바로 돌진을 시작한다 — `P1_Gate = -1`(1회 한정)이라 잘못 발동하면 그 판의 P1은 끝이다. `P2_BackRadius`(40) / `P3_ForwardRadius`(30)도 같은 이유로 "항상 발동" ↔ "한 번도 발동 안 함" 사이를 시드가 결정한다. 트리거를 **남은 경로 진행도**로 잡는 편이 이 맵 생성기와 궁합이 맞다. 최소한 서로 다른 시드 3개에서 P1 발동 지점을 눈으로 확인한 뒤 값을 확정할 것 — 프로토타입 밸런싱의 첫 항목.
- [ ] **보스 전용 HP UI 미도입.** 잡몹과 같은 `MonsterHealthBar`를 자식으로 붙여 최소한 체력이 보이게는 해뒀다(`Boss.Stat.MaxHp` 800이라 진행도 판단 수단이 없으면 밸런싱 피드백 자체가 안 돈다). 전용 UI·등장 연출 도입 여부는 여전히 미정이며, 오면 이 임시 체력바를 떼면 된다.
- [ ] **그래프에 기본 진군 브랜치가 반드시 있어야 한다.** 없으면 P1 돌진 성공 후 속도 배수가 고착된다(`EnemyAccelerateAction`이 도달 성공 시 원복하지 않는 이유는 P1 절 참조).
- [ ] **`EnemyAgent.unitLayerMask`가 비면 P2·P3가 조용히 발동하지 않는다.** 반경 질의가 항상 0을 반환한다. #235 프리팹 작성 시 확인.
- [ ] 보스 전용 HP UI / 등장 연출 도입 여부. 현재 프로젝트에 경고 배너 UI가 없고 카메라에 연출 API가 없다.
- [ ] 게임 배속(`Time.timeScale`)과 패턴 타이밍의 관계. BT의 `Wait`과 노드의 `Time.deltaTime`이 전부 스케일드 타임이라 2배속에서 패턴 쿨다운도 2배 빨라진다.

## 참고

- `Docs/Monster/Boss/BossNodeReference.md` — 리프 노드 정의와 작성 규약
- `Docs/Monster/MonsterMovement.md` — 경로·이동 시스템
- `Docs/Monster/MonsterAnimation.md` — 애니메이션 FSM
- `Docs/GDD.md` §3.4 승패 조건 / §4.2 밤 페이즈 / §8 미확정(스테이지·보스 구성)
- `Docs/Review/WatchList.md` — WL-094(보스 수치 authoring 위치), WL-096(중간보스의 최종 웨이브 점유), WL-038(destroyDelay와 웨이브 판정)
