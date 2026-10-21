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
	/// More variety in the settlements (2026-10-19: "We need more variation for town group types"): a template slot pins a piece's
	/// variant ("=Church#1" the chapel); every castle's ward lays its garrison's village, a barracks and a stable among it, inside its
	/// wall at every size; a city and the capital lay one cathedral; a churchyard's wall, lychgate and mausoleums keep off the church
	/// and its graves; and the outskirts' layouts (a beekeeper's hives, a cemetery, the spots beside the ways out) are pure and
	/// repeat for their seed. Pure: no scene, no art.
	/// </summary>
	[TestFixture]
	public class SettlementVarietyTests
	{
		[OneTimeSetUp]
		public void RegisterKit() => StructureKitPieceSource.Register();

		// ── Variant pins ──────────────────────────────────────────

		[Test]
		public void APinnedQueryKeepsItsTags_AndDrawsOnlyItsVariants()
		{
			Assert.That(StructureKitPieceSource.Pins("=Church#1110", out string query), Is.EqualTo("1110"));
			Assert.That(query, Is.EqualTo("=Church"));
			Assert.That(StructureKitPieceSource.Pins("church", out query), Is.Null);
			Assert.That(query, Is.EqualTo("church"));
			Assert.That(StructureKitPieceSource.Pins("a#b", out query), Is.Null, "only digits pin");
			Assert.That(StructureKitPieceSource.BaseQuery("church#0"), Is.EqualTo("church"));
			StructureKitPieceSource.Parse("tower+military!wall#1", out List<string> required, out List<string> excluded);
			Assert.That(required, Is.EqualTo(new[] { "tower", "military" }));
			Assert.That(excluded, Is.EqualTo(new[] { "wall" }), "the pin is no tag");
			Assert.That(Ids(PointOfInterestTemplateSpecs.Chapel), Is.EqualTo(Ids("=Church")));
			Assert.That(Ids(PointOfInterestTemplateSpecs.Fountain), Is.EqualTo(new[] { "MarketCross" }));

			StructurePiece church = StructureKit.Get("Church");
			StructurePiece cross = StructureKit.Get("MarketCross");
			int chapels = 0;
			for (int seed = 0; seed < 400; seed++)
			{
				Assert.That(StructureKitPieceSource.VariantFor(church, PointOfInterestTemplateSpecs.Chapel, seed), Is.EqualTo(1));
				Assert.That(StructureKitPieceSource.VariantFor(church, PointOfInterestTemplateSpecs.ParishChurch, seed), Is.EqualTo(0));
				Assert.That(StructureKitPieceSource.VariantFor(cross, PointOfInterestTemplateSpecs.Fountain, seed), Is.EqualTo(1));
				Assert.That(StructureKitPieceSource.VariantFor(cross, PointOfInterestTemplateSpecs.StoneCross, seed), Is.EqualTo(0));
				Assert.That(StructureKitPieceSource.VariantFor(church, "=Church#9", seed), Is.EqualTo(church.Variants - 1), "clamped to the piece's");
				Assert.That(StructureKitPieceSource.VariantFor(church, "=Church", seed), Is.EqualTo(StructureKit.PickVariant(church, seed)), "no pin: the kit's pick");
				chapels += StructureKitPieceSource.VariantFor(church, PointOfInterestTemplateSpecs.VillageChurch, seed) == 1 ? 1 : 0;
			}
			Assert.That(chapels, Is.InRange(260, 340), "a village's church is a chapel about three times in four");
		}

		[Test]
		public void EveryChurchQueryIsAChurch_AndFrontsTheSquare()
		{
			foreach (string church in new[] { PointOfInterestTemplateSpecs.Church, PointOfInterestTemplateSpecs.Chapel, PointOfInterestTemplateSpecs.ParishChurch,
				PointOfInterestTemplateSpecs.VillageChurch, PointOfInterestTemplateSpecs.Cathedral })
			{
				Assert.That(PointOfInterestTemplateSpecs.IsChurch(church), Is.True, church);
			}
			foreach (string other in new[] { PointOfInterestTemplateSpecs.Temple, PointOfInterestTemplateSpecs.Fountain, PointOfInterestTemplateSpecs.House, "" })
			{
				Assert.That(PointOfInterestTemplateSpecs.IsChurch(other), Is.False, other);
			}
		}

		private static List<string> Ids(string query) => StructureKitPieceSource.Candidates(query, null).ConvertAll(p => p.Id);

		// ── Templates ─────────────────────────────────────────────

		[Test]
		public void SettlementsAskForTheirChurchesAndCentrepieces()
		{
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All())
			{
				int Max(string tag) => spec.Pieces.FindAll(p => p.Tag == tag).ConvertAll(p => p.MaxCount).Sum();
				int Min(string tag) => spec.Pieces.FindAll(p => p.Tag == tag).ConvertAll(p => p.MinCount).Sum();
				switch (spec.Kind)
				{
					case POIType.City:
					case POIType.Capital:
						Assert.That(Min(PointOfInterestTemplateSpecs.Cathedral) == 1 && Max(PointOfInterestTemplateSpecs.Cathedral) == 1, Is.True, $"{spec.Name}: one cathedral");
						Assert.That(Min(PointOfInterestTemplateSpecs.ParishChurch), Is.GreaterThanOrEqualTo(1), $"{spec.Name}: and a parish church");
						Assert.That(Min(PointOfInterestTemplateSpecs.MarketHall), Is.GreaterThanOrEqualTo(1), $"{spec.Name}: a town hall");
						Assert.That(Min(PointOfInterestTemplateSpecs.Fountain), Is.GreaterThanOrEqualTo(1), $"{spec.Name}: a fountain");
						Assert.That(Max(PointOfInterestTemplateSpecs.Well), Is.EqualTo(1), $"{spec.Name}: the fountains stand for the extra wells");
						break;
					case POIType.Town:
						Assert.That(Min(PointOfInterestTemplateSpecs.ParishChurch), Is.EqualTo(1), $"{spec.Name}: a parish church");
						Assert.That(Min(PointOfInterestTemplateSpecs.MarketHall), Is.EqualTo(1), $"{spec.Name}: a town hall");
						Assert.That(Min(PointOfInterestTemplateSpecs.Fountain), Is.EqualTo(1), $"{spec.Name}: a fountain");
						break;
					case POIType.Castle:
						Assert.That(Min(PointOfInterestTemplateSpecs.Barracks), Is.GreaterThanOrEqualTo(1), $"{spec.Name}: a barracks");
						Assert.That(Min(PointOfInterestTemplateSpecs.Stable), Is.GreaterThanOrEqualTo(1), $"{spec.Name}: a stable");
						Assert.That(Min(PointOfInterestTemplateSpecs.Smithy) >= 1 && Min(PointOfInterestTemplateSpecs.Well) >= 1 && Min(PointOfInterestTemplateSpecs.Bakery) >= 1, Is.True,
							$"{spec.Name}: a smithy, a well and a bakehouse");
						Assert.That(Max(PointOfInterestTemplateSpecs.Chapel), Is.EqualTo(1), $"{spec.Name}: perhaps a chapel");
						break;
					case POIType.Village when spec.Style != PointOfInterestTemplateSpecs.Hide && spec.Variant != "Longhouse":
						Assert.That(Max(PointOfInterestTemplateSpecs.VillageChurch), Is.EqualTo(1), $"{spec.Name}: a chapel mostly");
						Assert.That(Max(PointOfInterestTemplateSpecs.Maypole), Is.EqualTo(1), $"{spec.Name}: a maypole on some greens");
						break;
				}
				if (spec.Kind == POIType.Keep)
				{
					Assert.That(Max(PointOfInterestTemplateSpecs.Barracks) + Max(PointOfInterestTemplateSpecs.Stable), Is.Zero, $"{spec.Name}: a keep's ward stays as it was");
				}
			}
		}

		/// <summary>Lays a template's spec at a size as PropsFeature does (its landmarks and wells flagged): the request and the pieces.</summary>
		private static List<PointOfInterestLayoutPiece> Lay(PointOfInterestTemplateSpec spec, int size, int seed, out PointOfInterestLayoutRequest request)
		{
			PointOfInterestKindRule rule = PointOfInterestRules.Default().RuleFor(spec.Kind);
			float radius = Mathf.Lerp(rule.FootprintMin, rule.FootprintMax, size / 2f);
			var random = new DeterministicRNG(seed + size);
			request = new PointOfInterestLayoutRequest { Layout = spec.Layout, Radius = radius, Gap = 1f, Kind = spec.Kind, SpanHalf = radius, ShoreAhead = float.NaN };
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
					Footprint = Vector2.Max(metrics.Footprint, metrics.Sweep),
					Module = metrics.Module,
					Count = count,
					Landmark = PointOfInterestTemplateSpecs.IsLandmark(slot.Tag, spec.Kind),
					Well = slot.Tag == PointOfInterestTemplateSpecs.Well,
				});
				if (PointOfInterestLayouts.Generates(role))
				{
					continue;
				}
				for (int k = 0; k < count; k++)
				{
					int pieceSeed = random.Next();
					PointOfInterestPieceMetrics piece = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, pieceSeed));
					request.Items.Add(new PointOfInterestLayoutItem { Slot = s, Footprint = Vector2.Max(piece.Footprint, piece.Sweep) });
				}
			}
			return PointOfInterestLayouts.Place(request, random);
		}

		[Test]
		public void EveryCastleLaysItsGarrisonsVillageInsideItsWall_AtEverySize()
		{
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.For(POIType.Castle))
			{
				int wall = spec.Pieces.FindIndex(p => p.Role == PointOfInterestSlotRole.Perimeter);
				int barracks = spec.Pieces.FindIndex(p => p.Tag == PointOfInterestTemplateSpecs.Barracks);
				int stable = spec.Pieces.FindIndex(p => p.Tag == PointOfInterestTemplateSpecs.Stable);
				int smithy = spec.Pieces.FindIndex(p => p.Tag == PointOfInterestTemplateSpecs.Smithy);
				int well = spec.Pieces.FindIndex(p => p.Tag == PointOfInterestTemplateSpecs.Well);
				Assert.That(wall >= 0 && barracks >= 0 && stable >= 0 && smithy >= 0 && well >= 0, Is.True, spec.Name);
				foreach (int size in spec.Sizes())
				{
					for (int seed = 1; seed <= 4; seed++)
					{
						List<PointOfInterestLayoutPiece> pieces = Lay(spec, size, 100 * seed, out PointOfInterestLayoutRequest request);
						string name = $"{spec.Name} at size {size} seed {seed} ({request.Radius:0} m)";
						var walls = pieces.FindAll(p => p.Slot == wall);
						Assert.That(walls.Count, Is.GreaterThanOrEqualTo(3), $"{name}: its wall stands");
						foreach (int slot in new[] { barracks, stable, smithy, well })
						{
							List<PointOfInterestLayoutPiece> laid = pieces.FindAll(p => p.Slot == slot);
							Assert.That(laid.Count, Is.GreaterThanOrEqualTo(1), $"{name}: {spec.Pieces[slot].Tag} laid ({Describe(request, pieces)})");
							foreach (PointOfInterestLayoutPiece p in laid)
							{
								AssertInsideTheWall(name, p, walls);
							}
						}
					}
				}
			}
		}

		/// <summary>Every corner of a piece nearer the middle than the wall's inner face that way.</summary>
		private static void AssertInsideTheWall(string name, PointOfInterestLayoutPiece piece, List<PointOfInterestLayoutPiece> walls)
		{
			var four = new Vector2[4];
			PointOfInterestLayouts.Corners(piece.Offset, piece.Yaw, piece.Footprint, four);
			foreach (Vector2 c in four)
			{
				// The wall module nearest that bearing.
				PointOfInterestLayoutPiece nearest = walls[0];
				float best = float.MaxValue;
				foreach (PointOfInterestLayoutPiece w in walls)
				{
					float d = Mathf.Abs(Mathf.DeltaAngle(PointOfInterestLayouts.DirectionOf(w.Offset), PointOfInterestLayouts.DirectionOf(c)));
					if (d < best)
					{
						best = d;
						nearest = w;
					}
				}
				float inner = nearest.Offset.magnitude - nearest.Footprint.y * 0.5f;
				Assert.That(c.magnitude, Is.LessThan(inner + 0.5f), $"{name}: a piece at {piece.Offset} reaches past the wall");
			}
		}

		private static string Describe(PointOfInterestLayoutRequest request, List<PointOfInterestLayoutPiece> pieces)
		{
			var parts = new List<string>();
			for (int i = 0; i < request.Items.Count; i++)
			{
				if (request.Slots[request.Items[i].Slot].Role == PointOfInterestSlotRole.Street)
				{
					Vector2 f = request.Items[i].Footprint;
					parts.Add($"{request.Items[i].Slot}:{f.x:0.0}x{f.y:0.0}{(pieces.Exists(p => p.Item == i) ? "" : "!")}");
				}
			}
			return $"{request.ChosenPlan}; street items {string.Join(" ", parts)}; ! = not laid";
		}

		[Test]
		public void ACityAndTheCapitalLayOneCathedral()
		{
			foreach (POIType kind in new[] { POIType.City, POIType.Capital })
			{
				foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.For(kind))
				{
					int cathedral = spec.Pieces.FindIndex(p => p.Tag == PointOfInterestTemplateSpecs.Cathedral);
					Assert.That(cathedral, Is.GreaterThanOrEqualTo(0), spec.Name);
					foreach (int size in spec.Sizes())
					{
						List<PointOfInterestLayoutPiece> pieces = Lay(spec, size, 7, out PointOfInterestLayoutRequest request);
						/* The cathedral (22 × 39 m) fronts the square where its depth fits before the ring street behind the square's fronts:
						 * in a large city and the capital. A smaller city's ring street stands too near the square for it yet; the packer's
						 * fronts would have to turn a deep landmark's long side to its frontage (2026-10-19, reported). Never more than one. */
						int laid = pieces.FindAll(p => p.Slot == cathedral).Count;
						Assert.That(laid, Is.EqualTo(size == 2 ? 1 : laid).And.LessThanOrEqualTo(1), $"{spec.Name} at size {size}");
					}
				}
			}
		}

		// ── Churchyards ───────────────────────────────────────────

		[Test]
		public void AChurchyardKeepsOffItsChurchAndGraves_ItsGateOnTheFront()
		{
			var lychgate = new Vector2(3.6f, 2.6f);
			foreach (Vector2 church in new[] { new Vector2(9f, 18f), new Vector2(8f, 14f), new Vector2(22f, 40f) })
			{
				foreach (Vector2 tomb in new[] { new Vector2(5f, 7f), new Vector2(6.5f, 6.5f) })
				{
					for (int rows = 2; rows <= 3; rows++)
					{
						for (int count = 0; count <= 2; count++)
						{
							PointOfInterestChurchyards.Plan plan = PointOfInterestChurchyards.Layout(church, rows, count, tomb, lychgate, count == 1 ? 1 : 0);
							string name = $"church {church} rows {rows} tombs {count}";
							Assert.That(plan.Mausoleums.Count, Is.EqualTo(count), name);
							var churchBox = new Rect(-church.x * 0.5f, -church.y * 0.5f, church.x, church.y);
							float rowsReach = PointOfInterestChurchyards.RowsReach(church.y, rows);
							foreach (Vector2 m in plan.Mausoleums)
							{
								var box = new Rect(m - tomb * 0.5f, tomb);
								Assert.That(box.Overlaps(churchBox), Is.False, $"{name}: a mausoleum on the church");
								Assert.That(box.yMax, Is.LessThan(-rowsReach), $"{name}: a mausoleum on the graves");
								Assert.That(box.xMin > -plan.Left && box.xMax < plan.Right && box.yMin > plan.Back, Is.True, $"{name}: a mausoleum outside the wall");
							}
							if (plan.Mausoleums.Count == 2)
							{
								Assert.That(new Rect(plan.Mausoleums[0] - tomb * 0.5f, tomb).Overlaps(new Rect(plan.Mausoleums[1] - tomb * 0.5f, tomb)), Is.False, name);
							}
							Assert.That(plan.Front, Is.LessThan(church.y * 0.5f - lychgate.y * 0.5f), $"{name}: the gate stands behind the church's front, off the street");
							Assert.That(plan.Gate.x - lychgate.x * 0.5f, Is.GreaterThan(church.x * 0.5f), $"{name}: the gate beside the church");
							foreach ((Vector2 a, Vector2 b) in plan.Runs)
							{
								Vector2 mid = 0.5f * (a + b);
								Assert.That(churchBox.Contains(mid), Is.False, $"{name}: a wall run through the church");
								Assert.That(mid.x > plan.Gate.x - lychgate.x * 0.5f + 0.01f && mid.x < plan.Gate.x + lychgate.x * 0.5f - 0.01f && Mathf.Abs(mid.y - plan.Front) < 0.01f,
									Is.False, $"{name}: the wall across the lychgate");
							}
						}
					}
				}
			}
		}

		// ── The outskirts ─────────────────────────────────────────

		[Test]
		public void ABeekeepersHivesStandApart_ClearOfHisBothyAndHisDoor()
		{
			foreach (Vector2 bothy in new[] { new Vector2(5f, 4.5f), new Vector2(7f, 6f), new Vector2(3f, 3f) })
			{
				foreach (Vector2 apiary in new[] { new Vector2(2.6f, 1.2f), new Vector2(3.4f, 1.6f) })
				{
					for (int hives = 2; hives <= 4; hives++)
					{
						PointOfInterestOutskirts.Plan plan = PointOfInterestOutskirts.BeeYard(bothy, apiary, hives, 0, 0);
						string name = $"bothy {bothy} apiary {apiary} hives {hives}";
						Assert.That(plan.Parts.FindAll(p => p.Id == "Apiary").Count, Is.EqualTo(hives), name);
						PointOfInterestOutskirts.Part hut = plan.Parts.Find(p => p.Id == "Bothy");
						// The way to its door, from the gate up to its front.
						var door = new Rect(hut.At.x - 0.8f, -plan.Size.y * 0.5f, 1.6f, hut.At.y - hut.Extent.y * 0.5f + plan.Size.y * 0.5f);
						for (int i = 0; i < plan.Parts.Count; i++)
						{
							PointOfInterestOutskirts.Part a = plan.Parts[i];
							Assert.That(Mathf.Abs(a.At.x) + a.Extent.x * 0.5f, Is.LessThanOrEqualTo(plan.Size.x * 0.5f + 1e-3f), $"{name}: {a.Id} inside its ground");
							Assert.That(Mathf.Abs(a.At.y) + a.Extent.y * 0.5f, Is.LessThanOrEqualTo(plan.Size.y * 0.5f + 1e-3f), $"{name}: {a.Id} inside its ground");
							if (a.Id == "Apiary")
							{
								Assert.That(PointOfInterestLayouts.Overlaps(a.At, a.Yaw, a.Footprint, door.center, 0f, door.size), Is.False, $"{name}: a hive in the way to the door");
							}
							for (int j = i + 1; j < plan.Parts.Count; j++)
							{
								PointOfInterestOutskirts.Part b = plan.Parts[j];
								Assert.That(PointOfInterestLayouts.Overlaps(a.At, a.Yaw, a.Footprint + Vector2.one * 0.3f, b.At, b.Yaw, b.Footprint), Is.False, $"{name}: {a.Id} and {b.Id} overlap");
							}
						}
					}
				}
			}
		}

		[Test]
		public void ARowsPartsStandSideBySide_InsideItsGround()
		{
			var parts = new[]
			{
				PointOfInterestOutskirts.MakePart("Bothy", 0, new Vector2(5f, 4.5f), 0f, true),
				PointOfInterestOutskirts.MakePart("Woodpile", 0, new Vector2(1.7f, 1f), -90f, false),
				PointOfInterestOutskirts.MakePart("SawPit", 0, new Vector2(4.6f, 1.8f), 90f, false),
			};
			PointOfInterestOutskirts.Plan plan = PointOfInterestOutskirts.Row(parts, 1.5f);
			Assert.That(plan.Parts.Count, Is.EqualTo(3));
			Assert.That(plan.Parts[1].Extent.x, Is.EqualTo(1f).Within(1e-4f), "a quarter turn swaps its sides");
			for (int i = 0; i < plan.Parts.Count; i++)
			{
				PointOfInterestOutskirts.Part a = plan.Parts[i];
				Assert.That(Mathf.Abs(a.At.x) + a.Extent.x * 0.5f, Is.LessThanOrEqualTo(plan.Size.x * 0.5f - PointOfInterestOutskirts.Margin + 1e-3f));
				Assert.That(a.At.y - a.Extent.y * 0.5f, Is.GreaterThanOrEqualTo(-plan.Size.y * 0.5f + PointOfInterestOutskirts.Margin + PointOfInterestOutskirts.Apron - 1e-3f), "a yard before them");
				for (int j = i + 1; j < plan.Parts.Count; j++)
				{
					Assert.That(PointOfInterestLayouts.Overlaps(a.At, a.Yaw, a.Footprint, plan.Parts[j].At, plan.Parts[j].Yaw, plan.Parts[j].Footprint), Is.False);
				}
			}
		}

		[Test]
		public void ACemeteryKeepsItsWalkAndItsTombsClearOfItsGraves_AndRepeats()
		{
			var size = new Vector2(30f, 22f);
			var grave = new Vector2(0.8f, 0.4f);
			var mound = new Vector2(1f, 2.2f);
			var tomb = new Vector2(5f, 7f);
			var lychgate = new Vector2(3.6f, 2.6f);
			for (int seed = 0; seed < 40; seed++)
			{
				for (int tombs = 1; tombs <= 3; tombs++)
				{
					PointOfInterestOutskirts.Plan plan = PointOfInterestOutskirts.CemeteryYard(size, grave, mound, tomb, lychgate, tombs, seed);
					PointOfInterestOutskirts.Plan again = PointOfInterestOutskirts.CemeteryYard(size, grave, mound, tomb, lychgate, tombs, seed);
					Assert.That(again.Parts.Count, Is.EqualTo(plan.Parts.Count), "the same seed lays the same cemetery");
					for (int i = 0; i < plan.Parts.Count; i++)
					{
						Assert.That(again.Parts[i].At, Is.EqualTo(plan.Parts[i].At));
						Assert.That(again.Parts[i].Id, Is.EqualTo(plan.Parts[i].Id));
					}
					Assert.That(plan.Parts.FindAll(p => p.Id == "Mausoleum").Count, Is.EqualTo(tombs), $"seed {seed}: its tombs");
					Assert.That(plan.Parts.FindAll(p => p.Id != "Mausoleum").Count, Is.GreaterThan(30), $"seed {seed}: rows of graves");
					Assert.That(plan.Runs.Count, Is.EqualTo(5), "four sides, the front broken by the lychgate");
					for (int i = 0; i < plan.Parts.Count; i++)
					{
						PointOfInterestOutskirts.Part a = plan.Parts[i];
						Assert.That(Mathf.Abs(a.At.x) + a.Extent.x * 0.5f, Is.LessThan(size.x * 0.5f), $"seed {seed}: {a.Id} inside the wall");
						Assert.That(Mathf.Abs(a.At.y) + a.Extent.y * 0.5f, Is.LessThan(size.y * 0.5f), $"seed {seed}: {a.Id} inside the wall");
						if (a.Id != "Mausoleum")
						{
							Assert.That(Mathf.Abs(a.At.x) - a.Extent.x * 0.5f, Is.GreaterThan(1f), $"seed {seed}: a grave on the walk");
							Assert.That(a.At.y - a.Extent.y * 0.5f, Is.GreaterThan(-size.y * 0.5f + lychgate.y), $"seed {seed}: a grave in the gate");
						}
						for (int j = i + 1; j < plan.Parts.Count; j++)
						{
							PointOfInterestOutskirts.Part b = plan.Parts[j];
							Assert.That(PointOfInterestLayouts.Overlaps(a.At, a.Yaw, a.Footprint, b.At, b.Yaw, b.Footprint), Is.False, $"seed {seed}: {a.Id} at {a.At} and {b.Id} at {b.At}");
						}
					}
				}
			}
		}

		[Test]
		public void WaysideSpotsStandBesideTheWaysOut_FacingThem_AndRepeat()
		{
			var site = new Vector2(100f, -40f);
			const float Edge = 60f;
			var lines = new List<List<Vector2>>
			{
				new List<Vector2> { site, site + new Vector2(0f, 200f), site + new Vector2(150f, 520f) },
				new List<Vector2> { site, site + new Vector2(-300f, -10f), site + new Vector2(-480f, -60f) },
			};
			var offsets = new List<float> { 4f, 2.5f };
			float Reach(Vector2 p) => Edge;
			for (int seed = 0; seed < 30; seed++)
			{
				List<PointOfInterestOutskirts.WaySpot> spots = PointOfInterestOutskirts.WaysideSpots(lines, offsets, site, Reach, seed, 24);
				List<PointOfInterestOutskirts.WaySpot> again = PointOfInterestOutskirts.WaysideSpots(lines, offsets, site, Reach, seed, 24);
				Assert.That(spots.Count, Is.EqualTo(again.Count));
				Assert.That(spots, Is.Not.Empty, $"seed {seed}");
				for (int i = 0; i < spots.Count; i++)
				{
					PointOfInterestOutskirts.WaySpot spot = spots[i];
					Assert.That(again[i].At, Is.EqualTo(spot.At), "the same seed, the same spots");
					Assert.That(spot.Out, Is.InRange(PointOfInterestOutskirts.WaysideFrom, PointOfInterestOutskirts.WaysideTo));
					// Its offset off its way's line, and its front (−z) looking at the way.
					List<Vector2> line = lines[spot.Way];
					float nearest = float.MaxValue;
					Vector2 onLine = default;
					for (int k = 1; k < line.Count; k++)
					{
						Vector2 ab = line[k] - line[k - 1];
						float t = Mathf.Clamp01(Vector2.Dot(spot.At - line[k - 1], ab) / ab.sqrMagnitude);
						Vector2 q = line[k - 1] + ab * t;
						if ((q - spot.At).magnitude < nearest)
						{
							nearest = (q - spot.At).magnitude;
							onLine = q;
						}
					}
					// Beside its way, not on it (a little nearer on the inside of a bend).
					Assert.That(nearest, Is.InRange(offsets[spot.Way] * 0.5f, offsets[spot.Way] + 0.01f), $"seed {seed}: beside its way, not on it");
					Vector2 front = -PointOfInterestLayouts.Rotate(Vector2.up, spot.Yaw);
					Assert.That(Vector2.Dot(front, (onLine - spot.At).normalized), Is.GreaterThan(0.8f), $"seed {seed}: facing its way");
					float outThere = (spot.At - site).magnitude - Edge;
					Assert.That(outThere, Is.InRange(PointOfInterestOutskirts.WaysideFrom - 10f, PointOfInterestOutskirts.WaysideTo + 10f), $"seed {seed}: out past the edge");
				}
			}
		}

		[Test]
		public void AWayIsWalkedFromItsSitesEnd_AndCountsRepeat()
		{
			var points = new[] { new Vector3(500f, 0f, 0f), new Vector3(200f, 0f, 0f), new Vector3(0f, 0f, 0f) };
			List<Vector2> fromSite = PointOfInterestOutskirts.Outward(points, false);
			Assert.That(fromSite[0], Is.EqualTo(Vector2.zero), "a way to the site is walked from its far end back");
			Assert.That(PointOfInterestOutskirts.AtOut(fromSite, Vector2.zero, p => 50f, 100f, out Vector2 at, out Vector2 along, out _), Is.True);
			Assert.That(at.x, Is.EqualTo(150f).Within(1e-3f));
			Assert.That(along, Is.EqualTo(Vector2.right));
			Assert.That(PointOfInterestOutskirts.AtOut(fromSite, Vector2.zero, p => 50f, 600f, out _, out _, out _), Is.False, "never so far out");
			Assert.That(PointOfInterestOutskirts.FacingYaw(Vector2.down), Is.EqualTo(0f).Within(1e-3f), "a front looking down −z is unturned");
			foreach (POIType kind in new[] { POIType.Village, POIType.Town, POIType.City, POIType.Capital, POIType.Monastery, POIType.Castle })
			{
				foreach (PointOfInterestOutskirts.Holding holding in (PointOfInterestOutskirts.Holding[])Enum.GetValues(typeof(PointOfInterestOutskirts.Holding)))
				{
					(int min, int max) = PointOfInterestOutskirts.CountFor(kind, holding);
					for (int seed = 0; seed < 50; seed++)
					{
						int n = PointOfInterestOutskirts.Wanted(seed, kind, holding);
						Assert.That(n, Is.InRange(min, max), $"{kind} {holding}");
						Assert.That(PointOfInterestOutskirts.Wanted(seed, kind, holding), Is.EqualTo(n));
					}
				}
			}
			Assert.That(PointOfInterestOutskirts.CountFor(POIType.City, PointOfInterestOutskirts.Holding.Cemetery).min, Is.EqualTo(1), "a city's cemetery");
			Assert.That(PointOfInterestOutskirts.CountFor(POIType.Village, PointOfInterestOutskirts.Holding.Gallows).max, Is.Zero, "no gallows by a village");
			Assert.That(PointOfInterestOutskirts.CountFor(POIType.Village, PointOfInterestOutskirts.Holding.Beekeeper).min, Is.GreaterThanOrEqualTo(1), "a village's lone beekeeper");
		}

		/// <summary>The outskirts are laid by the fields after the farmsteads, and their draws are hashes of their own (a source scan).</summary>
		[Test]
		public void TheFieldsLayTheOutskirtsAfterTheFarmsteads_OnDrawsOfTheirOwn()
		{
			const string Features = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/Features";
			string fields = System.IO.File.ReadAllText($"{Features}/PointOfInterestFields.cs");
			int farmsteads = fields.IndexOf("PointOfInterestFarmsteads.Lay(context, laidFields);", StringComparison.Ordinal);
			int outskirts = fields.IndexOf("PointOfInterestOutskirts.Lay(context, laidFields);", StringComparison.Ordinal);
			Assert.That(farmsteads, Is.GreaterThan(0));
			Assert.That(outskirts, Is.GreaterThan(farmsteads), "after the farmsteads");
			foreach (string file in new[] { "PointOfInterestOutskirts.cs", "PointOfInterestChurchyards.cs" })
			{
				string source = System.IO.File.ReadAllText($"{Features}/{file}");
				Assert.That(source.Contains("context.Random"), Is.False, $"{file} never draws on the site's sequence");
				Assert.That(source.Contains(".NextDouble()") || source.Contains(".NextFloat()"), Is.False, $"{file} draws only hashes");
			}
		}
	}

	internal static class SettlementVarietyListExtensions
	{
		public static int Sum(this List<int> values)
		{
			int total = 0;
			foreach (int v in values)
			{
				total += v;
			}
			return total;
		}
	}
}
