# [임시] 상호작용 아웃라인 — 스크린 스페이스 실루엣 이행 작업지시서

> ⚠️ **이 문서는 작업용 임시 문서다. 이행이 끝나면 삭제한다.**
> 삭제 조건과 삭제 전 반영처는 §8에 있다. `Docs/README.md` 색인에는 **일부러 등재하지 않았다**(삭제 시 색인도 고쳐야 하므로).
>
> **작성**: 2026-07-31 · **소유**: n0wst4ndup
> **관련 이슈**: #213(현행 shell 구현) · #148(전역 비주얼 룩) · #138(건물 시인성)
> **정본 문서**: `Docs/Core/InteractionOutline.md`(상호작용 아웃라인 명세) · `Docs/Rendering/VisualLookPipeline.md`(전역 룩)

---

## 1. 왜 이 작업을 하는가

현행 상호작용 아웃라인은 **인버티드 헐(shell) 방식**이다 — 대상의 렌더러마다 같은 메시를 쓰는 자식 렌더러를 만들고 거기에만 FlatKit 아웃라인 머티리얼을 물린다. #213에서 의도적으로 **임시 수단**으로 채택했고, 다음 네 가지가 구조적 한계로 남아 있다.

| 한계 | 현상 |
|---|---|
| 부품마다 테두리가 난다 | `Castle.prefab`이 **462 렌더러**(Sweet_Land 소품 조립)라 선택 표시가 실루엣이 아니라 **그물망**으로 보인다 |
| 렌더러 512개 상한 | `k_MaxShellRenderers = 512`. 초과 대상은 아웃라인을 **생략하고 경고만** 남긴다 |
| 스무스 노멀 프리베이크가 필수 | 하드 노멀 로우폴리에 헐을 씌우면 점선 프린지가 생겨, 에디터 베이커+레지스트리를 따로 운영 중이다 |
| 픽셀 룩과 양립 불가 (조건부) | 픽셀레이션은 아직 **보류**지만(§2.2), 켜는 순간 막힌다 — 컬러 버퍼를 블록 단위로 재샘플링하므로 1~2px 기하 아웃라인이 블록 양자화에서 **깜빡이며 끊긴다**. shell로 가면 이 선택지가 영영 닫힌다 |

**앞으로 들어올 에셋이 이 한계를 더 세게 때린다.** 본진 후보로 검토 중인 Candy Land 에셋은 `candyland.fbx` 하나에 마을 전체가 들어간 통짜 프리팹이고 머티리얼만 130개다 — 렌더러 수가 512를 넘으면 **아웃라인이 아예 안 나온다**.

---

## 2. 확정된 방향

**스크린 스페이스 실루엣**으로 교체한다.

```
[대상 렌더러 → 마스크 RT]   슬롯 값 기록: 1=Hover 2=Select 3=MergePreview
        ↓
[dilate → 원본 마스크 차감]  = 실루엣 링 (두께는 픽셀 그리드에 스냅)
        ↓
[값→색 매핑 후 컬러 버퍼 합성]
```

**핵심 이득**

- 부품 수와 무관하게 **오브젝트 전체 실루엣 하나** → 그물망 문제 소멸, 512 상한 소멸, 통짜 프리팹 그대로 수용
- 스무스 노멀 프리베이크가 **상호작용 경로에서는 불필요**해진다
- shell GameObject 생성·파괴·스킨드 블렌드셰이프 매 프레임 동기화가 **전부 삭제**된다 (첫 호버 17ms 스파이크도 사라짐 — 비트 플래그 세팅만 남는다)
- dilate 반경을 **픽셀 블록의 정수배**로 잡으면 아웃라인이 픽셀 그리드에 정확히 맞는다. 인버티드 헐로는 원리상 불가능하다
- 가려짐 제어가 공짜 — depth test를 끄면 "건물 뒤 타워도 선택 표시가 보인다"가 한 줄이다

**유일한 손해**: 한 마스크에 슬롯 값을 인코딩하므로 **인접한 같은 슬롯 오브젝트끼리 실루엣이 합쳐진다**(그룹 선택한 타워 두 개가 붙어 있으면 한 덩어리). 다른 슬롯끼리는 값이 달라 경계가 잡힌다. 그룹 선택 맥락에선 자연스러운 편이라 감수한다.

### 2.2 확정된 전제 (2026-07-31, 이 이행의 범위를 좁힌다)

| 결정 | 이 작업에 미치는 영향 |
|---|---|
| **아트 전역 아웃라인을 하지 않는다.** 아웃라인 = 선택·호버 전용 | 시각 언어 충돌 걱정이 사라져 **색·두께를 가독성 기준으로만** 정하면 된다. 그리고 **스무스 노멀 베이커·레지스트리·사본 13개가 전부 삭제 대상**이 된다(Phase 3에서 "판단"이 아니라 "삭제"로 확정) |
| **툰 셰이딩 = 그림자 단조화로 2D 느낌** (확정) | 이 이행과 독립. 병행 진행 가능 |
| **픽셀레이션은 보류 — 실물 보고 결정** | ⚠️ **이 이행이 그 결정을 기다리면 안 된다.** 두께를 **스크린 픽셀 단위 기본**으로 정의하고, 픽셀 그리드 스냅은 **켜고 끄는 옵션 모드**로 얹는다 |
| **Candy Land 구매 미확정** | Phase 0의 렌더러 수 측정은 구매 후로 미룬다. 다만 `Castle.prefab` 462개만으로도 이행 근거는 충분하다 — **착수를 막지 않는다** |

### 2.1 기각한 대안 — `InteractionOutline.md` §9 (MPB로 `_OutlineColor` 덮어쓰기)

정본 문서 §9에 적힌 이행 경로다. **다시 논의하지 않도록 기각 근거를 남긴다.**

1. **부품별 테두리가 그대로 남는다.** 애초에 가장 큰 불만(T5)이었는데 안 풀린다. 통짜 프리팹에선 더 심해진다.
2. **선택 피드백이 아트 라인과 같은 선이다.** 상시 켜진 툰 라인의 색만 바뀌는 건데, 픽셀 룩에서 1px 선의 색 변화는 거의 안 읽힌다. 시각 언어가 겹친다.
3. **MaterialPropertyBlock은 SRP Batcher 배칭을 깬다.** 462개 렌더러에 MPB를 걸면 호버하는 동안 그 오브젝트가 배칭에서 빠져, §9가 내세운 "추가 드로우 0"이 상쇄된다.
4. **전면 FlatKit 전환이 선행 조건**이라 아트가 끝나기 전엔 착수 자체가 불가능하다.

---

## 3. 조사로 확정된 사실 (재조사 불필요)

FlatKit 벤더 트리를 직접 확인한 결과다.

| 사실 | 근거 |
|---|---|
| FlatKit **Outline** 피처는 선택 하이라이트로 **못 쓴다** | `RenderFeatures/Outline/OutlineSettings.cs` — depth/normals/color **전역 엣지 검출** 풀스크린. 레이어 마스크도 대상 지정도 없다. 아트 룩 전용 |
| 현행 shell이 쓰는 **ObjectOutline** 피처는 RenderObjects + LayerMask 필터다 | `RenderFeatures/ObjectOutline/ObjectOutlineRendererFeature.cs:76-86` — `overrideShader = FlatKit/Stylized Surface`, `overrideShaderPassIndex: 1`, `PassNames: ["Outline"]` |
| FlatKit **Pixelation**은 순수 포스트프로세스다 | `RenderFeatures/Pixelation/FlatKitPixelation.cs` + `PixelationSettings.cs` — `_PixelSize = 1/resolution`, 기본 `resolution = 320`, 기본 이벤트 `BeforeRenderingPostProcessing`(500). **이미 그려진 컬러 버퍼를 블록 단위로 재샘플링**한다 |
| 1920 화면 기준 픽셀 블록은 대략 **6px** | `resolution=320`(긴 변 기준) |

---

## 4. 대상 마킹 방식 (핵심 설계점)

**게임 레이어를 바꾸면 안 된다.** `Selectable`(6)·`PlayerBase`(9)가 `MouseManager._selectableMask` 레이캐스트와 얽혀 있어, 호버 중에 대상을 다른 레이어로 옮기면 **레이캐스트가 깨진다.**

→ **`Renderer.renderingLayerMask` 비트 1개를 우리가 소유한다.**

- 렌더링 전용 uint라 물리·레이캐스트와 무관하다
- MPB와 달리 SRP Batcher를 깨지 않는다
- 패스에서 `FilteringSettings.renderingLayerMask`로 필터한다
- 조작은 항상 **OR로 켜고 AND-NOT으로 끈다**(비트 대입 금지 — 아트가 다른 비트를 쓸 수 있다)

> ⚠️ **미검증**: `FilteringSettings.renderingLayerMask` 필터가 URP 17 Render Graph 경로에서 의도대로 도는지는 스파이크로 확인해야 한다(§6 Phase 0).
>
> **폴백**: 안 되면 우리가 이미 수집해둔 렌더러 배열에 `CommandBuffer.DrawRenderer(r, maskMat)`를 직접 돌린다. 462회여도 머티리얼 하나라 부담이 없고, 필터 게임 없이 완전히 결정적이다. 폴백으로 가더라도 설계의 나머지는 그대로다.

---

## 5. 유지 / 삭제 경계

이 설계의 목적 중 하나가 "교체 지점을 `OutlineHighlight` 내부 한 곳으로 고정"하는 것이었다. **바깥은 한 줄도 안 바뀐다.**

| 유지 (건드리지 말 것) | 삭제 / 교체 |
|---|---|
| `OutlineHighlight.Set(kind, bool)` 공개 API | `EnsureShells` / `CreateShell` / `DiscardShells` / `HasDeadShell` |
| 우선순위 로직 `TryResolveSlot`(MergePreview > Selected\|GroupSelected > Hover) | `k_MaxShellRenderers = 512` 상한과 경고 |
| `OutlineInteractionDriver` 전체(이벤트 구독·Swap·씬 전환 방어) | `SkinnedPair` + `LateUpdate` 블렌드셰이프 동기화 |
| `IOutlineTargetProvider` + 영지 노드 분기(회오리·본진 = null) | `OutlineShell` 레이어(12), 렌더러 3개 마스크 설정(Opaque/Transparent/Prepass) |
| `TowerMergeCoordinator` 핑크 프리뷰 경로 | shell 머티리얼 6변형(`s_materials[3,2]`) |
| `RangeCircle` 제외 규칙 (§6 주의사항 8) | 스무스 메시 레지스트리 **의존**(에셋·베이커 자체는 아트용으로 잔존) |
| 런타임 부트스트랩 방식(씬 파일 무수정) | 줌 대응 폭 계산 `width = 35/orthoSize` → 픽셀 그리드 스냅으로 대체 |

신규 작업량은 **RendererFeature + 셰이더 약 250~350줄**. `OutlineHighlight` 내부는 오히려 100줄 이상 줄어든다.

---

## 6. 주의사항 (함정 목록)

착수 전에 반드시 읽을 것. 대부분 이미 한 번 밟은 지뢰다.

1. **게임 레이어를 바꾸지 말 것** — §4. 레이캐스트가 깨진다.
2. **`renderingLayerMask`는 OR/AND-NOT으로만** — 대입하면 아트가 쓰는 비트를 날린다.
3. **MaterialPropertyBlock을 쓰지 말 것** — SRP Batcher 배칭이 깨진다(§2.1-3).
4. **렌더 순서에 근거가 있다** — 상호작용 실루엣은 UI 피드백이라 **틸트-시프트 블러 대상이 아니다**(화면 위/아래에서 선택했을 때 표시가 흐려지면 안 된다). 픽셀레이션을 켤 경우엔 그보다는 앞이어야 그리드에 맞는다. → `틸트-시프트 → 상호작용 실루엣 → [픽셀레이션]`. 아트 전역 라인은 **미채택**이라 순서에서 빠졌다(§2.2).
5. **같은 `RenderPassEvent`에서 피처 리스트 순서에 의존하지 말 것** — 동작은 하지만 누가 리스트를 재정렬하면 조용히 깨진다. 우리 자작 피처는 이벤트 값을 명시적으로 벌려 잡는다(정확한 값 배정은 구현 시 확정).
6. **PC / Mobile 렌더러 양쪽에 반드시 등재** — 상호작용 실루엣은 룩이 아니라 **기능**이라, `VisualLookPipeline.md` §2 결정 5("일단 PC만")의 적용 대상이 아니다. 현행 shell도 양쪽에 들어가 있다. PC는 **Deferred**, Mobile은 **Forward**라 마스크 RT 거동을 각각 확인할 것.
7. **`MinMapCamera` 게이팅** — cullingMask가 `-1`이라 그대로 두면 미니맵에도 아웃라인이 나온다. #213에서 T2로 미해결로 남은 항목인데, 피처 방식이면 카메라 판별 한 줄로 끝난다. **이번에 같이 닫을 것.**
8. **`RangeCircle` 제외 규칙을 마스크에도 옮길 것** — 사거리 원이 타워 **자식**으로 생성되므로(`Tower.ShowRangeCircle`), 조상에 `RangeCircle`이 있는 렌더러를 빼지 않으면 원판에 테두리가 생긴다. `Fill` 자식이 MeshRenderer라 타입 필터로는 안 걸러진다.
9. **씬 파일을 건드리지 말 것** — 드라이버가 런타임 부트스트랩인 이유가 정본 씬 병합 규칙(`Docs/Core/SceneWorkflow.md`) 때문이다. 튜닝값은 인스펙터가 아니라 상수로 둔다.
10. **`AssetDatabase.SaveAssets()` 금지** — 무관한 더티 에셋(동적 JP 폰트 아틀라스, 미니맵 RenderTexture)까지 디스크에 써서 남의 작업 트리를 더럽힌다. `SaveAssetIfDirty(대상)`만 쓴다.
11. **산출물을 `Assets/Imported/` 안에 만들지 말 것** — 그 폴더는 `.gitignore` 대상이고 내부에 중첩 git 저장소로 따로 관리된다. 우리 C# 렌더러 피처와 셰이더는 **프로젝트 저장소 쪽**(`Assets/Scripts/Rendering/`·`Assets/Shaders/` 제안)에 둔다.
12. **`.meta` 동반 커밋** — 에디터 밖에서 파일을 만들었으면 `unity-cli editor refresh`로 Unity가 `.meta`를 생성하게 한 뒤 에셋과 함께 커밋한다.
13. **shell 코드를 먼저 지우지 말 것** — 새 경로가 PC/Mobile 양쪽에서 실제로 보이는 것을 확인한 **다음에** 걷어낸다(§7 Phase 순서).
14. **URP 렌더러 에셋 저장 시 포맷 마이그레이션이 딸려 온다** — `m_AssetVersion` 상승과 신규 필드가 함께 기록된다. 불가피하므로 diff에 포함하되, 다른 사람 브랜치와 충돌할 수 있으니 커밋 시 명시할 것.

---

## 7. 단계별 체크리스트

### Phase 0 — 측정·스파이크 (착수 판단)

- [x] ~~Candy Land 렌더러 수 측정~~ → **구매 완료, 실측 213개**(2026-08-03). 512 상한에 안 걸린다.
      즉 "상한 초과로 아웃라인이 아예 안 나온다"는 착수 근거는 **성립하지 않았다** — 다만 §1의
      나머지 세 한계(부품별 테두리·스무스 노멀 의존·픽셀 룩 비양립)는 그대로라 이행 근거는 유효하다
- [x] ~~`FilteringSettings.renderingLayerMask` 필터 확인~~ → **검증하지 않고 폴백을 택했다.**
      문서가 폴백으로 지정한 "수집해둔 렌더러 배열을 직접 그리는" 방식(`RasterCommandBuffer.DrawRenderer`)이
      필터 거동에 의존하지 않아 완전히 결정적이고, 대상 수가 그룹당 1~5개로 작아 부담이 없다.
      필터 경로가 필요해지면(대상이 수백 개로 커지면) 그때 스파이크한다
- [x] PC **Deferred** 경로에서 마스크 RT 정상 확인 (2026-08-03, 실루엣 정상 출력)
- [ ] Mobile **Forward** 경로 동일 확인 — 피처는 등재했으나 **미검증**(퀄리티 레벨 전환 필요)
- [x] 스파이크 잔재 0 / 콘솔 에러 0 확인 — 해제 시 잔재 없음(해제 vs 호버 차이 0.57% = 링 픽셀만)

### Phase 1 — 신규 경로 구축 (shell 유지, 병행) — **완료 (2026-08-03)**

- [x] 마스크 셰이더 + dilate/합성 셰이더 — `Assets/Shaders/Outline/InteractionOutlineMask.shader` ·
      `InteractionOutlineComposite.shader`
- [x] `ScriptableRendererFeature` — `Assets/Scripts/Rendering/InteractionOutlineFeature.cs`.
      `InteractionOutlineRegistry.HasTargets`가 false면 패스를 등록조차 하지 않는다(평시 비용 0)
- [x] 슬롯 값 인코딩 — 마스크 R 채널에 **0.25/0.5/0.75**(1/2/3을 R8 양자화 여유를 두고 매핑).
      색 매핑은 합성 셰이더의 `SlotToColor`, 값이 큰 쪽이 겹칠 때 우선(MergePreview > Select > Hover)
- [x] **두께 = 스크린 픽셀 단위**(`_Thickness`, 기본 3px). 픽셀 그리드 스냅이 필요해지면
      이 값을 블록 정수배로 넘기면 되고 셰이더는 그대로다 — 픽셀 채택 결정을 기다리지 않는다
- [x] 슬롯별 `ZTest` 분기 — 마스크 머티리얼 3개의 `_ZTestMode`로 갈린다(`Always`=8 투시 / `LEqual`=4 가려짐).
      기본값은 문서 제안대로 **호버=가려짐 / 선택·프리뷰=투시**. 피처 인스펙터에서 토글 가능
- [x] `MinMapCamera` 게이팅 — 피처의 `excludedCameraNames`(기본 `{"MinMapCamera"}`) + Reflection/Preview 카메라 제외
- [x] PC / Mobile 렌더러 양쪽 등재 — 아웃라인은 룩이 아니라 **기능**이므로 `VisualLookPipeline.md` §2 결정 5의 예외
- [x] 렌더 이벤트 값 명시 배정 — **`AfterRenderingTransparents`(500)**.
      ⚠️ 이 문서 §3이 FlatKit Pixelation 기본 이벤트를 `BeforeRenderingPostProcessing`(500)이라 적었는데
      **실제 값은 550**이다(실측). 즉 실루엣(500) → 픽셀레이션(550) 순서가 성립한다

**검증**(룩데브 씬 `GameScene_600`, `Building_1` 렌더러 2개):
셸 방식은 내부 엣지까지 전부 선이 그려졌으나(`Screenshots/148/29_B_hover_outline.png`),
새 경로는 **외곽 실루엣 하나**만 나온다(`30_screenspace_hover.png`). 해제 시 잔재 없음.

### Phase 2 — `OutlineHighlight` 내부 교체 — **완료 (2026-08-03)**

- [x] 렌더러 수집 로직 유지 + `RangeCircle` 제외 규칙 유지 (주의사항 8) — `IsEligible` 그대로
- [x] shell 생성 대신 **`InteractionOutlineRegistry.Set/Clear`** (§4의 `DrawRenderer` 폴백 경로)
- [x] 공개 API·우선순위 로직 **무변경** — `GetOrAdd` / `Set(kind, bool)` / `TryResolveSlot` 그대로.
      바깥 코드는 한 줄도 바뀌지 않았다
- [x] `SetWidth(float)`는 **시그니처만 남긴 no-op**으로 전환. 두께가 스크린 픽셀 단위가 되어
      줌 보정이 불필요해졌다(오브젝트를 삼키던 문제 자체가 사라짐). 드라이버가 계속 호출해도 무해하며,
      드라이버의 폭 계산 제거는 정리 단계로 미뤘다
- [x] `OnEnable`/`OnDisable`/`OnDestroy`에서 등록 해제 — 비활성·파괴된 대상이 마스크에 남아
      유령 실루엣이 되는 것을 막는다. 셸 시절에는 자식 파괴로 자동 정리됐던 부분이라 새로 필요해졌다
- [x] 검증: 호버 → 선택 우선순위(초록이 노랑을 덮음) 정상, 해제 시 잔재 없음,
      **셸 오브젝트 0개 생성**. 대상은 `Castle`(렌더러 5개) — 셸이면 그물망이 나왔을 케이스
- [ ] 그룹 선택·합성 프리뷰 4경로 + 밤 전환·배치 시작·합성 소모 시 잔존 없음 — **플레이 모드 실측 미완**
      (`InteractionOutline.md` §8 표 그대로 밟아야 한다)

### Phase 2에서 함께 삭제한 셸 코드 (Phase 3의 코드 부분)

`OutlineHighlight`에서 제거: `Shell`/`SkinnedPair` 구조체 · `LateUpdate` 블렌드셰이프 동기화 ·
`EnsureShells`/`CreateShell`/`DiscardShells`/`HasDeadShell` · `SetVisible` · `k_MaxShellRenderers`(512 상한) ·
`ShellLayer` 조회 · `GetSharedMaterial`/`SlotColor`/`s_materials`(6변형) · 스무스 노멀 키워드 분기.

**색은 이제 렌더러 피처의 인스펙터에 있다**(`PC_Renderer`/`Mobile_Renderer` → Interaction Outline).
셸 시절 authored 값(노랑 1/0.92/0.2 · 초록 0.25/1/0.35 · 핑크 1/0.35/0.75)을 그대로 옮겼다 —
아트가 코드 수정 없이 만질 수 있게 된 것이 부수 이득이다.

### Phase 3 — shell 잔재 제거

- [ ] `EnsureShells`/`CreateShell`/`DiscardShells`/`SkinnedPair`/`LateUpdate` 삭제
- [ ] `k_MaxShellRenderers` 상한 삭제
- [ ] `OutlineShell` 레이어(12) 회수 + PC/Mobile 렌더러의 Opaque/Transparent/**Prepass** 마스크 원복
- [ ] FlatKit `ObjectOutline` 피처 **제거**(PC/Mobile 양쪽) — shell이 유일한 사용처였고 아트 라인은 미채택이다(§2.2)
- [ ] **스무스 노멀 자산 일괄 삭제**(§2.2로 확정 — 판단 아님):
  - `Assets/Scripts/Editor/OutlineSmoothMeshBaker.cs` (에디터 메뉴 `NorthLand/Outline/*`)
  - `Assets/Scripts/GameManager/MouseManager/Highlight/OutlineSmoothMeshRegistry.cs`
  - `Assets/Resources/Outline/OutlineSmoothMeshRegistry.asset`
  - `Assets/Meshes/OutlineSmooth/*.asset` (13개) + 각 `.meta`
  - ⚠️ 삭제 전 다른 참조가 없는지 Grep. 삭제 후 `unity-cli editor refresh`로 고아 `.meta` 확인

### Phase 4 — 문서 정리 후 이 문서 삭제

- [ ] `Docs/Core/InteractionOutline.md` — §0 요지·§6.1~6.4·§7·§9를 새 방식으로 갱신(특히 **§9 이행 경로를 이 문서 내용으로 대체**)
- [ ] `Docs/Rendering/VisualLookPipeline.md` — §3.6 상태를 구현 완료로 갱신, 확정된 이벤트 값·두께 기록
- [ ] `Docs/Review/SystemMap.md` — 렌더러 피처·셰이더 등재
- [ ] `Docs/README.md` 색인 문구 확인
- [ ] **이 문서 삭제**

---

## 8. 열린 결정

### 해소됨 (2026-07-31)

- [x] **아트 전역 아웃라인 수단** → **아웃라인 자체를 안 한다.** 선택·호버 전용(§2.2). 스무스 노멀 자산 일괄 삭제 확정
- [x] **툰 셰이딩 방향** → 그림자 단조화로 2D 느낌, 확정. 이 이행과 독립이라 병행 가능
- [x] **픽셀 룩** → **보류하되 이 이행을 막지 않는다.** 두께는 스크린 픽셀 기본 + 그리드 스냅 옵션(§2.2)
- [x] **가려짐 표시 정책** → 구조는 지금 넣고 값은 나중에. 제안: 호버=가려짐 / 선택·프리뷰=투시

### 남은 것

- [ ] **Candy Land 구매 확정 시 본진 배선 위치** — 영지 노드 경로에 붙이면 `TerritoryNodeStateVisual.OutlineTarget`이 본진(`Kind.None`)에서 `null`을 반환하므로 **아웃라인이 안 걸린다.** 독립 건물로 붙이면 Castle과 같은 배선(루트 `BoxCollider` + `Selectable` 레이어 + `IHoverable`/`ISelectable` 구현체)이면 된다. 구매 확정 후 재논의
- [ ] **가려짐 정책 최종값** — 구현 후 켜보며 확정. 전부 투시면 디오라마 착시가 깨진다
- [ ] **실루엣 색·두께** — 현행 임시색(호버 노랑 / 선택 초록 / 프리뷰 핑크)을 그대로 갈지. 상시 라인이 없어져 자유도가 커졌다

---

## 9. 삭제 조건

Phase 4까지 전부 체크되고, 정본 문서(`InteractionOutline.md`·`VisualLookPipeline.md`·`SystemMap.md`)에 결과가 반영되면 **이 파일을 삭제한다.** 이 문서에만 있고 정본에 없는 내용이 남아 있으면 삭제하지 말 것 — 특히 §2.1(기각 근거)과 §6(주의사항)은 정본 어딘가로 옮겨져야 한다.
