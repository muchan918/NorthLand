using UnityEngine;

/// <summary>
/// 공용 효과음 재생 진입점. 호출부는 클립도 볼륨도 모르고 "무슨 일이 일어났는지"만 말한다.
///
/// 클립은 <see cref="SfxBank"/>(Resources의 SO 1개)가 들고, 재생은 <see cref="AudioManager"/>가 한다.
/// 그래서 이 클래스는 상태를 거의 갖지 않으며 **씬 배치도 부팅도 필요 없다** — 싱글톤을 하나 더 늘리지
/// 않으려고 정적 클래스로 뒀다(WL-002).
///
/// ⚠ <c>SoundId</c> 같은 enum + 딕셔너리를 두지 않는다(`Docs/Core/AudioManager.md` §7). 소리마다 **이름 있는
/// 메서드**라 호출부에서 오타가 컴파일 에러로 잡히고, "이 소리가 어디서 나는지"를 참조 찾기로 셀 수 있다.
///
/// 새 소리를 늘리는 절차: <see cref="SfxBank"/>에 <c>Cue</c> 필드 추가 → 여기 메서드 추가 → 뱅크 에셋에 배선.
/// </summary>
public static class Sfx
{
    // Resources 루트 기준 경로. 에셋을 옮기면 여기도 함께 고친다(다른 Resources 소비처와 같은 규약).
    private const string BankPath = "ScriptableObjects/SfxBank";

    private static SfxBank bank;

    // 뱅크가 없을 때 매 클릭마다 Resources.Load를 다시 때리지 않기 위한 플래그.
    // 경고도 이 플래그 덕에 1회만 나간다 — 없으면 콘솔이 클릭 수만큼 같은 줄로 덮인다.
    private static bool loadAttempted;

    /// <summary>
    /// 플레이 진입마다 정적 캐시를 비운다.
    ///
    /// 지금은 프로젝트 설정이 도메인 리로드를 켜 둬서(`EditorSettings.m_EnterPlayModeOptions: 0`)
    /// 정적 필드가 자동으로 초기화되지만, **속도 때문에 Domain Reload를 끄는 순간 무증상이 아니게 된다**
    /// — <see cref="loadAttempted"/>가 <c>true</c>인 채로 두 번째 플레이 세션에 들어가고, 그 사이 뱅크가
    /// 언로드됐다면 다시 집어올 기회가 없다. 증상은 "두 번째 플레이부터 효과음만 안 남"이라 원인에서 멀다.
    ///
    /// 리로드가 켜져 있을 때는 아무 일도 하지 않는 두 줄이므로 비용이 없다.
    /// <c>SubsystemRegistration</c>은 씬 로드보다 앞이라 첫 소비처보다 항상 먼저 돈다
    /// (<c>UndoRequest.RegisterShortcut</c>이 같은 시점을 쓴다).
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        bank = null;
        loadAttempted = false;
    }

    private static SfxBank Bank
    {
        get
        {
            // ⚠ `bank != null`을 조건에 더하지 말 것. 뱅크 에셋이 아예 없는 프로젝트에서는 영영
            //   null이라 매 호출마다 Resources.Load와 경고가 반복된다 — 이 플래그의 존재 이유가 그것이다.
            //   정적 캐시가 세션을 넘어 남는 문제는 아래 ResetStatics가 담당한다.
            if (loadAttempted)
            {
                return bank;
            }

            loadAttempted = true;
            bank = Resources.Load<SfxBank>(BankPath);

            if (bank == null)
            {
                Debug.LogWarning($"[Sfx] 사운드 뱅크를 찾지 못했습니다(Assets/Resources/{BankPath}.asset). 공용 효과음이 재생되지 않습니다.");
            }

            return bank;
        }
    }

    /// <summary>버튼·토글을 누른 순간. <see cref="UiClickSfx"/>가 전역으로 부르므로 버튼별 배선은 필요 없다.</summary>
    public static void ButtonClick()
    {
        Play(Bank != null ? Bank.ButtonClick : null);
    }

    /// <summary>
    /// 건물·타워를 클릭해 정보 패널이 열리는 순간. **패널이 켜지는 쪽(OnEnable)이 아니라 클릭하는 쪽에서 부른다** —
    /// 패널 루트 몇 개는 씬 로드 시 켜진 채 시작했다가 <c>Awake</c>에서 스스로 닫히고(`CastlePanelUI` 등),
    /// 하단 액션 패널은 낮/밤마다 토글된다(`PhasePanelSwitcher`). 활성화를 신호로 삼으면 그 둘이 전부 오발한다.
    /// </summary>
    public static void PanelOpen()
    {
        Play(Bank != null ? Bank.PanelOpen : null);
    }

    /// <summary>
    /// 튜토리얼 말풍선이 새로 나타나는 순간. 문구를 갱신하는 호출이 아니라
    /// 비활성 상태에서 활성 상태로 바뀌는 경계에서만 <see cref="TutorialOverlay"/>가 부른다.
    /// </summary>
    public static void TutorialBubbleOpened()
    {
        Play(Bank != null ? Bank.TutorialBubbleOpen : null);
    }

    /// <summary>
    /// 튜토리얼 팝업이 새로 나타나는 순간. 선택 언어가 바뀌어 열린 팝업의 내용을 다시 그릴 때는
    /// 호출하지 않으므로 안내음이 중복 재생되지 않는다.
    /// </summary>
    public static void TutorialPopupOpened()
    {
        Play(Bank != null ? Bank.TutorialPopupOpen : null);
    }

    /// <summary>타워가 실제로 설치된 순간. 일반 배치와 합성 결과 배치가 같은 경로(`TowerPlacer.PlaceTower`)를 지난다.</summary>
    public static void TowerInstalled()
    {
        Play(Bank != null ? Bank.TowerInstall : null);
    }

    /// <summary>
    /// 조작이 반려된 순간 — 배치 불가 지점 클릭, 합성 재료·코스트 부족, 주민 증가 실패가 공유한다.
    /// 이 소리가 없으면 전부 <c>Debug.Log</c>만 남기고 화면에서는 **아무 일도 안 일어난 것처럼 보인다**.
    /// </summary>
    public static void Rejected()
    {
        Play(Bank != null ? Bank.Rejected : null);
    }

    /// <summary>
    /// 커서가 <see cref="CursorKind.Blocked"/>(「안 됨」)인 자리를 클릭한 순간 — <b>대상 표면이 아예 없는 곳</b>
    /// (하늘·맵 밖·타일 사이 틈)이다.
    ///
    /// <b><see cref="Rejected"/>와 나눈 이유</b>: 커서 그림이 이미 둘을 구분하고 있다. 표면 위에서 규칙에
    /// 걸려 반려된 것(<c>Placing</c> 커서 + <see cref="Rejected"/>)과 애초에 조작 대상이 아닌 곳
    /// (<c>Blocked</c> 커서)은 플레이어가 해야 할 다음 행동이 다르다 — 앞은 "다른 타일을 고른다",
    /// 뒤는 "지도 위로 커서를 옮긴다".
    ///
    /// ⚠ <b>부르는 곳은 지금 스킬 조준 하나뿐이다.</b> 배치 모드의 같은 상황(표면 밖 클릭)은 이미
    /// <see cref="Rejected"/>를 부르고 있고 두 클립이 청감상 거의 같아 그대로 뒀다(#550). 나중에 클립이
    /// 갈리면 <c>MouseManager.UpdatePlacement</c>의 표면 밖 분기를 이쪽으로 옮기면 된다.
    /// </summary>
    public static void Blocked()
    {
        Play(Bank != null ? Bank.Blocked : null);
    }

    /// <summary>
    /// 충전이 없어 스킬을 쓸 수 없는데 시전을 시도한 순간. Q 단축키와 <b>비활성 상태의 스킬 버튼 클릭</b>이
    /// 함께 쓴다 — 후자는 <c>Button.interactable == false</c>라 <c>onClick</c>도 <see cref="UiClickSfx"/>의
    /// 공용 클릭음도 지나지 않으므로, <see cref="IDisabledClickFeedback"/> 훅을 통해 여기로 온다.
    ///
    /// ⚠ <b>낮 페이즈·게임 종료로 못 쓰는 경우에는 울리지 않는다</b>(<c>SkillManager.IsOnCooldown</c>이
    /// 그 둘을 걸러낸다). "기다리면 된다"는 안내가 기다려도 소용없는 상태에서 나가면 거짓말이 된다.
    /// </summary>
    public static void SkillOnCooldown()
    {
        Play(Bank != null ? Bank.SkillOnCooldown : null);
    }

    /// <summary>
    /// 건물 레벨이 오른 순간. 생산 라인 업그레이드와 업그레이드 전용 건물이 같은 소리를 쓴다 —
    /// 플레이어에게는 둘 다 "이 건물이 좋아졌다" 하나의 사건이고, 컨트롤러도
    /// <see cref="ManagementController.BuildingAction.Upgraded"/> 한 종류로만 알린다.
    /// </summary>
    public static void BuildingUpgraded()
    {
        Play(Bank != null ? Bank.BuildingUpgrade : null);
    }

    /// <summary>
    /// 본진에서 주민 수가 늘어난 순간. 다른 소리와 달리 <see cref="AudioManager.PlaySfxExclusive"/>로 나간다 —
    /// 클립이 길어(9.5초) 원샷으로 두면 연타 시 여러 벌이 겹쳐 쌓인다. 전용 소스라 끊고 처음부터 다시 난다.
    /// </summary>
    public static void ResidentIncreased()
    {
        PlayExclusive(Bank != null ? Bank.ResidentIncreased : null);
    }

    /// <summary>
    /// 같은 소리를 **한 프레임에 두 번 이상 요청하면 첫 번째만 낸다.**
    ///
    /// 선택 표시의 소유자가 둘(대상 자신의 <c>ISelectable</c> 훅 + <c>TowerMergeCoordinator</c>)이라,
    /// 타워를 한 번 클릭하면 <c>Tower.OnSelected</c>가 **같은 프레임에 두 번** 불린다 —
    /// <c>MouseManager.CommitClick</c>이 한 번, 그 뒤 <c>OnPrimarySelect</c> → 코디네이터의
    /// <c>RefreshPanel</c>이 한 번. 그쪽 주석이 "표시는 idempotent라 겹쳐도 무해"라고 적어둔 그대로인데,
    /// **소리는 idempotent가 아니다** — 두 번 겹쳐 울려 그 소리만 유독 크게 들렸다.
    ///
    /// 호출부를 하나로 줄이는 대신 여기서 거르는 이유: 두 번 부르는 것이 그쪽 설계의 의도이고(표시 복구),
    /// 소리를 위해 그 구조를 비틀면 사거리 원·정보 패널 쪽에서 잔존 버그가 되살아난다(WL-087).
    /// </summary>
    private static bool ClaimFrame(SfxBank.Cue cue)
    {
        int frame = Time.frameCount;

        if (cue.LastPlayedFrame == frame)
        {
            return false;
        }

        cue.LastPlayedFrame = frame;
        return true;
    }

    /// <summary>
    /// 조작을 되돌린 순간. 되돌리기 버튼과 Ctrl+Z가 <see cref="UndoRequest.Submit"/> 한 곳으로 모이므로
    /// 소리도 거기 한 곳에서 난다 — 단축키에만 소리가 빠지는 사고가 구조적으로 생기지 않는다.
    /// </summary>
    public static void Undone()
    {
        Play(Bank != null ? Bank.Undo : null);
    }

    /// <summary>
    /// 다시 실행한 순간. <b>아직 부르는 곳이 없다</b> — 다시 실행 기능 자체가 없어 클립만 미리 꽂아뒀다.
    /// 기능이 생기면 되돌리기와 대칭이 되도록 요청 진입점 한 곳에서 부를 것.
    /// </summary>
    public static void Redone()
    {
        Play(Bank != null ? Bank.Redo : null);
    }

    private static void Play(SfxBank.Cue cue)
    {
        // 클립 미배선(cue.Clip == null)은 매니저가 조용히 무시한다 — 뱅크가 덜 채워져도 게임이 깨지지 않는다.
        if (cue == null || AudioManager.Instance == null || !ClaimFrame(cue))
        {
            return;
        }

        AudioManager.Instance.PlaySfx(cue.Clip, cue.Volume);
    }

    private static void PlayExclusive(SfxBank.Cue cue)
    {
        if (cue == null || AudioManager.Instance == null || !ClaimFrame(cue))
        {
            return;
        }

        AudioManager.Instance.PlaySfxExclusive(cue.Clip, cue.Volume);
    }
}
