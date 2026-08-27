using System;
using UnityEngine;

// 건물 바로가기 바에서 지정한 건물로 이동하거나, 월드에서 같은 건물을 직접 선택하면 충족된다.
// 바로가기도 내부적으로 MouseManager.SelectExternally를 호출하므로 두 이벤트가 모두 올 수 있지만,
// TutorialCondition.Fire는 단계 종료를 한 번만 처리하므로 중복 통지는 무해하다.
//
// ⚠ 클래스 이름을 바꾸면 [SerializeReference]로 저장된 기존 스텝 데이터가 깨진다.
[Serializable]
public class BuildingShortcutUsedCondition : TutorialCondition
{
    [Tooltip("특정 건물만 인정하려면 지정한다. 비우면 아무 건물이나 인정한다.")]
    [SerializeField]
    private BuildingAsset targetBuilding;

    private BuildingShortcutBar _bar;
    private MouseManager _mouse;

    public override void Begin(TutorialContext context)
    {
        _bar = context.ShortcutBar;
        _mouse = MouseManager.Instance;

        if (_bar != null)
        {
            _bar.Focused += OnFocused;
        }

        if (_mouse != null)
        {
            _mouse.OnPrimarySelect += OnPrimarySelect;
        }

        if (_bar == null && _mouse == null)
        {
            Debug.LogWarning($"[{nameof(BuildingShortcutUsedCondition)}] 건물 선택 입력을 받을 대상을 찾지 못해 이 단계를 넘어갈 수 없다.");
        }
    }

    public override void End()
    {
        if (_bar != null)
        {
            _bar.Focused -= OnFocused;
            _bar = null;
        }

        if (_mouse != null)
        {
            _mouse.OnPrimarySelect -= OnPrimarySelect;
            _mouse = null;
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

    private void OnPrimarySelect(ISelectable selected)
    {
        if (selected is BuildingInfo building
            && (targetBuilding == null || building.Asset == targetBuilding))
        {
            Fire();
        }
    }
}
