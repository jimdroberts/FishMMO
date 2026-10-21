#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What stands out round a settlement beyond its houses and its farmsteads (2026-10-19: "beekeepers out in fields alone, and
	/// other things"): its lone holdings beside its fields and at its woods' edge, its wayside crosses along the roads out, its
	/// gallows on a rise by a road, and a city's cemetery outside its walls. Laid by PointOfInterestFields after its farmsteads.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The holdings.</b> A beekeeper's bothy and its hives in an arc before it beside a crop or a fallow field, apart from the
	/// farmsteads (<see cref="BeeYard"/>); a shepherd's hut and his fold beside a pasture; an orchard keeper's bothy beside an
	/// orchard; a woodcutter's bothy, his trunks, his woodpile and perhaps a saw pit, or a charcoal burner's clamp and lean-to, at
	/// the edge of the nearest wood. Each faces the nearest way, its yard trodden bare and a track out to the way, on gentle, dry,
	/// open ground clear of every field, farmstead and site. Its buildings are laid through PointOfInterestSiteContext.AddProp, so
	/// their doors are hung (PointOfInterestFarmsteads.HangLateDoors) and their rooms furnished as a settlement's are.
	/// </para>
	/// <para>
	/// <b>The woods</b> are the scene's forest stands (TerrainScatter.StandField: the same field the scatter grows the site's
	/// biome's forest rule in, from the scene's seed), searched outward along bearings from the site's edge for the first ground the
	/// stands cover; the woodcutter's yard is laid in the open just short of it. A biome that grows no trees in stands keeps no
	/// woodcutter.
	/// </para>
	/// <para>
	/// <b>Along the ways out</b> (the routed ways that leave the site, never its streets or yards): wayside crosses and shrines on a
	/// post beside the way, <see cref="WaysideFrom"/>–<see cref="WaysideTo"/> metres out, facing it; a town's gallows on the
	/// highest gentle ground beside a road out; a city's or the capital's cemetery beside a road a little way out, its graves in
	/// rows, its mausoleums along its back, a low wall round it with a lychgate to the road (<see cref="CemeteryYard"/>).
	/// </para>
	/// <para>
	/// <b>Nothing that was laid moves.</b> Every draw is a hash of the site's seed and a salt of this file's own, never the site's
	/// sequence; the fields and the farmsteads before are as they were. Each claims its ground (PointOfInterestFieldClaims) under the
	/// farmsteads' claim (<see cref="PointOfInterestFarmsteads.ClaimSite"/>), so a later site's fields keep off it. A piece the kit
	/// has no art for yet (its new holdings, 2026-10-19) is counted in the site's notes and left out; its ground is still claimed.
	/// </para>
	/// </remarks>
	public static class PointOfInterestOutskirts
	{
		/// <summary>What the outskirts hold.</summary>
		public enum Holding : byte { Beekeeper, Shepherd, OrchardKeeper, Woodcutter, WaysideCross, Gallows, Cemetery }

		// Salts of this file's own hash streams, one per holding (so a holding added later moves none of the others).
		private const int BeeSalt = 0x42454553, ShepherdSalt = 0x53484550, OrchardSalt = 0x4F524348, WoodSalt = 0x574F4F44,
			CrossSalt = 0x43524F53, GallowsSalt = 0x47414C4C, CemeterySalt = 0x43454D59, CountSalt = 0x434E5453, FinishSalt = 0x46494E4F;

		/// <summary>The steepest ground a holding's yard stands on, degrees; a gallows's rise may be a little steeper.</summary>
		public const float MaxSlopeDegrees = 6f, GallowsSlopeDegrees = 9f, WaysideSlopeDegrees = 16f;

		/// <summary>How much the ground may fall across one building, metres.</summary>
		public const float MaxFall = 1.2f;

		/// <summary>How far a lone holding keeps from a farmstead's ground besides the fields' gap, metres: it stands apart.</summary>
		public const float FarmsteadApart = 20f;

		/// <summary>How far out past the site's edge along a way a wayside cross stands, metres, and how far apart two stand.</summary>
		public const float WaysideFrom = 80f, WaysideTo = 400f, WaysideApart = 60f;

		/// <summary>How far out past the site's edge along a road a gallows and a cemetery stand, metres.</summary>
		public const float GallowsFrom = 60f, GallowsTo = 350f, CemeteryFrom = 30f, CemeteryTo = 260f;

		/// <summary>How far past the site's edge a woodcutter looks for a wood's margin, metres, and how far short of it his yard stands.</summary>
		public const float WoodReach = 320f, WoodStandOff = 12f;

		/// <summary>The open ground left round a holding's pieces inside its claim, metres, and the yard before them.</summary>
		public const float Margin = 1.5f, Apron = 3f;

		/// <summary>Where the kit has no piece yet, the footprints laid by (the pieces of 2026-10-19), metres.</summary>
		public static readonly Vector2 BothySize = new Vector2(5f, 4.5f), ShepherdHutSize = new Vector2(3.6f, 3.6f), SheepfoldSize = new Vector2(10f, 10f),
			GallowsSize = new Vector2(4.5f, 4.5f), WaysideCrossSize = new Vector2(1.6f, 1.6f);

		/// <summary>The fewest and most of a holding round a site of a kind.</summary>
		public static (int min, int max) CountFor(POIType kind, Holding holding)
		{
			bool village = kind == POIType.Village, town = kind == POIType.Town, city = kind == POIType.City || kind == POIType.Capital;
			bool monastery = kind == POIType.Monastery;
			switch (holding)
			{
				case Holding.Beekeeper:
					return village || town || city || monastery ? (1, 2) : (0, 0);
				case Holding.Shepherd:
					return city ? (1, 2) : village || town || monastery ? (1, 1) : (0, 0);
				case Holding.OrchardKeeper:
					return village || town || city || monastery ? (1, 2) : (0, 0);
				case Holding.Woodcutter:
					return city ? (1, 2) : village || town || monastery ? (1, 1) : (0, 0);
				// (2026-10-20) One or two to a settlement, as its other monuments (PointOfInterestStructureCaps).
				case Holding.WaysideCross:
					return village || town ? (1, 2) : city ? (2, 2) : (0, 0);
				case Holding.Gallows:
					return town ? (0, 1) : city ? (1, 1) : (0, 0);
				case Holding.Cemetery:
					return city ? (1, 1) : (0, 0);
			}
			return (0, 0);
		}

		/// <summary>How many of a holding a site keeps (pure: its seed and its kind).</summary>
		public static int Wanted(int siteSeed, POIType kind, Holding holding)
		{
			(int min, int max) = CountFor(kind, holding);
			return max <= 0 ? 0 : min + (int)(PointOfInterestPlanner.Hash(siteSeed, CountSalt, (int)holding, 0) % (uint)(max - min + 1));
		}

		// ── The layouts (pure) ────────────────────────────────────────

		/// <summary>One piece of a holding in its own frame (its way in at −z): the kit piece, where, turned how, and the ground it covers there.</summary>
		public struct Part
		{
			public string Id;
			public int Variant;
			public Vector2 At;
			/// <summary>Its yaw in the holding's frame, degrees: 0 its front (−z) to the way in.</summary>
			public float Yaw;
			/// <summary>The ground it covers in the holding's frame (its footprint turned by <see cref="Yaw"/>, as a box square to the frame).</summary>
			public Vector2 Extent;
			/// <summary>Its own footprint, unturned.</summary>
			public Vector2 Footprint;
			/// <summary>A building: a floor under it, seated to its foundation, its fall across it held to <see cref="MaxFall"/>.</summary>
			public bool Building;
		}

		/// <summary>A holding laid out: the ground it claims (its frame's x and z), its parts, and for a walled one its wall's runs and its gate.</summary>
		public sealed class Plan
		{
			public Vector2 Size;
			public readonly List<Part> Parts = new List<Part>();
			public readonly List<(Vector2 A, Vector2 B)> Runs = new List<(Vector2, Vector2)>();
			public Vector2 Gate;
		}

		/// <summary>A part with its ground in the holding's frame (its footprint turned by a quarter turn's multiple, or any yaw by its turned box).</summary>
		public static Part MakePart(string id, int variant, Vector2 footprint, float yaw, bool building)
		{
			float a = yaw * Mathf.Deg2Rad, c = Mathf.Abs(Mathf.Cos(a)), s = Mathf.Abs(Mathf.Sin(a));
			return new Part
			{
				Id = id,
				Variant = variant,
				Yaw = yaw,
				Footprint = footprint,
				Extent = new Vector2(footprint.x * c + footprint.y * s, footprint.x * s + footprint.y * c),
				Building = building,
			};
		}

		/// <summary>
		/// Parts side by side along x (pure), each with its back to the holding's back, <paramref name="gap"/> apart, an
		/// <see cref="Apron"/> of yard before them and <see cref="Margin"/> round them: a shepherd's hut and fold, an orchard keeper's
		/// bothy and woodpile, a woodcutter's bothy and his piles.
		/// </summary>
		public static Plan Row(IReadOnlyList<Part> parts, float gap)
		{
			var plan = new Plan();
			float width = 0f, depth = 0f;
			for (int i = 0; i < parts.Count; i++)
			{
				width += parts[i].Extent.x + (i > 0 ? gap : 0f);
				depth = Mathf.Max(depth, parts[i].Extent.y);
			}
			plan.Size = new Vector2(width + 2f * Margin, depth + 2f * Margin + Apron);
			float x = -width * 0.5f, back = plan.Size.y * 0.5f - Margin;
			foreach (Part p in parts)
			{
				Part placed = p;
				placed.At = new Vector2(x + p.Extent.x * 0.5f, back - p.Extent.y * 0.5f);
				plan.Parts.Add(placed);
				x += p.Extent.x + gap;
			}
			plan.Gate = new Vector2(0f, -plan.Size.y * 0.5f);
			return plan;
		}

		/// <summary>
		/// A beekeeper's holding (pure): his bothy with its door to the way in, <paramref name="hives"/> (2..4) apiaries in an arc before
		/// it to either side of the way to its door, each facing out, far enough apart that no two benches touch; the whole centred in
		/// its claim with <see cref="Margin"/> round it.
		/// </summary>
		public static Plan BeeYard(Vector2 bothy, Vector2 apiary, int hives, int variant, int apiaryVariant)
		{
			hives = Mathf.Clamp(hives, 2, 4);
			float need = apiary.magnitude + 0.5f;
			// Alternately right and left of the door, 30° off it, the next pair further round.
			const float First = 30f;
			float step = 38f;
			float radius = Mathf.Max(bothy.magnitude * 0.5f + apiary.magnitude * 0.5f + 0.8f, need / (2f * Mathf.Sin(step * 0.5f * Mathf.Deg2Rad)));
			radius = Mathf.Max(radius, need / (2f * Mathf.Sin(First * Mathf.Deg2Rad)));
			var parts = new List<Part> { MakePart("Bothy", variant, bothy, 0f, true) };
			for (int i = 0; i < hives; i++)
			{
				float side = (i & 1) == 0 ? 1f : -1f;
				float angle = side * (First + (i / 2) * step);
				float a = angle * Mathf.Deg2Rad;
				Part hive = MakePart("Apiary", apiaryVariant, apiary, -angle, false);
				hive.At = new Vector2(radius * Mathf.Sin(a), -radius * Mathf.Cos(a));
				parts.Add(hive);
			}
			// Centred in its ground.
			Vector2 lo = new Vector2(float.MaxValue, float.MaxValue), hi = new Vector2(float.MinValue, float.MinValue);
			foreach (Part p in parts)
			{
				lo = Vector2.Min(lo, p.At - p.Extent * 0.5f);
				hi = Vector2.Max(hi, p.At + p.Extent * 0.5f);
			}
			Vector2 middle = 0.5f * (lo + hi);
			var plan = new Plan { Size = hi - lo + Vector2.one * (2f * Margin) };
			foreach (Part p in parts)
			{
				Part moved = p;
				moved.At -= middle;
				plan.Parts.Add(moved);
			}
			plan.Gate = new Vector2(-middle.x, -plan.Size.y * 0.5f);
			return plan;
		}

		/// <summary>The grave piece a cemetery's grave is and whether it is left a gap (pure: the cemetery's seed and the grave's place).</summary>
		private static (bool Gap, bool Mound, float Lean) Grave(int seed, int row, int column)
		{
			float gap = PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(seed, CemeterySalt, row * 131 + column, 1));
			float mound = PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(seed, CemeterySalt, row * 131 + column, 2));
			float lean = PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(seed, CemeterySalt, row * 131 + column, 3));
			return (gap < 0.12f, mound < 0.15f, (lean - 0.5f) * 8f);
		}

		/// <summary>
		/// A cemetery outside a city's walls (pure; its front, −z, to the road): <paramref name="size"/> of ground walled round, the
		/// lychgate in the middle of the front, a walk from it up the middle to the back; rows of graves either side of the walk, all
		/// facing the road, a gap where an old grave has gone and a plain mound among them; <paramref name="mausoleums"/> (0..3) along
		/// the back, facing the gate.
		/// </summary>
		public static Plan CemeteryYard(Vector2 size, Vector2 grave, Vector2 mound, Vector2 mausoleum, Vector2 lychgate, int mausoleums, int seed)
		{
			var plan = new Plan { Size = size };
			float hx = size.x * 0.5f, hz = size.y * 0.5f;
			int count = Mathf.Clamp(mausoleums, 0, 3);
			while (count > 1 && count * (mausoleum.x + 1.5f) + 1f > size.x - 2f)
			{
				count--;
			}
			float mz = hz - 1.2f - mausoleum.y * 0.5f;
			for (int m = 0; m < count; m++)
			{
				// Evenly along the back, the middle one (of three) across the head of the walk.
				Part tomb = MakePart("Mausoleum", (int)(PointOfInterestPlanner.Hash(seed, CemeterySalt, m, 9) & 1u), mausoleum, 0f, true);
				tomb.At = new Vector2(-hx + (m + 0.5f) * size.x / count, mz);
				plan.Parts.Add(tomb);
			}
			float rowsFrom = -hz + lychgate.y * 0.5f + 2.5f;
			float rowsTo = count > 0 ? mz - mausoleum.y * 0.5f - 1.2f : hz - 1.2f;
			const float Across = 1.7f, Along = 2.3f, Walk = 1.5f;
			int row = 0;
			for (float z = rowsFrom + grave.y * 0.5f; z + grave.y * 0.5f <= rowsTo; z += Along, row++)
			{
				int column = 0;
				for (float x = -hx + 1.2f + grave.x * 0.5f; x + grave.x * 0.5f <= hx - 1.2f; x += Across, column++)
				{
					if (Mathf.Abs(x) - Mathf.Max(grave.x, mound.x) * 0.5f < Walk)
					{
						continue;
					}
					(bool gap, bool isMound, float lean) = Grave(seed, row, column);
					if (gap)
					{
						continue;
					}
					Part g = MakePart(isMound ? "GraveMound" : "Gravestone", (int)(PointOfInterestPlanner.Hash(seed, CemeterySalt, row * 131 + column, 4) % 6u),
						isMound ? mound : grave, lean, false);
					g.At = new Vector2(x, z);
					plan.Parts.Add(g);
				}
			}
			plan.Gate = new Vector2(0f, -hz);
			float gateHalf = lychgate.x * 0.5f;
			plan.Runs.Add((new Vector2(gateHalf, -hz), new Vector2(hx, -hz)));
			plan.Runs.Add((new Vector2(hx, -hz), new Vector2(hx, hz)));
			plan.Runs.Add((new Vector2(hx, hz), new Vector2(-hx, hz)));
			plan.Runs.Add((new Vector2(-hx, hz), new Vector2(-hx, -hz)));
			plan.Runs.Add((new Vector2(-hx, -hz), new Vector2(-gateHalf, -hz)));
			return plan;
		}

		// ── Along the ways (pure) ─────────────────────────────────────

		/// <summary>The yaw (degrees) that turns a piece's front (its −z) to look along <paramref name="front"/> (world x, z).</summary>
		public static float FacingYaw(Vector2 front) => Mathf.Atan2(-front.x, -front.y) * Mathf.Rad2Deg;

		/// <summary>A way's line in plan from the site's end outward (pure): its points in order from the end that is the site's.</summary>
		public static List<Vector2> Outward(IReadOnlyList<Vector3> points, bool fromStart)
		{
			var line = new List<Vector2>();
			if (points == null)
			{
				return line;
			}
			for (int i = 0; i < points.Count; i++)
			{
				Vector3 p = points[fromStart ? i : points.Count - 1 - i];
				line.Add(new Vector2(p.x, p.z));
			}
			return line;
		}

		/// <summary>
		/// The point on a way's line (from its site's end, <see cref="Outward"/>) where it first stands <paramref name="outMetres"/> past
		/// the site's edge (<paramref name="reach"/>: the edge's distance from <paramref name="site"/> toward a point), the way's direction
		/// there, and the half width index (the point after it). Pure. False when the way never gets that far out.
		/// </summary>
		public static bool AtOut(IReadOnlyList<Vector2> line, Vector2 site, Func<Vector2, float> reach, float outMetres, out Vector2 at, out Vector2 along, out int index)
		{
			at = default;
			along = Vector2.up;
			index = -1;
			if (line == null || line.Count < 2)
			{
				return false;
			}
			float Out(Vector2 p) => (p - site).magnitude - reach(p);
			float before = Out(line[0]);
			for (int i = 1; i < line.Count; i++)
			{
				float now = Out(line[i]);
				if (now >= outMetres && before < outMetres)
				{
					float t = Mathf.Clamp01((outMetres - before) / Mathf.Max(1e-4f, now - before));
					at = Vector2.Lerp(line[i - 1], line[i], t);
					Vector2 d = line[i] - line[i - 1];
					along = d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.up;
					index = i;
					return true;
				}
				before = now;
			}
			return false;
		}

		/// <summary>One spot beside a way: where, facing it, on which way and how far out.</summary>
		public struct WaySpot
		{
			public Vector2 At;
			public float Yaw;
			public int Way;
			public float Out;
		}

		/// <summary>
		/// The spots a site's wayside crosses are tried at, in order (pure): a way drawn by the site's seed, a distance out drawn between
		/// <see cref="WaysideFrom"/> and <see cref="WaysideTo"/>, a side; set <paramref name="offset"/> off the way's middle line (past its
		/// trodden edge), facing it. A spot whose way never reaches so far out is skipped.
		/// </summary>
		public static List<WaySpot> WaysideSpots(IReadOnlyList<List<Vector2>> lines, IReadOnlyList<float> offset, Vector2 site, Func<Vector2, float> reach, int seed, int attempts)
		{
			var spots = new List<WaySpot>();
			if (lines == null || lines.Count == 0)
			{
				return spots;
			}
			for (int k = 0; k < attempts; k++)
			{
				int way = (int)(PointOfInterestPlanner.Hash(seed, CrossSalt, k, 1) % (uint)lines.Count);
				float outMetres = Mathf.Lerp(WaysideFrom, WaysideTo, PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(seed, CrossSalt, k, 2)));
				float side = (PointOfInterestPlanner.Hash(seed, CrossSalt, k, 3) & 1u) == 0 ? 1f : -1f;
				if (!AtOut(lines[way], site, reach, outMetres, out Vector2 at, out Vector2 along, out _))
				{
					continue;
				}
				Vector2 normal = PointOfInterestLinearFeatures.Left(along) * side;
				spots.Add(new WaySpot { At = at + normal * offset[way], Yaw = FacingYaw(-normal), Way = way, Out = outMetres });
			}
			return spots;
		}

		// ── The scene ─────────────────────────────────────────────────

		/// <summary>
		/// A worn earth strip from <paramref name="a"/> to <paramref name="b"/>, <paramref name="half"/> metres either side, as a yard of
		/// the site <paramref name="toId"/> (the path field draws it; a floor keeps the grass and the trees out).
		/// </summary>
		public static void Track(PointOfInterestSiteContext context, Vector2 a, Vector2 b, float half, float wear, ScenePathPointFlags shape, int toId)
		{
			if (context?.Points?.Paths == null || context.GroundAt == null)
			{
				return;
			}
			int id = 0;
			foreach (ScenePath existing in context.Points.Paths)
			{
				id = Math.Max(id, existing != null ? existing.Id : 0);
			}
			byte flags = (byte)(ScenePathPointFlags.InSite | shape);
			context.Points.Paths.Add(new ScenePath
			{
				Id = id + 1,
				Class = ScenePathClass.Yard,
				FromId = -1,
				ToId = toId,
				Points = new[] { new Vector3(a.x, context.GroundAt(a.x, a.y), a.y), new Vector3(b.x, context.GroundAt(b.x, b.y), b.y) },
				HalfWidth = new[] { half, half },
				Wear = new[] { wear, wear },
				Surface = new[] { (byte)ScenePathSurface.Earth, (byte)ScenePathSurface.Earth },
				Flags = new[] { flags, flags },
			});
		}

		/// <summary>
		/// Lays a site's outskirts beside its laid fields and along its ways out (see the class remarks), then hangs the doors they
		/// added. Returns how many holdings, crosses, gallows and cemeteries were laid.
		/// </summary>
		public static int Lay(PointOfInterestSiteContext context, IReadOnlyList<PointOfInterestFarmsteads.LaidField> fields)
		{
			PointOfInterestRecord record = context?.Record;
			if (record == null || context.GroundAt == null || fields == null)
			{
				return 0;
			}
			bool any = false;
			foreach (Holding holding in (Holding[])Enum.GetValues(typeof(Holding)))
			{
				any |= CountFor(record.Kind, holding).max > 0;
			}
			// Herders under hide keep no bothies; a site of no kind with outskirts lays none.
			if (!any || string.Equals(context.Style ?? string.Empty, PointOfInterestTemplateSpecs.Hide, StringComparison.OrdinalIgnoreCase))
			{
				return 0;
			}
			int firstDoor = context.Doors.Count;
			var lay = new Outer(context, fields);
			// The largest first, while the roads' frontage is free: the cemetery, the gallows; then the holdings; the crosses last.
			int laid = lay.Cemeteries() + lay.Gallows() + lay.Beekeepers() + lay.Shepherds() + lay.OrchardKeepers() + lay.Woodcutters() + lay.Crosses();
			PointOfInterestFarmsteads.HangLateDoors(context, firstDoor);
			if (laid > 0 || lay.Missing.Count > 0)
			{
				context.Notes?.Add($"{record.Name}: its outskirts: {lay.Summary()}" +
					(lay.Missing.Count > 0 ? $"; no art yet for {string.Join(", ", lay.Missing)} (regenerate the structure art)." : "."));
			}
			return laid;
		}

		/// <summary>One site's outskirts as they are laid: the ground's tests, the pieces and what was claimed.</summary>
		private sealed class Outer
		{
			private readonly PointOfInterestSiteContext context;
			private readonly PointOfInterestRecord record;
			private readonly IReadOnlyList<PointOfInterestFarmsteads.LaidField> fields;
			private readonly float stature;
			private readonly PathExclusion ways;
			private readonly List<ScenePath> routes = new List<ScenePath>();
			/// <summary>The routed ways that leave the site, each from its end at the site outward.</summary>
			private readonly List<(List<Vector2> Line, ScenePath Path)> leaving = new List<(List<Vector2>, ScenePath)>();
			private readonly List<(Vector2 Centre, float Yaw, Vector2 Size)> placed = new List<(Vector2, float, Vector2)>();
			/// <summary>The other sites near enough to matter, for keeping out of their outlines.</summary>
			private readonly List<PointOfInterestRecord> neighbours = new List<PointOfInterestRecord>();
			private readonly bool allowLocal;
			private readonly StructureFinish finish;
			private readonly Vector2 site;
			public readonly SortedSet<string> Missing = new SortedSet<string>(StringComparer.Ordinal);
			private readonly int[] laid = new int[7];

			public Outer(PointOfInterestSiteContext context, IReadOnlyList<PointOfInterestFarmsteads.LaidField> fields)
			{
				this.context = context;
				record = context.Record;
				this.fields = fields;
				stature = Mathf.Max(0.5f, RaceStature.BuildingScale(record.Race));
				site = new Vector2(record.Position.x, record.Position.z);
				if (context.Points?.Paths != null)
				{
					foreach (ScenePath path in context.Points.Paths)
					{
						if (path == null || path.Class == ScenePathClass.Yard || path.Class == ScenePathClass.Street || path.Count < 2)
						{
							continue;
						}
						routes.Add(path);
						if (path.FromId == record.Id || path.ToId == record.Id)
						{
							leaving.Add((Outward(path.Points, path.FromId == record.Id), path));
						}
					}
				}
				ways = new PathExclusion(routes);
				if (context.Points?.Points != null)
				{
					foreach (PointOfInterestRecord other in context.Points.Points)
					{
						if (other != null && other.Id != record.Id && (new Vector2(other.Position.x, other.Position.z) - site).magnitude < other.Radius + record.Radius + WoodReach + WaysideTo + 200f)
						{
							neighbours.Add(other);
						}
					}
				}
				allowLocal = context.Scene.IsValid() && LocalArtScope.IsLocalScenePath(context.Scene.path);
				// Weathered as the site's own pieces are, by its props feature's finish.
				finish = StructureFinish.None;
				BiomeTemplate biome = context.BiomeAt?.Invoke(record.Position.x, record.Position.z);
				if (context.Template?.Features != null)
				{
					foreach (PointOfInterestFeature feature in context.Template.Features)
					{
						if (feature is PropsFeature props)
						{
							finish = PropsFeature.FinishFor(props.Finish, props.FinishChance, record.Kind, BiomeTemplateArt.Weathering(biome), 0f,
								PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(record.SiteSeed, FinishSalt, 0, 0)));
							break;
						}
					}
				}
			}

			private float U(int salt, int a, int b) => PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(record.SiteSeed, salt, a, b));

			public string Summary()
			{
				string[] names = { "beekeeper(s)", "shepherd(s)", "orchard keeper(s)", "woodcutter(s) or collier(s)", "wayside cross(es)", "gallows", "cemetery" };
				var parts = new List<string>();
				for (int i = 0; i < laid.Length; i++)
				{
					if (laid[i] > 0)
					{
						parts.Add($"{laid[i]} {names[i]}");
					}
				}
				return parts.Count > 0 ? string.Join(", ", parts) : "nothing laid";
			}

			// ── Pieces ───────────────────────────────────────────────

			private static Vector2 FootprintOf(string id, Vector2 fallback)
			{
				StructurePiece piece = StructurePieces.Find(id);
				return piece != null ? piece.Footprint : fallback;
			}

			private static int VariantOf(string id, float roll)
			{
				StructurePiece piece = StructurePieces.Find(id);
				return piece != null ? Mathf.Clamp((int)(roll * piece.Variants), 0, piece.Variants - 1) : 0;
			}

			/// <summary>A kit piece's prefab in the site's finish (the plain one where that finish has none); null, counted, where the kit has no such piece or no art.</summary>
			private GameObject Kit(string id, int variant, bool weathered = true)
			{
				if (StructurePieces.Find(id) == null)
				{
					Missing.Add(id);
					return null;
				}
				StructureFinish withFinish = weathered ? finish : StructureFinish.None;
				GameObject prefab = StructureKit.Prefab(id, variant, withFinish, allowLocal);
				if (prefab == null && withFinish != StructureFinish.None)
				{
					prefab = StructureKit.Prefab(id, variant, StructureFinish.None, allowLocal);
				}
				if (prefab == null)
				{
					Missing.Add(id);
				}
				return prefab;
			}

			/// <summary>A piece seated as a settlement's are (PropsFeature.SeatHeight): no higher than its centre's ground, low enough that its foundation reaches its lowest corner.</summary>
			private void Seat(GameObject prefab, string id, Vector2 at, float yaw, Vector2 footprint)
			{
				if (prefab == null)
				{
					return;
				}
				StructurePiece piece = StructurePieces.Find(id);
				float depth = (piece != null ? piece.Depth : 0.3f) * stature;
				var corners = new Vector2[4];
				PointOfInterestLayouts.Corners(at, yaw, footprint, corners);
				float centre = context.GroundAt(at.x, at.y), lowest = centre;
				foreach (Vector2 c in corners)
				{
					lowest = Mathf.Min(lowest, context.GroundAt(c.x, c.y));
				}
				context.AddProp(prefab, new Vector3(at.x, Mathf.Min(centre, lowest + Mathf.Max(0f, depth) * 0.9f), at.y), Quaternion.Euler(0f, yaw, 0f), Vector3.one * stature);
			}

			/// <summary>A building's plan as a floor (its grass and trees kept out of it) and an apron round it.</summary>
			private void Floor(Vector2 at, float yaw, Vector2 footprint)
			{
				bool alongX = footprint.x >= footprint.y;
				Vector2 axis = PointOfInterestLayouts.Rotate(alongX ? Vector2.right : Vector2.up, yaw);
				float longest = Mathf.Max(footprint.x, footprint.y), shortest = Mathf.Min(footprint.x, footprint.y);
				Track(context, at - axis * (longest * 0.5f + 0.5f), at + axis * (longest * 0.5f + 0.5f), shortest * 0.5f + 0.5f, 0.85f, ScenePathPointFlags.Squared, record.Id);
				Track(context, at - axis * (longest * 0.5f), at + axis * (longest * 0.5f), shortest * 0.5f, 1f, ScenePathPointFlags.Squared | ScenePathPointFlags.Floor, record.Id);
			}

			// ── Ground ───────────────────────────────────────────────

			/// <summary>
			/// Whether a holding's ground lies clear: off every other site's fields and the site's own, apart from its farmsteads and
			/// what the outskirts laid, and on ground no steeper than <paramref name="maxSlope"/>, dry, open, off the ways (or, for a
			/// small piece beside one, only off its trodden width), outside every site.
			/// </summary>
			public bool Fits(Vector2 centre, float yaw, Vector2 size, float maxSlope, float apart, bool besideWay = false)
			{
				int claim = PointOfInterestFarmsteads.ClaimSite(record.Id);
				if (context.FieldClaims != null)
				{
					if (!context.FieldClaims.Clear(claim, centre, yaw, size))
					{
						return false;
					}
					// The farmsteads' ground and this builder's own are claimed under the same claim, which Clear passes over.
					foreach (PointOfInterestFieldClaims.Claim c in context.FieldClaims.All)
					{
						if (c.Site == claim && PointOfInterestLayouts.Overlaps(centre, yaw, size + Vector2.one * (PointOfInterestFieldClaims.Gap + apart), c.Centre, c.Yaw, c.Size))
						{
							return false;
						}
					}
				}
				Vector2 grown = size + Vector2.one * PointOfInterestFieldClaims.Gap;
				foreach (PointOfInterestFarmsteads.LaidField f in fields)
				{
					if (PointOfInterestLayouts.Overlaps(centre, yaw, grown, f.Centre, f.Yaw, f.Size + Vector2.one * PointOfInterestFieldClaims.Gap))
					{
						return false;
					}
				}
				foreach ((Vector2 c, float y, Vector2 s) in placed)
				{
					if (PointOfInterestLayouts.Overlaps(centre, yaw, grown, c, y, s))
					{
						return false;
					}
				}
				Vector2 ax = PointOfInterestLayouts.Rotate(Vector2.right, yaw), az = PointOfInterestLayouts.Rotate(Vector2.up, yaw);
				int nx = Mathf.Max(2, Mathf.CeilToInt(size.x / 4f)), nz = Mathf.Max(2, Mathf.CeilToInt(size.y / 4f));
				for (int i = 0; i <= nx; i++)
				{
					for (int j = 0; j <= nz; j++)
					{
						Vector2 p = centre + ax * ((i / (float)nx - 0.5f) * size.x) + az * ((j / (float)nz - 0.5f) * size.y);
						float ground = context.GroundAt(p.x, p.y);
						if (float.IsNaN(ground) || float.IsInfinity(ground) || context.SkyAt != null && context.SkyAt(p.x, p.y))
						{
							return false;
						}
						if (context.SlopeAt != null && context.SlopeAt(p.x, p.y) > maxSlope)
						{
							return false;
						}
						if (context.Water != null && context.Water.KindAt(p.x, p.y) != WaterKind.None || context.IsWet(p.x, p.y, 0.6f))
						{
							return false;
						}
						if (context.Points != null && context.Points.IsKeptOut(p.x, p.y) || (besideWay ? OnAWay(p, 0.6f) : ways.Trees(p.x, p.y)))
						{
							return false;
						}
						if (InASite(p))
						{
							return false;
						}
					}
				}
				return true;
			}

			/// <summary>Whether a point lies inside this site's outline or a neighbour's (a little past it).</summary>
			private bool InASite(Vector2 p)
			{
				if (Inside(record, p))
				{
					return true;
				}
				foreach (PointOfInterestRecord other in neighbours)
				{
					if (Inside(other, p))
					{
						return true;
					}
				}
				return false;
			}

			private static bool Inside(PointOfInterestRecord r, Vector2 p)
			{
				Vector2 d = p - new Vector2(r.Position.x, r.Position.z);
				float bearing = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
				return d.magnitude < PointOfInterestOutline.ReachToward(r.Outline, r.Radius, bearing) * 1.05f + 6f;
			}

			/// <summary>The site's edge's distance from its anchor toward a point, metres.</summary>
			private float Reach(Vector2 p)
			{
				Vector2 d = p - site;
				return PointOfInterestOutline.ReachToward(record.Outline, record.Radius, Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg);
			}

			/// <summary>Whether a point lies within <paramref name="pad"/> metres of a routed way's trodden width.</summary>
			private bool OnAWay(Vector2 p, float pad)
			{
				foreach (ScenePath path in routes)
				{
					for (int i = 0; i + 1 < path.Count; i++)
					{
						Vector3 a3 = path.Points[i], b3 = path.Points[i + 1];
						var a = new Vector2(a3.x, a3.z);
						Vector2 ab = new Vector2(b3.x, b3.z) - a;
						float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
						float half = path.HalfWidth != null && path.HalfWidth.Length == path.Count ? Mathf.Lerp(path.HalfWidth[i], path.HalfWidth[i + 1], t) : 1f;
						if ((a + ab * t - p).magnitude < half + pad)
						{
							return true;
						}
					}
				}
				return false;
			}

			/// <summary>The half width of a way at its point <paramref name="index"/> (its widest where it has no widths).</summary>
			private static float HalfWidthAt(ScenePath path, int index)
			{
				if (path.HalfWidth == null || path.HalfWidth.Length == 0)
				{
					return ScenePathStyle.For(path.Class).HalfWidth;
				}
				return path.HalfWidth[Mathf.Clamp(index, 0, path.HalfWidth.Length - 1)];
			}

			/// <summary>The nearest point on a routed way within <paramref name="reach"/> metres of <paramref name="p"/>.</summary>
			private bool NearestWay(Vector2 p, float reach, out Vector2 nearest)
			{
				nearest = p;
				float best = reach;
				bool found = false;
				foreach (ScenePath path in routes)
				{
					for (int i = 0; i + 1 < path.Count; i++)
					{
						Vector3 a3 = path.Points[i], b3 = path.Points[i + 1];
						var a = new Vector2(a3.x, a3.z);
						Vector2 ab = new Vector2(b3.x, b3.z) - a;
						float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
						Vector2 q = a + ab * t;
						float d = (q - p).magnitude;
						if (d < best)
						{
							best = d;
							nearest = q;
							found = true;
						}
					}
				}
				return found;
			}

			/// <summary>Whether a track may run straight from a holding's gate to a way: dry, gentle, off every field and every claim but its own.</summary>
			private bool OpenBetween(Vector2 a, Vector2 b, Vector2 own)
			{
				float length = (b - a).magnitude;
				int steps = Mathf.Max(1, Mathf.CeilToInt(length / 2f));
				for (int i = 1; i < steps; i++)
				{
					Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
					if (context.IsWet(p.x, p.y, 0.2f) || context.SlopeAt != null && context.SlopeAt(p.x, p.y) > 14f)
					{
						return false;
					}
					foreach (PointOfInterestFarmsteads.LaidField f in fields)
					{
						if (PointOfInterestPaddies.Inside(f.Centre, f.Yaw, f.Size, p.x, p.y) > -1f)
						{
							return false;
						}
					}
					if (context.FieldClaims != null)
					{
						foreach (PointOfInterestFieldClaims.Claim claim in context.FieldClaims.All)
						{
							if (claim.Centre != own && PointOfInterestPaddies.Inside(claim.Centre, claim.Yaw, claim.Size, p.x, p.y) > -1f)
							{
								return false;
							}
						}
					}
				}
				return true;
			}

			private void Claim(Vector2 centre, float yaw, Vector2 size)
			{
				placed.Add((centre, yaw, size));
				context.FieldClaims?.Add(PointOfInterestFarmsteads.ClaimSite(record.Id), centre, yaw, size);
			}

			/// <summary>The quarter turn of <paramref name="frame"/> whose −z looks most toward <paramref name="to"/> from <paramref name="from"/>.</summary>
			private static float Facing(float frame, Vector2 from, Vector2 to)
			{
				float best = float.NegativeInfinity, yaw = frame;
				for (int q = 0; q < 4; q++)
				{
					float y = frame + q * 90f;
					float facing = Vector2.Dot(-PointOfInterestLayouts.Rotate(Vector2.up, y), to - from);
					if (facing > best)
					{
						best = facing;
						yaw = y;
					}
				}
				return yaw;
			}

			/// <summary>
			/// A spot beside one of the site's fields that <paramref name="uses"/> admits, drawn by <paramref name="salt"/>,
			/// <paramref name="k"/> and <paramref name="attempt"/>: square to the field, a field's gap and <paramref name="apart"/> off one
			/// of its sides, its way in (−z) turned toward the nearest way, on ground that fits.
			/// </summary>
			private bool BesideAField(Func<PointOfInterestFarmsteads.LaidField, bool> uses, int salt, int k, int attempt, Vector2 size, out Vector2 centre, out float yaw)
			{
				centre = default;
				yaw = 0f;
				var admitted = new List<PointOfInterestFarmsteads.LaidField>();
				foreach (PointOfInterestFarmsteads.LaidField f in fields)
				{
					if (uses(f))
					{
						admitted.Add(f);
					}
				}
				if (admitted.Count == 0)
				{
					return false;
				}
				int key = k * 131 + attempt;
				PointOfInterestFarmsteads.LaidField field = admitted[(int)(PointOfInterestPlanner.Hash(record.SiteSeed, salt, key, 3) % (uint)admitted.Count)];
				int side = (int)(PointOfInterestPlanner.Hash(record.SiteSeed, salt, key, 4) & 3);
				float along = U(salt, key, 5) - 0.5f;
				Vector2 ax = PointOfInterestLayouts.Rotate(Vector2.right, field.Yaw), az = PointOfInterestLayouts.Rotate(Vector2.up, field.Yaw);
				Vector2 normal = side == 0 ? az : side == 1 ? -az : side == 2 ? ax : -ax;
				Vector2 tangent = side < 2 ? ax : az;
				float halfNormal = side < 2 ? field.Size.y * 0.5f : field.Size.x * 0.5f;
				float halfTangent = side < 2 ? field.Size.x * 0.5f : field.Size.y * 0.5f;
				Vector2 edge = field.Centre + normal * halfNormal + tangent * (along * halfTangent);
				Vector2 toward = NearestWay(edge, 150f, out Vector2 way) ? way : site;
				yaw = Facing(field.Yaw, edge, toward);
				Vector2 fx = PointOfInterestLayouts.Rotate(Vector2.right, yaw), fz = PointOfInterestLayouts.Rotate(Vector2.up, yaw);
				float reach = Mathf.Abs(Vector2.Dot(normal, fx)) * size.x * 0.5f + Mathf.Abs(Vector2.Dot(normal, fz)) * size.y * 0.5f;
				centre = edge + normal * (PointOfInterestFieldClaims.Gap + 1f + reach);
				return true;
			}

			/// <summary>Whether each building of a plan stands on ground falling no more than <see cref="MaxFall"/> across it.</summary>
			private bool BuildingsFit(Plan plan, Vector2 centre, float yaw)
			{
				var corners = new Vector2[4];
				foreach (Part part in plan.Parts)
				{
					if (!part.Building)
					{
						continue;
					}
					Vector2 at = centre + PointOfInterestLayouts.Rotate(part.At, yaw);
					PointOfInterestLayouts.Corners(at, yaw + part.Yaw, part.Footprint, corners);
					float lo = context.GroundAt(at.x, at.y), hi = lo;
					foreach (Vector2 c in corners)
					{
						float g = context.GroundAt(c.x, c.y);
						lo = Mathf.Min(lo, g);
						hi = Mathf.Max(hi, g);
					}
					if (hi - lo > MaxFall * stature)
					{
						return false;
					}
				}
				return true;
			}

			/// <summary>Lays a planned holding at <paramref name="centre"/> turned to <paramref name="yaw"/>: its pieces, the ground they wear, its yard and the track out to the nearest way.</summary>
			private void Build(Plan plan, Vector2 centre, float yaw)
			{
				foreach (Part part in plan.Parts)
				{
					Vector2 at = centre + PointOfInterestLayouts.Rotate(part.At, yaw);
					float partYaw = yaw + part.Yaw;
					if (part.Building)
					{
						Floor(at, partYaw, part.Footprint);
					}
					Seat(Kit(part.Id, part.Variant, part.Building), part.Id, at, partYaw, part.Footprint);
				}
				// The yard trodden before the doors, and out of its way in to the nearest way where it is near and open; else a few paces.
				Vector2 gate = centre + PointOfInterestLayouts.Rotate(plan.Gate, yaw);
				Vector2 outward = PointOfInterestLayouts.Rotate(Vector2.down, yaw);
				Track(context, gate, centre, Mathf.Clamp(plan.Size.x * 0.2f, 1f, 2.5f), 0.6f, ScenePathPointFlags.None, record.Id);
				Vector2 end = gate + outward * 6f;
				if (NearestWay(gate, 80f, out Vector2 way) && OpenBetween(gate, way, centre))
				{
					end = way;
				}
				Track(context, gate, end, 1f, 0.7f, ScenePathPointFlags.None, record.Id);
			}

			/// <summary>Lays a plan beside a field of <paramref name="uses"/>, trying up to <paramref name="attempts"/> spots; true when laid.</summary>
			private bool LayBesideFields(Plan plan, Func<PointOfInterestFarmsteads.LaidField, bool> uses, int salt, int k, int attempts)
			{
				for (int attempt = 0; attempt < attempts; attempt++)
				{
					if (!BesideAField(uses, salt, k, attempt, plan.Size, out Vector2 centre, out float yaw)
						|| !Fits(centre, yaw, plan.Size, MaxSlopeDegrees, FarmsteadApart) || !BuildingsFit(plan, centre, yaw))
					{
						continue;
					}
					Build(plan, centre, yaw);
					Claim(centre, yaw, plan.Size);
					return true;
				}
				return false;
			}

			// ── The holdings ─────────────────────────────────────────

			/// <summary>Lone beekeepers beside the crop and fallow fields, apart from the farmsteads.</summary>
			public int Beekeepers()
			{
				int wanted = Wanted(record.SiteSeed, record.Kind, Holding.Beekeeper), n = 0;
				Vector2 bothy = FootprintOf("Bothy", BothySize) * stature, apiary = FootprintOf("Apiary", new Vector2(2.6f, 1.2f)) * stature;
				for (int k = 0; k < wanted; k++)
				{
					int hives = 2 + (int)(PointOfInterestPlanner.Hash(record.SiteSeed, BeeSalt, k, 1) % 3u);
					Plan plan = BeeYard(bothy, apiary, hives, VariantOf("Bothy", U(BeeSalt, k, 2)), U(BeeSalt, k, 3) < 0.7f ? 0 : 1);
					bool Uses(PointOfInterestFarmsteads.LaidField f) => !f.Orchard && (f.Use == PointOfInterestFarmsteads.FieldUse.Grain
						|| f.Use == PointOfInterestFarmsteads.FieldUse.Crop || f.Use == PointOfInterestFarmsteads.FieldUse.Fallow);
					n += LayBesideFields(plan, Uses, BeeSalt, k, 30) ? 1 : 0;
				}
				laid[(int)Holding.Beekeeper] = n;
				return n;
			}

			/// <summary>A shepherd's hut and his fold beside a pasture.</summary>
			public int Shepherds()
			{
				int wanted = Wanted(record.SiteSeed, record.Kind, Holding.Shepherd), n = 0;
				for (int k = 0; k < wanted; k++)
				{
					// A drystone fold where the country is stone-built or the roll says, else hurdles; the hut to the fold's left or right.
					bool stoneBuilt = string.Equals(context.Style, PointOfInterestTemplateSpecs.Stone, StringComparison.OrdinalIgnoreCase);
					int foldVariant = stoneBuilt || U(ShepherdSalt, k, 1) < 0.5f ? 0 : 1;
					Part hut = MakePart("ShepherdHut", VariantOf("ShepherdHut", U(ShepherdSalt, k, 2)), FootprintOf("ShepherdHut", ShepherdHutSize) * stature, 0f, true);
					Part fold = MakePart("Sheepfold", foldVariant, FootprintOf("Sheepfold", SheepfoldSize) * stature, 0f, false);
					Plan plan = Row(U(ShepherdSalt, k, 3) < 0.5f ? new[] { hut, fold } : new[] { fold, hut }, 3f);
					n += LayBesideFields(plan, f => f.Use == PointOfInterestFarmsteads.FieldUse.Pasture, ShepherdSalt, k, 30) ? 1 : 0;
				}
				laid[(int)Holding.Shepherd] = n;
				return n;
			}

			/// <summary>An orchard keeper's bothy beside an orchard, his woodpile against it, a hive or two by some.</summary>
			public int OrchardKeepers()
			{
				int orchards = 0;
				foreach (PointOfInterestFarmsteads.LaidField f in fields)
				{
					orchards += f.Orchard ? 1 : 0;
				}
				int wanted = Mathf.Min(orchards, Wanted(record.SiteSeed, record.Kind, Holding.OrchardKeeper)), n = 0;
				for (int k = 0; k < wanted; k++)
				{
					var parts = new List<Part>
					{
						MakePart("Bothy", 0, FootprintOf("Bothy", BothySize) * stature, 0f, true),
						MakePart("Woodpile", VariantOf("Woodpile", U(OrchardSalt, k, 1)), FootprintOf("Woodpile", new Vector2(1.7f, 1f)) * stature, -90f, false),
					};
					if (U(OrchardSalt, k, 2) < 0.4f)
					{
						parts.Add(MakePart("Apiary", 0, FootprintOf("Apiary", new Vector2(2.6f, 1.2f)) * stature, 0f, false));
					}
					n += LayBesideFields(Row(parts, 1.2f), f => f.Orchard, OrchardSalt, k, 30) ? 1 : 0;
				}
				laid[(int)Holding.OrchardKeeper] = n;
				return n;
			}

			/// <summary>A woodcutter's or a charcoal burner's yard at the edge of the nearest wood.</summary>
			public int Woodcutters()
			{
				int wanted = Wanted(record.SiteSeed, record.Kind, Holding.Woodcutter), n = 0;
				if (wanted <= 0)
				{
					return 0;
				}
				TerrainScatter.StandField stands = Stands();
				if (stands == null)
				{
					return 0;
				}
				List<(float Out, Vector2 Edge, Vector2 Dir)> margins = WoodMargins(stands);
				int next = 0;
				for (int k = 0; k < wanted; k++)
				{
					Plan plan;
					if (U(WoodSalt, k, 1) < 0.6f)
					{
						var parts = new List<Part>
						{
							MakePart("Bothy", VariantOf("Bothy", U(WoodSalt, k, 2)), FootprintOf("Bothy", BothySize) * stature, 0f, true),
							MakePart("Woodpile", VariantOf("Woodpile", U(WoodSalt, k, 3)), FootprintOf("Woodpile", new Vector2(1.7f, 1f)) * stature, -90f, false),
							MakePart("LogPile", VariantOf("LogPile", U(WoodSalt, k, 4)), FootprintOf("LogPile", new Vector2(5.4f, 2.4f)) * stature, 0f, false),
						};
						if (U(WoodSalt, k, 5) < 0.5f)
						{
							parts.Add(MakePart("SawPit", VariantOf("SawPit", U(WoodSalt, k, 6)), FootprintOf("SawPit", new Vector2(4.6f, 1.8f)) * stature, 90f, false));
						}
						plan = Row(parts, 1.5f);
					}
					else
					{
						plan = Row(new[]
						{
							MakePart("CharcoalClamp", 1, FootprintOf("CharcoalClamp", new Vector2(6f, 6f)) * stature, 0f, false),
							MakePart("LeanTo", VariantOf("LeanTo", U(WoodSalt, k, 7)), FootprintOf("LeanTo", new Vector2(3f, 2.5f)) * stature, 0f, true),
						}, 2.5f);
					}
					// The nearest margins first; its way in toward the settlement.
					for (; next < margins.Count; next++)
					{
						(float _, Vector2 edge, Vector2 dir) = margins[next];
						Vector2 centre = edge - dir * (WoodStandOff + plan.Size.y * 0.5f);
						float yaw = FacingYaw(-dir);
						if (!Fits(centre, yaw, plan.Size, MaxSlopeDegrees, FarmsteadApart) || !BuildingsFit(plan, centre, yaw))
						{
							continue;
						}
						Build(plan, centre, yaw);
						Claim(centre, yaw, plan.Size);
						n++;
						next++;
						break;
					}
				}
				laid[(int)Holding.Woodcutter] = n;
				return n;
			}

			/// <summary>The scene's forest stands as the site's biome grows them (its first tree rule that grows in stands); null for none.</summary>
			private TerrainScatter.StandField Stands()
			{
				BiomeTemplate biome = context.BiomeAt?.Invoke(record.Position.x, record.Position.z);
				if (biome == null || context.Request == null)
				{
					return null;
				}
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
							uint seed = TerrainScatter.StandSeed(SceneGenerator.SceneSeed(context.Request));
							return new TerrainScatter.StandField(seed, rule.forestMetres, rule.forestCover, rule.forestEdge, rule.forestOpen);
						}
					}
				}
				return null;
			}

			/// <summary>
			/// Where the woods begin round the site: along 48 bearings (turned by the site's seed) outward from its edge, the first ground
			/// the stands cover within <see cref="WoodReach"/>; nearest first.
			/// </summary>
			private List<(float Out, Vector2 Edge, Vector2 Dir)> WoodMargins(TerrainScatter.StandField stands)
			{
				var margins = new List<(float, Vector2, Vector2)>();
				float turn = U(WoodSalt, 0, 0) * 360f;
				const int Bearings = 48;
				for (int b = 0; b < Bearings; b++)
				{
					float bearing = turn + b * 360f / Bearings;
					var dir = new Vector2(Mathf.Sin(bearing * Mathf.Deg2Rad), Mathf.Cos(bearing * Mathf.Deg2Rad));
					float edge = PointOfInterestOutline.ReachToward(record.Outline, record.Radius, bearing);
					for (float d = edge * 1.1f + 30f; d <= edge + WoodReach; d += 6f)
					{
						Vector2 p = site + dir * d;
						if (stands.Noise(p.x, p.y) >= stands.Threshold)
						{
							margins.Add((d - edge, p, dir));
							break;
						}
					}
				}
				margins.Sort((x, y) => x.Item1.CompareTo(y.Item1));
				return margins;
			}

			// ── Along the ways out ───────────────────────────────────

			/// <summary>Wayside crosses and shrines on posts beside the ways out, facing them.</summary>
			public int Crosses()
			{
				int wanted = Wanted(record.SiteSeed, record.Kind, Holding.WaysideCross), n = 0;
				if (wanted <= 0 || leaving.Count == 0)
				{
					return 0;
				}
				var lines = new List<List<Vector2>>();
				var offsets = new List<float>();
				foreach ((List<Vector2> line, ScenePath path) in leaving)
				{
					lines.Add(line);
					// Past the way's trodden edge (its widest) by a pace.
					float widest = 0f;
					for (int i = 0; i < path.Count; i++)
					{
						widest = Mathf.Max(widest, HalfWidthAt(path, i));
					}
					offsets.Add(widest + 1.2f + WaysideCrossSize.y * 0.5f * stature);
				}
				var stood = new List<Vector2>();
				foreach (WaySpot spot in WaysideSpots(lines, offsets, site, Reach, record.SiteSeed, wanted * 12))
				{
					if (n >= wanted)
					{
						break;
					}
					if (stood.Exists(s => (s - spot.At).magnitude < WaysideApart))
					{
						continue;
					}
					// A cross mostly, now and then a little shrine on its post.
					bool shrine = U(CrossSalt, stood.Count + n * 7, 8) < 0.3f;
					string id = shrine ? "Shrine" : "WaysideCross";
					Vector2 size = (shrine ? FootprintOf("Shrine", WaysideCrossSize) : FootprintOf("WaysideCross", WaysideCrossSize)) * stature;
					if (!Fits(spot.At, spot.Yaw, size, WaysideSlopeDegrees, 0f, besideWay: true))
					{
						continue;
					}
					Seat(Kit(id, shrine ? 1 : VariantOf("WaysideCross", U(CrossSalt, n, 9)), false), id, spot.At, spot.Yaw, size);
					Claim(spot.At, spot.Yaw, size);
					stood.Add(spot.At);
					n++;
				}
				laid[(int)Holding.WaysideCross] = n;
				return n;
			}

			/// <summary>The roads out (a cart track or better) from the site's end, with the ways they are.</summary>
			private List<(List<Vector2> Line, ScenePath Path)> Roads()
			{
				return leaving.FindAll(w => w.Path.Class == ScenePathClass.Road || w.Path.Class == ScenePathClass.Highway || w.Path.Class == ScenePathClass.CartTrack);
			}

			/// <summary>A town's gallows on the highest gentle ground beside a road out, facing it.</summary>
			public int Gallows()
			{
				if (Wanted(record.SiteSeed, record.Kind, Holding.Gallows) <= 0)
				{
					return 0;
				}
				Vector2 size = FootprintOf("Gallows", GallowsSize) * stature;
				float bestHeight = float.NegativeInfinity;
				Vector2 bestAt = default;
				float bestYaw = 0f;
				List<(List<Vector2> Line, ScenePath Path)> roads = Roads();
				for (int r = 0; r < roads.Count; r++)
				{
					for (float o = GallowsFrom; o <= GallowsTo; o += 15f)
					{
						if (!AtOut(roads[r].Line, site, Reach, o, out Vector2 at, out Vector2 along, out int index))
						{
							break;
						}
						for (int side = -1; side <= 1; side += 2)
						{
							Vector2 normal = PointOfInterestLinearFeatures.Left(along) * side;
							Vector2 c = at + normal * (HalfWidthAt(roads[r].Path, index) + 3f + size.y * 0.5f);
							float h = context.GroundAt(c.x, c.y);
							float yaw = FacingYaw(-normal);
							if (h > bestHeight && Fits(c, yaw, size, GallowsSlopeDegrees, 10f))
							{
								bestHeight = h;
								bestAt = c;
								bestYaw = yaw;
							}
						}
					}
				}
				if (float.IsNegativeInfinity(bestHeight))
				{
					return 0;
				}
				Seat(Kit("Gallows", U(GallowsSalt, 0, 1) < 0.65f ? 0 : 1), "Gallows", bestAt, bestYaw, size);
				Track(context, bestAt, bestAt, Mathf.Max(size.x, size.y) * 0.5f + 1.5f, 0.6f, ScenePathPointFlags.None, record.Id);
				if (NearestWay(bestAt, 30f, out Vector2 way))
				{
					Track(context, bestAt, way, 0.8f, 0.6f, ScenePathPointFlags.None, record.Id);
				}
				Claim(bestAt, bestYaw, size + Vector2.one * 2f);
				laid[(int)Holding.Gallows] = 1;
				return 1;
			}

			/// <summary>A city's cemetery outside its walls beside a road out, walled, its lychgate to the road.</summary>
			public int Cemeteries()
			{
				if (Wanted(record.SiteSeed, record.Kind, Holding.Cemetery) <= 0)
				{
					return 0;
				}
				var size = new Vector2(Mathf.Lerp(24f, 34f, U(CemeterySalt, 0, 1)), Mathf.Lerp(18f, 26f, U(CemeterySalt, 0, 2))) * Mathf.Max(1f, stature);
				Vector2 grave = FootprintOf("Gravestone", new Vector2(0.8f, 0.4f)) * stature, mound = FootprintOf("GraveMound", new Vector2(1f, 2.2f)) * stature;
				Vector2 tomb = FootprintOf("Mausoleum", new Vector2(5f, 7f)) * stature;
				Vector2 lychgate = FootprintOf("Lychgate", PointOfInterestChurchyards.LychgateFallback) * stature;
				int tombs = 1 + (int)(PointOfInterestPlanner.Hash(record.SiteSeed, CemeterySalt, 0, 3) % 3u);
				// Only the mausoleums its churchyards left the settlement room for (2026-10-20, PointOfInterestStructureCaps): two in all.
				tombs = Mathf.Min(tombs, PointOfInterestStructureCaps.Tally.Of(context).Room(PointOfInterestChurchyards.MausoleumType));
				Plan plan = CemeteryYard(size, grave, mound, tomb, lychgate, tombs, unchecked((int)PointOfInterestPlanner.Hash(record.SiteSeed, CemeterySalt, 0, 4)));
				List<(List<Vector2> Line, ScenePath Path)> roads = Roads();
				if (roads.Count == 0)
				{
					return 0;
				}
				int first = (int)(PointOfInterestPlanner.Hash(record.SiteSeed, CemeterySalt, 0, 5) % (uint)roads.Count);
				for (float o = CemeteryFrom; o <= CemeteryTo; o += 10f)
				{
					for (int i = 0; i < roads.Count; i++)
					{
						(List<Vector2> line, ScenePath path) = roads[(first + i) % roads.Count];
						if (!AtOut(line, site, Reach, o, out Vector2 at, out Vector2 along, out int index))
						{
							continue;
						}
						for (int s = 0; s < 2; s++)
						{
							float side = ((PointOfInterestPlanner.Hash(record.SiteSeed, CemeterySalt, i, 6) + (uint)s) & 1u) == 0 ? 1f : -1f;
							Vector2 normal = PointOfInterestLinearFeatures.Left(along) * side;
							Vector2 centre = at + normal * (HalfWidthAt(path, index) + 2.5f + size.y * 0.5f);
							float yaw = FacingYaw(-normal);
							if (!Fits(centre, yaw, size, MaxSlopeDegrees, 6f) || !BuildingsFit(plan, centre, yaw))
							{
								continue;
							}
							BuildCemetery(plan, centre, yaw, lychgate);
							Claim(centre, yaw, size);
							laid[(int)Holding.Cemetery] = 1;
							return 1;
						}
					}
				}
				return 0;
			}

			/// <summary>Lays a planned cemetery: its graves and mausoleums, its wall, its lychgate and the way from it to the road.</summary>
			private void BuildCemetery(Plan plan, Vector2 centre, float yaw, Vector2 lychgate)
			{
				foreach (Part part in plan.Parts)
				{
					Vector2 at = centre + PointOfInterestLayouts.Rotate(part.At, yaw);
					float partYaw = yaw + part.Yaw;
					if (part.Building)
					{
						Floor(at, partYaw, part.Footprint);
					}
					Seat(Kit(part.Id, part.Variant, part.Building), part.Id, at, partYaw, part.Footprint);
				}
				Vector2 gate = centre + PointOfInterestLayouts.Rotate(plan.Gate, yaw);
				Seat(Kit("Lychgate", 0), "Lychgate", gate, yaw, lychgate);
				var runs = new List<(Vector2 A, Vector2 B)>();
				foreach ((Vector2 a, Vector2 b) in plan.Runs)
				{
					runs.Add((centre + PointOfInterestLayouts.Rotate(a, yaw), centre + PointOfInterestLayouts.Rotate(b, yaw)));
				}
				int missing = 0;
				PointOfInterestChurchyards.LayWall(context, runs, (p, y, e) => context.IsWet(p.x, p.y, 0.1f), null, allowLocal, ref missing);
				if (missing > 0)
				{
					Missing.Add("LinearDrystone/IronFence");
				}
				// The walk up the middle, and out through the gate to the road.
				Vector2 forward = PointOfInterestLayouts.Rotate(Vector2.up, yaw);
				Track(context, gate - forward * 2f, gate + forward * (plan.Size.y - 2f), 0.8f, 0.75f, ScenePathPointFlags.None, record.Id);
				if (NearestWay(gate, 40f, out Vector2 way))
				{
					Track(context, gate, way, 1f, 0.75f, ScenePathPointFlags.None, record.Id);
				}
			}
		}
	}
}
#endif
