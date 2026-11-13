using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Client;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
    public class GrassEarlyWorkTests
    {
        const string ComputePath = "Assets/Prefabs/Client/Weather/Shaders/FishGrassBlades.compute";
        static uint Bits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value));

        [Test]
        public void PreparedColourMatchesReferenceAcrossBytesMipsWrappingAndPackedBladeColours()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required");
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ProjectFiles.Path("Assets/TestHarness/World/Editor/GrassColourPreparedProbe.compute")));
            try
            {
                int pyramid = GrassAlbedoTexels.PyramidTexels(128,out int mips), start = GrassTerrainAtlas.TerrainUints + GrassAlbedoTexels.HeaderUints;
                int linearBase = start + pyramid; var data = new uint[linearBase + 256];
                data[19]=Bits(1); data[20]=Bits(128); data[21]=Bits(mips); data[22]=Bits(pyramid); data[23]=(uint)GrassTerrainAtlas.TerrainUints;
                var rng = new System.Random(51273);
                for(int i=start;i<linearBase;i++)data[i]=(uint)rng.Next(1<<24)|0xff000000u;
                // Every sRGB byte occurs in each component, in the first mip.
                for(uint i=0;i<256;i++)data[start+i]=i|((255-i)<<8)|(((i*97)&255)<<16)|0xff000000u;
                using var shared = new GraphicsBuffer(GraphicsBuffer.Target.Structured,data.Length,4); shared.SetData(data);
                shader.SetInt("_GrassLinearTableBase",linearBase);
                var prepare=Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ProjectFiles.Path(ComputePath)));
                try { int prep=prepare.FindKernel("FishGrassPrepareColour"); prepare.SetInt("_GrassLinearTableBase",linearBase); prepare.SetBuffer(prep,"_GrassColourTableOut",shared); prepare.Dispatch(prep,1,1,1); }
                finally { Object.DestroyImmediate(prepare); }
                var points = new Vector2[8192];
                for(int i=0;i<points.Length;i++)points[i]=i<256 ? new Vector2((i%128+.5f)/128f,(i/128+.5f)/128f) : new Vector2((float)rng.NextDouble()*6-3,(float)rng.NextDouble()*6-3);
                using var input = new GraphicsBuffer(GraphicsBuffer.Target.Structured,points.Length,8); input.SetData(points);
                using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured,points.Length,16);
                int check=shader.FindKernel("CheckColour");shader.SetBuffer(check,"_GrassShared",shared);shader.SetBuffer(check,"_ColourPoints",input);shader.SetBuffer(check,"_ColourResults",output);shader.SetInt("_ColourPointCount",points.Length);
                var reference=new Vector4[points.Length];var prepared=new Vector4[points.Length];
                shader.SetInt("_GrassUsePreparedColour",0);shader.Dispatch(check,points.Length/64,1,1);output.GetData(reference);
                shader.SetInt("_GrassUsePreparedColour",1);shader.Dispatch(check,points.Length/64,1,1);output.GetData(prepared);
                for(int i=0;i<points.Length;i++)
                {
                    Assert.That(Vector3.Distance((Vector3)reference[i],(Vector3)prepared[i]),Is.LessThan(5e-7f),"filtered RGB "+i);
                    Assert.That(prepared[i].w,Is.EqualTo(reference[i].w),"packed blade colour "+i);
                }
                var actual=new uint[data.Length];shared.GetData(actual);
                for(int i=0;i<linearBase;i++)Assert.That(actual[i],Is.EqualTo(data[i]),"Preparation modified atlas data "+i);
                for(int i=0;i<256;i++)Assert.That(BitConverter.Int32BitsToSingle((int)actual[linearBase+i]),Is.InRange(-1e-6f,1.000001f));
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HiddenTilesSkipMainWorkButPreserveShadowWork(bool shadows)
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required");
            var shader=Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ProjectFiles.Path(ComputePath)));
            var depth=new Texture2D(32,32,TextureFormat.RFloat,false,true);
            try
            {
                depth.SetPixels(Enumerable.Repeat(Color.white,1024).ToArray());depth.Apply();
                using var tiles=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,16); tiles.SetData(new uint[]{0,0,0,0});
                using var items=new GraphicsBuffer(GraphicsBuffer.Target.Structured,256,16);
                using var counts=new GraphicsBuffer(GraphicsBuffer.Target.Raw,1,4);
                using var layout=new GraphicsBuffer(GraphicsBuffer.Target.Raw,2,4);layout.SetData(new uint[]{0,256});
                int k=shader.FindKernel("FishGrassGather");shader.SetBuffer(k,"_GrassTiles",tiles);shader.SetBuffer(k,"_GrassItemsOut",items);shader.SetBuffer(k,"_GrassGatherCounts",counts);shader.SetBuffer(k,"_GrassGatherLayout",layout);
                shader.SetInt("_GrassTileCount",1);shader.SetInt("_GrassRingCount",0);shader.SetVector("_GrassLattice",new Vector4(.1f,.15f,0,0));shader.SetVector("_GrassCamera",new Vector4(0,0,-50,1000));shader.SetVector("_GrassLight",shadows?new Vector4(0,-1,0,1000):Vector4.zero);shader.SetVectorArray("_GrassPlanes",Enumerable.Repeat(new Vector4(0,0,0,100),6).ToArray());
                shader.SetTexture(k,"_FishHiZ",depth);shader.SetMatrix("_FishHiZViewProj",Matrix4x4.Scale(new Vector3(1f/128,1f/128,1f/1000)));shader.SetVector("_FishHiZEye",new Vector4(0,0,-50,0));shader.SetVector("_FishHiZForward",new Vector4(0,0,1,0));
                uint Count(bool enabled,out uint[] records)
                {
                    counts.SetData(new uint[]{0});shader.SetVector("_FishHiZParams",new Vector4(32,32,1,enabled?1:0));shader.Dispatch(k,1,1,1);var n=new uint[1];counts.GetData(n);records=new uint[256*4];items.GetData(records);return n[0];
                }
                uint full=Count(false,out var before),culled=Count(true,out var after);Assert.That(full,Is.GreaterThan(0));
                if(!shadows)Assert.That(culled,Is.Zero);
                else
                {
                    Assert.That(culled,Is.EqualTo(full));
                    for(int i=0;i<full;i++){Assert.That(after[i*4],Is.EqualTo(before[i*4]));Assert.That(after[i*4+1],Is.EqualTo(before[i*4+1]));Assert.That(after[i*4+2],Is.EqualTo(before[i*4+2]&~8u));Assert.That(after[i*4+3],Is.EqualTo(before[i*4+3]));}
                }
                // A large motion margin puts the box across the near plane: retain it conservatively.
                shader.SetVector("_FishHiZEye",new Vector4(0,0,-50,100));
                Assert.That(Count(true,out var moved),Is.EqualTo(full)); Assert.That(moved[2]&8u,Is.EqualTo(8u));
                shader.SetVector("_FishHiZEye",new Vector4(0,0,-50,0));
                var offscreen=Matrix4x4.Scale(new Vector3(1f/128,1f/128,1f/1000));offscreen.m03=3;
                shader.SetMatrix("_FishHiZViewProj",offscreen);
                Assert.That(Count(true,out var outside),Is.EqualTo(full)); Assert.That(outside[2]&8u,Is.EqualTo(8u));
                shader.SetMatrix("_FishHiZViewProj",Matrix4x4.Scale(new Vector3(1f/128,1f/128,1f/1000)));
                // A clear depth buffer must restore the main view next frame, without rebuilding source tiles.
                depth.SetPixels(Enumerable.Repeat(new Color(10000,0,0,1),1024).ToArray());depth.Apply();
                Assert.That(Count(true,out var restored),Is.EqualTo(full)); Assert.That(restored[2]&8u,Is.EqualTo(8u));
            }
            finally { Object.DestroyImmediate(shader);Object.DestroyImmediate(depth); }
        }

        [TestCase(ShaderCompilerPlatform.Vulkan,BuildTarget.StandaloneLinux64)]
        [TestCase(ShaderCompilerPlatform.D3D,BuildTarget.StandaloneWindows64)]
        [TestCase(ShaderCompilerPlatform.WebGPU,BuildTarget.WebGL)]
        public void InlandFoamShortcutCompilesForClientTargets(ShaderCompilerPlatform platform,BuildTarget target)
        {
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(ProjectFiles.Path("Assets/Plugins/FishMMO Water/Shaders/FishInlandWater.shader"));
            foreach(var keywords in new[] {
                new[]{"_MAIN_LIGHT_SHADOWS_CASCADE","_CLUSTER_LIGHT_LOOP"},
                new[]{"_MAIN_LIGHT_SHADOWS_CASCADE","_CLUSTER_LIGHT_LOOP","_WATER_DEPTH","_WATER_REFRACTION"} })
            foreach(var stage in new[]{ShaderType.Vertex,ShaderType.Fragment})
            {
                var result=ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0).CompileVariant(stage,keywords,platform,target);
                Assert.That(result.Success,Is.True,string.Join("\n",result.Messages.Select(m=>m.message)));
                Assert.That(result.Messages.Any(m=>m.severity==ShaderCompilerMessageSeverity.Error),Is.False);
            }
        }
    }
}
