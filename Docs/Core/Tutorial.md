# 튜토리얼 — 구조와 단계 추가 절차

> **기준 코드: #408(틀 구현) 시점.** 이 문서는 "어떻게 단계를 붙이는가"와 "왜 이 구조인가"를 함께 다룬다.
> 실제 안내 내용은 **#271**, UI 에셋·연출은 별도 후속 이슈다.
> 관련: [UIZOrder.md](UIZOrder.md)(캔버스 레이어) · [SceneWorkflow.md](SceneWorkflow.md)(씬 복사 규약) ·
> [DayNightManager.md](DayNightManager.md) · [MouseManager.md](MouseManager.md) ·
> 이슈 #408(틀) · #271(안내 내용) · WL-192

---

## 0. 30초 요약

**새 단계를 붙이는 비용은 "완료 조건 하나 + 단계 에셋 하나"다.** 진행 로직·화면 표시는 건드리지 않는다.

이미 있는 조건을 다른 값으로 쓰는 경우엔 **코드 0줄** — 인스펙터에서 단계 에셋 하나 만들고 드롭다운에서 고르면 끝이다.

| 상황 | 하는 일 |
|---|---|
| 이미 있는 조건을 다른 값으로 | 인스펙터만. **코드 0줄** ([§2.1](#21-케이스-a--코드-0줄)) |
| 새로운 종류를 기다려야 함 | 조건 파일 **1개** ([§2.2](#22-케이스-b--조건-파일-1개)) |
| 기존 조건이 너무 헐렁함 | 그 조건 파일에 필드 + `if` ([§2.3](#23-케이스-c--조건-좁히기)) |

**막혔다** → [§5](#5-막혔을-때--증상--원인) · **차단·강조를 붙이려 한다** → [§4](#4-아직-없는-것)를 먼저 읽을 것

---

## 1. 구조

파일은 전부 `Assets/Scripts/Tutorial/`에 있다.

```
TutorialStepAsset.cs      단계 1개의 안내 내용 (ScriptableObject)
TutorialController.cs     진행을 소유 — 지금 몇 단계인지 아는 유일한 곳
TutorialOverlay.cs        팝업·말풍선의 '표시'만 담당
TutorialCondition.cs      완료 조건의 공통 계약 (abstract)
TutorialContext.cs        조건이 쓰는 씬 참조 주소록
Conditions/
  BuildingActionCondition.cs   경영 공간 행동 감시
  PhaseChangedCondition.cs     낮/밤 전환 감시
```

에디터 지원 한 줄이 `Assets/Scripts/Editor/ManagedReferencePickerDrawer.cs` 맨 아래에 있다(`TutorialConditionDrawer`, [§3.3](#33-왜-serializereference인가)).

### 1.1 한 단계의 흐름

1. 컨트롤러가 리스트에서 지금 단계를 꺼낸다
2. 오버레이에 **팝업**을 띄우라고 시킨다
3. 확인 버튼이 눌리면 오버레이가 컨트롤러에 알린다(`PopupConfirmed`)
4. 컨트롤러가 **말풍선**을 띄우고, 그 단계의 **조건에게 감시를 시작**시킨다(`Begin`)
5. 조건이 "됐다"고 알리면(`Satisfied`) 감시를 끝내고(`End`) 다음 단계로

팝업과 말풍선은 각각 생략 가능하다. 팝업 문구가 비면 건너뛰고, **조건이 비면 확인만으로 다음 단계로 간다** — "설명만 하는 단계"가 그렇게 표현된다.

### 1.2 셋은 서로 모른다

이 구조의 전부다.

- **컨트롤러는 조건이 무엇을 감시하는지 모른다.** `TutorialController.cs`에 `BuildingActionCondition`이라는 이름조차 나오지 않는다 — `Satisfied` 통지만 받는다
- **오버레이는 지금이 몇 단계인지 모른다.** "이 문구를 띄워라"만 받는다. 확인이 눌리면 스스로 판단하지 않고 이벤트로 알리기만 한다
- **조건은 튜토리얼의 존재를 모른다.** 자기가 구독한 이벤트만 본다

그래서 조건이 100개가 돼도 컨트롤러와 오버레이는 안 고친다.

> ⚠ **"컨트롤러를 안 고친다"는 단계·조건을 늘릴 때의 얘기다.** 차단·강조 같은 **새 기능**을 붙일 때는 고친다([§4](#4-아직-없는-것)).

### 1.3 공개 API

```csharp
// TutorialController
public bool IsRunning { get; }
public void StartTutorial();
public void StopTutorial();

// TutorialOverlay
public event Action PopupConfirmed;
public void ShowPopup(string title, string body, Sprite image);
public void HidePopup();
public void ShowBubble(string text);
public void HideBubble();
public void HideAll();

// TutorialCondition (상속해서 구현)
public event Action Satisfied;
public abstract void Begin(TutorialContext context);
public abstract void End();
protected void Fire();
```

### 1.4 씬 구성

| 오브젝트 | 비고 |
|---|---|
| `TutorialCanvas` | Canvas `sortingOrder = 600`. 보상(500) 위, 설정(700) 아래 — [UIZOrder.md](UIZOrder.md) §3 |
| └ `Popup` | 화면 전체를 덮고 `Raycast Target`을 **켠다**. 팝업이 떠 있는 동안 뒤쪽 입력이 막히는 건 이 덕분이다 |
| └ `Bubble` | `Raycast Target`을 **반드시 끈다**(자식 텍스트도). 안 끄면 말풍선 뒤 오브젝트가 클릭되지 않는다 |
| `TutorialController` | `Overlay` 슬롯 + 단계 리스트 + `startOnPlay` 스위치 + `Debug Mode`/`Debug Steps`([§2.4](#24-단계-하나만-떼어내-돌려보기)) |

튜토리얼 시스템과 22개 단계는 정본 `Assets/Scenes/GameScene.unity`에 배치돼 있다. 작업용 복사본은
`Assets/Personal/muchan/Scene/TutorialTest2.unity`이며, 이후 정본 씬 변경은
[SceneWorkflow.md](SceneWorkflow.md) §4를 따른다.

단계 에셋은 `Assets/Resources/ScriptableObjects/Tutorial/`에 둔다.

### 1.5 진입·종료 계약

- `TutorialMode`는 씬을 로드하기 전에 활성화한다. 같은 `GameScene`을 사용하므로 튜토리얼 전용 씬은 없다.
- 새로하기는 현재 슬롯의 `PlayerData.tutorialCompleted`가 `false`일 때만 튜토리얼로 진입한다.
- 완료와 스킵은 모두 완료 상태를 슬롯별로 저장하고 일반 `GameScene`을 다시 로드한다. 저장 실패는
  오류로 남기되 일시정지와 단계 규칙을 정리하고 본 게임 전환은 계속한다.
- 시드 지정 새로하기가 튜토리얼을 거치면 `TutorialController`가 복귀 시드를 보관하고, 종료 후 본 게임에
  같은 마스터 시드를 다시 전달한다.
- `TutorialRelayUI`의 다시 보기는 경고 팝업 확인 후 현재 슬롯의 `run-save.json`을 삭제하고 튜토리얼을
  1일차부터 시작한다. 삭제에 실패하면 현재 게임을 유지한다.
- 튜토리얼 중 `DayNightManager.OnDayStart` 자동 저장은 건너뛴다. 튜토리얼 런은 이어하기 데이터의
  소유자가 아니다.
- `TutorialOverlay.SkipRequested`가 스킵 요청을 전달한다. 오버레이는 완료 기록이나 씬 전환을 직접
  처리하지 않는다.

---

## 2. 새 단계 추가하기

### 2.1 케이스 A — 코드 0줄

이미 있는 조건을 다른 값으로 쓰는 경우다. 예를 들어 "건물을 업그레이드하세요"는 `BuildingActionCondition`의 값만 `Upgraded`로 바꾸면 된다.

1. `Create > Tutorial > Step`으로 단계 에셋 생성
2. `NorthLand_Tutorial` String Table에 의미 기반 키와 ko-KR/en-US/ja-JP 문구 추가
3. 단계 에셋의 팝업 제목·본문·말풍선 슬롯에 해당 키 입력
4. `Completion` 드롭다운에서 조건 선택 → 그 아래 나타나는 값 설정
5. `TutorialController`의 `Steps` 리스트에 원하는 위치로 드래그

키는 단계 순서가 아니라 내용으로 짓는다(예: `tutorial.camera.drag.title` / `.body` / `.bubble`).
`tutorial.step01.*`처럼 순서를 키에 넣으면 단계 재배치 때 문구 소유권이 어긋난다. 빈 슬롯은 기존처럼 해당
팝업·말풍선을 생략한다.

**순서는 리스트의 등록 순서가 전부다.** 단계 에셋은 자기가 몇 번째인지 갖지 않는다(`MonsterWaveAsset`과 같은 규칙). 순서를 바꾸는 데 코드 수정이 필요 없어야 한다는 것이 #271의 요구사항이다.

### 2.2 케이스 B — 조건 파일 1개

기다릴 대상이 새로운 종류일 때다. `Conditions/` 아래에 파일 하나만 만든다.

```csharp
using System;
using NorthLand.Combat;
using UnityEngine;

// 이 단계가 시작된 뒤로 타워를 새로 배치하면 충족된다.
[Serializable]
public class TowerPlacedCondition : TutorialCondition
{
    [SerializeField]
    private int requiredCount = 1;

    private int _baseline;

    public override void Begin(TutorialContext context)
    {
        // 단계 시작 시점을 기준으로 잡는다 — 이미 깔려 있던 타워를 세면 즉시 통과해버린다.
        _baseline = Tower.Active.Count;
        Tower.ActiveChanged += OnActiveChanged;
    }

    public override void End()
    {
        Tower.ActiveChanged -= OnActiveChanged;
    }

    private void OnActiveChanged()
    {
        if (Tower.Active.Count - _baseline >= requiredCount)
        {
            Fire();
        }
    }
}
```

**다른 파일은 하나도 안 고친다.** 만들면 `Completion` 드롭다운에 자동으로 나타나고, `requiredCount` 칸도 Unity가 알아서 그린다([§3.3](#33-왜-serializereference인가)).

#### 구독할 수 있는 이벤트

| 행동 | 이벤트 | Context 필요? |
|---|---|---|
| 주민 배치·회수 · 건물 업그레이드 · 주민 수 증가 | `ManagementController.OnBuildingAction` | ✅ |
| 낮→밤 / 밤→낮 | `DayNightManager.OnDayToNight` / `OnNightToDay` | ❌ `.Instance` |
| 낮 시작(1일차 부트스트랩 포함) | `DayNightManager.OnDayStart` | ❌ `.Instance` |
| 타워 배치·제거 | `Tower.ActiveChanged` (**static**) | ❌ |
| 스킬 착탄 | `SkillManager.ImpactResolved` | ❌ `.Instance` |
| 웨이브 클리어 | `MonsterSpawn.WaveCleared` | ✅ |
| 자원 변동 | `ResourceWallet.OnChanged` | ✅ |
| 선택·호버·드래그 | `MouseManager`의 각 이벤트 | ❌ `.Instance` |

**`TutorialContext`에는 "씬을 뒤져야 찾을 수 있는 것"만 담는다.** `static Instance`가 있거나 이벤트 자체가 `static`이면 조건이 직접 쓴다. 새로 담을 게 생기면 `TutorialContext`에 프로퍼티 한 줄을 추가한다 — 그 파일 외에는 안 고친다.

> **행동 단계에 타이머 금지.** 플레이어가 무언가를 하기를 기다리는 단계의 완료 판정은 각 시스템이 이미 가진 이벤트를 구독해서 한다. 시간으로 넘기면 아무것도 하지 않아도 통과되므로 그 단계의 학습이 사라진다(#271 요구사항).
>
> **예외 — 연출 간격.** 팝업도 말풍선도 없이 다음 안내가 뜨는 시점만 미루는 단계는 `DelayCondition`으로 시간을 쓴다. 가르치는 것이 없으므로 위 근거가 적용되지 않는다. 예: 낮→밤 전환 직후 몬스터가 걸어 나오는 것을 잠깐 보여준 뒤 스킬 안내를 띄우는 간격.
>
> 판별 기준은 하나다 — **말풍선이 있으면 타이머를 쓰지 않는다.**

#### 아직 이벤트가 없는 것

붙이려면 해당 시스템에 통지를 먼저 추가해야 한다.

- **웨이브 시작** — 통지가 없다. `MonsterSpawn.StartRound()`를 호출하는 곳(`CombatMapMonsterConnector` · `StageBuilder`)이 있을 뿐 아무것도 알리지 않는다. `OnDayToNight`로 우회한다
- **타워 합성 확정** — 전용 이벤트가 없다. `TowerMergeCoordinator.OnGroupChanged`는 선택 집합 변경이라 확정이 아니다. `Tower.ActiveChanged` + `CommandHistory.OnChanged`로 간접 관측해야 한다
- **보상 선택 완료** — `WaveRewardSelectionUI.SelectRewardAsync`가 UniTask를 반환할 뿐이라 훅을 하나 추가해야 한다

### 2.3 케이스 C — 조건 좁히기

기존 조건이 너무 헐렁할 때다. `BuildingActionCondition`은 지금 **아무 건물에서든** 주민 배치가 일어나면 통과한다. 말풍선이 "설탕 농장에 주민을 넣으세요"라면 안내와 판정이 어긋난다.

그 조건 파일에 필드 하나와 검사 한 줄을 더한다.

```csharp
[SerializeField]
private BuildingAsset targetBuilding;   // 비우면 아무 건물이나 통과
```

```csharp
if (targetBuilding != null && building != targetBuilding)
{
    return;
}
```

**기존 단계는 그 칸이 비어 있으므로 동작이 그대로다.** 메서드를 새로 만들지 않는다.

### 2.4 단계 하나만 떼어내 돌려보기

뒤쪽 단계를 고칠 때마다 앞 단계를 전부 통과하는 것은 비용이다. `TutorialController`의
**`Debug Mode`를 켜면 `Steps` 대신 `Debug Steps`만 진행한다.** 보고 싶은 단계 에셋을 그 리스트에
넣으면 그것부터 시작한다.

- 진행할 리스트는 `StartTutorial`에서 **한 번 확정한다.** 진행 도중 스위치를 뒤집으면 인덱스가
  다른 리스트를 가리켜 엉뚱한 단계로 뛰기 때문이다(`MonsterSpawnWaveProvider.isTutorialRun`과 같은 규칙).
- 켜져 있으면 시작 시 콘솔에 경고를 남긴다 — 단계가 통째로 안 보이는 것을 버그로 오해하기 쉬운 자리다.
- **`Steps`는 건드리지 않는다.** 정식 순서는 그대로 남으므로 스위치만 끄면 원래대로 돌아온다.

⚠ 게임의 다른 상태를 만들어 주지는 않는다. 밤 단계를 이 방식으로 띄우면 시작은 낮이므로
`SkillUsedCondition`처럼 밤을 요구하는 조건(`SkillManager.CanCast`)은 충족되지 않는다 —
필요한 앞 단계(예: `DayEnd`)를 `Debug Steps`에 같이 넣어야 한다.

---

## 3. 조건 작성 규칙

### 3.1 계약

`Begin`에서 걸고, `End`에서 풀고, 조건이 맞으면 `Fire()`. 그게 전부다.

`Fire()`가 `protected`인 이유는 **외부에서 조건을 강제로 충족시키지 못하게** 하기 위해서다.

### 3.2 반드시 지킬 것

| 규칙 | 안 지키면 |
|---|---|
| `Begin`에서 건 구독을 `End`에서 **전부** 푼다 | 지나간 단계의 조건이 계속 듣고 있다가 엉뚱한 때 발화한다 |
| 상태를 가진 조건은 `Begin`에서 **초기화**한다 | 조건 객체는 에셋에 저장된다 — 이전 플레이의 값이 남아 시작하자마자 통과한다 |
| 매개변수 없는 생성자를 남긴다 | `Completion` 드롭다운 후보에서 빠진다 |
| **클래스 이름·네임스페이스를 바꾸지 않는다** | `[SerializeReference]`가 타입 이름으로 저장하므로 **기존 단계 에셋의 조건이 통째로 날아간다** |

구독을 어느 쪽에 걸었는지 기억하지 말고 `End`에서 관련된 것을 다 푸는 편이 안전하다 — 안 걸린 이벤트를 `-=` 하는 것은 무해하다(`PhaseChangedCondition`이 그렇게 한다).

`Begin`에서 대상을 못 찾았으면 **경고 로그를 남긴다.** 조용히 넘어가면 "왜 단계가 안 넘어가지"로 한참 헤맨다.

### 3.3 왜 `[SerializeReference]`인가

`TutorialCondition`은 추상 타입이라 일반 `[SerializeField]`로는 **저장 자체가 안 된다.** `[SerializeReference]`는 실제 타입 이름까지 함께 저장해서 추상 타입 칸에 아무 자식이나 담을 수 있게 한다.

Unity는 **단일 필드**의 managed reference에는 타입 선택 UI를 그리지 않는다. 그래서 `Assets/Scripts/Editor/ManagedReferencePickerDrawer.cs` 맨 아래의 `TutorialConditionDrawer` 한 줄이 드롭다운을 붙인다 — `ProjectileFlight` · `HitEffect` · `TargetingPolicy`가 쓰는 것과 **같은 도구**다.

그 드로어는 기반 타입을 런타임에 읽어 후보를 찾고, 자식 필드는 Unity 기본 그리기에 넘긴다. **그래서 새 조건을 만들어도 드로어를 안 고친다.**

조건을 `ScriptableObject`로 만드는 방법도 있으나 택하지 않았다. SO는 공유 인스턴스라 런타임 상태가 에셋에 남고, 같은 조건 에셋을 두 단계가 쓰면 상태가 엉킨다(`Instantiate` 복사본을 쓰면 개념이 하나 더 는다). 단계마다 자기 조건 인스턴스를 인라인으로 갖는 지금 방식이 그 함정을 피한다.

---

## 4. 아직 없는 것

자동 진입·완료 기록·스킵·정본 씬 배치는 #479에서 구현됐다. 아래 항목은 후속 범위다.

| 없는 것 | 비고 |
|---|---|
| 딤 · 강조 · 대상 외 입력 차단 | **팝업 구간은 이미 막힌다**(`Popup`이 전체 화면 + `Raycast Target`). 말풍선 구간이 안 막힌다 |
| 다국어 | `NorthLand_Tutorial`(ko-KR/en-US/ja-JP) 적용 완료. 단계 SO는 의미 기반 키를 저장하고 표시 시 현재 로케일로 조회한다. 진행 중 로케일 변경은 `TutorialController`가 현재 팝업·말풍선을 다시 그리며, 확인·스킵·재진입 팝업의 정적 문구는 `LocalizeStringEvent`가 갱신한다 |
| 팝업 구간 일시정지 | `GamePauseReason.Tutorial` 추가로 방향은 정해짐. ⚠ **`Time.timeScale = 0`이어도 `MouseManager.Update()`는 계속 돈다** — 일시정지는 입력을 막지 않는다 |
| UI 에셋 · 연출 | 지금은 회색 박스 + 기본 텍스트. 강조 색은 기존 아웃라인(호버 노랑 · 선택 초록 · 합성 핑크)과 겹치지 않게 할 것 |

### 차단·강조를 붙일 때 — 먼저 확인할 것

⚠ **`MouseManager`는 `EventSystem.IsPointerOverGameObject()` 하나만 보고 "UI 위냐"를 판정한다.** 화면을 덮으면 강조한 타일·타워조차 클릭되지 않는다. #271 본문이 *"대상만 조작 가능은 딤에 구멍을 뚫어서 되지 않는다"* 고 적은 근거가 이것이다.

딤 `Graphic`에 `ICanvasRaycastFilter`를 구현해 허용 영역에서 `IsRaycastLocationValid`를 `false`로 돌리면 그 구멍에서 판정이 뒤집힐 것으로 보이나, **아직 검증되지 않았다.**

안 되면 `MouseManager`에 런타임 입력 게이트를 넣어야 한다. 그 파일은 선택·배치·스킬 조준·유닛 드래그가 모두 매달려 있어 범위가 크게 달라진다.

**그러므로 차단 작업은 이 검증부터 하고 나머지를 쌓는다.** 코드를 다 쌓은 뒤에 알면 되돌릴 것이 많다.

붙일 때 고칠 곳은 넷이다.

| 파일 | 무엇을 |
|---|---|
| `TutorialStepAsset` | 가드 모드(차단/강조만/안내만) + 강조 대상 목록 필드 |
| `TutorialOverlay` | 딤 + 구멍 뚫기 |
| `TutorialController` | 단계 진입 시 허용 대상 전달, 이탈 시 해제 |
| 신규 | `TutorialAnchor` — 씬 UI에 붙여 "이게 강조 대상"이라 표시. **SO는 씬 오브젝트를 참조할 수 없으므로 문자열 ID 간접 참조가 강제된다** |

---

## 5. 막혔을 때 — 증상 → 원인

| 증상 | 원인 |
|---|---|
| `Completion` 칸에 드롭다운이 안 보인다 | `ManagedReferencePickerDrawer.cs`에 `TutorialConditionDrawer`가 없다 |
| 드롭다운에 내가 만든 조건이 안 뜬다 | `[Serializable]`이 없거나, `abstract`이거나, 매개변수 없는 생성자가 없다 |
| 팝업 확인을 눌러도 아무 일이 없다 | 그 단계에 조건도 말풍선도 없으면 그냥 지나간다. 콘솔 로그로 진행 상황을 본다 |
| 행동해도 단계가 안 넘어간다 | `Completion`이 비었거나, 조건이 대상을 못 찾았다(콘솔에 경고가 찍힌다) |
| 시작하자마자 단계를 통과해버린다 | 조건이 상태를 `Begin`에서 초기화하지 않는다 |
| 말풍선 뒤 오브젝트가 클릭되지 않는다 | `Bubble`(또는 자식 텍스트)의 `Raycast Target`이 켜져 있다 |
| 등록한 단계가 통째로 안 뜬다 | `Debug Mode`가 켜져 있다 — `Debug Steps`만 돈다([§2.4](#24-단계-하나만-떼어내-돌려보기)). 시작 시 콘솔에 경고가 찍힌다 |
| 이전 단계의 조건이 또 발화한다 | `End`에서 구독을 안 풀었다 |
| 단계 에셋의 조건이 통째로 비어 있다 | 조건 클래스 이름을 바꿨다([§3.2](#32-반드시-지킬-것)) |
| 1일차 낮 신호를 놓친다 | `DayNightManager.OnDayStart`는 `Start()`에서 한 프레임 지연 후 최초 발행된다 |
