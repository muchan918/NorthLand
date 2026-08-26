using System;
using UnityEngine;

// 주민을 생산 건물로 드래그해 실제 배치에 성공하면 충족된다.
// ManagementController의 VillagerAssigned는 +/- 버튼과 드래그가 함께 쓰므로,
// 드래그 조작을 가르치는 단계는 ResidentDragCoordinator의 전용 성공 신호를 본다.
//
// ⚠ 클래스 이름을 바꾸면 [SerializeReference]로 저장된 기존 스텝 데이터가 깨진다.
[Serializable]
public class ResidentDragAssignedCondition : TutorialCondition
{
    [Tooltip("드래그로 배치해야 하는 주민 수.")]
    [Min(1)]
    [SerializeField]
    private int requiredCount = 1;

    [Tooltip("켜면 드래그 성공과 함께 미배치 주민이 한 명도 남지 않아야 완료된다.")]
    [SerializeField]
    private bool requireAllVillagersAssigned = true;

    private ResidentDragCoordinator _drag;
    private ManagementController _management;
    private int _assigned;

    public override void Begin(TutorialContext context)
    {
        _assigned = 0;
        _drag = ResidentDragCoordinator.Instance;
        _management = context.Management;

        if (_drag == null)
        {
            Debug.LogWarning($"[{nameof(ResidentDragAssignedCondition)}] ResidentDragCoordinator가 없어 이 단계를 넘어갈 수 없다.");
            return;
        }

        _drag.VillagersAssignedByDrag += OnAssigned;

        if (_management != null)
        {
            _management.OnChanged += Evaluate;
        }
        else if (requireAllVillagersAssigned)
        {
            Debug.LogWarning($"[{nameof(ResidentDragAssignedCondition)}] ManagementController가 없어 전체 주민 배치 여부를 확인할 수 없다.");
        }
    }

    public override void End()
    {
        if (_drag != null)
        {
            _drag.VillagersAssignedByDrag -= OnAssigned;
            _drag = null;
        }

        if (_management != null)
        {
            _management.OnChanged -= Evaluate;
            _management = null;
        }
    }

    private void OnAssigned(int count)
    {
        _assigned += count;
        Evaluate();
    }

    private void Evaluate()
    {
        if (_assigned < Mathf.Max(1, requiredCount))
        {
            return;
        }

        if (requireAllVillagersAssigned
            && (_management == null || _management.HasIdleVillagers))
        {
            return;
        }

        Fire();
    }
}
