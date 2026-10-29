using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.Client
{
	/// <summary>
	/// Creates the client's weather look: the precipitation material, the render profile and the
	/// audio cue table, in the client's static assets. Existing assets are kept.
	/// </summary>
	public static class WeatherRenderAssets
	{
		public const string Folder = "Assets/Prefabs/Client/Weather";
		public const string MaterialPath = Folder + "/Precipitation.mat";
		public const string ProfilePath = Folder + "/Weather Render Profile.asset";
		public const string TerrainInstancingComputePath = "Assets/Prefabs/Client/Weather/Shaders/FishTerrainInstancing.compute";
		public const string VegetationIndirectShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishVegetationIndirect.shader";
		public const string WeatherLitIndirectShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishWeatherLitIndirect.shader";
		public const string GrassBladesComputePath = "Assets/Prefabs/Client/Weather/Shaders/FishGrassBlades.compute";
		public const string GrassBladesShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishGrassBlades.shader";
		public const string SeaLifeShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishSeaLife.shader";
		public const string AmbientLifeShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishAmbientLife.shader";
		public const string AirMotesShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishAirMotes.shader";
		public const string FallingLeavesShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishFallingLeaves.shader";
		public const string SiteEmbersShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishSiteEmbers.shader";
		public const string SiteFlamesShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishSiteFlames.shader";
		public const string LighthouseBeamsShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishLighthouseBeams.shader";
		public const string CaveDripsShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishCaveDrips.shader";
		public const string CaveWaterLifeShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishCaveWaterLife.shader";
		public const string SeaHallVentsShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishSeaHallVents.shader";
		public const string RockfallStonesShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishRockfallStones.shader";
		public const string RockfallMarksShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishRockfallMarks.shader";
		public const string CaveSkylightShaftsShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishSkylightShafts.shader";
		public const string IndirectShaderVariantsPath = "Assets/Prefabs/Shared/Biomes/Generated/Variants/FishIndirectVariants.shadervariants";
		public const string AudioPath = Folder + "/Weather Audio Profile.asset";
		public const string ShaderName = "FishMMO/Weather/Precipitation";
		public const string SplashShaderName = "FishMMO/Weather/Precipitation Splash";
		public const string SkyFolder = "Assets/Templates/World/Sky";
		public const string SkyProfilePath = SkyFolder + "/Temperate Sky.asset";

		[DashboardTool(DashboardToolAttribute.Weather, "Create weather render profile", Section = "Content", Order = 2,
			Tooltip = "Bakes the weather textures if missing, then creates the precipitation material, the render profile and the audio cue table under " + Folder + ".")]
		public static void CreateFromDashboard()
		{
			WeatherRenderProfile profile = Ensure();
			Debug.Log(profile != null ? $"[Weather render] {AssetDatabase.GetAssetPath(profile)} is ready." : "[Weather render] The precipitation shader was not found.");
		}

		[DashboardTool(DashboardToolAttribute.Weather, "Create all weather content", Section = "Content", Order = -1,
			Tooltip = "Runs the content generator, the texture baker and the render profile in one go. Authored content is kept.")]
		public static void CreateEverything()
		{
			WeatherContentGenerator.GenerateFromDashboard();
			CreateFromDashboard();
			EnsureSkyProfile();
		}

		/// <summary>
		/// The default sky, shared so the server can name it in region actions, and given to the
		/// home world if it has none. Worlds without one use the same defaults built in memory.
		/// </summary>
		public static SkyProfile EnsureSkyProfile()
		{
			WorldEditorAssets.EnsureFolder(SkyFolder);
			SkyProfile sky = AssetDatabase.LoadAssetAtPath<SkyProfile>(SkyProfilePath);
			if (sky == null)
			{
				sky = ScriptableObject.CreateInstance<SkyProfile>();
				sky.name = "Temperate Sky";
				AssetDatabase.CreateAsset(sky, SkyProfilePath);
			}
			WorldEditorAssets.RegisterAddressable(sky);
			SolarSystemProfile system = SolarSystemProfile.Resolve();
			if (system != null && system.HomeWorld != null && system.HomeWorld.Sky == null)
			{
				Undo.RecordObject(system.HomeWorld, "Home sky");
				system.HomeWorld.Sky = sky;
				EditorUtility.SetDirty(system.HomeWorld);
			}
			AssetDatabase.SaveAssets();
			return sky;
		}

		/// <summary>A material for a shader under the weather folder, created once and kept client-only.</summary>
		private static Material EnsureMaterial(string name, string shaderName)
		{
			string path = $"{Folder}/{name}.mat";
			Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
			if (material == null)
			{
				Shader shader = Shader.Find(shaderName);
				if (shader == null)
				{
					Debug.LogError($"[Weather render] Shader {shaderName} was not found.");
					return null;
				}
				material = new Material(shader) { name = name };
				AssetDatabase.CreateAsset(material, path);
			}
			WorldEditorAssets.RegisterAddressable(material, WorldEditorAssets.ClientStaticGroup);
			return material;
		}

		public static WeatherRenderProfile Ensure()
		{
			WorldEditorAssets.EnsureFolder(Folder);
			if (AssetDatabase.LoadAssetAtPath<Texture2D>(WeatherTextureBaker.AtlasPath) == null)
			{
				WeatherTextureBaker.Bake(WeatherTextureBaker.Folder, WeatherTextureBaker.DefaultSeed);
			}
			Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(WeatherTextureBaker.AtlasPath);
			Texture2D noise = AssetDatabase.LoadAssetAtPath<Texture2D>(WeatherTextureBaker.NoisePath);

			Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
			if (material == null)
			{
				Shader shader = Shader.Find(ShaderName);
				if (shader == null)
				{
					return null;
				}
				material = new Material(shader) { name = "Precipitation" };
				material.SetTexture("_MainTex", atlas);
				AssetDatabase.CreateAsset(material, MaterialPath);
			}
			WorldEditorAssets.RegisterAddressable(material, WorldEditorAssets.ClientStaticGroup);

			Material sky = EnsureMaterial("Sky", "FishMMO/Sky");
			Material skyBody = EnsureMaterial("Sky Body", "FishMMO/Sky Body");
			Material curtain = EnsureMaterial("Curtain", "FishMMO/Weather/Curtain");
			Material bolt = EnsureMaterial("Lightning Bolt", "FishMMO/Weather/Bolt");
			Material cookie = EnsureMaterial("Cloud Cookie", "Hidden/FishMMO/Weather/CloudCookie");
			Material clouds = EnsureMaterial("Clouds", FishCloudsFeature.ShaderName);
			if (skyBody != null)
			{
				skyBody.enableInstancing = false;
			}

			WeatherAudioProfile audio = AssetDatabase.LoadAssetAtPath<WeatherAudioProfile>(AudioPath);
			if (audio == null)
			{
				audio = ScriptableObject.CreateInstance<WeatherAudioProfile>();
				audio.name = "Weather Audio Profile";
				AssetDatabase.CreateAsset(audio, AudioPath);
			}
			WorldEditorAssets.RegisterAddressable(audio, WorldEditorAssets.ClientStaticGroup);

			WeatherRenderProfile profile = AssetDatabase.LoadAssetAtPath<WeatherRenderProfile>(ProfilePath);
			if (profile == null)
			{
				profile = ScriptableObject.CreateInstance<WeatherRenderProfile>();
				profile.name = "Weather Render Profile";
				AssetDatabase.CreateAsset(profile, ProfilePath);
			}
			Undo.RecordObject(profile, "Weather render profile");
			if (profile.PrecipitationMaterial == null) profile.PrecipitationMaterial = material;
			// Where the rain lands. Its own material, because it is its own shader and its own
			// blend; sharing the precipitation material would mean one atlas row deciding both.
			if (profile.SplashMaterial == null) profile.SplashMaterial = EnsureMaterial("Precipitation Splash", SplashShaderName);
			if (profile.PrecipitationAtlas == null) profile.PrecipitationAtlas = atlas;
			if (profile.Noise == null) profile.Noise = noise;
			if (profile.Audio == null) profile.Audio = audio;
			if (profile.SkyMaterial == null) profile.SkyMaterial = sky;
			if (profile.SkyBodyMaterial == null) profile.SkyBodyMaterial = skyBody;
			if (profile.CurtainMaterial == null) profile.CurtainMaterial = curtain;
			if (profile.BoltMaterial == null) profile.BoltMaterial = bolt;
			// Tornadoes and dust devils: referenced here so a build includes their shaders. Vortex.mat and
			// Vortex Debris.mat ship in the folder, since a player build never runs this and without them
			// draws no tornado; ensured every time, like the materials above, so they are registered as
			// addressables even once the profile already holds them.
			Material vortex = EnsureMaterial("Vortex", VortexPresenter.FunnelShaderName);
			Material vortexDebris = EnsureMaterial("Vortex Debris", VortexPresenter.DebrisShaderName);
			if (profile.VortexMaterial == null) profile.VortexMaterial = vortex;
			if (profile.VortexDebrisMaterial == null) profile.VortexDebrisMaterial = vortexDebris;
			if (profile.CloudCookieMaterial == null) profile.CloudCookieMaterial = cookie;
			if (profile.CloudMaterial == null) profile.CloudMaterial = clouds;
			// The volumes the clouds are carved from, baked once and kept.
			if (profile.CloudShape == null) profile.CloudShape = CloudNoiseBaker.Ensure(CloudNoiseBaker.ShapePath, CloudNoiseBaker.ShapeSize, CloudNoiseBaker.DefaultSeed, true);
			if (profile.CloudDetail == null) profile.CloudDetail = CloudNoiseBaker.Ensure(CloudNoiseBaker.DetailPath, CloudNoiseBaker.DetailSize, CloudNoiseBaker.DefaultSeed + 17, false);
			CloudRendererSetup.EnsureFeature(clouds);
			// The GPU-driven terrain trees and details: referenced here so a client build includes them,
			// and nothing else does (no Resources folder, no Always Included Shaders).
			if (profile.TerrainInstancingCompute == null) profile.TerrainInstancingCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(TerrainInstancingComputePath);
			if (profile.VegetationIndirectShader == null) profile.VegetationIndirectShader = AssetDatabase.LoadAssetAtPath<Shader>(VegetationIndirectShaderPath);
			if (profile.WeatherLitIndirectShader == null) profile.WeatherLitIndirectShader = AssetDatabase.LoadAssetAtPath<Shader>(WeatherLitIndirectShaderPath);
			// The procedural blade grass: same rule, client only.
			if (profile.GrassBladesCompute == null) profile.GrassBladesCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(GrassBladesComputePath);
			if (profile.GrassBladesShader == null) profile.GrassBladesShader = AssetDatabase.LoadAssetAtPath<Shader>(GrassBladesShaderPath);
			// The sea's background creatures: same rule.
			if (profile.SeaLifeShader == null) profile.SeaLifeShader = AssetDatabase.LoadAssetAtPath<Shader>(SeaLifeShaderPath);
			// What drifts on the open sea (2026-10-25): its compute and shader, same rule.
			if (profile.SeaFloatsCompute == null) profile.SeaFloatsCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(SeaFloatSystem.ComputeAssetPath);
			if (profile.SeaFloatsShader == null) profile.SeaFloatsShader = AssetDatabase.LoadAssetAtPath<Shader>(SeaFloatSystem.ShaderAssetPath);
			// The land's birds, bats and small animals: same rule.
			if (profile.AmbientLifeShader == null) profile.AmbientLifeShader = AssetDatabase.LoadAssetAtPath<Shader>(AmbientLifeShaderPath);
			// What floats in the air round the camera: same rule.
			if (profile.AirMotesShader == null) profile.AirMotesShader = AssetDatabase.LoadAssetAtPath<Shader>(AirMotesShaderPath);
			// The leaves coming down from the trees: same rule.
			if (profile.FallingLeavesShader == null) profile.FallingLeavesShader = AssetDatabase.LoadAssetAtPath<Shader>(FallingLeavesShaderPath);
			// The sparks off the sites' fires and forges: same rule.
			if (profile.SiteEmbersShader == null) profile.SiteEmbersShader = AssetDatabase.LoadAssetAtPath<Shader>(SiteEmbersShaderPath);
			if (profile.SiteFlamesShader == null) profile.SiteFlamesShader = AssetDatabase.LoadAssetAtPath<Shader>(SiteFlamesShaderPath);
			// The lighthouses' turning beams: same rule.
			if (profile.LighthouseBeamsShader == null) profile.LighthouseBeamsShader = AssetDatabase.LoadAssetAtPath<Shader>(LighthouseBeamsShaderPath);
			// The caves' drips (2026-10-20): same rule.
			if (profile.CaveDripsShader == null) profile.CaveDripsShader = AssetDatabase.LoadAssetAtPath<Shader>(CaveDripsShaderPath);
			// The caves' still water's beads and leeches (2026-10-26): same rule.
			if (profile.CaveWaterLifeShader == null) profile.CaveWaterLifeShader = AssetDatabase.LoadAssetAtPath<Shader>(CaveWaterLifeShaderPath);
			if (profile.SeaHallVentsShader == null) profile.SeaHallVentsShader = AssetDatabase.LoadAssetAtPath<Shader>(SeaHallVentsShaderPath);
			if (profile.RockfallStonesShader == null) profile.RockfallStonesShader = AssetDatabase.LoadAssetAtPath<Shader>(RockfallStonesShaderPath);
			if (profile.RockfallMarksShader == null) profile.RockfallMarksShader = AssetDatabase.LoadAssetAtPath<Shader>(RockfallMarksShaderPath);
			// The daylight's shafts under the caves' skylights (2026-10-20): same rule.
			if (profile.CaveSkylightShaftsShader == null) profile.CaveSkylightShaftsShader = AssetDatabase.LoadAssetAtPath<Shader>(CaveSkylightShaftsShaderPath);
			// Generated with the biome art (gitignored, path-derived GUID), so it may not exist yet; assigned once it does.
			if (profile.IndirectShaderVariants == null) profile.IndirectShaderVariants = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(IndirectShaderVariantsPath);
			// The moss layer's surface (FishMoss.hlsl): generated with the biome art too, same rule. Referenced here so a
			// client build ships it; until then the editor loads it by path and a build draws a plain green.
			if (profile.MossAlbedo == null) profile.MossAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(MossShaderGlobals.AlbedoPath);
			if (profile.MossNormal == null) profile.MossNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(MossShaderGlobals.NormalPath);
			// The cave cover (2026-10-20): its compute and shader, and its art, generated with the structures (same rule as the moss).
			if (profile.CaveCoverCompute == null) profile.CaveCoverCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(CaveCoverRenderer.ComputeAssetPath);
			if (profile.CaveCoverShader == null) profile.CaveCoverShader = AssetDatabase.LoadAssetAtPath<Shader>(CaveCoverRenderer.ShaderAssetPath);
			if (profile.CaveCoverMesh == null) profile.CaveCoverMesh = AssetDatabase.LoadAssetAtPath<Mesh>(CaveCoverRenderer.MeshAssetPath);
			// The rock seam fillets (2026-10-21): their compute and shader, same rule.
			if (profile.RockFilletsCompute == null) profile.RockFilletsCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(RockFillets.ComputeAssetPath);
			if (profile.RockFilletShader == null) profile.RockFilletShader = AssetDatabase.LoadAssetAtPath<Shader>(RockFillets.ShaderAssetPath);
			// The client's own terrain heightmap draw (2026-10-28): its compute and shader, same rule.
			if (profile.TerrainHeightmapCompute == null) profile.TerrainHeightmapCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(TerrainHeightmapSystem.ComputeAssetPath);
			if (profile.TerrainHeightmapShader == null) profile.TerrainHeightmapShader = AssetDatabase.LoadAssetAtPath<Shader>(TerrainHeightmapSystem.ShaderAssetPath);
			EditorUtility.SetDirty(profile);
			WorldEditorAssets.RegisterAddressable(profile, WorldEditorAssets.ClientStaticGroup);
			AssetDatabase.SaveAssets();
			return profile;
		}
	}
}
