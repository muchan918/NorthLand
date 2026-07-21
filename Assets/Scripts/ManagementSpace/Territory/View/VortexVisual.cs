using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 선택 가능(Selectable) 영토를 표시하는 소용돌이 비주얼.<br/>
/// 프로젝트에 소용돌이 에셋이 없어 전부 코드로 만든다(#93): 절차 생성 스파이럴 텍스처를
/// XZ 평면 쿼드에 입히고 Y축으로 회전시킨다. <see cref="TerritoryNodeStateVisual"/>이
/// 런타임에 생성·설정하므로 인스펙터 필드 없이 <see cref="Init"/>로만 구성된다.
/// </summary>
public class VortexVisual : MonoBehaviour
{
    // 텍스처는 인스턴스마다 새로 만들지 않도록 공유한다(노드 수십 개가 동일 무늬 사용).
    private static Texture2D _sharedTexture;

    private Material _material;
    private Renderer _renderer;

    private float _baseDiameter;
    private float _spinSpeed;
    private Color _baseColor;
    private Color _hoverColor;
    private Color _dimColor;

    private bool _hovered;
    private bool _claimable = true;
    private float _spinMultiplier = 1f;
    private bool _vanishing;

    /// <summary>스폰 직후 1회 호출된다. 머티리얼이 null이면 Sprites/Default로 폴백(엣지 LineRenderer와 동일 규약).</summary>
    public void Init(Material sourceMaterial, float diameter, Color baseColor, Color hoverColor, Color dimColor, float spinSpeed)
    {
        _baseDiameter = diameter;
        _spinSpeed = spinSpeed;
        _baseColor = baseColor;
        _hoverColor = hoverColor;
        _dimColor = dimColor;

        if (_sharedTexture == null)
        {
            _sharedTexture = GenerateSpiralTexture(256);
        }

        // 쿼드를 자식으로 두고(X+90도 = 위를 보게) 이 오브젝트의 Y 회전으로 소용돌이를 돌린다.
        var quad = new GameObject("Quad");
        quad.transform.SetParent(transform, false);
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        quad.transform.localPosition = new Vector3(0f, 0.3f, 0f); // 지면과의 z-fight 방지

        var filter = quad.AddComponent<MeshFilter>();
        filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

        _renderer = quad.AddComponent<MeshRenderer>();
        _material = sourceMaterial != null
            ? new Material(sourceMaterial)
            : new Material(Shader.Find("Sprites/Default"));
        _material.mainTexture = _sharedTexture; // URP Unlit이면 [MainTexture] _BaseMap으로 매핑된다
        _renderer.sharedMaterial = _material;
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        ApplyLook();
    }

    /// <summary>호버 하이라이트 — 밝은 틴트 + 회전 가속 + 스케일 펄스. 확장 불가 상태면 무시된다.</summary>
    public void SetHighlight(bool hovered)
    {
        _hovered = hovered;
        ApplyLook();
    }

    /// <summary>오늘 확장 가능 여부 — 소진 시 회색 틴트 + 저속 회전(기존 회색 노드 시맨틱 계승).</summary>
    public void SetClaimable(bool claimable)
    {
        _claimable = claimable;
        ApplyLook();
    }

    /// <summary>
    /// 확보 연출 1단계: 스핀업하며 0으로 축소. 파괴는 호출자(<see cref="TerritoryNodeStateVisual"/>) 몫.
    /// ct는 호출자(부모 노드)의 destroyCancellationToken — 이 오브젝트는 부모의 자식이라
    /// 부모 파괴 시 함께 파괴되므로 호출자 토큰 하나로 수명이 보장된다.
    /// </summary>
    public async UniTask VanishAsync(float duration, CancellationToken ct)
    {
        _vanishing = true;
        _spinMultiplier = 4f;
        Vector3 startScale = transform.localScale;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.localScale = startScale * Mathf.Max(0f, 1f - elapsed / duration);
            await UniTask.Yield(ct);
        }
    }

    private void Update()
    {
        transform.Rotate(0f, _spinSpeed * _spinMultiplier * Time.deltaTime, 0f, Space.Self);
    }

    private void OnDestroy()
    {
        // 공유 텍스처는 유지, 인스턴스 머티리얼만 정리(런타임 new Material 누수 방지).
        if (_material != null)
        {
            Destroy(_material);
        }
    }

    private void ApplyLook()
    {
        if (_vanishing)
        {
            return; // 연출 중엔 호버/상태 갱신이 스케일·속도를 덮어쓰지 않게
        }

        bool highlighted = _hovered && _claimable;
        _material.color = highlighted ? _hoverColor : (_claimable ? _baseColor : _dimColor);
        _spinMultiplier = highlighted ? 2f : (_claimable ? 1f : 0.3f);
        transform.localScale = Vector3.one * (_baseDiameter * (highlighted ? 1.15f : 1f));
    }

    // 아르키메데스 나선 3팔 + 중심 코어 + 외곽 페이드. 흰색에 알파만 실어 틴트는 머티리얼 색으로.
    private static Texture2D GenerateSpiralTexture(int size)
    {
        const int arms = 3;
        const float turns = 2.5f;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            name = "VortexSpiral (generated)",
        };

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                if (r >= 1f)
                {
                    pixels[y * size + x] = new Color32(255, 255, 255, 0);
                    continue;
                }

                float theta = Mathf.Atan2(v, u);
                // 팔 무늬: 각도 + 반지름 비례 위상 → 바깥으로 감기는 나선 밴드
                float band = 0.5f + 0.5f * Mathf.Sin(arms * theta + turns * 2f * Mathf.PI * r);
                band = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.8f, band));

                float edgeFade = 1f - Mathf.SmoothStep(0.75f, 1f, r); // 외곽으로 부드럽게 소멸
                float core = Mathf.Clamp01(1f - r / 0.18f);           // 중심 밝은 코어
                float alpha = Mathf.Clamp01(Mathf.Max(band * edgeFade, core));

                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }
}
