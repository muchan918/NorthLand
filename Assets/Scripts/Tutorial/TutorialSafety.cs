using NorthLand.Combat;
using UnityEngine;

// 튜토리얼 씬에서 본진이 파괴되지 않게 지킨다.
// 처음부터 무적으로 두지 않는 이유: "적이 성에 도달하면 피해를 입는다"는 것 자체가
// 가르쳐야 할 내용이다. HP가 실제로 줄어드는 것을 보여주다가, 위험 수위에서만 무적으로 전환한다.
//
// 이 컴포넌트가 켜져 있는 것 자체가 스위치다 — TutorialController를 참조하지 않는다.
// 참조하면 '튜토리얼 진행 상태'와 '보호 구간'을 동기화하는 문제가 새로 생기는데,
// 실제로 필요한 것은 "이 씬에서는 죽지 않는다"뿐이다.
//
// ⚠ 임계값은 단일 타격 데미지보다 충분히 커야 한다. PlayerBase.TakeDamage는 피해를 전부 적용한
//    뒤에 OnHpChanged를 발행하고 그 다음 사망을 판정하므로, 한 방에 임계값을 넘어 0 이하로
//    떨어지면 이 컴포넌트가 개입할 틈이 없다(무적은 지나간 피해를 되돌리지 못하고,
//    TryRestoreCurrentHp도 0 이하 복원을 거부한다).
public class TutorialSafety : MonoBehaviour
{
    [Tooltip("본진 HP가 이 값 이하로 떨어지면 무적으로 전환한다. 단일 타격 데미지보다 충분히 크게 잡을 것.")]
    [Min(1f)]
    [SerializeField]
    private float invincibleBelowHp = 100f;

    private PlayerBase _base;

    private void OnEnable()
    {
        // 본진이 아직 스폰되지 않았을 수 있다 — static 이벤트로 늦은 스폰도 잡는다.
        PlayerBase.OnBaseSpawned += Bind;

        Bind(PlayerBase.Instance);
    }

    private void OnDisable()
    {
        PlayerBase.OnBaseSpawned -= Bind;

        Unbind();
    }

    private void Bind(PlayerBase playerBase)
    {
        if (playerBase == null || ReferenceEquals(playerBase, _base))
        {
            return;
        }

        // 본진이 다시 스폰되면 이전 것의 구독과 무적 상태를 먼저 정리한다.
        Unbind();

        _base = playerBase;
        _base.OnHpChanged += OnHpChanged;

        // 붙는 시점에 이미 위험 수위일 수 있다.
        OnHpChanged(_base.CurrentHp, _base.MaxHp);
    }

    private void Unbind()
    {
        if (_base == null)
        {
            return;
        }

        _base.OnHpChanged -= OnHpChanged;

        // 남겨 두면 정식 플레이에서도 죽지 않는 버그가 된다.
        _base.DebugInvincible = false;
        _base = null;
    }

    // 한 번 켜진 무적은 다시 끄지 않는다. HP가 회복되면 껐다 켜는 것이 맞아 보이지만,
    // 그러면 경계값 근처에서 무적이 깜빡이면서 위의 '한 방에 넘어가는' 실패 경로가 다시 열린다.
    private void OnHpChanged(float currentHp, float maxHp)
    {
        if (_base == null)
        {
            return;
        }

        if (currentHp <= invincibleBelowHp)
        {
            _base.DebugInvincible = true;
        }
    }
}
