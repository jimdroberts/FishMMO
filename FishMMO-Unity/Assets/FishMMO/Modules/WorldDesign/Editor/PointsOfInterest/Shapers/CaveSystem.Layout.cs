#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The planner for a cave system with a <see cref="CaveCharacter"/> (2026-10-13: "Layouts are pretty predictable and no
	/// massive features (enormous caves with lots of glowy rocks lining the walkable pathways, rock bridges leading over
	/// crevices and to other deeper areas, etc)"): a graph, not a line.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Hubs and levels.</b> The trunk comes down from the mouth to a first hub: a room, or an enormous hall of terraced
	/// lobes joined by ramps. From each hub, branches go off to more hubs on the same level, and one way goes down to the
	/// level under it: a chasm split through the hub's floor with a bridge over it and a climb down into it, a great pit with
	/// a ledge wound down its wall, a narrow chimney with a rope, or a steep passage. Afterwards some hubs far apart on a
	/// level are joined again (loops), giant columns stand in the halls, and the deepest, greatest chamber is the site's.
	/// </para>
	/// <para>
	/// <b>Never a trap.</b> No fall damage, so a chasm or pit is not a hazard; but whatever can be fallen into has a way
	/// out — the ledge, the climb, or the level its floor opens onto. Every climb is checked against the noisy field itself
	/// (clear of the walls along its length, ground at its foot, a floor to step off onto at its top); a feature whose climb
	/// does not fit is taken back out whole.
	/// </para>
	/// <para>
	/// <b>Character.</b> The same pieces in different measure: halls get halls and columns; rifts tall canyon passages and
	/// chasms; an abyss three levels, pits and chasms; a warren many small branches and loops; a crystal cave galleries (the
	/// dressing lines them). Classic keeps the old single line (CaveSystem.Plan).
	/// </para>
	/// </remarks>
	public sealed partial class CaveSystem
	{
		/// <summary>A form's character at a size, from a roll in 0..1.</summary>
		public static CaveCharacter CharacterFor(CaveForm form, int size, float roll)
		{
			switch (form)
			{
				case CaveForm.Mine:
					return CaveCharacter.Workings;
				case CaveForm.Grotto:
				case CaveForm.SeaCave:
					return CaveCharacter.Classic;
				case CaveForm.LavaTube:
					return roll < 0.4f ? CaveCharacter.Classic : roll < 0.78f ? CaveCharacter.Halls : CaveCharacter.Abyss;
				default:
					if (size <= 0)
					{
						return roll < 0.25f ? CaveCharacter.Classic : roll < 0.6f ? CaveCharacter.Warren : roll < 0.8f ? CaveCharacter.Rift : CaveCharacter.Crystal;
					}
					if (size == 1)
					{
						return roll < 0.22f ? CaveCharacter.Halls : roll < 0.42f ? CaveCharacter.Rift : roll < 0.58f ? CaveCharacter.Abyss : roll < 0.78f ? CaveCharacter.Warren : CaveCharacter.Crystal;
					}
					return roll < 0.38f ? CaveCharacter.Halls : roll < 0.62f ? CaveCharacter.Abyss : roll < 0.78f ? CaveCharacter.Rift : CaveCharacter.Crystal;
			}
		}

		/// <summary>How many levels a character's system goes down through.</summary>
		private static int LevelsFor(CaveCharacter character, int size, DeterministicRNG rng)
		{
			int levels;
			switch (character)
			{
				case CaveCharacter.Abyss: levels = size >= 2 ? 3 : 2; break;
				case CaveCharacter.Warren: levels = rng.NextFloat() < 0.5f ? 1 : 2; break;
				case CaveCharacter.Halls:
				case CaveCharacter.Rift:
				case CaveCharacter.Crystal: levels = 2; break;
				default: levels = 1; break;
			}
			return size <= 0 ? Mathf.Min(levels, 2) : levels;
		}

		// ── Depth (2026-10-21) ────────────────────────────────────

		/// <summary>The deepest a limestone or ice cave goes under its mouth, metres.</summary>
		public const float MaxCaveDepth = 500f;
		/// <summary>The deepest a lava tube's stacked tubes go under its mouth, metres.</summary>
		public const float MaxLavaDepth = 120f;
		/// <summary>The shallowest a rolled depth runs, metres: a pocket in the hill.</summary>
		public const float LeastDepth = 3f;
		/// <summary>Shallower than this a cave is a pocket: one short passage into one chamber (<see cref="CaveCharacter.Classic"/>).</summary>
		public const float PocketDepth = 12f;
		/// <summary>A lava tube's pocket depth: a tube runs long and nearly level, so only the shallowest is a pocket.</summary>
		public const float PocketTubeDepth = 7f;
		/// <summary>How far a level's floor may wander from its even share of the depth, as a share of the gap between levels.</summary>
		private const float LevelJitter = 0.2f;

		/// <summary>Whether a form's depth is rolled by <see cref="DepthFor"/> (limestone and ice caves, lava tubes); the rest keep their profile's.</summary>
		public static bool RollsDepth(CaveForm form) => form == CaveForm.Cave || form == CaveForm.IceCave || form == CaveForm.LavaTube;

		/// <summary>The deepest a form's system goes under its mouth, metres.</summary>
		public static float MaxDepthOf(CaveForm form) => form == CaveForm.LavaTube ? MaxLavaDepth : MaxCaveDepth;

		/// <summary>
		/// How deep a system goes under its mouth (2026-10-21: "randomness in depth up to 500m but also really small caves"): a
		/// log-logistic roll from the seed (heavy-tailed: most tens of metres, a rare few hundreds), its median by the site's size,
		/// cut to <see cref="LeastDepth"/> .. <see cref="MaxDepthOf"/> without piling up at either end. Limestone and ice caves:
		/// medians 22 / 45 / 80 m by size, about one in 65 / 24 / 10 past 300 m and one in 4 / 10 / 26 under 12 m (a pocket). Lava
		/// tubes: 14 / 23 / 32 m, stacked up to 120 m. Before the sea's cap (the plan keeps every floor over the sea).
		/// </summary>
		public static float DepthFor(CaveForm form, int size, int seed) => DepthFor(form, size, seed, LeastDepth, MaxCaveDepth);

		/// <summary>
		/// As <see cref="DepthFor(CaveForm, int, int)"/>, within a scene's own range (2026-10-21, the pre-cut prompt's cave depth,
		/// PointOfInterestSettings.CaveDepthRange): the same distribution cut to the range, so its shape holds inside it. The
		/// form's own bounds still hold (a lava tube's 5–120 m): a range wholly past them takes the form's nearest end.
		/// </summary>
		public static float DepthFor(CaveForm form, int size, int seed, float rangeLeast, float rangeMost)
		{
			size = Mathf.Clamp(size, 0, 2);
			bool lava = form == CaveForm.LavaTube;
			float median = lava ? 14f + 9f * size : size == 0 ? 22f : size == 1 ? 45f : 80f;
			float shape = lava ? 1.8f : 1.6f + 0.05f * size;
			RangeOf(form, rangeLeast, rangeMost, out float least, out float most);
			// The distribution's own cumulative share at each end; the roll is spread between them (a truncated quantile).
			float Share(float x) => 1f / (1f + Mathf.Pow(median / x, shape));
			float p = Mathf.Lerp(Share(least), Share(most), Unit(unchecked((uint)seed), 0xDE97u));
			return Mathf.Clamp(median * Mathf.Pow(p / (1f - p), 1f / shape), least, most);
		}

		/// <summary>A form's depth bounds within a scene's range: the overlap of the two, or the form's end nearest a range past it.</summary>
		public static void RangeOf(CaveForm form, float rangeLeast, float rangeMost, out float least, out float most)
		{
			float formLeast = form == CaveForm.LavaTube ? 5f : LeastDepth, formMost = MaxDepthOf(form);
			if (rangeMost < rangeLeast)
			{
				(rangeLeast, rangeMost) = (rangeMost, rangeLeast);
			}
			least = Mathf.Clamp(rangeLeast, formLeast, formMost);
			most = Mathf.Clamp(rangeMost, formLeast, formMost);
		}

		/// <summary>The deepest a single line (<see cref="CaveCharacter.Classic"/>) is laid; deeper, the system is a graph of levels.</summary>
		private static float ClassicDeepest(CaveForm form) => form == CaveForm.LavaTube ? 20f : 45f;

		/// <summary>A deep system's character in place of a classic one: rolled again on its own salt until it is a graph.</summary>
		private static CaveCharacter DeepCharacter(CaveForm form, int size, int seed)
		{
			for (uint k = 1; k <= 8; k++)
			{
				CaveCharacter c = CharacterFor(form, size, Unit(unchecked((uint)seed), 0x2C1Bu + 0x101u * k));
				if (c != CaveCharacter.Classic)
				{
					return c;
				}
			}
			return CaveCharacter.Halls;
		}

		/// <summary>
		/// How many levels a system of a depth goes down through (2026-10-21): about one every 35–60 m (a lava tube's stacked
		/// tubes every 18–32 m), the spacing its seed's; up to eight (four stacked tubes).
		/// </summary>
		public static int LevelsForDepth(CaveForm form, float depth, int seed)
		{
			float u = Unit(unchecked((uint)seed), 0x1E7E1u);
			if (form == CaveForm.LavaTube)
			{
				return Mathf.Clamp(1 + Mathf.FloorToInt((depth - 8f) / Mathf.Lerp(18f, 32f, u)), 1, 4);
			}
			return Mathf.Clamp(1 + Mathf.FloorToInt((depth - 10f) / Mathf.Lerp(35f, 60f, u)), 1, 8);
		}

		/// <summary>
		/// Each level's floor (system frame, falling): the depth shared out evenly, each but the last moved by up to
		/// <see cref="LevelJitter"/> of the gap by its own hash of the seed, and none under <paramref name="lowest"/>.
		/// </summary>
		public static float[] LevelFloors(float depth, int levels, int seed, float lowest)
		{
			levels = Mathf.Max(1, levels);
			var floors = new float[levels];
			float gap = depth / levels;
			for (int i = 0; i < levels; i++)
			{
				float jitter = i < levels - 1 ? LevelJitter * (2f * Unit(unchecked((uint)seed), 0x1E7E2u + (uint)i * 0x9E37u) - 1f) : 0f;
				floors[i] = Mathf.Max(lowest, -gap * (i + 1 + jitter));
			}
			return floors;
		}

		/// <summary>
		/// How many times the passage of a two-level system (the most most characters had before depth was rolled) a system of
		/// <paramref name="levels"/> is given: half as much again for each level past two.
		/// </summary>
		private static float LevelBudget(int levels) => 1f + 0.5f * Mathf.Max(0, levels - 2);

		/// <summary>How many metres of passage a character's system is given, against the profile's trunk.</summary>
		private static float BudgetShare(CaveCharacter character)
		{
			switch (character)
			{
				case CaveCharacter.Warren: return 1.5f;
				case CaveCharacter.Rift: return 1.25f;
				case CaveCharacter.Abyss: return 1.2f;
				case CaveCharacter.Crystal: return 1.2f;
				default: return 1f;
			}
		}

		// ── Probing the field ─────────────────────────────────────

		/// <summary>The floor under a point in the void (marched down the noisy field from it), or NaN where the point is rock.</summary>
		internal float FloorBelow(Vector3 p, float reach = 12f, float step = 0.1f)
		{
			if (Void(p) >= 0f)
			{
				return float.NaN;
			}
			float y = p.y;
			while (Void(new Vector3(p.x, y - step, p.z)) < 0f)
			{
				y -= step;
				if (y < p.y - reach)
				{
					return float.NaN;
				}
			}
			return y;
		}

		/// <summary>The roof over a point in the void (the smooth field: the column it holds is cut from that), or NaN.</summary>
		internal float RoofAbove(Vector3 p, float reach = 80f)
		{
			if (Smooth(p) >= 0f)
			{
				return float.NaN;
			}
			float y = p.y;
			const float step = 0.25f;
			while (Smooth(new Vector3(p.x, y + step, p.z)) < 0f)
			{
				y += step;
				if (y > p.y + reach)
				{
					return float.NaN;
				}
			}
			return y;
		}

		/// <summary>
		/// Whether a climb fits the field: clear of the rock by a body's width along its length (on its climber's side), a
		/// wall behind a ladder, ground at its foot, and at its top a floor to step off onto with headroom over it.
		/// </summary>
		public bool ClimbFits(in CaveClimb climb)
		{
			Vector3 up = Vector3.up;
			float length = Vector3.Distance(climb.Bottom, climb.Top);
			if (length < 1.5f)
			{
				return false;
			}
			for (float d = 1f; d <= length - 0.3f; d += 0.5f)
			{
				Vector3 p = Vector3.Lerp(climb.Bottom, climb.Top, d / length);
				// The rung or the rope may lie against the rock (a ladder is leant on it, and neither collides); the climber
				// hanging out from it, 0.15 to 0.75 m along Out, must not.
				bool head = climb.Top.y - p.y < 1f;
				if (Void(p) > (head ? 0.2f : 0.05f) || Void(p + climb.Out * 0.45f) > -0.3f || Void(p + climb.Out * 0.45f + up * 1.2f) > -0.3f)
				{
					return false;
				}
			}
			// The landing a pace off the top: ground under it (the lip's rounding may let it down a little), air and headroom over it.
			Vector3 landing = climb.Top + climb.TopExit * 0.7f;
			if (Void(landing - up * 0.6f) < 0f || Void(landing + up * 0.3f) > -0.1f || Void(landing + up * 1.9f) > 0f)
			{
				return false;
			}
			return Void(climb.Bottom - up * 0.3f) >= 0f && Void(climb.Bottom + up * 0.5f) < -0.2f;
		}

		private sealed class Layout
		{
			private readonly CaveSystem system;
			private readonly Walker walker;
			private readonly Profile pf;
			private readonly DeterministicRNG rng;
			private readonly int siteId, size;
			private readonly float[] levelFloor;
			private readonly int levels;
			private readonly int maxChambers;
			/// <summary>How many halls each level still wants (by level).</summary>
			private readonly Dictionary<int, int> hallLevels = new Dictionary<int, int>();
			private readonly Queue<Hub> frontier = new Queue<Hub>();
			private readonly List<Hub> hubs = new List<Hub>();
			private float budget;

			private sealed class Hub
			{
				/// <summary>Its chambers (indices into the system's): one room, or a hall's lobes (the first its middle).</summary>
				public readonly List<int> Lobes = new List<int>();
				public int Level;
				/// <summary>The way into it, horizontal: what its branches leave away from.</summary>
				public Vector3 Entry;
				/// <summary>The heading of a chasm split across it (degrees), or NaN: its branches keep off the chasm's line.</summary>
				public float RiftYaw = float.NaN;
				public bool Hall;
				/// <summary>A landing partway down a chained descent (2026-10-21): a small room the next drop leaves from, no hub of a level's own.</summary>
				public bool Landing;
			}

			/// <summary>One drop of a descent: the floor it goes down to, the level of the hub at its foot, and whether that is a landing.</summary>
			private struct Leg
			{
				public float Floor;
				public int Level;
				public bool Landing;
			}

			/// <summary>
			/// The most one drop of a descent falls, metres (a chasm's climb, a pit's ledge or a chimney's rope): a level farther
			/// down is reached by drops chained through landings partway down.
			/// </summary>
			private const float LegDrop = 34f;

			/// <summary>The lists' lengths at a point, to take a feature back out whole when its climb does not fit.</summary>
			private struct Mark
			{
				public int Passages, Chambers, Solids, Rifts, Shafts, Crossings, Climbs, Hubs;
				public float Budget;
			}

			/// <param name="floors">Each level's floor, falling (<see cref="LevelFloors"/>): as many levels as it holds.</param>
			public Layout(CaveSystem system, Walker walker, Profile pf, DeterministicRNG rng, int siteId, int size, float[] floors, float budget)
			{
				this.system = system;
				this.walker = walker;
				this.pf = pf;
				this.rng = rng;
				this.siteId = siteId;
				this.size = size;
				this.levels = Mathf.Max(1, floors.Length);
				this.budget = budget;
				levelFloor = floors;
				// A deeper system more chambers, a few more for each level (2026-10-21), to a cap that keeps its rock in budget.
				maxChambers = Mathf.Min(MostChambers, (size >= 2 ? 34 : size == 1 ? 24 : 12) + Mathf.Max(0, this.levels - 2) * (size >= 2 ? 5 : size == 1 ? 4 : 3));
				// More halls, and more of them to a system (2026-10-18: "Caves need even bigger hallows"): a halls cave has two
				// on its deepest level at the largest size and most often one higher up too; an abyss, a crystal cave and a rift
				// one now and then.
				CaveCharacter c = system.Character;
				int last = this.levels - 1;
				if (size >= 1 && (c == CaveCharacter.Halls || c == CaveCharacter.Crystal && (size >= 2 || rng.NextFloat() < 0.5f)
					|| c == CaveCharacter.Abyss && rng.NextFloat() < 0.5f || c == CaveCharacter.Rift && rng.NextFloat() < 0.3f
					|| c == CaveCharacter.Classic && size >= 2 && rng.NextFloat() < 0.3f))
				{
					hallLevels[last] = c == CaveCharacter.Halls && size >= 2 ? 2 : 1;
				}
				if (c == CaveCharacter.Halls && this.levels >= 2 && rng.NextFloat() < (size >= 2 ? 0.75f : 0.4f))
				{
					hallLevels[0] = 1;
				}
				// A deep system (four levels or more) now and then has one more on a level between, on a stream of its own so the
				// draws above hold.
				if (this.levels >= 4 && size >= 1 && Unit(unchecked((uint)system.Seed), 0x4A11u) < (c == CaveCharacter.Halls ? 0.7f : 0.35f))
				{
					int middle = 1 + (int)(Unit(unchecked((uint)system.Seed), 0x4A12u) * (this.levels - 2));
					hallLevels[middle] = hallLevels.TryGetValue(middle, out int had) ? had + 1 : 1;
				}
			}

			/// <summary>The most chambers any system is given, however deep.</summary>
			private const int MostChambers = 64;

			private float Depth(int level) => -levelFloor[Mathf.Clamp(level, 0, levels - 1)];

			private Mark Save() => new Mark
			{
				Passages = system.Passages.Count, Chambers = system.Chambers.Count, Solids = system.Solids.Count, Rifts = system.Rifts.Count,
				Shafts = system.Shafts.Count, Crossings = system.Crossings.Count, Climbs = system.Climbs.Count, Hubs = hubs.Count, Budget = budget,
			};

			private void Restore(Mark m)
			{
				system.Passages.RemoveRange(m.Passages, system.Passages.Count - m.Passages);
				system.Chambers.RemoveRange(m.Chambers, system.Chambers.Count - m.Chambers);
				system.Solids.RemoveRange(m.Solids, system.Solids.Count - m.Solids);
				system.Rifts.RemoveRange(m.Rifts, system.Rifts.Count - m.Rifts);
				system.Shafts.RemoveRange(m.Shafts, system.Shafts.Count - m.Shafts);
				system.Crossings.RemoveRange(m.Crossings, system.Crossings.Count - m.Crossings);
				system.Climbs.RemoveRange(m.Climbs, system.Climbs.Count - m.Climbs);
				hubs.RemoveRange(m.Hubs, hubs.Count - m.Hubs);
				budget = m.Budget;
				system.PrepareField();
			}

			/// <summary>A walk with this layout's options, the walker's own put back after.</summary>
			private CavePassage Walk(CavePassageKind kind, CaveNode start, float heading, float length, float depth, float tall, float avoidFrom, float pitch = 0f)
			{
				walker.Tall = tall;
				walker.Avoid = true;
				walker.AvoidFrom = avoidFrom;
				walker.PitchLimit = pitch;
				start.Tall = tall;
				start.Centre.y = start.Floor + FloorDepth * start.Radius * pf.Flat * tall;
				CavePassage passage = walker.Walk(kind, start, heading, length, depth, 0f, 0f, false, start.Radius);
				walker.Tall = 1f;
				walker.Avoid = false;
				walker.AvoidFrom = 0f;
				walker.PitchLimit = 0f;
				return passage;
			}

			private void Add(CavePassage passage)
			{
				system.Passages.Add(passage);
				budget -= passage.Nodes[passage.Nodes.Count - 1].Along;
				system.PrepareField();
			}

			private static Vector3 Dir(float yaw)
			{
				float r = yaw * Mathf.Deg2Rad;
				return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
			}

			private static float Yaw(Vector3 dir) => Mathf.Repeat(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 360f);

			private static Vector3 Flat(Vector3 v)
			{
				v.y = 0f;
				return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
			}

			/// <summary>A passage's heading at its end, horizontal.</summary>
			private static Vector3 EndDir(CavePassage passage)
			{
				int n = passage.Nodes.Count;
				return Flat(passage.Nodes[n - 1].Centre - passage.Nodes[Mathf.Max(0, n - 3)].Centre);
			}

			/// <summary>How far a chamber reaches from its middle in a direction, at its floor.</summary>
			private static float ReachOf(in CaveChamber c, Vector3 dir)
			{
				float yr = c.Yaw * Mathf.Deg2Rad;
				float along = dir.x * Mathf.Sin(yr) + dir.z * Mathf.Cos(yr);
				float across = dir.x * Mathf.Cos(yr) - dir.z * Mathf.Sin(yr);
				float r = 1f / Mathf.Sqrt(along * along / (c.Radii.x * c.Radii.x) + across * across / (c.Radii.z * c.Radii.z) + 1e-9f);
				return r * Mathf.Sqrt(1f - 0.16f);
			}

			// ── The plan ───────────────────────────────────────────

			/// <summary>Lays everything past the trunk; false (with why) where not even the first hub fits.</summary>
			public bool Run(CavePassage trunk, out string problem)
			{
				problem = null;
				system.Passages.Add(trunk);
				budget -= trunk.Nodes[trunk.Nodes.Count - 1].Along;
				system.PrepareField();
				RoomsAlong(trunk);
				Hub first = HubAt(trunk, 0, final: levels == 1);
				if (first == null)
				{
					problem = "no chamber fits under the rock at the trunk's end";
					return false;
				}
				frontier.Enqueue(first);
				var descended = new bool[levels];
				// The way down first, level under level (2026-10-21), so the budget reaches the bottom of a deep system before the
				// branches on its upper levels spend it; each level's hub then branches in turn (the queue goes round the levels).
				Hub at = first;
				for (int level = 0; level < levels - 1 && budget > 12f && system.Chambers.Count < maxChambers; level++)
				{
					Hub down = Descend(at);
					if (down == null)
					{
						break;
					}
					descended[level] = true;
					frontier.Enqueue(down);
					at = down;
				}
				while (frontier.Count > 0 && budget > 12f && system.Chambers.Count < maxChambers)
				{
					Hub hub = frontier.Dequeue();
					if (hub.Level < levels - 1 && (!descended[hub.Level] || rng.NextFloat() < 0.2f))
					{
						Hub down = Descend(hub);
						if (down != null)
						{
							descended[hub.Level] = true;
							frontier.Enqueue(down);
						}
					}
					int exits = ExitCount();
					for (int e = 0; e < exits && budget > 12f && system.Chambers.Count < maxChambers; e++)
					{
						Hub next = Branch(hub);
						if (next != null)
						{
							frontier.Enqueue(next);
						}
					}
				}
				Loops();
				Columns();
				Cavern();
				return true;
			}

			private int ExitCount()
			{
				switch (system.Character)
				{
					case CaveCharacter.Warren: return rng.Range(2, 4);
					case CaveCharacter.Halls:
					case CaveCharacter.Crystal: return rng.Range(1, 4);
					default: return rng.Range(1, 3);
				}
			}

			/// <summary>Rooms along a passage (a mine's stopes), now and then, where the rock holds them.</summary>
			private void RoomsAlong(CavePassage passage)
			{
				CaveKind kind = system.Form == CaveForm.Mine ? CaveKind.Stope : CaveKind.Room;
				if (pf.RoomEveryMax >= 999f)
				{
					return;
				}
				float next = passage.Nodes[0].Along + rng.Range(pf.RoomEveryMin, pf.RoomEveryMax) * 0.8f;
				for (int i = 2; i < passage.Nodes.Count - 3; i++)
				{
					if (passage.Nodes[i].Along < next)
					{
						continue;
					}
					if (walker.TryChamber(passage, i, rng.Range(pf.RoomMin, pf.RoomMax), kind, siteId, out CaveChamber room))
					{
						system.Chambers.Add(room);
						next = passage.Nodes[i].Along + rng.Range(pf.RoomEveryMin, pf.RoomEveryMax) * 1.2f;
					}
					else
					{
						next = passage.Nodes[i].Along + 8f;
					}
				}
				system.PrepareField();
			}

			/// <summary>The hub at the foot of a descent's drop: the next level's, or a landing on the way to it.</summary>
			private Hub HubAt(CavePassage passage, Leg leg) => HubAt(passage, leg.Level, !leg.Landing && leg.Level == levels - 1, leg.Landing);

			/// <summary>
			/// The hub at a passage's end: a hall where one is wanted on this level and fits, else a room (a cavern at the bottom);
			/// a landing's a small room.
			/// </summary>
			private Hub HubAt(CavePassage passage, int level, bool final, bool landing = false)
			{
				Vector3 entry = EndDir(passage);
				if (!landing && hallLevels.TryGetValue(level, out int wanted) && wanted > 0)
				{
					Hub hall = Hall(passage, level, entry);
					if (hall != null)
					{
						hallLevels[level] = wanted - 1;
						return hall;
					}
				}
				bool small = system.Character == CaveCharacter.Warren;
				for (int attempt = 0; attempt < 4 && passage.Nodes.Count >= 3; attempt++)
				{
					float scale = final ? rng.Range(pf.CavernMin, pf.CavernMax) * (small ? 0.8f : 1f) : rng.Range(pf.RoomMin, pf.RoomMax) * (small ? 0.85f : landing ? 0.8f : 1.1f);
					if (walker.TryChamber(passage, passage.Nodes.Count - 1, scale, final ? CaveKind.Cavern : CaveKind.Room, siteId, out CaveChamber room))
					{
						room.Rough = system.Character == CaveCharacter.Rift ? 1.25f : 1f;
						system.Chambers.Add(room);
						system.PrepareField();
						var hub = new Hub { Level = level, Entry = entry, Landing = landing };
						hub.Lobes.Add(system.Chambers.Count - 1);
						hubs.Add(hub);
						return hub;
					}
					if (passage.Nodes.Count <= 5)
					{
						break;
					}
					passage.Nodes.RemoveAt(passage.Nodes.Count - 1);
				}
				system.PrepareField();
				return null;
			}

			// ── Halls ──────────────────────────────────────────────

			/// <summary>
			/// An enormous hall at a passage's end (up to ~250 m across, its vault up to ~90 m high, 2026-10-18; ~160 m before):
			/// a middle lobe entered at its edge, and lobes round it on terraces of their own, each joined to the middle by a
			/// ramp; null where the rock over it will not hold one. Where the rock is too thin for the vault its breadth would
			/// give it, the vault is let down to what the rock holds (no lower than <see cref="Walker.LowestVault"/> of its
			/// breadth) before the hall is made smaller: a broad, low hall rather than a small one.
			/// </summary>
			private Hub Hall(CavePassage passage, int level, Vector3 entry)
			{
				CaveNode end = passage.Nodes[passage.Nodes.Count - 1];
				bool lava = system.Form == CaveForm.LavaTube;
				float rx = size >= 2 ? rng.Range(60f, 125f) : rng.Range(36f, 70f);
				if (system.Character == CaveCharacter.Crystal)
				{
					rx *= 0.75f;
				}
				float across = rng.Range(0.6f, 0.9f), tall = lava ? rng.Range(0.3f, 0.4f) : rng.Range(0.45f, 0.65f);
				float yaw = Yaw(entry);
				CaveChamber middle = default;
				bool fits = false;
				for (int shrink = 0; shrink < 10 && !fits; shrink++, rx *= 0.86f)
				{
					if (rx < 14f)
					{
						break;
					}
					Vector3 c = end.Centre + entry * (0.62f * rx);
					float ry = Vault(c, end.Floor, yaw, rx, rx * across, rx * tall, MaxVault);
					var radii = new Vector3(rx, ry, rx * across);
					fits = walker.TryLobe(c, end.Floor, yaw, radii, 0f, CaveChamberKind.Hall, siteId, 1f, out middle, q => Walker.NearChain(passage, q));
				}
				if (!fits)
				{
					return null;
				}
				Mark mark = Save();
				system.Chambers.Add(middle);
				var hub = new Hub { Level = level, Entry = entry, Hall = true };
				hub.Lobes.Add(system.Chambers.Count - 1);
				hubs.Add(hub);
				system.PrepareField();
				int lobes = size >= 2 ? rng.Range(4, 9) : rng.Range(2, 6);
				for (int k = 0; k < lobes; k++)
				{
					float turn = rng.Range(-150f, 150f);
					Vector3 dir = Dir(yaw + turn);
					float share = rng.Range(0.4f, 0.72f);
					float lrx = middle.Radii.x * share, lrz = lrx * rng.Range(0.65f, 1f);
					float reach = ReachOf(middle, dir);
					Vector3 at = middle.Centre + dir * ((reach + lrx) * rng.Range(0.5f, 0.68f));
					// Terraced most of the time: a step up or down from the middle's floor, ramped.
					float rise = rng.NextFloat() < 0.65f ? rng.Range(1.5f, 7f) * (rng.NextFloat() < 0.5f ? -1f : 1f) : 0f;
					float lobeYaw = Yaw(dir) + rng.Range(-30f, 30f);
					var radii = new Vector3(lrx, Vault(at, middle.Floor + rise, lobeYaw, lrx, lrz, lrx * tall * rng.Range(0.85f, 1.2f), 0.8f * MaxVault), lrz);
					// It may open into the hall's other lobes at another floor (their terraces are ramped) and over its way in.
					bool Ours(Vector3 q) => Walker.NearChain(passage, q) || InHall(mark.Chambers, q);
					if (!walker.TryLobe(at, middle.Floor + rise, lobeYaw, radii, 0f, CaveChamberKind.Hall, siteId, 1f, out CaveChamber lobe, Ours)
						&& (rise == 0f || !walker.TryLobe(at, middle.Floor, lobeYaw, radii, 0f, CaveChamberKind.Hall, siteId, 1f, out lobe, Ours)))
					{
						continue;
					}
					Mark before = Save();
					system.Chambers.Add(lobe);
					system.PrepareField();
					if (Mathf.Abs(lobe.Floor - middle.Floor) >= 0.6f && !Ramp(middle, lobe))
					{
						Restore(before);
					}
				}
				if (hub.Lobes.Count == 1)
				{
					for (int i = mark.Chambers + 1; i < system.Chambers.Count; i++)
					{
						hub.Lobes.Add(i);
					}
				}
				return hub;
			}

			/// <summary>Whether a point lies within (a little past) one of a hall's lobes as they stand: the chambers from <paramref name="first"/> on.</summary>
			private bool InHall(int first, Vector3 p)
			{
				for (int i = first; i < system.Chambers.Count; i++)
				{
					CaveChamber c = system.Chambers[i];
					float yr = c.Yaw * Mathf.Deg2Rad;
					Vector3 o = p - c.Centre;
					float along = o.x * Mathf.Sin(yr) + o.z * Mathf.Cos(yr), across = o.x * Mathf.Cos(yr) - o.z * Mathf.Sin(yr);
					var e = new Vector3(along / (c.Radii.x + 3f), (o.y - c.Slope * along) / (c.Radii.y + 3f), across / (c.Radii.z + 3f));
					if (e.sqrMagnitude < 1f)
					{
						return true;
					}
				}
				return false;
			}

			/// <summary>The tallest a hall's vault stands over its floor (its half-height), metres.</summary>
			private const float MaxVault = 90f;

			/// <summary>
			/// A hall lobe's vault (half-height): as tall as its breadth would make it (<paramref name="want"/>, at least 8 m and
			/// at most <paramref name="most"/>), let down to what the rock over it holds but no lower than a share of its breadth.
			/// </summary>
			private float Vault(Vector3 centre, float floor, float yaw, float rx, float rz, float want, float most)
			{
				float ry = Mathf.Clamp(want, 8f, most);
				float holds = walker.LobeVault(centre, floor, yaw, rx, rz, 0f, 1f) - 0.01f;
				return Mathf.Min(ry, Mathf.Max(Mathf.Max(6f, Walker.LowestVault * rx), holds));
			}

			/// <summary>
			/// A ramp from the lower of two overlapping lobes up onto the higher's terrace, along the line between their middles:
			/// it climbs at a walk from the lower floor to the edge of the lower lobe's vault at the upper floor, and a pace on.
			/// </summary>
			private bool Ramp(in CaveChamber a, in CaveChamber b)
			{
				CaveChamber low = a.Floor <= b.Floor ? a : b, high = a.Floor <= b.Floor ? b : a;
				Vector3 u = Flat(high.Centre - low.Centre);
				float rise = high.Floor - low.Floor;
				// The lower lobe's section at the upper floor's height: where its void stops and the terrace begins.
				float h = (high.Floor - low.Centre.y) / low.Radii.y;
				float section = Mathf.Sqrt(Mathf.Max(0f, 1f - h * h));
				float edge = ReachOf(low, u) / Mathf.Sqrt(1f - 0.16f) * section;
				Vector3 lip = low.Centre + u * edge;
				float run = rise / Mathf.Tan(17f * Mathf.Deg2Rad);
				if (run > 0.85f * ReachOf(low, u) + edge * 0.1f)
				{
					return false;
				}
				var ramp = new CaveSolid
				{
					Kind = CaveSolidKind.Ramp,
					A = new Vector3(lip.x - u.x * run, low.Floor, lip.z - u.z * run),
					B = new Vector3(lip.x + u.x * 1.4f, high.Floor, lip.z + u.z * 1.4f),
					Width = rng.Range(1.6f, 2.6f),
				};
				system.Solids.Add(ramp);
				system.PrepareField();
				// Its top end must land on the terrace: rock under it, air over it.
				Vector3 top = ramp.B + u * 0.6f;
				float floor = system.FloorBelow(top + Vector3.up * 1.5f, 3f);
				return !float.IsNaN(floor) && Mathf.Abs(floor - high.Floor) < 0.5f;
			}

			/// <summary>Giant columns in the halls, clear of the ways across them.</summary>
			private void Columns()
			{
				foreach (Hub hub in hubs)
				{
					if (!hub.Hall)
					{
						continue;
					}
					int want = system.Character == CaveCharacter.Halls ? rng.Range(1, 3 + size) : rng.Range(0, 2);
					List<(Vector3 a, Vector3 b)> ways = WaysAcross(hub);
					for (int k = 0, tries = 0; k < want && tries < want * 6; tries++)
					{
						CaveChamber lobe = system.Chambers[hub.Lobes[rng.Range(0, hub.Lobes.Count)]];
						float a = rng.Range(0f, 2f * Mathf.PI), t = Mathf.Sqrt(rng.Range(0.1f, 0.5f));
						float yr = lobe.Yaw * Mathf.Deg2Rad;
						Vector3 along = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr)), side = new Vector3(along.z, 0f, -along.x);
						Vector3 p = lobe.Centre + along * (Mathf.Cos(a) * t * lobe.Radii.x) + side * (Mathf.Sin(a) * t * lobe.Radii.z);
						float radius = rng.Range(1.6f, Mathf.Clamp(0.1f * lobe.Radii.x, 2f, 5.5f));
						if (Near(ways, p, radius + 3.5f) || NearMiddle(hub, p, radius) || system.NearChasmOrClimb(new Vector3(p.x, lobe.Floor + 1f, p.z), radius * 2f + 2f))
						{
							continue;
						}
						float floor = system.FloorBelow(new Vector3(p.x, lobe.Floor + 1.5f, p.z), 4f);
						float roof = float.IsNaN(floor) ? float.NaN : system.RoofAbove(new Vector3(p.x, floor + 1f, p.z));
						if (float.IsNaN(roof) || roof - floor < 8f)
						{
							continue;
						}
						system.Solids.Add(new CaveSolid
						{
							Kind = CaveSolidKind.Column,
							A = new Vector3(p.x, floor, p.z),
							B = new Vector3(p.x, roof, p.z),
							Width = radius,
							Depth = rng.Range(0.5f, 1.3f),
						});
						k++;
					}
				}
				system.PrepareField();
			}

			/// <summary>The ways across a hub: between every pair of the passage ends that open into it.</summary>
			private List<(Vector3 a, Vector3 b)> WaysAcross(Hub hub)
			{
				var ends = new List<Vector3>();
				foreach (CavePassage p in system.Passages)
				{
					foreach (CaveNode n in new[] { p.Nodes[0], p.Nodes[p.Nodes.Count - 1] })
					{
						foreach (int i in hub.Lobes)
						{
							CaveChamber c = system.Chambers[i];
							Vector3 d = n.Centre - c.Centre;
							d.y = 0f;
							if (d.magnitude < Mathf.Max(c.Radii.x, c.Radii.z) + n.Radius + 2f && Mathf.Abs(n.Floor - c.Floor) < 8f)
							{
								ends.Add(new Vector3(n.Centre.x, n.Floor, n.Centre.z));
								break;
							}
						}
					}
				}
				foreach (CaveSolid s in system.Solids)
				{
					if (s.Kind == CaveSolidKind.Ramp || s.Kind == CaveSolidKind.Bridge)
					{
						ends.Add(s.A);
						ends.Add(s.B);
					}
				}
				var ways = new List<(Vector3, Vector3)>();
				Vector3 middle = system.Chambers[hub.Lobes[0]].Centre;
				foreach (Vector3 e in ends)
				{
					ways.Add((e, middle));
				}
				return ways;
			}

			/// <summary>Within a column's reach of a lobe's middle: where a site's camp or den stands, and its floor is measured.</summary>
			private bool NearMiddle(Hub hub, Vector3 p, float radius)
			{
				foreach (int i in hub.Lobes)
				{
					CaveChamber c = system.Chambers[i];
					float keep = radius + Mathf.Max(6f, 0.3f * Mathf.Min(c.Radii.x, c.Radii.z));
					if (new Vector2(p.x - c.Centre.x, p.z - c.Centre.z).sqrMagnitude < keep * keep)
					{
						return true;
					}
				}
				return false;
			}

			private static bool Near(List<(Vector3 a, Vector3 b)> ways, Vector3 p, float reach)
			{
				foreach ((Vector3 a, Vector3 b) in ways)
				{
					var ab = new Vector2(b.x - a.x, b.z - a.z);
					var ap = new Vector2(p.x - a.x, p.z - a.z);
					float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					if ((ap - ab * t).sqrMagnitude < reach * reach)
					{
						return true;
					}
				}
				return false;
			}

			// ── Branches ───────────────────────────────────────────

			/// <summary>Where a way may leave a hub: a point at the foot of one of its walls, its floor and heading.</summary>
			private bool Exit(Hub hub, out CaveNode start, out float heading, float radius)
			{
				start = default;
				heading = 0f;
				Vector3 middle = system.Chambers[hub.Lobes[0]].Centre;
				for (int attempt = 0; attempt < 8; attempt++)
				{
					int lobeIndex = hub.Lobes[rng.Range(0, hub.Lobes.Count)];
					CaveChamber lobe = system.Chambers[lobeIndex];
					// Away from the way in (and, in a hall, out from its middle), and off a chasm's line.
					Vector3 outward = hub.Hall && lobeIndex != hub.Lobes[0] ? Flat(lobe.Centre - middle) : hub.Entry;
					float yaw = Yaw(outward) + rng.Range(-110f, 110f);
					if (Vector3.Angle(Dir(yaw), -hub.Entry) < 55f)
					{
						continue;
					}
					if (!float.IsNaN(hub.RiftYaw) && Mathf.Abs(DeltaAngle(Mathf.Repeat(yaw - hub.RiftYaw, 180f), 0f)) < 35f)
					{
						continue;
					}
					Vector3 dir = Dir(yaw);
					Vector3 p = lobe.Centre + dir * (0.82f * ReachOf(lobe, dir));
					float floor = system.FloorBelow(new Vector3(p.x, FloorOf(lobe, p) + 1.2f, p.z), 3f);
					// Off a chasm and clear of a climb's foot and head: a way leaving there cut the ground a climb steps off onto.
					if (float.IsNaN(floor) || Mathf.Abs(floor - FloorOf(lobe, p)) > 0.35f || system.NearChasmOrClimb(new Vector3(p.x, floor + 1f, p.z), radius + 2f))
					{
						continue;
					}
					start = new CaveNode { Centre = new Vector3(p.x, floor, p.z), Radius = radius, Floor = floor, Along = 0f };
					heading = yaw;
					return true;
				}
				return false;
			}

			private float BranchRadius()
			{
				float r0 = system.Radius;
				switch (system.Character)
				{
					case CaveCharacter.Warren: return Mathf.Max(MinBore, r0 * rng.Range(0.5f, 0.75f));
					case CaveCharacter.Halls: return Mathf.Max(MinBore, r0 * rng.Range(0.8f, 1.1f));
					case CaveCharacter.Rift: return Mathf.Max(MinBore, r0 * rng.Range(0.45f, 0.7f));
					default: return Mathf.Max(MinBore, r0 * rng.Range(0.55f, 0.9f));
				}
			}

			private float BranchTall()
			{
				if (system.Character == CaveCharacter.Rift)
				{
					return rng.Range(1.7f, 2.7f);
				}
				return rng.NextFloat() < 0.15f ? rng.Range(1.3f, 1.8f) : 1f;
			}

			/// <summary>A branch off a hub on its level: to another hub most of the time, else an alcove or a narrowing end.</summary>
			private Hub Branch(Hub hub)
			{
				float br = BranchRadius();
				if (!Exit(hub, out CaveNode start, out float heading, br))
				{
					return null;
				}
				float length = Mathf.Min(budget, rng.Range(pf.BranchMin, pf.BranchMax) * (system.Character == CaveCharacter.Warren ? 0.7f : 1f));
				if (length < 12f)
				{
					return null;
				}
				CavePassage branch = Walk(CavePassageKind.Branch, start, heading, length, Depth(hub.Level), BranchTall(), br + 4f);
				if (branch.Nodes.Count < 4)
				{
					return null;
				}
				Add(branch);
				float roll = rng.NextFloat();
				if (roll < 0.7f)
				{
					Hub next = HubAt(branch, hub.Level, final: hub.Level == levels - 1 && rng.NextFloat() < 0.25f);
					if (next != null)
					{
						return next;
					}
				}
				else if (roll < 0.85f && walker.TryChamber(branch, branch.Nodes.Count - 1, rng.Range(1.4f, 2f), CaveKind.Alcove, siteId, out CaveChamber alcove))
				{
					system.Chambers.Add(alcove);
					system.PrepareField();
					return null;
				}
				Narrow(branch);
				return null;
			}

			/// <summary>A dead end: the last few nodes draw in and stop.</summary>
			private void Narrow(CavePassage passage)
			{
				int n = passage.Nodes.Count;
				for (int i = Mathf.Max(0, n - 3); i < n; i++)
				{
					CaveNode node = passage.Nodes[i];
					float t = (i - (n - 3)) / 2f;
					node.Radius = Mathf.Lerp(node.Radius, Mathf.Max(MinBore, 0.7f * node.Radius), t);
					// Level to its end (where segments of a narrowing bore meet at other floors, the union dips steeply between
					// them), and the bore drawn in round that floor: its middle lowered with it, or the floor narrows to a gutter.
					node.Floor = passage.Nodes[Mathf.Max(0, n - 3)].Floor;
					node.Centre.y = node.Floor + FloorDepth * node.Radius * pf.Flat * node.Height;
					passage.Nodes[i] = node;
				}
				system.PrepareField();
			}

			// ── Going down ─────────────────────────────────────────

			private enum Descent { Rift, Pit, Chimney, Steep }

			/// <summary>
			/// The way down from a hub to the level under it: one drop where it is no farther than <see cref="LegDrop"/>, else drops
			/// chained through landings partway down (2026-10-21: levels 35–60 m apart in caves up to 500 m deep). Where a later drop
			/// does not fit, the last landing is taken as the next level's way in (its branches walk on down to that level).
			/// </summary>
			private Hub Descend(Hub hub)
			{
				int next = hub.Level + 1;
				float from = system.Chambers[hub.Lobes[0]].Floor;
				float drop = from - levelFloor[next];
				int legs = drop > LegDrop ? Mathf.CeilToInt(drop / LegDrop) : 1;
				Hub at = hub;
				for (int k = 1; k <= legs; k++)
				{
					bool last = k == legs;
					var leg = new Leg { Floor = last ? levelFloor[next] : Mathf.Lerp(from, levelFloor[next], k / (float)legs), Level = last ? next : hub.Level, Landing = !last };
					Hub down = DescendLeg(at, leg);
					if (down == null)
					{
						if (at == hub)
						{
							return null;
						}
						at.Level = next;
						at.Landing = false;
						return at;
					}
					at = down;
				}
				return at;
			}

			/// <summary>One drop down from a hub, by what suits the character, falling back to a steep passage.</summary>
			private Hub DescendLeg(Hub hub, Leg leg)
			{
				var order = new List<(Descent kind, float weight)>();
				switch (system.Character)
				{
					case CaveCharacter.Rift: order.Add((Descent.Rift, 0.6f)); order.Add((Descent.Chimney, 0.25f)); order.Add((Descent.Pit, 0.1f)); break;
					case CaveCharacter.Abyss: order.Add((Descent.Pit, 0.5f)); order.Add((Descent.Rift, 0.35f)); order.Add((Descent.Chimney, 0.15f)); break;
					case CaveCharacter.Halls: order.Add((Descent.Rift, 0.35f)); order.Add((Descent.Pit, 0.2f)); order.Add((Descent.Chimney, 0.1f)); break;
					case CaveCharacter.Warren: order.Add((Descent.Chimney, 0.35f)); order.Add((Descent.Rift, 0.15f)); break;
					case CaveCharacter.Crystal: order.Add((Descent.Rift, 0.3f)); order.Add((Descent.Pit, 0.2f)); order.Add((Descent.Chimney, 0.15f)); break;
					default: order.Add((Descent.Rift, 0.2f)); order.Add((Descent.Chimney, 0.15f)); break;
				}
				// Weighted, without repeats: each kind keyed once by a roll raised to its weight's inverse, highest first.
				var keyed = new List<(Descent kind, float key)>();
				foreach ((Descent kind, float weight) in order)
				{
					keyed.Add((kind, Mathf.Pow(Mathf.Max(1e-6f, rng.NextFloat()), 1f / weight)));
				}
				keyed.Sort((x, y) => y.key.CompareTo(x.key));
				keyed.Add((Descent.Steep, 0f));
				foreach ((Descent kind, float _) in keyed)
				{
					Mark mark = Save();
					Hub down = kind == Descent.Rift ? RiftDown(hub, leg) : kind == Descent.Pit ? PitDown(hub, leg) : kind == Descent.Chimney ? ChimneyDown(hub, leg) : SteepDown(hub, leg);
					if (down != null)
					{
						return down;
					}
					Restore(mark);
				}
				return null;
			}

			/// <summary>How far the way on from a drop's foot runs: a short way to a landing, a branch's length to a level's hub.</summary>
			private float OnLength(Leg leg) => leg.Landing ? rng.Range(14f, 28f) : rng.Range(pf.BranchMin, pf.BranchMax);

			/// <summary>
			/// A steep passage down to a drop's floor, and the hub at its foot: at a walk's full pitch whatever the form's own (a
			/// lava tube's nearly level profile would need a passage kilometres long).
			/// </summary>
			private Hub SteepDown(Hub hub, Leg leg)
			{
				float br = BranchRadius();
				if (!Exit(hub, out CaveNode start, out float heading, br))
				{
					return null;
				}
				float pitch = Mathf.Max(pf.Pitch, WalkPitch);
				float drop = start.Floor - leg.Floor;
				float length = Mathf.Max(rng.Range(pf.BranchMin, pf.BranchMax), drop / Mathf.Tan((pitch - 4f) * Mathf.Deg2Rad) * 1.25f);
				CavePassage way = Walk(CavePassageKind.Branch, start, heading, length, -leg.Floor, 1f, br + 4f, pitch);
				if (way.Nodes.Count < 4 || start.Floor - way.Nodes[way.Nodes.Count - 1].Floor < 0.6f * drop)
				{
					return null;
				}
				Add(way);
				return HubAt(way, leg);
			}

			/// <summary>
			/// A chasm split across a hub's floor, square to the way in, down to the next level: a bridge over it (left standing
			/// in the rock, now and then planks on ropes), a climb down into it beside the bridge, and a passage on from one of
			/// its ends at its floor.
			/// </summary>
			private Hub RiftDown(Hub hub, Leg leg)
			{
				// The hub's room, or the hall lobe farthest out.
				int index = hub.Lobes[0];
				if (hub.Hall && hub.Lobes.Count > 1)
				{
					index = hub.Lobes[rng.Range(1, hub.Lobes.Count)];
				}
				CaveChamber room = system.Chambers[index];
				float small = Mathf.Min(room.Radii.x, room.Radii.z);
				if (small < 6f || !float.IsNaN(hub.RiftYaw))
				{
					return null;
				}
				Vector3 e = hub.Hall && index != hub.Lobes[0] ? Flat(room.Centre - system.Chambers[hub.Lobes[0]].Centre) : hub.Entry;
				Vector3 axis = new Vector3(e.z, 0f, -e.x);
				Vector3 m = room.Centre + e * (0.15f * ReachOf(room, e));
				float top = FloorOf(room, m);
				float width = Mathf.Min(rng.Range(1.6f, 3.6f), 0.5f * ReachOf(room, e) - 2f);
				if (width < 1.4f)
				{
					return null;
				}
				float bottom = Mathf.Clamp(leg.Floor, top - 40f, top - 10f);
				if (bottom < system.LowestFloor - system.Origin.y + 0.5f)
				{
					return null;
				}
				float half = ReachOf(room, axis) / Mathf.Sqrt(1f - 0.16f) + 10f;
				var rift = new CaveRift
				{
					A = new Vector3(m.x - axis.x * half, bottom, m.z - axis.z * half),
					B = new Vector3(m.x + axis.x * half, bottom, m.z + axis.z * half),
					Top = top + 1.5f,
					Width = width,
					Narrow = width * rng.Range(0.6f, 0.9f),
				};
				// Nothing else may lie in its way under the floor it splits (sampled clear of that floor's own reach).
				for (float s = -half; s <= half; s += 2f)
				{
					for (float y = bottom + 1f; y < top - width - 2f; y += 2.5f)
					{
						if (system.Smooth(new Vector3(m.x + axis.x * s, y, m.z + axis.z * s)) < width + 1f)
						{
							return null;
						}
					}
				}
				system.Rifts.Add(rift);
				// The crossing: rock left standing, or now and then planks on ropes.
				float reach = width + 2.2f;
				Vector3 a = m - e * reach, b = m + e * reach;
				a.y = FloorOf(room, a);
				b.y = FloorOf(room, b);
				bool rope = rng.NextFloat() < (system.Character == CaveCharacter.Rift ? 0.35f : 0.2f);
				if (rope)
				{
					system.Crossings.Add(new CaveCrossing { Kind = CaveCrossingKind.Rope, A = a, B = b, HalfWidth = 0.8f });
				}
				else
				{
					system.Solids.Add(new CaveSolid
					{
						Kind = CaveSolidKind.Bridge,
						A = a,
						B = b,
						Width = rng.Range(1.3f, 2.2f),
						Depth = rng.Range(1.4f, 2.4f),
						Arch = rng.Range(1.5f, 4f),
					});
				}
				system.PrepareField();
				// On from one of its ends at its floor, to the next level's hub.
				float sign = rng.NextFloat() < 0.5f ? -1f : 1f;
				float br = Mathf.Max(MinBore, Mathf.Min(BranchRadius(), rift.Narrow + 0.6f));
				Vector3 from = m + axis * (sign * (half - 3f));
				var start = new CaveNode { Centre = new Vector3(from.x, bottom, from.z), Radius = br, Floor = bottom, Along = 0f };
				// Clear of the chasm it starts in before it keeps off chasms.
				CavePassage on = Walk(CavePassageKind.Branch, start, Yaw(axis * sign), OnLength(leg), -leg.Floor, 1f, br + width + 10f);
				if (on.Nodes.Count < 4)
				{
					return null;
				}
				Add(on);
				Hub down = HubAt(on, leg);
				if (down == null || !RiftClimb(m, e, axis, top, bottom, width, rope ? 1.2f : system.Solids[system.Solids.Count - 1].Width))
				{
					return null;
				}
				hub.RiftYaw = Yaw(axis);
				return down;
			}

			/// <summary>
			/// Lays a climb: its wall stilled along it (<see cref="Quiet"/>), its top brought to the floor found a pace off it,
			/// then checked against the field as it now is. False, and nothing kept, where it does not fit.
			/// </summary>
			private bool AddClimb(CaveClimb climb)
			{
				system.Climbs.Add(climb);
				system.PrepareField();
				float landing = system.FloorBelow(climb.Top + climb.TopExit * 0.6f + Vector3.up * 1.2f, 2.5f);
				if (!float.IsNaN(landing) && Mathf.Abs(landing - climb.Top.y) < 0.8f)
				{
					climb.Top.y = landing;
					system.Climbs[system.Climbs.Count - 1] = climb;
					system.PrepareField();
					if (system.ClimbFits(climb))
					{
						return true;
					}
				}
				system.Climbs.RemoveAt(system.Climbs.Count - 1);
				system.PrepareField();
				return false;
			}

			/// <summary>A climb down a chasm's wall beside its crossing, on whichever side and at whichever spot the rock lets it fit.</summary>
			private bool RiftClimb(Vector3 m, Vector3 e, Vector3 axis, float top, float bottom, float width, float crossing)
			{
				foreach (float along in new[] { crossing + 2.5f, -(crossing + 2.5f), crossing + 5f, -(crossing + 5f), crossing + 8f, -(crossing + 8f) })
				{
					foreach (float side in new[] { 1f, -1f })
					{
						Vector3 line = m + axis * along;
						Vector3 wallward = e * side;
						// The floor at the lip here (a sloping room's, or a lower terrace's, is not the middle's).
						Vector3 rim = line + wallward * (width + 1.2f);
						float lipFloor = system.FloorBelow(new Vector3(rim.x, top + 2.5f, rim.z), 5f);
						if (float.IsNaN(lipFloor) || lipFloor - bottom < 4f)
						{
							continue;
						}
						// The smooth wall's reach toward the line at each height (its noise is stilled along a climb).
						var walls = new List<(float y, float x)>();
						float clear = float.MaxValue;
						for (float y = bottom + 0.6f; y < lipFloor - 0.3f; y += 1f)
						{
							float x = WallDistance(new Vector3(line.x, y, line.z), wallward, width + 3f);
							walls.Add((y, x));
							clear = Mathf.Min(clear, x);
						}
						float lip = WallDistance(new Vector3(line.x, lipFloor - 0.4f, line.z), wallward, width + 3f);
						if (clear == float.MaxValue || lip == float.MaxValue || clear < 0.9f)
						{
							continue;
						}
						// Straight down from the lip where the wall under it stands back; else a ladder leant on the lip, its
						// foot as far out as keeps it clear of the wall at every height below.
						bool hangs = clear >= lip - 0.2f;
						float headX = hangs ? lip - 0.45f : lip - 0.05f;
						float footX = hangs ? lip - 0.45f : clear - 0.45f;
						if (!hangs)
						{
							foreach ((float y, float x) in walls)
							{
								float t = (y - bottom) / Mathf.Max(0.5f, lipFloor - bottom);
								if (t < 0.95f)
								{
									// Clear of the wall by a hand's breadth at its foot, closing to the lip it rests on.
									footX = Mathf.Min(footX, (x - 0.15f * (1f - t) - headX * t) / (1f - t));
								}
							}
							if (footX < 0.3f)
							{
								continue;
							}
						}
						Vector3 foot = line + wallward * footX;
						Vector3 head = line + wallward * headX;
						float lean = new Vector2(head.x - foot.x, head.z - foot.z).magnitude / Mathf.Max(1f, lipFloor - bottom);
						float footFloor = system.FloorBelow(new Vector3(foot.x, bottom + 1.2f, foot.z), 3f);
						if (float.IsNaN(footFloor) || lean > 0.22f)
						{
							continue;
						}
						foot.y = footFloor;
						head.y = lipFloor;
						var climb = new CaveClimb
						{
							Kind = hangs ? CaveClimbKind.Rope : CaveClimbKind.Ladder,
							Bottom = foot,
							Top = head,
							Out = -wallward,
							TopExit = wallward,
						};
						if (AddClimb(climb))
						{
							return true;
						}
					}
				}
				return false;
			}

			/// <summary>How far from a point in the void the smooth wall is along a direction (marched), or float.MaxValue within reach.</summary>
			private float WallDistance(Vector3 p, Vector3 dir, float reach)
			{
				if (system.Smooth(p) >= 0f)
				{
					return float.MaxValue;
				}
				for (float d = 0.15f; d <= reach; d += 0.15f)
				{
					if (system.Smooth(p + dir * d) >= 0f)
					{
						return d;
					}
				}
				return float.MaxValue;
			}

			/// <summary>A short way from a hub to the lip of a shaft dug or fallen in ahead of it; its end, or null.</summary>
			private CavePassage Approach(Hub hub, out float heading)
			{
				float br = BranchRadius();
				heading = 0f;
				if (!Exit(hub, out CaveNode start, out heading, br))
				{
					return null;
				}
				// Level from a landing (its floor lies under its level's): on toward the lip, not back up.
				float depth = hub.Landing ? -system.Chambers[hub.Lobes[0]].Floor : Depth(hub.Level);
				CavePassage way = Walk(CavePassageKind.Branch, start, heading, rng.Range(10f, 26f), depth, 1f, br + 4f);
				if (way.Nodes.Count < 3)
				{
					return null;
				}
				Add(way);
				return way;
			}

			/// <summary>Whether a shaft's volume is all rock now (but where its way in reaches it), and roofed by enough of it.</summary>
			private bool ShaftClear(Vector3 centre, float radius, float bottom, float top, Vector3 entry, float entryRadius)
			{
				float amp = walker.Amp;
				for (int k = 0; k <= 8; k++)
				{
					Vector3 p = k == 8 ? centre : centre + Dir(k * 45f) * radius;
					if (walker.Ground(new Vector3(p.x, top, p.z)) - (top + amp) < MinCover || !walker.Inside(p, radius + 12f))
					{
						return false;
					}
				}
				for (float y = bottom + 0.8f; y < top; y += 2.5f)
				{
					for (int k = 0; k <= 8; k++)
					{
						Vector3 p = new Vector3(centre.x, y, centre.z) + (k == 8 ? Vector3.zero : Dir(k * 45f) * radius * 0.9f);
						var flat = new Vector2(p.x - entry.x, p.z - entry.z);
						if (flat.magnitude < entryRadius + 2f && y > entry.y - 1f)
						{
							continue;
						}
						if (system.Smooth(p) < 0.5f)
						{
							return false;
						}
					}
				}
				return true;
			}

			/// <summary>
			/// A great pit ahead of a short way from the hub, wound round by a ledge from the way's floor down to the pit's,
			/// and a passage on from the ledge's foot to the next level's hub.
			/// </summary>
			private Hub PitDown(Hub hub, Leg leg)
			{
				CavePassage way = Approach(hub, out _);
				if (way == null)
				{
					return null;
				}
				CaveNode end = way.Nodes[way.Nodes.Count - 1];
				Vector3 dir = EndDir(way);
				float ledge = rng.Range(2.2f, 3f);
				float pitch = rng.Range(1.1f, 1.8f);
				float radius = Mathf.Clamp(pitch / LedgeGrade + ledge + rng.Range(0.5f, 3f), 7f, size >= 2 ? 15f : 11f);
				// The ledge starts level with the floor the way actually has at its end (2026-10-20): the smooth unions of its
				// segments and the chamber it leaves let that floor down as much as a metre under the nodes' own, and a ledge
				// started at the nodes' floor stood a metre-high step (52°) over the way's end.
				float found = system.FloorBelow(new Vector3(end.Centre.x, end.Floor + 1.2f, end.Centre.z), 3f);
				float floor = float.IsNaN(found) ? end.Floor : found;
				float bottom = Mathf.Clamp(leg.Floor, floor - 45f, floor - 10f);
				if (bottom < system.LowestFloor - system.Origin.y + 0.5f)
				{
					return null;
				}
				// The way ends on the pit's rim, the ledge starting off it at the wall under its mouth.
				Vector3 centre = new Vector3(end.Centre.x, 0f, end.Centre.z) + dir * (radius + 0.3f);
				float top = floor + Mathf.Max(5f, 0.6f * radius);
				if (!ShaftClear(centre, radius, bottom, top, new Vector3(end.Centre.x, floor, end.Centre.z), end.Radius))
				{
					return null;
				}
				float hand = rng.NextFloat() < 0.5f ? -1f : 1f;
				float start = Yaw(-dir);
				system.Shafts.Add(new CaveShaft { Bottom = new Vector3(centre.x, bottom, centre.z), Top = top, Radius = radius });
				system.Solids.Add(new CaveSolid
				{
					Kind = CaveSolidKind.Spiral,
					A = new Vector3(centre.x, bottom, centre.z),
					B = new Vector3(centre.x, floor, centre.z),
					Width = ledge,
					Depth = radius,
					Arch = hand * pitch,
					Angle = start,
				});
				system.PrepareField();
				// The ledge's foot: where it has wound down to the pit's floor.
				float turned = (floor - bottom) / pitch * Mathf.Rad2Deg;
				float footYaw = start + hand * turned;
				Vector3 outward = Dir(footYaw);
				float br = BranchRadius();
				Vector3 from = centre + outward * (radius - 0.5f);
				var exit = new CaveNode { Centre = new Vector3(from.x, bottom, from.z), Radius = br, Floor = bottom, Along = 0f };
				CavePassage on = Walk(CavePassageKind.Branch, exit, footYaw, OnLength(leg), -leg.Floor, 1f, radius + 4f);
				if (on.Nodes.Count < 4)
				{
					return null;
				}
				Add(on);
				// Off the ledge's foot onto the way on, walked (2026-10-21): the smooth union of the pit's floor and the way's first
				// segment let the floor down half a metre under the ledge's end, a 47° step; such a pit is taken back out.
				float line = radius - 0.5f * ledge, last = (floor - bottom) / pitch;
				var walk = new List<Vector3>();
				foreach (float back in new[] { 2f, 1f, 0f })
				{
					float phi = Mathf.Max(0f, last - back / line);
					float yaw = start * Mathf.Deg2Rad + hand * phi;
					walk.Add(new Vector3(centre.x + Mathf.Sin(yaw) * line, floor - phi * pitch + 0.3f, centre.z + Mathf.Cos(yaw) * line));
				}
				for (int i = 0; i < Mathf.Min(4, on.Nodes.Count); i++)
				{
					walk.Add(new Vector3(on.Nodes[i].Centre.x, on.Nodes[i].Floor + 0.3f, on.Nodes[i].Centre.z));
				}
				if (!system.WalkFits(walk, out _))
				{
					return null;
				}
				return HubAt(on, leg);
			}

			/// <summary>
			/// A narrow chimney ahead of a short way from the hub, a rope (a ladder in a mine) down its wall under the way's
			/// mouth, and a passage on from its foot to the next level's hub.
			/// </summary>
			private Hub ChimneyDown(Hub hub, Leg leg)
			{
				CavePassage way = Approach(hub, out _);
				if (way == null)
				{
					return null;
				}
				return ShaftDown(way, leg, Mathf.Clamp(leg.Floor, way.Nodes[way.Nodes.Count - 1].Floor - 35f, way.Nodes[way.Nodes.Count - 1].Floor - 8f),
					system.Form == CaveForm.Mine);
			}

			/// <summary>A shaft at a way's end down to <paramref name="bottom"/>, its climb, a way on from its foot and the hub there (<paramref name="leg"/>'s).</summary>
			private Hub ShaftDown(CavePassage way, Leg leg, float bottom, bool mine)
			{
				CaveNode end = way.Nodes[way.Nodes.Count - 1];
				Vector3 dir = EndDir(way);
				float floor = end.Floor;
				if (bottom < system.LowestFloor - system.Origin.y + 0.5f)
				{
					return null;
				}
				// Wide enough that the walls' roughness leaves a climber room.
				float radius = mine ? rng.Range(1.6f, 1.9f) : Mathf.Max(1.8f, walker.Amp + 1.5f) * rng.Range(1f, 1.2f);
				Vector3 centre = new Vector3(end.Centre.x, 0f, end.Centre.z) + dir * (radius + 0.3f);
				float top = floor + 2f * end.Radius * Mathf.Max(0.8f, pf.Flat) + 0.6f;
				if (!ShaftClear(centre, radius, bottom, top, new Vector3(end.Centre.x, floor, end.Centre.z), end.Radius))
				{
					return null;
				}
				system.Shafts.Add(new CaveShaft { Bottom = new Vector3(centre.x, bottom, centre.z), Top = top, Radius = radius, Square = mine, Yaw = Yaw(dir) });
				system.PrepareField();
				// The climb hangs down the wall under the way's mouth; at its top it steps off back into the way.
				var climb = new CaveClimb
				{
					Kind = mine ? CaveClimbKind.Ladder : rng.NextFloat() < 0.5f ? CaveClimbKind.Rope : CaveClimbKind.RopeLadder,
					Bottom = centre - dir * (radius - 0.4f),
					Top = centre - dir * (radius - 0.4f),
					Out = dir,
					TopExit = -dir,
				};
				climb.Bottom.y = bottom;
				climb.Top.y = floor;
				if (!AddClimb(climb))
				{
					return null;
				}
				float br = mine ? system.Radius : BranchRadius();
				Vector3 from = centre + dir * (radius - 0.3f);
				var exit = new CaveNode { Centre = new Vector3(from.x, bottom, from.z), Radius = br, Floor = bottom, Along = 0f };
				float heading = Yaw(dir) + (mine ? (rng.NextFloat() < 0.5f ? 0f : rng.NextFloat() < 0.5f ? 90f : -90f) : rng.Range(-40f, 40f));
				float length = mine ? rng.Range(40f, 90f) : OnLength(leg);
				CavePassage on = Walk(CavePassageKind.Branch, exit, heading, length, -bottom, 1f, radius + 4f);
				if (on.Nodes.Count < 4)
				{
					return null;
				}
				Add(on);
				if (mine)
				{
					var hub = new Hub { Level = leg.Level, Entry = EndDir(on) };
					hubs.Add(hub);
					// A mine's level is its drift: the stopes along it are its rooms.
					RoomsAlong(on);
					if (walker.TryChamber(on, on.Nodes.Count - 1, rng.Range(pf.RoomMin, pf.RoomMax), CaveKind.Stope, siteId, out CaveChamber stope))
					{
						system.Chambers.Add(stope);
						system.PrepareField();
						hub.Lobes.Add(system.Chambers.Count - 1);
					}
					return hub;
				}
				return HubAt(on, leg);
			}

			// ── Loops ──────────────────────────────────────────────

			/// <summary>Hubs far apart on one level joined again by a way of their own, so the system is not a tree.</summary>
			private void Loops()
			{
				int want;
				switch (system.Character)
				{
					case CaveCharacter.Warren: want = rng.Range(2, 5); break;
					case CaveCharacter.Crystal:
					case CaveCharacter.Rift: want = rng.Range(1, 3); break;
					default: want = rng.Range(0, 2); break;
				}
				for (int k = 0, tries = 0; k < want && tries < want * 6 && hubs.Count >= 2; tries++)
				{
					Hub a = hubs[rng.Range(0, hubs.Count)], b = hubs[rng.Range(0, hubs.Count)];
					if (a == b || a.Level != b.Level || a.Landing || b.Landing || a.Lobes.Count == 0 || b.Lobes.Count == 0)
					{
						continue;
					}
					CaveChamber from = system.Chambers[a.Lobes[0]], to = system.Chambers[b.Lobes[0]];
					Vector3 apart = to.Centre - from.Centre;
					apart.y = 0f;
					float gap = apart.magnitude - Mathf.Max(from.Radii.x, from.Radii.z) - Mathf.Max(to.Radii.x, to.Radii.z);
					if (gap < 15f || gap > 140f || Mathf.Abs(to.Floor - from.Floor) > 0.3f * apart.magnitude)
					{
						continue;
					}
					if (system.LinkWithin(from, to, 220f, rng, walker.Inside, walker.Ground))
					{
						budget -= gap;
						k++;
					}
				}
			}

			/// <summary>The site's chamber: the middle of its deepest hall, else its deepest level's greatest room.</summary>
			private void Cavern()
			{
				int best = -1;
				float score = float.MinValue;
				for (int i = 0; i < system.Chambers.Count; i++)
				{
					CaveChamber c = system.Chambers[i];
					if (c.Kind == CaveChamberKind.Alcove)
					{
						continue;
					}
					bool middleOfHall = false;
					foreach (Hub h in hubs)
					{
						middleOfHall |= h.Hall && h.Lobes.Count > 0 && h.Lobes[0] == i;
					}
					float s = c.Radii.x * c.Radii.z * (middleOfHall ? 4f : 1f) - 40f * c.Floor;
					if (s > score)
					{
						score = s;
						best = i;
					}
				}
				for (int i = 0; i < system.Chambers.Count; i++)
				{
					CaveChamber c = system.Chambers[i];
					if (i == best)
					{
						c.Kind = CaveChamberKind.Cavern;
						c.Radii.y = Mathf.Max(6f, c.Radii.y);
					}
					else if (c.Kind == CaveChamberKind.Cavern)
					{
						c.Kind = CaveChamberKind.Room;
					}
					system.Chambers[i] = c;
				}
				system.PrepareField();
			}

			// ── Mines ──────────────────────────────────────────────

			/// <summary>
			/// A mine's deeper levels under its adit: from the end of a drift (or a short crosscut off the adit), a ladder shaft or
			/// an incline down to a new drift with its stopes and side drifts; and in some deep mines, where the last level
			/// broke into the rock's own hollows, a natural cavern split by a chasm with a trestle over it.
			/// </summary>
			public void Levels(CavePassage adit)
			{
				int extra = size <= 0 ? 0 : size == 1 ? 1 : rng.NextFloat() < 0.7f ? 2 : 1;
				CavePassage level = adit;
				for (int k = 0; k < extra; k++)
				{
					Mark mark = Save();
					CavePassage next = MineLevel(level, k);
					if (next == null)
					{
						Restore(mark);
						break;
					}
					level = next;
				}
				if (size >= 1 && rng.NextFloat() < 0.4f)
				{
					Mark mark = Save();
					if (!BrokeThrough(level))
					{
						Restore(mark);
					}
				}
				system.PrepareField();
			}

			/// <summary>One level down from a drift: a crosscut to a shaft or an incline, and the new drift; null where none fits.</summary>
			private CavePassage MineLevel(CavePassage from, int index)
			{
				// A crosscut at right angles off a node a little short of the drift's end.
				int at = Mathf.Clamp(from.Nodes.Count - 1 - rng.Range(2, 6), 1, from.Nodes.Count - 1);
				CaveNode node = from.Nodes[at];
				Vector3 along = Flat(from.Nodes[Mathf.Min(at + 1, from.Nodes.Count - 1)].Centre - from.Nodes[at - 1].Centre);
				float side = rng.NextFloat() < 0.5f ? -1f : 1f;
				float heading = Yaw(along) + 90f * side;
				Vector3 dir = Dir(heading);
				float r = system.Radius;
				var start = new CaveNode { Centre = node.Centre + dir * (0.6f * node.Radius), Radius = r, Floor = node.Floor, Along = 0f };
				float drop = rng.Range(12f, 22f);
				bool incline = rng.NextFloat() < 0.4f;
				if (incline)
				{
					// Down at a steady walkable grade, then on as the level's drift.
					float run = drop / Mathf.Tan(19f * Mathf.Deg2Rad);
					CavePassage winze = Walk(CavePassageKind.Branch, start, heading, run + rng.Range(40f, 80f), -(node.Floor - drop), 1f, r + 4f, 20f);
					if (winze.Nodes.Count < 6 || node.Floor - winze.Nodes[winze.Nodes.Count - 1].Floor < 0.7f * drop)
					{
						return null;
					}
					Add(winze);
					RoomsAlong(winze);
					return winze;
				}
				CavePassage crosscut = Walk(CavePassageKind.Branch, start, heading, rng.Range(8f, 18f), -node.Floor, 1f, r + 4f);
				if (crosscut.Nodes.Count < 3)
				{
					return null;
				}
				Add(crosscut);
				int before = system.Passages.Count;
				float shaftFoot = crosscut.Nodes[crosscut.Nodes.Count - 1].Floor - drop;
				Hub down = ShaftDown(crosscut, new Leg { Floor = shaftFoot, Level = index + 1 }, shaftFoot, mine: true);
				if (down == null || system.Passages.Count == before)
				{
					return null;
				}
				CavePassage drift = system.Passages[system.Passages.Count - 1];
				// A side drift or two off the new level, each to a stope.
				int sides = rng.Range(0, 3);
				for (int s = 0; s < sides; s++)
				{
					int n = rng.Range(2, Mathf.Max(3, drift.Nodes.Count - 2));
					CaveNode off = drift.Nodes[n];
					Vector3 d = Flat(drift.Nodes[Mathf.Min(n + 1, drift.Nodes.Count - 1)].Centre - drift.Nodes[n - 1].Centre);
					float h = Yaw(d) + (rng.NextFloat() < 0.5f ? 90f : -90f);
					var branchStart = new CaveNode { Centre = off.Centre + Dir(h) * (0.6f * off.Radius), Radius = r, Floor = off.Floor, Along = 0f };
					CavePassage spur = Walk(CavePassageKind.Branch, branchStart, h, rng.Range(pf.BranchMin, pf.BranchMax), -off.Floor, 1f, r + 4f);
					if (spur.Nodes.Count < 4)
					{
						continue;
					}
					Add(spur);
					if (walker.TryChamber(spur, spur.Nodes.Count - 1, rng.Range(1.4f, 2f), CaveKind.Stope, siteId, out CaveChamber stope))
					{
						system.Chambers.Add(stope);
						system.PrepareField();
					}
				}
				return drift;
			}

			/// <summary>
			/// The last drift broken through into a natural cavern (as rough as a cave's, though the mine is smooth-cut), a chasm
			/// split across it with a trestle (or planks on ropes) over it and a rope down, and its far lobe beyond.
			/// </summary>
			private bool BrokeThrough(CavePassage drift)
			{
				int n = drift.Nodes.Count;
				if (n < 3)
				{
					return false;
				}
				CaveNode end = drift.Nodes[n - 1];
				Vector3 dir = EndDir(drift);
				float rough = 0.3f / Mathf.Max(0.02f, system.NoiseShare);
				float rx = rng.Range(10f, 18f);
				CaveChamber cavern = default;
				bool fits = false;
				for (int shrink = 0; shrink < 4 && !fits; shrink++, rx *= 0.85f)
				{
					var radii = new Vector3(rx, Mathf.Clamp(0.6f * rx, 6f, 14f), rx * rng.Range(0.7f, 0.95f));
					fits = walker.TryLobe(end.Centre + dir * (0.7f * rx), end.Floor, Yaw(dir), radii, 0f, CaveChamberKind.Hall, siteId, rough, out cavern, q => Walker.NearChain(drift, q));
				}
				if (!fits)
				{
					return false;
				}
				system.Chambers.Add(cavern);
				system.PrepareField();
				Vector3 axis = new Vector3(dir.z, 0f, -dir.x);
				Vector3 m = cavern.Centre + dir * (0.1f * rx);
				float top = FloorOf(cavern, m);
				float width = Mathf.Min(rng.Range(1.8f, 3f), 0.45f * rx - 2f);
				float bottom = Mathf.Max(top - rng.Range(10f, 22f), system.LowestFloor - system.Origin.y + 0.5f);
				if (width < 1.4f || top - bottom < 6f)
				{
					return true;
				}
				float half = ReachOf(cavern, axis) / Mathf.Sqrt(1f - 0.16f) + 6f;
				for (float s = -half; s <= half; s += 2f)
				{
					for (float y = bottom + 1f; y < top - width - 2f; y += 2.5f)
					{
						if (system.Smooth(new Vector3(m.x + axis.x * s, y, m.z + axis.z * s)) < width + 1f)
						{
							return true;
						}
					}
				}
				Mark mark = Save();
				system.Rifts.Add(new CaveRift
				{
					A = new Vector3(m.x - axis.x * half, bottom, m.z - axis.z * half),
					B = new Vector3(m.x + axis.x * half, bottom, m.z + axis.z * half),
					Top = top + 1.5f,
					Width = width,
					Narrow = width * rng.Range(0.6f, 0.85f),
				});
				float reach = width + 2.2f;
				Vector3 a = m - dir * reach, b = m + dir * reach;
				a.y = FloorOf(cavern, a);
				b.y = FloorOf(cavern, b);
				system.Crossings.Add(new CaveCrossing { Kind = rng.NextFloat() < 0.7f ? CaveCrossingKind.Trestle : CaveCrossingKind.Rope, A = a, B = b, HalfWidth = 1f });
				system.PrepareField();
				if (!RiftClimb(m, dir, axis, top, bottom, width, 1.4f))
				{
					// The cavern stands without its chasm.
					Restore(mark);
				}
				return true;
			}
		}

		// ── Loops within one system ───────────────────────────────

		/// <summary>
		/// A way of its own between two of this system's chambers (a loop), walked as a link is and kept off every other
		/// passage and chamber at another floor; false where none fits.
		/// </summary>
		private bool LinkWithin(CaveChamber from, CaveChamber to, float maxLength, DeterministicRNG rng, System.Func<Vector3, float, bool> inside,
			System.Func<Vector3, float> groundAt)
		{
			float r = Mathf.Max(MinBore, Radius * 0.7f);
			float amp = NoiseShare * Radius;
			float step = Mathf.Clamp(0.9f * r, 2.5f, 5f);
			Vector3 flat = to.Centre - from.Centre;
			flat.y = 0f;
			Vector3 dir0 = flat.normalized;
			var link = new CavePassage { Kind = CavePassageKind.Link };
			Vector3 centre = from.Centre + dir0 * (Mathf.Max(from.Radii.x, from.Radii.z) * 0.7f);
			float floor = FloorOf(from, centre);
			centre.y = floor + FloorDepth * r * Flat;
			link.Nodes.Add(new CaveNode { Centre = centre, Radius = r, Floor = floor, Along = 0f });
			float along = 0f;
			float heading = Mathf.Atan2(dir0.x, dir0.z) * Mathf.Rad2Deg;
			for (int guard = 0; guard < 300 && along < maxLength; guard++)
			{
				Vector3 toTarget = to.Centre - centre;
				toTarget.y = 0f;
				float reach = Mathf.Max(to.Radii.x, to.Radii.z) * 0.6f;
				float gap = FloorOf(to, centre) - floor;
				if (toTarget.magnitude <= reach && Mathf.Abs(gap) <= 0.4f)
				{
					Passages.Add(link);
					PrepareField();
					return true;
				}
				float aim = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
				float remaining = Mathf.Max(step, toTarget.magnitude - reach);
				if (Mathf.Abs(gap) > remaining * 0.33f)
				{
					aim += 75f;
				}
				heading += Mathf.Clamp(DeltaAngle(heading, aim), -20f, 20f) + rng.Range(-8f, 8f);
				float p = Mathf.Clamp(Mathf.Atan2(gap, remaining) * Mathf.Rad2Deg, -(WalkPitch - 2f), WalkPitch - 2f);
				float run = step * Mathf.Cos(p * Mathf.Deg2Rad);
				bool found = false;
				foreach (float turn in LinkTurns)
				{
					float h = heading + turn, hr = h * Mathf.Deg2Rad;
					float nf = floor + step * Mathf.Sin(p * Mathf.Deg2Rad);
					var n = new Vector3(centre.x + Mathf.Sin(hr) * run, nf + FloorDepth * r * Flat, centre.z + Mathf.Cos(hr) * run);
					if (!inside(n, r + 12f))
					{
						continue;
					}
					// Over the whole bore, its sides too (BoreCover): a loop along a steep face kept its middle covered and broke out.
					if (BoreCover(groundAt, n, r, Flat, amp, centre) < MinCover)
					{
						// Too near the surface here: go deeper first.
						nf = floor - step * Mathf.Sin((WalkPitch - 2f) * Mathf.Deg2Rad);
						n.y = nf + FloorDepth * r * Flat;
						if (BoreCover(groundAt, n, r, Flat, amp, centre) < MinCover)
						{
							continue;
						}
					}
					var fromXz = new Vector2(n.x - from.Centre.x, n.z - from.Centre.z);
					var toXz = new Vector2(n.x - to.Centre.x, n.z - to.Centre.z);
					bool leaving = fromXz.magnitude < Mathf.Max(from.Radii.x, from.Radii.z) * 1.3f;
					bool arriving = toXz.magnitude < Mathf.Max(to.Radii.x, to.Radii.z) * 1.5f;
					if (!leaving && !arriving && Smooth(n) < r + 2f && Mathf.Abs(FloorNear(n) - nf) > 0.5f || NearChasmOrClimb(n, r + 1f))
					{
						continue;
					}
					if (nf + Origin.y < LowestFloor)
					{
						continue;
					}
					centre = n;
					floor = nf;
					heading = h;
					found = true;
					break;
				}
				if (!found)
				{
					return false;
				}
				along += step;
				link.Nodes.Add(new CaveNode { Centre = centre, Radius = r * rng.Range(0.92f, 1.08f), Floor = floor, Along = along });
			}
			return false;
		}
	}
}
#endif
