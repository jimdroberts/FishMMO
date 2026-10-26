#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Ways through the rock (2026-10-18: "Caves should connect to/try to connect to each other so pathways through mountains
	/// are valid"): the links that join two caves' systems, and a second mouth on the far side of a cave's own hill with a
	/// passage from it to the first mouth's trunk, so the hill can be walked through.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One walker.</b> Every link is walked by <see cref="WalkTo"/>: toward its end's floor no steeper than a walk, winding
	/// back on itself where the drop is too much for the way left, under the rock all the way, round every void at another
	/// floor. The ends are chambers (their sloping floors) or the nodes of a trunk.
	/// </para>
	/// <para>
	/// <b>Junctions on a passage.</b> A link meets a trunk's node by a level stub square out of the trunk's side
	/// (<see cref="Stub"/>), out past the void round it: all its climbing and falling is done clear of the trunk. A link that
	/// came in at an angle still climbing cut across the sloping trunk beside the node at another floor, and stepped down
	/// onto it (a 50° floor).
	/// </para>
	/// <para>
	/// <b>Joins that happen.</b> A join tried only the host's deepest chamber against the other's nearest, and two caves of a
	/// kind were sited 500–600 m apart against a 420 m reach: next to none joined. Now every chamber and every trunk past its
	/// mouth's zone is a candidate end on both sides, ranked by how far apart they lie across and in depth (a shallow way from
	/// mouth to mouth ranks with a deep one between caverns), and the nearest few pairs are walked in turn.
	/// </para>
	/// <para>
	/// <b>Through the hill.</b> Where the hill a cave is cut into has another face within <see cref="ThroughReach"/> (the far
	/// foot below a crest that stands over both, a standable foot under a face looking away from the first), a second mouth
	/// opens there and its passage is walked in to the first mouth's trunk: from either mouth the trunk and that passage are
	/// one walkable way through. Its own random stream, so the system it is added to is unchanged.
	/// </para>
	/// </remarks>
	public sealed partial class CaveSystem
	{
		// ── Walking a link ────────────────────────────────────────

		/// <summary>An end a link is walked from or to: a chamber (its sloping floor) or one node of a passage (its floor there).</summary>
		private struct LinkEnd
		{
			public Vector3 Centre;
			public bool IsChamber;
			public CaveChamber Chamber;
			/// <summary>A node's floor and bore (a chamber's are its own).</summary>
			public float NodeFloor, NodeRadius;
			/// <summary>The way its passage runs at a node (plan, unit).</summary>
			public Vector3 Run;

			public float FloorAt(Vector3 p) => IsChamber ? FloorOf(Chamber, p) : NodeFloor;

			/// <summary>
			/// How far out a point lies, in the end's own measure: a chamber's ellipse (1 on its rim, along its heading and
			/// across), a node's bore (1 a bore's width from its centre); plan only.
			/// </summary>
			public float Measure(Vector3 p)
			{
				var d = new Vector2(p.x - Centre.x, p.z - Centre.z);
				if (!IsChamber)
				{
					return d.magnitude / Mathf.Max(0.5f, NodeRadius);
				}
				float yr = Chamber.Yaw * Mathf.Deg2Rad;
				float along = d.x * Mathf.Sin(yr) + d.y * Mathf.Cos(yr), across = d.x * Mathf.Cos(yr) - d.y * Mathf.Sin(yr);
				float a = along / Mathf.Max(0.5f, Chamber.Radii.x), b = across / Mathf.Max(0.5f, Chamber.Radii.z);
				return Mathf.Sqrt(a * a + b * b);
			}

			/// <summary>Where a link arrives at it: well inside a chamber (six tenths of the way out), on a node's own bore.</summary>
			public float ArriveWithin => IsChamber ? 0.6f : 1.1f;

			/// <summary>
			/// How far out of a chamber (in its measure) a link leaving it may cross the void round it at another floor (its
			/// walls' rounding), and arriving at it, twice that: its floor runs on from the link's. None at a node (a stub's tip
			/// in the rock beside the passage, <see cref="Stub"/>).
			/// </summary>
			public float Leaves => IsChamber ? 1.3f : 0f;
			public float Arrives => IsChamber ? 2.6f : 0f;

			public static LinkEnd Of(in CaveChamber chamber) => new LinkEnd { Centre = chamber.Centre, IsChamber = true, Chamber = chamber };

			public static LinkEnd Of(in CaveNode node, Vector3 run) => new LinkEnd { Centre = node.Centre, NodeFloor = node.Floor, NodeRadius = node.Radius, Run = run };
		}

		/// <summary>
		/// The rock over a link's bore at <paramref name="n"/> (system frame, its centre), metres: the least ground over its middle,
		/// over both its sides nine tenths of its radius out square to its run from <paramref name="previous"/>, and over the middle
		/// and both sides half way back to that, less its top (the bore's roof and the noise's amplitude): as a passage's walker
		/// reckons it (Walker.Cover), with the sides half way back too.
		/// The middle alone let a link run along a steep hillside's contour with metres over its middle and its downhill wall out
		/// of the ground (2026-10-26, Moron 2: 3–5 m over the middle, the downhill side 2 m out under a 60° face): the shell
		/// stops a skin under the ground, so the cave lay open to the sky there.
		/// </summary>
		private static float BoreCover(Func<Vector3, float> groundAt, Vector3 n, float radius, float flat, float amp, Vector3 previous)
		{
			float top = n.y + radius * flat + amp;
			var run = new Vector3(n.x - previous.x, 0f, n.z - previous.z);
			Vector3 side = run.sqrMagnitude > 1e-8f ? new Vector3(run.z, 0f, -run.x).normalized * (0.9f * radius) : Vector3.zero;
			Vector3 mid = 0.5f * (n + previous);
			float cover = Mathf.Min(groundAt(n), groundAt(mid)) - top;
			cover = Mathf.Min(cover, Mathf.Min(groundAt(n + side), groundAt(n - side)) - top);
			// And either side half way back: a face falling across the way between two nodes.
			return Mathf.Min(cover, Mathf.Min(groundAt(mid + side), groundAt(mid - side)) - top);
		}

		/// <summary>
		/// A link's first node, leaving a chamber toward <paramref name="toward"/>: seven tenths of the way out along that bearing
		/// (inside its ellipse, however long and narrow the hall).
		/// </summary>
		private CaveNode StartIn(in LinkEnd chamber, Vector3 toward, float radius)
		{
			Vector3 centre = chamber.Centre;
			var dir = new Vector3(toward.x - centre.x, 0f, toward.z - centre.z);
			dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
			float yr = chamber.Chamber.Yaw * Mathf.Deg2Rad;
			float a = (dir.x * Mathf.Sin(yr) + dir.z * Mathf.Cos(yr)) / Mathf.Max(0.5f, chamber.Chamber.Radii.x);
			float b = (dir.x * Mathf.Cos(yr) - dir.z * Mathf.Sin(yr)) / Mathf.Max(0.5f, chamber.Chamber.Radii.z);
			centre += dir * (0.7f / Mathf.Max(1e-4f, Mathf.Sqrt(a * a + b * b)));
			float floor = FloorOf(chamber.Chamber, centre);
			centre.y = floor + FloorDepth * radius * Flat;
			return new CaveNode { Centre = centre, Radius = radius, Floor = floor, Along = 0f };
		}

		/// <summary>
		/// The level stub a link meets a passage's node by (or leaves it by): straight out of the passage's side toward
		/// <paramref name="toward"/>, square to it, at the node's floor, out past the void round it (<paramref name="clearance"/>
		/// and a step more), so the link's climbing or falling is all done clear of the passage. From the node out to its tip;
		/// null where the rock over it is thin or its tip would meet another void at another floor (inside the band round the
		/// passage, the passage's own floors either side of the node read as another's).
		/// </summary>
		private List<CaveNode> Stub(in LinkEnd node, Vector3 toward, float radius, float clearance, CaveGround ground, Func<Vector3, float, bool> crosses)
			=> Stub(node, toward, radius, clearance, ground, crosses, out _);

		private List<CaveNode> Stub(in LinkEnd node, Vector3 toward, float radius, float clearance, CaveGround ground, Func<Vector3, float, bool> crosses, out string problem)
		{
			problem = null;
			var side = new Vector3(node.Run.z, 0f, -node.Run.x);
			var to = new Vector3(toward.x - node.Centre.x, 0f, toward.z - node.Centre.z);
			if (Vector3.Dot(side, to) < 0f)
			{
				side = -side;
			}
			float step = Mathf.Clamp(0.9f * radius, 2.5f, 5f);
			float reach = node.NodeRadius + clearance + step;
			int count = Mathf.CeilToInt(reach / step);
			var stub = new List<CaveNode>(count + 1);
			for (int i = 0; i <= count; i++)
			{
				float d = Mathf.Min(reach, i * step);
				Vector3 c = node.Centre + side * d;
				c.y = node.NodeFloor + FloorDepth * radius * Flat;
				if (i > 0)
				{
					float cover = BoreCover(p => ground.At(p.x + Origin.x, p.z + Origin.z) - Origin.y, c, radius, Flat, Amp, stub[i - 1].Centre);
					problem = cover < MinCover ? $"the rock over a junction's stub {d:0} m out is {cover:0.#} m thin"
						: !ground.Contains(c.x + Origin.x, c.z + Origin.z, radius + 12f) ? "a junction's stub runs off the ground"
						: d > node.NodeRadius + clearance && crosses(c, node.NodeFloor) ? $"a junction's stub {d:0} m out crosses another passage"
						: NearChasmOrClimb(c, radius + 1f) ? "a junction's stub meets a chasm or a climb" : null;
					if (problem != null)
					{
						return null;
					}
				}
				stub.Add(new CaveNode { Centre = c, Radius = radius, Floor = node.NodeFloor, Along = d });
			}
			return stub;
		}

		/// <summary>A stub's tip as the end a link is walked to: a point in the rock at the node's floor, arrived at on its own bore.</summary>
		private static LinkEnd TipOf(List<CaveNode> stub, Vector3 run)
		{
			CaveNode tip = stub[stub.Count - 1];
			return LinkEnd.Of(tip, run);
		}

		/// <summary>One passage of the nodes of several, in order, the joins' repeated nodes dropped and its lengths measured afresh.</summary>
		private static CavePassage Joined(CavePassageKind kind, params IEnumerable<CaveNode>[] parts)
		{
			var passage = new CavePassage { Kind = kind };
			float along = 0f;
			foreach (IEnumerable<CaveNode> part in parts)
			{
				if (part == null)
				{
					continue;
				}
				foreach (CaveNode n in part)
				{
					if (passage.Nodes.Count > 0)
					{
						float d = Vector3.Distance(passage.Nodes[passage.Nodes.Count - 1].Centre, n.Centre);
						if (d < 0.05f)
						{
							continue;
						}
						along += d;
					}
					CaveNode m = n;
					m.Along = along;
					passage.Nodes.Add(m);
				}
			}
			return passage;
		}

		/// <summary>
		/// A link walked from <paramref name="start"/> to <paramref name="end"/> (both in this system's frame): toward the end's
		/// floor no steeper than <see cref="WalkPitch"/> less 2° (a link meets floors at both ends, and each junction's crease
		/// steepens it), winding back on itself (a switchback) where the drop is too much for the way left, under
		/// <see cref="MinCover"/> of rock past <paramref name="zone"/>, round every void <paramref name="blocked"/> says it may not
		/// cross (tested at each node and half way to it) and every chasm and climb, never under <paramref name="minFloor"/>. Its
		/// nodes from the start, or null where it is blocked or wanders past <paramref name="maxLength"/>, with
		/// <paramref name="problem"/> saying why.
		/// </summary>
		/// <param name="inward">For a link out of a mouth, the heading into the hill: it keeps within 60° of it until past the
		/// mouth's zone, so it goes into the hill and never along its face. NaN for none.</param>
		/// <param name="level">Metres it runs level from its start (a mouth's threshold); 0 for none.</param>
		/// <param name="zone">Metres from its start within which its roof may be thin (a mouth's zone); 0 for none.</param>
		private List<CaveNode> WalkTo(CaveNode start, float heading, LinkEnd end, float maxLength, DeterministicRNG rng, CaveGround ground,
			Func<Vector3, float, bool> blocked, float minFloor, float inward, float level, float zone, out string problem)
		{
			problem = null;
			float r = start.Radius;
			float amp = Amp;
			float step = Mathf.Clamp(0.9f * r, 2.5f, 5f);
			float steepest = WalkPitch - 2f;
			float GroundAt(Vector3 local) => ground.At(local.x + Origin.x, local.z + Origin.z) - Origin.y;
			var nodes = new List<CaveNode> { start };
			Vector3 centre = start.Centre;
			float floor = start.Floor, along = 0f;
			for (int guard = 0; guard < 800 && along < maxLength; guard++)
			{
				var toEnd = new Vector3(end.Centre.x - centre.x, 0f, end.Centre.z - centre.z);
				float gap = end.FloorAt(centre) - floor;
				float measure = end.Measure(centre);
				if (measure <= end.ArriveWithin && Mathf.Abs(gap) <= 0.4f)
				{
					return nodes;
				}
				bool entering = along + step <= level;
				float aim = Mathf.Atan2(toEnd.x, toEnd.z) * Mathf.Rad2Deg;
				// The way left to the end's arrival rim, along the line to its centre.
				float remaining = Mathf.Max(step, toEnd.magnitude * (1f - end.ArriveWithin / Mathf.Max(end.ArriveWithin, measure)));
				if (!entering && Mathf.Abs(gap) > remaining * 0.33f)
				{
					aim += 75f;
				}
				heading += Mathf.Clamp(DeltaAngle(heading, aim), -20f, 20f) + rng.Range(-8f, 8f);
				bool fromMouth = !float.IsNaN(inward) && along < zone;
				if (fromMouth)
				{
					heading = inward + Mathf.Clamp(DeltaAngle(inward, heading), -60f, 60f);
				}
				float pitch = entering ? 0f : Mathf.Clamp(Mathf.Atan2(gap, remaining) * Mathf.Rad2Deg, -steepest, steepest);
				float run = step * Mathf.Cos(pitch * Mathf.Deg2Rad);
				bool found = false;
				foreach (float turn in LinkTurns)
				{
					float h = heading + turn;
					if (fromMouth && Mathf.Abs(DeltaAngle(inward, h)) > 60f)
					{
						continue;
					}
					float hr = h * Mathf.Deg2Rad;
					float nf = floor + step * Mathf.Sin(pitch * Mathf.Deg2Rad);
					var n = new Vector3(centre.x + Mathf.Sin(hr) * run, nf + FloorDepth * r * Flat, centre.z + Mathf.Cos(hr) * run);
					if (!ground.Contains(n.x + Origin.x, n.z + Origin.z, r + 12f))
					{
						continue;
					}
					float nextAlong = along + step;
					// Thin over a mouth's threshold, the bore's middle under the ground through the rest of its zone, then the rock's cover.
					// Past the zone over the whole bore, its sides too (BoreCover); within it over its middle only (the collar's).
					float need = nextAlong >= zone ? MinCover : nextAlong <= level ? float.NegativeInfinity : -(r * Flat + amp);
					float cover = nextAlong >= zone ? BoreCover(GroundAt, n, r, Flat, amp, centre) : GroundAt(n) - (n.y + r * Flat + amp);
					if (cover < need)
					{
						if (nextAlong < zone || entering)
						{
							continue;
						}
						// Too near the surface here: go deeper first.
						nf = floor - step * Mathf.Sin(steepest * Mathf.Deg2Rad);
						n.y = nf + FloorDepth * r * Flat;
						if (BoreCover(GroundAt, n, r, Flat, amp, centre) < MinCover)
						{
							continue;
						}
					}
					if (nf < minFloor || blocked(n, nf) || blocked(0.5f * (centre + n), 0.5f * (floor + nf)) || NearChasmOrClimb(n, r + 1f)
						|| Overlaps(nodes, n, nf, r))
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
					problem = $"blocked {along:0} m along (cover, the ground's edge, the sea's reach or another passage at another floor), floor {floor:0.#}, gap {gap:0.#}";
					return null;
				}
				along += step;
				nodes.Add(new CaveNode { Centre = centre, Radius = r * rng.Range(0.92f, 1.08f), Floor = floor, Along = along });
			}
			problem = $"it wandered {along:0} m without arriving";
			return null;
		}

		/// <summary>
		/// Whether a link's next node would meet its own way back along it at another floor: a switchback turned too tight,
		/// its bore over or under the run it came up (the field it is walked against holds only what was there before it).
		/// The last two nodes are its own run on, not a crossing.
		/// </summary>
		private static bool Overlaps(List<CaveNode> nodes, Vector3 n, float floor, float radius)
		{
			for (int i = 0; i < nodes.Count - 2; i++)
			{
				CaveNode o = nodes[i];
				float reach = radius + o.Radius + 2f;
				if (Mathf.Abs(o.Floor - floor) > 0.5f && new Vector2(o.Centre.x - n.x, o.Centre.z - n.z).sqrMagnitude < reach * reach)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// The places a link may leave or reach a system (shifted into this system's frame by <paramref name="shift"/>): every
		/// chamber, and every few nodes of each trunk past its mouth's zone.
		/// </summary>
		private static List<LinkEnd> EndsOf(CaveSystem system, Vector3 shift, float spacing = TrunkEndSpacing, bool chambers = true, bool firstTrunkOnly = false)
		{
			var ends = new List<LinkEnd>();
			if (chambers)
			{
				foreach (CaveChamber c in system.Chambers)
				{
					CaveChamber moved = c;
					moved.Centre += shift;
					moved.Floor += shift.y;
					ends.Add(LinkEnd.Of(moved));
				}
			}
			foreach (CavePassage p in system.Passages)
			{
				if (p.Kind != CavePassageKind.Trunk || p.Nodes.Count < 2)
				{
					continue;
				}
				// Its mouth's zone: the trunk's thin-roofed first stretch, which a link would break out of.
				float zone = system.ZoneAt(p.Nodes[0].Centre);
				float next = zone + 4f;
				for (int i = 0; i < p.Nodes.Count; i++)
				{
					CaveNode n = p.Nodes[i];
					if (n.Along < next)
					{
						continue;
					}
					Vector3 run = p.Nodes[Mathf.Min(p.Nodes.Count - 1, i + 1)].Centre - p.Nodes[Mathf.Max(0, i - 1)].Centre;
					run.y = 0f;
					CaveNode moved = n;
					moved.Centre += shift;
					moved.Floor += shift.y;
					ends.Add(LinkEnd.Of(moved, run.sqrMagnitude > 1e-6f ? run.normalized : Vector3.forward));
					next = n.Along + spacing;
				}
				if (firstTrunkOnly)
				{
					break;
				}
			}
			return ends;
		}

		/// <summary>How far apart along a trunk its nodes are offered as a link's end, metres.</summary>
		private const float TrunkEndSpacing = 16f;

		/// <summary>The zone of the mouth nearest a point (a trunk's first node lies at its own mouth).</summary>
		private float ZoneAt(Vector3 p) => Mouths.Count > 0 ? Mouths[NearestMouth(p, out _)].Zone : 0f;

		/// <summary>The most pairs of ends a join walks before it gives up.</summary>
		private const int MaxJoinTries = 24;

		/// <summary>
		/// How near another passage or chamber (its smooth void) a link's bore may pass at another floor, metres: two metres of
		/// rock between them and half the two walls' noise (it seldom bulges out on both at once; with all of it, two caves
		/// 140 m apart found no link between them).
		/// </summary>
		private static float Clearance(float radius, float amps) => radius + 2f + 0.5f * amps;

		/// <summary>
		/// Joins <paramref name="other"/> (a system planned before, from another cave site) into this one: a link passage between
		/// the nearest pair of places on the two (chambers, trunks past their zones) that a walkable way under the rock fits,
		/// trying the nearest few in turn. On success the other's mouths, passages and chambers become this system's (in its
		/// frame) and the link is added; false (and nothing changed) where none fits, with <see cref="JoinProblem"/> saying why.
		/// </summary>
		public bool TryJoin(CaveSystem other, CaveGround ground, float maxLength, int seed)
		{
			if (other == null || other.Chambers.Count == 0 || Chambers.Count == 0)
			{
				JoinProblem = "no chambers to join";
				return false;
			}
			if (segA == null)
			{
				Prepare();
			}
			Vector3 shift = other.Origin - Origin;
			List<LinkEnd> mine = EndsOf(this, Vector3.zero), theirs = EndsOf(other, shift);
			var pairs = new List<(float cost, int a, int b)>();
			float nearest = float.MaxValue;
			for (int a = 0; a < mine.Count; a++)
			{
				for (int b = 0; b < theirs.Count; b++)
				{
					Vector3 apart = theirs[b].Centre - mine[a].Centre;
					float flat = new Vector2(apart.x, apart.z).magnitude;
					nearest = Mathf.Min(nearest, flat);
					if (flat > maxLength)
					{
						continue;
					}
					// Near across and near in depth: a link climbing far runs out of rock over it, and winds a long way to do it.
					float rise = theirs[b].FloorAt(theirs[b].Centre) - mine[a].FloorAt(mine[a].Centre);
					pairs.Add((flat + 3f * Mathf.Abs(rise), a, b));
				}
			}
			pairs.Sort((p, q) =>
			{
				int c = p.cost.CompareTo(q.cost);
				if (c != 0) return c;
				c = p.a.CompareTo(q.a);
				return c != 0 ? c : p.b.CompareTo(q.b);
			});
			float r = Mathf.Max(MinBore, Radius * 0.8f);
			float clearance = Clearance(r, Amp + other.Amp);
			float lowest = Mathf.Max(LowestFloor, other.LowestFloor) - Origin.y;
			// Round both systems' other passages and chambers, never through them: where a link crossed a sloping room at an
			// angle, the two floors met in a drop.
			bool Crosses(Vector3 n, float nf)
				=> Smooth(n) < clearance && Mathf.Abs(FloorNear(n) - nf) > 0.5f
					|| other.Smooth(n - shift) < clearance && Mathf.Abs(other.FloorNear(n - shift) + shift.y - nf) > 0.5f
					|| other.NearChasmOrClimb(n - shift, r + 1f);
			var tried = new List<(Vector3 a, Vector3 b)>();
			string problem = null;
			foreach ((float _, int ia, int ib) in pairs)
			{
				if (tried.Count >= MaxJoinTries)
				{
					break;
				}
				LinkEnd from = mine[ia], to = theirs[ib];
				// Not the same two places again: a way blocked between two lobes of halls is blocked between their neighbours.
				if (tried.Exists(t => (t.a - from.Centre).sqrMagnitude < 900f && (t.b - to.Centre).sqrMagnitude < 900f))
				{
					continue;
				}
				tried.Add((from.Centre, to.Centre));
				var rng = new DeterministicRNG(unchecked(seed + (tried.Count - 1) * 7919));
				// Out of a chamber, or out of a trunk's side by a stub; into the other the same way.
				List<CaveNode> leave = from.IsChamber ? null : Stub(from, to.Centre, r, clearance, ground, Crosses);
				List<CaveNode> reach = to.IsChamber ? null : Stub(to, from.Centre, r, clearance, ground, Crosses);
				if (!from.IsChamber && leave == null || !to.IsChamber && reach == null)
				{
					problem = "no room beside a trunk for a junction";
					continue;
				}
				CaveNode start = leave != null ? leave[leave.Count - 1] : StartIn(from, to.Centre, r);
				LinkEnd target = reach != null ? TipOf(reach, to.Run) : to;
				// Only the chamber it leaves and the one it reaches are its to cross into near them.
				bool Blocked(Vector3 n, float nf) => from.Measure(n) >= from.Leaves && target.Measure(n) >= target.Arrives && Crosses(n, nf);
				Vector3 toward = target.Centre - start.Centre;
				float heading = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
				List<CaveNode> walk = WalkTo(start, heading, target, maxLength * 1.6f, rng, ground, Blocked, lowest, float.NaN, 0f, 0f, out problem);
				if (walk == null)
				{
					continue;
				}
				if (reach != null)
				{
					reach.Reverse();
				}
				Passages.Add(Joined(CavePassageKind.Link, leave, walk, reach));
				Absorb(other, shift);
				Prepare();
				JoinProblem = null;
				return true;
			}
			JoinProblem = pairs.Count == 0 ? $"the nearest places on the two are {nearest:0} m apart" : problem;
			return false;
		}

		// ── Through the hill ──────────────────────────────────────

		/// <summary>How far through its hill (in plan) a cave looks for the hill's far face to open a second mouth on, metres.</summary>
		public const float ThroughReach = 600f;
		/// <summary>The longest passage walked in from a second mouth to the first mouth's trunk, metres.</summary>
		public const float ThroughLength = 900f;
		/// <summary>How far the hill must stand over both feet between them for a way through it, metres: rock over the bore all the way.</summary>
		public const float ThroughRise = 12f;
		/// <summary>The most the far foot may stand over or under the first mouth, rise over the run between them.</summary>
		public const float ThroughGrade = 0.3f;
		/// <summary>The headings tried either side of straight through the hill, degrees apart, and how many each way.</summary>
		private const float ThroughFanDegrees = 10f;
		private const int ThroughFan = 4;
		/// <summary>The most far faces and trunk ends tried.</summary>
		private const int ThroughFaces = 3, ThroughTries = 4;

		/// <summary>The forms a hill may be gone through by (a second mouth on its far side): limestone, ice and lava caves.</summary>
		public static bool GoesThrough(CaveForm form) => form == CaveForm.Cave || form == CaveForm.IceCave || form == CaveForm.LavaTube;

		/// <summary>
		/// Opens a second mouth on the far face of the hill the first mouth is cut into, where there is one within
		/// <see cref="ThroughReach"/>, and walks a passage from it to the first mouth's trunk (past its zone, met by a stub out of
		/// its side): the hill is then a way through, in at one mouth and out at the other. The new mouth is the system's last;
		/// its passage is a trunk of its own (its first node at its mouth, its last on the first trunk's node). False (and
		/// nothing changed) where there is no far face or no way in from it fits.
		/// </summary>
		/// <param name="refused">Ground a mouth may not open on (water, another site's footprint); null for none.</param>
		public bool TryAddThrough(int siteId, CaveGround ground, int seed, Func<float, float, bool> refused, out string problem)
		{
			problem = null;
			if (!GoesThrough(Form) || Mouths.Count == 0 || Passages.Count == 0 || Passages[0].Kind != CavePassageKind.Trunk || Mouths[0].Kind != CaveMouthKind.Ground)
			{
				problem = "it is not a cave that goes through its hill";
				return false;
			}
			if (segA == null)
			{
				Prepare();
			}
			List<(Vector3 Foot, float Yaw, float Value)> faces = FarFaces(0, ground, refused);
			if (faces.Count == 0)
			{
				problem = $"its hill has no far face within {ThroughReach:0} m";
				return false;
			}
			// The first mouth's trunk past its zone: the way through joins it there, at its own floor.
			List<LinkEnd> ends = EndsOf(this, Vector3.zero, 0.5f * TrunkEndSpacing, chambers: false, firstTrunkOnly: true);
			if (ends.Count == 0)
			{
				problem = "its trunk ends inside its mouth's zone";
				return false;
			}
			var rng = new DeterministicRNG(seed);
			float r = Radius;
			float clearance = Clearance(r, 2f * Amp);
			bool Crosses(Vector3 n, float nf) => Smooth(n) < clearance && Mathf.Abs(FloorNear(n) - nf) > 0.5f;
			for (int f = 0; f < faces.Count && f < ThroughFaces; f++)
			{
				(Vector3 foot, float yaw, float _) = faces[f];
				float yr = yaw * Mathf.Deg2Rad;
				var outward = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
				// The mouth where the slope behind the foot turns steep (40° over two metres), as the first mouth's (Plan).
				float mx = foot.x, mz = foot.z;
				for (float d = 0f; d <= 20f; d += 0.5f)
				{
					float x = foot.x - outward.x * d, z = foot.z - outward.z * d;
					if (ground.At(x - outward.x * 2f, z - outward.z * 2f) - ground.At(x, z) >= 1.68f)
					{
						mx = x;
						mz = z;
						break;
					}
				}
				float floorY = ground.At(mx, mz);
				if (floorY < LowestFloor)
				{
					problem = "its far face's foot stands under the sea's reach";
					continue;
				}
				float zone = Mathf.Max(14f, 3.5f * r);
				var mouth = new CaveMouth
				{
					Floor = new Vector3(mx, floorY, mz) - Origin, Yaw = Mathf.Repeat(yaw, 360f), SiteId = siteId, Radius = r, Zone = zone,
					Collar = 1.5f, CollarLow = 1.5f, Shell = 3f, Kind = CaveMouthKind.Ground,
				};
				var start = new CaveNode
				{
					Centre = mouth.Floor + outward * (0.2f * r) + Vector3.up * (FloorDepth * r * Flat), Radius = r, Floor = mouth.Floor.y, Along = 0f,
				};
				float inward = Mathf.Repeat(yaw + 180f, 360f);
				// The trunk's nodes nearest across and in depth first.
				var ranked = new List<(float cost, int index)>();
				for (int i = 0; i < ends.Count; i++)
				{
					Vector3 apart = ends[i].Centre - start.Centre;
					ranked.Add((new Vector2(apart.x, apart.z).magnitude + 3f * Mathf.Abs(ends[i].NodeFloor - start.Floor), i));
				}
				ranked.Sort((p, q) => p.cost != q.cost ? p.cost.CompareTo(q.cost) : p.index.CompareTo(q.index));
				var tried = new List<Vector3>();
				foreach ((float _, int index) in ranked)
				{
					if (tried.Count >= ThroughTries)
					{
						break;
					}
					LinkEnd end = ends[index];
					if (tried.Exists(t => (t - end.Centre).sqrMagnitude < 400f))
					{
						continue;
					}
					tried.Add(end.Centre);
					List<CaveNode> reach = Stub(end, start.Centre, r, clearance, ground, Crosses, out problem);
					if (reach == null)
					{
						continue;
					}
					List<CaveNode> walk = WalkTo(start, inward, TipOf(reach, end.Run), ThroughLength, rng, ground, Crosses, LowestFloor - Origin.y, inward, 0.8f * r + 1f, zone, out problem);
					if (walk == null)
					{
						continue;
					}
					reach.Reverse();
					Mouths.Add(mouth);
					Passages.Add(Joined(CavePassageKind.Trunk, walk, reach));
					Prepare();
					return true;
				}
			}
			problem = $"no way in from its far face fits ({problem})";
			return false;
		}

		/// <summary>
		/// The far faces of the hill behind a mouth, best first: along headings fanned about straight through it, the first foot
		/// past a crest standing <see cref="ThroughRise"/> over both feet where a face rises behind standable ground looking on
		/// away from the mouth (<see cref="PointOfInterestPlanner.FaceScore"/>), within <see cref="ThroughGrade"/> of the
		/// mouth's height, over the sea's reach and on ground not <paramref name="refused"/>. World positions.
		/// </summary>
		private List<(Vector3 Foot, float Yaw, float Value)> FarFaces(int mouthIndex, CaveGround ground, Func<float, float, bool> refused)
		{
			var found = new List<(Vector3, float, float)>();
			CaveMouth m = Mouths[mouthIndex];
			Vector3 at = Origin + m.Floor;
			float inward = m.Yaw + 180f;
			for (int k = -ThroughFan; k <= ThroughFan; k++)
			{
				float h = inward + k * ThroughFanDegrees, hr = h * Mathf.Deg2Rad;
				var dir = new Vector2(Mathf.Sin(hr), Mathf.Cos(hr));
				float crest = float.MinValue;
				for (float d = 24f; d <= ThroughReach; d += 3f)
				{
					float x = at.x + dir.x * d, z = at.z + dir.y * d;
					if (!ground.Contains(x, z, 40f))
					{
						break;
					}
					float g = ground.At(x, z);
					crest = Mathf.Max(crest, g);
					// Still climbing the near side, or on the top: the crest must stand well over both feet.
					if (crest - g < ThroughRise || crest - at.y < ThroughRise)
					{
						continue;
					}
					float score = PointOfInterestPlanner.FaceScore(ground.At, null, x, z, 0f, out float yaw);
					if (score < 0.2f || Mathf.Abs(DeltaAngle(yaw, h)) > 50f)
					{
						continue;
					}
					if (g < LowestFloor + 0.5f || Mathf.Abs(g - at.y) > ThroughGrade * d || refused != null && refused(x, z))
					{
						continue;
					}
					found.Add((new Vector3(x, g, z), yaw, score * (1f - 0.5f * d / ThroughReach) * (1f - 0.08f * Mathf.Abs(k))));
					break;
				}
			}
			found.Sort((a, b) => b.Item3.CompareTo(a.Item3));
			return found;
		}
	}
}
#endif
