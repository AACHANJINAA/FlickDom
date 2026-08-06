// FlickStylizedPBR.shader(임찬진 작성)의 별도 사본에 Triplanar 매핑을 적용한 실험용 변형입니다.
// 원본 셰이더는 건드리지 않고, 캡-옆면 UV 이음매를 없애는 실험만 이 파일에서 진행합니다.
Shader "FlickDom/StylizedPBR_Triplanar"
{
    Properties
    {
        _MainTex ("Base Color (Albedo)", 2D) = "white" {}
        _BaseColor ("Base Color Tint", Color) = (1, 1, 1, 1)

        [NoScaleOffset] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0

        [NoScaleOffset] _MetallicGlossMap ("Mask Map (R=Metallic, A=Smoothness)", 2D) = "white" {}
        _Metallic ("Metallic Scale", Range(0, 1)) = 0.0
        _Smoothness ("Smoothness Scale", Range(0, 1)) = 0.5
        _SpecularIntensity ("PBR Reflectance Scale", Range(0, 2)) = 0.65

        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 0)

        _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower ("Rim Power", Range(0.1, 10.0)) = 3.0
        _RimIntensity ("Rim Intensity", Range(0, 1)) = 0.12

        [Header(Triplanar Mapping)]
        _TriplanarScale ("Triplanar Scale (tiles per world unit)", Float) = 1.0
        _TriplanarSharpness ("Triplanar Blend Sharpness", Range(1, 32)) = 4.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #define _SPECULAR_SETUP 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 tangentOS    : TANGENT;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float3 tangentWS    : TEXCOORD2;
                float3 bitangentWS  : TEXCOORD3;
                float2 uv           : TEXCOORD4;
                float3 positionOS   : TEXCOORD5;
                float3 normalOS     : TEXCOORD6;
            };

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
                half _RimIntensity;
                float _TriplanarScale;
                float _TriplanarSharpness;
            CBUFFER_END

            // 삼면(X/Y/Z) 투영 가중치 - 노멀이 각 축에 얼마나 정렬돼 있는지로 블렌드 비율을 계산
            // 오브젝트 스페이스 노멀 기준 - 월드 스페이스로 하면 오브젝트가 회전할 때 무늬가 표면 위를 미끄러지듯 움직임
            float3 TriplanarWeights(float3 normalOS, float sharpness)
            {
                float3 blend = pow(abs(normalOS), sharpness);
                return blend / max(dot(blend, 1.0), 1e-5);
            }

            // 텍스처 하나를 3방향(X/Y/Z)으로 샘플링해 가중 평균
            // 오브젝트 스페이스 좌표 사용 - 디스크가 보드 위를 움직이거나 굴러도(플릭 게임 특성상 항상 이동/회전함)
            // 무늬가 표면에 고정된 채로 오브젝트와 같이 움직임 (월드 스페이스였다면 이동할 때마다 무늬가 미끄러짐)
            half4 SampleTriplanar(TEXTURE2D_PARAM(tex, samp), float3 positionOS, float3 blend, float scale)
            {
                half4 cx = SAMPLE_TEXTURE2D(tex, samp, positionOS.zy * scale);
                half4 cy = SAMPLE_TEXTURE2D(tex, samp, positionOS.xz * scale);
                half4 cz = SAMPLE_TEXTURE2D(tex, samp, positionOS.xy * scale);
                return cx * blend.x + cy * blend.y + cz * blend.z;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);

                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);

                float sign = input.tangentOS.w * GetOddNegativeScale();
                output.bitangentWS = cross(output.normalWS, output.tangentWS) * sign;

                output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;

                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Albedo/마스크맵은 UV 대신 오브젝트 스페이스 3방향 투영으로 샘플링
                // - 캡-옆면 이음매 없이 이어지면서, 오브젝트 스페이스라 디스크가 움직여도 무늬가 표면에 고정됨
                float3 triBlend = TriplanarWeights(normalize(input.normalOS), _TriplanarSharpness);
                half4 texColor = SampleTriplanar(TEXTURE2D_ARGS(_MainTex, sampler_MainTex), input.positionOS, triBlend, _TriplanarScale);
                half4 mask = SampleTriplanar(TEXTURE2D_ARGS(_MetallicGlossMap, sampler_MetallicGlossMap), input.positionOS, triBlend, _TriplanarScale);

                half3 albedo = texColor.rgb * _BaseColor.rgb;
                half alpha = texColor.a * _BaseColor.a;
                half metallic = saturate(mask.r * _Metallic);
                half smoothness = saturate(mask.a * _Smoothness);

                // 노멀 맵은 UV 기반 샘플링 그대로 유지 (트라이플래너 노멀 블렌딩은 범위 밖)
                half4 bumpMap = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv);
                half3 normalTS = UnpackNormalScale(bumpMap, _BumpScale);

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
                surfaceData.occlusion = 1.0h;
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
