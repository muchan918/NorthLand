# 전투 공간 타워 합성(Tower Merge) — 기능 명세 (진실의 원천)

> **상태**: 데이터 구조(#194)·실행부(#195) **구현·검증 완료** · 선택/패널 UI(#183) **코드 구현 완료(컴파일 검증)** · 정본 씬 배선·E2E 검증 예정
> **소유**: muchan(데이터·실행 #194/#195) · n0wst4ndup(선택·패널 UI #183) · SUNGSOO(타워 프리팹/전투)
> **구현 파일 — 데이터·실행(#194/#195)**:
> - `Assets/Scripts/Data/Tower/TowerRecipe.cs` — 레시피 SO(재료/결과/추가비용)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerFusionMatcher.cs` — 포함 매칭(순수 static)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerFusionController.cs` — 실행 진입점(`TryFuse(recipe, group)`)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerPlacer.cs` — 배치 오버로드 + 배치 시 그룹 마커 부착
> - `Assets/Scripts/CombatSystem/Tower/Tower.cs` — `Asset` 읽기 접근자
> **구현 파일 — 커맨드 패턴(#263)**:
> - `Assets/Scripts/Command/IReversibleCommand.cs` — Execute/Confirm/Commit/Undo **4단 계약**(#281에서 중립 위치로 이전)
> - `Assets/Scripts/Command/ReversibleCommandBase.cs` — 4단 상태 기계 + 비용 환원 **공통 기반**(#444에서 승격)
> - `Assets/Scripts/Command/CommandHistory.cs` — 되돌리기 LIFO 히스토리(#281)
> - `Assets/Scripts/Command/UndoRequest.cs` — 되돌리기 요청 단일 진입점(버튼 · Ctrl+Z 공용, #444)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerMergeCommand.cs` — 재료 소프트 소모·확정·원복
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerFootprint.cs` — `Release()`/`Reoccupy()`(점유만 임시 해제)
> **구현 파일 — 선택·패널 UI(#183)**:
> - `Assets/Scripts/GameManager/MouseManager/IGroupSelectable.cs` — 그룹 선택 자격 마커(도메인 중립)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerGroupSelectable.cs` — 타워 마커 구현(런타임 부착)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerMergeGroup.cs` — 선택 재료 집합(순수 C#, 코디네이터 소유) — 구 `TowerWallet` 대체
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerMergeCoordinator.cs` — 선택 두뇌·게이팅·패널 권위·실행 오케스트레이션
> - `Assets/Scripts/UI/TowerPanel/TowerMergePanelView.cs` — 합성 패널(선택 리스트 + 후보 버튼)
> - `Assets/Scripts/GameManager/MouseManager/MouseManager.cs` — Shift 추가선택·`OnGroupSelectToggled`(수정)
> **관련**: GDD §5.8, `Docs/Build2/2팀 빌드 2 다음 빌드 계획.md` §1, WL-076·WL-077, 이슈 #183/#194/#195
> **참조**: `Docs/Core/TowerPlacement.md`, `Docs/Core/MouseManager.md`, `Docs/Review/SystemMap.md`(§1 TowerFusion 행·§2 API·§3 접점)
> **문서 계약**: 코드가 이 명세와 어긋나면 문서를 갱신한다(팀 계약 #7). 공개 API·계약이 바뀌는 PR은 SystemMap을 같은 PR에서 갱신한다.
>
> ⚠️ **네이밍(합성 = Merge로 통합 방향)**: 기획·이슈·GDD·문서는 **"타워 합성(Tower Merge)"** 으로 부른다. 코드는 향후 **`Merge`로 통합**할 방향이라, **신규 #183 코드는 `Merge` 접두로 시작**했다(`TowerMergeGroup`/`TowerMergeCoordinator`/`TowerMergePanelView`). 다만 #194/#195에서 먼저 병합된 `TowerFusionController`/`TowerFusionMatcher`/`TowerRecipe`는 **레거시로 남아 있다(추후 일괄 리네임 — 별건)** — 이 문서를 읽을 때 "합성 실행부/매칭 = `TowerFusion*`"으로 대응시킬 것. 이 문서가 `TowerFusion.md`를 대체·폐기하는 단일 진실 원천이다.

---

## 0. 설계 요지

- **결과 타워는 특수 타입이 아니라 일반 `TowerAsset`이다.** 합성 결과도 신규 타워 종류(§13 콘텐츠)로 `TowerTable.csv` 행 + `Towers/` SO를 만들면 기존 배치·전투 파이프라인을 그대로 탄다. 새 런타임 타입 불요.
- **레시피는 SO 전용(`TowerRecipe`)** — 재료/결과가 `TowerAsset` 참조라 CSV ID 문자열 resolve보다 인스펙터 직접 드래그가 자연스럽다(CSV 미경유).
- **선택 UI와 실행부의 유일한 이음매 = 재료 집합(`TowerMergeGroup`, 순수 C#·코디네이터 소유).** 선택(#183)은 "선택된 재료 타워를 집합에 넣고 빼는 것"까지만 하고, 그 다음(매칭·비용·소모·배치)은 실행부(#195)가 이미 처리한다. **집합이 이음매**라 실행부는 선택 UI가 붙어도 매칭/배치 로직 무수정(시그니처만 `TryFuse(recipe, group)`으로 그룹을 받음).
- **입력은 MouseManager 단일 창구(계약 #1), 선택 집합 소유는 코디네이터, 대상 자격은 마커.** MouseManager는 "그룹 선택 참여 가능"을 마커 인터페이스로만 알고 "타워"라는 도메인은 모른다(제네릭 유지, SystemMap §6).
- **결과 배치는 `TowerPlacer` 고스트 흐름을 재사용한다**(TowerPlacement.md §7) — 일반 타워 배치와 동일한 UX·검증·자원 게이트웨이.
- **합성은 낮(배치 페이즈) 전용** — 타워 배치와 같은 축(§10, WL-077).

---

## 1. 목적

낮 페이즈에 플레이어가 **전투 공간에 설치된 타워 여러 개를 재료로 선택**하고, 그 조합으로 만들 수 있는 **상위(결과) 타워 후보 버튼**을 눌러 합성하는 상호작용. 같은 타워를 계속 까는 대신 설치물을 재료로 성장시키는 **전투 공간의 성장 축**(GDD §5.8). 밤 수비 전에 낮 동안 전력을 재구성하는 전략 레이어를 만든다.

---

## 2. 범위

**In — 구현·검증 완료 (#194 데이터 / #195 실행)**
- 레시피 데이터(`TowerRecipe`: 재료 TowerID별 개수 → 결과 `TowerAsset` + 추가비용 `ExtraCost`)
- 포함 매칭(`TowerFusionMatcher`) — 선택 ≥ 필요면 성립, 소모는 필요 개수만, 여분 허용
- 실행(`TowerFusionController.TryFuse(recipe, group)`): 매칭 검증 → `CanAfford` → **재료 소프트 소모(#263)** → `TowerPlacer` 고스트 배치 → 확정 시 `ExtraCost` 지불 + 재료 진짜 파괴 / 취소 시 재료 원복
- (구 임시 홀더 `TowerWallet`은 #183에서 `TowerMergeGroup`으로 대체·폐기)

**In — 구현·검증 완료 (#263 커맨드 패턴)**
- 소모 시점을 배치 확정 → **후보 버튼 클릭**으로 이동. **재료가 점유했던 타일에 결과를 놓을 수 있다**(구 F2 제약 해소)
- 되돌리기: 배치 취소 시 재료 타워 재활성화 + 타일 재점유(`IReversibleCommand` 트랜잭션). **#281에서 4단이 되면서 확정한 합성도 밤 전까지 되돌릴 수 있다**(§9.3)
- 배치 중 핑크 고정(`_previewCommitted`) 제거 — 칠할 대상이 클릭 순간 사라져 목적을 잃음(§8.4)

**In — 코드 구현 완료 (#183, 씬 배선·E2E 예정)**
- 멀티 선택 모델: 설치 타워를 수정키로 추가 선택 → 순서 있는 재료 집합(§7)
- 패널 스왑: 1개=타워 인포 패널 / 2개 이상=합성 패널(§8)
- 합성 패널: 선택 리스트(상단) + 후보 버튼(하단, 매칭 시 활성)(§8)
- 낮 전용 게이팅 + 밤/씬 전환 리셋(§10)
- 선택 타워 월드 하이라이트(아트 TBD)(§8.4)

**Out — 후속/미결**
- 드래그 범위 선택 입력(§13)
- 합성 결과 정보 패널의 확정 UX·스탯 조립 공유(§8.3, 선택)
- 레시피 족보·`ExtraCost` 수치·다단 합성 밸런스(GDD §8, §13)
- 결과 타워 전용 콘텐츠(신규 `TowerAsset`)(§13)

---

## 3. 네이밍 매핑 (문서 개념 ↔ 코드 식별자)

| 문서·기획 개념 | 코드 식별자 | 비고 |
| --- | --- | --- |
| 타워 합성 / Merge | (기능명 — 코드 통합 목표 접두는 `Merge`) | 신규=Merge, 기존=Fusion 레거시 |
| 합성 실행부 | `TowerFusionController` | 레거시(Fusion). `TryFuse(recipe, group)` |
| 매칭 규칙 | `TowerFusionMatcher` | 레거시(Fusion). 순수 static, `TryResolve`/`BuildRequired`/`CanFuse` |
| 레시피 | `TowerRecipe` (SO) | 레거시(접두 없음). `Materials`/`Result`/`ExtraCost` |
| 재료 집합(선택 상태) | `TowerMergeGroup` | 순수 C#·코디네이터 소유. `Towers`/`Add`/`Remove`/`Clear`/`Prune`/`OnChanged`. 구 `TowerWallet` 대체 |
| 되돌릴 수 있는 조작 | `IReversibleCommand` | `Execute`/`Confirm`/`Commit`/`Undo` **4단**(#263→#281). 구현체는 합성·배치·**경영**(#444) 셋이고, 상태 기계는 `ReversibleCommandBase`가 공유한다 |
| 되돌리기 히스토리 | `CommandHistory` | LIFO 20. `Confirm`이 등록 시점, 밤 진입에 일괄 `Commit`(#281). **#444로 경영 조작도 이 스택을 쓴다** — 합성과 건물 업그레이드가 한 순서에 섞인다 |
| 재료 소모 트랜잭션 | `TowerMergeCommand` | 소프트 소모(타일 `Release`+비활성화) → `Confirm`=세션 성공(재료는 살아 있다) → `Commit`=밤에 진짜 파괴 / `Undo`=결과 회수+재료 원복 |
| 선택 코디네이터 | `TowerMergeCoordinator` | 그룹 소유·게이팅·패널 권위·실행 오케스트레이션(파사드: `SelectedTowers`/`OnGroupChanged`/`CanMerge`/`RequestMerge`) |
| 합성 패널 뷰 | `TowerMergePanelView` | 선택 리스트 + 후보 버튼. 코디네이터만 참조 |
| 레시피 카탈로그 | `TowerRecipeCatalog` | `All` = `Resources.LoadAll<TowerRecipe>("ScriptableObjects/TowerRecipes")`(lazy 1회) |
| 재료→결과 역방향 색인 | `TowerMergeTargetIndex` | 순수 static. `RecipesUsing(towerId)` — 정보 패널 "상위 타워" 블록용(§8.5) |
| 상위 타워 행 뷰 | `TowerMergeTargetSlot` | 아이콘+이름 2슬롯. 툴팁은 `TowerTooltipSource` 런타임 부착 |
| 타워 표시 이름 | `TowerDisplayName` | `Of`/`EnsureData`. 합성 패널·도감·정보 패널·툴팁 공용 단일 출처(§8.5) |
| 그룹 선택 자격 마커 | `IGroupSelectable`(도메인 중립) + `TowerGroupSelectable`(타워 구현) | MouseManager가 마커만 소비 → 타워 개념 없이 제네릭 |
| 결과·재료 타워 정의 | `TowerAsset` (SO) | 결과도 일반 타워 |
| 배치 타워의 원본 SO 조회 | `Tower.Asset` | 읽기 전용 |

> 네이밍 정책: 신규 #183 코드는 `Merge` 접두로 시작(코드 전체를 Merge로 통합하는 방향). 기존 #194/#195의 `TowerFusion*`/`TowerRecipe`는 레거시로 남아 있고 추후 일괄 리네임(별건).

---

## 4. 아키텍처 개요

```
[플레이어 클릭/수정키]
        │  (입력 단일 창구 — 계약 #1)
        ▼
   MouseManager ── 수정키 없음 ──▶ 평클릭 (OnPrimarySelect·항상 발행) ────┐
        │                                                          │
        └─ 수정키 + 마커(IGroupSelectable) ─▶ 그룹 토글 이벤트 ──┐  │
                                                                │  │
                                                                ▼  ▼
                                            [선택 코디네이터] (순서 있는 재료 집합 소유)
                                                     │  낮 게이팅·리셋(§10)
                                       ┌─────────────┼──────────────┐
                                       ▼             ▼              ▼
                                TowerMergeGroup  그룹 하이라이트   패널 스위처
                                  (이음매·순수C#)  (마커 훅)       (0/1/≥2 분기)
                                       │                              │
                    ┌──────────────────┘                             ▼
                    ▼                                    1개=TowerInfoUI / ≥2=합성 패널
        TowerFusionMatcher.CanFuse ── 후보 버튼 활성 판정 ◀── 합성 패널(후보 버튼)
                                                                      │ onClick → 코디네이터.RequestMerge
                                                                      ▼
                                              TowerFusionController.TryFuse(recipe, group)
                                                 매칭 → CanAfford
                                                 → 커맨드 Execute (재료 즉시 소프트 소모·타일 해제)
                                                 → TowerPlacer 고스트 배치
                                                    ├ 확정 → ExtraCost 지불 + Commit(재료 파괴)
                                                    └ 취소 → Undo(재료 원복)
```

**흐름 요약**: 선택(MouseManager+마커) → 집합 소유(코디네이터가 `TowerMergeGroup`) → 이음매(그룹) → {버튼 활성 판정 = 매칭, 실행 = 컨트롤러}. 버튼 활성 판정과 실행이 **같은 매칭 함수**(`TowerFusionMatcher`)를 공유해 규칙이 단일 출처다.

---

## 5. 데이터 모델 (#194, 완료)

- **`TowerRecipe`(SO)**: `List<MaterialEntry> Materials`(재료 `TowerAsset`+`Count`, multiset) / `TowerAsset Result` / `List<ResourceCost> ExtraCost`(합성 추가 자원/마나석). CSV 미경유 인스펙터 손입력.
- **결과 타워**: 별도 특수 타입이 아니라 일반 `TowerAsset`. 신규 결과 타워는 `TowerTable.csv` 행 + SO + 프리팹/고스트/스탯을 추가(§13 콘텐츠).
- **레시피 카탈로그(전체 열거)**: #183 후보 버튼 패널이 순회·매칭하려면 전체 레시피 목록이 필요하다. **출처 = `TowerRecipeCatalog.All`** = `Resources.LoadAll<TowerRecipe>("ScriptableObjects/TowerRecipes")`(lazy 1회 캐시). 폴더에 SO를 넣으면 자동으로 후보에 들어간다 — 레시피가 13종으로 늘면서 인스펙터 등록 누락이 반복돼(등록을 잊은 SO가 조용히 후보에서 빠진다) 자동 열거로 바뀌었다.
  - ~~**출처 = 패널의 인스펙터 직렬화 배열 `[SerializeField] TowerRecipe[] _recipes`**~~ — 폐기(등록 대상 명시 통제 < 누락 방지). WL-076(a) 축.
  - ⚠ **버튼 순서가 비결정적이 됐다** — `Resources.LoadAll`은 순서를 보장하지 않으므로, 인스펙터 배열이 제공했던 결정적 순서(F6)의 근거가 사라졌다. **미해소**: 후보 버튼은 아직 정렬하지 않는다. §8.5의 상위 타워 행은 뷰에서 (등급 → 표시 이름)으로 정렬하므로 영향받지 않는다 — 후보 버튼도 같은 규칙으로 맞추는 것이 남은 일이다.

---

## 6. 매칭 규칙 — 포함 매칭 (#194, 완료)

- 레시피 재료를 **`(TowerID, 필요개수)`** 로 집계(`TowerFusionMatcher.BuildRequired`, 같은 종류가 여러 엔트리로 나뉘어도 합산, 무효 엔트리 무시).
- 선택 집합의 타워를 `Tower.Asset.TowerID`로 읽어, 레시피의 모든 `(종류, 필요개수)`를 **모두 포함**하면(선택 개수 ≥ 필요 개수) 성립.
- **여분 허용**: 레시피에 없는 종류·초과분이 섞여도 충족 유지. **소모는 필요 개수만큼만**(`TryResolve`가 소모 인덱스를 정확히 반환).
- **여러 레시피 동시 충족 → 여러 후보 버튼 동시 활성.**
- 후보 버튼 활성 판정 = `TowerFusionMatcher.CanFuse(group.Towers, recipe)`(코디네이터 파사드 `CanMerge(recipe)` 경유) — **실행부와 같은 함수**를 써 규칙 재구현을 금지(단일 출처).

---

## 7. 선택 모델 (#183, 구현됨) — 코디네이터 + 마커

### 7.1 소유·자격 (확정 아키텍처)
- **선택 집합 소유 = `TowerMergeCoordinator`**(MonoBehaviour). 순서 있는 재료 집합 `TowerMergeGroup`(순수 C#)을 내부에 소유한다(선택 순서 = 등록 순서). MouseManager는 집합을 들지 않는다.
- **그룹 선택 자격 = 도메인 중립 마커 `IGroupSelectable`**. 타워는 별도 컴포넌트 `TowerGroupSelectable`로 이를 구현하고(`Tower.cs`(Combat) 무편집), 건물·영지 노드 등 다른 `ISelectable`은 구현하지 않는다 → MouseManager는 "타워"를 모른 채 마커 유무로만 판정(제네릭 유지, SystemMap §6). 마커는 `TowerPlacer`가 타워 배치 시 런타임 부착(`AddComponent`, `TowerFootprint`와 동일 지점)한다.
- 마커는 그룹 하이라이트 훅 `OnGroupSelected()`/`OnGroupDeselected()`를 **단일 선택 훅(`ISelectable.OnSelected/OnDeselected`)과 분리**해 노출한다 — 코디네이터가 집합 가감 시 호출(§8.4).

### 7.2 MouseManager 계약 확장 (입력 단일 창구)
현재 `MouseManager`는 완전 단일 선택(`_selected` 단일 참조)이다. #183은 다음을 **추가**한다(기존 `OnSelectionChanged(ISelectable)` 시그니처는 무변경 — 기존 구독자 보호):

- **추가 선택 키 = Shift**(확정, 필요 시 재조정). 키 판정은 MouseManager가 소유(게임플레이 코드의 `Keyboard.current` 직접 폴링 금지, 계약 #1). *WL-073 유의: 우클릭이 카메라 드래그와 이미 이중 점유 → 추가 선택 키를 우클릭이 아닌 Shift로 두어 충돌을 피한다.*
- **그룹 토글 이벤트**(예: `OnGroupSelectToggled(IGroupSelectable)`) 신설: **Shift + 마커 대상** 클릭 시 발행. 발행 직전에 **`Select(null)`로 단일 `_selected`를 비운다**(WL-087 수정, 원안은 "건드리지 않음"이었다). 이후 무엇을 보일지는 §8.1 스위처가 집합 크기로 결정하므로, 단일 선택 상태를 남겨두면 그 부수 표시(사거리 원·인포)를 아무도 못 내린다. **마커 없는 대상(건물·빈 곳)에는 적용하지 않는다** — 집합이 안 바뀌는데 `_selected`만 비면 "집합엔 있는데 화면엔 아무것도 없는" 어긋난 상태가 된다. 순서도 계약이다: 토글 **뒤**에 비우면 `count==1` 복귀에서 스위처가 켠 인포·원을 도로 끈다.
  - 부수 효과: 건물처럼 그룹에 못 들어가는 대상을 선택해 둔 채 Shift로 타워를 담기 시작해도 그쪽 사거리 원·패널이 함께 정리된다. 스위처는 `Tower`만 알기 때문에 이 경로가 아니면 못 잡는다.
  - **#164 리팩토링 반영**: 마법 타워(오라)도 이제 단일 `Tower` 타입이라 그룹 선택에 정상적으로 담긴다 — 예전에는 별개 `AuraTower` 클래스라 `sel is Tower`/`TowerGroupSelectable.Tower`에 걸리지 않아 조용히 제외됐고, 평클릭 시 담아둔 그룹이 해제되는 부작용도 있었다(구 WL-131). 실제로 **레시피 재료가 되는지는 `TowerRecipe` 저작 문제**로 분리됐다(코드가 막지 않는다).
  - 밤에는 코디네이터가 토글을 무시하므로(§10 게이팅) Shift+타워 클릭이 "단일 선택 해제"로만 끝난다 — 밤에 합성이 잠긴 상태에서의 무의미한 입력이라 의도된 동작으로 둔다.
- **평클릭·빈 곳 해제 = `OnPrimarySelect` 신설**(F3 + WL-085): 평클릭(해석된 `ISelectable`)·빈 곳 클릭 시 `OnPrimarySelect(ISelectable|null)`를 **중복 제거 없이 항상** 발행한다. 코디네이터가 이걸로 그룹을 리셋(타워면 `SetSingle`)/해제(그 외·null)한다. → 기존엔 이 신호를 `Select(null)`의 `OnSelectionChanged`로 받으려 했으나 `if (_selected == next) return;` 중복 제거에 삼켜졌다(**Shift로만 선택 시 `_selected==null` → 빈 곳 해제 불발**, 이미 선택된 타워 재평클릭 시 단일화 불발 — WL-085). `OnSelectionChanged`는 기존 단일 선택 구독자용으로 그대로 두고, 그룹 경로만 이 새 이벤트로 분리. **우클릭은 해제에 쓰지 않는다**(카메라 드래그 이중 점유 WL-073, 이슈 AC에서 의도적 이탈 — F3).

### 7.3 입력 규칙 (이슈 §상세)
| 입력 | 동작 |
| --- | --- |
| 키 없이 타워 클릭 | 집합 전체 해제 후 그 타워 **단일 선택** |
| Shift + 미선택 타워 | 단일 선택 해제 후 집합 **끝에 추가**(순서 보존) |
| Shift + 이미 선택된 타워 | 단일 선택 해제 후 집합에서 **토글 제거**(나머지 순서 유지) |
| Shift + 건물/영지 노드 등 비-타워 | **무시**(집합·단일 선택 둘 다 불변 — 마커 없음) |
| 빈 곳 클릭 | **전체 해제** |
| 우클릭 | 해제 아님 — 카메라 드래그·배치/조준 취소 전용(WL-073, F3) |
| (입력 아님) 배치 시작 | **전체 해제** — `MouseManager.BeginPlacement`가 `ClearSelection()`을 호출한다(WL-086). 자원 배치·합성 배치 모두 해당하며, 고스트를 든 화면에 이전 선택의 사거리 원·초록·인포/합성 패널이 남지 않는다 |

### 7.4 집합 = `TowerMergeGroup` (이음매, 단일 리스트)
- 코디네이터는 **순수 C# `TowerMergeGroup` 하나를 유일한 백킹 스토어로 직접 조작**한다(`Add`/`Remove`/`Clear`/`Prune`)(F4). 별도 동기화 리스트가 없어 어긋날 표면이 없다. 그룹의 `OnChanged`(Add/Remove/Clear/Prune 성공 시 발행) 하나로 하이라이트·패널·실행부 소모까지 모든 변경이 단일 통지된다 — 코디네이터가 구독해 `RefreshHighlight`/`RefreshPanel`/`OnGroupChanged` 발행.
- 실행부(`TowerFusionController.TryFuse(recipe, group)`)·매칭(`TowerFusionMatcher`)은 매칭/배치 로직 **무수정**으로 이 그룹의 `Towers`를 소비. 실행부는 재료 소모 시 `group.Remove(t)`를 부르며, 이 역시 `OnChanged`로 UI가 자동 갱신된다.
- MouseManager가 넘기는 것은 도메인 중립 `IGroupSelectable`(**Tower 미노출** — 마커는 `OnGroupSelected/OnGroupDeselected`만)이므로, 코디네이터가 `grp is TowerGroupSelectable`로 캐스팅해 `Tower`를 얻어 `_group.Add(tower)`. 평클릭 통지(`OnPrimarySelect`)에서는 `sel is Tower`면 `SetSingle(tower)`, 아니면 `Clear()`. 재료 식별은 `tower.Asset.TowerID`, `Tower.Asset`이 null인 항목은 실행부에서 제외.

---

## 8. 패널 UX (#183, 구현됨)

### 8.1 패널 스왑 (오른쪽 패널 한 자리) — 스위처가 단일 권위
**우측 패널의 최종 결정권은 스위처 하나**로 못박는다(F1, 집합 크기 이벤트 구독). 기존 단일선택 경로(`Tower.OnSelected/OnDeselected`→`TowerInfoUI`, #153)와 경합하지 않도록, 스위처가 집합 크기로 3분기하고 인포 표시/숨김은 **idempotent**하게 다룬다:
- **0개** → 두 패널 모두 숨김(`TowerInfoUI.HideInfo()` + 합성 패널 off).
- **1개** → 인포 패널만. 그 타워의 정보 표시는 **멤버 타워의 `OnSelected()`를 (재)호출해 재사용**한다(스탯 조립을 재구현하지 않음). 특히 **2→1 축소**는 단일선택 `OnSelected`가 재발화되지 않으므로 스위처가 명시적으로 호출.
- **2개 이상** → 합성 패널 표시 + 스위처가 **능동적으로 `TowerInfoUI.HideInfo()`**(직전 단일선택이 띄워둔 인포를 확실히 내림).

**`OnSelected`를 직접 부르면 `OnDeselected`도 스위처가 진다**(WL-087 수정). `TowerInfoUI.HideInfo()`만으로는 부족하다 — 이 훅 쌍에는 정보 패널뿐 아니라 **사거리 원**(#192, `Tower`가 자식 GO로 소유)이 함께 묶여 있고, 스위처는 남의 사거리 원을 직접 끄는 창구가 없기 때문이다. 인포를 띄워준 대상을 `_infoShownFor` 슬롯 하나로 기억하고, 대상이 바뀌거나(1→다른 타워) 사라질 때(1→0, 1→2+, 밤 리셋) 그 대상의 `OnDeselected()`를 부른다 — `RefreshHighlight`의 `_highlighted` diff와 같은 패턴의 1개짜리 축소판이며, 파괴된 참조는 Unity 오버로드 `==`로 거른다.

두 패널은 **동시에 보이지 않는다.** `_selected`(MouseManager) vs 집합(코디네이터)의 관계: **평클릭 경로에선 사실상 일치, Shift 경로에선 `_selected`가 비고(§7.2) 집합만 남는다.** 표시/숨김이 idempotent라 기존 MouseManager 경로가 같은 인포를 한 번 더 켜/꺼도 무해 — 단 "무엇을 보일지"의 판단은 항상 스위처가 이긴다.

### 8.2 합성 패널 구성
- **상단 Vertical Scroll View — 선택 리스트**: 선택된 재료 타워를 **선택 순서대로** 한 행씩. 집합 변경 시 즉시 갱신. 행 라벨 = `tower.Asset.TowerID` → `TowerData.NameKey` → 로컬라이즈(`NorthLand_Towers`, `LocalizationHelper.Get`). (행별 제거 버튼은 선택.)
- **하단 Horizontal Scroll View — 후보 버튼**: **레시피(카탈로그)마다 버튼 1개를 미리 생성해 담아두고 기본 `SetActive(false)`**. 매칭되는 레시피의 버튼만 `SetActive(true)`.
  - 활성 판정 = `_coordinator.CanMerge(recipe)`(= `TowerFusionMatcher.CanFuse(group.Towers, recipe)`). (매칭 규칙 재구현 금지 — §6 단일 출처.)
  - **표시(재료)와 활성(코스트)을 가른다** — `SetActive` = `CanMerge`(재료 매칭), `interactable` = `_coordinator.CanAffordMerge(recipe)`(#406, WL-209). 예전에는 표시 조건이 재료뿐이라 **자원이 모자라도 버튼이 눌렸고 눌러도 조용히 반려**됐다. 거절음이 붙은 뒤에도 재료 부족과 코스트 부족이 같은 클립을 공유해 사유를 구분할 수 없었다 — 회색 표시가 그 자리를 대신한다. **최종 검증은 여전히 실행부(`TryFuse` 4단계)가 한다**(방어): 그룹이 판정과 클릭 사이에 바뀔 수 있다.
    - 판정 경로는 `뷰 → 코디네이터(CanAffordMerge) → 실행부(TowerFusionController.CanAfford) → ManagementController.CanAfford`다. **뷰가 경영 시스템을 직접 부르지 않는다**(팀 계약 #6 · §8 파사드), 그리고 코스트 규칙(`recipe.ExtraCost`)을 아는 실행부가 답해야 `TryFuse`와 식이 갈리지 않는다.
    - 경영이 없는 씬(테스트)에서는 무료라 항상 활성이다.
  - **버튼 표시 = 결과 타워(`recipe.Result`) 아이콘 + 이름**(#445). 둘 다 프리팹(`TowerButton.prefab` — 배치 팔레트와 같은 것)의 `TowerButtonView.Set(Sprite, string)` 슬롯에 채운다. 이름은 `TowerDisplayName.Of`(단일 출처, §8.5), 아이콘은 `TowerAsset.Icon`(미할당이면 슬롯 off — 흰 사각형보다 빈 칸이 낫다는 `ResourceAsset` 계보 규약).
    - 도입 시엔 `GetComponentInChildren<TMP_Text>()`로 **라벨만** 채웠다(아이콘 필드가 없던 시절 규약). `TowerAsset.Icon`이 생기고 실사용 타워 19종이 전부 채워진 뒤에도 이 경로가 남아, 후보 버튼은 테두리 안이 빈 칸이라 무슨 타워인지 그림으로 알 수 없었다.
    - `SetLocked`는 부르지 않는다 — 합성 후보에는 **해금** 개념이 없다. 원본 프리팹의 `TowerLockOverlay`는 `m_IsActive: 0`이라 그대로 조용하다(§8.5 `TowerMergeTargetSlot`이 같은 이유로 그 컴포넌트를 떼어낸 것과 같은 판단).
  - **호버 툴팁의 코스트 슬롯 = 소모될 재료 타워**(#445, `TowerMergeCandidateHover` → `TowerTooltipView.Show(..., recipe)`). 배치 팔레트 버튼은 그 자리에 **자원**을 노란 줄로 내지만, 합성은 **자원이 아니라 타워가 나간다** — 결과 SO의 `Cost`는 지불되지 않으므로(§9: `TryFuse`는 `ExtraCost`로 배치를 연다) 그걸 그대로 그리면 합성 전용 타워는 `Cost`가 비어 있어 노란 줄이 통째로 사라지고 합성이 "공짜"로 보인다.
    - 표기 = `재료`(로컬라이즈 키 `game.merge.materials`, `NorthLand_default`) 다음 `타워명 x수량` 줄 나열, 이어서 `ExtraCost`가 있으면 자원 줄. 라벨을 붙이는 이유는 이 슬롯이 평소 자원을 그리는 자리라 라벨이 없으면 타워 이름이 자원명으로 읽히기 때문이다.
    - **재료 집계는 `TowerFusionMatcher.BuildRequired`** — §6 단일 출처. 여기서 다시 세면 "툴팁엔 2개인데 실제로는 3개를 먹는" 어긋남이 생긴다.
    - ⚠ **표시하는 것은 레시피의 요구량이지 "지금 선택한 것 중 무엇이 소모되는가"가 아니다.** 후보 버튼은 매칭될 때만 켜지므로 둘이 실질적으로 같고, 실제 소모 대상은 핑크 프리뷰(§8.4)가 월드에서 가리킨다.
  - **onClick → `_coordinator.RequestMerge(recipe)`**(코디네이터가 그룹을 물려 `TryFuse(recipe, group)` 호출). 버튼이 자기 `TowerRecipe`를 클로저로 물음.
  - **갱신 시점 = 그룹이 바뀔 때마다** 전 버튼 재판정 — `_coordinator.OnGroupChanged` 구독(패널이 활성일 때. 코디네이터는 내부적으로 `TowerMergeGroup.OnChanged`를 이 이벤트로 포워딩). 패널은 `OnEnable`에서도 현재 상태로 1회 동기화.
    - **자원이 바뀔 때도 후보만 다시 칠한다** — `_coordinator.OnAffordabilityChanged`(실행부가 `ManagementController.OnChanged`를 받아 파사드로 다시 냄). 패널이 열린 채 자원이 변하는 경로가 실제로 있다: **되돌리기(Ctrl+Z)의 자원 환불**. 이게 없으면 "자원은 충분한데 버튼이 회색"이 되어, 클릭해도 `interactable=false`라 거절음조차 나지 않는다. 선택 리스트는 그대로이므로 행 재생성 없이 `RefreshCandidates`만 돈다.
  - **UX 트레이드오프(경미)**: `SetActive` 방식은 비매칭 버튼이 사라져 스크롤뷰가 리플로우된다(선택 변경마다 버튼이 튀어나왔다 사라짐). 이슈가 택한 방식이라 유지하되, 튐이 거슬리면 '전체 표시 + `interactable`로 회색' 대안 고려. 또 **여분 허용 시 실제 소모될 재료가 무엇인지**(선택 순서 index로 결정)는 리스트에 표시되지 않음 — 후속 폴리시(호버 시 소모 대상 하이라이트).

> **주의**: 이 하단 후보 버튼 영역은 **배치 팔레트(`TowerSelectPanelView`, 새 타워 건설 선택)와 다르다.** 합성 패널은 이미 배치된 타워들의 조합 결과를 보여준다. 골격은 `TowerSelectPanelView`를 참고 모델로 삼되(버튼 동적 생성·조건부 활성·클릭 시 배치 진입), 대상이 `List<TowerRecipe>` + 매칭 여부 + `TryFuse`로 바뀐다.

### 8.3 결과 정보 패널 (선택, 후속)
현재 선택으로 만들 수 있는 결과 타워(활성 후보 중 선택/호버한 레시피)의 `Result` 스탯을 표시. `Tower`의 스탯 텍스트 조립 규칙과 공유할지 별도 조합할지는 미결(WL-079 스탯 표시 다중화 축과 함께). #183 완료기준에는 없음.

### 8.4 시각 피드백
- 집합에 든 타워를 월드에서 강조(아웃라인/하이라이트). 코디네이터가 마커의 그룹 훅(§7.1)으로 켜고 끈다 — **단일 선택 하이라이트와 별개**. 아트·연출 방식 TBD.
- 색 규약은 `InteractionOutline.md`(#213): 그룹 재료 = 초록, **그 레시피가 실제로 소모할 재료 = 핑크**.
- 핑크는 후보 버튼 **호버 동안만** 켜진다. 예전에는 클릭 후 배치가 끝날 때까지 고정하는 잠금(`_previewCommitted`)이 있었는데, 클릭 순간 커서가 버튼을 벗어나며 "무엇이 소모되는지"가 사라지는 것을 막기 위한 것이었다. **#263이 소모를 클릭 시점으로 앞당기면서 칠할 대상 자체가 그 순간 없어져 잠금이 목적을 잃었다** — 무엇이 소모됐는지는 재료가 비워진 자리가 말해준다. 상세는 `InteractionOutline.md` §5.3.
- 소모→배치 구간의 시각적 공백은 연출(#265)이 채운다 — **구현됨, §9.2**. 재료가 있던 자리에 흰 입자가 떠 있으므로, 핑크 고정이 하던 "무엇을 소모했는지가 배치 내내 보인다"를 그대로 이어받는다.

### 8.5 재료 → 상위 타워 역방향 표시 (구현됨)

타워 **1개**를 선택하면(§8.1의 `count == 1` 분기 = `TowerInfoUI`) 그 타워를 재료로 쓰는 **상위 타워 목록**을 정보 패널 하단 "합성 후보" 블록에 띄운다. 자리(`_mergeContainer`/`_mergeContent`)는 #153에서 미리 잡아둔 것이고, 여기서 채우는 로직이 붙었다.

**왜 필요한가**: 합성 패널(§8.2)은 2개 이상 선택해야 열리므로, 플레이어가 **먼저 조합을 알아야** 재료를 모을 수 있다. 도감(`FusionTowerCodexUI`)이 그 역할을 하지만 "결과 → 필요한 재료" 방향이라, 손에 든 타워에서 출발할 수 없다.

도감은 결과 타워에서 필요한 재료 레시피를 역조회하고, 이름·역할·설명·능력치는 툴팁과 같은 `NorthLand.UI.TowerInfoFormatter.BuildHeader/BuildDescription/BuildStats`를 사용한다. 두 화면의 표시 규칙을 따로 조립하지 않는다.

- **조회 방향이 셋이다**: 선택 집합→레시피(`TowerFusionMatcher.CanFuse`, 합성 패널) / 결과→재료(`FusionTowerCodexUI.recipeByResult`, 도감) / **재료→결과(`TowerMergeTargetIndex`, 이 절)**.
- **색인 = `TowerMergeTargetIndex.RecipesUsing(towerId)`**(순수 static, lazy 1회 구축). 재료 집계는 **`TowerFusionMatcher.BuildRequired`를 쓴다** — §6 단일 출처. 재료 판정을 두 벌로 구현하면 "정보 패널엔 상위 타워로 뜨는데 실제로는 재료로 안 걸리는" 어긋남이 생긴다.
  - `Result`가 없는 레시피는 색인에서 제외한다(행을 그릴 결과 타워가 없다 = 저작 실수).
- **칸 = 결과 타워 아이콘 하나**(`TowerMergeTargetSlot`). **이름 칸은 의도적으로 없다** — 정보 패널은 폭이 좁아 이름 배너까지 넣으면 칸이 커져 한 줄에 몇 개 안 들어가고 블록이 패널을 통째로 밀어낸다. **이름은 호버 툴팁이 낸다**(아래). 정본 프리팹 `@NorthLand/Prefabs/UI/TowerTargetSlot.prefab`은 `Slot`·`Img_Bg`·`Img_Icon`만 갖고 `TowerMergeTargetSlot._name`은 **비워두는 것이 정상**이다(컴포넌트는 이름 칸을 가진 변종을 위해 선택 슬롯으로 남겨 뒀다).
  - **겉모습은 배치 팔레트 칸과 같은 계보다** — 프리팹은 `TowerButton.prefab`을 복제해 `Button`·`TowerButtonView`·`TowerLockOverlay`·**배너 서브트리**를 떼고 이 컴포넌트를 붙인 것이다. 같은 아이콘을 같은 테두리로 보여주되 **누를 수는 없는** 칸이다.
  - 팔레트의 `TowerButtonView`를 그대로 쓰지 않는 이유: `SetLocked`·해제 연출이 배치 팔레트의 **해금** 개념과 한 몸이고 정보 패널에는 잠금이 없다. (원본 프리팹의 `TowerLockOverlay`는 `m_IsActive: 0`이라 남겨둬도 조용하지만, 배선할 슬롯이 늘고 의도가 흐려진다.)
  - ⚠ **아이콘 전용은 "이름을 못 보여준다"가 아니라 "이름을 툴팁으로 옮겼다"다.** 아이콘이 비슷한 2·3차 타워가 늘면 칸만 보고는 구분이 어려우므로, **`TowerAsset.Icon`이 서로 구별되게 저작돼 있어야 한다는 전제가 이 결정에 딸려 온다**(`TowerAddGuide.md` §3.5 `Icon` 항목). 구분이 실제로 안 되기 시작하면 그때 이름 칸을 가진 변종 프리팹으로 바꾸면 되고, 코드는 이미 그 경로를 받는다.
  - 상세 스탯·코스트·**이름**은 **호버 툴팁**이 맡는다 — `TowerTooltipSource`를 런타임 부착해 재사용하므로 칸 프리팹에 툴팁 배선이 없다(§8.2 후보 버튼·`TowerSelectPanelView`와 같은 선례). 칸 안에 `Raycast Target`이 켜진 `Image`가 하나 있어야 호버가 잡히는데, 복제 원본의 `Slot/Img_Bg`가 이미 그래서 `Button`을 떼도 유지된다.
    - **레시피를 함께 넘긴다**(`Init(result, recipe)`, #445) — 여기 뜨는 타워는 합성으로만 얻으므로 자원 코스트가 비어 있고, 툴팁이 낼 수 있는 유일한 코스트가 "무슨 타워 몇 개"다. 이 블록의 존재 이유가 "이 타워로 무엇을 만들 수 있나"인데 **무엇이 더 필요한지**를 안 보여주면 반쪽이다(표기 규칙은 §8.2 후보 버튼과 같다).
    - ⚠ `TowerInfoUI`가 `Set(result.Icon, TowerDisplayName.Of(result))`에서 **이름을 계속 넘기는 이유**: 정본 칸은 그 값을 버리지만 그 호출이 `EnsureData`로 `result.Data`를 채워 **툴팁이 이름·역할·설명 키를 읽게** 한다. "안 쓰는 인자"로 보고 지우면 툴팁이 `TowerID`로 떨어진다.
  - **클릭 동작은 없다**(의도). 우측 패널의 최종 결정권은 스위처 하나라는 §8.1 계약을 건드리지 않으려면, 칸이 패널을 갈아치우는 경로를 만들지 않는 게 맞다.
  - ⚠ **칸 프리팹은 별도 저장소(`NorthLand-Imported`) 소속이다** — 미동기 환경에서는 `_mergeSlotPrefab` 참조가 풀려 블록이 조용히 사라진다. `TowerInfoUI.HasMergeSlotWiring`이 **1회 경고**를 남겨 "배선 누락"과 "미동기"를 구분해 주고, 동기화 계약은 `SystemMap.md` §4에 등재돼 있다(#445, Imported `85fd857d3` 이상).
- **표시 순서 = 등급 → 표시 이름**(도감 `LoadData`와 같은 규칙 → 두 화면의 순서가 일치). **정렬은 뷰가 한다** — 이름 정렬이 로케일 의존이라, 로케일을 모르는 색인이 미리 정해두면 언어를 바꿀 때 어긋난다.
  - ⚠ 합성 패널(§8.2)의 후보 버튼은 아직 `Resources.LoadAll` 순서라 **같은 기능의 두 블록이 순서 규칙이 다르다**(§5 ⚠). 버튼 쪽에 정렬을 넣을 때 `TowerInfoUI.CompareByRarityThenName`을 그대로 쓸 것.
- **가시성 필터가 생기면 색인이 아니라 도메인 쪽에 둔다**(선결 합의). 정보 패널은 지금 코디네이터 파사드를 우회해 static 색인을 직접 부른다 — 읽기 전용이고 판정이 없으므로 무해하지만, GDD §5.8이 TBD로 남긴 족보가 확정되며 "무엇을 보여줄 것인가"에 조건이 붙는 순간(미발견 레시피 숨김, `UnlockWave` 연동 등) 그 조건을 색인에 넣으면 합성 패널·도감·정보 패널 **세 곳이 각자 필터를 갖게 된다**. 그때 필터는 `TowerMergeCoordinator`(또는 도감이 가질 발견 상태)가 소유하고 색인은 순수 조회로 남긴다.
- **밤에도 뜬다**. §10 게이팅은 **실행**에 걸리는 것이고 이건 조작이 아니라 정보다 — 밤에 감추면 다음 낮 계획을 세울 수 없다.
- **표시 여부 판정은 `childCount`가 아니라 뷰가 추적하는 행 리스트로 한다.** `Destroy`가 프레임 끝에 반영되므로, 같은 프레임에 비우고 다시 채우는 이 경로에서 `_mergeContent.childCount`는 방금 지운 행까지 세어 "표시할 게 0인데 블록이 켜진" 상태를 만든다(#153이 남긴 `childCount > 0` 규약을 이때 교체했다).

**표시 이름 단일 출처(`TowerDisplayName`)**: 같은 해석 규칙(`TowerID` → `NameKey` → `NorthLand_Towers`)이 합성 패널·도감·정보 패널·툴팁 4곳에 필요한데 구현이 갈려 있었다 — 도감은 `TowerID`가 비면 SO 파일명으로 내려갔지만 합성 패널은 `"?"`만 냈고, `TowerAsset.Data`(런타임 전용, 에셋 미직렬화) 채움 책임도 호출부마다 달랐다. 이름은 플레이어가 타워를 식별하는 유일한 수단이라 폴백이 갈리면 같은 타워가 화면마다 다르게 불린다 → `TowerDisplayName.Of` / `.EnsureData` 하나로 수렴.

---

## 9. 실행 흐름 (#195 → #263 커맨드 패턴으로 교체, 완료 — 후보 버튼이 부르는 대상)

```
후보 버튼 onClick → 코디네이터.RequestMerge(recipe) → TowerFusionController.TryFuse(recipe, group) : bool
  ① 그룹 타워 → TowerID 목록 (null/파괴/Asset 없음 제외)
  ② TowerFusionMatcher.BuildRequired(recipe) → (TowerID,개수) 집계
  ③ TowerFusionMatcher.TryResolve → 소모할 타워 인덱스 확정 (부족 시 false 반환·로그)
  ④ ManagementController.CanAfford(recipe.ExtraCost) (관리 없으면 무료)
  ⑤ 결과 SO의 런타임 Data 방어 채움(패널 경로 안 거칠 때 대비)
  ⑥ TowerDissolveEffect.Play(재료들, TileSize)      ← 소모 **직전**. 시각 사본을 여기서 뜬다 (§9.2)
  ⑦ TowerMergeCommand.Execute()  ← 재료를 **여기서** 소모한다 (클릭 시점)
       재료마다: TowerFootprint.Release()(타일 점유 해제) + SetActive(false)
  ⑧ TowerPlacer.BeginTowerPlacement(recipe.Result, recipe.ExtraCost, onConfirmed, onEnded) : bool
       고스트 → 타일 확정 → ExtraCost TrySpend + 결과 Instantiate + 결과가 타일 Occupy
       → onConfirmed(place)  : AdoptResult(결과 커맨드 편입) → CommandHistory.Push(=Confirm)
                               **재료는 아직 살아 있다** — 진짜 Destroy는 밤 진입 Commit에서 (#281)
                               + 연출.ConvergeTo(place.Placed)
       → onEnded             : IsConfirmed면 연출.Abort()만 하고 **즉시 반환**(#281 — 확정 뒤 Undo가
                               이제 동작하므로 무조건 부르면 확정한 합성이 되감긴다)
                               아니면 Undo + 연출.Reassemble()
       (반환 false면 배치를 열지 못한 것 → 즉시 Undo + 연출.Abort. 이 경로엔 종료 통지가 오지 않는다)
```

- **소모 시점 = 후보 버튼 클릭 시점**(#263). 이 순서 하나가 커맨드 패턴을 도입한 이유 전부이며, **재료가 점유했던 타일에 결과를 놓을 수 있게 된다**(구 F2 제약 해소, WL-077 후단).
- `Destroy`는 되돌릴 수 없으므로 **소프트 소모**(타일 해제 + 비활성화)로 한다. 나머지(`Tower.Active` 등록 해제, 스탯 원장 비움, 버프 오라가 남긴 modifier 회수와 그 복원)는 **`Tower.OnDisable`/`OnEnable`이 이미 대칭**이라 커맨드가 손대지 않는다 — 풀 재사용을 대비해 만들어 둔 왕복이 그대로 쓰인다.
- **확정/취소 판단은 커맨드가 자기 상태로 한다.** 배치 세션 종료 통지(`TowerPlacer`의 `onEnded`)는 어느 쪽으로 끝났는지 알려주지 않으므로 `IsConfirmed`를 읽어 가른다 → **`TowerPlacer`·`MouseManager` 무수정.**
  ⚠ **#281에서 이 콜백이 조건부가 됐다.** 예전에는 확정 뒤의 `Undo`가 상태 검사에 막혀 조용히 무시돼서 두 콜백을 다 걸어도 안전했다. 이제 `Confirmed`에서 `Undo`는 **동작하므로**, `IsConfirmed`면 먼저 반환해야 한다.
- **선택 집합에서 빼는 일은 아무도 명시적으로 하지 않는다.** 비활성화 → `Tower.Active` 이탈 → `ActiveChanged` → 코디네이터의 `Prune`이 걷어낸다(구 `ConsumeMaterials`의 `group.Remove`가 하던 몫). 커맨드는 `TowerMergeGroup`을 모른다.
- **부작용(수용)**: 고스트를 취소하면 재료·타일·비용은 원복되지만 **선택 집합은 돌아오지 않는다** — 다시 합성하려면 재료를 다시 고른다. `BeginPlacement`의 `ClearSelection`(§7.3 마지막 행)은 그대로 두고 커맨드 범위를 "재료"로 좁게 유지했다(§14 확장 여지).
- 비용 지불은 `ManagementController.CanAfford/TrySpend`(WL-017 게이트웨이)로만 — `TowerPlacer` 확정 경로 재사용(별도 차감 로직 없음). 관리가 씬에 없으면 무료(permissive). **`ExtraCost`는 종전대로 확정 시점 차감**이다(클릭 시점 차감 아님) — 자원은 타일과 달리 먼저 빼야 할 이유가 없고, 그러면 환불 경로가 늘고 자원 UI가 깜빡인다.

### 9.1 진행 중 커맨드의 수명 (별도 안전망을 두지 않은 근거)

진행 중인 커맨드는 **항상 최대 1개**다. 그것을 떠받치는 것은 `BeginPlacement`가 **새 세션을 열기 전에 `CancelPlacement`를 먼저 부른다**는 사실이다 — 그 취소가 이전 세션의 종료 콜백(= 이전 커맨드의 `Undo`)을 발화시키므로, 새 커맨드가 자리를 잡는 시점엔 이전 커맨드가 이미 해소돼 있다. (`TowerPlacer.keepPlacing`은 이 보장과 무관하다 — WL-105는 별개 축이다.)

#281 이후에도 **진행 중(`Executed`) 커맨드**는 최대 1개라는 이 불변식이 그대로다. `Confirm`을 지난 커맨드는 `CommandHistory` 스택으로 넘어가므로 **진행 중인 것과 쌓인 것은 겹치지 않는 두 집합**이고, 그 사실이 밤 진입 시 `CommandHistory.CommitAll`과 `PhasePanelSwitcher.ShowNight`의 구독 순서를 무의미하게 만든다(어느 쪽이 먼저 와도 결과가 같아 순서 강제 장치를 두지 않았다).

그래서 전역 Undo 스택이 없고, 아래 경로가 전부 **기존 취소 경로 하나로 수렴**하므로 별도 정리 코드도 두지 않았다:

| 상황 | 원복 경로 |
| --- | --- |
| 우클릭 취소 | `MouseManager.UpdatePlacement` → `CancelPlacement` → `onEnded` → `Undo` |
| 밤 전환 | `PhasePanelSwitcher.ShowNight` → `CancelPlacement` → 〃 |
| 새 배치 시작 | `BeginPlacement`가 먼저 `CancelPlacement` → 〃 (이전 커맨드가 원복된 뒤 새 커맨드가 걸린다) |
| **확정 클릭했지만 배치 실패** | `PlaceTower`가 앵커 없음·건설 불가·`TrySpend` 실패로 조기 반환 → `onConfirmed`(=`Confirm`+등록) **미발화** → 그래도 `MouseManager`가 뒤이어 `CancelPlacement` → `Undo`. 재료가 정확히 복구된다 |
| 씬 전환 | `MouseManager.HandleSceneLoaded` → `CancelPlacement` → 〃. 이 시점엔 재료가 이미 파괴됐지만 `Undo`의 null 가드가 흡수한다 |
| 게임오버 | 합성은 낮 전용이고 승패는 밤에 갈리므로, 그 시점엔 밤 전환이 이미 배치를 취소한 뒤다 |

> **불변식 하나는 코드로 강제한다**: `Release`된 발자국이 `Reoccupy` 없이 되살아나면 타워는 살아나는데 타일은 빈 칸으로 남아 그 위에 또 배치된다. 정상 경로(`Undo`)는 활성화 전에 `Reoccupy`를 부르지만, 커맨드를 거치지 않는 경로(향후 연출·철거)가 생길 수 있어 `TowerFootprint.OnEnable`에 자기치유 안전망을 뒀다(정상 경로에선 no-op).

> 씬 언로드 시 "비활성 재료가 씬에 남는" 문제는 없다 — 비활성 GameObject도 씬과 함께 파괴되고, 재료는 `DontDestroyOnLoad`가 아니다. 반대로 `TowerFusionController.OnDestroy`에서 `Undo`를 부르는 식의 안전망은 **오히려 해롭다**: 파괴 중인 오브젝트에 `SetActive(true)`를 걸게 된다.

### 9.2 소모 연출 (`TowerDissolveEffect`) — #265

> **⚠ 룩·수치는 임시다.** `TowerPlacement.md` §9.3의 단서가 그대로 적용된다 — 타워 에셋이 임시이고 아트 방향이 미정이라, 아래는 "플레이에서 이상하지 않다"까지만 확인된 값이다. **다만 아래 §9.2.2의 세 순서 계약은 임시가 아니다.**

#### 9.2.1 무엇을 하는가

```
후보 버튼 클릭
  → 재료가 하얘짐 (시각 사본의 머티리얼을 흰 언릿으로 교체)
  → 살짝 부풀었다가 bounds.center로 수축 → 사라짐
  → 그 중심 1점에서 입자로 폭발
  → 재료 자리 상공에서 부유 (배치 대기 동안 무한 루프 — Y축 회전 + 개별 위상 흔들림)
      ├ 확정 → 결과 타워로 수렴 → 소멸        (등장 연출의 팝과 동시에 도착)
      └ 취소 → 제자리로 역수렴 → 재조립 팝     (바닥 링 없음 — 링은 "새로 배치됨"의 언어)
```

재료가 여러 개면 각자 자기 자리에서 출발해 자기 자리로 돌아오고, 폭발은 약간의 시차를 둔다.

**메시 정점을 한 번도 읽지 않는다.** 입자 시작점이 중심 한 점이라 실루엣 분해가 필요 없고, 따라서 `isReadable: 0`(프로젝트 FBX 1664개 중 573개)과 무관하다. 이 시퀀스는 취향이 아니라 그 제약이 고른 형태다.

**화이트아웃은 `OutlineHighlight`의 shell이 아니라 사본 머티리얼 교체다.** 어차피 사본을 뜨므로 그 사본만 칠하면 되고, 원본 무편집이라는 shell의 이점은 그대로 남는다. shell은 `OutlineShell` 레이어 + 본체 패스 제외라는 **다른 목적의 셋업**을 타고 있어 이 용도로 쓰려면 레이어·패스를 따로 맞춰야 한다. (반대로 재료가 그룹 선택 상태면 shell이 켜져 있으므로, 사본을 뜰 때 그 레이어의 렌더러는 **걸러낸다** — 안 그러면 실루엣이 두 겹이 된다.)

#### 9.2.2 계약 — **이 부분은 유지해야 한다**

| # | 규칙 | 어기면 |
| --- | --- | --- |
| ⓐ | `Play`는 커맨드 `Execute` **직전**에 부른다 | 커맨드가 재료를 `SetActive(false)` 하고 나면 **복제할 시각물이 없다** — 연출이 통째로 빈다 |
| ⓑ | `ConvergeTo`는 `TowerPlacer` 확정 콜백에서, **등장 연출보다 앞**에 부른다 | 등장 연출이 결과 타워 스케일을 0으로 만든 뒤라 **쪼그라든 bounds의 중심**으로 입자가 모인다 |
| ⓒ | `Reassemble`은 커맨드 `Undo` **직후 같은 프레임**에 부른다 | 되살아난 재료가 원본 크기로 **한 프레임 번쩍인** 뒤에야 입자가 도착한다 |

**마무리 통지는 폭발이 끝나기 전에도 온다.** 배치 대기가 짧으면(실수로 눌렀다가 바로 취소, 다른 후보 버튼 클릭, 밤 전환) 소멸 구간(재료 2개 기준 약 0.52초) 도중에 확정/취소가 도착한다. 그 경로는 남은 구간을 **즉시 완료 상태로 밀어붙인 뒤**(실루엣 파괴 + 알갱이를 부유 위치·정상 크기로 스냅 + 알파 1) 마무리로 넘어간다. 그냥 빠져나가면 흰 실루엣이 재료 타일에 최대 0.73초 얼어붙고 알파가 0에 멈춰 **수렴 입자가 아예 보이지 않는다** — 연출이 존재하는 이유가 그 구간에서만 통째로 사라진다. 폭발만 건너뛰므로 "재료 자리에서 출발한다"는 인과는 유지된다.

**bounds는 스케일 점유를 푼 뒤에 잰다.** 재료가 방금 배치됐거나(등장 팝) 직전 합성이 취소돼 재조립 중이면 루트 스케일이 0~과도기 값이라, 그대로 재면 bounds가 한 점으로 붕괴해 화이트아웃·수축이 통째로 보이지 않는다. `VfxScaleHold.Acquire(target).Release()`로 원본만 되찾고 점유는 넘겨받지 않는다(이 연출은 소멸 구간에서 대상 스케일을 건드리지 않는다). 등장 연출이 `Acquire`를 측정보다 먼저 부르는 것과 같은 규칙이다.

**시간 축은 등장 연출과 공유한다**(`TowerSpawnEffect.ConvergeDuration` / `.PopDuration`). 유입 입자의 비행 시간을 **거리가 아니라 시간으로** 묶는 것이 핵심이다 — 재료가 배치 지점에서 얼마나 떨어져 있든 `ConvergeDuration` 안에 도착하므로 결과 타워가 튀어나오는 순간을 넘기지 않는다. 속도를 고정하면(= 거리에 비례한 시간) 먼 재료의 입자가 **타워가 다 선 뒤에** 도착해 "재료가 모여 타워가 됐다"는 인과가 뒤집히고, 이 연출이 존재하는 이유가 사라진다.

그 상한 **안에서는** 알갱이마다 속도가 다르다. 도착 시각을 **알갱이 크기가 정한다** — 작은 것은 듀레이션의 절반쯤에 먼저 닿고, 가장 큰 것만이 듀레이션을 꽉 채운다. "가벼운 게 빠르다"는 직관과 맞고, **가장 느린 놈의 도착 시각이 곧 상한**이라 어느 알갱이도 팝보다 늦지 않는다. 크기와 속도는 **같은 난수 하나**에서 뽑는다(따로 뽑으면 큰데 빠른 알갱이가 섞여 규칙이 안 읽힌다).

궤적은 **직선**이고 속도는 거의 등속이다. 둘 다 속도 차이를 보이게 하려는 선택이다: 궤적을 휘게 하면(소용돌이·포물선) 알갱이가 전부 같은 곡선을 그려 한 덩어리로 보이고, 강한 ease-in을 걸면 이동이 끝자락에 몰려 빠른 알갱이와 느린 알갱이가 구분되지 않는다.

**#265는 결과 타워에 `TowerSpawnEffect.PlayAsync`를 걸지 않는다.** 배치 확정이면 `TowerPlacer`가 어차피 등장 연출을 재생하므로, 합성은 거기에 자기 입자를 나란히 얹기만 한다 — 같은 대상에 두 번 재생되는 일이 없다.

**길이 기준은 전부 타일 한 칸이다**(풋프린트가 아니라). 이 연출이 말하려는 것은 "이 알갱이는 저 칸 것"이고 칸 하나가 그 언어의 단위다 — 다중 셀 타워가 재료가 돼도 구름이 커지면 옆 칸까지 덮어 오히려 식별을 해친다. 알갱이 크기도 같은 이유로 타일 기준이며, **등장 연출도 알갱이만은 타일 기준을 쓴다**(`TowerPlacement.md` §9.3.2 앵커 표). 두 연출이 같은 물질로 보이려면 알갱이 크기가 타워 칸 수에 흔들리면 안 된다.

#### 9.2.3 로직과의 분리

연출은 **시각 전용·논블로킹**이다. 커맨드는 연출을 기다리지 않고(타일을 즉시 비우는 것이 #263의 목적) 연출은 사본으로 독립 재생하므로, 연출이 중간에 죽어도 합성 상태는 어긋나지 않는다. 반대로 §9.1의 취소 경로가 전부 `Undo` 하나로 수렴하는 덕에 연출의 마무리도 그 한 지점에 얹힌다.

마무리는 **선착순**이다(`Abort`/`ConvergeTo`/`Reassemble` 중 먼저 정해진 것이 이긴다). 그래서 확정 뒤에 오는 종료 통지의 `Abort`가 진행 중인 수렴을 잘라먹지 않고, 확정/취소 어느 쪽도 아닌 종료 경로(밤 전환 등)에서는 안전망으로 동작한다.

재조립 동안 재료 스케일은 `VfxScaleHold`가 배타 점유하며, 어떤 경로로 끊겨도(씬 전환·취소·예외) 연출 호스트의 `OnDestroy`가 원복한다 — **안 보이는 타워가 최악의 실패 모드**라는 판단은 등장 연출과 같다.

### 9.3 확정한 합성 되돌리기 — #281 (→ #444)

배치 세션이 성공으로 끝나도 합성은 **밤 전까지 되돌릴 수 있다.** 되돌리기 버튼(`TowerUndoButtonView`, 페이즈 패널 밖에 상시 배치 — 밤엔 비활성)과 **Ctrl+Z**가 모두 `UndoRequest.Submit()`을 지나 `CommandHistory.Undo()`를 부르고, 히스토리 최상단이 이 합성이면 결과 타워가 회수되고 재료가 복원된다.

⚠ **#444로 스택에 경영 조작(건물 업그레이드·주민 증축)이 함께 쌓인다.** "최상단이 이 합성인가"는 이제 타워 조작만 세어 판단할 수 없다 — 합성 뒤에 건물을 올렸다면 Ctrl+Z 한 번은 그 건물을 되돌린다. 상태 기계도 `ReversibleCommandBase`로 올라갔으므로 아래 계약은 `TowerMergeCommand.OnUndo(wasConfirmed)`가 지킨다(`wasConfirmed=false`면 ③만 도는 #263의 취소 경로다).

**되돌리기 순서 — 네 단계 전부가 계약이다:**

```
① TowerDissolveEffect.Play([결과 타워], TileSize, DissolveMode.Rewind)
     ← 결과 타워가 **아직 살아 있는 동안**. Play의 시각 사본 복제가 동기로 끝나므로
       바로 아래에서 Destroy해도 안전하다
② _result.Undo()      결과 타워: 선택 해제 → TowerFootprint.Release() → Destroy → ExtraCost 환원(Grant)
③ RestoreMaterials()  재료: Reoccupy() → SetActive(true)
④ effect.RestoreTo(재료들)  가루의 목적지 등록 + 같은 프레임에 스케일 0으로 잡기
```

- **② → ③ 순서가 핵심이다.** 결과 타워의 풋프린트는 재료가 쓰던 타일과 겹친다(그 자리에 놓을 수 있게 한 것이 #263의 목적이다). `Object.Destroy`는 프레임 끝까지 지연되므로 `TowerFootprint.OnDestroy`도 그때까지 안 도는데, 그 전에 재료를 `Reoccupy`하면 타일이 아직 점유로 보여 `TowerFootprint`가 **소유권을 실제 점유자에게 남기고 목록에서 뺀다**(§9.1의 불변식 ②). 그러면 재료는 살아나는데 타일이 없다. 그래서 `TowerPlaceCommand.Undo`가 `Destroy` **전에** `Release()`를 명시적으로 부른다.
- **④는 ③ 직후 같은 프레임이어야 한다** — `Reassemble`과 같은 규율이다(③이 `SetActive(true)`를 걸었으므로 렌더 전에 숨기지 않으면 원본 크기 타워가 한 프레임 번쩍인다).
- **결과 배치는 히스토리에 따로 오르지 않는다.** `TowerFusionController`가 `PlacementOwner.Caller`로 배치를 열고 `AdoptResult`로 편입하므로, 합성 전체가 커맨드 하나로 되돌아간다. 나눠 올리면 한 번의 합성이 두 번에 나눠 되감겨 **결과도 재료도 없는 빈 타일**이 중간에 한 번 보인다. 연출 소유권도 함께 넘어간다(`PlaysUndoDissolve`를 내려, 결과 타워가 자기 몫으로 한 번 더 터지지 않게 한다).
- **자원은 `ExtraCost`만 환원된다.** 재료 원가는 재료가 되살아나는 것으로 이미 갚아지기 때문이고, 커맨드가 **실지불 비용**을 들고 있어 이 구분이 자동으로 성립한다.
- **밤에는 불가능하다.** `OnDayToNight`에 히스토리 전체가 `Commit`되어 재료가 진짜로 파괴되고 스택이 비워진다.

**LIFO여야 하는 이유**는 편의가 아니다. `A 배치(tile1) → A+B 합성 → C를 (비워진) tile1에 배치` 상태에서 합성을 먼저 되돌리면 A가 tile1을 되찾지 못하고 위의 불변식 ②가 발동해 **A가 타일 없는 타워가 된다.** LIFO가 C를 먼저 되돌리게 강제해 이 경로를 원천 차단한다 — LIFO 자체가 유효성 검사다. ⚠ 낮 중 타워 철거·사망처럼 히스토리를 거치지 않고 대상이 사라지는 경로가 생기면 이 불변식이 깨진다.

같은 논지가 경영 축에도 그대로 적용된다(#444) — 본진 레벨이 하위 건물의 실질 Max를 정하므로 본진을 먼저 되돌리면 하위 건물이 상한을 넘은 레벨로 남고, LIFO가 그것을 막는다. 상세는 `Docs/ManagementArea/BuildingUpgrade.md` §10.

---

## 10. 게이팅 / 수명주기

- **낮(배치 페이즈) 전용** — 멀티 선택·합성 패널 전환·실행 전부 낮에만. 밤에는 Shift+클릭 그룹 토글을 무시하고 합성 패널로 전환하지 않는다. 코디네이터가 `DayNightManager.Instance?.CurrentPhase == Day` 판정(`Instance` null이면 permissive, WL-002 완화 패턴), 입력 핸들러(`HandlePrimarySelect`/`HandleGroupToggle`)와 `RequestMerge` 진입에서 게이팅한다. 이 제한의 대상은 **합성으로 게임 상태를 바꾸는 조작**이다. 바로가기·숫자키·미니맵을 통한 공간 간 카메라 이동과 기존 타워 정보 열람은 밤에도 허용하며, 합성 게이트와 독립된 정책이다. (실행부 `TryFuse` 자체 방어 게이팅은 defense-in-depth로 미추가 — muchan 협의 옵션.)
- **리셋**: 밤 진입(`OnDayToNight`) 시 **코디네이터는 선택 집합만 비운다**. 진행 중인 배치(일반/합성 공통) 취소(F5 — 확정이 밤으로 넘어가는 것 방지)는 **`PhasePanelSwitcher.ShowNight`가 `MouseManager.CancelPlacement()`로** 담당한다 — 페이즈 취소 책임을 한 곳에 모음(낮 진입 스킬 조준 취소 `ShowDay`와 대칭, 🟡 소유권 이관). 코디네이터는 **씬 오브젝트**라 씬 전환 시 새로 생성돼 그룹이 자연히 비므로 별도 `sceneLoaded` 리셋은 불요. `RefreshHighlight`는 파괴된 `Tower` 참조를 null 가드해 `OnGroupDeselected` NRE를 피한다('죽은 참조 역참조 금지', WL-033 축).
- **코디네이터 수명주기**(F7): MouseManager는 `DontDestroyOnLoad`, `Tower.ActiveChanged`는 static이라 씬보다 오래 산다 → 코디네이터(씬 오브젝트)는 `OnDestroy`에서 이들 구독을 **반드시 해제**한다(안 하면 씬 언로드 후 죽은 구독자를 호출 — `Projectile.DamageDealt` static 구독 주의와 같은 계열).
- **외부 파괴 대응**(WL-076(b) 해소): 그룹에 든 타워가 외부 사유(철거·전투 사망 등)로 파괴되면 → 코디네이터가 **`Tower.ActiveChanged` 구독 → `_group.Prune(t => t == null || !Tower.Active.Contains(t))`**. `Tower.OnDisable`이 `Active.Remove` **직후** `ActiveChanged`를 발행하는데, 그 시점엔 아직 Unity 가짜 null이 아니라 `t == null`만으론 못 거른다 → **이미 Remove가 끝난 `Tower.Active` 멤버십**으로 판정해 그 프레임에 정확히 제거한다(리스트에 죽은 슬롯이 남아 `Count`만 부풀던 유령 상태 방지). 합성 소모 경로는 실행부가 `group.Remove`로 즉시 갱신한다.

---

## 11. 시스템 책임 분담

| 단계 | 소유 | 비고 |
| --- | --- | --- |
| 포인터/키보드 입력·레이캐스트·마커 판정 | **MouseManager** | 도메인(타워) 무지, 마커만 앎. 계약 #1 |
| 선택 집합(순서)·낮 게이팅·리셋·그룹/하이라이트/패널 구동·실행 오케스트레이션 | **`TowerMergeCoordinator`**(#183, n0wst4ndup) | MouseManager 이벤트 구독, 파사드 노출 |
| 재료 집합 저장(이음매) | **`TowerMergeGroup`**(순수 C#, 코디네이터 소유) | 실행부·매칭이 소비, `OnChanged` 단일 통지 |
| 매칭 규칙 | **`TowerFusionMatcher`**(순수) | 버튼 활성·실행 공유 단일 출처 |
| 합성 실행(검증·배치·소모) | **`TowerFusionController`**(muchan) | `TryFuse(recipe, group)`. 진행 중 커맨드 1개를 배치 콜백으로 물고 있다 |
| 재료 소모의 되돌리기 | **`TowerMergeCommand`**(#263) | 소프트 소모/확정/원복. `TowerMergeGroup`을 모른다(집합 정리는 `Prune`이 한다). 상태 기계·비용 환원은 `ReversibleCommandBase`(#444)에 있다 |
| 결과 고스트 배치·타일 검증·점유 | **`TowerPlacer`** / `TowerFootprint` | TowerPlacement.md. `TowerFootprint`가 `Release`/`Reoccupy`로 임시 해제도 담당(#263) |
| 자원(ExtraCost) 지불 | **`ManagementController`** | `CanAfford`/`TrySpend`, WL-017 |
| 소모 연출(화이트아웃·폭발·부유·수렴·재조립) | **`TowerDissolveEffect`**(#265, n0wst4ndup, #281에서 개명) | 시각 전용·논블로킹. **도메인 무지** — `Transform` 목록과 타일 한 칸 크기만 받는다. 등장 연출과 부품(`GrainSwarm`·`VfxScaleHold`)·시간 축을 공유. §9.2 |
| 재료 원본 SO 조회 | **`Tower.Asset`**(Combat, 읽기) | SUNGSOO |
| 낮/밤 게이팅 신호 | **`DayNightManager`** | `CurrentPhase`/전환 이벤트 |

---

## 12. 인수 조건

**데이터·실행부 (#194/#195) — 완료**
- [x] 레시피 데이터 정의(재료 TowerID별 개수 → 결과 `TowerAsset` + `ExtraCost`)
- [x] 결과 타워를 일반 `TowerAsset`로 표현
- [x] 포함 매칭(정확 충족/여분 허용/부족 실패/다종 재료) — 순수 함수 검증 가능(EditMode 후보)
- [x] 그룹 충족 시 결과 타워 고스트 생성 → 타일 배치 → 확정 시 재료 `Destroy` + `ExtraCost` 차감 *(#263에서 소모 시점이 클릭으로 앞당겨짐 — 현행 흐름은 §9)*
- [x] 재료·비용 부족 시 실행 안 됨(로그), 고스트 취소 시 재료·비용 보존

**선택/패널 UI (#183) — 코드 구현·컴파일 완료 / 아래는 정본 씬 배선 후 E2E로 확정할 인수 항목**
- [ ] 타워 1개 선택 → 인포 패널(기존 동작 회귀 없음).
- [ ] Shift로 타워 2개 이상 선택 → 인포 숨김 + 합성 패널 표시.
- [ ] 위 전환에서 **직전 단일 선택의 초록 사거리 원도 함께 사라진다**(WL-087 회귀 감시 — 원이 남으면 합성 패널 시인성을 해친다). 1개로 축소하면 다시 뜨고, 0·빈 곳·밤 전환에서도 남지 않는다. 건물을 선택해 둔 채 Shift로 타워를 담기 시작한 경우도 동일(마법 타워는 #164 리팩토링 후 그룹에 담기므로 이 경로가 아니다).
- [ ] 합성 패널 상단 리스트가 **선택 순서대로** 채워지고 집합 변경 시 즉시 갱신.
- [ ] Shift+이미 선택된 타워 → 리스트에서 토글 제거(순서 유지).
- [ ] 선택 1개로 축소 시 인포 복귀, 0이면 숨김.
- [ ] 키 없이 타워 클릭 = 집합 해제 후 단일 선택, 빈 곳 클릭 = 전체 해제(**우클릭은 선택 해제 아님** — F3, 이슈 AC에서 의도적 이탈).
- [ ] Shift+건물/영지 노드 → 무시(합성 리스트 불변).
- [ ] (더미 레시피로) 집합이 레시피 재료를 **모두 포함**하면 해당 후보 버튼 `SetActive(true)`, 여분 허용, 미충족 시 비활성.
- [ ] 여러 레시피 동시 충족 시 여러 후보 버튼 동시 활성.
- [ ] 밤에는 멀티 선택/합성 패널 전환이 일어나지 않는다.
- [ ] 정본 `GameScene`에서 동작 확인(SceneWorkflow 준수).

> 검증: 개인 테스트 씬 Play 확인(팀 컨벤션 — 유닛 테스트 없음). 매칭(`TowerFusionMatcher`)은 순수 함수라 프로젝트 첫 EditMode 테스트 후보.

---

## 13. 열린 결정 / TBD / 의존

- **[구현 PR] SystemMap 갱신 필수**: #183은 MouseManager 공개 선택 계약(그룹 토글 이벤트·빈 곳 클리어 신호)과 신규 코디네이터/마커를 추가하므로, 구현 PR에서 `SystemMap.md`(§1 TowerFusion 행·§2 API·§3 접점 — MouseManager·TowerFusion 인근)를 같이 갱신한다.
- **추가 선택 키 = Shift**(§7.2): WL-073(우클릭 이중 점유) 회피 겸. 재조정 시 이 문서·구현 동시 수정.
- ~~**레시피 카탈로그 출처 = 패널 인스펙터 배열 `TowerRecipe[] _recipes`**~~ → **변경됨**: `TowerRecipeCatalog.All`(`Resources.LoadAll`, §5). 폴더 투입만으로 후보에 오른다. **남은 것은 순서** — `Resources.LoadAll`이 비결정적이라 후보 버튼 순서의 F6 근거가 사라졌다(§5 ⚠). §8.5가 쓰는 (등급 → 표시 이름) 정렬을 후보 버튼에도 적용하는 것이 해소책.
- **stale 버튼 방어**(§10, WL-076(b) 해소): `TowerMergeGroup.Prune(predicate)` + 코디네이터가 `Tower.ActiveChanged` 구독해 **`Tower.Active` 멤버십 기준**으로 호출(OnDisable 시점 가짜-null 미형성 문제 회피).
- ~~**결과 배치·소모 타이밍(F2 결정)**~~ → **해소(#263)**. 커맨드 패턴을 도입해 소모를 클릭 시점으로 앞당겼다: 소프트 소모(타일 `Release` + 비활성화) → 확정 시 `Commit`(진짜 `Destroy`) / 취소 시 `Undo`(재활성화 + `Reoccupy`). **재료가 점유했던 타일에 결과를 놓을 수 있다.** 흐름은 §9로 교체됨. 남은 선택지(취소 시 선택 집합까지 복원)는 §14 확장 여지로 이월.
- **낮/밤 실행 게이팅**(§10, WL-077 phase): 코디네이터 `RequestMerge`/입력 핸들러가 낮 게이팅 → UI 경로 밤 실행 차단. 실행부 `TryFuse` 자체 방어 게이팅은 미추가(옵션, muchan 협의).
- **결과 정보 패널 배선**(§8.3): 스탯 표시를 `Tower`/`TowerInfoUI`와 공유할지 별도 조합할지(WL-079 축).
- **결과 타워 콘텐츠**: 합성 결과용 신규 `TowerAsset`(`TowerTable.csv` 행 + 프리팹/고스트/스탯). 현재 테스트는 기존 타워를 결과로 재사용.
- **밸런스·규칙**(GDD §8): 레시피 족보(재료 조합→결과)·`ExtraCost` 수치·재료 승계(레벨/버프) 여부 미정.
  - **효과 승계는 구현됐다 → [`Tower.md`](Tower.md) §3.9**(#274 Phase 5). 효과의 *종류*만 계승하고 수치는 결과 SO가 적으며, 계승 여부는 `TowerRecipe.InheritEffects`가 레시피별로 정한다. 후보 버튼 호버 시 툴팁에 `Inherit: Stun + Slow` 형태로 표시된다(판정은 핑크 프리뷰와 **같은 소모 대상**을 쓴다).
    ⚠ **남은 것은 족보다** — 현재 `InheritEffects`를 켠 레시피가 **0개**라 게임 동작은 그대로다. 그리고 **기존 타워 SO를 결과로 쓰면 안 된다**: 필터는 합성 산물에만 걸리므로, 결과 SO에 효과를 정의하는 순간 그 타워를 평범하게 배치해도 효과가 켜져 조용히 강화된다. 결과 SO는 **합성으로만 나오는 것**이어야 한다.
  - **레벨/버프 승계**는 여전히 미정(효과 승계와 별개 축).
- **드래그 범위 선택**: 후속 입력 확장(범위에 먼저 들어온 순서로 등록).

---

## 14. 확장 여지

- **다단 합성**: 합성 결과를 다시 다른 레시피 재료로(레시피가 `TowerAsset` 참조라 자연 지원).
- **레시피 조건 확장**: 재료 타워의 레벨·버프 상태 승계 여부(`Tower.ApplyBuff`/`RemoveBuff` 계약 연동 시).
- **취소 시 선택 집합 복원**(#263에서 의도적으로 범위 밖): 지금은 재료·타일만 원복하고 그룹은 비워진 채다. 재선택이 번거롭다는 피드백이 나오면 커맨드 복원 대상에 그룹 스냅샷을 더한다 — 커맨드가 이미 재료 목록을 들고 있어 확장 비용은 작다.
- ~~**연출**(#264 배치 등장 / #265 합성 소모)~~ — **구현됨(§9.2)**. 로직 위에 시각만 얹었으므로 §9 흐름은 바뀌지 않았다. 남은 확장은 룩 자체(아트 방향 확정 후 전면 재검토)와 재료가 많을 때의 밀도 조정 정도다.
- **그룹 선택 일반화**: `IGroupSelectable` 마커가 도메인 중립이라, 향후 병사 등 다른 전투/경영 오브젝트의 다중 선택에도 코디네이터 패턴을 재사용 가능.

---

## 부록 A. 씬 배선 가이드 (#183 — 정본 GameScene, SceneWorkflow 준수)

> 코드·레시피는 구현·검증 완료. 아래는 에디터에서 오브젝트/참조를 잇는 절차다. SceneWorkflow상 정본을 직접 편집하지 말고 개인 복사본 → 스냅샷(`Scenes/Branches/`) → 정본 승격 절차를 따른다.

**1. 코디네이터 오브젝트**
- 빈 GameObject(예: `TowerMergeCoordinator`) + `TowerMergeCoordinator` 컴포넌트.
- `_controller` → 씬의 `TowerFusionController`. `_mergePanel` → 합성 패널 루트(3).

**2. 실행부/배치**(대부분 기존)
- `TowerFusionController`: `_placer` → 씬 `TowerPlacer`, `_management` → (옵션, 비우면 자동 탐색). ⚠ 구 `_wallet`/`_recipe`/`TryFuseSelected`는 폐기 — 인스펙터에 없어야 정상.
- `TowerPlacer`는 기존 배치용 그대로(변경 없음). 배치 시 `TowerGroupSelectable`를 런타임 자동 부착하므로 타워 프리팹 편집 불필요.

**3. 합성 패널 UI**(우측, `TowerInfoUI`와 같은 자리 — 동시 표시 안 됨)
- 패널 루트(예: `TowerMergePanel`) + `TowerMergePanelView`. 시작 시 활성/비활성 무관(코디네이터 `Start`가 꺼줌). **이 루트가 곧 `_mergePanel`.**
- 자식:
  - **상단 Vertical Scroll View** → Content(Vertical Layout Group + Content Size Fitter) = `_selectedListContent`.
  - **선택 리스트 행 프리팹**(TMP_Text 1개 포함) = `_selectedRowPrefab`.
  - **하단 Horizontal Scroll View** → Content(Horizontal Layout Group + Content Size Fitter) = `_candidateContent`.
  - **후보 버튼 프리팹**(Button + 자식 TMP_Text) = `_candidateButtonPrefab`(기존 `TowerSelectPanelView` 버튼 재사용 가능).
  - `_coordinator` → 1의 코디네이터.

**3-b. 정보 패널 "상위 타워" 블록 (§8.5)** — `TowerInfoUI`가 있는 인포 패널 쪽

겉모습을 배치 팔레트와 같게 맞추는 것이 전제다. 팔레트의 Scroll View를 인포 패널로 복사해 두고 시작한다.

1. **칸 프리팹**: `Assets/Imported/@NorthLand/Prefabs/UI/TowerButton.prefab`을 복제한다(정본 이름: `TowerTargetSlot.prefab`).
   - 루트에서 **`Button` 컴포넌트 제거**(누를 수 없는 칸). `interactable = false`로 대신하지 말 것 — 원본 `Transition`이 `ColorTint`라 칸이 회색으로 죽는다.
   - 루트에서 **`TowerButtonView` 제거** → **`TowerMergeTargetSlot` 추가**. `_icon` → `Slot/Img_Icon`. **`_name`은 배선하지 않는다**(아이콘 전용 — §8.5).
   - **배너 서브트리 삭제**(`Banner`·`Img_Banner`·`Txt_Name`) — 이름은 호버 툴팁이 내므로 칸에 이름 자리를 두지 않는다. 남겨두면 칸 높이가 팔레트 버튼만큼 커져 정보 패널을 밀어낸다.
   - **`TowerLockOverlay` 자식 삭제**(정보 패널에 해금 개념이 없다). 원본이 `m_IsActive: 0`이라 남겨도 보이지는 않지만 의도를 흐린다.
   - `Slot/Img_Bg`의 **`Raycast Target`은 켜진 채로 둔다** — 이게 호버 툴팁을 잡는 유일한 그래픽이다(`Button`을 떼도 남는다).
   - 툴팁 감지기(`TowerTooltipSource`)는 **런타임 부착**이라 프리팹에 넣지 않는다.
2. **`TowerInfoUI` 인스펙터 배선**: `_mergeContainer`(블록 루트) → `_mergeContent`(복사해 온 Scroll View의 Content) → `_mergeSlotPrefab`(1의 프리팹). 배선이 비면 블록이 뜨지 않고, `HasMergeSlotWiring`이 **경고를 1회** 남긴다(무엇이 null인지 + Imported 동기화 확인 안내) — 조용히 접으면 배선 누락과 저장소 미동기가 같은 증상으로 보인다.
3. 칸이 여러 개일 수 있다(예: `archer_tower`는 다수 레시피의 재료) → 복사해 온 Scroll View가 그대로 흡수하므로 Content의 Layout Group만 원본 설정을 유지하면 된다.

⚠ 칸 프리팹은 `Assets/Imported/@NorthLand/` 아래에 있어 **별도 저장소(`NorthLand-Imported`) 커밋이 함께 필요하다**(RewardCard 프리팹과 같은 축, WL-160). 정본은 `@NorthLand/Prefabs/UI/TowerTargetSlot.prefab`이고 동기화 계약은 `SystemMap.md` §4에 등재돼 있다 — **Imported `85fd857d3`(타워 타겟 슬롯 프리펩 추가) 이상**, 커밋 순서는 Imported 선행(WL-040).

**4. 그 외**
- `MouseManager`(기존, 코드만 확장)·`DayNightManager`(낮/밤 게이팅) 별도 배선 불필요.
- 레시피는 `Assets/Resources/ScriptableObjects/TowerRecipes/`에 **SO를 넣으면 자동으로** 후보에 오른다(`TowerRecipeCatalog.All`, §5) — 인스펙터 등록 단계는 없어졌다. 현재 13종이 들어 있다.
- **레이어**: 타워 콜라이더가 `MouseManager._selectableMask`에 포함돼야 Shift 클릭이 마커를 잡는다(기존 타워 단일 선택이 동작 중이면 이미 충족).

**검증 체크리스트(Play)**: 타워 1개=인포 / Shift 2개=인포 숨김+합성 패널(리스트 순서대로) / archer 2개 → `Recipe_Example_ArcherToGatling` 버튼 활성 → 클릭 → 고스트 배치·확정 → archer 2개 소멸+gatling 생성 / Shift로 1개로 축소=인포 복귀, 빈 곳 클릭=해제 / Shift+건물=무시 / 밤 전환=선택·패널 리셋.
