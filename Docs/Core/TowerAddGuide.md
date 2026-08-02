# 새 타워 종류 추가하기 — 절차

> **이 문서는 "어떻게 하는가"다.** "왜 이런 구조인가 / 무엇이 어떻게 동작하는가"는
> [Tower.md](Tower.md)가 정본이고, 여기서는 손을 움직이는 순서만 다룬다.
> **기준 코드: #274 Phase 5 + Phase 4.5(투사체 비행 부품화) 이후** — 타워 구조가 바뀌면 이 문서부터 의심할 것.
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

부품 재고: 액션 3종(공격 / 버프 오라 / 디버프 오라) · 명중 3종(`Single`/`Area`/`Chain`) ·
비행 2종(`Homing`/`Ballistic`) · 효과 4종(`Burn`/`Poison`/`Slow`/`Stun`). 각각의 동작은
[Tower.md](Tower.md) §3.5(액션) · §3.7(비행·명중) · §3.8(효과).

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
| 5 | SO 수치 기입 | `Assets/Resources/ScriptableObjects/Towers/{ID}.asset` | 저장 시 `OnValidate` 경고 | 부모 저장소 |
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
| **`Actions` 리스트** | ✅ | **이 타워가 무엇을 하는지의 정본.** 인스펙터 `+`로 `Attack Action`(공격) / `Buff Aura Action`(아군 강화) / `Debuff Aura Action`(적 약화)을 담는다. **여러 개 담아도 된다** — 하이브리드가 그렇게 만들어진다 |
| `enemyLayerMask` | 공격 타워면 ✅ | 대상 탐색 레이어 |
| `firePoint` | 선택 | 발사 위치. 비우면 타워 루트에서 나간다 |
| `data`(TowerAsset) | 선택 | 채우면 3.5의 SO와 **같은 것**이어야 한다(다르면 경고 후 배치된 쪽으로 재조립) |

**확인** 3.5에서 SO에 이 프리팹을 물리고 저장하면 `OnValidate`가 액션↔수치 짝을 검사한다.
**⚠ 함정** ① **같은 타입의 액션을 둘 담지 말 것**(내부 소스 키 충돌 → 스탯·상태이상 슬롯을 서로 덮어씀)
② `Actions`가 비면 **예외도 경고도 없이** 아무 동작을 안 한다 ③ 커밋이 중첩 저장소로 간다([§2](#2-7단계-마스터-체크리스트)).
각 액션의 동작은 [Tower.md](Tower.md) §3.5.

### 3.4 고스트 프리팹

**어디서** 3.3과 같은 폴더, 이름 관례는 `{타워이름}_Ghost` 또는 `-GHOST`.
**무엇을** 배치 미리보기용 반투명 모델. **Collider가 없어야 한다.**
**확인** 배치 모드에서 마우스를 따라다니고 타일 위에서 유효/무효 색이 바뀐다.
**⚠ 함정** Collider가 붙어 있으면 자기 자신이 배치 레이캐스트를 가로채 타일을 못 짚는다. 3.3에 묻어서
잊기 쉬워 별도 단계로 세었다. 전제 목록 원문은 [TowerPlacement.md](TowerPlacement.md) §7 「전제(와이어링)」.

### 3.5 SO 수치 기입

**어디서** `Assets/Resources/ScriptableObjects/Towers/{TowerID}.asset`
**무엇을** 인스펙터 위에서부터:

- [ ] `TowerPrefab` / `GhostPrefab` — 3.3·3.4에서 만든 것
- [ ] `Cost` — 배치 비용(자원 SO + 수량, 여러 줄 가능)
- [ ] `Attack` — `AttackDamage` / `AttackRange` / `AttackInterval` / `ProjectilePrefab` /
      **`Flight`**(줄 오른쪽 드롭다운에서 `Homing` 또는 `Ballistic` 선택 → 그 안에 `Speed`·`ArcHeight`)
- [ ] `Impact` — `Single` / `Area`(+`SplashRadius`) / `Chain`(+`ChainRadius`·`MaxChainTargets`·`ChainDamageFalloff`)
- [ ] `BuffAura` — `Radius` + `Modifiers`(강화할 스탯·수치) / `DebuffAura` — `Radius` + `Interval`(재적용 주기)
- [ ] `Effects` — `+`로 `Burn`/`Poison`/`Slow`/`Stun`을 담고 **그 자리에서 수치 입력**.
      **공격 액션과 디버프 오라가 이 리스트를 공유**한다 — 같은 "화상"이 명중 효과도 되고 장판도 된다

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

**`TowerAction`**([TowerAction.cs](../../Assets/Scripts/CombatSystem/Tower/TowerAction.cs)) — 규칙
4가지가 그 파일 상단에 명문화돼 있다: ① 수치를 갖지 않는다(전부 SO) ② 씬 배선은 `Owner`를 통해 읽는다
③ 런타임 상태는 `[NonSerialized]` ④ 소스 키는 `SourceId`를 쓴다. 생명주기 규약은 [Tower.md](Tower.md) §3.3.

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
