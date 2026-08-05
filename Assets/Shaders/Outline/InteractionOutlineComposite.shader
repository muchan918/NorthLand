// #213 상호작용 아웃라인 — 스크린 스페이스 실루엣의 2단계: 마스크를 dilate 해 링을 뽑아 합성한다.
// 설계: Docs/Rendering/WIP-OutlineMigration.md §2
//
//   [마스크 RT]  슬롯 값(0.25=Hover / 0.5=Select / 0.75=MergePreview)
//        ↓
//   [dilate → 원본 차감]  = 실루엣 링
//        ↓
//   [값→색 매핑 후 알파 블렌드]
//
// 두께는 **스크린 픽셀 단위**가 기본이다(문서 §2.2 — 픽셀레이션 채택 여부에 묶이지 않기 위함).
// 픽셀 그리드 스냅이 필요해지면 `_Thickness`를 블록 정수배로 넘기면 되고 셰이더는 그대로다.
Shader "NorthLand/Interaction Outline Composite"
{
    Properties
    {
        _Thickness ("Thickness (px)", Float) = 3
        _HoverColor ("Hover Color", Color) = (1, 0.85, 0.2, 1)
        _SelectColor ("Select Color", Color) = (0.3, 1, 0.4, 1)
        _PreviewColor ("Merge Preview Color", Color) = (1, 0.35, 0.75, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "InteractionOutlineComposite"

            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Vert / _BlitTexture / _BlitScaleBias / sampler_PointClamp 를 제공한다.
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Thickness;
            float4 _HoverColor;
            float4 _SelectColor;
            float4 _PreviewColor;

            // 마스크 텍셀 크기(1/width, 1/height). Blitter가 바인딩한 텍스처의 _TexelSize에
            // 의존하지 않고 피처가 명시적으로 넘긴다 — 바인딩 경로에 따라 채워지지 않을 수 있다.
            float4 _MaskTexelSize;

            // 원형 16탭. 고정 패턴에 두께만 곱해 쓴다 — 탭 수를 두께에 연동하면
            // 두꺼울 때 링에 구멍이 생기므로, 두께가 커지면 링 두께로만 반영되게 둔다.
            static const float2 k_Taps[16] =
            {
                float2( 1.000,  0.000), float2( 0.924,  0.383), float2( 0.707,  0.707), float2( 0.383,  0.924),
                float2( 0.000,  1.000), float2(-0.383,  0.924), float2(-0.707,  0.707), float2(-0.924,  0.383),
                float2(-1.000,  0.000), float2(-0.924, -0.383), float2(-0.707, -0.707), float2(-0.383, -0.924),
                float2( 0.000, -1.000), float2( 0.383, -0.924), float2( 0.707, -0.707), float2( 0.924, -0.383)
            };

            // Blit.hlsl은 _BlitTexture를 TEXTURE2D_X로 선언한다 — X 계열 매크로로 샘플링해야 한다.
            float SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).r;
            }

            // 슬롯 값 → 색. 값이 큰 쪽이 우선순위가 높다(MergePreview > Select > Hover) —
            // 인접한 서로 다른 슬롯이 겹칠 때 더 중요한 신호가 살아남게 한다.
            float4 SlotToColor(float slot)
            {
                if (slot > 0.625) return _PreviewColor;
                if (slot > 0.375) return _SelectColor;
                return _HoverColor;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;

                // 내부는 칠하지 않는다 — 링만 남긴다.
                if (SampleMask(uv) > 0.01)
                {
                    discard;
                }

                // dilate: 주변 탭 중 가장 큰 슬롯 값을 취한다.
                float2 radius = _MaskTexelSize.xy * max(_Thickness, 0.5);

                float found = 0.0;

                UNITY_UNROLL
                for (int i = 0; i < 16; i++)
                {
                    found = max(found, SampleMask(uv + k_Taps[i] * radius));
                }

                // 두께가 2px을 넘으면 바깥 링에 구멍이 생기므로 중간 반경도 한 겹 더 본다.
                if (_Thickness > 2.0)
                {
                    float2 halfRadius = radius * 0.5;

                    UNITY_UNROLL
                    for (int j = 0; j < 16; j++)
                    {
                        found = max(found, SampleMask(uv + k_Taps[j] * halfRadius));
                    }
                }

                if (found < 0.01)
                {
                    discard;
                }

                return half4(SlotToColor(found));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
