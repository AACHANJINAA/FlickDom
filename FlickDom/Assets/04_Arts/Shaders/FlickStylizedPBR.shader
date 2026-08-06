Shader "FlickDom/StylizedPBR"
{
    // 유니티 인스펙터(에디터)에 노출되는 변수들입니다.
    Properties
    {
        _MainTex ("Base Color (Albedo)", 2D) = "white" {}
        _BaseColor ("Base Color Tint", Color) = (1, 1, 1, 1)
        _TextureStrength ("Albedo Texture Strength", Range(0, 1)) = 1.0
        [Toggle] _RotateTexture90 ("Rotate Texture 90 Degrees", Float) = 0.0
        
        [NoScaleOffset] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0

        [NoScaleOffset] _MetallicGlossMap ("Mask Map (R=Metallic, A=Smoothness)", 2D) = "white" {}
        _Metallic ("Metallic Scale", Range(0, 1)) = 0.0
        _Smoothness ("Smoothness Scale", Range(0, 1)) = 0.5
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 0.0
        _SpecularIntensity ("PBR Reflectance Scale", Range(0, 2)) = 0.65
        
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 0)
        
        _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower ("Rim Power", Range(0.1, 10.0)) = 3.0
        _RimIntensity ("Rim Intensity", Range(0, 1)) = 0.12
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #define _SPECULAR_SETUP 1
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
                half _TextureStrength;
                half _RotateTexture90;
                half _BumpScale;
                half _Metallic;
                half _Smoothness;
                half _OcclusionStrength;
                half _SpecularIntensity;
                half4 _EmissionColor;
                half4 _RimColor;
                half _RimPower;
                half _RimIntensity;
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
                half rotateTexture = step(0.5h, _RotateTexture90);
                float2 rotatedUV = float2(1.0f - input.uv.y, input.uv.x);
                float2 materialUV = lerp(input.uv, rotatedUV, rotateTexture);

                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, materialUV);
                half3 albedo = lerp(half3(1.0h, 1.0h, 1.0h), texColor.rgb, _TextureStrength) * _BaseColor.rgb;
                half alpha = texColor.a * _BaseColor.a;
                
                half4 mask = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, materialUV);
                half metallic = saturate(mask.r * _Metallic);
                half smoothness = saturate(mask.a * _Smoothness);

                half4 bumpMap = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, materialUV);
                half3 normalTS = UnpackNormalScale(bumpMap, _BumpScale);
                half2 rotatedNormalXY = half2(normalTS.y, -normalTS.x);
                normalTS.xy = lerp(normalTS.xy, rotatedNormalXY, rotateTexture);
                
                half3x3 tangentToWorld = half3x3(
                    normalize(input.tangentWS), 
                    normalize(input.bitangentWS), 
                    normalize(input.normalWS)
                );
                half3 normal = NormalizeNormalPerPixel(mul(normalTS, tangentToWorld));
                half3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionHCS;
                inputData.normalWS = normal;
                inputData.viewDirectionWS = viewDir;
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = ComputeFogFactor(input.positionHCS.z);
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SampleSH(normal);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionHCS);
                inputData.shadowMask = half4(1, 1, 1, 1);
                inputData.tangentToWorld = tangentToWorld;

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.specular = lerp(half3(0.04h, 0.04h, 0.04h), albedo, metallic) * _SpecularIntensity;
                surfaceData.metallic = metallic;
                surfaceData.smoothness = smoothness;
                surfaceData.normalTS = normalTS;
                surfaceData.emission = _EmissionColor.rgb;
                surfaceData.occlusion = lerp(1.0h, mask.g, _OcclusionStrength);
                surfaceData.alpha = alpha;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);

                half rim = pow(saturate(1.0h - dot(normal, viewDir)), _RimPower);
                color.rgb += _RimColor.rgb * albedo * rim * _RimIntensity;
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = alpha;
                return color;
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
