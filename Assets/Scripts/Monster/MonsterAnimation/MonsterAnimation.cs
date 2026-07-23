using UnityEngine;

public class MonsterAnimation : MonoBehaviour
{
    private static readonly int IsMoveHash =
        Animator.StringToHash("IsMove");

    private static readonly int IsAttackHash =
        Animator.StringToHash("IsAttack");

    private static readonly int IsDieHash =
        Animator.StringToHash("IsDie");

    [SerializeField] private Animator animator;

    private bool isDead;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (animator == null)
        {
            Debug.LogError(
                $"[{nameof(MonsterAnimation)}] Animator를 찾을 수 없습니다.",
                gameObject);
        }
    }

    public void SetMoveAnimation(bool isMoving)
    {
        if (animator == null || isDead)
        {
            return;
        }

        animator.SetBool(IsMoveHash, isMoving);

        if (isMoving)
        {
            animator.SetBool(IsAttackHash, false);
        }
    }

    public void SetAttackAnimation(bool isAttacking)
    {
        if (animator == null || isDead)
        {
            return;
        }

        if (isAttacking)
        {
            animator.SetBool(IsMoveHash, false);
        }


        animator.SetBool(IsAttackHash, isAttacking);
    }

    public void PlayDeathAnimation()
    {
        if (animator == null || isDead)
        {
            return;
        }

        isDead = true;

        animator.SetBool(IsMoveHash, false);
        animator.SetBool(IsAttackHash, false);
        animator.SetBool(IsDieHash, true);
    }
}
