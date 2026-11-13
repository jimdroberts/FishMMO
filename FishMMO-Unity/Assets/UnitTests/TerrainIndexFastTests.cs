using System;
using System.Linq;
using System.Runtime.InteropServices;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
namespace FishMMO.UnitTests
{
	public class TerrainIndexFastTests
	{
		[StructLayout(LayoutKind.Sequential)] private struct Point { public Vector2Int A,B,C,D; public Vector2 F,Pad; }
		[TestCase(false)]
		[TestCase(true)]
		public void CommonLayersAndBoundaryFallbackMatchTheGeneralGpuMerge(bool forceFull)
		{
			if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required");
			var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ProjectFiles.Path("Assets/TestHarness/World/Editor/TerrainIndexFilterProbe.compute")));
			try
			{
				var random = new System.Random(926); var points = new Point[16000];
				uint Pack(int[] ids) => (uint)ids[0] | (uint)ids[1]<<8 | (uint)ids[2]<<16 | (uint)ids[3]<<24;
				for (int i = 0; i < points.Length; i++)
				{
					int[] ids = Enumerable.Range(0,256).OrderBy(_=>random.Next()).Take(4).ToArray();
					Vector2Int Texel(bool common)
					{
						uint index = common ? Pack(ids) : Pack(Enumerable.Range(0,4).Select(_=>random.Next(256)).ToArray());
						uint weights = i % 13 == 0 ? 0u : Pack(Enumerable.Range(0,4).Select(_=>random.Next(256)).ToArray());
						return new Vector2Int(unchecked((int)index),unchecked((int)weights));
					}
					var a = Texel(true); var b = Texel(i%4!=0); var c = Texel(i%4!=0); var d = Texel(i%4!=0);
					if (i%7==0) a.x=b.x=c.x=d.x=0; // malformed duplicate ids must take the general path.
					float f = i%5==0 ? 0 : i%5==1 ? 1 : i%5==2 ? .5f : i%5==3 ? 1e-7f : (float)random.NextDouble();
					points[i] = new Point { A=a,B=b,C=c,D=d,F=new Vector2(f,(float)random.NextDouble()) };
				}
				using var input = new GraphicsBuffer(GraphicsBuffer.Target.Structured,points.Length,48); input.SetData(points);
				using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured,points.Length*4,16);
				int kernel = shader.FindKernel("Probe"); shader.SetBuffer(kernel,"_Points",input); shader.SetBuffer(kernel,"_Results",output); shader.SetInt("_PointCount",points.Length); shader.SetFloat("_FishIndexFullFilter",forceFull?1:0); shader.Dispatch(kernel,(points.Length+63)/64,1,1);
				var result = new Vector4[points.Length*4]; output.GetData(result);
				for (int i = 0; i < points.Length; i++) { Assert.That(Vector4.Distance(result[i*4],result[i*4+1]),Is.LessThan(1e-6f),"weights "+i); Assert.That(result[i*4+2],Is.EqualTo(result[i*4+3]),"ids "+i); }
			}
			finally { Object.DestroyImmediate(shader); }
		}
		[TestCase(ShaderCompilerPlatform.Vulkan,BuildTarget.StandaloneLinux64)]
		[TestCase(ShaderCompilerPlatform.D3D,BuildTarget.StandaloneWindows64)]
		[TestCase(ShaderCompilerPlatform.WebGPU,BuildTarget.WebGL)]
		public void FastTerrainShaderCompiles(ShaderCompilerPlatform platform,BuildTarget target)
		{
			var shader = AssetDatabase.LoadAssetAtPath<Shader>(ProjectFiles.Path("Assets/Prefabs/Client/Weather/Shaders/FishWeatherTerrainArrayGpu.shader"));
			foreach (var stage in new[] { ShaderType.Vertex,ShaderType.Fragment })
			{
				var result = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0).CompileVariant(stage,new[] {"_CLUSTER_LIGHT_LOOP","_MAIN_LIGHT_SHADOWS_CASCADE","_SHADOWS_SOFT"},platform,target);
				Assert.That(result.Success,Is.True,string.Join("\n",result.Messages.Select(m=>m.message))); Assert.That(result.Messages.Any(m=>m.severity==ShaderCompilerMessageSeverity.Error),Is.False);
			}
		}
	}
}
