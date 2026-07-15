using UnityEngine;

public class MonsterAnimation : MonoBehaviour
{
    private static readonly int IsMoveHash =
        Animator.StringToHash("IsMove");

    private static readonly int IsAttackHash =
        Animator.StringToHash("IsAttack");

    private static readonly int IsDieHash =
        Animator.StringToHash("IsDie");


    [SerializeField]
    private MonsterMove monsterMove;




    [SerializeField]
    private Animator animator;

    private bool isDead;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (monsterMove == null)
        {
            monsterMove = GetComponentInParent<MonsterMove>();
        }

        if (animator == null)
        {
            Debug.LogError(
                $"[{nameof(MonsterAnimation)}] Animator를 찾을 수 없습니다.",
                gameObject);
        }
    }

#if UNITY_EDITOR
    private void Update()
    {
        if (animator == null)
            return;

        // F: 이동 애니메이션 켜기/끄기
        if (Input.GetKeyDown(KeyCode.F))
        {
           
            bool nextMoveState = !animator.GetBool(IsMoveHash);
            SetAttackAnimation(false);
            SetMoveAnimation(nextMoveState);
        }

        // G: 공격 애니메이션 켜기/끄기
        if (Input.GetKeyDown(KeyCode.G))
        {
            bool nextAttackState = !animator.GetBool(IsAttackHash);
            SetAttackAnimation(nextAttackState);
        }

        // H: 사망 애니메이션 실행
        if (Input.GetKeyDown(KeyCode.H))
        {
            PlayDeathAnimation();

        }
    }
#endif

    public void SetMoveAnimation(bool isMoving)
    {
        if (animator == null || isDead)
            return;

        animator.SetBool(IsMoveHash, isMoving);

        if (isMoving)
        {
            animator.SetBool(IsAttackHash, false);
        }
    }

    public void SetAttackAnimation(bool isAttacking)
    {
        if (animator == null || isDead)
            return;

        monsterMove?.SetMoveEnabled(!isAttacking);

        if (isAttacking)
        {
            animator.SetBool(IsMoveHash, false);
        }

        animator.SetBool(IsAttackHash, isAttacking);
    }

    public void PlayDeathAnimation()
    {
        if (animator == null || isDead)
            return;

        isDead = true;


        monsterMove?.SetMoveEnabled(false);

        animator.SetBool(IsMoveHash, false);
        animator.SetBool(IsAttackHash, false);

        animator.SetBool(IsDieHash, true);
    }
}