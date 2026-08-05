// #213 상호작용 아웃라인 — 스크린 스페이스 실루엣의 1단계: 대상을 마스크 RT에 슬롯 값으로 그린다.
// 설계: Docs/Rendering/WIP-OutlineMigration.md §2
//
// 셸(인버티드 헐) 방식과 달리 지오메트리를 부풀리지 않는다. 대상 렌더러를 R 채널에만 상수값으로
// 그려두고, 합성 패스에서 dilate 후 원본을 차감해 링을 뽑는다. 그래서 렌더러가 몇 개든
// **오브젝트 전체에 실루엣 하나**가 나온다(부품별 테두리가 생기지 않는다).
//
// ZTest를 프로퍼티로 뺀 이유: 슬롯별 가려짐 정책을 머티리얼 상태 한 줄로 나누기 위함이다.
// LEqual(4) = 가려지면 안 보임 / Always(8) = 앞 오브젝트를 투시. 값 확정은 켜보며 한다(문서 §3.6).
Shader "NorthLand/Interaction Outline Mask"
{
    Properties
    {
        // 슬롯 값. 합성 패스가 색으로 매핑한다. R8 양자화 여유를 두려고 0.25 간격을 쓴다.
        _SlotValue ("Slot Value", Float) = 0.25

        // UnityEngine.Rendering.CompareFunction 값. 4 = LessEqual, 8 = Always.
        _ZTestMode ("ZTest Mode", Float) = 4
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "InteractionOutlineMask"

            // 마스크만 만든다 — 깊이는 읽되 쓰지 않는다.
            ZWrite Off
            ZTest [_ZTestMode]
            Cull Off            // 벤더 에셋에 양면 머티리얼이 섞여 있다(CandyLand 전량 Cull Off)
            ColorMask R
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // SkinnedMeshRenderer도 대상이다 — 스키닝은 Unity가 정점 버퍼에 반영해 넘겨주므로
            // 여기서는 평범한 오브젝트→클립 변환만 하면 된다.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _SlotValue;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(_SlotValue, 0, 0, 0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
