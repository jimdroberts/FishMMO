using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.UnitTests
{
	public class LightCookieVariantTests
	{
		private const string Header = "Assets/Prefabs/Client/Weather/Shaders/FishLightCookieVariants.hlsl";

		[Test]
		public void CookieProgramsCompileForWebGpuWithoutStaticCookieVariants()
		{
			if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
				Assert.Ignore("Requires WebGL build support.");
			foreach (string name in new[] { "FishWeatherLit", "FishWeatherLitIndirect", "FishVegetation", "FishVegetationIndirect" })
			{
				var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Prefabs/Client/Weather/Shaders/" + name + ".shader");
				Assert.That(shader.keywordSpace.FindKeyword("_LIGHT_COOKIES").isDynamic, Is.True, name);
				string[] keys = name.EndsWith("Indirect", StringComparison.Ordinal)
					? new[] { "_CLUSTER_LIGHT_LOOP", "PROCEDURAL_INSTANCING_ON" } : new[] { "_CLUSTER_LIGHT_LOOP" };
				var result = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0)
					.CompileVariant(ShaderType.Vertex, keys, ShaderCompilerPlatform.WebGPU, BuildTarget.WebGL);
				Assert.That(result.Success, Is.True, name);
				Assert.That(result.ShaderData.Length, Is.GreaterThan(0), name);
			}
		}

		[Test]
		public void DynamicCookiesMatchStaticProgramsAcrossLiveKeywordChanges()
		{
			if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Requires GPU rendering.");
			string folder = "Assets/CookieFixture_" + Guid.NewGuid().ToString("N");
			Directory.CreateDirectory(folder);
			bool prior = Shader.IsKeywordEnabled("_LIGHT_COOKIES"), asyncBefore = ShaderUtil.allowAsyncCompilation;
			string[] textureNames = { "_MainLightCookieTexture", "_AdditionalLightsCookieAtlasTexture" };
			var textures = Array.ConvertAll(textureNames, Shader.GetGlobalTexture);
			string[] floatNames = { "_MainLightCookieTextureFormat", "_AdditionalLightsCookieAtlasTextureFormat", "_CookieTestAdditional", "_FishCloudCookie" };
			var floats = Array.ConvertAll(floatNames, Shader.GetGlobalFloat);
			var matrix = Shader.GetGlobalMatrix("_MainLightWorldToLight");
			var matrices = Shader.GetGlobalMatrixArray("_AdditionalLightsWorldToLights");
			var rects = Shader.GetGlobalVectorArray("_AdditionalLightsCookieAtlasUVRects");
			var types = Shader.GetGlobalFloatArray("_AdditionalLightsLightTypes");
			var bits = Shader.GetGlobalFloatArray("_AdditionalLightsCookieEnableBits");
			Material material = null;
			Texture2D cookie = null;
			RenderTexture target = null;
			try
			{
				string common = @"
#include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
struct V {float4 pos:SV_POSITION;};
V Vert(uint id:SV_VertexID){V v;v.pos=GetFullScreenTriangleVertexPosition(id);return v;}
";
				string sample = @"
#include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/LightCookie/LightCookie.hlsl""
float _CookieTestAdditional;
float4 Frag(V i):SV_Target {
    return float4(_CookieTestAdditional > .5 ? SampleAdditionalLightCookie(0,float3(0,0,0)) : SampleMainLightCookie(float3(0,0,0)),1);
}
";
				string Pass(string code) => "Pass {ZTest Always ZWrite Off Cull Off HLSLPROGRAM\n#pragma use_dxc vulkan\n#pragma target 4.5\n#pragma vertex Vert\n#pragma fragment Frag\n" + code + "\nENDHLSL\n}";
				string source = "Shader \"Hidden/FishMMO/CookieFixture\" {SubShader {" +
					Pass(common + "float4 Frag(V i):SV_Target{return 1;}") +
					Pass("#define _LIGHT_COOKIES 1\n" + common + sample) +
					Pass("#include_with_pragmas \"" + Header + "\"\n" + common + sample) +
					Pass("#include_with_pragmas \"" + Header + "\"\n" + common +
						"#include \"Assets/Plugins/FishMMO Water/Shaders/FishWaterSun.hlsl\"\n" +
						"float4 Frag(V i):SV_Target{return float4(FishWaterCloudLight(float3(0,0,0),.85),1);}") + "}}";
				string path = folder + "/Fixture.shader";
				File.WriteAllText(path, source);
				AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
				var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
				Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
				Assert.That(shader.keywordSpace.FindKeyword("_LIGHT_COOKIES").isDynamic, Is.True);
				ShaderUtil.allowAsyncCompilation = false;
				material = new Material(shader);
				cookie = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
				var color = new Color(.2f, .4f, .7f, 1);
				cookie.SetPixels(new[] { color, color, color, color });
				cookie.Apply();
				foreach (string name in textureNames) Shader.SetGlobalTexture(name, cookie);
				Shader.SetGlobalMatrix("_MainLightWorldToLight", Matrix4x4.identity);
				Shader.SetGlobalFloat("_AdditionalLightsCookieAtlasTextureFormat", 0);
				Shader.SetGlobalFloat("_FishCloudCookie", 1);
				var testMatrices = new Matrix4x4[256];
				var testRects = new Vector4[256];
				var testTypes = new float[256];
				for (int i = 0; i < 256; i++)
				{
					testMatrices[i] = Matrix4x4.identity;
					testRects[i] = new Vector4(1, 1, 0, 0);
					testTypes[i] = 1; // Directional cookie, using URP's non-structured array path.
				}
				Shader.SetGlobalMatrixArray("_AdditionalLightsWorldToLights", testMatrices);
				Shader.SetGlobalVectorArray("_AdditionalLightsCookieAtlasUVRects", testRects);
				Shader.SetGlobalFloatArray("_AdditionalLightsLightTypes", testTypes);
				target = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGBFloat);
				target.Create();
				foreach (bool additional in new[] { false, true })
				foreach (bool enabled in new[] { false, true, false, true })
				foreach (float format in new[] { 0f, -1f })
				{
					Shader.SetGlobalFloat("_CookieTestAdditional", additional ? 1 : 0);
					Shader.SetGlobalFloatArray("_AdditionalLightsCookieEnableBits", new[] { format == 0 ? BitConverter.Int32BitsToSingle(1) : 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f });
					Shader.SetGlobalFloat("_MainLightCookieTextureFormat", format);
					if (enabled) Shader.EnableKeyword("_LIGHT_COOKIES"); else Shader.DisableKeyword("_LIGHT_COOKIES");
					var expected = Capture(material, target, enabled ? 1 : 0);
					var actual = Capture(material, target, 2);
					string context = $"additional={additional}, enabled={enabled}, format={format}";
					for (int c = 0; c < 4; c++) Assert.That(actual[c], Is.EqualTo(expected[c]).Within(.00001f), context);
					Assert.That(actual.r, Is.EqualTo(enabled && format == 0 ? .2f : 1f).Within(.00001f), context);
					var water = Capture(material, target, 3);
					Assert.That(water.r, Is.EqualTo(enabled && format == 0 ? .2f : .85f).Within(.0001f), "water cloud cookie: " + context);
				}
			}
			finally
			{
				ShaderUtil.allowAsyncCompilation = asyncBefore;
				if (prior) Shader.EnableKeyword("_LIGHT_COOKIES"); else Shader.DisableKeyword("_LIGHT_COOKIES");
				for (int i = 0; i < textureNames.Length; i++) Shader.SetGlobalTexture(textureNames[i], textures[i]);
				for (int i = 0; i < floatNames.Length; i++) Shader.SetGlobalFloat(floatNames[i], floats[i]);
				Shader.SetGlobalMatrix("_MainLightWorldToLight", matrix);
				Shader.SetGlobalMatrixArray("_AdditionalLightsWorldToLights", matrices != null && matrices.Length > 0 ? matrices : new Matrix4x4[256]);
				Shader.SetGlobalVectorArray("_AdditionalLightsCookieAtlasUVRects", rects != null && rects.Length > 0 ? rects : new Vector4[256]);
				Shader.SetGlobalFloatArray("_AdditionalLightsLightTypes", types != null && types.Length > 0 ? types : new float[256]);
				Shader.SetGlobalFloatArray("_AdditionalLightsCookieEnableBits", bits != null && bits.Length > 0 ? bits : new float[8]);
				if (material != null) UnityEngine.Object.DestroyImmediate(material);
				if (cookie != null) UnityEngine.Object.DestroyImmediate(cookie);
				if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
				AssetDatabase.DeleteAsset(folder);
			}
		}

		private static Color Capture(Material material, RenderTexture target, int pass)
		{
			using (var cmd = new CommandBuffer())
			{
				cmd.SetRenderTarget(target);
				cmd.ClearRenderTarget(false, true, Color.magenta);
				cmd.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 3);
				Graphics.ExecuteCommandBuffer(cmd);
			}
			var previous = RenderTexture.active;
			var read = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true);
			try
			{
				RenderTexture.active = target;
				read.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
				read.Apply();
				return read.GetPixel(4, 4);
			}
			finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(read); }
		}
	}
}
