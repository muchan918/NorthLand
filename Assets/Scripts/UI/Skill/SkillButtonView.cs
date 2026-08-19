using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// 스킬 버튼 1개(#103). 클릭 시 MouseManager에 스킬 타겟팅을 요청하고,
/// 확정되면 SkillManager.CastAt이 실행되도록 연결한다. TowerSelectPanelView.cs의 배선 방식 참고.
/// 충전 소진/낮 게이팅 중엔 Button의 Disabled Color(인스펙터에서 설정)로 막고,
/// 보유 충전 수와 다음 충전까지 남은 초를 TMP 텍스트로 함께 보여준다(#319).
[RequireComponent(typeof(Button))]
public class SkillButtonView : MonoBehaviour
{
    [SerializeField] Button _button;
    [SerializeField] GameObject _skillGhostPrefab; // 마우스를 따라다닐 범위 인디케이터

    [Tooltip("스킬 인디케이터·시전 지점의 고정 y. 전투맵에서 가장 낮은 도로 타일 윗면 높이에 맞춘다.")]
    // 씬 값(GameScene의 2)과 같은 기본값을 둔다 — 기본값이 몬스터 부양 높이(6f)보다 위면
    // 시전면이 몬스터 위로 올라가 스킬이 전부 빗나간다(#398). 프리팹 리셋·신규 씬에서 조용히 재발할 자리다.
    [SerializeField] float _castHeight = 2f;
    [Tooltip("다음 충전까지 남은 시간(초) 표시. 비워두면 표시하지 않는다.")]
    [SerializeField] TMP_Text _rechargeText;
    [Tooltip("보유 충전 수 표시(#319). 비워두면 표시하지 않는다.")]
    [SerializeField] TMP_Text _chargeText;

    // 마지막으로 찍은 값. 매 프레임 문자열을 새로 만들지 않으려고 캐싱한다 — TMP의 text 세터가
    // 같은 값을 걸러내더라도 보간 문자열은 이미 할당된 뒤라, 조립 자체를 건너뛰어야 의미가 있다.
    // -1은 "아직 한 번도 안 찍음"이라 첫 프레임에 반드시 갱신된다.
    int _shownCharges = -1;
    int _shownMaxCharges = -1;
    int _shownRechargeSeconds = -1;

    private void Awake()
    {
        if (_button == null) _button = GetComponent<Button>();
        _button.onClick.AddListener(HandleClick);
    }

    private void Update()
    {
        if (SkillManager.Instance == null) return;

        _button.interactable = SkillManager.Instance.CanCast();
        RefreshRechargeText();
        RefreshChargeText();
    }

    // 다음 1발이 찰 때까지 남은 시간을 올림한 정수 초로 보여주고, 만충이면 숨긴다 —
    // "0"이 남아 있으면 아직 기다리는 중인 것처럼 읽힌다.
    private void RefreshRechargeText()
    {
        if (_rechargeText == null) return;

        float remaining = SkillManager.Instance.RechargeRemaining;
        bool show = remaining > 0f;

        if (_rechargeText.gameObject.activeSelf != show)
            _rechargeText.gameObject.SetActive(show);

        if (!show) return;

        // 초 단위로 올림하므로 실제로 바뀌는 건 초당 1회뿐이다.
        int seconds = Mathf.CeilToInt(remaining);
        if (seconds == _shownRechargeSeconds) return;

        _shownRechargeSeconds = seconds;
        _rechargeText.text = seconds.ToString();
    }

    // "2/2" 형태로 보유/최대를 함께 보여준다 — 보유만 찍으면 몇 발까지 차는지 알 수 없다.
    // 보상이 없어도 기본 1발이 있으므로 항상 표시한다.
    private void RefreshChargeText()
    {
        if (_chargeText == null) return;

        int charges = SkillManager.Instance.Charges;
        int maxCharges = SkillManager.Instance.MaxCharges;
        if (charges == _shownCharges && maxCharges == _shownMaxCharges) return;

        _shownCharges = charges;
        _shownMaxCharges = maxCharges;
        _chargeText.text = $"{charges}/{maxCharges}";
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