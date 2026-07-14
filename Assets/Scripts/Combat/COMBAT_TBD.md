# 전투 시스템 TBD (미결정 사항)

> 팀 컨벤션 회의 후 결정 및 수정 예정. 결정되면 이 문서와 관련 코드 주석(`TODO(TBD)`)을 함께 갱신할 것.

## 1. 대상 탐지 필터링 방식: LayerMask vs Tag

- **현재 상태(임시):** `LayerMask` 방식 사용.
  - `Physics.OverlapSphere(..., layerMask)`로 물리 쿼리 단계에서 대상 진영 레이어만 수집.
  - 관련 코드: `Tower.enemyLayerMask`, `Enemy.targetLayerMask`, `PlayerUnit.targetLayerMask` (각 파일에 `TODO(TBD)` 주석 있음).
  - 임시로 추가 필요한 레이어: `Enemy`, `PlayerUnit`, `PlayerBase` (또는 회의 결과에 따라 통합/변경).
- **대안:** `Tag` 기반 필터링, 또는 별도 진영 컴포넌트 참조 방식 등.
- **결정 시 반영 위치:**
  - 각 클래스의 `FindTarget()` 내부 탐지/필터 로직
  - 위 `[SerializeField] LayerMask ...` 필드
  - `ProjectSettings/TagManager.asset` (레이어/태그 정의 — 공유 파일, 팀 합의 필요)

## 2. 참고: 코드상의 진영 판별(`Faction`)은 위 결정과 별개

- `Faction` enum(`Player`/`Enemy`)은 코드 레벨의 진영 판별용이며 Unity Layer/Tag와 독립적임.
- 필터링 방식이 무엇으로 결정되든 `Faction != Faction` 2차 검증(오사 방지)은 유지 예정.
