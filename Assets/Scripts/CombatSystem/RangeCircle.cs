using UnityEngine;
using UnityEngine.Rendering;

namespace NorthLand.Combat
{
    /// 바닥(XZ 평면)에 눕는 범용 범위 원: 굵은 외곽선 + 반투명 채움. 이슈 #192.
    /// 사거리는 반지름에 따라 정점을 매번 계산해야 하는 "절차적 지오메트리"라 프리팹이 아닌 코드로 만든다.
    /// (UI·정적 비주얼은 프리팹 저작이 원칙 — 이건 그 예외에 해당한다.)
    ///
    /// 사용법(한 줄 생성 → 반지름/표시만 제어):
    ///     var circle = RangeCircle.Create(parent, fillColor, outlineColor);
    ///     circle.SetRadius(5f);
    ///     circle.Hide();  // circle.Show();
    ///
    /// 색은 프로젝트 전역에서 쓰는 "Sprites/Default"(반투명 언릿·양면)로 칠한다 — 셀 하이라이트·스킬 원과 동일해 URP PC/Mobile 양쪽에서 동작(신규 셰이더 에셋 불필요).
    /// 런타임 Mesh/Material은 GC 대상이 아니므로 OnDestroy에서 직접 파괴한다(누수 방지, PR#115 리뷰).
    [DisallowMultipleComponent]
    public class RangeCircle : MonoBehaviour
    {
        const string k_Shader = "Sprites/Default";
        const int k_Segments = 48;       // 원 근사 다각형의 변 수(클수록 매끈)
        const float k_OutlineWidth = 0.6f; // 외곽선 폭(맵 스케일에 안 맞으면 이 값만 조정)
        const float k_YOffset = 0.06f;   // z-파이팅 방지용으로 바닥 살짝 위

        LineRenderer _outline;
        MeshRenderer _fillRenderer;
        Mesh _fillMesh;
        Material _outlineMat;
        Material _fillMat;
        float _radius = float.NaN;       // 같은 반지름이면 지오메트리 재생성 생략
        bool _built;                     // 지오메트리/머티리얼 1회 구성 가드(Awake·Create 어느 쪽이 먼저 부르든 안전)

        /// 범위 원을 하나 만들어 반환한다. parent가 null이면 월드에 독립 생성.
        public static RangeCircle Create(Transform parent, Color fillColor, Color outlineColor, string name = "RangeCircle")
        {
            var go = new GameObject(name);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);

                // "월드 반경" 계약 유지: 부모(타워) 루트에 스케일/회전이 있어도 원은 월드 기준 크기·바닥 평면(XZ)을 유지한다.
                // SetRadius가 받는 값(Tower.AttackRange 등)은 OverlapSphere가 쓰는 월드 반경이므로, 로컬 지오메트리(반경=로컬 단위)가
                // 곧 월드 반경이 되도록 부모 변환을 역보정한다. 미보정 시 스케일 걸린 프리팹에서 표시 반경이 실제와 어긋난다(#192 재발).
                Vector3 ls = parent.lossyScale;
                go.transform.localScale = new Vector3(
                    Mathf.Approximately(ls.x, 0f) ? 1f : 1f / ls.x,
                    Mathf.Approximately(ls.y, 0f) ? 1f : 1f / ls.y,
                    Mathf.Approximately(ls.z, 0f) ? 1f : 1f / ls.z);
                go.transform.rotation = Quaternion.identity; // 부모 회전과 무관하게 바닥 평면 고정
            }
            // 활성 GO에 AddComponent하면 Awake가 즉시(동기) 실행되어 지오메트리가 준비된다.
            var circle = go.AddComponent<RangeCircle>();
            circle.SetColors(fillColor, outlineColor);
            return circle;
        }

        // 활성 GO면 AddComponent 시 Awake로, 비활성이면 Create/공개 메서드의 명시 호출로 구성된다.
        void Awake() => Build();

        // 지오메트리·머티리얼을 1회만 구성한다. 언제 호출해도 안전(멱등).
        // 비활성 부모에 붙였을 때 Awake가 미실행돼 필드가 null인 채 역참조되는 NRE를 방지한다.
        void Build()
        {
            if (_built) return;
            _built = true;

            // 외곽선: 이 GO의 LineRenderer(로컬 좌표 원).
            _outline = gameObject.AddComponent<LineRenderer>();
            _outline.useWorldSpace = false;
            _outline.loop = true;
            _outline.widthMultiplier = k_OutlineWidth;
            _outline.shadowCastingMode = ShadowCastingMode.Off;
            _outline.receiveShadows = false;
            _outlineMat = new Material(Shader.Find(k_Shader));
            _outline.sharedMaterial = _outlineMat;

            // 채움: Renderer는 GO당 하나만 가능하므로 자식 GO에 MeshRenderer로 둔다.
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(transform, false);
            _fillMesh = new Mesh { name = "RangeCircleFill" };
            fillGo.AddComponent<MeshFilter>().sharedMesh = _fillMesh;
            _fillRenderer = fillGo.AddComponent<MeshRenderer>();
            _fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _fillRenderer.receiveShadows = false;
            _fillMat = new Material(Shader.Find(k_Shader));
            _fillRenderer.sharedMaterial = _fillMat;
        }

        /// 채움/외곽선 색. 채움은 보통 알파를 낮춰 반투명하게 넘긴다.
        public void SetColors(Color fill, Color outline)
        {
            Build();
            _fillMat.color = fill;
            _outline.startColor = _outline.endColor = outline;
        }

        /// 원의 반경(월드 반경). 같은 값이면 재생성을 생략한다.
        public void SetRadius(float radius)
        {
            Build();
            if (Mathf.Approximately(_radius, radius)) return;
            _radius = radius;
            Rebuild(radius);
        }

        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);

        // 둘레 점 48개를 삼각함수로 뽑아, 외곽선(선)과 채움(삼각형 팬)을 함께 다시 만든다.
        void Rebuild(float r)
        {
            // 둘레 점 (로컬 좌표, XZ 평면): x=cosθ·r, z=sinθ·r
            var ring = new Vector3[k_Segments];
            for (int i = 0; i < k_Segments; i++)
            {
                float angle = i / (float)k_Segments * Mathf.PI * 2f;
                ring[i] = new Vector3(Mathf.Cos(angle) * r, k_YOffset, Mathf.Sin(angle) * r);
            }

            // 외곽선: 둘레 점을 그대로 이어 loop.
            _outline.positionCount = k_Segments;
            _outline.SetPositions(ring);

            // 채움: 중심 1점 + 둘레 점 → 삼각형 팬(피자 조각). Sprites/Default는 양면이라 와인딩 무관.
            var verts = new Vector3[k_Segments + 1];
            verts[0] = new Vector3(0f, k_YOffset, 0f);
            ring.CopyTo(verts, 1);

            var tris = new int[k_Segments * 3];
            for (int i = 0; i < k_Segments; i++)
            {
                tris[i * 3] = 0;                              // 중심
                tris[i * 3 + 1] = i + 1;                      // 둘레 현재 점
                tris[i * 3 + 2] = (i + 1) % k_Segments + 1;   // 둘레 다음 점(마지막은 첫 점으로 순환)
            }

            _fillMesh.Clear();
            _fillMesh.vertices = verts;
            _fillMesh.triangles = tris;
            _fillMesh.RecalculateBounds();
        }

        void OnDestroy()
        {
            if (_fillMesh != null) Destroy(_fillMesh);
            if (_fillMat != null) Destroy(_fillMat);
            if (_outlineMat != null) Destroy(_outlineMat);
        }
    }
}
