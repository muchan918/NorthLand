# 보스 BT 노드 레퍼런스

- 관련 이슈: 미생성 (신규 생성 예정)
- 구현 위치: `Assets/Scripts/CombatSystem/Enemy/AI/` (예정)
- 설계 문서: `Docs/Monster/Boss/BossDesign.md`
- 이 문서는 Unity Behavior 커스텀 리프 노드의 **정의 대장**이다. 노드는 보스가 늘어날수록 재사용되며 계속 추가된다. 노드를 새로 만들거나 파라미터를 바꾼 사람은 같은 PR에서 이 표의 행을 갱신한다.
- 현재 **모든 노드가 미구현**이다. 상태 칸으로 구분한다.

> `Assets/Scripts/CombatSystem/Enemy/MiniBoss/`의 기존 노드 4종은 이 대장에 포함되지 않는다. 중간보스 전용이며 재사용하지도, 참조하지도 않는다.

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

### EnemyAgent가 노출해야 하는 것

노드가 요구하는 능력의 목록이다. `EnemyAgent`는 기존 `Enemy` / `MonsterMove` / `Animator`에 대한 얇은 파사드이며, 노드는 이 경계 너머를 알지 못한다.

| 능력 | 용도 | 비고 |
|---|---|---|
| 패턴 이동속도 배수 (읽기/쓰기) | 돌진 가속, 방어 태세 크롤 | 감속 디버프 축과 **곱해지는** 별도 축이어야 한다 |
| 실효 이동속도 (읽기) | 돌진 충돌 피해 계산 | 배수가 아니라 디버프까지 반영된 최종 속도 |
| 이동 소유권 (쓰기) | 준비 동작 중 정지 | `Enemy.Update`의 매 프레임 덮어쓰기를 차단해야 한다 |
| 받는 피해 배수 (쓰기) | 방어 태세 | |
| 애니메이션 트리거 | 준비 모션 | `EnemyAgent`가 `Animator`를 직접 들면 `MonsterAnimation` 수정이 불필요하다 |
| 진행 방향 (읽기) | 앞뒤 판정 | |
| HP 비율 (읽기) | 조건 분기, HP 연동 파라미터 | |

## 작성 규약

Unity Behavior `com.unity.behavior` 1.0.16 기준이다.

- **네임스페이스를 두지 않는다.** 따라서 **클래스 이름이 전역에서 유일**해야 한다. 새 노드를 만들기 전에 이름 중복을 확인한다.
- Action은 `Unity.Behavior.Action`을, Condition은 `Unity.Behavior.Condition`을 상속한다. 네임스페이스 없는 파일에서는 `Action`이 `System.Action`과 충돌할 수 있으므로 별칭을 지정한다.
- 클래스는 `partial`로 선언하고 직렬화 어트리뷰트와 속성 백 생성 어트리뷰트를 붙인다. 노드 설명 어트리뷰트에 표시 이름·설명·story·category·id를 채운다.
- 노드 `id`는 32자리 16진수 GUID이며 **노드마다 새로 발급**한다. 복사해 쓰면 그래프에서 노드가 뒤섞인다.
- `category`는 `Action/Enemy` 또는 `Conditions/Enemy`로 통일한다. 에디터 노드 목록의 분류에 쓰인다.
- 입력 파라미터는 Blackboard 변수 필드로 선언한다. 상수를 코드에 박지 않는다.
- 즉시 끝나는 동작은 시작 시점에 처리하고 성공을 반환한다. 시간이 걸리는 동작은 실행 중 상태를 유지하다가 완료 시 성공을 반환한다.
- 실패는 한국어 메시지를 로그로 남기고 실패를 반환한다 (SystemMap §6 컨벤션).
- `Agent`가 null이면 실패를 반환한다. 다만 `Target`이 null인 경우는 조건 노드에서 조용히 거짓으로 처리한다 — 본진은 밤에 런타임 스폰되므로 초반에 null인 것이 정상이다.

## Condition 노드

공통 입력 `Agent`는 표에서 생략한다.

| 노드 | 파라미터 | 동작 | 사용처 | 상태 |
|---|---|---|---|---|
| `EnemyDistanceToTargetBelowCondition` | `Target`(GameObject), `Distance`(float) | `Target`까지 거리가 `Distance` 미만이면 참. `Target`이 null이면 거짓 | P1 | 미구현 |
| `EnemyUnitsInRangeCondition` | `Filter`(Ally\|Tower\|Hostile), `Direction`(Any\|Forward\|Backward), `Radius`(float), `MinCount`(int), `LayerMask` | 지정 방향 반경 안의 대상 수가 `MinCount` 이상이면 참. 방향은 `Agent`의 진행 방향과의 내적 부호로 판정한다 | P2, P3 | 미구현 |
| `EnemyPatternGateCondition` | `Key`(string), `CooldownSeconds`(float) | 해당 `Key`가 한 번도 사용되지 않았거나 마지막 사용 후 `CooldownSeconds`가 지났으면 참. `CooldownSeconds < 0`이면 1회 한정 | P1, P3 | 미구현 |

`Filter`가 `Tower`일 때는 `Tower.Active` 정적 리스트를 순회한다(물리 질의 불필요). `Ally` / `Hostile`은 `Physics.OverlapSphereNonAlloc`에 `LayerMask`를 적용한다. `AuraTower`는 `Tower.Active`에 등록되지 않으므로 `Tower` 필터에 잡히지 않는다.

## Action 노드

공통 입력 `Agent`는 표에서 생략한다.

| 노드 | 파라미터 | 동작 | 사용처 | 상태 |
|---|---|---|---|---|
| `EnemyResolveTargetAction` | `TargetKind`(PlayerBase\|NearestTower\|NearestAlly\|Self), out `Target`(GameObject) | `TargetKind`에 해당하는 GameObject를 찾아 Blackboard 변수에 기록한다. 못 찾으면 실패 | P1 | 미구현 |
| `EnemyMarkPatternUsedAction` | `Key`(string) | 패턴 사용 시각을 기록한다. `EnemyPatternGateCondition`과 짝을 이룬다 | P1, P3 | 미구현 |
| `EnemyHoldPositionAction` | `Duration`(float) | 이동 소유권을 잡고 `Duration` 동안 제자리에 멈춘다. 종료 시 소유권을 반납한다 | P1 | 미구현 |
| `EnemyPlayAnimationAction` | `Trigger`(string), `WaitForEnd`(bool) | 애니메이션 트리거를 발동한다. `WaitForEnd`면 재생이 끝날 때까지 실행 상태를 유지한다 | P1 | 미구현 |
| `EnemyAccelerateAction` | `Target`(GameObject), `MaxFactor`(float), `AccelPerSecond`(float), `ArriveDistance`(float) | 매 프레임 패턴 속도 배수를 `AccelPerSecond`만큼 올리되 `MaxFactor`로 클램프한다. `Target`까지 거리가 `ArriveDistance` 이하가 되면 성공 | P1 | 미구현 |
| `EnemyImpactTargetAction` | `Target`(GameObject), `DamagePerSpeedUnit`(float), `MinSpeed`(float) | `Target`의 `IDamageable`에 `실효 이동속도 × DamagePerSpeedUnit` 피해를 준다. 실효 속도가 `MinSpeed` 미만이면 피해 없이 성공 | P1 | 미구현 |
| `EnemySetSpeedFactorAction` | `Factor`(float), `Duration`(float) | 패턴 속도 배수를 설정한다. `Duration > 0`이면 그 시간 뒤, 0이면 종료 시 원복한다 | P1, P2 | 미구현 |
| `EnemySetDamageTakenFactorAction` | `Factor`(float), `Duration`(float) | 받는 피해 배수를 설정한다. 종료 시 원복한다 | P2 | 미구현 |
| `EnemyApplyTowerDebuffAction` | `Radius`(float), `DamageMultiplier`(float), `AttackSpeedMultiplier`(float), `Duration`(float) | 반경 안의 `Tower.Active` 각각에 고유 sourceId로 `ApplyBuff`를 건다. 배율 1 미만이면 디버프가 된다 | P3 | 미구현 |
| `EnemyShowTelegraphCircleAction` | `Radius`(float), `Duration`(float), `FillColor`, `OutlineColor` | `RangeCircle`로 예고 범위를 표시하고 `Duration` 뒤 숨긴다. 종료 시 정리한다 | P3 | 미구현 |
| `EnemySpawnMinionsAction` | `Prefab`(GameObject), `Count`(int), `MaxAlive`(int) | 스폰 지점에 잡몹을 투입한다. `monsterParent` 자식으로 넣고 경로를 부여한다. 현재 생존 수가 `MaxAlive` 이상이면 스킵하고 성공 | P4 | 미구현 |

## 보조 타입

| 타입 | 역할 | 상태 |
|---|---|---|
| `EnemyAgent` | 노드가 참조하는 베이스 MonoBehaviour. `Enemy`와 병존하며 무상태 파사드로 동작한다. 위 「EnemyAgent가 노출해야 하는 것」 참조 | 미구현 |
| `EnemyPatternMemory` | 패턴 `Key`별 마지막 사용 시각을 보관한다. `EnemyAgent`가 들고 있어도 되고 별도 컴포넌트로 분리해도 된다 | 미구현 |

## 기존 시스템에 필요한 변경

`EnemyAgent`가 파사드 역할을 하므로 노드는 아래 클래스에 직접 닿지 않는다. 다만 `EnemyAgent`가 능력을 제공하려면 기존 클래스에 진입점이 필요하다. 상세는 설계 문서의 「선행 작업」 절을 따른다.

| 대상 | 변경 | 필요 이유 |
|---|---|---|
| `MonsterMove` | 이동속도 다축 합성(패턴 축 × 디버프 축) + 하한 클램프 + 실효 속도 노출 | 감속 타워와 돌진 가속이 같은 값을 두고 경쟁해야 한다. **감속 타워 담당자와 공유 계약** |
| `Enemy` | BT 이동 소유권 플래그 | `Update`가 매 프레임 `movement.IsStopped`를 덮어써 준비 동작 중 정지가 무효화된다 |
| `Enemy.TakeDamage` | 받는 피해 배수 적용 지점 | 현재 감쇠·방어·무적 지점이 전혀 없다 |
| `MonsterSpawn` | 공개 스폰 API | `SpawnPrefab` / `SpawnGroupAsync`가 private이다 |
| `MonsterAnimation` | 없음 | `EnemyAgent`가 `Animator`를 직접 들면 수정이 불필요하다 |

## 새 노드 추가 절차

1. 기존 표에 같은 일을 하는 노드가 있는지 먼저 확인한다. 있으면 **새 노드 대신 파라미터를 추가**한다.
2. 이름을 정한다. 접두사 `Enemy` + 동사구 + `Action` / `Condition`. 특정 보스 이름을 넣지 않는다. 네임스페이스가 없으므로 전역 유일해야 한다.
3. 노드가 필요로 하는 능력이 `EnemyAgent`에 이미 있는지 확인한다. 없으면 `EnemyAgent`에 먼저 추가하고, 기존 클래스를 직접 참조하지 않는다.
4. `Assets/Scripts/CombatSystem/Enemy/AI/Nodes/` 에 파일을 만들고 작성 규약을 따른다. `id`는 새 GUID를 발급한다.
5. 에디터 밖에서 파일을 만들었다면 `unity-cli editor refresh`로 `.meta`를 생성시키고, 커밋에 에셋과 `.meta`를 함께 포함한다.
6. 이 문서의 표에 행을 추가하고 상태 칸을 채운다.
7. 수치는 그래프 노드 입력에 인라인으로 박지 말고 Blackboard 변수로 올린다.

## 미확정 / TODO

- [ ] 모든 노드 미구현. `EnemyAgent`와 선행 변경이 들어간 뒤 착수한다.
- [ ] `EnemyUnitsInRangeCondition`의 `Filter` / `Direction`을 열거형 Blackboard 변수로 받을 수 있는지 에디터에서 확인 필요. 불가하면 노드를 방향별로 분리한다.
- [ ] `EnemySpawnMinionsAction`의 `Prefab`을 Blackboard 변수로 프리팹 에셋 참조할 수 있는지 확인 필요. 불가하면 웨이브 SO나 `EnemyAgent`에서 참조를 받는다.
- [ ] `EnemyPlayAnimationAction`의 재생 종료 판정 방식 미정. `normalizedTime` 폴링과 AnimationEvent 콜백 중 선택.
- [ ] `EnemyApplyTowerDebuffAction`의 sourceId 채번 규칙 미정. 기존 관례는 TowerID 해시 / `GetInstanceID` / 고유 문자열 해시이며 다른 소스와 겹치지 않아야 한다.
- [ ] 노드 타이밍이 전부 스케일드 타임이라 게임 배속에 비례한다. 의도인지 확인 필요.

## 참고

- `Docs/Monster/Boss/BossDesign.md` — 보스 설계와 패턴별 노드 구성
- `Docs/Skill/PlayerSkill.md` — 런타임 `AddComponent` 선례, effectId 분리 규약
- `Docs/Review/SystemMap.md` §6 — 실패 처리·로그 컨벤션
