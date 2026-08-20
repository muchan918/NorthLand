using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// 스킬 버튼 1개(#103). 클릭 시 MouseManager에 스킬 타겟팅을 요청하고,
/// 확정되면 SkillManager.CastAt이 실행되도록 연결한다. TowerSelectPanelView.cs의 배선 방식 참고.
/// 충전 소진/낮 게이팅 중엔 Button의 Disabled Color(인스펙터에서 설정)로 막고,
/// 다음 충전까지의 진행을 원형 게이지로 보여준다(#397). 보유 충전 수와 남은 초는 서로를
/// 대신하는 표시라 동시에 뜨지 않는다 — 0발일 때만 남은 초, 1발 이상일 때만 충전 수(#319).
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
    [Tooltip("다음 충전까지의 진행을 그리는 원형 게이지(#397). Image Type=Filled/Radial 360. 비워두면 표시하지 않는다.")]
    [SerializeField] Image _fillImage;

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
        RefreshFill();
        RefreshRechargeText();
        RefreshChargeText();
    }

    // 게이지는 0에서 1로 차오르는 방향(Clockwise 체크 기준). 만충이면 통째로 끈다 —
    // 꽉 찬 게이지를 남겨두면 "아직 뭔가 진행 중"으로 읽히고, 만충은 더 기다릴 것이 없는 상태다.
    // fillAmount 세터가 같은 값을 걸러내므로 여기엔 별도 캐싱을 두지 않는다.
    private void RefreshFill()
    {
        if (_fillImage == null) return;

        bool show = SkillManager.Instance.Charges < SkillManager.Instance.MaxCharges;
        if (_fillImage.gameObject.activeSelf != show)
            _fillImage.gameObject.SetActive(show);

        if (!show) return;

        _fillImage.fillAmount = SkillManager.Instance.RechargeProgress01;
    }

    // 다음 1발이 찰 때까지 남은 시간을 올림한 정수 초로 보여주되, 0발일 때만 띄운다(#397) —
    // 충전이 남아 있으면 지금 쓸 수 있다는 뜻이라, 초가 같이 보이면 기다려야 하는 것처럼 읽힌다.
    // 진행 상황은 그 동안 원형 게이지가 대신 보여준다.
    private void RefreshRechargeText()
    {
        if (_rechargeText == null) return;

        float remaining = SkillManager.Instance.RechargeRemaining;
        bool show = SkillManager.Instance.Charges == 0;

        if (_rechargeText.gameObject.activeSelf != show)
            _rechargeText.gameObject.SetActive(show);

        if (!show) return;

        // 초 단위로 올림하므로 실제로 바뀌는 건 초당 1회뿐이다.
        int seconds = Mathf.CeilToInt(remaining);
        if (seconds == _shownRechargeSeconds) return;

        _shownRechargeSeconds = seconds;
        _rechargeText.text = seconds.ToString();
    }

    // 보유 수만 숫자로 찍는다(#397). 두 경우엔 아예 숨긴다:
    //  - 최대가 1발이면 값이 0/1뿐이라 회색 처리·게이지와 완전히 중복이다. 추가시전(#319) 보상으로
    //    2발이 되는 순간 숫자가 나타나므로, 그 등장 자체가 "연발이 생겼다"는 신호가 된다.
    //  - 0발이면 같은 자리를 남은 초가 대신하므로 "0"은 노이즈다.
    private void RefreshChargeText()
    {
        if (_chargeText == null) return;

        int charges = SkillManager.Instance.Charges;
        int maxCharges = SkillManager.Instance.MaxCharges;

        bool show = maxCharges > 1 && charges > 0;
        if (_chargeText.gameObject.activeSelf != show)
            _chargeText.gameObject.SetActive(show);

        if (!show) return;
        if (charges == _shownCharges && maxCharges == _shownMaxCharges) return;

        _shownCharges = charges;
        _shownMaxCharges = maxCharges;
        _chargeText.text = charges.ToString();
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