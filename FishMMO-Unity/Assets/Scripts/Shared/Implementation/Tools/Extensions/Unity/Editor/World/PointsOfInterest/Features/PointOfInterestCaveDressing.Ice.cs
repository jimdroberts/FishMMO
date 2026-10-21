#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class PointOfInterestCaveDressing
	{
		private sealed partial class Dresser
		{
			/// <summary>What the ice pass laid: falls, curtains, pools, hoarfrost, frozen stream ice and the falls' lights.</summary>
			public int IceFalls, IceCurtains, IcePools, IceFrost, IceStreams, IceLights;

			/// <summary>
			/// An ice cave's ice (2026-10-20, caves livened: "frozen waterfalls down walls and drops, icicle curtains along roof edges,
			/// frozen pools on floors (walkable, glassy), frost on the walls near them"): laid by <see cref="CaveIce.Lay"/> over the
			/// cave's cold zone and over the water its streams' plan freezes (<see cref="CaveStreams.Plan"/>). Last of all the passes,
			/// on streams of its own, so nothing laid before it moves; nothing at all in any other form of cave.
			/// </summary>
			private void Ice()
			{
				if (!CaveIce.Applies(system))
				{
					return;
				}
				CaveIceLayout layout = CaveIce.Lay(system, habitat ??= CaveHabitat.For(system), CaveStreams.Plan(system),
					(p, margin) => InLane(p, margin) || NearMouth(p), (p, margin) => OnFeature(p, margin) || Kept(p, margin) || NearMouth(p), glowLights);
				bool missing = false;
				foreach (CaveIcePiece piece in layout.Pieces)
				{
					if (!CaveArt.TryGlow(piece.Kind, piece.Hue, piece.Variant, out CaveGlowArt art))
					{
						missing = true;
						continue;
					}
					if (!Place(art.Prefab, piece.Position, piece.Rotation, piece.Scale))
					{
						continue;
					}
					switch (piece.Kind)
					{
						case CaveGlowKind.IceFall: IceFalls++; break;
						case CaveGlowKind.IcicleCurtain: IceCurtains++; break;
						case CaveGlowKind.Hoarfrost: IceFrost++; break;
						default:
							if (piece.Stream)
							{
								IceStreams++;
							}
							else
							{
								IcePools++;
							}
							break;
					}
				}
				if (missing)
				{
					Missing.Add("cave ice art");
				}
				foreach (Vector3 light in layout.Lights)
				{
					// A blue glow-ice light (the crystals' hue 1), as an ice cave's frost crystals have.
					Glow(light, CaveIce.FallLightReach, 1);
					IceLights++;
				}
				PointOfInterestFeatureKit.Note(context, $"cave ice: {IceFalls} frozen falls, {IceCurtains} icicle curtains, {IcePools} frozen pools, {IceStreams} pieces of frozen stream, {IceFrost} hoarfrost, {IceLights} ice lights");
			}

			/// <summary>Within <paramref name="margin"/> of a site's kept ground (its camp, its dungeon gate's forecourt, a grotto's ledge).</summary>
			private bool Kept(Vector3 p, float margin)
			{
				foreach ((Vector3 centre, float radius) in keepClear)
				{
					if (Mathf.Abs(p.y - centre.y) < 3f && new Vector2(p.x - centre.x, p.z - centre.z).sqrMagnitude < (radius + margin) * (radius + margin))
					{
						return true;
					}
				}
				return false;
			}
		}
	}

	/// <summary>One piece of an ice cave's ice (<see cref="CaveIce"/>): its glow art, where (system frame), how turned and how large.</summary>
	public struct CaveIcePiece
	{
		public CaveGlowKind Kind;
		public int Hue, Variant;
		public Vector3 Position;
		public Quaternion Rotation;
		public Vector3 Scale;
		/// <summary>A piece of a frozen stream (or the ice of a pool the streams' plan froze), not one of the pass's own.</summary>
		public bool Stream;
	}

	/// <summary>What <see cref="CaveIce.Lay"/> lays in an ice cave: pieces and the falls' lights in the system's frame, and its counts.</summary>
	public sealed class CaveIceLayout
	{
		public readonly List<CaveIcePiece> Pieces = new List<CaveIcePiece>();
		/// <summary>Where a blue light stands before a tall fall (SceneEmitterKind.Crystal, hue 1), system frame.</summary>
		public readonly List<Vector3> Lights = new List<Vector3>();
		public int Falls, Curtains, Pools, Sheets, Frost, StreamPieces, StreamFalls;
	}

	/// <summary>
	/// An ice cave's ice (2026-10-20, caves livened): frozen waterfalls down its walls and over its drops, icicle curtains along its
	/// roof's edges, frozen pools in its basins and sheets of ice on its floors (walked on, glassy), hoarfrost on the walls round
	/// them, and the water its streams' plan would run frozen in place (<see cref="CaveStreamPlan.Frozen"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where it is cold.</b> An ice cave is frozen in its cold zone (CaveHabitat: <see cref="ColdReach"/> m walked in from a
	/// mouth) and everywhere it lies more than <see cref="ColdSinkDepth"/> m under its mouth's sill, however far in (2026-10-21,
	/// "cold traps freeze deep": the winter air sinks and pools there and never leaves; this replaced biome-suite decision 51's
	/// 160 m reach for the sink), and meltwater from the thaw ring past the zone runs back in and freezes (<see cref="ThawRing"/>):
	/// the ice is laid over both (<see cref="Cold"/>); the cold trap the layout sank in the deepest chamber
	/// (<see cref="CavePitKind.ColdTrap"/>) is always frozen, its ice the thickest. How much of each kind of ice a cave holds
	/// grows with its chambers.
	/// </para>
	/// <para>
	/// <b>Measured on the field.</b> A fall stands against a face found by marching the rock (a chamber's wall, a terrace's
	/// riser, a chasm's side), from its foot up to where the face meets the roof or falls back at a lip, leaning as the face
	/// leans; a curtain hangs where the roof steps down by <see cref="RoofStep"/> m within a metre and a bit, along the step, the
	/// icicles over a way kept <see cref="Headroom"/> off its floor; a pool's ice lies dead level in a basin the layout sank (its
	/// surface from the bowl's own geometry, <see cref="CaveSystem.BowlGrade"/>), a sheet of ice on a floor where a plane fits
	/// the floor within <see cref="SheetResidual"/> m; hoarfrost where a wall is flat enough under its patch.
	/// </para>
	/// <para>
	/// <b>Never in the way.</b> No fall's foot or cone, no curtain and no pool on any way the layout built (bridges, ramps,
	/// climbs, a terrace's walk, a pit's shaft), on a site's kept ground or at a mouth (<c>feature</c>); no fall's cone on a lane
	/// (<c>way</c>). Ice on the floor is walked on: a sheet may lie across a lane (its rim is bevelled into the rock, no step).
	/// </para>
	/// <para>
	/// <b>Pure and deterministic.</b> A function of the system, its habitat, its streams' plan and what the caller refuses, each
	/// kind of ice on a stream of its own: the same cave lays the same, and the passes before it never see it.
	/// </para>
	/// </remarks>
	public static class CaveIce
	{
		/// <summary>
		/// The cold zone and the thaw ring (CaveHabitat's), metres walked in from a mouth, and how far under the mouth's sill a
		/// point lies in the cold trap, frozen however far in (2026-10-21; it was frozen only within 160 m, decision 51).
		/// </summary>
		public const float ColdReach = 90f, ColdSinkDepth = 8f, ThawRing = 20f;
		/// <summary>The least face a fall is laid down, and the most of a face it covers (a seep high on a taller one), metres.</summary>
		public const float MinFall = 1.8f, MaxFall = 14f;
		/// <summary>How far a roof must step down within <see cref="StepReach"/> for icicles to hang along the step, metres.</summary>
		public const float RoofStep = 0.8f, StepReach = 1.3f;
		/// <summary>The least clearance under icicles over a way, metres (the dressing's headroom).</summary>
		public const float Headroom = 2.4f;
		/// <summary>
		/// The steepest floor a sheet of ice lies on (rise over run: a chamber's floor climbs with its passage, and the ice that froze
		/// as it ran over it follows it), a frozen stream's, and how far the floor may stray from the sheet's plane, metres.
		/// </summary>
		public const float SheetSlope = 0.42f, StreamSlope = 0.45f, SheetResidual = 0.16f;
		/// <summary>The most pieces a frozen stream's ice is laid in, a system (each up to <see cref="StreamPiece"/> m long).</summary>
		public const int MaxStreamPieces = 40;
		public const float StreamPiece = 4f;
		/// <summary>How frozen a place must be for ice to be laid there (<see cref="Cold"/>): all the cold zone and the thaw ring but its far edge.</summary>
		public const float Freezing = 0.1f;
		/// <summary>The reach of a fall's blue light, metres, the least distance between any two cave lights, and the most a cave gets.</summary>
		public const float FallLightReach = 9f, LightSpacing = 9f;
		public const int MaxLights = 5;
		/// <summary>The hues of <see cref="ProceduralArtCatalogue.IceHues"/>: the blue-white near a mouth, the deep blue far in.</summary>
		public const int ClearHue = 0, GlacialHue = 1;
		/// <summary>How far in the ice turns from clear to glacial blue, metres walked from a mouth.</summary>
		public const float GlacialFrom = 45f;

		/// <summary>Whether the system is an ice cave (the only form this pass dresses).</summary>
		public static bool Applies(CaveSystem system) => system != null && system.Form == CaveForm.IceCave;

		/// <summary>
		/// How frozen a point of an ice cave is (system frame): 1 within its cold zone and anywhere in its cold trap (more than
		/// <see cref="ColdSinkDepth"/> under the nearest mouth's sill), falling to 0 across the thaw ring past the zone (the
		/// meltwater that runs back in freezes there), 0 beyond. A system without a mouth is frozen through.
		/// </summary>
		public static float Cold(CaveHabitat habitat, CaveSystem system, Vector3 local)
		{
			if (habitat == null || system == null || system.Mouths.Count == 0)
			{
				return 1f;
			}
			if (local.y < SillOf(system, local) - ColdSinkDepth)
			{
				return 1f;
			}
			float walked = habitat.WalkedFromMouth(local);
			if (float.IsInfinity(walked) || float.IsNaN(walked))
			{
				return 1f;
			}
			return 1f - Mathf.Clamp01((walked - ColdReach) / ThawRing);
		}

		/// <summary>
		/// The sill the cold air pools under: the floor of the way-in mouth nearest a point (an opening in a roof is no sill; a
		/// system of only those takes the nearest of them).
		/// </summary>
		public static float SillOf(CaveSystem system, Vector3 local)
		{
			float sill = 0f, best = float.MaxValue;
			bool anyWay = false;
			foreach (CaveMouth mouth in system.Mouths)
			{
				anyWay |= mouth.Kind != CaveMouthKind.Skylight;
			}
			foreach (CaveMouth mouth in system.Mouths)
			{
				if (anyWay && mouth.Kind == CaveMouthKind.Skylight)
				{
					continue;
				}
				float d = (local - mouth.Floor).sqrMagnitude;
				if (d < best)
				{
					best = d;
					sill = mouth.Floor.y;
				}
			}
			return sill;
		}

		/// <summary>
		/// Lays an ice cave's ice. <paramref name="way"/>(p, margin): a lane or a mouth there (no fall's cone); <paramref name="feature"/>:
		/// a built way, kept ground or a mouth (nothing at all). <paramref name="lights"/>: the cave lights already laid, kept
		/// <see cref="LightSpacing"/> off. Empty for any other form of cave.
		/// </summary>
		public static CaveIceLayout Lay(CaveSystem system, CaveHabitat habitat, CaveStreamPlan streams, Func<Vector3, float, bool> way, Func<Vector3, float, bool> feature, IReadOnlyList<Vector3> lights = null)
		{
			var layout = new CaveIceLayout();
			if (!Applies(system))
			{
				return layout;
			}
			system.Prepare();
			habitat ??= CaveHabitat.For(system);
			way ??= (p, m) => false;
			feature ??= (p, m) => false;
			var laying = new Laying(system, habitat, way, feature, lights, layout);
			int chambers = system.Chambers.Count;
			// As many as its chambers call for (2026-10-21: a deep system's dozens of frozen chambers were held to a small cave's ice).
			laying.MaxFalls = Mathf.Clamp(2 + chambers / 2, 2, 40);
			laying.MaxCurtains = Mathf.Clamp(4 + chambers, 4, 80);
			laying.MaxSheets = Mathf.Clamp(2 + chambers / 2, 2, 40);
			laying.MaxFrost = Mathf.Clamp(20 + 2 * chambers, 40, 160);
			// The water first (where the streams' plan would run it, frozen), then the basins, then the pass's own ice, the frost last.
			laying.Streams(streams);
			laying.Basins();
			laying.Falls();
			laying.Curtains();
			laying.Sheets();
			laying.Frost();
			return layout;
		}

		/// <summary>The extents of a frozen pool's variant on x and z at scale 1 (its widest is 1), measured once from its art's mesh.</summary>
		public static Vector2 PoolExtent(int variant)
		{
			lock (poolExtents)
			{
				if (!poolExtents.TryGetValue(variant, out Vector2 e))
				{
					Bounds b = CaveGlowMeshes.Build(CaveGlowKind.FrozenPool, variant, 0, ProceduralArtCatalogue.DefaultSeed).Bounds;
					e = new Vector2(Mathf.Max(b.max.x, -b.min.x), Mathf.Max(b.max.z, -b.min.z));
					poolExtents[variant] = e;
				}
				return e;
			}
		}

		private static readonly Dictionary<int, Vector2> poolExtents = new Dictionary<int, Vector2>();

		// ── The field ─────────────────────────────────────────────────

		private static bool Rock(CaveSystem s, Vector3 p) => s.Void(p) >= 0f;

		/// <summary>The floor under and the roof over a point in the void (marched coarse, then bisected); false in the rock.</summary>
		public static bool FloorAndRoof(CaveSystem s, Vector3 p, out float floor, out float roof)
		{
			floor = roof = p.y;
			if (Rock(s, p))
			{
				return false;
			}
			if (!March(s, p, Vector3.down, 20f, 0.25f, out float down) || !March(s, p, Vector3.up, 60f, 0.25f, out float up))
			{
				return false;
			}
			floor = p.y - down;
			roof = p.y + up;
			return roof - floor > 1f;
		}

		/// <summary>How far from a point in the void along a direction the rock begins (within <paramref name="reach"/>); false where it does not.</summary>
		public static bool March(CaveSystem s, Vector3 from, Vector3 dir, float reach, float step, out float distance)
		{
			distance = 0f;
			if (Rock(s, from))
			{
				return false;
			}
			float last = 0f;
			for (float d = step; d <= reach + 1e-4f; d += step)
			{
				if (Rock(s, from + dir * d))
				{
					float a = last, b = d;
					for (int i = 0; i < 7; i++)
					{
						float mid = 0.5f * (a + b);
						if (Rock(s, from + dir * mid))
						{
							b = mid;
						}
						else
						{
							a = mid;
						}
					}
					distance = 0.5f * (a + b);
					return true;
				}
				last = d;
			}
			return false;
		}

		/// <summary>The rock's outward normal (into the void) at a point near its surface.</summary>
		private static Vector3 Outward(CaveSystem s, Vector3 p)
		{
			const float e = 0.08f;
			var g = new Vector3(
				s.Void(p + new Vector3(e, 0f, 0f)) - s.Void(p - new Vector3(e, 0f, 0f)),
				s.Void(p + new Vector3(0f, e, 0f)) - s.Void(p - new Vector3(0f, e, 0f)),
				s.Void(p + new Vector3(0f, 0f, e)) - s.Void(p - new Vector3(0f, 0f, e)));
			return g.sqrMagnitude > 1e-12f ? -g.normalized : Vector3.up;
		}

		private static Vector3 Flat(Vector3 v)
		{
			v.y = 0f;
			return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
		}

		private static Vector3 Dir(float radians) => new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));

		// ── Laying ────────────────────────────────────────────────────

		/// <summary>One laying of a system's ice: what it has taken, and each kind's own random stream.</summary>
		private sealed class Laying
		{
			private readonly CaveSystem system;
			private readonly CaveHabitat habitat;
			private readonly Func<Vector3, float, bool> way, feature;
			private readonly IReadOnlyList<Vector3> earlier;
			private readonly CaveIceLayout layout;
			/// <summary>What the pass's own pieces cover (centre, radius in plan): none laid over another.</summary>
			private readonly List<(Vector3 at, float radius)> taken = new List<(Vector3, float)>();
			/// <summary>Where ice was laid, for the hoarfrost round it (a point on the floor near it, how far the frost may look).</summary>
			private readonly List<(Vector3 at, float reach)> spots = new List<(Vector3, float)>();
			/// <summary>The pits whose ice the streams' plan laid (its frozen pools), so the basins pass leaves them.</summary>
			private readonly HashSet<int> pitsTaken = new HashSet<int>();
			public int MaxFalls, MaxCurtains, MaxSheets, MaxFrost;

			public Laying(CaveSystem system, CaveHabitat habitat, Func<Vector3, float, bool> way, Func<Vector3, float, bool> feature, IReadOnlyList<Vector3> earlier, CaveIceLayout layout)
			{
				this.system = system;
				this.habitat = habitat;
				this.way = way;
				this.feature = feature;
				this.earlier = earlier;
				this.layout = layout;
			}

			private DeterministicRNG Stream(uint salt) => new DeterministicRNG(unchecked((int)((uint)system.Seed * 0x9E3779B1u ^ salt)));

			private float ColdAt(Vector3 p) => Cold(habitat, system, p);

			private bool Frozen(Vector3 p) => ColdAt(p) >= Freezing;

			private int HueAt(Vector3 p)
			{
				float walked = habitat.WalkedFromMouth(p);
				return !float.IsInfinity(walked) && walked < GlacialFrom ? ClearHue : GlacialHue;
			}

			private bool Free(Vector3 p, float radius)
			{
				foreach ((Vector3 at, float r) in taken)
				{
					if (Mathf.Abs(p.y - at.y) < 4f && new Vector2(p.x - at.x, p.z - at.z).sqrMagnitude < (r + radius) * (r + radius))
					{
						return false;
					}
				}
				return true;
			}

			private void Add(CaveGlowKind kind, int hue, int variant, Vector3 at, Quaternion rotation, Vector3 scale, bool stream = false)
			{
				layout.Pieces.Add(new CaveIcePiece { Kind = kind, Hue = hue, Variant = variant, Position = at, Rotation = rotation, Scale = scale, Stream = stream });
			}

			// ── Frozen water from the streams' plan ──

			public void Streams(CaveStreamPlan plan)
			{
				if (plan == null || !plan.Active || !plan.Frozen)
				{
					return;
				}
				var rng = Stream(0x51CEu);
				for (int b = 0; b < plan.Bodies.Count; b++)
				{
					CaveStreamBody body = plan.Bodies[b];
					if (body.Path == null || body.Path.Length == 0)
					{
						continue;
					}
					if (body.Kind == CaveStreamKind.Pool)
					{
						// Frozen where the cave is cold, as the streams are; past the thaw ring nothing of it shows (none is written as water).
						if (body.Pit >= 0 && body.Pit < system.Pits.Count && Basin(body.Pit, true, false))
						{
							pitsTaken.Add(body.Pit);
							layout.Pools++;
							continue;
						}
						Vector3 c = body.Path[0];
						if (Frozen(c) && Sheet(c, Mathf.Max(1f, body.Width), SheetSlope, rng.Range(0f, 360f), rng.NextFloat() < 0.5f ? 0 : 2, true, true))
						{
							layout.Pools++;
						}
						continue;
					}
					StreamIce(body, rng);
				}
			}

			/// <summary>A frozen stream: tilted pieces of sheet ice flush on its floor, its falls frozen down their faces.</summary>
			private void StreamIce(CaveStreamBody body, DeterministicRNG rng)
			{
				Vector3[] path = body.Path;
				float half = 0.5f * Mathf.Clamp(0.8f * body.Width, 0.8f, 2.2f);
				Vector2 extent = PoolExtent(1);
				for (int i = 1; i < path.Length; i++)
				{
					Vector3 a = path[i - 1], b = path[i];
					float draw = rng.NextFloat();
					if (body.Falls != null && i < body.Falls.Length && body.Falls[i])
					{
						if (FrozenFall(a, b, draw))
						{
							layout.StreamFalls++;
						}
						continue;
					}
					float length = new Vector2(b.x - a.x, b.z - a.z).magnitude;
					int pieces = Mathf.Max(1, Mathf.CeilToInt(length / StreamPiece));
					// Past the thaw ring the stream is not frozen (nor written as water: nothing of it shows).
					if (!Frozen(a) && !Frozen(b))
					{
						continue;
					}
					for (int k = 0; k < pieces; k++)
					{
						Vector3 p0 = Vector3.Lerp(a, b, k / (float)pieces), p1 = Vector3.Lerp(a, b, (k + 1f) / pieces);
						// Flush on the floor the plan measured, re-found under each piece's ends (the bevel hides what strays between).
						p0.y = FloorNear(p0);
						p1.y = FloorNear(p1);
						Vector3 run = p1 - p0;
						float flat = new Vector2(run.x, run.z).magnitude;
						if (flat < 0.2f || Mathf.Abs(run.y) > StreamSlope * flat)
						{
							continue;
						}
						Vector3 mid = 0.5f * (p0 + p1);
						if (layout.StreamPieces >= MaxStreamPieces || feature(mid, half))
						{
							continue;
						}
						Vector3 along = run.normalized;
						Vector3 across = Vector3.Cross(Vector3.up, Flat(run)).normalized;
						Vector3 normal = Vector3.Cross(along, across);
						if (normal.y < 0f)
						{
							normal = -normal;
						}
						// Every other piece a hair higher, so where two overlap at a joint they never fight.
						Vector3 at = mid + normal * (0.02f + 0.006f * (k & 1));
						Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(along, normal), normal);
						float sx = 0.5f * flat / Mathf.Max(0.2f, extent.x) + 0.3f, sz = half / Mathf.Max(0.2f, extent.y);
						Add(CaveGlowKind.FrozenPool, HueAt(at), 1, at - normal * CaveGlowMeshes.FrozenPoolRise, rotation, new Vector3(sx, 1f, sz), true);
						layout.StreamPieces++;
						spots.Add((mid, 4f));
					}
				}
			}

			/// <summary>The floor under a point near the floor (system frame), or its own height where none is found.</summary>
			private float FloorNear(Vector3 p)
			{
				Vector3 from = p + Vector3.up * 0.6f;
				return March(system, from, Vector3.down, 2.5f, 0.1f, out float d) ? from.y - d : p.y;
			}

			/// <summary>A stream's fall frozen: an ice fall from its lip down to its foot, against the face it pours down where there is one.</summary>
			private bool FrozenFall(Vector3 top, Vector3 foot, float draw)
			{
				float height = top.y - foot.y;
				if (height < 1.2f || layout.Falls + layout.StreamFalls >= MaxFalls + 4)
				{
					return false;
				}
				// Toward the lip the water came over, else the nearest wall at half its height.
				Vector3 into = new Vector3(top.x - foot.x, 0f, top.z - foot.z);
				if (into.sqrMagnitude < 0.09f)
				{
					float best = 3f;
					into = Vector3.forward;
					Vector3 mid = foot + Vector3.up * (0.5f * height);
					for (int k = 0; k < 8; k++)
					{
						Vector3 d = Dir(k * Mathf.PI / 4f);
						if (March(system, mid, d, best, 0.2f, out float hit) && hit < best)
						{
							best = hit;
							into = d;
						}
					}
				}
				into = Flat(into);
				Vector3 up = (top - foot).normalized;
				Vector3 forward = into - up * Vector3.Dot(into, up);
				if (forward.sqrMagnitude < 1e-4f)
				{
					forward = into;
				}
				int variant = height < 3f ? 0 : draw < 0.5f ? 2 : 1;
				float wide = Mathf.Clamp(height / CaveGlowMeshes.IceFallHeight, 0.6f, 1.3f);
				Add(CaveGlowKind.IceFall, HueAt(foot), variant, foot, Quaternion.LookRotation(forward.normalized, up), new Vector3(wide, Mathf.Min(height, MaxFall) / CaveGlowMeshes.IceFallHeight, wide), true);
				taken.Add((foot, 0.5f * CaveGlowMeshes.IceFallWidths[variant] * wide));
				spots.Add((foot, 6f));
				return true;
			}

			// ── Basins ──

			/// <summary>Every basin the layout sank (pothole, sinkhole, cold trap) frozen where it is cold; a cold trap always.</summary>
			public void Basins()
			{
				for (int i = 0; i < system.Pits.Count; i++)
				{
					if (!pitsTaken.Contains(i) && Basin(i, false, false))
					{
						layout.Pools++;
					}
				}
			}

			/// <summary>
			/// A basin's ice: dead level, standing a share of its depth over its flat bottom (a cold trap's the most), reaching up its
			/// sides at the bowl's grade; its surface widened a little so its lobes bury their rim in the sides.
			/// </summary>
			private bool Basin(int index, bool stream, bool always = false)
			{
				CavePit pit = system.Pits[index];
				if (pit.Kind != CavePitKind.Pothole && pit.Kind != CavePitKind.Sinkhole && pit.Kind != CavePitKind.ColdTrap && pit.Kind != CavePitKind.Gour)
				{
					return false;
				}
				if (pit.Hole < 0 || pit.Hole >= system.Hollows.Count || system.Hollows[pit.Hole].Kind != CaveHollowKind.Bowl)
				{
					return false;
				}
				CaveHollow bowl = system.Hollows[pit.Hole];
				if (!always && pit.Kind != CavePitKind.ColdTrap && !Frozen(bowl.A))
				{
					return false;
				}
				float bed = bowl.A.y - bowl.Depth;
				float share = pit.Kind == CavePitKind.ColdTrap ? 0.72f : 0.55f;
				float surface = bed + Mathf.Clamp(share * bowl.Depth, 0.3f, Mathf.Max(0.3f, bowl.Depth - 0.25f));
				float flat = Mathf.Max(0.3f * bowl.Radius, bowl.Radius - bowl.Depth / CaveSystem.BowlGrade);
				float reach = Mathf.Min(bowl.Radius, flat + (surface - bed) / CaveSystem.BowlGrade);
				var at = new Vector3(bowl.A.x, surface, bowl.A.z);
				if (reach < 0.6f || Rock(system, at + Vector3.up * 0.3f))
				{
					return false;
				}
				uint h = PointOfInterestPlanner.Hash(system.Seed, Mathf.RoundToInt(at.x * 10f), Mathf.RoundToInt(at.z * 10f), 0x1CE);
				Vector2 extent = PoolExtent(0);
				float s = 1.08f * reach;
				Add(CaveGlowKind.FrozenPool, pit.Kind == CavePitKind.ColdTrap ? GlacialHue : HueAt(at), 0, at + Vector3.down * CaveGlowMeshes.FrozenPoolRise, Quaternion.Euler(0f, PointOfInterestPlanner.Unit(h) * 360f, 0f),
					new Vector3(s / Mathf.Max(0.2f, extent.x), 1f, s / Mathf.Max(0.2f, extent.y)), stream);
				taken.Add((at, reach));
				spots.Add((new Vector3(at.x, bowl.A.y, at.z), bowl.Radius + 5f));
				return true;
			}

			// ── Sheets of floor ice ──

			/// <summary>Sheets of ice on the floors of cold chambers, where a plane fits the floor; a long one now and then down a passage.</summary>
			public void Sheets()
			{
				var rng = Stream(0x5EE7u);
				int laid = 0;
				foreach (CaveChamber chamber in system.Chambers)
				{
					float area = Mathf.PI * chamber.Radii.x * chamber.Radii.z;
					int tries = Mathf.Clamp(Mathf.RoundToInt(area / 140f), 1, 4);
					float cy = chamber.Yaw * Mathf.Deg2Rad;
					Vector3 along = Dir(cy), across = new Vector3(along.z, 0f, -along.x);
					for (int i = 0; i < tries; i++)
					{
						float a = rng.Range(0f, 2f * Mathf.PI), t = Mathf.Sqrt(rng.NextFloat()) * 0.7f;
						float radius = rng.Range(1.2f, Mathf.Clamp(0.3f * Mathf.Min(chamber.Radii.x, chamber.Radii.z), 1.5f, 4f));
						float yaw = rng.Range(0f, 360f), roll = rng.NextFloat();
						if (laid >= MaxSheets)
						{
							continue;
						}
						Vector3 p = chamber.Centre + along * (Mathf.Cos(a) * t * chamber.Radii.x) + across * (Mathf.Sin(a) * t * chamber.Radii.z);
						p.y = CaveSystem.FloorOf(chamber, p);
						if (!Frozen(p))
						{
							continue;
						}
						if (Sheet(p, radius, SheetSlope, yaw, roll < 0.6f ? 0 : 2, false, false))
						{
							laid++;
							layout.Sheets++;
						}
					}
				}
				foreach (CavePassage passage in system.Passages)
				{
					float next = system.Mouths.Count > 0 ? system.Mouths[0].Zone : 0f;
					for (int i = 1; i < passage.Nodes.Count; i++)
					{
						CaveNode node = passage.Nodes[i];
						if (node.Along < next)
						{
							continue;
						}
						next = node.Along + rng.Range(18f, 36f);
						float roll = rng.NextFloat();
						if (laid >= MaxSheets || roll > 0.5f)
						{
							continue;
						}
						var p = new Vector3(node.Centre.x, node.Floor, node.Centre.z);
						if (!Frozen(p))
						{
							continue;
						}
						Vector3 dir = Flat(node.Centre - passage.Nodes[i - 1].Centre);
						// Long down the passage: v1 (0.6 as wide as long), turned along it.
						if (Sheet(p, Mathf.Clamp(0.9f * node.Radius, 1f, 2.6f), SheetSlope, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg - 90f, 1, false, false))
						{
							laid++;
							layout.Sheets++;
						}
					}
				}
			}

			/// <summary>
			/// A sheet of ice on the floor round a point: the plane that best fits the floor under its rim and middle (no steeper than
			/// <paramref name="maxSlope"/>, the floor nowhere further than <see cref="SheetResidual"/> from it), shrunk until one
			/// fits; its surface a little over the plane so only the rock's highest knobs come through it.
			/// </summary>
			private bool Sheet(Vector3 centre, float radius, float maxSlope, float yaw, int variant, bool stream, bool always)
			{
				for (float r = radius; r >= 0.9f; r *= 0.75f)
				{
					if (!Free(centre, r) || feature(centre, r))
					{
						continue;
					}
					const int around = 8;
					var xs = new float[around + 1];
					var zs = new float[around + 1];
					var ys = new float[around + 1];
					bool ok = true;
					for (int k = 0; k <= around && ok; k++)
					{
						Vector2 off = k == 0 ? Vector2.zero : new Vector2(Mathf.Cos(k * Mathf.PI / 4f), Mathf.Sin(k * Mathf.PI / 4f)) * (0.95f * r);
						// From over the floor's high side however it slopes (no steeper than a sheet may lie).
						var p = new Vector3(centre.x + off.x, centre.y + 0.6f + maxSlope * off.magnitude, centre.z + off.y);
						if (!FloorAndRoof(system, p, out float floor, out float roof) || roof - floor < 2.2f || Mathf.Abs(floor - centre.y) > 0.6f + 1.1f * maxSlope * off.magnitude)
						{
							ok = false;
							break;
						}
						xs[k] = off.x;
						zs[k] = off.y;
						ys[k] = floor;
					}
					if (!ok || !FitPlane(xs, zs, ys, out float a0, out float bx, out float bz))
					{
						continue;
					}
					if (bx * bx + bz * bz > maxSlope * maxSlope)
					{
						continue;
					}
					float over = 0f, under = 0f;
					for (int k = 0; k <= around; k++)
					{
						float res = ys[k] - (a0 + bx * xs[k] + bz * zs[k]);
						over = Mathf.Max(over, res);
						under = Mathf.Min(under, res);
					}
					if (over - under > SheetResidual)
					{
						continue;
					}
					var normal = new Vector3(-bx, 1f, -bz).normalized;
					var at = new Vector3(centre.x, a0 + 0.5f * over + 0.02f, centre.z);
					Vector3 heading = Dir(yaw * Mathf.Deg2Rad);
					Vector3 forward = heading - normal * Vector3.Dot(heading, normal);
					Vector2 extent = PoolExtent(variant);
					float s = r / Mathf.Max(extent.x, extent.y);
					Add(CaveGlowKind.FrozenPool, HueAt(at), variant, at - normal * CaveGlowMeshes.FrozenPoolRise, Quaternion.LookRotation(forward.normalized, normal), new Vector3(s, 1f, s), stream);
					taken.Add((at, r));
					spots.Add((at, r + 5f));
					return true;
				}
				return false;
			}

			/// <summary>The least-squares plane y = a + bx·x + bz·z through the points; false when they do not fix one.</summary>
			private static bool FitPlane(float[] xs, float[] zs, float[] ys, out float a, out float bx, out float bz)
			{
				a = bx = bz = 0f;
				int n = xs.Length;
				double sx = 0, sz = 0, sy = 0, sxx = 0, szz = 0, sxz = 0, sxy = 0, szy = 0;
				for (int i = 0; i < n; i++)
				{
					sx += xs[i]; sz += zs[i]; sy += ys[i];
					sxx += xs[i] * xs[i]; szz += zs[i] * zs[i]; sxz += xs[i] * zs[i];
					sxy += xs[i] * ys[i]; szy += zs[i] * ys[i];
				}
				// Centred, so the slope is solved apart from the mean.
				double mx = sx / n, mz = sz / n, my = sy / n;
				double cxx = sxx - n * mx * mx, czz = szz - n * mz * mz, cxz = sxz - n * mx * mz, cxy = sxy - n * mx * my, czy = szy - n * mz * my;
				double det = cxx * czz - cxz * cxz;
				if (Math.Abs(det) < 1e-9)
				{
					return false;
				}
				bx = (float)((cxy * czz - czy * cxz) / det);
				bz = (float)((czy * cxx - cxy * cxz) / det);
				a = (float)(my - bx * mx - bz * mz);
				return true;
			}

			// ── Ice falls ──

			/// <summary>Frozen falls down the tall faces of cold chambers, over the risers of their terraces and down the sides of their chasms.</summary>
			public void Falls()
			{
				var rng = Stream(0xFA11u);
				// Feet to try, each with the way into its face: a chamber's walls, a terrace's riser, a chasm's sides.
				var feet = new List<(Vector3 foot, Vector3 into)>();
				foreach (CaveChamber chamber in system.Chambers)
				{
					float perimeter = 2f * Mathf.PI * Mathf.Sqrt(0.5f * (chamber.Radii.x * chamber.Radii.x + chamber.Radii.z * chamber.Radii.z));
					int tries = Mathf.Clamp(Mathf.RoundToInt(perimeter / 10f), 2, 10);
					for (int i = 0; i < tries; i++)
					{
						float a = rng.Range(0f, 2f * Mathf.PI);
						if (WallFootIn(chamber, Dir(a), out Vector3 foot))
						{
							feet.Add((foot, Dir(a)));
						}
					}
				}
				foreach (CaveTerrace terrace in system.Terraces)
				{
					if (terrace.Height < MinFall || terrace.Solid < 0 || terrace.Solid >= system.Solids.Count)
					{
						continue;
					}
					CaveSolid slab = system.Solids[terrace.Solid];
					float t = rng.Range(0.25f, 0.75f);
					Vector3 lip = Vector3.Lerp(slab.A, slab.B, t) + terrace.Out * (slab.Width + 0.7f);
					// Down off the lip to the floor below, then back to the riser's foot.
					var probe = new Vector3(lip.x, lip.y - 0.5f * terrace.Height, lip.z);
					if (FloorAndRoof(system, probe, out float floor, out _) && March(system, new Vector3(lip.x, floor + 0.3f, lip.z), -terrace.Out, 2.5f, 0.1f, out float back))
					{
						Vector3 foot = new Vector3(lip.x, floor, lip.z) - terrace.Out * Mathf.Max(0f, back - 0.25f);
						feet.Add((foot, -terrace.Out));
					}
				}
				foreach (CaveRift rift in system.Rifts)
				{
					Vector3 line = Flat(rift.B - rift.A);
					var side = new Vector3(line.z, 0f, -line.x);
					float t = rng.Range(0.3f, 0.7f), sign = rng.NextFloat() < 0.5f ? -1f : 1f;
					Vector3 bottom = Vector3.Lerp(rift.A, rift.B, t);
					if (FloorAndRoof(system, bottom + Vector3.up * 0.6f, out float floor, out _) && March(system, new Vector3(bottom.x, floor + 0.3f, bottom.z), side * sign, 4f, 0.1f, out float wall))
					{
						feet.Add((new Vector3(bottom.x, floor, bottom.z) + side * (sign * Mathf.Max(0f, wall - 0.25f)), side * sign));
					}
				}
				foreach ((Vector3 foot, Vector3 into) in feet)
				{
					float draw = rng.NextFloat(), size = rng.Range(0.85f, 1.15f);
					if (layout.Falls >= MaxFalls)
					{
						break;
					}
					if (!Frozen(foot))
					{
						continue;
					}
					Fall(foot, into, draw, size);
				}
			}

			/// <summary>The foot of a chamber's wall along a heading from its middle, half a metre over its sloping floor.</summary>
			private bool WallFootIn(in CaveChamber chamber, Vector3 dir, out Vector3 foot)
			{
				foot = default;
				var from = new Vector3(chamber.Centre.x, CaveSystem.FloorOf(chamber, chamber.Centre) + 0.5f, chamber.Centre.z);
				if (Rock(system, from))
				{
					return false;
				}
				float reach = Mathf.Max(chamber.Radii.x, chamber.Radii.z) + 3f;
				Vector3 last = from;
				for (float d = 0.25f; d <= reach; d += 0.25f)
				{
					Vector3 p = from + dir * d;
					p.y = CaveSystem.FloorOf(chamber, p) + 0.5f;
					if (Rock(system, p))
					{
						Vector3 back = last - dir * 0.25f;
						Vector3 down = back + Vector3.up * 0.4f;
						if (!March(system, down, Vector3.down, 3f, 0.1f, out float drop))
						{
							return false;
						}
						foot = new Vector3(back.x, down.y - drop, back.z);
						return true;
					}
					last = p;
				}
				return false;
			}

			/// <summary>
			/// A face measured from a foot (on the floor, a little off the rock) along the way into it: up from the foot while the rock
			/// stays within reach behind and the air before it, until the roof or an overhang closes over it, or the rock falls back
			/// (a lip). Its bottom and top on the rock's surface.
			/// </summary>
			private bool Face(Vector3 foot, Vector3 into, out Vector3 bottom, out Vector3 faceTop, out bool lip)
			{
				bottom = faceTop = foot;
				lip = false;
				if (!March(system, foot + Vector3.up * 0.3f, into, 2f, 0.05f, out float first))
				{
					return false;
				}
				float last = first, good = 0.3f;
				var measured = new List<(float h, float d)> { (0.3f, first) };
				for (float h = 0.55f; h <= MaxFall + 0.3f; h += 0.25f)
				{
					Vector3 q = foot + Vector3.up * h;
					if (Rock(system, q))
					{
						break;
					}
					if (!March(system, q, into, last + 1.2f, 0.08f, out float d) || d - last > 0.45f)
					{
						lip = true;
						break;
					}
					// A face leans, bulges and hollows a little; a step back or out is where it ends.
					if (Mathf.Abs(d - last) > 0.25f)
					{
						break;
					}
					last = d;
					good = h;
					measured.Add((h, d));
				}
				// Near a plane from its foot to its top, no hollow behind the fall and no bulge through it: as high as that holds (a
				// vault curving over is followed only so far, the fall a seep high on its wall).
				int top = measured.Count - 1;
				for (; top > 0; top--)
				{
					(float hk, float dk) = measured[top];
					bool straight = true;
					for (int j = 1; j < top && straight; j++)
					{
						float line = Mathf.Lerp(first, dk, (measured[j].h - 0.3f) / Mathf.Max(0.25f, hk - 0.3f));
						straight = Mathf.Abs(measured[j].d - line) <= 0.35f;
					}
					if (straight)
					{
						break;
					}
					// Cut short of where it ran on: not a lip any more.
					lip = false;
				}
				good = measured[top].h;
				last = measured[top].d;
				bottom = new Vector3(foot.x, foot.y, foot.z) + into * first;
				faceTop = foot + Vector3.up * good + into * last;
				return good >= MinFall;
			}

			/// <summary>A frozen fall up a face from a foot, if the face is tall and broad enough and its cone keeps off every way.</summary>
			private bool Fall(Vector3 foot, Vector3 into, float draw, float size)
			{
				if (!Face(foot, into, out Vector3 bottom, out Vector3 top, out bool lip))
				{
					return false;
				}
				float height = top.y - bottom.y;
				int variant = height < 3.2f ? 0 : lip ? (draw < 0.6f ? 1 : 2) : (draw < 0.5f ? 0 : 1);
				float wide = Mathf.Clamp(height / CaveGlowMeshes.IceFallHeight, 0.6f, 1.4f) * size;
				float halfWidth = 0.5f * CaveGlowMeshes.IceFallWidths[variant] * wide;
				float apron = CaveGlowMeshes.IceFallAprons[variant] * wide;
				Vector3 cone = bottom - into * (0.5f * apron);
				if (!Free(cone, halfWidth + 0.5f) || feature(bottom, halfWidth) || feature(cone, apron) || way(bottom - into * apron, 0.3f) || way(top, 0f))
				{
					return false;
				}
				// The face must run the fall's width either side at half its height: no fall over a corner.
				Vector3 across = Vector3.Cross(Vector3.up, into).normalized;
				Vector3 mid = Vector3.Lerp(bottom, top, 0.5f) - into * 0.5f;
				foreach (float sign in new[] { -1f, 1f })
				{
					Vector3 side = mid + across * (sign * 0.85f * halfWidth);
					if (Rock(system, side) || !March(system, side, into, 1.6f, 0.1f, out _))
					{
						return false;
					}
				}
				Vector3 up = (top - bottom).normalized;
				Vector3 forward = into - up * Vector3.Dot(into, up);
				Add(CaveGlowKind.IceFall, HueAt(bottom), variant, bottom, Quaternion.LookRotation(forward.normalized, up), new Vector3(wide, height / CaveGlowMeshes.IceFallHeight, wide));
				layout.Falls++;
				taken.Add((cone, halfWidth + 0.3f));
				spots.Add((bottom - into * 0.6f, 6f));
				// A blue light before a tall one, spaced off every other cave light.
				if (height >= 3f && layout.Lights.Count < MaxLights)
				{
					Vector3 light = bottom - into * 1.4f + Vector3.up * Mathf.Min(0.4f * height, 2.5f);
					if (!Rock(system, light) && Spaced(light))
					{
						layout.Lights.Add(light);
					}
				}
				return true;
			}

			private bool Spaced(Vector3 p)
			{
				float min = LightSpacing * LightSpacing;
				if (earlier != null)
				{
					foreach (Vector3 q in earlier)
					{
						if ((q - p).sqrMagnitude < min)
						{
							return false;
						}
					}
				}
				foreach (Vector3 q in layout.Lights)
				{
					if ((q - p).sqrMagnitude < min)
					{
						return false;
					}
				}
				return true;
			}

			// ── Icicle curtains ──

			/// <summary>Icicles along the roof's steps in cold chambers and where a low passage's roof meets a high one.</summary>
			public void Curtains()
			{
				var rng = Stream(0x1C1Cu);
				var points = new List<Vector3>();
				foreach (CaveChamber chamber in system.Chambers)
				{
					float area = Mathf.PI * chamber.Radii.x * chamber.Radii.z;
					int tries = Mathf.Clamp(Mathf.RoundToInt(area / 45f), 2, 10);
					float cy = chamber.Yaw * Mathf.Deg2Rad;
					Vector3 along = Dir(cy), across = new Vector3(along.z, 0f, -along.x);
					for (int i = 0; i < tries; i++)
					{
						float a = rng.Range(0f, 2f * Mathf.PI), t = Mathf.Sqrt(rng.Range(0.05f, 0.85f));
						Vector3 p = chamber.Centre + along * (Mathf.Cos(a) * t * chamber.Radii.x) + across * (Mathf.Sin(a) * t * chamber.Radii.z);
						p.y = CaveSystem.FloorOf(chamber, p) + 1f;
						points.Add(p);
					}
				}
				foreach (CavePassage passage in system.Passages)
				{
					// The passage's ends, where it opens into a chamber under a higher roof, and along it every few metres in the cold,
					// where its roof steps as its bore changes.
					foreach (int i in new[] { 1, passage.Nodes.Count - 2 })
					{
						if (i >= 0 && i < passage.Nodes.Count)
						{
							CaveNode node = passage.Nodes[i];
							points.Add(new Vector3(node.Centre.x, node.Floor + 1f, node.Centre.z));
						}
					}
					float next = system.Mouths.Count > 0 ? 0.6f * system.Mouths[0].Zone : 0f;
					for (int i = 1; i + 1 < passage.Nodes.Count; i++)
					{
						CaveNode node = passage.Nodes[i];
						if (node.Along < next)
						{
							continue;
						}
						next = node.Along + rng.Range(6f, 12f);
						points.Add(new Vector3(node.Centre.x, node.Floor + 1f, node.Centre.z));
					}
				}
				foreach (Vector3 p in points)
				{
					float turn = rng.Range(0f, 2f * Mathf.PI), pick = rng.NextFloat(), size = rng.Range(0.75f, 1.2f);
					if (layout.Curtains >= MaxCurtains)
					{
						break;
					}
					if (!Frozen(p) || !FloorAndRoof(system, p, out float floor, out float roof) || roof - floor < 3f)
					{
						continue;
					}
					for (int k = 0; k < 6; k++)
					{
						Vector3 d = Dir(turn + k * Mathf.PI / 3f);
						if (Curtain(new Vector3(p.x, floor + 1f, p.z), d, roof, pick, size))
						{
							break;
						}
					}
				}
			}

			/// <summary>A curtain where the roof steps down from over a point toward a heading: along the step, hung from the lower roof.</summary>
			private bool Curtain(Vector3 p, Vector3 d, float roof, float pick, float size)
			{
				Vector3 q = p + d * StepReach;
				if (!FloorAndRoof(system, q, out _, out float lower) || roof - lower < RoofStep)
				{
					return false;
				}
				// The step's edge between the two, by halving.
				float a = 0f, b = StepReach, mid = 0.5f * (roof + lower);
				for (int i = 0; i < 6; i++)
				{
					float t = 0.5f * (a + b);
					if (!FloorAndRoof(system, p + d * t, out _, out float r) || r < mid)
					{
						b = t;
					}
					else
					{
						a = t;
					}
				}
				Vector3 edge = p + d * (b + 0.12f);
				if (!FloorAndRoof(system, edge, out float floorUnder, out float hang))
				{
					return false;
				}
				int variant = pick < 0.45f ? 0 : pick < 0.8f ? 1 : 2;
				float span = CaveGlowMeshes.IcicleSpans[variant] * size, longest = CaveGlowMeshes.IcicleDrops[variant] * size;
				Vector3 along = Vector3.Cross(Vector3.up, d).normalized;
				// Clearance under its middle and its ends: off the floor, and a person's headroom over a way.
				float drop = longest;
				foreach (float s in new[] { -0.45f, 0f, 0.45f })
				{
					Vector3 at = new Vector3(edge.x, floorUnder + 1f, edge.z) + along * (s * span);
					if (!FloorAndRoof(system, at, out float f, out float r) || feature(new Vector3(at.x, f, at.z), 0.3f))
					{
						return false;
					}
					float clear = r - f - (way(new Vector3(at.x, f, at.z), 0f) ? Headroom : 0.6f);
					// Less the drop hung at the longest one's tip.
					drop = Mathf.Min(drop, clear - 0.1f * size);
				}
				float scaleY = drop / CaveGlowMeshes.IcicleDrops[variant];
				Vector3 hangAt = new Vector3(edge.x, hang + 0.05f, edge.z);
				if (scaleY < 0.25f || !Free(hangAt, 0.5f * span))
				{
					return false;
				}
				Add(CaveGlowKind.IcicleCurtain, HueAt(hangAt), variant, hangAt, Quaternion.LookRotation(d, Vector3.up), new Vector3(size, scaleY, size));
				layout.Curtains++;
				taken.Add((hangAt, 0.5f * span));
				spots.Add((new Vector3(edge.x, floorUnder, edge.z), 5f));
				return true;
			}

			// ── Hoarfrost ──

			/// <summary>Hoarfrost on the walls round the ice laid, where a wall is flat enough under a patch.</summary>
			public void Frost()
			{
				var rng = Stream(0xF205u);
				// Moist air meeting the frozen rock frosts the walls of the cold passages too, not only round the ice.
				foreach (CavePassage passage in system.Passages)
				{
					float next = system.Mouths.Count > 0 ? 0.6f * system.Mouths[0].Zone : 0f;
					foreach (CaveNode node in passage.Nodes)
					{
						if (node.Along < next)
						{
							continue;
						}
						next = node.Along + rng.Range(10f, 18f);
						var p = new Vector3(node.Centre.x, node.Floor, node.Centre.z);
						if (Frozen(p))
						{
							spots.Add((p, node.Radius + 3f));
						}
					}
				}
				foreach ((Vector3 at, float reach) in spots)
				{
					for (int k = 0; k < 3; k++)
					{
						float turn = rng.Range(0f, 2f * Mathf.PI), rise = rng.Range(0.4f, 2.6f), pick = rng.NextFloat(), size = rng.Range(0.6f, 1.1f), spin = rng.Range(-12f, 12f);
						if (layout.Frost >= MaxFrost)
						{
							return;
						}
						Vector3 from = at + Vector3.up * rise;
						Vector3 dir = Dir(turn);
						if (!March(system, from, dir, reach, 0.2f, out float hit))
						{
							continue;
						}
						Vector3 surface = from + dir * hit;
						Vector3 n = Outward(system, surface);
						if (Mathf.Abs(n.y) > 0.5f || feature(surface, 0.5f))
						{
							continue;
						}
						int variant = pick < 0.5f ? 0 : pick < 0.75f ? 1 : 2;
						Vector2 patch = CaveGlowMeshes.HoarfrostPatches[variant] * size;
						Vector3 upw = (Vector3.up - n * Vector3.Dot(Vector3.up, n)).normalized;
						Quaternion turnIn = Quaternion.AngleAxis(spin, n);
						upw = turnIn * upw;
						Vector3 right = Vector3.Cross(upw, n).normalized;
						if (!FlatUnder(surface, n, right, upw, patch) || !Free(surface, 0.4f * Mathf.Max(patch.x, patch.y)))
						{
							continue;
						}
						Add(CaveGlowKind.Hoarfrost, 0, variant, surface, Quaternion.LookRotation(-n, upw), Vector3.one * size);
						layout.Frost++;
						taken.Add((surface, 0.3f * Mathf.Max(patch.x, patch.y)));
					}
				}
			}

			/// <summary>Whether the rock under a patch's corners lies within a few centimetres of its plane, so no crystal floats or is buried.</summary>
			private bool FlatUnder(Vector3 centre, Vector3 n, Vector3 right, Vector3 up, Vector2 patch)
			{
				foreach (Vector2 c in new[] { new Vector2(-0.4f, -0.4f), new Vector2(0.4f, -0.4f), new Vector2(-0.4f, 0.4f), new Vector2(0.4f, 0.4f) })
				{
					Vector3 corner = centre + right * (c.x * patch.x) + up * (c.y * patch.y);
					Vector3 from = corner + n * 0.2f;
					if (!March(system, from, -n, 0.32f, 0.02f, out float d) || Mathf.Abs(d - 0.2f) > 0.07f)
					{
						return false;
					}
				}
				return true;
			}
		}
	}
}
#endif
