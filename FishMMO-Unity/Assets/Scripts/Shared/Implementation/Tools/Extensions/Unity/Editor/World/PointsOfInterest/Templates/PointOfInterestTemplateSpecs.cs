#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// One template the authoring tool writes, as plain data (no ScriptableObject), so tests read the whole catalogue
	/// with no assets: its kind, variant name, fit, ground, style, layout and slots.
	/// </summary>
	public sealed class PointOfInterestTemplateSpec
	{
		public POIType Kind;
		/// <summary>The variant's name: the asset is "&lt;Kind&gt; - &lt;Variant&gt;".</summary>
		public string Variant;
		public float Weight = 1f;
		public float DefaultBiomeWeight = 1f;
		public readonly List<PointOfInterestBiomeWeight> BiomeWeights = new List<PointOfInterestBiomeWeight>();
		public readonly List<string> RaceCategories = new List<string>();
		public bool Small = true, Medium = true, Large = true;
		public float PadFlatness = 1f;
		public string Style;
		public PointOfInterestLayout Layout;
		public readonly List<PointOfInterestPieceSlot> Pieces = new List<PointOfInterestPieceSlot>();
		public PointOfInterestFinish Finish = PointOfInterestFinish.Auto;
		public float FinishChance = 1f;
		/// <summary>Stands in water on purpose (a drowned village): its pieces are laid under the water line too.</summary>
		public bool InWater;
		/// <summary>Built in a carved cave's chamber (<see cref="PointOfInterestChamberFeature"/>), not at its mouth.</summary>
		public bool InChamber;
		/// <summary>
		/// Built only on a world with liquid water on its surface (<see cref="PointOfInterestTemplate.NeedsLiquidWater"/>, decision 96,
		/// 2026-10-25): a holy well's spring chapel, where its silent well-head stands on any world.
		/// </summary>
		public bool NeedsLiquidWater;

		/// <summary>The asset's name.</summary>
		public string Name => $"{Kind} - {Variant}";

		/// <summary>The catalogue group's folder under <see cref="PointOfInterestTemplate.Folder"/>.</summary>
		public string GroupFolder => $"{PointOfInterestTemplate.Folder}/{PointOfInterestKinds.Info(Kind).Group}";

		/// <summary>The size classes the template takes, in order (0 small … 2 large).</summary>
		public IEnumerable<int> Sizes()
		{
			if (Small) yield return 0;
			if (Medium) yield return 1;
			if (Large) yield return 2;
		}

		// ── Fluent setup, for the catalogue below ────────────────────

		/// <summary>Adds a slot: a structure-kit query (StructureKitPieceSource), how many, where, how worn and how big.</summary>
		public PointOfInterestTemplateSpec S(string tag, int min, int max, PointOfInterestSlotRole role = PointOfInterestSlotRole.Auto,
			float decayMin = 0f, float decayMax = 0f, float scaleMin = 1f, float scaleMax = 1f, string style = null)
		{
			Pieces.Add(new PointOfInterestPieceSlot
			{
				Tag = tag,
				MinCount = min,
				MaxCount = Math.Max(min, max),
				Role = role,
				MinDecay = decayMin,
				MaxDecay = Math.Max(decayMin, decayMax),
				MinScale = scaleMin,
				MaxScale = Math.Max(scaleMin, scaleMax),
				Style = style ?? string.Empty,
			});
			return this;
		}

		/// <summary>These biomes at <paramref name="weight"/> (asset names; a later entry for a biome wins).</summary>
		public PointOfInterestTemplateSpec In(float weight, params string[] biomes)
		{
			foreach (string biome in biomes)
			{
				BiomeWeights.Add(new PointOfInterestBiomeWeight(biome, weight));
			}
			return this;
		}

		/// <summary>
		/// The weight the list gives a biome by its asset name (a later entry winning; <see cref="DefaultBiomeWeight"/> for one it
		/// does not name): what the biome art authoring writes onto that biome's template for this variant where it differs from
		/// the weight elsewhere (BiomeArtSpec.Entry.SiteVariants, 2026-10-16). The cut reads the biome template's.
		/// </summary>
		public float BiomeWeightByName(string biome)
		{
			float weight = DefaultBiomeWeight;
			if (!string.IsNullOrEmpty(biome))
			{
				foreach (PointOfInterestBiomeWeight entry in BiomeWeights)
				{
					if (string.Equals(entry.Biome, biome, StringComparison.OrdinalIgnoreCase))
					{
						weight = entry.Weight;
					}
				}
			}
			return weight;
		}

		/// <summary>The weight of a biome the list does not name.</summary>
		public PointOfInterestTemplateSpec Elsewhere(float weight)
		{
			DefaultBiomeWeight = weight;
			return this;
		}

		public PointOfInterestTemplateSpec Races(params string[] categories)
		{
			RaceCategories.AddRange(categories);
			return this;
		}

		public PointOfInterestTemplateSpec Sized(bool small, bool medium, bool large)
		{
			Small = small;
			Medium = medium;
			Large = large;
			return this;
		}

		public PointOfInterestTemplateSpec Weighs(float weight)
		{
			Weight = weight;
			return this;
		}

		public PointOfInterestTemplateSpec Weathered(PointOfInterestFinish finish, float chance = 1f)
		{
			Finish = finish;
			FinishChance = chance;
			return this;
		}

		public PointOfInterestTemplateSpec Pad(float flatness)
		{
			PadFlatness = flatness;
			return this;
		}

		public PointOfInterestTemplateSpec Wet()
		{
			InWater = true;
			return this;
		}

		public PointOfInterestTemplateSpec Chamber()
		{
			InChamber = true;
			return this;
		}

		/// <summary>Built only where the world has liquid water (<see cref="NeedsLiquidWater"/>).</summary>
		public PointOfInterestTemplateSpec NeedsWater()
		{
			NeedsLiquidWater = true;
			return this;
		}
	}

	/// <summary>
	/// The project's point-of-interest templates: one or more for every kind the structure kit builds
	/// (<see cref="PointOfInterestTraits.Structure"/>) and for every carved cave kind (<see cref="CaveShaper"/>; built in
	/// its chamber), as data. <see cref="PointOfInterestTemplateAuthoring"/> writes them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Variants by biome.</b> A kind's variants split its sites by the site's biome: timber in forests and farmland,
	/// stone in deserts, hills and mountains, longhouses and hide yurts in the cold and on the steppe, stilts in swamps.
	/// The planner's catalogue already decides where a kind may stand; these weights only choose among its variants.
	/// </para>
	/// <para>
	/// <b>Race filters never strand a site.</b> A template with race categories fits only a site whose race is in them,
	/// and a site with no race (a kind that takes none) fits none, so race filters are only on kinds that take a race,
	/// and every such kind keeps one variant with no filter: a site whose biome offered no race of the kind's categories
	/// still builds.
	/// </para>
	/// <para>
	/// <b>Queries</b> are the structure kit's tags as <see cref="StructureKitPieceSource"/> reads them; a test holds every
	/// one to resolving in the kit. Decay past the kit's ruin threshold lays a piece's ruin, so a sunken city names
	/// walls and houses and gets fallen ones.
	/// </para>
	/// </remarks>
	public static class PointOfInterestTemplateSpecs
	{
		// ── Queries ──────────────────────────────────────────────────
		public const string House = "house";
		public const string Stilt = "house+stilt";
		public const string RuinedHouse = "house+ruin";
		public const string Wall = "wall!gate!tower!fence";
		public const string Palisade = "palisade!gate";
		public const string Gate = "gate";
		public const string WallTower = "tower+wall";
		public const string Watchtower = "tower+military!wall!keep";
		public const string Keep = "keep";
		/// <summary>
		/// A capital's palace (2026-10-19: "massive palace style castles with interiors (kings chair?) for capitals"), by its throne
		/// hall's id: PropsFeature keeps the whole compound's ground for the slot and lays the compound there (PointOfInterestPalace),
		/// its gatehouse, towers and curtain round a court with the garden, the two wings and the throne hall.
		/// </summary>
		public const string Palace = "=ThroneHall";
		public const string Tent = "tent";
		public const string Yurt = "=TentLarge";
		public const string LeanTo = "=LeanTo";
		public const string Fire = "fire";
		public const string Storage = "storage";
		public const string Banner = "banner";
		public const string Bedding = "bedding";
		public const string Menhir = "=StandingStone";
		public const string CircleStone = "=CircleStone";
		public const string Altar = "altar";
		public const string Shrine = "shrine";
		public const string Obelisk = "=Obelisk";
		public const string Statue = "=Statue";
		public const string Plinth = "=StatuePlinth";
		public const string Column = "column";
		public const string BrokenColumn = "column+ruin";
		public const string Rubble = "rubble";
		public const string Gravestone = "=Gravestone";
		public const string Mound = "=GraveMound";
		public const string CryptDoor = "=CryptEntrance";
		/// <summary>Dungeon doors set into a face, anchored at their thresholds (StructurePieces.Dungeon.cs, 2026-10-18).</summary>
		public const string DungeonPortal = "=DungeonPortal", DungeonBarrow = "=DungeonBarrow";
		public const string Mausoleum = "=Mausoleum";
		public const string Bones = "bones";
		public const string Fence = "fence";
		public const string Well = "well";
		public const string Lighthouse = "lighthouse";
		public const string Stall = "market";
		public const string Pier = "dock";
		public const string Bridge = "bridge";
		public const string Longhouse = "=Longhouse";
		public const string Cottage = "=TimberHouseSmall";
		public const string StoneHouse = "=StoneHouse";
		public const string RuinedWall = "=RuinedWall";
		public const string CollapsedTower = "=CollapsedTower";
		public const string RuinedKeep = "=RuinedKeep";
		public const string RuinedStatue = "=RuinedStatue";
		public const string Portal = "portal";
		public const string Church = "church";
		public const string Temple = "temple";
		public const string Tavern = "tavern";
		public const string Smithy = "smithy";
		public const string Stable = "stable";
		public const string Notice = "notice";
		/// <summary>Light sources (2026-10-12: torches, braziers): a torch on a pole, and a fire bowl.</summary>
		public const string TorchPost = "=TorchPost";
		public const string Brazier = "=Brazier";
		public const string Slipway = "=Slipway";
		public const string Shipwreck = "=Shipwreck";
		/// <summary>Mills and the rural works (2026-10-14).</summary>
		public const string TowerMill = "=TowerMill", Watermill = "=Watermill", Sawmill = "=Sawmill", SawPit = "=SawPit", LogPile = "=LogPile",
			PlankStack = "=PlankStack", Granary = "=Granary", Dovecote = "=Dovecote", Apiary = "=Apiary", CharcoalClamp = "=CharcoalClamp",
			LimeKiln = "=LimeKiln", TanningYard = "=TanningYard";
		/// <summary>
		/// The town's other buildings (2026-10-19), by id: none carries a tag an older query matches, so no site that does not
		/// name them changes. A shop-house's trade, sign and wares are PointOfInterestTrades'; a farmstead is the fields'.
		/// </summary>
		public const string WizardTower = "=WizardTower", Library = "=Library", Observatory = "=Observatory",
			Barracks = "=Barracks", GuardHouse = "=GuardHouse", ArcheryRange = "=ArcheryRange", TrainingYard = "=TrainingYard",
			Barn = "=Barn", HayRick = "=HayRick", Pigsty = "=Pigsty", HenHouse = "=HenHouse",
			Healer = "=Healer", Bakery = "=Bakery", Workshop = "=Workshop", DyeWorks = "=DyeWorks", PotteryKiln = "=PotteryKiln", Brewhouse = "=Brewhouse",
			MarketHall = "=MarketHall", ShopHouse = "=ShopHouse", Warehouse = "=Warehouse", Pillory = "=Pillory", MarketCross = "=MarketCross",
			CountingHouse = "=CountingHouse", DigSite = "=DigSite", DigCamp = "=DigCamp";
		/// <summary>Light and lore round a wizard's tower (StructurePieces.Lights.cs).</summary>
		public const string RuneStone = "=RuneStone", Glowjar = "=Glowjar";
		/// <summary>
		/// A settlement's churches by its size and its square's centrepieces (2026-10-19: "more variation for town group types"), by
		/// variant (StructureKitPieceSource's "#" pin): a village's chapel with its bell-cote (the parish church a time in four), a
		/// town's parish church under its spire, a city's or the capital's cathedral fronting its square (tagged "cathedral", so no
		/// "church" query lays one); the stone market cross and the fountain (MarketCross's two variants) and a village green's maypole.
		/// </summary>
		public const string Chapel = "=Church#1", ParishChurch = "=Church#0", VillageChurch = "=Church#1110", Cathedral = "=Cathedral",
			StoneCross = "=MarketCross#0", Fountain = "=MarketCross#1", Maypole = "=Maypole";

		/// <summary>
		/// Whether a slot's query lays a church (any church query, a pinned variant, or the cathedral): it fronts the square as a
		/// landmark and keeps a churchyard (PropsFeature).
		/// </summary>
		public static bool IsChurch(string tag)
		{
			string query = StructureKitPieceSource.BaseQuery(tag);
			return query == Church || query == "=Church" || query == Cathedral;
		}

		/// <summary>
		/// Whether a slot's pieces are a site's landmarks (PointOfInterestLayoutSlot.Landmark: laid before its houses, fronting its square
		/// where it has one): its churches, temple, market hall and library; in a castle's ward its barracks and its stable too, which
		/// are its garrison's and must find room before the houses fill it (2026-10-19).
		/// </summary>
		public static bool IsLandmark(string tag, POIType kind)
		{
			if (IsChurch(tag) || tag == Temple || tag == MarketHall || tag == Library)
			{
				return true;
			}
			return kind == POIType.Castle && (tag == Barracks || tag == Stable);
		}

		// ── Styles ───────────────────────────────────────────────────
		public const string Timber = "timber", Stone = "stone", Hide = "hide", Iron = "iron", Earth = "earth";

		// ── Biomes (asset names in Assets/Templates/Entity/Biomes, exactly: a name no biome has matches nothing) ───
		// The variants' DEFAULT biomes (2026-10-16, phase I5): the biome art authoring writes each biome the weight these lists
		// give it for every variant (where it differs from the variant's weight elsewhere) onto its template, and the cut picks
		// by the template's. A change here reaches a scene once the biome templates are re-authored.
		public static readonly string[] Swamp = { "Swamp", "Mangrove", "Peat Bog", "Wetlands", "Swamp Ruins", "Swamp Temple", "Swamp Cave" };
		// Dust Sea left the arid list 2026-10-18 (decision 44, the Dust Sea round): a sea of dust on airless and thin-aired worlds,
		// nobody lives there, so it takes no arid settlement, inn or lodge variants (its template's own weights, from its file, rule).
		// Wasteland left it 2026-10-25 (decision 96): a war-wasted land takes no arid inn, lodge or hermitage variants either.
		public static readonly string[] Arid = { "Desert", "High Desert", "Badlands", "Salt Flat", "Oasis", "Scrubland" };
		public static readonly string[] Highland = { "Mountain Slope", "Alpine", "Hills", "Rocky Terrain", "Scree", "Karst", "Rocky Coast", "Alpine Meadow" };
		public static readonly string[] Cold = { "Tundra", "Taiga", "Snow", "Glacier", "Ice Sheet", "Ice Shelf", "Permanent Ice" };
		public static readonly string[] Woods = { "Forest", "Woodland", "Taiga", "Jungle", "Bamboo Forest", "Forest Ruins", "Jungle Ruins", "Jungle Temple" };
		public static readonly string[] Steppe = { "Steppe", "Savanna", "Tundra" };
		public static readonly string[] Volcanic = { "Volcanic", "Volcanic Cave", "Volcanic Temple", "Geyser Basin", "Sulphur Flats", "Molten Surface" };

		// ── Race categories (RaceTemplate.Category) ───────────────────
		public static readonly string[] Civil = { "Humanoid", "Beastfolk", "Fey", "Giant" };
		public static readonly string[] Outlaw = { "Humanoid", "Beastfolk", "Giant" };
		public static readonly string[] Monster = { "Beast", "Undead", "Aberration", "Elemental", "Outsider", "Construct", "Draconic", "Plant" };
		public static readonly string[] Aquatic = { "Aquatic" };

		private const PointOfInterestSlotRole Auto = PointOfInterestSlotRole.Auto, Centre = PointOfInterestSlotRole.Centre,
			Ring = PointOfInterestSlotRole.Ring, Perimeter = PointOfInterestSlotRole.Perimeter, Gates = PointOfInterestSlotRole.Gate,
			Towers = PointOfInterestSlotRole.WallTower, Street = PointOfInterestSlotRole.Street, Rows = PointOfInterestSlotRole.Rows,
			Scatter = PointOfInterestSlotRole.Scatter, Line = PointOfInterestSlotRole.Line, Span = PointOfInterestSlotRole.Span,
			Piers = PointOfInterestSlotRole.Pier, Edges = PointOfInterestSlotRole.Edge;

		private const PointOfInterestLayout Single = PointOfInterestLayout.Single, Round = PointOfInterestLayout.Ring,
			Grid = PointOfInterestLayout.Grid, Streets = PointOfInterestLayout.Street, Scattered = PointOfInterestLayout.Scattered,
			Walled = PointOfInterestLayout.Walled, Linear = PointOfInterestLayout.Linear;

		private static PointOfInterestTemplateSpec T(List<PointOfInterestTemplateSpec> all, POIType kind, string variant, PointOfInterestLayout layout, string style)
		{
			var spec = new PointOfInterestTemplateSpec { Kind = kind, Variant = variant, Layout = layout, Style = style };
			all.Add(spec);
			return spec;
		}

		/// <summary>Every template, fresh objects each call, in a fixed order.</summary>
		public static List<PointOfInterestTemplateSpec> All()
		{
			var a = new List<PointOfInterestTemplateSpec>();
			Settlements(a);
			Strongholds(a);
			Works(a);
			Wild(a);
			Sacred(a);
			Dead(a);
			Ruins(a);
			Wetland(a);
			Coast(a);
			Encounters(a);
			Caves(a);
			RoundKinds(a);
			return a;
		}

		/// <summary>The templates of one kind.</summary>
		public static List<PointOfInterestTemplateSpec> For(POIType kind) => All().FindAll(s => s.Kind == kind);

		// ── Settlements ──────────────────────────────────────────────

		private static void Settlements(List<PointOfInterestTemplateSpec> a)
		{
			// Villages: a street of houses round a well; longhouses in the cold, yurts on the steppe. (2026-10-19: "more structure
			// types for towns, villages, and cities": a village may keep a healer, a bakehouse, a brewhouse, a craftsman's shop and a
			// shop or two among its houses, a kiln at its edge, a cross and the stocks on its green. Its barns, sties, hen houses and
			// ricks are its farmsteads', beside its fields (PointOfInterestFarmsteads; 2026-10-19: "Farm houses and barns should be
			// placed next to farm fields and crops"), never among its houses.) Its church is mostly a chapel under a bell-cote, now and
			// then a parish church with its spire; a maypole stands on some greens (2026-10-19: "more variation for town group types").
			// Its lone beekeepers, shepherds, woodcutters and wayside crosses are its outskirts' (PointOfInterestOutskirts).
			T(a, POIType.Village, "Timber", Streets, Timber)
				.S(Granary, 0, 1, Scatter, style: Timber).S(Dovecote, 0, 1, Scatter, style: Stone).S(Apiary, 0, 2, Scatter, style: Timber)
				.S(Well, 1, 1, Centre).S(Stall, 0, 3, Centre).S(House, 7, 15, Street)
				.S(VillageChurch, 0, 1, Street, style: Stone).S(Tavern, 0, 1, Street, style: Timber).S(Smithy, 0, 1, Street, style: Timber).S(Notice, 1, 1, Centre, style: Timber)
				.S(Storage, 4, 10, Scatter).S(Banner, 0, 2, Scatter)
				.S(Healer, 0, 1, Street).S(Bakery, 0, 1, Street).S(Brewhouse, 0, 1, Street).S(Workshop, 0, 1, Street).S(ShopHouse, 0, 2, Street)
				.S(StoneCross, 0, 1, Centre).S(Pillory, 0, 1, Centre)
				.S(PotteryKiln, 0, 1, Edges)
				.S(Maypole, 0, 1, Centre)
				.In(0.1f, Arid).In(0.5f, Highland).In(0.5f, Cold).In(0.4f, Swamp);
			T(a, POIType.Village, "Stone", Streets, Stone)
				.S(Granary, 0, 1, Scatter, style: Timber).S(Dovecote, 0, 1, Scatter, style: Stone).S(Apiary, 0, 2, Scatter, style: Timber)
				.S(Well, 1, 1, Centre).S(Stall, 0, 3, Centre).S(House, 7, 14, Street)
				.S(VillageChurch, 0, 1, Street, style: Stone).S(Tavern, 0, 1, Street, style: Timber).S(Smithy, 0, 1, Street, style: Timber).S(Notice, 1, 1, Centre, style: Timber)
				.S(Storage, 4, 10, Scatter).S(Banner, 0, 2, Scatter)
				.S(Healer, 0, 1, Street).S(Bakery, 0, 1, Street).S(Brewhouse, 0, 1, Street).S(Workshop, 0, 1, Street).S(ShopHouse, 0, 2, Street)
				.S(StoneCross, 0, 1, Centre).S(Pillory, 0, 1, Centre)
				.S(PotteryKiln, 0, 1, Edges)
				.S(Maypole, 0, 1, Centre)
				.Elsewhere(0.3f).In(2f, Arid).In(2f, Highland).Races(Civil);
			T(a, POIType.Village, "Longhouse", Streets, Timber)
				.S(Fire, 1, 1, Centre).S(Longhouse, 3, 6, Street).S(Cottage, 2, 6, Street)
				.S(Storage, 3, 8, Scatter).S(Banner, 1, 2, Scatter)
				.S(Healer, 0, 1, Street).S(Brewhouse, 0, 1, Street)
				.Elsewhere(0.05f).In(2.5f, Cold).In(0.4f, Woods).Races(Civil);
			T(a, POIType.Village, "Yurts", Round, Hide)
				.S(Yurt, 6, 12, Ring).S(Fire, 1, 2, Centre).S(Stall, 0, 2, Scatter).S(Storage, 3, 8, Scatter).S(Bedding, 2, 6, Scatter)
				.Elsewhere(0.02f).In(1.2f, Steppe).In(0.6f, Arid).In(1f, Cold).Pad(0.8f);

			// Towns: two or more streets, a market square.
			// (2026-10-19) A town's shops line its streets among the houses, its healer, bakehouse, brewhouse and workshops, a counting
			// house, perhaps a market hall on the square and a guard's barracks; the wizard's tower, the butts and the practice yard,
			// the dyers and the potters at its edge, where there is room and the smoke and the stink are least in the way.
			// (2026-10-19) Its parish church, its town hall on the square and a fountain on the market every town keeps; a stone cross
			// beside it in some.
			T(a, POIType.Town, "Timber", Streets, Timber)
				.S(Granary, 1, 2, Scatter, style: Timber).S(Dovecote, 0, 1, Scatter, style: Stone).S(TanningYard, 0, 1, Scatter, style: Timber).S(Apiary, 0, 1, Scatter, style: Timber)
				.S(Well, 1, 2, Centre).S(Stall, 3, 8, Centre).S(House, 14, 28, Street)
				.S(ParishChurch, 1, 1, Street, style: Stone).S(Temple, 0, 1, Street, style: Stone).S(Tavern, 1, 2, Street, style: Timber).S(Smithy, 1, 2, Street, style: Timber)
				.S(Stable, 1, 2, Scatter, style: Timber).S(Notice, 1, 2, Centre, style: Timber)
				.S(Storage, 8, 16, Scatter).S(Watchtower, 0, 2, Scatter).S(Banner, 2, 4, Scatter)
				.S(MarketHall, 1, 1, Street).S(ShopHouse, 2, 5, Street).S(Healer, 1, 1, Street).S(Bakery, 1, 1, Street).S(Workshop, 1, 2, Street)
				.S(Brewhouse, 0, 1, Street).S(CountingHouse, 0, 1, Street).S(Barracks, 0, 1, Street).S(Library, 0, 1, Street)
				.S(Pillory, 1, 1, Centre).S(StoneCross, 0, 1, Centre)
				.S(WizardTower, 0, 1, Edges).S(ArcheryRange, 0, 1, Edges).S(TrainingYard, 0, 1, Edges).S(DyeWorks, 0, 1, Edges).S(PotteryKiln, 0, 1, Edges)
				.S(Fountain, 1, 1, Centre)
				.In(0.1f, Arid).In(0.5f, Highland).In(0.5f, Cold);
			T(a, POIType.Town, "Stone", Streets, Stone)
				.S(Granary, 1, 2, Scatter, style: Timber).S(Dovecote, 0, 1, Scatter, style: Stone).S(TanningYard, 0, 1, Scatter, style: Timber).S(Apiary, 0, 1, Scatter, style: Timber)
				.S(Well, 1, 2, Centre).S(Stall, 3, 8, Centre).S(House, 14, 26, Street)
				.S(ParishChurch, 1, 1, Street, style: Stone).S(Temple, 0, 1, Street, style: Stone).S(Tavern, 1, 2, Street, style: Timber).S(Smithy, 1, 2, Street, style: Timber)
				.S(Stable, 1, 2, Scatter, style: Timber).S(Notice, 1, 2, Centre, style: Timber)
				.S(Storage, 8, 16, Scatter).S(Banner, 2, 4, Scatter)
				.S(MarketHall, 1, 1, Street).S(ShopHouse, 2, 5, Street).S(Healer, 1, 1, Street).S(Bakery, 1, 1, Street).S(Workshop, 1, 2, Street)
				.S(Brewhouse, 0, 1, Street).S(CountingHouse, 0, 1, Street).S(Barracks, 0, 1, Street).S(Library, 0, 1, Street)
				.S(Pillory, 1, 1, Centre).S(StoneCross, 0, 1, Centre)
				.S(WizardTower, 0, 1, Edges).S(ArcheryRange, 0, 1, Edges).S(TrainingYard, 0, 1, Edges).S(DyeWorks, 0, 1, Edges).S(PotteryKiln, 0, 1, Edges)
				.S(Fountain, 1, 1, Centre)
				.Elsewhere(0.3f).In(2f, Arid).In(2f, Highland).Races(Civil);
			T(a, POIType.Town, "Palisaded", Walled, Timber)
				.S(Granary, 1, 2, Scatter, style: Timber).S(Dovecote, 0, 1, Scatter, style: Stone).S(TanningYard, 0, 1, Scatter, style: Timber).S(Apiary, 0, 1, Scatter, style: Timber)
				.S(Palisade, 1, 1, Perimeter).S(Gate, 2, 2, Gates).S(Watchtower, 2, 4, Towers)
				.S(Well, 1, 1, Centre).S(Stall, 2, 6, Centre).S(House, 13, 25, Street).S(Storage, 6, 12, Scatter).S(Banner, 2, 4, Scatter)
				.S(ParishChurch, 1, 1, Street, style: Stone).S(Temple, 0, 1, Street, style: Stone).S(Tavern, 1, 2, Street, style: Timber).S(Smithy, 1, 2, Street, style: Timber)
				.S(Stable, 1, 2, Scatter, style: Timber).S(Notice, 1, 2, Centre, style: Timber)
				.S(ShopHouse, 2, 4, Street).S(Healer, 1, 1, Street).S(Bakery, 1, 1, Street).S(Workshop, 1, 2, Street)
				.S(Brewhouse, 0, 1, Street).S(Barracks, 0, 1, Street).S(GuardHouse, 0, 2, Street)
				.S(Pillory, 1, 1, Centre).S(StoneCross, 0, 1, Centre)
				.S(ArcheryRange, 0, 1, Edges).S(TrainingYard, 0, 1, Edges).S(PotteryKiln, 0, 1, Edges)
				.S(MarketHall, 1, 1, Street).S(Fountain, 1, 1, Centre)
				.Elsewhere(0.6f).In(1.2f, Woods).In(0.2f, Arid).Races(Civil);

			// Cities: walled in stone, gatehouses on the main streets, a keep in the middle. (2026-10-19) A cathedral fronting the square
			// and a parish church or two; fountains on the market in place of the second and third wells (wells stand
			// PointOfInterestLayouts.WellSpacing apart; a fountain is no well); its cemetery outside the walls (PointOfInterestOutskirts).
			T(a, POIType.City, "Stone", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 2, 4, Gates).S(WallTower, 6, 12, Towers)
				.S(Keep, 1, 1, Centre).S(Well, 1, 1, Centre).S(Stall, 6, 12, Centre)
				.S(House, 20, 36, Street).S(House, 18, 34, Street, style: Timber)
				.S(ParishChurch, 1, 1, Street, style: Stone).S(Temple, 1, 1, Street, style: Stone).S(Tavern, 2, 2, Street, style: Timber).S(Smithy, 2, 2, Street, style: Timber)
				.S(Stable, 1, 2, Scatter, style: Timber).S(Notice, 2, 3, Centre, style: Timber)
				.S(Storage, 10, 20, Scatter).S(Banner, 4, 8, Scatter)
				// (2026-10-19) Every trade and calling a city keeps.
				.S(MarketHall, 1, 1, Street).S(Library, 1, 1, Street).S(ShopHouse, 4, 8, Street).S(Healer, 1, 2, Street).S(Bakery, 1, 2, Street)
				.S(Workshop, 2, 2, Street).S(Brewhouse, 1, 2, Street).S(CountingHouse, 1, 1, Street).S(Barracks, 1, 1, Street).S(GuardHouse, 0, 2, Street)
				.S(Warehouse, 0, 1, Street).S(Pillory, 1, 1, Centre).S(StoneCross, 1, 1, Centre)
				.S(WizardTower, 1, 1, Edges).S(Observatory, 0, 1, Edges).S(TrainingYard, 1, 1, Edges).S(ArcheryRange, 0, 1, Edges)
				.S(DyeWorks, 0, 1, Edges).S(PotteryKiln, 0, 1, Edges)
				.S(Cathedral, 1, 1, Street).S(Fountain, 1, 2, Centre)
				.Elsewhere(1f).In(1.5f, Arid).In(1.5f, Highland);
			T(a, POIType.City, "Timber", Walled, Timber)
				.S(Palisade, 1, 1, Perimeter).S(Gate, 2, 4, Gates).S(Watchtower, 6, 10, Towers)
				.S(Keep, 1, 1, Centre, style: Stone).S(Well, 1, 1, Centre).S(Stall, 6, 12, Centre)
				.S(House, 38, 68, Street).S(Storage, 10, 20, Scatter).S(Banner, 4, 8, Scatter)
				.S(ParishChurch, 1, 1, Street, style: Stone).S(Temple, 1, 1, Street, style: Stone).S(Tavern, 2, 2, Street, style: Timber).S(Smithy, 2, 2, Street, style: Timber)
				.S(Stable, 1, 2, Scatter, style: Timber).S(Notice, 2, 3, Centre, style: Timber)
				.S(MarketHall, 1, 1, Street).S(Library, 0, 1, Street).S(ShopHouse, 4, 8, Street).S(Healer, 1, 2, Street).S(Bakery, 1, 2, Street)
				.S(Workshop, 2, 2, Street).S(Brewhouse, 1, 2, Street).S(CountingHouse, 1, 1, Street).S(Barracks, 1, 1, Street).S(GuardHouse, 0, 2, Street)
				.S(Warehouse, 0, 1, Street).S(Pillory, 1, 1, Centre).S(StoneCross, 1, 1, Centre)
				.S(WizardTower, 1, 1, Edges).S(Observatory, 0, 1, Edges).S(TrainingYard, 1, 1, Edges).S(ArcheryRange, 0, 1, Edges)
				.S(DyeWorks, 0, 1, Edges).S(PotteryKiln, 0, 1, Edges)
				.S(Cathedral, 1, 1, Street).S(Fountain, 1, 2, Centre)
				.Elsewhere(0.4f).In(1.5f, Woods).In(1f, Cold).In(0.05f, Arid).Races(Civil);

			// The capital: larger than a city (the planner's footprint), more of everything, the palace at its heart (2026-10-19: its
			// centre slot was the keep; the palace compound, PointOfInterestPalace, stands where the keep stood, the slot changed in place).
			// (2026-10-20: "There should only be one or two of the same type of structure in a town, city, village, capital, etc.") Not
			// more of each building, though: a city and the capital keep one of each landmark and at most two of any other building
			// (PointOfInterestStructureCaps, which PropsFeature holds every slot to), their size in their houses and shop-houses.
			T(a, POIType.Capital, "Stone", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 4, 4, Gates).S(WallTower, 12, 20, Towers)
				.S(Palace, 1, 1, Centre).S(Statue, 2, 2, Centre).S(Obelisk, 1, 1, Centre).S(Well, 1, 1, Centre).S(Stall, 12, 20, Centre)
				.S(House, 40, 64, Street).S(House, 32, 56, Street, style: Timber)
				.S(ParishChurch, 1, 2, Street, style: Stone).S(Temple, 1, 1, Street, style: Stone).S(Tavern, 2, 2, Street, style: Timber).S(Smithy, 2, 2, Street, style: Timber)
				.S(Stable, 2, 2, Scatter, style: Timber).S(Notice, 3, 5, Centre, style: Timber)
				.S(Storage, 20, 30, Scatter).S(Banner, 8, 12, Scatter)
				// (2026-10-19) The realm's seat: every trade and calling, its astronomer's tower and its mage's.
				.S(MarketHall, 1, 1, Street).S(Library, 1, 1, Street).S(ShopHouse, 6, 10, Street).S(Healer, 2, 2, Street).S(Bakery, 2, 2, Street)
				.S(Workshop, 2, 2, Street).S(Brewhouse, 2, 2, Street).S(CountingHouse, 1, 1, Street).S(Barracks, 1, 2, Street).S(GuardHouse, 2, 2, Street)
				.S(Warehouse, 1, 2, Street).S(Pillory, 1, 1, Centre).S(StoneCross, 1, 1, Centre)
				.S(WizardTower, 1, 1, Edges).S(Observatory, 1, 1, Edges).S(TrainingYard, 1, 2, Edges).S(ArcheryRange, 1, 1, Edges)
				.S(DyeWorks, 1, 1, Edges).S(PotteryKiln, 1, 1, Edges)
				.S(Cathedral, 1, 1, Street).S(Fountain, 2, 2, Centre)
				.Sized(false, false, true).In(1.5f, Arid).In(1.5f, Highland);
			T(a, POIType.Capital, "Timber", Walled, Timber)
				.S(Wall, 1, 1, Perimeter, style: Stone).S(Gate, 4, 4, Gates, style: Stone).S(WallTower, 12, 20, Towers, style: Stone)
				.S(Palace, 1, 1, Centre, style: Stone).S(Statue, 2, 2, Centre).S(Well, 1, 1, Centre).S(Stall, 12, 20, Centre)
				.S(House, 64, 112, Street).S(Storage, 20, 30, Scatter).S(Banner, 8, 12, Scatter)
				.S(ParishChurch, 1, 2, Street, style: Stone).S(Temple, 1, 1, Street, style: Stone).S(Tavern, 2, 2, Street, style: Timber).S(Smithy, 2, 2, Street, style: Timber)
				.S(Stable, 2, 2, Scatter, style: Timber).S(Notice, 3, 5, Centre, style: Timber)
				.S(MarketHall, 1, 1, Street).S(Library, 1, 1, Street).S(ShopHouse, 6, 10, Street).S(Healer, 2, 2, Street).S(Bakery, 2, 2, Street)
				.S(Workshop, 2, 2, Street).S(Brewhouse, 2, 2, Street).S(CountingHouse, 1, 1, Street).S(Barracks, 1, 2, Street).S(GuardHouse, 2, 2, Street)
				.S(Warehouse, 1, 2, Street).S(Pillory, 1, 1, Centre).S(StoneCross, 1, 1, Centre)
				.S(WizardTower, 1, 1, Edges).S(Observatory, 1, 1, Edges).S(TrainingYard, 1, 2, Edges).S(ArcheryRange, 1, 1, Edges)
				.S(DyeWorks, 1, 1, Edges).S(PotteryKiln, 1, 1, Edges)
				.S(Cathedral, 1, 1, Street).S(Fountain, 2, 2, Centre)
				.Sized(false, false, true).Elsewhere(0.4f).In(1.5f, Woods).In(1f, Cold).In(0.05f, Arid).Races(Civil);

			// Ports: a waterfront street, piers out from the shore ahead (the planner turns a port to the sea).
			// A port is a harbour town (2026-10-12: "Harbours should probably have towns to go with them"): its
			// waterfront has the taverns, chapel, smithy and warehouses a town's has, and the ships at its piers and in its
			// roads are PointOfInterestHarbour's.
			T(a, POIType.Port, "Timber", Streets, Timber)
				.S(Pier, 2, 4, Piers).S(Well, 1, 1, Centre).S(Stall, 3, 6, Centre).S(Notice, 1, 1, Centre).S(House, 12, 22, Street)
				.S(Tavern, 1, 2, Street).S(Church, 0, 1, Street, style: Stone).S(Smithy, 1, 1, Street).S(Stable, 0, 1, Scatter)
				.S(Storage, 12, 22, Scatter).S("=Woodpile", 1, 3, Scatter).S(Lighthouse, 0, 1, Scatter).S(Banner, 1, 3, Scatter)
				// (2026-10-19) The merchants' warehouses and counting house, the ship-chandlers' and coopers' shops (PointOfInterestTrades).
				.S(Warehouse, 1, 2, Street).S(ShopHouse, 3, 6, Street).S(Workshop, 1, 2, Street).S(Bakery, 0, 1, Street).S(Healer, 0, 1, Street)
				.S(CountingHouse, 0, 1, Street).S(Pillory, 0, 1, Centre)
				.In(0.2f, Arid).In(0.6f, Highland);
			T(a, POIType.Port, "Stone", Streets, Stone)
				.S(Pier, 2, 4, Piers, style: Timber).S(Well, 1, 1, Centre).S(Stall, 3, 6, Centre).S(Notice, 1, 1, Centre, style: Timber).S(House, 12, 20, Street)
				.S(Tavern, 1, 2, Street, style: Timber).S(Church, 1, 1, Street).S(Smithy, 1, 1, Street, style: Timber).S(Stable, 0, 1, Scatter, style: Timber)
				.S(Storage, 12, 22, Scatter).S(Lighthouse, 0, 1, Scatter).S(Banner, 1, 3, Scatter)
				.S(Warehouse, 1, 2, Street).S(ShopHouse, 3, 6, Street).S(Workshop, 1, 2, Street).S(Bakery, 0, 1, Street).S(Healer, 0, 1, Street)
				.S(CountingHouse, 0, 1, Street).S(Pillory, 0, 1, Centre)
				.Elsewhere(0.3f).In(2f, Arid).In(2f, Highland).Races(Civil);

			T(a, POIType.TradingPost, "Market", Single, Timber)
				.S(House, 1, 1, Centre).S(Stall, 3, 6, Ring).S(Tent, 1, 3, Scatter, style: Hide).S(Storage, 6, 12, Scatter)
				.S(Well, 0, 1, Scatter).S(Banner, 1, 2, Scatter);
			T(a, POIType.TradingPost, "Caravanserai", Round, Hide)
				.S(Tent, 4, 8, Ring).S(Well, 1, 1, Centre).S(Stall, 2, 4, Scatter, style: Timber).S(Storage, 6, 12, Scatter)
				.Elsewhere(0.2f).In(2f, Arid).In(1.5f, Steppe);

			T(a, POIType.Waystation, "Inn", Single, Timber)
				.S(House, 1, 1, Centre).S(LeanTo, 1, 2, Scatter).S(Well, 0, 1, Scatter).S(Storage, 2, 5, Scatter).S(Fire, 0, 1, Scatter).S(TorchPost, 1, 2, Scatter);
			T(a, POIType.Waystation, "Stone Inn", Single, Stone)
				.S(StoneHouse, 1, 1, Centre).S(LeanTo, 1, 2, Scatter).S(Well, 1, 1, Scatter).S(Storage, 2, 5, Scatter)
				.Elsewhere(0.3f).In(2f, Arid).In(2f, Highland);
		}

		// ── Strongholds ─────────────────────────────────────────────

		private static void Strongholds(List<PointOfInterestTemplateSpec> a)
		{
			// (2026-10-19) A garrison drills: a practice yard in a keep's ward, a castle's barracks and its butts. (2026-10-19: "the large
			// castles should have an interior town/village with at least a barracks and stable") Every castle's ward is its village:
			// the garrison's barracks and the stable for its horses, a smithy, a bakehouse, houses backed onto the curtain round the
			// keep, perhaps a chapel; its well out in the open ward, laid after the buildings (beside the keep, where a centrepiece
			// goes, it took the room a small bailey's barracks or stable needed). A keep's ward stays as it was.
			T(a, POIType.Keep, "Stone", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(WallTower, 0, 4, Towers)
				.S(Keep, 1, 1, Centre).S(Well, 0, 1, Centre).S(Storage, 3, 6, Scatter).S(Banner, 2, 4, Scatter).S(Brazier, 1, 3, Scatter)
				.S(TrainingYard, 0, 1, Edges);
			T(a, POIType.Keep, "Palisade", Walled, Timber)
				.S(Palisade, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(Watchtower, 1, 1, Centre).S(Longhouse, 1, 2, Rows)
				.S(Storage, 3, 6, Scatter).S(Banner, 2, 4, Scatter).S(Fire, 0, 1, Scatter).S(TorchPost, 2, 4, Scatter)
				.S(TrainingYard, 0, 1, Edges)
				.Elsewhere(0.4f).In(1.5f, Woods).In(1.5f, Cold).In(0.1f, Arid).Races(Civil);

			T(a, POIType.Castle, "Stone", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 2, Gates).S(WallTower, 4, 8, Towers)
				.S(Keep, 1, 1, Centre).S(Well, 1, 1, Scatter).S(House, 4, 10, Street)
				.S(Storage, 4, 8, Scatter).S(Banner, 3, 6, Scatter).S(Brazier, 2, 4, Scatter)
				.S(Barracks, 1, 1, Street).S(Bakery, 1, 1, Street).S(TrainingYard, 1, 1, Edges).S(ArcheryRange, 0, 1, Edges)
				.S(Stable, 1, 1, Street, style: Timber).S(Smithy, 1, 1, Street, style: Timber).S(Chapel, 0, 1, Street, style: Stone);
			T(a, POIType.Castle, "Highland", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(WallTower, 6, 10, Towers)
				.S(Keep, 1, 1, Centre).S(Well, 1, 1, Scatter).S(Longhouse, 2, 4, Street, style: Timber)
				.S(Storage, 4, 8, Scatter).S(Banner, 3, 6, Scatter).S(Brazier, 2, 4, Scatter)
				.S(Barracks, 1, 1, Street).S(TrainingYard, 1, 1, Edges)
				.S(Stable, 1, 1, Street, style: Timber).S(Smithy, 1, 1, Street, style: Timber).S(Bakery, 1, 1, Street).S(Chapel, 0, 1, Street, style: Stone).S(House, 0, 4, Street)
				.Elsewhere(0.3f).In(2f, Highland).In(1.5f, Cold).Races(Civil);

			T(a, POIType.Fortress, "Stone", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 2, 2, Gates).S(WallTower, 6, 10, Towers)
				.S(Keep, 1, 1, Centre).S(Watchtower, 2, 4, Scatter, style: Timber).S(Longhouse, 3, 6, Street, style: Timber)
				.S(Tent, 4, 10, Rows, style: Hide).S(Storage, 6, 12, Scatter).S(Banner, 6, 10, Scatter).S(Brazier, 2, 5, Scatter).S(TorchPost, 2, 4, Scatter)
				.S(Barracks, 1, 2, Street).S(GuardHouse, 0, 2, Street).S(TrainingYard, 1, 1, Edges).S(ArcheryRange, 1, 1, Edges);
			T(a, POIType.Fortress, "Palisade", Walled, Timber)
				.S(Palisade, 1, 1, Perimeter).S(Gate, 2, 2, Gates).S(Watchtower, 4, 8, Towers)
				.S(Longhouse, 2, 4, Street).S(Tent, 6, 14, Rows, style: Hide).S(Fire, 1, 2, Centre)
				.S(Storage, 6, 12, Scatter).S(Banner, 6, 10, Scatter).S(TorchPost, 3, 6, Scatter)
				.S(TrainingYard, 0, 1, Edges).S(ArcheryRange, 0, 1, Edges)
				.Elsewhere(0.5f).In(1.5f, Woods).In(1.5f, Steppe).Races(Outlaw);

			T(a, POIType.Tower, "Timber", Single, Timber)
				.S(Watchtower, 1, 1, Centre).S(Storage, 1, 3, Scatter).S(Banner, 0, 1, Scatter);
			T(a, POIType.Tower, "Stone", Single, Stone)
				.S(WallTower, 1, 1, Centre).S(Storage, 1, 3, Scatter).S(Banner, 0, 1, Scatter)
				.Elsewhere(0.4f).In(2f, Arid).In(2f, Highland);
			// A mage's tower alone in the wilds (2026-10-19: "Wizard tower (with optional library?)"): its rune stones and glowing jars
			// about it, a hive and a well for the long years. And an astronomer's tower ("giant telescopes to see the stars?"), best
			// under the dry, clear skies of the high and arid places.
			T(a, POIType.Tower, "Wizard", Single, Stone)
				.S(WizardTower, 1, 1, Centre).S(RuneStone, 2, 4, Scatter).S(Glowjar, 1, 3, Scatter, style: Timber).S(Well, 0, 1, Scatter)
				.S(Apiary, 0, 1, Scatter, style: Timber).S(Storage, 1, 2, Scatter, style: Timber).S(Statue, 0, 1, Scatter)
				.Weighs(0.5f).Elsewhere(1f).In(1.4f, Woods).In(1.2f, Swamp);
			T(a, POIType.Tower, "Observatory", Single, Stone)
				.S(Observatory, 1, 1, Centre).S(Storage, 1, 2, Scatter, style: Timber).S(Brazier, 0, 2, Scatter).S(Well, 0, 1, Scatter)
				.Weighs(0.35f).Elsewhere(0.6f).In(2f, Highland).In(1.6f, Arid);
		}

		// ── Works: bridges, mines, quarries, lighthouses ────────────

		private static void Works(List<PointOfInterestTemplateSpec> a)
		{
			// Spans laid across the river the planner sited it on, as long as the water is wide (PropsFeature.MeasureCrossing).
			T(a, POIType.Bridge, "Timber", Linear, Timber).S(Bridge, 1, 1, Span).Pad(0f);

			// The adit itself is dug by the cave shaper into the rock face the planner found (CaveForm.Mine: its portal, sets,
			// track and stopes are the cave dressing's); the template is the yard before it.
			T(a, POIType.Mine, "Adit", Single, Timber)
				.S(LeanTo, 1, 2, Scatter).S(Storage, 3, 6, Scatter).S("=OreBin", 0, 1, Scatter)
				.S(Rubble, 2, 4, Scatter, style: Stone).S(Fire, 0, 1, Scatter).S(TorchPost, 1, 2, Scatter).Pad(0.7f);
			// Mines that do not all look alike (2026-10-12): a shaft head with its headframe and ore bins; a worked-out
			// one left to the weather; and a delving, cut by a hold's masons. The portal the cave dressing puts at the mouth
			// follows the template's name (PointOfInterestCaveDressing.PortalVariant).
			T(a, POIType.Mine, "Shaft Head", Scattered, Timber)
				.S("=Headframe", 1, 1, Scatter).S("=OreBin", 1, 2, Scatter).S(LeanTo, 1, 2, Scatter).S(Storage, 2, 5, Scatter)
				.S(Rubble, 2, 5, Scatter, style: Stone).S(TorchPost, 1, 3, Scatter).Pad(0.7f);
			T(a, POIType.Mine, "Abandoned", Scattered, Timber)
				.S(LeanTo, 0, 1, Scatter, 0.4f, 0.8f).S(Storage, 1, 3, Scatter, 0.3f, 0.7f).S("=OreBin", 0, 1, Scatter, 0.3f, 0.7f)
				.S(Rubble, 3, 6, Scatter, style: Stone).S(Bones, 0, 2, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.8f).Pad(0.6f).Weighs(0.6f);
			T(a, POIType.Mine, "Delving", Single, Stone)
				.S(StoneHouse, 0, 1, Scatter).S(Storage, 2, 5, Scatter, style: Timber).S("=OreBin", 1, 1, Scatter, style: Timber)
				.S(Statue, 0, 1, Scatter).S(Rubble, 1, 3, Scatter).Pad(0.7f).Weighs(0.6f);

			T(a, POIType.Quarry, "Cut Stone", Scattered, Stone)
				.S(LimeKiln, 0, 1, Scatter).S(Rubble, 4, 8, Scatter).S(Plinth, 2, 4, Scatter).S("=FallenColumn", 1, 3, Scatter)
				.S(LeanTo, 1, 2, Scatter, style: Timber).S(Storage, 3, 6, Scatter, style: Timber).Pad(0.4f);

			T(a, POIType.Lighthouse, "Stone", Single, Stone)
				.S(Lighthouse, 1, 1, Centre).S(Storage, 1, 3, Scatter, style: Timber).S(Cottage, 0, 1, Scatter, style: Timber);

			// A shipwright's yard (2026-10-12: "We need docks (dockyards should be Points of interest)"): a ship on the
			// stocks in the middle of the yard, the planner having turned it to the sea; a pier out over the water, the
			// shipwright's house, sheds and a forge for the ironwork, timber stacked to season. Boats lie at the pier
			// (PointOfInterestHarbour).
			T(a, POIType.Dockyard, "Slipway", Single, Timber)
				.S(Slipway, 1, 1, Centre).S(Pier, 1, 2, Piers).S(House, 1, 1, Scatter).S(LeanTo, 1, 2, Scatter).S(Smithy, 0, 1, Scatter)
				.S("=Woodpile", 2, 4, Scatter).S(Storage, 3, 6, Scatter).S("=Cart", 0, 1, Scatter).S(TorchPost, 1, 3, Scatter).Pad(0.6f);

			// Mills (2026-10-14: "Windmills, watermills, lumbermills, and more"). The tower mill on the rise the planner chose
			// for its wind, among the fields it grinds for (PointOfInterestFields), the miller's cottage and granary by it.
			// (2026-10-15: the post mill it replaced stood on a mound or an open trestle, "weird platforms". The template keeps
			// its name "Post" so re-authoring overwrites the asset: a stale template is left in place and still picked.)
			T(a, POIType.Windmill, "Post", Single, Timber)
				.S(TowerMill, 1, 1, Centre, style: Stone).S(Cottage, 0, 1, Scatter).S(Granary, 0, 1, Scatter).S("=Cart", 0, 1, Scatter).S("=HayBales", 1, 2, Scatter)
				.S(Storage, 1, 3, Scatter).Pad(0.6f);
			// The watermill on the bank the planner sited it on, its front (the wheel's side) to the river, its floor a fixed
			// freeboard over the water (StructurePieces.RiverBankFreeboard). The pad levels its yard to that floor and stops at
			// the shore (PointOfInterestPad.Shore): a whole disc once filled the channel the wheel turns in, and no pad at all
			// left the mill sunk into a bank that rose behind it (2026-10-15).
			T(a, POIType.Watermill, "Wheel", Single, Stone)
				.S(Watermill, 1, 1, Centre).S(Cottage, 0, 1, Scatter, style: Timber).S("=Cart", 0, 1, Scatter, style: Timber).S(Storage, 2, 4, Scatter, style: Timber)
				.S(Apiary, 0, 1, Scatter, style: Timber).Pad(1f);
			// The sawmill likewise, its yard stacked with trunks waiting and planks seasoning, a pit for the odd job.
			T(a, POIType.LumberMill, "Sash", Single, Timber)
				.S(Sawmill, 1, 1, Centre).S(LogPile, 2, 4, Scatter).S(PlankStack, 2, 3, Scatter).S(SawPit, 0, 1, Scatter)
				.S(Cottage, 0, 1, Scatter).S("=Cart", 0, 1, Scatter).S("=Woodpile", 1, 2, Scatter).Pad(1f);
		}

		// ── Wild camps and lairs ────────────────────────────────────

		private static void Wild(List<PointOfInterestTemplateSpec> a)
		{
			T(a, POIType.Camp, "Lean-tos", Round, Timber)
				.S(LeanTo, 3, 6, Ring).S(Fire, 1, 1, Centre).S(Bedding, 2, 5, Scatter, style: Hide).S(Storage, 2, 5, Scatter)
				.In(1.5f, Woods).Pad(0.8f);
			T(a, POIType.Camp, "Hide Tents", Round, Hide)
				.S(Tent, 4, 8, Ring).S(Fire, 1, 1, Centre).S(Bedding, 2, 5, Scatter).S(Storage, 2, 5, Scatter, style: Timber)
				.S(Banner, 0, 1, Scatter, style: Timber).S(TorchPost, 0, 2, Scatter).Races(Outlaw).Pad(0.8f);
			T(a, POIType.Camp, "Yurts", Round, Hide)
				.S(Yurt, 3, 6, Ring).S(Fire, 1, 2, Centre).S(Bedding, 2, 5, Scatter).S(Storage, 2, 5, Scatter, style: Timber)
				.Elsewhere(0.05f).In(2f, Cold).In(1.5f, Steppe).Pad(0.8f);
			T(a, POIType.Camp, "Crude", Round, Timber)
				.S(LeanTo, 2, 4, Ring, 0.2f, 0.5f).S(Fire, 1, 1, Centre).S(Bones, 3, 8, Scatter).S(Rubble, 0, 2, Scatter, style: Stone)
				.Races(Monster).Pad(0.6f);

			T(a, POIType.BanditCamp, "Open", Round, Hide)
				.S(Tent, 3, 6, Ring).S(Fire, 1, 1, Centre).S(Storage, 4, 8, Scatter, style: Timber).S(Banner, 1, 2, Scatter, style: Timber)
				.S(Bedding, 2, 5, Scatter).S(TorchPost, 1, 3, Scatter).Pad(0.8f);
			T(a, POIType.BanditCamp, "Palisade", Walled, Timber)
				.S(Palisade, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(Fire, 1, 1, Centre).S(Tent, 3, 6, Ring, style: Hide)
				.S(Storage, 4, 8, Scatter).S(Banner, 1, 3, Scatter).S(Watchtower, 0, 1, Scatter).S(TorchPost, 2, 4, Scatter)
				.Races(Outlaw).In(1.5f, Woods).Pad(0.8f);

			T(a, POIType.HuntingLodge, "Lodge", Single, Timber)
				.S(House, 1, 1, Centre).S(LeanTo, 1, 2, Scatter).S(Fire, 0, 1, Scatter).S(Storage, 2, 4, Scatter).S(Bones, 1, 3, Scatter)
				.In(0.3f, Arid);
			T(a, POIType.HuntingLodge, "Hide Lodge", Single, Hide)
				.S(Yurt, 1, 1, Centre).S(Tent, 0, 2, Scatter).S(Fire, 1, 1, Scatter).S(Bones, 2, 4, Scatter).S(Storage, 1, 3, Scatter, style: Timber)
				.Elsewhere(0.2f).In(2f, Cold).In(1.5f, Steppe);

			T(a, POIType.LumberCamp, "Timber", Round, Timber)
				.S(SawPit, 1, 1, Scatter).S(LogPile, 1, 3, Scatter).S(PlankStack, 0, 2, Scatter).S(CharcoalClamp, 0, 1, Scatter)
				.S(LeanTo, 2, 4, Ring).S(Fire, 1, 1, Centre).S(Storage, 4, 8, Scatter).S(Cottage, 0, 1, Scatter).S(Bedding, 1, 3, Scatter, style: Hide)
				.Pad(0.7f);

			T(a, POIType.FishingCamp, "Shore", Single, Timber)
				.S(Fire, 1, 1, Centre).S(LeanTo, 1, 3, Ring).S(Pier, 1, 1, Piers).S(Storage, 2, 5, Scatter).S(Bedding, 0, 2, Scatter, style: Hide)
				.Pad(0.7f);
			T(a, POIType.FishingCamp, "Huts", Single, Timber)
				.S(Cottage, 1, 1, Centre).S(Pier, 1, 2, Piers).S(Storage, 2, 5, Scatter).S(Fire, 0, 1, Scatter)
				.Elsewhere(0.6f).In(1.5f, Cold);

			T(a, POIType.MonsterDen, "Bones", Scattered, Earth)
				.S(Bones, 6, 12, Scatter).S(Rubble, 2, 5, Scatter, style: Stone).S(Mound, 0, 2, Scatter).Races(Monster).Pad(0.4f);
			T(a, POIType.MonsterDen, "Lair", Scattered, Timber)
				.S(Fire, 1, 1, Centre).S(LeanTo, 1, 3, Scatter, 0.2f, 0.6f).S(Bones, 4, 10, Scatter).S(Banner, 1, 2, Scatter, 0.2f, 0.5f)
				.S(Rubble, 1, 3, Scatter, style: Stone).Pad(0.4f);

			T(a, POIType.Nest, "Bones", Scattered, Earth)
				.S(Bones, 4, 10, Scatter).S(Rubble, 1, 3, Scatter, style: Stone);
		}

		// ── Sacred ──────────────────────────────────────────────────

		private static void Sacred(List<PointOfInterestTemplateSpec> a)
		{
			T(a, POIType.RitualSite, "Stones", Round, Stone)
				.S(Menhir, 5, 9, Ring).S(Altar, 1, 1, Centre).S(Fire, 1, 1, Centre).S(Bones, 2, 6, Scatter).S(Banner, 0, 2, Scatter, style: Timber)
				.Pad(0.6f);
			T(a, POIType.RitualSite, "Bonefire", Round, Timber)
				.S(Banner, 5, 8, Ring).S(Fire, 1, 1, Centre).S(Altar, 1, 1, Centre, style: Stone).S(Bones, 4, 10, Scatter)
				.Races(Monster).Weathered(PointOfInterestFinish.Charred, 0.4f).Pad(0.6f);

			T(a, POIType.StoneCircle, "Trilithons", Round, Stone)
				.S(CircleStone, 6, 12, Ring).S(Altar, 0, 1, Centre).Pad(0.7f);
			T(a, POIType.StoneCircle, "Menhirs", Round, Stone)
				.S(Menhir, 7, 13, Ring, 0f, 0.3f).S(Menhir, 0, 1, Centre).Pad(0.7f);

			T(a, POIType.Shrine, "Shrine", Single, Stone)
				.S(Shrine, 1, 1, Centre).S(Menhir, 0, 2, Scatter, scaleMin: 0.6f, scaleMax: 0.9f).S(Brazier, 0, 2, Scatter).Pad(0.8f);
			T(a, POIType.Shrine, "Statue", Single, Stone)
				.S(Statue, 1, 1, Centre).S(Altar, 0, 1, Scatter).Pad(0.8f).Weighs(0.5f);

			T(a, POIType.Temple, "Colonnade", Round, Stone)
				.S(Column, 8, 14, Ring).S(Altar, 1, 1, Centre).S(Statue, 2, 4, Centre).S(Shrine, 0, 1, Scatter).S(Brazier, 2, 4, Scatter);
			T(a, POIType.Temple, "Court", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Shrine, 1, 1, Centre).S(Statue, 2, 4, Centre).S(Column, 4, 8, Scatter).S(Well, 0, 1, Scatter).S(Brazier, 2, 4, Scatter);

			T(a, POIType.Monastery, "Cloister", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(Shrine, 1, 1, Centre).S(Statue, 1, 2, Centre).S(Well, 1, 1, Centre)
				.S(StoneHouse, 3, 6, Street).S(Storage, 2, 5, Scatter, style: Timber)
				.S(Dovecote, 0, 1, Scatter).S(Apiary, 1, 2, Scatter, style: Timber).S(Granary, 0, 1, Scatter, style: Timber)
				// (2026-10-19) The house's scriptorium, its infirmary and its brewhouse; its tithe barn is its grange's, by its fields
				// (PointOfInterestFarmsteads).
				.S(Library, 0, 1, Street).S(Healer, 0, 1, Street).S(Brewhouse, 0, 1, Street);
			T(a, POIType.Monastery, "Timber", Walled, Timber)
				.S(Palisade, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(Shrine, 1, 1, Centre, style: Stone).S(Well, 1, 1, Centre, style: Stone)
				.S(Longhouse, 1, 2, Street).S(House, 2, 5, Street).S(Storage, 2, 5, Scatter)
				.S(Apiary, 1, 2, Scatter).S(Granary, 0, 1, Scatter)
				.S(Healer, 0, 1, Street)
				.Elsewhere(0.4f).In(1.5f, Woods).In(1.5f, Cold);

			T(a, POIType.FeyRing, "Ring", Round, Stone)
				.S(Menhir, 7, 13, Ring, 0f, 0.2f, 0.35f, 0.55f).Weathered(PointOfInterestFinish.Mossy, 0.7f).Pad(0f);

			T(a, POIType.LeyNexus, "Obelisk", Round, Stone)
				.S(Menhir, 4, 6, Ring).S(Obelisk, 1, 1, Centre).Pad(0.5f);

			T(a, POIType.FallenStar, "Crater", Scattered, Stone)
				.S(Menhir, 3, 6, Scatter, 0.4f, 0.8f, 0.6f, 1.1f).S(Rubble, 4, 8, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 0.7f).Pad(0f);

			T(a, POIType.CorruptedGrove, "Altar", Single, Stone)
				.S(Altar, 1, 1, Centre).S(Menhir, 2, 5, Scatter, 0.3f, 0.7f).S(Bones, 4, 8, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 0.5f).Pad(0.3f);

			T(a, POIType.Portal, "Arch", Single, Stone)
				.S(Portal, 1, 1, Centre).S(Menhir, 4, 8, Ring, 0f, 0.3f).Pad(0.8f);
		}

		// ── The dead ────────────────────────────────────────────────

		private static void Dead(List<PointOfInterestTemplateSpec> a)
		{
			// An iron railing with one opening at the heading, a tomb in the middle, graves in rows round it.
			T(a, POIType.Graveyard, "Railed", Grid, Stone)
				.S(Fence, 1, 1, Perimeter, style: Iron).S(Mausoleum, 0, 1, Centre).S(Gravestone, 12, 30, Rows, 0f, 0.3f)
				.S(Mound, 0, 4, Rows).Pad(0.8f);
			T(a, POIType.Graveyard, "Earthen", Grid, Earth)
				.S(Mound, 8, 20, Rows, 0f, 0.3f).S(Gravestone, 4, 10, Rows, 0f, 0.4f).S(Menhir, 0, 1, Centre)
				.Elsewhere(0.6f).In(1.5f, Cold).In(1.5f, Steppe).Pad(0.6f);

			T(a, POIType.Barrow, "Ringed", Round, Stone)
				.S(Menhir, 5, 9, Ring, 0f, 0.3f).S(CryptDoor, 1, 1, Centre).S(Mound, 1, 3, Scatter);
			// A barrow being opened (2026-10-19: "archeologists"): the trench cut into its mound, the diggers' finds and their tents.
			T(a, POIType.Barrow, "Excavated", Round, Stone)
				.S(Menhir, 4, 7, Ring, 0.1f, 0.5f).S(DigSite, 1, 1, Centre, style: Earth).S(DigCamp, 1, 2, Scatter, style: Timber)
				.S(Tent, 1, 2, Scatter, style: Hide).S(Storage, 1, 3, Scatter, style: Timber).S(Mound, 0, 2, Scatter)
				.Weighs(0.3f).Pad(0.7f);

			T(a, POIType.Crypt, "Tomb", Single, Stone)
				.S(CryptDoor, 1, 1, Centre).S(Mausoleum, 0, 1, Scatter).S(Gravestone, 3, 8, Scatter, 0.2f, 0.6f).S(Bones, 1, 3, Scatter);

			T(a, POIType.Battlefield, "Field", Scattered, Timber)
				.S(Bones, 10, 25, Scatter).S(Banner, 3, 8, Scatter, 0.4f, 0.8f).S(Palisade, 2, 6, Scatter, 0.5f, 0.9f)
				.S(Storage, 2, 6, Scatter, 0.4f, 0.9f).S(Tent, 1, 3, Scatter, 0.6f, 1f, style: Hide).S(Mound, 2, 5, Scatter)
				.S(Rubble, 2, 5, Scatter, style: Stone).Pad(0.3f);

			T(a, POIType.Ossuary, "Tomb", Single, Stone)
				.S(Mausoleum, 1, 1, Centre).S(Bones, 4, 8, Scatter).S(Gravestone, 0, 3, Scatter, 0.2f, 0.5f);
		}

		// ── Ruins and relics ────────────────────────────────────────

		private static void Ruins(List<PointOfInterestTemplateSpec> a)
		{
			T(a, POIType.Ruins, "Stone", Scattered, Stone)
				.S(RuinedHouse, 2, 5, Scatter).S(RuinedWall, 3, 8, Scatter).S(BrokenColumn, 2, 5, Scatter).S(Rubble, 3, 6, Scatter)
				.S(RuinedStatue, 0, 2, Scatter).Pad(0.5f);
			T(a, POIType.Ruins, "Burned", Scattered, Timber)
				.S(RuinedHouse, 2, 5, Scatter).S(Palisade, 2, 5, Scatter, 0.5f, 0.9f).S(Storage, 2, 5, Scatter, 0.5f, 0.9f)
				.S(Rubble, 1, 3, Scatter, style: Stone).Weathered(PointOfInterestFinish.Charred, 0.7f)
				.In(1.5f, Woods).In(0.3f, Arid).Pad(0.5f);
			T(a, POIType.Ruins, "Temple", Round, Stone)
				.S(Column, 6, 10, Ring, 0.3f, 0.9f).S(Altar, 1, 1, Centre, 0.2f, 0.5f).S(Rubble, 2, 4, Scatter).S(RuinedStatue, 0, 2, Scatter)
				.Weighs(0.5f).In(1.5f, Arid).Pad(0.5f);
			// Ruins under excavation (2026-10-19: "archeologists"): an ancient floor being bared, a trench, the finds and the diggers' camp.
			T(a, POIType.Ruins, "Excavation", Scattered, Stone)
				.S(DigSite, 1, 2, Scatter, style: Earth).S(DigCamp, 1, 3, Scatter, style: Timber).S(RuinedWall, 2, 5, Scatter).S(BrokenColumn, 2, 4, Scatter)
				.S(RuinedHouse, 0, 2, Scatter).S(Tent, 1, 2, Scatter, style: Hide).S(Storage, 2, 4, Scatter, style: Timber).S(Rubble, 2, 4, Scatter)
				.Weighs(0.4f).In(1.3f, Arid).Pad(0.6f);

			T(a, POIType.RuinedTower, "Tower", Single, Stone)
				.S(CollapsedTower, 1, 1, Centre).S(Rubble, 1, 3, Scatter).S(RuinedWall, 0, 2, Scatter);
			T(a, POIType.RuinedTower, "Keep", Single, Stone)
				.S(RuinedKeep, 1, 1, Centre).S(Rubble, 1, 3, Scatter).Sized(false, true, true).Weighs(0.5f);

			T(a, POIType.Monument, "Obelisk", Single, Stone)
				.S(Obelisk, 1, 1, Centre).S(Menhir, 0, 4, Ring, 0f, 0.3f, 0.6f, 0.8f);
			T(a, POIType.Monument, "Statue", Single, Stone)
				.S(Statue, 1, 1, Centre).S(Column, 2, 4, Ring, 0f, 0.6f).S(Plinth, 0, 2, Scatter);

			T(a, POIType.Statue, "Standing", Single, Stone).S(Statue, 1, 1, Centre, 0f, 0.3f);
			T(a, POIType.Statue, "Broken", Single, Stone).S(RuinedStatue, 1, 1, Centre).S(Rubble, 0, 1, Scatter).Weighs(0.6f);

			T(a, POIType.Obelisk, "Obelisk", Single, Stone).S(Obelisk, 1, 1, Centre).S(Menhir, 0, 2, Scatter, 0f, 0f, 0.5f, 0.7f);

			// Columns and statues lining both sides of the road (along the heading); decay breaks some of them.
			T(a, POIType.AncientRoad, "Colonnade", Linear, Stone)
				.S(Column, 4, 10, Street, 0.1f, 0.8f).S(RuinedStatue, 0, 2, Street).S(Rubble, 1, 3, Scatter).Pad(0f);

			T(a, POIType.AbandonedFarm, "Farmstead", Scattered, Timber)
				.S(RuinedHouse, 1, 1, Centre).S(Well, 0, 1, Scatter, style: Stone).S(Palisade, 2, 5, Rows, 0.3f, 0.8f)
				.S(Storage, 2, 5, Scatter, 0.4f, 0.9f).S(LeanTo, 0, 2, Scatter, 0.4f, 0.8f).Pad(0.7f);
			T(a, POIType.AbandonedFarm, "Burned", Scattered, Timber)
				.S(RuinedHouse, 1, 2, Scatter).S(Palisade, 2, 4, Scatter, 0.5f, 0.9f).S(Storage, 2, 4, Scatter, 0.5f, 0.9f)
				.Weathered(PointOfInterestFinish.Charred, 0.7f).Weighs(0.5f).Pad(0.7f);

			T(a, POIType.Hermitage, "Cottage", Single, Timber)
				.S(Cottage, 1, 1, Centre).S(Shrine, 0, 1, Scatter, style: Stone).S(Storage, 1, 2, Scatter).S(Fire, 0, 1, Scatter);
			T(a, POIType.Hermitage, "Stone", Single, Stone)
				.S(StoneHouse, 1, 1, Centre).S(Shrine, 0, 1, Scatter).S(Well, 0, 1, Scatter)
				.Elsewhere(0.4f).In(2f, Highland).In(2f, Arid);

			T(a, POIType.Oasis, "Camp", Round, Hide)
				.S(Tent, 3, 6, Ring).S(Well, 1, 1, Centre, style: Stone).S(Stall, 1, 3, Scatter, style: Timber).S(Storage, 2, 5, Scatter, style: Timber)
				.Pad(0.6f);
		}

		// ── Wetland ─────────────────────────────────────────────────

		private static void Wetland(List<PointOfInterestTemplateSpec> a)
		{
			T(a, POIType.SunkenTemple, "Colonnade", Round, Stone)
				.S(Column, 6, 10, Ring, 0.3f, 0.8f).S(Altar, 1, 1, Centre, 0.2f, 0.5f).S(Statue, 1, 2, Centre, 0.4f, 0.8f)
				.S(Rubble, 2, 4, Scatter).Weathered(PointOfInterestFinish.Algae, 0.8f).Wet().Pad(0.4f);
			T(a, POIType.SunkenTemple, "Idol", Single, Stone)
				.S(Statue, 1, 1, Centre, 0.2f, 0.4f).S(Column, 3, 6, Ring, 0.5f, 0.9f).S(Bones, 3, 6, Scatter)
				.Weathered(PointOfInterestFinish.Algae, 0.8f).Races(Monster).Wet().Pad(0.4f);

			T(a, POIType.DrownedVillage, "Stilts", Scattered, Timber)
				.S(Stilt, 2, 5, Scatter, 0.4f, 0.8f).S(RuinedHouse, 3, 7, Scatter).S(Pier, 1, 3, Scatter, 0.4f, 0.8f)
				.S(Storage, 2, 5, Scatter, 0.5f, 0.9f).Weathered(PointOfInterestFinish.Algae, 0.7f).Wet().Pad(0.3f);

			T(a, POIType.WitchHut, "Stilt Hut", Single, Timber)
				.S(Stilt, 1, 1, Centre).S(Fire, 1, 1, Scatter).S(Bones, 2, 4, Scatter).S(Altar, 0, 1, Scatter, style: Stone)
				.S(Storage, 1, 3, Scatter).Pad(0.6f);
			T(a, POIType.WitchHut, "Cottage", Single, Timber)
				.S(Cottage, 1, 1, Centre).S(Fire, 1, 1, Scatter).S(Bones, 1, 3, Scatter).S(Storage, 1, 2, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.6f).Weighs(0.5f).Pad(0.6f);

			T(a, POIType.StiltVillage, "Stilts", Streets, Timber)
				.S(Fire, 1, 1, Centre).S(Stilt, 8, 16, Street).S(Pier, 1, 3, Piers).S(Storage, 4, 8, Scatter).Pad(0.4f);

			T(a, POIType.BogShrine, "Shrine", Single, Stone)
				.S(Shrine, 1, 1, Centre).S(Menhir, 2, 4, Ring, 0.1f, 0.4f, 0.6f, 0.8f).S(Bones, 0, 2, Scatter);

			T(a, POIType.WispHollow, "Stones", Round, Stone)
				.S(Menhir, 5, 9, Ring, 0.2f, 0.6f, 0.5f, 0.9f).S(Altar, 0, 1, Centre).Weathered(PointOfInterestFinish.Mossy, 0.6f);
		}

		// ── Coast and the sea floor ─────────────────────────────────

		private static void Coast(List<PointOfInterestTemplateSpec> a)
		{
			// A wreck is its hull on the beach (a cog's stern half, or a longship's bones) and its cargo strewn about it
			// (2026-10-12: "Shipwrecks don't have a wrecked ship mesh?").
			T(a, POIType.Wreck, "Debris", Scattered, Timber)
				.S(Shipwreck, 1, 1, Centre).S(Storage, 3, 8, Scatter, 0.3f, 0.8f).S(Rubble, 0, 2, Scatter, style: Stone);

			T(a, POIType.SmugglersCove, "Camp", Single, Timber)
				.S(Fire, 1, 1, Centre).S(Tent, 2, 4, Ring, style: Hide).S(Pier, 0, 1, Piers).S(Storage, 8, 14, Scatter).S(LeanTo, 1, 2, Scatter);

			T(a, POIType.PirateCove, "Hideout", Single, Timber)
				.S(House, 1, 1, Centre).S(Tent, 3, 6, Ring, style: Hide).S(Pier, 1, 2, Piers).S(Storage, 8, 16, Scatter)
				.S(Banner, 2, 4, Scatter).S(Fire, 1, 1, Scatter);

			T(a, POIType.SunkenShip, "Debris", Scattered, Timber)
				.S(Shipwreck, 1, 1, Centre).S(Storage, 3, 8, Scatter, 0.4f, 0.9f).S(Banner, 1, 2, Scatter, 0.6f, 0.9f).S(Bones, 0, 3, Scatter)
				.Weathered(PointOfInterestFinish.Algae).Pad(0f);

			T(a, POIType.SunkenRuins, "Stone", Scattered, Stone)
				.S(RuinedHouse, 2, 5, Scatter).S(RuinedWall, 3, 8, Scatter).S(BrokenColumn, 2, 6, Scatter).S(RuinedStatue, 0, 2, Scatter)
				.S(Rubble, 3, 6, Scatter).Weathered(PointOfInterestFinish.Algae).Pad(0f);

			// A walled city gone under: walls, houses and the keep laid past the ruin threshold come up as their ruins.
			T(a, POIType.SunkenCity, "Drowned", Walled, Stone)
				.S(Wall, 1, 1, Perimeter, 0.4f, 0.9f).S(Gate, 2, 2, Gates, 0.5f, 0.8f).S(Keep, 1, 1, Centre, 0.6f, 0.8f)
				.S(Statue, 1, 3, Centre, 0.3f, 0.9f).S(House, 15, 35, Street, 0.5f, 1f).S(Column, 4, 10, Scatter, 0.3f, 0.9f)
				.S(Rubble, 6, 12, Scatter).Weathered(PointOfInterestFinish.Algae).Pad(0f);
			/* The sea floor's own sites (2026-10-25, phase I2m, decision 36 (b)): the land kinds' pieces under the sea's growth until
			 * I7 gives them their own (drowned shrine, kraken den, crystal garden, colossus). */
			T(a, POIType.SunkenShrine, "Drowned", Single, Stone)
				.S(Shrine, 1, 1, Centre, 0.2f, 0.6f).S(Menhir, 0, 2, Scatter, 0.2f, 0.6f, 0.6f, 0.9f).S(Rubble, 1, 3, Scatter)
				.Weathered(PointOfInterestFinish.Algae).Pad(0f);
			T(a, POIType.SeaMonsterDen, "Bones", Scattered, Stone)
				.S(Bones, 6, 12, Scatter).S(Rubble, 3, 6, Scatter).S(Menhir, 0, 2, Scatter, 0.3f, 0.7f, 0.5f, 0.8f)
				.Weathered(PointOfInterestFinish.Algae).Pad(0f);
			T(a, POIType.SeaCrystalFormation, "Spires", Scattered, Stone)
				.S(Menhir, 2, 5, Scatter, 0f, 0.3f, 0.5f, 1f).S(Rubble, 1, 2, Scatter).Pad(0f);
			T(a, POIType.SunkenMonument, "Colossus", Single, Stone)
				.S(Statue, 1, 1, Centre, 0.2f, 0.6f).S(Column, 2, 4, Ring, 0.3f, 0.8f).S(Rubble, 1, 3, Scatter)
				.Weathered(PointOfInterestFinish.Algae).Pad(0f);
			T(a, POIType.SunkenStatue, "Toppled", Single, Stone)
				.S(RuinedStatue, 1, 1, Centre).S(Rubble, 0, 2, Scatter).Weathered(PointOfInterestFinish.Algae).Pad(0f);

			T(a, POIType.SunkenCity, "Reef Halls", Streets, Stone)
				.S(Statue, 1, 2, Centre, 0.2f, 0.5f).S(StoneHouse, 15, 30, Street, 0.3f, 0.7f).S(Column, 6, 12, Scatter, 0.2f, 0.6f)
				.S(Rubble, 4, 8, Scatter).Weathered(PointOfInterestFinish.Algae).Races(Aquatic).Pad(0f);
		}

		// ── Caves: built in the carved chamber ─────────────────────

		/// <summary>
		/// The carved cave kinds (their shaper digs the system; Overhang and NaturalArch need no template): small and medium
		/// caves hold a camp or a den in their great cavern, a large cave's its dungeon entrance (PointOfInterestFeatureDefaults)
		/// among the bones and banners of whatever guards it.
		/// </summary>
		private static void Caves(List<PointOfInterestTemplateSpec> a)
		{
			foreach (POIType kind in new[] { POIType.Cave, POIType.IceCave, POIType.LavaTube })
			{
				T(a, kind, "Camp", Single, Timber)
					.S(Fire, 1, 1, Centre, style: Stone).S(Bedding, 1, 3, Scatter, style: Hide).S(Storage, 1, 3, Scatter).S(Bones, 0, 2, Scatter).S(TorchPost, 1, 2, Scatter)
					.Sized(true, true, false).Chamber().Pad(0f);
				T(a, kind, "Den", Scattered, Earth)
					.S(Bones, 3, 6, Scatter).S(Rubble, 0, 2, Scatter, style: Stone).S(Mound, 0, 1, Scatter)
					.Sized(true, true, false).Races(Monster).Chamber().Pad(0f);
				// The gate against the great cavern's wall, its braziers, guardians, banners and fallen stone are the dungeon
				// entrance's own (DungeonEntranceFeature): what the props strew is the bones of whatever went in.
				T(a, kind, "Deep", Scattered, Earth)
					.S(Bones, 2, 5, Scatter)
					.Sized(false, false, true).Chamber().Pad(0f);
			}
			T(a, POIType.Grotto, "Shrine", Single, Stone)
				.S(Shrine, 1, 1, Centre).S(Menhir, 0, 2, Scatter, 0f, 0.3f, 0.4f, 0.6f)
				.Weathered(PointOfInterestFinish.Mossy, 0.7f).Chamber().Pad(0f);
			T(a, POIType.Grotto, "Hollow", Scattered, Stone)
				.S(Rubble, 1, 2, Scatter).S(Bones, 0, 2, Scatter).Chamber().Pad(0f);
			T(a, POIType.SeaCave, "Cache", Scattered, Timber)
				.S(Storage, 2, 5, Scatter, 0.2f, 0.6f).S(Rubble, 0, 2, Scatter, style: Stone)
				.Weathered(PointOfInterestFinish.Algae, 0.5f).Chamber().Wet().Pad(0f);
		}

		// ── Encounters and resources ────────────────────────────────

		private static void Encounters(List<PointOfInterestTemplateSpec> a)
		{
			// The door set into the rock face the planner found, at the threshold its shaper cut there (DungeonEntranceShaper):
			// the centrepiece only, laid square to the face; its forecourt is dressed about the door's axis by the dungeon
			// entrance feature, never strewn up the cliff (2026-10-18). The dungeon behind it is hand-made (2026-10-10).
			T(a, POIType.DungeonEntrance, "Door", Single, Stone)
				.S(DungeonPortal, 1, 1, Centre).In(1.4f, Highland).In(1.3f, Arid);
			T(a, POIType.DungeonEntrance, "Barrow", Single, Stone)
				.S(DungeonBarrow, 1, 1, Centre).Weathered(PointOfInterestFinish.Mossy, 0.6f)
				.Elsewhere(0.5f).In(1.6f, Cold).In(1.6f, Steppe).In(1.2f, Woods);

			T(a, POIType.BossLair, "Stones", Round, Stone)
				.S(Menhir, 6, 10, Ring, 0.2f, 0.6f).S(Altar, 1, 1, Centre).S(Bones, 8, 16, Scatter).S(RuinedStatue, 1, 2, Scatter)
				.S(Banner, 2, 4, Scatter, 0.3f, 0.7f, style: Timber).Pad(0.6f);
			T(a, POIType.BossLair, "Warcamp", Walled, Timber)
				.S(Palisade, 1, 1, Perimeter, 0.2f, 0.5f).S(Gate, 1, 1, Gates).S(Fire, 1, 1, Centre).S(Banner, 4, 8, Ring)
				.S(Bones, 6, 12, Scatter).S(Tent, 2, 4, Scatter, style: Hide).Weighs(0.6f).Pad(0.6f);

			T(a, POIType.OreVein, "Spoil", Scattered, Stone)
				.S(Rubble, 1, 3, Scatter).S(Storage, 0, 2, Scatter, 0.2f, 0.6f, style: Timber).Pad(0f);
			T(a, POIType.CrystalFormation, "Spires", Scattered, Stone)
				.S(Menhir, 2, 5, Scatter, 0f, 0.3f, 0.5f, 1f).S(Rubble, 1, 2, Scatter).Pad(0f);
			T(a, POIType.AncientTree, "Shrine", Round, Stone)
				.S(Menhir, 3, 5, Ring, 0f, 0.3f, 0.4f, 0.6f).S(Shrine, 0, 1, Scatter).Weathered(PointOfInterestFinish.Mossy, 0.8f).Pad(0f);
			T(a, POIType.HerbGrove, "Stones", Scattered, Stone)
				.S(Menhir, 1, 3, Scatter, 0f, 0.2f, 0.3f, 0.5f).S(Storage, 0, 1, Scatter, style: Timber)
				.Weathered(PointOfInterestFinish.Mossy, 0.6f).Pad(0f);
			// Black glass: the charred finish on stone.
			T(a, POIType.ObsidianField, "Shards", Scattered, Stone)
				.S(Menhir, 4, 10, Scatter, 0.2f, 0.6f, 0.5f, 1.2f).S(Rubble, 2, 5, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 1f).Pad(0f);
		}

		// ── The biome rounds' own kinds (2026-10-25, phase NEW-POI-KINDS) ─────

		/// <summary>
		/// Placeholders on the kit's pieces for the biome rounds' kinds that the kit already reads as (a sunk gate, a flooded forum, a
		/// marsh tower, a toppled colossus, a rune door, a chapel, a well-head, a quarantined hamlet), until I7 gives each its signature
		/// pieces. The kinds the kit cannot show yet (the canyon's lip, a cinder cone, a reed isle, a war engine) have none: they are
		/// named and discovered but lay nothing. No biome hosts any of them yet, so no scene builds one.
		/// </summary>
		private static void RoundKinds(List<PointOfInterestTemplateSpec> a)
		{
			// Volcanic Temple (decision 95): the smiths' relics in the cinder, the phoenix's stone ring and pyre, a shrine in a tube mouth.
			T(a, POIType.AncientForge, "Relics", Scattered, Stone)
				.S(RuinedWall, 1, 2, Scatter).S(Menhir, 1, 1, Centre, 0.2f, 0.5f, 0.8f, 1.2f).S(Brazier, 1, 2, Scatter).S(Rubble, 3, 6, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 0.5f).Pad(0.4f);
			T(a, POIType.PhoenixNest, "Pyre", Round, Stone)
				.S(Menhir, 5, 8, Ring, 0f, 0.3f, 0.5f, 0.7f).S(Fire, 1, 1, Centre).S(Bones, 0, 2, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 0.6f).Pad(0.5f);
			T(a, POIType.LavaCaveTemple, "Grotto", Single, Stone)
				.S(Shrine, 1, 1, Centre).S(Altar, 0, 1, Scatter).S(Brazier, 1, 2, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 0.3f).Pad(0.6f);

			// Swamp Ruins (decision 87): the drowned city's gate sunk to its arch in black water and its flooded forum.
			T(a, POIType.MireGate, "Drowned", Single, Stone)
				.S(Gate, 1, 1, Centre, 0.3f, 0.6f).S(RuinedWall, 2, 4, Scatter).S(Rubble, 1, 3, Scatter)
				.Weathered(PointOfInterestFinish.Algae, 0.8f).Wet().Pad(0.3f);
			T(a, POIType.SunkenPlaza, "Forum", Round, Stone)
				.S(BrokenColumn, 6, 10, Ring).S(Altar, 1, 1, Centre, 0.2f, 0.5f).S(Menhir, 2, 6, Scatter, 0.1f, 0.4f, 0.4f, 0.6f).S(Rubble, 2, 4, Scatter)
				.Weathered(PointOfInterestFinish.Algae, 0.8f).Wet().Pad(0.2f);
			T(a, POIType.MarshWatchtower, "Mound", Single, Timber)
				.S(Watchtower, 1, 1, Centre).S(Fire, 0, 1, Scatter).S(Storage, 1, 3, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.5f).Pad(0.7f);
			T(a, POIType.FallenColossus, "Toppled", Single, Stone)
				.S(RuinedStatue, 1, 1, Centre, 0f, 0f, 2.2f, 3f).S(Plinth, 0, 1, Scatter).S(Rubble, 2, 4, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.6f).Pad(0.4f);
			T(a, POIType.SealedVault, "Rune Door", Single, Stone)
				.S(CryptDoor, 1, 1, Centre).S(RuneStone, 2, 4, Scatter).S(Rubble, 0, 2, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.5f).Pad(0.6f);

			// Swamp Temple (decision 88): the frogs' mound and its stone, a fowler's reed hide, a potters' brick kiln.
			T(a, POIType.FrogShrine, "Mound", Single, Earth)
				.S(Mound, 1, 1, Centre).S(Shrine, 1, 1, Centre, style: Stone).S(Menhir, 2, 4, Ring, 0f, 0.2f, 0.4f, 0.6f, style: Stone)
				.Weathered(PointOfInterestFinish.Mossy, 0.5f).Pad(0.6f);
			T(a, POIType.FowlersHide, "Hide", Single, Timber)
				.S(LeanTo, 1, 1, Centre).S(Storage, 0, 2, Scatter).S(Bedding, 0, 1, Scatter, style: Hide).Pad(0.6f);
			T(a, POIType.BrickKiln, "Kiln", Scattered, Timber)
				.S(PotteryKiln, 1, 1, Centre).S(LogPile, 1, 2, Scatter).S(Storage, 2, 4, Scatter).S(LeanTo, 0, 1, Scatter).Pad(0.7f);

			// Wasteland (decision 96): the stroke's cursed stone with its walls thrown outward, the lone chapel, the holy well (its
			// spring chapel only on a world with water: the silent well-head anywhere), the quarantined vill with its pyres.
			T(a, POIType.BlightedCrater, "Stroke", Scattered, Stone)
				.S(Menhir, 1, 1, Centre, 0.2f, 0.5f, 0.9f, 1.2f).S(RuinedWall, 3, 6, Ring, 0.6f, 1f).S(Rubble, 4, 8, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 0.6f).Pad(0f);
			T(a, POIType.Chapel, "Chapel", Single, Stone)
				.S(Chapel, 1, 1, Centre).S(Gravestone, 2, 6, Scatter, 0f, 0.3f).Pad(0.8f);
			T(a, POIType.HolyWell, "Well-head", Single, Stone)
				.S(Well, 1, 1, Centre).S(Shrine, 0, 1, Scatter).S(Menhir, 0, 2, Scatter, 0f, 0.2f, 0.4f, 0.6f).Pad(0.8f);
			T(a, POIType.HolyWell, "Spring Chapel", Single, Stone)
				.S(Chapel, 1, 1, Centre).S(Well, 1, 1, Scatter).Weighs(0.6f).NeedsWater().Pad(0.8f);
			T(a, POIType.PlagueVillage, "Quarantine", Streets, Timber)
				.S(House, 4, 8, Street, 0.2f, 0.6f).S(Fire, 1, 3, Scatter).S(Mound, 2, 5, Scatter).S(Storage, 1, 3, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 0.3f).Pad(0.6f);
		}
	}
}
#endif
