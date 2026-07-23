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
`RequestAdvancePhase`는 public이라 어떤 UI든 그대로 호출한다.

## 2. 낮/밤 루프 연동 (muchan `DayNightManager`)

컨트롤러가 `DayNightManager`의 전환 이벤트를 구독해 GDD §5·팀 계약 #5를 구현한다.

- **낮→밤 (`OnDayToNight`)**: 각 생산처 `Produce(주민 수)` 실행 → 자원이 지갑에 정산된다.
- **밤→낮 (`OnNightToDay`)**: 주민 배치를 0으로 초기화한다(배치는 매일 초기화).
- **페이즈 전환 요청**: 패널 버튼 → `ManagementController.RequestAdvancePhase()` → 낮이면
  `DayNightManager.EndDay()`(전원 배치돼야 활성 — 잉여 주민 게이트), 밤이면 `DayNightManager.EndNight()`.

### ⚠️ 밤→낮 전환 버튼은 임시다 (WL-018)
현재 패널이 밤→낮 전환(`EndNight()`)을 **임시로** 트리거한다. 이 씬엔 Combat이 없어 웨이브를 끝낼 주체가
없기 때문이다. **정식 게임에서 밤을 끝내는 책임은 밤을 끝내는 주체(Combat 웨이브 클리어 등)가 가져가야
하며**, 그때 경영 패널의 이 전환 호출은 제거/이관한다. 낮→밤(`EndDay`)은 경영의 정당한 책임이므로 유지.
(`ManagementController.RequestAdvancePhase` 주석에도 명시)

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

### 경계 심(seam) 상태
| 심 | #42 시점 | 현재(#43) |
|---|---|---|
| 정산 트리거 | 외부 수동 호출 | ✅ **`DayNightManager.OnDayToNight`에 연결됨** |
| 주민 수 입력 | 외부 입력 | ⏳ 여전히 **placeholder**(`ManagementController._maxVillagers`, 패널 +/-). 주민 시스템 생기면 교체 |

## 4. 책임 경계 (팀 계약)

- **#6 책임 경계**: 자원 차감 = 경영 시스템(지갑). 배치 판정·정보 표시는 각자. 입력은 MouseManager(단, 이 패널은 uGUI 버튼/EventSystem 사용 — 배치 창구 계약과 무관).
- **#3 자원 흐름** (GDD §3.2): 기본 자원(나무/철/식량) = 주민 배치 생산 **또는 영토 확장 보상**, 마나석 = 영토 확장·전투 보상에서만.
  (개정: 기본 자원의 영토 확장 보상 경로 허용 — n0wst4ndup 결정.)
  - **방향 전환(이번 이슈)**: **미개척 영지 자원**은 영지 해금 시 **매일 정산마다 일정량이 자동 수급**된다(GDD §3.2, 주민 배치·식량 소모 모두 없음). 영토 해금이라는 정당한 원천에서 나오므로 계약 위반이 아니다. (직전 '식량 소모 → 확장 자원 변환' 모델은 폐기 — §5.5 (A).) 마나석 → 기본 자원 전환 건물 등 자원 간 우회 변환 경로는 여전히 WL-042 합의 대기.
  - → 생산 라인 패널은 **주민 배치형 기본 3종(나무·철·식량) + 자동 수급형 확장 자원(가변, 영토 해금) + 마나석(보유량 표기용 row)** 으로 구성. 확장 자원·마나석은 +/- 없이 보유량 중심 표기(확장 자원은 일일 수급량도 표시). 마나석 지갑 적립 자체는 여전히 후속 시스템이 `Add`(§5.5 (C)).
- **#5 낮/밤 전환**: 위 §2대로 전환 이벤트 훅 구조로 구현.

## 5. 범위 밖 (후속 이슈)

- ❌ **주민 시스템**: 주민 보유·집계 (GDD §6.1). 현재 부재 — 주민 수는 `_maxVillagers` placeholder + 패널 +/-.
- ❌ **비용 소비처**: 건물 건설·타워 강화·병사 훈련(지갑 `TrySpend`/`CanAfford` 호출부).
- ❌ **마나석 생산 경로**: 영토 확장·전투 보상(지갑 `Add`). 생산 라인 아님.
- ❌ **실제 UI 아트/HUD 폴리싱**: 현재는 기능 배치. 아트 교체 시 뷰 참조만 재연결.
- ❌ **세이브/로드 영속화**.

## 5.5 방향 전환 — 확장 자원 & 건물 업그레이드 (GDD v0.3)

경영 자원 시스템은 다음 방향으로 확장됐다. **(A) 미개척 영지 자원·(C) 패널 자원 표기는 #166에서 구현 완료**, (B) 건물 업그레이드는 #139에서 구현. 밸런싱 **수치**(일일 수급량·업그레이드 비용 등)는 여전히 TBD(docs-are-dev-reference).

### (A) 미개척 영지 자원 라인 — 매일 자동 수급
- 경영 영토 확장(TerritoryGraph)이 **미개척 영지**를 확보하면 그 영지 고유의 **새 자원 종류 + 생산 라인**이 해금된다(GDD §3.2·§5.3).
- 새 라인은 **주민을 배치하지 않는다** — **매일 자원 정산 시 일정량이 꾸준히 자동 수급된다**(영지별 고정 일일 수급량). 식량 소모·변환은 없다. 영지별 일일 수급량 수치는 **TBD**.
- **주민·식량과 무관**: 확장 자원은 주민 배치·식량과 독립적인 패시브 수입이다. 확보한 영지 수가 늘수록 총 일일 수급이 늘어난다.
- **구현(#166)**: 각 미개척 영지 SO(`TerritoryDefinition`)가 자원 종류 + 일일 수급 [Min,Max]를 갖고, 확보 시점이 아니라 **주입 시점에 노드별 `DailyYield`를 롤**한다. 정산부 `ManagementController.HandleNightToDay`가 매 정산마다 Owned 노드에서 `ResourceWallet.Add(Kind, DailyYield)`(조회 API `SupplyDaily(kind)` = 그 종류 합). 패널은 **고정 행**((C)) — 특수 자원 4행은 항상 존재하되 미개방(수급 0) 시 회색 표시.

> **방향 전환 이력**: 직전 설계는 '식량을 소모해 확장 자원으로 변환'(식량 종속 = 밸런스 중앙 조절점) 모델이었으나, 팀 회의에서 **매일 정산 시 일정량 자동 수급**으로 변경했다. 식량은 다시 순수 기본 자원 하나로 돌아가고, '식량 vs 확장 자원' 배분 갈등·식량 종속 밸런스 축은 폐기된다(연동: GDD §8 "주민 배분 전략 재설계" 항목).

### (B) 건물 업그레이드 — 주민당 획득량 증가
- 기본 자원 생산 건물 3종(나무꾼의 집/광산/농장) 업그레이드 → **주민당 획득 자원량(주민당량) 증가**. 상세 설계는 **BuildingUpgrade.md** 참고.
- 현재 `ManagementController`는 전역 단일 `_baseAmountPerVillager`(모든 라인 공통)를 쓴다 — 업그레이드는 **라인(건물)별 주민당량**을 요구하므로 이 값이 라인별 상태로 분화돼야 한다(WL-021·WL-016 연동).
- **본진·마법연구소·연금술사의 집 업그레이드는 이번 범위 밖(TBD)**.

### (C) 자원 표기 — 생산 라인 패널 고정 행 (#166 구현)
자원 보유·수급 표기를 우측 패널의 **고정 행**으로 구성한다(동적 등록 아님 — 시작 시 한 번만 생성). ProdRow 열 구성: **이름 · 지갑(보유량) · 주민수+버튼 · +n**. row 유형(#166):

| row 유형 | 지갑(보유량) | 주민수+버튼 | +n | 회색/정렬 |
|---|---|---|---|---|
| 기본 자원(나무·철·식량) | ✅ | ✅ (주민 배치 +/-) | 예상 생산량(배치 0이면 +0) | 항상 정상 |
| 마나석 | ✅ | ❌ 숨김 | 웨이브 클리어 마나 미리보기(`ManaPerWaveClear`) | 항상 정상 |
| 미개척 영지 자원(금·루비·사파이어·다이아) | ✅ | ❌ 숨김 | 일일 수급량(미보유면 +0) | 수급 0이면 **회색**, 활성(>0) 우선 위로 재정렬 |

- **지갑(보유량)은 모든 행에 표시** — 종전 탑 바의 자원 총량 표기를 **행의 지갑 칸으로 이관**(탑 바의 `Wood/Iron/Food/Mana_hud`는 씬에서 비활성화). 정본은 행이다.
- **+n은 모든 행에 항상 표기**(+0 포함) — "이번 밤이 끝나면 들어올 양"으로 의미 통일(모든 수급이 `OnNightToDay` 정산).
- **주민수+버튼**: 주민 배치로 얻는 기본 3종만 주민수 칸·+/- 버튼을 노출. 마나·특수 자원은 그 칸을 **숨긴다**(열 정렬 위해 지갑·이름·+n 칸은 유지).
- **행 순서**: `[나무][철][식량][마나]` 고정 → 그 아래 특수 자원(활성 위 / 미개방 회색 아래, 매 갱신 `SetSiblingIndex` 재정렬).
- **구현**: `ProductionLineView`에 `Villager`/`Supply`/`Mana` 모드(공유 프리팹 `@NorthLand/Prefabs/UI/ProdRow.prefab`, 지갑=`_balanceText`→Wallet). `ManagementPanelView`가 `LineCount`(기본 라인)+마나+`ResourceKind`의 특수 종류(기본3·마나 제외)로 고정 행을 만든다. 미개방 회색은 활성 원색 캐시 + `_inactiveColor` 교체.
- **탑 바(top bar)**: 자원 4종 지갑 HUD는 **비활성화(행으로 이관 완료)**. 주민 풀·페이즈 표시는 탑 바에 유지. HUD 오브젝트 완전 삭제는 후속 정리로 남김.

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
1. 생산 라인 +/- 로 주민 배치(총 `_maxVillagers`까지). 밤에는 배치 불가.
2. 전환 버튼 → 낮→밤 정산(자원이 HUD에 증가). 잉여 주민 있으면 버튼 비활성.
3. 전환 버튼 → 밤→낮(임시). 주민 0 초기화 + Wave 증가, 자원은 유지.

**확인된 행동 계약** (Play 실동작):
- 낮→밤에 `주민당량 × 주민수`가 올바른 `ResourceKind`로 지갑에 정산된다.
- 밤→낮에 주민이 0으로 초기화되고 Wave가 증가하며 자원은 유지된다.
- 전원 배치돼야만 낮→밤 전환이 가능하다(잉여 게이트).
- 밤에는 주민 배치가 막힌다.

> ⚠️ unity-cli 스크린샷은 Screen Space Overlay 캔버스를 캡처하지 못한다 — 시각 확인은 에디터 Game 뷰에서.

---

*공개 계약은 `Docs/Review/SystemMap.md` §1(소유자)·§2(공개 API)·§3(접점)에 반영. 반복 이슈는 WatchList
WL-017(지갑 소유)·WL-018(밤→낮 트리거 임시).*
