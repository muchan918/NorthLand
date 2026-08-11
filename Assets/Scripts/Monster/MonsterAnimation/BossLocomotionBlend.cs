using UnityEngine;

// 실효 이동속도를 애니메이터에 흘려 걷기 → 달리기 → 전력질주를 자동으로 섞는다.
//
// **왜 Bool 플래그가 아니라 속도인가.** 돌진 모션을 `IsCharging` 같은 플래그로 켜면 그래프가
// 켜고 끄는 책임을 지고, 한 군데만 틀려도(값을 반대로 넣거나 끄는 배선을 빠뜨리거나) 보스가
// 내내 전력질주로 걸어 다닌다. 실제로 그렇게 됐었다. 속도를 그대로 흘리면 그래프가
// 관여할 게 없고 — 돌진은 속도를 올리므로 모션이 저절로 따라온다.
//
// `MoveSpeed`는 블렌드 트리의 임계값과 같은 단위(월드 유닛/초)다. 임계값 자체는 Animator
// 창의 블렌드 트리에서 authoring한다 — 밸런싱하는 사람이 코드를 안 열어도 되게.
//
// `MoveCadence`는 걷기 임계값 아래에서만 재생속도를 낮춘다. 방어 태세 크롤(기본의 0.24배)에서
// 걷기 클립이 제 속도로 돌면 발이 심하게 미끄러지기 때문이다. 임계값 위는 블렌드 트리가
// 담당하므로 1로 둔다.
//
// 보스 프리팹 루트에 붙인다. `EnemyAgent`와 같은 오브젝트여야 한다.
public class BossLocomotionBlend : MonoBehaviour
{
    private static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");
    private static readonly int MoveCadenceHash = Animator.StringToHash("MoveCadence");

    [SerializeField] private Animator animator;
    [SerializeField] private EnemyAgent agent;

    [Tooltip("이 속도 아래에서는 걷기 클립의 재생속도를 낮춰 발 미끄러짐을 줄인다. " +
             "블렌드 트리의 걷기 임계값과 같은 값으로 두는 것이 기본이다.")]
    [SerializeField] private float walkSpeed = 4.8f;

    [Tooltip("재생속도 하한. 0에 가까우면 크롤 중 애니메이션이 멎어 보인다.")]
    [SerializeField] private float minCadence = 0.35f;

    [Tooltip("속도 변화를 부드럽게 만드는 시간(초). 0이면 즉시 반영한다. " +
             "돌진 가속이 매 프레임 올라가므로 약간의 감쇠가 있어야 클립이 튀지 않는다.")]
    [SerializeField] private float smoothTime = 0.15f;

    private float smoothedSpeed;
    private float smoothVelocity;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (agent == null)
        {
            agent = GetComponentInChildren<EnemyAgent>();
        }

        // 조용한 무동작을 막는다 — 참조가 빠지면 보스가 걷기 클립만 재생하며 미끄러진다.
        if (animator == null || agent == null)
        {
            Debug.LogWarning($"[{name}] Animator 또는 EnemyAgent를 찾지 못해 이동 블렌드를 끕니다.", this);
            enabled = false;
            return;
        }

        smoothedSpeed = agent.EffectiveMoveSpeed;
    }

    private void Update()
    {
        // 패턴 속도 배수와 감속 디버프까지 반영된 최종 속도다. 배수를 읽으면 감속 타워로
        // 돌진을 늦춰도 모션은 전력질주로 남는다.
        float target = agent.EffectiveMoveSpeed;

        smoothedSpeed = smoothTime > 0f
            ? Mathf.SmoothDamp(smoothedSpeed, target, ref smoothVelocity, smoothTime)
            : target;

        animator.SetFloat(MoveSpeedHash, smoothedSpeed);

        float cadence = walkSpeed > 0f && smoothedSpeed < walkSpeed
            ? Mathf.Max(smoothedSpeed / walkSpeed, minCadence)
            : 1f;

        animator.SetFloat(MoveCadenceHash, cadence);
    }
}
