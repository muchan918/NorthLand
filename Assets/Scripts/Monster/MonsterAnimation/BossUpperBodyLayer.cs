using UnityEngine;

// 상체 전용 애니메이션 레이어의 weight를 레이어 상태에 따라 자동으로 켜고 끈다(#235).
//
// **왜 필요한가.** Override 레이어는 weight가 1이면 클립이 없는 Empty 상태에서도 마스크 범위를
// 점유한다. Write Defaults를 꺼도 마찬가지다 — 기본 포즈를 쓰는 대신 그 자리에 얼어붙을 뿐이다.
// 실측에서 걷기 중 팔 스윙이 완전히 멎었고(12프레임 변화 0.00도) weight 0일 때와 38도가 어긋났다.
// 그래서 레이어는 weight 0으로 놓고 상체 패턴 상태에 들어간 동안에만 켠다.
//
// **왜 BT가 아니라 여기서 하는가.** `EnemyPlayAnimationAction`은 `SetTrigger`만 할 수 있어
// weight를 직접 다루지 못한다. 켜기/끄기를 그래프에 쌍으로 배선하면 한쪽을 빠뜨렸을 때
// 상체가 영구히 고착되고, 그 증상은 "패턴이 끝났는데 팔을 든 채로 걷는다"라 원인 추적이 어렵다.
// 레이어의 현재 상태를 보고 스스로 판단하면 배선 실수 자체가 성립하지 않는다.
//
// 상체 패턴을 쓰는 보스 프리팹에 붙인다. 잡몹은 레이어가 하나뿐이라 붙일 필요가 없다.
public class BossUpperBodyLayer : MonoBehaviour
{
    [SerializeField] private Animator animator;

    [Tooltip("상체 패턴 레이어의 인덱스. Boss_Alien_01 컨트롤러 기준 1(UpperBody).")]
    [SerializeField] private int layerIndex = 1;

    [Tooltip("이 이름의 상태에 있는 동안은 레이어를 끈다 — 상체를 Base 레이어에 돌려준다.")]
    [SerializeField] private string idleStateName = "Empty";

    [Tooltip("weight가 0과 1 사이를 오가는 데 걸리는 시간(초). 0이면 즉시 전환한다.")]
    [SerializeField] private float fadeSeconds = 0.2f;

    private int idleStateHash;
    private float weight;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        idleStateHash = Animator.StringToHash(idleStateName);

        // 조용한 무동작을 막는다 — 레이어가 없으면 상체 패턴은 트리거만 소비되고 아무것도 안 보인다.
        if (animator == null)
        {
            Debug.LogWarning($"[{name}] Animator를 찾지 못해 상체 레이어 제어를 끕니다.", this);
            enabled = false;
            return;
        }

        if (animator.layerCount <= layerIndex)
        {
            Debug.LogWarning($"[{name}] AnimatorController에 레이어 {layerIndex}가 없어 " +
                "상체 레이어 제어를 끕니다. 상체 마스크 레이어가 있는 컨트롤러인지 확인하세요.", this);
            enabled = false;
            return;
        }

        animator.SetLayerWeight(layerIndex, 0f);
    }

    private void Update()
    {
        // 전이 중에는 목적지 상태를 본다. Empty에서 빠져나오는 첫 프레임부터 weight를 올려야
        // 상태 전이 블렌드와 weight 페이드가 같이 진행되어 팔이 튀지 않는다.
        AnimatorStateInfo info = animator.IsInTransition(layerIndex)
            ? animator.GetNextAnimatorStateInfo(layerIndex)
            : animator.GetCurrentAnimatorStateInfo(layerIndex);

        float target = info.shortNameHash != idleStateHash ? 1f : 0f;

        if (Mathf.Approximately(weight, target))
        {
            return;
        }

        weight = fadeSeconds > 0f
            ? Mathf.MoveTowards(weight, target, Time.deltaTime / fadeSeconds)
            : target;

        animator.SetLayerWeight(layerIndex, weight);
    }
}
