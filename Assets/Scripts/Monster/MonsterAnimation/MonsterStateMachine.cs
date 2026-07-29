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
            case MonsterState.Attack:
                routeMovement?.SetMoveEnabled(false);
                monsterAnimation?.SetMoveAnimation(false);
                monsterAnimation?.SetAttackAnimation(true);
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
