using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace NorthLand.Combat
{
    /// 타워 등장 연출(#264): 공중의 하얀 입자가 배치 지점으로 수렴 → 타워가 튀어나오며 바닥에 충격파 링.
    /// 일반 배치와 합성 배치(#265)가 **같은 함수**를 타므로 두 연출은 구분되지 않는다. 합성일 때는 재료
    /// 자리에서 날아온 입자가 여기에 겹쳐 들어오며, 그 수렴이 팝과 동시에 끝나도록 `ConvergeDuration`을
    /// 시간 축으로 공유한다.
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
    ///   - 공중 입자는 `bounds`(시각 크기), 바닥 링은 **풋프린트**(논리 크기)에서 유도한다. 바닥 연출은
    ///     그리드 언어라, 홀쭉한 타워에서도 "이 칸을 먹었다"가 링 크기로 읽혀야 한다.
    ///
    /// 알갱이 자체(빌보드 쿼드·텍스처·개수/크기 규칙)는 `GrainSwarm`, 대상 스케일 점유는 `VfxScaleHold`가
    /// 맡는다 — 둘 다 #265와 공유하는 부품이고, 여기 남은 것은 **이 연출 고유의 움직임**뿐이다.
    [DisallowMultipleComponent]
    public class TowerSpawnEffect : MonoBehaviour
    {
        // ── 시간(초) ─────────────────────────────────────────────────────────────────
        // 길이가 아니라 시간이므로 절대값이어도 에셋 교체와 무관하다.

        /// 입자가 모여드는 데 걸리는 시간. **public인 이유: #265의 합성 입자가 이 시간 축을 공유한다.**
        /// 재료 자리에서 배치 지점까지의 거리는 제각각이지만 소요 시간을 이 값으로 고정하면(= 먼 재료일수록
        /// 빠르게 난다) 아래 팝이 터지는 순간에 정확히 도착한다. 거리에 비례하는 속도로 날리면 먼 재료의
        /// 입자가 **타워가 다 선 뒤에** 도착해 인과가 뒤집힌다.
        public const float ConvergeDuration = 0.45f;

        /// 0에서 원본 크기로 튀어나오는 데 걸리는 시간. public인 이유는 `ConvergeDuration`과 같다 —
        /// #265의 재료 재조립이 같은 back-out 팝을 쓰므로, 두 연출의 "뽕!"이 같은 박자여야 한다.
        public const float PopDuration = 0.28f;

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

        private const float k_RingRadiusRatio = 0.62f;   // 링 최대 반경 = 풋프린트 × 이것
        private const float k_SwirlDegrees = 160f;       // 수렴하며 Y축으로 감기는 각도("슈우웅")

        private readonly List<Vector3> _starts = new();
        private readonly List<Vector3> _scales = new();
        private readonly List<float> _delays = new();

        private GrainSwarm _swarm;
        private VfxScaleHold.Handle _hold;

        /// 배치 확정 직후 호출(fire-and-forget). 연출은 **시각 전용·논블로킹**이라 호출자는 기다리지 않는다 —
        /// 타워는 이 시점에 이미 논리적으로 완성돼 있고, 연출은 그 위에 얹히기만 한다.
        ///
        /// ⚠ 재생 중 **대상 루트의 `localScale`을 배타적으로 소유한다**(0 → 원본). 이 창(약 0.45초) 동안
        /// 다른 시스템이 그 값을 쓰거나 캡처하면 안 된다 — 자세한 계약은 `TowerPlacement.md` §9.3.2.
        public static void Play(Transform target, float footprintSize)
            => PlayAsync(target, footprintSize).Forget();

        /// 연출 종료까지 기다려야 하는 호출자용.
        /// ct는 호출자 수명 토큰. 연출 자신의 수명과 합쳐지므로 어느 쪽이 끊겨도 UniTask가 남지 않는다.
        ///
        /// ⚠ `Play`와 같은 스케일 배타 소유 계약이 적용된다. 같은 대상에 이미 재생 중이면 `VfxScaleHold`가
        /// **그 연출의 스케일을 먼저 원복시키고 인계**한다 — 배치와 합성이 같은 타워에 각자 재생을 걸어도
        /// 스케일이 오염되지 않는다.
        public static async UniTask PlayAsync(Transform target, float footprintSize, CancellationToken ct = default)
        {
            if (target == null) return;

            // 점유를 **먼저** 잡는다. 앞선 연출이 있으면 이 시점에 원본 스케일이 복원되므로, 바로 아래
            // bounds 측정이 0으로 쪼그라든 대상을 재는 일이 없다.
            VfxScaleHold.Handle hold = VfxScaleHold.Acquire(target);

            // 대상에서 무언가를 읽는 건 이 한 줄이 전부다. 이 뒤로는 순수 좌표 계산이라 에셋을 다시 보지 않는다.
            Bounds bounds = CalculateVisualBounds(target, footprintSize);

            var host = new GameObject("TowerSpawnEffect");
            host.transform.position = bounds.center;
            var effect = host.AddComponent<TowerSpawnEffect>();
            effect._hold = hold;

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(effect.destroyCancellationToken, ct);
            try
            {
                await effect.RunAsync(bounds, footprintSize, linked.Token);
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
        /// public인 이유: #265가 재료 타워의 크기·수렴 목적지를 같은 규칙으로 재야 하고, 큐브로 연출을
        /// 검증할 때도 쓰인다.
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

        private async UniTask RunAsync(Bounds bounds, float footprintSize, CancellationToken ct)
        {
            _hold.SetFactor(0f); // 수렴이 끝날 때까지 숨긴다(bounds는 위에서 이미 떴다)

            Camera cam = Camera.main;
            SpawnParticles(bounds, footprintSize, GrainSwarm.ResolveGrainSize(footprintSize, cam), GrainSwarm.ResolveBillboard(cam));
            await ConvergeAsync(bounds.center, ct);
            _swarm.Clear();

            // 등장과 바닥 링은 동시에 터진다. 링 중심 y는 시각물의 밑면 — 피벗이 어디든 지면에 붙는다.
            var ground = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            await UniTask.WhenAll(
                _hold.PopAsync(PopDuration, ct),
                RingAsync(ground, footprintSize * k_RingRadiusRatio, ct));
        }

        private void SpawnParticles(Bounds bounds, float footprintSize, float size, Quaternion billboard)
        {
            _swarm = new GrainSwarm(transform);

            int count = GrainSwarm.ResolveCount(bounds);
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

                _swarm.Add(start, billboard, scale);
                _starts.Add(start);
                _scales.Add(scale);
                _delays.Add(Random.Range(0f, k_MaxArrivalDelay));
            }
        }

        private async UniTask ConvergeAsync(Vector3 destination, CancellationToken ct)
        {
            float elapsed = 0f;
            while (elapsed < ConvergeDuration)
            {
                elapsed += Time.unscaledDeltaTime; // 배속·일시정지 무관 — 아래 RingAsync 주석 참고
                if (_hold.IsSuperseded) return;    // 같은 대상에 새 연출이 들어왔다 — 잔상을 남기지 않고 접는다
                float t = Mathf.Clamp01(elapsed / ConvergeDuration);

                // 스웜 전체 알파를 한 번에 올린다 — 입자별 알파가 필요 없어 머티리얼 하나로 끝난다.
                _swarm.SetAlpha(t / k_FadeInPortion);

                for (int i = 0; i < _swarm.Count; i++)
                {
                    float p = Mathf.Clamp01((t - _delays[i]) / (1f - _delays[i]));
                    float eased = p * p * p; // ease-in: 떠 있다가 마지막에 빨려든다

                    // 직선 수렴은 밋밋하다. 남은 오프셋을 Y축으로 감아 소용돌이로 빨려들게 한다.
                    Vector3 offset = Quaternion.Euler(0f, k_SwirlDegrees * eased, 0f) * (_starts[i] - destination);

                    _swarm[i].position = destination + offset * (1f - eased);
                    _swarm[i].localScale = _scales[i] * (1f - eased * eased); // 도착하며 소멸
                }

                await UniTask.Yield(ct);
            }
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
                // 스케일 0인 채로 멈춘다** — 이 계열 연출이 최악으로 지목한 "안 보이는 타워"가 정지 버튼
                // 하나로 재현된다. x4 배속에서 수렴이 0.11초로 줄어 연출이 소실되는 것도 같은 원인이다.
                // 순수 시각·논블로킹 연출이라 게임플레이 타이밍과 무관하다(WL-100/WL-119와 같은 축).
                float elapsed = 0f;
                while (elapsed < k_RingDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    if (_hold.IsSuperseded) return; // ring 파괴는 finally가 책임진다
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
            _hold.Release();
            _swarm?.Dispose();
        }
    }
}
