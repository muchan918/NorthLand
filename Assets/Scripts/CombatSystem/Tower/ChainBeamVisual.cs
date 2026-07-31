using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NorthLand.Combat
{
    // 체인 홉 경로를 잇는 짧은 수명의 빔(#252). 히트스캔은 판정이 그 프레임에 끝나므로 이 연출은
    // **순수 사후 표시**다 — 데미지·명중과 어떤 인과도 없어서, 잘못 만들어도 밸런스가 흔들리지 않는다.
    //
    // 좌표를 스냅샷으로 고정하고 대상을 추적하지 않는다: 순간 판정이라 번개가 공중에 얼어붙은 모습이
    // 자연스럽고, 홉 대상이 연출 도중 죽어도 선이 깨지지 않는다(추적하면 파괴된 Transform을 역참조한다).
    //
    // 프리팹(ChainFields.BeamPrefab)이 있으면 그 외형을 쓰고, 없으면 코드가 최소 LineRenderer를 만든다.
    // 폴백을 두는 이유는 아트 머티리얼을 기다리지 않고 검증할 수 있게 하기 위함이며,
    // firePoint 미할당 시 타워 루트에서 발사하는 하위 호환과 같은 결이다.
    [DisallowMultipleComponent]
    public sealed class ChainBeamVisual : MonoBehaviour
    {
        // RangeCircle과 같은 셰이더 — 반투명 언릿·양면이라 URP PC/Mobile 양쪽에서 신규 에셋 없이 동작한다.
        const string k_Shader = "Sprites/Default";
        const float k_FallbackWidth = 0.8f;

        LineRenderer _line;

        // 코드 생성 경로에서만 소유한다. 프리팹 머티리얼은 공유 에셋이라 절대 파괴하면 안 된다.
        Material _ownedMaterial;

        float _lifetime;
        float _elapsed;
        Color _baseColor;

        // 코드 생성 경로만 색을 건드린다 — 프리팹은 아트가 gradient로 페이드를 저작할 수 있으므로 덮지 않는다.
        bool _fade;

        /// 경로를 잇는 빔을 띄운다. path는 타격 순서대로의 월드 좌표이고, origin(포신)이 맨 앞에 붙는다.
        /// prefab이 null이면 코드로 기본 빔을 만든다. 수명이 끝나면 스스로 파괴된다.
        public static void Spawn(
            GameObject prefab, Vector3 origin, List<Vector3> path, float lifetime, Color fallbackColor)
        {
            if (path == null || path.Count == 0 || lifetime <= 0f) return;

            bool ownVisual = prefab == null;
            GameObject go = ownVisual
                ? new GameObject("ChainBeam")
                : Instantiate(prefab, origin, Quaternion.identity);

            if (ownVisual) go.transform.position = origin;

            go.AddComponent<ChainBeamVisual>().Setup(origin, path, lifetime, fallbackColor, ownVisual);
        }

        void Setup(Vector3 origin, List<Vector3> path, float lifetime, Color color, bool ownVisual)
        {
            _lifetime = lifetime;
            _fade = ownVisual;

            // 프리팹은 LineRenderer를 이미 갖고 있다. 없으면(코드 경로 또는 저작 누락) 만들어 쓴다.
            if (!TryGetComponent(out _line)) _line = gameObject.AddComponent<LineRenderer>();

            if (ownVisual)
            {
                _ownedMaterial = new Material(Shader.Find(k_Shader));
                _line.sharedMaterial = _ownedMaterial;
                _line.widthMultiplier = k_FallbackWidth;
                _baseColor = color;
            }

            // 월드 좌표를 직접 꽂으므로 부모 변환에 영향받지 않는다(빔은 어디에도 부착되지 않는다).
            _line.useWorldSpace = true;
            _line.shadowCastingMode = ShadowCastingMode.Off;
            _line.receiveShadows = false;

            _line.positionCount = path.Count + 1;
            _line.SetPosition(0, origin);
            for (int i = 0; i < path.Count; i++) _line.SetPosition(i + 1, path[i]);

            if (_fade) ApplyAlpha(1f);
        }

        void Update()
        {
            _elapsed += Time.deltaTime;

            // 남은 수명 비율로 알파를 낮춘다. 페이드가 없으면 선이 툭 끊겨 번쩍임으로 읽히지 않는다.
            if (_fade) ApplyAlpha(1f - Mathf.Clamp01(_elapsed / _lifetime));

            if (_elapsed >= _lifetime) Destroy(gameObject);
        }

        // RangeCircle과 같은 방식(정점 색). Sprites/Default가 정점 색을 반영하므로 머티리얼을 건드릴 필요가 없다.
        void ApplyAlpha(float multiplier)
        {
            Color c = _baseColor;
            c.a *= multiplier;
            _line.startColor = c;
            _line.endColor = c;
        }

        void OnDestroy()
        {
            // 런타임 Material은 GC 대상이 아니다 — 빔은 발사마다 생성되므로 누수가 빠르게 쌓인다
            // (RangeCircle이 PR#115 리뷰에서 지적받은 것과 같은 사유).
            if (_ownedMaterial != null) Destroy(_ownedMaterial);
        }
    }
}
