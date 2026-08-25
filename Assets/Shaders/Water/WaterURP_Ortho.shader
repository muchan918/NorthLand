// Sweet_Land WaterURP.shader 기반 NorthLand 수면 셰이더.
// 화면 깊이 기반 shallow/deep/caustics는 사용하지 않는다. 거품은 깊이로 복원한 지형의
// 월드 높이가 실제 수면에 가까울 때만 표시해 오소그래픽 투영의 실루엣 오검출을 막는다.
Shader "NorthLand/WaterURP_Ortho"
{
    Properties
    {
        [Header(Surface)]
        _MaskSurface ("Mask", 2D) = "black" {}
        _SurfaceOpacity ("Opacity", range(0, 1)) = 1
        _ColorSurface ("Color", color) = (0.9, 0.9, 0.9, 1)

        [Header(Color)]
        _ColorDeep ("Water Color", color) = (0.1, 0.2, 0.9, 1)

        [Header(Normal)]
        _NormalMap ("Map", 2D) = "bump" {}
        _NormalStrength ("Srength", range(0, 1)) = 1

        [Header(Optics)]
        _Smoothness ("Smoothness", range(0, 1)) = 1

        [Header(Ambient)]
        _AmbientFresnel ("Fresnel", float) = 1
        _ColorAmbient ("Color", color) = (0.9, 0.9, 1)

        [Header(Foam)]
        [Toggle] _IsFoam ("Enable", float) = 1
        _MaskFoam ("Mask", 2D) = "white" {}
        _FoamHeightRange ("World Height Range", float) = 1
        _FoamCutoff ("Cutoff", range(0, 1)) = 0.5
        _FoamSoftness ("Softness", range(0.001, 1)) = 0.1
        _ColorFoam ("Color", color) = (1, 1, 1, 1)

    }
    SubShader
    {
        Tags
        {
        "RenderPipeline" = "UniversalPipeline"
        "Queue" = "Transparent-100"
        }

        Pass
        {
            Name "UniversalForward"

            HLSLPROGRAM

            #pragma vertex VertexFunction
            #pragma fragment FragmentFunction

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 position_os : POSITION;
            };

            struct Interpolators
            {
                float2 uv_ws : TEXCOORD0;
                float3 viewVector_ws : TEXCOORD1;
                float4 position_ss : TEXCOORD2;
                float eyeDepth : TEXCOORD3;
                float waterHeight_ws : TEXCOORD4;
                float4 position_cs : SV_POSITION;
            };

            void VertexFunction(Attributes attribs, out Interpolators varyings)
            {
	            float4 position_ws = mul(UNITY_MATRIX_M, attribs.position_os);
	            float4 position_cs = mul(UNITY_MATRIX_VP, position_ws);
	            varyings.uv_ws = position_ws.xz;
	            varyings.viewVector_ws = _WorldSpaceCameraPos - position_ws.xyz;
	            varyings.position_ss = ComputeScreenPos(position_cs);
	            varyings.eyeDepth = -TransformWorldToView(position_ws.xyz).z;
	            varyings.waterHeight_ws = position_ws.y;
	            varyings.position_cs = position_cs;
            }

            uniform sampler2D _MaskSurface;
            uniform float4 _MaskSurface_ST;
            uniform half _SurfaceOpacity;
            uniform half3 _ColorSurface;

            uniform half3 _ColorDeep;

            uniform sampler2D _NormalMap;
            uniform float4 _NormalMap_ST;
            uniform half _NormalStrength;

            uniform half _Smoothness;

            uniform half _AmbientFresnel;
            uniform half3 _ColorAmbient;

            uniform bool _IsFoam;
            uniform sampler2D _MaskFoam;
            uniform float4 _MaskFoam_ST;
            uniform half _FoamHeightRange;
            uniform half _FoamCutoff;
            uniform half _FoamSoftness;
            uniform half3 _ColorFoam;

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float SceneEyeDepthFromRaw(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    float depth01 = 1.0 - rawDepth;
                #else
                    float depth01 = rawDepth;
                #endif
                float orthoEyeDepth = lerp(_ProjectionParams.y, _ProjectionParams.z, depth01);
                float perspectiveEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                return unity_OrthoParams.w > 0.5 ? orthoEyeDepth : perspectiveEyeDepth;
            }

            float3 SceneWorldPosition(float2 uv, float rawDepth)
            {
                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif
                return ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
            }

            float Fresnel(float3 normal, float3 viewDir, float power)
            {
                return pow((1.0 - saturate(dot(normalize(normal), normalize(viewDir)))), power);
            }

            // Packing
            half3 UnpackNormalAG(half4 packedNormal, half scale)
            {
                half3 normal;
                normal.xy = packedNormal.ag * 2.0 - 1.0;
                normal.z = max(1.0e-16, sqrt(1.0 - saturate(dot(normal.xy, normal.xy))));

                normal.xy *= scale;
                return normal;
            }

            half3 UnpackNormalmapRGorAG(half4 packedNormal, half scale)
            {
                packedNormal.a *= packedNormal.r;
                return UnpackNormalAG(packedNormal, scale);
            }

            // Operations
            half3 NormalBlend(half3 A, half3 B)
            {
                return normalize(half3(A.rg + B.rg, A.b * B.b));
            }

            half3 NormalStrength(half3 normal, half strength)
            {
                normal.xy *= strength;
                return normalize(normal);
            }

            half3 SampleNormalMap(sampler2D map, float2 uv)
            {
                half4 sampleResult = tex2D(map, uv);
                return UnpackNormalmapRGorAG(sampleResult, 1);
            }

            half3 TransformNormalToWS(half3 tangent, half3 normal, half3 bitangent, half3 normal_ts)
            {
                return normalize(mul(float3x3(tangent, bitangent, normal), normal_ts));
            }

            void FragmentFunction(Interpolators varyings, out half4 outColor : SV_Target)
            {
	            half3 viewDir = normalize(varyings.viewVector_ws);

	            // Calculating Normal
	            half3 normal = SampleNormalMap(_NormalMap, varyings.uv_ws * _NormalMap_ST.xy + _Time * _NormalMap_ST.zw);
	            normal = NormalStrength(normal, _NormalStrength);
	            normal = TransformNormalToWS(half3(1, 0, 0), half3(0, 1, 0), half3(0, 0, 1), normal);

	            // 깊이/접촉 판정 없이 수면 전체에 동일한 기본색을 사용한다.
	            half3 waterColor = _ColorDeep;

	            // Specular Coloring
	            half3 halfVector = normalize(_MainLightPosition.xyz + viewDir);
	            half specMask = pow(saturate(dot(normal, halfVector)), _Smoothness * 1000) * sqrt(_Smoothness);
	            waterColor = lerp(waterColor, half3(1, 1, 1), specMask);

	            // Surface Mask Coloring
	            half surfaceMask = tex2D(_MaskSurface, varyings.uv_ws * _MaskSurface_ST.xy + _Time.y * _MaskSurface_ST.zw);
	            waterColor = lerp(waterColor, _ColorSurface, surfaceMask * _SurfaceOpacity);

	            // Fade Fresnel Coloring
	            half fresnel = saturate(Fresnel(normal, viewDir, _AmbientFresnel) + Fresnel(half3(0, 1, 0), viewDir, _AmbientFresnel));
	            waterColor = lerp(waterColor, _ColorAmbient, fresnel);

	            // 실제 수면 높이에 가까운 지형에만 흐르는 거품을 표시한다.
	            if(_IsFoam)
	            {
		            float2 uv_ss = varyings.position_ss.xy / varyings.position_ss.w;
		            float rawDepth = SampleSceneDepth(uv_ss);
		            float sceneEyeDepth = SceneEyeDepthFromRaw(rawDepth);
		            float signedDepth = sceneEyeDepth - varyings.eyeDepth;
		            float3 scenePosition_ws = SceneWorldPosition(uv_ss, rawDepth);

		            half isBehindWater = step(1.0e-4, signedDepth);
		            half heightMask = 1.0 - saturate(
		                abs(scenePosition_ws.y - varyings.waterHeight_ws) / max(_FoamHeightRange, 1.0e-4h));
		            half foamTexture = tex2D(
		                _MaskFoam,
		                varyings.uv_ws * _MaskFoam_ST.xy + _Time.y * _MaskFoam_ST.zw).r;
		            half foamSignal = foamTexture * heightMask * isBehindWater;
		            half foamMask = smoothstep(
		                _FoamCutoff,
		                _FoamCutoff + max(_FoamSoftness, 1.0e-3h),
		                foamSignal);
		            waterColor = lerp(waterColor, _ColorFoam, foamMask);
	            }

	            outColor = half4(waterColor.rgb, 1);
            }

            ENDHLSL
        }

        Pass
        {
	        Name "DepthOnly"
	        Tags { "LightMode"="DepthOnly" }

	        ColorMask 0
	        ZWrite On
	        ZTest LEqual

	        HLSLPROGRAM
	        #pragma vertex DepthOnlyVertex
	        #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
	        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"

	        ENDHLSL
        }

        Pass
        {
	        Name "DepthNormals"
	        Tags { "LightMode"="DepthNormals" }

	        ZWrite On
	        ZTest LEqual

	        HLSLPROGRAM
	        #pragma vertex DepthNormalsVertex
	        #pragma fragment DepthNormalsFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
	        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"

	        ENDHLSL
        }
    }
}
