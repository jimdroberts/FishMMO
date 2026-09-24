#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Creates the default weather content: one layer template per kind, the named presets, a few
	/// biome variants, the climate's default profile and a profile for every biome nobody has
	/// authored. Existing assets and authored profiles are never replaced.
	/// </summary>
	public static class WeatherContentGenerator
	{
		public const string Root = "Assets/Templates/Weather";
		public const string LayersFolder = Root + "/Layers";
		public const string PresetsFolder = Root + "/Presets";
		public const string VariantsFolder = Root + "/Presets/Biome Variants";

		/// <summary>What one run did.</summary>
		public sealed class Report
		{
			public int Templates;
			public int Presets;
			public int Biomes;
			public int Climates;
			public readonly List<string> Skipped = new List<string>();
			public override string ToString() =>
				$"{Templates} layer template(s), {Presets} preset(s), {Biomes} biome profile(s) and {Climates} climate default(s) created; {Skipped.Count} left as authored.";
		}

		[DashboardTool(DashboardToolAttribute.Weather, "Create default weather content", Section = "Content", Order = 0,
			Tooltip = "Creates the weather layer templates and presets under Assets/Templates/Weather, and fills the weather profile of every biome and climate that has none. Authored content is left alone.")]
		public static void GenerateFromDashboard()
		{
			Report report = Generate();
			Debug.Log("[Weather content] " + report);
			foreach (string line in report.Skipped)
			{
				Debug.Log("[Weather content] kept: " + line);
			}
		}

		public static Report Generate(string root = Root, bool fillBiomes = true)
		{
			var report = new Report();
			string layersFolder = root + "/Layers";
			string presetsFolder = root + "/Presets";
			string variantsFolder = root + "/Presets/Biome Variants";

			var templates = new Dictionary<WeatherLayerKind, WeatherLayerTemplate>();
			foreach (WeatherLayerKind kind in (WeatherLayerKind[])Enum.GetValues(typeof(WeatherLayerKind)))
			{
				templates[kind] = FindOrCreate<WeatherLayerTemplate>(layersFolder, kind.ToString(), report, t =>
				{
					t.Kind = kind;
					DefineTemplate(t);
					report.Templates++;
				});
			}

			var presets = new Dictionary<string, WeatherPreset>(StringComparer.Ordinal);
			foreach (PresetSpec spec in PresetSpecs)
			{
				presets[spec.Name] = FindOrCreate<WeatherPreset>(spec.Variant ? variantsFolder : presetsFolder, spec.Name, report, p =>
				{
					p.DisplayName = spec.Name;
					p.TransitionSeconds = spec.Transition;
					p.DurationMinutes = spec.Minutes;
					p.CellRadiusMeters = spec.Radius;
					foreach ((WeatherLayerKind kind, float intensity) in spec.Layers)
					{
						p.Layers.Add(new WeatherPresetLayer { Template = templates[kind], Intensity = intensity });
					}
					report.Presets++;
				});
			}

			if (!fillBiomes)
			{
				AssetDatabase.SaveAssets();
				return report;
			}

			foreach (ClimateSettings climate in WorldEditorAssets.FindAll<ClimateSettings>())
			{
				if (climate.DefaultWeather != null && climate.DefaultWeather.IsAuthored)
				{
					report.Skipped.Add($"climate {climate.name}");
					continue;
				}
				Undo.RecordObject(climate, "Default weather");
				climate.DefaultWeather = TemperateProfile(presets);
				EditorUtility.SetDirty(climate);
				report.Climates++;
			}

			foreach (BiomeTemplate biome in WorldEditorAssets.FindAll<BiomeTemplate>())
			{
				if (biome.Weather != null && biome.Weather.IsAuthored)
				{
					report.Skipped.Add($"biome {biome.name}");
					continue;
				}
				BiomeWeatherProfile profile = ProfileForBiome(biome, presets);
				if (profile == null)
				{
					// Ordinary land: the climate's default fits it.
					continue;
				}
				Undo.RecordObject(biome, "Biome weather");
				biome.Weather = profile;
				EditorUtility.SetDirty(biome);
				report.Biomes++;
			}
			AssetDatabase.SaveAssets();
			return report;
		}

		private static T FindOrCreate<T>(string folder, string name, Report report, Action<T> setup) where T : ScriptableObject
		{
			string path = $"{folder}/{WorldEditorAssets.Sanitize(name)}.asset";
			T existing = AssetDatabase.LoadAssetAtPath<T>(path);
			if (existing != null)
			{
				report.Skipped.Add($"{typeof(T).Name} {name}");
				return existing;
			}
			WorldEditorAssets.EnsureFolder(folder);
			T asset = ScriptableObject.CreateInstance<T>();
			asset.name = WorldEditorAssets.Sanitize(name);
			setup(asset);
			AssetDatabase.CreateAsset(asset, path);
			WorldEditorAssets.RegisterAddressable(asset);
			return asset;
		}

		// ── Layer templates ──

		private static AnimationCurve Line(float from, float to) => AnimationCurve.Linear(0f, from, 1f, to);

		/// <summary>Grows slowly, then fast: a sprinkle is little, a downpour a lot.</summary>
		private static AnimationCurve Ease(float from, float to)
		{
			return new AnimationCurve(new Keyframe(0f, from, 0f, 0f), new Keyframe(1f, to, (to - from) * 2f, 0f));
		}

		[DashboardTool(DashboardToolAttribute.Weather, "Update layer curves and presets to the shipped ones", Section = "Content", Order = 4,
			Tooltip = "Re-applies the built-in channel curves to every weather layer template, and the built-in layer sets to every preset. Use it after the shipped content changes; it OVERWRITES hand-tuning on both.",
			Confirm = "Overwrite the curves on every layer template and the layers on every preset with the shipped ones? Hand-tuned values will be lost.")]
		public static void RefreshLayerCurvesFromDashboard()
		{
			var changed = new List<string>();
			foreach (string guid in AssetDatabase.FindAssets("t:WeatherLayerTemplate"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var template = AssetDatabase.LoadAssetAtPath<WeatherLayerTemplate>(path);
				if (template == null)
				{
					continue;
				}
				DefineTemplate(template);
				EditorUtility.SetDirty(template);
				changed.Add(template.name);
			}
			// The presets too: their layer sets are shipped content in the same way the curves are.
			var templates = new Dictionary<WeatherLayerKind, WeatherLayerTemplate>();
			foreach (string guid in AssetDatabase.FindAssets("t:WeatherLayerTemplate"))
			{
				var template = AssetDatabase.LoadAssetAtPath<WeatherLayerTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (template != null)
				{
					templates[template.Kind] = template;
				}
			}
			var presets = new List<string>();
			foreach (string guid in AssetDatabase.FindAssets("t:WeatherPreset"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var preset = AssetDatabase.LoadAssetAtPath<WeatherPreset>(path);
				if (preset == null)
				{
					continue;
				}
				PresetSpec spec = System.Array.Find(PresetSpecs, candidate => candidate.Name == preset.name);
				if (spec == null || spec.Layers == null)
				{
					continue;
				}
				preset.Layers.Clear();
				foreach ((WeatherLayerKind kind, float intensity) in spec.Layers)
				{
					if (templates.TryGetValue(kind, out WeatherLayerTemplate template))
					{
						preset.Layers.Add(new WeatherPresetLayer { Template = template, Intensity = intensity });
					}
				}
				EditorUtility.SetDirty(preset);
				presets.Add(preset.name);
			}
			AssetDatabase.SaveAssets();
			Debug.Log($"[Weather content] {changed.Count} layer template(s) and {presets.Count} preset(s) back on the shipped content." +
				(changed.Count > 0 ? $"\n  Templates: {string.Join(", ", changed)}" : string.Empty) +
				(presets.Count > 0 ? $"\n  Presets: {string.Join(", ", presets)}" : string.Empty));
		}

		private static void Add(WeatherLayerTemplate t, WeatherChannel channel, AnimationCurve curve, float scale = 1f)
		{
			t.Channels.Add(new WeatherChannelCurve { Channel = channel, Curve = curve, Scale = scale });
		}

		/// <summary>The channels each kind writes, from intensity 0 to 1.</summary>
		public static void DefineTemplate(WeatherLayerTemplate t)
		{
			t.Channels.Clear();
			t.MinTemperature = -1f;
			t.MaxTemperature = 1f;
			t.DefaultTransitionSeconds = 30f;
			switch (t.Kind)
			{
				case WeatherLayerKind.Clouds:
					Add(t, WeatherChannel.CloudCover, Line(0.05f, 1f));
					Add(t, WeatherChannel.CloudDensity, Line(0.2f, 1f));
					Add(t, WeatherChannel.CloudBase, Line(0.8f, 0.3f));
					t.DefaultTransitionSeconds = 60f;
					break;
				case WeatherLayerKind.Rain:
					Add(t, WeatherChannel.Precipitation, Ease(0f, 1f));
					Add(t, WeatherChannel.DropSize, Line(0.3f, 1f));
					// Rain comes out of a deck of thick low cloud, not out of a lid over the world:
					// a shower leaves plenty of sky, and only a downpour closes it over.
					Add(t, WeatherChannel.CloudCover, Line(0.4f, 0.92f));
					Add(t, WeatherChannel.CloudDensity, Line(0.4f, 1f));
					Add(t, WeatherChannel.FogDensity, Ease(0f, 0.25f));
					Add(t, WeatherChannel.WindGust, Line(0f, 0.3f));
					Add(t, WeatherChannel.HumidityOffset, Line(0.05f, 0.3f));
					break;
				case WeatherLayerKind.Snow:
					Add(t, WeatherChannel.Precipitation, Ease(0f, 0.9f));
					Add(t, WeatherChannel.DropSize, Line(0.1f, 0.6f));
					Add(t, WeatherChannel.CloudCover, Line(0.5f, 0.95f));
					Add(t, WeatherChannel.CloudDensity, Line(0.4f, 0.9f));
					Add(t, WeatherChannel.FogDensity, Ease(0f, 0.4f));
					Add(t, WeatherChannel.TemperatureOffset, Line(-0.05f, -0.2f));
					t.MaxTemperature = 0.25f;
					break;
				case WeatherLayerKind.Hail:
					Add(t, WeatherChannel.Precipitation, Ease(0f, 0.8f));
					Add(t, WeatherChannel.DropSize, Line(0.5f, 1f));
					Add(t, WeatherChannel.CloudCover, Line(0.8f, 1f));
					Add(t, WeatherChannel.CloudDensity, Line(0.8f, 1f));
					Add(t, WeatherChannel.WindGust, Line(0.2f, 0.6f));
					Add(t, WeatherChannel.TemperatureOffset, Line(0f, -0.1f));
					t.DefaultTransitionSeconds = 15f;
					break;
				case WeatherLayerKind.Ash:
					Add(t, WeatherChannel.Precipitation, Ease(0f, 0.7f));
					Add(t, WeatherChannel.DropSize, Line(0.2f, 0.4f));
					Add(t, WeatherChannel.CloudCover, Line(0.4f, 1f));
					Add(t, WeatherChannel.CloudDensity, Line(0.3f, 0.9f));
					Add(t, WeatherChannel.FogDensity, Line(0.1f, 0.6f));
					Add(t, WeatherChannel.FogHeight, Line(0.4f, 0.8f));
					Add(t, WeatherChannel.TemperatureOffset, Line(0f, 0.1f));
					t.DefaultTransitionSeconds = 90f;
					break;
				case WeatherLayerKind.Sand:
					Add(t, WeatherChannel.Precipitation, Line(0f, 1f));
					Add(t, WeatherChannel.DropSize, Line(0.1f, 0.3f));
					Add(t, WeatherChannel.WindSpeed, Line(0.3f, 1f));
					/* No heading. A sandstorm means "it is blowing hard and carrying sand", not "the
					 * wind blows toward 70 degrees" — see the Wind layer below for why. */
					Add(t, WeatherChannel.WindGust, Line(0.2f, 0.8f));
					Add(t, WeatherChannel.FogDensity, Line(0.1f, 0.8f));
					Add(t, WeatherChannel.FogHeight, Line(0.2f, 0.5f));
					Add(t, WeatherChannel.HumidityOffset, Line(0f, -0.4f));
					break;
				case WeatherLayerKind.Wind:
					Add(t, WeatherChannel.WindSpeed, Line(0f, 1f));
					/* SPEED ONLY, NEVER A HEADING.
					 *
					 * This used to write a flat 60 degrees, and the sand layer 70. Heading blends as
					 * a vector weighted by speed, so any preset containing wind dragged the whole
					 * scene's wind toward one authored compass point — and since most stormy presets
					 * contain a wind layer, that was most weather. A world with trade winds,
					 * westerlies and polar easterlies blew the same way everywhere, all year.
					 *
					 * The direction is the DRIVER's: WeatherDriver.PrevailingWind bands it by
					 * latitude and leans it poleward, and it breathes over hours. A layer says how
					 * hard it is blowing; where it blows from is a property of the place and the
					 * season, not of the preset that happens to be running. */
					Add(t, WeatherChannel.WindGust, Ease(0f, 0.7f));
					t.DefaultTransitionSeconds = 20f;
					break;
				case WeatherLayerKind.Fog:
					Add(t, WeatherChannel.FogDensity, Line(0f, 1f));
					Add(t, WeatherChannel.FogHeight, Line(0.2f, 0.6f));
					Add(t, WeatherChannel.VolumetricFog, Line(0f, 0.8f));
					Add(t, WeatherChannel.HumidityOffset, Line(0f, 0.15f));
					t.DefaultTransitionSeconds = 90f;
					break;
				case WeatherLayerKind.Lightning:
					Add(t, WeatherChannel.LightningRate, Line(0f, 1f));
					Add(t, WeatherChannel.CloudCover, Line(0.8f, 1f));
					Add(t, WeatherChannel.CloudDensity, Line(0.9f, 1f));
					t.DefaultTransitionSeconds = 10f;
					break;
				case WeatherLayerKind.Aurora:
					Add(t, WeatherChannel.Aurora, Line(0f, 1f));
					t.MaxTemperature = 0.2f;
					t.DefaultTransitionSeconds = 120f;
					break;
				case WeatherLayerKind.ClearSky:
					// No channels, on purpose: at full strength it overrides the drifting field and
					// asks for nothing in its place, which is what a clear sky is.
					t.DefaultTransitionSeconds = 60f;
					break;
			}
		}

		// ── Presets ──

		private sealed class PresetSpec
		{
			public string Name;
			public (WeatherLayerKind, float)[] Layers;
			public float Transition = 45f;
			public Vector2 Minutes = new Vector2(10f, 25f);
			public Vector2 Radius = new Vector2(150f, 600f);
			public bool Variant;
		}

		private static PresetSpec P(string name, float transition, Vector2 minutes, Vector2 radius, params (WeatherLayerKind, float)[] layers)
			=> new PresetSpec { Name = name, Transition = transition, Minutes = minutes, Radius = radius, Layers = layers };

		private static PresetSpec V(string name, params (WeatherLayerKind, float)[] layers)
			=> new PresetSpec { Name = name, Layers = layers, Variant = true };

		private const WeatherLayerKind Clouds = WeatherLayerKind.Clouds, Rain = WeatherLayerKind.Rain, Snow = WeatherLayerKind.Snow,
			Hail = WeatherLayerKind.Hail, Ash = WeatherLayerKind.Ash, Sand = WeatherLayerKind.Sand, Wind = WeatherLayerKind.Wind,
			Fog = WeatherLayerKind.Fog, Lightning = WeatherLayerKind.Lightning, Aurora = WeatherLayerKind.Aurora,
			ClearSky = WeatherLayerKind.ClearSky;

		private static readonly PresetSpec[] PresetSpecs =
		{
			// A clear sky is a layer that asks for nothing at full strength, so it overrides the
			// field. A Clouds layer at zero — what this was — is skipped as nothing at all.
			P("Clear", 60f, new Vector2(15f, 40f), new Vector2(400f, 900f), (ClearSky, 1f)),
			P("Fair", 60f, new Vector2(15f, 40f), new Vector2(300f, 800f), (Clouds, 0.3f), (Wind, 0.15f)),
			P("Overcast", 60f, new Vector2(15f, 35f), new Vector2(300f, 800f), (Clouds, 1f), (Wind, 0.2f)),
			P("Mist", 60f, new Vector2(10f, 25f), new Vector2(200f, 600f), (Fog, 0.75f), (Clouds, 0.45f)),
			P("Sprinkle", 30f, new Vector2(5f, 15f), new Vector2(150f, 400f), (Rain, 0.15f), (Clouds, 0.6f)),
			P("Light Rain", 40f, new Vector2(10f, 25f), new Vector2(200f, 500f), (Rain, 0.35f), (Clouds, 0.75f), (Wind, 0.2f)),
			P("Medium Rain", 40f, new Vector2(10f, 25f), new Vector2(200f, 550f), (Rain, 0.6f), (Clouds, 0.9f), (Wind, 0.3f)),
			P("Heavy Rain", 45f, new Vector2(8f, 20f), new Vector2(200f, 550f), (Rain, 0.9f), (Clouds, 1f), (Wind, 0.45f), (Fog, 0.25f)),
			P("Thunderstorm", 40f, new Vector2(8f, 20f), new Vector2(250f, 600f), (Rain, 0.85f), (Clouds, 1f), (Wind, 0.6f), (Lightning, 0.7f), (Fog, 0.2f)),
			P("Light Snow", 60f, new Vector2(15f, 35f), new Vector2(250f, 600f), (Snow, 0.3f), (Clouds, 0.8f)),
			P("Heavy Snow", 60f, new Vector2(12f, 30f), new Vector2(250f, 600f), (Snow, 0.8f), (Clouds, 1f), (Wind, 0.3f), (Fog, 0.3f)),
			P("Blizzard", 45f, new Vector2(10f, 25f), new Vector2(300f, 650f), (Snow, 1f), (Wind, 0.85f), (Clouds, 0.95f), (Fog, 0.6f)),
			P("Hailstorm", 20f, new Vector2(5f, 12f), new Vector2(150f, 350f), (Hail, 0.8f), (Rain, 0.35f), (Clouds, 1f), (Wind, 0.5f), (Lightning, 0.3f)),
			P("Ashfall", 90f, new Vector2(15f, 40f), new Vector2(300f, 700f), (Ash, 0.7f), (Fog, 0.4f), (Clouds, 0.6f)),
			P("Sandstorm", 30f, new Vector2(8f, 20f), new Vector2(300f, 700f), (Sand, 0.9f), (Wind, 0.9f)),
			P("Windy", 20f, new Vector2(8f, 20f), new Vector2(300f, 800f), (Wind, 0.7f), (Clouds, 0.35f)),
			P("Aurora Night", 120f, new Vector2(20f, 40f), new Vector2(500f, 900f), (Aurora, 0.8f), (Clouds, 0.05f)),
			V("Swamp Rain", (Rain, 0.75f), (Fog, 0.55f), (Clouds, 0.95f), (Wind, 0.1f)),
			V("Jungle Downpour", (Rain, 1f), (Clouds, 1f), (Fog, 0.35f), (Lightning, 0.2f)),
			V("Tundra Snow", (Snow, 0.55f), (Wind, 0.6f), (Clouds, 0.7f)),
			V("Dune Sandstorm", (Sand, 1f), (Wind, 1f), (Fog, 0.2f)),
			V("Eruption Ashfall", (Ash, 1f), (Fog, 0.6f), (Clouds, 1f), (Lightning, 0.2f)),
		};

		private static WeightedWeatherPreset W(Dictionary<string, WeatherPreset> presets, string name, float weight)
			=> new WeightedWeatherPreset { Preset = presets[name], Weight = weight };

		private static WeatherPresetSuitability S(Dictionary<string, WeatherPreset> presets, string name, float value)
			=> new WeatherPresetSuitability { Preset = presets[name], Suitability = value };

		private static WeatherPresetVariant Swap(Dictionary<string, WeatherPreset> presets, string preset, string variant)
			=> new WeatherPresetVariant { Preset = presets[preset], Variant = presets[variant] };

		private static WeatherPresetLayer Bg(Dictionary<string, WeatherPreset> presets, WeatherLayerKind kind, float intensity)
		{
			foreach (WeatherPresetLayer layer in presets["Mist"].Layers)
			{
				if (layer.Template != null && layer.Template.Kind == kind)
				{
					return new WeatherPresetLayer { Template = layer.Template, Intensity = intensity };
				}
			}
			foreach (WeatherPreset preset in presets.Values)
			{
				foreach (WeatherPresetLayer layer in preset.Layers)
				{
					if (layer.Template != null && layer.Template.Kind == kind)
					{
						return new WeatherPresetLayer { Template = layer.Template, Intensity = intensity };
					}
				}
			}
			return null;
		}

		/// <summary>The default for ordinary land: mostly fair, some rain, the odd storm.</summary>
		public static BiomeWeatherProfile TemperateProfile(Dictionary<string, WeatherPreset> presets)
		{
			var profile = new BiomeWeatherProfile { CellsPerSquareKm = 0.4f };
			profile.CellSpawns.Add(W(presets, "Fair", 3f));
			profile.CellSpawns.Add(W(presets, "Overcast", 2f));
			profile.CellSpawns.Add(W(presets, "Sprinkle", 2f));
			profile.CellSpawns.Add(W(presets, "Light Rain", 2f));
			profile.CellSpawns.Add(W(presets, "Medium Rain", 1f));
			profile.CellSpawns.Add(W(presets, "Heavy Rain", 0.5f));
			profile.CellSpawns.Add(W(presets, "Thunderstorm", 0.5f));
			profile.CellSpawns.Add(W(presets, "Mist", 1f));
			profile.CellSpawns.Add(W(presets, "Windy", 1f));
			profile.CellSpawns.Add(W(presets, "Light Snow", 0.5f));
			profile.CellSpawns.Add(W(presets, "Hailstorm", 0.2f));
			profile.CellSpawns.Add(W(presets, "Aurora Night", 0.3f));
			profile.Suitability.Add(S(presets, "Sandstorm", 0.2f));
			profile.Suitability.Add(S(presets, "Dune Sandstorm", 0.2f));
			profile.Suitability.Add(S(presets, "Ashfall", 0.3f));
			return profile;
		}

		private static bool Has(string name, params string[] words)
		{
			foreach (string word in words)
			{
				if (name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>A profile for a biome by its name, or null when the climate default suits it.</summary>
		public static BiomeWeatherProfile ProfileForBiome(BiomeTemplate biome, Dictionary<string, WeatherPreset> presets)
		{
			string name = biome.name;
			var p = new BiomeWeatherProfile();

			if (Has(name, "abyss", "coral", "seamount", "underwater", "deep ocean", "cave"))
			{
				// Under water or under rock: nothing falls and nothing blows.
				p.Forbidden = WeatherKindMask.All;
				p.CellsPerSquareKm = 0f;
				return p;
			}
			if (Has(name, "desert", "badlands", "wasteland", "scrub", "savanna"))
			{
				p.CellsPerSquareKm = 0.3f;
				p.CellSpawns.Add(W(presets, "Fair", 3f));
				p.CellSpawns.Add(W(presets, "Windy", 3f));
				p.CellSpawns.Add(W(presets, "Sandstorm", Has(name, "desert", "badlands") ? 4f : 1.5f));
				p.CellSpawns.Add(W(presets, "Sprinkle", Has(name, "savanna", "scrub") ? 1.5f : 0.3f));
				p.Background.Add(Bg(presets, Fog, 0.08f));
				p.Suitability.Add(S(presets, "Heavy Rain", 0.3f));
				p.Suitability.Add(S(presets, "Thunderstorm", 0.4f));
				p.Suitability.Add(S(presets, "Swamp Rain", 0.1f));
				if (Has(name, "desert"))
				{
					p.Variants.Add(Swap(presets, "Sandstorm", "Dune Sandstorm"));
				}
				return p;
			}
			if (Has(name, "volcanic", "crater"))
			{
				p.CellsPerSquareKm = 0.35f;
				p.CellSpawns.Add(W(presets, "Ashfall", 4f));
				p.CellSpawns.Add(W(presets, "Overcast", 2f));
				p.CellSpawns.Add(W(presets, "Windy", 1f));
				p.Background.Add(Bg(presets, Fog, 0.15f));
				p.Variants.Add(Swap(presets, "Ashfall", "Eruption Ashfall"));
				p.Forbidden = WeatherKindMask.Snow;
				return p;
			}
			if (Has(name, "glacier", "permanent ice", "snow", "tundra", "taiga", "ice", "scree", "mountain slope"))
			{
				bool extreme = Has(name, "glacier", "permanent ice", "snow", "ice");
				p.CellsPerSquareKm = 0.4f;
				p.CellSpawns.Add(W(presets, "Light Snow", 3f));
				p.CellSpawns.Add(W(presets, "Heavy Snow", 2f));
				p.CellSpawns.Add(W(presets, "Blizzard", extreme ? 2f : 0.8f));
				p.CellSpawns.Add(W(presets, "Overcast", 2f));
				p.CellSpawns.Add(W(presets, "Aurora Night", 1f));
				p.CellSpawns.Add(W(presets, "Windy", 1.5f));
				if (extreme)
				{
					p.Background.Add(Bg(presets, Wind, 0.2f));
				}
				if (Has(name, "tundra", "taiga"))
				{
					p.Variants.Add(Swap(presets, "Light Snow", "Tundra Snow"));
				}
				p.Suitability.Add(S(presets, "Sandstorm", 0f));
				p.Suitability.Add(S(presets, "Ashfall", 0.2f));
				p.Forbidden = WeatherKindMask.Sand;
				return p;
			}
			if (Has(name, "jungle", "swamp", "mangrove", "wetland", "estuary"))
			{
				bool swamp = Has(name, "swamp", "mangrove", "wetland");
				p.CellsPerSquareKm = 0.55f;
				p.CellSpawns.Add(W(presets, "Medium Rain", 3f));
				p.CellSpawns.Add(W(presets, "Heavy Rain", 3f));
				p.CellSpawns.Add(W(presets, "Thunderstorm", 2f));
				p.CellSpawns.Add(W(presets, "Mist", 2f));
				p.CellSpawns.Add(W(presets, "Overcast", 1f));
				if (swamp)
				{
					p.Background.Add(Bg(presets, Fog, 0.25f));
				}
				p.Variants.Add(Swap(presets, "Heavy Rain", swamp ? "Swamp Rain" : "Jungle Downpour"));
				p.Suitability.Add(S(presets, "Sandstorm", 0.05f));
				p.Forbidden = WeatherKindMask.Sand;
				return p;
			}
			if (Has(name, "ocean", "coast", "lake", "river", "beach", "water"))
			{
				p.CellsPerSquareKm = 0.45f;
				p.CellSpawns.Add(W(presets, "Mist", 2f));
				p.CellSpawns.Add(W(presets, "Light Rain", 2f));
				p.CellSpawns.Add(W(presets, "Heavy Rain", 2f));
				p.CellSpawns.Add(W(presets, "Thunderstorm", 1f));
				p.CellSpawns.Add(W(presets, "Windy", 2f));
				p.CellSpawns.Add(W(presets, "Fair", 2f));
				p.Forbidden = WeatherKindMask.Sand | WeatherKindMask.Ash;
				return p;
			}
			return null;
		}
	}
}
#endif
