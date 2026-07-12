# DayNightManager 설계 문서

게임의 **낮(경영)/밤(수비) 페이즈 전환**을 관리하고, 다른 시스템이 구독할 전환 이벤트를 제공하는
중앙 매니저 문서. 자원 정산·본진 회복·주민 배치 초기화 등 페이즈에 반응하는 로직을 구현할 때 참고한다.

- 관련 이슈: **#6**
- 구현 위치: `Assets/Personal/muchan/DayNight/`
- 이 문서는 **현재 구현된 구조**를 정리한 것이다. 코드를 바꾼 사람은 이 문서도 함께 갱신해 어긋나지 않게 유지한다. 미구현 항목은 [8. 미확정/TODO](#8-미확정--todo)에 모아둔다.

> ⚠️ 밤→낮 전환 트리거가 **3초 고정 타이머(임시 테스트 코드)** 다. 실제로는 웨이브 클리어 시 전환돼야 하며, Combat 웨이브 시스템이 완성되면 교체된다.

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
| | 낮/밤 전환 연출(비주얼) → **UI/연출 시스템** |

## 3. 상태 구조

```
        EndDay()                    3초 경과(임시)
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
- **Night**: `NightTimerRoutine` 코루틴이 3초 대기 후 자동으로 Day로 복귀(임시 — 실제로는 웨이브 클리어가 트리거해야 함).

## 4. 이벤트 훅

| 이벤트 | 발생 시점 | 구독 예시 |
|---|---|---|
| `OnDayStart` | 낮이 시작되는 **모든** 시점 (1일차 부트스트랩 포함) | 본진 체력 회복 |
| `OnDayToNight` | 낮→밤 전환 순간 | 주민 배치 기반 자원 정산 |
| `OnNightToDay` | 밤→낮 전환 순간 (웨이브 종료를 의미) | 주민 배치 초기화 |

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
    DayNightManager.Instance.OnDayToNight += SettleResources;
    DayNightManager.Instance.OnNightToDay += ResetVillagerPlacement;
}

private void OnDestroy()
{
    if (DayNightManager.Instance == null) return;
    DayNightManager.Instance.OnDayStart -= RestoreBaseHealth;
    DayNightManager.Instance.OnDayToNight -= SettleResources;
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
| `DayNightManager.cs` | 중앙 매니저(씬 싱글톤 `Instance`, `DontDestroyOnLoad` 없음). 페이즈 관리·이벤트 발행 |
| `DayNightManagerTest.cs` | (테스트) 세 이벤트를 구독해 Console에 로그 출력 |

- **생명주기**: 씬 싱글톤. 경영/전투 공간이 한 씬에 공존해 씬 전환에 걸쳐 상태를 유지할 이유가 없다는 판단(WL-002 참고 사례로 SystemMap §5에 기록).
- **씬**: `Assets/Personal/muchan/Scene/ManageSpace.unity` (테스트용 버튼 배치)

## 7. 미확정 / TODO

- [ ] **밤 종료 트리거**: 현재 3초 고정 타이머(코루틴) placeholder. 실제로는 웨이브 클리어(Combat 시스템)가 트리거해야 하고, UniTask로 교체 검토
- [ ] **본진 체력 회복 / 자원 정산 / 주민 배치 초기화**: 이벤트 훅만 존재, 실제 로직은 각 소유 시스템(미구현)이 구독해서 채워야 함
- [ ] **낮/밤 전환 연출**: Build0 계획의 "버튼 클릭 시 낮 밤 전환 연출"은 미구현 — UI/연출 시스템 담당
- [ ] **낮→밤 트리거 UI**: 지금은 테스트용 버튼 하나. 실제 UI 버튼/디자인 확정 필요

## 8. 참고

- GDD 관련 시스템: `Docs/GDD.md` §5(게임 루프), §6.6(보상 시스템)
- 팀 계약: `Docs/Review/SystemMap.md` §4 5번("낮/밤 전환 계약"), §5(매니저 수명주기)
