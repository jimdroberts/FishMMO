using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The fog as cloud whose base is the ground: the CPU side of the cloud march walking it — where its
	/// shell lies over the terrain, what it takes out of the light, how it drifts, and that its drops are
	/// drawn once and only once.
	/// </summary>
	/// <remarks>
	/// Pinned to what a fog is and to what the march needs of it, not to the numbers the code happened to
	/// give: the layer the march walks is the one the fog's physics made (FogLayer), with the fog's own
	/// extinction from its water; its shell holds every top it can have over every ground in view and
	/// reaches down to the lowest ground; there is no shell at all when there is no fog, and the march
	/// then starts at the clouds; the pipeline's distance fog and the sky's horizon leave the drops out
	/// while their layer is drawn; the fog drifts on the wind at its own height, which near the ground
	/// is less than the ten-metre wind; its structure moves it about without changing how much of it
	/// there is; and its top is a smooth fade of its density with height — as deep as the physics says a
	/// real fog's is — never a surface with texture on it.
	/// </remarks>
	[TestFixture]
	public class FogCloudTests
	{
		private static PlanetAir Earth => PlanetAir.Earthlike;

		/// <summary>Calm, damp air at dawn under a high: a fog morning (FogLayerTests.Dawn).</summary>
		private static WeatherDriver.Synoptic Dawn(float humidity = 0.62f, float wind = 0.5f)
		{
			return new WeatherDriver.Synoptic
			{
				Humidity = humidity,
				Pressure = 0.6f,
				Instability = 0.15f,
				Wind = new Vector2(wind, 0f),
				LocalTime01 = 0.27f,
			};
		}

		/// <summary>A frame carrying the fog the physics makes of this much fog in that air.</summary>
		private static WeatherFrame FogFrame(float fog, float humidity = 0.62f, float windAtTen = 0.5f)
		{
			WeatherDriver.Synoptic air = Dawn(humidity, windAtTen);
			AirColumn column = AirColumn.Of(Earth, 285f, air.Humidity, air.Pressure, air.Instability);
			var frame = new WeatherFrame();
			frame[WeatherChannel.FogDensity] = fog;
			frame[WeatherChannel.WindSpeed] = windAtTen / 30f;
			FogLayer.Of(air, column, Earth, fog, 0.2f).WriteTo(ref frame);
			return frame;
		}

		// ── The band is the fog's physics ─────────────────────────────

		[Test]
		public void TheMarchedFogIsTheLayerThePhysicsMade()
		{
			float[] fogs = { 0.16f, 0.5f, 1f };
			float lastDepth = 0f, lastExtinction = 0f;
			foreach (float fog in fogs)
			{
				WeatherFrame frame = FogFrame(fog);
				FogLayer layer = FogLayer.In(frame);
				FogLayerView view = FogLayerView.Of(frame);
				LogAssert.IsTrue(view.Visible, $"fog {fog:0.00} is a layer to march");
				Assert.That(view.Depth, Is.EqualTo(layer.Depth).Within(1e-3f), "as deep as the night chilled it");
				Assert.That(view.Extinction, Is.EqualTo(AirPhysics.FogExtinction(frame[WeatherChannel.FogDensity])).Within(1e-9f),
					"and as thick as its water makes it");
				LogAssert.IsTrue(view.Depth > lastDepth && view.Extinction > lastExtinction, $"deeper and thicker as it thickens: {view.Depth:0} m, {view.Extinction:0.0000}/m");
				lastDepth = view.Depth;
				lastExtinction = view.Extinction;
			}
			// Koschmieder: a mist is kilometres of view, a dense fog tens of metres.
			float mist = 3.912f / FogLayerView.Of(FogFrame(0.16f)).Extinction;
			float dense = 3.912f / FogLayerView.Of(FogFrame(1f)).Extinction;
			Assert.That(mist, Is.InRange(1000f, 10000f), "a mist: kilometres of visibility");
			Assert.That(dense, Is.InRange(30f, 150f), "a dense fog: tens of metres");
		}

		// ── The shell ─────────────────────────────────────────────────

		[Test]
		public void TheShellHoldsEveryTopTheFogCanHaveOverTheGroundInView()
		{
			var views = new[]
			{
				new FogLayerView { Extinction = 1e-3f, Depth = 20f, TopSoftness = 1f },
				new FogLayerView { Extinction = 1e-3f, Depth = 20f, TopSoftness = 20f },
				new FogLayerView { Extinction = 0.013f, Depth = 150f, TopSoftness = 37f, Lift = 0.5f },
				new FogLayerView { Extinction = 0.065f, Depth = 400f, TopSoftness = 20f },
			};
			const float lowest = 0f, highest = 1600f, pooled = 700f;
			foreach (FogLayerView view in views)
			{
				Vector2 shell = view.Shell(highest, pooled);
				LogAssert.IsTrue(shell.y > shell.x, "a fog has a shell");
				for (float ground = lowest; ground <= highest; ground += 37f)
				{
					for (float pool = lowest; pool <= pooled; pool += 53f)
					{
						FogLayerView.Column column = view.Over(ground, pool);
						// The top of its fade is the last height with any fog in it (FishFogDensity): under
						// the ceiling, and nothing there above it.
						float highestFog = column.Top + column.Soft;
						LogAssert.IsTrue(highestFog < shell.y, $"over ground at {ground:0} m (pool {pool:0} m) the fog reaches {highestFog:0} m, under a ceiling of {shell.y:0} m");
						LogAssert.AreEqual(0f, column.Profile(highestFog + 1e-3f), "no fog over the top of its fade");
					}
				}
				// And down to sea level: a lying fog fills down to whatever ground a ray meets, and all of
				// it is above the sea.
				LogAssert.IsTrue(shell.x <= 0f, $"the floor {shell.x:0} m reaches sea level");
			}
		}

		[Test]
		public void TheShellFollowsTheTerrainAndTheFogPoolsInTheValleys()
		{
			var view = new FogLayerView { Extinction = 0.013f, Depth = 80f, TopSoftness = 4f };
			Vector2 flat = view.Shell(0f, 0f);
			Vector2 mountain = view.Shell(1600f, 600f);
			// Over flat ground the shell is the fog and a little: the whole kilometre up to the clouds is
			// skipped. Over a mountain it rises with the pooled air — a fog does not climb a peak, it
			// leaves a skin on it.
			LogAssert.IsTrue(flat.y < view.Depth * 2f, $"over the plain the shell is the fog's own depth and its top: {flat.y:0} m");
			LogAssert.IsTrue(mountain.y > 600f + view.Depth, $"over the mountain it rises with the pooled air: {mountain.y:0} m");
			LogAssert.IsTrue(mountain.y - 1600f < 0.5f * view.Depth, $"and over the summit holds only the skin a ridge keeps, not the fog's depth: {mountain.y - 1600f:0} m over it");

			// The layer itself: a valley under the pooled air is full to the pool's level, a ridge over
			// it has a skin; lying on the ground, it has no base above the valley floor, so a gorge
			// narrower than the terrain map's texel (the Cov Viaduct), whose floor the map puts far too
			// high, fills to its real floor.
			FogLayerView.Column valley = view.Over(140f, 200f);
			FogLayerView.Column ridge = view.Over(300f, 200f);
			LogAssert.IsTrue(valley.Top - 140f > view.Depth, $"the valley fills to the pool's level: {valley.Top - 140f:0} m deep");
			LogAssert.IsTrue(ridge.Top - 300f <= view.Depth * FogLayerView.RidgeSkin + 1e-3f, $"the ridge stands out of it under a skin: {ridge.Top - 300f:0} m");
			LogAssert.IsTrue(valley.Base - valley.BaseSoft <= 0.001f, "a lying fog has no base above any ground there is");
			Assert.That(valley.Profile(20f), Is.EqualTo(1f).Within(1e-6f), "so a gorge a hundred metres under the map's ground is full of it");
		}

		[Test]
		public void AFogLiesOnTheSeaAndStopsAtItsSurface()
		{
			// Fog is air: over the sea it lies on the water and none of it is under the surface — where the
			// rays end on the sea bed, and the sea, drawn later, lays what the march found in front of it
			// over itself (FishWaterFog.hlsl).
			var view = new FogLayerView { Extinction = 0.013f, Depth = 60f, TopSoftness = 3f };
			FogLayerView.Column overSea = view.Over(-35f, -10f);
			LogAssert.AreEqual(0f, overSea.Profile(-0.01f), "nothing under the surface");
			LogAssert.AreEqual(0f, overSea.Profile(-20f), "nor down at the sea bed");
			Assert.That(overSea.Profile(1f), Is.EqualTo(1f).Within(1e-6f), "and all of it a metre up");
			LogAssert.IsTrue(overSea.Top >= view.Depth - 1e-3f, $"as deep over the sea as anywhere: top {overSea.Top:0} m");
			// And the march's range stops there too.
			Vector2 shell = view.Shell(0f, 0f);
			Assert.That(shell.x, Is.EqualTo(-FogLayerView.ShellMargin).Within(1e-6f), "the shell's floor is sea level");
		}

		[Test]
		public void NoFogNoShellAndTheMarchStartsAtTheClouds()
		{
			// Nothing in the frame: no layer, no shell, and the air between the ground and the deck is
			// not walked at all.
			FogLayerView clear = FogLayerView.Of(WeatherFrame.Clear);
			LogAssert.IsFalse(clear.Visible, "no fog, no layer");
			Vector2 none = clear.Shell(900f, 400f);
			LogAssert.IsTrue(none.y < none.x, "and no shell");
			LogAssert.AreEqual(800f, SkySystem.CloudStackFloor(800f, false, false, none.x), "the march starts at the lowest cloud");
			// A frame whose fog the wind mixed out has no layer either (FogLayer.WriteTo takes the drops out).
			WeatherFrame mixed = FogFrame(0.6f, humidity: 0.62f, windAtTen: 8f);
			LogAssert.IsFalse(FogLayerView.Of(mixed).Visible, "a fog the breeze mixed out is not marched");

			// A fog takes the floor down to its own, sea level, under every ground it lies on; rain haze to
			// the ground.
			Vector2 fog = FogLayerView.Of(FogFrame(0.5f)).Shell(900f, 400f);
			float withFog = SkySystem.CloudStackFloor(800f, false, true, fog.x);
			LogAssert.IsTrue(withFog <= 0f, $"with a fog the march reaches down to sea level: {withFog:0} m");
			LogAssert.AreEqual(0f, SkySystem.CloudStackFloor(800f, true, false, 0f), "rain haze hangs to the ground");
			LogAssert.AreEqual(0f, SkySystem.CloudStackFloor(float.MaxValue, false, false, 0f), "no cloud at all: from the ground");
		}

		// ── The top ───────────────────────────────────────────────────

		/// <summary>A fog of this depth, wind and lift, as the renderer draws it (the layer written straight in).</summary>
		private static FogLayerView Drawn(float depth, float windAtTen, float lift, float fog)
		{
			var frame = new WeatherFrame();
			frame[WeatherChannel.FogDensity] = fog;
			frame[WeatherChannel.WindSpeed] = windAtTen / 30f;
			new FogLayer { Depth = depth, Lift = lift }.WriteTo(ref frame);
			return FogLayerView.Of(frame);
		}

		/// <summary>From a thin still ground mist to a deep fog in a breeze and a lifted sheet.</summary>
		private static FogLayerView[] Fogs() => new[]
		{
			Drawn(12f, 0.5f, 0f, 0.16f),
			Drawn(40f, 0.5f, 0f, 0.3f),
			Drawn(75f, 2f, 0f, 0.5f),
			Drawn(150f, 0.5f, 0f, 0.8f),
			Drawn(150f, 8f, 0f, 0.8f),
			Drawn(300f, 0.5f, 0.8f, 1f),
		};

		[Test]
		public void TheTopFallsOffSmoothlyAndOnlyOnce()
		{
			// A fog's top is its inversion, and its water falls away under it smoothly (Costabloz et al.
			// 2025): a fade, not a surface. Walked up through it the density never rises again once it has
			// started to fall, falls no faster than over the top sixth of the deepest, best-mixed fog, and
			// bends no more sharply than the fade's own smoothstep — no crease where the fade starts or
			// stops, and nothing finer riding on it. And nothing displaces it: there is no heave.
			const float ground = 100f;
			foreach (FogLayerView view in Fogs())
			{
				LogAssert.AreEqual(0f, view.Heave, "the top is never displaced");
				FogLayerView.Column column = view.Over(ground, ground);
				float from = Mathf.Max(ground, column.Base + column.BaseSoft);
				float to = column.Top + column.Soft + 1f;
				// Coarse enough that float rounding near 1 is far below the curvature a fade has; a
				// second difference of a cubic is its second derivative exactly, whatever the step.
				const int N = 400;
				float h = (to - from) / N;
				float steepest = 0f, sharpest = 0f;
				float previous = column.Profile(from);
				Assert.That(previous, Is.EqualTo(1f).Within(1e-4f), $"{view.Depth:0} m: full at the foot of its top");
				float before = previous;
				for (int i = 1; i <= N; i++)
				{
					float p = column.Profile(from + i * h);
					LogAssert.IsTrue(p <= previous + 1e-6f, $"{view.Depth:0} m deep: the density never rises again on the way up ({p:0.0000} after {previous:0.0000} at {from + i * h:0.0} m)");
					steepest = Mathf.Max(steepest, (previous - p) / h);
					if (i >= 2)
					{
						sharpest = Mathf.Max(sharpest, Mathf.Abs(p - 2f * previous + before) / (h * h));
					}
					before = previous;
					previous = p;
				}
				LogAssert.AreEqual(0f, previous, "and nothing is left over the top of its fade");
				// A smoothstep across 2·soft falls at most 0.75/soft per metre and bends at most 1.5/soft².
				float soft = column.Soft;
				LogAssert.IsTrue(steepest <= 0.75f / soft * 1.01f, $"{view.Depth:0} m: falls no faster than its fade ({steepest:0.0000}/m)");
				LogAssert.IsTrue(steepest <= 0.75f / (FogLayerView.MixedTopShare * view.Depth) * 1.01f,
					$"{view.Depth:0} m: no faster than over the top sixth of a mixed fog ({steepest:0.0000}/m)");
				LogAssert.IsTrue(sharpest <= 1.5f / (soft * soft) * 1.05f + 1e-4f, $"{view.Depth:0} m: no crease and nothing finer ({sharpest:0.00000}/m²)");
			}
		}

		[Test]
		public void TheTopIsAsSoftAsTheFogsPhysics()
		{
			const float ground = 100f;
			// A thin still mist holds its water at the ground and less all the way up: its fade starts at
			// the ground.
			FogLayerView mist = Drawn(20f, 0.5f, 0f, 0.16f);
			Assert.That(FogLayerView.TopShare(20f, 0f, 0f), Is.EqualTo(FogLayerView.StillTopShare).Within(1e-6f));
			FogLayerView.Column thin = mist.Over(ground, ground);
			LogAssert.IsTrue(thin.Profile(ground + 0.25f * mist.Depth) < 1f && thin.Profile(ground + 0.25f * mist.Depth) > thin.Profile(ground + mist.Depth),
				"a ground mist thins from the ground up");
			// A deep, mixed fog is full to near its top and falls away over its top sixth.
			FogLayerView deep = Drawn(200f, 0.5f, 0f, 0.8f);
			Assert.That(deep.TopSoftness, Is.EqualTo(FogLayerView.MixedTopShare * 200f).Within(1e-3f));
			FogLayerView.Column full = deep.Over(ground, ground);
			Assert.That(full.Profile(ground + 0.9f * deep.Depth), Is.EqualTo(1f).Within(1e-6f), "full to nine tenths of its depth");
			LogAssert.IsTrue(full.Profile(ground + deep.Depth * (1f + FogLayerView.MixedTopShare) + 0.01f) == 0f, "and gone a fade's half over its top");
			// The wind rolls its top: a softer mean fade, a third of the depth wide.
			float rolled = FogLayerView.TopShare(200f, 1f, 0f);
			LogAssert.IsTrue(rolled >= FogLayerView.RolledTopShare && rolled > FogLayerView.TopShare(200f, 0f, 0f), $"a sheared top is softer: {rolled:0.000} of the depth");
			// Lifted into stratus it is a mixed layer, whatever its depth.
			Assert.That(FogLayerView.TopShare(40f, 0f, 1f), Is.EqualTo(FogLayerView.MixedTopShare).Within(1e-6f), "a lifted sheet is mixed");
			// Through the night, calm, the top sharpens as the fog deepens past optical thickness — never softer.
			float last = float.MaxValue;
			for (float depth = 5f; depth <= 500f; depth += 5f)
			{
				float share = FogLayerView.TopShare(depth, 0f, 0f);
				LogAssert.IsTrue(share <= last + 1e-6f && share >= FogLayerView.MixedTopShare - 1e-6f && share <= FogLayerView.StillTopShare + 1e-6f,
					$"{depth:0} m: {share:0.000} of the depth");
				last = share;
			}
			// And the view carries it: never a harder edge than a mixed fog's.
			foreach (FogLayerView view in Fogs())
			{
				LogAssert.IsTrue(view.TopSoftness >= FogLayerView.MixedTopShare * view.Depth - 1e-3f, $"{view.Depth:0} m: never a hard edge ({view.TopSoftness:0.0} m)");
			}
			FogLayerView breezy = Drawn(150f, 8f, 0f, 0.8f);
			Assert.That(breezy.TopSoftness, Is.EqualTo(150f * FogLayerView.TopShare(150f, 1f, 0f)).Within(1e-3f), "a fresh breeze works it fully");
		}

		[Test]
		public void ASofterTopHoldsTheSameFog()
		{
			// The fade is centred on the physics' top, so however soft it is the column holds exactly the
			// fog the physics made — the depth, at the fog's extinction — and the visibility straight up
			// through it is the physics' too. A softer top moves fog above the top, never adds or loses it.
			const float ground = 100f;
			foreach (float depth in new[] { 5f, 30f, 120f, 400f })
			{
				foreach (float share in new[] { 0.01f, FogLayerView.MixedTopShare, FogLayerView.RolledTopShare, 0.5f, 1f })
				{
					var view = new FogLayerView { Extinction = 0.02f, Depth = depth, TopSoftness = share * depth };
					FogLayerView.Column column = view.Over(ground, ground);
					Assert.That(column.Above(ground), Is.EqualTo(depth).Within(1e-3f * depth), $"{depth:0} m at {share:0.00}: the column holds the depth");
					double sum = 0.0;
					const int N = 20000;
					float top = column.Top + column.Soft;
					float h = (top - ground) / N;
					for (int i = 0; i < N; i++)
					{
						sum += column.Profile(ground + (i + 0.5f) * h) * h;
					}
					Assert.That((float)sum, Is.EqualTo(depth).Within(2e-3f * depth), $"{depth:0} m at {share:0.00}: and so does its profile, summed");
				}
			}
		}

		// ── Drawn once ────────────────────────────────────────────────

		[Test]
		public void TheFogsExtinctionIsNeverCountedTwice()
		{
			WeatherFrame frame = FogFrame(0.5f);
			frame[WeatherChannel.Precipitation] = 0.3f;
			frame[WeatherChannel.RainWeight] = 1f;
			float falling = WeatherFogPresenter.FallingExtinction(frame);
			float total = WeatherFogPresenter.Extinction(frame);
			Assume.That(FogLayerView.Of(frame).Visible, "a fog to draw");
			LogAssert.IsTrue(total > falling && falling > 0f, "fog and rain both take light");

			// Drawn as a layer — by the march, or by the fallback where it does not run — the distance
			// fog keeps only what is falling, in its extinction and in the share it blends the region's
			// fog and the sky's horizon by.
			Assert.That(WeatherFogPresenter.UniformExtinction(frame, true), Is.EqualTo(falling).Within(1e-9f));
			Assert.That(WeatherFogPresenter.UniformAmount(frame, true), Is.EqualTo(1f - Mathf.Exp(-falling * 250f)).Within(1e-6f));
			LogAssert.IsTrue(WeatherFogPresenter.UniformAmount(frame, true) < WeatherFogPresenter.Amount(frame), "the fog's drops are not in the distance fog's share");

			// A fog with nothing falling leaves the pipeline's fog off altogether: switched on with
			// nothing to draw, it melted the sky's horizon into its colour all the same.
			WeatherFrame dry = FogFrame(0.5f);
			LogAssert.AreEqual(0f, WeatherFogPresenter.UniformAmount(dry, true), "nothing falling, no distance fog");
			var noRegion = new FogState { Enabled = false };
			FogState composed = FogComposer.Compose(noRegion, WeatherFogPresenter.UniformAmount(dry, true), Color.gray,
				WeatherFogPresenter.UniformExtinction(dry, true), 1e6f);
			LogAssert.IsFalse(composed.Enabled, "and the pipeline's fog stays off");

			// Nothing drawing the layer: the drops go back into the distance fog, never lost.
			Assert.That(WeatherFogPresenter.UniformExtinction(frame, false), Is.EqualTo(total).Within(1e-9f));
			Assert.That(WeatherFogPresenter.UniformAmount(frame, false), Is.EqualTo(WeatherFogPresenter.Amount(frame)).Within(1e-6f));
		}

		// ── How it moves and how it lies ──────────────────────────────

		[Test]
		public void TheFogDriftsOnTheWindAtItsOwnHeight()
		{
			LogAssert.AreEqual(0f, FogLayerView.DriftWind(0f, 80f, 0f), "no wind, no drift");
			LogAssert.AreEqual(0f, FogLayerView.DriftWind(4f, 0f, 0f), "no fog, nothing to drift");
			// The weather's wind is the ten-metre wind: a fog whose middle is at ten metres drifts on it.
			Assert.That(FogLayerView.DriftWind(4f, 20f, 0f), Is.EqualTo(4f).Within(0.05f), "at ten metres, the ten-metre wind");
			float shallow = FogLayerView.DriftWind(4f, 5f, 0f);
			float deep = FogLayerView.DriftWind(4f, 200f, 0f);
			float lifted = FogLayerView.DriftWind(4f, 200f, 1f);
			LogAssert.IsTrue(shallow < 4f && shallow > 2f, $"a shallow mist creeps, slowed by the ground: {shallow:0.0} m/s");
			LogAssert.IsTrue(deep > 4f && deep < 8f, $"a deep fog drifts faster, on the wind higher up: {deep:0.0} m/s");
			LogAssert.IsTrue(lifted > deep, $"a sheet lifted off the ground faster still: {lifted:0.0} m/s");
			// And the view carries it: the drift is the fog's own wind, not the ten-metre wind.
			WeatherFrame frame = FogFrame(0.5f, windAtTen: 1.5f);
			FogLayerView view = FogLayerView.Of(frame);
			Assert.That(view.WindSpeed, Is.EqualTo(FogLayerView.DriftWind(frame[WeatherChannel.WindSpeed] * 30f, view.Depth, view.Lift)).Within(1e-5f));
		}

		[Test]
		public void ItsStructureMovesTheFogAboutWithoutChangingHowMuchThereIs()
		{
			// The body's structure is log-normal about the physics' extinction: over a structure of unit
			// spread its mean is one, however patchy, so the visibility the physics set is the visibility
			// on average — a young patchy mist is no clearer or thicker overall than an even one.
			for (float spread = 0.2f; spread <= 1.5f; spread += 0.1f)
			{
				double sum = 0.0, weight = 0.0;
				const int N = 4000;
				for (int i = 0; i <= N; i++)
				{
					double s = -8.0 + 16.0 * i / N;
					double p = System.Math.Exp(-0.5 * s * s);
					sum += p * FogLayerView.StructureFactor((float)s, spread);
					weight += p;
				}
				Assert.That(sum / weight, Is.EqualTo(1.0).Within(2e-3), $"a spread of {spread:0.0} keeps the mean");
			}
			// And it does move it: denser in a bank, thinner in a gap, more so the patchier the fog.
			LogAssert.IsTrue(FogLayerView.StructureFactor(1f, 0.9f) > FogLayerView.StructureFactor(1f, 0.35f) && FogLayerView.StructureFactor(1f, 0.35f) > 1f,
				"a bank is denser than the mean, the more so in a patchy fog");
			LogAssert.IsTrue(FogLayerView.StructureFactor(-1f, 0.9f) < 1f, "a gap thinner");
		}

		// ── Lit by the light the ground is lit by ─────────────────────────

		[Test]
		public void TheFogIsLitThroughTheCloudOverIt_AsTheGroundIs()
		{
			/* The light carrying the cloud shadow is the open light; each surface takes the cloud's share
			 * at its own place off it (the cookie). The fog was lit by the view's average instead —
			 * under a storm's core several times what the ground there got. Published as the average for
			 * what cannot read the shadow, with the factor that turns it into the surfaces' light where
			 * the shadow is read: average × (1/average) × cookie = the light × cookie. */
			var open = new Color(2.4f, 2.3f, 2.1f);
			foreach (float average in new[] { 0.05f, 0.3f, 0.7f, 1f })
			{
				Color published = open * average;
				float share = FogLayerView.CookieShare(average, true);
				foreach (float cookie in new[] { 0.02f, 0.3f, 1f })
				{
					float fog = published.g * share * cookie, surface = open.g * cookie;
					LogAssert.IsTrue(Mathf.Abs(fog - surface) < 1e-4f * surface + 1e-6f, $"average {average}, cookie {cookie}: the fog's light {fog:0.000} is the ground's {surface:0.000}");
				}
			}
			LogAssert.IsTrue(FogLayerView.CookieShare(0.4f, false) == 0f, "no cloud shadow: nothing to read, the published light is the light");
			LogAssert.IsTrue(FogLayerView.CookieShare(0f, true) <= 1000f + 1e-3f, "an average of nothing does not blow up");

			/* The mapping is URP's own: the offset lands on the cookie's middle, and a half-size either
			 * side on its edges, in the light's plane. */
			Matrix4x4 worldToLight = Matrix4x4.TRS(new Vector3(30f, 400f, -20f), Quaternion.Euler(50f, 30f, 0f), Vector3.one).inverse;
			Vector2 size = new Vector2(4000f, 4000f), offset = new Vector2(120f, -80f);
			FogLayerView.CookieRows(worldToLight, size, offset, out Vector4 u, out Vector4 v);
			Matrix4x4 lightToWorld = worldToLight.inverse;
			foreach (var (local, expected) in new[] { (new Vector3(offset.x, offset.y, 0f), new Vector2(0.5f, 0.5f)), (new Vector3(offset.x + 2000f, offset.y, 700f), new Vector2(1f, 0.5f)), (new Vector3(offset.x, offset.y - 2000f, -300f), new Vector2(0.5f, 0f)) })
			{
				Vector3 p = lightToWorld.MultiplyPoint3x4(local);
				var uv = new Vector2(Vector3.Dot(new Vector3(u.x, u.y, u.z), p) + u.w, Vector3.Dot(new Vector3(v.x, v.y, v.z), p) + v.w);
				LogAssert.IsTrue((uv - expected).magnitude < 1e-3f, $"light-plane {local} reads the cookie at {uv}, as URP does ({expected}); along the light it does not move");
			}

			string layer = SourceScanPins.ReadCode("Assets/Prefabs/Client/Weather/Shaders/FishFogLayer.hlsl");
			LogAssert.IsTrue(layer.Contains("float3 sun = _FishFogLightColor.rgb * sunShare * risen"), "FishFogLight lights beam and diffuse by the light through the cloud over the point");
			LogAssert.IsTrue(layer.Contains("* _FishFogLightColor.w;"), "and FishFogSunShare turns the average back into the surfaces' light");
			string march = SourceScanPins.ReadCode("Assets/Prefabs/Client/Weather/Shaders/FishCloudVolume.hlsl");
			LogAssert.IsTrue(march.Contains("FishFogLight(altitudeHere, direction, fogColumn, _FishFogLayer.x, 1.0, FishFogSunShare(position))"), "the march lights each fog sample by the cloud over it");
			string height = SourceScanPins.ReadCode("Assets/Prefabs/Client/Weather/Shaders/FishHeightFog.shader");
			LogAssert.IsTrue(height.Contains("FishFogSunShare(litAt)"), "and so does the analytic fallback");
			string view = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Weather/Presentation/FogLayerView.cs");
			LogAssert.IsTrue(view.Contains("LightColor = new Vector4(bright.r, bright.g, bright.b, share),"), "the published light carries the factor");
		}
	}
}
