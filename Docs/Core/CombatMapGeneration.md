# 전투맵 생성 규칙

## 1. 목적과 정본

이 문서는 `Assets/Scripts/CombatSpace/Map`의 절차적 전투맵 생성 설정과 안정성 검증 규칙의 정본이다.

- 설정 에셋: `Assets/Resources/ScriptableObjects/CombatMapSetting/CombatMapGenerationSettings.asset`
- 런타임 생성기: `Assets/Scripts/CombatSpace/Map/CombatMapGenerator.cs`
- 안정성 테스트: `CombatMapGenerator.RunStabilityTest()`
- 씬별 폴백 시드 목록: `CombatMapGenerator.validatedSeeds`

구 `Assets/Scripts/CombatSpace/MapBuilder` 기반 구현은 현재 정본이 아니다.

## 2. 현재 맵 설정

| 항목 | 값 | 의미 |
| --- | ---: | --- |
| `Width` / `Height` | 36 / 36 | 전체 논리 그리드 크기 |
| `MapMargin` | 7 | 웨이포인트 배치 영역의 바깥 여백 |
| 웨이포인트 사용 영역 | 22×22 | `36 - 7×2` |
| `MinWaypointCount` | 6 | 최소 주요 웨이포인트 수 |
| `MaxWaypointCount` | 9 | 최대 주요 웨이포인트 수 |
| `MinWaypointDistance` | 7 | 주요 웨이포인트 사이의 초기 최소 거리 |
| `MaxWaypointPlacementAttempts` | 30 | 거리 단계별 웨이포인트 후보 생성 횟수 |
| `RouteNoiseScale` | 0.08 | 경로 노이즈 좌표 배율 |
| `RouteNoiseWeight` | 3 | A* 경로 비용에 적용하는 노이즈 영향 |
| `TurnPenalty` | 0.5 | 경로가 방향을 바꿀 때 추가하는 비용 |
| `WaterRoadClearance` | 0 | 도로와 Water의 직접 인접을 허용 |

`WaterRoadClearance = 0`은 2026-08-13 회의에서 확정한 의도적인 설정이다. 도로 완충을 사용하지 않고 Water가 Road 바로 옆에 생성되는 것을 허용한다. 이 결정은 본진 보호 밴드 문제(WL-089)를 해소한 것으로 보지 않는다.

## 3. 맵 축소에 따른 설정 조정

맵 크기를 70×70에서 36×36으로 축소하면서 웨이포인트가 실제로 배치되는 영역은 22×22로 줄었다.

기존 설정은 다음과 같았다.

- 웨이포인트 수: 8~12개
- `MinWaypointDistance`: 15

`WaypointGenerator`는 설정한 최소 거리로 웨이포인트 배치를 반복해서 시도하고, 실패할 때마다 거리를 1씩 줄인다. 22×22 영역을 3×3 셀로 나누면 한 셀의 크기가 약 7×7이므로 거리 15는 유지되기 어렵고 런타임 완화에 의존하게 된다.

좁은 맵에 많은 웨이포인트를 두면 경로가 여러 번 접힌다. `RouteGenerator`는 이미 생성된 Road와 겹치거나 예상하지 않은 위치에서 인접하는 경로를 허용하지 않으므로, 다음 구간을 찾지 못해 생성에 실패할 수 있다.

이에 따라 현재 설정을 다음과 같이 조정했다.

- `MinWaypointCount`: 8 → 6
- `MaxWaypointCount`: 12 → 9
- `MinWaypointDistance`: 15 → 7

## 4. 안정성 테스트 결과

2026-08-13 현재 설정으로 연속된 시드 100개를 검사했다.

| 항목 | 결과 |
| --- | ---: |
| 테스트 시드 | 100개 |
| 성공 | 95개 |
| 실패 | 5개 |
| 성공률 | 95.0% |
| Road 최소 | 64칸 |
| Road 최대 | 136칸 |
| Road 평균 | 87.0칸 |
| Grass 평균 | 382.2칸 |
| Water 평균 | 157.4칸 |
| 전체 시간 | 2377ms |
| 맵당 평균 | 23.8ms |

이 테스트에서 통과한 폴백 시드는 다음과 같다.

```text
1, 2, 3, 5, 6
```

`GameScene`의 `CombatMapGenerator.validatedSeeds`는 위 목록을 사용한다. 일반 생성은 요청 시드부터 최대 5회 시도한 뒤, 모두 실패하면 이 목록을 결정적인 순서로 순회한다.

## 5. 설정 변경 시 검증 규칙

다음 항목을 변경하면 같은 브랜치에서 안정성 테스트 100회를 다시 실행한다.

- `Width` / `Height`
- `MapMargin`
- 웨이포인트 최소·최대 개수
- `MinWaypointDistance`
- `RouteNoiseScale` / `RouteNoiseWeight`
- `TurnPenalty`
- Water 생성 설정
- 경로 생성 또는 `RouteValidator` 규칙

테스트 결과에는 다음 값을 기록한다.

- 성공·실패 개수와 성공률
- Road 최소·최대·평균 길이
- Grass·Water 평균 타일 수
- 전체 시간과 맵당 평균 시간
- 최초 실패 원인 로그
- 새 설정에서 실제로 통과한 폴백 시드

폴백 시드는 과거 설정에서 사용하던 목록을 그대로 유지하지 않는다. 현재 설정으로 다시 검증된 시드만 `validatedSeeds`에 등록한다.

## 6. 조정 우선순위

생성 성공률이 낮아지면 실패 로그를 먼저 확인한다.

- `Waypoint 생성 실패`: `MinWaypointDistance`, 웨이포인트 개수, `MaxWaypointPlacementAttempts`를 검토한다.
- `Road 경로 생성 실패`: 웨이포인트 개수와 `RouteNoiseWeight`를 먼저 검토한다.
- `Road 검증 실패`: 순서상 이웃이 아닌 Road의 인접 또는 중복 좌표 여부를 확인한다.

한 번에 여러 값을 바꾸지 않고 한 항목씩 변경한 뒤 같은 시드 범위로 다시 측정한다.

## 7. 세이브 호환성

전투맵은 전체 타일을 저장하지 않고 마스터 시드, 전투맵 파생 시드와 생성 설정으로 재생성한다. 설치된 타워는 재생성된 맵의 그리드 좌표를 기준으로 복원된다.

따라서 다음 항목을 변경하면 같은 시드에서도 다른 맵이 생성될 수 있다.

- 맵 크기와 여백
- 웨이포인트 개수와 최소 거리
- 경로 생성 및 검증 규칙
- Grass 생성과 침식 설정
- Water 생성 설정
- 시드 파생 또는 맵 생성 알고리즘

이러한 변경은 기존 Run 세이브와 호환되지 않는다. 호환되지 않는 세이브를 그대로 복원하면 저장된 타워 좌표가 맵 범위를 벗어나거나 Road·Water를 가리킬 수 있다.

2026-08-13의 70×70 → 36×36 맵 축소와 생성 설정 조정은 기존 Run 세이브를 폐기하는 변경으로 결정했다. 새 Run에는 새 시드 재현 버전을 기록하고, 이전 버전의 Run은 로드 단계에서 명시적으로 거부한다.

앞으로 시드로 재생성되는 결과가 달라지는 변경에는 다음 작업을 함께 수행한다.

1. 시드 재현 버전 증가
2. 기존 Run 세이브 호환 불가 공지
3. 안정성 테스트 재실행 및 결과 기록
4. 현재 설정에서 통과한 `validatedSeeds` 재선정

단순 밸런스 수치처럼 맵의 타일 종류·경로·좌표 결과를 바꾸지 않는 변경에는 시드 재현 버전을 올리지 않는다.
