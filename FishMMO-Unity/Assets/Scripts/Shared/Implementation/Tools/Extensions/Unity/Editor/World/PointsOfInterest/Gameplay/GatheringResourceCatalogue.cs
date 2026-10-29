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
		/// <summary>Bones, antler and ivory from old kills and middens (decision 25, 2026-10-25): no site kind gathers them yet (FamiliesFor) and no row stocks them.</summary>
		Bone = 6,
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
		/// Biome asset names the resource belongs in; empty for every biome. Since 2026-10-16 the sites read the templates'
		/// Gathering Resources; since 2026-10-26 (phase CODE-TABLE CLEARANCE, decision 1) each biome's spec file names its own
		/// (<see cref="BiomeArtSpec.Entry.Gathers"/>, written from these lists), so these lists are only the cut's fallback for a
		/// template never authored (BiomeTemplateArt.Gathering). Change a biome's resources in its file.
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

		// Geyser Basin (its round, 2026-10-26) gathers its own glass (raven-flint): the volcanic list without it.
		private static readonly string[] Volcanic = { "Volcanic", "Lava Tube", "Volcanic Cave", "Molten Surface", "Sulphur Flats", "Volcanic Temple", "Crater" };
		private static readonly string[] Caves = { "Cave", "Ice Cave", "Swamp Cave", "Volcanic Cave", "Lava Tube", "Karst" };
		private static readonly string[] Alien = { "Impact Basin", "Radiation Plain", "Regolith Plain", "Tholin Plain", "Nitrogen Ice Field", "Methane Lake", "Cryovolcanic Plain", "Ice Geyser Field", "Tidal Fracture", "Dust Sea", "Runaway Greenhouse Plain", "Sulphuric Cloud Deck", "Rille", "Subsurface Ocean Vent" };
		// The strongholds and palaces stand in temperate country; their gardens and moats grow its herbs.
		// Grassland, Plains, Hills, Steppe and Alpine Meadow (their rounds, 2026-10-26) gather their own herbs: the list without them.
		private static readonly string[] Temperate = { "Farmland", "Woodland", "Forest", "Valley", "Forest Ruins", "Castle", "Fortress", "Marble Palace" };
		// Alpine Meadow, Glacier, Mountain Slope and Ice Sheet (their rounds, 2026-10-26) gather their own: the cold list without them.
		private static readonly string[] Cold = { "Alpine", "Tundra", "Taiga", "Snow", "Permanent Ice", "Ice Shelf", "Ice Palace", "Ice Ruin" };
		// Wetlands, Estuary and Mangrove (their rounds, 2026-10-26) gather their own: the bog and swamp list without them.
		private static readonly string[] Wet = { "Swamp", "Peat Bog", "Swamp Ruins", "Swamp Temple" };
		private static readonly string[] Tropical = { "Jungle Ruins", "Jungle Temple" };

		private static readonly List<GatheringResource> all = new List<GatheringResource>
		{
			// ── Ore ──────────────────────────────────────────────────
			new GatheringResource { Node = "Copper Lode", Item = "Copper Ore", Family = GatheringFamily.Ore, Tier = 1, Visual = "Boulder_Sandstone_Round",
				Tint = new Color(0.86f, 0.52f, 0.32f), Size = 1.6f, MaxUses = 4, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				// Not Hills', Rocky Terrain's, Plains' or Steppe's since their rounds (2026-10-26): they mine their own.
				Biomes = new[] { "Desert", "High Desert", "Scrubland", "Valley", "Wasteland" },
				Description = "Green-stained copper ore from soft sedimentary rock." },
			new GatheringResource { Node = "Iron Vein", Item = "Iron Ore", Family = GatheringFamily.Ore, Tier = 1, Visual = "Boulder_Basalt_Round",
				Tint = new Color(0.62f, 0.34f, 0.26f), Size = 1.7f, MaxUses = 4, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				// Not Mountain Slope's, Rocky Terrain's, Scree's or Tundra's since their rounds (2026-10-26).
				Biomes = new[] { "Hills", "Volcanic", "Taiga", "Forest", "Valley", "Rocky Coast", "Wasteland", "Castle", "Fortress" },
				Description = "Rust-red iron ore, found wherever the rock is hard — and as black sand on the beach." },
			new GatheringResource { Node = "Silver Vein", Item = "Silver Ore", Family = GatheringFamily.Ore, Tier = 2, Visual = "Formation_Granite_Corestone_0",
				Tint = new Color(0.82f, 0.87f, 0.94f), Size = 1.8f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				// Not Mountain Slope's, Hills', Scree's, Glacier's or Ice Sheet's since their rounds (2026-10-26).
				Biomes = new[] { "Alpine", "Karst", "Tundra", "Ice Palace", "Ice Ruin" },
				Description = "Bright silver ore from the granite of the high country." },
			new GatheringResource { Node = "Gold Vein", Item = "Gold Ore", Family = GatheringFamily.Ore, Tier = 3, Visual = "Formation_Quartzite_Block_0",
				Tint = new Color(1.0f, 0.82f, 0.34f), Size = 1.7f, MaxUses = 3, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 2,
				// The Hills round (2026-10-26) mines it in the schist; not Mountain Slope's since its round.
				Biomes = new[] { "High Desert", "Valley", "Hills", "Marble Palace" },
				Description = "Gold in white quartz, where old rivers and dry ranges cut deep." },
			new GatheringResource { Node = "Starsilver Vein", Item = "Starsilver Ore", Family = GatheringFamily.Ore, Tier = 4, Visual = "Formation_Gneiss_Boulder_0",
				Tint = new Color(0.58f, 0.86f, 1.0f), Size = 1.9f, MaxUses = 2, GatherSeconds = 4.5f, MinAmount = 1, MaxAmount = 1,
				// Fallen stars lie where nothing buries them: the ice sheets keep every meteorite that lands on them.
				// Not Mountain Slope's, Glacier's or Ice Sheet's since their rounds (2026-10-26): the ice sheet keeps its star-stones.
				Biomes = new[] { "Alpine", "Impact Basin", "Crater", "Ice Shelf" },
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
				// Scrubland, Savanna and Steppe (their rounds, 2026-10-26) gather their own herbs: the dry list without them, so the fallback is their files.
				Biomes = new[] { "High Desert", "Oasis", "Wasteland" },
				Description = "Hardy petals that store the desert's heat." },
			new GatheringResource { Node = "Mirewort Patch", Item = "Mirewort", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_PitcherPlant",
				Tint = new Color(0.62f, 0.82f, 0.52f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = Wet, Description = "A bog plant prized by alchemists." },
			new GatheringResource { Node = "Bloodroot Patch", Item = "Bloodroot", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_FlowersWarm",
				Tint = new Color(1.0f, 0.55f, 0.55f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = Tropical, Description = "A red root from the deep jungle." },
			new GatheringResource { Node = "Samphire Patch", Item = "Samphire", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_ShrubSamphire",
				Tint = new Color(0.72f, 0.9f, 0.6f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Rocky Coast" },
				Description = "Salty sea-asparagus from the tideline." },

			// ── Fungus ───────────────────────────────────────────────
			new GatheringResource { Node = "Glowcap Cluster", Item = "Glowcap", Family = GatheringFamily.Fungus, Tier = 2, Visual = "Detail_MushroomCave",
				Tint = new Color(0.62f, 1.0f, 0.9f), Size = 0.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = Caves, Description = "A cave mushroom that glows in the dark." },
			new GatheringResource { Node = "Morel Cluster", Item = "Forest Morel", Family = GatheringFamily.Fungus, Tier = 1, Visual = "Detail_MushroomForest",
				Tint = new Color(0.88f, 0.78f, 0.62f), Size = 0.5f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				// Not Woodland's since its round (2026-10-26): its chanterelles took the morels' place.
				Biomes = new[] { "Forest", "Taiga", "Forest Ruins", "Valley" },
				Description = "An honest woodland mushroom." },
			new GatheringResource { Node = "Bog Puffball Cluster", Item = "Bog Puffball", Family = GatheringFamily.Fungus, Tier = 1, Visual = "Detail_MushroomSwamp",
				Tint = new Color(0.88f, 0.88f, 0.72f), Size = 0.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = Wet, Description = "Puffs a cloud of spores when picked." },

			// ── Wood ─────────────────────────────────────────────────
			new GatheringResource { Node = "Ancient Oak", Item = "Ancient Heartwood", Family = GatheringFamily.Wood, Tier = 3, Visual = "Tree_Oak",
				Tint = new Color(1.0f, 0.95f, 0.86f), Size = 0.0f, Footprint = 0.18f, MaxUses = 5, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 2,
				// Not Woodland's, Hills', Grassland's or Plains' since their rounds (2026-10-26): Woodland has its own King Oak.
				Biomes = new[] { "Forest", "Forest Ruins", "Valley" },
				Description = "Heartwood from an oak older than any kingdom." },
			new GatheringResource { Node = "Elder Ironbark", Item = "Ironbark Heartwood", Family = GatheringFamily.Wood, Tier = 3, Visual = "Tree_Baobab",
				Tint = new Color(0.92f, 0.86f, 0.8f), Size = 0.0f, Footprint = 0.3f, MaxUses = 5, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 2,
				// Not Steppe's since its round (2026-10-26): it burns saxaul.
				Biomes = new[] { "Scrubland", "Oasis" },
				Description = "Wood so dense it sinks." },
			new GatheringResource { Node = "Frostpine Elder", Item = "Frostpine Heartwood", Family = GatheringFamily.Wood, Tier = 3, Visual = "Tree_Spruce",
				Tint = new Color(0.86f, 0.95f, 1.0f), Size = 0.0f, Footprint = 0.15f, MaxUses = 5, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 2,
				// Not Tundra's (treeless) or Mountain Slope's since their rounds (2026-10-26).
				Biomes = new[] { "Taiga", "Alpine", "Snow" },
				Description = "Pale, resinous wood from the oldest pines." },

			// ── Marine ───────────────────────────────────────────────
			// Gathered round the sunken sites (FamiliesFor): the sea floor and a lake's bed had no nodes at all (audit 2026-10-11).
			new GatheringResource { Node = "Pearl Oyster Bed", Item = "Pearl", Family = GatheringFamily.Marine, Tier = 2, Visual = "Detail_MusselBedOyster",
				Tint = new Color(0.92f, 0.9f, 0.86f), Size = 1.2f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 1,
				// Freshwater pearl mussels in the lakes and the clean rivers.
				Biomes = new[] { "Coral Reef" },
				Description = "An oyster bed; one shell in a hundred holds a pearl worth the dive." },
			new GatheringResource { Node = "Kelp Holdfast", Item = "Kelp Frond", Family = GatheringFamily.Marine, Tier = 1, Visual = "Detail_Kelp",
				Tint = new Color(0.78f, 0.86f, 0.55f), Size = 1.8f, MaxUses = 4, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Coastal Water", "Seamount", "Underwater Canyon" },
				Description = "Tough fronds from a kelp forest's floor." },
			new GatheringResource { Node = "Manganese Nodules", Item = "Manganese Nodule", Family = GatheringFamily.Marine, Tier = 3, Visual = "Boulder_Nodule_Round",
				Tint = new Color(0.55f, 0.5f, 0.46f), Size = 1.4f, MaxUses = 3, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Abyssal Plain", "Deep Ocean" },
				Description = "Metal-rich lumps grown a millimetre a million years on the abyssal floor." },
			new GatheringResource { Node = "Glass Sponge", Item = "Glass Spicule", Family = GatheringFamily.Marine, Tier = 3, Visual = "Detail_GlassSponge",
				Tint = new Color(0.86f, 0.94f, 1.0f), Size = 1.3f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Deep Ocean", "Seamount", "Underwater Canyon" },
				Description = "Silica lace from a sponge older than the kingdoms above it." },
			new GatheringResource { Node = "Vent Clam Bed", Item = "Vent Pearl", Family = GatheringFamily.Marine, Tier = 4, Visual = "Detail_MusselBedVentClam",
				Tint = new Color(1.0f, 0.86f, 0.7f), Size = 1.2f, MaxUses = 2, GatherSeconds = 4.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Subsurface Ocean Vent" },
				Description = "A pearl grown in the heat of a black smoker." },

			// ── Coastal Water (biome round, 2026-10-26): the Celtic shelf's harvest; its pearl oysters and reef coral went to the
			// warm reef (they were never this sea's). ──
			new GatheringResource { Node = "Flat Oyster Bed", Item = "Flat Oyster", Family = GatheringFamily.Marine, Tier = 1, Visual = "Detail_MusselBedFlatOyster",
				Tint = new Color(0.9f, 0.88f, 0.86f), Size = 1.2f, MaxUses = 4, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Coastal Water" }, Description = "Round grey-lilac oysters dredged from the old beds off the harbours." },
			new GatheringResource { Node = "Scallop Bed", Item = "Pilgrim Scallop", Family = GatheringFamily.Marine, Tier = 2, Visual = "Detail_MusselBedScallop",
				Tint = new Color(1.0f, 0.86f, 0.78f), Size = 1.2f, MaxUses = 3, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Coastal Water" }, Description = "Great scallops recessed in the sand; their shells are the pilgrim's badge." },
			new GatheringResource { Node = "Carrageen", Item = "Irish Moss", Family = GatheringFamily.Marine, Tier = 1, Visual = "Detail_Carrageen",
				Tint = new Color(0.95f, 0.85f, 0.95f), Size = 0.9f, MaxUses = 4, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Coastal Water" }, Description = "Purple-red fan weed with blue-tipped fronds, boiled down for a healer's jelly." },
			new GatheringResource { Node = "Dulse", Item = "Dulse", Family = GatheringFamily.Marine, Tier = 1, Visual = "Detail_Dulse",
				Tint = new Color(1.0f, 0.88f, 0.86f), Size = 0.9f, MaxUses = 4, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Coastal Water" }, Description = "Red-brown hand-shaped fronds, dried and chewed by every fisher on the coast." },
			new GatheringResource { Node = "Maerl", Item = "Maerl Lime", Family = GatheringFamily.Marine, Tier = 1, Visual = "Detail_Pebbles_Maerl",
				Tint = new Color(1.0f, 0.86f, 0.9f), Size = 1.0f, MaxUses = 4, GatherSeconds = 2.0f, MinAmount = 2, MaxAmount = 4,
				Biomes = new[] { "Coastal Water" }, Description = "Pink coralline gravel, carted inland to sweeten sour fields." },
			new GatheringResource { Node = "Cassiterite", Item = "Tinstone", Family = GatheringFamily.Ore, Tier = 2, Visual = "Formation_Dolerite_Gully_0",
				Tint = new Color(0.62f, 0.52f, 0.46f), Size = 1.4f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Coastal Water" }, Description = "Black-brown tinstone in knee-twinned prisms, won from the sea caves and the islet adits." },
			new GatheringResource { Node = "Bellspar", Item = "Bellspar", Family = GatheringFamily.Crystal, Tier = 3, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.55f, 0.48f, 1.0f), Size = 1.4f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Coastal Water" }, Description = "Violet-blue hollow prisms that ring like a drowned city's bells when touched." },

			// ── Ocean (biome round, 2026-10-26): the slope sea's harvest; its pearls and kelp were the shelf's. ──
			new GatheringResource { Node = "Lophelia Coral", Item = "Lime-coral", Family = GatheringFamily.Marine, Tier = 2, Visual = "Detail_Lophelia",
				Tint = new Color(1.0f, 0.94f, 0.9f), Size = 1.3f, MaxUses = 3, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Ocean" }, Description = "White branches of cold-water coral, burnt for mortar and carved for charms." },
			new GatheringResource { Node = "Bubblegum Coral", Item = "Rose Coral", Family = GatheringFamily.Marine, Tier = 3, Visual = "Detail_BubblegumCoral",
				Tint = new Color(1.0f, 0.78f, 0.8f), Size = 1.6f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Ocean" }, Description = "Knobbed rose-red coral from the mound crests, prized by jewellers." },
			// LATER (I7, Bone sites): no site kind gathers the Bone family yet; the whale tow will.
			new GatheringResource { Node = "Baleen Plate", Item = "Baleen", Family = GatheringFamily.Bone, Tier = 2, Visual = "Formation_Moundstone_Slabs_0",
				Tint = new Color(0.3f, 0.28f, 0.26f), Size = 1.4f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Ocean" }, Description = "Whalebone plates cut from a right whale's jaw, for stays, bows and combs." },
			new GatheringResource { Node = "Siderite Spar", Item = "Siderite", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.86f, 0.66f, 0.42f), Size = 1.3f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Ocean" }, Description = "Honey-brown saddle rhombs of iron spar on cream ankerite." },
			new GatheringResource { Node = "Galerna-stone", Item = "Galerna-stone", Family = GatheringFamily.Crystal, Tier = 4, Visual = "Formation_WaterIce_Crystal_0",
				Tint = new Color(0.62f, 0.5f, 0.72f), Size = 1.5f, MaxUses = 2, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Ocean" }, Description = "Smoky storm-violet quartz whose ember core brightens before the sudden storm." },
			// LATER (I7): only at the lamiak's grotto, one to a site; the shrine's own nodes stand in.
			new GatheringResource { Node = "Lamia Comb", Item = "Lamia Comb", Family = GatheringFamily.Marine, Tier = 4, Visual = "Detail_ShellsAtlantic",
				Tint = new Color(1.0f, 0.84f, 0.4f), Size = 0.8f, MaxUses = 1, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Ocean" }, Description = "A golden comb left on a grotto ledge; its owner will want it back." },
			// ── Tundra (biome round, 2026-10-26) ── Tusk weathering out of the thaw slumps, roseroot and snow lichen, labradorite in the
			// outcrops, sky-iron where a star fell. LATER (I7): no site kind gathers Bone yet (FamiliesFor), so the ivory waits for one.
			new GatheringResource { Node = "Mammoth Ivory", Item = "Mammoth Ivory", Family = GatheringFamily.Bone, Tier = 3, Visual = "Deadwood_Driftwood",
				Tint = new Color(1.0f, 0.95f, 0.84f), Size = 1.8f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Tundra" }, Description = "A frozen giant's tusk weathering out of a thaw slump, yellowed and cracked." },
			new GatheringResource { Node = "Arctic Root Patch", Item = "Arctic Root", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_Roseroot",
				Tint = new Color(0.9f, 0.86f, 0.62f), Size = 0.7f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Tundra" }, Description = "Roseroot: its cut root smells of roses and keeps a traveller on his feet." },
			new GatheringResource { Node = "Snow Lichen Patch", Item = "Snow Lichen", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_LichenSnow",
				Tint = new Color(0.96f, 0.94f, 0.78f), Size = 0.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Tundra" }, Description = "Crisp cream lichen, boiled for broth and for a cough." },
			new GatheringResource { Node = "Labradorite Seam", Item = "Labradorite", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Formation_Gneiss_FrostJacked_0",
				Tint = new Color(0.5f, 0.78f, 0.86f), Size = 1.5f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Tundra" }, Description = "Dark grey stone that flashes blue, green and gold when turned in the light." },
			new GatheringResource { Node = "Sky-iron Fragment", Item = "Sky-iron", Family = GatheringFamily.Ore, Tier = 3, Visual = "Boulder_Basalt_Round",
				Tint = new Color(0.52f, 0.5f, 0.54f), Size = 1.2f, MaxUses = 2, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Tundra" }, Description = "Iron that fell from the sky, cold-hammered into blades by the herders." },

			// ── Ice Sheet (biome round, 2026-10-26) ── Stone-oil from the petrel ledges, breath-ice from the blue-ice fields, star-stones,
			// blue quartz, and horn-ice at the Winter Bull's horns. LATER (I7): Nest and Landmark sites gather nothing yet (FamiliesFor);
			// stone-oil rides the Ore family (caves), horn-ice the Crystal family, one to a scene only once the Horns piece exists.
			new GatheringResource { Node = "Stone-oil Crust", Item = "Stone-oil", Family = GatheringFamily.Ore, Tier = 3, Visual = "Formation_Charnockite_Ledges_0",
				Tint = new Color(0.36f, 0.24f, 0.14f), Size = 1.6f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Ice Sheet" }, Description = "Black-amber pitch scraped from a nunatak ledge, laid down by ages of snow petrels." },
			new GatheringResource { Node = "Breath-ice", Item = "Breath-ice", Family = GatheringFamily.Crystal, Tier = 4, Visual = "IceBoulder_Rounded",
				Tint = new Color(0.62f, 0.8f, 1.0f), Size = 1.3f, MaxUses = 2, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Ice Sheet" }, Description = "Ancient blue ice that crackles as it melts, letting out air ten thousand years old." },
			new GatheringResource { Node = "Star-stone", Item = "Star-stone", Family = GatheringFamily.Ore, Tier = 3, Visual = "Boulder_Basalt_Round",
				Tint = new Color(0.42f, 0.36f, 0.32f), Size = 0.9f, MaxUses = 2, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Ice Sheet" }, Description = "A stony meteorite in a black fusion crust, kept by the blue ice where it fell." },
			new GatheringResource { Node = "Blue Quartz Cluster", Item = "Blue Quartz", Family = GatheringFamily.Crystal, Tier = 3, Visual = "Formation_Charnockite_Boulder_0",
				Tint = new Color(0.62f, 0.72f, 0.9f), Size = 1.4f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Ice Sheet" }, Description = "Opalescent blue-grey quartz out of the charnockite." },
			new GatheringResource { Node = "Horn-ice", Item = "Horn-ice", Family = GatheringFamily.Crystal, Tier = 4, Visual = "Serac_Tower",
				Tint = new Color(0.7f, 0.86f, 1.0f), Size = 1.6f, MaxUses = 1, GatherSeconds = 5.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Ice Sheet" }, Description = "A splinter of the Winter Bull's shed horn, ringed like a ram's, cold enough to burn." },

			// ── Glacier (biome round, 2026-10-26) ── The scribes' birch bark, incense and rhubarb roots, ant-gold from the outwash,
			// sapphire from the nunataks' pegmatites, old snout ice for the cellars, and the serpent-jewel of the ice caves.
			new GatheringResource { Node = "Bhojpatra Bark", Item = "Bhojpatra Bark", Family = GatheringFamily.Wood, Tier = 2, Visual = "Tree_Bhojpatra",
				Tint = new Color(1.0f, 0.92f, 0.86f), Size = 0.0f, Footprint = 0.2f, MaxUses = 4, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Glacier" }, Description = "Sheets of salmon-white birch bark, fine enough to write a chronicle on." },
			new GatheringResource { Node = "Dhoop Root Patch", Item = "Dhoop Root", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_HerbDhoop",
				Tint = new Color(0.86f, 0.8f, 0.92f), Size = 0.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Glacier" }, Description = "A resinous root burned as incense at the ice-cave shrines." },
			new GatheringResource { Node = "Rhubarb Root Patch", Item = "Rhubarb Root", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_RhubarbHimalayan",
				Tint = new Color(0.92f, 0.78f, 0.72f), Size = 0.9f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Glacier" }, Description = "A bitter yellow root dug from the forefield, a purge and a dye." },
			new GatheringResource { Node = "Ant-gold Sand", Item = "Ant-gold", Family = GatheringFamily.Ore, Tier = 2, Visual = "Formation_Greenstone_Scree_0",
				Tint = new Color(1.0f, 0.84f, 0.42f), Size = 1.4f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Glacier" }, Description = "Gold sand thrown up from the outwash by the fox-sized ants, washed in goatskin sluices." },
			new GatheringResource { Node = "Cornflower Sapphire", Item = "Cornflower Sapphire", Family = GatheringFamily.Crystal, Tier = 3, Visual = "Formation_Greenstone_Block_0",
				Tint = new Color(0.52f, 0.6f, 1.0f), Size = 1.4f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Glacier" }, Description = "Velvet-blue sapphire from a pegmatite pocket in the nunatak rock." },
			new GatheringResource { Node = "Old Ice", Item = "Old Ice", Family = GatheringFamily.Crystal, Tier = 1, Visual = "IceBoulder_Slab",
				Tint = new Color(0.72f, 0.86f, 1.0f), Size = 1.4f, MaxUses = 4, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Glacier" }, Description = "Blue snout ice cut in blocks for the cellars below." },
			new GatheringResource { Node = "Nagamani Cluster", Item = "Nagamani", Family = GatheringFamily.Crystal, Tier = 4, Visual = "Formation_WaterIce_Crystal_0",
				Tint = new Color(0.42f, 0.88f, 0.75f), Size = 1.2f, MaxUses = 1, GatherSeconds = 4.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Glacier" }, Description = "A water-clear serpent-jewel with a sea-green heart that glows in the dark." },
			// ── Forest (biome round, 2026-10-26) ── Yew for bow staves, wild garlic from the ravines, truffles under the oaks, and the
			// foxfire of the deep wood's rotten logs.
			new GatheringResource { Node = "Yew Heartwood", Item = "Yew Stave", Family = GatheringFamily.Wood, Tier = 2, Visual = "Tree_Yew",
				Tint = new Color(1.0f, 0.9f, 0.84f), Size = 0.0f, Footprint = 0.25f, MaxUses = 4, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Forest" }, Description = "Red yew heartwood with its pale sapwood, split for a bow stave." },
			new GatheringResource { Node = "Wild Garlic", Item = "Ramsons", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_FlowersRamsons",
				Tint = new Color(0.94f, 1.0f, 0.92f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Forest" }, Description = "Broad wild-garlic leaves and white star flowers; the whole ravine smells of it." },
			new GatheringResource { Node = "Truffle Patch", Item = "Black Truffle", Family = GatheringFamily.Fungus, Tier = 2, Visual = "Detail_MushroomCep",
				Tint = new Color(0.7f, 0.62f, 0.56f), Size = 0.7f, MaxUses = 2, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Forest" }, Description = "A knobbled black truffle dug from the roots of an old oak." },
			new GatheringResource { Node = "Foxfire Wood", Item = "Foxfire Wood", Family = GatheringFamily.Fungus, Tier = 3, Visual = "Detail_MushroomFoxfire",
				Tint = new Color(0.82f, 1.0f, 0.72f), Size = 0.9f, MaxUses = 2, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Forest" }, Description = "Rotten wood laced with honey-fungus threads that glow green in the dark." },
			// ── Woodland (biome round, 2026-10-26) ── The wood's own king oak, the coppice's nuts and chestnuts, chanterelles, flint from
			// the chalk, woad for the dyers, and the golden mistletoe cut with a gold sickle.
			new GatheringResource { Node = "King Oak", Item = "King Oak Heartwood", Family = GatheringFamily.Wood, Tier = 3, Visual = "Tree_OakWoodland",
				Tint = new Color(1.0f, 0.96f, 0.86f), Size = 0.0f, Footprint = 0.2f, MaxUses = 5, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Woodland" }, Description = "Heartwood from the great pollard oak a wood is named for." },
			new GatheringResource { Node = "Chanterelle Patch", Item = "Chanterelle", Family = GatheringFamily.Fungus, Tier = 1, Visual = "Detail_MushroomChanterelle",
				Tint = new Color(1.0f, 0.92f, 0.7f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Woodland" }, Description = "Golden trumpets that smell faintly of apricots." },
			new GatheringResource { Node = "Hazelnut Cluster", Item = "Hazelnuts", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_Bush_HazelCoppice",
				Tint = new Color(1.0f, 0.95f, 0.85f), Size = 0.0f, Footprint = 0.15f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Woodland" }, Description = "Ripe cobnuts shaken from the coppice hazel." },
			new GatheringResource { Node = "Sweet Chestnut Fall", Item = "Sweet Chestnuts", Family = GatheringFamily.Herb, Tier = 2, Visual = "Tree_SweetChestnut",
				Tint = new Color(1.0f, 0.94f, 0.82f), Size = 0.0f, Footprint = 0.2f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Woodland" }, Description = "Glossy chestnuts trodden out of their spiny husks under the old tree." },
			new GatheringResource { Node = "Flint Nodules", Item = "Flint", Family = GatheringFamily.Ore, Tier = 1, Visual = "Detail_Rocks_Flint",
				Tint = new Color(0.9f, 0.9f, 0.92f), Size = 1.3f, MaxUses = 4, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Woodland" }, Description = "Black flint from a chalk scrape, for strike-a-lights and knapped blades." },
			new GatheringResource { Node = "Woad Patch", Item = "Woad Leaves", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_FlowersWoad",
				Tint = new Color(0.9f, 1.0f, 0.92f), Size = 0.8f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Woodland" }, Description = "Blue-green woad leaves, crushed and balled for the dyers' vats." },
			// LATER (I9b): the mistletoe clump on its host; a round evergreen bush tinted gold stands in.
			new GatheringResource { Node = "Golden Mistletoe", Item = "Golden Mistletoe", Family = GatheringFamily.Herb, Tier = 3, Visual = "Detail_Bush_Box",
				Tint = new Color(1.0f, 0.86f, 0.42f), Size = 0.0f, Footprint = 0.12f, MaxUses = 1, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Woodland" }, Description = "A ball of mistletoe that glows faint gold at dusk, cut only with a golden sickle." },

			// ── Alpine Meadow (biome round, 2026-10-26) ── The pastures' herbs, the fairy-ring champignons, stone-pine heartwood for
			// carving, antimony needles from the mining hills' andesite, and stormglass from the tarns and caves.
			new GatheringResource { Node = "Arnica Bloom", Item = "Arnica", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_HerbArnica",
				Tint = new Color(1.0f, 0.82f, 0.5f), Size = 0.7f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Alpine Meadow" }, Description = "Orange-gold mountain flowers steeped into a salve for bruises." },
			new GatheringResource { Node = "Gentian Root", Item = "Gentian Root", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_GentianYellow",
				Tint = new Color(1.0f, 0.92f, 0.6f), Size = 0.9f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Alpine Meadow" }, Description = "The bitter yellow gentian's root, dug for the shepherds' bitters." },
			new GatheringResource { Node = "Iceland Moss", Item = "Iceland Moss", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_LichenIceland",
				Tint = new Color(0.95f, 0.9f, 0.75f), Size = 0.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Alpine Meadow" }, Description = "Curled olive lichen, famine bread and a cough cure." },
			new GatheringResource { Node = "Carline Star", Item = "Carline Star", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_CarlineThistle",
				Tint = new Color(1.0f, 0.97f, 0.9f), Size = 0.6f, MaxUses = 2, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Alpine Meadow" }, Description = "A stemless silver thistle star; nailed over a door, it closes before the storm." },
			new GatheringResource { Node = "Fairy-ring Champignon", Item = "Champignon", Family = GatheringFamily.Fungus, Tier = 1, Visual = "Detail_FairyRing",
				Tint = new Color(1.0f, 0.92f, 0.8f), Size = 0.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Alpine Meadow" }, Description = "Tan caps from a ring in the turf; the shepherds leave the burnt rings alone." },
			new GatheringResource { Node = "Cembra Heartwood", Item = "Stone-pine Heartwood", Family = GatheringFamily.Wood, Tier = 3, Visual = "Tree_SnagCembra",
				Tint = new Color(1.0f, 0.92f, 0.86f), Size = 0.0f, Footprint = 0.15f, MaxUses = 4, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Alpine Meadow" }, Description = "Fragrant pink-gold wood from a lightning-struck stone pine, the carvers' favourite." },
			new GatheringResource { Node = "Stibnite Vein", Item = "Stibnite", Family = GatheringFamily.Ore, Tier = 2, Visual = "Formation_Andesite_Block_0",
				Tint = new Color(0.6f, 0.64f, 0.72f), Size = 1.4f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Alpine Meadow" }, Description = "Steel-grey antimony needles in the andesite: kohl, bell-metal and the alchemists' grey wolf." },
			new GatheringResource { Node = "Stormglass Cluster", Item = "Stormglass", Family = GatheringFamily.Crystal, Tier = 4, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.6f, 0.6f, 0.78f), Size = 1.4f, MaxUses = 2, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Alpine Meadow" }, Description = "Smoky violet spheres full of cloud, threads of lightning flickering in them." },

			// ── Jungle (biome round, 2026-10-26) ── The forest kingdoms' gifts: kola, palm oil and wine, the market herbs, camwood, iroko, raffia,
			// laterite iron for the bloomeries, tourmaline and leafglass from the caves, termite mushrooms in the rains.
			new GatheringResource { Node = "Kola Nuts", Item = "Kola Nut", Family = GatheringFamily.Herb, Tier = 1, Visual = "Tree_Kola",
				Tint = new Color(1.0f, 0.9f, 0.8f), Size = 0.0f, Footprint = 0.12f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Jungle" }, Description = "The bitter gift nut, offered at every greeting." },
			new GatheringResource { Node = "Palm Fruit", Item = "Palm Fruit", Family = GatheringFamily.Herb, Tier = 1, Visual = "Tree_OilPalm",
				Tint = new Color(1.0f, 0.7f, 0.4f), Size = 0.0f, Footprint = 0.15f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Jungle" }, Description = "Orange-red oil-palm bunches, pressed for red oil." },
			new GatheringResource { Node = "Palm Wine", Item = "Palm Wine", Family = GatheringFamily.Herb, Tier = 2, Visual = "Tree_OilPalm",
				Tint = new Color(1.0f, 0.95f, 0.8f), Size = 0.0f, Footprint = 0.15f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Jungle" }, Description = "Sap tapped into gourds at the crown; sweet at dawn, sour by noon." },
			new GatheringResource { Node = "Alligator Pepper", Item = "Alligator Pepper", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_FlowersAlligatorPepper",
				Tint = new Color(1.0f, 0.8f, 0.8f), Size = 0.7f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Jungle" }, Description = "Scarlet pods of hot seeds, chewed at feasts and sworn on." },
			new GatheringResource { Node = "Bitter Leaf", Item = "Bitter Leaf", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_Bush_Bitterleaf",
				Tint = new Color(0.8f, 1.0f, 0.8f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Jungle" }, Description = "Washed bitter leaves for soups and fevers." },
			new GatheringResource { Node = "Scent Leaf", Item = "Scent Leaf", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_HerbScentLeaf",
				Tint = new Color(0.85f, 1.0f, 0.85f), Size = 0.7f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Jungle" }, Description = "Aromatic leaves for stews and the sick-room." },
			new GatheringResource { Node = "Costus Stem", Item = "Costus Stem", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_FlowersCostus",
				Tint = new Color(1.0f, 0.8f, 0.75f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Jungle" }, Description = "Sour sap-filled ginger stems, chewed for thirst." },
			new GatheringResource { Node = "Calabar Bean", Item = "Calabar Bean", Family = GatheringFamily.Herb, Tier = 3, Visual = "Detail_DebrisPods",
				Tint = new Color(0.7f, 0.5f, 0.35f), Size = 0.8f, MaxUses = 2, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Jungle" }, Description = "A poison bean of the stream lianas, used in the old trials." },
			new GatheringResource { Node = "Camwood Heartwood", Item = "Camwood Heartwood", Family = GatheringFamily.Wood, Tier = 2, Visual = "Tree_AfricanMahogany",
				Tint = new Color(1.0f, 0.6f, 0.5f), Size = 0.0f, Footprint = 0.15f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Jungle" }, Description = "Red dye-wood rubbed into skin and cloth for rank." },
			new GatheringResource { Node = "Iroko Timber", Item = "Iroko Timber", Family = GatheringFamily.Wood, Tier = 2, Visual = "Tree_Iroko",
				Tint = new Color(1.0f, 0.9f, 0.8f), Size = 0.0f, Footprint = 0.2f, MaxUses = 4, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Jungle" }, Description = "Hard golden timber that no termite will eat." },
			new GatheringResource { Node = "Raffia Fibre", Item = "Raffia Fibre", Family = GatheringFamily.Herb, Tier = 1, Visual = "Tree_RaffiaPalm",
				Tint = new Color(0.95f, 0.9f, 0.7f), Size = 0.0f, Footprint = 0.15f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Jungle" }, Description = "Pale leaf fibre for thatch, cord and woven cloth." },
			new GatheringResource { Node = "Laterite Iron Ore", Item = "Laterite Iron", Family = GatheringFamily.Ore, Tier = 1, Visual = "Formation_Laterite_Block_0",
				Tint = new Color(0.8f, 0.45f, 0.3f), Size = 1.3f, MaxUses = 3, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Jungle" }, Description = "Pisolite ironstone smelted in clay shaft furnaces." },
			new GatheringResource { Node = "Watermelon Tourmaline", Item = "Watermelon Tourmaline", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(1.0f, 0.5f, 0.7f), Size = 1.3f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Jungle" }, Description = "Striated prisms, pink at the core and green at the rind." },
			new GatheringResource { Node = "Leafglass", Item = "Leafglass", Family = GatheringFamily.Crystal, Tier = 3, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.5f, 1.0f, 0.55f), Size = 1.4f, MaxUses = 2, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Jungle" }, Description = "Translucent green plates whose veins look like leaf skeletons; it glows like breathing." },
			new GatheringResource { Node = "Termite Mushrooms", Item = "Termite Mushroom", Family = GatheringFamily.Fungus, Tier = 1, Visual = "Detail_MushroomTermite",
				Tint = new Color(0.9f, 0.85f, 0.75f), Size = 0.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Jungle" }, Description = "Tall rain-season caps that rise from the mounds." },

			// ── Bamboo Forest (biome round, 2026-10-26) ── Culms and spring shoots, matsutake and shiitake, tea, kudzu starch, ferns, andesite
			// and tile clay, jadeite, twinned quartz and firefly glass.
			new GatheringResource { Node = "Bamboo Culm", Item = "Bamboo Culm", Family = GatheringFamily.Wood, Tier = 1, Visual = "Tree_Moso",
				Tint = new Color(0.9f, 1.0f, 0.7f), Size = 0.0f, Footprint = 0.1f, MaxUses = 4, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Bamboo Forest" }, Description = "Straight pale culms for scaffold, pipe and fence." },
			new GatheringResource { Node = "Takenoko Shoot", Item = "Bamboo Shoot", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_Takenoko",
				Tint = new Color(0.9f, 0.8f, 0.6f), Size = 0.7f, MaxUses = 2, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Bamboo Forest" }, Description = "Spring shoots dug before they break the leaf litter." },
			new GatheringResource { Node = "Matsutake", Item = "Matsutake", Family = GatheringFamily.Fungus, Tier = 3, Visual = "Detail_MushroomMatsutake",
				Tint = new Color(0.85f, 0.7f, 0.55f), Size = 0.6f, MaxUses = 2, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Bamboo Forest" }, Description = "A prized stout mushroom of the red-pine duff." },
			new GatheringResource { Node = "Shiitake", Item = "Shiitake", Family = GatheringFamily.Fungus, Tier = 1, Visual = "Detail_MushroomShiitake",
				Tint = new Color(0.8f, 0.65f, 0.5f), Size = 0.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Bamboo Forest" }, Description = "Brown cracked caps off the fallen cedar logs." },
			new GatheringResource { Node = "Tea Leaf", Item = "Tea Leaf", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_Bush_Chanoki",
				Tint = new Color(0.7f, 1.0f, 0.6f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Bamboo Forest" }, Description = "First-flush leaves from the clipped tea rows." },
			new GatheringResource { Node = "Kuzu Root", Item = "Kuzu Root", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_HerbDokudami",
				Tint = new Color(0.95f, 0.95f, 0.85f), Size = 0.8f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Bamboo Forest" }, Description = "Starch-rich roots dug from under the smothering vine." },
			new GatheringResource { Node = "Zenmai Fiddlehead", Item = "Fiddlehead", Family = GatheringFamily.Fungus, Tier = 1, Visual = "Detail_FernZenmai",
				Tint = new Color(0.9f, 0.75f, 0.55f), Size = 0.7f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Bamboo Forest" }, Description = "Woolly cinnamon crosiers of the royal fern, eaten as mountain greens." },
			new GatheringResource { Node = "Andesite Block", Item = "Andesite Block", Family = GatheringFamily.Ore, Tier = 1, Visual = "Formation_Andesite_Block_0",
				Tint = new Color(0.7f, 0.72f, 0.75f), Size = 1.4f, MaxUses = 4, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Bamboo Forest" }, Description = "Blue-grey platy blocks for lanterns, steps and podiums." },
			new GatheringResource { Node = "Masa Clay", Item = "Masa Clay", Family = GatheringFamily.Ore, Tier = 1, Visual = "Formation_Andesite_Block_0",
				Tint = new Color(0.85f, 0.75f, 0.6f), Size = 1.2f, MaxUses = 4, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Bamboo Forest" }, Description = "Pale granite-grus clay, fired into roof tiles." },
			new GatheringResource { Node = "Jadeite Nodule", Item = "Jadeite", Family = GatheringFamily.Crystal, Tier = 3, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.6f, 0.9f, 0.65f), Size = 1.3f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Bamboo Forest" }, Description = "Waxy apple-green and lavender nodules." },
			new GatheringResource { Node = "Twinquartz", Item = "Twinned Quartz", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.9f, 0.95f, 1.0f), Size = 1.3f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Bamboo Forest" }, Description = "Flat heart-shaped water-clear twins." },
			new GatheringResource { Node = "Hotarubi Cluster", Item = "Hotarubi", Family = GatheringFamily.Crystal, Tier = 4, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.8f, 1.0f, 0.4f), Size = 1.4f, MaxUses = 2, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Bamboo Forest" }, Description = "Firefly glass: a clear druse full of drifting yellow-green pinpoints." },

			// ── Mountain Slope (biome round, 2026-10-26) ── Laterite ironstone from the road cuts, opal and moonstone from the cliff bands,
			// kosso blossom, wild coffee and gesho, termite mushrooms, ancient yellowwood, and wild honey. LATER (I7): Wild Honey Hive
			// at the tree hives only; the meeting fig stands in as its visual.
			new GatheringResource { Node = "Laterite Ironstone", Item = "Bog Iron", Family = GatheringFamily.Ore, Tier = 1, Visual = "Formation_Phonolite_Rind_0",
				Tint = new Color(0.72f, 0.4f, 0.28f), Size = 1.3f, MaxUses = 3, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Mountain Slope" }, Description = "Red pisolite ironstone pan broken from the road cuts and smelted in clay furnaces." },
			new GatheringResource { Node = "Hydrophane Seam", Item = "Hydrophane Opal", Family = GatheringFamily.Crystal, Tier = 3, Visual = "Formation_Phonolite_Ledges_0",
				Tint = new Color(1.0f, 0.92f, 0.8f), Size = 1.5f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Mountain Slope" }, Description = "Milky to honey opal nodules that drink water and flash fire when they dry." },
			new GatheringResource { Node = "Moonstone Cluster", Item = "Moonstone", Family = GatheringFamily.Crystal, Tier = 4, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.72f, 0.82f, 1.0f), Size = 1.4f, MaxUses = 2, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Mountain Slope" }, Description = "Tabular white stone with a drifting blue sheen that glows in the dark." },
			new GatheringResource { Node = "Kosso Blossom", Item = "Kosso Blossom", Family = GatheringFamily.Herb, Tier = 2, Visual = "Tree_Hagenia",
				Tint = new Color(1.0f, 0.78f, 0.78f), Size = 0.0f, Footprint = 0.15f, MaxUses = 3, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Mountain Slope" }, Description = "Pink-red flower tassels of the kosso tree, a bitter cure for worms." },
			new GatheringResource { Node = "Wild Coffee", Item = "Wild Coffee", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_Bush_Coffee",
				Tint = new Color(1.0f, 0.86f, 0.82f), Size = 0.0f, Footprint = 0.12f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Mountain Slope" }, Description = "Red cherries from the coffee shrubs of the warm forest, roasted over a brazier." },
			new GatheringResource { Node = "Gesho", Item = "Gesho Leaves", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_Bush_Gesho",
				Tint = new Color(0.9f, 1.0f, 0.86f), Size = 0.0f, Footprint = 0.12f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Mountain Slope" }, Description = "Glossy leaves that bitter the honey mead." },
			new GatheringResource { Node = "Termite Mushroom Ring", Item = "Termite Mushroom", Family = GatheringFamily.Fungus, Tier = 1, Visual = "Detail_MushroomTermitomyces",
				Tint = new Color(1.0f, 0.95f, 0.88f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Mountain Slope" }, Description = "Tall pale parasols that rise in rings from the termites' gardens after the rains." },
			new GatheringResource { Node = "Yellowwood Elder", Item = "Yellowwood Heartwood", Family = GatheringFamily.Wood, Tier = 3, Visual = "Tree_Podocarpus",
				Tint = new Color(1.0f, 0.95f, 0.7f), Size = 0.0f, Footprint = 0.15f, MaxUses = 5, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Mountain Slope" }, Description = "Golden heartwood from an ancient yellowwood fallen in the cloud forest." },
			new GatheringResource { Node = "Wild Honey Hive", Item = "Wild Honey", Family = GatheringFamily.Herb, Tier = 2, Visual = "Tree_Warka",
				Tint = new Color(1.0f, 0.9f, 0.6f), Size = 0.0f, Footprint = 0.15f, MaxUses = 2, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Mountain Slope" }, Description = "Dark honey from a bark hive slung high in a fig's limbs, for the mead." },

			// ── Scree (biome round, 2026-10-26) ── Wild mountain root at the fir edges, summer ice from the breathing talus, pale amethyst,
			// fir resin, and the goblin's dropped club. LATER (I7): the club only at a Wrestling Stone (one a scene), the root ≤ 3 nodes,
			// the ice at the Summer-ice Slopes; Summer Ice needs breathable air (a site kind's air rule).
			new GatheringResource { Node = "Mountain Root", Item = "Mountain Root", Family = GatheringFamily.Herb, Tier = 4, Visual = "Detail_HerbMountainRoot",
				Tint = new Color(1.0f, 0.9f, 0.8f), Size = 0.6f, MaxUses = 1, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Scree" }, Description = "A forked wild root shaped like a little man, worth a farm to the physicians below." },
			new GatheringResource { Node = "Summer Ice", Item = "Summer Ice", Family = GatheringFamily.Crystal, Tier = 3, Visual = "IceBoulder_Rounded",
				Tint = new Color(0.86f, 0.94f, 1.0f), Size = 1.1f, MaxUses = 2, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Scree" }, Description = "Ice that grows inside the talus in high summer, cut out and carried down for the physicians." },
			new GatheringResource { Node = "Eonyang Amethyst", Item = "Eonyang Amethyst", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Formation_Granodiorite_Block_0",
				Tint = new Color(0.86f, 0.78f, 1.0f), Size = 1.4f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Scree" }, Description = "Pale lilac amethyst points from pockets in the granodiorite." },
			new GatheringResource { Node = "Fir Resin", Item = "Fir Resin", Family = GatheringFamily.Herb, Tier = 1, Visual = "Tree_KoreanFir",
				Tint = new Color(1.0f, 0.95f, 0.85f), Size = 0.0f, Footprint = 0.15f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Scree" }, Description = "Clear fragrant resin bled from the fir bark, for lamps and wounds." },
			new GatheringResource { Node = "Goblin's Club", Item = "Goblin's Club", Family = GatheringFamily.Wood, Tier = 4, Visual = "Deadwood_Stump_Broken",
				Tint = new Color(0.9f, 0.82f, 0.72f), Size = 0.8f, MaxUses = 1, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Scree" }, Description = "A knobbed club left by a goblin at dawn; whoever holds it hears it ask to go home." },
			// ── Scrubland (biome round, 2026-10-26) ── The canal builders' harvest: saguaro fruit, mesquite pods, cholla buds, roasted agave
			// hearts, chia, creosote resin and desert tobacco, the cereus tuber dug on the bloom night, agave fibre, carrizo cane and canal
			// cotton, ironwood, red ochre, caliche nodules, peridot and sunshell. LATER (I9b P29): the fruit and buds only in their season.
			new GatheringResource { Node = "Saguaro Fruit", Item = "Saguaro Fruit", Family = GatheringFamily.Herb, Tier = 1, Visual = "Tree_Saguaro",
				Tint = new Color(1.0f, 0.82f, 0.78f), Size = 0f, Footprint = 0.15f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Scrubland" }, Description = "Red split fruit knocked down from the saguaro crowns with a rib pole in early summer." },
			new GatheringResource { Node = "Mesquite Pods", Item = "Mesquite Pods", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_DebrisMesquitePods",
				Tint = new Color(1.0f, 0.95f, 0.82f), Size = 1.0f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Scrubland" }, Description = "Sweet tan pods gathered under the bosque, ground into meal." },
			new GatheringResource { Node = "Cholla Buds", Item = "Cholla Buds", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_ChollaTeddybear",
				Tint = new Color(1.0f, 0.95f, 0.85f), Size = 0.9f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Scrubland" }, Description = "Spring flower buds twisted off the cholla with tongs and pit-roasted." },
			new GatheringResource { Node = "Agave Heart", Item = "Roasted Agave Heart", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_AgaveHohokam",
				Tint = new Color(0.92f, 1.0f, 0.92f), Size = 1.0f, MaxUses = 2, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Scrubland" }, Description = "The hearts of the field agaves, roasted a day and a night in the rock pits." },
			new GatheringResource { Node = "Chia Patch", Item = "Chia Seed", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_HerbChia",
				Tint = new Color(0.9f, 0.88f, 1.0f), Size = 0.7f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Scrubland" }, Description = "Tiny grey seeds beaten from the chia whorls into a basket." },
			new GatheringResource { Node = "Creosote Resin", Item = "Creosote Resin", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_Bush_Creosote",
				Tint = new Color(0.92f, 1.0f, 0.82f), Size = 0f, Footprint = 0.15f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Scrubland" }, Description = "Lac and resin scraped from the creosote, for glue, mending and fevers." },
			new GatheringResource { Node = "Desert Tobacco", Item = "Desert Tobacco", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_HerbDesertTobacco",
				Tint = new Color(0.95f, 1.0f, 0.9f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Scrubland" }, Description = "Sticky grey leaves from the rock crevices, dried for the pipe." },
			new GatheringResource { Node = "Moonbloom Tuber", Item = "Moonbloom Tuber", Family = GatheringFamily.Herb, Tier = 3, Visual = "Detail_QueenOfTheNight",
				Tint = new Color(0.98f, 0.96f, 1.0f), Size = 0.7f, MaxUses = 1, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Scrubland" }, Description = "The great tuber of the queen of the night, dug only where it bloomed." },
			new GatheringResource { Node = "Agave Fibre", Item = "Agave Fibre", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_AgaveHohokam",
				Tint = new Color(0.95f, 0.95f, 0.85f), Size = 1.0f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Scrubland" }, Description = "Leaf fibre beaten from the agave for cord, nets and sandals." },
			new GatheringResource { Node = "Carrizo Cane", Item = "Carrizo Cane", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_ReedsCarrizo",
				Tint = new Color(1.0f, 0.95f, 0.85f), Size = 1.0f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Scrubland" }, Description = "Straight canes cut from the canal banks for mats, arrows and roofs." },
			new GatheringResource { Node = "Cotton Boll", Item = "Cotton Boll", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_FlowersCottonCanal",
				Tint = new Color(1.0f, 1.0f, 0.96f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Scrubland" }, Description = "White bolls picked from the canal fields for spinning." },
			new GatheringResource { Node = "Desert Ironwood", Item = "Ironwood", Family = GatheringFamily.Wood, Tier = 2, Visual = "Tree_Ironwood",
				Tint = new Color(0.86f, 0.84f, 0.82f), Size = 0f, Footprint = 0.18f, MaxUses = 4, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Scrubland" }, Description = "Iron-dark heartwood so dense it sinks, from a tree along the washes." },
			new GatheringResource { Node = "Red Ochre", Item = "Red Ochre", Family = GatheringFamily.Ore, Tier = 1, Visual = "Boulder_Gneiss_Round",
				Tint = new Color(0.86f, 0.42f, 0.32f), Size = 1.4f, MaxUses = 4, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Scrubland" }, Description = "Hematite earth dug from the ochre pits, for paint and pottery slip." },
			new GatheringResource { Node = "Caliche Nodule", Item = "Caliche Nodule", Family = GatheringFamily.Ore, Tier = 1, Visual = "Detail_Pebbles_Caliche",
				Tint = new Color(0.96f, 0.94f, 0.88f), Size = 1.3f, MaxUses = 4, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Scrubland" }, Description = "Knobby white lime nodules from the benches, burnt for plaster." },
			new GatheringResource { Node = "Peridot Bomb", Item = "Peridot", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Formation_Basalt_Knob_0",
				Tint = new Color(0.62f, 0.86f, 0.32f), Size = 1.4f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Scrubland" }, Description = "Olive-green glassy grains cracked out of black basalt bombs on the malpaís." },
			new GatheringResource { Node = "Sunshell Scatter", Item = "Sunshell", Family = GatheringFamily.Crystal, Tier = 3, Visual = "Formation_Caliche_Plate_0",
				Tint = new Color(1.0f, 0.86f, 0.6f), Size = 1.3f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Scrubland" }, Description = "An iridescent spiral shell-crystal that keeps the day's light for a while after sunset." },
			// ── Savanna (biome round, 2026-10-26) ── The plateau's gold, copper, iron and soapstone, the baobab's fruit and bast, marula,
			// mopane worms, thatching grass, bowstring hemp, resurrection leaf, leadwood charcoal and the three crystals. LATER (I9b P29):
			// mopane worms only early in the wet season.
			new GatheringResource { Node = "Reef Gold", Item = "Reef Gold", Family = GatheringFamily.Ore, Tier = 3, Visual = "Formation_Quartzite_Wedge_0",
				Tint = new Color(1.0f, 0.86f, 0.42f), Size = 1.7f, MaxUses = 3, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Savanna" }, Description = "Gold in the white quartz reefs, fire-set and crushed on the granite slabs." },
			new GatheringResource { Node = "Alluvial Gold", Item = "Gold Dust", Family = GatheringFamily.Ore, Tier = 2, Visual = "Detail_Pebbles_Quartzite",
				Tint = new Color(1.0f, 0.88f, 0.5f), Size = 1.2f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Savanna" }, Description = "Gold dust panned from the sand rivers into a reed quill." },
			new GatheringResource { Node = "Malachite Copper", Item = "Copper Ore", Family = GatheringFamily.Ore, Tier = 2, Visual = "Formation_Schist_Lump_0",
				Tint = new Color(0.5f, 0.86f, 0.62f), Size = 1.6f, MaxUses = 4, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Savanna" }, Description = "Green-stained copper ore from the greenstone belts, cast into cross ingots." },
			new GatheringResource { Node = "Ironstone", Item = "Ironstone", Family = GatheringFamily.Ore, Tier = 1, Visual = "Boulder_Basalt_Round",
				Tint = new Color(0.7f, 0.36f, 0.24f), Size = 1.5f, MaxUses = 4, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Savanna" }, Description = "Ferricrete pellets and ironstone from the red earth, for the clay furnaces." },
			new GatheringResource { Node = "Soapstone", Item = "Soapstone", Family = GatheringFamily.Ore, Tier = 2, Visual = "Boulder_Schist_Round",
				Tint = new Color(0.72f, 0.82f, 0.74f), Size = 1.5f, MaxUses = 3, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Savanna" }, Description = "Soft grey-green stone the carvers turn into bowls and birds." },
			new GatheringResource { Node = "Baobab Fruit", Item = "Baobab Fruit", Family = GatheringFamily.Herb, Tier = 1, Visual = "Tree_BaobabMapungubwe",
				Tint = new Color(1.0f, 0.96f, 0.86f), Size = 0f, Footprint = 0.3f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Savanna" }, Description = "Velvet pods of tart chalky pulp from the baobab." },
			new GatheringResource { Node = "Baobab Bast", Item = "Baobab Bast", Family = GatheringFamily.Herb, Tier = 2, Visual = "Tree_BaobabMapungubwe",
				Tint = new Color(0.96f, 0.9f, 0.82f), Size = 0f, Footprint = 0.3f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Savanna" }, Description = "Inner bark stripped from the baobab and beaten into cord and bark cloth." },
			new GatheringResource { Node = "Marula Fruit", Item = "Marula Fruit", Family = GatheringFamily.Herb, Tier = 2, Visual = "Tree_Marula",
				Tint = new Color(1.0f, 0.95f, 0.7f), Size = 0f, Footprint = 0.2f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Savanna" }, Description = "Yellow plums fallen under the marula, for the year's best brew." },
			new GatheringResource { Node = "Mopane Worms", Item = "Mopane Worms", Family = GatheringFamily.Herb, Tier = 1, Visual = "Tree_Mopane",
				Tint = new Color(0.95f, 0.9f, 0.86f), Size = 0f, Footprint = 0.15f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Savanna" }, Description = "Fat caterpillars picked from the mopane leaves, squeezed and sun-dried." },
			new GatheringResource { Node = "Thatchgrass", Item = "Thatchgrass", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_GrassThatch",
				Tint = new Color(1.0f, 0.95f, 0.8f), Size = 1.0f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Savanna" }, Description = "Head-high thatching grass cut and bound in the dry season." },
			new GatheringResource { Node = "Bowstring Hemp", Item = "Bowstring Hemp", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_Sansevieria",
				Tint = new Color(0.9f, 1.0f, 0.9f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Savanna" }, Description = "Banded sword leaves scraped for bowstrings and cord." },
			new GatheringResource { Node = "Resurrection Leaf", Item = "Resurrection Leaf", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_Myrothamnus",
				Tint = new Color(0.95f, 1.0f, 0.88f), Size = 0.7f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Savanna" }, Description = "Dead-looking fans from the granite cracks that green in water, for the healers' tea." },
			new GatheringResource { Node = "Leadwood Charcoal", Item = "Leadwood Charcoal", Family = GatheringFamily.Wood, Tier = 2, Visual = "Tree_LeadwoodDead",
				Tint = new Color(0.82f, 0.82f, 0.8f), Size = 0f, Footprint = 0.18f, MaxUses = 4, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Savanna" }, Description = "Charcoal burnt from leadwood that stood dead for a century, the smelters' fuel." },
			new GatheringResource { Node = "Reefgold Crystal", Item = "Reefgold Crystal", Family = GatheringFamily.Crystal, Tier = 3, Visual = "Formation_Quartzite_Block_0",
				Tint = new Color(1.0f, 0.9f, 0.6f), Size = 1.5f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Savanna" }, Description = "Milky quartz prisms with leafy native gold on their faces." },
			new GatheringResource { Node = "Emerald Prism", Item = "Emerald", Family = GatheringFamily.Crystal, Tier = 4, Visual = "Formation_Schist_Ridge_0",
				Tint = new Color(0.3f, 0.9f, 0.5f), Size = 1.4f, MaxUses = 2, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Savanna" }, Description = "Deep green hexagonal prisms in black schist." },
			new GatheringResource { Node = "Rainglass", Item = "Rainglass", Family = GatheringFamily.Crystal, Tier = 4, Visual = "Formation_Granite_Corestone_0",
				Tint = new Color(0.6f, 0.95f, 0.85f), Size = 1.4f, MaxUses = 2, GatherSeconds = 4.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Savanna" }, Description = "Blue-green prisms that brighten on the nights before rain." },
			// ── Grassland (biome round, 2026-10-26) ── The puszta's own: chamomile, soda bloom scraped from the szik, thatching reed, the
			// wild pear's fall, steppe peony root, field-mushroom rings, and the Turul's feather. LATER (I7): the feather only at the Turul
			// shrines and kunhalom tops; a rare herb node on the grassland stands in.
			new GatheringResource { Node = "Chamomile Patch", Item = "Chamomile", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_HerbChamomile",
				Tint = new Color(1.0f, 0.98f, 0.9f), Size = 0.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Grassland" }, Description = "Puszta chamomile, white rays round gold domes: a tea for sleep and a wash for wounds." },
			new GatheringResource { Node = "Soda Bloom", Item = "Natron", Family = GatheringFamily.Ore, Tier = 1, Visual = "Detail_ArtemisiaSalt",
				Tint = new Color(1.0f, 1.0f, 0.96f), Size = 0.8f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Grassland" }, Description = "White soda swept from a szik pan at dawn, for soap, glass and curing hides." },
			new GatheringResource { Node = "Thatching Reed", Item = "Thatching Reed", Family = GatheringFamily.Wood, Tier = 1, Visual = "Detail_ReedsThatch",
				Tint = new Color(1.0f, 0.95f, 0.82f), Size = 1.6f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 2, MaxAmount = 4,
				Biomes = new[] { "Grassland" }, Description = "Winter-cut reed from the soda-lake fringe, the roof of every tanya." },
			new GatheringResource { Node = "Wild Pear Fall", Item = "Wild Pear", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_DebrisSteppe",
				Tint = new Color(0.86f, 0.9f, 0.5f), Size = 0.7f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Grassland" }, Description = "Hard little pears fallen under the lone tree, sweet once bletted." },
			new GatheringResource { Node = "Steppe Peony Root", Item = "Peony Root", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_FlowersPeony",
				Tint = new Color(1.0f, 0.82f, 0.82f), Size = 0.6f, MaxUses = 2, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Grassland" }, Description = "The crimson peony's root from the kurgan slopes, against fits and fevers." },
			new GatheringResource { Node = "Field Mushroom Ring", Item = "Field Mushroom", Family = GatheringFamily.Fungus, Tier = 1, Visual = "Detail_MushroomField",
				Tint = new Color(1.0f, 0.97f, 0.92f), Size = 0.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 2, MaxAmount = 4,
				Biomes = new[] { "Grassland" }, Description = "White field mushrooms in a ring on the pasture after rain." },
			new GatheringResource { Node = "Turul Feather", Item = "Turul Feather", Family = GatheringFamily.Herb, Tier = 3, Visual = "Detail_GrassGoldbeard",
				Tint = new Color(1.0f, 0.82f, 0.36f), Size = 0.8f, MaxUses = 1, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Grassland" }, Description = "A golden flight feather of the great falcon, found at dawn on a mound top." },
			// ── Plains (biome round, 2026-10-26) ── The prairie's own: prairie turnip, coneflower root, bergamot, groundnut, the bowyer's
			// Osage stave, cottonwood driftwood, the buffalo jump's bone bed, red ochre, rose rocks, thunder-glass, sunspoke. LATER (I7):
			// the bone bed waits for a site kind that gathers Bone (FamiliesFor); thunder-glass only round the thunder-stones.
			new GatheringResource { Node = "Prairie Turnip", Item = "Prairie Turnip", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_HerbPrairieTurnip",
				Tint = new Color(0.9f, 0.88f, 1.0f), Size = 0.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Plains" }, Description = "The starchy root under a hairy blue-flowered herb, dried in braids for winter." },
			new GatheringResource { Node = "Coneflower Root", Item = "Coneflower Root", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_FlowersConeflower",
				Tint = new Color(1.0f, 0.86f, 0.94f), Size = 0.6f, MaxUses = 2, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Plains" }, Description = "A purple coneflower's numbing root, chewed for toothache and snakebite." },
			new GatheringResource { Node = "Wild Bergamot", Item = "Wild Bergamot", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_HerbBergamot",
				Tint = new Color(0.94f, 0.9f, 1.0f), Size = 0.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Plains" }, Description = "Lavender pompoms with a sharp mint smell, for colds and cleansing smoke." },
			new GatheringResource { Node = "Groundnut Tubers", Item = "Groundnut", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_HerbPrairieTurnip",
				Tint = new Color(0.86f, 0.72f, 0.7f), Size = 0.5f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 2, MaxAmount = 4,
				Biomes = new[] { "Plains" }, Description = "A string of nutty tubers under a twining vine in the draws." },
			new GatheringResource { Node = "Osage Bow-stave", Item = "Osage Stave", Family = GatheringFamily.Wood, Tier = 3, Visual = "Tree_OsageOrange",
				Tint = new Color(1.0f, 0.82f, 0.5f), Size = 0f, Footprint = 0.25f, MaxUses = 2, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Plains" }, Description = "Golden heartwood from the bowyer's tree, the finest bow wood on the plains." },
			new GatheringResource { Node = "Cottonwood Driftwood", Item = "Cottonwood Driftwood", Family = GatheringFamily.Wood, Tier = 1, Visual = "Deadwood_FallenLog_Cottonwood",
				Tint = new Color(1.0f, 0.98f, 0.94f), Size = 2f, MaxUses = 3, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Plains" }, Description = "Bleached cottonwood drifted onto a sandbar, light and dry." },
			new GatheringResource { Node = "Bison Bone Bed", Item = "Bison Bone", Family = GatheringFamily.Bone, Tier = 2, Visual = "Detail_DebrisBison",
				Tint = new Color(1.0f, 0.96f, 0.86f), Size = 1.2f, MaxUses = 3, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Plains" }, Description = "Bleached bones at the foot of a buffalo jump, for tools, glue and charms." },
			new GatheringResource { Node = "Red Ochre Pit", Item = "Red Ochre", Family = GatheringFamily.Ore, Tier = 1, Visual = "Boulder_Redbed_Round",
				Tint = new Color(1.0f, 0.62f, 0.5f), Size = 1.4f, MaxUses = 4, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Plains" }, Description = "Soft red earth dug from the red beds, the paint of lodges, shields and the dead." },
			new GatheringResource { Node = "Rose Rock Pocket", Item = "Rose Rock", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Detail_Pebbles_Redbed",
				Tint = new Color(0.9f, 0.6f, 0.5f), Size = 0.8f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Plains" }, Description = "Little reddish-brown barite roses weathered out of the red sand." },
			new GatheringResource { Node = "Thunder-glass", Item = "Thunder-glass", Family = GatheringFamily.Crystal, Tier = 3, Visual = "Boulder_Redbed_Slab",
				Tint = new Color(0.78f, 0.62f, 1.0f), Size = 1.0f, MaxUses = 1, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Plains" }, Description = "A root of glass where lightning went into the ground, still faintly violet." },
			new GatheringResource { Node = "Sunspoke Cluster", Item = "Sunspoke", Family = GatheringFamily.Crystal, Tier = 4, Visual = "Formation_Quartzite_Block_0",
				Tint = new Color(1.0f, 0.86f, 0.42f), Size = 1.4f, MaxUses = 2, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Plains" }, Description = "Clear quartz shot with golden needles like wheel spokes, warm at dawn and dusk." },
			// ── Steppe (biome round, 2026-10-26) ── White-earth wormwood and the homing wormwood, chiy stems, manna lichen, saxaul wood, sor
			// salt, steppe copper, dioptase, wolf's-eye, saiga horn. LATER (I7): the homing wormwood near the Singing Tomb and Wolf Ridge,
			// saiga horn once a site kind gathers Bone (FamiliesFor), copper and dioptase at the copper pits.
			new GatheringResource { Node = "White-earth Wormwood", Item = "Wormwood", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_WormwoodWhiteEarth",
				Tint = new Color(0.9f, 0.94f, 0.88f), Size = 0.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Steppe" }, Description = "Bitter silver wormwood, THE smell of the steppe: smoke for the yurt, a draught against worms." },
			new GatheringResource { Node = "Homing Wormwood", Item = "Homing Wormwood", Family = GatheringFamily.Herb, Tier = 3, Visual = "Detail_WormwoodWhiteEarth",
				Tint = new Color(0.7f, 0.84f, 1.0f), Size = 0.6f, MaxUses = 1, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Steppe" }, Description = "A sprig whose scent calls an exile home across any distance of grass." },
			new GatheringResource { Node = "Chiy Stems", Item = "Chiy Stems", Family = GatheringFamily.Wood, Tier = 1, Visual = "Detail_GrassChiy",
				Tint = new Color(1.0f, 0.96f, 0.88f), Size = 1.6f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 2, MaxAmount = 4,
				Biomes = new[] { "Steppe" }, Description = "Tall chiy stems cut by a sor, for the yurt's screens and mats." },
			new GatheringResource { Node = "Manna Lichen", Item = "Manna Lichen", Family = GatheringFamily.Fungus, Tier = 1, Visual = "Detail_LichenManna",
				Tint = new Color(1.0f, 1.0f, 0.94f), Size = 0.5f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 2, MaxAmount = 4,
				Biomes = new[] { "Steppe" }, Description = "Knobbly grey lichen blown into drifts, the manna that falls from the sky; ground into a flat bread." },
			new GatheringResource { Node = "Saxaul Wood", Item = "Saxaul Wood", Family = GatheringFamily.Wood, Tier = 2, Visual = "Deadwood_Snag_Saxaul",
				Tint = new Color(0.9f, 0.86f, 0.8f), Size = 1.6f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Steppe" }, Description = "Wood so hard it sinks in water and burns like coal." },
			new GatheringResource { Node = "Sor Salt", Item = "Sor Salt", Family = GatheringFamily.Ore, Tier = 1, Visual = "Detail_Biyurgun",
				Tint = new Color(1.0f, 1.0f, 0.98f), Size = 0.8f, MaxUses = 4, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Steppe" }, Description = "White crust broken from a sor's puffy salt." },
			new GatheringResource { Node = "Steppe Copper", Item = "Copper Ore", Family = GatheringFamily.Ore, Tier = 2, Visual = "Boulder_Porphyry_Round",
				Tint = new Color(0.5f, 0.86f, 0.62f), Size = 1.6f, MaxUses = 4, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Steppe" }, Description = "Malachite-green copper ore from the old pits in the porphyry." },
			new GatheringResource { Node = "Dioptase", Item = "Dioptase", Family = GatheringFamily.Crystal, Tier = 3, Visual = "Formation_Porphyry_Knob_0",
				Tint = new Color(0.2f, 0.86f, 0.62f), Size = 1.3f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Steppe" }, Description = "Vivid emerald-green prisms on white quartz in a copper pit's wall." },
			new GatheringResource { Node = "Wolf's-eye", Item = "Wolf's-eye", Family = GatheringFamily.Crystal, Tier = 4, Visual = "Formation_Porphyry_Split_0",
				Tint = new Color(1.0f, 0.78f, 0.42f), Size = 1.2f, MaxUses = 1, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Steppe" }, Description = "A smoky-amber chalcedony nodule with a bright eye that wakes when the wind rises." },
			new GatheringResource { Node = "Saiga Horn", Item = "Saiga Horn", Family = GatheringFamily.Bone, Tier = 2, Visual = "Detail_DebrisSaigaSkull",
				Tint = new Color(1.0f, 0.92f, 0.76f), Size = 0.8f, MaxUses = 2, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Steppe" }, Description = "Amber lyre-shaped horn from a bleached saiga skull." },

			// ── Estuary (biome round, 2026-10-26): the Wadden's harvest; its samphire, mirewort and puffballs went to the other salt and bog biomes. ──
			new GatheringResource { Node = "Glasswort", Item = "Glasswort", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_Glasswort",
				Tint = new Color(0.8f, 0.95f, 0.65f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Estuary" }, Description = "Jointed green marsh-asparagus, pickled for the winter and boiled down to soda for the glassmakers." },
			new GatheringResource { Node = "Mussel Bank", Item = "Blue Mussel", Family = GatheringFamily.Marine, Tier = 1, Visual = "Detail_MusselBank",
				Tint = new Color(0.8f, 0.85f, 1.0f), Size = 1.2f, MaxUses = 4, GatherSeconds = 2.0f, MinAmount = 2, MaxAmount = 4,
				Biomes = new[] { "Estuary" }, Description = "Blue-black mussels clustered on the banks the ebb lays bare." },
			new GatheringResource { Node = "Cockle Bed", Item = "Cockle", Family = GatheringFamily.Marine, Tier = 1, Visual = "Detail_ShellsCockle",
				Tint = new Color(1.0f, 0.95f, 0.88f), Size = 1.0f, MaxUses = 4, GatherSeconds = 2.0f, MinAmount = 2, MaxAmount = 4,
				Biomes = new[] { "Estuary" }, Description = "Ribbed cockles raked from the firm sand of the flats." },
			new GatheringResource { Node = "Lugworm", Item = "Lugworm", Family = GatheringFamily.Marine, Tier = 1, Visual = "Detail_LugwormCasts",
				Tint = new Color(0.9f, 0.82f, 0.7f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Estuary" }, Description = "Fat bait worms dug from beside their coiled casts." },
			new GatheringResource { Node = "Salt Peat", Item = "Salt Peat", Family = GatheringFamily.Ore, Tier = 2, Visual = "Detail_Rocks_Till",
				Tint = new Color(0.45f, 0.38f, 0.3f), Size = 1.2f, MaxUses = 4, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Estuary" }, Description = "Sea-soaked peat dug from the darg pits and burnt in lead pans for its salt." },
			new GatheringResource { Node = "Marsh Clay", Item = "Marsh Clay", Family = GatheringFamily.Ore, Tier = 1, Visual = "Formation_Till_Slump_0",
				Tint = new Color(0.72f, 0.74f, 0.72f), Size = 1.3f, MaxUses = 4, GatherSeconds = 2.5f, MinAmount = 2, MaxAmount = 4,
				Biomes = new[] { "Estuary" }, Description = "Blue-grey brick-earth cut from the creek banks." },
			new GatheringResource { Node = "Sea Lavender", Item = "Sea Lavender", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_FlowersSeaLavender",
				Tint = new Color(0.8f, 0.7f, 1.0f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Estuary" }, Description = "Mauve sprays of the July saltings, dried for a healer's astringent." },
			new GatheringResource { Node = "Scurvy-grass", Item = "Scurvy-grass", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_ScurvyGrass",
				Tint = new Color(0.85f, 1.0f, 0.8f), Size = 0.7f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Estuary" }, Description = "The sailors' sharp green herb, chewed against the long voyage." },
			new GatheringResource { Node = "Sea Wormwood", Item = "Sea Wormwood", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_SeaWormwood",
				Tint = new Color(0.82f, 0.9f, 0.82f), Size = 0.9f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Estuary" }, Description = "Silver high-marsh wormwood, bitter and sovereign against fever." },
			new GatheringResource { Node = "Vivianite", Item = "Vivianite", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.2f, 0.55f, 0.75f), Size = 1.2f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Estuary" }, Description = "Blue-green blades that darken to indigo, found in marsh clay and old bone." },
			new GatheringResource { Node = "Ebbspar", Item = "Ebbspar", Family = GatheringFamily.Crystal, Tier = 4, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.6f, 0.95f, 0.85f), Size = 1.4f, MaxUses = 2, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Estuary" }, Description = "Water-clear rhombs that glow green-grey only while the tide is out." },
			// ── Coral Reef (biome round, 2026-10-26): the atoll shelf's harvest — the masons' coral-stone, the cowrie currency, the black
			// coral, and the island phosphorite and lagoon glass of its caves. Its generic Living Coral went with the generic coral. ──
			new GatheringResource { Node = "Hirigaa Block", Item = "Coral-stone", Family = GatheringFamily.Ore, Tier = 2, Visual = "Detail_PoritesBoulder",
				Tint = new Color(0.94f, 0.9f, 0.78f), Size = 1.2f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Coral Reef" }, Description = "A living Porites boulder, sawn square and carved soft; it hardens in the air and needs no mortar." },
			new GatheringResource { Node = "Money Cowrie", Item = "Boli", Family = GatheringFamily.Marine, Tier = 1, Visual = "Detail_MoneyCowrie",
				Tint = new Color(1.0f, 0.95f, 0.76f), Size = 1.0f, MaxUses = 4, GatherSeconds = 1.5f, MinAmount = 2, MaxAmount = 5,
				Biomes = new[] { "Coral Reef" }, Description = "Glossy yellow-white cowries gathered by the basket on the reef flat: coin from Malé to Aden." },
			new GatheringResource { Node = "Black Coral", Item = "Black Coral", Family = GatheringFamily.Marine, Tier = 3, Visual = "Detail_BlackCoral",
				Tint = new Color(0.3f, 0.26f, 0.22f), Size = 1.4f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Coral Reef" }, Description = "A black-stemmed bush from the drop-off, polished for jewellery and charms against the evil eye." },
			new GatheringResource { Node = "Apatite", Item = "Phosphor-stone", Family = GatheringFamily.Ore, Tier = 2, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.4f, 0.8f, 0.7f), Size = 1.3f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Coral Reef" }, Description = "Sea-green glassy prisms on a cream phosphorite crust, raked from the islands' sea caves." },
			new GatheringResource { Node = "Lagoonglass", Item = "Lagoonglass", Family = GatheringFamily.Crystal, Tier = 3, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.3f, 0.85f, 0.82f), Size = 1.4f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Coral Reef" }, Description = "Turquoise blades that hold the lagoon's light and ripple it on the cave walls." },

			// ── Beach (biome round, 2026-10-26): the amber strand's harvest. Its iron sand and samphire went to the rocky coasts and marshes. ──
			new GatheringResource { Node = "Strand Amber", Item = "Amber", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Detail_AmberDrift",
				Tint = new Color(1.0f, 0.72f, 0.24f), Size = 0.8f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Beach" }, Description = "Honey and cherry pebbles in the wrack after an autumn storm; the reeves hold the right to them." },
			new GatheringResource { Node = "Sea Holly Root", Item = "Eryngo Root", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_FlowersAmberStrand",
				Tint = new Color(0.55f, 0.65f, 0.85f), Size = 0.7f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Beach" }, Description = "The long root of the steel-blue sea holly, candied by the strand folk as a sweet and a tonic." },
			new GatheringResource { Node = "Marram Bundle", Item = "Marram", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_GrassMarram",
				Tint = new Color(0.78f, 0.78f, 0.6f), Size = 1.0f, MaxUses = 4, GatherSeconds = 1.5f, MinAmount = 2, MaxAmount = 4,
				Biomes = new[] { "Beach" }, Description = "Stiff dune grass, twisted into rope and mats; it is a crime to pull it from the foredune." },
			new GatheringResource { Node = "Drift Timber", Item = "Driftwood", Family = GatheringFamily.Wood, Tier = 1, Visual = "Deadwood_Driftwood_Silver",
				Tint = new Color(0.78f, 0.76f, 0.7f), Size = 1.6f, MaxUses = 4, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Beach" }, Description = "Sand-blasted silver logs from the wrack line, fuel and fencing for the whole spit." },
			new GatheringResource { Node = "Sea Grape", Item = "Sea Grape", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_Bush_SeaGrape",
				Tint = new Color(0.55f, 0.25f, 0.4f), Size = 1.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Beach" }, Description = "Purple grape clusters from the windshorn hedge behind the warm strand." },
			new GatheringResource { Node = "Pearlspar", Item = "Pearlspar", Family = GatheringFamily.Crystal, Tier = 3, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.96f, 0.84f, 0.92f), Size = 1.4f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Beach" }, Description = "Nacreous plates in the sea caves, shimmering in pastel pinks and greens." },

			// ── Abyss (biome round, 2026-10-26): the hadal trench's harvest; its manganese nodules, glass sponge and vent clams are the plain's,
			// the deep ocean's and the vents'. LATER (I7, Bone sites): no site kind gathers the Bone family yet; the leviathan fall will. ──
			new GatheringResource { Node = "Seep Mussel Bed", Item = "Brine Pearl", Family = GatheringFamily.Marine, Tier = 3, Visual = "Detail_SeepMussel",
				Tint = new Color(0.7f, 0.62f, 0.46f), Size = 1.2f, MaxUses = 2, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Abyss" }, Description = "Brown mussels crowding a brine lake's shore; a pearl in one shell in a thousand, pickled black." },
			new GatheringResource { Node = "Fire-Ice Crust", Item = "Fire Ice", Family = GatheringFamily.Ore, Tier = 4, Visual = "Detail_HydrateMound",
				Tint = new Color(0.94f, 0.9f, 0.7f), Size = 1.3f, MaxUses = 2, GatherSeconds = 4.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Abyss" }, Description = "Yellow-white ice that burns blue in air, prised from the hydrate mounds." },
			new GatheringResource { Node = "Leviathan Bone", Item = "Leviathan Ivory", Family = GatheringFamily.Bone, Tier = 4, Visual = "Deadwood_LeviathanVertebra",
				Tint = new Color(0.9f, 0.85f, 0.7f), Size = 1.8f, MaxUses = 2, GatherSeconds = 5.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Abyss" }, Description = "Ivory from a leviathan fall, rib and vertebra under a red haze of bone-worms." },
			new GatheringResource { Node = "Lure Bulb", Item = "Lure Bulb", Family = GatheringFamily.Marine, Tier = 4, Visual = "Detail_LurePolyp",
				Tint = new Color(0.4f, 0.65f, 1.0f), Size = 0.8f, MaxUses = 2, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Abyss" }, Description = "A glowing bead cut from the lure polyps; a reagent that holds its blue light for a year." },
			new GatheringResource { Node = "Hadalite", Item = "Hadalite", Family = GatheringFamily.Crystal, Tier = 4, Visual = "Formation_WaterIce_Crystal_0",
				Tint = new Color(0.45f, 0.4f, 1.0f), Size = 1.5f, MaxUses = 2, GatherSeconds = 4.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Abyss" }, Description = "Violet-black prisms with a slow cold heartbeat that no hand can warm." },
			// ── Desert (biome round, 2026-10-26) ── The erg's harvest: sunglass from the glass fields, malachite from the copper mines, Ténéré
			// fossil wood, ostrich shell, colocynth, desert hyacinth, the resurrection plant, desert truffles after rain, desert dates, markh
			// rope fibre, toothbrush sticks and the bones along the ways. (Mineral rows are Ore, food and fibre Herb: the catalogue's families.)
			// LATER (I9b P29): the truffles only after rain, the rose of Jericho opened in water.
			new GatheringResource { Node = "Sunglass Lump", Item = "Sunglass", Family = GatheringFamily.Crystal, Tier = 3, Visual = "Detail_Pebbles_Quartzite",
				Tint = new Color(1.0f, 0.96f, 0.7f), Size = 1.2f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Desert" }, Description = "Pale lemon-green desert glass that drinks the day and glows faintly after dusk." },
			new GatheringResource { Node = "Malachite Crust", Item = "Malachite", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Boulder_Sandstone_Round",
				Tint = new Color(0.5f, 0.86f, 0.62f), Size = 1.5f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Desert" }, Description = "Banded green crusts with azurite spots, scraped from the copper-stained sandstone." },
			new GatheringResource { Node = "Petrified Wood Round", Item = "Petrified Wood", Family = GatheringFamily.Ore, Tier = 2, Visual = "Deadwood_PetrifiedLog_Tenere",
				Tint = new Color(1.0f, 0.92f, 0.82f), Size = 0f, Footprint = 0.2f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Desert" }, Description = "A honey-brown and black agatised round of fossil wood, broken where it lay on the reg." },
			new GatheringResource { Node = "Ostrich Eggshell", Item = "Ostrich Eggshell", Family = GatheringFamily.Ore, Tier = 1, Visual = "Detail_DebrisNeolithic",
				Tint = new Color(1.0f, 0.98f, 0.94f), Size = 1.0f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Desert" }, Description = "Old eggshell from the green Sahara's camps, for beads and water flasks." },
			new GatheringResource { Node = "Colocynth Gourd", Item = "Colocynth Gourd", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_Colocynth",
				Tint = new Color(1.0f, 0.96f, 0.8f), Size = 0.9f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Desert" }, Description = "A striped bitter apple off the runners, a purge and a poison in one." },
			new GatheringResource { Node = "Cistanche Spike", Item = "Cistanche Spike", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_Cistanche",
				Tint = new Color(1.0f, 0.95f, 0.7f), Size = 0.8f, MaxUses = 2, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Desert" }, Description = "The desert hyacinth's yellow club, dug from the sand beside its host shrub." },
			new GatheringResource { Node = "Rose of Jericho", Item = "Rose of Jericho", Family = GatheringFamily.Herb, Tier = 3, Visual = "Detail_RoseOfJericho",
				Tint = new Color(0.95f, 0.95f, 0.9f), Size = 0.7f, MaxUses = 2, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Desert" }, Description = "A dry curled ball that opens green in water: the resurrection draught." },
			new GatheringResource { Node = "Terfez Truffle", Item = "Desert Truffle", Family = GatheringFamily.Fungus, Tier = 1, Visual = "Detail_MushroomCluster",
				Tint = new Color(1.0f, 0.92f, 0.82f), Size = 0.6f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Desert" }, Description = "A pale truffle dug from a cracked sand mound after rain." },
			new GatheringResource { Node = "Balanites Date", Item = "Desert Date", Family = GatheringFamily.Herb, Tier = 1, Visual = "Tree_Balanites",
				Tint = new Color(1.0f, 0.96f, 0.82f), Size = 0f, Footprint = 0.15f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Desert" }, Description = "Yellow-green dates off the thorny tree every caravan steers by." },
			new GatheringResource { Node = "Markh Fibre", Item = "Markh Fibre", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_Bush_Markh",
				Tint = new Color(0.95f, 1.0f, 0.88f), Size = 0f, Footprint = 0.15f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Desert" }, Description = "Rush-like green stems stripped and twisted into rope." },
			new GatheringResource { Node = "Irak Stick", Item = "Irak Stick", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_Bush_Irak",
				Tint = new Color(0.95f, 1.0f, 0.9f), Size = 0f, Footprint = 0.15f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Desert" }, Description = "A chewing-stick cut from the toothbrush tree by the wells." },
			new GatheringResource { Node = "Camel Bone", Item = "Camel Bone", Family = GatheringFamily.Bone, Tier = 1, Visual = "Detail_DebrisBones",
				Tint = new Color(1.0f, 0.98f, 0.94f), Size = 1.0f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Desert" }, Description = "Bleached bone from the caravan ways, the toll of the crossing." },
			// ── Badlands (biome round, 2026-10-26) ── Fire opal from the vesicles, the peris' perilight, obsidian cores, pumice blocks, geven
			// gum, harmal seed, salep tubers, gundelia gum, madder root for red dye, apricots and grapes from the wash plots, and the dovecotes'
			// guano for the vineyards.
			new GatheringResource { Node = "Fire Opal Nodule", Item = "Fire Opal", Family = GatheringFamily.Crystal, Tier = 3, Visual = "Formation_Tuff_Tafoni_0",
				Tint = new Color(1.0f, 0.6f, 0.35f), Size = 1.4f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Badlands" }, Description = "An orange-red jelly nodule cracked from a vesicle, flashing a play of colour." },
			new GatheringResource { Node = "Perilight Cluster", Item = "Perilight", Family = GatheringFamily.Crystal, Tier = 4, Visual = "Formation_Tuff_Chimney_0",
				Tint = new Color(0.8f, 1.0f, 0.6f), Size = 1.4f, MaxUses = 2, GatherSeconds = 4.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Badlands" }, Description = "Water-clear crusts that glow cold green-gold at dusk, where the peris lived." },
			new GatheringResource { Node = "Obsidian Core", Item = "Obsidian Core", Family = GatheringFamily.Ore, Tier = 2, Visual = "Detail_Pebbles_Obsidian",
				Tint = new Color(0.9f, 0.86f, 0.86f), Size = 1.2f, MaxUses = 3, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Badlands" }, Description = "A black glass core from the old knapping floors, mahogany-banded." },
			new GatheringResource { Node = "Pumice Block", Item = "Pumice", Family = GatheringFamily.Ore, Tier = 1, Visual = "Detail_Rocks_Pumice",
				Tint = new Color(1.0f, 1.0f, 0.98f), Size = 1.3f, MaxUses = 4, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Badlands" }, Description = "Light white pumice from the lapilli fans, for scouring and building." },
			new GatheringResource { Node = "Tragacanth Gum", Item = "Tragacanth Gum", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_Bush_Tragacanth",
				Tint = new Color(1.0f, 0.98f, 0.88f), Size = 0f, Footprint = 0.12f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Badlands" }, Description = "Gum bled from the spiny geven cushions, for binders and sweets." },
			new GatheringResource { Node = "Harmal Seed", Item = "Harmal Seed", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_HerbHarmal",
				Tint = new Color(0.95f, 1.0f, 0.9f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Badlands" }, Description = "Syrian rue capsules, strung against the evil eye and burnt as a smoke." },
			new GatheringResource { Node = "Salep Tuber", Item = "Salep", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_HerbSalep",
				Tint = new Color(1.0f, 0.92f, 1.0f), Size = 0.7f, MaxUses = 2, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Badlands" }, Description = "The steppe orchid's tubers, dried and ground for a warm winter drink." },
			new GatheringResource { Node = "Kenger Gum", Item = "Kenger Gum", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_HerbKenger",
				Tint = new Color(0.95f, 0.9f, 0.95f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Badlands" }, Description = "Gum from the gundelia thistle's crown, chewed and traded." },
			new GatheringResource { Node = "Madder Root", Item = "Madder Root", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_HerbHarmal",
				Tint = new Color(1.0f, 0.7f, 0.65f), Size = 0.8f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Badlands" }, Description = "The wild madder's red root, dug for the weavers' dye." },
			new GatheringResource { Node = "Apricot Harvest", Item = "Apricot", Family = GatheringFamily.Herb, Tier = 1, Visual = "Tree_Apricot",
				Tint = new Color(1.0f, 0.88f, 0.7f), Size = 0f, Footprint = 0.15f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Badlands" }, Description = "Orange apricots off the wash orchards, sun-dried on the flat roofs." },
			new GatheringResource { Node = "Grape Cluster", Item = "Grapes", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_CropBushVine",
				Tint = new Color(0.9f, 0.82f, 1.0f), Size = 0.9f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Badlands" }, Description = "Purple-black clusters off the ground-trained bush vines, for the press." },
			new GatheringResource { Node = "Pigeon Guano", Item = "Pigeon Guano", Family = GatheringFamily.Ore, Tier = 1, Visual = "Detail_DebrisPigeon",
				Tint = new Color(1.0f, 1.0f, 0.96f), Size = 1.0f, MaxUses = 4, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Badlands" }, Description = "Guano swept from the dovecote ledges: the vineyards' fertiliser." },
			// ── Salt Flat (biome round, 2026-10-26) ── Amole bars from the cuts, brine salt from the pans, sylvite and halite from the caves,
			// hopper crystals off the brine rims, saltwort ash for glass and soap, myrrh, dobera seed (the famine food), desert-cotton wool,
			// aloe sap and the fantasy embersalt.
			new GatheringResource { Node = "Amole Bar", Item = "Amole Bar", Family = GatheringFamily.Ore, Tier = 2, Visual = "Formation_Saltrock_Bedded_0",
				Tint = new Color(1.0f, 1.0f, 0.98f), Size = 1.5f, MaxUses = 4, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Salt Flat" }, Description = "A uniform bar of bedded salt levered and axed from the crust: coin across the Horn." },
			new GatheringResource { Node = "Brine Salt", Item = "Brine Salt", Family = GatheringFamily.Ore, Tier = 1, Visual = "Detail_DebrisSaltHopper",
				Tint = new Color(1.0f, 1.0f, 1.0f), Size = 1.0f, MaxUses = 4, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Salt Flat" }, Description = "White salt raked from the brine pans and the drying rims." },
			new GatheringResource { Node = "Sylvite", Item = "Sylvite", Family = GatheringFamily.Ore, Tier = 3, Visual = "Formation_Saltrock_Fluted_0",
				Tint = new Color(1.0f, 0.62f, 0.5f), Size = 1.4f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Salt Flat" }, Description = "Brick-red potash salt from the galleries, a reagent prized by alchemists." },
			new GatheringResource { Node = "Hopper Crystal", Item = "Hopper Crystal", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Detail_DebrisSaltHopper",
				Tint = new Color(0.96f, 0.98f, 1.0f), Size = 0.9f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Salt Flat" }, Description = "A hollow stepped cube of halite grown on a brine pool's rim." },
			new GatheringResource { Node = "Halite Crystal", Item = "Halite Crystal", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Formation_Halite_Pinnacles_0",
				Tint = new Color(1.0f, 0.95f, 0.95f), Size = 1.3f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Salt Flat" }, Description = "Water-clear to pale pink salt cubes from the salt caves." },
			new GatheringResource { Node = "Salsola Ash", Item = "Salsola Ash", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_Bush_Salsola",
				Tint = new Color(0.95f, 0.92f, 0.92f), Size = 0f, Footprint = 0.12f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Salt Flat" }, Description = "Saltwort burnt to a grey soda ash for glass and soap." },
			new GatheringResource { Node = "Commiphora Resin", Item = "Myrrh", Family = GatheringFamily.Herb, Tier = 3, Visual = "Detail_Bush_Commiphora",
				Tint = new Color(1.0f, 0.9f, 0.7f), Size = 0f, Footprint = 0.12f, MaxUses = 2, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Salt Flat" }, Description = "Bitter red-brown myrrh tears bled from the peeling stems on the lava rim." },
			new GatheringResource { Node = "Dobera Seeds", Item = "Dobera Seeds", Family = GatheringFamily.Herb, Tier = 2, Visual = "Tree_Dobera",
				Tint = new Color(0.95f, 0.9f, 1.0f), Size = 0f, Footprint = 0.18f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Salt Flat" }, Description = "The dagu tree's purple drupes, soaked and boiled: the herders' famine food." },
			new GatheringResource { Node = "Aerva Wool", Item = "Aerva Wool", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_Aerva",
				Tint = new Color(1.0f, 1.0f, 0.98f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Salt Flat" }, Description = "White wool from the desert cotton's spikes, for stuffing and wicks." },
			new GatheringResource { Node = "Aloe Sap", Item = "Aloe Sap", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_HerbAloe",
				Tint = new Color(0.95f, 1.0f, 0.92f), Size = 0.9f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Salt Flat" }, Description = "Bitter sap from the escarpment aloes, a salve for burns and the glare." },
			new GatheringResource { Node = "Embersalt", Item = "Embersalt", Family = GatheringFamily.Crystal, Tier = 4, Visual = "Formation_Saltrock_Diapir_0",
				Tint = new Color(1.0f, 0.75f, 0.6f), Size = 1.4f, MaxUses = 2, GatherSeconds = 4.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Salt Flat" }, Description = "Amber-rose salt cubes that glow warm from dusk, said to hold the noon." },
			// ── Hills (biome round, 2026-10-26) ── Saffron from the terraces, cork, wild lavender, capers from the walls, mica books for
			// lantern panes, heath-briar burl, and the moura's golden comb. LATER (I7): the comb only at the mouras' springs and the
			// castros, at dawn; a rare herb node stands in.
			new GatheringResource { Node = "Saffron Crocus Patch", Item = "Saffron", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_FlowersSaffron",
				Tint = new Color(0.9f, 0.82f, 1.0f), Size = 0.5f, MaxUses = 2, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Hills" }, Description = "Three red threads from each lilac crocus on the terraces, worth their weight in silver." },
			new GatheringResource { Node = "Cork Bark", Item = "Cork", Family = GatheringFamily.Wood, Tier = 1, Visual = "Tree_CorkOak",
				Tint = new Color(1.0f, 0.86f, 0.72f), Size = 0f, Footprint = 0.25f, MaxUses = 2, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Hills" }, Description = "Curved planks of cork stripped from the oak every ninth summer." },
			new GatheringResource { Node = "Wild Lavender", Item = "Lavender", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_HerbLavender",
				Tint = new Color(0.9f, 0.86f, 1.0f), Size = 0.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Hills" }, Description = "Purple spikes from the fells, for oil, linen chests and the fever." },
			new GatheringResource { Node = "Caper Cluster", Item = "Capers", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_HerbRue",
				Tint = new Color(0.8f, 0.94f, 0.8f), Size = 0.5f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 2, MaxAmount = 4,
				Biomes = new[] { "Hills" }, Description = "Caper buds picked from a terrace wall's cracks, salted in jars." },
			new GatheringResource { Node = "Mica Sheets", Item = "Mica", Family = GatheringFamily.Crystal, Tier = 1, Visual = "Formation_Schist_Flags_0",
				Tint = new Color(0.96f, 0.94f, 0.86f), Size = 1.3f, MaxUses = 3, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Hills" }, Description = "Books of clear muscovite split from the schist, for lantern panes." },
			new GatheringResource { Node = "Heath Briar", Item = "Briar Burl", Family = GatheringFamily.Wood, Tier = 2, Visual = ProceduralArtCatalogue.BushPrefix + "TreeHeath",
				Tint = new Color(0.9f, 0.76f, 0.62f), Size = 1.6f, MaxUses = 2, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Hills" }, Description = "A tree-heath's root burl, close-grained and fire-proof, for carving." },
			new GatheringResource { Node = "Moura's Comb", Item = "Moura's Comb", Family = GatheringFamily.Herb, Tier = 3, Visual = "Detail_FlowersAsphodel",
				Tint = new Color(1.0f, 0.84f, 0.4f), Size = 0.6f, MaxUses = 1, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Hills" }, Description = "A golden comb left on a spring's stone at midsummer dawn; its owner will come for it." },

			// ── Wetlands (biome round, 2026-10-26): the Batiha's harvest; its mirewort and puffballs went to the bogs. ──
			new GatheringResource { Node = "Qasab Sheaf", Item = "Qasab Reed", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_ReedSheaves",
				Tint = new Color(1.0f, 0.92f, 0.7f), Size = 1.2f, MaxUses = 4, GatherSeconds = 2.0f, MinAmount = 2, MaxAmount = 4,
				Biomes = new[] { "Wetlands" }, Description = "Cut stems of the giant reed, bundled for house-ribs, mats and boats." },
			new GatheringResource { Node = "Bardi Pollen", Item = "Bardi Pollen", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_ReedsBardi",
				Tint = new Color(1.0f, 0.9f, 0.5f), Size = 1.0f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Wetlands" }, Description = "Golden cattail pollen, baked into the marsh-folk's sweet cakes." },
			new GatheringResource { Node = "Licorice Root", Item = "Licorice Root", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_Licorice",
				Tint = new Color(0.8f, 0.75f, 1.0f), Size = 0.9f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Wetlands" }, Description = "A sweet yellow root dug from the levees, steeped for the cough." },
			new GatheringResource { Node = "Date Cluster", Item = "Date", Family = GatheringFamily.Herb, Tier = 1, Visual = "Tree_DatePalm",
				Tint = new Color(1.0f, 0.8f, 0.55f), Size = 0.8f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 2, MaxAmount = 4,
				Biomes = new[] { "Wetlands" }, Description = "Orange date clusters cut from the palms of the old islands." },
			new GatheringResource { Node = "Bitumen Seep", Item = "Bitumen", Family = GatheringFamily.Ore, Tier = 2, Visual = "Detail_Rocks_Basalt",
				Tint = new Color(0.25f, 0.22f, 0.2f), Size = 1.2f, MaxUses = 4, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Wetlands" }, Description = "Black tar skimmed from the seeps, to caulk canoes and set bricks." },
			new GatheringResource { Node = "Carnelian Nodule", Item = "Carnelian", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(1.0f, 0.45f, 0.25f), Size = 1.2f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Wetlands" }, Description = "Waxy orange-red chalcedony nodules from the caves and the tell sherd-fields." },
			new GatheringResource { Node = "Abzulite Shard", Item = "Abzulite", Family = GatheringFamily.Crystal, Tier = 4, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.4f, 0.75f, 1.0f), Size = 1.4f, MaxUses = 2, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Wetlands" }, Description = "Deep sweet-water-blue prisms with a slow inner tide of light." },
			new GatheringResource { Node = "Plant of Youth", Item = "Plant of Youth", Family = GatheringFamily.Herb, Tier = 4, Visual = "Detail_FlowersButomus",
				Tint = new Color(0.7f, 0.85f, 1.0f), Size = 0.9f, MaxUses = 1, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Wetlands" }, Description = "A thorned blue-white flower said to bloom only under the sweet-water springs." },

			// ── Mangrove (biome round, 2026-10-26): the mangal's harvest; its mirewort, bloodroot, samphire and puffballs went to other biomes. ──
			new GatheringResource { Node = "Boriti Poles", Item = "Boriti Pole", Family = GatheringFamily.Wood, Tier = 2, Visual = "Deadwood_PoleStumps",
				Tint = new Color(1.0f, 0.7f, 0.6f), Size = 1.2f, MaxUses = 4, GatherSeconds = 3.0f, MinAmount = 2, MaxAmount = 4,
				Biomes = new[] { "Mangrove" }, Description = "Straight red-hearted mangrove poles, the coast's great export, cut for the roofs of stone towns." },
			new GatheringResource { Node = "Mangrove Bark", Item = "Tannin Bark", Family = GatheringFamily.Herb, Tier = 1, Visual = "Tree_Mkandaa",
				Tint = new Color(1.0f, 0.65f, 0.4f), Size = 1.0f, MaxUses = 3, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Mangrove" }, Description = "Orange tannin bark of the ceriops, boiled for dye and for curing hides." },
			new GatheringResource { Node = "Mud Crab", Item = "Mud Crab", Family = GatheringFamily.Marine, Tier = 1, Visual = "Detail_CrabChimneys",
				Tint = new Color(0.8f, 0.7f, 0.6f), Size = 0.9f, MaxUses = 3, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Mangrove" }, Description = "Heavy-clawed mud crabs hauled from their burrows beneath the roots." },
			new GatheringResource { Node = "Rock Oysters", Item = "Rock Oyster", Family = GatheringFamily.Marine, Tier = 1, Visual = "Detail_RootOysters",
				Tint = new Color(0.9f, 0.88f, 0.8f), Size = 1.1f, MaxUses = 4, GatherSeconds = 2.0f, MinAmount = 2, MaxAmount = 4,
				Biomes = new[] { "Mangrove" }, Description = "Small crusted oysters prised from the prop roots at low water." },
			new GatheringResource { Node = "Mud Whelk", Item = "Mud Whelk", Family = GatheringFamily.Marine, Tier = 1, Visual = "Detail_MudWhelks",
				Tint = new Color(0.7f, 0.6f, 0.5f), Size = 0.9f, MaxUses = 4, GatherSeconds = 1.5f, MinAmount = 2, MaxAmount = 4,
				Biomes = new[] { "Mangrove" }, Description = "Spired dark whelks, boiled in their hundreds in the creek villages." },
			new GatheringResource { Node = "Sandfish", Item = "Sandfish", Family = GatheringFamily.Marine, Tier = 2, Visual = "Detail_Sandfish",
				Tint = new Color(0.8f, 0.78f, 0.7f), Size = 0.9f, MaxUses = 3, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Mangrove" }, Description = "Grey-banded sea cucumbers from the lagoon floor, dried for the far markets." },
			new GatheringResource { Node = "Coral Lime", Item = "Coral Lime", Family = GatheringFamily.Ore, Tier = 1, Visual = "Formation_CoralRag_Block_0",
				Tint = new Color(0.9f, 0.88f, 0.8f), Size = 1.3f, MaxUses = 4, GatherSeconds = 3.0f, MinAmount = 2, MaxAmount = 4,
				Biomes = new[] { "Mangrove" }, Description = "Pitted coral rag burnt in the lime kilns on mangrove firewood." },
			new GatheringResource { Node = "Sandarusi Copal", Item = "Copal", Family = GatheringFamily.Wood, Tier = 3, Visual = "Deadwood_Snag_Mangrove",
				Tint = new Color(1.0f, 0.82f, 0.45f), Size = 1.0f, MaxUses = 2, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Mangrove" }, Description = "Amber resin dug from the landward fringe, burnt in the stone towns' halls." },
			new GatheringResource { Node = "Mangrove Fern", Item = "Mangrove Fern", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_MangroveFern",
				Tint = new Color(0.8f, 1.0f, 0.7f), Size = 1.0f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Mangrove" }, Description = "Leathery fronds with rust tips, bruised into a salve." },
			new GatheringResource { Node = "Rihani", Item = "Rihani", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_Rihani",
				Tint = new Color(0.8f, 0.7f, 1.0f), Size = 0.8f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Mangrove" }, Description = "Sweet basil from the courtyards, purple-spiked and sharply fragrant." },
			new GatheringResource { Node = "Marcasite", Item = "Marcasite", Family = GatheringFamily.Ore, Tier = 2, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.9f, 0.8f, 0.4f), Size = 1.2f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Mangrove" }, Description = "Brassy cockscomb crystals tarnished blue-violet, from the rag caves and creek banks." },
			new GatheringResource { Node = "Nuru", Item = "Nuru", Family = GatheringFamily.Crystal, Tier = 4, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.5f, 0.95f, 1.0f), Size = 1.4f, MaxUses = 2, GatherSeconds = 4.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Mangrove" }, Description = "Milky blue-white blades that sparkle like disturbed seafire in the rag caves." },

			// ── Lake and River (biome round, 2026-10-26): the clear lake's and the Rhenish river's harvest; the pearl beds and the river's gold vein went elsewhere. ──
			new GatheringResource { Node = "Lake Ore", Item = "Lake Ore", Family = GatheringFamily.Ore, Tier = 1, Visual = "Detail_Pebbles_Furrow",
				Tint = new Color(0.54f, 0.29f, 0.13f), Size = 1.2f, MaxUses = 4, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Lake" }, Description = "Limonite discs and pea-ore dredged from the shelf, the lake-folk's iron." },
			new GatheringResource { Node = "Lake Ball", Item = "Lake Ball", Family = GatheringFamily.Marine, Tier = 1, Visual = "Detail_LakeBall",
				Tint = new Color(0.55f, 0.85f, 0.45f), Size = 0.9f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Lake" }, Description = "Velvet-green spheres rolled smooth by the swell, steeped by the alchemists." },
			new GatheringResource { Node = "Spongilla", Item = "Spongilla", Family = GatheringFamily.Marine, Tier = 2, Visual = "Detail_LakeSponge",
				Tint = new Color(0.6f, 0.9f, 0.5f), Size = 0.9f, MaxUses = 3, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Lake" }, Description = "Green finger-crusts scraped from stones, their glassy spicules ground for polishing." },
			new GatheringResource { Node = "Bulrush Bundle", Item = "Bulrush Bundle", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_LakeBulrush",
				Tint = new Color(0.7f, 0.9f, 0.7f), Size = 1.2f, MaxUses = 4, GatherSeconds = 2.0f, MinAmount = 2, MaxAmount = 4,
				Biomes = new[] { "Lake" }, Description = "Round leafless culms cut for matting and rush seats." },
			new GatheringResource { Node = "Nixglass", Item = "Nixglass", Family = GatheringFamily.Crystal, Tier = 3, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.42f, 0.82f, 0.78f), Size = 1.3f, MaxUses = 3, GatherSeconds = 3.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Lake" }, Description = "Water-clear blue-green prisms that look wet and glow with a slow ripple." },
			new GatheringResource { Node = "River Gold", Item = "River Gold", Family = GatheringFamily.Ore, Tier = 2, Visual = "Detail_Pebbles_Rhine",
				Tint = new Color(1.0f, 0.82f, 0.34f), Size = 1.2f, MaxUses = 4, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "River" }, Description = "Placer flakes washed from the heads of the gravel bars." },
			new GatheringResource { Node = "Agate Nodule", Item = "Agate", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.54f, 0.6f, 0.66f), Size = 1.2f, MaxUses = 3, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "River" }, Description = "Banded chalcedony nodules from the bars and the basalt caves." },
			new GatheringResource { Node = "Noble Crayfish", Item = "Noble Crayfish", Family = GatheringFamily.Marine, Tier = 1, Visual = "Detail_EelBaskets",
				Tint = new Color(0.8f, 0.45f, 0.3f), Size = 0.9f, MaxUses = 3, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "River" }, Description = "Fasting food, taken in creels from under the cobble ledges." },
			new GatheringResource { Node = "Water Pepper", Item = "Water Pepper", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_FlowersFloodbank",
				Tint = new Color(0.9f, 0.6f, 0.6f), Size = 0.9f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "River" }, Description = "Pink-green spikes of the margin, dried as a poor man's pepper." },
			new GatheringResource { Node = "Soapwort", Item = "Soapwort", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_FlowersFloodbank",
				Tint = new Color(1.0f, 0.8f, 0.85f), Size = 0.9f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "River" }, Description = "Pale pink flowers whose roots lather, for washing wool." },
			new GatheringResource { Node = "Rhinegold Seam", Item = "Rhinegold", Family = GatheringFamily.Crystal, Tier = 4, Visual = "Formation_Gypsum_Crystal_0",
				Tint = new Color(0.85f, 0.66f, 0.22f), Size = 1.4f, MaxUses = 2, GatherSeconds = 4.5f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "River" }, Description = "Old-gold threads in milky quartz that shimmer like light through moving water." },
			// ── Rocky Terrain (biome round, 2026-10-26) ── The Pamir's lapis and night-lamp gem, fuel, hips, herbs and whetstone hornstone.
			new GatheringResource { Node = "Lajvard", Item = "Lajvard Lump", Family = GatheringFamily.Crystal, Tier = 3, Visual = "Formation_Hornfels_Block_0",
				Tint = new Color(0.7f, 0.8f, 1.0f), Size = 1.2f, MaxUses = 2, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Rocky Terrain" }, Description = "Deep ultramarine lapis cut from the white marble bands, flecked with gold pyrite." },
			new GatheringResource { Node = "Shabchiragh", Item = "Shabchiragh Gem", Family = GatheringFamily.Crystal, Tier = 4, Visual = "Formation_Hornfels_Block_0",
				Tint = new Color(1.0f, 0.7f, 0.75f), Size = 1.0f, MaxUses = 1, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Rocky Terrain" }, Description = "Rose-red night-lamp stones from the ruby mountain, which glow warm after sunset." },
			new GatheringResource { Node = "Teresken", Item = "Teresken Fuel", Family = GatheringFamily.Wood, Tier = 1, Visual = "Detail_Bush_Teresken",
				Tint = new Color(0.95f, 0.95f, 0.9f), Size = 0f, Footprint = 0.15f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Rocky Terrain" }, Description = "Grey-white woody root crowns grubbed from the gravel for the hearth." },
			new GatheringResource { Node = "Pamir Hips", Item = "Pamir Hips", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_Bush_PamirRose",
				Tint = new Color(1.0f, 0.85f, 0.8f), Size = 0f, Footprint = 0.15f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Rocky Terrain" }, Description = "Bottle-shaped red hips of the Pamir rose, dried for the winter." },
			new GatheringResource { Node = "Dragonhead", Item = "Dragonhead Spike", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_HerbDracocephalum",
				Tint = new Color(0.9f, 0.9f, 1.0f), Size = 0.7f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Rocky Terrain" }, Description = "Aromatic grey whorled spikes of the Pamir dragonhead." },
			new GatheringResource { Node = "Allardia", Item = "Allardia Rosette", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_FlowersAllardia",
				Tint = new Color(1.0f, 0.92f, 0.98f), Size = 0.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Rocky Terrain" }, Description = "Woolly silver rosettes picked from the scree for the physicians." },
			new GatheringResource { Node = "Hornstone", Item = "Hornstone Whetstone", Family = GatheringFamily.Ore, Tier = 1, Visual = "Formation_Hornfels_Block_0",
				Tint = new Color(0.8f, 0.8f, 0.9f), Size = 1.2f, MaxUses = 3, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Rocky Terrain" }, Description = "Fine flinty hornstone split into whetstones." },
			// ── Geyser Basin (biome round, 2026-10-26) ── The hot ground's sinter, mat, alum, bole, raven-flint, spar, zeolite and sun-spar, and its herbs.
			new GatheringResource { Node = "Geyserite Nodule", Item = "Geyserite Nodule", Family = GatheringFamily.Ore, Tier = 1, Visual = "Formation_Sinter_Cone_0",
				Tint = new Color(0.95f, 0.95f, 0.92f), Size = 1.0f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Geyser Basin" }, Description = "Beaded grey-white silica knobs broken from the geyser aprons." },
			new GatheringResource { Node = "Hveraslý Mat", Item = "Hveraslý Mat", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_MatHverasly",
				Tint = new Color(1.0f, 0.85f, 0.6f), Size = 0.9f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Geyser Basin" }, Description = "Orange microbial mat lifted from the runoff, for poultices and dye." },
			new GatheringResource { Node = "Solfatara Alum", Item = "Solfatara Alum", Family = GatheringFamily.Ore, Tier = 1, Visual = "Formation_Rhyolite_AlteredKnoll_0",
				Tint = new Color(1.0f, 0.95f, 0.8f), Size = 1.0f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Geyser Basin" }, Description = "White alum crusts scraped from the steaming clay, a dye mordant." },
			new GatheringResource { Node = "Painter's Bole", Item = "Painter's Bole", Family = GatheringFamily.Ore, Tier = 1, Visual = "Formation_Rhyolite_AlteredKnoll_0",
				Tint = new Color(1.0f, 0.8f, 0.75f), Size = 1.0f, MaxUses = 3, GatherSeconds = 2.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Geyser Basin" }, Description = "Ochre and rose clay pigment dug from the painted hills." },
			new GatheringResource { Node = "Raven-flint", Item = "Raven-flint", Family = GatheringFamily.Ore, Tier = 2, Visual = "Formation_Obsidian_Chunk_0",
				Tint = new Color(0.7f, 0.7f, 0.78f), Size = 1.0f, MaxUses = 3, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Geyser Basin" }, Description = "Black volcanic glass with grey snowflake spots, knapped to a razor edge." },
			new GatheringResource { Node = "Iceland Spar", Item = "Iceland Spar", Family = GatheringFamily.Crystal, Tier = 2, Visual = "Formation_Rhyolite_FlowBanded_0",
				Tint = new Color(1.0f, 0.97f, 0.88f), Size = 1.0f, MaxUses = 2, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Geyser Basin" }, Description = "Clear honey-tinted rhombs that show a doubled image." },
			new GatheringResource { Node = "Stilbite Sheaf", Item = "Stilbite Sheaf", Family = GatheringFamily.Crystal, Tier = 1, Visual = "Formation_Rhyolite_FlowBanded_0",
				Tint = new Color(1.0f, 0.82f, 0.74f), Size = 1.0f, MaxUses = 2, GatherSeconds = 2.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Geyser Basin" }, Description = "Peach-salmon bow-tie sheaves from the basalt vugs." },
			new GatheringResource { Node = "Solarsteinn", Item = "Solarsteinn", Family = GatheringFamily.Crystal, Tier = 3, Visual = "Formation_Rhyolite_FlowBanded_0",
				Tint = new Color(1.0f, 0.95f, 0.78f), Size = 1.0f, MaxUses = 1, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Geyser Basin" }, Description = "Spar rhombs whose white-gold glow turns toward the hidden sun." },
			new GatheringResource { Node = "Moonwort", Item = "Moonwort Frond", Family = GatheringFamily.Herb, Tier = 3, Visual = "Detail_Moonwort",
				Tint = new Color(0.9f, 1.0f, 0.9f), Size = 0.5f, MaxUses = 1, GatherSeconds = 3.0f, MinAmount = 1, MaxAmount = 1,
				Biomes = new[] { "Geyser Basin" }, Description = "A fleshy half-moon frond said to unlock locks and unshoe horses." },
			new GatheringResource { Node = "Adder's-tongue", Item = "Adder's-tongue Leaf", Family = GatheringFamily.Herb, Tier = 2, Visual = "Detail_AdderTongue",
				Tint = new Color(0.9f, 1.0f, 0.85f), Size = 0.5f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 2,
				Biomes = new[] { "Geyser Basin" }, Description = "A single oval leaf and spore spear, boiled into a wound salve." },
			new GatheringResource { Node = "Crottle Lichen", Item = "Crottle Lichen", Family = GatheringFamily.Herb, Tier = 1, Visual = "Detail_LichenCrottle",
				Tint = new Color(0.95f, 0.9f, 0.85f), Size = 0.6f, MaxUses = 3, GatherSeconds = 1.5f, MinAmount = 1, MaxAmount = 3,
				Biomes = new[] { "Geyser Basin" }, Description = "Grey crusts scraped from the boulders for the russet dye." },
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
