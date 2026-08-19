# 플레이어 스킬 & 보상 특수효과 시스템 가이드

플레이어 스킬(감전 1종)과, 웨이브 클리어 보상으로 스킬에 특수효과를 얹는 시스템(#169)을 정리한 문서. `Assets/Scripts/Skill`의 실제 구현을 기준으로 작성했다.

> 코드가 바뀌면 이 문서도 함께 갱신해서, 문서와 실제 구현이 어긋나지 않게 유지한다.

> ⚠️ **버프 스킬은 제거됐다 (#315).** 아래 4개 파일은 저장소에 그대로 있지만 **씬에 배선돼 있지 않아 실행되지 않는다** — `Assets/Scripts/Skill/BuffSkillManager.cs` · `BuffCastContext.cs` · `BurnBuff.cs` · `Assets/Scripts/UI/Skill/BuffSkillButtonView.cs`. `BuffSkillManager.Instance`는 **항상 null**이고, `BurnBuff`는 어디에도 부착돼 있지 않으며, `BuffBurnReward`는 `WaveRewardPool`에서 빠져 뽑히지 않는다. 이 문서에서 버프 관련 서술은 **"어떻게 동작했는가"의 기록**이지 현재 동작이 아니다. 되살리려면 코드를 다시 쓰는 게 아니라 씬 배선과 기획 재검토가 선행돼야 한다. 제거 이유: 조준도 타이밍 판단도 없어 "쿨 차면 누른다"가 유일한 최적 플레이였다(GDD §5.5).

## 1. 구성 요소

| 클래스 | 역할 |
| --- | --- |
| `SkillManager` | 감전 스킬(#103). 클릭 위치 AoE 즉시 데미지, 밤 게이팅 + **충전(탄약) 소모**(#319). 임팩트마다 `ImpactResolved(SkillCastContext)` 이벤트 발행 |
| ~~`BuffSkillManager`~~ | **미사용 (#315)** — 버프 스킬(#103). 즉시 발동, `Tower.Active` 전체에 공격력/공속 배율. 씬 미배선이라 `Instance`가 항상 null |
| `SkillEffectManager` | 보상 라우터(씬 싱글톤). `WaveRewardController.GrantReward` → `ApplyReward(reward)` → 타입 매칭 효과에 레벨 가산 위임. `GetLevel(type)`(세이브용) / 보상 후보 필터용 `IsMaxLevel(type)`(#292) / 카드 표시용 `GetSnapshot(type)` 제공 — 표시에 필요한 `Level`·`NextLevel`·`NextIsMax`·`Stats`(#287·#292)를 한 벌로 넘긴다. **값마다 따로 조회하지 말 것**(#353) |
| `SkillEffect` (추상) | 특수효과 공통 베이스(MonoBehaviour, `SkillEffectManager` 오브젝트에 부착). 레벨·상한 소유 + 스킬 이벤트 구독 관리 + 표시 수치 제공(`GetStatSummary`) |
| `SkillStatsFormatter` | 스킬 수치 표시 문자열의 단일 출처(#287). 라벨 조회(`NorthLand_Skills`)와 숫자 서식이 여기 한 곳에만 있다 — `TowerStatsFormatter` 대응. 보상 카드용 줄 + 마법 연구소 강화 미리보기용 줄(#398, §3.1)을 함께 소유한다 |
| `SkillHitScan` | 스킬 범위 판정의 단일 출처(#398, static). 감전·폭탄·전기장이 같은 수직 캡슐 규칙(`VerticalRange` 12f)으로 적을 모은다 — 버퍼 자동 확장 + `IDamageable` 중복 제거 포함(§5) |
| `SkillCastContext` | 시전 1회의 정보 묶음(착탄 위치/맞은 적). 효과가 써넣는 필드는 없다 — 읽기 전용(#319). 짝인 `BuffCastContext`는 버프 스킬과 함께 **미사용 (#315)** |
| `SkillVisualSet` | 마법 연구소 레벨 → 감전 착탄 이펙트 프리팹 매핑 SO(`Assets/Resources/ScriptableObjects/Skill/`). `Resolve(level)`로 구간 조회, `SkillManager`가 레벨 변경 시에만 캐싱 |

## 2. 핵심 구조 — 이벤트 구독 (#169 확정)

스킬 본체는 어떤 효과가 있는지 모른다. 이벤트만 발행하고, 효과가 스스로 구독한다.

```
보상 3택1 선택
  → WaveRewardController.GrantReward
  → SkillEffectManager.ApplyReward(reward)          ← 라우터: 타입 매칭
  → SkillEffect.OnRewardApplied()                    ← 한 번 선택 = 1레벨(#292)
       레벨 0→1 첫 획득: TrySubscribe()로 자기 스킬 이벤트에 구독 (1회)
       재선택(레벨업):   Level 변수만 가산 — 구독 없음, 수치만 강해짐
       maxLevel 도달:    더 오르지 않고, 다음 웨이브부터 후보에서 제외(#292)

스킬 시전
  → SkillManager: 임팩트마다 ImpactResolved?.Invoke(context)
  → 구독된(=획득한) 효과들만 호출됨. 구독자 0이면 기본 스킬 그대로
```

- 구독 대상은 `SkillEffect.TrySubscribe()`/`Unsubscribe()` virtual이 결정한다. 기본은 감전(`ImpactResolved`)이며,
  **다른 이벤트에 붙는 효과는 override로 대상을 바꾼다.** 이 확장점은 살아있지만 현재 실제 소비자는 없다 —
  유일한 사례였던 `BurnBuff`(`BuffResolved` 구독)가 버프 스킬과 함께 제거됐다(#315).
- 이슈 #169 원문의 "enum + 중앙 컨트롤러" 방침은 이 구조로 **변경 확정**됐다(팀 결정, 2026-07-23).

## 3. 특수효과 4종 (`WaveRewardType`)

| 타입 | 클래스 | 동작 | 레벨업 효과 |
| --- | --- | --- | --- |
| `Burn` | `BurnEffect` | 감전 착탄에 맞은 적에게 도트(대상의 `StatusEffectHandler` 재사용) | 틱 데미지 = 레벨 × 수치 |
| `Bomb` | `BombEffect`+`SkillBomb` | 착탄 지점에 구 프리팹(`Assets/Prefabs/Skill/SkillBomb.prefab`) 설치 → 지연 후 반경 폭발 | 폭발 데미지 = 레벨 × 수치 |
| `Count` | `CountEffect` | 감전의 **최대 충전**을 레벨만큼 올린다(총 1+레벨발). 충전 보유·소모·재적립은 `SkillManager`가 소유하고, 이 효과는 `OnLevelChanged`로 레벨을 밀어넣기만 한다(#319) | 최대 충전 +1 |
| `Field` | `FieldEffect`+`SkillField` | 착탄 지점에 장판 프리팹(`Assets/Prefabs/Skill/SkillField.prefab`) 설치 → `duration` 동안 `tickInterval`마다 **그 순간** 범위 안의 적에게 즉시 데미지 (#316) | 틱 데미지 = 레벨 × 수치 |
| ~~`BuffBurn`~~ | ~~`BurnBuff`~~ | **미사용 (#315)** — 버프 스킬 전용 효과였다. enum 값·`BuffBurnReward.asset`·클래스는 남아있으나 `WaveRewardPool`에서 빠져 뽑히지 않고, 컴포넌트도 씬에 없다 | — |

> **화상과 전기장은 축이 다르다.** 화상은 맞은 **대상**에 DoT를 붙여 따라다니고(`StatusEffectHandler`), 전기장은 **위치**에 결속된다 — 적이 장판을 벗어나면 그 즉시 데미지가 끊기고 재진입하면 다음 틱부터 다시 들어간다. 그래서 `FieldEffect.HandleImpact`는 `context.HitTargets`를 읽지 않고 `context.Position`만 쓰며, 대상에 상태를 붙이지 않는다(붙이면 장판을 벗어나도 계속 틱뎀이 들어가 정체성이 무너진다).

> **종류 4 = 카드 3**이라 첫 웨이브부터 후보가 섞이기 시작했다(#316). 다만 GDD §5.6의 조항(종류 ≥ 상한 + 3 = **6종**)에는 여전히 미달이라 **완화지 해소가 아니다** — 한 효과가 Lv3에 닿는 순간 남은 3종이 매번 전부 제시된다.

- 수치는 전부 각 효과 컴포넌트의 **인스펙터 직접 입력**(CSV 미사용 — 스킬 수치 정책과 동일, WL-015 축).
- 충전을 연달아 쓰면 시전마다 Burn/Bomb/Field가 각각 발동한다 — 조합 시너지 의도. 전기장은 대상에 붙는 디버프가 아니라 독립 오브젝트라 **장판이 여러 장 생겨 겹치는 자리에서 데미지가 합산**된다(각자 독립 타이머라 "갱신"으로 죽지 않는다). 중첩 상한은 두지 않았다 — 다만 자동 반복이 사라진 뒤로는 플레이어가 같은 자리에 다시 조준해야만 겹친다(#319).
- **웨이브 종료 시** 미폭발 폭탄·활성 장판은 취소된다(§5 규약, #200). 감전 자체에는 예약된 지연 발동이 없어 `SkillManager`는 취소 대상이 없다(#319).

### 3.1 마법 연구소 강화 (#205, 보상 축과 독립) — 기본 스탯 배율

마법 연구소(`magic_lab`) 업그레이드 레벨은 위 보상 특수효과(`SkillEffect.Level`)와는 **완전히 별개인 두 번째 축**이다 —
연구소 레벨 = **기본 스탯 배율**(`SkillManager`의 damage/radius/cooldown), 보상 = **특수 효과 레벨**(불변). 두 축은 동시 스택된다(`BuildingUpgrade.md` §8 착지점 확정).

- `SkillManager`가 `ManagementController.GetUpgradeLevel(magicLabAsset)`로 레벨(int)만 pull하고, 레벨→배율 매핑(인스펙터 authoring 리스트, 수치 placeholder)은 스킬 클래스가 소유한다. `ManagementController`는 "스킬"을 전혀 모른다.
- `SkillUpgradeLevel`에는 버프용 배율 4종(`BuffDamageMultiplierScale`/`BuffAttackSpeedMultiplierScale`/`BuffDurationMultiplier`/`BuffCooldownMultiplier`)도 남아있으나 **읽는 쪽이 없어 무의미하다** (#315, `BuildingUpgrade.md` §8).
- 시전 시점에 캐싱된 유효값(`effectiveDamage` 등)을 사용 — `ImpactResolved` 이벤트 발행이나 `SkillEffect` 구독 흐름은 건드리지 않는다. 보상 효과들은 여전히 자기 소유 필드 × `Level`로 완전히 독립 계산한다(§3 표 참고).
- **이 배율은 경영 패널에 실수치로 노출된다(#375 도입 → #398 개정)** — 연구소를 클릭하면 업그레이드 버튼 위에 "데미지: 36 → 42" 형태로 이번 업그레이드의 전후 값이 뜬다. 도입 당시에는 "감전 데미지가 기본 대비 N% 증가합니다"라는 % 문장형이었는데, 베이스 스탯이 화면 어디에도 없어 플레이어가 %를 실제 값으로 환산할 수 없었다(#398). 그 레벨에서 값이 변하지 않는 스탯은 줄이 통째로 빠진다.
  - **수치를 조립하는 쪽은 여전히 `BuildingInfoUI`**다(`SkillManager`가 아니다) — 매니저는 자기 `_magicLabAsset` 하나만 알아서, 두 번째 스킬 강화 건물이 생기면 클릭한 건물과 다른 수치를 내놓는다. 배율은 선택된 건물의 `UpgradeSteps`에서 읽는다.
  - **다만 베이스 스탯은 이제 UI가 읽는다** — %표기 시절 "UI는 베이스 스탯을 모른다"는 전제가 절대값 표시로 깨졌고, `damage`/`radius`/`cooldown`을 `BaseDamage`/`BaseRadius`/`BaseCooldown`으로 공개했다(배율 적용 **전** 원본이며, `Radius`(= `effectiveRadius`, 조준 인디케이터용)와 혼동 금지). 곱셈은 `static SkillManager.Scale`을 UI와 `RefreshUpgrade`가 **공유**한다 — 0/음수 배율 방어(`PositiveOr1`)까지 한 식에 있어야 "패널엔 36인데 실제론 30"이 안 생긴다.
  - 서식·라벨은 로컬라이제이션 문장이 아니라 **`SkillStatsFormatter`**(`BuildSkillDamageLine`/`…RadiusLine`/`…CooldownLine`)가 소유하고, 라벨 키는 `NorthLand_Skills`의 `skills.stat.damage`/`.radius`/`.cooldown`(+ 단위 `skills.stat.unit.second`)이다. 옛 `NorthLand_default`의 `lab.effect.*` 3키는 삭제됐다. 상세는 `BuildingUpgrade.md` §8.

### 3.2 마법 연구소 착탄 이펙트 교체 (#206)

연구소 레벨은 기본 스탯 배율(§3.1)뿐 아니라 **감전 착탄 이펙트 프리팹**도 바꾼다. 같은 축(연구소 레벨)의 두 번째 효과이며, 보상 특수효과(§3)와는 여전히 독립이다.

- 매핑은 `magic_lab.asset`이 아니라 **별도 SO(`SkillVisualSet`)** 가 소유한다. 배율은 도달 비용과 같은 리스트에 있어야 레벨 개수가 어긋나지 않지만(§3.1, PR#216), 이펙트는 **레벨마다 하나씩 있을 필요가 없어** 요구 조건이 다르다. 데이터 SO에 뷰 에셋 참조를 섞지 않으려는 의도도 있다.
- **희소 매핑**: 각 엔트리의 `FromLevel`은 "이 레벨 **이상**에서 적용"을 뜻하고, `Resolve(level)`은 `FromLevel <= level` 중 가장 큰 것을 고른다. 연구소가 5레벨이어도 `0 / 3 / 5` 세 칸만 채우면 되고, 나중에 레벨이 늘어도 세트를 손댈 필요가 없다. **`FromLevel = 0` 엔트리는 넣어둘 것** — 없으면 레벨 0 구간에서 폴백으로 떨어져 이펙트가 안 보인다.
- 조회 결과(`LevelVisual`)는 `SkillManager.RefreshUpgrade`에서 **레벨이 바뀔 때만** 캐싱한다(`_currentVisual`) — `effectiveDamage` 등을 미리 계산해 두는 것과 같은 이유로, 시전마다 리스트를 돌지 않는다.
- `ApplyImpact`이 그 프리팹을 스폰한다. 세트 미배선이거나 해당 레벨 엔트리가 없으면 기존 `impactEffectPrefab`으로 **폴백**하므로, 세트를 비워두면 이 기능 도입 전과 동일하게 동작한다.
- 엔트리별 `ScaleWithRadius`(기본 켬)를 켜면 이펙트 크기를 `effectiveRadius / radius` 비율로 보정한다 — 연구소가 `RadiusMultiplier`도 올리므로, 보정하지 않으면 조준 인디케이터 반경과 눈에 띄게 어긋난다.

**축 경계 — 낙하·메테오 연출은 이 축이 아니다.** 이 축이 바꾸는 건 "착탄 지점에 무엇을 스폰하느냐"뿐이고, 스킬은 **즉발형 그대로**다(데미지는 `CastAt` 시점에 확정, 시전 흐름 `CastAt`/`ImpactResolved` 무수정). 하늘에서 떨어지는 메테오처럼 **이동 + 지연 데미지**가 필요한 연출은 보상 특수효과 축(§3, §4 절차)에 `SkillEffect` 파생으로 넣는다 — `BombEffect`+`SkillBomb`이 이미 "착탄 지점에 프리팹 소환 → 자체 타이머 → 자기 반경 데미지 → 웨이브 종료 시 발동 없이 소멸" 패턴을 확립해 뒀다. 그렇게 나누면 기본 감전 데미지는 즉발로 이미 들어간 뒤라 "적이 먼저 죽고 나중에 메테오가 떨어지는" 어색함도 생기지 않는다.

## 4. 새 특수효과 추가 절차

1. `WaveRewardType`에 값 추가 — **반드시 맨 뒤에**(enum은 int 직렬화라 순서 바꾸면 기존 보상 에셋이 다른 타입이 됨).
2. `SkillEffect` 파생 클래스 1개 작성 — `Type` 프로퍼티 + 훅 구현:
   - 감전에 붙는 효과: `HandleImpact(SkillCastContext)` override.
   - 다른 이벤트에 붙는 효과: `TrySubscribe`/`Unsubscribe` override(대상 이벤트 교체) + 자기 핸들러. 현재 사용처는 없다(제거된 `BurnBuff`가 유일한 선례 — #315).
3. **`GetStatSummary()` 구현 — 선택이 아니라 필수다(#287).** `abstract`라 빠뜨리면 컴파일이 깨진다
   ("수치 표시가 없는 효과"가 조용히 출시되는 것을 막는 의도된 강제). 라벨·서식은 이 클래스 안에서 조립하지 말고
   `SkillStatsFormatter`에 Build 메서드를 추가하고, 라벨 키는 `NorthLand_Skills` 테이블의 `skills.stat.*`에 넣는다.
   수치 계산은 실제 적용부(`HandleImpact` 등)와 **같은 식**을 쓸 것 — 표시와 실효가 갈리지 않게 하려는 규약이다.
   다음 레벨은 직접 `Level + 1`로 계산하지 말고 베이스의 `NextLevel`을 쓴다 — 상한(#292)이 여기서 잘리므로,
   직접 계산하면 카드가 `Lv 3 → Lv 4` 같은 도달 불가 수치를 보여준다.
4. `GameScene`의 `SkillEffectManager` 오브젝트에 컴포넌트 부착 + 인스펙터 수치 입력(**`Max Level` 포함** — 씬에 저장돼야 값이 authoring된다).
5. 보상 에셋(`WaveRewardData`)의 `rewardType`을 새 타입으로 지정해 풀에 등록.

`SkillManager`/`SkillEffectManager`의 **이벤트 구독 흐름(`ImpactResolved`, `SkillEffect` 구독 관리)은 수정하지 않는 것이 정상**이다. 수정이 필요해 보이면 구조가 어긋난 것. (예외: §3.1의 마법 연구소 기본 스탯 배율은 시전 시점 base 값 계산에 얹는 별개 축이라 이 제약 밖 — 구독 흐름 자체는 그대로다.)

## 5. 규약과 함정

- **효과는 ScriptableObject가 아니라 MonoBehaviour** — 구독 여부·레벨이 런타임 상태라 SO에 넣으면 에디터에서 에셋에 값이 남는다. 레벨은 런(run) 단위 리셋이 의도 동작(씬 생명주기).
- **레벨 상한은 베이스가 소유한다(#292)** — `SkillEffect.maxLevel`(인스펙터, 기본 3)과 `NextLevel`/`IsMaxLevel`/`NextIsMaxLevel`이 전부 베이스에 있어 파생 4종은 클램프를 신경 쓰지 않는다. 만렙 효과를 후보에서 빼는 판정은 `WaveRewardController.CanOffer`가 소유하며, `WaveRewardPool`은 델리게이트만 받는다(SO가 씬 싱글톤을 모르게 하려는 경계). 상한값은 보상 종류 수와 함께 판단할 것 — GDD §5.6 참고.
- **`SkillCastContext.HitTargets`는 재사용 버퍼** — 이벤트 처리 중에만 유효, 필드에 보관 금지.
- **착탄 이펙트 프리팹은 스스로 끝나야 한다(§3.2)** — `ApplyImpact`은 `Instantiate`만 하고 수명을 관리하지 않는다. 프리팹 파티클의 `Stop Action`을 `Destroy`로 두는 것이 유일한 정리 수단이며, **자식 파티클이 하나라도 `Looping`이면 발동하지 않는다**(루트+자식이 모두 끝나야 트리거). 루프용 변형(`*_Loop_*`)을 쓸 때 특히 주의. 세트에 프리팹을 꽂을 때마다 확인할 것.
- **`_currentVisual`은 레벨 변경 시에만 갱신된다(§3.2)** — 플레이 중 `SkillVisualSet`을 편집해도 즉시 반영되지 않는다. 튜닝 중이면 플레이를 재시작하거나 연구소를 한 단계 올려 `RefreshUpgrade`를 태울 것.
- **`StatusEffectHandler` effectId 분리 규약**: 다른 id는 공존(각자 틱), 같은 id는 갱신. 현재 사용: 타워 오라=TowerID 해시, 감전 화상=`"skill_burn"` 해시. (`"buff_burn"`은 제거된 버프 화상이 쓰던 id — #315, 재사용 금지) 새 도트 효과는 고유 문자열 해시로 분리할 것.
- **`DamageInfo` source=null 규약**: 플레이어 스킬 계열은 `IAttacker` 개체가 아니므로 source를 null로 넘긴다(`SkillManager` 주석 참고).
- **`Projectile.DamageDealt`는 static 이벤트** — 구독 해제는 구독자 책임. 파괴 경로(`OnDestroy`→`Unsubscribe`)에서 반드시 해제. `BurnBuff`는 #315로 미사용이 됐고 현재 구독자는 `RampAction`(`Trigger=Hit`)뿐이다. ⚠ **빔 타워(`BeamAction`)는 이 이벤트를 발행하지 않으므로**, 여기 붙는 새 효과는 빔 타워에서 조용히 빠진다(WL-155).
- **웨이브/런 종료 시 진행 중 효과 취소(#200)**: 시전 후 **지연·지속 발동하는 효과 2종 한정** — 지연 폭탄(`SkillBomb`)·지속 장판(`SkillField`, #316) — 을 취소한다(추가시전이 충전형이 되며 반복 착탄이 사라져 `SkillManager`는 취소 대상이 없다, #319). 신호는 `DayNightManager.OnNightToDay`(밤→낮=웨이브 종료)와 `GameManager.OnResultDecided`(승리/게임오버 — `EndNight()`를 안 타는 종료 경로) 둘 다 구독(적이 사라졌거나 결과 화면 뒤에서 잔존 발동 방지). 폭탄·장판은 `initialized`를 내리고 자기 파괴(장판은 잔류 딜 없이 즉시 사라진다). **취소 대상 아님**: 적 DoT는 자체 타이머로 만료된다(낮엔 타워가 밤 게이팅돼 실害 0). 조준(타겟팅) 모드 취소는 별개로 `PhasePanelSwitcher`가 소유한다 — 웨이브 종료는 `OnDayStart`, 런 종료는 같은 `OnResultDecided`를 구독해 `MouseManager.CancelSkillTargeting()`을 부른다(#391). 입력 매니저 쪽에서 구독하지 않는 이유는 `MouseManager.md` §1 원칙 2·3(매니저는 도메인을 모른다)이며, 씬 오브젝트인 이쪽이 `GameManager`와 수명이 같아 재바인딩도 불필요하다.
- **범위 판정의 수직 축은 반경으로 풀지 말 것(#316 → #398 단일 출처화)** — 시전면·착탄점은 낮은 평면인데(`SkillButtonView._castHeight`, 씬 2) 지상 몬스터는 타일 표면 +6f(`CombatMapTileSpawner.monsterWaypointYOffset`, WL-063), 공중 몬스터는 거기서 +4f(`FlyingMonsterMove.altitude`) 더 떠 있다. 수평 반경으로 이 차이를 덮으면 밸런싱 범위가 같이 커지고 원반 인디케이터보다 넓게 맞으므로, **축을 나눠 수직만 연다** — `Physics.OverlapCapsuleNonAlloc`, 수평 단면은 정확히 반경 `radius`의 원이라 인디케이터·장판 비주얼과 1:1로 맞는다.
  - #316에서 `SkillField`만 이 방식이었고 감전·폭탄은 `OverlapSphere`로 남아 **공중 유닛에 적중 0**이었다(#398). 지금은 셋 다 **`SkillHitScan.CollectEnemies`** 한 곳을 통과한다 — 수직 폭은 `SkillHitScan.VerticalRange`(12f) 상수 하나가 소유하고, `SkillField`의 기즈모도 같은 값을 그린다(기즈모가 자기 값을 따로 들면 눈에 보이는 범위가 거짓이 된다).
  - 캡슐은 시전면 **위아래 양쪽**으로 연다 — `_castHeight`는 씬에서만 2고 스크립트 기본값은 20이라, 프리팹 리셋·신규 씬에서 시전면이 몬스터보다 위로 갈 수 있다. 시전면 아래엔 적이 없으므로 아래로 여는 대가는 없다.
  - `CollectEnemies`가 **버퍼 포화 시 2배로 키워 다시 친다**(64 시작) + `IDamageable` 단위 중복 제거. `OverlapNonAlloc`은 초과분을 조용히 버리는데, 연구소 Lv5에서 감전 반경이 27까지 커져 밀집 웨이브에서 몇 마리가 재현 불가능하게 빠지던 축이다(WL-157의 스킬·폭탄 쪽).
- ⚠ **트리거 콜라이더 방식은 이 프로젝트에서 성립하지 않는다** — WL-068이 한때 "지면까지 닿는 길쭉한 트리거 콜라이더"를 해소 방향으로 적었으나, 적 프리팹(`Tank.prefab`)에 **Rigidbody가 없어** `OnTrigger*`가 아예 발동하지 않는다(Unity는 양쪽 중 하나에 Rigidbody를 요구). `Assets/Scripts` 전체에 `OnTrigger*` 사용처가 0건이며 전 범위 판정이 `Physics.Overlap*NonAlloc`이다. 트리거로 가려면 장판 쪽에 Kinematic Rigidbody 추가 + Layer Collision Matrix 확인 + `OnTriggerExit`가 안 불리는 사망 케이스 처리가 따라붙는다 — #316이 캡슐 판정으로 해결했고 #398이 스킬 전체로 넓혔다. 그게 이 프로젝트의 방향이다.
- 관련 WatchList: **WL-068**(스킬 시전 Y와 몬스터 부양 높이 불일치 시 적중 0 — **#398에서 해소**, 위 캡슐 단일 출처 항목), **WL-050**(배율 덮어쓰기 비스택).

## 6. 잔여 작업 (#169)

- 보상 에셋 표시명·설명을 신규 효과에 맞게 재작성 + `NorthLand_Rewards`(ko/en/ja) 로컬라이즈 키 정리 — 기존 3종은 구 키(`rewards.fire.*`)가 그대로 보인다. `rewards.buff.burn.*` 키는 미사용 상태로 남아있다(#315). **전기장은 처음부터 `rewards.field.*` / `skills.stat.field_*`로 ko/en/ja 3개 로케일 모두 정상 authoring됐다(#316)** — 나머지를 정리할 때의 기준으로 쓸 것.
- **보상 종류 확장** — 4종/상한 3이라 3택1이 아직 성립하지 않는다(§3 표 아래, GDD §5.6의 종류 ≥ 6 조항). 전기장 추가(#316)로 첫 웨이브부터 후보가 섞이기 시작해 완화됐으나, 한 효과가 만렙에 닿으면 다시 무너진다.
