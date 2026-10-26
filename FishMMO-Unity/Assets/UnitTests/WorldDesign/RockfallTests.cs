using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// FALLING STONE (2026-10-25, phase I2u; decisions 79, 95, 97): every fall a pure function of the shared clock, deterministic,
	/// continuous across the 10 000 s wrap and never repeating, at the drafted rates (one fall per 20–60 min, ×3 freeze-thaw, ×2 rain);
	/// each stone's run continuous and stopping where it should (the coarse further: fall sorting); the bombs bursting exactly when
	/// VentBursts (and so <c>_FishVentBurst</c>) says; the breathing talus by the season; the stored chutes and cones the cut makes; talus
	/// size grading; FishRockfall.hlsl constant for constant; the plumbing; and nothing at all for any scene or rule until a round names it.
	/// </summary>
	[TestFixture]
	public class RockfallTests
	{
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders";
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const uint Chute = 0x00C0FFEEu;
		/// <summary>A moment far from the epoch, so a float would lose it and only the double search keeps it.</summary>
		private const double Far = 1.9e9;

		private static string Read(string path) => File.ReadAllText(path);

		private static List<Rockfall.Fall> FallsAt(double seconds, uint seed = Chute, Func<double, float> multiplier = null)
		{
			var list = new List<Rockfall.Fall>();
			Rockfall.FallsAt(seconds, seed, 1f, Rockfall.LifetimeOf(150f), multiplier, list);
			return list;
		}

		// ── Kinds ──────────────────────────────────────────────────

		[Test]
		public void ChuteKinds_AreAppendOnly_AndNamedByTheirPresets()
		{
			Assert.That((int)RockChuteKind.Scree, Is.EqualTo(0));
			Assert.That((int)RockChuteKind.Sciara, Is.EqualTo(1));
			Assert.That(Enum.GetValues(typeof(RockChuteKind)).Length, Is.EqualTo(2));
			Assert.That(RockChutes.IsChute("ScreeChutes", out RockChuteKind a) && a == RockChuteKind.Scree, Is.True);
			Assert.That(RockChutes.IsChute("sciara", out RockChuteKind b) && b == RockChuteKind.Sciara, Is.True);
			Assert.That(RockChutes.IsChute("Grooves", out _), Is.False);
			Assert.That(RockChutes.IsCone("ScreeCone") && !RockChutes.IsCone("Bajada"), Is.True);
			foreach (string kind in RockChutes.ChuteKinds.Concat(RockChutes.ConeKinds).Append(RockChutes.WalkingRockKind))
			{
				Assert.That(BiomeLandformPresets.Knows(kind), Is.True, $"{kind} names a landform preset");
			}
		}

		// ── The schedule ───────────────────────────────────────────

		[Test]
		public void Falls_ArePureInTheClock_AndDeterministic()
		{
			for (int i = 0; i < 400; i++)
			{
				double t = Far + i * 997.3;
				List<Rockfall.Fall> a = FallsAt(t), b = FallsAt(t);
				Assert.That(a.Count, Is.EqualTo(b.Count));
				for (int k = 0; k < a.Count; k++)
				{
					Assert.That(a[k].Start, Is.EqualTo(b[k].Start));
					Assert.That(a[k].Seed, Is.EqualTo(b[k].Seed));
					Assert.That(a[k].Stones, Is.EqualTo(b[k].Stones));
					Assert.That(a[k].Stones, Is.InRange(Rockfall.LeastStones, Rockfall.MostStones));
					Assert.That(a[k].Seed, Is.LessThanOrEqualTo(0xFFFFFFu), "a seed a float carries whole");
					Assert.That(a[k].Start, Is.LessThanOrEqualTo(t));
				}
			}
			// Another chute falls at other times.
			int same = 0, total = 0;
			for (int i = 0; i < 200; i++)
			{
				double t = Far + i * 3001.0;
				var mine = new HashSet<double>(FallsAt(t).Select(f => f.Start));
				foreach (Rockfall.Fall f in FallsAt(t, Chute ^ 0x1234u))
				{
					total++;
					same += mine.Contains(f.Start) ? 1 : 0;
				}
			}
			Assert.That(total, Is.GreaterThan(0));
			Assert.That(same, Is.EqualTo(0));
		}

		[Test]
		public void Falls_AreContinuousAcrossTheWrap_AndNeverRepeatIt()
		{
			// About every wrap of the motion clock: a fall seen just before is the same fall just after, a frame older.
			for (int w = 1; w < 300; w++)
			{
				double edge = Math.Floor(Far / 10000.0) * 10000.0 + w * 10000.0;
				List<Rockfall.Fall> before = FallsAt(edge - 0.02), after = FallsAt(edge + 0.02);
				foreach (Rockfall.Fall f in before)
				{
					Assert.That(after.Any(g => g.Start == f.Start && g.Seed == f.Seed), Is.True, "a fall does not vanish at the wrap");
				}
			}
			// The schedule of one wrap is not the next one's (the slots run on the unwrapped clock).
			var one = new List<Rockfall.Fall>();
			var two = new List<Rockfall.Fall>();
			double w0 = Math.Floor(Far / 10000.0) * 10000.0;
			Rockfall.FallsAt(w0 + 10000.0, Chute, 1f, 10000.0, null, one);
			Rockfall.FallsAt(w0 + 20000.0, Chute, 1f, 10000.0, null, two);
			Assert.That(one.Select(f => f.Start - w0).SequenceEqual(two.Select(f => f.Start - w0 - 10000.0)), Is.False);
		}

		[Test]
		public void TheRate_IsOneFallPerItsOwnTwentyToSixtyMinutes_AndTheWeatherRaisesIt()
		{
			for (uint seed = 1; seed < 40; seed++)
			{
				float period = Rockfall.PeriodOf(seed * 0x9E3779B1u);
				Assert.That(period, Is.InRange(Rockfall.LeastPeriod, Rockfall.MostPeriod));
			}
			uint s = Chute;
			double span = 200.0 * 86400.0;
			int Count(float boost, float m)
			{
				int n = 0;
				for (long slot = 0; slot < (long)(span / Rockfall.SlotSeconds); slot++)
				{
					n += Rockfall.Falls(slot + 15_000_000L, s, boost, m, out _, out _, out _) ? 1 : 0;
				}
				return n;
			}
			double expected = span / Rockfall.PeriodOf(s);
			int calm = Count(1f, 1f);
			Assert.That(calm, Is.InRange((int)(expected * 0.9), (int)(expected * 1.1)), "calm, dry: one per its own period");
			Assert.That(Count(1f, Rockfall.ThawBoost), Is.InRange((int)(calm * 2.6), (int)(calm * 3.3)), "freeze-thaw ×3");
			Assert.That(Count(Rockfall.RainBoost, 1f), Is.InRange((int)(calm * 1.8), (int)(calm * 2.2)), "a walking rock's (or rain's) ×2");
			Assert.That(Count(1f, 0f), Is.EqualTo(0));
		}

		[Test]
		public void TheMultiplier_ReadsTheKnots_ThawThenRain()
		{
			Rockfall.Knot K(float c, float r = 0f) => new Rockfall.Knot { Celsius = c, Rain = r };
			const double H = Rockfall.KnotSeconds;
			// Air at knot k: cold before knot 10, thawed from it.
			Func<long, Rockfall.Knot> thaw = k => K(k < 10 ? -3f : 2f);
			Assert.That(Rockfall.Multiplier(9.5 * H, thaw), Is.EqualTo(1f));
			Assert.That(Rockfall.Multiplier(10.0 * H, thaw), Is.EqualTo(Rockfall.ThawBoost), "the hour the air thaws");
			Assert.That(Rockfall.Multiplier(11.9 * H, thaw), Is.EqualTo(Rockfall.ThawBoost), "and the next");
			Assert.That(Rockfall.Multiplier(12.0 * H, thaw), Is.EqualTo(1f), "two hours after, no more");
			// Freezing (downward) is not a thaw; staying frozen or thawed is neither.
			Assert.That(Rockfall.Multiplier(10.5 * H, k => K(k < 10 ? 2f : -3f)), Is.EqualTo(1f));
			Assert.That(Rockfall.Multiplier(10.5 * H, k => K(-5f)), Is.EqualTo(1f));
			// Rain at this knot or the one before (the hour after it).
			Func<long, Rockfall.Knot> rain = k => K(8f, k == 20 ? 0.5f : 0f);
			Assert.That(Rockfall.Multiplier(19.5 * H, rain), Is.EqualTo(1f));
			Assert.That(Rockfall.Multiplier(20.5 * H, rain), Is.EqualTo(Rockfall.RainBoost));
			Assert.That(Rockfall.Multiplier(21.5 * H, rain), Is.EqualTo(Rockfall.RainBoost));
			Assert.That(Rockfall.Multiplier(22.5 * H, rain), Is.EqualTo(1f));
			Assert.That(Rockfall.Multiplier(20.5 * H, k => K(k < 20 ? -1f : 1f, 0.05f)), Is.EqualTo(Rockfall.ThawBoost), "a drizzle under the threshold");
			Assert.That(Rockfall.Multiplier(20.5 * H, k => K(k < 20 ? -1f : 1f, 0.3f)), Is.EqualTo(Rockfall.ThawBoost * Rockfall.RainBoost));
			Assert.That(Rockfall.Multiplier(20.5 * H, null), Is.EqualTo(1f));
			// Read at whole knots only: the same multiplier over the whole hour, whatever was asked before.
			int asked = 0;
			Func<long, Rockfall.Knot> counting = k => { asked++; return thaw(k); };
			float m1 = Rockfall.Multiplier(10.2 * H, counting), m2 = Rockfall.Multiplier(10.8 * H, counting);
			Assert.That(m1, Is.EqualTo(m2));
		}

		[Test]
		public void TheLastFall_IsTheNewestFallFound_AndTheScarFades()
		{
			for (int i = 0; i < 60; i++)
			{
				double t = Far + i * 7919.0;
				double last = Rockfall.LastFall(t, Chute, 1f, null);
				Assert.That(double.IsNaN(last), Is.False, "a chute falls within two days");
				Assert.That(last, Is.LessThanOrEqualTo(t));
				List<Rockfall.Fall> recent = FallsAt(t);
				if (recent.Count > 0)
				{
					Assert.That(last, Is.EqualTo(recent.Max(f => f.Start)));
				}
			}
			Assert.That(Rockfall.ScarFreshness(0.0), Is.EqualTo(1f).Within(1e-6f));
			Assert.That(Rockfall.ScarFreshness(3600.0), Is.LessThan(1f));
			Assert.That(Rockfall.ScarFreshness(86400.0), Is.InRange(0.3f, 0.6f), "a day on, a fainter scar");
			Assert.That(Rockfall.ScarFreshness(6.0 * 86400.0), Is.LessThan(0.05f), "gone over days");
			Assert.That(Rockfall.ScarFreshness(double.NaN), Is.EqualTo(0f));
		}

		// ── A stone's run ──────────────────────────────────────────

		[Test]
		public void ARun_IsContinuous_AndStopsWhereItShould()
		{
			foreach (float stop in new[] { 3f, 40f, 180f, 900f })
			{
				Rockfall.Run r = Rockfall.RunOf(stop, 12f, 3.5f, 7f);
				Assert.That(r.Peak, Is.LessThanOrEqualTo(12f + 1e-4f));
				float prev = 0f;
				for (float t = 0f; t <= r.Total + 2f; t += 0.01f)
				{
					float s = Rockfall.AlongAt(r, t);
					Assert.That(s, Is.GreaterThanOrEqualTo(prev - 1e-4f), "never back up the chute");
					Assert.That(s - prev, Is.LessThanOrEqualTo(r.Peak * 0.01f + 1e-3f), "no jump");
					Assert.That(Rockfall.SpeedAt(r, t), Is.LessThanOrEqualTo(r.Peak + 1e-4f));
					prev = s;
				}
				Assert.That(Rockfall.AlongAt(r, r.Total + 1f), Is.EqualTo(stop).Within(1e-3f));
				Assert.That(Rockfall.AlongAt(r, -1f), Is.EqualTo(0f));
			}
			Assert.That(Rockfall.RunOf(3f, 12f, 3.5f, 7f).Peak, Is.LessThan(12f), "a short run never reaches its top speed");
		}

		[Test]
		public void Stones_FallSorted_InRange_AndShowThenSink()
		{
			double coarse = 0, fine = 0;
			int nc = 0, nf = 0;
			for (uint seed = 1; seed < 400; seed++)
			{
				for (int i = 0; i < 12; i++)
				{
					Rockfall.Stone s = Rockfall.StoneOf(seed * 2654435761u & 0xFFFFFFu, i, RockChuteKind.Scree, 200f, 3f);
					Assert.That(s.Size, Is.InRange(Rockfall.ScreeLeast, Rockfall.ScreeMost));
					Assert.That(Mathf.Abs(s.Across) + s.Wobble, Is.LessThanOrEqualTo(3f * Rockfall.ScreeAcross * 1.25f + 1e-4f));
					Assert.That(s.Stop, Is.InRange(200f * Rockfall.LeastStop, 200f));
					Assert.That(s.Delay, Is.InRange(0f, Rockfall.StaggerSeconds));
					if (s.Rank > 0.6f) { coarse += s.Stop; nc++; }
					if (s.Rank < 0.1f) { fine += s.Stop; nf++; }
				}
			}
			Assert.That(coarse / nc, Is.GreaterThan(fine / nf + 15.0), "the coarse run on to the toe");
			Rockfall.Stone k = Rockfall.StoneOf(0x123456u, 3, RockChuteKind.Sciara, 1500f, 300f);
			Assert.That(k.Size, Is.InRange(Rockfall.SciaraLeast, Rockfall.SciaraMost));
			Rockfall.Run run = Rockfall.RunOf(k.Stop, k.Peak, k.Accel, k.Decel);
			Assert.That(Rockfall.StateAt(k, k.Delay - 0.01f).w, Is.EqualTo(0f), "not before it sets off");
			Assert.That(Rockfall.StateAt(k, k.Delay + 1f).w, Is.EqualTo(1f));
			Assert.That(Rockfall.StateAt(k, k.Delay + run.Total + Rockfall.RestSeconds - 1f).w, Is.EqualTo(1f), "lies where it stopped");
			Assert.That(Rockfall.StateAt(k, k.Delay + run.Total + Rockfall.RestSeconds + Rockfall.SinkSeconds + 1f).w, Is.EqualTo(0f), "then sinks out of sight");
			Assert.That(Rockfall.LifetimeOf(1500f), Is.GreaterThan(k.Delay + run.Total + Rockfall.RestSeconds + Rockfall.SinkSeconds), "the lifetime bounds every stone");
			Assert.That(Rockfall.StateAt(k, k.Delay + run.Total + 5f).z, Is.EqualTo(0f).Within(1e-5f), "no hop at rest");
		}

		// ── The bombs ──────────────────────────────────────────────

		[Test]
		public void Bombs_BurstExactlyWhenVentBurstsSays_AcrossTheWrap()
		{
			foreach (uint seed in new[] { 7u, 0xBADCAFEu, VentBursts.SceneSeed("Strongyle 1"), 0x5701B0u })
			{
				for (float t = 0f; t < 10000f; t += 0.37f)
				{
					bool burst = StromboliBombs.BurstOf(t, seed, 0, out float start, out float strength, out int slot);
					bool inside = burst && t > start && t < start + VentBursts.BurstSeconds;
					Assert.That(VentBursts.At(t, seed) > 0f, Is.EqualTo(inside), $"seed {seed:x} t {t}");
					if (burst)
					{
						Assert.That(strength, Is.InRange(VentBursts.Weakest, 1f));
						Assert.That(slot, Is.EqualTo((int)Math.Floor(t / VentBursts.PeriodOf(seed))));
					}
				}
				// Just after the wrap, the slot before is the last of the wrap before, its start 10 000 s back.
				int slots = (int)(10000f / VentBursts.PeriodOf(seed));
				StromboliBombs.BurstOf(5f, seed, -1, out float s0, out _, out int k0);
				StromboliBombs.BurstOf(9999f, seed, 0, out float s1, out _, out int k1);
				Assert.That(k0, Is.EqualTo(slots - 1));
				Assert.That(k1, Is.EqualTo(slots - 1));
				Assert.That(s0, Is.EqualTo(s1 - 10000f).Within(1e-3f));
			}
		}

		[Test]
		public void Bombs_AreThrownAsDrafted_AndLandOnTheGround()
		{
			const float g = 9.81f;
			for (int slot = 0; slot < 50; slot++)
			{
				int n = StromboliBombs.CountOf(0xABCDEFu, slot, 1f);
				Assert.That(n, Is.InRange(StromboliBombs.LeastBombs, StromboliBombs.MostBombs));
				Assert.That(StromboliBombs.CountOf(0xABCDEFu, slot, 0.4f), Is.LessThan(n + 1));
				for (int i = 0; i < n; i++)
				{
					Vector3 v = StromboliBombs.Launch(0xABCDEFu, slot, i, g, out float delay, out float size);
					Assert.That(delay, Is.InRange(0f, VentBursts.BurstSeconds));
					Assert.That(size, Is.InRange(StromboliBombs.LeastSize, StromboliBombs.MostSize));
					// On level ground it comes down at its drafted reach.
					float flight = StromboliBombs.Landing(new Vector3(0f, 10f, 0f), v, g, (x, z) => 10f, out Vector3 at);
					float reach = new Vector2(at.x, at.z).magnitude;
					Assert.That(reach, Is.InRange(StromboliBombs.LeastRange - 0.5f, StromboliBombs.MostRange + 0.5f));
					Assert.That(at.y, Is.EqualTo(10f).Within(0.02f));
					Assert.That(flight, Is.GreaterThan(0f));
					// Down a cone's flank it lands lower and further, on the ground.
					Func<float, float, float> cone = (x, z) => 10f - 0.6f * Mathf.Sqrt(x * x + z * z);
					StromboliBombs.Landing(new Vector3(0f, 11f, 0f), v, g, cone, out Vector3 down);
					Assert.That(down.y, Is.EqualTo(cone(down.x, down.z)).Within(0.2f));
					Assert.That(new Vector2(down.x, down.z).magnitude, Is.GreaterThan(reach - 1f));
				}
			}
			Assert.That(StromboliBombs.GlowAt(-1f), Is.EqualTo(0f));
			Assert.That(StromboliBombs.GlowAt(0f), Is.EqualTo(1f));
			Assert.That(StromboliBombs.GlowAt(30f), Is.InRange(0.2f, 0.3f));
			Assert.That(StromboliBombs.GlowAt(StromboliBombs.CoolSeconds), Is.EqualTo(0f));
		}

		// ── The breathing talus ────────────────────────────────────

		[Test]
		public void TheTalus_HoldsIceInSummer_AndSteamsInWinter_ByTheSeason()
		{
			Vector2 summer = TalusBreath.Of(0.5f, -15f, 14f), winter = TalusBreath.Of(0f, -15f, -12f);
			Assert.That(summer.x, Is.EqualTo(1f).Within(1e-4f));
			Assert.That(summer.y, Is.EqualTo(0f));
			Assert.That(winter.x, Is.EqualTo(0f));
			Assert.That(winter.y, Is.EqualTo(1f).Within(1e-4f));
			Assert.That(TalusBreath.Of(0.5f, 4f, 20f).x, Is.EqualTo(0f), "no winter frost, no summer ice");
			Assert.That(TalusBreath.Of(0f, -15f, 8f).y, Is.EqualTo(0f), "too warm to show the steam");
			Assert.That(TalusBreath.Of(0.25f, -15f, 0f).magnitude, Is.LessThan(1e-3f), "between the seasons, neither");
			float prev = TalusBreath.Of(0f, -15f, -12f).y;
			for (float p = 0f; p <= 1f; p += 0.001f)
			{
				Vector2 b = TalusBreath.Of(p, -15f, -12f);
				Assert.That(Mathf.Abs(b.y - prev), Is.LessThan(0.01f), "continuous through the year");
				prev = b.y;
			}
		}

		// ── What the cut stores ────────────────────────────────────

		private static BiomeLandformSite Line(string kind, uint seed, params Vector2[] points)
		{
			var segs = new List<BiomeLandformSegment>();
			float along = 0f, length = 0f;
			for (int i = 1; i < points.Length; i++)
			{
				length += Vector2.Distance(points[i - 1], points[i]);
			}
			for (int i = 1; i < points.Length; i++)
			{
				segs.Add(new BiomeLandformSegment(points[i - 1], points[i], 2f, 1f, along, length));
				along += Vector2.Distance(points[i - 1], points[i]);
			}
			return new BiomeLandformSite { Form = new BiomeLandform { Kind = kind }, BiomeID = 7, Seed = seed, Centre = points[0], Segments = segs };
		}

		[Test]
		public void TheCut_StoresChutesHeadFirst_WithARunoutOverTheCone()
		{
			// A face falling to +x at 60 %, flattening at x = 100 (the valley floor).
			Func<float, float, float> ground = (x, z) => x < 100f ? -0.6f * x : -60f - 0.02f * (x - 100f);
			var sites = new List<BiomeLandformSite>
			{
				// Drawn uphill (the low end first): the cut turns it round.
				Line("ScreeChutes", 11u, new Vector2(60f, 0f), new Vector2(30f, 1f), new Vector2(0f, 0f)),
				Line("Sciara", 12u, new Vector2(0f, 50f), new Vector2(90f, 50f)),
				Line("RiftZone", 13u, new Vector2(0f, 90f), new Vector2(50f, 90f)),
			};
			var chutes = new List<RockChute>();
			var points = new List<Vector4>();
			var cones = new List<TalusCone>();
			RockChutes.FromLandforms(sites, null, ground, chutes, points, cones);
			Assert.That(chutes.Count, Is.EqualTo(2), "only the chute landforms");
			RockChute scree = chutes[0], sciara = chutes[1];
			Assert.That(scree.Kind, Is.EqualTo(RockChuteKind.Scree));
			Assert.That(sciara.Kind, Is.EqualTo(RockChuteKind.Sciara));
			Assert.That(points[scree.First].x, Is.EqualTo(0f).Within(0.01f), "its head is its highest end");
			Assert.That(scree.Length, Is.GreaterThan(80f), "the runout carries on over the cone");
			Assert.That(points[scree.First + scree.Count - 1].x, Is.LessThan(110f), "and stops where the ground flattens");
			Assert.That(Mathf.Abs(sciara.Length - 90f), Is.LessThan(0.5f), "a sciara is its own runout");
			foreach (RockChute c in chutes)
			{
				for (int i = c.First + 1; i < c.First + c.Count; i++)
				{
					Assert.That(points[i].w, Is.GreaterThan(points[i - 1].w));
					Assert.That(points[i].w - points[i - 1].w, Is.LessThanOrEqualTo(RockChutes.PointSpacing + 1e-3f));
					Assert.That(points[i].y, Is.EqualTo(ground(points[i].x, points[i].z)).Within(1e-3f), "on the carved ground");
				}
				Assert.That(points[c.First + c.Count - 1].w, Is.EqualTo(c.Length).Within(1e-3f));
				Assert.That(c.Boost, Is.EqualTo(1f));
			}
			// Deterministic, and a walking rock nearby doubles its rate.
			var again = new List<RockChute>();
			RockChutes.FromLandforms(sites, null, ground, again, new List<Vector4>(), new List<TalusCone>());
			Assert.That(again.Select(c => c.Seed).SequenceEqual(chutes.Select(c => c.Seed)), Is.True);
			var pads = new List<BiomeLandformPad> { new BiomeLandformPad { Kind = "WalkingRock", BiomeID = 7, Centre = new Vector2(20f, 20f) } };
			RockChutes.FromLandforms(sites, pads, ground, again, new List<Vector4>(), new List<TalusCone>());
			Assert.That(again[0].Boost, Is.EqualTo(RockChutes.WalkingRockBoost));
			// Nothing to store: all empty.
			RockChutes.FromLandforms(new List<BiomeLandformSite>(), null, ground, chutes, points, cones);
			Assert.That(chutes.Count + points.Count + cones.Count, Is.EqualTo(0));
		}

		[Test]
		public void TalusGrading_IsFineAtTheApex_CoarseAtTheToe_AndOneOffTheCone()
		{
			BiomeLandform form = BiomeLandformPresets.Named("ScreeCone");
			var site = new BiomeLandformSite { Form = form, BiomeID = 5, Centre = new Vector2(100f, 100f), Radius = 50f, HalfLength = 50f, Yaw = 0f, Seed = 3u,
				Height = 10f, Bounds = new Vector4(40f, 40f, 160f, 160f) };
			float a = RockChutes.ConeAlong(site, 100.5f, 100f, out float wa);
			Assert.That(a, Is.LessThan(0.02f));
			Assert.That(wa, Is.EqualTo(1f));
			float t = RockChutes.ConeAlong(site, 148f, 100f, out float wt);
			Assert.That(t, Is.GreaterThan(0.9f));
			Assert.That(RockChutes.GradeAt(a, wa), Is.EqualTo(RockChutes.ApexScale).Within(0.02f));
			Assert.That(RockChutes.GradeAt(t, wt), Is.GreaterThan(1.6f));
			RockChutes.ConeAlong(site, 100f - 20f, 100f, out float behind);
			Assert.That(behind, Is.EqualTo(0f), "behind the apex: off the cone");
			Assert.That(RockChutes.GradeAt(0.5f, 0f), Is.EqualTo(1f));
			var zones = new BiomeLandformZones(new List<BiomeLandformSite> { site }, new BiomeLandforms.Context());
			Assert.That(zones.TalusGrade(5, 100.5f, 100f), Is.EqualTo(RockChutes.ApexScale).Within(0.02f));
			Assert.That(zones.TalusGrade(5, 148f, 100f), Is.GreaterThan(1.6f));
			Assert.That(zones.TalusGrade(6, 148f, 100f), Is.EqualTo(1f), "another biome's cone grades nothing");
			Assert.That(zones.TalusGrade(5, 1000f, 1000f), Is.EqualTo(1f));
			Assert.That(BiomeLandformZones.TalusGradeOf(null, 5, 148f, 100f), Is.EqualTo(1f));
			var cone = RockChutes.ConeOf(site, (x, z) => -0.5f * x);
			Assert.That(cone.Toe.x, Is.EqualTo(150f).Within(1e-3f));
			Assert.That(cone.Apex.y, Is.GreaterThan(cone.Toe.y));
		}

		// ── The GPU mirror ─────────────────────────────────────────

		private static Dictionary<string, string> Defines(string text)
		{
			var d = new Dictionary<string, string>();
			foreach (Match m in Regex.Matches(text, @"#define\s+(ROCK_\w+|BOMB_\w+)\s+([^\s/]+)"))
			{
				d[m.Groups[1].Value] = m.Groups[2].Value;
			}
			return d;
		}

		private static float F(Dictionary<string, string> d, string k) => float.Parse(d[k], CultureInfo.InvariantCulture);
		private static uint X(Dictionary<string, string> d, string k) => Convert.ToUInt32(d[k].TrimEnd('u').Substring(2), 16);

		[Test]
		public void TheShader_MirrorsRockfall_ConstantForConstant()
		{
			string hlsl = Read($"{Shaders}/FishRockfall.hlsl");
			Dictionary<string, string> d = Defines(hlsl);
			Assert.That(F(d, "ROCK_STAGGER_SECONDS"), Is.EqualTo(Rockfall.StaggerSeconds));
			Assert.That(F(d, "ROCK_REST_SECONDS"), Is.EqualTo(Rockfall.RestSeconds));
			Assert.That(F(d, "ROCK_SINK_SECONDS"), Is.EqualTo(Rockfall.SinkSeconds));
			Assert.That(F(d, "ROCK_SCREE_LEAST"), Is.EqualTo(Rockfall.ScreeLeast));
			Assert.That(F(d, "ROCK_SCREE_MOST"), Is.EqualTo(Rockfall.ScreeMost));
			Assert.That(F(d, "ROCK_SCIARA_LEAST"), Is.EqualTo(Rockfall.SciaraLeast));
			Assert.That(F(d, "ROCK_SCIARA_MOST"), Is.EqualTo(Rockfall.SciaraMost));
			Assert.That(F(d, "ROCK_LEAST_PEAK"), Is.EqualTo(Rockfall.LeastPeak));
			Assert.That(F(d, "ROCK_MOST_PEAK"), Is.EqualTo(Rockfall.MostPeak));
			Assert.That(F(d, "ROCK_LEAST_ACCEL"), Is.EqualTo(Rockfall.LeastAccel));
			Assert.That(F(d, "ROCK_MOST_ACCEL"), Is.EqualTo(Rockfall.MostAccel));
			Assert.That(F(d, "ROCK_LEAST_DECEL"), Is.EqualTo(Rockfall.LeastDecel));
			Assert.That(F(d, "ROCK_MOST_DECEL"), Is.EqualTo(Rockfall.MostDecel));
			Assert.That(F(d, "ROCK_LEAST_STOP"), Is.EqualTo(Rockfall.LeastStop));
			Assert.That(F(d, "ROCK_MOST_STOP"), Is.EqualTo(Rockfall.MostStop));
			Assert.That(F(d, "ROCK_LEAST_HOP"), Is.EqualTo(Rockfall.LeastHop));
			Assert.That(F(d, "ROCK_MOST_HOP"), Is.EqualTo(Rockfall.MostHop));
			Assert.That(F(d, "ROCK_LEAST_HOP_HEIGHT"), Is.EqualTo(Rockfall.LeastHopHeight));
			Assert.That(F(d, "ROCK_MOST_HOP_HEIGHT"), Is.EqualTo(Rockfall.MostHopHeight));
			Assert.That(F(d, "ROCK_SCREE_ACROSS"), Is.EqualTo(Rockfall.ScreeAcross));
			Assert.That(F(d, "ROCK_SCIARA_ACROSS"), Is.EqualTo(Rockfall.SciaraAcross));
			Assert.That(F(d, "ROCK_LEAST_WOBBLE"), Is.EqualTo(Rockfall.LeastWobble));
			Assert.That(F(d, "ROCK_MOST_WOBBLE"), Is.EqualTo(Rockfall.MostWobble));
			Assert.That(X(d, "ROCK_SALT_SIZE"), Is.EqualTo(Rockfall.SizeSalt));
			Assert.That(X(d, "ROCK_SALT_DELAY"), Is.EqualTo(Rockfall.DelaySalt));
			Assert.That(X(d, "ROCK_SALT_ACROSS"), Is.EqualTo(Rockfall.AcrossSalt));
			Assert.That(X(d, "ROCK_SALT_PEAK"), Is.EqualTo(Rockfall.PeakSalt));
			Assert.That(X(d, "ROCK_SALT_ACCEL"), Is.EqualTo(Rockfall.AccelSalt));
			Assert.That(X(d, "ROCK_SALT_DECEL"), Is.EqualTo(Rockfall.DecelSalt));
			Assert.That(X(d, "ROCK_SALT_STOP"), Is.EqualTo(Rockfall.StopSalt));
			Assert.That(X(d, "ROCK_SALT_HOP"), Is.EqualTo(Rockfall.HopSalt));
			Assert.That(X(d, "ROCK_SALT_HOP_HEIGHT"), Is.EqualTo(Rockfall.HopHeightSalt));
			Assert.That(X(d, "ROCK_SALT_WOBBLE"), Is.EqualTo(Rockfall.WobbleSalt));
			Assert.That(X(d, "ROCK_SALT_PHASE"), Is.EqualTo(Rockfall.PhaseSalt));
			Assert.That(F(d, "BOMB_COOL_SECONDS"), Is.EqualTo(StromboliBombs.CoolSeconds));
			Assert.That(F(d, "BOMB_REST_SECONDS"), Is.EqualTo(StromboliBombs.RestSeconds));
			Assert.That(F(d, "BOMB_SINK_SECONDS"), Is.EqualTo(StromboliBombs.SinkSeconds));
			// The sizes the client fills.
			Assert.That(F(d, "ROCK_MAX_POINTS"), Is.EqualTo(FishMMO.Client.RockfallField.MaxPoints));
			Assert.That(F(d, "ROCK_MAX_CHUTES"), Is.EqualTo(FishMMO.Client.RockfallField.MaxChutes));
			Assert.That(F(d, "ROCK_MAX_FALLS"), Is.EqualTo(FishMMO.Client.RockfallField.MaxFalls));
			Assert.That(F(d, "ROCK_MAX_BOMBS"), Is.EqualTo(FishMMO.Client.RockfallField.MaxBombs));
			Assert.That(F(d, "ROCK_MAX_BURSTS"), Is.EqualTo(FishMMO.Client.RockfallField.MaxBursts));
			Assert.That(F(d, "ROCK_STONES_PER_FALL"), Is.EqualTo(FishMMO.Client.RockfallField.StonesPerFall));
			Assert.That(F(d, "ROCK_STONES_PER_FALL"), Is.EqualTo(Rockfall.MostStones));
			Assert.That(F(d, "ROCK_MAX_CONES"), Is.EqualTo(FishMMO.Client.RockfallField.MaxCones));
			Dictionary<string, string> marks = Defines(Read($"{Shaders}/FishRockfallMarks.shader"));
			Assert.That(F(marks, "ROCK_MAX_DECALS"), Is.EqualTo(FishMMO.Client.RockfallField.MaxDecals));
			Assert.That(F(marks, "ROCK_BREATH_PER_CONE"), Is.EqualTo(FishMMO.Client.RockfallField.BreathPerCone));
			// The hash and the shapes of the stone, the run and the hop, as the C# draws them.
			Assert.That(hlsl, Does.Contain("uint h = (v * 0x9E3779B1u) ^ seed;"));
			Assert.That(hlsl, Does.Contain("h *= 0x7FEB352Du;").And.Contain("h *= 0x846CA68Bu;"));
			Assert.That(hlsl, Does.Contain("return RockUnitOf(RockMix((i * 0x9E3779B1u) ^ salt, seed));"));
			Assert.That(hlsl, Does.Contain("0.6 * rank + 0.4 * RockU(seed, i, ROCK_SALT_STOP)"));
			Assert.That(hlsl, Does.Contain("RockU(seed, i, ROCK_SALT_WOBBLE ^ 0x77u)"));
			Assert.That(hlsl, Does.Contain("r.peak = sqrt(max(1e-6, 2.0 * r.stop * r.accel * r.decel / (r.accel + r.decel)));"));
			Assert.That(hlsl, Does.Contain("abs(sin(3.14159265 * along / max(0.1, s.hopLength)))"));
			// Every new pass compiles with DXC for Vulkan.
			foreach (string shader in new[] { "FishRockfallStones.shader", "FishRockfallMarks.shader" })
			{
				string text = Read($"{Shaders}/{shader}");
				Assert.That(Regex.Matches(text, "HLSLPROGRAM").Count, Is.EqualTo(Regex.Matches(text, @"#pragma use_dxc vulkan").Count), shader);
			}
		}

		// ── The plumbing and the defaults ──────────────────────────

		[Test]
		public void ThePlumbing_StoresReadsAndDraws()
		{
			string map = Read("Assets/Scripts/Shared/Implementation/Tools/Biomes/SceneBiomeMap.cs");
			Assert.That(map, Does.Contain("public List<RockChute> Chutes").And.Contain("public List<TalusCone> TalusCones"));
			Assert.That(map, Does.Contain("SetRockChutes(null, null, null);"), "a new grid clears them");
			string gen = Read($"{World}/SceneGenerator.cs");
			Assert.That(gen, Does.Contain("StoreRockChutes(map, SceneLandforms.Sites(scene), SceneLandforms.Pads(scene)"));
			string scatter = Read($"{World}/Terrain/TerrainScatter.cs");
			Assert.That(scatter, Does.Contain("if (rule.talusGrading && options?.LandformZones != null)"));
			Assert.That(scatter, Does.Contain("scatterRule.GradeZones.TalusGrade(scatterRule.ZoneBiome, worldX, worldZ)"));
			string authoring = Read($"{World}/ProceduralArt/BiomeArtAuthoring.cs");
			Assert.That(authoring, Does.Contain("talusGrading = s.Graded,").And.Contain("to.talusGrading = from.talusGrading;").And.Contain("text.Append(\"|T\");"));
			string profile = Read("Assets/Scripts/Client/World/Weather/Presentation/WeatherRenderProfile.cs");
			Assert.That(profile, Does.Contain("public Shader RockfallStonesShader;").And.Contain("public Shader RockfallMarksShader;"));
			string assets = Read("Assets/Scripts/Client/World/Weather/Editor/WeatherRenderAssets.cs");
			Assert.That(assets, Does.Contain("profile.RockfallStonesShader = AssetDatabase.LoadAssetAtPath<Shader>(RockfallStonesShaderPath)"));
			Assert.That(assets, Does.Contain("profile.RockfallMarksShader = AssetDatabase.LoadAssetAtPath<Shader>(RockfallMarksShaderPath)"));
			string field = Read("Assets/Scripts/Client/World/Terrain/RockfallField.cs");
			Assert.That(field, Does.Contain("WorldMotion.Seconds").And.Contain("WorldMotion.Wrapped(WorldMotion.ShaderWrapSeconds)"));
			Assert.That(field, Does.Not.Contain("Time.time").And.Not.Contain("deltaTime"), "nothing on a private clock");
			Assert.That(field, Does.Contain("if (!chutes && !cones && !strombolian)"), "a scene with none draws nothing");
		}

		[Test]
		public void Defaults_NoSceneNoRuleAndNoSpecNamesAnyOfIt()
		{
			Assert.That(new PrefabSpawnRule().talusGrading, Is.False);
			Assert.That(new BiomeArtSpec.Scatter().Graded, Is.False);
			foreach (string path in Directory.GetFiles($"{World}/ProceduralArt", "*.cs", SearchOption.AllDirectories))
			{
				string text = Read(path);
				Assert.That(text.Contains("TalusGraded(") && !path.EndsWith("BiomeArtSpec.cs"), Is.False, $"{path} grades a rule");
				Assert.That(Regex.IsMatch(text, "\"(ScreeChutes|Sciara|ScreeCone)\"") && !path.EndsWith("BiomeLandformPresets.cs"), Is.False, $"{path} names a chute or cone");
			}
			// No stored map holds a chute or a cone (a map saved before the fields has none at all).
			foreach (string path in Directory.GetFiles("Assets", "*Biome Map.asset", SearchOption.AllDirectories))
			{
				string text = Read(path);
				Assert.That(Regex.IsMatch(text, @"\n  Chutes:\s*\n  - "), Is.False, $"{path} stores chutes");
				Assert.That(Regex.IsMatch(text, @"\n  TalusCones:\s*\n  - "), Is.False, $"{path} stores talus cones");
			}
			// And no template rule is graded.
			foreach (string path in Directory.GetFiles("Assets/Templates/Entity/Biomes", "*.asset", SearchOption.AllDirectories))
			{
				Assert.That(Read(path), Does.Not.Contain("talusGrading: 1"), path);
			}
		}
	}
}
