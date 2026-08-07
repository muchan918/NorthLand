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
- **정본 파일 이름은 절대 안 바뀐다.** `GameSceneManager`(`Assets/Scripts/GameManager/
  GameSceneManager.cs`)와 Build Settings는 항상 고정된 이름 `TitleScene`/`GameScene`만 참조한다.
  번호 붙은 스냅샷 파일(§4)은 그 이름 자체를 부팅 대상으로 쓰지 않는다 — 병합 확정 시 정본
  파일 이름으로 덮어써서 반영한다. 이렇게 하면 씬을 병합할 때마다 소스 코드(활성 씬 상수)를
  고치고 재컴파일할 필요가 없고, 갱신을 빠뜨려 부트 씬이 옛 버전을 가리키는 사고(WL-028,
  §6 참고)도 구조적으로 발생하지 않는다.

## 2. 정본 위치

- `Assets/Scenes/TitleScene.unity`, `Assets/Scenes/GameScene.unity` 두 파일만 정본이다.
- 개인 폴더(`Assets/Personal/<이름>/Scene/` 등)에 새 정본 씬을 만들지 않는다.
- 병합용 스냅샷·브랜치 작업 씬(번호 파일)은 정본과 섞이지 않게 `Assets/Scenes/Branches/`에 모아 둔다(§4·§5) — 정리 시 폴더째 비울 수 있도록.

## 3. 개인 작업 시작

1. 작업을 시작하는 시점의 main 브랜치에서 정본 씬(최신 상태의 `TitleScene.unity`/`GameScene.unity`)을
   가져온다.
2. 그 씬을 자신의 `Assets/Personal/<이름>/Scene/`으로 복사한다.
3. 복사본에서 작업한다 — 정본 파일 자체는 건드리지 않는다.

## 4. 병합(정본 반영) 규칙 — 버전 누적 스냅샷 + 정본 덮어쓰기

개인 작업을 정본에 반영할 때는 **스냅샷 추가**와 **정본 확정**을 분리된 두 단계로 한다.
정본 파일 이름(`GameScene.unity`/`TitleScene.unity`)은 이 과정 내내 그대로 유지되고,
`GameSceneManager`/Build Settings가 참조하는 대상도 바뀌지 않는다 — 코드 수정은 없다.

1. **스냅샷 추가**: `Assets/Scenes/Branches/`에 번호를 붙인 새 파일로 그 시점 작업 결과를 추가한다:
   `GameScene_1.unity`, `GameScene_2.unity`, `TitleScene_1.unity` … (번호는 그 씬 종류의 이전
   최고 번호 + 1). 스냅샷·브랜치 작업 씬은 정본(`Assets/Scenes/` 직하)과 섞이지 않도록 전용 하위
   폴더 `Branches/`에 모아 둔다 — 정리 시 폴더째 비울 수 있다(§5). 이 시점에는 아직 게임이 이
   파일을 로드하지 않는다 — 리뷰·비교용 히스토리다.
2. **정본 확정**: 리뷰 후 이 스냅샷을 반영하기로 하면, 스냅샷 파일의 내용을 정본 파일 이름
   (`GameScene.unity`/`TitleScene.unity`)에 **그대로 덮어쓴다**(번호 없는 정본 파일을 스냅샷
   내용으로 교체). 정본 파일 이름 자체는 바뀌지 않으므로 `GameSceneManager`/Build Settings는
   아무것도 고칠 필요 없이 다음 로드부터 바로 새 내용을 읽는다.
3. 번호 붙은 스냅샷 파일은 정본 확정 이후에도 그 주 동안은 삭제하지 않고 히스토리로 남겨둔다
   (§5에서 일괄 정리).
4. **정본 씬 diff 셀프 체크**: 커밋 전에 `git diff -- Assets/Scenes/GameScene.unity`로 변경 범위를
   확인한다. 특히 작업과 무관한 `RectTransform` 구동값(`m_AnchorMin`/`m_AnchorMax`/
   `m_AnchoredPosition`/`m_SizeDelta`), TMP 글꼴 크기, Canvas 설정이 함께 재저장되지 않았는지
   검사하고 비의도 변경은 되돌린다.

   실제로 반복 관측되는 구동값 3종이다(2026-08-07 기준, 씬을 저장할 때마다 뜬다):
   TMP `m_fontSize`(오토사이징 캐시) · 슬라이더 `m_Value`의 부동소수 오차(`1` → `0.9999998`) ·
   **`Assets/Settings/FlatKit/PixelationSettings_PC.asset`의 `_PixelSize`**(씬 파일은 아니지만
   `PixelationZoomBinder`가 카메라 줌에서 매 프레임 몰아 쓰는 값이라 같이 뜬다 — 무해하므로
   `git checkout`으로 되돌린다).

   ⚠️ **씬 YAML 블록이 통째로 재정렬되면 `git diff`에 삭제(`-`)로 보인다.** 오브젝트가 사라진 것으로
   오인하기 쉬우니, 삭제가 대량으로 보이면 diff를 읽지 말고 **개수로 확인**한다:
   `grep -c '^--- !u!1 &'`(GameObject 수) · `grep -c '^--- !u!'`(전체 컴포넌트 수)를 `git show HEAD:<경로>`와
   비교하면 증감이 바로 드러난다. 큰 diff에서는 다음 명령으로 레이아웃 구동값을 제외한 변경을
   먼저 확인하되, 최종적으로는 제외하지 않은 전체 diff도 검토한다.

   ```powershell
   git diff -- Assets/Scenes/GameScene.unity |
     rg -v "m_AnchorMin|m_AnchorMax|m_AnchoredPosition|m_SizeDelta"
   ```

## 5. 주간 빌드 시 정리

주간 빌드 시점에:

1. 정본 파일(`TitleScene.unity`/`GameScene.unity`)은 이미 §4②(정본 확정)에서 매번 최신 상태로
   갱신돼 있으므로 별도로 확정할 것이 없다.
2. 그 주에 쌓인 스냅샷·브랜치 작업 씬은 `Assets/Scenes/Branches/` 폴더를 통째로 비워 삭제한다
   (개별 파일을 골라 지우지 않아도 되도록 이 폴더에 모아 둔다). 정본(`Assets/Scenes/` 직하
   `GameScene.unity`/`TitleScene.unity`)은 그대로 둔다.
3. 다음 주 병합은 다시 `_1`부터 시작한다.

## 6. WL-028과의 관계

WL-028(경영 공간 씬 정본 이원화, `Docs/Review/WatchList.md`)은 `GameSceneManager`가 정본이 아닌
복사본 씬(`ManageSpace-Sungsoo.unity`)을 부팅하던 문제다. `GameSceneManager`가 이 문서의 정본
씬(`TitleScene`/`GameScene`)을 부팅하도록 정리되면 WL-028을 재검토할 수 있다 — 코드 작업
(씬 이름 교체 + Build Settings 등록) 완료 후 `Docs/Review/WatchList.md`에서 상태를 갱신한다.

## 7. NavMesh 재베이크 — 소유자 · 순서 (#305)

경영 공간 NavMesh는 **정적 베이크**다. 베이크 결과는 `Assets/Scenes/GameScene/NavMesh-NavMesh.asset`
(약 758KB 바이너리) 한 덩어리이고, **씬 지오메트리를 건드리면 통째로 바뀐다.** diff로 검토할 수 없고
병합할 수도 없어서, 두 사람이 각자 베이크해 커밋하면 충돌 해결책이 "한쪽을 버리고 다시 굽기"뿐이다.

1. **재베이크 소유자는 한 번에 한 명이다.** 씬 지오메트리(지형·건물·다리·`NavMeshModifierVolume`)를
   바꿔 재베이크가 필요해지면 **먼저 알리고**, 그 사이 다른 사람은 같은 씬의 지오메트리를 만지지 않는다.
   지오메트리를 건드리지 않는 작업(UI·프리팹 배치·컴포넌트 값)은 굽지 말 것 — 안 구우면 이 파일은
   diff에 안 나온다.
2. **재베이크에는 `Assets/Imported` 체크아웃이 필요하다.** 보행면 대부분과 다리 프록시
   (`NavProxy_ChocolateBridge`)가 `@NorthLand/Prefabs/Management/CandyLand.prefab` 안에 있다.
   Imported 없이 구우면 **걸을 수 있는 면이 통째로 빠진 데이터**가 나오고, 그래도 에러는 안 난다.
3. **`NavMeshSurface`는 아직 Imported 프리팹이 소유한다**(WL-160). 그래서 "베이크 데이터를 갈아끼운다"는
   변경이 **본 저장소 diff에 보이지 않는다.** 두 저장소에 걸친 변경은 PR 본문에 **짝이 되는
   NorthLand-Imported 커밋 SHA와 머지 순서**를 반드시 적는다 — 순서가 어긋나면 다리가 조용히 끊긴다.
4. 커밋할 때는 `.asset`과 `.meta`를 **함께** 넣는다(CLAUDE.md의 `.meta` 규칙).

## 8. 미확정 / TODO

- [ ] 번호 붙은 스냅샷 파일이 여러 주에 걸쳐 쌓였을 때 git 히스토리/용량 관리 방안(예: 오래된
      스냅샷을 커밋 전에 정리할지) — 아직 합의 없음
- [ ] 같은 병합 사이클에서 두 명이 동시에 병합을 시도할 때(번호 충돌) 순서 규칙 — 아직 합의 없음
