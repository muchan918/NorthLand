# 보스 BT 노드 레퍼런스

- 관련 이슈: #234 (리프 노드 세트), #233 (`EnemyAgent` + 기존 시스템 진입점)
- 구현 위치: 노드 `Assets/Scripts/CombatSystem/Enemy/AI/Nodes/` · 보조 타입 `Assets/Scripts/CombatSystem/Enemy/AI/`
- 설계 문서: `Docs/Monster/Boss/BossDesign.md`
- 이 문서는 Unity Behavior 커스텀 리프 노드의 **정의 대장**이다. 노드는 보스가 늘어날수록 재사용되며 계속 추가된다. 노드를 새로 만들거나 파라미터를 바꾼 사람은 같은 PR에서 이 표의 행을 갱신한다.
- Condition 3종 / Action 11종 / 보조 타입 4종 **구현 완료**(#234). 그래프 조립과 Play 검증은 #235에서 한다 — 현재 이 노드들을 쓰는 그래프 에셋이 없어 **런타임 동작은 미검증**이다. 검증된 것은 컴파일·GUID 유일성·에디터 노드 목록 등재·입력 타입 유효성이다.

> `Assets/Scripts/CombatSystem/Enemy/MiniBoss/`의 기존 노드 4종은 이 대장에 포함되지 않는다. 중간보스 전용이며 재사용하지도, 참조하지도 않는다. GUID 충돌은 없다(프로젝트 전체 18개 노드 전부 고유).

## 설계 원칙

리프 노드는 특정 보스에 종속되지 않는다. 보스별 차이는 코드가 아니라 **BT 그래프 구조와 Blackboard 값**으로 흡수한다. 새 보스를 추가할 때 C# 작성 없이 그래프 조립만으로 끝나는 것이 목표다.

- **이름에 보스를 넣지 않는다.** 노드의 대상은 임의의 `EnemyAgent`이며 잡몹에 붙여도 동작해야 한다. 접두사는 `Enemy`를 쓴다.
- **수치를 하드코딩하지 않는다.** 거리·배수·지속시간·개수는 전부 Blackboard 변수로 받는다.
- **대상을 노드가 찾아다니지 않는다.** 노드는 `GetComponent` 계열로 대상을 탐색하지 않는다. 자기 자신은 `Agent` 입력으로, 공격 대상은 `Target` 입력으로 **주입받는다**. 탐색으로 잡으면 의존성이 코드에 숨고 노드가 특정 컴포넌트 타입에 묶여 이식이 막힌다.
- **중단되어도 상태를 남기지 않는다.** 배수·플래그·연출 오브젝트를 건드리는 노드는 종료 시 원복한다. BT는 상위 컴포지트에 의해 언제든 중단될 수 있다.

## 대상 주입 방식

모든 노드는 공통 입력으로 `Agent`(타입 `EnemyAgent`)를 받는다. `EnemyAgent`는 노드가 필요로 하는 능력만 노출하는 베이스 MonoBehaviour다.

`EnemyAgent`는 `Enemy`를 상속하지 않고 **같은 오브젝트에 나란히 부착한다**(병존). 그래서 잡몹 프리팹에도 컴포넌트를 추가하는 것만으로 같은 노드를 재사용할 수 있고, `Enemy`의 `private` 멤버를 열 필요가 없다. 보스별 고유 능력이 필요하면 `EnemyAgent`를 상속한 파생 컴포넌트를 쓴다 — 노드 입력 타입이 `EnemyAgent`이므로 파생 타입이 그대로 들어간다.

`EnemyAgent`는 **상태를 갖지 않는다.** 패턴 이동속도 배수는 `MonsterMove`가, 받는 피해 배수는 `Enemy`가 소유하고 `EnemyAgent`는 전달만 한다. 양쪽이 같은 값을 들면 동기화가 깨진다. 유일한 예외는 패턴 쿨다운 기록(`EnemyPatternMemory`)이다.

와이어링은 두 가지 방법이 있고 둘 다 Unity Behavior가 정식 지원한다.

| 방법 | 절차 | 비고 |
|---|---|---|
| Blackboard 변수 연결 | 그래프의 `Self`(GameObject) 변수를 노드의 `Agent` 입력에 연결한다 | 패키지가 `GameObject` → `Component` 자동 캐스팅(`GetComponent`)을 지원한다 |
| 인스펙터 직접 지정 | 보스 프리팹의 `BehaviorGraphAgent` 인스펙터에서 Blackboard 변수 값을 오버라이드한다 | `UnityEngine.Object` 파생 타입이면 오브젝트 필드로 노출된다 |

전자를 기본으로 한다. 그래프 1본으로 여러 보스를 돌릴 때 프리팹마다 배선을 다시 하지 않아도 된다.

### EnemyAgent가 노출하는 것

`EnemyAgent`는 기존 `Enemy` / `MonsterMove` / `Animator`에 대한 얇은 파사드이며, 노드는 이 경계 너머를 알지 못한다. 구현: `Assets/Scripts/CombatSystem/Enemy/AI/EnemyAgent.cs`.

| 멤버 | 용도 | 비고 |
|---|---|---|
| `PatternSpeedFactor` (읽기/쓰기) | 돌진 가속, 방어 태세 크롤 | `MonsterMove`의 패턴 축에 위임. 감속 디버프 축과 **곱해진다** |
| `EffectiveMoveSpeed` (읽기) | 돌진 충돌 피해 계산 | 배수가 아니라 디버프까지 반영된 최종 속도 |
| `MovementOwned` (읽기/쓰기) | 준비 동작 중 정지, 돌진 중 전진 유지 | `Enemy.MovementOwnedByBehavior`에 위임. 켜면 `Enemy.Update`가 `IsStopped`와 `SetHasTarget` 둘 다 건드리지 않는다 |
| `MovementStopped` (읽기/쓰기) | 소유권 중 정지·재개 지시 | 속도 배수에 하한 클램프가 있어 **완전 정지는 이 축으로만** 가능하다 |
| `DamageTakenFactor` (읽기/쓰기) | 방어 태세 | `Enemy`에 위임. 0=무적, 1 초과=취약 |
| `TryPlayAnimation(trigger)` / `AnimationNormalizedTime` / `IsAnimatorInTransition` / `HasAnimator` | 준비 모션 + 재생 종료 판정 | `EnemyAgent`가 `Animator`를 직접 들어 `MonsterAnimation` 수정이 불필요하다 |
| `Forward` (읽기) | 앞뒤 판정 | `MonsterMove`가 붙은 transform 기준(루트가 아님) |
| `HpRatio` (읽기) | 조건 분기, HP 연동 파라미터 | |
| `Faction` (읽기) | 반경 질의의 아군/적군 판정 | 진영을 상수로 박지 않아 노드를 플레이어 측 유닛에 붙여도 Ally/Hostile이 뒤집히지 않는다 |
| `UnitLayerMask` (읽기) | 반경 질의의 물리 프리필터 | **`LayerMask`는 Blackboard 변수 지원 타입이 아니라** 노드 입력으로 못 받는다. 프리팹 인스펙터에서 authoring한다 |
| `IsPatternReady(key, cooldown)` / `MarkPatternUsed(key)` | 패턴 게이트 | 무상태 원칙의 유일한 예외(`EnemyPatternMemory`) |
| `SpawnMinion(prefab)` / `AliveMonsterCount` / `HasSpawner` / `BindSpawner(spawner)` | 지속 소환 | 스포너는 스폰 시점에 `MonsterSpawn`이 주입한다. 정적 싱글톤을 쓰지 않아 스포너 다중 구성이 가능하다 |

## 실행 모델 (그래프 저작 전 필독)

패키지 소스에서 확인한 동작이며, 패턴 그래프를 짤 때(#235) 전제가 되는 내용이다.

**Selector(`Try In Order`)는 선점하지 않는다.** 현재 자식이 Running인 동안 `SelectorComposite.OnUpdate`는 `Status.Waiting`만 반환하고 **앞선 자식의 조건을 다시 평가하지 않는다.** 한 브랜치에 진입하면 그 브랜치가 성공/실패로 끝날 때까지 트리는 거기 잔류한다.

따라서:

- **패턴 간 상호 배타는 우선순위가 아니라 이 잔류 성질이 보장한다.** 어느 순간에도 한 브랜치만 돈다. 우선순위는 "이전 브랜치가 끝나고 다시 평가하는 시점에 누가 뽑히는지"만 정한다.
- **조건이 풀렸는데도 패턴이 계속되는 구간이 생긴다.** P2 방어 태세를 짧은 지속시간으로 반복 갱신하는 설계가 바로 이 성질에 대한 대응이다 — 길게 잡으면 잡몹을 다 정리한 뒤에도 크롤이 유지된다.
- **Running이 끝나지 않는 노드는 패턴 Selector 전체를 영구 봉인한다.** 이때 P4 지속 소환은 별도 병렬 브랜치라 계속 돌기 때문에 겉보기로는 보스가 정상 동작하는 것처럼 보여 원인을 찾기 어렵다. **시간이 걸리는 노드에는 반드시 상한이 있어야 한다** — 이 세트에서는 `EnemyAccelerateAction.MaxDuration`, `EnemyPlayAnimationAction.MaxWaitSeconds`, 나머지는 `Duration`이 그 역할을 한다. 내장 `Time Out`(`Flow`) 데코레이터로 감싸는 방법도 있다.

**선점이 필요하면 `Priority Abort`를 쓴다.** `Flow/Abort` 카테고리의 `ObserverAbortModifier`가 조건 목록을 감시하다가 성립하면 낮은 우선순위 형제를 중단시키고 부모 컴포지트를 처음부터 재평가시킨다(`AbortTarget` = `LowerPriority` / `Self` / `Both`). 예: "돌진 준비 중에 본진이 파괴되면 즉시 중단" 같은 요구가 생기면 이걸 얹는다. 현재 설계는 선점 없이 성립하므로 기본 그래프에는 쓰지 않는다.

**노드가 Running이라고 보스가 멈추는 것이 아니다.** 이동은 `MonsterMove.Update`가 BT와 무관하게 구동한다. 정지는 `EnemyHoldPositionAction`이 이동 소유권을 잡고 `IsStopped`를 켤 때만 일어난다. 그래서 P3 마력 봉인의 예고 구간에도 보스는 계속 전진하며, 예고 원이 `Agent`의 자식이라 **원이 보스를 따라 움직인다** — 예고한 범위와 실제 봉인이 걸리는 범위가 어긋난다(아래 「미확정 / TODO」).

## 작성 규약

Unity Behavior `com.unity.behavior` 1.0.16 기준이다.

- **네임스페이스를 두지 않는다.** 따라서 **클래스 이름이 전역에서 유일**해야 한다. 새 노드를 만들기 전에 이름 중복을 확인한다. (패키지는 네임스페이스가 있어도 동작하지만 — 기존 MiniBoss 노드가 `NorthLand.Combat.Boss`를 쓴다 — 신규 노드는 이 규약으로 통일한다.)
- Action은 `Unity.Behavior.Action`을, Condition은 `Unity.Behavior.Condition`을 상속한다. 네임스페이스 없는 파일에서 `Action`은 `System.Action`과 충돌하므로 `using Action = Unity.Behavior.Action;` 별칭을 지정한다.
- 클래스는 `partial`로 선언하고 `[System.Serializable, GeneratePropertyBag]`을 붙인다. Action은 `[NodeDescription(...)]`, Condition은 `[Condition(...)]`으로 표시 이름·설명·story·category·id를 채운다 (**어트리뷰트가 다르다**).
- 노드 `id`는 32자리 16진수 GUID이며 **노드마다 새로 발급**한다. 복사해 쓰면 그래프에서 노드가 뒤섞인다.
- `category`는 `Action/Enemy` 또는 `Conditions/Enemy`로 통일한다. 에디터 노드 목록의 분류에 쓰인다.
- 입력 파라미터는 `[SerializeReference] public BlackboardVariable<T>` 필드로 선언한다. 상수를 코드에 박지 않는다.
- **`T`로 쓸 수 있는 타입이 제한된다**: `UnityEngine.Object` 파생 / 프리미티브 / enum / 패키지 지원 목록(`string`, `Color`, `Vector*`, `List<>` 등). `LayerMask`는 **쓸 수 없다** — `EnemyAgent`가 인스펙터에서 들고 노드가 읽는다.
- enum은 `[BlackboardEnum]`을 붙이면 그래프 Blackboard의 변수 타입 목록에도 노출된다. 노드 입력으로만 쓸 거면 없어도 동작하지만, 수치를 Blackboard로 올리는 원칙상 붙인다.
- 즉시 끝나는 동작은 `OnStart`에서 처리하고 성공을 반환한다. 시간이 걸리는 동작은 `OnUpdate`에서 Running을 유지하다가 완료 시 성공을 반환한다.
- **상태를 바꾸는 노드는 `OnEnd`에서 원복한다.** `OnEnd`는 정상 종료와 상위 컴포지트에 의한 중단 모두 지나가는 유일한 경로다. 원복 대상은 상수(1 등)가 아니라 **진입 시점에 읽어둔 값**이어야 한다 — 상위에서 걸어둔 배수를 지우지 않도록.
- 실패는 한국어 메시지를 `LogFailure`로 남기고 실패를 반환한다 (SystemMap §6 컨벤션).
- `Agent`가 null이면 실패를 반환한다. 다만 `Target`이 null인 경우는 조건 노드에서 조용히 거짓으로 처리한다 — 본진은 밤에 런타임 스폰되므로 초반에 null인 것이 정상이다.
- **"조건이 안 맞아 아무것도 안 한 것"은 실패가 아니라 성공이다.** 실패로 두면 상위 시퀀스가 매 틱 재시도한다(소환 상한 도달, 봉인 범위에 타워 없음, 실효 속도가 하한 미만 등).

## Condition 노드

공통 입력 `Agent`는 표에서 생략한다.

| 노드 | 파라미터 | 동작 | 사용처 | 상태 |
|---|---|---|---|---|
| `EnemyDistanceToTargetBelowCondition` | `Target`(GameObject), `Distance`(float) | `Target`까지 거리가 `Distance` 미만이면 참. `Target`이 null이거나 `Distance` 0 이하면 거짓 | P1 | 구현 |
| `EnemyUnitsInRangeCondition` | `Filter`(`EnemyUnitFilter`), `Direction`(`EnemyRelativeDirection`), `Radius`(float), `MinCount`(int) | 지정 방향 반경 안의 대상 수가 `MinCount` 이상이면 참. 방향은 `Agent.Forward`와의 내적 부호로 판정한다. `MinCount` 0 이하면 항상 참 | P2, P3 | 구현 |
| `EnemyPatternGateCondition` | `Key`(string), `CooldownSeconds`(float) | 해당 `Key`가 한 번도 사용되지 않았거나 마지막 사용 후 `CooldownSeconds`가 지났으면 참. `CooldownSeconds < 0`이면 1회 한정. `Key`가 비면 거짓 | P1, P3 | 구현 |

`LayerMask`는 노드 파라미터에서 빠졌다 — Blackboard 변수 지원 타입이 아니다. 대신 `EnemyAgent.UnitLayerMask`(프리팹 인스펙터 authoring)를 읽는다.

반경 질의는 `EnemyNodeQuery`가 공유한다. `Filter`가 `Tower`면 `Tower.Active` 정적 리스트를 순회하고(물리 질의 불필요), `Ally` / `Hostile`은 `Physics.OverlapSphereNonAlloc` 후 `IDamageable.Faction`을 `Agent.Faction`과 비교해 가른다. 콜라이더가 여럿인 프리팹에서 중복 집계되지 않게 `IDamageable` 단위로 중복 제거하며, **자기 자신은 항상 제외**한다(보스가 자기를 세면 임계값이 1 어긋난다).

`Tower` 필터의 집합은 **공격 타워**다 — `EnemyNodeQuery.IsAttackTower`(= `Tower.AttackInterval > 0`)로 판정하며 오라·유틸 계열(Magic 타입: haste / poison / 이동속도 감소)은 빠진다. `EnemyApplyTowerDebuffAction`도 같은 판정을 쓴다.

**`Tower.Active` 등록 여부로 판정하지 않는 이유**: 지금은 `AuraTower`가 `MonoBehaviour` 직접 파생이라 리스트에 아예 없어 두 방식의 결과가 같지만, `AuraTower : Tower` 리팩토링이 예정돼 있다. 등록 여부로 판정하면 그때 이동속도 감소 타워가 봉인 대상에 들어와 "봉인 중에도 감속은 살아남는다"는 설계 의도가 조용히 뒤집힌다. 카테고리 판정은 리팩토링 전후로 거동이 같다(현재는 no-op). 트리거(Condition)와 payload(Action)에 같은 판정을 쓰는 이유는 집합이 어긋나면 봉인해도 아무것도 안 걸리는 오라 타워 뭉치에 P3가 발동하기 때문이다.

## Action 노드

공통 입력 `Agent`는 표에서 생략한다.

| 노드 | 파라미터 | 동작 | 사용처 | 상태 |
|---|---|---|---|---|
| `EnemyResolveTargetAction` | `TargetKind`(`EnemyTargetKind`), `SearchRadius`(float), out `Target`(GameObject) | `TargetKind`에 해당하는 GameObject를 찾아 Blackboard 변수에 기록한다. 못 찾으면 실패(로그 없음 — 본진 미스폰이 정상 경로) | P1 | 구현 |
| `EnemyMarkPatternUsedAction` | `Key`(string) | 패턴 사용 시각을 기록한다. `EnemyPatternGateCondition`과 짝을 이룬다. 중단 시 되돌리지 않는다 | P1, P3 | 구현 |
| `EnemyHoldPositionAction` | `Duration`(float) | 이동 소유권을 잡고 `Duration` 동안 제자리에 멈춘다. 종료 시 정지를 풀고 소유권을 반납한다 | P1 | 구현 |
| `EnemyPlayAnimationAction` | `Trigger`(string), `WaitForEnd`(bool), `MaxWaitSeconds`(float) | 애니메이션 트리거를 발동한다. `WaitForEnd`면 재생이 끝날 때까지 Running을 유지한다 | P1 | 구현 |
| `EnemyAccelerateAction` | `Target`(GameObject), `MaxFactor`(float), `AccelPerSecond`(float), `ArriveDistance`(float), `MaxDuration`(float) | 이동 소유권을 잡고, 매 프레임 패턴 속도 배수를 `AccelPerSecond`만큼 올리되 `MaxFactor`로 클램프한다. `Target`까지 거리가 `ArriveDistance` 이하가 되면 성공. `MaxDuration` 초과 시 경고 로그 + 실패 | P1 | 구현 |
| `EnemyImpactTargetAction` | `Target`(GameObject), `DamagePerSpeedUnit`(float), `MinSpeed`(float) | `Target`의 `IDamageable`에 `실효 이동속도 × DamagePerSpeedUnit` 피해를 준다. 실효 속도가 `MinSpeed` 미만이면 피해 없이 성공 | P1 | 구현 |
| `EnemySetSpeedFactorAction` | `Factor`(float), `Duration`(float) | 패턴 속도 배수를 설정한다. `Duration > 0`이면 그 시간 유지 후 원복, **0 이하면 즉시 성공하며 원복하지 않는다**(기본 진군용) | P1, P2 | 구현 |
| `EnemySetDamageTakenFactorAction` | `Factor`(float), `Duration`(float) | 받는 피해 배수를 설정한다. 원복 규칙은 위와 같다 | P2 | 구현 |
| `EnemyApplyTowerDebuffAction` | `Radius`(float), `DamageMultiplier`(float), `AttackSpeedMultiplier`(float), `Duration`(float) | 반경 안의 **공격 타워**(`IsAttackTower`) 각각에 고유 sourceId로 `ApplyBuff`를 건다. 배율 1 미만이면 디버프가 된다. 범위에 타워가 없어도 성공 | P3 | 구현 |
| `EnemyShowTelegraphCircleAction` | `Radius`(float), `Duration`(float), `FillColor`(Color), `OutlineColor`(Color) | `RangeCircle`을 `Agent`의 자식으로 만들어 예고 범위를 표시하고 `Duration` 뒤 파괴한다. 종료 시 정리한다 | P3 | 구현 |
| `EnemySpawnMinionsAction` | `Prefab`(GameObject), `Count`(int), `MaxAlive`(int) | 스폰 지점에 잡몹을 투입한다. `monsterParent` 자식으로 넣고 경로를 부여한다. 상한은 마리마다 재확인하며, 상한에 걸려 한 마리도 못 넣어도 성공 | P4 | 구현 |

표에서 벗어난 곳 3군데는 구현 중 필요해서 조정한 것이다.

- **`EnemyResolveTargetAction`에 `SearchRadius` 추가.** `NearestTower` / `NearestAlly`가 탐색 반경 없이는 동작할 수 없다. `PlayerBase`와 `Self`는 이 값을 무시한다.
- **`EnemyPlayAnimationAction`에 `MaxWaitSeconds`, `EnemyAccelerateAction`에 `MaxDuration` 추가.** 둘 다 영구 Running 방지용이다. 트리거 이름이 AnimatorController에 없으면 전이가 일어나지 않고, 돌진은 `ArriveDistance`가 너무 작거나 대상이 경로에서 닿을 수 없으면 도달하지 못한다. 선점이 없는 Selector에서 영구 Running은 패턴 Selector 전체를 봉인한다(「실행 모델」 참조). 돌진 상한 초과는 **실패**로 반환한다 — 성공으로 두면 뒤이은 충돌 피해가 도달하지도 않은 채 터진다. 재생 종료 판정은 `normalizedTime` 폴링을 택했고, "전이가 한 번 끝난 뒤"부터 진행도를 신뢰한다 — 그러지 않으면 트리거 직후 이전 상태의 진행도(이미 1 초과)가 읽혀 준비 모션이 시작 전에 끝난 것으로 오판된다.
- **`EnemyAccelerateAction`의 원복이 비대칭이다.** 소유권은 항상 반납하지만 속도 배수는 **도달하지 못하고 끝난 경우에만** 되돌린다. 성공 시 유지하는 이유는 바로 뒤의 `EnemyImpactTargetAction`이 "충돌 시점의 실효 이동속도"를 읽어야 하기 때문이다 — 여기서 원복하면 평상시 속도가 읽혀 감속 파훼가 무의미해진다. 성공 후 배수 1 복귀는 그래프의 기본 진군 브랜치가 담당한다.

`EnemyApplyTowerDebuffAction`의 sourceId는 `Agent.GetInstanceID() ^ "EnemyApplyTowerDebuff".GetHashCode()`다. 인스턴스 ID만 쓰면 같은 보스가 거는 다른 효과와 충돌하고, 고정 문자열만 쓰면 보스 여러 마리가 서로의 봉인을 덮어쓴다. 이 노드는 만료를 `Tower`가 duration으로 처리하므로 종료 시 원복하지 않는다 — 되돌리면 예고를 보고 회피한 플레이어가 봉인을 공짜로 벗는다.

## 보조 타입

| 타입 | 역할 | 상태 |
|---|---|---|
| `EnemyAgent` | 노드가 참조하는 베이스 MonoBehaviour. `Enemy`와 병존하며 무상태 파사드로 동작한다. 위 「EnemyAgent가 노출하는 것」 참조 | 구현 |
| `EnemyPatternMemory` | 패턴 `Key`별 마지막 사용 시각을 보관한다. MonoBehaviour가 아니라 plain class로 `EnemyAgent`가 내부 필드로 든다 — 프리팹에 컴포넌트를 하나 더 요구하지 않기 위해 | 구현 |
| `EnemyNodeQuery` | 반경 질의 공용 static 헬퍼. `EnemyUnitsInRangeCondition`·`EnemyResolveTargetAction`·`EnemyApplyTowerDebuffAction`이 공유해 앞뒤 판정과 타워 집합 정의(`IsAttackTower`)가 갈라지지 않게 한다 | 구현 |
| `EnemyUnitFilter` / `EnemyRelativeDirection` / `EnemyTargetKind` | 노드 입력용 `[BlackboardEnum]` 열거형 3종 | 구현 |

## 기존 시스템에 필요한 변경

`EnemyAgent`가 파사드 역할을 하므로 노드는 아래 클래스에 직접 닿지 않는다. 다만 `EnemyAgent`가 능력을 제공하려면 기존 클래스에 진입점이 필요하다. 상세는 설계 문서의 「선행 작업」 절을 따른다.

| 대상 | 변경 | 상태 |
|---|---|---|
| `IMovementAgent` | 다축 합성 계약 추가 — `EffectiveMoveSpeed` / `PatternSpeedFactor` / `AddSpeedDebuff(sourceId, factor)` / `RemoveSpeedDebuff(sourceId)` | 완료(#233). **감속 타워 담당자와 공유 계약** — 구체 타입이 아니라 이 인터페이스로 부르면 된다 |
| `MonsterMove` | 축별 소유 + 합성 + 하한 클램프(`minMoveSpeed` 0.15, 직렬화 필드). `SetMoveSpeed`는 **기준 속도 주입**으로 의미 재정의 | 완료(#233). 크롤 배수가 `fallbackMoveSpeed`(3)로 되돌아 오히려 빨라지던 함정 제거 |
| `Enemy` | BT 이동 소유권 플래그 `MovementOwnedByBehavior` — 켜져 있으면 `Update`가 `IsStopped`와 `SetHasTarget` **둘 다** 건드리지 않는다 | 완료(#233) |
| `Enemy.TakeDamage` | 받는 피해 배수 `DamageTakenFactor` 적용 | 완료(#233) |
| `Enemy.SetSpeedMultiplier` | `movement`의 패턴 축 위임으로 전환. 로컬 `baseMoveSpeed` / `speedMultiplier` 필드 제거 | 완료(#233). 중간보스 그래프가 쓰는 진입점이라 시그니처 유지 |
| `MonsterSpawn` | 공개 스폰 API `SpawnMonster(prefab)` / `AliveMonsterCount` + 스폰 시점에 `EnemyAgent`로 스포너 주입 | 완료(#233). 정적 싱글톤을 쓰지 않아 스포너 다중 구성이 가능하다 |
| `MonsterAnimation` | 없음 | `EnemyAgent`가 `Animator`를 직접 든다 |

`Enemy.Update`에서 `SetHasTarget`까지 함께 차단하는 것은 설계 문서에 없던 보강이다. `MonsterStateMachine`이 Attack 상태에서 `SetMoveEnabled(false)`를 걸기 때문에(`MonsterStateMachine.cs:141`) 타겟 통지를 살려두면 돌진이 본진 사거리에 진입하는 순간 멈춰 P1이 절름발이가 된다. 부수 효과로 소유권 중에는 근접 평타가 나가지 않는다 — 충돌 피해가 그 역할을 대신한다.

## 새 노드 추가 절차

1. 기존 표에 같은 일을 하는 노드가 있는지 먼저 확인한다. 있으면 **새 노드 대신 파라미터를 추가**한다.
2. 이름을 정한다. 접두사 `Enemy` + 동사구 + `Action` / `Condition`. 특정 보스 이름을 넣지 않는다. 네임스페이스가 없으므로 전역 유일해야 한다.
3. 노드가 필요로 하는 능력이 `EnemyAgent`에 이미 있는지 확인한다. 없으면 `EnemyAgent`에 먼저 추가하고, 기존 클래스를 직접 참조하지 않는다.
4. `Assets/Scripts/CombatSystem/Enemy/AI/Nodes/` 에 파일을 만들고 작성 규약을 따른다. `id`는 새 GUID를 발급한다.
5. 에디터 밖에서 파일을 만들었다면 `unity-cli editor refresh`로 `.meta`를 생성시키고, 커밋에 에셋과 `.meta`를 함께 포함한다.
6. 이 문서의 표에 행을 추가하고 상태 칸을 채운다.
7. 수치는 그래프 노드 입력에 인라인으로 박지 말고 Blackboard 변수로 올린다.

### 검증 스니펫

`unity-cli exec`로 노드가 제대로 등록됐는지 확인할 수 있다. 에디터가 실행 중이어야 한다.

```csharp
// GUID 유일성 — NodeDescriptionAttribute의 속성은 internal이라 리플렉션으로 읽는다.
// 속성 이름은 Id가 아니라 GUID다.
var flags = System.Reflection.BindingFlags.Instance
          | System.Reflection.BindingFlags.Public
          | System.Reflection.BindingFlags.NonPublic;
var ids = new System.Collections.Generic.Dictionary<string, string>();
foreach (var t in typeof(EnemyAgent).Assembly.GetTypes())
{
    var nd = t.GetCustomAttributes(typeof(Unity.Behavior.NodeDescriptionAttribute), false);
    var cd = t.GetCustomAttributes(typeof(Unity.Behavior.ConditionAttribute), false);
    object a = nd.Length > 0 ? nd[0] : (cd.Length > 0 ? cd[0] : null);
    if (a == null) continue;
    string id = a.GetType().GetProperty("GUID", flags).GetValue(a).ToString();
    if (ids.ContainsKey(id)) Debug.LogError($"GUID 중복: {t.Name} <-> {ids[id]}");
    else ids[id] = t.Name;
}
```

에디터 노드 목록 등재와 입력 타입 유효성은 `Unity.Behavior.NodeRegistry`(내부 타입, `Unity.Behavior.Authoring` 어셈블리)의 `GetInfo(Type)` / `IsBlackboardVariableTypeValid(FieldInfo, ref Type)`로 확인한다. 후자가 거짓이면 그 입력은 그래프 에디터에 노출되지 않는다.

## 미확정 / TODO

- [x] Condition 3종 / Action 11종 / 보조 타입 4종 구현 완료(#234).
- [x] `EnemyUnitsInRangeCondition`의 `Filter` / `Direction`을 열거형 Blackboard 변수로 받을 수 있다. `[BlackboardEnum]`으로 확인했고 노드를 방향별로 분리하지 않았다.
- [x] `EnemySpawnMinionsAction`의 `Prefab`을 `BlackboardVariable<GameObject>`로 받을 수 있다. `GameObject`가 기본 Blackboard 타입이고 `UnityEngine.Object` 파생 변수는 `ObjectValue`로 에셋 참조를 직렬화한다.
- [x] `EnemyPlayAnimationAction`의 재생 종료 판정은 `normalizedTime` 폴링으로 정했다. AnimationEvent는 클립마다 이벤트를 심어야 해서 아직 없는 보스 AnimatorController(#235)의 저작 부담을 노드로 떠넘긴다.
- [x] `EnemyApplyTowerDebuffAction`의 sourceId는 `Agent.GetInstanceID() ^ 고정 문자열 해시`로 정했다(위 Action 절 참조).
- [ ] **런타임 동작 미검증.** 이 노드들을 쓰는 그래프 에셋이 아직 없다. 검증된 것은 컴파일 / GUID 유일성(프로젝트 전체 18개 고유) / 에디터 노드 목록 등재 14/14 / 입력 타입 유효성 14/14 / `PropertyBag` 생성 14/14다. 패턴 동작·중단 시 원복은 #235 Play 검증에서 확인한다.
- [ ] `EnemyAgent.UnitLayerMask`가 프리팹마다 채워져야 한다. 비어 있으면 반경 질의가 항상 0을 반환해 P2·P3가 조용히 발동하지 않는다 — #235에서 프리팹 작성 시 확인.
- [ ] `AliveMonsterCount`가 `monsterParent.childCount`라 **보스 자신과 사망 연출 중인 몬스터(`destroyDelay` 2초)가 포함된다**. `MaxAlive` 실효값이 의도보다 빡빡해진다. WL-038의 미해소 잔여와 같은 축 — #235 Play 검증에서 실측해 보정할지 판단한다.
- [ ] 노드 타이밍이 전부 스케일드 타임이라 게임 배속에 비례한다(`Time.deltaTime` / `Time.time`). BT 내장 `Wait`도 같은 성질이라 전체를 함께 정해야 한다. 의도인지 확인 필요.
- [ ] **P3 예고 범위와 실제 봉인 범위가 어긋난다 — 프로토타입은 ③으로 가고 정식 대응은 미확정(TBD).** 예고 중에도 보스가 전진하고(BT Running이 이동을 멈추지 않는다) 예고 원이 `Agent` 자식이라 함께 움직이므로, `EnemyApplyTowerDebuffAction`이 도는 시점의 보스 위치가 예고를 시작한 위치와 다르다.

  | 선택지 | 내용 | 판단 |
  |---|---|---|
  | ① 시전 중 정지 | 예고 앞에 `EnemyHoldPositionAction`을 둔다 | 예고 신뢰도 최고. 대신 보스가 자주 멈춰 P1 준비 정지의 긴장감이 희석되고 화력 집중 구간이 하나 더 생긴다 |
  | ② 예고 원 월드 고정 | 원을 `Agent` 자식이 아니라 월드에 생성 | 노드 수정 필요. 지금 코드를 늘릴 이유가 없다 |
  | ③ 드리프트 수용 | 예고 `Duration`을 짧게(0.5초 수준) 잡아 덮는다 | **프로토타입 채택.** 코드 변경 0, 짧으면 체감되지 않는다 |

  **프로토타입 플레이에서 드리프트가 체감되면 ①/②로 전환한다.** 코드 주석은 `EnemyShowTelegraphCircleAction.cs` 상단 `TODO(TBD)`.
- [ ] `EnemyNodeQuery`의 물리 버퍼가 64개 고정이다. 잡몹이 반경 안에 64체를 넘으면 초과분이 집계에서 빠진다 — P4 소환 상한과 함께 판단.

## 참고

- `Docs/Monster/Boss/BossDesign.md` — 보스 설계와 패턴별 노드 구성
- `Docs/Skill/PlayerSkill.md` — 런타임 `AddComponent` 선례, effectId 분리 규약
- `Docs/Review/SystemMap.md` §6 — 실패 처리·로그 컨벤션
