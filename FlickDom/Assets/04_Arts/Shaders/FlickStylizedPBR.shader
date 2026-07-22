Shader "FlickDom/StylizedPBR"
{
    // 유니티 인스펙터(에디터)에 노출되는 변수들입니다.
    Properties
    {
        _MainTex ("Base Color (Albedo)", 2D) = "white" {}
        _BaseColor ("Base Color Tint", Color) = (1, 1, 1, 1)
        
        [NoScaleOffset] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0

        [NoScaleOffset] _MetallicGlossMap ("Mask Map (R=Metallic, A=Smoothness)", 2D) = "white" {}
        _Metallic ("Metallic Scale", Range(0, 1)) = 0.0
        _Smoothness ("Smoothness Scale", Range(0, 1)) = 0.5
        _SpecularIntensity ("Specular Intensity (흰색 반사광 조절)", Range(0, 2)) = 0.5
        
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
                float4 tangentOS    : TANGENT;
                float2 uv           : TEXCOORD0;
            };

            // 프래그먼트 셰이더로 넘겨줄 데이터
            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float3 tangentWS    : TEXCOORD2;
                float3 bitangentWS  : TEXCOORD3;
                float2 uv           : TEXCOORD4;
            };

            // 프로퍼티 캐싱 (CBUFFER 안에 넣어야 SRP 배처 렌더링 최적화가 됩니다)
            TEXTURE2D(_MainTex);          SAMPLER(sampler_MainTex);
            TEXTURE2D(_BumpMap);          SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _BaseColor;
                half _BumpScale;
                half _Metallic;
                half _Smoothness;
                half _SpecularIntensity;
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
                
                // TBN (Tangent, Bitangent, Normal) 공간 행렬 구성
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);
                
                // tangent의 w값은 거울상(mirroring) 모델의 비트탄젠트 방향을 결정함
                float sign = input.tangentOS.w * GetOddNegativeScale();
                output.bitangentWS = cross(output.normalWS, output.tangentWS) * sign;
                
                output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 1. 텍스처 샘플링 (Albedo)
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float3 albedo = texColor.rgb * _BaseColor.rgb;
                float alpha = texColor.a * _BaseColor.a;
                
                // 2. 마스크 맵 샘플링 (R = Metallic, A = Smoothness)
                half4 mask = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, input.uv);
                float metallic = mask.r * _Metallic;
                float smoothness = mask.a * _Smoothness;

                // 3. 노멀 맵 샘플링 및 탄젠트 공간 -> 월드 공간 변환
                half4 bumpMap = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv);
                float3 normalTS = UnpackNormalScale(bumpMap, _BumpScale);
                
                float3x3 tangentToWorld = float3x3(
                    normalize(input.tangentWS), 
                    normalize(input.bitangentWS), 
                    normalize(input.normalWS)
                );
                // 노멀맵이 적용된 최종 월드 공간 법선(Normal)
                float3 normal = normalize(mul(normalTS, tangentToWorld));

                // 시선 벡터 정규화
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
                float specularPower = exp2(10.0 * smoothness + 1.0);
                // Metallic이 높을수록 정반사가 강해지며, Specular Intensity 슬라이더로 하얗게 타는 현상 억제
                float spec = pow(NdotH, specularPower) * metallic * _SpecularIntensity; 
                float3 specular = mainLight.color * spec * mainLight.shadowAttenuation;

                // -----------------------------------------------------
                // 3. Ambient (환경광)
                // -----------------------------------------------------
                float3 ambient = SampleSH(normal);

                // -----------------------------------------------------
                // 4. Rim Light (역광 림 라이트 - 장난감 감성)
                // -----------------------------------------------------
                float rim = 1.0 - saturate(dot(normal, viewDir));
                rim = smoothstep(0.4, 1.0, rim); // 림 라이트를 좀 더 얇고 선명하게 깎아줌
                rim = pow(rim, _RimPower);
                
                // 림 라이트가 물체를 덮어버리지(하얗게 타버리지) 않도록 물체의 고유 색상(albedo)과 조명 강도를 곱해줍니다.
                float3 rimLighting = _RimColor.rgb * rim * albedo * mainLight.color;

                // -----------------------------------------------------
                // 최종 컬러 연산
                // -----------------------------------------------------
                float3 finalColor = albedo * (diffuse + ambient) + specular + rimLighting + _EmissionColor.rgb;

                return half4(finalColor, alpha);
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
