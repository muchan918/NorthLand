using NorthLand.Combat;
using NorthLand.Core;
using UnityEngine;

public enum MonsterState
{
    Idle,
    Move,
    Attack,
    Death
}

public class MonsterStateMachine : MonoBehaviour
{
    private IRouteMovementAgent routeMovement;

    [SerializeField] private MonsterAnimation monsterAnimation;


    [SerializeField] private float destroyDelay = 2f;

    private MonsterState currentState = MonsterState.Idle;

    public MonsterState CurrentState => currentState;

    private bool hasTarget;



    private void Awake()
    {
        routeMovement = GetComponentInChildren<IRouteMovementAgent>();

        if (monsterAnimation == null)
        {
            monsterAnimation = GetComponentInChildren<MonsterAnimation>();
        }

        // **MonsterAnimation 해석 지점은 여기 하나다.** 직렬화 필드를 우선하고 없을 때만 탐색하는데,
        // 예전에는 Enemy가 GetComponentInChildren으로 따로 한 번 더 찾았다 — 두 경로가 지금은 같은
        // 결과를 내지만 저작이 어긋나면(다른 가지를 가리키거나 비활성으로 시작하면) 한쪽만 null이
        // 되고, 그러면 공격 모션이 통째로 사라지면서 로그가 한 줄도 없었다.
        if (monsterAnimation == null)
        {
            Debug.LogWarning($"[{name}] MonsterAnimation을 찾지 못해 공격·이동 모션이 동작하지 않습니다.", this);
        }
    }

    /// 공격 스윙 1회를 지시하고 **타격까지 남은 시간(초)** 을 돌려준다(#452). 0이면 즉발 처리하라는 뜻이다.
    ///
    /// `Enemy`가 `MonsterAnimation`을 직접 들지 않고 이 창구를 경유하는 이유가 둘이다.
    ///  ① **애니메이터 기록 주체를 한 컴포넌트로 모은다.** `IsAttack`을 켜는 쪽(스윙 시작)과 끄는 쪽
    ///     (Idle·Move 진입, Attack 이탈)이 서로 다른 파일에 있으면, 다음에 상태 기계를 만지는 사람이
    ///     `EnterState(Attack)`에 모션을 다시 매달아 #452가 고친 무한 루프를 재발시킬 여지가 남는다.
    ///  ② **`MonsterAnimation` 참조 해석이 한 곳으로 모인다**(위 Awake 주석).
    ///
    /// 공격 간격은 `Enemy`만 안다(`EnemyAsset` 소유). 그래서 값은 인자로 받고 판단은 여기서 하지 않는다.
    public float RequestAttackSwing(float attackInterval)
    {
        return monsterAnimation != null ? monsterAnimation.PlaySwing(attackInterval) : 0f;
    }

    /// 진행 중인 스윙을 접는다(#452). 스턴·BT 소유권 진입·게임 종료에서 `Enemy`가 부른다 —
    /// 접지 않으면 스턴 중에 예약된 피해가 스턴이 풀린 뒤 그대로 들어간다(#164 구멍의 시간차 재발).
    public void CancelAttackSwing()
    {
        monsterAnimation?.SetAttackAnimation(false);
    }

    private void Update()
    {
        if (currentState == MonsterState.Death)
        {
            return;
        }

        GameManager gameManager = GameManager.Instance;

        if (gameManager != null && gameManager.Result != GameResult.Playing)
        {
            ChangeState(MonsterState.Idle);
            return;
        }

        // 스턴 검사가 hasTarget보다 먼저 온다 — 스턴 중에는 공격·걷기 어느 모션도 재생되지 않아야 한다.
        // 순서가 뒤집히면 본진에 붙어 때리던 몬스터가 스턴 중에도 Attack 상태로 남는다(Enemy가
        // SetHasTarget(false)를 내려주므로 지금은 가려지지만, hasTarget을 세우는 경로가 하나 늘면 조용히 재발한다).
        //
        // 전용 스턴 상태를 두지 않고 Idle로 묶는다 — MonsterState에 Stun이 없고, 추가하면 애니메이터
        // 작업이 따라온다. 스턴 중 몬스터는 제자리에 서 있는 모습이 된다.
        if (routeMovement != null && routeMovement.IsStunned)
        {
            ChangeState(MonsterState.Idle);
            return;
        }

        if (hasTarget)
        {
            ChangeState(MonsterState.Attack);
            return;
        }

        if (routeMovement != null && !routeMovement.IsStopped && routeMovement.HasRouteRemaining)
        {
            ChangeState(MonsterState.Move);
            return;
        }

        ChangeState(MonsterState.Idle);
    }

    public void SetHasTarget(bool value)
    {
        hasTarget = value;
    }


    //    디버깅용 코드 나중에 필요없음녀 삭제
    //    private void LateUpdate()
    //    {
    //        Keyboard keyboard = Keyboard.current;
    //        if (keyboard == null)
    //        {
    //            return;
    //        }

    //        if (keyboard.fKey.wasPressedThisFrame)
    //        {
    //            ChangeState(MonsterState.Move);
    //        }

    //        if (keyboard.gKey.wasPressedThisFrame)
    //        {
    //            ChangeState(currentState == MonsterState.Attack
    //                ? MonsterState.Move
    //                : MonsterState.Attack);
    //        }

    //        if (keyboard.hKey.wasPressedThisFrame)
    //        {
    //            ChangeState(MonsterState.Death);
    //        }
    //    }




    public void ChangeState(MonsterState nextState)
    {
        if (currentState == MonsterState.Death)
        {
            return;
        }

        if (currentState == nextState)
        {
            return;
        }

        ExitState(currentState);
        currentState = nextState;
        EnterState(currentState);
    }

    private void EnterState(MonsterState state)
    {
        switch (state)
        {
            case MonsterState.Idle:
                routeMovement?.SetMoveEnabled(false);
                monsterAnimation?.SetMoveAnimation(false);
                monsterAnimation?.SetAttackAnimation(false);
                break;
            case MonsterState.Move:
                monsterAnimation?.SetAttackAnimation(false);
                routeMovement?.SetMoveEnabled(true);
                monsterAnimation?.SetMoveAnimation(true);
                break;
            // Attack 상태는 「교전 중 = 이동 정지」만 의미한다. 공격 모션은 여기서 켜지 않는다(#452) —
            // IsAttack을 상태 진입에 래치하면 사거리 안에 있는 동안 공격 클립이 자기 길이대로 무한
            // 반복해 공격 간격과 어긋난다(파랑 그러미는 2.57바퀴에 한 번만 실제로 때렸다).
            // 스윙 1회의 시작·종료는 Enemy가 RequestAttackSwing을 거쳐 지시한다 —
            // 공격 간격(AttackInterval)을 아는 쪽이 Enemy 하나뿐이다.
            case MonsterState.Attack:
                routeMovement?.SetMoveEnabled(false);
                monsterAnimation?.SetMoveAnimation(false);
                break;
            case MonsterState.Death:
                if (routeMovement != null)
                {
                    routeMovement.IsStopped = true;
                    routeMovement.SetMoveEnabled(false);
                }
                monsterAnimation?.PlayDeathAnimation();
                Destroy(gameObject, destroyDelay);
                break;
        }
    }

    private void ExitState(MonsterState state)
    {
        if (state == MonsterState.Attack)
        {
            monsterAnimation?.SetAttackAnimation(false);
        }
    }
}
