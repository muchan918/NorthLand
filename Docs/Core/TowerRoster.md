# 타워 획득 경로 정리표 (TowerRoster)

> **상태**: 획득 경로(직접배치/합성 전용) 표만 우선 작성 — 스탯·역할 전수 카탈로그(Cost/Attack/Impact/BuffAura/DebuffAura/Effects 열 포함)는 [#282](https://github.com/muchan918/NorthLand/issues/282) 완료 기준의 남은 범위로 별도.
> **작성 계기**: [#298](https://github.com/muchan918/NorthLand/issues/298) 리뷰에서 개틀링·스나이퍼가 직접배치 로스터에서 제외된 것이 #282가 사인오프한 결정 범위 밖임이 지적됨(WL-156) — 팀이 확인할 수 있도록 획득 경로 결정 현황을 한 곳에 모은다.
> **관련**: `Docs/GDD.md` §5.8(레시피 족보·합성 비용 TBD), `Docs/Core/TowerMerge.md`, `Docs/Review/WatchList.md`(WL-156)

## 획득 경로 표

| TowerID | 이름(가칭) | 획득 경로 | 재료(합성 전용만) | 근거 |
|---|---|---|---|---|
| `archer_tower` | 아처 | 직접배치 | — | 최초 9종 |
| `cannon_tower` | 캐논 | 직접배치 | — | 최초 9종 |
| `poison_tower` | 포이즌 | 직접배치 | — | 최초 9종 |
| `haste_tower` | 헤이스트 | 직접배치 | — | 최초 9종 |
| `choco_tower` | 초코(슬로우) | 직접배치 | — | 최초 9종 |
| `soda_tower` | 소다(스턴) | 직접배치 | — | 최초 9종 |
| `flame_field_tower` | 화염지대 | 직접배치 | — | #282 확정 — 장판형만 기본 배치 유지 |
| `flame_archer_tower` | 화염궁수 | 합성 전용 | 아처×1 + 화염지대×1 (`Recipe_FlameArcherTower`) | #282 "획득 경로 결정" 표로 확정 |
| `incendiary_cannon_tower` | 소이캐논 | 합성 전용 | 캐논×1 + 화염지대×1 (`Recipe_IncendiaryCannonTower`) | #282 "획득 경로 결정" 표로 확정 |
| `shotgun_tower` | 산탄 | 합성 전용 | 아처×3 (`Recipe_ShotgunTower`) | #298 신규 |
| `boomerang_tower` | 부메랑 | 합성 전용 | 아처×1 + 캐논×2 (`Recipe_BoomerangTower`) | #298 신규 — 원안(아처×2+캐논×1)이 기존 `Recipe_Example_Sniper`와 겹쳐 비율을 뒤집음 |
| `multi_inferno_tower` | 멀티인페르노 | 합성 전용 | 개틀링×2 + 포이즌×1 (`Recipe_multiinfernoTower`) | #298 신규 |
| `gatling_tower` | 개틀링 | 합성 전용 ⚠ | 아처×2 (`Recipe_Example_Gatling`) | **#298(커밋 1b69373)에서 직접배치 로스터 제외로 전환 — #282 "획득 경로 결정" 표엔 없는 신규 판단, 팀 사인오프 미완료(WL-156)** |
| `Sniper_tower` | 스나이퍼 | 합성 전용 ⚠ | 아처×1 + 캐논×1 (`Recipe_Example_Sniper`) | 위와 동일(WL-156) |
| `lightning_tower` | 번개사슬 | **없음(획득 불가)** | — | #282 후보#3 — SO만 존재, 레시피·프리팹 미배선. 별도 착수 필요 |

## 미해결 항목

- **WL-156**: 개틀링·스나이퍼의 합성 전용 전환은 화염아처·소이캐논과 달리 팀(muchan918/밸런싱)의 명시적 사인오프가 없다. 이 표를 근거로 리뷰·확인 필요 — 승인되면 이 줄의 ⚠ 제거, 반려되면 두 타워를 `TowerSelectPanelView._towers`에 복귀.
- **레시피 비용 저작 불일치**: 신규 3종(산탄/부메랑/멀티인페르노)은 `Cost: []` + `ExtraCost: []`(무료)인데, 화염아처/소이캐논은 `Cost`가 채워져 있다. GDD §5.8이 합성 비용 규칙을 TBD로 남긴 상태라 아직 규칙화되지 않음.
- **`lightning_tower`**: #282 완료 기준의 필수 항목(후보#3)인데 아직 레시피·프리팹이 없어 획득 불가 상태로 방치돼 있다. 별도 이슈로 진행 필요.
