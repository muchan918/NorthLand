# ManagementArea — 자원 시스템 (Wallet · Production · Panel)

> **담당**: n0wst4ndup
> **이슈**: #42(지갑·생산처 코어), #43(패널 UI·DayNightManager 연동)
> **경로(코드)**: `Assets/Scripts/ManagementSpace`
> **경로(씬)**: `Assets/Personal/n0wst4ndup/Management/scenes/ManagementSystem.unity`
> **상태**: ✅ 구현 완료 — 지갑·생산처 코어 + 경영 패널 UI + DayNightManager 낮/밤 루프 연동

이 문서는 경영 자원 시스템이 **무엇을 하고, 어떤 경계로 다른 시스템과 만나는지**의 기준선이다.
GDD의 경영 공간(§4.1)·자원 흐름(§4.2)·하루 루프(§5)를 런타임에서 떠받친다.

---

## 1. 구성 요소 (구현됨)

자원 흐름은 **입구(생산처) → 저장소(지갑) → 표시(패널)** 로 흐르고, **낮/밤 전환**이 정산·초기화를 구동한다.

| 요소 | 파일 | 역할 |
|---|---|---|
| `ResourceWallet` | `scripts/ResourceWallet.cs` | 자원 상태 저장소(순수 C#). `Get`/`CanAfford`/`Add`/`TrySpend` + `OnChanged`. 획득·차감 단일 창구 |
| `ResourceProductionSource` | `scripts/ResourceProductionSource.cs` | 건물 생산 단위(순수 C#). `주민당량 × 주민 수` → 지갑에 정산. `CalculateAmount`/`Produce`/`TryCreate` |
| `ManagementController` | `scripts/ManagementController.cs` | **로직/모델**(MonoBehaviour). 지갑·생산처·주민 배치 상태 소유, DayNightManager 이벤트 구독, UI 무관 |
| `ManagementPanelView` | `scripts/UI/ManagementPanelView.cs` | **뷰**. 컨트롤러 구독해 HUD·주민 풀·페이즈·생산 라인 렌더, 전환 버튼 연결 |
| `ProductionLineView` | `scripts/UI/ProductionLineView.cs` | 생산 라인 한 행(이름·주민수·예상생산량·+/- 버튼) |
| `ProdRow.prefab` | `prefabs/ProdRow.prefab` | 생산 라인 행 프리팹 |

**패널 구성**(NordHold 유사, 현재 구현): 상단 HUD(자원 4종 총량 · 주민 풀 · 낮/밤 표시 · 전환 버튼), 우측 생산 라인 리스트(+/- 주민 배치). → **재설계 예정**: 자원 총량 표기의 정본을 우측 패널로 이관, 탑 바 자원 표기는 4주차 제거 예정(§5.5 (C)).

### 로직/뷰 분리 (실제 UI 교체 대비)
컨트롤러는 UI를 모르고, 뷰는 로직을 갖지 않는다(위젯 참조 + 렌더링만). **실제 UI 아트로 교체할 때 뷰의
인스펙터 참조만 다시 연결**하면 컨트롤러·모델·지갑·생산처는 그대로다. `AssignVillager`/`UnassignVillager`/
`EndDay`는 public이라 어떤 UI든 그대로 호출한다.

## 2. 낮/밤 루프 연동 (muchan `DayNightManager`)

컨트롤러가 `DayNightManager`의 전환 이벤트를 구독해 GDD §5·팀 계약 #5를 구현한다.

- **낮→밤 (`OnDayToNight`)**: 각 생산처 `Produce(주민 수)` 실행 → 자원이 지갑에 정산된다.
- **밤→낮 (`OnNightToDay`)**: 초기화하는 것이 **없다**(#219) — 주민 배치는 전날 그대로 유지된다(매일 재배치 강제 제거).
- **페이즈 전환 요청**: 패널 버튼 → `ManagementEndDayConfirmPopup.Request(controller)` → 낮 프로세스 조건
  (유휴 주민 없음 `!HasIdleVillagers`) **하나**를 점검해 **충족이면 곧장**,
  미충족이면 **확인 팝업을 띄우고 [계속] 선택 시** `ManagementController.EndDay()` → `DayNightManager.EndDay()`.
  영토 미확장 경고는 #337에서 삭제됐고, **타워 미배치 경고는 #410에서 삭제됐다** — 타워를 일부러
  배치하지 않는 것도 전략이라 매일 뜨는 경고가 불편하고, 배치·합성·주민 배치 방법은 1스테이지 시작 전
  튜토리얼이 가르치므로 안내 목적도 사라졌다. 이로써 팝업은 **전투 공간(`Tower`)을 전혀 참조하지 않는다**
  (팀 계약 #4 공간 분리가 코드 수준에서도 성립).
  **강제 게이트는 없다**(#219, WL-022) — 버튼은 항상 활성이고 조건은 경고일 뿐이다.
  밤→낮(`EndNight()`)은 이 경로가 아니라 "웨이브 성공" 버튼이 직접 호출한다(WL-018).

### ⚠️ 밤→낮 전환 버튼은 임시다 (WL-018)
현재 패널이 밤→낮 전환(`EndNight()`)을 **임시로** 트리거한다. 이 씬엔 Combat이 없어 웨이브를 끝낼 주체가
없기 때문이다. **정식 게임에서 밤을 끝내는 책임은 밤을 끝내는 주체(Combat 웨이브 클리어 등)가 가져가야
하며**, 그때 경영 패널의 이 전환 호출은 제거/이관한다. 낮→밤(`EndDay`)은 경영의 정당한 책임이므로 유지.
(`ManagementController.EndDay` 주석에도 명시)

## 3. 자원 흐름 아키텍처

```
[정적 데이터]          [생산처]                    [지갑]                 [패널]
 BuildingAsset    ┌──────────────────┐
  .ProductionFields ─▶│ 주민당량 × 주민수 │
  ResourceAsset    │ OutputResource   │── Add(kind, 양) ─▶ ResourceWallet ── OnChanged ─▶ HUD
                   └──────────────────┘                      ▲
                         ▲            ▲                       │ TrySpend (후속: 소비처)
             (주민 수 입력)│    (정산 트리거)│
        [주민 시스템 부재  │   [DayNightManager │
         → placeholder]   │    OnDayToNight]   │
                          │                    │
            패널 +/- 버튼 → AssignVillager/UnassignVillager (ManagementController)
```

- **지갑**은 "지금 얼마 있고 더/뺄 수 있는가"만 안다. 벌고 쓰는 *이유*는 경계 밖(팀 계약 #3·#6).
- **생산처**는 `주민당량 × 주민수` 규칙만 안다. 두 입력(주민 수·정산 시점)은 소유하지 않는다.

### 자원 교환 경로 (#211, 연금술사의 집)

정산·수급과 별개로, 낮에 **마나석을 다른 자원으로 바꾸는 단방향 경로**가 하나 있다.

```
[정적 데이터]                     [게이트웨이]                        [지갑]
 BuildingAsset                ┌────────────────────────┐
  .Exchange.PayResource ─────▶│ TrySpend(마나석, PayAmount) │──┐
  .Exchange.Offers[]          │            ↓ 성공 시에만    │  ├─▶ ResourceWallet ──OnChanged─▶ StorePanelUI
   (GainResource,             │ Add(대상자원, GainAmount)   │──┘                              (버튼 활성 재계산)
    PayAmount, GainAmount)    └────────────────────────┘
                               ManagementController.TryExchange
                                        ▲
                          StorePanel [교환] 버튼 클릭
```

- **차감과 지급이 한 트랜잭션**이다. `TrySpend`가 실패하면 `Add`에 도달하지 않으므로 '공짜 자원'이 구조적으로 불가능하다.
- 그래서 이 경로는 계약 #3의 **제2 획득 경로가 아니라 마나석 소비처**다. `ResourceWallet.Add`는 계속 비공개이고, 소비처에 열린 것은 `CanExchange`/`TryExchange` 둘뿐이다.
- **낮 전용**(`TryUpgradeBuilding`과 동일 게이트). 밤에는 `CanExchange`가 false를 반환해 버튼도 함께 비활성화된다.
- 교환 대상은 마나석을 제외한 **3종**(나무·철·식량). 행 추가/삭제는 코드 변경 없이 `Exchange.Offers`(SO 리스트) 편집으로 끝난다.
  (영토 시스템 제거로 특수 자원 4종 행은 삭제됐다 — #337.)
- **역교환(자원 → 마나석)은 없다.** 마나석은 여전히 전투 보상으로만 들어온다.
- 교환비 수치는 SO authoring(밸런싱 TBD).
- **본진 레벨이 오르면 교환 효율이 개선된다**(**#229 구현 완료**): 지불 마나석은 그대로 두고 받는 자원량에 `Exchange.UpgradeLevels[].GainMultiplier`를 곱한다(예: 마나석 10 → 나무 10 이 나무 15).
  - ⚠ **배율 소스가 종전 설계와 다르다**: 연금술사는 **자체 업그레이드 버튼도 레벨도 없다**(`_upgradeBuildings`에 등록하지 않는다). 배율 행을 고르는 건 **본진 레벨**이며, `ExchangeUpgradeLevel.RequiredCastleLevel`이 "본진 몇 레벨부터 이 배율"을 뜻한다(만족하는 **마지막** 행 적용). 도달 비용(`Cost`)은 쓰이지 않는다 — 교환마다 내는 마나석이 곧 비용이다.
  - 표시부는 원본 `offer.GainAmount`가 아니라 **`ManagementController.ExchangeGainAmount`**(public 승격)를 써야 표시와 실지급이 일치한다.
  - ⚠ **곱셈 배율은 기본값이 작으면 해상도를 잃는다**: `GainAmount`가 1이면 정수 반올림 때문에 배율 1.5와 2.0이 둘 다 2가 된다. (종전 특수 자원 4종이 여기 걸려 기본 획득량을 2로 올렸었다 — 행 자체는 #337에서 삭제됐지만 함정은 남아 있다.) 항목별 증가 폭은 배율(모든 항목 공통)이 아니라 각 항목의 `GainAmount`로 조절한다. 상세: BuildingUpgrade.md §9.

### 경계 심(seam) 상태
| 심 | #42 시점 | 현재(#43) |
|---|---|---|
| 정산 트리거 | 외부 수동 호출 | ✅ **`DayNightManager.OnDayToNight`에 연결됨** |
| 주민 수 입력 | 외부 입력 | ✅ **본진 패널에서 증가(#227)** — `MaxVillagers` = `_maxVillagers`(시작값 2, 씬 직렬화) + `_bonusVillagers`(런타임 증가분). 상한은 `castle.asset`의 `Villager.Levels` 행 수(8행 = 최대 10명). 주민 **개체** 시스템이 생기면 출처 재이관 |

## 4. 책임 경계 (팀 계약)

- **#6 책임 경계**: 자원 차감 = 경영 시스템(지갑). 배치 판정·정보 표시는 각자. 입력은 MouseManager(단, 이 패널은 uGUI 버튼/EventSystem 사용 — 배치 창구 계약과 무관).
- **#3 자원 흐름** (GDD §3.2): 기본 자원(나무/철/식량) = 주민 배치 생산, 마나석 = 전투 보상.
  **자원은 이 4종뿐이다(#337)** — 영토 확장 보상 경로와 특수 자원 4종(금·루비·사파이어·다이아)은 영토 시스템째 삭제됐다.
  - **마나석 → 자원 교환은 연금술사의 집으로 확정**(#211, §3 '자원 교환 경로' — WL-042 해소). 지갑 획득 API를 열지 않고 차감+지급 원자 트랜잭션 하나만 노출했으므로 계약 위반이 아니다. **본진 업그레이드 비용도 종결(#229)** — 자원 종류 자유 authoring(현재 나무·철·마나석)이며 `TrySpend` 게이트웨이를 거치는 **순수 소비 경로**라 지갑을 늘리지 않는다(WL-042 완전 해소).
  - → 생산 라인 패널은 **주민 배치형 기본 3종(나무·철·식량) + 마나석(보유량 표기용 row)** 으로 구성. 마나석은 +/- 없이 보유량 중심 표기. 마나석 지갑 적립 자체는 여전히 후속 시스템이 `Add`(§5.5 (C)).
- **#5 낮/밤 전환**: 위 §2대로 전환 이벤트 훅 구조로 구현.

## 5. 범위 밖 (후속 이슈)

- ⏳ **주민 시스템**: 주민 **개체** 보유·집계 (GDD §5.1). 여전히 부재 — #227로 늘어난 건 '수'(`MaxVillagers`)뿐이고, 주민 하나하나를 표현하는 엔티티는 없다.
- ❌ **비용 소비처**: 건물 건설·타워 강화·병사 훈련(지갑 `TrySpend`/`CanAfford` 호출부).
- ❌ **마나석 생산 경로**: 전투 보상(지갑 `Add`). 생산 라인 아님.
- ❌ **실제 UI 아트/HUD 폴리싱**: 현재는 기능 배치. 아트 교체 시 뷰 참조만 재연결.
- ✅ **세이브/로드 영속화(#270)**: `RunSaveManager`가 모든 `ResourceKind`의 절대 보유량을 저장하고 `ManagementController.TryRestoreResource`를 통해 복원한다. `ResourceWallet`은 파일 포맷을 모르며 `TrySet`과 변경 이벤트만 제공한다.

## 5.5 방향 전환 — 확장 자원 & 건물 업그레이드 (GDD v0.3)

경영 자원 시스템은 다음 방향으로 확장됐다. **(C) 패널 자원 표기는 #166에서 구현 완료**, (B) 건물 업그레이드는 #139에서 구현. 밸런싱 **수치**(업그레이드 비용 등)는 여전히 TBD(docs-are-dev-reference).

### (A) 미개척 영지 자원 라인 — 폐기(#337)
- #166에서 영토 확장으로 해금하는 특수 자원 4종(금·루비·사파이어·다이아)이 **매일 자동 수급**되도록 구현했었다.
- **경영 공간 영토 시스템 전체가 #337에서 제거되면서 이 축도 함께 사라졌다.** 자원은 **나무·철·식량·마나석 4종뿐**이고,
  `ResourceKind`에서도 특수 4종이 삭제됐다(`TerritoryDefinition`·`SupplyDaily`·`Supply` row 모드 전부 제거).
- 그 결과 자원 획득 경로는 **주민 배치 생산 + 웨이브 클리어 마나석 + 연금술사 교환** 셋으로 단순해졌다.

> **방향 전환 이력**: '식량 소모 → 확장 자원 변환'(#166 이전) → '영지 확보 시 매일 자동 수급'(#166) → **영토 시스템째 폐기**(#337).

### (B) 건물 업그레이드 — 주민당 획득량 증가
- 기본 자원 생산 건물 3종(나무꾼의 집/광산/농장) 업그레이드 → **주민당 획득 자원량(주민당량) 증가**. 상세 설계는 **BuildingUpgrade.md** 참고.
- 현재 `ManagementController`는 전역 단일 `_baseAmountPerVillager`(모든 라인 공통)를 쓴다 — 업그레이드는 **라인(건물)별 주민당량**을 요구하므로 이 값이 라인별 상태로 분화돼야 한다(WL-021·WL-016 연동).
- **본진·마법연구소·연금술사의 집 업그레이드도 전부 구현 완료**: 마법연구소 = 스킬 기본 스탯 배율 강화(#205), 본진 = 하위 건물 Max 해금(#229), 연금술사 = 본진 레벨에 따른 교환 효율(#229, 위 §3). 상세는 BuildingUpgrade.md §8·§9.

### (C) 자원 표기 — 생산 라인 패널 고정 행 (#166 구현)
자원 보유·수급 표기를 우측 패널의 **고정 행**으로 구성한다(동적 등록 아님 — 시작 시 한 번만 생성). ProdRow 열 구성: **이름 · 지갑(보유량) · 주민수+버튼 · +n**. row 유형(#166):

| row 유형 | 지갑(보유량) | 주민수+버튼 | +n | 회색/정렬 |
|---|---|---|---|---|
| 기본 자원(나무·철·식량) | ✅ | ✅ (주민 배치 +/-) | 예상 생산량(배치 0이면 +0) | 항상 정상 |
| 마나석 | ✅ | ❌ 숨김 | 웨이브 클리어 마나 미리보기(`ManaPerWaveClear`) | 항상 정상 |

- **지갑(보유량)은 모든 행에 표시** — 종전 탑 바의 자원 총량 표기를 **행의 지갑 칸으로 이관**(탑 바의 `Wood/Iron/Food/Mana_hud`는 씬에서 비활성화). 정본은 행이다.
- **+n은 모든 행에 항상 표기**(+0 포함) — "이번 밤이 끝나면 들어올 양"으로 의미 통일(모든 수급이 `OnNightToDay` 정산).
- **주민수+버튼**: 주민 배치로 얻는 기본 3종만 주민수 칸·+/- 버튼을 노출. 마나석은 그 칸을 **숨긴다**(열 정렬 위해 지갑·이름·+n 칸은 유지).
- **행 순서**: `[나무][철][식량][마나]` **고정 4행**. 특수 자원 행과 활성 우선 재정렬은 #337에서 함께 제거돼 재정렬 자체가 없다.
- **구현**: `ProductionLineView`에 `Villager`/`Mana` 모드(공유 프리팹 `@NorthLand/Prefabs/UI/ProdRow.prefab`, 지갑=`_balanceText`→Wallet). `ManagementPanelView`가 `LineCount`(기본 라인)+마나로 고정 행을 만든다. 미개방 회색(`_inactiveColor`)은 특수 자원 전용이었으므로 함께 삭제됐다.
- **탑 바(top bar)**: 자원 4종 지갑 HUD는 **비활성화(행으로 이관 완료)**. 주민 풀·페이즈 표시는 탑 바에 유지. HUD 오브젝트 완전 삭제는 후속 정리로 남김.

## 5.6 표시 이름 — **임시** (#374)

> ⚠️ **아래 이름은 빌드용 잠정안이며 확정이 아니다.** 확정 전까지 이 표가 표시 이름의 유일한 정본이고,
> 다른 문서 본문(`Docs/Core/EconomyBalance.md` 등)의 "나무/철/식량/마나석" 표기는 **코드 식별자 기준**의
> 옛 표기다 — 그쪽을 이름에 맞춰 고치지 않는다(이름이 또 바뀌면 두 번 훑게 된다).

월드 아트가 과자 테마(`Assets/Imported/Sweet_Land`, `@NorthLand/Prefabs/Management/CandyLand.prefab` —
건물 프리팹이 Waffle·Cookie·Chocolate 메시로 조립돼 있다)인데 표시 문자열만 옛 이름이 남아 있어 맞춘 것이다.

### 무엇이 바뀌었나 — 4층 중 맨 끝 하나

| 층 | 예 | 이번에 바뀜? |
|---|---|---|
| C# enum `ResourceKind` | `Wood` | ❌ |
| CSV `ResourceID` / `BuildingID` | `wood` / `woodcutter_house` | ❌ |
| 로컬라이제이션 키 | `game.resources.wood` | ❌ |
| **String Table 값** | 나무 → **비스켓** | ✅ **여기만** |

### 자원

| `ResourceKind` | `ResourceID` | 키 | ko | en | ja |
|---|---|---|---|---|---|
| `Wood` | `wood` | `game.resources.wood` | 비스켓 | Biscuit | ビスケット |
| `Iron` | `iron` | `game.resources.iron` | 초코 | Chocolate | チョコ |
| `Food` | `food` | `game.resources.food` | 설탕 | Sugar | 砂糖 |
| `Mana` | `mana` | `game.resources.mana` | 별사탕 | Star Candy | こんぺいとう |

### 건물

| `BuildingID` | 키 접두 | ko | en | ja |
|---|---|---|---|---|
| `woodcutter_house` | `buildings.woodcutter.*` | 비스켓 하우스 | Biscuit House | ビスケットハウス |
| `mine` | `buildings.mine.*` | 초코나무 | Chocolate Tree | チョコの木 |
| `farm` | `buildings.farm.*` | 슈가 팜 | Sugar Farm | シュガーファーム |
| `castle` | `buildings.castle.*` | 캐슬 *(미변경)* | Castle | キャッスル |
| `alchemist_house` | `buildings.alchemist.*` | 연금술사의 집 *(미변경)* | Alchemist's House | 錬金術師館 |
| `magic_lab` | `buildings.lab.*` | 마법 연구소 *(미변경)* | Magic Lab | マジックラボ |

이름을 안 바꾼 3종도 **설명문(`.desc`)에 자원명이 박혀 있어** 함께 갱신했다.

### ⚠️ 식별자·키를 바꾸지 않은 이유

다음 사람이 "이름 바꿨는데 ID는 왜 옛날 거냐"며 손대기 쉬운 자리다. 근거를 남긴다:

- **세이브가 깨진다** — `RunData.BuildingId`는 `woodcutter_house` 같은 **문자열을 그대로 저장**한다
  (`Assets/Scripts/SaveData/Run/RunData.cs`). ID를 바꾸면 기존 이어하기가 복원에 실패한다.
  (`ResourceKind`는 enum→int라 **선언 순서만 유지하면** 안전하다)
- **참조가 조용히 어긋난다** — `TableImporter`가 `{ID}.asset`으로 파일 경로를 만든다
  (`Assets/Scripts/Editor/TableImporter.cs`). ID를 바꾸고 임포터를 돌리면 **새 GUID의 빈 SO가 생기고**,
  기존 참조(`ResourceAsset.Icon`·`ProductionFields.OutputResource`·씬의 `_productionBuildings`)는
  옛 파일을 계속 가리킨다. **에러가 안 난다** — 아이콘이나 생산 자원이 소리 없이 틀어진다.
- `Docs/Tools/StringTable.md` §4: 배포된 키는 리네임하지 않고 값만 수정한다(번역 매핑이 키 기준).

### 이름을 다시 바꿀 때

`Window > Asset Management > Localization Tables`에서 **값만** 수정하고 이 표를 갱신한다. 그게 전부다.
`.asset`을 텍스트로 직접 고치지 말 것(한글·일본어가 `\uXXXX` 이스케이프라 깨진다).
에디터가 켜져 있으면 메모리 값이 덮어쓰므로 에디터에서 편집하거나 `unity-cli exec`를 쓴다.

**문장 안에 이름이 박힌 엔트리** — 값 치환 시 같이 봐야 하는 곳:
`buildings.*.role` · `buildings.*.desc` · `buildings.alchemist.desc` · `buildings.lab.desc` ·
`castle.effect.lv2` · `castle.effect.lv3`.
치환 스크립트를 쓸 거면 **긴 이름부터** 적용할 것 — "광산→초코나무" 뒤에 "나무→비스켓"이 걸려
"초코비스켓"이 되는 사고가 실제로 났다.

## 6. 통합 계약 / 미결 사항

- **muchan 의존**: `ResourceKind`(지갑 키)·`BuildingAsset.ProductionFields`(생산처 입력)·`ResourceAsset.Data`
  (정산 시 `Kind` 해석, 호출부 `Start()` 채움 규약). muchan이 이 구조를 바꾸면 자원 시스템이 깨진다.
- **`DayNightManager` 의존**: `Instance`(nullable — 없으면 낮 간주), 이벤트 `OnDayToNight`/`OnNightToDay`,
  `EndDay()`/`EndNight()`. 씬에 `DayNightManager`가 있어야 루프가 돈다.
- **밤→낮 트리거 임시** (WL-018): §2 참고. 밤 종료 주체(Combat 등) 확정 시 이관.
- **지갑 소유** (WL-017): 이제 `ManagementController`가 지갑을 소유·노출(씬 범위). 전역 수명주기(WL-002)
  확정 시 재검토. 소비처·UI는 컨트롤러를 통해 접근.
- **한글 폰트**: 프로젝트에 한글 TMP 폰트 부재(LiberationSans만). **UI 텍스트는 임시 영어 표기**
  (라인 이름은 `ResourceKind`, HUD/페이즈 라벨 영어). 한글 폰트 도입은 프로젝트 전역 후속.
- **초기 보유량**: 게임 시작 기본 자원값 출처 미정 — 후속.

## 7. 검증 방법

지갑·생산처는 순수 로직이라 EditMode 유닛 테스트에 이상적이지만, **프로젝트에 asmdef가 없어**
(전부 Assembly-CSharp) test asmdef가 이들을 참조할 수 없다 → 진짜 유닛 테스트는 asmdef 도입(팀 합의) 후.
현재는 **씬 Play + 패널 조작**으로 검증한다(팀 관행, SystemMap §6).

**절차**: `ManagementSystem` 씬 Play →
1. 생산 라인 +/- 로 주민 배치(총 `MaxVillagers`까지 = 시작값 + 본진 증가분 #227). 밤에는 배치 불가.
2. 낮 종료 버튼 → **유휴 주민이 남아 있으면** 확인 팝업, [계속]이면 낮→밤 정산(자원이 HUD에 증가).
   유휴 주민이 없으면 팝업 없이 바로 넘어간다. 버튼은 어느 경우에도 비활성화되지 않는다(#219).
   - **타워 배치 여부는 더 이상 보지 않는다**(#410) — 타워를 하나도 세우지 않고 낮을 종료해도 경고가 없다.
3. 웨이브 성공 버튼 → 밤→낮(임시). Wave 증가, **자원·주민 배치 모두 유지**(초기화 없음, #219).

**확인된 행동 계약** (Play 실동작):
- 낮→밤에 `주민당량 × 주민수`가 올바른 `ResourceKind`로 지갑에 정산된다.
- 밤→낮에 Wave가 증가하며 **자원·주민 배치 모두 유지된다**(#219로 매일 재배치 강제가 사라졌다).
- 낮→밤 전환에 **강제 게이트가 없다**(#219, WL-022) — 유휴 주민이 있어도 확인 팝업 [계속]이면 넘어간다.
- 밤에는 주민 배치가 막힌다.

> ⚠️ unity-cli 스크린샷은 Screen Space Overlay 캔버스를 캡처하지 못한다 — 시각 확인은 에디터 Game 뷰에서.

---

*공개 계약은 `Docs/Review/SystemMap.md` §1(소유자)·§2(공개 API)·§3(접점)에 반영. 반복 이슈는 WatchList
WL-017(지갑 소유)·WL-018(밤→낮 트리거 임시).*
