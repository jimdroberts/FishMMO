using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// How big snowflakes are and how they are drawn: <see cref="SnowflakeSize"/>, and a grain smaller
	/// than a pixel (<see cref="PrecipitationField.Coverage"/>, <see cref="PrecipitationField.SubPixelAlpha"/>).
	/// Shapes and ranges from the measurements in their remarks, not spot values.
	/// </summary>
	[TestFixture]
	public class SnowflakeTests
	{
		private const string PrecipitationShader = "Assets/Prefabs/Client/Weather/Shaders/FishPrecipitation.shader";

		private static readonly float[] Rates = { 0.2f, 0.5f, 1f, 2f, 4f, 7f, 10f, 20f, 50f };

		// ── The flakes ───────────────────────────────────────────────────

		[Test]
		public void ColdSnowIsSingleCrystals_AMillimetreOrTwo()
		{
			/* Below −8 °C hardly anything sticks, and snow falls as the crystals it grew as: a millimetre
			 * or two, whatever the rate. A blizzard at −10 °C is fine powder, not a storm of saucers. */
			for (float t = -30f; t <= -8f; t += 1f)
			{
				foreach (float rate in Rates)
				{
					float mean = SnowflakeSize.MeanMillimetres(rate, t);
					LogAssert.IsTrue(mean >= 0.5f && mean <= 5f, $"cold snow ({t:0} °C, {rate:0.0} mm/h) is small crystals: {mean:0.00} mm");
				}
			}
			float blizzard = SnowflakeSize.MeanMillimetres(SnowflakeSize.RateMillimetresPerHour(0.8f, 1f), -10f);
			LogAssert.IsTrue(blizzard >= 0.8f && blizzard <= 2f, $"a heavy snow at −10 °C is about a millimetre: {blizzard:0.00} mm");
		}

		[Test]
		public void HeavySnowNearFreezingIsBigAggregates_WithATailToFiveCentimetres()
		{
			/* Within a few degrees of freezing the crystals are sticky and clump: heavy wet snow is
			 * flakes of a centimetre or so, the odd one several. */
			for (float t = -3f; t <= 0.5f; t += 0.5f)
			{
				for (float rate = 5f; rate <= 30f; rate += 2.5f)
				{
					float mean = SnowflakeSize.MeanMillimetres(rate, t);
					LogAssert.IsTrue(mean >= 5f && mean <= 20f, $"heavy snow at {t:0.0} °C, {rate:0.0} mm/h: aggregates of {mean:0.0} mm");
					float largest = SnowflakeSize.LargestMillimetres(t);
					float tail = SnowflakeSize.Draw(mean, largest, 0.99f);
					LogAssert.IsTrue(tail > 20f && tail <= 50f, $"the biggest of them run to a few centimetres: {tail:0.0} mm");
				}
			}
		}

		[Test]
		public void NoFlakeIsEverOverThePhysicalMaximum()
		{
			for (float t = -40f; t <= 2f; t += 0.5f)
			{
				float largest = SnowflakeSize.LargestMillimetres(t);
				LogAssert.IsTrue(largest <= SnowflakeSize.LargestAggregateMillimetres + 1e-4f, $"never past the largest aggregate measured ({largest:0.0} mm at {t:0.0} °C)");
				if (t <= SnowflakeSize.AggregationStarts)
				{
					LogAssert.IsTrue(largest <= SnowflakeSize.LargestCrystalMillimetres + 1e-4f, $"and in cold air never past the largest crystal ({largest:0.0} mm at {t:0} °C)");
				}
				foreach (float rate in Rates)
				{
					float mean = SnowflakeSize.MeanMillimetres(rate, t);
					for (float u = 0f; u < 1f; u += 0.01f)
					{
						float d = SnowflakeSize.Draw(mean, largest, u);
						LogAssert.IsTrue(d >= SnowflakeSize.SmallestMillimetres - 1e-5f && d <= largest + 1e-4f, $"every flake between the smallest and the largest: {d:0.00} mm");
					}
					LogAssert.IsTrue(SnowflakeSize.Draw(mean, largest, 0.999999f) <= largest + 1e-4f, "however far out the random falls");
				}
			}
		}

		[Test]
		public void FlakesGrowWithTheSnowfall_AndTowardFreezing()
		{
			for (float t = -10f; t <= 0f; t += 1f)
			{
				float last = 0f;
				foreach (float rate in Rates)
				{
					float mean = SnowflakeSize.MeanMillimetres(rate, t);
					LogAssert.IsTrue(mean >= last - 1e-4f, $"heavier snow, bigger flakes ({t:0} °C, {rate:0.0} mm/h: {mean:0.00} mm)");
					last = mean;
				}
			}
			foreach (float rate in Rates)
			{
				float last = 0f;
				for (float t = -30f; t <= 0f; t += 0.5f)
				{
					float mean = SnowflakeSize.MeanMillimetres(rate, t);
					LogAssert.IsTrue(mean >= last - 1e-4f, $"warmer, stickier, bigger ({rate:0.0} mm/h, {t:0.0} °C: {mean:0.00} mm)");
					last = mean;
				}
			}
			LogAssert.IsTrue(SnowflakeSize.MeanMillimetres(8f, -1f) > 3f * SnowflakeSize.MeanMillimetres(8f, -12f),
				"and the warmth is what turns crystals into clumps: several times the size near freezing");
		}

		[Test]
		public void TheAggregatesAreTheMeltedSpreadAsFlakesOfDendrites()
		{
			/* The closed form c·Γ(1 + 3/b)·Λ^(−3/b), against the mean of the flake size over Sekhon and
			 * Srivastava's melted exponential, summed numerically. */
			LogAssert.IsTrue(System.Math.Abs(SnowflakeSize.Gamma(5.0) - 24.0) < 1e-9, "Γ(5) = 4!");
			LogAssert.IsTrue(System.Math.Abs(SnowflakeSize.Gamma(0.5) - System.Math.Sqrt(System.Math.PI)) < 1e-9, "Γ(½) = √π");
			double c = System.Math.Pow(System.Math.PI / 6.0 / SnowflakeSize.AggregateMassCoefficient, 1.0 / SnowflakeSize.AggregateMassExponent);
			foreach (float rate in new[] { 0.5f, 1f, 3f, 8f })
			{
				double slope = SnowflakeSize.MeltedSlope * System.Math.Pow(rate, SnowflakeSize.MeltedSlopeExponent);
				double sum = 0.0;
				const double step = 1e-3;
				for (double dm = step * 0.5; dm < 40.0 / slope; dm += step)
				{
					sum += c * System.Math.Pow(dm, 3.0 / SnowflakeSize.AggregateMassExponent) * slope * System.Math.Exp(-slope * dm) * step;
				}
				float closed = SnowflakeSize.AggregateMeanMillimetres(rate);
				LogAssert.IsTrue(System.Math.Abs(closed - sum) < 0.01 * sum, $"at {rate} mm/h the aggregates average {closed:0.00} mm; summed, {sum:0.00}");
			}
			/* A millimetre drop's worth of ice is a four-millimetre flake of dendrites. */
			float oneMilligramFlake = Mathf.Pow(Mathf.PI / 6f / SnowflakeSize.AggregateMassCoefficient, 1f / SnowflakeSize.AggregateMassExponent);
			LogAssert.IsTrue(oneMilligramFlake > 3.5f && oneMilligramFlake < 4.5f, $"a 1 mm drop frozen into an aggregate is {oneMilligramFlake:0.0} mm across");
			LogAssert.IsTrue(SnowflakeSize.AggregateMeanMillimetres(50f) == SnowflakeSize.AggregateMeanMillimetres(SnowflakeSize.HeaviestMillimetresPerHour),
				"past the heaviest snowfall measured the flakes grow no more");
		}

		[Test]
		public void TheRateIsTheChannelsOwnScale()
		{
			LogAssert.IsTrue(Mathf.Abs(SnowflakeSize.RateMillimetresPerHour(1f, 1f) - 50f) < 1e-4f, "full scale is 50 mm/h");
			LogAssert.IsTrue(Mathf.Abs(SnowflakeSize.RateMillimetresPerHour(0.5f, 1f) - 12.5f) < 1e-4f, "and goes as the square");
			LogAssert.IsTrue(Mathf.Abs(SnowflakeSize.RateMillimetresPerHour(0.5f, 0.5f) - 6.25f) < 1e-4f, "snow's share of it");
			string air = SourceScanPins.ReadCode("Assets/Scripts/Shared/Implementation/Weather/AirPhysics.cs");
			LogAssert.IsTrue(air.Contains("float rate = 50f * p * p;"), "as the snowfall's haze reads it (AirPhysics.PrecipitationExtinction)");
		}

		[Test]
		public void AFlakeFallsAtAboutAMetreASecondWhateverItsSize()
		{
			PrecipitationLook snow = PrecipitationLook.Snow();
			LogAssert.IsTrue(Mathf.Abs(snow.FallSpeed.x - 0.8f) < 1e-4f, "0.8 m/s at a millimetre (Locatelli and Hobbs: V = 0.8·D^0.16)");
			float last = 0f;
			for (float d = SnowflakeSize.SmallestMillimetres; d <= SnowflakeSize.LargestAggregateMillimetres; d += 0.5f)
			{
				float v = SnowflakeSize.FallSpeed(d, snow.FallSpeed);
				LogAssert.IsTrue(v >= last - 1e-5f && v >= 0.6f && v <= 1.45f, $"a {d:0.0} mm flake falls at {v:0.00} m/s");
				last = v;
			}
			var still = new FishMMO.Shared.Weather.WeatherFrame();
			float small = -PrecipitationField.FallVelocity(still, snow, FishMMO.Shared.Weather.WeatherChannel.SnowWeight, 0f, snowflakeMillimetres: 1f).y;
			float big = -PrecipitationField.FallVelocity(still, snow, FishMMO.Shared.Weather.WeatherChannel.SnowWeight, 0f, snowflakeMillimetres: 20f).y;
			LogAssert.IsTrue(big > small && big < 1.7f * small, $"twenty times the size, {big / small:0.00} times the speed");
		}

		[Test]
		public void TheSnowLookHoldsTheMeasuredBounds_AndTheProfileAssetAgrees()
		{
			PrecipitationLook snow = PrecipitationLook.Snow();
			LogAssert.IsTrue(Mathf.Abs(snow.Size.x - SnowflakeSize.SmallestMillimetres * 0.001f) < 1e-7f, $"smallest flake {snow.Size.x * 1000f:0.0} mm");
			LogAssert.IsTrue(Mathf.Abs(snow.Size.y - SnowflakeSize.LargestAggregateMillimetres * 0.001f) < 1e-7f, $"largest {snow.Size.y * 1000f:0} mm");
			string asset = SourceScanPins.ReadSource("Assets/Prefabs/Client/Weather/Weather Render Profile.asset");
			Match block = Regex.Match(asset, @"\n  Snow:\s*\n    AtlasRow: \d+\s*\n    Size: \{x: ([0-9.eE-]+), y: ([0-9.eE-]+)\}");
			LogAssert.IsTrue(block.Success, "the profile has a Snow block");
			float x = float.Parse(block.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
			float y = float.Parse(block.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
			LogAssert.IsTrue(Mathf.Abs(x - snow.Size.x) < 1e-6f && Mathf.Abs(y - snow.Size.y) < 1e-6f, $"and it is the measured bounds, not the old 3–8 cm: {x}, {y}");
		}

		[Test]
		public void TheDrawnSizeIsTheFlakesNotItsQuads()
		{
			/* The flake sprite reaches 0.30–0.42 of its tile from the centre: across, 0.72 of the quad
			 * on average. The quad is drawn that much bigger than the flake. */
			string baker = SourceScanPins.ReadCode("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/WeatherTextureBaker.cs");
			Match arm = Regex.Match(baker, @"float armLength = ([0-9.]+)f \+ \(float\)random\.NextDouble\(\) \* ([0-9.]+)f;");
			LogAssert.IsTrue(arm.Success, "the baker draws the flake's arms");
			float shortest = float.Parse(arm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
			float extra = float.Parse(arm.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
			float fill = 2f * (shortest + 0.5f * extra);
			LogAssert.IsTrue(Mathf.Abs(fill - SnowflakeSize.SpriteFill) < 0.02f, $"the flake fills {fill:0.00} of its quad; the renderer takes {SnowflakeSize.SpriteFill:0.00}");

			string shader = SourceScanPins.ReadCode(PrecipitationShader);
			LogAssert.IsTrue(shader.Contains("float ownQuad = own / max(_PrecipGrain.w, 0.05);"), "the shader divides the flake by its fill");
			LogAssert.IsTrue(SourceScanPins.ReadCode("Assets/Scripts/Client/World/Weather/Presentation/PrecipitationField.cs").Contains("SnowflakeSize.SpriteFill);"), "and is told it for snow");
		}

		[Test]
		public void TheShaderDrawsFlakesFromTheSameSpread()
		{
			string shader = SourceScanPins.ReadCode(PrecipitationShader);
			LogAssert.IsTrue(shader.Contains("float e = -log(max(1.0 - input.random.y, 1e-4));"), "exponential from the particle's own random, as Draw");
			LogAssert.IsTrue(shader.Contains("own = min(_PrecipGrain.z, _PrecipGrain.y + max(0.0, _PrecipShape.y - _PrecipGrain.y) * e);"), "about the mean, above the smallest, held at the largest");
			LogAssert.IsTrue(Mathf.Abs(SnowflakeSize.Draw(5f, 50f, 1f - Mathf.Exp(-1f)) - 5f) < 1e-3f, "Draw: a draw one e-folding out is the mean");
		}

		// ── Smaller than a pixel ─────────────────────────────────────────

		[Test]
		public void AGrainUnderThePixelFloorKeepsItsLight()
		{
			/* A grain drawn over more pixels than it covers is made fainter by the area it gained, so
			 * its opacity times its area — the light it adds to the picture — is its own. */
			const float floor = 0.01f;
			for (float own = 0.0005f; own <= 0.03f; own += 0.0005f)
			{
				float drawn = Mathf.Max(own, floor);
				float light = PrecipitationField.Coverage(own, drawn) * drawn * drawn;
				LogAssert.IsTrue(Mathf.Abs(light - own * own) < 1e-9f + 1e-4f * own * own, $"a {own * 1000f:0.0} mm grain drawn {drawn * 1000f:0.0} mm across keeps its light");
				LogAssert.IsTrue(PrecipitationField.Coverage(own, drawn) <= 1f, "and is never brighter than opaque");
			}
			LogAssert.IsTrue(PrecipitationField.Coverage(0f, floor) == 0f, "nothing covers nothing");

			string shader = SourceScanPins.ReadCode(PrecipitationShader);
			LogAssert.IsTrue(shader.Contains("float drawnQuad = max(ownQuad, _PrecipTile.z * pixel) * step(1e-9, own);")
				&& shader.Contains("coverage = gained * gained * max(_PrecipCrowd.x, 1.0);"), "the shader floors and fades as Coverage says");
			LogAssert.IsTrue(shader.Contains("texel.a *= (half)min(1.0, input.coverage);"), "a drawn shape is never more opaque than one grain");
			LogAssert.IsTrue(shader.Contains("saturate(PrecipSubPixel(mean.a, input.grain.z, length(offset), streak) * input.coverage)"), "the dot carries the rest, up to opaque");
		}

		// ── One particle for many flakes ────────────────────────────────

		[Test]
		public void ASnowfallHoldsThousandsOfFlakesACubicMetre()
		{
			PrecipitationLook snow = PrecipitationLook.Snow();
			/* Against Sekhon and Srivastava's own count for aggregate snow, N₀/Λ = 1.09·10³·R^−0.49. */
			foreach (float rate in new[] { 1f, 3f, 10f })
			{
				float mean = SnowflakeSize.MeanMillimetres(rate, -2f);
				float n = SnowflakeSize.FlakesPerCubicMetre(rate, mean, SnowflakeSize.LargestMillimetres(-2f), SnowflakeSize.SmallestMillimetres, snow.FallSpeed);
				float measured = 1.09e3f * Mathf.Pow(rate, -0.49f);
				LogAssert.IsTrue(n > measured / 3f && n < measured * 3f, $"wet snow at {rate} mm/h holds {n:0} flakes a cubic metre; measured {measured:0}");
			}
			float wet = SnowflakeSize.FlakesPerCubicMetre(10f, SnowflakeSize.MeanMillimetres(10f, -1f), SnowflakeSize.LargestMillimetres(-1f), SnowflakeSize.SmallestMillimetres, snow.FallSpeed);
			float powder = SnowflakeSize.FlakesPerCubicMetre(10f, SnowflakeSize.MeanMillimetres(10f, -15f), SnowflakeSize.LargestMillimetres(-15f), SnowflakeSize.SmallestMillimetres, snow.FallSpeed);
			LogAssert.IsTrue(powder > 10f * wet, $"the same water as powder is many more, lighter flakes: {powder:0} against {wet:0}");
			float light = SnowflakeSize.FlakesPerCubicMetre(1f, 2f, 20f, SnowflakeSize.SmallestMillimetres, snow.FallSpeed);
			float heavy = SnowflakeSize.FlakesPerCubicMetre(5f, 2f, 20f, SnowflakeSize.SmallestMillimetres, snow.FallSpeed);
			LogAssert.IsTrue(Mathf.Abs(heavy / light - 5f) < 0.01f, "flakes of the same sizes, five times the water: five times as many");
			LogAssert.IsTrue(SnowflakeSize.FlakesPerCubicMetre(0f, 2f, 20f, SnowflakeSize.SmallestMillimetres, snow.FallSpeed) == 0f, "no snow, no flakes");
		}

		[Test]
		public void AParticleForManyFlakesSpecklesAsTheyWould_AndIsNeverBrighterThanThem()
		{
			/* n flakes of light a scattered at random vary a pixel by a²·n; a particle for n of them
			 * carrying c flakes' light varies it by c² — the same when c = √n — and adds only a 1/√n
			 * share of their mean light, which the haze carries. */
			foreach (float n in new[] { 1f, 40f, 1500f, 90000f })
			{
				float c = PrecipitationField.Crowd(n);
				LogAssert.IsTrue(Mathf.Abs(c * c - n) < 1e-3f * n, $"for {n:0} flakes a particle carries {c:0.0}: the snowfall's own speckle");
				LogAssert.IsTrue(c <= n + 1e-4f && c >= 1f, "never more than the flakes it stands for, never less than one");
			}
			LogAssert.IsTrue(PrecipitationField.Crowd(0.2f) == 1f, "a particle for less than a flake is still one flake");

			/* Under the pixel floor the crowd fills the dot, never past opaque; over it, a drawn flake
			 * stays one flake. */
			const float mean = 0.085f, quad = 3f;
			float crowd = PrecipitationField.Crowd(4000f);
			for (float own = 0.2f; own <= 6f; own += 0.2f)
			{
				float drawn = Mathf.Max(own, quad);
				float coverage = PrecipitationField.Coverage(own, drawn, crowd);
				float sum = 0f;
				for (int px = -3; px <= 3; px++)
				{
					for (int py = -3; py <= 3; py++)
					{
						float dx = px + 0.5f - 0.3f, dy = py + 0.5f - 0.7f;
						if (Mathf.Abs(dx) <= drawn * 0.5f && Mathf.Abs(dy) <= drawn * 0.5f)
						{
							sum += PrecipitationField.SubPixelAlpha(mean, drawn, Mathf.Sqrt(dx * dx + dy * dy), coverage);
						}
					}
				}
				float single = mean * own * own;
				LogAssert.IsTrue(sum <= crowd * single * 1.05f + 1e-4f, $"a {own:0.0} px grain's crowd lights {sum:0.00} px, at most its {crowd * single:0.00}");
				LogAssert.IsTrue(sum >= Mathf.Min(single, 1f) * 0.9f, $"and at least one grain's light, {single:0.00} px");
			}
			LogAssert.IsTrue(PrecipitationField.Coverage(5f, 5f, crowd) >= 1f, "a grain drawn at its own size is given its crowd too...");
			LogAssert.IsTrue(SourceScanPins.ReadCode(PrecipitationShader).Contains("texel.a *= (half)min(1.0, input.coverage);"), "...but its shape is held to one grain's opacity");

			string field = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Weather/Presentation/PrecipitationField.cs");
			LogAssert.IsTrue(field.Contains("crowd = Crowd(flakes / Mathf.Max(1e-6f, particles));"), "snow's particles are told how many flakes each is drawn for");
			LogAssert.IsTrue(field.Contains("block.SetVector(CrowdId, new Vector4(crowd, 0f, 0f, 0f));"), "and every kind's draw sets it");
		}

		[Test]
		public void AnUnresolvedGrainNeitherVanishesNorFlickers()
		{
			/* Swept across the pixel grid, the dot's pixels always add up to the grain's own light: it
			 * does not blink in and out as it falls between pixel centres. */
			const float mean = 0.085f;
			// Up to four pixels: a bigger quad holds a grain big enough to show its shape instead.
			foreach (float quad in new[] { PrecipitationField.MinQuadPixels, 3.5f, 4f })
			{
				float expected = mean * quad * quad;
				float lowest = float.MaxValue, highest = 0f;
				for (float ox = 0f; ox < 1f; ox += 0.125f)
				{
					for (float oy = 0f; oy < 1f; oy += 0.125f)
					{
						float sum = 0f;
						for (int px = -6; px <= 6; px++)
						{
							for (int py = -6; py <= 6; py++)
							{
								// Pixel centres against a grain centred at (ox, oy); only those inside its quad draw.
								float dx = px + 0.5f - ox, dy = py + 0.5f - oy;
								if (Mathf.Abs(dx) > quad * 0.5f || Mathf.Abs(dy) > quad * 0.5f)
								{
									continue;
								}
								sum += PrecipitationField.SubPixelAlpha(mean, quad, Mathf.Sqrt(dx * dx + dy * dy));
							}
						}
						lowest = Mathf.Min(lowest, sum);
						highest = Mathf.Max(highest, sum);
					}
				}
				LogAssert.IsTrue(lowest > 0.95f * expected && highest < 1.05f * expected,
					$"over a {quad:0} px quad the dot sums to {lowest / expected:0.000}–{highest / expected:0.000} of the grain's light wherever it sits");
			}
			string shader = SourceScanPins.ReadCode(PrecipitationShader);
			LogAssert.IsTrue(shader.Contains("float spread = 2.0 * PI * s * s + sqrt(2.0 * PI) * s * max(0.0, streakPixels);")
				&& shader.Contains("return meanCoverage * quadPixels * quadPixels * exp(-offsetPixels * offsetPixels / (2.0 * s * s)) / spread;"),
				"the shader's dot is SubPixelAlpha");
			LogAssert.IsTrue(shader.Contains("SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, input.uv.zw, _PrecipTile.x)"), "with the tile's mean coverage from the mip where a texel is the tile");
		}

		// ── Near enough to count ─────────────────────────────────────────

		[Test]
		public void TheShellsTileTheDistancesAndHoldTheSpeckleAcrossEverySeam()
		{
			/* Each shell is drawn from where the next one in stops, and the squares of the two weights add
			 * to one where they meet: the snow's speckle is the variance of the light, which goes as the
			 * square of each particle's, so it is as speckled across a seam as either side of it. */
			foreach (float box in new[] { 18f, 24f, 30f })
			{
				int shells = PrecipitationField.SnowShells;
				for (int shell = 0; shell + 1 < shells; shell++)
				{
					Vector4 outer = PrecipitationField.ShellFades(box, shell, shells);
					Vector4 inner = PrecipitationField.ShellFades(box, shell + 1, shells);
					LogAssert.IsTrue(Mathf.Abs(outer.x - inner.z) < 1e-5f && Mathf.Abs(outer.y - inner.w) < 1e-5f, $"{box} m, shell {shell}: it fades in where the next one in fades out");
					for (float d = outer.x; d <= outer.y; d += (outer.y - outer.x) / 16f)
					{
						float a = PrecipitationField.ShellWeight(d, outer), b = PrecipitationField.ShellWeight(d, inner);
						LogAssert.IsTrue(Mathf.Abs(a * a + b * b - 1f) < 1e-3f, $"{box} m at {d:0.00} m: the speckle holds across the seam ({a * a + b * b:0.000})");
					}
				}
				Vector4 nearest = PrecipitationField.ShellFades(box, shells - 1, shells);
				LogAssert.IsTrue(nearest.x >= 0.2f && nearest.y <= 0.6f, $"the innermost starts at the camera's near side: {nearest.x:0.00}–{nearest.y:0.00} m");
				Vector4 outermost = PrecipitationField.ShellFades(box, 0, shells);
				LogAssert.IsTrue(Mathf.Abs(outermost.w - 0.5f * box) < 1e-4f, "and the outermost fades out by its box's edge, as the one field always did");
				Vector4 single = PrecipitationField.ShellFades(box, 0, 1);
				LogAssert.IsTrue(Mathf.Abs(single.x - nearest.x) < 1e-5f && Mathf.Abs(single.w - 0.5f * box) < 1e-4f, "a kind drawn in one field runs from the camera to its edge");
			}
		}

		[Test]
		public void TheNearFieldHoldsASnowfallsOwnCount_NeverMore()
		{
			/* The innermost shell of a heavy snowfall is drawn nearly one particle to a flake, so the
			 * flakes within a metre or two are there at their real number; in a light one there are
			 * fewer flakes than particles, and the shell shows no more than the sky let fall. */
			PrecipitationLook snow = PrecipitationLook.Snow();
			const int particles = 8000;
			const float box = 24f;
			float Volume(int shell) { float b = PrecipitationField.ShellBox(box, shell); return b * b * 0.75f * b; }
			foreach (float warmth in new[] { -1f, -3f, -7f, -15f })
			{
				foreach (float rate in new[] { 0.2f, 1f, 5f, 10f })
				{
					float mean = SnowflakeSize.MeanMillimetres(rate, warmth);
					float largest = SnowflakeSize.LargestMillimetres(warmth);
					float flakes = SnowflakeSize.FlakesPerCubicMetre(rate, mean, largest, SnowflakeSize.SmallestMillimetres, snow.FallSpeed);
					for (int shell = 0; shell < PrecipitationField.SnowShells; shell++)
					{
						float shown = PrecipitationField.ShownShare(1f, flakes, particles, Volume(shell));
						float density = particles * shown / Volume(shell);
						LogAssert.IsTrue(density <= flakes * 1.0001f + 1e-4f, $"{warmth} °C, {rate} mm/h, shell {shell}: {density:0} particles a cubic metre, at most the {flakes:0} flakes");
						LogAssert.IsTrue(PrecipitationField.Crowd(flakes / Mathf.Max(1e-6f, density)) >= 1f, "a particle is at least one flake");
					}
					float nearest = particles * PrecipitationField.ShownShare(1f, flakes, particles, Volume(PrecipitationField.SnowShells - 1)) / Volume(PrecipitationField.SnowShells - 1);
					LogAssert.IsTrue(nearest >= Mathf.Min(flakes, 300f), $"{warmth} °C, {rate} mm/h: the near field holds {nearest:0} of the {flakes:0} flakes a cubic metre — hundreds, not the one it had");
				}
			}
			LogAssert.IsTrue(PrecipitationField.ShownShare(0.3f, 1e6f, particles, 10f) == 0.3f, "and a heavy fall shows the kind's own share");
			LogAssert.IsTrue(PrecipitationField.ShownShare(1f, 0f, particles, 10f) == 0f, "no flakes, no particles");
		}

		[Test]
		public void AStreakHoldsTheFlakesLight_HoweverLong()
		{
			/* A flake falling across the line of sight moves over the eye's moment; drawn along that, its
			 * light is spread along the streak and never added to. */
			const float mean = 0.085f, quad = 3f;
			foreach (float streak in new[] { 0f, 2f, 6f, 15f })
			{
				float expected = mean * quad * quad;
				float sum = 0f;
				for (float x = -12f; x <= 12f; x += 0.25f)
				{
					for (float y = -3f; y <= 3f; y += 0.25f)
					{
						// Distance from the streak's path: across it, and along it past either end.
						float along = Mathf.Max(0f, Mathf.Abs(x) - 0.5f * streak);
						sum += PrecipitationField.SubPixelAlpha(mean, quad, Mathf.Sqrt(along * along + y * y), 1f, streak) * 0.0625f;
					}
				}
				LogAssert.IsTrue(Mathf.Abs(sum - expected) < 0.03f * expected, $"a {streak:0} px streak holds {sum / expected:0.000} of the flake's light");
			}
			float oneMetre = SnowflakeSize.FallSpeed(3f, PrecipitationLook.Snow().FallSpeed) * PrecipitationField.StreakSeconds;
			LogAssert.IsTrue(oneMetre > 0.01f && oneMetre < 0.03f, $"a flake moves {oneMetre * 100f:0.0} cm over the eye's moment: a streak of pixels near, a dot far");

			string shader = SourceScanPins.ReadCode(PrecipitationShader);
			LogAssert.IsTrue(shader.Contains("float3 across = velocity - dot(velocity, view) * view;"), "the streak is the motion across the line of sight");
			LogAssert.IsTrue(shader.Contains("texel.a *= (half)input.motion.y;"), "and a drawn shape is smeared, not brightened, along it");
			string field = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Weather/Presentation/PrecipitationField.cs");
			LogAssert.IsTrue(field.Contains("exposure = StreakSeconds;") && field.Contains("shells = SnowShells;"), "snow is drawn in its shells, streaked");
		}

		[Test]
		public void AFlakeInTheFogIsFoggedAsTheGroundBehindItIs()
		{
			/* The fog's layer is drawn before the falling snow; a flake ten metres into a dense fog is half
			 * hidden by it, and was drawn as clear as one at arm's length. */
			string shader = SourceScanPins.ReadCode(PrecipitationShader);
			LogAssert.IsTrue(shader.Contains("#include \"FishFogLayer.hlsl\""), "the drops know the fog's layer");
			LogAssert.IsTrue(shader.Contains("lit = lit * through + inFog * (1.0 - through);"), "and lay it between themselves and the eye");
			LogAssert.IsTrue(shader.Contains("FishFogSunShare(centre)"), "lit as the fog there is, by the cloud over it");
			LogAssert.IsTrue(shader.Contains("_FishFogLayerShape.w > 0.5"), "only while something has drawn the layer; else the distance fog carries it");
		}
	}
}
