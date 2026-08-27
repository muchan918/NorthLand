# 튜토리얼 — 구조와 단계 추가 절차

> **기준 코드: #526(안내 이미지·접근 제한·본진 재사용 보강) 시점.** 이 문서는 "어떻게 단계를 붙이는가"와
> "왜 이 구조인가"를 함께 다룬다. 실제 안내 문구의 정본은 `NorthLand_Tutorial` String Table이다.
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

**막혔다** → [§6](#6-막혔을-때--증상--원인) · **행동 게이트를 설정한다** → [§4](#4-행동-게이트와-단계-규칙)를 먼저 읽을 것

---

## 1. 구조

파일은 전부 `Assets/Scripts/Tutorial/`에 있다.

```
TutorialStepAsset.cs      단계 1개의 안내 내용 (ScriptableObject)
TutorialController.cs     진행을 소유 — 지금 몇 단계인지 아는 유일한 곳
TutorialOverlay.cs        팝업·말풍선의 '표시'만 담당
TutorialAction.cs         단계가 허용할 수 있는 행동 플래그
TutorialInputGate.cs      실행·표시 게이트와 낮 종료 타워 요구치
HideDuringTutorial.cs     튜토리얼 중 전용 UI 오브젝트 숨김
TutorialCondition.cs      완료 조건의 공통 계약 (abstract)
TutorialContext.cs        조건이 쓰는 씬 참조 주소록
Conditions/
  BuildingActionCondition.cs   경영 공간 행동 감시
  PhaseChangedCondition.cs     낮/밤 전환 감시
  TowerCountCondition.cs       현재 보유 타워 수 감시
  ResidentDragAssignedCondition.cs 주민 드래그 성공 감시
```

에디터 지원 한 줄이 `Assets/Scripts/Editor/ManagedReferencePickerDrawer.cs` 맨 아래에 있다(`TutorialConditionDrawer`, [§3.3](#33-왜-serializereference인가)).

### 1.1 한 단계의 흐름

1. 컨트롤러가 리스트에서 지금 단계를 꺼낸다
2. 팝업이 있으면 실행 게이트를 닫고, 오버레이에 **팝업**을 띄우라고 시킨다
3. 확인 버튼이 눌리면 오버레이가 컨트롤러에 알린다(`PopupConfirmed`)
4. 컨트롤러가 단계의 `Allowed Actions`를 적용하고 **말풍선**을 띄운 뒤, **조건에게 감시를 시작**시킨다(`Begin`)
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
public event Action SkipRequested;
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
| └ `PopupInputBlocker` | 화면 전체 투명 `Image`. 팝업이 떠 있는 동안 다른 Canvas의 클릭을 막는다 |
| └ `Popup` | 안내 제목·본문·이미지·확인 버튼. 입력 차단은 별도 `PopupInputBlocker`가 담당한다 |
| └ `Bubble` | `Raycast Target`을 **반드시 끈다**(자식 텍스트도). 안 끄면 말풍선 뒤 오브젝트가 클릭되지 않는다 |
| 　└ `TutorialBubbleLayout` | 문장 길이에 맞춰 Bubble 크기를 1회 계산한다. 텍스트 안전 영역은 `BubbleText` RectTransform의 Anchor·`sizeDelta`가 정본이다 |
| `TutorialController` | `Overlay` 슬롯 + 단계 리스트 + `startOnPlay` 스위치 + `Debug Mode`/`Debug Steps`([§2.4](#24-단계-하나만-떼어내-돌려보기)) |

튜토리얼 시스템과 25개 단계는 `Assets/Prefabs/Tutorial/TutorialSystem.prefab`에 등록되어 정본
`Assets/Scenes/GameScene.unity`에서 사용된다. 작업용 복사본은
`Assets/Personal/muchan/Scene/TutorialTest3.unity`이며, 이후 정본 씬 변경은
[SceneWorkflow.md](SceneWorkflow.md) §4를 따른다.

단계 에셋은 `Assets/Resources/ScriptableObjects/Tutorial/`에 둔다.

### 1.5 진입·종료 계약

- `TutorialMode`는 씬을 로드하기 전에 활성화한다. 같은 `GameScene`을 사용하므로 튜토리얼 전용 씬은 없다.
- 새로하기는 현재 슬롯의 `PlayerData.tutorialCompleted`가 `false`일 때만 튜토리얼로 진입한다.
- 완료와 스킵은 모두 완료 상태를 슬롯별로 저장하고 일반 `GameScene`을 다시 로드한다. 저장 실패는
  오류로 남기되 일시정지와 단계 규칙을 정리하고 본 게임 전환은 계속한다.
- 튜토리얼 맵은 씬 로드 전에 `TutorialMode.MasterSeed`를 `GameSceneManager`의 기존 일회성 시드
  핸드오프에 넣어 생성한다. `RunBootstrapper`는 튜토리얼을 알지 않고 일반 시드 소비 경로를 그대로 사용한다.
- 시드 지정 새로하기가 튜토리얼을 거치면 입력 시드는 복귀용으로만 보관하고, 종료 후 본 게임에 전달한다.
- `TutorialRelayUI`의 다시 보기는 경고 팝업 확인 후 현재 슬롯의 `run-save.json`을 삭제하고 튜토리얼을
  1일차부터 시작한다. 선택 슬롯이 없으면 삭제할 Run이 없는 것으로 처리하며, 실제 삭제에 실패하면 현재 게임을 유지한다.
- 튜토리얼 중 `DayNightManager.OnDayStart` 자동 저장은 건너뛴다. 튜토리얼 런은 이어하기 데이터의
  소유자가 아니다.
- 타워 도감은 튜토리얼 중 진입할 수 없다. 버튼 오브젝트는 `HideDuringTutorial`로 숨기고,
  `FusionTowerCodexUI.Open()`도 `TutorialMode.IsActive`를 다시 검사해 외부 호출을 차단한다.
- `TutorialOverlay.SkipRequested`가 스킵 요청을 전달한다. 오버레이는 완료 기록이나 씬 전환을 직접
  처리하지 않는다.

### 1.6 튜토리얼 전용 런 규칙

튜토리얼 런 여부의 단일 정본은 `TutorialMode.IsActive`다. 정식 진입은 씬 로드 전에 `Enter()`하고,
작업 씬 직접 실행은 실행 순서가 빠른 `TutorialController.Awake`가 `startOnPlay`를 보고 먼저 `Enter()`한다.
초기 자원·적 체력·스킬 쿨다운 등 소비 시스템은 컨트롤러나 웨이브 공급자를 탐색하지 않고 이 값만 읽는다.
정식 `GameScene` 프리팹의 `startOnPlay`는 꺼져 있어 일반 Run에 아래 값이 적용되지 않는다.

| 항목 | 값 | 정본 |
|---|---:|---|
| 마스터 시드 | `15416` | `TutorialMode.MasterSeed` |
| 초기 비스켓(`ResourceKind.Wood`) | `20` | `TutorialMode.InitialBiscuit` |
| 초기 초콜릿·설탕 | `0` | `ManagementController.BuildModel`의 튜토리얼 분기 |
| 적 체력 | 일반 계산 결과의 `50%`(보스 포함) | `TutorialMode.EnemyHpScale` |
| 스킬 재충전 간격 | `3초` | `TutorialMode.SkillCooldownSeconds` |

절차 맵이 다시 구성될 때 `MonsterSpawn`은 이미 존재하는 `PlayerBase.Instance`를 본진으로 재사용한다.
같은 런에서 본진을 중복 생성하면 싱글톤 참조와 `TutorialSafety`의 무적 구독 대상이 바뀔 수 있으므로,
본진의 생명주기는 맵 재구성보다 길게 유지한다.

`MonsterSpawnWaveProvider.forceTutorialWaves`는 **튜토리얼 웨이브 구성만** 확인하는 에디터 테스트 옵션이다.
이 옵션만 켠 경우 초기 자원·적 체력·스킬 쿨다운·튜토리얼 UI는 바뀌지 않는다. 전체 튜토리얼 규칙까지
검증하려면 `TutorialController.startOnPlay`를 사용한다.

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
> **예외 — 연출 간격과 전환 통지.** 팝업도 말풍선도 없이 다음 안내가 뜨는 시점만 미루는 단계는 `DelayCondition`으로 시간을 쓴다. 가르치는 것이 없으므로 위 근거가 적용되지 않는다. 예: 낮→밤 전환 직후 몬스터가 걸어 나오는 것을 잠깐 보여준 뒤 스킬 안내를 띄우는 간격. 또한 튜토리얼 완료처럼 **플레이어 행동을 요구하지 않고** 다음 화면으로 자동 전환하는 통지 말풍선도 사용할 수 있다. 이때는 입력을 모두 제한하고 `ignoreGameSpeed`를 켜 실제 시간으로 센다.
>
> 판별 기준은 하나다 — **플레이어 행동을 요구하는 말풍선에는 타이머를 쓰지 않는다.**

#### 아직 이벤트가 없는 것

붙이려면 해당 시스템에 통지를 먼저 추가해야 한다.

- **웨이브 시작** — 통지가 없다. `MonsterSpawn.StartRound()`를 호출하는 곳(`CombatMapMonsterConnector` · `StageBuilder`)이 있을 뿐 아무것도 알리지 않는다. `OnDayToNight`로 우회한다
- **보상 선택 완료** — `WaveRewardSelectionUI.SelectRewardAsync`가 UniTask를 반환할 뿐이라 훅을 하나 추가해야 한다

타워 합성 확정은 `TowerFusionController.Fused`가 제공한다. `TowerMergedCondition`은 재료를 선택하거나
소모한 순간이 아니라 합성 결과 타워의 배치가 확정됐을 때 이 이벤트로 완료된다.

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

### 2.5 현재 25단계 정본

순서는 `TutorialSystem.prefab`의 `TutorialController.Steps` 등록 순서가 정본이다. 아래 표의 팝업 여부는
제목·본문·이미지 중 하나라도 설정됐는지를 뜻한다. 문구 자체는 SO가 저장한 의미 기반 키를 통해
`NorthLand_Tutorial` String Table에서 조회한다.

| # | 단계 에셋 | 팝업 | 완료 조건 | 핵심 단계 규칙 |
|---:|---|:---:|---|---|
| 1 | `CameraKeyboard` | X | `CameraMovedCondition` | 카메라 이동만 |
| 2 | `CameraDrag` | X | `CameraMovedCondition` | 카메라 이동만 |
| 3 | `CameraZoomOut` | X | `CameraMovedCondition` | 카메라 이동만 |
| 4 | `VillagerAssign` | O | `AllVillagersAssignedCondition` | 카메라, 주민 +/- |
| 5 | `TowerSelect` | O | `TowerSelectedCondition` | 카메라, 배치할 타워 선택 |
| 6 | `BuffTileIntro` | O | 없음(확인 후 즉시 진행) | 팝업 외 입력 없음 |
| 7 | `TowerPlace` | X | `TowerPlacedCondition` | 카메라, 타워 선택·배치 |
| 8 | `UndoIntro` | O | 없음(확인 후 즉시 진행) | 팝업 외 입력 없음 |
| 9 | `DayEnd` | X | `PhaseChangedCondition`(밤) | 카메라, 타워 선택·배치, Undo, 낮 종료; 타워 최소 1개 |
| 10 | `SkillIntro` | X | `DelayCondition` | 카메라, 스킬; 등장 연출 대기 |
| 11 | `SkillUse` | O | `SkillUsedCondition` | 팝업 동안만 정지, 확인 후 카메라·스킬 허용 |
| 12 | `NextDay` | X | `PhaseChangedCondition`(낮) | 카메라, 스킬 |
| 13 | `BuildingSelectIntro` | X | `DelayCondition`(1.5초) | 카메라만 |
| 14 | `ShortcutBarIntro` | O | `BuildingShortcutUsedCondition` | 카메라, 바로가기, 건물 선택 |
| 15 | `ProductionUpgrade` | X | `AllProductionLinesUpgradedCondition` | 생산 건물 3종 무료, 각 Lv.1 상한 |
| 16 | `VillagerIncrease` | O | `BuildingActionCondition` | 본진 주민 증가 무료, 1회 상한 |
| 17 | `VillagerAssignAgain` | O | `ResidentDragAssignedCondition` | 주민 선택·드래그; 드래그 성공 후 전원 배치 확인 |
| 18 | `SkillUpgrade` | O | `BuildingActionCondition` | 마법 연구소 무료 강화, Lv.1 상한 |
| 19 | `AlchemyExchange` | O | `BuildingActionCondition` | 연금술 교환 1회 |
| 20 | `CastleUpgrade` | O | `BuildingActionCondition` | 본진 무료 강화, Lv.1 상한 |
| 21 | `TowerBuildForMerge` | O | `TowerCountCondition` | 아처만 무료 배치, 아처 3개 보유, Undo·전투 지역 바로가기 허용 |
| 22 | `TowerMerge` | X | `TowerMergedCondition` | 설치 타워 다중 선택·합성·결과 배치 |
| 23 | `CombatIntro` | O | `PhaseChangedCondition`(밤) | 카메라, 낮 종료 |
| 24 | `WaveClear` | X | `PhaseChangedCondition`(낮) | 카메라, 스킬, 설치 타워 선택 |
| 25 | `TutorialComplete` | X | `DelayCondition`(3초, 실제 시간) | 입력 없음; 완료 통지 후 일반 게임으로 전환 |

`BuildingShortcutUsedCondition`은 바로가기 바의 `Focused`와 `MouseManager.OnPrimarySelect`를 함께
구독한다. 따라서 14단계는 지정 건물 바로가기뿐 아니라 월드의 같은 건물을 직접 클릭해도 완료된다.

`TowerPlacedCondition`은 단계 진입 뒤의 **증가량**을 보고, `TowerCountCondition`은 단계 진입 전에 있던
타워까지 포함한 **현재 보유량**을 본다. Undo·합성 단계에서 둘을 혼용하지 않는다.

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

## 4. 행동 게이트와 단계 규칙

### 4.1 실행 게이트와 표시 게이트

`TutorialInputGate`는 정적이며, 튜토리얼이 제한 중일 때 `TutorialAction` 비트 플래그로 행동을 판정한다.
하나의 단계가 여러 행동을 허용할 수 있다.

| API | 의미 |
|---|---|
| `Allows(action)` | 실제 시스템 진입을 허용하는가. 버튼뿐 아니라 도메인 메서드도 이것을 확인한다 |
| `AllowsForDisplay(action)` | 버튼을 활성 색으로 보여도 되는가. 팝업 뒤에서 다음 행동 버튼이 회색이 되지 않게 한다 |
| `AllowsEndDay()` | `EndDay` 플래그와 현재 타워 요구치를 함께 검사한다 |
| `Apply(flags)` | 행동 단계: 지정 플래그만 실행·표시 허용 |
| `ApplyPopup(flags)` | 팝업 단계: 실행은 전부 금지하고, 확인 뒤 쓸 행동만 표시 허용 |
| `Clear()` | 제한과 낮 종료 요구치를 모두 초기화. 일반 Run은 다시 permissive 상태가 된다 |

팝업의 투명 패널은 다른 Canvas의 클릭을 물리적으로 막는다. 이것만 신뢰하지 않고 `UndoRequest`,
`DayNightManager.EndDay`, `ManagementController`, `SkillManager`, `MouseManager`, 타워 합성 등 실제 진입점도
`Allows`를 검사한다. Ctrl+Z나 외부 코드 호출이 UI의 `interactable`을 우회할 수 있기 때문이다.

`Undo`는 독립 플래그다. 버튼과 Ctrl+Z는 모두 `UndoRequest.Submit()`을 통과하므로 SO에서 `Undo`를
허용하면 둘 다 열리고, 빼면 둘 다 막힌다. 자원 소비까지 되돌릴 수 있으므로 필요한 단계에만 명시한다.

낮 종료 타워 요구치가 1 이상이면 게이트가 `Tower.ActiveChanged`를 직접 구독하고, 현재 타워 수가 바뀔
때마다 `Changed`를 발행한다. UI는 `Tower`를 별도로 구독하지 않고 게이트의 결과만 다시 그린다. 따라서
배치와 Undo 어느 경로에서도 버튼 표시와 실제 `AllowsEndDay()` 판정이 같은 프레임에 함께 갱신된다.

### 4.2 `TutorialAction` 목록

| 플래그 | 허용하는 행동 |
|---|---|
| `SelectResident` | 월드 주민 단일·박스 선택 |
| `DragResident` | 주민을 직접 끌어 생산 건물에 배치 |
| `ChooseTowerForPlacement` | 타워 패널에서 배치할 타워 선택 |
| `PlaceTower` | 전투 타일에 타워 또는 합성 결과 배치 |
| `MoveCamera` | 키보드·드래그·휠·바로가기 카메라 이동 진입 |
| `UseBuildingShortcut` | 바로가기 바 버튼·숫자키의 공통 `Focus` |
| `EditResidentByButton` | 생산 라인의 주민 +/- |
| `EndDay` | 낮 종료 요청과 실제 페이즈 전환 |
| `UseSkill` | 스킬 버튼·조준 시작·실제 시전 |
| `SelectBuilding` | 월드 건물 선택·호버 |
| `UpgradeBuilding` | 생산 건물·마법 연구소·본진 업그레이드 |
| `IncreaseVillager` | 본진 주민 수 증가 |
| `Undo` | Undo 버튼과 Ctrl+Z |
| `SelectPlacedTower` | 설치된 타워 단일·다중 선택 |
| `AlchemyExchange` | 연금술사의 집 자원 교환 |
| `MergeTower` | 합성 후보 선택과 합성 실행 |

새 행동 플래그를 추가할 때는 UI 한 곳만 막지 않는다. 모든 입력 경로가 모이는 실제 시스템 진입점에
검사를 추가하고, 해당 UI가 지속 표시된다면 `TutorialInputGate.Changed` 구독과 `AllowsForDisplay`도 함께
검토한다. 정적 이벤트 구독은 `OnDisable` 또는 `OnDestroy`에서 반드시 해제한다.

월드 선택 대상은 `ITutorialSelectionGate.SelectionAction`으로 자신이 소비할 선택 플래그를 공개한다.
`MouseManager`는 건물·주민·타워 같은 구체 타입을 나열하지 않는다. 제한 중 이 인터페이스가 없는 선택
대상은 차단되므로 새 선택 타입을 추가할 때는 `ISelectable`/`IGroupSelectable`과 함께 구현해야 한다.

### 4.3 SO 단계 규칙 설정

| 필드 | 설정법 |
|---|---|
| `Restrict Actions` | 실제 행동 단계라면 켜고 `Allowed Actions`를 명시한다 |
| `Allowed Actions` | 그 단계에서 필요한 행동과 카메라 이동을 Flags로 함께 선택한다 |
| `Minimum Tower Count Before End Day` | 낮 종료 전 필요한 현재 타워 수. `0`이면 검사하지 않는다 |
| `Required Tower Before End Day` | 비우면 모든 타워, 지정하면 해당 `TowerAsset`만 센다 |
| `Pause Game During Step` | 팝업 진입부터 조건 완료까지 튜토리얼 pause reason을 건다 |
| `Resume Game After Popup` | 설명을 읽는 동안만 멈추고, 확인 후 scaled time이 필요한 행동을 재개한다 |
| `Free Tower Placement` | 해당 단계에서 타워 비용을 받지 않는다 |
| `Restrict Tower Panel To` | 타워 패널에서 지정한 타워만 선택 가능하게 한다 |
| `Free Management Cost` | 건물 강화·주민 증가 비용을 받지 않는다 |
| `Upgrade Cap` | 해당 단계의 건물 최대 내부 업그레이드 레벨. `0`이면 제한 없음 |
| `Villager Cap` | 해당 단계에서 늘릴 수 있는 추가 주민 횟수. `0`이면 제한 없음 |
| `Upgrade Allow List` | 해당 단계에서 강화할 수 있는 건물 목록. 무료 단계에서는 반드시 좁힌다 |

단계 전환 때 위 규칙은 새 SO 값으로 덮어쓰고, 종료·스킵·비활성화 경로에서는 모두 초기화한다.
`Upgrade Cap`을 완료 조건보다 낮게 잡거나, 무료 강화 대상의 `Upgrade Allow List`를 비워 다음 단계 건물까지
미리 강화할 수 있게 만들면 튜토리얼이 막힐 수 있다.

### 4.4 일시정지와 카메라

`Pause Game During Step`은 `GamePauseReason.Tutorial`을 사용한다. `Time.timeScale = 0`만으로 입력은 막히지
않으므로 행동 차단은 항상 `TutorialInputGate`가 담당한다.

스킬 착탄·이펙트는 일반 게임과 같은 scaled time 계약을 유지한다. `SkillUse`는 팝업을 읽는 동안만 멈추고
`Resume Game After Popup`으로 확인 직후 시간을 재개한다. 스킬만 별도로 unscaled time으로 돌리지 않는다.

카메라는 허용 행동에 `MoveCamera`가 있을 때만 입력과 프로그램 이동 진입을 받는다. 팝업에서 게이트가 닫히면
진행 중인 카메라 보간도 취소될 수 있다. 자동 바로가기 이동을 팝업 중에도 완주시키는 정책이 필요해지면
플레이어 입력과 프로그램 이동을 별도 상태로 분리해야 하며, `LateUpdate` 가드만 제거해서는 해결되지 않는다.

### 4.5 강조와 아웃라인

화면 딤은 현재 튜토리얼 흐름에서 사용하지 않는다. `TutorialStepAsset`에는 기존 강조 필드가 남아 있지만,
행동 제한의 정본은 딤 구멍이 아니라 `TutorialInputGate`다. 건물·타워 호버 아웃라인은
`MouseManager`가 현재 단계에서 선택 가능한 대상만 `OnHoverChanged`로 전달하는 기존 아웃라인 시스템을 쓴다.

---

## 5. 테스트 절차

### 5.1 전체 흐름

1. `Assets/Personal/muchan/Scene/TutorialTest3.unity`를 연다.
2. `TutorialController.startOnPlay`를 켜고 `Debug Mode`를 끈다.
3. `TutorialSystem.prefab`의 `Steps`가 [§2.5](#25-현재-25단계-정본) 순서인지 확인한다.
4. 1~25단계를 실제 행동으로 끝까지 진행한다.
5. 마지막 웨이브 종료 후 완료 저장과 일반 `GameScene` 전환을 확인한다.
6. 일반 Run에서 초기 자원·적 체력·스킬 쿨다운·무료 비용·행동 제한이 남지 않았는지 확인한다.

특히 다음 회귀를 반드시 확인한다.

- 타워 설치 후 Undo 버튼 또는 Ctrl+Z로 되돌리면 타워 수가 즉시 줄고, 타워가 없을 때 낮 종료가 막힌다.
- 타워 요구치가 생기거나 사라지는 단계 전환 직후 낮 종료 버튼 표시가 즉시 갱신된다.
- Undo 버튼과 Ctrl+Z가 같은 단계에서 함께 허용되거나 함께 거절된다.
- 팝업 중 뒤쪽 UI·월드 입력이 모두 막히지만, 확인 후 SO의 `Allowed Actions`만 열린다.
- 11단계에서 팝업 중 전투가 멈추고, 확인 후 시간이 재개되어 스킬 착탄과 완료 이벤트가 정상 동작한다.
- 14단계는 바로가기와 지정 건물 직접 클릭을 모두 인정한다.
- 15단계는 생산 건물 3종을 각각 한 번 강화해야 하며 다른 건물 선행 강화가 불가능하다.
- 17단계는 주민 드래그 성공 1회와 미배치 주민 0명을 함께 요구한다.
- 21단계는 아처 타워 현재 보유 3개를 요구하고, 22단계는 결과 타워 배치 확정 후 넘어간다.
- 21단계 안내에 따라 숫자키 `7`로 전투 지역에 이동할 수 있고, 튜토리얼 중 타워 도감 버튼은
  보이지 않으며 `FusionTowerCodexUI.Open()`을 직접 호출해도 열리지 않는다.
- 튜토리얼 웨이브 시작과 맵 재구성 뒤에도 `PlayerBase`가 하나만 존재하고, 몬스터가 기존 본진을
  정상적으로 인식하며 `TutorialSafety`의 무적 상태가 유지된다.
- 완료·스킵·오브젝트 비활성화 후 `TutorialInputGate`와 무료/상한 규칙이 초기화된다.
- `TutorialTest3` 직접 실행은 초기 자원 20·적 HP 50%·스킬 쿨다운 3초를 모두 적용한다.
- `forceTutorialWaves`만 켠 일반 실행은 튜토리얼 웨이브만 사용하고 위 수치와 UI는 바꾸지 않는다.
- 제한 중 건물·주민·설치 타워 선택은 각자의 선택 플래그에서만 허용되고, 빈 곳 클릭의 선택 해제는 유지된다.

### 5.2 특정 단계 디버그

`Debug Mode`를 켜고 필요한 선행 단계와 대상 단계만 `Debug Steps`에 넣는다. Debug는 게임 상태를 만들어
주지 않으므로 밤 스킬 단계에는 낮 종료를, 합성 단계에는 타워 건설 단계를 함께 넣는 식으로 준비한다.

SO·프리팹·씬을 변경했다면 `Docs/Tools/unity-cli-guide.md`에 따라 해당 경로를 `unity-cli reserialize`하고,
C#을 변경했다면 `unity-cli editor refresh --compile` 후 `unity-cli console --type error`를 확인한다.
PlayMode 전체 흐름 검증은 비용이 크므로 작업자와 합의한 경우에만 CLI로 실행하고, 평소에는 에디터에서
수동 전체 플레이 테스트를 수행한다.

## 6. 막혔을 때 — 증상 → 원인

| 증상 | 원인 |
|---|---|
| `Completion` 칸에 드롭다운이 안 보인다 | `ManagedReferencePickerDrawer.cs`에 `TutorialConditionDrawer`가 없다 |
| 드롭다운에 내가 만든 조건이 안 뜬다 | `[Serializable]`이 없거나, `abstract`이거나, 매개변수 없는 생성자가 없다 |
| 팝업 확인을 눌러도 아무 일이 없다 | 그 단계에 조건도 말풍선도 없으면 그냥 지나간다. 콘솔 로그로 진행 상황을 본다 |
| 행동해도 단계가 안 넘어간다 | `Completion`이 비었거나, 조건이 대상을 못 찾았다(콘솔에 경고가 찍힌다) |
| 시작하자마자 단계를 통과해버린다 | 조건이 상태를 `Begin`에서 초기화하지 않는다 |
| 말풍선 뒤 오브젝트가 클릭되지 않는다 | `Bubble`(또는 자식 텍스트)의 `Raycast Target`이 켜져 있다 |
| 팝업 뒤 UI가 클릭된다 | `PopupInputBlocker` 배선·활성 상태와 투명 `Image.raycastTarget`을 확인한다 |
| 버튼은 눌리는데 행동이 실행되지 않는다 | 현재 SO의 `Allowed Actions`에 해당 플래그가 없거나 UI 표시가 `AllowsForDisplay`와 동기화되지 않았다 |
| 버튼·단축키 중 하나만 Undo된다 | 둘 다 `UndoRequest.Submit()`을 쓰는지, 현재 단계에 `Undo` 플래그가 있는지 확인한다 |
| Undo한 타워가 낮 종료 수에 남는다 | `TowerPlaceCommand.OnUndo`가 Destroy 전에 비활성화하여 `Tower.Active`에서 즉시 빠지는지 확인한다 |
| 튜토리얼 수치가 일반 Run에도 남는다 | 종료 경로의 `ClearStepRules`/`TutorialInputGate.Clear`와 `TutorialMode.Exit`을 확인한다 |
| 등록한 단계가 통째로 안 뜬다 | `Debug Mode`가 켜져 있다 — `Debug Steps`만 돈다([§2.4](#24-단계-하나만-떼어내-돌려보기)). 시작 시 콘솔에 경고가 찍힌다 |
| 이전 단계의 조건이 또 발화한다 | `End`에서 구독을 안 풀었다 |
| 단계 에셋의 조건이 통째로 비어 있다 | 조건 클래스 이름을 바꿨다([§3.2](#32-반드시-지킬-것)) |
| 1일차 낮 신호를 놓친다 | `DayNightManager.OnDayStart`는 `Start()`에서 한 프레임 지연 후 최초 발행된다 |
