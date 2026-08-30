using UnityEngine;

namespace NorthLand.Combat
{
    [DisallowMultipleComponent]
    public sealed class AdaptiveTowerFoundation : MonoBehaviour
    {
        [SerializeField] private Transform foundation;
        [SerializeField, Min(0.01f)] private float minimumThickness = 0.4f;

        public void Fit(float lowestSurfaceY, float highestSurfaceY)
        {
            if (foundation == null ||!TryGetWorldBounds(out Bounds bounds) ||bounds.size.y <= Mathf.Epsilon)
            {
                return;
            }

            float targetHeight = Mathf.Max(minimumThickness,highestSurfaceY - lowestSurfaceY + minimumThickness);

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