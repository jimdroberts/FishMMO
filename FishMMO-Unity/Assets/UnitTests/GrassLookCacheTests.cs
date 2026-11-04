using System;
using System.Linq;
using FishMMO.Client;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace FishMMO.UnitTests
{
	public class GrassLookCacheTests
	{
		[TestCase(false)]
		[TestCase(true)]
		public void CachedSelectorsMatchFullLooksAndPreserveTheTypeBit(bool published)
		{
			if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required");
			var shader=Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/TestHarness/World/Editor/GrassLookCacheProbe.compute"));
			var biomes=new Texture2D(16,16,TextureFormat.RGBA32,false,true); var palette=new Texture2D(12,28,TextureFormat.RGBAFloat,false,true);
			try
			{
				biomes.SetPixels32(Enumerable.Range(0,256).Select(i=>new Color32((byte)(i%4),0,0,255)).ToArray());biomes.Apply(false,true);
				var pixels=new Color[12*28];for(int biome=0;biome<4;biome++)for(int row=0;row<7;row++)for(int v=0;v<12;v++)pixels[(biome*7+row)*12+v]=new Color(biome*.2f+v*.01f,row*.1f+v*.01f,v*.03f,row==0?(v+1)/12f:.5f+v*.05f);
				palette.SetPixels(pixels);palette.Apply(false,true);
				var random=new System.Random(516);var points=Enumerable.Range(0,8192).Select(i=>new Vector2(i%11==0?-64:i%11==1?64:(float)(random.NextDouble()*180-90),(float)(random.NextDouble()*180-90))).ToArray();
				using var input=new GraphicsBuffer(GraphicsBuffer.Target.Structured,points.Length,8);input.SetData(points);
				using var output=new GraphicsBuffer(GraphicsBuffer.Target.Structured,points.Length*12,16);
				int kernel=shader.FindKernel("Probe");shader.SetBuffer(kernel,"_Points",input);shader.SetBuffer(kernel,"_Results",output);shader.SetInt("_PointCount",points.Length);
				shader.SetTexture(kernel,"_FishFloraBiomes",biomes);shader.SetTexture(kernel,"_FishFloraPalette",palette);shader.SetVector("_FishFloraInfo",new Vector4(published?1:0,16,16,4));shader.SetVector("_FishFloraRect",new Vector4(-64,-64,1f/128,1f/128));
				shader.Dispatch(kernel,(points.Length+63)/64,1,1);var result=new Vector4[points.Length*12];output.GetData(result);
				for(int i=0;i<result.Length;i+=2)Assert.That(result[i],Is.EqualTo(result[i+1]),"pair "+i);
				Assert.That(GrassMath.BladeStride,Is.EqualTo(24),"selectors use spare record bits");
			}
			finally{Object.DestroyImmediate(shader);Object.DestroyImmediate(biomes);Object.DestroyImmediate(palette);}
		}
		[TestCase(ShaderCompilerPlatform.Vulkan,BuildTarget.StandaloneLinux64)]
		[TestCase(ShaderCompilerPlatform.D3D,BuildTarget.StandaloneWindows64)]
		[TestCase(ShaderCompilerPlatform.WebGPU,BuildTarget.WebGL)]
		public void CachedGrassPassesCompile(ShaderCompilerPlatform platform,BuildTarget target)
		{
			var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Prefabs/Client/Weather/Shaders/FishGrassBlades.shader");var sub=ShaderUtil.GetShaderData(shader).GetSubshader(0);
			for(int pass=0;pass<sub.PassCount;pass++)
			{
				var result=sub.GetPass(pass).CompileVariant(ShaderType.Vertex,new[]{"_CLUSTER_LIGHT_LOOP","_MAIN_LIGHT_SHADOWS_CASCADE","_SHADOWS_SOFT"},platform,target);
				Assert.That(result.Success,Is.True,pass+" "+string.Join("\n",result.Messages.Select(m=>m.message)));Assert.That(result.Messages.Any(m=>m.severity==ShaderCompilerMessageSeverity.Error),Is.False);
			}
		}
	}
}
