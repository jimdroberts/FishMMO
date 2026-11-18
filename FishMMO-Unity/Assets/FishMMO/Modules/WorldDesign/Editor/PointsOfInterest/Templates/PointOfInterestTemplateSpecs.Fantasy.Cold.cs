#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class PointOfInterestTemplateSpecs
	{
		/// <summary>The Cold pack's own pieces (StructurePieces.Fantasy.Cold.cs), by id.</summary>
		private const string ColdIceHall = "=IceBlockHall", ColdDragon = "=IceEncasedDragon", ColdLichSpire = "=LichSpire", ColdPalaceHall = "=IceSpireHall",
			ColdIceSpire = "=IceSpire", ColdSoldier = "=FrozenSoldier", ColdLongship = "=BeachedLongship", ColdCoralHall = "=CoralHall", ColdShellHouse = "=ShellHouse";
		/// <summary>The two shipwrecks (Shipwreck v0 a cog's stern half, v1 a longship's bones) and the four log piles' kin, pinned by variant.</summary>
		private const string ColdCogWreck = "=Shipwreck#0", ColdLongshipWreck = "=Shipwreck#1";

		/// <summary>
		/// The fantasy wave (2026-10-27, poi-fantasy-wave/WAVE.md): the templates of the Cold pack's kinds (21; the tidal island abbey is
		/// the Shapes agent's). The frozen north (the frost giants' steading, the frozen army, the dragon in the ice, the lich's spire, the
		/// ice court), the coast (the cliff castle, the smugglers' and wreckers' works, the siren's rock, the longship landing, the kraken's
		/// wrecks) and the waters (the merfolk's reef halls, the kraken's grounds, the lady's mere, the nymph's pool, the lake monster's lair).
		/// Every variant weighs 1 elsewhere: each biome's spec file weighs them (.Prefers), as named in memory poi-fantasy-wave/checkpoints/cold.md.
		/// </summary>
		private static void FantasyCold(List<PointOfInterestTemplateSpec> a)
		{
			// ── Pack C: the frozen north ──────────────────────────────

			// A frost giant's steading. Rime Hall: one great ice-block longhouse (giant-sized), a bonfire, mammoth and giant bones heaped
			// about it, banners of hide; the giants hold it, their chieftain in the large one. Winter Hold: the same hall behind a giant-built
			// stone curtain and its gate, braziers lit on the wall-walk (large only; any race holds it). Sundered Steading: the hall's ice
			// block-work broken and the curtain thrown down: nobody holds it any more.
			T(a, POIType.FrostGiantSteading, "Rime Hall", Scattered, Stone)
				.S(ColdIceHall, 1, 1, Centre, 0f, 0f, 1.3f, 1.5f).S(Fire, 1, 2, Scatter, 0f, 0f, 1.6f, 2.2f).S(Bones, 3, 7, Scatter, 0f, 0f, 2f, 3f)
				.S(Banner, 2, 4, Scatter, 0f, 0f, 1.5f, 1.8f, style: Hide).S(Storage, 2, 5, Scatter, 0f, 0f, 1.6f, 2f, style: Timber).S(ColdIceSpire, 0, 3, Scatter, 0f, 0f, 0.8f, 1.2f)
				.Races("Giant").Pad(0.7f);
			T(a, POIType.FrostGiantSteading, "Winter Hold", Walled, Stone)
				.S(Wall, 1, 1, Perimeter, 0f, 0f, 1.25f, 1.3f).S(Gate, 1, 1, Gates, 0f, 0f, 1.25f, 1.3f).S(ColdIceHall, 1, 1, Centre, 0f, 0f, 1f, 1.1f)
				.S(Brazier, 3, 6, Scatter, 0f, 0f, 1.6f, 2f).S(Bones, 3, 6, Scatter, 0f, 0f, 2f, 3f).S(Banner, 2, 4, Scatter, 0f, 0f, 1.5f, 1.8f)
				.Weighs(0.7f).Sized(false, false, true).Pad(0.7f);
			T(a, POIType.FrostGiantSteading, "Sundered Steading", Scattered, Stone)
				.S(ColdIceHall, 1, 1, Centre, 0f, 0f, 1.3f, 1.5f).S(RuinedWall, 4, 7, Ring, 0.3f, 0.7f, 1.5f, 1.8f).S(Rubble, 3, 6, Scatter, 0f, 0f, 1.5f, 2f)
				.S(Bones, 3, 6, Scatter, 0f, 0f, 2f, 3f).S(ColdIceSpire, 1, 3, Scatter)
				.Weighs(0.5f).Pad(0.6f).Held(PointOfInterestHolder.Ruined);

			// A rune stone: the carved memorial of the north, one or three raised on a barrow (Bautastein). Cairn: stones on a mound. Fallen: toppled and moss-grown.
			T(a, POIType.RuneStone, "Bautastein", Scattered, Stone)
				.S(RuneStone, 1, 3, Scatter, 0f, 0.1f, 1.1f, 1.5f).S(Mound, 1, 1, Centre, 0f, 0.2f, 0.8f, 1.1f).S(Menhir, 0, 2, Scatter, 0.1f, 0.4f, 0.5f, 0.8f)
				.Weathered(PointOfInterestFinish.Mossy, 0.4f).Pad(0.6f);
			T(a, POIType.RuneStone, "Memorial Ring", Round, Stone)
				.S(RuneStone, 1, 1, Centre, 0f, 0.1f, 1.4f, 1.8f).S(Menhir, 5, 8, Ring, 0.1f, 0.3f, 0.4f, 0.7f).S(Gravestone, 0, 3, Scatter, 0.2f, 0.5f)
				.Weathered(PointOfInterestFinish.Mossy, 0.5f).Weighs(0.6f).Pad(0.7f);

			// The frozen army: a host caught in the ice where it stood. Host: its ranks in rows, soldiers sunk to the thigh in ice, spears
			// raised, standards torn and stiff, bones between, a rune stone at its head. Rout: the same host broken and scattered.
			T(a, POIType.FrozenArmy, "Host", Grid, Stone)
				.S(ColdSoldier, 8, 14, Rows).S(Banner, 2, 4, Scatter, 0.3f, 0.6f).S(Bones, 2, 5, Scatter).S(RuneStone, 0, 1, Centre).S(ColdIceSpire, 0, 2, Scatter, 0f, 0f, 0.5f, 0.8f)
				.Pad(0.7f);
			T(a, POIType.FrozenArmy, "Rout", Scattered, Stone)
				.S(ColdSoldier, 6, 12, Scatter).S(RuinedStatue, 0, 2, Scatter, 0f, 0f, 0.8f, 1f).S(Bones, 3, 7, Scatter).S(Banner, 1, 3, Scatter, 0.4f, 0.8f).S(Rubble, 1, 3, Scatter)
				.Weighs(0.7f).Pad(0.7f);

			// A dragon asleep in the ice. Sleeper: the great hill of ice with its beast breaking out of it, shards and bones round it. Sealed
			// Sleeper: a ring of rune stones standing round it to hold it down, braziers burning cold between them.
			T(a, POIType.DragonInTheIce, "Sleeper", Single, Stone)
				.S(ColdDragon, 1, 1, Centre, 0f, 0f, 1.1f, 1.4f).S(Bones, 2, 5, Scatter, 0f, 0f, 1.6f, 2.4f).S(ColdIceSpire, 2, 4, Scatter).S(RuneStone, 0, 2, Scatter)
				.Pad(0.6f);
			T(a, POIType.DragonInTheIce, "Sealed Sleeper", Round, Stone)
				.S(ColdDragon, 1, 1, Centre, 0f, 0f, 1.1f, 1.3f).S(RuneStone, 5, 8, Ring, 0f, 0.15f, 1.3f, 1.7f).S(Brazier, 2, 4, Scatter, 0f, 0f, 1.2f, 1.5f).S(Bones, 1, 3, Scatter, 0f, 0f, 1.5f, 2f)
				.Weighs(0.7f).Pad(0.6f);

			// Mammoth bones in a heap: tusks and ribs scaled to the beast, where a herd died (Tusk Field); in a ring, where the hunters
			// of old set them (Tusk Ring).
			T(a, POIType.MammothGraveyard, "Tusk Field", Scattered, Earth)
				.S(Bones, 8, 14, Scatter, 0f, 0f, 2.2f, 3f).S(Rubble, 1, 3, Scatter).S(Mound, 0, 1, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.2f).Pad(0.5f);
			T(a, POIType.MammothGraveyard, "Tusk Ring", Round, Earth)
				.S(Bones, 6, 10, Ring, 0f, 0f, 2.5f, 3.3f).S(Fire, 0, 1, Centre).S(Menhir, 0, 2, Scatter, 0.1f, 0.4f, 0.6f, 0.9f, style: Stone)
				.Weighs(0.6f).Pad(0.6f);

			// A winter wolf's den: a snowed-over mound, bones about its mouth. Snow Den fits any pack's site; Pack Hollow (the beasts) is bigger.
			T(a, POIType.WinterWolfDen, "Snow Den", Scattered, Earth)
				.S(Mound, 1, 2, Centre, 0f, 0.1f, 1.3f, 1.8f).S(Bones, 3, 6, Scatter).S(Rubble, 0, 2, Scatter, style: Stone)
				.Pad(0.5f);
			T(a, POIType.WinterWolfDen, "Pack Hollow", Scattered, Earth)
				.S(Mound, 2, 3, Scatter, 0f, 0.1f, 1.4f, 2f).S(Bones, 5, 9, Scatter, 0f, 0f, 1.2f, 1.8f).S(ColdIceSpire, 0, 2, Scatter, 0f, 0f, 0.4f, 0.7f)
				.Races("Beast").Weighs(0.8f).Pad(0.5f);

			// The aurora shrine: a ring of standing stones about an altar, cold lights burning in jars. Sky Ring (a stone circle) and Light Stones (rune-lit).
			T(a, POIType.AuroraShrine, "Sky Ring", Round, Stone)
				.S(CircleStone, 8, 12, Ring).S(Altar, 1, 1, Centre).S(Glowjar, 3, 5, Scatter, style: Stone).S(Brazier, 0, 2, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.3f).Pad(0.8f);
			T(a, POIType.AuroraShrine, "Light Stones", Round, Stone)
				.S(RuneStone, 5, 7, Ring, 0f, 0.1f, 1.1f, 1.4f).S(Altar, 1, 1, Centre).S(Glowjar, 2, 4, Scatter, style: Stone).S(ColdIceSpire, 0, 2, Scatter, 0f, 0f, 0.5f, 0.8f)
				.Weighs(0.7f).Pad(0.8f);

			// The lich's spire. Frost Spire: the black tower in its bands of ice, graves and cold braziers round it, a broken ring of old
			// wall. Barrow Tower: the plain black spire among the dead (graves, a mausoleum, a mound), for the wastes where there is no ice.
			T(a, POIType.LichSpire, "Frost Spire", Single, Stone)
				.S(ColdLichSpire, 1, 1, Centre).S(Gravestone, 4, 8, Scatter, 0.1f, 0.5f).S(Brazier, 3, 5, Scatter).S(Bones, 3, 6, Scatter).S(RuinedWall, 2, 4, Ring)
				.S(ColdIceSpire, 1, 3, Scatter).Pad(0.8f);
			T(a, POIType.LichSpire, "Barrow Tower", Single, Stone)
				.S(DarkSpire, 1, 1, Centre).S(Mausoleum, 0, 1, Scatter).S(Mound, 2, 4, Scatter).S(Gravestone, 4, 9, Scatter, 0.1f, 0.5f).S(Brazier, 2, 4, Scatter).S(Bones, 3, 6, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 0.5f).Weighs(0.6f).Pad(0.8f);

			// The ice palace. Hall of Ice: the great hall among spires of ice, braziers, statues and banners. Frost Court: the same
			// hall guarded by the frozen court (soldiers sunk in ice among the spires). Fallen Court: broken, its hall alone among toppled
			// columns, shards and rubble; nobody holds it.
			T(a, POIType.IcePalace, "Hall of Ice", Round, Stone)
				.S(ColdPalaceHall, 1, 1, Centre).S(ColdIceSpire, 3, 6, Ring).S(Brazier, 2, 4, Scatter).S(Statue, 2, 4, Scatter).S(Banner, 2, 4, Scatter)
				.Pad(0.8f);
			T(a, POIType.IcePalace, "Frost Court", Scattered, Stone)
				.S(ColdPalaceHall, 1, 1, Centre).S(ColdSoldier, 4, 8, Scatter).S(ColdIceSpire, 4, 8, Scatter).S(Statue, 1, 3, Scatter).S(Brazier, 2, 4, Scatter)
				.Weighs(0.8f).Pad(0.8f);
			T(a, POIType.IcePalace, "Fallen Court", Scattered, Stone)
				.S(ColdPalaceHall, 1, 1, Centre).S(ColdIceSpire, 4, 8, Scatter).S(BrokenColumn, 3, 6, Scatter, 0.4f, 0.8f).S(RuinedWall, 2, 4, Ring).S(Rubble, 3, 6, Scatter)
				.S(RuinedStatue, 1, 2, Scatter).Weighs(0.5f).Pad(0.7f).Held(PointOfInterestHolder.Ruined);

			// A ship frozen into the shore ice. Locked Hulk: a cog's stern half in its ice, shards heaved up round it, bones. Ghost
			// Hull: a longship's ribs standing out of the floe, a dead crewman or two sunk beside it.
			T(a, POIType.FrozenShipwreck, "Locked Hulk", Scattered, Timber)
				.S(ColdCogWreck, 1, 1, Centre).S(ColdIceSpire, 3, 6, Scatter, 0f, 0f, 0.5f, 0.9f).S(Storage, 2, 5, Scatter, 0.3f, 0.7f).S(Bones, 1, 3, Scatter)
				.Pad(0.5f);
			T(a, POIType.FrozenShipwreck, "Ghost Hull", Scattered, Timber)
				.S(ColdLongshipWreck, 1, 1, Centre).S(ColdIceSpire, 2, 5, Scatter, 0f, 0f, 0.5f, 0.8f).S(ColdSoldier, 0, 2, Scatter).S(Bones, 2, 4, Scatter)
				.Weighs(0.8f).Pad(0.5f);

			// ── Pack Co: the coast ────────────────────────────────────

			// A castle on a headland. Sea Fort: a stone curtain across the neck with its gate, towers at the ends, the keep behind it
			// (rocky coasts and beaches). Head Fort: the same in timber, a palisade, watchtowers and a longhouse (the raiders' coasts,
			// the cold shores). Ruined Cliff Castle: the keep a stump, its curtain broken, nobody there.
			T(a, POIType.CliffCastle, "Sea Fort", Walled, Stone)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(WallTower, 2, 4, Towers).S(Keep, 1, 1, Centre).S(Brazier, 2, 4, Scatter).S(Banner, 2, 4, Scatter)
				.S(Storage, 3, 6, Scatter).S(Stable, 0, 1, Rows).Pad(0.8f);
			T(a, POIType.CliffCastle, "Head Fort", Walled, Timber)
				.S(Palisade, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(Watchtower, 1, 3, Towers).S(Longhouse, 1, 2, Rows).S(Keep, 0, 1, Centre, style: Stone)
				.S(Banner, 2, 4, Scatter).S(Fire, 1, 2, Scatter).S(Storage, 3, 6, Scatter).S(TorchPost, 2, 4, Scatter)
				.Races(Outlaw).Weighs(0.8f).Pad(0.8f);
			T(a, POIType.CliffCastle, "Ruined Cliff Castle", Scattered, Stone)
				.S(RuinedKeep, 1, 1, Centre).S(RuinedWall, 4, 8, Ring).S(CollapsedTower, 1, 2, Scatter).S(Rubble, 3, 6, Scatter).S(Bones, 0, 2, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.5f).Weighs(0.5f).Pad(0.6f).Held(PointOfInterestHolder.Ruined);

			// The smugglers' tunnel: a door set into the cliff face, kegs and bales stacked outside it, a lean-to and a lantern.
			T(a, POIType.SmugglersTunnel, "Run", Scattered, Timber)
				.S(CryptDoor, 1, 1, Centre).S(Storage, 4, 8, Scatter).S(LeanTo, 1, 2, Scatter).S(TorchPost, 1, 2, Scatter).S(Fire, 0, 1, Scatter)
				.Pad(0.5f);
			T(a, POIType.SmugglersTunnel, "Rum Hole", Scattered, Stone)
				.S(CryptDoor, 1, 1, Centre).S(Storage, 3, 6, Scatter).S(Tent, 1, 2, Scatter, style: Hide).S(Pier, 0, 1, Piers).S(Bones, 0, 2, Scatter).S(Banner, 0, 1, Scatter)
				.Weighs(0.7f).Pad(0.5f);

			// The wreckers' beacon: a false light. False Light: a fire bowl on a stone plinth on the cliff, a lean-to for the wreckers and
			// the wreck they lured below. Beacon Pyre: a great timber bonfire stacked high.
			T(a, POIType.WreckersBeacon, "False Light", Single, Stone)
				.S(Plinth, 1, 1, Centre, 0f, 0f, 1.4f, 1.8f).S(Brazier, 1, 2, Scatter, 0f, 0f, 1.5f, 2f).S(LeanTo, 1, 2, Scatter, style: Timber).S(Storage, 1, 3, Scatter, style: Timber)
				.S(ColdCogWreck, 0, 1, Scatter, 0.2f, 0.5f).S(Bones, 1, 3, Scatter).Pad(0.6f);
			T(a, POIType.WreckersBeacon, "Beacon Pyre", Single, Timber)
				.S(Fire, 1, 1, Centre, 0f, 0f, 2f, 2.6f).S(LogPile, 2, 4, Scatter).S(LeanTo, 1, 2, Scatter).S(ColdLongshipWreck, 0, 1, Scatter).S(Bones, 0, 2, Scatter)
				.Weighs(0.8f).Pad(0.6f);

			// The siren's rock: her statue on a rock among stones, the bones and the wrecks of the sailors she called. Singing Stone;
			// Wreck Rock has the wrecks.
			T(a, POIType.SirenRock, "Singing Stone", Single, Stone)
				.S(Statue, 1, 1, Centre, 0f, 0.15f, 1.1f, 1.4f).S(Menhir, 3, 6, Scatter, 0.1f, 0.4f, 0.8f, 1.6f).S(Bones, 2, 4, Scatter).S(Rubble, 1, 3, Scatter)
				.Weathered(PointOfInterestFinish.Algae, 0.4f).Pad(0.5f);
			T(a, POIType.SirenRock, "Wreck Rock", Scattered, Stone)
				.S(Statue, 1, 1, Centre, 0.1f, 0.3f, 1.1f, 1.3f).S(Menhir, 2, 5, Scatter, 0.1f, 0.4f, 1f, 1.8f).S(ColdLongshipWreck, 1, 1, Scatter).S(Bones, 3, 6, Scatter)
				.Weathered(PointOfInterestFinish.Algae, 0.5f).Weighs(0.8f).Pad(0.5f);

			// The sea witch's grotto: a shell-strewn shrine at the cave's mouth, glowing jars and bones, her kettle fire. Shell Shrine
			// fits any site; Witch's Hole (the outsiders) has her altar.
			T(a, POIType.SeaWitchGrotto, "Shell Shrine", Single, Stone)
				.S(Shrine, 1, 1, Centre).S(Glowjar, 3, 5, Scatter, style: Stone).S(Bones, 3, 6, Scatter).S(Rubble, 1, 3, Scatter).S(Fire, 0, 1, Scatter)
				.Weathered(PointOfInterestFinish.Algae, 0.5f).Pad(0.5f);
			T(a, POIType.SeaWitchGrotto, "Witch's Hole", Scattered, Stone)
				.S(Altar, 1, 1, Centre).S(Glowjar, 2, 4, Scatter, style: Stone).S(Bones, 4, 8, Scatter).S(Menhir, 1, 3, Scatter, 0.1f, 0.4f, 0.6f, 1f).S(Storage, 1, 2, Scatter)
				.Weathered(PointOfInterestFinish.Algae, 0.6f).Races("Outsider", "Aquatic").Weighs(0.8f).Pad(0.5f);

			// The longship landing. Raid Camp: longships hauled up on log rollers, war tents round a fire, banners, plunder stacked.
			// Strand Hold: a longhouse built behind the ships, a stake palisade at the tide line. Abandoned Landing: the camp long cold,
			// a ship's bones on the strand.
			T(a, POIType.LongshipLanding, "Raid Camp", Round, Hide)
				.S(Fire, 1, 1, Centre).S(Tent, 3, 5, Ring).S(ColdLongship, 2, 3, Scatter).S(Banner, 2, 3, Scatter).S(Storage, 3, 6, Scatter, style: Timber).S(Bones, 0, 2, Scatter)
				.Races(Outlaw).Pad(0.6f);
			T(a, POIType.LongshipLanding, "Strand Hold", Scattered, Timber)
				.S(Longhouse, 1, 1, Centre).S(ColdLongship, 2, 3, Scatter).S(Palisade, 3, 6, Scatter).S(Fire, 1, 2, Scatter).S(Banner, 2, 4, Scatter).S(Storage, 3, 6, Scatter)
				.S(TorchPost, 1, 3, Scatter).Races(Outlaw).Weighs(0.8f).Pad(0.6f);
			T(a, POIType.LongshipLanding, "Abandoned Landing", Scattered, Timber)
				.S(ColdLongshipWreck, 1, 2, Scatter).S(Tent, 1, 3, Scatter, 0.5f, 0.9f, style: Hide).S(Fire, 1, 1, Centre).S(Bones, 2, 4, Scatter).S(Storage, 1, 3, Scatter, 0.4f, 0.8f)
				.Weighs(0.5f).Pad(0.6f).Held(PointOfInterestHolder.Ruined);

			// The kraken's wreck: a hull crushed and broken in the monster's grip, tentacle bones scaled huge strewn about. Crushed Cog
			// (a cog's stern half), Splintered Longship (a longship's ribs).
			T(a, POIType.KrakenWreck, "Crushed Cog", Scattered, Timber)
				.S(ColdCogWreck, 1, 1, Centre).S(Bones, 4, 8, Scatter, 0f, 0f, 2.6f, 3.4f).S(Storage, 2, 5, Scatter, 0.3f, 0.7f).S(Rubble, 1, 3, Scatter, style: Stone)
				.Weathered(PointOfInterestFinish.Algae, 0.3f).Pad(0.5f);
			T(a, POIType.KrakenWreck, "Splintered Longship", Scattered, Timber)
				.S(ColdLongshipWreck, 1, 1, Centre).S(Bones, 4, 8, Scatter, 0f, 0f, 2.6f, 3.4f).S(Banner, 0, 1, Scatter, 0.6f, 0.9f)
				.Weathered(PointOfInterestFinish.Algae, 0.3f).Weighs(0.8f).Pad(0.5f);

			// ── Pack S: the waters ────────────────────────────────────

			// The merfolk's village, on the sea floor (no biome of its own: the reef, the coastal water and the open sea). Coral Halls: a
			// coral hall among scattered shell houses, a statue, sea-lamps. Shell Town: the shell houses ringed round the hall. Drowned
			// Reefhold: the hall left standing, the houses broken (nobody there); fits any site.
			T(a, POIType.MerfolkVillage, "Coral Halls", Scattered, Stone)
				.S(ColdCoralHall, 1, 1, Centre).S(ColdShellHouse, 6, 10, Scatter).S(Statue, 1, 2, Scatter).S(Glowjar, 3, 6, Scatter).S(Column, 2, 5, Scatter)
				.Weathered(PointOfInterestFinish.Algae).Races(Aquatic).Pad(0f);
			T(a, POIType.MerfolkVillage, "Shell Town", Round, Stone)
				.S(ColdCoralHall, 1, 1, Centre).S(ColdShellHouse, 5, 9, Ring).S(Glowjar, 3, 5, Scatter).S(Menhir, 0, 3, Scatter, 0f, 0.2f, 0.6f, 1f)
				.Weathered(PointOfInterestFinish.Algae).Races(Aquatic).Weighs(0.8f).Pad(0f);
			T(a, POIType.MerfolkVillage, "Drowned Reefhold", Scattered, Stone)
				.S(ColdCoralHall, 1, 1, Centre).S(ColdShellHouse, 3, 6, Scatter, 0.4f, 0.8f).S(Rubble, 2, 5, Scatter).S(Bones, 1, 3, Scatter).S(BrokenColumn, 1, 3, Scatter)
				.Weathered(PointOfInterestFinish.Algae).Weighs(0.5f).Pad(0f).Held(PointOfInterestHolder.Ruined);

			// The kraken's grounds: a floor of crushed ships and the monster's own bones, scaled huge. Crushed Fleet: cogs; Sunken Fleet: longships.
			T(a, POIType.KrakenGrounds, "Crushed Fleet", Scattered, Timber)
				.S(ColdCogWreck, 2, 3, Scatter).S(Bones, 4, 8, Scatter, 0f, 0f, 2.6f, 3.4f).S(Rubble, 2, 4, Scatter, style: Stone).S(Storage, 2, 5, Scatter, 0.4f, 0.8f)
				.Weathered(PointOfInterestFinish.Algae).Pad(0f);
			T(a, POIType.KrakenGrounds, "Sunken Fleet", Scattered, Timber)
				.S(ColdLongshipWreck, 2, 4, Scatter).S(Bones, 4, 8, Scatter, 0f, 0f, 2.6f, 3.4f).S(Menhir, 0, 2, Scatter, 0.3f, 0.7f, 0.8f, 1.4f)
				.Weathered(PointOfInterestFinish.Algae).Weighs(0.8f).Pad(0f);

			// The lady of the lake: her mere shrine on the shore, her statue holding up a sword, a short pier out over the water.
			// Mere Shrine; Sword Stone: the sword in a stone among a ring of lesser ones.
			T(a, POIType.LadyOfTheLake, "Mere Shrine", Single, Stone)
				.S(Shrine, 1, 1, Centre).S(Statue, 1, 1, Scatter, 0f, 0.1f, 1.1f, 1.3f).S(Pier, 1, 1, Piers).S(Menhir, 0, 3, Scatter, 0.1f, 0.4f, 0.5f, 0.8f).S(TorchPost, 1, 2, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.3f).Pad(0.7f);
			T(a, POIType.LadyOfTheLake, "Sword Stone", Round, Stone)
				.S(Altar, 1, 1, Centre).S(Statue, 1, 1, Scatter, 0f, 0.1f, 1.1f, 1.3f).S(Menhir, 5, 8, Ring, 0.1f, 0.3f, 0.5f, 0.8f).S(Pier, 0, 1, Piers)
				.Weathered(PointOfInterestFinish.Mossy, 0.4f).Weighs(0.7f).Pad(0.7f);

			// The nymph's pool: a small spring shrine with her statue and faerie lights. Fair Spring (a shrine and a well); Moss Pool (the pool in its stones).
			T(a, POIType.NymphPool, "Fair Spring", Single, Stone)
				.S(Shrine, 1, 1, Centre).S(Statue, 1, 1, Scatter, 0f, 0.1f).S(Well, 0, 1, Scatter).S(Glowjar, 2, 4, Scatter, style: Stone)
				.Weathered(PointOfInterestFinish.Mossy, 0.6f).Pad(0.8f);
			T(a, POIType.NymphPool, "Moss Pool", Round, Stone)
				.S(Statue, 1, 1, Centre, 0.1f, 0.3f).S(Menhir, 4, 7, Ring, 0.1f, 0.4f, 0.4f, 0.7f).S(Glowjar, 3, 5, Scatter, style: Stone)
				.Weathered(PointOfInterestFinish.Mossy, 0.8f).Weighs(0.8f).Pad(0.8f);

			// The lake monster's lair, on the lake bed: bones and rubble heaped, a wreck or two it dragged down. Bone Hole; Drowned Hulk fits any site.
			T(a, POIType.LakeMonsterLair, "Bone Hole", Scattered, Stone)
				.S(Bones, 4, 8, Scatter, 0f, 0f, 1.5f, 2.5f).S(Rubble, 3, 6, Scatter).S(Menhir, 0, 2, Scatter, 0.3f, 0.7f, 0.5f, 0.8f)
				.Weathered(PointOfInterestFinish.Algae).Races("Aquatic", "Beast").Pad(0f);
			T(a, POIType.LakeMonsterLair, "Drowned Hulk", Scattered, Timber)
				.S(ColdCogWreck, 1, 1, Centre).S(Bones, 3, 7, Scatter, 0f, 0f, 1.5f, 2.5f).S(Storage, 2, 5, Scatter, 0.4f, 0.8f).S(Rubble, 1, 3, Scatter, style: Stone)
				.Weathered(PointOfInterestFinish.Algae).Weighs(0.8f).Pad(0f);
		}
	}
}
#endif
