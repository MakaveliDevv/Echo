Shader "EchoProtocol/Object Hologram"
{
    Properties
    {
        [Header(Hologram Colors)]
        [HDR] [MainColor] _BaseColor("Base Color", Color) = (0, 1, 1, 1)
        [HDR] _LineColor("Scanline Color", Color) = (0, 1, 1, 1)
        [HDR] _RimColor("Rim Color", Color) = (0, 1, 1, 1)

        [Header(Rim Lighting)]
        _RimPower("Rim Power", Range(0.5, 10)) = 4

        [Header(Scanlines)]
        _ScanlineSpeed("Scanline Speed", Float) = 2
        _ScanlineDensity("Scanline Density", Float) = 50
        _ScanlineFlowDirection("Rim/Scanline Flow Direction XYZ", Vector) = (0, 1, 0, 0)

        [Header(Transparency)]
        _BaseAlpha("Base Alpha", Range(0, 1)) = 0.05
        _RimAlpha("Rim Alpha", Range(0, 1)) = 0.75
        _ScanlineAlpha("Scanline Alpha", Range(0, 1)) = 0.25
        _PulseStrength("Pulse Strength", Range(0, 1)) = 0.1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Back
        ZWrite Off

        Pass
        {
            Name "ObjectHologram"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM

            #pragma vertex HologramVertex
            #pragma fragment HologramFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _LineColor;
                half4 _RimColor;

                float _RimPower;
                float _ScanlineSpeed;
                float _ScanlineDensity;
                float4 _ScanlineFlowDirection;

                float _BaseAlpha;
                float _RimAlpha;
                float _ScanlineAlpha;
                float _PulseStrength;
            CBUFFER_END

            float3 NormalizeDirection(float3 direction, float3 fallback)
            {
                float directionLength = length(direction);

                if (directionLength <= 0.0001)
                    return fallback;

                return direction / directionLength;
            }

            Varyings HologramVertex(Attributes input)
            {
                Varyings output;

                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.worldPos = TransformObjectToWorld(input.positionOS.xyz);
                output.worldNormal = normalize(TransformObjectToWorldNormal(input.normalOS));
                output.viewDir = normalize(GetWorldSpaceViewDir(output.worldPos));

                return output;
            }

            half4 HologramFragment(Varyings input) : SV_Target
            {
                float rimGlow = 1.0 - saturate(dot(normalize(input.viewDir), normalize(input.worldNormal)));
                rimGlow = pow(rimGlow, _RimPower);

                float3 scanlineDirection = NormalizeDirection(
                    _ScanlineFlowDirection.xyz,
                    float3(0.0, 1.0, 0.0)
                );

                float scanlinePosition = dot(input.worldPos, scanlineDirection);
                float scanLines = sin(scanlinePosition * _ScanlineDensity + _Time.y * _ScanlineSpeed);
                scanLines = scanLines * 0.5 + 0.5;

                float pulse = sin(_Time.y * 2.0) * 0.5 + 0.5;

                float hologramIntensity = rimGlow + pulse * _PulseStrength;
                float lineIntensity = scanLines * 0.3;

                half3 lineColor = _LineColor.rgb * lineIntensity;
                half3 finalColor =
                    _BaseColor.rgb * hologramIntensity +
                    _RimColor.rgb * rimGlow +
                    lineColor;

                float finalAlpha =
                    _BaseAlpha +
                    rimGlow * _RimAlpha +
                    scanLines * _ScanlineAlpha;

                return half4(finalColor, saturate(finalAlpha));
            }

            ENDHLSL
        }
    }
}
