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
			QualifyRiverFeatures(records);
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
			new[] { "Upper", "High", "Low", "Lower" },
			new[] { "Upper", "High", "Middle", "Low", "Lower" },
		};

		private static readonly string[] Ordinals =
		{
			"First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth", "Tenth", "Eleventh", "Twelfth",
		};

		/// <summary>
		/// Tells apart the falls (and rapids, mouths) one river has in the scene. They share the river's planet-wide name, so a
		/// river stepping down a valley had seven "Dunwy Falls" (Flo Monolith probe, 2026-10-10). Highest first, they become
		/// Upper / Middle / Lower Dunwy Falls, or First… Twelfth past five; the root stays the river's, so it still reads the
		/// same in every scene. Rivers themselves are left alone: two runs of one river ARE the same river.
		/// </summary>
		public static void QualifyRiverFeatures(IList<PointOfInterestRecord> records)
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
				string[] words = group.Count < RiverQualifiers.Length ? RiverQualifiers[group.Count] : null;
				for (int i = 0; i < group.Count; i++)
				{
					string qualifier = words != null ? words[i] : i < Ordinals.Length ? Ordinals[i] : (i + 1).ToString();
					group[i].Name = Qualify(group[i].Name, qualifier);
				}
			}
		}

		/// <summary>"Dunwy Falls" → "Upper Dunwy Falls"; "The Hidden Race" → "The Upper Hidden Race".</summary>
		public static string Qualify(string name, string qualifier)
			=> name.StartsWith("The ", StringComparison.Ordinal) ? $"The {qualifier} {name.Substring(4)}" : $"{qualifier} {name}";

		/// <summary>"A cave in the Pine Forest." from the kind's display name and the biome's.</summary>
		public static string Describe(PointOfInterestRecord record, string biomeDisplayName)
		{
			string kind = PointOfInterestKinds.Info(record.Kind).DisplayName;
			string lower = kind.Length > 0 ? char.ToLowerInvariant(kind[0]) + kind.Substring(1) : kind;
			string article = lower.Length > 0 && "aeiou".IndexOf(lower[0]) >= 0 ? "An" : "A";
			if (string.IsNullOrWhiteSpace(biomeDisplayName))
			{
				return $"{article} {lower}.";
			}
			string where = PointOfInterestKinds.Info(record.Kind).Has(PointOfInterestTraits.Underwater) ? "beneath the" : "in the";
			return $"{article} {lower} {where} {biomeDisplayName}.";
		}

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
