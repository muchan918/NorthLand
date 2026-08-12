# AudioManager 설계 문서

게임 전체 사운드의 **볼륨 소유**와 **BGM 재생·전환**을 담당하는 매니저 문서.
소리를 내는 코드를 새로 쓰거나 설정 패널에서 볼륨을 다룰 때 참고한다.

- 관련 이슈: **#361** (설정 패널 UI는 **#346**)
- 구현 위치: `Assets/Scripts/GameManager/AudioManager.cs`, `Assets/Scripts/GameManager/BgmCue.cs`
- 이 문서는 **현재 구현된 구조**를 정리한 것이다. 코드를 바꾼 사람은 이 문서도 함께 갱신해 어긋나지 않게 유지한다. 미구현 항목은 [7. 미확정/TODO](#7-미확정--todo)에 모아둔다.

> ⚠️ **지금 SFX 볼륨은 아무 소리에도 걸리지 않는다.** 믹서를 쓰지 않으므로 볼륨은 `AudioManager`가 소유한
> `AudioSource`에만 적용된다. SFX 재생 경로가 아직 없어서 `Sfx` 채널은 값만 보관·노출되는 상태다(§2).

## 1. 목적 · 핵심 원칙

**볼륨의 단일 소유자**: "지금 볼륨이 얼마인지"는 `AudioManager` 하나만 안다. UI(슬라이더·토글)는 값을
소유하지 않고 `SetVolume`/`SetMuted`로 밀어 넣고 `OnAudioSettingsChanged`로 되받는다.

**"어떤 곡을 틀지"는 매니저가 모른다**: `AudioManager`는 크로스페이드 엔진이고, 트랙 선택은 씬 쪽
`BgmCue`가 한다(§4). 매니저에 씬·페이즈 지식을 넣지 않는다.

## 2. 왜 AudioMixer를 쓰지 않는가

채널이 Master/BGM/SFX 3개뿐이고 더킹·스냅샷·저역 필터 요구가 없다. 믹서 에셋 신설 + 모든
`AudioSource`의 `outputAudioMixerGroup` 배선 + 선형↔dB 변환 비용이 지금 얻는 것보다 크다고 판단했다.

대신 `AudioManager`가 볼륨 값을 소유하고 **자기가 소유한** `AudioSource.volume`에 곱해 넣는다.

**대가를 명확히 한다 — 매니저를 거치지 않는 재생은 볼륨 제어를 받지 못한다.**

| | 상태 |
|---|---|
| BGM | ✅ 매니저가 소스를 직접 소유 → 볼륨·음소거가 즉시 걸린다 |
| SFX | ❌ **재생 경로 없음.** 값만 저장·노출된다. 설정 패널의 SFX 슬라이더는 그때까지 무음 동작 |

유일한 기존 재생 지점은 `SkillManager`의 `AudioSource.PlayClipAtPoint`(스킬 착탄음)이고 아직 이관되지
않았다. 새 재생 경로를 만드는 쪽은 **반드시 `GetEffectiveVolume(channel)`을 곱해야** 볼륨 제어를 받는다 —
믹서가 없는 이상 이것이 유일한 연결 고리다.

더킹·스냅샷·저역 필터가 실제로 필요해지면 그때 AudioMixer 도입을 재검토한다.

## 3. 볼륨 모델

채널 3개(`AudioChannel.Master` / `Bgm` / `Sfx`) 각각 **0~1 선형 값 + 음소거 토글**을 갖는다.

```
실효 볼륨(채널) = Master 음소거면        0
                  채널이 음소거면        0
                  아니면  MasterVolume × 채널Volume      (Master 자신은 MasterVolume)
```

- 믹서가 없으므로 **dB 변환이 없다** — 슬라이더의 0~1을 그대로 곱한다.
- 기본값 **Master 1.0 / BGM 0.5 / SFX 0.8**. BGM은 배경이라 낮게 시작한다(밸런싱 축, 청감 확인 후 조정 가능).
- **음소거와 슬라이더는 서로를 건드리지 않는다.** `SetVolume`은 음소거 상태를 바꾸지 않고, 음소거는 값을
  0으로 덮어쓰지 않는다 — 토글을 풀면 원래 슬라이더 위치로 돌아온다. "음소거 중 슬라이더를 움직이면
  자동 해제" 같은 UX 정책은 **설정 패널(#346) 몫**이다.

### 3.1 영속화

`LocalizationManager`의 `SelectedLocale` 선례를 따라 **`PlayerPrefs`** 를 쓴다. 슬롯과 무관한 기기 공통
설정이고, #342가 말하는 공통 `settings.json`은 아직 코드에 존재하지 않는다 — 실제로 생기면 그때 이관한다.

| 키 | 타입 | 기본값 |
|---|---|---|
| `MasterVolume` / `BgmVolume` / `SfxVolume` | float (0~1) | 1.0 / 0.5 / 0.8 |
| `MasterMuted` / `BgmMuted` / `SfxMuted` | int (0·1) | 0 |

**쓰기는 즉시, 디스크 flush는 지연한다.** `PlayerPrefs.SetFloat`은 메모리 캐시라 슬라이더 드래그마다
불러도 싸지만 `PlayerPrefs.Save()`는 디스크 쓰기다. `Save()`는 `OnApplicationQuit` /
`OnApplicationPause(true)` / `OnDestroy`에서만 호출한다(dirty 플래그로 불필요한 쓰기 차단).
로드 값은 `Mathf.Clamp01`을 거친다 — 손상된 prefs가 1을 넘는 볼륨으로 들어오는 것을 막는다.

## 4. BGM 크로스페이드

`AudioSource` **2개**(같은 GameObject의 컴포넌트 2개)를 번갈아 쓴다. 둘 다 `loop = true`,
`playOnAwake = false`, `spatialBlend = 0`(2D — 씬 카메라의 `AudioListener` 위치와 무관).

### 4.1 페이드 가중치와 실효 볼륨은 분리한다

```
source.volume = fadeWeight(0~1) × 실효 볼륨(Bgm)
```

둘을 합쳐 `volume`을 직접 밀면 **페이드 도중 슬라이더를 움직였을 때 목표 볼륨이 덮어써진다.**
`Update`에서 매 프레임 위 식으로 다시 곱하므로 슬라이더 실시간 반영이 공짜로 따라온다.

### 4.2 페이드는 `unscaledDeltaTime`으로 진행한다

`GameSpeedController`가 일시정지에서 `Time.timeScale = 0`을 걸고(`GameSpeedController.cs:176`),
**설정 패널을 여는 것 자체가** `GamePauseReason.Settings` 정지다. scaled를 쓰면 패널을 연 채로는
페이드가 얼어붙는다.

### 4.3 그 밖의 규칙

- **같은 클립 재요청은 무시한다.** 씬 재로드나 페이즈 전환에서 트랙이 같으면 끊지 않고 이어 재생한다.
- **`null` 클립도 조용히 무시한다.** BGM 에셋 확보 전까지 `BgmCue` 필드가 비어 있어도 씬이 깨지지 않는다.
- **페이드 도중 재요청**이면 재사용할 소스가 옛 트랙을 물고 있으므로 `Stop()` 후 재생한다(트랙 3개가
  동시에 겹치지는 않는다 — 소스가 2개뿐이라 가장 오래된 것이 즉시 버려진다).
- **배속에서 피치를 건드리지 않는다.** `Time.timeScale`은 `AudioSource.pitch`에 영향을 주지 않으며,
  여기에 배속을 곱하는 코드를 넣지 않는다. 음악이 반음 올라가는 건 배속의 의도가 아니다.
- **일시정지 중에도 BGM은 흐른다.** 프로젝트는 `AudioListener.pause`를 어디서도 쓰지 않는다.

### 4.4 부팅

씬에 배치하지 않는다. `GameSceneManager`와 동일하게
`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`로 자체 부팅 + `DontDestroyOnLoad`.
두 씬 모두에서 필요하고, 씬에 두면 씬 파일 병합 충돌만 늘어나기 때문이다(`SceneWorkflow.md`).
씬에 수동 배치되는 경우를 대비한 중복 파괴 가드는 `Awake`에 있다.

## 5. 씬 배선 — `BgmCue`

`AudioManager`는 `DontDestroyOnLoad`라 **인스펙터 배선을 가질 수 없다.** 그래서 클립 배선과 낮/밤 구독은
씬 컴포넌트가 맡는다. 부수 효과로, 매니저가 씬마다 죽는 `DayNightManager.Instance`를 재구독·해제하는
수명 문제가 아예 생기지 않는다.

| 필드 | 의미 |
|---|---|
| `dayClip` | 낮 트랙. 페이즈가 없는 씬(타이틀)에서는 이 클립만 쓴다 |
| `nightClip` | 밤 트랙. **비워두면 페이즈 전환을 구독하지 않는다** |
| `fadeSeconds` | 트랙 교체 크로스페이드 길이(초). 기본 1 |

- 씬당 **1개** 배치한다.
- `Start`에서 `DayNightManager.Instance`의 `OnDayToNight`/`OnNightToDay`를 구독하고 `OnDestroy`에서
  해제한다. `DayNightManager`가 없는 씬(타이틀)에서는 구독하지 않고 `dayClip`만 1회 재생한다.
- 초기 1회는 `CurrentPhase`를 읽어 결정한다. 세이브 복원은 v1에서 **낮 페이즈만** 지원하므로
  (`RunSaveManager.Progress.cs`) 복원 타이밍과 어긋날 여지가 없다 — 밤 복원이 지원되면 재검토한다.
- 밤 트랙이 비어 있으면 밤에도 낮 트랙을 유지한다(같은 클립 재요청은 매니저가 무시한다).

## 6. 공개 API

```csharp
public enum AudioChannel { Master, Bgm, Sfx }

AudioManager.Instance                                  // BeforeSceneLoad 자체 부팅 — 항상 존재

float GetVolume(AudioChannel channel);
void  SetVolume(AudioChannel channel, float value01);  // 0~1 clamp. 음소거는 건드리지 않는다
bool  IsMuted(AudioChannel channel);
void  SetMuted(AudioChannel channel, bool muted);      // 볼륨 값은 보존

event Action OnAudioSettingsChanged;                   // 볼륨·음소거 변경 통지

float GetEffectiveVolume(AudioChannel channel);        // AudioSource.volume에 곱할 계수

void PlayBgm(AudioClip clip, float fadeSeconds = 1f);  // 같은 클립·null은 무시
void StopBgm(float fadeSeconds = 1f);
```

- 설정 패널(#346)은 `Get*`으로 슬라이더 초기값을 읽고, `Set*`으로 밀고, `OnAudioSettingsChanged`로
  코드 쪽 변경을 따라온다.
- **새 재생 경로를 만드는 쪽은 `GetEffectiveVolume`을 곱한다**(§2).

## 7. 미확정 / TODO

- [ ] **SFX 재생 API·풀링** — `PlaySfx(clip, position)`, `AudioSource` 풀, 동시재생 상한,
      `SkillManager.cs:269`의 `PlayClipAtPoint` 이관. 구조는 **중앙 풀 + `AudioClip` 직접 전달**로
      방향을 잡아뒀다(사운드 뱅크 SO·`SoundId` enum은 도입하지 않는다). **이게 끝나야 SFX 볼륨이
      실제 소리에 걸린다**
- [ ] **BGM 클립 에셋** — 현재 프로젝트에 BGM으로 쓸 클립이 하나도 없다(`Assets/Imported` 안의 SFX뿐).
      확보 전까지 `BgmCue` 필드는 비워둔다
- [ ] **`BgmCue` 씬 배치** — `TitleScene`/`GameScene`에 각 1개. 정본 씬 편집이라 `SceneWorkflow.md` §4
      병합 절차(개인 복사본 → `Branches/` 스냅샷 → 정본 확정)를 거쳐야 한다
- [ ] **설정 패널 슬라이더·토글 UI** → #346
- [ ] **UI 클릭·호버 공용 사운드** — SFX 재생 경로 이후
- [ ] **3D 사운드(거리 감쇠)·더킹·스냅샷** — 필요해지면 AudioMixer 도입을 재검토(§2)
- [ ] **`settings.json` 이관** — #342의 슬롯 무관 공통 설정이 실제로 생기면 `PlayerPrefs`에서 옮긴다

## 8. 참고

- 시스템 맵: `Docs/Review/SystemMap.md` §1 Audio 행, §2 Audio 공개 API
- 씬 편집 절차: `Docs/Core/SceneWorkflow.md` §4
- 일시정지·배속: `Assets/Scripts/UI/GameSpeedController.cs` (`GamePauseReason.Settings`)
