using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// 패턴 게이트(#234). 해당 Key가 한 번도 쓰이지 않았거나 마지막 사용 후 쿨다운이 지났으면 참.
// CooldownSeconds < 0이면 1회 한정 — 한 번 쓰면 이후 영구 거짓이다(P1 본진 돌진의 래치).
//
// 기록은 EnemyMarkPatternUsedAction이 남긴다. 두 노드가 짝을 이루므로 Key를 반드시 일치시켜야 한다.
// 조건 노드가 스스로 기록하지 않는 이유: Selector가 조건만 평가하고 시퀀스를 실행하지 않는 경우가
// 있어(상위 컴포지트 중단) 조건 평가 = 사용으로 치면 게이트가 헛돈다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[Condition(
    name: "Enemy Pattern Gate",
    description: "패턴 Key가 미사용이거나 쿨다운이 지났으면 참. 쿨다운이 음수면 1회 한정.",
    story: "[Agent] pattern [Key] is ready with cooldown [CooldownSeconds]",
    category: "Conditions/Enemy",
    id: "c774e4f9566549ea92ebc82ad0427c19")]
public partial class EnemyPatternGateCondition : Condition
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;

    // 패턴 식별자. EnemyMarkPatternUsedAction의 Key와 같아야 한다.
    [SerializeReference] public BlackboardVariable<string> Key;

    // 재발동까지 필요한 시간(초). 음수면 1회 한정, 0이면 제한 없음.
    [SerializeReference] public BlackboardVariable<float> CooldownSeconds;

    // 0 경고를 노드 인스턴스당 1회만 남기기 위한 래치. 조건 노드는 매 틱 평가되므로
    // 래치가 없으면 콘솔이 잠긴다.
    private bool warnedNoCooldown;

    public override bool IsTrue()
    {
        EnemyAgent agent = Agent?.Value;

        if (agent == null)
        {
            return false;
        }

        float cooldown = CooldownSeconds != null ? CooldownSeconds.Value : 0f;

        // 0은 "제한 없음"이라 게이트가 무의미해진다. 현재 설계에 0을 쓰는 패턴이 없고
        // (P1은 1회 한정=음수, P3는 양수 쿨다운) Blackboard 변수를 연결하지 않았을 때의
        // 기본값이 하필 0이라, 미설정을 조용히 통과시키면 패턴이 매 사이클 발동한다.
        if (cooldown == 0f && !warnedNoCooldown)
        {
            warnedNoCooldown = true;

            Debug.LogWarning($"[{agent.name}] 패턴 게이트 '{(Key != null ? Key.Value : null)}'의 " +
                "CooldownSeconds가 0이라 제한 없이 통과합니다. " +
                "1회 한정은 음수, 쿨다운은 양수로 설정하세요. Blackboard 변수 연결 누락일 수 있습니다.", agent);
        }

        return agent.IsPatternReady(Key != null ? Key.Value : null, cooldown);
    }
}
