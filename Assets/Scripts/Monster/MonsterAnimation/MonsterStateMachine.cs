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
            // 스윙 1회의 시작·종료는 Enemy가 MonsterAnimation.PlaySwing으로 직접 지시한다 —
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
