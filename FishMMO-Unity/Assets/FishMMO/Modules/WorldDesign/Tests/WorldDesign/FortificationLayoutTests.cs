using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The fortification contract as the layout keeps it (2026-10-19: "palisades aren't properly configured and aligned", "the stairs
	/// don't fit", "stairs are missing platforms at the top", "guard tower interiors are not always enterable"): every walled template
	/// laid over many seeds and sizes on irregular outlines, its curtain's modules meeting at shared joints with their corners closed,
	/// its gates and towers in line with the curtain (−z out, ±x along it), a walked curtain ending on its towers' sides instead of
	/// running through them, a palisade's towers inside it, and every wall stair flat against two straight walked modules with its
	/// landing on the walk. Pure: no scene.
	/// </summary>
	[TestFixture]
	public class FortificationLayoutTests
	{
		[OneTimeSetUp]
		public void RegisterKit() => StructureKitPieceSource.Register();

		/// <summary>How near two module ends (or an end and a station's side) must lie to be one joint, metres.</summary>
		private const float JointMetres = 0.03f;

		private sealed class Site
		{
			public string Name;
			public PointOfInterestLayoutRequest Request;
			public List<PointOfInterestLayoutPiece> Pieces;
			public int Wall = -1, Gate = -1, Tower = -1;
			public bool Walked;
			public float Module;
			/// <summary>Its outline at least 0.84 as wide as long: no end tighter than a 0.7 radius.</summary>
			public bool Gentle;
		}

		/// <summary>Every template with a ring of modules, laid at each of its sizes on <paramref name="seeds"/> outlines and sets of ways.</summary>
		private static IEnumerable<Site> WalledSites(int seeds)
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All())
			{
				int wall = -1;
				for (int s = 0; s < spec.Pieces.Count; s++)
				{
					if (PointOfInterestLayouts.RoleFor(spec.Layout, s, spec.Pieces[s].Role) == PointOfInterestSlotRole.Perimeter)
					{
						wall = s;
						break;
					}
				}
				if (wall < 0)
				{
					continue;
				}
				PointOfInterestKindRule rule = rules.RuleFor(spec.Kind);
				foreach (int size in spec.Sizes())
				{
					for (int seed = 0; seed < seeds; seed++)
					{
						Site site = Lay(spec, rule, size, seed, wall);
						if (site != null)
						{
							yield return site;
						}
					}
				}
			}
		}

		private static Site Lay(PointOfInterestTemplateSpec spec, PointOfInterestKindRule rule, int size, int seed, int wall)
		{
			float min = rule != null ? rule.FootprintMin : 30f, max = rule != null ? rule.FootprintMax : 30f;
			float radius = Mathf.Lerp(min, max, size / 2f) * (0.9f + 0.1f * (seed % 3));
			var random = new DeterministicRNG(1000 + 97 * seed + size);
			var request = new PointOfInterestLayoutRequest
			{
				Layout = spec.Layout,
				Radius = radius,
				Gap = 1f,
				Yaw = (seed * 37) % 360,
				SpanHalf = radius,
				ShoreAhead = radius * PointOfInterestLayouts.Inset,
				Outline = Oval(radius, 0.72f + 0.06f * (seed % 5), seed * 53f),
				Seed = seed * 7919 + 3,
			};
			int ways = seed % 4;
			for (int k = 0; k < ways; k++)
			{
				request.Entrances.Add(Mathf.Repeat(seed * 71f + k * 360f / ways + 13f * k, 360f));
			}
			var site = new Site { Name = $"{spec.Name} size {size} seed {seed} ({radius:0} m)", Request = request, Wall = wall, Gentle = seed % 5 >= 2 };
			for (int s = 0; s < spec.Pieces.Count; s++)
			{
				PointOfInterestPieceSlot slot = spec.Pieces[s];
				string style = string.IsNullOrEmpty(slot.Style) ? spec.Style : slot.Style;
				PointOfInterestSlotRole role = PointOfInterestLayouts.RoleFor(spec.Layout, s, slot.Role);
				int count = PropsFeature.CountFor(slot, size, true, random);
				// As PropsFeature lays them: a ring's gates and towers one design, measured by the site's seed for them.
				bool station = role == PointOfInterestSlotRole.Gate || role == PointOfInterestSlotRole.WallTower;
				int stationSeed = seed * 7919 + s;
				PointOfInterestPieceMetrics metrics = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, station ? stationSeed : 0));
				request.Slots.Add(new PointOfInterestLayoutSlot
				{
					Role = role,
					Footprint = Vector2.Max(metrics.Footprint, metrics.Sweep),
					Module = metrics.Module,
					Count = count,
					Walk = role == PointOfInterestSlotRole.Perimeter ? PropsFeature.PerimeterWalk(slot.Tag, style) : 0f,
					Join = station ? PropsFeature.StationJoin(slot.Tag, style, stationSeed) : 0f,
				});
				if (role == PointOfInterestSlotRole.Gate && site.Gate < 0)
				{
					site.Gate = s;
				}
				if (role == PointOfInterestSlotRole.WallTower && site.Tower < 0)
				{
					site.Tower = s;
				}
				if (PointOfInterestLayouts.Generates(role))
				{
					continue;
				}
				for (int k = 0; k < count; k++)
				{
					float decay = random.Range(slot.MinDecay, Mathf.Max(slot.MinDecay, slot.MaxDecay));
					float scale = random.Range(slot.MinScale, Mathf.Max(slot.MinScale, slot.MaxScale));
					PointOfInterestPieceMetrics piece = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, random.Next(), decay));
					request.Items.Add(new PointOfInterestLayoutItem { Slot = s, Footprint = piece.Footprint * scale });
				}
			}
			site.Module = request.Slots[wall].Module;
			if (!(site.Module > 0f))
			{
				return null;
			}
			site.Walked = request.Slots[wall].Walk > 0f;
			site.Pieces = PointOfInterestLayouts.Place(request, random);
			return site;
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

		private static Vector2 X(PointOfInterestLayoutPiece piece) => PointOfInterestLayouts.Rotate(Vector2.right, piece.Yaw);

		/// <summary>A piece's front: the way its −z looks.</summary>
		private static Vector2 Out(PointOfInterestLayoutPiece piece) => PointOfInterestLayouts.Rotate(Vector2.down, piece.Yaw);

		private static bool Module(Site site, PointOfInterestLayoutPiece piece) => piece.Role == PointOfInterestSlotRole.Perimeter && piece.From != piece.To;

		private static bool Station(PointOfInterestLayoutPiece piece) => piece.Role == PointOfInterestSlotRole.Gate || piece.Role == PointOfInterestSlotRole.WallTower;

		/// <summary>Where the curtain meets a station, metres each side of its centre: the layout's own rule.</summary>
		private static float Join(Site site, PointOfInterestLayoutPiece station)
		{
			PointOfInterestLayoutSlot slot = site.Request.Slots[station.Slot];
			if (site.Walked)
			{
				return PointOfInterestLayouts.StationJoin(slot);
			}
			return 0.5f * Mathf.Max(slot.Module, slot.Footprint.x);
		}

		/// <summary>The station whose side a joint lies on, or −1.</summary>
		private static int StationAt(Site site, Vector2 joint)
		{
			for (int i = 0; i < site.Pieces.Count; i++)
			{
				PointOfInterestLayoutPiece p = site.Pieces[i];
				if (!Station(p) || (!site.Walked && p.Role == PointOfInterestSlotRole.WallTower))
				{
					continue;
				}
				float join = Join(site, p);
				if (Vector2.Distance(joint, p.Offset + X(p) * join) < JointMetres || Vector2.Distance(joint, p.Offset - X(p) * join) < JointMetres)
				{
					return i;
				}
			}
			return -1;
		}

		private static void Report(List<string> problems, int checkedSites)
		{
			Assert.That(checkedSites, Is.GreaterThan(20), "walled sites laid");
			Assert.That(problems, Is.Empty, $"{problems.Count} problem(s):\n" + string.Join("\n", problems.GetRange(0, Mathf.Min(25, problems.Count))));
		}

		// ── The curtain ──────────────────────────────────────────

		[Test]
		public void EveryModuleMeetsItsNeighbourOrAStationAtAJoint()
		{
			var problems = new List<string>();
			int sites = 0, tiny = 0;
			foreach (Site site in WalledSites(8))
			{
				sites++;
				var ends = new List<(Vector2 at, int piece)>();
				for (int i = 0; i < site.Pieces.Count; i++)
				{
					if (Module(site, site.Pieces[i]))
					{
						ends.Add((site.Pieces[i].From, i));
						ends.Add((site.Pieces[i].To, i));
					}
				}
				if (ends.Count < 6)
				{
					// A ring too small for its gate (a keep on a narrow outline) lays none, as it always did; rare.
					tiny++;
					continue;
				}
				int loose = 0;
				foreach ((Vector2 at, int piece) in ends)
				{
					int shared = 0;
					foreach ((Vector2 other, int otherPiece) in ends)
					{
						shared += otherPiece != piece && Vector2.Distance(at, other) < JointMetres ? 1 : 0;
					}
					if (shared == 1 || (shared == 0 && StationAt(site, at) >= 0))
					{
						continue;
					}
					if (shared == 0 && site.Gate < 0)
					{
						// A ring with no gate slot leaves one opening: two loose ends.
						loose++;
						continue;
					}
					problems.Add($"{site.Name}: a module end at {at} meets {shared} others and no station");
				}
				if (loose > 2)
				{
					problems.Add($"{site.Name}: {loose} loose ends");
				}
			}
			Assert.That(tiny, Is.LessThan(sites / 20), "rings too small to lay");
			Report(problems, sites);
		}

		[Test]
		public void CornersAreClosedAndModulesKeepNearTheirLength()
		{
			var problems = new List<string>();
			int sites = 0;
			foreach (Site site in WalledSites(8))
			{
				sites++;
				float depth = site.Request.Slots[site.Wall].Footprint.y;
				var modules = site.Pieces.FindAll(p => Module(site, p));
				foreach (PointOfInterestLayoutPiece m in modules)
				{
					float chord = Vector2.Distance(m.From, m.To);
					if (chord < 0.6f * site.Module || chord > 1.45f * site.Module)
					{
						problems.Add($"{site.Name}: a {site.Module} m module laid {chord:0.00} m long at {m.Offset}");
					}
					// Its footprint's ends on its chord's (or run on past them at a corner), never short of them.
					float half = 0.5f * m.Footprint.x;
					float a = Vector2.Dot(m.From - m.Offset, X(m)), b = Vector2.Dot(m.To - m.Offset, X(m));
					if (a < -half - 0.01f || b > half + 0.01f)
					{
						problems.Add($"{site.Name}: a module at {m.Offset} stops short of its joints");
					}
				}
				// Two modules on a joint: their outer faces' corners meet (or, where the ring bends outward, their inner faces').
				for (int i = 0; i < modules.Count; i++)
				{
					for (int j = i + 1; j < modules.Count; j++)
					{
						PointOfInterestLayoutPiece p = modules[i], q = modules[j];
						Vector2? joint = Vector2.Distance(p.To, q.From) < JointMetres ? p.To : Vector2.Distance(p.From, q.To) < JointMetres ? p.From : (Vector2?)null;
						if (joint == null)
						{
							continue;
						}
						Vector2 Corner(PointOfInterestLayoutPiece m, float side)
						{
							float along = Vector2.Dot(joint.Value - m.Offset, X(m)) > 0f ? 1f : -1f;
							return m.Offset + X(m) * (along * 0.5f * m.Footprint.x) + Out(m) * (side * 0.5f * depth);
						}
						float gap = Mathf.Min(Vector2.Distance(Corner(p, 1f), Corner(q, 1f)), Vector2.Distance(Corner(p, -1f), Corner(q, -1f)));
						if (gap > 0.08f)
						{
							problems.Add($"{site.Name}: the corner at {joint.Value} opens {gap:0.00} m");
						}
					}
				}
			}
			Report(problems, sites);
		}

		// ── Gates and towers ─────────────────────────────────────

		[Test]
		public void GatesAndTowersFaceOutAndStandInLineWithTheCurtain()
		{
			var problems = new List<string>();
			int sites = 0, stations = 0;
			foreach (Site site in WalledSites(8))
			{
				sites++;
				var modules = site.Pieces.FindAll(p => Module(site, p));
				for (int i = 0; i < site.Pieces.Count; i++)
				{
					PointOfInterestLayoutPiece s = site.Pieces[i];
					if (!Station(s))
					{
						continue;
					}
					stations++;
					// −z out: its front looks away from the middle, square to the ring there.
					if (s.Offset.sqrMagnitude > 1f && Vector2.Dot(Out(s), s.Offset.normalized) < 0.5f)
					{
						problems.Add($"{site.Name}: the {s.Role} at {s.Offset} faces {Out(s)}, not out");
					}
					if (s.Role == PointOfInterestSlotRole.WallTower && !site.Walked)
					{
						continue;
					}
					// The curtain ends on both its sides, each end's module running along its x.
					float join = Join(site, s);
					foreach (float side in new[] { -1f, 1f })
					{
						Vector2 face = s.Offset + X(s) * (side * join);
						PointOfInterestLayoutPiece? met = null;
						foreach (PointOfInterestLayoutPiece m in modules)
						{
							if (Vector2.Distance(m.From, face) < JointMetres || Vector2.Distance(m.To, face) < JointMetres)
							{
								met = m;
							}
						}
						if (met == null)
						{
							if (site.Gate >= 0 || s.Role != PointOfInterestSlotRole.Gate)
							{
								problems.Add($"{site.Name}: no curtain meets the {s.Role} at {s.Offset} on its {(side > 0 ? "+x" : "−x")} side");
							}
							continue;
						}
						Vector2 run = (met.Value.To - met.Value.From).normalized;
						float angle = Vector2.Angle(run, X(s));
						angle = Mathf.Min(angle, 180f - angle);
						// In line where the ring is wide and gently curved (a city's); a castle's tighter ring turns at its gates and towers,
						// as polygonal curtains do, but never sharply; a keep's ring is little wider than its gatehouse.
						if (s.Offset.magnitude >= 40f && angle > (site.Gentle && s.Offset.magnitude >= 60f ? 15f : 30f))
						{
							problems.Add($"{site.Name}: the curtain meets the {s.Role} at {s.Offset} {angle:0}° off its x");
						}
					}
				}
			}
			Assert.That(stations, Is.GreaterThan(50), "gates and towers laid");
			Report(problems, sites);
		}

		[Test]
		public void AWalkedCurtainNeverRunsThroughATower()
		{
			var problems = new List<string>();
			int sites = 0, towers = 0;
			foreach (Site site in WalledSites(8))
			{
				sites++;
				foreach (PointOfInterestLayoutPiece t in site.Pieces)
				{
					if (t.Role != PointOfInterestSlotRole.WallTower)
					{
						continue;
					}
					towers++;
					// Inside its sides (its rooms, its stair, its walk doors' landings) no module's line runs: it ends on them.
					float join = site.Walked ? Join(site, t) : 0.5f * t.Footprint.x;
					float deep = 0.5f * t.Footprint.y - 0.1f;
					foreach (PointOfInterestLayoutPiece m in site.Pieces)
					{
						if (!Module(site, m))
						{
							continue;
						}
						for (int k = 1; k < 20; k++)
						{
							Vector2 p = Vector2.Lerp(m.From, m.To, k / 20f) - t.Offset;
							float along = Mathf.Abs(Vector2.Dot(p, X(t))), across = Mathf.Abs(Vector2.Dot(p, Out(t)));
							if (along < join - 0.1f && across < deep)
							{
								problems.Add($"{site.Name}: a module at {m.Offset} runs into the {(site.Walked ? "wall" : "palisade")} tower at {t.Offset}");
								break;
							}
						}
					}
				}
			}
			Assert.That(towers, Is.GreaterThan(30), "towers laid");
			Report(problems, sites);
		}

		// ── Wall stairs ──────────────────────────────────────────

		[Test]
		public void EveryWallStairStandsFlatAgainstAWalkWithItsLandingOnIt()
		{
			var problems = new List<string>();
			int sites = 0, walked = 0, stairs = 0;
			float length = StructurePieces.WallStairLength, offset = StructurePieces.CurtainHalfThickness + 0.5f * StructurePieces.WallStairWidth;
			foreach (Site site in WalledSites(8))
			{
				sites++;
				if (!site.Walked)
				{
					if (site.Request.Stairs.Count > 0)
					{
						problems.Add($"{site.Name}: {site.Request.Stairs.Count} stairs against a curtain with no walk");
					}
					continue;
				}
				walked++;
				if (site.Request.Stairs.Count == 0 && site.Gate >= 0 && site.Request.Radius >= 40f)
				{
					problems.Add($"{site.Name}: a walked curtain with no stair up to it");
				}
				foreach (PointOfInterestLayoutStair stair in site.Request.Stairs)
				{
					stairs++;
					PointOfInterestLayoutPiece foot = site.Pieces[stair.FootModule], landing = site.Pieces[stair.LandingModule];
					if (!Module(site, foot) || !Module(site, landing) || site.Request.Slots[foot.Slot].Walk <= 0f)
					{
						problems.Add($"{site.Name}: the stair at {stair.Offset} is not against two walked modules");
						continue;
					}
					// The two modules one straight line, the stair's yaw theirs, its landing on the +x one.
					Vector2 x = PointOfInterestLayouts.Rotate(Vector2.right, stair.Yaw);
					float bend = Vector2.Angle(foot.To - foot.From, landing.To - landing.From);
					if (bend > 0.5f || Mathf.Abs(Mathf.DeltaAngle(stair.Yaw, foot.Yaw)) > 0.5f || Mathf.Abs(Mathf.DeltaAngle(stair.Yaw, landing.Yaw)) > 0.5f)
					{
						problems.Add($"{site.Name}: the stair at {stair.Offset} stands against a bend of {bend:0.0}° (yaw {stair.Yaw:0.0} vs {foot.Yaw:0.0}/{landing.Yaw:0.0})");
					}
					if (Vector2.Distance(foot.To, landing.From) > JointMetres)
					{
						problems.Add($"{site.Name}: the stair at {stair.Offset}'s modules do not meet foot to landing");
					}
					// Its back on their inner face, all along it: no gap, no overlap.
					foreach (PointOfInterestLayoutPiece m in new[] { foot, landing })
					{
						float inward = Vector2.Dot(stair.Offset - m.From, -Out(m));
						if (Mathf.Abs(inward - offset) > 0.02f)
						{
							problems.Add($"{site.Name}: the stair at {stair.Offset} stands {inward - offset:+0.00;-0.00} m off the wall's inner face");
						}
					}
					// Wholly along the two modules; its landing over the landing module.
					float a = Vector2.Dot(stair.Offset - x * (0.5f * length) - foot.From, x), b = Vector2.Dot(stair.Offset + x * (0.5f * length) - foot.From, x);
					float reach = Vector2.Dot(landing.To - foot.From, x);
					if (a < -0.01f || b > reach + 0.01f)
					{
						problems.Add($"{site.Name}: the stair at {stair.Offset} runs past its modules ({a:0.00}..{b:0.00} of 0..{reach:0.00})");
					}
					float top = Vector2.Dot(stair.Offset + x * (0.5f * length - 0.5f * StructurePieces.WallStairLanding) - landing.From, x);
					if (top < 0f || top > Vector2.Distance(landing.From, landing.To))
					{
						problems.Add($"{site.Name}: the stair at {stair.Offset}'s landing is not over its landing module");
					}
					// Its landing in the opening of the landing module's rail, the opening at least as wide as it.
					float stretch = landing.Footprint.x / site.Module;
					Vector2 opening = landing.Offset + X(landing) * (StructurePieces.CurtainLandingGap * stretch);
					float landingAt = Vector2.Dot(stair.Offset + x * (0.5f * length - 0.5f * StructurePieces.WallStairLanding) - opening, x);
					if (Mathf.Abs(landingAt) > 0.02f || StructurePieces.CurtainLandingGapHalf * stretch < 0.5f * StructurePieces.WallStairLanding)
					{
						problems.Add($"{site.Name}: the stair at {stair.Offset}'s landing misses the rail's opening by {landingAt:0.00} m (opening ±{StructurePieces.CurtainLandingGapHalf * stretch:0.00})");
					}
					// Nothing else stands on it or before its foot.
					foreach (PointOfInterestLayoutPiece p in site.Pieces)
					{
						if (p.Role == PointOfInterestSlotRole.Perimeter || Station(p))
						{
							continue;
						}
						if (PointOfInterestLayouts.Overlaps(stair.Offset, stair.Yaw, new Vector2(length + 1f, StructurePieces.WallStairWidth), p.Offset, p.Yaw, p.Footprint))
						{
							problems.Add($"{site.Name}: a {p.Role} at {p.Offset} stands on the stair at {stair.Offset}");
						}
					}
				}
			}
			Assert.That(walked, Is.GreaterThan(10), "walked curtains laid");
			Assert.That(stairs, Is.GreaterThan(walked), "stairs laid");
			Report(problems, sites);
		}

		[Test]
		public void APalisadesTowersStandInsideItWithTheirBacksToIt()
		{
			var problems = new List<string>();
			int sites = 0, towers = 0;
			foreach (Site site in WalledSites(8))
			{
				sites++;
				if (site.Walked)
				{
					continue;
				}
				var modules = site.Pieces.FindAll(p => Module(site, p));
				foreach (PointOfInterestLayoutPiece t in site.Pieces)
				{
					if (t.Role != PointOfInterestSlotRole.WallTower)
					{
						continue;
					}
					towers++;
					// Its back toward the nearest module, inside it.
					PointOfInterestLayoutPiece nearest = modules[0];
					foreach (PointOfInterestLayoutPiece m in modules)
					{
						if ((m.Offset - t.Offset).sqrMagnitude < (nearest.Offset - t.Offset).sqrMagnitude)
						{
							nearest = m;
						}
					}
					Vector2 toWall = nearest.Offset - t.Offset;
					if (Vector2.Dot(toWall, Out(t)) <= 0f || t.Offset.magnitude >= nearest.Offset.magnitude)
					{
						problems.Add($"{site.Name}: the palisade's tower at {t.Offset} is not inside it with its back to it");
					}
				}
			}
			Assert.That(towers, Is.GreaterThan(10), "palisade towers laid");
			Report(problems, sites);
		}

		[Test]
		public void TheSameSiteLaysTheSameRing()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.For(POIType.Castle))
			{
				int wall = spec.Pieces.FindIndex(p => p.Role == PointOfInterestSlotRole.Perimeter);
				Site a = Lay(spec, rules.RuleFor(spec.Kind), 1, 3, wall), b = Lay(spec, rules.RuleFor(spec.Kind), 1, 3, wall);
				Assert.That(a.Pieces.Count, Is.EqualTo(b.Pieces.Count));
				for (int i = 0; i < a.Pieces.Count; i++)
				{
					Assert.That(a.Pieces[i].Offset, Is.EqualTo(b.Pieces[i].Offset));
					Assert.That(a.Pieces[i].From, Is.EqualTo(b.Pieces[i].From));
				}
				Assert.That(a.Request.Stairs.Count, Is.EqualTo(b.Request.Stairs.Count));
			}
		}

		// ── The pieces ───────────────────────────────────────────

		[Test]
		public void AWalkedModuleIsLaidFromJointToJointWithItsFacesUpright()
		{
			var piece = new PointOfInterestLayoutPiece { Offset = new Vector2(0.1f, 0f), Yaw = 0f, Footprint = new Vector2(6.2f, 2.5f), From = new Vector2(-3f, 0f), To = new Vector2(3f, 0f) };
			Vector3 from = new Vector3(-3f, 10f, 0f), to = new Vector3(3f, 10.6f, 0f);
			(Vector3 position, Quaternion rotation, float stretch) = PropsFeature.CurtainFrame(from, to, piece, 6f, 0f);
			Vector3 x = rotation * Vector3.right, z = rotation * Vector3.forward;
			Assert.That(Vector3.Angle(x, to - from), Is.LessThan(0.05f), "its x along the chord");
			Assert.That(Mathf.Abs(z.y), Is.LessThan(1e-3f), "its faces upright: its z level");
			Assert.That(z.z, Is.GreaterThan(0.999f), "its −z out as laid");
			// Its own ends (less the run on past a corner) land on the joints.
			float extra = piece.Footprint.x - 6f;
			Vector3 a = position + rotation * new Vector3(-0.5f * (6f * stretch - extra) - 0.1f, 0f, 0f);
			Vector3 b = position + rotation * new Vector3(0.5f * (6f * stretch - extra) - 0.1f, 0f, 0f);
			Assert.That(Vector3.Distance(a, from), Is.LessThan(0.01f), $"its −x end on its joint: {a}");
			Assert.That(Vector3.Distance(b, to), Is.LessThan(0.01f), $"its +x end on its joint: {b}");
		}

		[Test]
		public void AWallStairClimbsInRisersToALandingLevelWithTheWalk()
		{
			float walk = StructurePieces.CurtainWalk("WallSegment", 0);
			Assert.That(walk, Is.GreaterThan(4f), "the curtain's walk");
			Assert.That(StructurePieces.CurtainWalk("WallSegment", 1), Is.EqualTo(0f), "the coped wall has none");
			Assert.That(StructurePieces.CurtainWalk("PalisadeSegment", 0), Is.EqualTo(0f), "a palisade has none");
			Assert.That(walk / StructurePieces.WallStairRisers, Is.LessThanOrEqualTo(0.2f + 1e-4f), "risers of 20 cm at most");
			Assert.That(StructurePieces.CurtainWalkWidth, Is.GreaterThanOrEqualTo(1.5f), "1.5 m clear behind the parapet");
			Assert.That(StructurePieces.WallStairLanding, Is.GreaterThanOrEqualTo(StructurePieces.WallStairWidth), "a landing as deep as the flight is wide");
			foreach (int variant in new[] { 0, 1 })
			{
				StructurePiece stair = StructureKit.Get("WallStair");
				Assert.That(stair.Footprint.x, Is.EqualTo(StructurePieces.WallStairLength).Within(0.05f), "its length");
				Assert.That(stair.Footprint.y, Is.EqualTo(StructurePieces.WallStairWidth).Within(0.05f), "its width, its back on the wall");
				Assert.That(stair.Height, Is.GreaterThanOrEqualTo(walk + 0.9f), $"v{variant}: a rail round its landing");
			}
		}
	}
}
