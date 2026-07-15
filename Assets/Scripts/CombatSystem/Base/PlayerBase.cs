using NorthLand.Core;
using UnityEngine;

namespace NorthLand.Combat
{
    public class PlayerBase : MonoBehaviour, IDamageable
    {
        [SerializeField] float maxHp = 1000f;

        float currentHp;

        void Awake()
        {
            currentHp = maxHp;
        }

        public Faction Faction => Faction.Player;
        public bool IsDead => currentHp <= 0f;

        public void TakeDamage(DamageInfo info)
        {
            if (IsDead) return;   // 이미 파괴됨 — 추가 피해·중복 판정 차단

            currentHp -= info.Amount;
            // Debug.Log($"{name} took {info.Amount} dmg, hp={currentHp}");   // 디버그용 — 전투 중 로그 스팸 방지 위해 비활성

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
