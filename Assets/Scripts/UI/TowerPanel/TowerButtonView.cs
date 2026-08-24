using Cysharp.Threading.Tasks;   // Forget() — 해제 연출은 버튼 갱신을 막지 않는다
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 타워 선택 버튼 한 칸의 위젯 참조. <see cref="TowerSelectPanelView"/>가 생성 직후 <see cref="Set"/>로 채운다.<br/>
/// 아이콘을 <c>Button.targetGraphic</c>에 그리지 않는 이유: targetGraphic은 테두리(Img_Frame)가 맡아야
/// 클릭 영역이 칸 전체(90x90)가 되고, 거기에 스프라이트를 덮어쓰면 테두리가 지워지기 때문이다.
/// 그래서 아이콘 슬롯을 별도 참조로 갖는다.
/// </summary>
public class TowerButtonView : MonoBehaviour
{
    [Tooltip("타워 아이콘 (Slot/Img_Icon) — 테두리 안쪽에 그려진다.")]
    [SerializeField] Image _icon;
    [Tooltip("타워 이름 (Banner/Txt_Name)")]
    [SerializeField] TMP_Text _name;
    [Tooltip("이름 배너 묶음 (Banner) — 이름 없이 쓰는 화면에서 통째로 끈다. 비어 있으면 라벨만 비운다.")]
    [SerializeField] GameObject _nameBanner;
    [Tooltip("잠금 오버레이 (Slot/TowerLockOverlay) — 미해금 타워에만 켠다.")]
    [SerializeField] GameObject _lockOverlay;
    [Tooltip("해제 연출. 비어 있으면 오버레이에서 찾고, 그래도 없으면 연출 없이 즉시 걷는다.")]
    [SerializeField] TowerLockUnlockEffect _unlockEffect;

    [Header("비활성 표시")]
    [Tooltip("버튼이 죽었을 때 아이콘에 씌울 색. Button의 색 전이는 targetGraphic(테두리) 하나에만 걸려 아이콘까지 닿지 않는다(#470).")]
    [SerializeField] Color _dimmedIconColor = new(0.42f, 0.42f, 0.42f, 1f);

    Color _normalIconColor = Color.white;
    bool _iconColorCached;
    Button _button;
    // 배선 유실 경고는 **세션당 1회**다(TowerInfoUI._mergeWiringWarned와 같은 규약이지만 static인 이유:
    // 그쪽은 패널 1개인데 이쪽은 타워 수만큼 칸이 생겨, 인스턴스 플래그면 같은 경고가 칸마다 쏟아진다).
    static bool s_bannerWiringWarned;
    // 선택 가능 여부의 **요청값**. 해제 연출이 도는 동안 적용을 미루므로 요청과 적용을 분리해 들고 있는다.
    bool _requestedSelectable = true;

    /// <summary>
    /// 잠금 오버레이 표시. **해금 여부만** 반영한다 — 자원 부족으로 버튼이 죽은 상태에까지
    /// 자물쇠를 띄우면 "아직 안 열림"과 "돈이 없음"이 같아 보인다.
    ///
    /// <para>해제 연출은 여기서 <b>전이</b>로만 발화한다 — 오버레이의 현재 표시 상태가 곧 "직전 값"이라
    /// 별도 플래그를 들지 않는다. 플래그를 들면 세이브 복원처럼 갱신이 이벤트 없이 일어나는 경로에서
    /// 표시와 플래그가 갈라진다(#424).</para>
    /// </summary>
    public void SetLocked(bool locked)
    {
        if (_lockOverlay == null) return;

        var effect = ResolveEffect();

        // 연출이 도는 중이면 이미 "열림"으로 가고 있다. activeSelf는 아직 true이므로
        // 이 가드가 없으면 재생 중 들어온 갱신마다 연출이 처음부터 다시 돈다.
        if (!locked && effect != null && effect.IsPlaying) return;

        bool wasLocked = _lockOverlay.activeSelf;
        if (locked == wasLocked) return;

        if (locked)
        {
            // 잠김으로 되돌아가는 건 정상 진행에는 없다(씬 재시작·복원뿐) — 연출 없이 즉시.
            if (effect != null) effect.SnapToLocked();
            else _lockOverlay.SetActive(true);
            return;
        }

        // 버튼 갱신이 연출을 기다릴 이유는 없다 — 던지고 빠진다.
        if (effect != null) PlayUnlockAsync(effect).Forget();
        else
        {
            _lockOverlay.SetActive(false);
            ApplySelectable();   // 연출이 없으면 미룰 것도 없다
        }
    }

    // 자물쇠가 아직 떨고 있는데 칸이 먼저 살아나면 "열렸는데 왜 자물쇠가 남아 있지"로 읽힌다.
    // 연출이 끝난 **뒤** 그 시점의 최신 요청값을 적용한다 — "끝나면 활성"이 아니라 "끝나면 최신값"인
    // 이유는, 연출 도중 자원이 바뀌어 못 사게 됐다면 살아나는 게 아니라 회색으로 남아야 하기 때문이다.
    async UniTaskVoid PlayUnlockAsync(TowerLockUnlockEffect effect)
    {
        // PlayAsync는 취소(파괴·재진입)를 안에서 삼키고 정상 종료하므로 여기서 잡을 예외가 없다.
        await effect.PlayAsync();

        if (this == null) return;   // 연출 중 파괴(씬 전환)
        ApplySelectable();
    }

    /// <summary>
    /// 이 칸을 고를 수 있는지(#470). 자원 부족·미해금·튜토리얼 제한 중 무엇이 원인인지는 구분하지
    /// 않는다 — 호출부가 계산한 AND 결과를 그대로 넘긴다.
    ///
    /// <para><b>왜 아이콘 색과 <c>interactable</c>을 한 메서드가 갖는가</b>: 둘은 같은 사실의 두 표현인데
    /// 주인이 갈리면 해제 연출 도중 갈라진다. <c>interactable</c>은 Button의 ColorTint를 통해
    /// <c>targetGraphic</c>(테두리)을 즉시 밝히므로, 색만 미루고 이걸 호출부가 따로 세우면
    /// 자물쇠가 떠는 동안 테두리만 먼저 살아난다.</para>
    ///
    /// <para><b>왜 CanvasGroup.alpha가 아닌가</b>: 알파를 내리면 테두리와 자물쇠까지 같이 흐려져
    /// "잠김"과 "못 산다"가 다시 섞인다. 아이콘만 채도를 떨어뜨려 두 상태를 분리한다.</para>
    ///
    /// <para><b>해제 연출 중에는 미룬다</b>: 해금 순간 호출부는 <see cref="SetLocked"/>로 연출을 걸고
    /// 곧바로 이 메서드를 부른다. 요청값만 기록하고, 실제 적용은 연출이 끝날 때
    /// <see cref="PlayUnlockAsync"/>가 한다.</para>
    /// </summary>
    public void SetSelectable(bool selectable)
    {
        _requestedSelectable = selectable;

        var effect = ResolveEffect();
        if (effect != null && effect.IsPlaying) return;

        ApplySelectable();
    }

    // 요청값을 실제 표시로 옮긴다. 저장된 전이가 아니라 **매번 최신 요청값을 읽으므로** 연출 종료·
    // 자원 변동 등 어느 경로로 불려도 결과가 같다(중복 호출이 무해하다).
    void ApplySelectable()
    {
        if (_icon != null)
        {
            CacheIconColor();
            _icon.color = _requestedSelectable ? _normalIconColor : _dimmedIconColor;
        }

        if (_button == null) _button = GetComponent<Button>();
        if (_button != null) _button.interactable = _requestedSelectable;
    }

    TowerLockUnlockEffect ResolveEffect()
    {
        if (_unlockEffect == null && _lockOverlay != null)
            _unlockEffect = _lockOverlay.GetComponent<TowerLockUnlockEffect>();
        return _unlockEffect;
    }

    // 평상시 색은 프리팹 저작값이다 — Color.white를 상수로 박으면 아이콘에 틴트를 준 프리팹에서
    // 첫 회복 시 색이 조용히 바뀐다. Awake가 아니라 지연 캐시인 이유는 첫 호출이 Awake보다
    // 앞설 수 있는 경로(비활성 프리팹에서 Instantiate 직후 채움)를 막기 위함이다.
    void CacheIconColor()
    {
        if (_iconColorCached) return;
        _normalIconColor = _icon.color;
        _iconColorCached = true;
    }

    /// <summary>
    /// 아이콘을 채우고 이름 배너를 끈다 — <b>칸은 아이콘 전용이고 이름은 호버 툴팁이 낸다</b>(#470).
    /// 배치 팔레트는 <see cref="TowerTooltipSource"/>, 합성 후보는 <c>TowerMergeCandidateHover</c>가 담당한다.
    ///
    /// <para><b>이름을 받는 오버로드를 두지 않는다</b>: <see cref="HideNameBanner"/>가 칸 높이를 파괴적으로
    /// 감산하고 되돌리는 경로가 없다. 이름을 받는 진입점을 남겨두면, 이미 한 번 아이콘 전용으로 채워진
    /// 칸에 그걸 부르는 순간 배너가 돌아오지 않고 높이만 깎인 채 남는다 — 에러 없이. 이름이 필요한
    /// 화면이 생기면 그때 원래 높이를 캐시하는 <c>SetNameBannerVisible(bool)</c> 대칭 API로 만들 것.</para>
    /// </summary>
    public void Set(Sprite icon)
    {
        if (_icon != null)
        {
            // 아이콘 미할당 SO를 흰 사각형으로 그리지 않는다 — 빈 칸이 낫다(ResourceAsset 계보와 같은 규약).
            _icon.enabled = icon != null;
            _icon.sprite = icon;
        }

        HideNameBanner();
    }

    // 배너를 끄면 세로 레이아웃이 배너 몫(배너 높이 + 간격)을 빈칸으로 들고 있게 된다 —
    // 프리팹의 저작값에서 그만큼을 빼 되돌린다. 90을 상수로 박지 않는 이유는 프리팹 칸 크기가
    // 바뀔 때 조용히 어긋나기 때문이다.
    void HideNameBanner()
    {
        if (_nameBanner == null)
        {
            // `_name`은 배선됐는데 `_nameBanner`가 비어 있으면 **배선 유실**이다 — 정본 프리팹은 둘 다
            // 배선하고, 배너 자체가 없는 변종이면 그 자식인 라벨도 없어 둘 다 null이다. 프리팹이 별
            // 저장소(NorthLand-Imported)에 있어 미동기 환경에서 이 조합이 나오는데, 컴파일도 통하고
            // 콘솔도 조용해서 "왜 내 화면만 칸이 삐져나오나"로만 보인다(#445가 겪은 경로).
            if (_name != null)
            {
                if (!s_bannerWiringWarned)
                {
                    s_bannerWiringWarned = true;
                    Debug.LogWarning($"[타워버튼] _nameBanner 미배선 — 이름 배너를 끄지 못해 칸이 커진 채로 남습니다. " +
                                     $"NorthLand-Imported의 TowerButton.prefab 동기화를 확인하세요(4e41e3227 이상). ({name})", this);
                }
                _name.text = string.Empty;
            }
            return;
        }
        if (!_nameBanner.activeSelf) return;   // 이미 끈 칸에 두 번 적용해 높이를 두 번 깎지 않는다

        var bannerLayout = _nameBanner.GetComponent<LayoutElement>();
        _nameBanner.SetActive(false);

        var rootLayout = GetComponent<LayoutElement>();
        if (rootLayout == null || bannerLayout == null) return;

        // LayoutElement는 인스펙터 체크박스가 꺼진 항목에 **-1**을 돌려준다 — 그대로 더하면
        // spacing이 양수인 만큼 shrink가 양수가 되어(-1 + 4 = 3) 아래 가드를 통과하고 엉뚱한 값을 깎는다.
        // 음수를 먼저 0으로 잘라내고, 배너 높이가 저작돼 있지 않으면(0) 축소를 건너뛰며 소리를 낸다.
        float bannerHeight = Mathf.Max(0f, bannerLayout.preferredHeight, bannerLayout.minHeight);
        if (bannerHeight <= 0f)
        {
            Debug.LogWarning($"[타워버튼] 이름 배너에 LayoutElement 높이가 저작되지 않아 칸 높이를 줄이지 못했습니다 — 배너 자리가 빈칸으로 남습니다({name}).", this);
            return;
        }

        var group = GetComponent<VerticalLayoutGroup>();
        float shrink = bannerHeight + (group != null ? group.spacing : 0f);
        if (shrink <= 0f) return;

        if (rootLayout.minHeight > 0f) rootLayout.minHeight -= shrink;
        if (rootLayout.preferredHeight > 0f) rootLayout.preferredHeight -= shrink;
    }
}
