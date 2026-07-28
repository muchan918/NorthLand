# 상호작용 아웃라인(Interaction Outline) — 설계 명세

> **상태**: **호버 노랑 / 선택·그룹 초록 / 합성 프리뷰 핑크 + 영지 노드 대상(§5.4) 구현 완료**(§11 체크리스트). 남은 것은 **Mobile 렌더러 시각 검증(T9)**, **미니맵 노출 여부(T2)**, 그리고 `MouseManager.md`·`TowerMerge.md`·SystemMap 갱신이다. 색·선 굵기는 전부 **임시(아트 TBD)**.
> **소유**: n0wst4ndup(#213)
> **이슈**: #213 [Feature] 상호작용 아웃라인 — 호버=노란색 / 선택=초록색(그룹 포함) / 합성 후보 버튼 호버 시 재료 타워만 핑크색
> **구현 파일**:
> - `Assets/Scripts/GameManager/MouseManager/Highlight/OutlineHighlight.cs` — (**구현 완료**) 아웃라인 표시 컴포넌트, 상태 플래그·색 우선순위·shell 생성
> - `Assets/Scripts/GameManager/MouseManager/Highlight/OutlineInteractionDriver.cs` — (**구현 완료**) MouseManager 이벤트 구독 → 호버 노랑·단일 선택 초록 구동 + 줌 대응 폭 갱신
> - `Assets/Scripts/GameManager/MouseManager/Highlight/IOutlineTargetProvider.cs` — (**구현 완료**) 아웃라인 대상 GO를 대신 지정하는 훅(구현체: `TerritoryNodeView`)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerGroupSelectable.cs` — (**수정 완료**) 하늘색 쿼드 제거, `IHoverable` 추가 구현, 그룹 초록
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerMergeCoordinator.cs` — (**수정 완료**) `PreviewMerge`/`ClearMergePreview` 추가
> - `Assets/Scripts/UI/TowerPanel/TowerMergeCandidateHover.cs` — (**구현 완료**) 후보 버튼 EventSystem 호버 → 코디네이터 프리뷰 호출
> - `Assets/Scripts/UI/TowerPanel/TowerMergePanelView.cs` — (**수정 완료**) `BuildCandidates`에서 위 컴포넌트 런타임 배선
> - `Assets/Scripts/GameManager/MouseManager/MouseManager.cs` · `Assets/Scripts/CombatSystem/Tower/Tower.cs` — (**수정 완료**) 파괴된 선택/호버 대상 통지 방어(§8, WL-033 계열 버그 수정)
> - `Assets/Scripts/ManagementSpace/Territory/View/TerritoryNodeView.cs` — (**수정 완료**) `IOutlineTargetProvider` 구현 — 판단은 상태 비주얼에 위임(§5.4)
> - `Assets/Scripts/ManagementSpace/Territory/View/TerritoryNodeStateVisual.cs` — (**수정 완료**) `OutlineTarget` 공개(회오리·본진=null, 섬/산=인스턴스)
> - `Assets/Scripts/Editor/OutlineSmoothMeshBaker.cs` — (**구현 완료**, 에디터) 대상 메시의 스무스 노멀 사본을 에셋으로 굽는 메뉴(§6.4)
> - `Assets/Scripts/GameManager/MouseManager/Highlight/OutlineSmoothMeshRegistry.cs` — (**구현 완료**) 원본 메시 → 스무스 사본 매핑 SO(런타임 부착 컴포넌트가 인스펙터 배선 없이 `Resources.Load`로 조회)
> - `Assets/Resources/Outline/OutlineSmoothMeshRegistry.asset` + `Assets/Meshes/OutlineSmooth/*.asset`(13개) — (**생성 완료**) 베이크 산출물
> - `Assets/Settings/PC_Renderer.asset` · `Assets/Settings/Mobile_Renderer.asset` — (**적용 완료**) 아웃라인 렌더러 피처 추가 + `OutlineShell`을 **Opaque·Transparent·Prepass 세 마스크 모두에서** 제외, PC/Mobile 동일
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

### 2.5 PR 리뷰 지적 중 **유지**로 결정한 2건 (2026-07-27, 소유자 판단)

리뷰(PR#218)가 🔴/🟠로 올린 두 항목은 **바꾸지 않는다**. 근거와 해소 조건을 여기 남긴다 — 나중에 같은 지적이 반복되면 이 절을 먼저 읽고 판단할 것.

**(1) 베이커의 벤더 네임스페이스 직접 참조 — 유지, 이동으로 해소 예정**

- 지적: `Assets/Scripts/Editor/OutlineSmoothMeshBaker.cs`가 `FlatKit.MeshSmoother`를 컴파일 타임 직접 참조한다 → `Assets/Imported/`(`.gitignore:162`) 미동기화 팀원에서 Assembly-CSharp-Editor 컴파일 실패(WL-062 계열).
- 결정: **스무딩 로직을 자체 구현하지 않고 참조를 유지한다.** 대신 비주얼 작업(#148) 때 이 베이커를 **`Assets/Imported/@NorthLand`(아트 저장소) 안으로 옮긴다** — 옮기고 나면 참조하는 쪽과 참조되는 쪽이 같은 트리에 있게 되므로, 벤더 트리가 없는 환경에는 **베이커 파일 자체가 없어** 컴파일이 깨질 대상이 사라진다. 즉 "덕타이핑으로 감추기"가 아니라 **거주지 정리로 원인을 없애는** 경로다.
- 그때까지의 실제 위험: 베이커는 **에디터 전용 1회성 툴**이고 산출물(`Assets/Meshes/OutlineSmooth/*` + 레지스트리)은 이미 프로젝트 저장소에 커밋돼 있다 → 벤더 트리가 없는 팀원도 **아웃라인 표시 자체는 정상 동작**한다(런타임 경로에 벤더 참조 없음).
- 해소 조건: 베이커가 `Assets/Imported/@NorthLand` 아래로 이동하면 이 항목 종결. 이동 전까지 `Assets/Scripts/` 안에 **새로운** 벤더 참조를 추가하지는 않는다.

**(2) 호버 노랑 vs "보유 영토 노랑"(GDD §6.3) 색 충돌 — 충돌 아님, 노랑 유지**

- 지적: `Docs/GDD.md:165`와 `TerritoryNodeView._ownedColor`(1, 0.85, 0.2)가 노랑을 '보유 영토'에 배정했는데 호버색도 노랑이라 §5.4 착수 시 구분이 사라진다.
- 결정: **충돌하지 않는다.** 노드의 "노란 구체"는 **구형 색상 경로**(`TerritoryNodeView._visual`)의 표현이고, 현행 신형 프리팹(`TerritoryNodeV2`)은 `TerritoryNodeStateVisual`이 그 View를 **가로채** 상태별 시각물(회오리 / 섬·산 프리팹)로 갈아끼운다 — 즉 **보유 상태를 노란색으로 알리지 않는다**(보유 = 섬이 솟아 있음). `_ownedColor`/`_hoverColor`는 상태 비주얼이 없는 구형 프리팹에서만 쓰인다.
- 따라서 상호작용 색 언어는 아래 배정을 그대로 유지한다. 최종 색은 여전히 아트 TBD(T4)이고, 그때 재검토한다.

| 색 | 의미 | 소유 |
|---|---|---|
| 노랑 | **호버**(지금 커서가 있음) | #213 |
| 초록 | **선택**(단일·그룹) — `Tower.selectionRangeColor` 사거리 원과 정합 | #213 |
| 핑크 | **합성 시 소모 예정** | #213 |
| (색 없음) | 영지 회오리 = 자체 틴트·회전·스케일 펄스로 호버 표현(§5.4) | #93 |

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
- **`Prepass Layer Mask`(URP 17 신규 필드)도 반드시 같이 제외한다** — 같은 값 `4294963199`. 이 마스크는 depth/normals 프리패스의 필터라 마스크 기본값(전 레이어)으로 두면 아웃라인 패스와 무관하게 **shell이 원본과 같은 위치로 깊이를 쓴다.** 원본이 불투명·닫힌 메시일 때는 자기 깊이와 같아 증상이 안 보이지만, **원본이 반투명 평면(영지 회오리 Quad)이면 그 뒤의 바다가 깊이 테스트에서 탈락해 호버 순간 회오리가 흰 사각형으로 변한다.** 실측 A/B는 §10.1. `m_AssetVersion 2→3` 마이그레이션으로 이 필드가 생겼기 때문에, **URP 렌더러 에셋 포맷이 올라갈 때는 새 레이어 마스크 필드가 추가됐는지 확인해야 한다**(세 마스크를 같은 값으로 유지).
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

`OutlineHighlight`가 **static 공유 머티리얼을 [색 3종 × 스무스 노멀 여부 2] = 최대 6개** 지연 생성한다(`Shader.Find("FlatKit/Stylized Surface")` — `RangeCircle`의 `Shader.Find` 선례와 동일하게 인스펙터 배선 없이 런타임 부착 대상에서도 동작해야 한다).

`uv3`가 없는 메시에 `DR_OUTLINE_SMOOTH_NORMALS`를 켜면 셰이더가 노멀을 0으로 읽어 **아웃라인이 깨진다** → shell 생성 시 `mesh.HasVertexAttribute(TexCoord2)`로 판정해 키워드를 켠/끈 변형을 골라 쓴다. 스무스 사본이 아직 베이크되지 않은 메시(§6.4 폴백)도 이 경로로 안전하게 표시된다.

| 상태 | 임시 색 | 비고 |
|---|---|---|
| Hover | 노랑 | 아트 TBD |
| Selected / GroupSelected | 초록 | 아트 TBD |
| MergePreview | 핑크 | 아트 TBD |

색 상수는 `OutlineHighlight` 최상단 `static readonly Color` 3개로만 존재한다(교체 지점 1곳 = 완료 기준 충족). 아트가 확정되면 이 3줄 또는 §9 이행 후 머티리얼 에셋으로 승격한다.

> **주의**: static 공유 머티리얼은 도메인 리로드까지 살아 있다. 대상별 인스턴스가 아니므로 `OnDestroy`에서 파괴하지 **않는다**(다른 대상이 쓰고 있다). 반대로 향후 대상별 색 변형이 필요해지면 그때는 MPB로 덮되 머티리얼은 계속 공유한다.

**빌드 시 셰이더 변종 스트리핑은 이 경로에서 문제되지 않는다**(리뷰 PR#218 🟠 확인 결과). 아웃라인 패스의 키워드 선언이 `#pragma multi_compile _ DR_OUTLINE_ON` / `#pragma multi_compile _ DR_OUTLINE_SMOOTH_NORMALS`(`Assets/Imported/FlatKit/Shaders/StylizedSurface/StylizedSurface.shader`, pass `Outline`)라 **`shader_feature`가 아니라 `multi_compile`** 이다 — 머티리얼 에셋에 그 키워드 조합이 없어도 변종이 빌드에 남는다. 셰이더 자체는 렌더러 피처의 `overrideShader` 참조로 포함된다. 단 **플레이어 빌드 1회 확인은 여전히 남은 검증**(T9와 함께)이고, 프로젝트에 셰이더 스트리핑 콜백을 도입하면 이 전제가 깨진다.

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
- `Selected`(MouseManager 단일 선택)와 `GroupSelected`(코디네이터 그룹)를 **분리한 이유**: 둘은 서로 다른 주체가 쓰고 수명이 다르다. 한 플래그를 공유하면 한쪽이 다른 쪽 상태를 지운다. WL-087 수정으로 Shift 클릭이 `_selected`를 비우게 된 뒤에도 분리는 그대로 값을 한다 — 그때 드라이버가 `Set(Selected,false)`를 걸지만 코디네이터가 켜 둔 `GroupSelected`는 살아 있어 **초록이 끊기지 않는다**(공유 플래그였다면 Shift로 담는 순간 재료 타워의 초록이 꺼졌을 것).

---

## 5. 구동 배선

### 5.1 노랑·초록 — 드라이버 1개가 이벤트로 구동 (핵심 결정)

`OutlineInteractionDriver`가 두 이벤트만 구독한다. **씬 파일을 건드리지 않도록 `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`로 스스로 부팅해 `DontDestroyOnLoad`로 남는다** — 정본 씬/개인 복사 병합 규칙(`Docs/Core/SceneWorkflow.md`)과 충돌을 피하기 위한 선택이고, `TowerGroupSelectable`이 런타임 부착인 것과 같은 계보다. 대가로 튜닝값(폭 계수·클램프)은 인스펙터가 아니라 코드 상수다(`RangeCircle` 선례). MouseManager가 늦게 등장하는 씬도 있어 구독은 붙을 때까지 매 프레임 확인하고, 씬 전환 시 참조를 비운다(WL-033).

| 이벤트 | 시그니처 | 구동 |
|---|---|---|
| `MouseManager.OnHoverChanged` | `Action<IHoverable>` | 이전 대상 `Set(Hover,false)` → 새 대상 `Set(Hover,true)` |
| `MouseManager.OnSelectionChanged` | `Action<ISelectable>` | 이전 대상 `Set(Selected,false)` → 새 대상 `Set(Selected,true)` |

**왜 각 대상의 `ISelectable`/`IHoverable` 훅 안에서 직접 켜지 않는가**
1. 두 이벤트는 항상 "현재 대상"을 실어 오고 드라이버가 `_lastHovered`/`_lastSelected`를 들고 있으므로 **대칭이 구조적으로 보장**된다.
2. 그래서 **WL-087**(코디네이터 `RefreshPanel`이 `count==1`에서 `t.OnSelected()`만 부르고 대칭 `OnDeselected()`가 없음)에 걸리지 않는다. 2→1 복귀 시 남은 타워는 그룹에 있으므로 `GroupSelected`로 초록이 유지된다. *(WL-087 자체는 이 이슈에서 고치지 않은 채 남았다가, 사거리 원 잔존으로 드러나 `_infoShownFor` diff + Shift 경로의 `Select(null)`로 종결 — `TowerMerge.md` §7.2·§8.1.)*
3. `Tower.cs`(Combat 소유)·`BuildingInfo`·`AuraTower`·`TerritoryNodeView`를 **한 줄도 수정하지 않는다**.
4. `MouseManager.Select`는 **낮/밤 게이트가 없다** → **밤에 타워를 클릭해도 초록이 뜬다**(사거리 원·정보 패널과 피드백이 일치). 코디네이터의 `IsDay` 게이트는 그대로 유지된다(밤에는 그룹·합성이 잠긴 채 단일 초록만 뜬다).

**타워 호버는 훅이 없다**: `Tower`는 `IAttacker`·`ISelectable`만 구현하므로 `hit.collider.TryGetComponent(out IHoverable)`가 잡지 못해 이벤트 자체가 오지 않는다 → `TowerGroupSelectable`이 `IHoverable`을 **추가 구현**한다(`GetTooltipContent()` → `null`, 계약상 "색만 바꾸는 호버 대상"으로 허용). 이 마커는 `TowerPlacer.cs:298`이 배치 시 `AddComponent`하므로 **합성 결과 타워까지 자동 부착**된다.

> ⚠️ **GO당 `IHoverable`은 하나만 잡힌다.** `TryGetComponent`는 부모 탐색도 하지 않는다. 훗날 타워 툴팁을 추가할 때 별도 컴포넌트를 만들면 **툴팁이나 아웃라인 중 하나가 조용히 죽는다** → 반드시 `TowerGroupSelectable`에 합쳐야 한다. 판정이 콜라이더와 같은 GO 기준이므로 마커는 계속 타워 **루트**에 붙인다.

### 5.2 그룹 초록 — 코디네이터 단일 경로

`TowerMergeCoordinator.RefreshHighlight()`가 이미 diff(`_highlighted` HashSet) + 파괴 참조 정리까지 하는 유일 경로다. 여기서 `IGroupSelectable.OnGroupSelected/OnGroupDeselected` → `TowerGroupSelectable` → `Set(GroupSelected, on)`.

평클릭도 `OnPrimarySelect → HandlePrimarySelect → TowerMergeGroup.SetSingle`로 **그룹 집합에 들어간다**(단일/다중 구분 없음) → 잔존 없음. 하늘색 쿼드(`k_HighlightColor`, `k_HighlightSize`, `CreateHighlight`)는 삭제한다.

### 5.3 핑크 — 합성 후보 버튼 호버 + 배치 확정 대기

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

**단, 버튼을 클릭한 뒤에는 위 트리거가 전부 막힌다 — `_previewCommitted` 잠금**

호버 프리뷰만 있으면 **클릭하는 순간 커서가 버튼을 벗어나며 `OnPointerExit`가 즉시 핑크를 걷어가고**, 재료가 초록으로 되돌아간 채 결과 타워 고스트를 들고 다니게 된다. 정작 "이 타워들이 지금 소모될 예정"이라는 정보가 가장 필요한 구간이 배치 중이므로, 클릭 시점의 소모 대상을 **배치 세션이 끝날 때까지 고정**한다.

- 코디네이터 `RequestMerge`가 `TowerFusionController.TryFuse(recipe, group, onEnded)`의 **반환값이 true(배치가 실제로 시작됨)일 때만** 칠하고 `_previewCommitted = true`. 재료·코스트 부족으로 반려되면 종료 콜백도 오지 않으므로 잠그면 안 된다.
- 잠금 중에는 `PreviewMerge`/`ClearMergePreview`가 no-op — 배치 중 다른 후보 버튼에 커서가 스쳐도 재료 표시가 흔들리지 않는다.
- 유일한 해제 경로는 `onEnded` → `EndMergeCommit`. 신호는 `TowerPlacer.EndPlacement`(= `PlacementRequest.OnEnded`)에서 오며 **확정·취소·다른 배치로 교체 전부**를 덮는다. 확정이면 재료가 `Destroy`되고, 취소면 재료가 남되 **선택 집합은 이미 비어 있으므로 아무 아웃라인도 없는 상태로** 돌아간다(배치 시작 시 전체 해제 — 위 §5.1 표).
- **호출 순서가 계약이다**: `ResolveConsumeTargets`(판정) → `TryFuse` → (성공 시) `ApplyPreview` → 잠금. 가운데 낀 `TryFuse`가 `BeginPlacement`를 부르고, 그게 **① 전체 해제로 선택 집합을 비우고 ② 직전 배치를 취소하며 그쪽 `EndMergeCommit`을 발화**시킨다. 그래서 판정은 집합이 살아 있는 **앞**에서, 칠하기는 정리가 끝난 **뒤**에 해야 한다. 순서를 바꾸면 각각 "소모 대상 계산 불가" / "방금 켠 핑크가 지워짐"으로 조용히 깨진다(`TowerPlacer._onConfirmed`를 `BeginPlacement` 이후에 대입하는 것과 같은 계열의 함정).
- 밤 전환은 `HandleDayToNight`에서 잠금을 직접 푼다 — 배치 취소(`PhasePanelSwitcher.ShowNight`)로도 풀리지만 이벤트 구독 순서에 기대지 않기 위해.

### 5.4 영지 노드 — 섬/산만, 회오리는 제외 (**구현 완료**)

영지 노드는 상태에 따라 시각물이 교체된다(`TerritoryNodeStateVisual`): 확보 전 = `VortexVisual`이 런타임 생성한 **평면 Quad**(소용돌이), 확보 후 = 섬/산 프리팹 인스턴스. 평면 Quad에 인버티드 헐을 씌우면 소용돌이 모양이 아니라 **사각형**이 나온다 → **회오리는 기존 `_vortexHoverColor` 하이라이트(틴트 + 회전 가속 + 스케일 펄스)를 그대로 쓰고 아웃라인을 붙이지 않는다.**

대상이 상태에 따라 달라지므로 훅을 하나 둔다.

```csharp
public interface IOutlineTargetProvider { GameObject OutlineTarget { get; } }  // null = 아웃라인 없음
```

- **`TerritoryNodeView`가 구현하고**(호버·선택 히트는 노드 루트 콜라이더에서 나오므로 인터페이스도 루트에 있어야 드라이버가 찾는다) 판단은 `TerritoryNodeStateVisual.OutlineTarget`에 위임한다: 회오리·본진 → `null`, 확보 후 → **섬 인스턴스 GO**.
- 상태 비주얼이 없는 **구형 프리팹**(색상 경로)은 노드 루트 자신을 돌려준다 = 드라이버 기본 동작과 같다(회귀 없음).
- 드라이버는 히트한 컴포넌트의 GO에서 이 인터페이스를 찾아 있으면 그 대상에, 없으면 히트 GO에 적용한다.
- `TerritoryNodeView.OnSelected`는 "즉시 확보(비가역)"를 오버로드하고 있어 **선택 상태가 유지되지 않는다** → 영지 노드는 **호버 노랑만**, 초록은 대상 외.

**왜 노드 루트를 대상으로 쓰면 안 되는가(실측된 두 증상, §10.1)**

| 증상 | 원인 |
|---|---|
| 회오리 호버 시 회오리가 **흰 사각형**으로 변한다 | 노드 루트를 대상으로 잡으면 자식인 회오리 Quad에 shell이 생긴다. 그 shell이 프리패스에 깊이를 써서 뒤의 바다가 사라진다(§3.2 프리패스 마스크). **아웃라인 패스 자체는 `Cull Front`라 평면에서 아무것도 그리지 않는다** — 즉 사각형은 아웃라인이 아니라 깊이 구멍이었다 |
| 확보 후 **산에 호버해도 테두리가 없다** | 노드 루트의 `OutlineHighlight`가 회오리 시절에 shell 수집을 이미 마쳐(1회 캐시) 죽은 참조만 들고 있었다. 새로 스폰된 산에는 shell이 생기지 않는다 → §6.1-4의 재빌드 규칙으로 함께 해소 |

---

## 6. 렌더러 수집 정책

### 6.1 수집 규칙

1. `GetComponentsInChildren<MeshRenderer>(true)` + `GetComponentsInChildren<SkinnedMeshRenderer>(true)` — 타워·건물은 시각물이 자식에 있다(`TerritoryNodeView._visual` 분리 구조와 동일 계보). 타입을 이 둘로 한정하면 `LineRenderer`·`TrailRenderer`·`ParticleSystemRenderer`가 자동 배제된다.
2. **제외**: 조상에 `RangeCircle`이 있는 렌더러 — `Tower.cs:106`/`AuraTower.cs:127`이 사거리 원을 **타워 자식**(`"TowerRangeSelection"`)으로 생성하므로 제외하지 않으면 사거리 원판에 테두리가 생긴다. `RangeCircle`의 `Fill` 자식이 MeshRenderer라 타입 필터로는 걸러지지 않는다.
3. **제외**: 이미 만든 `OutlineShell` 자신(재수집 시 무한 증식 방지).
4. 수집은 **첫 표시 시 1회**, 결과를 캐시한다. 사거리 원은 첫 선택 때 지연 생성되므로 수집 시점이 그 전/후 어느 쪽이든 조상 검사로 안전해야 한다.
5. **단, 캐시한 shell 중 하나라도 파괴됐으면(= 원본 렌더러가 사라졌으면) 살아남은 shell까지 정리하고 다시 수집한다.** 대상의 시각물이 런타임에 교체되는 경우(영지 노드 회오리→섬)에 1회 캐시를 고정하면 죽은 참조만 남아 **아웃라인이 조용히 사라진다**(§5.4 표). 검사 비용은 캐시 리스트 순회 1회뿐이고 정상 대상은 첫 조건에서 빠져나간다. 대상 자체를 바꿔치기할 수 있으면 `IOutlineTargetProvider`(§5.4)를 쓰는 것이 우선이고, 이 규칙은 그 훅이 없는 대상까지 받쳐주는 안전망이다.

### 6.2 렌더러가 많은 대상 — 상한과 그 이유 (프록시 계획은 폐기)

`Castle.prefab`은 루트에 `BoxCollider` + `BuildingInfo` + `BuildingTooltipSource`가 붙은 정상 호버 대상인데 내용물이 Sweet_Land 소품 조립이라 **462 렌더러**(Terrace 128, TerraceRail 130, Balustrade 48, MainHall_L1 54 …)다. `Mine`도 44개다.

**박스 프록시는 원리상 불가능해 폐기했다.** 인버티드 헐은 **원본 지오메트리가 깊이를 써서 헐의 안쪽 면을 가려줄 때만** 테두리로 보인다. 대상보다 큰 박스는 가려주는 것이 없어 헐이 화면에 그대로 남아 **통째로 칠해진다**(실측 확인). 대체로 `RangeCircle` 바닥 링을 붙여봤지만 그 컴포넌트는 **편집 모드 씬 뷰에서 렌더되지 않아**(기존 `Sprites/Default` 경로 문제 — 이 이슈 범위 밖) 검증 자체가 불가능했다 → 검증할 수 없는 대체 경로를 남기지 않고 삭제했다(`RangeCircle`에 넣던 폭 인자도 원복).

**현재 정책**

- 상한 `k_MaxShellRenderers = 512` — 실측 최대인 Castle(462)을 통과시킨다. shell은 전부 같은 머티리얼이라 SRP Batcher로 묶이고, 비용은 "호버 중인 그 오브젝트를 한 번 더 그리는 것"이다. 첫 호버 시 shell 462개 생성이 **약 17ms(1회)**, 이후 0.
- 상한 초과 시 **아웃라인 생략 + 경고 로그**(대상 이름·렌더러 수). 조용히 누락시키지 않는다.
- 부품마다 테두리가 생겨 Castle 같은 대상은 다소 번잡하다 — 저폴리 실루엣 프록시 메시는 아트 작업으로 #138/#148에 남긴다(T5).

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

**구현 상태: 완료**(`Assets/Scripts/Editor/OutlineSmoothMeshBaker.cs`, `Assets/Scripts/GameManager/MouseManager/Highlight/OutlineSmoothMeshRegistry.cs`)

| 항목 | 값 |
|---|---|
| 에디터 메뉴 | `NorthLand/Outline/1. 베이크 대상 자동 수집` → `2. 스무스 메시 베이크` |
| 자동 수집 범위 | `@NorthLand/Prefabs/Tower`, `@NorthLand/Prefabs/Territory` — 이름에 `ghost`/`bullet`/`arrow`가 있거나 메시가 없는 프리팹은 제외하고, 제외 목록도 로그로 남긴다 |
| 대상 프리팹 | **16개** (타워 9종 + `Candy_04` + `Mountain_01~06`) — 레지스트리 인스펙터에서 손으로 가감 가능 |
| 실제 베이크 결과 | **유니크 메시 13개** (`CandyTower_01`·`Pot_01`·`TowerCap_01` 등이 여러 타워에 공유돼 중복 제거됨). 전부 uv3 채워짐 |
| 산출물 위치 | `Assets/Meshes/OutlineSmooth/<name>_smooth.asset` |
| 레지스트리 | `Assets/Resources/Outline/OutlineSmoothMeshRegistry.asset` (`Resources.Load`, DataTable CSV와 같은 규약) |
| 재실행 | 멱등 — 살아 있는 사본은 재사용한다(`신규 0 / 재사용 13`) |

**산출물을 `Assets/Imported` 밖에 두는 이유**: `Assets/Imported/`는 프로젝트 저장소에서 **`.gitignore` 대상**(`.gitignore:162`)이고 내부에 **중첩 git 저장소**(`Assets/Imported/.git`)로 따로 관리된다. 그 안에 구우면 팀원이 프로젝트 저장소를 받아도 사본이 따라오지 않아 레지스트리 참조가 깨진다. 원본 메시(아트 저장소)는 프리팹과 함께 오므로, **원본=아트 저장소 / 사본=프로젝트 저장소** 조합으로 둔다. 벤더 트리(`Sweet_Land`·`TARBO`)는 어느 쪽으로도 수정하지 않는다.

**폴백(T10 종결)**: 레지스트리에 사본이 없으면 `Resolve()`가 **원본 메시를 그대로 반환**한다 — 아웃라인이 끊겨 보이지만 기능은 살아 있다. 레지스트리 에셋 자체가 없으면 최초 1회만 경고 로그를 남긴다(매 호출 `Resources.Load` 반복 금지).

**주의 — `AssetDatabase.SaveAssets()` 금지**: 무관한 더티 에셋(동적 JP 폰트 아틀라스, 미니맵 RenderTexture 등)까지 디스크에 써서 남의 작업 트리를 더럽힌다. 베이커는 `SaveAssetIfDirty(registry)`만 쓴다(사본 메시는 `CreateAsset` 시점에 이미 기록된다).

건물은 프록시(§6.2)를 쓰므로 베이크 대상이 아니다 — 프록시 메시는 우리가 생성하므로 스무스 노멀을 처음부터 채워 만든다.

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
| Prepass Layer Mask (URP 17 신규) | `OutlineShell` 제외 (`m_Bits: 4294963199`) | **적용 완료** — 빼지 않으면 shell이 depth/normals 프리패스에 찍혀 **뒤의 오브젝트가 사라진다**(회오리 흰 사각형의 실제 원인, §10.1 A/B). 마이그레이션으로 생긴 필드라 기본값이 전 레이어(`4294967295`)였다 |
| 카메라 컬링 마스크 | 변경 없음(`-1`) | cullResults에 남아야 피처가 그릴 수 있다 |

> **미니맵 주의**: 씬에 `MinMapCamera`(cullingMask `-1`, depth 0)와 `Main Camera`(`-1`, depth -1)가 있다. 그대로 두면 **미니맵에도 아웃라인이 나온다.** 미니맵에 표시할지 결정하고, 표시하지 않을 거면 `MinMapCamera`의 컬링 마스크에서 `OutlineShell`을 제외한다. → **TODO(구현 시 결정)**

---

## 8. 수명주기 · 잔존 방지 체크리스트

| 상황 | 처리 |
|---|---|
| 배치 모드·스킬 조준 진입 | `MouseManager.BeginPlacement`/`BeginSkillTargeting`이 `ClearHover()`를 호출 → `OnHoverChanged(null)` → 드라이버가 노랑 해제. **모드 전환 순간 노란 아웃라인이 남지 않는지 확인 필요** |
| 배치·조준 취소 | `CancelPlacement`/`CancelSkillTargeting`은 `_mode = Idle`만 되돌린다 → 다음 `UpdateHover`에서 자연 복구 |
| Esc / 빈 곳 클릭 | `MouseManager.ClearSelection()`(= `Select(null)` + `OnPrimarySelect(null)`) → 단일 초록·그룹 초록 동시 해제 |
| **배치 시작** | `BeginPlacement`가 같은 `ClearSelection()`을 부른다 → 고스트를 드는 순간 초록(단일·그룹)·사거리 원·인포/합성 패널이 전부 내려간다(WL-086). 합성 경로에서 그 직후 켜지는 재료 핑크만 남는다(§5.3) |
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

### 10.1 영지 노드 버그 2건 — 원인 실측과 수정 (2026-07-27)

플레이 중 보고된 증상: **(1) 회오리에 호버하면 회오리가 도는 흰 사각형으로 변한다. (2) 확보한 산에 호버하면 테두리가 안 보인다.** 편집 모드에서 회오리·산을 세우고 shell을 붙여 결정론적으로 재현했다(잔재 0, 씬 미저장, 콘솔 에러 0).

**캡처 리그 주의**: `screenshot --view scene`은 백그라운드 에디터에서 카메라 변경을 반영하지 못했다(§10의 `sv.Focus()` 주의와 같은 계열). 그래서 **임시 카메라 + `RenderTexture` 직접 렌더 → `EncodeToPNG`** 로 찍었다 — URP 파이프라인 결과를 그대로 얻으면서 프레이밍이 결정론적이다. 앞으로 이 문서의 비주얼 A/B는 이 방식을 쓴다.

| # | 실측 | 결과 |
|---|---|---|
| 1 | 회오리 호버 시 흰 사각형의 정체 | 아웃라인 패스가 **아니다** — 픽셀색이 `_OutlineColor`(노랑 1, 0.92, 0.2)가 아니라 하늘/앰비언트에 가까운 (0.867, 0.973, 0.961)이었다 |
| 2 | `m_PrepassLayerMask`에서 레이어 12 제외 A/B(다른 조건 동일) | 같은 픽셀이 **(0.867, 0.973, 0.961) → (0.239, 0.482, 0.765) = 바다색**. 즉 shell이 프리패스에 깊이를 써서 **뒤의 바다를 탈락시킨 깊이 구멍**이었다 → 세 마스크를 같은 값으로 통일(§7) |
| 3 | 같은 마스크 변경이 실제 메시 아웃라인을 해치는지 | `Mountain_01` 호버 캡처 전/후 **차이 없음**(노란 실루엣 유지) — 아웃라인 패스는 프리패스 깊이에 의존하지 않는다 |
| 4 | 산 아웃라인 누락 재현 | 노드 루트 대상으로 `[회오리 호버 → 확보 → 산 호버]` 시 shell 개수 **1 → 0**. 섬 인스턴스를 대상으로 삼으면 **1** |
| 5 | 수정 후 회귀 | 회오리 호버 = 흰 사각형 없음(틴트·펄스만), 산 호버 = 노란 실루엣, 시각물 교체 후 재호버 shell **1개**(재빌드 동작), 컴파일 에러 0 |

캡처(로컬 전용 — `Screenshots/`는 `.gitignore:184` 대상이라 저장소에 올라가지 않는다): `repro_hover_on|off.png`(증상), `repro_prepass_on|off.png`(원인 A/B), `fix_vortex_hover.png`·`fix_mountain_hover.png`(수정 후). 다시 필요하면 위 절차로 재생성한다.

---

## 11. 완료 기준 (이슈 #213 체크리스트 매핑)

- [x] 호버 대상(건물·배치된 타워·영지 섬/산)에 커서를 올리면 노란 아웃라인 on, 벗어나면 off → §5.1 · §5.4(영지, 편집 모드 캡처로 확인 §10.1)
- [x] `ISelectable` 클릭 시 초록 on, 다른 대상·빈 곳·Esc에서 off → §5.1
- [x] Shift 다중 선택도 같은 초록, 다시 Shift로 빼면 즉시 off(WL-087 표면 재발 없음) → §5.2
- [x] 선택된(초록) 대상에 호버해도 노랑으로 밀리지 않음 → §4 (편집 모드 캡처로 확인)
- [x] 후보 버튼 호버 시 **소모될 재료만** 핑크, 여분은 초록 유지 → §5.3
- [x] 버튼에서 벗어나거나 패널이 닫히거나 집합이 바뀌면 핑크 잔존 없음 → §5.3 표
- [x] 버튼 **클릭 후 고스트 배치 중에도** 재료가 핑크 유지, 확정·취소 시점에 해제 → §5.3 `_previewCommitted`
- [x] 합성 소모·철거된 타워의 아웃라인이 월드에 남지 않음 → §8 (shell이 자식이라 함께 파괴)
- [x] 아웃라인이 클릭/호버 레이캐스트를 막지 않음 → §3.1, §10-4
- [x] `TowerGroupSelectable`의 하늘색 바닥 쿼드가 아웃라인으로 대체됨 → §5.2
- [x] 임시 색 3종을 한 곳에서 변경 가능 → §3.3
- [ ] PC/Mobile URP 양쪽에서 보임 → PC(Deferred) 확인, **Mobile 미확인(T9)**
- [x] 런타임 생성물 누수 없음(이 설계에서는 파괴 대상이 없음을 주석으로 명시) → §8
- [ ] `Docs/Core/MouseManager.md` §8 · `Docs/Core/TowerMerge.md` §8.4 갱신 + `Docs/Review/SystemMap.md` 반영
- [ ] #138(건물 시인성) 범위 정리 — 이 이슈의 호버 노랑이 #138 후보 중 하나를 실질적으로 구현한다. #138을 닫거나 "버튼 UI"로 좁힌다

---

## 12. 미확정 / TODO

| # | 항목 | 결정권 |
|---|---|---|
| ~~T1~~ | ~~shell 본체 패스 억제 방식~~ | **종결** — Layer Mask 제외로 확정(§10-2) |
| T2 | 미니맵(`MinMapCamera`)에 아웃라인 표시 여부 | 기획/아트 (미확인, 컬링 마스크 `-1`) |
| ~~T3~~ | ~~렌더러 수집 상한 값~~ | **종결** — 512로 확정, 초과 시 생략+경고(§6.2) |
| T4 | 임시 3색의 최종 색·선 굵기 | 아트 TBD |
| T5 | 큰 건물 저폴리 실루엣 프록시 메시 제작(부품별 테두리 번잡함 완화) | #148/#138 |
| T6 | 관통(엑스레이) 표시 필요 여부 | 기획 |
| T7 | `Soldier` 레이어(8) 등 리젝된 시스템 잔재 정리 | 별건 |
| T8 | 폭 드라이버 상수(C ≈ 35)와 상·하한 클램프 최종값 | 실기 튜닝 |
| T9 | **Mobile(Forward) 렌더러 시각 검증** — 품질 레벨 전환 후 캡처 | 구현 중 필수 |
| ~~T10~~ | ~~스무스 사본이 없는 메시의 폴백 정책~~ | **종결** — 원본 메시로 폴백 + 레지스트리 부재 시 1회 경고(§6.4) |

---

## 13. 변경 이력

| 날짜 | 내용 |
|---|---|
| 2026-07-27 | 최초 작성 — #213 착수 전 조사·결정 기록(shell 방식 확정, 컨버전은 #148로 분리). 구현 미착수 |
| 2026-07-27 | 렌더 경로 스파이크 완료 — 레이어(12)·렌더러 피처 PC/Mobile 적용, T1 종결(Layer Mask 제외), 직교 파라미터 확정(§3.4), 스무스 노멀 프리베이크 필수 판정(§6.4). Mobile 시각 검증(T9)·미니맵(T2) 미확인 |
| 2026-07-27 | 스무스 노멀 베이커·레지스트리 구현 완료(§6.4) — 대상 16 프리팹 → 유니크 메시 13개 베이크, 산출물은 gitignore되지 않는 `Assets/Meshes/OutlineSmooth`. T10 종결 |
| 2026-07-27 | 표시 경로 구현 완료 — `OutlineHighlight`/`OutlineInteractionDriver`/`IOutlineTargetProvider`, 타워 `IHoverable` 추가로 호버 노랑, 그룹 초록으로 하늘색 쿼드 대체, 합성 프리뷰 핑크. 박스 프록시 폐기·상한 512로 확정(§6.2), 머티리얼을 스무스 여부까지 6변형으로(§3.3), 드라이버는 런타임 부트스트랩(§5.1). 파괴 대상 통지 예외(WL-033 계열) 수정. T3 종결 |
| 2026-07-27 | 영지 노드 버그 2건 수정(§10.1 실측) — ① `m_PrepassLayerMask`에서 `OutlineShell` 제외(PC/Mobile): shell이 프리패스에 깊이를 써 회오리 뒤 바다를 지우던 **흰 사각형** 제거 ② §5.4 구현: `TerritoryNodeView`가 `IOutlineTargetProvider`, 판단은 `TerritoryNodeStateVisual.OutlineTarget`(회오리·본진 null / 섬 인스턴스) ③ shell 캐시가 죽었으면 재빌드(§6.1-5) — 시각물 교체 후 아웃라인이 사라지던 문제. 리뷰 지적 2건(벤더 참조·노랑 색 충돌)은 **유지 결정**으로 §2.5에 근거·해소 조건 기록 |
