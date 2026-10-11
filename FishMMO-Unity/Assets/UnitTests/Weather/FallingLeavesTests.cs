using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// When leaves fall (<see cref="FallingLeaves"/>): exactly while the canopies thin, by the vegetation
	/// shader's own leaf-loss curve; and the canopy map's crown arithmetic (<see cref="LeafCanopyMap"/>).
	/// </summary>
	[TestFixture]
	public class FallingLeavesTests
	{
		private const string VegetationPasses = "Assets/Prefabs/Client/Weather/Shaders/FishVegetationPasses.hlsl";
		private const string LeafShader = "Assets/Prefabs/Client/Weather/Shaders/FishFallingLeaves.shader";

		/// <summary>The canopy's mean bareness over its plants' spread of phase, numerically.</summary>
		private static float MeanBare(float phase, float strength)
		{
			const int Plants = 400;
			float sum = 0f;
			for (int i = 0; i < Plants; i++)
			{
				sum += FallingLeaves.Bare(phase, (i + 0.5f) / Plants, strength);
			}
			return sum / Plants;
		}

		[Test]
		public void FallRate_IsTheRateTheCanopiesThin()
		{
			/* Every leaf a crown loses comes down: the rate is d(mean bareness)/d(phase), scaled so the peak
			 * is 1 (the mean ramps over BareSpan of the year). Checked through the whole autumn. */
			const float H = 0.0005f;
			for (float phase = 0.80f; phase <= 0.99f; phase += 0.0025f)
			{
				float numeric = (MeanBare(phase + H, 1f) - MeanBare(phase - H, 1f)) / (2f * H) * FallingLeaves.BareSpan;
				Assert.AreEqual(numeric, FallingLeaves.SeasonalFall(phase, 1f), 0.03f, $"phase {phase:F4}");
			}
		}

		[Test]
		public void NothingFalls_WhileTheCrownsLeafOutOrStandFull()
		{
			// Spring's leaf-out (bareness falling) and the summer drop nothing seasonal.
			for (float phase = 0.02f; phase <= 0.83f; phase += 0.01f)
			{
				Assert.AreEqual(0f, FallingLeaves.SeasonalFall(phase, 1f), 1e-6f, $"phase {phase:F2}");
			}
		}

		[Test]
		public void PeakFall_JustBeforeMidwinter()
		{
			// Full from 0.88 to 0.90, over by 0.94, begun at 0.84.
			Assert.AreEqual(1f, FallingLeaves.SeasonalFall(0.89f, 1f), 1e-4f);
			Assert.AreEqual(0f, FallingLeaves.SeasonalFall(0.835f, 1f), 1e-4f);
			Assert.AreEqual(0f, FallingLeaves.SeasonalFall(0.945f, 1f), 1e-4f);
			// The swing's strength scales it, and no season known means none.
			Assert.AreEqual(0.5f, FallingLeaves.SeasonalFall(0.89f, 0.5f), 1e-4f);
			Assert.AreEqual(0f, FallingLeaves.SeasonalFall(0.89f, 0f));
		}

		[Test]
		public void OverTheAutumn_AWholeCanopyComesDown()
		{
			// ∫ rate dphase = BareSpan · (the full change in mean bareness) = BareSpan.
			const int Steps = 20000;
			float sum = 0f;
			for (int i = 0; i < Steps; i++)
			{
				sum += FallingLeaves.SeasonalFall((i + 0.5f) / Steps, 1f) / Steps;
			}
			Assert.AreEqual(FallingLeaves.BareSpan, sum, 0.001f);
		}

		[Test]
		public void BareTwin_MatchesTheVegetationShader()
		{
			// The curve is copied from VegSeasonTint; if it changes there, it must change here.
			string hlsl = SourceScanPins.ReadCode(VegetationPasses);
			StringAssert.Contains("float fromWinter = abs(phase - 0.02);", hlsl);
			StringAssert.Contains("float bare = saturate((0.16 - fromWinter + (variation - 0.5) * 0.04) / 0.06) * strength * known;", hlsl);
			Assert.AreEqual(0.02f, FallingLeaves.BareCentre);
			Assert.AreEqual(0.16f, FallingLeaves.BareReach);
			Assert.AreEqual(0.06f, FallingLeaves.BareSpan);
			Assert.AreEqual(0.04f, 2f * FallingLeaves.PlantSpread, 1e-6f);
		}

		[Test]
		public void SomeAllYear_MoreInAGust()
		{
			Vector2 summer = FallingLeaves.Rates(0.5f, 1f, 0f, 0f);
			// Raised above a real wood's few percent for atmosphere (Jim, 2026-10-10): leaves on the air all year.
			Assert.That(summer.x, Is.InRange(0.1f, 0.25f), "a broadleaf wood's summer trickle");
			Assert.That(summer.y, Is.InRange(0.03f, 0.1f), "an evergreen's year-round drop");
			Vector2 autumnCalm = FallingLeaves.Rates(0.89f, 1f, 0f, 0f);
			Vector2 autumnGust = FallingLeaves.Rates(0.89f, 1f, 0.2f, 1f);
			Assert.That(autumnCalm.x, Is.GreaterThan(2.5f * summer.x), "autumn is still the season of falling leaves");
			Assert.That(autumnGust.x, Is.GreaterThan(autumnCalm.x));
			Assert.AreEqual(0.5f, FallingLeaves.WindRelease(0f, 0f), 1e-6f);
			Assert.That(FallingLeaves.WindRelease(1f, 1f), Is.LessThanOrEqualTo(1.5f));
		}

		[Test]
		public void AutumnLeaves_HaveTurned()
		{
			// The all-year trickle (raised 2026-10-10) mixes a few green leaves into the autumn fall: still mostly turned.
			Assert.That(FallingLeaves.TurnedShare(0.89f, 1f, 0f, 0f), Is.GreaterThan(0.85f));
			Assert.AreEqual(0.5f, FallingLeaves.TurnedShare(0.5f, 1f, 0f, 0f), 1e-4f);
		}

		[Test]
		public void Downwind_IsTheDriftSinceTheCrown()
		{
			// Ten metres down at a metre a second in a 3 m/s wind: thirty metres downwind.
			Assert.AreEqual(30f, FallingLeaves.Downwind(12f, 2f, 1f, 3f), 1e-4f);
			Assert.AreEqual(0f, FallingLeaves.Downwind(5f, 8f, 1f, 3f), "not yet let go");
			Assert.AreEqual(0f, FallingLeaves.Downwind(12f, 2f, 1f, 0f), "still air");
		}

		[Test]
		public void TerminalSpeed_IsALeafs()
		{
			// v = √(2mg / ρC_dA): 0.3 g, 30 cm², C_d ≈ 1 → ~1.3 m/s; flutterers and tumblers bracket it.
			float v = Mathf.Sqrt(2f * 0.0003f * 9.81f / (1.2f * 1f * 0.003f));
			Assert.That(v, Is.InRange(FallingLeaves.TerminalSpeed.x, FallingLeaves.TerminalSpeed.y));
		}

		[Test]
		public void CrownCover_OverlapsAtRandom()
		{
			Assert.AreEqual(1f, LeafCanopyMap.CrownWeight(0f, 4f));
			Assert.AreEqual(0f, LeafCanopyMap.CrownWeight(4f, 4f));
			Assert.AreEqual(0.75f, LeafCanopyMap.CrownWeight(2f, 4f), 1e-5f);
			Assert.AreEqual(0f, LeafCanopyMap.Cover(0f));
			Assert.That(LeafCanopyMap.Cover(1f), Is.InRange(0.6f, 0.75f));
			Assert.That(LeafCanopyMap.Cover(10f), Is.GreaterThan(0.99f));
		}

		[Test]
		public void Shader_ShowsALeafOnlyUnderACrownUpwind()
		{
			string shader = SourceScanPins.ReadCode(LeafShader);
			StringAssert.Contains("float density = deciduous + canopy.g * _LeafRates.y;", shader);
			StringAssert.Contains("source = centre.xz - _LeafWind.xy * (_LeafWind.z * drop / fall);", shader);
			// Its fall is a whole 64th of the typical leaf's, so the wrap moves nothing.
			StringAssert.Contains("speed = max(1.0, round(speed * 64.0)) / 64.0;", shader);
			// Lit by the sky's trilight, never SampleSH.
			StringAssert.Contains("FishTrilight(n)", shader);
			StringAssert.DoesNotContain("SampleSH", shader);
		}
	}
}
