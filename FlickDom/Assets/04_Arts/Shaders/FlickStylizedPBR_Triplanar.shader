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
        _SpecularIntensity ("Specular Intensity (흰색 반사광 조절)", Range(0, 2)) = 0.5

        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 0)

        _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower ("Rim Power", Range(0.1, 10.0)) = 3.0

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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
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
                float _TriplanarScale;
                float _TriplanarSharpness;
            CBUFFER_END

            // 삼면(X/Y/Z) 투영 가중치 - 월드 노멀이 각 축에 얼마나 정렬돼 있는지로 블렌드 비율을 계산
            float3 TriplanarWeights(float3 normalWS, float sharpness)
            {
                float3 blend = pow(abs(normalWS), sharpness);
                return blend / max(dot(blend, 1.0), 1e-5);
            }

            // 텍스처 하나를 3방향(X/Y/Z)으로 샘플링해 가중 평균 - UV 이음매 없이 월드 좌표 기준으로 이어짐
            half4 SampleTriplanar(TEXTURE2D_PARAM(tex, samp), float3 positionWS, float3 blend, float scale)
            {
                half4 cx = SAMPLE_TEXTURE2D(tex, samp, positionWS.zy * scale);
                half4 cy = SAMPLE_TEXTURE2D(tex, samp, positionWS.xz * scale);
                half4 cz = SAMPLE_TEXTURE2D(tex, samp, positionWS.xy * scale);
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
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Albedo/마스크맵은 UV 대신 월드 좌표 3방향 투영으로 샘플링 - 캡-옆면 이음매 없이 이어짐
                float3 triBlend = TriplanarWeights(normalize(input.normalWS), _TriplanarSharpness);
                half4 texColor = SampleTriplanar(TEXTURE2D_ARGS(_MainTex, sampler_MainTex), input.positionWS, triBlend, _TriplanarScale);
                half4 mask = SampleTriplanar(TEXTURE2D_ARGS(_MetallicGlossMap, sampler_MetallicGlossMap), input.positionWS, triBlend, _TriplanarScale);

                float3 albedo = texColor.rgb * _BaseColor.rgb;
                float alpha = texColor.a * _BaseColor.a;
                float metallic = mask.r * _Metallic;
                float smoothness = mask.a * _Smoothness;

                // 노멀 맵은 UV 기반 샘플링 그대로 유지 (트라이플래너 노멀 블렌딩은 범위 밖)
                half4 bumpMap = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv);
                float3 normalTS = UnpackNormalScale(bumpMap, _BumpScale);

                float3x3 tangentToWorld = float3x3(
                    normalize(input.tangentWS),
                    normalize(input.bitangentWS),
                    normalize(input.normalWS)
                );
                float3 normal = normalize(mul(normalTS, tangentToWorld));

                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float NdotL = saturate(dot(normal, mainLight.direction));
                float3 diffuse = mainLight.color * NdotL * mainLight.shadowAttenuation;

                float3 halfVector = normalize(mainLight.direction + viewDir);
                float NdotH = saturate(dot(normal, halfVector));

                float specularPower = exp2(10.0 * smoothness + 1.0);
                float spec = pow(NdotH, specularPower) * metallic * _SpecularIntensity;
                float3 specular = mainLight.color * spec * mainLight.shadowAttenuation;

                float3 ambient = SampleSH(normal);

                float rim = 1.0 - saturate(dot(normal, viewDir));
                rim = smoothstep(0.4, 1.0, rim);
                rim = pow(rim, _RimPower);

                float3 rimLighting = _RimColor.rgb * rim * albedo * mainLight.color;

                float3 finalColor = albedo * (diffuse + ambient) + specular + rimLighting + _EmissionColor.rgb;

                return half4(finalColor, alpha);
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
