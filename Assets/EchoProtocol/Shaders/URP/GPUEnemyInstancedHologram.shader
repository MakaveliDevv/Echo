Shader "EchoProtocol/GPU Enemy Instanced Hologram"
{
    Properties
    {
        [HDR] _BaseColor("Base Color", Color) = (0, 1, 1, 1)
        [HDR] _LineColor("Scanline Color", Color) = (0, 1, 1, 1)
        [HDR] _RimColor("Rim Color", Color) = (0, 1, 1, 1)

        _EnemyHeight("Enemy Height", Float) = 1
        _EnemyScale("Enemy Scale", Float) = 1
        _RimPower("Rim Power", Range(0.5, 10)) = 4
        _ScanlineSpeed("Scanline Speed", Float) = 2
        _ScanlineDensity("Scanline Density", Float) = 50
        _ScanlineFlowDirection("Rim/Scanline Flow Direction XYZ", Vector) = (0, 1, 0, 0)
        _BaseAlpha("Base Alpha", Range(0, 1)) = 0.06
        _RimAlpha("Rim Alpha", Range(0, 1)) = 0.75
        _ScanlineAlpha("Scanline Alpha", Range(0, 1)) = 0.25
        _PulseStrength("Pulse Strength", Range(0, 1)) = 0.1

        [Header(Falling Numbers)]
        [Toggle] _NumbersEnabled("Enable Numbers", Float) = 1
        [HDR] _NumberColor("Number Color", Color) = (0, 1, 1, 1)
        [HDR] _InvestigateNumberColor("Investigate Number Color", Color) = (1, 0.45, 0, 1)
        [HDR] _ChaseNumberColor("Chase Number Color", Color) = (1, 0, 0, 1)
        _NumberAlpha("Number Alpha", Range(0, 1)) = 0.8
        _NumberColumns("Number Columns", Float) = 12
        _NumberRows("Number Rows", Float) = 20
        _NumberSpeed("Number Speed", Float) = 0.5
        _NumberThickness("Number Thickness", Range(0.02, 0.2)) = 0.08
        _NumberFlowDirection("Number Flow Direction XY", Vector) = (0, 1, 0, 0)

        [Header(State Visual Feedback)]
        _InvestigateSpeedBoost("Investigate Speed Boost", Range(0, 5)) = 0.6
        _ChaseSpeedBoost("Chase Speed Boost", Range(0, 8)) = 2.5
        _ChaseGlowBoost("Chase Glow Boost", Range(0, 5)) = 1.5
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

        HLSLINCLUDE

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct GPUEnemy
        {
            float2 position;
            float2 velocity;
            float2 targetPosition;

            float maxSpeed;
            float detectionDistance;
            float attackDistance;

            int state;
            float stateTimer;
            int patrolIndex;
            int active;

            float investigateVisual;
            float chaseVisual;
        };

        StructuredBuffer<GPUEnemy> _Enemies;

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _LineColor;
            half4 _RimColor;

            float _EnemyHeight;
            float _EnemyScale;
            float _RimPower;
            float _ScanlineSpeed;
            float _ScanlineDensity;
            float4 _ScanlineFlowDirection;
            float _BaseAlpha;
            float _RimAlpha;
            float _ScanlineAlpha;
            float _PulseStrength;

            float _NumbersEnabled;
            half4 _NumberColor;
            half4 _InvestigateNumberColor;
            half4 _ChaseNumberColor;
            float _NumberAlpha;
            float _NumberColumns;
            float _NumberRows;
            float _NumberSpeed;
            float _NumberThickness;
            float4 _NumberFlowDirection;

            float _InvestigateSpeedBoost;
            float _ChaseSpeedBoost;
            float _ChaseGlowBoost;

            float4 _PlayerPosition;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            uint instanceID : SV_InstanceID;
        };

        struct EnemyVertexData
        {
            float3 worldPosition;
            float3 worldNormal;
            float3 localPosition;
            float2 meshUV;
            float state;
            float detectionDistance;
            float investigateVisual;
            float chaseVisual;
            float chaseDistance01;
        };

        float2 Rotate2D(float2 value, float angle)
        {
            float sine = sin(angle);
            float cosine = cos(angle);

            return float2(
                value.x * cosine - value.y * sine,
                value.x * sine + value.y * cosine
            );
        }

        EnemyVertexData BuildEnemyVertexData(Attributes input)
        {
            EnemyVertexData output;
            GPUEnemy enemy = _Enemies[input.instanceID];

            float2 flatVelocity = enemy.velocity;
            float angle = 0.0;

            if (dot(flatVelocity, flatVelocity) > 0.0001)
            {
                angle = atan2(flatVelocity.x, flatVelocity.y);
            }

            float3 localPosition = input.positionOS.xyz * _EnemyScale;
            float2 rotatedXZ = Rotate2D(localPosition.xz, angle);

            output.worldPosition = float3(
                enemy.position.x + rotatedXZ.x,
                _EnemyHeight + localPosition.y,
                enemy.position.y + rotatedXZ.y
            );

            float2 rotatedNormalXZ = Rotate2D(input.normalOS.xz, angle);
            output.worldNormal = normalize(float3(rotatedNormalXZ.x, input.normalOS.y, rotatedNormalXZ.y));
            output.localPosition = localPosition;
            output.meshUV = input.uv;
            output.state = enemy.state;
            output.detectionDistance = enemy.detectionDistance;
            output.investigateVisual = enemy.investigateVisual;
            output.chaseVisual = enemy.chaseVisual;

            float distanceToPlayer = distance(enemy.position, _PlayerPosition.xy);
            float closeToPlayer01 = 1.0 - saturate(distanceToPlayer / max(enemy.detectionDistance, 0.001));
            output.chaseDistance01 = smoothstep(0.0, 1.0, closeToPlayer01) * enemy.chaseVisual;

            return output;
        }

        float3 NormalizeDirection3D(float3 direction, float3 fallback)
        {
            float directionLength = length(direction);

            if (directionLength <= 0.0001)
                return fallback;

            return direction / directionLength;
        }

        float2 NormalizeDirection2D(float2 direction, float2 fallback)
        {
            float directionLength = length(direction);

            if (directionLength <= 0.0001)
                return fallback;

            return direction / directionLength;
        }

        float Random01(float2 value)
        {
            return frac(sin(dot(value, float2(12.9898, 78.233))) * 43758.5453);
        }

        float Box(float2 uv, float2 center, float2 size)
        {
            float2 distanceFromCenter = abs(uv - center);
            float2 halfSize = size * 0.5;

            return step(distanceFromCenter.x, halfSize.x) *
                   step(distanceFromCenter.y, halfSize.y);
        }

        float DrawDigit(float2 uv, int digit, float thickness)
        {
            float segmentA = Box(uv, float2(0.5, 0.88), float2(0.55, thickness));
            float segmentB = Box(uv, float2(0.78, 0.66), float2(thickness, 0.35));
            float segmentC = Box(uv, float2(0.78, 0.28), float2(thickness, 0.35));
            float segmentD = Box(uv, float2(0.5, 0.10), float2(0.55, thickness));
            float segmentE = Box(uv, float2(0.22, 0.28), float2(thickness, 0.35));
            float segmentF = Box(uv, float2(0.22, 0.66), float2(thickness, 0.35));
            float segmentG = Box(uv, float2(0.5, 0.49), float2(0.55, thickness));

            float result = 0.0;

            if (digit == 0)
                result = max(max(max(segmentA, segmentB), max(segmentC, segmentD)), max(segmentE, segmentF));

            if (digit == 1)
                result = max(segmentB, segmentC);

            if (digit == 2)
                result = max(max(max(segmentA, segmentB), segmentG), max(segmentE, segmentD));

            if (digit == 3)
                result = max(max(max(segmentA, segmentB), segmentG), max(segmentC, segmentD));

            if (digit == 4)
                result = max(max(segmentF, segmentG), max(segmentB, segmentC));

            if (digit == 5)
                result = max(max(max(segmentA, segmentF), segmentG), max(segmentC, segmentD));

            if (digit == 6)
                result = max(max(max(segmentA, segmentF), max(segmentE, segmentD)), max(segmentC, segmentG));

            if (digit == 7)
                result = max(max(segmentA, segmentB), segmentC);

            if (digit == 8)
                result = max(max(max(segmentA, segmentB), max(segmentC, segmentD)), max(max(segmentE, segmentF), segmentG));

            if (digit == 9)
                result = max(max(max(segmentA, segmentB), max(segmentC, segmentD)), max(segmentF, segmentG));

            return result;
        }

        float2 BuildNumberUV(float3 localPosition)
        {
            float angle01 = atan2(localPosition.x, localPosition.z) / 6.28318530718 + 0.5;
            float height01 = saturate(localPosition.y / max(_EnemyScale, 0.001) + 0.5);

            return float2(angle01, height01);
        }

        float GetVisualSpeedMultiplier(
            float investigateVisual,
            float chaseVisual,
            float chaseDistance01)
        {
            float chaseSpeedWeight = chaseVisual * lerp(0.35, 1.0, chaseDistance01);

            return
                1.0 +
                investigateVisual * _InvestigateSpeedBoost +
                chaseSpeedWeight * _ChaseSpeedBoost;
        }

        half3 GetNumberColor(float investigateVisual, float chaseVisual)
        {
            half3 color = _NumberColor.rgb;
            color = lerp(color, _InvestigateNumberColor.rgb, saturate(investigateVisual));
            color = lerp(color, _ChaseNumberColor.rgb, saturate(chaseVisual));

            return color;
        }

        float GetFallingNumberMask(float2 numberUV, float speedMultiplier)
        {
            float2 numberFlowDirection = NormalizeDirection2D(
                _NumberFlowDirection.xy,
                float2(0.0, 1.0)
            );

            numberUV += numberFlowDirection * _Time.y * _NumberSpeed * speedMultiplier;

            float2 grid = numberUV * float2(_NumberColumns, _NumberRows);
            float2 cellID = floor(grid);
            float2 cellUV = frac(grid);

            float randomValue = Random01(cellID);
            int digit = (int)floor(randomValue * 10.0);

            float digitMask = DrawDigit(cellUV, digit, _NumberThickness);
            float columnVariation = Random01(float2(cellID.x, 0.0));
            float fade = lerp(0.35, 1.0, columnVariation);

            return digitMask * fade * saturate(_NumbersEnabled);
        }

        ENDHLSL

        Pass
        {
            Name "GPUEnemyHologram"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM

            #pragma vertex HologramVertex
            #pragma fragment HologramFragment
            #pragma target 4.5

            struct HologramVaryings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
                float state : TEXCOORD3;
                float investigateVisual : TEXCOORD4;
                float chaseVisual : TEXCOORD5;
                float chaseDistance01 : TEXCOORD6;
            };

            HologramVaryings HologramVertex(Attributes input)
            {
                EnemyVertexData enemyVertex = BuildEnemyVertexData(input);

                HologramVaryings output;
                output.positionHCS = TransformWorldToHClip(enemyVertex.worldPosition);
                output.worldPos = enemyVertex.worldPosition;
                output.worldNormal = enemyVertex.worldNormal;
                output.viewDir = normalize(GetWorldSpaceViewDir(enemyVertex.worldPosition));
                output.state = enemyVertex.state;
                output.investigateVisual = enemyVertex.investigateVisual;
                output.chaseVisual = enemyVertex.chaseVisual;
                output.chaseDistance01 = enemyVertex.chaseDistance01;

                return output;
            }

            half4 HologramFragment(HologramVaryings input) : SV_Target
            {
                float rimGlow = 1.0 - saturate(dot(normalize(input.viewDir), normalize(input.worldNormal)));
                rimGlow = pow(rimGlow, _RimPower);

                float speedMultiplier = GetVisualSpeedMultiplier(
                    input.investigateVisual,
                    input.chaseVisual,
                    input.chaseDistance01
                );

                float3 scanlineDirection = NormalizeDirection3D(
                    _ScanlineFlowDirection.xyz,
                    float3(0.0, 1.0, 0.0)
                );

                float scanlinePosition = dot(input.worldPos, scanlineDirection);
                float scanLines = sin(scanlinePosition * _ScanlineDensity + _Time.y * _ScanlineSpeed * speedMultiplier);
                scanLines = scanLines * 0.5 + 0.5;

                float pulse = sin(_Time.y * 2.0 + input.worldPos.x * 0.31 + input.worldPos.z * 0.17) * 0.5 + 0.5;

                half3 stateTint = lerp(
                    _BaseColor.rgb,
                    half3(1.6, 0.1, 0.2),
                    saturate(input.chaseVisual)
                );

                float chaseGlow = input.chaseDistance01 * _ChaseGlowBoost;

                half3 finalColor =
                    stateTint * (_BaseAlpha + pulse * _PulseStrength + chaseGlow * 0.35) +
                    _RimColor.rgb * rimGlow * (1.0 + chaseGlow) +
                    _LineColor.rgb * scanLines * (0.3 + chaseGlow * 0.25);

                float finalAlpha =
                    _BaseAlpha +
                    rimGlow * _RimAlpha * (1.0 + chaseGlow * 0.6) +
                    scanLines * _ScanlineAlpha * (1.0 + chaseGlow * 0.4);

                return half4(finalColor, saturate(finalAlpha));
            }

            ENDHLSL
        }

        Pass
        {
            Name "FallingNumbersPass"

            Tags
            {
                "LightMode" = "SRPDefaultUnlit"
            }

            Blend SrcAlpha One
            Cull Off
            ZWrite Off

            HLSLPROGRAM

            #pragma vertex NumbersVertex
            #pragma fragment NumbersFragment
            #pragma target 4.5

            struct NumberVaryings
            {
                float4 positionHCS : SV_POSITION;
                float2 numberUV : TEXCOORD0;
                float investigateVisual : TEXCOORD1;
                float chaseVisual : TEXCOORD2;
                float chaseDistance01 : TEXCOORD3;
            };

            NumberVaryings NumbersVertex(Attributes input)
            {
                EnemyVertexData enemyVertex = BuildEnemyVertexData(input);

                NumberVaryings output;
                output.positionHCS = TransformWorldToHClip(enemyVertex.worldPosition);
                output.numberUV = BuildNumberUV(enemyVertex.localPosition);
                output.investigateVisual = enemyVertex.investigateVisual;
                output.chaseVisual = enemyVertex.chaseVisual;
                output.chaseDistance01 = enemyVertex.chaseDistance01;

                return output;
            }

            half4 NumbersFragment(NumberVaryings input) : SV_Target
            {
                float speedMultiplier = GetVisualSpeedMultiplier(
                    input.investigateVisual,
                    input.chaseVisual,
                    input.chaseDistance01
                );

                float digitMask = GetFallingNumberMask(input.numberUV, speedMultiplier);
                float alertAlphaBoost = input.investigateVisual * 0.15 + input.chaseDistance01 * 0.45;
                float alpha = digitMask * saturate(_NumberAlpha + alertAlphaBoost);

                half3 color = GetNumberColor(
                    input.investigateVisual,
                    input.chaseVisual
                ) * digitMask * (1.0 + input.chaseDistance01 * _ChaseGlowBoost);

                return half4(color, alpha);
            }

            ENDHLSL
        }
    }
}
