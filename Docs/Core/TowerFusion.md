# 전투 공간 타워 합성 — 기능 명세

> **상태**: 합성 **실행부 구현·검증 완료(#194 데이터 구조 / #195 실행)** · **선택 UI + 후보 버튼(#183) 예정**
> **소유**: muchan(합성 데이터·실행) · #183 담당(선택 UI·후보 버튼·결과 정보 패널) · SUNGSOO(타워 프리팹/전투)
> **구현 파일(완료분)**:
> - `Assets/Scripts/Data/Tower/TowerRecipe.cs` (레시피 SO — 재료/결과/추가비용)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerWallet.cs` (재료 후보 홀더 — 임시)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerFusionMatcher.cs` (포함 매칭 순수 함수)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerFusionController.cs` (실행 진입점)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerPlacer.cs` (배치 오버로드 추가)
> - `Assets/Scripts/CombatSystem/Tower/Tower.cs` (`Asset` 읽기 접근자 추가)
> **관련**: GDD §5.8, `Docs/Build2/2팀 빌드 2 다음 빌드 계획.md` §1, WL-076
> **참조**: `Docs/Core/TowerPlacement.md`, `Docs/Review/SystemMap.md`(§1 TowerFusion 행·§2 API·§3 접점)
> 코드가 이 명세와 어긋나면 문서를 갱신한다(팀 계약 #7).

---

## 0. 설계 요지

- **결과 타워는 특수 타입이 아니라 일반 `TowerAsset`이다.** 합성 결과도 신규 타워 종류(§7 확장)로 `TowerTable.csv` 행 + `Towers/` SO를 만들면, 기존 배치·전투 파이프라인을 그대로 탄다.
- **레시피는 SO 전용(`TowerRecipe`)** — 재료/결과가 `TowerAsset` 참조라 CSV ID 문자열 resolve보다 인스펙터 직접 드래그가 자연스럽다(CSV 미경유).
- **실행부와 선택 UI의 경계 = `TowerWallet`.** 선택 UI(#183)는 "선택된 재료 타워를 지갑에 넣는 것"까지만 하고, 그 다음(매칭·소모·배치)은 실행부가 이미 처리한다. **지갑이 유일한 이음매다.**
- **결과 배치는 `TowerPlacer` 고스트 흐름을 재사용한다**(TowerPlacement.md §7) — 일반 타워 배치와 동일한 UX.

---

## 1. 목적

낮 페이즈에 플레이어가 **설치된 타워 여러 개를 재료로 선택**하고, **후보(결과) 타워 버튼**을 눌러 상위 타워로 합성하는 상호작용. 전투 공간의 성장 축(GDD §5.8). 본 명세는 합성 **실행부(완료)**와 그 위에 얹을 **선택 UI(#183, 예정)**를 함께 다룬다.

---

## 2. 범위

**In — 구현 완료(#194/#195)**
- 레시피 데이터(`TowerRecipe`: 재료 TowerID별 개수 → 결과 `TowerAsset` + 추가 비용 `ExtraCost`)
- 포함 매칭(`TowerFusionMatcher.TryResolve`) — 선택 ≥ 필요면 성립, 소모는 필요 개수만
- 실행(`TowerFusionController.TryFuse`): 매칭 검증 → `CanAfford` → `TowerPlacer` 고스트 배치 → 확정 시 `ExtraCost` 지불 + 재료 `Destroy`
- 임시 재료 홀더(`TowerWallet`, 인스펙터 드래그) + 테스트 버튼 1개

**Out — 예정(#183, 이 문서의 주 대상)**
- 재료 **선택**: 설치된 타워 클릭(복수 선택, shift) → 지갑 반영
- **선택 목록 표시**: 현재 지갑에 담긴 재료 타워 목록 UI
- **후보 버튼 패널**: 레시피별 결과 타워 버튼(매칭 가능 시 활성)
- **결과 정보 패널**: 현재 선택으로 만들 결과 타워 정보(이름·공격력·사거리·공속·특성)

---

## 3. 구성 요소와 계약

| 요소 | 역할 | 선택 UI가 쓰는 법 |
| --- | --- | --- |
| `TowerWallet` | 재료 후보 홀더(`List<Tower>`) | **여기에 넣고 뺀다** — `Add(Tower)`/`Remove(Tower)`/`Clear()`/`Towers`(읽기) |
| `Tower.Asset` | 배치된 타워의 원본 `TowerAsset`(→ `TowerID`) | 선택 목록 표시·매칭 판정에 필요한 타워 식별 |
| `TowerRecipe`(SO) | 재료(`Materials`)/결과(`Result`)/추가비용(`ExtraCost`) | 후보 버튼 1개 = 레시피 1개. 결과 정보 = `Result` |
| `TowerFusionMatcher` | 포함 매칭. `TryResolve`(순수 코어) + `BuildRequired(recipe)` + `CanFuse(wallet, recipe)` | **버튼 활성 판정** = `CanFuse(wallet.Towers, recipe)`. 실행부와 동일 규칙(단일 출처) |
| `TowerFusionController.TryFuse(TowerRecipe)` | 합성 실행(검증+배치+소모) | **버튼 onClick → `TryFuse(recipe)`** 한 줄 |

> `TryFuse(TowerRecipe)`는 이미 임의 레시피를 받는 공개 진입점이라 **후보 버튼 그대로 재사용**한다. 현재 컨트롤러의 `_recipe`(테스트 단일 레시피) + `TryFuseSelected()`는 디버그 버튼용 — 실제 패널은 버튼마다 자기 레시피로 `TryFuse(recipe)`를 호출한다.

---

## 4. 선택 UI 구현 가이드 (#183)

기존 **타워 선택 패널(`TowerSelectPanelView`)이 그대로 참고 모델**이다. 그 패널이 `List<TowerAsset>`으로 버튼을 만들고 자원 감당 여부로 활성/비활성하며 클릭 시 `TowerPlacer.BeginTowerPlacement(tower)`를 부르는 구조를, 합성은 `List<TowerRecipe>` + 매칭 여부 + `TryFuse(recipe)`로 옮기면 된다.

### 4.1 재료 선택 → 지갑

- 타워는 이미 `ISelectable`(`Tower : ISelectable`)이라 `MouseManager`가 클릭 선택을 통지한다. **복수 선택(shift)**은 현재 MouseManager가 단일 선택이라 확장이 필요하다(§7 TBD).
- 선택된 타워를 `wallet.Add(tower)`, 해제 시 `wallet.Remove(tower)`. 재료 식별은 `tower.Asset.TowerID`.
- 계약 #1(입력 단일 창구): 클릭 판정은 반드시 `ISelectable`/`MouseManager` 경유 — `Mouse.current` 직접 폴링 금지.

### 4.2 선택 목록 표시

- `wallet.Towers`를 순회해 각 재료 타워의 이름/아이콘을 나열(제거 버튼 선택). 이름은 `tower.Asset.TowerID` → `TowerData.NameKey` → 로컬라이즈(`NorthLand_Towers`, `LocalizationHelper.Get`).

### 4.3 후보 버튼 패널 (핵심)

`TowerSelectPanelView`와 동일 골격:

1. **레시피 카탈로그**: 패널에 `[SerializeField] List<TowerRecipe> _recipes`(인스펙터 등록) 또는 `Resources.LoadAll<TowerRecipe>`. 기존 패턴(직렬화 리스트) 권장.
2. **버튼 생성**: 레시피 1개당 버튼 1개. 버튼은 자기 `TowerRecipe`를 클로저로 물고(=`TowerSelectPanelView.AddTowerButton`이 `tower`를 무는 방식), **onClick → `_fusionController.TryFuse(recipe)`**.
3. **버튼 표시**: 결과 타워(`recipe.Result`) 정보 — 이름은 `Result.TowerID` → `TowerData.NameKey` 로컬라이즈. (아이콘 필드가 생기면 교체.)
4. **활성 판정**: 버튼 `interactable` = **현재 지갑이 이 레시피를 충족하는가**. 실행부와 **같은 공개 함수**를 그대로 쓴다(매칭 규칙 재구현 금지):
   - `TowerFusionMatcher.CanFuse(wallet.Towers, recipe)` 성공 + `management.CanAfford(recipe.ExtraCost)`면 활성.
   - (집계만 따로 필요하면 `TowerFusionMatcher.BuildRequired(recipe)` — `TryFuse`가 쓰는 바로 그 함수.)
5. **갱신 시점**: **지갑이 바뀔 때마다** 전 버튼 재판정. `TowerWallet.OnChanged`(Add/Remove/Clear 시 발행)를 구독하면 된다 — 기존 패널이 `ManagementController.OnChanged`→`RefreshAffordability`를 구독하는 것과 동형.

### 4.4 결과 정보 패널

- 현재 선택으로 만들 수 있는 결과 타워(활성 후보 중 선택/호버한 레시피)의 `Result` 스탯을 우측에 표시 — 공격력/사거리/공속/특성. `Tower.BuildStatsText()`(TowerInfoUI 연동)와 같은 조합 규칙을 재사용하거나 `Result`의 `Single/Area/Chain/Magic` 필드에서 직접 읽는다.

---

## 5. 실행 흐름 (구현 완료 — 버튼이 부르는 대상)

```
후보 버튼 onClick → TowerFusionController.TryFuse(recipe)
  ① 지갑 타워 → TowerID 목록, 레시피 → (TowerID,개수) 집계
  ② TowerFusionMatcher.TryResolve → 소모할 타워 확정 (실패 시 중단)
  ③ ManagementController.CanAfford(recipe.ExtraCost) (없으면 무료)
  ④ TowerPlacer.BeginTowerPlacement(recipe.Result, recipe.ExtraCost, onConfirmed)
       고스트 → 타일 확정 → ExtraCost TrySpend + 결과 Instantiate
       → onConfirmed: 소모 대상 타워 Destroy + wallet.Remove
```

- **소모 시점 = 배치 확정 시점**(고스트 ESC 취소 시 재료·비용 보존). 즉시 소모로 바꾸려면 `TryFuse`에서 배치 전에 소모하도록 이동.
- 결과 위치 = 플레이어가 고스트로 새 타일 지정(TowerPlacement.md 재사용).

---

## 6. 인수 조건

**실행부(#195) — 완료**
- [x] 지갑 재료가 레시피를 충족하면 후보 실행 시 결과 타워 고스트 생성 → 타일 배치
- [x] 배치 확정 시 재료 타워 소모(Destroy) + `ExtraCost` 차감(관리 있을 때)
- [x] 재료 부족/비용 부족 시 실행 안 됨(로그)
- [x] 고스트 취소 시 재료·비용 보존
- [x] 매칭 순수 함수 검증(정확 충족/여분 허용/부족 실패/다종 재료)

**선택 UI(#183) — 예정**
- [ ] 설치 타워 복수 선택(shift) → 지갑 반영 + 선택 목록 표시
- [ ] 후보 버튼이 레시피별로 나열되고, 지갑이 충족하는 레시피만 활성
- [ ] 지갑 변경 시 후보 버튼 활성 상태 즉시 갱신
- [ ] 결과 정보 패널에 결과 타워 스탯 표시

검증: 개인 테스트 씬 Play 확인(팀 컨벤션 — 유닛 테스트 없음). 매칭 로직은 `TowerFusionMatcher`가 순수 함수라 EditMode 테스트 가능(프로젝트 첫 테스트 후보).

---

## 7. TBD / 의존

- **[#183] 복수 선택(shift)**: `MouseManager`가 현재 단일 선택 — shift 복수 선택 지원을 MouseManager에 추가하거나, 합성 선택 레이어가 `OnSelectionChanged`를 듣고 shift 상태로 누적. 계획서 §1의 "shift 차별점".
- **[#183] 선택 상태 = 지갑**: 임시 `TowerWallet`(인스펙터 드래그)을 선택 UI가 채우도록 교체. 실행부(TryFuse)는 무수정.
- ~~`TowerWallet.OnChanged` 이벤트 추가~~ — **구현됨**(Add/Remove/Clear에서 발행). 후보 버튼(#183)이 구독해 활성 갱신. 매칭 규칙도 `TowerFusionMatcher.CanFuse`/`BuildRequired` 공개로 실행부와 단일 출처화됨.
- **결과 정보 패널 배선**: `Result` 스탯 표시 규칙을 `TowerInfoUI`/`Tower.BuildStatsText`와 공유할지 별도 조합할지.
- **레시피 카탈로그 출처**: 패널 직렬화 `List<TowerRecipe>` vs `Resources.LoadAll`. 결정 후 통일.
- **밸런스·규칙(GDD §8)**: 레시피 족보(재료 조합→결과)·`ExtraCost` 수치·낮/밤 합성 허용 여부 미정.
- **결과 타워 콘텐츠**: 합성 결과용 신규 `TowerAsset`(`TowerTable.csv` 행 + 프리팹/고스트/스탯). 현재 테스트는 기존 타워(Sniper)를 결과로 재사용.
- **재료 타일 점유 해제 — 구현됨**: 배치 시 `TowerFootprint`(인스턴스 부착)가 점유 타일을 기록하고 `OnDestroy`에서 `BattleTile.Occupied`를 해제 → 합성 소모·향후 철거 시 그 타일에 재배치 가능. (일반 타워 철거 UI는 아직 없으나, 파괴 경로는 이 컴포넌트로 일반화됨.)
- **즉시 소모 옵션**: 현재 확정 시점 소모. 기획상 "버튼 누르면 즉시 재료 소멸"을 원하면 소모 시점을 앞당김(취소 시 손실 트레이드오프).

---

## 8. 확장 여지

- **다단 합성**: 합성 결과 타워를 다시 다른 레시피의 재료로(레시피가 `TowerAsset` 참조라 자연스럽게 지원).
- **레시피 조건 확장**: 재료 타워의 레벨·버프 상태 승계 여부(계획서 §1 미결) — `Tower.ApplyBuff`/`RemoveBuff` 계약과 연동 시 확장.
- **연출**: 재료 소멸 → 결과 등장 이펙트(계획서 §1).
