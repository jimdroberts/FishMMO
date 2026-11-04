using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FishMMO.Client;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The WebGPU light table (AdditionalLightTable, FishLightTable*.hlsl): URP's per-light arrays leave every lit program's
	/// $Globals there, the table fits the device's storage-buffer budget, its layout agrees across C#, the kernel and the
	/// shaders, the kernel copies exactly what URP sets, and native platforms are untouched.
	/// </summary>
	public class WebGpuLightTableTests
	{
		/// <summary>The arrays URP declares per visible light / shadow slice / Forward+ reflection probe.</summary>
		static readonly string[] UrpLightArrays =
		{
			"_AdditionalLightsPosition","_AdditionalLightsColor","_AdditionalLightsAttenuation","_AdditionalLightsSpotDir",
			"_AdditionalLightsOcclusionProbes","_AdditionalLightsLayerMasks","_AdditionalShadowParams","_AdditionalLightsWorldToShadow",
			"_AdditionalLightsWorldToLights","_AdditionalLightsCookieAtlasUVRects","_AdditionalLightsLightTypes","_AdditionalLightsCookieEnableBits",
			"urp_ReflProbes_BoxMax","urp_ReflProbes_BoxMin","urp_ReflProbes_ProbePosition","urp_ReflProbes_MipScaleOffset","urp_ReflProbes_Rotation",
		};

		/// <summary>WebGPU's default maxStorageBuffersPerShaderStage.</summary>
		const int WebGpuStorageBuffersPerStage = 8;

		// The heaviest lighting state the browser runs (as WebGpuRenderingCompatibilityTests), with additional-light shadows,
		// cookies and light layers on so every array path is compiled.
		static readonly string[] HeavyKeywords = {"_CLUSTER_LIGHT_LOOP","_ADDITIONAL_LIGHTS","_MAIN_LIGHT_SHADOWS_CASCADE","_ADDITIONAL_LIGHT_SHADOWS","_SHADOWS_SOFT",
			"_REFLECTION_PROBE_BLENDING","_REFLECTION_PROBE_BOX_PROJECTION","_REFLECTION_PROBE_ATLAS","_SCREEN_SPACE_OCCLUSION","_LIGHT_LAYERS","_LIGHT_COOKIES",
			"_NORMALMAP","_EMISSION","_ALPHATEST_ON","_FISH_LOOK_TABLE","_FISH_LOOK_ARRAY"};

		const string CookieHeader = "FishLightCookieVariants.hlsl";

		/// <summary>Every pass (shader path, subshader, pass index, name) whose program includes the shared cookie header, i.e. reads the table on WebGPU.</summary>
		static IEnumerable<(string path,ShaderData.Pass pass,string name,bool twin)> TablePasses()
		{
			foreach(string file in Directory.GetFiles("Assets","*.shader",SearchOption.AllDirectories))
			{
				string source=File.ReadAllText(file);
				if(!source.Contains(CookieHeader))continue;
				var shader=AssetDatabase.LoadAssetAtPath<Shader>(file.Replace('\\','/'));
				Assert.That(shader,Is.Not.Null,file);
				// Program bodies in declaration order, matched to passes by name.
				var programs=Regex.Matches(source,"Name\\s+\"([^\"]+)\"(.*?)ENDHLSL",RegexOptions.Singleline).Cast<Match>()
					.Where(m=>m.Groups[2].Value.Contains(CookieHeader)).Select(m=>m.Groups[1].Value).ToHashSet();
				bool twin=source.Contains("FishIndirectInstancing.hlsl");
				var data=ShaderUtil.GetShaderData(shader);
				for(int s=0;s<data.SubshaderCount;s++)
				{
					var sub=data.GetSubshader(s);
					for(int p=0;p<sub.PassCount;p++)
						if(programs.Contains(sub.GetPass(p).Name))yield return (file,sub.GetPass(p),Path.GetFileNameWithoutExtension(file)+" "+sub.GetPass(p).Name,twin);
				}
			}
		}

		static IEnumerable<string[]> KeywordSets(bool twin)
		{
			yield return HeavyKeywords;
			if(!twin)yield break;
			foreach(string instancing in new[]{"DOTS_INSTANCING_ON","PROCEDURAL_INSTANCING_ON"})
				foreach(bool first in new[]{false,true})
				{
					var k=HeavyKeywords.Concat(new[]{instancing});
					if(first)k=k.Concat(new[]{WebGpuDrawCapabilities.FirstInstanceKeyword});
					yield return k.ToArray();
				}
		}

		// Every table pass's WebGPU compile, once per run (the two budget tests read the same programs).
		static List<(string label,ShaderData.VariantCompileInfo result)> compiled;

		static List<(string label,ShaderData.VariantCompileInfo result)> Compiled()
		{
			if(compiled!=null)return compiled;
			var list=new List<(string,ShaderData.VariantCompileInfo)>();
			foreach(var (_,pass,name,twin) in TablePasses())
				foreach(string[] keywords in KeywordSets(twin))
				{
					string label=name+" ["+string.Join(" ",keywords.Where(k=>k.Contains("INSTANC")))+"]";
					var result=pass.CompileVariant(ShaderType.Vertex,keywords,ShaderCompilerPlatform.WebGPU,BuildTarget.WebGL);
					Assert.That(result.Success,Is.True,label+": "+string.Join("\n",result.Messages.Select(m=>m.message+" "+m.file+":"+m.line)));
					var globals=result.ConstantBuffers.Where(b=>b.Name.Contains("Globals")).ToArray();
					Debug.Log("[WebGPU light table] "+label+" globals "+string.Join(", ",globals.Select(b=>b.Name+"("+b.Size+" B, "+b.Fields.Length+" fields)"))
						+" storage "+result.ResourceBindings.Count(r=>r.Kind==ShaderData.ResourceKind.Buffer)+": "
						+string.Join(", ",result.ResourceBindings.Where(r=>r.Kind==ShaderData.ResourceKind.Buffer).Select(r=>r.Name)));
					list.Add((label,result));
				}
			return compiled=list;
		}

		// The point of the table: no lit program's constants carry a per-light array on WebGPU, and every such pass still compiles.
		[Test]
		public void LitPassesCarryNoUrpLightArraysOnWebGpu()
		{
			var all=Compiled();
			Assert.That(all.Select(c=>c.label.Split(' ')[0]).Distinct().Count(),Is.GreaterThan(20),"too few table shaders found");
			foreach(var (label,result) in all)
				foreach(var buffer in result.ConstantBuffers)
				{
					var arrays=buffer.Fields.Select(f=>f.Name).Intersect(UrpLightArrays).ToArray();
					Assert.That(arrays,Is.Empty,label+" "+buffer.Name+" still carries "+string.Join(", ",arrays));
				}
		}

		// The table is one more storage buffer: every table program (the union of its stages, which bounds each stage) within
		// WebGPU's default per-stage limit, with the heaviest keywords and the BatchRendererGroup / indirect / first-instance forms.
		[Test]
		public void TablePassesFitWebGpuStorageBuffersPerStage()
		{
			foreach(var (label,result) in Compiled())
			{
				var buffers=result.ResourceBindings.Where(r=>r.Kind==ShaderData.ResourceKind.Buffer).Select(r=>r.Name).ToArray();
				Assert.That(buffers.Length,Is.LessThanOrEqualTo(WebGpuStorageBuffersPerStage),label+": "+string.Join(", ",buffers));
			}
		}

		// The redirect is live where the views' light falls (not merely compiled out): the hot lit passes bind the table.
		[TestCase("FishWeatherLitIndirect ForwardLit [DOTS_INSTANCING_ON FISH_INDIRECT_FIRST_INSTANCE]")]
		[TestCase("FishWeatherLitIndirect ForwardLit [PROCEDURAL_INSTANCING_ON]")]
		[TestCase("FishVegetationIndirect ForwardLit [DOTS_INSTANCING_ON FISH_INDIRECT_FIRST_INSTANCE]")]
		[TestCase("FishVegetationIndirect ForwardLit [PROCEDURAL_INSTANCING_ON]")]
		[TestCase("FishWeatherLit ForwardLit []")]
		[TestCase("FishWeatherTerrainArrayGpu ForwardLit []")]
		[TestCase("FishBackdropGround ForwardLit []")]
		[TestCase("FishGrassBlades ForwardLit []")]
		[TestCase("FishWorldHlod ForwardLit []")]
		public void HotLitPassesReadTheTable(string label)
		{
			var match=Compiled().Where(c=>c.label==label).ToArray();
			Assert.That(match.Length,Is.EqualTo(1),"no program "+label);
			Assert.That(match[0].result.ResourceBindings.Select(r=>r.Name),Does.Contain("_FishLightTable"),label);
		}

		static int Define(string source,string name)
		{
			var m=Regex.Match(source,@"#define\s+"+name+@"\s+\(?(\d+)\)?\s");
			Assert.That(m.Success,Is.True,name+" not a number");
			return int.Parse(m.Groups[1].Value);
		}

		// One layout: the shaders' and the kernel's rows, the buffer C# allocates, and URP's own limits on WebGPU.
		[Test]
		public void LayoutAgreesAcrossCSharpShadersAndUrp()
		{
			string layout=File.ReadAllText("Assets/Prefabs/Client/Weather/Shaders/FishLightTableLayout.hlsl");
			Assert.That(Define(layout,"FISH_LIGHT_TABLE_LIGHTS"),Is.EqualTo(AdditionalLightTable.Lights));
			Assert.That(Define(layout,"FISH_LIGHT_TABLE_PROBES"),Is.EqualTo(AdditionalLightTable.Probes));
			Assert.That(Define(layout,"FISH_LIGHT_TABLE_LIGHT_ROWS"),Is.EqualTo(AdditionalLightTable.LightRows));
			Assert.That(Define(layout,"FISH_LIGHT_TABLE_SLICE_ROWS"),Is.EqualTo(AdditionalLightTable.SliceRows));
			Assert.That(Define(layout,"FISH_LIGHT_TABLE_PROBE_ROWS"),Is.EqualTo(AdditionalLightTable.ProbeRows));
			// The rows of a light: eight vectors and the four of its cookie matrix; of a probe: four and seven mip rects.
			Assert.That(Define(layout,"FISH_LIGHT_ROW_WORLD_TO_LIGHT")+4,Is.EqualTo(AdditionalLightTable.LightRows));
			Assert.That(Define(layout,"FISH_PROBE_ROW_MIP_SCALE_OFFSET")+7,Is.EqualTo(AdditionalLightTable.ProbeRows));
			Assert.That(AdditionalLightTable.Rows,Is.EqualTo(AdditionalLightTable.Lights*(AdditionalLightTable.LightRows+AdditionalLightTable.SliceRows)+AdditionalLightTable.Probes*AdditionalLightTable.ProbeRows+1));
			// URP's WebGPU light cap (Input.hlsl MAX_VISIBLE_LIGHTS = the mobile count there; maxVisibleReflectionProbes = min(it, 64)).
			string config=File.ReadAllText("Packages/com.unity.render-pipelines.universal-config/Runtime/ShaderConfig.cs.hlsl");
			int webGpuLights=Define(config,"MAX_VISIBLE_LIGHT_COUNT_MOBILE");
			Assert.That(AdditionalLightTable.Lights,Is.EqualTo(webGpuLights),"URP's WebGPU light cap changed: resize the table");
			Assert.That(AdditionalLightTable.Probes,Is.EqualTo(Math.Min(webGpuLights,64)));
		}

		/// <summary>The URP sources the table's copies come from (FishLightTableLighting.hlsl, FishLightTable.hlsl, the gather's declarations).</summary>
		static readonly (string file,string sha)[] UrpSources =
		{
			("Input.hlsl","72ce05de8f49228c"),("RealtimeLights.hlsl","59d1d75a99532078"),("Shadows.hlsl","caa3e10c26476cd4"),
			("GlobalIllumination.hlsl","1f920be6e525dbb4"),("LightCookie/LightCookie.hlsl","2660a79e9653764b"),
			("LightCookie/LightCookieInput.hlsl","e548590c9f8bc05d"),("Lighting.hlsl","1132ec024298a9e3"),
		};

		// A URP upgrade that touches any of them must re-copy the table's versions (and check no new reader of an array appeared).
		[Test]
		public void CopiedUrpLightingSourcesAreUnchanged()
		{
			string root=FileUtil.GetPhysicalPath("Packages/com.unity.render-pipelines.universal/ShaderLibrary");
			using var sha=System.Security.Cryptography.SHA256.Create();
			foreach(var (file,expected) in UrpSources)
			{
				string text=File.ReadAllText(Path.Combine(root,file)).Replace("\r\n","\n");
				string actual=string.Concat(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text)).Take(8).Select(b=>b.ToString("x2")));
				Assert.That(actual,Is.EqualTo(expected),file+" changed since FishLightTableLighting.hlsl copied it: re-copy the functions that read per-light arrays");
			}
		}

		// The kernel copies URP's globals bit for bit, light-layer masks that are not valid floats included.
		[Test]
		public void GatherCopiesUrpGlobalsBitForBit()
		{
			if(!SystemInfo.supportsComputeShaders)Assert.Ignore("GPU required");
			Assert.That(AdditionalLightTable.EnsureCompute(),Is.True,"no gather kernel");
			var random=new System.Random(4101);
			Vector4 V()=>new Vector4((float)random.NextDouble()*200-100,(float)random.NextDouble()*200-100,(float)random.NextDouble()*200-100,(float)random.NextDouble()*2-1);
			Matrix4x4 M(){var m=new Matrix4x4();for(int i=0;i<16;i++)m[i]=(float)random.NextDouble()*4-2;return m;}
			int L=AdditionalLightTable.Lights,P=AdditionalLightTable.Probes;
			var vectors=new Dictionary<string,Vector4[]>();
			foreach(string name in new[]{"_AdditionalLightsPosition","_AdditionalLightsColor","_AdditionalLightsAttenuation","_AdditionalLightsSpotDir","_AdditionalLightsOcclusionProbes","_AdditionalShadowParams","_AdditionalLightsCookieAtlasUVRects"})
				vectors[name]=Enumerable.Range(0,L).Select(_=>V()).ToArray();
			foreach(string name in new[]{"urp_ReflProbes_BoxMax","urp_ReflProbes_BoxMin","urp_ReflProbes_ProbePosition","urp_ReflProbes_Rotation"})
				vectors[name]=Enumerable.Range(0,P).Select(_=>V()).ToArray();
			vectors["urp_ReflProbes_MipScaleOffset"]=Enumerable.Range(0,P*7).Select(_=>V()).ToArray();
			// Layer masks and the cookie word are uint bit patterns in floats: denormals (1, 0x7f) and an all-layers word.
			float[] masks=Enumerable.Range(0,L).Select(i=>BitConverter.Int32BitsToSingle(i==0?1:i==1?0x7f:i==2?unchecked((int)0xffffffffu)>>1:1<<(i%31))).ToArray();
			float[] types=Enumerable.Range(0,L).Select(i=>(float)(i%3)).ToArray();
			float[] cookieBits={BitConverter.Int32BitsToSingle(0x00a5005a)};
			var slices=Enumerable.Range(0,L).Select(_=>M()).ToArray();
			var worldToLights=Enumerable.Range(0,L).Select(_=>M()).ToArray();
			foreach(var pair in vectors)Shader.SetGlobalVectorArray(pair.Key,pair.Value);
			Shader.SetGlobalFloatArray("_AdditionalLightsLayerMasks",masks);
			Shader.SetGlobalFloatArray("_AdditionalLightsLightTypes",types);
			Shader.SetGlobalFloatArray("_AdditionalLightsCookieEnableBits",cookieBits);
			Shader.SetGlobalMatrixArray("_AdditionalLightsWorldToShadow",slices);
			Shader.SetGlobalMatrixArray("_AdditionalLightsWorldToLights",worldToLights);
			using var table=new GraphicsBuffer(GraphicsBuffer.Target.Structured,AdditionalLightTable.Rows,AdditionalLightTable.RowStride);
			using(var cmd=new CommandBuffer())
			{
				AdditionalLightTable.Gather(cmd,table);
				Graphics.ExecuteCommandBuffer(cmd);
			}
			var rows=new uint[AdditionalLightTable.Rows*4];
			table.GetData(rows);
			uint Bits(float f)=>unchecked((uint)BitConverter.SingleToInt32Bits(f));
			void Row(int row,Vector4 v,string what){for(int c=0;c<4;c++)Assert.That(rows[row*4+c],Is.EqualTo(Bits(v[c])),what+" component "+c);}
			string[] lightRows={"_AdditionalLightsPosition","_AdditionalLightsColor","_AdditionalLightsAttenuation","_AdditionalLightsSpotDir","_AdditionalLightsOcclusionProbes","_AdditionalShadowParams","_AdditionalLightsCookieAtlasUVRects"};
			for(int i=0;i<L;i++)
			{
				int at=i*AdditionalLightTable.LightRows;
				for(int f=0;f<lightRows.Length;f++)Row(at+f,vectors[lightRows[f]][i],lightRows[f]+"["+i+"]");
				Assert.That(rows[(at+7)*4],Is.EqualTo(Bits(masks[i])),"layer mask "+i);
				Assert.That(rows[(at+7)*4+1],Is.EqualTo(Bits(types[i])),"light type "+i);
				for(int r=0;r<4;r++)Row(at+8+r,worldToLights[i].GetRow(r),"world to light "+i+" row "+r);
				int slice=L*AdditionalLightTable.LightRows+i*AdditionalLightTable.SliceRows;
				for(int r=0;r<4;r++)Row(slice+r,slices[i].GetRow(r),"world to shadow "+i+" row "+r);
			}
			int probes=L*(AdditionalLightTable.LightRows+AdditionalLightTable.SliceRows);
			for(int p=0;p<P;p++)
			{
				int at=probes+p*AdditionalLightTable.ProbeRows;
				Row(at,vectors["urp_ReflProbes_BoxMax"][p],"box max "+p);
				Row(at+1,vectors["urp_ReflProbes_BoxMin"][p],"box min "+p);
				Row(at+2,vectors["urp_ReflProbes_ProbePosition"][p],"position "+p);
				Row(at+3,vectors["urp_ReflProbes_Rotation"][p],"rotation "+p);
				for(int m=0;m<7;m++)Row(at+4+m,vectors["urp_ReflProbes_MipScaleOffset"][p*7+m],"mip "+p+"/"+m);
			}
			Assert.That(rows[(AdditionalLightTable.Rows-1)*4],Is.EqualTo(Bits(cookieBits[0])),"cookie enable word");
		}

		// Native is untouched: the table exists only under SHADER_API_WEBGPU, and native programs keep URP's own arrays.
		[Test]
		public void NativeProgramsKeepUrpArraysAndNeverReadTheTable()
		{
			string cookies=File.ReadAllText("Assets/Prefabs/Client/Weather/Shaders/"+CookieHeader);
			foreach(Match m in Regex.Matches(cookies,"#include \"(FishLightTable[A-Za-z]*\\.hlsl)\""))
			{
				string before=cookies.Substring(0,m.Index);
				Assert.That(before.LastIndexOf("#if defined(SHADER_API_WEBGPU)"),Is.GreaterThan(before.LastIndexOf("#endif")),m.Groups[1].Value+" is included outside the WebGPU guard");
			}
			var keywords=HeavyKeywords.Concat(new[]{"PROCEDURAL_INSTANCING_ON"}).ToArray();
			foreach(var (_,pass,name,_) in TablePasses().Where(t=>t.name=="FishWeatherLitIndirect ForwardLit"||t.name=="FishWeatherTerrainArrayGpu ForwardLit"||t.name=="FishVegetationIndirect ForwardLit"))
				foreach(var (api,target) in new[]{(ShaderCompilerPlatform.Vulkan,BuildTarget.StandaloneLinux64),(ShaderCompilerPlatform.D3D,BuildTarget.StandaloneWindows64)})
				{
					string native=pass.PreprocessVariant(ShaderType.Vertex,keywords,api,target,false).PreprocessedCode;
					Assert.That(native.Length,Is.GreaterThan(1000),name+" "+api+": no preprocessed source");
					Assert.That(native,Does.Not.Contain("_FishLightTable"),name+" "+api+" reads the table");
					Assert.That(native,Does.Not.Contain("FishTableGetAdditionalLight"),name+" "+api+" takes the table's light functions");
					Assert.That(native,Does.Match(@"cbuffer\s+AdditionalLights\b"),name+" "+api+" lost URP's AdditionalLights");
					Assert.That(native,Does.Contain("_AdditionalLightsPosition["),name+" "+api+" no longer reads URP's light array");
				}
			// And the C# gather never runs there.
			Assert.That(AdditionalLightTable.Needed,Is.EqualTo(SystemInfo.graphicsDeviceType==GraphicsDeviceType.WebGPU));
		}
	}
}
