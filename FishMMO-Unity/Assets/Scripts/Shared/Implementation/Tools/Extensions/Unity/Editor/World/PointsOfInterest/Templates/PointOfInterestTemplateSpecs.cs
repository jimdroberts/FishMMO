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

		// ── Styles ───────────────────────────────────────────────────
		public const string Timber = "timber", Stone = "stone", Hide = "hide", Iron = "iron", Earth = "earth";

		// ── Biomes (asset names in Assets/Templates/Entity/Biomes) ───
		public static readonly string[] Swamp = { "Swamp", "Mangrove", "Peat Bog", "Wetlands", "Wetland", "Swamp Ruins", "Swamp Temple", "Swamp Cave" };
		public static readonly string[] Arid = { "Desert", "High Desert", "Badlands", "Salt Flat", "Oasis", "Scrubland", "Arid", "Dust Sea", "Wasteland", "Barren" };
		public static readonly string[] Highland = { "Mountain", "Mountain Slope", "Alpine", "Hills", "Rocky Terrain", "Scree", "Karst", "Rocky Coast", "Alpine Meadow" };
		public static readonly string[] Cold = { "Tundra", "Taiga", "Snow", "Periglacial", "Glacier", "Ice Sheet", "Ice Shelf", "Permanent Ice" };
		public static readonly string[] Woods = { "Forest", "Woodland", "Taiga", "Jungle", "Rainforest", "Bamboo Forest", "Forest Ruins", "Jungle Ruins", "Jungle Temple", "Temperate" };
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
			Piers = PointOfInterestSlotRole.Pier;

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
			return a;
		}

		/// <summary>The templates of one kind.</summary>
		public static List<PointOfInterestTemplateSpec> For(POIType kind) => All().FindAll(s => s.Kind == kind);

		// ── Settlements ──────────────────────────────────────────────

		private static void Settlements(List<PointOfInterestTemplateSpec> a)
		{
			// Villages: a street of houses round a well; longhouses in the cold, yurts on the steppe.
			T(a, POIType.Village, "Timber", Streets, Timber)
				.S(Well, 1, 1, Centre).S(Stall, 0, 3, Centre).S(House, 8, 18, Street)
				.S(Storage, 4, 10, Scatter).S(Banner, 0, 2, Scatter)
				.In(0.1f, Arid).In(0.5f, Highland).In(0.5f, Cold).In(0.4f, Swamp);
			T(a, POIType.Village, "Stone", Streets, Stone)
				.S(Well, 1, 1, Centre).S(Stall, 0, 3, Centre).S(House, 8, 16, Street)
				.S(Storage, 4, 10, Scatter).S(Banner, 0, 2, Scatter)
				.Elsewhere(0.3f).In(2f, Arid).In(2f, Highland).Races(Civil);
			T(a, POIType.Village, "Longhouse", Streets, Timber)
				.S(Fire, 1, 1, Centre).S(Longhouse, 3, 6, Street).S(Cottage, 2, 6, Street)
				.S(Storage, 3, 8, Scatter).S(Banner, 1, 2, Scatter)
				.Elsewhere(0.05f).In(2.5f, Cold).In(0.4f, Woods).Races(Civil);
			T(a, POIType.Village, "Yurts", Round, Hide)
				.S(Yurt, 6, 12, Ring).S(Fire, 1, 2, Centre).S(Stall, 0, 2, Scatter).S(Storage, 3, 8, Scatter).S(Bedding, 2, 6, Scatter)
				.Elsewhere(0.02f).In(1.2f, Steppe).In(0.6f, Arid).In(1f, Cold).Pad(0.8f);

			// Towns: two or more streets, a market square.
			T(a, POIType.Town, "Timber", Streets, Timber)
				.S(Well, 1, 2, Centre).S(Stall, 3, 8, Centre).S(House, 20, 40, Street)
				.S(Storage, 8, 16, Scatter).S(Watchtower, 0, 2, Scatter).S(Banner, 2, 4, Scatter)
				.In(0.1f, Arid).In(0.5f, Highland).In(0.5f, Cold);
			T(a, POIType.Town, "Stone", Streets, Stone)
				.S(Well, 1, 2, Centre).S(Stall, 3, 8, Centre).S(House, 20, 36, Street)
				.S(Storage, 8, 16, Scatter).S(Banner, 2, 4, Scatter)
				.Elsewhere(0.3f).In(2f, Arid).In(2f, Highland).Races(Civil);
			T(a, POIType.Town, "Palisaded", Walled, Timber)
				.S(Palisade, 1, 1, Perimeter).S(Gate, 2, 2, Gates).S(Watchtower, 2, 4, Towers)
				.S(Well, 1, 1, Centre).S(Stall, 2, 6, Centre).S(House, 18, 34, Street).S(Storage, 6, 12, Scatter).S(Banner, 2, 4, Scatter)
				.Elsewhere(0.6f).In(1.2f, Woods).In(0.2f, Arid).Races(Civil);

			// Cities: walled in stone, gatehouses on the main streets, a keep in the middle.
			T(a, POIType.City, "Stone", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 2, 4, Gates).S(WallTower, 6, 12, Towers)
				.S(Keep, 1, 1, Centre).S(Well, 2, 3, Centre).S(Stall, 6, 12, Centre)
				.S(House, 25, 45, Street).S(House, 25, 45, Street, style: Timber)
				.S(Storage, 10, 20, Scatter).S(Banner, 4, 8, Scatter)
				.Elsewhere(1f).In(1.5f, Arid).In(1.5f, Highland);
			T(a, POIType.City, "Timber", Walled, Timber)
				.S(Palisade, 1, 1, Perimeter).S(Gate, 2, 4, Gates).S(Watchtower, 6, 10, Towers)
				.S(Keep, 1, 1, Centre, style: Stone).S(Well, 2, 3, Centre).S(Stall, 6, 12, Centre)
				.S(House, 50, 90, Street).S(Storage, 10, 20, Scatter).S(Banner, 4, 8, Scatter)
				.Elsewhere(0.4f).In(1.5f, Woods).In(1f, Cold).In(0.05f, Arid).Races(Civil);

			// The capital: larger than a city (the planner's footprint), more of everything, the keep at its heart (Jim's default).
			T(a, POIType.Capital, "Stone", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 4, 4, Gates).S(WallTower, 12, 20, Towers)
				.S(Keep, 1, 1, Centre).S(Statue, 2, 4, Centre).S(Obelisk, 1, 1, Centre).S(Well, 3, 5, Centre).S(Stall, 12, 20, Centre)
				.S(House, 50, 80, Street).S(House, 40, 70, Street, style: Timber)
				.S(Storage, 20, 30, Scatter).S(Banner, 8, 12, Scatter)
				.Sized(false, false, true).In(1.5f, Arid).In(1.5f, Highland);
			T(a, POIType.Capital, "Timber", Walled, Timber)
				.S(Wall, 1, 1, Perimeter, style: Stone).S(Gate, 4, 4, Gates, style: Stone).S(WallTower, 12, 20, Towers, style: Stone)
				.S(Keep, 1, 1, Centre, style: Stone).S(Statue, 2, 4, Centre).S(Well, 3, 5, Centre).S(Stall, 12, 20, Centre)
				.S(House, 80, 140, Street).S(Storage, 20, 30, Scatter).S(Banner, 8, 12, Scatter)
				.Sized(false, false, true).Elsewhere(0.4f).In(1.5f, Woods).In(1f, Cold).In(0.05f, Arid).Races(Civil);

			// Ports: a waterfront street, piers out from the shore ahead (the planner turns a port to the sea).
			T(a, POIType.Port, "Timber", Streets, Timber)
				.S(Pier, 1, 3, Piers).S(Well, 1, 1, Centre).S(Stall, 2, 5, Centre).S(House, 10, 24, Street)
				.S(Storage, 10, 20, Scatter).S(Lighthouse, 0, 1, Scatter).S(Banner, 1, 3, Scatter)
				.In(0.2f, Arid).In(0.6f, Highland);
			T(a, POIType.Port, "Stone", Streets, Stone)
				.S(Pier, 1, 3, Piers, style: Timber).S(Well, 1, 1, Centre).S(Stall, 2, 5, Centre).S(House, 10, 22, Street)
				.S(Storage, 10, 20, Scatter).S(Lighthouse, 0, 1, Scatter).S(Banner, 1, 3, Scatter)
				.Elsewhere(0.3f).In(2f, Arid).In(2f, Highland).Races(Civil);

			T(a, POIType.TradingPost, "Market", Single, Timber)
				.S(House, 1, 1, Centre).S(Stall, 3, 6, Ring).S(Tent, 1, 3, Scatter, style: Hide).S(Storage, 6, 12, Scatter)
				.S(Well, 0, 1, Scatter).S(Banner, 1, 2, Scatter);
			T(a, POIType.TradingPost, "Caravanserai", Round, Hide)
				.S(Tent, 4, 8, Ring).S(Well, 1, 1, Centre).S(Stall, 2, 4, Scatter, style: Timber).S(Storage, 6, 12, Scatter)
				.Elsewhere(0.2f).In(2f, Arid).In(1.5f, Steppe);

			T(a, POIType.Waystation, "Inn", Single, Timber)
				.S(House, 1, 1, Centre).S(LeanTo, 1, 2, Scatter).S(Well, 0, 1, Scatter).S(Storage, 2, 5, Scatter).S(Fire, 0, 1, Scatter);
			T(a, POIType.Waystation, "Stone Inn", Single, Stone)
				.S(StoneHouse, 1, 1, Centre).S(LeanTo, 1, 2, Scatter).S(Well, 1, 1, Scatter).S(Storage, 2, 5, Scatter)
				.Elsewhere(0.3f).In(2f, Arid).In(2f, Highland);
		}

		// ── Strongholds ─────────────────────────────────────────────

		private static void Strongholds(List<PointOfInterestTemplateSpec> a)
		{
			T(a, POIType.Keep, "Stone", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(WallTower, 0, 4, Towers)
				.S(Keep, 1, 1, Centre).S(Well, 0, 1, Centre).S(Storage, 3, 6, Scatter).S(Banner, 2, 4, Scatter);
			T(a, POIType.Keep, "Palisade", Walled, Timber)
				.S(Palisade, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(Watchtower, 1, 1, Centre).S(Longhouse, 1, 2, Rows)
				.S(Storage, 3, 6, Scatter).S(Banner, 2, 4, Scatter).S(Fire, 0, 1, Scatter)
				.Elsewhere(0.4f).In(1.5f, Woods).In(1.5f, Cold).In(0.1f, Arid).Races(Civil);

			T(a, POIType.Castle, "Stone", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 2, Gates).S(WallTower, 4, 8, Towers)
				.S(Keep, 1, 1, Centre).S(Well, 1, 1, Centre).S(House, 4, 10, Street)
				.S(Storage, 4, 8, Scatter).S(Banner, 3, 6, Scatter);
			T(a, POIType.Castle, "Highland", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(WallTower, 6, 10, Towers)
				.S(Keep, 1, 1, Centre).S(Well, 1, 1, Centre).S(Longhouse, 2, 4, Street, style: Timber)
				.S(Storage, 4, 8, Scatter).S(Banner, 3, 6, Scatter)
				.Elsewhere(0.3f).In(2f, Highland).In(1.5f, Cold).Races(Civil);

			T(a, POIType.Fortress, "Stone", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 2, 2, Gates).S(WallTower, 6, 10, Towers)
				.S(Keep, 1, 1, Centre).S(Watchtower, 2, 4, Scatter, style: Timber).S(Longhouse, 3, 6, Street, style: Timber)
				.S(Tent, 4, 10, Rows, style: Hide).S(Storage, 6, 12, Scatter).S(Banner, 6, 10, Scatter);
			T(a, POIType.Fortress, "Palisade", Walled, Timber)
				.S(Palisade, 1, 1, Perimeter).S(Gate, 2, 2, Gates).S(Watchtower, 4, 8, Towers)
				.S(Longhouse, 2, 4, Street).S(Tent, 6, 14, Rows, style: Hide).S(Fire, 1, 2, Centre)
				.S(Storage, 6, 12, Scatter).S(Banner, 6, 10, Scatter)
				.Elsewhere(0.5f).In(1.5f, Woods).In(1.5f, Steppe).Races(Outlaw);

			T(a, POIType.Tower, "Timber", Single, Timber)
				.S(Watchtower, 1, 1, Centre).S(Storage, 1, 3, Scatter).S(Banner, 0, 1, Scatter);
			T(a, POIType.Tower, "Stone", Single, Stone)
				.S(WallTower, 1, 1, Centre).S(Storage, 1, 3, Scatter).S(Banner, 0, 1, Scatter)
				.Elsewhere(0.4f).In(2f, Arid).In(2f, Highland);
		}

		// ── Works: bridges, mines, quarries, lighthouses ────────────

		private static void Works(List<PointOfInterestTemplateSpec> a)
		{
			// Spans laid across the river the planner sited it on, as long as the water is wide (PropsFeature.MeasureCrossing).
			T(a, POIType.Bridge, "Timber", Linear, Timber).S(Bridge, 1, 1, Span).Pad(0f);

			// The adit faces out of the rock face the planner found (its heading).
			T(a, POIType.Mine, "Adit", Single, Timber)
				.S(CryptDoor, 1, 1, Centre, style: Stone).S(LeanTo, 1, 2, Scatter).S(Storage, 3, 6, Scatter)
				.S(Rubble, 2, 4, Scatter, style: Stone).S(Fire, 0, 1, Scatter).Pad(0.7f);

			T(a, POIType.Quarry, "Cut Stone", Scattered, Stone)
				.S(Rubble, 4, 8, Scatter).S(Plinth, 2, 4, Scatter).S("=FallenColumn", 1, 3, Scatter)
				.S(LeanTo, 1, 2, Scatter, style: Timber).S(Storage, 3, 6, Scatter, style: Timber).Pad(0.4f);

			T(a, POIType.Lighthouse, "Stone", Single, Stone)
				.S(Lighthouse, 1, 1, Centre).S(Storage, 1, 3, Scatter, style: Timber).S(Cottage, 0, 1, Scatter, style: Timber);
		}

		// ── Wild camps and lairs ────────────────────────────────────

		private static void Wild(List<PointOfInterestTemplateSpec> a)
		{
			T(a, POIType.Camp, "Lean-tos", Round, Timber)
				.S(LeanTo, 3, 6, Ring).S(Fire, 1, 1, Centre).S(Bedding, 2, 5, Scatter, style: Hide).S(Storage, 2, 5, Scatter)
				.In(1.5f, Woods).Pad(0.8f);
			T(a, POIType.Camp, "Hide Tents", Round, Hide)
				.S(Tent, 4, 8, Ring).S(Fire, 1, 1, Centre).S(Bedding, 2, 5, Scatter).S(Storage, 2, 5, Scatter, style: Timber)
				.S(Banner, 0, 1, Scatter, style: Timber).Races(Outlaw).Pad(0.8f);
			T(a, POIType.Camp, "Yurts", Round, Hide)
				.S(Yurt, 3, 6, Ring).S(Fire, 1, 2, Centre).S(Bedding, 2, 5, Scatter).S(Storage, 2, 5, Scatter, style: Timber)
				.Elsewhere(0.05f).In(2f, Cold).In(1.5f, Steppe).Pad(0.8f);
			T(a, POIType.Camp, "Crude", Round, Timber)
				.S(LeanTo, 2, 4, Ring, 0.2f, 0.5f).S(Fire, 1, 1, Centre).S(Bones, 3, 8, Scatter).S(Rubble, 0, 2, Scatter, style: Stone)
				.Races(Monster).Pad(0.6f);

			T(a, POIType.BanditCamp, "Open", Round, Hide)
				.S(Tent, 3, 6, Ring).S(Fire, 1, 1, Centre).S(Storage, 4, 8, Scatter, style: Timber).S(Banner, 1, 2, Scatter, style: Timber)
				.S(Bedding, 2, 5, Scatter).Pad(0.8f);
			T(a, POIType.BanditCamp, "Palisade", Walled, Timber)
				.S(Palisade, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(Fire, 1, 1, Centre).S(Tent, 3, 6, Ring, style: Hide)
				.S(Storage, 4, 8, Scatter).S(Banner, 1, 3, Scatter).S(Watchtower, 0, 1, Scatter)
				.Races(Outlaw).In(1.5f, Woods).Pad(0.8f);

			T(a, POIType.HuntingLodge, "Lodge", Single, Timber)
				.S(House, 1, 1, Centre).S(LeanTo, 1, 2, Scatter).S(Fire, 0, 1, Scatter).S(Storage, 2, 4, Scatter).S(Bones, 1, 3, Scatter)
				.In(0.3f, Arid);
			T(a, POIType.HuntingLodge, "Hide Lodge", Single, Hide)
				.S(Yurt, 1, 1, Centre).S(Tent, 0, 2, Scatter).S(Fire, 1, 1, Scatter).S(Bones, 2, 4, Scatter).S(Storage, 1, 3, Scatter, style: Timber)
				.Elsewhere(0.2f).In(2f, Cold).In(1.5f, Steppe);

			T(a, POIType.LumberCamp, "Timber", Round, Timber)
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
				.S(Shrine, 1, 1, Centre).S(Menhir, 0, 2, Scatter, scaleMin: 0.6f, scaleMax: 0.9f).Pad(0.8f);
			T(a, POIType.Shrine, "Statue", Single, Stone)
				.S(Statue, 1, 1, Centre).S(Altar, 0, 1, Scatter).Pad(0.8f).Weighs(0.5f);

			T(a, POIType.Temple, "Colonnade", Round, Stone)
				.S(Column, 8, 14, Ring).S(Altar, 1, 1, Centre).S(Statue, 2, 4, Centre).S(Shrine, 0, 1, Scatter);
			T(a, POIType.Temple, "Court", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Shrine, 1, 1, Centre).S(Statue, 2, 4, Centre).S(Column, 4, 8, Scatter).S(Well, 0, 1, Scatter);

			T(a, POIType.Monastery, "Cloister", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(Shrine, 1, 1, Centre).S(Statue, 1, 2, Centre).S(Well, 1, 1, Centre)
				.S(StoneHouse, 4, 8, Street).S(Storage, 2, 5, Scatter, style: Timber);
			T(a, POIType.Monastery, "Timber", Walled, Timber)
				.S(Palisade, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(Shrine, 1, 1, Centre, style: Stone).S(Well, 1, 1, Centre, style: Stone)
				.S(Longhouse, 1, 2, Street).S(House, 2, 5, Street).S(Storage, 2, 5, Scatter)
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
			// No hull in the kit yet: a wreck is its cargo and its mast.
			T(a, POIType.Wreck, "Debris", Scattered, Timber)
				.S(Storage, 3, 8, Scatter, 0.3f, 0.8f).S(Banner, 1, 2, Scatter, 0.6f, 0.9f).S(Rubble, 0, 2, Scatter, style: Stone);

			T(a, POIType.SmugglersCove, "Camp", Single, Timber)
				.S(Fire, 1, 1, Centre).S(Tent, 2, 4, Ring, style: Hide).S(Pier, 0, 1, Piers).S(Storage, 8, 14, Scatter).S(LeanTo, 1, 2, Scatter);

			T(a, POIType.PirateCove, "Hideout", Single, Timber)
				.S(House, 1, 1, Centre).S(Tent, 3, 6, Ring, style: Hide).S(Pier, 1, 2, Piers).S(Storage, 8, 16, Scatter)
				.S(Banner, 2, 4, Scatter).S(Fire, 1, 1, Scatter);

			T(a, POIType.SunkenShip, "Debris", Scattered, Timber)
				.S(Storage, 3, 8, Scatter, 0.4f, 0.9f).S(Banner, 1, 2, Scatter, 0.6f, 0.9f).S(Bones, 0, 3, Scatter)
				.Weathered(PointOfInterestFinish.Algae).Pad(0f);

			T(a, POIType.SunkenRuins, "Stone", Scattered, Stone)
				.S(RuinedHouse, 2, 5, Scatter).S(RuinedWall, 3, 8, Scatter).S(BrokenColumn, 2, 6, Scatter).S(RuinedStatue, 0, 2, Scatter)
				.S(Rubble, 3, 6, Scatter).Weathered(PointOfInterestFinish.Algae).Pad(0f);

			// A walled city gone under: walls, houses and the keep laid past the ruin threshold come up as their ruins.
			T(a, POIType.SunkenCity, "Drowned", Walled, Stone)
				.S(Wall, 1, 1, Perimeter, 0.4f, 0.9f).S(Gate, 2, 2, Gates, 0.5f, 0.8f).S(Keep, 1, 1, Centre, 0.6f, 0.8f)
				.S(Statue, 1, 3, Centre, 0.3f, 0.9f).S(House, 15, 35, Street, 0.5f, 1f).S(Column, 4, 10, Scatter, 0.3f, 0.9f)
				.S(Rubble, 6, 12, Scatter).Weathered(PointOfInterestFinish.Algae).Pad(0f);
			T(a, POIType.SunkenCity, "Reef Halls", Streets, Stone)
				.S(Statue, 1, 2, Centre, 0.2f, 0.5f).S(StoneHouse, 15, 30, Street, 0.3f, 0.7f).S(Column, 6, 12, Scatter, 0.2f, 0.6f)
				.S(Rubble, 4, 8, Scatter).Weathered(PointOfInterestFinish.Algae).Races(Aquatic).Pad(0f);
		}

		// ── Caves: built in the carved chamber ─────────────────────

		/// <summary>
		/// The carved cave kinds (their shaper digs the tunnel and chamber; Overhang and NaturalArch need no template):
		/// small and medium caves hold a camp or a den in the chamber, a large cave's chamber its dungeon entrance
		/// (PointOfInterestFeatureDefaults) among the bones of whatever guards it.
		/// </summary>
		private static void Caves(List<PointOfInterestTemplateSpec> a)
		{
			foreach (POIType kind in new[] { POIType.Cave, POIType.IceCave, POIType.LavaTube })
			{
				T(a, kind, "Camp", Single, Timber)
					.S(Fire, 1, 1, Centre, style: Stone).S(Bedding, 1, 3, Scatter, style: Hide).S(Storage, 1, 3, Scatter).S(Bones, 0, 2, Scatter)
					.Sized(true, true, false).Chamber().Pad(0f);
				T(a, kind, "Den", Scattered, Earth)
					.S(Bones, 3, 6, Scatter).S(Rubble, 0, 2, Scatter, style: Stone).S(Mound, 0, 1, Scatter)
					.Sized(true, true, false).Races(Monster).Chamber().Pad(0f);
				T(a, kind, "Deep", Scattered, Earth)
					.S(Bones, 2, 5, Scatter).S(Rubble, 1, 3, Scatter, style: Stone).S(Banner, 0, 2, Scatter, 0.3f, 0.7f, style: Timber)
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
			// The door faces out of the rock face the planner found; the dungeon behind it is hand-made (Jim, 2026-10-10).
			T(a, POIType.DungeonEntrance, "Door", Single, Stone)
				.S(CryptDoor, 1, 1, Centre).S(Statue, 0, 2, Scatter, 0.3f, 0.7f).S(Rubble, 1, 3, Scatter).S(Bones, 0, 3, Scatter).Pad(0.5f);

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
	}
}
#endif
