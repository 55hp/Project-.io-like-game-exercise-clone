Shader "Custom/RadialReveal"
{
    Properties
    {
        _BaseMap ("Base Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _RevealCenter ("Reveal Center (World)", Vector) = (0, 0, 0, 0)
        _RevealRadius ("Reveal Radius", Float) = 0
        _RevealSoftness ("Reveal Softness", Float) = 0.5
        [HDR] _EdgeColor ("Edge Glow Color", Color) = (1, 1, 1, 1)
        _EdgeWidth ("Edge Width", Float) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "RadialReveal"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float4 _RevealCenter;
                float _RevealRadius;
                float _RevealSoftness;
                half4 _EdgeColor;
                float _EdgeWidth;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 delta = input.positionWS.xz - _RevealCenter.xz;
                float dist = length(delta);

                // Hard cutoff with soft edge via smoothstep.
                float softness = max(_RevealSoftness, 0.001);
                float revealAlpha = 1.0 - smoothstep(_RevealRadius - softness, _RevealRadius, dist);

                // Clip fully outside pixels.
                clip(revealAlpha - 0.001);

                // Base color. The texture contributes RGB only — its alpha must NOT drive the
                // zone's opacity (otherwise a texture with low/zero alpha makes the area vanish).
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 baseRGB  = texColor.rgb * _BaseColor.rgb;

                // Leading-edge glow: a band of width _EdgeWidth just INSIDE the current reveal
                // radius. Because it is measured relative to _RevealRadius, it tracks the sweeping
                // front during the conquest animation regardless of how large the radius is.
                float edgeInner = _RevealRadius - max(_EdgeWidth, 0.0001);
                float edgeFactor = smoothstep(edgeInner, _RevealRadius, dist);
                edgeFactor = saturate(edgeFactor) * revealAlpha;

                // Edge color is HDR (can exceed 1) so Bloom picks up the moving front.
                half3 finalColor = lerp(baseRGB, _EdgeColor.rgb, edgeFactor);

                // Opacity depends only on the zone's own alpha and the reveal mask — never on the texture alpha.
                half finalAlpha = _BaseColor.a * revealAlpha;

                return half4(finalColor, finalAlpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
