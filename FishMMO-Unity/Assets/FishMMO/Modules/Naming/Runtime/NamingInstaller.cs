using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Core;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Plugs the Naming module into the core (Phase 4 of the core and modules plan, 2026-11-18): the race registry follows the
	/// races the core loads (<see cref="RaceTemplate.RaceLoaded"/>), and the scene objects' names come from the generator
	/// (<see cref="SceneObjectNames.Source"/>).
	/// </summary>
	public static class NamingInstaller
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void Install()
		{
			RaceTemplate.RaceLoaded -= RaceRegistry.Register;
			RaceTemplate.RaceLoaded += RaceRegistry.Register;
			RaceTemplate.RaceUnloaded -= RaceRegistry.Unregister;
			RaceTemplate.RaceUnloaded += RaceRegistry.Unregister;
			foreach (RaceTemplate race in RaceTemplate.Loaded)
			{
				RaceRegistry.Register(race);
			}
			SceneObjectNames.Source = SceneObjectNameSource.Instance;
		}

#if UNITY_EDITOR
		[UnityEditor.InitializeOnLoadMethod]
		private static void InstallInEditor() => Install();
#endif
	}

	/// <summary>The generator as the core's scene object name source (<see cref="ISceneObjectNameSource"/>), over <see cref="SceneObjectNameResolver"/>.</summary>
	public sealed class SceneObjectNameSource : ISceneObjectNameSource
	{
		public static readonly SceneObjectNameSource Instance = new SceneObjectNameSource();

		public int DeriveSeed(SceneObjectNamingSettings settings, string fallbackObjectSeed) => SceneObjectNameResolver.DeriveSeed(settings, fallbackObjectSeed);

		public CharacterGender PickGender(SceneObjectNamingSettings settings, RaceTemplate ownRace, int seed)
		{
			RaceTemplate race = SceneObjectNameResolver.ResolveRace(settings, ownRace);
			return SceneObjectNameResolver.ResolveGender(settings.GenderPolicy, race, SceneObjectNameResolver.GenderRng(seed));
		}

		public void ResolvePlace(SceneObjectNamingSettings settings, Vector3 position, Scene scene, out int biomeID, out byte variantIndex)
		{
			WorldSceneSettings.TryGetForScene(scene, out WorldSceneSettings sceneSettings);
			BiomeTemplate biome = SceneObjectNameResolver.ResolveBiome(settings, position, sceneSettings, out BiomeClimateVariant variant);
			biomeID = BiomeRegistry.IDOf(biome);
			variantIndex = SceneObjectNameResolver.VariantIndexOf(biome, sceneSettings, variant);
		}

		public bool TryBuild(SceneObjectNamingSettings settings, RaceTemplate ownRace, int seed, CharacterGender gender, string autoProfession,
			int biomeID, byte variantIndex, Scene scene, out string name, out string error)
		{
			RaceTemplate race = SceneObjectNameResolver.ResolveRace(settings, ownRace);
			BiomeTemplate biome = null;
			BiomeClimateVariant variant = null;
			if (biomeID != 0)
			{
				WorldSceneSettings.TryGetForScene(scene, out WorldSceneSettings sceneSettings);
				BiomeRegistry.TryGetByID(biomeID, out biome);
				variant = SceneObjectNameResolver.VariantAt(biome, sceneSettings, variantIndex);
			}
			return SceneObjectNameResolver.TryBuild(settings, race, seed, gender, out name, out error, autoProfession, biome, variant);
		}
	}
}
