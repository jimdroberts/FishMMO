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
					foreach (Match hit in named.Matches(Regex.Replace(SourceScanPins.CodeOnly(File.ReadAllText(file)), "\"(?:[^\"\\\\]|\\\\.)*\"", "\"\"")))
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
			bool weatherInstalled = InstalledModules.Has("WorldSimulation");
			if (weatherInstalled)
			{
				string weather = SourceScanPins.ReadCode("Assets/FishMMO/Modules/WorldSimulation/Client/Weather/ClientWeather.cs");
				LogAssert.IsFalse(weather.Contains("WorldClockBroadcast") || weather.Contains("WorldTick.Source") || weather.Contains("WorldMotion.TimeManager"), "the weather only reads the clock");
			}
			string host = SourceScanPins.ReadCode("Assets/FishMMO/Server/Implementation/World/SceneServer/Clock/WorldClockHost.cs");
			LogAssert.IsTrue(host.Contains("WorldTick.Source ="), "the server's clock is the server's tick source");
			if (weatherInstalled)
			{
				string weatherHost = SourceScanPins.ReadCode("Assets/FishMMO/Modules/WorldSimulation/Server/Weather/WeatherHost.cs");
				LogAssert.IsFalse(weatherHost.Contains("TickSource ="), "not the weather host, or a server without weather would have no tick");
			}

			string system = SourceScanPins.ReadCode("Assets/FishMMO/Server/Implementation/World/SceneServer/SceneServer/SceneServerSystem.cs");
			int clockAt = system.IndexOf("InitializeWorldClock(");
			// The weather is a module service (SceneServerModuleServices), started after the clock.
			int weatherAt = system.IndexOf("InitializeModuleServices(");
			LogAssert.IsTrue(clockAt >= 0 && weatherAt > clockAt, "the clock starts before the weather, so its spawn handler tells an arriving character the clock first");
			string game = SourceScanPins.ReadCode("Assets/FishMMO/Client/Client.cs");
			LogAssert.IsTrue(game.IndexOf("new ClientWorldClock()") >= 0 && game.IndexOf("new ClientWorldClock()") < game.IndexOf("ClientServices.CreateAll("), "the client's clock is set up before its weather");
		}
	}
}
