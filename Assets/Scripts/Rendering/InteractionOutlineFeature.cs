using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// #213 상호작용 아웃라인 — 스크린 스페이스 실루엣 렌더러 피처.
/// 설계·이행 근거: Docs/Core/InteractionOutline.md §3(렌더링 방식)·§9(이행 기록)
///
///   [대상 렌더러 → 마스크 RT]  슬롯 값 기록(0.25=Hover / 0.5=Select / 0.75=MergePreview)
///           ↓
///   [dilate → 원본 차감]       = 실루엣 링
///           ↓
///   [값→색 매핑 후 컬러 버퍼 합성]
///
/// 셸(인버티드 헐)과의 차이: 지오메트리를 만들지 않으므로 **부품 수와 무관하게 오브젝트 전체
/// 실루엣 하나**가 나온다. 렌더러 512개 상한도, 스무스 노멀 프리베이크 의존도 없다.
///
/// 렌더 이벤트를 AfterRenderingTransparents(500)로 잡은 이유(§3.8):
///  - 틸트-시프트(예정)보다 **뒤** — 선택 표시는 UI 피드백이라 블러 대상이 아니다
///  - 픽셀레이션(550)보다 **앞** — 켤 경우 화면 전체가 같은 그리드에 맞아야 한다
/// 같은 이벤트에 몰아넣고 리스트 순서에 의존하지 않도록 값을 명시적으로 벌려 잡는다.
[DisallowMultipleRendererFeature("Interaction Outline")]
public class InteractionOutlineFeature : ScriptableRendererFeature
{
    [Header("Shaders")]
    [SerializeField] private Shader maskShader;

    [SerializeField] private Shader compositeShader;

    [Header("Look")]
    [Tooltip("실루엣 두께(스크린 픽셀). 픽셀레이션을 채택하면 블록 정수배로 스냅하면 된다 — " +
             "두께 정의 자체가 픽셀 채택 여부에 묶이지 않게 스크린 픽셀을 기본으로 둔다.")]
    [Range(1f, 12f)]
    [SerializeField] private float thickness = 3f;

    [SerializeField] private Color hoverColor = new Color(1f, 0.85f, 0.2f, 1f);

    [SerializeField] private Color selectedColor = new Color(0.3f, 1f, 0.4f, 1f);

    [SerializeField] private Color mergePreviewColor = new Color(1f, 0.35f, 0.75f, 1f);

    [Header("Occlusion (가려짐 정책)")]
    [Tooltip("끄면 가려진 부분의 실루엣이 사라진다(현행 셸 동작). 켜면 앞 오브젝트를 투시한다. " +
             "호버는 레이캐스트로 맞춘 대상이라 정의상 보이는 상태이므로 끄는 것이 기본.")]
    [SerializeField] private bool hoverSeeThrough;

    [SerializeField] private bool selectedSeeThrough = true;

    [SerializeField] private bool mergePreviewSeeThrough = true;

    [Header("Cameras")]
    [Tooltip("이 이름을 가진 카메라에서는 그리지 않는다. 미니맵 카메라는 cullingMask가 -1이라 " +
             "그대로 두면 미니맵에도 아웃라인이 나온다(#213 T2).")]
    [SerializeField] private string[] excludedCameraNames = { "MinMapCamera" };

    private InteractionOutlinePass _pass;
    private Material _maskHover;
    private Material _maskSelected;
    private Material _maskMergePreview;
    private Material _composite;

    public override void Create()
    {
        if (maskShader == null || compositeShader == null)
        {
            return;
        }

        // 슬롯별 머티리얼 3개. MaterialPropertyBlock을 쓰지 않는 이유는 문서 §6-3과 같다 —
        // 다만 여기서는 배칭이 아니라 "드로우 사이에 상태를 바꾸지 않는다"는 단순성이 목적이다.
        _maskHover = CreateMask(0.25f, hoverSeeThrough);
        _maskSelected = CreateMask(0.5f, selectedSeeThrough);
        _maskMergePreview = CreateMask(0.75f, mergePreviewSeeThrough);

        _composite = CoreUtils.CreateEngineMaterial(compositeShader);

        _pass = new InteractionOutlinePass
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents,
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass == null || _composite == null)
        {
            return;
        }

        // 대상이 하나도 없으면 패스 자체를 등록하지 않는다 — 평시 비용 0.
        if (!InteractionOutlineRegistry.HasTargets)
        {
            return;
        }

        Camera cam = renderingData.cameraData.camera;

        if (cam == null || IsExcluded(cam))
        {
            return;
        }

        CameraType type = renderingData.cameraData.cameraType;

        if (type == CameraType.Reflection || type == CameraType.Preview)
        {
            return;
        }

        _pass.Setup(_maskHover, _maskSelected, _maskMergePreview, _composite, thickness,
            hoverColor, selectedColor, mergePreviewColor);

        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_maskHover);
        CoreUtils.Destroy(_maskSelected);
        CoreUtils.Destroy(_maskMergePreview);
        CoreUtils.Destroy(_composite);
    }

    private Material CreateMask(float slotValue, bool seeThrough)
    {
        Material m = CoreUtils.CreateEngineMaterial(maskShader);
        m.SetFloat("_SlotValue", slotValue);

        // CompareFunction.Always = 8(투시), LessEqual = 4(가려지면 안 보임).
        m.SetFloat("_ZTestMode", seeThrough ? (float)CompareFunction.Always : (float)CompareFunction.LessEqual);

        return m;
    }

    private bool IsExcluded(Camera cam)
    {
        if (excludedCameraNames == null)
        {
            return false;
        }

        for (int i = 0; i < excludedCameraNames.Length; i++)
        {
            if (!string.IsNullOrEmpty(excludedCameraNames[i]) && cam.name == excludedCameraNames[i])
            {
                return true;
            }
        }

        return false;
    }

    private sealed class InteractionOutlinePass : ScriptableRenderPass
    {
        private static readonly Vector4 k_BlitScaleBias = new Vector4(1f, 1f, 0f, 0f);
        private static readonly int k_ThicknessId = Shader.PropertyToID("_Thickness");
        private static readonly int k_HoverColorId = Shader.PropertyToID("_HoverColor");
        private static readonly int k_SelectColorId = Shader.PropertyToID("_SelectColor");
        private static readonly int k_PreviewColorId = Shader.PropertyToID("_PreviewColor");
        private static readonly int k_MaskTexelSizeId = Shader.PropertyToID("_MaskTexelSize");

        private readonly ProfilingSampler _sampler = new ProfilingSampler("Interaction Outline");
        private readonly List<Renderer> _hover = new List<Renderer>();
        private readonly List<Renderer> _selected = new List<Renderer>();
        private readonly List<Renderer> _mergePreview = new List<Renderer>();

        private Material _maskHover;
        private Material _maskSelected;
        private Material _maskMergePreview;
        private Material _composite;
        private float _thickness;

        public void Setup(Material maskHover, Material maskSelected, Material maskMergePreview, Material composite,
            float thickness, Color hover, Color selected, Color mergePreview)
        {
            _maskHover = maskHover;
            _maskSelected = maskSelected;
            _maskMergePreview = maskMergePreview;
            _composite = composite;
            _thickness = thickness;

            _composite.SetFloat(k_ThicknessId, thickness);
            _composite.SetColor(k_HoverColorId, hover);
            _composite.SetColor(k_SelectColorId, selected);
            _composite.SetColor(k_PreviewColorId, mergePreview);
        }

        private sealed class MaskPassData
        {
            public Material MaskHover;
            public Material MaskSelected;
            public Material MaskMergePreview;
            public List<Renderer> Hover;
            public List<Renderer> Selected;
            public List<Renderer> MergePreview;
        }

        private sealed class CompositePassData
        {
            public Material Composite;
            public TextureHandle Mask;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            if (_composite == null || !resourceData.activeColorTexture.IsValid())
            {
                return;
            }

            _hover.Clear();
            _selected.Clear();
            _mergePreview.Clear();
            InteractionOutlineRegistry.Collect(_hover, _selected, _mergePreview);

            if (_hover.Count == 0 && _selected.Count == 0 && _mergePreview.Count == 0)
            {
                return;
            }

            // 마스크 RT — R8 한 장. 슬롯 값만 담으므로 컬러 정밀도가 필요 없다.
            TextureDesc maskDesc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
            maskDesc.name = "_InteractionOutlineMask";
            maskDesc.format = GraphicsFormat.R8_UNorm;
            maskDesc.clearBuffer = true;
            maskDesc.clearColor = Color.clear;
            maskDesc.depthBufferBits = DepthBits.None;
            maskDesc.filterMode = FilterMode.Point;
            maskDesc.wrapMode = TextureWrapMode.Clamp;
            maskDesc.msaaSamples = MSAASamples.None;

            TextureHandle mask = renderGraph.CreateTexture(maskDesc);

            using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>("Interaction Outline Mask",
                       out MaskPassData passData, _sampler))
            {
                passData.MaskHover = _maskHover;
                passData.MaskSelected = _maskSelected;
                passData.MaskMergePreview = _maskMergePreview;
                passData.Hover = _hover;
                passData.Selected = _selected;
                passData.MergePreview = _mergePreview;

                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);

                // 가려짐 판정을 위해 카메라 깊이를 읽는다. ZTest는 마스크 머티리얼 상태로 슬롯별 분기된다.
                if (resourceData.activeDepthTexture.IsValid())
                {
                    builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                }

                // 등록된 렌더러를 직접 그리므로 렌더 그래프가 패스를 컬링하지 않게 막는다.
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((MaskPassData data, RasterGraphContext ctx) =>
                {
                    DrawSlot(ctx.cmd, data.Hover, data.MaskHover);
                    DrawSlot(ctx.cmd, data.Selected, data.MaskSelected);
                    DrawSlot(ctx.cmd, data.MergePreview, data.MaskMergePreview);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>("Interaction Outline Composite",
                       out CompositePassData passData, _sampler))
            {
                passData.Composite = _composite;
                passData.Mask = mask;

                builder.UseTexture(mask, AccessFlags.Read);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                float width = Mathf.Max(1f, cameraData.cameraTargetDescriptor.width);
                float height = Mathf.Max(1f, cameraData.cameraTargetDescriptor.height);
                _composite.SetVector(k_MaskTexelSizeId, new Vector4(1f / width, 1f / height, width, height));

                builder.SetRenderFunc((CompositePassData data, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, data.Mask, k_BlitScaleBias, data.Composite, 0);
                });
            }
        }

        private static void DrawSlot(RasterCommandBuffer cmd, List<Renderer> renderers, Material material)
        {
            if (material == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer r = renderers[i];

                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                {
                    continue;
                }

                // 서브메시가 여럿이면 전부 그려야 실루엣에 구멍이 안 생긴다.
                int subMeshCount = Mathf.Max(1, r.sharedMaterials.Length);

                for (int sub = 0; sub < subMeshCount; sub++)
                {
                    cmd.DrawRenderer(r, material, sub, 0);
                }
            }
        }
    }
}
