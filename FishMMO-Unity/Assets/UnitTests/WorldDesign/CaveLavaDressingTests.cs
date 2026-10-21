using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// A lava tube that still runs (2026-10-20, cave lava): its dressing (<see cref="CaveLavaDressing"/>) lays nothing for a cooled
	/// plan and the same every time for a molten one, its lava lights within the budget and spaced off each other and the glow's,
	/// nothing on the floor in the lava; its emitter kinds appended after every old one; the lava light's churn and seed
	/// (<see cref="SceneMagma"/>); the new glow art as large as the placer is told; the client reads the new kinds.
	/// </summary>
	[TestFixture]
	public class CaveLavaDressingTests
	{
		private const int ArtSeed = ProceduralArtCatalogue.DefaultSeed;

		/// <summary>A foot at 70 m, a face rising 60 m over 30 m, a rolling plateau behind (CaveLifeBakeTests' escarpment).</summary>
		private static Func<float, float, float> Escarpment(float tiltDegrees, int variant)
		{
			float a = tiltDegrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
			return (x, z) =>
			{
				float u = c * z - s * x, w = s * z + c * x;
				float t = Mathf.Clamp01(u / 30f);
				float face = 10f + 60f * (t * t * (3f - 2f * t));
				float roll = 3f * Mathf.Sin(w * 0.05f + variant) + 2f * Mathf.Sin(u * 0.031f + 0.7f * variant) * Mathf.Clamp01(u / 40f);
				return 60f + face + roll * Mathf.Clamp01((u + 10f) / 30f) - 0.04f * Mathf.Max(0f, -u);
			};
		}

		/// <summary>A lava tube, rebuilt from its stored shape as the dressing reads it.</summary>
		private static CaveSystem Tube(int seed, int size = 1)
		{
			float tilt = seed * 13f % 40f - 20f;
			CaveGround ground = CaveGround.Sample(Escarpment(tilt, seed), -560f, -560f, 560f, 560f, 2f);
			CaveSystem system = CaveSystem.Plan(CaveForm.LavaTube, size, new Vector3(0f, 0f, -1f), Mathf.Repeat(180f - tilt, 360f), seed * 7919, seed, ground, 0f, out string problem);
			Assert.That(system, Is.Not.Null, $"lava tube seed {seed} was refused: {problem}");
			return CaveSystem.FromValues(system.ToValues(), system.Origin, system.Seed);
		}

		/// <summary>
		/// A plan built by hand on a tube's own shapes: a river down its trunk past the mouth's zone (its surface a little over the
		/// trunk's floor), a lake in its deepest chamber, a fall from the trunk's last node down two metres.
		/// </summary>
		private static CaveLavaPlan HandPlan(CaveSystem system)
		{
			var plan = new CaveLavaPlan { Active = true };
			CavePassage trunk = system.Passages.First(p => p.Kind == CavePassageKind.Trunk);
			float zone = system.Mouths[0].Zone;
			List<Vector3> river = trunk.Nodes.Where(n => n.Along > zone).Select(n => new Vector3(n.Centre.x, n.Floor + 0.05f, n.Centre.z)).ToList();
			Assert.That(river.Count, Is.GreaterThanOrEqualTo(2), "the trunk runs past the mouth's zone");
			plan.Bodies.Add(new CaveLavaBody { Kind = CaveLavaKind.River, Path = river.ToArray(), Width = 1.6f, Passage = 0, Chamber = -1, Pit = -1 });
			if (system.Chambers.Count > 0)
			{
				CaveChamber deepest = system.Chambers.OrderBy(c => c.Floor).First();
				float radius = Mathf.Max(1.5f, 0.35f * Mathf.Min(deepest.Radii.x, deepest.Radii.z));
				plan.Bodies.Add(new CaveLavaBody { Kind = CaveLavaKind.Lake, Path = new[] { new Vector3(deepest.Centre.x, deepest.Floor + 0.05f, deepest.Centre.z) }, Width = radius, Passage = -1, Chamber = 0, Pit = -1 });
			}
			Vector3 last = river[river.Count - 1];
			plan.Bodies.Add(new CaveLavaBody { Kind = CaveLavaKind.Fall, Path = new[] { last, last + new Vector3(0.6f, -2f, 0.6f) }, Width = 1.2f, Passage = 0, Chamber = -1, Pit = -1 });
			return plan;
		}

		private static readonly int[] Seeds = { 4, 7, 11 };

		// ── A cooled cave ─────────────────────────────────────────────

		[Test]
		public void ACooledPlan_LaysNothing_AndRefusesNothing()
		{
			CaveSystem system = Tube(4);
			CaveLavaDressingLayout cooled = CaveLavaDressing.Lay(system, CaveLavaPlan.Cooled);
			Assert.That(cooled.Pieces, Is.Empty);
			Assert.That(cooled.Emitters, Is.Empty);
			// Bodies but not molten: still nothing (a plan says itself whether it runs).
			CaveLavaPlan held = HandPlan(system);
			held.Active = false;
			Assert.That(CaveLavaDressing.Lay(system, held).Pieces, Is.Empty);
			Assert.That(CaveLavaDressing.Lay(system, null).Emitters, Is.Empty);
			Assert.That(CaveLavaDressing.Lay(null, HandPlan(system)).Pieces, Is.Empty);
			// The dresser's guard on every earlier pass's prop: never true for a cooled plan, wherever the point.
			foreach (CaveLavaBody body in HandPlan(system).Bodies)
			{
				foreach (Vector3 p in body.Path)
				{
					Assert.That(CaveLavaDressing.InLava(CaveLavaPlan.Cooled, p), Is.False);
					Assert.That(CaveLavaDressing.InLava(held, p), Is.False, "a plan that does not run holds no lava");
				}
			}
		}

		[Test]
		public void InLava_IsTheBodiesFootprint_UnderItsSurface()
		{
			var plan = new CaveLavaPlan { Active = true };
			plan.Bodies.Add(new CaveLavaBody { Kind = CaveLavaKind.River, Path = new[] { new Vector3(0f, 0f, 0f), new Vector3(10f, -1f, 0f) }, Width = 2f });
			plan.Bodies.Add(new CaveLavaBody { Kind = CaveLavaKind.Lake, Path = new[] { new Vector3(30f, 5f, 0f) }, Width = 4f });
			Assert.That(CaveLavaDressing.InLava(plan, new Vector3(5f, -0.6f, 0.5f)), Is.True, "in the channel, under the surface");
			Assert.That(CaveLavaDressing.InLava(plan, new Vector3(5f, 0.6f, 0.5f)), Is.False, "on the bench over it");
			Assert.That(CaveLavaDressing.InLava(plan, new Vector3(5f, -0.6f, 2f)), Is.False, "beside it");
			Assert.That(CaveLavaDressing.InLava(plan, new Vector3(5f, -40f, 0f)), Is.False, "a level far below is not the lava's");
			Assert.That(CaveLavaDressing.InLava(plan, new Vector3(32f, 4.5f, 1f)), Is.True, "in the lake");
			Assert.That(CaveLavaDressing.InLava(plan, new Vector3(35f, 4.5f, 0f)), Is.False, "past the lake's rim");
		}

		// ── A molten one ──────────────────────────────────────────────

		[Test]
		public void AHandBuiltPlan_DressesTheSame_EveryTime()
		{
			foreach (int seed in Seeds)
			{
				CaveSystem a = Tube(seed), b = Tube(seed);
				CaveLavaDressingLayout first = CaveLavaDressing.Lay(a, HandPlan(a));
				CaveLavaDressingLayout again = CaveLavaDressing.Lay(b, HandPlan(b));
				Assert.That(first.Pieces.Count, Is.GreaterThan(0), $"seed {seed}: a molten tube is dressed");
				Assert.That(first.Emitters.Count(e => e.Kind == SceneEmitterKind.Magma), Is.GreaterThan(0), $"seed {seed}: and lit");
				Assert.That(again.Pieces.Count, Is.EqualTo(first.Pieces.Count), $"seed {seed}");
				for (int i = 0; i < first.Pieces.Count; i++)
				{
					CaveLavaPiece p = first.Pieces[i], q = again.Pieces[i];
					Assert.That((q.Kind, q.Hue, q.Variant, q.Position, q.Rotation, q.Scale), Is.EqualTo((p.Kind, p.Hue, p.Variant, p.Position, p.Rotation, p.Scale)), $"seed {seed} piece {i}");
				}
				Assert.That(again.Emitters.Select(e => (e.Kind, e.Position, e.Seed, e.Reach)), Is.EqualTo(first.Emitters.Select(e => (e.Kind, e.Position, e.Seed, e.Reach))), $"seed {seed}");
				Console.WriteLine($"seed {seed}: {first.Pieces.Count} pieces (floor cracks {first.FloorCracks}, wall cracks {first.WallCracks}, seeps {first.Seeps}, "
					+ $"lavacicles {first.Lavacicles}, crusts {first.Crusts}), {first.Lights} lava lights, {first.Fumes} fumes");
				// Every piece is a lava kind in the Magma hues, a variant it has.
				foreach (CaveLavaPiece p in first.Pieces)
				{
					Assert.That(new[] { CaveGlowKind.MagmaCrack, CaveGlowKind.MagmaSeep, CaveGlowKind.Lavacicle, CaveGlowKind.LavaCrust }, Does.Contain(p.Kind));
					Assert.That(new[] { CaveLavaDressing.MoltenHue, CaveLavaDressing.BankedHue }, Does.Contain(p.Hue));
					Assert.That(p.Variant, Is.InRange(0, CaveArt.VariantCount(p.Kind) - 1));
					Assert.That(p.Scale, Is.InRange(0.3f, 1.5f));
				}
			}
		}

		[Test]
		public void LavaLights_StayWithinTheBudget_SpacedOffEachOtherAndTheGlow()
		{
			foreach (int seed in Seeds)
			{
				CaveSystem system = Tube(seed);
				CaveLavaPlan plan = HandPlan(system);
				// A glow light the dressing laid before, where the river's first light would otherwise go.
				var glow = new List<Vector3> { plan.Bodies[0].Path[0] + Vector3.up * CaveLavaDressing.LightRise };
				CaveLavaDressingLayout layout = CaveLavaDressing.Lay(system, plan, null, glow);
				List<SceneEmitter> lights = layout.Emitters.Where(e => e.Kind == SceneEmitterKind.Magma).ToList();
				List<SceneEmitter> fumes = layout.Emitters.Where(e => e.Kind == SceneEmitterKind.Fumes).ToList();
				Assert.That(lights.Count, Is.EqualTo(layout.Lights).And.LessThanOrEqualTo(CaveLavaDressing.MaxLights));
				Assert.That(fumes.Count, Is.EqualTo(layout.Fumes).And.LessThanOrEqualTo(CaveLavaDressing.MaxFumes));
				for (int i = 0; i < lights.Count; i++)
				{
					Assert.That(system.Void(lights[i].Position), Is.LessThan(0f), $"seed {seed}: light {i} stands in the air");
					Assert.That(lights[i].Reach, Is.InRange(14f, 32f));
					Assert.That((glow[0] - lights[i].Position).magnitude, Is.GreaterThanOrEqualTo(CaveLavaDressing.GlowLightSpacing - 1e-3f), $"seed {seed}: light {i} is off the glow's");
					for (int j = i + 1; j < lights.Count; j++)
					{
						Assert.That((lights[i].Position - lights[j].Position).magnitude, Is.GreaterThanOrEqualTo(CaveLavaDressing.LightSpacing - 1e-3f), $"seed {seed}: lights {i} and {j}");
					}
				}
				for (int i = 0; i < fumes.Count; i++)
				{
					Assert.That(system.Void(fumes[i].Position), Is.LessThan(0f), $"seed {seed}: fumes {i} hang in the air");
					Assert.That(fumes[i].Reach, Is.InRange(4f, 9f));
					for (int j = i + 1; j < fumes.Count; j++)
					{
						Assert.That((fumes[i].Position - fumes[j].Position).magnitude, Is.GreaterThanOrEqualTo(CaveLavaDressing.FumeSpacing - 1e-3f));
					}
				}
				// A river's light carries its half-width and its kind for the embers and the haze.
				SceneEmitter riverLight = lights.FirstOrDefault(e => SceneMagma.BodyOf(e.Seed) == SceneMagmaBody.River);
				if (riverLight.Kind == SceneEmitterKind.Magma)
				{
					Assert.That(SceneMagma.HalfWidthOf(riverLight.Seed), Is.EqualTo(0.8f).Within(0.051f));
				}
			}
		}

		[Test]
		public void NothingOnTheFloor_StandsInTheLava_OrOnTheLayoutsWays()
		{
			foreach (int seed in Seeds)
			{
				CaveSystem system = Tube(seed);
				CaveLavaPlan plan = HandPlan(system);
				// Refuse a band across the tube, as the dresser refuses a crossing: nothing on the floor may lie in it.
				Vector3 mid = plan.Bodies[0].Path[plan.Bodies[0].Path.Length / 2];
				bool Refused(Vector3 p, float margin) => Mathf.Abs(p.x - mid.x) < 3f + margin && Mathf.Abs(p.z - mid.z) < 3f + margin;
				CaveLavaDressingLayout layout = CaveLavaDressing.Lay(system, plan, Refused);
				foreach (CaveLavaPiece p in layout.Pieces.Where(p => p.Kind == CaveGlowKind.MagmaCrack || p.Kind == CaveGlowKind.LavaCrust))
				{
					bool onWall = Vector3.Dot(p.Rotation * Vector3.up, Vector3.up) < 0.7f;
					if (onWall)
					{
						continue;
					}
					Assert.That(CaveLavaDressing.InLava(plan, p.Position, 0f, 0.05f), Is.False, $"seed {seed}: a {p.Kind} at {p.Position} lies in the lava");
					Assert.That(Refused(p.Position, 0f), Is.False, $"seed {seed}: a {p.Kind} at {p.Position} lies on a refused way");
				}
				foreach (CaveLavaPiece p in layout.Pieces.Where(p => p.Kind == CaveGlowKind.Lavacicle))
				{
					Assert.That(system.Void(p.Position + Vector3.down * 0.3f), Is.LessThan(0f), $"seed {seed}: a lavacicle hangs into air");
				}
			}
		}

		[Test]
		public void TheLayoutsOwnPlan_IsDressed_TheSameEveryTime_WithinTheBudget()
		{
			int molten = 0;
			for (int seed = 1; seed <= 12; seed++)
			{
				CaveSystem system = Tube(seed);
				CaveLavaPlan plan = CaveLava.Plan(system);
				if (!plan.Active || plan.Bodies.Count == 0)
				{
					Assert.That(CaveLavaDressing.Lay(system, plan).Pieces, Is.Empty, $"seed {seed}: cooled, undressed");
					continue;
				}
				molten++;
				var clock = System.Diagnostics.Stopwatch.StartNew();
				CaveLavaDressingLayout layout = CaveLavaDressing.Lay(system, plan);
				double seconds = clock.Elapsed.TotalSeconds;
				CaveSystem again = Tube(seed);
				CaveLavaDressingLayout second = CaveLavaDressing.Lay(again, CaveLava.Plan(again));
				Assert.That(second.Pieces.Select(p => (p.Kind, p.Position, p.Scale)), Is.EqualTo(layout.Pieces.Select(p => (p.Kind, p.Position, p.Scale))), $"seed {seed}");
				Assert.That(second.Emitters.Select(e => (e.Kind, e.Position, e.Seed)), Is.EqualTo(layout.Emitters.Select(e => (e.Kind, e.Position, e.Seed))), $"seed {seed}");
				Assert.That(layout.Lights, Is.LessThanOrEqualTo(CaveLavaDressing.MaxLights));
				Assert.That(layout.Pieces.Count, Is.LessThanOrEqualTo(CaveLavaDressing.MaxPieces));
				Console.WriteLine($"seed {seed}: {plan.Bodies.Count} bodies ({string.Join(", ", plan.Bodies.Select(b => $"{b.Kind} {b.Width:0.0} m"))}): "
					+ $"{layout.Pieces.Count} pieces (floor cracks {layout.FloorCracks}, wall cracks {layout.WallCracks}, seeps {layout.Seeps}, lavacicles {layout.Lavacicles}, "
					+ $"crusts {layout.Crusts}), {layout.Lights} lava lights, {layout.Fumes} fumes, laid in {seconds:0.00} s");
			}
			Console.WriteLine($"{molten} of 12 tubes molten by the layout's plan");
		}

		// ── The emitters and the lava light ───────────────────────────

		[Test]
		public void TheEmitterKinds_AreAppended_AfterEveryOldOne()
		{
			string[] old = { "Smoke", "Fire", "Forge", "Lamp", "Hearth", "Indoor", "Crystal", "Torch", "Glow", "Beacon" };
			string[] names = Enum.GetNames(typeof(SceneEmitterKind));
			for (int i = 0; i < old.Length; i++)
			{
				Assert.That(names[i], Is.EqualTo(old[i]), "append only: scene assets store the kind");
				Assert.That((int)(SceneEmitterKind)Enum.Parse(typeof(SceneEmitterKind), old[i]), Is.EqualTo(i));
			}
			Assert.That((int)SceneEmitterKind.Magma, Is.EqualTo(10));
			Assert.That((int)SceneEmitterKind.Fumes, Is.EqualTo(11));
			// The lava's bodies keep the plan's ordinals.
			Assert.That((int)SceneMagmaBody.River, Is.EqualTo((int)CaveLavaKind.River));
			Assert.That((int)SceneMagmaBody.Lake, Is.EqualTo((int)CaveLavaKind.Lake));
			Assert.That((int)SceneMagmaBody.Fall, Is.EqualTo((int)CaveLavaKind.Fall));
		}

		[Test]
		public void TheLavaLightsSeed_CarriesItsBody_AndKeepsItsPhase()
		{
			foreach (uint hash in new[] { 0u, 0xFFFFFFFFu, 0x12345678u })
			{
				foreach (SceneMagmaBody body in (SceneMagmaBody[])Enum.GetValues(typeof(SceneMagmaBody)))
				{
					foreach (float half in new[] { 0f, 0.3f, 0.8f, 7.45f, 25.5f, 40f })
					{
						uint seed = SceneMagma.Seed(hash, half, body);
						Assert.That(SceneMagma.BodyOf(seed), Is.EqualTo(body));
						Assert.That(SceneMagma.HalfWidthOf(seed), Is.EqualTo(Mathf.Min(half, 25.5f)).Within(0.051f));
						Assert.That(seed & 0xFFFFu, Is.EqualTo(hash & 0xFFFFu), "its own phase kept");
					}
				}
			}
			Assert.That(SceneMagma.BodyOf(0x3u << SceneMagma.BodyShift), Is.EqualTo(SceneMagmaBody.River), "an unknown body reads as a river");
			var lake = new SceneEmitter { Position = new Vector3(3f, 4f, 5f), Kind = SceneEmitterKind.Magma, Seed = SceneMagma.Seed(9u, 6f, SceneMagmaBody.Lake) };
			Vector4 ball = SceneMagma.HeatBall(lake);
			Assert.That(ball.w, Is.EqualTo(6f + SceneMagma.HeatMargin).Within(0.06f));
			Assert.That(ball.y, Is.EqualTo(4f + SceneMagma.HeatRise).Within(1e-4f));
			Assert.That(SceneMagma.HeatBall(new SceneEmitter { Seed = SceneMagma.Seed(0u, 0.2f, SceneMagmaBody.River) }).w, Is.EqualTo(SceneMagma.HeatLeast));
			Assert.That(SceneMagma.Sparks(SceneMagma.Seed(0u, 1f, SceneMagmaBody.Fall)), Is.GreaterThan(SceneMagma.Sparks(SceneMagma.Seed(0u, 1f, SceneMagmaBody.River))));
		}

		[Test]
		public void TheChurn_IsAPureFunctionOfTheSharedClock_AndMeetsItselfAcrossTheWrap()
		{
			float wrap = (float)SceneMagma.WrapSeconds;
			foreach (float hz in new[] { SceneMagma.SurgeHz, SceneMagma.RollHz, SceneMagma.SpatterHz })
			{
				double cycles = hz * SceneMagma.WrapSeconds;
				Assert.That(Math.Abs(cycles - Math.Round(cycles)), Is.LessThan(1e-3), $"{hz} Hz is whole cycles in the wrap");
			}
			foreach (SceneMagmaBody body in (SceneMagmaBody[])Enum.GetValues(typeof(SceneMagmaBody)))
			{
				for (float t = 0f; t < 60f; t += 0.37f)
				{
					float c = SceneMagma.Churn(t, 12.5f, -3f, body);
					Assert.That(c, Is.InRange(0.79f, 1.21f));
					Assert.That(SceneMagma.Churn(t, 12.5f, -3f, body), Is.EqualTo(c), "the same moment, the same churn");
				}
				Assert.That(SceneMagma.Churn(wrap - 1e-3f, 1f, 2f, body), Is.EqualTo(SceneMagma.Churn(0f, 1f, 2f, body)).Within(0.01f), "no jump at the wrap");
			}
			uint river = SceneMagma.Seed(0u, 1f, SceneMagmaBody.River);
			Assert.That(SceneMagma.Light(5f, Vector3.zero, river), Is.EqualTo(SceneMagma.Brightness(river) * SceneMagma.Churn(5f, 0f, 0f, SceneMagmaBody.River)));
		}

		[Test]
		public void LavaLights_AreAsBrightAndFarReachingAsTheirBody()
		{
			float last = 0f;
			for (float half = 0.3f; half <= 12f; half += 0.5f)
			{
				float r = SceneMagma.Brightness(SceneMagma.Seed(0u, half, SceneMagmaBody.River));
				float lake = SceneMagma.Brightness(SceneMagma.Seed(0u, half, SceneMagmaBody.Lake));
				float fall = SceneMagma.Brightness(SceneMagma.Seed(0u, half, SceneMagmaBody.Fall));
				Assert.That(r, Is.GreaterThanOrEqualTo(last), "a wider river is no dimmer");
				last = r;
				Assert.That(lake, Is.GreaterThan(r), $"{half} m: a lake outshines a river as wide");
				Assert.That(fall, Is.GreaterThan(r), $"{half} m: a fall outshines a river as wide");
				Assert.That(r, Is.GreaterThan(3.2f), "a river outshines a campfire (Fire 3.2)");
				Assert.That(SceneMagma.ReachFor(SceneMagmaBody.Lake, half), Is.GreaterThanOrEqualTo(SceneMagma.ReachFor(SceneMagmaBody.River, half)));
				Assert.That(SceneMagma.RiseFor(SceneMagmaBody.Lake, half), Is.InRange(0.5f, 2.5f));
			}
			Assert.That(SceneMagma.Brightness(SceneMagma.Seed(0u, 25f, SceneMagmaBody.Lake)), Is.EqualTo(SceneMagma.LakeMost));
		}

		[Test]
		public void ThePool_TakesTheNearestLavaLightsFirst_ThenTheRestNearestFirst()
		{
			var eye = Vector3.zero;
			var all = new List<SceneEmitter>();
			// Thirty crystal lights from 1 m out, then lava lights from 14 m out every 3 m (13 within the priority reach, 2 past it).
			for (int i = 0; i < 30; i++)
			{
				all.Add(new SceneEmitter { Position = new Vector3(1f + i, 0f, 0f), Kind = SceneEmitterKind.Crystal, Seed = (uint)i });
			}
			for (int i = 0; i < 15; i++)
			{
				all.Add(new SceneEmitter { Position = new Vector3(0f, 0f, 14f + 3f * i), Kind = SceneEmitterKind.Magma, Seed = 1000u + (uint)i });
			}
			List<SceneEmitter> nearestFirst = all.OrderBy(e => (e.Position - eye).sqrMagnitude).ToList();
			var into = new List<SceneEmitter>();
			SceneMagma.PickLights(nearestFirst, eye, 16, into);
			Assert.That(into.Count, Is.EqualTo(16));
			int lava = into.Count(e => e.Kind == SceneEmitterKind.Magma);
			Assert.That(lava, Is.EqualTo(SceneMagma.PriorityLights), "the nearest dozen lava lights first (all within 50 m)");
			Assert.That(into.Where(e => e.Kind == SceneEmitterKind.Magma).All(e => e.Position.magnitude <= SceneMagma.PriorityReach), Is.True);
			Assert.That(into.Where(e => e.Kind == SceneEmitterKind.Crystal).Select(e => e.Seed), Is.EqualTo(new uint[] { 0, 1, 2, 3 }), "then the nearest of the rest");
			// In the given order, and the same every time.
			var again = new List<SceneEmitter>();
			SceneMagma.PickLights(nearestFirst, eye, 16, again);
			Assert.That(again.Select(e => e.Seed), Is.EqualTo(into.Select(e => e.Seed)));
			Assert.That(into.Select(e => (e.Position - eye).sqrMagnitude), Is.Ordered);
			// No lava near: the nearest sixteen, as before.
			List<SceneEmitter> crystals = nearestFirst.Where(e => e.Kind == SceneEmitterKind.Crystal).ToList();
			SceneMagma.PickLights(crystals, eye, 16, into);
			Assert.That(into.Select(e => e.Seed), Is.EqualTo(crystals.Take(16).Select(e => e.Seed)));
		}

		[Test]
		public void AMoltenTubesCrystals_GiveWayToItsLava()
		{
			Assert.That(CaveLavaDressing.IsCrystalArt("Cave_Crystal_Amber_2"), Is.True);
			Assert.That(CaveLavaDressing.IsCrystalArt("Cave_GiantCrystal_Violet_0"), Is.True);
			Assert.That(CaveLavaDressing.IsCrystalArt("Cave_Mineral_Sunstone_1"), Is.True);
			Assert.That(CaveLavaDressing.IsCrystalArt("Cave_GlowOreVein_Emberstone_1"), Is.False, "emberstone is a molten tube's own glow");
			Assert.That(CaveLavaDressing.IsCrystalArt("Cave_MagmaCrack_Molten_0"), Is.False);
			Assert.That(CaveLavaDressing.IsCrystalArt(null), Is.False);
			var plan = new CaveLavaPlan { Active = true };
			plan.Bodies.Add(new CaveLavaBody { Kind = CaveLavaKind.River, Path = new[] { new Vector3(0f, 0f, 0f), new Vector3(20f, 0f, 0f) }, Width = 2f });
			plan.Bodies.Add(new CaveLavaBody { Kind = CaveLavaKind.Lake, Path = new[] { new Vector3(50f, 0f, 0f) }, Width = 5f });
			Assert.That(CaveLavaDressing.DistanceToLava(plan, new Vector3(10f, 1f, 0.5f), 6f), Is.EqualTo(0f), "over it");
			Assert.That(CaveLavaDressing.DistanceToLava(plan, new Vector3(10f, 1f, 4f), 6f), Is.EqualTo(3f).Within(1e-4f), "beside the river");
			Assert.That(CaveLavaDressing.DistanceToLava(plan, new Vector3(58f, 0f, 0f), 6f), Is.EqualTo(3f).Within(1e-4f), "beside the lake");
			Assert.That(CaveLavaDressing.DistanceToLava(plan, new Vector3(10f, 20f, 4f), 6f), Is.EqualTo(float.PositiveInfinity), "a level above");
			Assert.That(CaveLavaDressing.DistanceToLava(CaveLavaPlan.Cooled, Vector3.zero, 6f), Is.EqualTo(float.PositiveInfinity));
		}

		// ── The art ───────────────────────────────────────────────────

		[Test]
		public void TheLavaArt_IsAppended_InTheMagmaHues_AsLargeAsThePlacerIsTold()
		{
			string[] glowKinds = Enum.GetNames(typeof(CaveGlowKind));
			// Appended after every kind before them, in order; kinds appended since (the caves' hanging and ice art) come after them.
			int first = Array.IndexOf(glowKinds, "MagmaCrack");
			Assert.That(first, Is.GreaterThan(0), "the lava's kinds are there");
			Assert.That(glowKinds.Skip(first).Take(4), Is.EqualTo(new[] { "MagmaCrack", "MagmaSeep", "Lavacicle", "LavaCrust" }), "appended together, in order");
			Assert.That((int)CaveGlowKind.MagmaCrack, Is.EqualTo(first), "never renumbered");
			Assert.That((int)CaveGlowKind.GlowVine, Is.EqualTo(14), "no older kind moved");
			Assert.That(CaveArt.HueName(CaveGlowKind.MagmaCrack, CaveLavaDressing.MoltenHue), Is.EqualTo("Molten"));
			Assert.That(CaveArt.HueName(CaveGlowKind.MagmaCrack, CaveLavaDressing.BankedHue), Is.EqualTo("Banked"));
			foreach (CaveGlowHue hue in ProceduralArtCatalogue.MagmaHues)
			{
				Assert.That(hue.Emission.r, Is.GreaterThan(hue.Emission.g).And.GreaterThan(hue.Emission.b), $"{hue.Name} glows orange (blackbody: red over green over blue)");
				Assert.That(hue.Emission.g, Is.GreaterThan(hue.Emission.b));
				Assert.That(hue.Albedo.maxColorComponent, Is.LessThan(0.12f), $"{hue.Name}'s crust is black basalt");
			}
			for (int v = 0; v < CaveArt.VariantCount(CaveGlowKind.MagmaCrack); v++)
			{
				Bounds b = CaveGlowMeshes.Build(CaveGlowKind.MagmaCrack, v, 0, ArtSeed).Bounds;
				Assert.That(b.max.y, Is.InRange(0.05f, 0.1f), $"crack v{v} lies flat (its lips a few centimetres proud)");
				float reach = Mathf.Max(Mathf.Max(b.max.x, -b.min.x), Mathf.Max(b.max.z, -b.min.z));
				Assert.That(reach, Is.LessThanOrEqualTo(CaveGlowMeshes.MagmaCrackReach[v] + 0.05f), $"crack v{v} reaches no further than the placer allows for");
			}
			for (int v = 0; v < CaveArt.VariantCount(CaveGlowKind.LavaCrust); v++)
			{
				Bounds b = CaveGlowMeshes.Build(CaveGlowKind.LavaCrust, v, 0, ArtSeed).Bounds;
				Vector2 size = CaveGlowMeshes.LavaCrustSizes[v];
				Assert.That(b.max.y, Is.InRange(0.05f, 0.13f), $"crust v{v} is a low skin");
				Assert.That(b.size.x, Is.LessThanOrEqualTo(size.x + 0.02f), $"crust v{v} length");
				Assert.That(b.size.z, Is.LessThanOrEqualTo(size.y + 0.02f), $"crust v{v} width");
			}
			for (int v = 0; v < CaveArt.VariantCount(CaveGlowKind.MagmaSeep); v++)
			{
				Bounds b = CaveGlowMeshes.Build(CaveGlowKind.MagmaSeep, v, 0, ArtSeed).Bounds;
				Assert.That(b.max.y, Is.EqualTo(CaveGlowMeshes.MagmaSeepHeights[v]).Within(0.12f), $"seep v{v} stands as tall as the placer fits it");
				Assert.That(b.min.y, Is.InRange(-0.1f, 0f), $"seep v{v}'s foot lies on the floor");
			}
			for (int v = 0; v < CaveArt.VariantCount(CaveGlowKind.Lavacicle); v++)
			{
				Bounds b = CaveGlowMeshes.Build(CaveGlowKind.Lavacicle, v, 0, ArtSeed).Bounds;
				Assert.That(-b.min.y, Is.InRange(CaveGlowMeshes.LavacicleDrops[v] - 0.05f, CaveGlowMeshes.LavacicleDrops[v] + 0.15f), $"lavacicle v{v} hangs as far as the placer allows for");
			}
			// Every lava piece glows at its hottest and is dark crust somewhere.
			foreach (CaveGlowKind kind in new[] { CaveGlowKind.MagmaCrack, CaveGlowKind.MagmaSeep, CaveGlowKind.Lavacicle, CaveGlowKind.LavaCrust })
			{
				List<Vector2> uvs = CaveGlowMeshes.Build(kind, 0, 0, ArtSeed).UVs;
				Assert.That(uvs.Max(u => u.x), Is.GreaterThan(CaveGlowMeshes.Glow(0.6f)), $"{kind} glows");
				Assert.That(uvs.Min(u => u.x), Is.LessThan(CaveGlowMeshes.Glow(0.1f)), $"{kind} has dark crust");
			}
		}

		// ── The client ────────────────────────────────────────────────

		[Test]
		public void TheClient_LightsTheLava_ThrowsItsEmbersAndFumes_AndBendsTheAirOverIt()
		{
			string pool = File.ReadAllText("Assets/Scripts/Client/World/Terrain/SiteLightPool.cs");
			Assert.That(pool, Does.Contain("case SceneEmitterKind.Magma:").And.Contain("SceneMagma.Light(time, e.Position, e.Seed)"));
			Assert.That(pool, Does.Contain("kind != SceneEmitterKind.Fumes"), "fumes give no light");
			Assert.That(pool, Does.Contain("SceneMagma.PickLights(candidates, eye, MaxLights, near);"), "lava lights win their place in the pool");
			string embers = File.ReadAllText("Assets/Scripts/Client/World/Terrain/SiteEmbers.cs");
			Assert.That(embers, Does.Contain("kind == SceneEmitterKind.Magma || kind == SceneEmitterKind.Fumes"));
			string shader = File.ReadAllText("Assets/Prefabs/Client/Weather/Shaders/FishSiteEmbers.shader");
			Assert.That(shader, Does.Contain("bool lava = kind == 5;").And.Contain("return FumesVert(input, fire, strength);"));
			Assert.That(shader, Does.Not.Contain("_Time"), "on the shared clock alone");
			string shimmer = File.ReadAllText("Assets/Prefabs/Client/Weather/Shaders/FishHeatShimmer.shader");
			Assert.That(shimmer, Does.Contain("HeatSpotVariance(eye, d, distanceToP)"));
			Assert.That(File.ReadAllText("Assets/Scripts/Client/World/Weather/Sky/FishHeatShimmerFeature.cs"), Does.Contain("CaveLavaHeat.Spots(camera.transform.position, heatSpots)"));
			Assert.That(File.ReadAllText("Assets/Scripts/Client/World/Terrain/CaveLavaHeat.cs"), Does.Contain("SceneMagma.HeatBall(near[i])"));
		}
	}
}
