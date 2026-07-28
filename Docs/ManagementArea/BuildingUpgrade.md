# BuildingUpgrade — 생산 건물 업그레이드 (주민당 획득량 증가)

> **담당**: n0wst4ndup
> **이슈**: #139 (feature/139-building-upgrade)
> **경로(코드)**: `Assets/Scripts/ManagementSpace`
> **상태**: ✅ **로직 구현·검증 완료**(#139) — 라인 소스를 `BuildingAsset`로 이관 + 라인별 레벨/주민당량 런타임
> 상태 + 업그레이드 공개 API. PlayMode 검증 26/26 PASS(§6). **UI 통합은 다음 이슈**(경영 패널 레벨 표시·업그레이드
> 버튼). 레벨수·비용·증가폭 **수치는 placeholder TBD**(밸런싱 후속).
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

### ❌ 범위 밖 (TBD / 후속)
- **본성 · 연금술사의 집** 등의 업그레이드 — 효과·구조 미정, 이번 이슈에 없음.
  - **마법 연구소는 후속으로 구현됨** — 생산 라인이 아니라 **업그레이드 전용 건물 트랙**으로 별도 구현했다(마나석 비용, 강화 효과는 TODO). §8 참고.
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
- [x] **레벨 상한·리셋 정책**: **확정** — 상한 = 테이블 길이(`LineMaxLevel`), 런 내 유지(세이브 미도입 → Play/런 시작 시 초기화).
- [x] **업그레이드 게이팅**: **낮 전용 확정**(`IsDay`). 영토확장 완료 요구 없음. 잉여 주민 게이트(CanEndDay)와 독립.
- [ ] **UI 통합**(다음 이슈): 경영 패널에 레벨 표시·업그레이드 버튼(`TryUpgrade`/`CanUpgrade`/`LineLevel`/`LineUpgradeCost` 바인딩).
- [ ] **세이브/로드**: 업그레이드 레벨 영속화(전역 세이브 미도입 상태).
- [x] **마법 연구소 업그레이드**: **업그레이드 전용 건물 트랙으로 구현됨**(§8). 마나석 비용·레벨 추적 + 강화 효과(스킬 기본 스탯 배율, #205) 구현 완료.
- [ ] **연금술사 업그레이드**(범위 밖): `alchemist_house`는 **Skill 타입**이라 마법 연구소와 동일하게 `Skill.UpgradeLevels` authoring + `_upgradeBuildings` 배선만으로 확장 가능.
- [ ] **본성 업그레이드**(범위 밖): `castle.asset`(#227에서 `headquarters.asset`에서 개명, WL-061 해소)은 **`BuildingType.Castle`**(#227에서 General에서 분리, enum 끝에 추가)이며 `BuildingAssetEditor`가 이 타입에 그리는 건 **주민 증가 테이블(`Villager`)뿐**이라 업그레이드 레벨 테이블은 여전히 authoring 불가 → **SO만 추가로는 안 된다.** 착수하려면 레벨 테이블 필드를 타입 중립 그룹으로 승격(또는 에디터가 `Castle`에도 레벨 테이블을 그리게)하는 선행 작업이 필요하다(§8 참고, 리뷰 지적). **#227 진전**: 패널(`CastlePanelUI`)과 업그레이드 버튼 자리·레벨 표시는 이미 있다 — 레벨은 `GetUpgradeLevel(본진)+1`로 읽으므로 본진을 `_upgradeBuildings`에 등록하는 순간 **UI 수정 없이 연동된다.**

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
- [~] **연금술사(Store 타입, #211)**: 먼저 **마나석 교환소**가 구현됐다(`BuildingType.Store` + `BuildingAsset.Exchange`, 상세는 `Docs/ManagementArea/Resources.md` §3). **업그레이드도 이 건물의 확정 설계다** — 마법 연구소와 같은 "마나석으로 레벨만 올리는" 트랙이고, 효과는 **교환 효율 개선**(지불 마나석 고정, 받는 자원량 × `GainMultiplier`). 데이터 형태도 `SkillUpgradeLevel`과 같은 계보로 `ExchangeUpgradeLevel { Cost, GainMultiplier }`를 한 리스트에 둔다.
  - **#211에서는 교환만 구현했고 `Exchange.UpgradeLevels`는 비어 있다.**
  - 아래 본성과 **동일한 선행 작업**이 필요하다. 즉 "같은 트랙에 SO만 추가하면 확장"이라던 종전 기술은 틀렸다: `BuildUpgradeBuildings()`가 `BuildingType`과 무관하게 `Skill.UpgradeLevels`만 하드코딩으로 읽으므로, `Exchange.UpgradeLevels`는 등록 자체가 안 된다.
- [ ] **본성(`BuildingType.Castle`, #227에서 General에서 분리)**: 레벨 테이블이 `Skill` 필드 그룹에 결박돼 있어 Castle 타입도 인스펙터 authoring 불가 — 필드를 **타입 중립 그룹으로 승격**하는 선행 작업 필요(리뷰 지적). "SO만 추가"로는 안 됨.
  - 참고: **주민 증가(#227)는 이 트랙을 타지 않는다.** `Villager.Levels`를 독립 데이터 그룹으로 두고 `BuildingAsset`을 직접 받는 별도 게이트웨이(`TryIncreaseVillagers`)로 갔기 때문에, 아래 선행 작업 없이 구현됐다. 즉 선행 작업은 **본성 '업그레이드'에만** 필요하다.
- [ ] **선행 작업 — 레벨 테이블 타입 중립 승격** (본성·연금술사 공통): `ManagementController.BuildUpgradeBuildings()`의 `building.Skill?.UpgradeLevels` 하드코딩을 타입 중립 소스로 바꾼다. 컨트롤러가 이 테이블에서 실제로 읽는 건 **`Cost`뿐이므로**(`UpgradeBuildingCost`/`CanUpgradeBuilding`/`TryUpgradeBuilding`), 필드 그룹별 레벨 리스트에서 비용만 뽑아 주는 헬퍼 하나면 충분하다. 효과값은 지금처럼 소비 측이 자기 필드 그룹에서 직접 읽는다(`SkillManager`가 `Skill.UpgradeLevels`를, 교환이 `Exchange.UpgradeLevels.GainMultiplier`를). 이게 끝나면 **본성 업그레이드와 연금술사 교환 효율 업그레이드가 동시에 열린다.**

---

*구현 완료 문서. 잔여 TBD(수치 밸런싱·세이브·UI 통합·스킬 강화 효과)는 §5·§8 참고.*
