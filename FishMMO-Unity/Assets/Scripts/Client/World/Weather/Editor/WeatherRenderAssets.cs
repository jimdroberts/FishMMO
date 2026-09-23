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
			SolarSystemProfile system = WorldEditorAssets.FindFirst<SolarSystemProfile>();
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
			if (profile.CloudCookieMaterial == null) profile.CloudCookieMaterial = cookie;
			if (profile.CloudMaterial == null) profile.CloudMaterial = clouds;
			// The volumes the clouds are carved from, baked once and kept.
			if (profile.CloudShape == null) profile.CloudShape = CloudNoiseBaker.Ensure(CloudNoiseBaker.ShapePath, CloudNoiseBaker.ShapeSize, CloudNoiseBaker.DefaultSeed, true);
			if (profile.CloudDetail == null) profile.CloudDetail = CloudNoiseBaker.Ensure(CloudNoiseBaker.DetailPath, CloudNoiseBaker.DetailSize, CloudNoiseBaker.DefaultSeed + 17, false);
			CloudRendererSetup.EnsureFeature(clouds);
			EditorUtility.SetDirty(profile);
			WorldEditorAssets.RegisterAddressable(profile, WorldEditorAssets.ClientStaticGroup);
			AssetDatabase.SaveAssets();
			return profile;
		}
	}
}
