Shader "RuleGame/RuleWheel/OrbBody"
{
    Properties
    {
        [PerRendererData] _MainTex ("Body",2D)="white"{}
        _Color("Tint",Color)=(1,1,1,1)
        _OrbPosition("Orb world position",Vector)=(0,0,0,0)
        _GlowStrength("Reflected light",Float)=0
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
            struct Varyings { COMMON_2D_OUTPUTS half4 color:COLOR; float3 positionWS:TEXCOORD1; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _OrbPosition;
                half _GlowStrength;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                SetUpSpriteInstanceProperties();
                input.positionOS=UnityFlipSprite(input.positionOS,unity_SpriteProps.xy);
                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS=TransformObjectToWorld(input.positionOS);
                output.positionCS=TransformWorldToHClip(output.positionWS);
                output.uv=input.uv; output.color=input.color*_Color*unity_SpriteColor;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half4 body=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv)*input.color;
                float2 delta=input.positionWS.xy-_OrbPosition.xy;
                half light=exp(-dot(delta,delta)*3.8)*_GlowStrength;
                // 只给靠近手掌与脸侧的已有像素暖色反光，不添加人物轮廓或改变身体透明度。
                body.rgb=body.rgb*lerp(half3(1,1,1),half3(1.16,1.04,.85),light)
                    +half3(1,.8,.48)*light*.06;
                return body;
            }
            ENDHLSL
        }
    }
}
