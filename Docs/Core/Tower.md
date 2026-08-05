# 전투 타워(Tower) — 시스템 명세 `[구현됨]`

> **이 문서는 전부 "현재 코드가 이렇게 동작한다"이다.** `[제안]`은 하나도 없다 —
> 지금 타워를 건드리는 작업의 기준선이자 리뷰 기준이다.
>
> **새 타워를 추가하는 절차는 [TowerAddGuide.md](TowerAddGuide.md)에 있다** — 이 문서는 "왜/무엇",
> 그쪽은 "어떻게"다. 구조 재설계 제안 [TowerRedesign.md](TowerRedesign.md)(#274)는 Phase 1~5가 전부
> 구현·병합돼 폐기 예정이며, 그 §11(새 타워 추가법)은 `TowerAddGuide.md`로 이관됐다.
>
> 관련 문서: [TowerPlacement.md](TowerPlacement.md)(배치) · [TowerMerge.md](TowerMerge.md)(합성) ·
> [GDD.md](../GDD.md) §5.8 · [SystemMap.md](../Review/SystemMap.md) §2
> 관련 이슈: #164(현 구조 확립) · #274(구조 정리)

---

## 0. 설계 요지

- **타워 = 껍데기 + 액션.** `Tower` 클래스는 "이 타워가 무엇을 하는 물건인지" 모른다. 정체성(SO)·
  스탯 원장·레지스트리·페이즈 게이팅만 갖고, 실제 능력은 담긴 액션이 갖는다.
- **타워 종류의 정본은 프리팹의 `Actions` 리스트다.** 공격/버프 오라/디버프 오라의 차이는 상속이 아니라
  인스펙터에서 담는 액션 조합으로 표현한다. 소비처(합성·스킬·보스 BT)는 `Tower` 하나만 알면 된다.
- **새 타워를 추가할 때 코드를 건드리지 않는다.** 프리팹에 액션을 담고 SO에 수치를 적으면 끝이다.
  실제 절차(CSV·SO·프리팹·씬 등록·검증)는 [TowerAddGuide.md](TowerAddGuide.md).
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

- 껍데기(`Tower`) + 액션(`TowerAction`) 조립 모델(#164 → #274)
- 액션 4종: `AttackAction`(투사체) · `HitscanAttackAction`(빔, #252) · `BuffAuraAction`(아군 강화) ·
  `DebuffAuraAction`(적 약화). 앞의 둘은 능력 계약 `IAttackAction`을 공유한다
- 전달 방식 2종(투사체 / 히트스캔) — **명중 방식과 다른 축이다**(§3.7.1)
- 투사체 비행 2종(유도 / 예측 포격) — `ProjectileFlight` 부품. 새 방식 = 파생 1개(§3.7)
- 투사체 명중 2종(단일 / 스플래시). 체인은 히트스캔이 배달한다
- 스탯 원장 `TowerStats` — 4개 소스 수렴, 소스별 합산 중첩
- 페이즈 게이팅(공격·디버프 = 밤 전용 / 버프 오라 = 상시)
- 명중 효과 4종(화상·독·감속·스턴) — `HitEffect` 부품. **공격 액션과 디버프 오라가 같은 리스트를 공유**
- 합성 효과 계승 — 재료의 효과 **종류**만 물려받고 수치는 결과 SO가 적는다(§3.9)
- 데이터 파이프라인: `TowerTable.csv` → `TowerData` → `TowerAsset`(SO) → 프리팹
- 저작 검증 `TowerAsset.OnValidate` — 프리팹 `Actions`와 SO 수치의 불일치를 저장 시점에 경고(§4.3)

### Out — 이 문서 범위 밖

- **구조 재설계 제안 → [TowerRedesign.md](TowerRedesign.md)**
- 배치 그리드·풋프린트·타일 버프 계산 → [TowerPlacement.md](TowerPlacement.md)
- 레시피·매칭·선택 UI·커맨드 트랜잭션 → [TowerMerge.md](TowerMerge.md)
- 타워 배치/합성 연출 → #264 / #265
- 밸런싱 수치 — 현재 SO 인스펙터 수기 authoring(WL-015, §6 #3)
- 타워 철거 — **경로가 아예 없다**(§6 #1)

---

## 3. 현재 구조

### 3.1 껍데기 + 액션 — 프리팹이 정본

```
[프리팹] ArcherTower
 ▼ Tower (MonoBehaviour, IAttacker, ISelectable)   ← 모든 타워가 이 하나
     data            archer_tower    정체성(SO)
     firePoint       Muzzle          배선 — 액션이 Owner를 통해 읽는다
     enemyLayerMask  Enemy           배선
     Actions                         ★ 이 타워가 하는 일 — [SerializeReference]
       ▼ [0] Attack Action
       ▼ [1] Buff Aura Action        ← 하이브리드는 항목 추가로 끝

 Tower가 내부에 갖는 것
     TowerStats stats                스탯 modifier 단일 원장
     static List<Tower> Active       씬의 모든 조립된 타워 + ActiveChanged 이벤트
     RangeCircle                     선택 시 사거리 원
```

**액션은 MonoBehaviour가 아니다** — 프리팹의 `Tower.Actions`에 직렬화된 순수 C# 객체이고,
인스펙터에서 `+ Attack Action` / `+ Buff Aura Action`을 골라 담는다(#274). 그래서 **프리팹만 보면 이
타워가 무엇을 하는지 알 수 있고, 새 타워를 추가할 때 코드를 건드릴 일이 없다.**

`Tower.Update`([Tower.cs](../../Assets/Scripts/CombatSystem/Tower/Tower.cs))가 하는 일 전부:

```csharp
stats.Prune(Time.time);                       // 만료된 버프 정리
bool isNight = ...;
foreach (var action in actions) {
    if (action == null) continue;             // [SerializeReference] rename 시 null 항목이 생긴다
    if (action.ActivePhase == NightOnly && !isNight) continue;
    action.Tick(deltaTime);                   // 실제 동작은 액션이
}
```

**리스트인 이유**: 단일 참조면 `if (attack != null)` 분기가 누적돼 이 클래스가 다시 만능 클래스로
돌아간다. 리스트는 **공격 + 오라 하이브리드 타워를 공짜로 허용**하기도 한다(현재 그런 타워는 없음).

**액션이 지켜야 할 규칙 4가지**는 [TowerAction.cs](../../Assets/Scripts/CombatSystem/Tower/TowerAction.cs)
상단에 명문화돼 있다 — ① 수치를 갖지 않는다(전부 SO) ② 배선은 `Owner`를 통해 읽는다
③ 런타임 상태는 직렬화하지 않는다 ④ 소스 키는 `SourceId`(= `Owner.GetInstanceID() ^ 타입해시`)를 쓴다.

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

### 3.3 액션의 생명주기 규약

[TowerAction.cs](../../Assets/Scripts/CombatSystem/Tower/TowerAction.cs)에 명문화돼 있다.
**어기면 초기화 순서 버그가 되돌아온다.**

1. 초기화는 `OnInitialize` 한 곳에서만 — 액션은 MonoBehaviour가 아니라 애초에 `Awake`/`Start`가 없다
2. `Update`를 스스로 돌지 않는다 — 호스트가 게이팅 후 `Tick`으로 구동한다
3. `Dispose`는 호스트 비활성화(철거·풀 반환) 시 호출된다 — 외부에 남긴 상태를 여기서 걷어낸다

**예외가 없다.** 예전 `BuffAuraBehaviour`는 `Tower.ActiveChanged`(static 이벤트) 구독을 `Initialize`에서
걸고 `OnDestroy`에서 푸는 **비대칭** 쌍이었고, 그것이 규약의 유일한 구멍이었다(SystemMap F7).
액션은 MonoBehaviour가 아니라 `OnDestroy`가 없으므로 구독을 `Initialize`↔`Dispose` **대칭 쌍**으로 둔다 —
`Tower.OnDisable`이 `Dispose`를 부르고 Unity가 파괴 전에 `OnDisable`을 먼저 호출하므로 누수 경로가 닫힌다.

액션은 프리팹에 직렬화되므로 **직렬화 필드를 가질 수 있다.** 다만 규칙 ①에 따라 수치는 두지 않고,
`firePoint`·`enemyLayerMask` 같은 씬 배선은 규칙 ②에 따라 `Owner`(= `Tower.FirePoint`/`EnemyLayerMask`)를
통해 읽는다. 그래서 예전의 `TowerBuildContext` 주입 구조체가 사라졌다.

> ⚠ **런타임 상태는 반드시 `[NonSerialized]`**여야 한다(규칙 ③). `[SerializeReference]` 객체는
> `Instantiate` 시 인스턴스마다 깊은 복사되므로, 직렬화만 안 하면 쿨다운·버퍼가 자동으로 타워별 독립이 된다.

### 3.4 조립 — `Tower.Build`

[Tower.cs:145](../../Assets/Scripts/CombatSystem/Tower/Tower.cs)가 **타워가 무엇을 하는 물건이 되는지
결정하는 유일한 지점**이다. `TowerPlacer`·합성·테스트 씬이 전부 여기를 통과한다.

```
Build(asset)
 ├─ 이미 같은 SO면 → 재무장(Initialize)만 하고 반환
 ├─ 프리팹이 문 SO ≠ 배치되는 SO면 → 경고 후 배치된 쪽으로 재조립 (WL-129)
 ├─ 이전 액션 Dispose (외부에 남긴 상태 회수)
 ├─ data = asset
 ├─ ReinitializeActions() → 각 액션에 Initialize(this, data)
 └─ Register() → Active 등록 + ActiveChanged 발행
```

**액션을 만들지 않고 이미 있는 것을 초기화만 한다**(#274). 예전에는 여기서 `TowerBehaviourFactory`가
SO의 `TowerType`을 보고 `AddComponent`로 조립하고, 새 SO에서 빠진 컴포넌트를 다시 떼어냈다
(`StripUnusedBehaviourComponents`). **프리팹이 정본이면 "SO에서 빠진 행동"이라는 개념 자체가 없다.**

**`Active` 등록 시점이 `OnEnable`이 아니라 `Build`인 것이 핵심이다** — 배치 프리뷰(고스트)가 타워로
집계되지 않는 근거다(WL-066). "조립되지 않은 타워는 존재하지 않는 타워"가 규칙이다.

### 3.5 액션 3종

| 액션 | 페이즈 | 대상 | 구동 |
|---|---|---|---|
| `AttackAction` | `NightOnly` | 사거리 내 최근접 적 1 | 쿨다운(`AttackInterval`) |
| `HitscanAttackAction` | `NightOnly` | 사거리 내 최근접 적 1 → 홉 | 쿨다운(`AttackInterval`) |
| `BuffAuraAction` | **`Always`** | 반경 내 아군 **공격** 타워 | **폴링 안 함** — `ActiveChanged` → 더티 플래그 |
| `DebuffAuraAction` | `NightOnly` | 반경 내 적 전부 | 주기 재스캔(`Interval`) |

- **`AttackAction`이 명중 방식별로 갈리지 않는 이유**: 단일/스플래시는 별개 액션이 아니라 **명중 방식만**
  다르다. 대상 탐색·쿨다운·발사 경로가 완전히 같아서 `ProjectileImpact`를 전략으로 쓴다(§3.7).
- **그런데 체인은 별개 액션인 이유**: 체인이 다른 것은 명중 방식이 아니라 **전달 방식**이다(§3.7.1).
  투사체를 아예 만들지 않으므로 `ProjectileImpact` 전략으로 표현할 수 없다.
- **두 공격 액션이 공통 기반 클래스를 갖지 않는 이유**: BuffAura/DebuffAura가 독립 sealed인 것과 같은
  관례다 — 공유할 **규칙**은 `ChainResolver` 같은 곳으로 빼고 골격은 각자 소유한다. 상속을 끼우면
  액션 하나를 읽을 때 부모까지 따라가야 한다. 공유하는 것은 능력 계약 `IAttackAction`뿐이다.
  ⚠ 그 대가로 `FindTarget`이 두 곳에 복제돼 있다(WL-151).
- **버프 오라가 더티 플래그를 쓰는 이유**: `ActiveChanged`는 **플래그만 세우고** 실제 재계산은 `Tick`에서
  한다. ① 예전에는 `Reapply`가 이벤트 핸들러 안에서 직접 돌아 재진입 위험이 있었고 ② 합성으로 재료
  3개가 동시에 빠지면 이벤트가 3번 발화해 `Reapply`도 3번 돌았다(드래그 선택도 마찬가지). 지금은
  프레임당 1회로 접힌다. 단 **배치 직후만은 `Initialize`에서 즉시 1회 적용**한다 — 낮 정보 패널이
  그 프레임에 버프된 스탯을 보여야 하기 때문.
- **버프 오라가 `Always`인 이유**: 배치 즉시 효과가 걸려야 낮 정보 패널에 버프된 스탯이 보인다.
- **버프 오라가 폴링하지 않는 이유**: 타워는 스스로 움직이지 않으므로 대상 집합이 바뀌는 순간은
  "타워가 추가/제거될 때"뿐이고, 그것이 곧 `Tower.ActiveChanged`다.
- **버프 오라가 공격 타워만 대상으로 하는 이유**([BuffAuraAction.cs](../../Assets/Scripts/CombatSystem/Tower/BuffAuraAction.cs)):
  효과가 없어서만이 아니다. **오라 반경이 사거리 축을 공유하므로**, 버프 오라끼리 서로를 버프하면
  A가 B의 반경을 넓히고 넓어진 B가 다시 A를 덮는 **순서 의존 피드백 고리**가 생긴다.
  `Has<IAttackAction>()` 능력 판정이 그 고리를 구조적으로 끊는다.
- **디버프 오라가 효과를 회수하지 않는 이유**: 대상이 `Duration`을 스스로 소진하는 설계다. 타워가
  철거되면 남은 시간만큼 효과가 흐르다 만료되는 게 의도된 거동이다.
- **디버프 오라가 무엇을 거는지는 `TowerAsset.Effects`가 정한다**(§3.8) — 공격 액션과 같은 리스트다.
  `DebuffAuraFields`에 남은 것은 `Radius`와 재스캔 주기 `Interval`뿐이고, 예전에 여기 박혀 있던
  `Duration`/`Modifiers`/`Damage`는 효과 부품으로 옮겨갔다. 그래서 **화상 장판 타워가 새 코드 없이 생긴다.**

### 3.6 능력 질의 — `Has<T>()` / `Get<T>()`

소비처가 타워의 **구상 타입이 아니라 능력**을 묻게 하는 창구(`where T : class`).

⚠ **능력을 물을 때는 구상 액션이 아니라 능력 인터페이스(`IAttackAction`)를 쓴다**(#252).
구상 액션으로 물으면 전달 방식을 하나 늘릴 때마다 판정하는 모든 곳이 조용히 그 타워를 제외한다 —
컴파일 에러가 아니라 "버프가 안 걸리는 타워"·"공격 스탯이 안 뜨는 툴팁"으로 나타나 발견이 늦다.
제약이 `TowerAction`이 아니라 `class`인 이유가 이것이다(`IAttackAction`은 `TowerAction`을 상속하지 않는다 —
구현체가 이미 `TowerAction` 파생이라 클래스를 하나 더 끼울 수 없다).

**"이 타워는 공격하는가"** → `Has<IAttackAction>()`. 현재 소비처 4곳:

| 소비처 | 용도 |
|---|---|
| `BuffAuraAction.CollectTargets` | 버프 대상 필터(위 피드백 고리 차단) |
| `EnemyNodeQuery.IsAttackTower` | 보스 P3 마력 봉인 대상 필터 |
| `Tower.AttackDamage/Range/Interval`·`TryAttack` | 공격 액션이 없으면 0/false — "공격 안 하는 타워"를 값으로 표현 |
| `TowerTooltipView.BuildStats`(배치 전, `TowerAsset.HasAction<T>()` 경유) | 공격 스탯 줄 vs 오라 반경 줄 |

**"이 타워는 투사체를 쏘는가"** → `Has<AttackAction>()`. 전달 방식을 **일부러** 구분하는 판정이며
현재 사례는 `BurnBuff`(#169) 하나다 — 근거는 §3.7.1. 이 방향으로 쓸 때는 **왜 구상 액션인지 주석을
남길 것**(그러지 않으면 다음 사람이 "인터페이스로 고쳐야 하는 버그"로 읽는다).

배치 **전** 경로(툴팁·저작 검증)는 인스턴스가 없어 `TowerAsset.HasAction<T>()`를 쓴다 —
프리팹의 직렬화된 액션 리스트를 그대로 들여다보므로 초기화 없이 답한다.

### 3.7 투사체 — 비행 축과 명중 축

투사체는 **콜라이더 충돌 판정을 쓰지 않는다.** 물리 트리거 없이 거리 계산만으로 명중을 정한다.
그리고 **서로 독립인 축이 둘** 있으며, **둘 다 타워 SO가 정한다**(#274 Phase 1).

| 축 | 무엇 | 어디서 정하나 | 형태 |
|---|---|---|---|
| **비행** `ProjectileFlight` | 얼마나 빠르게·어떤 궤적으로 도달하는가 | `TowerAsset.Attack.Flight` **부품 하나** | `[SerializeReference]` 클래스 |
| **명중** `ProjectileImpact` | 도달하면 누구를 때리는가 | `TowerAsset`의 `Impact`·`SplashRadius` | struct |

**두 축의 형태가 다르다.** 명중은 `AttackAction`이 조립 시 1회 만드는 struct라 발사마다 복사되지만,
비행은 **SO에 담긴 부품을 그대로 참조**한다 — 액션은 만들지 않고 `asset.Attack.Flight`를 넘길 뿐이다
(구 `BuildFlight`는 #274 Phase 4.5에서 사라졌다). 발사 시 `Projectile.Init(target, damage, source, flight, impact, effects)`.

> ⚠ **비행 부품이 공유된다는 것이 설계의 전제다.** 한 타워가 쏜 투사체 10발이 같은 부품 객체를
> 참조하므로 **부품은 상태를 가지면 안 된다** — 진행값은 전부 `FlightState`에 담아 `Projectile`이
> 소유하고 `ref`로 넘긴다. 액션(`TowerAction`)과 **정반대**인 지점이다: 액션은 프리팹에 담겨
> `Instantiate` 시 인스턴스마다 깊은 복사되므로 상태를 가져도 안전했다.

**탄환 프리팹에 남는 설정은 `rotationOffset` 하나뿐이다** — 모델 메시의 기수가 어느 축을 보는지 보정하는
값이라(화살 −90, 공 0) 타워가 알 이유가 없다. 즉 역할이 이렇게 갈린다:

```
탄환 프리팹  =  메시 · 트레일 · 파티클 · 모델 축 보정       "어떻게 보이는가"
타워 SO      =  어떻게 날아가서 어떻게 터지는가 + 모든 수치   "무엇을 하는가"
```

`ProjectilePrefab` 필드는 **"어떤 모양으로 보일지"만 고른다.** 그래서 `Rolly_Bullet` 하나를
archer/gatling/Sniper/soda 4개 타워가 공유하면서도 각자 다른 속도·궤적을 가질 수 있다.

#### 수명 계약 — `Impact`와 `Finished`는 독립이다

부품이 매 프레임 돌려주는 `FlightStep`이 **두 플래그를 따로** 갖는다. 이 분리가 이 축의 핵심이다:

```csharp
void Update() {                                  // 분기가 없다
    FlightStep step = flight.Step(this, target, ref flightState, Time.deltaTime);
    // 기수 회전은 호스트가 한 곳에서 — 부품은 위치만 정한다
    if (step.Impact)   OnHit(step.ImpactPos);
    if (step.Finished) Destroy(gameObject);
}
```

| | 표현 |
|---|---|
| 유도탄·포격 | 도달 시 `Impact` + `Finished` |
| 대상 소실(유도탄) | `Finished`만 — 명중 없이 사라진다 |
| **관통탄** | 적을 지날 때마다 `Impact`, 사거리 끝에 `Finished` |
| **부메랑** | 왕복하며 여러 번 `Impact`, 복귀 완료 시 `Finished` |

예전에는 **"도달 = 명중 = 소멸"이 한 덩어리**로 비행 코드 양쪽에 하드코딩돼 있어, 아래 두 줄을
표현할 방법이 아예 없었다. **새 비행 방식 = `ProjectileFlight` 파생 클래스 1개이고 `Projectile.cs`는
한 글자도 안 바뀐다**(#274 Phase 4.5).

#### 비행 2종 — [ProjectileFlight.cs](../../Assets/Scripts/CombatSystem/Tower/ProjectileFlight.cs)

| | 판정 | 대상이 도중에 죽으면 |
|---|---|---|
| `HomingFlight` | 매 프레임 추적, `remaining < 0.1f` | **투사체 소멸, 명중 없음** |
| `BallisticFlight` | 발사 순간 대상 위치를 `Landing`으로 **고정**, 진행도 `t >= 1f` | **고정된 착탄점에 그대로 명중** |

`ArcHeight`는 **비주얼 전용**이다 — 평면 추적 위에 포물선 높이를 얹기만 하고 판정에는 들어가지 않는다.

> **인스펙터에서 고르는 법**: `TowerAsset > Attack > Flight` 줄 오른쪽의 타입 드롭다운.
> Unity는 `List`로 감싼 managed reference에만 `+` 피커를 주고 **단일 필드에는 안 주므로**,
> `Editor/ManagedReferencePickerDrawer.cs`가 그 자리를 메운다(타입을 바꿔도 같은 이름의 수치는 이어받는다).

⚠ **현재 SO 9개 전부 `Homing`이다** — `Ballistic`을 저작한 타워는 아직 없다. 캐논의 곡사도
`Ballistic`이 아니라 **`Homing` + `ArcHeight` 15**다(겉보기만 포격, 실제로는 반드시 맞는 유도탄).
다만 **경로 자체는 #274 Phase 4.5에서 실기 검증됐다** — 고정 착탄점 명중과 피해량을 확인했다.
`Ballistic`이 의미를 갖는 것은 **광역과 짝지을 때**다: 빗나가도 스플래시가 주변을 때리므로
"적 무리의 길목을 예측해 쏜다"가 성립한다.
다만 **"한 번도 안 쓰인 것"은 아니다** — #274 Phase 1 이전에 `Personal/SUNGSOO/`의 탄환 프리팹
`TB_CanonTower_Lvl2_Ball`(Ballistic, arc 10)과 `SweetLand Prefab/CandyBullet`(Ballistic, arc 30)이
그렇게 저작돼 있었다. 둘 다 **참조 0건 고아**여서 이관 대상에서 빠졌고, 값은 git 히스토리에만 남는다.
`Ballistic`을 지우지 않는 근거가 이것이다.

#### 명중 2종 — `OnHit`([Projectile.cs](../../Assets/Scripts/CombatSystem/Tower/Projectile.cs))

| | 데미지 대상 | 판정 기준 |
|---|---|---|
| `Single` | 그 대상 하나 | 대상 참조 |
| `Area` | `OverlapSphere(impactPos, SplashRadius)` 내 적 전부 | **위치** — 대상 생사 무관 |

두 경로는 전부 `Projectile.Hit(victim, amount)` 한 곳을 지난다 — **데미지 + 통지 + 효과**가 거기 모여 있다.
예전에는 스턴이 `Single` 경로에서만 적용돼 Area 타워에 저작해도 예외 없이 조용히 무시됐다
(#274 Phase 4에서 구조적으로 해소).

⚠ **체인은 이 표에 없다** — 투사체가 배달하지 않으므로 `Hit`을 지나지 않고, 따라서 **명중 효과도 걸지 않는다**.
그 예외의 근거와 저작 시점 경고는 §3.7.1.

#### 왜 `Single`/`Area`가 별개 행동이 아닌가

```
대상 탐색 → 쿨다운 → 투사체 생성 → 비행 → [OnHit]
└────────── 둘이 완전히 동일 ──────────┘      └ 여기 한 스텝만 다름
```

`AttackAction`이 명중 방식으로 갈리지 않는 이유가 이것이다. 액션으로 나눴다면 `FindTarget`·쿨다운·
`Instantiate`가 복붙된다. 그래서 `ProjectileImpact`를 전략 값으로 넘긴다.

#### §3.7.1 전달 방식 — 투사체 vs 히트스캔 (#252)

**명중 방식과 다른 축이다.** 명중은 "도달하면 누구를 때리는가"이고, 전달은 "애초에 어떻게 도달하는가"다.

| | 전달 | 명중 해결 | 액션 |
|---|---|---|---|
| 투사체 | `Projectile` 인스턴스가 비행 | 착탄 프레임에 `OnHit` | `AttackAction` |
| 히트스캔 | 없음 — 발사 프레임에 즉시 판정 | `ChainResolver.Resolve` | `HitscanAttackAction` |

체인이 히트스캔인 이유는 연출이 아니라 **구조적 결함 제거**다. 투사체 체인에는 "비행 중 첫 대상이 죽는"
창이 있었고, 그 창에 걸리면 `Homing`은 조기 파괴되고 `Ballistic`은 착탄 시 생존 검사에 걸려
**홉 전체가 사라졌다**. 발사와 명중이 같은 프레임에 끝나면 그 창이 존재하지 않는다.

**체인 여부의 정본은 `Impact`가 아니라 프리팹의 `HitscanAttackAction` 유무다** — #274의
"종류의 정본은 프리팹"과 같은 축이다. `Impact`는 히트스캔에서 읽히지 않는다.

명중 규칙은 `ChainResolver`가 소유한다. ⚠ **static이 아니라 타워별 인스턴스**여야 한다 — 순회 중
`Projectile.DamageDealt` 구독자가 같은 콜스택에서 다른 타워를 발사시킬 수 있고, 상태가 static이면
그 중첩 호출이 진행 중인 순회의 `hitSet`을 비워 이미 때린 적을 다시 고른다(에러 없이 데미지 분포만
틀어져 재현이 어렵다). 같은 이유로 **명중 통지는 순회가 끝난 뒤 일괄 발행**한다.

##### 체인이 명중 효과를 걸지 않는 이유 (#252 확정)

① **테마**: 번개(전격)라 발화·기절이 성립하지 않는다.
② **수치**: 1발에 최대 `MaxChainTargets`명을 때리므로, 그대로 걸면 CC/DoT 처리량이 "1발 = 1명" 전제로
잡은 튜닝을 무너뜨린다.

지원하려면 `ChainResolver.Resolve`에 효과 축을 더해 홉마다 부여하는 형태여야 하고 **기획 결정이 선행**한다.

이것은 #274 Phase 4가 닫은 "조용히 무시되는 저작" 표면에 구멍을 하나 남기는 것이므로,
`TowerAsset.OnValidate`가 저작 시점에 경고한다(§4.3) — 결정을 **조용하지 않게** 유지하는 것이 조건이다.
화상 버프(#169)도 같은 근거로 `Has<AttackAction>()`(구상 액션)으로 체인을 거른다 — 그쪽은
**의도적으로** 전달 방식을 구분하는 유일한 판정이다(§3.2의 능력 질의 규칙 참조).

> **원거리 적도 같은 `Projectile`을 쓴다**(`Enemy.TryRangedAttack`). 다만 `EnemyAsset.RangedFields`엔
> 궤적 저작 필드가 없어 `Homing` + 직선으로 고정되어 있다. 현재 모든 `EnemyAsset`의
> `Ranged.ProjectilePrefab`이 null이라 이 경로 자체가 미사용이다.


### 3.8 명중 효과 — `HitEffect`

**액션과 같은 패턴을 한 층 아래에 적용한 것이다:**

```
타워          →  [액션 목록]      "이 타워는 무엇을 하는가"        §3.1
액션          →  [효과 목록]      "맞으면 무슨 일이 나는가"        §3.8
```

`TowerAsset.Effects`에 `[SerializeReference]`로 담긴다 — 인스펙터에서 `+ Burn`, `+ Slow`를 골라 붙이고
**그 자리에서 수치를 넣는다.** 부품과 수치가 한 덩어리라 저작이 한 번에 끝난다.

| 효과 | 수치 | 거는 곳 |
|---|---|---|
| `BurnEffect` · `PoisonEffect` | `DamagePerTick` · `TickInterval` · `Duration` | `StatusEffectHandler.ApplyOrRefresh` |
| `SlowEffect` | `Multiplier`(0.6 = 40% 감속) · `Duration` | `ApplySlow` |
| `StunEffect` | `Duration` | `ApplySlow(배율 0)` — 스턴 축으로 간다 |

**신규 런타임 인프라가 0이다.** 전부 기존 `StatusEffectHandler`로 흘러가고, 효과 소유·지속시간 소진·
소스별 공존은 이미 대상 쪽에 구현돼 있다(§5.4). 지금까지 부족했던 건 "어떤 효과를 걸지"가 데이터가
아니었다는 것뿐이다.

**공격 액션과 디버프 오라가 같은 리스트를 공유한다** — "맞으면 화상"과 "장판에 화상"은 거는 방식만 다르고
효과 자체는 같기 때문이다. 그래서 **화상 장판 타워가 새 코드 없이** 만들어진다.

#### 소스 키

```
sourceId = 쏜 쪽의 GetInstanceID() ^ (int)EffectKind
```

같은 종류 타워 여러 기는 인스턴스 ID가 달라 **자동 중첩**되고, 한 타워 안에서는 종류당 하나로 수렴한다.
`EnemyApplyTowerDebuffAction`의 `agentID ^ 효과종류해시` 관례와 동형이다.

⚠ **`Effects` 리스트의 순서를 섞거나 항목을 지우지 말 것** — 키가 `Kind`로 채번되므로 종류가 바뀌면
진행 중이던 효과가 대상 쪽에 **회수되지 않는 유령**으로 남는다.

⚠ **스턴만 예외로 공유 static ID를 쓴다.** #274 이전부터 모든 소다 타워가 단일 슬롯(`"onhit.stun"`)을
공유해왔고, 인스턴스별로 바꾸면 밸런스가 미세하게 달라진다. 가동률 상한 자체는 **대상 기준** 게이트에서
나오므로(§5.4) 바꿔도 영구 스턴은 안 나지만, 이 리팩터링에서는 현행 동작을 보존했다.

#### DoT는 원장을 탄다

`BurnEffect`/`PoisonEffect`의 피해와 틱 간격은 소스 타워의 `TowerStats`를 거쳐 합성된다 — 타일 버프의
공격력·공속이 장판 피해에도 반영된다. 감속 강도는 거치지 않는다(`TowerStat`에 CC 축이 없다, WL-127).

> ⚠ `BurnEffect`는 이름이 **두 곳에** 있다 — 여기 `NorthLand.Combat.BurnEffect`(`HitEffect` 파생)와
> `Skill/BurnEffect.cs`의 전역 `BurnEffect`(`SkillEffect` 파생, #169). `using NorthLand.Combat`을 쓴
> 파일에서 `BurnEffect`라고 쓰면 **전역(스킬) 쪽이 이긴다.** 컴파일 에러로 드러나지만 헷갈리는 자리라,
> 이 타입이 필요하면 정식명으로 쓸 것.

### 3.9 합성 효과 계승

**합성하면 재료가 갖고 있던 효과의 "종류"를 결과 타워가 물려받는다**(#274 Phase 5).

```
소다(스턴) + 초코(감속)  →  결과 타워가 스턴 + 감속을 둘 다 갖는다
```

**수치는 계승하지 않는다.** 결과 SO의 `Effects`에 미리 적힌 수치 중 **재료가 가져온 종류만 켜진다.**
수치까지 물려받으면 "같은 효과가 겹칠 때 max냐 합산이냐"를 정해야 하고, 합산은 같은 타워를 계속
합성할 때 무한 스택이 된다 — **종류만 계승하면 그 질문 자체가 사라지고**, 결과 타워의 밸런싱을
손저작으로 완전히 제어할 수 있다.

| 조각 | 역할 |
|---|---|
| `TowerRecipe.InheritEffects` | 계승 여부는 **레시피가** 정한다(기본 false) |
| `Tower.ActiveEffectKinds` / `ActivateEffects` | 활성 종류를 **인스턴스가** 소유한다(아래 ★) |
| `TowerFusionMatcher.ResolveInheritedKinds` | 재료의 활성 종류 합집합 ∩ 결과 SO 정의 종류. **툴팁과 실행부의 단일 출처** |
| `TowerRecipe.OnValidate` | 재료가 내는 효과가 결과 SO에 없으면 경고(없으면 조용히 사라진다) |

#### ★ 활성 종류를 SO가 아니라 인스턴스가 갖는 이유

**① SO 오염.** `TowerAsset`은 씬의 모든 인스턴스가 공유한다. 런타임에 `Effects`를 건드리면 다음 합성이
이전 계승분을 물려받고, `[SerializeReference]`라 **진짜로 직렬화되어 `.asset` 파일에 영구히 남는다.**

**② 다단 합성.** 합성 결과도 다시 재료가 된다(`A+B→C`, `C+D→E`). "재료 C가 무엇을 갖는가"를 C의
**SO**에서 읽으면 꺼져 있는 효과까지 잡힌다 — C 인스턴스의 활성 상태를 읽어야 맞다.

#### 적용은 pull이다

`Tower.Build`가 배치 확정 콜백보다 **먼저** 돌아서, 액션이 초기화되는 시점엔 아직 계승분이 정해지지
않았다. 그래서 효과 목록을 미리 거르지 않고 **거는 순간** `Owner.IsEffectActive(kind)`로 묻는다
(오라 `Radius`가 접근할 때마다 원장을 평가하는 것과 같은 패턴 — 초기화 순서 의존이 생기지 않는다).

거는 지점 두 곳(`Projectile.ApplyEffects` · `DebuffAuraAction.ApplyDebuff`)과 **표시**(`DescribeEffects`)가
**같은 술어를 공유한다.** 표시부가 자기 규칙을 쓰면 "정보 패널엔 독이 있다는데 실제로는 안 걸리는"
어긋남이 생긴다(WL-079/WL-130과 같은 축).

> ⚠ **현재 `InheritEffects`를 켠 레시피는 0개다** — 배관만 완성돼 있고 게임 동작은 그대로다.
> 기존 타워 SO를 결과로 쓰면 **필터가 없는 평범한 배치에서도** 그 효과가 켜져 프로덕션 타워가
> 조용히 강화되므로, 결과 SO는 합성으로만 나오는 것이어야 한다. 족보 확정은 기획 몫이다.

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
**동기화하는 필드는 `TowerID` 하나뿐**이다(#274 Phase 3). 수치는 손으로 채우고, "이 타워가 무엇을 하는가"는
프리팹의 `Actions`가 정하므로 CSV가 관여하지 않는다 — 이 임포터의 실질적 역할은 **없는 SO를 만들어주는 것**이다.

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

### 4.3 저작 검증 — `TowerAsset.OnValidate`

**`TowerType`/`MagicEffectType` enum은 #274 Phase 3에서 삭제됐다.** 종류의 정본이 프리팹의 `Actions`로
옮겨가면서 CSV·SO 양쪽에 있던 중복도 함께 사라졌고, `TableImporter`가 동기화하는 필드는 **`TowerID` 하나**만
남았다(실질적 역할은 "없는 SO를 만들어주는 것").

대신 SO 수치와 프리팹 구성이 갈라질 수 있게 됐으므로 `OnValidate`가 저장 시점에 잡는다(WL-130 해소):

| 검사 | 왜 |
|---|---|
| `Actions`에 공격 액션이 있는데 공격 수치가 빔 (역방향도) | 배치해도 아무것도 안 쏜다 / 적은 수치를 아무도 안 읽는다 |
| `Actions`에 **같은 타입이 둘** | `TowerAction.SourceId`가 충돌해 스탯·상태이상 슬롯을 서로 덮어쓴다 |
| `Actions`에 **null 항목** | `[SerializeReference]`는 클래스 rename·삭제 시 항목을 null로 남긴다 |
| `AttackAction`과 `HitscanAttackAction`이 **함께** | 같은 수치로 투사체와 빔이 모두 나가 DPS가 두 배가 된다 |
| `Impact == Area`인데 `SplashRadius <= 0` | 단일 명중과 같아진다 |
| 오라 액션이 있는데 해당 `Radius`가 0 | 아무에게도 안 닿는다 |

히트스캔(체인) 전용 — **읽히지 않는 저작**을 드러내는 쪽이다(#252, 구 `TowerAssetEditor`의 HelpBox 대체):

| 검사 | 왜 |
|---|---|
| `MaxChainTargets <= 1` | 튕기지 않고 단일 타격이 된다 |
| `MaxChainTargets > 1`인데 `ChainRadius <= 0` | 다음 홉을 못 찾아 최초 대상만 맞는다 |
| `MaxChainTargets > 1`인데 `ChainDamageFalloff <= 0` | 2번째 홉부터 데미지가 0(감쇠 없음은 1) |
| `Attack.ProjectilePrefab`/`Flight`가 저작돼 있음 | 빔으로 전달되므로 읽히지 않는다 |
| `Impact != Single` | 투사체를 거치지 않으므로 `Impact`가 읽히지 않는다 |
| `Effects`가 비어 있지 않음 | **체인은 명중 효과를 걸지 않는다**(§3.7.1의 확정 결정) |

인스펙터를 커스터마이즈하지 않고 저장 시점 경고로 푼 이유: 체인 필드가 늘 때마다 에디터를 함께
고쳐야 하는 유지보수 지점을 만들지 않기 위함이다(구 `TowerAssetEditor`가 정확히 그 지점이었다).

이 조합들은 전부 **예외 없이 조용히 아무 일도 안 일어나는** 종류라, 예전엔 플레이해보기 전엔 알 수 없었다
(WL-001의 `lightning_tower` 전 필드 0이 정확히 그 패턴이다).

> `TowerPrefab`이 아직 없으면 검증하지 않는다 — 저작 도중에 경고가 쏟아지지 않게 하기 위함이다.

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
> −1.0(데미지 0), 3기면 음수다. 하류에 클램프가 없어(`AttackAction` → `Projectile` →
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

### ⚠ 타워 프리팹 9개는 부모 저장소 밖에 산다

**`Assets/Imported/`는 `.gitignore`(`:162`)에 있고 자체 중첩 git 저장소(`muchan918/NorthLand-Imported`)를
갖는다.** 그런데 주력 타워 프리팹 9개(`ArcherTower`·`CandyCanon`·`SodaTower`·`SniperTower`·
`GatlingShooter`·`RollyShooter`·`ChocoFallTower`·`HasteTowerTest`·`PoisonTowerTest`)가
`@NorthLand/Prefabs/Tower/` 아래에 있다.

#274 Phase 2에서 그 9개의 `Tower.Actions`를 채운 것은 부모 저장소 커밋(`ca79dce`)에 실리지 못했고,
**중첩 저장소에 따로 커밋했다(`818f1e7`).** 부모 저장소의 커밋 하나만 받아서는 전달되지 않는다.

> **`Actions`가 빈 타워는 예외도 경고도 없이 아무 동작을 안 한다** — `Tower.Update`가 빈 리스트를
> 순회할 뿐이다. **"타워가 안 움직인다"를 만나면 `Assets/Imported/`가 최신인지부터 확인할 것.**
> 프리팹 구성이 걸린 작업은 앞으로도 **커밋이 두 저장소로 갈라진다.**

`Personal/SUNGSOO/` 아래 나머지 5개는 부모 저장소가 추적하므로 이미 포함돼 있다.

### 그 밖의 열린 항목

| # | 항목 | 상태 |
|---|---|---|
| 1 | **타워 철거 경로가 없다** | `TowerFootprint.OnDestroy`가 철거를 전제하는데 실제 UI/코드가 없다. 현재 유일한 파괴 경로는 합성 소모 |
| 2 | `lightning_tower.asset` 전 필드 0 | `TowerPrefab`/`GhostPrefab`도 둘 다 null이라 배치해도 무동작(WL-001). 수치 기입 필요. ⚠ `OnValidate`는 `TowerPrefab`이 null이면 검증을 건너뛰므로 **이 SO는 경고도 안 난다** |
| 3 | 밸런싱 수치가 CSV 밖 SO에 authoring | WL-015. `TableImporter`가 동기화하는 건 이제 `TowerID` 하나뿐이다(#274 Phase 3) — 수치는 전부 인스펙터 수기 |
| 4 | CC 강도 축 부재 | WL-127. `TowerStat`에 축이 없어 순수 감속 타워가 타일 버프에서 사거리만 이득(§5.3 ②). `SlowEffect.Multiplier`도 원장을 거치지 않는다 |
| 5 | `TowerPlacer.keepPlacing` 공유 | WL-105. true면 합성 재료 1회 소모 후 `ExtraCost`만으로 결과 타워 복제 가능 |
| 6 | EditMode 테스트 0건 | `TowerStats`·`AuraModifiers`·`HitEffect`·액션 3종이 전부 순수 C#인데 테스트가 없다. **`.asmdef`가 프로젝트에 하나도 없어** 테스트 어셈블리부터 만들어야 한다(`com.unity.test-framework 1.6.0`은 설치됨) |
| 6-a | 경량 더미로 **공격** 타워를 검증할 수 없다 | `DebugDamageTarget.HitPosition`(`Personal/SUNGSOO/`)이 `throw new NotImplementedException()`이라 `Projectile.Init`이 즉시 터진다(`DummyDamageable`도 같은 상태 — WL-121). 오라 검증에는 쓸 수 있지만 발사 경로를 보려면 `hitPosition`이 배선된 실제 몬스터 프리팹(`Blue_Grummy` 등)을 써야 한다. 스텁을 `=> transform`으로 바꾸면 둘 다 열린다 |
| 7 | ~~`Projectile.chainHitSet`이 static~~ | **#252에서 해소.** 체인이 `ChainResolver`로 분리되며 순회 상태가 **타워별 인스턴스**가 됐고(재진입으로 집합이 덮이는 경로가 구조적으로 사라짐), 명중 통지도 순회 밖에서 일괄 발행한다. §3.7.1 참조 |
| 8 | 투사체가 풀링 없이 매 발사 `Instantiate`/`Destroy` | `Area`의 `Physics.OverlapSphere`도 할당형이다 — 대상 탐색 쪽은 `NonAlloc` + 고정 버퍼를 쓰는데 투사체만 다르다(체인 홉은 #252에서 `NonAlloc`으로 전환됐다, WL-025) |
| 9 | `Personal/SUNGSOO/` 고아 프리팹·구버전 SO | `CannonTowerTest.prefab`·`SweetLand Prefab/CandyCanon.prefab`은 참조 0건. `CombatData/debuff_tower.asset`은 정규 폴더 밖 구버전 스키마 |
| 10 | 원거리 적이 동작하지 않는다 | 모든 `EnemyAsset`의 `Ranged.ProjectilePrefab`이 null이라 `Enemy.TryRangedAttack`이 즉시 false. 궤적 저작 필드도 없어 `Homing` 직선 고정이다(§3.7 각주) |

---

## 부록 A. 관련 파일

| 역할 | 경로 |
|---|---|
| 코어 | `Assets/Scripts/CombatSystem/Tower/` (Tower · **TowerAction · AttackAction · BuffAuraAction · DebuffAuraAction** · TowerStats · AuraModifiers · TowerStatsFormatter · Projectile · TowerTileBuff · TowerReloadVisual) |
| 데이터 | `Assets/Scripts/Data/Tower/` (TowerAsset · TowerData · TowerTable · TowerRecipe) · `Assets/Resources/DataTables/TowerTable.csv` · `Assets/Resources/ScriptableObjects/Towers/` |
| 배치·합성 | `Assets/Scripts/GameManager/MouseManager/TowerPlacement/` |
| UI | `Assets/Scripts/UI/TowerPanel/` · `Assets/Scripts/GameManager/MouseManager/TowerInfoUI.cs` |
| 에디터 | `Assets/Scripts/Editor/TableImporter.cs` (`TowerAssetEditor.cs`는 #274 Phase 1에서 삭제) |
| 상태이상 | `Assets/Scripts/CombatSystem/StatusEffect/` (**HitEffect** · StatusEffectHandler · MoveSpeedComposer) |
| 프리팹 | `Assets/Imported/@NorthLand/Prefabs/Tower/` (타워 9 + 고스트 + 탄환) · `Assets/Personal/SUNGSOO/Prefabs/` · `SweetLand Prefab/` (5, WL-065) |

---

## 부록 B. 개정 이력

| 개정 | 내용 |
|---|---|
| 초판 (#274) | §3~§5 현행 기록 + 재설계 제안(상속 트리)을 §6~§11로 동거 |
| 2차 (#274) | 재설계안을 액션 리스트로 전면 개정. §4.2/§4.3 개수를 실측치로 정정(6곳→쓰기 4곳, 5곳→9개 파일 34줄). §5.4의 "static이 스턴 상한의 근거"를 코드 재확인 후 정정 |
| 3차 (#274) | **§3.7 투사체 절 신설** — `FlightMode`가 `Docs/` 전체에 한 번도 없었다. 비행/명중 두 축이 독립인데 소유자가 갈라져 있음을 기록 |
| 4차 (#274) | **재설계 제안을 [TowerRedesign.md](TowerRedesign.md)로 분리.** 이 문서가 1031줄까지 불어 Core 문서 중 2위의 2배가 됐고, "현재 명세"와 "제안"이 섞여 읽을 때마다 사실/제안을 판단해야 했다. 이제 이 문서에 `[제안]`은 없다. 기존 §12를 §6(현재 코드의 열린 문제)으로 재편 |
| 5차 (#274 **Phase 1 구현**) | `TowerAsset` 스키마 평탄화 — `Single`/`Area`/`Chain`/`Magic` 래퍼 제거, `Impact`/`SplashRadius`/`Chain*`/`BuffAura`/`DebuffAura`가 최상위로. **비행 축(`Flight`/`ArcHeight`)을 탄환 프리팹 → SO로 이관**하고 `ProjectileFlight` struct 신설(§3.7 재작성). `MagicRadius` → `PreviewRadius`. `TowerAssetEditor.cs` 삭제. §4.3 참조 9개 파일 → 6개 |
| 6차 (#274 **Phase 2 구현**) | **행동 3종을 액션 리스트로 전환.** `Tower`가 `[SerializeReference] List<TowerAction>`을 직접 소유하고, 종류의 정본이 SO의 `TowerType`에서 **프리팹의 `Actions`**로 옮겨갔다(§3.1·§3.4 재작성). `ITowerBehaviour`·`TowerBuildContext`·`TowerBehaviourFactory`·`StripUnusedBehaviourComponents` 삭제. 버프 오라 구독이 `Initialize`↔`Dispose` 대칭 쌍이 되어 §3.3의 예외가 사라지고 더티 플래그로 재진입·중복 재계산이 차단됐다. 프리팹 14개에 `Actions` 채움(Missing Script 0). §4.3 참조 6개 파일 → **4개, switch 0개** |
| 7차 (#274 **Phase 3 구현**) | **`TowerType`/`MagicEffectType` enum 삭제** — 선언·SO 필드·CSV 컬럼 2개(9행)·`TableImporter` 동기화·로그 참조 전부 제거. 종류의 정본이 프리팹의 `Actions` 하나로 수렴했다. **`TowerAsset.OnValidate` 신설**(§4.3 신규) — 액션↔수치 불일치·중복 액션·null 항목·명중 방식 수치 누락을 저장 시점에 경고(WL-130 해소). 낡은 §4.3(참조 목록)·§4.4(2중 정보 문제) 삭제. `Tower.cs`의 `?.` 누락 NRE 수정(§6 #9 해소) |
| 8차 (#274 **Phase 4 구현**) | **명중 효과 부품화** — `HitEffect`(Burn/Poison/Slow/Stun) 신설, `TowerAsset.Effects`에 `[SerializeReference]`로 담는다(§3.8 신규). 공격 액션과 디버프 오라가 **같은 리스트를 공유**해 화상 장판 타워가 새 코드 없이 성립한다. `Projectile`의 세 명중 경로를 `Hit()` 한 곳으로 모아 **스턴이 Single에만 걸리던 실버그 해소**(§6 #10 삭제). `OnHitStunDuration`·`ProjectileImpact.StunDuration`·`DebuffAuraFields`의 `Duration`/`Modifiers`/`Damage` 제거 |
| 12차 (#274 **Phase 4.5 구현**) | **투사체 비행 축 부품화.** §3.7 재작성 — `FlightMode` enum + `Projectile.Update`의 분기가 사라지고 `ProjectileFlight` 부품이 됐다. **수명 계약(`Impact`↔`Finished` 분리)** 절 신설: "도달 = 명중 = 소멸"이 한 덩어리였던 것을 갈라 관통탄·부메랑이 표현 가능해졌다. **부품은 SO 공유라 무상태**여야 하고 진행값은 `FlightState`를 `Projectile`이 소유한다는 것을 명시(액션과 정반대). `Ballistic` 첫 실기 검증. 단일 `[SerializeReference]` 필드에 Unity가 타입 피커를 안 줘서 `ManagedReferencePickerDrawer` 신설 |
| 11차 (#274 **Phase 5 구현**) | **§3.9 합성 효과 계승 신설** — 종류만 계승하는 이유, 활성 종류를 SO가 아니라 인스턴스가 갖는 이유(SO 오염·다단 합성), 적용이 pull인 이유와 표시부가 같은 술어를 공유해야 하는 이유. §2 In 범위 추가. §3.8에 `BurnEffect` 이름 충돌 주의(`NorthLand.Combat` ↔ 전역 Skill) 추가. ⚠ 배관만 완료이고 `InheritEffects`를 켠 레시피는 0개라 게임 동작은 그대로다 |
| 10차 (#274 **플레이 모드 검증**) | Phase 1~4를 빈 씬 하네스로 실기 검증 — 발사·명중(Single/Area)·비행 아크·`HitEffect` 3경로·스턴 게이트·오라 2종·`Initialize`↔`Dispose` 대칭·페이즈 게이팅 전부 통과, 에러 0건. 상세 표는 [TowerRedesign.md](TowerRedesign.md) 「검증 상태」. 프리팹 9개를 중첩 저장소에 커밋(`818f1e7`)해 §6 상단 경고를 상시 주의사항으로 재작성. §6 #6-a 신설(경량 더미의 `HitPosition` 미구현이 공격 검증을 막는다, WL-121) |
| 9차 (#274 인계 정리) | §6에 **`Assets/Imported/` 중첩 저장소 경고** 신설 — 타워 프리팹 9개가 부모 저장소 밖이라 Phase 2의 `Actions` 채움이 커밋에 안 실렸다는 사실이 커밋 메시지에만 있었다. §6 번호 재정렬(…7,8,11 → 1~10) + #3·#4·#6의 낡은 서술 정정 + #10(원거리 적 미동작) 추가 |
| 13차 (**#252 병합**) | **전달 방식 축을 문서에 도입** — §3.7.1 신설(투사체 vs 히트스캔, 체인이 히트스캔인 구조적 근거, `ChainResolver`가 static이 아니어야 하는 이유, 체인이 명중 효과를 걸지 않는 확정 결정). #252(체인 히트스캔)와 #274(액션 부품화)가 같은 파일들을 각자 고쳐 병합에서 #252의 결정을 #274 구조 위로 이식했다 — **결정은 하나도 뒤집히지 않았고 구조만 옮겼다.** `IAttackAction` 능력 계약 신설(§3.2 능력 질의 규칙 재작성 — 구상 액션으로 물으면 전달 방식이 다른 타워가 조용히 빠진다). 명중 3종 → 2종(§3.7에서 `Chain` 삭제, `ProjectileImpact.MakeChain`/`ImpactKind.Chain` 제거). §4.3에 히트스캔 검증 6종 추가(구 `TowerAssetEditor` HelpBox 대체). §6 #7(static `chainHitSet`) 해소 처리 |
