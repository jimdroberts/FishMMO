using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Openings in great chambers' roofs up to the sky (CaveSystem.Skylights.cs, 2026-10-20, caves livened): planned only where
	/// the rock over a vault is thin, open from the floor to the sky, holed in the terrain over the opening and nowhere else,
	/// ringed by a lip that stands proud of the ground, never a way in (no landing, no own mouth for the ways and the map, no
	/// graded approach), stored and read back exactly, the same each time, and leaving the rest of the cave as it was. The light's
	/// shared maths (SceneSkylight) too. On a low plateau over an escarpment's foot (a lava tube under a plain, a cave under a low
	/// hill), where roofs are thin enough to fall in.
	/// </summary>
	[TestFixture]
	public class CaveSkylightTests
	{
		/// <summary>The terrain's hole grid at 1200 m tiles and 512 holes a tile: 2.34 m quads from the scene's corner.</summary>
		private const float HoleStep = 1200f / 512f, GridX = -2400f, GridZ = -2400f;
		private const float Lift = 60f;

		/// <summary>A face rising <paramref name="rise"/> metres over 30 m behind the mouth, then a gently rolling plateau.</summary>
		private static Func<float, float, float> Plateau(float tiltDegrees, int variant, float rise)
		{
			float a = tiltDegrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
			return (x, z) =>
			{
				float u = c * z - s * x, w = s * z + c * x;
				float t = Mathf.Clamp01(u / 30f);
				float face = 10f + rise * (t * t * (3f - 2f * t));
				float roll = 2f * Mathf.Sin(w * 0.04f + variant) + 1.5f * Mathf.Sin(u * 0.027f + 0.7f * variant) * Mathf.Clamp01(u / 40f);
				return Lift + face + roll * Mathf.Clamp01((u + 10f) / 30f) - 0.04f * Mathf.Max(0f, -u);
			};
		}

		private static CaveSystem PlanCave(CaveForm form, int size, int seed, float rise, out Func<float, float, float> g)
		{
			float tilt = seed * 13f % 40f - 20f;
			g = Plateau(tilt, seed, rise);
			return CaveSystem.Plan(form, size, new Vector3(0f, 0f, -1f), Mathf.Repeat(180f - tilt, 360f), seed * 7919, seed,
				CaveGround.Sample(g, -560f, -560f, 560f, 560f, 2f), 0f, out _);
		}

		private sealed class Case
		{
			public CaveSystem System, Bare;
			public Func<float, float, float> Ground;
			public int Seed;
			public string What => $"{System.Form} {System.SizeClass} seed {Seed} ({System.Character})";
		}

		private static List<Case> cases;
		private static int planned;

		/// <summary>Lava tubes, caves and ice caves of sizes 1 and 2 over a few seeds and two plateau heights, each with its skylights opened (and a bare twin).</summary>
		private static List<Case> Cases()
		{
			if (cases != null)
			{
				return cases;
			}
			cases = new List<Case>();
			foreach (CaveForm form in new[] { CaveForm.LavaTube, CaveForm.Cave, CaveForm.IceCave })
			{
				foreach (int size in new[] { 1, 2 })
				{
					for (int seed = 1; seed <= 8; seed++)
					{
						float rise = seed % 2 == 0 ? 14f : 24f;
						CaveSystem system = PlanCave(form, size, seed, rise, out Func<float, float, float> g);
						if (system == null)
						{
							continue;
						}
						planned++;
						CaveGround ground = CaveGround.Sample(g, -700f, -700f, 700f, 700f, 2f);
						int added = system.TryAddSkylights(ground, seed, seed * 31 + 7, null, out _);
						if (added > 0)
						{
							cases.Add(new Case { System = system, Bare = PlanCave(form, size, seed, rise, out _), Ground = g, Seed = seed });
						}
					}
				}
			}
			Console.WriteLine($"{cases.Count} of {planned} planned systems opened skylights");
			return cases;
		}

		[Test]
		public void SomeSystemsOpenSkylightsAndNoneTooMany()
		{
			List<Case> all = Cases();
			Assert.That(all.Count, Is.GreaterThan(0), "no system opened a skylight on a low plateau");
			foreach (Case c in all)
			{
				List<int> skylights = c.System.Skylights();
				Assert.That(skylights.Count, Is.InRange(1, CaveSystem.MostSkylights(c.System.SizeClass)), c.What);
				Assert.That(CaveSystem.SkylightShare(c.System.Form), Is.GreaterThan(0f), c.What);
			}
			Assert.That(CaveSystem.SkylightShare(CaveForm.Mine), Is.Zero);
			Assert.That(CaveSystem.SkylightShare(CaveForm.Grotto), Is.Zero);
			Assert.That(CaveSystem.SkylightShare(CaveForm.SeaCave), Is.Zero);
		}

		[Test]
		public void EachOpensFromAGreatChambersFloorUpToTheSky()
		{
			foreach (Case c in Cases())
			{
				CaveSystem s = c.System;
				foreach (int m in s.Skylights())
				{
					CaveMouth mouth = s.Mouths[m];
					Vector3 top = CaveSystem.SkylightOpening(mouth);
					string what = $"{c.What}: skylight {m}";
					Assert.That(mouth.Kind, Is.EqualTo(CaveMouthKind.Skylight), what);
					Assert.That(mouth.Radius, Is.InRange(CaveSystem.SkylightMinRadius - 1e-3f, CaveSystem.SkylightMaxRadius + 1e-3f), what);
					Assert.That(mouth.Zone, Is.EqualTo(mouth.Radius), what);
					Assert.That(new Vector2(top.x - mouth.Floor.x, top.z - mouth.Floor.z).magnitude, Is.LessThan(1e-3f), $"{what}: its opening is over its floor");
					float ground = c.Ground(s.Origin.x + top.x, s.Origin.z + top.z) - s.Origin.y;
					Assert.That(top.y, Is.EqualTo(ground).Within(0.6f), $"{what}: its opening is on the ground");
					float vault = CaveSystem.SkylightVault(mouth);
					Assert.That(vault - mouth.Floor.y, Is.GreaterThanOrEqualTo(CaveSystem.SkylightMinVault - 0.3f), $"{what}: a vault over it");
					Assert.That(top.y - vault, Is.InRange(CaveSystem.MinCover - 0.6f, CaveSystem.SkylightMaxCover + 0.6f), $"{what}: thin rock over the vault");
					// Open all the way up its axis, from a metre over the floor to the ground: the sky is seen from the floor.
					for (float y = mouth.Floor.y + 1f; y <= top.y + 0.5f; y += 0.5f)
					{
						Assert.That(s.Void(new Vector3(mouth.Floor.x, y, mouth.Floor.z)), Is.LessThan(0f), $"{what}: rock across its throat {y - mouth.Floor.y:0.#} m over the floor");
					}
					// And a floor under it.
					Assert.That(s.Void(new Vector3(mouth.Floor.x, mouth.Floor.y - 0.4f, mouth.Floor.z)), Is.GreaterThanOrEqualTo(0f), $"{what}: no floor under it");
					// Clear of every way in.
					for (int o = 0; o < s.Mouths.Count; o++)
					{
						if (o != m)
						{
							float d = new Vector2(mouth.Floor.x - s.Mouths[o].Floor.x, mouth.Floor.z - s.Mouths[o].Floor.z).magnitude;
							Assert.That(d, Is.GreaterThan(CaveSystem.SkylightClear), $"{what}: {d:0} m from mouth {o}");
						}
					}
				}
			}
		}

		[Test]
		public void TheTerrainIsHoledOverTheOpeningAlone()
		{
			foreach (Case c in Cases())
			{
				CaveSystem s = c.System;
				foreach (int m in s.Skylights())
				{
					CaveMouth mouth = s.Mouths[m];
					Vector3 top = s.Origin + CaveSystem.SkylightOpening(mouth);
					List<Vector2Int> cells = s.HoleCells(m, c.Ground, HoleStep, GridX, GridZ);
					string what = $"{c.What}: skylight {m}";
					Assert.That(cells.Count, Is.GreaterThan(0), $"{what}: nothing holed over it");
					bool axis = false;
					float reach = mouth.Radius * (1f + CaveSystem.SkylightFlare) + s.HoleMargin + s.Amp + 2f * HoleStep;
					foreach (Vector2Int cell in cells)
					{
						var centre = new Vector2(GridX + (cell.x + 0.5f) * HoleStep, GridZ + (cell.y + 0.5f) * HoleStep);
						float d = (centre - new Vector2(top.x, top.z)).magnitude;
						axis |= d < HoleStep;
						Assert.That(d, Is.LessThan(reach), $"{what}: a hole {d:0.#} m from its opening's middle");
					}
					Assert.That(axis, Is.True, $"{what}: its middle is not holed");
					Assert.That(s.Mouths[m].Collar, Is.GreaterThan(0f), what);
				}
			}
		}

		[Test]
		public void ItsLipStandsProudOfTheGroundAllRound()
		{
			foreach (Case c in Cases())
			{
				CaveSystem s = c.System;
				foreach (int m in s.Skylights())
				{
					s.HoleCells(m, c.Ground, HoleStep, GridX, GridZ);
				}
				Bounds e = s.Extent;
				CaveGround ground = CaveGround.Sample(c.Ground, s.Origin.x + e.min.x - 4f, s.Origin.z + e.min.z - 4f, s.Origin.x + e.max.x + 4f, s.Origin.z + e.max.z + 4f, 1f);
				foreach (int m in s.Skylights())
				{
					CaveMouth mouth = s.Mouths[m];
					int ringed = 0;
					const int Angles = 16;
					for (int k = 0; k < Angles; k++)
					{
						float a = k * 2f * Mathf.PI / Angles;
						for (float r = 0.5f * mouth.Radius; r <= mouth.Radius * (1f + CaveSystem.SkylightFlare) + 4f; r += 0.2f)
						{
							float x = mouth.Floor.x + Mathf.Cos(a) * r, z = mouth.Floor.z + Mathf.Sin(a) * r;
							float g = c.Ground(s.Origin.x + x, s.Origin.z + z) - s.Origin.y;
							if (s.Field(new Vector3(x, g + 0.6f, z), ground) < 0f)
							{
								ringed++;
								break;
							}
						}
					}
					Assert.That(ringed, Is.GreaterThanOrEqualTo(Angles - 2), $"{c.What}: skylight {m}: rock stands over the ground at its lip on only {ringed} of {Angles} bearings");
				}
			}
		}

		[Test]
		public void ASkylightIsNoWayIn()
		{
			foreach (Case c in Cases())
			{
				CaveSystem s = c.System;
				var shape = new PointOfInterestShape { SiteId = c.Seed, Shaper = CaveShaper.ShaperName, Position = s.Origin, Values = s.ToValues(), Seed = s.Seed };
				List<CaveMouth> own = CaveShaper.OwnMouths(new[] { shape }, c.Seed);
				Assert.That(own.Exists(m => m.Kind == CaveMouthKind.Skylight), Is.False, $"{c.What}: a skylight among the site's own mouths (the ways' ends)");
				Assert.That(own.Count, Is.EqualTo(s.Mouths.Count - s.Skylights().Count), c.What);
				Assert.That(CaveShaper.LandableMouths(new[] { shape }, c.Seed), Is.EqualTo(own.FindAll(m => CaveSystem.LandingOf(m, out _)).Count), c.What);
				// The ways are routed round each as a pit, its middle over its floor.
				List<(Vector3 Middle, float Radius)> pits = CaveShaper.SkylightPits(new[] { shape });
				Assert.That(pits.Count, Is.EqualTo(s.Skylights().Count), c.What);
				foreach ((Vector3 middle, float radius) in pits)
				{
					Assert.That(radius, Is.GreaterThan(CaveSystem.SkylightMinRadius), c.What);
					Assert.That(s.Skylights().Exists(m => (s.Origin + CaveSystem.SkylightOpening(s.Mouths[m]) - middle).sqrMagnitude < 1e-4f), Is.True, c.What);
				}
				foreach (int m in s.Skylights())
				{
					Assert.That(CaveSystem.LandingOf(s.Mouths[m], out _), Is.False, c.What);
					Assert.That(PointOfInterestEntrances.IsWayIn(s.Mouths[m]), Is.False, c.What);
					Assert.That(s.GradesApproachOf(m), Is.False, c.What);
				}
			}
		}

		[Test]
		public void ItIsStoredAndReadBackExactly()
		{
			foreach (Case c in Cases())
			{
				CaveSystem s = c.System;
				CaveSystem back = CaveSystem.FromValues(s.ToValues(), s.Origin, s.Seed);
				Assert.That(back, Is.Not.Null, c.What);
				Assert.That(back.Skylights(), Is.EqualTo(s.Skylights()), c.What);
				Assert.That(back.ToValues(), Is.EqualTo(s.ToValues()), c.What);
				foreach (int m in s.Skylights())
				{
					CaveMouth mouth = s.Mouths[m];
					for (int k = 0; k < 24; k++)
					{
						var p = new Vector3(mouth.Floor.x + (k % 5 - 2) * 0.37f * mouth.Radius, Mathf.Lerp(mouth.Floor.y, mouth.Landing.y, k / 23f), mouth.Floor.z + (k % 3 - 1) * 0.41f * mouth.Radius);
						Assert.That(back.Void(p), Is.EqualTo(s.Void(p)), $"{c.What}: the throat read back differs");
					}
				}
			}
		}

		[Test]
		public void TheSameSkylightsEachTime()
		{
			foreach (Case c in Cases())
			{
				// Two fresh plans (the fixture's own have had their holes reckoned, which sets their collars).
				CaveSystem one = PlanCave(c.System.Form, c.System.SizeClass, c.Seed, c.Seed % 2 == 0 ? 14f : 24f, out Func<float, float, float> g);
				CaveSystem two = PlanCave(c.System.Form, c.System.SizeClass, c.Seed, c.Seed % 2 == 0 ? 14f : 24f, out _);
				one.TryAddSkylights(CaveGround.Sample(g, -700f, -700f, 700f, 700f, 2f), c.Seed, c.Seed * 31 + 7, null, out _);
				two.TryAddSkylights(CaveGround.Sample(g, -700f, -700f, 700f, 700f, 2f), c.Seed, c.Seed * 31 + 7, null, out _);
				Assert.That(one.Skylights().Count, Is.EqualTo(c.System.Skylights().Count), c.What);
				Assert.That(two.ToValues(), Is.EqualTo(one.ToValues()), c.What);
			}
		}

		[Test]
		public void TheRestOfTheCaveIsAsItWas()
		{
			foreach (Case c in Cases())
			{
				if (c.System.Form == CaveForm.LavaTube)
				{
					// A molten tube's lava keeps off the lit floor (its plan reads the mouths), so its beds may move.
					continue;
				}
				CaveSystem s = c.System, bare = c.Bare;
				List<int> skylights = s.Skylights();
				int compared = 0;
				foreach (CavePassage passage in bare.Passages)
				{
					foreach (CaveNode n in passage.Nodes)
					{
						for (int k = 0; k < 3; k++)
						{
							Vector3 p = n.Centre + new Vector3(0.3f * k, 0.5f * k - 0.5f, -0.2f * k);
							bool near = false;
							foreach (int m in skylights)
							{
								CaveMouth mouth = s.Mouths[m];
								float reach = mouth.Radius * (1f + CaveSystem.SkylightFlare) + 3f * s.Radius + 4f;
								near |= new Vector2(p.x - mouth.Floor.x, p.z - mouth.Floor.z).magnitude < reach;
							}
							if (near)
							{
								continue;
							}
							compared++;
							Assert.That(s.Void(p), Is.EqualTo(bare.Void(p)), $"{c.What}: the void changed away from its skylights");
						}
					}
				}
				Assert.That(compared, Is.GreaterThan(0), c.What);
			}
		}

		[Test]
		public void MouthDistanceRunsUpTheThroat()
		{
			var ground = new CaveMouth { Floor = new Vector3(1f, 2f, 3f), Kind = CaveMouthKind.Ground };
			Assert.That(CaveSystem.MouthDistance(ground, new Vector3(1f, 12f, 3f)), Is.EqualTo(10f).Within(1e-4f));
			var sky = new CaveMouth { Floor = new Vector3(0f, -30f, 0f), Kind = CaveMouthKind.Skylight, Water = -20f, Landing = new Vector3(0f, 0f, 0f), Radius = 4f };
			Assert.That(CaveSystem.MouthDistance(sky, new Vector3(5f, -10f, 0f)), Is.EqualTo(5f).Within(1e-4f), "beside the throat");
			Assert.That(CaveSystem.MouthDistance(sky, new Vector3(0f, 3f, 0f)), Is.EqualTo(3f).Within(1e-4f), "over the opening");
			Assert.That(CaveSystem.MouthDistance(sky, new Vector3(0f, -28f, 0f)), Is.EqualTo(8f).Within(1e-4f), "under the vault");
		}

		[Test]
		public void TheLightsSeedCarriesTheOpeningsHeight()
		{
			foreach (float h in new[] { 0f, 3.4f, 17.25f, 44.9f, 300f })
			{
				uint seed = SceneSkylight.Seed(h, 0xABCDEu);
				Assert.That(SceneSkylight.Height(seed), Is.EqualTo(h).Within(0.051f));
				var e = new SceneEmitter { Position = new Vector3(5f, -40f, 7f), Kind = SceneEmitterKind.Skylight, Seed = seed, Reach = 4.5f };
				Assert.That(SceneSkylight.Opening(e).y, Is.EqualTo(-40f + h).Within(0.051f));
				Assert.That(SceneSkylight.Radius(e), Is.EqualTo(4.5f));
			}
			Assert.That(SceneSkylight.Seed(10f, 1u) >> SceneSkylight.HeightBits, Is.EqualTo(1u), "the hash is kept over the height");
		}

		[Test]
		public void TheSkysShareAndTheBeamAreWhereTheOpeningIs()
		{
			var opening = new Vector3(0f, 0f, 0f);
			const float R = 5f;
			Assert.That(SceneSkylight.SkyShare(opening, R, new Vector3(0f, -0.01f, 0f)), Is.EqualTo(1f).Within(1e-3f), "in the opening, all the sky");
			float floor = SceneSkylight.SkyShare(opening, R, new Vector3(0f, -20f, 0f));
			Assert.That(floor, Is.EqualTo(SceneSkylight.SkyBoost * R * R / (R * R + 400f)).Within(1e-4f), "under it, the boosted view factor");
			Assert.That(SceneSkylight.SkyShare(opening, R, new Vector3(30f, -20f, 0f)), Is.LessThan(0.2f * floor), "off to the side, far less");
			Assert.That(SceneSkylight.SkyShare(opening, R, new Vector3(0f, 2f, 0f)), Is.Zero, "over it, not its to give");

			Vector3 noon = Vector3.up, slant = new Vector3(0.6f, 0.8f, 0f);
			Assert.That(SceneSkylight.InBeam(opening, R, noon, new Vector3(1f, -20f, 1f)), Is.EqualTo(1f), "straight under a noon sun");
			Assert.That(SceneSkylight.InBeam(opening, R, noon, new Vector3(9f, -20f, 0f)), Is.Zero, "beside the beam");
			// A slanting sun lands its beam off to the side away from it: 20 m down, 15 m over.
			Assert.That(SceneSkylight.InBeam(opening, R, slant, new Vector3(-15f, -20f, 0f)), Is.EqualTo(1f).Within(1e-4f), "the slanting beam's patch");
			Assert.That(SceneSkylight.InBeam(opening, R, slant, new Vector3(0f, -20f, 0f)), Is.Zero, "not straight under it then");
			Assert.That(SceneSkylight.InBeam(opening, R, new Vector3(0.99f, 0.01f, 0f).normalized, new Vector3(0f, -1f, 0f)), Is.Zero, "no beam from a sun on the horizon");
			Assert.That(SceneSkylight.BeamOnFloor(20f, R, noon), Is.EqualTo(1f));
			Assert.That(SceneSkylight.BeamOnFloor(40f, R, slant), Is.Zero, "a deep throat's floor gets no slanting sun");
		}

		[Test]
		public void TheShadersMirrorTheSharedMathsAndTheClientIsWired()
		{
			const string shaders = "Assets/Prefabs/Client/Weather/Shaders/";
			string include = System.IO.File.ReadAllText(shaders + "FishSkylights.hlsl");
			float Define(string name)
			{
				var match = System.Text.RegularExpressions.Regex.Match(include, @"#define\s+" + name + @"\s+([0-9.]+)");
				Assert.That(match.Success, Is.True, $"FishSkylights.hlsl defines {name}");
				return float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
			}
			Assert.That(Define("FISH_SKYLIGHTS"), Is.EqualTo(SceneSkylight.MaxNear));
			Assert.That(Define("FISH_SKYLIGHT_SKY_BOOST"), Is.EqualTo(SceneSkylight.SkyBoost));
			Assert.That(Define("FISH_SKYLIGHT_SKY_OFF_AXIS"), Is.EqualTo(SceneSkylight.SkyOffAxis));
			Assert.That(Define("FISH_SKYLIGHT_BEAM_INNER"), Is.EqualTo(SceneSkylight.BeamInner));
			Assert.That(Define("FISH_SKYLIGHT_BEAM_OUTER"), Is.EqualTo(SceneSkylight.BeamOuter));
			Assert.That(Define("FISH_SKYLIGHT_LOWEST_SUN"), Is.EqualTo(SceneSkylight.LowestSun));
			Assert.That(System.IO.File.ReadAllText(shaders + "FishSkylightShafts.shader"), Does.Contain($"#define SLOTS {SceneSkylight.MaxNear}"));
			Assert.That(System.IO.File.ReadAllText(shaders + "FishGroundMap.hlsl"), Does.Contain("#include \"FishSkylights.hlsl\"").And.Contain("FishSkylightSky(positionWS)"));
			Assert.That(System.IO.File.ReadAllText(shaders + "FishWeatherLitForwardPass.hlsl"), Does.Contain("FishSkylightSun(inputData.positionWS, fishSun.direction)"));
			const string client = "Assets/Scripts/Client/World/";
			Assert.That(System.IO.File.ReadAllText(client + "Terrain/SiteLightPool.cs"), Does.Contain("case SceneEmitterKind.Skylight:"));
			Assert.That(System.IO.File.ReadAllText(client + "Weather/Sky/SkySystem.cs"), Does.Contain("CaveSkylights.OpenAt(eye)"));
			Assert.That(System.IO.File.ReadAllText(client + "Terrain/CaveSkylights.cs"), Does.Contain("SetGlobalVectorArray(SkylightsId"));
		}

		[Test]
		public void TheThroatMeshesClosedAndItsPiecesAreAMouths()
		{
			int meshed = 0;
			foreach (Case c in Cases())
			{
				if (meshed >= 2)
				{
					break;
				}
				CaveSystem s = c.System;
				for (int m = 0; m < s.Mouths.Count; m++)
				{
					s.HoleCells(m, c.Ground, HoleStep, GridX, GridZ);
				}
				Bounds e = s.Extent;
				CaveGround ground = CaveGround.Sample(c.Ground, s.Origin.x + e.min.x - 4f, s.Origin.z + e.min.z - 4f, s.Origin.x + e.max.x + 4f, s.Origin.z + e.max.z + 4f, HoleStep, GridX, GridZ);
				List<CaveMesher.Piece> pieces = CaveMesher.Build(s, ground, out _, true);
				var all = new MeshBuilder(1);
				foreach (CaveMesher.Piece piece in pieces)
				{
					all.Append(piece.Mesh);
				}
				(int odd, _) = OpenEdges(all);
				Assert.That(odd, Is.Zero, $"{c.What}: edges on an odd number of faces");
				foreach (int sky in s.Skylights())
				{
					Vector3 top = CaveSystem.SkylightOpening(s.Mouths[sky]);
					bool lit = false;
					foreach (CaveMesher.Piece piece in pieces)
					{
						lit |= piece.Mouth && piece.Mesh.TriangleCount > 0 && Near(piece, top, 40f);
					}
					Assert.That(lit, Is.True, $"{c.What}: no piece round skylight {sky}'s opening is a mouth's (cast shadows, always drawn)");
				}
				meshed++;
			}
			Assert.That(meshed, Is.GreaterThan(0));
		}

		private static bool Near(CaveMesher.Piece piece, Vector3 p, float reach)
		{
			foreach (Vector3 v in piece.Mesh.Positions)
			{
				if ((v - p).sqrMagnitude < reach * reach)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Edges on an odd number of faces, and on four or more (vertices welded by position), as PointOfInterestCaveTests counts them.</summary>
		private static (int odd, int pinched) OpenEdges(MeshBuilder m)
		{
			var weld = new Dictionary<Vector3, int>();
			var use = new Dictionary<(int, int), int>();
			List<int> t = m.Submeshes[0];
			int Id(int v)
			{
				if (!weld.TryGetValue(m.Positions[v], out int id))
				{
					weld[m.Positions[v]] = id = weld.Count;
				}
				return id;
			}
			for (int i = 0; i < t.Count; i += 3)
			{
				int a = Id(t[i]), b = Id(t[i + 1]), c = Id(t[i + 2]);
				foreach ((int x, int y) in new[] { (a, b), (b, c), (c, a) })
				{
					var key = x < y ? (x, y) : (y, x);
					use[key] = use.TryGetValue(key, out int n) ? n + 1 : 1;
				}
			}
			int odd = 0, pinched = 0;
			foreach (int n in use.Values)
			{
				odd += n % 2;
				pinched += n >= 4 ? 1 : 0;
			}
			return (odd, pinched);
		}
	}
}
