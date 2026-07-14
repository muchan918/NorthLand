# 씬 작업 워크플로우

정본 씬을 어디에 두고, 개인이 어떻게 작업하고, 그 결과를 어떻게 정본에 반영하는지를 정하는 문서.
씬을 만들거나 고치는 사람은 반드시 이 절차를 따른다.

- 관련 이슈: **#65**
- 정본 위치: `Assets/Scenes/`
- 이 문서는 **현재 합의된 규칙**을 정리한 것이다. 규칙을 바꾼 사람은 이 문서도 함께 갱신해
  어긋나지 않게 유지한다.

## 1. 목적 · 핵심 원칙

스크립트는 `Assets/Scripts/`가 정본(시스템/공간 폴더, [CLAUDE.md](../../CLAUDE.md) 참고)이지만,
씬은 여러 사람이 동시에 같은 `.unity` 파일을 건드리면 바이너리 병합이 사실상 불가능하다.
그래서 씬은 **개인 복사본에서 작업 → 번호를 붙인 새 파일로 병합**하는 방식으로, 스크립트와
다른 절차를 쓴다.

- 정본 씬은 항상 `Assets/Scenes/TitleScene.unity` / `Assets/Scenes/GameScene.unity` **두 개뿐**이다.
- `Assets/Personal/<이름>/`은 씬 작업 중에만 쓰는 임시 복사본 저장소다. 여기서 만든 씬을 정본으로
  승격하지 않는다(항상 2번 규칙의 병합 절차를 거친다).

## 2. 정본 위치

- `Assets/Scenes/TitleScene.unity`, `Assets/Scenes/GameScene.unity` 두 파일만 정본이다.
- 개인 폴더(`Assets/Personal/<이름>/Scene/` 등)에 새 정본 씬을 만들지 않는다.

## 3. 개인 작업 시작

1. 작업을 시작하는 시점의 main 브랜치에서 정본 씬(최신 상태의 `TitleScene.unity`/`GameScene.unity`)을
   가져온다.
2. 그 씬을 자신의 `Assets/Personal/<이름>/Scene/`으로 복사한다.
3. 복사본에서 작업한다 — 정본 파일 자체는 건드리지 않는다.

## 4. 병합(정본 반영) 규칙 — 버전 누적 방식

개인 작업을 정본에 반영할 때 기존 `GameScene.unity`/`TitleScene.unity`를 **덮어쓰지 않는다**.

1. `Assets/Scenes/`에 번호를 붙인 새 파일로 추가한다: `GameScene_1.unity`, `GameScene_2.unity`,
   `TitleScene_1.unity` … (번호는 그 씬 종류의 이전 최고 번호 + 1)
2. Build Settings와 `GameSceneManager`(`Assets/Scripts/GameManager/GameSceneManager.cs`)가 참조하는
   "현재 활성 씬"을 방금 추가한 최신 번호 파일로 갱신한다.
3. 번호 없는 `TitleScene.unity`/`GameScene.unity`는 그 주의 마지막 정리(§5) 전까지는 갱신하지 않고
   그대로 둔다 — 매 병합마다 실제로 게임이 로드하는 대상은 최신 번호 파일이다.

## 5. 주간 빌드 시 정리

주간 빌드 시점에:

1. 그 주의 최종 상태(가장 최신 번호 파일)를 번호 없는 `TitleScene.unity`/`GameScene.unity`로 확정한다.
2. 그 주에 쌓인 번호 붙은 스냅샷 파일(`*_1`, `*_2`, …)은 모두 삭제한다.
3. 다음 주 병합은 다시 `_1`부터 시작한다.

## 6. WL-028과의 관계

WL-028(경영 공간 씬 정본 이원화, `Docs/Review/WatchList.md`)은 `GameSceneManager`가 정본이 아닌
복사본 씬(`ManageSpace-Sungsoo.unity`)을 부팅하던 문제다. `GameSceneManager`가 이 문서의 정본
씬(`TitleScene`/`GameScene`)을 부팅하도록 정리되면 WL-028을 재검토할 수 있다 — 코드 작업
(씬 이름 교체 + Build Settings 등록) 완료 후 `Docs/Review/WatchList.md`에서 상태를 갱신한다.

## 7. 미확정 / TODO

- [ ] 번호 붙은 스냅샷 파일이 여러 주에 걸쳐 쌓였을 때 git 히스토리/용량 관리 방안(예: 오래된
      스냅샷을 커밋 전에 정리할지) — 아직 합의 없음
- [ ] 같은 병합 사이클에서 두 명이 동시에 병합을 시도할 때(번호 충돌) 순서 규칙 — 아직 합의 없음
