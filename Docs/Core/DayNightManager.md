# DayNightManager 설계 문서

게임의 **낮(경영)/밤(수비) 페이즈 전환**을 관리하고, 다른 시스템이 구독할 전환 이벤트를 제공하는
중앙 매니저 문서. 자원 정산·본진 회복·주민 배치 초기화 등 페이즈에 반응하는 로직을 구현할 때 참고한다.

- 관련 이슈: **#6**
- 구현 위치: `Assets/Scripts/DayNight/`
- 이 문서는 **현재 구현된 구조**를 정리한 것이다. 코드를 바꾼 사람은 이 문서도 함께 갱신해 어긋나지 않게 유지한다. 미구현 항목은 [8. 미확정/TODO](#8-미확정--todo)에 모아둔다.

> ⚠️ 밤→낮 전환은 현재 **좌측 하단 "웨이브 성공" 버튼(`NightActionPanelView`, 임시 UI, #66)이 `EndNight()`를 직접 호출**하는 것으로만 일어난다. 실제로는 웨이브 클리어 시 Combat 시스템이 `EndNight()`를 호출해야 한다(WL-018). 3초 자동 타이머 코드는 참고용으로 주석 처리해뒀다(§7 참고).

## 1. 목적 · 핵심 원칙

**단일 책임**: "지금이 낮인지 밤인지"와 "언제 전환이 일어났는지"는 `DayNightManager` **하나만** 안다.
자원 정산, 본진 회복, 주민 배치 초기화 같은 **실제 로직은 각 소유 시스템이 이벤트를 구독해서 처리**한다
(GDD §5, SystemMap §4 "낮/밤 전환 계약": "페이즈에 반응하는 시스템은 전환 이벤트 훅 구조여야 한다").

- `DayNightManager`는 페이즈 상태와 전환 이벤트만 쏜다. 자원 차감/회복/초기화 로직을 직접 수행하지 않는다.
- 다른 시스템은 `DayNightManager.Instance`의 이벤트를 구독하거나 `CurrentPhase`를 조회해서 반응한다.

## 2. 책임 범위

| 담당함 (DayNightManager) | 담당하지 않음 (다른 시스템) |
|---|---|
| 현재 페이즈(`CurrentPhase`) 관리 | 본진 체력 회복 로직 → **본진/체력 시스템** |
| 웨이브 카운트(`WaveCount`) 관리 | 주민 배치 기반 자원 정산 → **자원/경영 시스템** |
| 전환 시점에 이벤트 발행 | 주민 배치 초기화 → **주민 시스템** |
| | 낮/밤 전환 연출(비주얼) → **UI/연출 시스템** (`DayNightLightingController.cs`, #7·§6) |

## 3. 상태 구조

```
        EndDay()                      EndNight()
   ┌───────────────────────►┐  ┌──────────────────────┐
   │                         │  │                      │
┌──┴───────────┐       ┌─────▼──┴─────┐                │
│      Day      │       │     Night     │◄──────────────┘
│  (경영 페이즈) │       │  (수비 페이즈) │
└──┬────────────┘       └───────────────┘
   │  ▲
   └──┘ (1일차 부트스트랩: Awake 시 Day로 시작)
```

- **Day**: `EndDay()` 호출(현재는 테스트 버튼) 전까지 유지.
- **Night**: `EndNight()` 호출(현재는 테스트 버튼) 전까지 유지. 두 메서드 모두 public이라 호출 주체가 버튼이든 향후 Combat 웨이브 클리어 로직이든 상관없다 — 대칭적인 진입점 계약.

## 4. 이벤트 훅

| 이벤트 | 발생 시점 | 구독 예시 |
|---|---|---|
| `OnDayStart` | 낮이 시작되는 **모든** 시점 (1일차 부트스트랩 포함) | 본진 체력 회복 |
| `OnDayToNight` | 낮→밤 전환 순간 | 주민 배치 확정(자원 정산 없음) |
| `OnNightToDay` | 밤→낮 전환 순간 (웨이브 종료를 의미) | 주민 배치 기반 자원 정산(먼저) + 주민 배치 초기화(그 다음) |

`OnNightToDay`와 `OnDayStart`는 2일차부터 항상 같이(순서: `OnNightToDay` → `OnDayStart`) 발생하지만,
**1일차는 `OnDayStart`만 단독으로 발생**한다(밤을 거치지 않았으므로 웨이브 증가·배치 초기화 대상이 없음).
그래서 "밤이 있었다"는 전제가 필요한 로직은 반드시 `OnNightToDay`에, 매 낮 무조건 실행돼야 하는 로직은
`OnDayStart`에 구독해야 한다.

### 4.1 구독 패턴

```csharp
private void Start()
{
    if (DayNightManager.Instance == null)
    {
        Debug.LogError("DayNightManager 없음");
        return;
    }

    DayNightManager.Instance.OnDayStart += RestoreBaseHealth;
    // 정산이 초기화보다 먼저 실행돼야 하므로 구독 순서(= 실행 순서) 주의
    DayNightManager.Instance.OnNightToDay += SettleResources;
    DayNightManager.Instance.OnNightToDay += ResetVillagerPlacement;
}

private void OnDestroy()
{
    if (DayNightManager.Instance == null) return;
    DayNightManager.Instance.OnDayStart -= RestoreBaseHealth;
    DayNightManager.Instance.OnNightToDay -= SettleResources;
    DayNightManager.Instance.OnNightToDay -= ResetVillagerPlacement;
}
```

- `Instance`는 null일 수 있다 → 호출부 null 체크 필수(SystemMap §2/WL-014 관행).
- 구독 후 반드시 `OnDestroy`에서 해제한다. 안 하면 파괴된 오브젝트를 이벤트가 계속 참조해 예외가 날 수 있다.
- 이벤트 구독 없이 "지금 낮인지"만 필요하면 `DayNightManager.Instance.CurrentPhase == DayNightManager.Phase.Day`로 즉시 조회하면 된다(예: 배치 가능 여부 판정).

## 5. Start() 초기화 순서 주의

`DayNightManager`는 씬에 여러 오브젝트가 있을 때 **Awake 순서를 Unity가 보장하지 않는다**는 문제를
피하기 위해, 1일차 `OnDayStart`를 자기 `Start()`에서 곧바로 쏘지 않고 **한 프레임(`yield return null`)
지연시켜** 발행한다. 그 프레임의 모든 `Start()`가 끝난 뒤에만 코루틴이 재개된다는 점은 Unity가 보장하는
더 강한 규칙이라, 구독자가 몇 번째로 초기화됐는지와 무관하게 안전하게 첫 이벤트를 받을 수 있다.
반대로 구독자를 `Awake()`로 옮기는 것은 해결책이 아니다 — `Instance`가 아직 할당되지 않았을 수 있어
오히려 `NullReferenceException` 위험이 생긴다.

## 6. 구현 현황 (실제 파일)

| 파일 | 역할 |
|---|---|
| `DayNightManager.cs` | 중앙 매니저(씬 싱글톤 `Instance`, `DontDestroyOnLoad` 없음). 페이즈 관리·이벤트 발행. `EndDay()`/`EndNight()` 둘 다 public |
| `DayNightManagerTest.cs` | (테스트) 세 이벤트를 구독해 Console에 로그 출력. null 가드 + `OnDestroy` 구독 해제 포함 |
| `DayNightLightingController.cs` | (#7) `OnDayToNight`/`OnNightToDay` 구독, Directional Light·Ambient(Trilight)·Skybox를 프리셋 값으로 즉시 전환(스냅). Fog 제외 |
| `ManagementController.cs`(`HandleNightToDay`) | (#66) `OnNightToDay` 구독 — 자원 정산(먼저) + 주민 배치 초기화(그 다음) 실제 로직 구현. `OnDayToNight`은 더 이상 구독하지 않음 |
| `NightActionPanelView.cs` | (#66, 임시) 밤에만 좌측 하단에 노출되는 "웨이브 성공/실패/보스 처치" + "낮 종료" 버튼 4개. "웨이브 성공"이 `EndNight()`를 직접 호출(WL-018 임시 트리거) |

- **생명주기**: 씬 싱글톤. 경영/전투 공간이 한 씬에 공존해 씬 전환에 걸쳐 상태를 유지할 이유가 없다는 판단(WL-002 참고 사례로 SystemMap §5에 기록).
- **씬**: `Assets/Scenes/GameScene.unity` (정본, `Docs/Core/SceneWorkflow.md`). 낮/밤 전환 버튼은 `NightActionPanelView`(밤 전용 3개) + `ManagementPanelView`의 낮 종료 버튼(낮 전용 1개)로 구성

## 7. 미확정 / TODO

- [ ] **밤 종료 자동화**: 지금은 `EndNight()`를 "웨이브 성공" 버튼(`NightActionPanelView`, #66)으로 수동 호출. 실제로는 Combat 웨이브 클리어가 이 메서드를 호출해야 함(WL-018). 자동 타이머로 되돌릴 일이 생기면 `DayNightManager.cs`에 주석 처리된 `NightTimerRoutine` 코루틴(UniTask로 교체 예정)을 참고
- [x] **자원 정산 / 주민 배치 초기화**: `ManagementController.HandleNightToDay()`로 구현 완료(#66). `OnNightToDay` 시점에 정산(먼저)→초기화(그 다음) 순서로 실행
- [ ] **본진 체력 회복**: 이벤트 훅(`OnDayStart`)만 존재, 실제 로직은 본진/체력 소유 시스템(미구현)이 구독해서 채워야 함
- [x] **낮/밤 전환 연출**: `DayNightLightingController.cs`로 구현 완료(#7, §6 참고). Directional Light·Ambient·Skybox를 즉시 전환
- [ ] **부드러운 전환(Lerp)**: 지금은 프리셋 값을 즉시 스냅 적용. 밤 종료 자동화(§7 상단 항목)를 코루틴에서 UniTask로 교체할 때 같이 Lerp 전환을 붙일 예정 — 별도 코루틴 기반으로 먼저 만들지 않기로 결정(작업 이중화 방지)
- [ ] **낮/밤 트리거 UI**: 지금은 임시 버튼 4개(밤: 웨이브 성공/실패/보스 처치, 낮: 낮 종료, `NightActionPanelView`/`ManagementPanelView`, #66). "웨이브 실패"/"보스 처치"도 Combat의 실제 웨이브 실패·보스 사망 판정 연동 전까지의 임시 대체물. 실제 UI 버튼/디자인 확정 필요

## 8. 참고

- GDD 관련 시스템: `Docs/GDD.md` §5(게임 루프), §6.6(보상 시스템)
- 팀 계약: `Docs/Review/SystemMap.md` §4 5번("낮/밤 전환 계약"), §5(매니저 수명주기)
