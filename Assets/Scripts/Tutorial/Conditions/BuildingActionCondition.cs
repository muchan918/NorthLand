using System;
using UnityEngine;

// 경영 공간에서 특정 행동이 일어나면 충족된다.
// 주민 배치 · 주민 회수 · 건물 업그레이드 · 주민 수 증가가 전부 이 조건 하나로 커버된다 —
// 무엇을 기다릴지는 인스펙터의 action 값이 정한다.
[Serializable]
public class BuildingActionCondition : TutorialCondition
{
    [SerializeField]
    private ManagementController.BuildingAction action =
        ManagementController.BuildingAction.VillagerAssigned;

    private ManagementController _management;

    public override void Begin(TutorialContext context)
    {
        _management = context.Management;

        if (_management == null)
        {
            Debug.LogWarning("[Tutorial] 씬에서 ManagementController를 찾지 못했다 — 이 단계는 넘어가지 않는다.");
            return;
        }

        _management.OnBuildingAction += OnBuildingAction;
    }

    public override void End()
    {
        if (_management == null)
        {
            return;
        }

        _management.OnBuildingAction -= OnBuildingAction;
        _management = null;
    }

    private void OnBuildingAction(BuildingAsset building, ManagementController.BuildingAction happened)
    {
        if (happened == action)
        {
            Fire();
        }
    }
}