# 전투 타워 구조 재설계 — 제안 `[합의 대기]`

> **이 문서는 임시 문서다.**
> 전부 **아직 코드에 없는 제안**이며, 합의 전까지 이 문서를 근거로 리뷰하거나 구현하지 말 것.
> **구현이 끝나면 내용을 [Tower.md](Tower.md)에 `[구현됨]`으로 흡수하고 이 파일은 폐기한다.**
> (`Docs/Review/WatchList.md` ↔ `WatchList-Archive.md`와 같은 수명 기준 분리다.)
>
> 현재 코드가 어떻게 동작하는지는 **[Tower.md](Tower.md)가 정본**이다. 이 문서는 그 위에서
> "무엇을 어떻게 바꿀 것인가"만 다룬다.
>
> 관련 문서: [Tower.md](Tower.md)(현재 구조) · [TowerPlacement.md](TowerPlacement.md)(배치) ·
> [TowerMerge.md](TowerMerge.md)(합성) · [GDD.md](../GDD.md) §5.8 · [SystemMap.md](../Review/SystemMap.md) §2
> 관련 이슈: #164(현 구조 확립) · #274(구조 정리)

---

## 구현 진행 상황 (#274)

| Phase | 내용 | 상태 |
|---|---|---|
| 1 | `TowerAsset` 스키마 평탄화 + 비행 축 SO 이관(§6·§6.1) | ✅ **완료** — [Tower.md](Tower.md) §3.7·§4가 현행 |
| 2 | 행동 → 액션 리스트(§2~§5), 프리팹 14개 `Actions`(§10.3) | ⬜ |
| 3 | `TowerType`/`MagicEffectType` enum 제거(§3), `OnValidate`(§3) | ⬜ |
| 4 | 명중 효과 부품화(§8) | ⬜ |
| 5 | 합성 효과 계승(§9) | ⬜ 기획 사인오프 대기 |

**완료된 절의 내용은 [Tower.md](Tower.md)가 정본이다.** 전부 끝나면 이 문서는 폐기한다.

---

## 3분 요약

**지금**: `Tower` 하나가 `TowerType` enum을 보고 런타임에 `AddComponent`로 행동을 조립한다.
그래서 ① 프리팹만 봐선 무슨 타워인지 모르고 ② 새 타워 종류를 만들 때 코드 3곳(enum·팩토리·에디터)을
고쳐야 하고 ③ 행동이 직렬화 필드를 못 갖는다.

**바꾸는 것**: `Tower`가 `[SerializeReference] List<TowerAction>`을 직접 소유한다. 행동은 MonoBehaviour를
벗고 **순수 C# 액션**이 되어 프리팹 인스펙터의 리스트에서 조립된다. 팩토리와 `TowerType` enum이 사라진다.

```
[프리팹] ArcherTower
 ▼ Tower
     data / firePoint / enemyLayerMask     (현재 위치 그대로)
     Actions                                ★ 이 타워가 하는 일 — 프리팹이 정본
       ▼ [0] Attack Action
       ▼ [1] Buff Aura Action               ← 하이브리드는 항목 추가로 끝
```

**얻는 것**: 새 타워를 **코드 0줄**로 추가한다(기획·아트가 혼자 할 수 있다). 새 효과는 클래스 1개,
완전히 새로운 동작도 클래스 1개이며 **`Tower`는 무수정**이다. 저작 패턴이 두 층에서 재귀적으로 같아진다 —
`타워 → [행동 목록]`, `행동 → [효과 목록]`.

**치르는 것**: `TowerAsset` 스키마 평탄화 + 프리팹 14개의 `Actions` 채우기. **`Tower.cs` GUID가 안 바뀌므로
프리팹 `m_Script` 교체도, `firePoint`/`enemyLayerMask` 값 복사도 없다.**

**지금 필요한 결정**: `CombatSystem/Tower`는 SystemMap상 KIM-SUNGSOO 영역이다 — **착수 전 합의가 필수**이고
이 문서가 그 자료다(§12 #5). 합성 효과 계승(§9)은 게임플레이 규칙 신설이라 **기획 사인오프**가 별도로 필요하다.

### 읽기 안내

| 목적 | 읽을 곳 |
|---|---|
| 방향만 판단 | 이 요약 |
| 합의 회의 | §1~§7 + **부록 A**(왜 다른 안이 아닌지) |
| 구현 착수 | §2~§10 |
| 현재 코드 파악 | **[Tower.md](Tower.md)** §3~§5 |

---

## 1. 문제

| 현재 | 증상 |
|---|---|
| 새 타워 추가 시 5곳을 알아야 함 | CSV 행 · `TowerType` enum · 팩토리 분기 · SO 필드 그룹 · 에디터 분기 |
| 프리팹만 봐서는 무슨 타워인지 모름 | 행동이 런타임 `AddComponent`라 인스펙터에 안 보임 |
| 행동이 직렬화 필드를 못 가짐 | `TowerBuildContext` 주입 구조체가 그것 때문에 존재 |
| 효과 추가 시 SO 필드가 늘어남 | `OnHitStunDuration` 하나가 소다 타워를 위해 추가된 상태 |

> 검토했다가 기각한 두 안(상속 트리 / 단일 클래스 + 내부 switch)은 **[부록 A](#부록-a-검토했으나-채택하지-않은-안)**에 있다.

---

## 2. 채택 구조 — `Tower`가 액션 리스트를 소유한다

행동을 **런타임 `AddComponent`가 아니라 프리팹에 직렬화된 순수 C# 객체**로 바꾼다.
`Tower`는 구상 클래스 그대로 남고, `[SerializeReference] List<TowerAction>`을 직접 소유한다.

```
[프리팹] ArcherTower
 ▼ Tower                             껍데기 — Tower.md §3의 책임 그대로 계승
     data            archer_tower    정체성
     firePoint       Muzzle          배선 (현재 위치 그대로)
     enemyLayerMask  Enemy           배선 (현재 위치 그대로)
     Actions                         ★ 이 타워가 하는 일 — 프리팹이 정본
       ▼ [0] Attack Action
             (수치는 SO가 갖는다 — §4 ①)
       ▼ [1] Buff Aura Action        ← 하이브리드는 항목 추가로 끝
```

```csharp
// 순수 C# — MonoBehaviour 아님. switch 없음.
[Serializable] public abstract class TowerAction {
    public abstract TowerActivePhase ActivePhase { get; }
    public abstract void Initialize(Tower owner, TowerAsset asset);
    public abstract void Tick(float deltaTime);
    public abstract void Dispose();
    public abstract float DisplayRange { get; }
    public abstract string DescribeStats();
}

[Serializable] sealed class AttackAction     : TowerAction { ... }
[Serializable] sealed class BuffAuraAction   : TowerAction { ... }
[Serializable] sealed class DebuffAuraAction : TowerAction { ... }
```

`Tower.Build`는 **행동을 만들지 않고 이미 있는 것을 초기화만** 한다:

```
Build(asset)
 ├─ 이미 같은 SO면 → 재무장(Initialize)만 하고 반환        (그대로)
 ├─ 프리팹이 문 SO ≠ 배치되는 SO면 → 경고 (WL-129)         (그대로)
 ├─ 이전 액션 Dispose                                      (그대로)
 ├─ data = asset
 ├─ ReinitializeActions()          ← 팩토리 호출이 사라진 자리
 └─ Register()
```

`StripUnusedBehaviourComponents()`가 사라지는 이유: **프리팹이 정본이면 "새 SO에서 빠진 행동"이라는
개념 자체가 없다.** `Update`의 페이즈 게이팅과 `OnDisable`의 `Dispose → Unregister → stats.Clear` 순서는
손대지 않는다.

**액션 3개로 타워 9종을 커버한다. 20종이 되어도 안 늘어난다** — 늘어나는 것은 SO와 프리팹뿐이다.

---

## 3. 타워 종류의 정본 = 프리팹의 `Actions` 리스트

`TowerType`/`MagicEffectType` enum과 CSV 컬럼을 삭제한다. 종류를 아는 것은 **프리팹의 `Actions`에
무엇이 담겼는가** 하나뿐이다.

| | 현재 | 제안 |
|---|---|---|
| `TowerTable.csv` `TowerType`/`MagicEffectType` 컬럼 | 있음(`TableImporter` 입력용) | **삭제** |
| `TowerAsset.TowerType`/`MagicEffectType` 필드 | 있음(런타임이 읽는 유일한 것) | **삭제** |
| 프리팹 | 종류 정보 없음(런타임 팩토리가 결정) | **`Actions` 리스트 = 정본** |

`TowerAsset.OnValidate`가 `TowerPrefab`의 `Actions`를 읽어 저작 실수를 잡는다(WL-130 해소):

- `Actions`에 `AttackAction`이 있는데 `Attack`이 비었다 (역방향도)
- `Impact == Area`인데 `SplashRadius <= 0` / `Impact == Chain`인데 `MaxChainTargets <= 1`
- `Actions`에 **같은 타입이 두 번** 들어 있다 (§4 ④의 소스 ID가 충돌한다)
- `Actions`에 **null 항목**이 있다 (`[SerializeReference]`에서 클래스 rename 시 발생)

> **살아남는 유일한 enum은 `ImpactKind`**(Single/Area/Chain)인데, 이건 "이 타워가 무엇이냐"가 아니라
> "투사체가 어떻게 터지느냐"이고 이미 [Projectile.cs:8](../../Assets/Scripts/CombatSystem/Tower/Projectile.cs)에
> 존재한다. 정체성 enum이 아니라 명중 해석 파라미터이므로 §1의 문제를 되살리지 않는다.

---

## 4. 설계 규칙 4가지 — 어기면 원래 문제로 되돌아간다

**① 액션은 수치를 갖지 않는다.**
전부 `TowerAsset`에서 읽는다. `Initialize`에서 자기 몫(`asset.Attack` / `asset.BuffAura` /
`asset.DebuffAura`)을 집어간다. `[SerializeReference]`라 수치를 액션에 직접 둘 수도 있지만, 그러면
**밸런싱 값이 프리팹으로 내려가** 머지 충돌이 늘고 데이터 테이블화 경로(WL-015)에서 더 멀어진다.
액션 리스트는 "이 타워가 하는 일의 목록"이고, 수치의 단일 출처는 계속 SO다.

**② 배선은 `Tower`가 갖고 액션은 `owner`를 통해 읽는다.**

```csharp
internal Transform FirePoint => firePoint;
internal LayerMask EnemyLayerMask => enemyLayerMask;
```

`Initialize(Tower owner, TowerAsset asset)` 2인자로 충분해져 **`TowerBuildContext`가 사라진다.**
`[SerializeReference]` 관리 객체 안에 `Transform` 참조를 직접 두는 것은 프리팹 오버라이드에서 다루기
까다로우므로 피한다. 덤으로 **프리팹 14개의 `firePoint`/`enemyLayerMask` 값이 제자리에 남는다**(§10.3).

**③ 액션의 런타임 상태는 직렬화하지 않는다.**
`cooldownTimer`·`buffed` 리스트·`hitBuffer`는 `[NonSerialized]` 또는 `[SerializeField] 없는 private`으로.
`[SerializeReference]` 객체는 `Instantiate` 시 인스턴스마다 깊은 복사되므로 런타임 상태는 자동으로
타워별 독립이 된다 — 이 성질이 액션을 MonoBehaviour 없이도 성립하게 하는 근거다.

**④ 소스 ID 채번** — 액션엔 `GetInstanceID()`가 없다.

```csharp
protected int SourceId => owner.GetInstanceID() ^ GetType().Name.GetHashCode();
```

`owner`가 타워 인스턴스별로 다르므로 **[Tower.md](Tower.md) §5.2의 "같은 종류 오라 타워 여러 기가 서로
합산 중첩"이 그대로 보존된다.** 이 성질이 깨졌던 전례가 있다 — 예전에 디버프 오라가 `TowerID` 해시를 써서
감속 타워를 2기 지어도 배율이 1중첩에 머물렀고, 보스 P1 돌진의 유일한 파훼 수단이 무력화됐다.

---

## 5. `Tower.md` §3에서 그대로 계승되는 것

**이 재설계는 #164의 성과를 버리지 않는다.** 아래는 한 줄도 바뀌지 않는다:

| 계승 항목 | 근거 |
|---|---|
| 스탯 원장 `TowerStats` | [Tower.md](Tower.md) §5 전체 — 액션이 그대로 `owner.Stats.Evaluate`를 부른다 |
| `Active` 레지스트리 + `ActiveChanged` | 등록 시점이 `Build`인 것도 그대로(WL-066) |
| 페이즈 게이팅을 호스트가 한 곳에서 | `TowerActivePhase`도 그대로(WL-044) |
| 표시 위임 — `DisplayRange` / `DescribeStats` | 액션이 자기 표시를 안다(WL-079) |
| 능력 질의 `Has<T>()` / `Get<T>()` | 제약만 `where T : TowerAction`으로. **소비처 로직은 동일** |

특히 `Has<T>()`를 인터페이스 검사로 바꾸지 않는 것이 중요하다.
[EnemyNodeQuery.IsAttackTower](../../Assets/Scripts/CombatSystem/Enemy/AI/EnemyNodeQuery.cs)는
**보스 P3 마력 봉인의 대상 정의**이고, "봉인 중에도 감속(오라 타워)은 살아남아 P1 파훼 수단이 유지된다"는
밸런스 의도가 그 한 줄에 걸려 있다(`Docs/Monster/Boss/TankGraphSpec.md`). `Has<AttackBehaviour>()` →
`Has<AttackAction>()`은 **식별자 한 단어**만 바뀌고 판정 결과는 같아야 한다.

---

## 6. 수치의 위치 — SO로 모은다

**수치는 SO에 두고**(단일 출처 유지) 액션이 자기에게 필요한 것만 읽는다. `TowerAsset`의
`Single`/`Area`/`Chain`/`Magic` 래퍼를 풀어 `[Header]`로 평탄하게 둔다:

```
[Header("공격")]  AttackFields Attack
[Header("비행")]  FlightMode Flight · ProjectileSpeed · ArcHeight        ← 프리팹에서 이관
[Header("명중")]  ImpactKind Impact · SplashRadius · ChainRadius · MaxChainTargets · ChainDamageFalloff
[Header("오라")]  BuffAuraFields BuffAura · DebuffAuraFields DebuffAura
[SerializeReference] List<HitEffect> Effects        ← §8
```

타입별 필드는 7개뿐이라(`SplashRadius` 1 + Chain 3 + Aura 3) 그룹 클래스 없이 평탄해도 읽을 만하다.
안 쓰는 타워에서 그 값이 0이어도 **아무도 안 읽으므로 무해하다.**

`TowerAsset.MagicRadius`(`:29-34`)는 `TowerType` switch를 품고 있으므로 `PreviewRadius`로 대체한다:

```csharp
public float PreviewRadius =>
    Mathf.Max(Attack?.AttackRange ?? 0f, BuffAura?.Radius ?? 0f, DebuffAura?.Radius ?? 0f);
```

WL-056의 "오라 반경 단일 출처" 성질은 유지하면서 `TowerType` 의존만 걷어낸다.
`TowerPlacer.cs:168-176`의 `Magic` 분기가 이 한 줄로 접힌다.

### 6.1 비행 축도 SO로 가져온다

[Tower.md](Tower.md) §3.7이 기록한 소유자 분리를 여기서 없앤다 — 지금은 **명중은 타워 SO가, 비행은 탄환
프리팹이** 정하고 있다. `ProjectileFlight` struct를 `ProjectileImpact`와 **대칭**으로 신설해 함께 넘긴다:

```csharp
public struct ProjectileFlight { public FlightMode Mode; public float Speed; public float ArcHeight; }

projectile.Init(target, damage, flight, impact, source);
```

지금은 `Init(target, Damage, fields.ProjectileSpeed, owner, impact)`로 **`speed`만 별도 인자로 떠 있어**
왜 그것만 특별한지 설명이 안 된다. 비행 기술자로 흡수하면 "대상·데미지·비행·명중·소스"로 읽힌다.

**근거 4가지:**

1. **같은 궤적을 만드는 두 값이 다른 파일에 산다.** 캐논 곡사를 튜닝하려면 `cannon_tower.asset`(속도 100)과
   `CandyBullet.prefab`(`arcHeight` 15)을 오간다.
2. **궤적은 비주얼이 아니라 밸런스다.** 속도와 아크는 착탄까지의 시간을 정하고, 그것이 움직이는 적에 대한
   실효 DPS를 정한다. 밸런스 값은 밸런스가 사는 곳에 있어야 한다.
3. **WL-015 방향에 역행한다.** 수치를 CSV로 내리는 것이 방향인데, `TableImporter`는 CSV→SO 경로를 갖지만
   CSV→프리팹 경로는 없고 앞으로도 만들 이유가 없다. 프리팹에 있는 수치는 그 경로에서 한 칸 더 멀다.
4. **탄환 프리팹이 공유된다.** `Rolly_Bullet` 하나를 archer/gatling/sniper/soda가 함께 써서 타워별로 다른
   비행을 줄 수 없다. — 단, ①~③은 **프리팹이 타워당 1:1이 되어도 그대로 유효**하다. 값이 같아지는 것과
   어느 파일에 사는가는 다른 문제다.

**프리팹에 남는 것은 `rotationOffset` 하나**다. 모델 메시의 기수가 어느 축을 보는지 보정하는 값이라
화살은 `-90`, 공은 `0`이고 **타워가 알 이유도 알아서도 안 된다.** 결과적으로 역할이 갈린다:

```
탄환 프리팹  =  메시 · 트레일 · 파티클 · 모델 축 보정       "어떻게 보이는가"
타워 SO      =  어떻게 날아가서 어떻게 터지는가 + 모든 수치   "무엇을 하는가"
```

`ProjectilePrefab` 필드의 의미도 명확해진다 — **"어떤 모양으로 보일지"만 고르는 것**이 되고, 지금처럼
"모양 + 숨은 비행 설정"을 함께 고르는 게 아니게 된다.

> `Ballistic`은 살아 있는 SO 기준 사용처 0이지만 **삭제하지 않는다.**
>
> ⚠ **"한 번도 안 쓰였다"는 아니다** — 구현 중 확인해보니 `Personal/SUNGSOO/`의 탄환 프리팹
> `TB_CanonTower_Lvl2_Ball`(Ballistic, arc 10)과 `SweetLand Prefab/CandyBullet`(Ballistic, arc 30)이
> 실제로 그렇게 저작돼 있었다. 둘 다 **참조 0건 고아**라 어떤 `TowerAsset`도 가리키지 않아 이관 대상에서
> 빠졌고, 값은 이제 git 히스토리에만 남는다. 즉 **캐논 곡사를 진짜 포격으로 만들려던 시도가 있었고**
> 프리팹이 고아가 되면서 사라진 것이다. 안 쓰이던 이유가 "프리팹에 박혀 골라 쓰기 불편해서"라는
> 추정을 뒷받침한다.
>
> 게임 디자인상 `Ballistic`이 의미를 갖는 것은 **광역과 짝지을 때**다 — 빗나가도 스플래시가 주변을
> 때리므로 "적 무리의 길목을 예측해 쏜다"가 성립한다. 단일 대상에 주면 그냥 빗나가기만 해서 손해다.

---

## 7. 이 재설계로 사라지는 것

`TowerBehaviourFactory` · `StripUnusedBehaviourComponents()` · `TowerBuildContext` ·
`ITowerBehaviour`(→ `TowerAction`) · `TowerType`/`MagicEffectType` enum · `TowerAsset.MagicRadius` ·
`TowerAsset`의 `Single`/`Area`/`Chain`/`Magic` 래퍼 · **`TowerAssetEditor.cs` 파일 전체** ·
`ProjectileImpact.StunDuration` · `TowerAsset.OnHitStunDuration` ·
`Projectile`의 `[SerializeField] flightMode`/`arcHeight`(→ SO로 이관, §6.1)

`TowerAssetEditor`가 통째로 사라지는 이유: 존재 이유가 `TowerType`으로 필드 그룹을 골라 그리는 것이었는데
필드가 평탄해지면 기본 인스펙터로 충분하다. 하드코딩된 `FindProperty` 문자열 9개와 `enumValueIndex`
직접 캐스팅 위험([Tower.md](Tower.md) §4.3 주석)이 함께 사라진다.

**덤으로 좋아지는 것 2개:**

**① `BuffAuraAction`의 이벤트 구독이 대칭 쌍이 된다.** 현재 `BuffAuraBehaviour`는 `Initialize`에서 걸고
`OnDestroy`에서 푸는 **비대칭**이고, [Tower.md](Tower.md) §3.3이 "생명주기 규약의 유일한 예외"라고
명시해둔 자리다. 액션은 MonoBehaviour가 아니므로 구독을 `Initialize`↔`Dispose` 쌍으로 두면 된다 —
`Tower.OnDisable`이 `Dispose`를 부르고 Destroy 시 `OnDisable`이 선행하므로 static 이벤트 누수 경로가 닫힌다.
동시에 **더티 플래그화**(이벤트는 플래그만, 실행은 `Tick`에서)로 ⓐ 이벤트 핸들러 내 `Reapply` 재진입 차단
ⓑ 합성으로 재료 3개가 동시에 빠질 때 3번 돌던 재계산이 프레임당 1회로 접힘을 얻는다.

**② 씬 없이 EditMode 테스트가 가능해진다.** 액션이 순수 C#이 되면서 `TowerStats`가 `Time.time`을 직접
읽지 않도록 설계된 것과 같은 이유의 이득을 본다. 현재 테스트 0건에 그물을 깔 자리가 생긴다.

---

## 8. 명중 효과 부품화

> **§2와 같은 패턴을 한 층 아래에 적용한 것이다.**
>
> ```
> 타워          →  [행동 목록]      "이 타워는 무엇을 하는가"        §2
> 행동(액션)     →  [효과 목록]      "맞으면 무슨 일이 나는가"        §8
> ```
>
> 두 층이 같은 규칙이면 저작자가 배울 것이 절반이다 — 인스펙터에서 `+`로 부품을 고르고 그 자리에 수치를
> 넣는 감각이 위아래 동일하다. `[SerializeReference]` 주의점(클래스 rename 시 참조 끊김 → `[MovedFrom]`,
> null 항목 검증)도 한 벌만 익히면 된다.

### 8.1 데이터

```csharp
public enum EffectKind { Burn, Poison, Slow, Stun }

[Serializable] public abstract class HitEffect {
    public abstract EffectKind Kind { get; }
    public abstract void Apply(IDamageable target, IAttacker source, int sourceId);
}

// TowerAsset — 이 타워가 낼 수 있는 효과와 그 수치
[SerializeReference] public List<HitEffect> Effects;
```

구현체: `BurnEffect { DamagePerTick, TickInterval, Duration }` ·
`PoisonEffect { ... }` · `SlowEffect { Multiplier, Duration }` · `StunEffect { Duration }`

**부품과 수치가 한 덩어리다.** 인스펙터에서 `+ Burn`, `+ Slow`를 골라 붙이고 그 자리에서 수치를 넣는다.

### 8.2 신규 인프라를 만들지 않는다

모든 `HitEffect`는 **기존 `StatusEffectHandler`로 흘러간다**(`ApplyOrRefresh` / `ApplySlow`).
효과 소유·지속시간 소진·소스별 공존이 이미 대상 쪽에 구현돼 있다([Tower.md](Tower.md) §5.4).
화상·독 추가에 필요한 런타임 인프라는 **0**이다 — 지금 부족한 건 "어떤 효과를 걸지"가 데이터가
아니라는 것뿐이다.

### 8.3 `sourceId` 채번

```
sourceId = tower.GetInstanceID() ^ (int)EffectKind
```

기존 관례를 따른다(`EnemyApplyTowerDebuffAction.cs:78`의 `agentID ^ 효과종류해시`). §4 ④와 같은 꼴이다.

- 같은 종류 타워 여러 기 → 인스턴스ID가 다르므로 **자동 중첩**
- 한 타워 안에서 → 종류당 하나(§9.2에서 그렇게 되도록 보장)

⚠ **부품 인덱스를 섞으면 안 된다.** SO에서 부품 순서를 바꾸거나 하나 지우면 진행 중이던 효과의
`sourceId`가 바뀌어, 대상 쪽에 **회수되지 않는 유령 효과**가 남는다.

### 8.4 디버프 오라도 같은 부품을 쓴다

`DebuffAuraAction.Tick()`이 범위 내 적에게 `Effects`를 순회 적용한다. 그러면 **"화상 장판 타워"가
공짜로 생긴다** — 지금은 `DebuffAuraFields`에 `Damage`/`Modifiers` 수기 필드로 DoT·슬로우가 박혀 있다.

### 8.5 덤 — 실버그 하나가 여기서 고쳐진다

현재 `AttackBehaviour.BuildImpact`([:114-115](../../Assets/Scripts/CombatSystem/Tower/AttackBehaviour.cs))는
`TowerType`과 무관하게 `impact.StunDuration`을 채우는데,
`Projectile.OnHit`([:166-186](../../Assets/Scripts/CombatSystem/Tower/Projectile.cs))은
**`Single` 경로에서만** `ApplyStun`을 부른다. Area/Chain 타워 SO에 `OnHitStunDuration`을 저작하면
**조용히 무시된다.**

`Effects`를 `OnHit`의 세 경로(Single/Area/Chain) 공통 지점에서 적용하면 이 비대칭이 구조적으로 사라진다.
현재 이 필드를 쓰는 SO는 `soda_tower`(0.7) 하나뿐이라 마이그레이션 비용도 없다.

---

## 9. 합성 효과 계승

> GDD §5.8과 [TowerMerge.md](TowerMerge.md) §13이 **"재료 승계"를 TBD로 열어둔** 자리다. 이 절이 그 안이다.
> **기획 사인오프가 별도로 필요하다**(§12 #4).

### 9.1 규칙

**계승되는 것은 효과의 "종류"뿐이고, 수치는 결과 SO가 적는다.**

```
TowerRecipe
 ├─ Materials · Result · ExtraCost      ← 기존
 └─ InheritEffects (bool)               ← 신규. 계승 여부는 레시피가 정한다

합성 확정
 → 재료들의 활성 효과 종류를 합집합으로 수집          {Burn, Slow}
 → 결과 SO(Result)의 Effects 중 그 종류만 활성화
 → 결과 SO에 정의됐지만 재료에 없던 종류는 꺼진 채로 남음
```

**왜 종류만 계승하는가:**
- 수치까지 계승하면 "같은 효과가 겹칠 때 max냐 합산이냐"를 정해야 하는데, 합산은 같은 타워를 계속
  합성할 때 무한 스택이 되고 max는 또 다른 규칙이 된다. **종류만 계승하면 그 질문 자체가 사라진다.**
- 결과 타워의 밸런싱을 손저작으로 완전히 제어할 수 있다 — "화염 계열 상위 타워는 화상이 유독 세다"가
  공짜로 표현된다.
- 효과 종류 상한도 불필요하다. 다단 합성으로 종류가 4개까지 쌓여도 각 수치를 결과 SO가 통제한다.

**저작 부담은 작다.** 결과 SO는 **그 레시피의 재료가 낼 수 있는 효과만** 적으면 된다 — 화상+슬로우
레시피면 결과 SO엔 `Burn`/`Slow` 둘뿐이다.

### 9.2 ★ 활성 효과 종류는 **인스턴스**가 소유한다

```csharp
public class Tower : MonoBehaviour {
    readonly HashSet<EffectKind> activeKinds = new();
    public IReadOnlyCollection<EffectKind> ActiveEffectKinds => activeKinds;
    public void ActivateEffects(IEnumerable<EffectKind> kinds) { ... }
}
```

**두 가지 이유로 SO에 쓰면 안 된다:**

**① SO 오염.** `Result`는 `TowerAsset` SO 하나고 씬의 모든 인스턴스가 공유한다. 런타임에
`Result.Effects`를 건드리면 다음 합성이 이전 계승분을 물려받고, `[SerializeReference]`라 진짜로
직렬화되어 **`.asset` 파일에 영구히 남는다.**
(현재 `TowerAsset.Data`도 같은 패턴인데, `TowerData`가 `[Serializable]`이 아니라 직렬화가 안 돼서
우연히 살아남은 것이다.)

**② 다단 합성.** 합성 결과 타워도 다시 재료가 될 수 있다(`TowerPlacer.cs:312` 주석).

```
화상타워 + 슬로우타워 → A     (A의 활성 = {Burn, Slow})
A + 독타워            → B     (B의 활성 = {Burn, Slow, Poison})
```

두 번째 합성에서 "재료 A가 화상·슬로우를 갖는다"를 판정하려면 **A의 SO가 아니라 A 인스턴스의 활성
상태**를 읽어야 한다. SO를 읽으면 정의된 효과 전부(꺼진 것 포함)가 잡힌다.

> 이 절의 근거는 **타워 클래스 구조와 무관**하다 — SO 오염과 다단 합성은 §2를 어떻게 짜든 그대로 성립한다.

### 9.3 실행부 연결

`TowerFusionController.TryFuse`가 이미 `groupTowers` 리스트를 만드는 자리(`:42`)에서 재료들의
`ActiveEffectKinds`를 모으고, `TowerPlacer`를 거쳐 결과 타워에 전달한다.

**툴팁과 실행부는 같은 함수를 공유해야 한다** — `ResolveInheritedKinds(recipe, group)` static 하나.
`TowerFusionMatcher`가 후보 버튼 활성 판정과 실행부를 공유하는 것과 같은 이유다(규칙 재구현 금지).

### 9.4 플레이어 가시성

후보 버튼 호버 시 **"화상 + 슬로우를 물려받습니다"**가 보여야 한다. 안 보이면 플레이어는 합성을
그냥 "상위 타워 만들기"로만 인식하고 조합의 재미를 느끼지 못한다.
`TowerMergeCandidateHover`가 이미 호버 훅을 잡고 있으므로(핑크 프리뷰용) 그 자리에 붙인다.

### 9.5 저작 실수 방어

`TowerRecipe.OnValidate` — **재료가 낼 수 있는 효과 중 결과 SO에 정의되지 않은 게 있으면 경고**.
없으면 그 효과가 조용히 사라진다(WL-001의 `lightning_tower` 전 필드 0과 같은 무증상 패턴).

---

## 10. 마이그레이션

### 10.1 `Tower_v2` 같은 병렬 파일을 만들지 않는다

Unity 프리팹·씬은 컴포넌트를 클래스 이름 + GUID로 물고 있어, 이름을 바꾸면 Missing Script가 되고
인스펙터 배선이 날아간다. 그리고 `Tower`는 **구상 타입으로 23개 파일에 박혀 있고 `Tower.Active`가
시스템의 중심**이라(`Active` 소비처 6곳 + `ActiveChanged` 3곳), 껍데기를 복제하면 v1/v2 타워가 서로를 못 본다:

| 기능 | 증상 |
|---|---|
| 버프 오라 · 버프 스킬 · 보스 마력 봉인 | `Tower.Active` 순회 → v2 타워가 대상에서 빠짐 |
| 합성 선택 | `sel is Tower` → v2 타워 클릭해도 선택 집합에 안 들어감 |
| 배치 | `TryGetComponent(out Tower)` → v2는 조립 자체가 안 됨 |

버전 관리는 git 브랜치가 한다.

### 10.2 이름 고정 필수 vs 자유 재작성

| 이름 고정 필수 (프리팹·씬·.asset이 참조) | 자유롭게 재작성/삭제 가능 (참조 0건) |
|---|---|
| **`Tower`** · `Projectile` (프리팹) | `AttackBehaviour` · `BuffAuraBehaviour` · `DebuffAuraBehaviour` |
| `TowerAsset` · `TowerRecipe` (.asset 11개) | `ITowerBehaviour` · `TowerBehaviourFactory` |
| `TowerTileBuff` · `TowerReloadVisual` · `TowerFootprint` · `TowerGroupSelectable` | `TowerStats` · `AuraModifiers` · `TowerStatsFormatter` |
| `TowerPlacer` · `TowerSelectPanelView` · `TowerMergeCoordinator` · `TowerFusionController` · `TowerMergePanelView` · `TowerInfoUI` (씬) | `TowerFusionMatcher` · `TowerMergeGroup` · `TowerMergeCommand` |

**왼쪽은 클래스명과 `[SerializeField]` 필드명만 유지하면 내부를 자유롭게 갈아엎어도 프리팹이 안 깨진다.**
오른쪽 행동 3종이 자유로운 이유는 `[AddComponentMenu("")]` + 런타임 `AddComponent` 전용이라
**어떤 프리팹도 물고 있지 않기 때문이다** — 그래서 `AttackAction`/`BuffAuraAction`/`DebuffAuraAction`으로
파일째 갈아엎어도 아무것도 안 깨진다.

**이 안이 `Tower`를 구상 클래스로 남기는 것이 [부록 A.1](#a1-상속-트리--tower-abstract--파생-7종)의
상속 트리 대비 결정적인 이득이다** — 왼쪽 첫 칸이 그대로이므로 프리팹 14개의 `m_Script` GUID를
건드릴 일이 없다.

### 10.3 프리팹 마이그레이션 — `Actions` 리스트만 채운다

**실측: `Tower.cs` GUID `76c1645faf600e94e9cd5231388979f5`를 문 프리팹은 14개다.**

| 위치 | 개수 | 비고 |
|---|---|---|
| `Assets/Imported/@NorthLand/Prefabs/Tower/` | 9 | 정본 계열 |
| `Assets/Personal/SUNGSOO/Prefabs/` | 3 | `ArcherTowerTest` · `CannonTowerTest` · `AuraTowerTest` |
| `Assets/Personal/SUNGSOO/SweetLand Prefab/` | 2 | `CandyCanon` · `RolliShooter` — 정본 계열과 `fileID`까지 같은 복제본(WL-065) |

- **고스트 프리팹 13개는 대상이 아니다** — `Tower` 컴포넌트가 아예 없는 순수 메시 트리다.
- `CannonTowerTest.prefab`과 `SweetLand Prefab/CandyCanon.prefab`은 **어떤 씬·프리팹도 참조하지 않는
  고아**다. 마이그레이션 대상에서 뺄지(=삭제) 담당자 확인이 선행돼야 한다(§12 #6).

**작업 내용은 하나뿐이다** — 각 프리팹의 `Tower.Actions`에 액션을 담는다.
`firePoint`/`enemyLayerMask`는 §4 ②에 따라 `Tower`에 그대로 남으므로 **값을 건드리지 않는다**
(14개 전부 `enemyLayerMask.m_Bits: 128`, 레이어 7 — 검증 기준값).

⚠ **`.prefab` YAML을 손으로 고치지 말 것.** `unity-cli exec`로 일회성 에디터 스크립트를 돌린다:
`TowerAsset` 순회 → `TowerPrefab`을 `PrefabUtility.LoadPrefabContents` → `Attack`/`BuffAura`/`DebuffAura`
유무를 보고 해당 액션 추가 → `SaveAsPrefabAsset` + `UnloadPrefabContents`.
작업 후 `unity-cli editor refresh` + **Missing Script 0건 확인 필수**.

> `lightning_tower.asset`은 `TowerPrefab`/`GhostPrefab`이 둘 다 null이라 마이그레이션 대상이 없다(WL-001).
> `archer_tower.asset`의 `TowerPrefab`은 `ArcherTower.prefab`이 **아니라 `RollyShooter.prefab`**이다 —
> 씬 인스펙터에 남은 `ArcherTower` 기본값은 런타임에 SO가 덮으므로 실사용은 RollyShooter다.

### 10.4 컴파일러가 못 잡는 것 — 위험도 순

| # | 대상 | 증상 |
|---|---|---|
| 1 | `AuraTowerTestDriver.cs:32-33, 49-52` — 리플렉션 `"data"`/`"enemyLayerMask"` + `AddComponent<Tower>()` | `Actions`가 빈 채로 생성되어 **아무 동작도 하지 않는 타워**가 된다 |
| 2 | `EnemyNodeQuery.IsAttackTower` = 보스 P3 마력 봉인 대상 정의 | 판정 의미가 바뀌면 밸런스 의도가 **조용히 뒤집힌다**(§5) |
| 3 | `TowerPlacer.cs:333` 타일 버프 → `:339` `Build` 순서 | 역전 시 버프 오라 초기 반경 오류. **주석에만 존재** |
| 4 | `TowerMergeCommand`의 `Release()`→`SetActive(false)` / `Reoccupy()`→`SetActive(true)` | `Tower.OnEnable`/`OnDisable` 대칭성에 의존 |
| 5 | `TowerMergeCoordinator.cs:270-273`의 `Prune` | "`OnDisable` 시점엔 아직 Unity fake-null이 아니다"에 의존 |
| 6 | `Skill/BurnBuff.cs:80` `source is not Tower` | `Tower`가 구상 클래스로 남으므로 **이 안에서는 안전** |

### 10.5 거의 안 건드려도 되는 곳

`Tower` 타입과 `TowerStats`만 알기 때문에 그대로 동작한다:
`TowerMergeGroup` · `TowerMergeCommand` · `TowerFusionMatcher` · `TowerMergeCoordinator` ·
`TowerFootprint` · `TowerGroupSelectable` · `BuffSkillManager` · `TowerInfoUI`(문자열 계약만) ·
`TowerStats` · `AuraModifiers` · `TowerStatsFormatter`

---

## 11. 새 타워 / 새 효과 추가하는 법

### 11.1 현재 — 5곳

1. `TowerTable.csv`에 행 추가 (ID·이름키·**TowerType**·풋프린트·설명키)
2. `TableImporter` 실행 또는 `Towers/{ID}.asset` 수동 생성
3. SO에서 `TowerType` 선택 → 해당 필드 그룹에 수치 입력 + 프리팹/고스트 연결
4. 새 **종류**라면 `TowerType` enum 추가 + `TowerBehaviourFactory` 분기 + `TowerAssetEditor` 분기
5. 로컬라이제이션 `NorthLand_Towers`에 이름/역할/설명 키 추가
6. `TowerSelectPanelView._towers`에 SO 등록

### 11.2 재설계 후 — 코드 0줄

| 하는 일 | 코드? |
|---|---|
| 1. 프리팹에 `Tower`를 붙이고 `firePoint`/`enemyLayerMask` 배선 | ✗ |
| 2. `Actions`에 `+ Attack Action` (하이브리드면 `+ Buff Aura Action`까지) | ✗ |
| 3. `Towers/{ID}.asset` 생성 → 수치 + `Effects`에 부품 드래그 + 프리팹/고스트 연결 | ✗ |
| 4. CSV 행(이름키·설명키·풋프린트) + 로컬라이제이션 키 + `TowerSelectPanelView` 등록 | ✗ |

**enum·팩토리·에디터 분기를 건드릴 일이 없어진다.** 기획·아트가 프로그래머 없이 타워를 추가할 수 있고,
이것이 이 재설계의 가장 큰 실익이다.

이 층에서 만들 수 있는 것이 생각보다 넓다:

| 원하는 타워 | 방법 |
|---|---|
| 스플래시 + 화상 | `AttackAction` · `Impact=Area` · `Effects=[Burn]` |
| 체인 + 감속 | `AttackAction` · `Impact=Chain` · `Effects=[Slow]` |
| 독 장판 / 화상 장판 | `DebuffAuraAction` · `Effects=[Poison]` / `[Burn]` |
| **공격하면서 아군을 강화하는 하이브리드** | `AttackAction` **+** `BuffAuraAction` 둘 다 담기 |

### 11.3 세 층으로 정리하면

- **Level 1 — 기존 부품 조합**: 위 표. **코드 0줄**
- **Level 2 — 새 효과**(빙결·출혈·방어력 감소 등): `HitEffect` 파생 **1개**
  1. `EffectKind`에 값 추가
  2. `HitEffect` 파생 클래스 1개 — `Apply()`에서 `StatusEffectHandler`를 호출
  3. 원하는 타워 SO의 `Effects` 리스트에 인스펙터로 드래그 + 수치 입력

  **끝이다.** 투사체·타워·합성 코드는 무수정이다. 공격 액션과 디버프 오라가 같은 부품을 공유하므로
  하나 만들면 양쪽에서 쓴다. 합성 조합은 결과 SO에 그 효과를 정의하고 `InheritEffects`를 켜면 자동 성립한다.
- **Level 3 — 완전히 새로운 동작**(밤 시작 시 1회 폭발, 넉백, 레이저 빔, 자원 생산 등):
  `TowerAction` 파생 **1개**. 껍데기는 부품이 뭔지 모르고 `Tick`만 부르므로 **`Tower`는 한 글자도 안 바뀐다**

---

## 12. 열린 결정 / TBD (재설계 관련)

> 현재 코드 자체의 미결 항목은 [Tower.md](Tower.md) §6에 있다.

| # | 항목 | 상태 |
|---|---|---|
| 1 | **스턴 `sourceId`를 인스턴스별로 바꿀지** — 현재 `Projectile.StunEffectId`가 static이라 모든 소다 타워가 스턴 슬롯 하나를 공유한다. 코드 재확인 결과 **가동률 상한은 이 static이 아니라 대상 쪽 게이트에서 나온다**([Tower.md](Tower.md) §5.4) — 인스턴스별로 채번해도 `CanStunNow()`가 같은 자리에서 막으므로 **실동작 차이가 거의 없다**. 즉 §8.3 규칙의 예외를 둘 이유도 딱히 없다 | **낮은 우선순위** — 재설계 PR에서는 현행 고정 ID 유지, **별도 이슈로 분리** |
| 2 | 결과 SO에 재료 효과가 정의되지 않았을 때 | §9.5 `OnValidate` 경고로 잡기로 함(합의 대기) |
| 3 | 합성으로만 생기는 고유 효과("화상+슬로우 → 폭발") | 현재 안은 미지원. 나중에 `BaseEffects`(항상 켜짐) 리스트를 추가하면 됨 — 지금 구조가 막지 않음 |
| 4 | GDD §5.8 · [TowerMerge.md](TowerMerge.md) §13의 "재료 승계 TBD" | §9가 그 안. **기획 사인오프 필요** |
| 5 | `CombatSystem/Tower`는 SystemMap상 **KIM-SUNGSOO 영역** | 재설계는 공격 계약(`TowerAsset.Attack`)과 프리팹 구조를 함께 바꾼다 — **착수 전 합의 필수**(WL-001과 같은 축). **이 문서가 그 합의 자료. 사인오프 대기** |
| 6 | `Personal/SUNGSOO/` 고아 프리팹·구버전 SO | `CannonTowerTest.prefab`·`SweetLand Prefab/CandyCanon.prefab`은 참조 0건. `CombatData/debuff_tower.asset`은 지금은 없는 `BuffAura.Interval` 필드가 남은 구버전 스키마 — **마이그레이션 전 폐기 여부 확인 필요**(§10.3) |
| 7 | **투사체 부품화** — 비행·명중을 `[SerializeReference]` 부품으로 승격할지 | **판단 유예.** 아래 §12-A |

### 12-A. 투사체 부품화 — 왜 지금 정하지 않는가

§2는 타워 축의 enum + switch를 없앴지만, **투사체 축에는 그 패턴이 그대로 남는다** —
`FlightMode` enum + `Projectile.Update()`의 분기, `ImpactKind` enum + `OnHit()`의 switch.
방식이 3~4종을 넘으면 [부록 A.2](#a2-클래스-하나--내부-switch)에서 기각한 바로 그 구조가 된다.

**그럼에도 이번 범위에 넣지 않는 이유:**

- `Docs/GDD.md`에 투사체 관련 언급이 **한 줄도 없다.** 어떤 공격·비행 방식이 필요한지 목록이 아직 없다.
- §4가 확립한 규칙 4가지가 자리잡은 뒤면 투사체 부품화는 **같은 패턴의 재적용**이 되어 훨씬 싸다.
- 타워 구조 작업과 **의존이 거의 없다** — §2(액션 리스트)는 투사체와 접점이 0이고, §8(효과 부품화)만
  `Projectile.OnHit`을 건드린다. 순서를 뒤로 미뤄도 손해가 없다.

**그때의 설계 방향 (스케치):**

```csharp
[Serializable] public abstract class ProjectileFlight {   // 상태 없음 → SO 공유 안전
    public abstract void Step(ref FlightState s, float dt);
    public abstract bool ReachedImpact(in FlightState s);
}
```

⚠ **상태는 `Projectile`이 소유해야 한다.** 액션(§2)은 **프리팹**에 담겨 `Instantiate` 시 자동 복제되지만
(§4 ③), 비행 부품은 **SO**에 담기므로 복제되지 않는다. 투사체 10발이 SO의 부품 하나를 공유하면
`traveled`·`homingPos` 같은 진행값이 뒤엉킨다. 부품은 규칙만, 상태는 투사체가 — 이 분리가 전제다.

⚠ **함께 걷어내야 하는 전제**: "투사체는 한 번 맞고 `Destroy`"가 `UpdateHoming`(`:136-140`)과
`UpdateBallistic`(`:159-163`) **양쪽에 하드코딩**돼 있다. 부메랑·관통탄은 이걸 어긴다(가는 길에 여러 번
때리고, 때려도 안 죽고, 대상이 아니라 경로를 따라간다). "비행 부품이 끝났다고 할 때까지 산다"로 바꿔야
부품화가 값어치를 한다.

**레이저/빔은 여기 속하지 않는다.** 투사체를 `Instantiate` 하지 않으므로 `ImpactKind`에 값을 더하는 게
아니라 `AttackAction`과 나란히 놓이는 **별도 액션**(`BeamAction`)이다 — §11.3의 **Level 3**이고
`Tower`는 무수정.

**전제 조건**: 부품화하려면 비행 설정이 SO에 있어야 한다. §6.1의 이관이 **그 첫 칸**이고, struct에서
부품 클래스로 승격하는 비용은 SO 필드 하나 교체다.

---

## 부록 A. 검토했으나 채택하지 않은 안

> 합의가 끝나면 다시 읽을 일이 없는 내용이라 본문에서 내렸다. **"왜 저 방향이 아닌가"가 궁금할 때만** 보면 된다.

### A.1 상속 트리 — `Tower` abstract + 파생 7종

초판의 안이었다. `Tower`를 abstract로 만들고 `SingleTargetTower`/`AreaTower`/`ChainTower`/`BuffAuraTower`/
`DebuffAuraTower` 등 **7개 파생 클래스**로 쪼갠다. §1의 문제는 확실히 풀지만 두 가지를 잃는다.

**① 하이브리드 타워를 구조적으로 포기한다.**
[Tower.cs:28-29](../../Assets/Scripts/CombatSystem/Tower/Tower.cs)가 행동을 단일 참조가 아니라
**리스트**로 둔 이유를 주석에 명시해뒀다 — "공격+오라 하이브리드 타워를 공짜로 허용한다". 상속 트리에선
`AttackTower`이면서 동시에 `BuffAuraTower`일 수 없다. 지금 그런 타워가 없다고 해서 여는 문을 닫을 이유는
없고, 특히 §9 합성으로 상위 타워를 만들수록 "공격하면서 주변을 강화하는 타워"는 자연스러운 요구다.

**② 프리팹 마이그레이션 비용이 초판의 추정보다 크다.** 실측(§10.3에 상세):

| 이전 추정 | 실측 |
|---|---|
| "타워 프리팹 9종 + 고스트" | `Tower.cs` GUID(`76c1645f…`)를 문 프리팹 **14개**. 고스트 프리팹 13개엔 `Tower` 컴포넌트가 **아예 없어** 무관 |
| "필드명만 유지하면 인스펙터 값 보존" | 맞지만 `Personal/SUNGSOO/Scripts/AuraTowerTestDriver.cs:49-52`가 `"data"`/`"enemyLayerMask"`를 **문자열 리플렉션**으로 물고 있어 **컴파일 타임에 안 잡힌다** |

상속 트리로 가면 프리팹 14개의 `m_Script` GUID를 전부 교체해야 하고, 하나라도 놓치면 Missing Script가 된다.

### A.2 클래스 하나 + 내부 switch

반대 방향의 유혹 — 행동 3종을 하나로 합치고 안에서 모드를 고르는 안:

```csharp
public class TowerBehaviour : MonoBehaviour, ITowerBehaviour {
    enum Mode { Attack, BuffAura, DebuffAura }
    [SerializeField] Mode mode;
    public void Tick(float dt) { switch (mode) { ... } }
}
```

**이것은 [Tower.md](Tower.md) §3.2가 기록한 #164 이전 상태로의 회귀다.** `ITowerBehaviour`는 멤버가 6개
(`ActivePhase`·`Initialize`·`Tick`·`Dispose`·`DisplayRange`·`DescribeStats`)라 모드를 내부에서 결정하면
**그 6개가 전부 switch가 된다** — 예전 `AuraTower`가 `MagicEffectType`으로 6곳에서 분기하던 것과 같은 수다.
그때 나온 사고가 WL-044(페이즈 게이팅이 한쪽에만 있어 오라가 낮에도 동작) · WL-079(스탯 텍스트 3곳 복붙) ·
WL-050/081(버프 원장 두 벌)이다.

덤으로 한 컴포넌트가 공격이면서 동시에 오라일 수 없으므로 하이브리드도 A.1과 똑같이 막힌다.

**파일 수가 줄어드는 것은 단순해진 것이 아니다** — 판단이 사라진 게 아니라 안으로 숨었을 뿐이고,
행동이 4종·5종이 되면 매번 그 6곳을 다 열어야 한다.

---

## 부록 B. 재설계 후 파일 변화

| | 파일 |
|---|---|
| **신규** | `CombatSystem/Tower/TowerAction.cs` · `AttackAction.cs` · `BuffAuraAction.cs` · `DebuffAuraAction.cs` · `StatusEffect/HitEffect.cs` |
| **삭제** | `ITowerBehaviour.cs` · `TowerBehaviourFactory.cs` · `AttackBehaviour.cs` · `BuffAuraBehaviour.cs` · `DebuffAuraBehaviour.cs` · `Editor/TowerAssetEditor.cs` |
| **수정** | `Tower.cs`(액션 리스트 소유) · `TowerAsset.cs`(평탄화 + 비행 + `Effects`) · `TowerData.cs`(enum 제거) · `Projectile.cs`(비행 이관 + 효과 적용 공통화) · `TowerPlacer.cs`(`PreviewRadius`) · `TableImporter.cs` · `EnemyNodeQuery.cs`(식별자) |
| **일회성** | `Editor/TowerAssetMigration.cs` — SO 스키마 이전 + 프리팹 `Actions` 채움. 검증 후 삭제 |

기존 파일 목록은 [Tower.md](Tower.md) 부록 A 참조.

---

## 부록 C. 개정 이력

| 개정 | 내용 |
|---|---|
| 초판 (#274) | Tower.md §6~§11로 작성. **상속 트리** 안(`Tower` abstract + 파생 7종) |
| 2차 (#274) | **액션 리스트**로 전면 개정 — 하이브리드 타워 보존 + 프리팹 `m_Script` 무변경(부록 A.1). Tower.md §4.2/§4.3 개수를 실측치로 정정. 마이그레이션을 실측 프리팹 14개 기준으로 교체. Tower.md §5.4의 "static이 스턴 상한의 근거"를 코드 재확인 후 정정 |
| 3차 (#274) | Tower.md에 §3.7 투사체 절 신설(비행 축이 문서 전체에 없었다). **비행 축 SO 이관**(§6.1) 추가. 투사체 부품화 판단 유예(§12-A) 기록 |
| 4차 (#274) | **Tower.md에서 이 문서를 분리.** Tower.md가 1031줄까지 불어 Core 문서 중 2위의 2배가 됐고, "현재 명세"와 "제안"이 섞여 읽을 때마다 사실/제안을 판단해야 했다. 폐기안 2개를 부록 A로 내리고 3분 요약을 신설 |
