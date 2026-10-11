#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Names one record; <paramref name="index"/> counts the retries (0, 1, 2…) after a clash. Null or empty for none.</summary>
	public delegate string PointOfInterestNamer(PointOfInterestRecord record, int index);

	/// <summary>
	/// Names a scene's records, each unique within the scene, and writes their one-line descriptions.
	/// </summary>
	/// <remarks>
	/// The namer is a seam: the generator passes <see cref="Generator"/> (the name generator, through
	/// <see cref="NameGenerator.Generate(POIRequest)"/>), tests pass a stub, so planning never depends on the naming
	/// templates being loaded.
	/// </remarks>
	public static class PointOfInterestNaming
	{
		/// <summary>How many draws a record gets before it falls back to its kind's name and a number.</summary>
		public const int MaxAttempts = 12;

		/// <summary>
		/// Whether a record is named from its planet river (a river, or its fall, rapids, mouth or delta): drawn once from
		/// the body's seed and the planet river, never retried, and kept even when it repeats a name in the scene, so the
		/// river and its falls share one root ("River Tamar", "Tamar Falls") in every scene they cross. A retry index
		/// would change the name and break that.
		/// </summary>
		public static bool SharesRiverName(PointOfInterestRecord record)
			=> record != null && record.PlanetRiver >= 0 && record.Kind != POIType.Lake && PlaceNameDefaults.IsBiomeFree(record.Kind);

		/// <summary>
		/// Names every record in order, retrying with Index 1, 2… until the name is unused in the scene; a record named
		/// from its planet river (<see cref="SharesRiverName"/>) takes its first draw as it is.
		/// </summary>
		public static void NameAll(IList<PointOfInterestRecord> records, PointOfInterestNamer namer, Func<PointOfInterestRecord, string> describe = null)
		{
			var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var fallbacks = new Dictionary<POIType, int>();
			foreach (PointOfInterestRecord record in records)
			{
				string chosen = null;
				for (int attempt = 0; attempt < MaxAttempts && namer != null; attempt++)
				{
					string name = null;
					try
					{
						name = namer(record, attempt);
					}
					catch (Exception ex)
					{
						Debug.LogWarning($"[Points of interest] Naming a {record.Kind} failed: {ex.Message}");
						break;
					}
					if (string.IsNullOrWhiteSpace(name))
					{
						break;
					}
					if (SharesRiverName(record) || !used.Contains(name))
					{
						chosen = name.Trim();
						break;
					}
				}
				if (chosen == null)
				{
					string display = PointOfInterestKinds.Info(record.Kind).DisplayName;
					fallbacks.TryGetValue(record.Kind, out int n);
					do
					{
						n++;
						chosen = n == 1 ? display : $"{display} {n}";
					}
					while (used.Contains(chosen));
					fallbacks[record.Kind] = n;
				}
				used.Add(chosen);
				record.Name = chosen;
			}
			QualifyRiverFeatures(records, namer, used);
			if (describe != null)
			{
				foreach (PointOfInterestRecord record in records)
				{
					record.Description = describe(record) ?? string.Empty;
				}
			}
		}

		private static readonly string[][] RiverQualifiers =
		{
			null,
			null,
			new[] { "Upper", "Lower" },
			new[] { "Upper", "Middle", "Lower" },
		};

		private static readonly string[] Ordinals =
		{
			"First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth", "Tenth", "Eleventh", "Twelfth",
		};

		/// <summary>
		/// Tells apart the falls (and rapids, mouths) one river has in the scene. They share the river's planet-wide name, so a
		/// river stepping down a valley had seven "Dunwy Falls" (Flo Monolith probe, 2026-10-10). Two or three become Upper /
		/// (Middle /) Lower Dunwy Falls, highest first. Past three, a river's falls are each named for themselves, as real
		/// ones are (Vernal, Nevada, Bridalveil on one river): the highest and the lowest keep the river's name as Upper and
		/// Lower, and the ones between take their own (2026-10-11: "First … Ninth Dunwy Falls" read as a list, not places).
		/// With no namer (tests, templates not loaded) the ones between are numbered. Rivers themselves are left alone: two
		/// runs of one river ARE the same river.
		/// </summary>
		public static void QualifyRiverFeatures(IList<PointOfInterestRecord> records, PointOfInterestNamer namer = null, HashSet<string> used = null)
		{
			var groups = new Dictionary<(POIType, int, string), List<PointOfInterestRecord>>();
			foreach (PointOfInterestRecord record in records)
			{
				if (record == null || record.Kind == POIType.River || !SharesRiverName(record) || string.IsNullOrEmpty(record.Name))
				{
					continue;
				}
				var key = (record.Kind, record.PlanetRiver, record.Name);
				if (!groups.TryGetValue(key, out List<PointOfInterestRecord> list))
				{
					groups[key] = list = new List<PointOfInterestRecord>();
				}
				list.Add(record);
			}
			foreach (List<PointOfInterestRecord> group in groups.Values)
			{
				if (group.Count < 2)
				{
					continue;
				}
				group.Sort((a, b) =>
				{
					int c = b.Position.y.CompareTo(a.Position.y);
					return c != 0 ? c : a.Id.CompareTo(b.Id);
				});
				string root = group[0].Name;
				if (group.Count < RiverQualifiers.Length)
				{
					string[] words = RiverQualifiers[group.Count];
					for (int i = 0; i < group.Count; i++)
					{
						group[i].Name = Qualify(root, words[i]);
					}
					continue;
				}
				group[0].Name = Qualify(root, "Upper");
				group[group.Count - 1].Name = Qualify(root, "Lower");
				for (int i = 1; i < group.Count - 1; i++)
				{
					string own = OwnName(group[i], namer, used);
					group[i].Name = own ?? Qualify(root, i < Ordinals.Length ? Ordinals[i] : (i + 1).ToString());
				}
			}
		}

		/// <summary>A river feature's own name, apart from its river's: drawn as any place is, unique in the scene; null when none comes.</summary>
		private static string OwnName(PointOfInterestRecord record, PointOfInterestNamer namer, HashSet<string> used)
		{
			if (namer == null)
			{
				return null;
			}
			int river = record.PlanetRiver;
			// Named as a place of its own, from the scene and its id, not from the planet river.
			record.PlanetRiver = -1;
			try
			{
				for (int attempt = 0; attempt < MaxAttempts; attempt++)
				{
					string name = namer(record, attempt)?.Trim();
					if (string.IsNullOrEmpty(name))
					{
						return null;
					}
					if (used == null || used.Add(name))
					{
						return name;
					}
				}
				return null;
			}
			finally
			{
				record.PlanetRiver = river;
			}
		}

		/// <summary>
		/// The words a site may be called by, from what its template builds: a timber bridge is a bridge or a span, never a
		/// viaduct, a causeway or a ford; a plain shrine is no chapel; a timber tower no spire (2026-10-11: names did not
		/// match what stood there). Null where the template leaves the kind's own words fitting, or the site has none.
		/// </summary>
		public static string[] TypeWordsFor(PointOfInterestRecord record)
		{
			if (record == null || string.IsNullOrEmpty(record.Template))
			{
				return null;
			}
			// "Bridge - Timber", "Camp - Yurts (Medium-Large)": the style is between the dash and any size.
			string style = record.Template;
			int dash = style.IndexOf(" - ", StringComparison.Ordinal);
			style = dash >= 0 ? style.Substring(dash + 3) : style;
			int paren = style.IndexOf(" (", StringComparison.Ordinal);
			style = (paren >= 0 ? style.Substring(0, paren) : style).Trim();
			return TemplateWords.TryGetValue((record.Kind, style), out string[] words) ? words : null;
		}

		private static readonly Dictionary<(POIType, string), string[]> TemplateWords = new Dictionary<(POIType, string), string[]>
		{
			[(POIType.Bridge, "Timber")] = new[] { "Bridge", "Footbridge", "Span", "Crossing", "Plank Bridge" },
			[(POIType.Shrine, "Shrine")] = new[] { "Shrine", "Wayshrine", "Altar" },
			[(POIType.Shrine, "Statue")] = new[] { "Shrine", "Effigy", "Idol" },
			[(POIType.Tower, "Timber")] = new[] { "Watchtower", "Tower", "Lookout", "Watch" },
			[(POIType.Tower, "Stone")] = new[] { "Tower", "Watchtower", "Turret", "Lookout", "Beacon" },
			[(POIType.Monument, "Obelisk")] = new[] { "Obelisk", "Needle", "Pillar", "Monument" },
			[(POIType.Monument, "Statue")] = new[] { "Statue", "Monument", "Effigy", "Memorial" },
			[(POIType.Statue, "Standing")] = new[] { "Statue", "Effigy", "Figure" },
			[(POIType.Statue, "Broken")] = new[] { "Broken Statue", "Fallen Effigy", "Toppled Statue" },
			[(POIType.Camp, "Lean-tos")] = new[] { "Camp", "Shelter", "Bivouac" },
			[(POIType.Camp, "Hide Tents")] = new[] { "Camp", "Encampment", "Tents" },
			[(POIType.Camp, "Yurts")] = new[] { "Camp", "Encampment", "Yurts" },
			[(POIType.Camp, "Crude")] = new[] { "Camp", "Shelter", "Fire-ring" },
			[(POIType.BanditCamp, "Open")] = new[] { "Camp", "Hideout", "Den" },
			[(POIType.BanditCamp, "Palisade")] = new[] { "Stockade", "Hideout", "Holdfast" },
			[(POIType.Graveyard, "Railed")] = new[] { "Graveyard", "Cemetery", "Burying Ground" },
			[(POIType.Graveyard, "Earthen")] = new[] { "Boneyard", "Gravefield", "Barrow-field" },
			[(POIType.Temple, "Colonnade")] = new[] { "Temple", "Fane", "Sanctuary" },
			[(POIType.Temple, "Court")] = new[] { "Temple", "Sanctuary", "House" },
			[(POIType.Monastery, "Cloister")] = new[] { "Monastery", "Abbey", "Priory", "Cloister" },
			[(POIType.Monastery, "Timber")] = new[] { "Priory", "Friary", "Retreat" },
			[(POIType.Ruins, "Stone")] = new[] { "Ruins", "Remains", "Foundations" },
			[(POIType.Ruins, "Burned")] = new[] { "Ruins", "Ashes", "Burnt Walls" },
			[(POIType.Ruins, "Temple")] = new[] { "Ruined Temple", "Fallen Fane", "Broken Sanctum" },
			[(POIType.RuinedTower, "Tower")] = new[] { "Ruined Tower", "Broken Tower", "Shell" },
			[(POIType.RuinedTower, "Keep")] = new[] { "Ruined Keep", "Broken Keep", "Old Keep" },
			[(POIType.Hermitage, "Cottage")] = new[] { "Hermitage", "Cell", "Cottage" },
			[(POIType.Hermitage, "Stone")] = new[] { "Hermitage", "Cell", "Anchorhold" },
			[(POIType.HuntingLodge, "Lodge")] = new[] { "Hunting Lodge", "Lodge", "Hunt Hall" },
			[(POIType.HuntingLodge, "Hide Lodge")] = new[] { "Hunters' Camp", "Hide Lodge", "Hunting Blind" },
			[(POIType.Waystation, "Inn")] = new[] { "Inn", "Waystation", "Rest" },
			[(POIType.Waystation, "Stone Inn")] = new[] { "Inn", "Waystation", "Hostel" },
			[(POIType.Crypt, "Tomb")] = new[] { "Crypt", "Tomb", "Sepulchre", "Mausoleum" },
			[(POIType.Ossuary, "Tomb")] = new[] { "Ossuary", "Charnel House", "Bone Hall" },
			[(POIType.Mine, "Adit")] = new[] { "Mine", "Diggings", "Adit", "Delving" },
			[(POIType.Quarry, "Cut Stone")] = new[] { "Quarry", "Stone-pit", "Delf" },
			[(POIType.Portal, "Arch")] = new[] { "Portal", "Gate", "Arch", "Waygate" },
			[(POIType.DungeonEntrance, "Door")] = new[] { "Door", "Gate", "Stair", "Doors", "Entrance" },
			[(POIType.Lighthouse, "Stone")] = new[] { "Lighthouse", "Light", "Beacon" },
			[(POIType.FishingCamp, "Shore")] = new[] { "Fishing Camp", "Landing", "Net-huts" },
			[(POIType.FishingCamp, "Huts")] = new[] { "Fishing Camp", "Net-huts", "Smokehouse" },
			[(POIType.LumberCamp, "Timber")] = new[] { "Lumber Camp", "Logging Camp", "Woodyard", "Sawpit" },
			[(POIType.TradingPost, "Market")] = new[] { "Trading Post", "Market", "Post" },
			[(POIType.TradingPost, "Caravanserai")] = new[] { "Caravanserai", "Trading Post", "Khan" },
			[(POIType.StoneCircle, "Trilithons")] = new[] { "Henge", "Stone Circle", "Trilithons" },
			[(POIType.StoneCircle, "Menhirs")] = new[] { "Standing Stones", "Stone Circle", "Stones", "Ring" },
			[(POIType.RitualSite, "Stones")] = new[] { "Ritual Site", "Altar", "Offering Stone", "Rite-ground" },
			[(POIType.RitualSite, "Bonefire")] = new[] { "Ritual Site", "Bonefire", "Blood Ring" },
			[(POIType.WitchHut, "Stilt Hut")] = new[] { "Hut", "Stilt Hut", "Hovel" },
			[(POIType.WitchHut, "Cottage")] = new[] { "Cottage", "Hut", "Croft" },
			[(POIType.AbandonedFarm, "Farmstead")] = new[] { "Farm", "Farmstead", "Steading", "Grange" },
			[(POIType.AbandonedFarm, "Burned")] = new[] { "Burned Farm", "Burnt Steading", "Ashen Farm" },
			[(POIType.MonsterDen, "Bones")] = new[] { "Den", "Lair", "Bone-pile" },
			[(POIType.MonsterDen, "Lair")] = new[] { "Den", "Lair", "Lurk" },
			[(POIType.Nest, "Bones")] = new[] { "Nest", "Brood", "Roost" },
			[(POIType.Oasis, "Camp")] = new[] { "Oasis", "Wells", "Watering" },
		};

		/// <summary>"Dunwy Falls" → "Upper Dunwy Falls"; "The Hidden Race" → "The Upper Hidden Race".</summary>
		public static string Qualify(string name, string qualifier)
			=> name.StartsWith("The ", StringComparison.Ordinal) ? $"The {qualifier} {name.Substring(4)}" : $"{qualifier} {name}";

		/// <summary>Kinds whose display name is a plural that takes no article ("Ruins in the Forest").</summary>
		private static readonly HashSet<POIType> PluralKinds = new HashSet<POIType>
		{
			POIType.Ruins, POIType.Rapids, POIType.FumaroleField, POIType.MangroveMaze,
		};

		/// <summary>
		/// One line about a site: what it is, whose, and where. "An elven village in the Forest."; "A cave of the dryads in
		/// the Forest."; "Ruins in the Tundra."; "A lake." — not "A lake in the Lake", "A rapids" or "A river Mouth".
		/// </summary>
		public static string Describe(PointOfInterestRecord record, string biomeDisplayName)
		{
			PointOfInterestKindInfo info = PointOfInterestKinds.Info(record.Kind);
			string kind = info.DisplayName.ToLowerInvariant();
			string race = record.Race ?? string.Empty;
			bool settled = info.Has(PointOfInterestTraits.Settlement) || record.Kind == POIType.Camp;
			string adjective = race.Length > 0 && settled ? RaceNaming.AdjectiveOf(race) : null;
			string plural = race.Length > 0 && !settled ? RaceNaming.PluralOf(race) : null;

			string phrase = !string.IsNullOrEmpty(adjective) ? $"{adjective.ToLowerInvariant()} {kind}" : kind;
			if (!PluralKinds.Contains(record.Kind))
			{
				phrase = $"{(("aeiou".IndexOf(phrase[0]) >= 0) ? "an" : "a")} {phrase}";
			}
			if (!string.IsNullOrEmpty(plural))
			{
				phrase += $" of the {plural.ToLowerInvariant()}";
			}
			phrase = char.ToUpperInvariant(phrase[0]) + phrase.Substring(1);

			// Where: not when the biome is the thing itself or the water it lies in ("A lake in the Lake").
			bool redundant = string.IsNullOrWhiteSpace(biomeDisplayName)
				|| biomeDisplayName.IndexOf(info.DisplayName, StringComparison.OrdinalIgnoreCase) >= 0
				|| info.DisplayName.IndexOf(biomeDisplayName, StringComparison.OrdinalIgnoreCase) >= 0
				|| info.Group == PointOfInterestGroup.Water && IsWaterBiome(biomeDisplayName);
			if (redundant)
			{
				return phrase + ".";
			}
			string where = info.Has(PointOfInterestTraits.Underwater) ? "beneath the" : "in the";
			return $"{phrase} {where} {biomeDisplayName}.";
		}

		private static bool IsWaterBiome(string biome)
			=> biome.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0 || biome.IndexOf("Lake", StringComparison.OrdinalIgnoreCase) >= 0
				|| biome.IndexOf("Ocean", StringComparison.OrdinalIgnoreCase) >= 0 || biome.IndexOf("River", StringComparison.OrdinalIgnoreCase) >= 0
				|| biome.IndexOf("Sea", StringComparison.OrdinalIgnoreCase) >= 0;

		/// <summary>The seed a record's name is drawn from, kept on it.</summary>
		public static int NameSeed(int sceneSeed, PointOfInterestRecord record)
			=> (int)(PointOfInterestPlanner.Hash(sceneSeed, record.Id, 0x4A4D45, 0) & 0x7FFFFFFFu);

		/// <summary>
		/// The name generator as a namer: a POI request for the record's biome, climate variant, kind and race, the
		/// scene's name as the region and the record's id as the object; for a record named from its planet river
		/// (<see cref="SharesRiverName"/>) the body's terrain seed and the planet river instead, so a river and its falls
		/// have one name in every scene they cross. Null when the naming
		/// templates are not loaded: the caller falls back to plain kind names.
		/// </summary>
		public static PointOfInterestNamer Generator(string sceneName, string bodySeed, int sceneSeed)
		{
			if (!NameGenerator.IsReady)
			{
				return null;
			}
			return (record, index) =>
			{
				bool river = SharesRiverName(record);
				if (river && index > 0)
				{
					return null;
				}
				// Water's names (rivers, falls, lakes) need no biome; everything else is named from its biome's phonology.
				BiomeRegistry.TryGetByID(record.BiomeID, out BiomeTemplate biome);
				if (!PlaceNameDefaults.IsBiomeFree(record.Kind) && (biome == null || biome.Naming == null || !biome.Naming.IsUsable))
				{
					return null;
				}
				record.NameSeed = NameSeed(sceneSeed, record);
				var request = new POIRequest
				{
					BiomeID = biome != null ? record.BiomeID : 0,
					Variant = VariantOf(biome, record.VariantIndex),
					POIType = record.Kind,
					Race = record.Race ?? string.Empty,
					RegionSeed = river ? bodySeed : sceneName,
					ObjectSeed = river ? record.PlanetRiver.ToString() : record.Id.ToString(),
					Index = index > 0 ? index : (int?)null,
					// Called by what is built there, when its template says.
					TypeWords = TypeWordsFor(record),
				};
				return new NameGenerator(record.NameSeed).Generate(request)?.Name;
			};
		}

		/// <summary>The biome's own climate variant a record's index names (index + 1), or null.</summary>
		public static BiomeClimateVariant VariantOf(BiomeTemplate biome, byte index)
		{
			if (biome == null || index == 0 || biome.ClimateVariants == null || index > biome.ClimateVariants.Count)
			{
				return null;
			}
			return biome.ClimateVariants[index - 1];
		}

		/// <summary>The index (+1) of a variant among the biome's own, 0 for none.</summary>
		public static byte VariantIndexOf(BiomeTemplate biome, BiomeClimateVariant variant)
		{
			if (biome == null || variant == null || biome.ClimateVariants == null)
			{
				return 0;
			}
			for (int i = 0; i < biome.ClimateVariants.Count && i < 254; i++)
			{
				if (biome.ClimateVariants[i] == variant)
				{
					return (byte)(i + 1);
				}
			}
			return 0;
		}
	}

	/// <summary>Who a site belongs to: a seeded pick among the races at home in its biome (Jim, 2026-10-10).</summary>
	public static class PointOfInterestRaces
	{
		/// <summary>One race the planner may pick: its naming key, its category, its affinity for the site's biome.</summary>
		public readonly struct Candidate
		{
			public readonly string Key;
			public readonly string Category;
			public readonly float Weight;

			public Candidate(string key, string category, float weight)
			{
				Key = key;
				Category = category;
				Weight = weight;
			}
		}

		/// <summary>
		/// A seeded, weighted pick among the candidates whose category is one of <paramref name="categories"/> (all of
		/// them when none matches or none is asked); empty when there is no candidate.
		/// </summary>
		public static string Pick(IReadOnlyList<Candidate> candidates, IReadOnlyList<string> categories, int seed)
		{
			if (candidates == null || candidates.Count == 0)
			{
				return string.Empty;
			}
			var pool = new List<Candidate>();
			if (categories != null && categories.Count > 0)
			{
				foreach (Candidate candidate in candidates)
				{
					foreach (string category in categories)
					{
						if (string.Equals(candidate.Category, category, StringComparison.OrdinalIgnoreCase))
						{
							pool.Add(candidate);
							break;
						}
					}
				}
			}
			if (pool.Count == 0)
			{
				pool.AddRange(candidates);
			}
			float total = 0f;
			foreach (Candidate candidate in pool)
			{
				total += Mathf.Max(0f, candidate.Weight);
			}
			float roll = PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(seed, 0x5ACE, 0, 0)) * total;
			foreach (Candidate candidate in pool)
			{
				roll -= Mathf.Max(0f, candidate.Weight);
				if (roll < 0f)
				{
					return candidate.Key ?? string.Empty;
				}
			}
			return pool[pool.Count - 1].Key ?? string.Empty;
		}
	}

	/// <summary>
	/// Per-scene unlock indices for sites players unlock by index (waypoints, portals), carried over a re-cut.
	/// </summary>
	public static class PointOfInterestUnlocks
	{
		/// <summary>Jim's default for matching a site to the one it replaces: same kind, within 200 m.</summary>
		public const float MatchMetres = 200f;

		/// <summary>
		/// Which sites take an unlock index; null assigns none (the generator then leaves every index −1). Set by the
		/// gameplay that unlocks by index, e.g. from an <c>[InitializeOnLoad]</c>.
		/// </summary>
		public static Func<PointOfInterestRecord, bool> Unlockable;

		/// <summary>
		/// Gives each unlockable record an index: the index of the nearest previous record of the same kind within
		/// <paramref name="matchMetres"/> (closest pairs first, each previous index used once), else the lowest index no
		/// record holds. Records that are not unlockable get −1.
		/// </summary>
		public static void Assign(IList<PointOfInterestRecord> records, IReadOnlyList<PointOfInterestRecord> previous,
			Func<PointOfInterestRecord, bool> unlockable, float matchMetres = MatchMetres)
		{
			if (records == null)
			{
				return;
			}
			foreach (PointOfInterestRecord record in records)
			{
				record.UnlockIndex = -1;
			}
			if (unlockable == null)
			{
				return;
			}
			var wanting = new List<PointOfInterestRecord>();
			foreach (PointOfInterestRecord record in records)
			{
				if (unlockable(record))
				{
					wanting.Add(record);
				}
			}
			var taken = new HashSet<int>();
			if (previous != null)
			{
				var pairs = new List<(float d2, int fresh, int old)>();
				for (int i = 0; i < wanting.Count; i++)
				{
					for (int j = 0; j < previous.Count; j++)
					{
						PointOfInterestRecord old = previous[j];
						if (old == null || old.UnlockIndex < 0 || old.Kind != wanting[i].Kind)
						{
							continue;
						}
						float dx = old.Position.x - wanting[i].Position.x, dz = old.Position.z - wanting[i].Position.z;
						float d2 = dx * dx + dz * dz;
						if (d2 <= matchMetres * matchMetres)
						{
							pairs.Add((d2, i, j));
						}
					}
				}
				pairs.Sort((a, b) =>
				{
					int c = a.d2.CompareTo(b.d2);
					if (c != 0) return c;
					c = a.fresh.CompareTo(b.fresh);
					return c != 0 ? c : a.old.CompareTo(b.old);
				});
				var usedOld = new HashSet<int>();
				foreach ((float _, int fresh, int old) in pairs)
				{
					int index = previous[old].UnlockIndex;
					if (wanting[fresh].UnlockIndex >= 0 || usedOld.Contains(old) || taken.Contains(index))
					{
						continue;
					}
					wanting[fresh].UnlockIndex = index;
					usedOld.Add(old);
					taken.Add(index);
				}
			}
			int next = 0;
			foreach (PointOfInterestRecord record in wanting)
			{
				if (record.UnlockIndex >= 0)
				{
					continue;
				}
				while (taken.Contains(next))
				{
					next++;
				}
				record.UnlockIndex = next;
				taken.Add(next);
			}
		}
	}
}
#endif
