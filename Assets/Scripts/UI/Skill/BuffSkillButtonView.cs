using UnityEngine;
using UnityEngine.UI;

/// 버프 스킬 버튼(#103). 감전(SkillButtonView)과 달리 타겟팅이 없어 클릭 시 바로 발동한다.
/// 쿨다운/낮 게이팅 중엔 Button의 Disabled Color로만 표시(오버레이 없음).
[RequireComponent(typeof(Button))]
public class BuffSkillButtonView : MonoBehaviour
{
    [SerializeField] Button _button;

    private void Awake()
    {
        if (_button == null) _button = GetComponent<Button>();
        _button.onClick.AddListener(HandleClick);
    }

    private void Update()
    {
        if (BuffSkillManager.Instance == null) return;

        _button.interactable = BuffSkillManager.Instance.CanCast();
    }

    private void HandleClick()
    {
        BuffSkillManager.Instance?.Activate();
    }
}
