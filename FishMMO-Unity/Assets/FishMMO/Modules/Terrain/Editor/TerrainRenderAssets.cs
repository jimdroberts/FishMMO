using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.Client
{
	/// <summary>
	/// Fills the Terrain module's render profile whenever the weather's is (<see cref="WeatherRenderAssets.Ensure"/>): every
	/// shader and compute it draws with is referenced from the profile, so a client build includes them, and nothing else does
	/// (no Resources folder, no Always Included Shaders). Existing values are kept.
	/// </summary>
	public static class TerrainRenderAssets
	{
		public const string ModuleFolder = "Assets/FishMMO/Modules/Terrain";
		public const string ShaderFolder = ModuleFolder + "/Shaders";
		public const string ProfilePath = TerrainRenderProfile.EditorProfilePath;
		public const string TerrainInstancingComputePath = ShaderFolder + "/FishTerrainInstancing.compute";
		public const string VegetationIndirectShaderPath = "Assets/FishMMO/Shaders/Surface/FishVegetationIndirect.shader";
		public const string WeatherLitIndirectShaderPath = "Assets/FishMMO/Shaders/Surface/FishWeatherLitIndirect.shader";
		public const string GrassBladesComputePath = ShaderFolder + "/FishGrassBlades.compute";
		public const string GrassBladesShaderPath = ShaderFolder + "/FishGrassBlades.shader";
		public const string IndirectShaderVariantsPath = "Assets/Prefabs/Shared/Biomes/Generated/Variants/FishIndirectVariants.shadervariants";

		[InitializeOnLoadMethod]
		private static void Register()
		{
			WeatherRenderAssets.RegisterModuleProfile(TerrainModule.ModuleId, Fill);
		}

		/// <summary>Makes or loads the profile, assigns what is missing, registers it as a client addressable; the problems found.</summary>
		public static int Fill()
		{
			TerrainRenderProfile terrain = WeatherRenderAssets.EnsureProfile<TerrainRenderProfile>(ProfilePath, "Terrain Render Profile");
			// The GPU-driven terrain trees and details: referenced here so a client build includes them,
			// and nothing else does (no Resources folder, no Always Included Shaders).
			if (terrain.TerrainInstancingCompute == null) terrain.TerrainInstancingCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(TerrainInstancingComputePath);
			if (terrain.VegetationIndirectShader == null) terrain.VegetationIndirectShader = AssetDatabase.LoadAssetAtPath<Shader>(VegetationIndirectShaderPath);
			if (terrain.WeatherLitIndirectShader == null) terrain.WeatherLitIndirectShader = AssetDatabase.LoadAssetAtPath<Shader>(WeatherLitIndirectShaderPath);
			// The procedural blade grass: same rule, client only.
			if (terrain.GrassBladesCompute == null) terrain.GrassBladesCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(GrassBladesComputePath);
			if (terrain.GrassBladesShader == null) terrain.GrassBladesShader = AssetDatabase.LoadAssetAtPath<Shader>(GrassBladesShaderPath);
			// Generated with the biome art (gitignored, path-derived GUID), so it may not exist yet; assigned once it does.
			if (terrain.IndirectShaderVariants == null) terrain.IndirectShaderVariants = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(IndirectShaderVariantsPath);
			// The moss layer's surface (FishMoss.hlsl): generated with the biome art too, same rule. Referenced here so a
			// client build ships it; until then the editor loads it by path and a build draws a plain green.
			if (terrain.MossAlbedo == null) terrain.MossAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(MossShaderGlobals.AlbedoPath);
			if (terrain.MossNormal == null) terrain.MossNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(MossShaderGlobals.NormalPath);
			// The cave cover (2026-10-20): its compute and shader, and its art, generated with the structures (same rule as the moss).
			if (terrain.CaveCoverCompute == null) terrain.CaveCoverCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(CaveCoverRenderer.ComputeAssetPath);
			if (terrain.CaveCoverShader == null) terrain.CaveCoverShader = AssetDatabase.LoadAssetAtPath<Shader>(CaveCoverRenderer.ShaderAssetPath);
			if (terrain.CaveCoverMesh == null) terrain.CaveCoverMesh = AssetDatabase.LoadAssetAtPath<Mesh>(CaveCoverRenderer.MeshAssetPath);
			// The rock seam fillets (2026-10-21): their compute and shader, same rule.
			if (terrain.RockFilletsCompute == null) terrain.RockFilletsCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(RockFillets.ComputeAssetPath);
			if (terrain.RockFilletShader == null) terrain.RockFilletShader = AssetDatabase.LoadAssetAtPath<Shader>(RockFillets.ShaderAssetPath);
			// The client's own terrain heightmap draw (2026-10-28): its compute and shader, same rule.
			if (terrain.TerrainHeightmapCompute == null) terrain.TerrainHeightmapCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(TerrainHeightmapSystem.ComputeAssetPath);
			if (terrain.TerrainHeightmapShader == null) terrain.TerrainHeightmapShader = AssetDatabase.LoadAssetAtPath<Shader>(TerrainHeightmapSystem.ShaderAssetPath);
			EditorUtility.SetDirty(terrain);
			WorldEditorAssets.RegisterAddressable(terrain, WorldEditorAssets.ClientStaticGroup);
			return terrain.TerrainInstancingCompute == null ? 1 : 0;
		}
	}
}
