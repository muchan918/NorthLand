using System;
using UnityEngine;

// 건물 바로가기 바에서 지정한 건물로 이동하면 충족된다.
// 건물을 월드에서 직접 클릭한 경우는 인정하지 않는다 — 그건 BuildingSelectedCondition의 몫이고,
// 이 단계가 가르치는 것은 "바로가기 바가 있다"이기 때문이다.
// (바로가기도 내부적으로 MouseManager.SelectExternally를 부르므로, 전용 통지 없이는 두 경로가 구분되지 않는다.)
//
// ⚠ 클래스 이름을 바꾸면 [SerializeReference]로 저장된 기존 스텝 데이터가 깨진다.
[Serializable]
public class BuildingShortcutUsedCondition : TutorialCondition
{
    [Tooltip("특정 건물만 인정하려면 지정한다. 비우면 아무 건물이나 인정한다.")]
    [SerializeField]
    private BuildingAsset targetBuilding;

    private BuildingShortcutBar _bar;

    public override void Begin(TutorialContext context)
    {
        _bar = context.ShortcutBar;

        if (_bar == null)
        {
            Debug.LogWarning($"[{nameof(BuildingShortcutUsedCondition)}] 씬에서 BuildingShortcutBar를 찾지 못해 이 단계를 넘어갈 수 없다.");

            return;
        }

        _bar.Focused += OnFocused;
    }

    public override void End()
    {
        if (_bar != null)
        {
            _bar.Focused -= OnFocused;
            _bar = null;
        }
    }

    private void OnFocused(BuildingAsset building)
    {
        // 초점에 건물이 연결되지 않은 항목(null)은 어느 건물인지 알 수 없으므로 인정하지 않는다.
        if (building == null)
        {
            return;
        }

        if (targetBuilding == null || building == targetBuilding)
        {
            Fire();
        }
    }
}
