using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;
using FishMMO.Water;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The floater's physics as pure functions: natural periods and damping from a body's size and
	/// mass, an integrator that keeps its period and damping and cannot blow up, the plane fitted to
	/// the sea, and the outcome that matters — large bergs barely move and growlers bob.
	/// </summary>
	[TestFixture]
	public class WaterFloaterTests
	{
		private const float G = 9.81f;
		private const float Rho = WaterFloater.SeaWaterDensity;

		[Test]
		public void HeavePeriod_IsTheBoxFormula()
		{
			// A 10 × 10 m box floating 8 m deep: m = ρAd, a₃₃ = (4/3)ρa³, T = 2π√((m + a₃₃)/(ρgA)).
			float area = 100f, draught = 8f;
			float mass = Rho * area * draught;
			float a = Mathf.Sqrt(area / Mathf.PI);
			float expected = 2f * Mathf.PI * Mathf.Sqrt((mass + 4f / 3f * Rho * a * a * a) / (Rho * G * area));
			float omega = WaterFloater.HeaveNaturalFrequency(mass, area, Rho, G);
			Assert.That(WaterFloater.NaturalPeriod(omega), Is.EqualTo(expected).Within(1e-3f));
			// Without the added mass it would be the textbook 2π√(d/g); the water makes it slower.
			Assert.That(WaterFloater.NaturalPeriod(omega), Is.GreaterThan(2f * Mathf.PI * Mathf.Sqrt(draught / G)));
		}

		[Test]
		public void MetacentricHeight_OfABox_IsBMMinusBG()
		{
			// 10 m wide, 10 m long, 8 m under and 2 m over: BM = W²/(12d) = 1.0417, B at −4, G at −3.
			float gm = WaterFloater.MetacentricHeight(800f, 10f * 1000f / 12f, -4f, -3f);
			Assert.That(gm, Is.EqualTo(100f / (12f * 8f) - 1f).Within(1e-4f));
		}

		[Test]
		public void TiltPeriod_GrowsWithInertia_AndShrinksWithStability()
		{
			float area = 100f, volume = 800f, mass = Rho * volume;
			float slow = WaterFloater.TiltNaturalFrequency(mass, 4f, volume, 0.5f, area, Rho, G);
			float heavier = WaterFloater.TiltNaturalFrequency(mass, 6f, volume, 0.5f, area, Rho, G);
			float stiffer = WaterFloater.TiltNaturalFrequency(mass, 4f, volume, 2f, area, Rho, G);
			Assert.That(heavier, Is.LessThan(slow));
			Assert.That(stiffer, Is.GreaterThan(slow));
			Assert.That(WaterFloater.TiltNaturalFrequency(mass, 4f, volume, -1f, area, Rho, G), Is.EqualTo(0f), "unstable has no restoring");
		}

		[Test]
		public void Damping_IsRadiationOnTopOfViscous_AndFlatBodiesAreDampedMore()
		{
			// The same waterplane, one a raft and one a deep keel: the raft radiates far more.
			float area = 400f;
			float raftMass = Rho * area * 1f, keelMass = Rho * area * 100f;
			float raftOmega = WaterFloater.HeaveNaturalFrequency(raftMass, area, Rho, G);
			float keelOmega = WaterFloater.HeaveNaturalFrequency(keelMass, area, Rho, G);
			float raft = WaterFloater.HeaveDampingRatio(raftOmega, area, raftMass, 1f, Rho, G, 0.05f);
			float keel = WaterFloater.HeaveDampingRatio(keelOmega, area, keelMass, 100f, Rho, G, 0.05f);
			Assert.That(keel, Is.GreaterThanOrEqualTo(0.05f));
			Assert.That(keel, Is.LessThan(0.1f));
			Assert.That(raft, Is.GreaterThan(1f), "a raft is overdamped");
			float tilt = WaterFloater.TiltDampingRatio(1f, 1e4f, 1e8f, 2f, Rho, G, 0.05f);
			Assert.That(tilt, Is.GreaterThan(0.05f));
		}

		[Test]
		public void Step_KeepsThePeriodAndDampingItIsGiven()
		{
			float omega = 2f, zeta = 0.1f, x = 1f, v = 0f, t = 0f, dt = 0.001f;
			float previous = x, before = x;
			var peaks = new System.Collections.Generic.List<(float time, float value)>();
			for (int i = 0; i < 20000; i++)
			{
				WaterFloater.Step(ref x, ref v, 0f, 0f, omega, zeta, dt);
				t += dt;
				if (previous > before && previous > x)
				{
					peaks.Add((t - dt, previous));
				}
				before = previous;
				previous = x;
			}
			Assert.That(peaks.Count, Is.GreaterThan(3));
			float period = peaks[2].time - peaks[1].time;
			float expected = 2f * Mathf.PI / (omega * Mathf.Sqrt(1f - zeta * zeta));
			Assert.That(period, Is.EqualTo(expected).Within(expected * 0.01f));
			float decrement = Mathf.Log(peaks[1].value / peaks[2].value);
			float measured = decrement / Mathf.Sqrt(4f * Mathf.PI * Mathf.PI + decrement * decrement);
			Assert.That(measured, Is.EqualTo(zeta).Within(0.01f));
		}

		[Test]
		public void Step_IsStableAtAnyStep_AndSettlesOnItsTarget()
		{
			float x = 0f, v = 0f;
			for (int i = 0; i < 200; i++)
			{
				// A stiff, barely damped body stepped a second at a time: explicit Euler would explode.
				WaterFloater.Step(ref x, ref v, 3f, 0f, 30f, 0.01f, 1f);
				Assert.That(float.IsFinite(x) && Mathf.Abs(x) < 10f, $"step {i}: {x}");
			}
			Assert.That(x, Is.EqualTo(3f).Within(1e-3f));
		}

		[Test]
		public void AHeavilyDampedBody_FollowsTheWater_RatherThanLaggingIt()
		{
			// Damping acts against the water's motion, so a raft rides the surface (response → 1).
			Assert.That(WaterFloater.ResponseAmplitude(0.8f, 1.5f, 4f), Is.EqualTo(1f).Within(0.05f));
			// And a body far stiffer than the waves follows them whatever its damping.
			Assert.That(WaterFloater.ResponseAmplitude(0.1f, 5f, 0.05f), Is.EqualTo(1f).Within(0.01f));
		}

		[Test]
		public void FitPlane_RecoversAPlaneExactly()
		{
			float[] x = { 0f, -3f, 3f }, z = { 2f, -1f, -1f }, h = new float[3];
			for (int i = 0; i < 3; i++)
			{
				h[i] = 0.4f + 0.05f * x[i] - 0.02f * z[i];
			}
			WaterFloater.FitPlane(x, z, h, 3, out float mean, out float sx, out float sz);
			Assert.That(mean, Is.EqualTo(0.4f).Within(1e-5f));
			Assert.That(sx, Is.EqualTo(0.05f).Within(1e-5f));
			Assert.That(sz, Is.EqualTo(-0.02f).Within(1e-5f));
		}

		[Test]
		public void DeepWater_AttenuatesWithDepth()
		{
			float k = WaterFloater.DeepWaterWavenumber(8f, G);
			Assert.That(k, Is.EqualTo(Mathf.Pow(2f * Mathf.PI / 8f, 2f) / G).Within(1e-5f));
			Assert.That(WaterFloater.DepthAttenuation(k, 0f), Is.EqualTo(1f));
			Assert.That(WaterFloater.DepthAttenuation(k, 100f), Is.LessThan(0.01f));
			Assert.That(WaterFloater.PeakPeriod(8f, G), Is.EqualTo(2f * Mathf.PI * 8f / (0.877f * G)).Within(1e-3f));
		}

		/// <summary>Heave motion over wave height for a catalogue berg in a sea of this period.</summary>
		private static float Heave(IcebergShape shape, float period)
		{
			FloatingBody b = IceMeshes.BuildIceberg(in shape, IceMeshes.Resolution[0], 1).Body;
			float omega = WaterFloater.HeaveNaturalFrequency(b.Mass, b.WaterplaneArea, Rho, G);
			float zeta = WaterFloater.HeaveDampingRatio(omega, b.WaterplaneArea, b.Mass, b.MeanDraught, Rho, G, 0.05f);
			return WaterFloater.ResponseAmplitude(2f * Mathf.PI / period, omega, zeta)
				* WaterFloater.DepthAttenuation(WaterFloater.DeepWaterWavenumber(period, G), b.MeanDraught);
		}

		[Test]
		public void LargeBergsBarelyMove_AndGrowlersBob()
		{
			IcebergShape growler = default, large = default;
			foreach (IcebergShape s in IceMeshes.Icebergs)
			{
				if (s.Size == IcebergSize.Growler) growler = s;
				if (s.Size == IcebergSize.Large) large = s;
			}
			foreach (float period in new[] { 6f, 8f, 10f })
			{
				Assert.That(Heave(growler, period), Is.GreaterThan(0.9f), $"growler in a {period} s sea");
				Assert.That(Heave(large, period), Is.LessThan(0.05f), $"{large.Name} in a {period} s sea");
			}
			FloatingBody g = IceMeshes.BuildIceberg(in growler, IceMeshes.Resolution[0], 1).Body;
			FloatingBody l = IceMeshes.BuildIceberg(in large, IceMeshes.Resolution[0], 1).Body;
			Assert.That(g.HeavePeriod(G), Is.LessThan(5f));
			Assert.That(l.HeavePeriod(G), Is.GreaterThan(20f));
		}

		[Test]
		public void AnEstimatedBody_IsUsable()
		{
			var bounds = new Bounds(new Vector3(0f, -10f, 0f), new Vector3(40f, 30f, 30f));
			FloatingBody b = FloatingBody.Estimate(bounds, WaterFloater.GlacialIceDensity);
			Assert.That(b.IsValid);
			Assert.That(b.MeanDraught, Is.GreaterThan(0f).And.LessThan(25f));
			Assert.That(float.IsFinite(b.HeavePeriod(G)) && b.HeavePeriod(G) > 0f);
		}
	}
}
