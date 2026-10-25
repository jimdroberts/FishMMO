#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Dresses a carved cave system (2026-10-11: "much more realistic features"): dripstone on its floors and roofs
	/// where water has stood and dripped — stalagmites, stalactites hung over them, columns where the two have met,
	/// flowstone down the walls and gour pools on cavern floors — breakdown blocks fallen from the roof, glowing crystals
	/// in some chambers, and in a mine its timbering, track, an ore tub and lamps. Then (2026-10-13: "enormous caves with
	/// lots of glowy rocks lining the walkable pathways") what the layout's features need and what lights the way: the
	/// climbs' ladders and ropes and their scene climbables, the crossings' rope walks and trestles
	/// (PointOfInterestCaveDressing.Ways.cs), glowing crystal, fungus and glow-worms lining the ways across halls, bridges,
	/// ramps and ledges (.Glow.cs), explorers' camps and lanterns (.Camps.cs), and a mine's ore, lamps, signs and miners'
	/// camp (.Workings.cs).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>By the cave's stone.</b> A limestone cave (cave, grotto, sea cave) takes calcite dripstone and selenite; an ice
	/// cave ice stalagmites, icicles and frost crystals; a lava tube basalt dribble spires and slabs of its fallen lining
	/// (lava stalactites hang from its roof); a mine none of these, only the timbering and the spoil of the ore.
	/// </para>
	/// <para>
	/// <b>The shell's rock (2026-10-16).</b> The formations are the rock the cave is cut in (<see cref="CaveShaper.ShellOf"/>,
	/// its biome template's Cave Shells: the ground's rock for a cave, grotto, sea cave or mine, basalt for a lava tube, ice
	/// for an ice cave), so a cave in sandstone country has sandstone blocks fallen from its sandstone roof. Before, the
	/// form alone chose them (limestone, basalt, quartzite, water ice) whatever the walls were. Where the shell's rock has no
	/// art for a shape (dripstone exists only as limestone's sinter-dressed calcite, basalt's dribble and water ice's), the
	/// fallen rock and spoil take that rock's nearest broken shape (<see cref="FormationArt"/>), and anything else stands in
	/// the form's old rock (<see cref="StandInRock"/>, reported with the dressing): dripstone is calcite precipitated from
	/// the water, not the wall rock, so calcite dripstone in a sandstone cave is what such a cave holds.
	/// </para>
	/// <para>
	/// <b>Never in the way.</b> Every passage's line is a lane kept clear of anything on the floor and of anything hanging
	/// lower than 2.4 m over it, and the middle of each site's own chamber (where its camp or den goes) is kept clear too;
	/// formations stand in the chambers' outer rings and along the passages' walls. Stalactites hang where the roof is,
	/// found by marching the system's field; nothing is placed where the field says rock.
	/// </para>
	/// <para>
	/// <b>Some are overgrown</b> (<see cref="PointOfInterestCaveMouths.Overgrown"/>): the growth round the mouth (the
	/// biome's ferns, bushes and creeper) carries in over the floor of each way in, thinning as the daylight fails, in
	/// patches; bushes only near the mouth and off the lane, ferns and creeper down to a trodden line along the middle.
	/// The terrain's own plants cannot follow: the cave's floor is its mesh, and the terrain is holed under it.
	/// </para>
	/// <para>
	/// The system's host site dresses all of it (a joined site's chambers included); a joined site dresses nothing.
	/// Props, so instanced, culled and collided with like the rest; formations from the biome art's formation prefabs, the
	/// mine's from the structure kit. Seeded by the system, so a repaint dresses the same.
	/// </para>
	/// </remarks>
	[InitializeOnLoad]
	public sealed partial class PointOfInterestCaveDressing : IPointOfInterestSiteBuilder
	{
		static PointOfInterestCaveDressing() => PointOfInterestSiteBuilders.Register(new PointOfInterestCaveDressing());

		/// <summary>A lane's half-width either side of a passage's line, metres: a person and a little room.</summary>
		private const float Lane = 1.4f;
		/// <summary>The least clearance over a lane under anything hung from the roof, metres.</summary>
		private const float Headroom = 2.4f;

		public bool Handles(PointOfInterestRecord record) => CaveShaper.TryForm(record.Kind, out _);

		private sealed class Set
		{
			/// <summary>The rock the formations are made of: the cave's shell's (<see cref="FormationRock"/>).</summary>
			public string Rock;
			public string Floor, Hanging, Wall, Pool, Breakdown, Crystal;
			/// <summary>The crystals' own stone, when not the cave's (selenite in a limestone cave).</summary>
			public string CrystalRock;
			/// <summary>The formations' heights at scale 1 (the shapes' own), metres.</summary>
			public float FloorHeight = 2.6f, WallHeight = 1.6f;
			public float Density = 1f, CrystalChance, BreakdownShare = 0.25f;
			/// <summary>
			/// The crystals the chambers grow when they are a deposit, not the cave's own stone (<see cref="CrystalRock"/> set): the
			/// cave's biome's (its template's Cave Crystals, 2026-10-16), a kind drawn by weight for each cluster.
			/// </summary>
			public IReadOnlyList<BiomeCaveCrystal> Crystals;
			/// <summary>The crystals' light's hue (SceneEmitterKind.Crystal), or −1 for one by chance.</summary>
			public int Hue = -1;
		}

		/// <summary>
		/// The formations a cave of a form takes (their rock is the cave's shell's, set by <see cref="Build"/>; until 2026-10-16
		/// it was the form's alone, as <see cref="StandInRock"/> still states).
		/// </summary>
		private static Set SetFor(CaveForm form)
		{
			switch (form)
			{
				case CaveForm.IceCave:
					return new Set { Floor = "Stalagmite", Hanging = "Stalagmite", Wall = "FrostFlowers", Crystal = "Crystal", Breakdown = "Lobe",
						FloorHeight = 2.2f, WallHeight = 0.6f, CrystalChance = 0.6f, Hue = 1 };
				case CaveForm.LavaTube:
					return new Set { Floor = "Dribble", Hanging = "Dribble", Breakdown = "Slabs", FloorHeight = 1.6f, Density = 0.6f, BreakdownShare = 0.5f };
				case CaveForm.SeaCave:
					return new Set { Floor = "Stalagmite", Hanging = "Stalagmite", Breakdown = "Block", Density = 0.35f, BreakdownShare = 0.6f };
				case CaveForm.Mine:
					return new Set { Breakdown = "Scree", Density = 0.3f, BreakdownShare = 1f };
				default:
					return new Set { Floor = "Stalagmite", Hanging = "Stalagmite", Wall = "Flowstone", Pool = "Gours", Breakdown = "Block",
						Crystal = "Crystal", CrystalRock = "Gypsum", CrystalChance = 0.3f };
			}
		}

		/// <summary>
		/// The cave biome's own formations (its Cave Formations, 2026-10-25, I2n) over a form's set: where the list names the form, each
		/// place takes its shape by weight among those whose world gate admits the world (a place it leaves out, or whose every shape
		/// is gated off, unformed), one shape a place a system, drawn on a stream of its own only when a place has several; a list not
		/// naming the form leaves the set as it was. The defaults (one shape a place, as the set's) change nothing.
		/// </summary>
		private static void ApplyFormations(Set set, IReadOnlyList<BiomeCaveFormation> list, CaveForm form, in BiomeWorldConditions world, int seed)
		{
			var f = (BiomeCaveForm)form;
			if (!BiomeCaveInteriors.NamesForm(list, f))
			{
				return;
			}
			set.Floor = ShapeOf(list, f, BiomeCaveFormationRole.Floor, in world, seed);
			set.Hanging = ShapeOf(list, f, BiomeCaveFormationRole.Hanging, in world, seed);
			set.Wall = ShapeOf(list, f, BiomeCaveFormationRole.Wall, in world, seed);
			set.Pool = ShapeOf(list, f, BiomeCaveFormationRole.Pool, in world, seed);
			set.Breakdown = ShapeOf(list, f, BiomeCaveFormationRole.Breakdown, in world, seed);
			set.Crystal = ShapeOf(list, f, BiomeCaveFormationRole.Crystal, in world, seed);
		}

		/// <summary>A place's shape for a system: the one there is, or one by weight from the system's seed and the place; null for none.</summary>
		private static string ShapeOf(IReadOnlyList<BiomeCaveFormation> list, BiomeCaveForm form, BiomeCaveFormationRole role, in BiomeWorldConditions world, int seed)
		{
			List<(string Shape, float Weight)> shapes = BiomeCaveInteriors.Shapes(list, form, role, in world);
			if (shapes.Count <= 1)
			{
				return shapes.Count == 1 ? shapes[0].Shape : null;
			}
			float roll = new DeterministicRNG(unchecked(seed * 0x6E31 + 0x1D7 * ((int)role + 1))).NextFloat();
			return BiomeCaveInteriors.Pick(shapes, roll);
		}

		// ── The formations' rock ──────────────────────────────────────

		/// <summary>
		/// The rock a cave's formations are made of, from the rock its shell is cut in (2026-10-16: the formations follow the
		/// shell): the shell's own, glacier ice's formations being water ice's (the shell's Ice is the cliff rock's name for it).
		/// </summary>
		public static string FormationRock(string shell) => shell == CliffRocks.Ice ? RockTypes.WaterIce.Name : shell;

		/// <summary>
		/// The rock a form's formations were made of before they followed the shell (by form alone, whatever the walls): the
		/// art every shape of the form's set exists in, so it stands in where the shell's rock has none for a shape.
		/// </summary>
		public static string StandInRock(CaveForm form)
		{
			switch (form)
			{
				case CaveForm.IceCave:
					return RockTypes.WaterIce.Name;
				case CaveForm.LavaTube:
					return RockTypes.Basalt.Name;
				case CaveForm.Mine:
					return RockTypes.Quartzite.Name;
				default:
					return RockTypes.Limestone.Name;
			}
		}

		/// <summary>The broken rock a roof sheds, nearest first: what fallen blocks are in a rock without blocks.</summary>
		private static readonly string[] FallenShapes = { "Block", "Slabs", "Split", "Boulder", "Chunk", "Lump", "Stack", "Flags", "Platy", "Talus", "Scree", "Tafoni" };

		/// <summary>The broken rock a working leaves, nearest first: what spoil is in a rock without scree.</summary>
		private static readonly string[] SpoilShapes = { "Scree", "Talus", "Chunk", "Platy", "Flags", "Stack", "Slabs", "Block", "Split", "Lump", "Boulder", "Tafoni" };

		/// <summary>
		/// The (rock, shape) a formation of a cave whose formations are <paramref name="rock"/> is drawn with: that rock's own
		/// shape where its art has it; for fallen rock (Block, Slabs) and spoil (Scree), the rock's nearest broken shape; else
		/// the shape in the form's old rock (<see cref="StandInRock"/>). Pure: reads the rock types' shapes, not the assets.
		/// </summary>
		public static (string Rock, string Shape) FormationArt(string rock, string shape, CaveForm form)
		{
			if (!string.IsNullOrEmpty(rock) && RockTypes.TryGet(rock, out RockType type))
			{
				if (RockTypes.TryShape(in type, shape, out _))
				{
					return (rock, shape);
				}
				string[] alternatives = shape == "Scree" ? SpoilShapes : shape == "Block" || shape == "Slabs" ? FallenShapes : null;
				if (alternatives != null)
				{
					foreach (string alternative in alternatives)
					{
						if (RockTypes.TryShape(in type, alternative, out _))
						{
							return (rock, alternative);
						}
					}
				}
			}
			return (StandInRock(form), shape);
		}

		/// <summary>The formation shapes a form's caves place in their own rock (every one but a crystal of its own stone).</summary>
		public static List<string> FormationShapes(CaveForm form)
		{
			Set set = SetFor(form);
			var shapes = new List<string>();
			foreach (string shape in new[] { set.Floor, set.Hanging, set.Wall, set.Pool, set.Breakdown, set.CrystalRock == null ? set.Crystal : null })
			{
				if (shape != null && !shapes.Contains(shape))
				{
					shapes.Add(shape);
				}
			}
			return shapes;
		}

		public void Build(PointOfInterestSiteContext context)
		{
			PointOfInterestShape shape = CaveShaper.ShapeFor(context.Points, context.Record.Id);
			if (shape == null || CaveShaper.IsLink(shape.Values, out _))
			{
				return;
			}
			CaveSystem system = CaveSystem.FromValues(shape.Values, shape.Position, shape.Seed);
			if (system == null)
			{
				return;
			}
			var clock = System.Diagnostics.Stopwatch.StartNew();
			int emitters = context.Points != null && context.Points.Emitters != null ? context.Points.Emitters.Count : 0;
			Set set = SetFor(system.Form);
			// The formations are the shell's rock: the very rock the shaper built the cave's walls in (its host site's biome's).
			set.Rock = FormationRock(CaveShaper.ShellOf(context.Request, context.Record, system));
			// The cave's biomes (2026-10-25, phase I2n): its host site's, whose are its mouth, its shell and a mine, and the cave biome
			// the host links its form to where the shell is one that biome forms in, whose template dresses the inside (its
			// formations, crystals, glow, lights, animals). Its crystals are the chambers' deposits where the form grows them (a
			// cave's, a grotto's: selenite in every biome before 2026-10-16), the interior's with the host's glowing ones as
			// accents; an ice cave's crystals stay its own ice.
			CaveBiome caves = CaveInteriorArt.Of(context.Request, context.Record, system);
			BiomeWorldConditions world = CaveInteriorArt.World(context.Request);
			ApplyFormations(set, CaveInteriorArt.Formations(caves.Content), system.Form, in world, system.Seed);
			if (set.CrystalRock != null)
			{
				set.Crystals = CaveInteriorArt.Crystals(in caves);
			}
			var dresser = new Dresser(context, system, set, in caves, BiomeAir.IsBreathable(in world), new DeterministicRNG(unchecked(system.Seed * 0x2F6B + 0xD1E5)));
			dresser.Run();
			// Lights counts every emitter the dressing added that lights: the glow's own and those its lamps, torches and fires give
			// off (not the drips' nor the lava's fumes, which only the client's effects read).
			int lights = 0;
			if (context.Points != null && context.Points.Emitters != null)
			{
				for (int i = emitters; i < context.Points.Emitters.Count; i++)
				{
					SceneEmitterKind kind = context.Points.Emitters[i].Kind;
					lights += kind == SceneEmitterKind.Drip || kind == SceneEmitterKind.Fumes ? 0 : 1;
				}
			}
			// With the time it took, so the dressing's marches can be measured in a cut's notes.
			PointOfInterestFeatureKit.Note(context, $"cave dressed in {clock.Elapsed.TotalSeconds:0.00} s ({system.Character}"
				+ (caves.Linked ? $", inside the {caves.Interior.name}" : string.Empty) + $"): {dresser.Placed} props, {lights} lights"
				+ $", glow: {dresser.Glowing} ({dresser.Lights} glow lights), lamps: {dresser.Lamps}, climbs: {dresser.Climbed}, crossings: {dresser.Crossed}, camps: {dresser.Camps}"
				+ (dresser.Grown > 0 ? $", overgrown: {dresser.Grown} plants" : string.Empty)
				+ (dresser.Vined > 0 ? $", vines and roots: {dresser.Vined}" : string.Empty)
				+ (dresser.Clutter > 0 ? $", clutter: {dresser.Clutter}" : string.Empty)
				+ $", cave life: {dresser.LifeSpots} spots"
				+ (dresser.LavaLights + dresser.LavaPieces + dresser.LavaFumes > 0 ? $", lava: {dresser.LavaPieces} pieces, {dresser.LavaLights} lava lights, {dresser.LavaFumes} fumes" : string.Empty)
				+ (dresser.StoodIn.Count > 0 ? $"; {set.Rock} has no art for: {string.Join(", ", dresser.StoodIn)}" : string.Empty)
				+ (dresser.Unfit.Count > 0 ? $"; cave lights that cannot stand where the biome names them: {string.Join(", ", dresser.Unfit)}" : string.Empty)
				+ (dresser.Missing.Count > 0 ? $"; missing art: {string.Join(", ", dresser.Missing)} (Generate Biome Art, then repaint)" : string.Empty));
		}

		private sealed partial class Dresser
		{
			private readonly PointOfInterestSiteContext context;
			private readonly CaveSystem system;
			private readonly Set set;
			private readonly DeterministicRNG rng;
			/// <summary>
			/// The biome dressing the cave's inside (2026-10-25: its interior where its host links one, else its host's), or null: what
			/// lights it (its template's Cave Lights), its crystals and glow.
			/// </summary>
			private readonly BiomeTemplate biome;
			/// <summary>The biome the cave opens in (its host site's): its mouth's growth, the land over it (roots, the trees above).</summary>
			private readonly BiomeTemplate host;
			/// <summary>Both (<see cref="CaveBiome"/>).</summary>
			private readonly CaveBiome caves;
			/// <summary>Whether the world's air burns a flame: a linked interior's lights are the cold Glow kinds where it does not.</summary>
			private readonly bool breathable;
			/// <summary>Draws which of the biome's crystals each chamber cluster is, on its own stream, so the mix never moves the rest.</summary>
			private readonly DeterministicRNG crystalPick;
			private readonly List<(Vector3 a, Vector3 b)> lanes = new List<(Vector3, Vector3)>();
			private readonly List<(Vector3 centre, float radius)> keepClear = new List<(Vector3, float)>();
			public readonly HashSet<string> Missing = new HashSet<string>();
			/// <summary>The formations the shell's rock has no art for, each as "shape (stand-in rock's)" or "shape (as alternative)".</summary>
			public readonly SortedSet<string> StoodIn = new SortedSet<string>(System.StringComparer.Ordinal);
			/// <summary>Cave lights the biome names for a place they cannot be set in (a wall light where one stands on the floor), each "Place: Light".</summary>
			public readonly SortedSet<string> Unfit = new SortedSet<string>(System.StringComparer.Ordinal);
			public int Placed, Lights, Grown;
			/// <summary>Glow art laid, lanterns laid, climbs and crossings drawn, camps pitched.</summary>
			public int Glowing, Lamps, Climbed, Crossed, Camps;
			/// <summary>Where every crystal-kind light of this system stands (system frame): what keeps the glow's lights sparse.</summary>
			private readonly List<Vector3> glowLights = new List<Vector3>();

			public Dresser(PointOfInterestSiteContext context, CaveSystem system, Set set, in CaveBiome caves, bool breathable, DeterministicRNG rng)
			{
				this.context = context;
				this.system = system;
				this.set = set;
				this.caves = caves;
				biome = caves.Content;
				host = caves.Host;
				this.breathable = breathable;
				this.rng = rng;
				crystalPick = new DeterministicRNG(unchecked(system.Seed * 0x3A7D + 0x51C3));
				foreach (CavePassage passage in system.Passages)
				{
					for (int i = 0; i + 1 < passage.Nodes.Count; i++)
					{
						CaveNode a = passage.Nodes[i], b = passage.Nodes[i + 1];
						lanes.Add((new Vector3(a.Centre.x, a.Floor, a.Centre.z), new Vector3(b.Centre.x, b.Floor, b.Centre.z)));
					}
				}
				// Each site's own chamber's middle is its camp's and a large cave's dungeon entrance's (PointOfInterestChamberFeature).
				foreach (CaveMouth mouth in system.Mouths)
				{
					if (system.TryChamberOf(mouth.SiteId, out CaveChamber own))
					{
						keepClear.Add((new Vector3(own.Centre.x, own.Floor, own.Centre.z), Mathf.Max(4.5f, 0.6f * Mathf.Min(own.Radii.x, own.Radii.z))));
						// A large cave's dungeon gate against its great cavern's wall, and its forecourt (DungeonEntranceShaper).
						PointOfInterestRecord site = context.Points != null ? context.Points.Find(mouth.SiteId) : null;
						if (site != null && DungeonEntranceShaper.HasCavernGate(site.Kind, site.SizeClass)
							&& DungeonEntranceShaper.TryCavernGateClearing(system, mouth.SiteId, out Vector3 gate, out float gateClear))
						{
							keepClear.Add((gate, gateClear));
						}
						if (system.Form == CaveForm.Grotto)
						{
							// A grotto's shrine or camp stands on the dry ledge at its back (CaveShaper.TryChamber).
							float back = 0.55f * own.Radii.x, yr = own.Yaw * Mathf.Deg2Rad;
							keepClear.Add((new Vector3(own.Centre.x + Mathf.Sin(yr) * back, own.Floor + own.Slope * back, own.Centre.z + Mathf.Cos(yr) * back), 3f));
						}
					}
				}
			}

			public void Run()
			{
				if (system.Form == CaveForm.Mine)
				{
					Mine();
				}
				foreach (CaveChamber chamber in system.Chambers)
				{
					Chamber(chamber);
				}
				foreach (CavePassage passage in system.Passages)
				{
					Passage(passage);
				}
				if (PointOfInterestCaveMouths.Overgrown(system))
				{
					Overgrow();
				}
				// The layout's features and the glow last, each on its own random stream: what the passes above laid stands
				// where it did before these were added.
				Climbs();
				Crossings();
				if (system.Form == CaveForm.Mine)
				{
					Workings();
					// 2026-10-20 (cave realism): the working's clutter (PointOfInterestCaveDressing.Clutter.cs).
					MineClutter();
				}
				else
				{
					GlowAll();
					Encampments();
					// 2026-10-20 (cave realism): mushroom trees, groves, rings, clumps and rubble (PointOfInterestCaveDressing.Fungi.cs).
					Fungi();
				}
				// Roots, daylight ivy and the deep dark's glowroots (PointOfInterestCaveDressing.Vines.cs).
				CaveVines();
				// Where its animals live: measured, not placed, on a stream of its own (PointOfInterestCaveDressing.Life.cs).
				Life();
				// 2026-10-20 (cave lava): a lava tube that still runs, dressed round its lava last of all (PointOfInterestCaveDressing.Lava.cs).
				Lava();
				// 2026-10-20 (caves livened): each its own pass on its own stream, after every earlier one, so none of theirs moves.
				// Its streams and pools' banks and spray (PointOfInterestCaveDressing.Streams.cs).
				Streams();
				// Daylight through the roof: the rim's plants and the lit floor below (PointOfInterestCaveDressing.Skylights.cs).
				Skylights();
				// Drips from the roof and the puddles they fall in (PointOfInterestCaveDressing.Drips.cs).
				Drips();
				// What others left: bones, carvings, cave-ins, mine carts, hidden caches (PointOfInterestCaveDressing.Story.cs).
				Story();
				// What hangs from the roof: glow-worm threads, roots, guano under the roosts (PointOfInterestCaveDressing.Hanging.cs).
				Hanging();
				// An ice cave's frozen falls, icicle curtains and frozen pools (PointOfInterestCaveDressing.Ice.cs).
				Ice();
			}

			// ── Overgrowth ────────────────────────────────────────

			/// <summary>How far in the daylight lets plants grow, in bore radii at the mouth, within bounds (metres).</summary>
			private const float LightRadii = 5f, LightMin = 8f, LightMax = 24f;
			/// <summary>The trodden line down a way in that nothing grows on: half its width, metres.</summary>
			private const float Trodden = 0.6f;

			/// <summary>
			/// Plants of the mouth's growth over the floor of each way in, as far as the light goes. Its own random stream,
			/// so an overgrown cave's formations stand where they would in a bare one.
			/// </summary>
			private void Overgrow()
			{
				PointOfInterestCaveMouths.Growth growth = PointOfInterestCaveMouths.GrowthFor(PointOfInterestCaveMouths.BiomeOf(context, system));
				if (growth.Species.Count == 0)
				{
					return;
				}
				var random = new DeterministicRNG(unchecked(system.Seed * 0x1F3D + 0x6A09));
				var noise = new Vector2(random.NextFloat() * 1000f, random.NextFloat() * 1000f);
				foreach (CavePassage passage in system.Passages)
				{
					if (passage.Kind != CavePassageKind.Trunk || passage.Nodes.Count < 2)
					{
						continue;
					}
					List<CaveNode> nodes = passage.Nodes;
					float start = nodes[0].Along;
					float light = Mathf.Clamp(LightRadii * nodes[0].Radius, LightMin, LightMax);
					float end = Mathf.Min(start + light, nodes[nodes.Count - 1].Along);
					for (float along = start + 0.4f; along < end; along += 0.6f)
					{
						CentreAt(nodes, along, out Vector3 centre, out float floor);
						CentreAt(nodes, along + 0.5f, out Vector3 ahead, out _);
						Vector3 dir = ahead - centre;
						dir.y = 0f;
						dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
						var across = new Vector3(dir.z, 0f, -dir.x);
						float radius = RadiusAt(nodes, along);
						// The daylight falls off into the cave: thick at the mouth, the last few small and far between.
						float lit = 1f - (along - start) / light;
						lit *= lit;
						for (int k = 0; k < 3; k++)
						{
							Vector3 p = centre + across * (random.Range(-0.95f, 0.95f) * radius) + dir * random.Range(-0.3f, 0.3f);
							p.y = floor + 0.5f;
							float patch = Mathf.Clamp01(Mathf.PerlinNoise(noise.x + p.x * 0.3f, noise.y + p.z * 0.3f) * 1.8f - 0.4f);
							float roll = random.NextFloat(), pick = random.NextFloat(), size = random.NextFloat(), yaw = random.Range(0f, 360f);
							if (roll >= lit * (0.3f + 0.7f * patch) || !FloorAndRoof(p, out float ground, out float roof))
							{
								continue;
							}
							var at = new Vector3(p.x, ground - 0.03f, p.z);
							// Not under a grotto's pool or a sea cave's tide.
							if (!float.IsNaN(system.WaterLevel) && system.Origin.y + ground < system.WaterLevel + 0.05f)
							{
								continue;
							}
							bool nearWall = Mathf.Abs(Vector3.Dot(p - centre, across)) > 0.6f * radius;
							int species;
							float scale;
							if (growth.Bushes.Count > 0 && pick < 0.15f && lit > 0.35f && roof - ground > 2.4f && !InLane(at, 0f))
							{
								species = growth.Bushes[Mathf.FloorToInt(size * 997f) % growth.Bushes.Count];
								scale = 0.55f + 0.45f * lit;
							}
							else if (growth.Creeper >= 0 && (nearWall ? pick < 0.5f : pick < 0.25f))
							{
								species = growth.Creeper;
								scale = 0.8f + 0.5f * size;
							}
							else if (growth.Ferns.Count > 0)
							{
								int fern = Mathf.Abs(Mathf.FloorToInt(Mathf.PerlinNoise(noise.x + 3f + p.x * 0.08f, noise.y + p.z * 0.08f) * 997f)) % growth.Ferns.Count;
								species = growth.Ferns[fern];
								// Smaller the less light they have.
								scale = (0.55f + 0.45f * Mathf.Sqrt(lit)) * (0.8f + 0.4f * size);
							}
							else
							{
								continue;
							}
							if (roof - ground < 1.2f || InLane(at, Trodden - Lane))
							{
								continue;
							}
							if (Place(growth.Species[species], at, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale))
							{
								Grown++;
							}
						}
					}
				}
			}

			/// <summary>The passage's bore radius at an arc length along it, between its nodes.</summary>
			private static float RadiusAt(List<CaveNode> nodes, float along)
			{
				int i = 1;
				while (i < nodes.Count - 1 && nodes[i].Along < along)
				{
					i++;
				}
				CaveNode a = nodes[i - 1], b = nodes[i];
				return Mathf.Lerp(a.Radius, b.Radius, Mathf.Clamp01((along - a.Along) / Mathf.Max(1e-4f, b.Along - a.Along)));
			}

			// ── Chambers ──────────────────────────────────────────

			private void Chamber(CaveChamber chamber)
			{
				float cy = chamber.Yaw * Mathf.Deg2Rad;
				var along = new Vector3(Mathf.Sin(cy), 0f, Mathf.Cos(cy));
				var across = new Vector3(along.z, 0f, -along.x);
				float area = Mathf.PI * chamber.Radii.x * chamber.Radii.z;
				int count = Mathf.Clamp(Mathf.RoundToInt(area / 14f * set.Density), 0, 40);
				bool cavern = chamber.Kind == CaveChamberKind.Cavern;
				for (int i = 0; i < count; i++)
				{
					// The outer ring, 0.45–0.92 of the way to the wall.
					float a = rng.Range(0f, 2f * Mathf.PI), t = Mathf.Sqrt(rng.Range(0.2f, 0.85f));
					Vector3 p = chamber.Centre + along * (Mathf.Cos(a) * t * chamber.Radii.x) + across * (Mathf.Sin(a) * t * chamber.Radii.z);
					p.y = CaveSystem.FloorOf(chamber, p) + 0.5f;
					float roll = rng.NextFloat();
					if (roll < set.BreakdownShare)
					{
						Breakdown(p);
					}
					else if (set.Pool != null && cavern && roll < set.BreakdownShare + 0.12f)
					{
						FloorPiece(p, set.Pool, 0.5f, rng.Range(0.7f, 1.3f), wall: false);
					}
					else
					{
						Dripstone(p, rng.NextFloat() < 0.45f);
					}
				}
				// Flowstone down the walls: at the wall's foot, its back to the rock.
				if (set.Wall != null)
				{
					int walls = Mathf.Clamp(Mathf.RoundToInt(area / 60f * set.Density), 0, 8);
					for (int i = 0; i < walls; i++)
					{
						float a = rng.Range(0f, 2f * Mathf.PI);
						Vector3 dir = along * Mathf.Cos(a) + across * Mathf.Sin(a);
						Vector3 p = new Vector3(chamber.Centre.x, chamber.Floor + 0.5f, chamber.Centre.z);
						if (WallFoot(p, dir, Mathf.Max(chamber.Radii.x, chamber.Radii.z) + 2f, out Vector3 foot, chamber))
						{
							FloorPiece(foot - dir * 0.6f, set.Wall, set.WallHeight, rng.Range(0.7f, 1.2f), wall: true, facing: -dir);
						}
					}
				}
				// Crystals: a cluster or three in some chambers. A deposit (a cave's, a grotto's) is the biome's crystals, a kind by
				// weight each, lit as its kind is (selenite with a cave light beside it, as every cave's was); an ice cave's are its ice.
				bool deposit = set.CrystalRock != null;
				if (set.Crystal != null && (!deposit || BiomeCaveCrystals.Total(set.Crystals) > 0f) && rng.NextFloat() < set.CrystalChance * (cavern ? 1.5f : 1f))
				{
					int clusters = rng.Range(1, 4);
					for (int i = 0; i < clusters; i++)
					{
						float a = rng.Range(0f, 2f * Mathf.PI);
						Vector3 dir = along * Mathf.Cos(a) + across * Mathf.Sin(a);
						Vector3 p = new Vector3(chamber.Centre.x, chamber.Floor + 0.5f, chamber.Centre.z);
						float reach = Mathf.Clamp(0.4f * Mathf.Max(chamber.Radii.x, chamber.Radii.z), 5f, 10f);
						if (!deposit)
						{
							if (WallFoot(p, dir, Mathf.Max(chamber.Radii.x, chamber.Radii.z) + 2f, out Vector3 foot, chamber)
								&& FloorPiece(foot - dir * 0.5f, set.Crystal, 1f, rng.Range(0.8f, 1.6f), true, -dir, set.CrystalRock, out Vector3 at))
							{
								Glow(at + Vector3.up * 0.8f, reach);
							}
							continue;
						}
						BiomeCaveCrystals.TryPick(set.Crystals, BiomeCaveCrystals.NeedsRoll(set.Crystals) ? crystalPick.NextFloat() : 0f, out BiomeCaveCrystalKind kind);
						CaveMineral mineral = ProceduralArtCatalogue.CaveMineralOf(kind);
						if (WallFoot(p, dir, Mathf.Max(chamber.Radii.x, chamber.Radii.z) + 2f, out Vector3 depositFoot, chamber)
							&& FloorPiece(depositFoot - dir * 0.5f, () => Mineral(kind), 1f, rng.Range(0.8f, 1.6f), true, -dir, out Vector3 laid) && mineral.Lit)
						{
							Glow(laid + Vector3.up * 0.8f, reach, mineral.LightHue >= 0 ? mineral.LightHue : set.Hue, mineral.Flickers);
						}
					}
				}
			}

			// ── Passages ──────────────────────────────────────────

			private void Passage(CavePassage passage)
			{
				if (set.Density <= 0f)
				{
					return;
				}
				float next = system.Mouths[0].Zone * 0.6f;
				for (int i = 1; i < passage.Nodes.Count; i++)
				{
					CaveNode node = passage.Nodes[i];
					if (node.Along < next || NearMouth(node.Centre))
					{
						continue;
					}
					next = node.Along + rng.Range(5f, 12f) / set.Density;
					Vector3 dir = node.Centre - passage.Nodes[i - 1].Centre;
					dir.y = 0f;
					dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
					Vector3 side = new Vector3(dir.z, 0f, -dir.x) * (rng.NextFloat() < 0.5f ? -1f : 1f);
					var p = new Vector3(node.Centre.x, node.Floor + 0.5f, node.Centre.z);
					// Against one wall: as far out as the floor goes, less a formation's reach.
					if (WallFoot(p, side, 2f * node.Radius + 1f, out Vector3 foot))
					{
						Vector3 at = foot - side * 0.7f;
						if (rng.NextFloat() < 0.3f)
						{
							Breakdown(at);
						}
						else
						{
							Dripstone(at, rng.NextFloat() < 0.35f);
						}
					}
					// Straws and stalactites over the passage, short enough over its lane.
					if (set.Hanging != null && rng.NextFloat() < 0.6f)
					{
						Vector3 over = p + side * rng.Range(-0.5f, 0.5f) * node.Radius + dir * rng.Range(-1.5f, 1.5f);
						Hang(over, rng.Range(0.25f, 0.6f));
					}
				}
			}

			// ── Pieces ────────────────────────────────────────────

			/// <summary>A stalagmite on the floor, maybe the stalactite over it, and where they come near enough a column.</summary>
			private void Dripstone(Vector3 near, bool withHanging)
			{
				if (set.Floor == null)
				{
					Breakdown(near);
					return;
				}
				if (!FloorAndRoof(near, out float floor, out float roof))
				{
					return;
				}
				var at = new Vector3(near.x, floor, near.z);
				if (InLane(at, 0.8f))
				{
					return;
				}
				float room = roof - floor;
				// A column where the roof is low enough for the two to have met (a quarter of them).
				bool column = room < 7f && rng.NextFloat() < 0.25f;
				float up = column ? 0.55f * room : Mathf.Min(rng.Range(0.4f, 1.5f) * set.FloorHeight, 0.55f * room);
				float scale = up / set.FloorHeight;
				if (scale < 0.15f)
				{
					return;
				}
				float width = Mathf.Clamp(scale * rng.Range(0.8f, 1.3f), 0.3f, 2.2f);
				Place(Formation(set.Floor), at, Quaternion.Euler(0f, rng.Range(0f, 360f), 0f), new Vector3(width, scale, width));
				if (set.Hanging != null && (withHanging || column))
				{
					float down = column ? 0.5f * room : Mathf.Min(rng.Range(0.3f, 0.9f) * set.FloorHeight, 0.4f * room);
					float s = down / set.FloorHeight;
					Place(Formation(set.Hanging), new Vector3(at.x, roof + 0.05f, at.z), Quaternion.Euler(180f, rng.Range(0f, 360f), 0f), new Vector3(width * 0.8f, s, width * 0.8f));
				}
			}

			/// <summary>A stalactite (or a cluster of straws) hung from the roof over a point, its length a share of the formation's.</summary>
			private void Hang(Vector3 under, float share)
			{
				if (!FloorAndRoof(under, out float floor, out float roof))
				{
					return;
				}
				float down = share * set.FloorHeight;
				// Over a lane, never lower than a tall person's head.
				if (InLane(new Vector3(under.x, floor, under.z), 0f))
				{
					down = Mathf.Min(down, roof - floor - Headroom);
				}
				float s = down / set.FloorHeight;
				if (s < 0.08f)
				{
					return;
				}
				float width = Mathf.Clamp(s * rng.Range(0.6f, 1f), 0.15f, 1.2f);
				Place(Formation(set.Hanging), new Vector3(under.x, roof + 0.05f, under.z), Quaternion.Euler(180f, rng.Range(0f, 360f), 0f), new Vector3(width, s, width));
			}

			/// <summary>A block or slab fallen from the roof, or spoil.</summary>
			private void Breakdown(Vector3 near)
			{
				if (set.Breakdown == null || !FloorAndRoof(near, out float floor, out _))
				{
					return;
				}
				var at = new Vector3(near.x, floor, near.z);
				if (InLane(at, 1f))
				{
					return;
				}
				float s = rng.Range(0.2f, 0.55f);
				Place(Formation(set.Breakdown), at, Quaternion.Euler(rng.Range(-8f, 8f), rng.Range(0f, 360f), rng.Range(-8f, 8f)), new Vector3(s * rng.Range(0.8f, 1.2f), s, s * rng.Range(0.8f, 1.2f)));
			}

			private bool FloorPiece(Vector3 near, string shape, float height, float scale, bool wall, Vector3 facing = default, string rock = null)
				=> FloorPiece(near, shape, height, scale, wall, facing, rock, out _);

			private bool FloorPiece(Vector3 near, string shape, float height, float scale, bool wall, Vector3 facing, string rock, out Vector3 at)
				=> FloorPiece(near, () => Formation(shape, rock), height, scale, wall, facing, out at);

			/// <summary>
			/// A piece on the floor near a point, against a wall (facing out of it) or free: false where the floor is not found, the
			/// roof is too low for it or it would stand in a lane. <paramref name="prefab"/> is asked for its prefab only once the
			/// spot is taken, at the very point the formation's variant was always drawn.
			/// </summary>
			private bool FloorPiece(Vector3 near, System.Func<GameObject> prefab, float height, float scale, bool wall, Vector3 facing, out Vector3 at)
			{
				at = default;
				if (!FloorAndRoof(near, out float floor, out float roof) || roof - floor < height * scale + 0.3f)
				{
					return false;
				}
				at = new Vector3(near.x, floor, near.z);
				if (InLane(at, wall ? 0.6f : 1.6f * scale))
				{
					return false;
				}
				float yaw = facing.sqrMagnitude > 1e-4f ? Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg : rng.Range(0f, 360f);
				return Place(prefab(), at, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale);
			}

			// ── Mines ─────────────────────────────────────────────

			private void Mine()
			{
				// The portal at each mouth, facing out.
				int portal = PortalVariant(context.Template != null ? context.Template.name : null);
				foreach (CaveMouth mouth in system.Mouths)
				{
					float s = mouth.Radius / 2.1f;
					Place(StructureKit.Prefab("MinePortal", portal), mouth.Floor + Dir(mouth.Yaw) * 0.4f, Quaternion.Euler(0f, mouth.Yaw + 180f, 0f), new Vector3(s, Mathf.Max(1f, s), 1f));
				}
				// The drifts' lights and the stopes' (the biome's Cave Lights, 2026-10-16: torches in sconces every 14–20 m and a
				// torch on a pole in each stope in every biome before).
				bool driftLit = CaveLight(BiomeCaveLightPlace.MineDrift, out BiomeCaveLight drift);
				BiomeCaveLights.DriftGap(drift.Spacing, out float gapMin, out float gapMax);
				if (!driftLit)
				{
					BiomeCaveLights.DriftGap(BiomeCaveLights.DriftSpacing, out gapMin, out gapMax);
				}
				bool stopeLit = CaveLight(BiomeCaveLightPlace.MineStope, out BiomeCaveLight stopeLight);
				foreach (CavePassage passage in system.Passages)
				{
					float nextSet = 2f, nextLamp = 8f;
					bool trunk = passage.Kind == CavePassageKind.Trunk;
					for (int i = 1; i < passage.Nodes.Count; i++)
					{
						CaveNode a = passage.Nodes[i - 1], b = passage.Nodes[i];
						float length = b.Along - a.Along;
						if (length <= 1e-3f)
						{
							continue;
						}
						Vector3 dir = b.Centre - a.Centre;
						float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
						for (float along = Mathf.Max(a.Along, nextSet); along < b.Along; along = nextSet)
						{
							float t = (along - a.Along) / length;
							float floor = Mathf.Lerp(a.Floor, b.Floor, t);
							Vector3 c = Vector3.Lerp(a.Centre, b.Centre, t);
							float radius = Mathf.Lerp(a.Radius, b.Radius, t);
							// Sized to the bore: its posts inside the floor's width, its cap under the roof. On the floor as carved
							// under the lower of its two feet (the field's floor is not the nodes' line), so neither post stands off it.
							float s = radius / 2.1f;
							Vector3 across = new Vector3(dir.z, 0f, -dir.x).normalized * (StructurePieces.MineSetHalfWidth * s);
							float footA = FloorUnder(c + across, floor), footB = FloorUnder(c - across, floor);
							Place(StructureKit.Prefab("MineSet", rng.NextFloat() < 0.5f ? 1 : 0), new Vector3(c.x, Mathf.Min(footA, footB), c.z), Quaternion.Euler(0f, yaw, 0f), new Vector3(s, Mathf.Clamp(s, 0.95f, 1.1f), 1f));
							nextSet = along + rng.Range(2.6f, 3.2f);
						}
						for (float along = Mathf.Max(a.Along, nextLamp); along < b.Along; along = nextLamp)
						{
							float t = (along - a.Along) / length;
							Vector3 c = Vector3.Lerp(a.Centre, b.Centre, t);
							Vector3 side = new Vector3(dir.z, 0f, -dir.x).normalized;
							float floor = Mathf.Lerp(a.Floor, b.Floor, t);
							// The drift's light on the rock at head height (a torch in its sconce, leaning into the drift, before): a wall light
							// on the rock, a ground light at the wall's foot, a hanging one from the roof by the wall (the piece gives off its light).
							float hand = rng.NextFloat() < 0.5f ? 1f : -1f;
							if (WallFoot(new Vector3(c.x, floor + 1.8f, c.z), side * hand, 2f * StructurePieces.MineSetHalfWidth + 1f, out Vector3 rock))
							{
								int variant = rng.NextFloat() < 0.5f ? 1 : 0;
								if (driftLit)
								{
									DriftLight(in drift, variant, rock, side * hand, floor);
								}
							}
							nextLamp = along + rng.Range(gapMin, gapMax);
						}
					}
					if (trunk)
					{
						LayTrack(passage);
					}
				}
				// Every stope lit, and its spoil and the ore's glint along its walls.
				foreach (CaveChamber stope in system.Chambers)
				{
					// A torch on a pole where the work was, by the wall.
					Vector3 post = Dir(rng.Range(0f, 360f));
					if (WallFoot(new Vector3(stope.Centre.x, stope.Floor + 0.5f, stope.Centre.z), post, Mathf.Max(stope.Radii.x, stope.Radii.z) + 2f, out Vector3 postFoot, stope)
						&& FloorAndRoof(postFoot - post * 0.8f, out float postFloor, out float postRoof) && postRoof - postFloor > 3f)
					{
						Vector3 at = postFoot - post * 0.8f;
						int variant = rng.NextFloat() < 0.5f ? 1 : 0;
						Quaternion turn = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);
						if (stopeLit)
						{
							GroundLight(in stopeLight, variant, new Vector3(at.x, postFloor, at.z), turn);
						}
					}
					int glints = rng.Range(2, 5);
					for (int i = 0; i < glints; i++)
					{
						Vector3 dir = Dir(rng.Range(0f, 360f));
						if (WallFoot(new Vector3(stope.Centre.x, stope.Floor + 0.5f, stope.Centre.z), dir, Mathf.Max(stope.Radii.x, stope.Radii.z) + 2f, out Vector3 foot, stope))
						{
							FloorPiece(foot - dir * 0.4f, "Cubes", 1f, rng.Range(0.5f, 0.9f), true, -dir, "Pyrite");
						}
					}
				}
			}

			/// <summary>MineTrack's length (StructurePieces.Mine): its pivot is at its middle, on the sleepers' underside.</summary>
			private const float TrackPieceMetres = 3f;

			/// <summary>MineTrack's rail top over its pivot: where a tub's wheels stand.</summary>
			private const float RailTopMetres = 0.14f;

			/// <summary>How far a piece's sleepers may sit into a rise of the floor under it (their depth is 0.08 m).</summary>
			private const float SleeperBedMetres = 0.05f;

			/// <summary>Floor samples along each piece.</summary>
			private const int TrackSamples = 7;

			/// <summary>
			/// The track along a trunk, a piece after another by arc length along its centre line (crossing its nodes, so no
			/// segment shorter than a piece is skipped: a 1.5 m gap was left every 7.5 m on a mine's 2.5 m steps), laid on the
			/// floor as carved, not on the nodes' line (the field's smooth union carves the floor up to ~0.45 m under it, and
			/// unevenly). The rails run on unbroken: each joint takes one height, first the floor's under it, then raised
			/// until no floor sample along either piece stands more than <see cref="SleeperBedMetres"/> over the line between
			/// its joints; each piece is pitched joint to joint. A tub stands on the rails at the track's end.
			/// </summary>
			private void LayTrack(CavePassage passage)
			{
				List<CaveNode> nodes = passage.Nodes;
				if (nodes.Count < 2)
				{
					return;
				}
				float start = nodes[0].Along;
				int count = Mathf.FloorToInt((nodes[nodes.Count - 1].Along - start) / TrackPieceMetres);
				if (count < 1)
				{
					return;
				}
				var joints = new Vector3[count + 1];
				var floors = new float[count, TrackSamples];
				var heights = new float[count + 1];
				for (int j = 0; j <= count; j++)
				{
					CentreAt(nodes, start + j * TrackPieceMetres, out joints[j], out _);
				}
				// The floor under each piece, as the mesh is carved; a joint starts on the lower of its two pieces' floors there.
				for (int j = 0; j < count; j++)
				{
					Vector3 a = joints[j], b = joints[j + 1];
					for (int k = 0; k < TrackSamples; k++)
					{
						float f = k / (TrackSamples - 1f);
						CentreAt(nodes, start + (j + f) * TrackPieceMetres, out _, out float nominal);
						floors[j, k] = FloorUnder(new Vector3(Mathf.Lerp(a.x, b.x, f), 0f, Mathf.Lerp(a.z, b.z, f)), nominal);
					}
					heights[j] = j == 0 ? floors[0, 0] : Mathf.Min(heights[j], floors[j, 0]);
					heights[j + 1] = floors[j, TrackSamples - 1];
				}
				// Raised where the floor rises between joints (over a hump, the sleepers bed into it a little), a few passes so
				// a raise at one joint is seen by the piece beyond it.
				for (int pass = 0; pass < 8; pass++)
				{
					bool raised = false;
					for (int j = 0; j < count; j++)
					{
						float over = float.MinValue;
						for (int k = 0; k < TrackSamples; k++)
						{
							over = Mathf.Max(over, floors[j, k] - Mathf.Lerp(heights[j], heights[j + 1], k / (TrackSamples - 1f)));
						}
						if (over > SleeperBedMetres + 1e-3f)
						{
							heights[j] += over - SleeperBedMetres;
							heights[j + 1] += over - SleeperBedMetres;
							raised = true;
						}
					}
					if (!raised)
					{
						break;
					}
				}
				Vector3 lastCentre = default;
				Quaternion lastTurn = default;
				bool laid = false;
				for (int j = 0; j < count; j++)
				{
					var a = new Vector3(joints[j].x, heights[j], joints[j].z);
					var b = new Vector3(joints[j + 1].x, heights[j + 1], joints[j + 1].z);
					var flat = new Vector2(b.x - a.x, b.z - a.z);
					if (flat.magnitude < 0.5f)
					{
						continue;
					}
					float yaw = Mathf.Atan2(flat.x, flat.y) * Mathf.Rad2Deg;
					float pitch = -Mathf.Atan2(b.y - a.y, flat.magnitude) * Mathf.Rad2Deg;
					Quaternion turn = Quaternion.Euler(pitch, yaw, 0f);
					Vector3 centre = 0.5f * (a + b);
					Place(StructureKit.Prefab("MineTrack", rng.NextFloat() < 0.15f ? 1 : 0), centre, turn, Vector3.one);
					laid = true;
					lastCentre = centre;
					lastTurn = turn;
				}
				// A tub on the last piece's rails, or tipped off them beside it; by the last stope.
				if (laid && nodes.Count > 2)
				{
					if (rng.NextFloat() < 0.3f)
					{
						Vector3 side = lastTurn * Vector3.right;
						Vector3 at = lastCentre + new Vector3(side.x, 0f, side.z).normalized * 1.1f;
						Place(StructureKit.Prefab("MineCart", 1), new Vector3(at.x, FloorUnder(at, lastCentre.y), at.z), Quaternion.Euler(0f, lastTurn.eulerAngles.y, 0f), Vector3.one);
					}
					else
					{
						Place(StructureKit.Prefab("MineCart", 0), lastCentre + lastTurn * new Vector3(0f, RailTopMetres, 0f), lastTurn, Vector3.one);
					}
				}
			}

			/// <summary>The passage's centre line and nominal floor at an arc length along it (system frame), between its nodes.</summary>
			private static void CentreAt(List<CaveNode> nodes, float along, out Vector3 centre, out float floor)
			{
				int i = 1;
				while (i < nodes.Count - 1 && nodes[i].Along < along)
				{
					i++;
				}
				CaveNode a = nodes[i - 1], b = nodes[i];
				float t = Mathf.Clamp01((along - a.Along) / Mathf.Max(1e-4f, b.Along - a.Along));
				centre = Vector3.Lerp(a.Centre, b.Centre, t);
				floor = Mathf.Lerp(a.Floor, b.Floor, t);
			}

			/// <summary>
			/// The floor as carved under (x, z) of <paramref name="p"/> (system frame), to a centimetre: marched down from a
			/// little over <paramref name="nominal"/> (the nodes' floor, which the carved floor lies a little under where
			/// segments and chambers meet) and bisected; <paramref name="nominal"/> where that point is rock.
			/// </summary>
			private float FloorUnder(Vector3 p, float nominal)
			{
				float top = nominal + 0.6f;
				if (system.Void(new Vector3(p.x, top, p.z)) >= 0f)
				{
					return nominal;
				}
				float y = top, step = 0.1f;
				while (system.Void(new Vector3(p.x, y - step, p.z)) < 0f && y > nominal - 3f)
				{
					y -= step;
				}
				// The surface lies between y - step (rock) and y (void).
				float lo = y - step, hi = y;
				for (int k = 0; k < 4; k++)
				{
					float mid = 0.5f * (lo + hi);
					if (system.Void(new Vector3(p.x, mid, p.z)) < 0f)
					{
						hi = mid;
					}
					else
					{
						lo = mid;
					}
				}
				return 0.5f * (lo + hi);
			}

			/// <summary>The mine's portal to its template: boarded up when abandoned, dressed stone for a delving, timber otherwise.</summary>
			private static int PortalVariant(string template)
			{
				if (string.IsNullOrEmpty(template))
				{
					return 0;
				}
				return template.Contains("Abandoned") ? 1 : template.Contains("Delving") ? 2 : 0;
			}

			// ── Lights ────────────────────────────────────────────

			private readonly Dictionary<BiomeCaveLightPlace, (bool lit, BiomeCaveLight light)> caveLights = new Dictionary<BiomeCaveLightPlace, (bool, BiomeCaveLight)>();

			/// <summary>How the cave's biome lights a place in its caves and mines (BiomeTemplateArt.CaveLight, read once a place); false where it is unlit.</summary>
			private bool CaveLight(BiomeCaveLightPlace place, out BiomeCaveLight light)
			{
				if (!caveLights.TryGetValue(place, out (bool lit, BiomeCaveLight light) known))
				{
					known.lit = CaveInteriorArt.CaveLight(in caves, place, breathable, out known.light);
					caveLights[place] = known;
				}
				light = known.light;
				return known.lit;
			}

			/// <summary>A light's kit piece in a variant (wrapped to the piece's own), or null until the art is generated.</summary>
			private GameObject LightPiece(BiomeWayLightKind light, int variant)
			{
				string id = BiomeWayLights.PieceId(light);
				StructurePiece piece = StructureKit.Get(id);
				if (piece == null)
				{
					Missing.Add(id);
					return null;
				}
				GameObject prefab = StructureKit.Prefab(id, ((variant % piece.Variants) + piece.Variants) % piece.Variants);
				if (prefab == null)
				{
					Missing.Add(id);
				}
				return prefab;
			}

			/// <summary>A light that stands on the ground at a spot (system frame); a wall or hanging light is not stood on a floor (noted).</summary>
			private bool GroundLight(in BiomeCaveLight light, int variant, Vector3 local, Quaternion rotation)
			{
				if (BiomeWayLights.MountOf(light.Light) != BiomeWayLightMount.Ground)
				{
					Unfit.Add($"{light.Place}: {light.Light}");
					return false;
				}
				return Place(LightPiece(light.Light, variant), local, rotation, Vector3.one);
			}

			/// <summary>
			/// A drift's light where a ray across it met the rock at head height (<paramref name="rock"/>, the ray along
			/// <paramref name="into"/>): a wall light on the rock (its back plate to it, as the drift's torches were), a ground light
			/// at the wall's foot, a hanging one from the roof a pace off the wall where it clears a tall person's head.
			/// </summary>
			private void DriftLight(in BiomeCaveLight light, int variant, Vector3 rock, Vector3 into, float floor)
			{
				switch (BiomeWayLights.MountOf(light.Light))
				{
					case BiomeWayLightMount.Wall:
						Place(LightPiece(light.Light, variant), rock - into * 0.05f, Quaternion.LookRotation(into, Vector3.up), Vector3.one);
						break;
					case BiomeWayLightMount.Ground:
					{
						Vector3 foot = rock - into * 0.5f;
						Place(LightPiece(light.Light, variant), new Vector3(foot.x, FloorUnder(foot, floor), foot.z), Quaternion.LookRotation(-into, Vector3.up), Vector3.one);
						break;
					}
					default:
					{
						Vector3 under = rock - into * 0.6f;
						if (Roof(under, 6f, out float roof) && roof - floor - CaveArt.HangingLanternDrop(variant) >= Headroom)
						{
							Place(LightPiece(light.Light, variant), new Vector3(under.x, roof + 0.02f, under.z), Quaternion.LookRotation(into, Vector3.up), Vector3.one);
						}
						break;
					}
				}
			}

			private void Glow(Vector3 local, float reach) => Glow(local, reach, set.Hue);

			/// <summary>
			/// A crystal's cave light: its hue in bits 8–9 (<paramref name="hue"/> −1: one by the seed); a crystal that flickers
			/// (stormglass, 2026-10-18) gives a cold glow with the flicker in the same hue instead (SceneGlow).
			/// </summary>
			private void Glow(Vector3 local, float reach, int hue, bool flickers = false)
			{
				uint seed = PointOfInterestPlanner.Hash(system.Seed, Mathf.RoundToInt(local.x * 10f), Mathf.RoundToInt(local.z * 10f), 0xC4);
				if (flickers)
				{
					Emit(local, SceneEmitterKind.Glow, SceneGlow.WithFlicker(SceneGlow.WithCrystalHue(seed, hue), true), reach);
					return;
				}
				if (hue >= 0)
				{
					seed = (seed & ~0x300u) | ((uint)hue << 8);
				}
				Emit(local, SceneEmitterKind.Crystal, seed, reach);
			}

			private void Emit(Vector3 local, SceneEmitterKind kind, uint seed, float reach)
			{
				if (context.Points == null)
				{
					return;
				}
				context.Points.Emitters ??= new List<SceneEmitter>();
				context.Points.Emitters.Add(new SceneEmitter { Position = system.Origin + local, Kind = kind, Seed = seed, Reach = reach });
				Lights++;
				if (kind == SceneEmitterKind.Crystal || kind == SceneEmitterKind.Glow)
				{
					glowLights.Add(local);
				}
			}

			// ── Geometry ──────────────────────────────────────────

			/// <summary>The floor and the roof over a point in the void (system frame), by marching the field; false where it is rock.</summary>
			private bool FloorAndRoof(Vector3 p, out float floor, out float roof)
			{
				floor = roof = p.y;
				if (system.Void(p) >= 0f)
				{
					return false;
				}
				float y = p.y;
				while (system.Void(new Vector3(p.x, y, p.z)) < 0f && y > p.y - 20f)
				{
					y -= 0.05f;
				}
				floor = y;
				y = p.y;
				while (system.Void(new Vector3(p.x, y, p.z)) < 0f && y < p.y + 60f)
				{
					y += 0.1f;
				}
				roof = y;
				return roof - floor > 1f;
			}

			/// <summary>
			/// Where a ray from <paramref name="from"/> meets the wall, its foot on the floor; false where it meets none within
			/// reach. Over a chamber's floor the ray keeps half a metre over its sloping plane, so it meets the wall and not
			/// the floor rising.
			/// </summary>
			private bool WallFoot(Vector3 from, Vector3 dir, float reach, out Vector3 foot, CaveChamber? over = null)
			{
				foot = from;
				if (system.Void(from) >= 0f)
				{
					return false;
				}
				Vector3 last = from;
				for (float d = 0.25f; d <= reach; d += 0.25f)
				{
					Vector3 p = from + dir * d;
					if (over.HasValue)
					{
						p.y = CaveSystem.FloorOf(over.Value, p) + 0.5f;
					}
					if (system.Void(p) >= 0f)
					{
						foot = last;
						return true;
					}
					last = p;
				}
				return false;
			}

			/// <summary>
			/// Within <paramref name="margin"/> + a lane of any passage's line on the floor, or in a site's camp ground; or on a
			/// way the layout built (a bridge's or ramp's deck, a crossing, a pit's ledge, the foot or the head of a climb).
			/// </summary>
			/// <remarks><paramref name="exceptSolid"/>: a solid's index whose own deck or ledge is not refused (what lines it).</remarks>
			private bool InLane(Vector3 p, float margin, int exceptSolid = -1)
			{
				if (OnFeature(p, margin, exceptSolid))
				{
					return true;
				}
				foreach ((Vector3 centre, float radius) in keepClear)
				{
					if (Mathf.Abs(p.y - centre.y) < 3f && new Vector2(p.x - centre.x, p.z - centre.z).sqrMagnitude < (radius + margin) * (radius + margin))
					{
						return true;
					}
				}
				float reach = Lane + margin;
				foreach ((Vector3 a, Vector3 b) in lanes)
				{
					var ab = new Vector2(b.x - a.x, b.z - a.z);
					var ap = new Vector2(p.x - a.x, p.z - a.z);
					float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					if ((ap - ab * t).sqrMagnitude < reach * reach && Mathf.Abs(p.y - Mathf.Lerp(a.y, b.y, t)) < 3f)
					{
						return true;
					}
				}
				return false;
			}

			/// <summary>On, or within <paramref name="margin"/> of, a way the layout built: nothing is set down there.</summary>
			private bool OnFeature(Vector3 p, float margin, int exceptSolid = -1)
			{
				for (int i = 0; i < system.Solids.Count; i++)
				{
					if (i == exceptSolid)
					{
						continue;
					}
					CaveSolid solid = system.Solids[i];
					switch (solid.Kind)
					{
						case CaveSolidKind.Bridge:
						case CaveSolidKind.Ramp:
							if (NearLine(p, solid.A, solid.B, solid.Width + 0.6f + margin, 4f))
							{
								return true;
							}
							break;
						case CaveSolidKind.Spiral:
							// The whole pit: its ledge winds all round it, and its floor is where the ledge comes down.
							if (new Vector2(p.x - solid.A.x, p.z - solid.A.z).magnitude < solid.Depth + 1f + margin && p.y > solid.A.y - 2f && p.y < solid.B.y + 4f)
							{
								return true;
							}
							break;
					}
				}
				foreach (CaveCrossing crossing in system.Crossings)
				{
					if (NearLine(p, crossing.A, crossing.B, crossing.HalfWidth + 1f + margin, 4f))
					{
						return true;
					}
				}
				// A terrace's walk along its top, a little in from its lip (CaveSystem.TerraceWalk), and the pits that drop out of
				// a floor with a way down them (2026-10-20, the layout's variety): nothing on the walk, nothing in the shaft.
				foreach (CaveTerrace terrace in system.Terraces)
				{
					if (terrace.Solid < 0 || terrace.Solid >= system.Solids.Count)
					{
						continue;
					}
					CaveSolid slab = system.Solids[terrace.Solid];
					float inset = Mathf.Min(1.2f, Mathf.Max(0.6f, 0.5f * terrace.Walk - 0.3f));
					Vector3 edge = terrace.Out * (slab.Width - inset);
					if (NearLine(p, slab.A + edge, slab.B + edge, 0.9f + margin, 2.5f))
					{
						return true;
					}
				}
				foreach (CavePit pit in system.Pits)
				{
					bool drop = pit.Kind == CavePitKind.Shaft || pit.Kind == CavePitKind.Winze || pit.Kind == CavePitKind.Ledged;
					if (drop && new Vector2(p.x - pit.Rim.x, p.z - pit.Rim.z).magnitude < pit.Radius + 1f + margin && p.y > pit.Bottom - 2f && p.y < pit.Rim.y + 4f)
					{
						return true;
					}
				}
				foreach (CaveClimb climb in system.Climbs)
				{
					if (NearLine(p, climb.Bottom, climb.Bottom + climb.Out * 1.5f, 1.2f + margin, 3f)
						|| NearLine(p, climb.Top, climb.Top + climb.TopExit * 2f, 1.2f + margin, 3f))
					{
						return true;
					}
				}
				return false;
			}

			/// <summary>Within <paramref name="reach"/> of a segment in plan, and within <paramref name="height"/> of it.</summary>
			private static bool NearLine(Vector3 p, Vector3 a, Vector3 b, float reach, float height)
			{
				var ab = new Vector2(b.x - a.x, b.z - a.z);
				var ap = new Vector2(p.x - a.x, p.z - a.z);
				float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
				return (ap - ab * t).sqrMagnitude < reach * reach && Mathf.Abs(p.y - Mathf.Lerp(a.y, b.y, t)) < height;
			}

			private bool NearMouth(Vector3 p)
			{
				foreach (CaveMouth mouth in system.Mouths)
				{
					if ((p - mouth.Floor).sqrMagnitude < mouth.Zone * mouth.Zone * 0.36f)
					{
						return true;
					}
				}
				return false;
			}

			private static Vector3 Dir(float yaw)
			{
				float r = yaw * Mathf.Deg2Rad;
				return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
			}

			/// <summary>
			/// A formation's prefab: in its own named rock (the crystals' selenite), else in the shell's, by the art that rock has
			/// (<see cref="FormationArt"/>). The variant is drawn first either way, so a stand-in leaves the draws after it as they were.
			/// </summary>
			private GameObject Formation(string shape, string rock = null)
			{
				int variant = rng.Range(0, RockTypes.VariantCount);
				string artRock = rock, artShape = shape;
				if (rock == null)
				{
					(artRock, artShape) = FormationArt(set.Rock, shape, system.Form);
					if (artRock != set.Rock)
					{
						StoodIn.Add($"{shape} ({artRock}'s)");
					}
					else if (artShape != shape)
					{
						StoodIn.Add($"{shape} (as {artShape})");
					}
				}
				string name = RockArtNames.FormationPrefab(artRock, artShape, variant);
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(name));
				if (prefab == null)
				{
					Missing.Add($"{artRock} {artShape}");
				}
				return prefab;
			}

			/// <summary>
			/// A cave crystal's prefab (CaveArt.MineralPrefab): its variant drawn first as a formation's is, so selenite (gypsum's
			/// Crystal formation) is laid from the very same draws as before 2026-10-16.
			/// </summary>
			private GameObject Mineral(BiomeCaveCrystalKind kind)
			{
				int variant = rng.Range(0, RockTypes.VariantCount);
				GameObject prefab = CaveArt.MineralPrefab(kind, variant);
				if (prefab == null)
				{
					CaveMineral mineral = ProceduralArtCatalogue.CaveMineralOf(kind);
					Missing.Add(mineral.Formation ? $"{mineral.FormationRock} {mineral.FormationShape}" : $"cave crystal {kind}");
				}
				return prefab;
			}

			private bool Place(GameObject prefab, Vector3 local, Quaternion rotation, Vector3 scale)
			{
				if (prefab == null)
				{
					return false;
				}
				// Nothing stands in molten lava, and a molten tube's glowing crystals give way to it (2026-10-20): refused after every
				// draw for it, so no stream moves (never in a cooled cave).
				if (InLava(local) || LavaOutshines(prefab, local))
				{
					return false;
				}
				// Nor in a wet cave's water but its rock (2026-10-20, caves livened, PointOfInterestCaveDressing.Streams.cs), likewise.
				if (InStream(prefab, local))
				{
					return false;
				}
				context.AddProp(prefab, system.Origin + local, rotation, scale);
				Placed++;
				return true;
			}
		}
	}
}
#endif
