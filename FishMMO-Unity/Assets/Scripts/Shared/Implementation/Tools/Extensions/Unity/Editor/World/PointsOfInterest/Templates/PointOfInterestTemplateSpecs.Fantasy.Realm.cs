#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class PointOfInterestTemplateSpecs
	{
		// Pieces the Realm kinds name by id (the kit's own constants cover the rest).
		private const string RmCart = "=Cart", RmGallows = "=Gallows", RmGibbet = "=Gallows#1", RmScaffold = "=Gallows#0", RmCross = "=WaysideCross",
			RmThrone = "=Throne", RmStables = "=Stables", RmHay = "=HayBales", RmChest = "=Chest", RmStrongbox = "=Strongbox",
			RmRuinedStone = "=RuinedStoneHouse", RmRuinedTimber = "=RuinedTimberHouse", RmRuinedBarn = "=RuinedBarn", RmCairn = "=GraveMound#1",
			RmDelving = "=MinePortal#2", RmAdit = "=MinePortal#0", RmAbandonedAdit = "=MinePortal#1", RmMineCart = "=MineCart", RmCaveIn = "=CaveInTimbers",
			RmRing = "=PortalRing", RmArch = "=PortalArch", RmHolestone = "=CircleStone#1", RmGarden = "=MarketCross#1", RmBarn = "=Barn";

		/// <summary>The fantasy wave (2026-10-27, poi-fantasy-wave/WAVE.md): the templates of the Realm pack's kinds (33).</summary>
		private static void FantasyRealm(List<PointOfInterestTemplateSpec> a)
		{
			// ── Dragon's Lair: a hoard door in charred rock, a roost of bones, a hold the wyrm burned out ──
			T(a, POIType.DragonLair, "Hoard", Scattered, Stone)
				.S(CryptDoor, 1, 1, Centre).S(Bones, 6, 12, Scatter, 0f, 0.3f, 1.5f, 3f).S(Rubble, 3, 6, Scatter)
				.S(Menhir, 4, 7, Ring, 0.1f, 0.4f, 1.2f, 1.8f).S(RmChest, 0, 2, Scatter).S(RmStrongbox, 0, 2, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 1f).Pad(0.5f).In(1.4f, Volcanic).In(1.4f, Highland).In(1.2f, Arid);
			T(a, POIType.DragonLair, "Bone Roost", Round, Stone)
				.S(Menhir, 5, 8, Ring, 0.1f, 0.5f, 1.5f, 2.2f).S(Bones, 8, 14, Scatter, 0f, 0.3f, 2f, 3.5f).S(Rubble, 2, 5, Scatter)
				.S(RuinedStatue, 0, 2, Scatter).Weathered(PointOfInterestFinish.Charred, 0.8f).Pad(0.5f).Elsewhere(0.6f).In(1.4f, Cold).In(1.2f, Steppe).In(1.2f, Highland);
			T(a, POIType.DragonLair, "Burned Hold", Scattered, Stone)
				.S(RuinedKeep, 1, 1, Centre).S(RuinedWall, 2, 4, Scatter).S(Bones, 6, 10, Scatter, 0f, 0.3f, 1.5f, 3f).S(Rubble, 3, 6, Scatter)
				.S(Banner, 1, 3, Scatter, 0.5f, 0.9f, style: Timber).Weathered(PointOfInterestFinish.Charred, 1f).Pad(0.5f).Elsewhere(0.5f);

			// ── Troll Bridge: the span the planner sited over the river, with the troll's fire and bones beside it ──
			T(a, POIType.TrollBridge, "Toll Span", Linear, Timber)
				.S(Bridge, 1, 1, Span).S(Fire, 1, 1, Scatter).S(Bones, 2, 4, Scatter).S(Bedding, 1, 2, Scatter, style: Hide).S(Storage, 0, 2, Scatter)
				.Pad(0f);
			T(a, POIType.TrollBridge, "Mossed Span", Linear, Timber)
				.S(Bridge, 1, 1, Span, 0.1f, 0.3f).S(Bones, 3, 6, Scatter).S(Bedding, 1, 1, Scatter, style: Hide).S(Fire, 0, 1, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 1f).Pad(0f).Weighs(0.7f).In(1.4f, Woods).In(1.4f, Swamp);

			// ── Giant's Steading: a great hall, a bone garth, a stone hold, everything half again too large ──
			T(a, POIType.GiantsSteading, "Great Hall", Walled, Timber)
				.S(Fence, 1, 1, Perimeter, 0f, 0.2f, 1.6f, 1.6f).S(Gate, 1, 1, Gates).S(Longhouse, 1, 1, Centre, 0f, 0.1f, 1.8f, 1.8f)
				.S(Fire, 1, 1, Scatter, 0f, 0f, 1.6f, 1.8f).S(Storage, 3, 6, Scatter, 0f, 0.2f, 1.6f, 2.2f).S(Bones, 3, 6, Scatter, 0f, 0.2f, 1.5f, 2.5f)
				.Races(Outlaw).Pad(0.7f).In(1.3f, Highland).In(1.2f, Cold);
			T(a, POIType.GiantsSteading, "Bone Garth", Scattered, Hide)
				.S(Longhouse, 1, 1, Centre, 0.1f, 0.3f, 2f, 2.4f).S(Bones, 6, 10, Scatter, 0f, 0.2f, 1.8f, 3f).S(Fire, 1, 2, Scatter, 0f, 0f, 1.6f, 2f)
				.S(Bedding, 2, 4, Scatter, 0f, 0f, 1.8f, 2.4f).S(Storage, 2, 5, Scatter, 0f, 0.2f, 1.8f, 2.4f).S(Fence, 1, 3, Scatter, 0.2f, 0.5f, 1.6f, 1.8f)
				.Pad(0.7f).Elsewhere(0.6f).In(1.3f, Cold).In(1.2f, Steppe);
			T(a, POIType.GiantsSteading, "Stone Hold", Scattered, Stone)
				.S(StoneHouse, 1, 1, Centre, 0f, 0.2f, 2.2f, 2.4f).S(Menhir, 3, 5, Ring, 0f, 0.2f, 1.2f, 1.8f).S(Fire, 1, 1, Scatter, 0f, 0f, 1.6f, 1.8f)
				.S(Storage, 2, 4, Scatter, 0f, 0.2f, 1.8f, 2.4f).S(Bones, 2, 5, Scatter, 0f, 0.2f, 1.5f, 2.5f)
				.Weathered(PointOfInterestFinish.Mossy, 0.4f).Pad(0.7f).Elsewhere(0.7f).In(1.3f, Highland);

			// ── Goblin Warren: burrow mouths in the turf, or a broken stockade round them ──
			T(a, POIType.GoblinWarren, "Burrows", Scattered, Earth)
				.S(Mound, 4, 8, Scatter).S(Palisade, 1, 3, Scatter, 0.5f, 0.9f).S(Bones, 3, 6, Scatter).S(Fire, 1, 2, Scatter)
				.S(Storage, 2, 5, Scatter, style: Timber).S(Banner, 1, 2, Scatter, 0.4f, 0.8f, style: Hide).Pad(0.6f);
			T(a, POIType.GoblinWarren, "Stockade", Walled, Timber)
				.S(Palisade, 1, 1, Perimeter, 0.4f, 0.8f).S(Gate, 1, 1, Gates).S(Mound, 3, 6, Scatter, style: Earth).S(Fire, 1, 1, Centre)
				.S(Bones, 2, 5, Scatter).S(Storage, 2, 4, Scatter).S(Banner, 1, 2, Scatter, 0.4f, 0.8f, style: Hide)
				.Races("Humanoid").Pad(0.6f).Weighs(0.8f);

			// ── Haunted Manor: a grey hall in its garden wall, a burned grange, a family crypt ──
			T(a, POIType.HauntedManor, "Grey Manor", Walled, Stone)
				.S(RuinedWall, 1, 1, Perimeter).S(Gate, 1, 1, Gates, 0.3f, 0.6f).S(RmRuinedStone, 1, 1, Centre).S(Gravestone, 2, 5, Scatter, 0.2f, 0.6f)
				.S(Rubble, 2, 4, Scatter).Weathered(PointOfInterestFinish.Mossy, 1f).Pad(0.6f);
			T(a, POIType.HauntedManor, "Burned Grange", Scattered, Timber)
				.S(RmRuinedTimber, 1, 2, Centre).S(RmRuinedBarn, 0, 1, Scatter).S(Fence, 2, 4, Scatter, 0.4f, 0.8f).S(Gravestone, 2, 4, Scatter, 0.3f, 0.7f)
				.Weathered(PointOfInterestFinish.Charred, 0.7f).Pad(0.6f).Elsewhere(0.7f).In(1.3f, Swamp).In(1.2f, Woods);
			T(a, POIType.HauntedManor, "Family Crypt", Scattered, Stone)
				.S(RmRuinedStone, 1, 1, Centre).S(Mausoleum, 1, 1, Scatter, 0.2f, 0.4f).S(Gravestone, 4, 8, Scatter, 0.2f, 0.6f).S(Brazier, 0, 2, Scatter)
				.S(RuinedWall, 1, 3, Scatter).Weathered(PointOfInterestFinish.Mossy, 1f).Pad(0.6f).Elsewhere(0.7f);

			// ── Hanging Tree: the gallows or gibbet beside a great tree, with what hangs and lies under it ──
			T(a, POIType.HangingTree, "Gallows Oak", Single, Timber)
				.S(RmScaffold, 1, 1, Centre).S(Bones, 1, 3, Scatter).S(Gravestone, 0, 2, Scatter, 0.2f, 0.6f).Pad(0.7f);
			T(a, POIType.HangingTree, "Gibbet Tree", Single, Timber)
				.S(RmGibbet, 1, 1, Centre).S(Bones, 2, 4, Scatter).S(Gravestone, 0, 2, Scatter, 0.3f, 0.7f).Weathered(PointOfInterestFinish.Mossy, 0.5f).Pad(0.7f).Weighs(0.8f);

			// ── Buried Hoard: a small cairn over a cache, or a barrow cache with a chest in its flank ──
			T(a, POIType.BuriedHoard, "Cairn", Single, Stone)
				.S(RmCairn, 1, 1, Centre).S(Menhir, 1, 1, Scatter, 0f, 0.3f, 0.4f, 0.6f).S(Rubble, 1, 3, Scatter).S(RmChest, 0, 1, Scatter).Pad(0.6f);
			T(a, POIType.BuriedHoard, "Hidden Cache", Single, Earth)
				.S(Mound, 1, 1, Centre).S(Rubble, 1, 2, Scatter).S(RmStrongbox, 1, 2, Scatter).S(RmChest, 0, 1, Scatter).S(Bones, 0, 2, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.5f).Pad(0.6f).Weighs(0.8f);

			// ── Ambush Site: an overturned cart on the road, or a hide on the slope above ──
			T(a, POIType.AmbushSite, "Overturned Cart", Scattered, Timber)
				.S(RmCart, 1, 1, Centre, 0.2f, 0.5f).S(Bones, 2, 5, Scatter).S(Banner, 1, 2, Scatter, 0.5f, 0.9f).S(Storage, 2, 4, Scatter, 0.2f, 0.6f).Pad(0.6f);
			T(a, POIType.AmbushSite, "Hide", Scattered, Timber)
				.S(LeanTo, 1, 2, Centre, style: Hide).S(Fire, 0, 1, Scatter).S(Bedding, 1, 3, Scatter).S(Bones, 2, 4, Scatter).S(Storage, 1, 3, Scatter)
				.Races(Outlaw).Pad(0.6f).Weighs(0.8f);

			// ── Wrecked Caravan: wagons and what they carried, or a dune-wrecked halt ──
			T(a, POIType.WreckedCaravan, "Wagon Train", Scattered, Timber)
				.S(RmCart, 2, 4, Scatter, 0.2f, 0.7f).S(Storage, 3, 6, Scatter, 0.2f, 0.6f).S(Bones, 2, 5, Scatter).S(RmHay, 0, 2, Scatter).Pad(0.6f);
			T(a, POIType.WreckedCaravan, "Last Camp", Scattered, Hide)
				.S(RmCart, 1, 2, Scatter, 0.4f, 0.8f).S(Tent, 1, 2, Scatter, 0.4f, 0.8f).S(Fire, 1, 1, Centre, 0.3f, 0.6f).S(Bones, 3, 6, Scatter).S(Storage, 2, 4, Scatter, 0.3f, 0.7f)
				.Pad(0.6f).Elsewhere(0.6f).In(1.4f, Arid).In(1.2f, Steppe);

			// ── Crossroads: the wayside cross where roads meet, with its notice, a torch and maybe a gallows ──
			T(a, POIType.Crossroads, "Stone Cross", Single, Stone)
				.S("=WaysideCross#0", 1, 1, Centre).S(Notice, 0, 1, Scatter).S(RmGallows, 0, 1, Scatter).S(TorchPost, 1, 3, Scatter).Pad(0.8f);
			T(a, POIType.Crossroads, "Calvary", Single, Timber)
				.S("=WaysideCross#1", 1, 1, Centre).S(Notice, 0, 1, Scatter).S(TorchPost, 1, 2, Scatter).S(Gravestone, 0, 2, Scatter).Pad(0.8f).Weighs(0.8f);

			// ── Boundary Stone: a carved march stone ──
			T(a, POIType.BoundaryStone, "Marker", Single, Stone)
				.S(RuneStone, 1, 1, Centre).S(Menhir, 0, 2, Scatter, 0f, 0.2f, 0.4f, 0.6f).S(Rubble, 1, 2, Scatter).Weathered(PointOfInterestFinish.Mossy, 0.6f).Pad(0.8f);
			T(a, POIType.BoundaryStone, "Mearstone", Single, Stone)
				.S(Menhir, 1, 1, Centre, 0f, 0.2f, 0.7f, 0.9f).S(Rubble, 1, 3, Scatter).Weathered(PointOfInterestFinish.Mossy, 0.6f).Pad(0.8f).Weighs(0.8f);

			// ── Signal Beacon: the chain of hill fires, each a brazier on a plinth, a watch-fire, or a beacon tower ──
			T(a, POIType.SignalBeacon, "Balefire", Single, Stone)
				.S(Plinth, 1, 1, Centre, 0f, 0f, 1.5f, 1.8f).S(Brazier, 1, 1, Centre, 0f, 0f, 2f, 2.4f).S(LogPile, 2, 4, Scatter).S(LeanTo, 1, 1, Scatter).Pad(0.8f)
				.In(1.3f, Highland).Elsewhere(1f);
			T(a, POIType.SignalBeacon, "Watch Fire", Single, Timber)
				.S(Fire, 1, 1, Centre, 0f, 0f, 2f, 2.4f).S(LogPile, 3, 5, Scatter).S(LeanTo, 1, 2, Scatter).S(Banner, 0, 1, Scatter).Pad(0.8f).Weighs(0.8f)
				.In(1.3f, Woods).In(1.3f, Steppe);
			T(a, POIType.SignalBeacon, "Beacon Tower", Single, Stone)
				.S(Watchtower, 1, 1, Centre).S(Brazier, 1, 2, Scatter, 0f, 0f, 1.6f, 2f).S(LogPile, 2, 4, Scatter).S(Banner, 1, 2, Scatter).Pad(0.8f).Weighs(0.7f);

			// ── Toll Gate: the bar across the road, its watch-post, notice and toll-keeper's cottage ──
			T(a, POIType.TollGate, "Toll Bar", Single, Timber)
				.S(Gate, 1, 1, Centre).S(Watchtower, 1, 1, Scatter, 0f, 0.1f, 0.8f, 0.9f).S(Notice, 1, 1, Scatter).S(Cottage, 1, 1, Scatter)
				.S(Fence, 0, 2, Scatter).S(TorchPost, 1, 2, Scatter).Pad(0.8f);
			T(a, POIType.TollGate, "Toll House", Single, Stone)
				.S(Gate, 1, 1, Centre).S(StoneHouse, 1, 1, Scatter).S(Notice, 1, 1, Scatter).S(Banner, 1, 2, Scatter).S(Brazier, 1, 2, Scatter)
				.Pad(0.8f).Weighs(0.8f).In(1.3f, Highland).In(1.2f, Arid);

			// ── Hamlet: a few cottages and a well, timber, stone or longhouses ──
			T(a, POIType.Hamlet, "Thorp", Streets, Timber)
				.S(Cottage, 3, 6, Street).S(House, 0, 2, Street).S(Well, 0, 1, Centre).S(Fence, 1, 3, Scatter).S(Storage, 2, 5, Scatter).S(RmHay, 0, 2, Scatter)
				.Pad(0.7f);
			T(a, POIType.Hamlet, "Stone Stead", Streets, Stone)
				.S(StoneHouse, 3, 5, Street).S(Cottage, 0, 2, Street).S(Well, 0, 1, Centre).S(Fence, 1, 3, Scatter).S(Storage, 2, 4, Scatter).S(Banner, 0, 1, Scatter)
				.Pad(0.7f).Elsewhere(0.5f).In(1.5f, Highland).In(1.4f, Arid);
			T(a, POIType.Hamlet, "Longhouse Stead", Streets, Timber)
				.S(Longhouse, 1, 2, Street).S(Cottage, 2, 4, Street).S(Fire, 1, 1, Centre).S(Storage, 3, 6, Scatter).S(Fence, 1, 2, Scatter)
				.Pad(0.7f).Elsewhere(0.4f).In(1.5f, Cold).In(1.2f, Woods);

			// ── Manor House: a walled house with a stable, chapel and garden fountain ──
			T(a, POIType.ManorHouse, "Stone Manor", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(StoneHouse, 1, 1, Centre, 0f, 0f, 1.3f, 1.3f).S(Chapel, 0, 1, Scatter).S(Stable, 1, 1, Scatter)
				.S(RmGarden, 0, 1, Scatter).S(Well, 0, 1, Scatter).S(Banner, 1, 2, Scatter).S(Storage, 2, 4, Scatter).Pad(0.7f);
			T(a, POIType.ManorHouse, "Timber Manor", Walled, Timber)
				.S(Fence, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S("=TimberHouseLarge", 1, 1, Centre, 0f, 0f, 1.2f, 1.3f).S(RmBarn, 0, 1, Scatter).S(Stable, 1, 1, Scatter)
				.S(Well, 0, 1, Scatter).S(Storage, 2, 5, Scatter).S(RmHay, 0, 2, Scatter).Pad(0.7f).Elsewhere(0.6f).In(1.4f, Woods).In(1.4f, Swamp);
			T(a, POIType.ManorHouse, "Grange", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(StoneHouse, 1, 1, Centre, 0f, 0f, 1.3f, 1.3f).S(Granary, 1, 1, Scatter).S(RmBarn, 0, 1, Scatter)
				.S(Dovecote, 0, 1, Scatter).S(Well, 1, 1, Scatter).S(Storage, 2, 4, Scatter).Pad(0.7f).Elsewhere(0.7f);

			// ── Siege Camp: tents and a palisade before a hold, or an open leaguer of lines and banners ──
			T(a, POIType.SiegeCamp, "Palisaded Lines", Walled, Timber)
				.S(Palisade, 1, 1, Perimeter, 0.1f, 0.4f).S(Gate, 1, 1, Gates).S(Fire, 1, 1, Centre).S(Tent, 6, 10, Ring, style: Hide).S(Banner, 2, 4, Scatter)
				.S(RuinedWall, 0, 2, Scatter).S(Storage, 3, 6, Scatter).Pad(0.8f);
			T(a, POIType.SiegeCamp, "Leaguer", Round, Hide)
				.S(Tent, 6, 10, Ring).S(Fire, 1, 2, Centre).S(Banner, 2, 4, Scatter, style: Timber).S(RuinedWall, 1, 2, Scatter).S(RmCart, 1, 2, Scatter, style: Timber)
				.S(Storage, 3, 6, Scatter, style: Timber).Pad(0.8f).Weighs(0.8f);

			// ── Burned Village: charred houses, smoke, graves ──
			T(a, POIType.BurnedVillage, "Ashes", Scattered, Timber)
				.S(RmRuinedTimber, 4, 8, Scatter).S(Fire, 0, 2, Scatter).S(Mound, 1, 3, Scatter).S(Bones, 0, 2, Scatter).S(Rubble, 1, 3, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 1f).Pad(0.6f);
			T(a, POIType.BurnedVillage, "Cinders", Scattered, Stone)
				.S(RmRuinedStone, 3, 6, Scatter).S(RuinedWall, 1, 3, Scatter).S(Rubble, 3, 6, Scatter).S(Mound, 1, 2, Scatter).S(Fire, 0, 1, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 1f).Pad(0.6f).Elsewhere(0.6f).In(1.4f, Highland).In(1.3f, Arid);

			// ── Warband Camp: the war camp's stockade, a horde of yurts, a black host round braziers ──
			T(a, POIType.WarbandCamp, "Palisaded Host", Walled, Timber)
				.S(Palisade, 1, 1, Perimeter, 0.2f, 0.5f).S(Gate, 1, 1, Gates).S(Fire, 1, 1, Centre).S(Tent, 5, 8, Ring, style: Hide)
				.S(Banner, 3, 3, Scatter).S(Bones, 4, 8, Scatter).S(Storage, 2, 5, Scatter).Pad(0.8f);
			T(a, POIType.WarbandCamp, "Horde", Round, Hide)
				.S(Yurt, 5, 8, Ring).S(Fire, 1, 2, Centre).S(Banner, 3, 3, Scatter, style: Timber).S(Bones, 3, 6, Scatter).S(Storage, 2, 4, Scatter, style: Timber)
				.S(Bedding, 2, 5, Scatter).Pad(0.8f).Elsewhere(0.4f).In(1.5f, Steppe).In(1.2f, Arid);
			T(a, POIType.WarbandCamp, "Black Host", Round, Hide)
				.S(Tent, 5, 8, Ring).S(Brazier, 2, 3, Centre).S(Menhir, 2, 4, Scatter, 0.1f, 0.4f, style: Stone).S(Banner, 3, 3, Scatter, style: Timber)
				.S(Bones, 8, 14, Scatter).Weathered(PointOfInterestFinish.Charred, 0.7f).Pad(0.8f).Weighs(0.8f).Elsewhere(0.5f).In(1.4f, Volcanic).In(1.2f, Swamp);

			// ── Cultist Hideout: a crypt door and altar in the dark, or a hidden fane of rune stones ──
			T(a, POIType.CultistHideout, "Undercroft", Single, Stone)
				.S(CryptDoor, 1, 1, Centre).S(Altar, 1, 1, Scatter).S(Brazier, 2, 2, Scatter).S(Glowjar, 1, 3, Scatter).S(Rubble, 1, 3, Scatter).S(Bones, 0, 2, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.8f).Pad(0.6f);
			T(a, POIType.CultistHideout, "Hidden Fane", Round, Stone)
				.S(RuneStone, 3, 5, Ring).S(Altar, 1, 1, Centre).S(Brazier, 2, 2, Scatter).S(Glowjar, 2, 4, Scatter).S(Bones, 0, 2, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.8f).Pad(0.6f).Weighs(0.8f);

			// ── Ancient Wall: a line of broken curtain with its fallen towers ──
			T(a, POIType.AncientWall, "Rampart", Scattered, Stone)
				.S(RuinedWall, 6, 12, Line, 0.4f, 0.9f).S(CollapsedTower, 1, 2, Scatter).S(Rubble, 2, 5, Scatter).Weathered(PointOfInterestFinish.Mossy, 0.8f).Pad(0.4f);
			T(a, POIType.AncientWall, "Watch Wall", Scattered, Stone)
				.S(RuinedWall, 5, 10, Line, 0.4f, 0.9f).S(CollapsedTower, 1, 2, Scatter).S(Obelisk, 0, 1, Scatter, 0.2f, 0.5f).S(Rubble, 2, 4, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.6f).Pad(0.4f).Weighs(0.8f);

			// ── Spell Scar: broken stones round a scorched blast, glowing jars where the magic still pools ──
			T(a, POIType.SpellScar, "Burn", Scattered, Stone)
				.S(Menhir, 2, 4, Scatter, 0.5f, 0.9f, 0.7f, 1.2f).S(Rubble, 3, 5, Scatter).S(Glowjar, 2, 2, Scatter).Weathered(PointOfInterestFinish.Charred, 1f).Pad(0.3f);
			T(a, POIType.SpellScar, "Glassing", Scattered, Stone)
				.S(RuneStone, 1, 2, Scatter, 0.4f, 0.8f).S(Rubble, 4, 7, Scatter).S(Glowjar, 3, 5, Scatter).Weathered(PointOfInterestFinish.Charred, 1f).Pad(0.3f).Weighs(0.7f);

			// ── Planar Rift: a portal ring or torn arch, its stone circle broken, glow-jars where the veil is thin ──
			T(a, POIType.PlanarRift, "Torn Ring", Round, Stone)
				.S(CircleStone, 5, 8, Ring, 0.3f, 0.7f).S(RmRing, 1, 1, Centre).S(Glowjar, 3, 5, Scatter).Weathered(PointOfInterestFinish.Charred, 0.4f).Pad(0.4f);
			T(a, POIType.PlanarRift, "Torn Arch", Round, Stone)
				.S(CircleStone, 4, 7, Ring, 0.4f, 0.8f).S(RmArch, 1, 1, Centre, 0.2f, 0.5f).S(Glowjar, 2, 4, Scatter).Weathered(PointOfInterestFinish.Charred, 0.4f).Pad(0.4f).Weighs(0.8f);

			// ── Petrified Battle: a host turned to stone mid-charge ──
			T(a, POIType.PetrifiedBattle, "Stone Host", Scattered, Stone)
				.S(Statue, 8, 16, Scatter, 0f, 0.2f).S(RuinedStatue, 3, 6, Scatter).S(Banner, 2, 5, Scatter, 0.5f, 0.9f, style: Timber).S(Rubble, 3, 6, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.5f).Pad(0.5f);
			T(a, POIType.PetrifiedBattle, "Frozen Field", Scattered, Stone)
				.S(Statue, 8, 14, Scatter, 0f, 0.2f).S(RuinedStatue, 4, 8, Scatter).S(Banner, 2, 4, Scatter, 0.6f, 0.9f, style: Timber).S(Rubble, 2, 5, Scatter)
				.Pad(0.5f).Elsewhere(0.5f).In(1.4f, Cold).In(1.2f, Highland).In(1.2f, Steppe);

			// ── Titan's Bones: ribs and a skull the size of houses, half sunk ──
			T(a, POIType.TitansBones, "Ribs", Scattered, Earth)
				.S(Bones, 8, 14, Scatter, 0f, 0.3f, 3f, 5f).S(Rubble, 1, 3, Scatter).Pad(0.5f);
			T(a, POIType.TitansBones, "Sunken Skull", Scattered, Earth)
				.S(Bones, 10, 14, Scatter, 0.2f, 0.5f, 3.5f, 5f).S(Rubble, 2, 4, Scatter).Weathered(PointOfInterestFinish.Mossy, 0.6f).Pad(0.5f).Weighs(0.8f).Elsewhere(0.6f).In(1.4f, Arid).In(1.2f, Steppe);

			// ── Wishing Stone: a holed stone hung with ribbons, with a little shrine ──
			T(a, POIType.WishingStone, "Holed Stone", Single, Stone)
				.S(RmHolestone, 1, 1, Centre).S(Banner, 2, 4, Scatter, 0f, 0.3f, 0.5f, 0.7f, style: Timber).S(Shrine, 0, 1, Scatter).Weathered(PointOfInterestFinish.Mossy, 0.5f).Pad(0.8f);
			T(a, POIType.WishingStone, "Ribbon Stone", Single, Stone)
				.S("=CircleStone#0", 1, 1, Centre).S(Banner, 2, 3, Scatter, 0f, 0.3f, 0.5f, 0.7f, style: Timber).Weathered(PointOfInterestFinish.Mossy, 0.5f).Pad(0.8f).Weighs(0.8f);

			// ── Elder Monolith: a black shaft many times a man's height ──
			T(a, POIType.ElderMonolith, "Black Obelisk", Single, Stone)
				.S(Obelisk, 1, 1, Centre, 0f, 0.1f, 2.5f, 3.5f).S(Rubble, 2, 4, Scatter).Weathered(PointOfInterestFinish.Charred, 1f).Pad(0.6f);
			T(a, POIType.ElderMonolith, "Standing Slab", Single, Stone)
				.S(Menhir, 1, 1, Centre, 0f, 0.1f, 2.5f, 3f).S(Rubble, 2, 4, Scatter).S(RuneStone, 0, 2, Scatter, 0f, 0.2f).Weathered(PointOfInterestFinish.Charred, 1f).Pad(0.6f).Weighs(0.8f);

			// ── Waygate: a standing ring or arch with its old circle, glowing where it is still awake ──
			T(a, POIType.WaygateRing, "Ring Gate", Round, Stone)
				.S(RmRing, 1, 1, Centre).S(CircleStone, 6, 9, Ring, 0.1f, 0.4f).S(Glowjar, 1, 3, Scatter).Pad(0.7f);
			T(a, POIType.WaygateRing, "Arch Gate", Round, Stone)
				.S(RmArch, 1, 1, Centre).S(CircleStone, 6, 8, Ring, 0.1f, 0.4f).S(Glowjar, 1, 3, Scatter).Pad(0.7f).Weighs(0.8f);

			// ── Golem Graveyard: still guards fallen where they stood, in a field or in rows ──
			T(a, POIType.GolemGraveyard, "Field", Scattered, Stone)
				.S(RuinedStatue, 5, 9, Scatter, 0f, 0.3f, 1.4f, 2f).S(Rubble, 3, 6, Scatter).Pad(0.5f);
			T(a, POIType.GolemGraveyard, "Still Rows", Grid, Stone)
				.S(RuinedStatue, 6, 9, Rows, 0.2f, 0.5f, 1.4f, 1.8f).S(Rubble, 2, 5, Scatter).Weathered(PointOfInterestFinish.Mossy, 0.5f).Pad(0.5f).Weighs(0.8f);

			// ── Kobold Delving: a dressed-stone adit door in the face, the kobolds' bones, banner and traps ──
			T(a, POIType.KoboldDelving, "Delving Door", Single, Stone)
				.S(RmDelving, 1, 1, Centre).S(Bones, 2, 4, Scatter).S(Banner, 1, 2, Scatter, style: Hide).S(RmMineCart, 0, 1, Scatter).S(Rubble, 1, 3, Scatter)
				.S(RmCaveIn, 0, 1, Scatter).Pad(0.6f);
			T(a, POIType.KoboldDelving, "Timbered Adit", Single, Timber)
				.S(RmAdit, 1, 1, Centre).S(Bones, 2, 4, Scatter).S(Banner, 1, 2, Scatter, style: Hide).S(RmCaveIn, 0, 2, Scatter).S(RmMineCart, 0, 1, Scatter)
				.S(Storage, 1, 3, Scatter).Pad(0.6f).Weighs(0.8f);

			// ── Lost Library: a moss-grown reading hall in its fallen garden wall, a scriptorium, a sunken athenaeum ──
			T(a, POIType.LostLibrary, "Moss Archive", Scattered, Stone)
				.S(Library, 1, 1, Centre, 0.2f, 0.4f).S(RuinedWall, 2, 3, Scatter).S(BrokenColumn, 3, 6, Scatter).S(RuinedStatue, 1, 2, Scatter).S(Rubble, 2, 4, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 1f).Pad(0.6f).In(1.4f, Woods).In(1.2f, Swamp);
			T(a, POIType.LostLibrary, "Scriptorium", Scattered, Stone)
				.S(RmRuinedStone, 2, 3, Scatter).S(Library, 1, 1, Centre, 0.3f, 0.5f).S(Statue, 1, 2, Scatter, 0.2f, 0.5f).S(Glowjar, 1, 3, Scatter).S(Rubble, 2, 4, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.8f).Pad(0.6f).Weighs(0.8f);
			T(a, POIType.LostLibrary, "Dry Athenaeum", Scattered, Stone)
				.S(Observatory, 1, 1, Centre, 0.3f, 0.5f).S(Column, 3, 6, Ring, 0.2f, 0.5f).S(BrokenColumn, 2, 4, Scatter).S(Altar, 0, 1, Scatter, 0.2f, 0.5f).S(Rubble, 3, 6, Scatter)
				.Pad(0.6f).Elsewhere(0.5f).In(1.4f, Arid).In(1.2f, Highland);

			// ── Pack Hold: Commandery, Lost Throne, Royal Mausoleum ──
			T(a, POIType.Commandery, "Stone", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(Chapel, 1, 1, Centre).S(Barracks, 1, 2, Scatter).S(Stable, 1, 1, Scatter).S(Well, 0, 1, Scatter)
				.S(Banner, 3, 5, Scatter).S(TrainingYard, 0, 1, Edges).S(Brazier, 1, 2, Scatter).Pad(0.7f);
			T(a, POIType.Commandery, "Preceptory", Walled, Timber)
				.S(Palisade, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(Chapel, 1, 1, Centre).S(Longhouse, 1, 2, Scatter).S(Stable, 1, 1, Scatter).S(Banner, 3, 5, Scatter)
				.S(Storage, 2, 4, Scatter).Pad(0.7f).Elsewhere(0.6f).In(1.3f, Woods).In(1.2f, Cold).Races(Civil);

			T(a, POIType.LostThrone, "Stone Seat", Single, Stone)
				.S(RmThrone, 1, 1, Centre, 0f, 0f, 1.2f, 1.4f).S(Plinth, 0, 1, Scatter).S(BrokenColumn, 4, 7, Ring, 0.3f, 0.7f).S(Rubble, 2, 4, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.8f).Pad(0.7f);
			T(a, POIType.LostThrone, "Empty Dais", Single, Stone)
				.S(Plinth, 1, 1, Centre, 0f, 0f, 1.6f, 1.8f).S(RmThrone, 1, 1, Scatter, 0f, 0f, 1.2f, 1.4f).S(Column, 2, 4, Ring, 0.1f, 0.3f).S(BrokenColumn, 2, 4, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.6f).Pad(0.7f).Weighs(0.8f);

			T(a, POIType.RoyalMausoleum, "Walled Tomb", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 1, Gates, 0.2f, 0.5f).S(Mausoleum, 1, 1, Centre, 0f, 0.2f, 1.4f, 1.4f).S(Gravestone, 6, 8, Scatter, 0.1f, 0.4f)
				.S(Obelisk, 2, 2, Scatter).Weathered(PointOfInterestFinish.Mossy, 0.7f).Pad(0.6f);
			T(a, POIType.RoyalMausoleum, "Barrowdown", Scattered, Earth)
				.S(Mausoleum, 1, 1, Centre, 0.1f, 0.3f, 1.4f, 1.4f).S(Mound, 2, 3, Scatter, 0f, 0f, 2f, 3f).S(Menhir, 3, 5, Ring, 0f, 0.2f).S(Gravestone, 4, 6, Scatter, 0.2f, 0.5f)
				.S(Obelisk, 1, 2, Scatter).Weathered(PointOfInterestFinish.Mossy, 0.8f).Pad(0.6f).Weighs(0.8f).Elsewhere(0.6f).In(1.4f, Steppe).In(1.2f, Cold);
		}
	}
}
#endif
