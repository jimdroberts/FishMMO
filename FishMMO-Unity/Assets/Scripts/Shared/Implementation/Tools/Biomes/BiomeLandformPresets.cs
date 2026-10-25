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
			float fit = 0f, int paint = -1, BiomeLandformPart part = BiomeLandformPart.Whole, bool pad = false, float moatBias = 0f)
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
				MoatBias = moatBias,
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

			// ── 2026-10-25, phase I2g-b (decisions 52, 54–66): every draft written after the first landform phase ──

			// Karst (decision 61): tower karst and cone karst on two forms of their own, the dolines, uvalas, poljes, tiankengs and ponors.
			new Preset("Fenglin", "Karst", Make("Fenglin", BiomeLandformForm.Tower, density: 4f, size: V(60f, 250f), height: V(60f, 220f), top: 0.3f, flank: 80f,
				count: 14, roughness: 0.45f, lean: 0.04f, moat: 3f, moatDepth: 1f, slope: V(0f, 6f), paint: 4, part: BiomeLandformPart.Top, pad: true)),
			new Preset("Fengcong", "Karst", Make("Fengcong", BiomeLandformForm.Cockpits, Field, density: 1f, width: V(180f, 400f), height: V(60f, 180f), flank: 45f,
				roughness: 0.5f, slope: V(6f, 90f), paint: 2, part: BiomeLandformPart.Floor)),
			new Preset("Doline", "Karst", Make("Doline", BiomeLandformForm.Hollow, density: 25f, size: V(15f, 70f), height: V(3f, 15f), slope: V(0f, 20f),
				paint: 2, part: BiomeLandformPart.Floor)),
			new Preset("Uvala", "Karst", Make("Uvala", BiomeLandformForm.Hollow, density: 0.4f, perScene: 3, size: V(150f, 500f), width: V(60f, 180f),
				height: V(10f, 30f), count: 4, paint: 2, part: BiomeLandformPart.Floor)),
			new Preset("Polje", "Karst", Make("Polje", BiomeLandformForm.Pan, Fitted, fit: 0.35f, size: V(600f, 3000f), height: V(25f, 80f), top: 0.85f, flank: 30f,
				anchor: BiomeLandformAnchor.Lowest, paint: 3, part: BiomeLandformPart.Floor, pad: true)),
			new Preset("Tiankeng", "Karst", Make("Tiankeng", BiomeLandformForm.Pit, density: 0.05f, perScene: 1, size: V(120f, 320f), height: V(100f, 250f), top: 0.4f,
				slope: V(0f, 25f), paint: 4, part: BiomeLandformPart.Floor, pad: true)),
			new Preset("Ponor", "Karst", Make("Ponor", BiomeLandformForm.Pit, density: 0.5f, perScene: 4, size: V(5f, 15f), height: V(6f, 18f),
				anchor: BiomeLandformAnchor.NearWater, nearWater: 30f)),

			// Jungle Ruins (decision 56): the forested temple mound on the stepped pyramid, cenotes, the bajo.
			new Preset("TempleMound", "Jungle Ruins", Make("TempleMound", BiomeLandformForm.Pyramid, density: 0.3f, perScene: 4, size: V(40f, 120f), width: V(4f, 8f),
				height: V(15f, 45f), top: 0.25f, count: 9, rim: 1f, roughness: 0.35f, slope: V(0f, 10f), pad: true)),
			new Preset("Cenote", "Jungle Ruins", Make("Cenote", BiomeLandformForm.Pit, density: 0.3f, perScene: 3, size: V(20f, 60f), height: V(15f, 45f),
				slope: V(0f, 8f), pad: true)),
			new Preset("Bajo", "Jungle Ruins", Make("Bajo", BiomeLandformForm.Pan, density: 0.2f, perScene: 1, size: V(300f, 1500f), height: V(1f, 3f), top: 0.85f,
				anchor: BiomeLandformAnchor.Lowest, slope: V(0f, 4f), paint: 3, part: BiomeLandformPart.Floor)),

			// Jungle Temple (decision 59): the exposed temple mountain, the moated enclosure, the baray.
			new Preset("TempleMountain", "Jungle Temple", Make("TempleMountain", BiomeLandformForm.Pyramid, density: 0.1f, perScene: 2, size: V(40f, 150f),
				width: V(4f, 8f), height: V(8f, 25f), top: 0.35f, count: 4, rim: 4f, slope: V(0f, 6f), align: BiomeLandformAlign.Cardinal, pad: true)),
			new Preset("MoatedEnclosure", "Jungle Temple", Make("MoatedEnclosure", BiomeLandformForm.Pyramid, density: 0.1f, perScene: 3, size: V(100f, 400f),
				length: V(120f, 500f), width: V(8f, 14f), height: V(0.5f, 1.5f), top: 0.9f, count: 1, rim: 1f, moat: 40f, moatDepth: 3f, slope: V(0f, 5f),
				align: BiomeLandformAlign.Cardinal, pad: true)),
			new Preset("Baray", "Jungle Temple", Make("Baray", BiomeLandformForm.Tank, density: 0.05f, perScene: 1, size: V(75f, 300f), length: V(300f, 1200f),
				height: V(1f, 3f), rim: 6f, crown: 0.1f, crownDepth: 3f, anchor: BiomeLandformAnchor.Lowest, align: BiomeLandformAlign.Cardinal, slope: V(0f, 3f), pad: true)),

			// Jungle (decision 52): granite inselbergs, laterite breakaways, the iya earthwork round a town (a ring and its ditch until I7 sites it).
			new Preset("Inselberg", "Jungle", Make("Inselberg", BiomeLandformForm.Tower, density: 0.05f, perScene: 2, size: V(150f, 600f), height: V(60f, 250f),
				top: 0.8f, flank: 65f, roughness: 0.2f, slope: V(0f, 15f), pad: true)),
			new Preset("LateriteBreakaway", "Jungle", Make("LateriteBreakaway", BiomeLandformForm.Scarp, density: 0.3f, perScene: 3, size: V(80f, 200f),
				length: V(300f, 1500f), width: V(6f, 15f), height: V(3f, 10f), slope: V(1f, 12f))),
			new Preset("Iya", "Jungle", Make("Iya", BiomeLandformForm.Ring, density: 0.05f, perScene: 1, size: V(400f, 1200f), width: V(8f, 14f), height: V(2f, 4f),
				moat: 10f, moatDepth: 6f, slope: V(0f, 8f), pad: true)),

			// Mangrove (decision 60): its own creeks (the estuary's re-tuned), mud islands crowned in the tide, the ukanda pan, the raised
			// reef's terrace, rag dolines (named apart from Karst's Doline, which "Dolines" means).
			new Preset("MangroveCreeks", "Mangrove (TidalCreeks re-tuned)", Make("MangroveCreeks", BiomeLandformForm.Dendritic, density: 0.5f, perScene: 3,
				length: V(150f, 600f), width: V(2f, 15f), height: V(0.5f, 2.5f), rim: 0.3f, count: 4, roughness: 0.3f, anchor: BiomeLandformAnchor.Shore,
				paint: 7, part: BiomeLandformPart.Flank)),
			new Preset("MudIslands", "Mangrove", Make("MudIslands", BiomeLandformForm.Platform, density: 2f, size: V(30f, 80f), length: V(40f, 200f), height: V(0.5f, 1.5f),
				top: 0.6f, datum: BiomeLandformDatum.SeaSurface, datumDepth: V(0.4f, 1.2f), depth: V(0.2f, 3f), slope: V(0f, 4f))),
			new Preset("Ukanda", "Mangrove", Make("Ukanda", BiomeLandformForm.Pan, density: 1f, perScene: 3, size: V(80f, 400f), height: V(0.3f, 1f), top: 0.85f,
				datum: BiomeLandformDatum.SeaSurface, datumDepth: V(-1.5f, -0.9f), slope: V(0f, 3f), paint: 4, part: BiomeLandformPart.Floor)),
			new Preset("RagTerrace", "Mangrove", Make("RagTerrace", BiomeLandformForm.Scarp, density: 0.3f, perScene: 2, size: V(40f, 120f), length: V(300f, 1500f),
				width: V(4f, 10f), height: V(3f, 8f), anchor: BiomeLandformAnchor.Shore, datumDepth: V(-8f, -3f), paint: 5, part: BiomeLandformPart.Top)),
			new Preset("RagDolines", "Mangrove", Make("RagDolines", BiomeLandformForm.Hollow, density: 2f, size: V(30f, 80f), width: V(3f, 10f), height: V(1f, 4f),
				top: 0.5f, count: 6, slope: V(0f, 15f))),

			// Lava Tube (decision 57): the skylight chain, a collapse trench, lava rises with their cleft, hornitos.
			new Preset("SkylightChain", "Lava Tube", Make("SkylightChain", BiomeLandformForm.PitChain, density: 0.2f, perScene: 1, length: V(600f, 3000f),
				width: V(15f, 120f), height: V(10f, 60f), top: 0.7f, anchor: BiomeLandformAnchor.Highest, pad: true)),
			new Preset("CollapseTrench", "Lava Tube", Make("CollapseTrench", BiomeLandformForm.Meander, density: 0.2f, perScene: 2, length: V(400f, 2000f),
				width: V(30f, 80f), height: V(8f, 25f), top: 0.8f, anchor: BiomeLandformAnchor.Highest, pad: true)),
			new Preset("LavaRise", "Lava Tube", Make("LavaRise", BiomeLandformForm.Table, density: 3f, size: V(20f, 80f), length: V(30f, 150f), height: V(2f, 8f),
				top: 0.8f, crown: 0.08f, crownDepth: 2f, slope: V(0f, 6f), paint: 2, part: BiomeLandformPart.Top)),
			new Preset("Hornito", "Lava Tube", Make("Hornito", BiomeLandformForm.Cone, density: 10f, perScene: 30, size: V(2f, 6f), height: V(1f, 5f), crown: 0.3f,
				crownDepth: 0.8f, slope: V(0f, 8f))),

			// Ice Ruin (decision 58): the winter wall's stagnant ice front, drift mounds over the buried streets (with air only).
			new Preset("WinterWall", "Ice Ruin", Make("WinterWall", BiomeLandformForm.Tower, density: 0.5f, perScene: 1, size: V(150f, 400f), height: V(8f, 25f),
				top: 0.9f, flank: 70f, anchor: BiomeLandformAnchor.Highest, slope: V(0f, 15f), pad: true)),
			new Preset("DriftMound", "Ice Ruin", Make("DriftMound", BiomeLandformForm.Platform, density: 15f, size: V(8f, 20f), length: V(10f, 30f), height: V(0.5f, 2f),
				top: 0.5f, roughness: 0.2f, align: BiomeLandformAlign.SceneAxis, slope: V(0f, 8f),
				air: BiomeAtmosphereRequirement.Standard | BiomeAtmosphereRequirement.Thick)),

			// Ice Palace (decision 54): the summit caldera holding its frozen lake.
			new Preset("SummitCaldera", "Ice Palace", Make("SummitCaldera", BiomeLandformForm.Caldera, density: 1f, perScene: 1, size: V(400f, 1500f),
				height: V(0.12f, 0.16f), top: 0.55f, rim: 0.04f, count: 9, anchor: BiomeLandformAnchor.Highest, pad: true)),

			// Impact Basin (decision 55): the multi-ring basin fitted to every patch at least 620 m across (300 000 m²).
			new Preset("MultiRingBasin", "Impact Basin", Make("MultiRingBasin", BiomeLandformForm.MultiRing, Fitted, fit: 0.9f, size: V(620f, 40000f),
				height: V(0.015f, 0.015f), rim: 0.02f, anchor: BiomeLandformAnchor.Lowest, pad: true)),

			// Methane Lake (decision 62): rampart pits, the labyrinth, a ria flooded from the shore, the Magnetberg.
			new Preset("RampartPit", "Methane Lake", Make("RampartPit", BiomeLandformForm.Pan, density: 1f, perPatch: 3, size: V(100f, 500f), height: V(20f, 120f),
				top: 0.55f, rim: 15f, slope: V(0f, 10f), pad: true)),
			new Preset("Labyrinth", "Methane Lake", Make("Labyrinth", BiomeLandformForm.Labyrinth, Field, density: 1f, width: V(40f, 200f), height: V(15f, 60f))),
			new Preset("Ria", "Methane Lake", Make("Ria", BiomeLandformForm.Dendritic, density: 0.2f, perScene: 1, length: V(1000f, 4000f), width: V(40f, 150f),
				height: V(20f, 60f), top: 0.5f, count: 3, anchor: BiomeLandformAnchor.Shore)),
			new Preset("Magnetberg", "Methane Lake", Make("Magnetberg", BiomeLandformForm.Tower, density: 0.05f, perScene: 1, size: V(60f, 200f), height: V(40f, 120f),
				top: 0.15f, flank: 60f, count: 7, roughness: 0.8f, anchor: BiomeLandformAnchor.Highest, pad: true)),

			// Ice Sheet (decision 64): nunataks with their wind scoop, a blue-ice tail in each one's lee (the entry after it), megadunes
			// with their lee faces, and on thin air the spiral troughs and Swiss-cheese pits.
			new Preset("Nunatak", "Ice Sheet", Make("Nunatak", BiomeLandformForm.Cone, density: 0.5f, perPatch: 4, size: V(80f, 300f), height: V(25f, 140f),
				roughness: 0.6f, moat: 45f, moatDepth: 12f, moatBias: 0.8f, align: BiomeLandformAlign.Wind, paint: 3, part: BiomeLandformPart.Top, pad: true)),
			new Preset("BlueIceTail", "Ice Sheet", Make("BlueIceTail", BiomeLandformForm.Tongue, size: V(120f, 300f), length: V(300f, 900f), height: V(-2f, -0.5f),
				anchor: BiomeLandformAnchor.LeeOf, align: BiomeLandformAlign.Wind, paint: 2, part: BiomeLandformPart.Whole)),
			new Preset("Megadunes", "Ice Sheet", Make("Megadunes", BiomeLandformForm.SedimentWaves, Field, density: 1f, width: V(1500f, 3000f), height: V(2f, 6f),
				align: BiomeLandformAlign.Wind, paint: 1, part: BiomeLandformPart.Lee)),
			new Preset("SpiralTroughs", "Ice Sheet", Make("SpiralTroughs", BiomeLandformForm.Spiral, density: 0.05f, perScene: 1, size: V(4000f, 12000f),
				width: V(300f, 800f), height: V(20f, 60f), count: 4, lean: 0.6f, anchor: BiomeLandformAnchor.Highest, air: BiomeAtmosphereRequirement.Thin,
				paint: 7, part: BiomeLandformPart.Flank)),
			new Preset("SwissCheese", "Ice Sheet", Make("SwissCheese", BiomeLandformForm.Hollow, density: 2f, size: V(150f, 400f), width: V(15f, 80f), height: V(2f, 6f),
				top: 0.7f, count: 16, air: BiomeAtmosphereRequirement.Thin, paint: 1, part: BiomeLandformPart.Floor)),

			// Molten Surface (decision 65; lava-relative datums are phase I2r's, these stand on the ground): paterae, overflow benches,
			// crust islands, spatter ramparts, lava channels, the plume's paint-only ring, pressure ridges.
			new Preset("Patera", "Molten Surface", Make("Patera", BiomeLandformForm.Pan, density: 0.3f, perScene: 2, size: V(400f, 1500f), height: V(30f, 120f),
				top: 0.85f, rim: 4f, anchor: BiomeLandformAnchor.Lowest, paint: 8, part: BiomeLandformPart.Floor, pad: true)),
			new Preset("OverflowBench", "Molten Surface", Make("OverflowBench", BiomeLandformForm.Contour, density: 0.5f, perScene: 3, length: V(300f, 1500f),
				width: V(10f, 30f), height: V(2f, 12f), top: 0.6f, lean: -0.5f, paint: 2, part: BiomeLandformPart.Crest)),
			new Preset("CrustIsland", "Molten Surface", Make("CrustIsland", BiomeLandformForm.Table, density: 0.3f, perScene: 3, size: V(40f, 200f), height: V(5f, 15f),
				top: 0.8f, pad: true)),
			new Preset("SpatterRampart", "Molten Surface", Make("SpatterRampart", BiomeLandformForm.Straight, density: 0.3f, perScene: 3, length: V(150f, 600f),
				width: V(6f, 16f), height: V(2f, 8f), roughness: 0.6f)),
			new Preset("LavaChannel", "Molten Surface", Make("LavaChannel", BiomeLandformForm.Meander, density: 0.3f, perScene: 2, length: V(500f, 3000f),
				width: V(8f, 25f), height: V(2f, 6f), top: 0.7f, rim: 3.5f, anchor: BiomeLandformAnchor.Highest)),
			new Preset("PlumeRing", "Molten Surface", Make("PlumeRing", BiomeLandformForm.Ring, density: 0.1f, perScene: 1, size: V(800f, 1800f), width: V(120f, 270f),
				air: BiomeAtmosphereRequirement.Airless | BiomeAtmosphereRequirement.Thin, paint: 3, part: BiomeLandformPart.Whole)),
			new Preset("PressureRidges", "Molten Surface", Make("PressureRidges", BiomeLandformForm.SedimentWaves, Field, density: 1f, width: V(10f, 25f),
				height: V(0.5f, 2f))),

			// Nitrogen Ice Field (decision 66): Sputnik's convection cells (their troughs, interiors and junctions for the zone bands),
			// bladed ridges striking north, nitrogen glaciers, the block montes, the pit fields.
			new Preset("ConvectionCells", "Nitrogen Ice Field", Make("ConvectionCells", BiomeLandformForm.Cells, Field, density: 1f, width: V(250f, 700f),
				height: V(5f, 15f), moat: 28f, moatDepth: 8f)),
			new Preset("BladedRidges", "Nitrogen Ice Field", Make("BladedRidges", BiomeLandformForm.Fins, Field, density: 1f, width: V(40f, 90f), height: V(15f, 40f),
				roughness: 0.5f, align: BiomeLandformAlign.Meridian)),
			new Preset("NitrogenGlacier", "Nitrogen Ice Field", Make("NitrogenGlacier", BiomeLandformForm.Tongue, density: 0.3f, perPatch: 3, size: V(60f, 200f),
				length: V(600f, 3000f), height: V(5f, 20f), roughness: 0.3f, anchor: BiomeLandformAnchor.Highest, align: BiomeLandformAlign.Downslope)),
			new Preset("BlockMontes", "Nitrogen Ice Field", Make("BlockMontes", BiomeLandformForm.Tower, density: 0.3f, perPatch: 1, size: V(300f, 1500f),
				height: V(40f, 150f), top: 0.3f, flank: 50f, count: 9, roughness: 0.9f)),
			new Preset("PitField", "Nitrogen Ice Field", Make("PitField", BiomeLandformForm.Hollow, density: 1f, size: V(200f, 600f), width: V(6f, 25f), height: V(1f, 3f),
				top: 0.5f, count: 24)),

			// Oasis: spring mounds with their summit pool, the birket salt lake (its brine look is the lake's, a later water phase).
			new Preset("SpringMound", "Oasis", Make("SpringMound", BiomeLandformForm.Dome, density: 0.6f, perScene: 8, size: V(20f, 60f), height: V(3f, 10f),
				crown: 0.17f, crownDepth: 1.2f, anchor: BiomeLandformAnchor.Lowest, paint: 6, part: BiomeLandformPart.Whole, pad: true)),
			new Preset("Birket", "Oasis", Make("Birket", BiomeLandformForm.Pan, density: 0.3f, perScene: 2, size: V(300f, 1500f), height: V(0.5f, 2f), top: 0.85f,
				anchor: BiomeLandformAnchor.Lowest, slope: V(0f, 3f), paint: 2, part: BiomeLandformPart.Rim, pad: true)),

			// Ice Shelf (decision 68; the floating reach itself is phase I2s): rolls, rifts and leads opened to the sea below its surface
			// (the floor datum), polynyas, pressure belts, ice rumples, tide cracks, an epishelf lake, a stranded ice island, Hafgerðingar.
			new Preset("ShelfRolls", "Ice Shelf", Make("ShelfRolls", BiomeLandformForm.SedimentWaves, Field, density: 1f, width: V(150f, 300f), height: V(0.8f, 2f),
				align: BiomeLandformAlign.Wind, paint: 1, part: BiomeLandformPart.Whole)),
			new Preset("Rift", "Ice Shelf", Make("Rift", BiomeLandformForm.Grooves, density: 1f, perPatch: 3, length: V(500f, 3000f), width: V(20f, 150f),
				height: V(6f, 20f), top: 0.9f, datum: BiomeLandformDatum.SeaSurface, datumDepth: V(6f, 6f), pad: true)),
			new Preset("Leads", "Ice Shelf", Make("Leads", BiomeLandformForm.Crevasses, Field, density: 1f, width: V(200f, 800f), height: V(1f, 3f), top: 0.05f,
				paint: 3, part: BiomeLandformPart.Floor)),
			new Preset("Polynya", "Ice Shelf", Make("Polynya", BiomeLandformForm.Pan, density: 0.5f, perPatch: 2, size: V(80f, 400f), height: V(8f, 25f), top: 0.8f,
				datum: BiomeLandformDatum.SeaSurface, datumDepth: V(8f, 25f), anchor: BiomeLandformAnchor.Shore)),
			new Preset("PressureBelt", "Ice Shelf", Make("PressureBelt", BiomeLandformForm.Contour, density: 1f, perScene: 4, length: V(300f, 2000f),
				width: V(6f, 20f), height: V(2f, 6f), lean: 0.4f, roughness: 0.6f, paint: 4, part: BiomeLandformPart.Whole)),
			new Preset("IceRumple", "Ice Shelf", Make("IceRumple", BiomeLandformForm.Oblong, density: 0.3f, size: V(60f, 200f), length: V(200f, 600f),
				height: V(5f, 15f), top: 0.2f, paint: 4, part: BiomeLandformPart.Whole)),
			new Preset("TideCracks", "Ice Shelf", Make("TideCracks", BiomeLandformForm.Contour, density: 1f, perScene: 3, length: V(300f, 2000f), width: V(1f, 3f),
				height: V(-2f, -1f), top: 0.4f)),
			new Preset("EpishelfLake", "Ice Shelf", Make("EpishelfLake", BiomeLandformForm.Pan, density: 0.3f, perScene: 2, size: V(300f, 1200f), height: V(1f, 3f),
				top: 0.85f, datum: BiomeLandformDatum.SeaSurface, datumDepth: V(-1f, -1f), air: BiomeAtmosphereRequirement.Standard | BiomeAtmosphereRequirement.Thick,
				paint: 1, part: BiomeLandformPart.Floor)),
			new Preset("IceIsland", "Ice Shelf", Make("IceIsland", BiomeLandformForm.Table, density: 0.1f, perScene: 1, size: V(300f, 1200f), height: V(15f, 30f),
				top: 0.85f, pad: true)),
			new Preset("Hafgerdingar", "Ice Shelf", Make("Hafgerdingar", BiomeLandformForm.Arc, density: 0.1f, perScene: 1, size: V(1200f, 3000f),
				length: V(800f, 2500f), width: V(30f, 60f), height: V(12f, 25f), lean: -0.6f, paint: 4, part: BiomeLandformPart.Whole)),

			// Ocean: Lophelia coral mounds along the contour of the deep slope, pockmark fields, and in the cold band iceberg plough marks.
			new Preset("CoralMounds", "Ocean", Make("CoralMounds", BiomeLandformForm.Oblong, density: 1f, perScene: 12, size: V(80f, 300f), length: V(120f, 450f),
				height: V(5f, 25f), top: 0.2f, roughness: 0.4f, align: BiomeLandformAlign.Contour, depth: V(200f, 1200f), paint: 2, part: BiomeLandformPart.Flank)),
			new Preset("Pockmarks", "Ocean", Make("Pockmarks", BiomeLandformForm.Hollow, density: 1f, size: V(300f, 900f), width: V(20f, 80f), height: V(2f, 6f),
				count: 24, paint: 3, part: BiomeLandformPart.Floor)),
			new Preset("PloughMarks", "Ocean", Make("PloughMarks", BiomeLandformForm.Grooves, density: 0.5f, length: V(500f, 3000f), width: V(10f, 40f),
				height: V(1f, 4f), paint: 5, part: BiomeLandformPart.Whole)
				.Banded(new Vector2(-1f, -0.3f), default)),

			// Geyser Basin: the low sinter shield the geyser clusters build (its pool basins and runoff are the geothermal carve's, I2q).
			new Preset("SinterShield", "Geyser Basin", Make("SinterShield", BiomeLandformForm.Dome, density: 2f, perScene: 6, size: V(10f, 30f), height: V(0.5f, 1.5f),
				top: 0.4f, slope: V(0f, 8f))),

			// Permanent Ice (decision 70): the summit dome smoothing the cap, the dry caps' ice tablets with their sheer lips, the wet
			// caps' steps round the margin (author IceTablet a second time banded dry for the draft's "×2 dry").
			new Preset("SummitDome", "Permanent Ice", Make("SummitDome", BiomeLandformForm.Dome, Fitted, fit: 0.7f, size: V(150f, 6000f), height: V(20f, 60f),
				anchor: BiomeLandformAnchor.Highest, paint: 0, part: BiomeLandformPart.Whole)),
			new Preset("IceTablet", "Permanent Ice", Make("IceTablet", BiomeLandformForm.Table, density: 0.6f, perPatch: 5, size: V(25f, 150f), height: V(8f, 30f),
				top: 0.9f, rim: 0.5f, slope: V(0f, 15f), paint: 7, part: BiomeLandformPart.Flank, pad: true)),
			new Preset("StepMargin", "Permanent Ice", Make("StepMargin", BiomeLandformForm.Scarp, density: 1f, perPatch: 3, size: V(40f, 120f), length: V(300f, 1200f),
				width: V(6f, 25f), height: V(6f, 25f), anchor: BiomeLandformAnchor.PatchEdge, nearWater: 45f, paint: 7, part: BiomeLandformPart.Flank)
				.Banded(default, new Vector2(-0.3f, 1f))),

			// Radiation Plain (decision 71): Valhalla's ring graben round a pale palimpsest, a catena of merged craters on a line, knobs
			// left on a vanished rim's arc, worn lag-floored bowls, the scorched furrow and the bolt's radial scar (both with their pad).
			new Preset("RingGraben", "Radiation Plain", Make("RingGraben", BiomeLandformForm.RingGraben, Fitted, fit: 0.8f, perScene: 1, size: V(600f, 2500f),
				width: V(12f, 40f), height: V(3f, 10f), anchor: BiomeLandformAnchor.Centroid, paint: 1, part: BiomeLandformPart.Floor)),
			new Preset("Catena", "Radiation Plain", Make("Catena", BiomeLandformForm.Impact, BiomeLandformPlacement.Chain, density: 0.5f, perScene: 2, size: V(20f, 80f),
				length: V(500f, 2000f), height: V(0.15f, 0.15f), rim: 0.035f, count: 20, lean: 0.05f, roughness: 0.1f)),
			new Preset("KnobField", "Radiation Plain", Make("KnobField", BiomeLandformForm.Cone, BiomeLandformPlacement.Chain, density: 0.5f, perPatch: 2, size: V(30f, 150f),
				length: V(600f, 2000f), height: V(15f, 60f), top: 0.15f, count: 12, lean: 0.45f, roughness: 0.6f, paint: 3, part: BiomeLandformPart.Flank, pad: true)),
			new Preset("DegradedCrater", "Radiation Plain", Make("DegradedCrater", BiomeLandformForm.Hollow, density: 1f, size: V(100f, 400f), height: V(3f, 12f),
				top: 0.5f, roughness: 0.3f)),
			new Preset("ScorchFurrow", "Radiation Plain", Make("ScorchFurrow", BiomeLandformForm.Grooves, density: 0.2f, perScene: 1, length: V(300f, 800f),
				width: V(8f, 20f), height: V(1f, 3f), paint: 6, part: BiomeLandformPart.Whole, pad: true)),
			new Preset("BoltScar", "Radiation Plain", Make("BoltScar", BiomeLandformForm.Star, density: 0.2f, perScene: 1, size: V(80f, 300f), height: V(-3f, -1f),
				count: 9, paint: 6, part: BiomeLandformPart.Whole, pad: true)),

			// Regolith Plain (decision 72): the crater population (power-law sizes, four ageing classes carved old first, floor pads),
			// the bright-rayed young crater (airless), old floors filled flat (Cayley plains), the rampart crater of thin air.
			new Preset("CraterField", "Regolith Plain", Make("CraterField", BiomeLandformForm.Impact, density: 60f, size: V(8f, 400f), height: V(0.1f, 0.1f),
				rim: 0.035f, top: 0.1f, pad: true).PowerLaw(2f, true)),
			new Preset("RayCrater", "Regolith Plain", Make("RayCrater", BiomeLandformForm.Impact, density: 0.2f, perScene: 2, size: V(300f, 1200f),
				height: V(0.2f, 0.2f), rim: 0.04f, top: 0.15f, crown: 0.18f, crownDepth: 0.04f, count: 9, roughness: 0.3f, air: BiomeAtmosphereRequirement.Airless,
				pad: true)),
			new Preset("CayleyFill", "Regolith Plain", Make("CayleyFill", BiomeLandformForm.SpillFill, Field, density: 1f, height: V(0.02f, 0.05f), slope: V(0f, 5f),
				paint: 3, part: BiomeLandformPart.Floor)),
			new Preset("RampartCrater", "Regolith Plain", Make("RampartCrater", BiomeLandformForm.Rampart, density: 0.3f, perScene: 3, size: V(200f, 900f),
				height: V(0.15f, 0.15f), rim: 0.03f, top: 0.2f, crown: 1.9f, crownDepth: 4f, air: BiomeAtmosphereRequirement.Thin, paint: 5,
				part: BiomeLandformPart.Ejecta)),

			// Salt Flat: the pan filled dead flat, the megapolygon net of pressure ridges, the brine eyes, the salt dome (its top dolines
			// and the height ≥ 0.52 gate are not built; the dome stands on the patch's highest ground instead).
			new Preset("SaltPan", "Salt Flat", Make("SaltPan", BiomeLandformForm.SpillFill, Field, density: 1f, height: V(0.02f, 0.05f), paint: 0,
				part: BiomeLandformPart.Floor)),
			new Preset("SaltRidges", "Salt Flat", Make("SaltRidges", BiomeLandformForm.Polygons, Field, density: 1f, width: V(40f, 120f), height: V(0.5f, 1.5f),
				paint: 1, part: BiomeLandformPart.Rim)),
			new Preset("BrineEye", "Salt Flat", Make("BrineEye", BiomeLandformForm.Pit, density: 0.5f, perScene: 3, size: V(8f, 20f), height: V(2f, 6f), top: 0.6f,
				anchor: BiomeLandformAnchor.Lowest, slope: V(0f, 6f), pad: true)),
			new Preset("SaltDome", "Salt Flat", Make("SaltDome", BiomeLandformForm.Table, density: 0.1f, perScene: 1, size: V(200f, 600f), height: V(20f, 80f),
				top: 0.7f, rim: 1f, roughness: 0.4f, anchor: BiomeLandformAnchor.Highest, pad: true)),

			// Rocky Coast (decision 74): flagstone stacks off the cliff (their tops pads), the Stoorworm's line of teeth, the Old Man,
			// geos cut inland from the shore, the gloup behind the cliff, the wave-cut platform, the ayre across a bay's mouth (the oyce
			// behind it is the sea's own water), the tidal brough and its causeway. The arch across a geo is a formation (I7), not a carve;
			// the Old Man's basalt plinth and the gloup's sea cave are not built.
			new Preset("SeaStacks", "Rocky Coast", Make("SeaStacks", BiomeLandformForm.Tower, density: 3f, perScene: 8, size: V(15f, 50f), height: V(15f, 60f),
				top: 0.1f, flank: 80f, count: 5, roughness: 0.5f, anchor: BiomeLandformAnchor.ShoreSetback, nearWater: 45f, depth: V(0.3f, 30f), pad: true)),
			new Preset("StoorwormTeeth", "Rocky Coast", Make("StoorwormTeeth", BiomeLandformForm.Tower, BiomeLandformPlacement.Chain, density: 0.5f, perScene: 1,
				size: V(20f, 50f), length: V(300f, 900f), height: V(12f, 45f), top: 0.1f, flank: 82f, count: 7, lean: 0.05f, roughness: 0.2f,
				anchor: BiomeLandformAnchor.ShoreSetback, nearWater: 150f, depth: V(0.3f, 40f), pad: true)),
			new Preset("OldMan", "Rocky Coast", Make("OldMan", BiomeLandformForm.Tower, density: 0.3f, perScene: 1, size: V(25f, 40f), height: V(40f, 100f),
				top: 0.15f, flank: 86f, count: 7, roughness: 0.6f, anchor: BiomeLandformAnchor.ShoreSetback, nearWater: 60f, depth: V(0.3f, 20f), pad: true)),
			new Preset("Geos", "Rocky Coast", Make("Geos", BiomeLandformForm.Geo, density: 2f, length: V(40f, 300f), width: V(5f, 25f), height: V(6f, 30f),
				top: 0.85f, datum: BiomeLandformDatum.SeaSurface, datumDepth: V(2f, 2f), anchor: BiomeLandformAnchor.Shore, paint: 1, part: BiomeLandformPart.Floor)),
			new Preset("Gloup", "Rocky Coast", Make("Gloup", BiomeLandformForm.Pit, density: 0.5f, perScene: 2, size: V(20f, 40f), height: V(15f, 30f), top: 0.7f,
				datum: BiomeLandformDatum.SeaSurface, datumDepth: V(1f, 3f), anchor: BiomeLandformAnchor.ShoreSetback, nearWater: 75f, pad: true)),
			new Preset("WaveCutPlatform", "Rocky Coast", Make("WaveCutPlatform", BiomeLandformForm.Bench, Field, density: 1f, height: V(0.3f, 1f), paint: 2,
				part: BiomeLandformPart.Floor)),
			new Preset("Ayre", "Rocky Coast", Make("Ayre", BiomeLandformForm.Straight, density: 1f, perScene: 2, length: V(100f, 800f), width: V(20f, 60f),
				height: V(1.5f, 3f), top: 0.3f, datum: BiomeLandformDatum.SeaSurface, datumDepth: V(-3f, -1.5f), anchor: BiomeLandformAnchor.BayMouth, paint: 1,
				part: BiomeLandformPart.Whole)),
			new Preset("TidalBrough", "Rocky Coast", Make("TidalBrough", BiomeLandformForm.Table, density: 0.3f, perScene: 1, size: V(80f, 300f), width: V(10f, 20f),
				height: V(15f, 40f), top: 0.8f, rim: 1f, moatDepth: 0.8f, datum: BiomeLandformDatum.SeaSurface, datumDepth: V(-40f, -15f),
				anchor: BiomeLandformAnchor.ShoreSetback, nearWater: 250f, depth: V(0.3f, 60f), pad: true)),

			// Savanna: granite kopjes (a castle of corestones: a rough fluted tower), low dwala pavements (not the jungle's sheer
			// inselberg), seasonal pans, the termite islands (their mounds a zone the rules band on), the cleft table hill (its pad).
			new Preset("Kopje", "Savanna", Make("Kopje", BiomeLandformForm.Tower, density: 0.5f, perScene: 6, size: V(60f, 200f), height: V(20f, 60f),
				top: 0.55f, flank: 55f, count: 9, roughness: 0.9f, paint: 4, part: BiomeLandformPart.Whole, pad: true)),
			new Preset("Dwala", "Savanna", Make("Dwala", BiomeLandformForm.Oblong, density: 0.3f, perScene: 4, size: V(60f, 200f), length: V(100f, 400f),
				height: V(5f, 25f), top: 0.2f, roughness: 0.2f, paint: 4, part: BiomeLandformPart.Whole)),
			new Preset("Pan", "Savanna", Make("Pan", BiomeLandformForm.Pan, density: 1f, perScene: 8, size: V(30f, 150f), height: V(0.5f, 2f), top: 0.8f,
				anchor: BiomeLandformAnchor.Lowest, slope: V(0f, 5f), paint: 5, part: BiomeLandformPart.Floor, pad: true)),
			new Preset("TermiteIslands", "Savanna", Make("TermiteIslands", BiomeLandformForm.Dome, density: 30f, size: V(8f, 20f), height: V(0.6f, 2f),
				roughness: 0.2f, slope: V(0f, 8f))),
			new Preset("TableHill", "Savanna", Make("TableHill", BiomeLandformForm.Table, density: 0.1f, perScene: 1, size: V(120f, 300f), height: V(30f, 60f),
				top: 0.9f, rim: 1f, crown: 0.06f, crownDepth: 15f, pad: true)),

			// Rille (decision 76): the sinuous rille from its vent hollow, the beaded rille of a fallen-in tube, the straight graben, the
			// arcuate set round a plain's edge. Author the rilles before the graben, so the graben cut them. Their stored lines are the
			// sites' own segments. Not built yet: the inner rille, roofed stretches, en échelon relay ramps, the drainage pits on a
			// graben's floor, a heading from the patch's long axis.
			new Preset("SinuousRille", "Rille", Make("SinuousRille", BiomeLandformForm.Rille, density: 0.2f, perPatch: 1, perScene: 2, length: V(1500f, 6000f),
				width: V(120f, 500f), height: V(25f, 120f), top: 0.4f, rim: 4f, crownDepth: 1.2f, anchor: BiomeLandformAnchor.Highest, pad: true)),
			new Preset("BeadedRille", "Rille", Make("BeadedRille", BiomeLandformForm.Rille, density: 0.2f, perScene: 2, length: V(800f, 3000f), width: V(60f, 200f),
				height: V(15f, 50f), top: 0.3f, rim: 2f, crown: 0.5f, anchor: BiomeLandformAnchor.Highest)),
			new Preset("StraightGraben", "Rille", Make("StraightGraben", BiomeLandformForm.Graben, density: 0.3f, perScene: 3, length: V(1000f, 5000f),
				width: V(150f, 600f), height: V(20f, 80f), top: 0.6f)),
			new Preset("ArcuateRille", "Rille", Make("ArcuateRille", BiomeLandformForm.Graben, density: 0.2f, perScene: 1, size: V(4000f, 16000f),
				length: V(1500f, 6000f), width: V(120f, 300f), height: V(15f, 50f), top: 0.6f, count: 3, moat: 550f, anchor: BiomeLandformAnchor.Lowest)),

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
			// 2026-10-25, I2g-b: the newer drafts' other names.
			{ "shakehole", "Doline" },
			{ "sinkhole", "Doline" },
			{ "swallowhole", "Ponor" },
			{ "swallet", "Ponor" },
			{ "towerkarst", "Fenglin" },
			{ "conekarst", "Fengcong" },
			{ "cockpitkarst", "Fengcong" },
			{ "cockpit", "Fengcong" },
			{ "compounddoline", "Uvala" },
			{ "tidalcreeksretuned", "MangroveCreeks" },
			{ "mkokocreek", "MangroveCreeks" },
			{ "kisima", "RagDolines" },
			{ "multiringbasin", "MultiRingBasin" },
			{ "ringbasin", "MultiRingBasin" },
			{ "impactbasin", "MultiRingBasin" },
			{ "summitcrater", "SummitCaldera" },
			{ "skylight", "SkylightChain" },
			{ "rampart", "RampartPit" },
			{ "labyrinthterrain", "Labyrinth" },
			{ "lavalake", "Patera" },
			{ "convectioncell", "ConvectionCells" },
			{ "bladedterrain", "BladedRidges" },
			{ "breakaway", "LateriteBreakaway" },
			{ "lateritescarp", "LateriteBreakaway" },
			{ "iyaditch", "Iya" },
			{ "roll", "ShelfRolls" },
			{ "shelfroll", "ShelfRolls" },
			{ "pressureridgebelt", "PressureBelt" },
			{ "pressurebelt", "PressureBelt" },
			{ "hafgerðingar", "Hafgerdingar" },
			{ "tidecrack", "TideCracks" },
			{ "epishelf", "EpishelfLake" },
			// Decisions 70/71's names for the Permanent Ice and Radiation Plain drafts' presets.
			{ "panne", "SaltPannes" },
			{ "rima", "StraightGraben" },
			{ "fossae", "StraightGraben" },
			{ "seastack", "SeaStacks" },
			{ "stack", "SeaStacks" },
			{ "geo", "Geos" },
			{ "oyce", "Ayre" },
			{ "brough", "TidalBrough" },
			{ "wavecutplatform", "WaveCutPlatform" },
			{ "cayleyplain", "CayleyFill" },
			{ "craterpopulation", "CraterField" },
			{ "brinepool", "BrineEye" },
			{ "valhalla", "RingGraben" },
			{ "iceringbasin", "RingGraben" },
			{ "craterchain", "Catena" },
			{ "worncrater", "DegradedCrater" },
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
