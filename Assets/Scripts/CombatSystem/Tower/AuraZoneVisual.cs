using UnityEngine;

namespace NorthLand.Combat
{
    /// <summary>
    /// 오라 타워의 장판 이펙트를 **실제 영향 반경**(<see cref="Tower.DisplayRange"/>)에 맞춘다.
    ///
    /// 스케일을 프리팹에 박으면 두 경우에 표시가 실제와 어긋난다 —
    /// ① 밸런싱으로 <c>DebuffAura.Radius</c>를 조정할 때, ② 사거리 버프 타일로 런타임에 반경이 변할 때.
    /// ②는 원리적으로 하드코딩이 따라갈 수 없다. `RangeCircle`이 선택 원에서 같은 문제를 푸는 방식과 같은 축이다.
    ///
    /// 스케일 식은 이렇다:
    /// <code>
    /// localScale = (반경 × 2 × edgeCompensation) ÷ (스케일 1에서의 이펙트 지름 × 부모 누적 스케일)
    /// </code>
    /// "스케일 1에서의 이펙트 지름"은 <see cref="MeasuredDiameter"/>가 자식 파티클까지 훑어 **실측**한다.
    /// 프리팹마다 손으로 적던 상수를 없앤 이유는 그 상수가 실제로 틀려 있었기 때문이다 — 아트가 이펙트를
    /// 교체하거나 파티클 크기를 만지는 순간 상수는 조용히 거짓이 되고, 증상은 "장판이 판정보다 크다"로만 나타난다.
    /// 부모 누적 스케일을 런타임에 읽으므로 타워마다 루트 스케일이 달라도(FlameFieldTower는 0.48) 신경 쓸 필요가 없다.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    [ExecuteAlways]
    public sealed class AuraZoneVisual : MonoBehaviour
    {
        [Tooltip("텍스처가 쿼드 가장자리까지 차지 않는 만큼 곱한다. 1.15 = 바깥 13%가 비어 보인다는 뜻. " +
                 "기하학적으로는 1.0이 정확하지만, 방사형 오라 텍스처는 대개 여백과 페이드가 있어 실제보다 작아 보인다.")]
        [SerializeField] float edgeCompensation = 1f;

        [Tooltip("스케일 1에서 이 이펙트가 덮는 로컬 지름. **0이면 자식 파티클까지 훑어 자동 산출한다** — " +
                 "평소에는 0으로 둘 것.\n" +
                 "손으로 적는 것은 자동 산출이 성립하지 않을 때만이다: startSize가 Curve 모드면 constantMax가 " +
                 "0이라 못 재고, '보이는 가장자리'를 최외곽이 아닌 특정 파트로 정하고 싶을 때도 직접 적어야 한다.")]
        [SerializeField] float referenceDiameter;

        ParticleSystem _ps;
        ParticleSystemRenderer _renderer;
        Tower _tower;

        // 같은 입력이면 재계산을 생략한다(RangeCircle.SetRadius와 같은 축).
        //
        // ⚠ **반경만으로 캐시하면 안 된다.** 스케일 식은 부모 누적 스케일에도 의존하고, 그 값은
        // 등장 연출(`TowerSpawnEffect`)이 타워 루트를 0 → 원본으로 애니메이션하는 동안 매 프레임 변한다.
        // 반경만 키로 쓰면 팝 중간(예: 최종값의 10%)에 계산된 스케일이 그대로 굳어 **장판이 영구히 10배로**
        // 남는다 — 실측으로 부모 0.048에서 계산된 localScale 43이 그대로 유지됐다(월드 지름 279, 정상 25).
        // 예외도 로그도 없이 그림만 어긋나는 축이라 두 값을 같이 캐시한다.
        float _appliedRadius = float.NaN;
        float _appliedParentScale = float.NaN;

        // 실측 지름 캐시. 이펙트 기하는 런타임에 변하지 않으므로 1회만 잰다(<=0 = 아직 안 잼).
        float _measuredDiameter = -1f;

        void OnEnable()
        {
            Cache();
            _appliedRadius = float.NaN;        // 풀링으로 재사용될 때 이전 값이 남지 않도록
            _appliedParentScale = float.NaN;
            _measuredDiameter = -1f;           // 에디트 모드에서 이펙트를 수정하면 다시 재도록
            Apply();
        }

        // 사거리 버프는 게임 중에 붙었다 떨어진다 — 매 프레임 확인하되 값이 같으면 즉시 빠진다.
        void LateUpdate() => Apply();

        void Cache()
        {
            if (_ps == null) _ps = GetComponent<ParticleSystem>();
            if (_renderer == null) _renderer = GetComponent<ParticleSystemRenderer>();
            if (_tower == null) _tower = GetComponentInParent<Tower>();
        }

        void Apply()
        {
            Cache();
            if (_tower == null || _ps == null || _renderer == null) return;

            float radius = _tower.DisplayRange;
            if (radius <= 0f) return;                                  // 액션 미초기화(에디트 모드 등)

            float parentScale = transform.parent != null ? transform.parent.lossyScale.x : 1f;
            if (parentScale <= 0f) return;                             // 팝 시작 프레임(스케일 0)

            // 반경과 부모 스케일 **둘 다** 그대로일 때만 생략한다(위 _appliedParentScale 주석 참조).
            if (Mathf.Approximately(_appliedRadius, radius)
                && Mathf.Approximately(_appliedParentScale, parentScale)) return;

            // 로컬 스케일 1에서 이 이펙트가 덮는 지름. 명시값이 없으면 실측한다(아래 MeasuredDiameter).
            float unitDiameter = referenceDiameter > 0f ? referenceDiameter : MeasuredDiameter();
            if (unitDiameter <= 0f) return;

            transform.localScale = Vector3.one * (radius * 2f * edgeCompensation / (unitDiameter * parentScale));
            _appliedRadius = radius;
            _appliedParentScale = parentScale;
        }

        // 경계선 역할을 하는 자식의 이름 규약. 바닥 장판 3종(`Choco/FlameField/Poison_Area`)이 이 이름으로
        // "링 최댓값=경계"인 파트를 따로 두고 있다 — 아트가 붙인 의도된 명찰이라 그대로 신뢰한다.
        //
        // ⚠ 이 컴포넌트가 붙은 프리팹은 6개이고 **절반만 이 파트를 갖는다.** 바닥 장판 `*_Area` 3종은
        // 이 경로를 타고, 빛기둥 `*_Aura` 3종(`VerticalGlow` 하나뿐)은 파트가 없어 아래 폴백(전체 최댓값)으로
        // 떨어진다 — 폴백이 틀렸던 원인인 `AreaWaves`가 `*_Aura`에는 없어 성립한다.
        // 이름이 곧 규약이므로 아트가 파트 이름을 바꾸면 경고 없이 폴백으로 내려간다.
        const string BoundaryParticleName = "AreaBounds";

        /// 스케일 1에서 이 이펙트가 덮는 지름을 **자식 파티클까지 훑어** 실측한다(1회 계산 후 캐시).
        ///
        /// 예전 자동 산출은 루트 파티클만 봤다. 그런데 이 계열 이펙트의 루트는 Cone으로 잔입자를 뿌리는
        /// 파트라 0.7이 나오고, 경계는 자식(AreaWaves/AreaSmoke)이 잡는다 — 그래서 프리팹마다 손으로 상수를
        /// 박아야 했고, 실제로 그 상수가 틀려 있었다(11.5로 적혀 있었지만 실측 13.5 → 장판이 판정보다 38% 크게 표시).
        /// 상수를 유지하는 한 이펙트를 교체·수정할 때마다 같은 오차가 조용히 다시 생긴다.
        ///
        /// ⚠ **"자식 중 최댓값"을 기준으로 삼으면 안 되는 경우가 있다.** `AreaWaves`처럼 커지면서
        /// 페이드아웃하는 링은 `sizeOverLifetime`이 최대인 순간(t=1)에 `colorOverLifetime` 알파가 이미
        /// 0이다 — 즉 "기하학적으로 가장 큰 순간"이 "눈에 안 보이는 순간"과 겹친다. 이 값을 기준 지름으로
        /// 쓰면 장판을 실제 사거리에 맞췄다고 계산되지만, 사람 눈에 보이는 밝은 링은 그 60%대 크기에서
        /// 이미 다 사라지기 시작해 체감상 사거리보다 훨씬 작아 보인다(#421 후속 — 사거리 원과 비교해 발견).
        ///
        /// 그래서 `AreaBounds`(경계선 전용 파트, 정의 위 `BoundaryParticleName`)가 있으면 **그것만** 기준으로
        /// 잰다 — 이 파트는 커브상 크기가 거의 처음부터 최댓값 근처(0.96~1.0)라 알파가 높은 구간에서도
        /// 크기가 거의 그대로다. 없는 프리팹(경계선 파트를 안 둔 이펙트)은 예전처럼 전체 최댓값으로 폴백한다.
        ///
        /// 파트 하나의 지름 = `startSize × 메시 크기 × sizeOverLifetime 최댓값 + 방출 형태 반경 × 2`.
        ///   · 메시가 없으면(빌보드) 텍스처 쿼드가 startSize 그대로라 메시 크기를 1로 둔다
        ///   · `sizeOverLifetime`은 생존 중 커지는 배율이라 곱한다(1 이하면 줄어드는 것이므로 그대로 반영)
        ///   · 방출 형태 반경은 입자가 **태어나는 위치**의 퍼짐이라 크기와 별개로 더한다
        ///   · 자식의 로컬 스케일은 이 컴포넌트 기준으로 누적해 곱한다
        ///
        /// ⚠ `velocityOverLifetime`으로 입자가 밖으로 흘러가는 이펙트는 이 식이 과소평가한다(현재 3종은
        /// 전부 꺼져 있어 성립한다). 그런 이펙트가 들어오면 `referenceDiameter`에 명시값을 넣을 것.
        float MeasuredDiameter()
        {
            if (float.IsNaN(_measuredDiameter)) return 0f;   // 이미 실패한 이펙트 — 재측정도 재경고도 하지 않는다
            if (_measuredDiameter > 0f) return _measuredDiameter;

            ParticleSystem[] systems = GetComponentsInChildren<ParticleSystem>(true);

            float result = 0f;
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i].name != BoundaryParticleName) continue;
                result = ParticleDiameter(systems[i]);
                break;
            }

            // 경계선 파트가 없는 프리팹은 예전처럼 전체 자식 중 최댓값으로 근사한다.
            if (result <= 0f)
            {
                for (int i = 0; i < systems.Length; i++)
                {
                    float diameter = ParticleDiameter(systems[i]);
                    if (diameter > result) result = diameter;
                }
            }

            if (result <= 0f)
            {
                Debug.LogWarning($"[AuraZoneVisual] 이펙트 지름을 실측할 수 없습니다({name}) — startSize가 " +
                                 "Curve 모드일 수 있습니다. referenceDiameter에 값을 직접 넣어 주세요.", this);

                // 실패도 기억한다. 성공만 캐시하면 Apply가 unitDiameter<=0으로 조기 반환해 _appliedRadius도
                // 안 잡히므로, LateUpdate마다 같은 측정을 다시 하고 경고를 매 프레임 찍는다 — 하필 원인을
                // 찾으려고 콘솔을 보는 순간 그 콘솔이 이 경고로 덮인다. GetComponentsInChildren의 매 프레임
                // 할당도 함께 사라진다.
                _measuredDiameter = float.NaN;
                return 0f;
            }

            _measuredDiameter = result;
            return result;
        }

        // 파티클 하나의 스케일 1 기준 지름(누적 로컬 스케일 포함). MeasuredDiameter의 경계선 경로/폴백
        // 경로가 같은 식을 쓰므로 공통화했다.
        float ParticleDiameter(ParticleSystem ps)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            float meshSize = renderer != null && renderer.mesh != null ? renderer.mesh.bounds.size.x : 1f;

            float diameter = ps.main.startSize.constantMax * meshSize * MaxSizeOverLifetime(ps);
            if (ps.shape.enabled) diameter += ps.shape.radius * 2f;

            // 이 컴포넌트까지의 누적 스케일. 자기 자신의 localScale은 우리가 매 프레임 덮어쓰는 값이라 뺀다.
            for (Transform t = ps.transform; t != null && t != transform; t = t.parent)
            {
                diameter *= t.localScale.x;
            }

            return diameter;
        }

        // 생존 중 크기 배율의 최댓값. Curve는 21점 샘플링으로 근사한다(정점 위치를 모르므로 균등 샘플).
        static float MaxSizeOverLifetime(ParticleSystem ps)
        {
            var sol = ps.sizeOverLifetime;
            if (!sol.enabled) return 1f;

            ParticleSystem.MinMaxCurve size = sol.size;
            if (size.mode != ParticleSystemCurveMode.Curve && size.mode != ParticleSystemCurveMode.TwoCurves)
                return Mathf.Max(size.constant, size.constantMax);

            float max = 0f;
            const int samples = 20;
            for (int i = 0; i <= samples; i++)
            {
                float time = i / (float)samples;
                float value = size.curve != null ? size.curve.Evaluate(time) : 0f;
                if (size.mode == ParticleSystemCurveMode.TwoCurves && size.curveMax != null)
                    value = Mathf.Max(value, size.curveMax.Evaluate(time));
                if (value > max) max = value;
            }
            return max * size.curveMultiplier;
        }
    }
}
