using System;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>The body an ambient creature is built and animated as (AmbientCreatureMeshes, FishAmbientLife.hlsl's <c>_Mode</c>).</summary>
	public enum AmbientShape
	{
		/// <summary>A small passerine: finch, sparrow, tit, starling, blackbird.</summary>
		Songbird,
		/// <summary>A crow, rook or raven: black, broad fingered wings.</summary>
		Crow,
		/// <summary>A gull: long narrow pointed wings, white below.</summary>
		Gull,
		/// <summary>A buzzard, hawk or eagle: broad wings with spread primaries, short tail.</summary>
		Raptor,
		/// <summary>A vulture: a raptor's plan, longer and darker, the head small.</summary>
		Vulture,
		/// <summary>A dabbling duck: a heavy body on the water, a flat bill.</summary>
		Duck,
		/// <summary>A bat: membrane wings on long fingers, a small furry body, ears.</summary>
		Bat,
		/// <summary>A rat: long body, naked tail as long again.</summary>
		Rat,
		/// <summary>A mouse or vole: smaller, big ears, a shorter tail.</summary>
		Mouse,
		/// <summary>A squirrel: long hind legs, a bushy tail curled over the back.</summary>
		Squirrel,
		/// <summary>A rabbit or hare: a round body, long ears, big hind feet, a white scut.</summary>
		Rabbit,
		/// <summary>A lizard: low and sprawling, a tail longer than the body.</summary>
		Lizard,
		/// <summary>A shore crab, walking sideways.</summary>
		Crab,
		/// <summary>A frog or toad: squat and wide, folded hind legs.</summary>
		Frog,
		/// <summary>An owl: a big round head on a short body, broad rounded wings, a short tail.</summary>
		Owl,
		/// <summary>A goose, swan or crane in flight: a long neck stretched ahead, long steady-beating wings.</summary>
		Goose,
		/// <summary>A butterfly: a slip of a body under two broad rounded wings, flapping and gliding in fits.</summary>
		Butterfly,
		/// <summary>A dragonfly or damselfly: a long thin body, narrow glassy wings held out flat, a blur when it beats.</summary>
		Dragonfly,
		/// <summary>A beetle: a domed shell over six legs, a small head with feelers. Walks like a crab, but forward.</summary>
		Beetle,
		/// <summary>
		/// A ray of the air: a flat diamond of a body that is all wing, a whip of a tail, rowing slowly and gliding. Only
		/// where the air is dense and cold enough to swim in.
		/// </summary>
		Ray,
		/// <summary>
		/// A swarm: a whole cloud of insects drawn as one, every one of them a speck flown by the shader about the swarm's
		/// middle (<see cref="AmbientCreatureKind.SwarmStyle"/> says how).
		/// </summary>
		Swarm,
		/// <summary>A spider: a small head-and-body before a round abdomen, eight long legs bent high at the knee. Walks like a beetle.</summary>
		Spider,
		/// <summary>
		/// A cave cricket: a humped, wingless body on long spiny hind legs, its feelers twice its length sweeping ahead of it in
		/// the dark. Walks like a beetle.
		/// </summary>
		Cricket,
		/// <summary>
		/// A millipede: a long low string of round segments, two pairs of short legs to each, a pair of short feelers. Its legs
		/// step in waves down its length (FishAmbientLife.hlsl's crawler mode, each pair a little behind the one before).
		/// </summary>
		Millipede,
		/// <summary>A land snail: a coiled shell on a long soft foot, two eye-stalks and two shorter feelers. Glides as a crawler.</summary>
		Snail,
		/// <summary>A small fish: a fusiform body, a forked tail fin, pectoral and pelvic fins. Swims with a lizard's body wave.</summary>
		Fish,
		/// <summary>
		/// A jellyfish: a bell a little wider than deep, tentacles trailing from its rim; it rows by the bell's quick squeeze and
		/// slow relaxing (FishAmbientLife.hlsl's own mode 5).
		/// </summary>
		Jelly,
		/// <summary>A worm: a slender round body tapering to both ends, a few bristles. Writhes with a lizard's body wave.</summary>
		Worm,
	}

	/// <summary>
	/// The places in a cave a kind lives at (<see cref="FishMMO.Shared.SceneCaveLifeKind"/>, one bit each by its ordinal), for
	/// the cave creatures placed from the cut's cave-life spots rather than the terrain (AmbientCaveLife).
	/// </summary>
	[Flags]
	public enum AmbientCaveNiche
	{
		None = 0,
		Floor = 1 << 0,
		Wall = 1 << 1,
		Roost = 1 << 2,
		Flight = 1 << 3,
		Pool = 1 << 4,
		Mouth = 1 << 5,
		Glow = 1 << 6,
	}

	/// <summary>The kinds of cave a kind lives in (<see cref="FishMMO.Shared.SceneCaveForm"/>, one bit each by its ordinal).</summary>
	[Flags]
	public enum AmbientCaveForms
	{
		None = 0,
		Cave = 1 << 0,
		Grotto = 1 << 1,
		SeaCave = 1 << 2,
		IceCave = 1 << 3,
		LavaTube = 1 << 4,
		Mine = 1 << 5,
		/// <summary>The caves water made in limestone: a cave, a grotto, a sea cave.</summary>
		Limestone = Cave | Grotto | SeaCave,
		/// <summary>Every dry cave but ice: the stone caves and a mine.</summary>
		Rock = Cave | Grotto | SeaCave | LavaTube | Mine,
	}

	/// <summary>How a swarm's insects fly about its middle (FishAmbientLife.hlsl's swarm mode, <c>_SwarmStyle</c>).</summary>
	public enum AmbientSwarmStyle
	{
		/// <summary>
		/// Midges dancing in a column over a marker (a bush, a post, a stretch of water): bobbing up and down a hand's
		/// breadth at a time, the column swaying as a whole.
		/// </summary>
		Column = 0,
		/// <summary>Mosquitoes and gnats: a loose cloud, each one jinking on its own.</summary>
		Cloud = 1,
		/// <summary>Flies: tight fast loops round a spot near the ground, now and then a dart out and back.</summary>
		Buzz = 2,
		/// <summary>Fireflies: spread wide and low, drifting slowly, each flashing on its own rhythm.</summary>
		Drift = 3,
		/// <summary>Bees and hoverflies over flowers: hanging still, then darting to the next bloom.</summary>
		Hover = 4,
		/// <summary>Locusts: a wide low cloud all streaming one way with the wind, churning as it goes.</summary>
		Plague = 5,
		/// <summary>
		/// Luminous spores and springtails in a cave's still air: a wide slow haze, each mote sinking and lifting on the faintest
		/// draught and kindling and fading on a slow breath of its own.
		/// </summary>
		Spores = 6,
	}

	/// <summary>
	/// Which parts of a creature give off its own light (bioluminescence), as its mesh's emission mask (COLOR.a, AmbientCreatureMeshes):
	/// the shader's emission (FishAmbientLife.hlsl, <c>_EmitColour</c>) lights only those.
	/// </summary>
	public enum AmbientGlowParts
	{
		/// <summary>No light of its own.</summary>
		None,
		/// <summary>The whole body (a luminous millipede's armour, an ice worm, an ember mite).</summary>
		Body,
		/// <summary>Two lamps on the shield behind the head (a click beetle's headlamps).</summary>
		Lanterns,
		/// <summary>The front of the foot under the head (a glowing land snail's light organ).</summary>
		Foot,
		/// <summary>A line of light down each flank (a cave fish's lateral lines) and a few lamps under the eye.</summary>
		LateralLines,
		/// <summary>The wings' outer edges and the body (a wisp moth).</summary>
		WingEdges,
		/// <summary>A double row of spots down the back, each twinkling on its own (a lantern newt).</summary>
		Spots,
		/// <summary>The bell's rim and canals and the tentacles (a jelly): the light runs out from the crown as it squeezes.</summary>
		Bell,
	}

	/// <summary>How a creature's own light comes and goes over time (<see cref="AmbientGlow"/>), on the shared clock.</summary>
	public enum AmbientGlowPattern
	{
		/// <summary>A steady glow that breathes slowly brighter and dimmer.</summary>
		Pulse,
		/// <summary>Trains of a few short flashes, then dark a while (a firefly's, a glowing snail's).</summary>
		FlashTrain,
		/// <summary>Waves of light running nose to tail along the body, again and again (a millipede).</summary>
		Wave,
		/// <summary>The light running out from the crown to the rim and down the tentacles with each squeeze of the bell.</summary>
		Ripple,
		/// <summary>Each lamp twinkling on its own slow rhythm, the body's whole glow steady.</summary>
		Twinkle,
		/// <summary>A restless ember's flicker: incommensurate wavers that never settle.</summary>
		Flicker,
	}

	/// <summary>
	/// The kinds of world a creature lives on (<see cref="AmbientGating.WorldOf"/>): which air the scene's planet or
	/// moon has and what its weather is made of. A world with no air has none of them.
	/// </summary>
	[Flags]
	public enum AmbientWorld
	{
		/// <summary>No life at all: no air, a molten surface, or a runaway greenhouse.</summary>
		None = 0,
		/// <summary>Air like the home world's, with a water cycle: birds, bats, rats, insects.</summary>
		Earthly = 1 << 0,
		/// <summary>Air too thin for a wing to beat in: only what crawls and basks on the ground.</summary>
		ThinAir = 1 << 1,
		/// <summary>Dense, bitter-cold air whose weather is methane, nitrogen or ammonia: easy flight for the light-bodied.</summary>
		Cryogenic = 1 << 2,
	}

	/// <summary>How a kind lives and moves (AmbientLifeMotion).</summary>
	public enum AmbientBehaviour
	{
		/// <summary>A flock wheeling over open country or the coast, now and then settling on the ground or in trees.</summary>
		Flock,
		/// <summary>A bird alone or in a pair: perched in a tree crown or foraging on the ground, flitting between.</summary>
		Perch,
		/// <summary>A bird of prey circling in a thermal, then gliding to the next.</summary>
		Soar,
		/// <summary>A bat hawking insects: fast, erratic, low over water and along tree lines.</summary>
		Hawk,
		/// <summary>A ground animal: sitting, foraging, scurrying between spots, bolting for cover.</summary>
		Scurry,
		/// <summary>A waterfowl paddling about on still water.</summary>
		Paddle,
		/// <summary>
		/// Birds passing over on their way somewhere else: a straight crossing from one side of the sky to the other, now
		/// and then — a skein of geese high in its V, a pair of pigeons, a few small birds bounding over the treetops.
		/// </summary>
		Transit,
		/// <summary>
		/// An insect swarm keeping station over its spot (a marker, a pool, a patch of flowers), drifting a little, drawn
		/// as one cloud (<see cref="AmbientShape.Swarm"/>).
		/// </summary>
		Swarm,
	}

	/// <summary>Which of the settings' families a kind belongs to: each has its own draw distance and density.</summary>
	public enum AmbientFamily
	{
		Flocks,
		SoloBirds,
		Raptors,
		Bats,
		Critters,
		/// <summary>Insect swarms: a cloud seen from close by, gone a short way off.</summary>
		Swarms,
	}

	/// <summary>When in the day a kind is about.</summary>
	public enum AmbientActivity
	{
		/// <summary>By day; a little busier in the first hours (birds at dawn).</summary>
		Diurnal,
		/// <summary>By night.</summary>
		Nocturnal,
		/// <summary>Mostly at dawn and dusk, some by day and night: rabbits, deer, many rodents.</summary>
		Crepuscular,
		/// <summary>From dusk, through the night: bats, which leave the roost as the light goes.</summary>
		Dusk,
		/// <summary>Any hour (shore crabs work the tide, not the clock).</summary>
		AnyTime,
	}

	/// <summary>
	/// The kinds of country a biome offers wildlife (<see cref="AmbientGating.HabitatOfTemplate"/>). The biome template carries
	/// it as <see cref="FishMMO.Shared.Biomes.BiomeHabitat"/>, the same members by the same bits (a test keeps them equal).
	/// </summary>
	[Flags]
	public enum AmbientHabitat
	{
		None = 0,
		Forest = 1 << 0,
		Woodland = 1 << 1,
		Jungle = 1 << 2,
		Grassland = 1 << 3,
		Farmland = 1 << 4,
		Scrub = 1 << 5,
		Wetland = 1 << 6,
		Desert = 1 << 7,
		/// <summary>Beaches, rocky shores, estuaries: where land meets the sea.</summary>
		Coast = 1 << 8,
		/// <summary>Open sea.</summary>
		Sea = 1 << 9,
		/// <summary>Lakes and rivers.</summary>
		FreshWater = 1 << 10,
		Mountain = 1 << 11,
		Tundra = 1 << 12,
		/// <summary>Snow fields, glaciers, ice sheets: nothing lives on them but a passing seabird.</summary>
		Ice = 1 << 13,
		Settlement = 1 << 14,
		Cave = 1 << 15,
		Volcanic = 1 << 16,
		/// <summary>No life at all: wasteland, alien ground, airless rock.</summary>
		Barren = 1 << 17,
		/// <summary>
		/// Ruins, graveyards and abandoned farms (the scene's points of interest, not its biome): broken walls full of
		/// holes and ledges, where owls, bats, crows, rats and lizards live.
		/// </summary>
		Ruins = 1 << 18,
		/// <summary>
		/// Other worlds' open ground: regolith, dust seas, tholin plains, methane shores. Lifeless to the home world's
		/// animals; home to the creatures of thin and frozen air.
		/// </summary>
		Alien = 1 << 19,
	}

	/// <summary>One kind of ambient creature: what it looks like, where and when it lives, and how it moves.</summary>
	[Serializable]
	public class AmbientCreatureKind
	{
		public string Name = "Songbird";
		public AmbientShape Shape = AmbientShape.Songbird;
		public AmbientBehaviour Behaviour = AmbientBehaviour.Perch;
		public AmbientFamily Family = AmbientFamily.SoloBirds;
		public bool Enabled = true;
		/// <summary>The worlds it lives on; nothing lives on a world whose air is <see cref="AmbientWorld.None"/>.</summary>
		public AmbientWorld Worlds = AmbientWorld.Earthly;

		// ── Size and numbers ──
		/// <summary>Body length, metres (bill to tail; a crab's width), min..max per animal.</summary>
		public Vector2 Length = new Vector2(0.13f, 0.17f);
		/// <summary>Animals in a group, min..max.</summary>
		public Vector2Int GroupSize = new Vector2Int(1, 2);
		/// <summary>Groups per square kilometre where it is at home (before the family's density).</summary>
		public float GroupsPerKm2 = 40f;
		/// <summary>How far a group's members spread about its centre, metres.</summary>
		public float Spread = 3f;
		/// <summary>How far from home it ranges, metres (a flock's loop, a bird's foraging round, a rat's run).</summary>
		public Vector2 Roam = new Vector2(10f, 25f);

		// ── Movement ──
		/// <summary>Travelling speed, metres a second (flight, a scurry, a paddle).</summary>
		public float Speed = 6f;
		/// <summary>Wing beats (strides, hops) a second at that speed.</summary>
		public float StrokeHz = 14f;
		/// <summary>Flocks and raptors: metres above the ground they fly at, min..max. Bats: their hawking band.</summary>
		public Vector2 Altitude = new Vector2(10f, 35f);
		/// <summary>Seconds one episode of its routine lasts (a perch, a foraging bout, a flight loop), min..max.</summary>
		public Vector2 Episode = new Vector2(20f, 45f);
		/// <summary>Transit: the share of its episodes in which a group crosses (the sky is not always full).</summary>
		public float PassShare = 0.5f;
		/// <summary>Transit: flies in a V (geese, cranes, swans); otherwise a loose straggle.</summary>
		public bool Vee;

		// ── Where ──
		/// <summary>The country it lives in.</summary>
		public AmbientHabitat Habitats = AmbientHabitat.Woodland;
		/// <summary>Country it is most at home in: groups there are <see cref="PreferWeight"/> times as common.</summary>
		public AmbientHabitat Prefers = AmbientHabitat.None;
		public float PreferWeight = 2f;
		/// <summary>The climate it lives in, -1..1 (the scene's painted climate at the place; 0 = 0 °C, 1 = 33.1 °C).</summary>
		public Vector2 Climate = new Vector2(-0.5f, 1f);
		/// <summary>Only near the sea (seabirds, shore crabs).</summary>
		public bool NeedsSea;
		/// <summary>Only near fresh water (frogs, ducks).</summary>
		public bool NeedsFreshWater;
		/// <summary>Only where there are trees (squirrels).</summary>
		public bool NeedsTrees;
		/// <summary>
		/// Only far out at sea (2026-10-25, I2p: albatrosses, shearwaters, storm petrels): metres from any land its home must lie, the
		/// sea under it and no dry ground on eight bearings at this distance and at half of it (AmbientLifeMotion.IsOffshore). 0: no such rule.
		/// </summary>
		public float Offshore;

		// ── When ──
		public AmbientActivity Activity = AmbientActivity.Diurnal;
		/// <summary>The live air temperature (-1..1) below which none are about.</summary>
		public float ColdLimit = -0.3f;
		/// <summary>Likes heat: the hotter and sunnier the busier (lizards). Others shelter from a hot noon.</summary>
		public bool HeatLover;
		/// <summary>How much rain (0..1) drives all of them to shelter; 0 = indifferent to rain.</summary>
		public float RainLimit = 0.45f;
		/// <summary>Busier in the wet (frogs).</summary>
		public bool RainLover;
		/// <summary>Flies: high wind grounds it.</summary>
		public bool Flies = true;
		/// <summary>Gone in winter (hibernation: bats, lizards, frogs).</summary>
		public bool Hibernates;
		/// <summary>Fewer in winter (migrants).</summary>
		public bool Migrates;
		/// <summary>Needs thermals: sunny days only (soaring raptors).</summary>
		public bool NeedsThermals;

		// ── Fear ──
		/// <summary>
		/// Its flight initiation distance, metres: how close a person may come before it flies or bolts. Larger
		/// animals and flocks go sooner; 0 = never (birds aloft, bats).
		/// </summary>
		public float FlushMetres = 5f;
		/// <summary>Metres a second it flees at.</summary>
		public float FleeSpeed = 8f;

		// ── Look ──
		public Color Back = new Color(0.45f, 0.35f, 0.25f);
		public Color Belly = new Color(0.8f, 0.72f, 0.6f);
		/// <summary>Bill, legs, wing tips, ears — the accent colour.</summary>
		public Color Accent = new Color(0.2f, 0.16f, 0.12f);
		/// <summary>When set, each member is tinted one of these (songbirds of several species).</summary>
		public Color[] Palette = Array.Empty<Color>();
		public float Smoothness = 0.25f;
		/// <summary>Casts shadows within the settings' shadow distance.</summary>
		public bool CastShadows = true;

		// ── Swarms ──
		/// <summary>How a swarm's insects fly (<see cref="AmbientShape.Swarm"/> only).</summary>
		public AmbientSwarmStyle SwarmStyle = AmbientSwarmStyle.Cloud;
		/// <summary>
		/// A swarm's own light at the height of a flash (fireflies' cold green-gold), times <see cref="Accent"/>; 0 for
		/// insects that only show by the light they catch.
		/// </summary>
		public float Glow;

		// ── Its own light (2026-10-20) ──
		/// <summary>Which parts of it glow (none for a creature with no light of its own; swarms use <see cref="Glow"/>).</summary>
		public AmbientGlowParts GlowParts;
		/// <summary>How its light comes and goes.</summary>
		public AmbientGlowPattern GlowPattern;
		/// <summary>Its light's colour (sRGB): a millipede's cyan, a click beetle's yellow-green, an ember's orange.</summary>
		public Color GlowColour = new Color(0.4f, 1f, 0.8f);
		/// <summary>Its light at its brightest, times <see cref="GlowColour"/> (linear, HDR: past one the bloom catches it).</summary>
		public float GlowStrength = 2f;
		/// <summary>
		/// Seconds one round of its pattern takes (a breath, a flash train and the dark after it, a wave's run down the body, a
		/// flicker's slowest waver); each animal a little its own about it.
		/// </summary>
		public float GlowPeriod = 4f;
		/// <summary>The share of its light it keeps between pulses and flashes, 0 dark .. 1 steady.</summary>
		public float GlowRest = 0.3f;

		// ── Caves ──
		/// <summary>
		/// The places in a carved cave it lives at (the cut's cave-life spots: AmbientCaveLife); none for a kind that never goes
		/// underground. A kind only of the caves has <see cref="GroupsPerKm2"/> 0: the open land never places it.
		/// </summary>
		public AmbientCaveNiche CaveNiche;
		/// <summary>The caves it lives in.</summary>
		public AmbientCaveForms CaveForms = AmbientCaveForms.Rock;
		/// <summary>Groups at a spot of its niche, on average, where the cave suits it (before the family's density).</summary>
		public float CavePerSpot = 0.5f;
		/// <summary>
		/// How far from the daylight it lives, min..max (<see cref="FishMMO.Shared.SceneCaveLifeSpot.Dark"/>: 0 in a mouth's
		/// light, 1 in the dark for good): the entrance's moths and spiders, the deep cave's blind white things.
		/// </summary>
		public Vector2 CaveDark = new Vector2(0f, 1f);
		/// <summary>Only where the cave is wet (by its water, under a dripping roof): salamanders, olms.</summary>
		public bool CaveNeedsDamp;
	}

	/// <summary>The kinds the world starts with.</summary>
	public static class AmbientLifeCatalogue
	{
		private const AmbientHabitat OpenCountry = AmbientHabitat.Grassland | AmbientHabitat.Farmland | AmbientHabitat.Scrub;
		private const AmbientHabitat Wooded = AmbientHabitat.Forest | AmbientHabitat.Woodland | AmbientHabitat.Jungle;
		/// <summary>The open ground of a world under other air: its own alien plains and whatever rock, sand and ice its biomes are named for.</summary>
		private const AmbientHabitat OtherGround = AmbientHabitat.Alien | AmbientHabitat.Desert | AmbientHabitat.Scrub | AmbientHabitat.Mountain | AmbientHabitat.Volcanic
			| AmbientHabitat.Tundra | AmbientHabitat.Grassland | AmbientHabitat.Cave;
		/// <summary>On another world no cold drives them in: the limit lies below anything the scale reads.</summary>
		private const float NeverTooCold = -2f;

		/// <summary>Temperatures in the scale's units (-1..1, 0 = 0 °C, 1 = 33.1 °C) from degrees Celsius.</summary>
		public static float Celsius(float degrees) => degrees / 33.1f;

		/// <summary>
		/// Seventy kinds: sixty-two of the home world's (eight of them insect swarms, eighteen of them the caves', eleven of those
		/// with a light of their own), three of thin air and five of frozen air. Numbers are grounded in the animals': flight initiation distances from field studies
		/// (small passerines 4-8 m, corvids 15-25 m, gulls 10-20 m, ducks 10-20 m, rabbits ~10 m), wing beats from
		/// high-speed footage (finches 15-20 Hz, crows 3-4 Hz, gulls 3 Hz, bats 8-12 Hz), and densities thinned to
		/// what a player notices rather than what a census would count.
		/// </summary>
		public static AmbientCreatureKind[] Defaults()
		{
			return new[]
			{
				// ── Flocks ──
				new AmbientCreatureKind
				{
					Name = "Finch flock", Shape = AmbientShape.Songbird, Behaviour = AmbientBehaviour.Flock, Family = AmbientFamily.Flocks,
					Length = new Vector2(0.13f, 0.2f), GroupSize = new Vector2Int(12, 36), GroupsPerKm2 = 12f, Spread = 5f, Roam = new Vector2(50f, 110f),
					Speed = 11f, StrokeHz = 15f, Altitude = new Vector2(8f, 30f), Episode = new Vector2(60f, 120f),
					Habitats = OpenCountry | AmbientHabitat.Woodland | AmbientHabitat.Wetland | AmbientHabitat.Settlement | AmbientHabitat.Tundra,
					Prefers = AmbientHabitat.Farmland | AmbientHabitat.Grassland, Climate = new Vector2(Celsius(-12f), 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(-15f), RainLimit = 0.3f, Migrates = true,
					FlushMetres = 12f, FleeSpeed = 10f,
					Back = Hex(0x5a4a3a), Belly = Hex(0xb8a488), Accent = Hex(0x2a2420), Smoothness = 0.2f,
					Palette = new[] { Hex(0xffffff), Hex(0xe8d8c0), Hex(0x8a7a6a), Hex(0x3a3a40) },
				},
				new AmbientCreatureKind
				{
					Name = "Gulls", Shape = AmbientShape.Gull, Behaviour = AmbientBehaviour.Flock, Family = AmbientFamily.Flocks,
					Length = new Vector2(0.4f, 0.55f), GroupSize = new Vector2Int(3, 12), GroupsPerKm2 = 25f, Spread = 9f, Roam = new Vector2(60f, 140f),
					Speed = 9f, StrokeHz = 2.8f, Altitude = new Vector2(6f, 28f), Episode = new Vector2(70f, 140f),
					Habitats = AmbientHabitat.Coast | AmbientHabitat.Sea | AmbientHabitat.FreshWater | AmbientHabitat.Ice | AmbientHabitat.Wetland | AmbientHabitat.Farmland,
					Prefers = AmbientHabitat.Coast, PreferWeight = 3f, NeedsSea = true, Climate = new Vector2(-1f, 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = -0.9f, RainLimit = 0.6f,
					FlushMetres = 15f, FleeSpeed = 9f,
					Back = Hex(0xa8b0b8), Belly = Hex(0xf2f2ee), Accent = Hex(0x1e1e22), Smoothness = 0.3f,
				},
				new AmbientCreatureKind
				{
					Name = "Rooks", Shape = AmbientShape.Crow, Behaviour = AmbientBehaviour.Flock, Family = AmbientFamily.Flocks,
					Length = new Vector2(0.42f, 0.5f), GroupSize = new Vector2Int(4, 14), GroupsPerKm2 = 8f, Spread = 8f, Roam = new Vector2(60f, 120f),
					Speed = 9f, StrokeHz = 3.6f, Altitude = new Vector2(10f, 35f), Episode = new Vector2(70f, 130f),
					Habitats = OpenCountry | AmbientHabitat.Woodland | AmbientHabitat.Tundra | AmbientHabitat.Settlement | AmbientHabitat.Mountain | AmbientHabitat.Ruins,
					Prefers = AmbientHabitat.Farmland, PreferWeight = 3f, Climate = new Vector2(-0.6f, 0.9f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(-25f), RainLimit = 0.5f,
					FlushMetres = 22f, FleeSpeed = 9f,
					Back = Hex(0x18181c), Belly = Hex(0x222228), Accent = Hex(0x101012), Smoothness = 0.5f,
				},

				// ── Birds alone or in pairs ──
				new AmbientCreatureKind
				{
					Name = "Songbird", Shape = AmbientShape.Songbird, Behaviour = AmbientBehaviour.Perch, Family = AmbientFamily.SoloBirds,
					Length = new Vector2(0.11f, 0.17f), GroupSize = new Vector2Int(1, 2), GroupsPerKm2 = 500f, Spread = 4f, Roam = new Vector2(10f, 25f),
					Speed = 7f, StrokeHz = 17f, Episode = new Vector2(6f, 18f),
					Habitats = Wooded | OpenCountry | AmbientHabitat.Wetland | AmbientHabitat.Settlement | AmbientHabitat.Tundra | AmbientHabitat.Mountain,
					Prefers = Wooded | AmbientHabitat.Settlement, PreferWeight = 2.5f, Climate = new Vector2(Celsius(-15f), 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(-15f), RainLimit = 0.35f, Migrates = true,
					FlushMetres = 5f, FleeSpeed = 7f,
					Back = Hex(0x6a5440), Belly = Hex(0xc8b090), Accent = Hex(0x2a2016), Smoothness = 0.2f,
					// Robin, sparrow, great tit, blue tit, blackbird, chaffinch: the tint over a brown base.
					Palette = new[] { Hex(0xffb090), Hex(0xd8c0a0), Hex(0xf0e070), Hex(0x90b8f0), Hex(0x2a2a2a), Hex(0xe8a0a0) },
				},
				new AmbientCreatureKind
				{
					Name = "Crow", Shape = AmbientShape.Crow, Behaviour = AmbientBehaviour.Perch, Family = AmbientFamily.SoloBirds,
					Length = new Vector2(0.44f, 0.52f), GroupSize = new Vector2Int(1, 2), GroupsPerKm2 = 45f, Spread = 6f, Roam = new Vector2(20f, 45f),
					Speed = 9f, StrokeHz = 3.8f, Episode = new Vector2(10f, 28f),
					Habitats = Wooded | OpenCountry | AmbientHabitat.Settlement | AmbientHabitat.Coast | AmbientHabitat.Tundra | AmbientHabitat.Mountain | AmbientHabitat.Wetland
						| AmbientHabitat.Ruins,
					Prefers = AmbientHabitat.Farmland | AmbientHabitat.Settlement | AmbientHabitat.Ruins, Climate = new Vector2(-0.7f, 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(-30f), RainLimit = 0.55f,
					FlushMetres = 15f, FleeSpeed = 9f,
					Back = Hex(0x141418), Belly = Hex(0x1c1c22), Accent = Hex(0x0c0c0e), Smoothness = 0.55f,
				},
				new AmbientCreatureKind
				{
					Name = "Duck", Shape = AmbientShape.Duck, Behaviour = AmbientBehaviour.Paddle, Family = AmbientFamily.SoloBirds,
					Length = new Vector2(0.5f, 0.6f), GroupSize = new Vector2Int(2, 6), GroupsPerKm2 = 120f, Spread = 4f, Roam = new Vector2(5f, 12f),
					Speed = 0.45f, StrokeHz = 6f, Episode = new Vector2(25f, 60f),
					Habitats = AmbientHabitat.FreshWater | AmbientHabitat.Wetland | AmbientHabitat.Grassland | AmbientHabitat.Farmland | Wooded | AmbientHabitat.Settlement,
					NeedsFreshWater = true, Climate = new Vector2(Celsius(-10f), 1f),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(-12f), RainLimit = 0f,
					FlushMetres = 14f, FleeSpeed = 12f,
					Back = Hex(0x6a5844), Belly = Hex(0x8a7a64), Accent = Hex(0xd0a030), Smoothness = 0.4f,
					// Mallard drake (green head reads at distance as dark), ducks brown.
					Palette = new[] { Hex(0xffffff), Hex(0xd0d0d0), Hex(0xa8b8a0) },
				},
				new AmbientCreatureKind
				{
					Name = "Buzzard", Shape = AmbientShape.Raptor, Behaviour = AmbientBehaviour.Soar, Family = AmbientFamily.Raptors,
					Length = new Vector2(0.5f, 0.58f), GroupSize = new Vector2Int(1, 2), GroupsPerKm2 = 4f, Spread = 20f, Roam = new Vector2(150f, 300f),
					Speed = 9f, StrokeHz = 3f, Altitude = new Vector2(50f, 140f), Episode = new Vector2(90f, 160f),
					Habitats = OpenCountry | AmbientHabitat.Woodland | AmbientHabitat.Mountain | AmbientHabitat.Tundra | AmbientHabitat.Desert | AmbientHabitat.Wetland,
					Climate = new Vector2(-0.6f, 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(-20f), RainLimit = 0.1f, NeedsThermals = true,
					FlushMetres = 0f,
					Back = Hex(0x5a4430), Belly = Hex(0xc8b494), Accent = Hex(0x2a2018), Smoothness = 0.25f,
				},
				new AmbientCreatureKind
				{
					Name = "Vulture", Shape = AmbientShape.Vulture, Behaviour = AmbientBehaviour.Soar, Family = AmbientFamily.Raptors,
					Length = new Vector2(0.95f, 1.1f), GroupSize = new Vector2Int(2, 6), GroupsPerKm2 = 1.5f, Spread = 35f, Roam = new Vector2(200f, 400f),
					Speed = 10f, StrokeHz = 1.8f, Altitude = new Vector2(80f, 200f), Episode = new Vector2(120f, 200f),
					Habitats = AmbientHabitat.Desert | AmbientHabitat.Scrub | AmbientHabitat.Grassland | AmbientHabitat.Mountain,
					Climate = new Vector2(Celsius(14f), 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(8f), RainLimit = 0.1f, NeedsThermals = true, HeatLover = true,
					FlushMetres = 0f,
					Back = Hex(0x3a3028), Belly = Hex(0x4a3e34), Accent = Hex(0x1a1612), Smoothness = 0.2f,
				},

				// ── Birds of the night and the high air ──
				new AmbientCreatureKind
				{
					// Tawny, barn and long-eared owls: out of the roost at dusk, hunting from a perch at the wood's edge and
					// dropping onto the grass; the rest of the day asleep in a crown.
					Name = "Owl", Shape = AmbientShape.Owl, Behaviour = AmbientBehaviour.Perch, Family = AmbientFamily.SoloBirds,
					Length = new Vector2(0.34f, 0.42f), GroupSize = new Vector2Int(1, 2), GroupsPerKm2 = 22f, Spread = 8f, Roam = new Vector2(25f, 60f),
					Speed = 7f, StrokeHz = 3.2f, Episode = new Vector2(18f, 45f),
					Habitats = Wooded | AmbientHabitat.Farmland | AmbientHabitat.Grassland | AmbientHabitat.Settlement | AmbientHabitat.Wetland | AmbientHabitat.Tundra
						| AmbientHabitat.Ruins,
					Prefers = AmbientHabitat.Woodland | AmbientHabitat.Farmland | AmbientHabitat.Ruins, PreferWeight = 2.5f, Climate = new Vector2(-0.8f, 1f),
					Activity = AmbientActivity.Nocturnal, ColdLimit = Celsius(-35f), RainLimit = 0.3f,
					FlushMetres = 10f, FleeSpeed = 7f,
					Back = Hex(0x7a6448), Belly = Hex(0xd8c8a8), Accent = Hex(0x3a3024), Smoothness = 0.12f,
					// Tawny brown, a barn owl's gold and white, a grey phase.
					Palette = new[] { Hex(0xffffff), Hex(0xfff0d0), Hex(0xc8c4c0) },
				},
				new AmbientCreatureKind
				{
					// Golden and white-tailed eagles: the largest thing in the sky, circling far above the ridges and the coast.
					Name = "Eagle", Shape = AmbientShape.Raptor, Behaviour = AmbientBehaviour.Soar, Family = AmbientFamily.Raptors,
					Length = new Vector2(0.8f, 0.95f), GroupSize = new Vector2Int(1, 2), GroupsPerKm2 = 1.2f, Spread = 40f, Roam = new Vector2(250f, 420f),
					Speed = 13f, StrokeHz = 2.2f, Altitude = new Vector2(110f, 320f), Episode = new Vector2(120f, 220f),
					Habitats = AmbientHabitat.Mountain | AmbientHabitat.Coast | AmbientHabitat.Tundra | AmbientHabitat.Grassland | AmbientHabitat.Scrub
						| AmbientHabitat.Forest | AmbientHabitat.Wetland | AmbientHabitat.Desert,
					Prefers = AmbientHabitat.Mountain | AmbientHabitat.Coast, PreferWeight = 4f, Climate = new Vector2(-0.8f, 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(-30f), RainLimit = 0.15f, NeedsThermals = true,
					FlushMetres = 0f,
					Back = Hex(0x3e2c1c), Belly = Hex(0x4e3824), Accent = Hex(0x1c140e), Smoothness = 0.2f,
					Palette = new[] { Hex(0xffffff), Hex(0xe8d8b8) },
				},
				new AmbientCreatureKind
				{
					// Gulls riding the updraft off the shore and the warm air over a harbour town: high, wings still, wheeling.
					Name = "Gulls aloft", Shape = AmbientShape.Gull, Behaviour = AmbientBehaviour.Soar, Family = AmbientFamily.Raptors,
					Length = new Vector2(0.45f, 0.62f), GroupSize = new Vector2Int(2, 7), GroupsPerKm2 = 6f, Spread = 25f, Roam = new Vector2(80f, 200f),
					Speed = 9f, StrokeHz = 2.8f, Altitude = new Vector2(25f, 90f), Episode = new Vector2(70f, 140f),
					Habitats = AmbientHabitat.Coast | AmbientHabitat.Sea | AmbientHabitat.Settlement | AmbientHabitat.Farmland | AmbientHabitat.FreshWater | AmbientHabitat.Wetland,
					Prefers = AmbientHabitat.Coast | AmbientHabitat.Settlement, PreferWeight = 3f, NeedsSea = true, Climate = new Vector2(-0.6f, 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(-15f), RainLimit = 0.5f,
					FlushMetres = 0f,
					Back = Hex(0xa8b0b8), Belly = Hex(0xf2f2ee), Accent = Hex(0x1e1e22), Smoothness = 0.3f,
				},
				new AmbientCreatureKind
				{
					// Geese, swans and cranes on passage: a skein high over the land, calling, straight on to wherever it goes.
					Name = "Geese skein", Shape = AmbientShape.Goose, Behaviour = AmbientBehaviour.Transit, Family = AmbientFamily.Raptors,
					Length = new Vector2(0.75f, 0.95f), GroupSize = new Vector2Int(7, 23), GroupsPerKm2 = 1.6f, Spread = 3.2f, Roam = new Vector2(280f, 400f),
					Speed = 17f, StrokeHz = 3.2f, Altitude = new Vector2(70f, 240f), Episode = new Vector2(90f, 200f), PassShare = 0.45f, Vee = true,
					Habitats = OpenCountry | AmbientHabitat.Wetland | AmbientHabitat.FreshWater | AmbientHabitat.Coast | AmbientHabitat.Tundra
						| AmbientHabitat.Woodland | AmbientHabitat.Forest | AmbientHabitat.Settlement | AmbientHabitat.Mountain,
					Prefers = AmbientHabitat.Wetland | AmbientHabitat.Farmland | AmbientHabitat.Coast, PreferWeight = 2f, Climate = new Vector2(-0.6f, 0.85f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(-20f), RainLimit = 0.4f, Migrates = true,
					FlushMetres = 0f,
					Back = Hex(0x6a645a), Belly = Hex(0xb0a898), Accent = Hex(0x1e1c1a), Smoothness = 0.25f,
					// Greylags, a white swan now and then, a crane's grey.
					Palette = new[] { Hex(0xffffff), Hex(0xe8e4dc), Hex(0xb8bcc4) },
				},
				new AmbientCreatureKind
				{
					// Pigeons and doves crossing between fields and roofs: fast and direct, in ones and twos and small parties.
					Name = "Pigeons", Shape = AmbientShape.Crow, Behaviour = AmbientBehaviour.Transit, Family = AmbientFamily.Flocks,
					Length = new Vector2(0.3f, 0.4f), GroupSize = new Vector2Int(1, 6), GroupsPerKm2 = 14f, Spread = 4f, Roam = new Vector2(160f, 240f),
					Speed = 15f, StrokeHz = 6f, Altitude = new Vector2(15f, 55f), Episode = new Vector2(40f, 90f), PassShare = 0.55f,
					Habitats = OpenCountry | AmbientHabitat.Woodland | AmbientHabitat.Forest | AmbientHabitat.Settlement | AmbientHabitat.Coast | AmbientHabitat.Mountain,
					Prefers = AmbientHabitat.Settlement | AmbientHabitat.Farmland, PreferWeight = 2.5f, Climate = new Vector2(Celsius(-12f), 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(-15f), RainLimit = 0.4f,
					FlushMetres = 0f,
					Back = Hex(0x7a7e88), Belly = Hex(0x9a9ca4), Accent = Hex(0x3a3c44), Smoothness = 0.35f,
					Palette = new[] { Hex(0xffffff), Hex(0xd8d0c8), Hex(0xb8b0a8) },
				},
				new AmbientCreatureKind
				{
					// Small birds going over: a handful bounding across the sky above the treetops.
					Name = "Passing songbirds", Shape = AmbientShape.Songbird, Behaviour = AmbientBehaviour.Transit, Family = AmbientFamily.Flocks,
					Length = new Vector2(0.12f, 0.18f), GroupSize = new Vector2Int(1, 8), GroupsPerKm2 = 30f, Spread = 3f, Roam = new Vector2(120f, 180f),
					Speed = 12f, StrokeHz = 16f, Altitude = new Vector2(6f, 30f), Episode = new Vector2(30f, 70f), PassShare = 0.6f,
					Habitats = Wooded | OpenCountry | AmbientHabitat.Wetland | AmbientHabitat.Settlement | AmbientHabitat.Tundra | AmbientHabitat.Mountain | AmbientHabitat.Coast,
					Prefers = AmbientHabitat.Woodland | AmbientHabitat.Farmland | AmbientHabitat.Settlement, PreferWeight = 2f, Climate = new Vector2(Celsius(-12f), 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(-15f), RainLimit = 0.3f, Migrates = true,
					FlushMetres = 0f,
					Back = Hex(0x5a4a3a), Belly = Hex(0xb8a488), Accent = Hex(0x2a2420), Smoothness = 0.2f,
					Palette = new[] { Hex(0xffffff), Hex(0xe8d8c0), Hex(0x8a7a6a), Hex(0x3a3a40), Hex(0xf0e070) },
				},

				// ── Bats ──
				new AmbientCreatureKind
				{
					Name = "Bats", Shape = AmbientShape.Bat, Behaviour = AmbientBehaviour.Hawk, Family = AmbientFamily.Bats,
					Length = new Vector2(0.08f, 0.11f), GroupSize = new Vector2Int(3, 9), GroupsPerKm2 = 100f, Spread = 10f, Roam = new Vector2(8f, 16f),
					Speed = 6f, StrokeHz = 10f, Altitude = new Vector2(2f, 9f),
					Habitats = Wooded | AmbientHabitat.Wetland | AmbientHabitat.FreshWater | AmbientHabitat.Farmland | AmbientHabitat.Settlement
						| AmbientHabitat.Cave | AmbientHabitat.Mountain | AmbientHabitat.Grassland | AmbientHabitat.Scrub | AmbientHabitat.Desert | AmbientHabitat.Ruins,
					Prefers = AmbientHabitat.Wetland | AmbientHabitat.FreshWater | AmbientHabitat.Cave | AmbientHabitat.Woodland | AmbientHabitat.Ruins, PreferWeight = 2.5f,
					Climate = new Vector2(Celsius(0f), 1f),
					// Insect bats stay in the roost below about 5 °C (no insects fly) and in rain (it clogs their sonar and soaks them).
					Activity = AmbientActivity.Dusk, ColdLimit = Celsius(5f), RainLimit = 0.15f, Hibernates = true,
					FlushMetres = 0f,
					Back = Hex(0x2a221e), Belly = Hex(0x3a302a), Accent = Hex(0x1a1614), Smoothness = 0.2f, CastShadows = false,
				},

				// ── Insects ──
				new AmbientCreatureKind
				{
					// Whites, brimstones, fritillaries, blues: over flowery grass, field edges and gardens on warm sunny days,
					// flitting in fits a metre or so up.
					Name = "Butterflies", Shape = AmbientShape.Butterfly, Behaviour = AmbientBehaviour.Hawk, Family = AmbientFamily.Bats,
					Length = new Vector2(0.03f, 0.045f), GroupSize = new Vector2Int(1, 4), GroupsPerKm2 = 450f, Spread = 6f, Roam = new Vector2(4f, 10f),
					Speed = 1.6f, StrokeHz = 9f, Altitude = new Vector2(0.4f, 2.2f),
					Habitats = OpenCountry | AmbientHabitat.Woodland | AmbientHabitat.Settlement | AmbientHabitat.Wetland | AmbientHabitat.Jungle
						| AmbientHabitat.Mountain | AmbientHabitat.Ruins,
					Prefers = AmbientHabitat.Grassland | AmbientHabitat.Farmland | AmbientHabitat.Scrub, PreferWeight = 2.5f, Climate = new Vector2(Celsius(8f), 1f),
					// Ectotherms that need the sun on their wings: under about 13 °C or an overcast sky they sit it out.
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(13f), HeatLover = true, RainLimit = 0.08f, Hibernates = true, NeedsThermals = true,
					FlushMetres = 0f,
					Back = Hex(0xf0ead8), Belly = Hex(0xe0d8c0), Accent = Hex(0x2a2420), Smoothness = 0.1f, CastShadows = false,
					// Cabbage white, brimstone, small tortoiseshell's orange, a blue, a peacock's red.
					Palette = new[] { Hex(0xffffff), Hex(0xf8f0a0), Hex(0xf0a050), Hex(0x90b0f0), Hex(0xc05040) },
				},
				new AmbientCreatureKind
				{
					// Hawkers and darters patrolling a pond or a slow river's margin, hanging and darting low over the water.
					Name = "Dragonflies", Shape = AmbientShape.Dragonfly, Behaviour = AmbientBehaviour.Hawk, Family = AmbientFamily.Bats,
					Length = new Vector2(0.05f, 0.08f), GroupSize = new Vector2Int(1, 3), GroupsPerKm2 = 600f, Spread = 4f, Roam = new Vector2(4f, 9f),
					Speed = 3.5f, StrokeHz = 30f, Altitude = new Vector2(0.3f, 1.6f),
					Habitats = AmbientHabitat.Wetland | AmbientHabitat.FreshWater | AmbientHabitat.Grassland | AmbientHabitat.Farmland | Wooded,
					Prefers = AmbientHabitat.Wetland | AmbientHabitat.FreshWater, PreferWeight = 3f, NeedsFreshWater = true, Climate = new Vector2(Celsius(8f), 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(14f), HeatLover = true, RainLimit = 0.12f, Hibernates = true, NeedsThermals = true,
					FlushMetres = 0f,
					Back = Hex(0x2a6a8a), Belly = Hex(0x3a7a6a), Accent = Hex(0x141414), Smoothness = 0.6f, CastShadows = false,
					// An emperor's blue, a green hawker, a darter's red.
					Palette = new[] { Hex(0xffffff), Hex(0x90e0a0), Hex(0xf07050) },
				},

				// ── Ground critters ──
				new AmbientCreatureKind
				{
					Name = "Rat", Shape = AmbientShape.Rat, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.2f, 0.26f), GroupSize = new Vector2Int(1, 3), GroupsPerKm2 = 600f, Spread = 3f, Roam = new Vector2(2f, 5f),
					Speed = 1.6f, StrokeHz = 6f, Episode = new Vector2(5f, 14f),
					Habitats = AmbientHabitat.Farmland | AmbientHabitat.Settlement | AmbientHabitat.Wetland | AmbientHabitat.Coast | AmbientHabitat.Grassland
						| AmbientHabitat.Woodland | AmbientHabitat.Scrub | AmbientHabitat.Cave | AmbientHabitat.Ruins,
					Prefers = AmbientHabitat.Settlement | AmbientHabitat.Farmland | AmbientHabitat.Wetland | AmbientHabitat.Ruins, PreferWeight = 3f, Climate = new Vector2(Celsius(-8f), 1f),
					Activity = AmbientActivity.Nocturnal, ColdLimit = Celsius(-10f), RainLimit = 0.6f,
					FlushMetres = 4f, FleeSpeed = 3f,
					Back = Hex(0x4a4038), Belly = Hex(0x7a7064), Accent = Hex(0xb08a80), Smoothness = 0.25f,
					// And into the caves and old workings from the mouth, as far as the dark goes before there is nothing to eat.
					CaveNiche = AmbientCaveNiche.Floor, CaveForms = AmbientCaveForms.Cave | AmbientCaveForms.Grotto | AmbientCaveForms.Mine | AmbientCaveForms.LavaTube,
					CavePerSpot = 0.35f, CaveDark = new Vector2(0f, 0.9f),
				},
				new AmbientCreatureKind
				{
					Name = "Mouse", Shape = AmbientShape.Mouse, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.08f, 0.1f), GroupSize = new Vector2Int(1, 3), GroupsPerKm2 = 700f, Spread = 2f, Roam = new Vector2(1f, 3f),
					Speed = 1.2f, StrokeHz = 9f, Episode = new Vector2(4f, 10f),
					Habitats = OpenCountry | AmbientHabitat.Woodland | AmbientHabitat.Forest | AmbientHabitat.Tundra | AmbientHabitat.Settlement | AmbientHabitat.Mountain,
					Climate = new Vector2(Celsius(-20f), 1f),
					Activity = AmbientActivity.Nocturnal, ColdLimit = Celsius(-20f), RainLimit = 0.5f,
					FlushMetres = 3f, FleeSpeed = 2.5f,
					Back = Hex(0x6a5a48), Belly = Hex(0xb0a490), Accent = Hex(0xd0a8a0), Smoothness = 0.25f,
				},
				new AmbientCreatureKind
				{
					Name = "Squirrel", Shape = AmbientShape.Squirrel, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.22f, 0.26f), GroupSize = new Vector2Int(1, 2), GroupsPerKm2 = 300f, Spread = 4f, Roam = new Vector2(3f, 7f),
					Speed = 2.2f, StrokeHz = 4f, Episode = new Vector2(6f, 16f),
					Habitats = AmbientHabitat.Forest | AmbientHabitat.Woodland | AmbientHabitat.Settlement, NeedsTrees = true,
					Climate = new Vector2(Celsius(-20f), Celsius(30f)),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(-15f), RainLimit = 0.3f,
					FlushMetres = 7f, FleeSpeed = 5f,
					Back = Hex(0x9a5228), Belly = Hex(0xe8d8c0), Accent = Hex(0x7a3a1a), Smoothness = 0.2f,
					Palette = new[] { Hex(0xffffff), Hex(0xffffff), Hex(0x9a9a9a) },
				},
				new AmbientCreatureKind
				{
					Name = "Rabbit", Shape = AmbientShape.Rabbit, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.36f, 0.46f), GroupSize = new Vector2Int(1, 4), GroupsPerKm2 = 250f, Spread = 6f, Roam = new Vector2(3f, 8f),
					Speed = 1.2f, StrokeHz = 2.5f, Episode = new Vector2(8f, 20f),
					Habitats = OpenCountry | AmbientHabitat.Woodland | AmbientHabitat.Tundra | AmbientHabitat.Settlement | AmbientHabitat.Desert | AmbientHabitat.Mountain,
					Prefers = AmbientHabitat.Grassland | AmbientHabitat.Farmland, Climate = new Vector2(Celsius(-25f), 1f),
					Activity = AmbientActivity.Crepuscular, ColdLimit = Celsius(-30f), RainLimit = 0.4f,
					FlushMetres = 9f, FleeSpeed = 9f,
					Back = Hex(0x8a7458), Belly = Hex(0xc8b89c), Accent = Hex(0xf0ece4), Smoothness = 0.15f,
				},
				new AmbientCreatureKind
				{
					Name = "Lizard", Shape = AmbientShape.Lizard, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.18f, 0.3f), GroupSize = new Vector2Int(1, 3), GroupsPerKm2 = 600f, Spread = 4f, Roam = new Vector2(2f, 5f),
					Speed = 0.8f, StrokeHz = 7f, Episode = new Vector2(5f, 15f),
					Habitats = AmbientHabitat.Desert | AmbientHabitat.Scrub | AmbientHabitat.Mountain | AmbientHabitat.Volcanic | AmbientHabitat.Coast
						| AmbientHabitat.Grassland | AmbientHabitat.Jungle | AmbientHabitat.Ruins,
					Prefers = AmbientHabitat.Desert | AmbientHabitat.Scrub | AmbientHabitat.Ruins, PreferWeight = 3f, Climate = new Vector2(Celsius(14f), 1f),
					// Ectotherms: below about 15 °C they cannot run, so they stay hidden; the hot noon that drives everything
					// else into the shade is when they bask.
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(15f), HeatLover = true, RainLimit = 0.2f, Hibernates = true,
					FlushMetres = 4f, FleeSpeed = 4f,
					Back = Hex(0x7a7048), Belly = Hex(0xb8b088), Accent = Hex(0x4a4430), Smoothness = 0.45f,
					Palette = new[] { Hex(0xffffff), Hex(0xd8c8a0), Hex(0xa0c090), Hex(0xc09878) },
				},
				new AmbientCreatureKind
				{
					Name = "Shore crab", Shape = AmbientShape.Crab, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.07f, 0.13f), GroupSize = new Vector2Int(2, 7), GroupsPerKm2 = 1400f, Spread = 4f, Roam = new Vector2(1f, 3f),
					Speed = 0.5f, StrokeHz = 3f, Episode = new Vector2(5f, 14f),
					Habitats = AmbientHabitat.Coast, NeedsSea = true, Climate = new Vector2(Celsius(4f), 1f),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(4f), RainLimit = 0f,
					FlushMetres = 4f, FleeSpeed = 1.5f,
					Back = Hex(0x8a4428), Belly = Hex(0xc87a4a), Accent = Hex(0x5a2a18), Smoothness = 0.5f,
					Palette = new[] { Hex(0xffffff), Hex(0xa8b070), Hex(0xd09070) },
					// The wet rock of a sea cave the tide works over, and a sea grotto's.
					CaveNiche = AmbientCaveNiche.Floor, CaveForms = AmbientCaveForms.SeaCave | AmbientCaveForms.Grotto, CavePerSpot = 0.6f, CaveDark = new Vector2(0f, 1f),
				},
				new AmbientCreatureKind
				{
					Name = "Frog", Shape = AmbientShape.Frog, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.06f, 0.1f), GroupSize = new Vector2Int(2, 6), GroupsPerKm2 = 1000f, Spread = 4f, Roam = new Vector2(1f, 3f),
					Speed = 0.7f, StrokeHz = 1.5f, Episode = new Vector2(6f, 18f),
					Habitats = AmbientHabitat.Wetland | AmbientHabitat.FreshWater | Wooded | AmbientHabitat.Grassland | AmbientHabitat.Farmland,
					Prefers = AmbientHabitat.Wetland, PreferWeight = 3f, NeedsFreshWater = true, Climate = new Vector2(Celsius(4f), 1f),
					// Frogs come out on warm wet nights and stay put in the cold (they bury in the mud below ~8 °C).
					Activity = AmbientActivity.Nocturnal, ColdLimit = Celsius(8f), RainLimit = 0f, RainLover = true, Hibernates = true,
					FlushMetres = 3f, FleeSpeed = 1.8f,
					Back = Hex(0x4a6a30), Belly = Hex(0xb0b080), Accent = Hex(0x2a3a18), Smoothness = 0.6f,
					Palette = new[] { Hex(0xffffff), Hex(0xb0a070), Hex(0x90b0a0) },
				},

				// ── More of the home world's ground and air ──
				new AmbientCreatureKind
				{
					// Brown and mountain hares: rangier than a rabbit, alone on open ground, crouched in a form by day and out
					// grazing in the half-light; bolts far and fast.
					Name = "Hare", Shape = AmbientShape.Rabbit, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.5f, 0.62f), GroupSize = new Vector2Int(1, 2), GroupsPerKm2 = 60f, Spread = 8f, Roam = new Vector2(5f, 12f),
					Speed = 1.6f, StrokeHz = 2.2f, Episode = new Vector2(8f, 22f),
					Habitats = OpenCountry | AmbientHabitat.Tundra | AmbientHabitat.Mountain | AmbientHabitat.Desert,
					Prefers = AmbientHabitat.Grassland | AmbientHabitat.Farmland | AmbientHabitat.Tundra, PreferWeight = 2.5f, Climate = new Vector2(Celsius(-30f), 1f),
					Activity = AmbientActivity.Crepuscular, ColdLimit = Celsius(-35f), RainLimit = 0.45f,
					FlushMetres = 14f, FleeSpeed = 12f,
					Back = Hex(0x8a7050), Belly = Hex(0xd8ccb8), Accent = Hex(0x2a2018), Smoothness = 0.15f,
					// A brown hare's tawny, a mountain hare's blue-grey.
					Palette = new[] { Hex(0xffffff), Hex(0xd8d4cc), Hex(0xf0e0c8) },
				},
				new AmbientCreatureKind
				{
					// Chipmunks: small ground squirrels of the woodland floor, darting between logs and stones by day, gone
					// below ground for the winter.
					Name = "Chipmunk", Shape = AmbientShape.Squirrel, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.12f, 0.15f), GroupSize = new Vector2Int(1, 2), GroupsPerKm2 = 260f, Spread = 3f, Roam = new Vector2(2f, 5f),
					Speed = 1.8f, StrokeHz = 5f, Episode = new Vector2(5f, 12f),
					Habitats = AmbientHabitat.Forest | AmbientHabitat.Woodland | AmbientHabitat.Scrub | AmbientHabitat.Mountain,
					Prefers = AmbientHabitat.Woodland, PreferWeight = 2f, Climate = new Vector2(Celsius(-10f), Celsius(28f)),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(2f), RainLimit = 0.3f, Hibernates = true,
					FlushMetres = 5f, FleeSpeed = 4f,
					Back = Hex(0x9a6a3a), Belly = Hex(0xe0c8a0), Accent = Hex(0x3a2414), Smoothness = 0.2f,
				},
				new AmbientCreatureKind
				{
					// Field voles: in short bursts round the clock, never far from their runs through the grass.
					Name = "Vole", Shape = AmbientShape.Mouse, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.09f, 0.12f), GroupSize = new Vector2Int(1, 4), GroupsPerKm2 = 600f, Spread = 2f, Roam = new Vector2(1f, 3f),
					Speed = 0.9f, StrokeHz = 8f, Episode = new Vector2(4f, 10f),
					Habitats = AmbientHabitat.Grassland | AmbientHabitat.Farmland | AmbientHabitat.Wetland | AmbientHabitat.Tundra | AmbientHabitat.Woodland,
					Prefers = AmbientHabitat.Grassland | AmbientHabitat.Wetland, PreferWeight = 2f, Climate = new Vector2(Celsius(-25f), Celsius(28f)),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(-25f), RainLimit = 0.5f,
					FlushMetres = 3f, FleeSpeed = 2f,
					Back = Hex(0x5a4a38), Belly = Hex(0x9a8c78), Accent = Hex(0x3a2e24), Smoothness = 0.25f,
				},
				new AmbientCreatureKind
				{
					// Toads: warty and slow, walking rather than leaping, out on mild damp nights far from any water.
					Name = "Toad", Shape = AmbientShape.Frog, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.08f, 0.12f), GroupSize = new Vector2Int(1, 2), GroupsPerKm2 = 400f, Spread = 5f, Roam = new Vector2(2f, 5f),
					Speed = 0.4f, StrokeHz = 1.2f, Episode = new Vector2(8f, 20f),
					Habitats = AmbientHabitat.Woodland | AmbientHabitat.Forest | AmbientHabitat.Farmland | AmbientHabitat.Grassland | AmbientHabitat.Settlement
						| AmbientHabitat.Ruins | AmbientHabitat.Scrub,
					Prefers = AmbientHabitat.Woodland | AmbientHabitat.Farmland, PreferWeight = 2f, Climate = new Vector2(Celsius(2f), 1f),
					Activity = AmbientActivity.Nocturnal, ColdLimit = Celsius(6f), RainLimit = 0f, RainLover = true, Hibernates = true,
					FlushMetres = 2f, FleeSpeed = 0.8f,
					Back = Hex(0x6a5034), Belly = Hex(0xb8a080), Accent = Hex(0x3a2a1a), Smoothness = 0.3f,
					Palette = new[] { Hex(0xffffff), Hex(0xb09070), Hex(0x8a8a60) },
				},
				new AmbientCreatureKind
				{
					// House geckos: pale, on warm walls and ruined stone after dark, hunting the moths that come to them.
					Name = "Gecko", Shape = AmbientShape.Lizard, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.1f, 0.16f), GroupSize = new Vector2Int(1, 2), GroupsPerKm2 = 500f, Spread = 3f, Roam = new Vector2(1f, 4f),
					Speed = 0.9f, StrokeHz = 8f, Episode = new Vector2(5f, 14f),
					Habitats = AmbientHabitat.Settlement | AmbientHabitat.Ruins | AmbientHabitat.Desert | AmbientHabitat.Scrub | AmbientHabitat.Jungle | AmbientHabitat.Coast,
					Prefers = AmbientHabitat.Settlement | AmbientHabitat.Ruins, PreferWeight = 3f, Climate = new Vector2(Celsius(18f), 1f),
					Activity = AmbientActivity.Nocturnal, ColdLimit = Celsius(16f), RainLimit = 0.3f, Hibernates = true,
					FlushMetres = 3f, FleeSpeed = 3f,
					Back = Hex(0xc0b090), Belly = Hex(0xe8e0c8), Accent = Hex(0x8a7a5a), Smoothness = 0.4f,
					Palette = new[] { Hex(0xffffff), Hex(0xd0d8b0), Hex(0xd8c0a0) },
				},
				new AmbientCreatureKind
				{
					// Ground beetles: glossy black hunters running over the leaf litter at night, a violet or green sheen in the light.
					Name = "Ground beetles", Shape = AmbientShape.Beetle, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.02f, 0.035f), GroupSize = new Vector2Int(1, 4), GroupsPerKm2 = 900f, Spread = 3f, Roam = new Vector2(1f, 3f),
					Speed = 0.25f, StrokeHz = 9f, Episode = new Vector2(5f, 12f),
					Habitats = Wooded | AmbientHabitat.Grassland | AmbientHabitat.Farmland | AmbientHabitat.Settlement | AmbientHabitat.Ruins | AmbientHabitat.Wetland,
					Prefers = AmbientHabitat.Forest | AmbientHabitat.Woodland, PreferWeight = 2f, Climate = new Vector2(Celsius(-2f), 1f),
					Activity = AmbientActivity.Nocturnal, ColdLimit = Celsius(5f), RainLimit = 0f, Hibernates = true,
					FlushMetres = 1f, FleeSpeed = 0.4f,
					Back = Hex(0x14141c), Belly = Hex(0x1c1c24), Accent = Hex(0x0c0c10), Smoothness = 0.85f,
					Palette = new[] { Hex(0xffffff), Hex(0x90a0d0), Hex(0x90c0a0) },
				},
				new AmbientCreatureKind
				{
					// Dung and scarab beetles: trundling over hot open ground in the sun.
					Name = "Dung beetles", Shape = AmbientShape.Beetle, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.02f, 0.032f), GroupSize = new Vector2Int(1, 3), GroupsPerKm2 = 500f, Spread = 4f, Roam = new Vector2(1f, 4f),
					Speed = 0.2f, StrokeHz = 7f, Episode = new Vector2(6f, 14f),
					Habitats = AmbientHabitat.Grassland | AmbientHabitat.Farmland | AmbientHabitat.Scrub | AmbientHabitat.Desert,
					Prefers = AmbientHabitat.Grassland | AmbientHabitat.Scrub, PreferWeight = 2f, Climate = new Vector2(Celsius(12f), 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(14f), HeatLover = true, RainLimit = 0.2f, Hibernates = true,
					FlushMetres = 1f, FleeSpeed = 0.3f,
					Back = Hex(0x1a1610), Belly = Hex(0x2a2418), Accent = Hex(0x0e0c08), Smoothness = 0.7f,
					Palette = new[] { Hex(0xffffff), Hex(0x8ab070), Hex(0xb09060) },
				},
				new AmbientCreatureKind
				{
					// Grasshoppers and crickets: in the warm grass by day, springing away from a foot.
					Name = "Grasshoppers", Shape = AmbientShape.Beetle, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.03f, 0.05f), GroupSize = new Vector2Int(2, 6), GroupsPerKm2 = 1200f, Spread = 4f, Roam = new Vector2(1f, 4f),
					Speed = 0.5f, StrokeHz = 6f, Episode = new Vector2(4f, 10f),
					Habitats = AmbientHabitat.Grassland | AmbientHabitat.Farmland | AmbientHabitat.Scrub | AmbientHabitat.Mountain,
					Prefers = AmbientHabitat.Grassland, PreferWeight = 2.5f, Climate = new Vector2(Celsius(10f), 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(15f), HeatLover = true, RainLimit = 0.15f, Hibernates = true,
					FlushMetres = 1.5f, FleeSpeed = 1.5f,
					Back = Hex(0x6a8a3a), Belly = Hex(0x9ab060), Accent = Hex(0x3a4a20), Smoothness = 0.3f,
					Palette = new[] { Hex(0xffffff), Hex(0xc0b070), Hex(0xa0c070) },
				},
				new AmbientCreatureKind
				{
					// Night moths: pale wings fluttering low over the grass and round the walls of a house after dark.
					Name = "Moths", Shape = AmbientShape.Butterfly, Behaviour = AmbientBehaviour.Hawk, Family = AmbientFamily.Bats,
					Length = new Vector2(0.025f, 0.04f), GroupSize = new Vector2Int(1, 4), GroupsPerKm2 = 500f, Spread = 6f, Roam = new Vector2(4f, 10f),
					Speed = 1.3f, StrokeHz = 12f, Altitude = new Vector2(0.5f, 3f),
					Habitats = OpenCountry | Wooded | AmbientHabitat.Settlement | AmbientHabitat.Wetland | AmbientHabitat.Ruins,
					Prefers = AmbientHabitat.Woodland | AmbientHabitat.Settlement, PreferWeight = 2f, Climate = new Vector2(Celsius(4f), 1f),
					Activity = AmbientActivity.Nocturnal, ColdLimit = Celsius(7f), RainLimit = 0.15f, Hibernates = true,
					FlushMetres = 0f,
					Back = Hex(0xc8bca0), Belly = Hex(0xb0a488), Accent = Hex(0x5a4e3a), Smoothness = 0.05f, CastShadows = false,
					Palette = new[] { Hex(0xffffff), Hex(0xd8d0c0), Hex(0xa89880), Hex(0xe8e0d0) },
				},
				new AmbientCreatureKind
				{
					// Swallows and martins hawking insects over meadows and water: low, fast, swerving, all summer long.
					Name = "Swallows", Shape = AmbientShape.Songbird, Behaviour = AmbientBehaviour.Hawk, Family = AmbientFamily.Bats,
					Length = new Vector2(0.16f, 0.19f), GroupSize = new Vector2Int(2, 6), GroupsPerKm2 = 40f, Spread = 15f, Roam = new Vector2(15f, 35f),
					Speed = 9f, StrokeHz = 9f, Altitude = new Vector2(1.5f, 14f),
					Habitats = AmbientHabitat.Farmland | AmbientHabitat.Grassland | AmbientHabitat.Wetland | AmbientHabitat.FreshWater | AmbientHabitat.Settlement | AmbientHabitat.Coast,
					Prefers = AmbientHabitat.Farmland | AmbientHabitat.Wetland | AmbientHabitat.FreshWater, PreferWeight = 2.5f, Climate = new Vector2(Celsius(8f), 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(10f), RainLimit = 0.35f, Migrates = true,
					FlushMetres = 0f,
					Back = Hex(0x1a2440), Belly = Hex(0xe8e0d0), Accent = Hex(0x8a3a20), Smoothness = 0.5f, CastShadows = false,
				},
				new AmbientCreatureKind
				{
					// Hummingbirds: a hum of wings at a flower, gone in a blink, back again.
					Name = "Hummingbirds", Shape = AmbientShape.Songbird, Behaviour = AmbientBehaviour.Hawk, Family = AmbientFamily.Bats,
					Length = new Vector2(0.08f, 0.1f), GroupSize = new Vector2Int(1, 2), GroupsPerKm2 = 200f, Spread = 4f, Roam = new Vector2(3f, 7f),
					Speed = 3f, StrokeHz = 50f, Altitude = new Vector2(0.6f, 3f),
					Habitats = AmbientHabitat.Jungle | AmbientHabitat.Woodland | AmbientHabitat.Scrub | AmbientHabitat.Settlement | AmbientHabitat.Mountain,
					Prefers = AmbientHabitat.Jungle, PreferWeight = 3f, Climate = new Vector2(Celsius(16f), 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(12f), RainLimit = 0.3f, Migrates = true,
					FlushMetres = 0f,
					Back = Hex(0x2a7a50), Belly = Hex(0xc8d8c0), Accent = Hex(0x1a1a1a), Smoothness = 0.75f, CastShadows = false,
					// A ruby throat, a violet crown, a gold.
					Palette = new[] { Hex(0xffffff), Hex(0xd06080), Hex(0x60a0e0), Hex(0xe0c050) },
				},
				new AmbientCreatureKind
				{
					// Parrots: loud bright flocks crossing the jungle canopy morning and evening.
					Name = "Parrots", Shape = AmbientShape.Songbird, Behaviour = AmbientBehaviour.Flock, Family = AmbientFamily.Flocks,
					Length = new Vector2(0.3f, 0.42f), GroupSize = new Vector2Int(4, 14), GroupsPerKm2 = 10f, Spread = 6f, Roam = new Vector2(50f, 100f),
					Speed = 11f, StrokeHz = 8f, Altitude = new Vector2(10f, 35f), Episode = new Vector2(60f, 120f),
					Habitats = AmbientHabitat.Jungle | AmbientHabitat.Woodland | AmbientHabitat.Scrub | AmbientHabitat.Grassland,
					Prefers = AmbientHabitat.Jungle, PreferWeight = 3f, Climate = new Vector2(Celsius(18f), 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(10f), RainLimit = 0.5f,
					FlushMetres = 15f, FleeSpeed = 11f,
					Back = Hex(0x2a9a3a), Belly = Hex(0x8ac040), Accent = Hex(0x2a2a2a), Smoothness = 0.45f,
					// Green, a macaw's scarlet, a blue, a yellow.
					Palette = new[] { Hex(0xffffff), Hex(0xe04040), Hex(0x4090e0), Hex(0xf0d040) },
				},
				new AmbientCreatureKind
				{
					// Terns: white and grey, black-capped, beating along the shore and dropping on fish.
					Name = "Terns", Shape = AmbientShape.Gull, Behaviour = AmbientBehaviour.Flock, Family = AmbientFamily.Flocks,
					Length = new Vector2(0.3f, 0.38f), GroupSize = new Vector2Int(3, 14), GroupsPerKm2 = 12f, Spread = 7f, Roam = new Vector2(50f, 110f),
					Speed = 10f, StrokeHz = 3.6f, Altitude = new Vector2(4f, 20f), Episode = new Vector2(60f, 120f),
					Habitats = AmbientHabitat.Coast | AmbientHabitat.Sea | AmbientHabitat.FreshWater | AmbientHabitat.Wetland,
					Prefers = AmbientHabitat.Coast, PreferWeight = 3f, NeedsSea = true, Climate = new Vector2(Celsius(0f), 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(2f), RainLimit = 0.5f, Migrates = true,
					FlushMetres = 14f, FleeSpeed = 9f,
					Back = Hex(0xc8ccd0), Belly = Hex(0xf6f6f2), Accent = Hex(0x101012), Smoothness = 0.3f,
				},

				// ── Insect swarms: one cloud each, its insects flown by the shader (GroupSize is insects, Spread the cloud's radius) ──
				new AmbientCreatureKind
				{
					// Non-biting midges dancing in a column over a marker by water or a bush, thickest at dusk and dawn, catching
					// the low sun.
					Name = "Midge column", Shape = AmbientShape.Swarm, Behaviour = AmbientBehaviour.Swarm, Family = AmbientFamily.Swarms, SwarmStyle = AmbientSwarmStyle.Column,
					Length = new Vector2(0.004f, 0.006f), GroupSize = new Vector2Int(50, 110), GroupsPerKm2 = 1200f, Spread = 0.8f, Roam = new Vector2(1f, 3f),
					Speed = 0.3f, Altitude = new Vector2(1.2f, 3.5f), Episode = new Vector2(30f, 80f),
					Habitats = AmbientHabitat.Wetland | AmbientHabitat.FreshWater | Wooded | AmbientHabitat.Grassland | AmbientHabitat.Farmland | AmbientHabitat.Settlement
						| AmbientHabitat.Tundra,
					Prefers = AmbientHabitat.Wetland | AmbientHabitat.FreshWater, PreferWeight = 3f, Climate = new Vector2(Celsius(-5f), 1f),
					Activity = AmbientActivity.Crepuscular, ColdLimit = Celsius(3f), RainLimit = 0.6f, Hibernates = true,
					FlushMetres = 0f,
					Back = Hex(0x3a3630), Belly = Hex(0x4a4640), Accent = Hex(0x2a2622), Smoothness = 0.2f, CastShadows = false,
				},
				new AmbientCreatureKind
				{
					// Gnats and fungus flies: small loose clouds hanging in the shade of trees and over the grass on any mild
					// day, drizzle or not; the bugs an ordinary afternoon has.
					Name = "Gnat cloud", Shape = AmbientShape.Swarm, Behaviour = AmbientBehaviour.Swarm, Family = AmbientFamily.Swarms, SwarmStyle = AmbientSwarmStyle.Cloud,
					Length = new Vector2(0.003f, 0.005f), GroupSize = new Vector2Int(20, 50), GroupsPerKm2 = 1500f, Spread = 1.2f, Roam = new Vector2(1f, 4f),
					Speed = 0.3f, Altitude = new Vector2(0.8f, 2.5f), Episode = new Vector2(30f, 70f),
					Habitats = Wooded | AmbientHabitat.Grassland | AmbientHabitat.Farmland | AmbientHabitat.Wetland | AmbientHabitat.FreshWater | AmbientHabitat.Settlement
						| AmbientHabitat.Scrub | AmbientHabitat.Tundra | AmbientHabitat.Mountain,
					Prefers = Wooded | AmbientHabitat.Wetland, PreferWeight = 2f, Climate = new Vector2(Celsius(-5f), 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(4f), RainLimit = 0.4f, Hibernates = true,
					FlushMetres = 0f,
					Back = Hex(0x34302a), Belly = Hex(0x48443c), Accent = Hex(0x24201c), Smoothness = 0.2f, CastShadows = false,
				},
				new AmbientCreatureKind
				{
					// Mosquitoes: a whining cloud over marsh and still water, out from dusk into the night; the tundra's summer
					// clouds of them.
					Name = "Mosquito cloud", Shape = AmbientShape.Swarm, Behaviour = AmbientBehaviour.Swarm, Family = AmbientFamily.Swarms, SwarmStyle = AmbientSwarmStyle.Cloud,
					Length = new Vector2(0.005f, 0.008f), GroupSize = new Vector2Int(25, 60), GroupsPerKm2 = 1200f, Spread = 1.6f, Roam = new Vector2(1f, 4f),
					Speed = 0.4f, Altitude = new Vector2(0.6f, 1.8f), Episode = new Vector2(30f, 70f),
					Habitats = AmbientHabitat.Wetland | AmbientHabitat.FreshWater | AmbientHabitat.Jungle | AmbientHabitat.Forest | AmbientHabitat.Woodland | AmbientHabitat.Tundra,
					Prefers = AmbientHabitat.Wetland | AmbientHabitat.Jungle | AmbientHabitat.Tundra, PreferWeight = 3f, Climate = new Vector2(Celsius(-8f), 1f),
					Activity = AmbientActivity.Dusk, ColdLimit = Celsius(8f), RainLimit = 0.35f, Hibernates = true,
					FlushMetres = 0f,
					Back = Hex(0x2a2622), Belly = Hex(0x3a342e), Accent = Hex(0x1a1612), Smoothness = 0.2f, CastShadows = false,
				},
				new AmbientCreatureKind
				{
					// House and blow flies buzzing round a spot by a farmyard, a midden or a carcass in the heat of the day.
					Name = "Fly swarm", Shape = AmbientShape.Swarm, Behaviour = AmbientBehaviour.Swarm, Family = AmbientFamily.Swarms, SwarmStyle = AmbientSwarmStyle.Buzz,
					Length = new Vector2(0.006f, 0.009f), GroupSize = new Vector2Int(10, 26), GroupsPerKm2 = 1500f, Spread = 0.6f, Roam = new Vector2(2f, 6f),
					Speed = 0.6f, Altitude = new Vector2(0.3f, 1.2f), Episode = new Vector2(15f, 40f),
					Habitats = AmbientHabitat.Farmland | AmbientHabitat.Settlement | AmbientHabitat.Grassland | AmbientHabitat.Scrub | AmbientHabitat.Desert
						| AmbientHabitat.Jungle | AmbientHabitat.Ruins | AmbientHabitat.Coast | AmbientHabitat.Wetland | AmbientHabitat.Woodland | AmbientHabitat.Forest,
					Prefers = AmbientHabitat.Farmland | AmbientHabitat.Settlement | AmbientHabitat.Ruins, PreferWeight = 3f, Climate = new Vector2(Celsius(5f), 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(8f), HeatLover = true, RainLimit = 0.3f, Hibernates = true,
					FlushMetres = 0f,
					Back = Hex(0x1a1a1e), Belly = Hex(0x26262a), Accent = Hex(0x3a5a4a), Smoothness = 0.7f, CastShadows = false,
				},
				new AmbientCreatureKind
				{
					// Fireflies: a meadow's edge or a marsh full of slow-drifting lights on a warm summer night, each flashing on
					// its own rhythm.
					Name = "Firefly swarm", Shape = AmbientShape.Swarm, Behaviour = AmbientBehaviour.Swarm, Family = AmbientFamily.Swarms, SwarmStyle = AmbientSwarmStyle.Drift,
					Length = new Vector2(0.01f, 0.014f), GroupSize = new Vector2Int(14, 36), GroupsPerKm2 = 900f, Spread = 5f, Roam = new Vector2(2f, 6f),
					Speed = 0.15f, Altitude = new Vector2(0.4f, 1.6f), Episode = new Vector2(40f, 90f),
					Habitats = AmbientHabitat.Wetland | AmbientHabitat.FreshWater | AmbientHabitat.Grassland | Wooded | AmbientHabitat.Farmland,
					Prefers = AmbientHabitat.Wetland | AmbientHabitat.Woodland | AmbientHabitat.Jungle, PreferWeight = 2.5f, Climate = new Vector2(Celsius(8f), 1f),
					Activity = AmbientActivity.Nocturnal, ColdLimit = Celsius(12f), RainLimit = 0.2f, Hibernates = true,
					FlushMetres = 0f,
					Back = Hex(0x2a2418), Belly = Hex(0x3a3220), Accent = Hex(0xc8ff50), Smoothness = 0.3f, CastShadows = false, Glow = 6f,
				},
				new AmbientCreatureKind
				{
					// Honeybees, bumblebees and hoverflies working a patch of flowers on a sunny day.
					Name = "Bees", Shape = AmbientShape.Swarm, Behaviour = AmbientBehaviour.Swarm, Family = AmbientFamily.Swarms, SwarmStyle = AmbientSwarmStyle.Hover,
					Length = new Vector2(0.012f, 0.016f), GroupSize = new Vector2Int(6, 18), GroupsPerKm2 = 1200f, Spread = 2.2f, Roam = new Vector2(3f, 8f),
					Speed = 0.25f, Altitude = new Vector2(0.25f, 0.8f), Episode = new Vector2(20f, 50f),
					Habitats = AmbientHabitat.Grassland | AmbientHabitat.Farmland | AmbientHabitat.Settlement | AmbientHabitat.Scrub | AmbientHabitat.Woodland
						| AmbientHabitat.Mountain | AmbientHabitat.Jungle,
					Prefers = AmbientHabitat.Grassland | AmbientHabitat.Farmland | AmbientHabitat.Settlement, PreferWeight = 2.5f, Climate = new Vector2(Celsius(6f), 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(10f), HeatLover = true, RainLimit = 0.15f, Hibernates = true,
					FlushMetres = 0f,
					Back = Hex(0xd0a020), Belly = Hex(0x2a2010), Accent = Hex(0x1a1408), Smoothness = 0.3f, CastShadows = false,
				},
				new AmbientCreatureKind
				{
					// A locust swarm: rare, wide and low over hot dry country, streaming downwind and churning as it goes.
					Name = "Locust swarm", Shape = AmbientShape.Swarm, Behaviour = AmbientBehaviour.Swarm, Family = AmbientFamily.Swarms, SwarmStyle = AmbientSwarmStyle.Plague,
					Length = new Vector2(0.035f, 0.05f), GroupSize = new Vector2Int(120, 160), GroupsPerKm2 = 6f, Spread = 18f, Roam = new Vector2(40f, 90f),
					Speed = 2.5f, Altitude = new Vector2(1f, 5f), Episode = new Vector2(60f, 140f),
					Habitats = AmbientHabitat.Desert | AmbientHabitat.Scrub | AmbientHabitat.Grassland | AmbientHabitat.Farmland,
					Prefers = AmbientHabitat.Scrub | AmbientHabitat.Grassland, PreferWeight = 2f, Climate = new Vector2(Celsius(22f), 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = Celsius(20f), HeatLover = true, RainLimit = 0.2f, Hibernates = true,
					FlushMetres = 0f,
					Back = Hex(0x8a7040), Belly = Hex(0xb0985a), Accent = Hex(0x4a3a20), Smoothness = 0.3f, CastShadows = false,
				},

				// ── Thin air: nothing flies, everything crawls or basks ──
				new AmbientCreatureKind
				{
					// Rust-red beetles the size of a thumbnail, working the dust in little bands and freezing still in the sun.
					Name = "Dust skitters", Shape = AmbientShape.Beetle, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Worlds = AmbientWorld.ThinAir,
					Length = new Vector2(0.05f, 0.09f), GroupSize = new Vector2Int(2, 8), GroupsPerKm2 = 900f, Spread = 4f, Roam = new Vector2(1f, 4f),
					Speed = 0.6f, StrokeHz = 8f, Episode = new Vector2(5f, 14f),
					Habitats = OtherGround | AmbientHabitat.Ice | AmbientHabitat.Coast, Prefers = AmbientHabitat.Alien | AmbientHabitat.Desert, PreferWeight = 2.5f,
					Climate = new Vector2(-1f, 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = NeverTooCold, HeatLover = true, RainLimit = 0.3f,
					FlushMetres = 2.5f, FleeSpeed = 1.5f,
					Back = Hex(0x8a4a2a), Belly = Hex(0x5a3a28), Accent = Hex(0x2a1a12), Smoothness = 0.7f,
					Palette = new[] { Hex(0xffffff), Hex(0xd8a070), Hex(0xa89078) },
				},
				new AmbientCreatureKind
				{
					// Drakelings: drake-kin no longer than a forearm, red-scaled and gold-bellied, basking on warm rock and
					// darting into the cracks.
					Name = "Sand drakelings", Shape = AmbientShape.Lizard, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Worlds = AmbientWorld.ThinAir,
					Length = new Vector2(0.25f, 0.4f), GroupSize = new Vector2Int(1, 2), GroupsPerKm2 = 220f, Spread = 5f, Roam = new Vector2(3f, 8f),
					Speed = 0.9f, StrokeHz = 6f, Episode = new Vector2(6f, 16f),
					Habitats = OtherGround, Prefers = AmbientHabitat.Mountain | AmbientHabitat.Volcanic, PreferWeight = 3f, Climate = new Vector2(-1f, 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = NeverTooCold, HeatLover = true, RainLimit = 0.2f,
					FlushMetres = 5f, FleeSpeed = 4f,
					Back = Hex(0x6a3022), Belly = Hex(0xc8963a), Accent = Hex(0x2a1410), Smoothness = 0.5f,
					Palette = new[] { Hex(0xffffff), Hex(0xd8b090), Hex(0xa08070) },
				},
				new AmbientCreatureKind
				{
					// Stone crabs: grey as the scree they sidle over, out at dusk and dawn, hidden under slabs through the glare.
					Name = "Stone crabs", Shape = AmbientShape.Crab, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Worlds = AmbientWorld.ThinAir,
					Length = new Vector2(0.12f, 0.22f), GroupSize = new Vector2Int(1, 4), GroupsPerKm2 = 350f, Spread = 4f, Roam = new Vector2(1f, 4f),
					Speed = 0.4f, StrokeHz = 2.5f, Episode = new Vector2(6f, 16f),
					Habitats = OtherGround, Prefers = AmbientHabitat.Mountain | AmbientHabitat.Cave, PreferWeight = 2.5f, Climate = new Vector2(-1f, 1f),
					Activity = AmbientActivity.Crepuscular, ColdLimit = NeverTooCold, RainLimit = 0f,
					FlushMetres = 3f, FleeSpeed = 1.2f,
					Back = Hex(0x5a5a58), Belly = Hex(0x8a867c), Accent = Hex(0x3a3a36), Smoothness = 0.3f,
					Palette = new[] { Hex(0xffffff), Hex(0xb0a890), Hex(0x9098a0) },
				},

				// ── Frozen air: dense and bitter cold, light bodies fly in it with ease ──
				new AmbientCreatureKind
				{
					// Glossy blue-black beetles with frost-pale legs, out at any hour on the ice and the tholin sands.
					Name = "Rime beetles", Shape = AmbientShape.Beetle, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Worlds = AmbientWorld.Cryogenic,
					Length = new Vector2(0.06f, 0.12f), GroupSize = new Vector2Int(2, 6), GroupsPerKm2 = 800f, Spread = 4f, Roam = new Vector2(1f, 4f),
					Speed = 0.5f, StrokeHz = 6f, Episode = new Vector2(6f, 16f),
					Habitats = OtherGround | AmbientHabitat.Ice | AmbientHabitat.Coast | AmbientHabitat.Wetland, Prefers = AmbientHabitat.Ice | AmbientHabitat.Alien, PreferWeight = 2f,
					Climate = new Vector2(-1f, 1f),
					Activity = AmbientActivity.AnyTime, ColdLimit = NeverTooCold, RainLimit = 0.6f,
					FlushMetres = 2.5f, FleeSpeed = 1.4f,
					Back = Hex(0x1e2a3e), Belly = Hex(0x2a3448), Accent = Hex(0x8ab0d0), Smoothness = 0.8f,
					Palette = new[] { Hex(0xffffff), Hex(0xa0c0e0), Hex(0x9080b0) },
				},
				new AmbientCreatureKind
				{
					// Frost moths: pale wings a hand across, drifting low over the ice in slow fits.
					Name = "Frost moths", Shape = AmbientShape.Butterfly, Behaviour = AmbientBehaviour.Hawk, Family = AmbientFamily.Bats,
					Worlds = AmbientWorld.Cryogenic,
					Length = new Vector2(0.12f, 0.22f), GroupSize = new Vector2Int(1, 4), GroupsPerKm2 = 260f, Spread = 6f, Roam = new Vector2(5f, 12f),
					Speed = 1.2f, StrokeHz = 2.5f, Altitude = new Vector2(0.6f, 4f),
					Habitats = OtherGround | AmbientHabitat.Ice | AmbientHabitat.Coast | AmbientHabitat.Wetland, Prefers = AmbientHabitat.Alien | AmbientHabitat.Ice, PreferWeight = 2f,
					Climate = new Vector2(-1f, 1f),
					Activity = AmbientActivity.AnyTime, ColdLimit = NeverTooCold, RainLimit = 0.3f,
					FlushMetres = 0f,
					Back = Hex(0xd8e4f0), Belly = Hex(0xc0d0e0), Accent = Hex(0x405070), Smoothness = 0.15f, CastShadows = false,
					Palette = new[] { Hex(0xffffff), Hex(0xc8e0ff), Hex(0xe0d0ff), Hex(0xb8f0e0) },
				},
				new AmbientCreatureKind
				{
					// Sky rays: dusk-violet wings two strides across, rowing slowly high over the ice and gliding for minutes.
					Name = "Sky rays", Shape = AmbientShape.Ray, Behaviour = AmbientBehaviour.Soar, Family = AmbientFamily.Raptors,
					Worlds = AmbientWorld.Cryogenic,
					Length = new Vector2(1.4f, 2.6f), GroupSize = new Vector2Int(1, 3), GroupsPerKm2 = 2.5f, Spread = 30f, Roam = new Vector2(150f, 320f),
					Speed = 6f, StrokeHz = 0.5f, Altitude = new Vector2(40f, 180f), Episode = new Vector2(100f, 200f),
					Habitats = OtherGround | AmbientHabitat.Ice | AmbientHabitat.Coast | AmbientHabitat.Wetland, Prefers = AmbientHabitat.Alien | AmbientHabitat.Ice, PreferWeight = 2f,
					Climate = new Vector2(-1f, 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = NeverTooCold, RainLimit = 0.3f,
					FlushMetres = 0f,
					Back = Hex(0x3a3450), Belly = Hex(0xb8b8d0), Accent = Hex(0x1a1828), Smoothness = 0.4f,
					Palette = new[] { Hex(0xffffff), Hex(0xa8c0d8), Hex(0xc8a8d0) },
				},
				new AmbientCreatureKind
				{
					// Glimmer gnats: a faint blue haze of tiny fliers over the ice, each lit from within, at any hour.
					Name = "Glimmer gnats", Shape = AmbientShape.Swarm, Behaviour = AmbientBehaviour.Swarm, Family = AmbientFamily.Swarms, SwarmStyle = AmbientSwarmStyle.Cloud,
					Worlds = AmbientWorld.Cryogenic,
					Length = new Vector2(0.006f, 0.01f), GroupSize = new Vector2Int(30, 70), GroupsPerKm2 = 1000f, Spread = 2f, Roam = new Vector2(2f, 5f),
					Speed = 0.3f, Altitude = new Vector2(0.6f, 3f), Episode = new Vector2(30f, 80f),
					Habitats = OtherGround | AmbientHabitat.Ice | AmbientHabitat.Coast | AmbientHabitat.Wetland, Prefers = AmbientHabitat.Alien | AmbientHabitat.Ice, PreferWeight = 2f,
					Climate = new Vector2(-1f, 1f),
					Activity = AmbientActivity.AnyTime, ColdLimit = NeverTooCold, RainLimit = 0.3f,
					FlushMetres = 0f,
					Back = Hex(0x405070), Belly = Hex(0x506080), Accent = Hex(0x80c8ff), Smoothness = 0.3f, CastShadows = false, Glow = 2.5f,
				},
				new AmbientCreatureKind
				{
					// Kite-rays: palm-sized rays in shoals of a dozen or more, wheeling low like a flock of finches.
					Name = "Kite-ray shoal", Shape = AmbientShape.Ray, Behaviour = AmbientBehaviour.Flock, Family = AmbientFamily.Flocks,
					Worlds = AmbientWorld.Cryogenic,
					Length = new Vector2(0.25f, 0.4f), GroupSize = new Vector2Int(6, 18), GroupsPerKm2 = 8f, Spread = 7f, Roam = new Vector2(60f, 130f),
					Speed = 5f, StrokeHz = 1.2f, Altitude = new Vector2(6f, 30f), Episode = new Vector2(70f, 140f),
					Habitats = OtherGround | AmbientHabitat.Ice | AmbientHabitat.Coast | AmbientHabitat.Wetland, Prefers = AmbientHabitat.Alien, PreferWeight = 2f,
					Climate = new Vector2(-1f, 1f),
					Activity = AmbientActivity.Diurnal, ColdLimit = NeverTooCold, RainLimit = 0.3f,
					FlushMetres = 10f, FleeSpeed = 6f,
					Back = Hex(0x4a5a78), Belly = Hex(0xd0d8e8), Accent = Hex(0x20283a), Smoothness = 0.35f,
					Palette = new[] { Hex(0xffffff), Hex(0xb0d0f0), Hex(0xd0c0f0), Hex(0xa0e0d0) },
				},

				// ── The caves: placed from the cut's cave-life spots, never on the open land (GroupsPerKm2 0). Underground the
				// hour, the weather and the season's heat do not reach (AmbientGating.CaveActivity): the air is the ground's year mean (AmbientCaveLife.CaveAir). ──
				new AmbientCreatureKind
				{
					// Horseshoe and long-eared bats: colonies hanging wing-wrapped from the high roofs by day and through the
					// winter's sleep, circling the cave at dusk and streaming out of its mouth into the night, back before dawn.
					// GroupSize is a roost's colony (a box of air or a mouth's stream has its own share of it).
					Name = "Cave bats", Shape = AmbientShape.Bat, Behaviour = AmbientBehaviour.Hawk, Family = AmbientFamily.Bats,
					Length = new Vector2(0.07f, 0.1f), GroupSize = new Vector2Int(6, 28), GroupsPerKm2 = 0f, Spread = 1.2f, Roam = new Vector2(2f, 6f),
					Speed = 5.5f, StrokeHz = 11f, Altitude = new Vector2(0f, 0f),
					Habitats = AmbientHabitat.Cave, Climate = new Vector2(Celsius(-2f), 1f),
					// Always in the cave (hanging when not flying: AmbientCaveLife reads the night outside for when they go).
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(-6f), RainLimit = 0f,
					FlushMetres = 0f,
					Back = Hex(0x3a2e26), Belly = Hex(0x5a4a3e), Accent = Hex(0x1e1814), Smoothness = 0.2f, CastShadows = false,
					Palette = new[] { Hex(0xffffff), Hex(0xd8c8b8), Hex(0xb0a8a0) },
					CaveNiche = AmbientCaveNiche.Roost | AmbientCaveNiche.Flight | AmbientCaveNiche.Mouth,
					CaveForms = AmbientCaveForms.Rock | AmbientCaveForms.IceCave, CavePerSpot = 1f, CaveDark = new Vector2(0f, 1f),
				},
				new AmbientCreatureKind
				{
					// Cave crickets: humped and wingless, on long legs, feelers sweeping; in crowds on the walls and floors from
					// just inside the mouth inward, the commonest thing alive in a cave, up to the cold mountains' caves at the
					// edge of the frost (as the alpine cave wētā).
					Name = "Cave crickets", Shape = AmbientShape.Cricket, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.035f, 0.055f), GroupSize = new Vector2Int(4, 12), GroupsPerKm2 = 0f, Spread = 0.6f, Roam = new Vector2(0.3f, 0.9f),
					Speed = 0.35f, StrokeHz = 6f, Episode = new Vector2(5f, 14f),
					Habitats = AmbientHabitat.Cave, Climate = new Vector2(Celsius(0f), 1f),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(-1f), RainLimit = 0f,
					FlushMetres = 1.2f, FleeSpeed = 1.2f,
					Back = Hex(0x8a6a48), Belly = Hex(0xb89a78), Accent = Hex(0x5a4430), Smoothness = 0.35f, CastShadows = false,
					Palette = new[] { Hex(0xffffff), Hex(0xd8c8a8), Hex(0xc0a888) },
					CaveNiche = AmbientCaveNiche.Floor | AmbientCaveNiche.Wall, CaveForms = AmbientCaveForms.Rock, CavePerSpot = 1.4f, CaveDark = new Vector2(0.05f, 1f),
				},
				new AmbientCreatureKind
				{
					// Cave spiders: long-legged and dark, waiting on the walls for what blunders in from outside, and pale blind
					// kin of theirs all the way into the deep dark hunting the crickets; the sheet-web kin hunt on in caves
					// barely above freezing.
					Name = "Cave spiders", Shape = AmbientShape.Spider, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.025f, 0.04f), GroupSize = new Vector2Int(1, 4), GroupsPerKm2 = 0f, Spread = 0.5f, Roam = new Vector2(0.15f, 0.5f),
					Speed = 0.3f, StrokeHz = 5f, Episode = new Vector2(10f, 30f),
					Habitats = AmbientHabitat.Cave, Climate = new Vector2(Celsius(-1f), 1f),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(-2f), RainLimit = 0f,
					FlushMetres = 0.8f, FleeSpeed = 0.8f,
					Back = Hex(0x3a2e24), Belly = Hex(0x5a4a3a), Accent = Hex(0x7a5a3a), Smoothness = 0.5f, CastShadows = false,
					Palette = new[] { Hex(0xffffff), Hex(0xc8b098), Hex(0x9a9088) },
					CaveNiche = AmbientCaveNiche.Wall | AmbientCaveNiche.Floor, CaveForms = AmbientCaveForms.Rock, CavePerSpot = 0.9f, CaveDark = new Vector2(0.05f, 1f),
				},
				new AmbientCreatureKind
				{
					// Cave salamanders: slow, slender and slick, on the dripping walls and wet floors of a limestone cave where the
					// air never dries and never warms.
					Name = "Cave salamander", Shape = AmbientShape.Lizard, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.09f, 0.13f), GroupSize = new Vector2Int(1, 2), GroupsPerKm2 = 0f, Spread = 0.6f, Roam = new Vector2(0.3f, 0.8f),
					Speed = 0.12f, StrokeHz = 2.5f, Episode = new Vector2(12f, 30f), RainLover = true,
					Habitats = AmbientHabitat.Cave, Climate = new Vector2(Celsius(3f), Celsius(20f)),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(2f), RainLimit = 0f,
					FlushMetres = 1f, FleeSpeed = 0.5f,
					Back = Hex(0x5a3a2a), Belly = Hex(0x8a6a4a), Accent = Hex(0xa87a3a), Smoothness = 0.75f, CastShadows = false,
					Palette = new[] { Hex(0xffffff), Hex(0xc0a890), Hex(0x9a8a7a) },
					CaveNiche = AmbientCaveNiche.Floor | AmbientCaveNiche.Wall, CaveForms = AmbientCaveForms.Cave | AmbientCaveForms.Grotto,
					CavePerSpot = 0.8f, CaveDark = new Vector2(0.2f, 1f), CaveNeedsDamp = true,
				},
				new AmbientCreatureKind
				{
					// Olms: blind pink-white cave salamanders a hand and more long, red gill-tufts, swimming slowly in the
					// still fresh water of a cave's pools.
					Name = "Olm", Shape = AmbientShape.Lizard, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.2f, 0.28f), GroupSize = new Vector2Int(1, 3), GroupsPerKm2 = 0f, Spread = 0.8f, Roam = new Vector2(0.5f, 1.5f),
					Speed = 0.08f, StrokeHz = 1.6f, Episode = new Vector2(20f, 50f), RainLover = true,
					Habitats = AmbientHabitat.Cave, Climate = new Vector2(Celsius(3f), Celsius(18f)),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(2f), RainLimit = 0f,
					FlushMetres = 0f,
					Back = Hex(0xe8c8c0), Belly = Hex(0xf0dcd4), Accent = Hex(0xd06a6a), Smoothness = 0.8f, CastShadows = false,
					CaveNiche = AmbientCaveNiche.Pool, CaveForms = AmbientCaveForms.Cave | AmbientCaveForms.Grotto, CavePerSpot = 1.2f, CaveDark = new Vector2(0f, 1f),
					CaveNeedsDamp = true,
				},
				new AmbientCreatureKind
				{
					// Herald moths and their kin: wintering in a cave's mouth, fluttering in the half-light just inside it.
					Name = "Cave moths", Shape = AmbientShape.Butterfly, Behaviour = AmbientBehaviour.Hawk, Family = AmbientFamily.Bats,
					Length = new Vector2(0.03f, 0.045f), GroupSize = new Vector2Int(1, 4), GroupsPerKm2 = 0f, Spread = 2f, Roam = new Vector2(2f, 4f),
					Speed = 1f, StrokeHz = 10f, Altitude = new Vector2(0f, 0f),
					Habitats = AmbientHabitat.Cave, Climate = new Vector2(Celsius(0f), 1f),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(1f), RainLimit = 0f,
					FlushMetres = 0f,
					Back = Hex(0xb08050), Belly = Hex(0xc8a070), Accent = Hex(0x6a4a2a), Smoothness = 0.05f, CastShadows = false,
					Palette = new[] { Hex(0xffffff), Hex(0xe0c0a0), Hex(0xc8b8a0) },
					CaveNiche = AmbientCaveNiche.Flight, CaveForms = AmbientCaveForms.Rock, CavePerSpot = 1.2f, CaveDark = new Vector2(0f, 0.6f),
				},
				new AmbientCreatureKind
				{
					// Fungus gnats: the glow-worms' grown flies, a faint blue-green dance in the still damp air under a high roof,
					// each lit from within as the larvae on the roof are.
					Name = "Fungus gnats", Shape = AmbientShape.Swarm, Behaviour = AmbientBehaviour.Swarm, Family = AmbientFamily.Swarms, SwarmStyle = AmbientSwarmStyle.Cloud,
					Length = new Vector2(0.004f, 0.007f), GroupSize = new Vector2Int(14, 40), GroupsPerKm2 = 0f, Spread = 1.4f, Roam = new Vector2(0.5f, 1.5f),
					Speed = 0.15f, Altitude = new Vector2(0f, 0f), Episode = new Vector2(30f, 80f),
					Habitats = AmbientHabitat.Cave, Climate = new Vector2(Celsius(2f), 1f),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(1f), RainLimit = 0f,
					FlushMetres = 0f,
					Back = Hex(0x304048), Belly = Hex(0x405058), Accent = Hex(0x60e0c8), Smoothness = 0.3f, CastShadows = false, Glow = 3f,
					CaveNiche = AmbientCaveNiche.Glow, CaveForms = AmbientCaveForms.Cave | AmbientCaveForms.Grotto, CavePerSpot = 1f, CaveDark = new Vector2(0.2f, 1f),
				},
				new AmbientCreatureKind
				{
					// Bent-toed cave geckos: pale and banded, flat to the walls of warm caves, hunting the crickets.
					Name = "Cave geckos", Shape = AmbientShape.Lizard, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.1f, 0.16f), GroupSize = new Vector2Int(1, 2), GroupsPerKm2 = 0f, Spread = 0.8f, Roam = new Vector2(0.4f, 1.2f),
					Speed = 0.6f, StrokeHz = 7f, Episode = new Vector2(6f, 16f),
					Habitats = AmbientHabitat.Cave, Climate = new Vector2(Celsius(17f), 1f),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(15f), RainLimit = 0f,
					FlushMetres = 2f, FleeSpeed = 2f,
					Back = Hex(0xb0a080), Belly = Hex(0xd8d0b8), Accent = Hex(0x6a5a40), Smoothness = 0.4f, CastShadows = false,
					Palette = new[] { Hex(0xffffff), Hex(0xd8d0c0), Hex(0xc8b8a0) },
					CaveNiche = AmbientCaveNiche.Wall | AmbientCaveNiche.Floor, CaveForms = AmbientCaveForms.Rock, CavePerSpot = 0.7f, CaveDark = new Vector2(0f, 1f),
				},

				// ── The caves' light-makers (2026-10-20: "Bioluminescent creatures should also exist in caves"; "more glow is good
				// inside the caves"): each lit by the emission (GlowParts, GlowPattern: AmbientGlow) or, a swarm, by its own light.
				// Realism first — the luminous millipedes, glowing snails, click beetles and ice worms are real animals, their
				// colours and rhythms the real ones' — and the rest fantasy kin of real things (blind cave fish and freshwater jellies
				// given light, a newt with a fire salamander's livery lit, moths and mites of a world with more magic in its dark).
				// Kept to the walking level where they can be seen (floors, walls, the low glow air: PointOfInterestCaveDressing.Life). ──
				new AmbientCreatureKind
				{
					// Lantern beetles: click beetles with two headlamps on the shield (Pyrophorus), a steady yellow-green that
					// breathes slowly, creeping over the damp rock and the fungus where the glow gathers.
					Name = "Lantern beetles", Shape = AmbientShape.Beetle, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.025f, 0.04f), GroupSize = new Vector2Int(1, 3), GroupsPerKm2 = 0f, Spread = 0.5f, Roam = new Vector2(0.2f, 0.6f),
					Speed = 0.06f, StrokeHz = 4f, Episode = new Vector2(12f, 30f),
					Habitats = AmbientHabitat.Cave, Climate = new Vector2(Celsius(6f), 1f),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(4f), RainLimit = 0f,
					FlushMetres = 0.8f, FleeSpeed = 0.4f,
					Back = Hex(0x3a2a1c), Belly = Hex(0x4a3a2a), Accent = Hex(0x2a1e14), Smoothness = 0.55f, CastShadows = false,
					GlowParts = AmbientGlowParts.Lanterns, GlowPattern = AmbientGlowPattern.Pulse, GlowColour = Hex(0xc0ff50), GlowStrength = 5f, GlowPeriod = 6f, GlowRest = 0.55f,
					CaveNiche = AmbientCaveNiche.Floor | AmbientCaveNiche.Wall, CaveForms = AmbientCaveForms.Limestone | AmbientCaveForms.Mine, CavePerSpot = 0.9f,
					CaveDark = new Vector2(0.15f, 1f),
				},
				new AmbientCreatureKind
				{
					// Luminous millipedes (Motyxia): the whole armour lit a cold cyan-green (about 495 nm), here a light that runs
					// down the body in waves as its legs do; on the walls and floors of the deep cave.
					Name = "Glow millipedes", Shape = AmbientShape.Millipede, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.035f, 0.06f), GroupSize = new Vector2Int(1, 4), GroupsPerKm2 = 0f, Spread = 0.5f, Roam = new Vector2(0.2f, 0.7f),
					Speed = 0.025f, StrokeHz = 3f, Episode = new Vector2(15f, 40f),
					Habitats = AmbientHabitat.Cave, Climate = new Vector2(Celsius(3f), Celsius(26f)),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(2f), RainLimit = 0f,
					FlushMetres = 0.5f, FleeSpeed = 0.15f,
					Back = Hex(0x3e3a34), Belly = Hex(0x5a5248), Accent = Hex(0x26221e), Smoothness = 0.6f, CastShadows = false,
					GlowParts = AmbientGlowParts.Body, GlowPattern = AmbientGlowPattern.Wave, GlowColour = Hex(0x50f0d8), GlowStrength = 3.5f, GlowPeriod = 3.5f, GlowRest = 0.35f,
					CaveNiche = AmbientCaveNiche.Floor | AmbientCaveNiche.Wall, CaveForms = AmbientCaveForms.Limestone | AmbientCaveForms.Mine, CavePerSpot = 1f,
					CaveDark = new Vector2(0.2f, 1f),
				},
				new AmbientCreatureKind
				{
					// Glowing land snails (Quantula): an amber shell on a pale foot whose front flashes yellow-green in short trains,
					// on the damp rock of a limestone cave; withdrawn into the shell when disturbed.
					Name = "Glowing snails", Shape = AmbientShape.Snail, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.03f, 0.05f), GroupSize = new Vector2Int(1, 3), GroupsPerKm2 = 0f, Spread = 0.4f, Roam = new Vector2(0.1f, 0.3f),
					Speed = 0.005f, StrokeHz = 0.6f, Episode = new Vector2(40f, 120f),
					Habitats = AmbientHabitat.Cave, Climate = new Vector2(Celsius(6f), 1f),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(4f), RainLimit = 0f,
					FlushMetres = 0.4f, FleeSpeed = 0.02f,
					Back = Hex(0x9a7040), Belly = Hex(0xb8a890), Accent = Hex(0x5a3a1e), Smoothness = 0.8f, CastShadows = false,
					GlowParts = AmbientGlowParts.Foot, GlowPattern = AmbientGlowPattern.FlashTrain, GlowColour = Hex(0xa8ff90), GlowStrength = 6f, GlowPeriod = 6f, GlowRest = 0.1f,
					CaveNiche = AmbientCaveNiche.Floor | AmbientCaveNiche.Wall, CaveForms = AmbientCaveForms.Limestone, CavePerSpot = 0.9f, CaveDark = new Vector2(0.1f, 1f),
					CaveNeedsDamp = true,
				},
				new AmbientCreatureKind
				{
					// Spore motes: luminous spores and springtails hanging in a cave's still damp air (and foxfire's in a mine's,
					// off its rotting props), a pale green haze each mote of which kindles and fades on its own slow breath.
					Name = "Spore motes", Shape = AmbientShape.Swarm, Behaviour = AmbientBehaviour.Swarm, Family = AmbientFamily.Swarms, SwarmStyle = AmbientSwarmStyle.Spores,
					Length = new Vector2(0.002f, 0.004f), GroupSize = new Vector2Int(30, 90), GroupsPerKm2 = 0f, Spread = 2.5f, Roam = new Vector2(0.5f, 1.5f),
					Speed = 0.05f, Altitude = new Vector2(0f, 0f), Episode = new Vector2(30f, 80f),
					Habitats = AmbientHabitat.Cave, Climate = new Vector2(Celsius(1f), 1f),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(0f), RainLimit = 0f, Flies = false,
					FlushMetres = 0f,
					Back = Hex(0x3a4a40), Belly = Hex(0x4a5a50), Accent = Hex(0x90f0b8), Smoothness = 0.2f, CastShadows = false, Glow = 2.5f,
					CaveNiche = AmbientCaveNiche.Glow, CaveForms = AmbientCaveForms.Cave | AmbientCaveForms.Grotto | AmbientCaveForms.Mine, CavePerSpot = 1.2f,
					CaveDark = new Vector2(0.15f, 1f),
				},
				new AmbientCreatureKind
				{
					// Lanternfish of the caves: blind, pale, a finger long, schooling slowly in the still fresh water; a line of blue
					// light runs down each flank (the lateral line that feels the water, lit) and three lamps under the blind eye.
					Name = "Cave lanternfish", Shape = AmbientShape.Fish, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.05f, 0.09f), GroupSize = new Vector2Int(2, 6), GroupsPerKm2 = 0f, Spread = 0.8f, Roam = new Vector2(0.5f, 1.5f),
					Speed = 0.12f, StrokeHz = 3f, Episode = new Vector2(20f, 50f),
					Habitats = AmbientHabitat.Cave, Climate = new Vector2(Celsius(4f), Celsius(24f)),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(2f), RainLimit = 0f,
					FlushMetres = 0f,
					Back = Hex(0xd8ccc8), Belly = Hex(0xf0e8e4), Accent = Hex(0xb89898), Smoothness = 0.85f, CastShadows = false,
					GlowParts = AmbientGlowParts.LateralLines, GlowPattern = AmbientGlowPattern.Wave, GlowColour = Hex(0x60c0ff), GlowStrength = 3.5f, GlowPeriod = 4f, GlowRest = 0.45f,
					CaveNiche = AmbientCaveNiche.Pool, CaveForms = AmbientCaveForms.Cave | AmbientCaveForms.Grotto, CavePerSpot = 1.2f, CaveDark = new Vector2(0f, 1f),
					CaveNeedsDamp = true,
				},
				new AmbientCreatureKind
				{
					// Lantern jellies: thumbnail jellies (the freshwater jelly's kin, and the sea's in a sea cave's pools) whose
					// rim, canals and tentacles glow blue-violet, the light running out from the crown at each beat of the bell.
					Name = "Lantern jellies", Shape = AmbientShape.Jelly, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.03f, 0.06f), GroupSize = new Vector2Int(1, 4), GroupsPerKm2 = 0f, Spread = 1f, Roam = new Vector2(0.5f, 1.5f),
					Speed = 0.03f, StrokeHz = 0.8f, Episode = new Vector2(30f, 80f),
					Habitats = AmbientHabitat.Cave, Climate = new Vector2(Celsius(3f), 1f),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(1f), RainLimit = 0f,
					FlushMetres = 0f,
					Back = Hex(0xc0d0e8), Belly = Hex(0xe0e8f4), Accent = Hex(0x8098c8), Smoothness = 0.9f, CastShadows = false,
					GlowParts = AmbientGlowParts.Bell, GlowPattern = AmbientGlowPattern.Ripple, GlowColour = Hex(0x8098ff), GlowStrength = 4f, GlowRest = 0.35f,
					CaveNiche = AmbientCaveNiche.Pool, CaveForms = AmbientCaveForms.Limestone, CavePerSpot = 1f, CaveDark = new Vector2(0f, 1f),
				},
				new AmbientCreatureKind
				{
					// Wisp moths: pale moths of the deep dark whose wing edges glow a cold blue-white, wheeling slow in the glow air
					// and the halls' open air (the "will-o'-the-wisps" seen deep in caves).
					Name = "Wisp moths", Shape = AmbientShape.Butterfly, Behaviour = AmbientBehaviour.Hawk, Family = AmbientFamily.Bats,
					Length = new Vector2(0.04f, 0.06f), GroupSize = new Vector2Int(1, 3), GroupsPerKm2 = 0f, Spread = 2f, Roam = new Vector2(2f, 4f),
					Speed = 0.6f, StrokeHz = 7f, Altitude = new Vector2(0f, 0f),
					Habitats = AmbientHabitat.Cave, Climate = new Vector2(Celsius(2f), 1f),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(1f), RainLimit = 0f,
					FlushMetres = 0f,
					Back = Hex(0xd0d4e0), Belly = Hex(0xe4e8f0), Accent = Hex(0x8090b8), Smoothness = 0.1f, CastShadows = false,
					GlowParts = AmbientGlowParts.WingEdges, GlowPattern = AmbientGlowPattern.Pulse, GlowColour = Hex(0xa8ccff), GlowStrength = 3f, GlowPeriod = 5f, GlowRest = 0.4f,
					CaveNiche = AmbientCaveNiche.Flight | AmbientCaveNiche.Glow, CaveForms = AmbientCaveForms.Cave | AmbientCaveForms.Grotto | AmbientCaveForms.Mine,
					CavePerSpot = 1f, CaveDark = new Vector2(0.3f, 1f),
				},
				new AmbientCreatureKind
				{
					// Lantern newts: slate-dark newts with a fire salamander's double row of spots down the back, each a warm gold
					// lamp twinkling on its own, on the wet rock of the deep cave.
					Name = "Lantern newts", Shape = AmbientShape.Lizard, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.08f, 0.12f), GroupSize = new Vector2Int(1, 2), GroupsPerKm2 = 0f, Spread = 0.6f, Roam = new Vector2(0.3f, 0.8f),
					Speed = 0.1f, StrokeHz = 2.5f, Episode = new Vector2(12f, 30f), RainLover = true,
					Habitats = AmbientHabitat.Cave, Climate = new Vector2(Celsius(3f), Celsius(20f)),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(2f), RainLimit = 0f,
					FlushMetres = 1f, FleeSpeed = 0.5f,
					Back = Hex(0x2a2a32), Belly = Hex(0x4a3a30), Accent = Hex(0xe0a040), Smoothness = 0.75f, CastShadows = false,
					GlowParts = AmbientGlowParts.Spots, GlowPattern = AmbientGlowPattern.Twinkle, GlowColour = Hex(0xffc060), GlowStrength = 3f, GlowPeriod = 4f, GlowRest = 0.35f,
					CaveNiche = AmbientCaveNiche.Floor | AmbientCaveNiche.Wall, CaveForms = AmbientCaveForms.Cave | AmbientCaveForms.Grotto,
					CavePerSpot = 0.6f, CaveDark = new Vector2(0.25f, 1f), CaveNeedsDamp = true,
				},
				new AmbientCreatureKind
				{
					// Ember mites: fire-touched mites of the lava tubes, crowds of red-orange specks flickering like the last
					// coals over the bare basalt (a lava tube's only light-makers; the fantasy's, with the mites of the real tubes).
					Name = "Ember mites", Shape = AmbientShape.Spider, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.012f, 0.02f), GroupSize = new Vector2Int(5, 14), GroupsPerKm2 = 0f, Spread = 0.5f, Roam = new Vector2(0.2f, 0.6f),
					Speed = 0.08f, StrokeHz = 8f, Episode = new Vector2(6f, 18f),
					Habitats = AmbientHabitat.Cave, Climate = new Vector2(-1f, 1f),
					Activity = AmbientActivity.AnyTime, ColdLimit = NeverTooCold, RainLimit = 0f,
					FlushMetres = 0.6f, FleeSpeed = 0.5f,
					Back = Hex(0x3a1a10), Belly = Hex(0x5a2a14), Accent = Hex(0x8a3a10), Smoothness = 0.4f, CastShadows = false,
					GlowParts = AmbientGlowParts.Body, GlowPattern = AmbientGlowPattern.Flicker, GlowColour = Hex(0xff7020), GlowStrength = 4f, GlowPeriod = 1.6f, GlowRest = 0.4f,
					CaveNiche = AmbientCaveNiche.Floor | AmbientCaveNiche.Wall, CaveForms = AmbientCaveForms.LavaTube, CavePerSpot = 2.2f, CaveDark = new Vector2(0f, 1f),
				},
				new AmbientCreatureKind
				{
					// Ice worms (Mesenchytraeus): finger-joint-long dark worms that live in glacier ice at its melting point and die
					// above about 5 °C, here glowing ice-blue as they writhe over an ice cave's walls and floors. A cold-trap ice
					// cave keeps its ice under a ground year mean of several degrees, hence the band's top.
					Name = "Ice worms", Shape = AmbientShape.Worm, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.015f, 0.03f), GroupSize = new Vector2Int(3, 10), GroupsPerKm2 = 0f, Spread = 0.5f, Roam = new Vector2(0.1f, 0.4f),
					Speed = 0.01f, StrokeHz = 1.2f, Episode = new Vector2(20f, 60f),
					Habitats = AmbientHabitat.Cave, Climate = new Vector2(-1f, Celsius(9f)),
					Activity = AmbientActivity.AnyTime, ColdLimit = Celsius(-12f), RainLimit = 0f,
					FlushMetres = 0f,
					Back = Hex(0x24283a), Belly = Hex(0x343a4c), Accent = Hex(0x181a26), Smoothness = 0.8f, CastShadows = false,
					GlowParts = AmbientGlowParts.Body, GlowPattern = AmbientGlowPattern.Pulse, GlowColour = Hex(0x60b8ff), GlowStrength = 3f, GlowPeriod = 6f, GlowRest = 0.4f,
					CaveNiche = AmbientCaveNiche.Floor | AmbientCaveNiche.Wall, CaveForms = AmbientCaveForms.IceCave, CavePerSpot = 4f, CaveDark = new Vector2(0f, 1f),
				},
			};
		}

		/// <summary>An sRGB colour from 0xRRGGBB.</summary>
		public static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
	}
}
