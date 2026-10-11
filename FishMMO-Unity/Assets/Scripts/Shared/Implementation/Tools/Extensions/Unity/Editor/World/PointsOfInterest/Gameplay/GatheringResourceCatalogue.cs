#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The resource families a generated gathering node can belong to.</summary>
	public enum GatheringFamily : byte
	{
		Ore = 0,
		Crystal = 1,
		Herb = 2,
		Fungus = 3,
		Wood = 4,
	}

	/// <summary>
	/// One generated resource: the node players gather from, the item it drops, and what it looks like.
	/// </summary>
	public sealed class GatheringResource
	{
		/// <summary>The node's name: the <see cref="GatheringNodeTemplate"/> asset (shown over the node) and, with " Node", its prefab.</summary>
		public string Node;
		/// <summary>The dropped item's name: a <see cref="CraftingMaterialTemplate"/> asset.</summary>
		public string Item;
		public GatheringFamily Family;
		/// <summary>1 common … 4 rare.</summary>
		public int Tier;
		/// <summary>The generated biome prefab (Assets/Prefabs/Shared/Biomes/Generated/Prefabs) whose LOD0 mesh the node shows.</summary>
		public string Visual;
		/// <summary>Multiplied into the visual's material colour so the node reads apart from scenery made of the same mesh.</summary>
		public Color Tint;
		/// <summary>The node's largest extent in metres; 0 keeps the visual's own size (trees).</summary>
		public float Size;
		/// <summary>The collider's share of the visual's footprint: 1 for a rock, a trunk's share for a tree.</summary>
		public float Footprint = 1.0f;
		public int MaxUses = 3;
		public float GatherSeconds = 2.0f;
		public int MinAmount = 1;
		public int MaxAmount = 2;
		/// <summary>Biome asset names the resource belongs in; empty for anywhere its family fits.</summary>
		public string[] Biomes = Array.Empty<string>();
		public string Description;

		/// <summary>Sell price of one item: a tier-scaled placeholder.</summary>
		public int Price => Tier <= 1 ? 4 : Tier == 2 ? 12 : Tier == 3 ? 35 : 120;

		/// <summary>Whether <paramref name="biomeName"/> is one of <see cref="Biomes"/>.</summary>
		public bool Suits(string biomeName)
		{
			if (string.IsNullOrEmpty(biomeName))
			{
				return false;
			}
			foreach (string biome in Biomes)
			{
				if (string.Equals(biome, biomeName, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}
	}

	/// <summary>
	/// The generated resources (Jim, 2026-10-10: generate GatheringNodeTemplates and node prefabs, none existed; placeholder
	/// items). <see cref="GatheringContentGenerator"/> turns each row into assets; <see cref="GatheringFeature"/> picks one per
	/// site with <see cref="Pick"/>.
	/// </summary>
	/// <remarks>
	/// <para><b>Ore by lithology and hardness.</b> Copper sits in soft sedimentary country (sandstone), iron anywhere hard
	/// (basalt), silver and gold in the crystalline and quartz-veined rock of mountains and dry ranges, and the rare
	/// starsilver (the mithril-like ore) in high gneiss and impact craters. Crystals split by origin: volcanic glass,
	/// cave-grown quartz, and an alien crystal for the bodies that are not Earth. Herbs and fungi follow the biome; the
	/// ancient trees are the wood.</para>
	/// <para>Names are stable: they are asset names and therefore addresses and template IDs. Add rows; do not rename.</para>
	/// </remarks>
	public static class GatheringResourceCatalogue
	{
		/// <summary>Where the visuals come from.</summary>
		public const string VisualFolder = "Assets/Prefabs/Shared/Biomes/Generated/Prefabs";

		private static readonly string[] Volcanic = { "Volcanic", "Lava Tube", "Volcanic Cave", "Molten Surface", "Geyser Basin", "Sulphur Flats", "Volcanic Temple", "Crater" };
		private static readonly string[] Caves = { "Cave", "Ice Cave", "Swamp Cave", "Volcanic Cave", "Lava Tube", "Karst" };
		private static readonly string[] Alien = { "Impact Basin", "Radiation Plain", "Regolith Plain", "Tholin Plain", "Nitrogen Ice Field", "Methane Lake", "Cryovolcanic Plain", "Ice Geyser Field", "Tidal Fracture", "Dust Sea", "Runaway Greenhouse Plain", "Sulphuric Cloud Deck", "Rille" };
		private static readonly string[] Temperate = { "Grassland", "Plains", "Farmland", "Woodland", "Forest", "Valley", "Hills", "Steppe", "Alpine Meadow", "Forest Ruins" };
		private static readonly string[] Cold = { "Alpine", "Alpine Meadow", "Tundra", "Taiga", "Snow", "Glacier", "Mountain Slope", "Permanent Ice" };
		private static readonly string[] Dry = { "Desert", "High Desert", "Savanna", "Scrubland", "Oasis", "Badlands", "Steppe", "Salt Flat" };
		private static readonly string[] Wet = { "Swamp", "Wetlands", "Peat Bog", "Mangrove", "Estuary", "Swamp Ruins", "Swamp Temple" };
		private static readonly string[] Tropical = { "Jungle", "Jungle Ruins", "Jungle Temple", "Bamboo Forest", "Mangrove" };

		private static readonly List<GatheringResource> all = new List<GatheringResource>
		{
			// ── Ore ──────────────────────────────────────────────────
			new GatheringResource { Node = "Copper Lode", Item = "Copper Ore", Family = GatheringFamily.Ore, Tier = 1, Visual = "Boulder_Sandstone_Round",
				Tint = new Color(0.86f, 0.52f, 0.32f), Size = 1.6f, MaxUses = 4, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Hills", "Badlands", "Desert", "High Desert", "Scrubland", "Rocky Terrain", "Valley", "Plains", "Steppe" },
				Description = "Green-stained copper ore from soft sedimentary rock." },
			new GatheringResource { Node = "Iron Vein", Item = "Iron Ore", Family = GatheringFamily.Ore, Tier = 1, Visual = "Boulder_Basalt_Round",
				Tint = new Color(0.62f, 0.34f, 0.26f), Size = 1.7f, MaxUses = 4, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Hills", "Mountain Slope", "Rocky Terrain", "Volcanic", "Scree", "Badlands", "Tundra", "Taiga", "Forest", "Valley", "Rocky Coast" },
				Description = "Rust-red iron ore, found wherever the rock is hard." },
			new GatheringResource { Node = "Silver Vein", Item = "Silver Ore", Family = GatheringFamily.Ore, Tier = 2, Visual = "Formation_Granite_Corestone_0",
				Tint = new Color(0.82f, 0.87f, 0.94f), Size = 1.8f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Alpine", "Mountain Slope", "Hills", "Karst", "Tundra", "Scree", "Glacier" },
				Description = "Bright silver ore from the granite of the high country." },
			new GatheringResource { Node = "Gold Vein", Item = "Gold Ore", Family = GatheringFamily.Ore, Tier = 3, Visual = "Formation_Quartzite_Block_0",
				Tint = new Color(1.0f, 0.82f, 0.34f), Size = 1.7f, MaxUses = 3, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Desert", "High Desert", "Badlands", "River", "Valley", "Mountain Slope", "Savanna" },
				Description = "Gold in white quartz, where old rivers and dry ranges cut deep." },
			new GatheringResource { Node = "Starsilver Vein", Item = "Starsilver Ore", Family = GatheringFamily.Ore, Tier = 4, Visual = "Formation_Gneiss_Boulder_0",
				Tint = new Color(0.58f, 0.86f, 1.0f), Size = 1.9f, MaxUses = 2, GatherSeconds = 4.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Alpine", "Mountain Slope", "Glacier", "Impact Basin", "Crater" },
				Description = "A pale, impossibly light ore. Rare beyond telling." },

			// ── Crystal ──────────────────────────────────────────────
			new GatheringResource { Node = "Emberglass Cluster", Item = "Emberglass Shard", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Formation_Obsidian_Shard_0",
				Tint = new Color(1.0f, 0.42f, 0.2f), Size = 1.4f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = Volcanic, Description = "Volcanic glass that never quite cools." },
			new GatheringResource { Node = "Lumen Quartz Cluster", Item = "Lumen Quartz", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.86f, 0.82f, 1.0f), Size = 1.4f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = Caves, Description = "Cave quartz that holds a faint light." },
			new GatheringResource { Node = "Voidstone Cluster", Item = "Voidstone", Family = GatheringFamily.Crystal, Tier = 3, Visual = "Formation_WaterIce_Crystal_0",
				Tint = new Color(0.62f, 0.32f, 1.0f), Size = 1.5f, MaxUses = 2, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = Alien, Description = "A crystal from no world anyone knows." },

			// ── Herb ─────────────────────────────────────────────────
			new GatheringResource { Node = "Silverleaf Patch", Item = "Silverleaf", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_BroadHerb",
				Tint = new Color(0.78f, 0.92f, 0.78f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = Temperate, Description = "A common healing leaf with a silver underside." },
			new GatheringResource { Node = "Frostbloom Patch", Item = "Frostbloom", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_FlowersAlpine",
				Tint = new Color(0.82f, 0.9f, 1.0f), Size = 0.7f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = Cold, Description = "A flower that blooms only in the cold." },
			new GatheringResource { Node = "Sunpetal Patch", Item = "Sunpetal", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_FlowersDesert",
				Tint = new Color(1.0f, 0.86f, 0.52f), Size = 0.7f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = Dry, Description = "Hardy petals that store the desert's heat." },
			new GatheringResource { Node = "Mirewort Patch", Item = "Mirewort", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_PitcherPlant",
				Tint = new Color(0.62f, 0.82f, 0.52f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = Wet, Description = "A bog plant prized by alchemists." },
			new GatheringResource { Node = "Bloodroot Patch", Item = "Bloodroot", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_FlowersWarm",
				Tint = new Color(1.0f, 0.55f, 0.55f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = Tropical, Description = "A red root from the deep jungle." },

			// ── Fungus ───────────────────────────────────────────────
			new GatheringResource { Node = "Glowcap Cluster", Item = "Glowcap", Family = GatheringFamily.Fungus, Tier = 2, Visual = "Detail_MushroomCave",
				Tint = new Color(0.62f, 1.0f, 0.9f), Size = 0.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = Caves, Description = "A cave mushroom that glows in the dark." },
			new GatheringResource { Node = "Morel Cluster", Item = "Forest Morel", Family = GatheringFamily.Fungus, Tier = 1, Visual = "Detail_MushroomForest",
				Tint = new Color(0.88f, 0.78f, 0.62f), Size = 0.5f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Forest", "Woodland", "Taiga", "Forest Ruins", "Bamboo Forest", "Valley" },
				Description = "An honest woodland mushroom." },
			new GatheringResource { Node = "Bog Puffball Cluster", Item = "Bog Puffball", Family = GatheringFamily.Fungus, Tier = 1, Visual = "Detail_MushroomSwamp",
				Tint = new Color(0.88f, 0.88f, 0.72f), Size = 0.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = Wet, Description = "Puffs a cloud of spores when picked." },

			// ── Wood ─────────────────────────────────────────────────
			new GatheringResource { Node = "Ancient Oak", Item = "Ancient Heartwood", Family = GatheringFamily.Wood, Tier = 3, Visual = "Tree_Oak",
				Tint = new Color(1.0f, 0.95f, 0.86f), Size = 0.0f, Footprint = 0.18f, MaxUses = 5, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Forest", "Woodland", "Forest Ruins", "Valley", "Hills", "Grassland", "Plains" },
				Description = "Heartwood from an oak older than any kingdom." },
			new GatheringResource { Node = "Elder Ironbark", Item = "Ironbark Heartwood", Family = GatheringFamily.Wood, Tier = 3, Visual = "Tree_Baobab",
				Tint = new Color(0.92f, 0.86f, 0.8f), Size = 0.0f, Footprint = 0.3f, MaxUses = 5, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Savanna", "Jungle", "Scrubland", "Steppe", "Oasis" },
				Description = "Wood so dense it sinks." },
			new GatheringResource { Node = "Frostpine Elder", Item = "Frostpine Heartwood", Family = GatheringFamily.Wood, Tier = 3, Visual = "Tree_Spruce",
				Tint = new Color(0.86f, 0.95f, 1.0f), Size = 0.0f, Footprint = 0.15f, MaxUses = 5, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Taiga", "Tundra", "Alpine", "Snow", "Mountain Slope" },
				Description = "Pale, resinous wood from the oldest pines." },
		};

		/// <summary>Every resource, in authoring order.</summary>
		public static IReadOnlyList<GatheringResource> All => all;

		/// <summary>The resource with this node name, or null.</summary>
		public static GatheringResource Find(string node)
		{
			foreach (GatheringResource resource in all)
			{
				if (string.Equals(resource.Node, node, StringComparison.OrdinalIgnoreCase))
				{
					return resource;
				}
			}
			return null;
		}

		/// <summary>
		/// The families a point-of-interest kind gathers. Empty for a kind that has no nodes of its own.
		/// </summary>
		public static GatheringFamily[] FamiliesFor(POIType kind)
		{
			switch (kind)
			{
				case POIType.OreVein:
				case POIType.Mine:
				case POIType.Quarry:
					return new[] { GatheringFamily.Ore };
				case POIType.CrystalFormation:
				case POIType.ObsidianField:
				case POIType.FallenStar:
					return new[] { GatheringFamily.Crystal };
				case POIType.HerbGrove:
				case POIType.WitchHut:
				case POIType.FeyRing:
					return new[] { GatheringFamily.Herb, GatheringFamily.Fungus };
				case POIType.AncientTree:
				case POIType.LumberCamp:
					return new[] { GatheringFamily.Wood };
				case POIType.Cave:
				case POIType.Grotto:
					return new[] { GatheringFamily.Ore, GatheringFamily.Crystal, GatheringFamily.Fungus };
				default:
					return Array.Empty<GatheringFamily>();
			}
		}

		/// <summary>
		/// Draws one resource of a family for a site, weighted towards those that suit its biome.
		/// </summary>
		/// <remarks>
		/// A resource that names the biome weighs 1; one that does not weighs 0.05, so a site still gets its family's node
		/// in a biome no row names (an alien world's mine). Rarer tiers weigh less: tier 4 a quarter, tier 3 a half.
		/// Deterministic in <paramref name="random"/>; rows are walked in authoring order.
		/// </remarks>
		/// <returns>The resource, or null when the family has none.</returns>
		public static GatheringResource Pick(GatheringFamily family, string biomeName, DeterministicRNG random)
		{
			float total = 0.0f;
			foreach (GatheringResource resource in all)
			{
				if (resource.Family == family)
				{
					total += Weight(resource, biomeName);
				}
			}
			if (total <= 0.0f)
			{
				return null;
			}
			float roll = (random != null ? random.NextFloat() : 0.0f) * total;
			GatheringResource last = null;
			foreach (GatheringResource resource in all)
			{
				if (resource.Family != family)
				{
					continue;
				}
				last = resource;
				roll -= Weight(resource, biomeName);
				if (roll < 0.0f)
				{
					return resource;
				}
			}
			return last;
		}

		/// <summary>A resource's draw weight in a biome (see <see cref="Pick"/>).</summary>
		public static float Weight(GatheringResource resource, string biomeName)
		{
			if (resource == null)
			{
				return 0.0f;
			}
			float fit = resource.Biomes.Length == 0 || resource.Suits(biomeName) ? 1.0f : 0.05f;
			float rarity = resource.Tier >= 4 ? 0.25f : resource.Tier == 3 ? 0.5f : 1.0f;
			return fit * rarity;
		}
	}
}
#endif
