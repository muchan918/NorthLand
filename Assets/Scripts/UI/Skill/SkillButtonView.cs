using UnityEngine;
using UnityEngine.UI;

/// 스킬 버튼 1개(#103). 클릭 시 MouseManager에 스킬 타겟팅을 요청하고,
/// 확정되면 SkillManager.CastAt이 실행되도록 연결한다. TowerSelectPanelView.cs의 배선 방식 참고.
/// 쿨다운/낮 게이팅 중엔 별도 오버레이 없이 Button의 Disabled Color(인스펙터에서 설정)로만 표시한다.
[RequireComponent(typeof(Button))]
public class SkillButtonView : MonoBehaviour
{
    [SerializeField] Button _button;
    [SerializeField] GameObject _skillGhostPrefab; // 마우스를 따라다닐 범위 인디케이터

    private void Awake()
    {
        if (_button == null) _button = GetComponent<Button>();
        _button.onClick.AddListener(HandleClick);
    }

    private void Update()
    {
        if (SkillManager.Instance == null) return;

        _button.interactable = SkillManager.Instance.CanCast();
    }

    private void HandleClick()
    {
        if (SkillManager.Instance == null || MouseManager.Instance == null) return;
        if (!SkillManager.Instance.CanCast()) return;

        if (_skillGhostPrefab == null)
        {
            Debug.LogError("[스킬버튼] skillGhostPrefab이 지정되지 않았습니다.");
            return;
        }

        MouseManager.Instance.BeginSkillTargeting(new SkillTargetRequest
        {
            GhostPrefab = _skillGhostPrefab,
            OnConfirmed = pos => SkillManager.Instance.CastAt(pos), // CastAt은 bool 반환 → Action<Vector3>엔 람다로 감싸 반환값 버림
        });
    }
}
