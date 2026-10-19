#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Which biomes a scene holds now and which a cut would give it, as shares of its ground, for the World Atlas.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Predicted</b> reads the planet on a grid over the rectangle, at the globe's own ground, through the same
	/// <see cref="PlanetClimateField.BiomeAt(double, double, out PlanetSurfacePoint)"/> and the same overrides the cut uses.
	/// What it cannot know is what the cut adds: erosion, finer relief, and the lake, river and salt-flat biomes laid
	/// where water runs. A few hundred samples, so it is quick enough to follow a rectangle being dragged.
	/// </para>
	/// <para>
	/// <b>Current</b> counts the cells of the scene's baked biome map, which is exactly what the scene was painted from.
	/// </para>
	/// </remarks>
	public static class SceneBiomePrediction
	{
		/// <summary>Samples along each side of the rectangle.</summary>
		public const int DefaultSamplesPerSide = 20;

		/// <summary>One biome's share of a scene, and the biomes the planet chose that it was painted over.</summary>
		public sealed class Share
		{
			/// <summary>The biome painted; null where none fitted.</summary>
			public BiomeTemplate Biome;
			/// <summary>Share of the scene, 0–1.</summary>
			public float Fraction;
			/// <summary>The planet's own choices an override replaced with <see cref="Biome"/>, largest first. Empty when none was.</summary>
			public readonly List<BiomeTemplate> ReplacedFrom = new List<BiomeTemplate>();
		}

		/// <summary>
		/// The biomes a cut of <paramref name="request"/> would paint, largest share first, with its overrides applied.
		/// Empty when the request stands on no body.
		/// </summary>
		public static List<Share> Predict(SceneGenerationRequest request, int samplesPerSide = DefaultSamplesPerSide)
		{
			var shares = new List<Share>();
			if (request == null || request.Body == null)
			{
				return shares;
			}
			// The editor registers the biomes on a deferred call; the resolver answers nothing without them.
			FishMMO.Shared.NameGeneration.Editor.NamingTemplateEditorLoader.EnsureLoaded();

			SolarSystemProfile system = SolarSystemProfile.Resolve(request.Body);
			PlanetClimateField field = PlanetClimateField.For(system, request.Body);
			SceneBiomeOverrides.Substitution overrides = request.BiomeOverrides != null ? request.BiomeOverrides.Resolve() : null;
			float liquidLevel = 0f;
			SurfaceLiquid liquid = overrides != null ? SurfaceLiquids.For(system, request.Body, out liquidLevel) : SurfaceLiquid.None;

			AtlasFootprint footprint = request.Footprint;
			double radiusKm = request.ResolvedRadiusKm;
			int n = Mathf.Max(2, samplesPerSide);
			var painted = new Dictionary<BiomeTemplate, int>();
			var replaced = new Dictionary<BiomeTemplate, Dictionary<BiomeTemplate, int>>();
			int none = 0;
			for (int z = 0; z < n; z++)
			{
				double north = ((z + 0.5) / n - 0.5) * request.SizeKm.y;
				for (int x = 0; x < n; x++)
				{
					double east = ((x + 0.5) / n - 0.5) * request.SizeKm.x;
					AtlasGeometry.FromUnit(AtlasGeometry.SceneToUnit(footprint, east, north, radiusKm), out double latitude, out double longitude);
					BiomeTemplate natural = field.BiomeAt(latitude, longitude, out PlanetSurfacePoint point);
					BiomeTemplate biome = overrides != null
						? overrides.Apply(natural, liquid != SurfaceLiquid.None && point.AltitudeMetres < liquidLevel)
						: natural;
					if (biome == null)
					{
						none++;
						continue;
					}
					painted.TryGetValue(biome, out int count);
					painted[biome] = count + 1;
					if (natural != biome && natural != null)
					{
						if (!replaced.TryGetValue(biome, out Dictionary<BiomeTemplate, int> sources))
						{
							replaced[biome] = sources = new Dictionary<BiomeTemplate, int>();
						}
						sources.TryGetValue(natural, out int c);
						sources[natural] = c + 1;
					}
				}
			}

			float total = n * n;
			foreach (KeyValuePair<BiomeTemplate, int> pair in painted)
			{
				var share = new Share { Biome = pair.Key, Fraction = pair.Value / total };
				if (replaced.TryGetValue(pair.Key, out Dictionary<BiomeTemplate, int> sources))
				{
					share.ReplacedFrom.AddRange(Ordered(sources));
				}
				shares.Add(share);
			}
			if (none > 0)
			{
				shares.Add(new Share { Biome = null, Fraction = none / total });
			}
			Sort(shares);
			return shares;
		}

		/// <summary>The biomes a baked map holds, largest share first. Empty for no map; an ID no registered biome has counts as none.</summary>
		public static List<Share> Current(SceneBiomeMap map)
		{
			var shares = new List<Share>();
			if (map == null || map.BiomeIDs == null || map.BiomeIDs.Length == 0)
			{
				return shares;
			}
			FishMMO.Shared.NameGeneration.Editor.NamingTemplateEditorLoader.EnsureLoaded();
			var counts = new Dictionary<int, int>();
			foreach (int id in map.BiomeIDs)
			{
				counts.TryGetValue(id, out int c);
				counts[id] = c + 1;
			}
			float total = map.BiomeIDs.Length;
			var byBiome = new Dictionary<BiomeTemplate, Share>();
			Share none = null;
			foreach (KeyValuePair<int, int> pair in counts)
			{
				if (pair.Key != 0 && BiomeRegistry.TryGetByID(pair.Key, out BiomeTemplate biome) && biome != null)
				{
					if (!byBiome.TryGetValue(biome, out Share share))
					{
						byBiome[biome] = share = new Share { Biome = biome };
						shares.Add(share);
					}
					share.Fraction += pair.Value / total;
				}
				else
				{
					if (none == null)
					{
						none = new Share();
						shares.Add(none);
					}
					none.Fraction += pair.Value / total;
				}
			}
			Sort(shares);
			return shares;
		}

		/// <summary>"Taiga 54%, Peat Bog 30% (over Forest), …", or <paramref name="empty"/> for no shares.</summary>
		public static string Describe(List<Share> shares, string empty = "none")
		{
			if (shares == null || shares.Count == 0)
			{
				return empty;
			}
			var parts = new List<string>(shares.Count);
			foreach (Share share in shares)
			{
				parts.Add(Describe(share));
			}
			return string.Join(", ", parts);
		}

		/// <summary>One share as a line: its name, its percentage and what it was painted over.</summary>
		public static string Describe(Share share)
		{
			string name = share.Biome != null ? share.Biome.ResolvedDisplayName : "No biome";
			string percent = share.Fraction >= 0.005f ? $"{Mathf.RoundToInt(share.Fraction * 100f)}%" : "<1%";
			if (share.ReplacedFrom.Count == 0)
			{
				return $"{name} {percent}";
			}
			var from = new List<string>(share.ReplacedFrom.Count);
			foreach (BiomeTemplate biome in share.ReplacedFrom)
			{
				from.Add(biome.ResolvedDisplayName);
			}
			return $"{name} {percent} (over {string.Join(", ", from)})";
		}

		private static void Sort(List<Share> shares)
		{
			shares.Sort((a, b) =>
			{
				int order = b.Fraction.CompareTo(a.Fraction);
				return order != 0 ? order : string.CompareOrdinal(a.Biome != null ? a.Biome.Key : "~", b.Biome != null ? b.Biome.Key : "~");
			});
		}

		private static List<BiomeTemplate> Ordered(Dictionary<BiomeTemplate, int> counts)
		{
			var list = new List<BiomeTemplate>(counts.Keys);
			list.Sort((a, b) =>
			{
				int order = counts[b].CompareTo(counts[a]);
				return order != 0 ? order : string.CompareOrdinal(a.Key, b.Key);
			});
			return list;
		}
	}
}
#endif
