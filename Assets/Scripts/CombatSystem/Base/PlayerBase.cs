using System;
using NorthLand.Core;
using UnityEngine;

namespace NorthLand.Combat
{
    public class PlayerBase : MonoBehaviour, IDamageable
    {
        [SerializeField] float maxHp = 100f;

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
        public event Action<float, float> OnHpChanged;

        public void TakeDamage(DamageInfo info)
        {
            if (IsDead) return;   // 이미 파괴됨 — 추가 피해·중복 판정 차단

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
     
    }
}
