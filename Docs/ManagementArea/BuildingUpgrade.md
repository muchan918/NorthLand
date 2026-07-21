# BuildingUpgrade — 생산 건물 업그레이드 (주민당 획득량 증가) [설계]

> **담당**: n0wst4ndup
> **이슈**: #139 (feature/139-building-upgrade)
> **경로(코드)**: `Assets/Scripts/ManagementSpace`
> **상태**: 🟡 **설계 중** — 수치·비용은 전부 TBD, 구조부터 확정 후 구현.
> 확정되지 않은 항목은 본문에서 **TBD / 제안**으로 명시한다(docs-are-dev-reference 규약).
> **GDD 근거**: §5.7(건물 업그레이드) · §3.2(자원 흐름) · §4.1(낮—건물 업그레이드)

이 문서는 경영 공간 **건물 업그레이드가 이번 이슈에서 무엇을 구현하고, 무엇을 미루는지**의 기준선이다.
자원 시스템 본체는 `Resources.md`, 자원 방향 전환(확장 자원)은 `Resources.md §5.5`를 따른다.

---

## 1. 목적 · 범위

낮 동안 생산 건물을 업그레이드해 **주민 1명이 만드는 자원량(주민당량)을 늘린다**(GDD §5.7).

### ✅ 이번 이슈 범위
- **대상 건물 3종**: 나무꾼의 집(나무) · 광산(철) · 농장(식량) — 즉 **기본 자원 생산 건물만**.
- **효과**: 업그레이드 시 해당 건물의 **주민당량(amount-per-villager) 증가**.
  - 생산량 = `주민당량 × 주민 수 × 생산배율`(현재 정산식, `Resources.md §3`). 업그레이드는 **주민당량** 항을 키운다.
- **레벨 구조**(제안): 건물마다 이산 레벨(Lv1→2→3…), 레벨업 시 자원 비용 소모 + 주민당량 +Δ.

### ❌ 범위 밖 (TBD / 후속)
- **본성 · 마법연구소 · 연금술사의 집** 등의 업그레이드 — 효과·구조 미정, 이번 이슈에 없음.
- **미개척 영지 확장 자원 라인**의 업그레이드(우선 기본 3종만).
- **밸런싱 수치 전부**: 레벨 수, 레벨당 비용, 레벨당 주민당량 증가폭 — **전부 TBD**(사용자 확인: "수치·비용 전부 TBD").
- 상단 자원 UI(top bar) 재설계(GDD §8 TODO, 별도).

---

## 2. 핵심 설계 쟁점 (구현 전 확정 필요)

### 쟁점 1 — per-instance 레벨 상태를 어디에 두는가 (WL-016)
**공유 SO에 레벨을 쓰면 안 된다.** 현재 건물 데이터는 건물 타입당 단일 `BuildingAsset`(SO)를 공유한다
(`Docs/Review/SystemMap.md` §2 "Data 채움 규약"). 여기에 레벨/주민당량을 쓰면 같은 타입의 다른 인스턴스·다음
런까지 오염된다(WL-016). → **레벨 상태는 런타임 상태로 분리**해야 한다.

- **제안**: 업그레이드 레벨·현재 주민당량은 `ManagementController`가 **라인별 런타임 상태**로 소유한다
  (지갑·주민 배치 상태를 이미 이 컨트롤러가 소유하는 것과 같은 계보). `BuildingAsset`은 **읽기 전용 기준값**
  (기본 주민당량·레벨 테이블)만 제공.

### 쟁점 2 — 전역 단일 주민당량 → 라인별 주민당량 (WL-021)
현재 `ManagementController._baseAmountPerVillager`는 **모든 라인 공통 단일값**이다. 업그레이드는 건물별로
주민당량이 달라지므로 이 값이 **라인(건물)별 배열/상태로 분화**돼야 한다.

- **제안**: 라인별 `currentAmountPerVillager[i]`(레벨 반영값)를 두고, 정산(`Produce`)·예상치
  (`LineExpectedProduction`)가 전역값 대신 이 라인별 값을 참조. `ResourceProductionSource`는 이미 주민당량을
  생성자 인자로 받으므로(레벨업 시 소스 재생성 or 값 갱신 경로 결정 필요).
- WL-021의 "라인 생성원을 배치된 건물 인스턴스로 이관"과 방향이 겹친다 — 건물 배치 시스템(#27) 통합 전이면
  **인스펙터 고정 라인 위에서 레벨 상태만 얹는** 최소 구현으로 시작할 수 있다(범위 관리).

### 쟁점 3 — 업그레이드 비용 · 수치 데이터 출처 (계약 #2 / WL-015)
레벨당 비용·주민당량 증가폭을 **CSV(계약 #2)** 로 둘지, **SO 인스펙터**(영토 효과·타워 스탯 선례, WL-015)로 둘지.
- 수치는 **TBD**. 데이터 출처만 미리 합의 필요 — 기본 자원 건물은 `BuildingAsset` 계열이므로 CSV 파이프라인
  (`BuildingTable`)에 레벨 테이블 컬럼을 얹는 쪽이 계약 #2와 정합적(muchan 데이터 소유 확인 필요).

### 쟁점 4 — 업그레이드 트리거(입력)와 비용 차감
- **입력**: 업그레이드는 uGUI 패널 버튼(경영 패널 계보) — `ManagementPanelView`/`ProductionLineView`에 레벨·
  업그레이드 버튼 추가. MouseManager 배치 창구(계약 #1)와 무관(패널은 EventSystem 사용, Resources.md §4).
- **비용 차감**: **반드시 `ManagementController.CanAfford/TrySpend(IReadOnlyList<ResourceCost>)` 게이트웨이 경유**
  (WL-017·WL-048 — 지갑 직접 접근·별도 차감 로직 발명 금지). 원자적 차감 후 레벨↑.

---

## 3. 데이터/상태 흐름 (제안)

```
[정적 데이터]                 [런타임 상태(ManagementController)]        [뷰]
 BuildingAsset(SO)   ──읽기──▶  라인별 Level[i] / AmountPerVillager[i]  ──▶ ProductionLineView
  기본 주민당량                  ▲            │                              (레벨·업그레이드 버튼)
  레벨 테이블(비용·증가폭)        │            │ TrySpend(cost) 게이트웨이
  ※ CSV vs SO = 쟁점3            │            ▼
                           업그레이드 버튼 → Upgrade(lineIndex) → 비용 차감 성공 시 Level++·AmountPerVillager 갱신
                                                                    → OnChanged → 정산식·예상치·HUD 갱신
```

- 정산(`HandleNightToDay`)·예상치(`LineExpectedProduction`)는 전역 `_baseAmountPerVillager` 대신 **라인별 주민당량**을 참조하도록 변경.
- 업그레이드는 **낮에만** 가능(밤 배치 잠금과 동일한 페이즈 게이팅, Resources.md §2). 게이팅 정책은 구현 시 확정.

---

## 4. 통합 계약 / 의존

| 접점 | 방식 |
|---|---|
| **자원 시스템**(Resources.md) | 주민당량 항을 업그레이드가 조정. 정산식·`LineExpectedProduction` 라인별화 |
| **지갑 소비**(WL-017/WL-048) | 업그레이드 비용은 `ManagementController.CanAfford/TrySpend(costs)` 경유 — 별도 차감 로직 금지 |
| **DataTable**(muchan) | 기본 주민당량·레벨 테이블 출처(`BuildingAsset`/`BuildingTable`). 데이터 출처 합의 = 쟁점3 |
| **UI**(`ManagementPanelView`/`ProductionLineView`) | 레벨 표시·업그레이드 버튼 추가. 로직은 컨트롤러만 호출(로직/뷰 분리 유지) |
| **Localization** | 업그레이드 버튼·레벨 라벨 문자열은 String Table 키(`LocalizationHelper`/`LocalizeStringEvent`) |

---

## 5. 미결 / TODO

- [ ] **수치 전부**: 레벨 수, 레벨당 비용, 레벨당 주민당량 증가폭 (밸런싱, 후속).
- [ ] **데이터 출처 합의**(쟁점3): 레벨 테이블 = CSV(`BuildingTable`) vs SO 인스펙터. muchan 협의.
- [ ] **레벨 상태 소유 확정**(쟁점1·2, WL-016/WL-021): `ManagementController` 라인별 런타임 상태로.
- [ ] **레벨 상한·리셋 정책**: 최대 레벨, 런 시작 시 리셋 여부(런 내 유지 vs 매일 유지).
- [ ] **업그레이드 게이팅**: 낮 전용? 영토 확장 완료 필요? 잉여 주민 게이트(CanEndDay)와의 상호작용.
- [ ] **세이브/로드**: 업그레이드 레벨 영속화(전역 세이브 미도입 상태).
- [ ] **본성/마법연구소/연금술사 업그레이드**(범위 밖): 효과·구조 정의 후 별도.

---

## 6. 검증 방법

지갑·정산은 순수 로직이라 이상적이나 asmdef 부재로 유닛 테스트 불가(Resources.md §7) — **씬 Play + 패널 조작**으로 검증한다.

**절차(제안)**: `GameScene` Play →
1. 생산 라인 주민 배치 후 예상 생산량 확인.
2. 업그레이드 버튼 → 비용 차감(HUD 감소) + 주민당량↑ → 같은 주민 수에서 예상 생산량 증가 확인.
3. 자원 부족 시 업그레이드 불가(조용한 실패 없이 로그/피드백).
4. 낮→밤 정산에서 상향된 주민당량이 실제 정산에 반영되는지 확인.

> ⚠️ unity-cli 스크린샷은 Screen Space Overlay 캔버스를 캡처하지 못한다 — 시각 확인은 에디터 Game 뷰에서.

---

## 7. 문서 반영 예정 (구현 PR에서)

- `SystemMap.md` §2(공개 API: `ManagementController`에 업그레이드 진입점 추가) · §3(접점: DataTable 레벨 테이블).
- `WatchList.md`: WL-016(per-instance 레벨 상태)·WL-021(라인별 주민당량)·WL-015(수치 출처) 진전 반영.

---

*이 문서는 설계 합의용 초안이다. §5 TBD가 확정되는 대로 갱신하고, 구현 착수 시 "설계" 표기를 해제한다.*
