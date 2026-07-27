# 상호작용 아웃라인(Interaction Outline) — 설계 명세

> **상태**: **렌더 경로 스파이크 검증 완료(§10) · 컴포넌트 구현 미착수(C# 0줄)**. 검증으로 확정된 것은 URP 설정 2개(`PC_Renderer`/`Mobile_Renderer`에 아웃라인 렌더러 피처 추가 + 레이어 마스크 제외)와 `OutlineShell` 레이어(12) 신설이며 **이미 커밋 대상**이다. 아래 "구현 예정 파일" 중 `.cs`·SO는 **아직 존재하지 않는다**. 구현이 진행되면 각 절의 `TODO`를 실제 동작으로 갱신한다.
> **소유**: n0wst4ndup(#213)
> **이슈**: #213 [Feature] 상호작용 아웃라인 — 호버=노란색 / 선택=초록색(그룹 포함) / 합성 후보 버튼 호버 시 재료 타워만 핑크색
> **구현 예정 파일**(전부 신규 또는 수정 예정 — 미착수):
> - `Assets/Scripts/GameManager/MouseManager/Highlight/OutlineHighlight.cs` — (신규) 아웃라인 표시 컴포넌트, 상태 플래그·색 우선순위·shell 생성/정리
> - `Assets/Scripts/GameManager/MouseManager/Highlight/OutlineInteractionDriver.cs` — (신규) MouseManager 이벤트 구독 → 호버 노랑·단일 선택 초록 구동
> - `Assets/Scripts/GameManager/MouseManager/Highlight/IOutlineTargetProvider.cs` — (신규) 아웃라인 대상 GO를 대신 지정하는 훅(영지 노드용)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerGroupSelectable.cs` — (수정) 하늘색 쿼드 제거, `IHoverable` 추가 구현, 그룹 초록
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerMergeCoordinator.cs` — (수정) `PreviewMerge`/`ClearMergePreview` 추가
> - `Assets/Scripts/UI/TowerPanel/TowerMergeCandidateHover.cs` — (신규) 후보 버튼 EventSystem 호버 → 코디네이터 프리뷰 호출
> - `Assets/Scripts/UI/TowerPanel/TowerMergePanelView.cs` — (수정) `BuildCandidates`에서 위 컴포넌트 배선
> - `Assets/Scripts/ManagementSpace/Territory/View/TerritoryNodeStateVisual.cs` — (수정) `IOutlineTargetProvider` 구현(회오리=null, 섬=인스턴스)
> - `Assets/Scripts/Editor/OutlineSmoothMeshBaker.cs` — (신규, 에디터) 대상 메시의 스무스 노멀 사본을 에셋으로 굽는 메뉴(§6.4)
> - `Assets/Scripts/GameManager/MouseManager/Highlight/OutlineSmoothMeshRegistry.cs` + `.asset` — (신규) 원본 메시 → 스무스 사본 매핑 SO(런타임 부착 컴포넌트가 인스펙터 배선 없이 조회)
> - `Assets/Settings/PC_Renderer.asset` · `Assets/Settings/Mobile_Renderer.asset` — (**적용 완료**) 아웃라인 렌더러 피처 추가 + `OutlineShell` 레이어 마스크 제외, PC/Mobile 동일
> - `ProjectSettings/TagManager.asset` — (**적용 완료**) `OutlineShell` 레이어(12) 신설
> **관련**: #148(전역 비주얼 룩 파이프라인 — 툰 셰이더), #138(경영 공간 건물 시인성), #67(호버 훅), #183/#192/#210, WL-076b·WL-085·WL-087
> **참조**: `Docs/Core/MouseManager.md` §8, `Docs/Core/TowerMerge.md` §8.4, `Docs/Review/SystemMap.md`, `Docs/Tools/unity-cli-guide.md`
> **문서 계약**: 코드가 이 명세와 어긋나면 문서를 갱신한다(팀 계약 #7). 공개 API·계약이 바뀌는 PR은 SystemMap을 같은 PR에서 갱신한다.

---

## 0. 설계 요지 (한 문단)

마우스 상호작용 상태를 **월드 오브젝트의 아웃라인**으로 표시한다. 색은 호버=노랑 / 선택=초록(그룹 포함) / 합성 재료 프리뷰=핑크(모두 **임시, 아트 TBD**). 표시 수단은 **FlatKit의 per-object 아웃라인 패스**를 쓰되, 대상 오브젝트의 머티리얼은 **건드리지 않고** 대상 메시를 공유하는 **아웃라인 전용 자식 렌더러(shell)** 에만 FlatKit 머티리얼을 입힌다. 상태 관리는 대상별 `OutlineHighlight` 컴포넌트 하나에 모으고, 구동은 `MouseManager` 이벤트를 구독하는 드라이버 1개 + 그룹 선택은 `TowerMergeCoordinator`가 담당한다. 전체 머티리얼을 FlatKit으로 바꾸는 컨버전은 **#148의 일이고 이 이슈에서 하지 않는다** — 대신 #148이 끝나면 이 문서 §9의 이행 경로로 shell을 걷어낸다.

---

## 1. 목적 / 범위

**목적**: "지금 무엇에 커서가 있고, 무엇이 선택돼 있고, 이 버튼을 누르면 무엇이 소모되는가"를 월드에서 즉시 읽히게 한다. `Docs/Core/MouseManager.md` §8의 "호버 하이라이트 연출" 잔여분(#67에서 훅만 만들고 연출을 미룬 부분)과 `Docs/Core/TowerMerge.md` §8.4 "선택 타워 월드 하이라이트(아트 TBD)"를 아웃라인으로 확정한다.

**In**
- 호버 노랑: 건물(`BuildingTooltipSource`), 배치된 타워, 영지 노드의 **확보 후 섬/산**
- 선택 초록: `ISelectable` 단일 선택(건물·`AuraTower`·타워) + `IGroupSelectable` 그룹 선택(타워 다중)
- 합성 프리뷰 핑크: 합성 패널 후보 버튼 호버 시 **실제로 소모될 재료 타워만**
- `TowerGroupSelectable`의 임시 하늘색 바닥 쿼드 제거

**Out**
- 전체 머티리얼 FlatKit 컨버전 / 툰 룩 전환 → **#148**
- 최종 색·선 굵기 아트 확정 → 아트 TBD (이 이슈는 임시 색 3종을 한 곳에서 바꿀 수 있게만 보장)
- 가림(엑스레이) 관통 표시 → 별건
- 적/몬스터, UI 요소, 타일 그리드

---

## 2. 결정과 근거

### 2.1 결정: shell 방식(자산 무수정) — "A안"

| | shell(**채택**) | 머티리얼 컨버전 + 렌더러 피처(반려, #148로) |
|---|---|---|
| 대상 머티리얼 | 무수정 | 전부 FlatKit Stylized Surface로 교체 |
| 상시 비용(하이라이트 0건) | **0** | Selectable 레이어 전체를 아웃라인 패스로 매 프레임 재드로우 |
| 하이라이트 1건 추가 비용 | 대상 렌더러 수만큼 | 0 |
| 룩 변경 | 없음 | 게임 전체 셀셰이딩화(아트 결정 필요) |
| 벤더 자산 수정 | 없음 | `Assets/Imported/Sweet_Land` 수정 또는 복제+전 프리팹 재배선 |
| FBX 내장 머티리얼 | 무관 | 추출 필요(영지 산) |

**측정 근거** (2026-07-27, `GameScene` 열린 상태에서 `unity-cli exec`로 실측):

| 항목 | 값 |
|---|---|
| 씬 활성 Renderer 총계 | 1695 |
| 그중 `Selectable`(레이어 6) | **985** |
| 그중 메인 카메라 프러스텀 내 | **913** |
| `ArcherTower.prefab` 렌더러 | MeshRenderer 1 |
| `SodaTower.prefab` 렌더러 | MeshRenderer 2 |
| `Castle.prefab` 렌더러 | **MeshRenderer 441 + SkinnedMeshRenderer 21** |
| `Mountain_01.prefab` 렌더러 | SkinnedMeshRenderer 1 (BlendShapeAnimator) |

재현 명령(참고):

```bash
printf '%s\n' 'var all = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);' \
 'var d = new System.Collections.Generic.Dictionary<int,int>();' \
 'foreach (var r in all) { int l = r.gameObject.layer; d.TryGetValue(l, out int c); d[l] = c + 1; }' \
 'var sb = new System.Text.StringBuilder("total=" + all.Length + "\n");' \
 'foreach (var kv in d) sb.Append("layer " + kv.Key + " " + UnityEngine.LayerMask.LayerToName(kv.Key) + " = " + kv.Value + "\n");' \
 'return sb.ToString();' | unity-cli exec
```

**해석**: FlatKit의 per-object 아웃라인 피처는 `RenderObjects` 파생이라 **레이어 마스크에 걸린 오브젝트를 아웃라인 패스로 한 번 더 전부 그린다.** `_OutlineWidth = 0`이어도 드로우콜과 정점 처리는 발생하고 프래그먼트만 깊이 테스트에서 탈락한다. 즉 마스크를 `Selectable`로 두면 아무것도 하이라이트하지 않아도 상시 ~913 드로우가 붙는다(Mobile 타깃 존재 → 아트 결정 없이 지금 지불할 비용이 아니다). shell은 켜진 것만 낸다.

### 2.2 컨버전을 지금 하지 않는 이유(#148로 미루는 근거)

1. 타워 10종·건물 8종이 Sweet_Land 머티리얼 **8개**(`Color`, `Color_2`, `Color_2_Glossy`, `Emission`, `Ground`, `Dots`, `Glass`, `Plasma`)를 공유한다 → 컨버전 = **게임 전체 룩 일괄 변경**. 이건 아트 방향 결정(#148)이지 상호작용 피드백 이슈의 결정이 아니다.
2. 영지 산(`Mountain_01~06`)은 `.mat` 참조 없이 `Sweet_Land/Meshes/Mountains/*.fbx` **내장 머티리얼**을 쓴다 → 컨버전 시 추출·재배선 선행 필요.
3. `Assets/Imported/` 벤더 트리 수정은 CLAUDE.md 금지 항목 → `@NorthLand`로 복제 후 전 프리팹 재배선이 필요하고, 그 디프가 "상호작용 아웃라인" PR에 섞인다.
4. #148의 툰 셰이더가 FlatKit Stylized Surface로 확정인지 Shader Graph인지 미정 → 지금 컨버전하면 재작업 위험.

### 2.3 발견된 기존 불일치(#148에서 정리할 것)

- **일부 타워 파트는 이미 FlatKit 머티리얼이다.** `SniperTower`·`SodaTower`·`GatlingShooter`가 FlatKit **데모** 머티리얼(`FlatKit/Demos/[Water] Ocean Islands/…/WaterScene_*.mat`, 셰이더 = `FlatKit/Stylized Surface`)을 참조한다. 데모 자산을 게임 프리팹이 물고 있는 상태 자체가 정리 대상이다.
- 우리 콘텐츠 머티리얼 중 FlatKit 셰이더를 쓰는 것은 위 데모 참조뿐이고, 나머지는 전부 URP Lit(`933532a4fcc9baf4fa0491de14d08ed7`)이다.

### 2.4 반려한 다른 대안

| 대안 | 반려 이유 |
|---|---|
| FlatKit 스크린스페이스 `Outline` 렌더러 피처(포스트 엣지 검출) | 전역·단색 — 대상별 3색이 불가능 |
| 하이라이트 대상을 전용 레이어로 옮겨 피처 마스크를 좁히기 | `_selectableMask` 레이캐스트와 물리 레이어 매트릭스를 깨뜨린다(#213 본문 경계 항목) |
| 대상 Renderer의 `materials` 배열에 아웃라인 머티리얼 append | 원본 머티리얼들이 인스턴스로 복제돼 누수·배칭 손실, 서브메시가 여러 개면 **마지막 서브메시만** 아웃라인, 다른 머티리얼 스왑 코드와 충돌 |
| 레거시 `FlatKit/Stylized Surface With Outline` 셰이더로 shell | 태그 없는(SRPDefaultUnlit) 패스라 PC의 **Deferred**(`m_RenderingMode: 2`)에서 그려질지 불확실 + CGPROGRAM/UnityCG 기반(SRP Batcher 비호환, URP 6000 경고 리스크). **모던 경로와 프로퍼티가 달라 #148 이행도 불리** |
| 자체 인버티드 헐 셰이더 신규 작성 | FlatKit으로 해결되는 것을 중복 구현. #148과 셰이더가 갈라진다 |

---

## 3. 렌더링 방식 상세 (shell)

### 3.1 구조

대상 트리에서 수집한 렌더러 하나마다 **자식 GameObject 하나**를 만든다.

```
Tower (Selectable 레이어, Collider, Tower, TowerGroupSelectable, OutlineHighlight)
└─ Mesh (MeshRenderer + MeshFilter)          ← 원본, 무수정
   └─ OutlineShell (OutlineShell 레이어)     ← 신규. 로컬 TRS = identity
      ├─ MeshFilter.sharedMesh = 원본 sharedMesh   (메시 복제 없음)
      └─ MeshRenderer.sharedMaterial = 3색 공유 머티리얼 중 하나
         shadowCastingMode = Off, receiveShadows = false, 콜라이더 없음
```

- **콜라이더를 아예 만들지 않는다** → 선택/호버 레이캐스트를 구조적으로 방해할 수 없다. (현행 `TowerGroupSelectable.CreateHighlight`가 `GameObject.CreatePrimitive` 후 `Destroy(GetComponent<Collider>())`로 피하던 함정을 없앤다.)
- 메시는 `sharedMesh` 공유 → **런타임 Mesh 생성 없음**(파괴 대상 아님).
- 머티리얼은 **색당 1개, static 공유**(아래 §3.3) → shell끼리 SRP Batcher로 묶이고, 대상마다 머티리얼 인스턴스를 만들지 않는다.
- 꺼질 때는 shell GO를 `SetActive(false)` — 파괴하지 않고 재사용(호버는 초당 여러 번 바뀐다).

### 3.2 아웃라인 패스를 그리는 경로

FlatKit의 per-object 아웃라인은 **`FlatKit/Stylized Surface` 셰이더의 pass 1("Outline", `Cull Front`, `Tags{"LightMode"="Outline"}`)** 이고, 이 패스는 URP 기본 파이프라인이 그리지 않는다 — `ObjectOutlineRendererFeature`(= `RenderObjects` 파생, `overrideShader = FlatKit/Stylized Surface`, `overrideShaderPassIndex = 1`, `PassNames = ["Outline"]`)가 필요하다.

- 이 피처를 **`PC_Renderer.asset`·`Mobile_Renderer.asset` 양쪽에** 1개씩 추가한다(CLAUDE.md: 렌더러/파이프라인 설정은 PC/Mobile 동시 적용).
- 피처의 **Layer Mask = `OutlineShell` 레이어만** → §2.1의 913 드로우 문제가 발생하지 않는다. 상시 비용은 "활성 shell 수"에 정확히 비례한다.
- shell의 **본체 패스(ForwardLit/GBuffer)가 그려지면 원본과 z-파이팅**한다. → Universal Renderer의 **`Opaque Layer Mask` / `Transparent Layer Mask`에서 `OutlineShell`을 제외**하면 본체 패스가 아예 그려지지 않는다(카메라 컬링 마스크는 그대로 두므로 cullResults에는 남아 렌더러 피처는 계속 그린다). **스파이크에서 검증됨(T1 해결) → 알파 클립 폴백은 불필요.** 적용값: `m_OpaqueLayerMask` = `m_TransparentLayerMask` = `4294963199`(= ~(1<<12)).
- 피처의 `Event`는 기본값 **300(AfterRenderingOpaques)** 을 쓴다 — 원본이 깊이를 쓴 뒤에 그려야 헐의 안쪽 면이 깊이 테스트에서 탈락하고 실루엣만 남는다.

### 3.4 파라미터 — 게임 카메라가 직교(orthographic)라는 전제

`Main Camera`는 **직교**이고 줌은 `CameraController2`가 `orthographicSize` **70~300**으로 클램프한다(씬 인스턴스 값. 스크립트 기본값 6/35는 씬에서 오버라이드됨). 이 조건에서 스파이크로 확정한 값:

| 프로퍼티 | 값 | 근거 |
|---|---|---|
| `_OutlineSpace` | `Screen` | 직교에서도 폭 제어가 예측 가능 |
| `_CameraDistanceImpact` | **1** | 직교는 `clipPosition.w == 1` 고정이라 기본값(0)이면 폭이 거리 항에 눌려 선이 거의 안 보인다. 1로 두면 `lerp(w, 4, 1) = 4` 상수가 되어 거리 의존이 사라진다 |
| `_OutlineDepthOffset` | **0 고정** | 직교의 clip z는 선형이라 0.05만 줘도 헐 전체가 깊이 테스트에서 탈락해 아웃라인이 사라진다. **원근에서 내부 크리스를 지우던 이 노브를 직교에서는 쓸 수 없다** → 대신 §6.4 스무스 노멀로 해결 |
| `_OutlineWidth` | **`C / orthographicSize`, C ≈ 35** (클램프 필요) | 폭은 화면 비율 고정이라 고정값을 쓰면 줌아웃 시 오브젝트를 삼킨다. size 70에서 0.5가 적정, size 300에서는 0.117로 줄여야 얇은 프린지가 된다 |
| `DR_OUTLINE_SMOOTH_NORMALS` | **on** | §6.4 |

**폭 드라이버**: 공유 머티리얼 3개의 `_OutlineWidth`를 카메라 `orthographicSize` 변화 시 한 번만 갱신하는 작은 컴포넌트를 둔다(모든 shell이 머티리얼을 공유하므로 `SetFloat` 3회로 전체 반영). 상·하한을 둬서 최소 가독 두께와 최대 두께를 보장한다. → 상수 C·클램프 최종값은 실기 튜닝(T8).

**측정 감각**: 타워는 화면에서 size 70일 때 약 100px, size 300일 때 약 20px 높이다. 즉 정밀한 실루엣보다 **가독성**이 기준이다.

### 3.3 색 — 임시 3종을 한 곳에서

`OutlineHighlight`가 **static 공유 머티리얼 3개**를 지연 생성한다(`Shader.Find("FlatKit/Stylized Surface")` — `RangeCircle`의 `Shader.Find` 선례와 동일하게 인스펙터 배선 없이 런타임 부착 대상에서도 동작해야 한다).

| 상태 | 임시 색 | 비고 |
|---|---|---|
| Hover | 노랑 | 아트 TBD |
| Selected / GroupSelected | 초록 | 아트 TBD |
| MergePreview | 핑크 | 아트 TBD |

색 상수는 `OutlineHighlight` 최상단 `static readonly Color` 3개로만 존재한다(교체 지점 1곳 = 완료 기준 충족). 아트가 확정되면 이 3줄 또는 §9 이행 후 머티리얼 에셋으로 승격한다.

> **주의**: static 공유 머티리얼은 도메인 리로드까지 살아 있다. 대상별 인스턴스가 아니므로 `OnDestroy`에서 파괴하지 **않는다**(다른 대상이 쓰고 있다). 반대로 향후 대상별 색 변형이 필요해지면 그때는 MPB로 덮되 머티리얼은 계속 공유한다.

---

## 4. 상태 모델 — 플래그 4개, 배타 토글 금지

```csharp
public enum OutlineKind { Hover, Selected, GroupSelected, MergePreview }
public void Set(OutlineKind kind, bool on);   // 멱등
```

- **독립 플래그**다. 토글식(`Toggle()`)이면 훅 호출이 비대칭일 때 상태가 어긋난다 → **`Set(kind, bool)` 멱등만 제공**한다.
- 최종 색 우선순위: **MergePreview > (Selected ‖ GroupSelected) > Hover**. 하나도 없으면 shell off.
  - 선택된 타워에 커서를 올려도 초록이 노랑으로 밀리지 않는다.
  - 후보 버튼 호버 시 핑크가 초록을 덮는다.
- `Selected`(MouseManager 단일 선택)와 `GroupSelected`(코디네이터 그룹)를 **분리한 이유**: 둘은 서로 다른 주체가 쓰고 수명이 다르다. 한 플래그를 공유하면 "Shift로 그룹에서 뺀 타워가 아직 MouseManager `_selected`인 경우" 한쪽이 다른 쪽 상태를 지운다.

---

## 5. 구동 배선

### 5.1 노랑·초록 — 드라이버 1개가 이벤트로 구동 (핵심 결정)

`OutlineInteractionDriver`(씬에 1개, MouseManager와 같은 오브젝트 권장)가 두 이벤트만 구독한다.

| 이벤트 | 시그니처 | 구동 |
|---|---|---|
| `MouseManager.OnHoverChanged` | `Action<IHoverable>` | 이전 대상 `Set(Hover,false)` → 새 대상 `Set(Hover,true)` |
| `MouseManager.OnSelectionChanged` | `Action<ISelectable>` | 이전 대상 `Set(Selected,false)` → 새 대상 `Set(Selected,true)` |

**왜 각 대상의 `ISelectable`/`IHoverable` 훅 안에서 직접 켜지 않는가**
1. 두 이벤트는 항상 "현재 대상"을 실어 오고 드라이버가 `_lastHovered`/`_lastSelected`를 들고 있으므로 **대칭이 구조적으로 보장**된다.
2. 그래서 **WL-087**(코디네이터 `RefreshPanel`이 `count==1`에서 `t.OnSelected()`만 부르고 대칭 `OnDeselected()`가 없음)에 걸리지 않는다. WL-087 자체는 정보 패널 쪽 버그로 **남는다**(이 이슈에서 고치지 않음). 2→1 복귀 시 남은 타워는 그룹에 있으므로 `GroupSelected`로 초록이 유지된다.
3. `Tower.cs`(Combat 소유)·`BuildingInfo`·`AuraTower`·`TerritoryNodeView`를 **한 줄도 수정하지 않는다**.
4. `MouseManager.Select`는 **낮/밤 게이트가 없다** → **밤에 타워를 클릭해도 초록이 뜬다**(사거리 원·정보 패널과 피드백이 일치). 코디네이터의 `IsDay` 게이트는 그대로 유지된다(밤에는 그룹·합성이 잠긴 채 단일 초록만 뜬다).

**타워 호버는 훅이 없다**: `Tower`는 `IAttacker`·`ISelectable`만 구현하므로 `hit.collider.TryGetComponent(out IHoverable)`가 잡지 못해 이벤트 자체가 오지 않는다 → `TowerGroupSelectable`이 `IHoverable`을 **추가 구현**한다(`GetTooltipContent()` → `null`, 계약상 "색만 바꾸는 호버 대상"으로 허용). 이 마커는 `TowerPlacer.cs:298`이 배치 시 `AddComponent`하므로 **합성 결과 타워까지 자동 부착**된다.

> ⚠️ **GO당 `IHoverable`은 하나만 잡힌다.** `TryGetComponent`는 부모 탐색도 하지 않는다. 훗날 타워 툴팁을 추가할 때 별도 컴포넌트를 만들면 **툴팁이나 아웃라인 중 하나가 조용히 죽는다** → 반드시 `TowerGroupSelectable`에 합쳐야 한다. 판정이 콜라이더와 같은 GO 기준이므로 마커는 계속 타워 **루트**에 붙인다.

### 5.2 그룹 초록 — 코디네이터 단일 경로

`TowerMergeCoordinator.RefreshHighlight()`가 이미 diff(`_highlighted` HashSet) + 파괴 참조 정리까지 하는 유일 경로다. 여기서 `IGroupSelectable.OnGroupSelected/OnGroupDeselected` → `TowerGroupSelectable` → `Set(GroupSelected, on)`.

평클릭도 `OnPrimarySelect → HandlePrimarySelect → TowerMergeGroup.SetSingle`로 **그룹 집합에 들어간다**(단일/다중 구분 없음) → 잔존 없음. 하늘색 쿼드(`k_HighlightColor`, `k_HighlightSize`, `CreateHighlight`)는 삭제한다.

### 5.3 핑크 — 합성 후보 버튼 호버

- **UI 측**: `TowerMergeCandidateHover`(`IPointerEnterHandler`/`IPointerExitHandler`, 선례 `Assets/Scripts/UI/TowerPanel/TowerTooltipSource.cs` — 월드 레이캐스트가 아니라 EventSystem 경로). `TowerMergePanelView.BuildCandidates()`가 레시피당 버튼 1개를 **`Awake`에서 미리 생성**하므로(`_candidates` 리스트, 매칭 시 `SetActive`) 그 자리에서 `captured` 레시피와 함께 배선한다.
- **뷰는 코디네이터 파사드만 호출한다**(현행 계약 유지) → 코디네이터에 `PreviewMerge(TowerRecipe)` / `ClearMergePreview()`를 추가하고, 월드 하이라이트 구동 권한은 계속 코디네이터가 단독으로 갖는다.
- **대상 판정은 재구현 금지**: `TowerFusionMatcher.TryResolve(towerIds, required, out consumeIndices)`를 재사용하고, 실행부 `TowerFusionController.TryFuse`와 **똑같이 `t == null || t.Asset == null`을 걸러낸 리스트**로 인덱스를 맞춘다(`TowerFusionController.cs:36` 참조). 포함 매칭이라 선택 집합에 여분 타워가 있을 수 있으므로 **여분은 초록 유지**.
- 코디네이터는 `_previewed` 집합으로 diff 관리(그룹 하이라이트와 동일 패턴).

**핑크 해제 트리거(전부 필요)**
| 트리거 | 이유 |
|---|---|
| `OnPointerExit` | 정상 경로 |
| `TowerMergeCandidateHover.OnDisable` | 버튼이 `SetActive(false)`로 꺼질 때 `OnPointerExit`가 오지 않을 수 있다(`Refresh`가 매칭 안 되는 버튼을 끈다) |
| 코디네이터 `HandleGroupChanged` | 선택 집합이 바뀌면 프리뷰 근거가 사라진다 |
| 코디네이터 `HandleDayToNight` | 밤 전환 시 집합 리셋과 함께 |
| 패널 루트 비활성화 | `RefreshPanel`이 2개 미만에서 `_mergePanel.SetActive(false)` |

### 5.4 영지 노드 — 섬/산만, 회오리는 제외

영지 노드는 상태에 따라 시각물이 교체된다(`TerritoryNodeStateVisual`): 확보 전 = `VortexVisual`이 런타임 생성한 **평면 Quad**(소용돌이), 확보 후 = 섬/산 프리팹 인스턴스. 평면 Quad에 인버티드 헐을 씌우면 소용돌이 모양이 아니라 **사각 테두리**가 나온다 → **회오리는 기존 `_vortexHoverColor` 하이라이트를 그대로 쓰고 아웃라인을 붙이지 않는다.**

대상이 상태에 따라 달라지므로 훅을 하나 둔다.

```csharp
public interface IOutlineTargetProvider { GameObject OutlineTarget { get; } }  // null = 아웃라인 없음
```

- `TerritoryNodeStateVisual`이 구현: 회오리 상태 → `null`, 확보 상태 → 섬 인스턴스 GO.
- 드라이버는 히트한 컴포넌트의 GO에서 이 인터페이스를 찾아 있으면 그 대상에, 없으면 히트 GO에 적용한다.
- `TerritoryNodeView.OnSelected`는 "즉시 확보(비가역)"를 오버로드하고 있어 **선택 상태가 유지되지 않는다** → 영지 노드는 **호버 노랑만**, 초록은 대상 외.

---

## 6. 렌더러 수집 정책

### 6.1 수집 규칙

1. `GetComponentsInChildren<MeshRenderer>(true)` + `GetComponentsInChildren<SkinnedMeshRenderer>(true)` — 타워·건물은 시각물이 자식에 있다(`TerritoryNodeView._visual` 분리 구조와 동일 계보). 타입을 이 둘로 한정하면 `LineRenderer`·`TrailRenderer`·`ParticleSystemRenderer`가 자동 배제된다.
2. **제외**: 조상에 `RangeCircle`이 있는 렌더러 — `Tower.cs:106`/`AuraTower.cs:127`이 사거리 원을 **타워 자식**(`"TowerRangeSelection"`)으로 생성하므로 제외하지 않으면 사거리 원판에 테두리가 생긴다. `RangeCircle`의 `Fill` 자식이 MeshRenderer라 타입 필터로는 걸러지지 않는다.
3. **제외**: 이미 만든 `OutlineShell` 자신(재수집 시 무한 증식 방지).
4. 수집은 **첫 표시 시 1회**, 결과를 캐시한다. 사거리 원은 첫 선택 때 지연 생성되므로 수집 시점이 그 전/후 어느 쪽이든 조상 검사로 안전해야 한다.

### 6.2 렌더러가 많은 대상 — 프록시 폴백

`Castle.prefab`은 루트에 `BoxCollider` + `BuildingInfo` + `BuildingTooltipSource`가 붙은 정상 호버 대상인데 내용물이 Sweet_Land 소품 조립이라 **462 렌더러**(Terrace 128, TerraceRail 130, Balustrade 48, MainHall_L1 54 …)다. 자식을 전부 씌우면 커서를 올리는 동안 +462 드로우 + 스킨드 21개 이중 스키닝 → 못 쓴다.

**정책**: 수집 결과가 상한(**초안 16개**, 인스펙터 노출)을 넘으면 자동 수집을 버리고 **프록시 shell 1개**로 폴백한다 + `Debug.LogWarning`으로 대상 이름과 렌더러 수를 남긴다(조용한 품질 저하 금지).

- 프록시 형상 1순위: 루트 `Collider`(주로 `BoxCollider`) 기반 박스 실루엣 → 1 드로우. 정확한 실루엣은 아니지만 "이 건물이 지금 대상"은 충분히 읽힌다. 구현은 **정점 위치가 공유된 유닛 큐브 메시 1개를 static으로 만들고**(uv3에 평균 노멀을 코드로 채워 §6.4의 스무스 노멀 요건을 처음부터 충족) shell 트랜스폼의 스케일로 콜라이더 bounds에 맞춘다 → 대상마다 메시를 만들지 않으므로 파괴 대상이 늘지 않는다.
- 프록시 형상 2순위(후속): 아트가 만든 저폴리 실루엣 메시를 `OutlineHighlight`에 직접 지정 → #148/#138에서 개선.

### 6.3 SkinnedMeshRenderer 처리

영지 산(`Mountain_01~06`)은 `SkinnedMeshRenderer` + `BlendShapeAnimator`(`Assets/Imported/Sweet_Land/Scripts/BlendShapeAnimator.cs`)로 블렌드셰이프가 애니메이트된다.

- shell도 `SkinnedMeshRenderer`로 만들고 `sharedMesh`·`bones`·`rootBone`을 **원본과 공유**한다.
- 블렌드셰이프 가중치는 원본이 매 프레임 바뀌므로, **shell이 켜져 있는 동안만** 원본 → shell로 가중치를 복사한다(꺼져 있으면 아무 일도 하지 않는다). 복사를 빠뜨리면 산 모양이 어긋난 아웃라인이 보인다.
- 스키닝이 두 번 돌지만 동시 대상이 1~2개라 무해하다.
- 산의 shell 메시는 §6.4의 스무스 사본을 쓰되 **블렌드셰이프가 보존된 사본**이어야 한다(`Object.Instantiate(mesh)`는 블렌드셰이프를 복사한다).

### 6.4 스무스 노멀 프리베이크 (필수)

**왜 필요한가**: 대상 모델이 전부 하드(스플릿) 노멀 로우폴리다. 인버티드 헐을 그대로 씌우면 헐이 면 단위로 찢어져 게임 줌에서 **점선처럼 끊긴 프린지**가 된다(스파이크 확인). 원근에서는 `_OutlineDepthOffset`으로 가릴 수 있었지만 직교에서는 그 노브를 못 쓴다(§3.4). FlatKit의 해법이자 유일하게 깨끗한 결과를 낸 방법은 **정점 위치를 공유하는 노멀을 평균해 uv3(TEXCOORD2)에 넣고 `DR_OUTLINE_SMOOTH_NORMALS`를 켜는 것**이다.

**왜 런타임 스무딩이 아니라 프리베이크인가**

| 사실 | 근거(실측) |
|---|---|
| FlatKit `MeshSmoother`는 런타임에서 못 쓴다 | `Assets/Imported/FlatKit/Utils/FlatKit.Utils.Editor.asmdef` → `includePlatforms: ["Editor"]` |
| 대상 메시 대부분이 런타임에서 읽히지 않는다 | `isReadable`: ArcherTower 1/1 true, **SodaTower 0/2, RollyShooter 0/2, Mine 0/44, Mountain_01 0/1** → 런타임 `mesh.vertices` 접근 불가 |
| 그러나 **에디터에서는 읽힌다** | `isReadable == false`인 `CandyTower_01`(9867 verts, 서브메시 2)·`TowerCap_01`에서 에디터 `mesh.vertices` 접근 성공 |
| 서브메시 개수는 우리 베이커에선 제약이 아니다 | FlatKit 인스펙터 UI만 멀티 서브메시를 거부한다. uv3만 채우면 되므로 서브메시 수와 무관(실제로 2-서브메시 메시에서 스무딩·렌더 성공) |

**절차**
1. 에디터 메뉴(`OutlineSmoothMeshBaker`)로 대상 프리팹 트리의 메시를 수집한다.
2. 메시별로 `Object.Instantiate` → `FlatKit.MeshSmoother.SmoothNormals(clone)`(에디터 asmdef 참조 가능) → `Assets/Imported/@NorthLand/Meshes/OutlineSmooth/<name>_smooth.asset`으로 저장. **벤더 트리(`Sweet_Land`, `TARBO`)는 건드리지 않는다.**
3. 원본 메시 → 스무스 사본 매핑을 `OutlineSmoothMeshRegistry`(SO)에 기록. shell 생성 시 이 레지스트리로 조회하고, **없으면 원본 메시로 폴백**(찢어진 아웃라인이지만 동작은 한다) + 1회 경고 로그.
4. 대상 규모: 타워 메시 + 산 메시 기준 **15~20개** 예상. 건물은 프록시(§6.2)를 쓰므로 베이크 대상이 아니다(프록시 메시는 우리가 생성하므로 스무스 노멀을 처음부터 채워 만든다).

**#148 재사용**: 툰 룩에서 아웃라인을 상시로 켜면 같은 스무스 노멀 문제를 그대로 만난다 → 이 베이커·레지스트리는 #148에서 그대로 쓰인다(§9).

---

## 7. 레이어 / 렌더링 설정

| 항목 | 값 | 상태 / 비고 |
|---|---|---|
| 신규 레이어 | `OutlineShell` = **12** | **적용 완료**. 기존: 0 Default, 1 TransparentFX, 2 Ignore Raycast, 3 Ground, 4 Water, 5 UI, 6 Selectable, 7 Enemy, 8 Soldier(병사 리젝 잔재), 9 PlayerBase, 11 MinimapOverlay (10·13+ 비어 있음) |
| 렌더러 피처 | `Flat Kit Per Object Outline`(`ObjectOutlineRendererFeature`) | **적용 완료** — `PC_Renderer.asset`(SSAO와 공존, **Deferred** `m_RenderingMode: 2`) · `Mobile_Renderer.asset`(Forward) 양쪽. `Event: 300`, `overrideShader` = FlatKit/Stylized Surface, `overrideShaderPassIndex: 1`, `PassNames: [Outline]` |
| 피처 Layer Mask | `OutlineShell`만 (`m_Bits: 4096`) | **적용 완료** — 상시 비용을 활성 shell 수로 한정 |
| `autoReferenceMaterials` | **0(off)** | 켜두면 FlatKit 머티리얼의 아웃라인 토글을 끌 때 이 피처가 자동 삭제될 수 있다 → 우리 shell 머티리얼은 코드가 관리하므로 끈다 |
| Opaque/Transparent Layer Mask | `OutlineShell` 제외 (`m_Bits: 4294963199`) | **적용 완료** — shell 본체 패스 억제(§3.2에서 검증) |
| 카메라 컬링 마스크 | 변경 없음(`-1`) | cullResults에 남아야 피처가 그릴 수 있다 |

> **미니맵 주의**: 씬에 `MinMapCamera`(cullingMask `-1`, depth 0)와 `Main Camera`(`-1`, depth -1)가 있다. 그대로 두면 **미니맵에도 아웃라인이 나온다.** 미니맵에 표시할지 결정하고, 표시하지 않을 거면 `MinMapCamera`의 컬링 마스크에서 `OutlineShell`을 제외한다. → **TODO(구현 시 결정)**

---

## 8. 수명주기 · 잔존 방지 체크리스트

| 상황 | 처리 |
|---|---|
| 배치 모드·스킬 조준 진입 | `MouseManager.BeginPlacement`/`BeginSkillTargeting`이 `ClearHover()`를 호출 → `OnHoverChanged(null)` → 드라이버가 노랑 해제. **모드 전환 순간 노란 아웃라인이 남지 않는지 확인 필요** |
| 배치·조준 취소 | `CancelPlacement`/`CancelSkillTargeting`은 `_mode = Idle`만 되돌린다 → 다음 `UpdateHover`에서 자연 복구 |
| Esc / 빈 곳 클릭 | `Select(null)` + `OnPrimarySelect(null)` → 단일 초록·그룹 초록 동시 해제 |
| Shift로 그룹에서 제거 | 코디네이터 `RefreshHighlight` diff가 `OnGroupDeselected` → 즉시 해제(WL-087 표면 재발 없음) |
| 밤 전환 | 코디네이터 `HandleDayToNight`가 집합 리셋 → 그룹 초록·핑크 해제. 단일 초록은 `MouseManager` 선택이 유지되는 동안 남는다(§5.1 의도된 동작) |
| 합성 소모·철거·사망 | 타워 GO 파괴 → shell은 자식이라 함께 파괴. 코디네이터는 `Tower.ActiveChanged` → `Prune`(WL-076b)로 죽은 참조 정리 |
| 컴포넌트 파괴 | 런타임 생성물 중 **대상별로 파괴할 것이 없다** — shell 메시는 원본/프리베이크 에셋 공유, 프록시는 static 유닛 큐브 공유, 머티리얼 3개도 static 공유다. shell GO는 자식이라 자동 파괴. → `RangeCircle`(PR#115 리뷰)처럼 `OnDestroy`에서 Mesh/Material을 파괴할 필요가 **없는 이유**를 주석으로 명시한다. 단 static 공유물(머티리얼 3개·유닛 큐브)은 도메인 리로드까지 유지되므로 **대상별 인스턴스를 만들지 않는 규칙을 깨지 말 것** |

---

## 9. #148 이후 이행 경로 (shell 걷어내기)

#148에서 전체 머티리얼이 FlatKit Stylized Surface로 바뀌고 툰 아웃라인이 **상시** 켜지면, 아웃라인 패스 비용은 아트가 지불하는 것이 된다. 그 시점의 상호작용 하이라이트는 **"이미 그려지는 아웃라인의 색만 덮어쓰기"** 가 되어 추가 드로우가 0이다.

| 구성 요소 | #148 이후 |
|---|---|
| `OutlineHighlight` 공개 API(`Set(kind,bool)`, 우선순위) | **그대로** |
| `OutlineInteractionDriver`, 훅 배선, 핑크 프리뷰 로직, 영지 `IOutlineTargetProvider` | **그대로** (작업량의 대부분) |
| `OutlineHighlight` 내부의 shell 생성·수집·프록시·스킨드 동기화 | **삭제**, 대상 렌더러에 `MaterialPropertyBlock`으로 `_OutlineColor`(+ 필요 시 `_OutlineWidth`) 덮어쓰기로 교체 (약 40~60줄) |
| `OutlineShell` 레이어, 피처의 Layer Mask, Opaque/Transparent 제외 | 재검토(피처 마스크가 아트용으로 넓어짐) |
| §2.3의 데모 머티리얼 참조(`WaterScene_*`) | #148에서 정리 |

즉 **교체 지점을 `OutlineHighlight` 내부 한 곳으로 고정**하는 것이 이 설계의 목적 중 하나다. 훅·이벤트·프리뷰 계약은 렌더링 방식과 독립이다.

---

## 10. 검증 스파이크 결과 (2026-07-27, GameScene 편집 모드)

`unity-cli exec`로 임시 타워(`ArcherTower`, `SodaTower`)를 세우고 shell을 붙여 씬 뷰 캡처로 확인했다. 스파이크 오브젝트·임시 머티리얼·스무스 사본은 모두 파괴하고 씬은 저장하지 않았다(잔재 0, 콘솔 에러 0).

| # | 항목 | 결과 |
|---|---|---|
| 1 | `OutlineShell` 레이어(12) 신설 + 피처를 PC/Mobile에 추가 | ✅ 적용, 커밋 대상 |
| 2 | `Opaque/Transparent Layer Mask` 제외만으로 shell 본체 패스 억제 | ✅ **z-파이팅 없음 → 알파 클립 불필요(T1 종결)** |
| 3 | PC(**Deferred**) 렌더러에서 아웃라인 렌더 | ✅ 확인 |
| 3b | Mobile(Forward) 렌더러에서 아웃라인 렌더 | ⚠️ **미확인** — 품질 레벨 전환이 필요해 보류(T9) |
| 4 | 레이캐스트 비간섭 | ✅ 구조적 보장(shell에 콜라이더를 만들지 않음) |
| 5 | 직교 카메라 파라미터 확정 | ✅ §3.4 표 — `impact=1`, `depthOffset=0`, `width = 35/orthoSize` |
| 6 | 하드 노멀 아웃라인 품질 | ❌ 게임 줌에서 점선 프린지 → **스무스 노멀 필수(§6.4)** |
| 7 | 스무스 노멀 적용 후 품질 | ✅ size 70에서 연속된 테두리로 읽힘(폭 0.5). size 300에서는 폭 0.117로 스케일해야 얇은 프린지 유지 |
| 8 | 에디터에서 `isReadable == false` 메시 읽기 | ✅ 가능 → 프리베이크 경로 성립(§6.4) |
| 9 | `MinMapCamera` 아웃라인 노출 | ⚠️ 미확인, 컬링 마스크가 `-1`이라 나올 것으로 예상(T2) |

**부수 발견**: `AssetDatabase.SaveAssets()`가 무관한 더티 에셋(JP 폰트 아틀라스, 미니맵 RenderTexture)까지 저장한다 → 스파이크 후 `git checkout`으로 되돌렸다. 또 URP 렌더러 에셋을 에디터가 저장하면 `m_AssetVersion 2→3` 포맷 마이그레이션(신규 필드 `m_PrepassLayerMask`·`xrSystemData`·`m_DepthAttachmentFormat` 등)이 함께 기록된다 — 불가피하므로 이 브랜치에 포함한다.

검증 명령 규약은 `Docs/Tools/unity-cli-guide.md`를 따른다(`.cs` 수정 후 `editor refresh --compile` → `console --type error` 0건, 에셋 텍스트 편집 후 `reserialize`). 씬 뷰 캡처는 `sv.pivot/rotation/size` 설정 후 **`sv.Focus()`** 를 호출해야 카메라가 실제로 갱신된다(백그라운드 에디터에서는 `Repaint()`만으로는 이전 프레임이 찍힌다 — 게임 뷰 캡처는 편집 모드에서 갱신되지 않아 판단 근거로 쓰지 말 것).

---

## 11. 완료 기준 (이슈 #213 체크리스트 매핑)

- [ ] 호버 대상(건물·배치된 타워·확보된 섬/산)에 커서를 올리면 노란 아웃라인 on, 벗어나면 off → §5.1, §5.4
- [ ] `ISelectable` 클릭 시 초록 on, 다른 대상·빈 곳·Esc에서 off → §5.1
- [ ] Shift 다중 선택도 같은 초록, 다시 Shift로 빼면 즉시 off(WL-087 표면 재발 없음) → §5.2
- [ ] 선택된(초록) 대상에 호버해도 노랑으로 밀리지 않음 → §4
- [ ] 후보 버튼 호버 시 **소모될 재료만** 핑크, 여분은 초록 유지 → §5.3
- [ ] 버튼에서 벗어나거나 패널이 닫히거나 집합이 바뀌면 핑크 잔존 없음 → §5.3 표
- [ ] 합성 소모·철거된 타워의 아웃라인이 월드에 남지 않음 → §8
- [ ] 아웃라인이 클릭/호버 레이캐스트를 막지 않음 → §3.1, §10-4
- [ ] `TowerGroupSelectable`의 하늘색 바닥 쿼드가 아웃라인으로 대체됨 → §5.2
- [ ] 임시 색 3종을 한 곳에서 변경 가능 → §3.3
- [ ] PC/Mobile URP 양쪽에서 보임 → §7, §10-3
- [ ] 런타임 생성물 누수 없음(이 설계에서는 파괴 대상이 없음을 주석으로 명시) → §8
- [ ] `Docs/Core/MouseManager.md` §8 · `Docs/Core/TowerMerge.md` §8.4 갱신 + `Docs/Review/SystemMap.md` 반영
- [ ] #138(건물 시인성) 범위 정리 — 이 이슈의 호버 노랑이 #138 후보 중 하나를 실질적으로 구현한다. #138을 닫거나 "버튼 UI"로 좁힌다

---

## 12. 미확정 / TODO

| # | 항목 | 결정권 |
|---|---|---|
| ~~T1~~ | ~~shell 본체 패스 억제 방식~~ | **종결** — Layer Mask 제외로 확정(§10-2) |
| T2 | 미니맵(`MinMapCamera`)에 아웃라인 표시 여부 | 기획/아트 (미확인, 컬링 마스크 `-1`) |
| T3 | 렌더러 수집 상한 값(초안 16)과 프록시 실루엣 품질 수용선 | 구현 중 실측 → 아트 |
| T4 | 임시 3색의 최종 색·선 굵기 | 아트 TBD |
| T5 | 큰 건물 저폴리 실루엣 프록시 메시 제작 | #148/#138 |
| T6 | 관통(엑스레이) 표시 필요 여부 | 기획 |
| T7 | `Soldier` 레이어(8) 등 리젝된 시스템 잔재 정리 | 별건 |
| T8 | 폭 드라이버 상수(C ≈ 35)와 상·하한 클램프 최종값 | 실기 튜닝 |
| T9 | **Mobile(Forward) 렌더러 시각 검증** — 품질 레벨 전환 후 캡처 | 구현 중 필수 |
| T10 | 스무스 사본이 없는 메시의 폴백 정책(원본 사용 + 경고 vs 아웃라인 생략) | 구현 중 |

---

## 13. 변경 이력

| 날짜 | 내용 |
|---|---|
| 2026-07-27 | 최초 작성 — #213 착수 전 조사·결정 기록(shell 방식 확정, 컨버전은 #148로 분리). 구현 미착수 |
| 2026-07-27 | 렌더 경로 스파이크 완료 — 레이어(12)·렌더러 피처 PC/Mobile 적용, T1 종결(Layer Mask 제외), 직교 파라미터 확정(§3.4), 스무스 노멀 프리베이크 필수 판정(§6.4). Mobile 시각 검증(T9)·미니맵(T2) 미확인 |
