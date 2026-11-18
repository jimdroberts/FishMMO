using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// A settlement's market place (2026-10-20: "Town centers (market area with well?) are generally too cramped and need to be spread
	/// out a bit more"): the square is sized to the settlement, the well or cross stands in it with walking room all round, the stalls
	/// stand in rows with aisles a cart passes, the other centrepieces apart, and the houses front the square's edge, never in it.
	/// </summary>
	[TestFixture]
	public class MarketPlaceTests
	{
		[SetUp]
		public void RegisterKit() => StructureKitPieceSource.Register();

		private const int Seeds = 6;

		/// <summary>Lays a spec at a size as PropsFeature does (its counts held to the site's tally, its wells and stalls flagged).</summary>
		private static List<PointOfInterestLayoutPiece> Lay(PointOfInterestTemplateSpec spec, int size, int seed, out PointOfInterestLayoutRequest request, bool terraced = false)
		{
			PointOfInterestKindRule rule = PointOfInterestRules.Default().RuleFor(spec.Kind);
			float radius = rule != null ? Mathf.Lerp(rule.FootprintMin, rule.FootprintMax, size / 2f) : 40f;
			var random = new DeterministicRNG(9100 + seed * 37 + size);
			var caps = new PointOfInterestStructureCaps.Tally(spec.Kind);
			request = new PointOfInterestLayoutRequest
			{
				Layout = spec.Layout, Radius = radius, Gap = 1f, Kind = spec.Kind, Seed = seed * 7919 + size,
				SpanHalf = radius, ShoreAhead = radius * PointOfInterestLayouts.Inset,
			};
			if (terraced)
			{
				// Three terraces up a hill, as a hill town is laid on (PointOfInterestTownPlan.HillTown).
				request.Pad = new PointOfInterestPad
				{
					Id = 1, Centre = new Vector3(0f, 50f, 0f), Radius = radius, TerraceAxis = Vector2.up,
					TerraceSteps = new[] { -0.26f * radius, 0.28f * radius }, TerraceLevels = new[] { 45f, 49f, 53f },
					TerraceRamps = new[] { 0.24f * radius, -0.24f * radius }, RampHalfWidth = 4f,
				};
			}
			for (int s = 0; s < spec.Pieces.Count; s++)
			{
				PointOfInterestPieceSlot slot = spec.Pieces[s];
				string style = string.IsNullOrEmpty(slot.Style) ? spec.Style : slot.Style;
				PointOfInterestSlotRole role = PointOfInterestLayouts.RoleFor(spec.Layout, s, slot.Role);
				int count = PropsFeature.CountFor(slot, size, true, random);
				if (!PointOfInterestLayouts.Generates(role))
				{
					count = caps.Take(PointOfInterestStructureCaps.TypeOfQuery(slot.Tag), count);
				}
				PointOfInterestPieceMetrics metrics = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, 0));
				bool station = role == PointOfInterestSlotRole.Gate || role == PointOfInterestSlotRole.WallTower;
				request.Slots.Add(new PointOfInterestLayoutSlot
				{
					Role = role,
					Footprint = Vector2.Max(metrics.Footprint, metrics.Sweep),
					Module = metrics.Module,
					Count = count,
					Walk = role == PointOfInterestSlotRole.Perimeter ? PropsFeature.PerimeterWalk(slot.Tag, style) : 0f,
					Join = station ? PropsFeature.StationJoin(slot.Tag, style, 0) : 0f,
					Landmark = PointOfInterestTemplateSpecs.IsLandmark(slot.Tag, spec.Kind),
					Well = slot.Tag == PointOfInterestTemplateSpecs.Well,
					Stall = slot.Tag == PointOfInterestTemplateSpecs.Stall,
				});
				if (PointOfInterestLayouts.Generates(role))
				{
					continue;
				}
				for (int k = 0; k < count; k++)
				{
					PointOfInterestPieceMetrics piece = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, random.Next()));
					Vector2 footprint = Vector2.Max(piece.Footprint, piece.Sweep);
					if (slot.Tag == PointOfInterestTemplateSpecs.Palace)
					{
						footprint = PointOfInterestPalace.Reserve(radius, 1f);
					}
					request.Items.Add(new PointOfInterestLayoutItem { Slot = s, Footprint = footprint });
				}
			}
			return PointOfInterestLayouts.Place(request, random);
		}

		/// <summary>The settlements' templates whose market this is about (every kind whose middle is a market).</summary>
		private static IEnumerable<PointOfInterestTemplateSpec> Settlements()
		{
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All())
			{
				if ((spec.Kind == POIType.Village || spec.Kind == POIType.Town || spec.Kind == POIType.City || spec.Kind == POIType.Capital || spec.Kind == POIType.Port)
					&& (spec.Layout == PointOfInterestLayout.Street || spec.Layout == PointOfInterestLayout.Walled)
					&& spec.Pieces.Exists(p => p.Tag == PointOfInterestTemplateSpecs.Well || p.Tag == PointOfInterestTemplateSpecs.Stall))
				{
					yield return spec;
				}
			}
		}

		/// <summary>Every settlement as it is laid on level ground, and the villages and towns again on a terraced hill.</summary>
		private static IEnumerable<(PointOfInterestTemplateSpec spec, bool terraced)> Laid()
		{
			foreach (PointOfInterestTemplateSpec spec in Settlements())
			{
				yield return (spec, false);
			}
			foreach (PointOfInterestTemplateSpec spec in Settlements())
			{
				if (spec.Kind == POIType.Village || spec.Kind == POIType.Town)
				{
					yield return (spec, true);
				}
			}
		}

		// ── Measuring ─────────────────────────────────────────────

		/// <summary>The clear ground between two footprints (rectangles at centres and yaws), metres; 0 where they touch or overlap.</summary>
		private static float Clearance(PointOfInterestLayoutPiece a, PointOfInterestLayoutPiece b)
		{
			if (PointOfInterestLayouts.Overlaps(a.Offset, a.Yaw, a.Footprint, b.Offset, b.Yaw, b.Footprint))
			{
				return 0f;
			}
			var ca = new Vector2[4];
			var cb = new Vector2[4];
			PointOfInterestLayouts.Corners(a.Offset, a.Yaw, a.Footprint, ca);
			PointOfInterestLayouts.Corners(b.Offset, b.Yaw, b.Footprint, cb);
			float best = float.PositiveInfinity;
			for (int i = 0; i < 4; i++)
			{
				for (int j = 0; j < 4; j++)
				{
					best = Mathf.Min(best, PointToSegment(ca[i], cb[j], cb[(j + 1) % 4]));
					best = Mathf.Min(best, PointToSegment(cb[j], ca[i], ca[(i + 1) % 4]));
				}
			}
			return best;
		}

		private static float PointToSegment(Vector2 p, Vector2 a, Vector2 b)
		{
			Vector2 ab = b - a;
			float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
			return Vector2.Distance(a + ab * t, p);
		}

		private static float Area(Vector2[] polygon)
		{
			float twice = 0f;
			for (int i = 0; i < polygon.Length; i++)
			{
				Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
				twice += a.x * b.y - a.y * b.x;
			}
			return Mathf.Abs(twice) * 0.5f;
		}

		private static bool InPolygon(Vector2[] polygon, Vector2 p)
		{
			bool inside = false;
			for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
			{
				if ((polygon[i].y > p.y) != (polygon[j].y > p.y)
					&& p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
				{
					inside = !inside;
				}
			}
			return inside;
		}

		/// <summary>A centre piece that is the citadel (a keep, the palace), not a market's piece.</summary>
		private static bool Citadel(PointOfInterestLayoutPiece piece) => Mathf.Max(piece.Footprint.x, piece.Footprint.y) >= 10f;

		private static bool IsMarket(PointOfInterestLayoutPiece piece) => piece.Item >= 0 && piece.Role == PointOfInterestSlotRole.Centre && !Citadel(piece);

		/// <summary>A building the market keeps its distance from: a street's, the edge's, the wall's, the citadel.</summary>
		private static bool IsBuilding(PointOfInterestLayoutPiece piece)
			=> piece.Role == PointOfInterestSlotRole.Street || piece.Role == PointOfInterestSlotRole.Edge || piece.Role == PointOfInterestSlotRole.Gate
				|| piece.Role == PointOfInterestSlotRole.WallTower || piece.Role == PointOfInterestSlotRole.Perimeter
				|| (piece.Role == PointOfInterestSlotRole.Centre && Citadel(piece));

		/// <summary>One site's market, measured.</summary>
		private struct Market
		{
			public string Plan;
			public float Square, Open;
			public int Asked, Laid, Inside, StallsAsked, StallsLaid, HousesAsked, HousesLaid, CitadelsAsked, CitadelsLaid;
			/// <summary>Each market piece's clearance to its nearest other market piece, and to its nearest building.</summary>
			public List<float> Between, ToBuildings;
			/// <summary>Each market piece that is no stall: its clearance to its nearest other market piece.</summary>
			public List<float> Fixtures;
			/// <summary>The stalls laid outside the square.</summary>
			public int StallsOut;
			/// <summary>The centrepiece's clear ground all round: to the nearest piece of any kind.</summary>
			public float Focal;
			public float Spread;
		}

		private static Market Measure(PointOfInterestTemplateSpec spec, int size, int seed, bool terraced = false)
		{
			List<PointOfInterestLayoutPiece> pieces = Lay(spec, size, seed, out PointOfInterestLayoutRequest request, terraced);
			var m = new Market { Plan = request.ChosenPlan.ToString(), Between = new List<float>(), ToBuildings = new List<float>(), Fixtures = new List<float>(), Focal = float.NaN };
			Vector2[] square = request.Plazas.Count > 0 ? request.Plazas[0] : null;
			m.Square = square != null ? Area(square) : 0f;
			m.Open = m.Square;
			for (int i = 0; i < request.Items.Count; i++)
			{
				PointOfInterestLayoutItem item = request.Items[i];
				PointOfInterestSlotRole role = request.Slots[item.Slot].Role;
				bool citadel = Mathf.Max(item.Footprint.x, item.Footprint.y) >= 10f;
				m.CitadelsAsked += role == PointOfInterestSlotRole.Centre && citadel ? 1 : 0;
				if (role == PointOfInterestSlotRole.Centre && !citadel)
				{
					m.Asked++;
					m.StallsAsked += spec.Pieces[item.Slot].Tag == PointOfInterestTemplateSpecs.Stall ? 1 : 0;
				}
				if (role == PointOfInterestSlotRole.Street && !request.Slots[item.Slot].Landmark)
				{
					m.HousesAsked++;
				}
			}
			var market = pieces.FindAll(IsMarket);
			m.Laid = market.Count;
			foreach (PointOfInterestLayoutPiece piece in pieces)
			{
				if (piece.Item >= 0 && piece.Role == PointOfInterestSlotRole.Street && !request.Slots[piece.Slot].Landmark)
				{
					m.HousesLaid++;
				}
				m.CitadelsLaid += piece.Item >= 0 && piece.Role == PointOfInterestSlotRole.Centre && Citadel(piece) ? 1 : 0;
			}
			Vector2 middle = market.Count > 0 ? market[0].Offset : Vector2.zero;
			foreach (PointOfInterestLayoutPiece piece in market)
			{
				m.StallsLaid += spec.Pieces[piece.Slot].Tag == PointOfInterestTemplateSpecs.Stall ? 1 : 0;
				if (square != null && InPolygon(square, piece.Offset))
				{
					m.Inside++;
					m.Open -= piece.Footprint.x * piece.Footprint.y;
				}
				m.Spread = Mathf.Max(m.Spread, Vector2.Distance(piece.Offset, middle));
				float between = float.PositiveInfinity, building = float.PositiveInfinity;
				foreach (PointOfInterestLayoutPiece other in pieces)
				{
					if (other.Offset == piece.Offset && other.Slot == piece.Slot)
					{
						continue;
					}
					if (IsMarket(other))
					{
						between = Mathf.Min(between, Clearance(piece, other));
					}
					else if (IsBuilding(other))
					{
						building = Mathf.Min(building, Clearance(piece, other));
					}
				}
				bool stall = spec.Pieces[piece.Slot].Tag == PointOfInterestTemplateSpecs.Stall;
				if (!float.IsPositiveInfinity(between))
				{
					m.Between.Add(between);
					if (!stall)
					{
						m.Fixtures.Add(between);
					}
				}
				m.StallsOut += stall && (square == null || !InPolygon(square, piece.Offset)) ? 1 : 0;
				if (!float.IsPositiveInfinity(building))
				{
					m.ToBuildings.Add(building);
				}
			}
			if (market.Count > 0)
			{
				float focal = float.PositiveInfinity;
				foreach (PointOfInterestLayoutPiece other in pieces)
				{
					if (!(other.Offset == market[0].Offset && other.Slot == market[0].Slot) && other.Role != PointOfInterestSlotRole.Perimeter)
					{
						focal = Mathf.Min(focal, Clearance(market[0], other));
					}
				}
				m.Focal = focal;
			}
			return m;
		}

		private static float Median(List<float> values)
		{
			if (values.Count == 0)
			{
				return float.NaN;
			}
			var sorted = new List<float>(values);
			sorted.Sort();
			return sorted[sorted.Count / 2];
		}

		private static float Min(List<float> values)
		{
			float least = float.NaN;
			foreach (float v in values)
			{
				least = float.IsNaN(least) ? v : Mathf.Min(least, v);
			}
			return least;
		}

		/// <summary>
		/// The probe: every settlement's market at every size over a few seeds, the square's area and its open ground, the clearances
		/// between the market's pieces and to the buildings round it, the centrepiece's clear ring, and what was laid of what was asked.
		/// </summary>
		[Test]
		public void Report()
		{
			Console.WriteLine("template | size | plans | square m2 | open m2 | market laid/asked (in square) | stalls laid/asked (outside) | between min/med | fixtures between min/med | to buildings min/med | focal clear min/med | spread | houses laid/asked | citadel laid/asked");
			foreach ((PointOfInterestTemplateSpec spec, bool terraced) in Laid())
			{
				foreach (int size in spec.Sizes())
				{
					float square = 0f, open = 0f, spread = 0f;
					int asked = 0, laid = 0, inside = 0, stallsAsked = 0, stallsLaid = 0, housesAsked = 0, housesLaid = 0, citadelsAsked = 0, citadelsLaid = 0;
					var between = new List<float>();
					var fixtures = new List<float>();
					int stallsOut = 0;
					var toBuildings = new List<float>();
					var focal = new List<float>();
					var plans = new SortedDictionary<string, int>(StringComparer.Ordinal);
					for (int seed = 1; seed <= Seeds; seed++)
					{
						Market m = Measure(spec, size, seed, terraced);
						plans[m.Plan] = plans.TryGetValue(m.Plan, out int n) ? n + 1 : 1;
						square += m.Square / Seeds;
						open += m.Open / Seeds;
						spread += m.Spread / Seeds;
						asked += m.Asked;
						laid += m.Laid;
						inside += m.Inside;
						stallsAsked += m.StallsAsked;
						stallsLaid += m.StallsLaid;
						housesAsked += m.HousesAsked;
						housesLaid += m.HousesLaid;
						citadelsAsked += m.CitadelsAsked;
						citadelsLaid += m.CitadelsLaid;
						between.AddRange(m.Between);
						fixtures.AddRange(m.Fixtures);
						stallsOut += m.StallsOut;
						toBuildings.AddRange(m.ToBuildings);
						if (!float.IsNaN(m.Focal))
						{
							focal.Add(m.Focal);
						}
					}
					var planList = new List<string>();
					foreach (KeyValuePair<string, int> p in plans)
					{
						planList.Add($"{p.Key}x{p.Value}");
					}
					Console.WriteLine($"{spec.Name}{(terraced ? " (terraced)" : "")} | {size} | {string.Join(" ", planList)} | {square:0} | {open:0} | {laid}/{asked} ({inside}) | {stallsLaid}/{stallsAsked} ({stallsOut}) | "
						+ $"{Min(between):0.0}/{Median(between):0.0} | {Min(fixtures):0.0}/{Median(fixtures):0.0} | {Min(toBuildings):0.0}/{Median(toBuildings):0.0} | {Min(focal):0.0}/{Median(focal):0.0} | {spread:0.0} | {housesLaid}/{housesAsked} | {citadelsLaid}/{citadelsAsked}");
				}
			}
		}

		// ── What the market keeps to ──────────────────────────────

		/// <summary>Every settlement's market on level ground at every size and seed: its spec, size, request and pieces.</summary>
		private static IEnumerable<(PointOfInterestTemplateSpec spec, int size, int seed, PointOfInterestLayoutRequest request, List<PointOfInterestLayoutPiece> pieces)> Markets()
		{
			foreach (PointOfInterestTemplateSpec spec in Settlements())
			{
				foreach (int size in spec.Sizes())
				{
					for (int seed = 1; seed <= Seeds; seed++)
					{
						List<PointOfInterestLayoutPiece> pieces = Lay(spec, size, seed, out PointOfInterestLayoutRequest request);
						yield return (spec, size, seed, request, pieces);
					}
				}
			}
		}

		private static bool IsStall(PointOfInterestTemplateSpec spec, PointOfInterestLayoutPiece piece) => spec.Pieces[piece.Slot].Tag == PointOfInterestTemplateSpecs.Stall;

		private static bool Same(PointOfInterestLayoutPiece a, PointOfInterestLayoutPiece b) => a.Item == b.Item && a.Slot == b.Slot && a.Offset == b.Offset;

		/// <summary>
		/// Nothing of the market stands within a stride and a half of another piece of it (they were spiralled out a metre apart, the
		/// least gap), and the well (the first piece laid) keeps its walking room all round from everything.
		/// </summary>
		[Test]
		public void AMarketsPiecesKeepRoomBetweenThem_AndAllRoundTheWell()
		{
			var problems = new List<string>();
			foreach ((PointOfInterestTemplateSpec spec, int size, int seed, PointOfInterestLayoutRequest request, List<PointOfInterestLayoutPiece> pieces) in Markets())
			{
				List<PointOfInterestLayoutPiece> market = pieces.FindAll(IsMarket);
				for (int i = 0; i < market.Count; i++)
				{
					for (int j = i + 1; j < market.Count; j++)
					{
						float clear = Clearance(market[i], market[j]);
						if (clear < 1.4f)
						{
							problems.Add($"{spec.Name} size {size} seed {seed}: {spec.Pieces[market[i].Slot].Tag} and {spec.Pieces[market[j].Slot].Tag} {clear:0.00} m apart");
						}
					}
				}
				if (market.Count > 0)
				{
					foreach (PointOfInterestLayoutPiece other in pieces)
					{
						if (!Same(other, market[0]) && other.Role != PointOfInterestSlotRole.Perimeter && Clearance(market[0], other) < 2.8f)
						{
							problems.Add($"{spec.Name} size {size} seed {seed}: the {spec.Pieces[market[0].Slot].Tag} has {Clearance(market[0], other):0.00} m to a {other.Role} {spec.Pieces[other.Slot].Tag}");
						}
					}
				}
			}
			Assert.That(problems, Is.Empty, string.Join("\n", problems));
		}

		/// <summary>
		/// The stalls stand in the square, not among the houses round it (all but a few of a site's, and nearly all of them together),
		/// and nearly every piece of the market keeps a walk from the buildings round it.
		/// </summary>
		[Test]
		public void TheMarketStandsInItsSquare_ClearOfTheHousesRoundIt()
		{
			var problems = new List<string>();
			int stalls = 0, outside = 0, pieces = 0, crowded = 0;
			var bySize = new Dictionary<string, (int stalls, int outside)>();
			foreach ((PointOfInterestTemplateSpec spec, int size, int seed, PointOfInterestLayoutRequest request, List<PointOfInterestLayoutPiece> laid) in Markets())
			{
				Vector2[] square = request.Plazas.Count > 0 ? request.Plazas[0] : null;
				Assert.That(square, Is.Not.Null, $"{spec.Name} size {size} seed {seed}: a market square");
				string key = $"{spec.Name} size {size}";
				bySize.TryGetValue(key, out (int stalls, int outside) tally);
				foreach (PointOfInterestLayoutPiece piece in laid.FindAll(IsMarket))
				{
					if (IsStall(spec, piece))
					{
						stalls++;
						tally.stalls++;
						if (!InPolygon(square, piece.Offset))
						{
							outside++;
							tally.outside++;
						}
					}
					pieces++;
					float nearest = float.PositiveInfinity;
					foreach (PointOfInterestLayoutPiece other in laid)
					{
						if (IsBuilding(other))
						{
							nearest = Mathf.Min(nearest, Clearance(piece, other));
						}
					}
					crowded += nearest < PointOfInterestLayouts.SquareEdgeMetres ? 1 : 0;
				}
				bySize[key] = tally;
			}
			foreach (KeyValuePair<string, (int stalls, int outside)> t in bySize)
			{
				if (t.Value.outside > Mathf.Max(2, t.Value.stalls / 4))
				{
					problems.Add($"{t.Key}: {t.Value.outside} of {t.Value.stalls} stalls outside the square");
				}
			}
			Assert.That(problems, Is.Empty, string.Join("\n", problems));
			Assert.That(outside, Is.LessThanOrEqualTo(stalls * 0.03f), $"{outside} of {stalls} stalls outside their squares");
			Assert.That(crowded, Is.LessThanOrEqualTo(pieces * 0.05f), $"{crowded} of {pieces} market pieces within {PointOfInterestLayouts.SquareEdgeMetres} m of a building");
		}

		/// <summary>A village's green is small, a town's market place larger, a city's square larger again and the capital's the largest.</summary>
		[Test]
		public void TheSquareGrowsWithTheSettlement()
		{
			var areas = new Dictionary<POIType, (float sum, int n)>();
			var least = new Dictionary<POIType, float>();
			foreach ((PointOfInterestTemplateSpec spec, int size, int seed, PointOfInterestLayoutRequest request, List<PointOfInterestLayoutPiece> _) in Markets())
			{
				float area = Area(request.Plazas[0]);
				least[spec.Kind] = least.TryGetValue(spec.Kind, out float l) ? Mathf.Min(l, area) : area;
				if (size == 2)
				{
					areas.TryGetValue(spec.Kind, out (float sum, int n) a);
					areas[spec.Kind] = (a.sum + area, a.n + 1);
				}
			}
			float Mean(POIType kind) => areas[kind].sum / areas[kind].n;
			Console.WriteLine($"largest: village {Mean(POIType.Village):0} town {Mean(POIType.Town):0} city {Mean(POIType.City):0} capital {Mean(POIType.Capital):0} port {Mean(POIType.Port):0} m2");
			Assert.That(Mean(POIType.Village), Is.LessThan(Mean(POIType.Town)));
			Assert.That(Mean(POIType.Town), Is.LessThan(Mean(POIType.City)));
			Assert.That(Mean(POIType.City), Is.LessThan(Mean(POIType.Capital)));
			// Every one at least (2026-10-20: a village's green had been 180 m2, a town's 240 to 650, a city's 500 to 1200).
			Assert.That(least[POIType.Village], Is.GreaterThanOrEqualTo(250f), "village green");
			Assert.That(least[POIType.Town], Is.GreaterThanOrEqualTo(500f), "town market place");
			Assert.That(least[POIType.City], Is.GreaterThanOrEqualTo(750f), "city square");
			Assert.That(least[POIType.Capital], Is.GreaterThanOrEqualTo(3000f), "the capital's square");
			Assert.That(least[POIType.Port], Is.GreaterThanOrEqualTo(350f), "a port's market");
		}

		/// <summary>
		/// A market's stalls stand in rows: (nearly) all square to one line, back to back or face to face across an aisle. A village's
		/// two or three may stand where its green has room for them.
		/// </summary>
		[Test]
		public void TheStallsStandInRows()
		{
			var problems = new List<string>();
			foreach ((PointOfInterestTemplateSpec spec, int size, int seed, PointOfInterestLayoutRequest _, List<PointOfInterestLayoutPiece> pieces) in Markets())
			{
				List<PointOfInterestLayoutPiece> stalls = pieces.FindAll(p => IsMarket(p) && IsStall(spec, p));
				if (stalls.Count < 4)
				{
					continue;
				}
				int most = 0;
				foreach (PointOfInterestLayoutPiece a in stalls)
				{
					int with = 0;
					foreach (PointOfInterestLayoutPiece b in stalls)
					{
						// Turned the same way or about, give or take the few degrees each is turned.
						with += Mathf.Abs(Mathf.DeltaAngle(a.Yaw, b.Yaw)) < 6f || Mathf.Abs(Mathf.DeltaAngle(a.Yaw, b.Yaw + 180f)) < 6f ? 1 : 0;
					}
					most = Math.Max(most, with);
				}
				if (most < stalls.Count * 0.75f)
				{
					problems.Add($"{spec.Name} size {size} seed {seed}: only {most} of {stalls.Count} stalls in line");
				}
			}
			Assert.That(problems, Is.Empty, string.Join("\n", problems));
		}

		/// <summary>A street village's well stands on its green beside the street, not in the road (it stood on the street's middle line).</summary>
		[Test]
		public void AStreetVillagesWellStandsOnItsGreen_OffTheStreet()
		{
			int checkedSites = 0;
			var four = new Vector2[4];
			foreach ((PointOfInterestTemplateSpec spec, int size, int seed, PointOfInterestLayoutRequest request, List<PointOfInterestLayoutPiece> pieces) in Markets())
			{
				if (request.ChosenPlan != PointOfInterestTownPlan.StreetVillage)
				{
					continue;
				}
				PointOfInterestLayoutPiece well = pieces.Find(p => IsMarket(p) && spec.Pieces[p.Slot].Tag == PointOfInterestTemplateSpecs.Well);
				Assert.That(well.Item, Is.GreaterThanOrEqualTo(0), $"{spec.Name} size {size} seed {seed}: its well");
				PointOfInterestLayouts.Corners(well.Offset, well.Yaw, well.Footprint, four);
				foreach (PointOfInterestLayoutStreet street in request.Streets)
				{
					for (int i = 1; i < street.Points.Length; i++)
					{
						foreach (Vector2 c in four)
						{
							Assert.That(PointToSegment(c, street.Points[i - 1], street.Points[i]), Is.GreaterThan(street.Width * 0.5f),
								$"{spec.Name} size {size} seed {seed}: the well stands in a street");
						}
					}
				}
				checkedSites++;
			}
			Assert.That(checkedSites, Is.GreaterThan(0), "some villages laid along a street");
		}

		/// <summary>Nothing of the market stands on anything else, and the same site lays the same market.</summary>
		[Test]
		public void TheMarketOverlapsNothing_AndRepeats()
		{
			foreach ((PointOfInterestTemplateSpec spec, int size, int seed, PointOfInterestLayoutRequest _, List<PointOfInterestLayoutPiece> pieces) in Markets())
			{
				foreach (PointOfInterestLayoutPiece piece in pieces.FindAll(IsMarket))
				{
					foreach (PointOfInterestLayoutPiece other in pieces)
					{
						if (!Same(piece, other))
						{
							Assert.That(PointOfInterestLayouts.Overlaps(piece.Offset, piece.Yaw, piece.Footprint, other.Offset, other.Yaw, other.Footprint), Is.False,
								$"{spec.Name} size {size} seed {seed}: a {spec.Pieces[piece.Slot].Tag} at {piece.Offset} stands on a {other.Role} {spec.Pieces[other.Slot].Tag}");
						}
					}
				}
				if (seed == 1)
				{
					List<PointOfInterestLayoutPiece> again = Lay(spec, size, seed, out _);
					Assert.That(again.Count, Is.EqualTo(pieces.Count), $"{spec.Name} size {size}: the same site again");
					for (int i = 0; i < pieces.Count; i++)
					{
						Assert.That(again[i].Offset, Is.EqualTo(pieces[i].Offset), $"{spec.Name} size {size}: piece {i} again");
					}
				}
			}
		}
	}
}
