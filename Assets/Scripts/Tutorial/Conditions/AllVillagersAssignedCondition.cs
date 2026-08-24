using System;
using UnityEngine;

// 미배치 주민이 하나도 남지 않으면 충족된다.
// "몇 번 배치했는가"가 아니라 "지금 다 배치되어 있는가"를 본다 —
// 배치했다 뺐다 해도 판정이 어긋나지 않고, 주민 수가 늘어도 규칙이 그대로 성립한다.
//
// ⚠ 클래스 이름을 바꾸면 [SerializeReference]로 저장된 기존 스텝 데이터가 깨진다.
[Serializable]
public class AllVillagersAssignedCondition : TutorialCondition
{
    private ManagementController _management;

    public override void Begin(TutorialContext context)
    {
        _management = context.Management;

        if (_management == null)
        {
            Debug.LogWarning($"[{nameof(AllVillagersAssignedCondition)}] 씬에서 ManagementController를 찾지 못해 이 단계를 넘어갈 수 없다.");

            return;
        }

        _management.OnChanged += OnChanged;

        // 이미 전부 배치된 상태로 진입할 수 있다.
        OnChanged();
    }

    public override void End()
    {
        if (_management != null)
        {
            _management.OnChanged -= OnChanged;
            _management = null;
        }
    }

    private void OnChanged()
    {
        if (!_management.HasIdleVillagers)
        {
            Fire();
        }
    }
}
