# 데이터 테이블 매니저 (Data Table Manager) 가이드

CSV로 정의한 게임 데이터(건물, 자원, 영토 등)를 `DataTableManager`가 어떻게 로드·제공하는지,
그리고 현재 합의된 테이블/스키마 구성을 정리한 문서. 새로운 데이터 종류를 추가하거나
기존 데이터를 코드에서 참조할 때 참고 용도로 쓴다. (관련 이슈: [#4](https://github.com/muchan918/NorthLand/issues/4))

> 이 문서의 테이블/컬럼 구성은 `Docs/GDD.md` 기준으로 잡은 **초안**이다. 실제 CSV나
> 코드에 테이블을 추가·변경한 사람은 이 문서도 함께 갱신해서, 문서와 실제 에셋이
> 어긋나지 않게 유지한다.

## 1. 구조 개요

```
DataTableManager (static)          ─ 테이블 이름 → DataTable 인스턴스 레지스트리, 최초 접근 시 전체 로드
  └─ XxxTable : DataTable          ─ CSV 한 장에 대응, Id로 개별 행 조회 (Get(id))
       └─ XxxData                  ─ CSV 한 행에 대응하는 순수 데이터 객체 (POCO)
```

- CSV 원본 위치: `Assets/Resources/Data/*.csv` (Unity `Resources.Load<TextAsset>` 사용)
- 코드 위치: `Assets/Personal/muchan/Data/`
- 파싱: `CsvHelper` (헤더 행의 컬럼명 ↔ 데이터 클래스 프로퍼티명을 대소문자 무시하고 자동 매칭)
- 존재하지 않는 Id를 조회하면 `null` 반환 + 에러 로그 → 호출부에서 항상 null 체크

## 2. 테이블 구성

| 테이블 이름 | 용도 | 관련 GDD 섹션 | 상태 |
|---|---|---|---|
| `ResourceTable` | 자원 4종(나무/철/식량/마나석) 정의 | 4.2 | 구현 예정 |
| `BuildingTable` | 생산 건물/타워 정의, 비용·생산량 | 4.2, 6.2 | 구현 예정 (완료 기준 필수) |
| `TerritoryTable` | 경영 영토 확장 시 얻는 고유 효과 | 6.3 | 미착수 |
| `TowerTable` | 타워별 세부 스탯(사거리/공격력/쿨타임) | 6.2 | 미착수 |
| `SkillTable` | 플레이어 스킬(범위/효과/쿨타임) | 6.5 | 미착수 |
| `RewardTable` | 웨이브 종료 보상 후보 | 6.6 | 미착수 |

`BuildingTable`이 타워 배치 비용까지 다루고 있어 초기엔 `TowerTable` 없이도 완료 기준을
충족할 수 있다. 타워 개별 밸런싱(사거리/공격력 등)이 필요해지는 시점에 분리한다.

## 3. 스키마(컬럼) 정의

컬럼명은 PascalCase로 통일하고 데이터 클래스 프로퍼티명과 1:1 대응시킨다. 한 번 배포된
컬럼명은 리네임하지 않고 값만 수정한다 (참조가 프로퍼티명 매칭 기준이라 리네임 시 깨짐).
`Id`는 테이블 내에서 고유해야 하며 0은 사용하지 않는다(미할당 값으로 예약).

### ResourceTable

| 컬럼 | 타입 | 설명 |
|---|---|---|
| `Id` | int (PK) | 자원 고유 번호 |
| `Name` | string | 표시용 한글 이름 |
| `Kind` | enum(`Wood`/`Iron`/`Food`/`Mana`) | 자원 종류 |

### BuildingTable

| 컬럼 | 타입 | 설명 |
|---|---|---|
| `Id` | int (PK) | 건물 고유 번호 |
| `Name` | string | 표시용 한글 이름 |
| `Category` | enum(`Production`/`Tower`) | 경영 공간 생산 건물 / 전투 공간 타워 구분 |
| `ProduceResource` | enum(`ResourceKind`) | 생산 건물이 생산하는 자원 (타워는 미사용) |
| `WoodCost` / `IronCost` / `FoodCost` / `ManaCost` | int | 건설 비용 (자원별) |
| `ProductionAmount` | int | 낮 1일당 생산량 (타워는 0) |
| `Description` | string | UI 툴팁용 설명 |

## 4. 코드에서 사용하기

### 4.1 데이터 조회 (기본 방식)

```csharp
var wood = DataTableManager.Get<ResourceTable>("ResourceTable").Get(1);
var lumberMill = DataTableManager.Get<BuildingTable>("BuildingTable").Get(1);
```

`DataTableManager.Get<T>(테이블 이름)`으로 테이블을 얻고, 각 테이블의 `Get(id)`로 행을
조회하는 두 단계 패턴을 모든 테이블이 동일하게 따른다.

### 4.2 목록 순회가 필요한 경우

건물 목록 UI처럼 전체 행이 필요하면 `XxxTable`에 `GetAll()` 형태의 조회 메서드를
필요할 때 추가한다 (현재는 Id 단건 조회만 지원).

### 4.3 주의사항

- `DataTableManager`는 static이라 최초 접근 시 전체 테이블을 로드한다 — 씬/타이밍에
  의존하지 않고 아무 곳에서나 바로 호출 가능
- CSV 로드 실패나 Id 미존재는 예외를 던지지 않고 `null` + 에러 로그로 처리되므로,
  호출부는 항상 null 체크 후 사용

## 5. 신규 데이터 추가할 때

1. 어느 테이블에 속하는지 2절 표에서 먼저 확인 (없으면 새 `XxxTable` 필요 여부 논의)
2. `Docs/GDD.md` 기준으로 컬럼 구성을 정하고 3절 표에 먼저 추가
3. `Assets/Resources/Data/XxxTable.csv`에 헤더 + 데이터 행 작성
4. `XxxData`(POCO)와 `XxxTable`(`DataTable` 상속, `Get(id)` 제공) 클래스 작성
5. `DataTableManager` 초기화 로직에 새 테이블 등록
6. Play 모드에서 `DataTableManager.Get<XxxTable>("XxxTable").Get(id)` 조회 결과를
   로그로 찍어 CSV가 의도대로 파싱되는지 확인
