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
            currentHp -= info.Amount;
            // Debug.Log($"{name} took {info.Amount} dmg, hp={currentHp}");

            if (IsDead)
                GameOver();
        }

        void GameOver()
        {
            Debug.Log("Game Over - 본진이 파괴되었습니다");
            // TODO: 실제 게임오버 처리(씬 전환 / UI 등) 연결
        }
    }
}
