# 카메라 시스템 가이드

정본 씬(`GameScene`)의 쿼터뷰 카메라 규칙을 정리한 문서. 기준 구현은 **`CameraController2`**다.

> 코드가 바뀌면 이 문서도 함께 갱신해서, 문서와 실제 구현이 어긋나지 않게 유지한다.

> ⚠ **`CameraController.cs`(번호 없는 쪽)는 미사용 잔존물이다**(WL-023). 엣지 스크롤·`MoveScreen`을 가진 구버전이고
> 정본 씬에는 배치돼 있지 않다. 이 문서의 이전 판이 그쪽 기준으로 쓰여 있었으니, 코드를 찾을 때 헷갈리지 말 것.

---

## 1. 구성 요소

| 용어 | 정의 | 정본 씬 배선 |
| --- | --- | --- |
| `cinemachineCamera` | 화면을 그리는 Cinemachine 카메라. 줌(오쏘그래픽 크기) 조절 대상 | `VCam_Territory` (회전 `(45°, 20°)`, Orthographic) |
| `cameraTarget` | 카메라가 따라다니는 타겟 Transform. 이동 입력은 이 오브젝트의 위치를 바꾼다 | `CameraTarget` |
| `mainCamera` | 화면 중심 → 지면 역산에 쓰는 실제 카메라(`MoveViewCenterTo`). 비우면 `Camera.main` | `Main Camera` |
| `waveRewardSelectionUI` / `settingUI` | 이 패널이 열려 있으면 **모든 카메라 입력을 차단**한다 | `RewardSystem` / `SettingCanvas` |

## 2. 이동

세 경로가 같은 `cameraTarget`을 움직인다. 결과 위치는 항상 `xBounds`·`zBounds`로 클램프된다.

| 경로 | 규칙 |
| --- | --- |
| **WASD** | 카메라의 forward/right를 y=0으로 눌러 정규화한 방향으로 `moveSpeed × unscaledDeltaTime` 이동 |
| **우클릭 드래그** | 드래그 시작 지점 대비 화면 이동량 × `dragSpeed`만큼 **반대 방향**으로 이동. UI 위에서 누르면 시작하지 않는다 |
| **미니맵 클릭** | `MoveTo` / `MoveViewCenterTo`가 목표를 잡고 `SmoothDamp`(`minimapMoveSmoothTime`)로 접근. WASD·드래그가 들어오면 즉시 취소 |

- **좌클릭이 아니라 우클릭 드래그다** — 좌클릭은 `MouseManager`의 선택 입력이라, 드래그 시작 지점의 오브젝트가 선택/해제되던 부작용을 피한 선택이다.
- ⚠ 우클릭은 배치·스킬 조준 모드에서 **취소**로도 쓰인다(`MouseManager`). 그 모드 중 드래그하면 취소와 카메라 이동이 함께 일어난다(WL-073).
- ⚠ 이동·줌 입력을 `Mouse`/`Keyboard.current`로 **직접 폴링**한다 — `MouseManager`의 '입력 단일 창구' 계약 밖이다(WL-023). `KeyboardManager`(#444)가 생긴 뒤에도 **여기로 옮기지 않는다**: 그쪽은 "눌린 순간 한 번" 단축키 디스패치이고, WASD·줌은 매 프레임 `isPressed`를 읽는 연속 입력이라 모델이 다르다(`MouseManager.md` §1 원칙 1의 예외 항목).
- 시간은 `unscaledDeltaTime` — 배속·일시정지와 무관하게 카메라는 같은 속도로 움직인다.

## 3. 줌 — `ZoomMouseWheel`

- 휠 스크롤 값이 0이면 아무 것도 하지 않는다.
- `Lens.OrthographicSize`에서 `스크롤값 × zoomSpeed`를 뺀 값을 다음 크기로 계산한다(휠 올리면 줌인, 내리면 줌아웃).
- `minZoomSize`~`maxZoomSize`로 클램프해 적용한다.
- **값이 실제로 바뀌었을 때만** `OnZoomChanged`를 발행한다 — 최대/최소에 붙은 채 휠을 굴리면 클램프에 걸려 값이 그대로다.

### 3.1 줌 소비 계약 (#138)

```csharp
public event Action<float> OnZoomChanged;   // 인자 = 변경 후 오쏘 사이즈 (변화량이 아니다)
public float CurrentZoomSize { get; }       // 붙을 때 1회 pull
public float MinZoomSize { get; }
public float MaxZoomSize { get; }
```

**페이로드가 변화량이 아닌 이유**: 소비처는 전부 "지금 얼마나 축소돼 있는가"로 판단한다. 변화량만 실으면 각자 누적 상태를
따로 들어야 하고, **게임 도중 생성된 오브젝트가 초기값을 받을 방법이 없다.** 그래서 계약은
**"붙을 때 `CurrentZoomSize`로 pull, 바뀌면 이벤트로 push"**다.

현재 소비처는 둘이고 **방식이 다른 것은 의도된 병존**이다:

| 소비처 | 방식 | 이유 |
| --- | --- | --- |
| `PixelationZoomBinder` | 매 `LateUpdate` 폴링 | `[ExecuteAlways]`로 **편집 모드에서도** 돌아야 한다 — 편집 모드에는 이벤트가 오지 않는다 |
| `ZoomDrivenVisibility` 파생 | 이벤트 + 초기 pull | 런타임 전용. 구간 판정이라 매 프레임 계산할 이유가 없다 |

> ⚠ **지금은 `ZoomMouseWheel`이 유일한 발행처다.** 부드러운 줌·대상 줌인·세이브 복원 등 `Lens`를 직접 쓰는 경로를 추가하면
> **그쪽에서도 발행해야 한다.** 안 하면 줌 연동 표시물이 조용히 옛 상태에 머물고, 증상이 "가끔 안 켜진다"라 원인에서 멀다.
> 발행이 `ZoomMouseWheel` 인라인이라 이를 구조로 막는 `ApplyZoom` seam이 아직 없다(WL-024).

## 4. 입력 차단 조건

다음 중 하나라도 참이면 그 프레임의 이동·줌 입력을 **전부** 건너뛴다(드래그 상태와 미니맵 이동도 취소).

- `waveRewardSelectionUI.Camerastop`(3택1 보상 패널) 또는 `settingUI.IsOpen`
- `GameManager.Instance.Result != GameResult.Playing`
- `Mouse.current == null`

## 5. 정본 씬 값 (2026-08-09 실측)

| 항목 | 값 |
| --- | --- |
| `moveSpeed` / `dragSpeed` | `50` / `0.3` |
| `zoomSpeed` | `10` |
| `minZoomSize` ~ `maxZoomSize` | `30` ~ `150` |
| `xBounds` / `zBounds` | `(-1450, 1200)` / `(-1050, 550)` |
| `minimapMoveSmoothTime` | `0.15` |

⚠ 줌 범위는 **줌 연동 표시물의 임계치와 짝**이다(`BuildingZoomHint`의 구간, `PixelationZoomBinder`의 해상도 보간).
범위를 바꾸면 그쪽 값도 함께 검토할 것 — 범위 자체는 `MinZoomSize`/`MaxZoomSize`로 읽으므로 폭이 바뀌어도 코드는 따라오지만,
**절대값으로 적어 둔 구간(예: `BuildingZoomHint`의 100~999)은 따라오지 않는다.**

## 6. 신규 씬에 카메라 적용할 때 체크리스트

- [ ] `CinemachineCamera`(Orthographic)와 `cameraTarget` Transform 연결
- [ ] `mainCamera` 연결(미니맵 클릭 시 화면 중심 보정에 필요, 비우면 `Camera.main` 폴백)
- [ ] 이동 범위(`xBounds`/`zBounds`)를 씬의 맵 크기에 맞게 설정
- [ ] 이동 속도(`moveSpeed`)·드래그 감도(`dragSpeed`) 확인
- [ ] 줌 속도(`zoomSpeed`)와 줌 범위(`minZoomSize`/`maxZoomSize`) 확인 — **줌 연동 표시물의 임계치와 함께**
- [ ] 입력 차단용 UI 참조(`waveRewardSelectionUI`/`settingUI`) 연결(없으면 차단이 동작하지 않는다)
- [ ] 카메라 `cullingMask` 확인 — **`SystemMap` §5의 "Everything에서 뺀다" 규약**(인스펙터 체크 해제가 허용목록으로 바꿔 버리는 함정)
