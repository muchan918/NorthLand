using System;
using NorthLand.Core;
using UnityEngine;

namespace NorthLand.Combat
{
    // IBaseStructure(#453)는 값 없는 역할 마커다 — 자폭병이 "본진"을 알아보는 유일한 축이며,
    // 구체 타입 검사를 대신한다. 본진 역할이 늘어나면(부성문·방벽 등) 그쪽에도 이 마커만 붙인다.
    public class PlayerBase : MonoBehaviour, IBaseStructure
    {
        [SerializeField] float maxHp = 200f;

        // ── 피격 경고음 (§6.4) ─────────────────────────────────────────────
        // **본진은 타격음을 소유하지 않는다.** 타격음의 주인은 때리는 쪽이고
        // (`EnemyAsset.AttackSfx`/`ImpactSfx`) 위치 기반 풀로 나가 화면 밖에서 무음이다.
        // 본진에 남은 것은 그 반대 축 하나 — 「본진이 깎였다」는 **2D 경고음**이다.
        // 패배 조건에 직결된 신호라 카메라가 어디를 보고 있든 들려야 하고, 그래서 `CombatSfx`가
        // 아니라 `Sfx.BaseDamaged()`(2D)로 낸다.
        //
        // 클립은 `SfxBank`가 든다 — 여기 `[SerializeField]`로 두면 씬의 `PlayerBase`가
        // `Assets/Imported`의 `Gate_02.prefab`이라 **메인 저장소만 받은 환경에서 조용히 무음**이 된다.
        // 뱅크 에셋은 메인 저장소(Resources)에 있어 그 축이 아예 없다.
        //
        // **구독 컴포넌트로 빼지 않았다.** 경고는 모든 피해 경로(평타·자폭·돌진)가 지나는
        // `TakeDamage` 한 곳에서 나야 하는 보편 신호라, 컴포넌트로 두면 프리팹에서 빠졌을 때
        // 에러 없이 조용히 무음이 된다 — 발사음을 `Tower.RaiseFired`에 둔 것과 같은 축이다(§6.3).
        // ⚠ **매 피격이 아니라 HP 임계를 아래로 통과할 때만 울린다.** 사이렌은 「지금 위험하다」는
        // **상태** 신호라 드물게 울려야 무게가 산다. 한 방이 본진 HP의 5~25%(잡몹 10 ~ 탱크 50 / 200)라
        // 매 피격에 물리면 공성 중에는 거의 연속으로 울리고, 연속으로 울리는 사이렌은 정보가 아니라
        // 밤의 배경음이 된다 — 특히 자폭병은 웨이브당 설계된 이벤트라(규약 ④) 런 내내 11번 울렸다.
        // 개별 타격의 피드백은 가해자의 위치음이 이미 담당한다(§6.4).
        [Header("피격 경고음")]
        [Tooltip("경고음을 낼 HP 비율 임계. 0.75 = HP가 75% 아래로 처음 떨어질 때 1회. " +
                 "정렬 순서는 상관없다. 클립은 SfxBank의 BaseDamaged가 든다.")]
        [SerializeField] float[] warningHpThresholds = { 0.75f, 0.5f, 0.25f, 0.1f };

        // 자기 소리 겹침 방지용 **하한**이다 — 주 게이트는 위 임계다.
        // `PlaySfx`는 `PlayOneShot`이라 동시재생 상한이 없어서, 임계 둘을 짧은 간격으로 연달아
        // 통과하면 사이렌 두 벌이 겹쳐 볼륨이 튄다. 클립 길이 이상으로 잡는다(현재 클립 1.10초).
        // ⚠ 이 하한에 막힌 통과는 **그냥 버린다.** 1초 전에 이미 사이렌이 울렸으므로 "위험하다"는
        // 전달됐고, 큐에 쌓아 뒤늦게 울리면 상황과 어긋난 시점에 경보가 난다.
        [Min(0f)]
        [Tooltip("경고음이 다시 날 수 있기까지의 최소 실시간 간격(초). 임계를 연달아 통과할 때 " +
                 "사이렌이 자기끼리 겹치는 것만 막는 하한이다 — 클립 길이 이상으로 둘 것.")]
        [SerializeField] float warningMinInterval = 1.2f;

        float lastWarningTime = float.NegativeInfinity;

        // 아래로 통과해 이미 알린 임계 수. **개수로 세는 것이 의도다** — 비율이 내려갈수록
        // 「만족하는 임계 수」는 단조 증가하므로, 배열이 어떤 순서로 저작돼 있어도 옳게 동작한다.
        // 인덱스나 값으로 추적하면 정렬 전제가 생기고, 저작자가 순서를 섞어 넣으면 조용히 틀린다.
        int announcedThresholds;

        float currentHp;

        // 성문(BaseGate)이 밤에 런타임 스폰되므로(MonsterSpawn.UpdateGate), 씬 싱글톤 +
        // 스폰 통지 이벤트로 UI가 "이미 있음"/"앞으로 생김" 두 경우를 한 경로로 처리하게 한다.
        // (TowerInfoUI/DayNightManager와 동일한 씬 싱글톤 계보)
        public static PlayerBase Instance { get; private set; }
        public static event Action<PlayerBase> OnBaseSpawned;
        public static event Action<DamageInfo, float> Damaged;

  
        void Awake()
        {
            currentHp = maxHp;
            Instance = this;
            OnHpChanged?.Invoke(currentHp, maxHp);
            OnBaseSpawned?.Invoke(this);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public Faction Faction => Faction.Player;
        public bool IsDead => currentHp <= 0f;

        // HP UI(상단 본진 체력바)가 구독하는 공개 계약. Awake와 TakeDamage에서 통지.
        public float CurrentHp => currentHp;
        public float MaxHp => maxHp;

        // 본진은 별도 피격 지점을 두지 않고 피벗을 그대로 쓴다(기존 동작 유지).
        public Transform HitPosition => transform;

        public event Action<float, float> OnHpChanged;

        /// [테스트 전용] 켜져 있으면 모든 피해를 무시한다. 기본 false라 정상 플레이 동작에 영향이 없다.
        /// 밖에서 매 프레임 HP를 되돌리는 방식으로는 대체할 수 없다 — TakeDamage가 그 자리에서
        /// GameOver()를 부르고, TryRestoreCurrentHp는 0 이하 복원을 거부하기 때문.
        public bool DebugInvincible { get; set; }

        public void TakeDamage(DamageInfo info)
        {
            if (IsDead || DebugInvincible) return;   // 이미 파괴됨 또는 무적 — 추가 피해·중복 판정 차단

            float appliedDamage = Mathf.Clamp(info.Amount, 0f, currentHp);
            currentHp -= info.Amount;
            Damaged?.Invoke(info, appliedDamage);
            PlayDamageWarning(appliedDamage);
            // Debug.Log($"{name} took {info.Amount} dmg, hp={currentHp}");   // 디버그용 — 전투 중 로그 스팸 방지 위해 비활성
            OnHpChanged?.Invoke(currentHp, maxHp);

            if (IsDead)
                GameOver();
        }

        /// 본진 피격 경고음(§6.4). **2D다** — `Sfx.BaseDamaged()` → `AudioManager.PlaySfx` 경로라
        /// 카메라가 어디를 보고 있든 같은 크기로 들린다. 클립은 `SfxBank`가 든다.
        ///
        /// ⚠ **타격음이 아니다.** 타격음은 가해자가 소유하고(`EnemyAsset.AttackSfx`/`ImpactSfx`)
        /// 위치 기반 풀로 나가 화면 밖에서 무음이다. 이 소리는 그 반대 축 하나만 맡는다 —
        /// 「본진이 깎였다」는 패배 조건 직결 신호라 화면을 안 보고 있을 때가 가장 알아야 하는
        /// 순간이다. 두 축을 한 소리로 겸하게 하면 둘 중 하나가 반드시 어긋난다.
        ///
        /// 실제로 깎인 값(`appliedDamage`)이 0이면 내지 않는다 — HP가 이미 바닥나 클램프된 잉여
        /// 피해와, 데미지 0으로 저작된 적(자폭병의 `Melee.Stat.AttackDamage`가 그렇다)이 여기 걸린다.
        /// 경고만 울리고 체력바는 그대로인 상태가 "맞았는데 안 깎인다"로 읽히는 것을 막는다.
        ///
        /// ⚠ **피해 종류를 보지 않는다.** 자폭·돌진·평타가 모두 같은 규칙을 탄다 — 임계를 넘겼는가만
        /// 본다. 예전에는 자폭을 건너뛰고 돌진에 디바운스 우회를 뒀는데, 그 둘은 「매 피격마다
        /// 울린다」는 전제 위의 보정이었다. 임계 방식에서는 큰 피해가 **자연히** 임계를 넘어 울리고
        /// (탱크 램 50 = 25%는 사실상 항상 넘는다) 작은 피해는 넘을 때만 울리므로 종류별 특례가
        /// 필요 없다. 그래서 그 목적으로 만들었던 `DamageKind`도 함께 걷어냈다.
        void PlayDamageWarning(float appliedDamage)
        {
            if (appliedDamage <= 0f)
                return;

            int reached = CountReachedThresholds();

            // 한 방에 임계 둘을 통과해도 **1회만** 울린다(탱크 램 50 = 25%가 그런 경우다).
            if (reached <= announcedThresholds)
                return;

            // ⚠ **겹침 방지 창에 막히면 장부를 올리지 않고 돌아간다 — 순서가 핵심이다.**
            // 예전에는 여기서 먼저 `announcedThresholds = reached`를 찍고 창을 검사했는데,
            // 그러면 막힌 통과가 **울리지도 않은 채 알린 것으로 기록돼 영영 사라졌다.**
            // 위험한 시나리오가 정확히 이것이다: 75% 경보 직후 0.5초 만에 25%·10%로 연쇄 하락하면
            // (탱크 램 50~90 한 방이면 임계 둘을 건너뛴다) **가장 절박한 경보가 조용히 씹힌다.**
            //
            // 장부를 미뤄 두면 다음 피해가 같은 통과를 다시 시도하므로, 창이 지나는 즉시 울린다 —
            // 최대 `warningMinInterval`만큼 늦을 뿐 잃지 않는다. 연쇄 하락 중에는 다음 피해가
            // 곧바로 오므로 실질 지연도 거의 없다. 피해가 멎었다면 위급 상황도 지난 것이라
            // 울리지 않는 편이 맞다.
            //
            // 리뷰가 제안한 「새 임계면 창을 무시하고 울린다」로 가지 않은 이유: 이 함수는
            // 같은 임계로 다시 오지 않으므로(위 가드) 그 조건이 곧 "항상 무시"가 되어 창이 사라진다.
            // 그러면 사이렌 두 벌이 겹쳐 볼륨이 배로 튄다(`PlaySfx`는 `PlayOneShot`이라 상한이 없고,
            // 현재 클립 peak가 -0.2dBFS라 두 벌이면 풀스케일에 닿는다).
            if (Time.unscaledTime - lastWarningTime < warningMinInterval)
                return;

            announcedThresholds = reached;
            lastWarningTime = Time.unscaledTime;

            Sfx.BaseDamaged();
        }

        /// 현재 HP 비율이 만족하는(= 그 아래로 내려간) 임계의 개수.
        int CountReachedThresholds()
        {
            if (warningHpThresholds == null || maxHp <= 0f)
            {
                return 0;
            }

            float ratio = currentHp / maxHp;
            int count = 0;

            for (int i = 0; i < warningHpThresholds.Length; i++)
            {
                if (ratio <= warningHpThresholds[i])
                {
                    count++;
                }
            }

            return count;
        }

        /// 경고 장부를 현재 HP에 맞춘다 — **울리지 않고** 통과 개수만 동기화한다.
        ///
        /// 세이브 복원에서 부르는 이유: HP 40%로 이어하기를 하면 75%·50% 임계가 이미 지나간
        /// 상태인데, 장부가 0이면 복원 직후 첫 피격에 그 둘을 한꺼번에 "새로 통과"한 것으로 보고
        /// 사이렌이 울린다. 증상이 "이어하기 하면 맞을 때 경보가 이상하게 난다"라 원인에서 멀다.
        ///
        /// ⚠ **본진 회복이 들어오면 이 함수를 회복 경로에서도 불러야 한다**(WL-206의 「낮 시작
        /// 본진 회복」이 구현되면). 부르지 않으면 회복 후 다시 내려갈 때 경보가 재무장되지 않아
        /// 조용히 지나간다. 장부 갱신을 한 함수로 묶어 둔 이유가 그것이다.
        void SyncWarningThresholds()
        {
            announcedThresholds = CountReachedThresholds();
        }

        void GameOver()
        {
            Debug.Log("Game Over - 본진이 파괴되었습니다");

            if (GameManager.Instance == null)
            {
                Debug.LogWarning("[PlayerBase] GameManager가 씬에 없어 게임오버가 통지되지 않았습니다.");
                return;
            }
            GameManager.Instance.TriggerGameOver();
        }

        /// <summary>
        /// 저장된 현재 HP를 절대값으로 복원한다.
        /// 피해 처리와 게임오버 판정은 발생시키지 않는다.
        /// </summary>
        /// <param name="hp">복원할 현재 HP.</param>
        /// <returns>HP 값이 유효하고 복원에 성공하면 true.</returns>
        public bool TryRestoreCurrentHp(float hp)
        {
            if (float.IsNaN(hp) ||float.IsInfinity(hp))
            {
                Debug.LogError($"[PlayerBase] 유효하지 않은 HP입니다: {hp}",this);

                return false;
            }

            if (hp <= 0f)
            {
                Debug.LogError($"[PlayerBase] 죽은 본진 HP는 복원할 수 없습니다: {hp}",this);

                return false;
            }

            if (hp > maxHp)
            {
                Debug.LogError($"[PlayerBase] 저장된 HP가 최대 HP를 초과합니다: 저장={hp}, 최대={maxHp}",this);

                return false;
            }

            currentHp = hp;

            // 복원된 HP 기준으로 경고 장부를 맞춘다 — 이미 지나간 임계를 다시 알리지 않게(§6.4).
            SyncWarningThresholds();

            OnHpChanged?.Invoke(currentHp,maxHp);

            return true;
        }

    }
}
