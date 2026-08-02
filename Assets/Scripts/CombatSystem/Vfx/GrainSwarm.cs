using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NorthLand.Combat
{
    /// 하얀 알갱이 스웜의 **렌더링 부품**. 타워 등장(#264)과 합성 소모(#265)가 같은 알갱이를 쓰므로
    /// 두 연출이 공유한다 — 알갱이가 눈에 같아 보여야 "재료가 흩어졌다 → 결과가 됐다"가 한 연출로 읽힌다.
    ///
    /// ## 이 클래스가 아는 것과 모르는 것
    /// **움직임을 모른다.** 수렴이든 폭발이든 부유든 좌표는 전부 호출자가 정하고, 여기는 알갱이를 만들고
    /// 갖고 있다가 지우는 일만 한다. 두 연출의 움직임이 전혀 다르므로(한쪽은 후광에서 한 점으로, 다른
    /// 쪽은 한 점에서 사방으로) 공유할 수 있는 건 딱 여기까지다.
    ///
    /// 아는 것은 **알갱이의 시각적 정체성**뿐이다: 빌보드 쿼드 + 절차 생성 알갱이 텍스처 + 흰색,
    /// 그리고 "몇 개를 얼마나 크게" 뽑을지의 규칙. 이 규칙이 한 곳에 있어야 두 연출의 알갱이가
    /// 같은 크기·같은 밀도로 보인다.
    ///
    /// ## 구현체가 GameObject + 쿼드인 이유(#264에서 정한 것)
    /// 규모가 동시 수십~수백 개라 instancing 이득이 없고, 수렴·부유가 전부 좌표 보간이라 attractor가
    /// 없는 ParticleSystem으로는 매 프레임 `SetParticles` 개입이 필요하다. 대신 정말 비싼 것(쿼드 메시·
    /// 알갱이 텍스처)만 static으로 공유한다 — 스웜 하나당 새로 만드는 것은 머티리얼 하나뿐이고,
    /// 그건 전체 페이드용이라 인스턴스여야 한다(`Dispose`에서 파괴).
    public sealed class GrainSwarm
    {
        // 프로젝트 표준 반투명 언릿(RangeCircle·셀 하이라이트·VortexVisual 공용) — URP PC/Mobile 양쪽에서
        // 동작하고 신규 셰이더 에셋이 필요 없다.
        private const string k_Shader = "Sprites/Default";

        // 알갱이 크기는 bounds가 아니라 **풋프린트**에서 뽑는다. 타일은 항상 15인데 타워 메시 크기는
        // 제각각이라(현재 프리팹만 봐도 높이 2.0~37.7, 19배), bounds 기준이면 스케일이 어긋난 프리팹에서
        // 알갱이까지 같이 어긋난다. 풋프린트는 그리드가 정하는 값이라 **에셋 교체와 무관하게 불변**이고,
        // 덕분에 모든 타워의 알갱이가 같은 크기로 보여 하나의 시각 언어가 된다.
        private const float k_GrainSizeRatio = 0.15f;    // 알갱이 한 변 = 풋프린트 × 이것
        private const float k_MaxGrainSizeRatio = 0.4f;  // 줌 하한이 넘지 못하는 상한(같은 기준)

        // 줌 보정. 입자는 월드 공간 쿼드라 타워와 **함께** 작아지므로 비례 보정은 필요 없다 — 필요한 건
        // 줌아웃(orthographicSize 300)에서 서브픽셀로 사라지지 않게 막는 화면 기준 하한이다. 직교 카메라는
        // 월드 크기 ↔ 화면 크기가 orthographicSize에 선형이라 하한도 거기에 비례시킨다(줌 범위 70~300).
        // OutlineInteractionDriver가 아웃라인 폭을 같은 값으로 스케일하는 것과 같은 성격의 처리다.
        private const float k_MinSizePerOrthoSize = 0.017f; // 1080p 기준 size 70 → 9px, 300 → 9px

        // 입자 개수는 bounds 부피의 세제곱근(= 특성 길이)에 비례. 작은 타워는 적게, 큰 타워는 많이.
        // 크기와 달리 개수는 bounds가 맞다 — 큰 타워를 같은 개수로 채우면 성기게 보인다.
        private const float k_CountPerUnit = 4f;
        private const int k_MinCount = 30;
        private const int k_MaxCount = 90;

        // 재생마다 새로 만들지 않는 공유물. 텍스처는 절차 생성이라 특히 아깝다(VortexVisual과 같은 규약).
        private static Mesh s_quad;
        private static Texture2D s_grain;

        private readonly Transform _parent;
        private readonly List<Transform> _grains = new();
        private Material _material;

        /// parent는 알갱이를 담을 부모(보통 연출 호스트). 알갱이 좌표는 전부 **월드**로 다루므로
        /// 부모가 어디에 있든 무관하다 — 부모는 수명 묶음일 뿐이다.
        public GrainSwarm(Transform parent)
        {
            _parent = parent;
        }

        public int Count => _grains.Count;
        public Transform this[int index] => _grains[index];

        /// 스웜 **전체**의 알파. 입자별 알파가 필요 없어 머티리얼 하나로 페이드가 끝난다.
        public void SetAlpha(float alpha)
        {
            if (_material != null) _material.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        }

        /// 알갱이 하나 추가. 회전은 빌보드 고정값(`ResolveBillboard`)을 그대로 넘기면 된다.
        public Transform Add(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            EnsureAssets();

            var go = new GameObject("Grain");
            go.transform.SetParent(_parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = s_quad;

            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            _grains.Add(go.transform);
            return go.transform;
        }

        /// 알갱이만 지운다(머티리얼은 유지 — 다음 구간에서 다시 뿌릴 수 있게).
        public void Clear()
        {
            foreach (Transform g in _grains)
            {
                if (g != null) Object.Destroy(g.gameObject);
            }
            _grains.Clear();
        }

        /// 스웜 종료. 인스턴스 머티리얼까지 파괴한다 — 연출 호스트의 `OnDestroy`에서 반드시 부를 것.
        public void Dispose()
        {
            Clear();
            if (_material != null)
            {
                Object.Destroy(_material);
                _material = null;
            }
        }

        /// 직교 카메라라 빌보드 회전은 **1회 고정**으로 끝난다(#264) — 매 프레임 카메라를 향할 필요가 없다.
        public static Quaternion ResolveBillboard(Camera cam)
            => cam != null ? cam.transform.rotation : Quaternion.identity;

        public static float ResolveGrainSize(float footprintSize, Camera cam)
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

        public static int ResolveCount(Bounds bounds)
        {
            Vector3 s = bounds.size;
            float characteristic = Mathf.Pow(Mathf.Max(0.001f, s.x * s.y * s.z), 1f / 3f);
            return Mathf.Clamp(Mathf.RoundToInt(characteristic * k_CountPerUnit), k_MinCount, k_MaxCount);
        }

        private void EnsureAssets()
        {
            if (s_quad == null) s_quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            if (s_grain == null) s_grain = CreateGrainTexture(64);

            if (_material == null)
            {
                _material = new Material(Shader.Find(k_Shader)) { name = "Grain" };
                _material.mainTexture = s_grain;
                _material.color = new Color(1f, 1f, 1f, 0f); // 페이드 인으로 시작 — 알파는 호출자가 올린다
            }
        }

        // 중심이 단단하고 가장자리로 사라지는 흰 알갱이. 흰색 고정 + 알파만 실어 틴트는 머티리얼 색으로 준다
        // (VortexVisual의 절차 생성 텍스처와 같은 규약 — 프로젝트에 파티클 텍스처 저작 파이프라인이 없다).
        private static Texture2D CreateGrainTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                name = "Grain (generated)",
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
