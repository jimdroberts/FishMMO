#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The point-of-interest catalogue as the planner reads it: one <see cref="PointOfInterestKindRule"/> per kind,
	/// the code defaults with any asset rows laid over them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Defaults are code</b>, so a fresh project, a test and a batch cut all plan the same scene without an asset.
	/// Density at Normal comes to roughly 2.5–3.5 placed sites per km² of temperate land before the ground rules thin
	/// it: 20–60 on an 8–20 km² scene. Biome names are the asset names in Assets/Templates/Entity/Biomes.
	/// </para>
	/// <para>
	/// <b>Order.</b> Placed kinds are sited tier by tier (<see cref="TierOf"/>) and, inside a tier, in
	/// <see cref="POIType"/> order. <see cref="POIType"/> is append-only, so a kind added later is sited last in its
	/// tier and never moves the sites of a kind sited before it.
	/// </para>
	/// </remarks>
	public sealed class PointOfInterestRules
	{
		private readonly Dictionary<POIType, PointOfInterestKindRule> rules = new Dictionary<POIType, PointOfInterestKindRule>();

		public PointOfInterestRules(IEnumerable<PointOfInterestKindRule> rows)
		{
			if (rows != null)
			{
				foreach (PointOfInterestKindRule row in rows)
				{
					if (row != null)
					{
						rules[row.Kind] = row;
					}
				}
			}
		}

		/// <summary>The code defaults, every row a fresh copy.</summary>
		public static PointOfInterestRules Default() => new PointOfInterestRules(Defaults());

		/// <summary>These rules with <paramref name="overrides"/> replacing the rows of their kinds.</summary>
		public PointOfInterestRules With(IEnumerable<PointOfInterestKindRule> overrides)
		{
			var merged = new List<PointOfInterestKindRule>();
			foreach (PointOfInterestKindRule row in rules.Values)
			{
				merged.Add(row.Clone());
			}
			var result = new PointOfInterestRules(merged);
			if (overrides != null)
			{
				foreach (PointOfInterestKindRule row in overrides)
				{
					if (row != null)
					{
						result.rules[row.Kind] = row.Clone();
					}
				}
			}
			return result;
		}

		/// <summary>The rule for a kind, or null when the catalogue has none.</summary>
		public PointOfInterestKindRule RuleFor(POIType kind) => rules.TryGetValue(kind, out PointOfInterestKindRule rule) ? rule : null;

		/// <summary>Adds or replaces a row.</summary>
		public void Set(PointOfInterestKindRule rule)
		{
			if (rule != null)
			{
				rules[rule.Kind] = rule;
			}
		}

		/// <summary>Removes a kind's row.</summary>
		public void Remove(POIType kind) => rules.Remove(kind);

		/// <summary>Every enabled row of a placement, in siting order: tier, then <see cref="POIType"/> ordinal.</summary>
		public List<PointOfInterestKindRule> Ordered(PointOfInterestPlacement placement)
		{
			var list = new List<PointOfInterestKindRule>();
			foreach (PointOfInterestKindRule rule in rules.Values)
			{
				if (rule.Enabled && rule.Placement == placement)
				{
					list.Add(rule);
				}
			}
			list.Sort((a, b) =>
			{
				int tier = TierOf(a.Kind).CompareTo(TierOf(b.Kind));
				return tier != 0 ? tier : ((int)a.Kind).CompareTo((int)b.Kind);
			});
			return list;
		}

		/// <summary>
		/// The siting tier of a placed kind: the capital, then the places people live and hold, then the dungeons, lairs
		/// and caves, then everything else, then the resources, which fill what is left.
		/// </summary>
		public static int TierOf(POIType kind)
		{
			switch (kind)
			{
				case POIType.Capital:
					return 0;
				case POIType.City:
				case POIType.Town:
				case POIType.Village:
				case POIType.Port:
				case POIType.Castle:
				case POIType.Keep:
				case POIType.Fortress:
				case POIType.StiltVillage:
				case POIType.Monastery:
				case POIType.TradingPost:
					return 1;
				case POIType.DungeonEntrance:
				case POIType.BossLair:
					return 2;
				case POIType.OreVein:
				case POIType.CrystalFormation:
				case POIType.AncientTree:
				case POIType.HerbGrove:
					return 4;
			}
			return PointOfInterestKinds.Info(kind).Has(PointOfInterestTraits.Terrain) ? 2 : 3;
		}

		// ── The defaults ─────────────────────────────────────────────

		private static readonly string[] Alien =
		{
			"Molten Surface", "Runaway Greenhouse Plain", "Sulphuric Cloud Deck", "Radiation Plain", "Regolith Plain", "Tholin Plain",
			"Nitrogen Ice Field", "Methane Lake", "Cryovolcanic Plain", "Ice Geyser Field", "Tidal Fracture", "Subsurface Ocean Vent",
			"Impact Basin", "Dust Sea",
		};

		private static readonly string[] Ice = { "Glacier", "Ice Sheet", "Ice Shelf", "Permanent Ice", "Snow", "Nitrogen Ice Field", "Ice Cave", "Ice Palace", "Ice Ruin" };
		private static readonly string[] Swamp = { "Swamp", "Mangrove", "Peat Bog", "Wetlands", "Swamp Ruins", "Swamp Temple", "Swamp Cave" };
		private static readonly string[] Desert = { "Desert", "High Desert", "Badlands", "Salt Flat", "Oasis", "Scrubland" };
		private static readonly string[] Woods = { "Forest", "Woodland", "Taiga", "Jungle", "Bamboo Forest", "Forest Ruins", "Jungle Ruins", "Jungle Temple" };
		private static readonly string[] Volcanic = { "Volcanic", "Volcanic Cave", "Volcanic Temple", "Geyser Basin", "Sulphur Flats", "Lava Tube" };

		/// <summary>Where people settle, and how gladly.</summary>
		private static readonly (string biome, float weight)[] Habitable =
		{
			("Farmland", 1.2f), ("Plains", 1f), ("Grassland", 1f), ("Valley", 1f), ("Oasis", 1f), ("Woodland", 0.9f), ("Forest", 0.8f),
			("Hills", 0.8f), ("Steppe", 0.7f), ("Savanna", 0.7f), ("Beach", 0.6f), ("Estuary", 0.6f), ("Taiga", 0.6f), ("Rocky Coast", 0.5f),
			("Alpine Meadow", 0.5f), ("Scrubland", 0.5f), ("Bamboo Forest", 0.5f), ("Jungle", 0.4f), ("Karst", 0.4f), ("Wetlands", 0.3f),
			("Mountain Slope", 0.3f), ("Tundra", 0.3f), ("High Desert", 0.3f), ("Forest Ruins", 0.3f), ("Desert", 0.2f), ("Badlands", 0.2f),
			("Castle", 1f), ("Fortress", 1f), ("Marble Palace", 1f), ("Mangrove", 0.2f), ("Swamp", 0.15f), ("Alpine", 0.2f),
		};

		/* The peoples who build and keep towns. Fey and giants were in this list, and the first real cut (Flo Monolith,
		 * 2026-10-10) gave the capital to glimmerlings and a castle to wisps; they keep their groves, rings and lairs. */
		private static readonly string[] Civil = { "Humanoid", "Beastfolk" };
		private static readonly string[] Monster = { "Beast", "Undead", "Aberration", "Elemental", "Outsider", "Construct", "Draconic", "Plant", "Giant" };
		private static readonly string[] Outlaw = { "Humanoid", "Beastfolk", "Giant" };

		private static PointOfInterestKindRule Placed(POIType kind, float perKm2, int min, int max, float spacing)
			=> new PointOfInterestKindRule { Kind = kind, Placement = PointOfInterestPlacement.Placed, PerKm2 = perKm2, Min = min, Max = max, SpacingMetres = spacing };

		private static PointOfInterestKindRule Detected(POIType kind, PointOfInterestDetector detector, int max, float spacing)
			=> new PointOfInterestKindRule { Kind = kind, Placement = PointOfInterestPlacement.Detected, Detector = detector, Max = max, SpacingMetres = spacing };

		private static PointOfInterestKindRule Settled(PointOfInterestKindRule rule)
		{
			rule.DefaultBiomeWeight = 0f;
			foreach ((string biome, float weight) in Habitable)
			{
				rule.Biomes.Add(new PointOfInterestBiomeWeight(biome, weight));
			}
			return rule.Races(Civil);
		}

		/// <summary>A kind found anywhere people or beasts could be, never on alien or ice ground.</summary>
		private static PointOfInterestKindRule Wild(PointOfInterestKindRule rule) => rule.Weigh(0f, Alien).Weigh(0.2f, Ice);

		/// <summary>The code catalogue: one fresh row per kind.</summary>
		public static List<PointOfInterestKindRule> Defaults()
		{
			var d = new List<PointOfInterestKindRule>
			{
				// ── Detected: water ──
				Detected(POIType.Waterfall, PointOfInterestDetector.Falls, 12, 60f),
				Detected(POIType.Rapids, PointOfInterestDetector.Rapids, 6, 250f),
				Detected(POIType.River, PointOfInterestDetector.Rivers, 8, 0f),
				Detected(POIType.Lake, PointOfInterestDetector.Lakes, 10, 0f).Area(1500f),
				Detected(POIType.Spring, PointOfInterestDetector.Springs, 4, 300f),
				Detected(POIType.HotSpring, PointOfInterestDetector.HotSprings, 4, 300f).Only("Geyser Basin", "Volcanic", "Sulphur Flats", "Volcanic Cave", "Molten Surface"),
				Detected(POIType.RiverMouth, PointOfInterestDetector.RiverMouths, 4, 300f),
				Detected(POIType.Delta, PointOfInterestDetector.Deltas, 1, 1000f),

				// ── Detected: landforms ──
				Detected(POIType.Peak, PointOfInterestDetector.Peaks, 4, 700f),
				Detected(POIType.Pass, PointOfInterestDetector.Passes, 2, 800f),
				Detected(POIType.Gorge, PointOfInterestDetector.Gorges, 2, 800f),
				Detected(POIType.Mesa, PointOfInterestDetector.Mesas, 3, 800f).Area(120000f),
				Detected(POIType.Butte, PointOfInterestDetector.Buttes, 4, 400f).Area(8000f),
				Detected(POIType.Valley, PointOfInterestDetector.BiomeCluster, 2, 1200f).Only("Valley").Area(500000f),
				Detected(POIType.Island, PointOfInterestDetector.Islands, 6, 300f).Area(3000f),
				Detected(POIType.Bay, PointOfInterestDetector.Bays, 2, 1000f),
				Detected(POIType.Headland, PointOfInterestDetector.Headlands, 3, 800f),
				Detected(POIType.Sinkhole, PointOfInterestDetector.Sinkholes, 4, 200f).Only("Karst"),
				Detected(POIType.Crater, PointOfInterestDetector.BiomeClusterLowest, 2, 1000f).Only("Crater").Area(100000f),
				Detected(POIType.DuneSea, PointOfInterestDetector.BiomeCluster, 1, 2000f).Only("Dust Sea", "Desert").Area(2000000f),
				Detected(POIType.SaltFlat, PointOfInterestDetector.BiomeCluster, 2, 1000f).Only("Salt Flat").Area(150000f),
				Detected(POIType.Glacier, PointOfInterestDetector.BiomeCluster, 2, 1500f).Only("Glacier", "Ice Sheet", "Ice Shelf", "Permanent Ice").Area(500000f),

				// ── Detected: volcanic, wetland, coast, alien ──
				Detected(POIType.Volcano, PointOfInterestDetector.BiomeClusterHighest, 1, 2000f).Only("Volcanic").Area(500000f),
				Detected(POIType.LavaLake, PointOfInterestDetector.BiomeClusterLowest, 2, 1000f).Only("Molten Surface").Area(50000f),
				Detected(POIType.FumaroleField, PointOfInterestDetector.BiomeCluster, 2, 800f).Only("Geyser Basin", "Sulphur Flats").Area(80000f),
				Detected(POIType.ObsidianField, PointOfInterestDetector.BiomeCluster, 1, 1500f).Only("Volcanic").Area(300000f),
				Detected(POIType.MangroveMaze, PointOfInterestDetector.BiomeCluster, 2, 1200f).Only("Mangrove").Area(300000f),
				Detected(POIType.CoralReef, PointOfInterestDetector.BiomeCluster, 2, 1000f).Only("Coral Reef").Area(50000f),
				Detected(POIType.IceGeyserField, PointOfInterestDetector.BiomeCluster, 2, 1000f).Only("Ice Geyser Field").Area(80000f),
				Detected(POIType.Cryovolcano, PointOfInterestDetector.BiomeClusterHighest, 1, 2000f).Only("Cryovolcanic Plain").Area(300000f),
				Detected(POIType.MethaneLake, PointOfInterestDetector.BiomeClusterLowest, 2, 1000f).Only("Methane Lake").Area(50000f),
				Detected(POIType.ImpactBasin, PointOfInterestDetector.BiomeClusterLowest, 1, 2000f).Only("Impact Basin").Area(300000f),
				Detected(POIType.TidalRift, PointOfInterestDetector.BiomeCluster, 2, 1000f).Only("Tidal Fracture").Area(80000f),

				// ── Placed: settlements and holdings (tier 0–1) ──
				Settled(Placed(POIType.Capital, 0f, 0, 1, 3000f).Footprint(160f, 220f, 14f)),
				Settled(Placed(POIType.City, 0.03f, 0, 1, 2500f).Footprint(110f, 150f, 14f)),
				Settled(Placed(POIType.Town, 0.08f, 0, 2, 1500f).Footprint(70f, 100f, 15f)),
				Settled(Placed(POIType.Village, 0.25f, 1, 5, 900f).Footprint(40f, 60f, 16f)),
				Settled(Placed(POIType.Port, 0.1f, 0, 2, 1500f).Footprint(50f, 80f, 14f)).Coast(120f),
				Settled(Placed(POIType.Keep, 0.06f, 0, 2, 1200f).Footprint(25f, 35f, 20f)),
				Settled(Placed(POIType.Castle, 0.04f, 0, 1, 2000f).Footprint(45f, 65f, 18f)),
				Settled(Placed(POIType.Fortress, 0.03f, 0, 1, 2000f).Footprint(55f, 75f, 18f)),
				Settled(Placed(POIType.StiltVillage, 0.15f, 0, 2, 900f).Footprint(35f, 55f, 14f)).NearWater(80f).Only(Swamp),
				Settled(Placed(POIType.Monastery, 0.03f, 0, 1, 1500f).Footprint(30f, 45f, 18f)).Altitude(0f, PointOfInterestKindRule.Unbounded),
				Settled(Placed(POIType.TradingPost, 0.05f, 0, 1, 1200f).Footprint(20f, 30f, 18f)),

				// ── Placed: dungeons, lairs and terrain-shaped kinds (tier 2) ──
				Wild(Placed(POIType.DungeonEntrance, 0.06f, 0, 2, 1200f).Footprint(12f, 18f, 30f)).Hard(0.4f, true),
				Wild(Placed(POIType.BossLair, 0.02f, 0, 1, 3000f).Footprint(35f, 50f, 18f)).AwayFromSettlements(900f).Races(Monster),
				Wild(Placed(POIType.Cave, 0.15f, 0, 4, 500f).Footprint(8f, 14f, 30f)).Hard(0.4f, true).Races(Monster),
				Placed(POIType.Grotto, 0.06f, 0, 2, 600f).Footprint(6f, 10f, 30f).Hard(0.3f, true).NearWater(120f).Weigh(0f, Alien),
				Placed(POIType.SeaCave, 0.06f, 0, 2, 600f).Footprint(6f, 10f, 30f).Hard(0.3f, true).Coast(40f).Altitude(0.5f, 12f),
				Placed(POIType.IceCave, 0.1f, 0, 2, 600f).Footprint(8f, 14f, 30f).Hard(0f, true).Only(Ice).Races(Monster),
				Placed(POIType.LavaTube, 0.1f, 0, 2, 600f).Footprint(8f, 14f, 30f).Hard(0.3f, true).Only(Volcanic).Races("Elemental", "Draconic", "Beast"),
				Placed(POIType.Overhang, 0.06f, 0, 3, 400f).Footprint(6f, 10f, 30f).Hard(0.45f, true),
				Placed(POIType.NaturalArch, 0.03f, 0, 1, 1200f).Footprint(10f, 18f, 30f).Hard(0.5f, true).Weigh(1.5f, Desert),

				// ── Placed: wetland ──
				Placed(POIType.SunkenTemple, 0.06f, 0, 1, 1200f).Footprint(25f, 35f, 14f).Only(Swamp).NearWater(60f).Races(Monster),
				Placed(POIType.DrownedVillage, 0.06f, 0, 1, 1200f).Footprint(30f, 45f, 14f).Only(Swamp).NearWater(60f),
				Placed(POIType.WitchHut, 0.1f, 0, 2, 800f).Footprint(10f, 14f, 18f).Only(Swamp).AwayFromSettlements(500f),
				Placed(POIType.BogShrine, 0.1f, 0, 2, 600f).Footprint(8f, 12f, 18f).Only(Swamp),
				Placed(POIType.WispHollow, 0.08f, 0, 2, 600f).Footprint(15f, 25f, 20f).Only(Swamp),

				// ── Placed: wild camps and lairs ──
				Wild(Placed(POIType.Camp, 0.15f, 0, 3, 600f).Footprint(15f, 22f, 18f)).AwayFromSettlements(600f).Races(Outlaw),
				Wild(Placed(POIType.BanditCamp, 0.1f, 0, 2, 800f).Footprint(18f, 26f, 18f)).AwayFromSettlements(700f).Races(Outlaw),
				Wild(Placed(POIType.HuntingLodge, 0.08f, 0, 2, 900f).Footprint(12f, 18f, 18f)).Weigh(1.5f, Woods),
				Placed(POIType.LumberCamp, 0.08f, 0, 2, 900f).Footprint(15f, 22f, 16f).Only(Woods),
				Wild(Placed(POIType.FishingCamp, 0.08f, 0, 2, 900f).Footprint(12f, 18f, 16f)).NearWater(60f),
				Wild(Placed(POIType.MonsterDen, 0.12f, 0, 3, 600f).Footprint(15f, 25f, 25f)).AwayFromSettlements(600f).Races(Monster),
				Wild(Placed(POIType.Nest, 0.1f, 0, 3, 500f).Footprint(10f, 16f, 30f)).AwayFromSettlements(600f).Races("Beast", "Draconic", "Aberration"),

				// ── Placed: sacred ──
				Wild(Placed(POIType.RitualSite, 0.06f, 0, 1, 1000f).Footprint(15f, 22f, 16f)).AwayFromSettlements(600f).Races(Monster),
				Wild(Placed(POIType.StoneCircle, 0.08f, 0, 2, 800f).Footprint(12f, 18f, 14f)),
				Wild(Placed(POIType.Shrine, 0.15f, 0, 3, 500f).Footprint(6f, 10f, 20f)),
				Wild(Placed(POIType.Temple, 0.04f, 0, 1, 1500f).Footprint(25f, 35f, 16f)),
				Placed(POIType.FeyRing, 0.05f, 0, 1, 1200f).Footprint(8f, 12f, 16f).Only(Woods).Weigh(1f, "Alpine Meadow", "Grassland"),
				Placed(POIType.LeyNexus, 0.03f, 0, 1, 2000f).Footprint(10f, 16f, 20f).Weigh(0.5f, Alien),
				Placed(POIType.FallenStar, 0.02f, 0, 1, 2500f).Footprint(12f, 20f, 25f).Weigh(0.7f, Alien),
				Placed(POIType.CorruptedGrove, 0.04f, 0, 1, 1500f).Footprint(20f, 30f, 20f).Only(Woods).Weigh(1f, Swamp).AwayFromSettlements(600f),
				Placed(POIType.Portal, 0.03f, 0, 1, 2500f).Footprint(10f, 14f, 16f).Weigh(0.5f, Alien),

				// ── Placed: the dead ──
				Wild(Placed(POIType.Graveyard, 0.1f, 0, 3, 600f).Footprint(15f, 25f, 14f)).NearSettlement(400f).Races("Undead"),
				Wild(Placed(POIType.Barrow, 0.06f, 0, 2, 800f).Footprint(10f, 16f, 20f)).Races("Undead"),
				Wild(Placed(POIType.Crypt, 0.05f, 0, 1, 1200f).Footprint(12f, 18f, 18f)).Hard(0.5f).Races("Undead"),
				Wild(Placed(POIType.Battlefield, 0.04f, 0, 1, 2000f).Footprint(40f, 70f, 14f)).Races("Undead"),
				Wild(Placed(POIType.Ossuary, 0.03f, 0, 1, 1500f).Footprint(10f, 14f, 20f)).Races("Undead"),

				// ── Placed: ruins ──
				Wild(Placed(POIType.Ruins, 0.15f, 0, 3, 700f).Footprint(20f, 35f, 18f)).Weigh(0.5f, Alien),
				Wild(Placed(POIType.RuinedTower, 0.08f, 0, 2, 900f).Footprint(8f, 12f, 22f)),
				Wild(Placed(POIType.Monument, 0.05f, 0, 1, 1200f).Footprint(8f, 12f, 16f)).Weigh(0.5f, Alien),
				Wild(Placed(POIType.Statue, 0.08f, 0, 2, 700f).Footprint(4f, 6f, 20f)),
				Wild(Placed(POIType.Obelisk, 0.06f, 0, 2, 900f).Footprint(4f, 6f, 20f)).Weigh(0.5f, Alien),
				Wild(Placed(POIType.AncientRoad, 0.03f, 0, 1, 1500f).Footprint(15f, 25f, 12f)),
				Settled(Placed(POIType.AbandonedFarm, 0.06f, 0, 2, 900f).Footprint(20f, 30f, 12f)),
				Wild(Placed(POIType.Hermitage, 0.05f, 0, 1, 1200f).Footprint(8f, 12f, 22f)).AwayFromSettlements(700f),
				Placed(POIType.Oasis, 0.2f, 0, 2, 1200f).Footprint(20f, 35f, 12f).Only(Desert).Weigh(0f, "Scrubland").NearWater(150f),

				// ── Placed: works, roads and the coast ──
				Settled(Placed(POIType.Tower, 0.12f, 0, 3, 800f).Footprint(6f, 9f, 22f)),
				Settled(Placed(POIType.Waystation, 0.08f, 0, 2, 1000f).Footprint(12f, 18f, 16f)).AwayFromSettlements(500f),
				Placed(POIType.Bridge, 0.1f, 0, 3, 500f).Footprint(6f, 30f, 90f),
				Wild(Placed(POIType.Mine, 0.08f, 0, 2, 900f).Footprint(12f, 18f, 30f)).Hard(0.55f, true),
				Wild(Placed(POIType.Quarry, 0.06f, 0, 1, 1200f).Footprint(20f, 30f, 28f)).Hard(0.55f),
				Wild(Placed(POIType.Lighthouse, 0.05f, 0, 1, 2000f).Footprint(6f, 9f, 25f)).Coast(60f).Altitude(3f, PointOfInterestKindRule.Unbounded),
				Wild(Placed(POIType.Wreck, 0.06f, 0, 2, 800f).Footprint(8f, 14f, 12f)).Coast(25f).Altitude(0.5f, 4f),
				Wild(Placed(POIType.SmugglersCove, 0.05f, 0, 1, 1500f).Footprint(12f, 18f, 20f)).Coast(50f).AwayFromSettlements(700f).Races(Outlaw),
				Wild(Placed(POIType.PirateCove, 0.04f, 0, 1, 2000f).Footprint(20f, 30f, 18f)).Coast(60f).AwayFromSettlements(900f).Races(Outlaw),

				// ── Placed: under the sea ──
				Placed(POIType.SunkenShip, 0.08f, 0, 2, 800f).Footprint(8f, 14f, 30f).Sea(5f, 40f),
				Placed(POIType.SunkenRuins, 0.05f, 0, 1, 1200f).Footprint(20f, 35f, 25f).Sea(4f, 30f).Races("Aquatic"),
				Placed(POIType.SunkenCity, 0.02f, 0, 1, 3000f).Footprint(60f, 90f, 20f).Sea(8f, 60f).Races("Aquatic"),

				// ── Placed: resources (tier 4) ──
				Wild(Placed(POIType.OreVein, 0.1f, 0, 3, 500f).Footprint(5f, 8f, 35f)).Hard(0.5f).Weigh(0.6f, Alien),
				Wild(Placed(POIType.CrystalFormation, 0.05f, 0, 2, 800f).Footprint(6f, 10f, 35f)).Hard(0.45f).Weigh(0.6f, Alien),
				Placed(POIType.AncientTree, 0.05f, 0, 1, 1500f).Footprint(10f, 14f, 18f).Only(Woods),
				Wild(Placed(POIType.HerbGrove, 0.08f, 0, 2, 700f).Footprint(8f, 12f, 20f)).Weigh(1.5f, Woods).Weigh(1.2f, Swamp),
			};

			// The dead and the sacred are not built on sea-floor biomes nor on the ice of a glacier's heart.
			foreach (PointOfInterestKindRule rule in d)
			{
				if (rule.Placement == PointOfInterestPlacement.Placed && !rule.Underwater && rule.DefaultBiomeWeight > 0f)
				{
					rule.Weigh(0f, "Ocean", "Deep Ocean", "Coastal Water", "Abyss", "Abyssal Plain", "Seamount", "Underwater Canyon", "Coral Reef",
						"Lake", "River");
				}
			}
			return d;
		}
	}
}
#endif
