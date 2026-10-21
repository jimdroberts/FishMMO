#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What a point of a cave's rock is like to live on, each 0..1.</summary>
	public struct CaveHabitatSample
	{
		/// <summary>Damp: 0 dry rock, 1 dripping, or at a pool's or stream's edge.</summary>
		public float Wet;
		/// <summary>The daylight that reaches it from a mouth or an opening in the roof: 0 the deep dark, 1 in the opening.</summary>
		public float Daylight;
		/// <summary>Silt, mud and grit settled on it: 0 bare (walls, roofs, ribs), 1 a hollow of the floor that gathers what washes in.</summary>
		public float Sediment;
		/// <summary>Worn by feet: 1 on a way through the cave, 0 off every way.</summary>
		public float Trodden;
		/// <summary>Organic matter: guano under roosts, humus and leaf litter blown in at a mouth, rotting timber in a mine.</summary>
		public float Organic;
		/// <summary>Where the cave's glow gathers (luminous fungi, glow moss, glowing crust): a deterministic field over the system.</summary>
		public float Glow;
	}

	/// <summary>
	/// The habitat field of a cave system: one pure, deterministic function of the system (its layout, mouths, water and seed),
	/// shared by everything that dresses a cave so they agree on where it is wet, lit, silted, trodden, rich and glowing — the rock's
	/// own layers (baked into the mesh), the cave cover scattered on it at run time, and the dressing's props and vines.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Mesh contract.</b> The cave mesher writes each vertex's sample as COLOR = (Wet, Daylight, Sediment, Trodden) and mesh UV
	/// channel 3 (TEXCOORD3) = (Organic, Glow). The rock shader and the run-time cover read only those; nothing at run time calls
	/// this class (it is editor-only, as the system is).
	/// </para>
	/// <para>Positions and normals are in the system's frame (as <see cref="CaveSystem"/>'s nodes are).</para>
	/// <para>
	/// <b>How it is worked out (2026-10-20).</b> Everything a sample needs that is not local is measured once, in
	/// <see cref="For"/>: a graph of the system's walking points (every passage node, every chamber's middle, the shafts' and
	/// chasms' ends), joined where passages meet and where they run through a chamber, and from each mouth the distance along it
	/// to every point (Dijkstra) — so the light from a mouth falls off with the way a person would walk in, never straight
	/// through the rock between two passages. A point of the rock belongs to the void primitive it is the wall of (the passage
	/// segment, chamber, shaft or chasm it is nearest in that primitive's own proportions), and takes that primitive's distances.
	/// The lanes the dressing keeps clear (1.4 m either side of each passage's line, and the ways across the great halls) are the
	/// trodden ways. All of it lives in arrays bucketed in 8 m cells; <see cref="Sample"/> only reads them, so the mesher calls it
	/// from many threads at once.
	/// </para>
	/// <para>
	/// <b>What makes each value.</b> Wet: drips that land in clusters (a field in plan, so the wet spot on the roof is over the wet
	/// spot on the floor), more under a high vault; seepage streaks down the walls; damp hollows; the edge of the system's water.
	/// A lava tube is dry but where rain blows in at an opening; an ice cave is frozen within its cold zone with a band of meltwater
	/// round it (90 m from a mouth, and all of the cold trap more than 8 m under the sill however far in, then a 20 m thaw ring). Daylight: from every mouth
	/// (a <c>Skylight</c> too when the layout has one), by the distance walked in. Sediment: what faces up, more in hollows, low
	/// down and deep in; a mine's floors are spoil. Trodden: the lanes. Organic: litter blown in at a mouth (and wrack at a sea
	/// cave's tide line), guano under the high vaults of halls where bats roost, rotting timber at a mine's wall feet. Glow:
	/// seeded patches 3–15 m across over the system, brightest where it is damp and dark; none in a lava tube, little in a mine,
	/// a faint glow-ice deep in an ice cave.
	/// </para>
	/// </remarks>
	public sealed class CaveHabitat
	{
		/// <summary>The mesh UV channel the second half of a sample is written to.</summary>
		public const int UvChannel = 3;

		/// <summary>A lane's half-width either side of a passage's line, metres (the dressing keeps it clear).</summary>
		public const float LaneHalfWidth = 1.4f;
		/// <summary>The worn core of a lane, metres either side of its line: fully trodden within it.</summary>
		public const float TroddenCore = 0.55f;
		/// <summary>The side of the cells the primitives and lanes are bucketed in, metres.</summary>
		private const float Bucket = 8f;
		/// <summary>The grid the glow's patches are seeded on, metres: each cell may hold one patch 3–15 m across.</summary>
		private const float GlowCell = 11f;
		/// <summary>
		/// An ice cave's cold zone and thaw ring, metres along the way in, and how far under the sill the cold trap lies, frozen
		/// however far in (2026-10-21; decision 51 froze it only within 160 m).
		/// </summary>
		private const float ColdReach = CaveIce.ColdReach, ColdSinkDepth = CaveIce.ColdSinkDepth, ThawRing = CaveIce.ThawRing;

		/// <summary>What a cave form does to its habitat: how damp, how much silt, litter and guano, how much glow.</summary>
		public struct FormTraits
		{
			public float Wet, Sediment, Litter, Guano, Glow;
			/// <summary>Floors are a mine's spoil (coarse) rather than washed-in silt.</summary>
			public bool Spoil;
			/// <summary>Dry rock but where rain blows in at an opening (a lava tube).</summary>
			public bool Dry;
			/// <summary>Frozen within a cold zone, with a ring of meltwater round it (an ice cave).</summary>
			public bool Frozen;
		}

		/// <summary>
		/// The one place the habitat's per-form physics is chosen (2026-10-20): code defaults until the biome suite's cave interiors
		/// (I2n) carry them on templates.
		/// </summary>
		public static FormTraits TraitsOf(CaveForm form)
		{
			switch (form)
			{
				case CaveForm.Grotto:
					return new FormTraits { Wet = 1.1f, Sediment = 1f, Litter = 1f, Guano = 0.5f, Glow = 1f };
				case CaveForm.SeaCave:
					return new FormTraits { Wet = 1.25f, Sediment = 0.8f, Litter = 0.35f, Guano = 0.3f, Glow = 0.7f };
				case CaveForm.IceCave:
					return new FormTraits { Wet = 0.6f, Sediment = 0.45f, Litter = 0.2f, Guano = 0f, Glow = 0.35f, Frozen = true };
				case CaveForm.LavaTube:
					return new FormTraits { Wet = 0.15f, Sediment = 0.6f, Litter = 0.7f, Guano = 0.8f, Glow = 0f, Dry = true };
				case CaveForm.Mine:
					return new FormTraits { Wet = 0.6f, Sediment = 1f, Litter = 0.5f, Guano = 0.25f, Glow = 0.3f, Spoil = true };
				default:
					return new FormTraits { Wet = 1f, Sediment = 1f, Litter = 1f, Guano = 1f, Glow = 1f };
			}
		}

		private enum PrimKind : byte { Segment, Chamber, Shaft, Rift }

		/// <summary>One void primitive a point of rock can be the wall of, with the graph points at its ends.</summary>
		private struct Prim
		{
			public PrimKind Kind;
			public Vector3 A, B;
			public float RadiusA, RadiusB, HeightA, HeightB, Top;
			public int NodeA, NodeB, Chamber;
		}

		private struct Lane
		{
			public Vector3 A, B;
			public float Half, Weight;
		}

		private readonly CaveSystem system;
		private readonly FormTraits traits;
		private readonly int seed;
		private readonly float invFlat;
		private readonly bool hasWater;
		private readonly float waterLocal;
		private readonly float splash;

		private readonly Prim[] prims;
		private readonly Dictionary<long, int[]> primBuckets;
		private readonly Lane[] lanes;
		private readonly Dictionary<long, int[]> laneBuckets;

		private readonly Vector3[] nodes;
		/// <summary>Per mouth, the distance walked from it to every graph point (+inf where none leads).</summary>
		private readonly float[][] distance;
		/// <summary>Per chamber, the graph points that open into it (passage nodes inside it, its neighbours' middles).</summary>
		private readonly int[][] portals;
		private readonly int chamberBase;

		private readonly Vector3[] mouthFloor;
		private readonly Vector3[] mouthOut;
		private readonly float[] mouthReach, mouthStrength, mouthRadius;
		private readonly bool[] mouthWet;
		private readonly bool[] mouthSky;

		/// <summary>The roof-opening kind when the layout has one (an enum member it may add): compiled whether or not it exists.</summary>
		private static readonly CaveMouthKind? skylightKind = Enum.TryParse("Skylight", out CaveMouthKind sky) ? sky : (CaveMouthKind?)null;

		private CaveHabitat(CaveSystem system)
		{
			this.system = system;
			system.Prepare();
			traits = TraitsOf(system.Form);
			seed = system.Seed;
			invFlat = 1f / Mathf.Max(0.2f, system.Flat);
			hasWater = !float.IsNaN(system.WaterLevel);
			waterLocal = hasWater ? system.WaterLevel - system.Origin.y : 0f;
			splash = system.Form == CaveForm.SeaCave ? 3f : 0.9f;

			// ── The graph's points ──
			var points = new List<Vector3>();
			var passageBase = new int[system.Passages.Count];
			for (int p = 0; p < system.Passages.Count; p++)
			{
				passageBase[p] = points.Count;
				foreach (CaveNode n in system.Passages[p].Nodes)
				{
					points.Add(new Vector3(n.Centre.x, n.Floor, n.Centre.z));
				}
			}
			chamberBase = points.Count;
			foreach (CaveChamber c in system.Chambers)
			{
				points.Add(new Vector3(c.Centre.x, c.Floor, c.Centre.z));
			}
			int shaftBase = points.Count;
			foreach (CaveShaft s in system.Shafts)
			{
				points.Add(s.Bottom);
				points.Add(new Vector3(s.Bottom.x, s.Top, s.Bottom.z));
			}
			int riftBase = points.Count;
			foreach (CaveRift r in system.Rifts)
			{
				points.Add(r.A);
				points.Add(r.B);
			}
			int climbBase = points.Count;
			foreach (CaveClimb c in system.Climbs)
			{
				points.Add(c.Bottom);
				points.Add(c.Top);
			}
			nodes = points.ToArray();
			var edges = new List<(int to, float w)>[nodes.Length];
			for (int i = 0; i < edges.Length; i++)
			{
				edges[i] = new List<(int, float)>(4);
			}
			void Join(int a, int b)
			{
				if (a == b)
				{
					return;
				}
				float w = (nodes[a] - nodes[b]).magnitude;
				edges[a].Add((b, w));
				edges[b].Add((a, w));
			}

			// Along each passage.
			for (int p = 0; p < system.Passages.Count; p++)
			{
				int count = system.Passages[p].Nodes.Count;
				for (int i = 0; i + 1 < count; i++)
				{
					Join(passageBase[p] + i, passageBase[p] + i + 1);
				}
			}
			// Where a passage ends on another: its end to the other's nearest node within reach.
			for (int p = 0; p < system.Passages.Count; p++)
			{
				List<CaveNode> own = system.Passages[p].Nodes;
				if (own.Count == 0)
				{
					continue;
				}
				foreach (int end in new[] { 0, own.Count - 1 })
				{
					CaveNode e = own[end];
					for (int q = 0; q < system.Passages.Count; q++)
					{
						if (q == p)
						{
							continue;
						}
						List<CaveNode> other = system.Passages[q].Nodes;
						int best = -1;
						float bestD = float.MaxValue;
						for (int i = 0; i < other.Count; i++)
						{
							float reach = e.Radius + other[i].Radius + 3f;
							float d = (nodes[passageBase[p] + end] - nodes[passageBase[q] + i]).magnitude;
							if (d < reach && d < bestD)
							{
								bestD = d;
								best = i;
							}
						}
						if (best >= 0)
						{
							Join(passageBase[p] + end, passageBase[q] + best);
						}
					}
				}
			}
			// Passage nodes inside a chamber open into it; lobes of one hall open into each other.
			var chamberPortals = new List<int>[system.Chambers.Count];
			for (int c = 0; c < system.Chambers.Count; c++)
			{
				chamberPortals[c] = new List<int>();
				CaveChamber ch = system.Chambers[c];
				for (int p = 0; p < system.Passages.Count; p++)
				{
					List<CaveNode> list = system.Passages[p].Nodes;
					for (int i = 0; i < list.Count; i++)
					{
						if (ChamberMeasure(ch, list[i].Centre) < 1.15f + list[i].Radius / Mathf.Max(1f, Mathf.Min(ch.Radii.x, ch.Radii.z)))
						{
							Join(passageBase[p] + i, chamberBase + c);
							chamberPortals[c].Add(passageBase[p] + i);
						}
					}
				}
			}
			for (int c = 0; c < system.Chambers.Count; c++)
			{
				for (int d = c + 1; d < system.Chambers.Count; d++)
				{
					CaveChamber a = system.Chambers[c], b = system.Chambers[d];
					if (ChamberMeasure(a, b.Centre) < 1.2f || ChamberMeasure(b, a.Centre) < 1.2f
						|| (Mathf.Abs(a.Floor - b.Floor) < 6f && Planar(a.Centre - b.Centre) < 0.85f * (Mathf.Max(a.Radii.x, a.Radii.z) + Mathf.Max(b.Radii.x, b.Radii.z))))
					{
						Join(chamberBase + c, chamberBase + d);
						chamberPortals[c].Add(chamberBase + d);
						chamberPortals[d].Add(chamberBase + c);
					}
				}
			}
			portals = new int[chamberPortals.Length][];
			for (int c = 0; c < chamberPortals.Length; c++)
			{
				portals[c] = chamberPortals[c].ToArray();
			}
			// Shafts and chasms: their two ends, each joined to what is nearest it within reach.
			void Attach(int point, float reach)
			{
				int best = -1;
				float bestD = float.MaxValue;
				// Onto the passages' and chambers' points only (a shaft's or chasm's own other end is joined to it already).
				for (int i = 0; i < shaftBase; i++)
				{
					float d = (nodes[i] - nodes[point]).magnitude;
					if (d < reach && d < bestD)
					{
						bestD = d;
						best = i;
					}
				}
				if (best >= 0)
				{
					Join(point, best);
				}
			}
			for (int s = 0; s < system.Shafts.Count; s++)
			{
				Join(shaftBase + 2 * s, shaftBase + 2 * s + 1);
				float reach = system.Shafts[s].Radius + 8f;
				Attach(shaftBase + 2 * s, reach);
				Attach(shaftBase + 2 * s + 1, reach);
			}
			for (int r = 0; r < system.Rifts.Count; r++)
			{
				Join(riftBase + 2 * r, riftBase + 2 * r + 1);
				CaveRift rift = system.Rifts[r];
				float reach = 2f * rift.Width + Mathf.Max(0f, rift.Top - Mathf.Min(rift.A.y, rift.B.y)) + 4f;
				Attach(riftBase + 2 * r, reach);
				Attach(riftBase + 2 * r + 1, reach);
			}
			// Climbs (2026-10-21): foot and head, each onto the way nearest it. A chimney's top stands a bore over the way that
			// comes to it, past its own reach, so a deep system's levels under one were walked from no mouth.
			for (int c = 0; c < system.Climbs.Count; c++)
			{
				Join(climbBase + 2 * c, climbBase + 2 * c + 1);
				Attach(climbBase + 2 * c, 8f);
				Attach(climbBase + 2 * c + 1, 8f);
			}

			// ── From each mouth ──
			int mouths = system.Mouths.Count;
			distance = new float[mouths][];
			mouthFloor = new Vector3[mouths];
			mouthOut = new Vector3[mouths];
			mouthReach = new float[mouths];
			mouthStrength = new float[mouths];
			mouthRadius = new float[mouths];
			mouthWet = new bool[mouths];
			mouthSky = new bool[mouths];
			for (int m = 0; m < mouths; m++)
			{
				CaveMouth mouth = system.Mouths[m];
				float yaw = mouth.Yaw * Mathf.Deg2Rad;
				mouthFloor[m] = mouth.Floor;
				mouthOut[m] = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
				mouthRadius[m] = Mathf.Max(1f, mouth.Radius);
				mouthSky[m] = skylightKind.HasValue && mouth.Kind == skylightKind.Value;
				// The twilight zone: about six bore radii in, a skylight's light falling straight down a little less far.
				mouthReach[m] = Mathf.Clamp((mouthSky[m] ? 4.5f : 6f) * mouthRadius[m], 10f, 40f);
				// Through water the light is dim and green.
				mouthWet[m] = mouth.Kind == CaveMouthKind.Submerged;
				mouthStrength[m] = mouthWet[m] ? 0.35f : 1f;
				distance[m] = Dijkstra(edges, mouth.Floor, mouthRadius[m]);
			}

			// ── The void's primitives, bucketed ──
			var list2 = new List<Prim>();
			for (int p = 0; p < system.Passages.Count; p++)
			{
				List<CaveNode> ns = system.Passages[p].Nodes;
				for (int i = 0; i + 1 < ns.Count; i++)
				{
					list2.Add(new Prim
					{
						Kind = PrimKind.Segment,
						A = ns[i].Centre,
						B = ns[i + 1].Centre,
						RadiusA = ns[i].Radius,
						RadiusB = ns[i + 1].Radius,
						HeightA = ns[i].Height,
						HeightB = ns[i + 1].Height,
						NodeA = passageBase[p] + i,
						NodeB = passageBase[p] + i + 1,
						Chamber = -1,
					});
				}
			}
			for (int c = 0; c < system.Chambers.Count; c++)
			{
				list2.Add(new Prim { Kind = PrimKind.Chamber, A = system.Chambers[c].Centre, Chamber = c, NodeA = chamberBase + c, NodeB = chamberBase + c });
			}
			for (int s = 0; s < system.Shafts.Count; s++)
			{
				CaveShaft shaft = system.Shafts[s];
				list2.Add(new Prim
				{
					Kind = PrimKind.Shaft,
					A = shaft.Bottom,
					B = new Vector3(shaft.Bottom.x, shaft.Top, shaft.Bottom.z),
					RadiusA = shaft.Radius,
					RadiusB = shaft.Radius,
					Top = shaft.Top,
					NodeA = shaftBase + 2 * s,
					NodeB = shaftBase + 2 * s + 1,
					Chamber = -1,
				});
			}
			for (int r = 0; r < system.Rifts.Count; r++)
			{
				CaveRift rift = system.Rifts[r];
				list2.Add(new Prim
				{
					Kind = PrimKind.Rift,
					A = rift.A,
					B = rift.B,
					RadiusA = Mathf.Max(0.5f, rift.Width),
					RadiusB = Mathf.Max(0.5f, rift.Width),
					Top = rift.Top,
					NodeA = riftBase + 2 * r,
					NodeB = riftBase + 2 * r + 1,
					Chamber = -1,
				});
			}
			prims = list2.ToArray();
			var buckets = new Dictionary<long, List<int>>();
			float pad = system.Amp * 1.6f + system.ShellDeep + 3f;
			for (int i = 0; i < prims.Length; i++)
			{
				Prim pr = prims[i];
				Vector3 lo, hi;
				switch (pr.Kind)
				{
					case PrimKind.Chamber:
					{
						CaveChamber ch = system.Chambers[pr.Chamber];
						float rx = Mathf.Max(ch.Radii.x, ch.Radii.z);
						var r = new Vector3(rx, ch.Radii.y + Mathf.Abs(ch.Slope) * rx, rx) * 1.1f + Vector3.one * pad;
						lo = ch.Centre - r;
						hi = ch.Centre + r;
						break;
					}
					default:
					{
						float r = Mathf.Max(pr.RadiusA * Mathf.Max(1f, pr.HeightA), pr.RadiusB * Mathf.Max(1f, pr.HeightB)) * 1.2f + pad;
						lo = Vector3.Min(pr.A, pr.B) - Vector3.one * r;
						hi = Vector3.Max(pr.A, pr.B) + Vector3.one * r;
						if (pr.Kind == PrimKind.Rift)
						{
							hi.y = Mathf.Max(hi.y, pr.Top + r);
						}
						break;
					}
				}
				AddToBuckets(buckets, lo, hi, i, true);
			}
			primBuckets = Freeze(buckets);

			// ── The lanes: every passage's line on its floor, and the ways across the great halls ──
			var laneList = new List<Lane>();
			bool mine = system.Form == CaveForm.Mine;
			foreach (CavePassage passage in system.Passages)
			{
				float weight = passage.Kind == CavePassageKind.Trunk ? 1f : passage.Kind == CavePassageKind.Link ? 0.9f : 0.75f;
				for (int i = 0; i + 1 < passage.Nodes.Count; i++)
				{
					CaveNode a = passage.Nodes[i], b = passage.Nodes[i + 1];
					laneList.Add(new Lane
					{
						A = new Vector3(a.Centre.x, a.Floor, a.Centre.z),
						B = new Vector3(b.Centre.x, b.Floor, b.Centre.z),
						// A mine's drift is worn wall to wall; a cave's way is a path down the middle.
						Half = mine ? Mathf.Max(LaneHalfWidth, 0.7f * Mathf.Min(a.Radius, b.Radius)) : LaneHalfWidth,
						Weight = mine ? 1f : weight,
					});
				}
			}
			if (!mine)
			{
				foreach (CaveChamber c in system.Chambers)
				{
					if ((c.Kind != CaveChamberKind.Hall && c.Kind != CaveChamberKind.Cavern) || Mathf.Max(c.Radii.x, c.Radii.z) < 12f)
					{
						continue;
					}
					var middle = new Vector3(c.Centre.x, c.Floor, c.Centre.z);
					float reach = Mathf.Max(c.Radii.x, c.Radii.z);
					foreach (CavePassage passage in system.Passages)
					{
						if (passage.Nodes.Count < 2)
						{
							continue;
						}
						foreach (CaveNode n in new[] { passage.Nodes[0], passage.Nodes[passage.Nodes.Count - 1] })
						{
							if (Planar(n.Centre - c.Centre) < reach + n.Radius + 2f && Mathf.Abs(n.Floor - c.Floor) < 8f)
							{
								laneList.Add(new Lane { A = new Vector3(n.Centre.x, n.Floor, n.Centre.z), B = middle, Half = LaneHalfWidth, Weight = 0.9f });
							}
						}
					}
					foreach (CaveSolid s in system.Solids)
					{
						if (s.Kind != CaveSolidKind.Bridge && s.Kind != CaveSolidKind.Ramp)
						{
							continue;
						}
						foreach (Vector3 end in new[] { s.A, s.B })
						{
							if (ChamberMeasure(c, end) < 1.05f && Mathf.Abs(end.y - CaveSystem.FloorOf(c, end)) < 3f)
							{
								laneList.Add(new Lane { A = end, B = middle, Half = LaneHalfWidth, Weight = 0.9f });
							}
						}
					}
				}
			}
			lanes = laneList.ToArray();
			var laneB = new Dictionary<long, List<int>>();
			for (int i = 0; i < lanes.Length; i++)
			{
				float r = lanes[i].Half + 1f;
				AddToBuckets(laneB, Vector3.Min(lanes[i].A, lanes[i].B) - Vector3.one * r, Vector3.Max(lanes[i].A, lanes[i].B) + Vector3.one * r, i, false);
			}
			laneBuckets = Freeze(laneB);
		}

		/// <summary>The habitat of a system: everything measured once here, so a caller that samples many points keeps one.</summary>
		public static CaveHabitat For(CaveSystem system) => new CaveHabitat(system);

		/// <summary>The habitat at a point of the rock with its outward (into the void) normal.</summary>
		public CaveHabitatSample Sample(Vector3 local, Vector3 normal)
		{
			Vector3 p = local;
			Vector3 n = normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.up;
			float floorness = Smooth(0.5f, 0.85f, n.y);
			float roofness = Smooth(0.3f, 0.7f, -n.y);
			float wallness = Mathf.Clamp01(1f - floorness - roofness);

			int owner = Owner(p, out float t);
			float headroom = Headroom(owner, p, t);
			float floor = system.FloorNear(p);
			float height = float.IsNaN(floor) || float.IsInfinity(floor) || Mathf.Abs(floor) > 1e5f ? 0f : p.y - floor;

			// ── Daylight, and how far in the nearest mouth is ──
			float daylight = 0f, walked = float.PositiveInfinity, litter = 0f, rain = 0f;
			for (int m = 0; m < distance.Length; m++)
			{
				float d = Walked(m, owner, p, t);
				Vector3 off = p - mouthFloor[m];
				// In the opening itself, and the lip and collar out in the open before it: lit as the day outside.
				if (off.sqrMagnitude < 6.25f * mouthRadius[m] * mouthRadius[m] && (mouthSky[m] || Vector3.Dot(off, mouthOut[m]) > 0f))
				{
					d = mouthSky[m] ? Mathf.Min(d, Mathf.Max(0f, Planar(off) - mouthRadius[m])) : 0f;
				}
				walked = Mathf.Min(walked, d);
				float f = 1f - Mathf.Clamp01(d / mouthReach[m]);
				daylight = Mathf.Max(daylight, mouthStrength[m] * f * f * (3f - 2f * f));
				if (!mouthWet[m])
				{
					float l = 1f - Smooth(0.2f * mouthReach[m], 1.25f * mouthReach[m], d);
					litter = Mathf.Max(litter, l);
					// Rain and snowmelt blow in a little way, and fall straight down an opening in the roof.
					float rainReach = mouthSky[m] ? 1.3f * mouthRadius[m] + 2f : 1.5f * mouthRadius[m] + 6f;
					rain = Mathf.Max(rain, 1f - Smooth(0.4f * rainReach, rainReach, mouthSky[m] ? Planar(off) : d));
				}
			}

			// ── Wet ──
			float hollow = Smooth(-0.05f, 0.35f, ProceduralNoise.Fbm3(new Vector3(p.x / 5f, 1.7f, p.z / 5f), 2, 0.5f, seed + 111));
			float dripField = ProceduralNoise.Gradient3(new Vector3(p.x / 9f, 0.5f, p.z / 9f), seed + 101)
				+ 0.45f * ProceduralNoise.Gradient3(new Vector3(p.x / 3.2f, 0.5f, p.z / 3.2f), seed + 102);
			float roofBoost = 0.15f * Smooth(3f, 14f, headroom);
			float drip = Smooth(0.12f - roofBoost, 0.45f - roofBoost, dripField);
			float seep = Smooth(0.15f, 0.45f, ProceduralNoise.Gradient3(new Vector3(p.x / 1.3f, p.y / 9f, p.z / 1.3f), seed + 103));
			float wet = roofness * drip + floorness * Mathf.Max(0.85f * drip, 0.45f * hollow) + wallness * Mathf.Max(0.75f * drip, 0.55f * seep);
			wet = Mathf.Max(wet, 0.12f);
			// Air moves near a mouth: the twilight zone dries out a little.
			wet *= traits.Wet * (1f - 0.3f * daylight);
			if (traits.Frozen && (!float.IsInfinity(walked) || ColdTrap(p)))
			{
				// In the cold trap under the sill frozen wherever it lies; above it, within the cold zone.
				float beyond = ColdTrap(p) ? -1f : walked - ColdReach;
				// Frozen dry within the cold zone; meltwater in the ring round it; then ordinary damp rock.
				float thaw = beyond <= 0f ? 0f : beyond >= ThawRing ? 0f : Mathf.Sin(Mathf.PI * beyond / ThawRing);
				wet = beyond <= 0f ? 0.05f * wet : Mathf.Max(wet, 0.9f * thaw);
			}
			if (traits.Dry)
			{
				wet = Mathf.Min(wet, 0.08f);
			}
			wet = Mathf.Max(wet, (traits.Dry ? 0.7f : 0.5f) * rain);
			if (hasWater)
			{
				float h = p.y - waterLocal;
				wet = Mathf.Max(wet, h <= 0f ? 1f : 1f - Smooth(0f, splash, h));
			}
			wet = Mathf.Clamp01(wet);

			// ── Sediment ──
			float deep = float.IsInfinity(walked) ? 1f : Smooth(20f, 120f, walked);
			float lowness = 1f - Smooth(0.3f, 2.5f, height);
			float sediment;
			if (traits.Spoil)
			{
				// Spoil: broken rock trodden into the floor, heaped deeper in the stopes — coarse, never silt.
				bool stope = owner >= 0 && prims[owner].Kind == PrimKind.Chamber;
				sediment = floorness * Mathf.Clamp01((stope ? 0.42f : 0.3f) + 0.15f * hollow);
			}
			else
			{
				sediment = floorness * Mathf.Clamp01(0.18f + 0.55f * hollow + 0.25f * deep + 0.2f * wet) * (0.45f + 0.55f * lowness) * traits.Sediment;
			}
			if (traits.Frozen && (ColdTrap(p) || !float.IsInfinity(walked) && walked < ColdReach))
			{
				sediment *= 0.5f;
			}

			// ── Trodden ──
			float trodden = 0f;
			if (floorness > 0f && !(hasWater && p.y < waterLocal - 0.2f))
			{
				float worn = LaneWear(p);
				float traffic = float.IsInfinity(walked) ? 0.6f : Mathf.Lerp(1f, 0.6f, Smooth(80f, 400f, walked));
				trodden = floorness * worn * traffic;
			}

			// ── Organic ──
			float organic = floorness * litter * (0.55f + 0.45f * hollow) * traits.Litter;
			if (owner >= 0 && prims[owner].Kind == PrimKind.Chamber && traits.Guano > 0f)
			{
				CaveChamber ch = system.Chambers[prims[owner].Chamber];
				if (ch.Kind == CaveChamberKind.Hall || ch.Kind == CaveChamberKind.Cavern || ch.Kind == CaveChamberKind.Room)
				{
					// Under the highest part of a high vault, where the bats roost: piles in clusters, not a sheet.
					float vault = Smooth(6f, 16f, ch.Radii.y);
					float central = 1f - Smooth(0.35f, 0.85f, PlanMeasure(ch, p));
					float piles = Smooth(0.05f, 0.4f, ProceduralNoise.Gradient3(new Vector3(p.x / 6f, 2.3f, p.z / 6f), seed + 131));
					organic = Mathf.Max(organic, floorness * vault * central * piles * (1f - daylight) * traits.Guano);
				}
			}
			if (system.Form == CaveForm.Mine && owner >= 0 && prims[owner].Kind == PrimKind.Segment)
			{
				// The sets' posts and lagging rot where they meet the floor along the drift's sides.
				float across = AcrossShare(owner, p, t);
				float foot = Mathf.Max(floorness * Smooth(0.45f, 0.85f, across), wallness * (1f - Smooth(0.3f, 1.6f, height)));
				float rot = 0.6f + 0.4f * ProceduralNoise.Gradient3(new Vector3(p.x / 2f, p.y / 2f, p.z / 2f), seed + 141);
				organic = Mathf.Max(organic, 0.6f * foot * rot);
			}
			if (hasWater && system.Form == CaveForm.SeaCave)
			{
				// Wrack at the tide line.
				float h = p.y - waterLocal;
				organic = Mathf.Max(organic, 0.5f * floorness * (1f - Smooth(0.6f, 1.6f, Mathf.Abs(h - 0.5f))));
			}
			organic = Mathf.Clamp01(organic);

			// ── Glow ──
			float glow = 0f;
			if (traits.Glow > 0f)
			{
				float field = GlowField(p);
				if (field > 0f)
				{
					float env = (0.3f + 0.7f * wet) * (1f - 0.85f * daylight);
					float character = system.Character == CaveCharacter.Crystal ? 1.3f : system.Character == CaveCharacter.Halls ? 1.1f : 1f;
					float form = traits.Glow;
					if (traits.Frozen)
					{
						// Faint glow-ice only deep in, past the light.
						form *= Smooth(30f, 60f, float.IsInfinity(walked) ? 1e4f : walked);
					}
					glow = Mathf.Clamp01(field * env * form * character);
				}
			}

			return new CaveHabitatSample
			{
				Wet = wet,
				Daylight = Mathf.Clamp01(daylight),
				Sediment = Mathf.Clamp01(sediment),
				Trodden = Mathf.Clamp01(trodden),
				Organic = organic,
				Glow = glow,
			};
		}

		/// <summary>How far a point is walked in from the nearest mouth, metres (+inf where the system has none).</summary>
		public float WalkedFromMouth(Vector3 local)
		{
			int owner = Owner(local, out float t);
			float best = float.PositiveInfinity;
			for (int m = 0; m < distance.Length; m++)
			{
				best = Mathf.Min(best, Walked(m, owner, local, t));
			}
			return best;
		}

		/// <summary>The first half of a sample as the mesh's vertex colour.</summary>
		public static Color ToColour(in CaveHabitatSample s) => new Color(s.Wet, s.Daylight, s.Sediment, s.Trodden);

		/// <summary>The second half of a sample as the mesh's <see cref="UvChannel"/> coordinates.</summary>
		public static Vector2 ToUv(in CaveHabitatSample s) => new Vector2(s.Organic, s.Glow);

		// ── Measuring ───────────────────────────────────────────────

		/// <summary>The primitive a point is the wall of: the nearest in its own proportions; −1 when none is near.</summary>
		private int Owner(Vector3 p, out float t)
		{
			t = 0f;
			int best = -1;
			float bestE = float.MaxValue;
			if (primBuckets.TryGetValue(Key(p, true), out int[] ids))
			{
				foreach (int id in ids)
				{
					float e = Measure(id, p, out float pt);
					if (e < bestE)
					{
						bestE = e;
						best = id;
						t = pt;
					}
				}
			}
			if (best < 0)
			{
				// Far from every primitive (the collar out on the hillside): the nearest of all.
				for (int id = 0; id < prims.Length; id++)
				{
					float e = Measure(id, p, out float pt);
					if (e < bestE)
					{
						bestE = e;
						best = id;
						t = pt;
					}
				}
			}
			return best;
		}

		/// <summary>A point's distance from a primitive in its own proportions (1 on its wall), and where along it the point is.</summary>
		private float Measure(int id, Vector3 p, out float t)
		{
			Prim pr = prims[id];
			switch (pr.Kind)
			{
				case PrimKind.Chamber:
					t = 0f;
					return ChamberMeasure(system.Chambers[pr.Chamber], p);
				case PrimKind.Shaft:
				{
					t = Mathf.Clamp01((p.y - pr.A.y) / Mathf.Max(0.1f, pr.Top - pr.A.y));
					float e = Planar(p - pr.A) / Mathf.Max(0.3f, pr.RadiusA);
					float r = Mathf.Max(0.3f, pr.RadiusA);
					return e + (p.y < pr.A.y ? (pr.A.y - p.y) / r : p.y > pr.Top ? (p.y - pr.Top) / r : 0f);
				}
				case PrimKind.Rift:
				{
					var ab = new Vector2(pr.B.x - pr.A.x, pr.B.z - pr.A.z);
					var ap = new Vector2(p.x - pr.A.x, p.z - pr.A.z);
					t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					float e = (ap - ab * t).magnitude / pr.RadiusA;
					float bottom = Mathf.Lerp(pr.A.y, pr.B.y, t);
					return e + (p.y < bottom ? (bottom - p.y) / pr.RadiusA : p.y > pr.Top ? (p.y - pr.Top) / pr.RadiusA : 0f);
				}
				default:
				{
					Vector3 ab = pr.B - pr.A, ap = p - pr.A;
					t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					Vector3 q = ap - ab * t;
					q.y *= invFlat / Mathf.Max(0.5f, Mathf.Lerp(pr.HeightA, pr.HeightB, t));
					return q.magnitude / Mathf.Max(0.3f, Mathf.Lerp(pr.RadiusA, pr.RadiusB, t));
				}
			}
		}

		/// <summary>The distance walked from mouth <paramref name="m"/> to a point of its owner's wall.</summary>
		private float Walked(int m, int owner, Vector3 p, float t)
		{
			float[] d = distance[m];
			if (owner < 0)
			{
				return float.PositiveInfinity;
			}
			Prim pr = prims[owner];
			if (pr.Kind == PrimKind.Chamber)
			{
				// Across the chamber's open floor from whichever way into it is nearest the mouth.
				float best = d[pr.NodeA] + Planar(p - nodes[pr.NodeA]);
				foreach (int portal in portals[pr.Chamber])
				{
					best = Mathf.Min(best, d[portal] + (p - nodes[portal]).magnitude);
				}
				return best;
			}
			float a = d[pr.NodeA], b = d[pr.NodeB];
			if (float.IsInfinity(a) || float.IsInfinity(b))
			{
				return Mathf.Min(a, b);
			}
			return Mathf.Lerp(a, b, t);
		}

		/// <summary>The height of the void over its floor at a point of its owner (the vault, the bore's roof).</summary>
		private float Headroom(int owner, Vector3 p, float t)
		{
			if (owner < 0)
			{
				return 3f;
			}
			Prim pr = prims[owner];
			switch (pr.Kind)
			{
				case PrimKind.Chamber:
				{
					CaveChamber ch = system.Chambers[pr.Chamber];
					return ch.Centre.y + ch.Radii.y - ch.Floor;
				}
				case PrimKind.Shaft:
					return pr.Top - pr.A.y;
				case PrimKind.Rift:
					return pr.Top - Mathf.Lerp(pr.A.y, pr.B.y, t);
				default:
					return Mathf.Lerp(pr.RadiusA * pr.HeightA, pr.RadiusB * pr.HeightB, t) * 2f * system.Flat;
			}
		}

		/// <summary>How far across a passage segment a point is, 0 on its line .. 1 at its wall (plan).</summary>
		private float AcrossShare(int owner, Vector3 p, float t)
		{
			Prim pr = prims[owner];
			Vector3 c = Vector3.Lerp(pr.A, pr.B, t);
			return Planar(p - c) / Mathf.Max(0.3f, Mathf.Lerp(pr.RadiusA, pr.RadiusB, t));
		}

		/// <summary>How worn a floor point is by the lanes over it, 0..1 (wavering edges, so a way is not ruled).</summary>
		private float LaneWear(Vector3 p)
		{
			if (!laneBuckets.TryGetValue(Key(p, false), out int[] ids))
			{
				return 0f;
			}
			float wobble = 0.25f * ProceduralNoise.Gradient3(new Vector3(p.x / 2.5f, 0.3f, p.z / 2.5f), seed + 121);
			float best = 0f;
			foreach (int id in ids)
			{
				Lane lane = lanes[id];
				var ab = new Vector2(lane.B.x - lane.A.x, lane.B.z - lane.A.z);
				var ap = new Vector2(p.x - lane.A.x, p.z - lane.A.z);
				float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
				if (Mathf.Abs(p.y - Mathf.Lerp(lane.A.y, lane.B.y, t)) > 1.6f)
				{
					continue;
				}
				float d = (ap - ab * t).magnitude + wobble;
				float core = lane.Half > LaneHalfWidth ? lane.Half - (LaneHalfWidth - TroddenCore) : TroddenCore;
				best = Mathf.Max(best, lane.Weight * (1f - Smooth(core, lane.Half, d)));
			}
			return best;
		}

		/// <summary>The seeded glow patches at a point, 0..1: at most one patch 3–15 m across per 11 m cell, broken at its edge.</summary>
		private float GlowField(Vector3 p)
		{
			Vector3 g = p / GlowCell;
			int gx = Mathf.FloorToInt(g.x), gy = Mathf.FloorToInt(g.y), gz = Mathf.FloorToInt(g.z);
			float best = 0f;
			int salt = seed ^ 0x6C0F2A1;
			for (int z = -1; z <= 1; z++)
			{
				for (int y = -1; y <= 1; y++)
				{
					for (int x = -1; x <= 1; x++)
					{
						int cx = gx + x, cy = gy + y, cz = gz + z;
						uint h = ProceduralNoise.Hash(cx, cy, cz, salt);
						if (Unit(h, 1) > 0.55f)
						{
							continue;
						}
						var centre = new Vector3(cx + Unit(h, 2), cy + Unit(h, 3), cz + Unit(h, 4)) * GlowCell;
						float radius = Mathf.Lerp(1.5f, 7.5f, Mathf.Pow(Unit(h, 5), 1.3f));
						float d2 = (p - centre).sqrMagnitude / (radius * radius);
						if (d2 >= 1f)
						{
							continue;
						}
						float f = (1f - d2) * (1f - d2) * Mathf.Lerp(0.6f, 1f, Unit(h, 6));
						best = Mathf.Max(best, f);
					}
				}
			}
			if (best <= 0f)
			{
				return 0f;
			}
			// Ragged: colonies spread from their middles in lobes, not discs.
			float lobes = 0.7f + 0.6f * ProceduralNoise.Gradient3(p / 1.7f, seed + 151);
			return Mathf.Clamp01(best * lobes * 1.25f);
		}

		/// <summary>Whether a point lies in an ice cave's cold trap: more than <see cref="ColdSinkDepth"/> under its nearest way-in mouth's sill.</summary>
		private bool ColdTrap(Vector3 p) => p.y < NearestMouthFloor(p) - ColdSinkDepth;

		/// <summary>The floor of the way-in mouth nearest a point (an opening in a roof is no sill, unless the system has only those).</summary>
		private float NearestMouthFloor(Vector3 p)
		{
			bool anyWay = false;
			for (int m = 0; m < mouthFloor.Length; m++)
			{
				anyWay |= !mouthSky[m];
			}
			float best = float.MaxValue, y = 0f;
			for (int m = 0; m < mouthFloor.Length; m++)
			{
				if (anyWay && mouthSky[m])
				{
					continue;
				}
				float d = (p - mouthFloor[m]).sqrMagnitude;
				if (d < best)
				{
					best = d;
					y = mouthFloor[m].y;
				}
			}
			return y;
		}

		/// <summary>A chamber's measure at a point: 1 on its ellipsoid, sheared by its slope as the field cuts it.</summary>
		private static float ChamberMeasure(in CaveChamber ch, Vector3 p)
		{
			float yaw = ch.Yaw * Mathf.Deg2Rad;
			var zAxis = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
			var xAxis = new Vector3(zAxis.z, 0f, -zAxis.x);
			Vector3 o = p - ch.Centre;
			float along = Vector3.Dot(o, zAxis);
			var e = new Vector3(along / Mathf.Max(0.5f, ch.Radii.x), (o.y - ch.Slope * along) / Mathf.Max(0.5f, ch.Radii.y), Vector3.Dot(o, xAxis) / Mathf.Max(0.5f, ch.Radii.z));
			return e.magnitude;
		}

		/// <summary>A chamber's measure in plan only (0 at its middle, 1 at its outline).</summary>
		private static float PlanMeasure(in CaveChamber ch, Vector3 p)
		{
			float yaw = ch.Yaw * Mathf.Deg2Rad;
			var zAxis = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
			var xAxis = new Vector3(zAxis.z, 0f, -zAxis.x);
			Vector3 o = p - ch.Centre;
			return new Vector2(Vector3.Dot(o, zAxis) / Mathf.Max(0.5f, ch.Radii.x), Vector3.Dot(o, xAxis) / Mathf.Max(0.5f, ch.Radii.z)).magnitude;
		}

		/// <summary>Distances walked from a mouth's floor to every graph point (the points near the mouth seeded straight).</summary>
		private float[] Dijkstra(List<(int to, float w)>[] edges, Vector3 from, float radius)
		{
			var d = new float[nodes.Length];
			for (int i = 0; i < d.Length; i++)
			{
				d[i] = float.PositiveInfinity;
			}
			var heap = new MinHeap(nodes.Length);
			float reach = 2f * radius + 6f;
			int nearest = -1;
			float nearestD = float.MaxValue;
			for (int i = 0; i < nodes.Length; i++)
			{
				float s = (nodes[i] - from).magnitude;
				if (s < nearestD)
				{
					nearestD = s;
					nearest = i;
				}
				if (s < reach)
				{
					d[i] = s;
					heap.Push(i, s);
				}
			}
			if (heap.Count == 0 && nearest >= 0)
			{
				d[nearest] = nearestD;
				heap.Push(nearest, nearestD);
			}
			while (heap.Count > 0)
			{
				heap.Pop(out int u, out float du);
				if (du > d[u])
				{
					continue;
				}
				foreach ((int v, float w) in edges[u])
				{
					float nd = du + w;
					if (nd < d[v])
					{
						d[v] = nd;
						heap.Push(v, nd);
					}
				}
			}
			return d;
		}

		/// <summary>A binary min-heap of (point, distance); ties broken by point index, so the walk is deterministic.</summary>
		private sealed class MinHeap
		{
			private readonly List<(float d, int i)> items;

			public MinHeap(int capacity) => items = new List<(float, int)>(capacity);

			public int Count => items.Count;

			private static bool Less((float d, int i) a, (float d, int i) b) => a.d != b.d ? a.d < b.d : a.i < b.i;

			public void Push(int i, float d)
			{
				items.Add((d, i));
				int k = items.Count - 1;
				while (k > 0)
				{
					int parent = (k - 1) >> 1;
					if (!Less(items[k], items[parent]))
					{
						break;
					}
					(items[k], items[parent]) = (items[parent], items[k]);
					k = parent;
				}
			}

			public void Pop(out int i, out float d)
			{
				(d, i) = items[0];
				int last = items.Count - 1;
				items[0] = items[last];
				items.RemoveAt(last);
				int k = 0;
				while (true)
				{
					int l = 2 * k + 1, r = l + 1, best = k;
					if (l < items.Count && Less(items[l], items[best]))
					{
						best = l;
					}
					if (r < items.Count && Less(items[r], items[best]))
					{
						best = r;
					}
					if (best == k)
					{
						break;
					}
					(items[k], items[best]) = (items[best], items[k]);
					k = best;
				}
			}
		}

		// ── Buckets and small maths ─────────────────────────────────

		private static long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

		private static long Key(Vector3 p, bool threeD)
			=> Key(Mathf.FloorToInt(p.x / Bucket), threeD ? Mathf.FloorToInt(p.y / Bucket) : 0, Mathf.FloorToInt(p.z / Bucket));

		private static void AddToBuckets(Dictionary<long, List<int>> buckets, Vector3 lo, Vector3 hi, int id, bool threeD)
		{
			int x0 = Mathf.FloorToInt(lo.x / Bucket), x1 = Mathf.FloorToInt(hi.x / Bucket);
			int y0 = threeD ? Mathf.FloorToInt(lo.y / Bucket) : 0, y1 = threeD ? Mathf.FloorToInt(hi.y / Bucket) : 0;
			int z0 = Mathf.FloorToInt(lo.z / Bucket), z1 = Mathf.FloorToInt(hi.z / Bucket);
			for (int x = x0; x <= x1; x++)
			{
				for (int y = y0; y <= y1; y++)
				{
					for (int z = z0; z <= z1; z++)
					{
						long key = Key(x, y, z);
						if (!buckets.TryGetValue(key, out List<int> list))
						{
							buckets[key] = list = new List<int>();
						}
						list.Add(id);
					}
				}
			}
		}

		private static Dictionary<long, int[]> Freeze(Dictionary<long, List<int>> buckets)
		{
			var frozen = new Dictionary<long, int[]>(buckets.Count);
			foreach (KeyValuePair<long, List<int>> kv in buckets)
			{
				frozen[kv.Key] = kv.Value.ToArray();
			}
			return frozen;
		}

		private static float Planar(Vector3 v) => Mathf.Sqrt(v.x * v.x + v.z * v.z);

		private static float Smooth(float a, float b, float x)
		{
			float t = Mathf.Clamp01((x - a) / (b - a));
			return t * t * (3f - 2f * t);
		}

		private static float Unit(uint h, uint salt) => ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ (salt * 0x9E3779B9u)));
	}
}
#endif
