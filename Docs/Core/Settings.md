# 설정 시스템

게임 설정 패널의 현재 구현, 저장 위치, UI 연결 규칙과 검증 절차를 정리한다.
설정 UI나 저장 키를 변경할 때 이 문서도 함께 갱신한다.

- 기준 프리팹: `Assets/Prefabs/UI/SettingCanvas.prefab`
- 설정 패널 제어: `Assets/Scripts/SettingUI/SettingUI.cs`
- 공통 설정 저장: `Assets/Scripts/SaveData/Settings/`
- 그래픽 설정: `Assets/Scripts/SettingUI/Graphics/`
- 사운드 UI: `Assets/Scripts/SettingUI/Sound/SoundSettingsUI.cs`
- 사운드 소유자: `Assets/Scripts/GameManager/AudioManager.cs`

## 1. 제공 설정

| 분류 | 항목 | 구현 | 저장 위치 |
|---|---|---|---|
| 일반 | 언어 | `LocalizationManager` | `settings.json` |
| 일반 | 키보드 이동 속도 | `CameraMoveSettingsUI` | `settings.json` |
| 일반 | 마우스 이동 속도 | `CameraMoveSettingsUI` | `settings.json` |
| 그래픽 | 화면 모드 | `DisplaySettings` | `settings.json` |
| 그래픽 | 해상도 | `DisplaySettings` | `settings.json` |
| 사운드 | 마스터 볼륨 | `SoundSettingsUI` → `AudioManager` | `settings.json` |
| 사운드 | BGM 볼륨 | `SoundSettingsUI` → `AudioManager` | `settings.json` |
| 사운드 | 효과음 볼륨 | `SoundSettingsUI` → `AudioManager` | `settings.json` |

화면 밝기 설정은 제공하지 않는다. `BrightnessSettings.cs`가 남아 있더라도 현재 UI/프리팹 계약에는 포함하지 않는다.

## 2. 패널 동작

`SettingUI`가 설정창과 `GeneralPanel`, `GraphicsPanel`, `SoundPanel`을 전환한다.

- 설정창을 열면 일반 패널을 기본으로 표시한다.
- 설정창이 열려 있는 동안 `GamePauseReason.Settings`로 게임을 일시정지한다.
- 닫기 또는 오브젝트 파괴 시 설정 일시정지 사유를 해제한다.
- Escape 키와 설정 버튼 모두 같은 열기/닫기 경로를 사용한다.
- 결과 화면 등 `GameResult.Playing`이 아닌 상태에서는 새로 열지 않는다.

## 3. 그래픽 설정

### 3.1 화면 모드

화면 모드 Dropdown의 **옵션 순서는 코드 계약**이다. 표시 문구만 번역하고 순서를 바꾸지 않는다.

| Dropdown 인덱스 | Unity 모드 | 한국어 표시 |
|---:|---|---|
| 0 | `ExclusiveFullScreen` | 전체 화면 |
| 1 | `FullScreenWindow` | 테두리 없는 창 |
| 2 | `Windowed` | 창 모드 |

- 기본값은 인덱스 `1`, 테두리 없는 창이다.
- 테두리 없는 창은 `Screen.currentResolution`의 모니터 기본 해상도로 적용한다.
- 선택 인덱스는 `GameSettingsData.screenMode`에 반영한다.

### 3.2 해상도

해상도 Dropdown도 다음 고정 순서를 유지한다.

| Dropdown 인덱스 | 해상도 |
|---:|---|
| 0 | 1920 × 1080 |
| 1 | 1600 × 900 |
| 2 | 1280 × 720 |

해상도를 선택하면 즉시 시험 적용하고 15초 확인 패널을 표시한다.

- **유지**: 선택 인덱스를 `GameSettingsData.resolutionIndex`에 반영한다.
- **되돌리기**: 변경 전 해상도와 화면 모드로 복원한다.
- **시간 초과**: 되돌리기와 동일하게 처리한다.
- 카운트다운은 UniTask와 unscaled time을 사용하므로 설정창으로 게임이 정지되어도 진행된다.

너비와 높이는 저장하지 않는다. 고정 해상도 목록에서 `resolutionIndex`로 다시 결정한다.
화면 모드와 확정된 해상도는 설정 패널을 닫을 때 다른 공통 설정과 함께 파일에 저장된다.

저장된 화면 모드와 해상도는 `GameSettingsService`가 설정 파일을 불러온 직후, 첫 씬이
시작되기 전에 `Screen`에 적용한다. `DisplaySettings`는 설정 UI 표시를 저장값과 동기화하고
사용자의 화면 모드·해상도 변경을 처리한다.

### 3.3 그래픽 UI 연결

`DisplaySettings`에는 다음 참조가 모두 필요하다.

- 화면 모드 TMP Dropdown
- 해상도 TMP Dropdown
- 해상도 확인 패널과 안내 TMP Text

화면 모드 Dropdown은 `OnScreenModeChanged(int)`를 호출한다. 해상도 Dropdown 리스너는
`DisplaySettings`가 런타임에 등록한다. 유지/되돌리기 버튼은 프리팹의 persistent call로 각각
`ConfirmResolutionChange()`와 `RevertResolutionChange()`에 연결한다.

## 4. 사운드 설정

`AudioManager`가 Master/BGM/SFX 세 채널의 값을 소유한다. `SoundSettingsUI`는 값을 별도로
소유하지 않고 슬라이더 입력을 매니저에 전달하며, `OnAudioSettingsChanged`를 받아 표시를 동기화한다.

| 채널 | 기본값 | 실효 볼륨 |
|---|---:|---|
| Master | 1.0 | Master |
| BGM | 0.5 | Master × BGM |
| SFX | 0.8 | Master × SFX |

세 슬라이더 범위는 `0~1`이며 소수 값을 사용한다. `SoundSettingsUI`에는 Master/BGM/SFX Slider 세 개가
모두 연결되어야 한다.

볼륨과 음소거 상태는 `GameSettingsData`의 `masterVolume`, `bgmVolume`, `sfxVolume`,
`masterMuted`, `bgmMuted`, `sfxMuted`에 반영한다. 설정 패널을 닫을 때
`GameSettingsService.TrySaveCurrentSettings()`가 `settings.json`에 저장한다. 현재 설정 패널에는 음소거 UI가 없다.

새 효과음 재생 경로는 `AudioManager`를 통하거나 `GetEffectiveVolume(AudioChannel.Sfx)`를 적용해야
사용자의 효과음 설정을 따른다. 자세한 계약은 `Docs/Core/AudioManager.md`를 따른다.

## 5. 저장 구조

모든 슬롯 공통 설정은 `settings.json` 하나에 저장한다.

`GameSettingsService`가 `Application.persistentDataPath/settings.json`을 관리한다.

- `localeCode`
- `lastSelectedSlotIndex`
- `keyboardMoveSpeedMultiplier`
- `mouseMoveSpeedMultiplier`
- `screenMode`
- `resolutionIndex`
- `masterVolume`
- `bgmVolume`
- `sfxVolume`
- `masterMuted`
- `bgmMuted`
- `sfxMuted`

카메라 이동 속도 배율 범위는 `0.5~2.0`이다. 자세한 파일 형식과 마이그레이션 규칙은
`Docs/Core/SaveSystem.md`를 따른다.

## 6. 로컬라이제이션

설정 문구는 `NorthLand_default` String Table을 사용하며 한국어(`ko-KR`), 영어(`en-US`),
일본어(`ja-JP`) 값을 모두 제공한다.

| 키 | 용도 |
|---|---|
| `settings.language.prompt` | 언어 선택 안내 |
| `settings.category.general` | 일반 탭 |
| `settings.category.graphics` | 그래픽 탭 |
| `settings.category.sound` | 사운드 탭 |
| `settings.controls.keyboard_speed` | 키보드 속도 |
| `settings.controls.mouse_speed` | 마우스 속도 |
| `settings.graphics.screen_mode` | 화면 모드 |
| `settings.graphics.resolution` | 해상도 |
| `settings.audio.master` | 마스터 볼륨 |
| `settings.audio.bgm` | 배경음 |
| `settings.audio.sfx` | 효과음 |
| `settings.graphics.screen_mode.fullscreen` | 전체 화면 옵션 |
| `settings.graphics.screen_mode.borderless` | 테두리 없는 창 옵션 |
| `settings.graphics.screen_mode.windowed` | 창 모드 옵션 |

고정 라벨은 `LocalizeStringEvent`를 사용한다. 화면 모드 옵션은
`ScreenModeDropdownLocalization`이 세 `LocalizedString`의 `StringChanged`를 구독해 Dropdown 옵션
0~2의 텍스트를 갱신한다. 해상도 숫자는 언어별로 달라지지 않으므로 로컬라이즈하지 않는다.

새 설정 키는 `settings.<분류>.<항목>` 형식을 사용한다. 화면 모드처럼 한 설정 아래 선택지가 있으면
`settings.<분류>.<항목>.<선택지>`로 확장한다.

## 7. 프리팹 수정 규칙

- 기준 프리팹은 `SettingCanvas.prefab`이다.
- `SettingCanvas Variant.prefab`은 기준 프리팹을 상속한다.
- Variant에서 `Apply All`을 하기 전에 의도하지 않은 삭제·추가 Override가 없는지 확인한다.
- Dropdown 옵션 순서 변경은 표시 변경이 아니라 기능 계약 변경이므로 `DisplaySettings`와 함께 검토한다.
- Localize String 참조는 세 로케일에 동일한 Key ID가 존재하는지 확인한다.
- 스크립트 참조, Slider, Button, TMP Dropdown을 교체했다면 프리팹 직렬화 참조도 다시 확인한다.

## 8. 검증 체크리스트

- [ ] 설정 버튼과 Escape로 설정창을 열고 닫을 수 있다.
- [ ] 설정창을 열면 게임이 정지하고 닫으면 정상적으로 재개한다.
- [ ] 일반/그래픽/사운드 탭이 올바른 패널을 표시한다.
- [ ] 언어 변경 후 모든 설정 라벨과 화면 모드 옵션이 즉시 갱신된다.
- [ ] 한국어·영어·일본어에서 텍스트가 잘리지 않는다.
- [ ] 화면 모드 세 옵션의 표시와 실제 모드가 일치한다.
- [ ] 해상도 유지 버튼을 누르면 재실행 후에도 선택값이 유지된다.
- [ ] 해상도 되돌리기와 15초 시간 초과가 이전 화면 상태를 복원한다.
- [ ] Master 슬라이더가 BGM과 SFX 양쪽에 적용된다.
- [ ] BGM과 SFX 슬라이더가 각 채널만 변경한다.
- [ ] 볼륨 0과 1에서 음소거 및 최대 볼륨이 정상적으로 들린다.
- [ ] 게임 재실행 후 언어, 이동 속도, 그래픽, 사운드 설정이 복원된다.

## 9. 알려진 주의점

- 테두리 없는 창 모드는 모니터 기본 해상도를 사용하는 것이 원칙이다. 해상도 Dropdown의 사용 정책을
  변경하면 화면 모드 전환 동작과 함께 검증한다.
- 고정 해상도가 대상 모니터에서 지원되지 않을 수 있으므로 독점 전체 화면은 반드시 실제 빌드로 시험한다.
- 해상도 옵션 개수나 순서를 바꿀 때 기존 `resolutionIndex` 저장값의 범위 검증도 함께 확인한다.
- 설정 초기화 기능을 추가할 때는 공통 `settings.json`을 처리한다.
