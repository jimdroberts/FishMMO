using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The shape families a biome's sculpted landforms are carved from (2026-10-18, phase I2g, decisions 25, 26, 36(e), 38–46): a
	/// handful of parametric shapes, each a pure function of where a landform was placed and the point asked (BiomeLandforms).
	/// </summary>
	/// <remarks>
	/// A family is the high nibble of a <see cref="BiomeLandformForm"/>, so the form alone says which shape function draws it.
	/// </remarks>
	public enum BiomeLandformFamily : byte
	{
		/// <summary>A raised mound: dome, cone, flat-topped platform, keyhole, oblong pillow or star; a summit pit and a moat ring as options.</summary>
		Mound = 0,
		/// <summary>An impact structure: bowl, raised rim, ejecta blanket and its rays, a central peak when large; or a ghost crater filled flat.</summary>
		Crater = 1,
		/// <summary>A hollow: a flat-floored pan, a sheer pit, a dish, or the low ground of a patch filled flat to its spill level.</summary>
		Depression = 2,
		/// <summary>A table or stair: a flat-topped mesa with a caprock lip, or rimstone treads stepping down a slope.</summary>
		Mesa = 3,
		/// <summary>A long raised line: a straight ridge, an arc, a ridge following the ground's contour, a ring reef, or a one-sided scarp.</summary>
		Ridge = 4,
		/// <summary>Cut lines: a dendritic creek network, a meandering valley, short downslope grooves, a stone river, a pass through a ring.</summary>
		Channels = 5,
		/// <summary>A tongue of moved ground: an alluvial fan, a lobe with a steep front, a slide scar with its deposit, lobate flow steps.</summary>
		Lobe = 6,
		/// <summary>A dune field over a patch, by the scene's wind: transverse, barchan, linear (seif), star or parabolic.</summary>
		Dunes = 7,
		/// <summary>A periodic field over a patch: sediment waves, frost polygons, slope rills.</summary>
		Waves = 8,
	}

	/// <summary>
	/// One landform shape: its family (<see cref="BiomeLandforms.FamilyOf"/>, the value's high nibble) and the form within it. Stored by
	/// value in assets: append only.
	/// </summary>
	public enum BiomeLandformForm : byte
	{
		/// <summary>Mound: a smooth dome (pingo, nebkha, barrow, mud-volcano flank when coned).</summary>
		Dome = 0x00,
		/// <summary>Mound: a straight-flanked cone (mud volcano, conical mound).</summary>
		Cone = 0x01,
		/// <summary>Mound: a flat top at a set height above the ground at its centre (terp, warft, motte, tell, platform mound, dibin).</summary>
		Platform = 0x02,
		/// <summary>Mound: a keyhole, a round rear mound joined to a squared front (kofun).</summary>
		Keyhole = 0x03,
		/// <summary>Mound: a long low pillow (pillow mound, palsa).</summary>
		Oblong = 0x04,
		/// <summary>Mound: a pyramid with radiating arms (star dune as a single draa).</summary>
		Star = 0x05,
		/// <summary>
		/// Mound (2026-10-25, phase I2g-b): a sheer tower with fluted flanks at <see cref="BiomeLandform.Flank"/> degrees and a rounded
		/// cap (<see cref="BiomeLandform.Top"/>: 0 flat … 1 a full dome); <see cref="BiomeLandform.Count"/> flutes round it, a foot notch as
		/// its moat (fenglin tower, inselberg, the winter wall's ice front, a lodestone crag).
		/// </summary>
		Tower = 0x06,
		/// <summary>
		/// Mound (I2g-b): a square-planned stepped pyramid of <see cref="BiomeLandform.Count"/> tiers, its plan as long as
		/// <see cref="BiomeLandform.Length"/>; <see cref="BiomeLandform.Rim"/> faces (1 or 4) carry an axial stair <see cref="BiomeLandform.Width"/>
		/// wide, and its moat is square, with a causeway on each stair's axis (temple mound, temple mountain, moated enclosure).
		/// </summary>
		Pyramid = 0x07,

		/// <summary>Crater: a fresh impact bowl with rim, ejecta, rays and secondaries; a central peak once large.</summary>
		Impact = 0x10,
		/// <summary>Crater: a ghost crater, only its low rim left, its inside filled flat.</summary>
		Ghost = 0x11,
		/// <summary>
		/// Crater (I2g-b, decision 55): a multi-ring basin fitted to its patch: an inward-facing outer scarp at the radius, massif rings
		/// at √2 spacing inward (0.71 and 0.5 of it) with gaps, a sunk floor filled flat with mare to a level, wrinkle ridges on it,
		/// radial sculpture and secondary crater chains in the ejecta, and a few mare pits. Its parts are the zone mask its biome's
		/// grounds and rules band on (Floor = mare, Crest = ridge crests, Melt, Rim = the rings, Knobby, Flank = the scarp, Ejecta, Rays).
		/// </summary>
		MultiRing = 0x12,
		/// <summary>
		/// Crater (I2g-b): a summit caldera on its patch's highest ground: a flat floor (a crater lake), steep inner walls, a rim of
		/// <see cref="BiomeLandform.Count"/> peaks and one outlet notch; no ejecta.
		/// </summary>
		Caldera = 0x13,
		/// <summary>
		/// Crater (2026-10-25, decision 71): an ice ring basin (Valhalla's): a pale, near-flat palimpsest at the middle (a fifth of the
		/// radius) and <see cref="BiomeLandform.Count"/> concentric graben out to the radius, spaced wider outward, each a flat-floored
		/// trough between two inward-facing scarps <see cref="BiomeLandform.Width"/> across and <see cref="BiomeLandform.Height"/> metres
		/// deep, broken into arcs (60–85 % continuous). Its parts: Floor/Top the palimpsest, Channel/Moat the troughs, Flank the scarps.
		/// </summary>
		RingGraben = 0x14,
		/// <summary>
		/// Crater (2026-10-25, decision 72): a rampart crater (Mars's fluidised ejecta, thin air): an Impact bowl and rim without rays,
		/// its ejecta a lobate sheet out to <see cref="BiomeLandform.Crown"/> radii (1 … 2.5) ending in a distal rampart ridge
		/// <see cref="BiomeLandform.CrownDepth"/> metres high; three in ten carry a second, inner lobe (double-layered).
		/// </summary>
		Rampart = 0x15,

		/// <summary>Depression: a pan with a flat floor (playa, palaeolake, brine lake, panne, spring bowl).</summary>
		Pan = 0x20,
		/// <summary>Depression: a pit with sheer sides (blue hole, guelta).</summary>
		Pit = 0x21,
		/// <summary>Depression: a dished hollow, or a cluster of them (wallow, blowout).</summary>
		Hollow = 0x22,
		/// <summary>Depression: every closed low of the patch filled flat to its spill level (dust-fill).</summary>
		SpillFill = 0x23,
		/// <summary>
		/// Depression (I2g-b): a rectangular embanked reservoir: a flat floor below the ground, earth dykes <see cref="BiomeLandform.Rim"/>
		/// metres high round it, an island at its middle (<see cref="BiomeLandform.Crown"/> of the width, <see cref="BiomeLandform.CrownDepth"/>
		/// metres above the ground) (baray).
		/// </summary>
		Tank = 0x24,

		/// <summary>Mesa: a flat table at a set height with steep flanks and a caprock lip (mesa, amba, kipuka, crag).</summary>
		Table = 0x30,
		/// <summary>Mesa: rimstone treads stepping down a slope, each rimmed into a pool (travertine terraces).</summary>
		Rimstone = 0x31,
		/// <summary>
		/// Mesa (2026-10-25, decision 74): a wave-cut platform as a field over its patch: the ground within three metres of the sea's
		/// surface cut to stepped flag treads (<see cref="BiomeLandform.Height"/> each) between half a metre below and above it.
		/// </summary>
		Bench = 0x32,

		/// <summary>Ridge: a straight ridge (sarn, abyssal hill, levee).</summary>
		Straight = 0x40,
		/// <summary>Ridge: an arc of a circle (siege bank and ditch).</summary>
		Arc = 0x41,
		/// <summary>Ridge: a ridge following the ground's contour (lateral moraine, foredune, sea dike).</summary>
		Contour = 0x42,
		/// <summary>Ridge: a closed ring rising round a filled lagoon (faru ring reef).</summary>
		Ring = 0x43,
		/// <summary>Ridge: a one-sided step, the ground beyond it raised (terrace scarp).</summary>
		Scarp = 0x44,
		/// <summary>
		/// Ridge (2026-10-25, decision 76): a graben, a flat-floored trough (<see cref="BiomeLandform.Top"/> its floor's share) between
		/// two inward-facing scarps along a stored line, <see cref="BiomeLandform.Height"/> metres deep: straight, or an arc of a circle
		/// <see cref="BiomeLandform.Size"/> across when one is set; <see cref="BiomeLandform.Count"/> parallel troughs
		/// <see cref="BiomeLandform.Moat"/> metres apart (an arcuate rille's set). Its parts: Floor, Wall/Scarp/Flank, Rim the shoulders.
		/// </summary>
		Graben = 0x45,

		/// <summary>Channels: a branching creek network grown up the slope from its outlet (tidal creeks).</summary>
		Dendritic = 0x50,
		/// <summary>Channels: one meandering channel down the slope, flat-floored and leveed as set (fan valley, wash).</summary>
		Meander = 0x51,
		/// <summary>Channels: short straight grooves down the slope, each ending at a resting boulder (boulder tracks).</summary>
		Grooves = 0x52,
		/// <summary>Channels: a channel of tumbled boulders from high ground to the lowest (stone river).</summary>
		StoneRiver = 0x53,
		/// <summary>Channels: passes cut through the biome's ring landforms (kandu).</summary>
		Pass = 0x54,
		/// <summary>
		/// Channels (I2g-b): a string of sheer pits along a meandering line down the slope, rock bridges left between them (a lava tube's
		/// skylight chain); every pit a pad.
		/// </summary>
		PitChain = 0x55,
		/// <summary>
		/// Channels (I2g-b, decision 64): <see cref="BiomeLandform.Count"/> troughs spiralling out from the place (Mars's polar troughs),
		/// each with one steep scarp (<see cref="BiomeLandform.Lean"/>), out to half its <see cref="BiomeLandform.Size"/>.
		/// </summary>
		Spiral = 0x56,
		/// <summary>
		/// Channels (2026-10-25, decision 74): a geo, a sheer slot from its shore place straight inland <see cref="BiomeLandform.Length"/>,
		/// floored under the sea (<see cref="BiomeLandform.Datum"/> SeaSurface) and rising at its head to the beach.
		/// </summary>
		Geo = 0x57,
		/// <summary>
		/// Channels (2026-10-25, decision 76): a sinuous rille, one meandering trench from its head (the patch's highest ground) down
		/// the slope, narrowing to half its width at its toe and shallowing out there; U-floored by <see cref="BiomeLandform.Top"/>, a
		/// levee lip <see cref="BiomeLandform.Rim"/> metres high; a vent hollow at its head <see cref="BiomeLandform.CrownDepth"/> times
		/// its depth (0 none); beaded when <see cref="BiomeLandform.Crown"/> is set (necks that share of its width every three widths).
		/// Its parts: Floor, Wall/Scarp/Flank, Rim the levees, Head the vent hollow.
		/// </summary>
		Rille = 0x58,

		/// <summary>Lobe: an alluvial fan spread down from a slope break.</summary>
		Fan = 0x60,
		/// <summary>Lobe: a tongue thickening down the slope to a steep front (rock glacier, solifluction lobe, cornice lip).</summary>
		Tongue = 0x61,
		/// <summary>Lobe: a headwall scar with its deposit below (slide scar, thaw slump).</summary>
		Slide = 0x62,
		/// <summary>Lobe: overlapping lobate fronts over a patch as low steps (flow-lobe steps).</summary>
		FlowSteps = 0x63,

		/// <summary>Dunes: crests across the wind.</summary>
		Transverse = 0x70,
		/// <summary>Dunes: crescents, horns downwind.</summary>
		Barchan = 0x71,
		/// <summary>Dunes: long serrated ridges along the wind with corridors between (seif, gassi).</summary>
		Linear = 0x72,
		/// <summary>Dunes: star dunes on a lattice, arms radiating.</summary>
		StarDune = 0x73,
		/// <summary>Dunes: parabolic dunes, arms trailing upwind, slip face downwind.</summary>
		Parabolic = 0x74,

		/// <summary>Waves: long crests along the contour (sediment waves).</summary>
		SedimentWaves = 0x80,
		/// <summary>Waves: low-centred polygons with raised rims.</summary>
		Polygons = 0x81,
		/// <summary>Waves: rills down every slope, knife crests between.</summary>
		Rills = 0x82,
		/// <summary>Waves: parallel rows of steep-sided rock fins along the axis (fin field).</summary>
		Fins = 0x83,
		/// <summary>Waves: narrow deep slots across the slope, a field of crevasses (glacier).</summary>
		Crevasses = 0x84,
		/// <summary>
		/// Waves (I2g-b, decision 61): a cone field on a jittered lattice, flanks at <see cref="BiomeLandform.Flank"/> degrees, the closed
		/// star-shaped hollows between them floored flat (fengcong cockpit karst).
		/// </summary>
		Cockpits = 0x85,
		/// <summary>
		/// Waves (I2g-b, decision 62): a dissolved plateau: flat-topped mesas in a warped cellular net, flat-floored valleys cut between
		/// them to the field's height (Titan's labyrinth terrain).
		/// </summary>
		Labyrinth = 0x86,
		/// <summary>
		/// Waves (I2g-b, decision 66): warped convection cells (Sputnik's): each cell's middle swelled by the field's height, a trough
		/// <see cref="BiomeLandform.Moat"/> metres wide and <see cref="BiomeLandform.MoatDepth"/> deep along every edge; its parts the
		/// troughs (Channel, Moat), the interiors (Top) and the triple junctions (Junction).
		/// </summary>
		Cells = 0x87,
	}

	/// <summary>How a landform's places are chosen within each patch its biome paints.</summary>
	public enum BiomeLandformPlacement : byte
	{
		/// <summary>Scattered at <see cref="BiomeLandform.Density"/> per km² of the patch, kept apart.</summary>
		Scatter = 0,
		/// <summary>One fitted to each patch (at its anchor), its diameter <see cref="BiomeLandform.FitShare"/> of the patch's.</summary>
		Fitted = 1,
		/// <summary>A field covering the whole patch (dunes, waves, flow steps, spill fill).</summary>
		Field = 2,
		/// <summary>
		/// A chain (2026-10-25, decision 71): each place found as a scatter's is the middle of a line <see cref="BiomeLandform.Length"/>
		/// long (straight, or an arc when <see cref="BiomeLandform.Lean"/> ≠ 0: the share of a half turn it bends through) along which
		/// <see cref="BiomeLandform.Count"/> sites of the form stand, sized from <see cref="BiomeLandform.Size"/> and tapering to both
		/// ends, set off the line by <see cref="BiomeLandform.Roughness"/> of their size (a catena of merged craters, the knobs left on a
		/// vanished crater's rim). Each member is an ordinary site of the form (its own pads and masks); members of one chain may overlap.
		/// </summary>
		Chain = 3,
	}

	/// <summary>Where in a patch a scattered or fitted landform stands.</summary>
	public enum BiomeLandformAnchor : byte
	{
		/// <summary>Anywhere its conditions allow.</summary>
		Anywhere = 0,
		/// <summary>The patch's centroid.</summary>
		Centroid = 1,
		/// <summary>The patch's lowest ground.</summary>
		Lowest = 2,
		/// <summary>The patch's shore, where its ground meets the sea's surface.</summary>
		Shore = 3,
		/// <summary>A slope break: ground inside the slope band whose downslope is markedly gentler (a gorge mouth, a gorge head).</summary>
		SlopeBreak = 4,
		/// <summary>Within <see cref="BiomeLandform.NearWater"/> metres of a river or lake.</summary>
		NearWater = 5,
		/// <summary>The patch's highest ground.</summary>
		Highest = 6,
		/// <summary>
		/// In the lee of each site of the entry just before it in the list (2026-10-25, decision 64): downwind of it by its radius and
		/// half its own length (a blue-ice tail behind each nunatak). Placed after that entry, one for each of its sites.
		/// </summary>
		LeeOf = 7,
		/// <summary>
		/// <see cref="BiomeLandform.NearWater"/> metres inside the patch's edge (2026-10-25, decision 70: the steps round a wet ice
		/// cap's margin), measured across the patch's cells; the scene's edge is not a patch edge.
		/// </summary>
		PatchEdge = 8,
		/// <summary>
		/// <see cref="BiomeLandform.NearWater"/> metres from the shoreline (2026-10-25, decision 74): inland (a gloup behind the cliff),
		/// or out to sea for an entry with a <see cref="BiomeLandform.Depth"/> band (a stack off the cliff, a tidal brough).
		/// </summary>
		ShoreSetback = 9,
		/// <summary>
		/// Across a bay's mouth (2026-10-25, decision 74): shallow water between two shores at most <see cref="BiomeLandform.Length"/>
		/// apart; a line entry lies along that span (an ayre closing an oyce).
		/// </summary>
		BayMouth = 10,
	}

	/// <summary>Which way a landform's long axis (or its wind frame) lies.</summary>
	public enum BiomeLandformAlign : byte
	{
		/// <summary>Each its own way, by its hash.</summary>
		Random = 0,
		/// <summary>Along the scene's prevailing wind (downwind).</summary>
		Wind = 1,
		/// <summary>Down the ground's slope at its place.</summary>
		Downslope = 2,
		/// <summary>Across the slope, along the contour.</summary>
		Contour = 3,
		/// <summary>One axis for the whole scene, by its seed (parallel abyssal hills).</summary>
		SceneAxis = 4,
		/// <summary>Square to the compass, its axis east (2026-10-25: a temple's enclosure and reservoir), within a degree.</summary>
		Cardinal = 5,
		/// <summary>Its axis north–south, within a degree (2026-10-25: bladed ridges' strike).</summary>
		Meridian = 6,
	}

	/// <summary>What a landform's heights are measured from.</summary>
	public enum BiomeLandformDatum : byte
	{
		/// <summary>From the ground it stands on.</summary>
		Ground = 0,
		/// <summary>A crest or floor at <see cref="BiomeLandform.DatumDepth"/> metres below the sea's surface (negative: above it).</summary>
		SeaSurface = 1,
	}

	/// <summary>The part of a landform its paint mask covers (<see cref="BiomeLandform.PaintPart"/>).</summary>
	public enum BiomeLandformPart : byte
	{
		/// <summary>All of it.</summary>
		Whole = 0,
		/// <summary>Its top: a mound's or table's flat summit.</summary>
		Top = 1,
		/// <summary>Its floor: a pan's, crater's or ring's inside, a dune field's corridors, the filled ground of a spill fill.</summary>
		Floor = 2,
		/// <summary>Its flanks and faces.</summary>
		Flank = 3,
		/// <summary>Its rim, lip or levees.</summary>
		Rim = 4,
		/// <summary>Its moat or ditch.</summary>
		Moat = 5,
		/// <summary>A crater's ray streaks.</summary>
		Rays = 6,
		/// <summary>Bright streaks down a dome's flanks (cryodome).</summary>
		Streaks = 7,
		/// <summary>A crater's ejecta blanket.</summary>
		Ejecta = 8,
		/// <summary>A channel's bed.</summary>
		Channel = 9,
		/// <summary>Dune sand deep enough to keep the slope sand (up to about 40°).</summary>
		Sand = 10,
		/// <summary>A ridge's or dune's crest.</summary>
		Crest = 11,
		/// <summary>The faces turned downwind: a dune's or a wave field's slip faces, a mound's lee flank (2026-10-25, decision 64).</summary>
		Lee = 12,
		/// <summary>A multi-ring basin's melt sheet: the ring of ground between its mare's lip and its peak ring (decision 55).</summary>
		Melt = 13,
		/// <summary>A multi-ring basin's knobby ground between its outer ring and its scarp (decision 55).</summary>
		Knobby = 14,
		/// <summary>A cell field's triple junctions, where three of its troughs meet (decision 66).</summary>
		Junction = 15,
		/// <summary>A crater's inner wall, rim crest to floor (2026-10-25, decision 72; the same ground as its Flank).</summary>
		Wall = 16,
		/// <summary>A rille's head: the vent hollow it rises from (decision 76).</summary>
		Head = 17,
		/// <summary>A trough's scarps: a graben's or a rille's walls (decision 76; the same ground as its Wall).</summary>
		Scarp = 18,
	}

	/// <summary>
	/// One sculpted landform a biome carves into its ground at the cut (<see cref="BiomeTemplate.Landforms"/>, 2026-10-18, phase I2g):
	/// its shape (<see cref="Form"/>), how often and where it stands, its sizes, and how its biome paints it. Stored by value; the
	/// rounds author them by name from <see cref="BiomeLandformPresets"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Ranges are (min, max); a landform picks within each by its hash. Several ranges are open at (0, 0): <see cref="Slope"/>,
	/// <see cref="Depth"/>, <see cref="Temperature"/> and <see cref="Humidity"/> then never refuse a place. For the Crater family
	/// <see cref="Height"/> and <see cref="Rim"/> are shares of the crater's diameter (a fresh bowl's depth is about 0.18 of it);
	/// for every other family they are metres.
	/// </para>
	/// <para>
	/// Until 2026-10-18 no ground was sculpted; an empty list (every biome's default) carves nothing, so a cut is unchanged.
	/// </para>
	/// </remarks>
	[Serializable]
	public struct BiomeLandform
	{
		[Tooltip("The landform's name: its preset's (Pingo, Tell, ImpactCrater…), and what marks it in the list.")]
		public string Kind;
		[Tooltip("Its shape: the family (the high nibble) and the form within it.")]
		public BiomeLandformForm Form;
		[Tooltip("Scattered by density, one fitted to each painted patch, or a field over the whole patch.")]
		public BiomeLandformPlacement Placement;
		[Tooltip("Where in a patch it stands.")]
		public BiomeLandformAnchor Anchor;
		[Tooltip("Which way its long axis (or wind frame) lies.")]
		public BiomeLandformAlign Align;
		[Tooltip("How many per km² of the biome's ground (Scatter); a dune field's share of its biome's dune mix (Field).")]
		[Min(0f)] public float Density;
		[Tooltip("Most in one painted patch (0: no limit).")]
		[Min(0)] public int PerPatch;
		[Tooltip("Most in a scene (0: no limit).")]
		[Min(0)] public int PerScene;
		[Tooltip("Fitted: the diameter as a share of the patch's (0: from Size).")]
		[Min(0f)] public float FitShare;
		[Tooltip("Footprint diameter, metres (min, max): a ring's diameter, an arc's radius × 2, a scarp's raised back.")]
		public Vector2 Size;
		[Tooltip("Length along its axis, metres (min, max); 0: round.")]
		public Vector2 Length;
		[Tooltip("Cross width, metres (min, max): a ridge's, ring's or channel's; a dune field's, wave field's or cluster's spacing.")]
		public Vector2 Width;
		[Tooltip("Height or depth, metres (min, max); a share of the diameter for craters.")]
		public Vector2 Height;
		[Tooltip("Share of the radius that is flat top or flat floor, 0 … 1.")]
		[Range(0f, 1f)] public float Top;
		[Tooltip("Flank angle, degrees (0: the form's own).")]
		[Range(0f, 89f)] public float Flank;
		[Tooltip("Rim, caprock lip, levee or ejecta height, metres (a share of the diameter for craters).")]
		public float Rim;
		[Tooltip("A summit pit's radius as a share of the radius (a crater's central peak's).")]
		[Min(0f)] public float Crown;
		[Tooltip("The summit pit's depth, metres (a crater's central peak's height as a share of its diameter).")]
		public float CrownDepth;
		[Tooltip("A moat or ditch round it (beside a ridge), metres wide; 0 none.")]
		[Min(0f)] public float Moat;
		[Tooltip("The moat's depth, metres.")]
		[Min(0f)] public float MoatDepth;
		[Tooltip("How much of the moat lies on the windward side, 0 an even ring … 1 a wind scoop upwind only (a nunatak's); needs Align Wind.")]
		[Range(0f, 1f)] public float MoatBias;
		[Tooltip("Scatter sizes drawn from a power law, N(> D) ∝ D^−power over Size (2: a crater population); 0 an even draw.")]
		[Min(0f)] public float SizePower;
		[Tooltip("Impact craters age by their hash (fresh 6 %, young 14 %, mature 45 %, old 35 %: shallower, softer, ray-less, flat-floored), carved old first.")]
		public bool Ages;
		[Tooltip("Rays, arms, streaks, steps, branch levels, cluster size or passes.")]
		[Min(0)] public int Count;
		[Tooltip("How broken its surface is, 0 smooth … 1 rough (a seif's serrated crest, a rock glacier's ridges).")]
		[Range(0f, 1f)] public float Roughness;
		[Tooltip("A ridge's lean, −1 … 1: the left face gentler (positive) or steeper (negative); a sea dike's 1:6 seaward face.")]
		[Range(-1f, 1f)] public float Lean;
		[Tooltip("What its crest or floor is measured from: the ground, or the sea's surface.")]
		public BiomeLandformDatum Datum;
		[Tooltip("Metres below the sea's surface its crest or floor stands (negative: above it); a brine lake's mirror below its rim; a contour ridge's level.")]
		public Vector2 DatumDepth;
		[Tooltip("Ground slope at its place, degrees (min, max); (0, 0) any.")]
		public Vector2 Slope;
		[Tooltip("Depth of its place below the sea's surface, metres (negative above); (0, 0) any.")]
		public Vector2 Depth;
		[Tooltip("Local mean temperature, climate scale −1 … 1 (min, max); (0, 0) any.")]
		public Vector2 Temperature;
		[Tooltip("Local humidity, climate scale −1 … 1 (min, max); (0, 0) any.")]
		public Vector2 Humidity;
		[Tooltip("The worlds' air it forms under; None: any.")]
		public BiomeAtmosphereRequirement Air;
		[Tooltip("NearWater anchor: at most this many metres from a river or lake. PatchEdge anchor: this many metres inside the patch's edge.")]
		[Min(0f)] public float NearWater;
		[Tooltip("The ground layer its biome paints on it: −1 none, 0 the main layer, k detail layer k − 1.")]
		public int PaintGround;
		[Tooltip("Which part of it that layer covers.")]
		public BiomeLandformPart PaintPart;
		[Tooltip("How strongly, 0 … 1 (0: not painted).")]
		[Range(0f, 1f)] public float PaintStrength;
		[Tooltip("Its top (a pool's surface, a resting boulder) is a pad for the points of interest that stand on it.")]
		public bool Pad;

		/// <summary>Its family (<see cref="BiomeLandforms.FamilyOf"/>).</summary>
		public BiomeLandformFamily Family => BiomeLandforms.FamilyOf(Form);

		/// <summary>True when it can carve anything: named, and placed somewhere.</summary>
		public bool Carves => !string.IsNullOrWhiteSpace(Kind) && (Placement != BiomeLandformPlacement.Scatter || Density > 0f || Form == BiomeLandformForm.Pass
			|| Anchor == BiomeLandformAnchor.LeeOf);

		/// <summary>True when its biome paints it.</summary>
		public bool Paints => PaintGround >= 0 && PaintStrength > 0f;

		/// <summary>A copy painted with a ground layer of its biome (0 main, k detail k − 1) over <paramref name="part"/>.</summary>
		public BiomeLandform Painted(int ground, BiomeLandformPart part, float strength = 1f)
		{
			BiomeLandform copy = this;
			copy.PaintGround = ground;
			copy.PaintPart = part;
			copy.PaintStrength = Mathf.Clamp01(strength);
			return copy;
		}

		/// <summary>A copy at another density per km² (Scatter) or mix share (dune fields).</summary>
		public BiomeLandform AtDensity(float density)
		{
			BiomeLandform copy = this;
			copy.Density = Mathf.Max(0f, density);
			return copy;
		}

		/// <summary>A copy with other caps per patch and per scene (0: none).</summary>
		public BiomeLandform Capped(int perPatch, int perScene)
		{
			BiomeLandform copy = this;
			copy.PerPatch = Mathf.Max(0, perPatch);
			copy.PerScene = Mathf.Max(0, perScene);
			return copy;
		}

		/// <summary>A copy whose top is (or is not) a pad for points of interest.</summary>
		public BiomeLandform WithPad(bool pad)
		{
			BiomeLandform copy = this;
			copy.Pad = pad;
			return copy;
		}

		/// <summary>A copy placed only within a band of the local climate (temperature, humidity, −1 … 1; (0, 0) open): a cold-water plough mark.</summary>
		public BiomeLandform Banded(Vector2 temperature, Vector2 humidity)
		{
			BiomeLandform copy = this;
			copy.Temperature = temperature;
			copy.Humidity = humidity;
			return copy;
		}

		/// <summary>A copy whose moat lies on its windward side by <paramref name="bias"/> (0 an even ring … 1 upwind only), aligned to the wind.</summary>
		public BiomeLandform WindScooped(float bias)
		{
			BiomeLandform copy = this;
			copy.MoatBias = Mathf.Clamp01(bias);
			if (copy.MoatBias > 0f)
			{
				copy.Align = BiomeLandformAlign.Wind;
			}
			return copy;
		}

		/// <summary>
		/// A copy whose scattered sizes follow a power law (<paramref name="power"/>: N(> D) ∝ D^−power) and, when
		/// <paramref name="ages"/>, whose impact craters age by their hash (2026-10-25, decision 72: a crater field).
		/// </summary>
		public BiomeLandform PowerLaw(float power, bool ages)
		{
			BiomeLandform copy = this;
			copy.SizePower = Mathf.Max(0f, power);
			copy.Ages = ages;
			return copy;
		}

		/// <summary>A copy under another name (a variant of a preset in its own list entry).</summary>
		public BiomeLandform Named(string kind)
		{
			BiomeLandform copy = this;
			copy.Kind = kind;
			return copy;
		}

		/// <summary>
		/// The entry as the authoring marks and reports it: its name, form and every value, invariant. Two entries print the same
		/// exactly when they carve and paint the same.
		/// </summary>
		public override string ToString()
		{
			var s = new StringBuilder(160);
			s.Append(Kind ?? "<none>").Append('|').Append(Form).Append('|').Append(Placement).Append('|').Append(Anchor).Append('|').Append(Align);
			Append(s, Density).Append(',').Append(PerPatch).Append(',').Append(PerScene);
			Append(s, FitShare);
			Append(s, Size); Append(s, Length); Append(s, Width); Append(s, Height);
			Append(s, Top); Append(s, Flank); Append(s, Rim); Append(s, Crown); Append(s, CrownDepth); Append(s, Moat); Append(s, MoatDepth);
			s.Append('|').Append(Count);
			Append(s, Roughness); Append(s, Lean);
			s.Append('|').Append(Datum);
			Append(s, DatumDepth); Append(s, Slope); Append(s, Depth); Append(s, Temperature); Append(s, Humidity);
			s.Append('|').Append((int)Air);
			Append(s, NearWater);
			s.Append('|').Append(PaintGround).Append(',').Append(PaintPart);
			Append(s, PaintStrength);
			s.Append('|').Append(Pad ? "pad" : "-");
			// Appended only when set (2026-10-25), so every entry written before prints as it did.
			if (MoatBias != 0f)
			{
				Append(s.Append("|wind"), MoatBias);
			}
			if (SizePower != 0f)
			{
				Append(s.Append("|pow"), SizePower);
			}
			if (Ages)
			{
				s.Append("|aged");
			}
			return s.ToString();
		}

		private static StringBuilder Append(StringBuilder s, float value) => s.Append('|').Append(value.ToString("R", CultureInfo.InvariantCulture));

		private static StringBuilder Append(StringBuilder s, Vector2 value)
			=> s.Append('|').Append(value.x.ToString("R", CultureInfo.InvariantCulture)).Append('~').Append(value.y.ToString("R", CultureInfo.InvariantCulture));
	}

	/// <summary>One stretch of a landform's line (a ridge's crest, a channel's thalweg), world xz, with its half width and depth or height.</summary>
	[Serializable]
	public struct BiomeLandformSegment
	{
		public Vector2 A;
		public Vector2 B;
		/// <summary>Half the width across the line, metres.</summary>
		public float Half;
		/// <summary>The depth cut (channels) or height raised (ridges) along it, metres.</summary>
		public float Depth;
		/// <summary>Metres along its line from the line's start to <see cref="A"/> and to <see cref="B"/>.</summary>
		public float AlongA;
		public float AlongB;
		/// <summary>The whole line's length, metres: its ends taper over a half width.</summary>
		public float LineLength;
		/// <summary>True where the line's start is a free end that tapers (a creek's head, a ridge's ends; never a channel's outlet).</summary>
		public bool TaperStart;

		public BiomeLandformSegment(Vector2 a, Vector2 b, float half, float depth, float alongA, float lineLength, bool taperStart = true)
		{
			A = a;
			B = b;
			Half = half;
			Depth = depth;
			AlongA = alongA;
			AlongB = alongA + Vector2.Distance(a, b);
			LineLength = lineLength;
			TaperStart = taperStart;
		}
	}

	/// <summary>
	/// One landform as the cut placed it (BiomeLandforms.Apply draws it): the entry it came from and everything its place decided,
	/// kept with the scene (GeneratedLandforms) so a repaint paints it again exactly.
	/// </summary>
	[Serializable]
	public struct BiomeLandformSite
	{
		/// <summary>The entry it was placed from.</summary>
		public BiomeLandform Form;
		/// <summary>Its biome's template id.</summary>
		public int BiomeID;
		/// <summary>Its centre (a fan's apex, a line's start), world xz.</summary>
		public Vector2 Centre;
		/// <summary>Half its footprint across its axis, metres (a ring's radius, a field's lattice cell).</summary>
		public float Radius;
		/// <summary>Half its length along its axis, metres (= <see cref="Radius"/> when round).</summary>
		public float HalfLength;
		/// <summary>Half its cross width, metres (a ring's rim, a field's spacing).</summary>
		public float HalfWidth;
		/// <summary>Its height or depth, metres (a crater's depth, already out of its share).</summary>
		public float Height;
		/// <summary>Its axis heading, degrees from +x toward +z.</summary>
		public float Yaw;
		/// <summary>Its own hash: every pick and every bit of noise it draws comes from it.</summary>
		public uint Seed;
		/// <summary>An absolute level it carves to (a platform's top, a pan's floor, a crest at the sea's datum), scene metres; NaN for none.</summary>
		public float Level;
		/// <summary>The ground at its centre when it was placed, scene metres.</summary>
		public float Ground;
		/// <summary>The world box it changes: min x, min z, max x, max z.</summary>
		public Vector4 Bounds;
		/// <summary>A dune field's share of its biome's dune mix: the band of the mix noise it covers.</summary>
		public float MixLow;
		public float MixHigh;
		/// <summary>The mix noise's own seed (one a biome, so its dune fields partition the patch).</summary>
		public uint MixSeed;
		/// <summary>Its line, for ridges and channels; empty otherwise.</summary>
		public List<BiomeLandformSegment> Segments;
	}

	/// <summary>
	/// A landform's top offered as a place for a point of interest to stand (2026-10-18, I2g): a terp's or motte's summit, a mesa's
	/// table, a fan's apron, a brine lake's mirror, a resting boulder. The sites that use them are built in phase I7.
	/// </summary>
	[Serializable]
	public struct BiomeLandformPad
	{
		/// <summary>The landform's name (its entry's <see cref="BiomeLandform.Kind"/>).</summary>
		public string Kind;
		public int BiomeID;
		/// <summary>The pad's centre, world xz.</summary>
		public Vector2 Centre;
		/// <summary>Its flat radius, metres.</summary>
		public float Radius;
		/// <summary>Its surface after the carve, scene metres (a pool's or mirror's water level for one that holds water).</summary>
		public float Level;
		/// <summary>True when the pad is a water surface (a pool, a mirror), not ground.</summary>
		public bool Water;
	}
}
