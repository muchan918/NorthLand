# AudioManager 설계 문서

게임 전체 사운드의 **볼륨 소유**와 **BGM 재생·전환**을 담당하는 매니저 문서.
소리를 내는 코드를 새로 쓰거나 설정 패널에서 볼륨을 다룰 때 참고한다.

- 관련 이슈: **#361** (설정 패널 UI는 **#346**)
- 구현 위치: `Assets/Scripts/GameManager/AudioManager.cs`, `Assets/Scripts/GameManager/BgmCue.cs`
- 이 문서는 **현재 구현된 구조**를 정리한 것이다. 코드를 바꾼 사람은 이 문서도 함께 갱신해 어긋나지 않게 유지한다. 미구현 항목은 [7. 미확정/TODO](#7-미확정--todo)에 모아둔다.

> ⚠️ **SFX는 2D 원샷 경로만 있다.** 믹서를 쓰지 않으므로 볼륨은 `AudioManager`가 소유한 `AudioSource`에만
> 적용된다. `PlaySfx`(§6)를 거치는 소리 — 현재는 낮/밤 전환 스팅어 — 만 SFX 볼륨을 따르고,
> `SkillManager`의 `PlayClipAtPoint`는 아직 통제 밖이다(§2).

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
| SFX (`PlaySfx` 경유) | ✅ 2D 원샷만. 현재 소비처는 낮/밤 전환 스팅어 2개 |
| SFX (그 밖) | ❌ `SkillManager`의 `AudioSource.PlayClipAtPoint`(스킬 착탄음)는 **통제 밖** — 볼륨·음소거가 걸리지 않는다 |

새 재생 경로를 만드는 쪽은 `PlaySfx`를 쓰거나, 직접 소스를 굴린다면 **반드시
`GetEffectiveVolume(channel)`을 곱해야** 볼륨 제어를 받는다 — 믹서가 없는 이상 이것이 유일한 연결 고리다.

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

## 4.5 오디오 에셋 · 임포트 설정

에셋은 아트 저장소에 있다: `Assets/Imported/@NorthLand/Sound/Bgm`(낮·밤 BGM),
`.../Sound/Effect`(낮↔밤 전환 스팅어).

⚠️ **`Assets/Imported`는 별도 저장소다** — 클립 추가나 `.meta`(임포트 설정) 변경은 **본 저장소 diff에
보이지 않는다.** 미동기화 상태에서는 에러 없이 **소리만 조용히 사라진다**(WL-040과 같은 축).

임포트 설정은 기본값(`DecompressOnLoad` + Vorbis)에서 아래처럼 바꿨다. 플랫폼 오버라이드는 두지 않았으므로
`defaultSettings`가 PC·Mobile 양쪽에 그대로 적용된다.

| | 클립 | Load Type | Preload | Load In Background | 근거 |
|---|---|---|---|---|---|
| BGM | 89.8s / 87.7s 2ch | **Streaming** | off | **on** | `DecompressOnLoad`면 두 트랙이 각각 30.2MB·32.1MB로 풀린다. 크로스페이드는 둘을 동시에 물므로 **62MB가 상주**한다 |
| 전환 스팅어 | 1.4s / 1.5s 2ch | DecompressOnLoad | **on** | off | 0.5MB짜리 짧은 소리라 압축 해제가 맞다. preload를 켜야 첫 재생이 늦지 않는다 |

- 스트리밍 + `preload=off` 조합에서 `Play()`는 `loadState=Loading`인 채로 받아들여지고, 로드가 끝나면
  이어서 재생된다(실측 확인). 그래서 첫 재생에 메인 스레드가 멈추지 않는다.
- **Vorbis quality는 100% 그대로**다. 90초 스테레오를 100%로 재인코딩하면 빌드 용량이 원본 mp3보다
  커질 수 있으나, 낮추는 것은 청감 tradeoff라 임의로 정하지 않았다(§7 TODO).

⚠️ **임포트 설정에는 클립별 게인(볼륨)이 없다.** `AudioImporter`가 가진 것은 로드 타입·압축·품질·
샘플레이트·강제 모노뿐이다(`.meta`의 `normalize`는 게인이 아니라 **모노 다운믹스 시 정규화** 플래그이고,
`forceToMono`가 꺼져 있으면 아무 효과도 없다). 특정 클립이 너무 크면 방법은 둘뿐이다 —
**파일을 낮은 게인으로 다시 내보내거나, 재생 시 배율을 곱하거나.** 전환 스팅어는 후자를 쓴다(§5).

측정값(2026-08-12): `DayToNight` peak **-0.8 dBFS** / RMS -17.4 dBFS, `NightToDay` peak -1.0 dBFS /
RMS -17.8 dBFS. 둘 다 피크가 0 dBFS 코앞까지 마스터링돼 있어 SFX 볼륨을 그대로 곱하면 BGM 위에서 과하다.

## 5. 씬 배선 — `BgmCue`

`AudioManager`는 `DontDestroyOnLoad`라 **인스펙터 배선을 가질 수 없다.** 그래서 클립 배선과 낮/밤 구독은
씬 컴포넌트가 맡는다. 부수 효과로, 매니저가 씬마다 죽는 `DayNightManager.Instance`를 재구독·해제하는
수명 문제가 아예 생기지 않는다.

| 필드 | 의미 |
|---|---|
| `dayClip` | 낮 트랙. 페이즈가 없는 씬(타이틀)에서는 이 클립만 쓴다 |
| `nightClip` | 밤 트랙. 비워두면 밤에도 낮 트랙을 유지한다 |
| `fadeSeconds` | 트랙 교체 크로스페이드 길이(초). 기본 1 |
| `dayToNightClip` / `nightToDayClip` | 전환 **순간에만** 1회 울리는 스팅어. SFX 채널 볼륨을 따른다 |
| `stingerVolume` | 스팅어 재생 배율(0~1). 임포트 설정에 클립별 게인이 없어(§4.5) 여기서 줄인다. 코드 기본값 0.35(≈ -9dB), **정본 씬 현재 값 0.4**(청감 조정). 두 클립의 레벨 차가 0.4dB뿐이라 공용 배율 하나로 충분하다 |

- 씬당 **1개** 배치한다. 현재 `GameScene`에만 있다 — 타이틀 트랙 에셋이 아직 없어 `TitleScene`은 비어 있다.
- `Start`에서 `DayNightManager.Instance`의 `OnDayToNight`/`OnNightToDay`를 구독하고 `OnDestroy`에서
  해제한다. 밤 트랙이 없어도 스팅어만 쓸 수 있으므로 **페이즈가 있는 씬이면 항상 구독**한다.
  `DayNightManager`가 없는 씬(타이틀)에서는 구독하지 않고 `dayClip`만 1회 재생한다.
- 스팅어는 전환 이벤트에서만 울린다 — `Start`의 초기 트랙 지정은 `PlayDay`/`PlayNight`를 직접 부르므로
  게임 시작이나 씬 로드에 전환음이 딸려 나오지 않는다.
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

void PlaySfx(AudioClip clip, float volumeScale = 1f);  // 2D 원샷. 볼륨 0·음소거면 재생 생략
```

`PlaySfx`는 소스 1개 + `PlayOneShot`이다(풀 아님). 그래서 두 가지 제약이 따라온다:

- **볼륨이 호출 시점에 구워진다** — 재생 중 슬라이더를 움직여도 이미 울리는 소리에는 반영되지 않는다.
  1~2초짜리 짧은 소리 전제다.
- **동시재생 상한이 없다** — `PlayOneShot`은 겹쳐 쌓인다. 프레임마다 부를 만한 소리(타워 발사음 등)는
  이 API가 아니라 풀 기반 경로를 기다린다(§7).

- 설정 패널(#346)은 `Get*`으로 슬라이더 초기값을 읽고, `Set*`으로 밀고, `OnAudioSettingsChanged`로
  코드 쪽 변경을 따라온다.
- **새 재생 경로를 만드는 쪽은 `GetEffectiveVolume`을 곱한다**(§2).

## 7. 미확정 / TODO

- [ ] **SFX 풀링·3D** — `PlaySfx(clip, position)`, `AudioSource` 풀, 동시재생 상한,
      `SkillManager.cs:269`의 `PlayClipAtPoint` 이관. 구조는 **중앙 풀 + `AudioClip` 직접 전달**로
      방향을 잡아뒀다(사운드 뱅크 SO·`SoundId` enum은 도입하지 않는다). 현재의 2D 원샷 `PlaySfx`는
      전환 스팅어처럼 **드물게 한 번 울리는 소리** 전용이다
- [x] **BGM·전환음 클립 에셋** — `Assets/Imported/@NorthLand/Sound`에 낮·밤 BGM 2개 + 전환 스팅어 2개(§4.5)
- [x] **`BgmCue` 씬 배치** — `GameScene`에 1개 배치·배선 완료
- [ ] **타이틀 BGM** — 트랙 에셋이 없어 `TitleScene`에는 `BgmCue`를 두지 않았다
- [ ] **Vorbis quality 조정** — 현재 100%. BGM 90초 스테레오라 빌드 용량 관점에서 낮출 여지가 있으나
      청감 tradeoff라 미결(§4.5)
- [ ] **설정 패널 슬라이더·토글 UI** → #346
- [ ] **UI 클릭·호버 공용 사운드** — 풀 기반 경로 이후
- [ ] **더킹·스냅샷** — 필요해지면 AudioMixer 도입을 재검토(§2)
- [ ] **`settings.json` 이관** — #342의 슬롯 무관 공통 설정이 실제로 생기면 `PlayerPrefs`에서 옮긴다

## 8. 참고

- 시스템 맵: `Docs/Review/SystemMap.md` §1 Audio 행, §2 Audio 공개 API
- 씬 편집 절차: `Docs/Core/SceneWorkflow.md` §4
- 일시정지·배속: `Assets/Scripts/UI/GameSpeedController.cs` (`GamePauseReason.Settings`)
