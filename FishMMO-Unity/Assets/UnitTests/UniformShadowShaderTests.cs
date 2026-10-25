using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.UnitTests
{
    public class UniformShadowShaderTests
    {
        private string folder;
        private Material material;
        private RenderTexture shadow, output;

        [OneTimeSetUp]
        public void CreateFixture()
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)
                Assert.Ignore("Requires a graphics device for shadow comparison sampling.");
            folder="Assets/ShadowFilteringFixture_"+Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(folder);
            string common=@"
                #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
                struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
                V Vert(uint id:SV_VertexID) { V o; o.positionCS=GetFullScreenTriangleVertexPosition(id); o.uv=GetFullScreenTriangleTexCoord(id); return o; }
                float _TestQuality;
                TEXTURE2D_SHADOW(_TestMap); SAMPLER_CMP(sampler_TestMap);
            ";
            string sample=@"
                #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl""
                half4 Frag(V i):SV_Target {
                    ShadowSamplingData data=(ShadowSamplingData)0;
                    data.shadowmapSize=float4(1.0/32.0,1.0/32.0,32.0,32.0);
                    data.shadowOffset0=float4(-0.5,-0.5,0.5,-0.5)/32.0;
                    data.shadowOffset1=float4(-0.5,0.5,0.5,0.5)/32.0;
                    data.softShadowQuality=_TestQuality;
                    half a=SampleShadowmap(TEXTURE2D_SHADOW_ARGS(_TestMap,sampler_TestMap),float4(i.uv,0.5,1),data,half4(1,_TestQuality,0,0),false);
                    return half4(a,a,a,1);
                }
            ";
            string Pass(string define,string fragment,bool depth=false) =>
                "Pass { Cull Off ZTest Always ZWrite "+(depth?"On":"Off")+" HLSLPROGRAM\n#pragma target 4.5\n#pragma vertex Vert\n#pragma fragment "+(depth?"Depth":"Frag")+"\n"+
                define+"\n"+common+fragment+"\nENDHLSL\n}\n";
            string shader="Shader \"Hidden/FishMMO/ShadowFilteringFixture\" { SubShader {\n"+
                Pass("","float Depth(V i):SV_Depth { return (((uint)(floor(i.uv.x*8)+floor(i.uv.y*8)))&1u)==0u?0.3:0.7; }",true)+
                Pass("",sample)+Pass("#define _SHADOWS_SOFT 1",sample)+
                Pass("#include \"Assets/Prefabs/Client/Weather/Shaders/FishShadowVariants.hlsl\"",sample)+"} }";
            string path=folder+"/Fixture.shader";File.WriteAllText(path,shader);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var asset=AssetDatabase.LoadAssetAtPath<Shader>(path);
            Assert.That(ShaderUtil.ShaderHasError(asset),Is.False);
            material=new Material(asset);
            shadow=new RenderTexture(32,32,24,RenderTextureFormat.Shadowmap) {filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            shadow.Create();
            output=new RenderTexture(32,32,0,RenderTextureFormat.ARGBFloat);output.Create();
            using(var cmd=new CommandBuffer())
            {
                cmd.SetRenderTarget(shadow);
                cmd.ClearRenderTarget(true,false,Color.black,1);
                cmd.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,3);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            material.SetTexture("_TestMap",shadow);
        }

        private Color[] Capture(int pass)
        {
            using(var cmd=new CommandBuffer())
            {
                cmd.SetRenderTarget(output);cmd.ClearRenderTarget(false,true,Color.magenta);
                cmd.DrawProcedural(Matrix4x4.identity,material,pass,MeshTopology.Triangles,3);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            var previous=RenderTexture.active;
            var pixels=new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);
            try
            {
                RenderTexture.active=output;pixels.ReadPixels(new Rect(0,0,32,32),0,0);pixels.Apply();
                return pixels.GetPixels();
            }
            finally {RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void UniformFilteringMatchesUrpHardAndEverySoftQuality(int quality)
        {
            material.SetFloat("_TestQuality",quality);
            Color[] expected=Capture(quality==0?1:2),actual=Capture(3);
            float min=1,max=0;
            for(int i=0;i<expected.Length;i++)
            {
                min=Mathf.Min(min,expected[i].r);max=Mathf.Max(max,expected[i].r);
                Assert.That(float.IsNaN(actual[i].r),Is.False);
                Assert.That(actual[i].r,Is.EqualTo(expected[i].r).Within(0.00001f),"pixel "+i+", shadow quality "+quality);
            }
            if(quality==0)Assert.That(max-min,Is.GreaterThan(0.5f),"the depth fixture must contain lit and shadowed texels");
        }

        [OneTimeTearDown]
        public void Cleanup()
        {
            if(material!=null)UnityEngine.Object.DestroyImmediate(material);
            if(shadow!=null){shadow.Release();UnityEngine.Object.DestroyImmediate(shadow);}
            if(output!=null){output.Release();UnityEngine.Object.DestroyImmediate(output);}
            if(folder!=null)AssetDatabase.DeleteAsset(folder);
        }
    }
}
