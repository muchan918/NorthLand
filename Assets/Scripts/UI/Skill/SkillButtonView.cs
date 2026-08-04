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

    [Tooltip("스킬 인디케이터·시전 지점의 고정 y. 전투맵에서 가장 낮은 도로 타일 윗면 높이에 맞춘다.")]
    [SerializeField] float _castHeight = 20f;

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
            Snap = SnapToCastHeight,
            OnConfirmed = pos => SkillManager.Instance.CastAt(pos), // CastAt은 bool 반환 → Action<Vector3>엔 람다로 감싸 반환값 버림
        });
    }

        // 시전 지점: 커서 광선을 고정 높이(_castHeight) 수평면에 투영해서 구한다.
    // hit.point의 x/z를 쓰면 레이가 높은 타일 옆면에 맞는 순간 지점이 튀어서, y를 고정해도
    // 타일 경계를 넘을 때마다 인디케이터가 덜컥 움직인다. 평면 투영은 커서 바로 아래에 항상 붙는다.
    private Vector3 SnapToCastHeight(Ray ray, RaycastHit hit)
    {
        Plane castPlane = new Plane(Vector3.up, new Vector3(0f, _castHeight, 0f));
        return castPlane.Raycast(ray, out float distance)
            ? ray.GetPoint(distance)
            : new Vector3(hit.point.x, _castHeight, hit.point.z); // 광선이 평면과 거의 평행할 때 폴백
    }
}