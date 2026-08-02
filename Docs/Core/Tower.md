# 전투 타워(Tower) — 시스템 명세 `[구현됨]`

> **이 문서는 전부 "현재 코드가 이렇게 동작한다"이다.** `[제안]`은 하나도 없다 —
> 지금 타워를 건드리는 작업의 기준선이자 리뷰 기준이다.
>
> **구조 재설계 제안은 [TowerRedesign.md](TowerRedesign.md)에 있다** (아직 코드에 없음, 합의 대기).
>
> 관련 문서: [TowerPlacement.md](TowerPlacement.md)(배치) · [TowerMerge.md](TowerMerge.md)(합성) ·
> [GDD.md](../GDD.md) §5.8 · [SystemMap.md](../Review/SystemMap.md) §2
> 관련 이슈: #164(현 구조 확립) · #274(구조 정리)

---

## 0. 설계 요지

- **타워 = 껍데기 + 부품.** `Tower` 클래스는 "이 타워가 무엇을 하는 물건인지" 모른다. 정체성(SO)·
  스탯 원장·레지스트리·페이즈 게이팅만 갖고, 실제 능력은 꽂힌 부품이 갖는다.
- **타워는 단일 구상 타입 하나뿐이다.** 공격/버프 오라/디버프 오라의 차이는 상속이 아니라
  런타임 행동 조립으로 표현한다. 소비처(합성·스킬·보스 BT)는 `Tower` 하나만 알면 된다.
- **스탯 modifier는 단일 원장(`TowerStats`)으로 수렴한다.** 타일 버프·버프 스킬·버프 오라·보스 마력
  봉인이 전부 여기로 들어오고, 합성 규칙은 `Evaluate` 한 곳에만 산다.
- **상태이상(DoT·슬로우·스턴)의 소유자는 타워가 아니라 대상이다.** 타워는 `StatusEffectHandler`에
  적용을 요청만 하고, 지속시간 소진은 대상이 한다 — 타워가 철거돼도 걸린 효과는 남은 시간만큼 흐른다.
- **투사체는 콜라이더 충돌을 쓰지 않는다.** 거리 계산으로 명중을 정하며, "어떻게 날아가는가"와
  "터지면 누구를 때리는가"는 **독립인 두 축**이다(§3.7).

---

## 1. 목적

전투 공간에 설치되어 밤 페이즈에 몬스터를 저지하는 설치물. 낮에 배치([TowerPlacement.md](TowerPlacement.md))
하고 합성([TowerMerge.md](TowerMerge.md))으로 성장시키며, 밤에 자동으로 동작한다(GDD §5.3/§5.8).

이 문서는 **타워 자체**를 다룬다 — 배치 UX와 합성 UX는 각각의 문서에 있고, 여기서는 그 둘이 전제하는
"타워가 무엇이고 어떻게 조립·동작하는가"를 정본으로 둔다.

---

## 2. 범위

### In

- 껍데기(`Tower`) + 행동 부품(`ITowerBehaviour`) 조립 모델(#164)
- 행동 3종: `AttackBehaviour`(투사체) · `BuffAuraBehaviour`(아군 강화) · `DebuffAuraBehaviour`(적 약화)
- 투사체 비행 2종(유도 / 예측 포격)과 명중 3종(단일 / 스플래시 / 체인)
- 스탯 원장 `TowerStats` — 4개 소스 수렴, 소스별 합산 중첩
- 페이즈 게이팅(공격·디버프 = 밤 전용 / 버프 오라 = 상시)
- 명중 시 스턴 1종 (`OnHitStunDuration`, #164 소다 타워)
- 데이터 파이프라인: `TowerTable.csv` → `TowerData` → `TowerAsset`(SO) → 프리팹

### Out — 이 문서 범위 밖

- **구조 재설계 제안 → [TowerRedesign.md](TowerRedesign.md)**
- 배치 그리드·풋프린트·타일 버프 계산 → [TowerPlacement.md](TowerPlacement.md)
- 레시피·매칭·선택 UI·커맨드 트랜잭션 → [TowerMerge.md](TowerMerge.md)
- 타워 배치/합성 연출 → #264 / #265
- 밸런싱 수치 — 현재 SO 인스펙터 수기 authoring(WL-015, §6 #3)
- 타워 철거 — **경로가 아예 없다**(§6 #1)

---

## 3. 현재 구조

### 3.1 껍데기 + 부품

```
Tower (MonoBehaviour, IAttacker, ISelectable)   ← 모든 타워가 이 하나
 ├─ TowerAsset data              정체성
 ├─ TowerStats stats             스탯 modifier 단일 원장
 ├─ static List<Tower> Active    씬의 모든 조립된 타워 + ActiveChanged 이벤트
 ├─ RangeCircle                  선택 시 사거리 원
 └─ List<ITowerBehaviour> behaviours   ← 실제 능력
      ├─ AttackBehaviour
      ├─ BuffAuraBehaviour
      └─ DebuffAuraBehaviour
```

`Tower.Update`([Tower.cs:347](../../Assets/Scripts/CombatSystem/Tower/Tower.cs))가 하는 일 전부:

```csharp
stats.Prune(Time.time);                       // 만료된 버프 정리
bool isNight = ...;
foreach (var behaviour in behaviours) {
    if (behaviour.ActivePhase == NightOnly && !isNight) continue;
    behaviour.Tick(deltaTime);                 // 실제 동작은 부품이
}
```

**리스트인 이유**: 단일 참조면 `if (attack != null)` 분기가 누적돼 이 클래스가 다시 만능 클래스로
돌아간다. 리스트는 **공격 + 오라 하이브리드 타워를 공짜로 허용**하기도 한다(현재 그런 타워는 없음).

### 3.2 왜 이렇게 됐나 (#164)

#164 이전에는 `Tower`(공격 전담)와 `AuraTower`(오라 전담) 두 MonoBehaviour가 따로 있었고, 각자
`TowerType` switch를 갖고 있었다. 그 결과:

| 증상 | 원인 | WL |
|---|---|---|
| 오라가 낮에도 상시 동작 | 페이즈 게이팅이 `Tower`에만 있고 `AuraTower`엔 없었음 | WL-044 |
| 스탯 텍스트가 3곳에 복붙 | `Tower`/`AuraTower`/`TowerTooltipView`가 각자 조립 | WL-079 |
| 버프 계산 원장이 두 벌 | `Tower.activeBuffs`(배율만) ↔ `TowerTileBuff`(Flat+%) | WL-050/081 |

껍데기/부품 분리로 셋이 한 번에 해소됐다.
([TowerRedesign.md](TowerRedesign.md) §5가 이 성과를 그대로 계승한다는 것을 명시한다.)

### 3.3 행동 부품의 생명주기 규약

[ITowerBehaviour.cs:41-44](../../Assets/Scripts/CombatSystem/Tower/ITowerBehaviour.cs)에 명문화돼 있다.
**어기면 초기화 순서 버그가 되돌아온다.**

1. `Awake`/`OnEnable`/`Start`에서 아무것도 하지 않는다 — 초기화는 `Initialize` 한 곳에서만
2. `Update`를 스스로 돌지 않는다 — 호스트가 게이팅 후 `Tick`으로 구동한다
3. `Dispose`는 호스트 비활성화(철거·풀 반환) 시 호출된다 — 외부에 남긴 상태를 여기서 걷어낸다

**유일한 예외**: `BuffAuraBehaviour.OnDestroy`가 `Tower.ActiveChanged` 구독을 해제한다. static
이벤트라 구독을 남긴 채 파괴되면 죽은 대상을 계속 호출한다(SystemMap F7).
⚠ 이 구독은 `Initialize`에서 걸고 `OnDestroy`에서 푸는 **비대칭** 쌍이다.

행동은 런타임 `AddComponent`로 붙으므로 **직렬화 필드를 가질 수 없다.** 그래서 `firePoint`(프리팹
계층 내 Transform)와 `enemyLayerMask`(프리팹별 배선)를 `TowerBuildContext`라는 주입 구조체로 넘긴다.

### 3.4 조립 — `Tower.Build`

[Tower.cs:145](../../Assets/Scripts/CombatSystem/Tower/Tower.cs)가 **타워가 무엇을 하는 물건이 되는지
결정하는 유일한 지점**이다. `TowerPlacer`·합성·테스트 씬이 전부 여기를 통과한다.

```
Build(asset)
 ├─ 이미 같은 SO면 → 재무장(Initialize)만 하고 반환
 ├─ 프리팹이 문 SO ≠ 배치되는 SO면 → 경고 후 배치된 쪽으로 재조립 (WL-129)
 ├─ 이전 행동 Dispose (외부에 남긴 상태 회수)
 ├─ TowerBehaviourFactory.Create(asset) → 행동 AddComponent
 ├─ StripUnusedBehaviourComponents() — 새 SO에서 빠진 행동 컴포넌트 제거
 ├─ ReinitializeBehaviours()
 └─ Register() → Active 등록 + ActiveChanged 발행
```

**`Active` 등록 시점이 `OnEnable`이 아니라 `Build`인 것이 핵심이다** — 배치 프리뷰(고스트)가 타워로
집계되지 않는 근거다(WL-066). "조립되지 않은 타워는 존재하지 않는 타워"가 규칙이다.

`TowerBehaviourFactory`는 스스로를 "`TowerType`/`MagicEffectType` switch가 사는 유일한 곳"이라고
선언하지만 **실제로는 4곳이 더 있다**(§4.3).

### 3.5 행동 3종

| 행동 | 페이즈 | 대상 | 구동 |
|---|---|---|---|
| `AttackBehaviour` | `NightOnly` | 사거리 내 최근접 적 1 | 쿨다운(`AttackInterval`) |
| `BuffAuraBehaviour` | **`Always`** | 반경 내 아군 **공격** 타워 | **폴링 안 함** — `ActiveChanged` 이벤트 |
| `DebuffAuraBehaviour` | `NightOnly` | 반경 내 적 전부 | 주기 재스캔(`Interval`) |

- **`AttackBehaviour`가 하나뿐인 이유**: 단일/스플래시/체인은 별개 행동이 아니라 **명중 방식만**
  다르다. 대상 탐색·쿨다운·발사 경로가 완전히 같아서 `ProjectileImpact`를 전략으로 쓴다(§3.7).
- **버프 오라가 `Always`인 이유**: 배치 즉시 효과가 걸려야 낮 정보 패널에 버프된 스탯이 보인다.
- **버프 오라가 폴링하지 않는 이유**: 타워는 스스로 움직이지 않으므로 대상 집합이 바뀌는 순간은
  "타워가 추가/제거될 때"뿐이고, 그것이 곧 `Tower.ActiveChanged`다.
- **버프 오라가 공격 타워만 대상으로 하는 이유**([BuffAuraBehaviour.cs:116](../../Assets/Scripts/CombatSystem/Tower/BuffAuraBehaviour.cs)):
  효과가 없어서만이 아니다. **오라 반경이 사거리 축을 공유하므로**, 버프 오라끼리 서로를 버프하면
  A가 B의 반경을 넓히고 넓어진 B가 다시 A를 덮는 **순서 의존 피드백 고리**가 생긴다.
  `Has<AttackBehaviour>()` 능력 판정이 그 고리를 구조적으로 끊는다.
- **디버프 오라가 효과를 회수하지 않는 이유**: 대상이 `Duration`을 스스로 소진하는 설계다. 타워가
  철거되면 남은 시간만큼 효과가 흐르다 만료되는 게 의도된 거동이다.

### 3.6 능력 질의 — `Has<T>()` / `Get<T>()`

소비처가 타워의 **구상 타입이 아니라 능력**을 묻게 하는 창구. 현재 소비처 3곳:

| 소비처 | 용도 |
|---|---|
| `BuffAuraBehaviour.CollectTargets` | 버프 대상 필터(위 피드백 고리 차단) |
| `EnemyNodeQuery.IsAttackTower` | 보스 P3 마력 봉인 대상 필터 |
| `Tower.AttackDamage/Range/Interval` | 공격 행동이 없으면 0 — "공격 안 하는 타워"를 값으로 표현 |

### 3.7 투사체 — 비행 축과 명중 축

투사체는 **콜라이더 충돌 판정을 쓰지 않는다.** 물리 트리거 없이 거리 계산만으로 명중을 정한다.
그리고 **서로 독립인 축이 둘** 있으며, **둘 다 타워 SO가 정한다**(#274 Phase 1).

| 축 | 무엇 | 어디서 정하나 |
|---|---|---|
| **비행** `ProjectileFlight` | 얼마나 빠르게·어떤 궤적으로 도달하는가 | `TowerAsset.Attack`의 `ProjectileSpeed`·`Flight`·`ArcHeight` |
| **명중** `ProjectileImpact` | 도달하면 누구를 때리는가 | `TowerAsset`의 `Impact`·`SplashRadius`·`Chain*` |

둘 다 `AttackBehaviour`가 조립 시 1회 만들어(`BuildFlight`/`BuildImpact`) 발사할 때
`Projectile.Init(target, damage, source, flight, impact)`로 넘긴다. struct라 발사마다 복사된다.

**탄환 프리팹에 남는 설정은 `rotationOffset` 하나뿐이다** — 모델 메시의 기수가 어느 축을 보는지 보정하는
값이라(화살 −90, 공 0) 타워가 알 이유가 없다. 즉 역할이 이렇게 갈린다:

```
탄환 프리팹  =  메시 · 트레일 · 파티클 · 모델 축 보정       "어떻게 보이는가"
타워 SO      =  어떻게 날아가서 어떻게 터지는가 + 모든 수치   "무엇을 하는가"
```

`ProjectilePrefab` 필드는 **"어떤 모양으로 보일지"만 고른다.** 그래서 `Rolly_Bullet` 하나를
archer/gatling/Sniper/soda 4개 타워가 공유하면서도 각자 다른 속도·궤적을 가질 수 있다.

#### 비행 2종

| | 판정 | 대상이 도중에 죽으면 |
|---|---|---|
| `Homing` ([:109](../../Assets/Scripts/CombatSystem/Tower/Projectile.cs)) | 매 프레임 추적, `remaining < 0.1f` | **투사체 소멸, 명중 없음** |
| `Ballistic` ([:144](../../Assets/Scripts/CombatSystem/Tower/Projectile.cs)) | 발사 순간 대상 위치를 `landingPos`로 **고정**, 진행도 `t >= 1f` | **고정된 착탄점에 그대로 명중** |

`ArcHeight`는 **비주얼 전용**이다 — 평면 추적 위에 포물선 높이를 얹기만 하고(`:127`, `:152`) 판정에는
들어가지 않는다.

⚠ **현재 SO 9개 전부 `Homing`이다** — `Ballistic` 경로는 지금 아무도 안 쓴다. 캐논의 곡사도
`Ballistic`이 아니라 **`Homing` + `ArcHeight` 15**다(겉보기만 포격, 실제로는 반드시 맞는 유도탄).
다만 **"한 번도 안 쓰인 것"은 아니다** — #274 Phase 1 이전에 `Personal/SUNGSOO/`의 탄환 프리팹
`TB_CanonTower_Lvl2_Ball`(Ballistic, arc 10)과 `SweetLand Prefab/CandyBullet`(Ballistic, arc 30)이
그렇게 저작돼 있었다. 둘 다 **참조 0건 고아**여서 이관 대상에서 빠졌고, 값은 git 히스토리에만 남는다.
`Ballistic`을 지우지 않는 근거가 이것이다.

#### 명중 3종 — `OnHit`([:166](../../Assets/Scripts/CombatSystem/Tower/Projectile.cs))

| | 데미지 대상 | 판정 기준 |
|---|---|---|
| `Single` | 그 대상 하나 | 대상 참조 |
| `Area` | `OverlapSphere(impactPos, SplashRadius)` 내 적 전부 | **위치** — 대상 생사 무관 |
| `Chain` | 최초 대상 → `FindNearestUnhit`로 홉, `dmg *= falloff` | 대상 참조 + 위치 |

#### 왜 `Single`/`Area`/`Chain`이 별개 행동이 아닌가

```
대상 탐색 → 쿨다운 → 투사체 생성 → 비행 → [OnHit]
└────────── 셋이 완전히 동일 ──────────┘      └ 여기 한 스텝만 다름
```

`AttackBehaviour`가 하나뿐인 이유가 이것이다. 타워 클래스로 나눴다면 `FindTarget`·쿨다운·`Instantiate`가
**3벌 복붙**된다. 그래서 `ProjectileImpact`를 전략 값으로 넘긴다.

> **원거리 적도 같은 `Projectile`을 쓴다**(`Enemy.TryRangedAttack`). 다만 `EnemyAsset.RangedFields`엔
> 궤적 저작 필드가 없어 `Homing` + 직선으로 고정되어 있다. 현재 모든 `EnemyAsset`의
> `Ranged.ProjectilePrefab`이 null이라 이 경로 자체가 미사용이다.

---

## 4. 데이터 파이프라인

### 4.1 흐름

```
TowerTable.csv         메타데이터만 (ID·이름키·타입·풋프린트·설명키). 수치 컬럼 없음
      ↓ DataTableManager
TowerData (POCO)       런타임 전용. 에셋에 저장 안 됨
      ↓ (호출부가 채움)
TowerAsset.Data        런타임 캐시 ([HideInInspector])
TowerAsset             ★ 실제 수치가 사는 곳 — 인스펙터 수기 authoring (WL-015)
                         공격 · 비행 · 명중 · 오라가 한 층으로 평탄하게 놓인다(#274 Phase 1)
      ↓ TowerPrefab 참조
프리팹                  Tower 컴포넌트 + firePoint + 콜라이더 + 모델
      ↓ Tower.Build
행동 부품 조립
```

`TableImporter.ImportTower`(에디터 메뉴)가 CSV를 읽어 `Towers/{TowerID}.asset`을 생성/갱신하는데,
**동기화하는 필드는 `TowerID`/`TowerType`/`MagicEffectType` 3개뿐**이다. 수치는 손으로 채운다.

> `TowerAsset`에는 커스텀 에디터가 없다 — 기본 인스펙터가 `[Header]`로 묶인 평탄 필드를 그대로 그린다.
> 구 `TowerAssetEditor`는 `TowerType`으로 필드 그룹을 골라 그리는 것이 존재 이유였는데, 평탄화 후에는
> 오히려 새 필드를 **가려서** #274 Phase 1에서 삭제했다.

### 4.2 `TowerAsset.Data` 채움 규약 — ⚠ 쓰기 4곳에 흩어져 있음

`Data`는 에셋에 저장되지 않는 런타임 캐시라 누군가 채워야 한다. **쓰는 곳은 4곳**이고, 네 곳이 전부
서로 다른 패턴이다:

| 위치 | 패턴 | 실패 시 |
|---|---|---|
| `TowerSelectPanelView.cs:74` | `if (null)` 가드 + `?.Get` | `LogWarning` |
| `TowerMergePanelView.cs:70` | `if (null)` 가드 + `?.Get` | 무처리 |
| `TowerFusionController.cs:79` | `if (null)` 가드 + `?.Get` | 무처리 |
| `Tower.cs:255` (`OnSelected`) | **`??=` + `?.` 없음** | `LogError` + return |

읽기만 하고 채우지 않는 곳(폴백·가드)은 별개다 — 쓰기 지점으로 세면 안 된다:

| 위치 | 하는 일 |
|---|---|
| `TowerTooltipView.cs:261,276` | 읽고 null이면 `TowerID`로 폴백 |
| `TowerPlacer.cs:144` | 읽고 null이면 `LogError` 후 배치 중단 (`:180`의 `GridWidth/Height` 접근을 이 가드가 지킨다) |

> ⚠ **알려진 버그**: `Tower.cs:255`만 `DataTableManager.Get<TowerTable>("TowerTable").Get(...)` 로
> null 조건 연산자가 없다(나머지 3곳은 전부 `?.Get`). `DataTableManager.Get<T>`
> ([DataTableManager.cs:33-43](../../Assets/Scripts/Data/DataTableManager.cs))는 테이블 미등록 시
> `LogError` 후 **null을 반환**하므로, `TowerTable`이 등록되지 않은 씬(테스트 씬 등)에서 타워를 클릭하면
> `NullReferenceException`이 난다 — 바로 다음 줄의 null 가드가 **도달 불가**다. §6 #9.

### 4.3 `TowerType`/`MagicEffectType` 참조가 실제로 있는 곳 — 6개 파일

#274 Phase 1의 스키마 평탄화로 **9개 파일 → 6개**로 줄었다. 없어진 3곳은
`AttackBehaviour.BuildImpact`(→ `ImpactKind` switch로 대체) · `TowerPlacer` 프리뷰 분기(→ `PreviewRadius`) ·
`TowerAssetEditor.cs`(파일째 삭제)다.

| 위치 | 성격 |
|---|---|
| `TowerData.cs:1,9,20,21` | enum 선언 + POCO 프로퍼티 (정본) |
| `TowerAsset.cs:14,15` | SO 필드 — **런타임이 읽는 유일한 것** |
| `TowerBehaviourFactory.cs` (`:28`, `:38-51`, `:56-61`) | `ResolveAttackFields` 삼항 + **switch 2개** |
| `TableImporter.cs:151,152,159,160` | CSV→SO 복사 |
| `TowerTableTest.cs:15` | 로그 |
| `Personal/SUNGSOO/AuraTowerTestDriver.cs:32,33` | 런타임 SO 조립 |

> ⚠ `TableImporter`는 CSV 문자열 → enum 파싱이라 enum 이름을 바꾸면 CSV도 같이 손봐야 한다.
>
> ⚠ **`TowerBehaviourFactory.ResolveAttackFields`(`:28`)의 `TowerType.Magic` 판정은 지우면 안 된다.**
> Unity는 `[Serializable]` 클래스 필드에 null을 허용하지 않아 `asset.Attack`이 오라 타워에서도 non-null이다.
> 그냥 `asset.Attack`을 돌려주면 오라 타워에도 `AttackBehaviour`가 붙어 `Has<AttackBehaviour>()`가 true가
> 되고, ① 보스 P3 마력 봉인이 오라 타워를 노리고 ② 버프 오라끼리 서로를 버프해 §3.5의 피드백 고리가
> 되살아난다. **예외 없이 밸런스만 조용히 뒤집힌다.**

각 위치를 어떻게 처리할지는 [TowerRedesign.md](TowerRedesign.md) §3·§7 참조.

### 4.4 종류 정보가 2중이라는 문제

`TowerType`이 `TowerTable.csv` 컬럼과 `TowerAsset` 필드 양쪽에 있는데, **런타임은 SO만 읽는다.**
CSV의 `TowerType`은 `TableImporter` 입력과 `TowerTableTest` 로그용이다. 둘이 어긋나도 아무 증상이 없다.

---

## 5. 스탯 원장 `TowerStats`

MonoBehaviour가 아닌 순수 C#. `Time.time`을 직접 읽지 않고 `now`를 주입받는다 — 씬 없이 EditMode
테스트로 합성 규칙을 검증할 수 있게 하기 위함(**단 현재 테스트는 0건**, §6 #6).

### 5.1 합성식

```
(기본값 + Σflat) × (1 + Σpercent/100) × (1 + Σ배율보너스)      ← 결과는 0 하한
```

축은 `TowerStat` 3종(AttackDamage / AttackRange / AttackSpeed), 모드는 `TowerModifierMode`
3종(Flat / Percentage / Multiplier). **소스별 합산 중첩, 같은 소스키는 교체(refresh).**

> **0 하한이 필수인 이유**([TowerStats.cs:74-79](../../Assets/Scripts/CombatSystem/Tower/TowerStats.cs)):
> 배율 모드는 보너스를 합산하므로(1.0 → +0, 0.5 → −0.5) 디버프 소스가 겹치면 합이 −1 아래로 내려간다.
> 보스 P3 마력 봉인은 `sourceId`가 에이전트별이라 보스 2기가 각각 `damageMul 0.5`를 걸면 보너스 합이
> −1.0(데미지 0), 3기면 음수다. 하류에 클램프가 없어(`AttackBehaviour` → `Projectile` →
> `Enemy.currentHp -= amount`) **음수 데미지가 그대로 회복이 된다.**

### 5.2 소스키 도메인

| 소스 | 소스키 | 지속 |
|---|---|---|
| 타일 버프 | 고정 `"TowerTileBuff".GetHashCode()` | 지속형 |
| 버프 오라 | `GetInstanceID()` (행동 인스턴스별) | 지속형 |
| 플레이어 버프 스킬 | 고정 문자열 해시 | 시간제 |
| 보스 마력 봉인 | `agent.GetInstanceID() ^ 효과종류해시` | 시간제 |
| 디버프 오라의 상태이상 | `GetInstanceID()` (대상 쪽 핸들러에 저장) | 대상이 소진 |

**인스턴스별 채번이 규약인 이유**: 예전에 디버프 오라가 `TowerID` 해시를 썼는데, 그러면 같은 종류
오라 타워 여러 기가 대상의 한 슬롯을 공유해 서로를 갱신만 했다. 감속 타워를 2기 지어도 배율이
1중첩에 머물러, 보스 P1 돌진의 유일한 파훼 수단이 무력화됐다.

### 5.3 원장 축 커버리지

- 공격 타워: 3축 전부
- 오라 타워: 반경(=AttackRange 축) + DoT 데미지(AttackDamage) + DoT 틱 간격(AttackSpeed)
- **미연결 2건**:
  ① 디버프 오라의 재스캔 주기(`Interval`) — 원장을 안 거친다. 재스캔이 잦아져도 DoT는 이미 대상이
     소유해 피해가 늘지 않기 때문. 독 타워에서 "공속"의 의미를 갖는 축은 `TickInterval`이다.
  ② **슬로우 강도** — `TowerStat`에 "CC 강도" 축이 없다. 공격력·공속에 매핑하면 의미가 어긋나므로,
     순수 감속 타워(`choco_tower`)는 타일 버프에서 **사거리만** 이득을 본다(WL-127, §6 #4).

### 5.4 상태이상은 대상이 소유한다

```
타워                        몬스터
────                        ──────
ApplyOrRefresh / ApplySlow → StatusEffectHandler
                              ├─ effects{}  ← DoT (effectId별 공존)
                              └─ slows{}    ← 감속·스턴
                                   ↓
                              MoveSpeedComposer (소스별 곱산 합성)
```

⚠ **감속 중첩은 곱산인데 타워 버프는 합산이다**(WL-127 비대칭, 미확정).

**스턴에는 가동률 상한이 있다**([StatusEffectHandler.cs:48-58](../../Assets/Scripts/CombatSystem/StatusEffect/StatusEffectHandler.cs)).
스턴 축은 `minMoveSpeed` 하한 클램프를 우회해 완전 정지를 만들므로, 클램프가 막던 소프트락을 핸들러가
대신 막는다 — ① 스턴 중 재적용 무시 ② 종료 후 면역 창(`stunImmunityWindow`). 판정은 **소스가 아니라
대상 기준**이다(소스 기준이면 서로 다른 스턴원 2개가 번갈아 걸어 영구 정지가 만들어진다).
현재 `Projectile.StunEffectId`가 static이라 **모든 소다 타워가 단일 소스를 공유**한다.

> ⚠ **이 static이 상한의 근거는 아니다.** 게이트 `CanStunNow()`([:148](../../Assets/Scripts/CombatSystem/StatusEffect/StatusEffectHandler.cs))는
> `!stunActive && Time.time >= stunImmuneUntil`이고 두 값 모두 **핸들러(대상) 인스턴스 필드**이며,
> 게이트가 `slows` 딕셔너리 조회보다 **앞에**(`:117`) 있다. 따라서 `effectId`가 타워별로 갈려도
> 두 번째 스턴은 같은 자리에서 거부된다 — 코드 주석 `:55`도 "타워를 몇 기 깔든 상한이 있다"고 적고 있다.
> static인 것은 **중복 방지일 뿐**이고, 상한은 전적으로 대상 쪽 두 규칙에서 나온다.
> ([TowerRedesign.md](TowerRedesign.md) §12 #1 참조)

---

## 6. 현재 코드의 열린 문제

재설계와 무관하게 남아 있는 항목들이다. 재설계 관련 미결은
[TowerRedesign.md](TowerRedesign.md) §12에 있다.

| # | 항목 | 상태 |
|---|---|---|
| 1 | **타워 철거 경로가 없다** | `TowerFootprint.OnDestroy`가 철거를 전제하는데 실제 UI/코드가 없다. 현재 유일한 파괴 경로는 합성 소모 |
| 2 | `lightning_tower.asset` 전 필드 0 | `TowerPrefab`/`GhostPrefab`도 둘 다 null이라 배치해도 무동작(WL-001). 수치 기입 필요 |
| 3 | 밸런싱 수치가 CSV 밖 SO에 authoring | WL-015. `TableImporter`가 동기화하는 건 `TowerID`/`TowerType`/`MagicEffectType` 3개뿐 |
| 4 | CC 강도 축 부재 | WL-127. `TowerStat`에 축이 없어 순수 감속 타워가 타일 버프에서 사거리만 이득(§5.3 ②) |
| 5 | `TowerPlacer.keepPlacing` 공유 | WL-105. true면 합성 재료 1회 소모 후 `ExtraCost`만으로 결과 타워 복제 가능 |
| 6 | EditMode 테스트 0건 | `TowerStats`·`TowerFusionMatcher`·`AuraModifiers`가 순수 C#으로 설계됐는데 테스트가 없다 |
| 7 | `Projectile.chainHitSet`이 static | "한 프레임에 하나의 투사체만 명중 처리된다"는 가정 위에 서 있다(`:72-73` 주석). 현재는 `ApplyChain`이 동기적으로 끝나 안전하지만, static 이벤트 `DamageDealt` 구독자(#169 `BurnBuff` 등)가 또 다른 체인 명중을 유발하면 **재진입으로 집합이 덮인다**. 지금은 그런 구독자가 없다 |
| 8 | 투사체가 풀링 없이 매 발사 `Instantiate`/`Destroy` | `Area`/`Chain`의 `Physics.OverlapSphere`도 할당형이다(`:205`, `:245`) — 대상 탐색 쪽은 `NonAlloc` + 고정 버퍼를 쓰는데 투사체만 다르다. 체인은 홉마다 부른다 |
| 9 | `Tower.cs:255` NRE 가능 | `?.`가 빠져 다음 줄 null 가드가 도달 불가(§4.2 주석). **#274 범위 — 재설계와 함께 고친다** |
| 10 | 명중 스턴이 `Single` 경로에만 적용됨 | `BuildImpact`는 타입 무관하게 `StunDuration`을 채우는데 `Projectile.OnHit`은 Single에서만 `ApplyStun`을 부른다. Area/Chain 타워에 저작하면 조용히 무시된다 |
| 11 | `Personal/SUNGSOO/` 고아 프리팹·구버전 SO | `CannonTowerTest.prefab`·`SweetLand Prefab/CandyCanon.prefab`은 참조 0건. `CombatData/debuff_tower.asset`은 지금은 없는 `BuffAura.Interval` 필드가 남은 구버전 스키마 |

---

## 부록 A. 관련 파일

| 역할 | 경로 |
|---|---|
| 코어 | `Assets/Scripts/CombatSystem/Tower/` (Tower · ITowerBehaviour · TowerBehaviourFactory · AttackBehaviour · Buff/DebuffAuraBehaviour · TowerStats · AuraModifiers · TowerStatsFormatter · Projectile · TowerTileBuff · TowerReloadVisual) |
| 데이터 | `Assets/Scripts/Data/Tower/` (TowerAsset · TowerData · TowerTable · TowerRecipe) · `Assets/Resources/DataTables/TowerTable.csv` · `Assets/Resources/ScriptableObjects/Towers/` |
| 배치·합성 | `Assets/Scripts/GameManager/MouseManager/TowerPlacement/` |
| UI | `Assets/Scripts/UI/TowerPanel/` · `Assets/Scripts/GameManager/MouseManager/TowerInfoUI.cs` |
| 에디터 | `Assets/Scripts/Editor/TableImporter.cs` (`TowerAssetEditor.cs`는 #274 Phase 1에서 삭제) |
| 상태이상 | `Assets/Scripts/CombatSystem/StatusEffect/StatusEffectHandler.cs` · `MoveSpeedComposer.cs` |
| 프리팹 | `Assets/Imported/@NorthLand/Prefabs/Tower/` (타워 9 + 고스트 + 탄환) · `Assets/Personal/SUNGSOO/Prefabs/` · `SweetLand Prefab/` (5, WL-065) |

---

## 부록 B. 개정 이력

| 개정 | 내용 |
|---|---|
| 초판 (#274) | §3~§5 현행 기록 + 재설계 제안(상속 트리)을 §6~§11로 동거 |
| 2차 (#274) | 재설계안을 액션 리스트로 전면 개정. §4.2/§4.3 개수를 실측치로 정정(6곳→쓰기 4곳, 5곳→9개 파일 34줄). §5.4의 "static이 스턴 상한의 근거"를 코드 재확인 후 정정 |
| 3차 (#274) | **§3.7 투사체 절 신설** — `FlightMode`가 `Docs/` 전체에 한 번도 없었다. 비행/명중 두 축이 독립인데 소유자가 갈라져 있음을 기록 |
| 5차 (#274 **Phase 1 구현**) | `TowerAsset` 스키마 평탄화 — `Single`/`Area`/`Chain`/`Magic` 래퍼 제거, `Impact`/`SplashRadius`/`Chain*`/`BuffAura`/`DebuffAura`가 최상위로. **비행 축(`Flight`/`ArcHeight`)을 탄환 프리팹 → SO로 이관**하고 `ProjectileFlight` struct 신설(§3.7 재작성). `MagicRadius` → `PreviewRadius`. `TowerAssetEditor.cs` 삭제. §4.3 참조 9개 파일 → 6개 |
| 4차 (#274) | **재설계 제안을 [TowerRedesign.md](TowerRedesign.md)로 분리.** 이 문서가 1031줄까지 불어 Core 문서 중 2위의 2배가 됐고, "현재 명세"와 "제안"이 섞여 읽을 때마다 사실/제안을 판단해야 했다. 이제 이 문서에 `[제안]`은 없다. 기존 §12를 §6(현재 코드의 열린 문제)으로 재편 |
