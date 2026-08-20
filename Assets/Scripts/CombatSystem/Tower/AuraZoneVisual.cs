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
    /// localScale = (반경 × 2 × edgeCompensation) ÷ (startSize × 메시크기 × 부모 누적 스케일)
    /// </code>
    /// 부모 누적 스케일을 런타임에 읽으므로 타워마다 루트 스케일이 달라도(FlameFieldTower는 0.48) 신경 쓸 필요가 없다.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    [ExecuteAlways]
    public sealed class AuraZoneVisual : MonoBehaviour
    {
        [Tooltip("텍스처가 쿼드 가장자리까지 차지 않는 만큼 곱한다. 1.15 = 바깥 13%가 비어 보인다는 뜻. " +
                 "기하학적으로는 1.0이 정확하지만, 방사형 오라 텍스처는 대개 여백과 페이드가 있어 실제보다 작아 보인다.")]
        [SerializeField] float edgeCompensation = 1f;

        [Tooltip("스케일 1에서 이 이펙트가 덮는 로컬 지름. 0이면 루트 파티클의 startSize × 메시 크기로 자동 산출한다.\n" +
                 "루트가 디스크 한 장인 단순 오라는 0(자동)으로 두고, 여러 파트가 합쳐진 이펙트는 " +
                 "경계를 정하는 파트의 지름을 직접 적는다(예: AreaDamage 계열의 AreaBounds = 11.5).")]
        [SerializeField] float referenceDiameter;

        ParticleSystem _ps;
        ParticleSystemRenderer _renderer;
        Tower _tower;

        // 같은 반경이면 재계산을 생략한다(RangeCircle.SetRadius와 같은 축).
        float _appliedRadius = float.NaN;

        void OnEnable()
        {
            Cache();
            _appliedRadius = float.NaN;   // 풀링으로 재사용될 때 이전 값이 남지 않도록
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
            if (Mathf.Approximately(_appliedRadius, radius)) return;

            // 로컬 스케일 1에서 이 이펙트가 덮는 지름.
            // 명시값이 있으면 그대로 쓴다 — 합성 이펙트는 루트가 경계를 정하지 않아 자동 산출이 성립하지 않는다
            // (AreaDamage 계열은 루트가 Cone으로 뿌리는 입자 하나라 0.7이 나오고, 경계는 AreaBounds가 잡는다).
            // ⚠ 자동 산출은 startSize가 Curve 모드면 constantMax가 0이라 실패한다 — 그 경우도 명시값을 쓸 것.
            float unitDiameter;
            if (referenceDiameter > 0f)
            {
                unitDiameter = referenceDiameter;
            }
            else
            {
                float meshSize = _renderer.mesh != null ? _renderer.mesh.bounds.size.x : 1f;
                unitDiameter = _ps.main.startSize.constantMax * meshSize;
            }
            float parentScale = transform.parent != null ? transform.parent.lossyScale.x : 1f;
            if (unitDiameter <= 0f || parentScale <= 0f) return;

            transform.localScale = Vector3.one * (radius * 2f * edgeCompensation / (unitDiameter * parentScale));
            _appliedRadius = radius;
        }
    }
}
