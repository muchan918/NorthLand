# 스트링 테이블 (String Table) 가이드

Unity 공식 Localization 패키지(`com.unity.localization`)의 String Table 사용법과 현재 상태를 정리한 문서.
개발 중 텍스트를 추가하거나 UI에 연결할 때 참고한다.

- 패키지: `com.unity.localization` (설치됨), 내부적으로 Addressables 사용
- 에셋 위치: `Assets/Localization/`
- 언어 전환 테스트 스크립트: `Assets/Scripts/Test/LocalizationTest.cs`

> 실제로 테이블/키를 만들거나 바꾼 사람은 이 문서도 함께 갱신해 문서와 에셋이 어긋나지 않게 유지한다.

## 1. 지원 언어 (Locale)

| 언어 | Locale 이름 | **코드 (m_Code)** |
|---|---|---|
| 한국어 | Korean (ko) | `ko-KR` |
| 영어 | English (United States) | `en-US` |
| 일본어 | Japanese (Japan) | `ja-JP` |

⚠️ **코드에서 `GetLocale(...)` 할 때는 반드시 위 `m_Code` 값**(`ko-KR`/`en-US`/`ja-JP`)을 써야 매칭된다.
한국어 Locale 이름이 "Korean (ko)"이라 `"ko"`로 착각하기 쉬운데, 실제 코드는 `ko-KR`이다.

```csharp
LocalizationSettings.SelectedLocale =
    LocalizationSettings.AvailableLocales.GetLocale("ko-KR");
```

## 2. 현재 상태 (실제 생성된 것)

- **테이블 컬렉션**: `NorthLand_default` **1개** (String Table Collection)
- **키**: `Test` **1개** (placeholder, 한국어 값 `"Test-kr"`)
- 아직 본격적인 UI/시스템 텍스트는 넣지 않았다. 실제 텍스트를 추가할 때는 [4. 키·테이블 컨벤션(제안)](#4-키--테이블-컨벤션-제안)을 따른다.

## 3. 코드에서 사용하기

### 3.1 인스펙터 노출 (기본 방식)

```csharp
using UnityEngine;
using UnityEngine.Localization;

public class SomeUI : MonoBehaviour
{
    [SerializeField] private LocalizedString label; // 인스펙터에서 NorthLand_default / Test 등 선택

    private void OnEnable()  => label.StringChanged += v => { /* TMP_Text.text = v; */ };
}
```

인스펙터에서 테이블·키를 드롭다운으로 고르므로 코드에 키 문자열을 직접 적지 않아도 된다(오타 방지). 기본으로 사용.

### 3.2 TMP 컴포넌트에 직접 연결

값이 고정된 라벨은 스크립트 없이 `LocalizeStringEvent` 컴포넌트를 `TMP_Text` 오브젝트에 추가하고
`On Update String`에 `TMP_Text.text`를 연결.

### 3.3 언어 전환

`LocalizationSettings.SelectedLocale`을 바꾸면 씬의 모든 참조가 자동 갱신된다. (예시: `LocalizationTest.cs`)

### 3.4 코드에서 키로 직접 조회 (예외적인 경우만)

```csharp
using UnityEngine.Localization.Settings;

var op = LocalizationSettings.StringDatabase
    .GetLocalizedStringAsync("NorthLand_default", "Test");
op.Completed += h => Debug.Log(h.Result);
```

로그·디버그 등 인스펙터 노출이 어려운 경우에만 사용.

### 3.5 변수가 들어가는 문장 (Smart String)

숫자/이름이 들어가는 문장은 해당 엔트리를 Smart 타입으로 설정하고 값을 채운다.

```
{wave}웨이브 시작. 남은 몬스터: {count}마리
```
```csharp
waveStartString.Arguments = new object[] { waveNumber, monsterCount };
waveStartString.RefreshString();
```

## 4. 키·테이블 컨벤션 (제안)

> 아직 적용 전. 실제 텍스트를 추가하기 시작할 때 이 규칙을 따르자는 제안이다.

**키 네이밍**: `카테고리.요소` 소문자 스네이크 케이스. 한 번 배포된 키는 리네임하지 않고 값만 수정한다(번역 매핑이 키 기준).

```
ui.common.confirm
ui.village.territory_expand_title
ui.combat.tower_place_invalid
system_notice.wave_start
reward_choice.title
```

**테이블 분리**: 하나의 거대한 테이블 대신 기능 단위로 나눈다(제안).

| 테이블 | 용도 | GDD |
|---|---|---|
| `UI_Common` | 확인/취소 등 공통 | - |
| `UI_Village` | 경영 공간(주민·건물·영토 확장) | 5.1, 6.1~6.3 |
| `UI_Combat` | 전투 공간(타워·병사·스킬) | 5.2, 6.2·6.4·6.5 |
| `System_Notice` | 낮/밤 전환, 웨이브, 게임오버 등 | 4.4, 5.x |
| `Reward_Choice` | 웨이브 보상 선택 | 5.2, 6.6 |

(현재는 `NorthLand_default` 하나만 존재. 위 구조로 갈지, 단일 테이블을 유지할지는 팀 합의 후 결정.)

## 5. 신규 키 추가 절차

1. 어느 테이블에 속하는지 정하기 (없으면 새 테이블 필요 여부 논의)
2. `Window > Asset Management > Localization Tables`에서 키 추가 + 한국어 값 입력
3. `en-US`/`ja-JP`는 번역 준비되면 채움 (미번역이면 Fallback으로 한국어 노출 — Locale의 Fallback 설정 확인)
4. 이 문서의 현재 상태/키 목록도 갱신

## 6. 참고

- Unity Localization 매뉴얼: https://docs.unity3d.com/Packages/com.unity.localization@latest
