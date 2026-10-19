using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// A biome's LOCAL art: per layer slot, the licensed or hand-made terrain layer or textures that
	/// replace the committed ones on this machine.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The governing rule: references may point FROM <c>Assets/LOCAL</c> TO committed assets,
	/// never the reverse.</b> Real art lives in <c>Assets/LOCAL</c>, which is gitignored, and a
	/// <see cref="BiomeTemplate"/> is committed — so a LOCAL texture can never be assigned on the
	/// template, or every other clone would load a template pointing at a file it does not have. The
	/// type is committed; its instances live only under <see cref="Folder"/>, each pointing at the
	/// committed biome it dresses.
	/// </para>
	/// <para>
	/// <b>Two readers, two rules.</b> Nothing at runtime looks at one.
	/// </para>
	/// <list type="bullet">
	/// <item><b>Textures, for every scene.</b> The terrain array baker resolves each palette layer
	/// LOCAL-first and writes the result into the arrays, which are gitignored build output, so a
	/// committed scene shows LOCAL ground art on this machine without referencing any of it. A clone
	/// without the LOCAL folder bakes the committed art from the same scene.</item>
	/// <item><b>References, for scenes under Assets/LOCAL only.</b> A slot's whole
	/// <see cref="SlotOverride.TerrainLayer"/> (as the terrain's own layer) and a spawn rule's
	/// <see cref="RuleOverride.Prefabs"/> are written into a scene's terrain data when it is painted —
	/// but only when the scene and its terrain data live under Assets/LOCAL
	/// (<c>LocalArtScope</c> decides, in one place). Every other scene is painted with the committed
	/// defaults, so a committed file can never be made to point at LOCAL art. A designer who wants
	/// real trees and grass keeps a private copy of a scene under <c>Assets/LOCAL/SceneCopies</c>.</item>
	/// </list>
	/// <para>
	/// <b>Slots are keyed like the scene palette keys them</b>: <c>main</c>, <c>detail/&lt;i&gt;</c>,
	/// <c>cliff/&lt;i&gt;</c>, <c>riverbed</c>, <c>lakebed</c>, plus <c>road</c> and <c>path</c>,
	/// so a scene's record of where a layer came from finds the same slot here.
	/// </para>
	/// <para>
	/// <b>A ground layer's own ways (2026-10-16)</b> are keyed by the ground's slot and the way's: <c>main/road</c>,
	/// <c>detail/2/path</c>, <c>detail/2/path/stone</c> (<see cref="WaySlot"/>; a main or detail layer's
	/// <see cref="TerrainTextureLayer.ways"/>). They hold a terrain layer, not a texture layer, so
	/// <see cref="CommittedLayer"/> has none for them and <see cref="SlotsOf"/> leaves them out; a sidecar entry under
	/// such a key dresses that way's art as any other slot's does, but only where the template names a way there.
	/// </para>
	/// </remarks>
	public sealed class BiomeLocalArt : ScriptableObject
	{
		/// <summary>Where sidecars live. Gitignored, with the rest of Assets/LOCAL.</summary>
		public const string Folder = "Assets/LOCAL/Biomes/Overrides";

		/// <summary>Whole-layer overrides by name: <c>&lt;TerrainLayersFolder&gt;/&lt;committed layer name&gt;.terrainlayer</c>.</summary>
		public const string TerrainLayersFolder = "Assets/LOCAL/Biomes/TerrainLayers";

		/// <summary>Per-texture overrides by name: <c>&lt;TexturesFolder&gt;/&lt;committed texture file name&gt;.&lt;ext&gt;</c>.</summary>
		public const string TexturesFolder = "Assets/LOCAL/Biomes/Textures";

		/// <summary>
		/// Prefab overrides by name, for LOCAL scenes only: <c>&lt;PrefabsFolder&gt;/&lt;committed prefab name&gt;.prefab</c>
		/// stands in for that prefab wherever a spawn rule or the ice placer would use it.
		/// </summary>
		public const string PrefabsFolder = "Assets/LOCAL/Biomes/Prefabs";

		/// <summary>
		/// Material overrides by name, for LOCAL scenes only: <c>&lt;MaterialsFolder&gt;/&lt;committed material name&gt;.mat</c>
		/// (for instance <c>Cliff_Granite.mat</c>) stands in for that generated material on placed pieces.
		/// </summary>
		public const string MaterialsFolder = "Assets/LOCAL/Biomes/Materials";

		/// <summary>The extensions a name-matched texture override may have, in the order they are tried.</summary>
		public static readonly string[] TextureExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".exr" };

		// Slot keys. Identical to SceneTerrainPalette's (pinned by a test), declared again here because
		// the palette is editor-only and this type is not.
		public const string SlotMain = "main";
		public const string SlotDetail = "detail/";
		public const string SlotCliff = "cliff/";
		public const string SlotLakebed = "lakebed";
		public const string SlotRiverbed = "riverbed";
		public const string SlotRoad = "road";
		public const string SlotPath = "path";
		public const string SlotCobbles = "path/stone";
		public const string SlotBarSand = "sediment/sand";
		public const string SlotBarGravel = "sediment/gravel";
		/// <summary>
		/// The prefix of a climate variant's ground override (<see cref="VariantSlot"/>: <c>variant/frozen/main</c>,
		/// <c>variant/scorched/detail/1</c>), 2026-10-18.
		/// </summary>
		public const string SlotVariant = "variant/";
		/// <summary>The prefix of a biome's extra bed layers by depth (<see cref="BedSlot"/>: <c>bed/0</c> … <c>bed/2</c>), 2026-10-18.</summary>
		public const string SlotBed = "bed/";

		/// <summary>The slot key of a biome's extra bed layer <paramref name="index"/> (<see cref="BiomeTemplate.BedTextureLayers"/>).</summary>
		public static string BedSlot(int index) => SlotBed + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

		/// <summary>The index of a bed layer's slot key (<see cref="BedSlot"/>), or −1 for every other key.</summary>
		public static int BedIndexOf(string slot)
		{
			if (slot == null || !slot.StartsWith(SlotBed, StringComparison.Ordinal))
			{
				return -1;
			}
			string digits = slot.Substring(SlotBed.Length);
			return int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index)
				&& index.ToString(System.Globalization.CultureInfo.InvariantCulture) == digits ? index : -1;
		}

		/// <summary>The three ways a ground layer may surface with its own ground, as the biome's own way slots name them.</summary>
		public static readonly string[] WaySlots = { SlotPath, SlotRoad, SlotCobbles };

		/// <summary>One slot's override. A whole terrain layer wins over the single textures.</summary>
		[Serializable]
		public sealed class SlotOverride
		{
			[Tooltip("The slot key: main, detail/<i>, cliff/<i>, riverbed, lakebed, road, path, path/stone (cobbles), sediment/sand or sediment/gravel; or a ground layer's own way: main/road, detail/<i>/path, detail/<i>/path/stone.")]
			public string Slot;
			[Tooltip("Replaces the whole layer: its textures, tiling and remaps.")]
			public TerrainLayer TerrainLayer;
			[Tooltip("Replaces the albedo only (used when no whole layer is set).")]
			public Texture2D Albedo;
			[Tooltip("Replaces the normal map only (used when no whole layer is set).")]
			public Texture2D Normal;
			[Tooltip("Replaces the mask map only (used when no whole layer is set).")]
			public Texture2D Mask;

			/// <summary>True when nothing is overridden.</summary>
			public bool IsEmpty => TerrainLayer == null && Albedo == null && Normal == null && Mask == null;

			/// <summary>True when two overrides would bake the same art.</summary>
			public bool SameArtAs(SlotOverride other)
			{
				if (other == null)
				{
					return IsEmpty;
				}
				return TerrainLayer == other.TerrainLayer && Albedo == other.Albedo && Normal == other.Normal && Mask == other.Mask;
			}
		}

		/// <summary>
		/// One spawn rule's override: the prefabs a LOCAL scene scatters in place of the rule's own.
		/// Everything else about the rule — density, bands, scale, sink — stays the committed rule's.
		/// </summary>
		/// <remarks>
		/// <b>Keyed by the rule's <see cref="PrefabSpawnRule.StableGuid"/></b>, which survives renames
		/// and reordering; the slot and rule name are kept beside it as the fallback key, so an override
		/// still finds its rule if the rule is re-authored with a fresh GUID under the same name.
		/// </remarks>
		[Serializable]
		public sealed class RuleOverride
		{
			[Tooltip("The rule's StableGuid: the primary key.")]
			public string RuleGuid;
			[Tooltip("The slot whose texture layer carries the rule (main, detail/<i>, ...): part of the fallback key.")]
			public string Slot;
			[Tooltip("The rule's name when the override was made: the rest of the fallback key.")]
			public string RuleName;
			[Tooltip("Scattered instead of the rule's own prefabs, in scenes under Assets/LOCAL only. One is drawn per spawn, as with the rule's own list.")]
			public GameObject[] Prefabs = new GameObject[0];

			/// <summary>True when no prefab is set.</summary>
			public bool IsEmpty
			{
				get
				{
					if (Prefabs == null)
					{
						return true;
					}
					for (int i = 0; i < Prefabs.Length; i++)
					{
						if (Prefabs[i] != null)
						{
							return false;
						}
					}
					return true;
				}
			}
		}

		[Tooltip("The committed biome this dresses.")]
		[SerializeField] private BiomeTemplate biome;

		[SerializeField] private List<SlotOverride> slots = new List<SlotOverride>();

		[Tooltip("Spawn-rule prefab overrides, used only when a scene under Assets/LOCAL is painted.")]
		[SerializeField] private List<RuleOverride> rules = new List<RuleOverride>();

		/// <summary>The committed biome this dresses.</summary>
		public BiomeTemplate Biome
		{
			get => biome;
			set => biome = value;
		}

		/// <summary>Every slot with an entry, empty ones included until they are removed.</summary>
		public IReadOnlyList<SlotOverride> Slots => slots;

		/// <summary>The override for a slot, or null. An entry with nothing in it counts as none.</summary>
		public SlotOverride Find(string slot)
		{
			if (string.IsNullOrEmpty(slot))
			{
				return null;
			}
			for (int i = 0; i < slots.Count; i++)
			{
				SlotOverride entry = slots[i];
				if (entry != null && entry.Slot == slot)
				{
					return entry.IsEmpty ? null : entry;
				}
			}
			return null;
		}

		/// <summary>The entry for a slot, made if missing. For the inspector's writes.</summary>
		public SlotOverride GetOrAdd(string slot)
		{
			for (int i = 0; i < slots.Count; i++)
			{
				if (slots[i] != null && slots[i].Slot == slot)
				{
					return slots[i];
				}
			}
			var entry = new SlotOverride { Slot = slot };
			slots.Add(entry);
			return entry;
		}

		/// <summary>Removes a slot's entry. Returns true if there was one.</summary>
		public bool Remove(string slot)
		{
			return slots.RemoveAll(entry => entry == null || entry.Slot == slot) > 0;
		}

		/// <summary>Every rule override, empty ones included until they are removed.</summary>
		public IReadOnlyList<RuleOverride> Rules => rules;

		/// <summary>
		/// The override for a rule, or null: by GUID first, else by slot and rule name. An entry with
		/// no prefab counts as none.
		/// </summary>
		public RuleOverride FindRule(string slot, PrefabSpawnRule rule)
		{
			return rule == null ? null : FindRule(slot, rule.StableGuid, rule.ruleName);
		}

		/// <summary><see cref="FindRule(string, PrefabSpawnRule)"/> by the key's parts.</summary>
		public RuleOverride FindRule(string slot, string ruleGuid, string ruleName)
		{
			int i = IndexOfRule(slot, ruleGuid, ruleName);
			RuleOverride entry = i >= 0 ? rules[i] : null;
			return entry == null || entry.IsEmpty ? null : entry;
		}

		/// <summary>The entry for a rule, made if missing, with its keys refreshed. For the inspector's writes.</summary>
		public RuleOverride GetOrAddRule(string slot, PrefabSpawnRule rule)
		{
			if (rule == null)
			{
				return null;
			}
			int i = IndexOfRule(slot, rule.StableGuid, rule.ruleName);
			RuleOverride entry = i >= 0 ? rules[i] : null;
			if (entry == null)
			{
				entry = new RuleOverride();
				rules.Add(entry);
			}
			entry.RuleGuid = rule.StableGuid;
			entry.Slot = slot;
			entry.RuleName = rule.ruleName;
			return entry;
		}

		/// <summary>Removes a rule's entry (and any null entries). Returns true if there was one.</summary>
		public bool RemoveRule(string slot, PrefabSpawnRule rule)
		{
			int removed = rules.RemoveAll(entry => entry == null);
			int i = rule != null ? IndexOfRule(slot, rule.StableGuid, rule.ruleName) : -1;
			if (i >= 0)
			{
				rules.RemoveAt(i);
				return true;
			}
			return removed > 0;
		}

		private int IndexOfRule(string slot, string ruleGuid, string ruleName)
		{
			if (!string.IsNullOrEmpty(ruleGuid))
			{
				for (int i = 0; i < rules.Count; i++)
				{
					if (rules[i] != null && string.Equals(rules[i].RuleGuid, ruleGuid, StringComparison.Ordinal))
					{
						return i;
					}
				}
			}
			if (string.IsNullOrEmpty(slot) || string.IsNullOrEmpty(ruleName))
			{
				return -1;
			}
			for (int i = 0; i < rules.Count; i++)
			{
				if (rules[i] != null && rules[i].Slot == slot && rules[i].RuleName == ruleName)
				{
					return i;
				}
			}
			return -1;
		}

		/// <summary>The slot key of a detail layer.</summary>
		public static string DetailSlot(int index) => SlotDetail + index;

		/// <summary>The slot key of a cliff layer.</summary>
		public static string CliffSlot(int index) => SlotCliff + index;

		/// <summary>
		/// The slot key of a ground layer's own way (<see cref="TerrainTextureLayer.ways"/>): the ground's slot (<c>main</c>,
		/// <c>detail/&lt;i&gt;</c>), a slash, and the way's (<see cref="SlotPath"/>, <see cref="SlotRoad"/>, <see cref="SlotCobbles"/>).
		/// </summary>
		public static string WaySlot(string groundSlot, string waySlot) => groundSlot + "/" + waySlot;

		/// <summary>
		/// Splits a ground layer's way key (<see cref="WaySlot"/>) into the ground's slot and the way's. False for every other
		/// key, the biome's own way slots (<c>road</c>, <c>path/stone</c>) included.
		/// </summary>
		public static bool TryParseWaySlot(string slot, out string groundSlot, out string waySlot)
		{
			groundSlot = null;
			waySlot = null;
			if (string.IsNullOrEmpty(slot))
			{
				return false;
			}
			// The longest way first: "detail/2/path/stone" is the cobbles of detail/2, not a path of "detail/2/path".
			foreach (string way in new[] { SlotCobbles, SlotRoad, SlotPath })
			{
				if (slot.Length <= way.Length + 1 || !slot.EndsWith(way, StringComparison.Ordinal) || slot[slot.Length - way.Length - 1] != '/')
				{
					continue;
				}
				string ground = slot.Substring(0, slot.Length - way.Length - 1);
				if (IsGroundSlot(ground))
				{
					groundSlot = ground;
					waySlot = way;
					return true;
				}
			}
			return false;
		}

		/// <summary>The slot key of ground slot <paramref name="ground"/>: 0 the main layer, k detail k − 1 (a variant override's order).</summary>
		public static string GroundSlot(int ground) => ground <= 0 ? SlotMain : DetailSlot(ground - 1);

		/// <summary>The ground index of a main or detail slot key (0 main, k detail k − 1), or −1 for any other key.</summary>
		public static int GroundIndexOf(string slot)
		{
			if (slot == SlotMain)
			{
				return 0;
			}
			return IsGroundSlot(slot) ? int.Parse(slot.Substring(SlotDetail.Length)) + 1 : -1;
		}

		/// <summary>
		/// How a variant is named in a slot key: its key (<see cref="BiomeClimateVariant.Key"/>, letters only), or its index
		/// in the template's list when it has no name (digits only, so the two never collide).
		/// </summary>
		public static string VariantId(BiomeClimateVariant variant, int index)
		{
			string key = variant != null ? variant.Key : string.Empty;
			return key.Length > 0 ? key : index.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// The slot key of a climate variant's override for one ground slot (<see cref="BiomeClimateVariant.TextureLayerOverrides"/>,
		/// 0 the main layer): <c>variant/&lt;id&gt;/main</c>, <c>variant/&lt;id&gt;/detail/&lt;i&gt;</c>. Keyed by the variant's name
		/// rather than its place in the list, so reordering the variants keeps a LOCAL sidecar's and the marks' keys.
		/// </summary>
		public static string VariantSlot(string variantId, int ground) => SlotVariant + variantId + "/" + GroundSlot(ground);

		/// <summary>Splits a variant override's key (<see cref="VariantSlot"/>) into the variant's id and the ground index. False for every other key.</summary>
		public static bool TryParseVariantSlot(string slot, out string variantId, out int ground)
		{
			variantId = null;
			ground = -1;
			if (string.IsNullOrEmpty(slot) || !slot.StartsWith(SlotVariant, StringComparison.Ordinal))
			{
				return false;
			}
			int slash = slot.IndexOf('/', SlotVariant.Length);
			if (slash <= SlotVariant.Length)
			{
				return false;
			}
			int index = GroundIndexOf(slot.Substring(slash + 1));
			if (index < 0)
			{
				return false;
			}
			variantId = slot.Substring(SlotVariant.Length, slash - SlotVariant.Length);
			ground = index;
			return true;
		}

		/// <summary>The template's own variant a slot key's id names (<see cref="VariantId"/>), with its index; null when none does.</summary>
		public static BiomeClimateVariant VariantNamed(BiomeTemplate biome, string variantId, out int index)
		{
			index = -1;
			if (biome?.ClimateVariants == null || string.IsNullOrEmpty(variantId))
			{
				return null;
			}
			for (int i = 0; i < biome.ClimateVariants.Count; i++)
			{
				if (biome.ClimateVariants[i] != null && VariantId(biome.ClimateVariants[i], i) == variantId)
				{
					index = i;
					return biome.ClimateVariants[i];
				}
			}
			return null;
		}

		/// <summary>True for the slots that may carry ways of their own: the main layer and the detail layers.</summary>
		public static bool IsGroundSlot(string slot)
		{
			return slot == SlotMain
				|| slot != null && slot.StartsWith(SlotDetail, StringComparison.Ordinal) && int.TryParse(slot.Substring(SlotDetail.Length), out int d) && d >= 0
				&& d.ToString() == slot.Substring(SlotDetail.Length);
		}

		/// <summary>The ground a ground layer's ways give one way (<see cref="WaySlots"/>), or null for the biome's own.</summary>
		public static TerrainLayer WayGround(GroundWays ways, string waySlot)
		{
			switch (waySlot)
			{
				case SlotPath: return ways.path;
				case SlotRoad: return ways.road;
				case SlotCobbles: return ways.cobbles;
			}
			return null;
		}

		/// <summary>Sets one way's ground on a ground layer's ways (null: the biome's own again).</summary>
		public static void SetWayGround(ref GroundWays ways, string waySlot, TerrainLayer ground)
		{
			switch (waySlot)
			{
				case SlotPath: ways.path = ground; break;
				case SlotRoad: ways.road = ground; break;
				case SlotCobbles: ways.cobbles = ground; break;
				default: throw new ArgumentException($"'{waySlot}' is not a way slot", nameof(waySlot));
			}
		}

		/// <summary>Every slot a biome has, in the order its inspector lists them.</summary>
		public static List<string> SlotsOf(BiomeTemplate biome)
		{
			var keys = new List<string> { SlotMain };
			if (biome == null)
			{
				return keys;
			}
			int details = biome.DetailTextureLayers != null ? biome.DetailTextureLayers.Count : 0;
			for (int i = 0; i < details; i++)
			{
				keys.Add(DetailSlot(i));
			}
			int cliffs = biome.CliffTextureLayers != null ? biome.CliffTextureLayers.Count : 0;
			for (int i = 0; i < cliffs; i++)
			{
				keys.Add(CliffSlot(i));
			}
			keys.Add(SlotRiverbed);
			keys.Add(SlotLakebed);
			keys.Add(SlotRoad);
			keys.Add(SlotPath);
			keys.Add(SlotCobbles);
			keys.Add(SlotBarSand);
			keys.Add(SlotBarGravel);
			// Each climate variant's ground overrides that are listed (2026-10-18): texture layers of the biome like any other.
			if (biome.ClimateVariants != null)
			{
				for (int v = 0; v < biome.ClimateVariants.Count; v++)
				{
					BiomeClimateVariant variant = biome.ClimateVariants[v];
					int overrides = variant?.TextureLayerOverrides != null ? variant.TextureLayerOverrides.Count : 0;
					for (int g = 0; g < overrides; g++)
					{
						if (variant.TextureLayerOverrides[g] != null)
						{
							keys.Add(VariantSlot(VariantId(variant, v), g));
						}
					}
				}
			}
			// The extra bed layers by depth (2026-10-18), none until a round adds them.
			int beds = biome.BedTextureLayers != null ? biome.BedTextureLayers.Count : 0;
			for (int i = 0; i < beds; i++)
			{
				keys.Add(BedSlot(i));
			}
			return keys;
		}

		/// <summary>The biome's committed texture layer for a slot, or null.</summary>
		public static TerrainTextureLayer CommittedLayer(BiomeTemplate biome, string slot)
		{
			if (biome == null || string.IsNullOrEmpty(slot))
			{
				return null;
			}
			switch (slot)
			{
				case SlotMain: return biome.MainTextureLayer;
				case SlotRiverbed: return biome.RiverbedTextureLayer;
				case SlotLakebed: return biome.LakebedTextureLayer;
				case SlotRoad: return biome.RoadTextureLayer;
				case SlotPath: return biome.SmallPathTextureLayer;
				case SlotCobbles: return biome.CobbleTextureLayer;
				case SlotBarSand: return biome.BarSandTextureLayer;
				case SlotBarGravel: return biome.BarGravelTextureLayer;
			}
			if (slot.StartsWith(SlotDetail, StringComparison.Ordinal) && int.TryParse(slot.Substring(SlotDetail.Length), out int d))
			{
				return biome.DetailTextureLayers != null && d >= 0 && d < biome.DetailTextureLayers.Count ? biome.DetailTextureLayers[d] : null;
			}
			if (slot.StartsWith(SlotCliff, StringComparison.Ordinal) && int.TryParse(slot.Substring(SlotCliff.Length), out int c))
			{
				return biome.CliffTextureLayers != null && c >= 0 && c < biome.CliffTextureLayers.Count ? biome.CliffTextureLayers[c] : null;
			}
			if (TryParseVariantSlot(slot, out string variantId, out int ground))
			{
				return VariantNamed(biome, variantId, out _)?.TextureOverrideFor(ground);
			}
			int bed = BedIndexOf(slot);
			if (bed >= 0)
			{
				return biome.BedTextureLayers != null && bed < biome.BedTextureLayers.Count ? biome.BedTextureLayers[bed] : null;
			}
			return null;
		}
	}
}
