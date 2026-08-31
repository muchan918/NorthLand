# AudioManager 설계 문서

게임 전체 사운드의 **볼륨 소유**와 **BGM 재생·전환**을 담당하는 매니저 문서.
소리를 내는 코드를 새로 쓰거나 설정 패널에서 볼륨을 다룰 때 참고한다.

- 관련 이슈: **#361** (설정 패널 UI는 **#346**)
- 구현 위치: `Assets/Scripts/GameManager/` — `AudioManager.cs`(재생 엔진·볼륨), `SoundCue.cs`+`TitleCue.cs`+`InGameCue.cs`(씬 큐, 5장), `SfxBank.cs`+`Sfx.cs`(공용 효과음 뱅크, 5.4절), `UiClickSfx.cs`+`UiClickSfxIgnore.cs`(버튼 클릭 전역 훅)
- 이 문서는 **현재 구현된 구조**를 정리한 것이다. 코드를 바꾼 사람은 이 문서도 함께 갱신해 어긋나지 않게 유지한다. 미구현 항목은 [7. 미확정/TODO](#7-미확정--todo)에 모아둔다.

> SFX는 화면 전역 2D 경로(`PlaySfx`/`PlaySfxExclusive`)와 전투 위치 기반 풀(`CombatSfx`)로 나뉜다.
> 후자는 Unity 3D 거리가 아니라 카메라 줌·화면 위치로 감쇠하며 매 프레임 SFX 설정을 반영한다(§6.2).

## 1. 목적 · 핵심 원칙

**볼륨의 단일 소유자**: "지금 볼륨이 얼마인지"는 `AudioManager` 하나만 안다. UI(슬라이더·토글)는 값을
소유하지 않고 `SetVolume`/`SetMuted`로 밀어 넣고 `OnAudioSettingsChanged`로 되받는다.

**"어떤 곡을 틀지"는 매니저가 모른다**: `AudioManager`는 크로스페이드 엔진이고, 트랙 선택은 씬 쪽
`SoundCue` 계층이 한다(§5). 매니저에 씬·페이즈 지식을 넣지 않는다. 대신 **씬마다 큐가 하나씩 있어야
한다**는 계약이 따라온다 — 큐가 없는 씬은 직전 씬의 BGM을 그대로 끌고 간다(§5.1).

## 2. 왜 AudioMixer를 쓰지 않는가

채널이 Master/BGM/SFX 3개뿐이고 더킹·스냅샷·저역 필터 요구가 없다. 믹서 에셋 신설 + 모든
`AudioSource`의 `outputAudioMixerGroup` 배선 + 선형↔dB 변환 비용이 지금 얻는 것보다 크다고 판단했다.

대신 `AudioManager`가 볼륨 값을 소유하고 **자기가 소유한** `AudioSource.volume`에 곱해 넣는다.

**대가를 명확히 한다 — 매니저를 거치지 않는 재생은 볼륨 제어를 받지 못한다.**

| | 상태 |
|---|---|
| BGM | ✅ 매니저가 소스를 직접 소유 → 볼륨·음소거가 즉시 걸린다 |
| SFX (`PlaySfx` 경유) | ✅ 2D 원샷. 소비처는 낮/밤 전환 스팅어 2 + 공용 효과음 10(튜토리얼 Bubble·Popup 안내음 포함, §5.4) |
| SFX (`PlaySfxExclusive` 경유) | ✅ 2D 전용 소스. 소비처는 주민 증가음 + 결과창 승리/패배 스팅어 **2개**. **매 프레임 볼륨을 다시 곱하므로 재생 중 슬라이더도 반영된다** |
| 전투 위치 SFX (`CombatSfx`) | ✅ 중앙 보이스 풀에서 매 프레임 `GetEffectiveVolume(Sfx)`를 곱한다 |

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

오디오 설정은 슬롯과 무관한 공통 `settings.json`에 저장한다. `AudioManager`는 시작할 때
`GameSettingsService.CurrentSettings`에서 Master/BGM/SFX 볼륨과 음소거 상태를 읽는다.

슬라이더나 음소거 상태가 변경되면 `SetAudioVolume` 또는 `SetAudioMuted`를 통해 메모리 설정을 갱신한다.
실제 파일 저장은 설정 패널을 닫을 때 `GameSettingsService.TrySaveCurrentSettings()`가 담당한다.
로드한 볼륨은 `Mathf.Clamp01`을 거친다 — 손상된 값이 1을 넘는 볼륨으로 들어오는 것을 막는다.

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
- **`null` 클립도 조용히 무시한다.** BGM 에셋 확보 전까지 `InGameCue`/`TitleCue` 필드가 비어 있어도 씬이 깨지지 않는다.
- **페이드 도중 재요청**이면 재사용할 소스가 옛 트랙을 물고 있으므로 `Stop()` 후 재생한다(트랙 3개가
  동시에 겹치지는 않는다 — 소스가 2개뿐이라 가장 오래된 것이 즉시 버려진다).
- **나가는 소스는 스왑 시점의 가중치를 기억한다**(`outgoingWeight`). 페이드인 중이던 소스가 그대로
  페이드아웃 대상이 되는데, `fadeProgress`가 0으로 리셋되므로 `(1 - fadeProgress)`만 곱하면 **최대
  볼륨으로 점프한 뒤 내려온다.** 가중치 0.9에서 끊으면 0.45여야 할 볼륨이 0.5로 튄다(실측 확인).
  정착 후 교체라면 `outgoingWeight`가 1이라 식이 동일하다.
- **배속에서 피치를 건드리지 않는다.** `Time.timeScale`은 `AudioSource.pitch`에 영향을 주지 않으며,
  여기에 배속을 곱하는 코드를 넣지 않는다. 음악이 반음 올라가는 건 배속의 의도가 아니다.
- **일시정지 중에도 BGM은 흐른다.** 프로젝트는 `AudioListener.pause`를 어디서도 쓰지 않는다.

### 4.4 부팅

씬에 배치하지 않는다. `GameSceneManager`와 동일하게
`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`로 자체 부팅 + `DontDestroyOnLoad`.
두 씬 모두에서 필요하고, 씬에 두면 씬 파일 병합 충돌만 늘어나기 때문이다(`SceneWorkflow.md`).
씬에 수동 배치되는 경우를 대비한 중복 파괴 가드는 `Awake`에 있다.

## 4.5 오디오 에셋 · 임포트 설정

에셋은 아트 저장소에 있다: `Assets/Imported/@NorthLand/Sound/Bgm`(낮·밤 BGM), `.../Sound/Effect`(낮↔밤 전환 스팅어),
`.../Sound/UI`(버튼 클릭·패널 오픈), `.../Sound/Feedback`(타워 설치·거절·주민 증가), `.../Sound/Resident`(주민 목소리).

⚠️ **`Assets/Imported`는 별도 저장소다** — 클립 추가나 `.meta`(임포트 설정) 변경은 **본 저장소 diff에
보이지 않는다.** 미동기화 상태에서는 에러 없이 **소리만 조용히 사라진다**(WL-040과 같은 축).

임포트 설정은 기본값(`DecompressOnLoad` + Vorbis)에서 아래처럼 바꿨다. 플랫폼 오버라이드는 두지 않았으므로
`defaultSettings`가 PC·Mobile 양쪽에 그대로 적용된다.

| | 클립 | Load Type | Preload | Load In Background | 근거 |
|---|---|---|---|---|---|
| BGM | 89.8s / 87.7s 2ch | **Streaming** | off | **on** | `DecompressOnLoad`면 두 트랙이 각각 30.2MB·32.1MB로 풀린다. 크로스페이드는 둘을 동시에 물므로 **62MB가 상주**한다 |
| 전환 스팅어 | 1.4s / 1.5s 2ch | DecompressOnLoad | **on** | off | 0.5MB짜리 짧은 소리라 압축 해제가 맞다. preload를 켜야 첫 재생이 늦지 않는다 |
| 공용 효과음 5본 | 0.27~9.46s 2ch | DecompressOnLoad | **on** | off | 스팅어와 같은 규약. 임포트 기본값이 `preload=off`라 **5본 모두 켜 줘야 했다** — 끄면 첫 재생이 늦는데, 클릭음에서 제일 티가 난다 |

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

### 4.5.1 공용 효과음 6본 — 출처와 편집

**6본 전부 `Assets/Imported/CartoonSFX` 팩 파일을 이름만 바꿔 복사한 것이다**(해시 대조로 확인).
그래서 **편집이 되돌릴 수 있다** — 팩 원본이 그대로 남아 있으므로 잘못되면 다시 복사하면 된다.
편집은 반드시 `@NorthLand/Sound/` 복사본에만 한다(팩 원본은 다른 데모 씬이 참조한다).

| `@NorthLand/Sound/…` | 팩 원본 (`CartoonSFX/UpdateOne/…`) |
|---|---|
| `UI/SFX_UI_Btn_Click` | `SlideUp/SFX_SlideUp_SineWave_03` |
| `UI/SFX_Panel_Open` | `SlideUp/SFX_SlideUp_Organic_04` |
| `Feedback/SFX_Tower_Install` | `Miscellaneous/SFX_Synth_BigSlide_01_wav` |
| `Feedback/SFX_Tower_CantPlace` | `Miscellaneous/SFX_Error_Synth_Funny` |
| `Feedback/SFX_Castle_ResidentIncrease` | `Miscellaneous/SFX_MusicalFanfare_Happy_01` |
| `Feedback/SFX_BuildingUpgrade` | `ExageratedImpact/SFX_ExageratedImpact_Cannon_Reverb_Explosion` |

#### 타워 전투음 14본 — 출처와 편집 (#540)

`@NorthLand/Sound/Towers/`에 있다. **`.wav`만 복사했고 `.meta`는 Unity가 새로 발급했다** — 위 ⚠의
GUID 충돌을 되풀이하지 않기 위함이고, 실제로 팩 원본 쪽에 수정이 하나도 남지 않은 것을 확인했다.
임포트 설정은 14본 전부 `DecompressOnLoad` + `preload` + Vorbis 100%로 통일했다.

아래 매핑은 **MD5 해시 대조로 확인**했다(위 6본과 같은 방식).

| `Sound/Towers/…` | 팩 원본 | 편집 |
|---|---|---|
| `SFX_Tower_Archer_Fire` | `Vefects/…/Anime VFX/SFX_Vefects_Anime_Stylized_Arrow_Shot_Cast` | 없음 |
| `SFX_Tower_Cannon_Fire` | `Particles/Vefects/Pixel Craft…/Fer/SFX_Vefects_Gun_Smoke_01` | 없음 |
| `SFX_Tower_KillStack_Fire` | `Vefects/…/Anime VFX/SFX_Vefects_Anime_Stylized_Gun_Shot` | 없음 |
| `SFX_Tower_IncendiaryCannon_Fire` | `Vefects/…/Flipbook VFX/SFX_Vefects_Flipbook_Fire_Burst_01` | 없음 |
| `SFX_Tower_Beam_Loop_1` | `Vefects/…/Free Fire VFX/SFX_Vefects_Fire_Small_L` | 없음 |
| `SFX_Tower_Beam_Loop_2` | `Vefects/…/Free Fire VFX/SFX_Vefects_Fire_Medium_L` | 없음 |
| `SFX_Tower_Beam_Loop_3` | `Vefects/…/Free Fire VFX/SFX_Vefects_Fire_Big_L` | 없음 |
| `SFX_Tower_Gatling_Fire` | `Vefects/…/Anime VFX/SFX_Vefects_Anime_Stylized_Gun_Shot` | **앞 0.22초만** + 뒤 5ms 페이드아웃 |
| `SFX_Tower_RampUp_Fire` | `Vefects/…/Flipbook VFX/SFX_Vefects_Flipbook_GunShot_03` | **앞 0.50초만** + 뒤 5ms 페이드아웃 |
| `SFX_Tower_Missile_Fire` | `Particles/MagicArsenal/…/Cast/magic_cast_fire` | **0.07~0.44초 구간** + 앞 3ms·뒤 5ms 페이드 |
| `SFX_Tower_Boomerang_Fire` | *(외부 조달)* | — |
| `SFX_Tower_Sniper_Fire` | *(외부 조달)* | — |
| `SFX_Tower_SodaTower_Impact` | *(외부 조달)* | — |
| `SFX_Tower_SodaCannon_Impact` | *(외부 조달)* | — |

- **빔 루프 3본은 팩 안에서 중복 저작돼 있다** — `Free Fire VFX/SFX_Vefects_Fire_*_L`과
  `Flipbook VFX/SFX_Vefects_Flipbook_Fire_*_Loop`가 바이트 단위로 같은 파일이다(해시 일치).
- **편집한 3본의 공통 근거는 "꼬리가 공격 간격보다 길다"**였다. 특히 `Anime_Stylized_Gun_Shot`은
  **1.02초 중 실제 소리가 0.18초뿐이고 나머지 0.84초가 디지털 무음**이라, 개틀링(간격 0.35초)에
  그대로 쓰면 들리지도 않는 구간에 보이스를 3개씩 물었다.
- `magic_cast_fire`는 길이가 아니라 **포락선이 문제**였다 — 0.10초에 걸쳐 차오르는 시전음이라
  발사 순간과 붙지 않았다(§5.6이 결과창 스팅어에서 다룬 것과 같은 축인데, 그쪽은 재생 시점을
  미뤘고 이쪽은 파일을 잘랐다. **재사용처가 하나뿐이라 파일을 자르는 편이 단순하다**).
- **외부 조달 4본은 팩 원본이 없으므로 되돌릴 수 없다.** 편집이 필요하면 원본을 따로 보관할 것.

> ⚠️ **`SFX_Castle_ResidentIncrease`는 `.meta`까지 함께 복사돼 GUID가 충돌했다.** 복사본이 팩 원본의
> GUID(`b5c64e6e…`)를 가져갔고, Unity가 **팩 원본 쪽에** 새 GUID를 발급했다 — 즉 팩 데모 씬이 참조하던
> 팬파레가 우리 복사본을 가리킨다(소리가 같아 들리지는 않는다). **미해결.** 고치려면 복사본 `.meta`를
> 지워 새 GUID를 받게 하고 팩 원본 `.meta`를 `git checkout`으로 되돌린 뒤, `SfxBank.asset`의 배선을
> 다시 해야 한다(GUID가 바뀌면 참조가 끊긴다). 나머지 5본은 `.meta` 없이 복사돼 깨끗하다.
> **교훈: 팩에서 클립을 가져올 때 `.wav`만 복사한다.**

**편집 내역(2026-08-24).** 원인은 "소리가 게임과 안 붙는다"였고, 포락선을 재서 원인을 갈랐다 —
앞쪽 디지털 무음은 5~18ms로 무시 가능했고, **진짜 지연은 `Tower_Install`의 완만한 스웰**이었다
(RMS 피크가 0.14s, 50% 도달이 0.08s인 슬라이드 음이라 클릭보다 늦게 얹혔다).

| 클립 | 길이 | peak | RMS | 어택(50% 도달) | 한 일 |
|---|---|---|---|---|---|
| `SFX_UI_Btn_Click` | 0.12s | -12.0 dBFS | -19.1 | 0.00s | 앞뒤 무음 트림 |
| `SFX_Panel_Open` | 0.21s | -13.8 dBFS | -25.4 | 0.02s | 〃 |
| `SFX_Tower_CantPlace` | 0.23s | **-5.7 dBFS** | -16.6 | 0.00s | 〃 |
| `SFX_BuildingUpgrade` | 1.53s | **-2.7 dBFS** | -20.5 | 0.00s | 〃 |
| `SFX_Tower_Install` | 1.80s | -12.7 dBFS | -24.7 | **0.00s** | 〃 + **앞 75ms 추가 절단**(스웰 제거 → RMS 피크 0.14s→0.06s) |
| `SFX_Castle_ResidentIncrease` | 9.45s | **-1.0 dBFS** | -22.6 | 6.02s | 앞 무음 트림 + **피크 정규화 -4.0→-1.0 dBFS**(BGM에 묻혀서) |

- 잘라낸 지점의 클릭음을 막으려고 **앞 3ms 페이드인 / 뒤 5ms 페이드아웃**을 넣었다. 무음 구간에서
  자른 클립에는 불필요하지만, 절단면이 생기는 `Tower_Install`에는 필수다.
- **`ResidentIncrease`의 꼬리는 자르지 않았다.** 큰 소리가 6.0초 지점에 있고 그 뒤 잔향이 -40~-56dB로
  빠지는데, **패널을 닫아도 끝까지 울리는 것이 의도**다(주민이 늘어난 것을 축하하는 팡파레).
- **파일 게인과 뱅크 볼륨은 축이 다르다.** 파일 정규화는 "그 클립이 가진 헤드룸을 쓴다"는 일회성·객관적
  조작이고, `SfxBank`의 `Volume`은 "다른 소리 대비 얼마나 크게"라는 튜닝 축이다. 레벨 편차(peak 기준
  11dB)는 **뱅크에서** 잡는다 — 파일을 정규화해 맞추면 같은 축을 두 곳에서 조절하게 된다.

현재 뱅크 볼륨: `buttonClick`·`panelOpen`·`towerInstall` 1.00 / `rejected` 0.45 / `buildingUpgrade` 0.40 /
`residentIncreased` 1.00. 앞의 둘은 원본이 크게 마스터링돼 눌렀고, 마지막은 **일부러 가장 크게** 둔다.

## 5. 씬 배선 — `SoundCue` 계층

`AudioManager`는 `DontDestroyOnLoad`라 **인스펙터 배선을 가질 수 없다.** 그래서 클립 배선과 이벤트 구독은
씬 컴포넌트가 맡는다. 부수 효과로, 매니저가 씬마다 죽는 `DayNightManager.Instance`를 재구독·해제하는
수명 문제가 아예 생기지 않는다.

```
SoundCue        (씬 오브젝트 — 큐를 모아두는 자리)
└── TitleCue    (TitleScene)   또는   InGameCue  (GameScene)
```

- `SoundCue`(abstract) — 공통 베이스. `fadeSeconds`를 소유하고 `PlayBgm`/`StopBgm`/`PlaySfx` 진입점을
  `AudioManager` null 가드와 함께 제공한다. 파생 큐는 "언제 무엇을"만 정한다.
- `TitleCue` — 타이틀 트랙 1개. **클립이 없어도 배치한다**(아래 계약).
- `InGameCue` — 낮/밤 트랙 + 페이즈 전환 스팅어.

### 5.1 계약 — 씬마다 정확히 하나의 큐가 답을 갖는다

매니저가 씬을 넘어 살아남으므로 **답을 갖지 않는 씬은 직전 씬의 BGM을 그대로 끌고 간다.** 실제로
타이틀이 그 사고였다(WL-180): 밤에 게임오버 → "메인으로" → 밤 BGM이 타이틀 화면에서 계속 루프했다.

타이틀 복귀 경로가 넷(`ResultUIManager`의 게임오버·클리어, `RunSaveManager`의 복원 실패 2곳,
`SettingUI`의 "메인으로")이라 **어느 하나를 고쳐서는 닫히지 않는다.** 정지 주체를 씬에 두면 경로 수와
무관해진다 — 그래서 `TitleCue`는 트랙 에셋이 없는 지금도 배치돼 있고, `titleClip`이 비면 `StopBgm()`을
부른다.

> ⚠️ 빈 클립을 `PlayBgm`에 넘기는 것으로는 안 된다 — 매니저가 조용히 무시해 **직전 트랙이 살아남는다.**
> "아무것도 틀지 않는다"는 `StopBgm`으로만 표현된다.

**같은 계약이 긴 SFX에도 적용된다.** `SoundCue.Awake`가 `AudioManager.StopSfxExclusive()`를 부른다 —
`PlaySfxExclusive`는 씬을 넘어 계속 울리는데(§6), 정지를 전환 경로마다 붙이면 경로가 넷이라 어느 하나를
고치는 것으로 닫히지 않는다. 큐가 이미 씬마다 하나씩 있으므로 거기 얹으면 경로 수와 무관해진다(WL-205).
⚠ 파생 큐가 `Awake`를 선언하면 이것이 불리지 않는다 — 지금 두 큐는 `Start`만 쓴다.

새 씬을 추가한다면 `SoundCue` 자식 큐를 함께 두거나, 그 씬이 직전 BGM을 이어받는 것이 의도임을
명시해야 한다.

### 5.2 필드

| 컴포넌트 | 필드 | 의미 |
|---|---|---|
| `SoundCue` | `fadeSeconds` | 트랙 교체 크로스페이드 길이(초). 기본 1 |
| `TitleCue` | `titleClip` | 타이틀 트랙. **비우면 직전 BGM을 페이드아웃하고 무음**(§5.1) |
| `InGameCue` | `dayClip` / `nightClip` | 낮·밤 트랙. 밤 트랙이 비면 밤에도 낮 트랙을 유지한다 |
| `InGameCue` | `dayToNightClip` / `nightToDayClip` | 전환 **순간에만** 1회 울리는 스팅어. SFX 채널 볼륨을 따른다 |
| `InGameCue` | `stingerVolume` | 스팅어 재생 배율(0~1). 임포트 설정에 클립별 게인이 없어(§4.5) 여기서 줄인다. 코드 기본값 0.35(≈ -9dB), **정본 씬 현재 값 0.4**(청감 조정). 두 클립의 레벨 차가 0.4dB뿐이라 공용 배율 하나로 충분하다 |

### 5.3 `InGameCue`의 페이즈 구독

- `Start`에서 `DayNightManager.Instance`의 `OnDayToNight`/`OnNightToDay`를 구독하고 `OnDestroy`에서
  해제한다. 구독 대상을 필드로 붙잡아 두므로 씬 파괴 순서로 `Instance`가 이미 바뀌었어도 자기가 건
  핸들러만 정확히 뗀다. 밤 트랙이 없어도 스팅어만 쓸 수 있으므로 **페이즈가 있는 씬이면 항상 구독**한다.
- 스팅어는 전환 이벤트에서만 울린다 — `Start`의 초기 트랙 지정은 `PlayDay`/`PlayNight`를 직접 부르므로
  게임 시작이나 씬 로드에 전환음이 딸려 나오지 않는다.

> ⚠️ **초기 트랙과 세이브 복원의 순서가 보장되지 않는다.** `InGameCue.Start`가 `CurrentPhase`를 읽어
> 초기 트랙을 고르는데, 그 값을 복원하는 `RunSaveManager`도 `Start`에서 돈다. 씬 오브젝트 사이의 `Start`
> 순서는 Unity가 보장하지 않는다. **지금은 드러나지 않는다** — 세이브 v1이 `Phase != Day` 복원을 아예
> 거부하기 때문이다(`RunSaveManager.Progress.cs`). 밤 세이브를 여는 순간 "밤에서 이어했는데 낮 BGM"이
> 확률적으로 난다. 그때는 `DayNightManager.OnDayStart`(한 프레임 지연 발행이라 복원 이후가 보장된다)를
> 함께 구독하거나 복원 완료 이벤트를 기다리게 한다. (WL-182)
- 초기 1회는 `CurrentPhase`를 읽어 결정한다. 세이브 복원은 v1에서 **낮 페이즈만** 지원하므로
  (`RunSaveManager.Progress.cs`) 복원 타이밍과 어긋날 여지가 없다 — 밤 복원이 지원되면 재검토한다.
- 밤 트랙이 비어 있으면 밤에도 낮 트랙을 유지한다(같은 클립 재요청은 매니저가 무시한다).

### 5.4 씬이 답을 갖지 않는 소리 — `SfxBank` / `Sfx`

§5의 전제("무엇을 틀지는 씬이 정한다")가 **성립하지 않는 소리가 있다.** 버튼 클릭음은 어느 씬에서나 같은
소리라 씬마다 같은 배선을 반복하게 되고, 새 씬을 만든 사람이 빠뜨리면 에러 없이 그 씬만 무음이 된다 —
WL-180이 BGM에서 낸 사고와 정확히 같은 형태다. 씬 큐로는 이 축을 못 막는다.

그래서 **주인이 없는 공용 효과음만** SO 하나가 소유한다.

```
Assets/Resources/ScriptableObjects/SfxBank.asset   (SfxBank — 클립 + 클립별 볼륨)
        ↑ Resources.Load 1회
Sfx (static)   ← 호출부는 이것만 부른다: Sfx.ButtonClick() / PanelOpen() / TutorialBubbleOpened() / TutorialPopupOpened() / ...
        ↓
AudioManager.PlaySfx  또는  PlaySfxExclusive
```

- **`Sfx`는 정적 클래스다** — 씬 배치도 부팅도 없다. 자가 부팅 싱글톤을 하나 더 늘리지 않기 위함(WL-002).
- **`SoundId` enum + 딕셔너리를 두지 않는다**(§7의 방향 그대로). 소리마다 이름 있는 메서드라 오타가 컴파일
  에러로 잡히고 "이 소리가 어디서 나는지"를 참조 찾기로 셀 수 있다.
- **범위를 좁게 유지한다.** 타워 발사음처럼 **클립 주인이 이미 있는 소리는 뱅크에 넣지 않는다** — 그쪽은
  각자의 SO가 클립을 들고 재생 시 직접 넘기는 방향이고(§7), 그 결정은 이 뱅크로 뒤집히지 않는다.
  뱅크가 커지기 시작하면 그건 범위를 벗어났다는 신호다.
- `SfxBank.Cue`는 **struct가 아니라 class다.** struct는 필드 초기화식을 못 가져 새 항목의 `Volume`이 0으로
  태어나고, 그러면 클립을 꽂아도 소리가 안 나서 "배선했는데 무음"으로 시간을 버린다.
- 뱅크 에셋이 없으면 경고 1회 후 조용히 무음이다(클릭마다 `Resources.Load`를 다시 때리지 않는다).

#### 소비처와 호출 지점

| 소리 | 부르는 곳 | 비고 |
|---|---|---|
| `ButtonClick` | `UiClickSfx`(전역 훅) | 버튼별 배선 없음 — 아래 참고 |
| `PanelOpen` | `BuildingInfo.ShowOnly` / `Tower.OnSelected` | **패널이 켜질 때가 아니라 클릭할 때** |
| `TutorialBubbleOpened` | `TutorialOverlay.ShowBubble` | Bubble 루트가 비활성→활성으로 바뀔 때만. 열린 Bubble의 Localization 갱신·닫힘에는 울리지 않음 |
| `TutorialPopupOpened` | `TutorialOverlay.ShowPopup` | Popup 루트가 비활성→활성으로 바뀔 때만. 열린 Popup의 Localization 갱신·닫힘에는 울리지 않음 |
| `TowerInstalled` | `TowerPlacer.PlaceTower` | 합성 결과 배치도 같은 경로를 지나 함께 덮인다 |
| `Rejected` | `TowerPlacer`(배치 반려) · `TowerFusionController`(재료·코스트 부족) · `CastlePanelUI`(주민 증가·본진 업그레이드 실패) · `BuildingInfoUI`(업그레이드 실패) | 지금은 클립 하나를 넷이 공유 |
| `Blocked` | `MouseManager.UpdateSkillTargeting`(스킬 조준 중 타일 밖 클릭) | 커서가 `CursorKind.Blocked`인 자리의 클릭. **배치 모드의 같은 상황은 여전히 `Rejected`를 쓴다** — 아래 참고 |
| `SkillOnCooldown` | `SkillButtonView`(Q 단축키 · **비활성 버튼 클릭**) | 충전 소진일 때만. 낮 페이즈·게임 종료로 못 쓰는 경우는 무음(`SkillManager.IsOnCooldown`) |
| `BuildingUpgraded` | `InGameCue.HandleBuildingAction` (`OnBuildingAction` 구독) | 생산 라인·업그레이드 전용 건물이 같은 소리 |
| `ResidentIncreased` | 〃 | `PlaySfxExclusive` — 클립이 9.5초라 연타 시 겹침 |
| *(결과창 스팅어)* | `ResultPanelAnimator`(승리/패배 패널, 뱅크 밖 인스펙터 배선) | `PlaySfxExclusive` — 승리 클립은 타격이 0.66초 지점이라 `startTime` 0.63으로 앞을 건너뛴다(§5.6) |
| `Undone` | `UndoRequest.Submit` | 되돌리기 버튼과 **Ctrl+Z가 같은 진입점**이라 한 곳에서 난다 |
| `Redone` | *(아직 없음)* | 다시 실행 기능이 없다 — 클립만 뱅크에 꽂아둔 상태 |

> ⚠️ **`Blocked`와 `Rejected`의 경계는 커서 그림이 정한다.** 표면 위에서 규칙에 걸려 반려된 것
> (`Placing` 커서)과 애초에 조작 대상이 아닌 곳(`Blocked` 커서 = `Default-No-32`)은 플레이어가 해야 할
> 다음 행동이 다르다 — 앞은 "다른 타일을 고른다", 뒤는 "지도 위로 커서를 옮긴다"
> (`CursorFeedback.md` §2 참고).
>
> **그런데 배치 모드의 표면 밖 클릭은 지금도 `Rejected`를 부른다**(`MouseManager.UpdatePlacement`의
> `_request.OnRejected`). 두 클립(`SFX_Tower_CantPlace` / `SFX_Error`)이 청감상 거의 같아 굳이 갈지
> 않기로 한 결정이다(#550). 클립이 갈리면 그 분기를 `Sfx.Blocked()`로 옮긴다 — 그때 `Blocked`의
> 호출부가 둘이 되고 `Rejected`는 "표면 위 반려" 전용이 된다.

> ⚠️ **비활성 버튼은 어느 소리 경로도 지나지 않는다.** `Button.interactable == false`면 `onClick`이 아예
> 발화하지 않고 `UiClickSfx`도 `Selectable.IsInteractable()`에서 빠진다 — 그래서 회색 버튼은 눌러도
> **아무 일도 안 일어난 것처럼 보인다**(스킬 쿨다운 중 버튼 연타가 실제로 그랬다). 그 자리를
> `IDisabledClickFeedback`이 메운다: 전역 훅이 비활성 `Selectable`을 눌렀을 때 그 오브젝트가 이 인터페이스를
> 구현하고 있으면 호출하고, 무슨 소리를 낼지는 **버튼 자신이 판단한다**(`ICursorHint`와 같은 패턴).
> 구현체 없는 버튼은 지금처럼 조용히 넘어간다 — 회색 버튼 대부분이 그렇다.
>
> ⚠ 이 훅은 **버튼·토글 타입 필터와 `UiClickSfxIgnore` 제외를 지나지 않는다.** 공용 클릭음이 아니라
> 버튼 자기 소리이므로 자기 규칙으로 낸다. 클릭음을 뺀 버튼(`UiClickSfxIgnore`)이라도 비활성 피드백은
> 별개로 살아 있다.

> ⚠️ **같은 소리는 한 프레임에 한 번만 난다**(`Sfx.ClaimFrame`). 선택 표시의 소유자가 둘이라
> — 대상 자신의 `ISelectable` 훅과 `TowerMergeCoordinator.RefreshPanel` — 타워를 **한 번** 클릭하면
> `Tower.OnSelected`가 같은 프레임에 **두 번** 불린다. 코디네이터 주석이 "표시는 idempotent라 겹쳐도
> 무해"라 적어둔 그대로인데 **소리는 idempotent가 아니라서** 그 소리만 두 겹으로 크게 들렸다.
> 호출부를 하나로 줄이지 않은 이유는 두 번 부르는 것이 그쪽 설계의 의도이기 때문이다(2→1 복귀 시
> 표시 복구) — 소리를 위해 비틀면 사거리 원 잔존 버그가 되살아난다(WL-087).

> ⚠️ **자기 결과음을 내는 버튼은 공용 클릭음에서 뺀다**(`UiClickSfxIgnore.ApplyTo`). 클릭음은 **누를 때**,
> 결과음은 **뗄 때** 나므로 빼지 않으면 두 소리가 연달아 겹쳐 들린다. 현재 제외 대상: 주민 증가 ·
> 업그레이드(본진·건물) · 합성 후보 · 되돌리기. **인스펙터가 아니라 소리를 배선한 코드 옆에서 붙인다** —
> 프리팹·씬 병합 충돌을 만들지 않고("`TowerMergeCandidateHover` 런타임 부착"과 같은 선례), 무엇보다
> "이 버튼은 자기 소리를 낸다"는 사실이 한 자리에 모인다.

**건물 사건의 성공음은 호출부가 아니라 `ManagementController.OnBuildingAction` 구독에서 난다**(WL-208).
같은 사건의 파티클이 이미 그 이벤트를 구독하고 있어(`BuildingFeedback`), 소리만 버튼 핸들러에 손으로
배선하면 트리거가 두 벌로 갈린다 — 진입점이 하나 늘 때 파티클은 자동으로 따라오고 소리만 조용히 빠진다
(실제로 `Test/BuildingsUpgradeHelper`가 그런 경로였다). 구독은 씬 큐(`InGameCue`)에 둔다. "이 씬에서 언제
무엇을 틀지"를 정하는 자리이고, 이미 `DayNightManager`를 같은 패턴으로 구독하고 있다.

**실패는 여전히 호출부 몫이다** — 컨트롤러는 성공만 알린다. 그래서 버튼 핸들러에는 `Sfx.Rejected()`만
남는다. ⚠ **두 버튼 모두 자원이 모자라도 눌린다**(비활성화는 최대 레벨 도달에만 걸린다) — 그 경로가
여태 `Debug.Log`만 남기고 조용히 반려돼 있었다.

> 소리를 늘릴 땐 `InGameCue.HandleBuildingAction`의 분기만 추가한다(호출부는 건드리지 않는다) —
> `BuildingFeedback`가 파티클을 늘리는 방식과 같다. 아직 소리가 없는 `VillagerAssigned`/`VillagerUnassigned`는
> 그냥 지나간다.

**⚠ 패널 오픈음을 `OnEnable`에 걸면 안 된다.** 두 가지가 오발한다 — ① `CastlePanelUI`·`StorePanelUI` 등은
씬 로드 시 **켜진 채로 시작**했다가 `Awake`에서 스스로 닫힌다(`Instance` 등록 때문에 꺼둘 수 없다),
② `PhasePanelSwitcher`가 낮/밤마다 하단 액션 패널을 토글한다(전환 스팅어 위에 겹친다). 그래서 신호를
"패널이 켜졌다"가 아니라 **"플레이어가 클릭했다"** 쪽에 둔다.

**⚠ 같은 대상을 다시 클릭하면 소리가 나지 않는다.** `MouseManager.Select`가 `_selected == next`로 중복
제거하기 때문이다. 패널이 이미 열려 있으므로 의도한 동작이다. 박스·Shift 다중 선택은 `Select`를 거치지
않아 타워를 여러 개 훑어도 겹쳐 쌓이지 않는다.

#### `UiClickSfx` — 버튼 클릭음을 버튼마다 배선하지 않는 이유

`onClick.AddListener`를 쓰는 파일이 30개고, 상점 교환 행·보상 카드·타워 팔레트처럼 **런타임에 생성되는**
버튼이 그중 상당수다. 하나씩 붙이는 방식은 "새 버튼을 만든 사람이 잊으면 그 버튼만 조용히 무음"이 되는데
컴파일러도 리뷰도 그걸 잡지 못한다.

그래서 입력 쪽에 훅을 하나 두고 눌린 대상을 역으로 찾는다(`AudioManager`와 같은
`RuntimeInitializeOnLoadMethod` + `DontDestroyOnLoad`, 씬 배치 0).

- 좌클릭이 눌린 프레임에 `EventSystem.RaycastAll`을 1회 돌리고 **최상단 히트에서 부모로 올라가며** 첫
  `Selectable`을 찾는다. 이건 EventSystem이 실제로 누를 대상을 고르는 규칙(`ExecuteHierarchy`)과 **같은 순서**라
  "소리는 났는데 버튼은 안 눌렸다"가 생기지 않는다. 모달 배경이 위를 덮으면 그쪽이 최상단이 되어 소리가
  나지 않고, 그때는 실제로도 버튼이 안 눌린다.
- `Button`·`Toggle`만 낸다(슬라이더·스크롤바는 "누르는" 조작이 아니다). `IsInteractable()`이 false면 내지 않는다.
- **누르는 순간**에 낸다(뗄 때가 아니라). 누른 채 밖으로 끌어 취소해도 소리는 이미 난 셈인데, 조작감에서는
  반응이 빠른 쪽이 낫다고 보고 그 대가를 받아들였다.
- 자기 소리를 따로 내는 버튼은 `UiClickSfxIgnore`로 뺀다(버튼에 붙이면 그것만, 패널 루트에 붙이면 전부).
- 상주 비용은 `Update`의 버튼 상태 조회 하나다 — 씬 탐색이 없다.

### 5.6 타격이 클립 중간에 있는 스팅어 — `startTime` (#538)

`PlaySfxExclusive`에 `startTime` 인자가 붙었다(기본 0이라 기존 호출부는 그대로다).

**앞쪽 무음을 잘라내는 기능이 아니다.** §4의 "소리가 게임과 안 붙는다" 항목과 같은 축인데 크기가 다르다 —
`Tower_Install`은 앞 75ms가 문제였지만, 결과창 승리 스팅어
(`SFX_Vefects_Stylized_Magic_Attack_Earth_Cast`, 2.252초)는 **디지털 무음이 0.15초, 그 뒤 0.63초까지가
거의 안 들리는 워밍업이고 실제 타격이 0.66초 지점**에 있다. 마법 시전음이라 차오르는 구간이 붙은 구조다.
그대로 재생하면 로고가 착지하고 0.66초 뒤에 소리가 난다.

50ms 단위 RMS 포락선(0 = 무음, 9 = 피크):

```
000111111111024422332111100000000000000000000
↑0.00      ↑0.50   ↑0.75      ↑1.25      ↑2.25
```

5ms로 좁히면 **0.629초에 골**(거의 무음)이 있고 0.663초부터 어택이 치솟는다. 그 골에서 시작하면
타격 트랜지언트가 하나도 잘리지 않는다 — `ResultPanelAnimator.stingerStartTime = 0.63`의 근거다.

⚠ **`time`은 `Play()` 전에 넣는다.** 재생을 걸어 두고 나중에 밀면 워밍업이 한 프레임 새어 나오는데,
그 한 프레임이 곧 이 기능의 실패다.

⚠ 이건 재생 시점 우회일 뿐 파일은 그대로다. 같은 클립을 다른 화면에서 처음부터 쓰고 싶으면 그쪽은
`startTime`을 넘기지 않으면 된다.

## 6. 공개 API

```csharp
public enum AudioChannel { Master, Bgm, Sfx }

AudioManager.Instance                                  // AfterSceneLoad 자체 부팅

float GetVolume(AudioChannel channel);
void  SetVolume(AudioChannel channel, float value01);  // 0~1 clamp. 음소거는 건드리지 않는다
bool  IsMuted(AudioChannel channel);
void  SetMuted(AudioChannel channel, bool muted);      // 볼륨 값은 보존

event Action OnAudioSettingsChanged;                   // 볼륨·음소거 변경 통지

float GetEffectiveVolume(AudioChannel channel);        // AudioSource.volume에 곱할 계수

void PlayBgm(AudioClip clip, float fadeSeconds = 1f);  // 같은 클립·null은 무시
void StopBgm(float fadeSeconds = 1f);

void PlaySfx(AudioClip clip, float volumeScale = 1f);           // 2D 원샷. 볼륨 0·음소거면 재생 생략
void PlaySfxExclusive(AudioClip clip, float volumeScale = 1f, float startTime = 0f);
                                                                // 2D 전용 소스. 직전 것을 끊고 처음부터
                                                                // startTime: 클립의 이 지점부터 재생(§5.6)
void StopSfxExclusive();                                        // 위 소리를 즉시 정지(씬 전환용, §5.1)
```

공용 효과음은 위를 직접 부르지 않고 `Sfx`를 거친다(§5.4):

```csharp
Sfx.ButtonClick();        Sfx.PanelOpen();          Sfx.TowerInstalled();
Sfx.Rejected();           Sfx.BuildingUpgraded();   Sfx.Undone();
Sfx.TutorialBubbleOpened(); Sfx.TutorialPopupOpened();
Sfx.Redone();             // 아직 부르는 곳이 없다(다시 실행 기능 미구현)
Sfx.ResidentIncreased();  // ← 이것만 PlaySfxExclusive로 나간다
```

같은 큐를 한 프레임에 두 번 요청하면 **첫 번째만 재생된다**(§5.4의 경고).

`PlaySfx`는 소스 1개 + `PlayOneShot`이다(풀 아님). 그래서 두 가지 제약이 따라온다:

- **볼륨이 호출 시점에 구워진다** — 재생 중 슬라이더를 움직여도 이미 울리는 소리에는 반영되지 않는다.
  1~2초짜리 짧은 소리 전제다.
- **동시재생 상한이 없다** — `PlayOneShot`은 겹쳐 쌓인다. 프레임마다 부를 만한 소리(타워 발사음 등)는
  이 API가 아니라 풀 기반 경로를 기다린다(§7).

`PlaySfxExclusive`는 그 두 제약을 뒤집은 경로다. `PlayOneShot`이 아니라 `clip` + `Play()`라 **지목해 멈출 수
있고**(연타에 겹치지 않는다) **볼륨을 매 프레임 다시 곱한다**(재생 중 슬라이더가 반영된다). 대가는
⚠ **소스가 하나뿐이라 이 경로의 소리들끼리도 서로를 끊는다**는 것이다 — 지금 소비처가 하나라 충분하고,
둘 이상이 동시에 울려야 하면 소리별 소스로 갈라야 한다.

> ⚠️ **이 경로는 씬을 넘어 계속 울린다.** 소스가 살아 있는 한 재생이 이어지므로, 정지 주체를 두지 않으면
> 9.5초짜리 팡파레가 타이틀 복귀 뒤에도 끝까지 들린다 — WL-180이 BGM에서 낸 사고와 형태가 같다.
> 그래서 `SoundCue.Awake`가 `StopSfxExclusive()`를 부른다(§5.1). 짧은 원샷(`PlaySfx`)은 대상이 아니다.

- 설정 패널(#346)은 `Get*`으로 슬라이더 초기값을 읽고, `Set*`으로 밀고, `OnAudioSettingsChanged`로
  코드 쪽 변경을 따라온다.
- **새 재생 경로를 만드는 쪽은 `GetEffectiveVolume`을 곱한다**(§2).

### 6.1 매니저를 거치지 않는 재생 경로

믹서가 없어 `GetEffectiveVolume`이 유일한 연결 고리이므로, **자기 `AudioSource`를 직접 미는 경로**가 생기면
여기 적는다. 지금 둘이다.

| 경로 | 볼륨 제어 | 성격 |
|---|---|---|
| `CombatSfxPool` | ✅ 매 프레임 `GetEffectiveVolume(Sfx)`를 곱한다 | 전투 위치 기반 스킬·향후 타워 효과음 |
| `ResidentVoice`(주민 대화 목소리) | ✅ 매 프레임 `GetEffectiveVolume(Sfx)`를 곱한다 | 주민 목소리 |

`PlaySfxExclusive`는 여기 넣지 않는다 — **매니저 안**의 소스이고 매니저가 직접 볼륨을 곱한다(§6).
`ResidentVoice`와 방식이 같아 보이지만 소스의 주인이 다르다.

> **`ResidentVoice`는 `PlaySfx`를 쓸 수 없어서 따로 났다.** 요구가 "화면 중심에 가까울수록 크게, 화면 밖은
> 무음"이라 **볼륨이 매 프레임 바뀌어야 하는데**, `PlaySfx`는 `PlayOneShot`이라 호출 시점의 볼륨을 굽는다.
> 그래서 자기 `AudioSource`를 들고 `volume`을 직접 갱신한다 — 부수 효과로 **설정 슬라이더가 이미 울리고
> 있는 소리에도 즉시 반영된다**(`PlaySfx`에는 없는 성질이다). 감쇠를 Unity의 3D 오디오에 맡기지 않은 근거는
> `Docs/ManagementArea/Resident.md` §11.16에 있다.

### 6.2 전투 위치 SFX 풀 (`CombatSfx`, #522)

소비자는 `CombatSfx.Play(clip, worldPosition, loop, volumeScale, priority)`만 호출한다. 내부 풀은 보이스
16개를 미리 만들고 최대 32개까지 확장하며, 재생 종료 시 삭제하지 않고 반환한다. 한 매니저의 `Update`가
활성 보이스의 카메라 감쇠·스테레오 팬·설정 볼륨·자연 종료를 처리한다.

- 줌: 오쏘 크기 80 이하 전체 볼륨, 80~160 감쇠, 160 이상 무음
- 위치: 화면 중앙이 가장 크고 경계에서 0, 화면 밖은 무음
- 상한 도달: 화면 밖 무음 → 낮은 우선순위 → 오래된 보이스 순으로 회수
- 핸들: 슬롯 인덱스와 세대 번호를 가진 값 형식. 반환된 슬롯을 예전 핸들이 잘못 끄지 못한다
- 씬 전환: 활성 보이스 전체 정지, 카메라 캐시 초기화
- **일시정지: 루프 보이스만 `Pause`/`UnPause`**(#540). `Time.timeScale = 0`은 `AudioSource`에 영향을
  주지 않고 풀의 `Update`도 계속 돌기 때문에, 명시적으로 걸지 않으면 **설정 패널·보상 선택창·결과창
  밑에서 빔과 전기장 소리가 계속 흐른다.** 단발음은 정지 중 스스로 소진되는 편이 자연스러워 뺐다 —
  정지 순간 울리던 발사음이 뚝 끊기면 그쪽이 더 어색하다.
  ⚠ 함정이 둘이다. ① `Pause()`가 `isPlaying`을 false로 만들어 **자연 종료 판정이 정지된 루프를
  회수해 버리므로**, 회수 검사보다 먼저 걸러야 한다. ② `CombatSfxHandle.IsPlaying`이 정지 중에도
  **true를 답해야 한다** — false를 주면 매 프레임 도는 소비처가 루프가 죽은 줄 알고 정지 화면에서
  계속 새 보이스를 잡는다(정지를 고치려다 다른 churn을 만드는 자리다)
- **루프는 들리지 않으면 새로 잡지 않는다**(`CombatSfx.IsAudible`, #540). 화면 밖 루프는 `LastGain`이
  계속 0이라 탈취 1순위인데, 뺏길 때마다 다시 잡으면 포화 상태에서 매 프레임 재획득이 반복되며
  **화면 안에서 들리던 소리를 60fps로 잘라 낸다.** 단발음은 곧 끝나므로 이 판정이 필요 없다
- 기본 감전: 시전음 1회 → 착탄 시 남은 시전음 정지 → 착탄음 1회, 둘 다 `High`
- 폭탄 특수효과: `BombEffect`가 클립·개별 볼륨을 authoring하고 `SkillBomb.Explode`가 실제 폭발 위치에서 `High` 단발 재생. 폭발 전 웨이브/런 종료로 취소되면 재생하지 않는다
- 전기장 특수효과: `FieldEffect`가 클립·개별 볼륨을 authoring하고 `SkillField`가 생성 위치에서 `Normal` 루프 재생. 반환된 `CombatSfxHandle`은 장판이 `OnDestroy`될 때 정지하므로 정상 만료·웨이브 종료·승패 확정에 함께 닫힌다

폭탄·전기장 클립을 `SfxBank`에 넣지 않는 이유는 그 뱅크가 주인이 없는 화면 전역 2D 공용음을 위한 것이고,
두 소리는 각 특수효과가 수치·VFX와 함께 소유하는 위치 기반 전투음이기 때문이다. 호출부는 풀이나
`AudioSource`를 직접 소유하지 않고 `CombatSfx` 계약만 사용한다.

`CombatSfx`가 `AudioManager`의 소스를 빌리지 않는 이유는 위치마다 독립적으로 매 프레임 볼륨이 바뀌기
때문이다. 대신 `GetEffectiveVolume(Sfx)`를 마지막 계수로 곱해 같은 설정 계약 아래 남는다.

### 6.3 타워 전투음 — 세 축 (#540)

클립은 **각 `TowerAsset`이 소유한다**(§5.4의 "뱅크는 주인 없는 소리 전용" 규약).
셋 다 `CombatSfx.Play(..., priority: Low)`로 나가므로 상한에서 스킬음·경고음보다 먼저 회수된다.

| 축 | SO 필드 | 발화 지점 | 성격 |
|---|---|---|---|
| 발사음 | `Attack.FireSfx` + `FireSfxVolume` | `Tower.RaiseFired` | 사건 · 원샷 |
| 착탄음 | `ImpactSfx` + `ImpactSfxVolume` | `AttackAction`의 `Projectile.Impacted` 구독 | 사건 · 원샷 |
| 빔 루프 | `Beam.LoopSfx` / `BeamStage.LoopSfx` + 각 볼륨 | `BeamAction.UpdateLoopSfx` | **상태** · 루프 |

**발사음을 구독 컴포넌트로 빼지 않았다.** 발사음은 공격 타워 전부가 갖는 보편 소리라, 프리팹마다
컴포넌트를 붙이는 방식이면 새 타워를 만든 사람이 잊었을 때 그 타워만 조용히 무음이 된다 —
`UiClickSfx`를 전역 훅으로 만든 것과 같은 축이다(§5.4). `TowerReloadVisual`이 컴포넌트인 것은
탄약 모형이 있는 타워에만 해당하는 **선택적** 연출이기 때문이고, 발사음은 그렇지 않다.
`RaiseFired`는 이미 발사 통지의 단일 창구이며 호출부도 `AttackAction` 한 곳이다.

**착탄음은 착탄 파티클(`ImpactVfx`)과 같은 구독을 탄다.** 트리거를 갈라 두면 진입점이 늘 때 한쪽만
조용히 빠진다(WL-208 — 건물 성공음이 같은 사고를 냈다). 부수 효과로 `isFresh` 필터를 공유하므로
**부메랑 재접촉·스플래시에서 소리가 배수로 늘지 않는다** — 착탄당 1회다.
⚠ 저작 여부는 파티클과 **따로** 본다. `ImpactVfxFields.IsAuthored`(프리팹 유무) 하나로 묶으면
**소리만 넣고 파티클은 비운 타워가 조용히 무음**이 된다(소다 계열이 그 경우다).

⚠ **`PelletCount > 1` 타워의 착탄음은 펠릿 수만큼 난다.** 구독이 `SpawnPellet` 안에 있어 펠릿마다
람다가 따로 걸리고, `isFresh`는 **한 탄의 재접촉**만 거르지 서로 다른 3발을 묶지 않는다. 위 "착탄당
1회"는 *한 탄* 기준이라는 뜻이다. 지금은 착탄음 소비처가 소다 2종(펠릿 1)뿐이라 드러나지 않지만,
산탄(`triple_shoot_tower`, 펠릿 3)에 착탄음을 붙이면 **발사 1회에 소리가 3번 겹친다.**
필요해지면 `SpawnPellet` 밖에서 한 벌로 묶어야 하고 그때 볼륨 규약도 다시 잡는다 —
**지금 코드를 바꾸지 말 것**: 관통·부메랑이 접촉마다 착탄 연출을 내는 현재 거동이 옳다.

**빔 루프는 사건이 아니라 상태다.** 빔은 쏘는 순간이 따로 없어 원샷을 걸 자리가 없고, 잠금이 살아
있는 동안 흐르다 끊긴다. 규칙 둘:
- **타워당 하나만 흐른다.** 멀티 인페르노는 대상을 5기까지 잠그지만 소리는 "이 타워가 지금 지지고
  있는가"를 알리는 것이라 대상마다 겹치면 안 된다.
- **단계 판정은 대상들 중 최대 진행도**다. 평균을 쓰면 새 대상이 잠길 때마다 소리가 내려가
  "약해졌다"로 잘못 읽힌다.

⚠ **단일 인페르노의 단계 볼륨이 0.60 → 0.45 → 0.35로 내려가는 것은 의도다.** 버그로 읽고 올리지 말 것 —
세 클립(`Fire_Small_L` / `Medium_L` / `Big_L`)의 원본 RMS가 **-31.9 / -25.8 / -18.3 dBFS**로 이미 계단이라,
같은 배율을 주면 격차가 13.6dB로 벌어져 3단계만 전장을 덮는다. 배율로 눌러 **실효 레벨을
-36 → -33 → -27dB**(3.6dB · 5.3dB 계단)로 잡은 값이다. "세지고 있다"는 읽히면서 과하지 않은 지점이고,
`BeamStage.LoopSfxVolume`의 툴팁("단계가 오를수록 커지게 하려면 여기서 조정한다")은 **원본 레벨이
평평한 세트를 쓸 때** 이야기다.

⚠ **정지는 `BeamAction.HideAllBeams` 안에 있다.** 빔을 끄는 경로가 `Dispose`(비활성화)와
`OnWaveEnd`(낮 전환) 둘인데 각 경로에 손으로 달면 세 번째가 생겼을 때 그쪽만 빠진다 —
"빔이 낮에도 켜진 채 남는다"는 기존 사고(#300 실측)의 소리 버전으로, 증상은 **낮 내내 불타는
소리가 흐르는 것**이다. 표시와 소리는 같은 상태의 두 얼굴이라 한 함수가 함께 끈다.

**저작 시 지켜야 할 수치 규약**

- **클립 길이 ≤ 공격 간격.** 넘으면 자기 소리끼리 겹쳐 그 배수만큼 보이스를 문다(들리지 않아도
  슬롯은 점유된다). 간격은 [CombatBalance.md](CombatBalance.md) §1.3 표에 있다.
- **어택(최대 음량 도달) ≤ 0.03초.** 차오르는 소리는 발사 순간과 붙지 않는다(§5.6).
- **레벨은 클립 RMS 기준으로 맞춘다.** 임포트 설정에 게인이 없으므로(§4.5) 편차는 `*SfxVolume`에서만
  잡는다. 현재 기준점은 아처(RMS -20.9 dBFS @ 0.6)이고, 다른 타워는 RMS 차이를 상쇄해 넣었다.
  ⚠ **개틀링만 일부러 기준보다 낮다** — 초당 2.9발이라 레벨을 맞추면 그 타워 하나가 전장을 덮는다.
  ⚠ **빔 루프도 일부러 낮다** — 지속음이 단발음만큼 크면 안 된다(실효 -36 ~ -27dB).
- **발사음에 폭발감을 넣지 않는다.** 착탄에서 터지는 타워(캐논·소이캐논·미사일)는 폭발이 착탄음
  몫이다. 발사음에 넣으면 착탄음을 붙이는 순간 한 발에 두 번 터진다.
- ⚠ **자폭병 폭발음과 음색이 겹치지 않게 한다.** 그 소리는 본진 HP 10%가 날아가는 **위험 신호**인데
  (2D로 재생돼 화면 밖에서도 들리게 설계돼 있다), 타워가 0.75초마다 같은 음색을 내면 플레이어가
  그것을 일상음으로 학습해 정작 반응하지 않게 된다. 실제로 캐논이 한 번 그 상태였다.

## 7. 미확정 / TODO

- [x] **전투 위치 SFX 풀** — #522에서 화면 좌표 기반 `CombatSfxPool`로 구현(§6.2). Unity 3D 감쇠는
      오쏘 카메라와 맞지 않아 사용하지 않는다. `ResidentVoice`는 주민 상태를 따라가는 독립 소스라 흡수하지 않는다.
- [ ] **화면 감쇠 계산 공용화** — `CombatSfxAudibility`와 `ResidentVoiceAudibility`의 뷰포트 위치·팬 계산을
      공용 타입으로 분리한다. 주민(40~80)과 전투(80~160)의 줌 밴드는 소비처별로 유지한다. 별도 이슈/PR 범위.
- [x] **타워 전투음 연결** — #540에서 발사음·착탄음·빔 루프 세 축으로 구현(§6.3).
      ⚠ **여기 적혀 있던 방향과 다르게 갔다.** "투사체 프리팹의 연출 컴포넌트"가 아니라 `AttackAction`이
      `Projectile.Impacted`를 구독하는 형태다 — 착탄 파티클(`ImpactVfx`, #521)이 이미 그 통지를 타고 있어서,
      트리거를 갈라 두면 진입점이 늘 때 한쪽만 조용히 빠지기 때문이다(WL-208). 대신 "피해 코어에
      `AudioSource`를 넣지 않는다"와 "공용 API를 쓴다"는 두 전제는 그대로 지켰다.
      또한 **발사음이라는 축이 이 항목에 없었다** — 착탄만으로는 "내 타워가 일하고 있다"가 안 들린다.
- [x] **BGM·전환음 클립 에셋** — `Assets/Imported/@NorthLand/Sound`에 낮·밤 BGM 2개 + 전환 스팅어 2개(§4.5)
- [x] **씬 배치** — `GameScene`에 `SoundCue/InGameCue`, `TitleScene`에 `SoundCue/TitleCue`
- [ ] **타이틀 BGM 클립** — 트랙 에셋이 없어 `TitleCue.titleClip`이 비어 있다(정지만 한다, §5.1).
      클립이 생기면 그 필드에 꽂으면 끝이고 코드 변경은 없다
- [ ] **Vorbis quality 조정** — 현재 100%. BGM 90초 스테레오라 빌드 용량 관점에서 낮출 여지가 있으나
      청감 tradeoff라 미결(§4.5)
- [ ] **밤 세이브와 초기 트랙 순서**(WL-182) — 밤 페이즈 복원을 여는 PR에서 §5.3의 순서 문제를 함께 닫는다
- [ ] **설정 패널 슬라이더·토글 UI** → #346. 패널 초기값은 `OnAudioSettingsChanged`가 아니라 `GetVolume`
      **pull**로 읽어야 한다 — 매니저의 초기 발행 시점(`Awake`)엔 구독자가 없다
- [x] **UI 클릭 공용 사운드** — `UiClickSfx` 전역 훅 + `SfxBank`(§5.4). 풀을 기다리지 않았다 — 클릭·패널
      오픈·설치·거절은 전부 드물게 한 번 울리는 2D 소리라 기존 원샷 경로로 충분하다
- [ ] **UI 호버 사운드** — 미착수. 호버는 클릭과 빈도가 달라(커서를 스치기만 해도 난다) 같은 경로로
      그대로 옮기면 안 된다 — 디바운스·쿨다운을 함께 정할 것
- [ ] **거절음 분화** — 지금 `Sfx.Rejected` 하나를 배치 반려·합성 실패·주민 증가 실패가 공유한다.
      상황별로 다른 소리가 필요해지면 `SfxBank`의 항목과 `Sfx`의 메서드를 함께 가른다
      (`PlacementRequest.OnRejected`도 사유 인자를 받는 형태로 바꾼다)
      - 첫 갈래로 `Sfx.Blocked`(`SFX_Error`)가 떨어져 나왔다(#550) — 기준은 **커서 그림**이다
        (`Blocked` 커서면 `Blocked`, `Placing` 커서면 `Rejected`). 다만 **배치 모드의 표면 밖 클릭은
        아직 `Rejected` 쪽에 남아 있다** — 두 클립이 청감상 거의 같아 옮기지 않았다. 클립이 갈리는
        순간 `MouseManager.UpdatePlacement`의 표면 밖 분기를 `Sfx.Blocked()`로 옮길 것
- [x] **`SFX_Castle_ResidentIncrease` 길이** — 자르지 않기로 했다. **패널을 닫아도 끝까지 울리는 것이
      의도**다(주민 증가를 축하하는 팡파레). 겹침은 `PlaySfxExclusive`가 막는다
- [ ] **팬파레가 BGM에 묻히는 문제** — 파일 정규화(+3dB)와 뱅크 볼륨 1.0(+3.1dB)으로 6dB 올렸다.
      그래도 부족하면 남은 선택지는 둘이다: ① 리미팅으로 RMS를 올린다(음색이 변한다),
      ② **BGM 더킹** — 이 소리가 울리는 동안 BGM 볼륨에 감쇠 계수를 곱한다. 매니저가 BGM 소스를
      직접 소유하므로 AudioMixer 없이도 가능하다(§2가 말한 "더킹이 필요해지면 재검토"의 첫 사례)
- [ ] **`SFX_Castle_ResidentIncrease`의 GUID 충돌 정리**(§4.5.1) — 팩 원본의 GUID를 복사본이 가져갔다
- [ ] **더킹·스냅샷** — 필요해지면 AudioMixer 도입을 재검토(§2)
- [x] **`settings.json` 이관** — 오디오 볼륨과 음소거를 슬롯 무관 공통 설정 파일로 통합했다

## 8. 참고

- 시스템 맵: `Docs/Review/SystemMap.md` §1 Audio 행, §2 Audio 공개 API
- 씬 편집 절차: `Docs/Core/SceneWorkflow.md` §4
- 일시정지·배속: `Assets/Scripts/UI/GameSpeedController.cs` (`GamePauseReason.Settings`)
