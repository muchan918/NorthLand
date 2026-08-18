# 새 타워 종류 추가하기 — 절차

> **이 문서는 "어떻게 하는가"다.** "왜 이런 구조인가 / 무엇이 어떻게 동작하는가"는
> [Tower.md](Tower.md)가 정본이고, 여기서는 손을 움직이는 순서만 다룬다.
> **기준 코드: #300(성장형 램프업) 이후** — #274 Phase 5 + Phase 4.5(투사체 비행 부품화) ·
> #298(산탄·부메랑·빔) · #300(성장 액션·대상별 램프)까지 반영. 타워 구조가 바뀌면 이 문서부터 의심할 것.
> **대상**: §0~§5·§7은 기획·아트용(코드 지식 불필요), §6만 프로그래머용.
> 관련: [Tower.md](Tower.md)(명세) · [TowerPlacement.md](TowerPlacement.md)(배치) ·
> [TowerMerge.md](TowerMerge.md)(합성) · [DataTableManager.md](../Tools/DataTableManager.md)(CSV) ·
> [StringTable.md](../Tools/StringTable.md)(로컬라이제이션) · 이슈 #274
> [TowerRedesign.md](TowerRedesign.md) 폐기 시 **흡수처는 두 곳** — 명세는 `Tower.md`, 절차는 이 문서.

---

## 0. 30초 요약

**기존 거동을 재사용하는 타워는 코드 0줄이다.** CSV 한 줄, SO 하나, 프리팹 둘, 씬 등록 한 번.

1. [CSV 행 추가](#31-csv-행-추가) → 2. [Table Importer로 SO 스텁 생성](#32-table-importer-실행) →
3. [타워 프리팹](#33-타워-프리팹) → 4. [고스트 프리팹](#34-고스트-프리팹) →
5. [SO 수치 기입](#35-so-수치-기입) → 6. [씬 등록](#36-씬-등록--가장-많이-빠뜨리는-단계) →
7. [로컬라이제이션](#37-로컬라이제이션)

**코드가 필요한지 모르겠다** → [§1](#1-코드가-필요한가) · **막혔다** → [§5](#5-막혔을-때--증상--원인) ·
**없는 동작이 필요하다** → [§6](#6-코드가-필요할-때--확장점-3개) ·
**합성 결과 타워다** → [§7](#7-합성-결과-타워일-때--델타만)

---

## 1. 코드가 필요한가

이 표에서 만들려는 타워를 찾으면 **코드는 한 줄도 필요 없다.**

| 원하는 타워 | 조립 방법 |
|---|---|
| 스플래시 + 화상 | `AttackAction` · `Impact=Area` · `Effects=[Burn]` |
| 체인(연쇄) + 감속 | `AttackAction` · `Impact=Chain` · `Effects=[Slow]` |
| 독 장판 / 화상 장판 | `DebuffAuraAction` · `Effects=[Poison]` / `[Burn]` |
| 아군 공격력·공속·사거리 강화 | `BuffAuraAction` · `BuffAura.Modifiers` |
| **공격하면서 아군도 강화하는 하이브리드** | `AttackAction` **+** `BuffAuraAction` 둘 다 담기 |
| 다수를 동시에 지지는 지속딜 | `BeamAction` · `Beam.MaxTargets` > 1 |
| 한 대상을 오래 지질수록 아파지는 지속딜 | `BeamAction` · `Beam.MaxTargets` = 1 **+** `Beam.LockRamp` |
| 때릴수록·잡을수록 스스로 강해지는 타워 | `AttackAction` **+** `RampAction` · `Ramp`의 축·트리거 지정 |

부품 재고: 액션 **5종**(공격 / 버프 오라 / 디버프 오라 / 빔 / 성장) · 명중 3종(`Single`/`Area`/`Chain`) ·
비행 **4종**(`Homing`/`Ballistic`/`Straight`/`Boomerang`) · 효과 4종(`Burn`/`Poison`/`Slow`/`Stun`) ·
램프 수치 부품 `RampProfile`. 각각의 동작은 [Tower.md](Tower.md) §3.5(액션) · §3.7(비행·명중) ·
§3.8(효과) · §3.10(성장).

위 표로 표현되면 **코드 0줄**이니 [§2](#2-7단계-마스터-체크리스트)부터 그대로 따라간다.
없는 **효과**(빙결·출혈)·**궤적**(관통탄)·**행동 축**(아군 소환)이 필요하면 파생 클래스 1개를 먼저
만든다([§6](#6-코드가-필요할-때--확장점-3개)) — 그 경우에도 §2~§3 절차는 똑같이 밟는다.

---

## 2. 7단계 마스터 체크리스트

| # | 하는 일 | 어디서 | 안 하면 | 커밋 대상 |
|---|---|---|---|---|
| 1 | CSV 행 추가 | `Assets/Resources/DataTables/TowerTable.csv` | `TowerAsset.Data`가 null → 배치 시 LogError 후 **거부** | 부모 저장소 |
| 2 | SO 스텁 생성 | `Tools > Table Importer` | SO가 안 생긴다 — 손으로 만들어도 되는 **선택 단계** | 부모 저장소 |
| 3 | 타워 프리팹 | `Assets/Imported/@NorthLand/Prefabs/Tower/{이름}/` | `Actions`가 비면 배치는 되는데 **아무 동작도 안 한다** | ⚠ **중첩 저장소** |
| 4 | 고스트 프리팹 | 같은 폴더 | 배치 진입 자체가 LogError로 막힌다 | ⚠ **중첩 저장소** |
| 5 | SO 수치 기입 | `Assets/Resources/ScriptableObjects/Towers/{ID}.asset` | 저장 시 `OnValidate` 경고 · ⚠ **밸런싱 규약을 벗어나면 아무 신호가 없다**([§3.5](#35-so-수치-기입)) | 부모 저장소 |
| 6 | 씬 등록 | `GameScene`의 타워 선택 패널 | **게임에 영영 안 나온다** | ⚠ **씬** |
| 7 | 로컬라이제이션 키 | `NorthLand_Towers` 테이블 | 이름이 `towers.xxx.name`으로 그대로 보인다 | 부모 저장소 |

**순서를 지키는 이유**: SO부터 만들고 싶어지지만 안 된다. `GridWidth`/`GridHeight`(풋프린트)와 이름·설명
키는 **SO가 아니라 CSV에** 있고, SO는 그 행을 `TowerID`로 찾아 런타임에 물어온다. CSV 행이 없으면
`TowerAsset.Data`가 null이라 배치 코드가 풋프린트를 못 읽고 배치를 거부한다. 또 `Tools > Table Importer`가
CSV를 읽어 SO 껍데기를 만들어주므로, CSV를 먼저 쓰면 2단계가 공짜다.

> ⚠ **프리팹은 다른 git 저장소에 커밋된다.** 타워 프리팹이 사는 `Assets/Imported/`는 부모 저장소의
> `.gitignore`에 있고 **자체 중첩 git 저장소**(`muchan918/NorthLand-Imported`)다. 3·4단계 작업은
> 부모 저장소에 `git add`해도 잡히지 않으므로, **그 폴더에서 따로 커밋·푸시**해야 동료에게 전달된다.
> 부모 저장소 커밋 하나만 받은 사람에게는 "타워가 아무 동작도 안 한다"로 나타난다.
> 경위는 [Tower.md §6](Tower.md).

---

## 3. 단계별 상세

각 단계는 **어디서 / 무엇을 / 확인 / ⚠ 함정** 순으로 적혀 있다.

### 3.1 CSV 행 추가

**어디서** `Assets/Resources/DataTables/TowerTable.csv` — 헤더는 정확히 이 6컬럼이다.

```
TowerID,NameKey,GridWidth,GridHeight,RoleKey,DescriptionKey
archer_tower,towers.archer.name,1,1,towers.archer.role,towers.archer.desc
```

**무엇을** 수치 컬럼은 하나도 없다 — 공격력·사거리 같은 밸런싱 값은 전부 SO(3.5)에 적는다.
`TowerID`는 SO 파일명이자 합성 매칭 키라 **전 파이프라인의 조인 키**다.
**확인** Unity로 돌아오면 도메인 리로드 때 자동 반영된다(임포트 불필요).
**⚠ 함정** ① **파일 끝에 개행이 없다**(현재 마지막 행 `soda_tower`) — 그냥 이어 쓰면 행이 붙어 깨지므로
줄바꿈을 먼저 넣을 것 ② `TowerID`는 **소문자 스네이크**가 관례다(기존 `Sniper_tower`가 대문자 혼용인데
따라하지 말 것) ③ 값에 쉼표가 들어가면 RFC4180 큰따옴표로 감싼다
([DataTableManager.md](../Tools/DataTableManager.md) §3) ④ `TowerID` 중복은 LogError로 잡힌다.

### 3.2 Table Importer 실행

**어디서** Unity 메뉴 `Tools > Table Importer` → `Tower` → `Import`
**무엇을** `Assets/Resources/ScriptableObjects/Towers/{TowerID}.asset`이 없으면 만들어준다.
**동기화하는 필드는 `TowerID` 하나뿐**이라 이미 수치를 채운 SO가 있어도 **재실행이 안전하다**
(고아 SO를 지우지도 않는다).
**확인** 위 경로에 `{TowerID}.asset`이 생긴다.
**⚠ 함정** 편의 단계일 뿐이다 — `Create > Scriptable Objects > TowerAsset`으로 손수 만들고
`TowerID`만 맞춰도 동일하다.

### 3.3 타워 프리팹

**어디서** `Assets/Imported/@NorthLand/Prefabs/Tower/{타워이름}/` — 타워당 폴더 1개가 관례이고 본체·고스트·탄환이 함께 산다(예: `ArcherTower/`).
**무엇을** 루트 GameObject에:

| 항목 | 필수 | 비고 |
|---|---|---|
| `Tower` 컴포넌트 | ✅ | 없으면 배치 시 LogError |
| Collider | ✅ | 클릭 선택용. 레이어가 `MouseManager._selectableMask`에 포함돼야 한다 |
| **`Actions` 리스트** | ✅ | **이 타워가 무엇을 하는지의 정본.** 인스펙터 `+`로 `Attack Action`(공격) / `Buff Aura Action`(아군 강화) / `Debuff Aura Action`(적 약화) / `Beam Action`(다중 잠금 지속딜) / `Ramp Action`(자가 성장)을 담는다. **여러 개 담아도 된다** — 하이브리드가 그렇게 만들어진다(성장 타워 = 공격 + 성장) |
| `enemyLayerMask` | 공격 타워면 ✅ | 대상 탐색 레이어 |
| `firePoint` | 선택 | 발사 위치. 비우면 타워 루트에서 나간다 |
| `data`(TowerAsset) | 선택 | 채우면 3.5의 SO와 **같은 것**이어야 한다(다르면 경고 후 배치된 쪽으로 재조립) |
| `TowerAnimationVisual` | 모델에 Animator가 있으면 ✅ | **연출 전용.** 모델 팩 컨트롤러는 파라미터가 전부 Trigger라 **누가 켜주지 않으면 영원히 Idle만 돈다.** 발사는 `fireState`(상태 직접 재생, 연사에 권장) 또는 `fireTrigger`. ⚠ **발사 클립이 루프면 정지 경로를 반드시 저작할 것** — 아래 함정 ④ |
| `TowerTurretAim` | 포탑 마디가 있으면 선택 | **연출 전용**(선회). 다만 `turret`을 물려야 `TargetLost`가 발행된다 — 그 신호가 위 정지 경로의 **유일한 출처**다 |

**확인** 3.5에서 SO에 이 프리팹을 물리고 저장하면 `OnValidate`가 액션↔수치 짝을 검사한다.
연출 컴포넌트 배선은 `Awake`가 검사해 콘솔 경고로 알린다(Animator 없음 / `turret` 미할당 / 루프 발사 클립 + 정지 경로 없음).
**⚠ 함정** ① **같은 타입의 액션을 둘 담지 말 것**(내부 소스 키 충돌 → 스탯·상태이상 슬롯을 서로 덮어씀)
② `Actions`가 비면 **예외도 경고도 없이** 아무 동작을 안 한다 ③ 커밋이 중첩 저장소로 간다([§2](#2-7단계-마스터-체크리스트)).
④ **모델 팩마다 `Fire` 저작이 정반대다.** FattyPoly Part2(CrossBow·Culverin)는 `Fire → Idle`에 exitTime 전이가
있어 클립이 끝나면 스스로 돌아오지만, **Part4(MachineGun·Minigun)는 `Fire` 클립이 `m_LoopTime: 1`이고
`Fire`에서 나가는 전이가 전부 조건부(`Idle`/`Reload`/`Remove`)라 무조건 탈출이 없다.** 후자에서 정지를
저작하지 않으면 적이 사라져도 발사 모션이 밤새 반복된다 — `idleTrigger`를 채우거나
(`playReloadOnTargetLost` + `reloadTrigger`)를 켜고 **`TowerTurretAim.turret`을 함께 물릴 것**(WL-193).
⑤ **탄환 프리팹을 여러 타워가 공유하면 소유자를 정할 것.** 현재 `Rolly_Bullet`
하나를 소다·화염아처·스나이퍼·킬스택 4종이 함께 물고 있어, 그 프리팹이나 그것이 참조하는 벤더 FBX를
건드리면 4종이 함께 흔들린다. 벤더 팩 재임포트가 메시를 같은 GUID로 덮어쓰는 일이 실제로 있으므로
(2026-08-18 `Bullet00.FBX` 28320→25024), **공용 탄환은 팩 밖(`@NorthLand`)으로 복제해 소유하는 쪽이 안전하다.**
각 액션의 동작은 [Tower.md](Tower.md) §3.5.

### 3.4 고스트 프리팹

**어디서** 3.3과 같은 폴더, 이름 관례는 `{타워이름}_Ghost` 또는 `-GHOST`.
**무엇을** 배치 미리보기용 반투명 모델. **Collider가 없어야 한다.**
**확인** 배치 모드에서 마우스를 따라다니고 타일 위에서 유효/무효 색이 바뀐다.
**⚠ 함정** Collider가 붙어 있으면 자기 자신이 배치 레이캐스트를 가로채 타일을 못 짚는다. 3.3에 묻어서
잊기 쉬워 별도 단계로 세었다. 전제 목록 원문은 [TowerPlacement.md](TowerPlacement.md) §7 「전제(와이어링)」.

### 3.5 SO 수치 기입

**어디서** `Assets/Resources/ScriptableObjects/Towers/{TowerID}.asset`

> ### ⚠ 값을 적기 전에 — 밸런싱 규약부터 확인한다 (#326)
> 수치를 감으로 적으면 **타워마다 자기 안에서만 말이 되는 값**이 된다. 그 상태를 정리한 것이
> [CombatBalance.md](CombatBalance.md)이고, 아래 세 줄이 그 요약이다.
>
> ⚠ **셋 중 하나만 `OnValidate`가 잡는다** — 나머지 둘은 어겨도 저장이 조용히 통과한다.
>
> | | 항목 | 강제 |
> |---|---|---|
> | ① | `공격 간격(초) ≤ 사거리(타일) ÷ 3` | ✅ **`OnValidate` 경고**(`TowerAsset.cs:158-177`) |
> | ② | 합성 결과 킬 수 > 재료 킬 수의 합 | ❌ 사람이 확인 |
> | ③ | 티어 밴드에서 눈금 선택 | ❌ 사람이 확인 |
>
> **1. 밴드에서 목표 킬 수를 고른다**(③) — [§6.0 눈금표](CombatBalance.md). 티어(직접배치 1차 /
> 합성 2차 / 합성 산물을 재료로 쓰면 3차…)를 정하고 그 밴드 안에서 눈금 하나를 고른다.
> 재료 자원 합이 클수록 높게.
>
> **2. [§6.1 환산식](CombatBalance.md)으로 발당 피해를 계산한다** — 감으로 적지 않는다.
>
> **3. 규약 ①을 확인한다** — 1타일 = 6유닛. 어기면 적이 사거리를 지나는 동안 발사 횟수가 1~2발에
> 그쳐 **쿨다운 위상에 따라 쏘거나 안 쏘거나** 한다(정수 오차 ±50% 이상).
> 이건 저장할 때 경고가 뜨므로 잊어도 잡힌다.
>
> **4. 합성 결과 타워라면 규약 ②를 확인한다**(②) — 재료 타워 킬 수의 합보다 강해야 한다. 아니면
> 합성이 순손실이라 아무도 안 만든다. 상한은 재료 합 × 1.3.
> ⚠ **여기가 가장 조용히 깨진다** — #326 이전에 2차 `Sniper`가 3차 `killstack`(재료가 스나이퍼)보다
> 강했는데 아무 신호도 없었다.

**무엇을** 인스펙터 위에서부터:

- [ ] `TowerPrefab` / `GhostPrefab` — 3.3·3.4에서 만든 것
- [ ] `Cost` — 배치 비용(자원 SO + 수량, 여러 줄 가능)
- [ ] `Attack` — `AttackDamage` / `AttackRange` / `AttackInterval` / `ProjectilePrefab` /
      **`Flight`**(줄 오른쪽 드롭다운에서 `Homing` 또는 `Ballistic` 선택 → 그 안에 `Speed`·`ArcHeight`)
- [ ] `Impact` — `Single` / `Area`(+`SplashRadius`) / `Chain`(+`ChainRadius`·`MaxChainTargets`·`ChainDamageFalloff`)
- [ ] `BuffAura` — `Radius` + `Modifiers`(강화할 스탯·수치) / `DebuffAura` — `Radius` + `Interval`(재적용 주기)
- [ ] `Beam` — `Range` / `MaxTargets` / `TickInterval` / `DamagePerTick`, 그리고 대상별 성장을 줄 거면
      `LockRamp`(`PerStack`·`MaxStacks`·`StackInterval`). **`MaxTargets`와 `LockRamp`만으로 멀티/단일
      인페르노가 갈린다** — 액션은 같다
- [ ] `Ramp` — 타워 전체가 성장할 거면 `Stat`(무엇이 오르는가) · `Trigger`(`Hit`/`Kill`) ·
      `Profile`(`PerStack`·`MaxStacks`·`DecaySeconds`). ⚠ `DecaySeconds = 0`은 "영구"가 아니라
      "웨이브 동안 유지"다 — 성장은 웨이브 종료에 일괄 초기화된다
- [ ] `Attack.BurstCount` / `BurstInterval` — 한 사이클에 **시간차로** 여러 발을 쏠 거면(#336).
      산탄(`PelletCount`)과 **다른 축이다** — 산탄은 한 순간에 부채꼴로, 연발은 같은 조준으로 시간을 두고
      나간다. 그래서 착탄 지점이 갈리고 착탄 구역이 발수만큼 생긴다. 기본값 1 = 기존 거동
- [ ] `GroundZone` — 착탄 지점에 지속 구역을 남길 거면 `ZonePrefab` / `Radius` / `Duration` / `Interval`.
      **저작 여부의 판정 기준은 `ZonePrefab` 하나**다(수치 기본값이 0이라 그렇지 않으면 전 타워에서 경고가 뜬다).
      ⚠ **판정 반경과 이펙트의 보이는 크기는 자동으로 맞지 않는다** — 반경은 여기, 보이는 크기는 프리팹
      스케일이 정한다. ⚠ **회전은 프리팹이 저작한 것을 그대로 쓴다** — 씬에 그냥 놨을 때 바닥에 눕는
      상태로 저작돼 있어야 한다(파티클 팩은 루트 회전을 자식이 상쇄하는 형태가 흔해, 덮으면 판이 90° 선다)
- [ ] `Effects` — `+`로 `Burn`/`Poison`/`Slow`/`Stun`을 담고 **그 자리에서 수치 입력**.
      **공격 액션·디버프 오라·착탄 구역이 이 리스트를 공유**한다 — 같은 "화상"이 명중 효과도 되고
      타워 중심 장판도 되고 착탄 구역도 된다

탄환 프리팹에는 수치를 넣지 않는다(모델 축 보정 하나뿐). 근거는 [Tower.md](Tower.md) §3.7·§3.8.
**확인** 저장(Ctrl+S) 시 Console에 `[TowerAsset]` 경고가 없으면 통과 → [§4](#4-검증--3개의-관문).
**⚠ 함정 — 복제할 SO를 고를 때**

| | 에셋 | 왜 |
|---|---|---|
| ✅ | `cannon_tower` | 공격 + `Impact=Area` + `Flight` 저작 완료. 공격 타워 표준 |
| ✅ | `poison_tower` · `choco_tower` | 오라 + `Effects` 저작 완료. 오라 타워 표준 |
| ❌ | `haste_tower` · `lightning_tower` | **미마이그레이션.** 삭제된 `TowerType`·`Single:`/`Area:` 래퍼 키가 아직 남아 있어 오독을 부른다 |

### 3.6 씬 등록 — 가장 많이 빠뜨리는 단계

**어디서** 정본 `GameScene`의 타워 선택 패널 → `TowerSelectPanelView`의 `타워 목록`
([TowerSelectPanelView.cs:23](../../Assets/Scripts/UI/TowerPanel/TowerSelectPanelView.cs))
**무엇을** 새 SO를 리스트에 드래그한다.
**확인** Play → 하단 선택 패널에 버튼이 하나 늘어난다.

> **레지스트리도 `Resources.Load`도 없다.** SO의 유일한 발견 경로가 인스펙터 참조라, 이걸 안 하면
> 에셋이 완벽해도 **게임에 영영 나오지 않는다.** (`Towers/`가 `Resources/` 아래 있는 것은 잔재다.)

**⚠ 함정** 씬 편집이므로 [SceneWorkflow.md](SceneWorkflow.md)의 정본/개인 복사본 규칙을 따를 것 —
개인 복사본에만 등록하면 동료 화면에는 안 나온다.

### 3.7 로컬라이제이션

**어디서** `Window > Asset Management > Localization Tables` → 테이블 **`NorthLand_Towers`**
**무엇을** 키 3개 × 로케일 3개(`ko-KR`/`en-US`/`ja-JP`). **CSV에 적은 키 문자열과 글자 그대로 일치**해야 한다.

| 키 | CSV 컬럼 | 쓰이는 곳 |
|---|---|---|
| `towers.{id}.name` | `NameKey` | 툴팁 헤더 · 합성 패널 |
| `towers.{id}.role` | `RoleKey` | 툴팁 헤더 뒤 "이름 - 역할" |
| `towers.{id}.desc` | `DescriptionKey` | 타워 정보 패널 |

**확인** Play → 툴팁·정보 패널에 한국어 이름이 뜬다(키 문자열이 그대로 보이면 실패).
**⚠ 함정** 오라 타워는 공통 공격 스탯이 없어 **설명(`desc`)이 사실상 유일한 정보**다 — 비워두지 말 것.
절차 상세는 [StringTable.md](../Tools/StringTable.md) §5.

---

## 4. 검증 — 3개의 관문

### ① 저장 시점 — `TowerAsset.OnValidate`

SO를 저장하면 아래 조합을 경고한다. **전부 "예외도 없이 조용히 아무 일도 안 일어나는" 조합**이라
이 경고가 유일한 방어선이다. 본 경고 → 돌아갈 단계:

| Console 경고(앞부분) | 돌아갈 곳 |
|---|---|
| `TowerPrefab '…'에 Tower 컴포넌트가 없습니다` | [3.3](#33-타워-프리팹) |
| `프리팹 Actions[n]가 비었습니다` | [3.3](#33-타워-프리팹) — 클래스 rename 흔적. `[MovedFrom]` 필요 |
| `프리팹에 …Action이(가) 둘 이상입니다` | [3.3](#33-타워-프리팹) — 같은 타입 중복 제거 |
| `프리팹에 AttackAction이 있는데 공격 수치가 비었습니다` | [3.5](#35-so-수치-기입) `Attack` |
| `공격 수치를 적었는데 프리팹에 AttackAction이 없습니다` | [3.3](#33-타워-프리팹) `Actions` |
| `Attack.Flight(비행 방식)가 비었습니다` | [3.5](#35-so-수치-기입) `Flight` 드롭다운 |
| `BuffAuraAction이 있는데 BuffAura.Radius가 0입니다` (Debuff도 동일) | [3.5](#35-so-수치-기입) 오라 `Radius` |
| `Impact=Area인데 SplashRadius가 0입니다` | [3.5](#35-so-수치-기입) |
| `Impact=Chain인데 MaxChainTargets가 …입니다` | [3.5](#35-so-수치-기입) |
| `PelletCount>1인데 …`(Flight·Impact·SpreadAngle 3종) | [3.5](#35-so-수치-기입) 산탄 저작 규칙(#298) |
| `Flight=BoomerangFlight인데 Impact=Area가 아닙니다` / `SplashRadius가 … HitRadius보다 좁습니다` | [3.5](#35-so-수치-기입) |
| `BeamAction이 있는데 Beam 수치가 비었습니다` (`MaxTargets`·`TickInterval` 경고도 동일) | [3.5](#35-so-수치-기입) `Beam` |
| `RampAction이 있는데 Ramp 수치가 비었습니다` (역방향 경고도 동일) | [3.5](#35-so-수치-기입) `Ramp` |
| `Ramp.Trigger=Hit인데 프리팹에 AttackAction이 없습니다` | 명중 통지(`Projectile.DamageDealt`)는 **투사체 공격만** 발행한다 — 빔 타워면 `Trigger=Kill` 또는 `Beam.LockRamp`를 쓴다 |
| `Beam.LockRamp를 적었는데 BeamAction이 없습니다` / `StackInterval이 0 이하입니다` | [3.5](#35-so-수치-기입) `Beam.LockRamp` |
| `BurstCount(n)>1인데 BurstInterval이 0입니다` | [3.5](#35-so-수치-기입) `Attack.BurstInterval` — 시간차가 없으면 산탄과 같아지고 착탄 구역도 한 점에 겹친다(#336) |
| `BurstCount를 적었는데 프리팹에 AttackAction이 없습니다` | [3.3](#33-타워-프리팹) `Actions` |
| `GroundZone.ZonePrefab은 지정됐는데 Radius/Duration/Interval 중 0이 있습니다` | [3.5](#35-so-수치-기입) `GroundZone` — 저작 판정은 `ZonePrefab` 하나로 하므로 수치가 0이면 구역이 생기지 않거나 생겨도 아무도 안 맞는다(#336) |
| `착탄 구역을 저작했는데 프리팹에 AttackAction이 없습니다` | [3.3](#33-타워-프리팹) — 착탄이 발생하지 않으므로 구역이 **영영** 생기지 않는다 |
| `착탄 구역을 저작했는데 Effects가 비어 있습니다` | [3.5](#35-so-수치-기입) `Effects` — 구역은 "어디에·얼마나 오래·얼마나 자주"만 알고 **무엇을 거는지는 `Effects`가 소유한다**(화상 수치 포함) |

> `TowerPrefab`이 비어 있으면 검증을 **건너뛴다**(저작 도중 경고 폭탄 방지). 즉 프리팹을 안 물린 SO는
> 경고도 안 난다 — `lightning_tower`가 전 필드 0인 채 조용한 이유다. 근거는 [Tower.md](Tower.md) §4.3.

### ② 컴파일 — 코드를 건드렸을 때만([§6](#6-코드가-필요할-때--확장점-3개))

```bash
unity-cli editor refresh --compile
```

이어서 `unity-cli console --type error`로 **에러 0**을 확인한다([unity-cli-guide.md](../Tools/unity-cli-guide.md) §2).

### ③ 인게임

- [ ] 낮에 하단 선택 패널에 버튼이 뜬다(자원이 부족하면 비활성 — 정상)
- [ ] 클릭 → 고스트가 따라오고 타일 위에서 유효/무효 색이 바뀐다 → 배치된다
- [ ] 버프 오라는 **낮에도** 동작(정보 패널 스탯이 오름) / 공격·디버프 오라는 **밤에만** 동작
- [ ] 타워를 클릭하면 정보 패널에 이름·역할·설명이 한국어로 뜬다

> 플레이 모드는 비용이 커서, 에이전트가 대신 검증할 때는 **사용자가 명시적으로 요청한 경우에만** 돌린다
> ([unity-cli-guide.md](../Tools/unity-cli-guide.md) 규칙 A8).

---

## 5. 막혔을 때 — 증상 → 원인

| 증상 | 짚어볼 곳 |
|---|---|
| 선택 패널에 버튼이 안 뜬다 | [3.6](#36-씬-등록--가장-많이-빠뜨리는-단계) 씬 등록 (또는 정본이 아닌 개인 복사본을 열고 있음) |
| 배치를 눌렀는데 LogError + 거부 | [3.1](#31-csv-행-추가) CSV 행 없음 → `TowerAsset.Data`가 null |
| 배치는 되는데 아무것도 안 한다 | [3.3](#33-타워-프리팹) `Actions` 빔. **경고도 예외도 안 난다** — 먼저 `Assets/Imported/`가 최신인지 확인([§2](#2-7단계-마스터-체크리스트)) |
| 고스트가 타일을 못 짚는다 | [3.4](#34-고스트-프리팹) 고스트에 Collider가 붙어 있음 |
| 타워를 클릭해도 선택이 안 된다 | [3.3](#33-타워-프리팹) Collider 없음 또는 레이어가 `_selectableMask` 밖 |
| 낮엔 되는데 밤에 안 쏜다(또는 반대) | 페이즈 게이팅이 액션별로 다르다 → [Tower.md §3.5](Tower.md) |
| 모션이 아예 안 난다 | [3.3](#33-타워-프리팹) `TowerAnimationVisual` 미부착·Animator 미할당. 콘솔에 `[TowerAnimationVisual]` 경고가 있는지 먼저 볼 것 |
| **발사 모션이 안 멈춘다**(적이 사라져도 반복) | [3.3](#33-타워-프리팹) 함정 ④ — 팩의 `Fire` 클립이 Loop인데 정지 경로 미저작. `idleTrigger` 또는 (`playReloadOnTargetLost`+`reloadTrigger`) **＋ `TowerTurretAim.turret`** |
| '적 소실 시' 마무리 연출이 안 난다 | [3.3](#33-타워-프리팹) `TowerTurretAim.turret` 미할당 → `TargetLost`가 영영 발행되지 않는다(`LateUpdate` 조기 반환) |
| 탄환 외형이 나도 모르게 바뀌었다 | [3.3](#33-타워-프리팹) 함정 ⑤ — 공용 탄환 프리팹/벤더 FBX가 재임포트로 덮어써졌는지 |
| 이름이 `towers.xxx.name`으로 보인다 | [3.7](#37-로컬라이제이션) 키 누락 또는 CSV 키 문자열 오타 |
| 효과(화상·감속 등)가 안 걸린다 | [3.5](#35-so-수치-기입) `Effects` 빔. 합성 결과 타워라면 계승 축 → [§7](#7-합성-결과-타워일-때--델타만) |
| 내 프리팹 작업이 동료 환경에 없다 | [§2](#2-7단계-마스터-체크리스트) 중첩 저장소 — 커밋이 갈라진다 |

---

## 6. 코드가 필요할 때 — 확장점 3개

확장점은 **정확히 3개**이고, 어느 쪽이든 **파생 클래스 1개**로 끝난다.
`Tower.cs`·`Projectile.cs`는 설계상 무수정이다.

| 확장점 | 언제 | 만드는 것 | 어디에 담기나 |
|---|---|---|---|
| `HitEffect` | 새 상태이상(빙결·출혈·방어력 감소) | `HitEffect` 파생 1개 + `EffectKind`에 값 추가 | SO의 `Effects` 리스트 |
| `ProjectileFlight` | 새 궤적(관통탄·부메랑) | `ProjectileFlight` 파생 1개 | SO의 `Attack.Flight` |
| `TowerAction` | 새 행동 축(아군 소환, 자원 생산) | `TowerAction` 파생 1개 | **프리팹**의 `Actions` 리스트 |

> **먼저 확인할 것 — 새 액션이 정말 필요한가.** #300의 성장 타워 3종은 **액션 1개(`RampAction`)로 셋을
> 다 만들었다.** 명중 램프와 처치 램프는 트리거만 다르고(SO의 `Ramp.Trigger`), 단일 인페르노는 아예
> 액션을 추가하지 않고 기존 `BeamAction`에 `Beam.LockRamp` 저작만 얹은 것이다. "거동이 다르다"가
> 곧 "액션이 다르다"는 아니다 — `AttackAction` 하나가 단일·스플래시·체인·산탄·부메랑을 전부 덮는 것과
> 같은 축이다. **트리거·수치로 갈릴 수 있으면 액션을 늘리지 않는다.**
>
> ⚠ **스탯을 바꾸는 거동이면 액션을 만들기 전에 원장을 볼 것.** 공격·빔·DoT 수치가 이미 전부
> `Owner.Stats.Evaluate`를 통과하므로, `TowerStats`에 소스를 얹는 것만으로 정보 패널·사거리 원까지
> 자동으로 따라온다(#300의 원장형 램프가 기존 액션을 한 줄도 고치지 않은 이유). 반대로 **대상별로
> 달라야 하는 값은 원장에 넣으면 안 된다** — 원장은 타워 단위다([Tower.md](Tower.md) §3.10).

**`HitEffect`**([HitEffect.cs](../../Assets/Scripts/CombatSystem/StatusEffect/HitEffect.cs)) — 지속
피해류는 `DamageOverTimeEffect`를 상속하면 `Kind` 한 줄로 끝난다. 공격 액션과 디버프 오라가 같은
리스트를 공유하므로 **하나 만들면 양쪽에서 쓴다.** ⚠ 기존 `Effects` 리스트의 **순서를 섞거나 항목을
지우지 말 것** — 소스 키가 `Kind`로 채번돼 진행 중이던 효과가 대상 쪽에 회수 불가 유령으로 남는다
([Tower.md](Tower.md) §3.8).

**`ProjectileFlight`**([ProjectileFlight.cs](../../Assets/Scripts/CombatSystem/Tower/ProjectileFlight.cs))
— ⚠ **무상태여야 한다.** 부품 하나를 그 타워가 쏜 투사체 전부가 공유하므로 진행값은 필드가 아니라
`ref FlightState`에 담는다(액션과 정반대). 반환하는 `FlightStep`의 `Impact`와 `Finished`가 분리돼 있어
"여러 번 명중 후 소멸"(관통·부메랑)이 표현된다([Tower.md](Tower.md) §3.7). 단일 필드라 Unity가 타입
피커를 안 주지만 `ManagedReferencePickerDrawer`가 이미 그 자리를 메워, 새 파생은 자동으로 드롭다운에 뜬다.
>
> ⚠ **유도 없이 고정 방향으로만 나는 비행 방식**(`StraightFlight`/`BoomerangFlight`처럼 발사 순간
> 방향을 스냅샷하고 이후 대상을 안 쫓는 것)도 **발사 시 3D 조준을 받는다**(#386). 예전에는
> `AttackAction.TryAttack`이 `aimDir.y`를 0으로 눌러 "발사 높이가 곧 평생 비행 높이"였고, 그래서
> **"`firePoint`를 월드 Y 2~3으로 낮춰둘 것"이 이 자리의 규약이었다 — 그 우회책은 없어졌다.**
> 타워는 Grass 타일 윗면(StartMap 실측 3.80, 사거리 버프타일은 5.00·6.20·7.40)에 앉고 몬스터는
> Road 윗면(0.80)을 걷는데, 수평으로 쏘면 그 높이차만큼 머리 위를 지나가는 것이 #386의 정체였다.
>
> 그래서 **`firePoint` 높이는 연출 기준으로 잡으면 된다.** 단 두 가지가 남는다.
> ① 조준점의 정본은 몬스터 프리팹의 `hitPosition`이다 — 그 저작이 전제다(§4.8 「몬스터
> `hitPosition` 동기화 계약」). 미할당은 `Enemy.Awake`가 `LogError`로 잡지만 **할당돼 있고 위치가
> 틀린 경우는 로그가 없다.**
> ② 펠릿의 사거리 예산(`self.Range`)은 **3D 경로 길이로 소모**되므로 머즐이 높으면 수평 도달이
> 줄어든다 — `수평 도달 = √(Range² − 낙차²)`. 사거리 17에서 낙차 2.2(잔디 상면 → 몬스터 몸통)는
> 16.9로 1% 미만이지만, 낙차 8이면 15.0(−12%), 낙차 12면 12.0(−29%)까지 준다. 아처 계열처럼
> 유도탄 전제로 높게 잡힌 `firePoint`를 고정 방향 비행에 그대로 복제하면 이 손실이 눈에 띈다.

**`TowerAction`**([TowerAction.cs](../../Assets/Scripts/CombatSystem/Tower/TowerAction.cs)) — 규칙
4가지가 그 파일 상단에 명문화돼 있다: ① 수치를 갖지 않는다(전부 SO) ② 씬 배선은 `Owner`를 통해 읽는다
③ 런타임 상태는 `[NonSerialized]` ④ 소스 키는 `SourceId`를 쓴다. 생명주기 규약은 [Tower.md](Tower.md) §3.3.
>
> ⚠ **웨이브를 넘겨선 안 되는 상태가 있으면 `OnWaveEnd()`를 구현할 것**(#300). 기본 no-op이라 잊어도
> 컴파일은 되지만, 특히 `NightOnly` 액션은 **낮에 `Tick`이 아예 돌지 않으므로 스스로 정리할 기회가
> 없다** — 밤 마지막 프레임의 상태(진행 중인 잠금, 켜진 `LineRenderer`)가 그대로 굳는다.
> `BeamAction`이 실제로 이 문제를 갖고 있었다(#298 → #300에서 해소). 페이즈 통지는 호스트가 주므로
> 액션이 `DayNightManager`를 구독하면 안 된다(WL-044).

> ⚠ **히트스캔(빔)형 공격 액션은 투사체 기반 보상 효과에서 제외된다.** 버프 화상(#169,
> [BurnBuff.cs](../../Assets/Scripts/Skill/BurnBuff.cs))처럼 `Projectile.DamageDealt` 이벤트를 구독하는
> 보상 효과는 실제 `Projectile`을 생성하는 공격에만 적용된다. `BeamAction`처럼 `LineRenderer`로
> 즉시 판정하는 히트스캔 공격은 이 이벤트를 발행하지 않아 자동으로 제외되고, 이는 버그가 아니라
> 설계 의도다(보상 텍스트도 "투사체 타워"로 스코프됨). **향후 체이닝 타워·빔형 공격 등 새 히트스캔
> `TowerAction`을 추가할 때도 기본적으로 같은 축에서 제외된다** — 그 공격에도 이 보상이 적용되길
> 원하면 해당 액션의 `ApplyToLocked`(또는 동급 지점)에서 같은 `Projectile.DamageDealt`를 직접
> 발행해야 한다.

> **공통** — 인스펙터 드롭다운에 뜨려면 **구상 클래스 · 비제네릭 · public 무인자 생성자**여야 한다.
> 클래스를 rename하면 기존 에셋·프리팹에 null 항목이 남으므로 `[MovedFrom]`을 붙일 것.

---

## 7. 합성 결과 타워일 때 — 델타만

합성 결과도 **평범한 `TowerAsset`**이다. §3의 7단계를 밟되
**[3.6 씬 등록](#36-씬-등록--가장-많이-빠뜨리는-단계)은 건너뛰고**(선택 패널에 올리면 아래 ⚠ 때문에
계승이 깨진다) 아래를 더한다.

1. `TowerRecipe` SO 생성 — `Assets/Resources/ScriptableObjects/TowerRecipes/`
   (`Create > Scriptable Objects > TowerRecipe`). `Materials`(재료 SO + 개수) · `Result`(결과 SO) ·
   `ExtraCost`(합성 추가 비용)를 채운다.
2. **씬의 합성 패널에 등록** — `TowerMergePanelView`의 `_recipes` 배열
   ([TowerMergePanelView.cs:26](../../Assets/Scripts/UI/TowerPanel/TowerMergePanelView.cs)).
   3.6과 같은 이유로, 등록 안 하면 후보 버튼이 안 뜬다.
3. `InheritEffects` — 켜면 재료가 갖고 있던 효과의 **종류만** 결과 타워가 물려받는다(수치는 결과 SO에
   적힌 값). 결과 SO의 `Effects`에 그 종류가 **미리 정의돼 있어야** 켤 수치가 있다.
   저장 시 `TowerRecipe.OnValidate`가 재료↔결과 불일치를 경고한다.

> ⚠ **결과 SO로 기존 프로덕션 타워를 쓰지 말 것.** 계승 필터는 합성으로 만들어진 인스턴스에만 걸린다 —
> 같은 SO를 선택 패널에서 평범하게 배치하면 `Effects`에 적힌 효과가 **전부 켜진 채로** 나온다.
> 결과 타워는 합성으로만 나오는 전용 SO여야 한다.

레시피 데이터 모델·매칭 규칙·씬 배선은 [TowerMerge.md](TowerMerge.md) §5·§6·§13·부록 A.

---

## 8. 낡아서 믿으면 안 되는 것

- **[StringTable.md](../Tools/StringTable.md) §2·§4** — "테이블 `NorthLand_default` 1개, 키 1개"라고
  적혀 있지만 실제로는 `NorthLand_Towers` 포함 6개다. **§5 신규 키 추가 절차만 유효.**
- **`haste_tower.asset` · `lightning_tower.asset`** — 미마이그레이션 SO. 복제 금지([3.5](#35-so-수치-기입)).
- **`archer_tower.asset`의 프리팹 참조** — `ArcherTower`가 아니라 **`RollyShooter`**를 물고 있다.
  이름으로 프리팹을 찾으면 헷갈리는 자리다.
- **[TowerRedesign.md](TowerRedesign.md)** — #274 제안 문서이고 폐기 예정이다. 절차는 이 문서가 정본.

---

## 부록. 개정 이력

| 개정 | 내용 |
|---|---|
| 초판 (#274) | `TowerRedesign.md` §11(제안 시제·4행 표)을 이관해 실측 절차서로 재작성. 7단계 체크리스트·`OnValidate` 경고 역인덱스·증상 역인덱스·확장점 3개 신설 |
| 2차 (#300) | 부품 재고를 실측치로 정정 — 액션 3종 → **5종**(`BeamAction`·`RampAction`), 비행 2종 → **4종**(`Straight`·`Boomerang`). #298·#300에서 늘어난 분이 §1에 반영돼 있지 않았다. §1 조립표에 빔·램프 3행 추가. §3.5 체크리스트에 `Beam`·`Ramp` 항목 추가(⚠ `DecaySeconds=0`은 "영구"가 아니라 "웨이브 동안 유지"). §4① `OnValidate` 역인덱스에 산탄·부메랑·빔·램프 경고 7행 추가. §6에 **"새 액션이 정말 필요한가"** 경고 신설 — #300은 타워 3종을 액션 1개로 만들었고(트리거·수치로 갈림) 단일 인페르노는 액션 추가 0이다. 스탯을 바꾸는 거동은 액션보다 원장을 먼저 보라는 지침과, `OnWaveEnd`를 구현하지 않으면 `NightOnly` 액션이 낮에 상태를 정리할 기회가 없다는 경고 추가 |
