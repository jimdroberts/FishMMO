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
		/// <summary>The water's width under it (a gorge's: rim to rim), metres.</summary>
		public float Span;
		/// <summary>True for a bridge over a gorge, rim to rim, high over its floor (PathRouter's gorge steps).</summary>
		public bool Gorge;
	}

	/// <summary>What the planner made.</summary>
	public sealed class PathPlan
	{
		public readonly List<ScenePath> Paths = new List<ScenePath>();
		public readonly List<PathBridgeSite> Bridges = new List<PathBridgeSite>();
		public readonly List<string> Notes = new List<string>();
		public int Fords;
		/// <summary>How many of <see cref="Bridges"/> span gorges rim to rim.</summary>
		public int Gorges;
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
				case POIType.Dockyard:
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

		/// <summary>
		/// How far a stretch of a way climbs past a grade (rise per metre), summed over its segments, metres: the ground's
		/// rise between points, a deck's points at <paramref name="deck"/>, with half a metre allowed for the heightmap's
		/// own roughness. Segments along a deck or inside a site are not counted.
		/// </summary>
		public static float Excess(ScenePath path, int from, int to, float grade, float deck = float.NaN)
		{
			float excess = 0f;
			for (int k = Mathf.Max(1, from + 1); k <= to; k++)
			{
				ScenePathPointFlags fa = path.FlagsAt(k - 1), fb = path.FlagsAt(k);
				if ((fa & fb & ScenePathPointFlags.Bridge) != 0 || ((fa | fb) & ScenePathPointFlags.InSite) != 0)
				{
					continue;
				}
				Vector3 a = path.Points[k - 1], b = path.Points[k];
				float ya = (fa & ScenePathPointFlags.Bridge) != 0 && !float.IsNaN(deck) ? deck : a.y;
				float yb = (fb & ScenePathPointFlags.Bridge) != 0 && !float.IsNaN(deck) ? deck : b.y;
				float run = new Vector2(b.x - a.x, b.z - a.z).magnitude;
				excess += Mathf.Max(0f, Mathf.Abs(yb - ya) - grade * run - 0.5f);
			}
			return excess;
		}

		/// <summary>
		/// Counts (and describes in <paramref name="notes"/>) the stretches of a finished way no one could walk: a segment
		/// climbing past <see cref="PathRouter.WalkableGrade"/>, or a deck whose banks differ by more than its own slope
		/// allows (<see cref="PropsFeature.MaxDeckSlopeDegrees"/>). The router, the joins and the straightening onto bridges
		/// each keep to these; this is the check that they did.
		/// </summary>
		public static int CountUnwalkable(ScenePath path, List<string> notes = null)
		{
			int found = 0;
			for (int k = 1; k < path.Count; k++)
			{
				if (Excess(path, k - 1, k, PathRouter.WalkableGrade) > 0f)
				{
					found++;
					Vector3 a = path.Points[k - 1], b = path.Points[k];
					notes?.Add($"Way {path.Id} ({path.Class}) climbs {Mathf.Abs(b.y - a.y):0.0} m in {new Vector2(b.x - a.x, b.z - a.z).magnitude:0.0} m at ({a.x:0}, {a.z:0}).");
				}
			}
			float deckGrade = Mathf.Tan(PropsFeature.MaxDeckSlopeDegrees * Mathf.Deg2Rad);
			for (int k = 0; k < path.Count; k++)
			{
				if ((path.FlagsAt(k) & ScenePathPointFlags.Bridge) == 0)
				{
					continue;
				}
				int start = k;
				while (k + 1 < path.Count && (path.FlagsAt(k + 1) & ScenePathPointFlags.Bridge) != 0)
				{
					k++;
				}
				Vector3 a = path.Points[Mathf.Max(0, start - 1)], b = path.Points[Mathf.Min(path.Count - 1, k + 1)];
				float run = new Vector2(b.x - a.x, b.z - a.z).magnitude;
				if (Mathf.Abs(b.y - a.y) > deckGrade * run + 1f)
				{
					found++;
					notes?.Add($"Way {path.Id} ({path.Class}) has a bridge between banks {Mathf.Abs(b.y - a.y):0.0} m apart in height over {run:0.0} m at ({a.x:0}, {a.z:0}).");
				}
			}
			return found;
		}

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
				plan.Notes.Add($"{plan.Paths.Count} way(s), {metres / 1000f:0.0} km: {roads} road(s), {tracks} cart track(s), {paths} footpath(s), {trails} trail(s); {plan.Bridges.Count} new bridge(s) ({plan.Gorges} over gorges), {plan.Fords} ford(s).");
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
						int joinCell = route.Cells[route.Cells.Count - 1];
						Vector2 join = NearestWayPoint(router.XOf(joinCell % router.Width), router.ZOf(joinCell / router.Width), router.Height[joinCell]);
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

			/// <summary>
			/// The point of a way laid where a spur joining at a cell meets it: the nearest within three cells that is
			/// reached at a walkable grade from the cell's height (the nearest in plan alone was once the way 60 m below a
			/// cliff's edge, and the spur stepped down to it). The cell itself when there is none.
			/// </summary>
			private Vector2 NearestWayPoint(float east, float north, float height)
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
						if (d < bestD && Mathf.Abs(path.Points[i].y - height) <= 0.5f + d * PathRouter.WalkableGrade)
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

				// A moated stronghold is reached past its moat, and crossed into on its drawbridge (the gate is laid on this
				// bearing, the drawbridge before it): the crossing is the site's, never a bridge site or a carve of its own.
				if (MoatShaper.TryMoat(input.Shapes, record.Id, out _, out float moatInner, out float moatWidth, out _))
				{
					end.Outer = Clear(centre + bearing * (moatInner + moatWidth + 4f), bearing);
					end.Inner.Add(centre + bearing * (moatInner + moatWidth * 0.5f));
					end.Inner.Add(centre + bearing * (moatInner - 1f));
					end.Inner.Add(centre + bearing * (record.Radius * 0.4f));
					return end;
				}

				/* Anything else: from the side facing the way, into the site a little; or, where that side is a cliff (a fall's
				 * site at its foot, a lookout on its edge), the nearest side round from it that is walked in on. The way runs
				 * straight from its outer point to its inner one, and once ran 60 m down a cliff to Lower Dunwy Falls. */
				for (int k = 0; k < 12; k++)
				{
					float turn = ((k + 1) / 2) * 30f * (k % 2 == 1 ? 1f : -1f) * Mathf.Deg2Rad;
					var dir = new Vector2(bearing.x * Mathf.Cos(turn) + bearing.y * Mathf.Sin(turn), -bearing.x * Mathf.Sin(turn) + bearing.y * Mathf.Cos(turn));
					Vector2 inner = centre + dir * (record.Radius * 0.4f);
					if (TryClear(inner, centre + dir * (record.Radius * 0.9f + 4f), dir, out Vector2 outer))
					{
						end.Outer = outer;
						end.Inner.Add(inner);
						return end;
					}
				}
				end.Outer = Clear(centre + bearing * (record.Radius * 0.9f + 4f), bearing);
				end.Inner.Add(centre + bearing * (record.Radius * 0.4f));
				return end;
			}

			/// <summary>The first point from <paramref name="at"/> outward a way may start from (not water, not holed, no cliff in it).</summary>
			private Vector2 Clear(Vector2 at, Vector2 outward) => TryClear(at, at, outward, out Vector2 outer) ? outer : at;

			/// <summary>
			/// The first point from <paramref name="at"/> outward a way may start from, reached from <paramref name="from"/> on
			/// walkable ground the whole way (it never looks past a cliff for one); false when there is none within 30 m.
			/// </summary>
			private bool TryClear(Vector2 from, Vector2 at, Vector2 outward, out Vector2 outer)
			{
				outer = at;
				if (!WalkableLine(from, at))
				{
					return false;
				}
				for (int step = 0; step < 10; step++)
				{
					Vector2 p = at + outward * (step * 3f);
					if (step > 0 && !WalkableLine(at + outward * ((step - 1) * 3f), p))
					{
						return false;
					}
					int i = router.IndexAt(p.x, p.y);
					if (!router.Blocked[i] && !router.IsSteep(i) && router.Water[i] != PathCellWater.Open && router.Water[i] != PathCellWater.River)
					{
						outer = p;
						return true;
					}
				}
				return false;
			}

			/// <summary>
			/// True when the ground along a straight line never climbs past <see cref="PathRouter.WalkableGrade"/>, sampled every
			/// metre from the router's half-cell ground (read once, shared with its cliff test) rather than the scene's.
			/// </summary>
			private bool WalkableLine(Vector2 a, Vector2 b)
			{
				float length = Vector2.Distance(a, b);
				int steps = Mathf.Max(1, Mathf.CeilToInt(length));
				float step = length / steps;
				float previous = router.GroundAt(a.x, a.y);
				for (int k = 1; k <= steps; k++)
				{
					Vector2 p = Vector2.Lerp(a, b, k / (float)steps);
					float h = router.GroundAt(p.x, p.y);
					if (Mathf.Abs(h - previous) > PathRouter.WalkableGrade * step + 0.25f)
					{
						return false;
					}
					previous = h;
				}
				return true;
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

			/// <summary>
			/// The route's cells as points, dropping the first and last (the exact ends stand in for them). Two cells further
			/// apart than any step are a gorge's span (<see cref="PathRouter.MaxGorgeSpan"/>): kept for <see cref="Finish"/>.
			/// </summary>
			private void AddCells(List<Vector2> line, List<bool> inSite, PathRouter.Route route, bool dropFirst, bool dropLast)
			{
				// One line per way: a route laid and then dropped (a spur too long) leaves none behind.
				gorgeSpans.Clear();
				int from = dropFirst ? 1 : 0, to = route.Cells.Count - (dropLast ? 1 : 0);
				for (int k = from; k < to; k++)
				{
					int c = route.Cells[k];
					var at = new Vector2(router.XOf(c % router.Width), router.ZOf(c / router.Width));
					if (k > from && Vector2.Distance(line[line.Count - 1], at) > GorgeStepCells * router.Cell)
					{
						gorgeSpans.Add((line[line.Count - 1], at));
					}
					line.Add(at);
					inSite.Add(false);
				}
			}

			/// <summary>A route step longer than this many cells (the longest plain step is a knight's, √5) is a gorge's span.</summary>
			private const float GorgeStepCells = 2.5f;

			/// <summary>The gorge spans of the route being finished: each span's two rim cells, in the order laid.</summary>
			private readonly List<(Vector2 a, Vector2 b)> gorgeSpans = new List<(Vector2 a, Vector2 b)>();

			// ── A finished way ─────────────────────────────────────

			/// <summary>
			/// Smooths a routed line, resamples it, and gives every point its height, width, wear, surface and flags; marks the
			/// way in the router for those after it, and the bridges it needs.
			/// </summary>
			private void Finish(List<Vector2> raw, List<bool> rawInSite, ScenePathClass way, PointOfInterestRecord from, PointOfInterestRecord to,
				float fromEntrance, float toEntrance, float wearScale, bool spur)
			{
				ScenePathStyle style = ScenePathStyle.For(way);
				// A gorge's span stays straight through the smoothing (its rim cells pinned), and is not inside a site.
				var spans = new List<(Vector2 a, Vector2 b)>(gorgeSpans);
				gorgeSpans.Clear();
				var pins = new List<bool>(rawInSite);
				for (int k = 0; k < raw.Count; k++)
				{
					foreach ((Vector2 a, Vector2 b) in spans)
					{
						pins[k] |= raw[k] == a || raw[k] == b;
					}
				}
				List<Vector2> smooth = Smooth(raw, pins, out List<bool> smoothPinned);
				var smoothInSite = new List<bool>(smoothPinned.Count);
				for (int k = 0; k < smooth.Count; k++)
				{
					bool rimCell = spans.Exists(span => smooth[k] == span.a || smooth[k] == span.b);
					smoothInSite.Add(smoothPinned[k] && !rimCell);
				}
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
				Gorges(path, spans);
				Crossings(path, style);
				// The check that nothing above made a stretch no one could walk (the plan's notes say where).
				CountUnwalkable(path, plan.Notes);
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
					int bridge = router.Bridge[router.IndexAt(p.x, p.z)];
					if (bridge != 0)
					{
						// Over a bridge already standing (or asked for by an earlier way): onto its deck along its axis.
						int start = i;
						while (i < n && router.Bridge[router.IndexAt(path.Points[i].x, path.Points[i].z)] == bridge)
						{
							i++;
						}
						if (BridgeAxis(bridge, out Vector3 centre, out float yaw, out float planned))
						{
							Straighten(path, start, i - 1, centre, yaw, planned > 0f ? planned : RunHalf(path, start, i - 1, centre, yaw));
						}
						else
						{
							for (int k = start; k < i; k++)
							{
								path.Flags[k] |= (byte)ScenePathPointFlags.Bridge;
							}
						}
						continue;
					}
					if (input.WaterAt == null || input.WaterAt(p.x, p.z) != PathCellWater.River)
					{
						i++;
						continue;
					}
					// A run of river: wade it or bridge it, on its deepest point.
					int first = i;
					float deepest = 0f;
					int deepestAt = i;
					while (i < n && input.WaterAt(path.Points[i].x, path.Points[i].z) == PathCellWater.River)
					{
						float depth = input.WaterDepth != null ? input.WaterDepth(path.Points[i].x, path.Points[i].z) : 0f;
						if (depth > deepest)
						{
							deepest = depth;
							deepestAt = i;
						}
						i++;
					}
					int last = i - 1;
					if (deepest <= style.FordDepth)
					{
						for (int k = Mathf.Max(0, first - 2); k <= Mathf.Min(n - 1, last + 2); k++)
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
					int a = Mathf.Max(0, first - 1), b = Mathf.Min(n - 1, last + 1);
					Vector3 pa = path.Points[a], pb = path.Points[b];
					// Square across the river from its deepest point, not along the way's run through it (AcrossRiver).
					Vector3 deep = path.Points[deepestAt];
					AcrossRiver(new Vector2(deep.x, deep.z), PointOfInterestPlanner.YawOf(pb.x - pa.x, pb.z - pa.z),
						out float acrossYaw, out Vector2 middle, out float width);
					float yr = acrossYaw * Mathf.Deg2Rad;
					var axis = new Vector2(Mathf.Sin(yr), Mathf.Cos(yr));
					Vector2 bankA = middle - axis * (width * 0.5f + 1f), bankB = middle + axis * (width * 0.5f + 1f);
					float bank = Mathf.Max(input.Ground(bankA.x, bankA.y), input.Ground(bankB.x, bankB.y));
					// Where the way steps off the deck at each end.
					Vector2 offA = middle - axis * (width * 0.5f + BridgeAbutmentMetres), offB = middle + axis * (width * 0.5f + BridgeAbutmentMetres);
					float groundA = input.Ground(offA.x, offA.y), groundB = input.Ground(offB.x, offB.y);

					// A bridge just laid a few metres off (this way's own last crossing of a river it runs beside, or
					// another way's) serves this one too: two decks side by side, or one across the other, read as neither.
					int near = NearBridge(middle, bank, width * 0.5f);
					if (near != 0 && BridgeAxis(near, out Vector3 nearCentre, out float nearYaw, out float nearHalf))
					{
						Straighten(path, a, b, nearCentre, nearYaw, nearHalf > 0f ? nearHalf : RunHalf(path, a, b, nearCentre, nearYaw));
						while (i < n && router.Bridge[router.IndexAt(path.Points[i].x, path.Points[i].z)] == near)
						{
							i++;
						}
						continue;
					}

					// No deck between banks further apart in height than it may slope (a fall's head and foot): the way
					// wades there rather than climb a bridge, and the walkability check below says so.
					if (Mathf.Abs(groundA - groundB) > Mathf.Tan(PropsFeature.MaxDeckSlopeDegrees * Mathf.Deg2Rad) * (width + 2f * BridgeAbutmentMetres) + 1f)
					{
						for (int k = first; k <= last; k++)
						{
							path.Flags[k] |= (byte)ScenePathPointFlags.Ford;
						}
						plan.Notes.Add($"Way {path.Id} meets a river between banks {Mathf.Abs(groundA - groundB):0.0} m apart in height at ({middle.x:0}, {middle.y:0}): no bridge laid there.");
						continue;
					}

					var site = new PathBridgeSite
					{
						PathId = path.Id,
						Centre = new Vector3(middle.x, bank, middle.y),
						Yaw = acrossYaw,
						Span = width,
					};
					plan.Bridges.Add(site);
					// Every way after this one may use it.
					int id = nextBridgeId--;
					router.MarkBridge(id, site.Centre, site.Yaw, site.Span * 0.5f + 2f);
					// The deck reaches a little past the water (PropsFeature.MeasureCrossing): the way runs straight onto it.
					Straighten(path, a, b, site.Centre, site.Yaw, site.Span * 0.5f + BridgeAbutmentMetres);
					// Past the points just laid on its deck: they are this crossing, not another bridge to straighten onto.
					while (i < n && router.Bridge[router.IndexAt(path.Points[i].x, path.Points[i].z)] == id)
					{
						i++;
					}
				}
			}

			/// <summary>
			/// Asks for a bridge over each gorge the route spans rim to rim: across from one rim cell to the other, at the lower
			/// rim's height (PropsFeature then lays its deck lip to lip), marked in the router so <see cref="Crossings"/> lays the
			/// way onto it and every way after may use it.
			/// </summary>
			private void Gorges(ScenePath path, List<(Vector2 a, Vector2 b)> spans)
			{
				foreach ((Vector2 a, Vector2 b) in spans)
				{
					Vector2 middle = (a + b) * 0.5f;
					float rim = Mathf.Min(input.Ground(a.x, a.y), input.Ground(b.x, b.y));
					var site = new PathBridgeSite
					{
						PathId = path.Id,
						Centre = new Vector3(middle.x, rim, middle.y),
						Yaw = PointOfInterestPlanner.YawOf(b.x - a.x, b.y - a.y),
						Span = Vector2.Distance(a, b),
						Gorge = true,
					};
					plan.Bridges.Add(site);
					router.MarkBridge(nextBridgeId--, site.Centre, site.Yaw, site.Span * 0.5f + 2f);
					plan.Gorges++;
				}
			}

			/// <summary>How far past the water's edge a bridge's deck is reckoned to reach, metres.</summary>
			private const float BridgeAbutmentMetres = 2.5f;
			/// <summary>Straight ground the way keeps beyond a deck's end before it bends away, metres.</summary>
			private const float BridgeApproachMetres = 5f;
			/// <summary>Over which, past the straight, the way eases back into its own line, metres.</summary>
			private const float BridgeEaseMetres = 12f;

			/// <summary>
			/// A bridge's centre and heading: a standing record's, or one an earlier way asked for (negative ids), whose
			/// planned deck half length comes too (0 for a record, whose deck the way's own run measures).
			/// </summary>
			private bool BridgeAxis(int id, out Vector3 centre, out float yaw, out float half)
			{
				centre = default;
				yaw = 0f;
				half = 0f;
				if (id < 0)
				{
					int index = -id - 1;
					if (index < plan.Bridges.Count)
					{
						centre = plan.Bridges[index].Centre;
						yaw = plan.Bridges[index].Yaw;
						half = plan.Bridges[index].Span * 0.5f + BridgeAbutmentMetres;
						return true;
					}
					return false;
				}
				foreach (PointOfInterestRecord record in input.Records)
				{
					if (record != null && record.Id == id)
					{
						centre = record.Position;
						yaw = record.Yaw;
						return true;
					}
				}
				return false;
			}

			/// <summary>How far either side of the way's own heading a bridge may turn to cross its river square, degrees.</summary>
			private const float BridgeTurnDegrees = 40f;

			/// <summary>
			/// How far from a bridge just laid (past its half span) another crossing is served by it rather than given its
			/// own, metres; and how far below or above it the other may lie and still be the same water.
			/// </summary>
			private const float BridgeShareMetres = 8f, BridgeShareRiseMetres = 4f;

			/// <summary>
			/// The shortest line across the river through a point in it, within <see cref="BridgeTurnDegrees"/> of the way's
			/// heading: the bridge's heading (degrees), the middle of the water on it and the water's width along it.
			/// </summary>
			/// <remarks>
			/// The bridge used to take the way's heading through the run of river and the run's length: a way crossing aslant,
			/// or running beside a river and dipping into it, laid its bridge along the water, so its deck ran down the river or
			/// off a fall's lip with one end over nothing (2026-10-12, Flo Monolith). The way is straightened onto the
			/// bridge's heading either side of it, so it turns to meet the deck square.
			/// </remarks>
			private void AcrossRiver(Vector2 at, float wayYaw, out float yaw, out Vector2 middle, out float width)
			{
				const float Step = 0.5f, MaxRun = 60f, Turn = 5f;
				yaw = wayYaw;
				middle = at;
				width = float.MaxValue;
				int turns = Mathf.RoundToInt(BridgeTurnDegrees / Turn);
				// The way's own heading first, then turning out either side: a tie keeps the heading nearest the way's.
				for (int k = 0; k <= 2 * turns; k++)
				{
					float candidate = wayYaw + ((k + 1) / 2) * Turn * (k % 2 == 1 ? 1f : -1f);
					float r = candidate * Mathf.Deg2Rad;
					var dir = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
					float ahead = 0f, behind = 0f;
					while (ahead < MaxRun && IsRiver(at + dir * (ahead + Step)))
					{
						ahead += Step;
					}
					while (behind < MaxRun && IsRiver(at - dir * (behind + Step)))
					{
						behind += Step;
					}
					float across = ahead + behind + Step;
					if (across < width - 1e-3f)
					{
						width = across;
						yaw = Mathf.Repeat(candidate, 360f);
						middle = at + dir * (0.5f * (ahead - behind));
					}
				}
			}

			private bool IsRiver(Vector2 at) => input.WaterAt != null && input.WaterAt(at.x, at.y) == PathCellWater.River;

			/// <summary>
			/// A bridge laid by an earlier crossing (an earlier way's, or this way's own) within <see cref="BridgeShareMetres"/>
			/// past its half span of a crossing here, at much the same height: its id, or 0.
			/// </summary>
			private int NearBridge(Vector2 at, float height, float half)
			{
				int found = 0;
				float best = float.MaxValue;
				for (int k = 0; k < plan.Bridges.Count; k++)
				{
					PathBridgeSite other = plan.Bridges[k];
					float d = Vector2.Distance(at, new Vector2(other.Centre.x, other.Centre.z));
					if (d <= Mathf.Max(half, other.Span * 0.5f) + BridgeShareMetres && Mathf.Abs(other.Centre.y - height) <= BridgeShareRiseMetres && d < best)
					{
						best = d;
						found = -k - 1;
					}
				}
				return found;
			}

			/// <summary>How far along a bridge's axis a run of the way's points reaches, metres: the deck's half length it used.</summary>
			private static float RunHalf(ScenePath path, int first, int last, Vector3 centre, float yaw)
			{
				float r = yaw * Mathf.Deg2Rad;
				var axis = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
				float half = 0f;
				for (int k = first; k <= last; k++)
				{
					half = Mathf.Max(half, Mathf.Abs(Vector2.Dot(new Vector2(path.Points[k].x - centre.x, path.Points[k].z - centre.z), axis)));
				}
				return half;
			}

			/// <summary>
			/// Lays the way straight along a bridge's axis over its deck and a few metres either side, easing back into its own
			/// line beyond: a way that bends onto a bridge meets it at an angle, and the deck's railings stand across it
			/// (2026-10-11). Flags the points on the deck as bridged and no others near it.
			/// </summary>
			/// <remarks>
			/// Never steeper than the way was routed: the straight line onto a deck can run up a bank the route went round (a
			/// fall's cliff beside the bridge's end), so the approach is straightened only as far as it stays as walkable as
			/// the way's own line, at full, half or none; the deck itself always lies on the axis.
			/// </remarks>
			private void Straighten(ScenePath path, int first, int last, Vector3 centre, float yaw, float half)
			{
				float r = yaw * Mathf.Deg2Rad;
				var axis = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
				var across = new Vector2(-axis.y, axis.x);
				float straight = half + BridgeApproachMetres, ease = straight + BridgeEaseMetres;
				int window = Mathf.CeilToInt(ease / PointSpacing) + 2;
				int lo = Mathf.Max(0, first - window), hi = Mathf.Min(path.Count - 1, last + window);
				var original = new Vector3[hi - lo + 1];
				var originalFlags = new byte[hi - lo + 1];
				for (int k = lo; k <= hi; k++)
				{
					original[k - lo] = path.Points[k];
					originalFlags[k - lo] = path.Flags[k];
				}

				void Lay(float strength)
				{
					for (int k = lo; k <= hi; k++)
					{
						path.Points[k] = original[k - lo];
						path.Flags[k] = originalFlags[k - lo];
						if ((path.FlagsAt(k) & ScenePathPointFlags.InSite) != 0)
						{
							continue;
						}
						var offset = new Vector2(original[k - lo].x - centre.x, original[k - lo].z - centre.z);
						float along = Vector2.Dot(offset, axis), side = Vector2.Dot(offset, across);
						if (Mathf.Abs(along) > ease || Mathf.Abs(side) > ease)
						{
							continue;
						}
						/* Eased back by how far out it lies either way: by its distance along the axis alone, a way leaving a
						 * bridge turned square across its river (AcrossRiver) reached the side limit above while still pulled
						 * most of the way in, and jumped back to its own line from one point to the next. */
						float keep = Mathf.Max(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(straight, ease, Mathf.Abs(along))),
							Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(straight, ease, Mathf.Abs(side))));
						bool deck = Mathf.Abs(along) <= half;
						Vector2 placed = new Vector2(centre.x, centre.z) + axis * along + across * (side * keep);
						if (!deck)
						{
							placed = Vector2.Lerp(new Vector2(original[k - lo].x, original[k - lo].z), placed, strength);
						}
						path.Points[k] = new Vector3(placed.x, input.Ground(placed.x, placed.y), placed.y);
						if (deck)
						{
							path.Flags[k] |= (byte)ScenePathPointFlags.Bridge;
						}
						else
						{
							path.Flags[k] &= unchecked((byte)~ScenePathPointFlags.Bridge);
						}
					}
				}

				// The way's own line onto the deck is the measure; the straightest no steeper than it is taken.
				float limit = PathRouter.MaxGradeOf(path.Class);
				Lay(0f);
				float own = Excess(path, Mathf.Max(0, lo - 1), Mathf.Min(path.Count - 1, hi + 1), limit, centre.y);
				foreach (float strength in Straightness)
				{
					Lay(strength);
					if (strength <= 0f || Excess(path, Mathf.Max(0, lo - 1), Mathf.Min(path.Count - 1, hi + 1), limit, centre.y) <= own + 1e-3f)
					{
						break;
					}
				}
			}

			/// <summary>How fully the approach to a bridge is straightened, tried in turn (<see cref="Straighten"/>).</summary>
			private static readonly float[] Straightness = { 1f, 0.5f, 0f };

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
