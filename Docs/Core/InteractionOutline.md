# 상호작용 아웃라인(Interaction Outline) — 설계 명세

> **표시 방식 이력**: 2026-07-27 인버티드 헐(shell)로 구현 → **2026-08-03 스크린 스페이스 실루엣으로 교체**.
> 이 문서는 교체 후 상태를 기술한다(§3 렌더링 방식 · §9 이행 기록). 셸 방식의 채택 근거와 실측
> 데이터는 역사 기록으로 §2.1에 남겼다. **공개 API·색 우선순위·대상 지정 훅은 교체 전후 무변경**이다.

> **상태**: **호버 노랑 / 선택·그룹 초록 / 합성 프리뷰 핑크 + 영지 노드 대상(§5.4) 구현 완료**(§11 체크리스트).
> 셸 잔재 정리 완료(2026-08-03) — `OutlineShell` 레이어(12) 회수, FlatKit `ObjectOutline` 피처 제거,
> 렌더러 세 마스크 원복, 스무스 노멀 자산 16개 삭제. 미니맵 노출(T2)은 피처의 카메라 제외 목록으로 해소.
> **남은 것은 Mobile 렌더러 시각 검증(T9)과 `MouseManager.md`·`TowerMerge.md` 갱신이다.**
> 색·선 굵기는 전부 **임시(아트 TBD)**.
> **소유**: n0wst4ndup(#213)
> **이슈**: #213 [Feature] 상호작용 아웃라인 — 호버=노란색 / 선택=초록색(그룹 포함) / 합성 후보 버튼 호버 시 재료 타워만 핑크색
> **구현 파일**:
> - `Assets/Scripts/GameManager/MouseManager/Highlight/OutlineHighlight.cs` — (**구현 완료**) 상태 플래그·색 우선순위·대상 렌더러 수집 → 레지스트리 등록
> - `Assets/Scripts/Rendering/InteractionOutlineRegistry.cs` · `InteractionOutlineFeature.cs` — (**구현 완료**) 대상 등록소 + 렌더러 피처(마스크 → dilate → 합성)
> - `Assets/Shaders/Outline/InteractionOutlineMask.shader` · `InteractionOutlineComposite.shader` — (**구현 완료**) 슬롯 값 기록 / 링 추출·색 매핑
> - `Assets/Scripts/GameManager/MouseManager/Highlight/OutlineInteractionDriver.cs` — (**구현 완료**) MouseManager 이벤트 구독 → 호버 노랑·단일 선택 초록 구동. 줌 대응 폭 갱신 호출은 **이제 no-op**(§9)
> - `Assets/Scripts/GameManager/MouseManager/Highlight/IOutlineTargetProvider.cs` — (**구현 완료**) 아웃라인 대상 GO를 대신 지정하는 훅(구현체: `TerritoryNodeView`)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerGroupSelectable.cs` — (**수정 완료**) 하늘색 쿼드 제거, `IHoverable` 추가 구현, 그룹 초록
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerMergeCoordinator.cs` — (**수정 완료**) `PreviewMerge`/`ClearMergePreview` 추가
> - `Assets/Scripts/UI/TowerPanel/TowerMergeCandidateHover.cs` — (**구현 완료**) 후보 버튼 EventSystem 호버 → 코디네이터 프리뷰 호출
> - `Assets/Scripts/UI/TowerPanel/TowerMergePanelView.cs` — (**수정 완료**) `BuildCandidates`에서 위 컴포넌트 런타임 배선
> - `Assets/Scripts/GameManager/MouseManager/MouseManager.cs` · `Assets/Scripts/CombatSystem/Tower/Tower.cs` — (**수정 완료**) 파괴된 선택/호버 대상 통지 방어(§8, WL-033 계열 버그 수정)
> - `Assets/Scripts/ManagementSpace/Territory/View/TerritoryNodeView.cs` — (**수정 완료**) `IOutlineTargetProvider` 구현 — 판단은 상태 비주얼에 위임(§5.4)
> - `Assets/Scripts/ManagementSpace/Territory/View/TerritoryNodeStateVisual.cs` — (**수정 완료**) `OutlineTarget` 공개(회오리·본진=null, 섬/산=인스턴스)
> - ~~`OutlineSmoothMeshBaker.cs` · `OutlineSmoothMeshRegistry.cs` · 레지스트리 `.asset` · `Assets/Meshes/OutlineSmooth/*.asset`(13개)~~ — **2026-08-03 삭제**(총 16개 자산). 스무스 노멀은 셸 전용 해법이었다(§6.4)
> - `Assets/Settings/PC_Renderer.asset` · `Assets/Settings/Mobile_Renderer.asset` — (**적용 완료**) **Interaction Outline** 피처 등재(PC/Mobile 동일). 색·두께·투시 정책·카메라 제외 목록이 여기 있다. 셸 시절의 FlatKit `ObjectOutline` 피처는 **제거**, 세 마스크(Opaque/Transparent/Prepass)는 전 레이어로 **원복** 완료
> - `ProjectSettings/TagManager.asset` — `OutlineShell` 레이어(12) **회수 완료**(레이어 이름 비움). 레이어 12를 쓰는 오브젝트가 씬·프리팹에 하나도 없음을 확인한 뒤 진행했다
> **관련**: #148(전역 비주얼 룩 파이프라인 — 툰 셰이더), #138(경영 공간 건물 시인성), #67(호버 훅), #183/#192/#210, WL-076b·WL-085·WL-087
> **참조**: `Docs/Core/MouseManager.md`, `Docs/Core/TowerMerge.md` §8.4, `Docs/Review/SystemMap.md`, `Docs/Tools/unity-cli-guide.md`
> **문서 계약**: 코드가 이 명세와 어긋나면 문서를 갱신한다(팀 계약 #7). 공개 API·계약이 바뀌는 PR은 SystemMap을 같은 PR에서 갱신한다.

---

## 0. 설계 요지 (한 문단)

마우스 상호작용 상태를 **월드 오브젝트의 아웃라인**으로 표시한다. 색은 호버=노랑 / 선택=초록(그룹 포함) / 합성 재료 프리뷰=핑크(모두 **임시, 아트 TBD**). 표시 수단은 **자작 스크린 스페이스 실루엣**이다 — 대상 렌더러를 마스크 RT에 슬롯 값으로 그리고, dilate 후 원본을 차감해 링을 뽑아 컬러 버퍼에 합성한다. 대상의 머티리얼도 프리팹도 **건드리지 않고**, 자식 오브젝트도 만들지 않는다. 그래서 부품이 몇 개든 **오브젝트 전체 실루엣 하나**가 나온다. 상태 관리는 대상별 `OutlineHighlight` 컴포넌트 하나에 모으고, 구동은 `MouseManager` 이벤트를 구독하는 드라이버 1개 + 그룹 선택은 `TowerMergeCoordinator`가 담당한다.

---

## 1. 목적 / 범위

**목적**: "지금 무엇에 커서가 있고, 무엇이 선택돼 있고, 이 버튼을 누르면 무엇이 소모되는가"를 월드에서 즉시 읽히게 한다. `MouseManager`의 호버 훅(`IHoverable.OnHoverEnter`/`OnHoverExit`)에 남아 있던 "하이라이트 연출" 잔여분(#67에서 훅만 만들고 연출을 미룬 부분)과 `Docs/Core/TowerMerge.md` §8.4 "선택 타워 월드 하이라이트(아트 TBD)"를 아웃라인으로 확정한다.

**In**
- 호버 노랑: 건물(`BuildingTooltipSource`), 배치된 타워, 영지 노드의 **확보 후 섬/산**
- 선택 초록: `ISelectable` 단일 선택(건물·타워 — 마법 타워 포함, #164 리팩토링으로 타워는 단일 `Tower` 타입) + `IGroupSelectable` 그룹 선택(타워 다중)
- 합성 프리뷰 핑크: 합성 패널 후보 버튼 호버 시 **실제로 소모될 재료 타워만**
- `TowerGroupSelectable`의 임시 하늘색 바닥 쿼드 제거

**Out**
- 전체 머티리얼 FlatKit 컨버전 / 툰 룩 전환 → **#148**
- 최종 색·선 굵기 아트 확정 → 아트 TBD (이 이슈는 임시 색 3종을 한 곳에서 바꿀 수 있게만 보장)
- 가림(엑스레이) 관통 표시 → 별건
- 적/몬스터, UI 요소, 타일 그리드

---

## 2. 결정과 근거

### 2.1 (역사) 결정: shell 방식(자산 무수정) — "A안"

> ⚠️ **이 결정은 2026-08-03 스크린 스페이스 실루엣으로 교체됐다**(§3·§9). 아래 표와 실측 데이터는
> "왜 머티리얼 컨버전 방식을 택하지 않았는가"의 근거로 여전히 유효하므로 남긴다 — 특히 `Selectable`
> 레이어 전체를 아웃라인 패스로 재드로우하면 상시 ~913 드로우가 붙는다는 측정치는 지금도 유효한 반려 근거다.

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

## 3. 렌더링 방식 상세 (스크린 스페이스 실루엣)

> 2026-08-03 이행. 이전 방식(인버티드 헐 shell)의 서술은 §9에 기각 근거와 함께 요약만 남겼다.

### 3.1 구조 — 아무 GameObject도 만들지 않는다

```
[대상 렌더러]  ─ DrawRenderer(마스크 머티리얼) ─→  [마스크 RT · R8]
                                                     슬롯 값 0.25/0.5/0.75
                                                          ↓
                                          [16탭 원형 dilate → 원본 차감]  = 링
                                                          ↓
                                          [값→색 매핑 후 컬러 버퍼에 알파 블렌드]
```

- `OutlineHighlight`는 **`InteractionOutlineRegistry`에 "이 렌더러들을 이 슬롯으로"만 등록**한다.
  자식 오브젝트·메시·머티리얼을 하나도 만들지 않으므로 파괴 정리 대상도 없다.
- 부품 수와 무관하게 **오브젝트 전체 실루엣 하나**가 나온다. 셸의 "그물망" 문제가 원리적으로 사라진다.
- 등록 대상이 0개면 렌더러 피처가 **패스를 등록조차 하지 않는다** → 평시 비용 0.

**구현 파일**

| 파일 | 역할 |
|---|---|
| `Assets/Scripts/Rendering/InteractionOutlineRegistry.cs` | 대상 등록소. 슬롯별 렌더러 목록 |
| `Assets/Scripts/Rendering/InteractionOutlineFeature.cs` | 렌더러 피처 + 2패스(마스크 / 합성) |
| `Assets/Shaders/Outline/InteractionOutlineMask.shader` | R 채널에 슬롯 값만 기록. `ZTest`가 프로퍼티 |
| `Assets/Shaders/Outline/InteractionOutlineComposite.shader` | dilate·차감·색 매핑·블렌드 |

### 3.2 대상 마킹 — `renderingLayerMask` 필터가 아니라 명시적 등록

`FilteringSettings.renderingLayerMask` 필터로 URP가 대상을 골라주게 하는 방식을 검토했으나,
URP 17 Render Graph 경로에서의 거동이 미검증이라 **수집해둔 렌더러 배열을 직접 그리는** 쪽을 택했다
(`RasterCommandBuffer.DrawRenderer`). 필터 거동에 의존하지 않아 완전히 결정적이고,
대상 수가 그룹당 1~5개로 작아 부담이 없다. 대상이 수백 개로 커지면 그때 필터 경로를 스파이크한다.

⚠️ **게임 레이어를 바꾸는 방식은 검토 단계에서 배제했다.** `Selectable`(6)·`PlayerBase`(9)가
`MouseManager._selectableMask` 레이캐스트와 얽혀 있어, 호버 중에 대상을 다른 레이어로 옮기면
**레이캐스트가 깨진다.**

### 3.3 렌더 이벤트 — `AfterRenderingTransparents`(500)

`VisualLookPipeline.md` §3.8의 순서 근거를 그대로 따른다.

- 틸트-시프트(예정)보다 **뒤** — 선택 표시는 UI 피드백이라 블러 대상이 아니다
- 픽셀레이션(**550**)보다 **앞** — 켤 경우 화면 전체가 같은 그리드에 맞아야 한다

같은 이벤트에 몰아넣고 피처 리스트 순서에 의존하지 않는다(누가 재정렬하면 조용히 깨진다).

### 3.4 파라미터 — 전부 렌더러 피처 인스펙터에

`PC_Renderer.asset` / `Mobile_Renderer.asset` → **Interaction Outline**. 코드 상수가 아니라
에셋 값이라 아트가 직접 만진다.

| 프로퍼티 | 기본값 | 의미 |
|---|---|---|
| `Thickness` | 3 px | **스크린 픽셀 단위.** 픽셀레이션 채택 시 블록 정수배로 넘기면 그리드에 맞는다 |
| `Hover / Selected / Merge Preview Color` | 노랑 / 초록 / 핑크 | 셸 시절 authored 값 그대로 이관 |
| `Hover See Through` | off | 호버는 레이캐스트로 맞춘 대상이라 정의상 보이는 상태다 |
| `Selected / Merge Preview See Through` | on | 선택 **후** 카메라가 움직이거나 앞이 가려질 수 있다. 합성 프리뷰는 "무엇이 소모되는지"가 기능의 목적 |
| `Excluded Camera Names` | `{ MinMapCamera }` | 미니맵 cullingMask가 `-1`이라 그대로 두면 미니맵에도 나온다 |

**직교 카메라 전제가 사라졌다.** 셸 시절에는 폭이 월드 단위라 `orthographicSize`에 반비례로
보정해야 했고(고정 폭은 줌아웃에서 오브젝트를 삼켰다), 스크린 픽셀 기준이 되면서 그 보정이
불필요해졌다. `OutlineHighlight.SetWidth(float)`는 **시그니처만 남은 no-op**이다.

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
3. `Tower.cs`(Combat 소유)·`BuildingInfo`·`TerritoryNodeView`를 **한 줄도 수정하지 않는다**. (작성 시점엔 `AuraTower`도 대상이었으나 #164 리팩토링으로 `Tower`에 통합됐다 — 드라이버가 구상 타입을 몰랐던 덕에 그 통합에도 아웃라인 코드는 무수정이었다.)
4. `MouseManager.Select`는 **낮/밤 게이트가 없다** → **밤에 타워를 클릭해도 초록이 뜬다**(사거리 원·정보 패널과 피드백이 일치). 코디네이터의 `IsDay` 게이트는 그대로 유지된다(밤에는 그룹·합성이 잠긴 채 단일 초록만 뜬다).
   - **이 "셋이 함께 뜨고 함께 진다"가 불변식이다.** 셋 중 일부만 내리는 경로를 만들면 `_selected`가 남아 그 대상이 재클릭에도 반응하지 않는다 → 표시를 내릴 땐 항상 `MouseManager.ClearSelection()`(SystemMap §2)을 쓴다. 페이즈 전환에서 실제로 깨졌던 지점이다(WL-086).

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

**클릭 이후 고정(`_previewCommitted`)은 폐기됐다 — #263**

예전에는 버튼을 클릭한 뒤 위 트리거를 전부 막고 배치가 끝날 때까지 핑크를 고정했다. 호버 프리뷰만 있으면 **클릭하는 순간 커서가 버튼을 벗어나며 `OnPointerExit`가 즉시 핑크를 걷어가고**, 재료가 초록으로 되돌아간 채 결과 고스트를 들고 다니게 되기 때문이었다.

**#263이 재료 소모를 클릭 시점으로 앞당기면서 이 잠금은 목적을 잃었다** — 칠할 대상이 그 순간 씬에서 사라진다. "무엇이 소모됐는지"는 이제 **재료가 비워진 자리**가 말해주고, 그 구간의 시각적 공백은 연출(#265: 화이트아웃 → 폭발 → 입자 부유)이 채운다 — **구현됨**, `TowerMerge.md` §9.2. 재료 자리에 흰 입자가 배치 내내 떠 있으므로 핑크 고정의 역할이 그대로 이어진다.

따라서 현재 계약은 단순하다:

- 핑크는 **호버 동안만**. `PreviewMerge`/`ClearMergePreview`에 잠금 검사가 없다.
- `RequestMerge`는 소모 대상을 미리 스냅샷하지 않는다. 집합이 비워지기 전에 "무엇을 칠할지" 확보해야 했던 **순서 계약(판정 먼저 · 칠하기 나중)도 함께 사라졌다.**
- 취소로 재료가 되살아날 때 핑크가 남지 않는 것은 `Undo` → 집합 변경 → `ClearMergePreview`가 **같은 콜스택에서** 돌기 때문이다(렌더 사이에 끼지 않아 깜빡임도 없다).
- `TowerFusionController.TryFuse`의 `onEnded` 인자도 제거됐다(유일한 소비처가 이 고정이었다). 연출처럼 **확정과 취소를 갈라 봐야 하는** 소비처가 생기면 그때 필요한 형태로 다시 낸다 — 구 `onEnded`는 둘을 구분하지 못했다.

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

`OutlineHighlight.EnsureSources()`가 `GetComponentsInChildren<MeshRenderer>` +
`<SkinnedMeshRenderer>`(비활성 포함)로 모으고 `IsEligible`로 걸러낸다.

- **`RangeCircle` 조상 제외** — 사거리 원은 타워 **자식**으로 생성되므로(`Tower.ShowRangeCircle`)
  빼지 않으면 원판에 테두리가 생긴다. `Fill` 자식이 MeshRenderer라 타입 필터로는 안 걸러진다
- **정점 0개 메시 제외** — 그릴 것이 없다
- `OutlineShell` 이름 자식 제외 — 셸 시절 잔재가 정본 씬에 남아 있을 수 있다

**시각물이 런타임에 교체되는 대상**(영지 노드 회오리→섬, 프리팹 교체)은 1회 수집으로 고정하면
교체 뒤 죽은 참조만 남아 **아웃라인이 조용히 사라진다** → 죽은 렌더러를 감지하면 다시 수집한다.

### 6.2 렌더러 수 상한 — **없다**

셸 시절의 512개 상한(`k_MaxShellRenderers`)은 **폐지됐다.** 실루엣은 마스크에 그리는 드로우 수만
늘 뿐 오브젝트를 만들지 않으므로, `Castle.prefab`(441 Mesh + 21 Skinned = 462) 같은 통짜 프리팹도
그대로 수용한다. 저폴리 실루엣 프록시 계획도 함께 불필요해졌다.

### 6.3 SkinnedMeshRenderer 처리

`RasterCommandBuffer.DrawRenderer`가 스킨드 렌더러를 그대로 그린다 — Unity가 스키닝을 반영한
정점을 넘겨주므로 마스크 셰이더는 평범한 오브젝트→클립 변환만 한다.

셸 시절 필요했던 것들이 전부 사라졌다: 본 배열 공유, `rootBone`/`updateWhenOffscreen` 복사,
그리고 **블렌드셰이프 가중치를 매 프레임 셸로 복사하던 `LateUpdate`**.

### 6.4 스무스 노멀 프리베이크 — **불필요**

지오메트리를 법선 방향으로 부풀리지 않으므로 하드 노멀 로우폴리에서도 점선 프린지가 생기지 않는다.
`OutlineSmoothMeshBaker` · `OutlineSmoothMeshRegistry` · `Assets/Meshes/OutlineSmooth/*.asset`(13개)는
**2026-08-03 삭제 완료**(베이커·레지스트리 스크립트, 레지스트리 `.asset`, 사본 메시 13개 — 총 16개 자산).

---

## 7. 레이어 / 렌더링 설정

**의존이 대부분 사라졌다.** 셸 시절에는 `OutlineShell` 레이어(12)와 URP 렌더러의
Opaque / Transparent / **Prepass** 레이어 마스크 세 곳을 정확히 맞춰야 했고, 셋 중 하나라도
빠뜨리면 뒤의 오브젝트가 깊이 테스트에서 탈락했다(영지 회오리가 흰 사각형이 되던 증상, §10.1).
스크린 스페이스 실루엣은 **자체 마스크 RT에 그리므로 그 설정을 전혀 쓰지 않는다.**

지금 필요한 설정은 하나뿐이다:

| 대상 | 설정 |
|---|---|
| `PC_Renderer.asset` · `Mobile_Renderer.asset` | **Interaction Outline** 피처 등재 |

아웃라인은 룩이 아니라 **기능**이므로 `VisualLookPipeline.md` §2 결정 5("룩 계층은 PC 전용")의
적용 대상이 아니다 — 양쪽 필수다. 단 **Mobile Forward 경로는 아직 미검증**이다(T9).

⚠️ **아직 남아 있는 셸 잔재**(무해하지만 정리 대상 — Phase 3):
`OutlineShell` 레이어(12), 세 마스크의 제외 설정, FlatKit `ObjectOutline` 피처 등재.
셸을 아무도 만들지 않으므로 레이어 12에 오브젝트가 생기지 않고, 그 피처는 그릴 대상이 없다.

> URP 렌더러 에셋을 저장하면 포맷 마이그레이션(`m_AssetVersion` 상승 + 신규 필드)이 딸려 온다.
> 불가피하므로 diff에 포함하되 다른 사람 브랜치와 충돌할 수 있으니 커밋 시 명시할 것.

---
## 8. 수명주기 · 잔존 방지 체크리스트

| 상황 | 처리 |
|---|---|
| 배치 모드·스킬 조준 진입 | `MouseManager.BeginPlacement`/`BeginSkillTargeting`이 `ClearHover()`를 호출 → `OnHoverChanged(null)` → 드라이버가 노랑 해제. **모드 전환 순간 노란 아웃라인이 남지 않는지 확인 필요** |
| 배치·조준 취소 | `CancelPlacement`/`CancelSkillTargeting`은 `_mode = Idle`만 되돌린다 → 다음 `UpdateHover`에서 자연 복구 |
| Esc / 빈 곳 클릭 | `MouseManager.ClearSelection()`(= `Select(null)` + `OnPrimarySelect(null)`) → 단일 초록·그룹 초록 동시 해제 |
| **배치 시작** | `BeginPlacement`가 같은 `ClearSelection()`을 부른다 → 고스트를 드는 순간 초록(단일·그룹)·사거리 원·인포/합성 패널이 전부 내려간다(WL-086). 합성 경로도 남는 아웃라인이 없다 — 재료는 이 시점에 이미 소모돼 씬에 없다(#263, §5.3) |
| Shift로 그룹에서 제거 | 코디네이터 `RefreshHighlight` diff가 `OnGroupDeselected` → 즉시 해제(WL-087 표면 재발 없음) |
| 밤 전환 | 코디네이터 `HandleDayToNight`가 집합 리셋 → 그룹 초록·핑크 해제 **+ `PhasePanelSwitcher.ShowNight`가 `MouseManager.ClearSelection()`** → 단일 초록·사거리 원·인포도 함께 해제. 코디네이터만 내리면 `_selected`가 남아 **초록만 잔존하고 그 타워는 밤에 재클릭해도 안 뜬다**(중복 제거에 삼킴) — §4-4 불변식 위반이라 두 신호를 짝지어 보낸다(WL-086) |
| 합성 소모·철거·사망 | 타워 GO 파괴 → shell은 자식이라 함께 파괴. 코디네이터는 `Tower.ActiveChanged` → `Prune`(WL-076b)로 죽은 참조 정리 |
| 컴포넌트 파괴 | 런타임 생성물 중 **대상별로 파괴할 것이 없다** — shell 메시는 원본/프리베이크 에셋 공유, 프록시는 static 유닛 큐브 공유, 머티리얼 3개도 static 공유다. shell GO는 자식이라 자동 파괴. → `RangeCircle`(PR#115 리뷰)처럼 `OnDestroy`에서 Mesh/Material을 파괴할 필요가 **없는 이유**를 주석으로 명시한다. 단 static 공유물(머티리얼 3개·유닛 큐브)은 도메인 리로드까지 유지되므로 **대상별 인스턴스를 만들지 않는 규칙을 깨지 말 것** |

---

## 9. 이행 완료 기록 — shell → 스크린 스페이스 실루엣

이 문서의 이전 판은 §9에 **MPB로 `_OutlineColor`를 덮어쓰는 이행안**을 적어두었다. 그 안은
**기각**됐고, 다시 논의하지 않도록 근거를 남긴다.

1. **부품별 테두리가 그대로 남는다.** 애초에 가장 큰 불만이었는데 해결되지 않는다.
   통짜 프리팹에서는 더 심해진다
2. **선택 피드백이 아트 라인과 같은 선이 된다.** 상시 켜진 툰 라인의 색만 바뀌는 셈이라
   픽셀 룩에서 1px 선의 색 변화는 거의 읽히지 않는다 — 시각 언어가 겹친다
3. **MaterialPropertyBlock이 SRP Batcher 배칭을 깬다.** 462개 렌더러에 MPB를 걸면
   호버하는 동안 그 오브젝트가 배칭에서 빠져 "추가 드로우 0"이라는 이점이 상쇄된다
4. **전면 FlatKit 전환이 선행 조건**이라 아트가 끝나기 전엔 착수 자체가 불가능하다

**실제로 채택한 것은 스크린 스페이스 실루엣**(§3)이다. 위 4개가 전부 해소되고, 덤으로
렌더러 512개 상한·스무스 노멀 프리베이크 의존·픽셀 룩 비양립까지 사라졌다.

**공개 계약은 바뀌지 않았다** — `OutlineHighlight.GetOrAdd(go).Set(kind, bool)`,
색 우선순위(§4), `IOutlineTargetProvider`(§5.4) 모두 그대로다. 드라이버·코디네이터·영지 노드
경로는 한 줄도 수정되지 않았다.

바뀐 계약 두 가지:

- **`SetWidth(float)`가 no-op이 됐다.** 두께가 스크린 픽셀 단위라 줌 보정이 불필요하다.
  호출부를 건드리지 않기 위해 시그니처만 남겼다
- **`OnEnable`/`OnDisable`/`OnDestroy`에서 등록 해제가 필요해졌다.** 셸은 대상의 자식이라
  파괴 시 자동 정리됐지만, 레지스트리는 명시적으로 지우지 않으면 마스크에 유령 실루엣이 남는다

셸 잔재(레이어 12·FlatKit `ObjectOutline` 피처·렌더러 마스크·스무스 노멀 자산)는 2026-08-03에 전부 제거됐다.

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
- [x] ~~버튼 **클릭 후 고스트 배치 중에도** 재료가 핑크 유지, 확정·취소 시점에 해제~~ → **#263에서 폐기.** 소모가 클릭 시점으로 앞당겨져 칠할 대상이 사라졌다 → §5.3
- [x] 합성 소모·철거된 타워의 아웃라인이 월드에 남지 않음 → §8 (shell이 자식이라 함께 파괴)
- [x] 아웃라인이 클릭/호버 레이캐스트를 막지 않음 → §3.1, §10-4
- [x] `TowerGroupSelectable`의 하늘색 바닥 쿼드가 아웃라인으로 대체됨 → §5.2
- [x] 임시 색 3종을 한 곳에서 변경 가능 → §3.3
- [ ] PC/Mobile URP 양쪽에서 보임 → PC(Deferred) 확인, **Mobile 미확인(T9)**
- [x] 런타임 생성물 누수 없음(이 설계에서는 파괴 대상이 없음을 주석으로 명시) → §8
- [x] `Docs/Core/MouseManager.md` 갱신 → #261 문서 개편에서 그룹 선택(#183)·아웃라인 위임 반영 완료
- [ ] `Docs/Core/TowerMerge.md` §8.4 갱신 + `Docs/Review/SystemMap.md` 반영
- [x] #138(건물 시인성) 범위 정리 — **#138은 아웃라인이 아니라 파티클로 갔다**(§14). 이 이슈의 호버 노랑은 "커서가 지금 어디 있는가"로 남고, #138은 "줌 아웃 시 어느 건물이 상호작용 가능한가"를 상시 파티클로 답한다 — 두 축이 겹치지 않는다

---

## 12. 미확정 / TODO

| # | 항목 | 결정권 |
|---|---|---|
| ~~T1~~ | ~~shell 본체 패스 억제 방식~~ | **종결** — Layer Mask 제외로 확정(§10-2) |
| T2 | 미니맵(`MinMapCamera`)에 아웃라인 표시 여부 | 기획/아트 (미확인, 컬링 마스크 `-1`) |
| ~~T3~~ | ~~렌더러 수집 상한 값~~ | **종결** — 512로 확정, 초과 시 생략+경고(§6.2) |
| T4 | 임시 3색의 최종 색·선 굵기 | 아트 TBD |
| T5 | 큰 건물 저폴리 실루엣 프록시 메시 제작(부품별 테두리 번잡함 완화) | #148 (#138은 아웃라인을 쓰지 않기로 해 이 축에서 빠졌다 — §14) |
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
| 2026-08-09 | #138(건물 시인성)이 5번째 종류 `OutlineKind.Interactable`을 추가했다가 **전량 원복**(§14). 이 문서의 공개 계약·슬롯 인코딩은 착수 전 상태와 동일하다 |

---

## 14. 반려 기록 — "상호작용 가능" 상시 아웃라인(#138)

> 결론: **아웃라인으로 하지 않는다.** 같은 제안이 다시 올라오면 이 절을 먼저 읽을 것.

#138(경영 공간 건물 시인성)은 "줌 아웃하면 건물 실루엣이 배경과 뭉개져 어느 것이 클릭 가능한지 안 읽힌다"를 다룬다.
첫 접근은 이 시스템에 **5번째 종류를 추가**하는 것이었다 — `OutlineKind.Interactable`(우선순위 최하, 검정),
슬롯 인코딩을 0.25/0.5/0.75에서 **0.2/0.4/0.6/0.8로 재배치**, 합성 셰이더에 `_InteractableColor`와 경계 0.3/0.5/0.7 추가,
PC/Mobile 렌더러 피처에 색·see-through 필드 추가.

**기술적으로는 성립했다.** 편집 모드 캡처로 4색(검정/노랑/초록/핑크)이 각각 정상 렌더되고 기존 3색 회귀가 없음을 확인했고,
비용도 문제가 아니었다 — 상시 대상 6종의 렌더러 합계가 **70개**로, §2.1이 반려 근거로 삼은 "레이어 전체 재드로우 = 상시 ~913 드로우"와는 자릿수가 다르다
(§2.1의 `Castle.prefab` 441+21은 **구형 프리팹** 수치다. 현행 `CandyLand` 기준 Castle은 6, Building_2가 52로 최대, 나머지는 한 자리).

**반려 사유는 미적인 것이다**: 상시 켜진 검은 테두리가 게임의 몰입을 깼다.
호버·선택은 **플레이어 행동에 대한 응답**이라 잠깐 뜨는 UI 언어로 읽히지만, 상시 표시는 월드에 늘 얹혀 있어 아트 라인과 경쟁한다.
줌 아웃 상태에서 6개 건물이 동시에 테두리를 두르면 화면이 "UI 오버레이"가 된다.

**대신 채택한 것**: 건물 자리에서 재생하는 **파티클**(`BuildingZoomHint` + `ZoomDrivenVisibility`, `SystemMap` §1·§2).
월드에 자연스럽게 얹히고, 건물마다 다른 이펙트를 줄 수 있어 "어떤 건물인가"까지 함께 전달된다.

**되돌린 범위**: `OutlineHighlight`·`InteractionOutlineRegistry`·`InteractionOutlineFeature`·`InteractionOutlineComposite.shader`·
`PC_Renderer`/`Mobile_Renderer` 전부 착수 전 상태로 `git restore`. **슬롯 인코딩도 0.25/0.5/0.75로 복귀**했다 —
소비처 없는 4번째 슬롯을 남기면 잘 돌던 시스템에 회귀 위험만 얹는 셈이기 때문이다.

**다시 검토할 만한 경우**: 상시 표시가 아니라 **짧게 명멸하는** 용법(예: 낮 시작 시 1회 펄스)이라면 미적 반려 사유가 성립하지 않는다.
그때는 5번째 슬롯 추가가 다시 후보가 되고, 위 변경 목록이 그대로 작업 범위다.
