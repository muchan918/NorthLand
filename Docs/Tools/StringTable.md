# 스트링 테이블 (String Table) 가이드

Unity 공식 Localization 패키지(`com.unity.localization`)의 String Table을 프로젝트에서
어떻게 사용하는지, 그리고 현재 합의된 테이블/키 구성을 정리한 문서. 개발 중 텍스트를
새로 추가하거나 UI에 연결할 때 참고 용도로 쓴다.

> 이 문서의 키 목록은 `Docs/GDD.md` 기준으로 잡은 **초안**이다. 실제 Unity 에디터에
> 테이블/키를 만들거나 수정한 사람은 이 문서도 함께 갱신해서, 문서와 실제 에셋이
> 어긋나지 않게 유지한다.

## 1. 지원 언어 (Locale)

| Locale | 코드 | 비고 |
|---|---|---|
| 한국어 | `ko` | 기준 언어. 번역 누락 시 Fallback |
| 영어 | `en` | |
| 일본어 | `ja` | |

키를 새로 추가할 때는 한국어부터 채우고, en/ja는 번역 전까지 비워둬도 된다 (Fallback으로 한국어가 노출됨).

## 2. 테이블 구성

GDD의 시스템 구분(경영 공간 / 전투 공간 / 공통)에 맞춰 기능 단위로 테이블을 나눈다.

| 테이블 이름 | 용도 | 관련 GDD 섹션 |
|---|---|---|
| `UI_Common` | 확인/취소 등 공통 버튼, 공통 팝업 문구 | - |
| `UI_Village` | 경영 공간: 주민 배치, 건물 건설, 경영 영토 확장 | 5.1, 6.1, 6.2, 6.3 |
| `UI_Combat` | 전투 공간: 타워 배치, 병사 배치, 스킬 사용 | 5.2, 6.2, 6.4, 6.5 |
| `System_Notice` | 낮/밤 전환, 본진 체력, 게임오버/클리어 등 시스템 알림 | 4.4, 5.1, 5.2 |
| `Reward_Choice` | 웨이브 종료 보상 선택 UI | 5.2, 6.6 |

## 3. 키 목록 (초안)

키 네이밍 규칙: `카테고리.요소` 소문자 스네이크 케이스. 한 번 배포된 키는 리네임하지 않고
값(텍스트)만 수정한다 (번역 매핑이 키 기준이라 리네임 시 참조가 끊어짐).

### UI_Common

| 키 | 한국어 (기본값) |
|---|---|
| `ui.common.confirm` | 확인 |
| `ui.common.cancel` | 취소 |
| `ui.common.close` | 닫기 |

### UI_Village (경영 공간)

| 키 | 한국어 (기본값) |
|---|---|
| `ui.village.villager_assign_title` | 주민 배치 |
| `ui.village.villager_assign_production` | 생산 건물에 배치 |
| `ui.village.villager_assign_training` | 훈련장에 배치 |
| `ui.village.building_unlock_title` | 건물 건설 |
| `ui.village.territory_expand_title` | 영토 확장 |
| `ui.village.territory_expand_confirm` | 이 영토를 확장하시겠습니까? |
| `ui.village.territory_effect_label` | 영토 효과 |

### UI_Combat (전투 공간)

| 키 | 한국어 (기본값) |
|---|---|
| `ui.combat.tower_place_title` | 타워 배치 |
| `ui.combat.tower_place_invalid` | 이곳에는 타워를 배치할 수 없습니다 |
| `ui.combat.soldier_place_title` | 병사 배치 |
| `ui.combat.soldier_place_waypoint_only` | 병사는 웨이포인트에만 배치할 수 있습니다 |
| `ui.combat.skill_select_title` | 스킬 선택 |
| `ui.combat.skill_target_select` | 스킬을 사용할 위치를 지정하세요 |

### System_Notice

| 키 | 한국어 (기본값) |
|---|---|
| `system_notice.day_start` | 낮이 시작됩니다. 본진 체력이 회복됩니다 |
| `system_notice.night_start` | 밤이 시작됩니다. 몬스터가 몰려옵니다 |
| `system_notice.wave_start` | {wave}웨이브 시작. 남은 몬스터: {count}마리 |
| `system_notice.wave_clear` | 웨이브 클리어 |
| `system_notice.game_over` | 본진이 함락되었습니다 |
| `system_notice.stage_clear` | 최종 보스를 처치했습니다. 승리! |

### Reward_Choice

| 키 | 한국어 (기본값) |
|---|---|
| `reward_choice.title` | 보상을 선택하세요 |
| `reward_choice.option_confirm` | 이 보상을 선택합니다 |

## 4. 코드에서 사용하기

### 4.1 인스펙터 노출 (기본 방식)

```csharp
using UnityEngine;
using UnityEngine.Localization;

public class TerritoryExpandPopup : MonoBehaviour
{
    [SerializeField] private LocalizedString titleString; // 인스펙터에서 UI_Village / ui.village.territory_expand_title 선택

    private void OnEnable() => titleString.StringChanged += OnTitleChanged;
    private void OnDisable() => titleString.StringChanged -= OnTitleChanged;

    private void OnTitleChanged(string value)
    {
        // TMP_Text.text = value;
    }
}
```

인스펙터에서 테이블과 키를 드롭다운으로 고르는 방식이라 코드에 키 문자열을 직접 적지 않아도 된다.
오타/키 불일치를 줄여주므로 기본으로 사용한다.

### 4.2 TMP 컴포넌트에 직접 연결

값이 고정된 라벨(버튼 텍스트 등)은 스크립트 없이 `LocalizeStringEvent` 컴포넌트를
`TMP_Text`가 붙은 GameObject에 추가하고, `On Update String` 이벤트에 `TMP_Text.text`를
연결해서 처리한다.

### 4.3 변수가 들어가는 문장 (Smart String)

`system_notice.wave_start`처럼 `{wave}`, `{count}` 같은 변수가 들어간 키는 해당
String Table 엔트리를 Smart 타입으로 설정해두고, 코드에서는 값만 채워 넣는다.

```csharp
waveStartString.Arguments = new object[] { waveNumber, monsterCount };
waveStartString.RefreshString();
```

### 4.4 키를 문자열로 직접 조회 (예외적인 경우만)

```csharp
using UnityEngine.Localization.Settings;

var op = LocalizationSettings.StringDatabase.GetLocalizedStringAsync("System_Notice", "system_notice.night_start");
op.Completed += handle => Debug.Log(handle.Result);
```

인스펙터 노출이 어려운 코드(로그, 디버그 출력 등)에서만 사용한다.

## 5. 신규 키 추가할 때

1. 어느 테이블에 속하는지 2절 표에서 먼저 확인 (없으면 팀에 새 테이블 필요 여부 논의)
2. `카테고리.요소` 규칙으로 키 이름 작성, 3절 표에 먼저 추가
3. Unity 에디터의 String Table Collection에 동일한 키/한국어 기본값 입력
4. en/ja는 번역 준비되면 채움 (미번역 상태로 커밋해도 무방, Fallback으로 한국어 노출)
