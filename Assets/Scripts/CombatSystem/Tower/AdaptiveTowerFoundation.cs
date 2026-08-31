using UnityEngine;
using UnityEngine.Serialization;

namespace NorthLand.Combat
{

    /// <summary>
    /// 받침대의 로컬 Y축을 높이 방향으로 사용한다.
    /// foundation의 localRotation은 반드시 Identity여야 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AdaptiveTowerFoundation : MonoBehaviour
    {
        [SerializeField] private Transform foundation;

        [SerializeField, FormerlySerializedAs("minimumThickness"), Min(0f)]
        private float extraThickness = 0.4f;
  
        public void Fit(float lowestSurfaceY, float highestSurfaceY)
        {
            if (foundation == null)
            {
                Debug.LogWarning($"[AdaptiveTowerFoundation] 받침대 Transform이 지정되지 않았습니다: {name}",this);

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