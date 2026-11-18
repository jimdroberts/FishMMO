using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The houses' fenced yards and the edge pieces the town layout lays (2026-10-19): a house with no yard stands exactly where it
	/// did, a yard's fences keep off the streets, the buildings and each other and close round it, its gate stands before the door,
	/// a shop keeps its front on the street, and an edge piece stands in the band inside the boundary with its long side along it.
	/// Pure: no scene.
	/// </summary>
	[TestFixture]
	public class PointOfInterestYardTests
	{
		[OneTimeSetUp]
		public void RegisterKit() => StructureKitPieceSource.Register();

		private static readonly PointOfInterestTownPlan[] Plans =
		{
			PointOfInterestTownPlan.StreetVillage, PointOfInterestTownPlan.GreenVillage, PointOfInterestTownPlan.MarketTown,
			PointOfInterestTownPlan.RadialCity, PointOfInterestTownPlan.Port, PointOfInterestTownPlan.Auto,
		};

		/// <summary>A town on an oval, entered from two roads: cottages that keep front gardens, shops that keep back yards, a hall that keeps none.</summary>
		private static PointOfInterestLayoutRequest Town(PointOfInterestTownPlan plan, int seed, float yardShare, int edges = 0)
		{
			float radius = 45f + (seed % 5) * 12f;
			bool walled = plan == PointOfInterestTownPlan.RadialCity;
			var request = new PointOfInterestLayoutRequest
			{
				Layout = walled ? PointOfInterestLayout.Walled : PointOfInterestLayout.Street,
				Radius = radius,
				Gap = 1f,
				Plan = plan,
				Seed = seed * 7919 + 3,
				Yaw = seed * 11f,
				YardShare = yardShare,
			};
			void Slot(PointOfInterestSlotRole role, Vector2 size, float module, int count, PointOfInterestYardKind yard = PointOfInterestYardKind.None)
			{
				int s = request.Slots.Count;
				request.Slots.Add(new PointOfInterestLayoutSlot { Role = role, Footprint = size, Module = module, Count = count, Yard = yard });
				if (!PointOfInterestLayouts.Generates(role))
				{
					for (int k = 0; k < count; k++)
					{
						request.Items.Add(new PointOfInterestLayoutItem { Slot = s, Footprint = size * (0.85f + 0.05f * ((k + seed) % 4)) });
					}
				}
			}
			if (walled)
			{
				Slot(PointOfInterestSlotRole.Perimeter, new Vector2(6f, 2.2f), 6f, 1);
				Slot(PointOfInterestSlotRole.Gate, new Vector2(12f, 8f), 12f, 2);
				Slot(PointOfInterestSlotRole.WallTower, new Vector2(6f, 6f), 0f, 6);
			}
			if (plan == PointOfInterestTownPlan.Port)
			{
				Slot(PointOfInterestSlotRole.Pier, new Vector2(3f, 6f), 6f, 2);
				request.ShoreAhead = radius * 0.6f;
			}
			Slot(PointOfInterestSlotRole.Centre, walled ? new Vector2(12f, 12f) : new Vector2(3f, 3f), 0f, 1);
			Slot(PointOfInterestSlotRole.Centre, new Vector2(3f, 2f), 0f, 3);
			Slot(PointOfInterestSlotRole.Street, new Vector2(6.5f, 7.5f), 0f, (int)(radius * 0.45f), PointOfInterestYardKind.Fronted);
			Slot(PointOfInterestSlotRole.Street, new Vector2(5f, 6f), 0f, (int)(radius * 0.2f), PointOfInterestYardKind.Behind);
			Slot(PointOfInterestSlotRole.Street, new Vector2(10f, 14f), 0f, 1);
			if (edges > 0)
			{
				Slot(PointOfInterestSlotRole.Edge, new Vector2(16f, 9f), 0f, edges);
				Slot(PointOfInterestSlotRole.Edge, new Vector2(6f, 11f), 0f, 1);
			}
			Slot(PointOfInterestSlotRole.Scatter, new Vector2(1.2f, 1.2f), 0f, 10);
			var outline = new float[PointOfInterestOutline.Bearings];
			for (int k = 0; k < outline.Length; k++)
			{
				float a = k * 2f * Mathf.PI / outline.Length - seed * 17f * Mathf.Deg2Rad;
				float x = Mathf.Sin(a) / (radius * 0.75f), z = Mathf.Cos(a) / radius;
				outline[k] = 1f / Mathf.Sqrt(x * x + z * z);
			}
			request.Outline = outline;
			request.Entrances.AddRange(new[] { 60f + seed * 3f, 230f + seed });
			foreach (float e in request.Entrances)
			{
				request.EntranceDepths.Add(PointOfInterestOutline.ReachToward(outline, radius, e + request.Yaw) * SiteEntrances.EntryDepth);
			}
			return request;
		}

		private static List<PointOfInterestLayoutPiece> Lay(PointOfInterestLayoutRequest request, int seed) => PointOfInterestLayouts.Place(request, new DeterministicRNG(seed + 100));

		private static IEnumerable<(string name, PointOfInterestLayoutRequest request, List<PointOfInterestLayoutPiece> pieces)> Towns(float share, int seeds = 16)
		{
			foreach (PointOfInterestTownPlan plan in Plans)
			{
				for (int seed = 0; seed < seeds; seed++)
				{
					PointOfInterestLayoutRequest request = Town(plan, seed, share);
					yield return ($"{plan}/{seed} ({request.ChosenPlan})", request, Lay(request, seed));
				}
			}
		}

		// ── A house with no yard is not moved ─────────────────────

		[Test]
		public void HousesWithNoYardLayWhereTheyDid()
		{
			int yards = 0, kept = 0;
			foreach (PointOfInterestTownPlan plan in Plans)
			{
				for (int seed = 0; seed < 24; seed++)
				{
					PointOfInterestLayoutRequest without = Town(plan, seed, 0f), with = Town(plan, seed, 1f);
					List<PointOfInterestLayoutPiece> before = Lay(without, seed), after = Lay(with, seed);
					Assert.That(without.Yards, Is.Empty, "no share, no yards");
					var yardOf = new Dictionary<int, PointOfInterestLayoutYard>();
					foreach (PointOfInterestLayoutYard yard in with.Yards)
					{
						yardOf[yard.Item] = yard;
					}
					var old = new Dictionary<int, PointOfInterestLayoutPiece>();
					foreach (PointOfInterestLayoutPiece p in before)
					{
						if (p.Role == PointOfInterestSlotRole.Street)
						{
							old[p.Item] = p;
						}
					}
					foreach (PointOfInterestLayoutPiece p in after)
					{
						if (p.Role != PointOfInterestSlotRole.Street)
						{
							continue;
						}
						Assert.That(old.TryGetValue(p.Item, out PointOfInterestLayoutPiece was), Is.True, $"{plan}/{seed}: item {p.Item} laid both times");
						Assert.That(p.Yaw, Is.EqualTo(was.Yaw), $"{plan}/{seed}: item {p.Item} keeps its yaw");
						if (!yardOf.TryGetValue(p.Item, out PointOfInterestLayoutYard yard))
						{
							Assert.That(p.Offset, Is.EqualTo(was.Offset), $"{plan}/{seed}: the house {p.Item} with no yard stands where it did");
							kept++;
							continue;
						}
						// A yard's house only steps straight back from its street, by its set-back.
						Vector2 step = PointOfInterestLayouts.Rotate(p.Offset - was.Offset, -p.Yaw);
						Assert.That(Mathf.Abs(step.x), Is.LessThan(1e-3f), $"{plan}/{seed}: the house {p.Item} steps straight back");
						Assert.That(step.y, Is.EqualTo(yard.SetBack).Within(1e-3f), $"{plan}/{seed}: the house {p.Item} steps back by its set-back");
						yards++;
					}
					Assert.That(after.FindAll(p => p.Role == PointOfInterestSlotRole.Street).Count, Is.EqualTo(old.Count), $"{plan}/{seed}: as many houses laid");
				}
			}
			Assert.That(yards, Is.GreaterThan(50), "yards were laid");
			Assert.That(kept, Is.GreaterThan(50), "houses kept their places");
		}

		[Test]
		public void YardsFollowTheirSeed()
		{
			PointOfInterestLayoutRequest a = Town(PointOfInterestTownPlan.StreetVillage, 3, 1f), b = Town(PointOfInterestTownPlan.StreetVillage, 3, 1f);
			Lay(a, 3);
			Lay(b, 3);
			Assert.That(a.Yards.Count, Is.EqualTo(b.Yards.Count));
			Assert.That(a.Yards, Is.Not.Empty);
			for (int i = 0; i < a.Yards.Count; i++)
			{
				Assert.That(b.Yards[i].Centre, Is.EqualTo(a.Yards[i].Centre));
				Assert.That(b.Yards[i].Fences.Count, Is.EqualTo(a.Yards[i].Fences.Count));
			}
		}

		[Test]
		public void TheTownPlansShareSetsHowManyYards()
		{
			int village = 0, villageHouses = 0, city = 0, cityHouses = 0;
			for (int seed = 0; seed < 16; seed++)
			{
				PointOfInterestLayoutRequest v = Town(PointOfInterestTownPlan.StreetVillage, seed, float.NaN);
				List<PointOfInterestLayoutPiece> laid = Lay(v, seed);
				village += v.Yards.Count;
				villageHouses += laid.FindAll(p => p.Role == PointOfInterestSlotRole.Street).Count;
				PointOfInterestLayoutRequest c = Town(PointOfInterestTownPlan.RadialCity, seed, float.NaN);
				cityHouses += Lay(c, seed).FindAll(p => p.Role == PointOfInterestSlotRole.Street).Count;
				city += c.Yards.Count;
			}
			Assert.That(village, Is.GreaterThan(villageHouses / 5), "a village fences a good share of its crofts");
			Assert.That(city * 2, Is.LessThan(village), "a city fences few");
		}

		// ── The fences ────────────────────────────────────────────

		[Test]
		public void YardFencesKeepOffStreetsBuildingsAndEachOther()
		{
			int runs = 0;
			foreach ((string name, PointOfInterestLayoutRequest request, List<PointOfInterestLayoutPiece> pieces) in Towns(1f))
			{
				for (int y = 0; y < request.Yards.Count; y++)
				{
					foreach (PointOfInterestYardFence run in request.Yards[y].Fences)
					{
						runs++;
						foreach (Vector2 p in Core(run, 0.25f, 0.25f))
						{
							Assert.That(p.magnitude, Is.LessThanOrEqualTo(Boundary(request, p) + 0.01f), $"{name}: a fence at {p} stays inside");
							foreach (PointOfInterestLayoutStreet street in request.Streets)
							{
								Assert.That(DistanceTo(street.Points, p), Is.GreaterThanOrEqualTo(street.Width * 0.5f - 0.02f), $"{name}: the fence at {p} keeps off a street");
							}
							foreach (PointOfInterestLayoutPiece piece in pieces)
							{
								Assert.That(InRect(p, piece.Offset, piece.Yaw, piece.Footprint, 0.02f), Is.False, $"{name}: the fence at {p} crosses a {piece.Role} at {piece.Offset}");
							}
							foreach (Vector2[] plaza in request.Plazas)
							{
								Assert.That(InConvex(plaza, p), Is.False, $"{name}: the fence at {p} stands in a plaza");
							}
						}
						// Never across another yard's fence.
						for (int o = 0; o < request.Yards.Count; o++)
						{
							if (o == y)
							{
								continue;
							}
							foreach (PointOfInterestYardFence other in request.Yards[o].Fences)
							{
								Assert.That(Cross(Shrunk(run, 0.3f), Shrunk(other, 0.3f)), Is.False, $"{name}: yard {y}'s fence {run.A}→{run.B} crosses yard {o}'s {other.A}→{other.B}");
							}
						}
					}
				}
			}
			Assert.That(runs, Is.GreaterThan(200));
		}

		[Test]
		public void AYardIsFencedAllRound()
		{
			foreach ((string name, PointOfInterestLayoutRequest request, List<PointOfInterestLayoutPiece> _) in Towns(1f))
			{
				var all = new List<PointOfInterestYardFence>();
				foreach (PointOfInterestLayoutYard yard in request.Yards)
				{
					all.AddRange(yard.Fences);
				}
				foreach (PointOfInterestLayoutYard yard in request.Yards)
				{
					foreach (PointOfInterestYardFence run in yard.Fences)
					{
						foreach (Vector2 end in new[] { run.A, run.B })
						{
							float nearest = float.PositiveInfinity;
							foreach (PointOfInterestYardFence other in all)
							{
								if (other.A == run.A && other.B == run.B)
								{
									continue;
								}
								nearest = Mathf.Min(nearest, DistanceTo(new[] { other.A, other.B }, end));
							}
							// Or its own house's wall (behind a shop the sides start beside it).
							Vector2 local = PointOfInterestLayouts.Rotate(end - yard.Centre, -yard.Yaw);
							float dx = Mathf.Max(0f, Mathf.Abs(local.x) - yard.Footprint.x * 0.5f), dz = Mathf.Max(0f, Mathf.Abs(local.y) - yard.Footprint.y * 0.5f);
							nearest = Mathf.Min(nearest, Mathf.Sqrt(dx * dx + dz * dz) - 0.3f);
							Assert.That(nearest, Is.LessThan(0.35f), $"{name}: the yard of item {yard.Item}'s fence end at {end} meets another run");
						}
					}
				}
			}
		}

		[Test]
		public void AFrontedYardsGateStandsBeforeItsDoor()
		{
			int checkedYards = 0;
			foreach ((string name, PointOfInterestLayoutRequest request, List<PointOfInterestLayoutPiece> pieces) in Towns(1f))
			{
				foreach (PointOfInterestLayoutYard yard in request.Yards)
				{
					if (yard.Kind != PointOfInterestYardKind.Fronted)
					{
						continue;
					}
					List<PointOfInterestYardFence> gates = yard.Fences.FindAll(f => f.Gate);
					Assert.That(gates.Count, Is.EqualTo(1), $"{name}: one gate run");
					PointOfInterestYardFence front = gates[0];
					Assert.That(DistanceTo(new[] { front.A, front.B }, yard.GateAt), Is.LessThan(1e-3f), $"{name}: the gate is on its run");
					Assert.That(pieces[yard.Piece].Offset, Is.EqualTo(yard.Centre), $"{name}: the yard's house is its piece");
					// Its door in the middle of its front wall (−z): the gate square out from it, a garden's depth away.
					Vector2 door = yard.Centre + PointOfInterestLayouts.Rotate(new Vector2(0f, -yard.Footprint.y * 0.5f), yard.Yaw);
					Vector2 local = PointOfInterestLayouts.Rotate(yard.GateAt - door, -yard.Yaw);
					Assert.That(Mathf.Abs(local.x), Is.LessThan(0.05f), $"{name}: the gate is on the line out from the door");
					Assert.That(-local.y, Is.GreaterThanOrEqualTo(PointOfInterestLayouts.LeastGardenMetres - 0.01f), $"{name}: a garden before the house");
					Assert.That(yard.SetBack, Is.GreaterThan(0f));
					checkedYards++;
				}
			}
			Assert.That(checkedYards, Is.GreaterThan(50));
		}

		[Test]
		public void AShopKeepsItsFrontOnTheStreet()
		{
			int shops = 0;
			foreach ((string name, PointOfInterestLayoutRequest request, List<PointOfInterestLayoutPiece> _) in Towns(1f, 24))
			{
				foreach (PointOfInterestLayoutYard yard in request.Yards)
				{
					if (yard.Kind != PointOfInterestYardKind.Behind)
					{
						continue;
					}
					shops++;
					Assert.That(yard.SetBack, Is.EqualTo(0f), $"{name}: a shop is not set back");
					Assert.That(yard.Fences.FindAll(f => f.Gate).Count, Is.EqualTo(1), $"{name}: one gate, at the back");
					foreach (PointOfInterestYardFence run in yard.Fences)
					{
						foreach (Vector2 p in new[] { run.A, run.B })
						{
							Vector2 local = PointOfInterestLayouts.Rotate(p - yard.Centre, -yard.Yaw);
							Assert.That(local.y, Is.GreaterThan(yard.Footprint.y * 0.5f - 0.7f), $"{name}: nothing fenced before the shop's front");
						}
					}
				}
			}
			Assert.That(shops, Is.GreaterThan(5));
		}

		[Test]
		public void YardsKeepTheTownPacked()
		{
			foreach ((string name, PointOfInterestLayoutRequest request, List<PointOfInterestLayoutPiece> pieces) in Towns(1f, 10))
			{
				AssertPacked(name, request, pieces);
			}
		}

		/// <summary>Every template laid as PropsFeature lays it, its houses' yards drawn by their tags at every share: still packed, every yard's house still on its street's side.</summary>
		[Test]
		public void EveryTemplateWithYardsStillPacks()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			int yards = 0;
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All())
			{
				PointOfInterestKindRule rule = rules.RuleFor(spec.Kind);
				foreach (int size in spec.Sizes())
				{
					float min = rule != null ? rule.FootprintMin : 10f, max = rule != null ? rule.FootprintMax : 10f;
					float radius = Mathf.Lerp(min, max, size / 2f);
					var random = new DeterministicRNG(1000 + size);
					var request = new PointOfInterestLayoutRequest { Layout = spec.Layout, Radius = radius, Gap = 1f, SpanHalf = radius, ShoreAhead = radius * PointOfInterestLayouts.Inset, YardShare = 1f, Seed = 77 + size };
					for (int s = 0; s < spec.Pieces.Count; s++)
					{
						PointOfInterestPieceSlot slot = spec.Pieces[s];
						string style = string.IsNullOrEmpty(slot.Style) ? spec.Style : slot.Style;
						PointOfInterestSlotRole role = PointOfInterestLayouts.RoleFor(spec.Layout, s, slot.Role);
						int count = PropsFeature.CountFor(slot, size, true, random);
						PointOfInterestPieceMetrics metrics = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, 0));
						request.Slots.Add(new PointOfInterestLayoutSlot
						{
							Role = role,
							Footprint = metrics.Footprint,
							Module = metrics.Module,
							Count = count,
							Yard = role == PointOfInterestSlotRole.Street ? PointOfInterestYards.KindFor(slot.Tag) : PointOfInterestYardKind.None,
						});
						if (PointOfInterestLayouts.Generates(role))
						{
							continue;
						}
						for (int k = 0; k < count; k++)
						{
							PointOfInterestPieceMetrics piece = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, random.Next(), 0f));
							request.Items.Add(new PointOfInterestLayoutItem { Slot = s, Footprint = piece.Footprint });
						}
					}
					List<PointOfInterestLayoutPiece> pieces = PointOfInterestLayouts.Place(request, random);
					AssertPacked($"{spec.Name} at size {size}", request, pieces);
					yards += request.Yards.Count;
				}
			}
			Assert.That(yards, Is.GreaterThan(20), "the templates' houses keep yards");
		}

		[Test]
		public void TheHouseTagsSayWhichMayKeepAYard()
		{
			Assert.That(PointOfInterestYards.KindFor(PointOfInterestTemplateSpecs.House), Is.EqualTo(PointOfInterestYardKind.Fronted));
			Assert.That(PointOfInterestYards.KindFor(PointOfInterestTemplateSpecs.Cottage), Is.EqualTo(PointOfInterestYardKind.Fronted));
			Assert.That(PointOfInterestYards.KindFor(PointOfInterestTemplateSpecs.Longhouse), Is.EqualTo(PointOfInterestYardKind.Fronted));
			Assert.That(PointOfInterestYards.KindFor(PointOfInterestTemplateSpecs.StoneHouse), Is.EqualTo(PointOfInterestYardKind.Fronted));
			Assert.That(PointOfInterestYards.KindFor("shop"), Is.EqualTo(PointOfInterestYardKind.Behind));
			foreach (string none in new[] { PointOfInterestTemplateSpecs.Stilt, PointOfInterestTemplateSpecs.RuinedHouse, PointOfInterestTemplateSpecs.Church,
				PointOfInterestTemplateSpecs.Temple, PointOfInterestTemplateSpecs.Tavern, PointOfInterestTemplateSpecs.Smithy, PointOfInterestTemplateSpecs.Stable, "", null })
			{
				Assert.That(PointOfInterestYards.KindFor(none), Is.EqualTo(PointOfInterestYardKind.None), $"'{none}' keeps no yard");
			}
		}

		[Test]
		public void AGateCentresOnItsDoorAndAFenceStopsBeforeADoor()
		{
			Assert.That(PointOfInterestYards.GateSpan(Vector2.zero, new Vector2(10f, 0f), new Vector2(4f, 2f), 3f, 0.4f, out Vector2 g0, out Vector2 g1), Is.True);
			Assert.That(g0.x, Is.EqualTo(2.5f).Within(1e-4f));
			Assert.That(g1.x, Is.EqualTo(5.5f).Within(1e-4f));
			PointOfInterestYards.GateSpan(Vector2.zero, new Vector2(10f, 0f), new Vector2(-3f, 0f), 3f, 0.4f, out g0, out _);
			Assert.That(g0.x, Is.EqualTo(0.4f).Within(1e-4f), "held a margin from the run's end");
			Assert.That(PointOfInterestYards.GateSpan(Vector2.zero, new Vector2(3.2f, 0f), Vector2.zero, 3f, 0.4f, out _, out _), Is.False, "too short a run");
			List<(Vector2 A, Vector2 B)> kept = PointOfInterestYards.ClearOfDoors(Vector2.zero, new Vector2(10f, 0f), new[] { new Vector2(5f, 1f), new Vector2(5f, 9f) }, 1.4f, 1.1f);
			Assert.That(kept.Count, Is.EqualTo(2), "one gap, for the near door only");
			Assert.That(kept[0].B.x, Is.EqualTo(3.9f).Within(1e-4f));
			Assert.That(kept[1].A.x, Is.EqualTo(6.1f).Within(1e-4f));
		}

		// ── The edge ──────────────────────────────────────────────

		[Test]
		public void EdgePiecesStandInTheBandAlongTheBoundary()
		{
			int pieces = 0, inBand = 0, along = 0, asked = 0, found = 0;
			foreach (PointOfInterestTownPlan plan in new[] { PointOfInterestTownPlan.StreetVillage, PointOfInterestTownPlan.GreenVillage, PointOfInterestTownPlan.MarketTown, PointOfInterestTownPlan.RadialCity })
			{
				for (int seed = 0; seed < 12; seed++)
				{
					// The smallest sites (45 m, a city's under 70 m) are packed full by their houses: a piece with no room is dropped there, as any is.
					if (seed % 5 < (plan == PointOfInterestTownPlan.RadialCity ? 2 : 1))
					{
						continue;
					}
					PointOfInterestLayoutRequest request = Town(plan, seed, float.NaN, 3);
					List<PointOfInterestLayoutPiece> laid = Lay(request, seed);
					string name = $"{plan}/{seed} ({request.ChosenPlan})";
					AssertPacked(name, request, laid);
					foreach (PointOfInterestLayoutPiece p in laid)
					{
						if (p.Role != PointOfInterestSlotRole.Edge)
						{
							continue;
						}
						if (plan == PointOfInterestTownPlan.RadialCity)
						{
							// Inside a wall the band is measured from the wall's inner face, which this test does not know: laid and packed is enough.
							continue;
						}
						pieces++;
						float share = p.Offset.magnitude / Mathf.Max(1f, Boundary(request, p.Offset));
						if (share >= PointOfInterestLayouts.EdgeBandShare - 0.05f)
						{
							inBand++;
						}
						Vector2 longAxis = PointOfInterestLayouts.Rotate(p.Footprint.x >= p.Footprint.y ? Vector2.right : Vector2.up, p.Yaw);
						if (Mathf.Abs(Vector2.Dot(longAxis, p.Offset.normalized)) < 0.4f)
						{
							along++;
						}
					}
					asked += request.Items.FindAll(i => request.Slots[i.Slot].Role == PointOfInterestSlotRole.Edge).Count;
					found += laid.FindAll(p => p.Role == PointOfInterestSlotRole.Edge).Count;
				}
			}
			Assert.That(found, Is.GreaterThanOrEqualTo((int)(asked * 0.85f)), $"{found} of {asked} edge pieces found room");
			Assert.That(pieces, Is.GreaterThan(70));
			Assert.That(inBand, Is.GreaterThanOrEqualTo((int)(pieces * 0.9f)), $"{inBand} of {pieces} edge pieces in the band");
			Assert.That(along, Is.GreaterThanOrEqualTo((int)(pieces * 0.75f)), $"{along} of {pieces} edge pieces long side along the boundary");
		}

		[Test]
		public void TheEdgeRoleIsAppendedAndNeverAnAutoDefault()
		{
			Assert.That((int)PointOfInterestSlotRole.Edge, Is.EqualTo(12), "appended after Pier");
			foreach (PointOfInterestLayout layout in (PointOfInterestLayout[])System.Enum.GetValues(typeof(PointOfInterestLayout)))
			{
				for (int s = 0; s < 4; s++)
				{
					Assert.That(PointOfInterestLayouts.RoleFor(layout, s, PointOfInterestSlotRole.Auto), Is.Not.EqualTo(PointOfInterestSlotRole.Edge));
				}
			}
			Assert.That(PointOfInterestLayouts.Generates(PointOfInterestSlotRole.Edge), Is.False, "counted, not made up");
		}

		// ── Geometry ──────────────────────────────────────────────

		private static IEnumerable<Vector2> Core(PointOfInterestYardFence run, float trim, float step)
		{
			Vector2 d = run.B - run.A;
			float length = d.magnitude;
			if (length <= 2f * trim)
			{
				yield break;
			}
			d /= length;
			for (float t = trim; t <= length - trim + 1e-4f; t += step)
			{
				yield return run.A + d * t;
			}
		}

		private static (Vector2 A, Vector2 B) Shrunk(PointOfInterestYardFence run, float by)
		{
			Vector2 d = run.B - run.A;
			float length = d.magnitude;
			if (length <= 2f * by)
			{
				Vector2 m = 0.5f * (run.A + run.B);
				return (m, m);
			}
			d /= length;
			return (run.A + d * by, run.B - d * by);
		}

		private static bool Cross((Vector2 A, Vector2 B) p, (Vector2 A, Vector2 B) q)
		{
			float Side(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
			if ((p.B - p.A).sqrMagnitude < 1e-8f || (q.B - q.A).sqrMagnitude < 1e-8f)
			{
				return false;
			}
			float d1 = Side(q.A, q.B, p.A), d2 = Side(q.A, q.B, p.B), d3 = Side(p.A, p.B, q.A), d4 = Side(p.A, p.B, q.B);
			if (d1 * d2 < 0f && d3 * d4 < 0f)
			{
				return true;
			}
			// Collinear and overlapping: one fence laid along another.
			if (Mathf.Abs(d1) < 1e-3f && Mathf.Abs(d2) < 1e-3f)
			{
				Vector2 u = (p.B - p.A).normalized;
				float a0 = 0f, a1 = Vector2.Dot(p.B - p.A, u);
				float b0 = Vector2.Dot(q.A - p.A, u), b1 = Vector2.Dot(q.B - p.A, u);
				return Mathf.Min(a1, Mathf.Max(b0, b1)) - Mathf.Max(a0, Mathf.Min(b0, b1)) > 0.05f;
			}
			return false;
		}

		private static float DistanceTo(IReadOnlyList<Vector2> line, Vector2 p)
		{
			float best = float.PositiveInfinity;
			for (int i = 1; i < line.Count; i++)
			{
				Vector2 a = line[i - 1], ab = line[i] - a;
				float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
				best = Mathf.Min(best, Vector2.Distance(a + ab * t, p));
			}
			return best;
		}

		private static bool InRect(Vector2 p, Vector2 centre, float yaw, Vector2 size, float shrink)
		{
			Vector2 local = PointOfInterestLayouts.Rotate(p - centre, -yaw);
			return Mathf.Abs(local.x) < size.x * 0.5f - shrink && Mathf.Abs(local.y) < size.y * 0.5f - shrink;
		}

		private static bool InConvex(Vector2[] polygon, Vector2 p)
		{
			int sign = 0;
			for (int i = 0; i < polygon.Length; i++)
			{
				Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
				float c = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
				int s = c > 1e-3f ? 1 : c < -1e-3f ? -1 : 0;
				if (s == 0)
				{
					return false;
				}
				if (sign == 0)
				{
					sign = s;
				}
				else if (s != sign)
				{
					return false;
				}
			}
			return true;
		}

		private static float Boundary(PointOfInterestLayoutRequest request, Vector2 toward)
			=> PointOfInterestLayouts.Inset * PointOfInterestOutline.ReachToward(request.Outline, request.Radius, PointOfInterestLayouts.DirectionOf(toward) + request.Yaw);

		/// <summary>No two pieces overlap (a wall ring's modules, a bridge's spans and a pier's modules apart), all inside the boundary.</summary>
		private static void AssertPacked(string name, PointOfInterestLayoutRequest request, List<PointOfInterestLayoutPiece> pieces)
		{
			var four = new Vector2[4];
			for (int i = 0; i < pieces.Count; i++)
			{
				PointOfInterestLayoutPiece a = pieces[i];
				if (!PointOfInterestLayouts.Reaches(a.Role) && !(a.Role == PointOfInterestSlotRole.Centre && a.Offset == Vector2.zero))
				{
					PointOfInterestLayouts.Corners(a.Offset, a.Yaw, a.Footprint, four);
					foreach (Vector2 c in four)
					{
						Assert.That(c.magnitude, Is.LessThanOrEqualTo(Boundary(request, c) + 0.01f), $"{name}: a {a.Role} piece at {a.Offset} stays inside");
					}
				}
				for (int j = i + 1; j < pieces.Count; j++)
				{
					PointOfInterestLayoutPiece b = pieces[j];
					if (Ring(a) && Ring(b) || a.Role == b.Role && (a.Role == PointOfInterestSlotRole.Span || a.Role == PointOfInterestSlotRole.Pier))
					{
						continue;
					}
					Assert.That(Overlap(a, b), Is.LessThan(0.05f), $"{name}: {a.Role} at {a.Offset} and {b.Role} at {b.Offset} overlap");
				}
			}
		}

		private static bool Ring(PointOfInterestLayoutPiece p) => p.Role == PointOfInterestSlotRole.Perimeter || p.Role == PointOfInterestSlotRole.Gate || p.Role == PointOfInterestSlotRole.WallTower;

		private static float Overlap(PointOfInterestLayoutPiece a, PointOfInterestLayoutPiece b)
		{
			var ca = new Vector2[4];
			var cb = new Vector2[4];
			PointOfInterestLayouts.Corners(a.Offset, a.Yaw, a.Footprint, ca);
			PointOfInterestLayouts.Corners(b.Offset, b.Yaw, b.Footprint, cb);
			float least = float.PositiveInfinity;
			foreach (Vector2[] poly in new[] { ca, cb })
			{
				for (int e = 0; e < 4; e++)
				{
					Vector2 edge = poly[(e + 1) % 4] - poly[e];
					if (edge.sqrMagnitude < 1e-8f)
					{
						continue;
					}
					Vector2 axis = new Vector2(-edge.y, edge.x).normalized;
					float minA = float.PositiveInfinity, maxA = float.NegativeInfinity, minB = float.PositiveInfinity, maxB = float.NegativeInfinity;
					foreach (Vector2 p in ca) { float d = Vector2.Dot(p, axis); minA = Mathf.Min(minA, d); maxA = Mathf.Max(maxA, d); }
					foreach (Vector2 p in cb) { float d = Vector2.Dot(p, axis); minB = Mathf.Min(minB, d); maxB = Mathf.Max(maxB, d); }
					least = Mathf.Min(least, Mathf.Min(maxA, maxB) - Mathf.Max(minA, minB));
				}
			}
			return float.IsPositiveInfinity(least) ? 0f : least;
		}
	}
}
