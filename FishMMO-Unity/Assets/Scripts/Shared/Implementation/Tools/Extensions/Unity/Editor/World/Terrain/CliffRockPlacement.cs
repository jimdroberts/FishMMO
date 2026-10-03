#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The ground the planner reads: the surface height at a point, false off the terrain or over a hole.</summary>
	public interface ICliffRockGround
	{
		bool TryHeight(float x, float z, out float height);
	}

	/// <summary>What cliff stands at a point: its rock, the roundness level the climate there gives, and the cliff layer's angle.</summary>
	public struct CliffRockSite
	{
		/// <summary>A <see cref="CliffRocks.Types"/> name.</summary>
		public string Type;
		/// <summary>Index into <see cref="CliffRocks.RoundnessLevels"/> for climatic types; ignored otherwise.</summary>
		public int Roundness;
		/// <summary>The slope, degrees, from which the ground counts as cliff.</summary>
		public float MinAngle;
	}

	/// <summary>Answers which cliff stands at (x, z) with ground height y; false where no cliff rock may stand.</summary>
	public delegate bool CliffRockSiteAt(float x, float z, float y, out CliffRockSite site);

	/// <summary>Tuning for <see cref="CliffRockPlacement.Plan"/>; the defaults are the shipped behaviour.</summary>
	public sealed class CliffRockPlacementOptions
	{
		/// <summary>Cell of the planner's own height grid, metres.</summary>
		public float CellMetres = 2f;
		/// <summary>Share of the steep ground the gate keeps (gullies take the rest); columnar cliffs use <see cref="ColumnarCoverage"/>.</summary>
		public float Coverage = 0.85f;
		public float ColumnarCoverage = 0.78f;
		/// <summary>Lay a near-continuous footing of the biggest rocks along every foot line.</summary>
		public bool FootRow = true;
		/// <summary>Raise talus cones at the angle of repose (terrain edits) and scatter fall-sorted debris on them.</summary>
		public bool Cones = true;
		/// <summary>The angle of repose, degrees.</summary>
		public float ReposeDegrees = 35f;
		/// <summary>Hard ceiling on rocks per scene.</summary>
		public int MaxRocks = 6000;
	}

	/// <summary>One rock to put in the scene: the payload piece, a pose and a scale (rotation applied after scale, as a Transform does).</summary>
	public struct PlacedCliffRock
	{
		public CliffPiece Piece;
		public Vector3 Position;
		/// <summary>Rotation only (orthonormal columns).</summary>
		public Matrix4x4 Rotation;
		public Vector3 Scale;
		/// <summary>Detached debris on a talus cone (rests on its broadest face).</summary>
		public bool Talus;
		/// <summary>A footing rock (the base rule was applied).</summary>
		public bool Base;
		/// <summary>Relative height in its band where it was placed: 0 foot, 1 crest.</summary>
		public float BandT;
		/// <summary>Area share hidden under the ground or earlier rock.</summary>
		public float Hidden;
		/// <summary>Share of the seating mesh's vertices that are visible underside near the ground (base rule).</summary>
		public float Underside;
		/// <summary>The rock's longest extent as placed (built size × scale), metres.</summary>
		public float Length;
		/// <summary>The burial the size curve asked for, as a share of the thickness along the slope normal.</summary>
		public float Embed;
		/// <summary>The burial achieved: deepest point below the surface along the normal over the thickness.</summary>
		public float EmbedAchieved;

		public Vector3 World(Vector3 local) => Position + Rotation.MultiplyVector(new Vector3(local.x * Scale.x, local.y * Scale.y, local.z * Scale.z));
		public Vector3 Normal(Vector3 n) => Rotation.MultiplyVector(new Vector3(n.x / Scale.x, n.y / Scale.y, n.z / Scale.z)).normalized;
	}

	/// <summary>A talus cone raised into the ground.</summary>
	public struct CliffCone
	{
		public Vector3 Apex;
		public float Radius;
		public float ApexHeight;
		public Vector3 Down;
		public string Type;
	}

	/// <summary>What the planner did.</summary>
	public sealed class CliffRockStats
	{
		public int Attempts, Rejected, Dropped, Face, Talus, Cones, Footing;
		public float CliffLength, Covered;
		public readonly Dictionary<CliffRole, int> PerRole = new Dictionary<CliffRole, int>();
		public readonly float[] ThirdSum = new float[3], ThirdMax = new float[3];
		public readonly int[] ThirdCount = new int[3];
		public float UndersideSum;
		public double HiddenSum;

		/// <summary>Mean and max longest extent per third of the band, foot to crest.</summary>
		public string SizeByHeight()
		{
			var parts = new List<string>();
			for (int i = 0; i < 3; i++)
			{
				parts.Add(ThirdCount[i] == 0 ? "-" : $"{ThirdSum[i] / ThirdCount[i]:0} / {ThirdMax[i]:0} m (n{ThirdCount[i]})");
			}
			return string.Join("; ", parts);
		}

		public override string ToString()
		{
			return $"{Face} face rocks ({Footing} footing) + {Talus} talus on {Cones} cones over {CliffLength:0} m of cliff, steep ground under rock {Covered:P0}; " +
				$"size mean/max per third foot→crest {SizeByHeight()}; footing underside {(Footing > 0 ? UndersideSum / Footing : 0f):P1}";
		}
	}

	/// <summary>The planner's result: the rocks, the cones and the edited ground they were fitted to.</summary>
	public sealed class CliffRockPlan
	{
		public readonly List<PlacedCliffRock> Rocks = new List<PlacedCliffRock>();
		public readonly List<CliffCone> Cones = new List<CliffCone>();
		public readonly CliffRockStats Stats = new CliffRockStats();
		internal CliffGrid Grid;
		internal CliffTopRaster Top;

		/// <summary>Metres the cones raised the ground at a point (0 where none did).</summary>
		public float HeightDelta(float x, float z) => Grid != null ? Grid.DeltaAt(x, z) : 0f;
		/// <summary>0..1: how much a point is talus (scree), for the splat and the scatter clean-up.</summary>
		public float Debris(float x, float z) => Grid != null ? Grid.DebrisAt(x, z) : 0f;
		/// <summary>True where a placed rock stands more than <paramref name="clearance"/> above the ground: no tree or grass there.</summary>
		public bool UnderRock(float x, float z, float clearance = 0.3f) => Top != null && Grid != null && Top.At(x, z) > Grid.Height(x, z) + clearance;
	}

	/// <summary>
	/// Plans a cliff of large rock pieces on steep ground. Pure (no scene, no assets): the scene side
	/// (<see cref="CliffPlacer"/>) builds the ground and site callbacks and turns the plan into objects.
	/// </summary>
	/// <remarks>
	/// The rules, in order of application:
	/// <list type="number">
	/// <item><b>Cliff zone.</b> Steep cells (slope ≥ the site's cliff angle) whose gate — fall-line
	/// noise (constant along a fall line: buttresses and gullies) plus weaker clustering noise — is above
	/// the coverage quantile; and gentler cells within a noise-driven reach of the steep mask (spurs up
	/// to ~25 m below the foot, crest rocks up to ~12 m behind the top). Tried steepest first, weighted-random.</item>
	/// <item><b>Footing.</b> Along each foot line the biggest rocks (titans where the band is ≥ 16 m
	/// tall, at most a couple), overlapping 10–35 %, only ~8 % of the line left open by noise.</item>
	/// <item><b>Size by height.</b> Quotas are each role's share of the cliff length; base-class rocks
	/// only in the lowest ~35 % of the band (±5 %); above that the longest extent is capped at 65→40 %
	/// of the base size up to 60 % of the band, 40→25 % to the crest (±10 % per rock); mid and fill
	/// rocks must touch bigger ones and avoid the foot.</item>
	/// <item><b>Rotate, then settle.</b> Sink until 92 % of the lowest 18 % of the rotated rock is below
	/// the support (ground or earlier rock); then push INTO THE SLOPE along its normal until the deepest
	/// point is <see cref="EmbedFor"/> of the thickness along the normal below the surface.</item>
	/// <item><b>Base rule.</b> A footing rock's widest point toward the valley (horizontally and down the
	/// slope) goes 3 % of its height below the surface, plus 0–10 % noise; then 4 % deeper per step
	/// while more than 1.5 % of its vertices are visible underside in its lowest third.</item>
	/// <item><b>Overlap.</b> Equal-volume spheres: the share of a new rock inside its neighbours under a
	/// per-rock 2–12 %, and no rock may swallow 45 % of one already placed.</item>
	/// <item><b>Rotation by structure.</b> Jointed: squared to the face's joint set ±12° with sheet-joint
	/// lean, crest corestones any way up. Bedded: one regional dip 0–10° per cliff, ±3°, strike ±10°.
	/// Foliated: one steep foliation 45–80°, ±5°. Columnar: vertical ±4°. Ice: crevasse sets ±10°.
	/// Debris: of 24 poses the one with the centre of mass lowest over the slope, long axis down it.</item>
	/// <item><b>Talus cones.</b> One per ~45 m of cliff below gullies and the tallest face, radius
	/// 0.45 × band + 4 (7–20 m), apex at the angle of repose against the foot, stamped ray by ray and
	/// stopped where the ground falls away, edges feathered; debris fall-sorted on it (1.5 m high up
	/// to 6.5 m at the toe) and 8–12 m blocks just beyond, resting on the ground only.</item>
	/// <item><b>Roundness.</b> The site's level picks the baked joint-block variant (granite).</item>
	/// </list>
	/// </remarks>
	public static class CliffRockPlacement
	{
		// ── Burial and size curves ────────────────────────────────────

		private static float Pw(float x, (float x, float y)[] k)
		{
			if (x <= k[0].x) return k[0].y;
			for (int i = 1; i < k.Length; i++) if (x <= k[i].x) return k[i - 1].y + (k[i].y - k[i - 1].y) * (x - k[i - 1].x) / (k[i].x - k[i - 1].x);
			return k[k.Length - 1].y;
		}
		private static readonly (float, float)[] EmbedMid = { (0f, 0.08f), (10f, 0.13f), (12f, 0.24f), (20f, 0.32f), (25f, 0.44f), (45f, 0.50f), (60f, 0.52f) };
		private static readonly (float, float)[] EmbedLo = { (10f, 0.05f), (12f, 0.20f), (20f, 0.20f), (25f, 0.35f) };
		private static readonly (float, float)[] EmbedHi = { (10f, 0.20f), (12f, 0.40f), (20f, 0.40f), (25f, 0.60f) };

		/// <summary>
		/// Burial grows with size: the share of a rock's thickness along the slope normal set below the
		/// surface. ≤ 10 m: 5–20 %; 12–20 m: 20–40 %; ≥ 25 m: 35–60 %; interpolated between, ±0.07 noise
		/// (<paramref name="noise"/> −1…1), never outside the size's band.
		/// </summary>
		public static float EmbedFor(float longest, float noise) => Mathf.Clamp(Pw(longest, EmbedMid) + 0.07f * noise, Pw(longest, EmbedLo), Pw(longest, EmbedHi));

		/// <summary>
		/// Hard size-by-height cap on the longest extent at relative band height t: none to 35 %, then
		/// 65→40 % of the base size to 60 %, then 40→25 % to the crest, ±10 % per rock.
		/// </summary>
		public static float HeightCap(float t, float baseLength, float noise)
		{
			float f = t <= 0.35f ? 10f : t <= 0.6f ? Mathf.Lerp(0.65f, 0.4f, (t - 0.35f) / 0.25f) : Mathf.Lerp(0.4f, 0.25f, (t - 0.6f) / 0.4f);
			return f * baseLength * (1f + 0.1f * noise);
		}

		/// <summary>Per-axis scale range by structure: bedded and foliated stay within ±15 % so the strata texture keeps its density.</summary>
		public static Vector2 AnisoRange(CliffStructure s, int axis)
		{
			switch (s)
			{
				case CliffStructure.Bedded:
				case CliffStructure.Foliated: return new Vector2(0.87f, 1.15f);
				case CliffStructure.Columnar: return axis == 1 ? new Vector2(0.8f, 1.2f) : new Vector2(0.85f, 1.15f);
				default: return new Vector2(0.8f, 1.25f);
			}
		}

		// ── Piece metrics ─────────────────────────────────────────────

		/// <summary>A piece's seating mesh (its collider level) and the measures the planner needs.</summary>
		internal sealed class Metrics
		{
			public CliffPiece Piece;
			public MeshBuilder Mesh;
			public float ExtX, ExtZ, Height, MinY, AlignYaw, Length;
			public Vector3 Com;
		}

		internal sealed class MetricsCache
		{
			private readonly Dictionary<CliffPiece, Metrics> cache = new Dictionary<CliffPiece, Metrics>();
			private readonly Func<CliffPiece, MeshBuilder> build;
			public MetricsCache(Func<CliffPiece, MeshBuilder> build) { this.build = build; }

			public Metrics Of(in CliffPiece piece)
			{
				if (cache.TryGetValue(piece, out Metrics m))
				{
					return m;
				}
				MeshBuilder mesh = build(piece);
				m = new Metrics { Piece = piece, Mesh = mesh };
				// Minimum-area footprint rectangle over 2-degree steps; its long side becomes local x.
				float best = float.MaxValue;
				for (int a = 0; a < 180; a += 2)
				{
					float r = a * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
					float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
					foreach (Vector3 p in mesh.Positions)
					{
						float x = c * p.x + s * p.z, z = -s * p.x + c * p.z;
						x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); z0 = Mathf.Min(z0, z); z1 = Mathf.Max(z1, z);
					}
					float area = (x1 - x0) * (z1 - z0);
					if (area < best)
					{
						best = area;
						m.AlignYaw = a;
						m.ExtX = x1 - x0;
						m.ExtZ = z1 - z0;
					}
				}
				if (m.ExtZ > m.ExtX)
				{
					m.AlignYaw += 90f;
					(m.ExtX, m.ExtZ) = (m.ExtZ, m.ExtX);
				}
				Bounds b = mesh.Bounds;
				m.Height = b.size.y;
				m.MinY = b.min.y;
				m.Length = Mathf.Max(m.ExtX, Mathf.Max(m.ExtZ, m.Height));
				Vector3 com = Vector3.zero;
				foreach (Vector3 p in mesh.Positions) com += p;
				m.Com = com / Mathf.Max(1, mesh.VertexCount);
				cache[piece] = m;
				return m;
			}
		}

		// ── Rotation helpers ──────────────────────────────────────────

		/// <summary>A rotation by the right-hand rule about an axis.</summary>
		public static Matrix4x4 Rot(Vector3 axis, float degrees)
		{
			axis.Normalize();
			float a = degrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a), t = 1f - c, x = axis.x, y = axis.y, z = axis.z;
			Matrix4x4 m = Matrix4x4.identity;
			m.m00 = t * x * x + c; m.m01 = t * x * y - s * z; m.m02 = t * x * z + s * y;
			m.m10 = t * x * y + s * z; m.m11 = t * y * y + c; m.m12 = t * y * z - s * x;
			m.m20 = t * x * z - s * y; m.m21 = t * y * z + s * x; m.m22 = t * z * z + c;
			return m;
		}

		/// <summary>Columns strike (x), up, downhill (z).</summary>
		public static Matrix4x4 Frame(Vector3 down)
		{
			var s = new Vector3(down.z, 0f, -down.x);
			Matrix4x4 m = Matrix4x4.identity;
			m.m00 = s.x; m.m10 = s.y; m.m20 = s.z;
			m.m01 = 0f; m.m11 = 1f; m.m21 = 0f;
			m.m02 = down.x; m.m12 = down.y; m.m22 = down.z;
			return m;
		}

		private static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
		private static float Flat(Vector3 v) => Mathf.Sqrt(v.x * v.x + v.z * v.z);

		// ── The plan ──────────────────────────────────────────────────

		private sealed class Candidate
		{
			public Metrics M;
			public Vector3 At, Down, S;
			public float Slope, Yaw, Pitch, Roll, EmbedNoise, BaseNoise, ExtraEmbed, Band = 0.18f;
			public bool Base, TerrainOnly;
			public Matrix4x4? R;
			public float[] Leans = { 0f };
		}

		private sealed class Run
		{
			public CliffGrid G;
			public CliffTopRaster Top;
			public MetricsCache Metrics;
			public DeterministicRNG Rng;
			public CliffRockPlacementOptions O;
			public CliffRockPlan Plan;
			public Matrix4x4 Bedding, Foliation;
		}

		/// <summary>
		/// Plans the cliff rocks of an area.
		/// </summary>
		/// <param name="ground">The surface.</param>
		/// <param name="area">The rectangle to plan over (x, z).</param>
		/// <param name="siteAt">Which cliff stands where.</param>
		/// <param name="seed">The scene's seed.</param>
		/// <param name="seatingMesh">A piece's collider-level mesh (<see cref="CliffRocks.Build"/> at <see cref="CliffRocks.CollisionLod"/>).</param>
		/// <param name="options">Null uses the defaults.</param>
		public static CliffRockPlan Plan(ICliffRockGround ground, Rect area, CliffRockSiteAt siteAt, uint seed, Func<CliffPiece, MeshBuilder> seatingMesh, CliffRockPlacementOptions options = null)
		{
			options ??= new CliffRockPlacementOptions();
			var plan = new CliffRockPlan();
			var g = new CliffGrid(ground, area, Mathf.Max(0.5f, options.CellMetres));
			plan.Grid = g;
			if (!g.AnalyseSites(siteAt, options.Coverage, options.ColumnarCoverage))
			{
				return plan;
			}
			var run = new Run
			{
				G = g,
				Top = new CliffTopRaster(0.5f),
				Metrics = new MetricsCache(seatingMesh),
				Rng = new DeterministicRNG((int)(seed ^ 0x51ff1u)),
				O = options,
				Plan = plan,
			};
			plan.Top = run.Top;
			// One bedding plane and one foliation per cliff (the scene's): beds line up across every rock.
			float az = run.Rng.NextFloat() * 6.2831853f;
			run.Bedding = Rot(new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az)), run.Rng.Range(0f, 10f));
			float faz = run.Rng.NextFloat() * 6.2831853f;
			run.Foliation = Rot(new Vector3(Mathf.Cos(faz), 0f, Mathf.Sin(faz)), run.Rng.Range(45f, 80f));

			plan.Stats.CliffLength = g.CliffLength;
			var zone = Zone(run);
			if (options.FootRow)
			{
				FootRow(run);
			}
			foreach (CliffRole role in new[] { CliffRole.Titan, CliffRole.Base, CliffRole.Mid, CliffRole.Fill, CliffRole.Crest })
			{
				if (role == CliffRole.Titan && options.FootRow)
				{
					continue; // Titans go into the footing.
				}
				Fill(run, zone, role);
			}
			plan.Stats.Face = plan.Rocks.Count;
			if (options.Cones)
			{
				Cones(run);
			}
			// Coverage of the steep ground.
			int steep = 0, covered = 0;
			for (int k = 0; k < g.Count; k++)
			{
				if (!g.IsSteep(k))
				{
					continue;
				}
				g.Position(k, out float x, out float z);
				steep++;
				if (run.Top.At(x, z) > g.H[k] + 0.2f)
				{
					covered++;
				}
			}
			plan.Stats.Covered = steep == 0 ? 0f : covered / (float)steep;
			foreach (PlacedCliffRock r in plan.Rocks)
			{
				if (r.Talus)
				{
					continue;
				}
				float longest = r.Length;
				int third = Mathf.Min(2, (int)(r.BandT * 3f));
				plan.Stats.ThirdSum[third] += longest;
				plan.Stats.ThirdCount[third]++;
				plan.Stats.ThirdMax[third] = Mathf.Max(plan.Stats.ThirdMax[third], longest);
				plan.Stats.HiddenSum += r.Hidden;
				if (r.Base)
				{
					plan.Stats.UndersideSum += r.Underside;
				}
			}
			return plan;
		}

		private static float Longest(Metrics m, Vector3 s) => Mathf.Max(m.ExtX * Mathf.Max(s.x, s.z), Mathf.Max(m.ExtZ * Mathf.Min(s.x, s.z), m.Height * s.y));

		/// <summary>The class covers by role: each role's share of the cliff length.</summary>
		private static float Cover(CliffStructure s, CliffRole role, bool footRow)
		{
			switch (role)
			{
				case CliffRole.Base: return footRow ? 0.4f : 0.8f;
				case CliffRole.Mid: return 0.5f;
				case CliffRole.Fill: return s == CliffStructure.Columnar ? 0.3f : 0.4f;
				case CliffRole.Crest: return s == CliffStructure.Columnar ? 0f : s == CliffStructure.Jointed ? 0.5f : 0.4f;
				default: return 0.2f;
			}
		}

		private static float BaseLength(string type) => CliffRocks.ShapesOf(type, CliffRole.Base)[0].Length;

		/// <summary>A piece of a role for a site: a weighted shape, a variant, the site's roundness.</summary>
		private static CliffPiece PickPiece(Run run, in CliffRockSite site, CliffRole role)
		{
			string type = site.Type;
			CliffRole r = role;
			// Crest rocks of jointed rock are corestones only where it weathers round; else small joint blocks.
			if (role == CliffRole.Crest && CliffRocks.StructureOf(type) == CliffStructure.Jointed && CliffRocks.Climatic(type) && site.Roundness < 1)
			{
				r = CliffRole.Fill;
			}
			CliffShape[] shapes = CliffRocks.ShapesOf(type, r);
			float sum = 0f;
			foreach (CliffShape s in shapes) sum += s.Weight;
			float u = run.Rng.NextFloat() * sum;
			int kind = shapes.Length - 1;
			for (int i = 0; i < shapes.Length; i++)
			{
				u -= shapes[i].Weight;
				if (u <= 0f)
				{
					kind = i;
					break;
				}
			}
			int roundness = CliffRocks.HasRoundness(type, r) ? Mathf.Clamp(site.Roundness, 0, CliffRocks.RoundnessLevels.Length - 1) : -1;
			return new CliffPiece(type, r, kind, roundness, run.Rng.Next(shapes[kind].Variants));
		}

		private static Vector3 Aniso(Run run, CliffStructure s)
		{
			Vector2 x = AnisoRange(s, 0), y = AnisoRange(s, 1);
			return new Vector3(run.Rng.Range(x.x, x.y), run.Rng.Range(y.x, y.y), run.Rng.Range(x.x, x.y));
		}

		// ── Zone ──────────────────────────────────────────────────────

		private struct ZonePoint
		{
			public Vector3 P;
			public float Key;
			public int Where; // 1 face, 2 crest side, 3 foot side
			public int Cell;
		}

		private static List<ZonePoint> Zone(Run run)
		{
			CliffGrid g = run.G;
			var list = new List<ZonePoint>();
			for (int k = 0; k < g.Count; k++)
			{
				if (g.Site[k] < 0 || !g.InZone[k])
				{
					continue;
				}
				g.Position(k, out float x, out float z);
				if (!g.Inside(x, z, 4f))
				{
					continue;
				}
				CliffRockSite site = g.Sites[g.Site[k]];
				float w;
				int where = 1;
				if (g.IsSteep(k))
				{
					w = 0.35f + 0.65f * Smooth(site.MinAngle, site.MinAngle + 22f, g.Slope[k]);
				}
				else
				{
					where = g.H[k] > g.NearestSteepH[k] ? 2 : 3;
					w = 0.25f * (1f - g.SteepDist[k] / Mathf.Max(1f, g.Reach[k]));
				}
				w *= 0.6f + 0.4f * g.GateStrength[k];
				float jx = (run.Rng.NextFloat() - 0.5f) * g.S, jz = (run.Rng.NextFloat() - 0.5f) * g.S;
				var p = new Vector3(x + jx, 0f, z + jz);
				p.y = g.Height(p.x, p.z);
				list.Add(new ZonePoint { P = p, Key = Mathf.Pow(Mathf.Max(1e-6f, run.Rng.NextFloat()), 1f / Mathf.Max(1e-3f, w)), Where = where, Cell = k });
			}
			list.Sort((a, b) => b.Key.CompareTo(a.Key));
			return list;
		}

		// ── Footing ───────────────────────────────────────────────────

		private static void FootRow(Run run)
		{
			CliffGrid g = run.G;
			var titans = new Dictionary<string, int>();
			foreach (List<Vector3> line in g.FootLines())
			{
				var arc = new float[line.Count];
				for (int i = 1; i < line.Count; i++)
				{
					arc[i] = arc[i - 1] + Mathf.Min(6f, Flat(line[i] - line[i - 1]));
				}
				float a = run.Rng.Range(0f, 8f);
				while (a < arc[arc.Length - 1] && run.Plan.Rocks.Count < run.O.MaxRocks)
				{
					int k = 1;
					while (k < line.Count - 1 && arc[k] < a) k++;
					Vector3 p = Vector3.Lerp(line[k - 1], line[k], Mathf.Clamp01((a - arc[k - 1]) / Mathf.Max(1e-3f, arc[k] - arc[k - 1])));
					if (!g.SiteNear(p.x, p.z, out CliffRockSite site))
					{
						a += 4f;
						continue;
					}
					float band = g.BandHeight(p.x, p.z);
					titans.TryGetValue(site.Type, out int nt);
					bool asTitan = nt < Mathf.Max(1, (int)(g.CliffLengthOf(site.Type) / 250f)) && band >= 16f && run.Rng.NextFloat() < 0.12f;
					CliffPiece piece = PickPiece(run, in site, asTitan ? CliffRole.Titan : CliffRole.Base);
					Metrics m = run.Metrics.Of(piece);
					CliffStructure structure = CliffRocks.StructureOf(site.Type);
					Vector3 S = Aniso(run, structure);
					float along = 0.5f * (m.ExtX * S.x + m.ExtZ * S.z);
					float cap = 8f + (structure == CliffStructure.Columnar ? 2.2f : 1.6f) * band;
					float longest = Longest(m, S);
					if (longest > cap)
					{
						S *= Mathf.Max(0.55f, cap / longest);
						along = 0.5f * (m.ExtX * S.x + m.ExtZ * S.z);
					}
					float gate = ProceduralNoise.Fbm3(new Vector3(p.x / 30f, 9.5f, p.z / 30f), 2, 0.5f, g.Seed + 29);
					if (gate < -0.45f)
					{
						a += along * 0.7f;
						continue;
					}
					Vector3 down = g.DownhillSmooth(p.x, p.z);
					// Level beds cannot lean into the slope, so a bedded footing slab sits further out on the foot, its back against the face.
					float outward = structure == CliffStructure.Bedded || structure == CliffStructure.Foliated ? 0.3f : 0.05f;
					Vector3 at = p + down * (outward * m.ExtZ * S.z + run.Rng.Range(-0.12f, 0.12f) * longest);
					at.y = g.Height(at.x, at.z);
					var c = NewCandidate(run, m, at, S, structure, true);
					c.R = InSitu(run, structure, m, at, c.Slope);
					run.Plan.Stats.Attempts++;
					if (Fit(run, c, 0.97f, out PlacedCliffRock rock) && Accept(run, rock, run.Rng.Range(0.1f, 0.3f)))
					{
						rock.BandT = 0f;
						rock.Base = true;
						Add(run, rock, piece.Role);
						run.Plan.Stats.Footing++;
						if (asTitan) titans[site.Type] = nt + 1;
						a += along * (1f - run.Rng.Range(0.1f, 0.35f));
						continue;
					}
					a += along * 0.35f;
				}
			}
		}

		// ── The classes ───────────────────────────────────────────────

		private static void Fill(Run run, List<ZonePoint> zone, CliffRole role)
		{
			CliffGrid g = run.G;
			var targets = new Dictionary<string, int>(StringComparer.Ordinal);
			var counts = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (string type in g.TypesPresent)
			{
				CliffStructure s = CliffRocks.StructureOf(type);
				float cover = Cover(s, role, run.O.FootRow);
				CliffShape[] shapes = CliffRocks.ShapesOf(type, role);
				float along = 0f;
				foreach (CliffShape sh in shapes)
				{
					along += 0.5f * (sh.Length + sh.Length * Mathf.Min(1f, sh.HeightOverWidth <= 1f ? 0.75f : 1f / sh.HeightOverWidth));
				}
				along /= shapes.Length;
				int target = Mathf.RoundToInt(cover * g.CliffLengthOf(type) / Mathf.Max(1f, along));
				if (role == CliffRole.Titan)
				{
					target = Mathf.Min(target, Mathf.Max(1, (int)(g.CliffLengthOf(type) / 250f)));
				}
				targets[type] = target;
				counts[type] = 0;
			}
			float nominal = 0f;
			foreach (ZonePoint zp in zone)
			{
				if (run.Plan.Rocks.Count >= run.O.MaxRocks)
				{
					break;
				}
				int si = g.Site[zp.Cell];
				if (si < 0)
				{
					continue;
				}
				CliffRockSite site = g.Sites[si];
				if (counts[site.Type] >= targets[site.Type])
				{
					continue;
				}
				CliffStructure structure = CliffRocks.StructureOf(site.Type);
				if (role == CliffRole.Crest ? zp.Where != 2 : zp.Where == 2)
				{
					continue;
				}
				Vector3 p = zp.P;
				float band = g.BandHeight(p.x, p.z);
				if (role == CliffRole.Titan && band < 16f)
				{
					continue;
				}
				nominal = CliffRocks.ShapesOf(site.Type, role)[0].Length;
				float baseL = BaseLength(site.Type);
				float t = g.BandT(p.x, p.z);
				float hn = run.Rng.Range(-1f, 1f);
				if (role != CliffRole.Crest)
				{
					// Hard: the biggest classes only low on the band.
					if ((role == CliffRole.Base || role == CliffRole.Titan) && t > 0.35f + 0.05f * hn) continue;
					if (nominal * 0.75f > HeightCap(t, baseL, hn)) continue;
					float mu = Mathf.Clamp(0.95f - 0.022f * nominal, 0.1f, 0.85f);
					float dz = (t - mu) / 0.28f;
					if (run.Rng.NextFloat() > Mathf.Exp(-0.5f * dz * dz)) continue;
					// The foot belongs to the big rocks: smaller ones only as a rare exception there.
					if (run.O.FootRow && t < 0.2f && nominal < 20f && run.Rng.NextFloat() < 0.85f) continue;
				}
				// Deep inside an already placed big rock's footprint.
				bool inside = false, near = role != CliffRole.Mid && role != CliffRole.Fill;
				foreach (PlacedCliffRock q in run.Plan.Rocks)
				{
					Metrics qm = run.Metrics.Of(q.Piece);
					float ql = Longest(qm, q.Scale), qr = Radius(qm, q.Scale), d = Flat(q.Position - p);
					if (d < 0.55f * qr && ql >= nominal * 0.6f) { inside = true; break; }
					if (!near && ql > nominal * 1.3f && d < qr * 1.15f + 0.5f * nominal) near = true;
				}
				if (inside || !near)
				{
					continue;
				}
				run.Plan.Stats.Attempts++;
				CliffPiece piece = PickPiece(run, in site, role);
				Metrics m = run.Metrics.Of(piece);
				Vector3 S = Aniso(run, structure);
				float longest = Longest(m, S);
				float cap = 8f + (structure == CliffStructure.Columnar ? 2.2f : 1.6f) * band;
				if (role != CliffRole.Crest)
				{
					cap = Mathf.Min(cap, HeightCap(t, baseL, hn));
				}
				if (longest > cap)
				{
					float f = cap / longest;
					if (f < 0.6f)
					{
						run.Plan.Stats.Rejected++;
						continue;
					}
					S *= f;
				}
				var c = NewCandidate(run, m, p, S, structure, t < 0.25f && role != CliffRole.Crest);
				c.ExtraEmbed = role == CliffRole.Crest ? 0.15f : 0f;
				bool freeCrest = role == CliffRole.Crest && (structure == CliffStructure.Jointed || structure == CliffStructure.Ice);
				c.R = freeCrest ? (Matrix4x4?)null : InSitu(run, structure, m, p, c.Slope);
				if (freeCrest)
				{
					float tilt = 8f;
					c.Yaw = run.Rng.NextFloat() * 360f;
					c.Pitch = run.Rng.Range(-tilt, tilt);
					c.Roll = run.Rng.Range(-tilt, tilt);
				}
				if (!Fit(run, c, 0.85f, out PlacedCliffRock rock))
				{
					run.Plan.Stats.Dropped++;
					continue;
				}
				if (!Accept(run, rock, run.Rng.Range(0.02f, 0.12f)))
				{
					run.Plan.Stats.Rejected++;
					continue;
				}
				rock.BandT = role == CliffRole.Crest ? 1f : t;
				rock.Base = c.Base;
				Add(run, rock, role);
				counts[site.Type]++;
			}
		}

		private static float Radius(Metrics m, Vector3 s) => 0.5f * Mathf.Pow(Mathf.Max(1e-3f, m.ExtX * s.x * m.ExtZ * s.z * m.Height * s.y), 1f / 3f);
		private static Vector3 Centre(in PlacedCliffRock r, Metrics m) => r.World(new Vector3(0f, m.MinY + m.Height * 0.5f, 0f));

		private static void Add(Run run, PlacedCliffRock rock, CliffRole role)
		{
			run.Plan.Rocks.Add(rock);
			run.Plan.Stats.PerRole[role] = (run.Plan.Stats.PerRole.TryGetValue(role, out int n) ? n : 0) + 1;
			Stamp(run, rock);
		}

		/// <summary>Volume overlap with every placed face rock (equal-volume spheres) against an allowance; never swallowing one.</summary>
		private static bool Accept(Run run, PlacedCliffRock rock, float allow)
		{
			Metrics m = run.Metrics.Of(rock.Piece);
			Vector3 ce = Centre(in rock, m);
			float rr = Radius(m, rock.Scale), sum = 0f;
			foreach (PlacedCliffRock q in run.Plan.Rocks)
			{
				if (q.Talus)
				{
					continue;
				}
				Metrics qm = run.Metrics.Of(q.Piece);
				float rq = Radius(qm, q.Scale), d = (Centre(in q, qm) - ce).magnitude;
				if (d >= rr + rq)
				{
					continue;
				}
				float lens = Lens(rr, rq, d);
				sum += lens / (4.18879f * rr * rr * rr);
				if (lens / (4.18879f * rq * rq * rq) > 0.45f)
				{
					return false;
				}
			}
			return sum <= allow;
		}

		/// <summary>Volume of the lens where spheres of radii a and b at distance d meet.</summary>
		public static float Lens(float a, float b, float d)
		{
			if (d >= a + b) return 0f;
			if (d <= Mathf.Abs(a - b)) { float m = Mathf.Min(a, b); return 4.18879f * m * m * m; }
			float t = a + b - d;
			return Mathf.PI * t * t * (d * d + 2f * d * (a + b) - 3f * (a - b) * (a - b)) / (12f * d);
		}

		private static Candidate NewCandidate(Run run, Metrics m, Vector3 at, Vector3 s, CliffStructure structure, bool isBase)
		{
			return new Candidate
			{
				M = m,
				At = at,
				Down = run.G.Downhill(at.x, at.z),
				Slope = run.G.SlopeAt(at.x, at.z),
				S = s,
				EmbedNoise = run.Rng.Range(-1f, 1f),
				BaseNoise = run.Rng.NextFloat(),
				Base = isBase,
			};
		}

		// ── Orientation ───────────────────────────────────────────────

		/// <summary>The in-situ pose by structure (see the class remarks).</summary>
		private static Matrix4x4 InSitu(Run run, CliffStructure structure, Metrics m, Vector3 p, float slope)
		{
			Vector3 down = run.G.DownhillSmooth(p.x, p.z);
			DeterministicRNG rng = run.Rng;
			Matrix4x4 align = Rot(Vector3.up, -m.AlignYaw);
			switch (structure)
			{
				case CliffStructure.Bedded:
					return run.Bedding * Frame(down) * Rot(Vector3.forward, rng.Range(-3f, 3f)) * Rot(Vector3.right, rng.Range(-3f, 3f))
						* Rot(Vector3.up, rng.Range(-10f, 10f) + (rng.NextFloat() < 0.5f ? 0f : 180f)) * align;
				case CliffStructure.Foliated:
					return run.Foliation * Frame(down) * Rot(Vector3.forward, rng.Range(-5f, 5f)) * Rot(Vector3.right, rng.Range(-5f, 5f))
						* Rot(Vector3.up, rng.Range(-10f, 10f) + (rng.NextFloat() < 0.5f ? 0f : 180f)) * align;
				case CliffStructure.Columnar:
				{
					float a = rng.NextFloat() * 6.2831853f;
					return Rot(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), rng.Range(0f, 4f)) * Rot(Vector3.up, rng.NextFloat() * 360f);
				}
				case CliffStructure.Ice:
					return Frame(down) * Rot(Vector3.forward, rng.Range(-6f, 6f)) * Rot(Vector3.right, rng.Range(-6f, 6f))
						* Rot(Vector3.up, rng.Range(-10f, 10f) + (rng.NextFloat() < 0.5f ? 0f : 180f)) * align;
				default:
				{
					float j = 12f, lean = new[] { 0.2f, 0.4f, 0.6f }[rng.Next(3)];
					return Frame(down) * Rot(Vector3.right, -lean * slope) * Rot(Vector3.forward, rng.Range(-j, j) * 0.6f) * Rot(Vector3.right, rng.Range(-j, j) * 0.6f)
						* Rot(Vector3.up, rng.Range(-j, j) + (rng.NextFloat() < 0.5f ? 0f : 180f)) * align;
				}
			}
		}

		/// <summary>
		/// A detached rock's resting pose: of 24 random orientations, the one whose centre of mass sits
		/// lowest over the local slope plane (on its broadest face — the hull's lowest-potential pose is
		/// the stable one), turned about the slope normal so its long axis runs down the fall line ±20°.
		/// </summary>
		public static Matrix4x4 StablePose(MeshBuilder mesh, Vector3 com, Vector3 s, Vector3 down, float slopeDegrees, DeterministicRNG rng)
		{
			float sl = slopeDegrees * Mathf.Deg2Rad;
			Vector3 n = (Vector3.up * Mathf.Cos(sl) - down * Mathf.Sin(sl)).normalized;
			Vector3 cs = new Vector3(com.x * s.x, com.y * s.y, com.z * s.z);
			Matrix4x4 best = Matrix4x4.identity;
			float bestH = float.MaxValue;
			for (int k = 0; k < 24; k++)
			{
				float y = rng.Range(-1f, 1f), a = rng.NextFloat() * 6.2831853f, h = Mathf.Sqrt(1f - y * y);
				Matrix4x4 q = Rot(new Vector3(h * Mathf.Cos(a), y, h * Mathf.Sin(a)), rng.NextFloat() * 360f);
				float minD = float.MaxValue;
				foreach (Vector3 v in mesh.Positions)
				{
					minD = Mathf.Min(minD, Vector3.Dot(q.MultiplyVector(new Vector3(v.x * s.x, v.y * s.y, v.z * s.z)), n));
				}
				float hc = Vector3.Dot(q.MultiplyVector(cs), n) - minD;
				if (hc < bestH)
				{
					bestH = hc;
					best = q;
				}
			}
			Vector3 u = Vector3.Cross(n, down).normalized, w = Vector3.Cross(u, n);
			double sxx = 0, sxy = 0, syy = 0;
			foreach (Vector3 v in mesh.Positions)
			{
				Vector3 p = best.MultiplyVector(new Vector3(v.x * s.x, v.y * s.y, v.z * s.z) - cs);
				float px = Vector3.Dot(p, u), py = Vector3.Dot(p, w);
				sxx += px * px; sxy += px * py; syy += py * py;
			}
			float axis = 0.5f * Mathf.Atan2((float)(2 * sxy), (float)(sxx - syy)) * Mathf.Rad2Deg;
			float turn = 90f - axis + rng.Range(-20f, 20f);
			return Rot(n, -turn) * best;
		}

		/// <summary>Height of a pose's centre of mass over the slope plane through its lowest point: what <see cref="StablePose"/> minimises.</summary>
		public static float ComHeight(MeshBuilder mesh, Vector3 com, Vector3 s, Matrix4x4 r, Vector3 n)
		{
			float minD = float.MaxValue;
			foreach (Vector3 v in mesh.Positions)
			{
				minD = Mathf.Min(minD, Vector3.Dot(r.MultiplyVector(new Vector3(v.x * s.x, v.y * s.y, v.z * s.z)), n));
			}
			return Vector3.Dot(r.MultiplyVector(new Vector3(com.x * s.x, com.y * s.y, com.z * s.z)), n) - minD;
		}

		// ── Seating ───────────────────────────────────────────────────

		private static float Support(Run run, float x, float z, bool terrainOnly)
		{
			float h = run.G.Height(x, z);
			return terrainOnly ? h : Mathf.Max(h, run.Top.At(x, z));
		}

		/// <summary>Rotate, then sink (no floating), then bury by size along the normal, then the base rule.</summary>
		private static bool Fit(Run run, Candidate c, float drop, out PlacedCliffRock best)
		{
			best = default;
			bool any = false;
			float bestHidden = float.MaxValue;
			MeshBuilder m = c.M.Mesh;
			var w = new Vector3[m.VertexCount];
			Vector3 n = run.G.NormalSmooth(c.At.x, c.At.z);
			foreach (float lean in c.R.HasValue ? new[] { 0f } : c.Leans)
			{
				Matrix4x4 r = c.R ?? Frame(c.Down) * Rot(Vector3.right, -lean * c.Slope) * Rot(Vector3.forward, c.Roll) * Rot(Vector3.right, -c.Pitch) * Rot(Vector3.up, c.Yaw);
				var rock = new PlacedCliffRock { Piece = c.M.Piece, Rotation = r, Scale = c.S, Position = new Vector3(c.At.x, 0f, c.At.z), Talus = c.TerrainOnly, Length = Longest(c.M, c.S) };
				float lo = float.MaxValue, hi = float.MinValue;
				for (int i = 0; i < w.Length; i++)
				{
					w[i] = rock.World(m.Positions[i]);
					lo = Mathf.Min(lo, w[i].y);
					hi = Mathf.Max(hi, w[i].y);
				}
				float band = lo + c.Band * (hi - lo);
				var gaps = new List<float>();
				for (int i = 0; i < w.Length; i++)
				{
					if (w[i].y <= band)
					{
						gaps.Add(w[i].y - Support(run, w[i].x, w[i].z, c.TerrainOnly));
					}
				}
				gaps.Sort();
				float sink = gaps.Count == 0 ? 0f : gaps[Mathf.Min(gaps.Count - 1, (int)(0.92f * gaps.Count))];
				rock.Position.y = -sink - 0.1f;
				// Size-graded burial along the slope normal.
				float tMin = float.MaxValue, tMax = float.MinValue, deepest = float.MinValue;
				for (int i = 0; i < w.Length; i++)
				{
					Vector3 v = w[i];
					v.y += rock.Position.y;
					float t = Vector3.Dot(v, n);
					tMin = Mathf.Min(tMin, t);
					tMax = Mathf.Max(tMax, t);
					deepest = Mathf.Max(deepest, (Support(run, v.x, v.z, c.TerrainOnly) - v.y) * n.y);
				}
				float thick = Mathf.Max(1e-3f, tMax - tMin);
				rock.Embed = Mathf.Min(0.85f, EmbedFor(Longest(c.M, c.S), c.EmbedNoise) + c.ExtraEmbed);
				float want = rock.Embed * thick;
				if (deepest < want)
				{
					rock.Position -= n * (want - deepest);
					deepest = want;
				}
				if (c.Base)
				{
					rock.Underside = BaseRule(run, m, ref rock, n, c.Down, c.Slope, c.BaseNoise, hi - lo, out float extra);
					deepest += extra;
				}
				rock.EmbedAchieved = deepest / thick;
				rock.Hidden = Hidden(run, m, in rock, c.TerrainOnly);
				if (rock.Hidden <= drop && rock.Hidden < bestHidden)
				{
					bestHidden = rock.Hidden;
					best = rock;
					any = true;
				}
			}
			return any;
		}

		/// <summary>
		/// The base rule: a footing rock grows out of the ground. Its widest point toward the valley — the
		/// vertex furthest down the fall line horizontally, and the one furthest down the slope's tangent —
		/// goes 3 % of its height below the surface along the normal, plus 0–10 % of noise (never
		/// shallower); then 4 % of its height deeper per step (at most 10) while more than 1.5 % of its
		/// vertices are visible underside — outward normal pointing down — in its lowest third above the
		/// ground. Returns that share.
		/// </summary>
		private static float BaseRule(Run run, MeshBuilder m, ref PlacedCliffRock rock, Vector3 n, Vector3 down, float slopeDegrees, float noise, float height, out float extra)
		{
			extra = 0f;
			float sl = slopeDegrees * Mathf.Deg2Rad;
			Vector3 tangent = (down * Mathf.Cos(sl) - Vector3.up * Mathf.Sin(sl)).normalized;
			for (int it = 0; it < 2; it++)
			{
				int iH = 0, iT = 0;
				float bH = float.MinValue, bT = float.MinValue;
				for (int i = 0; i < m.VertexCount; i++)
				{
					Vector3 v = rock.World(m.Positions[i]);
					float dh = Vector3.Dot(v, down), dt = Vector3.Dot(v, tangent);
					if (dh > bH) { bH = dh; iH = i; }
					if (dt > bT) { bT = dt; iT = i; }
				}
				float need = 0f;
				foreach (int i in new[] { iH, iT })
				{
					Vector3 v = rock.World(m.Positions[i]);
					float depth = (Support(run, v.x, v.z, false) - v.y) * n.y;
					need = Mathf.Max(need, 0.03f * height - depth);
				}
				if (it == 0)
				{
					need += noise * 0.10f * height;
				}
				if (need > 0f)
				{
					rock.Position -= n * need;
					extra += need;
				}
			}
			float under = 0f;
			for (int step = 0; step <= 10; step++)
			{
				under = Underside(run, m, in rock, height);
				if (under <= 0.015f || step == 10)
				{
					break;
				}
				rock.Position -= n * (0.04f * height);
				extra += 0.04f * height;
			}
			return under;
		}

		/// <summary>Share of a seated rock's vertices that are visible underside (normal y &lt; −0.25) within the lowest third above the support.</summary>
		private static float Underside(Run run, MeshBuilder m, in PlacedCliffRock rock, float height)
		{
			int bad = 0;
			for (int i = 0; i < m.VertexCount; i++)
			{
				Vector3 v = rock.World(m.Positions[i]);
				float gy = Support(run, v.x, v.z, false);
				if (v.y > gy && v.y < gy + height * 0.33f && rock.Normal(m.Normals[i]).y < -0.25f)
				{
					bad++;
				}
			}
			return bad / (float)Mathf.Max(1, m.VertexCount);
		}

		/// <summary>Area share of a seated rock under the support.</summary>
		private static float Hidden(Run run, MeshBuilder m, in PlacedCliffRock rock, bool terrainOnly)
		{
			double all = 0, hid = 0;
			foreach (List<int> tri in m.Submeshes)
			{
				for (int t = 0; t < tri.Count; t += 3)
				{
					Vector3 a = rock.World(m.Positions[tri[t]]), b = rock.World(m.Positions[tri[t + 1]]), c = rock.World(m.Positions[tri[t + 2]]);
					float area = Vector3.Cross(b - a, c - a).magnitude;
					Vector3 ce = (a + b + c) / 3f;
					all += area;
					if (ce.y < Support(run, ce.x, ce.z, terrainOnly) - 0.05f)
					{
						hid += area;
					}
				}
			}
			return (float)(hid / Math.Max(1e-9, all));
		}

		private static void Stamp(Run run, in PlacedCliffRock rock)
		{
			MeshBuilder m = run.Metrics.Of(rock.Piece).Mesh;
			foreach (List<int> tri in m.Submeshes)
			{
				for (int t = 0; t < tri.Count; t += 3)
				{
					run.Top.Add(rock.World(m.Positions[tri[t]]), rock.World(m.Positions[tri[t + 1]]), rock.World(m.Positions[tri[t + 2]]));
				}
			}
		}

		// ── Talus cones ───────────────────────────────────────────────

		private static void Cones(Run run)
		{
			CliffGrid g = run.G;
			var feet = new List<(int k, float score)>();
			for (int k = 0; k < g.Count; k++)
			{
				if (g.SteepDist[k] <= 0f || g.SteepDist[k] > 3f || g.H[k] >= g.NearestSteepH[k] || g.Site[k] < 0)
				{
					continue;
				}
				g.Position(k, out float x, out float z);
				if (!g.Inside(x, z, 20f))
				{
					continue;
				}
				Vector3 up = new Vector3(x, 0f, z) - g.DownhillSmooth(x, z) * 10f;
				bool bare = run.Top.At(up.x, up.z) < g.Height(up.x, up.z) + 0.5f;
				feet.Add((k, g.BandHeight(x, z) * (bare ? 1.8f : 1f) * run.Rng.Range(0.7f, 1.3f)));
			}
			feet.Sort((a, b) => b.score.CompareTo(a.score));
			float tan = Mathf.Tan(run.O.ReposeDegrees * Mathf.Deg2Rad);
			int maxCones = Mathf.Max(0, (int)(g.CliffLength / 45f));
			foreach ((int k, float _) in feet)
			{
				if (run.Plan.Cones.Count >= maxCones)
				{
					break;
				}
				g.Position(k, out float x, out float z);
				float band = g.BandHeight(x, z);
				if (band < 6f)
				{
					continue;
				}
				float r = Mathf.Clamp(4f + 0.45f * band, 7f, 20f) * run.Rng.Range(0.85f, 1.15f);
				Vector3 down = g.DownhillSmooth(x, z);
				Vector3 apex = new Vector3(x, 0f, z) - down * (0.25f * r);
				bool clash = false;
				foreach (CliffCone q in run.Plan.Cones)
				{
					if (Flat(q.Apex - apex) < 1.15f * (q.Radius + r)) { clash = true; break; }
				}
				if (clash)
				{
					continue;
				}
				float apexH = g.Height(apex.x, apex.z) + r * tan * 0.6f;
				// Only where the ground below can hold a cone: down the fall line its surface must meet the ground within 2.2 r.
				bool meets = false;
				for (float d = 1f; d < 2.2f * r; d += 0.5f)
				{
					Vector3 q = apex + down * d;
					if (apexH - d * tan <= g.Height(q.x, q.z)) { meets = true; break; }
				}
				if (!meets)
				{
					continue;
				}
				apex.y = apexH;
				run.Plan.Cones.Add(new CliffCone { Apex = apex, Radius = r, ApexHeight = apexH, Down = down, Type = g.Sites[g.Site[k]].Type });
			}
			g.StampCones(run.Plan.Cones, tan, g.Seed + 23);
			run.Plan.Stats.Cones = run.Plan.Cones.Count;

			// Fall-sorted debris on each cone, resting on the ground only (no towers of loose rock).
			foreach (CliffCone cone in run.Plan.Cones)
			{
				var site = new CliffRockSite { Type = cone.Type, Roundness = 0 };
				Vector3 strike = new Vector3(cone.Down.z, 0f, -cone.Down.x);
				float toe = cone.Radius;
				for (float d = 0f; d < 4f * cone.Radius; d += 0.5f)
				{
					Vector3 q = cone.Apex + cone.Down * d;
					if (d > 1f && cone.ApexHeight - d * tan <= g.Height(q.x, q.z) + 0.05f) { toe = d; break; }
				}
				int n = Mathf.Clamp(Mathf.RoundToInt(cone.Radius * 1.6f), 10, 32);
				var mine = new List<PlacedCliffRock>();
				CliffStructure structure = CliffRocks.StructureOf(cone.Type);
				for (int i = 0; i < n + 3 && run.Plan.Rocks.Count < run.O.MaxRocks; i++)
				{
					bool big = i >= n;
					float u = big ? run.Rng.Range(1f, 1.3f) : Mathf.Sqrt(run.Rng.Range(0.05f, 1f));
					float ang = run.Rng.Range(-75f, 75f) * Mathf.Deg2Rad;
					Vector3 dir = cone.Down * Mathf.Cos(ang) + strike * Mathf.Sin(ang);
					Vector3 at = cone.Apex + dir * (u * toe);
					if (!g.Inside(at.x, at.z, 4f))
					{
						continue;
					}
					at.y = g.Height(at.x, at.z);
					// Fall sorting: the size grows with the run-out.
					float size = big ? run.Rng.Range(8f, 12f) : Mathf.Lerp(1.5f, 6.5f, Mathf.Pow(u, 1.5f)) * run.Rng.Range(0.8f, 1.25f);
					CliffShape[] shapes = CliffRocks.ShapesOf(cone.Type, CliffRole.Debris);
					int kind = 0;
					for (int s = 1; s < shapes.Length; s++)
					{
						if (Mathf.Abs(shapes[s].Length - size) < Mathf.Abs(shapes[kind].Length - size)) kind = s;
					}
					var piece = new CliffPiece(cone.Type, CliffRole.Debris, kind, -1, run.Rng.Next(shapes[kind].Variants));
					Metrics m = run.Metrics.Of(piece);
					Vector3 S = Aniso(run, structure) * (size / Mathf.Max(0.1f, m.Length));
					var c = NewCandidate(run, m, at, S, structure, false);
					c.TerrainOnly = true;
					c.Band = 0.1f;
					c.R = StablePose(m.Mesh, m.Com, S, g.Downhill(at.x, at.z), g.SlopeAt(at.x, at.z), run.Rng);
					if (!Fit(run, c, 0.6f, out PlacedCliffRock rock))
					{
						continue;
					}
					Vector3 ce = Centre(in rock, m);
					float rr = Radius(m, S);
					bool hit = false;
					foreach (PlacedCliffRock q in mine)
					{
						Metrics qm = run.Metrics.Of(q.Piece);
						if ((Centre(in q, qm) - ce).magnitude < 0.75f * (Radius(qm, q.Scale) + rr)) { hit = true; break; }
					}
					if (hit)
					{
						continue;
					}
					rock.Talus = true;
					rock.BandT = 0f;
					mine.Add(rock);
					run.Plan.Rocks.Add(rock);
					Stamp(run, rock);
				}
				run.Plan.Stats.Talus += mine.Count;
			}
		}
	}

	/// <summary>The planner's height grid: heights, slope, smoothed gradient, sites, steep mask and the zone.</summary>
	internal sealed class CliffGrid
	{
		public readonly float X0, Z0, S;
		public readonly int NX, NZ;
		public readonly float[] H, Delta;
		public float[] Slope, GX, GZ, SGX, SGZ, Debris;
		public readonly bool[] Valid;
		public int[] Site;
		public bool[] Steep, InZone;
		public float[] SteepDist, NearestSteepH, Reach, GateStrength;
		public readonly List<CliffRockSite> Sites = new List<CliffRockSite>();
		public readonly List<string> TypesPresent = new List<string>();
		private readonly Dictionary<string, float> cliffLength = new Dictionary<string, float>(StringComparer.Ordinal);
		public float CliffLength;
		public int Seed;
		public int Count => NX * NZ;

		public CliffGrid(ICliffRockGround ground, Rect area, float cell)
		{
			S = cell;
			X0 = area.xMin;
			Z0 = area.yMin;
			NX = Mathf.Max(2, (int)(area.width / cell) + 1);
			NZ = Mathf.Max(2, (int)(area.height / cell) + 1);
			H = new float[NX * NZ];
			Delta = new float[NX * NZ];
			Valid = new bool[NX * NZ];
			float fallback = 0f;
			for (int j = 0; j < NZ; j++)
			{
				for (int i = 0; i < NX; i++)
				{
					int k = j * NX + i;
					if (ground.TryHeight(X0 + i * S, Z0 + j * S, out float h))
					{
						H[k] = h;
						Valid[k] = true;
						fallback = h;
					}
					else
					{
						H[k] = fallback;
					}
				}
			}
			Derive();
		}

		public void Position(int k, out float x, out float z) { x = X0 + (k % NX) * S; z = Z0 + (k / NX) * S; }
		public bool Inside(float x, float z, float margin) => x > X0 + margin && z > Z0 + margin && x < X0 + (NX - 1) * S - margin && z < Z0 + (NZ - 1) * S - margin;
		public bool IsSteep(int k) => Steep != null && Steep[k];

		/// <summary>Gradient (±2 cells) and 3×3-smoothed slope from the heights.</summary>
		public void Derive()
		{
			int n = NX * NZ;
			GX = new float[n]; GZ = new float[n]; Slope = new float[n];
			var sl = new float[n];
			for (int j = 0; j < NZ; j++)
			{
				for (int i = 0; i < NX; i++)
				{
					int i0 = Math.Max(0, i - 2), i1 = Math.Min(NX - 1, i + 2), j0 = Math.Max(0, j - 2), j1 = Math.Min(NZ - 1, j + 2);
					int k = j * NX + i;
					GX[k] = (H[j * NX + i1] - H[j * NX + i0]) / ((i1 - i0) * S);
					GZ[k] = (H[j1 * NX + i] - H[j0 * NX + i]) / ((j1 - j0) * S);
					sl[k] = Mathf.Atan(Mathf.Sqrt(GX[k] * GX[k] + GZ[k] * GZ[k])) * Mathf.Rad2Deg;
				}
			}
			for (int j = 0; j < NZ; j++)
			{
				for (int i = 0; i < NX; i++)
				{
					float sum = 0f; int c = 0;
					for (int b = -1; b <= 1; b++)
					{
						for (int a = -1; a <= 1; a++)
						{
							int ii = i + a, jj = j + b;
							if (ii < 0 || jj < 0 || ii >= NX || jj >= NZ) continue;
							sum += sl[jj * NX + ii];
							c++;
						}
					}
					Slope[j * NX + i] = sum / c;
				}
			}
			SGX = Blur(GX, 6, 2);
			SGZ = Blur(GZ, 6, 2);
		}

		private float[] Blur(float[] f, int r, int passes)
		{
			var a = (float[])f.Clone();
			var b = new float[a.Length];
			for (int p = 0; p < passes; p++)
			{
				for (int z = 0; z < NZ; z++)
				{
					float run = 0f; int c = 0;
					for (int k = 0; k <= Math.Min(NX - 1, r); k++) { run += a[z * NX + k]; c++; }
					for (int x = 0; x < NX; x++)
					{
						b[z * NX + x] = run / c;
						int add = x + r + 1, rem = x - r;
						if (add < NX) { run += a[z * NX + add]; c++; }
						if (rem >= 0) { run -= a[z * NX + rem]; c--; }
					}
				}
				for (int x = 0; x < NX; x++)
				{
					float run = 0f; int c = 0;
					for (int k = 0; k <= Math.Min(NZ - 1, r); k++) { run += b[k * NX + x]; c++; }
					for (int z = 0; z < NZ; z++)
					{
						a[z * NX + x] = run / c;
						int add = z + r + 1, rem = z - r;
						if (add < NZ) { run += b[add * NX + x]; c++; }
						if (rem >= 0) { run -= b[rem * NX + x]; c--; }
					}
				}
			}
			return a;
		}

		public float Bilerp(float[] f, float x, float z)
		{
			Cell(x, z, out int i, out int j, out float fx, out float fz);
			float a = f[j * NX + i], b = f[j * NX + i + 1], c = f[(j + 1) * NX + i], d = f[(j + 1) * NX + i + 1];
			return (a * (1 - fx) + b * fx) * (1 - fz) + (c * (1 - fx) + d * fx) * fz;
		}

		/// <summary>
		/// The cell (i, j) a point falls in, clamped so its far corner (i + 1, j + 1) is on the grid, and
		/// the point's fractions across it.
		/// </summary>
		/// <remarks>
		/// The index is clamped, not the coordinate. Clamping the coordinate to <c>NX - 1.0001f</c> looked
		/// safe, but above 2,048 cells a float cannot hold that 0.0001: on a 3,000-cell grid (a large scene
		/// at 2 m cells) 2999 − 0.0001 rounds to 2999, the index became the last column, and reading
		/// i + 1 ran off the row (and j + 1 off the array) — the crash in the contour tracer. A NaN
		/// coordinate lands in cell 0 instead of indexing at int.MinValue.
		/// </remarks>
		private void Cell(float x, float z, out int i, out int j, out float fx, out float fz)
		{
			float gx = (x - X0) / S, gz = (z - Z0) / S;
			if (float.IsNaN(gx)) gx = 0f;
			if (float.IsNaN(gz)) gz = 0f;
			gx = Mathf.Clamp(gx, 0f, NX - 1);
			gz = Mathf.Clamp(gz, 0f, NZ - 1);
			i = Math.Min((int)gx, NX - 2);
			j = Math.Min((int)gz, NZ - 2);
			fx = Mathf.Clamp01(gx - i);
			fz = Mathf.Clamp01(gz - j);
		}

		/// <summary>Height on the grid's triangulation ((i,j)-(i+1,j+1) diagonal).</summary>
		public float Height(float x, float z)
		{
			Cell(x, z, out int i, out int j, out float fx, out float fz);
			float a = H[j * NX + i], b = H[j * NX + i + 1], c = H[(j + 1) * NX + i], d = H[(j + 1) * NX + i + 1];
			return fx >= fz ? a + (b - a) * fx + (d - b) * fz : a + (d - c) * fx + (c - a) * fz;
		}

		public float DeltaAt(float x, float z) => Bilerp(Delta, x, z);
		public float DebrisAt(float x, float z) => Debris != null ? Bilerp(Debris, x, z) : 0f;
		public float SlopeAt(float x, float z) => Bilerp(Slope, x, z);

		public Vector3 Downhill(float x, float z)
		{
			float gx = Bilerp(GX, x, z), gz = Bilerp(GZ, x, z), l = Mathf.Sqrt(gx * gx + gz * gz);
			return l < 1e-5f ? new Vector3(0f, 0f, -1f) : new Vector3(-gx / l, 0f, -gz / l);
		}

		public Vector3 DownhillSmooth(float x, float z)
		{
			float gx = Bilerp(SGX, x, z), gz = Bilerp(SGZ, x, z), l = Mathf.Sqrt(gx * gx + gz * gz);
			return l < 1e-5f ? Downhill(x, z) : new Vector3(-gx / l, 0f, -gz / l);
		}

		public Vector3 NormalSmooth(float x, float z) => new Vector3(-Bilerp(SGX, x, z), 1f, -Bilerp(SGZ, x, z)).normalized;

		private int Nearest(float x, float z)
		{
			int i = Mathf.Clamp(Mathf.RoundToInt((x - X0) / S), 0, NX - 1), j = Mathf.Clamp(Mathf.RoundToInt((z - Z0) / S), 0, NZ - 1);
			return j * NX + i;
		}

		private bool SteepAt(float x, float z) => Steep[Nearest(x, z)];

		/// <summary>The site of the nearest cell that has one, within a few cells.</summary>
		public bool SiteNear(float x, float z, out CliffRockSite site)
		{
			site = default;
			int k = Nearest(x, z);
			if (Site[k] >= 0) { site = Sites[Site[k]]; return true; }
			int i0 = k % NX, j0 = k / NX;
			for (int r = 1; r <= 3; r++)
			{
				for (int j = j0 - r; j <= j0 + r; j++)
				{
					for (int i = i0 - r; i <= i0 + r; i++)
					{
						if (i < 0 || j < 0 || i >= NX || j >= NZ) continue;
						int q = j * NX + i;
						if (Site[q] >= 0) { site = Sites[Site[q]]; return true; }
					}
				}
			}
			return false;
		}

		public float CliffLengthOf(string type) => cliffLength.TryGetValue(type, out float l) ? l : 0f;

		/// <summary>
		/// Reads the sites, marks the steep mask, measures the cliff length per rock, computes the
		/// distance to the mask, the gate and the zone. False when there is no cliff at all.
		/// </summary>
		public bool AnalyseSites(CliffRockSiteAt siteAt, float coverage, float columnarCoverage)
		{
			int n = Count;
			Site = new int[n];
			Steep = new bool[n];
			var index = new Dictionary<(string, int, int), int>();
			for (int k = 0; k < n; k++)
			{
				Site[k] = -1;
				if (!Valid[k] || Slope[k] < 20f)
				{
					continue;
				}
				Position(k, out float x, out float z);
				if (!siteAt(x, z, H[k], out CliffRockSite site) || string.IsNullOrEmpty(site.Type))
				{
					continue;
				}
				var key = (site.Type, site.Roundness, Mathf.RoundToInt(site.MinAngle * 10f));
				if (!index.TryGetValue(key, out int si))
				{
					si = Sites.Count;
					index[key] = si;
					Sites.Add(site);
					if (!TypesPresent.Contains(site.Type)) TypesPresent.Add(site.Type);
				}
				Site[k] = si;
				Steep[k] = Slope[k] >= site.MinAngle;
			}
			// Cliff length: half the steep mask's boundary, per rock.
			for (int j = 0; j < NZ; j++)
			{
				for (int i = 0; i < NX; i++)
				{
					int k = j * NX + i;
					if (!Steep[k]) continue;
					int edges = 0;
					if (i == 0 || !Steep[k - 1]) edges++;
					if (i == NX - 1 || !Steep[k + 1]) edges++;
					if (j == 0 || !Steep[k - NX]) edges++;
					if (j == NZ - 1 || !Steep[k + NX]) edges++;
					if (edges == 0) continue;
					string type = Sites[Site[k]].Type;
					float l = edges * S * 0.5f * 0.785f; // ×π/4: a staircase boundary overstates a smooth one
					cliffLength[type] = CliffLengthOf(type) + l;
					CliffLength += l;
				}
			}
			if (CliffLength <= 0f)
			{
				return false;
			}
			Seed = (int)(ProceduralNoise.SeedFor("CliffRocks", (int)(X0 * 7 + Z0 * 13)) & 0x7fffffff);

			// Distance to the steep mask (two-pass chamfer through the source cell) and that cell's height.
			SteepDist = new float[n];
			NearestSteepH = new float[n];
			var src = new int[n];
			for (int k = 0; k < n; k++)
			{
				SteepDist[k] = Steep[k] ? 0f : float.MaxValue;
				src[k] = Steep[k] ? k : -1;
			}
			void Relax(int k, int q)
			{
				if (q < 0 || q >= n || src[q] < 0) return;
				int s = src[q];
				float dx = (k % NX - s % NX) * S, dz = (k / NX - s / NX) * S, d = Mathf.Sqrt(dx * dx + dz * dz);
				if (d < SteepDist[k]) { SteepDist[k] = d; src[k] = s; }
			}
			for (int pass = 0; pass < 2; pass++)
			{
				for (int z = 0; z < NZ; z++)
				{
					for (int x = 0; x < NX; x++)
					{
						int k = z * NX + x;
						if (x > 0) Relax(k, k - 1);
						if (z > 0) { Relax(k, k - NX); if (x > 0) Relax(k, k - NX - 1); if (x < NX - 1) Relax(k, k - NX + 1); }
					}
				}
				for (int z = NZ - 1; z >= 0; z--)
				{
					for (int x = NX - 1; x >= 0; x--)
					{
						int k = z * NX + x;
						if (x < NX - 1) Relax(k, k + 1);
						if (z < NZ - 1) { Relax(k, k + NX); if (x < NX - 1) Relax(k, k + NX + 1); if (x > 0) Relax(k, k + NX - 1); }
					}
				}
			}
			for (int k = 0; k < n; k++)
			{
				NearestSteepH[k] = src[k] >= 0 ? H[src[k]] : H[k];
				if (Site[k] < 0 && src[k] >= 0 && SteepDist[k] <= 30f)
				{
					Site[k] = Site[src[k]]; // the spur/crest ground takes its cliff's rock
				}
			}

			// The gate: fall-line noise (sampled with the point slid down its fall line to a datum, so it is
			// constant along a fall line) plus weaker clustering noise; spur and crest reach noises.
			var gate = new float[n];
			var spur = new float[n];
			var crest = new float[n];
			var near = new List<int>();
			for (int k = 0; k < n; k++)
			{
				if (Site[k] < 0 || SteepDist[k] > 30f) continue;
				near.Add(k);
				Position(k, out float x, out float z);
				Vector3 d = DownhillSmooth(x, z);
				float slide = H[k] / Mathf.Tan(55f * Mathf.Deg2Rad);
				float qx = x - d.x * slide, qz = z - d.z * slide;
				gate[k] = ProceduralNoise.Fbm3(new Vector3(qx / 42f, 0.5f, qz / 42f), 3, 0.5f, Seed + 11)
					+ 0.55f * ProceduralNoise.Fbm3(new Vector3(x / 20f, 1.5f, z / 20f), 3, 0.5f, Seed + 13);
				spur[k] = ProceduralNoise.Fbm3(new Vector3(qx / 30f, 2.5f, qz / 30f), 2, 0.5f, Seed + 17);
				crest[k] = ProceduralNoise.Fbm3(new Vector3(x / 26f, 3.5f, z / 26f), 2, 0.5f, Seed + 19);
			}
			float Std(float[] f)
			{
				double m = 0, v = 0;
				foreach (int k in near) m += f[k];
				m /= Math.Max(1, near.Count);
				foreach (int k in near) v += (f[k] - m) * (f[k] - m);
				return (float)Math.Sqrt(v / Math.Max(1, near.Count)) + 1e-6f;
			}
			float sg = Std(gate), ss = Std(spur), sc = Std(crest);
			// Per-structure gate thresholds: the coverage quantile of the gate over the steep cells.
			var steepGates = new Dictionary<bool, List<float>> { [false] = new List<float>(), [true] = new List<float>() };
			foreach (int k in near)
			{
				gate[k] /= sg; spur[k] /= ss; crest[k] /= sc;
				if (Steep[k]) steepGates[CliffRocks.StructureOf(Sites[Site[k]].Type) == CliffStructure.Columnar].Add(gate[k]);
			}
			InZone = new bool[n];
			Reach = new float[n];
			GateStrength = new float[n];
			var theta = new Dictionary<bool, float>();
			foreach (KeyValuePair<bool, List<float>> kv in steepGates)
			{
				float cov = kv.Key ? columnarCoverage : coverage;
				kv.Value.Sort();
				theta[kv.Key] = cov >= 0.999f || kv.Value.Count == 0 ? float.MinValue : kv.Value[(int)((1f - cov) * (kv.Value.Count - 1))];
			}
			foreach (int k in near)
			{
				float th = theta[CliffRocks.StructureOf(Sites[Site[k]].Type) == CliffStructure.Columnar];
				if (gate[k] < th) continue;
				GateStrength[k] = Mathf.Clamp01((gate[k] - th) / 1.5f);
				if (Steep[k])
				{
					InZone[k] = true;
					continue;
				}
				bool above = H[k] > NearestSteepH[k];
				Reach[k] = above ? 2f + 10f * Mathf.Max(0f, crest[k]) : 3f + 22f * Mathf.Max(0f, spur[k] - 0.2f);
				InZone[k] = SteepDist[k] <= Reach[k];
			}
			return true;
		}

		// ── Band ──────────────────────────────────────────────────────

		private float Walk(float x, float z, float sign)
		{
			float px = x, pz = z, last = Height(x, z), gap = 0f;
			for (int i = 0; i < 200; i++)
			{
				Vector3 d = DownhillSmooth(px, pz);
				px += d.x * 0.5f * sign;
				pz += d.z * 0.5f * sign;
				if (!Inside(px, pz, 1f)) break;
				if (SteepAt(px, pz)) { last = Height(px, pz); gap = 0f; }
				else if ((gap += 0.5f) >= 4f) break;
			}
			return last;
		}

		/// <summary>Moves an off-face point onto the steep ground within 8 m along its fall line; false when there is none.</summary>
		private bool OntoFace(ref float x, ref float z, out int side)
		{
			side = 0;
			if (SteepAt(x, z)) return true;
			Vector3 dn = DownhillSmooth(x, z);
			for (float d = 0.5f; d <= 8f; d += 0.5f)
			{
				if (SteepAt(x - dn.x * d, z - dn.z * d)) { x -= dn.x * d; z -= dn.z * d; side = -1; return true; }
				if (SteepAt(x + dn.x * d, z + dn.z * d)) { x += dn.x * d; z += dn.z * d; side = 1; return true; }
			}
			return false;
		}

		/// <summary>Height of THIS band: crest minus foot along the fall line through the point (or its face within 8 m).</summary>
		public float BandHeight(float x, float z)
		{
			if (!OntoFace(ref x, ref z, out _)) return 0f;
			return Mathf.Max(0f, Walk(x, z, -1f) - Walk(x, z, 1f));
		}

		/// <summary>Relative height in this band: 0 at its foot, 1 at its crest; off the face 0 below it and 1 above it.</summary>
		public float BandT(float x, float z)
		{
			float y = Height(x, z), fx = x, fz = z;
			if (!OntoFace(ref fx, ref fz, out int side))
			{
				int k = Nearest(x, z);
				return H[k] > NearestSteepH[k] ? 1f : 0f;
			}
			if (side != 0) return side < 0 ? 0f : 1f; // steep ground uphill: a foot; downhill: a crest
			float foot = Walk(x, z, 1f), crestH = Walk(x, z, -1f);
			return crestH - foot < 1f ? 0.5f : Mathf.Clamp01((y - foot) / (crestH - foot));
		}

		// ── Foot lines ────────────────────────────────────────────────

		/// <summary>The steep mask's downhill boundary as polylines (marching squares on slope − cliff angle).</summary>
		public List<List<Vector3>> FootLines()
		{
			var f = new float[Count];
			for (int k = 0; k < Count; k++)
			{
				f[k] = Site[k] >= 0 ? Slope[k] - Sites[Site[k]].MinAngle : -10f;
			}
			var result = new List<List<Vector3>>();
			foreach (List<Vector3> line in Contours(f, 0f))
			{
				List<Vector3> current = null;
				foreach (Vector3 p in line)
				{
					Vector3 d = DownhillSmooth(p.x, p.z);
					Vector3 q = p + d * 4f;
					bool foot = Inside(p.x, p.z, 6f) && SlopeAt(q.x, q.z) < SiteAngle(q.x, q.z) && Height(q.x, q.z) < p.y;
					if (foot)
					{
						current ??= new List<Vector3>();
						current.Add(p);
					}
					else if (current != null)
					{
						if (current.Count >= 3) result.Add(current);
						current = null;
					}
				}
				if (current != null && current.Count >= 3) result.Add(current);
			}
			return result;
		}

		private float SiteAngle(float x, float z)
		{
			int k = Nearest(x, z);
			return Site[k] >= 0 ? Sites[Site[k]].MinAngle : 40f;
		}

		/// <summary>Contour polylines of a field at a level, chained through shared cell edges.</summary>
		public List<List<Vector3>> Contours(float[] f, float level)
		{
			float V(int i, int j) { float v = f[j * NX + i]; return v == level ? v + 1e-4f : v; }
			Vector3 EdgePoint(long e)
			{
				int cell = (int)(e >> 1), i = cell % NX, j = cell / NX;
				bool vert = (e & 1) == 1;
				int i2 = vert ? i : i + 1, j2 = vert ? j + 1 : j;
				float a = V(i, j), b = V(i2, j2), t = (level - a) / (b - a);
				float x = X0 + (i + (i2 - i) * t) * S, z = Z0 + (j + (j2 - j) * t) * S;
				return new Vector3(x, Height(x, z), z);
			}
			var segs = new List<(long a, long b)>();
			for (int j = 0; j < NZ - 1; j++)
			{
				for (int i = 0; i < NX - 1; i++)
				{
					float a = V(i, j), b = V(i + 1, j), c = V(i + 1, j + 1), d = V(i, j + 1);
					bool A = a > level, B = b > level, C = c > level, D = d > level;
					long e0 = (long)(j * NX + i) * 2, e1 = (long)(j * NX + i + 1) * 2 + 1, e2 = (long)((j + 1) * NX + i) * 2, e3 = (long)(j * NX + i) * 2 + 1;
					var cross = new List<long>(4);
					if (A != B) cross.Add(e0);
					if (B != C) cross.Add(e1);
					if (C != D) cross.Add(e2);
					if (D != A) cross.Add(e3);
					if (cross.Count == 2) segs.Add((cross[0], cross[1]));
					else if (cross.Count == 4)
					{
						bool M = (a + b + c + d) * 0.25f > level;
						if (A != M) segs.Add((e3, e0));
						if (B != M) segs.Add((e0, e1));
						if (C != M) segs.Add((e1, e2));
						if (D != M) segs.Add((e2, e3));
					}
				}
			}
			var at = new Dictionary<long, List<int>>();
			for (int s = 0; s < segs.Count; s++)
			{
				foreach (long e in new[] { segs[s].a, segs[s].b })
				{
					if (!at.TryGetValue(e, out List<int> l)) at[e] = l = new List<int>(2);
					l.Add(s);
				}
			}
			var used = new bool[segs.Count];
			var lines = new List<List<Vector3>>();
			var starts = new List<int>();
			for (int s = 0; s < segs.Count; s++) if (at[segs[s].a].Count == 1 || at[segs[s].b].Count == 1) starts.Add(s);
			for (int s = 0; s < segs.Count; s++) starts.Add(s);
			foreach (int s0 in starts)
			{
				if (used[s0]) continue;
				long start = at[segs[s0].a].Count == 1 ? segs[s0].a : segs[s0].b;
				var line = new List<Vector3> { EdgePoint(start) };
				long cur = start;
				int s = s0;
				while (s >= 0 && !used[s])
				{
					used[s] = true;
					long next = segs[s].a == cur ? segs[s].b : segs[s].a;
					line.Add(EdgePoint(next));
					cur = next;
					s = -1;
					foreach (int t in at[cur]) if (!used[t]) { s = t; break; }
				}
				if (line.Count >= 2) lines.Add(line);
			}
			return lines;
		}

		// ── Cones ─────────────────────────────────────────────────────

		/// <summary>
		/// Raises the cones ray by ray from each apex (a ray stops where the cone first meets the ground,
		/// or where the ground falls away under it — no cone over a lower cliff), feathers the added
		/// height (3 passes of a 5×5 box) and marks it as debris; then re-derives the slopes.
		/// </summary>
		public void StampCones(List<CliffCone> cones, float tan, int noiseSeed)
		{
			var orig = (float[])H.Clone();
			foreach (CliffCone cone in cones)
			{
				var strike = new Vector3(cone.Down.z, 0f, -cone.Down.x);
				for (float a = -100f; a <= 100f; a += 0.5f)
				{
					float ar = a * Mathf.Deg2Rad;
					Vector3 dir = cone.Down * Mathf.Cos(ar) + strike * Mathf.Sin(ar);
					for (float d = 0f; d < 3.5f * cone.Radius; d += 0.4f)
					{
						float x = cone.Apex.x + dir.x * d, z = cone.Apex.z + dir.z * d;
						int i = Mathf.RoundToInt((x - X0) / S), j = Mathf.RoundToInt((z - Z0) / S);
						if (i < 0 || j < 0 || i >= NX || j >= NZ) break;
						int k = j * NX + i;
						float hc = cone.ApexHeight - d * tan + 0.6f * ProceduralNoise.Fbm3(new Vector3(x / 6f, 7.5f, z / 6f), 2, 0.5f, noiseSeed);
						if (d > 1f && hc <= orig[k]) break;
						if (hc - orig[k] > cone.Radius * tan * 0.8f) break;
						if (hc > H[k]) H[k] = hc;
					}
				}
			}
			var delta = new float[Count];
			for (int k = 0; k < Count; k++) delta[k] = H[k] - orig[k];
			for (int pass = 0; pass < 3; pass++)
			{
				var b = new float[Count];
				for (int j = 0; j < NZ; j++)
				{
					for (int i = 0; i < NX; i++)
					{
						float sum = 0f; int c = 0;
						for (int dj = -2; dj <= 2; dj++)
						{
							for (int di = -2; di <= 2; di++)
							{
								int ii = i + di, jj = j + dj;
								if (ii < 0 || jj < 0 || ii >= NX || jj >= NZ) continue;
								sum += delta[jj * NX + ii];
								c++;
							}
						}
						b[j * NX + i] = sum / c;
					}
				}
				delta = b;
			}
			Debris = new float[Count];
			for (int k = 0; k < Count; k++)
			{
				H[k] = orig[k] + delta[k];
				Delta[k] = delta[k];
				Debris[k] = Mathf.Clamp01(delta[k] / 0.6f);
			}
			bool[] steep = Steep;
			Derive();
			Steep = steep;
		}
	}

	/// <summary>A sparse max-height raster of placed rock tops (0.5 m cells in 32 m tiles), so a rock can rest on rock placed before it.</summary>
	internal sealed class CliffTopRaster
	{
		private const int Tile = 64;
		private readonly float s;
		private readonly Dictionary<long, float[]> tiles = new Dictionary<long, float[]>();

		public CliffTopRaster(float cell) { s = cell; }

		private static long Key(int tx, int tz) => ((long)tx << 32) ^ (uint)tz;

		public float At(float x, float z)
		{
			int i = Mathf.RoundToInt(x / s), j = Mathf.RoundToInt(z / s);
			int tx = Mathf.FloorToInt(i / (float)Tile), tz = Mathf.FloorToInt(j / (float)Tile);
			if (!tiles.TryGetValue(Key(tx, tz), out float[] t)) return float.MinValue;
			return t[(j - tz * Tile) * Tile + (i - tx * Tile)];
		}

		private void Max(int i, int j, float y)
		{
			int tx = Mathf.FloorToInt(i / (float)Tile), tz = Mathf.FloorToInt(j / (float)Tile);
			long key = Key(tx, tz);
			if (!tiles.TryGetValue(key, out float[] t))
			{
				t = new float[Tile * Tile];
				for (int q = 0; q < t.Length; q++) t[q] = float.MinValue;
				tiles[key] = t;
			}
			int idx = (j - tz * Tile) * Tile + (i - tx * Tile);
			if (y > t[idx]) t[idx] = y;
		}

		public void Add(Vector3 a, Vector3 b, Vector3 c)
		{
			float ax = a.x / s, az = a.z / s, bx = b.x / s, bz = b.z / s, cx = c.x / s, cz = c.z / s;
			float area = (bx - ax) * (cz - az) - (cx - ax) * (bz - az);
			int x0 = Mathf.FloorToInt(Mathf.Min(ax, Mathf.Min(bx, cx))), x1 = Mathf.CeilToInt(Mathf.Max(ax, Mathf.Max(bx, cx)));
			int z0 = Mathf.FloorToInt(Mathf.Min(az, Mathf.Min(bz, cz))), z1 = Mathf.CeilToInt(Mathf.Max(az, Mathf.Max(bz, cz)));
			if (Mathf.Abs(area) < 1e-6f)
			{
				Max(Mathf.RoundToInt(ax), Mathf.RoundToInt(az), Mathf.Max(a.y, Mathf.Max(b.y, c.y)));
				return;
			}
			for (int z = z0; z <= z1; z++)
			{
				for (int x = x0; x <= x1; x++)
				{
					float w0 = ((bx - x) * (cz - z) - (cx - x) * (bz - z)) / area, w1 = ((cx - x) * (az - z) - (ax - x) * (cz - z)) / area, w2 = 1f - w0 - w1;
					if (w0 < -0.02f || w1 < -0.02f || w2 < -0.02f) continue;
					Max(x, z, w0 * a.y + w1 * b.y + w2 * c.y);
				}
			}
		}
	}
}
#endif
