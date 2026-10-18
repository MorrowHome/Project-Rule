Shader "RuleGame/Player/SwordReflection"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _LightPosition ("Light world position", Vector) = (0,0,0,0)
        _Radius ("Light radius", Float) = 1
        _Intensity ("Light intensity", Float) = 0
        [HideInInspector] _RendererColor ("Renderer color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" }
        Blend SrcAlpha One
        Cull Off
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            struct Attributes { COMMON_2D_INPUTS half4 color : COLOR; };
            struct Varyings { COMMON_2D_OUTPUTS half4 color : COLOR; float2 world : TEXCOORD1; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _LightPosition;
                float _Radius, _Intensity;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.world = TransformObjectToWorld(input.positionOS).xy;
                output.uv = input.uv;
                output.color = input.color * unity_SpriteColor;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 sprite = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                float distance = length(input.world - _LightPosition.xy) / max(.01, _Radius);
                half falloff = pow(saturate(1 - distance), 2);
                half3 reflection = (sprite.rgb * .7 + .2) * half3(.48, .73, 1);
                return half4(reflection, sprite.a * falloff * _Intensity);
            }
            ENDHLSL
        }
    }
}
