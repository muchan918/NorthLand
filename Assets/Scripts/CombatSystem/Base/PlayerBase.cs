using System;
using NorthLand.Core;
using UnityEngine;

namespace NorthLand.Combat
{
    public class PlayerBase : MonoBehaviour, IDamageable
    {
        [SerializeField] float maxHp = 200f;

        float currentHp;

        // 성문(BaseGate)이 밤에 런타임 스폰되므로(MonsterSpawn.UpdateGate), 씬 싱글톤 +
        // 스폰 통지 이벤트로 UI가 "이미 있음"/"앞으로 생김" 두 경우를 한 경로로 처리하게 한다.
        // (TowerInfoUI/DayNightManager와 동일한 씬 싱글톤 계보)
        public static PlayerBase Instance { get; private set; }
        public static event Action<PlayerBase> OnBaseSpawned;

  
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

            currentHp -= info.Amount;
            // Debug.Log($"{name} took {info.Amount} dmg, hp={currentHp}");   // 디버그용 — 전투 중 로그 스팸 방지 위해 비활성
            OnHpChanged?.Invoke(currentHp, maxHp);

            if (IsDead)
                GameOver();
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
