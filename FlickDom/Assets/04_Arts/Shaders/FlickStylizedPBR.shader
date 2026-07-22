Shader "FlickDom/StylizedPBR"
{
    // 유니티 인스펙터(에디터)에 노출되는 변수들입니다.
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 0)
        
        _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower ("Rim Power", Range(0.1, 10.0)) = 3.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        // 메인 라이트와 색상을 그리는 핵심 패스
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            // URP 그림자 및 라이트 연산을 위한 키워드
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // 버텍스 셰이더 입력
            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
            };

            // 프래그먼트 셰이더로 넘겨줄 데이터
            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
            };

            // 프로퍼티 캐싱 (CBUFFER 안에 넣어야 SRP 배처 렌더링 최적화가 됩니다)
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Metallic;
                half _Smoothness;
                half4 _EmissionColor;
                half4 _RimColor;
                half _RimPower;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                // Object Space -> World Space -> Homogeneous Clip Space 변환
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 법선 및 시선 벡터 정규화
                float3 normal = normalize(input.normalWS);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);

                // URP 라이트 및 그림자 정보 가져오기
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // -----------------------------------------------------
                // 1. Diffuse (난반사 - Lambert)
                // -----------------------------------------------------
                float NdotL = saturate(dot(normal, mainLight.direction));
                float3 diffuse = mainLight.color * NdotL * mainLight.shadowAttenuation;

                // -----------------------------------------------------
                // 2. Specular (정반사 - Blinn-Phong)
                // -----------------------------------------------------
                float3 halfVector = normalize(mainLight.direction + viewDir);
                float NdotH = saturate(dot(normal, halfVector));
                
                // 유니티의 Smoothness(0~1)를 Specular Power로 변환
                float specularPower = exp2(10.0 * _Smoothness + 1.0);
                // Metallic이 높을수록 정반사가 강해짐
                float specularIntensity = pow(NdotH, specularPower) * _Metallic; 
                float3 specular = mainLight.color * specularIntensity * mainLight.shadowAttenuation;

                // -----------------------------------------------------
                // 3. Ambient (환경광)
                // -----------------------------------------------------
                float3 ambient = SampleSH(normal);

                // -----------------------------------------------------
                // 4. Rim Light (역광 림 라이트 - 장난감 감성)
                // -----------------------------------------------------
                float rim = 1.0 - saturate(dot(normal, viewDir));
                rim = pow(rim, _RimPower);
                float3 rimLighting = _RimColor.rgb * rim;

                // -----------------------------------------------------
                // 최종 컬러 연산
                // -----------------------------------------------------
                float3 finalColor = _BaseColor.rgb * (diffuse + ambient) + specular + rimLighting + _EmissionColor.rgb;

                return half4(finalColor, _BaseColor.a);
            }
            ENDHLSL
        }

        // 그림자를 드리우기 위한(Cast) 패스
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                
                // 그림자 바이어스(Shadow Bias)를 적용하여 클립 스페이스로 변환
                output.positionHCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, 0.0));
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
