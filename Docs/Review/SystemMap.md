# SystemMap — 시스템 지도와 통합 계약 (PR 리뷰 필수 입력)

> **목적**: PR 리뷰 시 "이 변경이 누구의 어떤 시스템과 만나는가"를 판단하는 기준 문서.
> **갱신 규칙**: 시스템의 공개 API·계약이 바뀌는 PR은 이 문서를 **같은 PR에서** 갱신한다.
> 자동 리뷰 워크플로우(`.github/workflows/pr-review.yml`)가 매 리뷰마다 이 문서를 읽는다 —
> 낡은 지도는 리뷰 품질을 직접 해친다.

## 1. 시스템 및 소유자

| 시스템                                      | 소유자     | 경로                                                                 | 상태                                                                                                                                                                    |
| ------------------------------------------- | ---------- | -------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| DataTable (CSV→static 레지스트리→SO)        | muchan     | `Assets/Scripts/Data`                                             | Resource, Building, Tower, Enemy 4종 구현. Tower/Enemy는 Combat(`Tower.cs`/`Enemy.cs`)이 `TowerAsset`/`EnemyAsset`을 직접 소비하도록 이관 완료(PR#80) — 잔여 종류 값 채움 + Soldier 이관은 진행 중(WL-001, 부분 착수). Territory/Reward 확장 예정. **Skill(#103)은 CSV 파이프라인을 쓰지 않기로 확정** — 밸런싱 수치 미정 + 스킬 1~2개뿐이라 과설계로 판단, `PlayerSkill` 시스템 행 참고                                |
| Combat (타워/몬스터 공격·데미지)            | SUNGSOO    | `Assets/Scripts/CombatSystem` | 공격/데미지 코어만. 이동·사망처리·투사체 없음. HP 조회 공개 API(`CurrentHp`/`MaxHp`/`OnHpChanged`) + `PlayerBase` 씬 싱글톤(`Instance`/`OnBaseSpawned`) 추가(#100, HP UI 연동용). `Tower.cs`에 PlayerSkill(#103, muchan)이 버프 배율과 자가 등록 정적 리스트 `Tower.Active`를 추가함. **#164 리팩토링으로 Tower.cs가 재구성됨(n0wst4ndup)**: 구 `AuraTower`를 폐기하고 **타워를 단일 `Tower` 타입 + 행동 조립(`ITowerBehaviour`)** 구조로 통합. 공격 로직은 `AttackBehaviour`로, 오라는 `BuffAuraBehaviour`/`DebuffAuraBehaviour`로 이관. 버프 배율 필드(`damageMultiplier`/`attackSpeedMultiplier`)와 `activeBuffs`는 스탯 원장 `TowerStats`로 통합(타일 버프까지 흡수). `Tower`가 소유하는 것은 정체성(SO/진영)·원장·선택 표현·레지스트리·게이팅뿐이며 "무엇을 하는 물건인지"는 전부 행동이 가진다. 공개 API 상세는 2절 참고. `Projectile.cs`에 PlayerSkill(#169, muchan)이 static 명중 이벤트 `Projectile.DamageDealt(IAttacker, IDamageable)` 추가(단일/스플래시/체인 데미지 4지점 직후 발행, 순수 추가 — 기존 로직 무수정. static이라 구독 해제는 구독자 책임, 현재 구독자는 `BurnBuff`). **`Tower.cs`에 TowerFusion(#195, muchan)이 읽기 접근자 `Asset`(=data) 추가 — 순수 읽기(배치된 타워의 원본 SO 조회, 합성 재료 TowerID 매칭용), 기존 로직·필드 무수정** |
| BattleMapBuilder (절차적 전투 맵)           | SUNJIN     | `Assets/Scripts/CombatSpace/MapBuilder`                          | 7×7 블록 경로 생성 구현. 싸이클 버그 해결이 다음 빌드 목표      
| MonsterMovement (지상/공중 경로 이동)       | SUNJIN     | `Assets/Scripts/Monster/MonsterMoveMent`, `Assets/Scripts/CombatSystem/IMovementAgent.cs` | `IMovementAgent`에서 경로 추종 계약을 `IRouteMovementAgent`로 분리. 지상은 `MonsterMove`, 공중은 `FlyingMonsterMove`가 구현한다. 공중 이동은 기존 경로를 일정 간격으로 샘플링하고 고도 오프셋을 적용해 선택된 지점 사이를 직선 비행한다. 이동속도 다축 합성은 순수 C# `MoveSpeedComposer` 한 곳에서 계산하며 두 이동 컴포넌트가 위임한다. `Enemy`·`MonsterSpawn`·`MonsterStateMachine`은 구체 이동 타입이 아니라 인터페이스를 소비한다(#209). |
| MouseManager (입력/선택/배치)               | n0wst4ndup | `Assets/Scripts/GameManager/MouseManager`                            | 4상태 머신 구현(Idle/BoxSelect/Placement/SkillTargeting — #103 SkillTargeting, #261 BoxSelect 추가). Snap 항등·CanPlaceAt 항상 true (TODO). 스킬 타겟팅은 전투 타일 전체 허용(`CombatMapTileView` 유무 질의, 도로 전용 제한 제거). 단일 선택 확정은 press가 아니라 **release** 시점(#261)                                                                                                                  |
| TowerVfx (타워 등장·합성 소모 연출, #264/#265) | n0wst4ndup | `Assets/Scripts/CombatSystem/Vfx` | **등장(`TowerSpawnEffect`)**: 배치 확정 시 입자 수렴 → 타워 등장 팝 → 바닥 링. **합성 소모(`TowerMergeDissolveEffect`, #265)**: 재료가 하얘지며 중심으로 수축 → 입자로 폭발 → 재료 자리 상공 부유(배치 대기 동안) → 확정이면 결과 타워로 수렴 / 취소면 제자리 역수렴 + 재조립 팝. 두 연출은 부품(`GrainSwarm` 알갱이, `VfxScaleHold` 스케일 점유)과 **시간 축**(`ConvergeDuration`·`PopDuration`)을 공유한다 — 유입 입자가 등장 팝보다 늦게 도착하면 "재료가 모여 타워가 됐다"가 성립하지 않기 때문. 합성 연출은 커맨드(#263)가 재료를 즉시 비활성화하므로 **소모 직전에 뜬 시각 사본**으로 독립 재생한다(로직 무결합, 연출이 죽어도 합성은 멀쩡). 이하는 등장 연출 기준: **대상을 모르는 범용 연출** — `Transform` + 풋프린트 크기만 받고 `Renderer.bounds`/`localScale`만 읽어, 타워 에셋이 교체돼도 코드 수정이 필요 없다(에셋 교체 회귀 검증 통과). 수치 앵커는 둘로 나뉜다: **크기·바닥 링은 풋프린트(논리, 에셋 무관 불변) / 개수·구름 모양은 bounds(시각)**. ⚠ **재생 중 대상 루트 `localScale`을 배타적으로 소유**하므로 그 창(약 0.45초 + 과도기 0.28초) 동안 스케일을 쓰거나 캡처하는 시스템이 있으면 깨진다 — 이 계약 때문에 `RangeCircle`의 보정 시점이 함께 바뀌었다(2절). 시간은 `unscaledDeltaTime`(일시정지 중 타워가 투명하게 멈추는 것 방지, WL-100). **룩·수치는 전부 임시(아트 TBD)** — 타워 에셋이 임시이고 아트 방향이 미정이라, 검증 통과와 설계 확정은 별개다. 명세 `Docs/Core/TowerPlacement.md` §9.3, `Docs/Core/TowerMerge.md` §9.2 |
| PlayerSkill (플레이어 스킬, #103)           | muchan     | `Assets/Scripts/Skill`                                                | 클릭 시전 감전 스킬(기본 스킬 1종). 밤 게이팅(`Tower.cs`와 동일하게 `DayNightManager.CurrentPhase` 직접 폴링)·쿨다운·범위 데미지(`IDamageable`/`DamageInfo` 재사용, 새 데미지 경로 없음). 수치는 CSV가 아니라 `SkillManager` 인스펙터 직접 입력(WL-015와 같은 축). **버프 스킬 구현 완료**(2번째 스킬, `BuffSkillManager`) — 타겟팅 없이 클릭 즉시 발동, `Tower.Active` 순회해 씬의 모든 Tower에 공격력/공격속도 배율을 일정 시간 부여(`Tower.ApplyBuff`). **#164 리팩토링 후**: 모든 타워가 단일 `Tower` 타입이라 오라 타워도 순회 대상에 들어오지만, 공격 행동이 없어 공격력/공속 modifier가 아무 효과를 내지 않는다(원장에 죽은 항목만 남음 — 무해). 오라 타워를 명시적으로 제외하려면 `tower.Has<AttackBehaviour>()` 게이팅을 넣으면 되나, "버프 스킬이 오라 타워도 강화해야 하는가"는 미결 기획 질문이라 현행 유지(muchan 소유). 보상 기반 특수효과 업그레이드(#169, 레벨 중첩) 진행 중 — **이벤트 구독 구조**(이슈 원문의 "enum+중앙 컨트롤러" 방침에서 변경): `SkillManager`가 임팩트마다 `ImpactResolved(SkillCastContext)` 이벤트 발행(효과 존재 모름, 구독자 0이면 기본 감전만). 특수효과는 추상 `SkillEffect`(MonoBehaviour, `SkillEffectManager` 오브젝트에 부착) 파생 — 레벨 0→1 시 스스로 이벤트 구독, 재선택은 `Level` 변수만 가산, 파괴 시 해제. `SkillEffectManager`는 라우터로 축소(`ApplyReward`→타입 매칭 효과에 위임, `GetLevel` 조회). `SkillCastContext`(착탄 위치·맞은 적 버퍼·`ExtraImpacts` 가산 필드)로 시전 계열 효과(추가시전: `ImpactIndex==0` 재귀 가드)까지 같은 이벤트로 수용. **화상(`BurnEffect`) 구현 완료** — 대상의 `StatusEffectHandler.ApplyOrRefresh` 재사용(AuraTower 패턴, effectId=`"skill_burn"` 해시, Combat 무수정), 틱 데미지 = 레벨 × 인스펙터 수치. **폭탄(`BombEffect`+`SkillBomb`) 구현 완료** — 착탄 지점에 `Assets/Prefabs/Skill/SkillBomb.prefab` 설치 → 지연 후 반경 폭발(OverlapSphere, 감전과 동일 LayerMask/DamageInfo 규약), 폭발 데미지 = 레벨 × 인스펙터 수치. **추가시전(`CountEffect`) 구현 완료** — 총 발동 = 1+레벨, 반복분은 UniTask로 `repeatInterval`(기본 0.5s) 간격 발동(`ImpactIndex==0` 재귀 가드, 반복분에서도 화상·폭탄 정상 발동). **버프 화상(`BurnBuff`) 구현 완료** — `SkillEffect`의 구독 대상이 가상화됨(`TrySubscribe`/`Unsubscribe` override, 기본은 감전): BurnBuff는 `BuffSkillManager.BuffResolved(BuffCastContext)`에 구독, 버프 지속시간 창 동안 `Projectile.DamageDealt`를 구독해 타워 투사체에 명중당한 적에게 화상(effectId=`"buff_burn"`, 재시전 시 창 연장, 창 밖 구독 해제). 새 효과 추가 = `SkillEffect` 파생 1개 + 씬 컴포넌트 부착(스킬·매니저 무수정). **웨이브 종료 취소(#200)**: `SkillManager`(추가시전 반복분)·`SkillBomb`(지연 폭탄)이 `DayNightManager.OnNightToDay` 구독 → 밤→낮 시 진행 중 효과 취소(낮 잔존 발동 방지). 조준 모드 취소는 `PhasePanelSwitcher`가 `OnDayStart`에서 담당(기존). **마법 연구소 기본 스탯 배율 강화 구현 완료(#205)** — `SkillManager`/`BuffSkillManager`가 각자 `magic_lab` `BuildingAsset` 참조 + `ManagementController.GetUpgradeLevel`로 레벨을 pull, 레벨→배율 매핑은 `BuildingAsset.Skill.UpgradeLevels`(SO, 도달 비용과 같은 리스트, WL-015와 같은 축)에 authoring — 씬에는 배율 데이터가 없어 밸런싱이 `GameScene.unity`를 안 건드린다(PR#216 리뷰 반영). 시전 시점 base damage/radius/cooldown·버프 배율/지속시간/쿨다운에 배율로 적용 — 보상 기반 특수효과(`SkillEffect.Level`, 위) 축과는 완전히 독립, 이벤트 구독 흐름 무수정(`PlayerSkill.md` §3.1) |
| WaveReward (웨이브 클리어 3택1 보상)        | SUNJIN     | `Assets/Scripts/Reward`                                               | 3택1 선택 UI(`WaveRewardSelectionUI`, timeScale 0 정지 + UniTask 대기)·랜덤 추출(`WaveRewardPool`)·웨이브 클리어 트리거(`WaveCompletionCoordinator`) 배선 완료(#132/#133, PR#150). `WaveRewardController.GrantReward`는 로그 + `SkillEffectManager.ApplyReward` 호출(#169 1단계, 매니저 없어도 동작). `WaveRewardType`(Burn/Bomb/Count/BuffBurn — 전부 스킬 특수효과, 임시 슬롯 소진)별로 매니저가 레벨 누적. 타입 확정·`NorthLand_Rewards` 로컬라이즈 키 정리는 #169 후속 단계(WL-043) |
| Localization                                | n0wst4ndup | `Assets/Scripts/Localization/LocalizationHelper.cs`, `Assets/Localization/*`(String Table 컬렉션), `Assets/Scripts/Test/LocalizationTest.cs` | String Table 4종(`NorthLand_default`/`NorthLand_buildings`/`NorthLand_Enemies`/`NorthLand_Towers`, ko-KR/en-US/ja-JP) 구축. Building/Enemy/Resource/Tower CSV 표시 문자열은 키로 이관 완료(WL-013 해소, PR#126 — 신규 `poison_tower` 행 포함). `LocalizationHelper`(static 동기 pull 헬퍼) 신설 — 호버 툴팁 등 '호출 시점 1회' 풀 경로 전용, 지속형 표시는 `LocalizeStringEvent`/`LocalizedString.StringChanged` 사용. 전투 공간(TowerInfoUI) 표시 배선은 후속(#102) |
| DayNightManager (낮/밤 상태·전환 이벤트 훅) | muchan     | `Assets/Scripts/DayNight`                                    | 상태 관리 + 전환 이벤트 훅 구현. 자원 정산/주민 배치 초기화는 `Management(Resource)`가 구현(#66), 본진 회복은 미구현(소유 시스템 대기). 밤→낮 트리거는 임시 UI(`NightActionPanelView`의 "웨이브 성공" 버튼, #66)가 `EndNight()` 직접 호출(웨이브 클리어 로직으로 교체 예정, WL-018) |
| DayNightLighting (낮/밤 전환 조명·스카이박스 연출, #7) | muchan     | `Assets/Scripts/DayNight`                                    | `OnDayToNight`/`OnNightToDay` 구독해 Directional Light·Ambient(Trilight)·Skybox를 즉시 전환(스냅). 부드러운 Lerp 전환은 미구현 — 밤 종료 자동화의 UniTask 전환 작업과 함께 후속 예정 |
| Management(Resource) (자원 지갑·생산처)     | n0wst4ndup | `Assets/Scripts/ManagementSpace`                              | 지갑·생산처(#42) + 경영 패널 UI·DayNightManager 낮/밤 루프 연동(#43, #66). 정산+주민 배치 초기화=OnNightToDay(정산 먼저). **밤→낮 전환은 이제 밤 전용 임시 UI(`NightActionPanelView`)의 "웨이브 성공" 버튼이 트리거(WL-018)** — 경영 패널(`RequestAdvancePhase`)은 낮→밤(`EndDay`)만 담당. 주민 수는 placeholder(주민 시스템 부재). 소비처·마나석 생산 후속. **✅ 확장 자원 라인 구현(#166)**: 미개척 영지(영토 해금) = 특수 자원(금/루비/사파이어/다이아) **매일 자동 수급** — `HandleNightToDay`가 Owned 노드에서 `SupplyDaily`만큼 `Add`(주민 배치 무관). 패널은 **고정 8행**(기본3+마나+특수4, 동적 등록 아님): 특수/마나는 +/- 숨김, 특수는 "+n"(일일 수급)·**미개방 시 회색**·활성 우선 재정렬, 마나 "+n"=`ManaPerWaveClear`. `ProductionLineView`에 Villager/Supply/Mana 모드. **지갑(보유량) 표기를 탑 바 → 각 행의 지갑 칸(`_balanceText`→ProdRow Wallet)으로 이관**(#166): 탑 바 `Wood/Iron/Food/Mana_hud` 비활성화, 주민 풀·페이즈만 탑 바 유지. **🔀 잔여 방향**: ②생산 건물 3종 업그레이드(#139 구현됨), ③탑 바 HUD 오브젝트 완전 삭제는 후속. **✅ 마법 연구소 업그레이드**: 생산 라인과 별개인 **업그레이드 전용 건물 트랙**(`_upgradeBuildings`)으로 구현 — 마나석 비용·레벨 추적 + 강화 효과(스킬 시스템이 `GetUpgradeLevel`로 레벨 참조, 결합도 최소)도 **구현 완료(#205)**. BuildingUpgrade.md §8 |
| TerritoryGraph (경영 영토 확장)             | n0wst4ndup (View 비주얼: muchan) | `Assets/Scripts/ManagementSpace/Territory`                    | 그래프 생성(Delaunay+프루닝)·클레임(`ISelectable`)·점진 공개·호버 하이라이트(`IHoverable`) 구현, `GameScene`에 씬 통합 완료(#18, #67). 하루 1회 확장 게이팅(`HasExpandedToday`, `DayNightManager.OnDayStart` 연동)도 #67에서 추가. **노드 비주얼 에셋 적용(#127, PR#128, muchan)**: `TerritoryNodeStateVisual`(상태→비주얼 스왑: Selectable=절차 생성 소용돌이 `VortexVisual`, Owned=산 에셋, 본진=씬 지형)+확보 연출(UniTask). `GameScene`의 `TerritoryGraphView._nodePrefab`=`TerritoryNodeV2`(간격 튜닝 세트와 결합 — WL-059). 구형 프리팹은 기존 색상 경로 폴백. **엣지 배 연출(#93, muchan)**: 엣지 선(LineRenderer)을 `SweetBoat` 랜덤 1척이 왕복하는 `TerritoryEdgeShip` 연출로 교체(선은 `_drawEdgeLines` 기본 꺼짐), 양끝이 모두 `Owned`일 때만 표시(`TerritoryGraph.IsOwned`). **🔀 영토 = 미개척 영지 자원 재설계 완료(#166)**: 효과 SO 계층(`TerritoryEffect`/`Grant`/`GainResident`/`ProductionMultiplier`/`Context`)을 **폐기·삭제**하고 `TerritoryDefinition`을 "자원 영지 정의"(`Kind`/`IslandPrefab`/`Min·MaxDaily`)로 리셰이프. 노드는 주입 시점에 `DailyYield`를 [Min,Max]에서 1회 롤(`TerritoryNode.DailyYield`, 시드 결정성). **확보 즉시 지급은 없고**, 매일 정산 시 `ManagementController`가 Owned 노드에서 자동 수급(GDD §3.2·§5.3). 섬 프리팹도 SO 소유로 이관(`TerritoryNodeStateVisual._mountainPrefabs`는 폴백만). `OnNodeClaimed` 훅은 뷰 확보 연출용으로 잔존하나 자원 적용엔 더 이상 안 씀(WL-030 종결) |
| TowerFusion (타워 합성/Merge, #194/#195/#183)          | muchan(데이터·실행) · n0wst4ndup(선택·패널 #183) | `Assets/Scripts/GameManager/MouseManager/TowerPlacement`(Wallet/Matcher/Controller), `Assets/Scripts/Data/Tower/TowerRecipe.cs` | 레시피 SO(`TowerRecipe`: 재료 TowerID별 개수→결과 `TowerAsset`+`ExtraCost`, CSV 미경유 인스펙터 손입력) + 포함 매칭(`TowerFusionMatcher`, 순수 함수) + `TowerPlacer` 재사용 배치. **후보 버튼 클릭 즉시 재료를 소프트 소모**(타일 `Release`+비활성화)하고 결과 고스트 배치 → 확정 시 `ExtraCost` 지불+재료 진짜 파괴, 취소 시 재료 원복(#263 커맨드 패턴, `IReversibleCommand`/`TowerMergeCommand`). **재료가 점유했던 타일에 결과를 놓을 수 있다** — 구 "확정 시점 소모" 때의 제약이 여기서 풀렸다(WL-077 후단). 결과=일반 `TowerAsset`(신규 CSV 행/SO). 타일 점유는 `TowerFootprint`(배치 인스턴스에 부착)가 소유하며 `OnDestroy`(파괴)와 `Release`/`Reoccupy`(임시 해제) 두 경로로 되돌린다. **선택/패널 UI(#183)는 명세 확정·구현 예정**: 코디네이터+마커(`IGroupSelectable`)로 멀티 선택(MouseManager 제네릭 유지), **집합=`TowerWallet` 단일 백킹 스토어**(이음매), 패널 스위처가 우측 패널 단일 권위(1개=`TowerInfoUI`/2개↑=합성 패널), 후보 버튼 활성=`TowerFusionMatcher.CanFuse`, 우클릭 해제 없음(Esc/빈곳만, WL-073), 밤 전환 시 진행 중 배치까지 취소, **낮 전용**. 현재는 임시 `TowerWallet`(씬 타워 인스펙터 드래그)가 선택셋 스탠드인. **⚠ 네이밍**: 문서·기획=합성/Merge, 코드 접두=`Fusion`(리네임 별건). 단일 진실 원천: `Docs/Core/TowerMerge.md`(구 `TowerFusion.md` 폐기·이관 완료) |
| BossAI (보스 BT 패턴 AI, #232/#233/#234/#235) | n0wst4ndup | `Assets/Scripts/CombatSystem/Enemy/AI`(`EnemyAgent`/`EnemyPatternMemory`/`EnemyNodeQuery`/열거형), `Assets/Scripts/CombatSystem/Enemy/AI/Nodes`(리프 노드 14종), `Assets/Behavior/TankBossBehavior.asset`(그래프), `Assets/Prefabs/Monster/Tank.prefab`, `Assets/Resources/ScriptableObjects/Enemies/tank.asset` | 기반(#233)·리프 노드 세트(#234)·패턴 그래프(#235) 구현 완료. **패턴 4종 + 기본 진군이 Play에서 동작 확인됨** — P2(뒤쪽 잡몹→크롤+피해감소, 조건 해제 시 복귀) / P3(앞쪽 타워+잡몹→타워 `AttackInterval` 1→2) / P1 / P4 / 감속 파훼(`AddSpeedDebuff` 대체 검증: 감속 2중첩으로 충돌 피해 0). **잔여 2건**: ① 보스 몸체가 캡슐이고 `AnimatorController` 미착수 — P1 준비 모션이 그래프에서 빠져 있다(`EnemyPlayAnimationAction`은 `Animator` 없으면 Failure) ② P1 충돌 후 보스 생존(경로 끝 `RouteCompleted → Destroy` 회피) 미검증. 수치는 전부 placeholder. 패턴 수치는 그래프 Blackboard 변수 37개로 authoring(WL-094 해소). 보스는 `EnemyTable.csv`에 `tank` 행으로 등재돼 CSV 파이프라인 안에 있다(importer는 기존 SO의 `EnemyID`/`EnemyType`만 덮어써 손입력 `Boss.Stat`·`BehaviorTree`를 보존한다 — `TableImporter.ImportEnemy`). `EnemyAgent.unitLayerMask`는 896(Enemy 7 \| Soldier 8 \| PlayerBase 9) — 이 마스크는 "질의 후보 집합"이고 진영 판정은 `EnemyNodeQuery.TryAccept`가 사후에 하므로 넓게 잡는 것이 계약과 일치한다(부분적으로 비면 `Hostile` 조건이 조용히 항상 0). **감속 파훼 불변식**(`MoveSpeed × MaxFactor × slow^n < MinSpeed`)이 수치 튜닝으로 깨졌다 복원된 이력이 있다(WL-122) — 밸런싱 시 `TankGraphSpec.md` 「감속 파훼 불변식」 표를 재계산할 것. `EnemyAgent`는 `Enemy`를 상속하지 않고 **병존**하는 무상태 파사드로, 값은 `MonsterMove`/`Enemy`가 소유하고 전달만 한다(유일한 예외는 패턴 쿨다운 기록). 노드는 `Enemy`/`MonsterMove`/`Animator`에 직접 닿지 않고 `EnemyAgent` 경계만 안다. **네임스페이스를 두지 않는 규약**이라 노드·보조 타입 클래스 이름이 전역 유일해야 한다(기존 MiniBoss 노드 4종은 `NorthLand.Combat.Boss`를 쓰며 이 세트와 무관·GUID 충돌 없음). 수치는 코드가 아니라 그래프 Blackboard 변수로 authoring한다(WL-094와 같은 축). 단 **`LayerMask`는 Blackboard 지원 타입이 아니라** `EnemyAgent.UnitLayerMask`(프리팹 인스펙터)에 둔다. **타워 접점(#164 리팩토링 반영 완료)**: P3 마력 봉인의 대상 집합은 `EnemyNodeQuery.IsAttackTower` = `Tower.Has<AttackBehaviour>()`로 판정한다. 모든 타워가 단일 `Tower` 타입이 된 뒤 **이 판정이 처음으로 실제 필터 역할을 한다**(예전엔 오라 타워가 별개 클래스라 `Tower.Active`에 없어서 자동으로 빠졌고, 그 뒤엔 `AttackInterval > 0` 휴리스틱이었다). 능력 질의로 바꿔 판정 근거가 다른 시스템의 구현 세부에 의존하지 않는다. 지키는 설계 의도: **"봉인 중에도 감속은 살아남아 P1 파훼 수단이 유지된다."** 편집모드 실측으로 `choco_tower`(Magic/Debuff)가 `IsAttackTower=false`임을 확인(2026-07-29). **감속 중첩 해소 + 밸런스 미결**: 감속 소스키가 인스턴스별로 바뀌어 같은 종류 감속 타워가 실제로 중첩되기 시작했다(구 `TowerID` 해시에선 1중첩에 고정 — **P1 파훼가 원천 불가**였다). 이후 `choco_tower` 감속을 −40%→**−20%(배율 0.8)** 로 조정(2026-07-29, n0wst4ndup) → `MoveSpeedComposer` 실측 `84 × 0.8ⁿ`: 5중첩 27.53(`MinSpeed 25` 초과) / **6중첩 22.02(피해 0)**. **파훼에 감속 타워 6개가 필요해 프로토타입 기준 과할 수 있다** — 조정 후보(감속 −30% 강화 / `P1_MinSpeed` 상향 / 합산 중첩 전환)와 권고는 `TankGraphSpec.md` 「감속 파훼 불변식」 절에 정리. 인게임 검증 미완. 보스 이름은 프로토타입 임시명 `Tank`이며 웨이브 편성은 프로토타입에서 조정하지 않는다(임의 웨이브에 `Count: 1`, WL-096은 이 이슈 범위 밖). 설계 `Docs/Monster/Boss/BossDesign.md` · 노드 대장 `Docs/Monster/Boss/BossNodeReference.md` |

## 2. 공개 API (다른 시스템이 소비해도 되는 것)

- `DataTableManager.Get<T>(string id)` — static. **null 반환 가능 → 호출부 null 체크 필수**
- `ResourceTable.Get(string id)` — null 반환 가능
- `BuildingTable.Get(string id)` — null 반환 가능
- `TowerTable.Get(string id)` — null 반환 가능. `TowerAsset`은 Combat의 `Tower.cs`가 직접 소비함
  (PR#80으로 이관 완료) — 잔여 타워 종류의 값 채움은 WL-001 참고
- `EnemyTable.Get(string id)` — null 반환 가능. **스탯(체력/이동속도/공격력/사거리/공격주기)은
  CSV/`EnemyData`에 없음** — `EnemyAsset`(SO)의 `EnemyType`별 필드 그룹(`MeleeFields`/`RangedFields`/
  `BossFields`, `TowerAsset`의 `TowerType`별 필드 그룹과 동일 패턴)에만 존재. 이슈#26 원 스펙(스탯
  CSV 컬럼 요구)과 다른 선택이며 WL-027로 추적 중 — 스탯이 필요한 소비처(#14/#15/#16)는
  `EnemyTable.Get(id)`가 아니라 `EnemyAsset` 조회 경로를 써야 함. `EnemyAsset`도 `TowerAsset`과
  동일하게 Combat의 `Enemy.cs`가 직접 소비함(PR#80으로 이관 완료, 옛 Combat 자체 `EnemyData`는
  삭제됨) — 잔여 종류 값 채움은 WL-001 참고. `BossFields.BehaviorTree`는 실제 BT 에셋 타입 미정 상태의 placeholder 필드
- `ResourceAsset.Data` / `BuildingAsset.Data` / `TowerAsset.Data` / `EnemyAsset.Data` — **호출부가
  Start()에서 직접 채우는 규약** (저장 안 됨)
- `BuildingInfoUI.Instance.ShowInfo(BuildingAsset)` / `HideInfo()` — 경영 공간 전용 정보 패널. `TowerInfoUI`와
  동일 구조의 별도 씬 싱글톤 (공간 분리 계약상 Combat의 `TowerInfoUI`와 공유하지 않음)
- `StorePanelUI.Instance.Show(BuildingAsset)` / `Hide()` — 교환 상점 패널(#211, 연금술사의 집). `BuildingInfoUI`와
  **같은 계보의 별도 씬 싱글톤**(정보 표시가 아니라 행마다 액션 버튼이 있는 목록이라 갱신 방식이 다르다 —
  행을 한 번만 만들고 `OnChanged`마다 `interactable`만 토글한다. 매번 재생성하면 클릭을 처리하는 도중 그 버튼이 파괴된다).
  둘 중 어느 패널을 열지는 **`BuildingInfo.OnSelected`가 `Exchange.Offers` 유무로 분기**한다 — `BuildingType`으로 분기하지
  않는다(타입은 인스펙터 authoring 분류일 뿐, 동작 게이트는 '데이터 존재'로 건다는 기존 컨벤션)
- `LocalizationHelper.Get(table, entry)` — static 동기 조회(현재 로케일). **풀(pull) 경로 전용** —
  호버 툴팁 등 호출 시점 1회 값이 필요한 경우만. 지속형 표시(상세 패널 등, 로케일 변경 시 자동 갱신
  필요)는 `LocalizeStringEvent`/`LocalizedString.StringChanged`를 쓴다. 테이블명 상수
  `k_DefaultTable`/`k_BuildingsTable`/`k_TowersTable`/`k_EnemiesTable` 제공 — **컬렉션명은 대소문자
  구분**이라 실제 컬렉션명(`NorthLand_Towers`/`NorthLand_Enemies`, 대문자)과 정확히 일치해야 함(PR#126에서 정정, WL-060)
- `IDamageable { Faction, IsDead, TakeDamage(DamageInfo) }`, `IAttacker`, `DamageInfo`,
  `Faction { Player, Enemy }` — namespace `NorthLand.Combat`
- `Enemy.CurrentHp` / `MaxHp` / `event Action<float,float> OnHpChanged`, `PlayerBase.CurrentHp` /
  `MaxHp` / `event OnHpChanged` / `static Instance` / `static event Action<PlayerBase> OnBaseSpawned`
  — HP UI(`Assets/Scripts/UI/HealthUI`, #100)가 구독하는 공개 계약. `PlayerBase.Instance`는 성문
  (BaseGate) 런타임 스폰 시점(`MonsterSpawn.UpdateGate`)에 설정됨 — `TowerInfoUI`/`DayNightManager`와
  동일한 씬 싱글톤 계보
- **`IMovementAgent` 이동속도 다축 합성 계약(#233/#209)** —
  `bool IsStopped { get; set; }`, `void SetMoveSpeed(float moveSpeed)`,
  `float EffectiveMoveSpeed { get; }`, `float PatternSpeedFactor { get; set; }`,
  `void AddSpeedDebuff(int sourceId, float factor)`,
  `void RemoveSpeedDebuff(int sourceId)`. namespace `NorthLand.Combat`.
  구현체는 `MonsterMove`와 `FlyingMonsterMove`이며, 구체 타입이 아니라 이 인터페이스로 소비한다.
  최종 속도는 순수 C# `MoveSpeedComposer`가 한 곳에서
  `기준 속도(SetMoveSpeed) × 패턴 배수 × Π 디버프 배수`로 계산하고
  `minMoveSpeed`(기본 0.15) 하한을 적용한다.
  이동속도 감소 타워와 보스 BT가 소비하는 공통 창구이며, 패턴 축과 디버프 축이 분리되어
  서로의 값을 덮어쓰지 않는다. 디버프는 소스별 곱산 중첩이고 같은 `sourceId`는 갱신만 한다.
  **완전 정지는 속도 배수가 아니라 `IsStopped`로 표현한다** — 하한 클램프가 있어
  배수를 0으로 설정해도 완전히 정지하지 않는다.
- **`IRouteMovementAgent : IMovementAgent` 경로 이동 계약(#209)** —
  `bool HasRouteRemaining { get; }`, `event Action RouteCompleted`,
  `void SetRoute(IReadOnlyList<Vector3> routePoints)`,
  `void SetMoveEnabled(bool enabled)`.
  구현체는 지상 이동 `MonsterMove`와 공중 이동 `FlyingMonsterMove`.
  `MonsterSpawn`은 스폰 시 이 인터페이스를 찾아 경로를 주입하며,
  `Enemy`와 `MonsterStateMachine`도 같은 인터페이스로 정지·이동·경로 완료를 제어한다.
  `FlyingMonsterMove`는 전달받은 지상 경로에서 일정 간격의 지점을 샘플링하고
  고도 오프셋을 적용해 지점 사이를 직선으로 이동한다. 마지막 경로 지점은
  샘플링 간격과 관계없이 반드시 포함한다.
- **`Enemy.MovementMode` 공중/지상 런타임 판별 창구(#209)** —
  `EnemyAsset.MovementMode`를 읽는 공개 접근자이며 데이터가 없으면 `Ground`를 반환한다.
  타워의 후속 안티에어 타겟팅 등 외부 시스템은 이동 컴포넌트 타입을 직접 검사하지 않고
  이 접근자를 사용한다. `MonsterSpawn.SpawnPrefab`은 `MovementMode.Flying`인데
  `FlyingMonsterMove`가 없는 프리팹을 오류로 처리하고 제거해 데이터와 실제 이동 구현의
  불일치를 스폰 시점에 차단한다. 공중/지상 분리를 위한 별도 Unity 레이어는 사용하지 않는다.
- `Enemy.MovementOwnedByBehavior` (bool, #233) — BT 이동 소유권. 켜져 있는 동안 `Enemy.Update`가
  `movement.IsStopped`를 건드리지 않고, `monsterStateMachine.SetHasTarget(false)`를 매 프레임 내려준다.
  타겟 통지를 다루는 이유: `MonsterStateMachine`이 Attack 상태에서 `SetMoveEnabled(false)`를 걸어
  (`MonsterStateMachine.cs:141`) 돌진이 본진 사거리 진입 시 멈추기 때문. **통지를 막는 것만으로는
부족하다 — `IRouteMovementAgent` 구현체(`MonsterMove`/`FlyingMonsterMove`)는 `IsStopped`와
`SetMoveEnabled`가 제어하는 내부 이동 허용 게이트가 독립이라, 소유권 획득 전 Attack 상태였다면
내부 이동 허용 값이 `false`로 남아 돌진 노드가 몬스터를 움직일 수 없다.
  부수 효과로 소유권 중에는 근접 평타가 나가지 않는다. **켠 쪽이 반드시 반납할 책임을 진다**
- `Enemy.DamageTakenFactor` (float, #233) — 받는 피해 배수. `TakeDamage` 한 곳에서만 적용된다.
  0 미만은 클램프(0=무적), 상한 없음(1 초과=취약)
- `Enemy.SetSpeedMultiplier(float)` / `Enemy.SpeedMultiplier` — #233 이후 `movement`의 **패턴 축 위임**.
 값 소유자는 현재 이동 구현체가 위임하는 `MoveSpeedComposer`이며 `Enemy`는 로컬 필드를 들지 않는다. 중간보스 그래프
  (`MidBossBehavior.asset`)가 쓰는 진입점이라 시그니처를 유지했다. 신규 노드는 `EnemyAgent`를 경유할 것
- `MonsterSpawn.SpawnMonster(GameObject prefab)` / `MonsterSpawn.AliveMonsterCount` (#233) —
  런타임 소환 창구. 웨이브 스폰과 같은 경로를 타 소환체도 `monsterParent` 자식으로 들어가고 경로를 받는다
  (웨이브 클리어 판정이 `monsterParent.childCount == 0`이라 밖에 두면 보스 사망 즉시 웨이브가 끝난다).
  **정적 싱글톤을 노출하지 않는다** — 스포너 다중 구성을 막지 않기 위해, 소환체는 스폰 시점에
  `EnemyAgent.BindSpawner`로 자기를 만든 스포너에 묶인다(경로 주입과 같은 자리).
  ⚠ `AliveMonsterCount`는 `childCount`라 **보스 자신과 사망 연출 중인 몬스터(`destroyDelay` 2초)를 포함**한다
- `EnemyAgent` (#233, 네임스페이스 없음) — 보스 BT 리프 노드가 참조하는 **유일한** 컴포넌트.
  `Enemy`와 병존하는 무상태 파사드. 노출 멤버 전체 목록은 `Docs/Monster/Boss/BossNodeReference.md`
  「EnemyAgent가 노출하는 것」. 잡몹 프리팹에 이 컴포넌트만 추가하면 같은 노드를 재사용할 수 있고,
  보스별 고유 능력은 이 클래스를 상속한 파생 컴포넌트로 얹는다(노드 입력 타입이 `EnemyAgent`라 그대로 들어간다)
- `ResourceWallet` (경영 자원 상태 저장소, 순수 C#) — `Get(ResourceKind)`, `CanAfford(kind, amount)`,
  `Add(kind, amount)`, `bool TrySpend(kind, amount)`(부족 시 false+로그, 차감 안 함),
  `event Action<ResourceKind,int> OnChanged`(종류, 변경 후 값). 자원 획득/차감은 이 창구로만(팀 계약 #3·#6)
- `ResourceProductionSource` (건물 생산 단위, 순수 C#) — `int CalculateAmount(villagerCount, amountPerVillager, mult)`(순수),
  `int Produce(villagerCount, amountPerVillager, mult)`(정산: 지갑에 Add, 넣은 양 반환), `static bool TryCreate(BuildingAsset, ResourceWallet, out)`(OutputResource만 캡처).
  **주민 수·주민당 생산량을 인자로 받는 무상태 심**(주민당량은 건물 업그레이드로 가변 — #139; readonly 필드 제거). 정산 트리거는 이제 `ManagementController`가 DayNightManager 이벤트로 호출. `OutputResource.Data.Kind`로 지갑 키 해석(→ Data 채움 규약 의존)
- `ManagementController` (경영 로직/모델, MonoBehaviour) — 지갑·생산처·주민 배치·업그레이드 상태 소유. `AssignVillager(int)`/
  `UnassignVillager(int)`, `RequestAdvancePhase()`(낮→밤 `EndDay()`·잉여 게이트 전용 — **밤→낮 `EndNight()`은 더 이상
  이 메서드가 호출하지 않음, #66. 밤 전용 임시 UI `NightActionPanelView`의 "웨이브 성공" 버튼이 직접 호출, WL-018**),
  **건물 업그레이드**(#139): `bool TryUpgrade(int)`·`bool CanUpgrade(int)`·`int LineLevel/LineMaxLevel/LineAmountPerVillager(int)`·
  `IReadOnlyList<ResourceCost> LineUpgradeCost(int)` — 낮 전용, 수치는 `BuildingAsset.Production.UpgradeLevels`(SO),
  **업그레이드 전용 건물 트랙**(마법 연구소 등, 생산 라인과 별개 index 도메인): `int UpgradeIndexOf(BuildingAsset)`·`int UpgradeBuildingLevel/UpgradeBuildingMaxLevel(int)`·
  `IReadOnlyList<ResourceCost> UpgradeBuildingCost(int)`·`bool CanUpgradeBuilding(int)`·`bool TryUpgradeBuilding(int)` — 낮 전용, 비용은 타입 중립 `BuildingAsset.UpgradeSteps`(#229, 종전 `Skill.UpgradeLevels` 하드코딩),
  같은 `TrySpend` 게이트웨이 경유. **`int GetUpgradeLevel(BuildingAsset)`** = 소비 시스템(스킬 강화 등)이 레벨을 읽는 저결합 창구(효과 적용은 소비 측 소유 — **`SkillManager`/`BuffSkillManager`가 구현 완료, #205**) — BuildingUpgrade.md §8,
  **본진 해금**(#229): `int CastleLevel` = 하위 Max 해금·교환 배율의 단일 기준, `int LineRequiredCastleLevel(int)`·`int UpgradeBuildingRequiredCastleLevel(int)` = 잠겼으면 필요한 본진 레벨 아니면 0 — **셋 다 내부값(0=미업그레이드), 화면 표시는 +1**. ⚠ `*MaxLevel`은 행 수가 아니라 **실질 Max**(연속 만족분) — BuildingUpgrade.md §9,
  **소비 게이트웨이** `bool CanAfford/TrySpend(IReadOnlyList<ResourceCost>)`(소비처는 지갑 직접 접근 대신 경유, 원자 차감 — WL-017),
  **자원 교환 게이트웨이**(#211, 연금술사의 집): `bool CanExchange(BuildingAsset, ExchangeOffer)`·`bool TryExchange(BuildingAsset, ExchangeOffer)` — 낮 전용,
  지불 자원 차감과 대상 자원 지급이 **한 트랜잭션**(차감 실패 시 지급하지 않음). 지갑에 자원을 넣는 **유일한 소비자 대면 API**이며,
  `ResourceWallet.Add`를 public으로 열지 않기 위한 형태다(팀 계약 #3·#6, WL-042 해소 근거). 교환비는 `BuildingAsset.Exchange.Offers`(SO),
  **`int ExchangeGainAmount(BuildingAsset, ExchangeOffer)`**(#229, private→public) = 본진 레벨 배율이 반영된 **실지급량** — 표시부는 원본 `offer.GainAmount` 대신 이걸 써야 표시=실지급이 맞는다,
  질의 `ResourceCount`/`LineCount`/`LineKind`/`LineExpectedProduction`/`AssignedTotal`/`IsDay`/`CanAdvancePhase`, `event OnChanged`(뷰 갱신).
  UI(`ManagementPanelView`/`ProductionLineView`)는 이 컨트롤러만 구독·호출 — UI 아트 교체 시 뷰 참조만 재연결
- `MouseManager.Instance.BeginPlacement(PlacementRequest)` / `CancelPlacement()` / `event OnSelectionChanged` —
  `BeginPlacement`는 진입 시 호버와 **선택(단일+그룹)을 전부 해제**한다(`ClearSelection()`). 선택에 딸린 표시가 정보 패널·사거리 원·아웃라인·합성 패널로 퍼져 있어, 고스트를 든 화면에 남으면 시인성을 해치기 때문(WL-086). 자원 배치·합성 배치 공통. **잔여: `BeginSkillTargeting`은 아직 호버만 해제한다**
- `MouseManager.Instance.ClearSelection()`(WL-086) — **선택 해제의 유일한 창구.** 단일(`Select(null)` → 대상의 `OnDeselected` = 정보 패널·사거리 원, 드라이버의 `Selected` 아웃라인)과 그룹(`OnPrimarySelect(null)` → 코디네이터 집합 해제 = `GroupSelected` 아웃라인·합성 패널)을 **함께** 비운다. 현재 호출자 3곳: Idle Esc / `BeginPlacement` / `PhasePanelSwitcher.ShowNight`.
  ⚠️ **표시만 내리고 `_selected`를 남기면 그 대상은 재클릭해도 다시 뜨지 않는다** — `Select`의 `_selected == next` 중복 제거가 삼킨다. 선택 표시를 내려야 하는 새 경로는 자체 처리하지 말고 이 메서드를 부를 것.
  `OnPrimarySelect`는 원래 "평클릭·Esc·빈 곳 클릭"(입력) 신호였으나 이 창구를 통해 모드·페이즈 전환도 태운다 — 지금은 구독자가 코디네이터 1곳이라 무해하고, "사용자 클릭"과 "시스템 정리"를 구분해야 하는 **3번째 소비자가 붙을 때** `OnSelectionCleared` 분리를 검토한다(WL-085의 판단 시점 패턴)
- `MouseManager.Instance` `event OnGroupSelectToggled(IGroupSelectable)`(#183) — Shift(추가 선택 키)+마커 클릭 시 토글 발행. **토글이 실제로 일어날 때만 발행 직전에 `Select(null)`**로 단일 선택을 비운다(마커 없는 대상은 무시 — 집합·`_selected` 둘 다 불변). 표시 권한을 그룹 경로에 통째로 넘기기 위한 것으로, 안 비우면 직전 단일 선택의 사거리 원이 합성 패널 위에 잔존한다(WL-087)
- `MouseManager.Instance` `event OnPrimarySelect(ISelectable)`(#183) — 평클릭(해석된 대상)·Esc·빈 곳 클릭 시 **중복 제거 없이 항상** 발행(그룹 선택 코디네이터 전용). `OnSelectionChanged`는 `_selected` 변화만 deduped 통지라 Shift-only 선택(`_selected==null`)에서 Esc·빈 곳 해제가 삼켜지던 문제(WL-085) 해소. **우클릭은 해제에 쓰지 않음**(카메라 드래그 이중 점유, WL-073)
- `MouseManager.Instance` **드래그 사각형 선택 3단계 통지**(#261) — `event OnBoxSelectBegin(bool additive)` / `OnBoxSelectUpdate(IReadOnlyList<IGroupSelectable>)` / `OnBoxSelectEnd()`. 좌드래그가 임계(기본 8px)를 넘으면 `Mode.BoxSelect`(Idle 하위) 진입. **갱신 목록은 사각형에 들어온 순서**를 보존하고, 진입 시 `Select(null)`로 단일 선택을 먼저 비운다(Shift 토글 경로와 같은 이유 — WL-087 계열). Shift는 **누른 시점 상태로 고정**되며 클릭의 **토글**과 달리 드래그는 **합집합**이다(의도된 비대칭). Esc = 드래그 중단 + 전체 해제(이전 상태 복원 없음)
  - ⚠️ **단일 선택 확정이 press→release로 이동**했다(#261) — 누른 순간엔 클릭/드래그를 구분할 수 없기 때문. 판정 내용은 무변경(`CommitClick`, release 시점에 레이캐스트를 새로 쏘므로 그새 파괴된 대상은 자동으로 걸러진다)이지만 `ISelectable`을 쓰는 **모든** 소비처(타워·건물·영지 노드·상점)가 이 경로를 지난다. 누름+뗌이 한 프레임에 함께 보고되는 경우를 누른 프레임에서 소화하고, Esc는 진행 중 제스처를 폐기한다(WL-144)
  - **모드 전환은 `SetMode` 단일 창구**(WL-143) — `_mode` 직접 대입 금지. BoxSelect 이탈 시 `OnBoxSelectEnd`를 **1회 보장**하기 위한 것으로, `CancelPlacement`/`CancelSkillTargeting`을 직접 부르는 경로(`PhasePanelSwitcher`)도 자동으로 덮인다. 드래그 종료 판정은 `wasReleasedThisFrame`이 아니라 `isPressed` **상태**로 한다 — 뗀 프레임을 놓치면 모드에 고착돼 모든 클릭·호버가 죽는다
  - **통지 목록은 콜백 안에서만 유효**하다(매니저 내부 리스트 — 캐시 금지). 기준 집합 스냅샷을 뜨는 구독자는 드래그 도중 대상이 파괴될 수 있음을 전제하고 되넣기 전 생존을 확인해야 한다
- `GroupSelectableRegistry.Register/Unregister(IGroupSelectable, Transform)`(#261) — 사각형 판정용 후보 목록. 레이캐스트는 점 하나만 쏠 수 있어 면적 판정이 불가 → 후보를 순회해 스크린 투영으로 포함 여부를 본다(카메라 뒤 제외, **가림은 무시**). 마커가 `OnEnable`/`OnDisable`에서 자기를 등록·해제하므로 주민 등 새 타입은 마커만 붙이면 편입(MouseManager 무수정)
- `MouseManager.Instance.BoxSelectScreenRect` / `IsBoxSelecting`(#261) — 사각형 뷰(`SelectionBoxView`)가 읽어 가는 표시용 상태. 뷰는 **런타임 생성 전용 Canvas**(`UILayer.SelectionBox=50`)라 씬 배치가 없고, 공용 UICanvas 리빌드를 유발하지 않는다(UIZOrder.md §3)
- `MouseManager.Instance.PointerPosition`(포인터 화면 좌표 — Mouse.current 직접 폴링 대신 이걸 쓴다) /
  `event OnHoverChanged(IHoverable)`(커서 밑 호버 대상, 없으면 null. Idle에서만 통지)
- `ISelectable { OnSelected(), OnDeselected() }`,
  `PlacementRequest { GhostPrefab, Snap(RaycastHit→pos), CanPlaceAt(RaycastHit), OnConfirmed(RaycastHit,pos), OnEnded, KeepPlacingAfterConfirm }` — **히트 인지형**: 스냅/검증/확정을 요청 측이 소유(MouseManager는 그리드 규칙 무지), `OnEnded`로 취소/확정 시 프리뷰 정리(PR#81)
- `MouseManager.Instance.BeginSkillTargeting(SkillTargetRequest)` / `CancelSkillTargeting()`(#103) —
  `SkillTargetRequest { GhostPrefab, OnConfirmed(Vector3), OnEnded }`. `PlacementRequest`의 경량 버전(`Snap`/`CanPlaceAt` 없음).
  **전투 타일 전체 허용**: `_placementMask` 히트에서 `CombatMapTileView` 유무로 전투 타일 여부만 판정(도로 전용 제한·유효/무효 색 제거),
  전투 타일 밖에선 인디케이터 숨김. **전투 타일 위 좌클릭**이면 `OnConfirmed(hit.point)`로 확정(타일 게이팅은 MouseManager 소유 — 요청 타입엔 `CanPlaceAt` 훅 없음)
- `SkillManager.Instance` — **null 반환 가능(씬에 없으면) → 호출부 null 체크 필수**(#103).
  `CastAt(Vector3)`(범위 내 적에게 데미지, 밤+쿨다운 게이팅 통과 못하면 false), `CanCast()`,
  `IsReady`, `CooldownRemaining01`(0~1, UI 바인딩용), `Radius`.
  `ImpactResolved` 이벤트(`Action<SkillCastContext>`, #169) — 임팩트(착탄)마다 발행, 보상 특수효과(`SkillEffect`)
  구독용. **컨텍스트의 `HitTargets`는 임팩트마다 재사용되는 버퍼 → 이벤트 처리 중에만 유효, 보관 금지.**
  `ExtraImpacts`/`ExtraImpactInterval`을 구독자가 가산하면 추가 임팩트가 그 간격으로 반복됨
- `BuffSkillManager.Instance` — **null 반환 가능 → 호출부 null 체크 필수**(#103). `Activate()`(타겟팅 없이
  즉시 발동, 밤+쿨다운 게이팅 통과 못하면 false), `CanCast()`, `IsReady`, `CooldownRemaining01`.
  `BuffResolved` 이벤트(`Action<BuffCastContext>`, #169) — 버프 시전마다 발행(Duration 포함), 버프 계열 특수효과 구독용
- `SkillEffectManager.Instance` — **null 반환 가능 → 호출부 null 체크 필수**(#169). 보상 라우터:
  `ApplyReward(WaveRewardData)`(타입 매칭 `SkillEffect` 컴포넌트에 레벨 가산 위임), `GetLevel(WaveRewardType)`(미보유 0)
- `Projectile.DamageDealt`(`static event Action<IAttacker, IDamageable>`, `NorthLand.Combat`, #169 muchan 추가) —
  투사체 데미지가 실제로 들어간 직후 발행(단일/스플래시/체인 전 경로). **static이므로 구독 해제는 구독자 책임**
  (파괴된 MonoBehaviour를 남기면 죽은 구독자 호출 버그). 현재 구독자: `BurnBuff`(버프 창 동안만)
- **타워는 단일 구상 타입 `Tower` 하나뿐이다(#164 리팩토링).** 공격/버프 오라/디버프 오라의 차이는 상속이 아니라
  **행동 조립**으로 표현한다 — 구 `AuraTower`(별개 MonoBehaviour)는 폐기됐다. 상호작용·합성·스킬·BT 계층은
  `Tower` 하나만 알면 되고, "이 타워가 무엇을 하는가"는 타입 검사가 아니라 `Has<T>()`로 묻는다
- `Tower.Build(TowerAsset asset)`(`NorthLand.Combat`) — **타워가 무엇을 하는 물건이 되는지 결정하는 유일한 지점.**
  SO를 보고 행동(`ITowerBehaviour`)을 조립·초기화하고 `Active`에 등록한다. `TowerPlacer.PlaceTower`가 배치 확정 시
  호출하며, 씬 배치·테스트 씬은 `OnEnable`이 직렬화된 `data`로 자가 조립(폴백)한다. 같은 SO로 재호출하면 재무장만,
  다른 SO면 경고 후 재조립(**패널에서 산 SO가 프리팹이 문 SO를 이긴다** — 구 WL-129의 무증상 불일치 해소).
  ⚠️ **`data`가 없으면 아무것도 하지 않고 `Active`에도 들어가지 않는다** — "조립되지 않은 타워는 존재하지 않는 타워"
- `Tower.Has<T>()` / `Tower.Get<T>()`(`where T : ITowerBehaviour`) — **능력 질의 창구.** 소비처가 타워의 구상 타입이
  아니라 능력을 묻게 한다. 예: 보스 P3 마력 봉인 대상 = `Has<AttackBehaviour>()`(`EnemyNodeQuery.IsAttackTower`)
- `Tower.Stats`(`TowerStats`, `NorthLand.Combat`) — **이 타워의 스탯 modifier 단일 원장.** 타일 버프·버프 스킬·
  버프 오라·보스 봉인이 전부 여기로 수렴하며 합성 규칙은 `TowerStats.Evaluate` 한 곳에만 산다:
  `(기본값 + Σflat) × (1 + Σpercent/100) × (1 + Σ배율보너스)`. 축은 `TowerStat`(AttackDamage/AttackRange/AttackSpeed)
  3종, 모드는 `TowerModifierMode`(Flat/Percentage/Multiplier). 소스별 합산 중첩, 같은 소스키는 교체(refresh).
  `Apply(sourceId, modifiers, duration, now)` / `Remove(sourceId)` / `Prune(now)` / `Evaluate(stat, baseValue)`.
  ⚠️ **`Evaluate`는 0 하한을 건다.** 배율 모드가 보너스를 합산하므로(0.5 → −0.5) 디버프 방향 소스가 겹치면
  합이 −1 아래로 내려가 결과가 음수가 되고, 하류에 클램프가 없어(`Enemy.currentHp -= amount`) **음수 데미지가
  회복이 된다**. 예: 보스 P3 마력 봉인은 sourceId가 에이전트별이라 보스 3기가 각각 `damageMul 0.5`를 걸면 음수.
  원장이 단일 출처이므로 이 하한 하나가 전 소비처를 덮는다.
  **원장 축 커버리지(오라 타워 포함)**: 공격 타워는 3축 전부, 오라 타워는 `Radius`(=AttackRange 축) +
  DoT의 `DamageAmount`(AttackDamage 축) + `TickInterval`(AttackSpeed 축)이 원장을 거친다 →
  **타일 버프 3종이 오라 타워에도 동일하게 먹힌다.** 예전에는 반경만 반영돼 "사거리 타일은 먹히는데
  공격력 타일은 무반응"이라는 예측 불가능한 비대칭이 있었다.
  의도적으로 원장을 거치지 **않는** 2가지 —
  ① 오라 재스캔 주기(`DebuffAuraFields.Interval`): 빠르게 해도 DoT는 이미 대상이 소유하므로 갱신만
  잦아지고 피해가 늘지 않는다(독 타워에서 "공속"의 의미를 갖는 축은 `TickInterval`이다).
  ② 슬로우 강도(`Modifiers`의 MoveSpeed): `TowerStat`에 "CC 강도" 축이 없고 공격력·공속에 매핑하면
  의미가 어긋난다 → 순수 감속 타워(choco)는 타일에서 사거리만 이득을 본다(축 신설이 선행 과제)
  **MonoBehaviour가 아닌 순수 C#이고 `Time.time`을 주입받는다** → 씬 없이 EditMode 테스트 가능
  (구 `Tower.activeBuffs`(배율만) ↔ `TowerTileBuff`(Flat+%) 이원화를 통합 — 구 WL-050/WL-081)
- `Tower.Active`(`static List<Tower>`, `NorthLand.Combat`) — 씬에 존재하는 **조립된** 모든 Tower.
  등록은 `Build`(=조립 완료), 해제는 `OnDisable`. `FindObjectsByType<Tower>()` 대체용.
  ⚠️ 등록 시점이 `OnEnable`이 아니라 `Build`인 것이 **고스트(배치 프리뷰)가 타워로 집계되지 않는 근거**다
- `Tower.ActiveChanged`(`static event Action`, `NorthLand.Combat`) — Tower가 `Active`에 추가/제거될 때 발생(#164).
  `BuffAuraBehaviour`가 폴링 없이 사거리 내 대상을 재계산하는 트리거(타워는 스스로 움직이지 않으므로 대상 집합이
  바뀌는 순간은 이때뿐). **static이므로 구독 해제는 구독자 책임**(F7) — `BuffAuraBehaviour`는 `Dispose`+`OnDestroy` 양쪽에서 해제
- `Tower.ApplyBuff(int sourceId, float damageMul, float attackSpeedMul, float duration)` — **원장 위의 얇은 어댑터.**
  기존 소비처(`BuffSkillManager`, `EnemyApplyTowerDebuffAction`)가 무수정으로 남도록 시그니처를 유지한다 —
  내부에서 `Stats.Apply`로 AttackDamage/AttackSpeed 배율 modifier 2개를 등록한다. duration>0=시간제, ≤0=지속형.
  소스키 도메인: 버프 스킬=`"skill.player_buff"` 해시 / 버프 오라=행동 `GetInstanceID()` /
  **디버프 오라=행동 `GetInstanceID()`(구 `TowerID` 해시에서 변경 — 같은 종류 감속 타워가 중첩되지 않던 문제 해소)** /
  타일 버프=`"TowerTileBuff"` 해시(지속형)
- `Tower.RemoveBuff(int sourceId)`(`NorthLand.Combat`) — 해당 소스의 modifier를 즉시 제거
- `Tower.ApplyTileBuff(TileBuffCalculationResult)`(`NorthLand.Combat`) — 타일 버프를 원장에 지속형 소스로 넣는다.
  `TowerTileBuff.Initialize`가 **푸시**한다(스탯 게터가 되읽지 않음) → 초기화 순서 의존이 없다.
  ⚠️ `TowerPlacer`는 이것을 `Tower.Build` **앞에** 호출해야 한다 — 버프 오라가 조립 시점에 자기 반경으로 대상을
  훑고, 그 반경이 타일 버프(사거리)에 의존하기 때문(구 `AuraTower`가 `Start`에서 반경을 재계산하던 우회로의 원인)
- `Tower.Asset`(`TowerAsset`, 읽기 전용, `NorthLand.Combat`, #195 muchan) — 배치된 타워의 원본 SO 조회(합성 재료 TowerID 매칭용). 순수 읽기
- `ITowerBehaviour`(`NorthLand.Combat`) — 타워 행동 한 조각. `ActivePhase`(NightOnly/Always) / `Initialize(in TowerBuildContext)` /
  `Tick(dt)` / `Dispose()` / `DisplayRange` / `DescribeStats()`. 구현 3종: `AttackBehaviour`(Single/Area/Chain — 차이는 `ProjectileImpact`
  전략뿐) · `BuffAuraBehaviour`(이벤트 구동, Always) · `DebuffAuraBehaviour`(Tick 폴링, NightOnly).
  **규약**: ⓐ Awake/OnEnable/Start에서 아무것도 하지 않는다(초기화는 `Initialize`만) ⓑ 스스로 `Update`를 돌지 않는다
  (호스트가 게이팅 후 `Tick`) ⓒ 외부에 남긴 상태는 `Dispose`에서 걷어낸다. **페이즈 게이팅을 호스트가 한 곳에서
  처리**하는 것이 구 WL-044(Tower엔 밤 게이팅, AuraTower 버프 경로엔 없음)의 재발 방지책이다.
  런타임 `AddComponent`로 붙으므로 직렬화 필드가 없다 → 프리팹 계층 참조(`firePoint`)와 `enemyLayerMask`는
  `Tower`에 남기고 `TowerBuildContext`로 주입한다.
  **`DisplayRange`**: 선택 시 그릴 사거리 원의 반경(공격=교전 사거리 / 오라=오라 반경 / 없으면 0).
  호스트는 행동들이 보고한 값의 **최대치**로 원을 그린다. `AttackRange`로 대신 그리면 공격 행동이 없는
  오라 타워에서 0이 되어 원이 사라진다(#192 회귀 — 정보 패널엔 반경이 뜨는데 바닥 원만 없어 더 눈에 띈다).
  `DescribeStats`와 같은 "행동이 자기 표시를 안다" 규약
- `TowerBehaviourFactory`(`NorthLand.Combat`) — **`TowerType`/`MagicEffectType` switch가 사는 유일한 곳.**
  `Create(host, asset, result)`(행동 조립) + `ResolveAttackFields(asset)`(TowerType→AttackFields 단일 출처).
  신규 타워 종류 = `ITowerBehaviour` 구현 1개 + 여기 분기 1줄.
  `ResolveAttackFields` 소비처 3곳: 행동 조립 / `TowerTooltipView`(배치 전 툴팁) / `TowerPlacer`(프리뷰 반경).
  세 곳 다 자체 switch를 갖고 있었고 전부 흡수했다 — 다음 타워 타입 추가 시 빠뜨릴 자리가 없다
- `TowerStatsFormatter`(`NorthLand.Combat`) — 스탯 표시 문자열 단일 출처(구 WL-079: `Tower`/`AuraTower`/`TowerTooltipView`
  3벌 복제 해소). 표시 경로는 둘로 갈리지만(배치 **전** 툴팁은 인스턴스가 없어 SO 원본을 본다) **라벨과 서식은 여기 한 곳**
- `AuraModifiers`(`NorthLand.Combat`, 순수 static) — SO의 `StatModifier` → 적용 값 환산.
  `ComputeSlowMultiplier(mods)`([0,1] 클램프) / `ConvertBuffModifiers(mods, dst)`. 적용부와 표시부가 같은 식을 공유
- `TowerRecipe`(SO, `Assets/Scripts/Data/Tower/TowerRecipe.cs`, #194) — `Materials`(재료 `TowerAsset`+개수)/`Result`(결과 `TowerAsset`)/`ExtraCost`(`List<ResourceCost>`). **인스펙터 손입력(CSV 미경유** — 재료·결과가 SO 참조라 ID 문자열 resolve보다 직접 드래그가 자연스러움)
- `bool TowerPlacer.BeginTowerPlacement(TowerAsset result, IReadOnlyList<ResourceCost> cost, System.Action onConfirmed, System.Action onEnded = null)`(#195) — 비용·확정 콜백 주입 오버로드(합성 결과 배치용. 확정 직후 콜백에서 합성이 재료 파괴를 **확정**한다 — 소모 자체는 그보다 앞선 버튼 클릭 시점, #263). 기존 `BeginTowerPlacement(TowerAsset)`은 `cost=so.Cost, onConfirmed=null`로 위임(동작 불변). 확정 시 `_management.TrySpend(cost)` 후 `Instantiate`.
  **`onEnded`** = 확정/취소/다른 배치로 교체 무관 **배치 세션 종료 1회**(`PlacementRequest.OnEnded`→`EndPlacement`에서 발화, 먼저 비우고 호출). **어느 쪽으로 끝났는지는 알려주지 않는다** — 구분이 필요한 소비처는 자기 상태로 판단해야 한다(합성 커맨드가 `Committed` 플래그로 그렇게 한다, #263). **반환값** = 세션이 실제로 시작됐는가 — false면 `onEnded`도 오지 않으므로 호출부가 "배치 동안 유지"할 상태를 걸면 안 되고, 이미 저지른 부수효과가 있으면 그 자리에서 되돌려야 한다(합성이 소모한 재료를 `Undo`한다).
  ⚠️ `onConfirmed`/`onEnded`는 `MouseManager.BeginPlacement` **이후에** 대입해야 한다 — `BeginPlacement` 내부 `CancelPlacement`가 직전 세션의 `EndPlacement`를 발화해 콜백을 소비·초기화하기 때문
- `bool TowerFusionController.TryFuse(TowerRecipe recipe, TowerMergeGroup group)`(#195, #183·#263에서 시그니처 변경) — 합성 실행. 포함 매칭+`CanAfford` 검증→**재료 소프트 소모(`TowerMergeCommand.Execute`)**→`TowerPlacer` 배치→확정 시 `Commit`(재료 `Destroy`)/취소 시 `Undo`(원복). 반환값(=배치 세션이 실제로 시작됐는가)은 `BeginTowerPlacement`를 그대로 통과시키며, **false면 소모한 재료를 즉시 `Undo`한다**(그 경로엔 종료 통지가 오지 않는다). 코디네이터 `RequestMerge`가 그룹을 물려 호출. **구 `onEnded` 인자는 #263에서 제거** — 유일한 소비처였던 합성 핑크 고정이 폐기됐고, 그 인자는 확정과 취소를 구분하지 못했다. **#265에서 확정/취소 구분이 필요해졌지만 인자를 되살리지 않았다** — 커맨드의 `IsCommitted`를 종료 콜백 안에서 읽어 `Reassemble`(취소) / `Abort`(확정) 로 갈랐다. 확정/취소 판단의 단일 출처가 여전히 커맨드 하나다
- `float TowerPlacer.TileSize`(#265) — 셀 간격. `Awake`에서 신맵 설정을 단일 출처로 해석해 둔 값이라, "타일 한 칸"을 기준 길이로 쓰는 쪽(합성 연출의 알갱이 크기)이 같은 해석을 다시 하지 않는다
- ⚠ `TowerPlacer.BeginTowerPlacement`의 `onConfirmed`가 `Action` → **`Action<Transform>`**(배치된 타워)로 바뀌었다(#265). 합성 연출이 수렴 목적지를 재려면 대상이 필요하고, 그 측정은 등장 연출이 스케일을 0으로 만들기 **전**이어야 해서 좌표가 아니라 Transform을 넘긴다
- `IReversibleCommand { bool Execute(); void Commit(); void Undo(); }`(#263) — 임시 실행 → 확정 또는 원복. 일반 Command의 2단이 아닌 이유는 "확정되어 더는 되돌릴 수 없음"을 표현해야 하기 때문(트랜잭션에 가깝다). **진행 중 커맨드는 항상 최대 1개**라 전역 Undo 스택을 두지 않는데, 그 보장의 근거는 `keepPlacing`이 아니라 **`BeginPlacement`가 새 세션 전에 `CancelPlacement`를 먼저 부른다**는 것이다(이전 커맨드의 `Undo`가 그때 발화). **위치 정책**: 이름은 범용이지만 현재 합성 전용이라 실행부와 같은 폴더 + 전역 네임스페이스에 둔다 — **두 번째 구현체(경영 철거 등)가 생기는 시점에 중립 위치로 이전**(WL-085와 같은 축)
- `TowerMergeCommand`(#263, 순수 C#) — 합성 재료의 소프트 소모. `Execute`=`TowerFootprint.Release()`+`SetActive(false)` / `Commit`=`Destroy` / `Undo`=`Reoccupy()`+`SetActive(true)`. **확정/취소 판단을 자기 상태로 한다** — 배치 종료 통지가 어느 쪽인지 알려주지 않으므로, 이 방식이라야 `TowerPlacer`/`MouseManager` 무수정으로 "취소일 때만 원복"이 성립한다. `TowerMergeGroup`을 모른다(집합 정리는 비활성화→`ActiveChanged`→`Prune`이 담당)
- `TowerFootprint.Release()` / `Reoccupy()`(#263) — 등록 목록은 유지한 채 `BattleTile.Occupied`만 임시 해제/복원. 규칙 셋: **①`OnDestroy`는 `Release` 이후 타일을 건드리지 않는다**(합성이 재료 자리에 결과를 놓으면 그 타일은 이미 결과의 것 — 무조건 해제하면 결과가 선 칸이 빈 칸으로 표시돼 그 위에 또 배치된다). **②`Reoccupy`는 이미 점유된 타일을 되찾지 않고 목록에서 뺀다**(되찾으면 소유권까지 가져와 ①의 증상이 되살아난다). **③`OnEnable`이 `Release` 상태면 스스로 `Reoccupy`**(커맨드를 거치지 않는 활성화 경로 대비 안전망, 정상 경로에선 no-op)
- `TowerMergeGroup`(#183, 순수 C#) — 선택 재료 집합. `IReadOnlyList<Tower> Towers`/`Add`/`Remove`/`Clear`/`SetSingle(tower)`(원자 단일화, 통지 1회)/`Prune(Predicate<Tower>)`(주입 판정으로 죽은 항목 제거) + `event Action OnChanged`(변경 시 발행, 코디네이터가 구독해 UI·하이라이트 갱신). **`TowerMergeCoordinator`가 소유**(씬 오브젝트 아님) — 구 임시 `TowerWallet`(#195) 대체·폐기
- `TowerMergeCoordinator`(#183, MonoBehaviour) — 합성 선택 두뇌·실행 오케스트레이터. `MouseManager.OnPrimarySelect`(평클릭/Esc/빈곳 → 그룹 리셋·해제)/`OnGroupSelectToggled`(Shift 토글)·`DayNightManager.OnDayToNight`(그룹만 리셋)·`Tower.ActiveChanged`(→ `Prune(t=>t==null||!Tower.Active.Contains(t))` stale 정리)·`TowerMergeGroup.OnChanged` 구독(낮 게이팅·하이라이트·우측 패널 스왑 1개=`TowerInfoUI`/2개↑=합성 패널). 마커→타워 해석은 `grp is TowerGroupSelectable`로 코디네이터가 흡수. 파사드: `SelectedTowers`/`event OnGroupChanged`/`CanMerge(recipe)`/`RequestMerge(recipe)`. `OnDestroy`에서 구독 해제(F7). **진행 중 배치 취소(밤)는 여기가 아니라 `PhasePanelSwitcher.ShowNight`가 담당**(페이즈 취소 책임 일원화)
- `IGroupSelectable { OnGroupSelected(); OnGroupDeselected() }`(#183, **도메인 완전 중립 — Tower 미참조**) + `TowerGroupSelectable`(타워 구현, `TowerPlacer.PlaceTower`가 `TowerFootprint`와 같은 지점에서 런타임 `AddComponent`) — MouseManager가 마커 유무로만 그룹 선택 자격 판정(타워 무지 → 제네릭). 마커→타워 해석은 소비처(`TowerMergeCoordinator`)가 `grp is TowerGroupSelectable` 캐스팅으로. 그룹 하이라이트 훅은 단일선택 `ISelectable`과 분리
- `TowerMergePanelView`(#183, UI) — 합성 패널. **코디네이터만 참조**(파사드). 상단 선택 리스트 + 하단 후보 버튼(레시피당 1개 미리 생성 후 기본 숨김, `CanMerge`면 `SetActive`, onClick→`RequestMerge`). 카탈로그=패널 인스펙터 직렬화 배열 `TowerRecipe[] _recipes`(등록 순서 결정적; 예시 SO 2종은 `Assets/Resources/ScriptableObjects/TowerRecipes/`)
- `TowerFusionMatcher`(#195) — 포함 매칭. `TryResolve(walletTowerIds, required, out consumeIndices)`(순수 코어, 씬 비의존 EditMode 테스트 대상, 소모 인덱스 반환) + `BuildRequired(TowerRecipe)`(재료→(TowerID,개수) 집계) + `CanFuse(IReadOnlyList<Tower>, TowerRecipe)`(bool, #183 후보 버튼 활성 판정용). 실행부·버튼이 같은 규칙을 공유하는 단일 출처
- `IHoverable { TooltipContent? GetTooltipContent(), void OnHoverEnter(), void OnHoverExit() }` — 호버 시 툴팁 내용을 pull 공급(호버 시점마다 호출 → 동적 값 가능, `null`이면 툴팁 없음)
  + 호버 진입/이탈 훅(하이라이트 등 연출, `MouseManager.SetHover`가 대상 전환 시 호출, #67)
- `TooltipUI.Instance.Show(TooltipContent)` / `Hide()` — 커서 추적 범용 툴팁 뷰(#38). **임시 싱글톤(UIManager 흡수 예정)**,
  `TowerInfoUI`/`BuildingInfoUI`와 동일 계보. `OnHoverChanged`를 자체 구독. `Assets/Scripts/GameManager/MouseHover`
- `TooltipContent { Header, Body, HeaderColor, BackgroundColor }` — 구체 개념 무지한 표시 데이터. 건물·버프 등 공급자가 채움
- `BuildingTooltipSource`(건물용 `IHoverable` 어댑터, `BuildingAsset`/`BuildingData` **읽기 전용** 소비) +
  `BuildingTooltipPalette`(`BuildingType`→색 SO). 클릭 선택 `BuildingInfo`와 **역할 분리**(호버=요약 툴팁, 클릭=기능 패널)
- `DayNightManager.Instance` — **null 반환 가능(씬에 없으면) → 호출부 null 체크 필수**.
  `CurrentPhase` / `WaveCount` / `EndDay()` / `EndNight()` / `event OnDayStart, OnDayToNight, OnNightToDay`.
  `OnDayStart`는 1일차 부트스트랩 포함 매 낮 시작마다 발생, `OnNightToDay`는 밤을 거친 전환에서만 발생(웨이브 종료 의미) — 구독 시 구분해서 사용할 것.
  `EndNight()`은 이제 `MonsterSpawn`이 웨이브 클리어(스폰 완료 후 생존 0) 시 자동 호출(#17)하며, 밤 전용 임시 UI(`NightActionPanelView`의 "웨이브 성공" 버튼, #66) 수동 호출도 병존한다. 단 클리어가 아직 처치가 아닌 본진 도달-디스폰 기준(처치 기반은 Enemy 병합 후 — WL-038); 실패/보스 판정 연동·임시 버튼 제거는 WL-018 잔여
- `TerritoryController.Instance` — 씬 싱글톤(`DayNightManager`와 동일 패턴, `DontDestroyOnLoad` 없음).
  `Graph`(읽기 전용 질의), `bool TryClaim(int nodeId)`(유일한 변경 진입점 — 구조 불변식은 `Graph`,
  하루 1회 게이팅 정책은 이 레이어), `bool HasExpandedToday`(오늘 확장 완료 여부, `OnDayStart`마다
  초기화), `event OnChanged`(그래프 상태 또는 `HasExpandedToday` 변경 시 발행)
- `TerritoryGraph` (순수 C# 모델) — `Nodes`, `Frontier`, `OwnedCount`, `GetNode(id)`(null 반환 가능),
  `bool IsRevealed(id)`(Owned+Selectable 공개 판정), `bool IsOwned(id)`(Owned 전용 — 엣지 배 연출 게이팅용, #93), `bool TryClaim(id)`(구조 불변식만 검사 — Selectable만 확보 가능),
  `event OnNodeClaimed`(효과 적용 훅 — `ManagementController`가 구독해 효과 Apply, WL-030 해소), `event OnChanged`
- **미개척 영지 자원 SO**(`ManagementSpace/Territory/TerritoryDefinition.cs`, TerritoryGraph.md §5, #166) —
  `TerritoryDefinition`(SO): `ResourceKind Kind`(금/루비/사파이어/다이아), `GameObject IslandPrefab`(확보 시 섬),
  `int MinDaily`/`MaxDaily`, `int RollDailyYield(System.Random)`(주입 시 [Min,Max] 1회 롤), 표시명/설명 키(`NorthLand_Territories`).
  수치는 **SO에 authored(CSV 아님, 팀 결정)**. `TerritoryNode.Definition`(SO ref)+`TerritoryNode.DailyYield`(롤 결과)에 주입되며,
  배정·롤은 `TerritoryController`가 생성 시 `_seed`로 수행(SO 4종<노드라 자원 중복 정상, 시드 결정성).
  **수급**: `ManagementController.HandleNightToDay`가 매 정산 시 `Graph.Nodes` 중 Owned+Definition 노드를 순회해
  `ResourceWallet.Add(Definition.Kind, DailyYield)` — **확보 즉시 지급 없음, 주민 배치 무관**(GDD §3.2 자동 수급).
  ⚠ 종전 효과 SO 계층(`TerritoryEffect`/`GrantResourceEffect`/`GainResidentEffect`/`ProductionMultiplierEffect`/`TerritoryEffectContext`)은 **삭제됨**.
  `ProductionModifiers`는 잔존하나 생산자가 없어 항상 ×1(기본 라인 정산·예상치 호출부 무변경)
- `ManagementController.SupplyDaily(ResourceKind)` — Owned 그 종류 영지들의 `DailyYield` 합(없으면 0). 패널 특수 자원 row의 "+n"·활성 판정용.
  `ManagementController.ManaPerWaveClear`(int) — 마나 row "+n" 미리보기용(웨이브 클리어 고정 마나)
- `StageRoadTracker.RoadWorldPoints` — ⚠️ HashSet(순서 없음). **이동 경로로 사용 불가**
- MapBuilder의 **순서 있는 경로·스폰 지점·최종 목표 좌표는 아직 공개 API가 없음** (WL-003)
- `TowerSpawnEffect.Play(Transform target, float footprintSize, float tileSize)`(`NorthLand.Combat`, #264/#265) — 등장 연출 재생(fire-and-forget).
  길이 인자가 **둘**인 이유: 풋프린트(칸 수 × 타일)는 링 반경·후광 두께를, tileSize(한 칸)는 **알갱이 크기**를 정한다.
  겸용하면 다중 셀 타워에서만 알갱이가 커져 합성 유입 입자와 크기가 어긋난다(1×1뿐인 현재는 두 값이 같아 무증상).
  **대상을 모른다** — `Tower`/`TowerAsset`/메시를 받지 않고 `Renderer.bounds`와 `localScale`만 읽는다. Renderer가 달린 큐브에도 재생된다.
  ⚠ **재생 중 대상 루트의 `localScale`을 배타적으로 소유한다**(0 → 원본, 안 보이는 창 약 0.45초 + 과도기 약 0.28초).
  이 창 동안 대상의 스케일을 쓰거나 **캡처**하는 다른 시스템이 있으면 깨진다 — 계약 전문은 `Docs/Core/TowerPlacement.md` §9.3.2.
  같은 대상에 이미 재생 중이면 기존 연출을 원복시키고 인계하므로 중복 호출은 안전하다.
- `TowerSpawnEffect.PlayAsync(Transform, float, CancellationToken)`(`NorthLand.Combat`, #264) — 위와 동일하되 종료까지 대기
- `TowerSpawnEffect.CalculateVisualBounds(Transform, float footprintSize)`(`NorthLand.Combat`, #264) — 대상의 월드 AABB.
  `RangeCircle` 자식은 제외한다(반경이 타워보다 커서 포함하면 bounds가 부풀음). Renderer가 없으면 풋프린트 크기의 대체 상자를 낸다
- `TowerSpawnEffect.ConvergeDuration` / `.PopDuration`(`NorthLand.Combat`, #265) — **두 연출이 공유하는 시간 축**(0.45s / 0.28s).
  합성 유입 입자가 `ConvergeDuration`을 쓰는 것은 취향이 아니라 요구다: 비행 시간을 거리가 아니라 시간으로 묶어야
  거리가 제각각인 재료의 입자가 **결과 타워가 튀어나오는 순간을 넘기지 않는다**. 속도를 고정하면 먼 재료의 입자가
  타워가 다 선 뒤에 도착해 인과가 뒤집힌다. 이 상한 **안에서는** 알갱이 크기가 각자의 도착 시각을 정하고
  (작을수록 빨리 — 크기와 속도를 같은 난수에서 뽑는다), 가장 큰 알갱이만이 이 값을 꽉 채운다
- `TowerMergeDissolveEffect.Play(IReadOnlyList<Transform> targets, float tileSize)`(`NorthLand.Combat`, #265) — 합성 재료 소모 연출 시작.
  **재료를 소모하기 직전에** 불러야 한다 — 커맨드가 `SetActive(false)`를 걸고 나면 복제할 시각물이 남지 않는다. 항상 유효한 인스턴스를 반환.
  길이 인자는 **타일 한 칸**이다(풋프린트 아님) — 이 연출의 모든 길이가 "저 칸 것"을 말하는 단위다
- `TowerMergeDissolveEffect.ConvergeTo(Transform placed)` / `.Reassemble()` / `.Abort()`(#265) — 부유 루프의 마무리 3종(선착순, 먼저 정해진 쪽이 이긴다).
  ⚠ `ConvergeTo`는 **`TowerPlacer` 확정 콜백에서**(등장 연출이 스케일을 0으로 만들기 전에) 불러야 목적지가 정상값이다.
  ⚠ `Reassemble`은 **커맨드 `Undo` 직후 같은 프레임에** 불러야 되살아난 재료가 한 프레임 번쩍이지 않는다.
  셋 다 **폭발이 끝나기 전에도 도착할 수 있다**(클릭 직후 취소 등) — 그 경로는 소멸 구간을 즉시 완료 상태로 스냅한 뒤 마무리로 넘어간다
- `GrainSwarm`(`NorthLand.Combat`, #265) — 두 연출이 공유하는 흰 알갱이 렌더링 부품(빌보드 쿼드·절차 텍스처·개수/크기 규칙·전체 알파).
  **움직임을 모른다** — 좌표는 전부 호출자가 정한다. `ResolveGrainSize`는 bounds도 풋프린트도 아닌 **타일 한 칸**을 받는다 —
  그리드가 정하는 값이라 에셋 교체와 무관하고, 칸 수에도 흔들리지 않아 **모든 타워의 알갱이가 같은 크기**로 보인다
- `VfxScaleHold.Acquire(Transform target) : Handle`(`NorthLand.Combat`, #265) — 대상 `localScale`의 배타 점유권 발급(0 → back-out 팝 → 원복).
  대상에 붙는 컴포넌트라 **점유 상태가 대상과 함께 죽는다**(구 static 딕셔너리의 죽은 키 누수 문제가 사라졌다).
  이미 점유 중이면 그 자리에서 원본을 복원하고 인계하며, `Handle.IsSuperseded`로 인계당한 연출이 남은 구간을 스스로 접는다
- ⚠ `RangeCircle`의 **부모 스케일 역보정 시점이 바뀌었다**(#264): 생성 1회 캡처 → `Show`/`SetRadius`/`LateUpdate`마다 재계산.
  부모 스케일이 런타임에 변하는 대상(등장 연출이 타워 루트를 0→1로 애니메이션)에서 보정이 잘못된 값으로 굳던 경로를 막는다.
  **부모 스케일이 고정된 기존 소비처는 동작이 동일하다**

## 3. 접점 매트릭스 (왼쪽 시스템을 건드리는 PR은 오른쪽 항목을 실제 코드로 대조)

| 접점                                     | 확인할 것                                                                                                                                                                                                                  |
| ---------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Combat ↔ BattleMapBuilder                | 몬스터 이동 경로(순서 있는 경로 API 부재 — WL-003), 레이어(WL-005), 좌표계(battlespace 로컬 vs 월드 — WL-007)                                                                                                              |
| Combat ↔ DataTable                       | 스탯 데이터 원본(CSV 파이프라인 vs 손입력 SO — WL-001)                                                                                                                                                                     |
| MouseManager ↔ BattleMapBuilder          | 그리드 스냅, CanPlaceAt·타일 종류 질의 API(WL-004), 좌표계(WL-007)                                                                                                                                                         |
| TowerVfx ↔ MouseManager / TowerFusion    | **스케일 배타 소유 계약**(`TowerPlacement.md` §9.3.2, 구현은 `VfxScaleHold`). 소유자가 둘이 됐다: 등장 연출은 결과 타워를, 합성 재조립은 되살아난 재료를 잡는다. 서로 다른 대상이라 정상 경로에선 겹치지 않지만, 겹치면 점유 세대로 인계된다. **#265는 결과 타워에 `PlayAsync`를 걸지 않는다** — 자기 입자를 같은 시간 축으로 나란히 날려 보낼 뿐이라 이중 재생 문제가 애초에 생기지 않는다(#264 시점의 우려는 이 설계로 해소). 연동 규칙 셋: ⓐ `TowerMergeDissolveEffect.Play`는 커맨드 `Execute` **직전**, ⓑ `ConvergeTo`는 `TowerPlacer` 확정 콜백에서 등장 연출보다 **앞**, ⓒ `Reassemble`은 커맨드 `Undo` **직후 같은 프레임**. 셋 다 어기면 조용히 어긋난다(복제할 시각물 없음 / 쪼그라든 bounds / 한 프레임 번쩍임). 드래그 선택은 콜라이더가 아니라 위치 기반이라 **연출 중(스케일 0)에도 대상에 도달한다** |
| MouseManager ↔ Combat                    | 타워 배치(PlacementRequest→Tower 프리팹), 선택(ISelectable), TowerInfoUI 데이터 연동(WL-011). **#164 리팩토링 후**: 배치 확정 시 `TowerPlacer`가 `Tower.Build(so)`를 호출해 인스턴스를 산 SO로 조립한다(프리팹↔SO 불일치 해소). 또 모든 타워가 단일 `Tower` 타입이 되어 마법 타워도 `sel is Tower`·`TowerGroupSelectable.Tower`에 정상적으로 걸린다 — 구 WL-131(마법 타워가 합성·그룹 선택에서 조용히 제외되고, 평클릭 시 담아둔 합성 그룹이 해제되던 부작용)이 타입 통합만으로 소멸 |
| DataTable ↔ Localization                 | 표시 문자열 소유권: Building/Enemy/Resource/Tower CSV 표시 문자열을 String Table 키로 이관 완료(WL-013 해소, PR#126). 잔여: 전투 공간(TowerInfoUI) 키 조회 배선 후속(#102). 테이블명 상수 대소문자 정합(WL-060)                                                                                                                                                          |
| Management(Resource) ↔ DataTable         | `ResourceKind`(지갑 키)·`BuildingAsset.ProductionFields`(생산처 입력)·`ResourceAsset.Data`(정산 시 `Kind` 해석, 호출부 `Start()` 채움 규약) 의존 — muchan이 이 구조 바꾸면 자원 시스템 깨짐                                |
| Management(Resource) ↔ DayNightManager   | 정산+주민 초기화=`OnNightToDay` 구독(정산 먼저), 낮→밤 전환=`ManagementController.RequestAdvancePhase()`가 `EndDay()` 호출. **밤→낮(`EndNight`)은 이제 `NightActionPanelView`의 "웨이브 성공" 버튼이 임시 트리거 — 밤 종료 주체(Combat 웨이브 클리어 등)로 책임 이관 필요(WL-018)**. 주민 수는 여전히 placeholder(주민 시스템 부재)                |
| BattleMapBuilder/Monster ↔ DayNightManager | 밤 시작(`OnDayToNight`)에 `StageBuilder`가 구독 → 다음 스테이지 생성(전투영역 확장) + `MonsterSpawn.StartRound`로 몬스터 스폰(`currentMapCount > 1`, #17). `MonsterSpawn`은 낮이면 스폰 스킵(경고 로그). 웨이브 클리어(스폰 완료 후 생존 0) 시 `MonsterSpawn`이 `EndNight()` 호출로 낮 복귀(#17) — 단 본진 도달-디스폰 기준(처치 기반은 Enemy 병합 후 WL-038); 실패/보스 판정·임시 버튼 제거는 WL-018 잔여 |
| Management(Resource) ↔ 주민(미존재)      | 주민 수 입력 심 — 현재 `_maxVillagers` placeholder + 패널 +/-. 주민 시스템 생기면 출처 이관                                                                                                                                |
| Management(Resource) ↔ Territory         | `TerritoryController.HasExpandedToday`(하루 1회 확장 완료 여부, `OnDayStart`마다 초기화)가 `ManagementController.CanAssignVillagers`를 게이팅 — 확장 전엔 `AssignVillager`/`UnassignVillager` 불가(이슈 #67, GDD §6.1). `ManagementController`가 `TerritoryController.OnChanged` 구독해 확장/낮 시작 시 패널 즉시 갱신(`ProductionLineView`의 `+`/`-` `interactable`도 함께 반영). `TerritoryController`가 씬에 없으면(null) 게이트 없이 배치 허용(permissive, WL-002와 동일 완화 패턴). **자원 수급(#166)**: `ManagementController.HandleNightToDay`가 매 정산 시 `Graph.Nodes`의 Owned+Definition 노드를 순회해 `ResourceWallet.Add(Definition.Kind, DailyYield)`로 **매일 자동 수급**(확보 즉시 지급·주민 배치 무관, GDD §3.2). 종전 `OnNodeClaimed` 즉시 효과 적용 경로는 제거됨 — muchan이 `TerritoryNode.Definition`/`DailyYield`·`Graph.Nodes` 구조를 바꾸면 수급 정산이 깨짐 |
| Management(Resource) ↔ PlayerSkill(마법 연구소) | **✅ 착지점 확정·구현 완료(#205)**. 메커니즘: `SkillManager`/`BuffSkillManager`가 `ManagementController.GetUpgradeLevel(magicLabAsset)`으로 **레벨(int)만** 읽고, 레벨→배율 매핑은 `magic_lab.asset`의 `Skill.UpgradeLevels`(SO, 도달 비용과 같은 리스트, 수치는 placeholder)에 authoring(컨트롤러는 "스킬"을 모름, `OnChanged` 통지→재-pull, 컨트롤러/UI 무수정). 비용·배율이 물리적으로 한 리스트라 레벨 개수가 어긋날 수 없다(PR#216 리뷰 — 최초엔 씬 인스펙터 리스트였다가 이관). **연구소 레벨 = 기본 스킬 스탯 배율**(damage/radius/cooldown, 버프 배율/지속시간/쿨다운), **보상 특수효과(`SkillEffect.Level`/`SkillEffectManager.GetLevel`, #169) = 독립된 두 번째 축** — 두 축은 동시 스택되며 서로 충돌·이중 스케일링 없음(코드 확인 완료). GDD §5.5 편입 완료. 상세: `BuildingUpgrade.md` §8, `PlayerSkill.md` §3.1 |
| DataTable(Building) ↔ MouseManager       | `BuildingInfo`가 `ISelectable` 구현 + `BuildingAsset` 보유 — 선택 시 `BuildingInfoUI` 직접 호출(이벤트 미구독, WL-011과 동일 패턴). `BuildingTooltipSource`(#38)가 `IHoverable` 구현 + `BuildingAsset`/`BuildingData`/`BuildingType`을 **읽기 전용** 소비(muchan 구조 바뀌면 툴팁 깨짐 — 자체 `DataTableManager.Get` 조회, Data 채움 규약 의존). `MouseManager`가 씬에 없으면 조용히 무반응(WL-002) — 씬마다 배치·`_camera` 재할당 필요 |
| MouseManager ↔ TerritoryGraph            | `TerritoryNodeView`가 `ISelectable`(클릭=즉시 `TerritoryController.TryClaim`) + `IHoverable`(호버 하이라이트 — 신형 프리팹(`TerritoryNodeV2`)은 `TerritoryNodeStateVisual`에 위임해 소용돌이 밝기/가속·소진 시 회색, 구형 프리팹은 기존 색 변경 폴백. `GetTooltipContent()`는 노드의 `TerritoryDefinition` 이름·설명을 `LocalizationHelper`(`NorthLand_Territories` 테이블)로 pull해 `TooltipUI`에 공급 — 정의 없는 노드(본진)는 `null`) 둘 다 구현 — 같은 콜라이더에 두 인터페이스 공존이 이미 지원되는 경로임을 실증(#67, `BuildingInfo`+`BuildingTooltipSource` 조합과 동일 패턴). Layer 6(`Selectable`) 배정 확인됨(WL-005 해소). **클릭 판정은 노드 루트 `SphereCollider` 전용** — `MouseManager`가 `hit.collider.TryGetComponent`로 부모 미탐색이므로 산 에셋의 자식 콜라이더는 스폰 시 전부 비활성(#127). 엣지 배 연출(#93)도 인스턴스 배의 `MeshCollider`를 스폰 시 제거해 선택 레이캐스트 간섭을 차단(동일 취지) |
| PlayerSkill ↔ MouseManager               | 스킬 버튼 클릭 → `BeginSkillTargeting(SkillTargetRequest)` → **전투 타일 위이면**(`CombatMapTileView` 존재) 확정, `OnConfirmed(Vector3)`로 `SkillManager.CastAt` 호출(#103). 인디케이터는 전투 타일 밖 숨김(유효/무효 색 없음). `PlacementRequest`와 별개 타입 — 그리드 개념 없음                                                                                                            |
| MouseManager ↔ CombatSpace(맵)           | 스킬 타겟팅이 히트 타일의 `CombatMapTileView` 유무로 전투 타일 여부 판정(#103 후속, 도로 전용 제한 제거). MouseManager→CombatSpace 단방향 읽기(입력 매니저가 전투 공간 타일 데이터에 의존 — 지켜볼 커플링)                                            |
| PlayerSkill ↔ Combat                     | (감전) 새 데미지 파이프라인 없이 `IDamageable`/`DamageInfo`/`Faction`을 그대로 소비(`Tower.FindTarget()`/`Projectile.ApplyArea()`와 동일한 `OverlapSphereNonAlloc`+Faction 필터링 패턴). `DamageInfo.Source`는 스킬 시전 시 `null`(IAttacker 개체가 아님 — 현재 아무도 역참조 안 해 안전). (버프) `Tower.Active` 순회 + `Tower.ApplyBuff` 호출. **#164 리팩토링 후 결합도가 낮아짐**: `ApplyBuff`가 스탯 원장(`TowerStats`) 위의 얇은 어댑터로 남아 시그니처가 보존되어 `BuffSkillManager`는 **한 줄도 바뀌지 않았다**. 이제 Combat 내부 구조(행동 조립·원장)가 바뀌어도 이 계약면만 유지되면 스킬 쪽은 영향받지 않는다 |
| TowerFusion ↔ Combat                     | `Tower.Asset` 읽기로 재료 TowerID 매칭. **재료 소모는 #263부터 2단계** — 클릭 시 `SetActive(false)`(소프트, `OnDisable`이 `Tower.Active` 해제·행동 `Dispose`·원장 비움) → 확정 시 `Destroy` / 취소 시 `SetActive(true)`(`OnEnable`이 타일 버프 재적용·행동 재무장·재등록). **`Tower.OnEnable`/`OnDisable`의 대칭 왕복이 이 원복의 유일한 근거다** — 풀 재사용용으로 만들어진 것을 합성이 그대로 쓴다. SUNGSOO가 그 대칭을 깨거나(한쪽에만 상태 추가) `Tower.data`/`Asset`·`TowerAsset` 필드 그룹을 바꾸면 매칭·배치·**원복**이 깨짐(WL-001 축, 읽기 접근자는 muchan이 `Tower.cs`에 추가) |
| TowerFusion ↔ MouseManager/TowerPlacer   | `TowerPlacer` 신규 오버로드로 고스트 배치 재사용(확정 콜백=커맨드 `Commit`, 종료 콜백=`Undo`, 비용은 기존 `TrySpend` 경로). **배치 코어는 #263에서도 무수정** — 확정/취소 구분을 커맨드가 자기 상태로 하므로 `TowerPlacer`에 "어느 쪽으로 끝났는지" 통로를 새로 뚫지 않았다. 진행 중 커맨드의 원복은 전부 기존 `CancelPlacement` 경로 하나로 수렴한다(Esc·우클릭 / 밤 전환 `PhasePanelSwitcher.ShowNight` / 새 배치의 선행 `CancelPlacement` / 씬 전환 `HandleSceneLoaded`) → 합성 전용 정리 코드 없음. tileSize·풋프린트(WL-034)·타일 종류 계약(WL-067)의 동일 전제를 그대로 상속 — TowerPlacer가 신맵 질의로 이관되면 합성 배치도 함께 따라감. **선택(#183)**: `TowerMergeCoordinator`가 `MouseManager.OnGroupSelectToggled`(Shift 토글)+`OnPrimarySelect`(평클릭/Esc/빈곳, 항상 발행)를 구독해 그룹을 만든다 — MouseManager는 마커 `IGroupSelectable`(타워는 `TowerGroupSelectable`, `TowerPlacer`가 배치 시 런타임 부착)만 보고 타워를 모름. n0wst4ndup이 MouseManager 선택 계약(Shift·Esc·`OnPrimarySelect`)을 확장 → 다른 선택 소비처(건물/영지)와 공존 확인. **밤 진입 배치 취소는 `PhasePanelSwitcher.ShowNight`로 이관**(페이즈 취소 일원화). **드래그 선택(#261)**: 코디네이터가 `OnBoxSelectBegin/Update/End`를 추가 구독해 같은 집합에 반영한다 — 시작에서 기준 집합 스냅샷(Shift면 유지 후 합집합, 아니면 교체), 갱신마다 `TowerMergeGroup.SetAll`로 순서 보존 원자 교체(내용 동일하면 no-op). **드래그 중에는 하이라이트만 실시간이고 패널·후보 버튼 갱신은 유예**(사각형이 2개를 넘나들 때 합성 패널 깜빡임·매 프레임 GC 방지), 종료 시 1회 처리. 밤 진입 시 유예 상태를 직접 해제(게이팅에 막혀 종료 통지를 못 받는 경우 대비) |
| TowerFusion ↔ Management(Resource)        | 합성 `ExtraCost`를 `ManagementController.CanAfford/TrySpend`(WL-017 게이트웨이)로 지불 — `TowerPlacer` 확정 경로 재사용(별도 차감 로직 없음). 컨트롤러가 씬에 없으면 무료(permissive) |
| 모든 시스템 ↔ 전역 설정                  | 레이어/태그(`ProjectSettings/TagManager.asset` — WL-005), URP 설정(`Assets/Settings`), 패키지(`Packages/manifest.json`)                                                                                                    |

## 4. 팀 계약 (위반 = 🔴 후보)

1. **입력 단일 창구**: 포인터/키보드 입력은 MouseManager만 읽는다. 게임플레이 코드의
   `Mouse.current`/`Keyboard.current` 직접 폴링 금지. 클릭 반응은 ISelectable, 그룹 선택 참여는
   IGroupSelectable 마커, 배치는 PlacementRequest로 참여. 스킬 타겟팅·드래그 사각형 선택(#261)도
   MouseManager 상태 추가로 구현했다 — 새 상호작용은 모드를 늘리고 **통지만** 하며, 집합·표시의
   소유는 소비처에 둔다. (Docs/Core/MouseManager.md)
2. **데이터 파이프라인**: 게임 수치는 CSV(`Assets/Resources/DataTables/`) → DataTableManager → SO
   패턴. 새 데이터 타입은 `XxxData`(POCO)+`XxxAsset`(SO)+`XxxTable` 템플릿을 따른다.
   Get 계열 null 반환 → 호출부 null 체크 필수. (Docs/Tools/DataTableManager.md)
3. **자원 흐름** (GDD §3.2): 기본 자원(나무/철/식량) = 주민 배치 생산 **또는 영토 확장 보상**, 마나석 =
   영토 확장·전투 보상에서만.
   - **방향 전환(GDD v0.3)**: **미개척 영지 자원**(영토 해금)은 주민 배치 없이 **매일 정산마다 일정량이 자동
     수급**된다(영토 확장 보상의 일종) — 영토 해금이라는 정당한 원천이므로 계약 위반 아님. (직전 '식량 소모 →
     확장 자원 변환' 모델은 폐기, WL-042 참고.)
   - **마나석 교환(#211, WL-042 해소)**: **연금술사의 집**(`BuildingType.Store`)이 마나석 → 자원 7종 **단방향** 교환을 제공한다
     (낮 전용, 역교환 없음). 이것은 새 획득 경로가 **아니라 마나석 소비처**다 — 지갑의 획득 API(`ResourceWallet.Add`)는
     계속 비공개이고, 소비처에 열린 것은 **차감+지급이 한 트랜잭션인 `ManagementController.TryExchange` 단일 진입점**뿐이라
     마나석 없이 자원이 생기는 경로가 없다. 신규 기능이 자원을 늘려야 할 때도 `Add`를 public으로 열지 말고 이 패턴을 따를 것.
   - **본진 업그레이드 비용(#229, WL-042 완전 종결)**: 자원 종류 자유 authoring(현재 나무·철·마나석), 차감은 동일한 `TrySpend` 경유 — 지갑을 늘리지 않는 **순수 소비 경로**라 계약 위반이 아니다.
4. **공간 분리** (GDD §4.1/§6.2): 경영 공간 = 건물, 전투 공간 = 타워. 두 영토는 독립 관리 —
   한쪽 확장이 다른 쪽 상태에 의존 금지.
5. **낮/밤 전환 계약** (GDD §5, Build0 계획): 낮 시작=본진 회복, 밤 시작(`OnDayToNight`)=전투 스테이지 확장+몬스터 스폰(#17), 밤→낮=주민 배치 기반 자원 정산(먼저)+
   주민 배치 초기화(그 다음)+웨이브 증가(모두 `OnNightToDay` 시점, #66). 페이즈에 반응하는 시스템은
   전환 이벤트 훅 구조여야 한다. (Docs/Core/DayNightManager.md)
6. **책임 경계** (MouseManager.md §2): 배치 판정=그리드/검증 시스템, 자원 차감=경영 시스템,
   정보 표시=UI. MouseManager는 선택 사실만 통지.
7. **문서-코드 동기화**: 시스템 구현·변경 PR은 해당 Docs/ 문서 갱신 포함 필수. (일치 여부 자체는
   설계 검증이 아님 — 갱신 포함 여부만 확인)
8. **저장소 배치** (CLAUDE.md): 스크립트 정본은 `Assets/Scripts/`(공간/시스템 폴더), 씬 등 비-스크립트 WIP는 `Assets/Personal/<이름>/`, `Assets/Imported/` 수정 금지.
   - **리뷰어 주석(Imported 사각지대)**: 씬/프리팹이 참조하는 유료·벤더 에셋(건물 프리팹, BaseGate 등)은 `Assets/Imported/`(중첩 git repo)에 상주할 수 있고 자동 리뷰 봇은 이를 읽지 못한다. 메인 repo diff에 `.prefab`이 없다고 해서 "프리팹 미생성/이슈 미충족"으로 단정하지 말 것 — 유료 에셋을 팀 공용 Imported 공간에만 두는 것이 정상 배치이며, 필요 시 작성자에게 확인한다(WL-040 참고, #92 건물 프리팹이 이 사각지대의 실제 오탐 사례).
   - **리뷰어 주석(죽은 사본)**: `Assets/Personal/SUNGSOO/Font/`는 폰트가 TMP 정본으로 이관되며 더 이상 참조되지 않는 죽은 사본이다 — 이 경로의 폰트 아틀라스 churn을 WL-041 재발로 보고하지 말 것(WL-041 참고, 삭제 대기 중).

## 5. 미합의 전역 계약 (합의 없는 변경·점유 = 최소 🟠)

- **레이어**: Enemy(7)/Soldier(8)/PlayerBase(9)가 `TagManager.asset`에 등재 완료(PR#80, WL-005 해소).
  단 각 스크립트(Tower/Soldier/Enemy)의 LayerMask vs Tag 방식 최종 확정은 TODO(TBD)로 남음.
  `TagManager.asset` 변경은 반드시 리뷰 대상.
- **좌표계**: MapBuilder는 battlespace 로컬 정수 그리드(MapSize=7), MouseManager/Combat은 월드 좌표.
  변환 유틸 없음.
- **네임스페이스**: `NorthLand.Combat`만 존재, 나머지 전역. asmdef 없음(전부 Assembly-CSharp).
- **매니저 수명주기**: static(DataTableManager) / DontDestroyOnLoad(MouseManager) / 씬 싱글톤
  (TowerInfoUI) 3종 공존. 부트스트랩 미결정. DayNightManager는 씬 싱글톤(DontDestroyOnLoad 없음)
  채택 — 경영/전투 공간이 한 씬에 공존해 씬 전환에 걸쳐 상태를 유지할 이유가 없다는 판단(WL-002 참고 사례).
- **에셋 로딩**: Resources.Load(DataTable)와 Addressables(Localization) 공존.
- **스탯 데이터 원본**: Combat의 `Tower.cs`/`Enemy.cs`가 `TowerAsset`/`EnemyAsset`(CSV 기반, 1절
  DataTable 상태 참고)을 직접 소비하도록 PR#80에서 이관 완료 — 옛 Combat 자체 SO는 삭제됨.
  잔여: 전 타워/적 종류 값 채움 + Soldier(`SoldierData`, 아직 Combat 자체 SO) 이관 (WL-001).
- **용어 '웨이포인트'**: MapBuilder의 StageWaypoint(블록 경계 연결점) ≠ GDD §6.4 웨이포인트(병사
  배치 지점) (WL-009).
- **용어 '스테이지'**: MapBuilder의 블록 단위 ≠ GDD의 런 단위 스테이지 (WL-009).

## 6. 확립된 컨벤션 (일관성 판단 기준)

- MonoBehaviour는 얇게(진입점), 로직은 생성자 주입 순수 C# 클래스로
- 실패 처리: `bool Try~(out/결과 객체)` + Debug.LogError 한국어 메시지 + null 반환(호출부 체크)
- `[SerializeField] private` 필드 기본, 프로퍼티는 expression-bodied (접두 `_camelCase` vs
  `camelCase` 혼재 — 통일 미결정)
- CSV POCO는 PascalCase 프로퍼티(CsvHelper), SO는 CreateAssetMenu
- 테스트: XxxTest.cs MonoBehaviour + 개인 테스트 씬 Play 확인 (유닛 테스트 없음)
- 커밋: `Feat|Fix: 한국어 요약 #이슈번호`
- **적 프리팹 mover 탐색 규약**: `MonsterMove`(`IMovementAgent` 구현)는 `Enemy` 루트 또는 그 자식
  GameObject 어디에 있어도 된다. mover를 참조하는 모든 지점이 `GetComponentInChildren`로 통일돼 있다
  (`Enemy.cs:56·72`, `MonsterSpawn.cs:327`, `MonsterStateMachine.cs:32`, `StatusEffectHandler.cs:51` —
  WL-111 A안 채택, main #212가 WL-093 수정으로 확정한 방향과 정합). 따라서 자식 GO에 둔 mover도
  이동·보스 램프·CC(슬로우/스턴)가 모두 정상 동작한다. 신규 적 프리팹은 이 탐색 규약을 벗어난
  이중 mover(루트+자식 동시 부착)만 피하면 된다.
- **Ghost 프리팹 규약(시각 전용)**: 배치 프리뷰 Ghost 프리팹에는 게임플레이 컴포넌트(`Tower`,
  `TowerReloadVisual` 등)를 붙이지 않는다 — 메시/머티리얼 등 시각 요소만. `MouseManager`가
  고스트를 컴포넌트 비활성 없이 `Instantiate`하므로(`MouseManager.cs:117`), live 컴포넌트가 실리면
  프리뷰가 실제 게임플레이를 실행한다. WL-066(Tower)·WL-110/WL-066 확장(구 AuraTower)의 정본 규약.
  **2차 방어선(#164 리팩토링)**: `Tower.Active` 등록 시점이 `OnEnable`이 아니라 `Build`로 옮겨져,
  조립되지 않은 인스턴스는 타워로 집계되지 않는다. 즉 규약을 어겨 고스트에 `Tower`가 실려도
  `data`가 없으면 아무 일도 일어나지 않는다 — 규약은 여전히 지켜야 하지만 어겼을 때의 피해가 없어졌다.
  오라 행동은 런타임 `AddComponent`로만 붙으므로 프리팹에 실릴 수 없다(구조적으로 불가).
  **현재 상태(2026-07-29 실측)**: 프로젝트 내 모든 Ghost 프리팹이 게임플레이 컴포넌트 0개로 규약 준수.
  이 정리에서 `HasteTowerTest-Ghost`·`PoisonTowerTest-Ghost`·`AuraTowerTest-Ghost`(live 오라 보유)와
  `GatlingShooter-GHOST`·`CannonTowerTest-Ghost`(live `Tower` 보유 — WL-066 종결 시 누락분)를 함께 정리했다.
  신규 타워 고스트 작성·프리팹 스왑 시 필수 확인(muchan/n0wst4ndup 게이트).
