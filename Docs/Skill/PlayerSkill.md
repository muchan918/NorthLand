# 플레이어 스킬 & 보상 특수효과 시스템 가이드

플레이어 스킬 2종(감전·버프)과, 웨이브 클리어 보상으로 스킬에 특수효과를 얹는 시스템(#169)을 정리한 문서. `Assets/Scripts/Skill`의 실제 구현을 기준으로 작성했다.

> 코드가 바뀌면 이 문서도 함께 갱신해서, 문서와 실제 구현이 어긋나지 않게 유지한다.

## 1. 구성 요소

| 클래스 | 역할 |
| --- | --- |
| `SkillManager` | 감전 스킬(#103). 클릭 위치 AoE 즉시 데미지, 밤 게이팅+쿨다운. 임팩트마다 `ImpactResolved(SkillCastContext)` 이벤트 발행 |
| `BuffSkillManager` | 버프 스킬(#103). 즉시 발동, `Tower.Active` 전체에 공격력/공속 배율. 시전마다 `BuffResolved(BuffCastContext)` 이벤트 발행 |
| `SkillEffectManager` | 보상 라우터(씬 싱글톤). `WaveRewardController.GrantReward` → `ApplyReward(reward)` → 타입 매칭 효과에 레벨 가산 위임. `GetLevel(type)` / `GetStatSummary(type, levelDelta)`(#287) 조회 제공 |
| `SkillEffect` (추상) | 특수효과 공통 베이스(MonoBehaviour, `SkillEffectManager` 오브젝트에 부착). 레벨 소유 + 스킬 이벤트 구독 관리 + 표시 수치 제공(`GetStatSummary`) |
| `SkillStatsFormatter` | 보상 카드 표시 문자열의 단일 출처(#287). 라벨 조회(`NorthLand_Skills`)와 숫자 서식이 여기 한 곳에만 있다 — `TowerStatsFormatter` 대응 |
| `SkillCastContext` / `BuffCastContext` | 시전 1회의 정보 묶음. 효과들이 읽고(위치/맞은 적/지속시간), 일부 필드는 효과가 써넣는다(`ExtraImpacts`) |

## 2. 핵심 구조 — 이벤트 구독 (#169 확정)

스킬 본체는 어떤 효과가 있는지 모른다. 이벤트만 발행하고, 효과가 스스로 구독한다.

```
보상 3택1 선택
  → WaveRewardController.GrantReward
  → SkillEffectManager.ApplyReward(reward)          ← 라우터: 타입 매칭
  → SkillEffect.OnRewardApplied(amount)
       레벨 0→1 첫 획득: TrySubscribe()로 자기 스킬 이벤트에 구독 (1회)
       재선택(레벨업):   Level 변수만 가산 — 구독 없음, 수치만 강해짐

스킬 시전
  → SkillManager: 임팩트마다 ImpactResolved?.Invoke(context)
  → BuffSkillManager: 시전마다 BuffResolved?.Invoke(context)
  → 구독된(=획득한) 효과들만 호출됨. 구독자 0이면 기본 스킬 그대로
```

- 구독 대상은 `SkillEffect.TrySubscribe()`/`Unsubscribe()` virtual이 결정한다. 기본은 감전(`ImpactResolved`),
  버프 계열 효과는 override로 `BuffResolved`에 붙는다(`BurnBuff` 참고).
- 이슈 #169 원문의 "enum + 중앙 컨트롤러" 방침은 이 구조로 **변경 확정**됐다(팀 결정, 2026-07-23).

## 3. 특수효과 4종 (`WaveRewardType`)

| 타입 | 클래스 | 동작 | 레벨업 효과 |
| --- | --- | --- | --- |
| `Burn` | `BurnEffect` | 감전 착탄에 맞은 적에게 도트(대상의 `StatusEffectHandler` 재사용) | 틱 데미지 = 레벨 × 수치 |
| `Bomb` | `BombEffect`+`SkillBomb` | 착탄 지점에 구 프리팹(`Assets/Prefabs/Skill/SkillBomb.prefab`) 설치 → 지연 후 반경 폭발 | 폭발 데미지 = 레벨 × 수치 |
| `Count` | `CountEffect` | 감전이 1클릭에 총 (1+레벨)회 발동, 반복분은 0.5초 간격(UniTask). `ImpactIndex==0`에서만 가산(무한 반복 가드) | 발동 횟수 +1 |
| `BuffBurn` | `BurnBuff` | 버프 지속시간 "창" 동안 `Projectile.DamageDealt`를 구독해 타워 투사체에 명중당한 적에게 도트. 재시전 시 창 연장 | 틱 데미지 = 레벨 × 수치 |

- 수치는 전부 각 효과 컴포넌트의 **인스펙터 직접 입력**(CSV 미사용 — 스킬 수치 정책과 동일, WL-015 축).
- 반복 임팩트(Count)에서도 Burn/Bomb이 정상 발동한다 — 조합 시너지 의도.
- **웨이브 종료 시** 진행 중이던 추가시전 반복분·미폭발 폭탄은 취소된다(§5 규약, #200).

### 3.1 마법 연구소 기본 스탯 배율 (#205, 보상 축과 독립)

마법 연구소(`magic_lab`) 업그레이드 레벨은 위 보상 특수효과(`SkillEffect.Level`)와는 **완전히 별개인 두 번째 축**이다 —
연구소 레벨 = **기본 스탯 배율**(`SkillManager`의 damage/radius/cooldown, `BuffSkillManager`의 공격력·공속 배율/지속시간/쿨다운), 보상 = **특수 효과 레벨**(불변). 두 축은 동시 스택된다(`BuildingUpgrade.md` §8 착지점 확정).

- `SkillManager`/`BuffSkillManager`가 `ManagementController.GetUpgradeLevel(magicLabAsset)`로 레벨(int)만 pull하고, 레벨→배율 매핑(인스펙터 authoring 리스트, 수치 placeholder)은 각 스킬 클래스가 소유한다. `ManagementController`는 "스킬"을 전혀 모른다.
- 시전 시점에 캐싱된 유효값(`effectiveDamage` 등)을 사용 — `ImpactResolved`/`BuffResolved` 이벤트 발행이나 `SkillEffect` 구독 흐름은 건드리지 않는다. 보상 효과들은 여전히 자기 소유 필드 × `Level`로 완전히 독립 계산한다(§3 표 참고).

## 4. 새 특수효과 추가 절차

1. `WaveRewardType`에 값 추가 — **반드시 맨 뒤에**(enum은 int 직렬화라 순서 바꾸면 기존 보상 에셋이 다른 타입이 됨).
2. `SkillEffect` 파생 클래스 1개 작성 — `Type` 프로퍼티 + 훅 구현:
   - 감전에 붙는 효과: `HandleImpact(SkillCastContext)` override.
   - 다른 스킬에 붙는 효과: `TrySubscribe`/`Unsubscribe` override(대상 이벤트 교체) + 자기 핸들러.
3. **`GetStatSummary(int levelDelta)` 구현 — 선택이 아니라 필수다(#287).** `abstract`라 빠뜨리면 컴파일이 깨진다
   ("수치 표시가 없는 효과"가 조용히 출시되는 것을 막는 의도된 강제). 라벨·서식은 이 클래스 안에서 조립하지 말고
   `SkillStatsFormatter`에 Build 메서드를 추가하고, 라벨 키는 `NorthLand_Skills` 테이블의 `skills.stat.*`에 넣는다.
   수치 계산은 실제 적용부(`HandleImpact` 등)와 **같은 식**을 쓸 것 — 표시와 실효가 갈리지 않게 하려는 규약이다.
   `levelDelta`는 보상의 `Amount`이며 호출부가 넘긴다(파생이 1로 가정하지 않는다).
4. `GameScene`의 `SkillEffectManager` 오브젝트에 컴포넌트 부착 + 인스펙터 수치 입력.
5. 보상 에셋(`WaveRewardData`)의 `rewardType`을 새 타입으로 지정해 풀에 등록.

`SkillManager`/`BuffSkillManager`/`SkillEffectManager`의 **이벤트 구독 흐름(`ImpactResolved`/`BuffResolved`, `SkillEffect` 구독 관리)은 수정하지 않는 것이 정상**이다. 수정이 필요해 보이면 구조가 어긋난 것. (예외: §3.1의 마법 연구소 기본 스탯 배율은 시전 시점 base 값 계산에 얹는 별개 축이라 이 제약 밖 — 구독 흐름 자체는 그대로다.)

## 5. 규약과 함정

- **효과는 ScriptableObject가 아니라 MonoBehaviour** — 구독 여부·레벨이 런타임 상태라 SO에 넣으면 에디터에서 에셋에 값이 남는다. 레벨은 런(run) 단위 리셋이 의도 동작(씬 생명주기).
- **`SkillCastContext.HitTargets`는 재사용 버퍼** — 이벤트 처리 중에만 유효, 필드에 보관 금지.
- **`StatusEffectHandler` effectId 분리 규약**: 다른 id는 공존(각자 틱), 같은 id는 갱신. 현재 사용: 타워 오라=TowerID 해시, 감전 화상=`"skill_burn"` 해시, 버프 화상=`"buff_burn"` 해시. 새 도트 효과는 고유 문자열 해시로 분리할 것.
- **`DamageInfo` source=null 규약**: 플레이어 스킬 계열은 `IAttacker` 개체가 아니므로 source를 null로 넘긴다(`SkillManager` 주석 참고).
- **`Projectile.DamageDealt`는 static 이벤트** — 구독 해제는 구독자 책임. 파괴 경로(`OnDestroy`→`Unsubscribe`)에서 반드시 해제(`BurnBuff.DeactivateWindow` 참고).
- **웨이브/런 종료 시 진행 중 효과 취소(#200)**: 시전 후 **지연 발동하는 효과 2종 한정** — 추가시전 반복 착탄(`SkillManager.RepeatImpactsAsync`)·지연 폭탄(`SkillBomb`) — 을 취소한다. 신호는 `DayNightManager.OnNightToDay`(밤→낮=웨이브 종료)와 `GameManager.OnResultDecided`(승리/게임오버 — `EndNight()`를 안 타는 종료 경로) 둘 다 구독(적이 사라졌거나 결과 화면 뒤에서 잔존 발동 방지). 추가 착탄은 파괴 토큰과 링크한 `CancellationTokenSource`로, 폭탄은 `initialized`를 내리고 자기 파괴. **취소 대상 아님**: 버프 화상 창(`BurnBuff.WindowAsync`)·타워 버프 배율(`Tower.ApplyBuff`)·적 DoT는 자체 타이머로 만료된다(낮엔 타워가 밤 게이팅돼 실害 0). 조준(타겟팅) 모드 취소는 별개로 `PhasePanelSwitcher`가 `OnDayStart`에서 담당.
- 관련 WatchList: **WL-074**(버프 데미지 배율이 투사체에 미적용 — 공속만 실효), **WL-068**(스킬 시전 Y와 몬스터 부양 높이 불일치 시 적중 0 — #200에서 "지면까지 닿는 길쭉한 트리거 콜라이더" 계약으로 해소 방향 확정), **WL-050**(배율 덮어쓰기 비스택).

## 6. 잔여 작업 (#169)

- 보상 에셋 4종 표시명·설명을 신규 효과에 맞게 재작성 + `NorthLand_Rewards`(ko/en/ja) 로컬라이즈 키 4종 추가 — 현재는 구 키(`rewards.fire.*`)가 그대로 보인다.
- WL-074 수정(Combat 소유 — SUNGSOO 협의 후 한 단어).
