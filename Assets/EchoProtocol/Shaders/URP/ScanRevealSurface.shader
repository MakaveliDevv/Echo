Shader "EchoProtocol/Scan Reveal Surface"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0, 0, 0, 1)
        [HDR] _SurfaceScanTint("Scan Tint Color", Color) = (0, 1, 1, 1)
        _ScanRevealIntensity("Scan Reveal Intensity", Range(0, 10)) = 4
        _ScanRevealAmbient("Scan Reveal Ambient", Range(0, 1)) = 0.03
        _VerticalFade("Vertical Fade", Range(0.1, 10)) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ScanReveal"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

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
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _SurfaceScanTint;
                float _ScanRevealIntensity;
                float _ScanRevealAmbient;
                float _VerticalFade;
            CBUFFER_END

            TEXTURE2D(_ScanWaveTexture);
            SAMPLER(sampler_ScanWaveTexture);

            float4 _ScanWaveOrigin;
            float _ScanWaveMaxDistance;
            float _ScanWaveActive;
            float4 _ScanWaveEdgeColor;
            float4 _ScanWaveTrailColor;
            float _ScanWaveEdgeIntensity;
            float _ScanWaveTrailIntensity;

            Varyings vert(Attributes input)
            {
                Varyings output;

                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.worldPos = TransformObjectToWorld(input.positionOS.xyz);
                output.worldNormal = normalize(TransformObjectToWorldNormal(input.normalOS));

                return output;
            }

            float2 ScanWaveUV(float3 worldPosition)
            {
                float2 offset = worldPosition.xz - _ScanWaveOrigin.xz;
                float distance = saturate(length(offset) / max(_ScanWaveMaxDistance, 0.001));
                float angle = atan2(offset.y, offset.x) / 6.28318530718 + 0.5;

                return float2(distance, angle);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float active = saturate(_ScanWaveActive);
                float2 scanUV = ScanWaveUV(input.worldPos);
                float4 scanProfile = SAMPLE_TEXTURE2D(_ScanWaveTexture, sampler_ScanWaveTexture, scanUV);

                float verticalDistance = abs(input.worldPos.y - _ScanWaveOrigin.y);
                float verticalMask = saturate(1.0 - verticalDistance / max(_VerticalFade, 0.001));

                float edge = scanProfile.r * _ScanWaveEdgeIntensity;
                float trail = scanProfile.g * _ScanWaveTrailIntensity;
                float halo = scanProfile.b * 0.35;
                float scanAmount = (edge + trail + halo) * verticalMask * active;

                float normalSpark = pow(1.0 - saturate(abs(input.worldNormal.y)), 2.0);
                scanAmount += normalSpark * edge * active * 0.35;

                half3 baseColor = _BaseColor.rgb * max(_ScanRevealAmbient, 0.0);
                half3 globalScanColor =
                    _ScanWaveEdgeColor.rgb * edge +
                    _ScanWaveTrailColor.rgb * trail +
                    _SurfaceScanTint.rgb * halo;

                half3 finalColor =
                    baseColor +
                    globalScanColor * scanAmount * _ScanRevealIntensity;

                return half4(finalColor, 1.0);
            }

            ENDHLSL
        }
    }
}
