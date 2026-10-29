#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class PointOfInterestTemplateSpecs
	{
		/// <summary>The Dry pack's own pieces (StructurePieces.Fantasy.Dry.cs), by id.</summary>
		private const string DryRoundHut = "=RoundHut", DryBaobabTree = "=Baobab", DryPyramidTomb = "=Pyramid", DrySphinxStatue = "=Sphinx",
			DryRockCutFront = "=RockCutFacade", DryAlcoveDwelling = "=AlcoveHouse", DryHallFront = "=CarvedHallFront", DryCliffKings = "=CliffRelief";
		private const string DryScarecrow = "=Scarecrow", DryStooks = "=Stooks", DryGallows = "=Gallows", DryBothy = "=Bothy", DrySheepfold = "=Sheepfold",
			DryMinePortalPiece = "=MinePortal", DryHeadframe = "=Headframe", DryOreBin = "=OreBin", DryBench = "=Bench", DryHerbShelf = "=HerbShelf", DryTempleBuilding = "temple";

		private static readonly string[] DryFarm = { "Farmland", "Valley", "Plains", "Hills" };
		private static readonly string[] DryGrass = { "Plains", "Grassland", "Farmland", "Valley", "Hills", "Alpine Meadow" };
		private static readonly string[] DryDesert = { "Desert", "High Desert", "Oasis" };
		private static readonly string[] DryHigh = { "Alpine", "Mountain Slope", "Scree", "Rocky Terrain", "Hills", "Alpine Meadow" };

		/// <summary>The fantasy wave (2026-10-27, poi-fantasy-wave/WAVE.md): the templates of the Dry pack's kinds (30; the three carved kinds are the Shapes agent's).</summary>
		private static void FantasyDry(List<PointOfInterestTemplateSpec> a)
		{
			// ── Pack G: open lands ───────────────────────────────────

			// Grange: the abbey's or the lord's home farm. Tithe Farm (timber, anywhere farmed), Abbey Grange (stone, with its chapel; hills and valleys),
			// Walled Barton (a stone-walled yard for the highland edge).
			T(a, POIType.Grange, "Tithe Farm", Single, Timber)
				.S(Barn, 1, 2, Centre).S(Granary, 1, 1, Scatter).S(Cottage, 1, 2, Scatter).S(DryStooks, 3, 6, Scatter).S(HayRick, 1, 3, Scatter).S(Well, 0, 1, Scatter).S(Dovecote, 0, 1, Scatter, style: Stone)
				.Pad(0.8f);
			T(a, POIType.Grange, "Abbey Grange", Single, Stone)
				.S(Chapel, 1, 1, Centre).S(Barn, 1, 1, Scatter, style: Timber).S(Granary, 1, 1, Scatter).S(StoneHouse, 1, 2, Scatter).S(Wall, 3, 6, Scatter).S(DryStooks, 2, 5, Scatter).S(Well, 0, 1, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.3f).In(1.4f, "Hills", "Valley", "Alpine Meadow").Pad(0.8f);
			T(a, POIType.Grange, "Walled Barton", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(Barn, 1, 1, Centre, style: Timber).S(Granary, 1, 1, Scatter).S(Cottage, 1, 2, Scatter).S(HayRick, 1, 2, Scatter)
				.In(1.3f, DryHigh).Weighs(0.6f).Sized(false, false, true).Pad(0.8f);

			T(a, POIType.Dovecote, "Culver House", Single, Stone)
				.S(Dovecote, 1, 1, Centre).S(Fence, 3, 6, Scatter).S(HayRick, 0, 1, Scatter).Pad(0.8f);
			T(a, POIType.Dovecote, "Fallen Columbarium", Single, Stone)
				.S(Dovecote, 1, 1, Centre, 0.3f, 0.6f).S(Rubble, 2, 4, Scatter).Weathered(PointOfInterestFinish.Mossy, 0.6f).Weighs(0.5f).Pad(0.6f);

			T(a, POIType.BeeGarden, "Bee Boles", Scattered, Stone)
				.S(Apiary, 3, 6, Scatter).S(Wall, 2, 4, Edges).S(DryHerbShelf, 1, 3, Scatter).S(DryBothy, 0, 1, Scatter, style: Timber).Pad(0.8f);
			T(a, POIType.BeeGarden, "Skeps", Scattered, Timber)
				.S(Apiary, 3, 6, Scatter).S(Fence, 3, 6, Scatter).S(DryHerbShelf, 1, 2, Scatter).S(DryBench, 0, 1, Scatter).Pad(0.8f).Weighs(0.8f);

			T(a, POIType.VillageGreen, "Maypole Green", Round, Timber)
				.S(Maypole, 1, 1, Centre).S(Well, 1, 1, Scatter, style: Stone).S(Pillory, 0, 1, Scatter).S(Notice, 1, 1, Scatter).S(DryBench, 2, 4, Ring).Pad(0.9f);
			T(a, POIType.VillageGreen, "Moot Green", Round, Stone)
				.S(StoneCross, 1, 1, Centre).S(DryBench, 3, 6, Ring).S(Well, 0, 1, Scatter).S(TorchPost, 2, 4, Ring).S(Notice, 0, 1, Scatter).Pad(0.9f).Weighs(0.7f);

			T(a, POIType.ScarecrowField, "Scarecrows", Scattered, Timber)
				.S(DryScarecrow, 4, 8, Scatter).S(HayRick, 1, 2, Scatter).S(DryStooks, 2, 5, Scatter).Pad(0.8f);
			T(a, POIType.ScarecrowField, "Tatterfield", Scattered, Timber)
				.S(DryScarecrow, 4, 8, Scatter, 0.3f, 0.8f).S(Bones, 1, 3, Scatter).S(DryStooks, 1, 3, Scatter, 0.2f, 0.6f).S(DryGallows, 0, 1, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 0.3f).Weighs(0.6f).Pad(0.7f);

			T(a, POIType.LongBarrow, "Long Barrow", Linear, Earth)
				.S(DungeonBarrow, 1, 1, Centre).S(Mound, 2, 3, Line).S(Menhir, 2, 4, Scatter, 0.1f, 0.4f, 0.9f, 1.3f, style: Stone)
				.Weathered(PointOfInterestFinish.Mossy, 0.7f).Pad(0.6f);
			T(a, POIType.LongBarrow, "Giant's Grave", Linear, Stone)
				.S(Mound, 3, 4, Line, 0f, 0.2f, 1.4f, 1.9f).S(Menhir, 4, 6, Scatter, 0.1f, 0.4f, 0.9f, 1.5f).S(Bones, 2, 4, Scatter, 0f, 0f, 1.5f, 2.5f)
				.Weathered(PointOfInterestFinish.Mossy, 0.7f).Weighs(0.6f).Pad(0.6f);

			T(a, POIType.Henge, "Great Ring", Round, Stone)
				.S(CircleStone, 12, 20, Ring).S(Altar, 1, 1, Centre).S(Menhir, 1, 2, Scatter, 0.1f, 0.3f, 0.6f, 0.9f)
				.Weathered(PointOfInterestFinish.Mossy, 0.6f).Pad(0.8f);
			T(a, POIType.Henge, "Sun Ring", Round, Stone)
				.S(Menhir, 12, 16, Ring, 0f, 0.3f, 1.0f, 1.3f).S(Altar, 1, 1, Centre).S(Brazier, 2, 4, Ring)
				.In(1.3f, "Alpine Meadow", "Hills").Weighs(0.6f).Pad(0.8f);

			T(a, POIType.GallowsHill, "Gibbet Hill", Scattered, Timber)
				.S(DryGallows, 1, 2, Scatter).S(Bones, 1, 3, Scatter).S(Gravestone, 0, 3, Scatter, 0.2f, 0.6f)
				.Weathered(PointOfInterestFinish.Charred, 0.3f).Pad(0.5f);

			T(a, POIType.KurganField, "Kurgans", Linear, Earth)
				.S(Mound, 4, 8, Line, 0f, 0.2f, 2f, 3f).S(Statue, 2, 2, Scatter, 0.3f, 0.6f, 0.8f, 1f, style: Stone)
				.Weathered(PointOfInterestFinish.Mossy, 0.4f).Pad(0.6f);
			T(a, POIType.KurganField, "Khan Mounds", Linear, Earth)
				.S(Mound, 3, 5, Line, 0f, 0.2f, 2.5f, 3.4f).S(Banner, 2, 4, Scatter, style: Hide).S(Bones, 2, 5, Scatter).S(Menhir, 1, 3, Scatter, 0.1f, 0.3f)
				.Weighs(0.6f).Pad(0.6f);

			// NomadCamp: yurts round a fire, a horse pen, the banner. Felt Yurts (the steppe), Black Tents (the dry lands), Raiders' Camp.
			T(a, POIType.NomadCamp, "Felt Yurts", Round, Hide)
				.S(Fire, 1, 1, Centre, style: Stone).S(Yurt, 4, 9, Ring).S(Fence, 3, 6, Scatter, style: Timber).S(Banner, 1, 2, Scatter).S(Storage, 2, 5, Scatter).S(Bedding, 0, 2, Scatter)
				.In(1.4f, "Steppe", "Savanna").In(0.7f, "Tundra").Pad(0.6f);
			T(a, POIType.NomadCamp, "Black Tents", Round, Hide)
				.S(Fire, 1, 1, Centre, style: Stone).S(Tent, 4, 8, Ring).S(Fence, 2, 4, Scatter, style: Timber).S(Banner, 1, 2, Scatter).S(Storage, 2, 5, Scatter).S(Bedding, 1, 3, Scatter)
				.In(1.4f, "Desert", "High Desert", "Scrubland", "Badlands", "Salt Flat").Weighs(0.7f).Pad(0.6f);
			T(a, POIType.NomadCamp, "War Camp", Round, Hide)
				.S(Fire, 1, 1, Centre, style: Stone).S(Yurt, 3, 6, Ring).S(Banner, 3, 5, Ring).S(Bones, 2, 4, Scatter).S(Storage, 2, 4, Scatter).S(TorchPost, 2, 3, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 0.2f).Weighs(0.4f).Pad(0.6f);

			T(a, POIType.StoneFigures, "Stone Row", Linear, Stone)
				.S(Statue, 4, 8, Line, 0.1f, 0.5f, 0.8f, 1f).Weathered(PointOfInterestFinish.Mossy, 0.5f).Pad(0.6f);

			T(a, POIType.Kraal, "Thorn Ring", Round, Timber)
				.S(Fire, 1, 1, Centre, style: Stone).S(DryRoundHut, 5, 10, Ring).S(Fence, 4, 8, Scatter).S(Storage, 2, 4, Scatter).S(Bedding, 0, 2, Scatter, style: Hide)
				.Pad(0.6f);
			T(a, POIType.Kraal, "Cattle Boma", Round, Timber)
				.S(DryRoundHut, 4, 8, Ring).S(Fence, 6, 10, Scatter).S(DrySheepfold, 1, 2, Centre, style: Timber).S(Fire, 1, 1, Scatter).S(Storage, 2, 4, Scatter)
				.Weighs(0.7f).Pad(0.6f);

			T(a, POIType.Baobab, "Old Baobab", Single, Earth)
				.S(DryBaobabTree, 1, 1, Centre).S(Shrine, 0, 1, Scatter, style: Stone).S(Bones, 0, 2, Scatter)
				.Pad(0.6f);

			T(a, POIType.Shieling, "Summer Huts", Scattered, Timber)
				.S(DryBothy, 1, 3, Scatter).S(DrySheepfold, 1, 1, Centre).S(Fence, 2, 4, Scatter).S(Fire, 0, 1, Scatter).Pad(0.7f);

			// ── Pack A: arid ─────────────────────────────────────────

			// Necropolis: Streets of Tombs (the city of the dead: avenues of mausolea), Plundered Necropolis (broken, bones, the crypt doors forced),
			// Tomb Field (a scatter of tombs round a few obelisks, the lesser sort of the desert fringe).
			T(a, POIType.Necropolis, "City of the Dead", Grid, Stone)
				.S(Mausoleum, 4, 8, Rows).S(CryptDoor, 1, 2, Scatter).S(Obelisk, 2, 2, Centre).S(RuinedWall, 2, 4, Edges).S(Brazier, 2, 4, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.2f).In(1.3f, "Desert", "High Desert", "Badlands").Pad(0.6f);
			T(a, POIType.Necropolis, "Plundered", Scattered, Stone)
				.S(Mausoleum, 4, 7, Scatter, 0.3f, 0.7f).S(CryptDoor, 1, 2, Scatter, 0.2f, 0.5f).S(Obelisk, 1, 2, Scatter, 0.3f, 0.6f).S(RuinedWall, 3, 5, Scatter).S(Bones, 3, 6, Scatter).S(Rubble, 3, 6, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 0.2f).Weighs(0.6f).Pad(0.6f);
			T(a, POIType.Necropolis, "Tomb Field", Scattered, Stone)
				.S(Mausoleum, 3, 5, Scatter).S(Gravestone, 6, 14, Scatter, 0.1f, 0.5f).S(Obelisk, 1, 2, Centre).S(Mound, 2, 5, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.4f).Weighs(0.7f).Elsewhere(0.5f).Pad(0.6f);

			T(a, POIType.Pyramid, "Smooth-sided", Single, Stone)
				.S(DryPyramidTomb + "#0", 1, 1, Centre).S(Obelisk, 0, 2, Scatter).S(Rubble, 1, 3, Scatter).S(Bones, 0, 2, Scatter)
				.In(1.3f, DryDesert).Pad(0.7f);
			T(a, POIType.Pyramid, "Stepped", Single, Stone)
				.S(DryPyramidTomb + "#1", 1, 1, Centre).S(Brazier, 0, 2, Scatter).S(Rubble, 1, 3, Scatter).S(Statue, 0, 2, Scatter, 0.2f, 0.5f)
				.In(1.2f, "High Desert", "Badlands").Weighs(0.8f).Pad(0.7f);
			T(a, POIType.Pyramid, "Fallen King's Tomb", Single, Stone)
				.S(DryPyramidTomb, 1, 1, Centre, 0.2f, 0.4f).S(RuinedStatue, 1, 2, Scatter).S(BrokenColumn, 2, 5, Scatter).S(Rubble, 3, 6, Scatter).S(Bones, 2, 4, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 0.2f).Weighs(0.4f).Pad(0.7f);

			T(a, POIType.Sphinx, "Half-buried", Single, Stone)
				.S(DrySphinxStatue, 1, 1, Centre).S(Plinth, 0, 2, Scatter, 0.2f, 0.5f).S(Rubble, 2, 4, Scatter).S(Obelisk, 0, 1, Scatter, 0.2f, 0.5f)
				.Pad(0.6f);

			T(a, POIType.RockCutFacade, "Treasury", Single, Stone)
				.S(DryRockCutFront + "#0", 1, 1, Centre).S(Rubble, 1, 3, Scatter).S(Brazier, 0, 2, Scatter).S(RuinedStatue, 0, 1, Scatter)
				.In(1.3f, "Badlands").Pad(0.4f);
			T(a, POIType.RockCutFacade, "Tomb Row", Single, Stone)
				.S(DryRockCutFront + "#1", 1, 1, Centre).S(Rubble, 1, 3, Scatter).S(Bones, 1, 3, Scatter).Weathered(PointOfInterestFinish.Charred, 0.1f)
				.Weighs(0.8f).Pad(0.4f);

			T(a, POIType.SandBuriedTemple, "Sunk Fane", Single, Stone)
				.S(DryTempleBuilding, 1, 1, Centre, 0.5f, 0.8f).S(BrokenColumn, 2, 5, Scatter).S(Rubble, 3, 6, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.1f).Pad(0.3f);

			T(a, POIType.DjinnPrison, "Brass Seal", Round, Stone)
				.S(Shrine, 1, 1, Centre).S(Brazier, 4, 4, Ring).S(Glowjar, 1, 2, Scatter).S(RuneStone, 1, 2, Scatter)
				.Pad(0.7f);

			T(a, POIType.CaravanHalt, "Well Camp", Round, Hide)
				.S(Well, 1, 1, Centre, style: Stone).S(Tent, 3, 5, Ring).S(Storage, 3, 3, Scatter).S(Fire, 1, 2, Scatter).S(Bedding, 0, 2, Scatter)
				.In(1.3f, "Desert", "High Desert", "Oasis", "Scrubland").Pad(0.6f);
			T(a, POIType.CaravanHalt, "Walled Caravanserai", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(Well, 1, 1, Centre).S(LeanTo, 2, 4, Scatter).S(Storage, 3, 5, Scatter).S(Stable, 0, 1, Scatter)
				.Weighs(0.5f).Sized(false, false, true).Pad(0.7f);

			T(a, POIType.BeastBoneyard, "Dry Bones", Scattered, Earth)
				.S(Bones, 10, 20, Scatter, 0f, 0f, 1.5f, 3f).Pad(0.4f);

			T(a, POIType.CliffPalace, "Alcove", Single, Stone)
				.S(DryAlcoveDwelling + "#0", 1, 1, Centre).S(Rubble, 1, 3, Scatter).S(Bones, 0, 2, Scatter).Pad(0.4f);
			T(a, POIType.CliffPalace, "Eagle Town", Single, Stone)
				.S(DryAlcoveDwelling + "#1", 1, 1, Centre).S(Rubble, 1, 3, Scatter).S(Banner, 0, 2, Scatter).Weighs(0.8f).Pad(0.4f);

			T(a, POIType.DreadRoad, "Black Way", Linear, Stone)
				.S(RuinedWall, 3, 6, Line).S(DryGallows, 2, 2, Scatter).S(Banner, 2, 4, Scatter, 0.3f, 0.6f).S(RuinedStatue, 1, 2, Scatter).S(Rubble, 2, 5, Scatter).S(Bones, 2, 4, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 0.7f).Pad(0.4f);

			// ── Pack H: highland ─────────────────────────────────────

			T(a, POIType.MountainKingsHall, "King's Gate", Single, Stone)
				.S(DryHallFront + "#0", 1, 1, Centre).S(StoneHouse, 3, 6, Scatter).S(Smithy, 1, 1, Scatter).S(RuneStone, 2, 4, Scatter).S(Brazier, 2, 3, Scatter).S(Banner, 2, 4, Scatter)
				.In(1.3f, "Mountain Slope", "Alpine").Pad(0.7f);
			T(a, POIType.MountainKingsHall, "Kings Before the Gate", Single, Stone)
				.S(DryHallFront + "#1", 1, 1, Centre).S(StoneHouse, 3, 5, Scatter).S(Smithy, 1, 1, Scatter).S(RuneStone, 2, 3, Scatter).S(Statue, 0, 2, Scatter, style: Stone).S(Brazier, 2, 3, Scatter)
				.In(1.2f, "Rocky Terrain", "Mountain Slope").Weighs(0.8f).Pad(0.7f);
			T(a, POIType.MountainKingsHall, "Delving Hall", Single, Stone)
				.S(DryHallFront, 1, 1, Centre).S(StoneHouse, 2, 4, Scatter).S(DryMinePortalPiece, 0, 1, Scatter).S(DryOreBin, 1, 2, Scatter, style: Timber).S(Smithy, 1, 1, Scatter).S("=MineCart", 0, 1, Scatter)
				.In(1.2f, "Hills", "Scree").Weighs(0.6f).Pad(0.7f);

			T(a, POIType.PassFort, "Pass Gate", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(WallTower, 2, 2, Towers).S(Barracks, 0, 1, Scatter).S(Banner, 2, 3, Scatter).S(Brazier, 1, 2, Scatter)
				.Pad(0.8f);

			T(a, POIType.Eyrie, "Crag Nest", Scattered, Earth)
				.S(Bones, 3, 6, Scatter).S(Mound, 1, 2, Scatter, 0f, 0f, 0.7f, 1f).S(Rubble, 1, 3, Scatter, style: Stone).Pad(0.2f);
			T(a, POIType.GriffinRoost, "Ledge Roost", Scattered, Earth)
				.S(Bones, 3, 6, Scatter, 0f, 0f, 1.2f, 2f).S(Mound, 2, 3, Scatter, 0f, 0f, 0.8f, 1.2f).S(Rubble, 1, 2, Scatter, style: Stone).Pad(0.2f);
			T(a, POIType.WyvernNest, "Scorched Crag", Scattered, Earth)
				.S(Bones, 3, 6, Scatter, 0f, 0f, 1.5f, 2.5f).S(Rubble, 2, 4, Scatter, style: Stone).S(Mound, 1, 2, Scatter, 0f, 0f, 1f, 1.4f)
				.Weathered(PointOfInterestFinish.Charred, 1f).Pad(0.2f);

			T(a, POIType.CliffRelief, "Two Kings", Single, Stone)
				.S(DryCliffKings + "#0", 1, 1, Centre).S(Rubble, 1, 3, Scatter).Weathered(PointOfInterestFinish.Mossy, 0.5f).Pad(0.4f);
			T(a, POIType.CliffRelief, "Three Watchers", Single, Stone)
				.S(DryCliffKings + "#1", 1, 1, Centre).S(Rubble, 1, 3, Scatter).Weathered(PointOfInterestFinish.Mossy, 0.5f).Weighs(0.6f).Pad(0.4f);

			T(a, POIType.SummitCairn, "Cairn", Single, Stone)
				.S(Menhir, 5, 9, Scatter, 0f, 0.3f, 0.3f, 0.5f).S(Banner, 0, 1, Centre, style: Hide).Pad(0.3f);

			T(a, POIType.PrayerFlagPass, "Flag Pass", Scattered, Hide)
				.S(Banner, 6, 12, Scatter).S(Shrine, 1, 1, Centre, style: Stone).S(Menhir, 3, 6, Scatter, 0f, 0.3f, 0.3f, 0.5f)
				.Pad(0.4f);

			// SilverMineTown: houses on a street below the adit, a smithy, a chapel. Stone Delving (cold, high), Timber Camp (the lower hills),
			// Boomtown (a tavern, stalls, a headframe and ore bins, rougher).
			T(a, POIType.SilverMineTown, "Stone Delving", Streets, Stone)
				.S(House, 4, 8, Street).S(Smithy, 1, 1, Street).S(Chapel, 1, 1, Street).S(Storage, 2, 4, Scatter, style: Timber).S(DryMinePortalPiece, 1, 1, Edges).S(DryOreBin, 1, 3, Scatter, style: Timber).S(TorchPost, 2, 4, Scatter)
				.In(1.3f, "Alpine", "Mountain Slope", "Rocky Terrain").Sized(false, true, true).Pad(0.6f);
			T(a, POIType.SilverMineTown, "Timber Camp", Streets, Timber)
				.S(House, 5, 9, Street).S(Smithy, 1, 1, Street).S(Chapel, 0, 1, Street).S(Storage, 3, 5, Scatter).S(DryMinePortalPiece, 1, 1, Edges).S("=MineCart", 0, 2, Scatter).S(DryOreBin, 1, 3, Scatter)
				.In(1.3f, "Hills", "Alpine Meadow").Weighs(0.8f).Sized(false, true, true).Pad(0.6f);
			T(a, POIType.SilverMineTown, "Boomtown", Streets, Timber)
				.S(House, 4, 8, Street).S(Tavern, 1, 1, Street).S(Smithy, 1, 1, Street).S(Stall, 1, 3, Centre).S(DryHeadframe, 1, 1, Edges).S(DryOreBin, 2, 4, Scatter).S(Storage, 3, 6, Scatter).S(TorchPost, 2, 4, Scatter)
				.Weighs(0.6f).Sized(false, true, true).Pad(0.6f);
		}
	}
}
#endif
