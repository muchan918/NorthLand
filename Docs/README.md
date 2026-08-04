# Docs

NorthLand: Last Stand (팀 유유아) 프로젝트 문서 모음.

## 문서 목록

| 문서 | 내용 |
|---|---|
| [GDD.md](GDD.md) | 게임 디자인 문서. **설계·게임플레이 결정 전에 먼저 읽기.** |
| [Core/MouseManager.md](Core/MouseManager.md) | 마우스 입력/선택/배치 중앙 매니저 설계·구현 현황 (#9) |
| [Core/DayNightManager.md](Core/DayNightManager.md) | 낮/밤 페이즈 전환 매니저·이벤트 훅 설계·구현 현황 (#6) |
| [Core/Tower.md](Core/Tower.md) | 전투 타워 본체 — 조립 모델·투사체·데이터 파이프라인·스탯 원장 **현황**(#164) |
| [Core/TowerAddGuide.md](Core/TowerAddGuide.md) | **새 타워 종류 추가 절차** — CSV·SO·프리팹·씬 등록·로컬라이제이션 7단계 + 검증 + 확장점 3개 (#274). Tower.md가 "왜/무엇"이면 이쪽은 "어떻게" |
| [Core/TowerRedesign.md](Core/TowerRedesign.md) | 타워 구조 재설계 **제안**(#274) — 액션 리스트·효과 부품화·합성 계승. **Phase 1~5 구현·병합 완료 → 폐기 대기. 흡수처는 두 곳(명세=Tower.md / 절차=TowerAddGuide.md)** |
| [Core/TowerMerge.md](Core/TowerMerge.md) | 타워 합성(Merge) — 레시피·매칭·실행(#194/#195) + 멀티 선택·합성 패널(#183) 단일 진실 원천 |
| [Core/InteractionOutline.md](Core/InteractionOutline.md) | 상호작용 아웃라인(호버/선택/합성 프리뷰) 설계·측정 근거 (#213) — **구현 완료**(shell 방식은 임시, 스크린 스페이스 실루엣으로 이행 예정) |
| [Core/UIZOrder.md](Core/UIZOrder.md) | HUD·모달 Canvas 표시 우선순위와 상위 패널 입력 차단 규칙 (#188) |
| [ManagementArea/Resources.md](ManagementArea/Resources.md) | 경영 자원 시스템(지갑·생산처·패널) + 확장 자원 방향(§5.5) |
| [ManagementArea/TerritoryGraph.md](ManagementArea/TerritoryGraph.md) | 경영 영토 확장(그래프 노드) — **영토=미개척 영지 방향 전환(§0)** |
| [ManagementArea/BuildingUpgrade.md](ManagementArea/BuildingUpgrade.md) | 생산 건물 업그레이드(주민당 획득량 증가) 설계 (#139) |
| [ManagementArea/Resident.md](ManagementArea/Resident.md) | 주민 캐릭터(경영 공간 분위기 군중) 명세 + 행위 목록 + BT 구조 — **구현 미착수, 행위 목록은 계속 채워 나가는 표** |
| [Monster/Boss/BossNodeReference.md](Monster/Boss/BossNodeReference.md) | 보스 BT 커스텀 리프 노드 정의 대장 — 보스 간 재사용, 노드가 늘어날 때마다 행 추가. **구현 미착수** |
| [Rendering/VisualLookPipeline.md](Rendering/VisualLookPipeline.md) | 전역 비주얼 룩(미니어처) 파이프라인 설계 — 틸트-시프트·그레이딩·라이팅·툰 셰이더 (#148). **렌더러 피처 순서의 단일 진실 원천**(§3.8) |
| [Tools/StringTable.md](Tools/StringTable.md) | 로컬라이제이션 String Table 사용법·현재 상태 (`ko-KR`/`en-US`/`ja-JP`) |
| [Tools/unity-cli-guide.md](Tools/unity-cli-guide.md) | Unity Editor 제어시 명령어·워크플로우 |
| [Review/SystemMap.md](Review/SystemMap.md) | 시스템 지도·공개 API·접점 매트릭스·팀 계약 — **PR 리뷰(자동/수동) 판단 기준** |
| [Review/WatchList.md](Review/WatchList.md) | 리뷰 간 누적 아키텍처 이슈 원장 (WL-번호) |
| [Build0/](Build0/) | 빌드 0 기록 (빌드 노트 / 결과 보고서 / 다음 계획) |
| [Integration/](Integration/) | 통합(Integration) 이슈별 작업 기록 — 무엇을 왜 합쳤는지, 발견한 버그, 갱신한 문서 |

## 폴더 규칙

- **`Core/`** — 게임 시스템 설계 및 구현 현황 문서
- **`ManagementArea/`** — 경영 공간 시스템 설계·현황 문서(자원·영토·건물 업그레이드)
- **`Monster/`** — 몬스터 시스템 문서. `Monster/Boss/`는 보스 BT — 노드 대장(`BossNodeReference.md`)만 보스 공용이라 위 목록에 등재하고, 보스 설계 문서는 보스마다 1본씩 늘어나므로 등재하지 않는다
- **`Tools/`** — 패키지·도구 사용 가이드
- **`Review/`** — PR 리뷰 기준 문서 (시스템 지도·통합 계약 / 추적 이슈 원장). 공개 API·계약이 바뀌면 같은 PR에서 갱신
- **`Build{N}/`** — 빌드별 기록
- **`Integration/`** — `[Integration]` 이슈(#65, #66, …)별 작업 기록. 파일명은 `Integration-{이슈번호}.md`.
  각 파일은 그 이슈 시점의 스냅샷이므로 이후 코드 변경에 맞춰 소급 수정하지 않는다 — 최신 구조는
  `Core/`·`Review/` 문서를 따른다

> 시스템을 구현·변경하면 해당 문서도 함께 갱신해, 문서와 코드/에셋이 어긋나지 않게 유지한다.
