using System;
using System.Collections;
using System.IO;
using System.Linq;
using FishMMO.Client;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace FishMMO.UnitTests
{
	public class ShaderPrewarmTests
	{
		[UnityTest]
		public IEnumerator AsyncCompileUsesRecordedKeywordsEvenAfterSceneGlobalsAreRestored()
		{
			if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
				Assert.Ignore("This test requires a real graphics device for ShaderUtil.IsPassCompiled.");
			string id = Guid.NewGuid().ToString("N");
			string folder = "Assets/PrewarmAsyncFixture_" + id;
			string keyword = "FISHMMO_PREWARM_" + id.ToUpperInvariant();
			Directory.CreateDirectory(folder);
			bool asyncBefore = ShaderUtil.allowAsyncCompilation;
			Material material = null;
			try
			{
				string path = folder + "/Fixture.shader";
				File.WriteAllText(path, "Shader \"Hidden/FishMMO/PrewarmAsync/" + id + "\" { SubShader { Pass { CGPROGRAM\n" +
					"#pragma vertex vert\n#pragma fragment frag\n#pragma multi_compile _ " + keyword + "\n" +
					"float4 vert(float4 p:POSITION):SV_POSITION{return p;}\nfloat4 frag():SV_Target{\n#ifdef " + keyword +
					"\nreturn float4(1,0,0,1);\n#else\nreturn float4(0,0,1,1);\n#endif\n}\nENDCG\n} } }");
				AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
				var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
				material = new Material(shader);
				Shader.EnableKeyword(GlobalKeyword.Create(keyword));
				ShaderUtil.allowAsyncCompilation = true;
				using (new ShaderPrewarm.KeywordScope(shader)) ShaderUtil.CompilePass(material, 0, false);
				Assert.That(Shader.IsKeywordEnabled(keyword), Is.True, "the open scene is restored before compilation completes");
				float deadline = Time.realtimeSinceStartup + 30;
				while (ShaderUtil.anythingCompiling)
				{
					Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "fixture compilation timed out");
					yield return null;
				}
				using (new ShaderPrewarm.KeywordScope(shader))
					Assert.That(ShaderUtil.IsPassCompiled(material, 0), Is.True, "the request must have captured the recorded OFF state, despite the scene's ON state");
			}
			finally
			{
				ShaderUtil.allowAsyncCompilation = asyncBefore;
				Shader.DisableKeyword(keyword);
				if (material != null) UnityEngine.Object.DestroyImmediate(material);
				AssetDatabase.DeleteAsset(folder);
			}
		}

		[TestCase("FishWeatherLit.shader")]
		[TestCase("FishWeatherLitIndirect.shader")]
		public void WeatherShadowExpansionUsesUniformSoftQualityWithoutDuplicatingPrograms(string filename)
		{
			var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Prefabs/Client/Weather/Shaders/" + filename);
			Assert.That(shader, Is.Not.Null);
			var variants = ShaderPrewarm.WithEveryShadowState(shader, new[] { "_NORMALMAP", "_SHADOWS_SOFT_HIGH" }).ToArray();
			Assert.That(variants.Length, Is.EqualTo(4));
			Assert.That(variants.Select(v => string.Join(" ", v.OrderBy(k => k))).Distinct().Count(), Is.EqualTo(4));
			foreach (string[] keywords in variants)
			{
				Assert.That(keywords, Does.Contain("_NORMALMAP"));
				Assert.That(keywords.Count(k => k.StartsWith("_SHADOWS_SOFT", StringComparison.Ordinal)), Is.Zero);
				Assert.That(keywords.Count(k => k.StartsWith("_MAIN_LIGHT_SHADOWS", StringComparison.Ordinal)), Is.LessThanOrEqualTo(1));
			}
			foreach (string keyword in new[] { "_SHADOWS_SOFT", "_SHADOWS_SOFT_LOW", "_SHADOWS_SOFT_MEDIUM", "_SHADOWS_SOFT_HIGH" })
				Assert.That(variants.Any(v => v.Contains(keyword)), Is.False, keyword);
			Assert.That(variants.Any(v => v.Contains("_MAIN_LIGHT_SHADOWS_SCREEN")), Is.True);
		}

		[Test]
		public void ExpansionDoesNotInventKeywordsAbsentFromTheShader_AndGlobalOverridesRestoreAfterFailure()
		{
			string folder = "Assets/PrewarmFixture_" + Guid.NewGuid().ToString("N");
			Directory.CreateDirectory(folder);
			string path = folder + "/Fixture.shader";
			string[] globals = { "_MAIN_LIGHT_SHADOWS_CASCADE", "FISHMMO_PREWARM_LOCAL", "FISHMMO_PREWARM_UNRELATED" };
			bool[] before = globals.Select(Shader.IsKeywordEnabled).ToArray();
			try
			{
				File.WriteAllText(path, "Shader \"Hidden/FishMMO/PrewarmFixture\" { SubShader { Pass { CGPROGRAM\n" +
					"#pragma vertex vert\n#pragma fragment frag\n#pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE\n" +
					"#pragma multi_compile_fragment _ _SHADOWS_SOFT_HIGH\n#pragma shader_feature_local FISHMMO_PREWARM_LOCAL\n" +
					"float4 vert(float4 p:POSITION):SV_POSITION{return p;}\nfloat4 frag():SV_Target{return 1;}\nENDCG\n} } }");
				AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
				var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
				var variants = ShaderPrewarm.WithEveryShadowState(shader, new[] { "_SHADOWS_SOFT_LOW", "FISHMMO_PREWARM_LOCAL" }).ToArray();
				Assert.That(variants.Length, Is.EqualTo(4));
				Assert.That(variants.SelectMany(v => v), Does.Not.Contain("_SHADOWS_SOFT_LOW"));
				Assert.That(variants.All(v => v.All(k => shader.keywordSpace.FindKeyword(k).isValid)), Is.True);

				foreach (string name in globals) Shader.EnableKeyword(GlobalKeyword.Create(name));
				Assert.Throws<InvalidOperationException>(() =>
				{
					using (new ShaderPrewarm.KeywordScope(shader))
					{
						Assert.That(Shader.IsKeywordEnabled(globals[0]), Is.False, "a scene's cascade setting must not override another recorded variant");
						Assert.That(Shader.IsKeywordEnabled(globals[1]), Is.True, "globally enabled names cannot override shader_feature_local");
						Assert.That(Shader.IsKeywordEnabled(globals[2]), Is.True, "unrelated globals must stay untouched");
						throw new InvalidOperationException("compile request failed");
					}
				});
				Assert.That(Shader.IsKeywordEnabled(globals[0]), Is.True);
			}
			finally
			{
				for (int i = 0; i < globals.Length; i++)
				{
					if (before[i]) Shader.EnableKeyword(globals[i]); else Shader.DisableKeyword(globals[i]);
				}
				AssetDatabase.DeleteAsset(folder);
			}
		}
	}
}
