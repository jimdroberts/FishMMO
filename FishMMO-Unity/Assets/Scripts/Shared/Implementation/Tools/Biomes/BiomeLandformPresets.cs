using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// Every landform the biome rounds asked for, by name (2026-10-18, phase I2g): each a <see cref="BiomeLandform"/> on one shape
	/// family with the sizes, densities and paint its draft gave, so a round authors <c>.Landforms("Pingo", "Palsa")</c> and tunes
	/// only what its biome needs (<see cref="BiomeLandform.Painted"/>, <see cref="BiomeLandform.AtDensity"/>, <see cref="BiomeLandform.Capped"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Paint grounds follow the drafts' slots (0 the main layer, k detail layer k − 1): a preset painted "d4" in its draft paints
	/// ground 5. A biome whose slots differ re-paints it with <see cref="BiomeLandform.Painted"/>.
	/// </para>
	/// <para>
	/// Names are matched ignoring case, spaces, hyphens and a plural "s" (<see cref="TryGet"/>), and a few drafts' other names are
	/// aliases (Palaeolake, Hallig mound, Wierde, Priele, Erg).
	/// </para>
	/// </remarks>
	public static class BiomeLandformPresets
	{
		/// <summary>One preset: its name, the biomes whose drafts asked for it, and the landform.</summary>
		public readonly struct Preset
		{
			public readonly string Name;
			public readonly string AskedBy;
			public readonly BiomeLandform Form;

			public Preset(string name, string askedBy, BiomeLandform form)
			{
				Name = name;
				AskedBy = askedBy;
				Form = form;
			}
		}

		private static Vector2 V(float min, float max) => new Vector2(min, max);

		private const BiomeLandformPlacement Scatter = BiomeLandformPlacement.Scatter;
		private const BiomeLandformPlacement Fitted = BiomeLandformPlacement.Fitted;
		private const BiomeLandformPlacement Field = BiomeLandformPlacement.Field;

		private static BiomeLandform Make(string kind, BiomeLandformForm form, BiomeLandformPlacement placement = BiomeLandformPlacement.Scatter,
			float density = 0f, int perPatch = 0, int perScene = 0, Vector2 size = default, Vector2 length = default, Vector2 width = default,
			Vector2 height = default, float top = 0f, float flank = 0f, float rim = 0f, float crown = 0f, float crownDepth = 0f, float moat = 0f,
			float moatDepth = 0f, int count = 0, float roughness = 0f, float lean = 0f, BiomeLandformAnchor anchor = BiomeLandformAnchor.Anywhere,
			BiomeLandformAlign align = BiomeLandformAlign.Random, BiomeLandformDatum datum = BiomeLandformDatum.Ground, Vector2 datumDepth = default,
			Vector2 slope = default, Vector2 depth = default, BiomeAtmosphereRequirement air = BiomeAtmosphereRequirement.None, float nearWater = 0f,
			float fit = 0f, int paint = -1, BiomeLandformPart part = BiomeLandformPart.Whole, bool pad = false)
		{
			return new BiomeLandform
			{
				Kind = kind,
				Form = form,
				Placement = placement,
				Anchor = anchor,
				Align = align,
				Density = density,
				PerPatch = perPatch,
				PerScene = perScene,
				FitShare = fit,
				Size = size,
				Length = length,
				Width = width,
				Height = height,
				Top = top,
				Flank = flank,
				Rim = rim,
				Crown = crown,
				CrownDepth = crownDepth,
				Moat = moat,
				MoatDepth = moatDepth,
				Count = count,
				Roughness = roughness,
				Lean = lean,
				Datum = datum,
				DatumDepth = datumDepth,
				Slope = slope,
				Depth = depth,
				Air = air,
				NearWater = nearWater,
				PaintGround = paint,
				PaintPart = part,
				PaintStrength = paint >= 0 ? 1f : 0f,
				Pad = pad,
			};
		}

		/// <summary>Every preset, in the order the drafts asked for them.</summary>
		public static readonly Preset[] All =
		{
			// Tundra (decision 25): permafrost mounds and the slumps beside thaw lakes.
			new Preset("Pingo", "Tundra", Make("Pingo", BiomeLandformForm.Dome, density: 0.3f, perScene: 4, size: V(30f, 60f), height: V(8f, 15f),
				crown: 0.2f, crownDepth: 1.5f, roughness: 0.2f, slope: V(0f, 6f))),
			new Preset("Palsa", "Tundra", Make("Palsa", BiomeLandformForm.Oblong, density: 3f, size: V(8f, 14f), length: V(15f, 40f), height: V(1.5f, 3.5f),
				top: 0.3f, moat: 3f, moatDepth: 0.4f, roughness: 0.3f, slope: V(0f, 4f))),
			new Preset("ThawSlump", "Tundra", Make("ThawSlump", BiomeLandformForm.Slide, density: 0.4f, perScene: 6, size: V(30f, 80f), length: V(40f, 120f),
				height: V(3f, 8f), anchor: BiomeLandformAnchor.NearWater, nearWater: 40f, align: BiomeLandformAlign.Downslope, slope: V(3f, 20f))),

			// Alpine (decision 25 round): rock glaciers out of the cirques, cornices on the lee of crests.
			new Preset("RockGlacier", "Alpine", Make("RockGlacier", BiomeLandformForm.Tongue, density: 0.2f, perScene: 3, size: V(60f, 150f), length: V(150f, 500f),
				height: V(8f, 25f), roughness: 0.5f, align: BiomeLandformAlign.Downslope, slope: V(5f, 20f))),
			new Preset("Cornice", "Alpine", Make("Cornice", BiomeLandformForm.Tongue, density: 4f, perScene: 20, size: V(6f, 12f), length: V(15f, 30f),
				height: V(1f, 2.5f), align: BiomeLandformAlign.Wind, slope: V(18f, 35f))),

			// Mountain Slope: the flat-topped sheer ambas the monasteries stand on.
			new Preset("Amba", "Mountain Slope", Make("Amba", BiomeLandformForm.Table, density: 0.05f, perScene: 1, size: V(250f, 700f), height: V(80f, 200f),
				top: 0.85f, roughness: 0.3f, count: 24, slope: V(0f, 25f), pad: true)),

			// Crater (decision 39): the impact structure fitted to each painted patch; its rays are detail 0's mask.
			new Preset("ImpactCrater", "Crater", Make("ImpactCrater", BiomeLandformForm.Impact, Fitted, fit: 0.8f, size: V(200f, 6000f), height: V(0.18f, 0.18f),
				top: 0.3f, rim: 0.04f, crown: 0.15f, crownDepth: 0.08f, count: 9, anchor: BiomeLandformAnchor.Centroid, paint: 1, part: BiomeLandformPart.Rays)),

			// Abyssal Plain: parallel abyssal hills on one scene axis, and the starfall crater of its FallenStar.
			new Preset("AbyssalHills", "Abyssal Plain", Make("AbyssalHills", BiomeLandformForm.Straight, density: 0.3f, perScene: 3, length: V(800f, 3000f),
				width: V(300f, 800f), height: V(40f, 250f), lean: 0.6f, align: BiomeLandformAlign.SceneAxis)),
			new Preset("StarfallCrater", "Abyssal Plain", Make("StarfallCrater", BiomeLandformForm.Impact, density: 0.02f, perScene: 1, size: V(30f, 80f),
				height: V(0.2f, 0.2f), top: 0.25f, rim: 0.06f, count: 6, pad: true)),

			// Abyss (decision 36 e): serpentinite mud volcanoes and brine lakes with a mirror below their rim.
			new Preset("MudVolcano", "Abyss", Make("MudVolcano", BiomeLandformForm.Cone, density: 0.3f, perScene: 3, size: V(60f, 200f), height: V(5f, 25f),
				crown: 0.15f, crownDepth: 1.5f, roughness: 0.2f)),
			new Preset("BrineLake", "Abyss", Make("BrineLake", BiomeLandformForm.Pan, density: 0.3f, perScene: 2, size: V(20f, 120f), height: V(1f, 4f),
				top: 0.8f, rim: 0.5f, datumDepth: V(0.5f, 3f), pad: true)),

			// Deep Ocean (decision 42): the slope's fan valley, sediment-wave fields and a slide scar.
			new Preset("FanValley", "Deep Ocean", Make("FanValley", BiomeLandformForm.Meander, density: 0.1f, perScene: 2, length: V(2000f, 8000f),
				width: V(200f, 600f), height: V(20f, 80f), top: 0.6f, rim: 8f, anchor: BiomeLandformAnchor.Highest, paint: 2, part: BiomeLandformPart.Channel)),
			new Preset("SedimentWaves", "Deep Ocean", Make("SedimentWaves", BiomeLandformForm.SedimentWaves, Field, density: 1f, width: V(40f, 120f),
				height: V(2f, 8f), align: BiomeLandformAlign.Downslope, slope: V(0.3f, 8f))),
			new Preset("SlideScar", "Deep Ocean", Make("SlideScar", BiomeLandformForm.Slide, density: 0.05f, perScene: 1, size: V(300f, 800f), length: V(600f, 2000f),
				height: V(20f, 60f), align: BiomeLandformAlign.Downslope, slope: V(2f, 15f), pad: true)),

			// Coastal Water (decision 38): the drowned boulder causeway running out from the shore.
			new Preset("Sarn", "Coastal Water", Make("Sarn", BiomeLandformForm.Straight, density: 0.3f, perScene: 2, length: V(300f, 2000f), width: V(15f, 40f),
				height: V(3f, 8f), roughness: 0.3f, anchor: BiomeLandformAnchor.Shore, align: BiomeLandformAlign.Downslope,
				datum: BiomeLandformDatum.SeaSurface, datumDepth: V(0.5f, 4f), pad: true)),

			// Coral Reef (decision 40): faru ring reefs round their lagoons, a blue hole, kandu passes through the rings.
			new Preset("Faru", "Coral Reef", Make("Faru", BiomeLandformForm.Ring, density: 0.5f, perScene: 2, size: V(300f, 1500f), width: V(40f, 120f),
				height: V(10f, 30f), roughness: 0.3f, datum: BiomeLandformDatum.SeaSurface, datumDepth: V(0f, 1f), depth: V(5f, 80f))),
			new Preset("BlueHole", "Coral Reef", Make("BlueHole", BiomeLandformForm.Pit, density: 0.2f, perScene: 1, size: V(30f, 80f), height: V(40f, 100f),
				depth: V(0.5f, 25f))),
			new Preset("Kandu", "Coral Reef", Make("Kandu", BiomeLandformForm.Pass, count: 2, width: V(40f, 150f), height: V(10f, 30f))),

			// Cryovolcanic Plain (decision 41): Ahuna's dome and its facula streaks, Kubrick's moated massif, lobate flow steps.
			new Preset("Cryodome", "Cryovolcanic Plain", Make("Cryodome", BiomeLandformForm.Dome, density: 1f, perPatch: 2, size: V(120f, 400f), height: V(25f, 80f),
				top: 0.45f, roughness: 0.2f, count: 12, paint: 2, part: BiomeLandformPart.Streaks)),
			new Preset("MoatedMassif", "Cryovolcanic Plain", Make("MoatedMassif", BiomeLandformForm.Dome, density: 0.5f, perPatch: 1, size: V(60f, 200f),
				height: V(20f, 60f), top: 0.3f, moat: 30f, moatDepth: 14f, roughness: 0.4f)),
			new Preset("FlowLobeSteps", "Cryovolcanic Plain", Make("FlowLobeSteps", BiomeLandformForm.FlowSteps, Field, density: 1f, width: V(80f, 200f),
				height: V(1f, 3f), count: 4)),

			// Dust Sea (decision 44): dust filled to its spill level, ghost craters, kipukas, boulder tracks and the Stone River.
			new Preset("DustFill", "Dust Sea", Make("DustFill", BiomeLandformForm.SpillFill, Field, density: 1f, height: V(0.02f, 0.05f), paint: 1, part: BiomeLandformPart.Floor)),
			new Preset("GhostCrater", "Dust Sea", Make("GhostCrater", BiomeLandformForm.Ghost, density: 5f, size: V(30f, 300f), rim: 0.012f, top: 0.85f)),
			new Preset("Kipuka", "Dust Sea", Make("Kipuka", BiomeLandformForm.Table, density: 1f, perPatch: 4, size: V(20f, 80f), height: V(3f, 15f), top: 0.75f,
				pad: true)),
			new Preset("BoulderTracks", "Dust Sea", Make("BoulderTracks", BiomeLandformForm.Grooves, density: 50f, length: V(20f, 150f), width: V(0.3f, 1f),
				height: V(0.15f, 0.4f), slope: V(12f, 45f), pad: true)),
			new Preset("StoneRiver", "Dust Sea", Make("StoneRiver", BiomeLandformForm.StoneRiver, density: 0.2f, perScene: 1, length: V(500f, 4000f),
				width: V(6f, 15f), height: V(1f, 2f), top: 0.5f, roughness: 0.6f, anchor: BiomeLandformAnchor.Highest, pad: true)),

			// Desert (decision 43): the erg's dune mix (star, seif with gassi corridors, nebkha), the guelta and the palaeolake bed.
			new Preset("StarDune", "Desert", Make("StarDune", BiomeLandformForm.StarDune, Field, density: 0.3f, width: V(800f, 1500f), height: V(60f, 150f),
				count: 4, align: BiomeLandformAlign.Wind, paint: 0, part: BiomeLandformPart.Sand)),
			new Preset("Seif", "Desert", Make("Seif", BiomeLandformForm.Linear, Field, density: 0.5f, width: V(400f, 1200f), height: V(10f, 40f), roughness: 0.5f,
				align: BiomeLandformAlign.Wind, paint: 3, part: BiomeLandformPart.Floor)),
			new Preset("TransverseDunes", "Desert", Make("TransverseDunes", BiomeLandformForm.Transverse, Field, density: 0.2f, width: V(100f, 250f),
				height: V(5f, 15f), align: BiomeLandformAlign.Wind, paint: 0, part: BiomeLandformPart.Sand)),
			new Preset("Barchans", "Desert", Make("Barchans", BiomeLandformForm.Barchan, Field, density: 0.2f, width: V(150f, 400f), height: V(5f, 20f),
				align: BiomeLandformAlign.Wind, paint: 0, part: BiomeLandformPart.Sand)),
			new Preset("Nebkha", "Desert", Make("Nebkha", BiomeLandformForm.Dome, density: 40f, size: V(3f, 12f), height: V(0.5f, 3f), roughness: 0.2f,
				slope: V(0f, 8f), pad: true)),
			new Preset("Guelta", "Desert", Make("Guelta", BiomeLandformForm.Pit, density: 0.3f, perScene: 2, size: V(10f, 40f), height: V(3f, 8f), top: 0.6f,
				anchor: BiomeLandformAnchor.SlopeBreak, slope: V(15f, 60f), pad: true)),
			new Preset("Paleolake", "Desert", Make("Paleolake", BiomeLandformForm.Pan, density: 1f, perScene: 1, size: V(200f, 1500f), height: V(1f, 4f),
				top: 0.85f, anchor: BiomeLandformAnchor.Lowest, slope: V(0f, 4f), paint: 5, part: BiomeLandformPart.Floor)),

			// Beach: the foredune along the shore, wandering parabolic dunes and blowouts.
			new Preset("Foredune", "Beach", Make("Foredune", BiomeLandformForm.Contour, density: 1f, perScene: 2, length: V(300f, 1500f), width: V(20f, 40f),
				height: V(2f, 6f), lean: 0.3f, anchor: BiomeLandformAnchor.Shore, datumDepth: V(-3f, -1.5f))),
			new Preset("ParabolicDune", "Beach", Make("ParabolicDune", BiomeLandformForm.Parabolic, Field, density: 1f, width: V(150f, 300f), height: V(8f, 25f),
				align: BiomeLandformAlign.Wind, paint: 0, part: BiomeLandformPart.Sand)),
			new Preset("Blowout", "Beach", Make("Blowout", BiomeLandformForm.Hollow, density: 5f, size: V(15f, 50f), height: V(1f, 4f), rim: 1f)),

			// Badlands: rill nets on steep ground, caprock mesas, flat-floored washes, the crag a citadel stands on.
			new Preset("Rills", "Badlands", Make("Rills", BiomeLandformForm.Rills, Field, density: 1f, width: V(2f, 6f), height: V(1f, 4f), slope: V(18f, 70f),
				paint: 1, part: BiomeLandformPart.Channel)),
			new Preset("CaprockMesa", "Badlands", Make("CaprockMesa", BiomeLandformForm.Table, density: 0.4f, perScene: 4, size: V(60f, 400f), height: V(15f, 60f),
				top: 0.8f, rim: 2f, roughness: 0.4f, count: 24, paint: 5, part: BiomeLandformPart.Top, pad: true)),
			new Preset("Wash", "Badlands", Make("Wash", BiomeLandformForm.Meander, density: 0.3f, perScene: 2, length: V(600f, 2500f), width: V(20f, 80f),
				height: V(3f, 15f), top: 0.75f, anchor: BiomeLandformAnchor.Highest, paint: 2, part: BiomeLandformPart.Floor)),
			new Preset("CitadelCrag", "Badlands", Make("CitadelCrag", BiomeLandformForm.Table, density: 0.05f, perScene: 1, size: V(60f, 140f), height: V(15f, 40f),
				top: 0.7f, roughness: 0.3f, pad: true)),

			// Plains: buffalo wallows, playas, the mound towns' platform mounds and conical mounds.
			new Preset("Wallow", "Plains", Make("Wallow", BiomeLandformForm.Hollow, density: 2f, size: V(30f, 80f), width: V(4f, 15f), height: V(0.3f, 0.8f),
				count: 8, slope: V(0f, 4f), paint: 4, part: BiomeLandformPart.Whole)),
			new Preset("Playa", "Plains", Make("Playa", BiomeLandformForm.Pan, density: 0.3f, size: V(60f, 300f), height: V(1f, 2f), top: 0.85f,
				anchor: BiomeLandformAnchor.Lowest, slope: V(0f, 4f), paint: 5, part: BiomeLandformPart.Floor)),
			new Preset("PlatformMound", "Plains", Make("PlatformMound", BiomeLandformForm.Platform, density: 0.05f, perScene: 1, size: V(30f, 90f), height: V(4f, 25f),
				top: 0.55f, count: 3, slope: V(0f, 6f), pad: true)),
			new Preset("ConicalMound", "Plains", Make("ConicalMound", BiomeLandformForm.Dome, density: 0.3f, size: V(10f, 25f), height: V(2f, 5f), slope: V(0f, 8f))),

			// Castle: rabbit warrens, the siege lines' bank and ditch, the old motte.
			new Preset("PillowMound", "Castle", Make("PillowMound", BiomeLandformForm.Oblong, density: 1.5f, perScene: 6, size: V(5f, 8f), length: V(15f, 30f),
				height: V(0.6f, 0.9f), top: 0.5f, moat: 1.5f, moatDepth: 0.4f, slope: V(0f, 10f), paint: 5, part: BiomeLandformPart.Whole)),
			new Preset("SiegeDitch", "Castle", Make("SiegeDitch", BiomeLandformForm.Arc, density: 0.5f, perScene: 2, size: V(300f, 500f), length: V(60f, 120f),
				width: V(6f, 10f), height: V(1.5f, 2.5f), moat: 6f, moatDepth: 2f, slope: V(0f, 10f), paint: 5, part: BiomeLandformPart.Whole)),
			new Preset("Motte", "Castle", Make("Motte", BiomeLandformForm.Platform, density: 0.1f, perScene: 1, size: V(30f, 60f), height: V(6f, 10f), top: 0.4f,
				flank: 35f, moat: 8f, moatDepth: 2.5f, slope: V(0f, 8f), pad: true)),

			// Bamboo Forest: the keyhole kofun in its wet moat ring.
			new Preset("Kofun", "Bamboo Forest", Make("Kofun", BiomeLandformForm.Keyhole, density: 0.1f, perScene: 1, size: V(30f, 70f), length: V(40f, 120f),
				height: V(6f, 15f), top: 0.2f, moat: 9f, moatDepth: 2f, slope: V(0f, 8f), pad: true)),

			// Wetlands: tells over buried cities, dibin settlement islands.
			new Preset("Tell", "Wetlands", Make("Tell", BiomeLandformForm.Platform, density: 0.3f, size: V(60f, 300f), height: V(3f, 15f), top: 0.55f, flank: 12f,
				roughness: 0.3f, slope: V(0f, 6f), paint: 5, part: BiomeLandformPart.Whole, pad: true)),
			new Preset("Dibin", "Wetlands", Make("Dibin", BiomeLandformForm.Platform, density: 1f, size: V(20f, 60f), height: V(1f, 2f), top: 0.8f, flank: 45f,
				slope: V(0f, 4f), pad: true)),

			// Forest Ruins (decision 46): travertine terraces below a spring, ruin mounds (the tell's shape, its own paint).
			new Preset("TravertineTerraces", "Forest Ruins", Make("TravertineTerraces", BiomeLandformForm.Rimstone, density: 0.3f, perScene: 2, size: V(20f, 60f),
				length: V(20f, 120f), height: V(0.3f, 2f), rim: 0.3f, count: 6, anchor: BiomeLandformAnchor.NearWater, nearWater: 60f,
				align: BiomeLandformAlign.Downslope, slope: V(4f, 25f), pad: true)),
			new Preset("RuinMound", "Forest Ruins", Make("RuinMound", BiomeLandformForm.Platform, density: 0.3f, size: V(20f, 60f), height: V(2f, 6f), top: 0.4f,
				roughness: 0.4f, slope: V(0f, 10f), paint: 3, part: BiomeLandformPart.Whole, pad: true)),

			// Farmland: the step between alluvial terraces, sandy levee ridges, fontanile spring heads.
			new Preset("TerraceScarp", "Farmland", Make("TerraceScarp", BiomeLandformForm.Scarp, density: 0.3f, perScene: 2, size: V(150f, 300f),
				length: V(300f, 1500f), width: V(6f, 20f), height: V(3f, 10f), slope: V(0f, 6f), paint: 6, part: BiomeLandformPart.Flank)),
			new Preset("Dosso", "Farmland", Make("Dosso", BiomeLandformForm.Straight, density: 0.3f, perScene: 3, length: V(300f, 1200f), width: V(30f, 80f),
				height: V(1f, 2f), align: BiomeLandformAlign.Downslope, slope: V(0f, 4f))),
			new Preset("FontanileHead", "Farmland", Make("FontanileHead", BiomeLandformForm.Pan, density: 0.3f, size: V(20f, 40f), height: V(1.5f, 1.5f), top: 0.6f,
				slope: V(0f, 4f), pad: true)),

			// Valley: lateral moraines along the sides, alluvial fans at the side-gorge mouths.
			new Preset("LateralMoraine", "Valley, Glacier", Make("LateralMoraine", BiomeLandformForm.Contour, density: 0.3f, perScene: 2, length: V(300f, 1500f),
				width: V(15f, 40f), height: V(8f, 25f), lean: -0.4f, roughness: 0.2f, slope: V(8f, 30f))),
			new Preset("AlluvialFan", "Valley", Make("AlluvialFan", BiomeLandformForm.Fan, density: 0.2f, perScene: 3, size: V(150f, 400f), height: V(4f, 15f),
				anchor: BiomeLandformAnchor.SlopeBreak, align: BiomeLandformAlign.Downslope, slope: V(10f, 35f), pad: true)),

			// Estuary (decision 45): creek networks, terps and warfts, the sea dike, the pannes.
			new Preset("TidalCreeks", "Estuary", Make("TidalCreeks", BiomeLandformForm.Dendritic, density: 0.5f, perScene: 3, length: V(200f, 800f),
				width: V(2f, 30f), height: V(0.5f, 3f), count: 3, anchor: BiomeLandformAnchor.Shore, paint: 7, part: BiomeLandformPart.Flank)),
			new Preset("Terp", "Estuary", Make("Terp", BiomeLandformForm.Platform, density: 0.1f, size: V(120f, 400f), height: V(3f, 7f), top: 0.55f, flank: 10f,
				slope: V(0f, 4f), pad: true)),
			new Preset("Warft", "Estuary", Make("Warft", BiomeLandformForm.Platform, density: 0.3f, size: V(25f, 60f), height: V(2f, 4f), top: 0.5f,
				slope: V(0f, 4f), pad: true)),
			new Preset("SeaDike", "Estuary", Make("SeaDike", BiomeLandformForm.Contour, density: 1f, perScene: 1, length: V(500f, 3000f), width: V(25f, 35f),
				height: V(3f, 4.5f), lean: 0.33f, anchor: BiomeLandformAnchor.Shore, datumDepth: V(-1f, -0.5f))),
			new Preset("SaltPannes", "Estuary", Make("SaltPannes", BiomeLandformForm.Pan, density: 20f, size: V(5f, 30f), height: V(0.15f, 0.3f), top: 0.7f,
				slope: V(0f, 3f), pad: true)),

			// Grassland: the Cuman kunhalom mounds.
			new Preset("Kunhalom", "Grassland", Make("Kunhalom", BiomeLandformForm.Dome, density: 0.1f, size: V(20f, 40f), height: V(2f, 6f), slope: V(0f, 6f), pad: true)),

			// Fortress (decision 48): the sel mudflow fan at a tributary's mouth.
			new Preset("MudflowFan", "Fortress", Make("MudflowFan", BiomeLandformForm.Fan, density: 0.5f, perScene: 3, size: V(60f, 200f), height: V(2f, 8f),
				rim: 0.8f, roughness: 0.5f, anchor: BiomeLandformAnchor.SlopeBreak, align: BiomeLandformAlign.Downslope, slope: V(8f, 35f), paint: 4,
				part: BiomeLandformPart.Whole)),

			// Glacier (decision 49): crevasse fields, moulins fed by supraglacial channels, medial and terminal moraines with kettle ponds,
			// ogive bands, and the snout portal the river is born from.
			new Preset("CrevasseField", "Glacier", Make("CrevasseField", BiomeLandformForm.Crevasses, Field, density: 1f, width: V(12f, 30f), height: V(6f, 15f),
				top: 0.15f, align: BiomeLandformAlign.Downslope, slope: V(8f, 40f), paint: 2, part: BiomeLandformPart.Floor)),
			new Preset("Moulin", "Glacier", Make("Moulin", BiomeLandformForm.Pit, density: 0.5f, perScene: 4, size: V(3f, 6f), height: V(8f, 20f), slope: V(0f, 10f),
				pad: true)),
			new Preset("SupraglacialChannels", "Glacier", Make("SupraglacialChannels", BiomeLandformForm.Meander, density: 1f, perScene: 6, length: V(150f, 600f),
				width: V(0.5f, 1f), height: V(0.4f, 1f), slope: V(1f, 12f))),
			new Preset("MedialMoraine", "Glacier", Make("MedialMoraine", BiomeLandformForm.Straight, density: 0.5f, perScene: 3, length: V(400f, 2000f),
				width: V(8f, 20f), height: V(1f, 3f), roughness: 0.3f, align: BiomeLandformAlign.Downslope, paint: 4, part: BiomeLandformPart.Whole)),
			new Preset("TerminalMoraine", "Glacier", Make("TerminalMoraine", BiomeLandformForm.Arc, density: 1f, perScene: 2, size: V(300f, 800f), length: V(200f, 600f),
				width: V(15f, 40f), height: V(5f, 20f), roughness: 0.2f, anchor: BiomeLandformAnchor.Lowest, paint: 4, part: BiomeLandformPart.Whole)),
			new Preset("KettlePonds", "Glacier", Make("KettlePonds", BiomeLandformForm.Hollow, density: 1f, perScene: 4, size: V(60f, 160f), width: V(8f, 30f),
				height: V(1f, 4f), count: 5, anchor: BiomeLandformAnchor.Lowest, pad: true)),
			new Preset("Ogives", "Glacier", Make("Ogives", BiomeLandformForm.SedimentWaves, Field, density: 1f, width: V(60f, 120f), height: V(0.2f, 0.5f),
				align: BiomeLandformAlign.Downslope, slope: V(2f, 12f), paint: 1, part: BiomeLandformPart.Crest)),
			new Preset("SnoutPortal", "Glacier", Make("SnoutPortal", BiomeLandformForm.Pit, density: 1f, perScene: 1, size: V(12f, 25f), height: V(4f, 8f), top: 0.5f,
				anchor: BiomeLandformAnchor.Lowest, pad: true)),

			// High Desert (decision 50): tinajas, fin fields, a volcanic neck, arroyos.
			new Preset("Tinaja", "High Desert", Make("Tinaja", BiomeLandformForm.Pit, density: 15f, perScene: 60, size: V(0.5f, 6f), height: V(0.3f, 1.5f), top: 0.5f,
				slope: V(0f, 12f), pad: true)),
			new Preset("FinField", "High Desert", Make("FinField", BiomeLandformForm.Fins, Field, density: 1f, width: V(15f, 40f), height: V(8f, 25f),
				align: BiomeLandformAlign.SceneAxis)),
			new Preset("VolcanicNeck", "High Desert", Make("VolcanicNeck", BiomeLandformForm.Star, density: 0.05f, perScene: 1, size: V(80f, 300f), height: V(60f, 150f),
				count: 3, roughness: 0.4f)),
			new Preset("Arroyo", "High Desert", Make("Arroyo", BiomeLandformForm.Meander, density: 0.3f, perScene: 2, length: V(500f, 2500f), width: V(8f, 30f),
				height: V(2f, 6f), top: 0.85f, anchor: BiomeLandformAnchor.Highest, paint: 2, part: BiomeLandformPart.Floor)),

			// Geyser Basin (I2g's share; pool basins, runoff and sinter at the vents are I2q's): blast craters and altered knolls.
			new Preset("ExplosionCrater", "Geyser Basin", Make("ExplosionCrater", BiomeLandformForm.Pit, density: 0.3f, perScene: 2, size: V(20f, 80f),
				height: V(6f, 20f), top: 0.4f, rim: 1f)),
			new Preset("PaintedHills", "Geyser Basin", Make("PaintedHills", BiomeLandformForm.Dome, density: 2f, size: V(40f, 150f), height: V(8f, 30f),
				roughness: 0.4f, paint: 3, part: BiomeLandformPart.Whole)),

			// Generic shapes a round may start from.
			new Preset("Mesa", "(generic)", Make("Mesa", BiomeLandformForm.Table, density: 0.2f, size: V(100f, 600f), height: V(20f, 120f), top: 0.8f, rim: 1f,
				pad: true)),
			new Preset("Mound", "(generic)", Make("Mound", BiomeLandformForm.Dome, density: 0.5f, size: V(10f, 40f), height: V(1f, 5f))),
		};

		/// <summary>Other names the drafts used for a preset (normalised key → preset name).</summary>
		private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
		{
			{ "palaeolake", "Paleolake" },
			{ "halligmound", "Warft" },
			{ "hallig", "Warft" },
			{ "wierde", "Terp" },
			{ "priele", "TidalCreeks" },
			{ "tidalcreek", "TidalCreeks" },
			{ "erg", "TransverseDunes" },
			{ "barchan", "Barchans" },
			{ "lineardune", "Seif" },
			{ "impactstructure", "ImpactCrater" },
			{ "kettlepond", "KettlePonds" },
			{ "kettle", "KettlePonds" },
			{ "seawall", "SeaDike" },
			{ "seadyke", "SeaDike" },
			{ "pillowmounds", "PillowMound" },
			{ "rimstone", "TravertineTerraces" },
			{ "sel", "MudflowFan" },
			{ "glaciermoraine", "LateralMoraine" },
		};

		private static Dictionary<string, int> index;

		private static string Key(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return string.Empty;
			}
			var chars = new List<char>(name.Length);
			foreach (char c in name)
			{
				if (char.IsLetterOrDigit(c))
				{
					chars.Add(char.ToLowerInvariant(c));
				}
			}
			return new string(chars.ToArray());
		}

		/// <summary>The preset a name means (case, spaces, hyphens and a plural "s" ignored; the drafts' other names too); false for none.</summary>
		public static bool TryGet(string name, out BiomeLandform form)
		{
			if (index == null)
			{
				var built = new Dictionary<string, int>();
				for (int i = 0; i < All.Length; i++)
				{
					built[Key(All[i].Name)] = i;
				}
				index = built;
			}
			string key = Key(name);
			foreach (string candidate in new[] { key, key.EndsWith("es") ? key.Substring(0, key.Length - 2) : null, key.EndsWith("s") ? key.Substring(0, key.Length - 1) : null, key + "s" })
			{
				if (candidate == null)
				{
					continue;
				}
				if (Aliases.TryGetValue(candidate, out string aliased))
				{
					form = All[index[Key(aliased)]].Form;
					return true;
				}
				if (index.TryGetValue(candidate, out int at))
				{
					form = All[at].Form;
					return true;
				}
			}
			form = default;
			return false;
		}

		/// <summary>
		/// The preset a name means, or — for a name no preset answers — an entry of that name that carves nothing (the authoring then
		/// reports it as unknown rather than a spec file failing to load).
		/// </summary>
		public static BiomeLandform Named(string name)
		{
			if (TryGet(name, out BiomeLandform form))
			{
				return form;
			}
			return new BiomeLandform { Kind = name, Form = BiomeLandformForm.Dome, Placement = BiomeLandformPlacement.Scatter, Density = 0f, PaintGround = -1 };
		}

		/// <summary>True when a name answers to a preset.</summary>
		public static bool Knows(string name) => TryGet(name, out _);
	}
}
