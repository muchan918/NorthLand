# WatchList — 누적 아키텍처 이슈 원장 (PR 리뷰 필수 입력)

> **목적**: PR 리뷰 간 '기억'. 리뷰 봇은 매 리뷰에서 이 목록과 대조하고 해결/악화/신설을 보고한다.
> **갱신 규칙**: 봇이 리뷰 코멘트의 "📒 WatchList 갱신 제안" 절에 항목을 제안하면, 팀이 검토 후
> 이 파일에 커밋한다. 봇이 직접 커밋하지 않는다.
> **RESOLVED 항목은 삭제하지 않고 상태만 바꾼다** (재발 감지용). 해결 PR 번호를 상태에 남긴다.
> **형식**: WL-번호 | 상태(OPEN/RESOLVED) | 등록 PR | 내용 | 해소 조건

- **WL-001** | OPEN | PR#29/53/59 | 스탯 SO 손입력 이원화 심화: TowerData/EnemyData/PlayerUnitData(SO 직접 입력) vs muchan 폴더 CSV 파이프라인. `TowerAsset`(CSV 기반 데이터 레이어)이 마련됐으나 Combat `Tower.cs`는 아직 미참조 — 마이그레이션은 SUNGSOO와 합의 후 별도 진행. PR#59 리뷰 메모: 공격 계약 정본을 `TowerAsset.*.Attack`로 확정할지 SUNGSOO와 합의 필요(이슈#25가 약속한 계약이 `TowerAsset`으로 옮겨감, `TowerAsset.AttackFields`가 Combat `TowerData` 필드를 이름까지 복제해 이중 표현). 또한 muchan의 전역 `TowerData`(POCO)와 `NorthLand.Combat.TowerData`(SO)가 동명 타입 — 지금은 네임스페이스가 달라 충돌 없지만 마이그레이션 시 혼동 우려, 한쪽 리네임 권장 | Tower/Enemy/PlayerUnit 스탯 단일 원본 합의 및 마이그레이션
- **WL-002** | OPEN | PR#31 | 매니저 수명주기 3종 난립(static/DontDestroyOnLoad/씬 싱글톤) + MouseManager.\_camera 씬 참조가 씬 전환 시 끊어짐 | 부트스트랩/매니저 규약 문서화 + TooltipUI(#38) 임시 싱글톤 추가 — UIManager 흡수 대상 3종째
- **WL-003** | OPEN | PR#32 | 몬스터 이동에 필요한 '순서 있는 경로' 미노출: StageBuilder.path private+Clear, RoadWorldPoints는 HashSet. 스폰 지점·최종 목표 좌표도 비공개 | MapBuilder에 순서 보존 경로 + 스폰/목표 공개 API 신설
- **WL-004** | OPEN | PR#31/32 | 배치 검증 공백: CanPlaceAt 항상 true, Snap 항등, 타일 종류(도로/용암/잔디) 질의 API 없음 → 도로·용암 위 타워 설치 가능 | MapBuilder 타일 질의 API + MouseManager 연동
- **WL-005** | OPEN | PR#29/31/53 | 레이어 규약 부재: Enemy/Selectable/Ground에 더해 PR#53에서 PlayerUnit·PlayerBase·Enemy(targetLayerMask) 전투 탐지 3종이 각자 SerializeField로 추가됨 — 미설정 시 무증상(탐지 0) | 전투 레이어 3종 명명·번호 팀 확정 + TagManager 등재 + SystemMap §5
- **WL-006** | OPEN | PR#22 | 에셋 로딩 이원화: Resources.Load(DataTable) vs Addressables(Localization) | 로딩 전략 단일화 결정
- **WL-007** | OPEN | - | 좌표계 계약 부재: MapBuilder는 battlespace 로컬 그리드, MouseManager/Combat은 월드 좌표 — 변환 유틸 없음 | 좌표 변환 계약 정의 (배치·이동 착수 전)
- **WL-008** | OPEN | PR#32 | 로그라이크 시드 재현성: 전역 UnityEngine.Random 사용, 시드 주입 설계 없음 | Run 시드 설계 후 MapRandom에 주입
- **WL-009** | OPEN | PR#32 | 용어 충돌: StageWaypoint(블록 연결점) vs GDD 웨이포인트(병사 배치 지점), '스테이지'(블록) vs GDD 스테이지(런 단위) | 병사 시스템 착수 전 리네임
- **WL-010** | OPEN | PR#29/53 | 폴더명 오타 `Scirpts` — PR#53에서 Base/·Unit/ 하위 신규 파일로 meta GUID 참조 추가 심화 | 폴더 리네임(참조 더 늘기 전)
- **WL-011** | OPEN | PR#31 | 선택 통지 이중 경로: OnSelectionChanged 이벤트(구독자 0) vs SelectableTest→TowerInfoUI 직접 호출 | 정본 경로 결정
- **WL-012** | OPEN | - | GDD §9 미확정 항목 결합 주의: 병사/스킬 통합 여부, 몬스터 테마, 스테이지/보스 구성, 밸런싱 수치 → 관련 코드는 결합을 느슨하게 | GDD 확정 시 해제
- **WL-013** | OPEN | PR#20/22 | 표시 문자열 소유권: ResourceData.DisplayName CSV 한글 하드코딩 vs Localization String Table | UI 노출 문자열의 키 이관 방침 결정
- **WL-014** | OPEN | PR#22/29/31 | Get/Instance 계열 null 무가드 역참조 반복 (DataTableManager.Get, MouseManager.Instance 등) | 호출부 null 가드 관행 정착
- **WL-015** | OPEN | PR#46/59 | 밸런싱 수치 소유권 이원화가 타워로 확대: 건물(주민당 생산량)에 이어 타워 공격 수치(damage/range/splash/chain/버프량)가 CSV가 아니라 TowerAsset SO 인스펙터 수기 입력(TowerAsset.*.Attack). 폴리모픽 필드라 평면 CSV에 담기 어려운 구조적 이유는 있으나 계약 #2(수치=CSV)와 긴장. 값 미기입 시 #21 소비 불가 | 폴리모픽 스탯의 데이터 원본 합의 + 값 기입
- **WL-016** | OPEN | PR#46 | BuildingAsset.Data 캐시가 건물 타입당 단일 SO — 인스턴스별 레벨/주민 상태(GDD §4.2 업그레이드) 도입 시 공유 SO 덮어쓰기 위험 | 정적 조회 데이터 vs per-instance 상태 경계 확정
- **WL-017** | OPEN | PR#48/#43 | ResourceWallet 소유권: #43에서 `ManagementController`가 지갑을 소유·노출(씬 범위, `OnChanged`로 UI 갱신)하여 하네스 로컬 문제는 해소. 단 전역/씬 간 공유(다른 씬의 소비처 접근) 방식은 WL-002 수명주기와 함께 미확정 | 전역 매니저/부트스트랩 규약 확정 시 지갑 소유·노출 최종화
- **WL-018** | OPEN | PR#49/#43 | 밤→낮 전환(`DayNightManager.EndNight()`) 자동 트리거 부재 — 현재 **경영 패널(#43 `ManagementController.RequestAdvancePhase`)이 밤에 `EndNight()`를 임시로 호출**. 정식으로는 밤을 끝내는 주체(Combat 웨이브 클리어/사망 처리 등)가 책임져야 하며, 경영 패널의 임시 호출은 그때 제거·이관해야 함 | 밤 종료 주체 확정 후 `EndNight()` 자동 호출로 연결하고 경영 패널의 임시 트리거 제거
- **WL-019** | OPEN | PR#50 | Tower 페이즈 게이팅 미연결: Tower.Update()가 낮/밤 구분 없이 공격. DayNightManager(CurrentPhase/OnDayToNight/OnNightToDay) 머지로 연결 가능해짐. GDD §5.2 밤 전용 동작. | Tower가 DayNightManager 구독/조회로 밤에만 공격하도록 연결 (WL-018과 세트)
- **WL-020** | OPEN | 이슈#7 | `DayNightLightingController`가 `RenderSettings.ambientMode`(Skybox→Trilight 전환)·`RenderSettings.skybox`(프라이빗 인스턴스 교체)를 씬 전역으로 변경 — 경영/전투 공간이 한 씬에 공존(SystemMap §5)해 전투 공간 조명·스카이박스도 함께 바뀜. 팀 계약 #4(공간 분리) 관련 Combat/BattleMapBuilder 조명 전제와 충돌 가능 | Combat/BattleMapBuilder 팀과 조명 영향 범위 합의, 필요 시 공간별 분리 방안 검토
- **WL-021** | OPEN | PR#52/#43 | 경영 생산 라인이 인스펙터 `ResourceAsset[]` + 단일 전역 `_baseAmountPerVillager`로 구성됨(ManagementController.BuildModel) — `ResourceProductionSource.TryCreate(BuildingAsset)`(건물별 주민당량·훈련장 필터) 경로 미사용. 건물 배치(#27) 통합 시 라인 생성원을 배치된 건물 인스턴스로 이관 필요. WL-015(밸런싱 수치 소유권)와 연동 | 건물 배치 시스템 연결 시 라인 생성원을 BuildingAsset 경로로 교체
- **WL-022** | OPEN | PR#52/#43 | "전원 배치라야 밤 전환"(CanEndDay) 게이트가 자원 라인 배치만 셈 — GDD §6.1의 훈련장 배치처·잉여 주민 정상 플레이와 충돌 예정. 밤 전환 조건을 배치 강제와 분리 필요 | 주민/훈련장 시스템 도입 시 밤 전환 조건 재설계
- **WL-023** | OPEN | PR#54 | 카메라 제어가 Mouse/Keyboard.current 직접 폴링(CameraController) — 입력 단일 창구(§4.1)와 배치 모드 충돌 우려. 카메라 입력을 MouseManager가 소유할지/예외로 명문화할지 미정 | 카메라 입력 소유권 합의 + SystemMap §4.1 반영
- **WL-024** | OPEN | PR#54 | 줌이 Lens.OrthographicSize에 하드결합 — #28 구도(쿼터뷰=원근 가능성) 미확정 상태에서 투영 방식 변경 시 재작업 | #28 확정 후 줌 대상 격리(ApplyZoom) 또는 확정값 반영
- **WL-025** | OPEN | PR#53 | 콜라이더→IDamageable 해석·타겟 선정 방식 분기: Tower(OverlapSphere+첫매치+TryGetComponent) vs PlayerUnit/Enemy(NonAlloc+최근접+GetComponentInParent) | 신규 패턴으로 Tower 통일, 프리팹 콜라이더 배치 규약 확정
- **WL-026** | OPEN | PR#59 | GDD 미기재 타워 택소노미 신규 도입: Single/Area/Chain/Magic(Buff/Debuff) 5종, 특히 haste_tower=타워가 타워를 버프하는 지원 타워. GDD §6.2/§8·보상(§6.6)·스킬 통합(§9)과 맞물릴 설계. 데이터 결합은 느슨 | GDD 반영 + 팀 합의(WL-012 연동)
- **WL-027** | OPEN | PR#61 | 경영 공간 씬 정본 이원화: 게임 부트 씬 ManageSpace-Sungsoo.unity가 muchan/Scene/ManageSpace.unity의 복사본으로 출발했으나 main 머지로 157줄 divergence 발생(muchan 정본엔 TowerDataTest·ManageObjects 등 존재, 부트 복사본엔 없음). GameSceneManager가 복사본을 부팅해 muchan 후속 경영 씬 작업이 게임에 반영 안 됨, 25779줄 씬 재동기화 불가 | 경영 씬 정본 1개 확정 + GameSceneManager.ManageSpaceScene/EditorBuildSettings를 정본으로 지정 + 복사본 폐기