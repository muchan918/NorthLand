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
public class TowerButtonView : MonoBehaviour, IDisabledClickFeedback
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
    [Tooltip("선택 표시 파티클 묶음 (Slot/Fx_Selected) — 이 타워로 배치 모드에 들어와 있는 동안만 켠다. 비어 있으면 표시 없이 넘어간다.")]
    [SerializeField] GameObject _selectedEffect;

    [Header("비활성 표시")]
    [Tooltip("버튼이 죽었을 때 아이콘에 씌울 색. Button의 색 전이는 targetGraphic(테두리) 하나에만 걸려 아이콘까지 닿지 않는다(#470).")]
    [SerializeField] Color _dimmedIconColor = new(0.42f, 0.42f, 0.42f, 1f);

    [Header("선택 스케일")]
    [Tooltip("선택된 칸의 배율. 프리팹 저작 스케일에 곱한다 — LayoutGroup은 localScale을 보지 않으므로 칸 자리는 흔들리지 않는다.")]
    [SerializeField] float _selectedScale = 1.1f;
    [Tooltip("선택 세션 중 나머지 칸의 배율. 아무도 고르지 않은 평상시에는 적용하지 않는다.")]
    [SerializeField] float _unselectedScale = 0.9f;

    Color _normalIconColor = Color.white;
    bool _iconColorCached;
    Vector3 _authoredScale = Vector3.one;
    bool _authoredScaleCached;
    Button _button;
    ParticleSystem[] _selectedParticles;
    bool _selectedParticlesCached;
    // 배선 유실 경고는 **세션당 1회**다(TowerInfoUI._mergeWiringWarned와 같은 규약이지만 static인 이유:
    // 그쪽은 패널 1개인데 이쪽은 타워 수만큼 칸이 생겨, 인스턴스 플래그면 같은 경고가 칸마다 쏟아진다).
    static bool s_bannerWiringWarned;
    // 선택 표시 슬롯의 배선 유실 경고도 같은 규약이다(EnsureSelectedEffectWired 참조).
    static bool s_selectedEffectWiringWarned;
    // 선택 가능 여부의 **요청값**. 해제 연출이 도는 동안 적용을 미루므로 요청과 적용을 분리해 들고 있는다.
    bool _requestedSelectable = true;
    // 비활성 사유가 **자원 부족 하나뿐인지**. 요청값과 적용값을 따로 드는 이유는 위와 같다 —
    // 해제 연출이 도는 동안 표시는 아직 옛 값이므로 소리도 옛 값을 따라야 한다.
    bool _requestedBlockedByCost;
    bool _blockedByCost;

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
    /// 이 칸을 고를 수 있는지(#470). <paramref name="selectable"/>은 호출부가 계산한 AND 결과 그대로다 —
    /// <b>표시</b>는 사유를 구분하지 않는다(회색 하나).
    ///
    /// <para><paramref name="blockedByCost"/>는 <b>소리 전용</b>이다 — 회색이 된 사유가 자원 부족 하나뿐일 때만
    /// true이며, 그 칸을 눌렀을 때 무엇을 낼지 <see cref="OnDisabledClick"/>이 이 값으로 판단한다.
    /// 사유를 이 뷰가 다시 계산하지 않는 이유는 그쪽 주석 참조.</para>
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
    public void SetSelectable(bool selectable, bool blockedByCost)
    {
        _requestedSelectable = selectable;
        _requestedBlockedByCost = blockedByCost;

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

        // 소리의 사유도 표시와 같은 시점에 옮긴다. 여기서 미루지 않으면, 해제 연출로 아직 회색인 칸이
        // 자원 부족 소리를 먼저 낸다 — 플레이어에게는 자물쇠가 보이는 채로 "돈이 없다"는 소리가 난다.
        _blockedByCost = _requestedBlockedByCost;
    }

    /// <summary>
    /// 이 칸의 타워로 <b>배치 모드에 들어와 있는지</b>(#563). 무엇이 선택인지는 판단하지 않는다 —
    /// 패널이 매 갱신마다 "선택된 타워 == 이 칸"을 계산해 넘긴다.
    ///
    /// <para><b>왜 선택 가능 여부(<see cref="SetSelectable"/>)와 엮지 않는가</b>: 배치 세션이 시작된 뒤
    /// 자원이 줄어 칸이 회색이 되는 경로가 있다. 그때 표시를 걷으면 "지금 뭘 놓고 있는지"가 사라진다 —
    /// 배치 중이라는 사실은 자원과 무관하게 세션이 끝날 때까지 유지된다.</para>
    ///
    /// <para><b>왜 알파가 아니라 GameObject를 끄는가</b>: UIParticle은 살아 있는 동안 매 프레임 메시를
    /// 굽고 자기 CanvasRenderer로 캔버스 배칭을 끊는다. 선택은 항상 한 칸뿐인데 알파만 내리면
    /// 나머지 칸 전부가 그 비용을 그대로 낸다.</para>
    ///
    /// <para><b>왜 Play/Stop을 명시로 부르는가</b>: <c>playOnAwake</c>는 <b>첫 활성화에만</b> 재생을 건다.
    /// 아래에서 <c>StopEmittingAndClear</c>로 멈춘 뒤 다시 켜면 자동으로 살아나지 않아, 두 번째 선택부터
    /// 조용히 아무것도 안 보인다.</para>
    /// </summary>
    public void SetSelected(bool selected, bool anySelected)
    {
        // **파티클 가드보다 앞이다.** 아래 두 줄(미배선 이탈 / 중복 이탈)은 파티클만의 사정인데,
        // 그 뒤에 두면 이펙트가 배선되지 않은 환경에서 스케일까지 함께 조용히 사라진다.
        ApplySelectedScale(selected, anySelected);

        if (!EnsureSelectedEffectWired()) return;
        // 표시 상태가 곧 직전 값이다 — 별도 플래그를 들지 않는 이유는 SetLocked와 같다(#424).
        if (_selectedEffect.activeSelf == selected) return;

        CacheSelectedParticles();

        if (selected)
        {
            _selectedEffect.SetActive(true);
            // withChildren:false — 캐시가 이미 자식 전부라 true면 중첩된 시스템에 Play가 두 번 간다.
            // Clear를 먼저 부르는 이유: 아래 Stop 경로를 **타지 않고** 꺼지는 길이 있다(패널째 비활성화되는
            // 밤 전환 등). 그때 남은 파티클이 다음 선택의 첫 프레임에 잔상으로 스친다.
            foreach (var ps in _selectedParticles) { ps.Clear(false); ps.Play(false); }
            return;
        }

        // 끄기 **전에** 지운다. 살아 있는 파티클을 남긴 채 비활성하면 다음 선택의 첫 프레임에
        // 지난번 잔상이 그대로 한 번 튄다(비활성은 시뮬레이션을 멈출 뿐 버퍼를 비우지 않는다).
        foreach (var ps in _selectedParticles) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        _selectedEffect.SetActive(false);
    }

    // 세 상태다 — 「선택됨 / 선택 세션 중의 나머지 / 아무도 안 고름」. 마지막을 1배로 되돌리지 않으면
    // 배치 모드에 들어가기도 전에 팔레트 전체가 쪼그라든 채로 그려진다(selected==false 하나로는 뒤 둘이
    // 구별되지 않아 anySelected를 함께 받는 이유).
    //
    // <para><b>왜 localScale인가</b>: LayoutGroup은 스케일을 무시하고 원래 크기로 자리를 잡으므로,
    // 칸 하나가 커져도 나머지가 밀리지 않는다. 대신 커진 칸이 이웃 위로 삐져나오는데, 형제 순서를
    // 올려 앞에 그리면(SetAsLastSibling) 그 순서가 곧 LayoutGroup의 배치 순서라 칸 위치가 바뀐다 —
    // 겹침이 거슬리면 순서가 아니라 그룹의 spacing으로 푼다.</para>
    void ApplySelectedScale(bool selected, bool anySelected)
    {
        CacheAuthoredScale();
        float factor = !anySelected ? 1f : (selected ? _selectedScale : _unselectedScale);
        transform.localScale = _authoredScale * factor;
    }

    // 기준은 `Vector3.one`이 아니라 **프리팹 저작 스케일**이다 — 절대값을 박으면 칸에 스케일을 준
    // 프리팹에서 첫 선택 때 크기가 조용히 바뀐다(`CacheIconColor`가 `Color.white`를 상수로 박지 않는 것과
    // 같은 근거이고, 도감 항목 `FusionTowerEntry.originalScale`도 같은 규약이다). 지연 캐시인 이유 역시
    // 그쪽과 같으며, 여기선 **이 스케일의 기입자가 우리뿐**이라 첫 호출 시점의 값이 항상 저작값이다.
    void CacheAuthoredScale()
    {
        if (_authoredScaleCached) return;
        _authoredScale = transform.localScale;
        _authoredScaleCached = true;
    }

    // 슬롯이 비어 있으면 선택 표시가 **조용히** 사라진다 — 프리팹이 별 저장소(NorthLand-Imported)에 있어
    // 미동기 환경에서 컴파일도 통하고 콘솔도 조용하다(WL-040이 굳힌 「§4 계약 등재 + null 시 1회 경고」 형태).
    // static인 이유는 `s_bannerWiringWarned`와 같다 — 인스턴스 플래그면 칸마다 같은 경고가 쏟아진다.
    bool EnsureSelectedEffectWired()
    {
        if (_selectedEffect != null) return true;

        if (!s_selectedEffectWiringWarned)
        {
            s_selectedEffectWiringWarned = true;
            Debug.LogWarning($"[타워버튼] _selectedEffect 미배선 — 타워를 골라도 선택 표시가 뜨지 않습니다. " +
                             $"NorthLand-Imported의 TowerButton.prefab 동기화를 확인하세요(aee42246c 이상). ({name})", this);
        }
        return false;
    }

    // includeInactive:true — 이펙트는 프리팹에서 꺼진 채로 저작된다. false면 빈 배열이 잡혀
    // Play가 아무 데도 닿지 않고, 에러도 나지 않는다.
    void CacheSelectedParticles()
    {
        if (_selectedParticlesCached) return;
        _selectedParticles = _selectedEffect.GetComponentsInChildren<ParticleSystem>(true);
        _selectedParticlesCached = true;

        if (_selectedParticles.Length == 0)
        {
            Debug.LogWarning($"[타워버튼] _selectedEffect 아래에 ParticleSystem이 없습니다 — 선택 표시가 켜져도 아무것도 그려지지 않습니다({name}).", this);
            return;
        }

        // **시간축은 unscaled다.** 이건 장식이 아니라 "지금 이걸 놓는 중"이라는 표시라,
        // 2배속(`GameSpeedController`가 전역 `Time.timeScale`을 올린다)에서 혼자 빨라지거나
        // 설정·튜토리얼 정지(`timeScale` 0)에서 얼어붙으면 안 된다 — SystemMap §6의
        // "안내·피드백은 Use Unscaled Time = on" 기준이고 `SpeedBoostEffect`와 같은 근거다.
        // 프리팹 저작에 맡기지 않고 코드에서 강제하는 이유도 그쪽과 같다: 파티클이 여러 개라
        // 하나만 빠뜨려도 그것만 멎는데, 증상이 인스펙터 깊은 곳에 있어 찾기 어렵다.
        foreach (var ps in _selectedParticles)
        {
            var main = ps.main;
            main.useUnscaledTime = true;
        }
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

    /// <summary>
    /// 회색이 된 칸을 눌렀을 때(<see cref="IDisabledClickFeedback"/>). <b>자원 부족이 유일한 사유일 때만</b>
    /// 소리를 낸다 — 미해금은 자물쇠 그림이 이미 이유를 말하고 있고, 튜토리얼 제한에 대고
    /// "자원을 모으라"고 안내하면 거짓말이 된다(<see cref="Sfx.InsufficientResources"/> 주석).
    ///
    /// 사유 판정을 여기서 하지 않는 이유: 게이트를 계산한 자리가 호출부다
    /// (<c>TowerSelectPanelView</c>는 미해금·자원·튜토리얼 셋, <c>TowerMergePanelView</c>는 코스트·튜토리얼 둘).
    /// 이 뷰가 다시 계산하면 판정이 두 벌이 되고, 게이트가 늘 때 한쪽만 고치면 어긋난다.
    ///
    /// ⚠ 게임 상태를 바꾸지 않는다 — <see cref="IDisabledClickFeedback"/>의 제약이다.
    /// 연타 겹침은 <see cref="Sfx.InsufficientResources"/>가 자기 안에서 막으므로 여기 게이트는 없다.
    /// </summary>
    public void OnDisabledClick(Selectable pressed)
    {
        if (!_blockedByCost) return;

        Sfx.InsufficientResources();
    }
}
