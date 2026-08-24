using UnityEngine;

/// 주민의 목소리. 주민 프리팹 **루트**에 붙인다.
///
/// | 애니메이터 상태 | 소리 | 방식 |
/// |---|---|---|
/// | `Talking_1~3` (R4 수다) | 수다 클립 | 그 상태에 있는 내내 반복 |
/// | `Laughing` (R12 웃음) | 웃음 클립 | 진입 시 1회 |
/// | `Surprised` (R7 놀람) | 놀람 클립 | 진입 시 1회 |
/// | `Wave` (R3 인사 · 작별) | **Hi 또는 Bye** | 진입 시 1회 — 대화 단계로 가른다 |
/// | `Run` (R8 귀가) | 밤 클립 | 집에 닿을 때까지 반복 |
///
/// ── BT가 아니라 애니메이터 상태를 따라간다 ────────────────────────────
///
/// `BossPatternVfx`와 같은 규약이다 — 보스 파티클을 BT에서 쏘던 것을 애니메이터 상태 추종으로 바꾼 이유가
/// 그대로 여기 적용된다: **BT에서 쏘면 상태 전이(크로스페이드)보다 먼저 터진다.** 상태를 보면
/// 소리와 그림이 어긋날 수가 없고, `ResidentConverseAction`을 한 줄도 고치지 않아도 된다.
///
/// 화자와 청자를 구분하는 코드도 필요 없다. 말하는 쪽만 `Talking_*`에 들어가고 웃는 쪽만 `Laughing`에
/// 들어가므로(§7.2), 상태를 따라가는 것만으로 **누가 소리를 내는지가 저절로 맞는다.**
///
/// ── 왜 2D인가 ────────────────────────────────────────────────────────
///
/// `spatialBlend = 0`으로 두고 볼륨·팬을 <see cref="ResidentVoiceAudibility"/>가 계산한 값으로 매 프레임
/// 직접 쓴다. 근거는 그쪽 주석에 있다(오쏘 카메라라 원근이 없고, `AudioListener`가 마을 위 463유닛에
/// 떠 있어 3D 감쇠가 화면과 무관해진다).
///
/// 그래서 **이 컴포넌트가 주민 루트에 있는 것은 소리 때문이 아니라 좌표와 Animator 때문이다.**
/// 자식 오브젝트로 내려도 얻는 것이 없다.
///
/// ⚠ <c>PlayOneShot</c>을 쓰지 않는다. 그쪽은 **호출 시점의 볼륨을 굽기 때문에**(`AudioManager.PlaySfx`의
///   경고와 같은 함정) 재생 중에 카메라를 움직여도 소리가 따라오지 않는다 — 이 기능의 요구가 정확히
///   그 반대다. `clip` + `Play()`로 틀고 `volume`을 매 프레임 갱신하면 설정 슬라이더도 즉시 반영된다.
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
[AddComponentMenu("NorthLand/Resident/Resident Voice")]
public class ResidentVoice : MonoBehaviour
{
    /// 수다 상태 이름. **`ResidentBehaviorGraphBuilder.TalkStates`와 같아야 한다** — 이름이 어긋나면
    /// 조용히 소리만 안 난다(컨트롤러에 없는 상태는 영영 매칭되지 않는다).
    private static readonly string[] k_TalkStates = { "Talking_1", "Talking_2", "Talking_3" };

    /// 웃음 상태 이름. 〃 `ResidentBehaviorGraphBuilder.LaughState`.
    private const string k_LaughState = "Laughing";

    /// 놀람 상태 이름(R7). 〃 `ResidentBehaviorGraphBuilder.SurprisedState`.
    private const string k_SurprisedState = "Surprised";

    /// 인사 상태 이름. 〃 `ResidentBehaviorGraphBuilder.GreetState`.
    /// **인사와 작별이 이 상태 하나를 공유한다** — 아래 <see cref="PickGreetingClip"/> 참고.
    private const string k_WaveState = "Wave";

    /// 귀가 달리기 상태 이름. 〃 `ResidentBehaviorGraphBuilder.RunState`.
    private const string k_RunState = "Run";

    [Header("클립")]
    [Tooltip("수다 중 무작위로 골라 재생한다. 애니메이션 번호와 짝을 맞추지 않는다 — 클립이 애니메이션보다 " +
             "훨씬 짧아(1.9초 vs 10.3초) 한 턴에 여러 번 들어가기 때문이다.")]
    [SerializeField] private AudioClip[] talkClips;

    [Tooltip("웃음(R12) 1회 재생용.")]
    [SerializeField] private AudioClip laughClip;

    [Tooltip("놀람(R7) 1회 재생용. 대화 상대가 사라졌을 때 등 Surprised 상태에 들어오는 순간 재생한다.")]
    [SerializeField] private AudioClip surprisedClip;

    [Tooltip("만났을 때의 인사(R3). Wave 상태 + 대화 단계가 Greeting일 때.")]
    [SerializeField] private AudioClip hiClip;

    [Tooltip("헤어질 때의 인사. Wave 상태 + 대화 단계가 Farewell일 때 — 애니메이션은 인사와 같은 것을 다시 쓴다.")]
    [SerializeField] private AudioClip byeClip;

    [Tooltip("밤에 집으로 뛰어가는 동안(R8) 무작위로 골라 반복 재생한다. 도착할 때까지 이어진다.")]
    [SerializeField] private AudioClip[] nightClips;

    [Header("재생")]
    [Tooltip("이 주민 목소리의 기본 크기. 최종 볼륨 = 이 값 × 화면 감쇠 × 설정의 효과음 볼륨.")]
    [Range(0f, 1f)]
    [SerializeField] private float volume = 1f;

    [Tooltip("수다 클립 사이의 쉬는 간격(초). 말하는 동안 이 간격을 두고 계속 재잘거린다.")]
    [SerializeField] private Vector2 gapSeconds = new Vector2(0.15f, 0.6f);

    [Tooltip("좌우 팬의 세기. 0이면 팬 없음, 1이면 화면 가장자리에서 완전히 한쪽으로 몰린다.")]
    [Range(0f, 1f)]
    [SerializeField] private float panAmount = 1f;

    private AudioSource source;
    private Animator animator;
    private Resident resident;

    private int[] talkHashes;
    private int laughHash;
    private int surprisedHash;
    private int waveHash;
    private int runHash;

    private float gapTimer;
    private int lastTalkIndex = -1;
    private int lastNightIndex = -1;

    /// 직전 프레임의 애니메이터 상태. 1회성 소리(웃음·인사)를 **상태에 들어온 순간에만** 트는 근거다.
    private int lastState;

    private void Awake()
    {
        source = GetComponent<AudioSource>();

        // 자식까지 탐색: Animator는 모델 자식에 붙는 프리팹 구성이 흔하다
        // (`ResidentAgent`가 같은 이유로 같은 탐색을 한다 — WL-093).
        animator = GetComponentInChildren<Animator>();

        // 인사와 작별을 가르는 데만 쓴다(PickGreetingClip). 상태만으로는 구분되지 않는다.
        resident = GetComponent<Resident>();

        // 조용한 무동작을 막는다 — 참조가 빠지면 이 주민만 영영 무음인데, 걷기·대화는 멀쩡해서
        // 증상이 "이 캐릭터만 말을 안 한다"로만 보인다(`ResidentAgent`와 같은 형태의 방어).
        if (animator == null)
        {
            Debug.LogWarning($"[{name}] Animator를 찾지 못해 목소리가 나오지 않습니다.", this);
        }

        // 저작 실수로 조용히 깨지지 않도록 재생 규약을 코드에서 못박는다.
        // 특히 spatialBlend는 0이어야 한다 — 3D로 두면 Unity의 거리 감쇠가 우리가 계산한 볼륨 위에
        // 한 번 더 곱해져, 마을 위 463유닛의 리스너 때문에 사실상 전부 무음이 된다.
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.volume = 0f;

        talkHashes = new int[k_TalkStates.Length];

        for (int i = 0; i < k_TalkStates.Length; i++)
        {
            talkHashes[i] = Animator.StringToHash(k_TalkStates[i]);
        }

        laughHash = Animator.StringToHash(k_LaughState);
        surprisedHash = Animator.StringToHash(k_SurprisedState);
        waveHash = Animator.StringToHash(k_WaveState);
        runHash = Animator.StringToHash(k_RunState);
    }

    /// ⚠ **주민은 풀에서 재사용된다**(밤에 거둬졌다 아침에 다시 나온다 · 드래그로 들렸다 놓인다).
    /// 여기서 끊지 않으면 다음에 켜질 때 어젯밤 웃음이 이어서 울린다 — 그래프를 `Restart`하는 것과
    /// 같은 이유다(`ResidentSpawner.RestartGraph`).
    private void OnDisable()
    {
        if (source != null)
        {
            source.Stop();
            source.clip = null;
        }

        gapTimer = 0f;
        lastState = 0;
    }

    // LateUpdate에서 돈다 — Animator가 이 프레임의 상태를 확정한 뒤에 읽어야 전이 판정이 흔들리지 않는다.
    private void LateUpdate()
    {
        if (source == null || animator == null)
        {
            return;
        }

        bool audible = ResidentVoiceAudibility.TryEvaluate(transform.position, out float gain, out float pan);

        // **매 프레임 다시 쓴다.** 이 한 줄이 "카메라를 움직이면 소리도 따라 움직인다"의 전부이고,
        // 설정 슬라이더가 이미 울리고 있는 소리에도 반영되는 이유이기도 하다.
        source.volume = audible ? gain * volume * SfxVolume : 0f;
        source.panStereo = audible ? pan * panAmount : 0f;

        int state = ResolveStateHash();
        bool entered = state != lastState;

        lastState = state;

        // 들리지 않는 곳에서는 **새로 시작하지 않는다.** 이미 울리는 소리는 볼륨 0으로 흘려보낸다 —
        // 여기서 끊으면 화면 경계를 스치는 것만으로 말이 뚝뚝 끊긴다.
        if (!audible)
        {
            return;
        }

        // 1회성(웃음·놀람·인사)은 상태에 **들어온 순간에만**, 반복(수다·귀가)은 그 상태에 있는 내내.
        if (state == laughHash)
        {
            if (entered) Play(laughClip);
        }
        else if (state == surprisedHash)
        {
            if (entered) Play(surprisedClip);
        }
        else if (state == waveHash)
        {
            if (entered) Play(PickGreetingClip());
        }
        else if (state == runHash)
        {
            UpdateRepeating(nightClips, ref lastNightIndex, entered);
        }
        else if (IsTalkState(state))
        {
            UpdateRepeating(talkClips, ref lastTalkIndex, entered);
        }
    }

    /// **인사인가 작별인가.** 애니메이션은 둘 다 `Wave` 하나를 쓰므로(`ResidentConverseAction.UpdateFarewell`이
    /// 인사 클립을 그대로 다시 튼다) **상태만으로는 구분되지 않는다.** 대화 세션의 단계가 유일한 단서다.
    ///
    /// 읽는 시점이 안전한 이유: `UpdateGreeting`/`UpdateFarewell` 둘 다 `BeginPlay` 직후 바로 `Running`을
    /// 반환하고, 단계를 넘기는 표시(`MarkGreeted`/`MarkFarewelled`)는 **애니메이션이 끝난 뒤 틱**에 한다.
    /// 그래서 `Wave`에 들어오는 프레임의 단계는 아직 `Greeting`이거나 `Farewell`이다.
    ///
    /// 세션이 없으면 인사로 친다 — 대화 밖에서 손을 흔드는 경로가 생기면 "만났다" 쪽이 자연스럽다.
    private AudioClip PickGreetingClip()
    {
        bool farewell = resident != null
            && resident.Conversation != null
            && resident.Conversation.Phase == ResidentConversation.ConversationPhase.Farewell;

        return farewell ? byeClip : hiClip;
    }

    /// 그 상태에 있는 내내 간격을 두고 반복한다(수다 · 밤 귀가).
    ///
    /// 클립이 애니메이션보다 훨씬 짧아(수다 1.6~1.9초 vs 3.8~10.3초) 한 번만 틀면 나머지가 무언극이 된다.
    /// 그래서 반복하되 **매번 다시 뽑는다** — 같은 클립이 연달아 나오면 반복이 그대로 들린다.
    private void UpdateRepeating(AudioClip[] clips, ref int lastIndex, bool justStarted)
    {
        if (justStarted)
        {
            gapTimer = 0f;   // 시작하는 순간에는 기다리지 않는다
        }

        if (source.isPlaying)
        {
            // 재생 중에 다음 간격을 미리 뽑아 둔다. 끝나는 프레임에 뽑으면 그 프레임에 바로 이어져 붙는다.
            gapTimer = Random.Range(gapSeconds.x, gapSeconds.y);
            return;
        }

        gapTimer -= Time.deltaTime;

        if (gapTimer <= 0f)
        {
            Play(PickDifferent(clips, ref lastIndex));
        }
    }

    /// 직전과 다른 클립을 고른다.
    private static AudioClip PickDifferent(AudioClip[] clips, ref int lastIndex)
    {
        if (clips == null || clips.Length == 0)
        {
            return null;
        }

        if (clips.Length == 1)
        {
            return clips[0];
        }

        int index;

        do
        {
            index = Random.Range(0, clips.Length);
        }
        while (index == lastIndex);

        lastIndex = index;

        return clips[index];
    }

    private void Play(AudioClip clip)
    {
        if (clip == null)
        {
            return;
        }

        source.clip = clip;
        source.Play();
    }

    /// 지금(또는 곧) 물릴 상태의 해시.
    ///
    /// **전이 중에는 다음 상태를 본다.** 크로스페이드가 도는 동안 화면의 그림은 이미 새 동작인데
    /// `GetCurrentAnimatorStateInfo`는 아직 이전 상태를 주므로, 현재만 보면 소리가 페이드 길이만큼 늦는다.
    /// 나가는 전이에서도 같은 규칙이 맞게 동작한다 — 수다에서 Idle로 빠지는 순간 말이 멎는다.
    private int ResolveStateHash()
    {
        AnimatorStateInfo info = animator.IsInTransition(0)
            ? animator.GetNextAnimatorStateInfo(0)
            : animator.GetCurrentAnimatorStateInfo(0);

        return info.shortNameHash;
    }

    private bool IsTalkState(int stateHash)
    {
        for (int i = 0; i < talkHashes.Length; i++)
        {
            if (talkHashes[i] == stateHash)
            {
                return true;
            }
        }

        return false;
    }

    /// 설정 패널의 효과음 볼륨. 매니저가 없는 씬(주민 테스트 씬)에서는 제한하지 않는다.
    private static float SfxVolume =>
        AudioManager.Instance != null ? AudioManager.Instance.GetEffectiveVolume(AudioChannel.Sfx) : 1f;
}
