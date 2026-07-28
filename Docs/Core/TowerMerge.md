# 전투 공간 타워 합성(Tower Merge) — 기능 명세 (진실의 원천)

> **상태**: 데이터 구조(#194)·실행부(#195) **구현·검증 완료** · 선택/패널 UI(#183) **코드 구현 완료(컴파일 검증)** · 정본 씬 배선·E2E 검증 예정
> **소유**: muchan(데이터·실행 #194/#195) · n0wst4ndup(선택·패널 UI #183) · SUNGSOO(타워 프리팹/전투)
> **구현 파일 — 데이터·실행(#194/#195)**:
> - `Assets/Scripts/Data/Tower/TowerRecipe.cs` — 레시피 SO(재료/결과/추가비용)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerFusionMatcher.cs` — 포함 매칭(순수 static)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerFusionController.cs` — 실행 진입점(`TryFuse(recipe, group)`)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerPlacer.cs` — 배치 오버로드 + 배치 시 그룹 마커 부착
> - `Assets/Scripts/CombatSystem/Tower/Tower.cs` — `Asset` 읽기 접근자
> **구현 파일 — 선택·패널 UI(#183)**:
> - `Assets/Scripts/GameManager/MouseManager/IGroupSelectable.cs` — 그룹 선택 자격 마커(도메인 중립)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerGroupSelectable.cs` — 타워 마커 구현(런타임 부착)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerMergeGroup.cs` — 선택 재료 집합(순수 C#, 코디네이터 소유) — 구 `TowerWallet` 대체
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerMergeCoordinator.cs` — 선택 두뇌·게이팅·패널 권위·실행 오케스트레이션
> - `Assets/Scripts/UI/TowerPanel/TowerMergePanelView.cs` — 합성 패널(선택 리스트 + 후보 버튼)
> - `Assets/Scripts/GameManager/MouseManager/MouseManager.cs` — Shift 추가선택·`OnGroupSelectToggled`·Idle Esc(수정)
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
- 실행(`TowerFusionController.TryFuse(recipe, group)`): 매칭 검증 → `CanAfford` → `TowerPlacer` 고스트 배치 → 확정 시 `ExtraCost` 지불 + 재료 `Destroy`
- (구 임시 홀더 `TowerWallet`은 #183에서 `TowerMergeGroup`으로 대체·폐기)

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
| 선택 코디네이터 | `TowerMergeCoordinator` | 그룹 소유·게이팅·패널 권위·실행 오케스트레이션(파사드: `SelectedTowers`/`OnGroupChanged`/`CanMerge`/`RequestMerge`) |
| 합성 패널 뷰 | `TowerMergePanelView` | 선택 리스트 + 후보 버튼. 코디네이터만 참조 |
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
   MouseManager ── 수정키 없음 ──▶ 평클릭/Esc (OnPrimarySelect·항상 발행) ─┐
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
                                                 매칭 → CanAfford → TowerPlacer 고스트 배치
                                                 → 확정 시 ExtraCost 지불 + 재료 Destroy
```

**흐름 요약**: 선택(MouseManager+마커) → 집합 소유(코디네이터가 `TowerMergeGroup`) → 이음매(그룹) → {버튼 활성 판정 = 매칭, 실행 = 컨트롤러}. 버튼 활성 판정과 실행이 **같은 매칭 함수**(`TowerFusionMatcher`)를 공유해 규칙이 단일 출처다.

---

## 5. 데이터 모델 (#194, 완료)

- **`TowerRecipe`(SO)**: `List<MaterialEntry> Materials`(재료 `TowerAsset`+`Count`, multiset) / `TowerAsset Result` / `List<ResourceCost> ExtraCost`(합성 추가 자원/마나석). CSV 미경유 인스펙터 손입력.
- **결과 타워**: 별도 특수 타입이 아니라 일반 `TowerAsset`. 신규 결과 타워는 `TowerTable.csv` 행 + SO + 프리팹/고스트/스탯을 추가(§13 콘텐츠).
- **레시피 카탈로그(전체 열거)**: #183 후보 버튼 패널이 순회·매칭하려면 전체 레시피 목록이 필요하다. **출처 = 패널의 인스펙터 직렬화 배열 `[SerializeField] TowerRecipe[] _recipes`**(구현 결정). 후보에 넣을 `TowerRecipe` SO를 인스펙터에 등록한다(예시 SO는 `Assets/Resources/ScriptableObjects/TowerRecipes/` — SO 정본 트리). `Resources.LoadAll<TowerRecipe>` 자동 열거 대안도 있으나, 등록 대상을 명시 통제하려고 인스펙터 배열을 택함(WL-076(a) 관련).
  - **버튼 순서** = 인스펙터 배열 순서(작성자가 직접 통제 → 결정적, F6 충족).

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
현재 `MouseManager`는 완전 단일 선택(`_selected` 단일 참조)이고 Idle에는 우클릭/Esc 처리가 없다. #183은 다음을 **추가**한다(기존 `OnSelectionChanged(ISelectable)` 시그니처는 무변경 — 기존 구독자 보호):

- **추가 선택 키 = Shift**(확정, 필요 시 재조정). 키 판정은 MouseManager가 소유(게임플레이 코드의 `Keyboard.current` 직접 폴링 금지, 계약 #1). *WL-073 유의: 우클릭이 카메라 드래그와 이미 이중 점유 → 추가 선택 키를 우클릭이 아닌 Shift로 두어 충돌을 피한다.*
- **그룹 토글 이벤트**(예: `OnGroupSelectToggled(IGroupSelectable)`) 신설: **Shift + 마커 대상** 클릭 시 발행. 발행 직전에 **`Select(null)`로 단일 `_selected`를 비운다**(WL-087 수정, 원안은 "건드리지 않음"이었다). 이후 무엇을 보일지는 §8.1 스위처가 집합 크기로 결정하므로, 단일 선택 상태를 남겨두면 그 부수 표시(사거리 원·인포)를 아무도 못 내린다. **마커 없는 대상(건물·빈 곳)에는 적용하지 않는다** — 집합이 안 바뀌는데 `_selected`만 비면 "집합엔 있는데 화면엔 아무것도 없는" 어긋난 상태가 된다. 순서도 계약이다: 토글 **뒤**에 비우면 `count==1` 복귀에서 스위처가 켠 인포·원을 도로 끈다.
  - 부수 효과: `AuraTower`(마법 타워)·건물처럼 그룹에 못 들어가는 대상을 선택해 둔 채 Shift로 타워를 담기 시작해도 그쪽 사거리 원·패널이 함께 정리된다. 스위처는 `Tower`만 알기 때문에 이 경로가 아니면 못 잡는다.
  - 밤에는 코디네이터가 토글을 무시하므로(§10 게이팅) Shift+타워 클릭이 "단일 선택 해제"로만 끝난다 — 밤에 합성이 잠긴 상태에서의 무의미한 입력이라 의도된 동작으로 둔다.
- **평클릭·Esc·빈 곳 해제 = `OnPrimarySelect` 신설**(F3 + WL-085): 평클릭(해석된 `ISelectable`)·Esc·빈 곳 클릭 시 `OnPrimarySelect(ISelectable|null)`를 **중복 제거 없이 항상** 발행한다. 코디네이터가 이걸로 그룹을 리셋(타워면 `SetSingle`)/해제(그 외·null)한다. → 기존엔 이 신호를 `Select(null)`의 `OnSelectionChanged`로 받으려 했으나 `if (_selected == next) return;` 중복 제거에 삼켜졌다(**Shift로만 선택 시 `_selected==null` → Esc·빈 곳 해제 불발**, 이미 선택된 타워 재평클릭 시 단일화 불발 — WL-085). `OnSelectionChanged`는 기존 단일 선택 구독자용으로 그대로 두고, 그룹 경로만 이 새 이벤트로 분리. **우클릭은 해제에 쓰지 않는다**(카메라 드래그 이중 점유 WL-073, 이슈 AC에서 의도적 이탈 — F3).

### 7.3 입력 규칙 (이슈 §상세)
| 입력 | 동작 |
| --- | --- |
| 키 없이 타워 클릭 | 집합 전체 해제 후 그 타워 **단일 선택** |
| Shift + 미선택 타워 | 단일 선택 해제 후 집합 **끝에 추가**(순서 보존) |
| Shift + 이미 선택된 타워 | 단일 선택 해제 후 집합에서 **토글 제거**(나머지 순서 유지) |
| Shift + 건물/영지 노드 등 비-타워 | **무시**(집합·단일 선택 둘 다 불변 — 마커 없음) |
| 빈 곳 클릭 / Esc | **전체 해제** |
| 우클릭 | 해제 아님 — 카메라 드래그·배치/조준 취소 전용(WL-073, F3) |
| (입력 아님) 배치 시작 | **전체 해제** — `MouseManager.BeginPlacement`가 Esc와 같은 `ClearSelection()`을 호출(WL-086). 자원 배치·합성 배치 모두 해당하며, 고스트를 든 화면에 이전 선택의 사거리 원·초록·인포/합성 패널이 남지 않는다 |

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

**`OnSelected`를 직접 부르면 `OnDeselected`도 스위처가 진다**(WL-087 수정). `TowerInfoUI.HideInfo()`만으로는 부족하다 — 이 훅 쌍에는 정보 패널뿐 아니라 **사거리 원**(#192, `Tower`/`AuraTower`가 자식 GO로 소유)이 함께 묶여 있고, 스위처는 남의 사거리 원을 직접 끄는 창구가 없기 때문이다. 인포를 띄워준 대상을 `_infoShownFor` 슬롯 하나로 기억하고, 대상이 바뀌거나(1→다른 타워) 사라질 때(1→0, 1→2+, 밤 리셋) 그 대상의 `OnDeselected()`를 부른다 — `RefreshHighlight`의 `_highlighted` diff와 같은 패턴의 1개짜리 축소판이며, 파괴된 참조는 Unity 오버로드 `==`로 거른다.

두 패널은 **동시에 보이지 않는다.** `_selected`(MouseManager) vs 집합(코디네이터)의 관계: **평클릭 경로에선 사실상 일치, Shift 경로에선 `_selected`가 비고(§7.2) 집합만 남는다.** 표시/숨김이 idempotent라 기존 MouseManager 경로가 같은 인포를 한 번 더 켜/꺼도 무해 — 단 "무엇을 보일지"의 판단은 항상 스위처가 이긴다.

### 8.2 합성 패널 구성
- **상단 Vertical Scroll View — 선택 리스트**: 선택된 재료 타워를 **선택 순서대로** 한 행씩. 집합 변경 시 즉시 갱신. 행 라벨 = `tower.Asset.TowerID` → `TowerData.NameKey` → 로컬라이즈(`NorthLand_Towers`, `LocalizationHelper.Get`). (행별 제거 버튼은 선택.)
- **하단 Horizontal Scroll View — 후보 버튼**: **레시피(카탈로그)마다 버튼 1개를 미리 생성해 담아두고 기본 `SetActive(false)`**. 매칭되는 레시피의 버튼만 `SetActive(true)`.
  - 활성 판정 = `_coordinator.CanMerge(recipe)`(= `TowerFusionMatcher.CanFuse(group.Towers, recipe)`). (매칭 규칙 재구현 금지 — §6 단일 출처.)
  - `ExtraCost` 감당 여부(`ManagementController.CanAfford(recipe.ExtraCost)`)는 (선택) `interactable`/딤 표시로 구분하되, **최종 검증은 실행부(`TryFuse`)가 한다**(방어). #183 완료기준은 매칭 기반 `SetActive`까지 — 현 구현은 `SetActive`만.
  - 버튼 표시 = 결과 타워(`recipe.Result`) 이름(→ `Result.TowerID` → `NameKey` 로컬라이즈, Data/NameKey 없으면 TowerID 폴백). 아이콘 필드가 생기면 교체.
  - **onClick → `_coordinator.RequestMerge(recipe)`**(코디네이터가 그룹을 물려 `TryFuse(recipe, group)` 호출). 버튼이 자기 `TowerRecipe`를 클로저로 물음.
  - **갱신 시점 = 그룹이 바뀔 때마다** 전 버튼 재판정 — `_coordinator.OnGroupChanged` 구독(패널이 활성일 때. 코디네이터는 내부적으로 `TowerMergeGroup.OnChanged`를 이 이벤트로 포워딩). 패널은 `OnEnable`에서도 현재 상태로 1회 동기화.
  - **UX 트레이드오프(경미)**: `SetActive` 방식은 비매칭 버튼이 사라져 스크롤뷰가 리플로우된다(선택 변경마다 버튼이 튀어나왔다 사라짐). 이슈가 택한 방식이라 유지하되, 튐이 거슬리면 '전체 표시 + `interactable`로 회색' 대안 고려. 또 **여분 허용 시 실제 소모될 재료가 무엇인지**(선택 순서 index로 결정)는 리스트에 표시되지 않음 — 후속 폴리시(호버 시 소모 대상 하이라이트).

> **주의**: 이 하단 후보 버튼 영역은 **배치 팔레트(`TowerSelectPanelView`, 새 타워 건설 선택)와 다르다.** 합성 패널은 이미 배치된 타워들의 조합 결과를 보여준다. 골격은 `TowerSelectPanelView`를 참고 모델로 삼되(버튼 동적 생성·조건부 활성·클릭 시 배치 진입), 대상이 `List<TowerRecipe>` + 매칭 여부 + `TryFuse`로 바뀐다.

### 8.3 결과 정보 패널 (선택, 후속)
현재 선택으로 만들 수 있는 결과 타워(활성 후보 중 선택/호버한 레시피)의 `Result` 스탯을 표시. `Tower`의 스탯 텍스트 조립 규칙과 공유할지 별도 조합할지는 미결(WL-079 스탯 표시 다중화 축과 함께). #183 완료기준에는 없음.

### 8.4 시각 피드백
- 집합에 든 타워를 월드에서 강조(아웃라인/하이라이트). 코디네이터가 마커의 그룹 훅(§7.1)으로 켜고 끈다 — **단일 선택 하이라이트와 별개**. 아트·연출 방식 TBD.

---

## 9. 실행 흐름 (#195, 완료 — 후보 버튼이 부르는 대상)

```
후보 버튼 onClick → 코디네이터.RequestMerge(recipe) → TowerFusionController.TryFuse(recipe, group)
  ① 그룹 타워 → TowerID 목록 (null/파괴/Asset 없음 제외)
  ② TowerFusionMatcher.BuildRequired(recipe) → (TowerID,개수) 집계
  ③ TowerFusionMatcher.TryResolve → 소모할 타워 인덱스 확정 (부족 시 중단·로그)
  ④ ManagementController.CanAfford(recipe.ExtraCost) (관리 없으면 무료)
  ⑤ 결과 SO의 런타임 Data 방어 채움(패널 경로 안 거칠 때 대비)
  ⑥ TowerPlacer.BeginTowerPlacement(recipe.Result, recipe.ExtraCost, onConfirmed)
       고스트 → 타일 확정 → ExtraCost TrySpend + 결과 Instantiate
       → onConfirmed: 소모 대상 타워 group.Remove(OnChanged 발행) + Destroy
```

- **소모 시점 = 배치 확정 시점**(고스트 Esc 취소 시 재료·비용 보존). 재료 소모(`Destroy`)는 `Tower.OnDisable`로 `Tower.Active`에서 자동 해제되고, `TowerFootprint`(배치 인스턴스 부착)가 `OnDestroy`로 점유 타일을 해제한다 → 소모 자리 재배치 가능.
- **알려진 제약(F2, 현행 유지)**: 소모가 확정 시점이라 **재료가 점유한 타일에는 결과를 즉시 놓을 수 없다**(재료는 확정 후에야 `Destroy`되어 타일 해제). 지금은 이 제약을 안고 가고, 향후 커맨드 패턴('클릭 즉시 소모 + 취소 시 원복')으로 개선 예정(§13).
- 비용 지불은 `ManagementController.CanAfford/TrySpend`(WL-017 게이트웨이)로만 — `TowerPlacer` 확정 경로 재사용(별도 차감 로직 없음). 관리가 씬에 없으면 무료(permissive).

---

## 10. 게이팅 / 수명주기

- **낮(배치 페이즈) 전용** — 멀티 선택·패널 전환·실행 전부 낮에만. 밤에는 Shift+클릭 그룹 토글을 무시하고 패널 전환도 하지 않는다. 코디네이터가 `DayNightManager.Instance?.CurrentPhase == Day` 판정(`Instance` null이면 permissive, WL-002 완화 패턴), 입력 핸들러(`HandlePrimarySelect`/`HandleGroupToggle`)와 `RequestMerge` 진입에서 게이팅. → 밤 실행이 코디네이터 앞단에서 막혀 WL-077의 밤 순간이동이 UI 경로에선 발생하지 않는다. (실행부 `TryFuse` 자체 방어 게이팅은 defense-in-depth로 미추가 — muchan 협의 옵션.)
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
| 합성 실행(검증·배치·소모) | **`TowerFusionController`**(muchan) | `TryFuse(recipe, group)` |
| 결과 고스트 배치·타일 검증·점유 | **`TowerPlacer`** / `TowerFootprint` | TowerPlacement.md |
| 자원(ExtraCost) 지불 | **`ManagementController`** | `CanAfford`/`TrySpend`, WL-017 |
| 재료 원본 SO 조회 | **`Tower.Asset`**(Combat, 읽기) | SUNGSOO |
| 낮/밤 게이팅 신호 | **`DayNightManager`** | `CurrentPhase`/전환 이벤트 |

---

## 12. 인수 조건

**데이터·실행부 (#194/#195) — 완료**
- [x] 레시피 데이터 정의(재료 TowerID별 개수 → 결과 `TowerAsset` + `ExtraCost`)
- [x] 결과 타워를 일반 `TowerAsset`로 표현
- [x] 포함 매칭(정확 충족/여분 허용/부족 실패/다종 재료) — 순수 함수 검증 가능(EditMode 후보)
- [x] 그룹 충족 시 결과 타워 고스트 생성 → 타일 배치 → 확정 시 재료 `Destroy` + `ExtraCost` 차감
- [x] 재료·비용 부족 시 실행 안 됨(로그), 고스트 취소 시 재료·비용 보존

**선택/패널 UI (#183) — 코드 구현·컴파일 완료 / 아래는 정본 씬 배선 후 E2E로 확정할 인수 항목**
- [ ] 타워 1개 선택 → 인포 패널(기존 동작 회귀 없음).
- [ ] Shift로 타워 2개 이상 선택 → 인포 숨김 + 합성 패널 표시.
- [ ] 위 전환에서 **직전 단일 선택의 초록 사거리 원도 함께 사라진다**(WL-087 회귀 감시 — 원이 남으면 합성 패널 시인성을 해친다). 1개로 축소하면 다시 뜨고, 0·빈 곳·Esc·밤 전환에서도 남지 않는다. `AuraTower`(마법 타워)나 건물을 선택해 둔 채 Shift로 타워를 담기 시작한 경우도 동일.
- [ ] 합성 패널 상단 리스트가 **선택 순서대로** 채워지고 집합 변경 시 즉시 갱신.
- [ ] Shift+이미 선택된 타워 → 리스트에서 토글 제거(순서 유지).
- [ ] 선택 1개로 축소 시 인포 복귀, 0이면 숨김.
- [ ] 키 없이 타워 클릭 = 집합 해제 후 단일 선택, 빈 곳 클릭/Esc = 전체 해제(**우클릭은 해제 아님** — F3, 이슈 AC에서 의도적 이탈).
- [ ] Shift+건물/영지 노드 → 무시(합성 리스트 불변).
- [ ] (더미 레시피로) 집합이 레시피 재료를 **모두 포함**하면 해당 후보 버튼 `SetActive(true)`, 여분 허용, 미충족 시 비활성.
- [ ] 여러 레시피 동시 충족 시 여러 후보 버튼 동시 활성.
- [ ] 밤에는 멀티 선택/합성 패널 전환이 일어나지 않는다.
- [ ] 정본 `GameScene`에서 동작 확인(SceneWorkflow 준수).

> 검증: 개인 테스트 씬 Play 확인(팀 컨벤션 — 유닛 테스트 없음). 매칭(`TowerFusionMatcher`)은 순수 함수라 프로젝트 첫 EditMode 테스트 후보.

---

## 13. 열린 결정 / TBD / 의존

- **[구현 PR] SystemMap 갱신 필수**: #183은 MouseManager 공개 선택 계약(그룹 토글 이벤트·Idle Esc/빈곳 클리어 신호)과 신규 코디네이터/마커를 추가하므로, 구현 PR에서 `SystemMap.md`(§1 TowerFusion 행·§2 API·§3 접점 — MouseManager·TowerFusion 인근)를 같이 갱신한다.
- **추가 선택 키 = Shift**(§7.2): WL-073(우클릭 이중 점유) 회피 겸. 재조정 시 이 문서·구현 동시 수정.
- **레시피 카탈로그 출처 = 패널 인스펙터 배열 `TowerRecipe[] _recipes`**(§5, WL-076(a)): 후보 레시피 SO를 인스펙터에 등록. 순서 = 배열 순서(결정적). 예시 SO 2종은 `Assets/Resources/ScriptableObjects/TowerRecipes/`.
- **stale 버튼 방어**(§10, WL-076(b) 해소): `TowerMergeGroup.Prune(predicate)` + 코디네이터가 `Tower.ActiveChanged` 구독해 **`Tower.Active` 멤버십 기준**으로 호출(OnDisable 시점 가짜-null 미형성 문제 회피).
- **결과 배치·소모 타이밍(F2 결정)**: **현행 유지** — 새 타일에 고스트 배치 + 확정 시 재료 `Destroy`. 재료 타일 재사용 불가 제약(WL-077a)을 인지하고 안고 간다. **향후 방향 = 커맨드 패턴**: 버튼 클릭 즉시 재료를 소모(타일 해제)해 자리를 재사용 가능하게 하되, **배치 취소 시 소모한 재료를 원복**한다. 이때 `Destroy`는 되돌릴 수 없으므로, 커맨드는 즉시 파괴 대신 **비활성화(SetActive false + 타일/점유 해제)로 '소프트 소모'** → 확정 시 진짜 `Destroy`, 취소 시 재활성화·재점유로 원복하는 형태가 자연스럽다(재료 스냅샷 재구성도 대안). 도입 시 §9 흐름 교체.
- **낮/밤 실행 게이팅**(§10, WL-077 phase): 코디네이터 `RequestMerge`/입력 핸들러가 낮 게이팅 → UI 경로 밤 실행 차단. 실행부 `TryFuse` 자체 방어 게이팅은 미추가(옵션, muchan 협의).
- **결과 정보 패널 배선**(§8.3): 스탯 표시를 `Tower`/`TowerInfoUI`와 공유할지 별도 조합할지(WL-079 축).
- **결과 타워 콘텐츠**: 합성 결과용 신규 `TowerAsset`(`TowerTable.csv` 행 + 프리팹/고스트/스탯). 현재 테스트는 기존 타워를 결과로 재사용.
- **밸런스·규칙**(GDD §8): 레시피 족보(재료 조합→결과)·`ExtraCost` 수치·재료 승계(레벨/버프) 여부 미정.
- **드래그 범위 선택**: 후속 입력 확장(범위에 먼저 들어온 순서로 등록).

---

## 14. 확장 여지

- **다단 합성**: 합성 결과를 다시 다른 레시피 재료로(레시피가 `TowerAsset` 참조라 자연 지원).
- **레시피 조건 확장**: 재료 타워의 레벨·버프 상태 승계 여부(`Tower.ApplyBuff`/`RemoveBuff` 계약 연동 시).
- **연출**: 재료 소멸 → 결과 등장 이펙트.
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

**4. 그 외**
- `TowerInfoUI`(기존 씬 싱글톤)·`MouseManager`(기존, 코드만 확장)·`DayNightManager`(낮/밤 게이팅) 별도 배선 불필요.
- 레시피는 패널 `TowerMergePanelView`의 인스펙터 배열 `_recipes`에 **등록해야 후보로 뜬다**. 예시 SO 2종 `Recipe_Example_Gatling`(archer×2+cannon×1→gatling)·`Recipe_Example_Sniper`(archer×1+cannon×1→Sniper)는 `Assets/Resources/ScriptableObjects/TowerRecipes/`. **`Recipe_Example_Sniper`는 새로 추가된 SO라 `_recipes`에 손수 넣어야** "다중 후보 동시 활성"이 검증된다(archer×2+cannon×1 선택 시 두 버튼 동시 활성).
- **레이어**: 타워 콜라이더가 `MouseManager._selectableMask`에 포함돼야 Shift 클릭이 마커를 잡는다(기존 타워 단일 선택이 동작 중이면 이미 충족).

**검증 체크리스트(Play)**: 타워 1개=인포 / Shift 2개=인포 숨김+합성 패널(리스트 순서대로) / archer 2개 → `Recipe_Example_ArcherToGatling` 버튼 활성 → 클릭 → 고스트 배치·확정 → archer 2개 소멸+gatling 생성 / Shift로 1개로 축소=인포 복귀, 빈곳·Esc=해제 / Shift+건물=무시 / 밤 전환=선택·패널 리셋.
