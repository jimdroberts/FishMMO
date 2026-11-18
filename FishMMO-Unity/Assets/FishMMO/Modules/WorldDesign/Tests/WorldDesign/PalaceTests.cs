using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;
using Part = FishMMO.Shared.WorldDesign.PointOfInterestPalace.Part;
using PartKind = FishMMO.Shared.WorldDesign.PointOfInterestPalace.PartKind;
using Tier = FishMMO.Shared.WorldDesign.PointOfInterestPalace.Tier;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// A capital's palace (2026-10-19: "massive palace style castles with interiors (kings chair?) for capitals"): the compound's plan
	/// (PointOfInterestPalace.Plan: nothing overlaps, every door has its approach, the doors look into the court on the garden's walks,
	/// the gate on the front, all inside the footprint, the same plan for the same seed) and the capitals' layouts keeping its ground
	/// as their citadel at every size and stature.
	/// </summary>
	[TestFixture]
	public class PalaceTests
	{
		/// <summary>
		/// The kit's measurements as the plan tests take them (pure: measuring builds meshes), held to the kit's own by
		/// <see cref="TheKitMeasuresAsThePlanTestsTakeIt"/>.
		/// </summary>
		private static PointOfInterestPalace.Sizes Kit => new PointOfInterestPalace.Sizes
		{
			Hall = new Vector2(23.3f, 39.3f),
			Wing = new Vector2(29.7f, 13.6f),
			Garden = new Vector2(20f, 20f),
			Gate = new Vector2(12f, 11.43f),
			Tower = new Vector2(9.2f, 9.3f),
			Module = 6f,
			CurtainDepth = 2.1f,
			GateJoin = new[] { 5.85f, 5.85f },
			TowerJoin = new[] { 3.84f, 3.95f },
			Corner = new Vector2(8.2f, 8.2f),
			CornerJoin = new[] { 3.84f, 3.95f },
			CornerDoor = new[] { new Vector4(2.0f, 2.0f, 0.7071f, 0.7071f), new Vector4(2.2f, 3.15f, 0f, 1f) },
		};

		private static readonly int[] Seeds = { 1, 2, 3, 7, 19, 101, 4242, 99991 };

		/// <summary>How far before a door its approach is kept clear, and how wide (metres).</summary>
		private const float ApproachDepth = 2.5f, ApproachWidth = 2f;

		[SetUp]
		public void RegisterKit() => StructureKitPieceSource.Register();

		private static IEnumerable<(Tier tier, int seed, PointOfInterestPalace.Layout plan)> Plans()
		{
			for (int t = 0; t < PointOfInterestPalace.Tiers; t++)
			{
				foreach (int seed in Seeds)
				{
					yield return ((Tier)t, seed, PointOfInterestPalace.Plan(Kit, (Tier)t, seed));
				}
			}
		}

		private static bool IsStation(PartKind kind) => kind == PartKind.Tower || kind == PartKind.CornerTower || kind == PartKind.Gatehouse;

		/// <summary>
		/// How deep before a door its way out is kept clear, metres: a corner tower's from its door in the wall's inner face across the
		/// wall, its steps (1.4 m on the round one's diagonal, 1.6 m off the square one's face) and a stride beyond.
		/// </summary>
		private static float ApproachOf(PartKind kind) => kind == PartKind.CornerTower ? 4.5f : ApproachDepth;

		[Test]
		public void ThePlanHasNoOverlapsAndKeepsEveryDoorsApproachClear()
		{
			var problems = new List<string>();
			foreach ((Tier tier, int seed, PointOfInterestPalace.Layout plan) in Plans())
			{
				List<Part> parts = plan.Parts;
				for (int i = 0; i < parts.Count; i++)
				{
					for (int j = i + 1; j < parts.Count; j++)
					{
						Part a = parts[i], b = parts[j];
						// The curtain runs into its stations (to their "curtain" marks) and its modules meet end to end, by design.
						bool joint = (a.Kind == PartKind.Curtain && (IsStation(b.Kind) || b.Kind == PartKind.Curtain)) || (b.Kind == PartKind.Curtain && IsStation(a.Kind));
						if (!joint && PointOfInterestLayouts.Overlaps(a.At, a.Yaw, a.Size, b.At, b.Yaw, b.Size))
						{
							problems.Add($"{tier} seed {seed}: {a.Kind} at {a.At} overlaps {b.Kind} at {b.At}");
						}
					}
				}
				for (int i = 0; i < parts.Count; i++)
				{
					Part door = parts[i];
					if (door.Kind == PartKind.Curtain || door.Kind == PartKind.Garden)
					{
						continue;
					}
					// The gatehouse's passage: its ward side (its front is the town's).
					Vector2 at = door.Kind == PartKind.Gatehouse ? door.At + PointOfInterestLayouts.Rotate(new Vector2(0f, door.Size.y * 0.5f), door.Yaw) : door.Door;
					Vector2 way = door.Kind == PartKind.Gatehouse ? PointOfInterestLayouts.Rotate(Vector2.up, door.Yaw) : door.Out;
					float depth = ApproachOf(door.Kind);
					Vector2 centre = at + way * (depth * 0.5f + 0.05f);
					float yaw = PointOfInterestLayouts.FacingYaw(PointOfInterestLayouts.DirectionOf(way));
					for (int j = 0; j < parts.Count; j++)
					{
						Part other = parts[j];
						if (j != i && PointOfInterestLayouts.Overlaps(centre, yaw, new Vector2(ApproachWidth, depth), other.At, other.Yaw, other.Size))
						{
							problems.Add($"{tier} seed {seed}: the approach to {door.Kind} at {door.At} runs into {other.Kind} at {other.At}");
						}
					}
				}
			}
			Assert.That(problems, Is.Empty, string.Join("\n", problems));
		}

		[Test]
		public void TheDoorsLookIntoTheCourtOnTheGardensWalks()
		{
			foreach ((Tier tier, int seed, PointOfInterestPalace.Layout plan) in Plans())
			{
				Part? garden = plan.Parts.Exists(p => p.Kind == PartKind.Garden) ? plan.Parts.Find(p => p.Kind == PartKind.Garden) : (Part?)null;
				if (garden.HasValue)
				{
					Assert.That(Vector2.Distance(garden.Value.At, plan.Court), Is.LessThan(1e-3f), $"{tier}: the garden is the court's middle");
				}
				foreach (Part part in plan.Parts)
				{
					if ((part.Kind != PartKind.Hall && part.Kind != PartKind.Wing) || !garden.HasValue)
					{
						continue;
					}
					Vector2 toCourt = (plan.Court - part.Door).normalized;
					Assert.That(Vector2.Dot(part.Out, toCourt), Is.GreaterThan(0.99f), $"{tier} seed {seed}: the {part.Kind} at {part.At} looks into the court");
					// On the garden's cross: the hall's door on its z walk, a wing's on its x walk.
					float off = part.Kind == PartKind.Hall ? part.Door.x - plan.Court.x : part.Door.y - plan.Court.y;
					Assert.That(Mathf.Abs(off), Is.LessThan(1e-3f), $"{tier} seed {seed}: the {part.Kind}'s door is on the garden's walk");
				}
				Assert.That(plan.Parts.FindAll(p => p.Kind == PartKind.Hall).Count, Is.EqualTo(1), $"{tier}: one throne hall");
			}
		}

		[Test]
		public void EveryPartIsInsideTheFootprint_AndTheGateIsOnTheFront()
		{
			var four = new Vector2[4];
			foreach ((Tier tier, int seed, PointOfInterestPalace.Layout plan) in Plans())
			{
				Vector2 half = plan.Footprint * 0.5f + Vector2.one * 1e-3f;
				foreach (Part part in plan.Parts)
				{
					PointOfInterestLayouts.Corners(part.At, part.Yaw, part.Size, four);
					foreach (Vector2 c in four)
					{
						Assert.That(Mathf.Abs(c.x) <= half.x && Mathf.Abs(c.y) <= half.y, Is.True, $"{tier} seed {seed}: {part.Kind} at {part.At} reaches {c}, outside {plan.Footprint}");
					}
				}
				List<Part> gates = plan.Parts.FindAll(p => p.Kind == PartKind.Gatehouse);
				if (tier != Tier.Walled)
				{
					Assert.That(gates, Is.Empty, $"{tier}: no walls");
					Assert.That(plan.Parts.Exists(p => p.Kind == PartKind.Curtain || p.Kind == PartKind.Tower), Is.False, $"{tier}: no walls");
					continue;
				}
				Assert.That(gates.Count, Is.EqualTo(1), "one gatehouse");
				Part gate = gates[0];
				Assert.That(Mathf.Abs(gate.At.x), Is.LessThan(1e-3f), "the gate in the middle of the front");
				Assert.That(Mathf.DeltaAngle(gate.Yaw, 0f), Is.EqualTo(0f).Within(1e-3f), "the gate's front looks out of the front (−z)");
				foreach (Part part in plan.Parts)
				{
					Assert.That(part.At.y, Is.GreaterThanOrEqualTo(gate.At.y - 1e-3f), $"{part.Kind} at {part.At} stands behind the front");
				}
				Assert.That(plan.Court.y, Is.GreaterThan(gate.At.y), "the court behind the gate");
				// The curtain closed round the ward: every module stretched by at most half (a run is whole modules), its front out of the ward.
				foreach (Part module in plan.Parts.FindAll(p => p.Kind == PartKind.Curtain))
				{
					Assert.That(module.Stretch, Is.InRange(0.66f, 1.5f), $"curtain module at {module.At}");
					Vector2 outward = PointOfInterestLayouts.Rotate(Vector2.down, module.Yaw);
					Assert.That(Vector2.Dot(outward, module.At - plan.Court), Is.GreaterThan(0f), $"curtain module at {module.At} looks out");
				}
				Assert.That(plan.Parts.FindAll(p => p.Kind == PartKind.CornerTower).Count, Is.EqualTo(4), "a corner tower at every corner");
				Assert.That(plan.Parts.FindAll(p => p.Kind == PartKind.Tower).Count, Is.GreaterThanOrEqualTo(2), "towers along the runs");
				foreach (Part corner in plan.Parts.FindAll(p => p.Kind == PartKind.CornerTower))
				{
					// Its field quadrant (−x, −z) out of the ward, its door into it.
					Vector2 field = PointOfInterestLayouts.Rotate(new Vector2(-1f, -1f), corner.Yaw);
					Assert.That(Vector2.Dot(field, corner.At - plan.Court), Is.GreaterThan(0f), $"the corner tower at {corner.At} looks out");
					Assert.That(Mathf.Sign(field.x) == Mathf.Sign(corner.At.x - plan.Court.x) && Mathf.Sign(field.y) == Mathf.Sign(corner.At.y - plan.Court.y), Is.True,
						$"the corner tower at {corner.At}: its field quadrant is the corner's outside");
					Assert.That(Vector2.Dot(corner.Out, plan.Court - corner.Door), Is.GreaterThan(0f), $"the corner tower at {corner.At} opens on the ward");
				}
				AssertTheRingCloses(plan, $"{tier} seed {seed}");
			}
		}

		/// <summary>
		/// The ward's walk closed (2026-10-19): every station's two walk doors (a corner tower's turning the corner) met square by a module's
		/// end, and every module's end met by its neighbour's or a station's walk door, so no walk door and no walk ends over a drop.
		/// </summary>
		private static void AssertTheRingCloses(PointOfInterestPalace.Layout plan, string name)
		{
			List<Part> modules = plan.Parts.FindAll(p => p.Kind == PartKind.Curtain);
			var ends = new List<(Vector2 at, float yaw)>();
			foreach (Part m in modules)
			{
				Vector2 x = PointOfInterestLayouts.Rotate(new Vector2(m.Size.x * 0.5f, 0f), m.Yaw);
				ends.Add((m.At + x, m.Yaw));
				ends.Add((m.At - x, m.Yaw));
			}
			var doors = new List<Vector2>();
			foreach (Part station in plan.Parts)
			{
				if (!IsStation(station.Kind))
				{
					continue;
				}
				float[] joins = station.Kind == PartKind.Gatehouse ? Kit.GateJoin : station.Kind == PartKind.CornerTower ? Kit.CornerJoin : Kit.TowerJoin;
				float join = joins[Mathf.Clamp(station.Variant, 0, joins.Length - 1)];
				// Its walk doors, each with the yaw of the module leaving it: a wall tower's and a gatehouse's on ±x (the module at its
				// own yaw), a corner tower's on +x (at its yaw) and +z (a quarter on).
				var walkDoors = station.Kind == PartKind.CornerTower
					? new[] { (new Vector2(join, 0f), 0f), (new Vector2(0f, join), 90f) }
					: new[] { (new Vector2(join, 0f), 0f), (new Vector2(-join, 0f), 0f) };
				foreach ((Vector2 local, float turn) in walkDoors)
				{
					Vector2 door = station.At + PointOfInterestLayouts.Rotate(local, station.Yaw);
					doors.Add(door);
					Assert.That(ends.Exists(e => Vector2.Distance(e.at, door) < 0.05f && Mathf.Abs(Mathf.DeltaAngle(e.yaw, station.Yaw + turn)) < 0.1f), Is.True,
						$"{name}: the {station.Kind} at {station.At} has the curtain square at its walk door {door}");
				}
			}
			foreach ((Vector2 at, float yaw) in ends)
			{
				int met = ends.FindAll(e => Vector2.Distance(e.at, at) < 0.05f).Count;
				bool closed = met >= 2 || doors.Exists(d => Vector2.Distance(d, at) < 0.05f);
				Assert.That(closed, Is.True, $"{name}: a curtain module ends at {at} over nothing");
			}
		}

		[Test]
		public void ThePlanIsDeterministic_AndItsFootprintIsTheTiersAlone()
		{
			for (int t = 0; t < PointOfInterestPalace.Tiers; t++)
			{
				PointOfInterestPalace.Layout first = PointOfInterestPalace.Plan(Kit, (Tier)t, 77), again = PointOfInterestPalace.Plan(Kit, (Tier)t, 77);
				Assert.That(again.Parts.Count, Is.EqualTo(first.Parts.Count));
				for (int i = 0; i < first.Parts.Count; i++)
				{
					Assert.That(again.Parts[i].At, Is.EqualTo(first.Parts[i].At));
					Assert.That(again.Parts[i].Yaw, Is.EqualTo(first.Parts[i].Yaw));
					Assert.That(again.Parts[i].Variant, Is.EqualTo(first.Parts[i].Variant));
				}
				foreach (int seed in Seeds)
				{
					Assert.That(PointOfInterestPalace.Plan(Kit, (Tier)t, seed).Footprint, Is.EqualTo(first.Footprint), $"{(Tier)t}: seed {seed}'s footprint");
				}
			}
			// The seed does draw the variants.
			var halls = new HashSet<int>();
			foreach (int seed in Seeds)
			{
				halls.Add(PointOfInterestPalace.Plan(Kit, Tier.Walled, seed).Parts.Find(p => p.Kind == PartKind.Hall).Variant);
			}
			Assert.That(halls.Count, Is.EqualTo(2), "both throne halls drawn over the seeds");
		}

		[Test]
		public void TheTierStepsDownWithStature_NeverPastItsShare()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			PointOfInterestKindRule capital = rules.RuleFor(POIType.Capital);
			foreach (float radius in new[] { capital.FootprintMin, capital.FootprintMax })
			{
				// The largest capital at the kit's stature walled; the smallest at least the palace unwalled.
				Tier kit = PointOfInterestPalace.TierFor(Kit, radius, 1f);
				Assert.That(radius >= capital.FootprintMax ? kit == Tier.Walled : kit <= Tier.Open, Is.True, $"a capital of {radius} m at the kit's stature: {kit}");
				Tier last = Tier.Walled;
				foreach (float scale in new[] { RaceStature.MinScale, 1f, 1.3f, 1.6f, 2f, 2.5f, 3f, RaceStature.MaxScale })
				{
					Tier tier = PointOfInterestPalace.TierFor(Kit, radius, scale);
					Assert.That((int)tier, Is.GreaterThanOrEqualTo((int)last), $"radius {radius} stature {scale}: the tier only steps down");
					last = tier;
					float reach = PointOfInterestPalace.Plan(Kit, tier, 0).Reach * scale;
					Assert.That(tier == Tier.Lone || reach <= PointOfInterestPalace.ReachShare * radius, Is.True, $"radius {radius} stature {scale}: {tier} reaches {reach:0} m");
					Console.WriteLine($"radius {radius:0} stature {scale:0.00}: {tier}, {PointOfInterestPalace.Reserve(Kit, radius, scale)} m");
				}
			}
		}

		// ── With the kit and the layout (meshes measured: run in Unity or under nativerun) ──

		[Test]
		public void TheKitMeasuresAsThePlanTestsTakeIt()
		{
			PointOfInterestPalace.Sizes kit = PointOfInterestPalace.Measured, taken = Kit;
			Console.WriteLine($"hall {kit.Hall} wing {kit.Wing} garden {kit.Garden} gate {kit.Gate} tower {kit.Tower} module {kit.Module} curtain {kit.CurtainDepth} " +
				$"gate joins {string.Join(",", kit.GateJoin)} tower joins {string.Join(",", kit.TowerJoin)} corner {kit.Corner} joins {string.Join(",", kit.CornerJoin)} doors {string.Join(",", kit.CornerDoor)}");
			for (int t = 0; t < PointOfInterestPalace.Tiers; t++)
			{
				PointOfInterestPalace.Layout plan = PointOfInterestPalace.Plan(kit, (Tier)t, 0);
				Console.WriteLine($"{(Tier)t}: {plan.Footprint.x:0.0} x {plan.Footprint.y:0.0} m, reach {plan.Reach:0.0} m, {plan.Parts.Count} parts");
			}
			void Near(Vector2 a, Vector2 b, string what) => Assert.That(Vector2.Distance(a, b), Is.LessThan(0.6f), $"{what}: kit {a}, the tests take {b}");
			Near(kit.Hall, taken.Hall, "hall");
			Near(kit.Wing, taken.Wing, "wing");
			Near(kit.Garden, taken.Garden, "garden");
			Near(kit.Gate, taken.Gate, "gatehouse");
			Near(kit.Tower, taken.Tower, "tower");
			Assert.That(kit.Module, Is.EqualTo(taken.Module).Within(0.01f), "module");
			Assert.That(kit.CurtainDepth, Is.EqualTo(taken.CurtainDepth).Within(0.05f), "curtain");
			Assert.That(kit.GateJoin.Length, Is.EqualTo(taken.GateJoin.Length));
			Assert.That(kit.TowerJoin.Length, Is.EqualTo(taken.TowerJoin.Length));
			Near(kit.Corner, taken.Corner, "corner tower");
			Assert.That(kit.CornerJoin.Length, Is.EqualTo(taken.CornerJoin.Length), "corner tower variants");
			for (int v = 0; v < kit.CornerJoin.Length; v++)
			{
				Assert.That(kit.CornerJoin[v], Is.EqualTo(taken.CornerJoin[v]).Within(0.1f), $"corner join v{v}");
				Assert.That(Vector4.Distance(kit.CornerDoor[v], taken.CornerDoor[v]), Is.LessThan(0.15f), $"corner door v{v}: kit {kit.CornerDoor[v]}");
			}
			for (int v = 0; v < kit.GateJoin.Length; v++)
			{
				Assert.That(kit.GateJoin[v], Is.EqualTo(taken.GateJoin[v]).Within(0.1f), $"gate join v{v}");
			}
			for (int v = 0; v < kit.TowerJoin.Length; v++)
			{
				Assert.That(kit.TowerJoin[v], Is.EqualTo(taken.TowerJoin[v]).Within(0.1f), $"tower join v{v}");
			}
			foreach (string id in new[] { PointOfInterestPalace.HallId, PointOfInterestPalace.WingId, PointOfInterestPalace.GardenId, PointOfInterestPalace.CornerId })
			{
				Assert.That(StructurePieces.Find(id), Is.Not.Null, $"the kit has {id}");
			}
		}

		/// <summary>The request PropsFeature builds for a capital (as LandmarkFrontageTests'), at a radius and a stature, the palace's item the compound.</summary>
		private static PointOfInterestLayoutRequest Request(PointOfInterestTemplateSpec spec, int size, float radius, float stature, int seed, DeterministicRNG random, out int palaceSlot)
		{
			var request = new PointOfInterestLayoutRequest
			{
				Layout = spec.Layout, Radius = radius, Gap = 1f, SpanHalf = radius, ShoreAhead = radius * PointOfInterestLayouts.Inset,
				Kind = spec.Kind, Seed = seed,
			};
			palaceSlot = -1;
			for (int s = 0; s < spec.Pieces.Count; s++)
			{
				PointOfInterestPieceSlot slot = spec.Pieces[s];
				string style = string.IsNullOrEmpty(slot.Style) ? spec.Style : slot.Style;
				PointOfInterestSlotRole role = PointOfInterestLayouts.RoleFor(spec.Layout, s, slot.Role);
				int count = PropsFeature.CountFor(slot, size, true, random);
				if (stature > 1.05f && count > 0)
				{
					count = Mathf.Max(1, Mathf.RoundToInt(count / (stature * stature)));
				}
				PointOfInterestPieceMetrics metrics = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, 0));
				bool station = role == PointOfInterestSlotRole.Gate || role == PointOfInterestSlotRole.WallTower;
				request.Slots.Add(new PointOfInterestLayoutSlot
				{
					Role = role, Footprint = metrics.Footprint, Module = metrics.Module, Count = count,
					Walk = role == PointOfInterestSlotRole.Perimeter ? PropsFeature.PerimeterWalk(slot.Tag, style) : 0f,
					Join = station ? PropsFeature.StationJoin(slot.Tag, style, 0) : 0f,
					Landmark = PointOfInterestTemplateSpecs.IsLandmark(slot.Tag, spec.Kind),
				});
				if (slot.Tag == PointOfInterestTemplateSpecs.Palace)
				{
					palaceSlot = s;
				}
				if (PointOfInterestLayouts.Generates(role))
				{
					continue;
				}
				for (int k = 0; k < count; k++)
				{
					int pieceSeed = random.Next();
					PointOfInterestPieceMetrics piece = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, pieceSeed, 0f));
					Vector2 footprint = slot.Tag == PointOfInterestTemplateSpecs.Palace ? PointOfInterestPalace.Reserve(radius, stature) : piece.Footprint * stature;
					request.Items.Add(new PointOfInterestLayoutItem { Slot = s, Footprint = footprint });
				}
			}
			return request;
		}

		[Test]
		public void EveryCapitalKeepsItsPalacesGround_AtEverySizeAndStature()
		{
			var problems = new List<string>();
			PointOfInterestKindRule rule = PointOfInterestRules.Default().RuleFor(POIType.Capital);
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.For(POIType.Capital))
			{
				foreach (int size in spec.Sizes())
				{
					foreach (float radius in new[] { rule.FootprintMin, 0.5f * (rule.FootprintMin + rule.FootprintMax), rule.FootprintMax })
					{
						foreach (float stature in new[] { 1f, 1.6f, 2.5f })
						{
							int laid = 0, cathedrals = 0, houses = 0, asked = 0;
							for (int seed = 1; seed <= 6; seed++)
							{
								var random = new DeterministicRNG(9100 + seed * 37 + size);
								PointOfInterestLayoutRequest request = Request(spec, size, radius, stature, seed, random, out int palaceSlot);
								List<PointOfInterestLayoutPiece> pieces = PointOfInterestLayouts.Place(request, random);
								string name = $"{spec.Name} size {size} radius {radius:0} stature {stature} seed {seed}";
								int index = pieces.FindIndex(p => p.Slot == palaceSlot && p.Item >= 0);
								if (index < 0)
								{
									problems.Add($"{name}: the palace was not laid ({request.ChosenPlan})");
									continue;
								}
								laid++;
								PointOfInterestLayoutPiece palace = pieces[index];
								Assert.That(palace.Footprint, Is.EqualTo(PointOfInterestPalace.Reserve(radius, stature)), $"{name}: its whole compound's ground");
								// Clear of every street (the packer lays a citadel before them, they go round it; a centrepiece laid at the market
								// instead may stand on them) and of every other piece.
								foreach (PointOfInterestLayoutStreet street in request.Streets)
								{
									for (int k = 1; k < street.Points.Length; k++)
									{
										Vector2 a = street.Points[k - 1], b = street.Points[k];
										if ((b - a).sqrMagnitude < 1e-4f)
										{
											continue;
										}
										float along = PointOfInterestLayouts.DirectionOf(b - a);
										if (PointOfInterestLayouts.Overlaps(palace.Offset, palace.Yaw, palace.Footprint, 0.5f * (a + b), along, new Vector2(street.Width, Vector2.Distance(a, b))))
										{
											// A giant's throne hall alone may find no room against the smallest capital's wall and stand on its
											// square instead (the layout's centrepiece always stands): told, not held.
											if (stature < 2f)
											{
												problems.Add($"{name}: a street runs through the palace ({request.ChosenPlan})");
											}
											else
											{
												Console.WriteLine($"{name}: the palace stands on the square's streets");
											}
											k = street.Points.Length;
										}
									}
								}
								for (int i = 0; i < pieces.Count; i++)
								{
									PointOfInterestLayoutPiece other = pieces[i];
									if (i != index && PointOfInterestLayouts.Overlaps(palace.Offset, palace.Yaw, palace.Footprint, other.Offset, other.Yaw, other.Footprint))
									{
										problems.Add($"{name}: piece of slot {other.Slot} at {other.Offset} stands in the palace");
									}
								}
								int cathedral = spec.Pieces.FindIndex(p => p.Tag == PointOfInterestTemplateSpecs.Cathedral);
								cathedrals += cathedral >= 0 && pieces.Exists(p => p.Slot == cathedral && p.Item >= 0) ? 1 : 0;
								for (int i = 0; i < request.Items.Count; i++)
								{
									if (spec.Pieces[request.Items[i].Slot].Role == PointOfInterestSlotRole.Street)
									{
										asked++;
										houses += pieces.Exists(p => p.Item == i) ? 1 : 0;
									}
								}
							}
							Console.WriteLine($"{spec.Name} size {size} radius {radius:0} stature {stature}: palace {PointOfInterestPalace.TierFor(PointOfInterestPalace.Measured, radius, stature)} " +
								$"{PointOfInterestPalace.Reserve(radius, stature)} laid {laid}/6, cathedral {cathedrals}/6, street pieces {houses}/{asked}");
							// At the kit's stature most of the houses keep their frontage (round a keep-sized citadel 71..80% of them do in
							// the smallest capital, all in the largest). A big race's capital is crowded round any centrepiece (35..94% round
							// a keep at 2.5 times), so it is told, not held.
							if (stature <= 1f && houses < asked * (radius >= 190f ? 0.9f : 0.55f))
							{
								problems.Add($"{spec.Name} size {size} radius {radius:0} stature {stature}: only {houses} of {asked} street pieces found room beside the palace");
							}
						}
					}
				}
			}
			Assert.That(problems, Is.Empty, string.Join("\n", problems));
		}
	}
}
