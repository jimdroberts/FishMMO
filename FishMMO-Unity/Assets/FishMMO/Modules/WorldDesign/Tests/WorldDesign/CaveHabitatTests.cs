using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The cave habitat (<see cref="CaveHabitat"/>, 2026-10-20) on caves planned on a synthetic escarpment: the same system gives the
	/// same field; every value is 0..1; sediment lies on floors and never on walls or roofs; the daylight falls off along the way in
	/// from the mouth and is gone deep in; the lanes are worn and the floor beside them is not; a lava tube does not glow and a
	/// limestone cave does; the mesher writes the field to every vertex of every piece (COLOR and UV channel 3); and the rock
	/// lookup (<see cref="CaveRockLayers"/>) gives each form its grounds.
	/// </summary>
	[TestFixture]
	public class CaveHabitatTests
	{
		/// <summary>A foot at 70 m, a face rising 60 m over 30 m, a rolling plateau behind (as PointOfInterestCaveTests' escarpment, lifted).</summary>
		private static float Escarpment(float x, float z)
		{
			float t = Mathf.Clamp01(z / 30f);
			float face = 70f + 60f * (t * t * (3f - 2f * t));
			float roll = 3f * Mathf.Sin(x * 0.05f + 1f) + 2f * Mathf.Sin(z * 0.031f + 0.7f) * Mathf.Clamp01(z / 40f);
			return face + roll * Mathf.Clamp01((z + 10f) / 30f) - 0.04f * Mathf.Max(0f, -z);
		}

		private static CaveGround Patch() => CaveGround.Sample(Escarpment, -560f, -560f, 560f, 560f, 2f);

		private static CaveSystem Plan(CaveForm form, int size, int seed)
		{
			CaveSystem system = CaveSystem.Plan(form, size, new Vector3(0f, 0f, -1f), 180f, seed * 7919, seed, Patch(), 0f, out string problem);
			Assert.That(system, Is.Not.Null, $"{form} size {size} seed {seed} was refused: {problem}");
			return system;
		}

		/// <summary>Points on the system's floors (up), walls (sideways) and roofs (down), from every passage node.</summary>
		private static IEnumerable<(Vector3 p, Vector3 n, string where)> Probes(CaveSystem system)
		{
			foreach (CavePassage passage in system.Passages)
			{
				for (int i = 0; i + 1 < passage.Nodes.Count; i++)
				{
					CaveNode a = passage.Nodes[i], b = passage.Nodes[i + 1];
					Vector3 run = b.Centre - a.Centre;
					var side = new Vector3(run.z, 0f, -run.x);
					side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.right;
					yield return (new Vector3(a.Centre.x, a.Floor, a.Centre.z), Vector3.up, "floor");
					yield return (a.Centre + side * a.Radius, -side, "wall");
					yield return (new Vector3(a.Centre.x, a.Centre.y + a.Radius * a.Height, a.Centre.z), Vector3.down, "roof");
				}
			}
		}

		private static void AssertSame(in CaveHabitatSample x, in CaveHabitatSample y, string label)
		{
			Assert.That(x.Wet, Is.EqualTo(y.Wet), label + " wet");
			Assert.That(x.Daylight, Is.EqualTo(y.Daylight), label + " daylight");
			Assert.That(x.Sediment, Is.EqualTo(y.Sediment), label + " sediment");
			Assert.That(x.Trodden, Is.EqualTo(y.Trodden), label + " trodden");
			Assert.That(x.Organic, Is.EqualTo(y.Organic), label + " organic");
			Assert.That(x.Glow, Is.EqualTo(y.Glow), label + " glow");
		}

		[Test]
		public void TheSameSystemGivesTheSameHabitat()
		{
			CaveSystem a = Plan(CaveForm.Cave, 1, 3), b = Plan(CaveForm.Cave, 1, 3);
			CaveHabitat ha = CaveHabitat.For(a), hb = CaveHabitat.For(b), again = CaveHabitat.For(a);
			int n = 0;
			foreach ((Vector3 p, Vector3 normal, string where) in Probes(a))
			{
				CaveHabitatSample sa = ha.Sample(p, normal);
				AssertSame(sa, hb.Sample(p, normal), $"{where} at {p} (replanned)");
				AssertSame(sa, again.Sample(p, normal), $"{where} at {p} (measured again)");
				n++;
			}
			Assert.That(n, Is.GreaterThan(30), "probed");
		}

		[TestCase(CaveForm.Cave, 1, 2)]
		[TestCase(CaveForm.Cave, 2, 5)]
		[TestCase(CaveForm.LavaTube, 1, 4)]
		[TestCase(CaveForm.Mine, 1, 6)]
		[TestCase(CaveForm.IceCave, 1, 7)]
		public void EveryValueIsBetweenZeroAndOne(CaveForm form, int size, int seed)
		{
			CaveSystem system = Plan(form, size, seed);
			CaveHabitat habitat = CaveHabitat.For(system);
			foreach ((Vector3 p, Vector3 normal, string where) in Probes(system))
			{
				CaveHabitatSample s = habitat.Sample(p, normal);
				foreach ((float v, string name) in new[] { (s.Wet, "wet"), (s.Daylight, "daylight"), (s.Sediment, "sediment"), (s.Trodden, "trodden"), (s.Organic, "organic"), (s.Glow, "glow") })
				{
					Assert.That(float.IsNaN(v), Is.False, $"{form}: {name} at the {where} {p} is NaN");
					Assert.That(v, Is.InRange(0f, 1f), $"{form}: {name} at the {where} {p}");
				}
			}
		}

		[Test]
		public void SedimentLiesOnFloorsAndNeverOnWallsOrRoofs()
		{
			CaveSystem system = Plan(CaveForm.Cave, 1, 2);
			CaveHabitat habitat = CaveHabitat.For(system);
			int floors = 0, silted = 0;
			foreach ((Vector3 p, Vector3 normal, string where) in Probes(system))
			{
				CaveHabitatSample s = habitat.Sample(p, normal);
				if (where == "floor")
				{
					floors++;
					silted += s.Sediment > 0.1f ? 1 : 0;
				}
				else
				{
					Assert.That(s.Sediment, Is.EqualTo(0f), $"sediment on the {where} at {p}");
					Assert.That(s.Trodden, Is.EqualTo(0f), $"a worn way on the {where} at {p}");
				}
			}
			Assert.That(silted, Is.GreaterThan(floors * 3 / 4), $"{silted} of {floors} floor points carry sediment");
		}

		[Test]
		public void TheDaylightFallsOffAlongTheWayIn()
		{
			CaveSystem system = Plan(CaveForm.Cave, 1, 2);
			CaveHabitat habitat = CaveHabitat.For(system);
			List<CaveNode> trunk = system.Passages[0].Nodes;
			float last = float.MaxValue, at0 = -1f;
			foreach (CaveNode node in trunk)
			{
				var floor = new Vector3(node.Centre.x, node.Floor, node.Centre.z);
				float day = habitat.Sample(floor, Vector3.up).Daylight;
				if (at0 < 0f)
				{
					at0 = day;
				}
				Assert.That(day, Is.LessThanOrEqualTo(last + 0.02f), $"daylight rises again {node.Along:0.0} m in");
				last = Mathf.Min(last, day);
				if (node.Along > 50f)
				{
					Assert.That(day, Is.EqualTo(0f), $"daylight {node.Along:0.0} m in");
				}
				Assert.That(habitat.WalkedFromMouth(floor), Is.GreaterThanOrEqualTo(node.Along * 0.7f - 4f), $"walked distance {node.Along:0.0} m in");
			}
			Assert.That(at0, Is.GreaterThan(0.8f), "the mouth's floor is in the day");
			// Deep in, nothing of the day anywhere: every chamber's floor past the twilight is dark.
			foreach (CaveChamber chamber in system.Chambers)
			{
				var p = new Vector3(chamber.Centre.x, chamber.Floor, chamber.Centre.z);
				if (habitat.WalkedFromMouth(p) > 60f)
				{
					Assert.That(habitat.Sample(p, Vector3.up).Daylight, Is.EqualTo(0f), $"a {chamber.Kind} {habitat.WalkedFromMouth(p):0} m in");
				}
			}
		}

		[Test]
		public void TheLanesAreWornAndTheFloorBesideThemIsNot()
		{
			CaveSystem system = Plan(CaveForm.Cave, 1, 2);
			CaveHabitat habitat = CaveHabitat.For(system);
			int on = 0, worn = 0, off = 0, bare = 0;
			foreach (CavePassage passage in system.Passages)
			{
				for (int i = 1; i + 1 < passage.Nodes.Count; i++)
				{
					CaveNode a = passage.Nodes[i], b = passage.Nodes[i + 1];
					var mid = new Vector3((a.Centre.x + b.Centre.x) * 0.5f, (a.Floor + b.Floor) * 0.5f, (a.Centre.z + b.Centre.z) * 0.5f);
					on++;
					worn += habitat.Sample(mid, Vector3.up).Trodden > 0.5f ? 1 : 0;
					Vector3 run = b.Centre - a.Centre;
					Vector3 side = new Vector3(run.z, 0f, -run.x).normalized;
					float reach = CaveHabitat.LaneHalfWidth + 0.8f;
					if (Mathf.Min(a.Radius, b.Radius) > reach + 0.3f)
					{
						off++;
						bare += habitat.Sample(mid + side * reach, Vector3.up).Trodden < 0.05f ? 1 : 0;
					}
				}
			}
			Assert.That(worn, Is.GreaterThan(on * 9 / 10), $"{worn} of {on} lane points worn");
			Assume.That(off, Is.GreaterThan(5), "passages wide enough to step off the lane");
			// Junctions, hall ways and the lanes of passages side by side wear some floor beside a lane: most is bare.
			Assert.That(bare, Is.GreaterThan(off * 2 / 3), $"{bare} of {off} points beside a lane are bare");
		}

		[Test]
		public void AnIceCaveIsFrozenWithinItsColdZone()
		{
			CaveSystem ice = Plan(CaveForm.IceCave, 1, 7);
			CaveHabitat habitat = CaveHabitat.For(ice);
			int probed = 0;
			foreach (CaveNode node in ice.Passages[0].Nodes)
			{
				var floor = new Vector3(node.Centre.x, node.Floor, node.Centre.z);
				float walked = habitat.WalkedFromMouth(floor);
				// Past where rain and snowmelt blow in at the mouth, and short of the cold zone's edge (90 m, decision 51).
				if (walked > 25f && walked < 80f)
				{
					Assert.That(habitat.Sample(floor, Vector3.up).Wet, Is.LessThanOrEqualTo(0.1f), $"the floor {walked:0} m into an ice cave is frozen dry");
					probed++;
				}
			}
			Assume.That(probed, Is.GreaterThan(0), "the trunk runs through the cold zone");
		}

		[Test]
		public void ALavaTubeDoesNotGlowAndALimestoneCaveDoes()
		{
			CaveSystem lava = Plan(CaveForm.LavaTube, 1, 4), cave = Plan(CaveForm.Cave, 2, 5);
			CaveHabitat lh = CaveHabitat.For(lava), ch = CaveHabitat.For(cave);
			foreach ((Vector3 p, Vector3 normal, string where) in Probes(lava))
			{
				CaveHabitatSample s = lh.Sample(p, normal);
				Assert.That(s.Glow, Is.EqualTo(0f), $"glow in a lava tube's {where} at {p}");
				Assert.That(s.Wet, Is.LessThanOrEqualTo(0.75f), $"a lava tube's {where} at {p} is dry but where rain blows in");
			}
			float most = 0f;
			int glowing = 0, probes = 0;
			foreach ((Vector3 p, Vector3 normal, string _) in Probes(cave))
			{
				float g = ch.Sample(p, normal).Glow;
				most = Mathf.Max(most, g);
				glowing += g > 0.05f ? 1 : 0;
				probes++;
			}
			Assert.That(most, Is.GreaterThan(0.3f), "the limestone cave glows somewhere");
			Assert.That(glowing, Is.LessThan(probes), "but not everywhere: the glow is in patches");
		}

		[Test]
		public void TheMesherWritesTheHabitatOnEveryVertex()
		{
			CaveSystem system = Plan(CaveForm.Cave, 0, 2);
			Bounds e = system.Extent;
			CaveGround ground = CaveGround.Sample(Escarpment, system.Origin.x + e.min.x - 4f, system.Origin.z + e.min.z - 4f,
				system.Origin.x + e.max.x + 4f, system.Origin.z + e.max.z + 4f, 2f);
			List<CaveMesher.Piece> pieces = CaveMesher.Build(system, ground, out string report);
			Console.WriteLine(report);
			int vertices = 0, silted = 0, worn = 0;
			foreach (CaveMesher.Piece piece in pieces)
			{
				Assert.That(piece.Mesh.Colors.Count, Is.EqualTo(piece.Mesh.VertexCount), $"piece {piece.Key}: a colour per vertex");
				Assert.That(piece.Habitat.Count, Is.EqualTo(piece.Mesh.VertexCount), $"piece {piece.Key}: a UV{CaveHabitat.UvChannel} per vertex");
				for (int i = 0; i < piece.Mesh.VertexCount; i++)
				{
					Color32 c = piece.Mesh.Colors[i];
					silted += c.b > 40 ? 1 : 0;
					worn += c.a > 120 ? 1 : 0;
					Assert.That(piece.Habitat[i].x, Is.InRange(0f, 1f));
					Assert.That(piece.Habitat[i].y, Is.InRange(0f, 1f));
				}
				vertices += piece.Mesh.VertexCount;
			}
			Assert.That(silted, Is.GreaterThan(vertices / 20), $"{silted} of {vertices} vertices silted");
			Assert.That(worn, Is.GreaterThan(0), "some vertices on a worn way");
		}

		[Test]
		public void EachFormHasItsGrounds()
		{
			foreach (CaveForm form in (CaveForm[])Enum.GetValues(typeof(CaveForm)))
			{
				CaveRockLook look = CaveRockLayers.For(null, form);
				Assert.That(look.Silt.Family, Is.Not.Empty, $"{form} silt");
				Assert.That(look.Gravel.Family, Is.Not.Empty, $"{form} gravel");
				Assert.That(look.Organic.Family, Is.Not.Empty, $"{form} litter");
				Assert.That(look.Trodden.Family, Is.Not.Empty, $"{form} worn way");
				Assert.That(look.Key, Is.EqualTo(CaveRockLayers.For(null, form).Key), $"{form} look is stable");
			}
			Assert.That(CaveRockLayers.For(null, CaveForm.LavaTube).GlowStrength, Is.EqualTo(0f), "a lava tube's rock has no glow crust");
			Assert.That(CaveRockLayers.For(null, CaveForm.LavaTube).Gravel.Family, Is.EqualTo(Ground.Cinder));
			Assert.That(CaveRockLayers.For(null, CaveForm.Mine).Trodden.Family, Is.EqualTo(Ground.PathDryEarth));
			Assert.That(CaveRockLayers.For(null, CaveForm.Cave).Key, Is.Not.EqualTo(CaveRockLayers.For(null, CaveForm.Mine).Key), "looks differ by form");
		}
	}
}
