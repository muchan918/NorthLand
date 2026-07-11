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
- 코드 위치: `Assets/Personal/muchan/Data/` (공통: `DataTable.cs`, `DataTableManager.cs`),
  `Assets/Personal/muchan/Data/<종류>/` (타입별 `XxxData`/`XxxAsset`/`XxxTable`, 예: `Resource/`, `Building/`),
  `Assets/Personal/muchan/Editor/` (에디터 전용 임포터)
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
  (`Assets/Personal/muchan/Editor/BuildingAssetEditor.cs`)가 커스텀 인스펙터를 그린다 —
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
2. `Table Type`에서 `Resource` 선택 (현재는 옵션 1개)
3. `Import` 버튼 클릭
4. `Assets/Resources/ScriptableObjects/Resources/`에 CSV 행마다 `.asset` 파일 생성/갱신
5. Console에 `ResourceTable Import 완료: N개` 로그 출력

관련 코드: [`TableImporter.cs`](../../Assets/Personal/muchan/Editor/TableImporter.cs)

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
2. 확인용 임시 스크립트 [`ResourceTableTest.cs`](../../Assets/Personal/muchan/Data/ResourceTableTest.cs)를
   씬의 빈 GameObject에 붙이고 Play → Console에 4개 자원의 `DisplayName`/`Kind`가 출력되는지 확인
3. 확인 후 `ResourceTableTest.cs`와 테스트용 GameObject는 정리 (또는 데모용으로 유지)
4. `Tools > Table Importer` → `Building` → `Import` → `Assets/Resources/ScriptableObjects/Buildings/`에
   9개 건물 `.asset` 생성 확인. `BuildingType`별로 하나씩 인스펙터를 열어
   `BuildingAssetEditor`가 해당 타입 필드 그룹만 보여주는지 확인
5. [`BuildingTableTest.cs`](../../Assets/Personal/muchan/Data/BuildingTableTest.cs)로 Play 모드에서
   9개 건물의 `DisplayName`/`BuildingType`/`Role`이 출력되는지 확인

## 7. 다음 계획

- 영토(Territory), 타워(Tower), 스킬(Skill), 보상(Reward) 등도 같은 패턴으로 확장
- 병사(Soldier) 데이터 타입이 생기면 `BuildingAsset.ProductionFields.ProducesSoldier`(bool)를
  `SoldierAsset` 참조로 교체 검토
