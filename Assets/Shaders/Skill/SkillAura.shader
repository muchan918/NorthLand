// 스킬 조준 인디케이터 전용 셰이더. 이슈 #286 + 원기둥 프로토타입(SkillPrefab.unity).
//
// 뎁스 테스트는 정상(ZTest LEqual)으로 둔다 — 한때 지형에 안 가려지게 하려고 ZTest Always(오버레이)로
// 그렸으나, 앞뒤 관계를 통째로 무시하는 탓에 아우라가 지형과 어우러지지 않고 "따로 노는" 느낌이 나서
// 되돌렸다. 지형 높낮이 대응은 원기둥 볼륨 자체의 높이로 해결한다(낮은 타일에 앉아도 옆 타일보다
// 원기둥이 높으면 그 위로 솟아 보인다).
//
// 방사형/수직 그라디언트는 정점 UV가 아니라 **로컬 정점 위치**로 직접 계산한다(uv 기반이면 유니티
// 기본 Cylinder 프리미티브처럼 옆면·캡 UV가 아틀라스로 뒤섞인 메시에 못 쓴다). _LocalRadius/_LocalHeight로
// "로컬 공간에서 반경/높이가 실제로 어디까지인지"를 알려주면, 메시가 원판이든 원기둥이든 같은 셰이더로
// 정확한 0~1 비율을 뽑는다 — 기본값은 유니티 Cylinder 프리미티브 규격(반지름 0.5, 높이 2, Y[-1,1]).
// SkillRangeIndicator.cs(평면 원판)는 로컬 반경이 실제 스킬 사거리(radius)와 같으므로 인스턴스마다
// _LocalRadius를 그 값으로 맞춰준다(SkillRangeIndicator.cs 참고) — 그 외엔 손댈 것 없음.
Shader "NorthLand/SkillAura"
{
    Properties
    {
        _Color ("Color", color) = (0.4, 0.9, 1, 0.85)
        _RingPosition ("Ring Position", range(0, 1)) = 0.85   // 링이 맺히는 반경 비율(0=중심, 1=바깥 테두리)
        _RingWidth ("Ring Width", range(0.01, 0.5)) = 0.12    // 링 밴드의 폭(클수록 두꺼운 링)
        _FillIntensity ("Fill Intensity", range(0, 1)) = 0.35 // 링 안쪽 채움의 밝기(0=거의 안 보임)
        _HeightFade ("Height Fade", range(0, 1)) = 0          // 위로 갈수록 옅어지는 정도(0=수직 페이드 없음 — 평면 원판 기본값)
        _LocalRadius ("Local Radius", float) = 0.5            // 메시 로컬 공간에서 바깥 테두리까지의 반경
        _LocalHeight ("Local Height", float) = 2               // 메시 로컬 공간에서 바닥~꼭대기 전체 높이
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "Universal"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+500"
        }

        Pass
        {
            Name "UniversalForward"

            ZWrite Off
            ZTest LEqual // 일반 뎁스 테스트 — 지형과 정상적인 앞뒤 관계를 갖는다
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

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
                float radiusT : TEXCOORD0; // 0=중심 축, 1=바깥 테두리(_LocalRadius) 반경 비율
                float heightT : TEXCOORD1; // 0=바닥, 1=꼭대기(_LocalHeight) 높이 비율
                float4 position_cs : SV_POSITION;
            };

            uniform half _LocalRadius;
            uniform half _LocalHeight;

            void VertexFunction(Attributes attribs, out Interpolators varyings)
            {
                float4 position_ws = mul(UNITY_MATRIX_M, attribs.position_os);
                varyings.position_cs = mul(UNITY_MATRIX_VP, position_ws);

                // 트랜스폼 스케일이 곱해지기 전(로컬 공간)에 계산해, 오브젝트를 어떤 스케일로 배치해도
                // 반경/높이 비율이 유지된다.
                half localRadius = max(_LocalRadius, 0.0001);
                half localHeight = max(_LocalHeight, 0.0001);
                varyings.radiusT = saturate(length(attribs.position_os.xz) / localRadius);
                varyings.heightT = saturate((attribs.position_os.y + localHeight * 0.5) / localHeight);
            }

            uniform half4 _Color;
            uniform half _RingPosition;
            uniform half _RingWidth;
            uniform half _FillIntensity;
            uniform half _HeightFade;

            half4 FragmentFunction(Interpolators varyings) : SV_Target
            {
                half radiusT = varyings.radiusT;

                // 채움: 중심은 거의 안 보이고 바깥으로 갈수록 옅게 밝아짐.
                half fill = radiusT * _FillIntensity;

                // 링: _RingPosition을 중심으로 폭 _RingWidth인 밝은 밴드. 바깥쪽은 테두리(radiusT=1)에서
                // 자연스럽게 0으로 페이드돼 원판 실루엣이 딱딱하게 끊기지 않는다.
                half ringInner = _RingPosition - _RingWidth * 0.5;
                half ringOuter = _RingPosition + _RingWidth * 0.5;
                half ring = smoothstep(ringInner, _RingPosition, radiusT) *
                            (1.0 - smoothstep(_RingPosition, max(ringOuter, 1.0), radiusT));

                // 수직 페이드: 바닥(heightT=0)은 그대로, 위로 갈수록(heightT→1) 옅어짐. _HeightFade=0(기본값,
                // 평면 원판 용도)이면 lerp가 상수 1이 되어 이 항이 완전히 없는 것과 동일 — 회귀 없음.
                half heightFade = lerp(1.0, 1.0 - varyings.heightT, _HeightFade);

                half alpha = saturate((fill + ring) * heightFade) * _Color.a;
                half3 color = _Color.rgb * (0.7 + ring * 0.9); // 링 부근만 더 밝게(글로우 느낌)

                return half4(color, alpha);
            }

            ENDHLSL
        }
    }
}
