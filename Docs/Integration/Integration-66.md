# Integration #66 — 경영 공간 + 전투 공간 통합, 밤/낮 이벤트 연동, 웨이브 성공/실패/보스 처치 임시 UI

muchan/n0wst4ndup/SUNGSOO 세 사람의 개인 작업 공간을 하나의 게임 씬으로 합치고, 낮/밤 루프가
실제로 손으로 검증 가능하도록 만든 통합 작업 기록.

- 관련 이슈: **#66** (이관: #65)
- 담당: 김유찬(muchan), Jaeseo Lee(n0wst4ndup)
- 이 문서는 **작업 시점의 스냅샷**이다. 이후 코드가 바뀌어도 이 문서를 소급 수정하지 않는다 —
  최신 구조는 `Docs/Core/DayNightManager.md`·`Docs/Review/SystemMap.md`를 따른다.

## 1. 배경

Combat의 실제 웨이브 클리어·보스 처치 판정이 아직 없는 상태에서, 경영 공간(자원·주민 배치)과
낮/밤 루프를 끝까지 손으로 돌려볼 방법이 없었다. 이 통합의 목적은 두 가지:

1. 흩어져 있던 경영 공간 프리팹·주민 배치 시스템·결과 UI를 한 씬(`GameScene.unity`)으로 합친다.
2. 밤 동안 결과(성공/실패/보스 처치)를 임의로 트리거할 수 있는 임시 버튼을 둬서, Combat 연동 전까지
   낮→밤→낮 전체 루프를 실제로 플레이해서 검증할 수 있게 한다.

## 2. 씬 통합

`Assets/Scenes/GameScene.unity`(정본, `Docs/Core/SceneWorkflow.md`)를 아래 세 소스에서 병합해 구성:

| 소스 | 가져온 것 |
|---|---|
| `Assets/Personal/muchan/Scene/ManageSpace.unity` | 건물/지형 프리팹, `DayNightManager`, `MouseManager`, Main Camera, Directional Light (베이스) |
| `Assets/Personal/n0wst4ndup/Management/scenes/ManagementSystem.unity` | `ManagementController` + `ManagementCanvas`(`ManagementPanelView`/`ProductionLineView`, 자원 HUD) |
| `Assets/Personal/SUNGSOO/Scene/ManageSpace-Sungsoo.unity` | `GameManager` + `ResultUI.prefab` 인스턴스만 (나머지는 베이스와 중복) |

중복 싱글톤(`DayNightManager`/`EventSystem`/`Main Camera`/`Directional Light`)은 muchan 베이스 것만 남기고
나머지 소스의 중복 오브젝트는 가져오지 않았다. `Assets/Scenes/TitleScene.unity`는 별도로 타이틀 화면
콘텐츠(시작 버튼 등)를 구성했다.

`GameSceneManager.cs`의 씬 이름 상수를 `SceneWorkflow.md` 정본 이름(`TitleScene`/`GameScene`)으로 교체하고,
`ProjectSettings/EditorBuildSettings.asset`에 두 씬을 `enabled: 1`로 등록했다(기존 `MainMenu.unity`/
`ManageSpace-Sungsoo.unity`는 `enabled: 0`으로 비활성화, 파일 자체는 보존). WL-028(씬 정본 이원화) 해소.

## 3. 자원 정산 시점 변경

- 기존: `ManagementController.HandleDayToNight()`(`OnDayToNight` 구독)가 낮→밤 전환 즉시 자원을 정산.
- 변경: 정산 로직을 `HandleNightToDay()`(`OnNightToDay` 구독)로 옮기고, 주민 배치 초기화보다 **먼저**
  실행되도록 순서를 고정. 즉 그 밤을 무사히 넘겨야(웨이브 성공) 그 밤 동안 배치한 주민 몫의 자원을 받는다.
- `ManagementController.RequestAdvancePhase()`는 이제 낮→밤(`EndDay()`)만 담당한다. 밤→낮(`EndNight()`)
  호출은 아래 4항의 "웨이브 성공" 버튼으로 이동했다(WL-018 갱신).

## 4. 밤 전용 임시 버튼 (`NightActionPanelView`)

`Assets/Scripts/ManagementSpace/UI/NightActionPanelView.cs`(신규) — 좌측 하단에 버튼 4개를 두고
페이즈에 따라 해당 버튼만 노출한다:

| 버튼 | 노출 시점 | 동작 |
|---|---|---|
| 낮 종료 | 낮 | `ManagementController.RequestAdvancePhase()` (기존 로직 재사용, 잉여 주민 게이트 포함) |
| 웨이브 성공 | 밤 | `DayNightManager.Instance.EndNight()` — 정산+초기화+WaveCount 증가, 증가 로그 출력 |
| 웨이브 실패 | 밤 | `GameManager.Instance.TriggerGameOver()`만 호출(`EndNight()` 호출 안 함 — 정산 없음) |
| 보스 처치 | 밤 | `GameManager.Instance.TriggerVictory()` |

게임오버/승리 UI는 새로 만들지 않고 기존 `GameManager`(#37/#56)·`ResultUIManager`·`ResultUI.prefab`을
그대로 재사용했다 — "타이틀로"/"다시 시작" 버튼도 이미 `GameSceneManager.LoadMainMenu()`/
`LoadManageSpace()`에 연결돼 있었다.

이 버튼들은 Combat의 실제 웨이브 클리어·보스 처치 판정이 생기기 전까지의 임시 대체물이다(WL-018).

## 5. 통합 중 발견·수정한 버그

씬을 옮겨 다닌 오브젝트에서 두 종류의 문제를 발견해 같이 고쳤다:

1. **`MouseManager._camera` 씬 재로드 시 유실**: `MouseManager`는 `DontDestroyOnLoad` 싱글톤이라
   `Awake()`가 최초 1회만 실행돼, 씬이 바뀌면 이전 씬의(파괴된) 카메라 참조를 계속 들고 있었다
   (`UnassignedReferenceException` 매 프레임 발생). `SceneManager.sceneLoaded` 구독으로 씬 로드마다
   `Camera.main`을 재바인딩하도록 수정하고, `RaycastMask()`에 `_camera == null` 가드를 추가했다.
2. **TitleScene "게임 시작" 버튼의 죽은 리스너**: 버튼의 `onClick`이 `MainMenuUI.OnClickStart`를 가리키고
   있었으나 씬에 `MainMenuUI` 컴포넌트 자체가 없어(대상 참조 `target=null`) 클릭해도 아무 반응이 없었다.
   `Canvas`에 `MainMenuUI`를 추가하고 버튼을 다시 연결해 해결.

두 경우 모두 "프리팹/오브젝트는 옮겨왔지만 그걸 참조하던 리스너·스크립트는 안 옮겨옴" 패턴이었다 —
씬 병합 작업에서 반복될 수 있는 함정이라 기록해둔다.

## 6. 갱신한 문서

- `Docs/Core/DayNightManager.md` — 이벤트 표·구독 예시·구현 현황·TODO를 새 정산 시점에 맞게 갱신
- `Docs/Review/SystemMap.md` — 팀 계약 #5, Management(Resource)/DayNightManager 시스템 행, 접점 매트릭스
- `Docs/Review/WatchList.md` — WL-018(트리거 주체 이동) 갱신, WL-028(씬 정본 이원화) RESOLVED,
  WL-032(GameManager 등 SystemMap 미등재) 신규 등록

## 7. 남은 것 / 알려진 제약

- WL-018: `NightActionPanelView`는 Combat 웨이브 클리어/보스 처치 판정이 생기면 제거 대상
- WL-032: `GameManager`/`ResultUIManager`/`GameSceneManager` 공개 API가 아직 `SystemMap.md`에 정식
  등재되지 않음(이번 통합에서는 발견·기록만 함)
- TitleScene의 메뉴 콘텐츠(디자인·연출)는 이번 범위에서 최소 동작(시작 버튼)만 확인 — 폴리싱은 별도 이슈
