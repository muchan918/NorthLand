using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// 스킬 버튼 1개(#103). 클릭 시 MouseManager에 스킬 타겟팅을 요청하고,
/// 확정되면 SkillManager.CastAt이 실행되도록 연결한다. TowerSelectPanelView.cs의 배선 방식 참고.
/// 충전 소진/낮 게이팅 중엔 Button의 Disabled Color(인스펙터에서 설정)로 막고,
/// 다음 충전까지의 진행을 원형 게이지로 보여준다(#397). 보유 충전 수와 남은 초는 서로를
/// 대신하는 표시라 동시에 뜨지 않는다 — 0발일 때만 남은 초, 1발 이상일 때만 충전 수(#319).
[RequireComponent(typeof(Button))]
public class SkillButtonView : MonoBehaviour, IDisabledClickFeedback
{
    // 충전 대기 안내음의 최소 재생 간격(초). 클립(SFX_Skill_Cooldown, 0.75초)보다 살짝 짧게 잡아
    // 겹치지 않으면서도 "두드리면 반응한다"는 감은 남긴다. 정확히 맞출 필요는 없다 — 겹침을 막는
    // 가드일 뿐이라 클립 길이가 바뀌어도 조금 겹치거나 조금 더 눌러 먹는 정도로 끝난다.
    private const float k_UnavailableSfxMinInterval = 0.7f;

    [SerializeField] Button _button;
    [SerializeField] GameObject _skillGhostPrefab; // 마우스를 따라다닐 범위 인디케이터

    [Tooltip("스킬 인디케이터·시전 지점의 고정 y. 전투맵에서 가장 낮은 도로 타일 윗면 높이에 맞춘다.")]
    // 씬 값(GameScene의 5)과 같은 기본값을 둔다 — 기본값이 몬스터 부양 높이보다 위면
    // 시전면이 몬스터 위로 올라가 스킬이 전부 빗나간다(#398). 프리팹 리셋·신규 씬에서 조용히 재발할 자리다.
    [SerializeField] float _castHeight = 5f;
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
    Action _shortcutHandler;

    // 충전 대기 안내음을 마지막으로 낸 시각. 연타를 흡수하는 데만 쓴다(아래 PlayUnavailableSfx).
    // 음수 초기값이라 첫 시도는 항상 통과한다.
    float _lastUnavailableSfxTime = float.NegativeInfinity;

    private void Awake()
    {
        if (_button == null) _button = GetComponent<Button>();
        _button.onClick.AddListener(HandleClick);

        // 마우스 클릭음은 UiClickSfx 전역 훅이 낸다. Q는 그 훅을 지나지 않으므로 전용 진입점에서
        // 조준 시작에 성공했을 때만 같은 공용 클릭음을 낸다.
        // KeyboardManager의 바인딩 목록은 static이므로 해제할 때 쓸 같은 Action 인스턴스를 보관한다.
        _shortcutHandler = HandleShortcut;
        KeyboardManager.Bind(Key.Q, KeyModifier.None, _shortcutHandler, "감전 스킬");
    }

    private void OnDestroy()
    {
        if (_button != null)
            _button.onClick.RemoveListener(HandleClick);

        if (_shortcutHandler != null)
            KeyboardManager.Unbind(Key.Q, KeyModifier.None, _shortcutHandler);
    }

    private void Update()
    {
        if (SkillManager.Instance == null) return;

        _button.interactable = SkillManager.Instance.CanCast()
            && TutorialInputGate.AllowsForDisplay(TutorialAction.UseSkill);
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
        => TryBeginTargeting();

    private void HandleShortcut()
    {
        if (TryBeginTargeting())
        {
            Sfx.ButtonClick();
            return;
        }

        PlayUnavailableSfx();
    }

    /// 버튼이 회색(비활성)인 채로 눌렸다 — Update가 `interactable`을 내려 둔 상태라 `onClick`도
    /// 공용 클릭음도 지나지 않으므로, UiClickSfx가 IDisabledClickFeedback으로 여기까지 넘겨준다.
    /// Q와 같은 소리를 내는 것이 의도다: 플레이어가 한 일("지금 스킬을 쓰려 했다")이 같다.
    public void OnDisabledClick(Selectable pressed)
        => PlayUnavailableSfx();

    /// 시전 시도가 반려된 뒤의 안내음. **충전 소진일 때만 낸다** — 낮 페이즈나 게임 종료로 막힌 상태는
    /// 기다려도 풀리지 않아 안내음이 거짓말이 되고, 튜토리얼 게이트로 막힌 것은 안내 문구가
    /// 이미 화면에 떠 있다. 판정은 SkillManager.IsOutOfCharges 한 곳이 소유한다.
    ///
    /// ⚠ **낮 페이즈 게이트가 실제로 걸리는 경로는 Q뿐이다.** 낮에는 PhasePanelSwitcher가 스킬 패널을
    /// 통째로 끄므로 「낮에 회색 버튼을 클릭」은 화면에 존재하지 않는다. 반면 Q는 살아 있다 —
    /// KeyboardManager는 static 목록에 바인딩을 들고 대상의 활성 여부를 보지 않고,
    /// 이 뷰는 Awake에서 Bind하고 OnDestroy에서만 Unbind하므로 패널이 꺼져도 바인딩이 남는다.
    /// 그래서 낮에 Q를 눌러도 무음인 것이 이 게이트의 실제 효과다.
    ///
    /// ⚠ **연타를 여기서 흡수한다.** Sfx.Play의 프레임 래치(ClaimFrame)는 **같은 프레임**의 중복만
    /// 막고, 이 소리가 나가는 AudioManager.PlaySfx는 동시재생 상한이 없다(SystemMap §2 — "드물게 한 번
    /// 울리는 짧은 소리에만 쓴다"). 클립이 0.75초라 초당 몇 번만 두드려도 여러 벌이 겹쳐 쌓이는데,
    /// **못 쓰는 것을 확인하려고 두세 번 두드리는 것이 정확히 이 기능의 대상 시나리오다**
    /// (그러면 "안 된다"는 안내가 경보처럼 커진다).
    ///
    /// Sfx 층이 아니라 이 뷰에 두는 이유: 다른 큐의 거동을 건드리지 않는다. 같은 노출이
    /// Sfx.Rejected(무효 타일 연타)에도 있으므로 공용 규약으로 올릴지는 AudioManager.md §7 TODO에 남겼다.
    ///
    /// 시간축은 **unscaled**다 — 안내·피드백은 배속·정지와 무관해야 한다(SystemMap §6).
    private void PlayUnavailableSfx()
    {
        if (SkillManager.Instance == null || !SkillManager.Instance.IsOutOfCharges)
            return;

        float now = Time.unscaledTime;
        if (now - _lastUnavailableSfxTime < k_UnavailableSfxMinInterval)
            return;

        _lastUnavailableSfxTime = now;
        Sfx.SkillOutOfCharges();
    }

    private bool TryBeginTargeting()
    {
        if (SkillManager.Instance == null || MouseManager.Instance == null) return false;
        if (!TutorialInputGate.Allows(TutorialAction.UseSkill)) return false;
        if (!SkillManager.Instance.CanCast()) return false;

        if (_skillGhostPrefab == null)
        {
            Debug.LogError("[스킬버튼] skillGhostPrefab이 지정되지 않았습니다.");
            return false;
        }

        MouseManager.Instance.BeginSkillTargeting(new SkillTargetRequest
        {
            GhostPrefab = _skillGhostPrefab,
            Snap = SnapToCastHeight,
            OnConfirmed = pos => SkillManager.Instance.CastAt(pos), // CastAt은 bool 반환 → Action<Vector3>엔 람다로 감싸 반환값 버림
        });

        return true;
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
