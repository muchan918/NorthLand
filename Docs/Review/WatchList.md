# WatchList — 누적 아키텍처 이슈 원장 (PR 리뷰 필수 입력)

> **목적**: PR 리뷰 간 '기억'. 리뷰 봇은 매 리뷰에서 이 목록과 대조하고 해결/악화/신설을 보고한다.
> **갱신 규칙**: 봇이 리뷰 코멘트의 "📒 WatchList 갱신 제안" 절에 항목을 제안하면, 팀이 검토 후
> 이 파일에 커밋한다. 봇이 직접 커밋하지 않는다.
> **RESOLVED 항목은 삭제하지 않고 상태만 바꾼다** (재발 감지용). 해결 PR 번호를 상태에 남긴다.
> **형식**: WL-번호 | 상태(OPEN/RESOLVED) | 등록 PR | 내용 | 해소 조건

- **WL-001** | OPEN | PR#29 | 스탯 데이터 이원화: Combat의 TowerData/EnemyData SO 직접 입력 vs DataTable CSV 파이프라인(문서 §7이 Tower 테이블 확장 예고) | Tower/Enemy 스탯의 단일 원본(CSV) 합의 및 마이그레이션
- **WL-002** | OPEN | PR#31 | 매니저 수명주기 3종 난립(static/DontDestroyOnLoad/씬 싱글톤) + MouseManager.\_camera 씬 참조가 씬 전환 시 끊어짐 | 부트스트랩/매니저 규약 문서화
- **WL-003** | OPEN | PR#32 | 몬스터 이동에 필요한 '순서 있는 경로' 미노출: StageBuilder.path private+Clear, RoadWorldPoints는 HashSet. 스폰 지점·최종 목표 좌표도 비공개 | MapBuilder에 순서 보존 경로 + 스폰/목표 공개 API 신설
- **WL-004** | OPEN | PR#31/32 | 배치 검증 공백: CanPlaceAt 항상 true, Snap 항등, 타일 종류(도로/용암/잔디) 질의 API 없음 → 도로·용암 위 타워 설치 가능 | MapBuilder 타일 질의 API + MouseManager 연동
- **WL-005** | OPEN | PR#29/31 | 레이어 규약 부재: Enemy/Selectable/Ground 레이어가 각자 SerializeField, TagManager 변경 무검토 이력 | 레이어 규약 문서화 + SystemMap 등재
- **WL-006** | OPEN | PR#22 | 에셋 로딩 이원화: Resources.Load(DataTable) vs Addressables(Localization) | 로딩 전략 단일화 결정
- **WL-007** | OPEN | - | 좌표계 계약 부재: MapBuilder는 battlespace 로컬 그리드, MouseManager/Combat은 월드 좌표 — 변환 유틸 없음 | 좌표 변환 계약 정의 (배치·이동 착수 전)
- **WL-008** | OPEN | PR#32 | 로그라이크 시드 재현성: 전역 UnityEngine.Random 사용, 시드 주입 설계 없음 | Run 시드 설계 후 MapRandom에 주입
- **WL-009** | OPEN | PR#32 | 용어 충돌: StageWaypoint(블록 연결점) vs GDD 웨이포인트(병사 배치 지점), '스테이지'(블록) vs GDD 스테이지(런 단위) | 병사 시스템 착수 전 리네임
- **WL-010** | OPEN | PR#29 | 폴더명 오타 `Assets/Personal/SUNGSOO/Scirpts` — 참조 늘기 전 수정 필요(meta GUID) | 폴더 리네임
- **WL-011** | OPEN | PR#31 | 선택 통지 이중 경로: OnSelectionChanged 이벤트(구독자 0) vs SelectableTest→TowerInfoUI 직접 호출 | 정본 경로 결정
- **WL-012** | OPEN | - | GDD §9 미확정 항목 결합 주의: 병사/스킬 통합 여부, 몬스터 테마, 스테이지/보스 구성, 밸런싱 수치 → 관련 코드는 결합을 느슨하게 | GDD 확정 시 해제
- **WL-013** | OPEN | PR#20/22 | 표시 문자열 소유권: ResourceData.DisplayName CSV 한글 하드코딩 vs Localization String Table | UI 노출 문자열의 키 이관 방침 결정
- **WL-014** | OPEN | PR#22/29/31 | Get/Instance 계열 null 무가드 역참조 반복 (DataTableManager.Get, MouseManager.Instance 등) | 호출부 null 가드 관행 정착
- **WL-015** | OPEN | PR#46 | 건물 밸런싱 수치(비용/주민당 생산량) 소유권 이원화: CSV 파이프라인(contract②) vs BuildingAsset SO 인스펙터. 현재 값 미기입 → #5/#8 소비 불가 | 수치 데이터 원본 합의 + 값 기입
- **WL-016** | OPEN | PR#46 | BuildingAsset.Data 캐시가 건물 타입당 단일 SO — 인스턴스별 레벨/주민 상태(GDD §4.2 업그레이드) 도입 시 공유 SO 덮어쓰기 위험 | 정적 조회 데이터 vs per-instance 상태 경계 확정
