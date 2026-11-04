using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.Build;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>The offline WebGPU harness: its build-scoped engine code options and its runtime A/B switches (by reflection:
	/// the harness assemblies are not compile references of the test assembly).</summary>
	public class WebGpuHarnessTests
	{
		private static Type BuildType => Type.GetType("FishMMO.TestHarness.World.Editor.WebGpuSmokeBuild, FishMMO.TestHarness.World.Editor", true);
		private static Type ProbeType => Type.GetType("FishMMO.TestHarness.World.WebGpuSmokeProbe, FishMMO.TestHarness.World", true);

		private static object Invoke(Type type, string method, object target, params object[] args)
		{
			try { return type.GetMethod(method, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance).Invoke(target, args); }
			catch (TargetInvocationException e) { throw e.InnerException; }
		}

		[TestCase(null, true)]
		[TestCase("1", true)]
		[TestCase("0", false)]
		public void HarnessBuildsUseWebAssembly2023UnlessOptedOut(string value, bool expected)
			=> Assert.That(Invoke(BuildType, "UseWasm2023", null, value), Is.EqualTo(expected));

		[TestCase(null, Il2CppCodeGeneration.OptimizeSpeed)]
		[TestCase("speed", Il2CppCodeGeneration.OptimizeSpeed)]
		[TestCase("size", Il2CppCodeGeneration.OptimizeSize)]
		public void HarnessBuildsGenerateSpecializedGenericsUnlessOptedOut(string value, Il2CppCodeGeneration expected)
			=> Assert.That(Invoke(BuildType, "CodeGeneration", null, value), Is.EqualTo(expected));

		// Both options are the harness's only: restored after the build (and after an interrupted one).
		[Test]
		public void HarnessBuildRestoresTheEngineCodeOptions()
		{
			string code = SourceScanPins.ReadCode("Assets/TestHarness/World/Editor/WebGpuSmokeBuild.cs");
			SourceScanPins.HoldsAndFires("restore", code,
				s => SourceScanPins.InOrder(s, "bool wasm2023 = PlayerSettings.WebGL.wasm2023;", "var codeGeneration = PlayerSettings.GetIl2CppCodeGeneration(NamedBuildTarget.WebGL);",
					"PlayerSettings.WebGL.wasm2023 = UseWasm2023(", "finally", "PlayerSettings.WebGL.wasm2023 = wasm2023; PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL,codeGeneration);"),
				SourceScanPins.Replace("PlayerSettings.WebGL.wasm2023 = wasm2023; ", string.Empty),
				"WebAssembly 2023 left on in the project after a harness build");
			StringAssert.Contains("PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL,(Il2CppCodeGeneration)recovery.codeGeneration);", code);
		}

		[Test]
		public void HarnessSettingsRejectUnknownAndMalformedNames()
		{
			var host = new GameObject("WebGPU harness test");
			try
			{
				var probe = host.AddComponent(ProbeType);
				Assert.Throws<ArgumentException>(() => Invoke(ProbeType, "SetSetting", probe, "noSuchSetting=1"));
				Assert.Throws<ArgumentException>(() => Invoke(ProbeType, "SetSetting", probe, "noEquals"));
				Assert.Throws<ArgumentException>(() => Invoke(ProbeType, "SetSetting", probe, "=1"));
				// The clock hold needs no world in the scene (nothing to hold): accepted, no error.
				Assert.DoesNotThrow(() => Invoke(ProbeType, "SetSetting", probe, "paused=0"));
			}
			finally { Object.DestroyImmediate(host); }
		}
	}
}
