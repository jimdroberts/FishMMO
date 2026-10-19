#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A scene's biome overrides (<see cref="WorldAtlasScene.BiomeOverrides"/>) as a cut carries them: asset GUIDs,
	/// resolved again each time a biome field is built.
	/// </summary>
	/// <remarks>
	/// Plain data because a cut refreshes the asset database and opens scenes in Single mode, either of which can leave a
	/// template reference held from before fake-null (the same reason the generator keeps its body and layer as paths).
	/// </remarks>
	public sealed class SceneBiomeOverrides
	{
		private readonly List<(string From, string To)> rules;

		private SceneBiomeOverrides(List<(string From, string To)> rules)
		{
			this.rules = rules;
		}

		/// <summary>How many rules do anything.</summary>
		public int Count => rules.Count;

		/// <summary>The rules that do anything, or null when there are none: an entry without a To is skipped.</summary>
		public static SceneBiomeOverrides Of(IReadOnlyList<BiomeOverride> entries)
		{
			if (entries == null)
			{
				return null;
			}
			var rules = new List<(string From, string To)>();
			for (int i = 0; i < entries.Count; i++)
			{
				string to = GuidOf(entries[i].To);
				if (string.IsNullOrEmpty(to))
				{
					continue;
				}
				rules.Add((GuidOf(entries[i].From), to));
			}
			return rules.Count > 0 ? new SceneBiomeOverrides(rules) : null;
		}

		/// <summary>The overrides an atlas entry asks for, or null.</summary>
		public static SceneBiomeOverrides Of(WorldAtlasScene entry)
		{
			return entry != null ? Of(entry.BiomeOverrides) : null;
		}

		/// <summary>The rules as an atlas entry stores them, loaded fresh; a template that no longer exists is dropped.</summary>
		public List<BiomeOverride> ToEntries()
		{
			var entries = new List<BiomeOverride>(rules.Count);
			foreach ((string from, string to) in rules)
			{
				BiomeTemplate target = Load(to);
				if (target == null)
				{
					continue;
				}
				entries.Add(new BiomeOverride { From = Load(from), To = target });
			}
			return entries;
		}

		/// <summary>The rules as templates, for one pass over a scene. Rules whose templates are gone are skipped, said once in the log.</summary>
		public Substitution Resolve()
		{
			var byKey = new Dictionary<string, BiomeTemplate>();
			BiomeTemplate any = null;
			foreach ((string from, string to) in rules)
			{
				BiomeTemplate target = Registered(Load(to));
				if (target == null)
				{
					Debug.LogWarning($"[Biome overrides] A rule's biome ({AssetDatabase.GUIDToAssetPath(to)}) no longer exists; the rule is skipped.");
					continue;
				}
				if (string.IsNullOrEmpty(from))
				{
					any ??= target;
					continue;
				}
				BiomeTemplate source = Load(from);
				if (source == null)
				{
					Debug.LogWarning($"[Biome overrides] A rule's biome ({AssetDatabase.GUIDToAssetPath(from)}) no longer exists; the rule is skipped.");
					continue;
				}
				// The first rule for a biome wins, as the list reads top to bottom.
				if (!byKey.ContainsKey(source.Key))
				{
					byKey.Add(source.Key, target);
				}
			}
			return new Substitution(byKey, any);
		}

		/// <summary>The rules resolved for one pass: which biome to paint for the one the planet chose.</summary>
		public sealed class Substitution
		{
			private readonly Dictionary<string, BiomeTemplate> byKey;
			private readonly BiomeTemplate any;

			internal Substitution(Dictionary<string, BiomeTemplate> byKey, BiomeTemplate any)
			{
				this.byKey = byKey;
				this.any = any;
			}

			/// <summary>
			/// The biome to paint where the planet chose <paramref name="chosen"/>: a rule naming it, else the catch-all on land the
			/// climate chose (never under the sea, and never a biome laid for water), else the planet's own choice.
			/// </summary>
			/// <param name="chosen">The planet's choice; null where none fitted.</param>
			/// <param name="underWater">True where the ground is under the body's sea or lava.</param>
			public BiomeTemplate Apply(BiomeTemplate chosen, bool underWater)
			{
				if (chosen != null && byKey.TryGetValue(chosen.Key, out BiomeTemplate named))
				{
					return named;
				}
				if (any != null && !underWater && (chosen == null || chosen.WaterRole == BiomeWaterRole.None))
				{
					return any;
				}
				return chosen;
			}
		}

		private static string GuidOf(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return null;
			}
			string path = AssetDatabase.GetAssetPath(biome);
			return string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
		}

		private static BiomeTemplate Load(string guid)
		{
			if (string.IsNullOrEmpty(guid))
			{
				return null;
			}
			string path = AssetDatabase.GUIDToAssetPath(guid);
			return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<BiomeTemplate>(path);
		}

		/// <summary>The registry's instance of a template, so the field's biomes compare by reference with the planet's choices.</summary>
		private static BiomeTemplate Registered(BiomeTemplate biome)
		{
			return biome != null && BiomeRegistry.TryGet(biome.Key, out BiomeTemplate registered) && registered != null ? registered : biome;
		}
	}
}
#endif
