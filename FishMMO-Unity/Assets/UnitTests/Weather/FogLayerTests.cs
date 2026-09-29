using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The fog as a layer on the ground with a top: how deep the night makes it, what the wind and the
	/// morning do to it, and how the renderer integrates it.
	/// </summary>
	/// <remarks>
	/// Pinned to what fogs do, not to the numbers the code happened to give: a mist is a few metres to a
	/// few tens deep and a dense fog a hundred or several; an overcast night makes little radiation fog;
	/// a breeze over damp but unsaturated air shares the night's chill out and keeps it clear, while over
	/// nearly saturated air it lifts the fog into stratus; the morning lifts it and the day never spreads
	/// its drops through the mixed layer (a humid afternoon's haze is the air's own); no fog stands past
	/// the cloud base; and a thin fog's first scattering is not a white card.
	/// </remarks>
	[TestFixture]
	public class FogLayerTests
	{
		[Test]
		public void ALyingFogFillsDownToWhateverGroundThereIs()
		{
			// The terrain map is a hundred metres to the texel and blurred: in a valley narrower than
			// that — a gorge under a viaduct — its ground stands well above the real floor. A fog lying
			// on the ground has no base; it fills down to the floor the ray actually meets — as far as the
			// sea, whose surface it lies on (FogCloudTests.AFogLiesOnTheSeaAndStopsAtItsSurface).
			var lying = new FogLayerView { Extinction = 0.01f, Depth = 100f, Lift = 0f, TopSoftness = 5f };
			FogLayerView.Column onGround = lying.Over(250f, 260f);
			LogAssert.IsTrue(onGround.Base - onGround.BaseSoft <= 0f, $"no base under a lying fog above the sea, got {onGround.Base:0} m over ground at 250 m");
			Assert.That(onGround.Profile(120f), Is.EqualTo(1f).Within(1e-6f), "a gorge's floor far under the map's ground is in it");
			LogAssert.IsTrue(onGround.Top >= 260f + 100f - 1e-3f, "and it still lies as deep as it is over the pooled air");

			var lifted = lying;
			lifted.Lift = 1f;
			FogLayerView.Column sheet = lifted.Over(250f, 260f);
			LogAssert.IsTrue(sheet.Base > 250f, $"a lifted fog is a sheet with its base above the ground, got {sheet.Base:0} m");
		}

		private static PlanetAir Earth => PlanetAir.Earthlike;

		/// <summary>Calm air just after dawn, as damp as a fog morning's.</summary>
		private static WeatherDriver.Synoptic Dawn(float humidity = 0.62f, float wind = 0.5f, float time = 0.27f)
		{
			return new WeatherDriver.Synoptic
			{
				Humidity = humidity,
				Pressure = 0.6f,
				Instability = 0.15f,
				Wind = new Vector2(wind, 0f),
				LocalTime01 = time,
			};
		}

		private static FogLayer Layer(in WeatherDriver.Synoptic air, float fog, float cloud = 0.2f, float kelvin = 285f)
		{
			AirColumn column = AirColumn.Of(Earth, kelvin, air.Humidity, air.Pressure, air.Instability);
			return FogLayer.Of(air, column, Earth, fog, cloud);
		}

		// ── The physics ───────────────────────────────────────────────

		[Test]
		public void AMistLiesShallowAndADenseFogDeep()
		{
			WeatherDriver.Synoptic air = Dawn();
			float mist = Layer(air, 0.16f).Depth;
			float fog = Layer(air, 0.5f).Depth;
			float dense = Layer(air, 1f).Depth;
			Assert.That(mist, Is.InRange(4f, 60f), "a mist a few metres to a few tens deep, the sky open over it");
			Assert.That(dense, Is.InRange(60f, 450f), "a dense fog a hundred metres or several");
			LogAssert.IsTrue(mist < fog && fog < dense, $"deeper as it thickens: {mist:0} < {fog:0} < {dense:0} m");
		}

		[Test]
		public void AnOvercastNightMakesLittleRadiationFog()
		{
			WeatherDriver.Synoptic air = Dawn();
			float clear = Layer(air, 0.6f, cloud: 0f).Depth;
			float overcast = Layer(air, 0.6f, cloud: 1f).Depth;
			LogAssert.IsTrue(overcast < clear * 0.5f, $"a cloudy sky sends the ground's heat back: {overcast:0} m under cloud, {clear:0} m clear");
			LogAssert.IsTrue(FogLayer.NightLoss(285f, 282f, Condensate.Water, 0f) > 50f, "a clear damp night loses tens of watts a square metre");
		}

		[Test]
		public void DamperAirChillsDeeper()
		{
			// The same fog in air with a smaller dew-point spread: the night need chill it less far, so
			// it chills more of it. Both well under their cloud bases.
			float drier = Layer(Dawn(humidity: 0.5f), 0.6f).Depth;
			float damper = Layer(Dawn(humidity: 0.62f), 0.6f).Depth;
			LogAssert.IsTrue(damper > drier, $"damp air fogs deeper: {damper:0} m against {drier:0} m");
		}

		[Test]
		public void ABreezeOverDampAirSharesTheChillOutAndKeepsTheNightClear()
		{
			// Damp but a good way from saturation (relative humidity two thirds, the base some 700 m up):
			// a calm night fogs a hundred metres deep; the same night in a breeze stirs its chill through
			// a few hundred metres of air, cools it all by a fraction of its spread, and none of it
			// reaches its dew point. That is why a radiation fog wants a calm.
			FogLayer calm = Layer(Dawn(wind: 0.5f), 0.6f);
			FogLayer breeze = Layer(Dawn(wind: 6f), 0.6f);
			LogAssert.AreEqual(0f, calm.Lift, "a calm fog lies on the ground");
			LogAssert.IsTrue(calm.Depth > 60f && calm.MixedOut == 0f, $"a calm night fogs: {calm.Depth:0} m, none taken");
			LogAssert.AreEqual(0f, breeze.Depth, "a breeze keeps the damp night clear");
			LogAssert.AreEqual(1f, breeze.MixedOut, "and takes the whole fog back");

			// And the frame says so: the driver asks for a fog, the physics answers none, and nothing is
			// left for the distance fog to spread over everything as a wash.
			WeatherDriver.Synoptic air = Dawn(wind: 8f);
			Assume.That(WeatherDriver.Background(air)[WeatherChannel.FogDensity], Is.GreaterThan(0.1f), "the driver asks for a dawn fog in damp air");
			AirColumn column = AirColumn.Of(Earth, 285f, air.Humidity, air.Pressure, air.Instability);
			WeatherFrame frame = WeatherPhysics.Frame(air, column, Earth, default, 0.1f, 1f, out _);
			LogAssert.AreEqual(0f, frame[WeatherChannel.FogDensity], "no fog in the frame");
			LogAssert.AreEqual(0f, frame[WeatherChannel.FogHeight], "and no layer");
		}

		[Test]
		public void ABreezeOverNearlySaturatedAirLiftsItIntoStratus()
		{
			// Nearly saturated air (the base under two hundred metres): the shared chill is still enough,
			// and saturates the stirred air from part of the way up — a grey ceiling over clear ground.
			FogLayer calm = Layer(Dawn(humidity: 0.9f, wind: 0.5f), 0.6f);
			FogLayer breeze = Layer(Dawn(humidity: 0.9f, wind: 8f), 0.6f);
			FogLayer fresh = Layer(Dawn(humidity: 0.9f, wind: 10f), 0.6f);
			LogAssert.AreEqual(0f, calm.Lift, "calm, it lies on the ground");
			LogAssert.IsTrue(breeze.Depth >= 0.99f * calm.Depth, $"stirred to its top: {breeze.Depth:0} m against {calm.Depth:0} m");
			LogAssert.IsTrue(breeze.Lift > 0.5f, $"and lifted into stratus: {breeze.Lift:0.00}");
			LogAssert.IsTrue(fresh.Lift > breeze.Lift, $"higher in a fresher wind: {fresh.Lift:0.00} against {breeze.Lift:0.00}");

			// The stirring depth itself: none without wind, tens of metres in a light breeze, more in a fresh one.
			LogAssert.AreEqual(0f, FogLayer.MixedDepth(0f, 4f, 0.003f, 285f, 9.81f));
			float light = FogLayer.MixedDepth(3f, 4f, 0.003f, 285f, 9.81f);
			float strong = FogLayer.MixedDepth(8f, 4f, 0.003f, 285f, 9.81f);
			Assert.That(light, Is.InRange(10f, 200f), "a light breeze stirs tens of metres");
			LogAssert.IsTrue(strong > light, $"a fresh one more: {strong:0} m against {light:0} m");
		}

		[Test]
		public void TheStirredSheetIsTheChillsAndNoMore()
		{
			// Stirred just past the chilled depth, the fog still reaches the ground; stirred far past it,
			// over air far from saturation, it is gone; between, a lifted sheet.
			FogLayer.Stirred(100f, 100.01f, 700f, 700f, out float top, out float lift, out float mixedOut);
			Assert.That(top, Is.EqualTo(100f).Within(0.1f));
			Assert.That(lift, Is.EqualTo(0f).Within(0.01f), "just past the chilled depth it still lies on the ground");
			LogAssert.AreEqual(0f, mixedOut);
			FogLayer.Stirred(100f, 400f, 700f, 700f, out top, out lift, out mixedOut);
			LogAssert.AreEqual(0f, top, "a chill shared four times over saturates nothing under a base of 700 m");
			LogAssert.AreEqual(1f, mixedOut);
			FogLayer.Stirred(100f, 130f, 200f, 200f, out top, out lift, out mixedOut);
			// Saturated from 200·(1 − 100/130) ≈ 46 m to 130 m.
			Assert.That(top, Is.EqualTo(130f).Within(0.1f));
			Assert.That(lift * FogLayer.LiftedBase * top, Is.EqualTo(200f * (1f - 100f / 130f)).Within(0.5f), "its base where the stirred air's spread runs out");
			LogAssert.AreEqual(0f, mixedOut);
			// A sheet thinner than a lifted layer is drawn keeps the light it takes: thinned by the cube law (AirPhysics.FogExponent).
			FogLayer.Stirred(100f, 1000f, 200f, 200f, out top, out lift, out mixedOut);
			float sheet = 200f - 200f * (1f - 100f / 1000f);
			float drawn = top * (1f - FogLayer.LiftedBase);
			LogAssert.AreEqual(1f, lift);
			float kept = 1f - mixedOut;
			Assert.That(AirPhysics.FogExtinction(0.6f * kept) * drawn, Is.EqualTo(AirPhysics.FogExtinction(0.6f) * sheet).Within(0.02f * AirPhysics.FogExtinction(0.6f) * sheet),
				"the drawn layer takes the light the thin sheet would");
		}

		[Test]
		public void TheMorningLiftsItAndTheDayLeavesTheHazeToTheAir()
		{
			FogLayer dawn = Layer(Dawn(time: 0.27f), 0.6f);
			FogLayer morning = Layer(Dawn(time: 0.34f), 0.6f);
			FogLayer afternoon = Layer(Dawn(time: 0.55f), 0.2f);
			FogLayer night = Layer(Dawn(time: 0f), 0.2f);
			LogAssert.IsTrue(morning.Lift > 0.3f && morning.Lift > dawn.Lift, $"the warming ground lifts it: {morning.Lift:0.00} against {dawn.Lift:0.00} at dawn");
			LogAssert.AreEqual(0f, afternoon.Lift, "by the afternoon it is not a lifted sheet");
			// The day does not spread the fog's drops through the mixed layer: a humid afternoon is milky
			// with the air's own haze (AirPhysics.HazeDistance), which the sky draws, not with fog.
			AirColumn column = AirColumn.Of(Earth, 285f, 0.62f, 0.6f, 0.15f);
			float mixedLayer = Mathf.Min(column.Base, Mathf.Min(column.Cap, column.Tropopause));
			Assert.That(afternoon.Depth, Is.EqualTo(night.Depth).Within(0.01f), "the same fog lies as deep by day as by night");
			LogAssert.IsTrue(afternoon.Depth < 0.25f * mixedLayer, $"not the mixed layer's: {afternoon.Depth:0} m of {mixedLayer:0} m");
		}

		[Test]
		public void NoFogStandsPastTheCloudBase()
		{
			for (float humidity = 0.45f; humidity <= 1.001f; humidity += 0.05f)
			{
				for (float wind = 0f; wind <= 12f; wind += 3f)
				{
					WeatherDriver.Synoptic air = Dawn(humidity, wind);
					AirColumn column = AirColumn.Of(Earth, 285f, air.Humidity, air.Pressure, air.Instability);
					FogLayer layer = FogLayer.Of(air, column, Earth, 1f, 0f);
					float ceiling = Mathf.Max(FogLayer.MinimumDepth, Mathf.Min(column.Base, Mathf.Min(column.Cap, column.Tropopause)));
					// Either none — the wind has shared the chill out — or a layer under the base.
					bool none = layer.Depth == 0f && layer.MixedOut == 1f;
					LogAssert.IsTrue(none || (layer.Depth <= ceiling + 0.01f && layer.Depth >= FogLayer.MinimumDepth),
						$"humidity {humidity:0.00}, wind {wind:0}: {layer.Depth:0} m under a base of {column.Base:0} m");
				}
			}
		}

		[Test]
		public void TheFrameCarriesTheLayer()
		{
			WeatherDriver.Synoptic air = Dawn(humidity: 0.75f);
			AirColumn column = AirColumn.Of(Earth, 285f, air.Humidity, air.Pressure, air.Instability);
			WeatherFrame frame = WeatherPhysics.Frame(air, column, Earth, default, 0.1f, 1f, out _);
			Assume.That(frame[WeatherChannel.FogDensity], Is.GreaterThan(0.1f), "damp still air at dawn fogs");
			FogLayer carried = FogLayer.In(frame);
			FogLayer worked = FogLayer.Of(air, column, Earth, frame[WeatherChannel.FogDensity], frame[WeatherChannel.CloudCover]);
			Assert.That(carried.Depth, Is.EqualTo(worked.Depth).Within(FogLayer.ChannelMetres * 1e-5f));
			Assert.That(carried.Lift, Is.EqualTo(worked.Lift).Within(1e-5f));
			LogAssert.IsTrue(carried.Depth > FogLayer.MinimumDepth, $"a real depth: {carried.Depth:0} m");
		}

		// ── The drawing ───────────────────────────────────────────────

		[Test]
		public void TheLayersPathIsTheIntegralOfItsProfile()
		{
			// A lifted sheet with a sharp top, and a still ground mist whose fade is its whole depth.
			var views = new[]
			{
				new FogLayerView { Extinction = 0.01f, Depth = 80f, Lift = 0.5f, TopSoftness = 6f },
				new FogLayerView { Extinction = 0.01f, Depth = 30f, Lift = 0f, TopSoftness = 30f },
			};
			float[] slopes = { 0f, 1e-4f, 0.01f, 0.05f, 0.3f, -0.2f, 1f };
			float[] starts = { 1.7f, 30f, 60f, 120f };
			foreach (FogLayerView view in views)
			{
				FogLayerView.Column column = view.Over(10f, 12f);
				LogAssert.IsTrue(column.Base + column.BaseSoft <= column.Top - column.Soft + 1e-3f, "the base's fade and the top's never overlap");
				foreach (float y0 in starts)
				{
					foreach (float dy in slopes)
					{
						const float t0 = 5f, t1 = 900f;
						float analytic = column.Path(y0, dy, t0, t1);
						// The same by brute force.
						const int steps = 20000;
						double sum = 0.0;
						float dt = (t1 - t0) / steps;
						for (int i = 0; i < steps; i++)
						{
							sum += column.Profile(y0 + dy * (t0 + (i + 0.5f) * dt)) * dt;
						}
						Assert.That(analytic, Is.EqualTo((float)sum).Within(Mathf.Max(0.5f, (float)sum * 0.01f)),
							$"{view.Depth:0} m, soft {view.TopSoftness:0}: from {y0} m at a slope of {dy}");
					}
				}
			}
		}

		[Test]
		public void TheLayerFillsTheValleyAndLeavesTheRidge()
		{
			var view = new FogLayerView { Extinction = 0.01f, Depth = 40f, Lift = 0f, TopSoftness = 2f };
			// Pooled air lies over a valley floor below it and a ridge above it alike (all above the sea,
			// under which there is no fog).
			FogLayerView.Column valley = view.Over(70f, 100f);
			FogLayerView.Column ridge = view.Over(170f, 100f);
			LogAssert.IsTrue(valley.Top - 70f > 60f, $"the valley fills to the pool's level: {valley.Top - 70f:0} m deep");
			LogAssert.IsTrue(ridge.Top - 170f < 10f, $"the ridge stands out of it under a skin: {ridge.Top - 170f:0} m");
			LogAssert.AreEqual(1f, valley.Profile(71f), "an unlifted fog reaches the valley floor");
		}

		[Test]
		public void AThinFogsFirstScatteringIsNotAWhiteCard()
		{
			// A white sun straight overhead and no sky light: what the fog gives back is the sun's alone.
			var lighting = new FogLayerView.Lighting
			{
				ToLight = new Vector4(0f, 1f, 0f, FogLayerView.PhaseAsymmetry),
				LightColor = new Vector4(1f, 1f, 1f, 1f),
				Ambient = new Vector4(0f, 0f, 0f, 1f),
			};
			// At a thin fog's top the beam is whole and nothing has been turned yet: one scattering,
			// averaged over every direction — a quarter of the light's colour (the phase per steradian
			// times π), not the whole of it as a white card would show.
			var thin = new FogLayerView { Extinction = 1e-4f, Depth = 100f, TopSoftness = 2f };
			FogLayerView.Column column = thin.Over(0f, 0f);
			Color top = thin.LightAt(column.Top + column.Soft + 1f, column, lighting);
			Assert.That(top.r, Is.EqualTo(FogLayerView.AveragePhase).Within(1e-4f), "a quarter of the light, not all of it");
			// Deep in a thick fog the beam is spent and the light arrives diffuse: the two-stream share
			// that still gets through, which is what a white card shows.
			var thick = new FogLayerView { Extinction = 0.05f, Depth = 200f, TopSoftness = 2f };
			FogLayerView.Column deep = thick.Over(0f, 0f);
			Color inside = thick.LightAt(1f, deep, lighting);
			float carry = 0.75f * (1f - FogLayerView.PhaseAsymmetry);
			float slant = thick.Extinction * deep.Above(1f);
			Assert.That(inside.r, Is.EqualTo(1f / (1f + carry * slant)).Within(1e-3f), "diffuse, as the two-stream answer says");
			LogAssert.IsTrue(inside.r > top.r, $"turned round many times, a thick fog is brighter than a thin one's first scattering: {inside.r:0.00} against {top.r:0.00}");
		}

		[Test]
		public void TheDistanceFogLeavesTheLayerToThePasses()
		{
			var frame = new WeatherFrame();
			frame[WeatherChannel.FogDensity] = 0.5f;
			frame[WeatherChannel.Precipitation] = 0.3f;
			frame[WeatherChannel.RainWeight] = 1f;
			new FogLayer { Depth = 80f }.WriteTo(ref frame);

			float falling = WeatherFogPresenter.FallingExtinction(frame);
			float total = WeatherFogPresenter.Extinction(frame);
			LogAssert.IsTrue(falling > 0f && total > falling, "rain and fog both take light");
			// Drawn as a layer: the distance fog keeps only what falls through the whole air.
			Assert.That(WeatherFogPresenter.UniformExtinction(frame, true), Is.EqualTo(falling).Within(1e-7f));
			// Nothing drawing the layer: the drops go back into the distance fog, never lost.
			Assert.That(WeatherFogPresenter.UniformExtinction(frame, false), Is.EqualTo(total).Within(1e-7f));
			// A frame with no layer in it (no depth) cannot be drawn as one either.
			frame[WeatherChannel.FogHeight] = 0f;
			Assert.That(WeatherFogPresenter.UniformExtinction(frame, true), Is.EqualTo(total).Within(1e-7f));
		}

		[Test]
		public void AMistIsPatchierThanADenseFogAndWindSoftensItsTop()
		{
			var mist = new WeatherFrame();
			mist[WeatherChannel.FogDensity] = 0.16f;
			new FogLayer { Depth = 25f }.WriteTo(ref mist);
			var dense = mist;
			dense[WeatherChannel.FogDensity] = 1f;
			LogAssert.IsTrue(FogLayerView.Of(mist).Patchiness > FogLayerView.Of(dense).Patchiness, "a young mist lies in patches; a dense fog has mixed");

			var windy = dense;
			windy[WeatherChannel.WindSpeed] = 8f / 30f;
			new FogLayer { Depth = 150f }.WriteTo(ref windy);
			var still = windy;
			still[WeatherChannel.WindSpeed] = 0f;
			// Kelvin–Helmholtz rolls a third of the depth deep, averaged into the mean fade, against the
			// upper sixth of a still, mixed fog.
			LogAssert.IsTrue(FogLayerView.Of(windy).TopSoftness > 2f * FogLayerView.Of(still).TopSoftness, "wind rolls the top into a softer fade");
			LogAssert.AreEqual(0f, FogLayerView.Of(windy).Heave, "and never heaves it: the top is a fade, not a surface");
			// The layer's extinction is the fog's own, from its water.
			Assert.That(FogLayerView.Of(dense).Extinction, Is.EqualTo(AirPhysics.FogExtinction(1f)).Within(1e-7f));
		}
	}
}
