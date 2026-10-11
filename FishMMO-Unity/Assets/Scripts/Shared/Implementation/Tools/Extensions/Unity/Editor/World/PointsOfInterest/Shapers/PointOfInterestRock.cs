#if UNITY_EDITOR
using System;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The stone a terrain shaper builds a site in: the same rule as the cliffs, river boulders and fall ledges beside it
	/// (<see cref="CliffRocks.RockFor"/>: the planet geology's rock where the site's biome accepts it, else the biome's
	/// own), so a cave mouth in a sandstone canyon is sandstone.
	/// </summary>
	public static class PointOfInterestRock
	{
		[ThreadStatic] private static SceneGenerationRequest lastRequest;
		[ThreadStatic] private static Func<float, float, float, string> lastGeology;

		/// <summary>The rock (a <see cref="RockTypes"/> name, or <see cref="CliffRocks.Ice"/>) at a site's position.</summary>
		public static string At(SceneGenerationRequest request, PointOfInterestRecord site, Vector3 position)
		{
			BiomeArtSpec.Entry spec = null;
			if (site != null && site.BiomeID != 0 && BiomeRegistry.TryGetByID(site.BiomeID, out BiomeTemplate biome) && biome != null)
			{
				spec = BiomeArtSpec.For(biome.name);
			}
			Func<float, float, float, string> geology = Geology(request);
			return CliffRocks.RockFor(spec, geology?.Invoke(position.x, position.y, position.z));
		}

		/// <summary>The planet's geology under a scene (cached for the request being built), or null with no planet.</summary>
		private static Func<float, float, float, string> Geology(SceneGenerationRequest request)
		{
			if (request == null || request.Body == null)
			{
				return null;
			}
			if (!ReferenceEquals(request, lastRequest) || lastGeology == null)
			{
				lastRequest = request;
				lastGeology = SceneGenerator.GeologyRockTypes(request, SolarSystemProfile.Resolve(request.Body));
			}
			return lastGeology;
		}

		/// <summary>A rock's generated material (<see cref="CliffRocks.MaterialName"/>), or null when the art is not generated.</summary>
		public static Material MaterialOf(string rock)
		{
			string name = CliffRocks.MaterialName(rock);
			return name != null ? AssetDatabase.LoadAssetAtPath<Material>(ProceduralArtCatalogue.MaterialPath(name)) : null;
		}
	}
}
#endif
