# CLAUDE.md

이 문서는 Claude Code(claude.ai/code)가 이 저장소에서 코드 작업을 할 때 참고하는 가이드다.

## 프로젝트 개요

NorthLand: Last Stand(팀 유유아)는 Unity로 개발 중인 그리드 기반 타워 디펜스 + 마을 경영 하이브리드 게임으로, 로그라이크 요소를 포함한다. 전체 게임 디자인은 `Docs/GDD.md`에 문서화되어 있다 — 디자인이나 게임플레이 로직 관련 결정을 내리기 전에 반드시 먼저 읽을 것. 핵심 시스템(낮/밤 루프, 자원 흐름, 영토 확장, 유닛 배치)이 해당 문서에 명세되어 있으며, 일부 메커니즘은 아직 미결 이슈/TODO로 표시되어 있다.

핵심 루프: 낮 페이즈(경영 — 자원 채집, 주민 배치, 생산 건물 건설, 타워 설치) → 밤 페이즈(수비 — 타워 자동 공격, 병사의 웨이포인트 저지, 플레이어의 지정 스킬 시전) → 웨이브 종료 보상 선택 → 반복. 독립적으로 확장 가능한 두 공간: 경영 공간과 전투 공간.

## 프로젝트 현재 상태

1차 통합 완료: 각 팀원의 `Assets/Personal/<이름>/`에 흩어져 있던 스크립트가 공용 `Assets/Scripts/`로 합쳐졌고, 담당자 이름이 아니라 **공간/시스템 단위** 폴더로 재편됐다(`CombatSystem`, `CombatSpace`, `ManagementSpace`, `GameManager`, `Data`, `DayNight`, `Camera`, `Monster`, `Editor`, `Test` 등). 현재 시스템: DataTable CSV 파이프라인, 전투 타워/적 데미지 코어(CombatSystem), 절차적 전투맵 빌더(CombatSpace/MapBuilder), 마우스 입력/선택/배치 매니저 + 로컬라이제이션(GameManager/MouseManager 등). `Docs/Review/SystemMap.md`가 시스템, 담당자, 공개 API, 통합 계약, 정확한 경로의 최신 지도다. 코드를 추가할 때는 메커니즘을 임의로 고안하지 말고 먼저 `Docs/GDD.md`에서 의도된 시스템 설계를 확인할 것.

## 툴링

- Unity Editor 버전: 6000.3.15f1 (`ProjectSettings/ProjectVersion.txt` 참조 — 정확히 이 버전으로 열고 빌드할 것).
- 렌더 파이프라인: Universal Render Pipeline(URP). PC용과 Mobile용 렌더러/파이프라인 에셋이 `Assets/Settings`에 분리되어 있다. 렌더러 피처·퀄리티·파이프라인 설정을 변경할 때는, 작업이 명시적으로 특정 플랫폼 대상이 아닌 한 **PC와 Mobile 에셋 양쪽 모두**에 적용할 것.
- 입력: 신형 Input System(`InputSystem_Actions.inputactions`). 레거시 `UnityEngine.Input` API(`Input.GetAxis`, `Input.GetKey` 등)는 절대 사용하지 말 것 — Active Input Handling이 Input System Package 단독으로 설정되어 있다.
- IDE 연동: `com.unity.ide.visualstudio` / `com.unity.ide.rider`를 통한 Visual Studio / Rider 연동. `.vscode/`에 Unity 디버깅 어태치 설정이 되어 있음(`dotnet.defaultSolution` = `NorthLand.slnx`).
- **Unity Editor 제어 (CLI)**: [unity-cli](https://github.com/youngwoocho02/unity-cli)가 연동되어 있다(커넥터 패키지는 `Packages/manifest.json`에 등록). **에디터가 실행 중일 때** 컴파일 확인, 콘솔 로그, EditMode/PlayMode 테스트, 임의 에디터 C# 실행(`exec`), 스크린샷, 에셋 reserialize를 모두 터미널에서 사용할 수 있다. Unity 관련 작업(씬, 프리팹, 에셋, 컴파일/테스트 검증) 전에 반드시 `Docs/Tools/unity-cli-guide.md`를 읽고 해당 워크플로우를 따를 것.
- 빌드: 플레이어 빌드는 팀이 Unity Editor에서 직접 수행한다 — 명시적으로 요청받지 않는 한 빌드를 트리거하지 **말 것**. 헤드리스 CI 파이프라인은 아직 없다. 이후 `Unity -batchmode`가 도입되더라도 에디터가 프로젝트를 열고 있는 동안에는 실행할 수 없음에 유의(프로젝트당 인스턴스 1개).
- 테스트: `com.unity.test-framework`가 설치되어 있고 테스트는 `unity-cli test`(EditMode/PlayMode)로 실행한다. 단 **아직 테스트가 하나도 없다** — 테스트가 추가되기 전까지 테스트 스위트를 검증 근거로 삼지 말 것.
- `.meta` 파일: 에셋에 딸린 `.meta`의 GUID를 손으로 수정하거나 재생성하지 말 것. 에디터 밖(터미널/스크립트)에서 파일을 새로 만들었다면 `unity-cli editor refresh`를 실행해 Unity가 `.meta`를 생성하게 한 뒤, 커밋 시 에셋과 `.meta`를 **반드시 함께** 포함할 것. (`.meta` 누락이나 고아 `.meta`는 다른 팀원 환경에서 레퍼런스 깨짐으로 나타난다)

## 저장소 컨벤션

- `Assets/Scripts/` — 스크립트 정본 위치. 담당자 이름이 아니라 공간/시스템 폴더(`CombatSystem`, `CombatSpace`, `ManagementSpace`, `GameManager`, `Data`, `DayNight`, `Camera`, `Monster`, `Editor`, `Test` 등, 정확한 목록은 `Docs/Review/SystemMap.md`)로 구성된다. 새 스크립트는 여기서 직접 작업하며, 통합 합의를 기다릴 필요 없이 해당 시스템 폴더에 바로 생성한다. **에이전트도 동일 규칙 적용**: 새 코드는 대상 시스템 폴더에 생성하고, 어느 폴더인지 불명확하면 사용자에게 확인할 것.
- `Assets/Personal/<이름>/` — 팀원별 개인 작업 폴더(현재 `muchan`, `SUNJIN`, `SUNGSOO`, `n0wst4ndup`). 스크립트가 아닌 **씬(Scene) 등 WIP 에셋** 전용으로 계속 쓰인다. 씬 작업 시 정본 위치·개인 복사·버전 누적 병합·주간 정리 규칙은 `Docs/Core/SceneWorkflow.md`를 따를 것.
- PR 리뷰(자동/수동): `Docs/Review/SystemMap.md`(시스템 맵, 통합 계약, 담당자 매트릭스)와 `Docs/Review/WatchList.md`(반복 이슈 대장, WL-번호)를 리뷰 기준선으로 따른다. 문서-코드 일치 여부는 리뷰 기준이 **아니다** — 팀은 코드에 맞춰 문서를 갱신하므로, 문서+코드 세트 자체가 올바른 방향인지를 판단할 것.
- `Assets/Imported/` — 내부에 자체 중첩 git 저장소를 포함한다. 벤더링된 외부 에셋 소스로 취급하고, 일반 기능 작업의 일부로 편집하지 말 것.
- `Assets/TutorialInfo/`와 `Assets/Readme.asset`은 URP 템플릿 기본 Readme 창의 잔재 — 게임의 일부가 아니다.