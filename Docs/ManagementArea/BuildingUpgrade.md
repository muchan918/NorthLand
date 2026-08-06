# BuildingUpgrade — 건물 업그레이드 (생산 · 마법 연구소 · 본진 해금 · 교환 효율)

> **담당**: n0wst4ndup (#139 생산 트랙) · muchan (#205 스킬 강화, #229 본진 해금)
> **이슈**: #139 (feature/139-building-upgrade) · #205 (마법 연구소 강화) · #229 (본진 업그레이드)
> **경로(코드)**: `Assets/Scripts/ManagementSpace`
> **상태**: ✅ **세 트랙 모두 구현 완료**. ①생산 라인 업그레이드(#139, PlayMode 26/26 PASS §6) ②업그레이드 전용
> 건물 트랙 + 스킬 강화 연동(#205, §8) ③**본진 레벨 해금 + 연금술사 교환 효율(#229, §9)** — 레벨 테이블
> 타입 중립 승격으로 §8이 남겨둔 선행 작업이 종결됐다. 레벨수·비용·증가폭 **수치는 밸런싱 TBD**.
> 확정되지 않은 항목은 본문에서 **TBD**로 명시한다(docs-are-dev-reference 규약).
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

### ❌ #139 범위 밖 (후속 이슈에서 해소됨)
- **본성 · 연금술사의 집** 등의 업그레이드 — #139 시점엔 효과·구조 미정이었으나 **#229에서 확정·구현 완료**(§9).
  본성은 레벨을 올려 하위 건물 Max를 해금하고, 연금술사는 자체 레벨 없이 본진 레벨이 곧 교환 효율이다.
  - **마법 연구소는 후속으로 구현됨** — 생산 라인이 아니라 **업그레이드 전용 건물 트랙**으로 별도 구현했다(마나석 비용, 강화 효과는 #205 완료). §8 참고.
- **미개척 영지 확장 자원 라인**의 업그레이드(우선 기본 3종만).
- **밸런싱 수치 전부**: 레벨 수, 레벨당 비용, 레벨당 주민당량 증가폭 — **전부 TBD**(사용자 확인: "수치·비용 전부 TBD").
- 상단 자원 UI(top bar) 재설계(GDD §8 TODO, 별도).

---

## 2. 핵심 설계 쟁점 (✅ 구현으로 확정)

**구현 결과 요약(#139)** — 아래 쟁점 상세는 결정 근거 기록으로 남긴다:
- **쟁점1 (WL-016) — 레벨 상태 위치**: ✅ `ManagementController`가 라인별 런타임 배열(`_level[]`·`_amountPerVillager[]`)로 소유. 공유 SO(`BuildingAsset`)엔 레벨 상태를 쓰지 않는다.
- **쟁점2 (WL-021) — 라인별 주민당량**: ✅ 라인 소스를 `ResourceAsset[]` → **`BuildingAsset[]`(`_productionBuildings`)** 로 이관. 전역 `_baseAmountPerVillager` **제거**, 라인별 주민당량이 정산·예상치를 구동. `ResourceProductionSource`는 주민당량을 `Produce(villagers, amountPerVillager, mult)` 인자로 받는 **무상태 심**으로 리팩터(readonly 필드 제거).
- **쟁점3 (WL-015) — 수치 출처**: ✅ **SO(`BuildingAsset.Production.UpgradeLevels`)에 authoring**(CSV 아님). BuildingData CSV엔 수치 컬럼이 없고 생산 수치(주민당량·비용)가 이미 `BuildingAsset` SO에 있어 SO가 정합적(타워 스탯·영토 효과 선례). 수치 자체는 placeholder TBD.
- **쟁점4 — 트리거·차감**: ✅ 공개 API `TryUpgrade(int)`(성공 bool) + 조회 `CanUpgrade`/`LineLevel`/`LineMaxLevel`/`LineAmountPerVillager`/`LineUpgradeCost`. 비용은 기존 `TrySpend(costs)` 게이트웨이로 **원자적** 차감(WL-017/WL-048). 입력 UI는 다음 이슈. 게이팅 = **낮 전용**(`IsDay`, 영토확장 완료는 요구 안 함).

### ⚠️ 업그레이드 소유 단위 — **건물 타입 단위**(현재 확정)
업그레이드 상태는 **라인 = 건물 타입 단위**로 소유한다(`_productionBuildings`가 타입당 SO 1개 → 라인당 레벨 1개).
경영 공간이 같은 타입 건물을 여러 채 배치하지 않는 현 구조에서 자연스러운 모델이며, **다음 UI 이슈는 "라인(타입)당
업그레이드 1개" 계약을 그대로 소비**하면 된다(index 순회 + `LineUpgradeCost==null`→"MAX").
- **인스턴스 단위**(같은 타입을 여러 채 지어 각각 따로 업그레이드)가 필요해지는 건 **건물 배치 시스템(#27)** 도입
  시점이다. 그때 라인 index 계약을 인스턴스 키로 열지 결정한다(WL-021 잔여). 그전까지는 타입 단위 유지.
- #139 완료기준 ⑤("같은 타입 다른 인스턴스와 단계 혼선 없음")은 **현재 인스턴스 개념이 없어 자동 충족**(공허참).
  단, 레벨 상태를 공유 SO가 아닌 런타임에 둔 덕분에(쟁점1) 인스턴스가 생겨도 오염되지 않는 구조는 이미 갖춰져 있다.

---

### (참고) 결정 근거 — 원래 쟁점 상세

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
  ※ SO에 authoring(쟁점3 해소)   │            ▼
                           TryUpgrade(lineIndex) 〔UI=다음 이슈〕 → TrySpend 성공 시 Level++·AmountPerVillager 갱신
                                                                    → OnChanged → 정산식·예상치·HUD 갱신
```

- 정산(`HandleNightToDay`)·예상치(`LineExpectedProduction`)는 전역 `_baseAmountPerVillager` 대신 **라인별 주민당량**(`_amountPerVillager[i]`)을 참조하도록 변경 — ✅ 구현됨.
- 업그레이드는 **낮에만** 가능(`IsDay` 게이트, 주민 배치와 동일한 페이즈 규칙) — ✅ 확정·구현됨(영토확장 완료는 요구 안 함).

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

- [ ] **수치 전부**: 레벨 수, 레벨당 비용, 레벨당 주민당량 증가폭 (밸런싱, 후속). **현재 placeholder**: 3종 모두 Lv1(5→7, wood20+iron10) / Lv2(7→9, wood40+iron20).
- [x] **데이터 출처 합의**(쟁점3): **SO 확정** — `BuildingAsset.Production.UpgradeLevels`에 authoring(CSV 아님, WL-015 선례).
- [x] **레벨 상태 소유 확정**(쟁점1·2, WL-016/WL-021): **완료** — `ManagementController` 라인별 런타임 배열(`_level`·`_amountPerVillager`), 라인 소스 `BuildingAsset[]` 이관.
- [x] **레벨 상한·리셋 정책**: **확정** — 상한 = 테이블 길이(`LineMaxLevel`), 런 내 유지. #270부터 낮 시작 상태를 저장해 종료 후 이어하기에서도 유지한다.
- [x] **업그레이드 게이팅**: **낮 전용 확정**(`IsDay`). 영토확장 완료 요구 없음. 잉여 주민과도 독립 — 애초에 독립이었고, #219에서 잉여 주민 게이트(`CanEndDay`) 자체가 폐지돼 확인 팝업 경고(`HasIdleVillagers`)로 강등됐다.
- [ ] **UI 통합**(다음 이슈): 경영 패널에 레벨 표시·업그레이드 버튼(`TryUpgrade`/`CanUpgrade`/`LineLevel`/`LineUpgradeCost` 바인딩).
- [x] **세이브/로드(#270)**: 생산·업그레이드 건물 레벨과 생산 라인 주민 배치를 BuildingID 기준으로 저장·복원한다. 인스펙터 배열 순서를 바꿔도 저장 의미가 유지된다.
- [x] **마법 연구소 업그레이드**: **업그레이드 전용 건물 트랙으로 구현됨**(§8). 마나석 비용·레벨 추적 + 강화 효과(스킬 기본 스탯 배율, #205) 구현 완료.
- [x] **연금술사 업그레이드**: **구현 완료(#229, §9)**. ⚠ 종전 기술("`alchemist_house`는 **Skill 타입**이라 `Skill.UpgradeLevels` authoring + 배선만으로 확장 가능")은 **틀렸다** — 실제로는 `BuildingType.Store`이고, 자체 업그레이드 버튼도 레벨도 없다. `Exchange.UpgradeLevels`에 배율 행을 두되 어느 행을 쓸지는 **본진 레벨**이 고른다(`_upgradeBuildings`에 등록하지 않는다).
- [x] **본성 업그레이드**: **구현 완료(#229, §9)**. 레벨 테이블을 타입 중립 그룹(`UpgradeStep`/`UpgradeSteps`)으로 승격해 아래 선행 작업을 종결했고, `BuildingAssetEditor`가 Castle 타입에 `Villager`+`Castle` 두 그룹을 그린다. **#227이 예고한 대로** 레벨 표시는 `GetUpgradeLevel(본진)+1`이라 본진을 `_upgradeBuildings`에 등록하는 것만으로 연동됐다(그 줄은 수정하지 않았다). 다만 업그레이드 **버튼 활성화와 비용 목록**은 새로 붙였다.

---

## 6. 검증 방법

asmdef 부재로 순수 유닛 테스트 불가(Resources.md §7) — UI도 다음 이슈라 패널 조작이 없어, **PlayMode + unity-cli `exec`**(리플렉션으로 지갑 시드·정산 트리거)로 공개 API를 직접 구동해 검증했다.

**검증 완료(#139, PlayMode exec — 26/26 PASS)**:
1. **초기 상태**: 라인 3개(wood/iron/food), 주민당량 5, 레벨 0, 최대 레벨 2.
2. **원자성**: wood만 있고 iron 부족 시 `CanUpgrade`=false, `TryUpgrade`=false, **wood 무차감·레벨 불변**(부분 차감 없음).
3. **성공 차감**: Lv1(주민당량 5→7, wood-20/iron-10), Lv2(7→9, wood-40/iron-20) — 정확히 차감.
4. **최대 레벨**: Lv2에서 `TryUpgrade`=false, 레벨 불변, `LineUpgradeCost`=null.
5. **정산 반영**: 업그레이드된 주민당량(9)이 `LineExpectedProduction`(9×2=18)과 실제 정산(`HandleNightToDay` → wood +18)에 반영, 정산 후 주민 0 초기화.

> ⚠️ UI 시각 검증은 다음 이슈(패널 표시·버튼)에서. unity-cli 스크린샷은 Screen Space Overlay 캔버스를 못 잡으므로 Game 뷰에서.

---

## 7. 문서 반영 (완료)

- `SystemMap.md` §1(Management 행: 업그레이드 구현 반영) · §2(공개 API에 `TryUpgrade`/`CanUpgrade`/`LineLevel` 등 추가).
- `WatchList.md`: WL-016(레벨=런타임 상태)·WL-021(라인별 주민당량·라인 소스 건물 이관)·WL-015(수치=SO) 진전 반영.
- `GDD.md` §5.7·§3.2와 정합(주민당량 증가 = 업그레이드 효과).

---

## 8. 업그레이드 전용 건물 트랙 (마법 연구소 등)

> **상태**: ✅ 로직·데이터·씬 배선 구현. 강화 효과(스킬 강화)도 **구현 완료**(#205) — 스킬 시스템이 레벨을 참조해 기본 스탯을 배율 강화한다(결합도 최소).

생산 3종(§1~§7)과 달리 **마법 연구소**는 자원을 생산하지 않는다 — 주민 배치·산출 자원·주민당량 개념이 없고,
**마나석으로 레벨만 올리는** 건물이다. 그래서 생산 라인 배열(`_productionBuildings`/`_sources`)에 억지로 끼우지 않고
(끼우면 경영 패널이 주민 배치 행을 만들어 버린다) **별도 트랙 `_upgradeBuildings`** 로 소유한다.

### 무엇을 재사용하고 무엇이 다른가
| 항목 | 생산 건물(§1~7) | 업그레이드 전용 건물(마법 연구소) |
|---|---|---|
| 소유 배열 | `_productionBuildings` → `_sources`/라인 | `_upgradeBuildings` → `_upgradeBuildingRefs` |
| 레벨 상태 | `_level[]`(런타임, WL-016) | `_upgradeLevel[]`(런타임, 동일 계보) |
| 수치 출처 | `BuildingAsset.Production.UpgradeLevels`(비용+주민당량) | `BuildingAsset.Skill.UpgradeLevels`(**비용+스킬 강화 배율**, `SkillUpgradeLevel`, #205) |
| 비용 차감 | `TrySpend(costs)` 게이트웨이(원자적) | **동일** `TrySpend(costs)` 게이트웨이 |
| 업그레이드 효과 | 주민당량↑(즉시, 정산 반영) | 레벨만 오르고, 강화는 소비 시스템(`SkillManager`/`BuffSkillManager`, #205)이 레벨을 참조해 기본 스탯 배율로 적용 |
| UI | `BuildingInfoUI`(클릭→패널→버튼) | **동일** `BuildingInfoUI`(별 분기). (연금술사의 집은 예외 — 업그레이드 트랙이 아니라 교환소라 **별도 `StorePanelUI`** 를 띄운다, #211) 효과 줄은 생산의 "주민당 5→7" 자리에 여전히 **"스킬 강화 (추후 구현)"** placeholder를 표시(`building.upgrade.skill_pending`) — 실제 강화 내용 표시로 교체는 후속 과제(#205 범위 밖, 선택사항) |

### 데이터 (SO)
> ⚠ **#229 이후**: 등록 경로가 `Skill.UpgradeLevels` 하드코딩에서 타입 중립 `BuildingAsset.UpgradeSteps`로 바뀌었고,
> 최대 레벨도 `Count`가 아니라 **본진 레벨로 열려 있는 만큼**(실질 Max)이다. 아래 서술은 마법 연구소 기준이며
> 규칙 전문은 §9를 따른다.

- `BuildingAsset.Skill.UpgradeLevels : List<SkillUpgradeLevel>` — index i = 레벨 (i+1), 최대 레벨 = `Count`.
  `SkillUpgradeLevel`은 **도달 비용(`Cost`)** + **스킬 강화 배율 7종**(`DamageMultiplier`/`RadiusMultiplier`/`CooldownMultiplier`(감전)·`BuffDamageMultiplierScale`/`BuffAttackSpeedMultiplierScale`/`BuffDurationMultiplier`/`BuffCooldownMultiplier`(버프))을 **같은 리스트**에 authoring한다(#205, PR#216 리뷰 반영 — 비용·배율이 서로 다른 파일에 있어 레벨 개수가 어긋나던 문제를 원천 차단).
- **현재 placeholder(TBD)**: `magic_lab` = Lv1 마나 20 / Lv2 마나 40 / Lv3 마나 60, 배율은 Lv1×1.2~1.6까지 단계적 증가(정확한 값은 SO 참고). (`Assets/Resources/ScriptableObjects/Buildings/magic_lab.asset`)
- 씬 배선: `GameScene`의 `ManagementController._upgradeBuildings[0]` = `magic_lab`. `SkillManager`/`BuffSkillManager`는 씬에 배율 리스트를 따로 갖지 않고 `_magicLabAsset.Skill.UpgradeLevels`를 직접 읽는다 — 밸런싱 패스가 씬 파일을 안 건드리게 됐다.

### 공개 API (`ManagementController`)
- `int UpgradeIndexOf(BuildingAsset)` — 업그레이드 건물 index(아니면 -1). BuildingInfoUI가 클릭한 건물이 이 트랙인지 판정.
- `int UpgradeBuildingLevel(int)` / `int UpgradeBuildingMaxLevel(int)` / `IReadOnlyList<ResourceCost> UpgradeBuildingCost(int)`(최대면 null).
- `bool CanUpgradeBuilding(int)`(낮+다음 레벨+마나 감당) / `bool TryUpgradeBuilding(int)`(성공 bool, 원자적 차감 후 레벨↑, `OnChanged` 발화).
- **`int GetUpgradeLevel(BuildingAsset)`** — 소비 시스템(스킬 강화 등)이 레벨을 읽는 **저결합 창구**. 미등록/미보유면 0.

### 결합도 최소 — 스킬 강화 연동 방식 ✅ **착지점 확정(#205)**
> 연구소 레벨 = **기본 스킬 스탯 배율**, 보상 = **특수 효과 레벨**(`SkillEffect.Level`, #169) — 두 축은 독립적으로 동시 스택된다. muchan 사인오프 완료. 상세: `Docs/Skill/PlayerSkill.md`.
- 메커니즘: 컨트롤러는 **레벨(int)만 노출**하고, 레벨→강화효과 매핑은 **소비 측(`SkillManager`/`BuffSkillManager`)이 소유**한다 —
  컨트롤러는 "스킬"을 전혀 모르고, 각 스킬은 마법 연구소 건물 SO 참조와 `GetUpgradeLevel`만 안다. 레벨 변경은 `OnChanged`로 통지되므로 구독 후 재-pull(BuildingInfoUI와 동일 패턴). **컨트롤러/UI 무수정**으로 스킬 쪽에만 배선이 붙었다.
- 레벨→배율 매핑은 `BuildingAsset.Skill.UpgradeLevels`(SO, index i = 레벨 i+1)에 authoring한다 — CSV 미사용(WL-015와 동일 축), 씬이 아니라 SO라 밸런싱 패스가 `GameScene.unity`를 안 건드린다(PR#216 리뷰 반영). 수치는 **placeholder(TBD, 밸런싱 후속)**.
- `SkillManager`/`BuffSkillManager`는 공통 베이스클래스로 묶지 않고 동일 패턴을 각자 구현했다(기존 "스킬 2개뿐이라 추상화 안 함" 방침 유지).

### 잔여 / TODO
- [ ] **수치 밸런싱**: 레벨 수·레벨당 마나 비용(현재 placeholder 20/40/60), 스킬 강화 배율(현재 placeholder).
- [ ] **클릭 오브젝트**: 마법 연구소를 클릭해 패널을 열려면 씬/프리팹에 `BuildingInfo`(+`Selectable` 레이어 콜라이더) 배치 필요 —
  생산 건물 클릭 오브젝트와 동일하게 건물 프리팹(Imported 사각지대 가능, WL-040) 쪽 작업.
- [x] **연금술사(Store 타입, #211 교환 → #229 효율 업그레이드)**: 교환소가 먼저 구현되고(`BuildingAsset.Exchange`, 상세는 `Docs/ManagementArea/Resources.md` §3), **효율 업그레이드는 #229에서 완성**됐다(§9). 지불 마나석 고정, 받는 자원량 × `GainMultiplier`라는 효과는 종전 설계 그대로지만 **레벨의 출처가 바뀌었다** — 자체 레벨 트랙을 갖는 대신 본진 레벨이 배율 행을 고른다.
- [x] **본성(`BuildingType.Castle`, #227에서 General에서 분리)**: **#229 구현 완료**(§9). 아래 선행 작업을 함께 처리했다.
  - 참고: **주민 증가(#227)는 이 트랙을 타지 않는다.** `Villager.Levels`를 독립 데이터 그룹으로 두고 `BuildingAsset`을 직접 받는 별도 게이트웨이(`TryIncreaseVillagers`)로 갔기 때문에, 선행 작업 없이 구현됐다. 본진 패널은 지금 **두 축을 나란히** 보여준다(주민 증가 / 레벨 업그레이드).
- [x] **선행 작업 — 레벨 테이블 타입 중립 승격** (본성·연금술사 공통): **#229에서 종결.** `BuildUpgradeBuildings()`의 `building.Skill?.UpgradeLevels` 하드코딩을 `building.UpgradeSteps`로 바꿨다. 예상대로 컨트롤러가 이 테이블에서 읽는 건 비용(+해금 요구치)뿐이었고, 효과값은 지금도 소비 측이 자기 필드 그룹에서 직접 읽는다(`SkillManager`가 `Skill.UpgradeLevels`를, 교환이 `Exchange.UpgradeLevels.GainMultiplier`를). 다만 **"비용만 뽑는 헬퍼"가 아니라 공통 베이스 클래스 추출**로 갔다 — 요구치 필드가 세 클래스에 모두 필요해져 반환 타입 자체가 공통 조상을 요구했기 때문이다(§9).

---

## 9. 본진 레벨 해금 + 교환 효율 (#229)

> **상태**: ✅ 구현 완료. §8이 남겨둔 "레벨 테이블 타입 중립 승격" 선행 작업을 함께 종결했다.

본진을 업그레이드하면 두 가지가 일어난다.

1. 생산 3종 + 마법 연구소의 **업그레이드 Max 레벨이 해금**된다.
2. 연금술사의 집 **교환 효율이 오른다** — 지불 마나석 고정, 받는 자원량 증가.

### 핵심 원칙 — 본진은 하위 건물을 모른다

본진 SO에 "나무꾼은 몇 레벨, 광산은 몇 레벨" 목록을 두지 **않는다.** 대신 각 건물의 레벨 행이
`RequiredCastleLevel`(이 레벨이 열리는 데 필요한 본진 레벨)을 소유한다. 그래서:

- 건물이 추가돼도 **그 건물 SO만** 편집하면 된다 — 본진 asset도 코드도 손대지 않는다.
- 레벨 행을 지우면 요구치도 함께 사라진다 — 행 개수와 요구치 개수가 어긋날 수 없다.

### 데이터 구조 — 타입 중립 승격

```csharp
[System.Serializable]
public abstract class UpgradeStep          // 신규 공통 베이스
{
    public List<ResourceCost> Cost;
    public int RequiredCastleLevel;        // 0 = 처음부터 열림
}
```

`UpgradeLevel`(생산) · `SkillUpgradeLevel` · `ExchangeUpgradeLevel` · `CastleUpgradeLevel`(신규)이 이를 상속하고,
각자의 `Cost` 선언을 제거했다. **기존 `.asset`은 무손실** — Unity가 상속 직렬화 필드를 같은 이름으로 평탄하게
저장하므로 YAML상 `Cost:` 위치가 그대로다(마이그레이션 불필요, 전 건물 덤프로 확인).

`BuildingAsset.UpgradeSteps`가 채워진 필드 그룹 하나를 골라 `IReadOnlyList<UpgradeStep>`으로 돌려준다
(`Castle` → `Skill` → `Production` → `Exchange` 순). `IReadOnlyList<out T>` 공변이라 복사·할당이 없고,
분기 키는 `BuildingType`이 아니라 **'데이터 존재'**다(`BuildingInfo.OnSelected` 계보).

본진 전용 그룹 `CastleFields.UpgradeLevels`는 **비용만** 갖는다 — 효과값이 없는 게 위 원칙의 표현이다.

### 실질 Max — 앞에서부터 '연속으로'

```
실질 Max = 첫 행부터 훑다가 RequiredCastleLevel > 현재 본진 레벨인 행에서 멈춘 지점
```

잠긴 행을 건너뛰고 뒷행을 살리지 **않는다.** 레벨은 순차 증가여야 하는데 2단계를 건너뛰고 3단계에
도달할 수는 없기 때문이다. 그래서 요구치가 비단조(`[0, 2, 1]`)면 3번째 행은 요구치를 만족해도 영원히
잠긴다 — `OnValidate`가 이 authoring을 경고한다.

**같은 요구치를 여러 행에 붙이면 본진 한 단계가 그만큼 한꺼번에 연다.** 건물마다 다른 성장 속도가
이 숫자만으로 표현된다(현재 생산 3종 `[0,0,1,1,2,2]` = 단계당 2레벨, 마법 연구소 `[0,0,0,1,2]` = 기본 3레벨 + 단계당 1).

**본진 자신은 게이팅에서 제외**한다(`ignoreGate`) — 자기 요구치로 스스로 잠기는 데드락을 막기 위해서다.
본진 행의 `RequiredCastleLevel`은 무시되며 `OnValidate`가 0을 권고한다.

### 연금술사만 의미가 다르다

자체 업그레이드 버튼이 없어 **레벨이라는 개념 자체가 없다**(`_upgradeBuildings`에 등록하지 않는다).
여기서 `RequiredCastleLevel`은 "이 레벨이 열린다"가 아니라 **"본진 몇 레벨부터 이 배율"**이고,
계산도 연속 스캔이 아니라 **요구치를 만족하는 마지막 행**의 `GainMultiplier`를 쓴다.

> ⚠ **곱셈 배율은 기본값이 작으면 해상도를 잃는다.** `GainAmount`가 1이면 `Mathf.RoundToInt` 때문에
> 배율 1.5와 2.0이 **둘 다 2**가 되어 성장이 멈춘 것처럼 보인다(특수 자원 4종에서 실제로 발생).
> 항목별 증가 폭을 조절할 땐 배율이 아니라 각 항목의 `GainAmount`(출발점)를 손봐야 한다 —
> 배율은 모든 교환 항목에 공통으로 걸리기 때문이다.

### 내부 레벨 vs 표시 레벨

`RequiredCastleLevel`과 `CastleLevel`은 **내부값**(0 = 미업그레이드)이고, **UI는 +1해서** 보여준다
(`CastlePanelUI`가 `GetUpgradeLevel + 1`로 제목을 그리는 규약). 잠금 안내 문구도 이 변환을 거쳐야 한다 —
빠뜨리면 "본진 Lv2인데 Lv2 필요"처럼 이미 만족한 조건을 요구하는 것으로 보인다(실제 발생·수정됨).

반면 **`OnValidate`·도달 불가 경고의 숫자는 내부값 그대로** 둔다. 인스펙터에 입력하는 값과 1:1로 대조하는
authoring 진단이라, +1하면 필드값과 경고가 어긋나 새 혼동이 생긴다.

### 공개 API 추가분 (`ManagementController`)

- `int CastleLevel` — 현재 본진 레벨(내부값). 해금·교환 배율의 **단일 기준**. `_castleIndex`는 씬 배선 없이
  `Castle.UpgradeLevels` 존재로 자동 탐색한다(씬 작업은 `_upgradeBuildings`에 본진 SO 추가 하나로 끝).
- `int LineRequiredCastleLevel(int)` / `int UpgradeBuildingRequiredCastleLevel(int)` — 잠겼으면 필요한 본진
  레벨(내부값), 잠기지 않았거나 진짜 최대면 0. **진짜 최대와 잠김을 구분**해 표시하기 위한 창구.
- `int ExchangeGainAmount(BuildingAsset, ExchangeOffer)` — private에서 승격. 표시부가 원본 `GainAmount` 대신
  이걸 써야 표시와 실지급이 일치한다.

### 파급 전파 — 추가 배선 없음

`TryUpgradeBuilding`이 이미 `OnChanged`를 발화하고 모든 패널이 컨트롤러에서 pull하므로, 본진을 올리면
하위 건물 Max·교환 표시가 자동으로 따라온다. 상점을 연 채로 본진을 올려도 획득량이 갱신된다
(`StoreOfferRow.SetGain` — 행 재생성은 금지, 클릭 처리 중 버튼이 파괴된다).

### authoring 가드

| 가드 | 위치 | 잡는 것 |
|---|---|---|
| `ValidateRequiredCastleLevels` | `BuildingAsset.OnValidate` | 음수·비단조(영구 잠김)·본진 자기참조·그룹 중복 |
| `ValidateUpgradeCosts` | 〃 | 총액 0(무료 업그레이드). **Exchange는 스킵** — 배율 행은 도달 비용이 없는 게 정상 |
| `WarnUnreachableCastleRequirements` | `BuildUpgradeBuildings` (Play 시작 1회) | 본진 최대 레벨을 넘는 요구치. SO는 서로를 모르므로 `OnValidate`로는 불가능 |

### 잔여 / TODO

- [ ] **수치 밸런싱**: 본진 비용(현재 나무100+철50 / 나무200+철120+마나30), 해금 폭, 교환 배율(1.5/2.0), 특수 자원 기본 획득량(2).
- [x] **세이브/로드(#270)**: 본진을 포함한 업그레이드 건물 레벨을 BuildingID 기준으로 영속화한다.

---

*구현 완료 문서. 잔여 TBD(수치 밸런싱·UI 통합)는 §5·§8·§9 참고.*
