using UnityEngine;
using UnityEngine.Serialization;

namespace NorthLand.Combat
{
    /// <summary>
    /// 받침대의 로컬 Y축을 월드 높이 방향으로 사용한다.
    /// Yaw 회전은 허용하지만, 로컬 Y축을 월드 Up에서 기울이는
    /// Pitch/Roll 회전은 허용하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AdaptiveTowerFoundation : MonoBehaviour
    {
        private const float MaxFoundationTiltDegrees = 0.01f;

        [SerializeField]
        private Transform foundation;

        [SerializeField, FormerlySerializedAs("minimumThickness"), Min(0f)]
        private float extraThickness = 0.4f;

        private void OnValidate()
        {
            if (foundation == null)
            {
                return;
            }

            float tiltDegrees = Vector3.Angle(foundation.up, Vector3.up);

            if (tiltDegrees <= MaxFoundationTiltDegrees)
            {
                return;
            }

            Debug.LogWarning($"[AdaptiveTowerFoundation] 받침대의 로컬 Y축이 월드 Up에서 {tiltDegrees:0.###}도 기울어져 있습니다. Yaw 회전만 허용하고 Pitch/Roll 회전은 제거하세요.",this);
        }

        public void Fit(float lowestSurfaceY,float highestSurfaceY)
        {
            if (foundation == null)
            {
                Debug.LogWarning($"[AdaptiveTowerFoundation] 받침대 Transform이 " +$"지정되지 않았습니다: {name}",this);

                return;
            }

            if (!TryGetWorldBounds(out Bounds bounds) || bounds.size.y <= Mathf.Epsilon)
            {
                return;
            }

            float targetHeight = highestSurfaceY - lowestSurfaceY + extraThickness;

            Vector3 scale = foundation.localScale;

            scale.y *= targetHeight / bounds.size.y;

            foundation.localScale = scale;

            if (!TryGetWorldBounds(out bounds))
            {
                return;
            }

            foundation.position += Vector3.up * (transform.position.y - bounds.max.y);
        }

        private bool TryGetWorldBounds(out Bounds bounds)
        {
            Renderer[] renderers = foundation.GetComponentsInChildren<Renderer>(true);

            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }
    }
}