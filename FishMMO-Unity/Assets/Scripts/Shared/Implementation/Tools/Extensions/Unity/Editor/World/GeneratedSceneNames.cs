#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.NameGeneration.Editor;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What a suggested scene name came from, so the prompt can explain itself.</summary>
	public struct SuggestedSceneName
	{
		/// <summary>The name, or null when none could be produced.</summary>
		public string Name;
		/// <summary>The biome the ground under the rectangle resolved to, for display.</summary>
		public string Biome;
		/// <summary>The climate variant that flavoured it ("frozen"), or null.</summary>
		public string Variant;
		/// <summary>Why there is no name, or null when there is one.</summary>
		public string Problem;

		public bool Usable => !string.IsNullOrEmpty(Name);
	}

	/// <summary>
	/// Names a scene after the ground it is being cut from.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The project already has a name generator that builds place names out of a biome's own
	/// phonology and vocabulary — "Frozen Kelbrin Spring" from a tundra, "The Sunken Mire" from a
	/// peat bog. Cutting a scene out of a globe knows exactly which biome that ground carries,
	/// because the same climate field that shades the planet says so, so the two fit together with
	/// nothing in between: ask the planet what is there, ask the generator what such a place is
	/// called.
	/// </para>
	/// <para>
	/// <b>Unseeded on purpose.</b> A seeded request would answer the same thing every time it was
	/// asked about the same rectangle, and the button exists to be pressed again when the first
	/// suggestion is not liked. The scene's position is not lost by this — it is on the atlas
	/// entry, which is where a position belongs.
	/// </para>
	/// </remarks>
	public static class GeneratedSceneNames
	{
		/// <summary>How many names to draw before giving up on finding an unused one.</summary>
		private const int Attempts = 24;

		/// <summary>
		/// A name for a scene about to be cut at a point on a body.
		/// </summary>
		/// <param name="body">The body being cut. Null gives no name.</param>
		/// <param name="layer">The scene's layer; an underground one is named as a dungeon.</param>
		/// <param name="latitude">Centre of the rectangle, in degrees.</param>
		/// <param name="longitude">Centre of the rectangle, in degrees.</param>
		/// <param name="taken">Names already in use, so a suggestion is never one that is refused.</param>
		public static SuggestedSceneName Suggest(
			WorldBody body, WorldAtlasLayer layer, double latitude, double longitude, IEnumerable<string> taken)
		{
			if (body == null)
			{
				return new SuggestedSceneName { Problem = "No body to take a name from." };
			}

			// In the editor outside play mode nothing has loaded the naming templates, and the
			// biome registry the resolver reads is filled by the same pass.
			NamingTemplateEditorLoader.EnsureLoaded();

			SolarSystemProfile system = SolarSystemProfile.Resolve(body);
			PlanetClimateField field = PlanetClimateField.For(system, body);
			BiomeTemplate biome = field.BiomeAt(latitude, longitude, out PlanetSurfacePoint point);

			if (biome == null)
			{
				return new SuggestedSceneName
				{
					Problem = BiomeRegistry.Count == 0
						? "No BiomeTemplate is registered, so there is nothing to name this after."
						: $"Nothing in the biome catalogue fits this ground ({point.Climate.Temperature:0.00} temperature, {point.Climate.Humidity:0.00} humidity on a {body.Atmosphere} world).",
				};
			}
			if (biome.Naming == null || !biome.Naming.IsUsable)
			{
				return new SuggestedSceneName
				{
					Biome = biome.ResolvedDisplayName,
					Problem = $"'{biome.ResolvedDisplayName}' carries no naming data. Run the biome naming generator, or type a name.",
				};
			}

			BiomeClimateVariant variant = biome.ResolveOwnVariant(point.Climate.Temperature, point.Climate.Humidity);
			bool underground = layer != null && layer.Underground;

			var generator = new NameGenerator();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (taken != null)
			{
				foreach (string name in taken)
				{
					seen.Add(name);
				}
			}

			// What the ground there is, for a region's word: read once, the same every time this spot is asked.
			RegionGround ground = underground ? default : RegionGround.Read(field, body, latitude, longitude);
			BiomeTemplate rootBiome = ground.Biome != null && ground.Biome.Naming != null && ground.Biome.Naming.IsUsable ? ground.Biome : biome;
			uint spot = PointsOfInterestHash(body, latitude, longitude);

			string last = null;
			for (int attempt = 0; attempt < Attempts; attempt++)
			{
				string candidate;
				try
				{
					/* A scene is a region kilometres across, not one landmark: it was named as a point of interest ("Flo
					 * Monolith", "Volax Obelisk" for a valley of forest and rivers, Jim 2026-10-11). Now a root in the
					 * biome's own sounds and a word for the country it is: a weald, downs, fens, fells, a vale, an isle. */
					candidate = underground
						? generator.Generate(new DungeonRequest { Biome = biome.Key, Variant = variant }).Name
						: RegionName(rootBiome, ground, unchecked((int)(spot ^ (uint)(attempt * 0x9E3779B1u))));
				}
				catch (Exception ex)
				{
					return new SuggestedSceneName
					{
						Biome = biome.ResolvedDisplayName,
						Problem = $"The name generator refused: {ex.Message}",
					};
				}

				last = candidate;
				if (!seen.Contains(candidate) && SceneGeneration.NameProblem(candidate, seen) == null)
				{
					return new SuggestedSceneName
					{
						Name = candidate,
						Biome = biome.ResolvedDisplayName,
						Variant = variant != null ? variant.Name : null,
					};
				}
			}

			/* Every draw was taken. Handing back the last one anyway is better than handing back
			 * nothing: the prompt refuses it out loud with the reason, which is far clearer than a
			 * button that does nothing when pressed. */
			return new SuggestedSceneName
			{
				Name = last,
				Biome = biome.ResolvedDisplayName,
				Variant = variant != null ? variant.Name : null,
				Problem = $"Every name drawn for '{biome.ResolvedDisplayName}' is already in use.",
			};
		}

		private static uint PointsOfInterestHash(WorldBody body, double latitude, double longitude)
			=> PointOfInterestPlanner.Mix(body.ResolvedTerrainSeed ^ PointOfInterestPlanner.Mix((uint)Math.Round(latitude * 1000.0) * 0x9E3779B1u ^ (uint)Math.Round(longitude * 1000.0)));

		/// <summary>The kind of country a region is, for the word it is called by.</summary>
		public enum RegionCountry
		{
			Other,
			Forest,
			Jungle,
			Grass,
			Wetland,
			Desert,
			Frozen,
			Volcanic,
			Badlands,
			Hills,
			Mountain,
			Valley,
			Coast,
			Island,
		}

		/// <summary>What a scene's ground is, read from the planet at its centre and a ring round it.</summary>
		public struct RegionGround
		{
			public BiomeTemplate Biome;
			public RegionCountry Country;

			/// <summary>Metres from the centre the ring is read at: about where a scene's middle ground lies.</summary>
			public const double RingKm = 1.5;

			public static RegionGround Read(PlanetClimateField field, WorldBody body, double latitude, double longitude)
			{
				double radiusKm = Math.Max(1.0, AtlasModel.RadiusOf(body));
				double degrees = RingKm / (radiusKm * Math.PI / 180.0);
				var counts = new Dictionary<BiomeTemplate, int>();
				int wet = 0, ringWet = 0, samples = 0;
				float low = float.PositiveInfinity, high = float.NegativeInfinity, ringMean = 0f, centre = 0f;
				int ringLand = 0;
				for (int k = -1; k < 8; k++)
				{
					double lat = latitude, lon = longitude;
					if (k >= 0)
					{
						double a = k * Math.PI / 4.0;
						lat = Math.Max(-89.9, Math.Min(89.9, latitude + degrees * Math.Cos(a)));
						lon = longitude + degrees * Math.Sin(a) / Math.Max(0.05, Math.Cos(latitude * Math.PI / 180.0));
					}
					BiomeTemplate here = field.BiomeAt(lat, lon, out PlanetSurfacePoint point);
					samples++;
					if (point.UnderWater)
					{
						wet++;
						ringWet += k >= 0 ? 1 : 0;
						continue;
					}
					if (here != null)
					{
						counts.TryGetValue(here, out int n);
						counts[here] = n + 1;
					}
					low = Math.Min(low, point.AltitudeMetres);
					high = Math.Max(high, point.AltitudeMetres);
					if (k < 0)
					{
						centre = point.AltitudeMetres;
					}
					else
					{
						ringMean += point.AltitudeMetres;
						ringLand++;
					}
				}
				var ground = new RegionGround();
				int best = 0;
				foreach (KeyValuePair<BiomeTemplate, int> pair in counts)
				{
					if (pair.Value > best || pair.Value == best && ground.Biome != null && string.CompareOrdinal(pair.Key.Key, ground.Biome.Key) < 0)
					{
						best = pair.Value;
						ground.Biome = pair.Key;
					}
				}
				RegionCountry country = CountryOf(ground.Biome);
				float relief = high > low ? high - low : 0f;
				ringMean = ringLand > 0 ? ringMean / ringLand : centre;
				if (ringWet >= 6 && wet < samples)
				{
					country = RegionCountry.Island;
				}
				else if (wet >= 2)
				{
					country = RegionCountry.Coast;
				}
				else if (country != RegionCountry.Frozen && country != RegionCountry.Volcanic && (relief > 700f || low > 2500f))
				{
					country = RegionCountry.Mountain;
				}
				else if (relief > 250f && centre < ringMean - 120f)
				{
					country = RegionCountry.Valley;
				}
				else if (relief > 250f && (country == RegionCountry.Grass || country == RegionCountry.Other))
				{
					country = RegionCountry.Hills;
				}
				ground.Country = country;
				return ground;
			}

			/// <summary>The country a biome is, by its name.</summary>
			public static RegionCountry CountryOf(BiomeTemplate biome)
			{
				if (biome == null)
				{
					return RegionCountry.Other;
				}
				string name = ((biome.Key ?? string.Empty) + " " + (biome.ResolvedDisplayName ?? string.Empty)).ToLowerInvariant();
				bool Has(params string[] words)
				{
					foreach (string word in words)
					{
						if (name.Contains(word))
						{
							return true;
						}
					}
					return false;
				}
				if (Has("jungle", "rainforest")) return RegionCountry.Jungle;
				if (Has("swamp", "marsh", "bog", "fen", "wetland", "mire", "mangrove")) return RegionCountry.Wetland;
				if (Has("volcan", "lava", "ash", "molten", "cinder")) return RegionCountry.Volcanic;
				if (Has("tundra", "ice", "glacier", "polar", "frozen", "snow", "frost")) return RegionCountry.Frozen;
				if (Has("desert", "dune", "sand", "erg", "salt")) return RegionCountry.Desert;
				if (Has("karst", "badland", "canyon", "mesa")) return RegionCountry.Badlands;
				if (Has("alpine", "mountain", "highland")) return RegionCountry.Mountain;
				if (Has("forest", "wood", "grove", "taiga", "boreal")) return RegionCountry.Forest;
				if (Has("savanna", "steppe", "grass", "prairie", "meadow", "farm", "plain", "heath", "moor")) return RegionCountry.Grass;
				return RegionCountry.Other;
			}
		}

		/// <summary>The words a country's regions are called by.</summary>
		public static string[] RegionWords(RegionCountry country)
		{
			switch (country)
			{
				case RegionCountry.Forest: return new[] { "Weald", "Wood", "Woods", "Holt", "Forest", "Greenwood" };
				case RegionCountry.Jungle: return new[] { "Jungle", "Tangle", "Deepwood", "Wilds" };
				case RegionCountry.Grass: return new[] { "Downs", "Plains", "Fields", "Meads", "Lea", "Grasslands" };
				case RegionCountry.Wetland: return new[] { "Fens", "Marsh", "Mire", "Mosses", "Wetlands", "Carrs" };
				case RegionCountry.Desert: return new[] { "Sands", "Wastes", "Dunes", "Desert", "Barrens" };
				case RegionCountry.Frozen: return new[] { "Barrens", "Wastes", "Frostlands", "Icefields", "Wilds" };
				case RegionCountry.Volcanic: return new[] { "Ashlands", "Cinderfields", "Burnlands", "Scorch" };
				case RegionCountry.Badlands: return new[] { "Badlands", "Breaks", "Canyons", "Scarps" };
				case RegionCountry.Hills: return new[] { "Hills", "Wolds", "Uplands", "Fells", "Rise" };
				case RegionCountry.Mountain: return new[] { "Highlands", "Peaks", "Heights", "Crags", "Mountains", "Fells" };
				case RegionCountry.Valley: return new[] { "Vale", "Dale", "Glen", "Valley", "Strath" };
				case RegionCountry.Coast: return new[] { "Coast", "Shore", "Strand", "Cape", "Shorelands" };
				case RegionCountry.Island: return new[] { "Isle", "Island", "Holm" };
				default: return new[] { "Reach", "Expanse", "Lands", "Basin", "Plain" };
			}
		}

		/// <summary>
		/// A region's name: a root in the biome's sounds and its country's word, as "Aradune Weald", "The Dunwy Downs",
		/// "Vale of Thane", "Isle of Holm". Empty when the biome has no sounds to name from.
		/// </summary>
		public static string RegionName(BiomeTemplate biome, RegionGround ground, int seed)
		{
			string root = NameGenerator.PlaceRoot(biome, seed);
			if (string.IsNullOrEmpty(root))
			{
				return string.Empty;
			}
			string[] words = RegionWords(ground.Country);
			uint h = PointOfInterestPlanner.Mix(unchecked((uint)seed * 0x85EBCA6Bu) ^ 0x52454731u);
			string word = words[(int)(h % (uint)words.Length)];
			float roll = PointOfInterestPlanner.Unit(PointOfInterestPlanner.Mix(h ^ 0xA5A5A5A5u));
			bool plural = word.EndsWith("s", StringComparison.Ordinal) && !word.EndsWith("ss", StringComparison.Ordinal);
			if (ground.Country == RegionCountry.Island || ground.Country == RegionCountry.Valley)
			{
				// "Isle of Holm", "Vale of Thane" read best; "Holm Isle", "Thane Dale" too.
				return roll < 0.5f ? $"{word} of {root}" : $"{root} {word}";
			}
			if (plural && roll < 0.3f)
			{
				return $"The {root} {word}";
			}
			return $"{root} {word}";
		}
	}
}
#endif
