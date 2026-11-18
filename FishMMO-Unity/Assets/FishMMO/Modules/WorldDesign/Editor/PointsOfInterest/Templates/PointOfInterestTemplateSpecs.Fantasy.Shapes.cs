#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class PointOfInterestTemplateSpecs
	{
		/// <summary>The Shapes pack's own piece (StructurePieces.Fantasy.Shapes.cs) and the dwarven gate's doors (the kit's dungeon portal, scaled).</summary>
		private const string ShapesCloisterHall = "=HangingCloister#0", ShapesCloisterChapel = "=HangingCloister#1",
			ShapesSquareDoor = "=DungeonPortal#1", ShapesPointedDoor = "=DungeonPortal#0";

		/// <summary>
		/// The carved kinds that are ground, not structures (no Structure trait), and still lay a template's props on what their shaper
		/// cut (2026-10-27): a cenote's ledge altar, the bones round a sandworm's funnel.
		/// </summary>
		public static bool CarvedGround(POIType kind) => kind == POIType.Cenote || kind == POIType.SandwormPit;

		/// <summary>
		/// The fantasy wave (2026-10-27, poi-fantasy-wave/WAVE.md): the templates of the five carved kinds, whose ground their shapers cut
		/// (Shapers/FantasyShapers.cs; DungeonEntranceShaper for the dwarven gate).
		/// </summary>
		private static void FantasyShapes(List<PointOfInterestTemplateSpec> a)
		{
			// Cenote: the site stands on the well's ledge over its pool (CenoteShaper), so the template lays only what the ledge holds.
			T(a, POIType.Cenote, "Sacred Well", Single, Stone)
				.S(Altar, 1, 1, Centre, 0f, 0.2f, 0.8f, 0.9f).S(Brazier, 0, 1, Centre, 0f, 0.3f, 0.8f, 0.9f)
				.Weathered(PointOfInterestFinish.Mossy, 0.8f).Pad(0f);
			T(a, POIType.Cenote, "Serpent Well", Single, Stone)
				.S(Shrine, 1, 1, Centre, 0.1f, 0.4f).S(Bones, 0, 2, Scatter, 0.2f, 0.6f, 0.6f, 0.8f)
				.Weathered(PointOfInterestFinish.Mossy, 1f).Weighs(0.7f).Pad(0f);

			// Sandworm Pit: the funnel is the shaper's; bones of what it took ring the lip and slide down the sand.
			T(a, POIType.SandwormPit, "Maw", Round, Stone)
				.S(Bones, 6, 10, Ring, 0.2f, 0.6f, 1.2f, 2.2f).S(Bones, 2, 5, Scatter, 0.3f, 0.7f, 1.5f, 3f).S(Rubble, 0, 2, Scatter, 0.4f, 0.8f).Pad(0f);
			T(a, POIType.SandwormPit, "Swallowed Caravan", Scattered, Timber)
				.S(Bones, 8, 14, Scatter, 0.2f, 0.7f, 1.2f, 2.5f).S(Storage, 1, 3, Scatter, 0.5f, 0.9f).S(Banner, 0, 1, Scatter, 0.6f, 0.9f)
				.Weighs(0.6f).Pad(0f);

			// Dwarven Gate: the kit's dwarven portal laid half again its size, square to the face its shaper cut the frame for; the
			// dungeon entrance feature dresses the forecourt (braziers, a guardian pair, banners) at the door's scale.
			float gate = DungeonEntranceShaper.DwarvenGateScale;
			T(a, POIType.DwarvenGate, "Deep Gate", Single, Stone)
				.S(ShapesSquareDoor, 1, 1, Centre, 0f, 0f, gate, gate).In(1.2f, "Mountain Slope", "Alpine", "Rocky Terrain");
			T(a, POIType.DwarvenGate, "Rune Door", Single, Stone)
				.S(ShapesPointedDoor, 1, 1, Centre, 0f, 0.2f, gate, gate).Weathered(PointOfInterestFinish.Mossy, 0.6f).Weighs(0.7f).In(1.3f, "Karst", "Hills", "Scree");

			// Hanging Monastery: halls hung on the face (FaceSnapShaper sets the site where the face meets the deck), nothing strewn
			// on the slope under them.
			T(a, POIType.HangingMonastery, "Hanging Hall", Single, Timber)
				.S(ShapesCloisterHall, 1, 1, Centre).Weathered(PointOfInterestFinish.Mossy, 0.3f).Pad(0f);
			T(a, POIType.HangingMonastery, "Cliff Chapel", Single, Timber)
				.S(ShapesCloisterChapel, 1, 1, Centre).Weathered(PointOfInterestFinish.Mossy, 0.3f).Weighs(0.8f).In(1.3f, "Bamboo Forest", "Karst").Pad(0f);

			// Tidal Island Abbey: a stone church on the island's heart, its cells and walls about it; one long fallen to the sea's raiders.
			T(a, POIType.TidalIslandAbbey, "Holy Isle", Single, Stone)
				.S(Chapel, 1, 1, Centre).S(StoneHouse, 1, 3, Scatter).S(Wall, 2, 4, Scatter).S(Gravestone, 2, 5, Scatter, 0.1f, 0.4f).S(Well, 0, 1, Scatter)
				.Weathered(PointOfInterestFinish.Mossy, 0.4f).Pad(0.7f);
			T(a, POIType.TidalIslandAbbey, "Ruined Abbey", Single, Stone)
				.S(Chapel, 1, 1, Centre, 0.5f, 0.8f).S(RuinedWall, 2, 5, Scatter, 0.3f, 0.7f).S(Gravestone, 3, 6, Scatter, 0.3f, 0.8f).S(Rubble, 1, 3, Scatter)
				.Held(PointOfInterestHolder.Ruined).Weathered(PointOfInterestFinish.Mossy, 0.9f).Weighs(0.5f).Pad(0.7f);
		}
	}
}
#endif
