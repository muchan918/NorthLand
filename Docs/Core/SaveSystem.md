# 세이브 시스템

> 이 문서는 플레이어 슬롯, 공통 설정, Run 저장의 파일 구조와 통합 계약을 설명한다.
> 사용자에게 보이는 정책은 `Docs/GDD.md` §5.9, 시스템 요약과 공개 API는
> `Docs/Review/SystemMap.md`를 함께 따른다.

## 1. 개요

- 플레이어 프로필 슬롯은 3개다. 내부 슬롯 인덱스는 `0`~`2`다.
- 각 슬롯은 식별 정보(`player.json`)와 Run 진행 상태(`run-save.json`)를 독립적으로 가진다.
- 언어, 마지막 선택 슬롯, 카메라 이동 속도, 그래픽 및 사운드 설정은 모든 슬롯이 공유하는 `settings.json`에 저장한다.
- 슬롯 표시 이름은 저장하지 않는다. UI가 현재 로케일의 `save.slot.name` Smart String과 슬롯 번호로 조립한다.
- 업적·해금·누적 통계는 현재 구현 범위 밖이다. 추가할 때는 슬롯 폴더의 별도 `meta.json`으로 관리한다.

## 2. 저장 경로

모든 경로의 루트는 `Application.persistentDataPath`다.

```text
Application.persistentDataPath/
├─ settings.json
└─ SaveSlots/
   ├─ slot-0/
   │  ├─ player.json
   │  └─ run-save.json
   ├─ slot-1/
   │  ├─ player.json
   │  └─ run-save.json
   └─ slot-2/
      ├─ player.json
      └─ run-save.json
```

슬롯을 삭제하면 해당 `slot-{index}` 폴더와 내부 파일을 함께 삭제한다.

## 3. 파일별 책임

### 3.1 `settings.json`

`GameSettingsService`와 `GameSettingsStore`가 소유한다.

- `localeCode`: 현재 언어 코드
- `lastSelectedSlotIndex`: 마지막 선택 슬롯. `-1`은 선택 없음
- `keyboardMoveSpeedMultiplier`: 키보드 카메라 이동 속도 배율
- `mouseMoveSpeedMultiplier`: 마우스 카메라 이동 속도 배율
- `screenMode`: 화면 모드 Dropdown 인덱스
- `resolutionIndex`: 해상도 Dropdown 인덱스
- `masterVolume`, `bgmVolume`, `sfxVolume`: 채널별 볼륨
- `masterMuted`, `bgmMuted`, `sfxMuted`: 채널별 음소거 상태
- 슬롯과 무관한 게임 공통 설정만 저장한다.

파일이 없으면 기본 설정을 생성해 저장한다. 기존 파일이 손상됐거나 지원하지 않는 상위 버전이면
파일을 덮어쓰지 않고 현재 실행에서만 기본값을 사용한다. 이 상태에서도 언어와 슬롯 선택은 메모리에
반영하지만 다음 실행에는 유지되지 않는다.

### 3.2 `player.json`

`PlayerSaveService`, `PlayerSlotManager`, `PlayerDataStore`가 소유한다.

- `playerId`: 슬롯을 식별하는 GUID 문자열
- `createdAt`: 생성 시각(Unix seconds)
- `lastPlayedAt`: 마지막 Run 저장 시각(Unix seconds)
- `tutorialCompleted`: 해당 슬롯에서 튜토리얼을 한 번 이상 완료하거나 스킵했는지 여부

표시 이름은 저장하지 않는다. 따라서 언어를 변경하거나 기존 슬롯을 불러와도 슬롯 이름은 현재 로케일로
표시된다. 파일은 존재하지만 파싱 또는 검증에 실패하면 UI에서 손상 슬롯으로 표시하며, 덮어쓰지 않고
사용자가 삭제한 뒤 다시 만들 수 있게 한다.

### 3.3 `run-save.json`

`RunSaveManager`가 각 시스템의 공개 API를 호출해 중앙에서 수집하고 복원한다.

- Run 마스터 시드와 시스템별 사용 시드
- 일차·웨이브·페이즈
- 자원, 생산 건물 레벨·주민 배치, 업그레이드 건물 레벨, 증축 주민 수
- 설치 타워의 `TowerID`, 맵 영역, 전투맵 셀 좌표 또는 StartMap 타일 ID
- 보상 특수효과 레벨
- 본진 현재 HP

런타임 인스턴스와 인스펙터 배열 인덱스는 저장하지 않는다. 건물은 `BuildingID`, 타워는 `TowerID`처럼
안정된 ID를 기록하고 밸런스 수치는 복원할 때 DataTable과 SO에서 다시 읽는다. 전투맵과 버프 타일은
전체 타일을 저장하지 않고 시드로 재생성한다.

## 4. 초기화와 슬롯 선택

1. `GameSettingsService`가 `BeforeSceneLoad`에서 생성되어 `settings.json`을 읽는다.
2. `PlayerSaveService`가 생성되고 `Start`에서 `lastSelectedSlotIndex`를 복원한다.
3. 선택 슬롯의 `player.json`이 정상일 때만 `HasSelectedSlot`이 참이 된다.
4. 새 게임과 이어하기는 선택된 슬롯이 있어야 시작할 수 있다.
5. `RunSaveManager`는 `PlayerSaveService.CurrentSlotPath`를 받아 해당 슬롯의 `run-save.json`만 사용한다.

슬롯 생성·선택·삭제가 성공하면 `PlayerSaveService.SelectedSlotChanged`가 발행된다. 마지막 선택 슬롯은
`GameSettingsService.TrySetLastSelectedSlotIndex`를 통해 공통 설정에 기록한다.

### 4.1 이어하기 씬 핸드오프

1. 타이틀의 `MainMenuUI`는 `RunSaveLoader.LoadAsync`로 선택 슬롯의 파일 읽기·버전 판별·마이그레이션·역직렬화를 완료한다.
2. 로드가 성공한 `RunData`는 `GameSceneManager.TryLoadContinue(RunData, out string)`에 전달한다. 이 호출이 일회성 데이터를 등록하고 게임 씬을 여는 일을 한 번에 수행한다.
3. `GameSceneManager`는 `DontDestroyOnLoad` 수명 동안 이 데이터를 일시 보관한다.
4. 게임 씬의 `RunSaveManager`가 `TryConsumeContinueData`로 데이터를 한 번만 소비해 런타임 상태를 복원한다.
5. 소비 성공 시 `GameSceneManager`는 이어하기 플래그와 `RunData` 참조를 즉시 제거한다.

`GameSceneManager`의 보관은 씬 경계를 넘기 위한 **일회성 전달 책임**이다. 파일 IO, JSON 구조, 버전 호환성 및
마이그레이션은 `RunSaveLoader`/`SaveSerializer`가 소유하고, 실제 게임 상태 적용은 `RunSaveManager`가 소유한다.
새 게임 또는 직접 입력 시드로 진입하면 남아 있는 이어하기 데이터는 폐기한다.

## 5. Run 저장과 복원

### 저장

- 자동 저장 시점은 1일차를 포함한 모든 `DayNightManager.OnDayStart`다.
- `TutorialMode.IsActive`인 동안에는 낮 시작 자동 저장을 건너뛴다. 튜토리얼 Run은 이어하기 세이브로 기록하지 않는다.
- `DayNightManager.OnDayStart`를 받은 `RunSaveManager`가 내부 `SaveNowAsync(CancellationToken)`를 실행해 현재 상태를 수집하고 선택 슬롯의 `run-save.json`을 교체한다. 이 메서드는 자동 저장 구현 전용이므로 공개 API가 아니다.
- 복원 중에는 자동 저장을 억제해 방금 읽은 세이브를 초기 상태로 덮어쓰지 않는다.
- Run 저장 성공 후 `player.json`의 `lastPlayedAt`을 갱신한다.

비동기 저장·로드 작업은 `SaveResult` 또는 `SaveResult<T>`로 성공 여부와 오류를 반환한다. 값이 있는 로드는
`Value`를 함께 제공하며, 취소는 `CancellationToken`과 `OperationCanceledException`으로 성공·실패 결과와 구분한다.

튜토리얼 다시 보기는 사용자 확인 후 `RunSaveManager.DeleteCurrentRunAsync(CancellationToken)`으로 현재 슬롯의
`run-save.json`을 먼저 삭제한다. 선택 슬롯이 없으면 삭제 대상이 없는 것으로 보고 성공을 반환한다. 선택 슬롯은
있지만 `SaveFileStore`가 초기화되지 않았다면 현재 슬롯 경로로 지연 생성한 뒤 삭제한다. 실제 파일 삭제에 실패한
경우에만 실패를 반환하며, 호출부는 튜토리얼로 전환하지 않는다.

### 복원

복원 순서는 시스템 의존성을 따라 중앙에서 관리한다.

1. 시드 선주입 및 맵 재생성
2. 진행·경영 상태 복원
3. 전투맵 공개 범위 복원
4. 타워 복원
5. 본진 HP와 보상 특수효과 복원
6. 페이즈 복원

타워는 프리팹을 직접 `Instantiate`하지 않고 `TowerPlacer.TryRestoreTower`를 사용한다. 그래야 타일 점유,
타일 버프, `Tower.Build`가 일반 배치와 같은 경로로 적용된다. 복원 불가능한 Run 세이브는 실패 반복을
막기 위해 삭제하고 타이틀로 돌아간다. 게임오버 또는 최종 승리로 Run이 끝난 경우에도 Run 세이브를 삭제한다.

⚠ **경영 복원 API(`ManagementController.TryRestoreProductionLine` / `TryRestoreUpgradeBuilding` /
`TryRestoreBonusVillagers`)는 세이브 전용이 아니다** — 되돌리기(#444)가 "이전 값으로 되맞추는 수단"으로
같은 API를 쓴다(`Docs/ManagementArea/BuildingUpgrade.md` §10). 되돌리기 전용 감소 경로를 새로 만들지 않은
의도적 선택이므로, **이 API의 시그니처·검증 규칙을 바꾸면 되돌리기가 함께 깨진다.** 특히 "비용을 차감하지
않고 페이즈 게이트를 타지 않는다"는 성질이 양쪽 모두의 전제다.

## 6. 파일 IO

`SaveFileStore`가 JSON 문자열의 파일 IO만 담당한다.

1. `{SavePath}.tmp`에 UTF-8(BOM 없음)으로 기록한다.
2. 기록 성공 후 기존 파일을 교체한다.
3. 실패하면 기존 파일을 유지하고 임시 파일 정리를 시도한다.

JSON 변환과 게임 상태 수집은 `SaveFileStore`의 책임이 아니다.

## 7. 버전과 마이그레이션

새 저장 파일은 공통 봉투를 사용한다.

```json
{
  "version": 1,
  "data": {}
}
```

현재 포맷 버전은 다음과 같다.

| 파일 | 버전 상수 | 현재 버전 |
| --- | --- | ---: |
| `player.json` | `PlayerSaveFormat.CurrentVersion` | 2 |
| `settings.json` | `GameSettingsFormat.CurrentVersion` | 2 |
| `run-save.json` | `SaveFormat.CurrentVersion` | 3 |

- `VersionedSaveSerializer<TData>`가 봉투의 `version`을 먼저 읽고 `data`를 나중에 변환한다.
- `SaveMigrationChain`은 v1→v2, v2→v3처럼 인접 버전 변환을 순서대로 적용한다.
- `player.json` v1→v2 마이그레이션은 `tutorialCompleted = false`를 추가한다.
- `settings.json` v1→v2 마이그레이션은 화면 모드·해상도와 Master/BGM/SFX 볼륨·음소거 기본값을 추가한다.
- 현재 빌드보다 높은 버전은 다운그레이드 손상을 막기 위해 로드를 거부한다.
- 기존 평면 `player.json`과 `settings.json`은 정상 로드·검증 후 봉투 형식으로 다시 저장한다.
- 스키마를 바꾸면 해당 파일의 `CurrentVersion`을 올리고 직전 버전에서 새 버전으로 가는 마이그레이션을 추가한다.

## 8. 구버전 Run 위치 이전

슬롯 도입 전의 `Application.persistentDataPath/run-save.json`은 슬롯을 생성하거나 선택할 때 이전을 시도한다.

- 선택 슬롯에 `run-save.json`이 없을 때만 이전한다.
- 선택 슬롯의 기존 Run은 절대 덮어쓰지 않는다.
- 새 위치 기록이 성공한 뒤에만 루트 원본을 삭제한다.
- 대상 슬롯에 Run이 이미 있으면 현재 슬롯 데이터를 우선하고 루트 파일은 보존한다.

## 9. 새 저장 데이터 추가 규칙

1. 데이터가 공통 설정, 슬롯 식별, Run 진행, 향후 메타 진행 중 어디에 속하는지 먼저 결정한다.
2. 해당 DTO에 필드와 안전한 기본값을 추가한다.
3. 해당 포맷의 `CurrentVersion`을 올린다.
4. 직전 버전에서 새 버전으로 가는 마이그레이션을 추가한다.
5. 수집과 복원 순서를 함께 구현한다. 밸런스 수치나 런타임 인스턴스는 저장하지 않는다.
6. 정상 저장·로드, 구버전, 손상 파일, 상위 버전, 저장 실패를 모두 검증한다.
7. 이 문서와 `SystemMap.md`의 계약을 같은 PR에서 갱신한다.

## 10. 검증 체크리스트

- [ ] 슬롯 3개를 생성·선택·삭제할 수 있다.
- [ ] 게임 재실행 후 마지막 선택 슬롯이 복원된다.
- [ ] 슬롯을 바꾸면 해당 슬롯의 Run만 사용한다.
- [ ] 슬롯 이름과 날짜가 현재 로케일로 표시된다.
- [ ] 손상된 `player.json`이 손상 슬롯으로 표시되고 삭제 가능하다.
- [ ] 손상된 `settings.json`이 보존되며 현재 실행의 언어 변경은 적용된다.
- [ ] 지원하지 않는 상위 버전 파일을 거부한다.
- [ ] 기존 평면 player/settings 파일이 봉투 형식으로 다시 저장된다.
- [ ] 구버전 루트 Run이 빈 대상 슬롯으로만 이전된다.
- [ ] 낮 시작 자동 저장과 이어하기 복원이 정상 동작한다.
- [ ] 게임오버·최종 승리 후 Run 세이브가 삭제된다.

