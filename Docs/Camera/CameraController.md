# 카메라 시스템 가이드

RTS류 게임에서 흔히 쓰는 탑뷰 카메라 이동/줌 규칙을 정리한 문서. `Camera` 폴더의 실제 구현(`CameraController`)을 기준으로 작성했다.

> 코드가 바뀌면 이 문서도 함께 갱신해서, 문서와 실제 구현이 어긋나지 않게 유지한다.

## 1. 구성 요소

| 용어 | 정의 |
| --- | --- |
| `cinemachineCamera` | 실제로 화면을 그리는 Cinemachine 카메라. 줌(오소그래픽 크기) 조절 대상 |
| `cameraTarget` | 카메라가 따라다니는 타겟 Transform. 이동 입력은 이 오브젝트의 위치를 바꾸는 방식으로 처리된다 |

## 2. 이동 규칙 — `MoveScreen`

- 매 프레임 `Camera.main`의 forward/right 벡터를 구하고, y값을 0으로 눌러 수평 평면 기준 방향으로 정규화한다.
- 키보드 WASD 입력에 따라 이동 방향을 누적한다: `W`/`S`는 forward/-forward, `A`/`D`는 -right/right.
- 마우스가 화면 가장자리(상하좌우 `edgeSize` 픽셀 이내, 기본값 20px)에 있으면 그 방향으로도 이동 방향이 추가된다 (엣지 스크롤).
- 엣지 스크롤이 한 번이라도 발동한 프레임에서는 이동 속도가 `moveSpeed`(기본 15)보다 5 빠르게 적용된다.
- 최종 이동 방향이 0이면(입력 없음) 이동하지 않는다. 그렇지 않으면 방향을 정규화한 뒤 속도와 `Time.deltaTime`을 곱해 `cameraTarget`의 위치를 갱신한다.
- 갱신된 위치는 `xBounds`(기본 `-40~10`), `zBounds`(기본 `0~50`)로 각각 클램프되어, 카메라가 지정된 영역 밖으로 나가지 않는다.

## 3. 줌 규칙 — `ZoomMouseWheel`

- 마우스 휠 스크롤 값을 읽어서, 0이면 아무 것도 하지 않는다.
- 현재 `cinemachineCamera.Lens.OrthographicSize`에서 `스크롤값 × zoomSpeed`(기본 2)를 뺀 값을 다음 크기로 계산한다 (휠을 올리면 축소/줌인, 내리면 확대/줌아웃).
- 계산된 크기는 `minZoomSize`~`maxZoomSize`(기본 `3`~`20`)로 클램프한 뒤 적용한다.

## 4. 신규 씬에 카메라 적용할 때 체크리스트

- [ ] `CinemachineCamera` 컴포넌트와 `cameraTarget` Transform 연결
- [ ] 이동 범위(`xBounds`/`zBounds`)를 씬의 맵 크기에 맞게 설정
- [ ] 이동 속도(`moveSpeed`)와 엣지 스크롤 감지 폭(`edgeSize`) 확인
- [ ] 줌 속도(`zoomSpeed`)와 줌 범위(`minZoomSize`/`maxZoomSize`) 확인
