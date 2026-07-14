# 데이터 테이블 매니저 (Data Table Manager)

CSV로 정의한 게임 데이터(자원, 건물 등)를 `DataTableManager`가 어떻게 로드·제공하는지,
그리고 지금까지 구현/논의된 내용을 정리한 문서. 새로운 데이터 종류를 추가하거나
기존 데이터를 코드에서 참조할 때 참고 용도로 쓴다. (관련 이슈: [#4](https://github.com/muchan918/NorthLand/issues/4))

완료 기준: (1) CSV → 게임 데이터 객체 변환이 동작한다 (2) 최소 1종 이상의 데이터가 CSV로
정의·로드된다. 자원(Resource) 1종 구현으로 두 조건 모두 충족.

## 1. 아키텍처

```
DataTableManager (static)     ─ 테이블 이름 → DataTable 인스턴스 레지스트리, 최초 접근 시 전체 로드
  └─ XxxTable : DataTable     ─ CSV 한 장에 대응, Id로 개별 행 조회 (Get(id))
       └─ XxxData             ─ CSV 한 행에 대응하는 순수 데이터 객체 (POCO)

XxxAsset : ScriptableObject    ─ CSV의 각 행을 실제 프로젝트 에셋으로 만든 것
  └─ Id (string)               ─ XxxData와 연결하는 키
  └─ Data (XxxData, 런타임 전용) ─ Play 중 DataTableManager가 채워주는 캐시, 저장 안 됨
```

- CSV 원본 위치: `Assets/Resources/DataTables/*.csv`
- SO 에셋 위치: `Assets/Resources/ScriptableObjects/<종류>/`
- 코드 위치: `Assets/Scripts/Data/` (공통: `DataTable.cs`, `DataTableManager.cs`),
  `Assets/Scripts/Data/<종류>/` (타입별 `XxxData`/`XxxAsset`/`XxxTable`, 예: `Resource/`, `Building/`),
  `Assets/Scripts/Editor/` (에디터 전용 임포터)
- CSV 파싱: `CsvHelper` (NuGetForUnity로 설치, `Assets/Packages/CsvHelper.33.1.0`) — 헤더 컬럼명과
  데이터 클래스 프로퍼티명을 대소문자 무시하고 자동 매칭
- 존재하지 않는 Id 조회 시 `null` 반환 + 에러 로그 → 호출부는 항상 null 체크

## 2. 왜 CSV만 쓰지 않고 ScriptableObject로 뽑는가

CSV 행은 숫자/문자열만 담을 수 있고 다른 Unity 에셋(프리팹, 스프라이트, 다른 데이터)을
참조할 수 없다. 반면 SO는 인스펙터에서 드래그해서 서로 참조시킬 수 있다.

이 프로젝트에서 필요한 목표: 나중에 `Building`(또는 `Upgrade`류) 에셋에 "이 자원이 몇 개
필요하다"는 비용 목록을 넣고 싶은데, 이때 자원 종류가 늘어나도 코드/CSV 컬럼 구조를 바꾸지
않고 리스트에 항목만 추가하는 형태로 가고 싶다. SO 참조 방식이면 아래처럼 간단히 만들 수 있다:

```csharp
[System.Serializable]
public class ResourceCost
{
    public ResourceAsset Resource;
    public int Amount;
}

public class BuildingAsset : ScriptableObject
{
    public List<ResourceCost> Cost;
}
```

인스펙터에서 `wood.asset`, `iron.asset` 등을 드래그해 넣고 `Amount`만 채우면 되므로,
CSV에 `WoodCost`/`IronCost`/... 식으로 자원별 컬럼을 미리 다 정의해둘 필요가 없다.
(이전 프로젝트의 `UpgradeAsset.Cost` / `Ingredient{ item, amount }` 패턴과 동일한 구조.)

## 3. 현재 구현된 테이블

### ResourceTable (GDD 4.2 — 나무/철/식량/마나석)

| 컬럼          | 타입                              | 설명                                          |
| ------------- | --------------------------------- | --------------------------------------------- |
| `ResourceID`  | string (PK)                       | 자원 고유 키 (`wood`, `iron`, `food`, `mana`) |
| `DisplayName` | string                            | 표시용 한글 이름                              |
| `Kind`        | enum(`Wood`/`Iron`/`Food`/`Mana`) | 자원 종류                                     |

CSV: `Assets/Resources/DataTables/ResourceTable.csv`

```
ResourceID,DisplayName,Kind
wood,나무,Wood
iron,철,Iron
food,식량,Food
mana,마나석,Mana
```

### BuildingTable (GDD 6.2 — 본진 건물)

| 컬럼           | 타입                                 | 설명                                                     |
| -------------- | ------------------------------------ | -------------------------------------------------------- |
| `BuildingID`   | string (PK)                          | 건물 고유 키 (`woodcutter_house`, `mine`, ...)           |
| `DisplayName`  | string                               | 표시용 한글 이름                                         |
| `BuildingType` | enum(`Production`/`General`/`Skill`) | 분류. 주민 배치로 뭔가 생산하는 건물은 전부 `Production` |
| `Role`         | string                               | 역할 한 줄 요약                                          |
| `Description`  | string                               | 기본 효과 설명 (UI 툴팁용)                               |

CSV에는 위 공통 필드만 있고, 건물별 세부 수치(생산량, 입·출력 자원)는 CSV가 아니라
`BuildingAsset`(SO)에 `BuildingType`별 필드 그룹으로 들어간다:

- `Production` → `ProductionFields { BaseAmountPerVillager, OutputResource, ProducesSoldier }`.
  나무꾼의 집/광산/농지는 `OutputResource`에 자원을 연결하고, 훈련장은 `ProducesSoldier = true`로
  둔다. **병사는 ResourceTable에 넣지 않는다** — GDD상 자원은 나무/철/식량/마나석 4종으로
  고정돼 있고(§3 팀 계약), 병사는 전투 스탯·교회 부활 등 화폐성 자원과 다른 생명주기를 가져
  나중에 별도 `SoldierData`/`SoldierTable`이 생길 가능성이 높기 때문.
- `Skill` → `SkillFields { InputResource }` (연금술사의 집/마법 연구소/군사학교)
- `General`(교회/본진)은 추가 필드 없음
- 건설 비용은 모든 타입 공통으로 `BuildingAsset.Cost : List<ResourceCost>` (2절 참고)
- `BuildingType`에 따라 인스펙터에 관련 필드 그룹만 보이도록 `BuildingAssetEditor`
  (`Assets/Scripts/Editor/BuildingAssetEditor.cs`)가 커스텀 인스펙터를 그린다 —
  프로젝트 최초의 `CustomEditor`/`SerializedProperty` 코드.

CSV: `Assets/Resources/DataTables/BuildingTable.csv`

```
BuildingID,DisplayName,BuildingType,Role,Description
woodcutter_house,나무꾼의 집,Production,나무 생산,배치된 주민 수만큼 나무 생산
mine,광산,Production,철 생산,배치된 주민 수만큼 철 생산
farm,농지,Production,식량 생산,배치된 주민 수만큼 식량 생산
training_camp,훈련장,Production,병사 생산,배치된 주민 수만큼 병사 생성(해당 저녁 전투용)
church,교회,General,병사 회복,"전투 중 HP 0 병사를 교회로 이동, 하루 동안 회복"
headquarters,본진,General,마을 성장 핵심,건물 최대 레벨 제한. 업그레이드 시 모든 건물 최대 레벨 증가
alchemist_house,연금술사의 집,Skill,비상 자원 공급,마나석을 나무/철/식량 중 선택해 교환
magic_lab,마법 연구소,Skill,플레이어 성장,마나석으로 스킬 획득·강화
military_school,군사학교,Skill,병사 강화,식량으로 병사 훈련 및 공격력·체력 증가
```

(참고: `church` 행처럼 필드 값에 쉼표가 들어가면 RFC4180 방식으로 큰따옴표로 감싸야 한다.
CsvHelper가 자동으로 처리하지만 수기로 행을 추가할 때 빠뜨리기 쉽다.)

### TowerTable (GDD 5.1/6.2 — 전투 공간 타워)

| 컬럼              | 타입                                | 설명                                                |
| ----------------- | ----------------------------------- | --------------------------------------------------- |
| `TowerID`         | string (PK)                         | 타워 고유 키 (`archer_tower`, `cannon_tower`, ...)  |
| `DisplayName`     | string                              | 표시용 한글 이름                                     |
| `TowerType`       | enum(`Single`/`Area`/`Chain`/`Magic`) | 공격 방식 분류                                     |
| `MagicEffectType` | enum(`None`/`Buff`/`Debuff`)        | `TowerType=Magic`일 때만 의미 있음. 그 외는 `None` 고정 |
| `GridWidth`       | int                                  | 배치 시 차지하는 그리드 칸수(가로). 공격 방식과 무관하게 모든 타워 공통 |
| `GridHeight`      | int                                  | 배치 시 차지하는 그리드 칸수(세로). 공격 방식과 무관하게 모든 타워 공통 |
| `Role`            | string                              | 역할 한 줄 요약                                      |
| `Description`     | string                              | 기본 효과 설명 (UI 툴팁용)                          |

CSV에는 위 공통 필드(분류 정보 포함)만 있고, 타워별 세부 수치(공격력/사거리/투사체,
버프·디버프 효과량 등)는 CSV가 아니라 `TowerAsset`(SO)에 `TowerType`(+ Magic이면
`MagicEffectType`)별 필드 그룹으로 들어간다. Building과 달리 타입 분기가 2단계다:

- `Single` → `SingleFields { Attack }`
- `Area` → `AreaFields { Attack, SplashRadius }`
- `Chain` → `ChainFields { Attack, ChainRadius, MaxChainTargets, ChainDamageFalloff }`
- `Magic` → `MagicFields { BuffAura, DebuffAura }`, 그중 `MagicEffectType`이 가리키는
  쪽(`BuffAuraFields` 또는 `DebuffAuraFields`)만 실제로 사용
- `Attack { AttackDamage, AttackRange, AttackInterval, ProjectilePrefab, ProjectileSpeed }`는
  Single/Area/Chain이 공통으로 내장하는 nested 구조체(중복 필드 선언 방지). 필드 의미는
  Combat의 기존 `TowerData`(`Assets/Scripts/CombatSystem/Tower/TowerData.cs`)와
  대응되도록 맞춰뒀다 — 실제 Combat 마이그레이션은 아직 미착수(WL-001)
- `BuffAuraFields`/`DebuffAuraFields { Radius, Interval, Modifiers: List<StatModifier>, Damage: OptionalDamage }`
  — 버프/디버프도 데미지가 있을 수 있어 `OptionalDamage { HasDamage, DamageAmount, TickInterval }`를
  공통 재사용
- 건설 비용은 모든 타입 공통으로 `TowerAsset.Cost : List<ResourceCost>` (2절, `ResourceCost` 재사용)
- `GridWidth`/`GridHeight`는 `Role`/`Description`과 같은 성격의 공통 CSV 필드다 — 다른 에셋을
  참조하지 않는 순수 수치라 `TowerAsset`(SO)에 별도 필드로 중복시키지 않고, `TowerData`(POCO)에만
  존재하며 `TowerAsset.Data.GridWidth`/`GridHeight`로 런타임에 조회한다(4.2절 조회 패턴).
  아직 MouseManager/BattleMapBuilder의 배치 검증(WL-004)이 이 값을 소비하진 않음 — 데이터만 우선 마련
- `TowerType`(1차) + `MagicEffectType`(Magic일 때 2차)에 따라 인스펙터에 관련 필드 그룹만
  보이도록 `TowerAssetEditor`(`Assets/Scripts/Editor/TowerAssetEditor.cs`)가
  `BuildingAssetEditor`와 동일한 패턴의 커스텀 인스펙터를 그린다

CSV: `Assets/Resources/DataTables/TowerTable.csv`

```
TowerID,DisplayName,TowerType,MagicEffectType,GridWidth,GridHeight,Role,Description
archer_tower,궁수 타워,Single,None,1,1,단일 대상 공격,사거리 내 가장 가까운 적 하나를 지속 공격
cannon_tower,대포,Area,None,1,1,광역 공격,착탄 지점 주변 범위 피해
lightning_tower,번개 타워,Chain,None,1,1,연쇄 공격,적 하나를 맞히면 주변 적으로 번개가 튐
haste_tower,가속의 탑,Magic,Buff,1,1,아군 공격속도 강화,범위 내 아군 타워 공격속도 증가
slow_tower,서리의 탑,Magic,Debuff,1,1,적 이동속도 감소,범위 내 적 이동속도 감소
```

### EnemyTable (GDD 5.2 — 전투 공간 몬스터)

| 컬럼          | 타입                          | 설명                        |
| ------------- | ----------------------------- | --------------------------- |
| `EnemyID`     | string (PK)                   | 몬스터 고유 키 (`goblin_warrior`, `goblin_archer`, `ogre_king`) |
| `DisplayName` | string                        | 표시용 한글 이름            |
| `EnemyType`   | enum(`Melee`/`Ranged`/`Boss`) | 공격 방식 분류              |
| `Role`        | string                         | 역할 한 줄 요약             |
| `Description` | string                         | 기본 효과 설명 (UI 툴팁용)  |

CSV에는 위 공통 필드만 있고, 몬스터별 세부 스탯(체력/이동속도/공격력/사거리/공격주기,
보스 전용 데이터)은 CSV가 아니라 `EnemyAsset`(SO)에 `EnemyType`별 필드 그룹으로 들어간다.
Tower와 동일한 이유(§3 TowerTable 절 참고)로, `Boss`가 향후 BehaviorTree 참조 등 다른
타입엔 없는 고유 필드를 가져야 해서 진짜 폴리모픽 구조다.

> **참고**: 이슈 [#26](https://github.com/muchan918/NorthLand/issues/26)의 원 스펙은
> `MonsterData`에 `HP`/`MoveSpeed`/`AttackDamage`를 CSV 컬럼으로 직접 요구했다(계약 우선 —
> #14/#15/#16이 이 데이터를 그대로 참조하는 전제). 실제 구현은 Tower의 밸런싱 수치 선례
> (WL-015)를 따라 스탯을 SO로 뺐다 — CSV 직접 조회(`EnemyTable.Get(id)`)만으로는 스탯을 얻을
> 수 없다는 뜻. 이 이원화는 **WL-027**로 추적 중이며, #14/#15/#16이 실제 스탯을 연동할 시점에
> `EnemyAsset` 경로로 조회하도록 맞추거나 스탯을 CSV로 승격할지 재논의가 필요하다.

타입별 필드 그룹:

- `Melee` → `MeleeFields { Stat }`
- `Ranged` → `RangedFields { Stat, ProjectilePrefab, ProjectileSpeed }`
- `Boss` → `BossFields { Stat, BehaviorTree }` — `BehaviorTree`는 실제 BT 에셋 타입이
  정해지기 전까지의 placeholder(`Object`). 정해지면 필드 타입만 교체하면 되고
  CSV/`EnemyData`(POCO)/`EnemyTable`은 변경할 필요 없음
- `Stat { MaxHp, MoveSpeed, AttackDamage, AttackRange, AttackInterval }`는 세 타입이
  공통으로 내장하는 nested 구조체. 필드 의미는 Combat의 기존 `EnemyData`
  (`Assets/Scripts/CombatSystem/Enemy/EnemyData.cs`)와 대응되도록 맞춰뒀다 —
  실제 Combat 마이그레이션은 아직 미착수(WL-001). `MoveSpeed`는 Combat 쪽엔 없는 신규 필드
- `EnemyType`에 따라 인스펙터에 관련 필드 그룹만 보이도록 `EnemyAssetEditor`
  (`Assets/Scripts/Editor/EnemyAssetEditor.cs`)가 `TowerAssetEditor`와 동일한
  패턴의 커스텀 인스펙터를 그린다 (1단계 분기만, `MagicEffectType` 같은 2단계 분기 없음)

CSV: `Assets/Resources/DataTables/EnemyTable.csv`

```
EnemyID,DisplayName,EnemyType,Role,Description
goblin_warrior,고블린 전사,Melee,근접 몬스터,본진까지 도달해 근접 공격을 가하는 기본 몬스터
goblin_archer,고블린 궁수,Ranged,원거리 몬스터,사거리 밖에서 원거리 공격을 가하는 몬스터
ogre_king,오우거 킹,Boss,보스 몬스터,강력한 스탯과 고유 행동 패턴(BehaviorTree)을 가진 보스 몬스터
```

## 4. 사용 방법

### 4.1 CSV 수정 후 Import가 필요한 경우 / 필요 없는 경우

`DataTableManager`는 static 클래스라 도메인 리로드(에디터 스크립트 컴파일, Play 진입)마다
CSV를 다시 읽는다. 그래서 반영 방식이 상황에 따라 다르다.

| 상황                                           | Import 필요 여부                                                                                                                                                               |
| ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 기존 행의 값만 수정 (`DisplayName`, `Kind` 등) | **불필요** — Play만 해도 바로 반영됨                                                                                                                                           |
| 새 행 추가 (새 `ResourceID`)                   | **필요** — 인스펙터에서 참조할 `.asset`이 새로 생성되어야 함                                                                                                                   |
| 행 삭제                                        | Import를 눌러도 기존 `.asset`은 자동 삭제되지 않음 — 고아 에셋이 남으므로 수동으로 지워야 함. 지우지 않고 방치하면 그 SO를 참조하던 곳에서 `Get()` 호출 시 "ID 없음" 에러 발생 |

### 4.2 코드에서 조회하기

```csharp
var wood = DataTableManager.Get<ResourceTable>("ResourceTable").Get("wood");
Debug.Log($"{wood.DisplayName} ({wood.Kind})");
```

`DataTableManager.Get<T>(테이블 이름)`으로 테이블을 얻고, 각 테이블의 `Get(id)`로 행을
조회하는 두 단계 패턴을 모든 테이블이 동일하게 따른다.

SO를 들고 있는 컴포넌트에서는 `Start()` 시점에 `Data`를 채워 쓴다 (도메인 리로드 후
`Data`는 비어있는 상태로 시작하므로 매번 다시 채워야 함):

```csharp
private void Start()
{
    Asset.Data = DataTableManager.Get<ResourceTable>("ResourceTable").Get(Asset.ResourceID);
}
```

### 4.3 `Tools > Table Importer` 사용법

1. Unity 메뉴 `Tools > Table Importer` 실행
2. `Table Type`에서 `Resource`/`Building`/`Tower` 중 선택
3. `Import` 버튼 클릭
4. `Assets/Resources/ScriptableObjects/<종류>/`에 CSV 행마다 `.asset` 파일 생성/갱신
5. Console에 `XxxTable Import 완료: N개` 로그 출력

관련 코드: [`TableImporter.cs`](../../Assets/Scripts/Editor/TableImporter.cs)

## 5. 신규 데이터 타입 추가할 때

1. `Docs/GDD.md` 기준으로 컬럼 구성을 정하고 이 문서 3절에 표로 추가
2. `Assets/Resources/DataTables/XxxTable.csv`에 헤더 + 데이터 행 작성
3. `XxxData`(POCO), `XxxAsset`(SO, Id + 런타임 캐시 필드), `XxxTable`(`DataTable` 상속,
   `Get(id)` 제공) 클래스 작성 — `ResourceData`/`ResourceAsset`/`ResourceTable`을 템플릿으로 복사
4. `DataTableManager.Init()`에 새 테이블 등록
5. `TableImporter`의 `TableType` enum에 새 항목 추가하고 `ImportXxx()` 메서드 작성
   (`ImportResource()`와 동일한 구조: CSV 읽기 → 폴더 생성 → 행마다 기존 에셋 갱신/신규 생성)
6. Play 모드에서 `DataTableManager.Get<XxxTable>("XxxTable").Get(id)` 조회 결과를
   로그로 찍어 CSV가 의도대로 파싱되는지 확인

## 6. 검증 방법

CLI 빌드/테스트가 없는 프로젝트이므로 Unity Editor에서 직접 확인한다.

1. `Tools > Table Importer` → `Resource` → `Import` → `Assets/Resources/ScriptableObjects/Resources/`에
   `wood.asset`, `iron.asset`, `food.asset`, `mana.asset` 4개 생성 확인
2. 확인용 임시 스크립트 [`ResourceTableTest.cs`](../../Assets/Scripts/Data/Resource/ResourceTableTest.cs)를
   씬의 빈 GameObject에 붙이고 Play → Console에 4개 자원의 `DisplayName`/`Kind`가 출력되는지 확인
3. 확인 후 `ResourceTableTest.cs`와 테스트용 GameObject는 정리 (또는 데모용으로 유지)
4. `Tools > Table Importer` → `Building` → `Import` → `Assets/Resources/ScriptableObjects/Buildings/`에
   9개 건물 `.asset` 생성 확인. `BuildingType`별로 하나씩 인스펙터를 열어
   `BuildingAssetEditor`가 해당 타입 필드 그룹만 보여주는지 확인
5. [`BuildingTableTest.cs`](../../Assets/Scripts/Data/Building/BuildingTableTest.cs)로 Play 모드에서
   9개 건물의 `DisplayName`/`BuildingType`/`Role`이 출력되는지 확인
6. `Tools > Table Importer` → `Tower` → `Import` → `Assets/Resources/ScriptableObjects/Towers/`에
   5개 타워 `.asset` 생성 확인. `TowerType`별로 하나씩 인스펙터를 열어 `TowerAssetEditor`가
   해당 타입 필드 그룹만 보여주는지, `Magic` 타입에서는 `MagicEffectType`(Buff/Debuff)에 따라
   `BuffAuraFields`/`DebuffAuraFields`가 올바르게 토글되는지 확인
7. [`TowerTableTest.cs`](../../Assets/Scripts/Data/Tower/TowerTableTest.cs)로 Play 모드에서
   5개 타워의 `DisplayName`/`TowerType`/`MagicEffectType`/`GridWidth`/`GridHeight`가
   출력되는지 확인
8. `Tools > Table Importer` → `Enemy` → `Import` → `Assets/Resources/ScriptableObjects/Enemies/`에
   3개 몬스터 `.asset` 생성 확인. `EnemyType`별로 하나씩 인스펙터를 열어 `EnemyAssetEditor`가
   해당 타입 필드 그룹만 보여주는지(`Boss`는 `BehaviorTree` placeholder 필드까지) 확인
9. [`EnemyTableTest.cs`](../../Assets/Scripts/Data/Enemy/EnemyTableTest.cs)로 Play 모드에서
   3개 몬스터의 `DisplayName`/`EnemyType`/`Role`이 출력되는지 확인

## 7. 다음 계획

- 영토(Territory), 스킬(Skill), 보상(Reward) 등도 같은 패턴으로 확장
- 병사(Soldier) 데이터 타입이 생기면 `BuildingAsset.ProductionFields.ProducesSoldier`(bool)를
  `SoldierAsset` 참조로 교체 검토
- 타워는 데이터 레이어(CSV/`TowerAsset`)까지만 구현됨 — Combat의 기존 `TowerData` SO/`Tower.cs`를
  이 파이프라인으로 마이그레이션하는 작업은 SUNGSOO와 합의 후 별도 진행 (WL-001)
- 타워 SO에 이펙트 프리팹(파티클/사운드) 등 시각 효과 참조 필드를 추가하는 확장 여지 있음
  (SO 기반이라 타입별 필드 그룹에 필드만 추가하면 됨, §2 참고)
