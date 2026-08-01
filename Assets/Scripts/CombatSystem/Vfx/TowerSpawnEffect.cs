using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace NorthLand.Combat
{
    /// 타워 등장 연출(#264): 공중의 하얀 입자가 배치 지점으로 수렴 → 타워가 튀어나오며 바닥에 충격파 링.
    /// 일반 배치와 합성 배치(#265)가 **같은 함수**를 타므로 두 연출은 구분되지 않는다.
    ///
    /// ## 이 클래스의 계약: 타워를 모른다
    /// 진입점이 받는 것은 `Transform` 하나와 풋프린트 크기뿐이다 — `Tower`도 `TowerAsset`도 메시도 받지
    /// 않는다. 대상에서 읽는 것은 `Renderer.bounds`(월드 AABB)와 `localScale`뿐이고, 나머지 수치는 전부
    /// 그 bounds/풋프린트에서 유도한 **비율**이다. 길이 상수가 코드에 하나도 없다.
    ///
    /// 왜 이렇게까지 하나: **타워 에셋이 임시라 통째로 교체될 예정이다.** 연출이 특정 메시·머티리얼·계층
    /// 구성을 조금이라도 참조하면 교체와 함께 깨진다. 메시 정점을 읽지 않는 것도 같은 이유로 필수인데,
    /// 프로젝트 FBX 1664개 중 573개가 `isReadable: 0`이고 신규 에셋이 어느 쪽일지 보장이 없다(#264 조사 —
    /// `OutlineSmoothMeshRegistry`가 정확히 같은 문제로 에디터 베이크를 택한 선례가 있다).
    ///
    /// 그 결과 **대상이 타워일 필요조차 없다** — Renderer가 달린 큐브에 그대로 재생된다. 타워 에셋을 보지
    /// 않고 연출을 완성·튜닝할 수 있고, 교체 후에도 멀쩡한지는 크기가 크게 다른 큐브 몇 개면 확인된다.
    ///
    /// 지켜야 할 규칙(깨면 에셋 결합이 되살아난다):
    ///   - `transform.position`이 아니라 `bounds.center`/`bounds.min.y` — 새 에셋의 피벗이 밑면이 아닐 수 있다.
    ///   - `Vector3.one`이 아니라 캡처한 원본 스케일 — 새 에셋 루트 스케일이 1이라는 보장이 없다.
    ///   - 공중 입자는 `bounds`(시각 크기), 바닥 링은 **풋프린트**(논리 크기)에서 유도한다. 바닥 연출은
    ///     그리드 언어라, 홀쭉한 타워에서도 "이 칸을 먹었다"가 링 크기로 읽혀야 한다.
    ///
    /// 입자 구현체는 GameObject + 빌보드 쿼드다. 규모가 동시 수십 개라 instancing 이득이 없고, 수렴은
    /// 좌표 보간이라 attractor가 없는 ParticleSystem으로는 매 프레임 `SetParticles` 개입이 필요하다.
    /// 대신 정말 비싼 것(쿼드 메시·알갱이 텍스처)만 static 공유한다 — 재생 1회당 새로 만드는 것은
    /// 머티리얼 하나뿐이고, 그건 전체 페이드용이라 인스턴스여야 한다(`OnDestroy`에서 파괴).
    [DisallowMultipleComponent]
    public class TowerSpawnEffect : MonoBehaviour
    {
        // 프로젝트 표준 반투명 언릿(RangeCircle·셀 하이라이트·VortexVisual 공용) — URP PC/Mobile 양쪽에서
        // 동작하고 신규 셰이더 에셋이 필요 없다.
        private const string k_Shader = "Sprites/Default";

        // ── 시간(초) ─────────────────────────────────────────────────────────────────
        // 길이가 아니라 시간이므로 절대값이어도 에셋 교체와 무관하다.
        private const float k_ConvergeDuration = 0.45f;
        private const float k_PopDuration = 0.28f;
        private const float k_RingDuration = 0.38f;
        private const float k_FadeInPortion = 0.25f; // 수렴 구간 앞부분 중 입자가 떠오르는 비율
        private const float k_MaxArrivalDelay = 0.3f; // 입자별 출발 시차(전부 같이 움직이면 판박이로 보인다)

        // ── 비율 ─────────────────────────────────────────────────────────────────────
        // 실제 길이는 전부 bounds/풋프린트에 곱해 얻는다. 여기에 월드 단위 상수를 추가하지 말 것.
        // 입자 구름은 대상 **표면에서 일정 두께 떨어진 후광**이다. 두 가지 실패를 함께 피한 형태다:
        //   ① 반경 하나짜리 구 → 반경이 `extents.magnitude`(높이에 지배됨)가 되어 높이 37.7·폭 10.4 타워에서
        //      폭 65짜리 구름이 나온다(실측). 타워를 감싸지 않고 옆으로 흩어진다.
        //   ② `extents`의 배수(타원체) → 비례는 맞지만 **껍질 안쪽이 타워 실루엣에 묻혀** 절반이 안 보이고,
        //      높은 타워에서 Y가 과도하게 늘어난다(실측: 65개 중 눈에 보이는 건 30개쯤).
        // 표면(= extents 방향 성분) + 고정 두께로 두면 홀쭉하든 넓적하든 항상 실루엣 바깥에 균일하게 뜬다.
        // 두께는 풋프린트 기준이라 에셋 교체와 무관하게 "대략 자기 칸 안"이라는 크기 감각이 유지된다.
        private const float k_HaloRatio = 0.55f;   // 후광 두께 = 풋프린트 × 이것
        private const float k_LowestDirY = -0.25f; // 분포 하단(중심보다 살짝 아래까지만 — 지면 아래 금지)

        // 알갱이 크기는 bounds가 아니라 **풋프린트**에서 뽑는다. 타일은 항상 15인데 타워 메시 크기는
        // 제각각이라(현재 프리팹만 봐도 높이 2.0~37.7, 19배), bounds 기준이면 스케일이 어긋난 프리팹에서
        // 알갱이까지 같이 어긋난다. 풋프린트는 그리드가 정하는 값이라 **에셋 교체와 무관하게 불변**이고,
        // 덕분에 모든 타워의 알갱이가 같은 크기로 보여 하나의 시각 언어가 된다(바닥 링과 같은 근거).
        private const float k_GrainSizeRatio = 0.15f;    // 알갱이 한 변 = 풋프린트 × 이것
        private const float k_MaxGrainSizeRatio = 0.4f;  // 줌 하한이 넘지 못하는 상한(같은 기준)
        private const float k_RingRadiusRatio = 0.62f;   // 링 최대 반경 = 풋프린트 × 이것
        private const float k_SwirlDegrees = 160f;       // 수렴하며 Y축으로 감기는 각도("슈우웅")
        private const float k_PopOvershoot = 1.70158f;   // back-out 이징 표준 계수

        // 입자 개수는 bounds 부피의 세제곱근(= 특성 길이)에 비례. 작은 타워는 적게, 큰 타워는 많이.
        // 크기와 달리 개수는 bounds가 맞다 — 큰 타워를 같은 개수로 채우면 성기게 보인다.
        private const float k_CountPerUnit = 4f;
        private const int k_MinCount = 30;
        private const int k_MaxCount = 90;

        // 줌 보정. 입자는 월드 공간 쿼드라 타워와 **함께** 작아지므로 비례 보정은 필요 없다 — 필요한 건
        // 줌아웃(orthographicSize 300)에서 서브픽셀로 사라지지 않게 막는 화면 기준 하한이다. 직교 카메라는
        // 월드 크기 ↔ 화면 크기가 orthographicSize에 선형이라 하한도 거기에 비례시킨다(줌 범위 70~300).
        // OutlineInteractionDriver가 아웃라인 폭을 같은 값으로 스케일하는 것과 같은 성격의 처리다.
        private const float k_MinSizePerOrthoSize = 0.017f; // 1080p 기준 size 70 → 9px, 300 → 9px

        // 재생마다 새로 만들지 않는 공유물. 텍스처는 절차 생성이라 특히 아깝다(VortexVisual과 같은 규약).
        private static Mesh s_quad;
        private static Texture2D s_grain;

        // 대상별 진행 중 연출. **이 연출은 대상 루트의 localScale을 배타적으로 소유하므로**(§9.3.2)
        // 같은 Transform에 두 번 겹치면 두 번째가 **이미 0이 된 스케일을 원본으로 캡처**해 타워가 영구히
        // 보이지 않게 된다 — 이 클래스가 최악으로 지목한 실패 모드가 정확히 이 경로로 열린다.
        // 배치(`TowerPlacer`)와 합성(#265)이 같은 대상에 각자 재생을 걸 수 있어 진입점에서 막는다.
        private static readonly Dictionary<Transform, TowerSpawnEffect> s_active = new(ReferenceComparer.Instance);

        /// Transform을 **참조 동일성**으로만 다루는 비교자. Unity의 오버로드를 쓰지 않는 이유는
        /// `Object.Equals`가 **파괴된 객체를 서로 같다고 판정**하기 때문이다(둘 다 null 취급).
        /// 연출 도중 타워가 사라지면(합성 소모·철거) 죽은 키가 호스트 정리 전까지 잠시 남는데, 그 키가
        /// 다른 죽은 키와 해시 버킷을 공유하면 엉뚱한 항목이 매칭될 수 있다.
        ///
        /// 관측된 버그를 고친 게 아니라 **의미론을 맞춰 둔 것**이다 — 실측으로 기본 비교자도 파괴 후
        /// 조회는 정상이었고 `GetHashCode`도 안정적이었다(파괴 전후 동일). 위험은 버킷 충돌 시의
        /// 오매칭뿐이고 드물다. 아래 `OnDestroy`의 `ReferenceEquals(_target, null)` 쪽이 실제 누수 방지선이다.
        private sealed class ReferenceComparer : IEqualityComparer<Transform>
        {
            public static readonly ReferenceComparer Instance = new();
            public bool Equals(Transform a, Transform b) => ReferenceEquals(a, b);
            public int GetHashCode(Transform o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }

        private readonly List<Transform> _particles = new();
        private readonly List<Vector3> _starts = new();
        private readonly List<Vector3> _scales = new();
        private readonly List<float> _delays = new();

        private Material _material;
        private Transform _target;
        private Vector3 _originalScale;
        private bool _scaleCaptured;
        private bool _superseded; // 같은 대상에 새 연출이 들어와 자리를 내줬다

        /// 배치 확정 직후 호출(fire-and-forget). 연출은 **시각 전용·논블로킹**이라 호출자는 기다리지 않는다 —
        /// 타워는 이 시점에 이미 논리적으로 완성돼 있고, 연출은 그 위에 얹히기만 한다.
        ///
        /// ⚠ 재생 중 **대상 루트의 `localScale`을 배타적으로 소유한다**(0 → 원본). 이 창(약 0.45초) 동안
        /// 다른 시스템이 그 값을 쓰거나 캡처하면 안 된다 — 자세한 계약은 `TowerPlacement.md` §9.3.2.
        public static void Play(Transform target, float footprintSize)
            => PlayAsync(target, footprintSize).Forget();

        /// 연출 종료까지 기다려야 하는 호출자용(#265 합성 시퀀스의 마지막 구간).
        /// ct는 호출자 수명 토큰. 연출 자신의 수명과 합쳐지므로 어느 쪽이 끊겨도 UniTask가 남지 않는다.
        ///
        /// ⚠ `Play`와 같은 스케일 배타 소유 계약이 적용된다. 같은 대상에 이미 재생 중이면 **그 연출을 먼저
        /// 끝내고 인계받는다** — 배치와 합성이 같은 타워에 각자 재생을 걸어도 스케일이 오염되지 않는다.
        public static async UniTask PlayAsync(Transform target, float footprintSize, CancellationToken ct = default)
        {
            if (target == null) return;

            // 같은 대상에 재생 중인 연출이 있으면 원본 스케일을 **지금** 되돌리고 자리를 넘겨받는다.
            // `Destroy`만으로는 늦다 — 파괴는 프레임 끝이라 그 사이 우리가 0을 원본으로 캡처한다.
            if (s_active.TryGetValue(target, out TowerSpawnEffect running) && running != null)
            {
                running.Supersede();
                Destroy(running.gameObject);
            }

            // 대상에서 무언가를 읽는 건 이 한 줄이 전부다. 이 뒤로는 순수 좌표 계산이라 에셋을 다시 보지 않는다.
            Bounds bounds = CalculateVisualBounds(target, footprintSize);

            var host = new GameObject("TowerSpawnEffect");
            host.transform.position = bounds.center;
            var effect = host.AddComponent<TowerSpawnEffect>();
            s_active[target] = effect;

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(effect.destroyCancellationToken, ct);
            try
            {
                await effect.RunAsync(target, bounds, footprintSize, linked.Token);
            }
            catch (System.OperationCanceledException)
            {
                // 중도 취소(씬 전환 등)는 정상 경로다. 대상 스케일 원복은 OnDestroy가 책임진다.
            }
            finally
            {
                if (host != null) Destroy(host);
            }
        }

        /// 대상의 월드 AABB. `Renderer.bounds`만 읽으므로 메시가 `isReadable: 0`이든 머티리얼이 무엇이든 무관하다.
        /// public인 이유: #265가 재료 타워의 크기를 같은 규칙으로 재야 하고, 큐브로 연출을 검증할 때도 쓰인다.
        public static Bounds CalculateVisualBounds(Transform target, float footprintSize)
        {
            var bounds = new Bounds();
            bool found = false;

            foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
            {
                // 사거리 원은 타워의 자식으로 생성된다(Tower.ShowRangeCircle). 반경이 타워보다 훨씬 커서
                // 포함하면 bounds가 통째로 부풀고 입자가 화면 밖에서 모여든다 — OutlineHighlight와 같은 제외 규칙.
                if (r.GetComponentInParent<RangeCircle>() != null) continue;
                if (!r.enabled) continue;

                if (!found)
                {
                    bounds = r.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            if (found && bounds.size.sqrMagnitude > 1e-6f) return bounds;

            // Renderer가 없거나 크기가 0인 대상도 연출이 조용히 사라지지 않게 풋프린트로 대체 상자를 만든다.
            // "어떤 에셋이든 동작한다"(#264 완료 기준)의 마지막 폴백 — 눈에 띄게 이상해야 원인을 찾는다.
            float side = Mathf.Max(0.01f, footprintSize);
            return new Bounds(target.position + Vector3.up * (side * 0.5f), Vector3.one * side);
        }

        private async UniTask RunAsync(Transform target, Bounds bounds, float footprintSize, CancellationToken ct)
        {
            _target = target;
            _originalScale = target.localScale; // Vector3.one이 아니라 원본 — 새 에셋의 루트 스케일은 미지수다
            _scaleCaptured = true;
            target.localScale = Vector3.zero;   // 수렴이 끝날 때까지 숨긴다(bounds는 위에서 이미 떴다)

            Camera cam = Camera.main;
            // 직교 카메라라 빌보드 회전은 1회 고정으로 끝난다(#264) — 매 프레임 카메라를 향할 필요가 없다.
            Quaternion billboard = cam != null ? cam.transform.rotation : Quaternion.identity;

            SpawnParticles(bounds, footprintSize, ResolveGrainSize(footprintSize, cam), billboard);
            await ConvergeAsync(bounds.center, ct);
            DespawnParticles();

            // 등장과 바닥 링은 동시에 터진다. 링 중심 y는 시각물의 밑면 — 피벗이 어디든 지면에 붙는다.
            var ground = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            await UniTask.WhenAll(
                PopTargetAsync(ct),
                RingAsync(ground, footprintSize * k_RingRadiusRatio, ct));
        }

        private void SpawnParticles(Bounds bounds, float footprintSize, float size, Quaternion billboard)
        {
            EnsureSharedAssets();

            _material = new Material(Shader.Find(k_Shader)) { name = "TowerSpawnGrain" };
            _material.mainTexture = s_grain;
            _material.color = new Color(1f, 1f, 1f, 0f); // 페이드 인으로 시작

            int count = ResolveCount(bounds);
            float halo = footprintSize * k_HaloRatio;

            for (int i = 0; i < count; i++)
            {
                // 위로 치우친 방향. y를 [-0.25, 1]로 리맵해 대부분은 위, 일부만 중심 살짝 아래에서 출발한다 —
                // 전부 위에 두면 타워를 감싸지 않고 머리 위에 뭉치고, 아래로 열면 지면을 뚫는다.
                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Lerp(k_LowestDirY, 1f, (dir.y + 1f) * 0.5f);
                dir.Normalize();

                // 표면(방향별 반지름) + 후광 두께. 두께에만 난수를 실어 실루엣 안으로 들어가지 않게 한다.
                Vector3 start = bounds.center
                    + Vector3.Scale(dir, bounds.extents)
                    + dir * (halo * Random.Range(0.6f, 1.4f));

                // 아래로 열어둔 분포(k_LowestDirY)가 지면을 뚫으면 그 알갱이는 바닥에 먹혀 그냥 사라진다.
                // 시각물 밑면(bounds.min.y)이 곧 지면이므로 거기에 알갱이 반 크기만큼 띄워 걸러낸다.
                start.y = Mathf.Max(start.y, bounds.min.y + size * 0.5f);
                Vector3 scale = Vector3.one * (size * Random.Range(0.6f, 1.35f));

                var go = new GameObject("Grain");
                go.transform.SetParent(transform, false);
                go.transform.SetPositionAndRotation(start, billboard);
                go.transform.localScale = scale;
                go.AddComponent<MeshFilter>().sharedMesh = s_quad;

                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;

                _particles.Add(go.transform);
                _starts.Add(start);
                _scales.Add(scale);
                _delays.Add(Random.Range(0f, k_MaxArrivalDelay));
            }
        }

        private async UniTask ConvergeAsync(Vector3 destination, CancellationToken ct)
        {
            float elapsed = 0f;
            while (elapsed < k_ConvergeDuration)
            {
                elapsed += Time.unscaledDeltaTime; // 배속·일시정지 무관 — 아래 RingAsync 주석 참고
                float t = Mathf.Clamp01(elapsed / k_ConvergeDuration);

                // 스웜 전체 알파를 한 번에 올린다 — 입자별 알파가 필요 없어 머티리얼 하나로 끝난다.
                _material.color = new Color(1f, 1f, 1f, Mathf.Clamp01(t / k_FadeInPortion));

                for (int i = 0; i < _particles.Count; i++)
                {
                    float p = Mathf.Clamp01((t - _delays[i]) / (1f - _delays[i]));
                    float eased = p * p * p; // ease-in: 떠 있다가 마지막에 빨려든다

                    // 직선 수렴은 밋밋하다. 남은 오프셋을 Y축으로 감아 소용돌이로 빨려들게 한다.
                    Vector3 offset = Quaternion.Euler(0f, k_SwirlDegrees * eased, 0f) * (_starts[i] - destination);

                    _particles[i].position = destination + offset * (1f - eased);
                    _particles[i].localScale = _scales[i] * (1f - eased * eased); // 도착하며 소멸
                }

                await UniTask.Yield(ct);
            }
        }

        private void DespawnParticles()
        {
            foreach (Transform p in _particles)
            {
                if (p != null) Destroy(p.gameObject);
            }
            _particles.Clear();
        }

        // 원본 스케일을 살짝 넘겼다가 되돌아온다 — "뽕!"의 정체는 이 오버슈트다.
        private async UniTask PopTargetAsync(CancellationToken ct)
        {
            float elapsed = 0f;
            while (elapsed < k_PopDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                // 연출 도중 타워가 사라지거나(합성 소모·철거) 같은 대상에 새 연출이 들어올 수 있다.
                // 인계된 뒤에도 계속 쓰면 새 연출이 정한 스케일을 한 프레임 덮는다.
                if (_superseded || _target == null) return;

                _target.localScale = _originalScale * BackOut(Mathf.Clamp01(elapsed / k_PopDuration));
                await UniTask.Yield(ct);
            }

            RestoreTarget(); // 누적 오차 없이 원본 스케일로 확정
        }

        private async UniTask RingAsync(Vector3 ground, float maxRadius, CancellationToken ct)
        {
            // 충격파는 "반지름에 따라 정점을 다시 뽑는 절차적 지오메트리"라 RangeCircle의 용도 그대로다.
            // 채움은 투명, 외곽선만 흰 링으로 쓴다. 타워의 자식이 아니라 월드 루트에 둬서 스케일 0인 동안에도
            // 정상 크기로 보이고, 나중에 타워 bounds 계산에도 끼어들지 않는다.
            RangeCircle ring = RangeCircle.Create(null, Color.clear, Color.white, "TowerSpawnRing");
            ring.transform.position = ground;

            try
            {
                // 세 구간 모두 `unscaledDeltaTime`을 쓴다. 배속·일시정지는 전역 `Time.timeScale`이라
                // (`GameSpeedController.ApplyTimeScale`) 스케일드 시간을 쓰면 **일시정지 중 타워가
                // 스케일 0인 채로 멈춘다** — 이 클래스가 최악으로 지목한 "안 보이는 타워"가 정지 버튼
                // 하나로 재현된다. x4 배속에서 수렴이 0.11초로 줄어 연출이 소실되는 것도 같은 원인이다.
                // 순수 시각·논블로킹 연출이라 게임플레이 타이밍과 무관하다(WL-100/WL-119와 같은 축).
                float elapsed = 0f;
                while (elapsed < k_RingDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / k_RingDuration);
                    float eased = 1f - (1f - t) * (1f - t); // ease-out: 터지듯 퍼지고 잦아든다

                    ring.SetRadius(Mathf.Max(0.01f, maxRadius * eased));
                    ring.SetColors(Color.clear, new Color(1f, 1f, 1f, 1f - t));
                    await UniTask.Yield(ct);
                }
            }
            finally
            {
                if (ring != null) Destroy(ring.gameObject);
            }
        }

        private void OnDestroy()
        {
            // 어떤 경로로 끊겨도(씬 전환·취소·예외) 타워가 스케일 0으로 남지 않게 하는 단일 지점.
            // 연출이 실패해도 게임이 망가지지는 않아야 한다 — 안 보이는 타워가 최악의 실패 모드다.
            RestoreTarget();

            // 내가 등록한 항목만 지운다 — 인계된 경우 레지스트리의 주인은 이미 새 연출이다.
            // **`_target != null`(Unity 오버로드)을 쓰면 안 된다.** 연출 도중 타워가 파괴되면(합성 소모·철거)
            // 가짜 null이 되어 이 분기를 건너뛰고 **항목이 영영 남는다** — 파괴된 Transform을 키로 쥔 채
            // 누적된다. 딕셔너리 조회 자체는 참조 기반이라 가짜 null 키로도 정상 동작하므로,
            // 순수 참조 검사(`ReferenceEquals`)로 판정해 죽은 대상까지 확실히 걷어낸다.
            if (!ReferenceEquals(_target, null)
                && s_active.TryGetValue(_target, out TowerSpawnEffect current) && ReferenceEquals(current, this))
            {
                s_active.Remove(_target);
            }

            if (_material != null) Destroy(_material);
        }

        /// 같은 대상에 새 연출이 들어와 자리를 내준다. 스케일을 즉시 원복하고 남은 구간을 멈춘다.
        private void Supersede()
        {
            RestoreTarget();
            _superseded = true;
        }

        private void RestoreTarget()
        {
            if (!_scaleCaptured || _target == null) return;
            _target.localScale = _originalScale;
            _scaleCaptured = false;
        }

        private static float ResolveGrainSize(float footprintSize, Camera cam)
        {
            float ideal = footprintSize * k_GrainSizeRatio;
            if (cam == null || !cam.orthographic) return ideal;

            // 화면 하한과 풋프린트 상한 사이로 죈다. 하한은 "줌아웃(size 300)에서도 보이게", 상한은
            // "알갱이가 칸을 뒤덮지 않게". 상한이 없으면 줌아웃에서 하한이 무조건 이겨 알갱이가 타일만 해진다.
            return Mathf.Clamp(
                cam.orthographicSize * k_MinSizePerOrthoSize,
                ideal,
                footprintSize * k_MaxGrainSizeRatio);
        }

        private static int ResolveCount(Bounds bounds)
        {
            Vector3 s = bounds.size;
            float characteristic = Mathf.Pow(Mathf.Max(0.001f, s.x * s.y * s.z), 1f / 3f);
            return Mathf.Clamp(Mathf.RoundToInt(characteristic * k_CountPerUnit), k_MinCount, k_MaxCount);
        }

        private static float BackOut(float t)
        {
            const float c3 = k_PopOvershoot + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + k_PopOvershoot * u * u;
        }

        private static void EnsureSharedAssets()
        {
            if (s_quad == null) s_quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            if (s_grain == null) s_grain = CreateGrainTexture(64);
        }

        // 중심이 단단하고 가장자리로 사라지는 흰 알갱이. 흰색 고정 + 알파만 실어 틴트는 머티리얼 색으로 준다
        // (VortexVisual의 절차 생성 텍스처와 같은 규약 — 프로젝트에 파티클 텍스처 저작 파이프라인이 없다).
        private static Texture2D CreateGrainTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                name = "TowerSpawnGrain (generated)",
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = 1f - Mathf.SmoothStep(0.15f, 1f, r);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a * a) * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
