# 전투 공간 타워 합성(Tower Merge) — 기능 명세 (진실의 원천)

> **상태**: 데이터 구조(#194)·실행부(#195) **구현·검증 완료** · 선택/패널 UI(#183) **명세 확정, 구현 예정**
> **소유**: muchan(데이터·실행 #194/#195) · n0wst4ndup(선택·패널 UI #183) · SUNGSOO(타워 프리팹/전투)
> **구현 파일(완료분, #194/#195)**:
> - `Assets/Scripts/Data/Tower/TowerRecipe.cs` — 레시피 SO(재료/결과/추가비용)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerFusionMatcher.cs` — 포함 매칭(순수 static)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerFusionController.cs` — 실행 진입점
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerWallet.cs` — 재료 후보 홀더(임시)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerPlacer.cs` — 배치 오버로드 추가
> - `Assets/Scripts/CombatSystem/Tower/Tower.cs` — `Asset` 읽기 접근자 추가
> **신규 파일(예정, #183)**: 선택 코디네이터 · 그룹 선택 마커 인터페이스 · 합성 패널 뷰 · 패널 스위처 (§7·§8, 명칭은 §3 네이밍 규칙)
> **관련**: GDD §5.8, `Docs/Build2/2팀 빌드 2 다음 빌드 계획.md` §1, WL-076·WL-077, 이슈 #183/#194/#195
> **참조**: `Docs/Core/TowerPlacement.md`, `Docs/Core/MouseManager.md`, `Docs/Review/SystemMap.md`(§1 TowerFusion 행·§2 API·§3 접점)
> **문서 계약**: 코드가 이 명세와 어긋나면 문서를 갱신한다(팀 계약 #7). 공개 API·계약이 바뀌는 PR은 SystemMap을 같은 PR에서 갱신한다.
>
> ⚠️ **네이밍(문서=합성/Merge, 코드=Fusion)**: 이 시스템은 기획·이슈·GDD에서 **"타워 합성(Tower Merge)"** 으로 부르지만, #194/#195에서 병합된 코드 식별자는 **`Fusion` 접두**를 쓴다(§3 매핑). 리네임은 muchan 병합 코드 대폭 수정이라 별건으로 미룬다 — 이 문서를 읽을 때 "합성 = `TowerFusion*`"으로 대응시킬 것. 이 문서가 `TowerFusion.md`를 대체·폐기하는 단일 진실 원천이다.

---

## 0. 설계 요지

- **결과 타워는 특수 타입이 아니라 일반 `TowerAsset`이다.** 합성 결과도 신규 타워 종류(§13 콘텐츠)로 `TowerTable.csv` 행 + `Towers/` SO를 만들면 기존 배치·전투 파이프라인을 그대로 탄다. 새 런타임 타입 불요.
- **레시피는 SO 전용(`TowerRecipe`)** — 재료/결과가 `TowerAsset` 참조라 CSV ID 문자열 resolve보다 인스펙터 직접 드래그가 자연스럽다(CSV 미경유).
- **선택 UI와 실행부의 유일한 이음매 = 재료 집합(`TowerWallet`).** 선택(#183)은 "선택된 재료 타워를 집합에 넣고 빼는 것"까지만 하고, 그 다음(매칭·비용·소모·배치)은 실행부(#195)가 이미 처리한다. **집합이 이음매**라 실행부는 선택 UI가 붙어도 무수정.
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
- 실행(`TowerFusionController.TryFuse`): 매칭 검증 → `CanAfford` → `TowerPlacer` 고스트 배치 → 확정 시 `ExtraCost` 지불 + 재료 `Destroy`
- 임시 재료 홀더(`TowerWallet`, 인스펙터 드래그) + 테스트 버튼 1개

**In — 명세 확정·구현 예정 (#183, 이 문서의 주 대상)**
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
| 타워 합성 / Merge | (기능명, 코드 접두는 `Fusion`) | 리네임은 별건 |
| 합성 실행부 | `TowerFusionController` | `TryFuse(recipe)` / `TryFuseSelected()` |
| 매칭 규칙 | `TowerFusionMatcher` | 순수 static, `TryResolve`/`BuildRequired`/`CanFuse` |
| 재료 집합(선택 상태) | `TowerWallet` | `Towers`/`Add`/`Remove`/`Clear`/`OnChanged` |
| 레시피 | `TowerRecipe` (SO) | `Materials`/`Result`/`ExtraCost` |
| 결과·재료 타워 정의 | `TowerAsset` (SO) | 결과도 일반 타워 |
| 배치 타워의 원본 SO 조회 | `Tower.Asset` | 읽기 전용 |
| (예정 #183) 선택 코디네이터 | 신규 — 기존 `TowerFusion*` 형제와 일관되게 `TowerFusion` 접두 권장 | §7 |
| (예정 #183) 그룹 선택 자격 마커 | 신규 **도메인 중립** 인터페이스(예: `IGroupSelectable`) | MouseManager가 소비 → 타워 개념 없이 제네릭 |

> 신규 #183 UI/뷰 클래스명은 작성자 재량이나, 실행부·매칭과 같은 폴더의 형제 클래스와의 정합을 위해 접두는 통일할 것.

---

## 4. 아키텍처 개요

```
[플레이어 클릭/수정키]
        │  (입력 단일 창구 — 계약 #1)
        ▼
   MouseManager ── 수정키 없음 ──▶ 단일 선택 (OnSelectionChanged) ─┐
        │                                                          │
        └─ 수정키 + 마커(IGroupSelectable) ─▶ 그룹 토글 이벤트 ──┐  │
                                                                │  │
                                                                ▼  ▼
                                            [선택 코디네이터] (순서 있는 재료 집합 소유)
                                                     │  낮 게이팅·리셋(§10)
                                       ┌─────────────┼──────────────┐
                                       ▼             ▼              ▼
                                  TowerWallet   그룹 하이라이트   패널 스위처
                                  (이음매)       (마커 훅)       (0/1/≥2 분기)
                                       │                              │
                    ┌──────────────────┘                             ▼
                    ▼                                    1개=TowerInfoUI / ≥2=합성 패널
        TowerFusionMatcher.CanFuse ── 후보 버튼 활성 판정 ◀── 합성 패널(후보 버튼)
                                                                      │ onClick
                                                                      ▼
                                              TowerFusionController.TryFuse(recipe)
                                                 매칭 → CanAfford → TowerPlacer 고스트 배치
                                                 → 확정 시 ExtraCost 지불 + 재료 Destroy
```

**흐름 요약**: 선택(MouseManager+마커) → 집합 소유(코디네이터) → 이음매(지갑) → {버튼 활성 판정 = 매칭, 실행 = 컨트롤러}. 버튼 활성 판정과 실행이 **같은 매칭 함수**(`TowerFusionMatcher`)를 공유해 규칙이 단일 출처다.

---

## 5. 데이터 모델 (#194, 완료)

- **`TowerRecipe`(SO)**: `List<MaterialEntry> Materials`(재료 `TowerAsset`+`Count`, multiset) / `TowerAsset Result` / `List<ResourceCost> ExtraCost`(합성 추가 자원/마나석). CSV 미경유 인스펙터 손입력.
- **결과 타워**: 별도 특수 타입이 아니라 일반 `TowerAsset`. 신규 결과 타워는 `TowerTable.csv` 행 + SO + 프리팹/고스트/스탯을 추가(§13 콘텐츠).
- **레시피 카탈로그(전체 열거)**: #183 후보 버튼 패널이 순회·매칭하려면 전체 레시피 열거가 필요하다. **출처 결정: `Resources.LoadAll<TowerRecipe>`**(전체 열거가 #194 완료기준이며, 패널마다 직렬화 리스트를 손으로 채우는 누락 위험을 없앰). `TowerRecipe` SO를 `Assets/Resources/…` 하위 규약 경로에 둔다. (WL-076(a) 카탈로그 출처 미정 해소 방향.)
  - **버튼 순서는 결정적으로**(F6): `Resources.LoadAll`은 반환 순서를 보장하지 않아 그대로 쓰면 플랫폼/빌드마다 후보 버튼 순서가 흔들린다. 로드 후 명시 정렬(레시피에 `SortOrder`(int) 필드 추가 또는 `Result.TowerID` 기준)로 순서를 고정한다.

---

## 6. 매칭 규칙 — 포함 매칭 (#194, 완료)

- 레시피 재료를 **`(TowerID, 필요개수)`** 로 집계(`TowerFusionMatcher.BuildRequired`, 같은 종류가 여러 엔트리로 나뉘어도 합산, 무효 엔트리 무시).
- 선택 집합의 타워를 `Tower.Asset.TowerID`로 읽어, 레시피의 모든 `(종류, 필요개수)`를 **모두 포함**하면(선택 개수 ≥ 필요 개수) 성립.
- **여분 허용**: 레시피에 없는 종류·초과분이 섞여도 충족 유지. **소모는 필요 개수만큼만**(`TryResolve`가 소모 인덱스를 정확히 반환).
- **여러 레시피 동시 충족 → 여러 후보 버튼 동시 활성.**
- 후보 버튼 활성 판정 = `TowerFusionMatcher.CanFuse(wallet.Towers, recipe)` — **실행부와 같은 함수**를 써 규칙 재구현을 금지(단일 출처).

---

## 7. 선택 모델 (#183, 예정) — 코디네이터 + 마커

### 7.1 소유·자격 (확정 아키텍처)
- **선택 집합 소유 = 별도 선택 코디네이터**(MonoBehaviour). 순서 있는 재료 집합(선택된 순서 = 등록 순서)을 리스트로 보유한다. MouseManager는 집합을 들지 않는다.
- **그룹 선택 자격 = 도메인 중립 마커 인터페이스**(예: `IGroupSelectable`). **타워만** 이를 구현하고, 건물·영지 노드 등 다른 `ISelectable`은 구현하지 않는다 → MouseManager는 "타워"를 모른 채 마커 유무로만 판정(제네릭 유지, SystemMap §6).
- 마커는 그룹 하이라이트 훅(예: `OnGroupSelected()`/`OnGroupDeselected()`)을 **단일 선택 훅(`ISelectable.OnSelected/OnDeselected`)과 분리**해 노출한다 — 코디네이터가 집합 가감 시 호출(§8.4).

### 7.2 MouseManager 계약 확장 (입력 단일 창구)
현재 `MouseManager`는 완전 단일 선택(`_selected` 단일 참조)이고 Idle에는 우클릭/Esc 처리가 없다. #183은 다음을 **추가**한다(기존 `OnSelectionChanged(ISelectable)` 시그니처는 무변경 — 기존 구독자 보호):

- **추가 선택 키 = Shift**(확정, 필요 시 재조정). 키 판정은 MouseManager가 소유(게임플레이 코드의 `Keyboard.current` 직접 폴링 금지, 계약 #1). *WL-073 유의: 우클릭이 카메라 드래그와 이미 이중 점유 → 추가 선택 키를 우클릭이 아닌 Shift로 두어 충돌을 피한다.*
- **그룹 토글 이벤트**(예: `OnGroupSelectToggled(IGroupSelectable)`) 신설: **Shift + 마커 대상** 클릭 시 발행. 이때 단일 `_selected`는 건드리지 않는다.
- **Idle Esc 처리 추가 + 빈 곳 클릭**(F3): 빈 곳 좌클릭(기존 `Select(null)`)·Esc → 코디네이터에 **전체 해제** 신호(전용 clear 이벤트 또는 `OnSelectionChanged(null)` 경유). **우클릭은 해제에 쓰지 않는다** — 우클릭은 이미 카메라 드래그(WL-073)·배치/조준 취소로 이중 점유라, 세 번째 의미를 얹으면 카메라 팬 중 선택이 사라진다. (이슈 #183 AC의 '우클릭=해제'에서 **의도적으로 이탈** — F3 결정.)

### 7.3 입력 규칙 (이슈 §상세)
| 입력 | 동작 |
| --- | --- |
| 키 없이 타워 클릭 | 집합 전체 해제 후 그 타워 **단일 선택** |
| Shift + 미선택 타워 | 집합 **끝에 추가**(순서 보존) |
| Shift + 이미 선택된 타워 | 집합에서 **토글 제거**(나머지 순서 유지) |
| Shift + 건물/영지 노드 등 비-타워 | **무시**(집합 불변 — 마커 없음) |
| 빈 곳 클릭 / Esc | **전체 해제** |
| 우클릭 | 해제 아님 — 카메라 드래그·배치/조준 취소 전용(WL-073, F3) |

### 7.4 집합 = 지갑 (이음매, 단일 리스트)
- 코디네이터는 **별도 집합 리스트를 두지 않고 `TowerWallet`을 유일한 백킹 스토어로 직접 조작**한다(`Add`/`Remove`/`Clear`)(F4). 즉 집합은 `wallet.Towers` 자체이고 순서는 지갑 삽입 순서 — 두 리스트를 동기화하다 어긋나는 버그 표면을 없앤다. 실행부(`TowerFusionController`)·매칭(`TowerFusionMatcher`)은 **무수정**으로 같은 지갑을 소비.
- MouseManager가 넘기는 것은 도메인 중립 `IGroupSelectable`이므로, 코디네이터가 `Tower`로 변환해(현재 마커 구현체는 `Tower`뿐) `wallet.Add(tower)`. 재료 식별은 `tower.Asset.TowerID`, `Tower.Asset`이 null인 항목은 제외.
- 임시 `TowerWallet`의 인스펙터 `List<Tower>` 손드래그는 코디네이터 배선 후 **테스트 폴백**으로만 남기거나 제거(실행부는 어느 쪽이든 무영향).

---

## 8. 패널 UX (#183, 예정)

### 8.1 패널 스왑 (오른쪽 패널 한 자리) — 스위처가 단일 권위
**우측 패널의 최종 결정권은 스위처 하나**로 못박는다(F1, 집합 크기 이벤트 구독). 기존 단일선택 경로(`Tower.OnSelected/OnDeselected`→`TowerInfoUI`, #153)와 경합하지 않도록, 스위처가 집합 크기로 3분기하고 인포 표시/숨김은 **idempotent**하게 다룬다:
- **0개** → 두 패널 모두 숨김(`TowerInfoUI.HideInfo()` + 합성 패널 off).
- **1개** → 인포 패널만. 그 타워의 정보 표시는 **멤버 타워의 `OnSelected()`를 (재)호출해 재사용**한다(스탯 조립을 재구현하지 않음). 특히 **2→1 축소**는 단일선택 `OnSelected`가 재발화되지 않으므로 스위처가 명시적으로 호출.
- **2개 이상** → 합성 패널 표시 + 스위처가 **능동적으로 `TowerInfoUI.HideInfo()`**(직전 단일선택이 띄워둔 인포를 확실히 내림).

두 패널은 **동시에 보이지 않는다.** `_selected`(MouseManager) vs 집합(코디네이터)의 관계: **count≤1에선 사실상 일치, count≥2에선 `_selected`는 마지막 평클릭 대상일 뿐 합성 흐름은 쓰지 않는다.** 표시/숨김이 idempotent라 기존 MouseManager 경로가 같은 인포를 한 번 더 켜/꺼도 무해 — 단 "무엇을 보일지"의 판단은 항상 스위처가 이긴다.

### 8.2 합성 패널 구성
- **상단 Vertical Scroll View — 선택 리스트**: 선택된 재료 타워를 **선택 순서대로** 한 행씩. 집합 변경 시 즉시 갱신. 행 라벨 = `tower.Asset.TowerID` → `TowerData.NameKey` → 로컬라이즈(`NorthLand_Towers`, `LocalizationHelper.Get`). (행별 제거 버튼은 선택.)
- **하단 Horizontal Scroll View — 후보 버튼**: **레시피(카탈로그)마다 버튼 1개를 미리 생성해 담아두고 기본 `SetActive(false)`**. 매칭되는 레시피의 버튼만 `SetActive(true)`.
  - 활성 판정 = `TowerFusionMatcher.CanFuse(wallet.Towers, recipe)`. (매칭 규칙 재구현 금지 — §6 단일 출처.)
  - `ExtraCost` 감당 여부(`ManagementController.CanAfford(recipe.ExtraCost)`)는 (선택) `interactable`/딤 표시로 구분하되, **최종 검증은 실행부(`TryFuse`)가 한다**(방어). #183 완료기준은 매칭 기반 `SetActive`까지.
  - 버튼 표시 = 결과 타워(`recipe.Result`) 이름(→ `Result.TowerID` → `NameKey` 로컬라이즈). 아이콘 필드가 생기면 교체.
  - **onClick → `TowerFusionController.TryFuse(recipe)`** 한 줄(버튼이 자기 `TowerRecipe`를 클로저로 물음).
  - **갱신 시점 = 지갑이 바뀔 때마다** 전 버튼 재판정 — `TowerWallet.OnChanged` 구독(기존 `TowerSelectPanelView`가 `ManagementController.OnChanged`→`RefreshAffordability`를 구독하는 것과 동형).
  - **UX 트레이드오프(경미)**: `SetActive` 방식은 비매칭 버튼이 사라져 스크롤뷰가 리플로우된다(선택 변경마다 버튼이 튀어나왔다 사라짐). 이슈가 택한 방식이라 유지하되, 튐이 거슬리면 '전체 표시 + `interactable`로 회색' 대안 고려. 또 **여분 허용 시 실제 소모될 재료가 무엇인지**(선택 순서 index로 결정)는 리스트에 표시되지 않음 — 후속 폴리시(호버 시 소모 대상 하이라이트).

> **주의**: 이 하단 후보 버튼 영역은 **배치 팔레트(`TowerSelectPanelView`, 새 타워 건설 선택)와 다르다.** 합성 패널은 이미 배치된 타워들의 조합 결과를 보여준다. 골격은 `TowerSelectPanelView`를 참고 모델로 삼되(버튼 동적 생성·조건부 활성·클릭 시 배치 진입), 대상이 `List<TowerRecipe>` + 매칭 여부 + `TryFuse`로 바뀐다.

### 8.3 결과 정보 패널 (선택, 후속)
현재 선택으로 만들 수 있는 결과 타워(활성 후보 중 선택/호버한 레시피)의 `Result` 스탯을 표시. `Tower`의 스탯 텍스트 조립 규칙과 공유할지 별도 조합할지는 미결(WL-079 스탯 표시 다중화 축과 함께). #183 완료기준에는 없음.

### 8.4 시각 피드백
- 집합에 든 타워를 월드에서 강조(아웃라인/하이라이트). 코디네이터가 마커의 그룹 훅(§7.1)으로 켜고 끈다 — **단일 선택 하이라이트와 별개**. 아트·연출 방식 TBD.

---

## 9. 실행 흐름 (#195, 완료 — 후보 버튼이 부르는 대상)

```
후보 버튼 onClick → TowerFusionController.TryFuse(recipe)
  ① 지갑 타워 → TowerID 목록 (null/파괴/Asset 없음 제외)
  ② TowerFusionMatcher.BuildRequired(recipe) → (TowerID,개수) 집계
  ③ TowerFusionMatcher.TryResolve → 소모할 타워 인덱스 확정 (부족 시 중단·로그)
  ④ ManagementController.CanAfford(recipe.ExtraCost) (관리 없으면 무료)
  ⑤ 결과 SO의 런타임 Data 방어 채움(패널 경로 안 거칠 때 대비)
  ⑥ TowerPlacer.BeginTowerPlacement(recipe.Result, recipe.ExtraCost, onConfirmed)
       고스트 → 타일 확정 → ExtraCost TrySpend + 결과 Instantiate
       → onConfirmed: 소모 대상 타워 Destroy + wallet.Remove
```

- **소모 시점 = 배치 확정 시점**(고스트 Esc 취소 시 재료·비용 보존). 재료 소모(`Destroy`)는 `Tower.OnDisable`로 `Tower.Active`에서 자동 해제되고, `TowerFootprint`(배치 인스턴스 부착)가 `OnDestroy`로 점유 타일을 해제한다 → 소모 자리 재배치 가능.
- **알려진 제약(F2, 현행 유지)**: 소모가 확정 시점이라 **재료가 점유한 타일에는 결과를 즉시 놓을 수 없다**(재료는 확정 후에야 `Destroy`되어 타일 해제). 지금은 이 제약을 안고 가고, 향후 커맨드 패턴('클릭 즉시 소모 + 취소 시 원복')으로 개선 예정(§13).
- 비용 지불은 `ManagementController.CanAfford/TrySpend`(WL-017 게이트웨이)로만 — `TowerPlacer` 확정 경로 재사용(별도 차감 로직 없음). 관리가 씬에 없으면 무료(permissive).

---

## 10. 게이팅 / 수명주기

- **낮(배치 페이즈) 전용** — 멀티 선택·패널 전환뿐 아니라 **실행(`TryFuse`)도** 낮에만. 밤에는 Shift+클릭 그룹 토글을 무시하고 패널 전환도 하지 않는다. `DayNightManager.Instance?.CurrentPhase == Day` 판정, `Instance` null이면 permissive(WL-002 완화 패턴과 동일). *현재 `TryFuse`에는 낮/밤 게이팅이 없어 밤 합성 시 타워 순간이동이 가능(WL-077) → 진입 게이팅을 실행부에도 추가해 해소.*
- **리셋**: 밤 진입(`OnDayToNight`)·씬 전환 시 (1) 선택 집합·지갑을 비우고, (2) **진행 중인 합성 고스트 배치도 취소**(`MouseManager.CancelPlacement()`)한다(F5). (2)를 빠뜨리면 밤으로 넘어간 뒤 확정 시 재료가 파괴되어 밤 순간이동/유령 소모가 남는다(`PhasePanelSwitcher`가 밤에 스킬 조준을 끊는 것과 동형). 리셋 시 지갑/집합의 타워는 이미 파괴됐을 수 있으므로 `OnGroupDeselected()` 등 **훅을 호출하지 말고 리스트만 비운다**(파괴된 `Tower`에 메서드를 부르면 NRE — WL-033의 인터페이스-null과는 다른 축이지만 같은 '죽은 참조 역참조 금지' 규율).
- **코디네이터 수명주기**(F7): MouseManager는 `DontDestroyOnLoad`라 씬보다 오래 산다 → 코디네이터(씬 오브젝트)는 `OnDestroy`에서 MouseManager 이벤트를 **반드시 구독 해제**한다(안 하면 씬 언로드 후 죽은 구독자를 호출 — `Projectile.DamageDealt` static 구독 주의와 같은 계열).
- **외부 파괴 대응**(WL-076(b)): 지갑/집합에 든 타워가 외부 사유(철거·전투 사망 등)로 파괴되면 `TowerWallet.OnChanged`가 발행되지 않아 후보 버튼이 stale해질 수 있다 → 코디네이터가 파괴를 감지해 집합에서 제거하거나 `TowerWallet.Prune()`(파괴된 항목 정리 + 이벤트 발행)을 도입한다.

---

## 11. 시스템 책임 분담

| 단계 | 소유 | 비고 |
| --- | --- | --- |
| 포인터/키보드 입력·레이캐스트·마커 판정 | **MouseManager** | 도메인(타워) 무지, 마커만 앎. 계약 #1 |
| 선택 집합(순서)·낮 게이팅·리셋·지갑/하이라이트/패널 구동 | **선택 코디네이터**(#183, n0wst4ndup) | MouseManager 이벤트 구독 |
| 재료 집합 저장(이음매) | **`TowerWallet`** | 실행부·매칭이 소비 |
| 매칭 규칙 | **`TowerFusionMatcher`**(순수) | 버튼 활성·실행 공유 단일 출처 |
| 합성 실행(검증·배치·소모) | **`TowerFusionController`**(muchan) | `TryFuse(recipe)` |
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
- [x] 지갑 충족 시 결과 타워 고스트 생성 → 타일 배치 → 확정 시 재료 `Destroy` + `ExtraCost` 차감
- [x] 재료·비용 부족 시 실행 안 됨(로그), 고스트 취소 시 재료·비용 보존

**선택/패널 UI (#183) — 예정**
- [ ] 타워 1개 선택 → 인포 패널(기존 동작 회귀 없음).
- [ ] Shift로 타워 2개 이상 선택 → 인포 숨김 + 합성 패널 표시.
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
- **레시피 카탈로그 출처 = `Resources.LoadAll<TowerRecipe>`**(§5, WL-076(a)): 규약 경로 확정 필요. 직렬화 리스트 대안은 누락 위험으로 비권장.
- **stale 버튼 방어**(§10, WL-076(b)): `TowerWallet.Prune()` 또는 코디네이터의 파괴 감지 — #183에서 도입.
- **결과 배치·소모 타이밍(F2 결정)**: **현행 유지** — 새 타일에 고스트 배치 + 확정 시 재료 `Destroy`. 재료 타일 재사용 불가 제약(WL-077a)을 인지하고 안고 간다. **향후 방향 = 커맨드 패턴**: 버튼 클릭 즉시 재료를 소모(타일 해제)해 자리를 재사용 가능하게 하되, **배치 취소 시 소모한 재료를 원복**한다. 이때 `Destroy`는 되돌릴 수 없으므로, 커맨드는 즉시 파괴 대신 **비활성화(SetActive false + 타일/점유 해제)로 '소프트 소모'** → 확정 시 진짜 `Destroy`, 취소 시 재활성화·재점유로 원복하는 형태가 자연스럽다(재료 스냅샷 재구성도 대안). 도입 시 §9 흐름 교체.
- **낮/밤 실행 게이팅**(§10, WL-077 phase): `TryFuse` 진입에 낮 전용 게이팅 추가로 밤 순간이동 방지.
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
