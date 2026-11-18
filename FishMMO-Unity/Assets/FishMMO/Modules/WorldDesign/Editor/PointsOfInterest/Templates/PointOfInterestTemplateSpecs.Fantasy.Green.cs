#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class PointOfInterestTemplateSpecs
	{
		/// <summary>The fantasy wave (2026-10-27, poi-fantasy-wave/WAVE.md): the templates of the Green pack's kinds (29).</summary>
		private static void FantasyGreen(List<PointOfInterestTemplateSpec> a)
		{
			// The new pieces' ids (StructurePieces.Fantasy.Green.cs), laid by id.
			const string Talan = "=Talan", RopeWalk = "=RopeWalk", TreeStair = "=TreeStair", WebCurtain = "=WebCurtain", WebSheet = "=WebSheet",
				Cocoon = "=Cocoon", HollowOak = "=HollowOak", StepPyramid = "=StepPyramid#0", RuneStepPyramid = "=StepPyramid#1",
				RuinedStepPyramid = "=RuinedStepPyramid", JaguarMaw = "=JaguarMaw#0", JaguarMawFangs = "=JaguarMaw#1", PileRoundHouse = "=PileRoundHouse",
				ReedHall = "=ReedHall", BasaltHall = "=BasaltHall#0", BasaltHallTwin = "=BasaltHall#1", RuinedMinster = "=RuinedMinster";
			const PointOfInterestFinish Mossy = PointOfInterestFinish.Mossy, Charred = PointOfInterestFinish.Charred, Algae = PointOfInterestFinish.Algae;

			// ── Pack W: woods ────────────────────────────────────────────
			// Druid Grove: a ring of standing stones and an altar among the trees; one with runes and faerie lights.
			T(a, POIType.DruidGrove, "Nemeton", Round, Stone)
				.S(CircleStone, 5, 8, Ring, 0f, 0.2f).S(Altar, 1, 1, Centre).S(Menhir, 1, 2, Scatter, 0f, 0.2f, 1.1f, 1.4f)
				.Weathered(Mossy, 0.8f).Pad(0.6f);
			T(a, POIType.DruidGrove, "Rune Grove", Round, Stone)
				.S(CircleStone, 5, 7, Ring).S(RuneStone, 1, 2, Scatter).S(Glowjar, 2, 4, Scatter).S(Altar, 1, 1, Centre)
				.Weathered(Mossy, 0.5f).Pad(0.6f).Weighs(0.7f);

			// Tree Village (important): talans round the great trunks, joined by rope walks. The scene's own big trees are not
			// something a template can name (Requests), so every talan stands on its own trunk. Forest/Woodland: the first;
			// Jungle*: the second (ground huts under the talans); Taiga: the third (a lodge under them).
			T(a, POIType.TreeVillage, "Talan Town", Scattered, Timber)
				.S(Talan, 3, 6, Scatter).S(TreeStair, 1, 3, Scatter).S(RopeWalk, 2, 4, Scatter).S(Storage, 1, 3, Scatter).S(Banner, 1, 2, Scatter)
				.S(Glowjar, 3, 5, Scatter).Weathered(Mossy, 0.4f).Pad(0.3f).In(1.4f, "Forest", "Woodland").Elsewhere(0.6f);
			T(a, POIType.TreeVillage, "Jungle Boughs", Scattered, Timber)
				.S(Talan, 2, 5, Scatter).S(Stilt, 2, 4, Scatter).S(RopeWalk, 2, 4, Scatter).S(TreeStair, 1, 2, Scatter).S(Fire, 1, 2, Scatter).S(Glowjar, 2, 4, Scatter)
				.Weathered(Mossy, 0.7f).Pad(0.3f).In(1.5f, "Jungle", "Jungle Ruins", "Jungle Temple").Elsewhere(0.15f);
			T(a, POIType.TreeVillage, "Taiga Lodge", Scattered, Timber)
				.S(Talan, 2, 3, Scatter).S(Longhouse, 1, 1, Centre).S(RopeWalk, 1, 2, Scatter).S(Fire, 1, 2, Scatter).S(Storage, 2, 4, Scatter)
				.Pad(0.4f).In(1.5f, "Taiga").Elsewhere(0.1f);

			// Troll Stones: stone trolls caught by dawn, hunched and huge.
			T(a, POIType.TrollStones, "Dawnstones", Scattered, Stone)
				.S(Statue, 2, 4, Scatter, 0.1f, 0.3f, 1.8f, 2.1f).S(RuinedStatue, 1, 2, Scatter, 0f, 0f, 1.6f, 2f).S(Rubble, 1, 3, Scatter)
				.Weathered(Mossy, 1f).Pad(0.4f);
			T(a, POIType.TrollStones, "Troll Ring", Round, Stone)
				.S(Statue, 3, 5, Ring, 0.1f, 0.3f, 1.8f, 2.1f).S(Fire, 1, 1, Centre, 0.6f, 0.8f).S(Bones, 1, 3, Scatter, 0f, 0f, 1.5f, 2f)
				.Weathered(Mossy, 1f).Pad(0.4f).Weighs(0.7f);

			// Spider Thicket: webs strung between dead trunks, cocoons and bones.
			T(a, POIType.SpiderThicket, "Webwood", Scattered, Hide)
				.S(WebCurtain, 2, 4, Scatter).S(WebSheet, 1, 3, Scatter).S(Cocoon, 3, 6, Scatter).S(Bones, 2, 4, Scatter)
				.Pad(0.3f).Elsewhere(1f);
			T(a, POIType.SpiderThicket, "Brood Nest", Single, Hide)
				.S(WebSheet, 1, 1, Centre).S(Cocoon, 5, 9, Scatter).S(WebCurtain, 1, 2, Scatter).S(Bones, 1, 3, Scatter)
				.Weathered(Charred, 0.1f).Pad(0.3f).Weighs(0.8f).In(1.5f, "Forest Ruins");

			// Charcoal Clamps: colliers' mounds smouldering under turf, their log stacks and a lean-to.
			T(a, POIType.CharcoalClamps, "Pitsteads", Scattered, Earth)
				.S(CharcoalClamp, 3, 6, Scatter).S(LogPile, 2, 4, Scatter).S(LeanTo, 1, 2, Centre).S(Storage, 0, 2, Scatter)
				.Weathered(Charred, 0.3f).Pad(0.6f);

			// Greenwood Hideout: outlaws' lean-tos round a fire; one under a hollow oak.
			T(a, POIType.GreenwoodHideout, "Trysting Oak", Round, Timber)
				.S(Fire, 1, 1, Centre).S(LeanTo, 2, 4, Ring).S(Bedding, 2, 4, Scatter, style: Hide).S(Storage, 1, 2, Scatter).S(Banner, 0, 1, Scatter)
				.Pad(0.6f);
			T(a, POIType.GreenwoodHideout, "Hollow Oak Camp", Scattered, Timber)
				.S(HollowOak, 1, 1, Centre).S(LeanTo, 2, 3, Scatter).S(Fire, 1, 1, Scatter).S(Storage, 1, 3, Scatter).S(Bedding, 1, 3, Scatter, style: Hide)
				.Pad(0.4f).Weighs(0.8f);

			// Dryad's Oak: a great hollow oak with its faerie-lit hollow and a small shrine.
			T(a, POIType.DryadOak, "Heartwood", Single, Timber)
				.S(HollowOak, 1, 1, Centre).S(Shrine, 1, 1, Scatter, 0f, 0.2f, 0.7f, 0.9f, style: Stone).S(Glowjar, 2, 3, Scatter).S(Altar, 0, 1, Scatter, 0f, 0.1f, 0.6f, 0.8f)
				.Weathered(Mossy, 0.8f).Pad(0.4f);
			T(a, POIType.DryadOak, "Split Oak", Single, Timber)
				.S("=HollowOak#1", 1, 1, Centre).S(Glowjar, 1, 3, Scatter).S(Menhir, 1, 3, Scatter, 0f, 0.2f, 0.4f, 0.6f, style: Stone)
				.Weathered(Mossy, 1f).Pad(0.4f).Weighs(0.7f);

			// Goblin Market: stalls, tents and lanterns in the hollow; one ringed round a fire.
			T(a, POIType.GoblinMarket, "Night Market", Scattered, Hide)
				.S(Stall, 4, 7, Scatter).S(Tent, 1, 3, Scatter).S(Glowjar, 4, 6, Scatter).S(TorchPost, 2, 3, Scatter)
				.Pad(0.5f);
			T(a, POIType.GoblinMarket, "Moon Market", Round, Hide)
				.S(Fire, 1, 1, Centre).S(Stall, 5, 7, Ring).S(Glowjar, 4, 6, Scatter).S(Tent, 1, 2, Scatter).S(Banner, 1, 2, Scatter)
				.Pad(0.5f).Weighs(0.8f);

			// Elf Waystone: a rune stone and a faerie light beside a way.
			T(a, POIType.ElfWaystone, "Waymark", Single, Stone)
				.S(RuneStone, 1, 1, Centre).S(Glowjar, 0, 1, Scatter).S(Menhir, 0, 2, Scatter, 0f, 0.2f, 0.4f, 0.6f)
				.Weathered(Mossy, 0.5f).Pad(0.8f);
			T(a, POIType.ElfWaystone, "Lantern Stones", Round, Stone)
				.S(RuneStone, 2, 3, Ring).S(Glowjar, 2, 3, Scatter)
				.Weathered(Mossy, 0.4f).Pad(0.8f).Weighs(0.6f);

			// Wolf Den: an earthen mound, bones and tumbled stone.
			T(a, POIType.WolfDen, "Den", Scattered, Earth)
				.S(Mound, 1, 2, Centre, 0f, 0.2f, 1.1f, 1.5f).S(Bones, 3, 6, Scatter).S(Rubble, 1, 3, Scatter)
				.Pad(0.5f);
			T(a, POIType.WolfDen, "Bone Lie", Single, Earth)
				.S(Rubble, 1, 1, Centre).S(Bones, 5, 8, Scatter).S(Mound, 0, 1, Scatter)
				.Pad(0.5f).Weighs(0.7f);

			// Moss Colossus: a giant stone head or body half-sunk in moss.
			T(a, POIType.MossColossus, "Old King", Single, Stone)
				.S(RuinedStatue, 1, 1, Centre, 0f, 0f, 3f, 4f).S(Rubble, 2, 4, Scatter).S(Menhir, 0, 2, Scatter, 0.1f, 0.4f, 0.5f, 0.8f)
				.Weathered(Mossy, 1f).Pad(0.4f);
			T(a, POIType.MossColossus, "Sleeper", Single, Stone)
				.S(RuinedStatue, 1, 1, Centre, 0f, 0f, 3.2f, 3.8f).S(BrokenColumn, 2, 4, Scatter).S(Rubble, 1, 3, Scatter)
				.Weathered(Mossy, 1f).Pad(0.4f).Weighs(0.7f);

			// ── Pack J: jungle ───────────────────────────────────────────
			// Step Pyramid (important): whole in a temple biome, fallen in a ruin biome, rune-banded as a third.
			T(a, POIType.StepPyramid, "Sun Steps", Single, Stone)
				.S(StepPyramid, 1, 1, Centre).S(Brazier, 2, 3, Scatter).S(Statue, 0, 2, Scatter, 0f, 0.2f, 1.1f, 1.3f).S(Rubble, 0, 2, Scatter)
				.Weathered(Mossy, 0.9f).Pad(0.7f).In(1.5f, "Jungle Temple", "Jungle").Elsewhere(0.8f);
			T(a, POIType.StepPyramid, "Fallen Fane", Single, Stone)
				.S(RuinedStepPyramid, 1, 1, Centre).S(BrokenColumn, 3, 5, Scatter).S(Rubble, 3, 6, Scatter)
				.Weathered(Mossy, 1f).Pad(0.7f).In(1.6f, "Jungle Ruins", "Forest Ruins", "Swamp Ruins").Elsewhere(0.5f);
			T(a, POIType.StepPyramid, "Rune Steps", Single, Stone)
				.S(RuneStepPyramid, 1, 1, Centre).S(RuneStone, 2, 4, Scatter).S(Brazier, 2, 3, Scatter)
				.Weathered(Mossy, 0.6f).Pad(0.7f).Weighs(0.8f).In(1.4f, "Jungle Temple", "Volcanic Temple").Elsewhere(0.5f);

			// Jaguar Temple: a doorway in the shape of a maw on a mound.
			T(a, POIType.JaguarTemple, "Maw", Single, Stone)
				.S(JaguarMaw, 1, 1, Centre).S(Statue, 2, 2, Scatter, 0f, 0.3f, 1.1f, 1.3f).S(Brazier, 1, 2, Scatter)
				.Weathered(Mossy, 0.9f).Pad(0.6f);
			T(a, POIType.JaguarTemple, "Spotted House", Single, Stone)
				.S(JaguarMawFangs, 1, 1, Centre).S(Statue, 2, 4, Scatter, 0.1f, 0.4f, 1f, 1.2f).S(Banner, 1, 2, Scatter).S(Rubble, 0, 2, Scatter)
				.Weathered(Mossy, 1f).Pad(0.6f).Weighs(0.8f);

			// Serpent Idol: a great carved head on a plinth with an altar and braziers.
			T(a, POIType.SerpentIdol, "Coil", Single, Stone)
				.S(Statue, 1, 1, Centre, 0f, 0.2f, 2f, 2.2f).S(Plinth, 1, 1, Scatter, 0f, 0.1f, 1.4f, 1.6f).S(Altar, 1, 1, Scatter).S(Brazier, 2, 2, Ring)
				.Weathered(Mossy, 1f).Pad(0.6f);

			// Lost City (important): a jungle-grown city, and a ruined forum round a fallen square.
			T(a, POIType.LostCity, "Fallen Seat", Scattered, Stone)
				.S(RuinedHouse, 8, 14, Scatter).S(Temple, 1, 1, Centre, 0.4f, 0.7f).S(BrokenColumn, 6, 10, Scatter).S(RuinedWall, 2, 4, Scatter).S(Rubble, 4, 8, Scatter)
				.Weathered(Mossy, 1f).Pad(0.3f).In(1.5f, "Jungle", "Jungle Ruins").Elsewhere(0.8f);
			T(a, POIType.LostCity, "Ruined Forum", Round, Stone)
				.S(BrokenColumn, 8, 12, Ring).S(Altar, 1, 1, Centre, 0.2f, 0.5f).S(RuinedHouse, 4, 8, Scatter).S(Obelisk, 1, 2, Scatter, 0.2f, 0.5f).S(RuinedStatue, 2, 4, Scatter).S(Rubble, 3, 6, Scatter)
				.Weathered(Mossy, 1f).Pad(0.3f).Weighs(0.8f).In(1.3f, "Jungle Temple", "Forest Ruins", "Swamp Ruins").Elsewhere(0.7f);

			// Lizardfolk Village (important): stilt houses on the shore, egg mounds, piers; a wallow on dry ground.
			T(a, POIType.LizardfolkVillage, "Basking", Streets, Timber)
				.S(Stilt, 4, 8, Street).S(Fire, 1, 3, Scatter).S(Mound, 3, 6, Scatter, 0f, 0.1f, 1.2f, 1.6f).S(Pier, 1, 2, Piers).S(Storage, 1, 3, Scatter)
				.Weathered(Mossy, 0.4f).Pad(0.4f).In(1.4f, "Swamp", "Wetlands", "Mangrove", "Swamp Temple").Elsewhere(0.8f);
			T(a, POIType.LizardfolkVillage, "Mudhall Wallow", Scattered, Earth)
				.S(Stilt, 3, 6, Scatter).S(Fire, 1, 2, Centre).S(Mound, 4, 7, Scatter, 0f, 0.1f, 1.2f, 1.6f).S(Banner, 1, 2, Scatter).S(Storage, 1, 3, Scatter)
				.Pad(0.4f).Weighs(0.8f).In(1.4f, "Jungle", "Jungle Temple", "Peat Bog").Elsewhere(0.5f);

			// Idol Vault: a crypt door guarded by statues and braziers; a sealed treasury.
			T(a, POIType.IdolVault, "Gold Room", Single, Stone)
				.S(CryptDoor, 1, 1, Centre).S(Statue, 2, 2, Scatter, 0f, 0.2f, 1.1f, 1.3f).S(Brazier, 2, 2, Scatter).S(RuneStone, 0, 2, Scatter)
				.Weathered(Mossy, 0.6f).Pad(0.6f);
			T(a, POIType.IdolVault, "Sealed Treasury", Single, Stone)
				.S(Mausoleum, 1, 1, Centre).S(Statue, 2, 3, Scatter).S(Brazier, 2, 3, Scatter)
				.Weathered(Mossy, 0.6f).Pad(0.6f).Weighs(0.7f);

			// ── Pack Wet: wetland ────────────────────────────────────────
			// Crannog (important enough for two): round houses on piles joined by timber causeways over lake shallows.
			T(a, POIType.Crannog, "Lake Dwelling", Scattered, Timber)
				.S(PileRoundHouse, 3, 6, Scatter).S(Pier, 2, 5, Piers).S(Storage, 1, 3, Scatter).S(Fire, 0, 1, Scatter).S(Banner, 1, 1, Scatter)
				.Weathered(Algae, 0.3f).Wet().Pad(0.1f);
			T(a, POIType.Crannog, "Isle Hall", Scattered, Timber)
				.S(PileRoundHouse, 4, 7, Scatter).S(Pier, 2, 4, Piers).S(Watchtower, 1, 1, Centre).S(Storage, 1, 3, Scatter)
				.Weathered(Algae, 0.3f).Wet().Pad(0.1f).Weighs(0.6f);

			// Hag's Cauldron: a fire, a great pot (the altar), bones and green faerie lights.
			T(a, POIType.HagsCauldron, "Kettle", Single, Timber)
				.S(Fire, 1, 1, Centre).S(Altar, 1, 1, Scatter, 0f, 0.1f, 0.7f, 0.9f).S(Bones, 2, 4, Scatter).S(Glowjar, 3, 3, Ring)
				.Weathered(Mossy, 0.5f).Pad(0.5f);
			T(a, POIType.HagsCauldron, "Witch Pot", Single, Timber)
				.S("=CookingTripod", 1, 1, Centre).S(Bones, 1, 3, Scatter).S(Glowjar, 2, 3, Scatter).S(LeanTo, 0, 1, Scatter)
				.Weathered(Mossy, 0.5f).Pad(0.5f).Weighs(0.7f);

			// Hydra Lair: huge bones and fouled mounds at the water.
			T(a, POIType.HydraLair, "Many Heads", Scattered, Earth)
				.S(Bones, 4, 7, Scatter, 0f, 0f, 1.6f, 2.2f).S(Mound, 2, 4, Scatter, 0f, 0.1f, 1.5f, 2f).S(Rubble, 1, 3, Scatter)
				.Weathered(Algae, 0.5f).Pad(0.2f);
			T(a, POIType.HydraLair, "Skull Fen", Single, Earth)
				.S(Mound, 1, 1, Centre, 0f, 0.1f, 2f, 2.4f).S(Bones, 5, 8, Ring, 0f, 0f, 2f, 2.8f)
				.Weathered(Algae, 0.5f).Pad(0.2f).Weighs(0.7f);

			// Reed Village (important): reed halls on islets with piers; also strung along streets.
			T(a, POIType.ReedVillage, "Mudhif Isle", Scattered, Timber)
				.S(ReedHall, 4, 8, Scatter).S(Pier, 2, 4, Piers).S(Fire, 1, 2, Scatter).S(Storage, 1, 3, Scatter)
				.Wet().Pad(0.1f);
			T(a, POIType.ReedVillage, "Marsh Town", Streets, Timber)
				.S(ReedHall, 5, 8, Street).S(Pier, 1, 3, Piers).S(Fire, 1, 2, Scatter).S(Storage, 1, 3, Scatter).S(Banner, 1, 1, Scatter)
				.Pad(0.2f).Weighs(0.7f).In(1.3f, "Estuary", "Wetlands").Elsewhere(0.8f);

			// Coven Circle: black stones round a fire and an altar.
			T(a, POIType.CovenCircle, "Black Ring", Round, Stone)
				.S(CircleStone, 6, 9, Ring).S(Fire, 1, 1, Centre).S(Altar, 1, 1, Centre).S(Bones, 1, 3, Scatter)
				.Weathered(Charred, 0.3f).Pad(0.6f);
			T(a, POIType.CovenCircle, "Hag Ring", Round, Stone)
				.S(Menhir, 5, 7, Ring, 0f, 0.3f).S(Brazier, 1, 1, Centre).S(Glowjar, 3, 4, Scatter).S(Bones, 0, 2, Scatter)
				.Weathered(Mossy, 0.5f).Pad(0.6f).Weighs(0.7f);

			// Lizardfolk Hatchery: egg mounds round a banner and a fire.
			T(a, POIType.LizardfolkHatchery, "Egg Mounds", Scattered, Earth)
				.S(Mound, 5, 9, Scatter, 0f, 0.1f, 0.8f, 1.1f).S(Fire, 1, 2, Scatter).S(Banner, 1, 2, Scatter)
				.Pad(0.4f);
			T(a, POIType.LizardfolkHatchery, "Clutch", Round, Earth)
				.S(Fire, 1, 1, Centre).S(Mound, 5, 8, Ring, 0f, 0.1f, 0.8f, 1.1f).S(Banner, 1, 1, Scatter)
				.Pad(0.4f).Weighs(0.7f);

			// Swamp Cathedral (important): a cathedral sunk and tilted in the mire; a chapel; a walled black abbey.
			T(a, POIType.SwampCathedral, "Sunk Minster", Single, Stone)
				.S(RuinedMinster, 1, 1, Centre, 0.4f, 0.7f, 1.3f, 1.5f).S(Gravestone, 4, 8, Scatter, 0.2f, 0.6f).S(Rubble, 2, 4, Scatter)
				.Weathered(Algae, 1f).Wet().Pad(0.2f).In(1.6f, "Swamp Temple", "Swamp Ruins").Elsewhere(0.6f);
			T(a, POIType.SwampCathedral, "Mire Chapel", Single, Stone)
				.S(Chapel, 1, 1, Centre, 0.2f, 0.4f).S(Gravestone, 4, 8, Scatter, 0.2f, 0.5f).S(Mausoleum, 0, 1, Scatter, 0.2f, 0.5f)
				.Weathered(Algae, 1f).Wet().Pad(0.2f).Weighs(0.8f);
			T(a, POIType.SwampCathedral, "Black Abbey", Round, Stone)
				.S(RuinedMinster, 1, 1, Centre, 0.4f, 0.7f, 1.3f, 1.5f).S(RuinedWall, 4, 6, Ring).S(Gravestone, 3, 6, Scatter, 0.2f, 0.6f).S(BrokenColumn, 2, 4, Scatter)
				.Weathered(Algae, 1f).Wet().Pad(0.2f).Weighs(0.6f).In(1.3f, "Swamp", "Peat Bog").Elsewhere(0.7f);

			// ── Pack V: volcanic ─────────────────────────────────────────
			// Fire Temple (important): a temple of braziers, a ring of fire stands, or a colonnade round a fire altar.
			T(a, POIType.FireTemple, "Flame House", Single, Stone)
				.S(Temple, 1, 1, Centre).S(Brazier, 4, 6, Ring).S(Altar, 1, 1, Scatter)
				.Weathered(Charred, 0.7f).Pad(0.6f).In(1.5f, "Volcanic Temple", "Volcanic").Elsewhere(0.8f);
			T(a, POIType.FireTemple, "Pyre", Round, Stone)
				.S(Brazier, 4, 6, Ring).S(Altar, 1, 1, Centre).S(Menhir, 2, 3, Scatter, 0f, 0.2f, 0.8f, 1f)
				.Weathered(Charred, 0.8f).Pad(0.6f).Weighs(0.8f);
			T(a, POIType.FireTemple, "Colonnade", Round, Stone)
				.S(Column, 5, 8, Ring).S(Altar, 1, 1, Centre).S(Brazier, 4, 4, Scatter).S(RuneStone, 1, 2, Scatter)
				.Weathered(Charred, 0.7f).Pad(0.6f).Weighs(0.7f).In(1.3f, "Geyser Basin", "Sulphur Flats", "Desert", "Badlands").Elsewhere(0.8f);

			// Fire Giant Hall: a basalt longhouse, a giant's smithy, braziers and bones.
			T(a, POIType.FireGiantHall, "Ember Hall", Scattered, Stone)
				.S(BasaltHall, 1, 1, Centre).S(Smithy, 1, 1, Scatter, 0f, 0f, 1.8f, 1.8f).S(Brazier, 2, 4, Scatter, 0f, 0f, 1.5f, 1.8f).S(Bones, 2, 4, Scatter, 0f, 0f, 1.6f, 2f).S(Rubble, 1, 3, Scatter)
				.Weathered(Charred, 0.5f).Pad(0.6f);
			T(a, POIType.FireGiantHall, "Forgehold", Scattered, Stone)
				.S(BasaltHallTwin, 1, 1, Centre).S(RuinedWall, 2, 4, Scatter).S(Brazier, 2, 3, Scatter, 0f, 0f, 1.5f, 1.8f).S(Bones, 1, 3, Scatter, 0f, 0f, 1.6f, 2f)
				.Weathered(Charred, 0.6f).Pad(0.6f).Weighs(0.7f);

			// Salamander Lair: a hot earth mound, fires, bones and scorched stone.
			T(a, POIType.SalamanderLair, "Ember Den", Scattered, Earth)
				.S(Mound, 1, 2, Centre, 0f, 0.2f, 1.1f, 1.4f).S(Fire, 2, 2, Scatter).S(Bones, 2, 4, Scatter).S(Rubble, 2, 4, Scatter)
				.Weathered(Charred, 0.8f).Pad(0.5f);
			T(a, POIType.SalamanderLair, "Hot Hollow", Round, Earth)
				.S(Fire, 2, 3, Centre).S(Rubble, 4, 6, Ring).S(Bones, 1, 3, Scatter)
				.Weathered(Charred, 0.9f).Pad(0.5f).Weighs(0.7f);

			// Ash-buried City (important): roofs and walls sunk in grey ash, cast figures, columns; a smaller cinder town.
			T(a, POIType.AshBuriedCity, "Grey City", Scattered, Stone)
				.S(RuinedHouse, 6, 12, Scatter, 0.4f, 0.7f).S(BrokenColumn, 2, 4, Scatter).S(Statue, 3, 5, Scatter, 0.1f, 0.3f).S(Rubble, 3, 6, Scatter)
				.Weathered(Charred, 0.8f).Pad(0.3f).In(1.4f, "Volcanic", "Volcanic Temple").Elsewhere(0.8f);
			T(a, POIType.AshBuriedCity, "Cinder Town", Scattered, Stone)
				.S(RuinedHouse, 4, 8, Scatter, 0.5f, 0.8f).S(Gate, 1, 1, Centre, 0.4f, 0.7f).S(Statue, 2, 4, Scatter, 0.1f, 0.3f).S(RuinedWall, 2, 4, Scatter).S(Rubble, 3, 6, Scatter)
				.Weathered(Charred, 0.8f).Pad(0.3f).Weighs(0.7f);
		}
	}
}
#endif
