// 낮/밤 전환 셀 와이프 (#101).
//
// 화면을 정사각 셀로 나누고, 대각선 순서(기본: 우하단 -> 좌상단)로 셀이 하나씩 "밤"으로 뒤집힌다.
//
// 왜 이렇게 나누는가: 밤 전환에서 화면공간인 건 NightVolume 그레이드뿐이고 나머지(디렉셔널
// 라이트/앰비언트/스카이박스/가로등)는 전부 씬 라이팅이라 "이 셀만 밤"이 원리적으로 불가능하다.
// 그래서 씬 라이팅과 볼륨 weight는 전역으로 Lerp하고, 이 패스가 아직 볼륨이 채우지 못한
// **남은 몫**을 뒤집힌 셀에만 얹어 그 칸만 먼저 밤 100%로 보이게 만든다.
//
//   volumeWeight = progress          (전역, DayNightLightingController가 담당)
//   셀 안쪽      = (1 - progress)    (이 셰이더가 담당)
//   -> 뒤집힌 셀은 항상 밤 100%, 아직 안 온 곳은 progress만큼만 어두워진 중간 상태
//
// 이 배분의 핵심은 progress=1에서 이 패스의 기여가 정확히 0이 된다는 것이다. 종료 시점의 화면은
// 오로지 볼륨이 만들므로, 아래 그레이드 근사식이 URP ColorAdjustments와 정확히 일치하지 않아도
// 전환이 끝날 때 튀지 않는다. (이 패스는 톤매핑 이후 LDR 이미지에 걸리므로 애초에 일치할 수 없다)
//
// 파라미터를 Properties에 두지 않고 전부 전역 uniform으로 선언한 이유: 이 값들은 매 프레임
// DayNightTransition이 구동하는데, 머티리얼 프로퍼티로 두면 런타임에 쓸 때마다 **머티리얼
// 에셋이 dirty가 되어 git diff에 뜬다**(물 머티리얼에서 겪은 문제와 같은 계열).
// 전역이면 에셋을 건드리지 않고, 튜닝 지점도 컴포넌트 하나로 모인다.
// 이름은 다른 셰이더와 충돌하지 않도록 _NightWipe_ 접두사를 붙인다.
Shader "NorthLand/NightWipe"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "NightWipe"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _NightWipe_Progress;

            // 뒤집힌 셀에 얹을 그레이드 양 = (목표 블렌드 - 현재 씬 블렌드).
            // 낮->밤이면 양수(더 어둡게), 밤->낮이면 음수(도로 밝게)로 들어와 같은 식이 양방향에 성립한다.
            // 전환이 끝나면 씬 블렌드가 목표에 도달해 0이 되므로 이 패스의 기여도 정확히 0이 된다.
            float _NightWipe_Amount;

            float _NightWipe_CellSize;
            float _NightWipe_Jitter;
            float _NightWipe_Reverse;

            float _NightWipe_PostExposure;
            float4 _NightWipe_ColorFilter;
            float _NightWipe_Saturation;
            float _NightWipe_Contrast;

            float _NightWipe_EdgeGlow;
            float _NightWipe_EdgeWidth;
            float4 _NightWipe_EdgeColor;

            // 셀 좌표를 0..1 난수로. 셀마다 고정이라 프레임 간에 흔들리지 않는다.
            float Hash(float2 cell)
            {
                return frac(sin(dot(cell, float2(127.1, 311.7))) * 43758.5453);
            }

            // 남은 몫(amount)만큼 밤 그레이드를 얹는다. amount=0이면 입력 그대로.
            float3 ApplyNight(float3 c, float amount)
            {
                c *= exp2(_NightWipe_PostExposure * amount);
                c *= pow(max(_NightWipe_ColorFilter.rgb, 1e-4), amount);

                float lum = dot(c, float3(0.2126, 0.7152, 0.0722));
                c = lerp(lum.xxx, c, 1.0 + _NightWipe_Saturation * amount);
                c = lerp(0.5.xxx, c, 1.0 + _NightWipe_Contrast * amount);

                return c;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float3 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                // 셀 중심의 화면 uv — 셀 안쪽 픽셀이 전부 같은 임계값을 갖게 한다.
                float cellPx = max(_NightWipe_CellSize, 1.0);
                float2 cell = floor(uv * _ScreenParams.xy / cellPx);
                float2 cellUV = saturate((cell + 0.5) * cellPx / _ScreenParams.xy);

                // texcoord는 v=1이 화면 **위쪽**이다(지터를 끄고 계단 방향을 눈으로 확인).
                // 따라서 우하단은 (u=1, v=0) / 좌상단은 (u=0, v=1).
                // 우하단이 0(가장 먼저), 좌상단이 1(가장 나중)이 되도록 잡는다.
                float t = ((1.0 - cellUV.x) + cellUV.y) * 0.5;
                t = lerp(t, 1.0 - t, step(0.5, _NightWipe_Reverse));

                t += (Hash(cell) - 0.5) * _NightWipe_Jitter;

                // (0,1]로 눌러 둔다: progress=0에서 아무 셀도 뒤집히지 않고,
                // progress=1에서 모든 셀이 뒤집히는 것을 보장한다.
                float threshold = saturate(t) * 0.98 + 0.02;

                float wiped = step(threshold, _NightWipe_Progress);

                // 씬 블렌드가 이미 적용한 몫을 뺀 나머지만 얹는다.
                col = ApplyNight(col, wiped * _NightWipe_Amount);

                // 막 뒤집힌 셀일수록 강한 선행 엣지. 전환이 끝나가면 함께 사라진다.
                float edge = smoothstep(_NightWipe_EdgeWidth, 0.0, _NightWipe_Progress - threshold) * wiped;
                col += _NightWipe_EdgeColor.rgb * (edge * _NightWipe_EdgeGlow * (1.0 - _NightWipe_Progress));

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
