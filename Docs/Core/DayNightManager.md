# DayNightManager 설계 문서

게임의 **낮(경영)/밤(수비) 페이즈 전환**을 관리하고, 다른 시스템이 구독할 전환 이벤트를 제공하는
중앙 매니저 문서. 자원 정산·본진 회복·주민 배치 초기화 등 페이즈에 반응하는 로직을 구현할 때 참고한다.

- 관련 이슈: **#6**
- 구현 위치: `Assets/Scripts/DayNight/`
- 이 문서는 **현재 구현된 구조**를 정리한 것이다. 코드를 바꾼 사람은 이 문서도 함께 갱신해 어긋나지 않게 유지한다. 미구현 항목은 [8. 미확정/TODO](#8-미확정--todo)에 모아둔다.

> ⚠️ 밤→낮 전환(`EndNight()`)은 현재 두 경로로 일어난다: (1) `MonsterSpawn`이 웨이브 클리어(스폰 완료 후 생존 몬스터 0) 시 자동 호출(#17), (2) 좌측 하단 "웨이브 성공" 버튼(`NightActionPanelView`, 임시 UI, #66) 수동 호출. 단 (1)의 "클리어"는 아직 처치가 아니라 몬스터의 본진 도달-디스폰 기준이다(처치 기반은 Enemy 병합 후 — WL-038). "웨이브 실패/보스 처치" 정식 판정 연동과 임시 버튼 제거는 WL-018 잔여. 3초 자동 타이머 코드는 참고용으로 주석 처리해뒀다(§7 참고).

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
| | 낮/밤 전환 연출(비주얼) → **UI/연출 시스템** (`DayNightTransition`이 구동, `DayNightLightingController`/`StreetLampController`가 적용 — #7·#101·§6·§6.1) |

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
| `OnDayToNight` | 낮→밤 전환 순간 | 전투 스테이지 확장 + 몬스터 스폰(`StageBuilder`, #17) |
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
| `DayNightLightingController.cs` | (#7·#136·#101) 낮/밤 룩의 **적용부**. Directional Light·Ambient(Trilight)·Skybox·`NightVolume` weight·물 틴트를 프리셋 값으로 적용한다. 진입점이 둘: `ApplyBlend(t)`(0=낮/1=밤, 임의 지점 — 전환이 매 프레임 호출)와 이벤트 구독(스냅). `subscribeToPhaseEvents`를 끄면 이벤트를 직접 구독하지 않는다 — **정본 `GameScene`은 꺼져 있고 `DayNightTransition`이 단독 구동**한다. Fog는 미채택(§6.1) |
| `StreetLampController.cs` | (#136) 마을 가로등 31개(`5_obj05_1.0_0_0/StreetLamps/Lamp_01~31`)를 밤에만 켠다. `SetBlend(t)`로 `turnOnAt`(0.15) 이후 구간에서 밝기가 올라온다. `DayNightLightingController`와 같은 `subscribeToPhaseEvents` 스위치를 갖는다 |
| `DayNightTransition.cs` | (#101) 전환 연출의 **구동부**. `OnDayToNight`/`OnNightToDay`를 구독해 UniTask로 위 둘의 블렌드와 셀 와이프 셰이더를 함께 몬다. `IsTransitioning`·`OnTransitionComplete` 공개(§6.1) |
| `ManagementController.cs`(`HandleNightToDay`) | (#66) `OnNightToDay` 구독 — 자원 정산(먼저) + 주민 배치 초기화(그 다음) 실제 로직 구현. `OnDayToNight`은 더 이상 구독하지 않음 |
| `NightActionPanelView.cs` | (#66, 임시) 밤에만 좌측 하단에 노출되는 "웨이브 성공/실패/보스 처치" + "낮 종료" 버튼 4개. "웨이브 성공"이 `EndNight()`를 직접 호출(WL-018 임시 트리거) |
| `StageBuilder.cs`(`OnDayToNight` 구독) | (#17) 밤 진입 시 다음 스테이지 생성(전투영역 확장) + `MonsterSpawn.StartRound`로 몬스터 스폰(`currentMapCount > 1`). `Start()`에서 구독, `OnDestroy()`에서 해제 |
| `MonsterSpawn.cs` | (#17) 밤에 스테이지 몬스터 스폰(`StartRound`). 스폰 완료 후 생존 0이 되면 `EndNight()` 호출(웨이브 클리어; 도달-디스폰 기준, 처치는 Enemy 병합 후 WL-038). 낮엔 스폰 스킵(경고 로그) |

- **생명주기**: 씬 싱글톤. 경영/전투 공간이 한 씬에 공존해 씬 전환에 걸쳐 상태를 유지할 이유가 없다는 판단(WL-002 참고 사례로 SystemMap §5에 기록).
- **씬**: `Assets/Scenes/GameScene.unity` (정본, `Docs/Core/SceneWorkflow.md`). 낮/밤 전환 버튼은 `NightActionPanelView`(밤 전용 3개) + `ManagementPanelView`의 낮 종료 버튼(낮 전용 1개)로 구성

### 6.1 전환 연출 — 셀 와이프 (#101)

밤으로 넘어갈 때 화면을 정사각 셀로 나눠 **우하단에서 좌상단으로** 하나씩 뒤집는다. 낮으로 돌아올 때는 반대 방향.

**왜 화면공간만으로는 안 되는가.** 밤 전환에서 화면공간인 것은 `NightVolume` 그레이드뿐이고, 나머지(디렉셔널
라이트·앰비언트·스카이박스·가로등 31개·물 틴트)는 전부 **씬 라이팅**이라 "화면의 이 셀만 밤"이 원리적으로
불가능하다. 같은 프레임에 낮과 밤을 동시에 보여주려면 씬을 두 번 렌더해야 하고, 그것을 피하면 한쪽은 정지
이미지가 된다(주민·몬스터가 얼어붙는다). 그래서 역할을 나눴다:

```
씬 블렌드 = progress          (전역 — DayNightLightingController.ApplyBlend / StreetLampController.SetBlend)
뒤집힌 셀 = 목표 - 씬 블렌드   (NightWipe 풀스크린 패스가 얹는 나머지)
→ 뒤집힌 칸은 항상 목표 상태 100%, 아직 안 온 곳은 progress만큼만 진행된 중간 상태
```

얹을 양을 `목표 - 현재`로 넘기므로 **밤→낮이면 부호가 뒤집혀 같은 식이 양방향에 성립**한다.

**이 배분의 핵심은 종료 지점이다.** `progress=1`에서 씬 블렌드가 목표에 도달해 패스의 기여가 정확히 0이 되므로,
셰이더의 그레이드 근사식이 URP `ColorAdjustments`와 일치하지 않아도(이 패스는 톤매핑 **이후** LDR 이미지에
걸리므로 애초에 일치할 수 없다) 전환이 끝날 때 튀지 않는다.

| 구성 요소 | 경로 |
|---|---|
| 구동부 | `Assets/Scripts/DayNight/DayNightTransition.cs` (UniTask) |
| 셰이더 | `Assets/Shaders/DayNight/NightWipe.shader` + `NightWipe.mat` |
| 렌더러 피처 | `PC_Renderer`/`Mobile_Renderer`의 `Night Wipe`(`FullScreenPassRendererFeature`, AfterRenderingPostProcessing) |

- **전환 중에만 피처를 켠다**(`SetActive`) — 평소 프레임 비용 0.
- **HUD는 덮이지 않는다.** 이 패스는 카메라 렌더 안에서 돌고 ScreenSpaceOverlay 캔버스는 그 뒤에 그려지므로
  구조상 자동으로 보장된다(별도 게이팅 코드 없음).
- **셰이더 파라미터는 Properties가 아니라 전역 uniform**(`_NightWipe_` 접두사)이다. 매 프레임 구동하는 값이라
  머티리얼 프로퍼티로 두면 쓸 때마다 **머티리얼 에셋이 dirty가 되어 git diff에 뜬다**. 튜닝 지점도
  `DayNightTransition` 컴포넌트 하나로 모인다.
- **재진입 가드**(#101 완료기준): 진행 중 다시 호출되면 이전 전환을 취소하고 그 목표를 즉시 확정한 뒤 새로
  시작한다. 중간 상태로 멈추면 라이팅이 어중간한 값에 남아 다음 전환의 시작점이 어긋난다.

⚠️ **`subscribeToPhaseEvents`를 켠 채로 두면 이중 적용된다.** 두 적용부(`DayNightLightingController`·
`StreetLampController`)가 이벤트에 직접 반응해 스냅으로 목표값을 찍어버리므로 연출이 성립하지 않는다.
정본 씬은 둘 다 꺼져 있다.

**현재 값**: duration 0.8초 / cell 36px / jitter 0.18 / edgeGlow 0.15 (전부 인스펙터 노출)

**튜닝 메모** — 와이프 방향은 **지터를 끄고** 계단 방향을 봐야 판별된다. 지터가 있으면 전선이 흩어져
스크린샷 눈대중으로는 좌우가 뒤집혀 보인다(실제로 한 번 헛짚었다). blit `texcoord`는 **v=1이 화면 위쪽**이다.
지터 0.35는 전선이 화면 절반에 흩뿌려져 "밤이 온다"가 아니라 노이즈로 읽혔고, 엣지 글로우도 지터와 겹쳐
넓게 반짝이면 "반짝인다"로 읽힌다.

## 7. 미확정 / TODO

- [ ] **밤 종료 자동화** (부분 착수, #17): `MonsterSpawn`이 웨이브 클리어(스폰 완료 후 생존 0) 시 `EndNight()`를 자동 호출하도록 연결. 단 "클리어"가 아직 처치가 아니라 본진 도달-디스폰 기준(처치 기반은 Enemy 병합 후 — WL-038)이고, "웨이브 실패/보스 처치" 판정 연동과 임시 버튼(`NightActionPanelView`) 제거는 WL-018 잔여. 자동 타이머로 되돌릴 일이 생기면 `DayNightManager.cs`에 주석 처리된 `NightTimerRoutine` 코루틴(UniTask로 교체 예정)을 참고
- [x] **자원 정산 / 주민 배치 초기화**: `ManagementController.HandleNightToDay()`로 구현 완료(#66). `OnNightToDay` 시점에 정산(먼저)→초기화(그 다음) 순서로 실행
- [ ] **본진 체력 회복**: 이벤트 훅(`OnDayStart`)만 존재, 실제 로직은 본진/체력 소유 시스템(미구현)이 구독해서 채워야 함
- [x] **낮/밤 전환 연출**: `DayNightLightingController.cs`로 구현 완료(#7, §6 참고)
- [x] **부드러운 전환(UniTask)**: `DayNightTransition`으로 구현 완료(#101, §6.1). 단순 Lerp가 아니라 **셀 와이프**다 — 씬 라이팅은 전역 보간, "셀이 먼저 밤이 되는" 부분만 풀스크린 패스가 담당
- [ ] **전환 중 입력·트리거 잠금** — **#101에서 분리됐다(팀 결정 2026-08-07). #101은 전환 연출로 닫는다.**
  #101 원문의 잠금 명세는 "라이트 전환만 UniTask Lerp로 만든다"는 전제 위에 쓰였는데, 구현이 셀 와이프 +
  전역 블렌드 구조로 가면서 그 전제가 바뀌었다. 잠금 대상·시점을 새 구조 기준으로 다시 정의해야 하므로
  원문 명세를 그대로 소화하지 않고 **별도 축(WL-162)으로 옮겼다.**
  `DayNightTransition.IsTransitioning`과 `OnTransitionComplete`는 그 배선을 위해 미리 뚫어 둔 것이며
  **현재 소비처는 0이다.** 재정의 시 후보로 남는 진입점:
  - [ ] 몬스터 웨이브 시작 — `StageBuilder.GenerateNextStage`가 `OnDayToNight`에서 동기로 `monsterSpawn.StartRound` 호출 → `OnTransitionComplete`를 기다리도록 재배선
  - [ ] 낮 종료/웨이브 성공 버튼 재클릭 — `ManagementController.RequestAdvancePhase`, `NightActionPanelView`
  - [ ] 영토 확장 클릭 — `TerritoryController.TryClaim`(`HasExpandedToday`가 `OnDayStart`에서 즉시 리셋됨)
  - [ ] 주민 배치 변경 — `ManagementController.IsDay`가 즉시 true가 됨
  - 타워 배치 게이팅은 이번 범위 밖(WL-019·#71) — 게이팅이 붙으면 같은 이유로 전환 중 배치 시작을 막아야 한다
- [ ] **낮/밤 트리거 UI**: 지금은 임시 버튼 4개(밤: 웨이브 성공/실패/보스 처치, 낮: 낮 종료, `NightActionPanelView`/`ManagementPanelView`, #66). "웨이브 실패"/"보스 처치"도 Combat의 실제 웨이브 실패·보스 사망 판정 연동 전까지의 임시 대체물. 실제 UI 버튼/디자인 확정 필요

## 8. 참고

- GDD 관련 시스템: `Docs/GDD.md` §5(게임 루프), §6.6(보상 시스템)
- 팀 계약: `Docs/Review/SystemMap.md` §4 5번("낮/밤 전환 계약"), §5(매니저 수명주기)
