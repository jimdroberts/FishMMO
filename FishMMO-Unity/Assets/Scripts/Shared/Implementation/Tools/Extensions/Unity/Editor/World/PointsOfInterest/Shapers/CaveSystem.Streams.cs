#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Water in a cave (2026-10-20, caves livened, <see cref="CaveStreams"/>): where it lies (<see cref="PlanStreams"/>, read
	/// only from the system's shapes) and the bed the field cuts for it — a stream's channel under the bench beside a passage's
	/// way, a pool's bowl, a fall's plunge pool — unioned into the void beside the lava's (the Tube hook in CaveSystem.cs), so
	/// everything that reads the field (the mesher, the dressing, the walk checks) sees one cave.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Planned as the lava is, with the lava's rules.</b> The ways, kept places, features and levels the lava keeps off
	/// (CaveSystem.Lava.cs: <c>LavaLanes</c>, <c>LavaClear</c>, <c>LavaOverLower</c>, its samples, banks and floors measured
	/// without any bed cut) are the water's too, read through the same scratch under the same lock: a cave either runs molten or
	/// holds water (<see cref="CaveStreams.Wet"/>), never both, so the two plans never meet.
	/// </para>
	/// <para>
	/// <b>Cut after planning.</b> Held while the layout plans (it holds the lava, and the water with it, until the system
	/// stands), then cut on every field preparation after, whatever later becomes of <see cref="LavaHeld"/> (a check that
	/// compares a cave with and without its lava leaves the water be); <see cref="StreamsHeld"/> holds it for a check of its own.
	/// A frozen plan (an ice cave's) is never cut.
	/// </para>
	/// </remarks>
	public sealed partial class CaveSystem
	{
		private bool streamsHeld, streamsReleased;
		private StreamBed streamBed;
		/// <summary>One plan's scratch (under the lava's lock): the streams laid so far that come to rest rather than sink, for a pool at their ends.</summary>
		private List<int> streamResting;

		/// <summary>Whether the field leaves the system's water uncut (its channels and pools), for a check that compares the cave with and without it. Setting it cuts the field again.</summary>
		public bool StreamsHeld
		{
			get => streamsHeld;
			set
			{
				if (value == streamsHeld)
				{
					return;
				}
				streamsHeld = value;
				if (buckets != null)
				{
					PrepareField();
				}
			}
		}

		/// <summary>Whether the field has any water's bed cut into it.</summary>
		public bool StreamsCut => streamBed != null;

		/// <summary>A system's roll for whether it holds water (<see cref="CaveStreams.Wet"/>), 0 .. 1 from its seed.</summary>
		public static float StreamRoll(int seed) => Unit(unchecked((uint)seed), 0x57EA3u);

		/// <summary>Cuts the water's beds into the field as the system now stands (from <see cref="PrepareField"/>; none while the layout plans).</summary>
		private void PrepareStreams()
		{
			streamBed = null;
			// The layout plans with its lava held (CaveSystem.Plan), the water with it; once it stands, the water is its own.
			if (lavaHeld && !streamsReleased)
			{
				return;
			}
			streamsReleased = true;
			if (streamsHeld)
			{
				return;
			}
			CaveStreamPlan plan = CaveStreams.Plan(this);
			if (plan.Active && !plan.Frozen && plan.Bodies.Count > 0)
			{
				streamBed = StreamBed.Build(plan, Seed);
			}
		}

		/// <summary>The void with the water's beds cut into it (unchanged where none is cut).</summary>
		private float WithStreams(Vector3 p, float d) => streamBed == null ? d : Mathf.Min(d, streamBed.At(p));

		// ── The bed ───────────────────────────────────────────────

		/// <summary>
		/// The beds cut for a plan's water: each stretch of a stream with banks a channel piece (a flat-floored slot between two
		/// points, open from its bed up past the floor across it), each pool with banks a bowl (deepest at its middle, a shallow
		/// shelf at its edge); bucketed in plan, built whole before the field is read (the mesher reads it from several threads).
		/// </summary>
		private sealed class StreamBed
		{
			private const float Cell = 8f;

			private struct Piece
			{
				public Vector3 A, B;
				public float Half, BedA, BedB, TopA, TopB, Shelf;
				public bool Basin;
			}

			private readonly List<Piece> pieces = new List<Piece>();
			private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
			private int seed;

			public static StreamBed Build(CaveStreamPlan plan, int seed)
			{
				var bed = new StreamBed { seed = seed };
				foreach (CaveStreamBody body in plan.Bodies)
				{
					if (body.Banks == null || body.Tops == null || body.Path == null || body.Path.Length == 0)
					{
						continue;
					}
					if (body.Kind == CaveStreamKind.Pool)
					{
						Vector3 c = body.Path[0];
						float bottom = c.y - body.Depth;
						bed.Add(new Piece { A = c, B = c, Half = body.Width, BedA = bottom, BedB = bottom, TopA = body.Tops[0], TopB = body.Tops[0], Shelf = c.y - 0.3f, Basin = true });
						continue;
					}
					for (int i = 0; i + 1 < body.Path.Length; i++)
					{
						if (float.IsNaN(body.Banks[i]) || float.IsNaN(body.Banks[i + 1]))
						{
							continue;
						}
						Vector3 a = body.Path[i], b = body.Path[i + 1];
						bed.Add(new Piece
						{
							A = a,
							B = b,
							Half = 0.5f * body.Width,
							BedA = a.y - body.Depth,
							BedB = b.y - body.Depth,
							TopA = body.Tops[i],
							TopB = body.Tops[i + 1],
						});
					}
				}
				// Nothing to cut (a plan of flooded basins, winzes and dead ends only): no bed at all.
				return bed.pieces.Count > 0 ? bed : null;
			}

			private void Add(Piece piece)
			{
				int id = pieces.Count;
				pieces.Add(piece);
				float r = piece.Half + 0.5f;
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
					float off = (ap - ab * t).magnitude;
					float across = off - piece.Half;
					float bed = piece.BedA + (piece.BedB - piece.BedA) * t, top = piece.TopA + (piece.TopB - piece.TopA) * t;
					if (piece.Basin)
					{
						// A bowl, not a well: deepest at its middle, shelving up to a wading edge at its rim.
						float u = Mathf.Clamp01(off / Mathf.Max(0.5f, piece.Half));
						bed = Mathf.Lerp(bed, piece.Shelf, u * u);
					}
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
				// Its sides a little uneven, as rock the water wore: never enough to close a channel or reach the lane beside it.
				return d - 0.08f * ProceduralNoise.Gradient3(p / 0.7f, seed + 67);
			}
		}

		// ── The plan ──────────────────────────────────────────────


		private static bool StreamRoom(CaveStreamPlan plan, int bodies = 1) => plan.Bodies.Count + bodies <= CaveStreams.MaxBodies;

		/// <summary>
		/// Whether a bed (system frame y) would lie under an awash cave's own water (a grotto's pool) or within a hand of it: its
		/// pool floods every void under its level (CaveShaper.WritePool), so a channel or bowl cut down there would be the pool's.
		/// </summary>
		private bool Awash(float bed) => !float.IsNaN(WaterLevel) && Origin.y + bed < WaterLevel + AwashClear;

		/// <summary>How far over an awash cave's own water every bed of its streams and pools stays, metres.</summary>
		public const float AwashClear = 0.2f;

		/// <summary>
		/// The system's water (<see cref="CaveStreams.Plan"/>, for a wet one): a mine's flooded winzes, streams down its passages
		/// in channels beside their ways (a spring pouring out of the wall into each one's head), falls over its chasms' lips and
		/// down from its roofs' avens into plunge pools, pools in its chambers, water standing in its basins and gours, a flooded
		/// dead end. Reads the system's shapes, and its floors as the field cuts them without any bed. <paramref name="frozen"/>
		/// lays the same water on the floors as ice (an ice cave's): nothing to cut.
		/// </summary>
		internal CaveStreamPlan PlanStreams(bool frozen)
		{
			if (buckets == null)
			{
				Prepare();
			}
			var plan = new CaveStreamPlan { Frozen = frozen };
			// The lava's scratch (its ways, kept places and laid bodies) under its lock: one plan at a time, lava or water.
			lock (lavaPlanning)
			{
				LavaLanes();
				lavaLaid = new List<(Vector3, float, int)>();
				streamResting = new List<int>();
				try
				{
					// The bodies are capped: a mine's sumps first, the trunks' streams, the chasms' and avens' falls (a stream ending by
					// a chasm pours over it before it pools), the pools where the trunks' streams come to rest, the chambers' pools,
					// the basins and gours, the other passages' streams and their pools, a flooded dead end.
					Winzes(plan);
					for (int p = 0; p < Passages.Count; p++)
					{
						if (Passages[p].Kind == CavePassageKind.Trunk)
						{
							StreamDown(plan, p);
						}
					}
					for (int r = 0; r < Rifts.Count; r++)
					{
						StreamChasmFall(plan, r);
					}
					AvenFalls(plan);
					ChamberStreams(plan);
					GrottoSpring(plan);
					EndPools(plan);
					StreamPools(plan);
					StreamBasins(plan);
					for (int p = 0; p < Passages.Count; p++)
					{
						if (Passages[p].Kind != CavePassageKind.Trunk)
						{
							StreamDown(plan, p);
						}
					}
					EndPools(plan);
					FloodedEnd(plan);
				}
				finally
				{
					lavaWays = null;
					lavaKeeps = null;
					lavaLaid = null;
					streamResting = null;
				}
			}
			plan.Active = plan.Bodies.Count > 0;
			if (frozen)
			{
				Freeze(plan);
			}
			return plan;
		}

		/// <summary>An ice cave's water laid on its floors: each body's surface down to the floor beside it where a bed would have been cut.</summary>
		private static void Freeze(CaveStreamPlan plan)
		{
			for (int b = 0; b < plan.Bodies.Count; b++)
			{
				CaveStreamBody body = plan.Bodies[b];
				if (body.Banks == null)
				{
					continue;
				}
				var path = (Vector3[])body.Path.Clone();
				for (int i = 0; i < path.Length && i < body.Banks.Length; i++)
				{
					if (!float.IsNaN(body.Banks[i]))
					{
						path[i].y = body.Banks[i];
					}
				}
				body.Path = path;
				plan.Bodies[b] = body;
			}
		}

		/// <summary>A body's own random number, 0 .. 1, from the seed and a salt.</summary>
		private float StreamUnit(uint salt, uint index, uint k) => Unit(Mix(unchecked((uint)Seed), salt + index * 0x9E37u), k);

		// ── Streams ───────────────────────────────────────────────

		/// <summary>
		/// The stream down a passage, if it runs one (most trunks; side passages and links by the seed; a mine's drainage ditch
		/// now and then): downstream from its higher end, in a channel along whichever wall leaves it the longer runs, its surface
		/// <see cref="CaveStreams.Drop"/> under the bench beside it, falling with that floor and never rising; split where it must
		/// not lie, each run a body: a run after a short gap carries its level (it ran on under a natural bridge), any other wells
		/// up from a spring pouring out of the wall into its head. Where its end comes to rest, a pool if one fits.
		/// </summary>
		private void StreamDown(CaveStreamPlan plan, int pi)
		{
			CavePassage passage = Passages[pi];
			int count = passage.Nodes.Count;
			if (count < 3 || !StreamRoom(plan))
			{
				return;
			}
			bool mine = Form == CaveForm.Mine;
			float odds = passage.Kind == CavePassageKind.Trunk ? (mine ? 0.35f : 0.8f) : passage.Kind == CavePassageKind.Link ? 0.45f : (mine ? 0.15f : 0.4f);
			if (StreamUnit(0x57EA1u, (uint)pi, 1) >= odds)
			{
				return;
			}
			var radii = new List<float>();
			foreach (CaveNode n in passage.Nodes)
			{
				radii.Add(n.Radius);
			}
			radii.Sort();
			float narrow = radii[radii.Count / 4];
			// A mine's is a ditch cut along its drift's wall; a cave's as wide as its bore lets it be beside the lane.
			float most = mine ? 1.1f : Mathf.Clamp(0.32f * narrow, CaveStreams.MinWidth, CaveStreams.MaxWidth);
			float width = Mathf.Min(most, FloorHalf(narrow) + ChannelUnder - CaveLava.LaneClearLeast);
			if (width < (mine ? 0.8f : CaveStreams.MinWidth))
			{
				return;
			}
			float inner = Mathf.Clamp(FloorHalf(narrow) + ChannelUnder - width, CaveLava.LaneClearLeast, CaveLava.LaneClear);
			bool reverse = passage.Nodes[count - 1].Floor > passage.Nodes[0].Floor + 0.3f;
			float offset = inner + 0.5f * width;
			List<LavaSample> right = LavaSamples(passage, reverse, 1f, offset, width), left = LavaSamples(passage, reverse, -1f, offset, width);
			int openRight = 0, openLeft = 0;
			foreach (LavaSample s in right) openRight += s.Open ? 1 : 0;
			foreach (LavaSample s in left) openLeft += s.Open ? 1 : 0;
			List<LavaSample> samples = openRight > openLeft || openRight == openLeft && StreamUnit(0x57EA1u, (uint)pi, 2) < 0.5f ? right : left;
			MeasureBanks(samples, width);
			// The floor a stride on toward the lane as well: a bore's floor dips toward its middle (its segments' smooth unions), and
			// water standing over that dip would be dammed only by the channel's lip.
			for (int k = 0; k < samples.Count; k++)
			{
				LavaSample sample = samples[k];
				if (!sample.Open)
				{
					continue;
				}
				float lane = HeldFloor(sample.At - sample.Side * (0.5f * width + 0.8f), sample.Line.y + 0.6f);
				if (!float.IsNaN(lane) && lane < sample.Bank)
				{
					sample.Bank = lane;
				}
				// Never down into an awash cave's own water (a grotto's pool stands at its level): it ends above it.
				if (Awash(sample.Bank - CaveStreams.Drop - CaveStreams.StreamDepth))
				{
					sample.Open = false;
				}
				samples[k] = sample;
			}

			const int MinRun = 9, Gap = 3;
			float carried = float.NaN;
			int lastEnd = -1000;
			int i = 0;
			while (i < samples.Count && StreamRoom(plan))
			{
				if (!samples[i].Open)
				{
					i++;
					continue;
				}
				bool welling = float.IsNaN(carried) || i - lastEnd > 15 || samples[i].Bank - carried > CaveStreams.MaxSink;
				float surface = welling ? samples[i].Bank - CaveStreams.Drop : Mathf.Min(carried, samples[i].Bank - CaveStreams.Drop);
				var path = new List<Vector3>();
				var banks = new List<float>();
				var tops = new List<float>();
				int j = i;
				bool sank = false;
				for (; j < samples.Count && samples[j].Open; j++)
				{
					float bank = samples[j].Bank;
					surface = Mathf.Min(surface, bank - CaveStreams.Drop);
					if (bank - surface > CaveStreams.MaxSink)
					{
						sank = true;
						break;
					}
					path.Add(new Vector3(samples[j].At.x, surface, samples[j].At.z));
					banks.Add(bank);
					tops.Add(samples[j].Top);
				}
				if (path.Count >= MinRun)
				{
					var falls = new List<bool>(new bool[path.Count]);
					// A fresh run wells up out of the wall: a spring pouring into its head where the wall stands over it.
					if (welling)
					{
						Spring(samples[i], path, banks, tops, falls);
					}
					int index = plan.Bodies.Count;
					plan.Bodies.Add(new CaveStreamBody
					{
						Kind = CaveStreamKind.Stream,
						Path = path.ToArray(),
						Falls = falls.Contains(true) ? falls.ToArray() : null,
						Width = width,
						Depth = CaveStreams.StreamDepth,
						Banks = banks.ToArray(),
						Tops = tops.ToArray(),
						Passage = pi,
						Chamber = -1,
						Pit = -1,
						Into = -1,
					});
					for (int k = i; k < j; k++)
					{
						lavaLaid.Add((samples[k].At, 0.5f * width, index));
					}
					if (!sank)
					{
						streamResting.Add(index);
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

		/// <summary>
		/// A grotto's spring (its one chamber too small to run a stream past its kept ledge), by the seed: pouring out of a crack
		/// in its wall a few metres over its pool, straight into the pool (CaveStreamWater runs it into the grotto's own lake).
		/// </summary>
		private void GrottoSpring(CaveStreamPlan plan)
		{
			if (Form != CaveForm.Grotto || float.IsNaN(WaterLevel) || !StreamRoom(plan) || StreamUnit(0x57EC5u, 0, 1) >= 0.75f)
			{
				return;
			}
			float level = WaterLevel - Origin.y;
			float rise = Mathf.Lerp(2.2f, 3.4f, StreamUnit(0x57EC5u, 0, 2));
			float turn = 360f * StreamUnit(0x57EC5u, 0, 3);
			for (int ci = 0; ci < Chambers.Count; ci++)
			{
				CaveChamber c = Chambers[ci];
				float most = 1.6f * Mathf.Max(c.Radii.x, c.Radii.z);
				for (int k = 0; k < 12; k++)
				{
					Vector3 dir = LavaDir(turn + 30f * k);
					var from = new Vector3(c.Centre.x, level + rise, c.Centre.z);
					if (HeldVoid(from) >= 0f)
					{
						break;
					}
					float out_ = 0f;
					while (HeldVoid(from + dir * (out_ + 0.1f)) < 0f && out_ < most)
					{
						out_ += 0.1f;
					}
					if (out_ >= most || out_ < 1.5f)
					{
						continue;
					}
					Vector3 lip = from + dir * (out_ - 0.3f);
					var landing = new Vector3(lip.x - dir.x * 0.7f, level, lip.z - dir.z * 0.7f);
					// Into the pool (water a good way down under where it lands), the whole fall open.
					bool open = HeldVoid(landing + Vector3.down * 0.4f) < 0f;
					for (float f = 0.25f; f < 0.95f && open; f += 0.25f)
					{
						open = Void(Vector3.Lerp(lip, landing, f) + Vector3.up * 0.25f, out _, false) < 0f;
					}
					if (!open || LavaOverLower(landing, 1f))
					{
						continue;
					}
					plan.Bodies.Add(new CaveStreamBody
					{
						Kind = CaveStreamKind.Stream,
						Path = new[] { lip, Vector3.Lerp(lip, landing, 0.5f), landing },
						Falls = new[] { false, true, true },
						Width = 0.7f,
						Depth = 0.3f,
						Banks = new[] { float.NaN, float.NaN, float.NaN },
						Tops = new[] { float.NaN, float.NaN, float.NaN },
						Passage = -1,
						Chamber = ci,
						Pit = -1,
						Into = -1,
					});
					return;
				}
			}
		}

		/// <summary>
		/// Streams down sloping chambers' floors (a grotto's above all, its back a dry ledge rising out of its pool), by the seed:
		/// from a spring in the wall at the chamber's high end, down its fall line to one side of its kept middle, ending where it
		/// must not run on (above a grotto's own pool, at another way) in a pool if one fits, else sinking into its bed.
		/// </summary>
		private void ChamberStreams(CaveStreamPlan plan)
		{
			for (int ci = 0; ci < Chambers.Count && StreamRoom(plan, 2); ci++)
			{
				CaveChamber c = Chambers[ci];
				float small = Mathf.Min(c.Radii.x, c.Radii.z);
				if (Mathf.Abs(c.Slope) < 0.08f || small < 4f || StreamUnit(0x57EC1u, (uint)ci, 1) >= (Form == CaveForm.Grotto ? 0.85f : 0.3f))
				{
					continue;
				}
				// Down the fall line: the floor rises along the chamber's heading by its slope.
				Vector3 uphill = LavaDir(c.Yaw) * Mathf.Sign(c.Slope);
				var across = new Vector3(uphill.z, 0f, -uphill.x);
				float first = StreamUnit(0x57EC1u, (uint)ci, 2) < 0.5f ? 1f : -1f;
				float width = Mathf.Clamp(0.1f * small, CaveStreams.MinWidth, 1.8f);
				foreach (float share in new[] { 0.45f, 0.6f, 0.75f })
				{
					bool laid = false;
					foreach (float side in new[] { first, -first })
					{
						if (ChamberStream(plan, ci, uphill, across * side, share, width))
						{
							laid = true;
							break;
						}
					}
					if (laid)
					{
						break;
					}
				}
			}
		}

		/// <summary>One chamber's stream, its line <paramref name="share"/> of the way out from the middle across the fall line; false where it would not run far enough.</summary>
		private bool ChamberStream(CaveStreamPlan plan, int ci, Vector3 uphill, Vector3 side, float share, float width)
		{
			CaveChamber c = Chambers[ci];
			float off = share * FloorReach(c, side);
			float top = 0.85f * FloorReach(c, uphill), bottom = -0.95f * FloorReach(c, -uphill);
			var samples = new List<LavaSample>();
			for (float t = top; t >= bottom; t -= 1f)
			{
				Vector3 at = c.Centre + side * off + uphill * t;
				at.y = FloorOf(c, at);
				var sample = new LavaSample { Line = at, Side = side, At = at, Radius = Mathf.Max(c.Radii.x, c.Radii.z), Height = c.Radii.y };
				sample.Open = OverFloor(c, at + side * (0.5f * width + 0.5f), -0.3f) && LavaClear(at, 0.5f * width);
				if (sample.Open)
				{
					float from = at.y + 0.8f;
					float near = HeldFloor(at - side * (0.5f * width + 0.8f), from), middle = HeldFloor(at, from), far = HeldFloor(at + side * (0.5f * width), from);
					sample.Open = !float.IsNaN(near) && !float.IsNaN(middle);
					if (sample.Open)
					{
						sample.Bank = Mathf.Min(near, middle);
						sample.Top = float.IsNaN(far) ? Mathf.Max(near, middle) + 0.6f : Mathf.Max(Mathf.Max(near, middle), far) + 0.3f;
						sample.Open = !Awash(sample.Bank - CaveStreams.Drop - CaveStreams.StreamDepth);
					}
				}
				samples.Add(sample);
			}
			// Its longest open run from the top, falling with the floor.
			int start = samples.FindIndex(q => q.Open);
			if (start < 0)
			{
				return false;
			}
			var path = new List<Vector3>();
			var banks = new List<float>();
			var tops = new List<float>();
			float surface = samples[start].Bank - CaveStreams.Drop;
			int j = start;
			for (; j < samples.Count && samples[j].Open; j++)
			{
				surface = Mathf.Min(surface, samples[j].Bank - CaveStreams.Drop);
				if (samples[j].Bank - surface > CaveStreams.MaxSink)
				{
					break;
				}
				path.Add(new Vector3(samples[j].At.x, surface, samples[j].At.z));
				banks.Add(samples[j].Bank);
				tops.Add(samples[j].Top);
			}
			if (path.Count < 7)
			{
				return false;
			}
			var falls = new List<bool>(new bool[path.Count]);
			// Its spring: out of the wall at the high end (the head's "side" is up the fall line, toward that wall).
			LavaSample head = samples[start];
			head.Side = uphill;
			Spring(head, path, banks, tops, falls);
			int index = plan.Bodies.Count;
			plan.Bodies.Add(new CaveStreamBody
			{
				Kind = CaveStreamKind.Stream,
				Path = path.ToArray(),
				Falls = falls.Contains(true) ? falls.ToArray() : null,
				Width = width,
				Depth = CaveStreams.StreamDepth,
				Banks = banks.ToArray(),
				Tops = tops.ToArray(),
				Passage = -1,
				Chamber = ci,
				Pit = -1,
				Into = -1,
			});
			for (int k = start; k < j; k++)
			{
				lavaLaid.Add((samples[k].At, 0.5f * width, index));
			}
			streamResting.Add(index);
			return true;
		}

		/// <summary>
		/// A spring pouring out of a crack in the wall over a stream's head into it: from as high as the wall stands over the
		/// channel (the fall at least <see cref="CaveStreams.MinFall"/>, drawn as one), landing on the stream a little way down it.
		/// The run before the landing is dropped, so no dry channel lies upstream of the water. Nothing where the head lies in a
		/// room, out from its walls.
		/// </summary>
		private void Spring(in LavaSample head, List<Vector3> path, List<float> banks, List<float> tops, List<bool> falls)
		{
			for (float up = 3f; up >= 1.8f; up -= 0.4f)
			{
				Vector3 vent = head.At;
				vent.y = head.Line.y + up;
				// Open on the rough field too (a knob of the wall over the channel: marching out from inside it, the crack was set in the rock).
				if (HeldVoid(vent) >= 0f || Void(vent, out _, false) >= 0f)
				{
					continue;
				}
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
				// Just out of the face, so the water leaves the rock in the open.
				vent -= head.Side * 0.3f;
				for (int k = 0; k < Mathf.Min(path.Count - 6, 6); k++)
				{
					Vector3 landing = path[k];
					float drop = vent.y - landing.y;
					if (drop < CaveStreams.MinFall || Plan2(vent, landing) < 0.6f)
					{
						continue;
					}
					// Its fall open the whole way down (a wall flaring out at its foot would stand in it).
					bool open = true;
					for (float f = 0.25f; f < 0.95f && open; f += 0.25f)
					{
						open = Void(Vector3.Lerp(vent, landing, f) + Vector3.up * 0.25f, out _, false) < 0f;
					}
					if (!open)
					{
						continue;
					}
					path.RemoveRange(0, k);
					banks.RemoveRange(0, k);
					tops.RemoveRange(0, k);
					falls.RemoveRange(0, k);
					Vector3 mid = Vector3.Lerp(vent, landing, 0.5f);
					path.InsertRange(0, new[] { vent, mid });
					banks.InsertRange(0, new[] { float.NaN, float.NaN });
					tops.InsertRange(0, new[] { float.NaN, float.NaN });
					falls.InsertRange(0, new[] { false, true });
					falls[2] = true;
					return;
				}
			}
		}

		/// <summary>A pool at the end of each stream laid since the last call that comes to rest and pours over nothing (<see cref="EndPool"/>).</summary>
		private void EndPools(CaveStreamPlan plan)
		{
			foreach (int si in streamResting)
			{
				CaveStreamBody stream = plan.Bodies[si];
				if (stream.Into < 0 && (stream.Falls == null || !stream.Falls[stream.Falls.Length - 1]))
				{
					EndPool(plan, si);
				}
			}
			streamResting.Clear();
		}

		/// <summary>
		/// A pool where a stream comes to rest (its channel closes before something it must not reach, or its passage ends): just
		/// past its end, as broad as fits there; the stream runs into it. None where none fits: the stream sinks into its bed (a swallet).
		/// </summary>
		private void EndPool(CaveStreamPlan plan, int si)
		{
			if (!StreamRoom(plan))
			{
				return;
			}
			CaveStreamBody stream = plan.Bodies[si];
			int n = stream.Path.Length;
			Vector3 end = stream.Path[n - 1], before = stream.Path[Mathf.Max(0, n - 4)];
			Vector3 run = FlatDir(end - before);
			float wanted = Mathf.Clamp(1.2f * stream.Width + 0.4f, 1.4f, 2.6f);
			foreach (float share in new[] { 0.6f, 0.2f, -0.3f })
			{
				var at = new Vector3(end.x, stream.Banks[n - 1], end.z) + run * (wanted * share);
				int pool = StreamPool(plan, -1, at, wanted, si, out float level, true, end.y);
				if (pool < 0)
				{
					continue;
				}
				if (level > end.y + 0.05f || Plan2(plan.Bodies[pool].Path[0], end) > plan.Bodies[pool].Width + 0.2f)
				{
					// Its level over the stream's end (it could not have run into it), or shrunk away from the end.
					plan.Bodies.RemoveAt(pool);
					lavaLaid.RemoveAll(l => l.body == pool);
					continue;
				}
				CaveStreamBody pooled = plan.Bodies[pool];
				var path = new List<Vector3>(stream.Path) { new Vector3(pooled.Path[0].x, level, pooled.Path[0].z) };
				var banks = new List<float>(stream.Banks) { float.NaN };
				var tops = new List<float>(stream.Tops) { float.NaN };
				stream.Path = path.ToArray();
				stream.Banks = banks.ToArray();
				stream.Tops = tops.ToArray();
				if (stream.Falls != null)
				{
					var falls = new List<bool>(stream.Falls) { false };
					stream.Falls = falls.ToArray();
				}
				stream.Into = pool;
				plan.Bodies[si] = stream;
				return;
			}
		}

		// ── Pools ─────────────────────────────────────────────────

		/// <summary>
		/// A pool centred at a point on a floor, as broad as fits there up to <paramref name="wanted"/> (no less than
		/// <see cref="PoolLeast"/>): floor all round it as the field cuts it, near level, clear of everything (but
		/// <paramref name="exceptBody"/>'s own, a stream it ends) — by a lane's width, or, <paramref name="close"/>, by a pace (a
		/// pool a stream or a fall feeds lies where the water comes, its shelving edge at the lane's). Its surface no higher than
		/// <paramref name="under"/>. Laid into the plan with its bowl; its index, or −1 where none fits.
		/// </summary>
		private int StreamPool(CaveStreamPlan plan, int chamber, Vector3 at, float wanted, int exceptBody, out float level, bool close = false, float under = float.MaxValue)
		{
			level = float.NaN;
			if (!StreamRoom(plan))
			{
				return -1;
			}
			float from = at.y + 0.8f;
			float centre = HeldFloor(at, from);
			if (float.IsNaN(centre))
			{
				return -1;
			}
			at.y = centre;
			for (float r = wanted; r >= PoolLeast - 1e-3f; r = r > PoolLeast ? Mathf.Max(PoolLeast, r - 0.4f) : 0f)
			{
				if (!LavaClear(at, r + (close ? 0.3f : WayLane - 0.4f), -1, exceptBody))
				{
					continue;
				}
				// Its rim and its middle as the field cuts them: floor all round it, a modest slope at most (a pool on a steep
				// floor would stand a wall over its water on its high side).
				float low = centre, high = centre;
				bool fits = true;
				for (int k = 0; k < 12 && fits; k++)
				{
					Vector3 q = at + LavaDir(30f * k) * (r + 0.6f);
					float found = HeldFloor(q, from + 0.6f);
					fits = !float.IsNaN(found);
					if (fits)
					{
						low = Mathf.Min(low, found);
						high = Mathf.Max(high, found);
					}
				}
				if (!fits || high - low > 1.6f)
				{
					continue;
				}
				level = Mathf.Min(low - CaveStreams.PoolDrop, under);
				int index = plan.Bodies.Count;
				float depth = Mathf.Max(0.6f, Mathf.Lerp(CaveStreams.PoolDepthMin, CaveStreams.PoolDepthMax, StreamUnit(0x57EB0u, (uint)index, 1)) * Mathf.Clamp01(0.4f + 0.3f * r));
				if (Awash(level - depth))
				{
					continue;
				}
				plan.Bodies.Add(new CaveStreamBody
				{
					Kind = CaveStreamKind.Pool,
					Path = new[] { new Vector3(at.x, level, at.z) },
					Width = r,
					Depth = depth,
					Banks = new[] { low },
					Tops = new[] { high + 0.4f },
					Passage = -1,
					Chamber = chamber,
					Pit = -1,
					Into = -1,
				});
				lavaLaid.Add((at, r, index));
				return index;
			}
			return -1;
		}

		/// <summary>The smallest pool laid on a floor (its radius), metres.</summary>
		private const float PoolLeast = 1.2f;

		/// <summary>
		/// Pools in chambers, by the seed (most in a cavern, fewer in halls and rooms, now and then a stope's floor flooded): one
		/// to a chamber, at most one, three or five by the system's size; each searched for on rings round the chamber's middle
		/// (from a turn its seed picks) as the lava's lakes are.
		/// </summary>
		private void StreamPools(CaveStreamPlan plan)
		{
			int made = 0, most = 1 + 2 * Mathf.Clamp(SizeClass, 0, 2);
			for (int ci = 0; ci < Chambers.Count && made < most && StreamRoom(plan); ci++)
			{
				CaveChamber c = Chambers[ci];
				float small = Mathf.Min(c.Radii.x, c.Radii.z);
				if (small < 4f)
				{
					continue;
				}
				float odds = c.Kind == CaveChamberKind.Cavern ? 0.6f : c.Kind == CaveChamberKind.Hall ? 0.4f : c.Kind == CaveChamberKind.Stope ? 0.3f : 0.35f;
				if (StreamUnit(0x57EB1u, (uint)ci, 1) >= odds)
				{
					continue;
				}
				float wanted = Mathf.Clamp(small * Mathf.Lerp(0.14f, 0.28f, StreamUnit(0x57EB1u, (uint)ci, 2)), PoolLeast, 7f);
				float turn = 360f * StreamUnit(0x57EB1u, (uint)ci, 3);
				bool laid = false;
				for (int ring = 0; ring < 5 && !laid; ring++)
				{
					float share = 0.35f + 0.12f * ring;
					for (int k = 0; k < 16 && !laid; k++)
					{
						Vector3 dir = LavaDir(turn + 22.5f * k + 11.25f * ring);
						Vector3 at = c.Centre + dir * (FloorReach(c, dir) * share);
						at.y = FloorOf(c, at);
						if (!OverFloor(c, at + dir * (wanted + 0.6f), -0.5f))
						{
							continue;
						}
						laid = StreamPool(plan, ci, at, wanted, -1, out _) >= 0;
					}
				}
				made += laid ? 1 : 0;
			}
		}

		/// <summary>
		/// The variety pass's basins (potholes, sinkholes) and gours with water standing in them, by the seed: a basin to
		/// <see cref="CaveStreams.PoolDrop"/> under the lowest of its rim, a gour brimming just under its rimstone lip. Nothing is
		/// cut: the basin is the bed.
		/// </summary>
		private void StreamBasins(CaveStreamPlan plan)
		{
			for (int pi = 0; pi < Pits.Count && StreamRoom(plan); pi++)
			{
				CavePit pit = Pits[pi];
				bool gour = pit.Kind == CavePitKind.Gour;
				if (pit.Kind != CavePitKind.Pothole && pit.Kind != CavePitKind.Sinkhole && !gour || pit.Hole < 0 || pit.Hole >= Hollows.Count
					|| Hollows[pit.Hole].Kind != CaveHollowKind.Bowl || !float.IsNaN(pit.Water)
					|| StreamUnit(0x57EB5u, (uint)pi, 1) >= (gour ? 0.9f : 0.7f))
				{
					continue;
				}
				CaveHollow bowl = Hollows[pit.Hole];
				bool kept = false;
				foreach ((Vector3 at, float radius, float height) in lavaKeeps)
				{
					kept |= Plan2(bowl.A, at) < radius + bowl.Radius && Mathf.Abs(bowl.A.y - at.y) < height;
				}
				if (kept || LavaOverLower(bowl.A, bowl.Radius))
				{
					continue;
				}
				float spill = bowl.Radius + Mathf.Max(0f, bowl.Top - bowl.A.y) / BowlGrade;
				float rim = float.MaxValue;
				for (int k = 0; k < 8; k++)
				{
					float found = HeldFloor(bowl.A + LavaDir(45f * k) * (spill + 0.3f), bowl.A.y + 1f);
					rim = float.IsNaN(found) ? rim : Mathf.Min(rim, found);
				}
				float bed = bowl.A.y - bowl.Depth, surface = Mathf.Min(bowl.A.y, rim) - (gour ? 0.06f : CaveStreams.PoolDrop);
				if (rim == float.MaxValue || surface - bed < (gour ? 0.15f : 0.3f) || Awash(bed))
				{
					continue;
				}
				float flat = Mathf.Max(0.3f * bowl.Radius, bowl.Radius - bowl.Depth / BowlGrade);
				float reach = Mathf.Min(bowl.Radius, flat + (surface - bed) / BowlGrade);
				int chamber = Chambers.FindIndex(c => OverFloor(c, bowl.A, 0.5f));
				int index = plan.Bodies.Count;
				plan.Bodies.Add(new CaveStreamBody
				{
					Kind = CaveStreamKind.Pool,
					Path = new[] { new Vector3(bowl.A.x, surface, bowl.A.z) },
					Width = reach,
					Depth = surface - bed,
					Passage = -1,
					Chamber = chamber,
					Pit = pi,
					Into = -1,
				});
				lavaLaid.Add((bowl.A, bowl.Radius, index));
			}
		}

		/// <summary>A mine's flooded winzes (the layout's sumps under its drainage level): water up the shaft to its level, its ladder going down into it.</summary>
		private void Winzes(CaveStreamPlan plan)
		{
			for (int pi = 0; pi < Pits.Count && StreamRoom(plan); pi++)
			{
				CavePit pit = Pits[pi];
				if (pit.Kind != CavePitKind.Winze || float.IsNaN(pit.Water) || pit.Hole < 0 || pit.Hole >= Shafts.Count)
				{
					continue;
				}
				CaveShaft shaft = Shafts[pit.Hole];
				float level = pit.Water - Origin.y;
				if (level <= shaft.Bottom.y + 0.2f)
				{
					continue;
				}
				int index = plan.Bodies.Count;
				plan.Bodies.Add(new CaveStreamBody
				{
					Kind = CaveStreamKind.Pool,
					Path = new[] { new Vector3(shaft.Bottom.x, level, shaft.Bottom.z) },
					Width = shaft.Radius * 1.42f + 0.3f,
					Depth = level - shaft.Bottom.y,
					Passage = -1,
					Chamber = -1,
					Pit = pi,
					Into = -1,
				});
				lavaLaid.Add((new Vector3(shaft.Bottom.x, level, shaft.Bottom.z), shaft.Radius * 1.42f, index));
			}
		}

		/// <summary>
		/// A flooded dead end (a sump), by the seed: the lowest passage end the system has that leads nowhere (no chamber, no other
		/// passage, no mouth, no climb near it), its water standing knee to thigh deep over the floor and back along the passage
		/// as far as the floor lies under it. Nothing is cut; nothing lies beyond it to be kept from.
		/// </summary>
		private void FloodedEnd(CaveStreamPlan plan)
		{
			if (!StreamRoom(plan) || StreamUnit(0x57EB8u, 0, 1) >= (Form == CaveForm.Mine ? 0.6f : 0.45f))
			{
				return;
			}
			int best = -1;
			float lowest = float.MaxValue;
			for (int pi = 0; pi < Passages.Count; pi++)
			{
				CavePassage p = Passages[pi];
				if (p.Nodes.Count < 4)
				{
					continue;
				}
				CaveNode end = p.Nodes[p.Nodes.Count - 1];
				// Lower than the way to it, so the water gathers there.
				if (end.Floor > p.Nodes[p.Nodes.Count - 3].Floor + 0.05f || end.Floor >= lowest)
				{
					continue;
				}
				if (!DeadEnd(pi))
				{
					continue;
				}
				best = pi;
				lowest = end.Floor;
			}
			if (best < 0)
			{
				return;
			}
			CaveNode last = Passages[best].Nodes[Passages[best].Nodes.Count - 1];
			var at = new Vector3(last.Centre.x, last.Floor, last.Centre.z);
			float floor = HeldFloor(at, last.Floor + 0.8f);
			if (float.IsNaN(floor))
			{
				return;
			}
			float depth = Mathf.Lerp(0.35f, 0.75f, StreamUnit(0x57EB8u, 0, 2));
			float reach = Mathf.Clamp(last.Radius * 2.5f, 3f, 7f);
			if (LavaOverLower(at, reach) || Awash(floor))
			{
				return;
			}
			int index = plan.Bodies.Count;
			plan.Bodies.Add(new CaveStreamBody
			{
				Kind = CaveStreamKind.Pool,
				Path = new[] { new Vector3(at.x, floor + depth, at.z) },
				Width = reach,
				Depth = depth,
				Passage = best,
				Chamber = -1,
				Pit = -1,
				Into = -1,
			});
			lavaLaid.Add((at, reach, index));
		}

		/// <summary>Whether a passage's far end leads nowhere: no chamber over it, no other passage near it, no mouth, climb, shaft, chasm, solid or water.</summary>
		private bool DeadEnd(int pi)
		{
			CavePassage passage = Passages[pi];
			CaveNode end = passage.Nodes[passage.Nodes.Count - 1];
			var at = new Vector3(end.Centre.x, end.Floor, end.Centre.z);
			float near = end.Radius + 7f;
			foreach (CaveChamber c in Chambers)
			{
				if (OverFloor(c, at, 4f))
				{
					return false;
				}
			}
			for (int q = 0; q < Passages.Count; q++)
			{
				if (q == pi)
				{
					continue;
				}
				foreach (CaveNode n in Passages[q].Nodes)
				{
					if (Plan2(n.Centre, at) < near + n.Radius && Mathf.Abs(n.Floor - end.Floor) < 6f)
					{
						return false;
					}
				}
			}
			foreach ((Vector3 keep, float radius, float height) in lavaKeeps)
			{
				if (Plan2(at, keep) < radius + near && Mathf.Abs(at.y - keep.y) < height)
				{
					return false;
				}
			}
			foreach (CaveClimb c in Climbs)
			{
				if (Plan2(c.Bottom, at) < near || Plan2(c.Top, at) < near)
				{
					return false;
				}
			}
			foreach (CaveShaft s in Shafts)
			{
				if (Plan2(s.Bottom, at) < near + s.Radius)
				{
					return false;
				}
			}
			foreach (CaveRift r in Rifts)
			{
				if (PlanToSegment(at, r.A, r.B, out _) < near + r.Width)
				{
					return false;
				}
			}
			foreach (CaveSolid s in Solids)
			{
				SolidBounds(s, out Vector3 lo, out Vector3 hi);
				if (at.x > lo.x - near && at.x < hi.x + near && at.z > lo.z - near && at.z < hi.z + near && at.y > lo.y - 3f && at.y < hi.y + 3f)
				{
					return false;
				}
			}
			foreach ((Vector3 laid, float half, int _) in lavaLaid)
			{
				if (Plan2(laid, at) < half + near && Mathf.Abs(laid.y - at.y) < 4f)
				{
					return false;
				}
			}
			return !NearHollow(at, near);
		}

		// ── Falls ─────────────────────────────────────────────────

		/// <summary>
		/// A fall down a chasm's free end, as the lava's (CaveSystem.Lava.cs): fed by the stream ending in the chasm's chamber where
		/// its way to the lip is clear, run on over the lip and down into a plunge pool on the chasm's floor; else, now and then,
		/// a spring pouring out of the chasm's own wall under its lip into the pool.
		/// </summary>
		private void StreamChasmFall(CaveStreamPlan plan, int ri)
		{
			if (!StreamRoom(plan, 2))
			{
				return;
			}
			CaveRift rift = Rifts[ri];
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
			int fed = -1;
			for (int b = 0; b < plan.Bodies.Count && fed < 0; b++)
			{
				CaveStreamBody body = plan.Bodies[b];
				if (body.Kind == CaveStreamKind.Stream && body.Into < 0 && OverFloor(chamber, body.Path[body.Path.Length - 1] + Vector3.up * CaveStreams.Drop, 1f))
				{
					fed = b;
				}
			}
			for (int pass = fed >= 0 ? 0 : 1; pass < 2; pass++)
			{
				if (pass == 1 && StreamUnit(0x57EAFu, (uint)ri, 1) >= 0.6f)
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
					if (!LavaPoolFits(poolAt, pool, bottom, axis, out float poolFloor, out float poolTop))
					{
						continue;
					}
					poolAt.y = poolFloor - CaveStreams.PoolDrop;
					if (Awash(poolAt.y - CaveStreams.PoolDepthMax))
					{
						continue;
					}
					if (pass == 0)
					{
						if (FedFall(plan, fed, ri, room, m, axis * free, e, lip, poolAt, pool, poolFloor, poolTop, rift))
						{
							return;
						}
						continue;
					}
					float side = StreamUnit(0x57EAFu, (uint)ri, 2) < 0.5f ? -1f : 1f;
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
					// Out of the face into the chasm, where the water leaves the rock.
					vent -= e * (side * 0.6f);
					if (vent.y - poolAt.y < CaveStreams.MinFall)
					{
						continue;
					}
					int pooled = PlungePool(plan, poolAt, pool, poolFloor, poolTop, room);
					AddFall(plan, vent, poolAt, Mathf.Clamp(rift.Narrow, 0.8f, 1.6f), -1, room, pooled);
					return;
				}
			}
		}

		/// <summary>The stream <paramref name="fed"/> run on over its chamber's floor to the chasm's lip and over it into a plunge pool; false where its way there is not clear.</summary>
		private bool FedFall(CaveStreamPlan plan, int fed, int ri, int room, Vector3 m, Vector3 toFree, Vector3 e, float lip, Vector3 poolAt, float pool,
			float poolFloor, float poolTop, in CaveRift rift)
		{
			CaveStreamBody stream = plan.Bodies[fed];
			CaveChamber chamber = Chambers[room];
			Vector3 from = stream.Path[stream.Path.Length - 1];
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
			var path = new List<Vector3>(stream.Path);
			var banks = new List<float>(stream.Banks);
			var tops = new List<float>(stream.Tops);
			float surface = from.y, bank = banks[banks.Count - 1], top = tops[tops.Count - 1];
			for (int s = 1; s <= steps; s++)
			{
				Vector3 q = Vector3.Lerp(from, edge, s / (float)steps);
				q.y = FloorOf(chamber, q);
				if (!LavaClear(q, 0.5f * stream.Width, ri, fed))
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
				surface = Mathf.Min(surface, bank - CaveStreams.Drop);
				if (bank - surface > CaveStreams.MaxSink)
				{
					return false;
				}
				path.Add(new Vector3(q.x, surface, q.z));
				banks.Add(bank);
				tops.Add(top);
			}
			if (surface - poolAt.y < 3f || !StreamRoom(plan))
			{
				return false;
			}
			for (int k = path.Count - steps; k < path.Count; k++)
			{
				lavaLaid.Add((new Vector3(path[k].x, banks[k], path[k].z), 0.5f * stream.Width, fed));
			}
			int pooled = PlungePool(plan, poolAt, pool, poolFloor, poolTop, room);
			// Over the lip and down: a point falling midway, then where it lands in the pool.
			Vector3 lipPoint = path[path.Count - 1];
			path.Add(Vector3.Lerp(lipPoint, poolAt, 0.5f));
			path.Add(poolAt);
			banks.Add(float.NaN);
			banks.Add(float.NaN);
			tops.Add(float.NaN);
			tops.Add(float.NaN);
			var falls = new List<bool>(stream.Falls ?? new bool[stream.Path.Length]);
			while (falls.Count < path.Count - 2)
			{
				falls.Add(false);
			}
			falls.Add(true);
			falls.Add(true);
			stream.Path = path.ToArray();
			stream.Banks = banks.ToArray();
			stream.Tops = tops.ToArray();
			stream.Falls = falls.ToArray();
			stream.Width = Mathf.Min(stream.Width, Mathf.Clamp(2f * rift.Narrow, 0.8f, 2f));
			stream.Chamber = room;
			stream.Into = pooled;
			plan.Bodies[fed] = stream;
			return true;
		}

		/// <summary>A plunge pool in a chasm's floor: a bowl cut under that floor, its surface the pool's.</summary>
		private int PlungePool(CaveStreamPlan plan, Vector3 at, float radius, float floor, float top, int chamber)
		{
			int index = plan.Bodies.Count;
			plan.Bodies.Add(new CaveStreamBody
			{
				Kind = CaveStreamKind.Pool,
				Path = new[] { at },
				Width = radius,
				Depth = CaveStreams.PoolDepthMax,
				Banks = new[] { floor },
				Tops = new[] { top },
				Passage = -1,
				Chamber = chamber,
				Pit = -1,
				Into = -1,
			});
			lavaLaid.Add((new Vector3(at.x, floor, at.z), radius, index));
			return index;
		}

		/// <summary>A fall on its own (a spring out of a chasm's wall, water down from an aven): its lip, a point falling midway, and where it lands in its pool.</summary>
		private static void AddFall(CaveStreamPlan plan, Vector3 lip, Vector3 landing, float width, int passage, int chamber, int into)
		{
			plan.Bodies.Add(new CaveStreamBody
			{
				Kind = CaveStreamKind.Stream,
				Path = new[] { lip, Vector3.Lerp(lip, landing, 0.5f), landing },
				Falls = new[] { false, true, true },
				Width = width,
				Depth = CaveStreams.StreamDepth,
				Banks = new[] { float.NaN, float.NaN, float.NaN },
				Tops = new[] { float.NaN, float.NaN, float.NaN },
				Passage = passage,
				Chamber = chamber,
				Pit = -1,
				Into = into,
			});
		}

		/// <summary>
		/// Water falling from the roof's avens (2026-10-20: an aven is where water once fell in down a joint; in a wet cave it
		/// still does), by the seed: out of a crack in the aven's wall partway up it, down that side into a pool on the floor
		/// under it — under its middle, or off toward whichever side of it a pool fits off every way (the water runs down the
		/// wall it comes out of).
		/// </summary>
		private void AvenFalls(CaveStreamPlan plan)
		{
			for (int ai = 0; ai < Hollows.Count && StreamRoom(plan, 2); ai++)
			{
				CaveHollow aven = Hollows[ai];
				if (aven.Kind != CaveHollowKind.Aven || StreamUnit(0x57EA7u, (uint)ai, 1) >= 0.55f)
				{
					continue;
				}
				// The floor under it, found down from just under the roof it rises from.
				Vector3 under = aven.A + Vector3.down * 1f;
				if (HeldVoid(under) >= 0f || float.IsNaN(HeldFloor(under, under.y, 40f)))
				{
					continue;
				}
				float wanted = Mathf.Clamp(aven.Radius * 0.9f, PoolLeast, 2.4f);
				float rise = Mathf.Clamp(0.4f * (aven.Top - aven.A.y), 0.5f, 4f);
				Vector3 crack = aven.A + Vector3.up * rise;
				if (HeldVoid(crack) >= 0f)
				{
					continue;
				}
				float turn = 360f * StreamUnit(0x57EA7u, (uint)ai, 2);
				bool laid = false;
				for (int k = 0; k < 17 && !laid && StreamRoom(plan, 2); k++)
				{
					// Under its middle first, then round it a little out and further out.
					Vector3 dir = LavaDir(turn + 45f * ((k + 7) % 8));
					float off = k == 0 ? 0f : k <= 8 ? 0.6f * aven.Radius : 1.1f * aven.Radius;
					Vector3 at = under + dir * off;
					if (HeldVoid(at) >= 0f)
					{
						continue;
					}
					float floor = HeldFloor(at, at.y, 40f);
					if (float.IsNaN(floor))
					{
						continue;
					}
					at.y = floor;
					int pooled = StreamPool(plan, Chambers.FindIndex(c => OverFloor(c, at, 0.5f)), at, wanted, -1, out float level, true);
					if (pooled < 0)
					{
						continue;
					}
					CaveStreamBody pool = plan.Bodies[pooled];
					// The crack it pours from: on the aven's wall on the pool's side, just out of the face.
					Vector3 lip = CrackLip(crack, dir, aven.Radius * 1.5f + 1f);
					// Down that side: it lands under the crack, a little out from the wall, inside the pool.
					Vector3 landing = new Vector3(lip.x, level, lip.z) - dir * 0.5f;
					Vector3 fromMiddle = landing - pool.Path[0];
					fromMiddle.y = 0f;
					float most = Mathf.Max(0.2f, pool.Width - 0.4f);
					if (fromMiddle.magnitude > most)
					{
						landing = pool.Path[0] + fromMiddle.normalized * most;
						landing.y = level;
					}
					if (lip.y - level < CaveStreams.MinFall || Plan2(lip, landing) < 0.3f)
					{
						plan.Bodies.RemoveAt(pooled);
						lavaLaid.RemoveAll(l => l.body == pooled);
						continue;
					}
					if (!FallOpen(lip, landing))
					{
						plan.Bodies.RemoveAt(pooled);
						lavaLaid.RemoveAll(l => l.body == pooled);
						continue;
					}
					AddFall(plan, lip, landing, Mathf.Clamp(0.5f * aven.Radius, 0.5f, 1.2f), -1, pool.Chamber, pooled);
					laid = true;
				}
				if (!laid && StreamRoom(plan))
				{
					Shower(plan, aven, crack, LavaDir(turn));
				}
			}
		}

		/// <summary>
		/// Whether a fall from a lip to its landing is open the whole way down on the rough field (2026-10-21: a shower down an
		/// aven's wall ran through a knob of it halfway down).
		/// </summary>
		private bool FallOpen(Vector3 lip, Vector3 landing)
		{
			for (float f = 0.2f; f < 0.95f; f += 0.15f)
			{
				if (Void(Vector3.Lerp(lip, landing, f) + Vector3.up * 0.2f, out _, false) >= 0f)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Where water leaves a crack in an aven's wall: out from the aven's middle to the smooth wall's face, then back off it
		/// until the rough field (as the mesher cuts the rock) is open there too (2026-10-21: a knob of the wall stood 0.3 m
		/// proud of the smooth face, and the crack was set inside it).
		/// </summary>
		private Vector3 CrackLip(Vector3 crack, Vector3 dir, float reach)
		{
			float out_ = 0f;
			while (HeldVoid(crack + dir * (out_ + 0.1f)) < 0f && out_ < reach)
			{
				out_ += 0.1f;
			}
			float back = Mathf.Max(0f, out_ - 0.3f);
			while (back > 0f && Void(crack + dir * back, out _, false) >= -0.05f)
			{
				back = Mathf.Max(0f, back - 0.1f);
			}
			return crack + dir * back;
		}

		/// <summary>
		/// Where no pool fits under an aven (most stand over a way): a thin shower out of its crack, down the wall onto the floor
		/// under it, where it runs away into the rock — the way under it walked through the falling water, wetted, never cut.
		/// Nowhere near a kept place (a camp, a mouth), a climb, a bridge or a chasm, nor over another level.
		/// </summary>
		private void Shower(CaveStreamPlan plan, in CaveHollow aven, Vector3 crack, Vector3 dir)
		{
			Vector3 lip = CrackLip(crack, dir, aven.Radius * 1.5f + 1f);
			Vector3 foot = lip - dir * 0.5f;
			if (HeldVoid(foot) >= 0f)
			{
				return;
			}
			float floor = HeldFloor(foot, foot.y, 40f);
			if (float.IsNaN(floor) || lip.y - floor < CaveStreams.MinFall + 0.5f || Awash(floor))
			{
				return;
			}
			foot.y = floor;
			foreach ((Vector3 at, float radius, float height) in lavaKeeps)
			{
				if (Plan2(foot, at) < radius + 1f && Mathf.Abs(foot.y - at.y) < height)
				{
					return;
				}
			}
			foreach (CaveClimb c in Climbs)
			{
				if (Plan2(c.Bottom, foot) < 3f || Plan2(c.Top, foot) < 3f)
				{
					return;
				}
			}
			foreach (CaveCrossing c in Crossings)
			{
				if (PlanToSegment(foot, c.A, c.B, out _) < c.HalfWidth + 2f)
				{
					return;
				}
			}
			foreach (CaveSolid sol in Solids)
			{
				if ((sol.Kind == CaveSolidKind.Bridge || sol.Kind == CaveSolidKind.Ramp || sol.Kind == CaveSolidKind.Spiral) && PlanToSegment(foot, sol.A, sol.B, out _) < sol.Width + 2f)
				{
					return;
				}
			}
			foreach (CaveRift r in Rifts)
			{
				if (PlanToSegment(foot, r.A, r.B, out _) < r.Width + 2f)
				{
					return;
				}
			}
			foreach (CaveShaft sh in Shafts)
			{
				if (Plan2(foot, sh.Bottom) < sh.Radius * 1.42f + 2f)
				{
					return;
				}
			}
			foreach ((Vector3 laid, float half, int _) in lavaLaid)
			{
				if (Plan2(laid, foot) < half + 1.5f && Mathf.Abs(laid.y - foot.y) < 3f)
				{
					return;
				}
			}
			if (LavaOverLower(foot, 1f))
			{
				return;
			}
			// A film over the floor where it lands, running off into the rock; its fall open the whole way down.
			Vector3 landing = foot + Vector3.up * 0.03f;
			if (!FallOpen(lip, landing))
			{
				return;
			}
			int index = plan.Bodies.Count;
			plan.Bodies.Add(new CaveStreamBody
			{
				Kind = CaveStreamKind.Stream,
				Path = new[] { lip, Vector3.Lerp(lip, landing, 0.5f), landing },
				Falls = new[] { false, true, true },
				Width = Mathf.Clamp(0.4f * aven.Radius, 0.4f, 0.8f),
				Depth = ShowerDepth,
				Banks = new[] { float.NaN, float.NaN, float.NaN },
				Tops = new[] { float.NaN, float.NaN, float.NaN },
				Passage = -1,
				Chamber = Chambers.FindIndex(c => OverFloor(c, foot, 0.5f)),
				Pit = -1,
				Into = -1,
			});
			lavaLaid.Add((foot, 0.5f, index));
		}

		/// <summary>How deep a shower's water runs where it lands (a film: its discharge, for the curtain's thickness), metres.</summary>
		public const float ShowerDepth = 0.12f;
	}
}
#endif
