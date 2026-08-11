using System.Collections.Generic;
using NorthLand.Combat;
using UnityEngine;

// 공격 애니메이션 1회 재생 시간을 실제 공격 간격(`Enemy.AttackInterval`)에 맞춘다.
//
// **왜 필요한가.** 공격 상태는 클립이 loop=false라 자기 전이(exit time 1)로 반복시키는데,
// 클립 길이(1.20초)와 공격 간격(2.5초)이 다르면 한 번 때리는 사이에 애니메이션이 두 번 돌아
// "휘두르는데 피해가 안 들어가는" 구간이 생긴다. 재생속도를 `클립 길이 / 공격 간격`으로 낮추면
// 한 바퀴가 정확히 한 번의 공격이 된다.
//
// 클립 길이를 직렬화 필드로 두지 않고 런타임에 읽는 이유: 클립을 갈아끼웠을 때 값이 조용히
// 어긋나기 때문이다. `AnimationClip.length`는 상태의 재생속도와 무관한 원본 길이라 안정적이다.
//
// 보스 프리팹 루트에 붙인다. `Enemy`와 같은 오브젝트여야 한다.
public class BossAttackCadence : MonoBehaviour
{
    private static readonly int AttackCadenceHash = Animator.StringToHash("AttackCadence");

    [SerializeField] private Animator animator;
    [SerializeField] private Enemy enemy;

    [Tooltip("공격 상태의 이름. 이 상태에 있을 때만 클립 길이를 읽는다.")]
    [SerializeField] private string attackStateName = "Attack";

    [Tooltip("재생속도 하한·상한. 공격 간격이 클립보다 훨씬 길거나 짧을 때 극단값을 막는다.")]
    [SerializeField] private float minCadence = 0.2f;
    [SerializeField] private float maxCadence = 2f;

    private int attackStateHash;
    private float cadence = 1f;

    // 배열 반환 오버로드는 호출마다 새로 할당한다. 공격 상태에 있는 동안 매 프레임 도는
    // 자리라 List를 재사용하는 오버로드를 쓴다 — 보스 1체면 체감 없지만 잡몹까지 번지면
    // 프레임마다 쓰레기가 쌓인다.
    private readonly List<AnimatorClipInfo> clipBuffer = new List<AnimatorClipInfo>();

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (enemy == null)
        {
            enemy = GetComponentInParent<Enemy>();
        }

        attackStateHash = Animator.StringToHash(attackStateName);

        // 조용한 무동작을 막는다 — 참조가 빠지면 공격 모션이 간격과 계속 어긋난다.
        if (animator == null || enemy == null)
        {
            Debug.LogWarning($"[{name}] Animator 또는 Enemy를 찾지 못해 공격 모션 속도 보정을 끕니다.", this);
            enabled = false;
            return;
        }

        animator.SetFloat(AttackCadenceHash, cadence);
    }

    private void Update()
    {
        // 전이 중에는 이전 상태의 클립이 읽히므로 배수를 갱신하지 않는다.
        if (animator.IsInTransition(0))
        {
            return;
        }

        if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash != attackStateHash)
        {
            return;
        }

        animator.GetCurrentAnimatorClipInfo(0, clipBuffer);

        if (clipBuffer.Count == 0)
        {
            return;
        }

        float clipLength = clipBuffer[0].clip.length;
        float interval = enemy.AttackInterval;

        // 간격이 0이면(스탯 미설정) 보정할 기준이 없다. 원래 속도로 둔다.
        float target = interval > 0f && clipLength > 0f
            ? Mathf.Clamp(clipLength / interval, minCadence, maxCadence)
            : 1f;

        if (!Mathf.Approximately(cadence, target))
        {
            cadence = target;
            animator.SetFloat(AttackCadenceHash, cadence);
        }
    }
}
