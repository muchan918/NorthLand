# Docs

NorthLand: Last Stand (팀 유유아) 프로젝트 문서 모음.

## 문서 목록

| 문서 | 내용 |
|---|---|
| [GDD.md](GDD.md) | 게임 디자인 문서. **설계·게임플레이 결정 전에 먼저 읽기.** |
| [Core/MouseManager.md](Core/MouseManager.md) | 마우스 입력/선택/배치 중앙 매니저 설계·구현 현황 (#9) |
| [Core/DayNightManager.md](Core/DayNightManager.md) | 낮/밤 페이즈 전환 매니저·이벤트 훅 설계·구현 현황 (#6) |
| [Rendering/VisualLookPipeline.md](Rendering/VisualLookPipeline.md) | 전역 비주얼 룩(미니어처) 파이프라인 설계 — 틸트-시프트·그레이딩·라이팅·툰 셰이더 (#148) |
| [Tools/StringTable.md](Tools/StringTable.md) | 로컬라이제이션 String Table 사용법·현재 상태 (`ko-KR`/`en-US`/`ja-JP`) |
| [Tools/unity-cli-guide.md](Tools/unity-cli-guide.md) | Unity Editor 제어시 명령어·워크플로우 |
| [Review/SystemMap.md](Review/SystemMap.md) | 시스템 지도·공개 API·접점 매트릭스·팀 계약 — **PR 리뷰(자동/수동) 판단 기준** |
| [Review/WatchList.md](Review/WatchList.md) | 리뷰 간 누적 아키텍처 이슈 원장 (WL-번호) |
| [Build0/](Build0/) | 빌드 0 기록 (빌드 노트 / 결과 보고서 / 다음 계획) |
| [Integration/](Integration/) | 통합(Integration) 이슈별 작업 기록 — 무엇을 왜 합쳤는지, 발견한 버그, 갱신한 문서 |

## 폴더 규칙

- **`Core/`** — 게임 시스템 설계 및 구현 현황 문서
- **`Tools/`** — 패키지·도구 사용 가이드
- **`Review/`** — PR 리뷰 기준 문서 (시스템 지도·통합 계약 / 추적 이슈 원장). 공개 API·계약이 바뀌면 같은 PR에서 갱신
- **`Build{N}/`** — 빌드별 기록
- **`Integration/`** — `[Integration]` 이슈(#65, #66, …)별 작업 기록. 파일명은 `Integration-{이슈번호}.md`.
  각 파일은 그 이슈 시점의 스냅샷이므로 이후 코드 변경에 맞춰 소급 수정하지 않는다 — 최신 구조는
  `Core/`·`Review/` 문서를 따른다

> 시스템을 구현·변경하면 해당 문서도 함께 갱신해, 문서와 코드/에셋이 어긋나지 않게 유지한다.
