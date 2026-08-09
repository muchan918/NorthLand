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
        if (WarnRetired()) return;
        
        if (_button == null) _button = GetComponent<Button>();
        _button.onClick.AddListener(HandleClick);
    }

    // #315 — BuffSkillManager와 같은 트립와이어. 되살릴 때는 이 메서드와 Awake 첫 줄만 지운다.
    bool WarnRetired()
    {
        Debug.LogError("[#315] 제거된 버프 스킬 버튼(BuffSkillButtonView)이 씬에 배선돼 있습니다 — 이 오브젝트를 삭제하세요.", this);
        enabled = false;
        return true;
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
