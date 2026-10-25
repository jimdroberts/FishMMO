using FishMMO.Client;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	public class ShaderProbeDiagnosticsTests
	{
		[Test]
		public void PrewarmSkipsProbeProgramsWithoutDroppingNormalLightingStates()
		{
			var shader = Shader.Find("FishMMO/Vegetation");
			Assert.That(ShaderPrewarm.WithEveryShadowState(shader, new[] { ShaderProbeDiagnostics.Keyword }).Any(), Is.False);
			Assert.That(ShaderPrewarm.WithEveryShadowState(shader, new string[0]).Any(), Is.True);
		}

		[TestCase("_FishCloudFieldSkip")]
		[TestCase("_FishVegetationDiag")]
		[TestCase("_FishVegContactAlways")]
		[TestCase("_FishVegPlantConst")]
		public void EditorProbesEnableTheirVariantAndRestoringGlobalsDisablesIt(string probe)
		{
			string[] names = { "_FishCloudFieldSkip", "_FishVegetationDiag", "_FishVegContactAlways", "_FishVegPlantConst" };
			var before = new float[names.Length];
			bool keywordBefore = Shader.IsKeywordEnabled(ShaderProbeDiagnostics.Keyword);
			for (int i = 0; i < names.Length; i++) before[i] = Shader.GetGlobalFloat(names[i]);
			try
			{
				foreach (string name in names) Shader.SetGlobalFloat(name, 0f);
				ShaderProbeDiagnostics.Sync();
				Assert.That(Shader.IsKeywordEnabled(ShaderProbeDiagnostics.Keyword), Is.False);
				Shader.SetGlobalFloat(probe, 1f);
				ShaderProbeDiagnostics.Sync();
				Assert.That(Shader.IsKeywordEnabled(ShaderProbeDiagnostics.Keyword), Is.True);
				Assert.That(Shader.GetGlobalFloat(probe), Is.EqualTo(1f), "synchronization must not change probe values");
				// A second active probe keeps the variant alive when the first one is restored.
				string other = probe == names[0] ? names[1] : names[0];
				Shader.SetGlobalFloat(other, 2f);
				Shader.SetGlobalFloat(probe, 0f);
				ShaderProbeDiagnostics.Sync();
				Assert.That(Shader.IsKeywordEnabled(ShaderProbeDiagnostics.Keyword), Is.True);
				Shader.SetGlobalFloat(other, 0f);
				ShaderProbeDiagnostics.Sync();
				Assert.That(Shader.IsKeywordEnabled(ShaderProbeDiagnostics.Keyword), Is.False);
			}
			finally
			{
				for (int i = 0; i < names.Length; i++) Shader.SetGlobalFloat(names[i], before[i]);
				if (keywordBefore) Shader.EnableKeyword(ShaderProbeDiagnostics.Keyword);
				else Shader.DisableKeyword(ShaderProbeDiagnostics.Keyword);
			}
		}

		[TestCase("FishMMO/Vegetation")]
		[TestCase("FishMMO/Vegetation Indirect")]
		[TestCase("FishMMO/Vegetation Prime")]
		[TestCase("Hidden/FishMMO/Weather/Clouds")]
		public void ProfilingShadersDeclareTheDiagnosticVariant(string name)
		{
			var shader = Shader.Find(name);
			Assert.That(shader, Is.Not.Null);
			var keyword = shader.keywordSpace.FindKeyword(ShaderProbeDiagnostics.Keyword);
			Assert.That(keyword.isValid, Is.True);
			Assert.That(keyword.isDynamic, Is.False, "the build must be able to remove the entire diagnostic program");
		}
	}
}
