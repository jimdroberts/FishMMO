#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
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
		/// <summary>What grows or lies on the sea floor (and a lake's): gathered round sunken sites.</summary>
		Marine = 5,
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
		/// <summary>
		/// Biome asset names the resource belongs in; empty for every biome. Since 2026-10-16 the biome art authoring copies
		/// this onto the templates (each named biome's Gathering Resources lists the resource) and the sites read the
		/// templates, so a change here reaches a scene after the next Author biome art.
		/// </summary>
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
	/// The generated resources (2026-10-10: generate GatheringNodeTemplates and node prefabs, none existed; placeholder
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
		private static readonly string[] Alien = { "Impact Basin", "Radiation Plain", "Regolith Plain", "Tholin Plain", "Nitrogen Ice Field", "Methane Lake", "Cryovolcanic Plain", "Ice Geyser Field", "Tidal Fracture", "Dust Sea", "Runaway Greenhouse Plain", "Sulphuric Cloud Deck", "Rille", "Subsurface Ocean Vent" };
		// The strongholds and palaces stand in temperate country; their gardens and moats grow its herbs.
		private static readonly string[] Temperate = { "Grassland", "Plains", "Farmland", "Woodland", "Forest", "Valley", "Hills", "Steppe", "Alpine Meadow", "Forest Ruins", "Castle", "Fortress", "Marble Palace" };
		private static readonly string[] Cold = { "Alpine", "Alpine Meadow", "Tundra", "Taiga", "Snow", "Glacier", "Mountain Slope", "Permanent Ice", "Ice Sheet", "Ice Shelf", "Ice Palace", "Ice Ruin" };
		private static readonly string[] Dry = { "Desert", "High Desert", "Savanna", "Scrubland", "Oasis", "Badlands", "Steppe", "Salt Flat", "Wasteland" };
		private static readonly string[] Wet = { "Swamp", "Wetlands", "Peat Bog", "Mangrove", "Estuary", "Swamp Ruins", "Swamp Temple" };
		private static readonly string[] Tropical = { "Jungle", "Jungle Ruins", "Jungle Temple", "Bamboo Forest", "Mangrove" };

		private static readonly List<GatheringResource> all = new List<GatheringResource>
		{
			// ── Ore ──────────────────────────────────────────────────
			new GatheringResource { Node = "Copper Lode", Item = "Copper Ore", Family = GatheringFamily.Ore, Tier = 1, Visual = "Boulder_Sandstone_Round",
				Tint = new Color(0.86f, 0.52f, 0.32f), Size = 1.6f, MaxUses = 4, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Hills", "Badlands", "Desert", "High Desert", "Scrubland", "Rocky Terrain", "Valley", "Plains", "Steppe", "Wasteland" },
				Description = "Green-stained copper ore from soft sedimentary rock." },
			new GatheringResource { Node = "Iron Vein", Item = "Iron Ore", Family = GatheringFamily.Ore, Tier = 1, Visual = "Boulder_Basalt_Round",
				Tint = new Color(0.62f, 0.34f, 0.26f), Size = 1.7f, MaxUses = 4, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Hills", "Mountain Slope", "Rocky Terrain", "Volcanic", "Scree", "Badlands", "Tundra", "Taiga", "Forest", "Valley", "Rocky Coast",
					"Beach", "Wasteland", "Castle", "Fortress" },
				Description = "Rust-red iron ore, found wherever the rock is hard — and as black sand on the beach." },
			new GatheringResource { Node = "Silver Vein", Item = "Silver Ore", Family = GatheringFamily.Ore, Tier = 2, Visual = "Formation_Granite_Corestone_0",
				Tint = new Color(0.82f, 0.87f, 0.94f), Size = 1.8f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Alpine", "Mountain Slope", "Hills", "Karst", "Tundra", "Scree", "Glacier", "Ice Sheet", "Ice Palace", "Ice Ruin" },
				Description = "Bright silver ore from the granite of the high country." },
			new GatheringResource { Node = "Gold Vein", Item = "Gold Ore", Family = GatheringFamily.Ore, Tier = 3, Visual = "Formation_Quartzite_Block_0",
				Tint = new Color(1.0f, 0.82f, 0.34f), Size = 1.7f, MaxUses = 3, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Desert", "High Desert", "Badlands", "River", "Valley", "Mountain Slope", "Savanna", "Marble Palace" },
				Description = "Gold in white quartz, where old rivers and dry ranges cut deep." },
			new GatheringResource { Node = "Starsilver Vein", Item = "Starsilver Ore", Family = GatheringFamily.Ore, Tier = 4, Visual = "Formation_Gneiss_Boulder_0",
				Tint = new Color(0.58f, 0.86f, 1.0f), Size = 1.9f, MaxUses = 2, GatherSeconds = 4.5f, MinAmount = 1, MaxAmount = 1,
				// Fallen stars lie where nothing buries them: the ice sheets keep every meteorite that lands on them.
				Biomes = new[] { "Alpine", "Mountain Slope", "Glacier", "Impact Basin", "Crater", "Ice Sheet", "Ice Shelf" },
				Description = "A pale, impossibly light ore. Rare beyond telling." },

			// ── Crystal ──────────────────────────────────────────────
			new GatheringResource { Node = "Emberglass Cluster", Item = "Emberglass Shard", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Formation_Obsidian_Shard_0",
				Tint = new Color(1.0f, 0.42f, 0.2f), Size = 1.4f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = Volcanic, Description = "Volcanic glass that never quite cools." },
			new GatheringResource { Node = "Lumen Quartz Cluster", Item = "Lumen Quartz", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.86f, 0.82f, 1.0f), Size = 1.4f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Cave", "Ice Cave", "Swamp Cave", "Volcanic Cave", "Lava Tube", "Karst", "Ice Palace", "Ice Ruin" },
				Description = "Cave quartz that holds a faint light." },
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
			new GatheringResource { Node = "Samphire Patch", Item = "Samphire", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_ShrubSamphire",
				Tint = new Color(0.72f, 0.9f, 0.6f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Beach", "Estuary", "Salt Flat", "Rocky Coast", "Mangrove" },
				Description = "Salty sea-asparagus from the tideline." },

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

			// ── Marine ───────────────────────────────────────────────
			// Gathered round the sunken sites (FamiliesFor): the sea floor and a lake's bed had no nodes at all (audit 2026-10-11).
			new GatheringResource { Node = "Pearl Oyster Bed", Item = "Pearl", Family = GatheringFamily.Marine, Tier = 2, Visual = "Detail_MusselBedOyster",
				Tint = new Color(0.92f, 0.9f, 0.86f), Size = 1.2f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 1,
				// Freshwater pearl mussels in the lakes and the clean rivers.
				Biomes = new[] { "Coastal Water", "Coral Reef", "Ocean", "Lake", "River" },
				Description = "An oyster bed; one shell in a hundred holds a pearl worth the dive." },
			new GatheringResource { Node = "Kelp Holdfast", Item = "Kelp Frond", Family = GatheringFamily.Marine, Tier = 1, Visual = "Detail_Kelp",
				Tint = new Color(0.78f, 0.86f, 0.55f), Size = 1.8f, MaxUses = 4, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Coastal Water", "Ocean", "Seamount", "Underwater Canyon" },
				Description = "Tough fronds from a kelp forest's floor." },
			new GatheringResource { Node = "Living Coral", Item = "Coral Fragment", Family = GatheringFamily.Marine, Tier = 2, Visual = "Detail_BrainCoral",
				Tint = new Color(1.0f, 0.72f, 0.66f), Size = 1.2f, MaxUses = 3, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Coral Reef", "Coastal Water" },
				Description = "A branch of reef coral, still bright." },
			new GatheringResource { Node = "Manganese Nodules", Item = "Manganese Nodule", Family = GatheringFamily.Marine, Tier = 3, Visual = "Boulder_Nodule_Round",
				Tint = new Color(0.55f, 0.5f, 0.46f), Size = 1.4f, MaxUses = 3, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Abyssal Plain", "Deep Ocean", "Abyss" },
				Description = "Metal-rich lumps grown a millimetre a million years on the abyssal floor." },
			new GatheringResource { Node = "Glass Sponge", Item = "Glass Spicule", Family = GatheringFamily.Marine, Tier = 3, Visual = "Detail_GlassSponge",
				Tint = new Color(0.86f, 0.94f, 1.0f), Size = 1.3f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Abyss", "Deep Ocean", "Seamount", "Underwater Canyon" },
				Description = "Silica lace from a sponge older than the kingdoms above it." },
			new GatheringResource { Node = "Vent Clam Bed", Item = "Vent Pearl", Family = GatheringFamily.Marine, Tier = 4, Visual = "Detail_MusselBedVentClam",
				Tint = new Color(1.0f, 0.86f, 0.7f), Size = 1.2f, MaxUses = 2, GatherSeconds = 4.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Subsurface Ocean Vent", "Abyss" },
				Description = "A pearl grown in the heat of a black smoker." },
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
		/// The families a point-of-interest kind gathers, its own first (a herb grove's herbs before its fungi): most of a
		/// site's nodes are of the first (<see cref="GatheringFeature"/>). Empty for a kind that has no nodes of its own.
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
				case POIType.LumberMill:
					return new[] { GatheringFamily.Wood };
				case POIType.Cave:
				case POIType.Grotto:
					return new[] { GatheringFamily.Ore, GatheringFamily.Crystal, GatheringFamily.Fungus };
				// What grows on a wreck and round a drowned wall: the sea floor's own harvest.
				case POIType.SunkenShip:
				case POIType.SunkenRuins:
				case POIType.SunkenCity:
				case POIType.SunkenShrine:
				case POIType.SeaMonsterDen:
				case POIType.SunkenMonument:
				case POIType.SunkenStatue:
					return new[] { GatheringFamily.Marine };
				case POIType.SeaCrystalFormation:
					return new[] { GatheringFamily.Crystal, GatheringFamily.Marine };
				default:
					return Array.Empty<GatheringFamily>();
			}
		}

		/// <summary>
		/// Draws one resource of a family for a site, from those its biome offers: the biome template's Gathering Resources
		/// (node names, <see cref="BiomeTemplate.GatheringResources"/>, read through BiomeTemplateArt.Gathering).
		/// </summary>
		/// <remarks>
		/// <para>Only the family's resources the biome offers are drawn when it offers any; every one of the family when it
		/// offers none, so a site still gets its family's node in a biome that offers none of it (an alien world's mine). Rarer
		/// tiers weigh less: tier 4 a quarter, tier 3 a half. Deterministic in <paramref name="random"/>; rows are walked in
		/// authoring order.</para>
		/// <para>Off-biome rows used to stay in the draw at a twentieth of the weight. With five other herbs beside the one a
		/// forest grows, that drew a stranger at one site in five: Flo Monolith's forest herb grove Bri Wortgarth grew bog
		/// Mirewort (2026-10-12).</para>
		/// <para>Until 2026-10-16 the biome was given by its name, matched against each row's <see cref="GatheringResource.Biomes"/>
		/// (<see cref="Pick(GatheringFamily, string, DeterministicRNG)"/>, kept for what the rows say themselves); the authoring
		/// now writes those rows' answer onto every template, and the draw is the same for the same offer.</para>
		/// </remarks>
		/// <returns>The resource, or null when the family has none.</returns>
		public static GatheringResource Pick(GatheringFamily family, IReadOnlyCollection<string> offered, DeterministicRNG random)
		{
			return Pick(family, resource => Offers(offered, resource), random);
		}

		/// <summary>
		/// Draws one resource of a family for a biome by its name, from the rows that name it (<see cref="GrowsIn"/>): the
		/// catalogue's own answer, which the biome art authoring copies onto each template (BiomeArtSpec.Entry.GatheringResources).
		/// A site draws from its template's offer instead (<see cref="Pick(GatheringFamily, IReadOnlyCollection{string}, DeterministicRNG)"/>).
		/// </summary>
		public static GatheringResource Pick(GatheringFamily family, string biomeName, DeterministicRNG random)
		{
			return Pick(family, resource => GrowsIn(resource, biomeName), random);
		}

		/// <summary>Whether a set of offered resources (node names) holds a resource; none is offered by a null set.</summary>
		public static bool Offers(IReadOnlyCollection<string> offered, GatheringResource resource)
		{
			if (offered == null || resource == null)
			{
				return false;
			}
			foreach (string node in offered)
			{
				if (string.Equals(node, resource.Node, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}

		private static GatheringResource Pick(GatheringFamily family, Func<GatheringResource, bool> grows, DeterministicRNG random)
		{
			bool grown = false;
			foreach (GatheringResource resource in all)
			{
				grown |= resource.Family == family && grows(resource);
			}
			float total = 0.0f;
			foreach (GatheringResource resource in all)
			{
				if (resource.Family == family)
				{
					total += Weight(resource, grows(resource), grown);
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
				float weight = Weight(resource, grows(resource), grown);
				if (weight <= 0.0f)
				{
					continue;
				}
				last = resource;
				roll -= weight;
				if (roll < 0.0f)
				{
					return resource;
				}
			}
			return last;
		}

		/// <summary>Whether a resource grows in a biome: one of its <see cref="GatheringResource.Biomes"/>, or anywhere when it names none.</summary>
		public static bool GrowsIn(GatheringResource resource, string biomeName)
		{
			return resource != null && (resource.Biomes.Length == 0 || resource.Suits(biomeName));
		}

		/// <summary>Whether any resource of a family grows in a biome.</summary>
		public static bool AnyGrowsIn(GatheringFamily family, string biomeName)
		{
			foreach (GatheringResource resource in all)
			{
				if (resource.Family == family && GrowsIn(resource, biomeName))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>A resource's draw weight in a biome (see <see cref="Pick"/>): 0 when it does not grow there but another of its family does.</summary>
		public static float Weight(GatheringResource resource, string biomeName)
		{
			return resource == null ? 0.0f : Weight(resource, GrowsIn(resource, biomeName), AnyGrowsIn(resource.Family, biomeName));
		}

		private static float Weight(GatheringResource resource, bool grows, bool familyGrows)
		{
			if (resource == null || familyGrows && !grows)
			{
				return 0.0f;
			}
			return resource.Tier >= 4 ? 0.25f : resource.Tier == 3 ? 0.5f : 1.0f;
		}
	}
}
#endif
