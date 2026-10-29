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
	/// overlap inside their footprints, close their wall rings, face sensibly and repeat for the same seed. Pure but for
	/// one check of the template assets on disk: no scene.
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
				// Or a carved ground kind that lays props on its cut (a cenote's altar, a sandworm pit's bones: 2026-10-27).
				Assert.That(cave || PointOfInterestKinds.Info(spec.Kind).Has(PointOfInterestTraits.Structure) || PointOfInterestTemplateSpecs.CarvedGround(spec.Kind), Is.True,
					$"{spec.Name} is for a kind the kit or a shaper builds");
				// A mine is dug by the cave shaper too, but its template is the yard before the adit (its ore is in the stope).
				Assert.That(spec.InChamber, Is.EqualTo(cave && spec.Kind != POIType.Mine), $"{spec.Name}: only caves build in a chamber");
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

		/// <summary>
		/// The template assets on disk, not the specs: an asset authored before a piece was retired keeps naming it and
		/// lays nothing in its place (2026-10-15: windmill sites without a windmill), since authoring keeps existing assets.
		/// </summary>
		[Test]
		public void EveryTemplateAssetResolvesInTheKit()
		{
			var missing = new List<string>();
			foreach (string guid in UnityEditor.AssetDatabase.FindAssets($"t:{nameof(PointOfInterestTemplate)}", new[] { PointOfInterestTemplate.Folder }))
			{
				string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
				List<string> tags = PointOfInterestTemplateAuthoring.Unresolved(UnityEditor.AssetDatabase.LoadAssetAtPath<PointOfInterestTemplate>(path));
				if (tags.Count > 0)
				{
					missing.Add($"{path}: {string.Join(", ", tags)}");
				}
			}
			Assert.That(missing, Is.Empty, "template assets naming pieces the kit no longer has (re-author them)");
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
				// (2026-10-19) Its palace compound where the keep was (PointOfInterestPalace).
				Assert.That(spec.Pieces.Exists(p => p.Role == PointOfInterestSlotRole.Centre && p.Tag == PointOfInterestTemplateSpecs.Palace), Is.True, $"{spec.Name} has its palace at the centre");
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
					Assert.That(PropsFeature.FinishFor(spec.Finish, spec.FinishChance, spec.Kind, BiomeArtSpec.WeatheringByName("Seabed"), 0f, 0.99f), Is.EqualTo(StructureFinish.Algae), spec.Name);
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
		public void WellsStandWellSpacingApart_AndASmallVillageKeepsOne()
		{
			var city = Request(PointOfInterestLayout.Walled, 130f,
				(PointOfInterestSlotRole.Perimeter, WallSize, 6f, 1), (PointOfInterestSlotRole.Gate, GateSize, 12f, 4), (PointOfInterestSlotRole.WallTower, TowerSize, 0f, 10),
				(PointOfInterestSlotRole.Centre, KeepSize, 0f, 1), (PointOfInterestSlotRole.Centre, new Vector2(2.4f, 2.4f), 0f, 4),
				(PointOfInterestSlotRole.Street, HouseSize, 0f, 70), (PointOfInterestSlotRole.Scatter, CrateSize, 0f, 15));
			var village = Request(PointOfInterestLayout.Street, 40f,
				(PointOfInterestSlotRole.Centre, new Vector2(2.4f, 2.4f), 0f, 2), (PointOfInterestSlotRole.Street, HouseSize, 0f, 12));
			foreach ((string name, PointOfInterestLayoutRequest request, int well, int least) in new[] { ("city", city, 4, 2), ("village", village, 0, 1) })
			{
				PointOfInterestLayoutSlot slot = request.Slots[well];
				slot.Well = true;
				request.Slots[well] = slot;
				for (int seed = 1; seed <= 6; seed++)
				{
					List<PointOfInterestLayoutPiece> wells = PointOfInterestLayouts.Place(request, new DeterministicRNG(seed)).FindAll(p => p.Slot == well);
					Assert.That(wells.Count, Is.GreaterThanOrEqualTo(least), $"{name} seed {seed}: wells laid");
					for (int i = 0; i < wells.Count; i++)
					{
						for (int j = i + 1; j < wells.Count; j++)
						{
							Assert.That(Vector2.Distance(wells[i].Offset, wells[j].Offset), Is.GreaterThanOrEqualTo(PointOfInterestLayouts.WellSpacing - 1e-5f),
								$"{name} seed {seed}: wells {i} and {j}");
						}
					}
					if (name == "village")
					{
						Assert.That(wells.Count, Is.EqualTo(1), $"village seed {seed}: a second well 50 m off would stand outside it");
					}
				}
			}
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
					// The shore at the site's edge ahead: a port's outline is drawn on dry land up to its water (PointOfInterestPlanner.Shape).
					var request = new PointOfInterestLayoutRequest { Layout = spec.Layout, Radius = radius, Gap = 1f, SpanHalf = radius, ShoreAhead = radius * PointOfInterestLayouts.Inset };
					for (int s = 0; s < spec.Pieces.Count; s++)
					{
						PointOfInterestPieceSlot slot = spec.Pieces[s];
						string style = string.IsNullOrEmpty(slot.Style) ? spec.Style : slot.Style;
						PointOfInterestSlotRole role = PointOfInterestLayouts.RoleFor(spec.Layout, s, slot.Role);
						int count = PropsFeature.CountFor(slot, size, true, random);
						PointOfInterestPieceMetrics metrics = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, 0));
						// As PropsFeature builds them: a stone curtain's walk (its towers stand on it), its gates' and towers' joins.
						bool station = role == PointOfInterestSlotRole.Gate || role == PointOfInterestSlotRole.WallTower;
						request.Slots.Add(new PointOfInterestLayoutSlot
						{
							Role = role, Footprint = metrics.Footprint, Module = metrics.Module, Count = count,
							Walk = role == PointOfInterestSlotRole.Perimeter ? PropsFeature.PerimeterWalk(slot.Tag, style) : 0f,
							Join = station ? PropsFeature.StationJoin(slot.Tag, style, 0) : 0f,
						});
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
						var sizes = new List<string>();
						for (int i = 0; i < request.Items.Count; i++)
						{
							if (spec.Pieces[request.Items[i].Slot].Role == PointOfInterestSlotRole.Street)
							{
								sizes.Add($"{request.Items[i].Footprint.x:0.0}x{request.Items[i].Footprint.y:0.0}{(pieces.Exists(p => p.Item == i) ? "" : "!")}");
							}
						}
						Assert.That(laid, Is.GreaterThanOrEqualTo(asked * 0.6f),
							$"{name}: {laid} of {asked} houses found room on its streets ({request.ChosenPlan}, {request.Streets.Count} streets; {string.Join(" ", sizes)}; ! = not laid)");
					}
				}
			}
		}

		/// <summary>No two pieces overlap (a wall ring's own modules, a bridge's spans and a pier's modules meet end to end), all inside the footprint.</summary>
		private static void AssertPacked(string name, PointOfInterestLayoutRequest request, List<PointOfInterestLayoutPiece> pieces)
		{
			var four = new Vector2[4];
			for (int i = 0; i < pieces.Count; i++)
			{
				PointOfInterestLayoutPiece a = pieces[i];
				bool centrepiece = a.Role == PointOfInterestSlotRole.Centre && a.Offset == Vector2.zero;
				if (!PointOfInterestLayouts.Reaches(a.Role) && !centrepiece)
				{
					// Every corner inside the boundary that way: the outline's (or the disc's) inset share.
					PointOfInterestLayouts.Corners(a.Offset, a.Yaw, a.Footprint, four);
					foreach (Vector2 c in four)
					{
						Assert.That(c.magnitude, Is.LessThanOrEqualTo(Boundary(request, c) + 0.01f), $"{name}: a {a.Role} piece at {a.Offset} stays inside");
					}
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

		/// <summary>The layout's boundary toward a site-frame offset: its outline's reach that way (its radius for a disc), inset.</summary>
		private static float Boundary(PointOfInterestLayoutRequest request, Vector2 toward)
			=> PointOfInterestLayouts.Inset * PointOfInterestOutline.ReachToward(request.Outline, request.Radius, PointOfInterestLayouts.DirectionOf(toward) + request.Yaw);

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
				// Fitted to its chord of the ring (stretched or shrunk a little), and run on past a corner to meet its neighbour.
				Assert.That(wall.Footprint.x, Is.EqualTo(6f).Within(6f * 0.15f), "a module is laid near its own length");
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
					// Within each one's run on past the corner (2026-10-19: the module off a gate leaves along its x, so the next
					// joint turns a little more than the ring's own curve).
					met = j != i && j / 2 != i / 2 && Vector2.Distance(ends[i], ends[j]) < 0.3f;
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

		/// <summary>
		/// A gate stands exactly on the bearing its way comes in on (a moat's drawbridge before it, the way's crossing on that
		/// line): the ring gives up a side or few rather than leave a gate half a side off the street ends.
		/// </summary>
		[TestCase(43f, 2)]
		[TestCase(47f, 2)]
		[TestCase(58f, 4)]
		[TestCase(61f, 4)]
		public void AWallsGatesStandOnTheirWaysBearings(float radius, int ways)
		{
			PointOfInterestLayoutRequest request = Request(PointOfInterestLayout.Walled, radius,
				(PointOfInterestSlotRole.Perimeter, WallSize, 6f, 1), (PointOfInterestSlotRole.Gate, GateSize, 12f, 1));
			for (int k = 0; k < ways; k++)
			{
				request.Entrances.Add(k * 360f / ways);
			}
			List<PointOfInterestLayoutPiece> gates = PointOfInterestLayouts.Place(request, new DeterministicRNG(3)).FindAll(p => p.Role == PointOfInterestSlotRole.Gate);
			Assert.That(gates.Count, Is.EqualTo(ways));
			foreach (float bearing in request.Entrances)
			{
				var dir = new Vector2(Mathf.Sin(bearing * Mathf.Deg2Rad), Mathf.Cos(bearing * Mathf.Deg2Rad));
				PointOfInterestLayoutPiece gate = gates.Find(g => Vector2.Dot(g.Offset.normalized, dir) > 0.9f);
				Assert.That(gate.Footprint, Is.EqualTo(GateSize), $"a gate toward {bearing}°");
				Assert.That(Mathf.Abs(gate.Offset.x * dir.y - gate.Offset.y * dir.x), Is.LessThan(0.01f), $"the gate toward {bearing}° stands on its bearing: {gate.Offset}");
				Assert.That(gate.Offset.magnitude, Is.LessThanOrEqualTo(radius), "the ring is never larger than its room");
			}
		}

		[Test]
		public void AWallWithNoGateLeavesOneOpeningAhead()
		{
			PointOfInterestLayoutRequest request = Request(PointOfInterestLayout.Walled, 20f, (PointOfInterestSlotRole.Perimeter, new Vector2(3f, 0.4f), 3f, 1));
			List<PointOfInterestLayoutPiece> walls = PointOfInterestLayouts.Place(request, new DeterministicRNG(5));
			Assert.That(walls.Count, Is.GreaterThan(20), "the ring closes all round but for its way in");
			float side = walls[0].Offset.magnitude;
			// A 3 m railing leaves two modules out (at least 4 m) for the way in: no module end within 2 m of the heading.
			foreach (PointOfInterestLayoutPiece wall in walls)
			{
				Vector2 along = PointOfInterestLayouts.Rotate(Vector2.right, wall.Yaw) * (wall.Footprint.x * 0.5f);
				foreach (Vector2 end in new[] { wall.Offset + along, wall.Offset - along, wall.Offset })
				{
					Assert.That(end.y < side * 0.8f || Mathf.Abs(end.x) > 2.9f, Is.True, $"nothing at the heading: {wall.Offset}");
				}
			}
		}

		/// <summary>
		/// Gates stand on the ways' own bearings whatever they are (2026-10-18: a road comes in on its own bearing), round an
		/// outline that is no circle, every module of the ring meeting the next (or a gate's side) within a few centimetres,
		/// and nothing of the ring outside the outline.
		/// </summary>
		[TestCase(37f, 160f, 250f)]
		[TestCase(5f, 95f, 300f)]
		public void AnIrregularWallsGatesStandOnTheRoadsBearings(float a, float b, float c)
		{
			PointOfInterestLayoutRequest request = Request(PointOfInterestLayout.Walled, 70f,
				(PointOfInterestSlotRole.Perimeter, WallSize, 6f, 1), (PointOfInterestSlotRole.Gate, GateSize, 12f, 3), (PointOfInterestSlotRole.WallTower, TowerSize, 0f, 6));
			request.Outline = Oval(70f, 0.7f, 30f);
			request.Yaw = 20f;
			request.Entrances.AddRange(new[] { a, b, c });
			List<PointOfInterestLayoutPiece> pieces = PointOfInterestLayouts.Place(request, new DeterministicRNG(8));
			AssertPacked("irregular wall", request, pieces);
			List<PointOfInterestLayoutPiece> gates = pieces.FindAll(p => p.Role == PointOfInterestSlotRole.Gate);
			Assert.That(gates.Count, Is.EqualTo(3));
			foreach (float bearing in request.Entrances)
			{
				var dir = new Vector2(Mathf.Sin(bearing * Mathf.Deg2Rad), Mathf.Cos(bearing * Mathf.Deg2Rad));
				PointOfInterestLayoutPiece gate = gates.Find(g => Vector2.Dot(g.Offset.normalized, dir) > 0.99f);
				Assert.That(gate.Footprint, Is.EqualTo(GateSize), $"a gate toward {bearing}°");
				Assert.That(Mathf.Abs(gate.Offset.x * dir.y - gate.Offset.y * dir.x), Is.LessThan(0.01f), $"the gate toward {bearing}° stands on its bearing");
			}
			// Every module meets the next: its outer face's end on its neighbour's (or, where the ring bends outward, its inner
			// face's: the modules mitred by running on past the corner), or its line's end on a gate's jamb.
			List<PointOfInterestLayoutPiece> walls = pieces.FindAll(p => p.Role == PointOfInterestSlotRole.Perimeter);
			Assert.That(walls.Count, Is.GreaterThan(20));
			var faces = new List<Vector2>();
			var backs = new List<Vector2>();
			var lines = new List<Vector2>();
			foreach (PointOfInterestLayoutPiece wall in walls)
			{
				Vector2 along = PointOfInterestLayouts.Rotate(Vector2.right, wall.Yaw) * (wall.Footprint.x * 0.5f);
				Vector2 outward = PointOfInterestLayouts.Rotate(Vector2.down, wall.Yaw) * (wall.Footprint.y * 0.5f);
				faces.Add(wall.Offset + along + outward);
				faces.Add(wall.Offset - along + outward);
				backs.Add(wall.Offset + along - outward);
				backs.Add(wall.Offset - along - outward);
				lines.Add(wall.Offset + along);
				lines.Add(wall.Offset - along);
			}
			var jambs = new List<Vector2>();
			foreach (PointOfInterestLayoutPiece gate in gates)
			{
				Vector2 along = PointOfInterestLayouts.Rotate(Vector2.right, gate.Yaw) * (gate.Footprint.x * 0.5f);
				jambs.Add(gate.Offset + along);
				jambs.Add(gate.Offset - along);
			}
			for (int i = 0; i < faces.Count; i++)
			{
				float nearest = float.PositiveInfinity;
				for (int j = 0; j < faces.Count; j++)
				{
					if (j / 2 != i / 2)
					{
						nearest = Mathf.Min(nearest, Mathf.Min(Vector2.Distance(faces[i], faces[j]), Vector2.Distance(backs[i], backs[j])));
					}
				}
				foreach (Vector2 jamb in jambs)
				{
					nearest = Mathf.Min(nearest, Vector2.Distance(lines[i], jamb));
				}
				Assert.That(nearest, Is.LessThan(0.08f), $"the wall end at {lines[i]} meets its neighbour");
			}
		}

		/// <summary>
		/// A moated site's wall keeps to its berm (2026-10-18: "Palisades and town walls land in moats sometimes"): every
		/// corner of the ring stands inside the ditch's inner bank by the footing's clearance, even where the moat runs
		/// nearer than the outline's edge.
		/// </summary>
		[Test]
		public void AMoatedWallStandsClearOfItsDitch()
		{
			PointOfInterestLayoutRequest request = Request(PointOfInterestLayout.Walled, 50f,
				(PointOfInterestSlotRole.Perimeter, new Vector2(4f, 1f), 4f, 1), (PointOfInterestSlotRole.Gate, new Vector2(4f, 2f), 4f, 1), (PointOfInterestSlotRole.WallTower, TowerSize, 0f, 4));
			request.Outline = Oval(50f, 0.75f, 0f);
			// A moat drawn tighter than the outline on one side: its inner bank 0.85 of the outline's reach there.
			float[] inner = new float[request.Outline.Length];
			for (int k = 0; k < inner.Length; k++)
			{
				inner[k] = request.Outline[k] * (k < inner.Length / 2 ? 0.85f : 1.02f);
			}
			var values = new float[5 + inner.Length];
			values[0] = 2f;
			values[1] = PointOfInterestOutline.Max(inner, 50f);
			values[2] = 7f;
			values[3] = 3f;
			Array.Copy(inner, 0, values, 5, inner.Length);
			request.Moat = new PointOfInterestShape { SiteId = 1, Shaper = MoatShaper.ShaperName, Values = values };
			List<PointOfInterestLayoutPiece> pieces = PointOfInterestLayouts.Place(request, new DeterministicRNG(2));
			var four = new Vector2[4];
			int ring = 0;
			foreach (PointOfInterestLayoutPiece piece in pieces)
			{
				if (piece.Role != PointOfInterestSlotRole.Perimeter && piece.Role != PointOfInterestSlotRole.Gate && piece.Role != PointOfInterestSlotRole.WallTower)
				{
					continue;
				}
				ring++;
				PointOfInterestLayouts.Corners(piece.Offset, piece.Yaw, piece.Footprint, four);
				foreach (Vector2 c in four)
				{
					float lip = MoatShaper.InnerToward(request.Moat, c.x, c.y);
					Assert.That(c.magnitude, Is.LessThanOrEqualTo(lip - PointOfInterestLayouts.MoatClearMetres + 0.01f), $"a {piece.Role} corner at {c} stands on the berm, not in the ditch");
				}
			}
			Assert.That(ring, Is.GreaterThan(15), "the ring is laid");
		}

		/// <summary>An outline drawn as an oval (world bearings): <paramref name="radius"/> along its long axis at <paramref name="axis"/>°, <paramref name="ratio"/> of it across.</summary>
		private static float[] Oval(float radius, float ratio, float axis)
		{
			var outline = new float[PointOfInterestOutline.Bearings];
			for (int k = 0; k < outline.Length; k++)
			{
				float a = k * 2f * Mathf.PI / outline.Length - axis * Mathf.Deg2Rad;
				float x = Mathf.Sin(a) / (radius * ratio), z = Mathf.Cos(a) / radius;
				outline[k] = 1f / Mathf.Sqrt(x * x + z * z);
			}
			return outline;
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
				// A house's front looks (to within its few degrees of turn) square onto the street before it, across its
				// set-back (the gap and up to most of a metre more) and half the street.
				AssertFronts(street, piece);
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

		/// <summary>
		/// A street house fronts a street (or a plaza's edge): straight out of its front, past its set-back (the gap and up to
		/// most of a metre more) and half the street, the street's line crosses square to it.
		/// </summary>
		private static void AssertFronts(PointOfInterestLayoutRequest request, PointOfInterestLayoutPiece piece)
		{
			Vector2 front = PointOfInterestLayouts.Rotate(Vector2.down, piece.Yaw);
			Vector2 door = piece.Offset + front * (piece.Footprint.y * 0.5f);
			bool Crosses(Vector2[] line, bool closed, float half)
			{
				int n = closed ? line.Length : line.Length - 1;
				for (int i = 0; i < n; i++)
				{
					Vector2 a = line[i], b = line[(i + 1) % line.Length], e = b - a;
					float den = front.x * e.y - front.y * e.x;
					if (Mathf.Abs(den) < 1e-6f)
					{
						continue;
					}
					Vector2 d = a - door;
					float s = (d.x * e.y - d.y * e.x) / den, u = (d.x * front.y - d.y * front.x) / den;
					// Square to within the few degrees a house is turned and a curving street bends across its width.
					if (u >= -0.02f && u <= 1.02f && s >= half + 0.85f && s <= half + 2f && Mathf.Abs(Vector2.Dot(front, e.normalized)) < 0.35f)
					{
						return true;
					}
				}
				return false;
			}
			bool fronts = false;
			foreach (PointOfInterestLayoutStreet street in request.Streets)
			{
				fronts |= Crosses(street.Points, false, street.Width * 0.5f);
			}
			foreach (Vector2[] plaza in request.Plazas)
			{
				fronts |= Crosses(plaza, true, 0f);
			}
			if (!fronts)
			{
				// What it does face, for the message: the nearest street line to its door.
				float nearest = float.PositiveInfinity, square = 0f;
				foreach (PointOfInterestLayoutStreet street in request.Streets)
				{
					for (int i = 1; i < street.Points.Length; i++)
					{
						Vector2 a = street.Points[i - 1], ab = street.Points[i] - a;
						float t = Mathf.Clamp01(Vector2.Dot(door - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
						float d = Vector2.Distance(a + ab * t, door) - street.Width * 0.5f;
						if (d < nearest)
						{
							nearest = d;
							square = Vector2.Dot(front, ab.normalized);
						}
					}
				}
				Assert.Fail($"the house at {piece.Offset} (yaw {piece.Yaw:0.0}) faces square onto a street (or a plaza) across its set-back; nearest kerb {nearest:0.00} m, cos {square:0.00}");
			}
		}

		/// <summary>
		/// Every town plan lays its houses on an outlined, entered site: inside the outline, none overlapping another or
		/// standing on a street, each fronting its street, most of them laid, the streets inside the outline, and every road's
		/// end on a street.
		/// </summary>
		[TestCase(PointOfInterestTownPlan.StreetVillage, 50f, 18)]
		[TestCase(PointOfInterestTownPlan.GreenVillage, 50f, 18)]
		[TestCase(PointOfInterestTownPlan.MarketTown, 85f, 36)]
		[TestCase(PointOfInterestTownPlan.RadialCity, 130f, 70)]
		[TestCase(PointOfInterestTownPlan.Port, 65f, 24)]
		[TestCase(PointOfInterestTownPlan.Bailey, 55f, 8)]
		[TestCase(PointOfInterestTownPlan.Auto, 85f, 36)]
		public void EveryTownPlanLaysItsHousesOnItsStreets(PointOfInterestTownPlan plan, float radius, int houses)
		{
			bool walled = plan == PointOfInterestTownPlan.RadialCity || plan == PointOfInterestTownPlan.Bailey;
			var slots = new List<(PointOfInterestSlotRole, Vector2, float, int)>();
			if (walled)
			{
				slots.Add((PointOfInterestSlotRole.Perimeter, WallSize, 6f, 1));
				slots.Add((PointOfInterestSlotRole.Gate, GateSize, 12f, 2));
				slots.Add((PointOfInterestSlotRole.WallTower, TowerSize, 0f, 6));
			}
			if (plan == PointOfInterestTownPlan.Port)
			{
				slots.Add((PointOfInterestSlotRole.Pier, new Vector2(3f, 6f), 6f, 2));
			}
			slots.Add((PointOfInterestSlotRole.Centre, plan == PointOfInterestTownPlan.Bailey || plan == PointOfInterestTownPlan.RadialCity ? KeepSize : new Vector2(3f, 3f), 0f, 1));
			slots.Add((PointOfInterestSlotRole.Centre, new Vector2(3f, 2f), 0f, 4));
			slots.Add((PointOfInterestSlotRole.Street, HouseSize, 0f, houses));
			slots.Add((PointOfInterestSlotRole.Scatter, CrateSize, 0f, 10));
			PointOfInterestLayoutRequest request = Request(walled ? PointOfInterestLayout.Walled : PointOfInterestLayout.Street, radius, slots.ToArray());
			request.Plan = plan;
			request.Outline = Oval(radius, 0.72f, 55f);
			request.Yaw = 15f;
			request.Seed = 31;
			request.Entrances.AddRange(new[] { 70f, 230f });
			if (plan == PointOfInterestTownPlan.Port)
			{
				request.ShoreAhead = radius * 0.6f;
			}
			for (int i = 0; i < request.Entrances.Count; i++)
			{
				float reach = PointOfInterestOutline.ReachToward(request.Outline, radius, request.Entrances[i] + request.Yaw);
				request.EntranceDepths.Add(reach * SiteEntrances.EntryDepth);
			}
			List<PointOfInterestLayoutPiece> pieces = PointOfInterestLayouts.Place(request, new DeterministicRNG(17));
			string name = $"{plan} ({request.ChosenPlan})";
			AssertPacked(name, request, pieces);
			Assert.That(request.Streets, Is.Not.Empty, $"{name}: streets laid");
			// The streets inside the outline.
			foreach (PointOfInterestLayoutStreet street in request.Streets)
			{
				foreach (Vector2 p in street.Points)
				{
					Assert.That(p.magnitude, Is.LessThanOrEqualTo(Boundary(request, p) + 0.01f), $"{name}: a street point at {p} stays inside");
				}
			}
			int houseSlot = slots.FindIndex(x => x.Item1 == PointOfInterestSlotRole.Street);
			List<PointOfInterestLayoutPiece> laid = pieces.FindAll(p => p.Slot == houseSlot);
			Assert.That(laid.Count, Is.GreaterThanOrEqualTo((int)(houses * 0.6f)), $"{name}: {laid.Count} of {houses} houses found room");
			var four = new Vector2[4];
			foreach (PointOfInterestLayoutPiece house in laid)
			{
				// A bailey's buildings stand backed onto the curtain, facing the ward; everywhere else a house fronts its street.
				if (request.ChosenPlan != PointOfInterestTownPlan.Bailey)
				{
					AssertFronts(request, house);
				}
				// No street's line runs within half its width of the house's walls.
				foreach (PointOfInterestLayoutStreet street in request.Streets)
				{
					for (int i = 1; i < street.Points.Length; i++)
					{
						for (float t = 0f; t <= 1f; t += 0.25f)
						{
							Vector2 p = Vector2.Lerp(street.Points[i - 1], street.Points[i], t);
							Vector2 local = PointOfInterestLayouts.Rotate(p - house.Offset, -house.Yaw);
							float dx = Mathf.Max(0f, Mathf.Abs(local.x) - house.Footprint.x * 0.5f), dz = Mathf.Max(0f, Mathf.Abs(local.y) - house.Footprint.y * 0.5f);
							Assert.That(Mathf.Sqrt(dx * dx + dz * dz), Is.GreaterThanOrEqualTo(street.Width * 0.5f - 0.05f), $"{name}: the house at {house.Offset} stands off the street at {p}");
						}
					}
				}
			}
			// Each road's end inside lies on a street (the street in from its entrance passes through it).
			for (int i = 0; i < request.Entrances.Count; i++)
			{
				float e = request.Entrances[i] * Mathf.Deg2Rad;
				Vector2 end = new Vector2(Mathf.Sin(e), Mathf.Cos(e)) * request.EntranceDepths[i];
				float nearest = float.PositiveInfinity;
				foreach (PointOfInterestLayoutStreet street in request.Streets)
				{
					for (int k = 1; k < street.Points.Length; k++)
					{
						Vector2 a = street.Points[k - 1], ab = street.Points[k] - a;
						float t = Mathf.Clamp01(Vector2.Dot(end - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
						nearest = Mathf.Min(nearest, Vector2.Distance(a + ab * t, end));
					}
				}
				Assert.That(nearest, Is.LessThan(1f), $"{name}: the road from {request.Entrances[i]}° ends on a street");
			}
		}

		/// <summary>The streets are a function of the site's plan seed: another seed lays another town, the same seed the same.</summary>
		[Test]
		public void TheTownPlanFollowsItsSeed()
		{
			PointOfInterestLayoutRequest Make(int seed)
			{
				PointOfInterestLayoutRequest request = Request(PointOfInterestLayout.Street, 80f, (PointOfInterestSlotRole.Centre, new Vector2(3f, 3f), 0f, 1),
					(PointOfInterestSlotRole.Street, HouseSize, 0f, 30));
				request.Outline = Oval(80f, 0.8f, 10f);
				request.Seed = seed;
				request.Plan = PointOfInterestTownPlan.MarketTown;
				return request;
			}
			PointOfInterestLayoutRequest a = Make(5), b = Make(5), c = Make(6);
			PointOfInterestLayouts.Place(a, new DeterministicRNG(1));
			PointOfInterestLayouts.Place(b, new DeterministicRNG(1));
			PointOfInterestLayouts.Place(c, new DeterministicRNG(1));
			Assert.That(b.Streets.Count, Is.EqualTo(a.Streets.Count));
			for (int i = 0; i < a.Streets.Count; i++)
			{
				Assert.That(b.Streets[i].Points, Is.EqualTo(a.Streets[i].Points));
			}
			bool differs = c.Streets.Count != a.Streets.Count;
			for (int i = 0; !differs && i < a.Streets.Count; i++)
			{
				differs = c.Streets[i].Points.Length != a.Streets[i].Points.Length || c.Streets[i].Points[c.Streets[i].Points.Length / 2] != a.Streets[i].Points[a.Streets[i].Points.Length / 2];
			}
			Assert.That(differs, Is.True, "another seed, another town");
		}

		/// <summary>A terraced site's pieces keep off its banks: nothing straddles a step between two terraces.</summary>
		[Test]
		public void NothingIsBuiltAcrossATerracesBank()
		{
			PointOfInterestLayoutRequest request = Request(PointOfInterestLayout.Street, 85f, (PointOfInterestSlotRole.Centre, new Vector2(3f, 3f), 0f, 1),
				(PointOfInterestSlotRole.Street, HouseSize, 0f, 36), (PointOfInterestSlotRole.Scatter, CrateSize, 0f, 20));
			var pad = new PointOfInterestPad
			{
				Id = 1,
				Centre = new Vector3(0f, 50f, 0f),
				Radius = 85f,
				TerraceAxis = Vector2.up,
				TerraceSteps = new[] { -22f, 24f },
				TerraceLevels = new[] { 45f, 49f, 53f },
				TerraceRamps = new[] { 20f, -20f },
				RampHalfWidth = 4f,
			};
			request.Pad = pad;
			request.Seed = 3;
			List<PointOfInterestLayoutPiece> pieces = PointOfInterestLayouts.Place(request, new DeterministicRNG(4));
			Assert.That(request.ChosenPlan, Is.EqualTo(PointOfInterestTownPlan.HillTown));
			AssertPacked("hill town", request, pieces);
			var four = new Vector2[4];
			int houses = 0;
			foreach (PointOfInterestLayoutPiece piece in pieces)
			{
				houses += piece.Role == PointOfInterestSlotRole.Street ? 1 : 0;
				PointOfInterestLayouts.Corners(piece.Offset, piece.Yaw, piece.Footprint, four);
				foreach (Vector2 c in four)
				{
					Assert.That(PointOfInterestTerraces.OnSlope(pad, c.x, c.y, 0f), Is.False, $"a {piece.Role} corner at {c} is on a bank");
				}
			}
			Assert.That(houses, Is.GreaterThanOrEqualTo(20), "the terraces hold the town");
		}

		[Test]
		public void ABridgeSpansTheWholeCrossing()
		{
			PointOfInterestLayoutRequest request = Request(PointOfInterestLayout.Linear, 10f, (PointOfInterestSlotRole.Span, SpanSize, 4f, 1));
			request.SpanHalf = 13f;
			List<PointOfInterestLayoutPiece> spans = PointOfInterestLayouts.Place(request, new DeterministicRNG(1));
			// 26 m in modules of 4: six, each fitted to 4.33 m, so the deck ends exactly at the crossing's ends (2026-10-11:
			// whole modules overshot by most of one, over the path).
			Assert.That(spans.Count, Is.EqualTo(Mathf.RoundToInt(26f / 4f)));
			float module = 26f / spans.Count;
			spans.Sort((p, q) => p.Offset.y.CompareTo(q.Offset.y));
			Assert.That(spans[0].Offset.y - module * 0.5f, Is.EqualTo(-13f).Within(1e-3f));
			Assert.That(spans[spans.Count - 1].Offset.y + module * 0.5f, Is.EqualTo(13f).Within(1e-3f));
			for (int i = 0; i < spans.Count; i++)
			{
				Assert.That(spans[i].Offset.x, Is.EqualTo(0f), "on the heading, across the river");
				Assert.That(spans[i].Yaw, Is.EqualTo(0f), "a span runs along its z, as the heading does");
				Assert.That(spans[i].Footprint.y, Is.EqualTo(module).Within(1e-4f), "fitted to its share");
				if (i > 0)
				{
					Assert.That(spans[i].Offset.y - spans[i - 1].Offset.y, Is.EqualTo(module).Within(1e-4f), "end to end");
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
			Assert.That(PropsFeature.FinishFor(PointOfInterestFinish.Auto, 1f, POIType.SunkenShip, BiomeArtSpec.WeatheringByName("Seabed"), 0f, 0.99f), Is.EqualTo(StructureFinish.Algae), "under water");
			Assert.That(PropsFeature.FinishFor(PointOfInterestFinish.Auto, 1f, POIType.Ruins, BiomeArtSpec.WeatheringByName("Desert"), 1f, 0f), Is.EqualTo(StructureFinish.None), "no moss in a desert");
			Assert.That(PropsFeature.FinishFor(PointOfInterestFinish.Auto, 1f, POIType.Ruins, BiomeArtSpec.WeatheringByName("Forest"), 0.8f, 0.5f), Is.EqualTo(StructureFinish.Mossy), "old stone in a forest");
			Assert.That(PropsFeature.FinishFor(PointOfInterestFinish.Auto, 1f, POIType.Village, BiomeArtSpec.WeatheringByName("Forest"), 0f, 0.5f), Is.EqualTo(StructureFinish.None), "a lived-in village is kept");
			Assert.That(PropsFeature.FinishFor(PointOfInterestFinish.Auto, 1f, POIType.Village, BiomeArtSpec.WeatheringByName("Swamp"), 0f, 0.1f), Is.EqualTo(StructureFinish.Algae), "a swamp's damp");
			Assert.That(PropsFeature.FinishFor(PointOfInterestFinish.Auto, 1f, POIType.Battlefield, BiomeArtSpec.WeatheringByName("Plains"), 0f, 0.2f), Is.EqualTo(StructureFinish.Charred), "a battlefield burned");
			Assert.That(PropsFeature.FinishFor(PointOfInterestFinish.Charred, 0.5f, POIType.Ruins, BiomeArtSpec.WeatheringByName("Plains"), 0f, 0.7f), Is.EqualTo(StructureFinish.None), "a forced finish on its share");
			Assert.That(PropsFeature.FinishFor(PointOfInterestFinish.Charred, 0.5f, POIType.Ruins, BiomeArtSpec.WeatheringByName("Plains"), 0f, 0.3f), Is.EqualTo(StructureFinish.Charred));
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
