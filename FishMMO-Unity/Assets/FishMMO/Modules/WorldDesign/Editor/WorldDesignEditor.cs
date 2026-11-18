using System;
using UnityEditor;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Plugs the World Design module into the core editor (Phase 4 of the core and modules plan, 2026-11-18): the Solar System
	/// and World Atlas pages on the dashboard (<see cref="DashboardFullPages"/>), the biome art generator's lock on the tool
	/// buttons (<see cref="DashboardJobs"/>), and the generated art and planet surfaces the Addressables dashboard keeps out
	/// of the committed groups (<see cref="AddressableGeneratedContent"/>), and the biome spec the weather content generator
	/// fills the biomes' ground from (<see cref="WeatherContentGenerator.BiomeGround"/>).
	/// </summary>
	public static class WorldDesignEditor
	{
		/// <summary>The dashboard pages' names (their categories under World).</summary>
		public const string SolarSystemPageName = "Solar System", WorldAtlasPageName = "World Atlas";

		[InitializeOnLoadMethod]
		public static void Install()
		{
			DashboardFullPages.Register(SolarSystemPageName, "World", 0, () => new SolarSystemPage());
			DashboardFullPages.Register(WorldAtlasPageName, "World", 1, () => new WorldAtlasPage());

			DashboardJobs.Register("world-design.biome-art", () => BiomeArtGenerator.IsBusy);
			BiomeArtGenerator.BusyChanged -= DashboardJobs.NotifyChanged;
			BiomeArtGenerator.BusyChanged += DashboardJobs.NotifyChanged;

			AddressableGeneratedContent.RegisterGenerated("world-design.procedural-art", ProceduralArtBuildGroup.IsGenerated);
			AddressableGeneratedContent.RegisterGenerated("world-design.planet-surfaces",
				path => path != null && path.StartsWith(PlanetSurfaceBaker.BakedDirectory + "/", StringComparison.OrdinalIgnoreCase));
			AddressableGeneratedContent.RegisterCommittedCleanup("world-design.procedural-art", ProceduralArtBuildGroup.RemoveCommittedEntries);

			WeatherContentGenerator.BiomeGround = BiomeGroundFromSpec;
		}

		/// <summary>What the biome's spec file says its ground gives the air (BiomeArtSpec.Entry.LooseGround and Emission).</summary>
		public static bool BiomeGroundFromSpec(string biomeName, out string looseGround, out string emits, out float emissionRate)
		{
			BiomeArtSpec.Entry entry = BiomeArtSpec.For(biomeName);
			looseGround = null;
			emits = null;
			emissionRate = 0f;
			if (entry == null || entry.LooseGround == null && !entry.Emission.HasValue)
			{
				return false;
			}
			looseGround = string.IsNullOrEmpty(entry.LooseGround) ? null : entry.LooseGround;
			if (entry.Emission.HasValue)
			{
				emits = entry.Emission.Value.Substance;
				emissionRate = entry.Emission.Value.Rate;
			}
			return true;
		}

		/// <summary>Opens the dashboard on the World Atlas, with a scene selected when given.</summary>
		public static void OpenWorldAtlas(string sceneName = null)
		{
			WorldAtlasPage.PendingSelection = sceneName;
			FishMMODashboard.OpenFullPage(WorldAtlasPageName);
		}
	}
}
