using UnityEngine;

public class MonsterAnimation : MonoBehaviour
{
    private static readonly int IsMoveHash =
        Animator.StringToHash("IsMove");

    private static readonly int IsAttackHash =
        Animator.StringToHash("IsAttack");

    private static readonly int IsDieHash =
        Animator.StringToHash("IsDie");

    // 공격 상태의 재생속도 배수(#452). 컨트롤러의 공격 상태에 Speed Parameter로 연결한다.
    private static readonly int AttackCadenceHash =
        Animator.StringToHash("AttackCadence");

    // 스윙 끝을 이 시간보다 앞으로 당기지는 않는다. 클립이 전이 시간보다 짧을 때 스윙이
    // 0초로 접히는 것을 막는 하한이다.
    private const float MinSwingHold = 0.05f;

    [SerializeField] private Animator animator;

    [Header("공격 스윙(#452)")]

    // 클립 길이를 직렬화하지 않고 클립 자체를 참조하는 이유: 길이를 숫자로 박아두면 클립을
    // 갈아끼웠을 때 값이 조용히 어긋난다. `AnimationClip.length`는 상태 재생속도와 무관한
    // 원본 길이라 안정적이다.
    //
    // 상태 진입 후 `GetCurrentAnimatorClipInfo`로 읽는 방법도 있지만 그러면 **스윙을 시작하는
    // 시점에는 길이를 모른다** — 재생속도와 타격 시점을 둘 다 스윙 시작 전에 확정해야 하므로
    // 여기서 미리 참조를 들고 있어야 한다.
    [Tooltip("공격 상태가 재생하는 클립. 길이를 읽어 공격 간격에 맞춰 재생속도를 정한다. " +
             "비워두면 스윙 제어가 꺼지고 예전처럼 루프 재생 + 즉발 피해가 된다.")]
    [SerializeField] private AnimationClip attackClip;

    [Tooltip("클립 안에서 실제 타격이 닿는 지점(0=시작, 1=끝). Enemy가 이 시점에 피해를 넣는다.")]
    [Range(0f, 1f)]
    [SerializeField] private float hitNormalizedTime = 0.5f;

    [Tooltip("재생속도 상한. 공격 간격이 클립보다 훨씬 짧을 때 스윙이 경련처럼 보이는 것을 막는다.")]
    [SerializeField] private float maxCadence = 2f;

    // 공격 상태를 나가는 전이 시간. 컨트롤러의 `공격 → Idle` 전이 duration과 맞춘다.
    //
    // 이 값만큼 IsAttack을 **미리** 내려야 한다. 클립 끝에서 내리면 페이드아웃 0.25초 동안
    // loop 클립이 두 번째 스윙을 시작해 앞부분이 겹쳐 들어온다 — 휘두르다 되감기는 것처럼 보인다.
    // 미리 내리면 페이드가 마무리 동작(follow-through)에 얹혀 클립 끝에서 정확히 끝난다.
    //
    // 런타임에 읽지 못해 직렬화한다: `Animator`는 **진행 중인** 전이 정보만 노출하고
    // (`GetAnimatorTransitionInfo`), 아직 시작하지 않은 전이의 duration을 알려주는 API가 없다.
    // 컨트롤러 값과 어긋나면 다시 겹침이 보이므로 함께 고칠 것.
    [Tooltip("컨트롤러의 「공격 → Idle」 전이 duration과 같은 값을 넣는다. 현재 컨트롤러는 0.25.")]
    [SerializeField] private float attackExitBlend = 0.25f;

    private bool isDead;

    // 남은 스윙 시간. 0이 되면 IsAttack을 내려 Idle로 돌아간다(= 다음 공격까지 뜸 들이는 구간).
    private float swingRemaining;

    // 폴백 경고를 인스턴스당 1회로 묶는다. 공격마다 나면 웨이브 후반에 콘솔이 잠긴다.
    private bool warnedSwingUnavailable;

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

    /// 스윙 단위 제어가 가능한가. false면 호출부는 예전 경로(루프 재생 + 즉발 피해)를 쓴다 —
    /// 공격 클립을 지정하지 않은 프리팹(자폭병 등)이 조용히 공격을 멈추지 않게 하는 장치다.
    public bool CanScheduleSwing =>
        animator != null && attackClip != null && attackClip.length > 0f;

    /// 스윙 1회를 시작하고 **타격까지 남은 시간(초)** 을 돌려준다. 0이면 즉발 처리하라는 뜻이다.
    ///
    /// 재생속도를 `클립 길이 / 스윙 시간`으로 맞춰 **애니메이션 1주기 = 공격 1회**를 성립시킨다.
    /// 공격 간격이 클립보다 길면 배속을 낮추는 대신 원속도로 한 번만 재생하고 남는 시간은 Idle로
    /// 둔다 — 3초 간격을 0.39배속으로 늘리면 슬로모션으로 휘두르는 것처럼 보인다.
    public float PlaySwing(float attackInterval)
    {
        if (animator == null || isDead)
        {
            return 0f;
        }

        if (!CanScheduleSwing || attackInterval <= 0f)
        {
            WarnSwingUnavailableOnce();

            SetAttackAnimation(true);
            return 0f;
        }

        float clipLength = attackClip.length;

        // 하한 1 — 간격이 클립보다 길 때 배속을 1 아래로 내리지 않는다(위 주석의 슬로모션 회피).
        float cadence = Mathf.Clamp(clipLength / attackInterval, 1f, maxCadence);
        float swingDuration = clipLength / cadence;

        animator.SetFloat(AttackCadenceHash, cadence);

        // IsAttack은 SetAttackAnimation만 쓴다 — 여기서 SetBool을 직접 부르면 스윙 장부와
        // 애니메이터를 한 자리에서 묶어 두는 불변식이 선언만 남는다(리뷰 지적).
        SetAttackAnimation(true);

        // Idle에 머무는 시간이 전이 왕복(나가기 + 들어오기)보다 짧으면, 들어갔다 나오는 두 페이드가
        // 서로 겹쳐 스윙만 뭉개진다. 그 구간은 연속 루프가 정답이므로 타이머를 걸지 않고 켠 채로 둔다
        // (배속은 이미 간격에 맞춰져 있어 애니메이션 1주기 = 공격 1회는 그대로 성립한다).
        float idleGap = attackInterval - swingDuration;

        swingRemaining = idleGap > attackExitBlend * 2f
            ? Mathf.Max(swingDuration - attackExitBlend, MinSwingHold)
            : 0f;

        return swingDuration * hitNormalizedTime;
    }

    /// 스윙 단위 제어가 불가능해 **예전 거동(루프 재생 + 즉발 피해)으로 되돌아갔다**는 사실을 드러낸다.
    ///
    /// 조용히 폴백하면 안 되는 이유: 공격 클립은 `Assets/Imported`(별도 저장소)의 프리팹에 배선되므로,
    /// 그 저장소를 동기화하지 않은 환경이 **정확히 이 상태**가 된다. 증상이 "받았는데 아무것도
    /// 안 바뀌었다"라 원인에서 멀고, 콘솔 한 줄이면 즉시 갈린다.
    private void WarnSwingUnavailableOnce()
    {
        if (warnedSwingUnavailable)
        {
            return;
        }

        warnedSwingUnavailable = true;

        Debug.LogWarning(
            $"[{name}] 공격 클립이 없어 공격 모션을 공격속도에 맞추지 못합니다 — 루프 재생 + 즉발 피해로 " +
            $"되돌아갑니다. 프리팹의 {nameof(MonsterAnimation)}.attackClip을 확인하세요" +
            "(Assets/Imported 저장소 미동기화가 가장 흔한 원인).",
            gameObject);
    }

    private void Update()
    {
        if (swingRemaining <= 0f)
        {
            return;
        }

        swingRemaining -= Time.deltaTime;

        if (swingRemaining <= 0f)
        {
            SetAttackAnimation(false);
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
            SetAttackAnimation(false);
        }
    }

    /// IsAttack의 **유일한 기록 지점**. false를 쓸 때 스윙 장부까지 함께 접는 것이 핵심이다 —
    /// 애니메이터만 내리고 타이머를 남기면 Update가 나중에 한 번 더 내려 이미 시작된 다음
    /// 스윙을 잘라먹는다.
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
        else
        {
            swingRemaining = 0f;
        }

        animator.SetBool(IsAttackHash, isAttacking);
    }

    public void PlayDeathAnimation()
    {
        if (animator == null || isDead)
        {
            return;
        }

        swingRemaining = 0f;
        isDead = true;

        animator.SetBool(IsMoveHash, false);
        animator.SetBool(IsAttackHash, false);
        animator.SetBool(IsDieHash, true);
    }
}
