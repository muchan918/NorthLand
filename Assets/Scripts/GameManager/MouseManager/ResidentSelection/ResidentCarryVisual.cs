using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

/// 들려 있는 주민을 **어디에 그릴지**만 맡는다(§8.1 R10).
///
/// 누구를 들었는가 · 어디에 배치되는가는 <see cref="ResidentDragCoordinator"/>가 계속 소유한다. 이쪽은
/// 그 목록을 받아 **탑으로 쌓아 커서를 따라가게 하고, 놓는 순간 터뜨려 떨어뜨린다.** 착지가 끝나면
/// <see cref="OnLanded"/>로 알릴 뿐 스포너·경영 컨트롤러를 직접 부르지 않는다 — 도메인 복귀는 코디네이터의 몫이다.
///
/// ── 문서와 달라진 점 ──────────────────────────────────────────
///
/// §8.1은 R10을 **"커서를 따라오는 공중 행렬"**(선두는 커서를, 나머지는 앞사람 뒤를)로 적어 뒀는데,
/// 실제로 채택한 것은 **수직 스택(탑)**이다. 행렬은 최대 축소에서 25px짜리 점이 줄지어 서는 그림이라
/// 몇 명인지 읽히지 않는 반면(§8.5), 탑은 **높이 하나로 인원이 읽힌다.** §8.3이 비워 뒀던
/// "빈 땅에 내려놓았을 때 그 많은 주민을 어떻게 하는가"도 여기서 **흩뿌려 착지**로 답한다.
///
/// ── 좌표 소유권 ────────────────────────────────────────────────
///
/// 들려 있는 동안 주민의 위치는 **전적으로 이 클래스가 소유한다**(§8.2 확정). `NavMeshAgent`와 BT는
/// <see cref="ResidentSpawner.TryCarry"/>가 꺼 두므로 끼어드는 주인이 없다. 착지해서 되돌려 준 뒤에야
/// 소유권이 `NavMeshAgent`로 돌아간다 — 둘 다 켜 두면 Agent가 지면으로 끌어내려 탑이 서지 않는다.
///
/// ── 왜 씬 컴포넌트가 아닌가 ────────────────────────────────────
///
/// <see cref="ResidentDragCoordinator"/>가 런타임에 자기 오브젝트에 붙인다(정본 씬을 건드리지 않는 같은 규칙).
/// 대신 **플레이 중 인스펙터에서 값을 만질 수 있다** — 부양 높이·간격·낙하는 눈으로 맞추는 수치라
/// 상수로 박아 두면 한 번 고칠 때마다 도메인 리로드를 기다리게 된다. 값이 정해지면 아래 기본값으로 옮겨 적는다.
[DisallowMultipleComponent]
public class ResidentCarryVisual : MonoBehaviour
{
    [Header("부양 (§8.1)")]
    [Tooltip("커서 지점에서 탑 첫 칸의 **발**까지 띄우는 높이. 0이면 맨 아래 주민의 발이 커서에 정확히 닿고, " +
             "음수면 커서가 몸을 파고들어 '쥐고 있는' 느낌이 강해진다. 양수로 키우면 화면상 커서 위로 " +
             "떠 보이지만 그만큼 커서와 벌어진다 — 건물 가림은 이 값이 아니라 아래 occlusionRise가 해결하므로 " +
             "가림 때문에 여기를 키울 필요는 없다.")]
    [SerializeField] private float liftHeight;

    [Tooltip("건물에 가리지 않도록 들어 올리는 높이(월드 Y). **화면 위치는 1픽셀도 변하지 않는다** — " +
             "오쏘 카메라의 시선축을 따라 x·z를 같이 밀어 올리기 때문이다. 250이면 시선축으로 354를 " +
             "확보해 실측상 가장 두꺼운 건물(B1, 최대 314)도 넘어선다. 카메라를 지나칠 만큼 크게 잡아도 " +
             "근평면 앞에서 잘라 내므로 사라지지는 않는다.")]
    [Min(0f)]
    [SerializeField] private float occlusionRise = 250f;

    [Tooltip("탑 한 칸의 높이. 앉은 자세의 몸 높이(약 2.2)보다 작으면 서로 파고든다.")]
    [Min(0.1f)]
    [SerializeField] private float stackSpacing = 2.2f;

    [Tooltip("커서를 따라붙는 속도. 클수록 딱 붙고, 작을수록 늘어진다.")]
    [Min(0.1f)]
    [SerializeField] private float followSharpness = 14f;

    [Tooltip("위층이 아래층보다 느리게 따라오는 정도. 0이면 탑이 통째로 뻣뻣하게 움직이고, " +
             "키우면 커서를 돌릴 때 위쪽이 뒤로 처지며 휜다.")]
    [Min(0f)]
    [SerializeField] private float levelLag = 0.25f;

    [Tooltip("들린 주민이 카메라 쪽으로 돌아서는 속도(초당 도). 탑이 제각각 다른 방향을 보면 " +
             "쌓인 것이 아니라 흩어진 것으로 읽힌다.")]
    [Min(0f)]
    [SerializeField] private float faceTurnSpeed = 540f;

    [Header("낙하 (§8.3)")]
    [Tooltip("터질 때 위로 튀어 오르는 초기 속도. 0이면 그냥 미끄러져 내린다.")]
    [Min(0f)]
    [SerializeField] private float burstUpSpeed = 3.5f;

    [Tooltip("낙하 중력. 클수록 짧고 무겁게 떨어진다.")]
    [Min(0.1f)]
    [SerializeField] private float gravity = 22f;

    [Tooltip("착지 지점을 흩뿌리는 반경. 인원이 많을수록 바깥쪽까지 쓴다.")]
    [Min(0f)]
    [SerializeField] private float scatterRadius = 2f;

    [Tooltip("흩뿌린 지점에서 NavMesh를 찾을 반경. 못 찾으면 들었던 자리로 돌려보낸다.")]
    [Min(0.1f)]
    [SerializeField] private float landingSnapDistance = 3f;

    [Tooltip("착지 몇 초 전에 미리 일어서기 시작할지. 0이면 닿는 순간 자세가 바뀌어 몸이 툭 내려앉는다.")]
    [Min(0f)]
    [SerializeField] private float standUpLead = 0.18f;

    [Tooltip("들어 올릴 때 앉은 자세로 넘어가는 크로스페이드 시간(초).")]
    [Min(0f)]
    [SerializeField] private float liftFadeSeconds = 0.15f;

    /// 착지가 끝났다 — (주민, 착지 지점). 코디네이터가 이 신호로 스포너에 되돌려 준다.
    ///
    /// **연출이 도메인을 직접 부르지 않는 유일한 이유가 이것이다.** 여기서 `ResidentSpawner`를 부르면
    /// "인원 회계는 코디네이터가 게이트웨이로만 만진다"는 §8의 경계가 무너진다.
    public event Action<Resident, Vector3> OnLanded;

    /// 앉은 자세는 발이 GameObject 원점에서 이만큼 떠 있다(Marshie 실측 0.61 — Idle은 0.08).
    /// 빼 주지 않으면 <see cref="liftHeight"/>가 0인데도 탑이 그만큼 위에서 시작한다.
    private const float SittingFootOffset = 0.61f;

    /// 가림 방지로 밀어 올릴 때 근평면 앞에 남겨 둘 여유(월드 유닛). 탑이 위로 쌓이는 만큼과
    /// 몸 크기를 덮을 정도면 된다.
    private const float NearPlaneMargin = 30f;

    /// 커서 밑 지면을 찾을 때 높이를 훑어 내리는 간격. 촘촘할수록 정확하지만 표본 수가 는다 —
    /// 어차피 명중한 뒤 한 번 정밀화하므로 이 값이 최종 정확도를 정하지는 않는다.
    private const float GroundMarchStep = 2f;

    /// 훑는 각 단계에서 NavMesh를 찾을 반경. 간격의 절반보다 커야 층 사이에 빈틈이 안 생긴다.
    private const float GroundMarchRadius = 2.5f;

    /// 훑을 높이 범위에 위아래로 더할 여유. 베이크 경계에 딱 붙은 면을 놓치지 않게 한다.
    private const float GroundMarchPadding = 4f;

    /// NavMesh를 통째로 못 찾았을 때 들린 자리 기준으로 훑을 범위(위아래 각각).
    private const float GroundMarchFallbackRange = 60f;

    /// 탑에 올라가 있거나 떨어지는 중인 주민 하나.
    private sealed class Held
    {
        public Resident Resident;

        /// 들기 직전의 자리. **NavMesh 위가 보장된 유일한 지점**이라 착지 실패의 폴백이 된다.
        public Vector3 Origin;

        /// 탑에서의 **논리 위치**. 가림 방지용 시선축 오프셋은 여기 섞지 않는다 —
        /// 섞으면 낙하가 카메라 쪽 수백 유닛 밖에서 시작해 화면 밖에서 날아 들어온다.
        public Vector3 Position;

        // ── 그림자 ──
        //
        // 들려 있는 동안 그림자를 끈다. 시선축으로 밀어 올린 몸이 그대로 그림자를 드리우면
        // **커서에서 수십 유닛 떨어진 허공에 그림자만 따로 논다.**
        public Renderer[] Renderers;
        public ShadowCastingMode[] ShadowModes;

        // ── 낙하 ──
        public Vector3 FallFrom;
        public Vector3 FallTo;
        public Quaternion Facing;
        public float Velocity0;
        public float Duration;
        public float Elapsed;
        public bool StoodUp;
    }

    private readonly List<Held> _carried = new();
    private readonly List<Held> _falling = new();

    /// 커서가 가리키던 마지막 지면 지점. 카메라나 NavMesh를 못 잡는 프레임에 탑이 원점으로 튀지 않게 한다.
    private Vector3 _lastGround;

    /// 커서 밑 지면을 훑을 높이 범위. NavMesh 전체의 y 범위이고, **드래그 시작에 한 번만** 잰다
    /// (<see cref="NavMesh.CalculateTriangulation"/>이 1.78ms라 매 프레임 부를 수 없다).
    private float _marchTop;
    private float _marchBottom;

    /// 흩뿌리기 각도의 시작점. 드래그마다 새로 뽑아 **같은 인원이 늘 같은 모양으로 떨어지지 않게** 한다.
    private float _scatterPhase;

    public int CarriedCount => _carried.Count;

    // ── 들기 ──────────────────────────────────────────────────────────

    /// 주민 하나를 탑 맨 위에 올린다. 스포너가 이미 carry 모드로 바꿔 둔 뒤에 불린다.
    public void Lift(Resident resident)
    {
        if (resident == null) return;

        // 이번 드래그의 첫 사람이다. 커서 지점을 아직 한 번도 못 풀었을 수 있으므로 들린 자리를 기준으로 두고,
        // 지면을 훑을 높이 범위도 여기서 한 번 잰다.
        if (_carried.Count == 0)
        {
            _lastGround = resident.transform.position;
            CaptureMarchRange(resident.transform.position.y);
        }

        var held = new Held
        {
            Resident = resident,
            Origin = resident.transform.position,
            Position = resident.transform.position,
        };

        SuppressShadows(held);
        _carried.Add(held);

        resident.Agent?.EnterCarriedPose(liftFadeSeconds);
    }

    /// 건물이 받아 갔다 — 연출 없이 목록에서만 뺀다. 몸을 감추는 것은 스포너가 한다(§3.2 뿅).
    ///
    /// 그림자는 되돌려 놓는다. 이 주민은 풀로 돌아가 다음 아침에 재사용되므로, 꺼 둔 채로 놓으면
    /// **그날부터 그림자 없이 걸어 다닌다.**
    public void Consume(Resident resident) => RestoreShadows(TakeHeld(resident));

    /// 들고 있던 전원을 **연출 없이** 들었던 자리로 돌려보낸다(밤 전환 · 드래그 재시작 방어).
    ///
    /// 떨어지는 중인 사람은 건드리지 않는다 — 착지 지점이 이미 NavMesh 위로 정해져 있어 그대로 두는 편이
    /// 안전하고, 밤이면 착지 직후 BT가 스스로 귀가 브랜치로 넘어간다(§8.2).
    public void AbortAll()
    {
        for (int i = 0; i < _carried.Count; i++)
        {
            Held held = _carried[i];

            RestoreShadows(held);

            if (held.Resident != null) OnLanded?.Invoke(held.Resident, held.Origin);
        }

        _carried.Clear();
    }

    /// 씬이 바뀌어 주민이 이미 파괴됐다. 되돌릴 대상이 없으므로 목록만 버린다(WL-033 계열).
    public void Clear()
    {
        _carried.Clear();
        _falling.Clear();
    }

    // ── 놓기 ──────────────────────────────────────────────────────────

    /// 탑을 터뜨린다. **떨어질 곳을 먼저 정하고** 그 지점으로 던진다(§8.1 착지 규칙).
    ///
    /// 순서가 요점이다 — 낙하가 끝난 뒤에 NavMesh로 스냅하면 착지 위치가 눈앞에서 미끄러진다.
    /// 반대로 먼저 정해 두면 물 위·절벽에 놓아도 도착점이 늘 유효하다.
    public void Burst(IReadOnlyList<Resident> residents)
    {
        if (residents == null || residents.Count == 0) return;

        // 손을 뗀 **이 프레임의** 커서 지점을 쓴다. 직전 LateUpdate의 값을 재활용하면 한 프레임만큼
        // 뒤처진 자리에 떨어지는데, 빠르게 움직이던 커서에서는 그 차이가 보인다.
        Vector3 groundPoint = ResolveGroundPoint();

        _scatterPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

        for (int i = 0; i < residents.Count; i++)
        {
            Held held = TakeHeld(residents[i]);

            if (held == null) continue;

            StartFall(held, ResolveLanding(groundPoint, i, residents.Count, held.Origin));
        }
    }

    /// 흩뿌릴 한 점을 정해 NavMesh 위로 끌어온다.
    ///
    /// 각도를 황금각으로 돌린다 — 인원이 몇이든 고르게 벌어진다. 무작위로 뿌리면 둘이 같은 자리에
    /// 겹치는 경우가 흔하고, 열 명이 한 점에 떨어지는 것이 애초에 §8.3이 걱정하던 그림이다.
    private Vector3 ResolveLanding(Vector3 groundPoint, int index, int count, Vector3 fallback)
    {
        float angle = index * 137.507764f * Mathf.Deg2Rad + _scatterPhase;

        // √ 비례로 반경을 키워야 안쪽과 바깥쪽의 밀도가 같아진다(선형으로 키우면 가운데가 빈다).
        float radius = count <= 1 ? 0f : scatterRadius * Mathf.Sqrt((index + 1f) / count);

        Vector3 candidate = groundPoint + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, landingSnapDistance, NavMesh.AllAreas))
        {
            return hit.position;
        }

        // 흩뿌린 자리가 NavMesh 밖이다(좁은 길·가장자리). **커서 지점 자체로 모은다** — 겹쳐 떨어지더라도
        // 놓은 자리에 놓이는 편이 낫다. 들었던 자리로 반송하면 플레이어에게는 "거절당했다"로 읽힌다.
        if (NavMesh.SamplePosition(groundPoint, out NavMeshHit center, landingSnapDistance, NavMesh.AllAreas))
        {
            return center.position;
        }

        // 놓은 자리 일대가 통째로 NavMesh 밖이다(물 위·절벽). 그때만 들었던 자리로 되돌린다.
        return fallback;
    }

    private void StartFall(Held held, Vector3 landing)
    {
        held.FallFrom = held.Position;
        held.FallTo = landing;
        held.Elapsed = 0f;
        held.StoodUp = false;

        // 같은 속도로 튀면 열 명이 한 몸처럼 움직인다. 개체마다 흩어 놓는다.
        held.Velocity0 = burstUpSpeed * UnityEngine.Random.Range(0.8f, 1.2f);

        float drop = Mathf.Max(0.01f, held.FallFrom.y - landing.y);

        // v0로 튀어 오른 뒤 g로 떨어져 **정확히** 착지 높이에 닿는 시각.
        // 0.5·g·T² − v0·T − drop = 0 의 양근이다. 높이 뜬 사람일수록 오래 나는 것이 저절로 성립해,
        // 탑이 아래부터 차례로 벗겨지듯 흩어진다.
        held.Duration = (held.Velocity0 + Mathf.Sqrt(held.Velocity0 * held.Velocity0 + 2f * gravity * drop)) / gravity;

        Vector3 outward = landing - held.FallFrom;
        outward.y = 0f;

        held.Facing = outward.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(outward.normalized)
            : held.Resident.transform.rotation;

        _falling.Add(held);
    }

    // ── 매 프레임 ─────────────────────────────────────────────────────

    private void LateUpdate()
    {
        float dt = Time.deltaTime;

        if (dt <= 0f) return;

        UpdateCarried(dt);
        UpdateFalling(dt);
    }

    private void UpdateCarried(float dt)
    {
        if (_carried.Count == 0) return;

        // 발이 커서에 닿는 높이를 기준으로 삼는다 — 앉은 자세가 몸을 이미 0.61 띄워 놓기 때문이다.
        Vector3 basePoint = ResolveGroundPoint() + Vector3.up * (liftHeight - SittingFootOffset);

        Vector3 push = ResolveOcclusionPush();
        bool hasFacing = TryResolveFacing(out Quaternion facing);

        for (int i = 0; i < _carried.Count; i++)
        {
            Held held = _carried[i];

            if (held.Resident == null) continue;

            Vector3 target = basePoint + Vector3.up * (i * stackSpacing);

            // 층이 높을수록 느리게 따라온다. 프레임률과 무관한 수렴이라 60/144fps에서 감각이 같다.
            float sharpness = followSharpness / (1f + i * levelLag);

            held.Position = Vector3.Lerp(held.Position, target, 1f - Mathf.Exp(-sharpness * dt));

            Transform body = held.Resident.transform;

            // 논리 위치 + 가림 방지 오프셋. 오프셋은 **화면 위치를 바꾸지 않으므로** 커서에 붙어 있는
            // 그림은 그대로고, 깊이만 카메라 앞으로 나온다.
            body.position = held.Position + push;

            if (hasFacing)
            {
                body.rotation = Quaternion.RotateTowards(body.rotation, facing, faceTurnSpeed * dt);
            }
        }
    }

    private void UpdateFalling(float dt)
    {
        Vector3 push = ResolveOcclusionPush();

        for (int i = _falling.Count - 1; i >= 0; i--)
        {
            Held held = _falling[i];

            if (held.Resident == null)
            {
                _falling.RemoveAt(i);
                continue;
            }

            held.Elapsed += dt;

            Transform body = held.Resident.transform;

            if (held.Elapsed >= held.Duration)
            {
                _falling.RemoveAt(i);

                // 오프셋을 남김없이 되돌린 자리가 곧 착지 지점이다.
                body.position = held.FallTo;
                RestoreShadows(held);

                OnLanded?.Invoke(held.Resident, held.FallTo);

                continue;
            }

            float u = held.Elapsed / held.Duration;

            // 수평은 앞부분에서 확 벌어졌다가 잦아든다 — 그래야 "터졌다"로 읽힌다. 세로는 그냥 포물선이다.
            float spread = 1f - (1f - u) * (1f - u) * (1f - u);

            Vector3 position = Vector3.Lerp(held.FallFrom, held.FallTo, spread);
            position.y = held.FallFrom.y + held.Velocity0 * held.Elapsed - 0.5f * gravity * held.Elapsed * held.Elapsed;

            // 가림 방지 오프셋을 **끝에 몰아서** 푼다. 푸는 동안에도 화면 위치는 변하지 않으므로
            // 눈에 보이는 것은 "언제부터 건물에 가려지기 시작하는가"뿐이다 — 착지 직전에 몰면
            // 그 전환이 지면에 닿는 순간과 겹쳐 눈에 띄지 않는다.
            body.position = position + push * (1f - u * u * u);
            body.rotation = Quaternion.RotateTowards(body.rotation, held.Facing, faceTurnSpeed * dt);

            // 착지 **직전에** 일어서기 시작한다. 앉은 자세는 발이 원점에서 0.61 떠 있어(Marshie 실측),
            // 닿는 순간에 바꾸면 몸이 그만큼 툭 내려앉는다. 겹쳐 섞으면 그 차이가 낙하에 흡수된다.
            if (!held.StoodUp && held.Elapsed >= held.Duration - standUpLead)
            {
                held.StoodUp = true;
                held.Resident.Agent?.ReturnToLocomotion(standUpLead);
            }
        }
    }

    // ── 조회 ──────────────────────────────────────────────────────────

    /// 커서가 가리키는 지면 지점.
    ///
    /// ⚠ **물리 레이캐스트로 짚을 수 없다** — 경영 공간의 지면에는 콜라이더가 아예 없다(웨이포인트 밑으로
    /// 레이를 쏴 확인했다. 콜라이더는 전투 타일 90 · 건물 6 · Ground 12뿐이고 마을 바닥은 그중에 없다).
    /// 그래서 `MouseManager`의 배치 표면 경로를 쓰지 못하고, **들어 올린 높이의 수평면**과 커서 광선을
    /// 교차시킨다.
    ///
    /// 그 교차점은 평평한 지면을 가정한 값이라 언덕에서 어긋난다 → NavMesh로 한 번 끌어와 **실제 높이**를
    /// 되찾는다. 못 찾으면(물 위·절벽) 교차점을 그대로 쓴다 — 공중이므로 NavMesh 밖으로도 끌고 다닐 수
    /// 있다는 것이 §8.1의 규칙이고, 유효성은 착지할 때만 따진다.
    ///
    /// 커서 좌표는 `MouseManager`에서 받는다 — `Mouse.current`를 직접 읽지 않는다(입력 단일 창구 계약).
    private Vector3 ResolveGroundPoint()
    {
        MouseManager mouse = MouseManager.Instance;
        Camera camera = Camera.main;

        if (mouse == null || camera == null) return _lastGround;

        Ray ray = camera.ScreenPointToRay(mouse.PointerPosition);

        if (TryMarchToNavMesh(ray, out Vector3 ground))
        {
            _lastGround = ground;
        }
        else if (TryIntersectHeight(ray, _lastGround.y, out Vector3 airborne))
        {
            // NavMesh 밖이다(물 위·절벽). 마지막으로 알던 높이의 평면으로 커서를 따라간다 —
            // 공중이므로 끌고 다니는 것 자체는 막지 않는 것이 §8.1의 규칙이고, 유효성은 착지할 때만 따진다.
            _lastGround = airborne;
        }

        return _lastGround;
    }

    /// 커서 광선이 **처음 만나는 NavMesh 면**을 위에서부터 훑어 찾는다.
    ///
    /// ⚠ **높이를 하나로 가정하면 안 된다.** 종전에는 들어 올린 자리의 높이로 수평면을 세워 교차시켰는데,
    /// 45° 카메라에서는 **높이차 1당 수평으로 1씩 어긋난다.** 이 맵의 NavMesh는 y −18.6 ~ +28.0(높이차 46.6)에
    /// 여러 층으로 깔려 있어서, 낮은 곳에서 든 주민을 높은 길에 놓으려 하면 교차점이 27유닛 밖에 찍히고
    /// 탐색이 실패해 **멀쩡한 길인데도 들었던 자리로 반송됐다.**
    ///
    /// **탐색 반경을 키우는 것은 해결이 아니다** — 반경 16이면 "찾기는" 하지만 겨눈 곳에서 30유닛 떨어진
    /// **다른 층**을 집어 온다(실측). 층을 구분하려면 높이를 훑는 수밖에 없다.
    ///
    /// 위에서부터 내려오는 순서가 핵심이다. **먼저 만나는 면이 곧 커서 밑에 보이는 면**이라, 다리나
    /// 고가 통로 위를 겨누면 그 위가 잡히고 밑 지면이 잡히지 않는다. 26단계에 0.03ms다(실측).
    private bool TryMarchToNavMesh(Ray ray, out Vector3 point)
    {
        point = default;

        for (float height = _marchTop; height >= _marchBottom; height -= GroundMarchStep)
        {
            if (!TrySampleAtHeight(ray, height, GroundMarchRadius, out Vector3 found)) continue;

            // 찾은 높이로 평면을 다시 세워 한 번 정밀화한다 — 훑는 간격만큼의 오차가 여기서 준다.
            point = TrySampleAtHeight(ray, found.y, GroundMarchStep, out Vector3 refined) ? refined : found;

            return true;
        }

        return false;
    }

    /// 광선을 <paramref name="height"/>의 수평면과 교차시키고, 그 자리에서 NavMesh를 찾는다.
    private static bool TrySampleAtHeight(Ray ray, float height, float radius, out Vector3 point)
    {
        point = default;

        if (!TryIntersectHeight(ray, height, out Vector3 candidate)) return false;

        if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, radius, NavMesh.AllAreas)) return false;

        point = hit.position;

        return true;
    }

    private static bool TryIntersectHeight(Ray ray, float height, out Vector3 point)
    {
        point = default;

        var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));

        if (!plane.Raycast(ray, out float distance)) return false;

        point = ray.GetPoint(distance);

        return true;
    }

    /// 훑을 높이 범위를 잰다. **드래그 시작에 한 번만** 부른다 — 이 호출이 1.78ms라 매 프레임은 안 된다.
    /// 영토 확장으로 NavMesh가 다시 구워져도 다음 드래그에서 새로 잰다.
    ///
    /// `NavMeshSurface.navMeshData.sourceBounds`를 쓰지 않는 이유: 그쪽은 **베이크에 넣은 소스 지오메트리의**
    /// 경계라 실제 결과와 다르다 — 이 씬에서 상한을 22로 보고하는데 실제 NavMesh는 28까지 있다(실측).
    private void CaptureMarchRange(float fallbackHeight)
    {
        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();

        if (triangulation.vertices == null || triangulation.vertices.Length == 0)
        {
            _marchTop = fallbackHeight + GroundMarchFallbackRange;
            _marchBottom = fallbackHeight - GroundMarchFallbackRange;

            return;
        }

        float min = float.MaxValue;
        float max = float.MinValue;

        for (int i = 0; i < triangulation.vertices.Length; i++)
        {
            float y = triangulation.vertices[i].y;

            if (y < min) min = y;
            if (y > max) max = y;
        }

        _marchTop = max + GroundMarchPadding;
        _marchBottom = min - GroundMarchPadding;
    }

    /// 건물에 가리지 않게 밀어 올릴 오프셋.
    ///
    /// **왜 월드 Y로 그냥 올리지 않는가.** 경영 카메라는 45° 기울어 있어 Y로 h만큼 올리면 화면에서도
    /// 0.71h만큼 위로 밀린다 — 가림을 피할 만큼 올리면 그만큼 커서와 벌어져서 "마우스에 붙어 있다"가
    /// 깨진다. 실제로 첫 판에서 그 두 증상이 같이 나왔다.
    ///
    /// **오쏘 카메라에서 시선축을 따라 움직이면 화면 위치가 전혀 변하지 않는다**(원근 나눗셈이 없어
    /// 깊이가 크기에도 위치에도 영향을 주지 않는다 — §8.5가 "카메라에 가깝게 띄운다는 효과가 0"이라고
    /// 적어 둔 바로 그 성질을, 여기서는 반대로 이용한다). 그래서 시선축을 그대로 오프셋으로 쓰고,
    /// 크기만 "월드 Y로 얼마나 올라가는가"로 환산해 받는다.
    private Vector3 ResolveOcclusionPush()
    {
        if (occlusionRise <= 0f) return Vector3.zero;

        Camera camera = Camera.main;

        if (camera == null) return Vector3.zero;

        Vector3 toCamera = -camera.transform.forward;

        // 카메라가 수평이면 시선축에 Y 성분이 없어 환산이 불가능하다. 경영 카메라는 45° 고정이지만,
        // 다른 카메라가 잡히는 씬에서 0으로 나누지 않도록 막는다.
        if (Mathf.Abs(toCamera.y) < 0.01f) return Vector3.zero;

        float distance = occlusionRise / toCamera.y;

        // ⚠ **카메라를 지나치면 근평면에 잘려 통째로 사라진다.** 들고 있던 주민이 소리 없이 없어지는
        //   것은 가려지는 것보다 훨씬 나쁜 실패라, 앵커와 카메라 사이 거리 안쪽으로 잘라 둔다.
        //   경영 카메라는 지면에서 600 넘게 떨어져 있어 평소에는 걸리지 않는다.
        float toNear = Vector3.Dot(_lastGround - camera.transform.position, camera.transform.forward)
                       - camera.nearClipPlane - NearPlaneMargin;

        return toCamera * Mathf.Clamp(distance, 0f, Mathf.Max(0f, toNear));
    }

    /// 들려 있는 동안 그림자를 끈다.
    ///
    /// 시선축으로 밀어 올린 몸은 **월드에서는 수십 유닛 위·옆에 있다.** 그림자는 화면이 아니라 월드에
    /// 드리우므로, 켜 둔 채로 두면 커서에서 한참 떨어진 바닥에 그림자만 따로 떠다닌다.
    /// 들린 주민의 발밑 표시는 별도 과제로 남아 있다(요청상 이번 범위 밖).
    private static void SuppressShadows(Held held)
    {
        held.Renderers = held.Resident.GetComponentsInChildren<Renderer>(true);
        held.ShadowModes = new ShadowCastingMode[held.Renderers.Length];

        for (int i = 0; i < held.Renderers.Length; i++)
        {
            held.ShadowModes[i] = held.Renderers[i].shadowCastingMode;
            held.Renderers[i].shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    /// 꺼 뒀던 그림자를 **원래 값 그대로** 되돌린다. 일괄로 On을 넣지 않는 이유는, 프리팹이 처음부터
    /// 꺼 둔 렌더러(눈·입 같은 장식)를 켜 버리면 그때부터 없던 그림자가 생기기 때문이다.
    private static void RestoreShadows(Held held)
    {
        if (held?.Renderers == null) return;

        for (int i = 0; i < held.Renderers.Length; i++)
        {
            if (held.Renderers[i] != null) held.Renderers[i].shadowCastingMode = held.ShadowModes[i];
        }

        held.Renderers = null;
        held.ShadowModes = null;
    }

    /// 들린 주민이 바라볼 방향(카메라 정면). 카메라가 없으면 회전에 손대지 않는다 —
    /// 기본값으로 돌려 버리면 전원이 북쪽을 보며 홱 도는 그림이 된다.
    private static bool TryResolveFacing(out Quaternion facing)
    {
        facing = Quaternion.identity;

        Camera camera = Camera.main;

        if (camera == null) return false;

        Vector3 toCamera = -camera.transform.forward;
        toCamera.y = 0f;

        if (toCamera.sqrMagnitude < 0.0001f) return false;

        facing = Quaternion.LookRotation(toCamera.normalized);

        return true;
    }

    private Held TakeHeld(Resident resident)
    {
        for (int i = 0; i < _carried.Count; i++)
        {
            if (!ReferenceEquals(_carried[i].Resident, resident)) continue;

            Held held = _carried[i];

            _carried.RemoveAt(i);

            return held;
        }

        return null;
    }
}
