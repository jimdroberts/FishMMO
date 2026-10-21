#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Molten lava in a lava tube (2026-10-20, <see cref="CaveLava"/>): where it lies (<see cref="PlanLava"/>, read only from the
	/// system's shapes) and the bed the field cuts for it — a river's channel under the floor beside the tube's way, a lake's
	/// basin, a fall's plunge pool — unioned into the void (<see cref="Tube(Vector3, out float, out float, out float, bool)"/>) so
	/// everything that reads the field (the mesher, the holes, the dressing, the walk checks) sees one cave.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Cut after planning.</b> The layout and the variety pass plan without it (<see cref="LavaHeld"/> while
	/// <see cref="Plan"/> runs), so a cooled system and a molten one of the same seed are the same system, stored alike; the
	/// plan keeps itself off everything they laid, and the field gains only void, under floors no way is walked on.
	/// </para>
	/// </remarks>
	public sealed partial class CaveSystem
	{
		private bool lavaHeld;
		private LavaBed lavaBed;

		/// <summary>
		/// Whether the field leaves the system's lava uncut (its channels and basins): while it is planned, and for a check that
		/// compares the cave with and without it. Setting it cuts the field again.
		/// </summary>
		public bool LavaHeld
		{
			get => lavaHeld;
			set
			{
				if (value == lavaHeld)
				{
					return;
				}
				lavaHeld = value;
				if (buckets != null)
				{
					PrepareField();
				}
			}
		}

		/// <summary>Whether the field has any lava cut into it.</summary>
		public bool LavaCut => lavaBed != null;

		/// <summary>A lava tube's roll for whether it still runs molten (<see cref="CaveLava.Molten"/>), 0 .. 1 from its seed.</summary>
		public static float LavaRoll(int seed) => Unit(unchecked((uint)seed), 0x1A7A5u);

		/// <summary>Cuts the lava into the field as the system now stands (from <see cref="PrepareField"/>; none while held).</summary>
		private void PrepareLava()
		{
			lavaBed = null;
			if (lavaHeld)
			{
				return;
			}
			CaveLavaPlan plan = CaveLava.Plan(this);
			if (plan.Active && plan.Bodies.Count > 0)
			{
				lavaBed = LavaBed.Build(plan, Seed);
			}
		}

		/// <summary>The void with the lava's bed cut into it (unchanged where none is cut).</summary>
		private float WithLava(Vector3 p, float d) => lavaBed == null ? d : Mathf.Min(d, lavaBed.At(p));

		// ── The bed ───────────────────────────────────────────────

		/// <summary>
		/// The beds cut for a plan's bodies: each river with banks a chain of channel pieces (flat-floored slots between its
		/// points, open from its bed up past the floor across it), each lake with banks a basin; bucketed in plan so a point
		/// reads only those near it. Built whole before the field is read (the mesher reads it from several threads).
		/// </summary>
		private sealed class LavaBed
		{
			private const float Cell = 8f;
			/// <summary>The bank's share along its own normal: the cone's distance scaled from the distance in plan.</summary>
			private static readonly float BankNormal = BankGrade / Mathf.Sqrt(1f + BankGrade * BankGrade);

			private struct Piece
			{
				public Vector3 A, B;
				public float Half, BedA, BedB, TopA, TopB;
				public bool Basin;
				/// <summary>A basin's rim (the lowest floor round it): over it the basin's side is graded back at <see cref="BankGrade"/>.</summary>
				public float Rim;
			}

			private readonly List<Piece> pieces = new List<Piece>();
			private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
			private int seed;

			public static LavaBed Build(CaveLavaPlan plan, int seed)
			{
				var bed = new LavaBed { seed = seed };
				foreach (CaveLavaBody body in plan.Bodies)
				{
					if (body.Banks == null || body.Tops == null || body.Path == null || body.Path.Length == 0)
					{
						continue;
					}
					if (body.Kind == CaveLavaKind.Lake)
					{
						Vector3 c = body.Path[0];
						bed.Add(new Piece { A = c, B = c, Half = body.Width, BedA = c.y - CaveLava.Depth, BedB = c.y - CaveLava.Depth, TopA = body.Tops[0], TopB = body.Tops[0], Basin = true,
							Rim = body.Banks.Length > 0 ? body.Banks[0] : c.y + CaveLava.Drop });
						continue;
					}
					for (int i = 0; i + 1 < body.Path.Length; i++)
					{
						Vector3 a = body.Path[i], b = body.Path[i + 1];
						bed.Add(new Piece
						{
							A = a,
							B = b,
							Half = 0.5f * body.Width,
							BedA = a.y - CaveLava.Depth,
							BedB = b.y - CaveLava.Depth,
							TopA = body.Tops[i],
							TopB = body.Tops[i + 1],
						});
					}
				}
				return bed;
			}

			private void Add(Piece piece)
			{
				int id = pieces.Count;
				pieces.Add(piece);
				float r = piece.Half + 0.5f + (piece.Basin ? Mathf.Max(0f, Mathf.Max(piece.TopA, piece.TopB) - piece.Rim) / BankGrade : 0f);
				int x0 = Mathf.FloorToInt((Mathf.Min(piece.A.x, piece.B.x) - r) / Cell), x1 = Mathf.FloorToInt((Mathf.Max(piece.A.x, piece.B.x) + r) / Cell);
				int z0 = Mathf.FloorToInt((Mathf.Min(piece.A.z, piece.B.z) - r) / Cell), z1 = Mathf.FloorToInt((Mathf.Max(piece.A.z, piece.B.z) + r) / Cell);
				for (int x = x0; x <= x1; x++)
				{
					for (int z = z0; z <= z1; z++)
					{
						long key = Key(x, z);
						if (!cells.TryGetValue(key, out List<int> list))
						{
							cells[key] = list = new List<int>();
						}
						list.Add(id);
					}
				}
			}

			private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

			/// <summary>The beds' void at a point (negative inside), 1e4 where none is near.</summary>
			public float At(Vector3 p)
			{
				if (!cells.TryGetValue(Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell)), out List<int> ids))
				{
					return 1e4f;
				}
				float d = 1e4f;
				foreach (int id in ids)
				{
					Piece piece = pieces[id];
					if (p.y < Mathf.Min(piece.BedA, piece.BedB) - 2f || p.y > Mathf.Max(piece.TopA, piece.TopB) + 2f)
					{
						continue;
					}
					float t = 0f;
					var ab = new Vector2(piece.B.x - piece.A.x, piece.B.z - piece.A.z);
					var ap = new Vector2(p.x - piece.A.x, p.z - piece.A.z);
					if (!piece.Basin)
					{
						t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					}
					float across = (ap - ab * t).magnitude - piece.Half;
					if (piece.Basin && p.y > piece.Rim)
					{
						// The bank over the rim graded back as a cone (its distance along the slope's normal), so a lake on a sloping
						// floor is a flattened basin with a walkable shore on its high side, not a wall over its lava.
						across = (across - (p.y - piece.Rim) / BankGrade) * BankNormal;
					}
					float bed = piece.BedA + (piece.BedB - piece.BedA) * t, top = piece.TopA + (piece.TopB - piece.TopA) * t;
					float v = Mathf.Max(across, Mathf.Max(bed - p.y, p.y - top));
					if (v < d)
					{
						d = v;
					}
				}
				if (d > 1f)
				{
					return d;
				}
				// Its sides a little uneven, as rock the lava wore: never enough to close a channel or reach the lane beside it.
				return d - 0.1f * ProceduralNoise.Gradient3(p / 0.8f, seed + 61);
			}
		}

		// ── Measuring the cave without its lava ───────────────────

		/// <summary>The noisy void without the lava's bed (as the mesher cuts the rock, but for the lava): where the gate's wall is found.</summary>
		private float HeldVoidAt(Vector3 p) => Void(p, out _, false);

		/// <summary>The void as the field stands without the lava's bed, smooth, the solids put back: what the plan measures floors on.</summary>
		private float HeldVoid(Vector3 p)
		{
			float d = Tube(p, out _, out _, out _, false);
			if (d > 1e3f)
			{
				return d;
			}
			float s = Solid(p, out _);
			return s < 1e3f ? Mathf.Max(d, -s) : d;
		}

		/// <summary>
		/// The floor under a point, found down from <paramref name="from"/> on the field without its lava (the walking floor: the
		/// smooth unions of a tube's segments let it down as much as a metre and a half under its nodes' own); NaN where the point
		/// is rock at <paramref name="from"/> or no floor lies within <paramref name="reach"/>.
		/// </summary>
		private float HeldFloor(Vector3 p, float from, float reach = 4f)
		{
			float open = from;
			if (HeldVoid(new Vector3(p.x, open, p.z)) >= 0f)
			{
				return float.NaN;
			}
			float rock = open - 0.4f;
			while (HeldVoid(new Vector3(p.x, rock, p.z)) < 0f)
			{
				open = rock;
				rock -= 0.4f;
				if (from - rock > reach)
				{
					return float.NaN;
				}
			}
			for (int k = 0; k < 6; k++)
			{
				float mid = 0.5f * (open + rock);
				if (HeldVoid(new Vector3(p.x, mid, p.z)) < 0f)
				{
					open = mid;
				}
				else
				{
					rock = mid;
				}
			}
			return open;
		}

		// ── The plan ──────────────────────────────────────────────

		/// <summary>
		/// A point of a river's run: the passage's centre line on its nodes' floor there, the way across to its channel, the
		/// channel's middle (y the nodes' floor), and once measured the walking floor beside it (<see cref="Bank"/>) and the
		/// highest floor across it (<see cref="Top"/>).
		/// </summary>
		private struct LavaSample
		{
			public Vector3 Line, Side, At;
			public float Radius, Height, Bank, Top;
			public bool Open;
		}

		/// <summary>The ways every walk takes (each passage's line, each passage's end into a chamber to its middle, ramps, bridges, crossings, terraces' walks, climbs' feet and heads).</summary>
		private List<(Vector3 a, Vector3 b)> lavaWays;
		/// <summary>The places kept clear: each chamber's middle (the site's own the most), each mouth and its approach.</summary>
		private List<(Vector3 at, float radius, float height)> lavaKeeps;
		/// <summary>The channels and lakes laid so far: their middles and half widths (a lake's radius), and which body each is.</summary>
		private List<(Vector3 at, float half, int body)> lavaLaid;
		/// <summary>The three lists above are one plan's scratch: one plan at a time.</summary>
		private readonly object lavaPlanning = new object();

		/// <summary>
		/// The system's molten lava (<see cref="CaveLava.Plan"/>, for a molten one): rivers down its passages in channels beside
		/// their ways, a fall pouring from a vent in the wall into each river's head, falls down the free ends of its chasms
		/// into plunge pools, lakes in its chambers and in the basins the variety pass sank. Reads the system's shapes, and its
		/// floors as the field cuts them without the lava.
		/// </summary>
		internal CaveLavaPlan PlanLava()
		{
			if (buckets == null)
			{
				Prepare();
			}
			var plan = new CaveLavaPlan { Active = true };
			lock (lavaPlanning)
			{
				LavaLanes();
				lavaLaid = new List<(Vector3, float, int)>();
				try
				{
					// The lakes first (2026-10-21: "Stacked tubes + big lakes"; the deepest cavern always holds one) and the basins', then
					// the trunks' rivers, the chasms' falls (fed by a river where one comes), and the side passages' rivers.
					LavaLakes(plan);
					LavaBasins(plan);
					for (int p = 0; p < Passages.Count; p++)
					{
						if (Passages[p].Kind == CavePassageKind.Trunk)
						{
							LavaRiver(plan, p);
						}
					}
					for (int r = 0; r < Rifts.Count; r++)
					{
						LavaChasmFall(plan, r);
					}
					for (int p = 0; p < Passages.Count; p++)
					{
						if (Passages[p].Kind != CavePassageKind.Trunk)
						{
							LavaRiver(plan, p);
						}
					}
				}
				finally
				{
					lavaWays = null;
					lavaKeeps = null;
					lavaLaid = null;
				}
			}
			return plan;
		}

		private static bool Room(CaveLavaPlan plan, int bodies = 1) => plan.Bodies.Count + bodies <= CaveLava.MaxBodies;

		private static Vector3 FlatDir(Vector3 v)
		{
			v.y = 0f;
			return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
		}

		private static Vector3 LavaDir(float yaw)
		{
			float r = yaw * Mathf.Deg2Rad;
			return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
		}

		private static float Plan2(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

		/// <summary>A point's distance in plan from a segment, and how far along it the nearest point lies (0 .. 1).</summary>
		private static float PlanToSegment(Vector3 p, Vector3 a, Vector3 b, out float t)
		{
			var ab = new Vector2(b.x - a.x, b.z - a.z);
			var ap = new Vector2(p.x - a.x, p.z - a.z);
			t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
			return (ap - ab * t).magnitude;
		}

		/// <summary>Whether a point lies over a chamber's floor (in plan, within its floor's outline grown by <paramref name="grow"/>), at a floor near its own.</summary>
		private static bool OverFloor(in CaveChamber c, Vector3 p, float grow, float height = 3f)
		{
			Vector3 o = p - c.Centre;
			o.y = 0f;
			float distance = o.magnitude;
			return distance <= FloorReach(c, distance > 1e-4f ? o / distance : Vector3.forward) + grow && Mathf.Abs(p.y - FloorOf(c, p)) < height;
		}

		/// <summary>How far a chamber's floor reaches from its middle in a direction (as the layout's reach).</summary>
		private static float FloorReach(in CaveChamber c, Vector3 dir)
		{
			float yr = c.Yaw * Mathf.Deg2Rad;
			float along = dir.x * Mathf.Sin(yr) + dir.z * Mathf.Cos(yr);
			float across = dir.x * Mathf.Cos(yr) - dir.z * Mathf.Sin(yr);
			float r = 1f / Mathf.Sqrt(along * along / (c.Radii.x * c.Radii.x) + across * across / (c.Radii.z * c.Radii.z) + 1e-9f);
			return r * Mathf.Sqrt(1f - 0.16f);
		}

		/// <summary>The ways and kept places (as the variety pass's own lanes, with the terraces' walks and the climbs' feet and heads).</summary>
		private void LavaLanes()
		{
			lavaWays = new List<(Vector3, Vector3)>();
			lavaKeeps = new List<(Vector3, float, float)>();
			foreach (CavePassage p in Passages)
			{
				for (int i = 0; i + 1 < p.Nodes.Count; i++)
				{
					CaveNode a = p.Nodes[i], b = p.Nodes[i + 1];
					lavaWays.Add((new Vector3(a.Centre.x, a.Floor, a.Centre.z), new Vector3(b.Centre.x, b.Floor, b.Centre.z)));
				}
			}
			var own = new HashSet<int>();
			foreach (CaveMouth m in Mouths)
			{
				if (TryChamberOf(m.SiteId, out CaveChamber chamber))
				{
					own.Add(Chambers.FindIndex(c => c.Centre == chamber.Centre && c.Kind == chamber.Kind));
				}
			}
			for (int ci = 0; ci < Chambers.Count; ci++)
			{
				CaveChamber c = Chambers[ci];
				Vector3 middle = new Vector3(c.Centre.x, c.Floor, c.Centre.z);
				foreach (CavePassage p in Passages)
				{
					foreach (CaveNode n in new[] { p.Nodes[0], p.Nodes[p.Nodes.Count - 1] })
					{
						if (OverFloor(c, new Vector3(n.Centre.x, n.Floor, n.Centre.z), n.Radius + 2f))
						{
							lavaWays.Add((new Vector3(n.Centre.x, n.Floor, n.Centre.z), middle));
						}
					}
				}
				float small = Mathf.Min(c.Radii.x, c.Radii.z);
				// The site's own middle kept: a small or middling site's camp or den over half of it; a large one's (its dungeon gate kept
				// on its own below, its floor strewn with bones only) a fifth (2026-10-21, it was 0.6 for every cavern), so its lake fits.
				float keep = own.Contains(ci)
					? (c.Kind == CaveChamberKind.Cavern ? (SizeClass >= 2 ? Mathf.Max(6f, 0.2f * small) : Mathf.Max(8f, 0.5f * small)) : Mathf.Max(4.5f, 0.35f * small))
					: c.Kind == CaveChamberKind.Hall ? Mathf.Max(3f, 0.12f * small) : 2f;
				lavaKeeps.Add((middle, keep, 6f));
			}
			// A large tube's dungeon gate against its great cavern's wall and its forecourt (DungeonEntranceShaper), found on the
			// field as it stands without the lava: no lake comes near enough to move it.
			if (SizeClass >= 2)
			{
				foreach (CaveMouth m in Mouths)
				{
					if (m.Kind != CaveMouthKind.Skylight && DungeonEntranceShaper.TryCavernGateClearing(this, m.SiteId, HeldVoidAt, out Vector3 gate, out float clear))
					{
						lavaKeeps.Add((gate, clear + 2f, 6f));
					}
				}
			}
			foreach (CaveSolid s in Solids)
			{
				if (s.Kind == CaveSolidKind.Ramp || s.Kind == CaveSolidKind.Bridge)
				{
					lavaWays.Add((s.A, s.B));
				}
			}
			foreach (CaveCrossing c in Crossings)
			{
				lavaWays.Add((c.A, c.B));
			}
			foreach (CaveTerrace t in Terraces)
			{
				List<Vector3> walk = TerraceWalk(t);
				for (int i = 0; i + 1 < walk.Count; i++)
				{
					lavaWays.Add((walk[i], walk[i + 1]));
				}
			}
			foreach (CaveClimb c in Climbs)
			{
				lavaWays.Add((c.Bottom, c.Bottom + c.Out * 1.5f));
				lavaWays.Add((c.Top, c.Top + c.TopExit * 1.5f));
			}
			foreach (CaveMouth m in Mouths)
			{
				lavaKeeps.Add((m.Floor, m.Zone + 2f * m.Radius + 8f, 25f));
			}
		}

		/// <summary>
		/// Whether lava may lie at a point on a floor with <paramref name="margin"/> round it: clear of every way (by a walker's
		/// half width more), kept place, chasm (but <paramref name="exceptRift"/>), shaft, climb, crossing, rock left standing,
		/// basin or trench, and of the lava laid before it (but <paramref name="exceptBody"/>'s).
		/// </summary>
		private bool LavaClear(Vector3 q, float margin, int exceptRift = -1, int exceptBody = -1)
		{
			const float WalkerHalf = 1f;
			foreach ((Vector3 a, Vector3 b) in lavaWays)
			{
				float d = PlanToSegment(q, a, b, out float t);
				if (d < margin + WalkerHalf && Mathf.Abs(q.y - Mathf.Lerp(a.y, b.y, t)) < 3f)
				{
					return false;
				}
			}
			foreach ((Vector3 at, float radius, float height) in lavaKeeps)
			{
				if (Plan2(q, at) < radius + margin && Mathf.Abs(q.y - at.y) < height)
				{
					return false;
				}
			}
			for (int r = 0; r < Rifts.Count; r++)
			{
				CaveRift rift = Rifts[r];
				if (r != exceptRift && PlanToSegment(q, rift.A, rift.B, out _) < rift.Width + margin + 0.5f
					&& q.y > Mathf.Min(rift.A.y, rift.B.y) - 2f && q.y < rift.Top + 2f)
				{
					return false;
				}
			}
			foreach (CaveShaft s in Shafts)
			{
				if (Plan2(q, s.Bottom) < s.Radius * 1.42f + margin + 0.5f && q.y > s.Bottom.y - 2f && q.y < s.Top + 2f)
				{
					return false;
				}
			}
			foreach (CaveClimb c in Climbs)
			{
				Vector3 ab = c.Top - c.Bottom, ap = q - c.Bottom;
				float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
				if ((ap - ab * t).magnitude < 2.5f + margin)
				{
					return false;
				}
			}
			foreach (CaveCrossing c in Crossings)
			{
				if (PlanToSegment(q, c.A, c.B, out float t) < c.HalfWidth + margin + 1f && Mathf.Abs(q.y - Mathf.Lerp(c.A.y, c.B.y, t)) < 4f)
				{
					return false;
				}
			}
			foreach (CaveSolid s in Solids)
			{
				SolidBounds(s, out Vector3 lo, out Vector3 hi);
				if (q.x > lo.x - margin && q.x < hi.x + margin && q.z > lo.z - margin && q.z < hi.z + margin && q.y > lo.y - 2f && q.y < hi.y + 2f)
				{
					return false;
				}
			}
			if (Hollows.Count > 0 && NearHollow(q + Vector3.up * 0.5f, margin))
			{
				return false;
			}
			foreach ((Vector3 at, float half, int body) in lavaLaid)
			{
				if (body != exceptBody && Plan2(q, at) < half + margin + 1.2f && Mathf.Abs(q.y - at.y) < 3.5f)
				{
					return false;
				}
			}
			return !LavaOverLower(q, margin, exceptRift);
		}

		/// <summary>How far under a floor another must lie to be another level's (not the same floor's own unevenness), metres.</summary>
		private const float LowerLevel = 2.5f;

		/// <summary>
		/// Whether any void another level down lies under a point (in plan, within <paramref name="margin"/>): a passage's bore, a
		/// chamber's floor, a chasm (but <paramref name="exceptRift"/>), a shaft. The scene's water is read in plan, the highest
		/// surface over a point answering (WaterQuery), so lava over a way under it would burn whoever walked there.
		/// </summary>
		private bool LavaOverLower(Vector3 q, float margin, int exceptRift = -1)
		{
			float under = q.y - LowerLevel;
			foreach (CavePassage p in Passages)
			{
				for (int i = 0; i + 1 < p.Nodes.Count; i++)
				{
					CaveNode a = p.Nodes[i], b = p.Nodes[i + 1];
					if (Mathf.Min(a.Floor, b.Floor) < under && PlanToSegment(q, a.Centre, b.Centre, out float t) < Mathf.Lerp(a.Radius, b.Radius, t) + margin
						&& Mathf.Lerp(a.Floor, b.Floor, t) < under)
					{
						return true;
					}
				}
			}
			foreach (CaveChamber c in Chambers)
			{
				Vector3 o = q - c.Centre;
				o.y = 0f;
				float distance = o.magnitude;
				if (FloorOf(c, q) < under && distance <= FloorReach(c, distance > 1e-4f ? o / distance : Vector3.forward) / Mathf.Sqrt(1f - 0.16f) + margin)
				{
					return true;
				}
			}
			for (int r = 0; r < Rifts.Count; r++)
			{
				CaveRift rift = Rifts[r];
				if (r != exceptRift && Mathf.Min(rift.A.y, rift.B.y) < under && PlanToSegment(q, rift.A, rift.B, out _) < rift.Width + margin)
				{
					return true;
				}
			}
			foreach (CaveShaft s in Shafts)
			{
				if (s.Bottom.y < under && Plan2(q, s.Bottom) < s.Radius * 1.42f + margin)
				{
					return true;
				}
			}
			return false;
		}

		// ── Rivers ────────────────────────────────────────────────

		/// <summary>The widest channel a bore of a radius takes, metres.</summary>
		private static float ChannelMost(float radius) => Mathf.Clamp(0.45f * radius, CaveLava.MinWidth, 3.2f);

		/// <summary>How far past the foot of its wall a channel may run (under the wall, as the lava wore it), metres.</summary>
		private const float ChannelUnder = 0.5f;

		/// <summary>How far out from its centre line a bore's floor reaches (its section is cut flat at its floor), metres.</summary>
		private static float FloorHalf(float radius) => 0.86f * radius;

		/// <summary>
		/// The river down a passage, if it runs one (every trunk; side passages and links by the seed): downstream from its
		/// higher end, in a channel along whichever wall leaves it the longer runs, its surface <see cref="CaveLava.Drop"/> under
		/// the floor beside it, falling with that floor and never rising; split where it must not lie, each run a body, the head
		/// of each fed by a fall from a vent in the wall unless it is the run before it welling up past a natural bridge.
		/// </summary>
		private void LavaRiver(CaveLavaPlan plan, int pi)
		{
			CavePassage passage = Passages[pi];
			int count = passage.Nodes.Count;
			if (count < 3 || !Room(plan))
			{
				return;
			}
			uint h = Mix(unchecked((uint)Seed), 0x1A7A0u + (uint)pi * 0x9E37u);
			if (passage.Kind != CavePassageKind.Trunk && Unit(h, 1) >= (passage.Kind == CavePassageKind.Link ? 0.6f : 0.5f))
			{
				return;
			}
			// As wide as the bore takes beside its lane where it runs narrow (its narrower quarter): where it is narrower still, the
			// channel is not cut.
			var radii = new List<float>();
			foreach (CaveNode n in passage.Nodes)
			{
				radii.Add(n.Radius);
			}
			radii.Sort();
			float narrow = radii[radii.Count / 4];
			float width = Mathf.Min(ChannelMost(narrow), FloorHalf(narrow) + ChannelUnder - CaveLava.LaneClearLeast);
			if (width < CaveLava.MinWidth)
			{
				return;
			}
			// Its near edge as far off the lane as leaves it that width (a narrow tube's a little nearer).
			float inner = Mathf.Clamp(FloorHalf(narrow) + ChannelUnder - width, CaveLava.LaneClearLeast, CaveLava.LaneClear);
			bool reverse = passage.Nodes[count - 1].Floor > passage.Nodes[0].Floor + 0.3f;
			float offset = inner + 0.5f * width;
			List<LavaSample> right = LavaSamples(passage, reverse, 1f, offset, width), left = LavaSamples(passage, reverse, -1f, offset, width);
			int openRight = 0, openLeft = 0;
			foreach (LavaSample s in right) openRight += s.Open ? 1 : 0;
			foreach (LavaSample s in left) openLeft += s.Open ? 1 : 0;
			List<LavaSample> samples = openRight > openLeft || openRight == openLeft && Unit(h, 2) < 0.5f ? right : left;
			MeasureBanks(samples, width);

			const int MinRun = 9, Gap = 3;
			float carried = float.NaN;
			int lastEnd = -1000;
			int i = 0;
			while (i < samples.Count && Room(plan))
			{
				if (!samples[i].Open)
				{
					i++;
					continue;
				}
				// Under a natural bridge (a short gap since the last run) the lava runs on at its own level; anywhere else it wells up afresh.
				bool welling = float.IsNaN(carried) || i - lastEnd > 15 || samples[i].Bank - carried > CaveLava.MaxSink;
				float surface = welling ? samples[i].Bank - CaveLava.Drop : Mathf.Min(carried, samples[i].Bank - CaveLava.Drop);
				var path = new List<Vector3>();
				var banks = new List<float>();
				var tops = new List<float>();
				int j = i;
				bool sank = false;
				for (; j < samples.Count && samples[j].Open; j++)
				{
					float bank = samples[j].Bank;
					surface = Mathf.Min(surface, bank - CaveLava.Drop);
					if (bank - surface > CaveLava.MaxSink)
					{
						sank = true;
						break;
					}
					path.Add(new Vector3(samples[j].At.x, surface, samples[j].At.z));
					banks.Add(bank);
					tops.Add(samples[j].Top);
				}
				if (path.Count >= MinRun && Room(plan, welling ? 2 : 1))
				{
					int index = plan.Bodies.Count;
					plan.Bodies.Add(new CaveLavaBody
					{
						Kind = CaveLavaKind.River,
						Path = path.ToArray(),
						Width = width,
						Banks = banks.ToArray(),
						Tops = tops.ToArray(),
						Passage = pi,
						Chamber = -1,
						Pit = -1,
					});
					for (int k = i; k < j; k++)
					{
						lavaLaid.Add((samples[k].At, 0.5f * width, index));
					}
					if (welling)
					{
						LavaVent(plan, pi, samples[i], path, width);
					}
					carried = surface;
					lastEnd = j - 1;
				}
				i = sank ? j + Gap : j;
				if (sank)
				{
					carried = float.NaN;
				}
			}
		}

		/// <summary>A passage's channel line, sampled every metre downstream on the side asked, with whether lava may lie at each (by the shapes alone).</summary>
		private List<LavaSample> LavaSamples(CavePassage passage, bool reverse, float side, float offset, float width)
		{
			int count = passage.Nodes.Count;
			var nodes = new CaveNode[count];
			for (int k = 0; k < count; k++)
			{
				nodes[k] = passage.Nodes[reverse ? count - 1 - k : k];
			}
			var across = new Vector3[count];
			for (int k = 0; k < count; k++)
			{
				Vector3 run = FlatDir(nodes[Mathf.Min(count - 1, k + 1)].Centre - nodes[Mathf.Max(0, k - 1)].Centre);
				across[k] = new Vector3(run.z, 0f, -run.x) * side;
			}
			var samples = new List<LavaSample>();
			for (int k = 0; k + 1 < count; k++)
			{
				CaveNode a = nodes[k], b = nodes[k + 1];
				var la = new Vector3(a.Centre.x, a.Floor, a.Centre.z);
				var lb = new Vector3(b.Centre.x, b.Floor, b.Centre.z);
				int steps = Mathf.Max(1, Mathf.CeilToInt(Plan2(la, lb)));
				for (int s = k == 0 ? 0 : 1; s <= steps; s++)
				{
					float f = s / (float)steps;
					var sample = new LavaSample
					{
						Line = Vector3.Lerp(la, lb, f),
						Side = FlatDir(Vector3.Lerp(across[k], across[k + 1], f)),
						Radius = Mathf.Lerp(a.Radius, b.Radius, f),
						Height = Mathf.Lerp(a.Height, b.Height, f),
					};
					sample.At = sample.Line + sample.Side * offset;
					sample.Open = offset + 0.5f * width <= FloorHalf(sample.Radius) + ChannelUnder && LavaClear(sample.At, 0.5f * width);
					samples.Add(sample);
				}
			}
			return samples;
		}

		/// <summary>
		/// Each open sample's floors as the field cuts them: the bank (the lower of the floor at the channel's near edge and at its
		/// middle: the bench the way runs on) and the top the bed is cut to (over the highest floor across it, up its far side
		/// toward the wall). A sample whose bench is not open floor is closed.
		/// </summary>
		private void MeasureBanks(List<LavaSample> samples, float width)
		{
			for (int k = 0; k < samples.Count; k++)
			{
				LavaSample s = samples[k];
				if (!s.Open)
				{
					continue;
				}
				float from = s.Line.y + 0.6f;
				float near = HeldFloor(s.At - s.Side * (0.5f * width), from);
				float middle = HeldFloor(s.At, from);
				float far = HeldFloor(s.At + s.Side * (0.5f * width), from);
				if (float.IsNaN(near) || float.IsNaN(middle))
				{
					s.Open = false;
				}
				else
				{
					s.Bank = Mathf.Min(near, middle);
					// Its far side in the wall's foot: cut a little under it (as the lava wore it), no higher.
					s.Top = Mathf.Max(near, middle) + 0.3f;
					s.Top = float.IsNaN(far) ? s.Top + 0.3f : Mathf.Max(s.Top, far + 0.3f);
				}
				samples[k] = s;
			}
		}

		/// <summary>
		/// A fall pouring from a vent in the wall over a river's head into it: from as high as lets it reach the channel forward
		/// (across, and on down the channel) half as far as it drops (as a fall is drawn), landing in the channel's middle; none
		/// where the head lies in a room, out from its walls.
		/// </summary>
		private void LavaVent(CaveLavaPlan plan, int pi, in LavaSample head, List<Vector3> path, float width)
		{
			if (!Room(plan))
			{
				return;
			}
			// The wall's face at each height, found outward from over the channel (the smooth unions of the bore's segments stand it
			// out past the capsule's own); none within the bore's reach is a room's open floor, not a wall.
			for (float up = 2.6f; up >= 1f; up -= 0.4f)
			{
				Vector3 vent = head.At;
				vent.y = head.Line.y + up;
				// Open on the rough field too (a knob of the wall over the channel: marching out from inside it, the crack was set in the rock).
				if (HeldVoid(vent) >= 0f || Void(vent, out _, false) >= 0f)
				{
					continue;
				}
				// The rock's face as the mesher cuts it, its knobs and all (without the lava, which lies far under it).
				float reach = Vector3.Dot(vent - head.Line, head.Side);
				while (Void(vent, out _, false) < 0f && reach < 1.6f * head.Radius)
				{
					vent += head.Side * 0.2f;
					reach += 0.2f;
				}
				if (reach >= 1.6f * head.Radius)
				{
					continue;
				}
				for (int k = 0; k < Mathf.Min(path.Count, 10); k++)
				{
					Vector3 landing = path[k];
					if (Plan2(vent, landing) < 0.5f * (vent.y - landing.y))
					{
						continue;
					}
					plan.Bodies.Add(new CaveLavaBody
					{
						Kind = CaveLavaKind.Fall,
						Path = new[] { vent, landing },
						Width = Mathf.Clamp(0.45f * width, 0.6f, 1.4f),
						Passage = pi,
						Chamber = -1,
						Pit = -1,
					});
					return;
				}
			}
		}

		// ── Falls into chasms ─────────────────────────────────────

		/// <summary>
		/// A fall down a chasm's free end (2026-10-20: "lava falls between levels into a pool"): the end away from the passage on
		/// from its floor, past its climb and its crossing; fed by the river ending in the chasm's chamber where one does and its
		/// way to the lip is clear, else (now and then) pouring from a vent in the chasm's own wall under its lip; a plunge pool
		/// under it.
		/// </summary>
		private void LavaChasmFall(CaveLavaPlan plan, int ri)
		{
			if (!Room(plan, 2))
			{
				return;
			}
			CaveRift rift = Rifts[ri];
			uint h = Mix(unchecked((uint)Seed), 0x1A7AFu + (uint)ri * 0x9E37u);
			Vector3 axis = FlatDir(rift.B - rift.A);
			var e = new Vector3(axis.z, 0f, -axis.x);
			float half = 0.5f * Plan2(rift.A, rift.B);
			Vector3 m = 0.5f * (rift.A + rift.B);
			float bottom = Mathf.Min(rift.A.y, rift.B.y), lipFloor = rift.Top - 1.5f;
			m.y = lipFloor;
			int room = -1;
			for (int c = 0; c < Chambers.Count && room < 0; c++)
			{
				if (OverFloor(Chambers[c], m, 0.5f, 2.5f))
				{
					room = c;
				}
			}
			if (room < 0)
			{
				return;
			}
			// The end the way on leaves from (its first node on the chasm's floor near one end); the other is free.
			float taken = 0f;
			foreach (CavePassage p in Passages)
			{
				CaveNode n = p.Nodes[0];
				var at = new Vector3(n.Centre.x, n.Floor, n.Centre.z);
				float s = Vector3.Dot(at - m, axis);
				if (Mathf.Abs(n.Floor - bottom) < 1.5f && PlanToSegment(at, rift.A, rift.B, out _) < rift.Width + 3f && Mathf.Abs(s) > 0.4f * half)
				{
					taken = Mathf.Sign(s);
				}
			}
			if (taken == 0f)
			{
				return;
			}
			float free = -taken;
			// Past the crossing and every climb on the free side.
			float clear = 4f;
			foreach (CaveClimb c in Climbs)
			{
				if (PlanToSegment(c.Bottom, rift.A, rift.B, out _) < rift.Width + 3f && c.Bottom.y < lipFloor - 2f)
				{
					float s = Vector3.Dot(c.Bottom - m, axis) * free;
					if (s > -3f)
					{
						clear = Mathf.Max(clear, s + 3.5f);
					}
				}
			}
			CaveChamber chamber = Chambers[room];
			float reach = FloorReach(chamber, axis * free);
			float pool = Mathf.Clamp(rift.Narrow + 0.6f, 1.5f, 3f);
			// The river ending in the chasm's chamber, on whichever side of it the river comes.
			int fed = -1;
			for (int b = 0; b < plan.Bodies.Count && fed < 0; b++)
			{
				CaveLavaBody body = plan.Bodies[b];
				if (body.Kind == CaveLavaKind.River && OverFloor(chamber, body.Path[body.Path.Length - 1] + Vector3.up * CaveLava.Drop, 1f))
				{
					fed = b;
				}
			}
			// Fed by the river where its way to a lip is clear; else, now and then, from a vent in the chasm's wall a little
			// under its lip, on the side its seed picks.
			for (int pass = fed >= 0 ? 0 : 1; pass < 2; pass++)
			{
				if (pass == 1 && Unit(h, 1) >= 0.7f)
				{
					return;
				}
				for (float lip = clear; lip <= reach - 2f; lip += 2f)
				{
					float land = lip + Mathf.Max(3f, 0.55f * (lipFloor - bottom));
					if (land > half - pool - 0.5f)
					{
						break;
					}
					Vector3 poolAt = m + axis * (free * land);
					// Its floor measured, and nothing of another level under it (2026-10-21: a stacked tube's chasm may stand over one).
					if (!LavaPoolFits(poolAt, pool, bottom, axis, out float poolFloor, out float poolTop)
						|| LavaOverLower(new Vector3(poolAt.x, poolFloor, poolAt.z), pool, ri))
					{
						continue;
					}
					poolAt.y = poolFloor - CaveLava.Drop;
					if (pass == 0)
					{
						if (LavaFedFall(plan, fed, ri, room, m, axis * free, e, lip, poolAt, pool, poolFloor, poolTop, rift))
						{
							return;
						}
						continue;
					}
					float side = Unit(h, 2) < 0.5f ? -1f : 1f;
					Vector3 vent = m + axis * (free * lip) + e * (side * (rift.Width + 0.1f));
					if (!LavaClear(new Vector3(vent.x, lipFloor, vent.z), 1f, ri))
					{
						continue;
					}
					vent.y = lipFloor - 1.2f;
					if (HeldVoid(vent) < 0f || HeldVoid(vent - e * (side * 0.5f)) >= 0f)
					{
						continue;
					}
					LavaPool(plan, poolAt, pool, poolFloor, poolTop);
					plan.Bodies.Add(new CaveLavaBody { Kind = CaveLavaKind.Fall, Path = new[] { vent, poolAt }, Width = Mathf.Clamp(rift.Narrow, 0.8f, 1.6f), Passage = -1, Chamber = room, Pit = -1 });
					return;
				}
			}
		}

		/// <summary>Whether a plunge pool fits a chasm's floor at a point: floor there and along it either way (the chasm's own), measured; its bank and top.</summary>
		private bool LavaPoolFits(Vector3 at, float radius, float bottom, Vector3 axis, out float floor, out float top)
		{
			floor = float.MaxValue;
			top = float.MinValue;
			foreach (float s in new[] { 0f, -radius, radius })
			{
				float found = HeldFloor(at + axis * s, bottom + 1.5f, 4f);
				if (float.IsNaN(found))
				{
					return false;
				}
				floor = Mathf.Min(floor, found);
				top = Mathf.Max(top, found);
			}
			top += 0.3f;
			return true;
		}

		/// <summary>The river <paramref name="fed"/> run on over its chamber's floor to the chasm's lip and over it into a pool; false where its way there is not clear.</summary>
		private bool LavaFedFall(CaveLavaPlan plan, int fed, int ri, int room, Vector3 m, Vector3 toFree, Vector3 e, float lip, Vector3 poolAt, float pool,
			float poolFloor, float poolTop, in CaveRift rift)
		{
			CaveLavaBody river = plan.Bodies[fed];
			CaveChamber chamber = Chambers[room];
			Vector3 from = river.Path[river.Path.Length - 1];
			float side = Mathf.Sign(Vector3.Dot(from - m, e));
			if (side == 0f)
			{
				side = 1f;
			}
			Vector3 edge = m + toFree * lip + e * (side * (rift.Width - 0.3f));
			int steps = Mathf.CeilToInt(Plan2(from, edge));
			if (steps < 2)
			{
				return false;
			}
			var path = new List<Vector3>(river.Path);
			var banks = new List<float>(river.Banks);
			var tops = new List<float>(river.Tops);
			float surface = from.y, bank = banks[banks.Count - 1], top = tops[tops.Count - 1];
			for (int s = 1; s <= steps; s++)
			{
				Vector3 q = Vector3.Lerp(from, edge, s / (float)steps);
				q.y = FloorOf(chamber, q);
				// Clear of everything but the chasm it pours into (its last stretch lies over the lip).
				if (!LavaClear(q, 0.5f * river.Width, ri, fed))
				{
					return false;
				}
				float found = HeldFloor(q, q.y + 0.6f);
				bool overLip = PlanToSegment(q, rift.A, rift.B, out _) < rift.Width + 0.5f;
				if (float.IsNaN(found) && !overLip)
				{
					return false;
				}
				if (!overLip)
				{
					bank = found;
					top = found + 0.3f;
				}
				surface = Mathf.Min(surface, bank - CaveLava.Drop);
				if (bank - surface > CaveLava.MaxSink)
				{
					return false;
				}
				path.Add(new Vector3(q.x, surface, q.z));
				banks.Add(bank);
				tops.Add(top);
			}
			if (surface - poolAt.y < 3f)
			{
				return false;
			}
			river.Path = path.ToArray();
			river.Banks = banks.ToArray();
			river.Tops = tops.ToArray();
			plan.Bodies[fed] = river;
			for (int k = path.Count - steps; k < path.Count; k++)
			{
				lavaLaid.Add((new Vector3(path[k].x, banks[k], path[k].z), 0.5f * river.Width, fed));
			}
			LavaPool(plan, poolAt, pool, poolFloor, poolTop);
			plan.Bodies.Add(new CaveLavaBody
			{
				Kind = CaveLavaKind.Fall,
				Path = new[] { new Vector3(edge.x, surface, edge.z), poolAt },
				Width = Mathf.Min(river.Width, Mathf.Clamp(2f * rift.Narrow, 0.8f, 2f)),
				Passage = river.Passage,
				Chamber = room,
				Pit = -1,
			});
			return true;
		}

		/// <summary>A plunge pool in a chasm's floor: a basin cut under that floor, its surface the lake's.</summary>
		private void LavaPool(CaveLavaPlan plan, Vector3 at, float radius, float floor, float top)
		{
			int index = plan.Bodies.Count;
			plan.Bodies.Add(new CaveLavaBody { Kind = CaveLavaKind.Lake, Path = new[] { at }, Width = radius, Banks = new[] { floor }, Tops = new[] { top }, Passage = -1, Chamber = -1, Pit = -1 });
			lavaLaid.Add((new Vector3(at.x, floor, at.z), radius, index));
		}

		// ── Lakes ─────────────────────────────────────────────────

		/// <summary>The broadest lava lake (its radius), metres: ~30 m across in a great cavern (2026-10-21).</summary>
		public const float LakeMost = 15f;
		/// <summary>The least the deepest cavern's lake is asked to be (its radius), metres, where its floor gives it room.</summary>
		private const float DeepLakeLeast = 7.5f;
		/// <summary>
		/// How steeply a lake's basin is graded back over its rim (rise over run, about 29°: walked down to the lava), and how far
		/// the floor round a lake may rise over its lowest rim: the bank cut down through it toward the lake.
		/// </summary>
		private const float BankGrade = 0.55f, BankRise = 4f;

		/// <summary>How far over the deepest great chamber's floor another may lie and still be one of the deepest, metres.</summary>
		private const float DeepBand = 12f;

		/// <summary>
		/// Lava lakes in chambers, before anything else is laid (2026-10-21: "Stacked tubes + big lakes"): the bottom of the system
		/// always holds one as broad as it fits (up to <see cref="LakeMost"/>): the broadest spot over the floors of its deepest
		/// great chambers (the caverns and halls within <see cref="DeepBand"/> of the deepest, the site's own last, its camp kept);
		/// then the other chambers by the seed (most in a cavern or a hall, fewer in rooms), deepest first, one to a chamber, up
		/// to one for every four chambers. Each a basin off every way and kept place, floor all round it, its high side graded
		/// back (<see cref="BankGrade"/>).
		/// </summary>
		private void LavaLakes(CaveLavaPlan plan)
		{
			List<int> deep = DeepChambers();
			int made = 0, most = Mathf.Clamp(1 + Chambers.Count / 4, 1, 10), deepest = -1;
			Vector3 bestAt = default;
			float bestRadius = -1f;
			foreach (int ci in deep)
			{
				CaveChamber c = Chambers[ci];
				uint h = Mix(unchecked((uint)Seed), 0x1A7B0u + (uint)ci * 0x9E37u);
				float wanted = Mathf.Clamp(Mathf.Min(c.Radii.x, c.Radii.z) * Mathf.Lerp(0.3f, 0.45f, Unit(h, 2)), DeepLakeLeast, LakeMost);
				if (wanted <= bestRadius)
				{
					continue;
				}
				if (BroadestLake(ci, wanted, Unit(h, 3), bestRadius, out Vector3 at, out float r))
				{
					deepest = ci;
					bestAt = at;
					bestRadius = r;
				}
			}
			if (deepest >= 0 && Room(plan) && LavaLake(plan, deepest, bestAt, bestRadius))
			{
				made++;
			}
			// The rest, deepest first (the lava pools at the bottom of the system), ties in the chambers' order.
			var order = new List<int>();
			for (int ci = 0; ci < Chambers.Count; ci++)
			{
				if (ci != deepest)
				{
					order.Add(ci);
				}
			}
			order.Sort((x, y) => Chambers[x].Floor != Chambers[y].Floor ? Chambers[x].Floor.CompareTo(Chambers[y].Floor) : x.CompareTo(y));
			foreach (int ci in order)
			{
				if (made >= most || !Room(plan))
				{
					break;
				}
				CaveChamber c = Chambers[ci];
				float small = Mathf.Min(c.Radii.x, c.Radii.z);
				if (small < 5f || c.Kind == CaveChamberKind.Stope)
				{
					continue;
				}
				uint h = Mix(unchecked((uint)Seed), 0x1A7B0u + (uint)ci * 0x9E37u);
				float odds = c.Kind == CaveChamberKind.Cavern ? 0.75f : 0.45f;
				if (Unit(h, 1) >= odds)
				{
					continue;
				}
				float wanted = Mathf.Clamp(small * Mathf.Lerp(0.16f, 0.32f, Unit(h, 2)), LakeLeast, 12f);
				made += FirstLake(plan, ci, wanted, Unit(h, 3)) ? 1 : 0;
			}
		}

		/// <summary>
		/// The deepest great chambers: every cavern and hall whose floor lies within <see cref="DeepBand"/> of the deepest of them,
		/// broadest first, the site's own (its camp at the middle) after the rest.
		/// </summary>
		public List<int> DeepChambers()
		{
			var deep = new List<int>();
			float lowest = float.MaxValue;
			foreach (CaveChamber c in Chambers)
			{
				if (c.Kind == CaveChamberKind.Cavern || c.Kind == CaveChamberKind.Hall)
				{
					lowest = Mathf.Min(lowest, c.Floor);
				}
			}
			var own = new HashSet<int>();
			foreach (CaveMouth m in Mouths)
			{
				if (TryChamberOf(m.SiteId, out CaveChamber chamber))
				{
					own.Add(Chambers.FindIndex(c => c.Centre == chamber.Centre && c.Kind == chamber.Kind));
				}
			}
			for (int ci = 0; ci < Chambers.Count; ci++)
			{
				CaveChamber c = Chambers[ci];
				if ((c.Kind == CaveChamberKind.Cavern || c.Kind == CaveChamberKind.Hall) && c.Floor <= lowest + DeepBand)
				{
					deep.Add(ci);
				}
			}
			float Score(int ci) => Mathf.Min(Chambers[ci].Radii.x, Chambers[ci].Radii.z) * (own.Contains(ci) ? 0.5f : 1f);
			deep.Sort((x, y) => Score(x) != Score(y) ? Score(y).CompareTo(Score(x)) : x.CompareTo(y));
			return deep;
		}

		/// <summary>The spot of a chamber's floor (on rings round its middle, from a turn its seed picks) where a lake fits broadest, over <paramref name="better"/>.</summary>
		private bool BroadestLake(int ci, float wanted, float turnShare, float better, out Vector3 best, out float bestRadius)
		{
			CaveChamber c = Chambers[ci];
			float turn = 360f * turnShare;
			best = default;
			bestRadius = better;
			for (int ring = 0; ring < 6 && bestRadius < wanted - 1e-3f; ring++)
			{
				float share = 0.3f + 0.11f * ring;
				for (int k = 0; k < 16 && bestRadius < wanted - 1e-3f; k++)
				{
					Vector3 dir = LavaDir(turn + 22.5f * k + 11.25f * ring);
					Vector3 at = c.Centre + dir * (FloorReach(c, dir) * share);
					// Measured on the field as it would be laid (floor all round, its bank still clear), narrowed until it is.
					for (float r = LakeFits(ci, at, wanted, bestRadius); r > bestRadius && r >= LakeLeast - 1e-3f; r -= 0.5f)
					{
						if (LakeMeasures(ci, at, r, out _, out _))
						{
							best = at;
							bestRadius = r;
							break;
						}
					}
				}
			}
			return bestRadius > better;
		}

		/// <summary>A lake at the first spot of a chamber's floor (on rings round its middle, from a turn its seed picks) that takes one, as broad as fits there.</summary>
		private bool FirstLake(CaveLavaPlan plan, int ci, float wanted, float turnShare)
		{
			CaveChamber c = Chambers[ci];
			float turn = 360f * turnShare;
			for (int ring = 0; ring < 6; ring++)
			{
				float share = 0.3f + 0.11f * ring;
				for (int k = 0; k < 16; k++)
				{
					Vector3 dir = LavaDir(turn + 22.5f * k + 11.25f * ring);
					Vector3 at = c.Centre + dir * (FloorReach(c, dir) * share);
					float r = LakeFits(ci, at, wanted, -1f);
					if (r > 0f && LavaLake(plan, ci, at, r))
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>The smallest lava lake laid in a chamber (its radius), metres.</summary>
		private const float LakeLeast = 1.8f;

		/// <summary>How far the bank of a lake graded back over its rim reaches past it, for a rise of the floor round it, metres.</summary>
		private static float BankReach(float rise) => Mathf.Max(0f, rise) / BankGrade;

		/// <summary>
		/// The broadest radius a lake fits at a point of a chamber's floor, up to <paramref name="wanted"/> and over
		/// <paramref name="better"/> (−1 where none does): clear of everything with its graded bank, floor all round it in the
		/// chamber's outline, the floor round it rising no more than <see cref="BankRise"/>.
		/// </summary>
		private float LakeFits(int ci, Vector3 at, float wanted, float better)
		{
			CaveChamber c = Chambers[ci];
			at.y = FloorOf(c, at);
			for (float r = wanted; r >= LakeLeast - 1e-3f && r > better; r = r > LakeLeast ? Mathf.Max(LakeLeast, r - 0.5f) : 0f)
			{
				float planned = float.MaxValue, plannedHigh = float.MinValue;
				for (int k = 0; k < 12; k++)
				{
					Vector3 rim = at + LavaDir(30f * k) * (r + 0.6f);
					float y = FloorOf(c, rim);
					planned = Mathf.Min(planned, y);
					plannedHigh = Mathf.Max(plannedHigh, y);
				}
				float bank = BankReach(plannedHigh - planned + 0.4f);
				if (plannedHigh - planned > BankRise)
				{
					continue;
				}
				bool fits = LavaClear(at, r + bank + CaveSystem.WayLane - 0.4f);
				for (int k = 0; k < 12 && fits; k++)
				{
					Vector3 rim = at + LavaDir(30f * k) * (r + 0.6f + bank);
					rim.y = FloorOf(c, rim);
					fits = OverFloor(c, rim, -0.5f);
				}
				if (fits)
				{
					return r;
				}
			}
			return -1f;
		}

		/// <summary>
		/// A lake of <paramref name="radius"/> at a point of a chamber's floor (<see cref="LakeFits"/>), measured on the field: floor
		/// all round it, rising no more than <see cref="BankRise"/>, its surface <see cref="CaveLava.Drop"/> under the lowest rim.
		/// </summary>
		private bool LavaLake(CaveLavaPlan plan, int ci, Vector3 at, float radius)
		{
			if (!LakeMeasures(ci, at, radius, out float low, out float high))
			{
				return false;
			}
			int index = plan.Bodies.Count;
			var centre = new Vector3(at.x, low - CaveLava.Drop, at.z);
			plan.Bodies.Add(new CaveLavaBody { Kind = CaveLavaKind.Lake, Path = new[] { centre }, Width = radius, Banks = new[] { low }, Tops = new[] { high + 0.4f }, Passage = -1, Chamber = ci, Pit = -1 });
			lavaLaid.Add((at, radius + BankReach(high + 0.4f - low), index));
			return true;
		}

		/// <summary>
		/// A lake's rim and middle measured on the field without the lava: floor all round it (its lowest <paramref name="low"/>,
		/// highest <paramref name="high"/>) rising no more than <see cref="BankRise"/>, over any water the cave stands in, and its
		/// bank as those floors grade it still clear of everything and on the chamber's floor.
		/// </summary>
		private bool LakeMeasures(int ci, Vector3 at, float radius, out float low, out float high)
		{
			CaveChamber c = Chambers[ci];
			at.y = FloorOf(c, at);
			low = float.MaxValue;
			high = float.MinValue;
			for (int k = 0; k <= 12; k++)
			{
				Vector3 q = k == 12 ? at : at + LavaDir(30f * k) * (radius + 0.6f);
				float found = HeldFloor(q, FloorOf(c, q) + 0.8f);
				if (float.IsNaN(found))
				{
					return false;
				}
				low = Mathf.Min(low, found);
				high = Mathf.Max(high, found);
			}
			if (high - low > BankRise || !float.IsNaN(WaterLevel) && Origin.y + low < WaterLevel + 1f)
			{
				return false;
			}
			float bank = BankReach(high + 0.4f - low);
			if (!LavaClear(at, radius + bank + CaveSystem.WayLane - 0.4f))
			{
				return false;
			}
			for (int k = 0; k < 12; k++)
			{
				Vector3 rim = at + LavaDir(30f * k) * (radius + 0.6f + bank);
				rim.y = FloorOf(c, rim);
				if (!OverFloor(c, rim, -0.5f))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// The variety pass's basins (potholes and sinkholes) deep enough to hold lava under their rims, by the seed: filled to
		/// <see cref="CaveLava.Drop"/> under the lowest of the floor round the rim, their own sides its shore. Nothing is cut: the basin is the bed.
		/// </summary>
		private void LavaBasins(CaveLavaPlan plan)
		{
			for (int pi = 0; pi < Pits.Count && Room(plan); pi++)
			{
				CavePit pit = Pits[pi];
				if (pit.Kind != CavePitKind.Pothole && pit.Kind != CavePitKind.Sinkhole || pit.Hole < 0 || pit.Hole >= Hollows.Count
					|| Hollows[pit.Hole].Kind != CaveHollowKind.Bowl || !float.IsNaN(pit.Water)
					|| Unit(Mix(unchecked((uint)Seed), 0x1A7B5u + (uint)pi * 0x9E37u), 1) >= 0.6f)
				{
					continue;
				}
				CaveHollow bowl = Hollows[pit.Hole];
				// Off the mouths and the kept middles (the variety pass laid it off the ways already).
				bool kept = false;
				foreach ((Vector3 at, float radius, float height) in lavaKeeps)
				{
					kept |= Plan2(bowl.A, at) < radius + bowl.Radius && Mathf.Abs(bowl.A.y - at.y) < height;
				}
				if (kept)
				{
					continue;
				}
				// The floor round its rim as the field cuts it (the lowest is where it would spill).
				float spill = bowl.Radius + Mathf.Max(0f, bowl.Top - bowl.A.y) / BowlGrade;
				float rim = float.MaxValue;
				for (int k = 0; k < 8; k++)
				{
					float found = HeldFloor(bowl.A + LavaDir(45f * k) * (spill + 0.3f), bowl.A.y + 1f);
					rim = float.IsNaN(found) ? rim : Mathf.Min(rim, found);
				}
				float bed = bowl.A.y - bowl.Depth, surface = Mathf.Min(bowl.A.y, rim) - CaveLava.Drop;
				// Deep enough that its lava stands clear of the bowl's rough floor (at 0.3 m the rock's grain broke through it).
				if (rim == float.MaxValue || surface - bed < 0.6f)
				{
					continue;
				}
				float flat = Mathf.Max(0.3f * bowl.Radius, bowl.Radius - bowl.Depth / BowlGrade);
				float reach = Mathf.Min(bowl.Radius, flat + (surface - bed) / BowlGrade);
				// Nothing of another level under it (2026-10-21: in stacked tubes a basin may stand over a lower one's way).
				if (LavaOverLower(new Vector3(bowl.A.x, Mathf.Min(bowl.A.y, rim), bowl.A.z), reach))
				{
					continue;
				}
				int chamber = Chambers.FindIndex(c => OverFloor(c, bowl.A, 0.5f));
				int index = plan.Bodies.Count;
				plan.Bodies.Add(new CaveLavaBody { Kind = CaveLavaKind.Lake, Path = new[] { new Vector3(bowl.A.x, surface, bowl.A.z) }, Width = reach, Passage = -1, Chamber = chamber, Pit = pi });
				lavaLaid.Add((bowl.A, bowl.Radius, index));
			}
		}
	}
}
#endif
