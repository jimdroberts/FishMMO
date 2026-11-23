using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.Client
{
	/// <summary>
	/// Fills the Ambient Life module's render profile whenever the weather's is (<see cref="WeatherRenderAssets.Ensure"/>):
	/// every shader and compute it draws with is referenced from the profile, so a client build includes them, and nothing
	/// else does (no Resources folder, no Always Included Shaders). Existing values are kept.
	/// </summary>
	public static class AmbientLifeRenderAssets
	{
		public const string ModuleFolder = "Assets/FishMMO/Modules/AmbientLife";
		public const string ShaderFolder = ModuleFolder + "/Shaders";
		public const string ProfilePath = AmbientLifeRenderProfile.EditorProfilePath;
		public const string SeaLifeShaderPath = ShaderFolder + "/FishSeaLife.shader";
		public const string AmbientLifeShaderPath = ShaderFolder + "/FishAmbientLife.shader";
		public const string AirMotesShaderPath = ShaderFolder + "/FishAirMotes.shader";
		public const string FallingLeavesShaderPath = ShaderFolder + "/FishFallingLeaves.shader";
		public const string SiteEmbersShaderPath = ShaderFolder + "/FishSiteEmbers.shader";
		public const string SiteFlamesShaderPath = ShaderFolder + "/FishSiteFlames.shader";
		public const string LighthouseBeamsShaderPath = ShaderFolder + "/FishLighthouseBeams.shader";
		public const string CaveDripsShaderPath = ShaderFolder + "/FishCaveDrips.shader";
		public const string CaveWaterLifeShaderPath = ShaderFolder + "/FishCaveWaterLife.shader";
		public const string SeaHallVentsShaderPath = ShaderFolder + "/FishSeaHallVents.shader";
		public const string RockfallStonesShaderPath = ShaderFolder + "/FishRockfallStones.shader";
		public const string RockfallMarksShaderPath = ShaderFolder + "/FishRockfallMarks.shader";

		[InitializeOnLoadMethod]
		private static void Register()
		{
			WeatherRenderAssets.RegisterModuleProfile(AmbientLifeModule.ModuleId, Fill);
		}

		/// <summary>Makes or loads the profile, assigns what is missing, registers it as a client addressable; the problems found.</summary>
		public static int Fill()
		{
			AmbientLifeRenderProfile ambient = WeatherRenderAssets.EnsureProfile<AmbientLifeRenderProfile>(ProfilePath, "Ambient Life Render Profile");
			// The sea's background creatures: same rule as the weather's.
			if (ambient.SeaLifeShader == null) ambient.SeaLifeShader = AssetDatabase.LoadAssetAtPath<Shader>(SeaLifeShaderPath);
			// What drifts on the open sea: its compute and shader, same rule as the weather's.
			if (ambient.SeaFloatsCompute == null) ambient.SeaFloatsCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(SeaFloatSystem.ComputeAssetPath);
			if (ambient.SeaFloatsShader == null) ambient.SeaFloatsShader = AssetDatabase.LoadAssetAtPath<Shader>(SeaFloatSystem.ShaderAssetPath);
			// The land's birds, bats and small animals: same rule as the weather's.
			if (ambient.AmbientLifeShader == null) ambient.AmbientLifeShader = AssetDatabase.LoadAssetAtPath<Shader>(AmbientLifeShaderPath);
			// What floats in the air round the camera: same rule as the weather's.
			if (ambient.AirMotesShader == null) ambient.AirMotesShader = AssetDatabase.LoadAssetAtPath<Shader>(AirMotesShaderPath);
			// The leaves coming down from the trees: same rule as the weather's.
			if (ambient.FallingLeavesShader == null) ambient.FallingLeavesShader = AssetDatabase.LoadAssetAtPath<Shader>(FallingLeavesShaderPath);
			// The sparks off the sites' fires and forges: same rule as the weather's.
			if (ambient.SiteEmbersShader == null) ambient.SiteEmbersShader = AssetDatabase.LoadAssetAtPath<Shader>(SiteEmbersShaderPath);
			if (ambient.SiteFlamesShader == null) ambient.SiteFlamesShader = AssetDatabase.LoadAssetAtPath<Shader>(SiteFlamesShaderPath);
			// The lighthouses' turning beams: same rule as the weather's.
			if (ambient.LighthouseBeamsShader == null) ambient.LighthouseBeamsShader = AssetDatabase.LoadAssetAtPath<Shader>(LighthouseBeamsShaderPath);
			// The caves' drips: same rule as the weather's.
			if (ambient.CaveDripsShader == null) ambient.CaveDripsShader = AssetDatabase.LoadAssetAtPath<Shader>(CaveDripsShaderPath);
			// The caves' still water's beads and leeches: same rule as the weather's.
			if (ambient.CaveWaterLifeShader == null) ambient.CaveWaterLifeShader = AssetDatabase.LoadAssetAtPath<Shader>(CaveWaterLifeShaderPath);
			if (ambient.SeaHallVentsShader == null) ambient.SeaHallVentsShader = AssetDatabase.LoadAssetAtPath<Shader>(SeaHallVentsShaderPath);
			if (ambient.RockfallStonesShader == null) ambient.RockfallStonesShader = AssetDatabase.LoadAssetAtPath<Shader>(RockfallStonesShaderPath);
			if (ambient.RockfallMarksShader == null) ambient.RockfallMarksShader = AssetDatabase.LoadAssetAtPath<Shader>(RockfallMarksShaderPath);
			EditorUtility.SetDirty(ambient);
			WorldEditorAssets.RegisterAddressable(ambient, WorldEditorAssets.ClientStaticGroup);
			if (ambient.SiteEmbersShader == null)
			{
				Debug.LogWarning("[Ambient life render] the site embers shader was not found at " + SiteEmbersShaderPath);
				return 1;
			}
			return 0;
		}
	}
}
