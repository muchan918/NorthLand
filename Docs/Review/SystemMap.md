# SystemMap — 시스템 지도와 통합 계약 (PR 리뷰 필수 입력)

> **목적**: PR 리뷰 시 "이 변경이 누구의 어떤 시스템과 만나는가"를 판단하는 기준 문서.
> **갱신 규칙**: 시스템의 공개 API·계약이 바뀌는 PR은 이 문서를 **같은 PR에서** 갱신한다.
> 자동 리뷰 워크플로우(`.github/workflows/pr-review.yml`)가 매 리뷰마다 이 문서를 읽는다 —
> 낡은 지도는 리뷰 품질을 직접 해친다.

## 1. 시스템 및 소유자

| 시스템                                      | 소유자     | 경로                                                                 | 상태                                                                                                                                                                    |
| ------------------------------------------- | ---------- | -------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| DataTable (CSV→static 레지스트리→SO)        | muchan     | `Assets/Scripts/Data`                                             | Resource, Building, Tower, Enemy 4종 구현. Tower/Enemy는 Combat(`Tower.cs`/`Enemy.cs`)이 `TowerAsset`/`EnemyAsset`을 직접 소비하도록 이관 완료(PR#80) — 잔여 종류 값 채움 + Soldier 이관은 진행 중(WL-001, 부분 착수). Reward 확장 예정(Territory는 #337에서 삭제). **Skill(#103)은 CSV 파이프라인을 쓰지 않기로 확정** — 밸런싱 수치 미정 + 스킬 1~2개뿐이라 과설계로 판단, `PlayerSkill` 시스템 행 참고                                |
| Combat (타워/몬스터 공격·데미지)            | SUNGSOO    | `Assets/Scripts/CombatSystem` | 공격/데미지 코어만. 이동·사망처리·투사체 없음. HP 조회 공개 API(`CurrentHp`/`MaxHp`/`OnHpChanged`) + `PlayerBase` 씬 싱글톤(`Instance`/`OnBaseSpawned`) 추가(#100, HP UI 연동용). `Tower.cs`에 PlayerSkill(#103, muchan)이 버프 배율과 자가 등록 정적 리스트 `Tower.Active`를 추가함. **#164 리팩토링으로 Tower.cs가 재구성됨(n0wst4ndup)**: 구 `AuraTower`를 폐기하고 **타워를 단일 `Tower` 타입 + 행동 조립(`ITowerBehaviour`)** 구조로 통합. 공격 로직은 `AttackBehaviour`로, 오라는 `BuffAuraBehaviour`/`DebuffAuraBehaviour`로 이관. 버프 배율 필드(`damageMultiplier`/`attackSpeedMultiplier`)와 `activeBuffs`는 스탯 원장 `TowerStats`로 통합(타일 버프까지 흡수). `Tower`가 소유하는 것은 정체성(SO/진영)·원장·선택 표현·레지스트리·게이팅뿐이며 "무엇을 하는 물건인지"는 전부 행동이 가진다. 공개 API 상세는 2절 참고. `Projectile.cs`에 PlayerSkill(#169, muchan)이 static 명중 이벤트 `Projectile.DamageDealt(IAttacker, IDamageable)` 추가(단일/스플래시/체인 데미지 4지점 직후 발행, 순수 추가 — 기존 로직 무수정. static이라 구독 해제는 구독자 책임, 현재 구독자는 `BurnBuff`). **`Tower.cs`에 TowerFusion(#195, muchan)이 읽기 접근자 `Asset`(=data) 추가 — 순수 읽기(배치된 타워의 원본 SO 조회, 합성 재료 TowerID 매칭용), 기존 로직·필드 무수정**. **#300에서 성장(램프업) 축이 추가됨(SUNGSOO)**: 전투 실적이 원장에 얹히는 첫 소스 — `RampAction`(신규 액션)·`RampProfile`(수치 부품)·`Enemy.Killed`(처치 귀속 통지)·`TowerAction.OnWaveEnd`(웨이브 종료 훅) 4종이 공개 계약에 추가됐다(2절 참고). `BeamAction`에 대상별 램프(`Beam.LockRamp`)가 붙었고, 낮에도 빔이 켜진 채 남던 #298 버그를 `OnWaveEnd`로 해소했다. 기존 액션·원장 규칙은 무수정. **#336에서 착탄 지속 구역·연발·포탑 조준 축이 추가됨(SUNGSOO)**: `GroundZone`(신규 컴포넌트 — 착탄점에 남아 반경 안의 적에게 `Effects`를 재적용, 신규 런타임 인프라 0) · `Projectile.Impacted`(착탄 위치 통지, **인스턴스** 이벤트라 해제 책임 없음) · `Attack.BurstCount`/`BurstInterval`(한 조준으로 시간차 연발 — 산탄과 다른 축) · `TowerTurretAim`(연출 전용) 4종이 공개 계약에 추가됐다(2절 참고). **대상 탐색이 `Tower.AcquireTarget`으로 이관됐다** — `AttackAction.FindTarget`이 삭제되고 호스트가 프레임당 1회 캐시로 소유한다(액션과 연출이 원점·반경·마스크가 같은 쿼리를 두 벌 돌리던 것을 합침). 밤/낮 신호는 `Tower.IsCombatPhase`로 읽기 전용 공개(연출이 `DayNightManager`를 각자 폴링하지 않게 함, WL-044). ⚠ **`AcquireTarget`은 `AttackRange`에 고정돼 공격 액션 없는 타워엔 항상 null이고 `BeamAction`의 자체 탐색이 남아 있다**(WL-178). **#387에서 조준 정책 축이 추가됨(SUNGSOO)**: "사거리 안의 누구를 겨누는가"가 하드코딩 최근접에서 **저작·전환 가능한 정책**이 됐다 — `TargetingPolicy`(`[SerializeReference]` 부품 5종: 앞선/뒤처진/가까운/체력 높은/체력 낮은 적) · `ITargetProfile`(체력·경로 진행도 조회) · `ITargetingSelector`(인게임 전환 창구) · `TowerAsset.Targeting` 4종이 공개 계약에 추가됐다(2절 참고). **스캔 루프는 `Tower.FindTarget` 한 곳 그대로다** — 모든 정책이 "점수 하나를 최대화"로 환원되어 정책은 점수 함수만 갖는다(#336이 만든 단일 출처가 정책을 늘려도 갈라지지 않는다). ⚠ **기본값이 최근접 → 앞선 적으로 바뀌었다(의도된 거동 변경)** — 저작이 빈 SO 21종 전부가 함께 바뀐다, `CombatBalance.md` §6.6. ⚠ **대상 고정의 소유자가 호스트가 아니라 `AttackAction`이다** — `AcquireTarget`은 매번 정책 1위를 답하고, 고정은 연발 사이클 내부로 한정된다(죽을 때까지 붙들면 1위가 자주 바뀌는 정책이 동작하지 않는다, 실측). 밸런싱 수치는 이동속도 기준 확정 대기로 미룸 |
| BattleMapBuilder (절차적 전투 맵)           | SUNJIN     | `Assets/Scripts/CombatSpace/MapBuilder`                          | 7×7 블록 경로 생성 구현. 싸이클 버그 해결이 다음 빌드 목표      
| MonsterMovement (지상/공중 경로 이동)       | SUNJIN     | `Assets/Scripts/Monster/MonsterMoveMent`, `Assets/Scripts/CombatSystem/IMovementAgent.cs` | `IMovementAgent`에서 경로 추종 계약을 `IRouteMovementAgent`로 분리. 지상은 `MonsterMove`, 공중은 `FlyingMonsterMove`가 구현한다. 공중 이동은 기존 경로를 일정 간격으로 샘플링하고 고도 오프셋을 적용해 선택된 지점 사이를 직선 비행한다. 이동속도 다축 합성은 순수 C# `MoveSpeedComposer` 한 곳에서 계산하며 두 이동 컴포넌트가 위임한다. `Enemy`·`MonsterSpawn`·`MonsterStateMachine`은 구체 이동 타입이 아니라 인터페이스를 소비한다(#209). **#387에서 `IRouteMovementAgent`에 `RemainingRouteDistance`가 추가됨(SUNGSOO)** — 타워 조준 정책의 "앞선 적 / 뒤처진 적" 판정 근거다(2절 참고). 계산은 순수 C# `RouteDistanceTracker`가 소유하고 지상·공중 두 구현이 위임한다(`MoveSpeedComposer`와 같은 축). ⚠ **새 mover를 만들 때 "경로를 모르면 `NaN`" 규약을 지킬 것** — 0이나 무한대로 대신하면 그 몬스터가 모든 타워의 1순위(또는 최후순위)로 고정된다. |
| MouseManager (입력/선택/배치)               | n0wst4ndup | `Assets/Scripts/GameManager/MouseManager`                            | **5상태** 머신 구현(Idle/BoxSelect/**UnitDrag**/Placement/SkillTargeting — #103 SkillTargeting, #261 BoxSelect, 주민 드래그로 UnitDrag 추가). Snap 항등·CanPlaceAt 항상 true (TODO). 스킬 타겟팅은 전투 타일 전체 허용(`CombatMapTileView` 유무 질의, 도로 전용 제한 제거). 단일 선택 확정은 press가 아니라 **release** 시점(#261). **`BoxSelect`와 `UnitDrag`는 형제이며 진입 시점에 배타적으로 갈린다** — 누른 순간 커서 밑에 `IDragHandle`이 있었는가가 유일한 기준(MouseManager.md §5.4). 그래서 **끌 수 있는 대상 위에서는 사각형 드래그를 시작할 수 없다**(의도된 대가) |
| TowerVfx (타워 등장·합성 소모 연출, #264/#265) | n0wst4ndup | `Assets/Scripts/CombatSystem/Vfx` | **등장(`TowerSpawnEffect`)**: 배치 확정 시 입자 수렴 → 타워 등장 팝 → 바닥 링. **소멸(`TowerDissolveEffect`, #265→#281)**: 타워가 하얘지며 중심으로 수축 → 입자로 폭발한 뒤 모드로 갈린다 — `Merge`(합성 소모: 재료 자리 상공 부유 → 확정이면 결과 타워로 수렴 / 취소면 제자리 역수렴 + 재조립 팝) / `Disperse`(배치 되돌리기: 사방 발산 소멸) / `Rewind`(합성 되돌리기: 가루가 재료 자리로 갈라져 이동 + 재료 팝 복원). 두 연출은 부품(`GrainSwarm` 알갱이, `VfxScaleHold` 스케일 점유)과 **시간 축**(`ConvergeDuration`·`PopDuration`)을 공유한다 — 유입 입자가 등장 팝보다 늦게 도착하면 "재료가 모여 타워가 됐다"가 성립하지 않기 때문. 합성 연출은 커맨드(#263)가 재료를 즉시 비활성화하므로 **소모 직전에 뜬 시각 사본**으로 독립 재생한다(로직 무결합, 연출이 죽어도 합성은 멀쩡). 이하는 등장 연출 기준: **대상을 모르는 범용 연출** — `Transform` + 풋프린트 크기만 받고 `Renderer.bounds`/`localScale`만 읽어, 타워 에셋이 교체돼도 코드 수정이 필요 없다(에셋 교체 회귀 검증 통과). 수치 앵커는 둘로 나뉜다: **크기·바닥 링은 풋프린트(논리, 에셋 무관 불변) / 개수·구름 모양은 bounds(시각)**. ⚠ **재생 중 대상 루트 `localScale`을 배타적으로 소유**하므로 그 창(약 0.45초 + 과도기 0.28초) 동안 스케일을 쓰거나 캡처하는 시스템이 있으면 깨진다 — 이 계약 때문에 `RangeCircle`의 보정 시점이 함께 바뀌었다(2절). 시간은 `unscaledDeltaTime`(일시정지 중 타워가 투명하게 멈추는 것 방지, WL-100). **룩·수치는 전부 임시(아트 TBD)** — 타워 에셋이 임시이고 아트 방향이 미정이라, 검증 통과와 설계 확정은 별개다. 명세 `Docs/Core/TowerPlacement.md` §9.3, `Docs/Core/TowerMerge.md` §9.2 |
| PlayerSkill (플레이어 스킬, #103)           | muchan     | `Assets/Scripts/Skill`                                                | 클릭 시전 감전 스킬(기본 스킬 1종). 밤 게이팅(`Tower.cs`와 동일하게 `DayNightManager.CurrentPhase` 직접 폴링)·충전(탄약, #319)·범위 데미지(`IDamageable`/`DamageInfo` 재사용, 새 데미지 경로 없음). 수치는 CSV가 아니라 `SkillManager` 인스펙터 직접 입력(WL-015와 같은 축). **버프 스킬 제거됨(#315)** — 한때 2번째 스킬(`BuffSkillManager`, 전체 타워 공격력·공속 배율)이 있었으나 조준도 타이밍 판단도 없어 "쿨 차면 누른다"가 유일한 최적 플레이라 삭제. **씬·프리팹·보상 풀에서 배선만 끊었고 C# 4개 파일(`BuffSkillManager`/`BuffCastContext`/`BurnBuff`/`BuffSkillButtonView`)은 저장소에 그대로 남아있다 — 코드는 존재하지만 미사용**이며 `BuffSkillManager.Instance`는 항상 null이다(§2 공개 API 참고). 다시 배선하지 말 것, 되살리려면 기획 재검토 선행(GDD §5.5). 보상 기반 특수효과 업그레이드(#169, 레벨 중첩) 진행 중 — **이벤트 구독 구조**(이슈 원문의 "enum+중앙 컨트롤러" 방침에서 변경): `SkillManager`가 임팩트마다 `ImpactResolved(SkillCastContext)` 이벤트 발행(효과 존재 모름, 구독자 0이면 기본 감전만). 특수효과는 추상 `SkillEffect`(MonoBehaviour, `SkillEffectManager` 오브젝트에 부착) 파생 — 레벨 0→1 시 스스로 이벤트 구독, 재선택은 `Level` 변수만 가산, 파괴 시 해제. `SkillEffectManager`는 라우터로 축소(`ApplyReward`→타입 매칭 효과에 위임, `GetLevel` 조회). `SkillCastContext`(착탄 위치·맞은 적 버퍼, 읽기 전용)로 정보를 전달한다. 레벨을 다른 시스템에 밀어야 하는 효과는 `SkillEffect.OnLevelChanged` override로 획득·복원 두 경로를 한 곳에서 받는다(#319). **화상(`BurnEffect`) 구현 완료** — 대상의 `StatusEffectHandler.ApplyOrRefresh` 재사용(AuraTower 패턴, effectId=`"skill_burn"` 해시, Combat 무수정), 틱 데미지 = 레벨 × 인스펙터 수치. **폭탄(`BombEffect`+`SkillBomb`) 구현 완료** — 착탄 지점에 `Assets/Prefabs/Skill/SkillBomb.prefab` 설치 → 지연 후 반경 폭발(감전과 동일 LayerMask/DamageInfo 규약, **범위 판정은 #398 이후 `SkillHitScan` 공유**), 폭발 데미지 = 레벨 × 인스펙터 수치. **추가시전(`CountEffect`) 충전형 재설계 완료(#319)** — 감전이 쿨다운 대신 **충전(탄약)**을 쓴다. `SkillManager`가 `charges`를 소유하고 시계 **하나**로 `effectiveCooldown` 간격마다 1발씩 채우며(롤 티모 버섯 규칙 — 시계가 둘이면 최대 충전과 무관하게 회복이 두 배가 된다), 밤 시작에 만충으로 리셋한다. `CountEffect`는 `OnLevelChanged`에서 `SetBonusCharges(Level)`만 호출해 최대 충전을 올린다(총 1+레벨발) — 자체 타이머·이벤트 구독·`HandleImpact`가 없다. 예전의 "같은 자리 자동 반복 착탄"은 조준 판단을 없앤다는 이유로 폐기(#315와 같은 축). **버프 화상(`BurnBuff`)은 버프 스킬과 함께 미사용(#315)** — 컴포넌트가 씬에서 제거되고 `BuffBurnReward`가 `WaveRewardPool`에서 빠져 뽑히지 않는다(enum 값 `WaveRewardType.BuffBurn`·SO·클래스는 보존). 이것이 확립한 **`SkillEffect.TrySubscribe`/`Unsubscribe` override로 구독 대상을 바꾸는 확장점은 살아있으나 현재 실 사용처가 0이다.** 새 효과 추가 = `SkillEffect` 파생 1개 + 씬 컴포넌트 부착(스킬·매니저 무수정). **웨이브 종료 취소(#200)**: `SkillBomb`(지연 폭탄)·`SkillField`(장판)가 `DayNightManager.OnNightToDay` 구독 → 밤→낮 시 진행 중 효과 취소(낮 잔존 발동 방지). 감전 자체에는 예약된 지연 발동이 없어 `SkillManager`는 취소 대상이 없다(#319 — `OnDayToNight`는 충전 만충 리셋용으로만 구독). 조준 모드 취소는 `PhasePanelSwitcher`가 `OnDayStart`에서 담당(기존). **마법 연구소 기본 스탯 배율 강화 구현 완료(#205)** — `SkillManager`가 `magic_lab` `BuildingAsset` 참조 + `ManagementController.GetUpgradeLevel`로 레벨을 pull, 레벨→배율 매핑은 `BuildingAsset.Skill.UpgradeLevels`(SO, 도달 비용과 같은 리스트, WL-015와 같은 축)에 authoring — 씬에는 배율 데이터가 없어 밸런싱이 `GameScene.unity`를 안 건드린다(PR#216 리뷰 반영). 시전 시점 base damage/radius/cooldown에 배율로 적용(`SkillUpgradeLevel`의 버프용 배율 4종은 소비처가 사라져 무의미 — #315) — 보상 기반 특수효과(`SkillEffect.Level`, 위) 축과는 완전히 독립, 이벤트 구독 흐름 무수정(`PlayerSkill.md` §3.1). **레벨별 착탄 이펙트 교체 구현 완료(#206)** — 연구소 레벨이 기본 스탯 배율에 더해 감전 **착탄 이펙트 프리팹**도 바꾼다. 매핑은 `magic_lab.asset`이 아니라 별도 SO `SkillVisualSet`(`Assets/Resources/ScriptableObjects/Skill/`)이 소유하며, `FromLevel` 기반 **희소 매핑**이라 레벨 개수를 맞출 의무가 없다(배율은 도달 비용과 같은 리스트여야 하지만 이펙트는 레벨마다 하나씩 있을 필요가 없어 요구 조건이 다르고, 데이터 SO에 뷰 에셋 참조를 섞지 않으려는 의도). `RefreshUpgrade`에서 엔트리를 캐싱(`_currentVisual`)하고 `ApplyImpact`이 스폰 — 세트 미배선/엔트리 없음이면 기존 `impactEffectPrefab`으로 폴백해 도입 전과 동일 동작. 엔트리별 `ScaleWithRadius`로 `effectiveRadius/radius` 비율 보정(조준 인디케이터 반경과 어긋남 방지). **스킬은 즉발형 유지** — 시전 흐름(`CastAt`/`ImpactResolved`) 무수정이고, 낙하·메테오처럼 이동+지연 데미지가 필요한 연출은 보상 특수효과 축(`SkillEffect` 파생, `BombEffect`+`SkillBomb` 패턴)의 몫으로 남겼다. 이펙트 프리팹은 파티클 `Stop Action: Destroy` 필수(자식이 하나라도 `Looping`이면 미발동 — 루트+자식이 모두 끝나야 트리거). 상세: `PlayerSkill.md` §3.2 |
| WaveReward (웨이브 클리어 3택1 보상)        | SUNJIN     | `Assets/Scripts/Reward`                                               | 3택1 선택 UI(`WaveRewardSelectionUI`, timeScale 0 정지 + UniTask 대기)·랜덤 추출(`WaveRewardPool`)·웨이브 클리어 트리거(`WaveCompletionCoordinator`) 배선 완료(#132/#133, PR#150). **카드 뷰는 프리팹 + `RewardCardView`로 분리(#320)** — `WaveRewardSelectionUI`는 후보 수만큼 카드 프리팹을 `cardContainer` 아래 `Instantiate`하고 `Bind`만 호출하며, 닫힐 때 파괴한다(`TowerMergePanelView`의 `_candidateButtonPrefab` 패턴과 동일). 도입 전에는 씬에 `Reward1~3`을 고정 배치하고 요소별 **평행 배열 6개**(`rewardButtons`/`rewardCards`/`nameLocalizers`/`descriptionLocalizers`/`iconImages`/`levelStatTexts`)를 같은 `i`로 인덱싱했다 — 배열 순서가 어긋나면 예외도 경고도 없이 엉뚱한 카드에 값이 들어갔고, 카드에 요소 하나를 추가할 때마다 배열 1개 + 씬 배선 3개가 늘었다. 카드 개수도 씬 배선이 아니라 후보 수가 정한다(`HorizontalLayoutGroup`이라 후보가 3장 미만이어도 빈 슬롯 없이 가운데 정렬 — #292 만렙 제외 시 실제 발생). **등급 표시(#320, 규약 개정 #353)**: 카드는 두 가지를 동시에 말한다 — **지금 보유**는 채워진 별(`GetLevel()` 개수)과 레벨 줄 왼쪽이, **고르면 도달**은 그 다음 한 칸의 미리보기 별과 카드면 스프라이트(`GetNextLevel()`)와 레벨 줄 오른쪽이 맡는다. 미리보기 칸은 `starOn`을 알파 0.45로 그리며 프리팹 배선이 필요 없다(RGB는 프리팹 값을 유지하고 알파만 교체). 도입 시엔 별도 `GetNextLevel()` 기준이었으나("고르면 몇 레벨이 되는가"), 레벨 줄이 "Lv 0 → Lv 1"로 현재값을 함께 보여주면서 미보유 카드에 별이 1개 켜진 채 떠 한 장이 두 레벨을 주장했다(#353). **카드면은 `GetLevel()`로 내리지 말 것** — 만렙은 후보에서 빠져 카드에 뜨는 현재 레벨이 0~2뿐이라 `faces[level-1]`은 Lv0·Lv1이 같은 카드면이 되고 최고 등급이 한 번도 나오지 않는다. **등급 표현은 색 틴트가 아니라 등급별 스킨 스프라이트 교체이며(#356), 레벨→스킨 매핑은 카드 프리팹의 `levelSkins` 배열 순서가 소유한다**(코드는 색 이름을 모르고 인덱스만 안다). **한 등급 = 스킨 한 벌**(`LevelSkin`: `face` 카드면 · `namePlate` 이름칸 · `descPlate` 설명칸 · `iconFrame` 아이콘 틀)이라, 등급이 늘어도 배열 길이 4개를 따로 맞출 필요가 없다. 아트는 `@NorthLand/Prefabs/RewardCard/**Sprites/**` 아래이고 색 순서는 **Green → Purple → Yellow**(레벨 1·2·3). 카드면 3장은 `RewardCard.png` 시트(각 600×720)로 **9-slice 보더가 없어 0.833 비율을 벗어나면 왜곡된다** — 카드 RectTransform은 400×600이라 세로로 늘어난 상태이며, 필요하면 `Preserve Aspect`나 500×600으로 대응한다. 카드 전체가 버튼이라 `Button.transition = None`으로 둔다(ColorTint면 버튼이 `cardFace.color`를 덮어써 코드 소유가 깨진다). **카드 내부 세로 배치는 앵커가 아니라 `CardImagePanel`의 `VerticalLayoutGroup`이 소유한다**(padding 32/32/24/24, spacing 12) — 각 행 높이는 `LayoutElement.preferredHeight`(Name 72 / IconFrame 96 / Description 240 / Level 72)로 정하고, **앵커를 손으로 맞추지 말 것**(VLG가 덮어쓴다). 아이콘은 `IconFrame` 자식이고 여백은 프레임의 VLG padding(16)이 소유한다. `IconFrame`의 VLG는 `childControl/ForceExpand W/H = false`라 `Icon`이 자기 크기(64×64 정사각)를 유지한다 — 이 넷을 켜면 아이콘이 프레임 폭 전체로 늘어난다. ⚠ 카드 프리팹은 별도 저장소(`NorthLand-Imported`)의 `@NorthLand/Prefabs/RewardCard/Card.prefab`이라 **두 저장소를 함께 머지해야 한다**(WL-160과 같은 축). 그래서 프리팹 쪽에 계약 2건이 걸린다(#353) — (1) **별 칸 수 = `SkillEffect.maxLevel`**(칸은 프리팹, 상한은 `GameScene` 인스펙터 소유라 서로를 모른다. 상한만 올리면 마지막 카드가 별 만땅+미리보기 없음인데 레벨 줄만 `Lv 3 → Lv 4`가 되어 규약이 깨진다 — `ApplyStars`가 칸 부족을 `LogWarning`으로 드러낸다), (2) **별 알파는 코드 소유 — 프리팹에서 authoring 금지**(꺼진 별을 알파로 표현하면 `ApplyStars`가 조용히 덮어쓴다. 켜짐/꺼짐 구분은 `starOn`/`starOff` 스프라이트로 할 것), (3) **`levelSkins` 벌 수 = `SkillEffect.maxLevel`**(#356, 별 칸과 같은 축 — 모자라면 `ApplySkin`이 `LogWarning`을 내고 마지막 스킨으로 클램프한다). `WaveRewardController.GrantReward`는 로그 + `SkillEffectManager.ApplyReward` 호출(#169 1단계, 매니저 없어도 동작). `WaveRewardType`(enum 6값 `Burn`/`Bomb`/`ExtraCast`/`BuffBurn`/`Field`/`Execute` — 전부 스킬 특수효과. `BuffBurn`은 enum·SO 모두 남아있으나 버프 스킬 제거로 풀에서 제외돼 **활성 5종**, #315)별로 매니저가 레벨 누적. **현재 활성 5종 = 카드 3, 레벨 상한 3** — 만렙 1개까지는 남은 4종 중 3장이 뽑혀 3택1이 성립하고, **만렙 2개**부터 매 웨이브 같은 3장으로 고정된다(GDD §5.6 — 완전 해소 조건은 상한+3 = 6종이라 1종 미달). 타입 확정·`NorthLand_Rewards` 로컬라이즈 키 정리는 #169 후속 단계(WL-043) |
| Localization                                | n0wst4ndup | `Assets/Scripts/Localization/LocalizationHelper.cs`, `Assets/Localization/*`(String Table 컬렉션), `Assets/Scripts/Test/LocalizationTest.cs` | String Table 4종(`NorthLand_default`/`NorthLand_buildings`/`NorthLand_Enemies`/`NorthLand_Towers`, ko-KR/en-US/ja-JP) 구축. 본진 효과 문구는 **레벨별 키 규약** `castle.effect.lv{n}`(n=도달할 표시 레벨)를 쓴다 — 무엇이 해금되는지가 레벨마다 달라 코드는 키만 조립하고 문구는 테이블에서 authoring한다. Building/Enemy/Resource/Tower CSV 표시 문자열은 키로 이관 완료(WL-013 해소, PR#126 — 신규 `poison_tower` 행 포함). `LocalizationHelper`(static 동기 pull 헬퍼) 신설 — 호버 툴팁 등 '호출 시점 1회' 풀 경로 전용, 지속형 표시는 `LocalizeStringEvent`/`LocalizedString.StringChanged` 사용. 전투 공간(TowerInfoUI) 표시 배선은 후속(#102) |
| PlayerSave / RunSave (플레이어 슬롯·Run 저장·이어하기, #270·#342) | sunjin1222 | `Assets/Scripts/SaveData` | **플레이어 슬롯 3개 × 슬롯당 Run 1개**. `PlayerSaveService`가 슬롯 생성·선택·삭제와 마지막 선택 슬롯 복원을 소유한다. 각 슬롯은 `Application.persistentDataPath/SaveSlots/slot-{index}/` 아래 `player.json`과 `run-save.json`을 독립 저장하고, 공통 설정은 슬롯 밖 `settings.json`에 저장한다. 슬롯 표시 이름은 저장하지 않고 현재 로케일에서 조립한다. `RunSaveManager`는 선택 슬롯을 선행 조건으로 삼고 복원 순서(시드/맵 생성 → 경영 → 맵 공개 → 타워 → 본진/보상 → 페이즈)를 중앙에서 소유한다. 구버전 루트 `run-save.json`은 선택 슬롯에 기존 Run이 없을 때만 이전하며, 대상 저장 성공 후 원본을 삭제한다. 세 파일은 `{ version, data }` 봉투와 파일별 버전·마이그레이션 체인을 사용한다. 상세 계약은 `Docs/Core/SaveSystem.md`. |
| DayNightManager (낮/밤 상태·전환 이벤트 훅) | muchan     | `Assets/Scripts/DayNight`                                    | 상태 관리 + 전환 이벤트 훅 구현. 자원 정산/주민 배치 초기화는 `Management(Resource)`가 구현(#66), 본진 회복은 미구현(소유 시스템 대기). 밤→낮 트리거는 임시 UI(`NightActionPanelView`의 "웨이브 성공" 버튼, #66)가 `EndNight()` 직접 호출(웨이브 클리어 로직으로 교체 예정, WL-018) |
| DayNightLighting (낮/밤 룩·전환 연출, #7·#136·#101) | muchan(#7) · N0WST4NDUP(#136·#101) | `Assets/Scripts/DayNight`, `Assets/Shaders/DayNight`, `Assets/Settings/NightLookProfile.asset` | **적용부/구동부 분리**. `DayNightLightingController`(적용) = Directional Light·Ambient(Trilight)·Skybox·`NightVolume.weight`·물 틴트(MPB). `StreetLampController`(적용) = 마을 가로등 31개. `DayNightTransition`(구동, #101) = UniTask로 위 둘의 `ApplyBlend`/`SetBlend`와 `Night Wipe` 풀스크린 패스를 함께 몬다. 두 적용부는 `subscribeToPhaseEvents`가 **꺼져 있고**(정본 씬) 전환이 단독 구동 — 켜면 이벤트에 직접 반응해 스냅으로 찍혀 이중 적용된다. ⚠️ **셀셰이딩(FlatKit) 씬이라 라이트·앰비언트로는 밤이 만들어지지 않는다** — 라이트 강도를 1/4로 내려도 화면 평균 휘도가 낮의 75%→73%에 그쳤고, `ColorAdjustments`를 얹어야 32%가 된다(실측). 그래서 밤의 어둡기·색은 밤 전용 볼륨(`NightLookProfile`, priority 2)이 만들고 **라이트는 오히려 밤에 높게(0.4→0.9) 유지**해 형태·전투 가독성을 담당한다. 같은 이유로 **가로등도 강도가 아니라 사거리가 인상을 결정**한다. 포그는 오쏘 카메라라 깊이 그라데이션이 안 생겨 미채택. 언릿(물·이미시브 사고 머티리얼)은 화면공간 그레이드만으로 부족해 별도 보정. 상세 `VisualLookPipeline.md` §3.3.1, `DayNightManager.md` §6·§6.1 |
| Management(Resource) (자원 지갑·생산처)     | n0wst4ndup | `Assets/Scripts/ManagementSpace`                              | 지갑·생산처(#42) + 경영 패널 UI·DayNightManager 낮/밤 루프 연동(#43, #66). 정산+주민 배치 초기화=OnNightToDay(정산 먼저). **밤→낮 전환은 이제 밤 전용 임시 UI(`NightActionPanelView`)의 "웨이브 성공" 버튼이 트리거(WL-018)** — 경영 패널(`RequestAdvancePhase`)은 낮→밤(`EndDay`)만 담당. 주민 수는 placeholder(주민 시스템 부재). 소비처·마나석 생산 후속. **🗑 확장 자원 라인 폐기(#337)**: #166의 미개척 영지 특수 자원(금/루비/사파이어/다이아) 자동 수급이 영토 시스템째 삭제됐다 — `SupplyDaily`·`Supply` row 모드·미개방 회색·활성 우선 재정렬 전부 제거. 패널은 **고정 4행**(나무·철·식량+마나, 동적 등록 아님): 마나는 +/- 숨김에 "+n"=`ManaPerWaveClear`. `ProductionLineView`에 Villager/Mana 모드. **자원명 텍스트는 아이콘으로 대체**(`ResourceIconTable` 조회, ProdRow `Img_Icon`) — 행 높이 80으로 4행이 스크롤 없이 들어간다. **지갑(보유량) 표기를 탑 바 → 각 행의 지갑 칸(`_balanceText`→ProdRow Wallet)으로 이관**(#166): 탑 바 `Wood/Iron/Food/Mana_hud` 비활성화, 주민 풀·페이즈만 탑 바 유지. **🔀 잔여 방향**: ②생산 건물 3종 업그레이드(#139 구현됨), ③탑 바 HUD 오브젝트 완전 삭제는 후속. **✅ 마법 연구소 업그레이드**: 생산 라인과 별개인 **업그레이드 전용 건물 트랙**(`_upgradeBuildings`)으로 구현 — 마나석 비용·레벨 추적 + 강화 효과(스킬 시스템이 `GetUpgradeLevel`로 레벨 참조, 결합도 최소)도 **구현 완료(#205)**. BuildingUpgrade.md §8. **✅ 건물 바로가기 패널(#390)**: 생산라인 위 버튼 6개 → `CameraController2.MoveTo` + `ZoomTo` + `MouseManager.SelectExternally`(클릭과 같은 경로라 패널 상호배타가 공짜로 따라온다). 목표 지점·줌 배율은 건물 `Obj_*`에 붙는 `BuildingFocusPoint`(`focusOffset`·`zoomSize`)가 든다 — **건물 피벗이 카메라가 설 자리와 달라**(같은 함정이 §MouseManager `IDragHandle` 항에도 있다) `MoveViewCenterTo`의 화면 중앙 보정으로는 맞지 않았고, 오프셋을 Play 중에 눈으로 맞춘 뒤 값을 회수하는 방식이다. 호버 이름 툴팁은 기존 `TooltipUI`와 **별개인 전용 패널**이고(검정 배경 + 커서 추적, 위치는 `MouseManager.PointerPosition`), 이름은 `BuildingTable` → `LocalizationHelper`로 **매 호버 조회**한다(런타임 언어 전환 반영). ⚠ **버튼 순번이 씬의 `EventTrigger`에 손으로 배선**돼 있어 `_entries` 배열 순서가 바뀌면 컴파일도 경고도 없이 엉뚱한 건물 이름이 뜬다 |
| ~~TerritoryGraph (경영 영토 확장)~~          | —          | ~~`Assets/Scripts/ManagementSpace/Territory`~~                  | **🗑 시스템 통째로 삭제됨(#337)**. 그래프 생성·클레임·하루 1회 게이팅·노드 비주얼(`TerritoryNodeStateVisual`/`VortexVisual`)·엣지 배 연출(`TerritoryEdgeShip`)·미개척 영지 자원 SO(`TerritoryDefinition`)가 모두 제거됐고, 그것이 해금하던 **특수 자원 4종(금·루비·사파이어·다이아)도 `ResourceKind`에서 삭제**됐다. 함께 사라진 것: `GameScene`의 `Territory` 오브젝트, 영토 세이브/시드(`TerritorySaveData`·`RunSeedDeriver.TerritoryTag`), 낮 종료 팝업의 "영토 미확장" 경고, `Docs/ManagementArea/TerritoryGraph.md`. 개인 폴더(`Assets/Personal/muchan|n0wst4ndup/Territory`)의 프리팹·씬은 팀원 WIP라 남겨 뒀다(Missing Script 경고 예상). **재도입 금지가 아니라 현재 게임에 없다는 뜻** — 되살리려면 이 행이 아니라 이슈 #337을 먼저 확인할 것 |
| TowerFusion (타워 합성/Merge, #194/#195/#183)          | muchan(데이터·실행) · n0wst4ndup(선택·패널 #183) | `Assets/Scripts/GameManager/MouseManager/TowerPlacement`(Wallet/Matcher/Controller), `Assets/Scripts/Data/Tower/TowerRecipe.cs` | 레시피 SO(`TowerRecipe`: 재료 TowerID별 개수→결과 `TowerAsset`+`ExtraCost`, CSV 미경유 인스펙터 손입력) + 포함 매칭(`TowerFusionMatcher`, 순수 함수) + `TowerPlacer` 재사용 배치. **후보 버튼 클릭 즉시 재료를 소프트 소모**(타일 `Release`+비활성화)하고 결과 고스트 배치 → 확정 시 `ExtraCost` 지불+재료 진짜 파괴, 취소 시 재료 원복(#263 커맨드 패턴, `IReversibleCommand`/`TowerMergeCommand`). **재료가 점유했던 타일에 결과를 놓을 수 있다** — 구 "확정 시점 소모" 때의 제약이 여기서 풀렸다(WL-077 후단). 결과=일반 `TowerAsset`(신규 CSV 행/SO). 타일 점유는 `TowerFootprint`(배치 인스턴스에 부착)가 소유하며 `OnDestroy`(파괴)와 `Release`/`Reoccupy`(임시 해제) 두 경로로 되돌린다. **선택/패널 UI(#183)는 명세 확정·구현 예정**: 코디네이터+마커(`IGroupSelectable`)로 멀티 선택(MouseManager 제네릭 유지), **집합=`TowerWallet` 단일 백킹 스토어**(이음매), 패널 스위처가 우측 패널 단일 권위(1개=`TowerInfoUI`/2개↑=합성 패널), 후보 버튼 활성=`TowerFusionMatcher.CanFuse`, 선택 해제는 빈 곳 클릭으로 처리하고 우클릭은 사용하지 않음(WL-073), 밤 전환 시 진행 중 배치까지 취소, **낮 전용**. 현재는 임시 `TowerWallet`(씬 타워 인스펙터 드래그)가 선택셋 스탠드인. **⚠ 네이밍**: 문서·기획=합성/Merge, 코드 접두=`Fusion`(리네임 별건). 단일 진실 원천: `Docs/Core/TowerMerge.md`(구 `TowerFusion.md` 폐기·이관 완료) |
| Command (되돌리기 커맨드·히스토리, #263/#281) | muchan | `Assets/Scripts/Command`(계약·히스토리), `Assets/Scripts/GameManager/MouseManager/TowerPlacement`(구현체 2종), `Assets/Scripts/UI/TowerPanel/TowerUndoButtonView.cs` | 낮 동안의 **타워 배치·합성**을 되돌린다(#281). `IReversibleCommand` 4단 트랜잭션 + static `CommandHistory`(LIFO 20, 씬 배선 없음). **경영 조작은 범위 밖**이고, 되돌릴 수 있는 것은 "방금 한 조작"뿐이라 **임의 철거 경로는 여전히 없다**(`Tower.md` §6 #1 미해소). Redo 없음. 명세 `Docs/Core/TowerPlacement.md` §7·§8, `Docs/Core/TowerMerge.md` §9.3 |
| BossAI (보스 BT 패턴 AI, #232/#233/#234/#235) | n0wst4ndup | `Assets/Scripts/CombatSystem/Enemy/AI`(`EnemyAgent`/`EnemyPatternMemory`/`EnemyNodeQuery`/열거형), `Assets/Scripts/CombatSystem/Enemy/AI/Nodes`(리프 노드 18종), `Assets/Scripts/Monster/MonsterAnimation`(`BossUpperBodyLayer`/`BossLocomotionBlend`/`BossAttackCadence`/`BossPatternVfx`), `Assets/Imported/@NorthLand/Particles/Boss/Tank`, `Assets/Imported/@NorthLand/Animations/Boss/Tank.controller`, `Assets/Imported/@NorthLand/Prefabs/Boss`, `Assets/Behavior/TankBossBehavior.asset`(그래프), `Assets/Imported/@NorthLand/Prefabs/Boss/Tank.prefab`(**정본 — 구 경로 `Assets/Prefabs/Monster/Tank.prefab`은 삭제됐다. §4.8 「Tank 정본 예외」**), `Assets/Resources/ScriptableObjects/Enemies/tank.asset` | 기반(#233)·리프 노드 세트(#234)·패턴 그래프(#235) 구현 완료. **패턴 4종 + 기본 진군이 Play에서 동작 확인됨** — P2(뒤쪽 잡몹→크롤+피해감소, 조건 해제 시 복귀) / P3(앞쪽 타워+잡몹→타워 `AttackInterval` 1→2) / P1 / P4 / 감속 파훼(`AddSpeedDebuff` 대체 검증: 감속 2중첩으로 충돌 피해 0). **애니메이션까지 완료**: 몸체를 캡슐에서 `Boss_Alien_01`(Humanoid)로 교체하고 **2레이어 AnimatorController**(Base 전신 + UpperBody 상체 마스크)를 붙였다. P2·P3·P4가 보스를 멈추지 않으므로 가드·봉인·소환은 **상체 레이어**에서 돌아 걸으면서 시전한다. 상체 레이어는 **기본 weight 0**이어야 한다 — Override 레이어는 클립 없는 `Empty` 상태에서도 마스크 범위를 점유해 팔이 얼어붙는다(실측: 걷기 팔 스윙 변화 0.00도, weight 0 대비 38도 어긋남). `BossUpperBodyLayer`가 레이어 상태를 보고 weight를 자동 페이드한다. 이동 모션은 플래그가 아니라 **실효 속도 기반 1D 블렌드 트리**(`BossLocomotionBlend`)로 고른다 — 돌진 플래그를 그래프가 켜고 끄던 구조에서 기본 진군 브랜치에 `IsCharging = true` 오타 하나로 보스가 내내 전력질주로 걸었다. 공격 모션은 `BossAttackCadence`가 `클립 길이 / Enemy.AttackInterval`을 재생속도로 흘려 1회 재생 = 1회 공격에 맞춘다(실측 2.5초 일치). **패턴 파티클도 BT가 아니라 애니메이터 상태를 따라간다**(`BossPatternVfx`) — BT에서 쏘면 상태 전이(0.15~0.25초)보다 먼저 터지고, 상태를 보면 지속 이펙트의 정지 배선을 빠뜨릴 수가 없다. **그래프는 파티클의 존재를 모르므로 VFX 추가에 그래프 배선이 따라오지 않는다.** 항목마다 레이어를 지정한다 — 컴포넌트가 한 레이어만 보면 `ChargeWindup`(전신 레이어)처럼 다른 레이어의 상태는 이름이 맞아도 영영 매칭되지 않고, 증상이 "파티클이 안 뜬다"라 원인이 파티클 쪽으로 보인다. 1회성 이펙트는 상태를 벗어나도 멈추지 않는다(`TowerSeal` 상태 1.2초 vs `NovaWater` 5.6초 — 멈추면 물결이 잘린다). 조명은 `ParticleSystem.IsAlive`로 파티클 수명을 따라가며, 여러 이펙트가 왼손 조명 하나를 공유하므로 **조명 기준 논리합**으로 판정해 켜기/끄기 경합을 없앤다. **Play 검증 완료**: 프리팹 스케일·콜라이더 확정(모델 ×6 · 콜라이더 height 10), P1 충돌 후 보스 생존(`P1_ArriveDistance` 5에서 경로 끝 파괴 회피 후 근접 공격 전환), 공격 모션 ↔ 공격 속도 일치, 상하체 분리. **잔여**: 감속 파훼 인게임 검증. 디버그 서클 4개는 제거됐고(노드 60 → 51, 남은 서클은 P3 예고 원 하나뿐이며 그건 게임 요소다) 보스는 **웨이브 7**에 편성돼 있다. **수치는 실측 관측으로 조정한 값이며 밸런싱 전까지 유지한다.** 패턴 수치는 그래프 Blackboard 변수 44개로 authoring(WL-094 해소). 보스는 `EnemyTable.csv`에 `tank` 행으로 등재돼 CSV 파이프라인 안에 있다(importer는 기존 SO의 `EnemyID`/`EnemyType`만 덮어써 손입력 `Boss.Stat`·`BehaviorTree`를 보존한다 — `TableImporter.ImportEnemy`). `EnemyAgent.unitLayerMask`는 896(Enemy 7 \| Soldier 8 \| PlayerBase 9) — 이 마스크는 "질의 후보 집합"이고 진영 판정은 `EnemyNodeQuery.TryAccept`가 사후에 하므로 넓게 잡는 것이 계약과 일치한다(부분적으로 비면 `Hostile` 조건이 조용히 항상 0). **감속 파훼 불변식**(`MoveSpeed × MaxFactor × slow^n < MinSpeed`)이 수치 튜닝으로 깨졌다 복원된 이력이 있다(WL-122) — 밸런싱 시 `TankGraphSpec.md` 「감속 파훼 불변식」 표를 재계산할 것. `EnemyAgent`는 `Enemy`를 상속하지 않고 **병존**하는 무상태 파사드로, 값은 `MonsterMove`/`Enemy`가 소유하고 전달만 한다(유일한 예외는 패턴 쿨다운 기록). 노드는 `Enemy`/`MonsterMove`/`Animator`에 직접 닿지 않고 `EnemyAgent` 경계만 안다. **네임스페이스를 두지 않는 규약**이라 노드·보조 타입 클래스 이름이 전역 유일해야 한다(기존 MiniBoss 노드 4종은 `NorthLand.Combat.Boss`를 쓰며 이 세트와 무관·GUID 충돌 없음). 수치는 코드가 아니라 그래프 Blackboard 변수로 authoring한다(WL-094와 같은 축). 단 **`LayerMask`는 Blackboard 지원 타입이 아니라** `EnemyAgent.UnitLayerMask`(프리팹 인스펙터)에 둔다. **타워 접점(#164 리팩토링 반영 완료)**: P3 마력 봉인의 대상 집합은 `EnemyNodeQuery.IsAttackTower` = `Tower.Has<AttackBehaviour>()`로 판정한다. 모든 타워가 단일 `Tower` 타입이 된 뒤 **이 판정이 처음으로 실제 필터 역할을 한다**(예전엔 오라 타워가 별개 클래스라 `Tower.Active`에 없어서 자동으로 빠졌고, 그 뒤엔 `AttackInterval > 0` 휴리스틱이었다). 능력 질의로 바꿔 판정 근거가 다른 시스템의 구현 세부에 의존하지 않는다. 지키는 설계 의도: **"봉인 중에도 감속은 살아남아 P1 파훼 수단이 유지된다."** 편집모드 실측으로 `choco_tower`(Magic/Debuff)가 `IsAttackTower=false`임을 확인(2026-07-29). **감속 중첩 해소 + 밸런스 미결**: 감속 소스키가 인스턴스별로 바뀌어 같은 종류 감속 타워가 실제로 중첩되기 시작했다(구 `TowerID` 해시에선 1중첩에 고정 — **P1 파훼가 원천 불가**였다). 이후 `choco_tower` 감속을 −40%→**−20%(배율 0.8)** 로 조정(2026-07-29, n0wst4ndup). **`Boss.Stat.MoveSpeed`가 12 → 4.8(×0.4, `TileSize` 대응)로 내려갈 때 `P1_MinSpeed`(25)와 `P1_DamagePerSpeedUnit`(1.5)이 누락됐던 것을 2026-08-11에 복원했다** — `MinSpeed` **10**(×0.4, 속도 단위) / `DamagePerSpeedUnit` **3.75**(×2.5, 속도 입력이 ×0.4로 줄어든 것을 상쇄). 계수를 "배율류라 스케일 대상 아님"으로 분류한 것이 누락 원인이었다. 복원 후 `33.6 × 0.8ⁿ` 기준 **6중첩 8.81(피해 0)**로 파훼 문턱이, 충돌 피해가 126(성문 HP 200의 63%)으로 스케일 변경 이전과 같아졌다. 인게임 검증 미완. ⚠ **`Boss.Stat.MoveSpeed`가 그래프 밖(`tank.asset`)에 있어 그래프만 보고 튜닝하면 이 불변식이 조용히 어긋난다.** ⚠ **한 블랙보드 값이 에셋 안에 네 벌로 직렬화되며 자동 동기화되지 않는다**(컴파일된 노드 / `BehaviorAuthoringGraph`의 `LinkedVariable` 대상 / `RuntimeBlackboardAsset` / `BehaviorBlackboardAuthoringAsset`). 블랙보드만 고치면 **게임 동작이 안 바뀌고**(런타임 바인딩이 블랙보드를 다시 읽지 않는다 — `BehaviorGraphAgent.Init` 후 실측), 컴파일된 노드만 고치면 **에디터에서 그래프를 열어 저장할 때 되돌아간다**(authoring 사본이 빌드 소스). 실제로 `P1_MinSpeed`가 WL-122의 15 → 25 변경에서 네 벌 중 두 벌만 갱신돼 오래 어긋나 있었고 2026-08-11 전수 조사로 정렬했다(WL-177). **값 변경은 Behavior 에디터에서, 스크립트로 건드렸다면 네 벌 전수 확인.** 보스 이름은 프로토타입 임시명 `Tank`다. **웨이브 편성: 최종 웨이브(7)의 마지막 그룹을 `Candy_King_01` → `Tank`(count 1)로 교체했다.** 둘은 **같은 `tank` EnemyAsset을 공유하는 같은 적**이며, `Candy_King_01`은 `BehaviorGraphAgent`가 없어 BT 패턴이 하나도 돌지 않는 미완성 몸체였다 — 컨텐츠 제거가 아니라 placeholder를 동작하는 구현으로 교체한 것이다. **중간보스는 별개로 `MidBoss`(`ogre_king` + `MidBossBehavior.asset`)가 웨이브 4에 그대로 있다.** WL-096(최종 웨이브 점유 시 보상 건너뜀)은 여전히 이 이슈 범위 밖. 설계 `Docs/Monster/Boss/BossDesign.md` · 노드 대장 `Docs/Monster/Boss/BossNodeReference.md` |
| Resident (경영 앰비언트 군중 BT, #276) | n0wst4ndup | `Assets/Scripts/ManagementSpace/Resident`(상태·레지스트리 3종·세션·스포너), `.../Resident/Nodes`(리프 노드 11종), `.../Resident/Debug`, `Assets/Scripts/Editor/ResidentBehaviorGraphBuilder.cs`, `Assets/Behavior/ResidentBehavior.asset`(그래프), `Assets/Imported/@NorthLand/{Prefabs,Animations}/Resident` | 마을에 사람이 산다는 것을 보여주는 **연출 개체군**이다 — 자원을 생산하지 않고 일터로 가지 않는다. **군중 수는 고정 풀이고 배치 상한(`MaxVillagers`)과 무관하다**(GDD §5.1 · §3 접점 행). 동작하는 행위: R1 유휴 · R2 산책 · R15 휴식 · R3 인사 · R4 수다 · R12 웃음 · R7 놀람 · R5 춤 · R8 귀가 · R9 등장. **구조**: `Resident`(상태 정본 — 세션 참조·사교성·조우 쿨다운·등장/귀가 플래그)와 `ResidentAgent`(BT 파사드)가 **병존**한다 — `Enemy`/`EnemyAgent`와 같은 구성이고, 노드는 파사드만 보고 `NavMeshAgent`/`Animator`에 직접 닿지 않는다. **대화는 세션 객체(`ResidentConversation`)가 소유하고 참가자는 참조만 든다** — 티커가 없고 진행이 참가자의 행동 종료에 붙어 있어 한쪽이 사라져도 세션이 멎지 않으며, 사라진 것을 남은 쪽이 이탈로 읽어 R7을 띄운다. **근접 질의는 물리가 아니라 레지스트리 3종**(주민·웨이포인트·문)으로 푼다 — 레이어·태그를 하나도 점유하지 않는다(`ProjectSettings` 변경 0건). **BT는 Priority Abort 선점 3개**(밤 · 대화 합류 · 춤 목격)를 쓴다 — 이 프로젝트에서 처음이고 보스 그래프는 미사용. 도입 근거는 밤 전환의 동시성이다(선점이 없으면 주민 30명이 각자 이동 구간 4초가 끝나기를 기다려 어긋나게 반응한다). ⚠ **브랜치 우선순위가 노드 X좌표로 결정된다**(`GraphAssetProcessor.GetSortedConnections`) — 순서가 뒤집히면 조건도 등록도 정상인 채 선점만 죽으므로 빌더 자기검사가 자식 순서를 **타입으로** 대조한다. ⚠ **그래프는 `ResidentBehaviorGraphBuilder`(에디터)의 산출물이라 손 편집이 재빌드에 사라진다** — 튜닝 값 회수용으로 `NorthLand/Resident/Dump Behavior Graph Values` 메뉴가 있다. 밸런싱 수치가 그래프 Blackboard가 아니라 빌더 상수에 있어 BossAI 행의 방향(WL-094)과 반대다(WL-151). 애니메이터는 **전이 없는 고립 상태 8개** + `CrossFadeInFixedTime`이라 Animator를 열면 전이가 없는 것이 정상이다. **✅ 정본 `GameScene` 이식 완료(#277)** — NavMesh 베이크(계단·섬·건물 내부 `NavMeshModifierVolume` 4개), 웨이포인트·문 지점 심기, 주민 30명 배치, 낮→밤→낮 1주기 실측. **⚠ 초콜릿 다리는 `NavMeshLink`가 아니라 보이지 않는 베이크 프록시 메시로 건넌다(#305)** — 링크는 이동이 직선이라 주민이 아치를 뚫었고, 정점에서 꺾어 2개로 나누면 **링크끼리는 연결되지 않아** 아예 못 건넜다. **⚠ 경영 공간 NavMesh 배선이 두 저장소에 걸쳐 있다** — `NavMeshSurface`와 다리 프록시 오브젝트는 `CandyLand.prefab`(별도 private repo `Assets/Imported`), 베이크 데이터(`Assets/Scenes/GameScene/NavMesh-NavMesh.asset`)와 프록시 메시는 본 저장소다. 배선이 본 저장소 diff에 보이지 않고 머지 순서가 어긋나면 다리가 조용히 끊기며, 재베이크에는 Imported 체크아웃이 필요하다(WL-160, `Docs/Core/SceneWorkflow.md` §7). 전체가 **정적 베이크**다. 종전 미해결이던 GDD §5.3 영토 확장(런타임 섬 프리팹 `Instantiate`)과의 충돌은 영토 시스템 삭제(#337)로 소멸했다(WL-161 종결). **✅ N인 대화(#277)**: 세션이 `Slot[]`→`List<Slot>`이 되고 진행 중 합류를 받는다(`TryJoin`/`CanAccept`/`MarkEncounterWithAll`). 자리는 `R = 거리 / (2·sin(π/N))` 원주 배치 + 최근접 그리디 배정(N=2는 기존 중점 대칭과 정확히 동일). 합류 흐름은 ①합류자 인사 → ②기존 참가자가 합류자를 보며 인사 → ③원주 재배치 → ④턴 초기화 4단이다. **⚠ 대화 밀림 방지는 `ResidentAgent.SetStationaryHold` 하나로 푼다** — 서 있는 참가자를 자기 회피 계산에서 뺀다(정지한 `NavMeshAgent`도 지역 회피 해에는 밀린다). **무리 중심에 세우던 `NavMeshObstacle`(`ResidentConversationObstacle`)은 폐기했다** — `carving = false`라 경로 계획이 모르는데 반경이 1.96(3인)이라, 지나가던 주민이 설 수 있는 자리가 중심에서 2.56 밖뿐이고 **골목 폭이 그보다 좁으면 유효해가 없어 영구 공전한다**(실측). `Clearance` 튜닝으로 못 푼다(통로 조건 `inner ≤ R − 1.8`이 `MinRadius`에 걸린다). 에이전트끼리는 겹쳐서라도 빠져나오므로 막는 주체를 참가자의 몸으로 되돌렸다 — 자세한 근거는 `Resident.md` §7.1 「왜 회피물을 폐기했는가」. **✅ 정지 금지 존(#332)**: 좁거나 우회로가 없는 통로에서는 대화·춤을 **아예 시작하지 않는다**(`ResidentNoStopZone` + 레지스트리, 씬 뷰 `BoxBoundsHandle`로 저작). 배치는 NavMesh 여유 반지름 + 웨이포인트 253쌍 경로 점유 실측으로 정했고 다리 2 · 계단 4에 놓여 보행 면적의 3.3%를 막는다. **폭만으로는 부족했다** — 계단은 폭 45.6m로 병목 판정을 통과했지만 경로 점유가 48~90/253으로 다리(41)보다 높았다(우회로 없는 필수 통로). ⚠ 존 6개는 `CandyLand` 프리팹 소유라 **본 저장소 diff에 배선이 없다**(WL-160). ⚠ 존 안의 웨이포인트는 결함이 아니라 **조용한 목적지 저작 수단**이다(`WP (15)` 100% 차단이 의도, `WP (24)`가 짝). **✅ 선택/아웃라인(#277)**: `ResidentSelectable`(런타임 부착 마커) + `ResidentSelectionCoordinator`가 호버 노랑·선택 초록·드래그 다중 선택을 붙이고 **유휴 주민 수(`MaxVillagers − AssignedTotal`)로 상한**을 건다. **✅ 배치 반응(#341)**: 패널 +1 소멸 · −1 퇴장(§11.14). **✅ 들기·건물 드롭 배치**: `IDragHandle` + `Mode.UnitDrag` + `ResidentDragCoordinator`(§11.15) — 단 **연출이 없어** 들린 주민이 커서를 따라오지 않고 그 자리에서 사라진다(R10·R11·착지·거절 피드백은 §8.3·§8.5 결론 대기). **✅ 주민 목소리**(§11.16): 수다(`Talking_1~3`)·웃음(`Laughing`)·인사/작별(`Wave`)·밤 귀가(`Run`)에 클립 8본. **오쏘 40 이하 전체 볼륨 → 40~80 페이드 → 80 이상 무음**이고 **화면 중앙일수록 크며 화면 밖은 무음**이다(끝값 80은 플레이하며 귀로 맞춘 값이다 — 처음 사양 50에서는 `SmoothStep`이 뒷구간을 빠르게 떨어뜨려 체감이 30~40밖에 안 됐다. **숫자만 보고 되돌리지 말 것**). ⚠ **`Wave` 하나를 인사와 작별이 공유**하므로(`UpdateFarewell`이 인사 클립을 다시 쓴다) 상태만으로는 Hi/Bye가 구분되지 않는다 — `Resident.Conversation.Phase`로 가르며, `BeginPlay` 뒤 단계 표시가 **다음 틱**이라는 순서에 기대고 있다(순서가 바뀌면 조용히 뒤바뀐다). ⚠ **Unity의 3D 오디오를 쓰지 않는다** — 오쏘 카메라라 원근이 없고 `AudioListener`가 마을 위 463유닛에 떠 있어 3D 감쇠가 화면과 무관해진다. 2D 재생 + 뷰포트 좌표로 볼륨·팬 직접 계산이며, 감쇠 거리는 유클리드가 아니라 `max(|dx|,|dy|)`다(16:9라 유클리드면 모서리가 화면 안인데 무음이 된다). **BT가 아니라 애니메이터 상태를 따라간다**(`BossPatternVfx`와 같은 규약 — 노드 무수정, 화자/청자 구분 코드도 불필요). **미착수**: R6 앉기 · R13/R14 공연(§10) · **§8 연출 전부 — 말풍선 작업과 묶였다**(§8.6): 대화·춤·놀람의 말풍선과 드래그 연출(R10·R11·부양 높이·착지·거절 피드백·최대 축소에서 점처럼 보임)은 **같은 질문의 다른 얼굴**이라 함께 정한다. 하나씩 앞서 정하면 화면에 두 체계가 공존하고 되돌리게 된다. ⚠ 말풍선의 줌 연동은 **목소리 구간(§11.16)과 반대편**(멀리서도 읽히는 표시)에 놓아야 줌 전 구간이 덮인다. 정본 `Docs/ManagementArea/Resident.md`(§11이 실제로 도는 것) |
| InteractionOutline (상호작용 아웃라인, #213) | n0wst4ndup | `Assets/Scripts/GameManager/MouseManager/Highlight`(`OutlineHighlight`/`OutlineInteractionDriver`/`IOutlineTargetProvider`/`IOutlineKindFilter`), `Assets/Scripts/Rendering`(`InteractionOutlineRegistry`/`InteractionOutlineFeature`), `Assets/Shaders/Outline`, `Assets/Settings/PC_Renderer.asset`·`Mobile_Renderer.asset` | **표시 방식 2회 전환**: 인버티드 헐(shell, 2026-07-27) → **스크린 스페이스 실루엣**(2026-08-03). 현재 방식은 대상 렌더러를 마스크 RT에 슬롯 값(호버/선택/합성프리뷰)으로 그리고 dilate 후 원본을 차감해 링을 뽑아 합성한다 — 자식 오브젝트·머티리얼·메시를 하나도 만들지 않고, **부품 수와 무관하게 오브젝트 전체 실루엣 하나**가 나온다. 셸의 렌더러 512개 상한·스무스 노멀 프리베이크·`OutlineShell` 레이어+세 마스크 의존이 전부 사라졌다. **공개 계약 무변경**: `OutlineHighlight.GetOrAdd(go).Set(kind, bool)`, 우선순위 MergePreview > (Selected\|GroupSelected) > Hover, `IOutlineTargetProvider`(대상 리다이렉트, 구현체 없음 — 유일한 구현체 `TerritoryNodeView`가 #337에서 삭제됨). **`IOutlineKindFilter` 추가(#302)** — 대상이 아웃라인을 **종류별로** 거부한다(구현체 `ResidentSelectable`: 가용 인원 0이면 선택 초록만 막고 호버 노랑은 살린다). `IOutlineTargetProvider`로는 표현할 수 없었다 — `Resolve()`가 호버·선택 **공용**이라 `null`을 돌리면 두 종류가 함께 죽는다. 드라이버는 이 축이 붙어도 여전히 도메인을 모른다. 색·두께·슬롯별 투시(`ZTest`)·카메라 제외 목록은 **렌더러 피처 인스펙터**에 있다(코드 상수 아님). 렌더 이벤트 `AfterRenderingTransparents`(500) — 틸트-시프트보다 뒤, 픽셀레이션(550)보다 앞(픽셀레이션은 2026-08-13 미채택·OFF지만 이벤트값은 유지 — `VisualLookPipeline.md` §3.8). `SetWidth(float)`는 **no-op**(두께가 스크린 픽셀 단위가 되어 줌 보정 불필요). **셸 잔재 정리 완료**(2026-08-03): 레이어 12 회수, FlatKit `ObjectOutline` 피처 제거, 렌더러 세 마스크 원복, 스무스 노멀 자산 16개 삭제. **잔여**: Mobile Forward 경로 미검증(T9). 정본 `Docs/Core/InteractionOutline.md` |
| VisualLook (전역 비주얼 룩, #148) | n0wst4ndup | `Assets/Scripts/Editor/FlatKitMaterialConverter.cs`, `Assets/Scripts/Rendering/PixelationZoomBinder.cs`, `Assets/Settings/FlatKit`(룩 템플릿·변환 기록), `Assets/Settings/MiniatureLookProfile.asset`, `Assets/Settings/PC_Renderer.asset` | **정본 `GameScene` 이행 완료(2026-08-04)** — 툰 셰이딩·룩 볼륨·라이팅이 정본 씬에서 함께 돈다. 툰 셰이딩 이관 대상: 본진(CandyLand) + 주민 + 플랫폼·브릿지 33개 + **환경 오브젝트 142개**. 원본 무수정 규칙 때문에 "원본 1개 → 사본 1개 + 렌더러 슬롯 교체" 방식이고, 룩 수치 정본은 템플릿 머티리얼 1개(`FlatKitToon_Template.mat`)다. 사본 **118개**는 아트 저장소 `@NorthLand/Materials/FlatKit` **한 곳**(카테고리별로 쪼개지 않는다 — 툴이 그 폴더에 만들고, 사본은 원본 1개당 1개라 카테고리에 귀속되지 않는다), 템플릿·매핑은 프로젝트 저장소. **플랫폼·환경·본진은 프리팹 에셋 자체에** 적용돼 있다(Prefab Variant를 Regular로 언팩 후 적용 — 어느 씬에 놓아도 툰 룩). 반투명 `Glass`(젤리 6슬롯)는 유리 느낌이 죽어 **URP Lit 원본 유지** — 한 렌더러에 FlatKit/URP Lit 슬롯이 공존한다. **완전 하드 컷** 확정(`_ShadowEdgeSize` 0 · `_Flatness` 1 · `_UnityShadowSharpness` 10), 대비는 낮게(`_ColorDim` 0.72/0.70/0.80). **룩 볼륨** = `MiniatureLookProfile`에 Tonemapping(Neutral) + Vignette(0.2/0.3) 2개 오버라이드, 양쪽 씬에 `LookVolume` 배치 + `Main Camera.renderPostProcessing` on(`MinMapCamera`는 off — 미니맵에 비네트 금지). Tonemapping은 흰 알베도가 노출에 클리핑돼 셀 컷이 사라지는 문제를 잡는 **재질 판단의 선행 조건**이었다. ⚠️ **볼륨 오버라이드는 `AssetDatabase.AddObjectToAsset` 없이 추가하면 `{fileID: 0}`으로 저장돼 조용히 사라진다**(사고 2회 — `MiniatureLookProfile` 2026-08-03, `NightLookProfile` 2026-08-07. 문서가 이미 있는 상태에서 재발했다. **같은 세션에서는 인메모리 인스턴스가 살아 있어 스크린샷·수치 검증이 전부 통과하고, 도메인 리로드 후에야 사라진다** — 코드로 프로파일을 만들면 그 자리에서 `.asset`을 텍스트로 읽어 확인할 것. `VisualLookPipeline.md` §3.1.1). **라이팅**은 키 라이트 1.5/Hard + 앰비언트 Trilight 눌림이고 **씬이 단일 출처**다 — `DayNightLightingController.captureDayPresetFromScene`(기본 켜짐)이 `Awake`에서 씬 값을 낮 프리셋으로 흡수하고 덮지 않는다. 이 스위치를 끄면 종전대로 프리셋이 씬을 덮으므로 값이 이원화된다(`VisualLookPipeline.md` §7). `nightPreset`은 전환 목표값이라 프리셋에 남는다. **픽셀레이션은 미채택**(2026-08-13, #374 — 2026-08-04 채택 확정 후 빌드 정리에서 기각, 소유자 합의). `PC_Renderer`에 **등재는 유지되나 `m_Active: 0`**이라 플래그 하나로 되돌릴 수 있다(§2 결정 8의 "다른 요소가 이 결정에 의존하지 않게" 원칙이 기각 때도 값을 했다). 기각으로 **소멸**: 줌 범위와의 결합(이제 줌은 게임플레이 기준으로만 정한다 — #28) · 미니맵 동반 픽셀화 · 틸트-시프트 양립 문제. 기각으로 **발생**: 픽셀레이션이 겸하던 앨리어싱 완화(고주파 −25% 실측)가 사라져 카메라 AA 판단이 미룰 수 없게 됐다(`VisualLookPipeline.md` §3.7.1·§8). **잔재**: `PixelationZoomBinder`가 씬에 남아 꺼진 피처 설정 에셋에 계속 써 diff churn을 만든다 — **WL-187**. 실측표(월드 블록 ≥1.0 · 화면 블록 ≥1px)는 재활성 대비로 §3.7.2에 남겼다. **컬러 그레이딩은 페이즈 무관 축에서는 미채택**(실물 보고 기각) — 다만 **밤 전용으로는 도입됐다**(`NightLookProfile`, ColorAdjustments+Bloom, priority 2, #136 · `VisualLookPipeline.md` §3.3.1). 밤 전환 셀 와이프(`Night Wipe` 피처 + `Assets/Shaders/DayNight/NightWipe.shader`, #101)가 렌더러 피처 목록에 추가됐고 **전환 중에만 활성**이다(순서는 §3.8이 단일 진실 원천). ⚠️ **`Night Wipe`만 PC/Mobile 양쪽 등재** — 룩 정제가 아니라 게임플레이 페이즈 연출이라 PC 전용 예외(§2 결정 5)를 적용하지 않았다. **미착수/미해결**: 틸트-시프트, 모바일 프리셋, **캐스트 그림자가 전혀 렌더되지 않음**(`PC_RPAsset.shadowDistance` 50 vs 카메라 591유닛 — 전역 에셋이라 팀 결정 대기), ~~미니맵이 함께 픽셀화됨~~ → **픽셀레이션 미채택으로 현재 발현하지 않는다**(구조는 그대로이므로 재활성 시 부활하는 조건부 결함: 렌더러가 `PC_Renderer` 1개뿐이라 모든 카메라가 공유하는데 벤더 피처가 SceneView/Preview/Overlay만 제외 → Base 카메라인 `MinMapCamera` 통과. 아웃라인은 `excludedCameraNames`로 이미 막혀 있어 **누출이 아니다**). 룩데브 씬 `Assets/Scenes/Branches/GameScene_600.unity`는 튜닝 완료로 **폐기 예정**(`Branches/`는 주간 정리에서 폴더째 비우는 위치 — 이후 튜닝은 정본 씬에서). 정본 `Docs/Rendering/VisualLookPipeline.md` |
| Camera (쿼터뷰 카메라 · 줌) | sunjin1222 · n0wst4ndup | `Assets/Scripts/Camera/CameraController2.cs`, `Assets/Scripts/Camera/CameraVisibility.cs`, `Assets/Scripts/Camera/ZoomDrivenVisibility.cs` | 정본 씬은 `CameraController2` 단독(구 `CameraController.cs`는 미사용 잔존, WL-023). 이동(WASD·우드래그·미니맵)과 줌(휠 → `CinemachineCamera.Lens.OrthographicSize`, 씬 범위 30~150)을 소유하며 `Mouse`/`Keyboard.current`를 직접 폴링한다 — MouseManager의 '입력 단일 창구' 계약 **밖**이다(WL-023·WL-073). **#138에서 `OnZoomChanged` 발행이 추가돼 줌 소비 축이 둘이 됐다**: `PixelationZoomBinder`(매 `LateUpdate` 폴링, `[ExecuteAlways]`라 편집 모드에서도 돌아야 해 이벤트로 이관 불가 — **의도된 병존**)와 `ZoomDrivenVisibility`(push + 붙을 때 1회 pull). 발행이 `ZoomMouseWheel` 인라인이라 `ApplyZoom` 격리 seam은 아직 없다(WL-024). **#390에서 `ZoomTo`/`UpdateTargetZoom`(SmoothDamp, `LateUpdate`)이 붙어 발행처가 둘로 늘었고, seam이 없는 탓에 렌즈 대입+발행이 그대로 복제됐다** — WL-024가 예고한 비용이 실제로 발생한 지점이다. **자동 모션은 수동 입력에 양보한다** — 이동은 WASD·우드래그가 `CancelMinimapMove`로, 줌은 휠이 `isZooming = false`로 각각 끊는다. ⚠ **양보를 빠뜨리면 증상이 "느려짐"이 아니라 "잠김"이다**(#390 실측) — `Update`에서 바꾼 배율을 같은 프레임 `LateUpdate`가 되돌리고, 굴리는 동안은 SmoothDamp가 목표에 닿지 못해 종료 조건도 안 걸린다. `CameraVisibility`(static)는 프러스텀 가시성 질의의 공용 창구로, 평면을 **프레임당 1회만** 계산해 캐시한다(질의 주체가 스포너 1 + 비행 중 N대) |
| Audio (볼륨 3채널 · BGM 재생/전환, #361) | n0wst4ndup | `Assets/Scripts/GameManager/AudioManager.cs`, `Assets/Scripts/GameManager/BgmCue.cs` | **AudioMixer를 쓰지 않는다** — 채널이 Master/BGM/SFX 3개뿐이고 더킹·스냅샷 요구가 없어 믹서 에셋 + 그룹 배선 + dB 변환 비용이 이득보다 크다고 판단했다. 대신 `AudioManager`가 볼륨 값을 소유하고 **자기가 소유한** `AudioSource.volume`에 곱해 넣는다(`실효 = Master × 채널`, 음소거면 0). ⚠ **대가**: 매니저를 거치지 않는 재생은 볼륨 제어를 받지 못한다 — `SkillManager`의 `AudioSource.PlayClipAtPoint`(스킬 착탄음)가 아직 그 상태다. SFX는 **2D 원샷 경로만** 있다(`PlaySfx` = 소스 1개 + `PlayOneShot`, 풀 아님): 볼륨이 **호출 시점에 구워져** 재생 중 슬라이더 변경이 반영되지 않고 **동시재생 상한이 없다** — 전환 스팅어처럼 드물게 한 번 울리는 소리 전용이고, 프레임마다 울릴 소리(타워 발사음 등)는 풀 기반 경로를 기다린다. 현재 소비처는 낮↔밤 전환 스팅어 2개. 부팅은 `GameSceneManager`와 같은 `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` + `DontDestroyOnLoad`(씬 배치 없음 — 씬 파일 병합 충돌을 만들지 않기 위함). BGM은 `AudioSource` 2개 크로스페이드이고, **페이드 가중치와 실효 볼륨을 분리해 매 프레임 다시 곱한다**(합치면 페이드 중 슬라이더 조작이 목표 볼륨을 덮어쓴다). 나가는 소스는 스왑 시점 가중치(`outgoingWeight`)를 기억한다 — 안 그러면 페이드 도중 트랙을 다시 요청했을 때 `fadeProgress` 리셋으로 최대 볼륨까지 튀었다 내려온다. 페이드는 **`unscaledDeltaTime`** — 설정 패널을 여는 것 자체가 `GamePauseReason.Settings` 정지(`timeScale` 0)라 scaled면 얼어붙는다. 배속(2·4배)에서 `pitch`는 건드리지 않는다. **"어떤 곡을 틀지"는 매니저가 모른다** — `DontDestroyOnLoad`라 인스펙터 배선을 가질 수 없어, 클립 배선과 낮/밤 구독은 씬의 `SoundCue` 계층(`SoundCue` abstract 베이스 → `TitleCue` / `InGameCue`)이 맡는다. 매니저가 씬마다 죽는 `DayNightManager`를 재구독할 일이 없는 대신 **씬마다 큐가 하나씩 있어야 한다**는 계약이 붙는다 — 큐가 없는 씬은 직전 씬 BGM을 그대로 끌고 간다(WL-180 실사고: 밤 게임오버 → 타이틀에서 밤 BGM 루프). 그래서 `TitleCue`는 트랙 에셋이 없는 지금도 배치돼 있고 `titleClip`이 비면 `StopBgm()`을 부른다 — 빈 클립을 `PlayBgm`에 넘기면 매니저가 무시해 직전 트랙이 살아남기 때문이다. 영속화는 `LocalizationManager`(`SelectedLocale`) 선례를 따라 **`PlayerPrefs`** 6키(`MasterVolume`/`BgmVolume`/`SfxVolume` + `*Muted`), 기본값 Master 1.0 · BGM 0.5 · SFX 0.8, `Save()` flush는 종료·포커스 상실 시점에만. **에셋**: `Assets/Imported/@NorthLand/Sound`(BGM 2 + 전환 스팅어 2) — ⚠ **별도 저장소라 클립·`.meta`(임포트 설정) 변경이 본 저장소 diff에 보이지 않고, 미동기화 시 에러 없이 소리만 사라진다**(WL-040과 같은 축). 임포트 설정은 기본값에서 조정했다: **BGM은 `Streaming`+`loadInBackground`**(`DecompressOnLoad`면 두 트랙이 30.2MB+32.1MB로 풀리는데 크로스페이드가 둘을 동시에 문다), 전환 스팅어는 `DecompressOnLoad`+`preload`. 플랫폼 오버라이드 없이 `defaultSettings`가 PC·Mobile 양쪽에 적용된다. Vorbis quality는 100% 유지(청감 tradeoff라 미결). ⚠ **임포트 설정에 클립별 게인이 없다** — 특정 클립이 크면 파일을 다시 내보내거나 재생 배율을 곱하는 수밖에 없다(`.meta`의 `normalize`는 모노 다운믹스용이라 게인이 아니다). 전환 스팅어는 피크가 -0.8/-1.0 dBFS로 꽉 차 있어 `BgmCue.stingerVolume`(코드 기본 0.35 ≈ -9dB, 정본 씬 0.4)로 눌러 재생한다. **⚠ 매니저를 거치지 않는 재생 경로가 둘이다**(AudioManager.md §6.1): `SkillManager`의 `PlayClipAtPoint`(볼륨 제어 **못 받는다**, WL-179 이관 대기)와 `ResidentVoice`(주민 목소리 — 자기 `AudioSource`를 들되 **매 프레임** `GetEffectiveVolume(Sfx)`를 곱해 제어 아래 남는다). 후자가 `PlaySfx`를 못 쓰는 이유는 **볼륨이 매 프레임 바뀌어야 하기 때문**이다(화면 중심 감쇠) — `PlayOneShot`은 호출 시점에 굽는다. 부수 효과로 이미 울리는 소리에도 슬라이더가 반영된다. ⚠ **SFX 풀을 만들 때 `ResidentVoice`를 흡수 대상으로 보지 말 것** — 그쪽은 위치 기반 3D 감쇠가 아니라 **화면 좌표 기반**이고(오쏘라 3D가 성립하지 않는다) 원샷이 아니다. **미착수**: SFX 풀링·3D·`PlayClipAtPoint` 이관(WL-179), 타이틀 BGM 클립(`TitleCue`는 배치돼 있고 클립만 비었다), UI 클릭 사운드. 정본 `Docs/Core/AudioManager.md` |
| ManagementVfx (경영 연출: 업그레이드·줌 힌트·열기구, #138) | n0wst4ndup | `Assets/Scripts/ManagementSpace/Vfx` | 세 갈래 모두 **컨트롤러·카메라에 연출 지식을 넣지 않는다**. **행동 연출**(`BuildingFeedback`) — `ManagementController.OnBuildingAction`을 구독해 자기 건물(`BuildingAsset`)의 알림만 골라 파티클 1회 재생. **줌 힌트**(`BuildingZoomHint`) — `ZoomDrivenVisibility` 파생. 오쏘 사이즈 구간(씬 100~999)에서 상시 파티클로 상호작용 가능 건물을 안내한다. ⚠ **낮/밤을 보지 않는다**: 밤에는 `IsDay` 게이트로 상호작용이 잠기는데(WL-104) 힌트는 그대로 뜬다 — **밤에도 노출하는 것으로 결정(TBD)**, 이질감이 발현하면 `DayNightManager` 구독으로 합성한다(베이스가 `protected IsVisible`을 열어 둔 이유). **열기구**(`BalloonFlightSpawner`/`BalloonFlight`) — 스폰 지점이 화면 밖이면 타이머조차 흘리지 않고, 회수는 종료 지점 도달 또는 화면 밖 8초 유예 중 먼저다. 그래서 동시 생존 수가 플레이어 시선에 비례한다(안 따라가면 2~3대, 끝까지 따라가면 ~8대). 시간은 `Time.deltaTime` — 월드 배경이므로 배속을 따른다(WL-100). 파티클 프리팹은 `Imported/@NorthLand/Particles/Management`에 있어 미동기화 시 연출만 사라진다(WL-040) |
| GameManager (승패 판정 · 결과 통지) | 미정(muchan918·sunjin1222 공동 편집) | `Assets/Scripts/GameManager/GameManager.cs` | 전투 씬 스코프 싱글톤. 본진 HP 0 → `TriggerGameOver()`, 최종 웨이브/보스 처치 → `TriggerVictory()`로 결과를 **최초 1회만** 확정하고(`Playing`으로는 확정 불가, 두 번째 호출은 조용히 무시) 결과 화면 표시는 `ResultUIManager`에 위임한 뒤 `OnResultDecided`를 발행한다. **이 클래스는 다른 시스템을 직접 부르지 않는다** — 시간 정지·저장 삭제·진행 중 효과 취소 같은 후속 처리는 전부 구독 측 책임이다(§2 참고). 소비처가 이미 5종이라 발행 시점·1회 계약을 모르면 중복 정리가 붙기 쉽다 |
| GameSceneManager (씬 전환 · Run 진입) | 미정(muchan918·sunjin1222 공동 편집) | `Assets/Scripts/GameManager/GameSceneManager.cs` | `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)`로 부팅하는 `DontDestroyOnLoad` 싱글톤(씬 배치 없음 — `AudioManager`와 같은 패턴). 씬 이름은 인덱스가 아니라 정본 상수(`TitleScene`/`GameScene`, WL-028)로 로드한다. `IsTitleScene`이 타이틀 판정의 단일 출처이며, 다른 매니저의 존재 여부로 씬을 추정하지 않는다. 새 게임·이어하기·시드 지정 진입이 전부 여기를 지나며, 이어하기 플래그와 마스터 시드는 `TryConsume*`로 **1회 소비**된다(소비 후 리셋 — 다음 진입에 새지 않게) |

`OutlineInteractionDriver`·`ResidentSelectionCoordinator`·`ResidentDragCoordinator`는 런타임에 자가 부팅되는 전역 소비처다. 씬 오브젝트에 중복 부착된 인스턴스는 호스트 오브젝트를 지우지 않고 `Destroy(this)`로 중복 컴포넌트만 제거한다. 세 소비처와 `BuildingFeedback`의 누락 경고는 `GameSceneManager.IsTitleScene`일 때만 억제하며, 개인 테스트 씬에서는 진단을 유지한다.

### Run/Seed (Run 단위 마스터 시드)

- **소유자**: sunjin1222
- **경로**: `Assets/Scripts/SeedData`, `Assets/Scripts/SaveData/RunData.cs`
- `RunBootstrapper`가 `[DefaultExecutionOrder(-1000)]`으로 전투맵보다 먼저 마스터 시드를 확정한다.
- 마스터 시드 결정 우선순위는 **Inspector 개발용 override → 타이틀에서 전달된 시드 → 새 무작위 시드**다.
- `RunSeedDeriver.Derive(masterSeed, systemTag)`는 고정 FNV-1a를 사용한다. 문자열 태그 기반이므로 시스템 추가나 호출 순서 변경이 기존 시스템 시드를 밀지 않는다.
- 현재 태그는 `CombatMap` 하나이며(`Territory` 태그는 #337에서 삭제), 각 시스템은 하나의 `System.Random` 인스턴스를 공유하지 않는다.
- `RunSeedData`는 마스터 시드, 파생 규칙 버전, 시스템별 요청 시드와 최종 사용 시드를 기록하고 `RunData`에 포함된다.
- 신규 Run은 요청 시드로 생성하고, 복원된 Run은 `UsedSeed != 0`이면 최종 사용 시드를 우선 주입한다. 전투맵 fallback으로 요청값과 실제 사용값이 달라질 수 있기 때문이다.
- `RunSeedContext`와 `RunData`는 씬의 `RunBootstrapper`가 소유한다. 새 static `Instance`는 추가하지 않는다. 향후 저장 소비자는 `RunBootstrapper`를 명시적으로 참조한다.
- **Play 검증(2026-08-04)**: 같은 마스터 시드로 전투맵·버프 타일·영토 그래프가 동일하며, 일반 새 게임은 시작할 때마다 다른 마스터 시드를 사용함을 확인했다. (영토 그래프는 #337에서 삭제 — 재검증 시 전투맵·버프 타일만 대상.)

- **전투맵 생성 설정·안정성 검증 정본**: `Docs/Core/CombatMapGeneration.md`

### RunSave (Run 단위 저장/복원)

- **소유자**: sunjin1222
- **경로**: `Assets/Scripts/SaveData`
- **상세 명세**: `Docs/Core/SaveSystem.md`
- 플레이어 슬롯은 3개이며, 각 슬롯은 `Application.persistentDataPath/SaveSlots/slot-{index}/player.json`과 `run-save.json`을 독립적으로 가진다. `RunSaveManager`는 `PlayerSaveService`에서 현재 선택 슬롯 경로를 받아 사용한다.
- 마지막으로 선택한 슬롯은 별도로 기억해 게임 재실행 시 복원한다. 슬롯을 선택하지 않은 상태에서는 새 게임과 이어하기를 시작하지 않는다.
- 언어 등 게임 공통 설정은 슬롯 밖 `Application.persistentDataPath/settings.json`에 저장한다.
- 구버전 루트 `Application.persistentDataPath/run-save.json`은 선택 슬롯에 Run 저장이 없을 때만 이전한다. 대상 기록이 성공한 뒤에만 구버전 원본을 삭제하며 기존 슬롯 Run을 덮어쓰지 않는다.
- 각 JSON 파일은 임시 파일 기록 성공 후 교체해 기존 세이브 손상을 막는다.
- `run-save.json`은 `SaveSerializer`, `player.json`과 `settings.json`은 `VersionedSaveSerializer<TData>`를 사용해 봉투의 `version`을 먼저 읽고 지원 버전의 `data`만 DTO로 변환한다. 파일마다 독립된 포맷 버전과 인접 버전 마이그레이션 체인을 가지며, 알 수 없는 상위 버전은 다운그레이드 손상을 막기 위해 거부한다.
- `RunSaveManager`는 각 시스템의 공개 조회/복원 API를 호출하는 중앙 오케스트레이터다. 기존 시스템에 `ISaveable`을 분산하지 않는다.
- v1 저장 시점은 낮 시작(`OnDayStart`)뿐이다. 복원 중에는 자동 저장을 억제해 읽은 파일을 초기 상태로 덮어쓰지 않는다.
- 전투 맵은 타일 전체가 아니라 Run 시드로 재생성한다. 저장 웨이브까지 공개 범위를 즉시 복원한 뒤 `TowerPlacer.TryRestoreTower`로 타워를 배치해 점유와 타일 버프를 동일 경로로 적용한다.
- 타이틀의 이어하기는 정상 파싱 가능한 지원 버전 세이브가 있을 때만 보인다. 게임오버·승리로 Run이 끝나면 세이브를 삭제한다.
- **Play 검증(2026-08-05)**: 3일차 상태에서 종료 후 이어하기 시 자원·건물 레벨·주민 배치·타워·본진 HP·보상 중첩·맵 공개 범위가 동일하며, 복원 후 신규 타워 배치와 다음 웨이브 진행이 가능함을 확인했다. 게임오버·승리 후 세이브 삭제도 확인했다.

## 2. 공개 API (다른 시스템이 소비해도 되는 것)

### GameManager / GameSceneManager (런 결과 · 씬 전환)

- `GameManager.Instance` / `Result` (`GameResult`: `Playing`/`GameOver`/`Victory`) — 현재 런 상태 pull. **전투 씬 스코프 싱글톤**이므로 `DontDestroyOnLoad` 쪽에서 캐시하면 씬마다 재바인딩이 필요하다(WL-002 축) — 씬 오브젝트에서 소비하는 편이 싸다.
- `GameManager.TriggerGameOver()` / `TriggerVictory()` — 결과 확정. **최초 1회만 인정**하고 이후 호출은 조용히 무시된다. `Playing`으로는 확정할 수 없다. 현재 발행처: `PlayerBase`(본진 HP 0), 웨이브 클리어 판정, `NightActionPanelView`(임시 버튼).
- `GameManager.OnResultDecided(GameResult)` — 결과 확정 통지. **UI 존재 여부와 무관하게** 항상 1회 발행된다. `GameManager`는 이 이벤트 외에 다른 시스템을 직접 부르지 않으므로, 런 종료 후속 처리(시간 정지·세이브 삭제·진행 중 효과/조준 취소)는 전부 구독 측 책임이다. 소비처 5종: `GameSpeedController`(정지), `RunSaveManager`(이어하기 세이브 삭제), `SkillBomb`·`SkillField`(잔존 발동 차단, WL-080), `PhasePanelSwitcher`(스킬 조준 취소 #391).
- `GameSceneManager.Instance.LoadMainMenu()` / `LoadManageSpace()` — 타이틀 ↔ 게임 씬 전환. 씬 이름 상수는 정본 고정이라 개인 사본을 지목하지 않는다(WL-028). 시드·이어하기 진입은 Run/Seed·RunSave 절 참고.
- `GameSceneManager.IsTitleScene` — 활성 씬이 정본 `TitleScene`인지 확인하는 단일 출처. 경고 억제처럼 씬 문맥이 필요한 코드는 `DayNightManager` 등 다른 시스템의 존재 여부로 추정하지 않고 이 값을 사용한다.
- `GameSceneManager.QuitGame()` — 에디터에서는 플레이 종료, 빌드에서는 `Application.Quit`.

### Run/Seed

- `RunSeedDeriver.Derive(int masterSeed, string systemTag)` — 플랫폼·호출 순서와 무관한 시스템별 시드 파생.
- `RunSeedContext.CreateRandomRun()` / `CreateRun(int)` / `Restore(RunData)` — Run 시드 생성·복원.
- `RunSeedContext.RecordCombatMapUsedSeed(int)` — 생성 완료 후 실제 사용 시드 기록. (`RecordTerritoryUsedSeed`는 #337에서 삭제)
- `RunBootstrapper.RunData` / `SeedData` / `MasterSeed` — 현재 씬 Run의 읽기 접근자.
- `GameSceneManager.LoadManageSpaceWithSeed(int)` / `TryConsumePendingMasterSeed(out int)` — 타이틀 입력 시드의 1회성 씬 전환 핸드오프.
- `CombatMapGenerator.TryGenerate(int)` / `RequestedSeed` / `UsedSeed` — 전투맵 요청·최종 시드 계약.
- `CombatMapInitializer.InitializeCombatMap(int)` / `UsedSeed` — 전투맵 생성·타일 배치 초기화 진입점.

### RunSave

- `SaveSerializer.Serialize(RunData)` / `TryDeserialize(string, out RunData, out string)` — Run 봉투 직렬화·버전 판별·마이그레이션·역직렬화 진입점.
- `SaveFileStore.Exists` / `TryRead` / `TryWrite` / `TryDelete` — 단일 세이브 파일 IO. 게임 상태나 JSON 구조는 알지 않는다.
- `PlayerSaveService.Instance` / `HasSelectedSlot` / `CurrentSlotPath` / `SelectedSlotChanged` — 현재 플레이어 슬롯과 Run 저장 경로를 제공하는 선행 계약.
- `PlayerSaveService.TryCreateAndSelectSlot` / `TrySelectSlot` / `TryDeleteSlot` / `TryUpdateLastPlayedAt` — 플레이어 슬롯 생성·선택·삭제와 최근 플레이 시각 갱신 진입점.
- `GameSettingsService.Instance` / `CurrentSettings` / `SettingsChanged` / `TrySetLocale` / `TrySetLastSelectedSlotIndex` — 플레이어 슬롯과 독립된 공통 설정 조회·변경 진입점.
- `GameSceneManager.LoadContinue()` / `TryConsumeContinueRequest()` — 타이틀에서 게임 씬으로 이어하기 요청을 한 번 전달한다.
- `ManagementController.TryRestoreResource` / `TryRestoreProductionLine` / `TryRestoreUpgradeBuilding` / `TryRestoreBonusVillagers` — 비용·보상 경로를 거치지 않는 경영 복원 전용 진입점. 건물은 배열 인덱스가 아니라 BuildingID로 찾는다.
- `TowerPlacer.TryRestoreTower(TowerAsset, Vector2Int, out Tower)` — 비용 차감·Undo·연출 없이 일반 배치 확정 경로를 재사용해 점유·타일 버프·`Tower.Build`를 적용한다.

### Audio

- `AudioManager.Instance` — `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)`로 자체 부팅하므로 씬 배치·초기화 순서와 무관하게 항상 존재한다.
- `AudioManager.GetVolume(AudioChannel)` / `SetVolume(AudioChannel, float 0~1)` — 채널 볼륨. `SetVolume`은 음소거 상태를 **건드리지 않는다**("음소거 중 슬라이더 조작 = 자동 해제" 같은 UX 정책은 설정 패널 몫).
- `AudioManager.IsMuted(AudioChannel)` / `SetMuted(AudioChannel, bool)` — 음소거. 볼륨 값은 보존되므로 해제하면 원래 슬라이더 위치로 돌아온다.
- `AudioManager.GetEffectiveVolume(AudioChannel)` — 실제로 `AudioSource.volume`에 곱할 계수(`Master × 채널`, 음소거면 0). **새 재생 경로를 만드는 쪽이 반드시 곱해야 볼륨 제어를 받는다** — 믹서가 없어 이것이 유일한 연결 고리다.
- `AudioManager.OnAudioSettingsChanged` — 볼륨·음소거 변경 통지. 설정 패널 슬라이더가 코드 쪽 변경을 따라오는 용도.
- `AudioManager.PlayBgm(AudioClip, float fadeSeconds = 1f)` / `StopBgm(float)` — BGM 교체·정지. 같은 클립 재요청과 `null` 클립은 조용히 무시한다(씬 재로드·페이즈 전환에서 같은 트랙이면 끊기지 않고, 클립 미배선이어도 깨지지 않는다).
- `AudioManager.PlaySfx(AudioClip, float volumeScale = 1f)` — 2D 원샷. 실효 SFX 볼륨이 0이면 재생 자체를 생략한다. **풀이 아니다** — 볼륨이 호출 시점에 구워지고 동시재생 상한이 없으므로 드물게 한 번 울리는 짧은 소리에만 쓴다.
- `SoundCue`(abstract, 씬 컴포넌트 베이스) — `fadeSeconds` 소유 + `PlayBgm`/`StopBgm`/`PlaySfx`를 `AudioManager` null 가드와 함께 제공한다. 파생 큐는 "언제 무엇을"만 정한다. 씬 배치는 `SoundCue` 오브젝트 아래 자식 큐 1개.
- `TitleCue : SoundCue` — `titleClip` 1개. **비어 있으면 `StopBgm()`** — 타이틀이 직전 게임 BGM을 끊는 주체다(위 계약).
- `InGameCue : SoundCue` — `dayClip`/`nightClip` + 전환 스팅어 `dayToNightClip`/`nightToDayClip`/`stingerVolume`. `DayNightManager`의 `OnDayToNight`/`OnNightToDay`를 구독해 트랙 전환과 스팅어를 요청하고 `OnDestroy`에서 해제한다. 스팅어는 **전환 순간에만** 울린다(`Start`의 초기 트랙 지정에는 딸려 나오지 않는다). ⚠ 초기 트랙은 `CurrentPhase` 스냅샷이라 세이브 복원과 `Start` 순서가 보장되지 않는다 — 밤 복원이 열리면 드러난다(WL-182).
- `CombatMapTileSpawner.SkipNextRevealAnimation()` — 이어하기에서 공개 타일을 즉시 최종 위치에 놓아 같은 프레임의 타워 물리 검색을 보장한다.
- `PlayerBase.TryRestoreCurrentHp` / `SkillEffectManager.TryRestoreLevel` / `DayNightManager.TryRestoreState` — 본진·보상 중첩·진행 상태의 절대값 복원 진입점.

- `DataTableManager.Get<T>(string id)` — static. **null 반환 가능 → 호출부 null 체크 필수**
- `ResourceTable.Get(string id)` — null 반환 가능
- `BuildingTable.Get(string id)` — null 반환 가능
- `TowerTable.Get(string id)` — null 반환 가능. `TowerAsset`은 Combat의 `Tower.cs`가 직접 소비함
  (PR#80으로 이관 완료) — 잔여 타워 종류의 값 채움은 WL-001 참고
- `EnemyTable.Get(string id)` — null 반환 가능. **스탯(체력/이동속도/공격력/사거리/공격주기)은
  CSV/`EnemyData`에 없음** — `EnemyAsset`(SO)의 `EnemyType`별 필드 그룹(`MeleeFields`/`RangedFields`/
  `BossFields`)에만 존재. `TowerAsset`도 같은 패턴이었으나 #274에서 평탄화되며 사라졌다. 이슈#26 원 스펙(스탯
  CSV 컬럼 요구)과 다른 선택이며 WL-027로 추적 중 — 스탯이 필요한 소비처(#14/#15/#16)는
  `EnemyTable.Get(id)`가 아니라 `EnemyAsset` 조회 경로를 써야 함. `EnemyAsset`도 `TowerAsset`과
  동일하게 Combat의 `Enemy.cs`가 직접 소비함(PR#80으로 이관 완료, 옛 Combat 자체 `EnemyData`는
  삭제됨) — 잔여 종류 값 채움은 WL-001 참고. `BossFields.BehaviorTree`는 실제 BT 에셋 타입 미정 상태의 placeholder 필드
- `ResourceAsset.Data` / `BuildingAsset.Data` / `TowerAsset.Data` / `EnemyAsset.Data` — **호출부가
  Start()에서 직접 채우는 규약** (저장 안 됨)
- `ResourceAsset.Icon` / `TowerAsset.Icon`(`Sprite`) — UI 표기용 아이콘. **자원·타워는 UI 어디서든 이름 텍스트가
  아니라 이 아이콘으로 그린다**(로케일 무관·폭 일정). 스프라이트 authoring은 이 SO 한 곳에서만 한다
- `ResourceIconTable.Get(ResourceKind)`(SO, `Assets/Scripts/Data/Resource/ResourceIconTable.cs`) — `ResourceKind`만
  알고 `ResourceAsset`을 모르는 뷰(`ProductionLineView`)용 매핑. 4종 자원(#337 고정)을 명시 필드로 들고
  `ResourceAsset.Icon`을 **참조만** 모은다(이중 authoring 없음). 미할당이면 null 반환 → 호출부가 이미지를 숨긴다
- `BuildingInfoUI.Instance.ShowInfo(BuildingAsset)` / `HideInfo()` — 경영 공간 전용 정보 패널. `TowerInfoUI`와
  동일 구조의 별도 씬 싱글톤 (공간 분리 계약상 Combat의 `TowerInfoUI`와 공유하지 않음)
- `TowerInfoUI.Instance.ShowInfo(TowerData, string statsText = null)` / `HideInfo()` — 전투 공간 타워 정보 패널.
  **`TowerData`를 통째로 받는 쪽이 정본**(이름·역할·설명을 각각 다른 TMP에 그리므로 키 하나로는 부족).
  `statsText`는 `Tower.BuildStatsText()`가 조합한 평문(숫자라 로컬라이즈 대상 아님)이며, 비면 스탯 블록을 통째로 숨긴다.
  `ShowInfo(string descriptionKey, string statsText = null)` 오버로드는 `TowerData`가 없는 테스트 헬퍼(`SelectableTest`) 전용
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
- `Enemy.IsBoss` (bool, #318) — 보스 술어. `data.EnemyType == EnemyType.Boss` 파생이며
  **최종보스와 중간보스를 모두 포함**한다(프리팹 전수 확인: `Tank`·`MidBoss`·`Candy_King_01` 3종).
  보스 여부가 필요한 곳은 자체 비교를 쓰지 말고 이걸 쓸 것 — `Enemy.Awake`의 BT 배선이 같은 비교를
  중복하던 것을 #318에서 이쪽으로 통일했다. ⚠ **파생이라 데이터 축이 갈리면 조용히 틀린다** —
  `EnemyType`은 스탯 블록 선택·근접/원거리 공격 경로 선택까지 겸하는 필드라, 원거리 보스가 들어오면
  `EnemyType.Ranged`가 되어 `IsBoss`가 false가 된다(WL-176). authored 플래그로 분리하는 것이 해법
- `Enemy.MarkForExecute(float thresholdRatio, float duration, bool debugLog = false)` (#318) —
  처형 표식 부여. `thresholdRatio`는 MaxHp 대비 **비율**(0~1). 재적용은 **갱신**이다(임계·지속 모두
  덮어쓴다 — `StatusEffectHandler.ApplyOrRefresh`와 같은 semantics). 표식이 사는 동안 `TakeDamage`가
  임계 이하를 감지하면 그 자리에서 처형된다(판정 주체는 `Enemy.TryExecute()` 하나).
  **보스 제외 가드는 이 메서드가 아니라 호출부가 소유한다** — `TakeDamage` 경로에 처형과 무관한
  조건을 심지 않기 위함. 부여 직후 1회 판정하므로 **이미 임계 이하인 대상은 부여 시점에 즉시 처형**된다
- `Enemy.SetSpeedMultiplier(float)` / `Enemy.SpeedMultiplier` — #233 이후 `movement`의 **패턴 축 위임**.
 값 소유자는 현재 이동 구현체가 위임하는 `MoveSpeedComposer`이며 `Enemy`는 로컬 필드를 들지 않는다. 중간보스 그래프
  (`MidBossBehavior.asset`)가 쓰는 진입점이라 시그니처를 유지했다. 신규 노드는 `EnemyAgent`를 경유할 것
- `MonsterSpawn.SpawnMonster(GameObject prefab)` / `MonsterSpawn.AliveMonsterCount` (#233) —
  런타임 소환 창구. 웨이브 스폰과 같은 경로를 타 소환체도 `monsterParent` 자식으로 들어가고 경로를 받는다
  (웨이브 클리어 판정이 `monsterParent.childCount == 0`이라 밖에 두면 보스 사망 즉시 웨이브가 끝난다).
  **정적 싱글톤을 노출하지 않는다** — 스포너 다중 구성을 막지 않기 위해, 소환체는 스폰 시점에
  `EnemyAgent.BindSpawner`로 자기를 만든 스포너에 묶인다(경로 주입과 같은 자리).
  ⚠ `AliveMonsterCount`는 `childCount`라 **보스 자신과 사망 연출 중인 몬스터(`destroyDelay` 2초)를 포함**한다
- `MonsterSpawnWaveProvider.TryGetWave(int, out IReadOnlyList<MonsterSpawnEntry>)` /
  `TryGetWaveComposition(int, out IReadOnlyList<WaveMonsterCount>)` /
  `TryGetRewardPool(int, out WaveRewardPool)` / `FinalWaveNumber` / `IsFinalWave(int)` (#294, #384) —
  `TryGetWaveComposition`은 `TryGetWave`와 같은 그룹 유효성 규칙(널 그룹·널 프리팹·0 이하 수량 제외)을
  거친 뒤 각 그룹의 `EnemyAsset`과 출현 수량을 웨이브 등록 순서대로 UI에 제공한다. 같은 `EnemyAsset`이
  뒤에서 다시 등장해도 합산하지 않고 별도 항목으로 유지하므로 미리보기에서 실제 그룹 출현 순서를 읽을
  수 있다. `WaveMonsterCount`는 `EnemyAsset Asset`과 `int Count`를 담는 읽기 전용 값이며, 원본
  `MonsterWaveAsset`과 프리팹 계층은 소비자에게 노출하지 않는다.
  **웨이브 번호 = `waves` 리스트에서 몇 번째인가(1-base)**. 진행 순서의 진실 공급원은 인스펙터
  리스트 순서 하나뿐이며, `MonsterWaveAsset`은 자기 번호를 갖지 않는다(`waveNumber` 필드 제거 —
  직렬화된 값과 실제 순서가 조용히 어긋나는 WL-126형 함정 제거). 순서 변경은 리스트 드래그,
  웨이브 추가는 리스트 append로 한다. **리스트의 마지막 항목이 최종 웨이브** —
  `FinalWaveNumber = 등록 개수`라 웨이브를 추가하면 승리 조건이 자동으로 따라온다.
  1-base↔0-base 변환은 private `TryGetWaveAsset` 한 곳에만 있으며, Provider의 공개 API만 이를 경유한다.
  ⚠ 리스트 중간의 null 슬롯은 경고 후 제외·압축된다(런타임 `orderedWaves`) — 빈 밤을 만들지 않기 위한
  **의도된 동작**으로, null은 웨이브가 아니라 authoring 노이즈로 본다. 빈 슬롯 뒤의 웨이브가 한 칸씩
  당겨지고 `FinalWaveNumber`(=유효 웨이브 개수)도 함께 줄어든다 → 빈 슬롯이 있으면 인스펙터 행 번호와
  웨이브 번호는 어긋난다
  ⚠ `Awake` 1회 빌드 — 런타임 중 `waves` 변경은 반영되지 않는다
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
- `ManagementController` (경영 로직/모델, MonoBehaviour) — 지갑·생산처·주민 배치·업그레이드 상태 소유. `bool AssignVillager(int)`/
  `bool UnassignVillager(int)`(**#341에서 `void`→`bool`** — 드롭 배치가 실패를 알아야 "그 자리에 놓기"를 할 수 있다. 성공 시
  `OnBuildingAction`에 `VillagerAssigned`/`VillagerUnassigned` 발행 → `ResidentSpawner`가 군중을 맞춘다), `RequestAdvancePhase()`(낮→밤 `EndDay()`·잉여 게이트 전용 — **밤→낮 `EndNight()`은 더 이상
  이 메서드가 호출하지 않음, #66. 밤 전용 임시 UI `NightActionPanelView`의 "웨이브 성공" 버튼이 직접 호출, WL-018**),
  **건물 업그레이드**(#139): `bool TryUpgrade(int)`·`bool CanUpgrade(int)`·`int LineLevel/LineMaxLevel/LineAmountPerVillager(int)`·
  `IReadOnlyList<ResourceCost> LineUpgradeCost(int)` — 낮 전용, 수치는 `BuildingAsset.Production.UpgradeLevels`(SO),
  **업그레이드 전용 건물 트랙**(마법 연구소 등, 생산 라인과 별개 index 도메인): `int UpgradeIndexOf(BuildingAsset)`·`int UpgradeBuildingLevel/UpgradeBuildingMaxLevel(int)`·
  `IReadOnlyList<ResourceCost> UpgradeBuildingCost(int)`·`bool CanUpgradeBuilding(int)`·`bool TryUpgradeBuilding(int)` — 낮 전용, 비용은 타입 중립 `BuildingAsset.UpgradeSteps`(#229, 종전 `Skill.UpgradeLevels` 하드코딩),
  같은 `TrySpend` 게이트웨이 경유. **`int GetUpgradeLevel(BuildingAsset)`** = 소비 시스템(스킬 강화 등)이 레벨을 읽는 저결합 창구(효과 적용은 소비 측 소유 — **`SkillManager`가 구현 완료, #205**. `BuffSkillManager`도 소비처였으나 #315로 제거) — BuildingUpgrade.md §8,
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
- `MouseManager.Instance.CancelInteractions()` — 배치 → 스킬 조준 → 호버 → 선택 순서로 모든 마우스 상호작용을 취소하는 **유일한 공용 창구**. 설정창·경영 종료 팝업·밤 진입·`BeginPlacement`가 공유하며 호출부가 취소 순서를 조합하지 않는다.
- `MouseManager.Instance.ClearSelection()`(WL-086) — **선택만 해제하는 유일한 창구.** 단일(`Select(null)` → 대상의 `OnDeselected` = 정보 패널·사거리 원, 드라이버의 `Selected` 아웃라인)과 그룹(`OnPrimarySelect(null)` → 코디네이터 집합 해제 = `GroupSelected` 아웃라인·합성 패널)을 **함께** 비운다. 선택만 내리는 UI는 직접 사용할 수 있지만, 배치·스킬 조준까지 포함한 전체 취소 경로는 `CancelInteractions()`를 사용한다.
- `MouseManager.Instance.SelectExternally(ISelectable)`(#390) — **코드에서 선택을 거는 유일한 창구**(바로가기 버튼 등). 클릭과 같은 `Select` 경로를 타므로 이전 선택 해제·건물 패널 상호배타(`BuildingInfo`의 private `HideAll`)·`_selected` 동기화가 그대로 유지된다. **`BuildingInfoUI`/`CastlePanelUI`/`StorePanelUI`를 직접 켜면 이 셋이 모두 깨진다** — 패널이 겹쳐 뜨고, `_selected`가 어긋나 그 대상을 클릭해도 다시 열리지 않는다. ⚠ `null`을 넘기면 `ClearSelection()`과 **완전히 같은 동작(선택 해제)** 이 되므로, 대상을 못 찾았을 때 그대로 흘려보내면 열려 있던 패널이 조용히 닫힌다 — 호출부가 유효성을 먼저 확인해야 한다. **진입 시 `CancelInteractions()`를 먼저 부른다**(#390 리뷰 WL-192 확정) — 클릭 경로는 `Idle`에서만 돌지만(`CommitClick` ← `UpdateIdle`) 코드 선택은 그 전제 밖이라, 배치·조준 중에 부르면 고스트를 든 채 패널이 열렸다. `BeginPlacement`가 반대 방향으로 하는 것과 대칭이다(거부가 아니라 **양보** — 카메라를 옮기고 패널을 여는 것이 사용자의 명시적 의도이므로). 그 결과 `_selected`가 항상 null에서 시작하므로 **같은 대상을 다시 넘겨도 중복 제거에 삼켜지지 않는다**(클릭과 다른 점).
  ⚠️ **표시만 내리고 `_selected`를 남기면 그 대상은 재클릭해도 다시 뜨지 않는다** — `Select`의 `_selected == next` 중복 제거가 삼킨다. 선택 표시를 내려야 하는 새 경로는 자체 처리하지 말고 이 메서드를 부를 것.
  `OnPrimarySelect`는 원래 "평클릭·빈 곳 클릭"(입력) 신호였으나 이 창구를 통해 모드·페이즈 전환도 태운다 — 지금은 구독자가 코디네이터 1곳이라 무해하고, "사용자 클릭"과 "시스템 정리"를 구분해야 하는 **3번째 소비자가 붙을 때** `OnSelectionCleared` 분리를 검토한다(WL-085의 판단 시점 패턴)
- `MouseManager.Instance` `event OnGroupSelectToggled(IGroupSelectable)`(#183) — Shift(추가 선택 키)+마커 클릭 시 토글 발행. **토글이 실제로 일어날 때만 발행 직전에 `Select(null)`**로 단일 선택을 비운다(마커 없는 대상은 무시 — 집합·`_selected` 둘 다 불변). 표시 권한을 그룹 경로에 통째로 넘기기 위한 것으로, 안 비우면 직전 단일 선택의 사거리 원이 합성 패널 위에 잔존한다(WL-087)
- `MouseManager.Instance` `event OnPrimarySelect(ISelectable)`(#183) — 평클릭(해석된 대상)·빈 곳 클릭 시 **중복 제거 없이 항상** 발행(그룹 선택 코디네이터 전용). `OnSelectionChanged`는 `_selected` 변화만 deduped 통지라 Shift-only 선택(`_selected==null`)에서 빈 곳 해제가 삼켜지던 문제(WL-085) 해소. **우클릭은 해제에 쓰지 않음**(카메라 드래그 이중 점유, WL-073)
- `MouseManager.Instance` **드래그 사각형 선택 3단계 통지**(#261) — `event OnBoxSelectBegin(bool additive)` / `OnBoxSelectUpdate(IReadOnlyList<IGroupSelectable>)` / `OnBoxSelectEnd()`. 좌드래그가 임계(기본 8px)를 넘으면 `Mode.BoxSelect`(Idle 하위) 진입. **갱신 목록은 사각형에 들어온 순서**를 보존하고, 진입 시 `Select(null)`로 단일 선택을 먼저 비운다(Shift 토글 경로와 같은 이유 — WL-087 계열). Shift는 **누른 시점 상태로 고정**되며 클릭의 **토글**과 달리 드래그는 **합집합**이다(의도된 비대칭). 드래그 도중 별도의 취소 입력은 없으며, 왼쪽 버튼을 놓으면 그 시점의 사각형 내용으로 선택을 확정한다.
  - ⚠️ **단일 선택 확정이 press→release로 이동**했다(#261) — 누른 순간엔 클릭/드래그를 구분할 수 없기 때문. 판정 내용은 무변경(`CommitClick`, release 시점에 레이캐스트를 새로 쏘므로 그새 파괴된 대상은 자동으로 걸러진다)이지만 `ISelectable`을 쓰는 **모든** 소비처(타워·건물·상점)가 이 경로를 지난다. 누름+뗌이 한 프레임에 함께 보고되는 경우를 누른 프레임에서 소화한다.(WL-144)
  - **모드 전환은 `SetMode` 단일 창구**(WL-143) — `_mode` 직접 대입 금지. BoxSelect 이탈 시 `OnBoxSelectEnd`를 **1회 보장**하기 위한 것으로, `CancelPlacement`/`CancelSkillTargeting`을 직접 부르는 경로(`PhasePanelSwitcher`)도 자동으로 덮인다. 드래그 종료 판정은 `wasReleasedThisFrame`이 아니라 `isPressed` **상태**로 한다 — 뗀 프레임을 놓치면 모드에 고착돼 모든 클릭·호버가 죽는다
  - **통지 목록은 콜백 안에서만 유효**하다(매니저 내부 리스트 — 캐시 금지). 기준 집합 스냅샷을 뜨는 구독자는 드래그 도중 대상이 파괴될 수 있음을 전제하고 되넣기 전 생존을 확인해야 한다
- `MouseManager.Instance` **유닛 끌기 2단계 통지** — `event OnUnitDragBegin(IDragHandle)` / `OnUnitDragEnd(GameObject dropTarget)`. 좌드래그가 임계를 넘었을 때 **누른 순간 커서 밑에 `IDragHandle`이 있었으면** `Mode.UnitDrag`(Idle 하위) 진입 — 사각형 선택과 배타다(MouseManager.md §5.4). 매니저는 대상을 해석하지 않고 놓는 순간의 `GameObject`를 그대로 넘긴다("생산 건물인가"는 매니저가 답할 질문이 아니다). 첫 소비처는 주민(`ResidentDragCoordinator` → `ManagementController.AssignVillager`)
  - ⚠ **건물 자리에 연출을 띄울 때 `transform.position`을 쓰지 말 것 — 그 피벗은 건물이 있는 자리가 아니다.** `CandyLand`의 건물 `Obj_*`는 피벗이 콜라이더에서 24~69유닛 떨어져 있고 `magic_lab`·`farm`·`castle` **셋은 피벗이 아예 같은 좌표**라 건물 구분조차 안 된다(배치는 BoxCollider `center` 오프셋 소유). 주민 소멸 연출을 피벗에 띄웠다가 마을 반대편에서 터진 실제 사례가 있다(Resident.md §11.15). 콜라이더 bounds · 심어 둔 앵커 · 레이캐스트 히트 지점 중 하나를 쓴다 — 놓은 자리가 필요해지면 `OnUnitDragEnd`에 좌표를 실어 보내면 되고(`PlacementRequest.OnConfirmed(hit, pos)` 선례) 지금은 쓰는 곳이 없어 싣지 않는다
  - `IDragHandle`은 **멤버 없는 순수 마커**다. 매니저가 요구하는 것은 "끌 수 있다"는 사실 하나뿐이고, 들린 뒤의 일은 전부 도메인 소비처가 정한다. 훅을 미리 만들면 빈 채로 남아 잘못된 자리에 연출이 걸린다
  - ⚠ **시작이 있으면 종료는 반드시 온다.** 배치·조준 전환이나 씬 로드로 모드가 끊겨도 `SetMode`가 `null`을 실어 발행한다 — 통지가 새면 패널이 멈추는 정도가 아니라 **들려서 감춰진 대상이 영영 화면에 안 돌아온다**(BoxSelect보다 대가가 크다)
  - ⚠ **드롭 대상 레이캐스트는 `IDragHandle`을 건너뛴다.** `Physics.Raycast`가 최근접 하나만 주므로, 안 그러면 **건물 앞에 선 주민 하나가 레이를 막아** 드롭이 조용히 실패한다. 드롭 대상 레이어에 두 타입이 함께 올라와 있는 이상 구조적으로 생기는 증상이다. `RaycastNonAlloc`(버퍼 32) + **버퍼가 차면 경고** — 조용히 잘리면 같은 증상으로 돌아온다
  - **`UnitDrag`는 호버를 켠 채로 둔다**(`BoxSelect`는 끈다) — 들린 대상이 화면에서 사라지는 구현이라 **호버가 조준의 유일한 단서**다. 단 평상시 호버가 아니라 **드롭과 같은 규칙**(끌 수 있는 것 건너뛰기)으로 찾는다: 노란 테두리가 뜬 그 대상이 곧 드롭을 받는 대상이라야 표시가 약속이 된다
  - ⚠ **UI 위에서는 드롭 대상이 없는 것으로 친다** — 패널 뒤의 건물이 레이에 걸리므로, 안 걸러 내면 **경영 패널 위에서 손을 뗐을 때 안 보이는 건물에 들어간다.** `BoxSelect`가 UI를 무시하고 계속되는 것과 다른 판단이다(그쪽은 결과가 보이고 이쪽은 안 보이는 곳에서 확정된다)
  - 취소 제스처가 없다 — **좌버튼 뗌이 유일한 종료점**이고, 우클릭은 끌기 중에도 카메라 이동이다(Resident.md §8.4)
- `GroupSelectableRegistry.Register/Unregister(IGroupSelectable, Transform)`(#261) — 사각형 판정용 후보 목록. 레이캐스트는 점 하나만 쏠 수 있어 면적 판정이 불가 → 후보를 순회해 스크린 투영으로 포함 여부를 본다(카메라 뒤 제외, **가림은 무시**). 마커가 `OnEnable`/`OnDisable`에서 자기를 등록·해제하므로 주민 등 새 타입은 마커만 붙이면 편입(MouseManager 무수정)
- `MouseManager.Instance.BoxSelectScreenRect` / `IsBoxSelecting`(#261) — 사각형 뷰(`SelectionBoxView`)가 읽어 가는 표시용 상태. 뷰는 **런타임 생성 전용 Canvas**(`UILayer.SelectionBox=50`)라 씬 배치가 없고, 공용 UICanvas 리빌드를 유발하지 않는다(UIZOrder.md §3)
- `MouseManager.Instance.PointerPosition`(포인터 화면 좌표 — Mouse.current 직접 폴링 대신 이걸 쓴다) /
  `event OnHoverChanged(IHoverable)`(커서 밑 호버 대상, 없으면 null. Idle에서만 통지)
- `ISelectable { OnSelected(), OnDeselected() }`,
  `PlacementRequest { GhostPrefab, Snap(RaycastHit→pos), CanPlaceAt(RaycastHit), OnConfirmed(RaycastHit,pos), OnEnded, KeepPlacingAfterConfirm }` — **히트 인지형**: 스냅/검증/확정을 요청 측이 소유(MouseManager는 그리드 규칙 무지), `OnEnded`로 취소/확정 시 프리뷰 정리(PR#81)
- `MouseManager.Instance.BeginSkillTargeting(SkillTargetRequest)` / `CancelSkillTargeting()`(#103) —
  `SkillTargetRequest { GhostPrefab, Snap(Ray,RaycastHit→pos), OnConfirmed(Vector3), OnEnded }`. `PlacementRequest`의 경량 버전(`CanPlaceAt` 없음).
  **시전 y 결정은 요청자 소유**(#289): `Snap`이 히트가 아니라 **커서 광선 ∩ 고정 높이 수평면**을 돌려주고, MouseManager는 그 값을 인디케이터 위치와 확정에 **함께** 쓴다 → 보이는 범위와 판정 위치가 항상 일치. `hit.point`를 쓰면 레이가 타일 옆면에 맞는 순간 x/z·y가 같이 튀어 타일 경계마다 인디케이터가 덜컥거린다(고정 평면은 타일 굴곡 추종을 **의도적으로** 포기한 선택 — 조준감 우선). 시전 높이는 `SkillButtonView._castHeight` 인스펙터 값(전투맵 표면 y 단일 출처 부재는 WL-149).
  `Snap` 시그니처가 `PlacementRequest`(`Func<RaycastHit,Vector3>`)와 두 벌인 상태 — 통일은 후속(MouseManager 소유자 합의 필요).
  **전투 타일 전체 허용**: `_placementMask` 히트에서 `CombatMapTileView` 유무로 전투 타일 여부만 판정(도로 전용 제한·유효/무효 색 제거),
  전투 타일 밖에선 인디케이터 숨김. **전투 타일 위 좌클릭**이면 `OnConfirmed(Snap 결과)`로 확정(타일 게이팅은 MouseManager 소유 — 요청 타입엔 `CanPlaceAt` 훅 없음)
- `SkillManager.Instance` — **null 반환 가능(씬에 없으면) → 호출부 null 체크 필수**(#103).
  `CastAt(Vector3)`(범위 내 적에게 데미지, 밤+충전 게이팅 통과 못하면 false), `CanCast()`,
  `IsReady`(=충전 보유), `Charges`/`MaxCharges`/`RechargeRemaining`(UI 바인딩용, #319), `Radius`,
  `SetBonusCharges(int)`(추가시전 보상이 최대 충전을 올릴 때), `RefillChargesNow()`(테스트 하네스 전용).
  **`BaseDamage`/`BaseRadius`/`BaseCooldown` + `static Scale(baseValue, multiplier)`(#398)** — 마법 연구소 배율이
  **곱해지기 전** 원본 스탯과, 그 배율을 적용하는 식. 경영 패널이 "데미지 30 → 36"을 그리려면 배율(건물 SO 소유)과
  베이스(여기 소유)가 둘 다 필요해서 열었다(§통합 계약 Management↔PlayerSkill). ⚠ **`Radius`(=`effectiveRadius`,
  강화 반영된 현재값, 조준 인디케이터용)와 혼동 금지.** 곱셈은 표시부도 반드시 `Scale`을 통과할 것 — 0/음수 배율
  방어(`PositiveOr1`)가 그 안에 있어, 식이 두 벌이 되면 "패널엔 36인데 실제론 30"이 조용히 생긴다.
  범위 판정은 `SkillHitScan.CollectEnemies`(#398, static)에 위임한다 — 감전·폭탄(`SkillBomb`)·전기장(`SkillField`)이
  같은 수직 캡슐 규칙(`SkillHitScan.VerticalRange` 12f)을 공유하며, 버퍼 포화 시 2배 확장 + `IDamageable` 중복 제거를
  그 안에서 처리한다(공중 유닛 미적중·무음 누락 해소).
  `ImpactResolved` 이벤트(`Action<SkillCastContext>`, #169) — 임팩트(착탄)마다 발행, 보상 특수효과(`SkillEffect`)
  구독용. **컨텍스트의 `HitTargets`는 임팩트마다 재사용되는 버퍼 → 이벤트 처리 중에만 유효, 보관 금지.**
  구독자가 써넣는 필드는 없다 — 컨텍스트는 읽기 전용이다(#319).
  착탄 이펙트는 `SkillVisualSet`(연구소 레벨→프리팹, `FromLevel` 희소 매핑)에서 조회하며 `RefreshUpgrade` 시점에
  캐싱된다(#206) — 세트 미배선 시 인스펙터 `impactEffectPrefab` 폴백. **공개 API 변화는 없다**(내부 연출 경로만 교체).
- ~~`BuffSkillManager.Instance`~~ — **공개 API 아님. 버프 스킬 제거(#315)로 씬 미배선 → `Instance`는 항상 null이다.**
  `BuffSkillManager.cs`·`BuffCastContext.cs`·`BurnBuff.cs`·`BuffSkillButtonView.cs` 4개 파일은 저장소에 남아있으나
  어디에도 배선돼 있지 않다. **새 코드가 이걸 호출하도록 붙이지 말 것** — 되살리려면 기획 재검토가 선행돼야 한다(GDD §5.5)
- `SkillEffectManager.Instance` — **null 반환 가능 → 호출부 null 체크 필수**(#169). 보상 라우터:
  `ApplyReward(WaveRewardData)`(타입 매칭 `SkillEffect` 컴포넌트에 레벨 가산 위임), `GetLevel(WaveRewardType)`(미보유 0),
  **`GetSnapshot(WaveRewardType)` → `SkillEffectSnapshot`**(#353, #287·#292의 개별 조회 4종을 대체) — 카드 한 장에
  필요한 값 한 벌: `Level`(지금 보유), `NextLevel`(고르면 도달할 레벨, 상한에서 잘림), `NextIsMax`(카드의 `Lv 2 → Max`
  표기용), `Stats`("현재 → 획득 후" 수치 줄, 평문·여러 줄 가능). **효과 미부착 시 `default`** — `Stats`가 `null`이며,
  그 상태는 `ApplyReward`가 경고만 내고 보상을 무시하는 상태와 같다. 표시부는 `Stats`가 비면 레벨 줄까지 비워야
  "고르면 오른다"는 거짓 표시가 생기지 않는다
  (**규약 구현 위치는 `RewardCardView.Bind` — #320에서 `WaveRewardSelectionUI`에서 옮겨왔다.**
  같은 이유로 **등급 별도 0개로 비운다** — 수치가 없는데 별만 채우면 같은 거짓 표시가 된다).
  ⚠ **값마다 따로 조회하지 말 것**(#353) — 조회가 흩어지면 "이 표시 요소는 현재 레벨인가 다음 레벨인가"가
  호출부마다 갈리고, 실제로 별과 레벨 줄이 서로 다른 레벨을 가리켰다. 카드면 색을 정하는 `WaveRewardSelectionUI`도
  같은 스냅샷을 넘겨받는다. **레벨 상한 조회(#292)**: `IsMaxLevel(WaveRewardType)`(만렙 여부 — 보상 후보 필터가 쓴다,
  효과 미부착 시 false라 후보에 남아 `ApplyReward` 경고로 배선 사고가 드러난다).
- `RewardCardView.Bind(WaveRewardData reward, SkillEffectSnapshot snapshot, Action<WaveRewardData> onSelect)`(#320, 스냅샷 인자 #353, 등급 스킨 #356) — 보상 카드
  한 장을 그리는 유일한 창구. 카드 프리팹 루트에 붙으며, **자기 자식 참조만 알고 후보 구성은 모른다**
  (`SkillVisualSet`이 레벨→프리팹 매핑을 소유하고 `SkillManager`가 조회만 하는 것과 같은 분리). 이름·설명은
  `LocalizationHelper.Get(k_RewardsTable, …)` 직접 호출이라 `LocalizeStringEvent` 배선이 없다(`TowerInfoUI` 선례).
  **등급 표현은 색 틴트가 아니라 등급별 스킨 스프라이트 교체다(#356)** — 도달 레벨이 가리키는 **스킨 한 벌**
  (`LevelSkin`: `face`/`namePlate`/`descPlate`/`iconFrame`)을 대응 Image 4개(`cardFace`/`namePlate`/`descPlate`/
  `iconFrame`)에 넣는다. 슬롯을 배열 4개로 흩지 않고 한 벌로 묶은 이유는 등급이 늘 때 배열 길이 4개를 따로
  맞춰야 하는 상태를 만들지 않기 위해서다 — **한 벌이 곧 한 등급이다.** 배열(`levelSkins`)은 카드 프리팹이
  소유하고 **코드는 색 이름을 모른다**; 어느 색을 몇 번째에 둘지는 인스펙터가 정한다.
  **슬롯별로 Image가 미배선이거나 스프라이트가 null이면 그 슬롯만 건너뛴다**(`ApplySprite`) — 아트가 아직 안 온
  칸이 있어도 나머지는 정상 동작해야 하기 때문. 도입 전에는 카드면 한 장에 `WaveRewardSelectionUI.levelColors`
  (동/은/금)를 틴트로 얹었는데, 카드 아트가 색까지 담게 되면서 틴트가 이중으로 곱해져 탁해졌다 — `levelColors`·
  `DefaultLevelColors`·`ResolveTint`는 함께 제거됐다. **레벨→스킨 매핑은 배열을 가진 `RewardCardView`가 통째로
  소유한다** — `ApplySkin`이 스냅샷에서 인덱스(`Mathf.Max(NextLevel - 1, 0)`)를 직접 파생하고 하한·상한 클램프와
  경고까지 한 곳에서 한다(별 표시가 스냅샷에서 파생하는 것과 같은 경로). 그래서 `Bind`에 인덱스 인자가 없다.
  경고는 **두 단계**다 — 배열이 아예 비었으면(0벌, 프리팹 미동기·구본 배선) "배선되지 않았습니다",
  벌 수가 도달 레벨보다 적으면 "모자랍니다". 앞은 기능 전체 무효화, 뒤는 최고 두 레벨이 같은 스킨을 쓰는 부분
  붕괴라 둘 다 조용히 넘기지 않는다.
  ⚠ **증가폭은 보상 SO가 아니라 레벨 규칙이 소유한다(#292)**: `WaveRewardData.amount`가 제거되어 **한 번 선택 = 1레벨** 고정이다.
  수량형 보상(마나석 등)은 이 트랙에 넣지 않는다는 것이 팀 결정이며(GDD §5.6), 되살리면 증가폭이 SO와 효과 양쪽에 생겨 표시/실효가 갈린다.
  ⚠ **`SkillEffect` 파생 계약이 강해졌다(#287)**: `public abstract string GetStatSummary()` 때문에
  **파생 클래스만 만들면 컴파일이 깨진다.** 수치 표시가 없는 효과가 조용히 출시되는 것을 막기 위한 의도된 강제다.
  라벨·서식은 파생이 각자 조립하지 않고 `SkillStatsFormatter`(단일 출처, `TowerStatsFormatter` 대응)에 추가한다.
  스킬 스탯 라벨의 스트링 테이블은 `NorthLand_Skills`(`skills.stat.*`)로, 타워 스탯 라벨(`NorthLand_default`의
  `game.tower.*`)과 **의도적으로 분리**돼 있다 — 스킬 문자열 증가와 `NorthLand_default` 병합 충돌 회피가 이유
- `Projectile.DamageDealt`(`static event Action<IAttacker, IDamageable>`, `NorthLand.Combat`, #169 muchan 추가) —
  투사체 데미지가 실제로 들어간 직후 발행(단일/스플래시/체인 전 경로). **static이므로 구독 해제는 구독자 책임**
  (파괴된 MonoBehaviour를 남기면 죽은 구독자 호출 버그). 현재 구독자: **`RampAction`**(`Trigger=Hit`일 때만, #300).
  구 구독자 `BurnBuff`는 #315로 미사용. ⚠ **빔 타워(`BeamAction`)는 이 이벤트를 발행하지 않는다** —
  여기 붙는 기능은 빔에서 조용히 빠진다(WL-155, `TowerAsset.OnValidate`가 저작 시점 경고만 낸다)
- **타워 본체 문서 = [`Docs/Core/Tower.md`](../Core/Tower.md)** — 조립 모델·투사체·데이터 파이프라인·스탯 원장·
  능력 질의의 정본. **전부 현행 코드 기준이므로 그대로 리뷰 기준선으로 쓴다.**
  구조 재설계 문서는 [`Docs/Core/TowerRedesign.md`](../Core/TowerRedesign.md)(#274) — **Phase 1~4는 구현·병합
  완료라 그 부분은 `Tower.md`가 정본이고, 남은 Phase 5(합성 효과 계승)만 합의 대기다.** 리뷰 기준은 `Tower.md`
- ⚠️ **#274 이름 변경 — 위 표(§1)와 아래 일부 서술에 남은 구 이름은 전부 삭제된 것들이다.**
  `ITowerBehaviour`→`TowerAction` · `AttackBehaviour`→`AttackAction` ·
  `BuffAuraBehaviour`→`BuffAuraAction` · `DebuffAuraBehaviour`→`DebuffAuraAction`.
  `TowerBehaviourFactory` · `TowerBuildContext` · `TowerAssetEditor` · `TowerType`/`MagicEffectType` enum은 **삭제**.
  능력 질의는 `Has<AttackAction>()`, 프리뷰 반경은 `TowerAsset.PreviewRadius`(구 `MagicRadius`).
  (§1 표 행은 4천 자짜리 단일 줄이라 고치면 diff를 읽을 수 없어 그대로 뒀다 — 상세는 `Tower.md` §3)
- **타워는 단일 구상 타입 `Tower` 하나뿐이다(#164 리팩토링).** 공격/버프 오라/디버프 오라의 차이는 상속이 아니라
  **행동 조립**으로 표현한다 — 구 `AuraTower`(별개 MonoBehaviour)는 폐기됐다. 상호작용·합성·스킬·BT 계층은
  `Tower` 하나만 알면 되고, "이 타워가 무엇을 하는가"는 타입 검사가 아니라 `Has<T>()`로 묻는다
- `Tower.Build(TowerAsset asset)`(`NorthLand.Combat`) — **타워가 무엇을 하는 물건이 되는지 결정하는 유일한 지점.**
  **액션을 만들지 않고 프리팹에 이미 담긴 것을 초기화만 한다**(#274, 구 `TowerBehaviourFactory` 경로 폐기) 후
  `Active`에 등록한다. `TowerPlacer.PlaceTower`가 배치 확정 시
  호출하며, 씬 배치·테스트 씬은 `OnEnable`이 직렬화된 `data`로 자가 조립(폴백)한다. 같은 SO로 재호출하면 재무장만,
  다른 SO면 경고 후 재조립(**패널에서 산 SO가 프리팹이 문 SO를 이긴다** — 구 WL-129의 무증상 불일치 해소).
  ⚠️ **`data`가 없으면 아무것도 하지 않고 `Active`에도 들어가지 않는다** — "조립되지 않은 타워는 존재하지 않는 타워"
- `Tower.Has<T>()` / `Tower.Get<T>()`(`where T : TowerAction`) — **능력 질의 창구.** 소비처가 타워의 구상 타입이
  아니라 능력을 묻게 한다. 예: 보스 P3 마력 봉인 대상 = `Has<AttackAction>()`(`EnemyNodeQuery.IsAttackTower`)
- `Tower.Stats`(`TowerStats`, `NorthLand.Combat`) — **이 타워의 스탯 modifier 단일 원장.** 타일 버프·
  버프 오라·보스 봉인이 전부 여기로 수렴하며(버프 스킬도 소비처였으나 #315로 제거) 합성 규칙은 `TowerStats.Evaluate` 한 곳에만 산다:
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
  `BuffAuraAction`이 폴링 없이 사거리 내 대상을 재계산하는 트리거(타워는 스스로 움직이지 않으므로 대상 집합이
  바뀌는 순간은 이때뿐). **static이므로 구독 해제는 구독자 책임**(F7) — 액션엔 `OnDestroy`가 없으므로 구독이
  `Initialize`↔`Dispose` **대칭 쌍**이고(#274, 구 F7 예외 해소), 이벤트는 더티 플래그만 세워 실행은 `Tick`에서 한다
- `Tower.ApplyBuff(int sourceId, float damageMul, float attackSpeedMul, float duration)` — **원장 위의 얇은 어댑터.**
  기존 소비처가 무수정으로 남도록 시그니처를 유지한다(#164 당시 `BuffSkillManager`·`EnemyApplyTowerDebuffAction`
  두 곳이었고, 전자는 #315로 사라져 **현재 실 소비처는 `EnemyApplyTowerDebuffAction` 하나뿐**) —
  내부에서 `Stats.Apply`로 AttackDamage/AttackSpeed 배율 modifier 2개를 등록한다. duration>0=시간제, ≤0=지속형.
  소스키 도메인: ~~버프 스킬=`"skill.player_buff"` 해시(#315로 미사용)~~ / 버프 오라=행동 `GetInstanceID()` /
  **디버프 오라=행동 `GetInstanceID()`(구 `TowerID` 해시에서 변경 — 같은 종류 감속 타워가 중첩되지 않던 문제 해소)** /
  타일 버프=`"TowerTileBuff"` 해시(지속형)
- `Tower.RemoveBuff(int sourceId)`(`NorthLand.Combat`) — 해당 소스의 modifier를 즉시 제거
- **`Enemy.Killed`**(`static event Action<IAttacker, Enemy>`, `NorthLand.Combat`, #300) — 적이 **처치되어**
  사라질 때 1회 발행. 첫 인자는 마지막으로 피해를 준 주체(모르면 null). `Die()`의 `isDying` 게이트 뒤라
  같은 프레임 다중 타격에도 중복되지 않는다. **static이므로 구독 해제는 구독자 책임**(`Projectile.DamageDealt`와 같은 규율).
  세 가지 성질이 배치에서 따라온다: ① **본진 도달 이탈은 킬이 아니다**(`HandleRouteCompleted`가 `Die()`를 우회)
  ② **DoT 처치는 귀속된다**(`StatusEffectHandler`가 원 소스를 전달) ③ **소스가 null인 피해(스킬·환경)로 죽으면
  아무에게도 귀속되지 않는다** — `TakeDamage`가 소스를 조건 없이 덮어쓰므로 마지막 일격 기준이다
- **`TowerAction.OnWaveEnd()`**(`virtual`, 기본 no-op, `NorthLand.Combat`, #300) — 웨이브 종료(밤→낮) 시
  호스트가 전 액션에 브로드캐스트한다. "이 웨이브 동안만" 유효한 상태를 버리는 자리.
  **`Tower.Update`가 이미 매 프레임 읽는 페이즈 값의 전이를 보고 부른다**(이벤트 구독 아님) — ① 페이즈 게이팅과
  신호원이 하나로 유지되고(WL-044 축) ② `DayNightManager.Instance`가 아직 null인 시점에 조립된 타워가 구독을
  놓쳐 **그 타워만 영구히 통지를 못 받는** 무증상 실패가 없으며 ③ 구독 해제 누락 경로가 없다.
  `ActivePhase`와 **무관하게** 호출된다. ⚠ `NightOnly` 액션은 낮에 `Tick`이 아예 돌지 않아 마지막 프레임 상태가
  굳으므로(진행 중 잠금, 켜진 `LineRenderer`) 그런 상태가 있으면 이 훅을 구현해야 한다 — `BeamAction`이 실제로
  그 버그를 갖고 있었고 #300에서 이 훅으로 해소했다.
  **#202(`SkillEffect.OnWaveEnd`)가 스킬 쪽에 요구하는 것과 같은 이름·같은 브로드캐스트 방식이다** — 스킬 담당자는
  `Tower.Update`/`TowerAction`의 이 구현을 그대로 참고하면 된다
- **`RampAction`**(`TowerAction` 파생, `NorthLand.Combat`, #300) — 전투 실적으로 자기 타워의 원장에 소스를
  하나 얹는 액션. 트리거는 SO(`TowerAsset.Ramp.Trigger`)가 고른다: `Hit`=`Projectile.DamageDealt` 구독 /
  `Kill`=`Enemy.Killed` 구독(둘 다 `source == Owner`만 센다). `ActivePhase = Always`(낮에도 감쇠가 돌아야
  다음 밤이 0에서 시작한다). 공개 읽기: `Stacks` / `Multiplier`.
  ⚠ **타워당 1개까지** — `TowerAction.SourceId`가 `호스트 ID ^ 액션 타입명`이라 둘이면 원장 슬롯이 충돌한다
- **`Tower.AcquireTarget()`**(`IDamageable`, `NorthLand.Combat`, #336 → #387) — 사거리 안에서 **조준 정책이 고른 1위**.
  **"이 타워가 지금 누구를 겨누는가"의 단일 출처**다. 예전에는 공격 액션과 포탑 조준 연출이 각자
  `OverlapSphere`를 돌렸는데, `TowerAction.Origin`이 `Owner.transform`이라 원점·반경·마스크·판정 기준이
  **완전히 같은 쿼리 두 벌**이었다 — 비용보다 "대상이 누구인가"의 정의가 둘로 갈리는 것이 문제다
  (조준 정책을 바꾸면 한쪽만 고쳐져 포탑이 겨눈 적과 실제로 맞는 적이 달라진다).
  **프레임당 실제 조회는 1회**고 같은 프레임의 이후 호출은 캐시를 돌려주므로, 소비처들이 서로의 호출
  주기를 몰라도 된다(발사 프레임에는 액션이 `Update`에서 계산하고 연출이 `LateUpdate`에서 받는다).
  ⚠ **사거리를 `AttackRange`(= `AttackAction.Range`)에 고정하므로 공격 액션이 없는 타워에서는 항상 null이다** —
  `BeamAction`의 자체 탐색은 그대로 남아 있어 조회가 아직 두 벌이다(WL-178)
  ⚠ **아무것도 붙들지 않는다(#387)** — "지금 1위가 누구인가"만 답한다. 대상 **고정**은 연발 사이클 동안
  `AttackAction`이 소유한다(`burstTarget`). 호스트가 대상이 죽을 때까지 붙들면 조준 정책이 "재선정하는
  순간"에만 의미를 갖게 되어, `뒤처진 적`처럼 새 적이 스폰될 때마다 1위가 바뀌는 정책이 동작하지 않는다
  (`앞선 적`은 선두가 계속 선두라 증상이 안 보여 **정책마다 다르게 고장 난다** — 실측으로 확인하고 되돌렸다)
- **`TargetingPolicy`**(`abstract`, `[Serializable]`, `NorthLand.Combat`, #387) — "사거리 안의 **누구를**
  겨누는가"의 규칙. `TowerAsset.Targeting`에 `[SerializeReference]`로 담긴다. 파생 5종:
  `FirstTargeting`(앞선 적, **기본값**) / `LastTargeting` / `NearestTargeting` / `HighestHpTargeting` / `LowestHpTargeting`.
  ★ **모든 정책이 "점수 하나를 최대화"로 환원되므로 정책은 `Score` 하나만 갖고 스캔 루프는 `Tower.FindTarget`
  한 곳에 그대로 남는다** — 정책마다 자기 탐색을 갖는 구조였다면 #336이 없앤 "조준 연출과 실제 사격의
  정의가 갈리는" 실패가 그대로 돌아온다. 새 조준 방식 = 파생 1개(enum·switch·에디터 무수정).
  ⚠ `Score`가 `float.NegativeInfinity`면 "순위를 매길 수 없다"는 뜻이고, 후보 **전원**이 그럴 때만
  호출부가 최근접으로 폴백한다 — 축이 다른 값(체력 점수 vs 거리 점수)을 한 비교에 섞지 않기 위함이다.
  ⚠ **인게임 순환 목록 `TargetingPolicy.All`은 손으로 채운다** — 파생만 만들면 SO 드롭다운(에디터
  `TypeCache`)에는 뜨지만 인게임 전환에는 안 나온다(런타임엔 `TypeCache`가 없고 리플렉션 열거는
  IL2CPP 스트리핑 위험). **정책에 수치 필드를 두지 말 것** — `All`이 무상태 전제로 인스턴스를 공유하므로
  필드가 생기는 순간 모든 타워가 그 값을 공유한다(WL-141과 같은 함정)
- **`ITargetProfile`**(`interface`, `NorthLand.Combat`, #387) — 조준 정책이 읽는 표적 부가 정보
  (`CurrentHp` / `MaxHp` / `RemainingRouteDistance`). **`Enemy`만 구현한다** — `IDamageable`에 얹지 않은
  이유는 그쪽이 "맞을 수 있는 모든 것"이라 `PlayerBase`·`Soldier`에게 "종점까지 남은 경로"가 무의미해서다.
  스캔은 후보마다 `as`로 물어보고, 없으면 정책이 순위 밖으로 뺀다.
  ⚠ **경로를 모르면 `RemainingRouteDistance`는 `NaN`이다**(0·무한대 금지 — 아래 참조)
- **`ITargetingSelector`**(`interface`, `NorthLand.Combat`, #387) — 인게임 조준 전환 창구
  (`TargetingName` / `CycleTargeting(step)`). `Tower`가 구현하고 `TowerInfoUI`가 소비한다.
  뷰가 `Tower`를 통째로 알지 않게 **둘만** 노출한다(패널의 pull 규칙 유지).
  ⚠ 전환값은 **인스턴스**(`Tower.targetingOverride`)가 소유한다 — `TowerAsset`에 쓰면 그 종류의 모든
  타워가 함께 바뀌고, `[SerializeReference]`라 에디터 플레이 모드의 클릭이 `.asset`에 영구 저장된다
  (`activeKinds`와 같은 축의 함정). 우선순위는 **인게임 전환 > SO 저작값 > 기본(앞선 적)**이며,
  다른 SO로 재조립되면(`Build`) 전환값도 함께 버린다
- **`IRouteMovementAgent.RemainingRouteDistance`**(`float`, `NorthLand.Combat`, #387, 소유 시스템 = MonsterMovement)
  — 종점(본진)까지 **경로를 따라 잰** 남은 길이. `앞선 적`/`뒤처진 적` 판정의 유일한 근거다.
  ⚠ **직선거리로 대신할 수 없다** — 경로가 꺾이는 맵에서는 직선상 가까운 적이 경로상으로는 한참 뒤일 수 있어
  순서가 그대로 뒤집힌다.
  ⚠ **값 규약**: `0` = 완주(종점 도달) / **`NaN` = 경로를 모른다**(순위 밖). 둘을 같은 값으로 내면 안 된다 —
  `FirstTargeting`의 점수가 `-잔여거리`라 **0은 실제 후보 전부를 이기는 최고점**이고, 경로가 빈 몬스터
  한 마리가 맵의 모든 타워를 독점한다(도입 시 실제로 이 버그가 있었다).
  계산은 `RouteDistanceTracker`(순수 C#, `Assets/Scripts/CombatSystem/`)가 소유하며 경로 확정 시 누적을
  1회 만들어 조회는 O(1)이다 — 재조준마다 **타워 수 × 후보 수**만큼 읽힌다
- **`Tower.IsCombatPhase`**(`bool`, 읽기 전용, `NorthLand.Combat`, #336) — 지금이 전투 시간(밤)인가.
  호스트가 `Update`에서 이미 계산해 둔 값을 그대로 공개한다. 연출 컴포넌트가 각자 `DayNightManager`를
  폴링하면 페이즈 규칙이 갈라지므로(WL-044) **신호원을 하나로 유지하려고** 새로 만들지 않고 있는 것을 열었다
- **`Projectile.Impacted`**(`event Action<Vector3>`, `NorthLand.Combat`, #336) — 착탄 **위치** 통지.
  착탄 지점에 남는 지속물(화상 구역 등)을 만들기 위한 창구다. `"Projectile.cs` 무수정" 원칙의 예외로,
  착탄 위치를 아는 지점이 `OnHit` 하나뿐이라 우회가 없다. ⚠ static인 `DamageDealt`와 달리 **인스턴스
  이벤트**라 구독자가 탄 한 발에만 붙고 탄이 파괴되면 함께 사라진다 — **해제 책임이 없다**.
  ⚠ 관통·부메랑처럼 여러 번 때리는 탄은 **명중마다 발행**되므로 한 번만 반응할 구독자는 스스로 걸러야 한다
- **`GroundZone`**(`MonoBehaviour`, `NorthLand.Combat`, #336) — 착탄 지점에 남아 반경 안의 적에게 효과를
  재적용하는 지속 구역. **신규 런타임 인프라가 0이다** — 루프는 `DebuffAuraAction.ApplyDebuff`와 같고,
  효과 소유·지속시간 소진은 여전히 대상의 `StatusEffectHandler`가, 수치는 `TowerAsset.Effects`가 맡는다.
  `DebuffAura`(타워 중심 고정)와 **다른 축**이다 — 중심이 착탄점이고 수명이 있다.
  ⚠ **소스 키를 장판 인스턴스별로 채번한다** — 타워 인스턴스로 채번하면 구역 2개가 겹쳐도 대상의 DoT 슬롯
  하나를 공유해 중첩이 사라진다(2연발의 "두 개 겹치면 두 배"가 여기 걸려 있다).
  이펙트 프리팹에 이 컴포넌트가 없어도 된다 — **런타임에 붙이므로** 벤더 파티클 팩 프리팹을 무수정으로 지정할 수 있다
- **`TowerTurretAim`**(`MonoBehaviour`, `NorthLand.Combat`, #336) — 포탑 마디를 조준 대상 쪽으로 돌리는
  **연출 전용** 컴포넌트. `TowerReloadVisual`과 같은 축이라 붙이지 않아도 **전투 결과가** 달라지지 않는다.
  ⚠ **사격은 이 회전을 기다리지 않는다** — 얽으면 선회 속도가 곧 DPS 노브가 되어 연출값이 밸런싱 표 밖에서
  화력을 흔든다
  ⚠ **다만 "전투 무관"이 "없어도 되는 것"은 아니다.** 이 컴포넌트는 `TargetLost`(적을 잃고 대기로 정착한 순간,
  `targetLostGrace`로 경계 채터 흡수)도 함께 내고, 그 신호가 `TowerAnimationVisual`이 **루프로 저작된 발사
  상태를 빠져나오는 유일한 출처**다. `turret` 미할당이면 `LateUpdate`가 조기 반환해 `TargetLost`가 영영
  발행되지 않으므로, 저작 상태를 `PublishesTargetLost`(읽기)로 노출해 소비처가 `Awake`에서 경고한다(WL-193)
- **`TowerAnimationVisual`**(`MonoBehaviour`, `NorthLand.Combat`, #359) — 타워 모델의 Animator를 생애 사건
  (발사·설치·해제·적 소실)에 맞춰 재생하는 **연출 전용** 컴포넌트. 모델 팩 컨트롤러는 파라미터가 전부
  Trigger라 **누가 켜주지 않으면 영원히 Idle만 돈다** — 프리팹을 제대로 물려도 모션이 안 나오는 이유가 여기 있다.
  공개 API는 `PlayFire`/`PlayReload`/`PlayIdle`/`PlayInstall`/`PlayRemove`(철거 경로가 없어 `PlayRemove`는
  호출자 대기 중). 발사는 `fireState`(상태 직접 재생 — 연사에서 반동이 사격과 어긋나지 않는다) 또는
  `fireTrigger`(그래프의 전이 규칙에 맡김) 중 하나.
  ⚠ **팩마다 `Fire` 저작이 정반대다** — Part2(CrossBow·Culverin)는 exitTime으로 `Idle` 복귀하지만
  Part4(MachineGun·Minigun)는 `Fire` 클립이 `m_LoopTime: 1`이고 `Fire`에서 나가는 전이가 전부 조건부라
  **무조건 탈출이 없다.** 그래서 정지가 그래프의 성질이 아니라 **저작 사항**이고, 어긋나면 예외도 경고도 없이
  밤새 발사 모션이 반복된다 — `Awake`가 그 조합(`idleTrigger` 또는 `playReloadOnTargetLost`+`reloadTrigger`,
  그리고 `TowerTurretAim.turret`)을 검사해 소리를 낸다. 저작 절차는 `TowerAddGuide.md` §3.3 함정 ④ (WL-193)
  ⚠ 설치 지연은 **unscaled**(등장 연출 `TowerSpawnEffect.ConvergeDuration`과 같은 축), 장전 지연은
  **scaled**(공격 쿨다운과 같은 축)다 — 두 지연이 같은 API를 쓰지 않는 것이 요점이다(WL-179)
- **`RampProfile`**(`[Serializable]`, `NorthLand.Combat`, #300) — 램프 **수치 규약**(`PerStack`/`MaxStacks`/
  `StackInterval`/`DecaySeconds`)과 스택→배율 환산(`Multiplier`/`StacksFromTime`). 소비처가 둘이고 **적용 지점이
  다르다**: `RampAction`(원장, 타워 전역) / `BeamAction.Beam.LockRamp`(대상별, 원장 미경유 — 원장은 타워 단위라
  "대상이 바뀌면 리셋"을 표현할 수 없다). ⚠ `DecaySeconds = 0`은 "영구"가 아니라 **"웨이브 동안 유지"**다
- `Tower.ApplyTileBuff(TileBuffCalculationResult)`(`NorthLand.Combat`) — 타일 버프를 원장에 지속형 소스로 넣는다.
  `TowerTileBuff.Initialize`가 **푸시**한다(스탯 게터가 되읽지 않음) → 초기화 순서 의존이 없다.
  ✅ **`Tower.Build`와의 호출 순서 의존은 #274에서 해소됐다** — 오라의 `Radius`가 접근할 때마다 원장을 평가하고
  `OnInitialize`에서 캐시하지 않는다(양쪽 순서 실측 동일). `TowerPlacer`의 현재 순서는 유지하되 어겨도 증상이 없다
- `Tower.Asset`(`TowerAsset`, 읽기 전용, `NorthLand.Combat`, #195 muchan) — 배치된 타워의 원본 SO 조회(합성 재료 TowerID 매칭용). 순수 읽기
- `TowerAction`(`NorthLand.Combat`, #274) — 타워가 하는 일 한 조각. **MonoBehaviour가 아니라 프리팹에
  `[SerializeReference]`로 직렬화되는 순수 C# 클래스**다. `ActivePhase`(NightOnly/Always) /
  `Initialize(Tower owner, TowerAsset asset)` / `Tick(dt)` / `Dispose()` / `DisplayRange` / `DescribeStats()`.
  구현 3종: `AttackAction`(Single/Area/Chain — 차이는 `ProjectileImpact` 전략뿐) ·
  `BuffAuraAction`(이벤트 구동, Always) · `DebuffAuraAction`(Tick 폴링, NightOnly).
  **규약**: ⓐ 초기화는 `OnInitialize`만 ⓑ 스스로 `Update`를 돌지 않는다(호스트가 게이팅 후 `Tick`)
  ⓒ 외부에 남긴 상태는 `Dispose`에서 걷어낸다. **페이즈 게이팅을 호스트가 한 곳에서 처리**하는 것이
  구 WL-044의 재발 방지책이다. 액션이 지켜야 할 **설계 규칙 4가지**(수치는 SO / 배선은 `Owner` /
  런타임 상태 비직렬화 / `SourceId` 채번)는 `TowerAction.cs` 상단과 **`Docs/Core/Tower.md` §3.1**에 있다.
  **`DisplayRange`**: 선택 시 그릴 사거리 원의 반경(공격=교전 사거리 / 오라=오라 반경 / 없으면 0).
  호스트는 액션들이 보고한 값의 **최대치**로 원을 그린다. `AttackRange`로 대신 그리면 공격 액션이 없는
  오라 타워에서 0이 되어 원이 사라진다(#192 회귀 — 정보 패널엔 반경이 뜨는데 바닥 원만 없어 더 눈에 띈다).
  `DescribeStats`와 같은 "액션이 자기 표시를 안다" 규약
- **타워 종류의 정본 = 프리팹의 `Actions` 리스트**(#274). 구 `TowerBehaviourFactory`(`TowerType`/`MagicEffectType`
  switch가 살던 유일한 곳)와 `TowerAssetEditor`는 **삭제됐다.** 배치 **전** 능력 질의는 `TowerAsset.HasAction<T>()`,
  프리뷰 반경은 `TowerAsset.PreviewRadius`(구 `MagicRadius`), 저작 검증은 `TowerAsset.OnValidate`(WL-130 해소).
  ⚠️ **`Actions`가 빈 타워는 예외도 경고도 없이 아무 동작을 안 한다** — 프리팹 9개가 `Assets/Imported/`
  (부모 저장소 밖, 자체 중첩 git)에 살아 **"타워가 안 움직인다"의 1순위 원인이 그 저장소 미동기화**다.
  상세는 **`Docs/Core/Tower.md` §3·§4.3·§6**
- `ProjectileFlight` 부품(`NorthLand.Combat`, #274 Phase 4.5) — 비행 축이 enum + `Update` 분기에서
  **`[SerializeReference]` 부품**이 됐다(`TowerAsset.Attack.Flight`). 구현 2종 `HomingFlight`/`BallisticFlight`.
  **새 비행 방식 = 파생 1개이고 `Projectile.cs`는 무수정.**
  ⚠️ **부품은 무상태여야 한다** — SO에 살아 그 타워가 쏜 투사체 전부가 같은 객체를 공유하므로,
  진행값은 `FlightState`에 담아 `Projectile`이 소유하고 `ref`로 넘긴다(액션과 **정반대** — 액션은
  프리팹에 담겨 인스턴스마다 복제된다).
  `FlightStep`의 **`Impact`와 `Finished`가 독립**이라 "때리고도 계속 나는 탄"(관통·부메랑)이 표현된다.
  명중 축(`ImpactKind` switch)은 의도적 보류. 상세: `Docs/Core/Tower.md` §3.7
- `Editor/ManagedReferencePickerDrawer.cs`(#274 Phase 4.5) — **단일 `[SerializeReference]` 필드의 타입 선택 UI.**
  Unity는 `List`로 감싼 managed reference에만 `+` 피커를 주고 단일 필드에는 안 준다. 기반 타입을
  런타임에 읽는 범용 드로어라 **새 부품 축은 빈 파생 클래스 한 줄로 등록**한다(타입 교체 시 같은 이름 수치 승계)
- `Tower.ActiveEffectKinds` / `ActivateEffects(kinds)` / `IsEffectActive(kind)`(#274 Phase 5) —
  **합성으로 계승된 효과 종류를 SO가 아니라 인스턴스가 소유한다.** SO에 쓰면 다음 합성이 이전 계승분을
  물려받고 `[SerializeReference]`라 `.asset`에 영구히 남으며, 다단 합성에서 꺼진 효과까지 잡힌다.
  적용(`Projectile.ApplyEffects`·`DebuffAuraAction.ApplyDebuff`)과 표시(`DescribeEffects`)가
  **`IsEffectActive` 한 술어를 공유**한다 — 갈라지면 "패널엔 뜨는데 안 걸리는" 어긋남이 된다
- `TowerFusionMatcher.ResolveInheritedKinds(recipe, materials)` — 계승 규칙 단일 출처. 툴팁
  (`TowerMergeCoordinator.PreviewInheritedKinds` → 핑크 프리뷰와 **같은 소모 대상** 판정)과 실행부
  (`TowerFusionController.TryFuse`)가 공유한다. opt-in은 `TowerRecipe.InheritEffects`(레시피별).
  ⚠️ **현재 켠 레시피는 0개** — 배관만 있고 게임 동작은 그대로다(족보는 기획 미정). 상세: `Tower.md` §3.9
- `TowerStatsFormatter`(`NorthLand.Combat`) — 스탯 표시 문자열 단일 출처(구 WL-079: `Tower`/`AuraTower`/`TowerTooltipView`
  3벌 복제 해소). 표시 경로는 둘로 갈리지만(배치 **전** 툴팁은 인스턴스가 없어 SO 원본을 본다) **라벨과 서식은 여기 한 곳**
- `AuraModifiers`(`NorthLand.Combat`, 순수 static) — SO의 `StatModifier` → 적용 값 환산.
  `ComputeSlowMultiplier(mods)`([0,1] 클램프) / `ConvertBuffModifiers(mods, dst)`. 적용부와 표시부가 같은 식을 공유
- `TowerRecipe`(SO, `Assets/Scripts/Data/Tower/TowerRecipe.cs`, #194) — `Materials`(재료 `TowerAsset`+개수)/`Result`(결과 `TowerAsset`)/`ExtraCost`(`List<ResourceCost>`). **인스펙터 손입력(CSV 미경유** — 재료·결과가 SO 참조라 ID 문자열 resolve보다 직접 드래그가 자연스러움)
- `bool TowerPlacer.BeginTowerPlacement(TowerAsset result, IReadOnlyList<ResourceCost> cost, System.Action onConfirmed, System.Action onEnded = null)`(#195) — 비용·확정 콜백 주입 오버로드(합성 결과 배치용. 확정 직후 콜백에서 합성이 재료 파괴를 **확정**한다 — 소모 자체는 그보다 앞선 버튼 클릭 시점, #263). 기존 `BeginTowerPlacement(TowerAsset)`은 `cost=so.Cost, onConfirmed=null`로 위임(동작 불변). 확정 시 `_management.TrySpend(cost)` 후 `Instantiate`.
  **`onEnded`** = 확정/취소/다른 배치로 교체 무관 **배치 세션 종료 1회**(`PlacementRequest.OnEnded`→`EndPlacement`에서 발화, 먼저 비우고 호출). **어느 쪽으로 끝났는지는 알려주지 않는다** — 구분이 필요한 소비처는 자기 상태로 판단해야 한다(합성 커맨드가 `Committed` 플래그로 그렇게 한다, #263). **반환값** = 세션이 실제로 시작됐는가 — false면 `onEnded`도 오지 않으므로 호출부가 "배치 동안 유지"할 상태를 걸면 안 되고, 이미 저지른 부수효과가 있으면 그 자리에서 되돌려야 한다(합성이 소모한 재료를 `Undo`한다).
  ⚠️ `onConfirmed`/`onEnded`는 `MouseManager.BeginPlacement` **이후에** 대입해야 한다 — `BeginPlacement` 내부 `CancelPlacement`가 직전 세션의 `EndPlacement`를 발화해 콜백을 소비·초기화하기 때문
- `bool TowerFusionController.TryFuse(TowerRecipe recipe, TowerMergeGroup group)`(#195, #183·#263에서 시그니처 변경) — 합성 실행. 포함 매칭+`CanAfford` 검증→**재료 소프트 소모(`TowerMergeCommand.Execute`)**→`TowerPlacer` 배치→확정 시 `Commit`(재료 `Destroy`)/취소 시 `Undo`(원복). 반환값(=배치 세션이 실제로 시작됐는가)은 `BeginTowerPlacement`를 그대로 통과시키며, **false면 소모한 재료를 즉시 `Undo`한다**(그 경로엔 종료 통지가 오지 않는다). 코디네이터 `RequestMerge`가 그룹을 물려 호출.
  **구 `onEnded` 인자는 #263에서 제거** — 유일한 소비처였던 합성 핑크 고정이 폐기됐고, 그 인자는 확정과 취소를 구분하지 못했다. **#265에서 확정/취소 구분이 필요해졌지만 인자를 되살리지 않았다** — 커맨드의 `IsConfirmed`(#281 이전 `IsCommitted`)를 종료 콜백 안에서 읽어 `Reassemble`(취소) / `Abort`(확정) 로 갈랐다. 확정/취소 판단의 단일 출처가 여전히 커맨드 하나다.
  **⚠ #281에서 이 종료 콜백이 조건부가 됐다** — `Confirmed`에서 `Undo`가 이제 **동작하므로** `IsConfirmed`면 `Abort`만 하고 먼저 반환해야 한다(무조건 부르면 확정한 합성이 세션 종료만으로 되감긴다, `TowerMerge.md` §9)
- `float TowerPlacer.TileSize`(#265) — 셀 간격. `Awake`에서 신맵 설정을 단일 출처로 해석해 둔 값이라, "타일 한 칸"을 기준 길이로 쓰는 쪽(합성 연출의 알갱이 크기)이 같은 해석을 다시 하지 않는다
- ⚠ `TowerPlacer.BeginTowerPlacement`의 `onConfirmed`가 `Action` → `Action<Transform>`(#265) → **`Action<TowerPlaceCommand>`**(#281)로 바뀌었다. 합성이 **결과 배치의 되돌리기 커맨드를 편입**해야 하기 때문이며,
  연출이 쓰던 Transform은 `command.Placed`로 같다 — 등장 연출이 스케일을 0으로 만들기 **전에** 재야 한다는 #265 계약도 그대로다
- `IReversibleCommand { bool Execute(); void Confirm(); void Commit(); void Undo(); bool IsConfirmed { get; } }`(#263, **#281에서 4단화**, `Assets/Scripts/Command/`) — `Confirm`=세션 성공(히스토리 등록 시점, 되돌리기는 **여전히 가능**) / `Commit`=되돌리기 불가 확정(밤 진입).
  두 뜻을 쪼갠 경위는 `IReversibleCommand.cs` 헤더, **진행 중(`Executed`) 커맨드 ≤ 1** 불변식(#263에서 불변)은 `TowerMerge.md` §9.1.
  구현체가 둘(합성·배치)이 되면서 중립 위치로 이전(구 위치 정책 해소, 전역 네임스페이스 유지)
- `CommandHistory`(#281, static, `Assets/Scripts/Command/CommandHistory.cs`) — `Push`(`Confirm()`도 여기서 부른다)/`Undo`/`CommitAll` + `OnChanged`/`CanUndo`/`Count`. **LIFO 깊이 20**(초과분은 버리지 않고 `Commit`).
  `GroupSelectableRegistry` 계보(static + `SubsystemRegistration` 리셋)라 씬 배선이 없고, `DayNightManager.OnDayToNight`에 지연 자기구독한다.
  ⚠ 그 자기구독(`EnsureSubscribed`)은 **`Push`와 `CanUndo` 양쪽에서** 돌아야 한다 — 판정 기준이 "누구에게 구독했는가"라서 씬 재로드 시 죽은 스택을 비우는 일도 여기서 일어나는데, 등록 경로에만 걸면 정리가 "다음 조작"으로 밀려 그 사이 버튼이 거짓 활성으로 보인다. 그래서 `CanUndo`는 **조회에 부작용이 있는 프로퍼티**다(멱등·비용 무시 가능).
  ⚠ **LIFO는 편의가 아니라 정확성 요건** — 근거는 `TowerMerge.md` §9.3
- `TowerMergeCommand`(#263, 순수 C#, #281 확장) — 합성 재료의 소프트 소모. `Execute`=`TowerFootprint.Release()`+`SetActive(false)` / `Confirm`=상태 전이(재료는 살아 있다) / `Commit`=`Destroy`(밤) / `Undo`=결과 회수 + `Reoccupy()`+`SetActive(true)`.
  **확정/취소 판단을 자기 상태로 한다** — 배치 종료 통지가 어느 쪽인지 알려주지 않으므로, 이 방식이라야 `TowerPlacer`/`MouseManager` 무수정으로 "취소일 때만 원복"이 성립한다. `TowerMergeGroup`을 모른다(집합 정리는 비활성화→`ActiveChanged`→`Prune`이 담당).
  **`AdoptResult(TowerPlaceCommand)`**(#281)로 결과 배치를 편입해 합성 전체가 커맨드 하나로 되돌아간다(연출 소유권도 `PlaysUndoDissolve`로 함께). ⚠ `Undo` 순서가 계약: **연출 → 결과 회수 → 재료 복원 → `RestoreTo`**(`TowerMerge.md` §9.3)
- `TowerPlaceCommand`(#281, 순수 C#) — 배치의 되돌리기. `Execute`=**인수(adopt)만 한다(부작용 없음)** / `Confirm`·`Commit`=상태 전이 / `Undo`=`Release()`→`Destroy`→`Grant(실지불 비용)`.
  ⚠ `Destroy` **전에** `Release()`를 부른다(`Destroy`가 프레임 끝까지 지연돼, 안 부르면 합성 되돌리기의 `Reoccupy`가 타일을 포기한다). 배치를 커맨드로 옮기지 않은 근거는 `TowerPlaceCommand.cs` 헤더
- `enum PlacementOwner { Placer, Caller }`(#281, `TowerPlacer.cs`) — 이 배치의 커맨드를 누가 히스토리에 올리는가. 일반 배치와 합성 결과 배치가 **같은 `PlaceTower`를 공유**하므로 필요하다(합성=`Caller`, 등록은 `AdoptResult`가 흡수).
  `BeginTowerPlacement`의 **기본값 없는 필수 인자** — 암묵 판정을 쓰지 않는 근거는 `TowerPlacement.md` §7
- `void ManagementController.Grant(IReadOnlyList<ResourceCost>)`(#281) — `TrySpend`의 대칭짝(private `AggregateCost` 재사용). 되돌리기가 실지불 비용을 100% 환원하는 유일한 경로.
  ⚠ **팀 계약 #3·#6(WL-017)을 갱신하는 지점** — 인자는 커맨드가 든 실지불 비용이어야 하고 임의 수량 지급에 쓰지 않는다(`TowerPlacement.md` §8)
- `bool MouseManager.IsPlacing`(#281) — 배치 고스트를 들고 있는가. 되돌리기 버튼이 "먼저 이 고스트부터 치운다"를 판정한다
- `TowerFootprint.Release()` / `Reoccupy()`(#263) — 등록 목록은 유지한 채 `BattleTile.Occupied`만 임시 해제/복원. 규칙 셋: **①`OnDestroy`는 `Release` 이후 타일을 건드리지 않는다**(합성이 재료 자리에 결과를 놓으면 그 타일은 이미 결과의 것 — 무조건 해제하면 결과가 선 칸이 빈 칸으로 표시돼 그 위에 또 배치된다). **②`Reoccupy`는 이미 점유된 타일을 되찾지 않고 목록에서 뺀다**(되찾으면 소유권까지 가져와 ①의 증상이 되살아난다). **③`OnEnable`이 `Release` 상태면 스스로 `Reoccupy`**(커맨드를 거치지 않는 활성화 경로 대비 안전망, 정상 경로에선 no-op)
- `TowerMergeGroup`(#183, 순수 C#) — 선택 재료 집합. `IReadOnlyList<Tower> Towers`/`Add`/`Remove`/`Clear`/`SetSingle(tower)`(원자 단일화, 통지 1회)/`Prune(Predicate<Tower>)`(주입 판정으로 죽은 항목 제거) + `event Action OnChanged`(변경 시 발행, 코디네이터가 구독해 UI·하이라이트 갱신). **`TowerMergeCoordinator`가 소유**(씬 오브젝트 아님) — 구 임시 `TowerWallet`(#195) 대체·폐기
- `TowerMergeCoordinator`(#183, MonoBehaviour) — 합성 선택 두뇌·실행 오케스트레이터. `MouseManager.OnPrimarySelect`(평클릭/빈곳 → 그룹 리셋·해제)/`OnGroupSelectToggled`(Shift 토글)·`DayNightManager.OnDayToNight`(그룹만 리셋)·`Tower.ActiveChanged`(→ `Prune(t=>t==null||!Tower.Active.Contains(t))` stale 정리)·`TowerMergeGroup.OnChanged` 구독(낮 게이팅·하이라이트·우측 패널 스왑 1개=`TowerInfoUI`/2개↑=합성 패널). 마커→타워 해석은 `grp is TowerGroupSelectable`로 코디네이터가 흡수. 파사드: `SelectedTowers`/`event OnGroupChanged`/`CanMerge(recipe)`/`RequestMerge(recipe)`. `OnDestroy`에서 구독 해제(F7). **진행 중 배치 취소(밤)는 여기가 아니라 `PhasePanelSwitcher.ShowNight`가 담당**(페이즈 취소 책임 일원화)
- `IGroupSelectable { OnGroupSelected(); OnGroupDeselected() }`(#183, **도메인 완전 중립 — Tower 미참조**) + `TowerGroupSelectable`(타워 구현, `TowerPlacer.PlaceTower`가 `TowerFootprint`와 같은 지점에서 런타임 `AddComponent`) — MouseManager가 마커 유무로만 그룹 선택 자격 판정(타워 무지 → 제네릭). 마커→타워 해석은 소비처(`TowerMergeCoordinator`)가 `grp is TowerGroupSelectable` 캐스팅으로. 그룹 하이라이트 훅은 단일선택 `ISelectable`과 분리
- `ResidentSelectable`(#277, 주민 구현 마커) — `IGroupSelectable`+`IHoverable`+`ISelectable`+`IOutlineKindFilter`를 **한 컴포넌트에** 구현한다. ⚠ **쪼개면 안 된다**: `MouseManager`는 `hit.collider.TryGetComponent<T>`로 대상을 찾아 **GameObject당 구현 하나만** 잡고 부모 탐색도 하지 않는다 — 나중에 주민 툴팁을 별도 컴포넌트로 빼면 툴팁이나 아웃라인 중 하나가 조용히 죽는다(`TowerGroupSelectable`에 같은 경고가 있다). 프리팹이 `Assets/Imported`의 별도 저장소에 있고 스포너 생성 주민에도 먹어야 해서 **런타임 부착**이다(`TowerGroupSelectable`과 같은 계보)
- `ResidentSelectionCoordinator`(#277, MonoBehaviour) — 주민 선택 두뇌. `MouseManager.OnPrimarySelect`(평클릭/빈곳)·`OnGroupSelectToggled`·`OnBoxSelectBegin/Update/End`·`DayNightManager.OnDayToNight` 구독. 파사드: `Selected`/`SelectionCap`/`Clear()`. **상한 = `MaxVillagers − AssignedTotal`(유휴 주민 수)** 이고 드래그 결과를 **선택 순서대로** 잘라낸다. ⚠ **상한은 집합만 막고 초록 표시는 못 막는다** — 단일 클릭의 초록은 코디네이터가 아니라 `OutlineInteractionDriver`가 `OnSelectionChanged`로 직접 켜므로, 표시 차단은 `IOutlineKindFilter`(위 `ResidentSelectable`)가 담당하는 **별개 경로**다(WL-158). 같은 이유로 **밤 정리도 두 갈래**다 — `Clear()`(자기 그룹 집합) + `_lastSingle != null`일 때만 `MouseManager.ClearSelection()`(전역 단일 선택). 주민은 밤에 비활성되고 `OutlineHighlight`는 플래그를 유지하므로 후자를 빠뜨리면 아침에 유령 초록이 뜬다. ⚠ **무조건 부르면 타워 선택까지 풀린다** — 타워 단일 선택은 밤에도 유지되는 것이 설계다(WL-145 세 번째 사례: 이 "밤 정리"는 공통 추출 시 소비처별 정책 훅으로 남겨야 한다). `OnSelectionChanged`(중복 제거됨)가 아니라 `OnPrimarySelect`를 쓰는 이유: 같은 대상을 다시 클릭하면 전자는 발행되지 않아 "빈 곳 클릭으로 해제"가 죽는다
- `ResidentDragCoordinator`(MonoBehaviour, 위와 같은 계보로 자가 부팅) — 주민을 들어 생산 건물에 떨어뜨려 배치한다(Resident.md §8 · §11.15). `MouseManager.OnUnitDragBegin/End`·`DayNightManager.OnDayToNight` 구독. 파사드: `CarriedCount`. **셋으로 갈린 책임 중 「번역」**: 누구를 들지 고르고(누른 주민이 선택 집합 안이면 집합 전체, 아니면 1명), 드롭 대상을 `BuildingInfo.Asset` → `LineIndexOf`로 라인에 매핑하고, `AssignVillager`를 부른다. 감추기·되돌리기는 `ResidentSpawner`가, 제스처는 `MouseManager`가 갖는다.
  ⚠ **밤·인원 상한을 여기서 다시 판정하지 않는다** — `AssignVillager`가 이미 보고 `bool`을 준다(같은 조건이 두 곳에 있으면 조용히 어긋난다). **성공을 확인한 뒤에 소멸시킨다**: 들 때의 감춤은 소멸이 아니라 보관이고, 실패는 전부(바닥 · 생산 건물 아님 · 밤 · 상한 · 다중 드롭의 남는 인원) **들었던 자리로 되돌리기**로 모인다.
  **연출은 아직 없다** — 들린 주민이 커서를 따라오지 않고 그 자리에서 사라진다(R10·R11·착지·거절 피드백 미착수, §8.3·§8.5 결론 대기). 판정과 회계는 그때도 바뀌지 않는다.
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
- `DayNightTransition` (#101, 씬 컴포넌트 — 싱글톤 아님, 인스펙터 참조로 배선) — 전환 연출 구동부.
  `UniTask PlayAsync(float target)`(0=낮/1=밤, 진행 중이면 이전 전환을 취소하고 목표를 확정한 뒤 새로 시작),
  `bool IsTransitioning`, `event OnTransitionComplete`. **전환 중 게이팅의 단일 조회 지점**이 될 자리다 —
  현재 소비처 0(§4 계약 5의 ⚠️ 참조)
- `DayNightLightingController.ApplyBlend(float t)` / `StreetLampController.SetBlend(float t)` — 낮(0)과 밤(1)
  사이 임의 지점을 적용하는 **적용부 진입점**. 전환이 매 프레임 호출한다. 두 컴포넌트의
  `subscribeToPhaseEvents`(인스펙터)는 정본 씬에서 **꺼져 있다** — 켜면 이벤트에 직접 반응해 스냅으로 찍혀
  연출과 이중 적용된다
- ~~`TerritoryController` / `TerritoryGraph` / `TerritoryDefinition` / `ManagementController.SupplyDaily`~~ — **전부 삭제됨(#337)**.
  경영 공간 영토 확장 시스템과 그것이 해금하던 특수 자원 4종(금/루비/사파이어/다이아)이 함께 제거됐다.
  `ProductionModifiers`는 잔존하나 등록처가 없어 항상 ×1(기본 라인 정산·예상치 호출부 무변경).
- `ManagementController.ManaPerWaveClear`(int) — 마나 row "+n" 미리보기용(웨이브 클리어 고정 마나)
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
- `TowerDissolveEffect.Play(IReadOnlyList<Transform> targets, float tileSize [, DissolveMode mode])`(`NorthLand.Combat`, #265, **#281에서 `TowerMergeDissolveEffect`에서 개명**) — 타워가 가루가 되는 연출 시작.
  **대상이 사라지기 직전에** 불러야 한다 — `SetActive(false)`나 `Destroy`가 걸리고 나면 복제할 시각물이 남지 않는다.
  시각 사본을 뜨는 `Build`가 이 호출 안에서 **동기로** 끝나므로 호출부는 같은 프레임에 대상을 파괴해도 안전하다(되돌리기 커맨드가 그 사실에 기댄다). 항상 유효한 인스턴스를 반환.
  길이 인자는 **타일 한 칸**이다(풋프린트 아님) — 이 연출의 모든 길이가 "저 칸 것"을 말하는 단위다
- `enum DissolveMode { Merge, Disperse, Rewind }`(#281) — 가루가 된 뒤 무엇을 하는가. **화이트아웃→수축→가루 폭발은 전 모드 공유**이고 그 뒤만 갈린다.
  `Merge`=부유하며 바깥의 마무리 통지를 기다린다(#265 그대로) / `Disperse`=사방 발산 소멸(배치 되돌리기) / `Rewind`=가루가 재료 자리로 갈라져 이동 + 재료 팝 복원(합성 되돌리기, 확정 수렴의 역방향).
  ⚠ **모드는 마무리 통지(`Ending`)와 축이 다르다** — 통지가 오면 소멸 구간이 폭발을 건너뛰므로, `Merge` 외 모드에서는 `RequestEnd`가 통지를 거부한다
- `TowerDissolveEffect.ConvergeTo(Transform placed)` / `.Reassemble()` / `.Abort()`(#265, **`Merge` 모드 전용**) — 부유 루프의 마무리 3종(선착순, 먼저 정해진 쪽이 이긴다).
  ⚠ `ConvergeTo`는 **`TowerPlacer` 확정 콜백에서**(등장 연출이 스케일을 0으로 만들기 전에) 불러야 목적지가 정상값이다.
  ⚠ `Reassemble`은 **커맨드 `Undo` 직후 같은 프레임에** 불러야 되살아난 재료가 한 프레임 번쩍이지 않는다.
  셋 다 **폭발이 끝나기 전에도 도착할 수 있다**(클릭 직후 취소 등) — 그 경로는 소멸 구간을 즉시 완료 상태로 스냅한 뒤 마무리로 넘어간다
- `TowerDissolveEffect.RestoreTo(IReadOnlyList<Transform> materials)`(#281, **`Rewind` 모드 전용**) — 가루가 돌아갈 자리를 등록하고 그 대상을 같은 프레임에 스케일 0으로 잡는다(`Reassemble`의 Rewind판 대응물, 같은 자리·같은 이유).
  ⚠ bounds는 **스케일 0으로 잡기 전에** 잰다 — 뒤에 재면 한 점으로 붕괴해 목적지가 전부 같은 좌표가 된다
- `GrainSwarm`(`NorthLand.Combat`, #265) — 두 연출이 공유하는 흰 알갱이 렌더링 부품(빌보드 쿼드·절차 텍스처·개수/크기 규칙·전체 알파).
  **움직임을 모른다** — 좌표는 전부 호출자가 정한다. `ResolveGrainSize`는 bounds도 풋프린트도 아닌 **타일 한 칸**을 받는다 —
  그리드가 정하는 값이라 에셋 교체와 무관하고, 칸 수에도 흔들리지 않아 **모든 타워의 알갱이가 같은 크기**로 보인다
- `VfxScaleHold.Acquire(Transform target) : Handle`(`NorthLand.Combat`, #265) — 대상 `localScale`의 배타 점유권 발급(0 → back-out 팝 → 원복).
  대상에 붙는 컴포넌트라 **점유 상태가 대상과 함께 죽는다**(구 static 딕셔너리의 죽은 키 누수 문제가 사라졌다).
  이미 점유 중이면 그 자리에서 원본을 복원하고 인계하며, `Handle.IsSuperseded`로 인계당한 연출이 남은 구간을 스스로 접는다
- ⚠ `RangeCircle`의 **부모 스케일 역보정 시점이 바뀌었다**(#264): 생성 1회 캡처 → `Show`/`SetRadius`/`LateUpdate`마다 재계산.
  부모 스케일이 런타임에 변하는 대상(등장 연출이 타워 루트를 0→1로 애니메이션)에서 보정이 잘못된 값으로 굳던 경로를 막는다.
  **부모 스케일이 고정된 기존 소비처는 동작이 동일하다**

**Resident (경영 앰비언트 군중, #276)** — 전부 전역 네임스페이스(BT 노드 규약이 네임스페이스를 두지 않아 세트를 맞췄다, WL-152)

- `ResidentRegistry`(static) — `Residents` / `Register` / `Unregister` / `TryFindNearestCandidate(self, radius, out)` / `CountNearby(self, radius)`. 대화 **합류** 후보는 반대 필터가 필요해 `TryFindNearestJoinable(self, radius, maxParticipants, out ResidentConversation)`이 따로 있다 — `IsAvailableForConversation`은 "혼자인 사람"을 찾으므로 이미 대화 중인 사람을 찾는 데 쓸 수 없다.
  **물리 질의 대신 쓰는 근접 탐색의 유일한 창구**다. 레이어·태그를 점유하지 않는 것이 이 선택의 요점이라, 소비처가 `Physics.Overlap*`으로 되돌아가면 그 이점이 사라진다
- `ResidentWaypointRegistry`(static) — `Waypoints` / `TryGetRandomWaypoint(out)`. 목적지의 출처
- `ResidentDoorPointRegistry`(static) — `Points` / `TryGetNearest(from, out)` / `CollectUsable(buffer)`.
  `CollectUsable`이 **호출부 버퍼를 받는 이유**: 스포너가 이 목록을 섞어 소비하므로 내부 재사용 버퍼를 내주면 다음 질의가 그 순서에 오염된다
- `ResidentNoStopZoneRegistry`(static, #332) — `Zones` / `Register` / `Unregister` / **`Contains(worldPoint)`**. 주민이 **멈춰 서면 안 되는 구역**(다리·좁은 골목)의 유일한 출처. 소비처는 `ResidentTryStartConversationAction`(신규 성립·합류)과 `ResidentDanceAction`.
  `ResidentNoStopZone`은 **콜라이더가 아니라 로컬 OBB**다(`Center`/`Size`/`IsUsable`/`Contains`) — 트리거 콜라이더는 `Physics.queriesHitTriggers` 기본값 때문에 레이캐스트에 잡혀 `MouseManager`의 선택·배치 마스크와 스킬 타게팅에서 새 레이어를 빠짐없이 빼야 한다. 씬 뷰 면 핸들은 `ResidentNoStopZoneEditor`의 `BoxBoundsHandle`이 준다(에디터 어셈블리).
  ⚠ **이 존만 Y를 센다.** 다른 근접 질의는 전부 높이를 무시하지만(계단·언덕), 다리는 지면 위로 떠 있어 Y를 무시하면 다리 밑 통로까지 함께 막힌다
  ⚠ **존 거절은 `Encounters.Mark`보다 앞에서 반환해야 한다** — 조우 쿨다운은 확률 판정 실패의 기록이고, 여기에 섞으면 존을 벗어난 직후에도 그 상대와 대화가 성립하지 않는다
  ⚠ **편집 모드에서는 레지스트리가 비어 있다**(`[ExecuteAlways]` 없음, `ResidentWaypoint`와 동일). 에디터 검증은 `ResidentNoStopZone.Contains`를 직접 부를 것
  ⚠ **저작물(존 6개)은 본 저장소에 없다** — `CandyLand/ResidentNoStopZones/` 아래, 즉 `Assets/Imported`의 별도 저장소 소유다(WL-160). 스크립트만 보고 "존이 배치돼 있는가"를 이 저장소 diff로 판정할 수 없다
  ⚠ **웨이포인트가 존 안에 있는 것은 버그 신호가 아니다** — 존 안에 목적지를 두면 "도착해서 자리만 잡고 아무것도 안 하는 조용한 자리"가 된다. `WP (15)`(차단 100%, 의도)와 `WP (24)`(8%)가 같은 언덕에서 대비 쌍을 이룬다. 판단 기준은 위치가 아니라 **의도**이므로, 존을 새로 그린 뒤에는 차단률을 다시 뽑아 대조할 것(`Resident.md` §11.13)
- `Resident` — `Conversation` / `Sociability` / `Encounters` / `Agent` / `IsDancing` / `IsEmerging` / `HasArrivedHome` / **`IsBusy`** / `IsAvailableForConversation`.
  **`IsBusy` 한 줄이 "무언가 하는 중인 주민에게 말을 걸 수 없다"를 보장한다** — 행위가 늘면 여기에만 더한다.
  조건을 소비처마다 나열하면 행위가 늘 때마다 모든 소비처를 고쳐야 한다(공연·앉기·들려 있음이 전부 여기로 들어올 예정)
- `ResidentAgent` — BT 리프 노드가 참조하는 **유일한** 컴포넌트(`EnemyAgent`와 같은 규약). `SetStationaryHold(bool)` — 대화 중 서 있는 단계에서 자기 `obstacleAvoidanceType`을 끈다. **`isStopped`만으로는 정지한 `NavMeshAgent`도 지역 회피 해에 밀려난다**(걸어가던 주민이 대화 중인 무리를 밀어내던 원인). `EnsureOnNavMesh()` — 오프메시로 밀려난 주민을 `Warp`로 끌어올린다(**부작용 있는 복구 함수**). 이동 노드가 `TrySetDestination` 직전에 부른다 — 오프메시면 목적지 지정이 조용히 무시돼 주민이 그 자리에서 영구히 굳는다. 상태를 **보기만** 할 때는 부작용 없는 `IsOnNavMesh`를 쓴다(이름이 비슷해 바꿔 쓰기 쉬운데, 그러면 "안 풀리는 주민" 또는 "의도치 않은 순간이동"이 조용히 섞인다).
  이동(`TrySetDestination`/`PauseMovement`/`StopMoving`/`HasArrived`/`SpeedFactor`) · 회전(`FaceTowards`/`IsFacing`/`ReleaseRotation`) · 애니메이션(`PlayState`/`ReturnToLocomotion`/`IsInState`/`AnimationNormalizedTime`/`CurrentClipName`).
  ⚠ **`SpeedFactor`는 배수다** — `Awake`에 캡처한 프리팹 기준 속도에 곱하므로 노드가 절대값을 쓰면 프리팹 조정이 그 노드만 안 따라온다. 켠 노드가 `OnEnd`에서 1로 되돌린다.
  ⚠ **회전 소유권은 평소 `NavMeshAgent`에 있다** — `FaceTowards`가 뺏고 `ReleaseRotation`이 반납한다. 반납을 빠뜨리면 그 주민은 이후 영원히 옆걸음으로 걷는다
- `ResidentConversation` — 대화 세션. **참가자가 소유하지 않는다.** 티커가 없고 진행은 참가자의 행동 종료(`Join`/`MarkGreeted`/`MarkApproached`/`AdvanceTurn`/`MarkFarewelled`)로만 넘어간다. **N인(최대 3) 세션이다**(#302) — 참가자가 `List<Slot>`이고 자리는 원주 배치(`ResolveStandPoints`, `R = distance / (2·sin(π/N))`, N=2면 기존 중점 대칭과 동일 결과)다. 진행 중 합류: `TryJoin(newcomer, max)` / `CanAccept(max)` / `MarkEncounterWithAll(outsider, seconds)`(합류 실패 쿨다운은 **전 참가자에게** 걸어야 실효가 있다 — 후보 판정이 구성원마다 돌기 때문), 시선은 `TryGetFocusPoint(self, out)`(화자를 본다. 합류 인사 동안은 `RecentJoiner`). 자리를 잡을 때 **원 중심이 지나가던 비참가자를 덮지 않도록** `PushCenterOffOutsiders`가 중심을 비킨다(합류 재배치가 통행인을 에워싸는 것을 막는다. 이동 상한 `MaxCenterShift = 1.2`). 합류하면 인사부터 다시 하고 턴이 초기화된다(상한 3이라 세션당 1회).
  ⚠ `HasLostParticipant`는 `Phase == Ended`를 먼저 거른다 — 안 그러면 먼저 정리한 쪽 때문에 **모든 대화가 R7 놀람으로 끝난다**
- ~~`ResidentConversationObstacle`(#302)~~ — **삭제됨.** 좁은 골목에서 지나가던 주민을 영구히 가두는 것이 실측돼 걷어냈다(위 Resident 행 참조). 다시 세우려면 **경로 계획이 아는 장애물**(`carving = true`)이어야 하고, 그 경우 참가자가 침식된 구멍에 빠져 오프메시가 되는 문제를 함께 풀어야 한다.
  ⚠ **`NavMeshObstacle.height`는 반높이 게터다** — `height = 2`로 넣으면 `size.y = 2`가 되고 읽을 때 1이 나온다(버그가 아니다)
- `ResidentVoice`(주민 프리팹 루트 컴포넌트) / `ResidentVoiceAudibility`(static) — 대화 목소리(Resident.md §11.16). **BT가 아니라 애니메이터 상태(`Talking_1~3`·`Laughing`)를 따라간다** — `BossPatternVfx`와 같은 규약이라 `ResidentConverseAction`을 고치지 않고, 화자/청자 구분 코드도 필요 없다(말하는 쪽만 `Talking_*`에, 웃는 쪽만 `Laughing`에 들어간다).
  ⚠ **상태 이름이 `ResidentBehaviorGraphBuilder`와 어긋나면 조용히 소리만 안 난다** — 컨트롤러에 없는 상태는 영영 매칭되지 않는다.
  `ResidentVoiceAudibility`는 카메라 조회·줌 판정을 **프레임당 1회만** 계산해 캐시한다(`CameraVisibility`와 같은 자리·같은 이유). 단 카메라를 못 찾으면 **무음으로 답한다** — `CameraVisibility`가 반대로(보인다고) 답하는 것과 다른 선택이고, "화면 밖인데 들린다"가 곧바로 버그로 들리기 때문이다.
  ⚠ **프리팹 배선과 클립 4본이 `Assets/Imported`(별도 저장소)에 있다**(WL-160과 같은 축) — 본 저장소만 들어가면 스크립트는 있는데 어떤 프리팹에도 안 붙어 있어 **에러 없이 소리만 안 난다.**
- `ResidentSpawner` — 인원(`crowdSize − AssignedTotal`)과 밤낮 출입의 소유자.
  ⚠ **BT 노드는 자기 GameObject를 끄지 않는다** — `Resident.MarkArrivedHome()`으로 표시만 남기고 스포너가 `LateUpdate`에서 거둔다(`BehaviorGraphAgent.Update` 스택 위에서 자기를 끄는 사고 회피).
  ⚠ **비활성화는 그래프를 끝내지 않는다** — 재사용 시 `BehaviorGraphAgent.Restart()`가 필요하다(안 하면 어젯밤 노드가 이어진다). 되살리는 **모든** 경로가 `RestartGraph`를 거친다(아침 등장 · 배치 −1 퇴장 · 드래그 복귀)
  - `bool TryCarry(Resident)` / `ReleaseCarried(Resident)` / `ConsumeCarried(Resident)` — 드래그로 들기(Resident.md §8 · §11.15). 통지 구독이 아니라 `ResidentDragCoordinator`가 **직접 부르는** 유일한 경로다(특정 개체를 지목하는 조작이라 상태 통지로 표현되지 않는다).
    ⚠ **`ConsumeCarried`는 `despawnEffectPrefab`("뿅")을 재생하지 않는다 — 그 파티클은 패널 +1 전용이다.** 설명하는 바가 다르다: 패널 +1은 *플레이어가 손댄 적 없는 자리에서 주민이 갑자기 사라지는 것*이고, 드롭은 플레이어가 직접 집어 넣은 것이다. 드래그에는 별도 연출이 들어올 예정이라 그때까지 비워 둔다(있는 것을 임시로 돌려쓰면 나중에 어느 쪽 연출인지 구분이 안 된다).
    ⚠ **감추기를 스포너가 갖는 이유는 풀의 불변식이다** — `TakeFromPool`이 "비활성 + 아침 대기열에 없음"을 재사용 후보로 보므로, 밖에서 그냥 `SetActive(false)`하면 **드래그 중 패널 −1에 손에 든 주민이 건물에서 걸어 나온다**.
    인원 산술은 무변경으로 맞는다 — 들면 `ActiveCount`가 N 줄고 배치 1건마다 `TargetCount`가 1 줄어 `TrimCrowd`의 초과분이 0 이하로 유지된다(배치 통지가 엉뚱한 주민을 대신 거둬 가지 않는다).

**Camera / ManagementVfx (경영 연출, #138)** — 전부 전역 네임스페이스(경영 공간 세트를 맞췄다, WL-029)

- `CameraController2.OnZoomChanged : Action<float>` — **페이로드는 변화량이 아니라 변경 후 오쏘 사이즈**다.
  변화량만 실으면 소비처가 누적 상태를 따로 들어야 하고 **게임 도중 생성된 오브젝트가 초기값을 받을 방법이 없다**.
  그래서 계약은 "붙을 때 `CurrentZoomSize`로 pull, 바뀌면 이벤트로 push"다(`ManagementController.OnChanged`와 같은 계보).
  값이 실제로 바뀔 때만 발행한다 — 최대/최소에 붙은 채 휠을 굴리면 클램프에 걸려 값이 그대로다.
  ⚠ **발행처가 `ZoomMouseWheel`과 `UpdateTargetZoom`(#390) 둘이다.** 세이브 복원 등 `Lens`를 직접 쓰는 경로를 더 추가하면
  그쪽에서도 발행해야 한다 — 안 하면 힌트가 조용히 옛 상태에 머문다(WL-024의 `ApplyZoom` seam이 이걸 구조로 막는다).
  ⚠ **`UpdateTargetZoom`은 값 변화 여부를 보지 않고 진행 중 매 프레임 발행한다** — 휠 쪽의 "바뀔 때만" 가드가 여기엔 없다(0.2초 남짓이라 방치).
- `CameraController2.CurrentZoomSize` / `MinZoomSize` / `MaxZoomSize` — 현재 값·범위 pull 창구
- `CameraController2.MoveTo(Vector3)` / `MoveViewCenterTo(Vector3, float groundY)` — 지정 지점으로 SmoothDamp 이동(`minimapMoveSmoothTime`, `unscaledDeltaTime`, `ClampPosition` 적용).
  전자는 **카메라 타겟이 설 자리**를 그대로 받고, 후자는 그 지점이 **화면 정중앙**에 오도록 오프셋을 보정한다.
  ⚠ **건물을 겨냥할 때 `MoveViewCenterTo(건물.position, 0)`은 맞지 않는다** — 건물 피벗이 서 있는 자리와 다르기 때문(위 `IDragHandle` 항의 같은 함정).
  바로가기 패널(#390)이 `MoveTo` + `BuildingFocusPoint` 오프셋을 쓰는 이유다.
  ⚠ WASD·우드래그가 들어오면 `CancelMinimapMove`로 **즉시 취소**된다(플레이어 조작 우선). 같은 규칙으로 휠은 `ZoomTo`를 끊는다 — 이동·줌 어느 쪽이든 **수동 입력이 자동 모션을 이긴다**
- `CameraController2.ZoomTo(float)`(#390) — 목표 오쏘 사이즈로 SmoothDamp 줌(`zoomSmoothTime`, `min/maxZoomSize`로 clamp)
- `CameraVisibility.IsVisible(Vector3 center, float radius) : bool`(static) — 프러스텀 가시성 질의.
  평면은 **프레임당 1회만** 계산해 캐시한다(질의 주체가 여럿이라 각자 계산하면 같은 값을 개수만큼 만든다).
  ⚠ **카메라를 못 찾으면 `true`를 반환한다** — "안 보인다"로 답하면 연출이 조용히 사라져 원인 추적이 어려워지기 때문
- `ZoomDrivenVisibility`(abstract MonoBehaviour) — 줌 구간에 반응하는 표시물의 공통 뼈대.
  파생은 `protected abstract void ApplyVisible(bool)` **하나만** 구현하고, 구독/해제·붙을 때 1회 pull·멱등·비활성화 시 내려놓기는 베이스가 담당한다.
  `protected bool IsVisible`은 파생이 다른 조건(예: 낮/밤)과 합성할 때 읽으라고 열어 둔 것이다.
  **공용 `enum ZoomLevel`(Near/Mid/Far)로 묶지 않은 이유**: 줌에 반응할 표시물(건물 힌트·머리 위 아이콘·주민 이모티콘)은
  켜지는 구간이 서로 달라, 경계를 공유하면 하나를 조정할 때마다 나머지가 끌려온다. **머리 위 아이콘·주민 이모티콘이 파생할 자리다**
  (연속값이 필요한 소비처 — 볼륨 페이드 등 — 는 이 뼈대가 아니라 `OnZoomChanged`를 직접 구독한다)
- `FeedbackParticle.PlayFromStart(ParticleSystem, Object ctx)` / `StopGracefully(ParticleSystem)`(static) — 파티클 재생 규약의 단일 창구.
  `PlayFromStart`는 `Stop(StopEmittingAndClear)` 후 `Play(true)`(잔상 겹침 방지), `StopGracefully`는 방출만 멈춰 이미 뜬 입자는 수명대로 사라지게 둔다.
  ⚠ **비활성 오브젝트에 `ParticleSystem.Play`는 예외 없이 조용히 실패한다** — 그 상태를 경고 로그로 잡아 주는 것이 이 헬퍼의 존재 이유 중 하나다
- `BalloonFlight.Launch(spawn, start, end, riseSpeed, cruiseSpeed, onReachedStart, onFinished)` — 풀에서 꺼낸 인스턴스도 이 진입점 하나로 상태가 완전히 초기화된다.
  ⚠ **풀 재사용 시 `Awake`가 다시 돌지 않아 Play On Awake가 발화하지 않는다** → `Launch`가 파티클을 명시적으로 재시작한다

## 3. 접점 매트릭스 (왼쪽 시스템을 건드리는 PR은 오른쪽 항목을 실제 코드로 대조)

| 접점                                     | 확인할 것                                                                                                                                                                                                                  |
| ---------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Combat ↔ BattleMapBuilder                | 몬스터 이동 경로(순서 있는 경로 API 부재 — WL-003), 레이어(WL-005), 좌표계(battlespace 로컬 vs 월드 — WL-007)                                                                                                              |
| Combat ↔ DataTable                       | 스탯 데이터 원본(CSV 파이프라인 vs 손입력 SO — WL-001)                                                                                                                                                                     |
| MouseManager ↔ BattleMapBuilder          | 그리드 스냅, CanPlaceAt·타일 종류 질의 API(WL-004), 좌표계(WL-007)                                                                                                                                                         |
| TowerVfx ↔ MouseManager / TowerFusion    | **스케일 배타 소유 계약**(`TowerPlacement.md` §9.3.2, 구현은 `VfxScaleHold`). 소유자가 둘이 됐다: 등장 연출은 결과 타워를, 합성 재조립은 되살아난 재료를 잡는다. 서로 다른 대상이라 정상 경로에선 겹치지 않지만, 겹치면 점유 세대로 인계된다. **#265는 결과 타워에 `PlayAsync`를 걸지 않는다** — 자기 입자를 같은 시간 축으로 나란히 날려 보낼 뿐이라 이중 재생 문제가 애초에 생기지 않는다(#264 시점의 우려는 이 설계로 해소). 연동 규칙 넷: ⓐ `TowerDissolveEffect.Play`는 대상이 사라지기 **직전**(합성은 커맨드 `Execute` 직전, 되돌리기는 `Destroy` 직전), ⓑ `ConvergeTo`는 `TowerPlacer` 확정 콜백에서 등장 연출보다 **앞**, ⓒ `Reassemble`은 커맨드 `Undo` **직후 같은 프레임**, ⓓ `RestoreTo`(#281)는 재료를 되살린 **직후 같은 프레임**이며 bounds 측정이 스케일 0 잡기보다 **앞**. 넷 다 어기면 조용히 어긋난다(복제할 시각물 없음 / 쪼그라든 bounds / 한 프레임 번쩍임 / 목적지가 한 점으로 붕괴). 드래그 선택은 콜라이더가 아니라 위치 기반이라 **연출 중(스케일 0)에도 대상에 도달한다** |
| MouseManager ↔ Combat                    | 타워 배치(PlacementRequest→Tower 프리팹), 선택(ISelectable), TowerInfoUI 데이터 연동(WL-011). **#164 리팩토링 후**: 배치 확정 시 `TowerPlacer`가 `Tower.Build(so)`를 호출해 인스턴스를 산 SO로 조립한다(프리팹↔SO 불일치 해소). 또 모든 타워가 단일 `Tower` 타입이 되어 마법 타워도 `sel is Tower`·`TowerGroupSelectable.Tower`에 정상적으로 걸린다 — 구 WL-131(마법 타워가 합성·그룹 선택에서 조용히 제외되고, 평클릭 시 담아둔 합성 그룹이 해제되던 부작용)이 타입 통합만으로 소멸 |
| DataTable ↔ Localization                 | 표시 문자열 소유권: Building/Enemy/Resource/Tower CSV 표시 문자열을 String Table 키로 이관 완료(WL-013 해소, PR#126). 잔여: 전투 공간(TowerInfoUI) 키 조회 배선 후속(#102). 테이블명 상수 대소문자 정합(WL-060)                                                                                                                                                          |
| Management(Resource) ↔ DataTable         | `ResourceKind`(지갑 키)·`BuildingAsset.ProductionFields`(생산처 입력)·`ResourceAsset.Data`(정산 시 `Kind` 해석, 호출부 `Start()` 채움 규약) 의존 — muchan이 이 구조 바꾸면 자원 시스템 깨짐                                |
| Management(Resource) ↔ DayNightManager   | 정산+주민 초기화=`OnNightToDay` 구독(정산 먼저), 낮→밤 전환=`ManagementController.RequestAdvancePhase()`가 `EndDay()` 호출. **밤→낮(`EndNight`)은 이제 `NightActionPanelView`의 "웨이브 성공" 버튼이 임시 트리거 — 밤 종료 주체(Combat 웨이브 클리어 등)로 책임 이관 필요(WL-018)**. 주민 수는 여전히 placeholder(주민 시스템 부재)                |
| BattleMapBuilder/Monster ↔ DayNightManager | 밤 시작(`OnDayToNight`)에 `StageBuilder`가 구독 → 다음 스테이지 생성(전투영역 확장) + `MonsterSpawn.StartRound`로 몬스터 스폰(`currentMapCount > 1`, #17). `MonsterSpawn`은 낮이면 스폰 스킵(경고 로그). 웨이브 클리어(스폰 완료 후 생존 0) 시 `MonsterSpawn`이 `EndNight()` 호출로 낮 복귀(#17) — 단 본진 도달-디스폰 기준(처치 기반은 Enemy 병합 후 WL-038); 실패/보스 판정·임시 버튼 제거는 WL-018 잔여. **`WaveCleared(int)`가 통보하는 웨이브 번호의 정본은 `DayNightManager.CurrentWave`다(#350)** — `MonsterSpawn.currentRound`는 `StartRound`가 검증을 전부 통과한 뒤에만 대입되므로 스폰 시작 전에는 `0`이거나 직전 웨이브 값이고, 그대로 넘기면 `MonsterSpawnWaveProvider.TryGetRewardPool`의 1-base 가드에 걸려 보상 3택1과 `IsFinalWave` 승리 판정이 함께 생략된다. `ForceClearWave`는 이 정본을 쓰며, 맵 공개 대기 중이던 `CombatMapMonsterConnector`의 지연 `StartRound`가 뒤늦게 도착하는 것을 `suppressedRound`로 1회 삼킨다(취소 통로가 없어 받는 쪽이 보정하는 구조 — 연결자 쪽 취소·재검증이 근본 해법) |
| Management(Resource) ↔ Resident(군중)    | **두 수는 별개다**(GDD §5.1): `MaxVillagers`는 **선택·배치 상한**(시작 2/최대 10, 본진 업그레이드로 성장), `ResidentSpawner.crowdSize`는 **고정 군중 풀**(20~30)이다. 군중을 상한에 맞추면 마을이 밋밋하고 드래그가 불편해 일부러 나눴다. **의존 방향은 한쪽뿐** — 스포너가 `AssignedTotal`을 읽어 `화면의 주민 수 = 군중 − 배치된 수`를 계산하고(§3.1), 경영은 주민 시스템을 전혀 모른다. 배치 수의 단일 진실 원천은 끝까지 `ManagementController`다. **✅ 두 번째 소비처 생김(#277)**: `ResidentSelectionCoordinator`가 `MaxVillagers − AssignedTotal`(= 유휴 주민 수)을 읽어 선택 상한으로 쓴다 — 의존 방향은 여전히 한쪽뿐이고(경영은 주민 선택을 모른다) 배치 수의 정본도 그대로 `ManagementController`다. **✅ 세 번째 소비처 + 양방향 반응(#341)**: 스포너가 **낮 도중에도** 인원을 맞춘다 — 패널 +1은 `OnChanged`(대상 없는 상태 통지)를 타고 화면의 주민 하나를 무작위로 거두고, 패널 −1은 `OnBuildingAction(VillagerUnassigned)`(**대상이 특정되는** 통지)를 타고 **그 건물** 자리에서 한 명을 내보낸다. 늘리는 쪽만 대상 통지를 쓰는 이유는 "어디서 나오는가"가 필요하기 때문이다(Resident.md §11.14). 의존 방향은 여전히 한쪽뿐이고 정본도 그대로 `ManagementController`다. ✅ **생산 건물 문 배치 완료** — `ResidentDoorPoint` 22 → 24개, 3종 모두 `CandyLand.prefab` 소유(WL-160)이고 콜라이더 안 + NavMesh 2유닛 안이라 폴백이 돌지 않는다. ⚠ **폴백 코드는 남아 있고 `BuildingInfo.transform.position`을 쓴다 — 그 피벗은 건물 자리가 아니다**(위 `OnUnitDragEnd` 경고). 문이 빠진 건물이 생기면 조용히 엉뚱한 자리에서 나오고, 건물당 1회 경고가 유일한 신호다 |
| Management(Resource) ↔ PlayerSkill(마법 연구소) | **✅ 착지점 확정·구현 완료(#205)**. 메커니즘: `SkillManager`가 `ManagementController.GetUpgradeLevel(magicLabAsset)`으로 **레벨(int)만** 읽고, 레벨→배율 매핑은 `magic_lab.asset`의 `Skill.UpgradeLevels`(SO, 도달 비용과 같은 리스트, 수치는 placeholder)에 authoring(컨트롤러는 "스킬"을 모름, `OnChanged` 통지→재-pull, 컨트롤러/UI 무수정). 비용·배율이 물리적으로 한 리스트라 레벨 개수가 어긋날 수 없다(PR#216 리뷰 — 최초엔 씬 인스펙터 리스트였다가 이관). **연구소 레벨 = 기본 스킬 스탯 배율**(damage/radius/cooldown — 같은 리스트의 버프용 배율 4종은 #315로 소비처 소멸) **+ 감전 착탄 이펙트 프리팹**(#206, 별도 SO `SkillVisualSet`이 매핑 소유 — 데이터 SO와 뷰 에셋 분리, `PlayerSkill.md` §3.2), **보상 특수효과(`SkillEffect.Level`/`SkillEffectManager.GetLevel`, #169) = 독립된 두 번째 축** — 두 축은 동시 스택되며 서로 충돌·이중 스케일링 없음(코드 확인 완료). GDD §5.5 편입 완료. **✅ 반대 방향 하나 추가(#398)**: 강화 미리보기를 %가 아니라 실수치("데미지 30 → 36")로 보여주려고 **`BuildingInfoUI`가 `SkillManager.BaseDamage`/`BaseRadius`/`BaseCooldown`을 읽는다** — #375까지는 "UI가 베이스 스탯을 모른다"가 전제였으나 절대값 표시가 그 전제를 깼다. 배율은 여전히 **클릭한 건물**에서 읽고(매니저가 문자열을 만들면 자기 `_magicLabAsset` 기준이라 두 번째 강화 건물에서 갈린다), 곱셈은 표시·실효가 `static SkillManager.Scale` 한 식을 공유한다. 상세: `BuildingUpgrade.md` §8, `PlayerSkill.md` §3.1~§3.2 |
| DataTable(Building) ↔ MouseManager       | `BuildingInfo`가 `ISelectable` 구현 + `BuildingAsset` 보유 — 선택 시 `BuildingInfoUI` 직접 호출(이벤트 미구독, WL-011과 동일 패턴). **부수 역할(#341)**: `OnEnable/OnDisable`에서 `BuildingInstanceRegistry`(`BuildingAsset → Transform`)에 자기 자리를 등록한다 — 경영 로직은 건물을 SO로만 아는데 주민 퇴장(§3.2)은 월드 좌표가 필요해서 생긴 유일한 다리다. 등록 주체를 새 마커가 아니라 여기로 둔 이유는 건물 루트에 이미 붙어 있는 유일한 컴포넌트라서(프리팹이 `Assets/Imported` 소유라 authoring 추가가 비싸다). ⚠ **SO를 키로 쓰는 것은 설계상 보장된 전제다** — 생산 건물은 **3종 고정**이고(GDD §5.7) 「건물 건설」은 *"새 건물을 지어 작업을 **해금**"*하는 것이라(GDD §5.2) **같은 건물을 여러 채 짓는 개념이 없다**(해금으로 건물 **종류**가 느는 것은 새 SO라 무관하다). 중복 경고는 authoring 실수를 잡는 그물이지 예상 경로가 아니다. **이 전제가 바뀌면 `LineIndexOf`와 함께 옮겨야 한다** — 한쪽만 옮기면 배치 −1 퇴장이 늘 첫 번째 건물에서만 나온다(WL-021). `BuildingTooltipSource`(#38)가 `IHoverable` 구현 + `BuildingAsset`/`BuildingData`/`BuildingType`을 **읽기 전용** 소비(muchan 구조 바뀌면 툴팁 깨짐 — 자체 `DataTableManager.Get` 조회, Data 채움 규약 의존). `MouseManager`가 씬에 없으면 조용히 무반응(WL-002) — 씬마다 배치·`_camera` 재할당 필요 |
| PlayerSkill ↔ MouseManager               | 스킬 버튼 클릭 → `BeginSkillTargeting(SkillTargetRequest)` → **전투 타일 위이면**(`CombatMapTileView` 존재) 확정, `OnConfirmed(Vector3)`로 `SkillManager.CastAt` 호출(#103). 인디케이터는 전투 타일 밖 숨김(유효/무효 색 없음). `PlacementRequest`와 별개 타입 — 그리드 개념 없음. **시전 y 결정은 요청자 소유**(#289): `SkillButtonView`가 `Snap`으로 커서 광선 ∩ 고정 높이(`_castHeight`) 수평면을 돌려주고 MouseManager가 그 값을 인디케이터·확정에 함께 쓴다(2절 참고) |
| MouseManager ↔ CombatSpace(맵)           | 스킬 타겟팅이 히트 타일의 `CombatMapTileView` 유무로 전투 타일 여부 판정(#103 후속, 도로 전용 제한 제거). MouseManager→CombatSpace 단방향 읽기(입력 매니저가 전투 공간 타일 데이터에 의존 — 지켜볼 커플링)                                            |
| PlayerSkill ↔ Combat                     | (감전) 새 데미지 파이프라인 없이 `IDamageable`/`DamageInfo`/`Faction`을 그대로 소비(`Tower.FindTarget()`/`Projectile.ApplyArea()`와 동일한 `Physics.Overlap*NonAlloc`+Faction 필터링 패턴 — **스킬 3종은 #398 이후 구가 아니라 수직 캡슐이며 `SkillHitScan` 한 곳을 통과한다**). `DamageInfo.Source`는 스킬 시전 시 `null`(IAttacker 개체가 아님). **#300 이후 이 null에 의미가 생겼다** —
`Enemy`가 마지막 피해 소스를 기록해 `Enemy.Killed`로 넘기므로, **스킬이 막타를 넣은 처치는 어느 타워에도
귀속되지 않는다**(의도된 거동). 역참조는 여전히 없어 NRE 위험은 없지만, 스킬 쪽이 나중에 `Source`를 채우게
되면 성장형 타워의 킬 집계가 함께 바뀐다는 점만 알고 있을 것. **(버프) 이 접점은 #315로 사라졌다** — 버프 스킬이 `Tower.Active` 순회 + `Tower.ApplyBuff`로 Combat에 닿는 유일한 경로였고, 지금 스킬→Combat 방향은 감전의 `IDamageable`/`DamageInfo` 하나뿐이다. `Tower.ApplyBuff` 자체는 남아있으며 소비처는 적 디버프(`EnemyApplyTowerDebuffAction`)뿐이다 |
| TowerFusion ↔ Combat                     | `Tower.Asset` 읽기로 재료 TowerID 매칭. **재료 소모는 #263부터 2단계** — 클릭 시 `SetActive(false)`(소프트, `OnDisable`이 `Tower.Active` 해제·행동 `Dispose`·원장 비움) → 확정 시 `Destroy` / 취소 시 `SetActive(true)`(`OnEnable`이 타일 버프 재적용·행동 재무장·재등록). **`Tower.OnEnable`/`OnDisable`의 대칭 왕복이 이 원복의 유일한 근거다** — 풀 재사용용으로 만들어진 것을 합성이 그대로 쓴다. SUNGSOO가 그 대칭을 깨거나(한쪽에만 상태 추가) `Tower.data`/`Asset`·`TowerAsset` 필드 그룹을 바꾸면 매칭·배치·**원복**이 깨짐(WL-001 축, 읽기 접근자는 muchan이 `Tower.cs`에 추가) |
| TowerFusion ↔ MouseManager/TowerPlacer   | `TowerPlacer` 신규 오버로드로 고스트 배치 재사용(확정 콜백=커맨드 `Commit`, 종료 콜백=`Undo`, 비용은 기존 `TrySpend` 경로). **배치 코어는 #263에서도 무수정** — 확정/취소 구분을 커맨드가 자기 상태로 하므로 `TowerPlacer`에 "어느 쪽으로 끝났는지" 통로를 새로 뚫지 않았다. 진행 중 커맨드의 원복은 전부 기존 `CancelPlacement` 경로 하나로 수렴한다(우클릭 / 밤 전환 `PhasePanelSwitcher.ShowNight` / 새 배치의 선행 `CancelPlacement` / 씬 전환 `HandleSceneLoaded`) → 합성 전용 정리 코드 없음. tileSize·풋프린트(WL-034)·타일 종류 계약(WL-067)의 동일 전제를 그대로 상속 — TowerPlacer가 신맵 질의로 이관되면 합성 배치도 함께 따라감. **선택(#183)**: `TowerMergeCoordinator`가 `MouseManager.OnGroupSelectToggled`(Shift 토글)+`OnPrimarySelect`(평클릭/빈곳, 항상 발행)를 구독해 그룹을 만든다 — MouseManager는 마커 `IGroupSelectable`(타워는 `TowerGroupSelectable`, `TowerPlacer`가 배치 시 런타임 부착)만 보고 타워를 모름. n0wst4ndup이 MouseManager 선택 계약(Shift·`OnPrimarySelect`)을 확장 → 다른 선택 소비처(건물)와 공존 확인. **밤 진입 배치 취소는 `PhasePanelSwitcher.ShowNight`로 이관**(페이즈 취소 일원화). **드래그 선택(#261)**: 코디네이터가 `OnBoxSelectBegin/Update/End`를 추가 구독해 같은 집합에 반영한다 — 시작에서 기준 집합 스냅샷(Shift면 유지 후 합집합, 아니면 교체), 갱신마다 `TowerMergeGroup.SetAll`로 순서 보존 원자 교체(내용 동일하면 no-op). **드래그 중에는 하이라이트만 실시간이고 패널·후보 버튼 갱신은 유예**(사각형이 2개를 넘나들 때 합성 패널 깜빡임·매 프레임 GC 방지), 종료 시 1회 처리. 밤 진입 시 유예 상태를 직접 해제(게이팅에 막혀 종료 통지를 못 받는 경우 대비) |
| TowerFusion ↔ Management(Resource)        | 합성 `ExtraCost`를 `ManagementController.CanAfford/TrySpend`(WL-017 게이트웨이)로 지불 — `TowerPlacer` 확정 경로 재사용(별도 차감 로직 없음). 컨트롤러가 씬에 없으면 무료(permissive) |
| Command ↔ TowerFusion / MouseManager / DayNightManager / Management | **히스토리에 오르는 것은 항상 바깥쪽 커맨드 하나**다 — 합성 결과 타워도 `TowerPlaceCommand`로 만들어지지만 `PlacementOwner.Caller` + `AdoptResult`가 편입해 따로 오르지 않는다(연출 소유권도 `PlaysUndoDissolve`로 함께). 밤 확정은 `OnDayToNight` 지연 자기구독이며 **`PhasePanelSwitcher.ShowNight`와 구독 순서가 무관하다**(진행 중 커맨드와 스택의 것은 겹치지 않는 두 집합). 자원 환원은 `ManagementController.Grant` 단일 경로(WL-017 유지). 상세 `TowerMerge.md` §9.3 |
| PhasePanelSwitcher ↔ GameManager          | **조준 취소의 소유자는 `PhasePanelSwitcher` 하나다** — 웨이브 종료는 `DayNightManager.OnDayStart`, 런 종료는 `GameManager.OnResultDecided`를 구독해 둘 다 `MouseManager.CancelSkillTargeting()`을 부른다(#391). **`MouseManager`가 직접 구독하지 않는 것이 계약이다** — `MouseManager.md` §1 원칙 2·3("매니저는 도메인을 모른다", "MouseManager는 수정되지 않아야 한다")이고, 실제로 그 파일에는 `GameManager`/`DayNightManager` 참조가 0건이다. 부수 이득: `PhasePanelSwitcher`는 `GameManager`와 수명이 같은 씬 오브젝트라 `DontDestroyOnLoad` ↔ 씬 싱글톤 재바인딩이 필요 없다(WL-002 축을 건드리지 않는다). ⚠ 두 구독은 서로 독립이므로 `Start`/`OnDestroy`의 `DayNightManager` 조기 반환보다 **앞**에 둔다 — 뒤에 두면 그쪽이 없는 씬에서 조준 취소까지 함께 빠진다. 런 종료 시 남는 다른 표시(타워 선택의 사거리 원·정보 패널)는 아직 열려 있다 — `CancelInteractions()`로 넓히면 함께 닫힌다 |
| 모든 시스템 ↔ 전역 설정                  | 레이어/태그(`ProjectSettings/TagManager.asset` — WL-005), URP 설정(`Assets/Settings`), 패키지(`Packages/manifest.json`)                                                                                                    |

## 4. 팀 계약 (위반 = 🔴 후보)

1. **입력 단일 창구**: 포인터/키보드 입력은 MouseManager만 읽는다. 게임플레이 코드의
   `Mouse.current`/`Keyboard.current` 직접 폴링 금지. 단, 씬 UI의 ESC는 씬별 단일 소유자만 직접 읽는다:
   `GameScene`은 `SettingUI`, `TitleScene`은 `MainMenuUI`가 소유한다. `SettingUI`는
   `GameManager.Instance`가 없는 씬에서 ESC를 처리하지 않는다. 같은 씬에 두 번째 ESC 소비처가 필요해지면 중앙 Cancel 라우터로
   이관하고 우선순위를 먼저 확정한다. 클릭 반응은 ISelectable, 그룹 선택 참여는
   IGroupSelectable 마커, 배치는 PlacementRequest로 참여. 스킬 타겟팅·드래그 사각형 선택(#261)도
   MouseManager 상태 추가로 구현했다 — 새 상호작용은 모드를 늘리고 **통지만** 하며, 집합·표시의
   소유는 소비처에 둔다. (Docs/Core/MouseManager.md)
2. **데이터 파이프라인**: 게임 수치는 CSV(`Assets/Resources/DataTables/`) → DataTableManager → SO
   패턴. 새 데이터 타입은 `XxxData`(POCO)+`XxxAsset`(SO)+`XxxTable` 템플릿을 따른다.
   Get 계열 null 반환 → 호출부 null 체크 필수. (Docs/Tools/DataTableManager.md)
3. **자원 흐름** (GDD §3.2): 기본 자원(나무/철/식량) = 주민 배치 생산, 마나석 = 전투 보상.
   **자원은 이 4종뿐이다(#337)** — 영토 확장 보상 경로와 특수 자원 4종(금·루비·사파이어·다이아)은
   경영 공간 영토 시스템째 삭제됐다.
   - **마나석 교환(#211, WL-042 해소)**: **연금술사의 집**(`BuildingType.Store`)이 마나석 → 자원 3종 **단방향** 교환을 제공한다
     (낮 전용, 역교환 없음). 이것은 새 획득 경로가 **아니라 마나석 소비처**다 — 지갑의 획득 API(`ResourceWallet.Add`)는
     계속 비공개이고, 소비처에 열린 것은 **차감+지급이 한 트랜잭션인 `ManagementController.TryExchange` 단일 진입점**뿐이라
     마나석 없이 자원이 생기는 경로가 없다. 신규 기능이 자원을 늘려야 할 때도 `Add`를 public으로 열지 말고 이 패턴을 따를 것.
   - **본진 업그레이드 비용(#229, WL-042 완전 종결)**: 자원 종류 자유 authoring(현재 나무·철·마나석), 차감은 동일한 `TrySpend` 경유 — 지갑을 늘리지 않는 **순수 소비 경로**라 계약 위반이 아니다.
4. **공간 분리** (GDD §4.1/§6.2): 경영 공간 = 건물, 전투 공간 = 타워. 두 영토는 독립 관리 —
   한쪽 확장이 다른 쪽 상태에 의존 금지.
5. **낮/밤 전환 계약** (GDD §5, Build0 계획): 낮 시작=본진 회복, 밤 시작(`OnDayToNight`)=전투 스테이지 확장+몬스터 스폰(#17), 밤→낮=주민 배치 기반 자원 정산(먼저)+
   주민 배치 초기화(그 다음)+웨이브 증가(모두 `OnNightToDay` 시점, #66). 페이즈에 반응하는 시스템은
   전환 이벤트 훅 구조여야 한다. (Docs/Core/DayNightManager.md)
   ⚠️ **전환은 이제 즉시 끝나지 않는다**(#101, 0.8초 셀 와이프). `CurrentPhase`는 여전히 동기로 바뀌고
   이벤트도 그 자리에서 발행되므로, **화면이 채 바뀌기 전에 후속 동작이 먼저 일어난다.** 전환 중 막아야 할
   것들(몬스터 스폰·페이즈 버튼 재클릭·주민 배치)은 `DayNightTransition.IsTransitioning`을 보거나
   `OnTransitionComplete`를 기다려야 한다 — **진입점은 뚫려 있으나 소비처가 0이다.**
   이 잠금 축은 **#101에서 분리해 WL-162로 옮겼다**(팀 결정 2026-08-07) — #101 원문의 잠금 명세가
   "라이트 전환만 UniTask Lerp"라는 전제 위에 쓰였는데 구현이 셀 와이프 + 전역 블렌드로 가면서
   전제가 바뀌어, 대상·시점을 새 구조 기준으로 재정의해야 한다(`DayNightManager.md` §7).
6. **책임 경계** (MouseManager.md §2): 배치 판정=그리드/검증 시스템, 자원 차감=경영 시스템,
   정보 표시=UI. MouseManager는 선택 사실만 통지.
7. **문서-코드 동기화**: 시스템 구현·변경 PR은 해당 Docs/ 문서 갱신 포함 필수. (일치 여부 자체는
   설계 검증이 아님 — 갱신 포함 여부만 확인)
8. **저장소 배치** (CLAUDE.md): 스크립트 정본은 `Assets/Scripts/`(공간/시스템 폴더), 씬 등 비-스크립트 WIP는 `Assets/Personal/<이름>/`, `Assets/Imported/` 수정 금지.
   - **리뷰어 주석(Imported 사각지대)**: 씬/프리팹이 참조하는 유료·벤더 에셋(건물 프리팹, BaseGate 등)은 `Assets/Imported/`(중첩 git repo)에 상주할 수 있고 자동 리뷰 봇은 이를 읽지 못한다. 메인 repo diff에 `.prefab`이 없다고 해서 "프리팹 미생성/이슈 미충족"으로 단정하지 말 것 — 유료 에셋을 팀 공용 Imported 공간에만 두는 것이 정상 배치이며, 필요 시 작성자에게 확인한다(WL-040 참고, #92 건물 프리팹이 이 사각지대의 실제 오탐 사례).
   - **StartMap 정본 예외(#335)**: 팀 합의에 따라 `StartMap.prefab`의 정본은 메인 저장소의 구 경로 `Assets/Prefabs/Map/StartMap.prefab`이 아니라 `Assets/Imported/NorthLand-Imported/@NorthLand/Prefabs/Tile/Map/StartMap.prefab`에서 관리한다. StartMap은 Imported 타일 프리팹과 자식 계층을 직접 구성하므로 관련 타일 프리팹과 같은 저장소에서 함께 변경한다. 메인 `GameScene.unity`는 이 프리팹의 GUID를 참조하며, 동일 GUID의 StartMap을 메인·Imported 양쪽에 동시에 두지 않는다.
   - **StartMap 동기화 계약**: NorthLand 실행·리뷰·빌드 전에 `NorthLand-Imported`를 동기화해야 하며 sparse checkout을 사용하면 `@NorthLand/Prefabs/Tile/Map/**`와 `@NorthLand/Prefabs/Tile/GrassTile*.prefab`을 반드시 포함한다. StartMap 또는 버프 타일 프리팹 변경은 Imported 저장소에 먼저 커밋·Push한 다음 메인 저장소의 Scene/SO 배선을 커밋한다. 미동기 환경에서는 StartMap이 Missing Prefab이 되거나 버프 타일이 일반 잔디로 폴백하고 아이콘이 누락될 수 있다(WL-082).
   - **StartMap 저장/복원 계약**: StartMap 루트에는 `StartMapTileRegistry`, 건설 가능한 자식 타일에는 고유한 `StartMapTileIdentity`가 있어야 한다. `GameScene`의 `RunSaveManager.startMapTileRegistry`는 배치된 StartMap 인스턴스의 Registry를 참조해야 하며, null이면 스타트맵 타워의 앵커 셀을 캡처할 수 없어 Run 저장/복원이 실패한다(WL-175, #328).
   - **Tank(최종보스) 정본 예외(#326, PR#368)**: 보스 프리팹의 정본은 메인 저장소의 구 경로 `Assets/Prefabs/Monster/Tank.prefab`(**삭제됨**)이 아니라 `Assets/Imported/@NorthLand/Prefabs/Boss/Tank.prefab`이다. Tank는 Imported의 모델·컨트롤러·파티클(`@NorthLand/Animations/Boss/Tank.controller`, `@NorthLand/Particles/Boss/Tank`)을 직접 조립하는 프리팹이라 그것들과 같은 저장소에서 함께 변경한다 — StartMap이 타일 프리팹과 같은 저장소에 있어야 하는 것과 같은 이유다. 동일 GUID의 Tank를 메인·Imported 양쪽에 동시에 두지 않는다.
     - ⚠ **이관이 "이동"이 아니라 "삭제 + 신규 생성"이라 GUID가 바뀌었다** — 구 `70c1fbefa025da447a8b0a70dd7c0c2a` → 신 `251ac093362a5e547a0ed5618a9729cd`. 구 GUID를 들고 있던 배선은 자동으로 따라오지 않고 **Missing Prefab으로만 나타난다.** `MonsterWave 15.asset:25`는 새 GUID로 재배선됐으나 **`BossTest.asset:18`은 구 GUID가 남아 있다**(2026-08-13 전수 확인 — 저장소 전체에서 이 한 곳). 테스트 전용 에셋이라 런에는 영향이 없지만, 보스 테스트 웨이브를 돌리면 아무것도 스폰되지 않는다.
   - **Tank 동기화 계약**: 보스가 등장하는 웨이브를 실행·리뷰·빌드하기 전에 `NorthLand-Imported`를 동기화해야 하며, sparse checkout을 쓰면 `@NorthLand/Prefabs/Boss/**` · `@NorthLand/Animations/Boss/**` · `@NorthLand/Particles/Boss/**`를 반드시 포함한다. Tank 프리팹 변경은 Imported 저장소에 먼저 커밋·Push한 다음 메인 저장소의 웨이브 SO 배선을 커밋한다. **미동기 환경에서는 최종 웨이브의 보스 그룹이 Missing Prefab이 되어 보스가 스폰되지 않고, 증상은 "최종 웨이브가 그냥 지나간다"로만 보인다** — 컴파일도 콘솔도 조용하다(WL-082와 같은 계통).
   - **몬스터 `hitPosition` 동기화 계약(#386)**: 몬스터 프리팹의 `hitPosition` 자식 트랜스폼은 **조준 방향의 정본**이다 — `AttackAction.TryAttack`이 `aimDir`의 Y를 살려 그 좌표를 향해 쏘고, `HomingFlight`의 추적점·`Projectile.ApplyArea`의 스플래시 중심·`ApplyChain`의 체인 원점·`TowerTurretAim`의 선회 대상·`Tower.IsTargetValid`의 사거리 판정이 전부 같은 필드를 읽는다. 실행·리뷰·빌드 전에 `NorthLand-Imported`를 **`57e08e53`(몬스터 히트포지션 할당 및 위치 재조정) 이상**으로 동기화해야 하며, sparse checkout을 쓰면 `@NorthLand/Prefabs/Monster/**`를 반드시 포함한다. 프리팹 변경은 Imported 저장소에 먼저 커밋·Push한 다음 메인 저장소의 코드·SO 배선을 커밋한다.
     - ⚠ **실패 모드가 두 단계이고 위쪽만 시끄럽다.** 미할당은 `Enemy.Awake`(`Enemy.cs:111-115`)가 `LogError` + 피벗 폴백으로 잡지만, **할당돼 있고 위치만 틀리면 로그가 한 줄도 없다** — 증상은 "특정 몬스터만 안 맞는다"뿐이고 컴파일도 콘솔도 조용하다(Tank 동기화 계약과 같은 계통, WL-082). 특히 떠 있는 적은 루트 피벗이 자기 콜라이더 **밖**이라 폴백조차 몸 아래를 겨눈다(`Flying_Bat` 콜라이더 y=[2.35~5.05], 루트 1.00).
     - 저작 기준: `hitPosition`은 **콜라이더 안, 몸통 중심 언저리**를 가리킨다. StartMap 스폰 y=1.00 기준 실측 — `Yellow_Grummy` 4.00 / `Red_Grummy` 4.24 / `Blue_Grummy` 6.00 / `MidBoss` 5.00 / `Flying_Bat` 4.24. 인스펙터의 로컬 Y에 부모 스케일(2.7~5.0)이 곱해지므로 표시값과 월드 높이가 다르다 — 조정 후 콜라이더 안인지 확인할 것(WL-122). `Phantom`·`Shadow`는 아직 미할당이다.
   - **Part4 타워 모델 동기화 계약(램프업·개틀링)**: 두 타워의 **모델·Animator·포탑 마디 배선·공용 탄환 프리팹이 전부 `Assets/Imported`에 있다** — 메인 저장소 쪽 변경은 SO 2종(`PlacementYaw`·`ProjectilePrefab`)과 FlatKit 변환 원장 1줄뿐이므로, **Imported를 `6c5e879c`(Feat: 램프업·개틀링 타워 모델 적용) 이상으로 동기화하지 않으면 SO의 프리팹 참조가 해소되지 않는다.** sparse checkout을 쓰면 `@NorthLand/Prefabs/Tower/GatlingShooter/**` · `@NorthLand/Prefabs/Tower/RampUpTower/**` · `@NorthLand/Prefabs/Projectile/**` · `@NorthLand/Materials/FlatKit/**` · `TowerAssets/FattyPolyTurretPart4/**`를 반드시 포함한다. 커밋 순서는 **Imported 선행**이다(WL-040). 몬스터 `hitPosition` 계약과 같은 형식이고 근거도 같지만, **사각지대가 반대 방향으로도 작동한다는 점이 이 건의 교훈이다** — 메인 diff만 보면 "프리팹 미생성"으로 오판하거나(§4 「Imported 사각지대」), 거꾸로 **원장 1줄이 보여서 "변환 완료"로 오판**한다. 후자가 실제로 났다: 2026-08-18 FlatKit 변환 사본이 만들어지고 원장에도 적혔지만 **프리팹 재배선이 빠져** 탄환이 벤더 URP Lit 머티리얼을 그대로 물고 있었다(WL-194).
   - **리뷰어 주석(죽은 사본)**: `Assets/Personal/SUNGSOO/Font/`는 폰트가 TMP 정본으로 이관되며 더 이상 참조되지 않는 죽은 사본이다 — 이 경로의 폰트 아틀라스 churn을 WL-041 재발로 보고하지 말 것(WL-041 참고, 삭제 대기 중).

## 5. 미합의 전역 계약 (합의 없는 변경·점유 = 최소 🟠)

- **레이어**: Enemy(7)/Soldier(8)/PlayerBase(9)가 `TagManager.asset`에 등재 완료(PR#80, WL-005 해소).
  단 각 스크립트(Tower/Soldier/Enemy)의 LayerMask vs Tag 방식 최종 확정은 TODO(TBD)로 남음.
  `TagManager.asset` 변경은 반드시 리뷰 대상.
  ⚠ **`3 = Tile`(전투 배치면) / `10 = Ground`(경영 보행면 · NavMesh 베이크 대상)** — 레이어 3은 예전에 `Ground`였고 #277에서 경영 보행면용 `Ground`가 10에 새로 생겼다. 이름만 보고 배치 마스크(`Tile`)와 NavMesh 마스크(`Ground`)를 맞바꾸면 조용히 어긋난다(코드 주석은 `PlacementButton.cs`).
  현재 등재: `0 Default` / `3 Tile` / `4 Water` / `5 UI` / `6 Selectable` / `7 Enemy` / `8 Soldier`(리젝 잔재) / `9 PlayerBase` / `10 Ground` / `11 MinimapOverlay` / `13 MinimapHidden`.
  **`12`는 비어 있다** — #213에서 `OutlineShell`을 회수하며 이름을 비웠다. 재사용 시 URP 렌더러의 Opaque/Transparent/Prepass 마스크에 셸 시절 제외 설정이 남아 있지 않은지 먼저 확인할 것(2026-08-09 실측으로는 PC/Mobile 모두 `-1`이라 깨끗하다).
  `13 = MinimapHidden`(#138)은 **미니맵에만 감출 월드 오브젝트**용이다 — 현재 소비처는 열기구 프리팹 2종이고, `MinMapCamera.cullingMask`에서만 제외된다.
- **카메라 컬링 마스크**: `Main Camera` = `-2049`(Everything − `MinimapOverlay(11)`), `MinMapCamera` = `-8193`(Everything − `MinimapHidden(13)`).
  두 카메라 모두 **"Everything에서 뺀다"** 형태를 유지한다 — 그래야 새 레이어가 자동으로 포함된다.
  ⚠ **인스펙터에서 Everything 상태로 항목 하나를 체크 해제하면 마스크가 "이름 있는 레이어만" 남기는 허용목록으로 바뀐다.**
  실제로 #138에서 `MinimapOverlay`만 끄려다 `10239`(비트 0~10 + 13)가 되어 **빈 슬롯 12·14~31이 전부 제외**된 적이 있다.
  그 상태에서 누가 레이어 14 이후를 등재하고 오브젝트를 올리면 **메인 화면에만 아무것도 렌더되지 않고, 컴파일 에러도 콘솔 경고도 없다.**
  마스크를 바꿀 때는 인스펙터 드롭다운이 아니라 **저장된 정수값을 확인**할 것.
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
- **연출 파티클 프리팹 규약(#138)**: 오브젝트는 **켜둔 채** `Play On Awake`를 끄고, 재생은 코드가 시킨다.
  `Looping`은 구동원이 정한다 — 1회성(행동 연출)은 off, 상시 표시(줌 힌트·배경 장식)는 on.
  `Culling Mode`는 상시 루프면 `Pause`(`PauseAndCatchup`은 화면 밖에 오래 있다가 들어올 때 밀린 시뮬레이션을 한꺼번에 따라잡아 스파이크가 난다).
  `Max Particles`는 `rate × lifetime`의 2~3배로 조인다(기본 1000은 폭주 안전장치 역할을 못 한다).
  ⚠ **비활성 GameObject에는 `ParticleSystem.Play`가 예외 없이 조용히 실패한다** — 그래서 "끄지 말고 Play On Awake만 끈다"가 규약이고,
  `FeedbackParticle`이 그 상태를 경고로 잡는다. ⚠ **풀에서 꺼낸 오브젝트는 `Awake`가 다시 돌지 않아 Play On Awake가 발화하지 않는다** —
  재사용 경로는 명시적으로 `Stop(Clear)` → `Play(true)`를 불러야 한다(안 하면 2회차부터 이펙트 없는 오브젝트가 돌아다닌다).
- **연출의 시간축 판단 기준(#138에서 정리, 규칙 확정은 WL-100 대기)**: **월드의 일부인가**로 가른다.
  월드 배경(열기구 비행·불꽃)은 `Time.deltaTime`·`Use Unscaled Time = off` — 배속에서 같이 빨라지고 일시정지에서 같이 멈추는 것이 자연스럽다.
  플레이어에게 주는 안내·피드백(건물 업그레이드 연출, 줌 힌트 파티클, `TowerSpawnEffect`/`TowerDissolveEffect`)은 `unscaledDeltaTime`·`Use Unscaled Time = on` —
  배속에서 소실되거나 일시정지 중 얼어붙으면 **정보가 전달되지 않는** 실패 모드가 된다.
- `[SerializeField] private` 필드 기본, 프로퍼티는 expression-bodied (접두 `_camelCase` vs
  `camelCase` 혼재 — 통일 미결정)
- CSV POCO는 PascalCase 프로퍼티(CsvHelper), SO는 CreateAssetMenu
- **타워 밸런싱 규약(#326)**: 정본은 [CombatBalance.md](../Core/CombatBalance.md), 저작 절차는
  [TowerAddGuide.md §3.5](../Core/TowerAddGuide.md).
  ① **`공격 간격(초) ≤ 사거리(타일) ÷ 3`** (1타일 = 6유닛) — 어기면 적이 사거리를 지나는 동안 발사가
  1~2발에 그쳐 쿨다운 위상에 따라 화력이 ±50% 이상 튄다.
  ② **합성 결과 타워의 1회 통과 킬 수 > 재료 타워 킬 수의 합** (상한 재료 합 × 1.3) — 아니면 합성이
  순손실이라 아무도 안 만든다. #326 이전에 2차 `Sniper`가 3차 `killstack`(재료가 스나이퍼)보다 강했다.
  ③ 화력 단위는 DPS가 아니라 **1회 통과 킬 수**이고, 티어별 밴드(0.1킬 = 1눈금)에서 고른다.
  ✅ **①은 `TowerAsset.OnValidate`가 강제한다**(`TowerAsset.cs:158-177` — `AttackInterval > AttackRange / 18`이면
  저장 시 경고. 18 = 타일 6유닛 × 계수 3).
  ⚠ **②·③은 코드가 잡지 못한다**(WL-169) — 재료 타워들의 킬 수 합을 알아야 해서 단일 SO 검사로는
  불가능하다. **신규 타워 PR에서 눈으로 확인할 것.**
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
