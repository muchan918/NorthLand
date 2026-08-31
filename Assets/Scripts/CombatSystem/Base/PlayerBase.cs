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

        // ── 피격음 (#540 후속) ──────────────────────────────────────────────
        // **`SfxBank`에 넣지 않는다** — 뱅크의 범위는 "주인이 없는 공용 소리"이고
        // (`Docs/Core/AudioManager.md` §5.4) 이 소리의 주인은 본진 하나다. 타워 발사음을 각자의
        // SO가, 자폭 폭발음을 `EnemyAsset`이 드는 것과 같은 규칙이다.
        //
        // **구독 컴포넌트로 빼지 않았다.** 본진 피격은 모든 피해 경로(평타·자폭·보스)가 지나는
        // `TakeDamage` 한 곳에서 나야 하는 보편 소리라, 컴포넌트로 두면 프리팹에서 빠졌을 때
        // 에러 없이 조용히 무음이 된다 — 발사음을 `Tower.RaiseFired`에 둔 것과 같은 축이다(§6.3).
        [Header("피격음")]
        [Tooltip("본진이 피해를 받은 순간 1회 재생할 효과음. 비우면 소리 없이 맞는다. " +
                 "화면 밖·줌아웃(오쏘 160 이상)에서는 들리지 않는다(AudioManager.md §6.2).")]
        [SerializeField] AudioClip hitSfx;

        [Range(0f, 2f)]
        [Tooltip("피격음 재생 배율. SFX 채널 볼륨에 곱해진다. 임포트 설정에는 클립별 게인이 없어 " +
                 "(AudioManager.md §4.5) 클립 사이의 레벨 차는 여기서만 맞출 수 있다.")]
        [SerializeField] float hitSfxVolume = 1f;

        // 디바운스. 밤 후반에는 본진에 붙은 근접 몹이 여러 마리라 **같은 프레임에 피해가 여러 번**
        // 들어오고, 그대로 두면 한 번에 여러 보이스를 물어 소리가 뭉치면서 풀 상한(32)까지 갉는다.
        // 실시간 기준(`Time.unscaledTime`)인 것이 의도다 — 오디오 뭉침은 배속과 무관한 실시간
        // 현상이라, 2배속에서 창이 함께 늘어나면 그 배속에서만 소리가 성기어진다.
        [Min(0f)]
        [Tooltip("피격음이 다시 날 수 있기까지의 최소 실시간 간격(초). 0이면 매 피해마다 난다. " +
                 "돌진 피해에는 걸리지 않는다.")]
        [SerializeField] float hitSfxMinInterval = 0.15f;

        // 돌진 피격음. 평타와 **크기가 자릿수로 다른 사건**이라 소리를 가른다 — `tank`의 P1은
        // `speed × 3.75`에 `MinSpeed 10` 게이트라 최소 37.5, 본진 HP의 18.75%가 한 번에 날아간다.
        // 판별은 `DamageKind.Impact`가 하고 이 컴포넌트는 고르기만 한다(§6.4).
        [Tooltip("돌진(충돌) 피해를 받은 순간 재생할 효과음. 비우면 일반 피격음으로 대신한다 — " +
                 "미배선이 새 무음 경로를 만들지 않게 한 것이다. 파삭한 일반 피격음과 갈리도록 " +
                 "묵직한 저역이 좋고, 자폭 폭발음과는 음색이 겹치지 않아야 한다(§6.3).")]
        [SerializeField] AudioClip impactSfx;

        [Range(0f, 2f)]
        [Tooltip("돌진 피격음 재생 배율. SFX 채널 볼륨에 곱해진다. " +
                 "1.0을 넘긴 몫은 화면 중앙에서 잘린다 — 헤드룸이 부족하면 클립 자체를 정규화할 것.")]
        [SerializeField] float impactSfxVolume = 1f;

        float lastHitSfxTime = float.NegativeInfinity;

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
            WarnIfHitSfxMissing();
            OnHpChanged?.Invoke(currentHp, maxHp);
            OnBaseSpawned?.Invoke(this);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// 피격음 클립 미배선을 부팅 시점에 1회 알린다(WL-040).
        ///
        /// **이 실패는 완전히 조용하다** — 배선 정본이 `Gate_02.prefab`(`Assets/Imported`, 별도 저장소)이라
        /// 메인 저장소만 받은 환경에서는 필드가 빈 채로 오고, `PlayHitSfx`가 `null`을 조용히 넘긴다
        /// (미저작 상태에서 씬이 깨지지 않게 한 것이 의도다). 컴파일·콘솔 신호가 0이라 증상이
        /// "내 환경만 소리가 이상한가"로 끝나고 원인이 저장소 동기화라는 단서가 없다.
        /// `BootWarmup.WarnIfTowerSfxMissing`(#540)이 타워 클립에 건 것과 같은 계약이다.
        ///
        /// ⚠ **재생 시점이 아니라 `Awake`인 것이 의도다.** 본진 피격은 밤이 한참 진행돼야 처음
        /// 일어나므로 `PlayHitSfx`에 걸면 발견이 그만큼 늦다 — 같은 처방을 따른
        /// `CursorSet.WarnIfArtMissing`(#517)도 부팅 시점 검사다. `Awake`는 인스턴스당 1회라
        /// 별도의 중복 게이트도 필요 없다.
        void WarnIfHitSfxMissing()
        {
            if (hitSfx == null)
            {
                Debug.LogWarning($"[PlayerBase] {name}: hitSfx가 비어 있어 본진 피격음이 나지 않습니다 — " +
                                 "Assets/Imported 저장소 동기화를 확인하세요.", this);
            }
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
            PlayHitSfx(info, appliedDamage);
            // Debug.Log($"{name} took {info.Amount} dmg, hp={currentHp}");   // 디버그용 — 전투 중 로그 스팸 방지 위해 비활성
            OnHpChanged?.Invoke(currentHp, maxHp);

            if (IsDead)
                GameOver();
        }

        /// 본진 피격음(#540 후속). 자폭 폭발음과 같은 위치 기반 풀을 탄다(`AudioManager.md` §6.2) —
        /// **화면 밖과 오쏘 160 이상 줌아웃에서는 무음이다.**
        ///
        /// 우선순위 `High`: 본진이 깎이는 것은 런의 패배 조건에 직결되는 사건이라, 상한에 닿았을 때
        /// 타워 전투음(`Low`)에 밀려 잘리면 안 된다.
        ///
        /// 실제로 깎인 값(`appliedDamage`)이 0이면 내지 않는다 — HP가 이미 바닥나 클램프된 잉여
        /// 피해와, 데미지 0으로 저작된 적(자폭병의 `Melee.Stat.AttackDamage`가 그렇다)이 여기 걸린다.
        /// 소리만 나고 체력바는 그대로인 상태가 "맞았는데 안 깎인다"로 읽히는 것을 막는다.
        ///
        /// ⚠ **자폭 피해는 건너뛴다.** 자폭병은 터지는 자리에서 자기 폭발음을 이미 내므로
        /// (`EnemyAsset.SelfDestruct.ExplosionSfx`) 피격음을 겹치면 같은 프레임·같은 지점에서
        /// 두 소리가 뭉친다. 가르는 축은 `DamageKind`이고 **가해자만 그것을 안다** — 여기서
        /// `Source is Enemy`를 캐스팅해 자폭병인지 되묻는 방식은 쓰지 않는다. 그 술어는
        /// "자폭 가능한 적"이지 "이번 피해가 자폭"이 아니라서, 자폭병이 평타도 하게 되는 날
        /// 조용히 틀린다.
        /// ⚠ **돌진(`Impact`)은 자기 클립을 쓰고 디바운스를 뚫는다.** 평타와 크기가 자릿수로 다른
        /// 단발 대타격이라(최소 37.5 = 본진 HP의 18.75%), 직전 0.15초에 잡몹 평타가 하나 들어와
        /// 있었다는 이유로 통째로 사라지면 안 된다 — 재현이 타이밍 의존이라 버그로 인지되지 않고
        /// "가끔 소리가 안 난다"로 남는 종류다. **"큰 피해"를 피해 비율로 추정하지 않는 것이
        /// 의도다** — 사건 종류로 직접 아는 편이 임계값 저작 없이도 정확하다.
        ///
        /// `impactSfx`가 비면 일반 피격음으로 폴백한다. 여기서 무음으로 두면 클립 미배선이
        /// **새 무음 경로를 하나 더** 만든다(`WarnIfHitSfxMissing`이 막으려는 것과 같은 실패).
        void PlayHitSfx(DamageInfo info, float appliedDamage)
        {
            if (appliedDamage <= 0f)
                return;

            if (info.Kind == DamageKind.SelfDestruct)
                return;

            // ⚠ 디바운스 우회와 클립 선택은 **따로 판정한다.** 묶어 두면 `impactSfx` 미배선일 때
            // 우회까지 함께 꺼져, 폴백으로 소리는 나는데 창에 걸려 사라지는 조합이 생긴다 —
            // 클립이 없어도 사건은 여전히 대타격이다.
            bool isImpact = info.Kind == DamageKind.Impact;
            bool useImpactClip = isImpact && impactSfx != null;

            AudioClip clip = useImpactClip ? impactSfx : hitSfx;

            // ⚠ **널 검사는 「쓸 클립」에 건다 — 특정 필드에 걸지 않는다.** 예전에는 함수 맨 위에서
            // `hitSfx == null`로 막았는데, 그러면 `impactSfx`만 배선한 조합이 함수에 들어오지도
            // 못해 **돌진음까지 함께 무음이 됐다**(리뷰 지적). 두 필드 중 하나가 비어 있는 것은
            // 에러가 아니라 저작자가 고를 수 있는 상태이므로(각 툴팁 참조), 규칙은
            // 「쓸 클립이 있으면 울린다」로 두 방향에 대칭이어야 한다.
            //
            // 디바운스 도장보다 먼저 검사하는 것도 의도다 — 울리지 못한 요청이 창을 소모하면
            // 뒤따르는 유효한 피해가 이유 없이 잘린다.
            if (clip == null)
                return;

            if (!isImpact && Time.unscaledTime - lastHitSfxTime < hitSfxMinInterval)
                return;

            // 우회로 울린 뒤에도 도장은 찍는다 — 돌진 직후 몰려드는 평타가 그 위에 겹치지 않게.
            lastHitSfxTime = Time.unscaledTime;

            CombatSfx.Play(
                clip,
                HitPosition.position,
                volumeScale: useImpactClip ? impactSfxVolume : hitSfxVolume,
                priority: CombatSfxPriority.High);
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
            OnHpChanged?.Invoke(currentHp,maxHp);

            return true;
        }

    }
}
