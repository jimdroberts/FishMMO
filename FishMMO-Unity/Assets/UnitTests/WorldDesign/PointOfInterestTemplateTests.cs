using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The point-of-interest templates and how their pieces are laid: a template for every kind the structure kit builds,
	/// every slot resolving in the kit, race filters that never strand a site, and layouts that pack pieces without
	/// overlap inside their footprints, close their wall rings, face sensibly and repeat for the same seed. Pure: no
	/// assets, no scene.
	/// </summary>
	[TestFixture]
	public class PointOfInterestTemplateTests
	{
		[OneTimeSetUp]
		public void RegisterKit() => StructureKitPieceSource.Register();

		// ── The catalogue ─────────────────────────────────────────

		[Test]
		public void EveryStructureKindHasATemplate()
		{
			List<PointOfInterestTemplateSpec> specs = PointOfInterestTemplateSpecs.All();
			var missing = new List<string>();
			int kinds = 0;
			foreach (PointOfInterestKindInfo info in PointOfInterestKinds.All)
			{
				if (!info.Has(PointOfInterestTraits.Structure))
				{
					continue;
				}
				kinds++;
				if (!specs.Exists(s => s.Kind == info.Kind))
				{
					missing.Add(info.Kind.ToString());
				}
			}
			Assert.That(missing, Is.Empty, "structure kinds with no template");
			Assert.That(kinds, Is.GreaterThan(50), "the catalogue's structure kinds were all seen");
			foreach (POIType cave in new[] { POIType.Cave, POIType.IceCave, POIType.LavaTube, POIType.Grotto, POIType.SeaCave })
			{
				Assert.That(specs.Exists(s => s.Kind == cave), Is.True, $"{cave} has a chamber template");
				Assert.That(specs.TrueForAll(s => s.Kind != cave || s.InChamber), Is.True, $"{cave} builds in its chamber");
				foreach (int size in new[] { 0, 1, 2 })
				{
					Assert.That(specs.Exists(s => s.Kind == cave && System.Linq.Enumerable.Contains(s.Sizes(), size) && s.RaceCategories.Count == 0), Is.True,
						$"{cave} has a template any site of size {size} fits");
				}
			}
			foreach (PointOfInterestTemplateSpec spec in specs)
			{
				bool cave = CaveShaper.TryForm(spec.Kind, out _);
				Assert.That(cave || PointOfInterestKinds.Info(spec.Kind).Has(PointOfInterestTraits.Structure), Is.True, $"{spec.Name} is for a kind the kit or the cave shaper builds");
				Assert.That(spec.InChamber, Is.EqualTo(cave), $"{spec.Name}: only caves build in a chamber");
				Assert.That(spec.Pieces, Is.Not.Empty, $"{spec.Name} lays something");
				Assert.That(spec.Sizes(), Is.Not.Empty, $"{spec.Name} takes some size");
			}
		}

		[Test]
		public void EverySlotResolvesInTheKit()
		{
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All())
			{
				foreach (PointOfInterestPieceSlot slot in spec.Pieces)
				{
					string style = string.IsNullOrEmpty(slot.Style) ? spec.Style : slot.Style;
					Assert.That(StructureKitPieceSource.Candidates(slot.Tag, style), Is.Not.Empty, $"{spec.Name}: '{slot.Tag}' resolves in the kit");
					Assert.That(slot.MaxCount, Is.GreaterThanOrEqualTo(slot.MinCount), $"{spec.Name}: '{slot.Tag}' count range");
				}
			}
		}

		[Test]
		public void RaceFiltersNeverStrandASite()
		{
			List<PointOfInterestTemplateSpec> specs = PointOfInterestTemplateSpecs.All();
			foreach (PointOfInterestTemplateSpec spec in specs)
			{
				if (!PointOfInterestKinds.Info(spec.Kind).Has(PointOfInterestTraits.UsesRace))
				{
					Assert.That(spec.RaceCategories, Is.Empty, $"{spec.Name}: its kind takes no race, so a race filter would fit no site");
				}
			}
			foreach (PointOfInterestKindInfo info in PointOfInterestKinds.All)
			{
				if (info.Has(PointOfInterestTraits.Structure) && info.Has(PointOfInterestTraits.UsesRace))
				{
					Assert.That(specs.Exists(s => s.Kind == info.Kind && s.RaceCategories.Count == 0), Is.True,
						$"{info.Kind} keeps a variant any race fits");
				}
			}
		}

		[Test]
		public void NamesAreUniqueAndFiledByGroup()
		{
			var names = new HashSet<string>();
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All())
			{
				Assert.That(names.Add(spec.Name), Is.True, $"{spec.Name} is named once");
				Assert.That(spec.Name, Does.StartWith($"{spec.Kind} - "));
				Assert.That(spec.GroupFolder, Is.EqualTo($"Assets/Templates/World/PointsOfInterest/{PointOfInterestKinds.Info(spec.Kind).Group}"));
			}
		}

		[Test]
		public void TheCapitalOutbuildsTheCity()
		{
			int Houses(POIType kind)
			{
				int most = 0;
				foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.For(kind))
				{
					int houses = 0;
					foreach (PointOfInterestPieceSlot slot in spec.Pieces)
					{
						if (slot.Role == PointOfInterestSlotRole.Street)
						{
							houses += slot.MaxCount;
						}
					}
					most = Math.Max(most, houses);
				}
				return most;
			}
			Assert.That(Houses(POIType.Capital), Is.GreaterThan(Houses(POIType.City)));
			PointOfInterestRules rules = PointOfInterestRules.Default();
			Assert.That(rules.RuleFor(POIType.Capital).FootprintMin, Is.GreaterThan(rules.RuleFor(POIType.City).FootprintMax), "a capital's footprint is larger than any city's");
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.For(POIType.Capital))
			{
				Assert.That(spec.Layout, Is.EqualTo(PointOfInterestLayout.Walled), spec.Name);
				Assert.That(spec.Pieces.Exists(p => p.Role == PointOfInterestSlotRole.Street), Is.True, $"{spec.Name} has streets");
				Assert.That(spec.Pieces.Exists(p => p.Role == PointOfInterestSlotRole.Centre && p.Tag == PointOfInterestTemplateSpecs.Keep), Is.True, $"{spec.Name} has its keep at the centre");
			}
		}

		[Test]
		public void SunkenKindsAreWeatheredInAlgae()
		{
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All())
			{
				if (PointOfInterestKinds.Info(spec.Kind).Has(PointOfInterestTraits.Underwater))
				{
					Assert.That(spec.Finish == PointOfInterestFinish.Algae || spec.Finish == PointOfInterestFinish.Auto, Is.True, spec.Name);
					Assert.That(PropsFeature.FinishFor(spec.Finish, spec.FinishChance, spec.Kind, "Seabed", 0f, 0.99f), Is.EqualTo(StructureFinish.Algae), spec.Name);
				}
			}
		}

		[Test]
		public void SizesSplitOnlyWhereTheFeaturesDiffer()
		{
			var spec = new PointOfInterestTemplateSpec { Kind = POIType.Camp, Variant = "Test" }.S(PointOfInterestTemplateSpecs.Tent, 1, 2);
			var specs = new List<PointOfInterestTemplateSpec> { spec };
			List<PointOfInterestTemplateAuthoring.Planned> same = PointOfInterestTemplateAuthoring.Plan(specs, (s, size) => "same");
			Assert.That(same.Count, Is.EqualTo(1));
			Assert.That(same[0].Name, Is.EqualTo("Camp - Test"));
			Assert.That(same[0].Path, Is.EqualTo("Assets/Templates/World/PointsOfInterest/Wild/Camp - Test.asset"));

			List<PointOfInterestTemplateAuthoring.Planned> split = PointOfInterestTemplateAuthoring.Plan(specs, (s, size) => size == 0 ? "small" : "bigger");
			Assert.That(split.Count, Is.EqualTo(2));
			Assert.That(split[0].Name, Is.EqualTo("Camp - Test (Small)"));
			Assert.That(split[0].Small && !split[0].Medium && !split[0].Large, Is.True);
			Assert.That(split[1].Name, Is.EqualTo("Camp - Test (Medium-Large)"));
			Assert.That(!split[1].Small && split[1].Medium && split[1].Large, Is.True);
			Assert.That(split[1].FeatureSize, Is.EqualTo(1));
		}

		// ── The kit as a piece source ─────────────────────────────

		[Test]
		public void QueriesNarrowTheKitAsDocumented()
		{
			StructureKitPieceSource.Parse("tower+military!wall!keep", out List<string> required, out List<string> excluded);
			Assert.That(required, Is.EqualTo(new[] { "tower", "military" }));
			Assert.That(excluded, Is.EqualTo(new[] { "wall", "keep" }));
			StructureKitPieceSource.Parse("standing-stone", out required, out excluded);
			Assert.That(required, Is.EqualTo(new[] { "standing-stone" }), "a hyphen is part of a tag");

			foreach (StructurePiece house in StructureKitPieceSource.Candidates(PointOfInterestTemplateSpecs.House, "timber"))
			{
				Assert.That(house.Style, Is.EqualTo(StructureStyle.Timber), house.Id);
				Assert.That(house.HasTag("ruin") || house.HasTag("stilt"), Is.False, $"{house.Id}: a plain house is whole and on the ground");
			}
			foreach (StructurePiece house in StructureKitPieceSource.Candidates(PointOfInterestTemplateSpecs.Stilt, null))
			{
				Assert.That(house.HasTag("stilt"), Is.True, house.Id);
			}
			Assert.That(Ids(PointOfInterestTemplateSpecs.Wall, "stone"), Is.EqualTo(new[] { "WallSegment" }));
			Assert.That(Ids(PointOfInterestTemplateSpecs.Wall, "timber"), Is.EqualTo(new[] { "PalisadeSegment" }));
			Assert.That(Ids(PointOfInterestTemplateSpecs.Gate, "stone"), Is.EqualTo(new[] { "Gatehouse" }));
			Assert.That(Ids(PointOfInterestTemplateSpecs.Watchtower, "timber"), Is.EqualTo(new[] { "Watchtower" }));
			Assert.That(Ids(PointOfInterestTemplateSpecs.WallTower, "stone"), Is.EqualTo(new[] { "WallTower" }));
			Assert.That(Ids(PointOfInterestTemplateSpecs.Longhouse, "stone"), Is.EqualTo(new[] { "Longhouse" }), "an exact piece ignores the style");
			Assert.That(Ids(PointOfInterestTemplateSpecs.Rubble, null), Is.Not.Empty, "a query of ruin-only pieces still answers");
			Assert.That(Ids(PointOfInterestTemplateSpecs.Stall, "stone"), Is.EqualTo(new[] { "MarketStall" }), "a style no piece has falls back to any");
			Assert.That(StructureKitPieceSource.ParseStyle("Stone"), Is.EqualTo(StructureStyle.Stone));
			Assert.That(StructureKitPieceSource.ParseStyle("orc"), Is.Null, "a race key is any style");
		}

		[Test]
		public void DecayPastTheThresholdLaysTheRuin()
		{
			for (int seed = 0; seed < 8; seed++)
			{
				Assert.That(StructureKitPieceSource.PieceFor(PointOfInterestTemplateSpecs.Wall, "stone", seed, 0.2f).Id, Is.EqualTo("WallSegment"));
				Assert.That(StructureKitPieceSource.PieceFor(PointOfInterestTemplateSpecs.Wall, "stone", seed, 0.8f).Id, Is.EqualTo("RuinedWall"));
				Assert.That(StructureKitPieceSource.PieceFor(PointOfInterestTemplateSpecs.Keep, "stone", seed, 0.8f).Id, Is.EqualTo("RuinedKeep"));
			}
			StructurePiece wall = StructureKitPieceSource.PieceFor(PointOfInterestTemplateSpecs.Wall, "stone", 1, 0.8f);
			Assert.That(wall.ModuleLength, Is.EqualTo(StructureKit.Get("WallSegment").ModuleLength), "a ruined wall keeps the ring's module");
		}

		[Test]
		public void TheKitRegistersBelowEveryOtherSource()
		{
			StructureKitPieceSource.Register();
			StructureKitPieceSource.Register();
			int count = 0;
			foreach (IStructurePieceSource source in PointOfInterestPieces.Sources)
			{
				count += source == StructureKitPieceSource.Instance ? 1 : 0;
			}
			Assert.That(count, Is.EqualTo(1), "registered once");
			IStructurePieceSource local = PointOfInterestPieces.Register((tag, style, seed) => null, 10);
			try
			{
				var sources = new List<IStructurePieceSource>(PointOfInterestPieces.Sources);
				Assert.That(sources.IndexOf(local), Is.LessThan(sources.IndexOf(StructureKitPieceSource.Instance)), "a hand-made source is asked before the kit");
			}
			finally
			{
				PointOfInterestPieces.Unregister(local);
			}
		}

		// ── Layouts ───────────────────────────────────────────────

		private static PointOfInterestLayoutRequest Request(PointOfInterestLayout layout, float radius, params (PointOfInterestSlotRole role, Vector2 size, float module, int count)[] slots)
		{
			var request = new PointOfInterestLayoutRequest { Layout = layout, Radius = radius, Gap = 1f };
			for (int s = 0; s < slots.Length; s++)
			{
				request.Slots.Add(new PointOfInterestLayoutSlot { Role = slots[s].role, Footprint = slots[s].size, Module = slots[s].module, Count = slots[s].count });
				if (!PointOfInterestLayouts.Generates(PointOfInterestLayouts.RoleFor(layout, s, slots[s].role)))
				{
					for (int k = 0; k < slots[s].count; k++)
					{
						// Sizes vary a little piece to piece, as scaled pieces do.
						request.Items.Add(new PointOfInterestLayoutItem { Slot = s, Footprint = slots[s].size * (0.85f + 0.05f * (k % 4)) });
					}
				}
			}
			return request;
		}

		private static readonly Vector2 HouseSize = new Vector2(6.5f, 7.5f), TentSize = new Vector2(3.5f, 4f), CrateSize = new Vector2(1.2f, 1.2f),
			WallSize = new Vector2(6f, 2.2f), GateSize = new Vector2(12f, 8f), TowerSize = new Vector2(6f, 6f), KeepSize = new Vector2(12f, 12f),
			StoneSize = new Vector2(1.6f, 1.2f), GraveSize = new Vector2(0.9f, 0.4f), SpanSize = new Vector2(3.4f, 4f);

		/// <summary>One request per layout type, as its templates use them.</summary>
		private static IEnumerable<(string name, PointOfInterestLayoutRequest request)> Requests()
		{
			yield return ("Single", Request(PointOfInterestLayout.Single, 14f,
				(PointOfInterestSlotRole.Centre, HouseSize, 0f, 1), (PointOfInterestSlotRole.Scatter, CrateSize, 0f, 8), (PointOfInterestSlotRole.Scatter, TentSize, 0f, 2)));
			yield return ("Ring", Request(PointOfInterestLayout.Ring, 20f,
				(PointOfInterestSlotRole.Ring, TentSize, 0f, 8), (PointOfInterestSlotRole.Centre, new Vector2(2.5f, 2.5f), 0f, 1), (PointOfInterestSlotRole.Scatter, CrateSize, 0f, 6)));
			yield return ("Grid", Request(PointOfInterestLayout.Grid, 22f,
				(PointOfInterestSlotRole.Perimeter, new Vector2(3f, 0.5f), 3f, 1), (PointOfInterestSlotRole.Centre, new Vector2(5f, 6f), 0f, 1),
				(PointOfInterestSlotRole.Rows, GraveSize, 0f, 30)));
			yield return ("Street", Request(PointOfInterestLayout.Street, 55f,
				(PointOfInterestSlotRole.Centre, new Vector2(3f, 3f), 0f, 1), (PointOfInterestSlotRole.Centre, new Vector2(3f, 2f), 0f, 3),
				(PointOfInterestSlotRole.Street, HouseSize, 0f, 18), (PointOfInterestSlotRole.Scatter, CrateSize, 0f, 10)));
			yield return ("Scattered", Request(PointOfInterestLayout.Scattered, 30f,
				(PointOfInterestSlotRole.Scatter, HouseSize, 0f, 4), (PointOfInterestSlotRole.Scatter, WallSize, 0f, 6), (PointOfInterestSlotRole.Scatter, CrateSize, 0f, 8)));
			yield return ("Walled", Request(PointOfInterestLayout.Walled, 130f,
				(PointOfInterestSlotRole.Perimeter, WallSize, 6f, 1), (PointOfInterestSlotRole.Gate, GateSize, 12f, 4), (PointOfInterestSlotRole.WallTower, TowerSize, 0f, 10),
				(PointOfInterestSlotRole.Centre, KeepSize, 0f, 1), (PointOfInterestSlotRole.Centre, new Vector2(3f, 2f), 0f, 8),
				(PointOfInterestSlotRole.Street, HouseSize, 0f, 70), (PointOfInterestSlotRole.Scatter, CrateSize, 0f, 15)));
			var bridge = Request(PointOfInterestLayout.Linear, 14f, (PointOfInterestSlotRole.Span, SpanSize, 4f, 1));
			bridge.SpanHalf = 13f;
			yield return ("Linear", bridge);
			var port = Request(PointOfInterestLayout.Street, 60f, (PointOfInterestSlotRole.Pier, new Vector2(3f, 6f), 6f, 2),
				(PointOfInterestSlotRole.Street, HouseSize, 0f, 16), (PointOfInterestSlotRole.Scatter, CrateSize, 0f, 10));
			port.ShoreAhead = 35f;
			yield return ("Port", port);
		}

		[Test]
		public void EveryLayoutPacksWithoutOverlapInsideItsFootprint()
		{
			foreach ((string name, PointOfInterestLayoutRequest request) in Requests())
			{
				List<PointOfInterestLayoutPiece> pieces = PointOfInterestLayouts.Place(request, new DeterministicRNG(11));
				Assert.That(pieces, Is.Not.Empty, name);
				AssertPacked(name, request, pieces);
			}
		}

		[Test]
		public void EveryTemplateLaysWithoutOverlapAtEverySize()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All())
			{
				PointOfInterestKindRule rule = rules.RuleFor(spec.Kind);
				foreach (int size in spec.Sizes())
				{
					float min = rule != null ? rule.FootprintMin : 10f, max = rule != null ? rule.FootprintMax : 10f;
					float radius = Mathf.Lerp(min, max, size / 2f);
					var random = new DeterministicRNG(1000 + size);
					var request = new PointOfInterestLayoutRequest { Layout = spec.Layout, Radius = radius, Gap = 1f, SpanHalf = radius, ShoreAhead = radius * 0.5f };
					for (int s = 0; s < spec.Pieces.Count; s++)
					{
						PointOfInterestPieceSlot slot = spec.Pieces[s];
						string style = string.IsNullOrEmpty(slot.Style) ? spec.Style : slot.Style;
						PointOfInterestSlotRole role = PointOfInterestLayouts.RoleFor(spec.Layout, s, slot.Role);
						int count = PropsFeature.CountFor(slot, size, true, random);
						PointOfInterestPieceMetrics metrics = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, 0));
						request.Slots.Add(new PointOfInterestLayoutSlot { Role = role, Footprint = metrics.Footprint, Module = metrics.Module, Count = count });
						if (PointOfInterestLayouts.Generates(role))
						{
							continue;
						}
						for (int k = 0; k < count; k++)
						{
							int seed = random.Next();
							float decay = random.Range(slot.MinDecay, Mathf.Max(slot.MinDecay, slot.MaxDecay));
							float scale = random.Range(slot.MinScale, Mathf.Max(slot.MinScale, slot.MaxScale));
							PointOfInterestPieceMetrics piece = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, seed, decay));
							request.Items.Add(new PointOfInterestLayoutItem { Slot = s, Footprint = piece.Footprint * scale });
						}
					}
					List<PointOfInterestLayoutPiece> pieces = PointOfInterestLayouts.Place(request, random);
					string name = $"{spec.Name} at size {size} ({radius:0} m)";
					AssertPacked(name, request, pieces);

					int centreSlot = spec.Pieces.FindIndex(p => PointOfInterestLayouts.RoleFor(spec.Layout, spec.Pieces.IndexOf(p), p.Role) == PointOfInterestSlotRole.Centre);
					if (centreSlot >= 0 && request.Slots[centreSlot].Count > 0)
					{
						// In the middle unless a pier or a span already took it (a fishing hut steps back from its pier).
						Assert.That(pieces.Exists(p => p.Slot == centreSlot), Is.True, $"{name}: its centrepiece stands");
					}
					int wallSlot = spec.Pieces.FindIndex(p => p.Role == PointOfInterestSlotRole.Perimeter);
					if (wallSlot >= 0)
					{
						Assert.That(pieces.FindAll(p => p.Slot == wallSlot).Count, Is.GreaterThanOrEqualTo(3), $"{name}: its wall ring fits");
					}
					int streetSlot = spec.Pieces.FindIndex(p => p.Role == PointOfInterestSlotRole.Street);
					if (streetSlot >= 0 && spec.Kind != POIType.AncientRoad)
					{
						int asked = 0, laid = 0;
						for (int i = 0; i < request.Items.Count; i++)
						{
							if (spec.Pieces[request.Items[i].Slot].Role == PointOfInterestSlotRole.Street)
							{
								asked++;
								laid += pieces.Exists(p => p.Item == i) ? 1 : 0;
							}
						}
						Assert.That(laid, Is.GreaterThanOrEqualTo(asked * 0.6f), $"{name}: {laid} of {asked} houses found room on its streets");
					}
				}
			}
		}

		/// <summary>No two pieces overlap (a wall ring's own modules, a bridge's spans and a pier's modules meet end to end), all inside the footprint.</summary>
		private static void AssertPacked(string name, PointOfInterestLayoutRequest request, List<PointOfInterestLayoutPiece> pieces)
		{
			float limit = request.Radius * PointOfInterestLayouts.Inset + 0.01f;
			for (int i = 0; i < pieces.Count; i++)
			{
				PointOfInterestLayoutPiece a = pieces[i];
				bool centrepiece = a.Role == PointOfInterestSlotRole.Centre && a.Offset == Vector2.zero;
				if (!PointOfInterestLayouts.Reaches(a.Role) && !centrepiece)
				{
					Assert.That(PointOfInterestLayouts.Reach(a.Offset, a.Yaw, a.Footprint), Is.LessThanOrEqualTo(limit), $"{name}: a {a.Role} piece at {a.Offset} stays inside");
				}
				for (int j = i + 1; j < pieces.Count; j++)
				{
					PointOfInterestLayoutPiece b = pieces[j];
					if (Group(a) != 0 && Group(a) == Group(b))
					{
						continue;
					}
					Assert.That(Overlap(a, b), Is.LessThan(0.05f), $"{name}: {a.Role} at {a.Offset} and {b.Role} at {b.Offset} overlap");
				}
			}
		}

		private static int Group(PointOfInterestLayoutPiece piece)
		{
			switch (piece.Role)
			{
				case PointOfInterestSlotRole.Perimeter:
				case PointOfInterestSlotRole.Gate:
				case PointOfInterestSlotRole.WallTower:
					return 1;
				case PointOfInterestSlotRole.Span:
					return 2;
				case PointOfInterestSlotRole.Pier:
					return 3;
			}
			return 0;
		}

		/// <summary>How far two footprints overlap along their least-overlapping axis, metres (≤ 0 when apart).</summary>
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

		[Test]
		public void AWallRingClosesRoundItsGates()
		{
			PointOfInterestLayoutRequest request = Request(PointOfInterestLayout.Walled, 60f,
				(PointOfInterestSlotRole.Perimeter, WallSize, 6f, 1), (PointOfInterestSlotRole.Gate, GateSize, 12f, 2));
			List<PointOfInterestLayoutPiece> pieces = PointOfInterestLayouts.Place(request, new DeterministicRNG(3));
			List<PointOfInterestLayoutPiece> walls = pieces.FindAll(p => p.Role == PointOfInterestSlotRole.Perimeter);
			List<PointOfInterestLayoutPiece> gates = pieces.FindAll(p => p.Role == PointOfInterestSlotRole.Gate);
			Assert.That(gates.Count, Is.EqualTo(2));
			Assert.That(walls.Count, Is.GreaterThan(10));

			// Every end of every wall module meets the end of another module or a gate's side.
			var ends = new List<Vector2>();
			foreach (PointOfInterestLayoutPiece wall in walls)
			{
				Vector2 along = PointOfInterestLayouts.Rotate(Vector2.right, wall.Yaw) * (wall.Footprint.x * 0.5f);
				ends.Add(wall.Offset + along);
				ends.Add(wall.Offset - along);
				Assert.That(wall.Footprint.x, Is.EqualTo(6f).Within(1e-4f), "a module is laid at its own length");
			}
			var gateEnds = new List<Vector2>();
			foreach (PointOfInterestLayoutPiece gate in gates)
			{
				Vector2 along = PointOfInterestLayouts.Rotate(Vector2.right, gate.Yaw) * (gate.Footprint.x * 0.5f);
				gateEnds.Add(gate.Offset + along);
				gateEnds.Add(gate.Offset - along);
			}
			for (int i = 0; i < ends.Count; i++)
			{
				bool met = false;
				for (int j = 0; j < ends.Count && !met; j++)
				{
					met = j != i && j / 2 != i / 2 && Vector2.Distance(ends[i], ends[j]) < 0.01f;
				}
				foreach (Vector2 end in gateEnds)
				{
					met |= Vector2.Distance(ends[i], end) < 0.6f;
				}
				Assert.That(met, Is.True, $"the wall end at {ends[i]} meets a neighbour");
			}
			// The first gate stands across the heading.
			PointOfInterestLayoutPiece ahead = gates.Find(g => g.Offset.y > 0f && Mathf.Abs(g.Offset.x) < 0.5f);
			Assert.That(ahead.Footprint, Is.EqualTo(GateSize), "a gate across the heading");
			Assert.That(Mathf.DeltaAngle(ahead.Yaw, 180f), Is.EqualTo(0f).Within(0.5f), "the gate's front looks out, ahead");
		}

		[Test]
		public void AWallWithNoGateLeavesOneOpeningAhead()
		{
			PointOfInterestLayoutRequest request = Request(PointOfInterestLayout.Walled, 20f, (PointOfInterestSlotRole.Perimeter, new Vector2(3f, 0.4f), 3f, 1));
			List<PointOfInterestLayoutPiece> walls = PointOfInterestLayouts.Place(request, new DeterministicRNG(5));
			float side = walls[0].Offset.magnitude;
			int sides = Mathf.RoundToInt(Mathf.PI / Mathf.Atan(1.5f / side));
			Assert.That(walls.Count, Is.EqualTo(sides - 2), "a 3 m railing leaves two modules out (at least 4 m) for the way in");
			foreach (PointOfInterestLayoutPiece wall in walls)
			{
				Assert.That(wall.Offset.y < side * 0.9f || Mathf.Abs(wall.Offset.x) > 2f, Is.True, $"nothing at the heading: {wall.Offset}");
			}
		}

		[Test]
		public void PiecesFaceSensibly()
		{
			PointOfInterestLayoutRequest ring = Request(PointOfInterestLayout.Ring, 20f, (PointOfInterestSlotRole.Ring, TentSize, 0f, 7), (PointOfInterestSlotRole.Centre, new Vector2(2f, 2f), 0f, 1));
			foreach (PointOfInterestLayoutPiece piece in PointOfInterestLayouts.Place(ring, new DeterministicRNG(2)))
			{
				if (piece.Role != PointOfInterestSlotRole.Ring)
				{
					continue;
				}
				Vector2 front = PointOfInterestLayouts.Rotate(Vector2.down, piece.Yaw);
				Assert.That(Vector2.Dot(front, -piece.Offset.normalized), Is.GreaterThan(0.99f), "a tent faces the fire");
			}

			PointOfInterestLayoutRequest street = Request(PointOfInterestLayout.Street, 50f, (PointOfInterestSlotRole.Street, HouseSize, 0f, 16));
			foreach (PointOfInterestLayoutPiece piece in PointOfInterestLayouts.Place(street, new DeterministicRNG(2)))
			{
				// A 45 m street plan is two 4 m streets crossing at the middle: a house's front looks square onto the
				// centre line of one of them across its set-back (the gap) and half the street.
				Vector2 front = PointOfInterestLayouts.Rotate(Vector2.down, piece.Yaw);
				Assert.That(Mathf.Max(Mathf.Abs(front.x), Mathf.Abs(front.y)), Is.EqualTo(1f).Within(1e-4f), "square to the streets");
				Vector2 kerb = piece.Offset + front * (piece.Footprint.y * 0.5f + 1f + 2f);
				Assert.That(Mathf.Abs(front.x) > 0.5f ? Mathf.Abs(kerb.x) : Mathf.Abs(kerb.y), Is.LessThan(0.01f), $"the house at {piece.Offset} fronts its street");
			}

			PointOfInterestLayoutRequest graves = Request(PointOfInterestLayout.Grid, 15f, (PointOfInterestSlotRole.Rows, GraveSize, 0f, 20));
			List<PointOfInterestLayoutPiece> rows = PointOfInterestLayouts.Place(graves, new DeterministicRNG(2));
			Assert.That(rows.Count, Is.EqualTo(20));
			var lines = new HashSet<float>();
			foreach (PointOfInterestLayoutPiece grave in rows)
			{
				Assert.That(grave.Yaw, Is.EqualTo(180f), "graves all face ahead");
				lines.Add(Mathf.Round(grave.Offset.y * 100f));
			}
			Assert.That(lines.Count, Is.LessThan(rows.Count / 2), "graves stand in rows");
		}

		[Test]
		public void ABridgeSpansTheWholeCrossing()
		{
			PointOfInterestLayoutRequest request = Request(PointOfInterestLayout.Linear, 10f, (PointOfInterestSlotRole.Span, SpanSize, 4f, 1));
			request.SpanHalf = 13f;
			List<PointOfInterestLayoutPiece> spans = PointOfInterestLayouts.Place(request, new DeterministicRNG(1));
			Assert.That(spans.Count, Is.EqualTo(7), "26 m of crossing in 4 m spans");
			spans.Sort((p, q) => p.Offset.y.CompareTo(q.Offset.y));
			Assert.That(spans[0].Offset.y - 2f, Is.LessThanOrEqualTo(-13f));
			Assert.That(spans[spans.Count - 1].Offset.y + 2f, Is.GreaterThanOrEqualTo(13f));
			for (int i = 0; i < spans.Count; i++)
			{
				Assert.That(spans[i].Offset.x, Is.EqualTo(0f), "on the heading, across the river");
				Assert.That(spans[i].Yaw, Is.EqualTo(0f), "a span runs along its z, as the heading does");
				if (i > 0)
				{
					Assert.That(spans[i].Offset.y - spans[i - 1].Offset.y, Is.EqualTo(4f).Within(1e-4f), "end to end");
				}
			}
		}

		[Test]
		public void PiersStartAtTheShoreOrNotAtAll()
		{
			PointOfInterestLayoutRequest request = Request(PointOfInterestLayout.Single, 30f, (PointOfInterestSlotRole.Pier, new Vector2(3f, 6f), 6f, 2));
			Assert.That(PointOfInterestLayouts.Place(request, new DeterministicRNG(1)), Is.Empty, "no shore, no pier");
			request.ShoreAhead = 12f;
			List<PointOfInterestLayoutPiece> piers = PointOfInterestLayouts.Place(request, new DeterministicRNG(1));
			Assert.That(piers.Count, Is.EqualTo(2 * 3), "two piers of three modules");
			foreach (PointOfInterestLayoutPiece pier in piers)
			{
				Assert.That(pier.Offset.y, Is.GreaterThanOrEqualTo(12f - 1e-4f), "out from the shore");
			}
		}

		[Test]
		public void TheSameSeedLaysTheSameSite()
		{
			foreach ((string name, PointOfInterestLayoutRequest request) in Requests())
			{
				List<PointOfInterestLayoutPiece> first = PointOfInterestLayouts.Place(request, new DeterministicRNG(42));
				List<PointOfInterestLayoutPiece> again = PointOfInterestLayouts.Place(request, new DeterministicRNG(42));
				Assert.That(again.Count, Is.EqualTo(first.Count), name);
				for (int i = 0; i < first.Count; i++)
				{
					Assert.That(again[i].Offset, Is.EqualTo(first[i].Offset), name);
					Assert.That(again[i].Yaw, Is.EqualTo(first[i].Yaw), name);
					Assert.That(again[i].Slot, Is.EqualTo(first[i].Slot), name);
					Assert.That(again[i].Item, Is.EqualTo(first[i].Item), name);
				}
			}
		}

		[Test]
		public void NothingIsLaidOnAGameplayClearing()
		{
			PointOfInterestLayoutRequest request = Request(PointOfInterestLayout.Street, 40f,
				(PointOfInterestSlotRole.Centre, new Vector2(6f, 6f), 0f, 1), (PointOfInterestSlotRole.Centre, new Vector2(3f, 2f), 0f, 6),
				(PointOfInterestSlotRole.Street, HouseSize, 0f, 14), (PointOfInterestSlotRole.Scatter, CrateSize, 0f, 30));
			var clearing = new Vector3(-3f, 2f, 6f);
			request.Clearings.Add(clearing);
			foreach (PointOfInterestLayoutPiece piece in PointOfInterestLayouts.Place(request, new DeterministicRNG(4)))
			{
				var spot = new PointOfInterestLayoutPiece { Offset = new Vector2(clearing.x, clearing.z), Footprint = Vector2.one * (2f * clearing.y) };
				Assert.That(Overlap(piece, spot), Is.LessThan(0.05f), $"a {piece.Role} piece at {piece.Offset} keeps off the waypoint's spot");
			}
		}

		[Test]
		public void AChamberReanchorsItsFeatures()
		{
			var site = new PointOfInterestRecord { Id = 7, Kind = POIType.Cave, Position = new Vector3(100f, 30f, 50f), Yaw = 90f, Radius = 12f, Race = "orc", SiteSeed = 99 };
			var context = new PointOfInterestSiteContext { Record = site, GroundAt = (x, z) => 80f };
			PointOfInterestSiteContext chamber = PointOfInterestChamberFeature.InChamber(context, new Vector3(120f, 22f, 60f), 5f, 180f);
			Assert.That(chamber.Record, Is.Not.SameAs(site), "the site's record is left as it was");
			Assert.That(site.Position, Is.EqualTo(new Vector3(100f, 30f, 50f)));
			Assert.That(chamber.Record.Id, Is.EqualTo(7));
			Assert.That(chamber.Record.Race, Is.EqualTo("orc"));
			Assert.That(chamber.Record.SiteSeed, Is.EqualTo(99));
			Assert.That(chamber.Record.Radius, Is.EqualTo(5f));
			Vector3 ahead = chamber.OnGround(new Vector2(0f, 2f));
			Assert.That(ahead.y, Is.EqualTo(22f), "on the chamber floor, not the hillside over it");
			Assert.That(ahead.x, Is.EqualTo(120f).Within(1e-3f));
			Assert.That(ahead.z, Is.EqualTo(58f).Within(1e-3f), "ahead along the chamber's heading");
		}

		// ── Counts and finishes ───────────────────────────────────

		[Test]
		public void CountsFollowTheSizeClass()
		{
			var slot = new PointOfInterestPieceSlot { Tag = "house", MinCount = 0, MaxCount = 9 };
			var random = new DeterministicRNG(9);
			for (int i = 0; i < 50; i++)
			{
				Assert.That(PropsFeature.CountFor(slot, 0, true, random), Is.InRange(0, 3));
				Assert.That(PropsFeature.CountFor(slot, 2, true, random), Is.InRange(6, 9));
				Assert.That(PropsFeature.CountFor(slot, 1, false, random), Is.InRange(0, 9));
			}
			var one = new PointOfInterestPieceSlot { Tag = "keep", MinCount = 1, MaxCount = 1 };
			Assert.That(PropsFeature.CountFor(one, 0, true, random), Is.EqualTo(1));
		}

		[Test]
		public void FinishesFollowTheSite()
		{
			Assert.That(PropsFeature.FinishFor(PointOfInterestFinish.Auto, 1f, POIType.SunkenShip, "Seabed", 0f, 0.99f), Is.EqualTo(StructureFinish.Algae), "under water");
			Assert.That(PropsFeature.FinishFor(PointOfInterestFinish.Auto, 1f, POIType.Ruins, "Desert", 1f, 0f), Is.EqualTo(StructureFinish.None), "no moss in a desert");
			Assert.That(PropsFeature.FinishFor(PointOfInterestFinish.Auto, 1f, POIType.Ruins, "Forest", 0.8f, 0.5f), Is.EqualTo(StructureFinish.Mossy), "old stone in a forest");
			Assert.That(PropsFeature.FinishFor(PointOfInterestFinish.Auto, 1f, POIType.Village, "Forest", 0f, 0.5f), Is.EqualTo(StructureFinish.None), "a lived-in village is kept");
			Assert.That(PropsFeature.FinishFor(PointOfInterestFinish.Auto, 1f, POIType.Village, "Swamp", 0f, 0.1f), Is.EqualTo(StructureFinish.Algae), "a swamp's damp");
			Assert.That(PropsFeature.FinishFor(PointOfInterestFinish.Auto, 1f, POIType.Battlefield, "Plains", 0f, 0.2f), Is.EqualTo(StructureFinish.Charred), "a battlefield burned");
			Assert.That(PropsFeature.FinishFor(PointOfInterestFinish.Charred, 0.5f, POIType.Ruins, "Plains", 0f, 0.7f), Is.EqualTo(StructureFinish.None), "a forced finish on its share");
			Assert.That(PropsFeature.FinishFor(PointOfInterestFinish.Charred, 0.5f, POIType.Ruins, "Plains", 0f, 0.3f), Is.EqualTo(StructureFinish.Charred));
		}

		private static string[] Ids(string query, string style)
		{
			List<StructurePiece> pieces = StructureKitPieceSource.Candidates(query, style);
			var ids = new string[pieces.Count];
			for (int i = 0; i < ids.Length; i++)
			{
				ids[i] = pieces[i].Id;
			}
			return ids;
		}
	}
}
