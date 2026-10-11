#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What the path planner needs to know about a scene: its ground, water, sites and what they are built as.</summary>
	public sealed class PathPlanInput
	{
		public float WidthMetres;
		public float DepthMetres;
		/// <summary>The ground's height at (east, north), metres.</summary>
		public Func<float, float, float> Ground;
		/// <summary>What water is at (east, north); null for a dry scene.</summary>
		public Func<float, float, PathCellWater> WaterAt;
		/// <summary>A river's depth under its surface at (east, north), metres.</summary>
		public Func<float, float, float> WaterDepth;
		/// <summary>True where the ground is holed (a cave's mouth, a canyon wall); null for none.</summary>
		public Func<float, float, bool> Blocked;
		public IReadOnlyList<PointOfInterestRecord> Records = Array.Empty<PointOfInterestRecord>();
		public IReadOnlyList<PointOfInterestKeepOut> KeepOuts = Array.Empty<PointOfInterestKeepOut>();
		public IReadOnlyList<PointOfInterestShape> Shapes = Array.Empty<PointOfInterestShape>();
		/// <summary>True for a site laid out along streets (a walled town, a street village): its ways come in at the street ends.</summary>
		public Func<PointOfInterestRecord, bool> HasStreets;
		public int Seed;
		/// <summary>The router's cell, metres.</summary>
		public float CellMetres = 4f;
		/// <summary>No way within this of the scene's edge, metres.</summary>
		public float EdgeMarginMetres = 24f;
	}

	/// <summary>A bridge a way needs where no bridge stands: the stage turns each into a Bridge site.</summary>
	public sealed class PathBridgeSite
	{
		public int PathId;
		public Vector3 Centre;
		/// <summary>Degrees about +y the deck runs along (across the river).</summary>
		public float Yaw;
		/// <summary>The water's width under it, metres.</summary>
		public float Span;
	}

	/// <summary>What the planner made.</summary>
	public sealed class PathPlan
	{
		public readonly List<ScenePath> Paths = new List<ScenePath>();
		public readonly List<PathBridgeSite> Bridges = new List<PathBridgeSite>();
		public readonly List<string> Notes = new List<string>();
		public int Fords;
	}

	/// <summary>
	/// Plans a scene's ways: which sites are joined, by what class of way, and where each runs on the ground.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The trunk network joins the settlements</b> (towns, cities, keeps …) along the relative neighbourhood graph of
	/// their positions: two are joined unless a third lies nearer to both, which is close to how real road networks look
	/// (every town reached, a few loops, no long parallel roads). The class follows the lesser end: city to city or the
	/// capital a highway; town to town or to a city a road; anything to a village a cart track. Trunks are routed first,
	/// biggest first, so lesser ways join them.
	/// </para>
	/// <para>
	/// <b>Every other site that people go to takes a spur</b>, run from the site until it meets any way (or the nearest
	/// settlement): a footpath to a shrine, a graveyard, a waterfall; a cart track to a mine; a faint trail to a ruin, a
	/// cave, a bandit camp. A spur fades from kept where it leaves the network to all but lost at its end, and a ruin's
	/// fades further. Sites under water, the landforms and the water itself take none. With no settlement in the scene the
	/// spurs join one another by trails instead.
	/// </para>
	/// <para>
	/// <b>Ways come in where a site opens</b>: a street town at the ends of its streets (and its walls take gates there,
	/// <see cref="ScenePath.FromEntrance"/>), a cave at its mouth, anything else from the side facing the way.
	/// </para>
	/// <para>
	/// <b>Rivers</b> are waded where no deeper than the class fords, bridged otherwise (<see cref="PathBridgeSite"/>);
	/// a bridge already standing is used by every way that comes near it.
	/// </para>
	/// </remarks>
	public static class PathNetworkPlanner
	{
		/// <summary>Metres between a way's stored points.</summary>
		public const float PointSpacing = 2f;

		/// <summary>A settlement's place in the hierarchy, 0 when the kind is not one.</summary>
		public static int RankOf(POIType kind)
		{
			switch (kind)
			{
				case POIType.Capital: return 5;
				case POIType.City: return 4;
				case POIType.Town:
				case POIType.Port:
				case POIType.Castle:
				case POIType.Fortress:
					return 3;
				case POIType.Village:
				case POIType.StiltVillage:
				case POIType.TradingPost:
				case POIType.Monastery:
				case POIType.Keep:
					return 2;
				default:
					return 0;
			}
		}

		/// <summary>The class of way between two settlements by rank.</summary>
		public static ScenePathClass LinkClass(int rankA, int rankB)
		{
			int low = Mathf.Min(rankA, rankB);
			if (low >= 4)
			{
				return ScenePathClass.Highway;
			}
			if (low >= 3)
			{
				return ScenePathClass.Road;
			}
			return ScenePathClass.CartTrack;
		}

		/// <summary>
		/// The spur a non-settlement kind takes, and how kept it is (a factor on its class's wear); false for a kind no way
		/// leads to.
		/// </summary>
		public static bool SpurOf(POIType kind, out ScenePathClass way, out float wearScale)
		{
			wearScale = 1f;
			switch (kind)
			{
				case POIType.Mine:
				case POIType.Quarry:
				case POIType.LumberCamp:
					way = ScenePathClass.CartTrack;
					return true;
				case POIType.AbandonedFarm:
					way = ScenePathClass.CartTrack;
					wearScale = 0.35f;
					return true;
				case POIType.Waystation:
				case POIType.Temple:
				case POIType.Graveyard:
				case POIType.Shrine:
				case POIType.Lighthouse:
				case POIType.Tower:
				case POIType.FishingCamp:
				case POIType.HuntingLodge:
				case POIType.Waterfall:
				case POIType.HotSpring:
				case POIType.Spring:
				case POIType.Oasis:
				case POIType.Portal:
				case POIType.DungeonEntrance:
				case POIType.Crypt:
				case POIType.Monument:
				case POIType.StoneCircle:
				case POIType.Peak:
					way = ScenePathClass.Footpath;
					return true;
				case POIType.Hermitage:
				case POIType.WitchHut:
				case POIType.BogShrine:
				case POIType.Statue:
				case POIType.Obelisk:
				case POIType.Landmark:
				case POIType.LeyNexus:
				case POIType.FallenStar:
				case POIType.AncientTree:
				case POIType.HerbGrove:
				case POIType.NaturalArch:
					way = ScenePathClass.Trail;
					return true;
				case POIType.Camp:
				case POIType.BanditCamp:
				case POIType.RitualSite:
				case POIType.BossLair:
				case POIType.PirateCove:
				case POIType.SmugglersCove:
				case POIType.Cave:
				case POIType.IceCave:
				case POIType.LavaTube:
				case POIType.Grotto:
				case POIType.SeaCave:
				case POIType.CorruptedGrove:
				case POIType.OreVein:
				case POIType.CrystalFormation:
					way = ScenePathClass.Trail;
					wearScale = 0.85f;
					return true;
				case POIType.Ruins:
				case POIType.RuinedTower:
				case POIType.Barrow:
				case POIType.Battlefield:
				case POIType.Ossuary:
				case POIType.FeyRing:
				case POIType.SunkenTemple:
				case POIType.DrownedVillage:
				case POIType.Overhang:
					way = ScenePathClass.Trail;
					wearScale = 0.55f;
					return true;
				default:
					way = ScenePathClass.Trail;
					return false;
			}
		}

		/// <summary>The longest a spur of a class is run before the site is left without one, metres.</summary>
		public static float MaxSpur(ScenePathClass kind)
		{
			switch (kind)
			{
				case ScenePathClass.CartTrack: return 2500f;
				case ScenePathClass.Footpath: return 1800f;
				default: return 1400f;
			}
		}

		public static PathPlan Plan(PathPlanInput input) => new Planner(input).Run();

		/// <summary>Where a way meets a site: the point outside it the route starts from, the points inside, and the bearing.</summary>
		private struct End
		{
			public Vector2 Outer;
			public List<Vector2> Inner;
			public float Entrance;
		}

		private sealed class Planner
		{
			private readonly PathPlanInput input;
			private readonly PathRouter router;
			private readonly PathPlan plan = new PathPlan();
			private int nextBridgeId = -1;

			public Planner(PathPlanInput input)
			{
				this.input = input;
				router = new PathRouter(input.WidthMetres, input.DepthMetres, input.CellMetres, input.Seed);
			}

			public PathPlan Run()
			{
				router.Fill(input.Ground, input.WaterAt, input.WaterDepth, input.Blocked, input.EdgeMarginMetres);
				foreach (PointOfInterestKeepOut keepOut in input.KeepOuts)
				{
					router.MarkOwner(keepOut.Id, keepOut.X, keepOut.Z, keepOut.Radius);
				}
				foreach (PointOfInterestRecord record in input.Records)
				{
					if (record != null && record.Kind == POIType.Bridge)
					{
						router.MarkBridge(record.Id, record.Position, record.Yaw, Mathf.Max(4f, record.Radius));
					}
				}

				var settlements = new List<PointOfInterestRecord>();
				var spurs = new List<(PointOfInterestRecord record, ScenePathClass way, float wear)>();
				var ancient = new List<PointOfInterestRecord>();
				foreach (PointOfInterestRecord record in input.Records)
				{
					if (record == null || PointOfInterestKinds.Info(record.Kind).Has(PointOfInterestTraits.Underwater))
					{
						continue;
					}
					if (RankOf(record.Kind) > 0)
					{
						settlements.Add(record);
					}
					else if (record.Kind == POIType.AncientRoad)
					{
						ancient.Add(record);
					}
					else if (SpurOf(record.Kind, out ScenePathClass way, out float wear))
					{
						spurs.Add((record, way, wear));
					}
				}

				Trunks(settlements);
				Spurs(spurs, settlements);
				foreach (PointOfInterestRecord record in ancient)
				{
					Ancient(record);
				}

				int roads = 0, tracks = 0, paths = 0, trails = 0;
				float metres = 0f;
				foreach (ScenePath path in plan.Paths)
				{
					metres += path.Length;
					switch (path.Class)
					{
						case ScenePathClass.Highway:
						case ScenePathClass.Road: roads++; break;
						case ScenePathClass.CartTrack: tracks++; break;
						case ScenePathClass.Footpath: paths++; break;
						default: trails++; break;
					}
				}
				plan.Notes.Add($"{plan.Paths.Count} way(s), {metres / 1000f:0.0} km: {roads} road(s), {tracks} cart track(s), {paths} footpath(s), {trails} trail(s); {plan.Bridges.Count} new bridge(s), {plan.Fords} ford(s).");
				return plan;
			}

			// ── The trunk network ──────────────────────────────────

			private void Trunks(List<PointOfInterestRecord> settlements)
			{
				var links = new List<(int a, int b, ScenePathClass way, float length)>();
				for (int a = 0; a < settlements.Count; a++)
				{
					for (int b = a + 1; b < settlements.Count; b++)
					{
						float ab = Flat(settlements[a].Position, settlements[b].Position);
						bool neighbours = true;
						for (int c = 0; c < settlements.Count && neighbours; c++)
						{
							if (c == a || c == b)
							{
								continue;
							}
							// Relative neighbourhood: a third nearer to both means the two are reached through it.
							neighbours = Mathf.Max(Flat(settlements[a].Position, settlements[c].Position), Flat(settlements[b].Position, settlements[c].Position)) >= ab;
						}
						if (neighbours)
						{
							links.Add((a, b, LinkClass(RankOf(settlements[a].Kind), RankOf(settlements[b].Kind)), ab));
						}
					}
				}
				// Biggest first, then shortest, then by id: lesser ways join greater ones, and the order never rests on the list.
				links.Sort((p, q) =>
				{
					int c = q.way.CompareTo(p.way);
					if (c != 0) return c;
					c = p.length.CompareTo(q.length);
					if (c != 0) return c;
					c = settlements[p.a].Id.CompareTo(settlements[q.a].Id);
					return c != 0 ? c : settlements[p.b].Id.CompareTo(settlements[q.b].Id);
				});
				foreach ((int a, int b, ScenePathClass way, float _) in links)
				{
					PointOfInterestRecord ra = settlements[a], rb = settlements[b];
					End ea = EndFor(ra, new Vector2(rb.Position.x, rb.Position.z));
					End eb = EndFor(rb, new Vector2(ra.Position.x, ra.Position.z));
					var request = new PathRouter.Request { Class = way, From = ea.Outer, To = eb.Outer, OwnA = ra.Id, OwnB = rb.Id };
					PathRouter.Route route = router.Find(request);
					if (route == null)
					{
						request.Relaxed = true;
						route = router.Find(request);
					}
					if (route == null)
					{
						plan.Notes.Add($"No way found between {Label(ra)} and {Label(rb)}.");
						continue;
					}
					var line = new List<Vector2>();
					var inSite = new List<bool>();
					AddInner(line, inSite, ea.Inner, true);
					line.Add(ea.Outer);
					inSite.Add(false);
					AddCells(line, inSite, route, true, true);
					line.Add(eb.Outer);
					inSite.Add(false);
					AddInner(line, inSite, eb.Inner, false);
					Finish(line, inSite, way, ra, rb, ea.Entrance, eb.Entrance, 1f, false);
				}
			}

			// ── Spurs ──────────────────────────────────────────────

			private void Spurs(List<(PointOfInterestRecord record, ScenePathClass way, float wear)> spurs, List<PointOfInterestRecord> settlements)
			{
				PointOfInterestRecord Nearest(Vector3 p, IEnumerable<PointOfInterestRecord> among)
				{
					PointOfInterestRecord best = null;
					float bestD = float.PositiveInfinity;
					foreach (PointOfInterestRecord r in among)
					{
						float d = Flat(p, r.Position);
						if (d < bestD || d == bestD && best != null && r.Id < best.Id)
						{
							best = r;
							bestD = d;
						}
					}
					return best;
				}

				var hubs = new List<PointOfInterestRecord>(settlements);
				var order = new List<(PointOfInterestRecord record, ScenePathClass way, float wear, float distance)>();
				foreach ((PointOfInterestRecord record, ScenePathClass way, float wear) in spurs)
				{
					PointOfInterestRecord hub = Nearest(record.Position, settlements);
					order.Add((record, way, wear, hub != null ? Flat(record.Position, hub.Position) : 0f));
				}
				// Greater ways first, nearer sites first: a footpath laid is a way a farther trail may join.
				order.Sort((p, q) =>
				{
					int c = q.way.CompareTo(p.way);
					if (c != 0) return c;
					c = p.distance.CompareTo(q.distance);
					return c != 0 ? c : p.record.Id.CompareTo(q.record.Id);
				});

				foreach ((PointOfInterestRecord record, ScenePathClass way, float wear, float _) in order)
				{
					PointOfInterestRecord hub = Nearest(record.Position, hubs);
					if (hub == null)
					{
						// No settlement anywhere: the first site waits for the others to find it.
						hubs.Add(record);
						continue;
					}
					float limit = MaxSpur(way);
					if (Flat(record.Position, hub.Position) > limit * 1.2f && !AnyWayNear(record.Position, limit))
					{
						continue;
					}
					End site = EndFor(record, new Vector2(hub.Position.x, hub.Position.z));
					End target = EndFor(hub, site.Outer);
					var request = new PathRouter.Request
					{
						Class = way,
						From = site.Outer,
						To = target.Outer,
						OwnA = record.Id,
						OwnB = hub.Id,
						JoinNetwork = 2f * input.CellMetres,
						MaxLength = limit,
					};
					PathRouter.Route route = router.Find(request);
					if (route == null)
					{
						request.Relaxed = true;
						route = router.Find(request);
					}
					if (route == null)
					{
						continue;
					}
					// From the network (or the hub) out to the site.
					var line = new List<Vector2>();
					var inSite = new List<bool>();
					bool reachedHub = !route.Joined;
					if (reachedHub)
					{
						AddInner(line, inSite, target.Inner, true);
						line.Add(target.Outer);
						inSite.Add(false);
					}
					else
					{
						Vector2 join = NearestWayPoint(router.XOf(route.Cells[route.Cells.Count - 1] % router.Width), router.ZOf(route.Cells[route.Cells.Count - 1] / router.Width));
						line.Add(join);
						inSite.Add(false);
					}
					route.Cells.Reverse();
					AddCells(line, inSite, route, !reachedHub ? false : true, true);
					line.Add(site.Outer);
					inSite.Add(false);
					AddInner(line, inSite, site.Inner, false);
					if (PolylineLength(line) > limit * 1.15f)
					{
						continue;
					}
					Finish(line, inSite, way, reachedHub ? hub : null, record, reachedHub ? target.Entrance : float.NaN, site.Entrance, wear, true);
					if (settlements.Count == 0)
					{
						hubs.Add(record);
					}
				}
			}

			private bool AnyWayNear(Vector3 p, float reach)
			{
				foreach (ScenePath path in plan.Paths)
				{
					foreach (Vector3 q in path.Points)
					{
						if (Flat(p, q) <= reach)
						{
							return true;
						}
					}
				}
				return false;
			}

			private Vector2 NearestWayPoint(float east, float north)
			{
				var at = new Vector2(east, north);
				Vector2 best = at;
				float bestD = 3f * input.CellMetres;
				foreach (ScenePath path in plan.Paths)
				{
					for (int i = 0; i < path.Count; i++)
					{
						if ((path.FlagsAt(i) & (ScenePathPointFlags.InSite | ScenePathPointFlags.Bridge)) != 0)
						{
							continue;
						}
						var q = new Vector2(path.Points[i].x, path.Points[i].z);
						float d = Vector2.Distance(q, at);
						if (d < bestD)
						{
							bestD = d;
							best = q;
						}
					}
				}
				return best;
			}

			// ── Ancient roads ──────────────────────────────────────

			/// <summary>An ancient road site: a stretch of the old way laid through it along its heading, a few hundred metres.</summary>
			private void Ancient(PointOfInterestRecord record)
			{
				float r = record.Yaw * Mathf.Deg2Rad;
				var along = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
				var centre = new Vector2(record.Position.x, record.Position.z);
				float reach = Mathf.Clamp(record.Radius * 4f, 80f, 260f);
				var request = new PathRouter.Request
				{
					Class = ScenePathClass.AncientRoad,
					From = centre - along * reach,
					To = centre + along * reach,
					OwnA = record.Id,
					OwnB = record.Id,
					Relaxed = true,
				};
				PathRouter.Route route = router.Find(request);
				if (route == null)
				{
					return;
				}
				var line = new List<Vector2>();
				var inSite = new List<bool>();
				AddCells(line, inSite, route, false, false);
				Finish(line, inSite, ScenePathClass.AncientRoad, record, record, float.NaN, float.NaN, 1f, false);
			}

			// ── Ends ───────────────────────────────────────────────

			private End EndFor(PointOfInterestRecord record, Vector2 toward)
			{
				var centre = new Vector2(record.Position.x, record.Position.z);
				var end = new End { Inner = new List<Vector2>(), Entrance = float.NaN };

				PointOfInterestShape cave = null;
				foreach (PointOfInterestShape shape in input.Shapes)
				{
					if (shape != null && shape.SiteId == record.Id && shape.Shaper == CaveShaper.ShaperName)
					{
						cave = shape;
						break;
					}
				}
				if (cave != null)
				{
					// A cave is entered at its mouth: out along the way it faces.
					float yr = cave.Yaw * Mathf.Deg2Rad;
					var outward = new Vector2(Mathf.Sin(yr), Mathf.Cos(yr));
					Vector2 front = new Vector2(cave.Position.x, cave.Position.z) + outward * (cave.Size.x * 0.5f);
					end.Outer = Clear(front + outward * 6f, outward);
					end.Inner.Add(front + outward * 1f);
					return end;
				}

				Vector2 bearing = toward - centre;
				if (bearing.sqrMagnitude < 1e-4f)
				{
					bearing = Vector2.up;
				}
				bearing.Normalize();
				if (input.HasStreets != null && input.HasStreets(record))
				{
					// A street town is entered at its streets' ends: its heading and the opposite, and across in a big one.
					int ways = record.Radius >= 55f ? 4 : 2;
					float bestDot = float.NegativeInfinity;
					Vector2 dir = bearing;
					float local = 0f;
					for (int k = 0; k < ways; k++)
					{
						float a = k * 360f / ways;
						float yr = (record.Yaw + a) * Mathf.Deg2Rad;
						var d = new Vector2(Mathf.Sin(yr), Mathf.Cos(yr));
						float dot = Vector2.Dot(d, bearing);
						if (dot > bestDot)
						{
							bestDot = dot;
							dir = d;
							local = a;
						}
					}
					end.Entrance = local;
					end.Outer = Clear(centre + dir * (record.Radius + 8f), dir);
					end.Inner.Add(centre + dir * (record.Radius * 0.7f));
					return end;
				}

				// Anything else: from the side facing the way, into the site a little.
				end.Outer = Clear(centre + bearing * (record.Radius * 0.9f + 4f), bearing);
				end.Inner.Add(centre + bearing * (record.Radius * 0.4f));
				return end;
			}

			/// <summary>The first point from <paramref name="at"/> outward a way may start from (not water, not holed).</summary>
			private Vector2 Clear(Vector2 at, Vector2 outward)
			{
				for (int step = 0; step < 10; step++)
				{
					Vector2 p = at + outward * (step * 3f);
					int i = router.IndexAt(p.x, p.y);
					if (!router.Blocked[i] && router.Water[i] != PathCellWater.Open && router.Water[i] != PathCellWater.River)
					{
						return p;
					}
				}
				return at;
			}

			private static void AddInner(List<Vector2> line, List<bool> inSite, List<Vector2> inner, bool reversed)
			{
				if (inner == null)
				{
					return;
				}
				for (int k = 0; k < inner.Count; k++)
				{
					line.Add(inner[reversed ? inner.Count - 1 - k : k]);
					inSite.Add(true);
				}
			}

			/// <summary>The route's cells as points, dropping the first and last (the exact ends stand in for them).</summary>
			private void AddCells(List<Vector2> line, List<bool> inSite, PathRouter.Route route, bool dropFirst, bool dropLast)
			{
				int from = dropFirst ? 1 : 0, to = route.Cells.Count - (dropLast ? 1 : 0);
				for (int k = from; k < to; k++)
				{
					int c = route.Cells[k];
					line.Add(new Vector2(router.XOf(c % router.Width), router.ZOf(c / router.Width)));
					inSite.Add(false);
				}
			}

			// ── A finished way ─────────────────────────────────────

			/// <summary>
			/// Smooths a routed line, resamples it, and gives every point its height, width, wear, surface and flags; marks the
			/// way in the router for those after it, and the bridges it needs.
			/// </summary>
			private void Finish(List<Vector2> raw, List<bool> rawInSite, ScenePathClass way, PointOfInterestRecord from, PointOfInterestRecord to,
				float fromEntrance, float toEntrance, float wearScale, bool spur)
			{
				ScenePathStyle style = ScenePathStyle.For(way);
				List<Vector2> smooth = Smooth(raw, rawInSite, out List<bool> smoothInSite);
				List<Vector2> points = Resample(smooth, smoothInSite, PointSpacing, out List<bool> inSite);
				if (points.Count < 2)
				{
					return;
				}
				int id = plan.Paths.Count + 1;
				int n = points.Count;
				var path = new ScenePath
				{
					Id = id,
					Class = way,
					FromId = from != null ? from.Id : -1,
					ToId = to != null ? to.Id : -1,
					FromEntrance = fromEntrance,
					ToEntrance = toEntrance,
					Points = new Vector3[n],
					HalfWidth = new float[n],
					Wear = new float[n],
					Surface = new byte[n],
					Flags = new byte[n],
				};

				// Distance along, for wear and paving.
				var along = new float[n];
				for (int i = 1; i < n; i++)
				{
					along[i] = along[i - 1] + Vector2.Distance(points[i - 1], points[i]);
				}
				float total = along[n - 1];
				bool paveFrom = from != null && RankOf(from.Kind) >= 4, paveTo = to != null && RankOf(to.Kind) >= 4;

				for (int i = 0; i < n; i++)
				{
					Vector2 p = points[i];
					path.Points[i] = new Vector3(p.x, input.Ground(p.x, p.y), p.y);
					float noise = PathRouter.Noise(p.x * 0.031f + input.Seed % 97, p.y * 0.031f - id);
					float kept;
					if (spur)
					{
						// Kept where it leaves the network, lost toward its end.
						kept = Mathf.Lerp(style.FarWear, style.Wear, Mathf.Exp(-along[i] / 450f)) * wearScale;
					}
					else
					{
						kept = style.Wear * wearScale;
					}
					kept = Mathf.Clamp01(kept - 0.08f * (noise - 0.5f));
					path.Wear[i] = kept;
					float half = style.HalfWidth;
					if (style.Surface == ScenePathSurface.Earth)
					{
						half *= Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(kept / Mathf.Max(0.05f, style.Wear))) * (0.92f + 0.16f * noise);
					}
					path.HalfWidth[i] = half;
					ScenePathSurface surface = style.Surface;
					if (way == ScenePathClass.Road && (paveFrom && along[i] < 250f || paveTo && total - along[i] < 250f))
					{
						surface = ScenePathSurface.Stone;
					}
					path.Surface[i] = (byte)surface;
					path.Flags[i] = (byte)(inSite[i] ? ScenePathPointFlags.InSite : ScenePathPointFlags.None);
				}
				Crossings(path, style);
				plan.Paths.Add(path);
				var marked = new List<Vector3>();
				for (int i = 0; i < n; i++)
				{
					if ((path.FlagsAt(i) & ScenePathPointFlags.InSite) == 0)
					{
						marked.Add(path.Points[i]);
					}
				}
				router.MarkWay(marked, way, style.HalfWidth);
			}

			/// <summary>Flags the points over rivers as fords or bridges, and asks for the bridges no standing one serves.</summary>
			private void Crossings(ScenePath path, in ScenePathStyle style)
			{
				int n = path.Count;
				int i = 0;
				while (i < n)
				{
					Vector3 p = path.Points[i];
					int cell = router.IndexAt(p.x, p.z);
					if (router.Bridge[cell] != 0)
					{
						path.Flags[i] |= (byte)ScenePathPointFlags.Bridge;
						i++;
						continue;
					}
					if (input.WaterAt == null || input.WaterAt(p.x, p.z) != PathCellWater.River)
					{
						i++;
						continue;
					}
					// A run of river: wade it or bridge it, on its deepest point.
					int start = i;
					float deepest = 0f;
					while (i < n && input.WaterAt(path.Points[i].x, path.Points[i].z) == PathCellWater.River)
					{
						deepest = Mathf.Max(deepest, input.WaterDepth != null ? input.WaterDepth(path.Points[i].x, path.Points[i].z) : 0f);
						i++;
					}
					int end = i - 1;
					if (deepest <= style.FordDepth)
					{
						for (int k = Mathf.Max(0, start - 2); k <= Mathf.Min(n - 1, end + 2); k++)
						{
							path.Flags[k] |= (byte)ScenePathPointFlags.Ford;
							if (path.Surface[k] != (byte)ScenePathSurface.Stone)
							{
								path.Surface[k] = (byte)ScenePathSurface.Gravel;
							}
						}
						plan.Fords++;
						continue;
					}
					int a = Mathf.Max(0, start - 1), b = Mathf.Min(n - 1, end + 1);
					for (int k = a; k <= b; k++)
					{
						path.Flags[k] |= (byte)ScenePathPointFlags.Bridge;
					}
					Vector3 pa = path.Points[a], pb = path.Points[b];
					Vector3 centre = (pa + pb) * 0.5f;
					var site = new PathBridgeSite
					{
						PathId = path.Id,
						Centre = new Vector3(centre.x, Mathf.Max(pa.y, pb.y), centre.z),
						Yaw = PointOfInterestPlanner.YawOf(pb.x - pa.x, pb.z - pa.z),
						Span = new Vector2(pb.x - pa.x, pb.z - pa.z).magnitude,
					};
					plan.Bridges.Add(site);
					// Every way after this one may use it.
					router.MarkBridge(nextBridgeId--, site.Centre, site.Yaw, site.Span * 0.5f + 2f);
				}
			}

			// ── Geometry ───────────────────────────────────────────

			/// <summary>Three rounds of corner cutting (Chaikin); the ends and the points inside sites stay put.</summary>
			private static List<Vector2> Smooth(List<Vector2> line, List<bool> fixedPoint, out List<bool> fixedOut)
			{
				List<Vector2> current = line;
				List<bool> flags = fixedPoint;
				for (int round = 0; round < 3; round++)
				{
					var next = new List<Vector2>(current.Count * 2);
					var nextFlags = new List<bool>(current.Count * 2);
					next.Add(current[0]);
					nextFlags.Add(flags[0]);
					for (int i = 0; i < current.Count - 1; i++)
					{
						Vector2 a = current[i], b = current[i + 1];
						if (flags[i] || flags[i + 1] || i == 0 || i == current.Count - 2)
						{
							// Keep a pinned point exactly, and the straight from it.
							if (i > 0)
							{
								next.Add(a);
								nextFlags.Add(flags[i]);
							}
							continue;
						}
						next.Add(Vector2.Lerp(a, b, 0.25f));
						nextFlags.Add(false);
						next.Add(Vector2.Lerp(a, b, 0.75f));
						nextFlags.Add(false);
					}
					next.Add(current[current.Count - 1]);
					nextFlags.Add(flags[flags.Count - 1]);
					current = next;
					flags = nextFlags;
				}
				fixedOut = flags;
				return current;
			}

			/// <summary>Points every <paramref name="spacing"/> metres along a line; a point inside a site keeps the flag of the stretch it lies on.</summary>
			private static List<Vector2> Resample(List<Vector2> line, List<bool> inSite, float spacing, out List<bool> flags)
			{
				var points = new List<Vector2>();
				flags = new List<bool>();
				if (line.Count == 0)
				{
					return points;
				}
				points.Add(line[0]);
				flags.Add(inSite[0]);
				float carry = 0f;
				for (int i = 1; i < line.Count; i++)
				{
					Vector2 a = line[i - 1], b = line[i];
					float length = Vector2.Distance(a, b);
					if (length < 1e-4f)
					{
						continue;
					}
					float t = spacing - carry;
					while (t < length)
					{
						points.Add(Vector2.Lerp(a, b, t / length));
						flags.Add(inSite[i - 1] && inSite[i]);
						t += spacing;
					}
					carry = length - (t - spacing);
				}
				if (Vector2.Distance(points[points.Count - 1], line[line.Count - 1]) > 0.25f)
				{
					points.Add(line[line.Count - 1]);
					flags.Add(inSite[inSite.Count - 1]);
				}
				return points;
			}

			private static float PolylineLength(List<Vector2> line)
			{
				float length = 0f;
				for (int i = 1; i < line.Count; i++)
				{
					length += Vector2.Distance(line[i - 1], line[i]);
				}
				return length;
			}

			private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

			private static string Label(PointOfInterestRecord record)
				=> !string.IsNullOrWhiteSpace(record.Name) ? record.Name : $"{PointOfInterestKinds.Info(record.Kind).DisplayName} {record.Id}";
		}
	}
}
#endif
