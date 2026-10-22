#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The linear features of a scene (2026-10-18, phase I2f, decisions 23 and 29): each biome's hedges, walls, fences, rows of trees,
	/// water channels and clearance rows (<see cref="BiomeTemplate.LinearFeatures"/>, <see cref="BiomeLinearStyle"/>) laid along the
	/// lines its country is divided by, as baked props in the scene's point-of-interest prop set.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where.</b> Round each field a settlement lays (<see cref="LayFieldEdges"/>, called by PointOfInterestFields in place of the
	/// kit's fence, its gate on the side toward the settlement); along the routed ways' verges, on one side or both; along a wood's
	/// margin (the contour where the biome's forest stands give way to open ground, TerrainScatter.StandField); round a small site's
	/// ground with gaps where its ways come in; as a fold beside a pastoral site; along a terrace's lip (<see cref="LayAlong"/>, for the
	/// terraced fields' builder). Channels are carried from the water to the fields (PointOfInterestLinearFeatures.Channels.cs).
	/// Everything but the fields' edges is laid by <see cref="LayScene"/> after every site is built, before the ways are baked.
	/// </para>
	/// <para>
	/// <b>How.</b> Walls, fences, hedge bases, gates and clearance rows are structure pieces (StructurePieces.Linear.cs) laid module by
	/// module, each pitched to the ground's chord between its ends, in the biome's structure palette or the style's own material; a
	/// hedge's leaves are the biome's own bushes painted thick along the line into the detail layers (as a cave mouth's are, so the
	/// GPU detail scatter draws them, with no collider), its woody base a piece with one; a row's trees and a hedge's standards are
	/// tree props (their trunks collide); a piantata's garlands are vine curtains swagged from tree to tree. A module is left out where
	/// it would stand in water, across a way, in another field or on ground too steep, and where the style's breaks fall.
	/// </para>
	/// <para>
	/// <b>Its own draws.</b> Every choice is a hash of the scene's or the site's seed with the place (<see cref="BiomeLinearFeatures.Hash"/>),
	/// never a draw from a site's sequence, so nothing else in the scene moves when a biome gains a style. A scene whose biomes name no
	/// style lays nothing and reads nothing (<see cref="AnyIn"/>): every scene today.
	/// </para>
	/// </remarks>
	public static partial class PointOfInterestLinearFeatures
	{
		/// <summary>The steepest a module's chord may climb along its line (rise over run); steeper is left out.</summary>
		public const float MaxGrade = 0.45f;
		/// <summary>The steepest ground a fold or a wood-edge line is laid over, degrees.</summary>
		public const float MaxSlopeDegrees = 22f;
		/// <summary>How far a fence, wall or hedge keeps from a way's trodden edge where it meets or crosses it, metres.</summary>
		public const float WayGapMetres = 0.6f;
		/// <summary>The shortest stretch laid along a lane or a wood's edge, metres.</summary>
		public const float MinRunMetres = 12f;
		/// <summary>The contour lattice a wood's edge is found on, metres.</summary>
		public const float EdgeCellMetres = 6f;
		/// <summary>How far a fold stands out past its site's edge, metres.</summary>
		public const float FoldOutMetres = 22f;
		/// <summary>A fold's size, metres.</summary>
		public static readonly Vector2 FoldSize = new Vector2(14f, 10f);
		/// <summary>How far past a small site's outline its bounds run, metres.</summary>
		public const float BoundsMargin = 4f;

		/// <summary>The kinds of site whose ground a <see cref="BiomeLinearPlace.SiteBounds"/> style closes in: farmsteads, small settlements, mills, a precinct.</summary>
		public static readonly POIType[] BoundedKinds =
		{
			POIType.Village, POIType.Monastery, POIType.AbandonedFarm, POIType.Windmill, POIType.Watermill, POIType.LumberMill, POIType.Hermitage,
			POIType.HuntingLodge, POIType.Waystation,
		};

		/// <summary>The kinds of site a <see cref="BiomeLinearPlace.Folds"/> style pens its flock beside.</summary>
		public static readonly POIType[] FoldKinds = { POIType.Camp, POIType.Village, POIType.AbandonedFarm, POIType.HuntingLodge, POIType.Hermitage };

		/// <summary>The settlements a lane's or a channel's Reach is measured from.</summary>
		public static readonly POIType[] SettlementKinds =
		{
			POIType.Village, POIType.Town, POIType.City, POIType.Capital, POIType.Port, POIType.Monastery, POIType.AbandonedFarm, POIType.Windmill,
		};

		// ── Pure: lines ───────────────────────────────────────────────

		/// <summary>One point along a line: where, and how far along it.</summary>
		public struct Station
		{
			public Vector2 At;
			public float Along;
		}

		/// <summary>Arc length along a polyline at each of its points (pure).</summary>
		public static float[] Along(IReadOnlyList<Vector2> line)
		{
			int n = line != null ? line.Count : 0;
			var s = new float[n];
			for (int i = 1; i < n; i++)
			{
				s[i] = s[i - 1] + (line[i] - line[i - 1]).magnitude;
			}
			return s;
		}

		/// <summary>A polyline's length (pure).</summary>
		public static float Length(IReadOnlyList<Vector2> line)
		{
			float[] s = Along(line);
			return s.Length > 0 ? s[s.Length - 1] : 0f;
		}

		/// <summary>The point at a distance along a polyline (pure; clamped to its ends).</summary>
		public static Vector2 PointAt(IReadOnlyList<Vector2> line, float[] along, float at)
		{
			int n = line.Count;
			if (n == 1 || at <= 0f)
			{
				return line[0];
			}
			for (int i = 1; i < n; i++)
			{
				if (along[i] >= at)
				{
					float span = along[i] - along[i - 1];
					return Vector2.Lerp(line[i - 1], line[i], span > 1e-6f ? (at - along[i - 1]) / span : 0f);
				}
			}
			return line[n - 1];
		}

		/// <summary>
		/// Stations evenly along a polyline (pure): as many intervals as cover it at no more than <paramref name="spacing"/> metres each
		/// (at least one), both ends included. None for a line under a centimetre.
		/// </summary>
		public static List<Station> Stations(IReadOnlyList<Vector2> line, float spacing)
		{
			var stations = new List<Station>();
			if (line == null || line.Count < 2 || !(spacing > 0f))
			{
				return stations;
			}
			float[] s = Along(line);
			float length = s[s.Length - 1];
			if (!(length > 0.01f))
			{
				return stations;
			}
			int count = Mathf.Max(1, Mathf.CeilToInt(length / spacing - 1e-3f));
			for (int k = 0; k <= count; k++)
			{
				float at = length * k / count;
				stations.Add(new Station { At = PointAt(line, s, at), Along = at });
			}
			return stations;
		}

		/// <summary>
		/// A polyline moved sideways by <paramref name="offset"/> metres, left of its direction positive (pure): each point along the
		/// mean of its two segments' normals, the move lengthened at a bend (a mitre, held to twice the offset).
		/// </summary>
		public static List<Vector2> Offset(IReadOnlyList<Vector2> line, float offset)
		{
			var moved = new List<Vector2>();
			int n = line != null ? line.Count : 0;
			for (int i = 0; i < n; i++)
			{
				Vector2 a = line[Mathf.Max(0, i - 1)], b = line[Mathf.Min(n - 1, i + 1)];
				Vector2 normal = Vector2.zero;
				if (i > 0)
				{
					normal += Left(line[i] - line[i - 1]);
				}
				if (i < n - 1)
				{
					normal += Left(line[i + 1] - line[i]);
				}
				if (normal.sqrMagnitude < 1e-10f)
				{
					normal = Left(b - a);
				}
				normal = normal.sqrMagnitude > 1e-10f ? normal.normalized : Vector2.zero;
				// The mitre: as far out as keeps both neighbouring segments the offset away, held to twice it.
				float cos = i > 0 && i < n - 1 ? Vector2.Dot(normal, Left(line[i] - line[i - 1])) : 1f;
				float mitre = Mathf.Min(2f, 1f / Mathf.Max(0.5f, cos));
				moved.Add(line[i] + normal * offset * mitre);
			}
			return moved;
		}

		/// <summary>The unit vector a quarter turn left of a direction on the plan (x east, y north), or zero.</summary>
		public static Vector2 Left(Vector2 d) => d.sqrMagnitude > 1e-12f ? new Vector2(-d.y, d.x).normalized : Vector2.zero;

		/// <summary>
		/// Which modules of a line are laid (pure): every module but those <paramref name="blocked"/> says may not stand, the gate
		/// module (a gap where the gate goes, <paramref name="gate"/>, −1 for none) and those the breaks draw (each module's own hash of
		/// <paramref name="seed"/>: a share <paramref name="breaks"/> of them, the same on every cut).
		/// </summary>
		public static bool[] Kept(int modules, Func<int, bool> blocked, int gate, int seed, float breaks)
		{
			var kept = new bool[Mathf.Max(0, modules)];
			for (int k = 0; k < kept.Length; k++)
			{
				kept[k] = k != gate && (blocked == null || !blocked(k))
					&& !(breaks > 0f && BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(seed, k, 0x42524B, 0)) < breaks);
			}
			return kept;
		}

		/// <summary>
		/// The runs of a polyline's points that may carry a feature (pure): consecutive points <paramref name="ok"/> admits, each run as
		/// its points, those shorter than <paramref name="minLength"/> metres dropped.
		/// </summary>
		public static List<List<Vector2>> Runs(IReadOnlyList<Vector2> line, Func<Vector2, bool> ok, float minLength)
		{
			var runs = new List<List<Vector2>>();
			List<Vector2> run = null;
			int n = line != null ? line.Count : 0;
			for (int i = 0; i <= n; i++)
			{
				bool on = i < n && (ok == null || ok(line[i]));
				if (on)
				{
					run ??= new List<Vector2>();
					run.Add(line[i]);
				}
				else if (run != null)
				{
					if (run.Count >= 2 && Length(run) >= minLength)
					{
						runs.Add(run);
					}
					run = null;
				}
			}
			return runs;
		}

		/// <summary>One side of a field or fold: its ends (the side runs from <see cref="From"/> to <see cref="To"/>), outward normal, and whether its gate is in it.</summary>
		public struct Side
		{
			public Vector2 From, To, Outward;
			public bool Gate;
		}

		/// <summary>
		/// A rectangle's four sides (pure; a field or a fold: centre, yaw degrees, size along its own x and z), each moved out by
		/// <paramref name="outset"/> metres, the gate in the side whose outward normal faces <paramref name="toward"/> most (the
		/// settlement), the same rule as the kit's field fence. Order: +z, −z, +x, −x, as the fence's. Each runs round the rectangle the
		/// same way, its inside on the left (2026-10-19): every corner is one side's end and the next's start, so a fence whose modules
		/// carry their post at their start has exactly one post on each, and a gate swung to its left swings in.
		/// </summary>
		public static Side[] RectangleSides(Vector2 centre, float yaw, Vector2 size, Vector2 toward, float outset = 0f)
		{
			float r = yaw * Mathf.Deg2Rad;
			var axisX = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
			var axisZ = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			float hx = size.x * 0.5f + outset, hz = size.y * 0.5f + outset;
			var sides = new[]
			{
				new Side { From = centre + axisZ * hz + axisX * hx, To = centre + axisZ * hz - axisX * hx, Outward = axisZ },
				new Side { From = centre - axisZ * hz - axisX * hx, To = centre - axisZ * hz + axisX * hx, Outward = -axisZ },
				new Side { From = centre + axisX * hx - axisZ * hz, To = centre + axisX * hx + axisZ * hz, Outward = axisX },
				new Side { From = centre - axisX * hx + axisZ * hz, To = centre - axisX * hx - axisZ * hz, Outward = -axisX },
			};
			Vector2 to = (toward - centre).sqrMagnitude > 1e-8f ? (toward - centre).normalized : Vector2.up;
			int gate = 0;
			float best = float.NegativeInfinity;
			for (int s = 0; s < 4; s++)
			{
				float facing = Vector2.Dot(sides[s].Outward, to);
				if (facing > best)
				{
					best = facing;
					gate = s;
				}
			}
			sides[gate].Gate = true;
			return sides;
		}

		/// <summary>The module a side's gate stands in (pure): its middle one, as the kit's field fence leaves its gap.</summary>
		public static int GateModule(int modules) => modules > 0 ? modules / 2 : -1;

		/// <summary>
		/// A site's bounds (pure): its outline (<see cref="PointOfInterestOutline.ReachToward"/>) grown by <paramref name="margin"/>
		/// metres, sampled every <paramref name="stepDegrees"/>, closed (the last point is the first).
		/// </summary>
		public static List<Vector2> Bounds(Vector2 centre, float[] outline, float radius, float margin, float stepDegrees = 6f)
		{
			var ring = new List<Vector2>();
			int n = Mathf.Max(8, Mathf.RoundToInt(360f / Mathf.Max(1f, stepDegrees)));
			for (int k = 0; k <= n; k++)
			{
				float bearing = 360f * (k % n) / n;
				float reach = PointOfInterestOutline.ReachToward(outline, radius, bearing) + margin;
				float b = bearing * Mathf.Deg2Rad;
				ring.Add(centre + new Vector2(Mathf.Sin(b), Mathf.Cos(b)) * reach);
			}
			return ring;
		}

		/// <summary>
		/// The level lines of a field (pure): marching squares over <paramref name="area"/> on a lattice <paramref name="cell"/> metres
		/// apart fixed to the scene's metres, where <paramref name="field"/> crosses 0, the segments chained into polylines (each in
		/// lattice order, so the same field gives the same lines).
		/// </summary>
		public static List<List<Vector2>> Contours(Func<float, float, float> field, Rect area, float cell)
		{
			var lines = new List<List<Vector2>>();
			if (field == null || !(cell > 0f) || area.width <= 0f || area.height <= 0f)
			{
				return lines;
			}
			int x0 = Mathf.FloorToInt(area.xMin / cell), z0 = Mathf.FloorToInt(area.yMin / cell);
			int nx = Mathf.CeilToInt(area.xMax / cell) - x0, nz = Mathf.CeilToInt(area.yMax / cell) - z0;
			var v = new float[nx + 1, nz + 1];
			for (int j = 0; j <= nz; j++)
			{
				for (int i = 0; i <= nx; i++)
				{
					float f = field((x0 + i) * cell, (z0 + j) * cell);
					v[i, j] = Mathf.Abs(f) < 1e-7f ? 1e-7f : f;
				}
			}
			// Each crossing is a point on a lattice edge, keyed by the edge, so neighbouring cells share it.
			long EdgeKey(int i, int j, int dir) => ((long)(j + 1) * (nx + 3) + (i + 1)) * 2 + dir;
			Vector2 Cross(int i, int j, int dir)
			{
				int i1 = dir == 0 ? i + 1 : i, j1 = dir == 0 ? j : j + 1;
				float a = v[i, j], b = v[i1, j1];
				float t = Mathf.Clamp01(a / (a - b));
				return new Vector2((x0 + i + (dir == 0 ? t : 0f)) * cell, (z0 + j + (dir == 1 ? t : 0f)) * cell);
			}
			var neighbours = new Dictionary<long, List<long>>();
			var points = new Dictionary<long, Vector2>();
			var order = new List<long>();
			void Link(long a, long b)
			{
				foreach ((long from, long to) in new[] { (a, b), (b, a) })
				{
					if (!neighbours.TryGetValue(from, out List<long> list))
					{
						list = new List<long>(2);
						neighbours[from] = list;
						order.Add(from);
					}
					list.Add(to);
				}
			}
			for (int j = 0; j < nz; j++)
			{
				for (int i = 0; i < nx; i++)
				{
					// The cell's crossed edges: bottom (i,j,0), top (i,j+1,0), left (i,j,1), right (i+1,j,1).
					var crossed = new List<long>(4);
					void Edge(int ei, int ej, int dir, float a, float b)
					{
						if (a > 0f != b > 0f)
						{
							long key = EdgeKey(ei, ej, dir);
							if (!points.ContainsKey(key))
							{
								points[key] = Cross(ei, ej, dir);
							}
							crossed.Add(key);
						}
					}
					Edge(i, j, 0, v[i, j], v[i + 1, j]);
					Edge(i + 1, j, 1, v[i + 1, j], v[i + 1, j + 1]);
					Edge(i, j + 1, 0, v[i, j + 1], v[i + 1, j + 1]);
					Edge(i, j, 1, v[i, j], v[i, j + 1]);
					if (crossed.Count == 2)
					{
						Link(crossed[0], crossed[1]);
					}
					else if (crossed.Count == 4)
					{
						// A saddle: joined by the cell's mean, so the lines never cross.
						float mean = 0.25f * (v[i, j] + v[i + 1, j] + v[i, j + 1] + v[i + 1, j + 1]);
						bool low = v[i, j] > 0f == mean > 0f;
						Link(crossed[0], low ? crossed[3] : crossed[1]);
						Link(crossed[2], low ? crossed[1] : crossed[3]);
					}
				}
			}
			var used = new HashSet<long>();
			// Open chains from their ends first, then the closed loops left.
			foreach (bool ends in new[] { true, false })
			{
				foreach (long start in order)
				{
					if (used.Contains(start) || ends && neighbours[start].Count != 1)
					{
						continue;
					}
					var line = new List<Vector2>();
					long at = start, previous = long.MinValue;
					while (true)
					{
						used.Add(at);
						line.Add(points[at]);
						long next = long.MinValue;
						foreach (long n in neighbours[at])
						{
							if (n != previous && !used.Contains(n))
							{
								next = n;
								break;
							}
						}
						if (next == long.MinValue)
						{
							// A closed loop comes back to its start.
							if (!ends && neighbours[at].Contains(start) && line.Count > 2)
							{
								line.Add(points[start]);
							}
							break;
						}
						previous = at;
						at = next;
					}
					if (line.Count >= 2)
					{
						lines.Add(line);
					}
				}
			}
			return lines;
		}

		/// <summary>The yaw (degrees) and pitch frame a piece built along x is laid in between two ground points: x along the chord.</summary>
		public static Quaternion AlongX(Vector3 a, Vector3 b)
		{
			Vector3 d = b - a;
			return d.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(d.normalized, Vector3.up) * Quaternion.Euler(0f, -90f, 0f) : Quaternion.identity;
		}

		// ── The styles of a scene ─────────────────────────────────────

		/// <summary>True when any of the biomes laid any style at all (pure but for the reader): the fast path every scene takes today.</summary>
		public static bool AnyIn(IEnumerable<BiomeTemplate> biomes)
		{
			if (biomes != null)
			{
				foreach (BiomeTemplate biome in biomes)
				{
					if (biome != null && BiomeLinearFeatures.Any(BiomeTemplateArt.LinearFeatures(biome)))
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>A biome's laid styles at a place, with their index in its list (for their draws).</summary>
		private static List<(BiomeLinearStyle Style, int Index)> StylesAt(BiomeTemplate biome, BiomeLinearPlace where)
		{
			var found = new List<(BiomeLinearStyle, int)>();
			IReadOnlyList<BiomeLinearStyle> styles = biome != null ? BiomeTemplateArt.LinearFeatures(biome) : null;
			if (styles != null)
			{
				for (int i = 0; i < styles.Count; i++)
				{
					if (styles[i].Where == where && BiomeLinearFeatures.IsLaid(styles[i]))
					{
						found.Add((styles[i], i));
					}
				}
			}
			return found;
		}

		// ── Fields ────────────────────────────────────────────────────

		/// <summary>
		/// Lays a field's boundary in its biome's first field-edge style the field's draw admits (channels apart: those water the
		/// fields from the scene pass): returns true when one was laid, and the field then takes no kit fence. Seeded by the site and
		/// the field's index, never the site's sequence. False (and nothing laid) where the biome names none, as every biome today.
		/// </summary>
		public static bool LayFieldEdges(PointOfInterestSiteContext context, Vector2 centre, float yaw, Vector2 size, Vector2 site, bool abandoned, int field)
		{
			BiomeTemplate biome = context?.BiomeAt != null ? context.BiomeAt(centre.x, centre.y) : null;
			if (biome == null)
			{
				return false;
			}
			foreach ((BiomeLinearStyle style, int index) in StylesAt(biome, BiomeLinearPlace.FieldEdges))
			{
				if (style.Kind == BiomeLinearKind.Channel)
				{
					continue;
				}
				int seed = unchecked((int)BiomeLinearFeatures.Hash(context.Record != null ? context.Record.SiteSeed : 0, 0x4C494E, field, index));
				if (!BiomeLinearFeatures.Admits(style.Density, BiomeLinearFeatures.Hash(seed, 1, 0, 0)))
				{
					continue;
				}
				var layer = new Layer(Copy(context), biome, style, seed) { Broken = abandoned, OwnField = (centre, yaw, size), Sides = true };
				foreach (Side side in RectangleSides(centre, yaw, size, site, OutsetOf(style.Kind)))
				{
					layer.Lay(new[] { side.From, side.To }, side.Gate);
				}
				layer.Report();
				return true;
			}
			return false;
		}

		/// <summary>How far outside a field's edge a kind is laid, metres: a hedge's or a row's width off the crop, a wall or fence on the line.</summary>
		public static float OutsetOf(BiomeLinearKind kind)
		{
			switch (kind)
			{
				case BiomeLinearKind.Hedge: return 0.8f;
				case BiomeLinearKind.TreeRow: return 2f;
				case BiomeLinearKind.Wall:
				case BiomeLinearKind.ClearanceRow: return 0.5f;
				default: return 0f;
			}
		}

		/// <summary>
		/// Lays a style along a line an outside builder found (the terraced fields' steps and lips, an orchard's rows, a garden's bed
		/// edging, a baulatura's ditch banks; I2j-b): the same laying as every other place, seeded by <paramref name="seed"/>. Inside
		/// <paramref name="ownField"/> (a field's centre, yaw and size) its own claim and keep-out stop nothing; <paramref name="seat"/>,
		/// where given, is the height each module is seated at (a terrace wall on the bench below its step) in place of the ground's.
		/// Given <paramref name="joins"/>, its modules are queued there to be laid with the other lines meeting them (a yard's runs: the
		/// caller closes it); else they are laid, with their closing posts, before it returns. <paramref name="planned"/>: a layout already
		/// kept the line off the ways, the fields and the keep-outs, so only water, no ground and too steep a pitch leave a module out.
		/// Returns the props laid (queued, with <paramref name="joins"/>).
		/// </summary>
		public static int LayAlong(PointOfInterestSiteContext context, BiomeTemplate biome, BiomeLinearStyle style, IReadOnlyList<Vector2> line, int seed,
			(Vector2 Centre, float Yaw, Vector2 Size)? ownField = null, Func<Vector2, float> seat = null, Joins joins = null, bool planned = false)
		{
			if (context == null || line == null || line.Count < 2 || !BiomeLinearFeatures.IsLaid(style))
			{
				return 0;
			}
			var layer = new Layer(Copy(context), biome, style, seed, joins) { OwnField = ownField, Seat = seat, Planned = planned };
			layer.Lay(line, false);
			layer.Report();
			return layer.Laid;
		}

		/// <summary>
		/// A site's context for laying in: the same site, ground, water, terrain and prop set, with a palette of its own (each style's
		/// pieces are laid in theirs), so the site's own features after it keep the site's palette.
		/// </summary>
		public static PointOfInterestSiteContext Copy(PointOfInterestSiteContext c) => new PointOfInterestSiteContext
		{
			Scene = c.Scene,
			Root = c.Root,
			Record = c.Record,
			Template = c.Template,
			Random = c.Random,
			Points = c.Points,
			Request = c.Request,
			TerrainFolder = c.TerrainFolder,
			Water = c.Water,
			GroundAt = c.GroundAt,
			Terrains = c.Terrains,
			Tiles = c.Tiles,
			SkyAt = c.SkyAt,
			SlopeAt = c.SlopeAt,
			BiomeAt = c.BiomeAt,
			FieldClaims = c.FieldClaims,
			Notes = c.Notes,
			Sink = c.Sink,
			Laid = null,
		};

		// ── The scene ─────────────────────────────────────────────────

		/// <summary>
		/// Lays every linear feature of the scene but the fields' edges (laid with the fields): the lanes' verges, the woods' edges,
		/// the small sites' bounds, the folds and the channels, each in the style of the biome it lies in. Runs after every site is
		/// built (the fields have claimed their ground, <paramref name="claims"/>) and before the ways are baked (a channel adds its
		/// bed to them). Returns the props laid; 0, reading nothing, where no biome of the scene names a style.
		/// </summary>
		public static int LayScene(Scene scene, Transform root, ScenePointsOfInterest asset, SceneGenerationRequest request, string terrainFolder,
			Terrain[,] terrains, TerrainTilePlan tiles, SceneWater water, Func<float, float, float> ground, Func<float, float, float> slope,
			PointOfInterestFieldClaims claims, PointOfInterestPropSink sink, List<string> notes)
		{
			if (asset == null || request == null || ground == null)
			{
				return 0;
			}
			// The last placing's channel lines go: this one stores its own (2026-10-18, I2j-b; a scene with none keeps none).
			ChannelMarginPaint.Clear(scene);
			SceneBiomeMap map = string.IsNullOrEmpty(terrainFolder) || string.IsNullOrEmpty(request.SceneName) ? null
				: AssetDatabase.LoadAssetAtPath<SceneBiomeMap>($"{terrainFolder}/{WorldEditorAssets.Sanitize(request.SceneName)} Biome Map.asset");
			var biomes = new List<BiomeTemplate>();
			var seen = new HashSet<int>();
			void Note(int id)
			{
				if (seen.Add(id) && BiomeRegistry.TryGetByID(id, out BiomeTemplate b) && b != null)
				{
					biomes.Add(b);
				}
			}
			if (map != null && map.BiomeIDs != null)
			{
				foreach (int id in map.BiomeIDs)
				{
					Note(id);
				}
			}
			foreach (PointOfInterestRecord record in asset.Points)
			{
				Note(record.BiomeID);
			}
			if (!AnyIn(biomes))
			{
				return 0;
			}
			int seed = unchecked((int)SceneGenerator.SceneSeed(request));
			Func<float, float, BiomeTemplate> biomeAt = (x, z) => map != null ? map.Sample(new Vector3(x, 0f, z)) : null;
			var scope = new SceneScope
			{
				Scene = scene,
				Root = root,
				Points = asset,
				Request = request,
				TerrainFolder = terrainFolder,
				Terrains = terrains,
				Tiles = tiles,
				Water = water,
				Ground = ground,
				Slope = slope,
				Claims = claims,
				Sink = sink,
				Notes = notes,
				BiomeAt = biomeAt,
				Seed = seed,
				Ways = asset.Paths != null ? new List<ScenePath>(asset.Paths) : new List<ScenePath>(),
			};
			int laid = 0;
			laid += LayLanes(scope);
			laid += LayWoodEdges(scope, biomes);
			laid += LaySiteBounds(scope);
			laid += LayFolds(scope);
			laid += LayChannels(scope);
			if (laid > 0)
			{
				notes?.Add($"Linear features: {laid:N0} piece(s), tree(s) and garland(s) laid along the lanes, woods' edges, small sites' bounds, folds and leats.");
			}
			return laid;
		}

		/// <summary>Everything the scene pass reads and lays into.</summary>
		private sealed class SceneScope
		{
			public Scene Scene;
			public Transform Root;
			public ScenePointsOfInterest Points;
			public SceneGenerationRequest Request;
			public string TerrainFolder;
			public Terrain[,] Terrains;
			public TerrainTilePlan Tiles;
			public SceneWater Water;
			public Func<float, float, float> Ground, Slope;
			public PointOfInterestFieldClaims Claims;
			public PointOfInterestPropSink Sink;
			public List<string> Notes;
			public Func<float, float, BiomeTemplate> BiomeAt;
			public int Seed;
			/// <summary>The ways as the sites left them (a channel's bed, added after, is not one).</summary>
			public List<ScenePath> Ways;
			/// <summary>The lane ditches laid so far (their pounds' lake ids follow the leats').</summary>
			public int DitchCount;

			/// <summary>A context for laying at a point (the POI root as its site: the field carve's record goes under it, as the fields' do).</summary>
			public PointOfInterestSiteContext Context(PointOfInterestRecord record = null) => new PointOfInterestSiteContext
			{
				Scene = Scene,
				Root = Root,
				Record = record,
				Points = Points,
				Request = Request,
				TerrainFolder = TerrainFolder,
				Water = Water,
				GroundAt = Ground,
				SlopeAt = Slope,
				Terrains = Terrains,
				Tiles = Tiles,
				BiomeAt = BiomeAt,
				FieldClaims = Claims,
				Notes = Notes,
				Sink = Sink,
				Laid = null,
			};

			/// <summary>How far a point lies from the nearest settlement's edge, metres (∞ with none).</summary>
			public float FromSettlement(Vector2 p)
			{
				float best = float.PositiveInfinity;
				foreach (PointOfInterestRecord r in Points.Points)
				{
					if (Array.IndexOf(SettlementKinds, r.Kind) >= 0)
					{
						best = Mathf.Min(best, Mathf.Max(0f, (p - new Vector2(r.Position.x, r.Position.z)).magnitude - r.Radius));
					}
				}
				return best;
			}
		}

		/// <summary>The scene's ways' verges, in the lane styles of the biomes they run through.</summary>
		private static int LayLanes(SceneScope scope)
		{
			int laid = 0;
			for (int w = 0; w < scope.Ways.Count; w++)
			{
				ScenePath way = scope.Ways[w];
				if (way == null || way.Count < 2 || way.Class == ScenePathClass.Street || way.Class == ScenePathClass.Yard)
				{
					continue;
				}
				// Stretches of the way in one biome, off bridges, fords, boardwalks and sites' own ground.
				int start = 0;
				for (int i = 1; i <= way.Count; i++)
				{
					bool split = i == way.Count || Blocks(way.FlagsAt(i)) != Blocks(way.FlagsAt(start))
						|| scope.BiomeAt(way.Points[i].x, way.Points[i].z) != scope.BiomeAt(way.Points[start].x, way.Points[start].z);
					if (!split)
					{
						continue;
					}
					if (!Blocks(way.FlagsAt(start)) && i - start >= 2)
					{
						BiomeTemplate biome = scope.BiomeAt(way.Points[start].x, way.Points[start].z);
						foreach ((BiomeLinearStyle style, int index) in StylesAt(biome, BiomeLinearPlace.Lanes))
						{
							if (BiomeLinearFeatures.Follows(style.Ways, way.Class))
							{
								laid += LayLane(scope, way, start, i - 1, biome, style, index);
							}
						}
					}
					start = i;
				}
			}
			return laid;
		}

		private static bool Blocks(ScenePathPointFlags flags) => (flags & (ScenePathPointFlags.Bridge | ScenePathPointFlags.Ford | ScenePathPointFlags.InSite | ScenePathPointFlags.Boardwalk)) != 0;

		/// <summary>One stretch of a way's verges in one style: each side its density admits, moved out past the trodden edge.</summary>
		private static int LayLane(SceneScope scope, ScenePath way, int from, int to, BiomeTemplate biome, BiomeLinearStyle style, int index)
		{
			var centre = new List<Vector2>();
			var half = new List<float>();
			for (int i = from; i <= to; i++)
			{
				centre.Add(new Vector2(way.Points[i].x, way.Points[i].z));
				half.Add(way.HalfWidth != null && i < way.HalfWidth.Length ? way.HalfWidth[i] : ScenePathStyle.For(way.Class).HalfWidth);
			}
			int laid = 0;
			int pick = (int)(BiomeLinearFeatures.Hash(scope.Seed, way.Id, from, index) & 1u);
			for (int s = 0; s < 2; s++)
			{
				if ((style.Sides == BiomeLinearSides.One || style.Kind == BiomeLinearKind.Channel) && s != pick)
				{
					continue;
				}
				int seed = unchecked((int)BiomeLinearFeatures.Hash(scope.Seed, way.Id * 31 + s, from, 0x4C414E + index));
				if (!BiomeLinearFeatures.Admits(style.Density, BiomeLinearFeatures.Hash(seed, 1, 0, 0)))
				{
					continue;
				}
				float sign = s == 0 ? 1f : -1f;
				var verge = new List<Vector2>(centre.Count);
				List<Vector2> unit = Offset(centre, 1f);
				for (int i = 0; i < centre.Count; i++)
				{
					verge.Add(centre[i] + (unit[i] - centre[i]) * sign * (half[i] + BiomeLinearFeatures.LaneMargin(style.Kind)));
				}
				var layer = new Layer(scope.Context(), biome, style, seed) { OwnWay = way };
				foreach (List<Vector2> run in Runs(verge, p => style.Reach <= 0f || scope.FromSettlement(p) <= style.Reach, MinRunMetres))
				{
					if (style.Kind == BiomeLinearKind.Channel)
					{
						laid += LayDitch(scope, layer, run, seed);
					}
					else
					{
						layer.Lay(run, false);
					}
				}
				layer.Report();
				laid += layer.Laid;
			}
			return laid;
		}

		/// <summary>The woods' margins, in the wood-edge styles of each biome that grows its trees in stands.</summary>
		private static int LayWoodEdges(SceneScope scope, List<BiomeTemplate> biomes)
		{
			int laid = 0;
			Rect area = SceneArea(scope.Terrains);
			foreach (BiomeTemplate biome in biomes)
			{
				List<(BiomeLinearStyle Style, int Index)> styles = StylesAt(biome, BiomeLinearPlace.ClearingEdges);
				if (styles.Count == 0 || area.width <= 0f)
				{
					continue;
				}
				PrefabSpawnRule rule = ForestRule(biome);
				if (rule == null)
				{
					scope.Notes?.Add($"Linear features: {Label(biome)} lays along its woods' edges but grows no trees in stands (no forest rule): none laid.");
					continue;
				}
				var stands = new TerrainScatter.StandField(TerrainScatter.StandSeed(unchecked((uint)scope.Seed)), rule.forestMetres, rule.forestCover, rule.forestEdge, rule.forestOpen);
				List<List<Vector2>> edges = Contours((x, z) => stands.Noise(x, z) - stands.Threshold, area, EdgeCellMetres);
				var exclusion = new PathExclusion(scope.Ways);
				bool Open(Vector2 p) => scope.BiomeAt(p.x, p.y) == biome && (scope.Slope == null || scope.Slope(p.x, p.y) <= MaxSlopeDegrees)
					&& !exclusion.Trees(p.x, p.y) && !scope.Points.IsKeptOut(p.x, p.y) && !InClaims(scope.Claims, p, 1f, null)
					&& (scope.Water == null || scope.Water.KindAt(p.x, p.y) == WaterKind.None);
				for (int e = 0; e < edges.Count; e++)
				{
					List<List<Vector2>> runs = Runs(edges[e], Open, MinRunMetres * 2f);
					for (int r = 0; r < runs.Count; r++)
					{
						foreach ((BiomeLinearStyle style, int index) in styles)
						{
							int seed = unchecked((int)BiomeLinearFeatures.Hash(scope.Seed, e, r, 0x574F44 + index));
							if (!BiomeLinearFeatures.Admits(style.Density, BiomeLinearFeatures.Hash(seed, 1, 0, 0)))
							{
								continue;
							}
							var layer = new Layer(scope.Context(), biome, style, seed);
							layer.Lay(runs[r], false);
							layer.Report();
							laid += layer.Laid;
							break;
						}
					}
				}
			}
			return laid;
		}

		/// <summary>The first of a biome's tree rules that grows in stands (its forest), or null.</summary>
		private static PrefabSpawnRule ForestRule(BiomeTemplate biome)
		{
			var layers = new List<TerrainTextureLayer> { biome.MainTextureLayer };
			if (biome.DetailTextureLayers != null)
			{
				layers.AddRange(biome.DetailTextureLayers);
			}
			foreach (TerrainTextureLayer layer in layers)
			{
				if (layer?.prefabSpawnRules == null)
				{
					continue;
				}
				foreach (PrefabSpawnRule rule in layer.prefabSpawnRules)
				{
					if (rule != null && rule.enableSpawning && rule.IsForested)
					{
						return rule;
					}
				}
			}
			return null;
		}

		/// <summary>The scene's ground on the plan: the union of its tiles.</summary>
		private static Rect SceneArea(Terrain[,] terrains)
		{
			bool any = false;
			float x0 = 0f, z0 = 0f, x1 = 0f, z1 = 0f;
			if (terrains != null)
			{
				foreach (Terrain terrain in terrains)
				{
					if (terrain == null || terrain.terrainData == null)
					{
						continue;
					}
					Vector3 o = terrain.transform.position, s = terrain.terrainData.size;
					x0 = any ? Mathf.Min(x0, o.x) : o.x;
					z0 = any ? Mathf.Min(z0, o.z) : o.z;
					x1 = any ? Mathf.Max(x1, o.x + s.x) : o.x + s.x;
					z1 = any ? Mathf.Max(z1, o.z + s.z) : o.z + s.z;
					any = true;
				}
			}
			return any ? Rect.MinMaxRect(x0, z0, x1, z1) : default;
		}

		/// <summary>True inside any claimed field grown by <paramref name="margin"/>, the field <paramref name="own"/> (centre, yaw, size) apart.</summary>
		private static bool InClaims(PointOfInterestFieldClaims claims, Vector2 p, float margin, (Vector2 Centre, float Yaw, Vector2 Size)? own)
		{
			if (claims == null)
			{
				return false;
			}
			foreach (PointOfInterestFieldClaims.Claim claim in claims.All)
			{
				if (own.HasValue && own.Value.Centre == claim.Centre)
				{
					continue;
				}
				if (PointOfInterestPaddies.Inside(claim.Centre, claim.Yaw, claim.Size, p.x, p.y) > -margin)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The small sites' bounds, with gaps where their ways come in (and a gate toward their heading where none does).</summary>
		private static int LaySiteBounds(SceneScope scope)
		{
			int laid = 0;
			foreach (PointOfInterestRecord record in scope.Points.Points)
			{
				if (Array.IndexOf(BoundedKinds, record.Kind) < 0 || !BiomeRegistry.TryGetByID(record.BiomeID, out BiomeTemplate biome))
				{
					continue;
				}
				foreach ((BiomeLinearStyle style, int index) in StylesAt(biome, BiomeLinearPlace.SiteBounds))
				{
					int seed = unchecked((int)BiomeLinearFeatures.Hash(record.SiteSeed, 0x424E44, index, 0));
					if (!BiomeLinearFeatures.Admits(style.Density, BiomeLinearFeatures.Hash(seed, 1, 0, 0)))
					{
						continue;
					}
					var centre = new Vector2(record.Position.x, record.Position.z);
					List<Vector2> ring = Bounds(centre, record.Outline, record.Radius, BoundsMargin);
					var layer = new Layer(scope.Context(record), biome, style, seed) { KeepOutsFree = true };
					if (!layer.CrossesAnyWay(ring))
					{
						// No way comes in: a gate toward the site's heading, the ring opened there.
						float b = record.Yaw * Mathf.Deg2Rad;
						ring = Rotate(ring, centre + new Vector2(Mathf.Sin(b), Mathf.Cos(b)) * 1000f);
						layer.GateAtStart = true;
					}
					layer.Lay(ring, false);
					layer.Report();
					laid += layer.Laid;
					break;
				}
			}
			return laid;
		}

		/// <summary>A closed ring started again at its point nearest <paramref name="toward"/> (so a gate at its start faces that way).</summary>
		private static List<Vector2> Rotate(List<Vector2> ring, Vector2 toward)
		{
			int n = ring.Count - 1, best = 0;
			for (int i = 1; i < n; i++)
			{
				if ((ring[i] - toward).sqrMagnitude < (ring[best] - toward).sqrMagnitude)
				{
					best = i;
				}
			}
			var turned = new List<Vector2>(ring.Count);
			for (int i = 0; i <= n; i++)
			{
				turned.Add(ring[(best + i) % n]);
			}
			return turned;
		}

		/// <summary>A fold beside each pastoral site its biome's fold style admits, on gentle dry ground clear of ways, fields and sites.</summary>
		private static int LayFolds(SceneScope scope)
		{
			int laid = 0;
			var exclusion = new PathExclusion(scope.Ways);
			foreach (PointOfInterestRecord record in scope.Points.Points)
			{
				if (Array.IndexOf(FoldKinds, record.Kind) < 0 || !BiomeRegistry.TryGetByID(record.BiomeID, out BiomeTemplate biome))
				{
					continue;
				}
				foreach ((BiomeLinearStyle style, int index) in StylesAt(biome, BiomeLinearPlace.Folds))
				{
					int seed = unchecked((int)BiomeLinearFeatures.Hash(record.SiteSeed, 0x464F4C, index, 0));
					if (!BiomeLinearFeatures.Admits(style.Density, BiomeLinearFeatures.Hash(seed, 1, 0, 0)))
					{
						continue;
					}
					var site = new Vector2(record.Position.x, record.Position.z);
					float first = BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(seed, 2, 0, 0)) * 360f;
					for (int t = 0; t < 8; t++)
					{
						float bearing = first + t * 45f;
						float b = bearing * Mathf.Deg2Rad;
						float reach = PointOfInterestOutline.ReachToward(record.Outline, record.Radius, bearing) + FoldOutMetres;
						Vector2 centre = site + new Vector2(Mathf.Sin(b), Mathf.Cos(b)) * reach;
						if (!FoldFits(scope, exclusion, record, centre, bearing))
						{
							continue;
						}
						var layer = new Layer(scope.Context(record), biome, style, seed) { KeepOutsFree = true, Sides = true };
						foreach (Side side in RectangleSides(centre, bearing, FoldSize, site, 0f))
						{
							layer.Lay(new[] { side.From, side.To }, side.Gate);
						}
						layer.Report();
						laid += layer.Laid;
						break;
					}
					break;
				}
			}
			return laid;
		}

		/// <summary>Whether a fold fits: every point round and across it gentle, dry, off the ways and fields, and clear of every other site.</summary>
		private static bool FoldFits(SceneScope scope, PathExclusion ways, PointOfInterestRecord own, Vector2 centre, float yaw)
		{
			foreach (Vector2 p in PointOfInterestPaddies.Interior(centre, yaw, FoldSize + Vector2.one * 2f))
			{
				float g = scope.Ground(p.x, p.y);
				if (float.IsNaN(g) || float.IsInfinity(g) || scope.Slope != null && scope.Slope(p.x, p.y) > 14f || ways.Trees(p.x, p.y) || InClaims(scope.Claims, p, 3f, null)
					|| scope.Water != null && scope.Water.KindAt(p.x, p.y) != WaterKind.None)
				{
					return false;
				}
				foreach (PointOfInterestRecord r in scope.Points.Points)
				{
					if (r != own && (p - new Vector2(r.Position.x, r.Position.z)).magnitude < r.Radius + 8f)
					{
						return false;
					}
				}
			}
			return true;
		}

		// ── Laying one line ───────────────────────────────────────────

		/// <summary>
		/// Lays one style along lines for one place (a field, a lane's stretch, a wood's edge, a site's bounds, a fold): modules, the
		/// gate, a hedge's leaves and standards, a row's trees and garlands, counting what it laid for the notes.
		/// </summary>
		private sealed class Layer
		{
			private readonly PointOfInterestSiteContext context;
			private readonly BiomeTemplate biome;
			private readonly BiomeLinearStyle style;
			private readonly int seed;
			private readonly float roll;
			private int lines;
			public int Laid;
			public bool Broken;
			/// <summary>A site's own bounds or fold: its own keep-out does not stop it.</summary>
			public bool KeepOutsFree;
			/// <summary>A closed ring opened at its start for its gate (a site with no way in).</summary>
			public bool GateAtStart;
			/// <summary>The field it bounds (its own claim does not stop it).</summary>
			public (Vector2 Centre, float Yaw, Vector2 Size)? OwnField;
			/// <summary>Laid side by side round a rectangle: a side's last tree is the next side's first, so it is left out.</summary>
			public bool Sides;
			/// <summary>The way it lines (its own trodden width is beside it, not across it).</summary>
			public ScenePath OwnWay;
			/// <summary>The height a module is seated at, where given (an outside builder's line, LayAlong), in place of the ground's.</summary>
			public Func<Vector2, float> Seat;
			/// <summary>A line a layout already kept off the ways, the fields and the keep-outs (LayAlong): only water, no ground and steepness stop it.</summary>
			public bool Planned;
			/// <summary>Where its modules are queued until their joints are known; its own (laid by <see cref="Report"/>) unless a caller's.</summary>
			private readonly Joins joins;
			private readonly bool ownJoins;
			private PathSegmentIndex ways;
			private readonly List<string> missing = new List<string>();
			private List<(Vector2 A, Vector2 B)> hedge;

			public BiomeTemplate Biome => biome;
			public BiomeLinearStyle Style => style;

			public Layer(PointOfInterestSiteContext context, BiomeTemplate biome, BiomeLinearStyle style, int seed, Joins joins = null)
			{
				this.context = context;
				this.biome = biome;
				this.style = style;
				this.seed = seed;
				roll = BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(seed, 3, 0, 0));
				ownJoins = joins == null;
				this.joins = joins ?? new Joins();
			}

			private PathSegmentIndex Ways()
			{
				if (ways == null)
				{
					var list = new List<ScenePath>();
					if (context.Points?.Paths != null)
					{
						foreach (ScenePath p in context.Points.Paths)
						{
							if (p != null && p != OwnWay && p.Class != ScenePathClass.Yard && p.Count >= 2)
							{
								list.Add(p);
							}
						}
					}
					ways = new PathSegmentIndex(list, p => p.MaxHalfWidth + WayGapMetres + 1f);
				}
				return ways;
			}

			/// <summary>True where a way's trodden width (plus <see cref="WayGapMetres"/>) covers a point.</summary>
			public bool OnWay(Vector2 p)
			{
				PathSegmentIndex index = Ways();
				List<(int path, int point)> near = index.Near(p.x, p.y);
				if (near == null)
				{
					return false;
				}
				foreach ((int path, int point) in near)
				{
					if (index.Distance(path, point, p.x, p.y, out float t) < index.HalfWidthAt(path, point, t) + WayGapMetres)
					{
						return true;
					}
				}
				return false;
			}

			/// <summary>True when a closed or open line crosses any way.</summary>
			public bool CrossesAnyWay(IReadOnlyList<Vector2> line)
			{
				foreach (Station st in Stations(line, 1f))
				{
					if (OnWay(st.At))
					{
						return true;
					}
				}
				return false;
			}

			/// <summary>Whether nothing may stand at a point: water, a way, another field, a keep-out, no ground, the scene's edge.</summary>
			private bool Blocked(Vector2 p)
			{
				float g = context.GroundAt != null ? context.GroundAt(p.x, p.y) : 0f;
				if (float.IsNaN(g) || float.IsInfinity(g) || context.SkyAt != null && context.SkyAt(p.x, p.y) || !context.InScene(p.x, p.y, 0f))
				{
					return true;
				}
				if (context.Water != null && context.Water.KindAt(p.x, p.y) != WaterKind.None || context.IsWet(p.x, p.y, 0.05f))
				{
					return true;
				}
				if (Planned)
				{
					return false;
				}
				if (!KeepOutsFree && OwnField == null && context.Points != null && context.Points.IsKeptOut(p.x, p.y))
				{
					return true;
				}
				return InClaims(context.FieldClaims, p, -0.3f, OwnField) || OnWay(p);
			}

			private float Ground(Vector2 p) => context.GroundAt != null ? context.GroundAt(p.x, p.y) : 0f;

			/// <summary>A joint on the line: on its seat where it has one, else sunk a little into the ground.</summary>
			private Vector3 Joint(Vector2 p) => Seat != null ? new Vector3(p.x, Seat(p), p.y) : new Vector3(p.x, Ground(p) - SinkMetres, p.y);

			/// <summary>Lays the style along one line, its gate (when <paramref name="gate"/>) in its middle module.</summary>
			public void Lay(IReadOnlyList<Vector2> line, bool gate)
			{
				lines++;
				switch (style.Kind)
				{
					case BiomeLinearKind.TreeRow:
						Trees(line);
						break;
					case BiomeLinearKind.Channel:
						// Channels are carried by their own machinery (LayChannels, LayDitch).
						break;
					default:
						Modules(line, gate);
						break;
				}
			}

			/// <summary>A wall's, fence's, hedge base's or clearance row's modules along a line, the gate in the gap it leaves.</summary>
			private void Modules(IReadOnlyList<Vector2> line, bool gate)
			{
				(string id, int variant) = StructurePieces.LinearPieceFor(style.Form, Broken || style.Breaks >= 0.25f, roll);
				StructurePiece piece = StructurePieces.Find(id);
				if (piece == null || !(piece.ModuleLength > 0f))
				{
					return;
				}
				List<Station> stations = Stations(line, piece.ModuleLength);
				int modules = stations.Count - 1;
				if (modules <= 0)
				{
					return;
				}
				float gateWidth = BiomeLinearFeatures.GateOf(style);
				int gateModule = (gate || GateAtStart) && gateWidth > 0f ? (GateAtStart ? 0 : GateModule(modules)) : -1;
				bool[] kept = Kept(modules, k => ModuleBlocked(stations[k].At, stations[k + 1].At), gateModule, seed ^ lines * 0x3141, style.Breaks + (Broken ? 0.35f : 0f));
				float yScale = BiomeLinearFeatures.HeightOf(style) / StructurePieces.LinearBuiltHeight(id);
				context.Palette = StructurePieces.LinearPalette(BiomeTemplateArt.StructurePalette(biome), style, id);
				for (int k = 0; k < modules; k++)
				{
					Vector2 a = stations[k].At, b = stations[k + 1].At;
					if (k == gateModule)
					{
						Gate(a, b);
						continue;
					}
					if (!kept[k])
					{
						continue;
					}
					int v = style.Form == BiomeLinearForm.Rail ? (BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(seed, k, lines, 7)) < 0.5f ? 0 : 1) : variant;
					GameObject prefab = StructureKit.Prefab(id, v);
					if (prefab == null)
					{
						Missing(id);
						return;
					}
					Place(prefab, id, v, a, b, piece.ModuleLength, yScale, Broken ? BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(seed, k, lines, 9)) * 14f - 7f : 0f);
					if (style.Kind == BiomeLinearKind.Hedge)
					{
						(hedge ??= new List<(Vector2, Vector2)>()).Add((a, b));
					}
				}
				if (style.Kind == BiomeLinearKind.Hedge)
				{
					Leaves();
					Standards(line);
				}
			}

			private bool ModuleBlocked(Vector2 a, Vector2 b)
			{
				if (Blocked(a) || Blocked(b) || Blocked(0.5f * (a + b)))
				{
					return true;
				}
				float run = (b - a).magnitude;
				return run > 1e-3f && Mathf.Abs(Ground(b) - Ground(a)) / run > MaxGrade;
			}

			/// <summary>
			/// A piece built along x queued between two joints (PointOfInterestLinearFeatures.Joins.cs): its ends on them, scaled along x to
			/// the chord and in y to the style's height; how far the ground dips under its middle below the chord goes with it, so a solid
			/// piece's joints are sunk to cover it (its neighbours' with them).
			/// </summary>
			private void Place(GameObject prefab, string id, int variant, Vector2 a, Vector2 b, float module, float yScale, float tilt)
			{
				Vector3 pa = Joint(a), pb = Joint(b);
				float dip = 0.5f * (pa.y + pb.y) - Joint(0.5f * (a + b)).y;
				joins.Module(context, context.Palette, prefab, id, variant, pa, pb, module, dip, yScale, tilt);
				Laid++;
			}

			/// <summary>A gate in the gap a gate module leaves: timber posts in a hedge or fence, stone piers in a wall or clearance row.</summary>
			private void Gate(Vector2 a, Vector2 b)
			{
				if (Blocked(0.5f * (a + b)))
				{
					return;
				}
				bool stone = style.Kind == BiomeLinearKind.Wall || style.Kind == BiomeLinearKind.ClearanceRow;
				GameObject prefab = StructureKit.Prefab("LinearGate", stone ? 1 : 0);
				if (prefab == null)
				{
					Missing("LinearGate");
					return;
				}
				// Its posts close the modules either side of it; one that cannot stand leaves them to their closing posts.
				joins.Gate(context, StructurePieces.LinearPalette(BiomeTemplateArt.StructurePalette(biome), style, "LinearGate"), prefab, Joint(a), Joint(b), StructurePieces.GateModule);
				Laid++;
			}

			/// <summary>A hedge's leaves: its bushes painted thick along every module laid, into the biome's own prototypes of them.</summary>
			private void Leaves()
			{
				if (hedge == null || hedge.Count == 0)
				{
					return;
				}
				var bushes = new List<GameObject>();
				foreach (GameObject p in style.Species ?? new List<GameObject>())
				{
					if (p != null)
					{
						bushes.Add(p);
					}
				}
				if (bushes.Count == 0)
				{
					context.Notes?.Add($"Linear features: {Label(biome)} {BiomeLinearFeatures.Id(style)} names no bushes: its hedges are bare stems.");
					return;
				}
				float half = style.Form == BiomeLinearForm.Wild ? 1.3f : style.Form == BiomeLinearForm.Clipped ? 0.7f : 0.85f;
				float core = style.Form == BiomeLinearForm.Wild ? 150f : style.Form == BiomeLinearForm.Clipped ? 235f : 200f;
				if (context.Terrains == null)
				{
					return;
				}
				foreach (Terrain terrain in context.Terrains)
				{
					PaintHedge(terrain, bushes, hedge, half, core);
				}
				hedge.Clear();
			}

			private void PaintHedge(Terrain terrain, List<GameObject> bushes, List<(Vector2 A, Vector2 B)> segments, float half, float core)
			{
				TerrainData data = terrain != null ? terrain.terrainData : null;
				if (data == null || data.detailResolution <= 0)
				{
					return;
				}
				Vector3 origin = terrain.transform.position, size = data.size;
				Rect tile = new Rect(origin.x, origin.z, size.x, size.z);
				float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
				foreach ((Vector2 a, Vector2 b) in segments)
				{
					minX = Mathf.Min(minX, Mathf.Min(a.x, b.x) - half);
					maxX = Mathf.Max(maxX, Mathf.Max(a.x, b.x) + half);
					minZ = Mathf.Min(minZ, Mathf.Min(a.y, b.y) - half);
					maxZ = Mathf.Max(maxZ, Mathf.Max(a.y, b.y) + half);
				}
				if (maxX < tile.xMin || minX > tile.xMax || maxZ < tile.yMin || minZ > tile.yMax)
				{
					return;
				}
				int res = data.detailResolution;
				float texelX = size.x / res, texelZ = size.z / res;
				int x0 = Mathf.Clamp(Mathf.FloorToInt((minX - origin.x) / texelX), 0, res - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((maxX - origin.x) / texelX), 0, res - 1);
				int z0 = Mathf.Clamp(Mathf.FloorToInt((minZ - origin.z) / texelZ), 0, res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - origin.z) / texelZ), 0, res - 1);
				int w = x1 - x0 + 1, h = z1 - z0 + 1;
				int count = bushes.Count;
				var layers = new PointOfInterestHerbGrove.PlantLayers[count];
				var maps = new int[count][,];
				var bases = new int[count][,];
				for (int s = 0; s < count; s++)
				{
					layers[s] = PointOfInterestHerbGrove.LayersOf(terrain, bushes[s], biome);
					if (layers[s].Own >= 0)
					{
						PointOfInterestHerbGrove.ReadMaps(data, x0, z0, w, h, layers[s], out maps[s], out bases[s]);
					}
				}
				float unit = Mathf.Max(1, data.maxDetailScatterPerRes) / 255f;
				int maxValue = Mathf.Max(1, data.maxDetailScatterPerRes);
				var changed = new bool[count];
				for (int j = 0; j < h; j++)
				{
					for (int i = 0; i < w; i++)
					{
						var p = new Vector2(origin.x + (x0 + i + 0.5f) * texelX, origin.z + (z0 + j + 0.5f) * texelZ);
						float best = float.MaxValue, along = 0f;
						foreach ((Vector2 a, Vector2 b) in segments)
						{
							float d = HedgeDistance(p, a, b, out float t);
							if (d < best)
							{
								best = d;
								along = Vector2.Lerp(a, b, t).x * 0.37f + Vector2.Lerp(a, b, t).y * 0.61f;
							}
						}
						if (best > half)
						{
							continue;
						}
						// One species a stretch of two or three metres, as a mixed hedge is planted.
						int s = (int)(BiomeLinearFeatures.Hash(seed, Mathf.FloorToInt(along / 2.6f), 0x4845, 0) % (uint)count);
						if (maps[s] == null)
						{
							continue;
						}
						float want = core * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((best - 0.35f * half) / (0.65f * half))));
						if (style.Form == BiomeLinearForm.Wild)
						{
							want *= 0.6f + 0.8f * BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(seed, Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), 0x57));
						}
						int value = Mathf.Clamp(Mathf.RoundToInt(want * unit), 0, maxValue);
						int grown = TerrainDetailBiomes.SiteCover(bases[s] != null ? bases[s][j, i] : 0, maps[s][j, i], bases[s] == null, value);
						if (grown != maps[s][j, i])
						{
							maps[s][j, i] = grown;
							changed[s] = true;
						}
					}
				}
				bool dirty = false;
				for (int s = 0; s < count; s++)
				{
					if (changed[s])
					{
						data.SetDetailLayer(x0, z0, layers[s].Own, maps[s]);
						dirty = true;
					}
				}
				if (dirty)
				{
					EditorUtility.SetDirty(data);
				}
			}

			/// <summary>A hedge's standard trees along a line, one every few spacings by the draws, where nothing stops them.</summary>
			private void Standards(IReadOnlyList<Vector2> line)
			{
				if (style.Standards == null || style.Standards.Count == 0)
				{
					return;
				}
				float spacing = Mathf.Max(8f, BiomeLinearFeatures.SpacingOf(style) * 18f);
				List<Station> stations = Stations(line, spacing);
				for (int k = 1; k < stations.Count - 1; k++)
				{
					uint h = BiomeLinearFeatures.Hash(seed, k, lines, 0x5354);
					if (BiomeLinearFeatures.Unit(h) > 0.6f || Blocked(stations[k].At))
					{
						continue;
					}
					Tree(style.Standards[(int)(h % (uint)style.Standards.Count)], stations[k].At, h, 1f);
				}
			}

			/// <summary>A row's trees along a line at the style's spacing, and a piantata's vines swagged between neighbours.</summary>
			private void Trees(IReadOnlyList<Vector2> line)
			{
				if (style.Species == null || style.Species.Count == 0)
				{
					context.Notes?.Add($"Linear features: {Label(biome)} {BiomeLinearFeatures.Id(style)} names no trees: no row laid.");
					return;
				}
				List<Station> stations = Stations(line, Mathf.Max(2f, BiomeLinearFeatures.SpacingOf(style)));
				Vector2? last = null;
				for (int k = 0; k < (Sides ? stations.Count - 1 : stations.Count); k++)
				{
					uint h = BiomeLinearFeatures.Hash(seed, k, lines, 0x524F57);
					Vector2 at = stations[k].At;
					if (Blocked(at) || style.Breaks > 0f && BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(seed, k, lines, 0x42)) < style.Breaks)
					{
						last = null;
						continue;
					}
					float scale = Mathf.Max(0.2f, style.Height > 0f ? style.Height : 1f);
					Tree(style.Species[(int)(h % (uint)style.Species.Count)], at, h, scale);
					if (style.Form == BiomeLinearForm.Piantata && last.HasValue)
					{
						Garland(last.Value, at, h);
					}
					last = at;
				}
			}

			private void Tree(GameObject prefab, Vector2 at, uint h, float scale)
			{
				if (prefab == null)
				{
					return;
				}
				if (!Instancable(prefab))
				{
					Missing(prefab.name + " (a tree that cannot be laid as a prop)");
					return;
				}
				float s = scale * (0.88f + 0.24f * BiomeLinearFeatures.Unit(BiomeLinearFeatures.Mix(h ^ 0x1234u)));
				context.AddProp(prefab, new Vector3(at.x, Ground(at) - 0.1f, at.y), Quaternion.Euler(0f, BiomeLinearFeatures.Unit(BiomeLinearFeatures.Mix(h ^ 0x9876u)) * 360f, 0f),
					Vector3.one * s);
				Laid++;
			}

			/// <summary>A vine curtain swagged between two trees of a piantata, its top at a garland's height, as wide as the gap.</summary>
			private void Garland(Vector2 a, Vector2 b, uint h)
			{
				string species = !string.IsNullOrEmpty(style.Vine) ? style.Vine : BiomeTemplateArt.Vines(biome)?.Pick(BiomeLinearFeatures.Unit(h));
				if (string.IsNullOrEmpty(species))
				{
					return;
				}
				var spot = new VineSpot { Species = species, Form = VineForm.Curtain, Size = "Fringe", Variant = (int)(h >> 5) & 1 };
				GameObject prefab = VinePlacement.PrefabFor(in spot);
				if (prefab == null)
				{
					Missing(spot.Prefab);
					return;
				}
				Vector2 mid = 0.5f * (a + b);
				Vector2 d = b - a;
				float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg - 90f;
				float width = d.magnitude * 0.85f;
				context.AddProp(prefab, new Vector3(mid.x, Ground(mid) + 2.6f, mid.y), Quaternion.Euler(0f, yaw, 0f), new Vector3(width / 2.2f, 0.75f, 1f));
				Laid++;
			}

			private void Missing(string what)
			{
				if (!missing.Contains(what))
				{
					missing.Add(what);
				}
			}

			/// <summary>Lays what it queued (its own joins), then one note for what this place laid, and one for what its art lacked.</summary>
			public void Report()
			{
				if (ownJoins && joins.Count > 0)
				{
					// Its modules were counted as they were queued: what closing adds is its posts.
					int queued = joins.Count;
					Laid += joins.Close() - queued;
					if (joins.MissingPosts > 0)
					{
						Missing(StructurePieces.LinearPost);
					}
				}
				if (missing.Count > 0)
				{
					context.Notes?.Add($"Linear features: {Label(biome)} {BiomeLinearFeatures.Id(style)}: not generated yet: {string.Join(", ", missing)} (Biome Tools → Generate biome art, then regenerate).");
				}
			}
		}

		/// <summary>A biome as the notes name it (its display name; a label, never a decision).</summary>
		private static string Label(BiomeTemplate biome) => biome != null ? biome.ResolvedDisplayName : "(no biome)";

		/// <summary>The distance from a point to a segment on the plan, and how far along it the nearest point lies (0..1).</summary>
		public static float HedgeDistance(Vector2 p, Vector2 a, Vector2 b, out float t)
		{
			Vector2 d = b - a;
			float len2 = d.sqrMagnitude;
			t = len2 > 1e-10f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / len2) : 0f;
			return (p - (a + d * t)).magnitude;
		}

		/// <summary>Whether a prefab can be laid as a baked prop (no Tree, billboard or skinned part), as the scatter's trees are baked.</summary>
		public static bool Instancable(GameObject prefab)
		{
			return prefab != null && prefab.GetComponentInChildren<Tree>(true) == null && prefab.GetComponentInChildren<BillboardRenderer>(true) == null
				&& prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) == null;
		}
	}
}
#endif
