using UnityEngine;
using UnityEngine.InputSystem;

public class AnimationTest : MonoBehaviour
{
    [SerializeField]
    private MonsterStateMachine monsterStateMachine;

    private void LateUpdate()
    {
        if (monsterStateMachine == null)
        {
            return;
        }

        if (monsterStateMachine.CurrentState == MonsterState.Death)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.fKey.wasPressedThisFrame)
        {
            monsterStateMachine.ChangeState(MonsterState.Move);
        }

        if (keyboard.gKey.wasPressedThisFrame)
        {
            MonsterState nextState =
                monsterStateMachine.CurrentState == MonsterState.Attack
                    ? MonsterState.Move
                    : MonsterState.Attack;

            monsterStateMachine.ChangeState(nextState);
        }

        if (keyboard.hKey.wasPressedThisFrame)
        {
            monsterStateMachine.ChangeState(MonsterState.Death);
        }
    }
}