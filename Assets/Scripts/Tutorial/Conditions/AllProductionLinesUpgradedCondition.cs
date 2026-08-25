using System;
using UnityEngine;

// 모든 생산 라인이 지정한 레벨 이상이면 충족된다.
// "몇 번 올렸는가"가 아니라 "지금 다 올라가 있는가"를 본다(AllVillagersAssignedCondition과 같은 계보) —
// 이 프로젝트에는 업그레이드 되돌리기(#444, Ctrl+Z)가 있어서, 횟수를 세면 되돌린 뒤에도
// 통과한 채로 남아 화면과 판정이 어긋난다.
//
// ⚠ 클래스 이름을 바꾸면 [SerializeReference]로 저장된 기존 스텝 데이터가 깨진다.
[Serializable]
public class AllProductionLinesUpgradedCondition : TutorialCondition
{
    [Tooltip("모든 생산 라인이 도달해야 하는 레벨. 1이면 '각각 한 번씩 업그레이드'다.")]
    [Min(1)]
    [SerializeField]
    private int requiredLevel = 1;

    private ManagementController _management;

    public override void Begin(TutorialContext context)
    {
        _management = context.Management;

        if (_management == null)
        {
            Debug.LogWarning($"[{nameof(AllProductionLinesUpgradedCondition)}] 씬에서 ManagementController를 찾지 못해 이 단계를 넘어갈 수 없다.");

            return;
        }

        _management.OnChanged += OnChanged;

        // 이미 전부 올라간 상태로 진입할 수 있다.
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
        int lineCount = _management.LineCount;

        // 라인이 하나도 없으면 '전부 만족'이 참이 되어 시작하자마자 통과한다 — 배선 사고를 조용히 넘기지 않는다.
        if (lineCount <= 0)
        {
            return;
        }

        for (int i = 0; i < lineCount; i++)
        {
            if (_management.LineLevel(i) < requiredLevel)
            {
                return;
            }
        }

        Fire();
    }
}
