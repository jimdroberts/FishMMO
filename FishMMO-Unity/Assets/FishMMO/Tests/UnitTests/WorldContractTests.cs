using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The core world contracts (Phase 4 of the core and modules plan, 2026-11-17): the world clock, the world tick, water,
	/// climbing and the slim weather reading are core, so gameplay and every module can read them whether or not the sky,
	/// the weather, the biomes or the water are installed. The world modules plug into them; the core never names a module.
	/// </summary>
	[TestFixture]
	public class WorldContractTests
	{
		/// <summary>The core's world contracts, and the hosts that keep the clock on each side.</summary>
		private static readonly string[] CoreWorld =
		{
			"Assets/FishMMO/Shared/Implementation/World",
			"Assets/FishMMO/Client/World/Clock",
			"Assets/FishMMO/Server/Implementation/World/SceneServer/Clock",
			"Assets/FishMMO/Server/Implementation/World/SceneServer/SceneServer/SceneServerSystem.WorldClock.cs",
			"Assets/FishMMO/Shared/Implementation/Entity/WorldSceneDetails/WorldSceneSettings.cs",
			"Assets/FishMMO/Client/GUI/World/Map/TrueNorth.cs",
		};

		/// <summary>Types that belong to the Sky &amp; Time, Climate &amp; Weather, Biomes and Water modules.</summary>
		private static readonly string[] ModuleTypes =
		{
			"SolarSystemProfile", "CalendarProfile", "WorldDayNightCycle", "SkySystem", "SkySchedule",
			"WeatherTimelines", "WeatherTimeline", "WeatherSample", "WeatherDriver", "WeatherField", "WeatherHost", "ClientWeather",
			"StormCell", "SceneWaterBodies", "SeaTide", "BiomeTemplate", "WorldAtlas", "WorldAtlasScene", "AtlasGeometry",
			"ClimateSettings", "SceneBiomeMap", "SceneShelterMap", "WorldBody", "AirOffsets", "WorldSceneClimate",
		};

		[Test]
		public void TheCoreWorldContracts_NameNoWorldModule()
		{
			Regex named = new Regex(@"\b(" + string.Join("|", ModuleTypes) + @")\b");
			int files = 0;
			var hits = new System.Collections.Generic.List<string>();
			foreach (string root in CoreWorld)
			{
				string[] sources = root.EndsWith(".cs")
					? new[] { ProjectFiles.Path(root) }
					: Directory.GetFiles(ProjectFiles.Folder(root), "*.cs", SearchOption.AllDirectories);
				foreach (string file in sources)
				{
					files++;
					foreach (Match hit in named.Matches(SourceScanPins.CodeOnly(File.ReadAllText(file))))
					{
						hits.Add($"{file}: {hit.Value}");
					}
				}
			}
			LogAssert.IsTrue(files >= 20, $"the scan found the core world sources ({files})");
			LogAssert.IsTrue(hits.Count == 0, "the core must reach the world modules through a contract, but names:\n" + string.Join("\n", hits));
		}

		[Test]
		public void WithoutTheWeatherModule_TheWeatherIsCalm()
		{
			var sampler = WeatherQuery.Sampler;
			try
			{
				WeatherQuery.Sampler = null;
				WeatherReading reading = WeatherQuery.Sample(default(Scene), Vector3.zero, 0u);
				LogAssert.AreEqual(WeatherReading.Calm.Kelvin, reading.Kelvin, "no weather module: mild");
				LogAssert.AreEqual(0f, reading.StormSeverity, "calm");
				LogAssert.AreEqual(PrecipitationKind.None, reading.Precipitation, "dry");
				LogAssert.AreEqual(1f, reading.Exposure, "open air");
			}
			finally
			{
				WeatherQuery.Sampler = sampler;
			}
		}

		[Test]
		public void WithoutTheCalendar_TheClockCountsFromTheDefaultEpoch_TheCalendarsOwnDefault()
		{
			LogAssert.AreEqual(WorldEpoch.DefaultUnixSeconds, CalendarProfile.DefaultEpochUnixSeconds, "a world that never changed its calendar reads the same time with the sky module or without it");
			var source = WorldEpoch.Source;
			try
			{
				WorldEpoch.Source = null;
				LogAssert.AreEqual(WorldEpoch.DefaultUnixSeconds, WorldEpoch.UnixSeconds, "no calendar installed");
				WorldEpoch.Source = () => 42L;
				LogAssert.AreEqual(42L, WorldEpoch.UnixSeconds, "an installed calendar's epoch is used");
			}
			finally
			{
				WorldEpoch.Source = source;
			}
		}

		[Test]
		public void TheSkysPreview_IsTheCoreClocksPreview()
		{
			double? hours = WorldClockPreview.Hours;
			try
			{
				WorldDayNightCycle.PreviewHours = 12.5;
				LogAssert.AreEqual(12.5, WorldClockPreview.Hours ?? -1.0, "the sky's preview holds the core clock, which the world's motion follows");
				WorldDayNightCycle.PreviewHours = null;
				LogAssert.IsFalse(WorldClockPreview.Hours.HasValue, "and lets it go");
			}
			finally
			{
				WorldClockPreview.Hours = hours;
			}
		}

		[Test]
		public void WithoutAWorldDesign_ASceneKeepsItsOwnCap_AndItsEntryOverridesIt()
		{
			var lookup = WorldSceneEntries.Lookup;
			var go = new GameObject("WorldContractTests Settings");
			try
			{
				WorldSceneSettings settings = go.AddComponent<WorldSceneSettings>();
				settings.MaxClients = 150;
				WorldSceneEntries.Lookup = null;
				LogAssert.IsTrue(settings.Entry == null, "no world design installed: no entry");
				LogAssert.AreEqual(150, settings.MaxClients, "and the scene keeps its own cap");
				var entry = ScriptableObject.CreateInstance<FishMMO.Shared.Atlas.WorldAtlasScene>();
				entry.MaxClients = 40;
				WorldSceneEntries.Lookup = _ => entry;
				LogAssert.AreEqual(40, settings.MaxClients, "the atlas entry's cap wins, through the core contract");
				LogAssert.AreEqual(0f, settings.Entry.NorthBearingAt(Vector3.zero), "an entry not placed on the globe keeps +Z as north");
				Object.DestroyImmediate(entry);
			}
			finally
			{
				WorldSceneEntries.Lookup = lookup;
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void ASceneThatAuthorsItsOwnClimate_CarriesItBesideTheCoreSettings()
		{
			var go = new GameObject("WorldContractTests Climate");
			var climate = ScriptableObject.CreateInstance<FishMMO.Shared.Biomes.ClimateSettings>();
			try
			{
				WorldSceneSettings settings = go.AddComponent<WorldSceneSettings>();
				LogAssert.IsTrue(go.GetComponent<WorldSceneClimate>() == null, "a scene with no climate of its own carries nothing for it");
				settings.SetClimate(null);
				LogAssert.IsTrue(go.GetComponent<WorldSceneClimate>() == null, "clearing what is not there adds nothing");
				settings.SetClimate(climate);
				LogAssert.IsTrue(go.GetComponent<WorldSceneClimate>() != null, "the climate's own component carries it");
				LogAssert.IsTrue(settings.Climate() == climate, "and the climate reads it (no atlas entry here)");
			}
			finally
			{
				Object.DestroyImmediate(go);
				Object.DestroyImmediate(climate);
			}
		}

		[Test]
		public void TheWorldSimBed_KeepsItsAuthoredClimate()
		{
			string scene = ProjectFiles.ReadAllText("Assets/Scenes/Test/WorldSim.unity").Replace("\r\n", "\n");
			string script = UnityEditor.AssetDatabase.AssetPathToGUID(ProjectFiles.Path("Assets/FishMMO/Shared/Implementation/Weather/WorldSceneClimate.cs"));
			Match doc = Regex.Match(scene, @"  m_Script: \{fileID: 11500000, guid: " + script + @", type: 3\}\n(?:  .*\n)*");
			LogAssert.IsTrue(doc.Success, "the bed's settings object carries a WorldSceneClimate");
			LogAssert.IsTrue(doc.Value.Contains("Climate: {fileID: 11400000, guid: 706a75aad517420f8dde51790ac44689"), "with the Default Climate it authored before the split");
		}

		[Test]
		public void TheCoreShaders_IncludeOnlyTheCoreAndUnity()
		{
			// The core surface stack (Jim, 2026-11-17): FishWeatherLit, FishVegetation and their includes are core rendering, so a
			// world module removed takes none of what they compile from.
			int files = 0;
			var foreign = new System.Collections.Generic.List<string>();
			foreach (string file in Directory.GetFiles(ProjectFiles.Folder("Assets/FishMMO/Shaders"), "*.*", SearchOption.AllDirectories))
			{
				if (!(file.EndsWith(".hlsl") || file.EndsWith(".shader") || file.EndsWith(".compute")))
				{
					continue;
				}
				files++;
				foreach (Match include in Regex.Matches(File.ReadAllText(file), "#include(?:_with_pragmas)?\\s+\"([^\"]+)\""))
				{
					string path = include.Groups[1].Value;
					bool core = path.StartsWith("Assets/FishMMO/Shaders/") || path.StartsWith("Packages/") || path == "UnityIndirect.cginc";
					if (!core)
					{
						foreign.Add($"{file}: {path}");
					}
				}
			}
			LogAssert.IsTrue(files >= 45, $"the scan found the core shaders ({files})");
			LogAssert.IsTrue(foreign.Count == 0, "a core shader includes outside the core:\n" + string.Join("\n", foreign));
		}

		[Test]
		public void WithNoSkyInstalled_TheSurfaceShadersHaveAMildAmbient()
		{
			FishMMO.Client.WorldSurfaceDefaults.Apply();
			Color sky = Shader.GetGlobalColor(FishMMO.Client.WorldSurfaceDefaults.AmbientSkyId);
			LogAssert.IsTrue(sky.r > 0.1f && sky.g > 0.1f && sky.b > 0.1f, "the ambient is never black without a sky: plants and particles would go dark");
		}

		[Test]
		public void TheClock_IsKeptByItsOwnHosts_AndStartsBeforeTheWeather()
		{
			string client = SourceScanPins.ReadCode("Assets/FishMMO/Client/World/Clock/ClientWorldClock.cs");
			LogAssert.IsTrue(client.Contains("RegisterBroadcast<WorldClockBroadcast>"), "the client's clock adopts the server's anchor");
			LogAssert.IsTrue(client.Contains("WorldTick.Source ="), "and is the client's tick source");
			string weather = SourceScanPins.ReadCode("Assets/FishMMO/Client/World/Weather/ClientWeather.cs");
			LogAssert.IsFalse(weather.Contains("WorldClockBroadcast") || weather.Contains("WorldTick.Source") || weather.Contains("WorldMotion.TimeManager"), "the weather only reads the clock");
			string host = SourceScanPins.ReadCode("Assets/FishMMO/Server/Implementation/World/SceneServer/Clock/WorldClockHost.cs");
			LogAssert.IsTrue(host.Contains("WorldTick.Source ="), "the server's clock is the server's tick source");
			string weatherHost = SourceScanPins.ReadCode("Assets/FishMMO/Server/Implementation/World/SceneServer/Weather/WeatherHost.cs");
			LogAssert.IsFalse(weatherHost.Contains("TickSource ="), "not the weather host, or a server without weather would have no tick");

			string system = SourceScanPins.ReadCode("Assets/FishMMO/Server/Implementation/World/SceneServer/SceneServer/SceneServerSystem.cs");
			int clockAt = system.IndexOf("InitializeWorldClock(");
			int weatherAt = system.IndexOf("InitializeWeather(");
			LogAssert.IsTrue(clockAt >= 0 && weatherAt > clockAt, "the clock starts before the weather, so its spawn handler tells an arriving character the clock first");
			string game = SourceScanPins.ReadCode("Assets/FishMMO/Client/Client.cs");
			LogAssert.IsTrue(game.IndexOf("new ClientWorldClock()") >= 0 && game.IndexOf("new ClientWorldClock()") < game.IndexOf("new ClientWeather()"), "the client's clock is set up before its weather");
		}
	}
}
