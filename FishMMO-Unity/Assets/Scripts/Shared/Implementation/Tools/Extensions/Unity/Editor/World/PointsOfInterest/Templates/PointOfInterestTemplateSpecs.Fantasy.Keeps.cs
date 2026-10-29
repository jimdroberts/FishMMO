#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Who holds a place a template builds (2026-10-27, the fantasy wave: keeps and towers "mixed by land", garrisoned in the settled
	/// lands, ruined or held by orcs, bandits, the undead or a dark sorcerer in the wild): what PointOfInterestTemplateAuthoring.KindFeatures
	/// makes of the kind's recommended gameplay.
	/// </summary>
	public enum PointOfInterestHolder
	{
		/// <summary>Its garrison or its people: the kind's own features, unchanged.</summary>
		Garrison,
		/// <summary>A hostile band: no waypoint, respawn or service NPC; a monster pack, an elite from medium size (and a lord where the spec names one).</summary>
		Hostile,
		/// <summary>Nobody: no NPC, waypoint or respawn at all (its region, discovery, gathering and dungeon doors stay).</summary>
		Ruined,
	}

	public static partial class PointOfInterestTemplateSpecs
	{
		/// <summary>The dark spire and the elven tower (StructurePieces.Fantasy.Keeps.cs), by id.</summary>
		public const string DarkSpire = "=DarkSpire", ElvenSpire = "=ElvenSpire";

		/// <summary>
		/// The fantasy wave (2026-10-27, poi-fantasy-wave/WAVE.md): the keeps' and towers' new variants (Orc Hold, Ruined Keep, Dark Keep,
		/// Dwarven Hold, Elven Hall; Dark Tower, Ruined Watchtower, Elven Tower, Dwarven Tower). The lands' own variants weigh 0 elsewhere
		/// and the ruins 0.3: each biome's spec file names the ones it holds (BiomeArtSpec.Entry.Prefers), the wild the hostile and ruined
		/// ones, the woods the elves', the high places the dwarves', so a biome no file has reached yet keeps its garrisons.
		/// </summary>
		private static void FantasyKeeps(List<PointOfInterestTemplateSpec> a)
		{
			// ── Keeps ──
			// An orc hold: a keep taken and ringed with a stake palisade, watchtowers on it, war tents and a longhouse in the ward, the
			// heads and bones of the beaten about it, their banners and fires; half burned.
			T(a, POIType.Keep, "Orc Hold", Walled, Timber).Elsewhere(0f)
				.S(Palisade, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(Watchtower, 1, 3, Towers)
				.S(Keep, 1, 1, Centre, style: Stone).S(Longhouse, 0, 1, Rows).S(Tent, 2, 5, Rows, style: Hide)
				.S(Banner, 3, 6, Scatter).S(Bones, 3, 6, Scatter).S(Fire, 2, 3, Scatter).S(Storage, 2, 4, Scatter).S(TorchPost, 1, 3, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 0.5f).Races(Outlaw).Held(PointOfInterestHolder.Hostile);
			// A keep fallen in: its stump in its rubble, a broken curtain round it, a fallen tower and a house or two, moss over all.
			// Nobody lives here: the keep's one variant any race fits in the wild.
			T(a, POIType.Keep, "Ruined Keep", Scattered, Stone).Elsewhere(0.3f)
				.S(RuinedKeep, 1, 1, Centre).S(RuinedWall, 4, 8, Ring).S(CollapsedTower, 0, 2, Scatter).S(RuinedHouse, 0, 2, Scatter)
				.S(Rubble, 3, 6, Scatter).S(Bones, 0, 2, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.6f).Pad(0.5f).Held(PointOfInterestHolder.Ruined);
			// A dark keep: the curtain, its towers and the keep blackened by old fires, braziers burning in the ward, bones and graves, a
			// sorcerer's black spire over it all (now and then at medium size, always at large); its lord waits in the large one.
			T(a, POIType.Keep, "Dark Keep", Walled, Stone).Elsewhere(0f)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(WallTower, 2, 4, Towers)
				.S(Keep, 1, 1, Centre).S(DarkSpire, 0, 1, Edges).S(Brazier, 3, 6, Scatter).S(Bones, 3, 6, Scatter).S(Gravestone, 0, 4, Scatter)
				.S(Banner, 1, 3, Scatter).S(RuneStone, 0, 2, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 0.9f).Races("Undead", "Humanoid").Held(PointOfInterestHolder.Hostile, boss: true);
			// A dwarven hold: a stone curtain and squat towers round the keep, the forge and the stone houses of its smiths, rune stones
			// glowing in the ward, braziers.
			T(a, POIType.Keep, "Dwarven Hold", Walled, Stone).Elsewhere(0f)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 1, Gates).S(WallTower, 2, 4, Towers)
				.S(Keep, 1, 1, Centre).S(Smithy, 1, 1, Rows).S(StoneHouse, 1, 3, Rows).S(RuneStone, 2, 4, Scatter).S(Brazier, 1, 3, Scatter)
				.S(Storage, 3, 6, Scatter).S(Banner, 1, 3, Scatter).Races("Humanoid");
			// An elven hall: a white tower inside a low curtain, timber halls about it, banners, faerie lights, a fountain-well and a statue.
			T(a, POIType.Keep, "Elven Hall", Walled, Stone).Elsewhere(0f)
				.S(Wall, 1, 1, Perimeter).S(Gate, 1, 1, Gates)
				.S(ElvenSpire, 1, 1, Centre).S(Longhouse, 1, 2, Rows, style: Timber).S(Banner, 3, 6, Scatter).S(Glowjar, 2, 4, Scatter, style: Timber)
				.S(Well, 0, 1, Scatter).S(Statue, 0, 1, Scatter).S(Storage, 1, 3, Scatter, style: Timber)
				.Races("Humanoid", "Fey");

			// ── Towers ──
			// A dark tower: the black spire, braziers and bones round it, the broken ring of an old wall, scorched; its sorcerer waits
			// in the large one.
			T(a, POIType.Tower, "Dark Tower", Single, Stone).Elsewhere(0f)
				.S(DarkSpire, 1, 1, Centre).S(RuinedWall, 2, 4, Ring).S(Brazier, 2, 4, Scatter).S(Bones, 2, 5, Scatter).S(RuneStone, 0, 2, Scatter)
				.Weathered(PointOfInterestFinish.Charred, 0.8f).Pad(0.8f).Held(PointOfInterestHolder.Hostile, boss: true);
			// A watchtower fallen to its stump, its rubble about it, mossy: nobody keeps watch here any more.
			T(a, POIType.Tower, "Ruined Watchtower", Single, Stone).Elsewhere(0.3f)
				.S(CollapsedTower, 1, 1, Centre).S(Rubble, 1, 3, Scatter).S(RuinedWall, 0, 2, Scatter).S(Bones, 0, 1, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.6f).Pad(0.6f).Held(PointOfInterestHolder.Ruined);
			// The elves' white tower, its faerie lights and banners about it.
			T(a, POIType.Tower, "Elven Tower", Single, Stone).Elsewhere(0f)
				.S(ElvenSpire, 1, 1, Centre).S(Glowjar, 1, 3, Scatter, style: Timber).S(Banner, 1, 2, Scatter).S(Statue, 0, 1, Scatter).S(Well, 0, 1, Scatter);
			// A dwarven tower: a squat round stone tower, rune stones glowing round it, a brazier and stores.
			T(a, POIType.Tower, "Dwarven Tower", Single, Stone).Elsewhere(0f)
				.S(WallTower, 1, 1, Centre).S(RuneStone, 2, 4, Scatter).S(Brazier, 1, 2, Scatter).S(Storage, 1, 2, Scatter).S(Banner, 0, 1, Scatter);
		}
	}
}
#endif
