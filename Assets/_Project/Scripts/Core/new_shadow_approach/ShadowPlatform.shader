Shader "Custom/ShadowPlatform"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0, 0, 0, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1.0
        _SoftnessPower ("Softness Falloff Power", Range(0.2, 4.0)) = 1.5
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent" 
            "RenderPipeline" = "UniversalPipeline" 
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Offset -1, -1

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
                UNITY_DEFINE_INSTANCED_PROP(float, _Alpha)
                UNITY_DEFINE_INSTANCED_PROP(float, _SoftnessPower)
            UNITY_INSTANCING_BUFFER_END(Props)

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float4 baseColor = UNITY_ACCESS_INSTANCED_PROP(Props, _BaseColor);
                float alphaProp = UNITY_ACCESS_INSTANCED_PROP(Props, _Alpha);
                float power = UNITY_ACCESS_INSTANCED_PROP(Props, _SoftnessPower);

                // uv.x is 0.0 at core and 1.0 at outer boundary
                float edgeFactor = 1.0 - saturate(input.uv.x);
                float smoothEdge = smoothstep(0.0, 1.0, edgeFactor);
                smoothEdge = pow(max(0.0001, smoothEdge), power);

                // Use smoothEdge for feather falloff and alphaProp for distance opacity (prevents squaring)
                float finalAlpha = baseColor.a * alphaProp * smoothEdge;
                half3 rgb = baseColor.rgb * input.color.rgb;

                return half4(rgb, finalAlpha);
            }
            ENDHLSL
        }
    }

    // Fallback for Built-in Pipeline or Scene View
    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            "Queue"="Transparent" 
            "IgnoreProjector"="True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Offset -1, -1

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _BaseColor;
            float _Alpha;
            float _SoftnessPower;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float edgeFactor = 1.0 - saturate(i.uv.x);
                float smoothEdge = smoothstep(0.0, 1.0, edgeFactor);
                float p = _SoftnessPower > 0.0 ? _SoftnessPower : 1.0;
                smoothEdge = pow(max(0.0001, smoothEdge), p);

                float finalAlpha = _BaseColor.a * _Alpha * smoothEdge;
                return fixed4(_BaseColor.rgb * i.color.rgb, finalAlpha);
            }
            ENDCG
        }
    }
    FallBack "Sprites/Default"
}
