Shader "RuleGame/RuleWheel/SoftOrbHalo"
{
    Properties {
        _Color("Warm light",Color)=(1,.86,.59,1)
        [HideInInspector] _RendererColor("Renderer color",Color)=(1,1,1,1)
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline"}
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            struct Attributes { COMMON_2D_INPUTS half4 color:COLOR; };
            struct Varyings { COMMON_2D_OUTPUTS half4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial) half4 _Color; CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                SetUpSpriteInstanceProperties();
                input.positionOS=UnityFlipSprite(input.positionOS,unity_SpriteProps.xy);
                output.positionCS=TransformObjectToHClip(input.positionOS);
                output.uv=input.uv; output.color=input.color*_Color*unity_SpriteColor; return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float2 delta=input.uv*2-1;
                half radius=length(delta);
                half soft=exp(-dot(delta,delta)*5)*(1-smoothstep(.65,1,radius));
                half core=exp(-dot(delta,delta)*90);
                // 柔和填满的光晕，没有硬圆环、尖刺或爆炸粒子。HDR 核心由局部 Bloom 扩散。
                half3 color=lerp(input.color.rgb,half3(1,.98,.9)*2.1,core);
                return half4(color,soft*input.color.a);
            }
            ENDHLSL
        }
    }
}
