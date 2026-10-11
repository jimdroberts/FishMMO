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
	}

	/// <summary>Which of the settings' families a kind belongs to: each has its own draw distance and density.</summary>
	public enum AmbientFamily
	{
		Flocks,
		SoloBirds,
		Raptors,
		Bats,
		Critters,
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

	/// <summary>The kinds of country a biome offers wildlife (<see cref="AmbientGating.HabitatOf(string)"/>).</summary>
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
	}

	/// <summary>The kinds the world starts with.</summary>
	public static class AmbientLifeCatalogue
	{
		private const AmbientHabitat OpenCountry = AmbientHabitat.Grassland | AmbientHabitat.Farmland | AmbientHabitat.Scrub;
		private const AmbientHabitat Wooded = AmbientHabitat.Forest | AmbientHabitat.Woodland | AmbientHabitat.Jungle;

		/// <summary>Temperatures in the scale's units (-1..1, 0 = 0 °C, 1 = 33.1 °C) from degrees Celsius.</summary>
		public static float Celsius(float degrees) => degrees / 33.1f;

		/// <summary>
		/// Sixteen kinds. Numbers are grounded in the animals': flight initiation distances from field studies
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
					Habitats = OpenCountry | AmbientHabitat.Woodland | AmbientHabitat.Tundra | AmbientHabitat.Settlement | AmbientHabitat.Mountain,
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
					Habitats = Wooded | OpenCountry | AmbientHabitat.Settlement | AmbientHabitat.Coast | AmbientHabitat.Tundra | AmbientHabitat.Mountain | AmbientHabitat.Wetland,
					Prefers = AmbientHabitat.Farmland | AmbientHabitat.Settlement, Climate = new Vector2(-0.7f, 1f),
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

				// ── Bats ──
				new AmbientCreatureKind
				{
					Name = "Bats", Shape = AmbientShape.Bat, Behaviour = AmbientBehaviour.Hawk, Family = AmbientFamily.Bats,
					Length = new Vector2(0.08f, 0.11f), GroupSize = new Vector2Int(3, 9), GroupsPerKm2 = 100f, Spread = 10f, Roam = new Vector2(8f, 16f),
					Speed = 6f, StrokeHz = 10f, Altitude = new Vector2(2f, 9f),
					Habitats = Wooded | AmbientHabitat.Wetland | AmbientHabitat.FreshWater | AmbientHabitat.Farmland | AmbientHabitat.Settlement
						| AmbientHabitat.Cave | AmbientHabitat.Mountain | AmbientHabitat.Grassland | AmbientHabitat.Scrub | AmbientHabitat.Desert,
					Prefers = AmbientHabitat.Wetland | AmbientHabitat.FreshWater | AmbientHabitat.Cave | AmbientHabitat.Woodland, PreferWeight = 2.5f,
					Climate = new Vector2(Celsius(0f), 1f),
					// Insect bats stay in the roost below about 5 °C (no insects fly) and in rain (it clogs their sonar and soaks them).
					Activity = AmbientActivity.Dusk, ColdLimit = Celsius(5f), RainLimit = 0.15f, Hibernates = true,
					FlushMetres = 0f,
					Back = Hex(0x2a221e), Belly = Hex(0x3a302a), Accent = Hex(0x1a1614), Smoothness = 0.2f, CastShadows = false,
				},

				// ── Ground critters ──
				new AmbientCreatureKind
				{
					Name = "Rat", Shape = AmbientShape.Rat, Behaviour = AmbientBehaviour.Scurry, Family = AmbientFamily.Critters, Flies = false,
					Length = new Vector2(0.2f, 0.26f), GroupSize = new Vector2Int(1, 3), GroupsPerKm2 = 600f, Spread = 3f, Roam = new Vector2(2f, 5f),
					Speed = 1.6f, StrokeHz = 6f, Episode = new Vector2(5f, 14f),
					Habitats = AmbientHabitat.Farmland | AmbientHabitat.Settlement | AmbientHabitat.Wetland | AmbientHabitat.Coast | AmbientHabitat.Grassland
						| AmbientHabitat.Woodland | AmbientHabitat.Scrub | AmbientHabitat.Cave,
					Prefers = AmbientHabitat.Settlement | AmbientHabitat.Farmland | AmbientHabitat.Wetland, PreferWeight = 3f, Climate = new Vector2(Celsius(-8f), 1f),
					Activity = AmbientActivity.Nocturnal, ColdLimit = Celsius(-10f), RainLimit = 0.6f,
					FlushMetres = 4f, FleeSpeed = 3f,
					Back = Hex(0x4a4038), Belly = Hex(0x7a7064), Accent = Hex(0xb08a80), Smoothness = 0.25f,
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
						| AmbientHabitat.Grassland | AmbientHabitat.Jungle,
					Prefers = AmbientHabitat.Desert | AmbientHabitat.Scrub, PreferWeight = 3f, Climate = new Vector2(Celsius(14f), 1f),
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
			};
		}

		/// <summary>An sRGB colour from 0xRRGGBB.</summary>
		public static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
	}
}
