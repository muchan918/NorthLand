using System.Collections.Generic;
using NorthLand.Combat;
using UnityEngine;

/// 주민의 대화 상태를 눈에 보이게 표시한다(#276). **디버그 전용** — 씬에 아무 GameObject에 하나 붙이면 된다.
///
/// 왜 필요한가: 대화는 세션 객체와 애니메이터 상태로만 표현되므로, 화면에서는 두 주민이 서로를 보고 서
/// 있는 것만 보인다. 인사·수다·웃음 클립이 실제로 재생되고 있는지, 누가 말하는 쪽인지, 자리잡기가 도는지
/// **아무것도 구분되지 않는다.** 색으로 갈라 놓으면 그 전부가 한눈에 읽힌다.
///
/// ── 왜 기즈모가 아니라 RangeCircle인가 ────────────────────────────
///
/// `RangeCircle`은 보스 예고 원(`EnemyShowTelegraphCircleAction`)·타워 사거리·스킬 범위가 공유하는
/// 절차적 원이고 실제 렌더러라 **게임 뷰에 그대로 보인다.** `OnDrawGizmos`는 씬 뷰에만 나오므로
/// 플레이하면서 확인하기에 맞지 않는다. 글자 라벨만 씬 뷰 기즈모로 함께 그린다(에디터 전용).
///
/// 보스 **노드**(`EnemyShowTelegraphCircleAction`)를 그대로 재사용하지는 못한다 — 고정 시간 1회 표시용이고,
/// Selector는 한 번에 한 브랜치만 돌려 대화 브랜치와 나란히 재생할 수 없다. 재사용되는 것은 그 노드가
/// 쓰던 `RangeCircle`이다.
[AddComponentMenu("NorthLand/Resident/Resident Debug View")]
public class ResidentDebugView : MonoBehaviour
{
    /// 원 반경의 하한. `RangeCircle`의 외곽선 폭이 0.6 상수라(타워 사거리 기준으로 잡힌 값) 반경을
    /// 이보다 작게 하면 외곽선이 원을 통째로 덮어 색이 뭉개진다. 공유 에셋이라 그쪽을 고치지 않는다.
    private const float MinRadius = 0.8f;

    [Header("표시 항목")]
    [Tooltip("대화 참가자의 발밑에 상태 색 원을 그린다. 게임 뷰에 보인다.")]
    [SerializeField] private bool showStateCircles = true;

    [Tooltip("대화 중인 두 주민을 선으로 잇는다. 군중 속에서 어느 둘이 짝인지 구분된다.")]
    [SerializeField] private bool showSessionLinks = true;

    [Tooltip("재생 중인 클립 이름과 단계를 글자로 띄운다. **씬 뷰에만 보인다**(에디터 전용).")]
    [SerializeField] private bool showLabels = true;

    [Header("모양")]
    [Min(MinRadius)]
    [SerializeField] private float circleRadius = 1f;

    [Tooltip("채움 색의 알파. 외곽선은 불투명하게 그린다.")]
    [Range(0f, 1f)]
    [SerializeField] private float fillAlpha = 0.35f;

    [SerializeField] private float linkHeight = 1.4f;

    [Header("상태 색")]
    [Tooltip("다가가기 — 이야기할 거리까지 걸어가는 중")]
    [SerializeField] private Color approachColor = new Color(1f, 0.85f, 0.2f);

    [Tooltip("인사(R3) — Wave 재생 중")]
    [SerializeField] private Color greetColor = new Color(0.2f, 0.9f, 1f);

    [Tooltip("헤어지는 인사 — 같은 Wave를 다시 쓴다. 색으로 갈라 첫 인사와 구분한다")]
    [SerializeField] private Color farewellColor = new Color(0.8f, 0.4f, 1f);

    [Tooltip("수다(R4) — 말하는 쪽")]
    [SerializeField] private Color speakColor = new Color(0.3f, 1f, 0.35f);

    [Tooltip("듣는 쪽 — Idle")]
    [SerializeField] private Color listenColor = new Color(0.35f, 0.5f, 1f);

    [Tooltip("웃음(R12) — Laughing 재생 중")]
    [SerializeField] private Color laughColor = new Color(1f, 0.55f, 0.1f);

    [Tooltip("놀람(R7) — Surprised 재생 중")]
    [SerializeField] private Color surprisedColor = new Color(1f, 0.25f, 0.25f);

    [Tooltip("춤(R5) — 혼자 춤추는 중")]
    [SerializeField] private Color danceColor = new Color(1f, 0.35f, 0.75f);

    [Tooltip("등장(R9) — 문에서 나와 직진 중")]
    [SerializeField] private Color emergeColor = new Color(0.6f, 1f, 0.6f);

    [Tooltip("귀가(R8) — 밤에 문으로 뛰는 중")]
    [SerializeField] private Color goHomeColor = new Color(0.35f, 0.3f, 0.7f);

    [Tooltip("합류 대기 — 상대가 아직 오지 않았다")]
    [SerializeField] private Color pendingColor = new Color(0.6f, 0.6f, 0.6f);

    /// 주민별 원. 주민의 자식으로 만들어 따라다니게 한다 — 주민이 파괴되면 원도 함께 사라진다.
    private readonly Dictionary<Resident, RangeCircle> circles = new Dictionary<Resident, RangeCircle>();

    /// 세션 연결선 풀. 대화 쌍 수만큼만 켜고 나머지는 비활성으로 남긴다.
    private readonly List<LineRenderer> links = new List<LineRenderer>();

    /// 정리 대상 수집 버퍼. 딕셔너리를 순회하며 지울 수 없다.
    private readonly List<Resident> stale = new List<Resident>();

    /// 같은 세션을 두 번 그리지 않기 위한 표시. 참가자가 각각 순회에 걸리기 때문이다.
    private readonly HashSet<ResidentConversation> drawnSessions = new HashSet<ResidentConversation>();

    private Material linkMaterial;

    private void OnDisable()
    {
        // 껐을 때 원이 남아 있으면 디버그 표시가 실제 상태인 것처럼 보인다.
        foreach (KeyValuePair<Resident, RangeCircle> pair in circles)
        {
            if (pair.Value != null)
            {
                pair.Value.Hide();
            }
        }

        for (int i = 0; i < links.Count; i++)
        {
            if (links[i] != null)
            {
                links[i].enabled = false;
            }
        }
    }

    private void OnDestroy()
    {
        // 런타임 생성 머티리얼은 GC 대상이 아니다(RangeCircle과 같은 이유).
        if (linkMaterial != null)
        {
            Destroy(linkMaterial);
        }
    }

    private void LateUpdate()
    {
        // LateUpdate에서 돈다 — NavMeshAgent가 이 프레임의 이동을 끝낸 뒤라 선이 한 프레임 뒤처지지 않는다.
        DrawCircles();
        DrawLinks();
        CollectStale();
    }

    private void DrawCircles()
    {
        IReadOnlyList<Resident> residents = ResidentRegistry.Residents;

        for (int i = 0; i < residents.Count; i++)
        {
            Resident resident = residents[i];

            if (resident == null)
            {
                continue;
            }

            // 무언가 하고 있는 주민만 표시한다 — 산책·유휴는 그냥 걸어다니는 그림이라
            // 원을 띄우면 30개가 상시로 깔린다.
            //
            // 조건을 bool로 묶지 않는다 — &&가 단락 평가되면 컴파일러가 out 인자의 대입을 보장하지 못한다.
            if (!showStateCircles || !enabled || !TryResolveColor(resident, out Color color))
            {
                if (circles.TryGetValue(resident, out RangeCircle existing) && existing != null)
                {
                    existing.Hide();
                }

                continue;
            }

            RangeCircle circle = GetOrCreateCircle(resident);

            if (circle == null)
            {
                continue;
            }

            circle.SetColors(new Color(color.r, color.g, color.b, fillAlpha), color);
            circle.SetRadius(Mathf.Max(MinRadius, circleRadius));
            circle.Show();
        }
    }

    /// 주민이 지금 무엇을 하고 있는지 색으로 환산한다.
    ///
    /// **재생 중인 클립을 먼저 본다.** 세션 단계만 보면 "수다 중"까지는 알 수 있어도 청자가 웃는 순간을
    /// 구분할 수 없고, 클립이 아예 재생되지 않는 고장(상태 이름 오타 등)도 드러나지 않는다 —
    /// 그때는 Idle 색으로 남으므로 "수다 중인데 듣는 색"이라는 어긋남이 눈에 보인다.
    /// 표시할 색을 정한다. **거짓이면 표시하지 않는다** — 산책·유휴는 색이 없다.
    private bool TryResolveColor(Resident resident, out Color color)
    {
        color = default;

        ResidentConversation session = resident.Conversation;
        ResidentAgent agent = resident.Agent;
        string clip = agent != null ? agent.CurrentClipName : null;
        bool waving = !string.IsNullOrEmpty(clip) && clip.Contains("Wave");

        // 문에서 나오는 중(R9). 세션도 춤도 아니므로 아래 판정에 닿기 전에 가른다.
        if (resident.IsEmerging)
        {
            color = emergeColor;
            return true;
        }

        // 귀가 중(R8)은 상태 플래그가 없다 — 밤이라는 전역 조건으로만 도는 브랜치라
        // 재생 중인 클립으로 판정한다. 클립이 안 돌면 색이 안 뜨므로 고장이 그대로 드러난다.
        if (!string.IsNullOrEmpty(clip) && clip.Contains("Running"))
        {
            color = goHomeColor;
            return true;
        }

        if (session == null)
        {
            // 세션도 없고 등장·귀가도 아니면 남는 것은 춤뿐이다.
            if (!resident.IsDancing)
            {
                return false;
            }

            color = danceColor;
            return true;
        }

        // 첫 인사와 헤어지는 인사는 **같은 클립**이라 클립만으로는 구분되지 않는다. 단계로 갈라 준다.
        // 그러면서도 "Wave가 실제로 도는가"는 유지한다 — 재생이 안 되면 이 색이 뜨지 않아 고장이 보인다.
        if (session.Phase == ResidentConversation.ConversationPhase.Farewell)
        {
            color = waving ? farewellColor : listenColor;
            return true;
        }

        if (!string.IsNullOrEmpty(clip))
        {
            if (clip.Contains("Surprised"))
            {
                color = surprisedColor;
                return true;
            }

            if (clip.Contains("Laughing"))
            {
                color = laughColor;
                return true;
            }

            if (waving)
            {
                color = greetColor;
                return true;
            }

            if (clip.Contains("Talking"))
            {
                color = speakColor;
                return true;
            }
        }

        color = session.Phase switch
        {
            ResidentConversation.ConversationPhase.Pending => pendingColor,
            ResidentConversation.ConversationPhase.Greeting => greetColor,
            ResidentConversation.ConversationPhase.Approaching => approachColor,
            _ => listenColor,
        };

        return true;
    }

    private RangeCircle GetOrCreateCircle(Resident resident)
    {
        if (circles.TryGetValue(resident, out RangeCircle circle) && circle != null)
        {
            return circle;
        }

        circle = RangeCircle.Create(resident.transform, Color.clear, Color.clear, "ResidentDebugCircle");
        circles[resident] = circle;

        return circle;
    }

    private void DrawLinks()
    {
        int used = 0;

        if (showSessionLinks && enabled)
        {
            drawnSessions.Clear();

            IReadOnlyList<Resident> residents = ResidentRegistry.Residents;

            for (int i = 0; i < residents.Count; i++)
            {
                Resident resident = residents[i];
                ResidentConversation session = resident != null ? resident.Conversation : null;

                // 참가자 둘이 각각 순회에 걸리므로 세션 단위로 1회만 그린다.
                if (session == null || !drawnSessions.Add(session))
                {
                    continue;
                }

                Resident partner = session.PartnerOf(resident);

                if (partner == null)
                {
                    continue;
                }

                LineRenderer link = GetOrCreateLink(used++);

                link.startColor = TryResolveColor(resident, out Color color) ? color : listenColor;
                link.endColor = TryResolveColor(partner, out Color partnerColor) ? partnerColor : listenColor;
                link.SetPosition(0, resident.transform.position + Vector3.up * linkHeight);
                link.SetPosition(1, partner.transform.position + Vector3.up * linkHeight);
                link.enabled = true;
            }
        }

        // 남는 선은 끈다. 파괴하지 않고 풀로 재사용한다 — 대화 쌍 수가 프레임마다 흔들린다.
        for (int i = used; i < links.Count; i++)
        {
            if (links[i] != null)
            {
                links[i].enabled = false;
            }
        }
    }

    private LineRenderer GetOrCreateLink(int index)
    {
        while (links.Count <= index)
        {
            var go = new GameObject($"ResidentDebugLink_{links.Count}");
            go.transform.SetParent(transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = 0.06f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            // RangeCircle과 같은 셰이더를 쓴다 — URP PC/Mobile 양쪽에서 동작하고 신규 에셋이 필요 없다.
            if (linkMaterial == null)
            {
                linkMaterial = new Material(Shader.Find("Sprites/Default"));
            }

            line.sharedMaterial = linkMaterial;

            links.Add(line);
        }

        return links[index];
    }

    /// 파괴된 주민의 항목을 딕셔너리에서 뺀다. 원 자체는 주민의 자식이라 함께 파괴됐다.
    private void CollectStale()
    {
        stale.Clear();

        foreach (KeyValuePair<Resident, RangeCircle> pair in circles)
        {
            if (pair.Key == null || pair.Value == null)
            {
                stale.Add(pair.Key);
            }
        }

        for (int i = 0; i < stale.Count; i++)
        {
            circles.Remove(stale[i]);
        }
    }

#if UNITY_EDITOR
    /// 씬 뷰에만 나오는 글자 라벨. 클립 이름을 그대로 띄우므로 **무엇이 재생 중인지 문자로 확인된다** —
    /// "말하고 있는 것 같은데 확신이 없다"를 없애는 것이 목적이다.
    private void OnDrawGizmos()
    {
        if (!showLabels || !enabled || !Application.isPlaying)
        {
            return;
        }

        IReadOnlyList<Resident> residents = ResidentRegistry.Residents;

        for (int i = 0; i < residents.Count; i++)
        {
            Resident resident = residents[i];

            if (resident == null || !TryResolveColor(resident, out Color color))
            {
                continue;
            }

            ResidentConversation session = resident.Conversation;
            ResidentAgent agent = resident.Agent;
            string clip = agent != null ? agent.CurrentClipName : null;

            string role = resident.IsEmerging
                ? "등장"
                : session == null
                    ? resident.IsDancing ? "춤" : "귀가"
                    : session.Phase == ResidentConversation.ConversationPhase.Talking
                        ? session.IsSpeaker(resident) ? "화자" : "청자"
                        : session.Phase.ToString();

            UnityEditor.Handles.color = color;
            UnityEditor.Handles.Label(
                resident.transform.position + Vector3.up * (linkHeight + 0.5f),
                $"{role}\n{clip}");
        }
    }
#endif
}
