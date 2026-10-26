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

		/// <summary>A copy with treads down its flank or benches down its walls (2026-10-25, I2g-d).</summary>
		private static BiomeLandform Steps(BiomeLandform form, int steps)
		{
			form.Steps = steps;
			return form;
		}

		/// <summary>A copy that shifts the lines carved before it along itself (a strike-slip trace, decision 90).</summary>
		private static BiomeLandform Shift(BiomeLandform form, Vector2 metres)
		{
			form.Shift = metres;
			return form;
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
			// Hornitos held to the skylight line (2026-10-25, I2g-d follow-up): on its flanks, so author Hornito right after SkylightChain.
			new Preset("Hornito", "Lava Tube", Make("Hornito", BiomeLandformForm.Cone, density: 60f, perPatch: 30, perScene: 30, size: V(2f, 6f), height: V(1f, 5f),
				crown: 0.3f, crownDepth: 0.8f, anchor: BiomeLandformAnchor.OnFlankOf)),
			new Preset("CollapseTrench", "Lava Tube", Make("CollapseTrench", BiomeLandformForm.Meander, density: 0.2f, perScene: 2, length: V(400f, 2000f),
				width: V(30f, 80f), height: V(8f, 25f), top: 0.8f, anchor: BiomeLandformAnchor.Highest, pad: true)),
			new Preset("LavaRise", "Lava Tube", Make("LavaRise", BiomeLandformForm.Table, density: 3f, size: V(20f, 80f), length: V(30f, 150f), height: V(2f, 8f),
				top: 0.8f, crown: 0.08f, crownDepth: 2f, slope: V(0f, 6f), paint: 2, part: BiomeLandformPart.Top)),

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

			// Molten Surface (decision 65): paterae, overflow benches, crust islands, spatter ramparts, lava channels, the plume's paint-only
			// ring, pressure ridges. The patera's floor and the crust island's top are measured from the LAVA's surface (2026-10-25, I2r:
			// the floor 1-4 m under it, so its lake floods it; the island's top 1-3 m over it); on a scene with no lava they stand on the ground.
			new Preset("Patera", "Molten Surface", Make("Patera", BiomeLandformForm.Pan, density: 0.3f, perScene: 2, size: V(400f, 1500f), height: V(30f, 120f),
				top: 0.85f, rim: 4f, anchor: BiomeLandformAnchor.Lowest, datum: BiomeLandformDatum.LavaSurface, datumDepth: V(1f, 4f), paint: 8,
				part: BiomeLandformPart.Floor, pad: true)),
			new Preset("OverflowBench", "Molten Surface", Make("OverflowBench", BiomeLandformForm.Contour, density: 0.5f, perScene: 3, length: V(300f, 1500f),
				width: V(10f, 30f), height: V(2f, 12f), top: 0.6f, lean: -0.5f, paint: 2, part: BiomeLandformPart.Crest)),
			new Preset("CrustIsland", "Molten Surface", Make("CrustIsland", BiomeLandformForm.Table, density: 0.3f, perScene: 3, size: V(40f, 200f), height: V(5f, 15f),
				top: 0.8f, datum: BiomeLandformDatum.LavaSurface, datumDepth: V(-3f, -1f), pad: true)),
			new Preset("SpatterRampart", "Molten Surface", Make("SpatterRampart", BiomeLandformForm.Straight, density: 0.3f, perScene: 3, length: V(150f, 600f),
				width: V(6f, 16f), height: V(2f, 8f), roughness: 0.6f)),
			new Preset("LavaChannel", "Molten Surface", Make("LavaChannel", BiomeLandformForm.Meander, density: 0.3f, perScene: 2, length: V(500f, 3000f),
				width: V(8f, 25f), height: V(2f, 6f), top: 0.7f, rim: 3.5f, anchor: BiomeLandformAnchor.Highest).MoltenLine()),
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
				width: V(6f, 20f), height: V(2f, 6f), count: 3, moat: 25f, lean: 0.4f, roughness: 0.6f, paint: 4, part: BiomeLandformPart.Whole)),
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
				length: V(800f, 2500f), width: V(30f, 60f), height: V(12f, 25f), count: 3, moat: 250f, lean: -0.6f, paint: 4, part: BiomeLandformPart.Whole)),

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

			// Ice Geyser Field (decision 53, I2q-b): Enceladus' tiger stripes, three parallel sulci 350 m apart, each a V-floored trough
			// 25–50 m wide and 8–20 m deep. Their stored segments are the scene's vent lines (IceStripes.FromLandforms), so a biome whose
			// vents follow lines (BiomeVents.Layout Fissures) sets its jets along them and the heat mask warms them. Not built yet: the
			// flank ridges, the Y-forks and hooks at the ends, the 30 m warp.
			new Preset("TigerStripe", "Ice Geyser Field", Make("TigerStripe", BiomeLandformForm.Graben, density: 0.4f, perPatch: 1, perScene: 1,
				length: V(1500f, 6000f), width: V(25f, 50f), height: V(8f, 20f), top: 0.15f, count: 3, moat: 350f)),

			// Scrubland (2026-10-25, I2g-c): coalescing fan aprons off the mountain fronts, the planed apron below them, broad braided washes
			// (shallower than Badlands' Wash, which keeps that name), black flow lobes in the lower ground and one great maar.
			new Preset("Bajada", "Scrubland", Make("Bajada", BiomeLandformForm.Fan, density: 0.2f, perScene: 3, size: V(600f, 2000f), height: V(6f, 25f),
				roughness: 0.2f, anchor: BiomeLandformAnchor.SlopeBreak, align: BiomeLandformAlign.Downslope, slope: V(8f, 35f))),
			new Preset("Pediment", "Scrubland", Make("Pediment", BiomeLandformForm.Fan, density: 0.15f, perScene: 2, size: V(400f, 1200f), height: V(2f, 6f),
				roughness: 0.5f, anchor: BiomeLandformAnchor.SlopeBreak, align: BiomeLandformAlign.Downslope, slope: V(5f, 30f))),
			new Preset("BraidedWash", "Scrubland", Make("BraidedWash", BiomeLandformForm.Meander, density: 0.3f, perScene: 3, length: V(500f, 2500f),
				width: V(5f, 40f), height: V(0.5f, 2f), top: 0.85f, anchor: BiomeLandformAnchor.Highest, paint: 2, part: BiomeLandformPart.Floor)),
			new Preset("Malpais", "Scrubland", Make("Malpais", BiomeLandformForm.FlowSteps, Field, density: 1f, width: V(60f, 250f), height: V(1.5f, 5f),
				count: 5, roughness: 0.6f)),
			new Preset("Maar", "Scrubland", Make("Maar", BiomeLandformForm.Caldera, density: 1f, perScene: 1, size: V(800f, 1400f), height: V(0.1f, 0.14f),
				top: 0.6f, rim: 0.05f, anchor: BiomeLandformAnchor.Lowest, pad: true)),

			// Rocky Terrain (2026-10-25, I2g-c): the flat high valley floor of the roof of the world, frost-shattered serrated crests.
			new Preset("Pamir", "Rocky Terrain", Make("Pamir", BiomeLandformForm.SpillFill, Field, density: 1f, height: V(0.02f, 0.05f), slope: V(0f, 8f))),
			new Preset("ShatterComb", "Rocky Terrain", Make("ShatterComb", BiomeLandformForm.Fins, Field, density: 1f, width: V(6f, 20f), height: V(4f, 15f),
				roughness: 0.7f, align: BiomeLandformAlign.SceneAxis)),

			// Scree (2026-10-25, I2g-c): cones and chutes under the cliffs, the rampart a snowbed piles at its foot, the hollow it digs, a
			// ridge of pinnacles, and the tracks of rocks that walk across a frozen playa.
			new Preset("ScreeCone", "Scree", Make("ScreeCone", BiomeLandformForm.Fan, density: 2f, perScene: 12, size: V(20f, 80f), height: V(4f, 20f),
				roughness: 0.3f, anchor: BiomeLandformAnchor.SlopeBreak, align: BiomeLandformAlign.Downslope, slope: V(25f, 60f))),
			new Preset("ScreeChutes", "Scree", Make("ScreeChutes", BiomeLandformForm.Grooves, density: 10f, length: V(40f, 200f), width: V(2f, 6f),
				height: V(0.5f, 2f), slope: V(30f, 60f))),
			new Preset("ProtalusRampart", "Scree", Make("ProtalusRampart", BiomeLandformForm.Arc, density: 0.5f, perScene: 3, size: V(60f, 200f), length: V(40f, 150f),
				width: V(6f, 15f), height: V(2f, 6f), roughness: 0.4f, anchor: BiomeLandformAnchor.SlopeBreak)),
			new Preset("NivationHollow", "Scree", Make("NivationHollow", BiomeLandformForm.Hollow, density: 2f, size: V(20f, 80f), height: V(2f, 6f), slope: V(15f, 35f))),
			new Preset("PinnacleRidge", "Scree", Make("PinnacleRidge", BiomeLandformForm.Tower, BiomeLandformPlacement.Chain, density: 0.3f, perScene: 2,
				size: V(8f, 25f), length: V(200f, 800f), height: V(15f, 40f), top: 0.1f, flank: 75f, count: 12, roughness: 0.4f)),
			new Preset("WalkingRock", "Scree", Make("WalkingRock", BiomeLandformForm.Grooves, density: 30f, length: V(20f, 200f), width: V(0.2f, 0.5f),
				height: V(0.05f, 0.15f), slope: V(0f, 3f), pad: true)),

			// ── 2026-10-25, phase I2g-d (decisions 77–96): the last drafts' landforms ──
			// Entries on a host (OnTopOf / OnFlankOf) stand on the sites of the nearest entry before them that is not itself on one:
			// author each right after its host (and its fellow members).

			// Seamount (decision 80): the guyot raised from the deep floor to its flat top 25–60 m under the swell, its drowned
			// shorelines as treads down its flank, the one pinnacle that breaks the swell and the lesser boilers on its top, rift spurs
			// and flank cones round it, a collapse scar down one side, and the flexural moat round its foot.
			new Preset("Guyot", "Seamount", Steps(Make("Guyot", BiomeLandformForm.Table, Fitted, fit: 0.7f, perPatch: 1, size: V(600f, 12000f), top: 0.6f, flank: 18f,
				rim: 3f, anchor: BiomeLandformAnchor.Centroid, datum: BiomeLandformDatum.SeaSurface, datumDepth: V(25f, 60f), paint: 3, part: BiomeLandformPart.Top,
				pad: true), 3)),
			new Preset("Pinnacle", "Seamount", Make("Pinnacle", BiomeLandformForm.Tower, perScene: 1, size: V(30f, 80f), top: 0.2f, flank: 55f, count: 5,
				roughness: 0.4f, anchor: BiomeLandformAnchor.OnTopOf, datum: BiomeLandformDatum.SeaSurface, datumDepth: V(1f, 4f), paint: 6, part: BiomeLandformPart.Flank,
				pad: true)),
			new Preset("Boilers", "Seamount", Make("Boilers", BiomeLandformForm.Cone, perPatch: 4, size: V(10f, 30f), roughness: 0.2f, anchor: BiomeLandformAnchor.OnTopOf,
				datum: BiomeLandformDatum.SeaSurface, datumDepth: V(3f, 9f))),
			new Preset("RiftSpurs", "Seamount", Make("RiftSpurs", BiomeLandformForm.Straight, perPatch: 3, length: V(1500f, 4000f), width: V(150f, 400f),
				height: V(30f, 120f), roughness: 0.4f, anchor: BiomeLandformAnchor.OnFlankOf, align: BiomeLandformAlign.HostDownhill, paint: 4, part: BiomeLandformPart.Crest)),
			new Preset("FlankCones", "Seamount", Make("FlankCones", BiomeLandformForm.Cone, density: 0.5f, perPatch: 12, size: V(80f, 250f), height: V(20f, 60f),
				crown: 0.2f, crownDepth: 2f, roughness: 0.2f, anchor: BiomeLandformAnchor.OnFlankOf)),
			new Preset("CollapseScar", "Seamount", Make("CollapseScar", BiomeLandformForm.Slide, perScene: 1, size: V(600f, 1500f), length: V(1000f, 3000f),
				height: V(40f, 120f), anchor: BiomeLandformAnchor.OnFlankOf, align: BiomeLandformAlign.HostDownhill, pad: true)),
			new Preset("FlexuralMoat", "Seamount", Make("FlexuralMoat", BiomeLandformForm.Ring, fit: 1.15f, width: V(300f, 800f), height: V(-40f, -10f),
				anchor: BiomeLandformAnchor.OnTopOf)),

			// Snow (decision 81): wind-scoured knolls, the drift in each one's lee (author it right after ScourKnoll), metre-high snow
			// dunes across the wind, and the sheltered hollow whose floor is the sleeper firs' grove mask.
			new Preset("ScourKnoll", "Snow", Make("ScourKnoll", BiomeLandformForm.Dome, density: 1f, size: V(40f, 150f), height: V(4f, 20f), roughness: 0.3f,
				align: BiomeLandformAlign.Wind, paint: 3, part: BiomeLandformPart.Top)),
			new Preset("LeeDrift", "Snow", Make("LeeDrift", BiomeLandformForm.Tongue, size: V(30f, 120f), length: V(60f, 250f), height: V(1f, 4f),
				anchor: BiomeLandformAnchor.LeeOf, align: BiomeLandformAlign.Wind, paint: 2, part: BiomeLandformPart.Whole)),
			new Preset("SnowDunes", "Snow", Make("SnowDunes", BiomeLandformForm.SedimentWaves, Field, density: 1f, width: V(20f, 40f), height: V(0.5f, 2f),
				align: BiomeLandformAlign.Wind, paint: 2, part: BiomeLandformPart.Crest)),
			new Preset("SleeperHollow", "Snow", Make("SleeperHollow", BiomeLandformForm.Hollow, density: 0.5f, perPatch: 3, size: V(80f, 200f), height: V(2f, 5f),
				top: 0.5f, slope: V(0f, 12f), paint: 4, part: BiomeLandformPart.Floor)),

			// Steppe (decision 82; its sor, takyr and dry valleys are SaltPan, Playa and Wash painted its own way): the rocky sopka hills,
			// the marmots' bobak mounds (their pads), a line of oba barrows along a crest.
			new Preset("Sopka", "Steppe", Make("Sopka", BiomeLandformForm.Dome, density: 0.25f, size: V(200f, 800f), height: V(12f, 45f), roughness: 0.3f,
				paint: 5, part: BiomeLandformPart.Top)),
			new Preset("BobakMounds", "Steppe", Make("BobakMounds", BiomeLandformForm.Dome, density: 50f, perScene: 300, size: V(8f, 20f), height: V(0.4f, 1.5f),
				slope: V(0f, 8f), paint: 7, part: BiomeLandformPart.Top, pad: true)),
			new Preset("ObaChain", "Steppe", Make("ObaChain", BiomeLandformForm.Dome, BiomeLandformPlacement.Chain, density: 0.3f, perScene: 2, size: V(12f, 30f),
				length: V(200f, 900f), height: V(1f, 3f), count: 14, roughness: 0.15f, anchor: BiomeLandformAnchor.Highest, align: BiomeLandformAlign.Contour, pad: true)),

			// Sulphur Flats (decision 84; the vents themselves are the geothermal carve's): Dallol's vent dome, collapse pits in the
			// crust, the white mountain that always burns, the tableland a town is carved into, open fissure rows.
			new Preset("VentDome", "Sulphur Flats", Make("VentDome", BiomeLandformForm.Dome, density: 0.1f, perScene: 1, size: V(600f, 1500f), height: V(20f, 50f),
				crown: 0.15f, crownDepth: 3f, roughness: 0.2f, paint: 4, part: BiomeLandformPart.Rim, pad: true)),
			new Preset("CollapsePit", "Sulphur Flats", Make("CollapsePit", BiomeLandformForm.Pit, density: 1f, perScene: 4, size: V(10f, 40f), height: V(3f, 10f),
				top: 0.5f, anchor: BiomeLandformAnchor.Lowest, pad: true)),
			new Preset("FireMountain", "Sulphur Flats", Make("FireMountain", BiomeLandformForm.Cone, density: 0.05f, perScene: 1, size: V(400f, 900f), height: V(60f, 150f),
				crown: 0.12f, crownDepth: 8f, roughness: 0.3f, pad: true)),
			new Preset("CarvedTableland", "Sulphur Flats", Make("CarvedTableland", BiomeLandformForm.Table, density: 0.1f, perScene: 1, size: V(80f, 200f),
				length: V(300f, 800f), height: V(15f, 30f), top: 0.85f, rim: 0.5f, roughness: 0.3f, count: 18, pad: true)),
			new Preset("FissureRow", "Sulphur Flats", Make("FissureRow", BiomeLandformForm.Straight, density: 0.3f, perScene: 3, length: V(200f, 800f),
				width: V(2f, 5f), height: V(-3f, -1f), roughness: 0.3f)),

			// Sulphuric Cloud Deck (decision 85): the tessera weave (fins at the patch's strike crossed at 70° by flat troughs), the
			// plateau's rim scarp, the fold belt's great fins, Cleopatra breached by its one channel. Not built: the bench below the
			// scarp (a wave-cut form, no sea here), the inner peak ring, the fold belt held to the highest patch, scatter along the strike.
			new Preset("TesseraWeave", "Sulphuric Cloud Deck", Make("TesseraWeave", BiomeLandformForm.Weave, Field, density: 1f, width: V(20f, 60f), height: V(3f, 12f),
				top: 0.25f, moat: 105f, moatDepth: 15f, paint: 3, part: BiomeLandformPart.Floor)),
			new Preset("PlateauScarp", "Sulphuric Cloud Deck", Make("PlateauScarp", BiomeLandformForm.Scarp, density: 0.3f, perPatch: 1, size: V(200f, 600f),
				length: V(1000f, 4000f), width: V(30f, 70f), height: V(30f, 60f), anchor: BiomeLandformAnchor.SlopeBreak, slope: V(3f, 40f))),
			new Preset("FoldBelt", "Sulphuric Cloud Deck", Make("FoldBelt", BiomeLandformForm.Fins, Field, density: 1f, perScene: 1, width: V(120f, 300f),
				height: V(20f, 60f), roughness: 0.3f)),
			new Preset("Cleopatra", "Sulphuric Cloud Deck", Make("Cleopatra", BiomeLandformForm.Impact, density: 0.1f, perScene: 1, size: V(300f, 900f),
				width: V(20f, 40f), height: V(0.18f, 0.18f), rim: 0.04f, top: 0.3f, align: BiomeLandformAlign.Downslope, pad: true).Breached(1.1f)),

			// Swamp Ruins (decision 87): barrows with a moulded drum and wet ditch (their pads), the sinking quarter the water floods,
			// the cuniculi's pits along their tunnel, the tufa spur the acropolis stands on, a harbour mole by the water.
			new Preset("Tumulus", "Swamp Ruins", Make("Tumulus", BiomeLandformForm.Dome, density: 4f, perPatch: 9, size: V(18f, 45f), height: V(4f, 10f), top: 0.2f,
				moat: 3f, moatDepth: 1f, slope: V(0f, 8f), paint: 4, part: BiomeLandformPart.Top, pad: true)),
			new Preset("SinkingQuarter", "Swamp Ruins", Make("SinkingQuarter", BiomeLandformForm.Pan, density: 0.5f, perScene: 2, size: V(150f, 400f),
				height: V(0.6f, 1.5f), top: 0.85f, anchor: BiomeLandformAnchor.Lowest, pad: true)),
			new Preset("Cuniculi", "Swamp Ruins", Make("Cuniculi", BiomeLandformForm.PitChain, density: 0.5f, perScene: 3, length: V(150f, 600f), width: V(3f, 5f),
				height: V(2f, 4f), top: 0.6f, moat: 30f, anchor: BiomeLandformAnchor.Highest, pad: true)),
			new Preset("TufaSpur", "Swamp Ruins", Make("TufaSpur", BiomeLandformForm.Table, density: 0.1f, perScene: 1, size: V(150f, 400f), height: V(12f, 30f),
				top: 0.8f, roughness: 0.3f, count: 20, anchor: BiomeLandformAnchor.SlopeBreak, slope: V(3f, 30f), pad: true)),
			new Preset("HarbourMole", "Swamp Ruins", Make("HarbourMole", BiomeLandformForm.Straight, density: 0.5f, perScene: 2, length: V(80f, 220f), width: V(8f, 20f),
				height: V(1f, 2f), top: 0.5f, anchor: BiomeLandformAnchor.NearWater, nearWater: 40f, paint: 2, part: BiomeLandformPart.Crest, pad: true)),

			// Swamp Temple (decision 88): the tarn in its maar bowl with the drainers' notch, cercado mounds in the humedal, the painted
			// megaboulders, the humedal itself (the palaeolake's shape, its own paint). Not built: the slump lobe below the notch.
			new Preset("TarnBowl", "Swamp Temple", Make("TarnBowl", BiomeLandformForm.Caldera, density: 0.1f, perScene: 1, size: V(200f, 450f), height: V(0.08f, 0.12f),
				top: 0.6f, rim: 0.06f, count: 5, pad: true).Breached(0.15f)),
			new Preset("CercadoMound", "Swamp Temple", Make("CercadoMound", BiomeLandformForm.Platform, density: 0.3f, perScene: 3, size: V(40f, 70f), height: V(1f, 2f),
				top: 0.7f, slope: V(0f, 4f), pad: true)),
			new Preset("PaintedStones", "Swamp Temple", Make("PaintedStones", BiomeLandformForm.Dome, BiomeLandformPlacement.Chain, density: 0.2f, perScene: 1,
				size: V(6f, 14f), length: V(60f, 150f), height: V(4f, 10f), count: 20, lean: 0.6f, roughness: 0.4f, pad: true)),
			new Preset("Humedal", "Swamp Temple", Make("Humedal", BiomeLandformForm.Pan, density: 1f, perScene: 1, size: V(300f, 1500f), height: V(0.5f, 1.5f), top: 0.85f,
				anchor: BiomeLandformAnchor.Lowest, slope: V(0f, 4f), paint: 4, part: BiomeLandformPart.Floor)),

			// Runaway Greenhouse Plain (decision 89): a touching cluster of pancake domes with their summit pits, the arachnoid's ring and
			// outer trough, the canale's constant-width channel edge to edge (its stored line), the airburst splotch, the shield field
			// (power-law sizes), the halo crater. Not built: the farrum's radial grooves and ring crack, the arachnoid's web of radial
			// fractures and second trough, the splotch's shattered core, the halo's dark parabola opening west (paint its ejecta meanwhile).
			new Preset("Farrum", "Runaway Greenhouse Plain", Make("Farrum", BiomeLandformForm.Platform, BiomeLandformPlacement.Chain, density: 0.2f, perScene: 1,
				size: V(150f, 500f), length: V(300f, 900f), height: V(15f, 45f), top: 0.7f, crown: 0.06f, crownDepth: 8f, count: 4, lean: 0.3f, roughness: 0.3f,
				paint: 1, part: BiomeLandformPart.Top, pad: true)),
			new Preset("Arachnoid", "Runaway Greenhouse Plain", Make("Arachnoid", BiomeLandformForm.Ring, density: 0.1f, perScene: 1, size: V(300f, 900f),
				width: V(60f, 160f), height: V(4f, 10f), moat: 15f, moatDepth: 3f, roughness: 0.3f, pad: true)),
			new Preset("Canale", "Runaway Greenhouse Plain", Make("Canale", BiomeLandformForm.Meander, density: 0.1f, perScene: 1, length: V(5000f, 20000f),
				width: V(15f, 40f), height: V(2f, 5f), top: 0.8f, rim: 1.5f, anchor: BiomeLandformAnchor.Highest, paint: 6, part: BiomeLandformPart.Floor)),
			new Preset("Splotch", "Runaway Greenhouse Plain", Make("Splotch", BiomeLandformForm.Hollow, density: 0.1f, perScene: 2, size: V(200f, 700f),
				height: V(0.2f, 0.5f), paint: 4, part: BiomeLandformPart.Whole, pad: true)),
			new Preset("ShieldField", "Runaway Greenhouse Plain", Make("ShieldField", BiomeLandformForm.Cone, density: 10f, perPatch: 25, size: V(40f, 160f),
				height: V(3f, 12f), crown: 0.06f, crownDepth: 3f, paint: 5, part: BiomeLandformPart.Flank).PowerLaw(2f, false)),
			new Preset("HaloCrater", "Runaway Greenhouse Plain", Make("HaloCrater", BiomeLandformForm.Impact, density: 0.1f, perScene: 1, size: V(300f, 700f),
				height: V(0.18f, 0.18f), rim: 0.04f, top: 0.25f, paint: 4, part: BiomeLandformPart.Ejecta, pad: true)),

			// Tidal Fracture (decision 90): crossing sets of double ridges with their medial trough (a younger set carved over an older),
			// cycloids (cusp pads), the chaos field's hummocky matrix with its rafts on it (author ChaosRafts right after it), lenticulae
			// (domes, with their pits as LenticulaPits), the pull-apart band, the strike-slip trace that shifts every line before it.
			// Not built: the rafts' tilt, the band's striations, scatter aligned along a line, ways shifted by the trace (only landforms are).
			new Preset("DoubleRidge", "Tidal Fracture", Make("DoubleRidge", BiomeLandformForm.Straight, density: 2f, perPatch: 8, length: V(600f, 3000f),
				width: V(60f, 160f), height: V(6f, 20f), top: 0.35f, crown: 0.35f, crownDepth: 0.5f, roughness: 0.2f, paint: 4, part: BiomeLandformPart.Crest)),
			new Preset("Cycloid", "Tidal Fracture", Make("Cycloid", BiomeLandformForm.Cycloid, density: 0.3f, perScene: 2, size: V(200f, 700f), width: V(40f, 80f),
				height: V(3f, 8f), top: 0.35f, crown: 0.35f, crownDepth: 0.5f, count: 9, pad: true)),
			new Preset("ChaosField", "Tidal Fracture", Make("ChaosField", BiomeLandformForm.Pan, density: 0.3f, perScene: 2, size: V(400f, 1500f), height: V(3f, 12f),
				top: 0.8f, roughness: 0.8f, paint: 2, part: BiomeLandformPart.Floor)),
			new Preset("ChaosRafts", "Tidal Fracture", Make("ChaosRafts", BiomeLandformForm.Table, density: 20f, perPatch: 20, size: V(20f, 200f), height: V(0.5f, 3f),
				top: 0.8f, roughness: 0.3f, anchor: BiomeLandformAnchor.OnTopOf, paint: 3, part: BiomeLandformPart.Top)),
			new Preset("Lenticulae", "Tidal Fracture", Make("Lenticulae", BiomeLandformForm.Dome, density: 6f, perPatch: 12, size: V(15f, 80f), height: V(2f, 8f),
				roughness: 0.3f, paint: 6, part: BiomeLandformPart.Whole)),
			new Preset("LenticulaPits", "Tidal Fracture", Make("LenticulaPits", BiomeLandformForm.Pit, density: 6f, perPatch: 12, size: V(15f, 80f), height: V(2f, 8f),
				top: 0.5f, paint: 6, part: BiomeLandformPart.Whole, pad: true)),
			new Preset("DilationalBand", "Tidal Fracture", Make("DilationalBand", BiomeLandformForm.Graben, density: 0.3f, perScene: 2, length: V(1000f, 4000f),
				width: V(80f, 300f), height: V(1f, 2f), top: 0.8f, paint: 2, part: BiomeLandformPart.Floor)),
			new Preset("StrikeSlip", "Tidal Fracture", Shift(Make("StrikeSlip", BiomeLandformForm.Straight, density: 0.2f, perScene: 1, length: V(2000f, 6000f),
				width: V(10f, 30f), paint: 2, part: BiomeLandformPart.Whole, pad: true), V(20f, 80f))),

			// Tholin Plain (decision 91; WornCraters is Radiation Plain's DegradedCrater, reused on thin air): Titan's seif sea with its
			// Y-forks and its wakes behind the hummocks (author IceHummock before it), the drowned crater breached upwind, the city's
			// seat, the frost-capped ridges of thin air. Not built: the seat held to the widest corridor and its windward sand ramp.
			new Preset("IceHummock", "Tholin Plain", Make("IceHummock", BiomeLandformForm.Oblong, density: 0.5f, perPatch: 3, size: V(20f, 80f), length: V(150f, 500f),
				height: V(10f, 40f), top: 0.2f, roughness: 0.3f, align: BiomeLandformAlign.Wind, pad: true)),
			new Preset("TitanSeif", "Tholin Plain", Make("TitanSeif", BiomeLandformForm.Linear, Field, density: 1f, width: V(900f, 2000f), height: V(40f, 120f),
				roughness: 0.2f, align: BiomeLandformAlign.Wind, air: BiomeAtmosphereRequirement.Standard | BiomeAtmosphereRequirement.Thick, paint: 2,
				part: BiomeLandformPart.Floor).Forked(V(2000f, 6000f)).Waked(V(3f, 6f))),
			new Preset("DrownedCrater", "Tholin Plain", Make("DrownedCrater", BiomeLandformForm.Impact, density: 0.1f, perScene: 1, size: V(600f, 1500f),
				height: V(0.11f, 0.11f), rim: 0.035f, top: 0.4f, align: BiomeLandformAlign.Wind,
				air: BiomeAtmosphereRequirement.Standard | BiomeAtmosphereRequirement.Thick, pad: true).Breached(-0.8f)),
			new Preset("GatelessPad", "Tholin Plain", Make("GatelessPad", BiomeLandformForm.Platform, density: 0.1f, perScene: 1, size: V(350f, 550f), height: V(1f, 3f),
				top: 0.85f, align: BiomeLandformAlign.Wind, pad: true)),
			new Preset("FrostcapRidges", "Tholin Plain", Make("FrostcapRidges", BiomeLandformForm.Straight, density: 0.5f, perPatch: 5, length: V(400f, 900f),
				width: V(120f, 300f), height: V(30f, 90f), roughness: 0.3f, align: BiomeLandformAlign.SceneAxis, air: BiomeAtmosphereRequirement.Thin, paint: 6,
				part: BiomeLandformPart.Crest)),

			// Underwater Canyon (decision 92): the canyon from its head down to the deep (its stored line), then on it the cyclic steps
			// down its floor, gullies down both walls, slides off its walls, and the fan valley at its mouth (author them after it).
			// Not built: inner terraces on the bends, its dendritic tributaries, a hanging one.
			new Preset("SubmarineCanyon", "Underwater Canyon", Make("SubmarineCanyon", BiomeLandformForm.Canyon, density: 1f, perScene: 1, length: V(4000f, 15000f),
				width: V(600f, 3000f), height: V(150f, 700f), top: 0.12f, anchor: BiomeLandformAnchor.Highest, paint: 1, part: BiomeLandformPart.Floor, pad: true)),
			new Preset("CyclicSteps", "Underwater Canyon", Make("CyclicSteps", BiomeLandformForm.Treads, width: V(30f, 120f), height: V(1f, 4f),
				anchor: BiomeLandformAnchor.OnTopOf, paint: 1, part: BiomeLandformPart.Crest)),
			new Preset("WallGullies", "Underwater Canyon", Make("WallGullies", BiomeLandformForm.Grooves, length: V(20f, 60f), width: V(6f, 20f), height: V(2f, 8f),
				anchor: BiomeLandformAnchor.OnFlankOf)),
			new Preset("WallSlide", "Underwater Canyon", Make("WallSlide", BiomeLandformForm.Slide, perScene: 2, perPatch: 2, size: V(150f, 400f), length: V(300f, 800f),
				height: V(20f, 60f), anchor: BiomeLandformAnchor.OnFlankOf, align: BiomeLandformAlign.HostDownhill, paint: 5, part: BiomeLandformPart.Whole)),
			new Preset("CanyonFan", "Underwater Canyon", Make("CanyonFan", BiomeLandformForm.Meander, length: V(1000f, 4000f), width: V(200f, 500f), height: V(10f, 40f),
				top: 0.6f, rim: 8f, anchor: BiomeLandformAnchor.OnTopOf, paint: 6, part: BiomeLandformPart.Rim)),

			// Volcanic (decision 93): the shield's summit caldera with its step-fault benches and the fire pit in its floor (right after
			// it), the rift zone's open crack (its stored line) with the pit craters along it (right after it), breached cinder cones,
			// ʻaʻā tongues with their channel, tumuli, kīpuka groves of older ground, a fresh breakout. Not built: the cones' 40 % breach
			// share (every one is breached), the spatter-cone rows on the rift.
			new Preset("ShieldCaldera", "Volcanic", Steps(Make("ShieldCaldera", BiomeLandformForm.Caldera, density: 0.1f, perScene: 1, size: V(1500f, 4000f),
				height: V(0.04f, 0.07f), top: 0.65f, rim: 0.01f, count: 3, anchor: BiomeLandformAnchor.Highest, pad: true), 3)),
			new Preset("FirePit", "Volcanic", Make("FirePit", BiomeLandformForm.Pit, perScene: 1, size: V(150f, 400f), height: V(60f, 150f), top: 0.6f,
				anchor: BiomeLandformAnchor.OnTopOf, pad: true)),
			new Preset("RiftZone", "Volcanic", Make("RiftZone", BiomeLandformForm.Straight, density: 0.3f, perScene: 2, length: V(500f, 3000f), width: V(2f, 6f),
				height: V(-15f, -3f), roughness: 0.4f, pad: true)),
			new Preset("PitCraterChain", "Volcanic", Make("PitCraterChain", BiomeLandformForm.PitChain, perScene: 2, width: V(100f, 500f), height: V(30f, 120f),
				top: 0.7f, anchor: BiomeLandformAnchor.OnTopOf, paint: 8, part: BiomeLandformPart.Flank, pad: true)),
			new Preset("CinderCone", "Volcanic", Make("CinderCone", BiomeLandformForm.Cone, density: 0.3f, perScene: 3, size: V(300f, 900f), height: V(30f, 150f),
				crown: 0.3f, crownDepth: 20f, roughness: 0.2f, align: BiomeLandformAlign.Wind, paint: 4, part: BiomeLandformPart.Flank, pad: true).Breached(0.8f)),
			new Preset("AaTongue", "Volcanic", Make("AaTongue", BiomeLandformForm.Tongue, density: 0.3f, perScene: 4, size: V(50f, 300f), length: V(300f, 2000f),
				height: V(2f, 6f), crown: 0.15f, crownDepth: 1.5f, roughness: 0.5f, align: BiomeLandformAlign.Downslope, paint: 1, part: BiomeLandformPart.Top).MoltenLine()),
			new Preset("Tumuli", "Volcanic", Make("Tumuli", BiomeLandformForm.Dome, density: 20f, perScene: 120, size: V(10f, 40f), height: V(1f, 5f), top: 0.3f,
				roughness: 0.3f, slope: V(0f, 8f))),
			new Preset("KipukaGrove", "Volcanic", Make("KipukaGrove", BiomeLandformForm.Platform, density: 0.5f, size: V(40f, 250f), height: V(0.5f, 3f), top: 0.6f,
				roughness: 0.5f, paint: 5, part: BiomeLandformPart.Whole)),
			new Preset("Breakout", "Volcanic", Make("Breakout", BiomeLandformForm.Meander, density: 0.2f, perScene: 3, length: V(100f, 600f), width: V(10f, 40f),
				height: V(-1.5f, -0.5f), top: 0.5f, paint: 6, part: BiomeLandformPart.Channel).MoltenLine()),

			// Volcanic Temple (decision 95): the summit terrace and its row of vent mouths (stored as their sites, right after it), the
			// sciara down one flank (its stored chute line), the fossa crater, the obsidian flow, the precinct's dry stepped terraces.
			// Not built: the sciara kept away from the precinct and out of the road planner, the fossa on the second-highest patch.
			new Preset("SummitTerrace", "Volcanic Temple", Make("SummitTerrace", BiomeLandformForm.Pan, density: 0.1f, perScene: 1, size: V(150f, 300f),
				length: V(250f, 500f), height: V(20f, 60f), top: 0.75f, anchor: BiomeLandformAnchor.Highest, pad: true)),
			new Preset("VentMouths", "Volcanic Temple", Make("VentMouths", BiomeLandformForm.Pit, perPatch: 4, size: V(20f, 60f), height: V(8f, 20f), top: 0.3f,
				rim: 1.5f, anchor: BiomeLandformAnchor.OnTopOf, pad: true)),
			new Preset("Sciara", "Volcanic Temple", Make("Sciara", BiomeLandformForm.Graben, density: 0.1f, perScene: 1, length: V(800f, 2500f), width: V(300f, 900f),
				height: V(20f, 80f), top: 0.7f, anchor: BiomeLandformAnchor.Highest, align: BiomeLandformAlign.Downslope, paint: 4, part: BiomeLandformPart.Floor)),
			new Preset("FossaCrater", "Volcanic Temple", Make("FossaCrater", BiomeLandformForm.Caldera, density: 0.1f, perScene: 1, size: V(300f, 500f),
				height: V(0.18f, 0.25f), top: 0.45f, rim: 0.04f, count: 4, anchor: BiomeLandformAnchor.Highest, paint: 5, part: BiomeLandformPart.Floor, pad: true)),
			new Preset("GlassFlow", "Volcanic Temple", Make("GlassFlow", BiomeLandformForm.Tongue, density: 0.2f, perScene: 2, size: V(100f, 300f), length: V(300f, 800f),
				height: V(15f, 40f), roughness: 0.7f, align: BiomeLandformAlign.Downslope, paint: 4, part: BiomeLandformPart.Top)),
			new Preset("PrecinctTerraces", "Volcanic Temple", Make("PrecinctTerraces", BiomeLandformForm.Rimstone, density: 0.2f, perScene: 2, size: V(40f, 80f),
				length: V(80f, 200f), height: V(6f, 12f), count: 4, align: BiomeLandformAlign.Downslope, slope: V(15f, 30f), pad: true)),
			// The ember rill (decision 95, 2026-10-25, I2r): a narrow runnel of lava wandering down from the summit, drawn molten.
			new Preset("EmberRill", "Volcanic Temple", Make("EmberRill", BiomeLandformForm.Meander, density: 0.2f, perScene: 3, length: V(80f, 400f), width: V(1.5f, 4f),
				height: V(-0.8f, -0.3f), top: 0.5f, anchor: BiomeLandformAnchor.Highest, paint: 6, part: BiomeLandformPart.Channel).MoltenLine()),

			// Subsurface Ocean Vent (decision 83): the sea hall's floor forms (the brine lake's machinery with a hot look; collapse pits
			// round a basalt pillar). They carve terrain as every landform does; the hall floor reads them once the sea halls' cave
			// floor carve does (phase I2n-b).
			new Preset("HotBrinePool", "Subsurface Ocean Vent", Make("HotBrinePool", BiomeLandformForm.Pan, density: 0.5f, perScene: 2, size: V(10f, 40f),
				height: V(1f, 3f), top: 0.8f, rim: 0.5f, datumDepth: V(0.5f, 2f), paint: 5, part: BiomeLandformPart.Rim, pad: true)),
			new Preset("LavaPillars", "Subsurface Ocean Vent", Make("LavaPillars", BiomeLandformForm.Tank, density: 1f, perScene: 3, size: V(3f, 8f), height: V(2f, 5f),
				crown: 0.35f)),

			// Scrubland (decision 77): the sunken oval court a ritual site stands in (its pad).
			new Preset("OvalCourt", "Scrubland", Make("OvalCourt", BiomeLandformForm.Pan, density: 0.1f, perScene: 2, size: V(25f, 40f), length: V(40f, 60f),
				height: V(1f, 2f), top: 0.8f, rim: 2.5f, slope: V(0f, 6f), pad: true)),

			// Wasteland (decision 96): the dried mere, the dead river's stored channel, the dish of the Dolorous Stroke with its radial
			// fissures, blowout dunes on a quarter of a patch (a lone dune field's density is its share), a hollow way. Not built: the
			// hollow way as a cut-road profile the ways follow (it is a sunken lane of its own until the roads carve one).
			new Preset("DeadMere", "Wasteland", Make("DeadMere", BiomeLandformForm.Pan, density: 0.5f, perScene: 3, size: V(80f, 400f), height: V(1f, 3f), top: 0.85f,
				anchor: BiomeLandformAnchor.Lowest, slope: V(0f, 5f), paint: 3, part: BiomeLandformPart.Floor, pad: true)),
			new Preset("DeadRiver", "Wasteland", Make("DeadRiver", BiomeLandformForm.Meander, density: 0.3f, perScene: 2, length: V(1000f, 4000f), width: V(6f, 20f),
				height: V(1f, 3f), top: 0.7f, roughness: 0.3f, anchor: BiomeLandformAnchor.Highest)),
			new Preset("StrokeScar", "Wasteland", Make("StrokeScar", BiomeLandformForm.Star, density: 0.1f, perScene: 1, size: V(150f, 300f), height: V(-2f, -1f),
				count: 11, paint: 6, part: BiomeLandformPart.Whole, pad: true)),
			new Preset("BlowoutDunes", "Wasteland", Make("BlowoutDunes", BiomeLandformForm.Parabolic, Field, density: 0.25f, width: V(40f, 120f), height: V(1f, 5f),
				align: BiomeLandformAlign.Wind, paint: 1, part: BiomeLandformPart.Sand)),
			new Preset("HollowWay", "Wasteland", Make("HollowWay", BiomeLandformForm.Meander, density: 0.3f, perScene: 2, length: V(400f, 2000f), width: V(4f, 8f),
				height: V(1f, 4f), top: 0.6f, anchor: BiomeLandformAnchor.Highest)),

			// Generic shapes a round may start from.
			new Preset("Mesa", "(generic)", Make("Mesa", BiomeLandformForm.Table, density: 0.2f, size: V(100f, 600f), height: V(20f, 120f), top: 0.8f, rim: 1f,
				pad: true)),
			new Preset("Mound", "(generic)", Make("Mound", BiomeLandformForm.Dome, density: 0.5f, size: V(10f, 40f), height: V(1f, 5f))),
		};

		/// <summary>Other names the drafts used for a preset (normalised key → preset name).</summary>
		private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
		{
			{ "palaeolake", "Paleolake" },
			{ "malpaís", "Malpais" },
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
			// Decision 53's names for Enceladus' stripes (I2q-b).
			{ "sulcus", "TigerStripe" },
			{ "sulci", "TigerStripe" },
			// 2026-10-25, I2g-d: the last drafts' other names.
			{ "sor", "SaltPan" },
			{ "takyr", "Playa" },
			{ "oba", "ObaChain" },
			{ "bobak", "BobakMounds" },
			{ "marmotmound", "BobakMounds" },
			{ "tessera", "TesseraWeave" },
			{ "pancakedome", "Farrum" },
			{ "farra", "Farrum" },
			{ "airburstscar", "Splotch" },
			{ "pullapartband", "DilationalBand" },
			{ "pitanddome", "Lenticulae" },
			{ "lenticula", "Lenticulae" },
			{ "seifdune", "TitanSeif" },
			{ "selk", "DrownedCrater" },
			{ "canyon", "SubmarineCanyon" },
			{ "cyclicstep", "CyclicSteps" },
			{ "aa", "AaTongue" },
			{ "lavarill", "EmberRill" },
			{ "pitcrater", "PitCraterChain" },
			{ "terrazza", "SummitTerrace" },
			{ "lavapillar", "LavaPillars" },
			{ "mere", "DeadMere" },
			{ "hollowlane", "HollowWay" },
			{ "dolorousstroke", "StrokeScar" },
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
