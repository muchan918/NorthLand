using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace NorthLand.Combat
{
    /// 합성 재료 소모 연출(#265): 후보 버튼을 누르면 재료 타워가 **하얘지며 가운데로 수축 → 입자로 폭발 →
    /// 재료 자리 상공에서 부유**한다. 배치가 끝나면 그 입자가 배치 지점으로 날아가거나(확정), 제자리로
    /// 돌아와 재료가 재조립된다(취소). 이 커밋은 앞쪽 절반(폭발 → 부유)까지다.
    ///
    /// ## 왜 시각 사본을 뜨는가
    /// 커맨드(#263)는 **버튼 클릭 즉시** 재료를 `SetActive(false)` + 타일 해제한다 — 자리를 비우는 것이
    /// 그 커맨드의 존재 이유라 연출을 기다려 줄 수 없다. 그래서 연출은 비활성화 **직전에** 뜬 사본으로
    /// 독립 재생한다. 사본은 렌더러의 `sharedMesh`/월드 트랜스폼만 복사하므로 비용이 거의 없고
    /// `isReadable`과 무관하다. 덕분에 로직과 연출이 완전히 분리돼, 연출이 중간에 죽어도 합성은 멀쩡하다.
    ///
    /// ## "가운데로 수축한 뒤 폭발"은 취향이 아니라 제약이 고른 형태다
    /// 타워 메시가 `isReadable: 0`인 경우가 많아(프로젝트 FBX 1664개 중 573개) 런타임에 정점을 읽을 수 없고,
    /// 따라서 **실루엣대로 흩어지게 만들 수 없다.** 입자 시작점이 중심 한 점이면 메시 데이터가 아예 필요
    /// 없어져 이 제약이 사라진다 — 이 연출은 전 구간에서 정점을 한 번도 읽지 않는다.
    ///
    /// ## 화이트아웃은 shell이 아니라 사본 머티리얼 교체다
    /// 어차피 사본을 뜨므로 그 사본의 머티리얼만 흰 언릿으로 갈아끼우면 끝이다. `OutlineHighlight`의 shell을
    /// 재사용하는 안(이슈 원안)은 shell이 `OutlineShell` 레이어 + 본체 패스 제외라는 **다른 목적의 셋업**을
    /// 타고 있어, 여기 쓰려면 레이어·패스를 따로 맞춰야 한다. 사본 쪽이 원본 무편집이라는 이점은 그대로 두고
    /// 코드가 훨씬 적다.
    ///
    /// 흰 실루엣은 `Sprites/Default`(프로젝트 표준 반투명 언릿)로 그린다. 반투명이라 깊이를 쓰지 않지만
    /// **모든 프래그먼트가 같은 흰색이라 그리는 순서가 결과를 바꾸지 않는다** — 불투명 셰이더 에셋을 새로
    /// 들이지 않고도 단단한 실루엣이 나온다.
    [DisallowMultipleComponent]
    public class TowerMergeDissolveEffect : MonoBehaviour
    {
        private const string k_Shader = "Sprites/Default";

        // OutlineHighlight가 shell을 두는 레이어. 재료는 대개 그룹 선택(초록 아웃라인) 상태로 합성되므로
        // shell을 걸러내지 않으면 흰 사본이 두 겹으로 떠서 실루엣이 두꺼워진다.
        private const string k_ShellLayerName = "OutlineShell";

        // ── 시간(초) ─────────────────────────────────────────────────────────────────
        private const float k_WhiteoutDuration = 0.14f;  // 하얘진 채로 "굳는" 순간(살짝 부풀며 숨을 들이쉰다)
        private const float k_CollapseDuration = 0.16f;  // 중심으로 빨려 들어가며 사라진다
        private const float k_BurstDuration = 0.22f;     // 중심 1점에서 부유 위치까지 터져 나간다
        private const float k_MaterialStagger = 0.06f;   // 재료별 출발 시차 — 동시에 터지면 한 덩어리로 보인다

        // ── 비율 ─────────────────────────────────────────────────────────────────────
        // TowerSpawnEffect와 같은 규율: 월드 단위 상수를 쓰지 않고 bounds/풋프린트에서 유도한다.
        private const float k_BreathScale = 1.06f;       // 화이트아웃 동안 부푸는 배율
        private const float k_CloudRadiusRatio = 0.5f;   // 부유 구름 반경 = 풋프린트 × 이것(= 대략 자기 칸 안)
        private const float k_RiseRatio = 0.35f;         // 상승 고도 = 시각물 높이 × 이것
        private const float k_BobAmplitudeRatio = 0.1f;  // 위아래 흔들림 = 풋프린트 × 이것
        private const float k_BobSpeed = 1.6f;           // 흔들림 각속도(rad/s)
        private const float k_OrbitDegPerSec = 22f;      // 구름 전체가 Y축으로 도는 속도 — "둥둥"의 정체

        /// 연출을 어떻게 끝낼지. 부유는 배치 대기 동안 무한 루프이므로 **바깥에서** 끝을 통지받는다.
        private enum Ending
        {
            None,
            Abort, // 조용히 걷어낸다(합성이 시작조차 못 했거나, 확정/취소 통지 없이 세션이 끝난 경우)
        }

        /// 재료 하나분의 연출 상태.
        private sealed class Source
        {
            public Bounds Bounds;        // 재료의 월드 AABB(소모 직전에 측정)
            public Transform Pivot;      // 흰 사본의 부모. bounds.center에 두어 **중심 기준**으로 수축시킨다
            public float Delay;          // 재료별 출발 시차
            public int GrainStart;       // 이 재료가 소유한 알갱이 구간 [start, start+count)
            public int GrainCount;
        }

        private readonly List<Source> _sources = new();

        // 알갱이별 데이터(전 재료 통합 인덱스). 부유 좌표는 소속 재료의 중심 + 이 오프셋으로 만든다.
        private readonly List<Vector3> _offsets = new(); // 부유 기준점 - 재료 중심
        private readonly List<Vector3> _scales = new();
        private readonly List<float> _phases = new();    // 위아래 흔들림 위상(전부 같은 박자면 기계처럼 보인다)

        private GrainSwarm _swarm;
        private Material _silhouette;
        private Ending _ending = Ending.None;
        private float _footprintSize;

        /// 재료 소모 **직전에** 호출한다 — 커맨드가 `SetActive(false)`를 걸고 나면 복제할 것이 남지 않는다.
        /// targets는 소모될 재료들, footprintSize는 타일 한 칸 크기(알갱이 크기 기준을 등장 연출과 맞추기 위한 값).
        /// 항상 유효한 인스턴스를 돌려주므로 호출부는 null을 검사하지 않아도 된다.
        public static TowerMergeDissolveEffect Play(IReadOnlyList<Transform> targets, float footprintSize)
        {
            var host = new GameObject("TowerMergeDissolveEffect");
            var effect = host.AddComponent<TowerMergeDissolveEffect>();
            effect._footprintSize = footprintSize;
            effect.RunAsync(targets, effect.destroyCancellationToken).Forget();
            return effect;
        }

        /// 연출을 조용히 끝낸다. 이미 마무리 구간에 들어갔으면 무시하므로, 세션 종료 통지에 안전망으로
        /// 걸어 두어도 진행 중인 마무리를 잘라먹지 않는다.
        public void Abort() => RequestEnd(Ending.Abort);

        private void RequestEnd(Ending ending)
        {
            if (this == null) return;          // 이미 호스트가 파괴됨(정상 종료 후 늦게 온 통지)
            if (_ending != Ending.None) return; // 선착순 — 먼저 정해진 마무리가 이긴다
            _ending = ending;
        }

        private async UniTaskVoid RunAsync(IReadOnlyList<Transform> targets, CancellationToken ct)
        {
            try
            {
                Camera cam = Camera.main;
                Build(targets, cam);

                await DissolveAsync(ct);
                await FloatAsync(ct);
                // 마무리 구간(확정 수렴 / 취소 재조립)은 후속 커밋에서 여기에 붙는다.
            }
            catch (System.OperationCanceledException)
            {
                // 씬 전환 등 중도 취소는 정상 경로다. 정리는 OnDestroy가 책임진다.
            }
            finally
            {
                if (this != null) Destroy(gameObject);
            }
        }

        // ── 준비 ─────────────────────────────────────────────────────────────────────
        private void Build(IReadOnlyList<Transform> targets, Camera cam)
        {
            _swarm = new GrainSwarm(transform);
            _silhouette = new Material(Shader.Find(k_Shader)) { name = "TowerMergeSilhouette" };
            _silhouette.color = Color.white;

            float grainSize = GrainSwarm.ResolveGrainSize(_footprintSize, cam);
            Quaternion billboard = GrainSwarm.ResolveBillboard(cam);
            float cloudRadius = _footprintSize * k_CloudRadiusRatio;
            float bobAmplitude = _footprintSize * k_BobAmplitudeRatio;

            for (int s = 0; s < targets.Count; s++)
            {
                Transform target = targets[s];
                if (target == null) continue;

                Bounds bounds = TowerSpawnEffect.CalculateVisualBounds(target, _footprintSize);
                var source = new Source
                {
                    Bounds = bounds,
                    Delay = s * k_MaterialStagger,
                    GrainStart = _swarm.Count,
                };

                source.Pivot = BuildSilhouette(target, bounds);

                // 알갱이는 지금 만들어 두고 폭발이 시작될 때까지 중심에 0 크기로 숨겨 둔다 —
                // 폭발 구간에서 생성하면 재료마다 다른 프레임에 힙 할당이 몰린다.
                int count = GrainSwarm.ResolveCount(bounds);
                for (int i = 0; i < count; i++)
                {
                    // 위로 치우친 방향. 아래로도 조금 열어 두되 지면 아래로는 내려가지 않게 한다
                    // (TowerSpawnEffect의 분포와 같은 규칙 — 두 연출의 구름이 같은 모양으로 읽혀야 한다).
                    Vector3 dir = Random.onUnitSphere;
                    dir.y = Mathf.Lerp(-0.2f, 1f, (dir.y + 1f) * 0.5f);
                    dir.Normalize();

                    Vector3 offset = dir * (cloudRadius * Random.Range(0.35f, 1f))
                                   + Vector3.up * (bounds.size.y * k_RiseRatio);

                    // 흔들림까지 감안해 최저점이 지면(시각물 밑면) 아래로 내려가지 않게 띄운다.
                    float minY = bounds.min.y + grainSize * 0.5f + bobAmplitude - bounds.center.y;
                    offset.y = Mathf.Max(offset.y, minY);

                    _swarm.Add(bounds.center, billboard, Vector3.zero);
                    _offsets.Add(offset);
                    _scales.Add(Vector3.one * (grainSize * Random.Range(0.6f, 1.35f)));
                    _phases.Add(Random.Range(0f, Mathf.PI * 2f));
                }

                source.GrainCount = _swarm.Count - source.GrainStart;
                _sources.Add(source);
            }
        }

        /// 대상의 렌더러마다 같은 메시를 쓰는 흰 사본을 만들고, `bounds.center`에 둔 피벗의 자식으로 붙인다.
        /// 피벗을 중심에 두는 것이 핵심이다 — 그래야 피벗 스케일만 줄여도 **중심으로** 수축한다
        /// (에셋 피벗이 밑면이라는 보장이 없으므로 원본 트랜스폼을 기준으로 삼으면 안 된다).
        private Transform BuildSilhouette(Transform target, Bounds bounds)
        {
            var pivot = new GameObject("Silhouette").transform;
            pivot.SetParent(transform, false);
            pivot.position = bounds.center;

            int shellLayer = LayerMask.NameToLayer(k_ShellLayerName);
            int cloned = 0;

            foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (r.gameObject.layer == shellLayer) continue;              // 아웃라인 shell(#213)
                if (r.GetComponentInParent<RangeCircle>() != null) continue; // 사거리 원(타워의 자식)
                if (!r.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;

                var copy = new GameObject(r.name);
                copy.transform.SetParent(pivot, true);
                copy.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                copy.transform.localScale = r.transform.lossyScale;
                copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                Paint(copy.AddComponent<MeshRenderer>(), r.sharedMaterials.Length);
                cloned++;
            }

            if (cloned == 0)
            {
                // MeshRenderer가 하나도 없는 에셋(스키닝 전용 등)도 실루엣이 조용히 사라지지 않게
                // bounds 크기의 흰 상자로 대체한다 — 눈에 띄게 투박해야 원인을 찾는다
                // (`CalculateVisualBounds`의 폴백과 같은 규약).
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "SilhouetteFallback";
                Destroy(box.GetComponent<Collider>());
                box.transform.SetParent(pivot, false);
                box.transform.localScale = bounds.size;
                Paint(box.GetComponent<MeshRenderer>(), 1);
            }

            return pivot;
        }

        // 서브메시 수만큼 같은 흰 머티리얼을 물린다(전부 같은 색이라 인스턴스를 나눌 이유가 없다).
        private void Paint(MeshRenderer renderer, int submeshCount)
        {
            var mats = new Material[Mathf.Max(1, submeshCount)];
            for (int i = 0; i < mats.Length; i++) mats[i] = _silhouette;

            renderer.sharedMaterials = mats;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // ── 구간 1: 화이트아웃 → 수축 → 폭발 ─────────────────────────────────────────
        private async UniTask DissolveAsync(CancellationToken ct)
        {
            // 재료별 시차가 있으므로 전체 길이는 마지막 재료가 끝나는 시각이다.
            float last = _sources.Count > 0 ? _sources[^1].Delay : 0f;
            float total = last + k_WhiteoutDuration + k_CollapseDuration + k_BurstDuration;
            float elapsed = 0f;

            while (elapsed < total)
            {
                // 배속·일시정지 무관(`unscaledDeltaTime`). 전역 timeScale을 타면 일시정지 중 재료가
                // 사라진 채로 멈추고, x4 배속에서는 연출이 통째로 소실된다 — TowerSpawnEffect와 같은 근거.
                elapsed += Time.unscaledDeltaTime;
                if (_ending != Ending.None) return; // 밤 전환 등으로 즉시 걷어내라는 통지

                float alpha = 0f;
                foreach (Source s in _sources)
                {
                    float t = elapsed - s.Delay;
                    if (t < 0f) continue;

                    if (t < k_WhiteoutDuration)
                    {
                        // 하얘진 채로 살짝 부푼다 — 수축 직전의 반동이 있어야 빨려 들어가는 맛이 산다.
                        SetPivotScale(s, Mathf.Lerp(1f, k_BreathScale, t / k_WhiteoutDuration));
                        continue;
                    }

                    t -= k_WhiteoutDuration;
                    if (t < k_CollapseDuration)
                    {
                        float p = t / k_CollapseDuration;
                        SetPivotScale(s, Mathf.Lerp(k_BreathScale, 0f, p * p)); // ease-in: 마지막에 훅 빨려든다
                        continue;
                    }

                    // 실루엣은 여기서 역할이 끝난다. 폭발부터는 알갱이가 대신한다.
                    if (s.Pivot != null)
                    {
                        Destroy(s.Pivot.gameObject);
                        s.Pivot = null;
                    }

                    t -= k_CollapseDuration;
                    float b = Mathf.Clamp01(t / k_BurstDuration);
                    float eased = 1f - (1f - b) * (1f - b) * (1f - b); // ease-out: 터지고 잦아든다
                    alpha = Mathf.Max(alpha, Mathf.Clamp01(b * 3f));   // 첫 재료가 터지는 즉시 스웜을 켠다

                    for (int i = s.GrainStart; i < s.GrainStart + s.GrainCount; i++)
                    {
                        _swarm[i].position = s.Bounds.center + _offsets[i] * eased;
                        _swarm[i].localScale = _scales[i] * eased;
                    }
                }

                _swarm.SetAlpha(alpha);
                await UniTask.Yield(ct);
            }
        }

        private static void SetPivotScale(Source s, float scale)
        {
            if (s.Pivot != null) s.Pivot.localScale = Vector3.one * scale;
        }

        // ── 구간 2: 부유 (배치 대기 동안 무한 루프) ───────────────────────────────────
        private async UniTask FloatAsync(CancellationToken ct)
        {
            float amplitude = _footprintSize * k_BobAmplitudeRatio;
            float t = 0f;

            while (_ending == Ending.None)
            {
                t += Time.unscaledDeltaTime;

                // 구름 전체를 Y축으로 천천히 돌린다. 위아래 흔들림만으로는 정지 화면처럼 보이고,
                // 회전이 들어가야 "무엇을 소모했는지"가 배치 내내 눈에 들어온다(핑크 고정의 역할 계승).
                Quaternion orbit = Quaternion.Euler(0f, k_OrbitDegPerSec * t, 0f);

                foreach (Source s in _sources)
                {
                    for (int i = s.GrainStart; i < s.GrainStart + s.GrainCount; i++)
                    {
                        Vector3 offset = orbit * _offsets[i];
                        offset.y += Mathf.Sin(t * k_BobSpeed + _phases[i]) * amplitude;
                        _swarm[i].position = s.Bounds.center + offset;
                    }
                }

                await UniTask.Yield(ct);
            }
        }

        private void OnDestroy()
        {
            _swarm?.Dispose();
            if (_silhouette != null) Destroy(_silhouette);
        }
    }
}
