#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Water;
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
						// (A shower's splash basin shelves to its brim, ankle deep at its middle: no step at its rim on the way it lies on.)
						float shelf = body.Depth <= SplashDepth + 1e-4f && body.Width <= SplashRadius + 1e-4f ? c.y - 0.05f : c.y - 0.3f;
						bed.Add(new Piece { A = c, B = c, Half = body.Width, BedA = bottom, BedB = bottom, TopA = body.Tops[0], TopB = body.Tops[0], Shelf = shelf, Basin = true });
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
					// the basins and gours, the other passages' streams and their pools, a flooded dead end; last the avens' showers, so
					// each comes down in whatever water lies under it.
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
					List<(int, Vector3, float)> showers = AvenFalls(plan);
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
					SinksRunOn(plan);
					Showers(plan, showers);
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
			int lastEnd = -1000, lastBody = -1;
			int i = 0;
			while (i < samples.Count && StreamRoom(plan))
			{
				if (!samples[i].Open)
				{
					i++;
					continue;
				}
				bool welling = float.IsNaN(carried) || i - lastEnd > 15 || samples[i].Bank - carried > CaveStreams.MaxSink;
				// Run on from the run before only where its water went on under the rock (2026-10-21: it sank in its bed a pace or
				// two from this one's head); anywhere else this one wells up afresh.
				Vector3 under = new Vector3(samples[i].At.x, Mathf.Min(carried, samples[i].Bank - CaveStreams.Drop), samples[i].At.z);
				if (!welling && (lastBody < 0 || !UnderRock(plan.Bodies[lastBody].Path[plan.Bodies[lastBody].Path.Length - 1], under, 0.3f, 0.5f * width)))
				{
					welling = true;
				}
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
					if (!welling)
					{
						RunOnInto(plan, lastBody, path[0]);
					}
					if (!sank)
					{
						streamResting.Add(index);
					}
					carried = surface;
					lastEnd = j - 1;
					lastBody = index;
				}
				i = sank ? j + Gap : j;
			}
		}

		/// <summary>
		/// A stream that runs on under the rock into the next run of its passage (a natural bridge over it, or a rise of the floor
		/// it sank under): its line carried on to that run's head, nothing cut for it there (CaveStreamWater joins the two), and no
		/// pool at its end.
		/// </summary>
		private void RunOnInto(CaveStreamPlan plan, int body, Vector3 head)
		{
			CaveStreamBody stream = plan.Bodies[body];
			int n = stream.Path.Length;
			var path = new List<Vector3>(stream.Path) { new Vector3(head.x, Mathf.Min(head.y, stream.Path[n - 1].y), head.z) };
			var banks = new List<float>(stream.Banks) { float.NaN };
			var tops = new List<float>(stream.Tops) { float.NaN };
			stream.Path = path.ToArray();
			stream.Banks = banks.ToArray();
			stream.Tops = tops.ToArray();
			if (stream.Falls != null)
			{
				stream.Falls = new List<bool>(stream.Falls) { false }.ToArray();
			}
			plan.Bodies[body] = stream;
			streamResting.Remove(body);
		}

		/// <summary>How far from its end a stream that would sink into its bed runs on under the rock into other water, metres.</summary>
		private const float RunOnReach = 4f;

		/// <summary>The most a stream run on over the floor into other water may step down into it at the last, metres (a rapid over the other's bank, no fall).</summary>
		private const float RunOnStep = 1.2f;

		/// <summary>
		/// Every stream that still sinks into its bed where other water lies near its end, no higher than it (another stream's
		/// channel, a pool): run on into it (<see cref="RunOnTo"/>), nearest first (2026-10-21: two passages' streams came to a
		/// junction at one level and one sank there a few paces from the other; a chamber's stream sank beside the trunk's).
		/// </summary>
		private void SinksRunOn(CaveStreamPlan plan)
		{
			for (int si = 0; si < plan.Bodies.Count; si++)
			{
				CaveStreamBody stream = plan.Bodies[si];
				int n = stream.Path.Length;
				if (stream.Kind != CaveStreamKind.Stream || stream.Into >= 0 || stream.Banks == null || float.IsNaN(stream.Banks[n - 1])
					|| stream.Falls != null && stream.Falls[n - 1])
				{
					continue;
				}
				Vector3 end = stream.Path[n - 1];
				var targets = new List<(float distance, int body, Vector3 at)>();
				for (int b = 0; b < plan.Bodies.Count; b++)
				{
					CaveStreamBody other = plan.Bodies[b];
					if (b == si)
					{
						continue;
					}
					if (other.Kind == CaveStreamKind.Pool)
					{
						Vector3 centre = other.Path[0];
						float d = Plan2(centre, end) - (other.Width - 0.4f);
						Vector3 at = centre + FlatDir(end - centre) * Mathf.Max(0f, other.Width - 0.4f);
						at.y = centre.y;
						if (d < RunOnReach && centre.y <= end.y && centre.y >= end.y - RunOnReach)
						{
							targets.Add((d, b, at));
						}
						continue;
					}
					// Not into a stream that runs into this one.
					if (other.Banks == null || OnChannel(stream, other.Path[other.Path.Length - 1], out _, out _))
					{
						continue;
					}
					// A little way up one that sinks where it ends, else (where only its end is near) into that end, where
					// CaveStreamWater runs the higher of two streams ending side by side into the other.
					float[] otherBanks = other.Banks;
					float keep = float.IsNaN(otherBanks[otherBanks.Length - 1]) || other.Into >= 0 ? 0f : 0.5f * other.Width + 0.6f;
					foreach (float back in keep > 0f ? new[] { keep, 0f } : new[] { 0f })
					{
						if (NearestOnLine(other.Path, k => !float.IsNaN(otherBanks[k]), end, back, out Vector3 best, out float nearest)
							&& nearest - 0.5f * other.Width < RunOnReach && best.y <= end.y && best.y >= end.y - RunOnReach)
						{
							targets.Add((nearest, b, best));
							break;
						}
					}
				}
				targets.Sort((x, y) => x.distance != y.distance ? x.distance.CompareTo(y.distance) : x.body.CompareTo(y.body));
				foreach ((float _, int body, Vector3 at) in targets)
				{
					if (RunOnTo(plan, si, body, at))
					{
						break;
					}
				}
			}
		}

		/// <summary>
		/// A sinking stream run on from its end into other water at <paramref name="at"/> (on <paramref name="body"/>'s line or in
		/// its pool, at its surface): under the rock where it would lie hidden the whole way (<see cref="UnderRock"/>); else over
		/// the floor in a channel of its own, cut as the rest of it is, where its way there is clear of everything (but the water
		/// it runs into) and the floor lets its surface fall gently to that water's. False where neither does.
		/// </summary>
		private bool RunOnTo(CaveStreamPlan plan, int si, int body, Vector3 at)
		{
			CaveStreamBody stream = plan.Bodies[si];
			CaveStreamBody other = plan.Bodies[body];
			Vector3 end = stream.Path[stream.Path.Length - 1];
			if (!UnderRock(end, at, 0.3f, 0.5f * stream.Width))
			{
				float reach = other.Kind == CaveStreamKind.Pool ? 0.4f : 0.5f * other.Width;
				int steps = Mathf.CeilToInt(Plan2(end, at) / 0.8f);
				var path = new List<Vector3>(stream.Path);
				var banks = new List<float>(stream.Banks);
				var tops = new List<float>(stream.Tops);
				float surface = end.y;
				for (int k = 1; k < steps; k++)
				{
					Vector3 q = Vector3.Lerp(end, at, k / (float)steps);
					if (Plan2(q, at) < reach)
					{
						break;
					}
					float bank = HeldFloor(q, surface + CaveStreams.Drop + 0.6f);
					if (float.IsNaN(bank))
					{
						return false;
					}
					q.y = bank;
					surface = Mathf.Min(surface, bank - CaveStreams.Drop);
					if (bank - surface > CaveStreams.MaxSink || Awash(surface - CaveStreams.StreamDepth) || !LavaClear(q, 0.5f * stream.Width, -1, si, body))
					{
						return false;
					}
					path.Add(new Vector3(q.x, surface, q.z));
					banks.Add(bank);
					tops.Add(bank + 0.3f);
				}
				// Its surface down to the water's it runs into, gently: no fall drawn where none is planned.
				if (at.y > surface || surface - at.y > RunOnStep)
				{
					return false;
				}
				for (int k = stream.Path.Length; k < path.Count; k++)
				{
					lavaLaid.Add((new Vector3(path[k].x, banks[k], path[k].z), 0.5f * stream.Width, si));
				}
				stream.Path = path.ToArray();
				stream.Banks = banks.ToArray();
				stream.Tops = tops.ToArray();
				if (stream.Falls != null)
				{
					var falls = new List<bool>(stream.Falls);
					while (falls.Count < path.Count)
					{
						falls.Add(false);
					}
					stream.Falls = falls.ToArray();
				}
				plan.Bodies[si] = stream;
			}
			RunOnInto(plan, si, at);
			if (other.Kind == CaveStreamKind.Pool)
			{
				stream = plan.Bodies[si];
				stream.Into = body;
				plan.Bodies[si] = stream;
			}
			return true;
		}

		/// <summary>
		/// Whether water going on under the rock from one point to another (its surface each end) lies hidden the whole way, the
		/// field (without any bed) rock <paramref name="over"/> over its surface but within a stride of either end, and nothing of
		/// another level under it within <paramref name="half"/> (<see cref="LavaOverLower"/>): a stream or a lava river run on
		/// under a natural bridge, never drawn over a way.
		/// </summary>
		private bool UnderRock(Vector3 from, Vector3 to, float over, float half)
		{
			float run = Plan2(from, to);
			for (float d = 0f; d <= run; d += 0.5f)
			{
				Vector3 p = Vector3.Lerp(from, to, run > 1e-4f ? d / run : 0f);
				if (d >= 0.6f && d <= run - 0.6f && HeldVoid(p + Vector3.up * over) < 0f || LavaOverLower(p, half))
				{
					return false;
				}
			}
			return true;
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
					// Out of the crack back over the pool, where the water thrown from it comes down.
					Vector3 landing = Thrown(lip, -dir, GrottoSpringDepth, level);
					// Into the pool (water a good way down under where it lands), the whole fall open.
					if (HeldVoid(landing + Vector3.down * 0.4f) >= 0f || !ThrowOpen(lip, -dir, GrottoSpringDepth, level) || LavaOverLower(landing, 1f))
					{
						continue;
					}
					plan.Bodies.Add(new CaveStreamBody
					{
						Kind = CaveStreamKind.Stream,
						Path = new[] { lip, Vector3.Lerp(lip, landing, 0.5f), landing },
						Falls = new[] { false, true, true },
						Width = 0.7f,
						Depth = GrottoSpringDepth,
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

		/// <summary>How deep a grotto's spring runs over its crack's lip (its discharge, for its curtain's throw and thickness), metres.</summary>
		private const float GrottoSpringDepth = 0.3f;

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
		/// channel (the fall at least <see cref="CaveStreams.MinFall"/>, drawn as one), landing on the stream's line a little way
		/// down it, where the water thrown out of the crack comes down (2026-10-21: <see cref="Thrown"/>; set by geometry alone,
		/// the curtain the inland water traces came down on the bench beside the stream). The run before the landing is dropped,
		/// so no dry channel lies upstream of the water. Nothing where the head lies in a room, out from its walls.
		/// </summary>
		private void Spring(in LavaSample head, List<Vector3> path, List<float> banks, List<float> tops, List<bool> falls)
		{
			const float depth = CaveStreams.StreamDepth;
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
				// Where its curtain comes down on the stream's line: the first point down it as far out from the crack as the water
				// is thrown falling to it.
				float Short(Vector3 at) => Plan2(vent, at) - FallHydraulics.Throw(CaveStreamWater.LipUnitDischarge(depth), vent.y - at.y);
				int most = Mathf.Min(path.Count - 6, 6);
				for (int k = 0; k < most; k++)
				{
					float a = Short(path[k]), b = Short(path[k + 1]);
					if (a > 0f)
					{
						// Already past the head: the crack stands too far off the line for its water to land on it.
						break;
					}
					if (b < 0f)
					{
						continue;
					}
					float t0 = 0f, t1 = 1f;
					for (int step = 0; step < 16; step++)
					{
						float t = 0.5f * (t0 + t1);
						if (Short(Vector3.Lerp(path[k], path[k + 1], t)) < 0f)
						{
							t0 = t;
						}
						else
						{
							t1 = t;
						}
					}
					float u = 0.5f * (t0 + t1);
					Vector3 landing = Vector3.Lerp(path[k], path[k + 1], u);
					Vector3 dir = FlatDir(landing - vent);
					// Its fall drawn as one, open the whole way down its path (a wall flaring out at its foot would stand in it).
					if (vent.y - landing.y < CaveStreams.MinFall || !ThrowOpen(vent, dir, depth, landing.y))
					{
						break;
					}
					float bank = Mathf.Lerp(banks[k], banks[k + 1], u), top = Mathf.Lerp(tops[k], tops[k + 1], u);
					path.RemoveRange(0, k + 1);
					banks.RemoveRange(0, k + 1);
					tops.RemoveRange(0, k + 1);
					falls.RemoveRange(0, k + 1);
					path.InsertRange(0, new[] { vent, Vector3.Lerp(vent, landing, 0.5f), landing });
					banks.InsertRange(0, new[] { float.NaN, float.NaN, bank });
					tops.InsertRange(0, new[] { float.NaN, float.NaN, top });
					falls.InsertRange(0, new[] { false, true, true });
					return;
				}
			}
		}

		/// <summary>
		/// Where water of a body <paramref name="depth"/> deep comes down to <paramref name="level"/>, pouring off a lip the way
		/// <paramref name="dir"/> points (level, unit length): out from it by its free throw (FallHydraulics.Throw), as the inland
		/// water's renderer traces a fall's curtain from its lip with the water the scene's hydrology gives it
		/// (<see cref="CaveStreamWater.LipUnitDischarge"/>). A fall's foot is put there, in the water it feeds.
		/// </summary>
		private static Vector3 Thrown(Vector3 lip, Vector3 dir, float depth, float level)
		{
			Vector3 p = lip + dir * FallHydraulics.Throw(CaveStreamWater.LipUnitDischarge(depth), lip.y - level);
			p.y = level;
			return p;
		}

		/// <summary>How far over the water it lands in a curtain's path is looked at for rock (<see cref="ThrowOpen"/>), metres: under that it is in the bed its water is cut (a channel under its bench, a pool's bowl), not cut while the plan is made.</summary>
		private const float ThrowOpenOver = 0.8f;

		/// <summary>
		/// Whether a curtain thrown off a lip the way <paramref name="dir"/> points (<see cref="Thrown"/>) is open on the rough field
		/// the whole way down its path (as <see cref="FallOpen"/>), from a fifth of its drop to <see cref="ThrowOpenOver"/> over its water.
		/// </summary>
		private bool ThrowOpen(Vector3 lip, Vector3 dir, float depth, float level)
		{
			float drop = lip.y - level;
			var path = new List<Vector2>();
			FallHydraulics.Throw(CaveStreamWater.LipUnitDischarge(depth), drop, FallHydraulics.DefaultTurbulence, path);
			int stride = Mathf.Max(1, path.Count / 16);
			for (int k = 0; k < path.Count; k += stride)
			{
				if (path[k].y < 0.2f * drop || path[k].y > drop - ThrowOpenOver)
				{
					continue;
				}
				Vector3 p = lip + dir * path[k].x + Vector3.down * path[k].y;
				if (Void(p + Vector3.up * 0.2f, out _, false) >= 0f)
				{
					return false;
				}
			}
			return true;
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
		private int StreamPool(CaveStreamPlan plan, int chamber, Vector3 at, float wanted, int exceptBody, out float level, bool close = false, float under = float.MaxValue,
			float least = PoolLeast)
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
			for (float r = wanted; r >= least - 1e-3f; r = r > least ? Mathf.Max(least, r - 0.4f) : 0f)
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
				// (Not one running on under the rock into the next run: its end is that run's.)
				if (body.Kind == CaveStreamKind.Stream && body.Into < 0 && body.Banks != null && !float.IsNaN(body.Banks[body.Banks.Length - 1])
					&& OverFloor(chamber, body.Path[body.Path.Length - 1] + Vector3.up * CaveStreams.Drop, 1f))
				{
					fed = b;
				}
			}
			var chasm = new ChasmPool { Rift = ri, Middle = m, ToFree = axis * free, Across = e, Bottom = bottom, Clear = clear, Half = half, Radius = pool };
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
					// The way the water pours: on down the chasm toward its free end, out from the lip.
					Vector3 poolAt = m + axis * (free * land);
					if (pass == 0)
					{
						if (FedFall(plan, fed, room, lip, chasm, rift))
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
					if (!PlungeUnder(chasm, vent, FlatDir(poolAt - vent), CaveStreams.StreamDepth, out Vector3 landing, out Vector3 centre, out float poolFloor, out float poolTop))
					{
						continue;
					}
					int pooled = PlungePool(plan, centre, pool, poolFloor, poolTop, room);
					AddFall(plan, vent, landing, Mathf.Clamp(rift.Narrow, 0.8f, 1.6f), -1, room, pooled);
					return;
				}
			}
		}

		/// <summary>A chasm a fall pours into: its middle on its lip's floor, the way to its free end, across it, its floor's lowest, how far past its middle a lip may lie clear of its climbs (each 3.5 m short of it), its half length, and the plunge pool's radius.</summary>
		private struct ChasmPool
		{
			public int Rift;
			public Vector3 Middle, ToFree, Across;
			public float Bottom, Clear, Half, Radius;
		}

		/// <summary>
		/// The plunge pool a fall off <paramref name="lip"/> pouring the way <paramref name="dir"/> points comes down in: round where
		/// its curtain lands (<see cref="Thrown"/>) on the chasm's floor, that floor measured; within the chasm, on its free end's
		/// side, clear of its climbs, the curtain open the whole way down. Its landing (at the pool's surface) and its centre (y the
		/// pool's surface), its floor and the top its bowl is cut to; false where none fits.
		/// </summary>
		private bool PlungeUnder(in ChasmPool chasm, Vector3 lip, Vector3 dir, float depth, out Vector3 landing, out Vector3 centre, out float floor, out float top)
		{
			Vector3 axis = FlatDir(chasm.ToFree);
			centre = default;
			floor = top = float.NaN;
			// First as if it fell to the chasm's lowest floor, then to the floor measured where that lands.
			landing = Thrown(lip, dir, depth, chasm.Bottom - CaveStreams.PoolDrop);
			for (int pass = 0; pass < 2; pass++)
			{
				if (!LavaPoolFits(landing, chasm.Radius, chasm.Bottom, axis, out floor, out top))
				{
					return false;
				}
				centre = new Vector3(landing.x, floor - CaveStreams.PoolDrop, landing.z);
				landing = Thrown(lip, dir, depth, centre.y);
			}
			Vector3 o = centre - chasm.Middle;
			float along = Vector3.Dot(o, chasm.ToFree), across = Mathf.Abs(Vector3.Dot(o, chasm.Across));
			CaveRift rift = Rifts[chasm.Rift];
			return Plan2(landing, centre) <= chasm.Radius - 0.3f && along >= chasm.Clear - 2.5f + chasm.Radius && along <= chasm.Half - chasm.Radius - 0.5f && across <= rift.Width
				&& lip.y - centre.y >= CaveStreams.MinFall && !Awash(centre.y - CaveStreams.PoolDepthMax) && ThrowOpen(lip, dir, depth, centre.y);
		}

		/// <summary>
		/// The stream <paramref name="fed"/> run on over its chamber's floor to the chasm's lip <paramref name="lip"/> metres toward
		/// its free end and over it, into a plunge pool where its curtain comes down (<see cref="PlungeUnder"/>); false where its
		/// way there is not clear or no pool fits.
		/// </summary>
		private bool FedFall(CaveStreamPlan plan, int fed, int room, float lip, in ChasmPool chasm, in CaveRift rift)
		{
			CaveStreamBody stream = plan.Bodies[fed];
			CaveChamber chamber = Chambers[room];
			int ri = chasm.Rift;
			Vector3 m = chasm.Middle, e = chasm.Across;
			Vector3 from = stream.Path[stream.Path.Length - 1];
			float side = Mathf.Sign(Vector3.Dot(from - m, e));
			if (side == 0f)
			{
				side = 1f;
			}
			Vector3 edge = m + chasm.ToFree * lip + e * (side * (rift.Width - 0.3f));
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
			// Over the lip the way it runs to it, into the pool where its water comes down.
			Vector3 lipPoint = path[path.Count - 1];
			if (!StreamRoom(plan) || !PlungeUnder(chasm, lipPoint, FlatDir(lipPoint - path[path.Count - 2]), stream.Depth, out Vector3 landing, out Vector3 poolAt,
				out float poolFloor, out float poolTop) || surface - poolAt.y < 3f)
			{
				return false;
			}
			for (int k = path.Count - steps; k < path.Count; k++)
			{
				lavaLaid.Add((new Vector3(path[k].x, banks[k], path[k].z), 0.5f * stream.Width, fed));
			}
			int pooled = PlungePool(plan, poolAt, chasm.Radius, poolFloor, poolTop, room);
			// Over the lip and down: a point falling midway, then where it lands in the pool.
			path.Add(Vector3.Lerp(lipPoint, landing, 0.5f));
			path.Add(landing);
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
		/// still does), by the seed: out of a crack in the aven's wall partway up it, into a pool on the floor round where the
		/// water thrown out of the crack comes down (<see cref="Thrown"/>). Where no pool fits under an aven (most stand over a
		/// way), a thin shower instead, laid once every other body is (<see cref="Showers"/>): the avens that shower, their cracks
		/// and their turns.
		/// </summary>
		private List<(int aven, Vector3 crack, float turn)> AvenFalls(CaveStreamPlan plan)
		{
			var showers = new List<(int, Vector3, float)>();
			const float depth = CaveStreams.StreamDepth;
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
				float below = HeldFloor(under, under.y, 40f);
				bool laid = false;
				for (int k = 0; k < 8 && !laid && StreamRoom(plan, 2); k++)
				{
					// The crack on one side of the aven, its water thrown back out over the floor under it.
					Vector3 dir = LavaDir(turn + 45f * k);
					Vector3 lip = CrackLip(crack, dir, aven.Radius * 1.5f + 1f);
					Vector3 landing = Thrown(lip, -dir, depth, below - CaveStreams.PoolDrop);
					var over = new Vector3(landing.x, under.y, landing.z);
					if (HeldVoid(over) >= 0f)
					{
						continue;
					}
					float floor = HeldFloor(over, over.y, 40f);
					if (float.IsNaN(floor))
					{
						continue;
					}
					// A pool round where it lands: there, or reaching on past it, or back under the crack.
					foreach (float shift in new[] { 0f, 0.45f, -0.45f })
					{
						Vector3 at = new Vector3(landing.x, floor, landing.z) - dir * (shift * wanted);
						int pooled = StreamPool(plan, Chambers.FindIndex(c => OverFloor(c, at, 0.5f)), at, wanted, -1, out float level, true);
						if (pooled < 0)
						{
							continue;
						}
						CaveStreamBody pool = plan.Bodies[pooled];
						Vector3 lands = Thrown(lip, -dir, depth, level);
						if (lip.y - level < CaveStreams.MinFall || Plan2(lands, pool.Path[0]) > pool.Width - 0.3f || !ThrowOpen(lip, -dir, depth, level))
						{
							plan.Bodies.RemoveAt(pooled);
							lavaLaid.RemoveAll(l => l.body == pooled);
							continue;
						}
						AddFall(plan, lip, lands, Mathf.Clamp(0.5f * aven.Radius, 0.5f, 1.2f), -1, pool.Chamber, pooled);
						laid = true;
						break;
					}
				}
				if (!laid)
				{
					showers.Add((ai, crack, turn));
				}
			}
			return showers;
		}

		/// <summary>
		/// The showers (<see cref="AvenFalls"/>), laid once every other body is so each comes down in water: out of a crack on
		/// whichever side of its aven throws it into a stream's channel (on the channel's line, at its surface: CaveStreamWater
		/// joins it to the stream) or a pool under it; else into a small plunge pool of its own beside the way; else into the
		/// splash basin its water wore in the floor (<see cref="SplashFits"/>); else none (2026-10-21: a shower used to come down on
		/// the bare floor and sink, beside a stream as often as not).
		/// </summary>
		private void Showers(CaveStreamPlan plan, List<(int aven, Vector3 crack, float turn)> showers)
		{
			foreach ((int ai, Vector3 crack, float turn) in showers)
			{
				if (!StreamRoom(plan))
				{
					return;
				}
				Shower(plan, Hollows[ai], crack, turn);
			}
		}

		/// <summary>How far round an aven's middle a shower's crack may throw its water into a stream or pool, metres past its own radius.</summary>
		private const float ShowerReach = 4f;

		/// <summary>One aven's shower (<see cref="Showers"/>).</summary>
		private void Shower(CaveStreamPlan plan, in CaveHollow aven, Vector3 crack, float turn)
		{
			const float depth = ShowerDepth;
			float wall = aven.Radius * 1.5f + 1f, reach = aven.Radius + ShowerReach;
			float width = Mathf.Clamp(0.4f * aven.Radius, 0.4f, 0.8f);
			Vector3 middle = aven.A;
			bool Fits(Vector3 lip, Vector3 landing)
			{
				return lip.y - landing.y >= CaveStreams.MinFall + 0.5f && !Awash(landing.y) && ThrowOpen(lip, FlatDir(landing - lip), depth, landing.y);
			}
			// The cracks round its wall, each throwing its water back in over the floor under it: the first (from the turn its seed
			// picks) whose water comes down on a stream's line (within a quarter of its width) or inside a pool.
			const int Cracks = 64;
			for (int k = 0; k < Cracks; k++)
			{
				Vector3 dir = LavaDir(turn + 360f * k / Cracks);
				Vector3 lip = CrackLip(crack, dir, wall);
				for (int b = 0; b < plan.Bodies.Count; b++)
				{
					CaveStreamBody body = plan.Bodies[b];
					if (body.Path[0].y > lip.y - CaveStreams.MinFall && body.Path[body.Path.Length - 1].y > lip.y - CaveStreams.MinFall)
					{
						continue;
					}
					if (body.Kind == CaveStreamKind.Pool)
					{
						Vector3 centre = body.Path[0];
						if (Plan2(centre, middle) > reach + body.Width)
						{
							continue;
						}
						Vector3 landing = Thrown(lip, -dir, depth, centre.y);
						if (Plan2(landing, centre) <= body.Width - 0.3f && Fits(lip, landing))
						{
							AddShower(plan, lip, landing, width, body.Chamber, b);
							return;
						}
						continue;
					}
					if (body.Banks == null)
					{
						continue;
					}
					// Its surface where the water comes down, found from where it would land on the stream's nearest stretch.
					Vector3 guess = Thrown(lip, -dir, depth, body.Path[body.Path.Length - 1].y);
					for (int pass = 0; pass < 2; pass++)
					{
						if (!OnChannel(body, guess, out float surface, out float off) || off > 0.25f * body.Width)
						{
							guess.y = float.NaN;
							break;
						}
						guess = Thrown(lip, -dir, depth, surface);
					}
					if (!float.IsNaN(guess.y) && OnChannel(body, guess, out float level, out float across) && across <= 0.25f * body.Width
						&& Mathf.Abs(level - guess.y) < 0.02f && Fits(lip, guess))
					{
						AddShower(plan, lip, guess, width, Chambers.FindIndex(c => OverFloor(c, guess + Vector3.up * CaveStreams.Drop, 0.5f)), -1);
						return;
					}
				}
			}
			// Into a plunge pool of its own, where one fits off the ways round where its water lands.
			float below = HeldFloor(middle + Vector3.down * 1f, middle.y - 1f, 40f);
			for (int k = 0; k < 8 && !float.IsNaN(below) && StreamRoom(plan, 2); k++)
			{
				Vector3 dir = LavaDir(turn + 45f * k);
				Vector3 lip = CrackLip(crack, dir, wall);
				Vector3 landing = Thrown(lip, -dir, depth, below - CaveStreams.PoolDrop);
				var over = new Vector3(landing.x, middle.y - 1f, landing.z);
				float floor = HeldVoid(over) < 0f ? HeldFloor(over, over.y, 40f) : float.NaN;
				if (float.IsNaN(floor))
				{
					continue;
				}
				int pooled = StreamPool(plan, Chambers.FindIndex(c => OverFloor(c, over, 0.5f)), new Vector3(landing.x, floor, landing.z), ShowerPool, -1, out float level, true, float.MaxValue, ShowerPool);
				if (pooled < 0)
				{
					continue;
				}
				CaveStreamBody pool = plan.Bodies[pooled];
				Vector3 lands = Thrown(lip, -dir, depth, level);
				if (Plan2(lands, pool.Path[0]) > pool.Width - 0.3f || !Fits(lip, lands))
				{
					plan.Bodies.RemoveAt(pooled);
					lavaLaid.RemoveAll(l => l.body == pooled);
					continue;
				}
				AddShower(plan, lip, lands, width, pool.Chamber, pooled);
				return;
			}
			// Else into the basin its water wore in the floor where it lands, ankle deep (on a way, waded through): nowhere near a
			// kept place (a camp, a mouth), a climb, a bridge or a chasm, other water, nor over another level.
			for (int k = 0; k < 8 && !float.IsNaN(below) && StreamRoom(plan, 2); k++)
			{
				Vector3 dir = LavaDir(turn + 45f * k);
				Vector3 lip = CrackLip(crack, dir, wall);
				Vector3 landing = Thrown(lip, -dir, depth, below);
				var over = new Vector3(landing.x, middle.y - 1f, landing.z);
				float floor = HeldVoid(over) < 0f ? HeldFloor(over, over.y, 40f) : float.NaN;
				if (float.IsNaN(floor) || !SplashFits(new Vector3(landing.x, floor, landing.z), out float low, out float high))
				{
					continue;
				}
				float level = low - SplashDrop;
				Vector3 centre = new Vector3(landing.x, level, landing.z);
				Vector3 lands = Thrown(lip, -dir, depth, level);
				if (Plan2(lands, centre) > SplashRadius - 0.3f || !Fits(lip, lands))
				{
					continue;
				}
				int pooled = plan.Bodies.Count;
				plan.Bodies.Add(new CaveStreamBody
				{
					Kind = CaveStreamKind.Pool,
					Path = new[] { centre },
					Width = SplashRadius,
					Depth = SplashDepth,
					Banks = new[] { low },
					Tops = new[] { high + 0.3f },
					Passage = -1,
					Chamber = Chambers.FindIndex(c => OverFloor(c, over, 0.5f)),
					Pit = -1,
					Into = -1,
				});
				lavaLaid.Add((new Vector3(landing.x, floor, landing.z), SplashRadius, pooled));
				AddShower(plan, lip, lands, width, plan.Bodies[pooled].Chamber, pooled);
				return;
			}
		}

		/// <summary>How far round a shower's splash basin reaches, metres.</summary>
		public const float SplashRadius = 0.8f;
		/// <summary>How deep a shower's splash basin stands at its middle, metres: ankle deep, waded through where it lies on a way.</summary>
		public const float SplashDepth = 0.3f;
		/// <summary>How far under the lowest of its rim a splash basin's water stands, metres: brimming.</summary>
		public const float SplashDrop = 0.05f;

		/// <summary>
		/// Whether a shower's splash basin fits on a floor at a point: floor all round it near level, and nothing near it it must
		/// keep off (as the showers that came down on the bare floor kept: kept places, climbs, crossings, bridges and ramps,
		/// chasms, shafts, other water, another level under it); the lowest and highest of its rim.
		/// </summary>
		private bool SplashFits(Vector3 at, out float low, out float high)
		{
			low = high = at.y;
			for (int k = 0; k < 12; k++)
			{
				float found = HeldFloor(at + LavaDir(30f * k) * (SplashRadius + 0.3f), at.y + 0.8f);
				if (float.IsNaN(found))
				{
					return false;
				}
				low = Mathf.Min(low, found);
				high = Mathf.Max(high, found);
			}
			if (high - low > 0.4f || Awash(low - SplashDrop - SplashDepth))
			{
				return false;
			}
			foreach ((Vector3 keep, float radius, float height) in lavaKeeps)
			{
				if (Plan2(at, keep) < radius + SplashRadius + 1f && Mathf.Abs(at.y - keep.y) < height)
				{
					return false;
				}
			}
			foreach (CaveClimb c in Climbs)
			{
				if (Plan2(c.Bottom, at) < 3f + SplashRadius || Plan2(c.Top, at) < 3f + SplashRadius)
				{
					return false;
				}
			}
			foreach (CaveCrossing c in Crossings)
			{
				if (PlanToSegment(at, c.A, c.B, out _) < c.HalfWidth + 2f + SplashRadius)
				{
					return false;
				}
			}
			foreach (CaveSolid sol in Solids)
			{
				if ((sol.Kind == CaveSolidKind.Bridge || sol.Kind == CaveSolidKind.Ramp || sol.Kind == CaveSolidKind.Spiral) && PlanToSegment(at, sol.A, sol.B, out _) < sol.Width + 2f + SplashRadius)
				{
					return false;
				}
			}
			foreach (CaveRift r in Rifts)
			{
				if (PlanToSegment(at, r.A, r.B, out _) < r.Width + 2f + SplashRadius)
				{
					return false;
				}
			}
			foreach (CaveShaft sh in Shafts)
			{
				if (Plan2(at, sh.Bottom) < sh.Radius * 1.42f + 2f + SplashRadius)
				{
					return false;
				}
			}
			foreach ((Vector3 laid, float half, int _) in lavaLaid)
			{
				if (Plan2(laid, at) < half + 1.5f + SplashRadius && Mathf.Abs(laid.y - at.y) < 3f)
				{
					return false;
				}
			}
			return !LavaOverLower(at, SplashRadius) && !NearHollow(at + Vector3.up * 0.5f, SplashRadius);
		}

		/// <summary>
		/// Where a point lies over a stream's channel (a stretch with its bed cut, never a fall's): its surface there and how far off
		/// the line, in plan; false where it lies over none (by more than half its width).
		/// </summary>
		private static bool OnChannel(in CaveStreamBody stream, Vector3 p, out float surface, out float off)
		{
			surface = float.NaN;
			off = float.MaxValue;
			for (int i = 0; i + 1 < stream.Path.Length; i++)
			{
				if (float.IsNaN(stream.Banks[i]) || float.IsNaN(stream.Banks[i + 1]))
				{
					continue;
				}
				float d = PlanToSegment(p, stream.Path[i], stream.Path[i + 1], out float t);
				if (d < off)
				{
					off = d;
					surface = Mathf.Lerp(stream.Path[i].y, stream.Path[i + 1].y, t);
				}
			}
			return off <= 0.5f * stream.Width;
		}

		/// <summary>The radius of a shower's own plunge pool, metres: a basin its water wore in the floor.</summary>
		private const float ShowerPool = 0.9f;

		/// <summary>A shower: out of its crack, a point falling midway, and where it lands in water (<paramref name="into"/> the pool it feeds, −1 a stream).</summary>
		private static void AddShower(CaveStreamPlan plan, Vector3 lip, Vector3 landing, float width, int chamber, int into)
		{
			plan.Bodies.Add(new CaveStreamBody
			{
				Kind = CaveStreamKind.Stream,
				Path = new[] { lip, Vector3.Lerp(lip, landing, 0.5f), landing },
				Falls = new[] { false, true, true },
				Width = width,
				Depth = ShowerDepth,
				Banks = new[] { float.NaN, float.NaN, float.NaN },
				Tops = new[] { float.NaN, float.NaN, float.NaN },
				Passage = -1,
				Chamber = chamber,
				Pit = -1,
				Into = into,
			});
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

		/// <summary>How deep a shower's water runs where it lands (a film: its discharge, for the curtain's thickness), metres.</summary>
		public const float ShowerDepth = 0.12f;
	}
}
#endif
