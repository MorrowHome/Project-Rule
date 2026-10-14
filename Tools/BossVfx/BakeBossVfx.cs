using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Effekseer;
using Effekseer.Editor;

[InitializeOnLoad]
internal static class BossVfxBaker
{
    static BossVfxBaker() { EditorApplication.update += Check; }
    static void Check()
    {
        const string request = "Library/BossVfx/request";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(request)) return;
        string mode=File.ReadAllText(request).Trim(); File.Delete(request);
        try { Bake(mode == "atlas"); File.WriteAllText("Library/BossVfx/result", "OK"); }
        catch(Exception e) { Debug.LogException(e); File.WriteAllText("Library/BossVfx/result",e.ToString()); }
    }
    static void Bake(bool atlas)
    {
        var pipeline = GraphicsSettings.defaultRenderPipeline;
        var quality = QualitySettings.renderPipeline;
        var active = RenderTexture.active;
        var go = new GameObject("__BossVfxBakeCamera") { hideFlags=HideFlags.HideAndDontSave };
        Camera camera=go.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;
        camera.orthographicSize=8f;camera.aspect=1f;camera.nearClipPlane=.1f;camera.farClipPlane=200f;
        camera.transform.position=new Vector3(0,0,-50);camera.backgroundColor=Color.black;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.cullingMask=1<<30;
        var rt = new RenderTexture(512,512,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
        rt.Create();camera.targetTexture=rt;
        var read = new Texture2D(512,512,TextureFormat.RGBA32,false,true);
        string[] names={"Blow2","Claw1","Dark2","Gun3","Light1","Light4","Sword3"};
        try
        {
            // 离屏烘焙借用 Built-in 相机，finally 恢复项目的 URP 资产；不保存场景或项目设置。
            GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
            EffekseerSettings.Instance.RendererType=EffekseerRendererType.Unity;
            EffekseerSettings.Instance.enableDistortion=false;EffekseerSettings.Instance.enableDepth=false;
            if(EffekseerSystem.Instance!=null) { if(EffekseerSystem.Instance.renderer!=null) EffekseerSystem.Instance.OnDisable(); EffekseerSystem.Instance.TermPlugin(); } var system = new EffekseerSystem(); system.InitPlugin(); system.OnEnable();
            Directory.CreateDirectory(atlas?"Assets/_Project/Art/Vfx/Effekseer":"Library/BossVfx/Previews");
            foreach(string name in names)
            {
                var effect=AssetDatabase.LoadAssetAtPath<EffekseerEffectAsset>("Assets/__BossVfxBake/"+name+".asset");
                if(effect==null)throw new Exception("Missing imported effect "+name);
                EffekseerSystem.Instance.LoadEffect(effect);
                var handle=EffekseerSystem.PlayEffect(effect,Vector3.zero);handle.layer=30;
                // 地面效果原本在 XZ 平面，旋到屏幕 XY 平面；斩击保留作者的正视角。
                if(name=="Light1")handle.SetRotation(Quaternion.Euler(90,0,0));
                if(atlas)
                {
                    int frames=64, side=8, frameSize=512;
                    var sheet = new Texture2D(side*frameSize,side*frameSize,TextureFormat.RGBA32,false,true);
                    var small=new RenderTexture(frameSize,frameSize,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);small.Create();
                    var pixels=new Texture2D(frameSize,frameSize,TextureFormat.RGBA32,false,true);
                    camera.targetTexture=small;
                    for(int f=0;f<frames;f++)
                    {
                        handle.UpdateHandleToMoveToFrame(f*2f);EffekseerSystem.Instance.UpdateRendererState();camera.Render();RenderTexture.active=small;
                        pixels.ReadPixels(new Rect(0,0,frameSize,frameSize),0,0);pixels.Apply();
                        Color[] colors=pixels.GetPixels();
                        for(int i=0;i<colors.Length;i++)
                        {
                            Color c=colors[i];float a=Mathf.Max(c.r,Mathf.Max(c.g,c.b));
                            colors[i]=a>.002f?new Color(c.r/a,c.g/a,c.b/a,a):Color.clear;
                        }
                        sheet.SetPixels(f%side*frameSize,(side-1-f/side)*frameSize,frameSize,frameSize,colors);
                    }
                    sheet.Apply();File.WriteAllBytes("Assets/_Project/Art/Vfx/Effekseer/"+name+".png",sheet.EncodeToPNG());
                    camera.targetTexture=rt;UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(sheet);small.Release();UnityEngine.Object.DestroyImmediate(small);
                }
                else
                {
                    handle.UpdateHandleToMoveToFrame(24);EffekseerSystem.Instance.UpdateRendererState();camera.Render();RenderTexture.active=rt;
                    read.ReadPixels(new Rect(0,0,512,512),0,0);read.Apply();
                    File.WriteAllBytes("Library/BossVfx/Previews/"+name+".png",read.EncodeToPNG());
                }
                handle.Stop();
            }
            if(atlas)AssetDatabase.Refresh();
        }
        finally
        {
            if(EffekseerSystem.Instance!=null) { if(EffekseerSystem.Instance.renderer!=null) EffekseerSystem.Instance.OnDisable(); EffekseerSystem.Instance.TermPlugin(); } GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;RenderTexture.active=active;
            camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(read);UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
