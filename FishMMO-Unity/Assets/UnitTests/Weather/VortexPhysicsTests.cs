using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// A tornado's funnel and spin, from its Rankine vortex (VortexPhysics). Shapes and ratios, not the
	/// numbers one implementation happens to give: the funnel flares into the base, narrows to a tip, hangs
	/// V²/g below it, is continuous through the core's edge, is densest in its core, and turns the way its
	/// hemisphere turns it; and through a storm's life a tornado lowers, touches down, splits when it is
	/// wide under a low base, and ropes out thin and leaning. Its striations wind at the wall's own
	/// pitch, lie a handful across it from rope to wedge, keep their broad octaves at 3 km while the fine
	/// ones fade first, and are as deep as the inflow's rolls swing its condensation level — but only
	/// where the spin holds the streamlines apart, so they never fan the flare out into spokes; and a
	/// ledge's faces are lit by the sky they turn to.
	/// </summary>
	[TestFixture]
	public class VortexPhysicsTests
	{
		private const float Core = 80f, Wind = 75f, Gravity = 9.81f;

		[Test]
		public void TheFunnelFlaresIntoTheBaseAndNarrowsToATip()
		{
			float depth = VortexPhysics.FunnelDepth(Wind, Gravity);
			LogAssert.IsTrue(float.IsPositiveInfinity(VortexPhysics.CondensationRadius(0f, Core, Wind, Gravity)),
				"at the cloud base itself the funnel is the cloud: unbounded");
			float previous = float.MaxValue;
			for (float share = 0.02f; share < 1f; share += 0.02f)
			{
				float radius = VortexPhysics.CondensationRadius(share * depth, Core, Wind, Gravity);
				LogAssert.IsTrue(radius < previous, $"the funnel must narrow all the way down; widened at {share:0.00} of its depth");
				previous = radius;
			}
			LogAssert.IsTrue(VortexPhysics.CondensationRadius(depth * 1.01f, Core, Wind, Gravity) == 0f,
				"below its tip there is no funnel");
		}

		[Test]
		public void TheFunnelHangsVSquaredOverGBelowTheBase()
		{
			float earth = VortexPhysics.FunnelDepth(Wind, Gravity);
			LogAssert.IsTrue(System.Math.Abs(earth - Wind * Wind / Gravity) < 0.01f, $"V²/g, got {earth:0.0} m");
			// A world of lower gravity hangs a longer funnel from the same vortex, in the ratio of the pulls.
			float arthis = VortexPhysics.FunnelDepth(Wind, 5.5f);
			LogAssert.IsTrue(System.Math.Abs(arthis / earth - Gravity / 5.5f) < 1e-3f, $"depth should scale as 1/g, got {arthis / earth:0.000}");
			// And a stronger vortex reaches further down: as the square of its wind.
			float stronger = VortexPhysics.FunnelDepth(Wind * 1.2f, Gravity);
			LogAssert.IsTrue(System.Math.Abs(stronger / earth - 1.44f) < 1e-3f, $"depth should scale as V², got {stronger / earth:0.000}");
		}

		[Test]
		public void TheFunnelIsContinuousThroughTheCoresEdge()
		{
			// Half the funnel's depth is where the two halves of the solution meet, at the core's radius.
			float depth = VortexPhysics.FunnelDepth(Wind, Gravity);
			float above = VortexPhysics.CondensationRadius(depth * 0.4999f, Core, Wind, Gravity);
			float below = VortexPhysics.CondensationRadius(depth * 0.5001f, Core, Wind, Gravity);
			LogAssert.IsTrue(System.Math.Abs(above - Core) < 0.1f && System.Math.Abs(below - Core) < 0.1f,
				$"both halves should meet at the core's {Core} m: {above:0.00} and {below:0.00}");
		}

		[Test]
		public void TheWindFollowsTheAirsEnergy()
		{
			// √(2·CAPE): a sultry 2800 J/kg allows about 75 m/s.
			LogAssert.IsTrue(System.Math.Abs(VortexPhysics.PeakWind(2800f, 1f) - 74.83f) < 0.05f, "√(2·2800) is 74.8 m/s");
			LogAssert.IsTrue(VortexPhysics.PeakWind(1e6f, 1f) == VortexPhysics.StrongestTornado, "held to the strongest tornado drawn");
			LogAssert.IsTrue(System.Math.Abs(VortexPhysics.PeakWind(2800f, 0.5f) - 0.5f * VortexPhysics.PeakWind(2800f, 1f)) < 1e-4f,
				"a half-grown storm turns at half the speed");
		}

		[Test]
		public void TheCoreTurnsAsOneAndTheOutsideLags()
		{
			float inner = VortexPhysics.AngularSpeed(Core * 0.3f, Core, Wind);
			float middle = VortexPhysics.AngularSpeed(Core * 0.9f, Core, Wind);
			LogAssert.IsTrue(System.Math.Abs(inner - middle) < 1e-4f, "the core turns as a solid body");
			float twice = VortexPhysics.AngularSpeed(Core * 2f, Core, Wind);
			LogAssert.IsTrue(System.Math.Abs(twice * 4f - VortexPhysics.AngularSpeed(Core, Core, Wind)) < 1e-3f,
				"outside, the angular speed falls as the radius squared");
			float peak = VortexPhysics.WindAt(Core, Core, Wind);
			LogAssert.IsTrue(peak >= VortexPhysics.WindAt(Core * 0.7f, Core, Wind) && peak >= VortexPhysics.WindAt(Core * 1.5f, Core, Wind),
				"the wind peaks at the core's edge");
		}

		[Test]
		public void TornadoesTurnWithTheirHemisphere()
		{
			LogAssert.IsTrue(VortexPhysics.Spin(35f, 7u, true) > 0f, "northern tornadoes turn anticlockwise seen from above");
			LogAssert.IsTrue(VortexPhysics.Spin(-35f, 7u, true) < 0f, "southern ones clockwise");
			bool either = VortexPhysics.Spin(35f, 2u, false) != VortexPhysics.Spin(35f, 3u, false);
			LogAssert.IsTrue(either, "dust devils go either way");
		}
			/// <summary>A day's CAPE whose speed limit is this wind: √(2·CAPE) = V.</summary>
		private static float CapeFor(float wind) => 0.5f * wind * wind;

		/// <summary>A supercell's rotating updraught in air of that CAPE: 0.3·√(2·CAPE), spun up half again.</summary>
		private static float UpdraftFor(float wind) => 0.3f * wind * VortexPhysics.MesocycloneUpdraftGain;

		private static VortexPhysics.Form Grown(float wind, float core, float wallCloud, float gravity, float lifting = VortexPhysics.StripsBoundGround)
		{
			return VortexPhysics.Tornado(CapeFor(wind), 1f, 1f, false, core, wallCloud, 2000f, new Vector2(10f, 0f), UpdraftFor(wind), gravity, lifting);
		}

		[Test]
		public void TheFunnelsEdgeIsWhereItsLowPressureMakesUpTheLift()
		{
			float depth = VortexPhysics.FunnelDepth(Wind, Gravity);
			for (float share = 0.05f; share < 1f; share += 0.05f)
			{
				float below = share * depth;
				float edge = VortexPhysics.CondensationRadius(below, Core, Wind, Gravity);
				float lift = VortexPhysics.DeficitLift(edge, Core, Wind, Gravity);
				LogAssert.IsTrue(System.Math.Abs(lift - below) < 1e-3f * depth,
					$"at {share:0.00} of the depth the deficit at the funnel's edge is worth {lift:0.0} m of lift, not the {below:0.0} m still needed");
			}
			LogAssert.IsTrue(System.Math.Abs(VortexPhysics.DeficitLift(0f, Core, Wind, Gravity) - depth) < 1e-3f, "the whole of V²/g at the axis");
		}

		[Test]
		public void TheFunnelIsDensestInItsCoreAndSoftAtItsEdge()
		{
			float depth = VortexPhysics.FunnelDepth(Wind, Gravity);
			float below = 0.3f * depth;
			float edge = VortexPhysics.CondensationRadius(below, Core, Wind, Gravity);
			float previous = float.MaxValue;
			for (float r = 0f; r < edge; r += edge / 20f)
			{
				float excess = VortexPhysics.CondensationExcess(r, below, Core, Wind, Gravity);
				LogAssert.IsTrue(excess > 0f && excess <= previous, $"inside the funnel it holds less water further out: {excess:0.0} m of lift at {r:0} m");
				previous = excess;
			}
			LogAssert.IsTrue(System.Math.Abs(VortexPhysics.CondensationExcess(edge, below, Core, Wind, Gravity)) < 1e-3f * depth, "nothing at its edge");
			LogAssert.IsTrue(VortexPhysics.CondensationExcess(edge * 1.2f, below, Core, Wind, Gravity) < 0f, "and none outside it");
		}

		[Test]
		public void AFunnelHoldsItsWaterUndiluted()
		{
			PlanetAir planet = PlanetAir.Earthlike;
			AirColumn column = AirColumn.Of(planet, 300f, 0.7f, -0.2f, 0.5f);
			float cloud = column.ExtinctionCoefficient(planet);
			float funnel = VortexPhysics.FunnelExtinction(column, planet);
			// A heaped cloud keeps about a third of its water; a vortex's core, which its spin keeps from mixing, all of it.
			LogAssert.IsTrue(cloud > 0f && System.Math.Abs(funnel / cloud - Mathf.Pow(1f / 0.35f, 2f / 3f)) < 0.01f,
				$"(1/0.35)^⅔ of the cloud's: {funnel / cloud:0.000}");
		}

		[Test]
		public void DebrisIsThrownHalfAsHighAsTheFunnelHangs()
		{
			LogAssert.IsTrue(System.Math.Abs(VortexPhysics.DebrisHeight(Wind, Gravity) - 0.5f * VortexPhysics.FunnelDepth(Wind, Gravity)) < 1e-3f, "V²/2g");
			LogAssert.IsTrue(VortexPhysics.DebrisHeight(Wind, 5.5f) > VortexPhysics.DebrisHeight(Wind, Gravity), "higher where the world pulls less");
		}

		[Test]
		public void AWeakTornadosFunnelStaysAloftOverItsDebrisAndAViolentOneReachesTheGround()
		{
			VortexPhysics.Form weak = Grown(35f, 60f, 700f, Gravity);
			LogAssert.IsTrue(weak.Aloft, $"a 35 m/s funnel hangs {weak.FunnelDepth:0} m under a 700 m wall cloud");
			float gap = weak.Top - weak.FunnelDepth - weak.DebrisHeight;
			LogAssert.IsTrue(gap > 100f, $"with clear air between its tip and its debris whirl: {gap:0} m");
			LogAssert.IsTrue(weak.Lifted > 0f, "which it still raises off the ground");

			VortexPhysics.Form violent = Grown(90f, 250f, 700f, Gravity);
			LogAssert.IsFalse(violent.Aloft, $"a 90 m/s funnel reaches {violent.FunnelDepth:0} m down, past the ground");
			LogAssert.IsTrue(violent.DebrisHeight > 0.5f * violent.Top, $"inside debris thrown {violent.DebrisHeight:0} m up");
			// A typical strong tornado is a few hundred metres across near the ground.
			Assert.That(violent.FunnelWidthAt(0f, Gravity), Is.InRange(150f, 600f), "a few hundred metres across at the ground");
			LogAssert.IsTrue(violent.FunnelWidthAt(0.9f * violent.Top, Gravity) > 2f * violent.FunnelWidthAt(0f, Gravity), "and flaring into the wall cloud");

			// The same storm on a world that pulls less hangs its funnel further down.
			LogAssert.IsTrue(Grown(35f, 60f, 700f, 5.5f).FunnelDepth > weak.FunnelDepth, "a longer funnel under less gravity");
		}

		[Test]
		public void AWideCoreUnderALowBaseIsAWedgeAndBreaksDown()
		{
			VortexPhysics.Form wedge = Grown(90f, 250f, 300f, Gravity);
			LogAssert.IsTrue(wedge.FunnelWidthAt(0f, Gravity) > wedge.Top, $"wider ({wedge.FunnelWidthAt(0f, Gravity):0} m) than it is tall ({wedge.Top:0} m)");
			LogAssert.IsTrue(wedge.SubVortices >= 2, $"and split into vortices going round inside it: swirl {wedge.Swirl:0.00}, {wedge.SubVortices}");
			LogAssert.IsTrue(wedge.Hollow > 0.9f, "round a hollow centre");

			VortexPhysics.Form rope = Grown(35f, 60f, 700f, Gravity);
			LogAssert.IsTrue(rope.SubVortices == 0 && rope.Hollow < 0.5f, $"a thin, weak one stays a single tight vortex: swirl {rope.Swirl:0.00}");
		}

		[Test]
		public void MoreSwirlMeansMoreVorticesUpToSix()
		{
			LogAssert.IsTrue(VortexPhysics.SubVortices(VortexPhysics.MultipleVortexSwirl - 0.01f) == 0, "one vortex below the breakdown");
			int previous = 0;
			for (float swirl = VortexPhysics.MultipleVortexSwirl; swirl < 5f; swirl += 0.1f)
			{
				int count = VortexPhysics.SubVortices(swirl);
				LogAssert.IsTrue(count >= 2 && count >= previous && count <= VortexPhysics.MostSubVortices, $"{count} at swirl {swirl:0.0}");
				previous = count;
			}
			LogAssert.IsTrue(previous == VortexPhysics.MostSubVortices, "six at the most");
		}

		[Test]
		public void ATornadoLowersFromTheCloudAndRaisesItsDebrisFirst()
		{
			const float cape = 4050f;
			VortexPhysics.Form young = VortexPhysics.Tornado(cape, 0.35f, 1f, false, 250f, 700f, 2000f, new Vector2(10f, 0f), UpdraftFor(90f), Gravity, VortexPhysics.StripsBoundGround);
			VortexPhysics.Form grown = VortexPhysics.Tornado(cape, 1f, 1f, false, 250f, 700f, 2000f, new Vector2(10f, 0f), UpdraftFor(90f), Gravity, VortexPhysics.StripsBoundGround);
			LogAssert.IsTrue(young.Aloft && young.Lifted > 0f, $"growing, its funnel is still aloft ({young.FunnelDepth:0} m of 700) while its wind already lifts the ground");
			LogAssert.IsFalse(grown.Aloft, "and grown, it has touched down");
		}

		[Test]
		public void ADyingTornadoRopesOutThinAndLeaning()
		{
			const float cape = 4050f;
			var motion = new Vector2(10f, 0f);
			VortexPhysics.Form grown = VortexPhysics.Tornado(cape, 1f, 1f, false, 250f, 700f, 2000f, motion, UpdraftFor(90f), Gravity, VortexPhysics.StripsBoundGround);
			VortexPhysics.Form dying = VortexPhysics.Tornado(cape, 0.3f, 1f, true, 250f, 700f, 2000f, motion, UpdraftFor(90f), Gravity, VortexPhysics.StripsBoundGround);
			LogAssert.IsTrue(dying.Rope > 0.5f, "roping out");
			LogAssert.IsTrue(dying.CoreRadius < 0.6f * grown.CoreRadius, $"thin: {dying.CoreRadius:0} m from {grown.CoreRadius:0} m");
			LogAssert.IsTrue(dying.TopOffset.magnitude > 2f * grown.TopOffset.magnitude, $"leaning: {dying.TopOffset.magnitude:0} m from {grown.TopOffset.magnitude:0} m");
			LogAssert.IsTrue(dying.Sway > grown.Sway, "wandering");
			LogAssert.IsTrue(dying.FunnelDepth < grown.FunnelDepth, "and its funnel lifting");
			LogAssert.IsTrue(dying.SubVortices == 0, "one vortex again");
		}

		[Test]
		public void TheFootLagsTheTop()
		{
			var motion = new Vector2(8f, -6f);
			Vector2 lean = VortexPhysics.TopOffset(motion, 700f, 30f);
			LogAssert.IsTrue(Vector2.Dot(lean, motion) > 0f, "the top stands ahead, where the storm is going");
			LogAssert.IsTrue(VortexPhysics.TopOffset(motion, 700f, 10f).magnitude > lean.magnitude, "and a weaker jet up its axis leans further");
			LogAssert.IsTrue(VortexPhysics.TopOffset(Vector2.zero, 700f, 30f) == Vector2.zero, "a still storm stands upright");
		}

		[Test]
		public void LooseGroundIsLiftedByLessWind()
		{
			float sand = WeatherPhysics.LiftingWind(null, PlanetAir.Earthlike, 300f);
			LogAssert.IsTrue(sand < VortexPhysics.StripsBoundGround, $"loose grains move at {sand:0.0} m/s, before grassland does");
			LogAssert.IsTrue(VortexPhysics.Lifted(30f, sand) > VortexPhysics.Lifted(30f, VortexPhysics.StripsBoundGround), "so a weak vortex raises more of a desert");
			LogAssert.IsTrue(VortexPhysics.Lifted(VortexPhysics.StripsBoundGround * 0.9f, VortexPhysics.StripsBoundGround) == 0f, "and nothing below the threshold");
			LogAssert.IsTrue(VortexPhysics.LiftingRadius(Core, Wind, sand) > VortexPhysics.LiftingRadius(Core, Wind, VortexPhysics.StripsBoundGround), "and further from its core");
		}

		[Test]
		public void ADustDevilCondensesNothing()
		{
			VortexPhysics.Form devil = VortexPhysics.DustDevil(1f, 1f, 8f, 180f, Vector2.zero, WeatherPhysics.LiftingWind(null, PlanetAir.Earthlike, 305f));
			LogAssert.IsTrue(devil.Valid && !devil.Condenses, "dust alone");
			LogAssert.IsTrue(devil.DebrisReachTop > devil.DebrisReachGround, "spreading as its plume climbs");
			LogAssert.IsFalse(VortexPhysics.DustDevil(1f, 1f, 8f, 180f, Vector2.zero, 40f).Valid, "and nothing to see where its wind cannot lift the ground");
		}

		// ── Striations ──────────────────────────────────────────────────────

		/// <summary>A pixel's footprint 3 km off, at 1080 lines and a 60° field: 2·tan 30°·3000/1080 m.</summary>
		private const float PixelAtThreeKilometres = 3.2075f;

		private static float HelixRateOf(in VortexPhysics.Form form, float belowBase, float gravity)
		{
			return VortexPhysics.HelixRate(belowBase, form.CoreRadius, form.PeakWind, gravity, form.AxialUpdraft);
		}

		private static VortexPhysics.Form RopingOut()
		{
			return VortexPhysics.Tornado(4050f, 0.3f, 1f, true, 250f, 700f, 2000f, new Vector2(10f, 0f), UpdraftFor(90f), Gravity, VortexPhysics.StripsBoundGround);
		}

		[Test]
		public void TheBandsWindAtTheWallsOwnPitch()
		{
			float climb = UpdraftFor(Wind);
			float depth = VortexPhysics.FunnelDepth(Wind, Gravity);
			for (float share = 0.05f; share < 0.99f; share += 0.05f)
			{
				float below = share * depth;
				float wall = VortexPhysics.CondensationRadius(below, Core, Wind, Gravity);
				float rate = VortexPhysics.HelixRate(below, Core, Wind, Gravity, climb);
				// The air on the wall goes round at the wall's ω and up at w: the band climbs as ω/w.
				float expected = VortexPhysics.AngularSpeed(wall, Core, Wind) / climb;
				LogAssert.IsTrue(System.Math.Abs(rate - expected) < 0.01f * expected,
					$"at {share:0.00} of the depth the bands should turn {expected:0.00000} rad/m, the wall's ω/w; got {rate:0.00000}");
				// And the turn is its integral up the wall.
				const float step = 0.5f;
				float slope = (VortexPhysics.HelixTurn(below + step, Core, Wind, Gravity, climb) - VortexPhysics.HelixTurn(below - step, Core, Wind, Gravity, climb)) / (2f * step);
				LogAssert.IsTrue(System.Math.Abs(slope - rate) < 0.02f * rate, $"the turn should grow at the rate, {rate:0.00000}; grew at {slope:0.00000}");
			}
			float half = 0.5f * depth;
			float upper = VortexPhysics.HelixTurn(half * 0.9999f, Core, Wind, Gravity, climb);
			float lower = VortexPhysics.HelixTurn(half * 1.0001f, Core, Wind, Gravity, climb);
			LogAssert.IsTrue(System.Math.Abs(upper - lower) < 1e-3f * System.Math.Max(1f, upper), $"the two halves meet at the core's edge: {upper:0.000} and {lower:0.000}");
			LogAssert.IsTrue(VortexPhysics.HelixTurn(-10f, Core, Wind, Gravity, climb) == 0f && VortexPhysics.HelixRate(-10f, Core, Wind, Gravity, climb) == 0f,
				"nothing above the base, where the funnel is the cloud");
		}

		[Test]
		public void TheCorkscrewsPitchIsTheUpdraughtOverTheWind()
		{
			float climb = UpdraftFor(Wind);
			float depth = VortexPhysics.FunnelDepth(Wind, Gravity);
			for (float share = 0.1f; share < 0.95f; share += 0.1f)
			{
				float below = share * depth;
				float wall = VortexPhysics.CondensationRadius(below, Core, Wind, Gravity);
				float angle = VortexPhysics.HelixAngle(below, Core, Wind, Gravity, climb);
				float expected = Mathf.Atan2(climb, VortexPhysics.WindAt(wall, Core, Wind));
				LogAssert.IsTrue(System.Math.Abs(angle - expected) < 1e-3f, $"atan(w/v) at the wall: {expected * Mathf.Rad2Deg:0.0}°, got {angle * Mathf.Rad2Deg:0.0}°");
			}
			// A stronger updraught stands the bands up; a stronger wind lays them down.
			float edge = 0.5f * depth;
			LogAssert.IsTrue(VortexPhysics.HelixAngle(edge, Core, Wind, Gravity, 2f * climb) > VortexPhysics.HelixAngle(edge, Core, Wind, Gravity, climb),
				"more updraught, steeper bands");
			float stronger = 1.3f * Wind;
			LogAssert.IsTrue(VortexPhysics.HelixAngle(0.5f * VortexPhysics.FunnelDepth(stronger, Gravity), Core, stronger, Gravity, climb)
				< VortexPhysics.HelixAngle(edge, Core, Wind, Gravity, climb), "more wind, flatter bands");
		}

		[Test]
		public void ARopeWindsTighterThanTheTornadoItWas()
		{
			VortexPhysics.Form grown = VortexPhysics.Tornado(4050f, 1f, 1f, false, 250f, 700f, 2000f, new Vector2(10f, 0f), UpdraftFor(90f), Gravity, VortexPhysics.StripsBoundGround);
			VortexPhysics.Form dying = RopingOut();
			// How far a band climbs in one turn in the core: 2π over the rate. Narrowed and spun up with its
			// updraught cut off, a rope's air goes round far more for every metre it climbs.
			float grownPitch = 2f * Mathf.PI / HelixRateOf(grown, 0.75f * grown.FunnelDepth, Gravity);
			float dyingPitch = 2f * Mathf.PI / HelixRateOf(dying, 0.75f * dying.FunnelDepth, Gravity);
			LogAssert.IsTrue(dyingPitch < 0.5f * grownPitch, $"a turn every {dyingPitch:0} m roping out, against {grownPitch:0} m grown");
		}

		[Test]
		public void StriationsAreAHandfulAcrossTheFunnelFromRopeToWedge()
		{
			var forms = new (string name, VortexPhysics.Form form)[]
			{
				("rope", Grown(35f, 60f, 700f, Gravity)),
				("tornado", Grown(70f, 160f, 900f, Gravity)),
				("wedge", Grown(90f, 250f, 300f, Gravity)),
				("roping out", RopingOut()),
			};
			int coarsest = VortexPhysics.StriationStarts(0);
			foreach (var (name, form) in forms)
			{
				float reach = Mathf.Min(form.FunnelDepth, form.Top);
				for (float share = 0.1f; share < 0.95f; share += 0.05f)
				{
					float below = share * reach;
					float wall = VortexPhysics.CondensationRadius(below, form.CoreRadius, form.PeakWind, Gravity);
					if (wall <= 1f)
					{
						continue;
					}
					// How many of the coarsest bands lie across the funnel as seen: its width over their spacing.
					float spacing = VortexPhysics.BandSpacing(coarsest, wall, HelixRateOf(form, below, Gravity));
					float across = 2f * wall / spacing;
					Assert.That(across, Is.InRange(0.9f, 8f), $"{name}: {across:0.0} of the coarsest bands across its {2f * wall:0} m, {below:0} m under the base");
				}
			}
		}

		[Test]
		public void AtThreeKilometresTheBroadBandsStayAndTheFineOnesFadeFirst()
		{
			foreach (VortexPhysics.Form form in new[] { Grown(35f, 60f, 700f, Gravity), Grown(70f, 160f, 900f, Gravity) })
			{
				float below = 0.5f * Mathf.Min(form.FunnelDepth, form.Top);
				float wall = VortexPhysics.CondensationRadius(below, form.CoreRadius, form.PeakWind, Gravity);
				float rate = HelixRateOf(form, below, Gravity);
				float previous = float.MaxValue;
				int drawn = 0;
				for (int octave = 0; octave < VortexPhysics.StriationOctaves; octave++)
				{
					float weight = VortexPhysics.BandWeight(VortexPhysics.BandSpacing(VortexPhysics.StriationStarts(octave), wall, rate), PixelAtThreeKilometres);
					LogAssert.IsTrue(weight <= previous, $"a finer octave is never drawn more than a coarser one: {weight:0.00} after {previous:0.00}");
					previous = weight;
					drawn += weight > 0.5f ? 1 : 0;
				}
				float coarse = VortexPhysics.BandSpacing(VortexPhysics.StriationStarts(0), wall, rate);
				LogAssert.IsTrue(VortexPhysics.BandWeight(coarse, PixelAtThreeKilometres) == 1f,
					$"the broadest bands, {coarse:0} m apart on a {2f * wall:0} m funnel, are whole at 3 km");
				LogAssert.IsTrue(drawn >= 2, $"and more than one octave shows there: {drawn}");
				// Further off, less of each and never more.
				float last = 1f;
				for (float distance = 1000f; distance < 60000f; distance *= 1.5f)
				{
					float weight = VortexPhysics.BandWeight(coarse, PixelAtThreeKilometres * distance / 3000f);
					LogAssert.IsTrue(weight <= last, $"the bands grew back at {distance:0} m");
					last = weight;
				}
			}
		}

		[Test]
		public void ABandIsDrawnOnlyWhileAPixelCanHoldIt()
		{
			LogAssert.IsTrue(VortexPhysics.BandWeight(2f, 1f) == 0f && VortexPhysics.BandWeight(1f, 1f) == 0f, "nothing at two pixels or less, where stripes alias to grey");
			LogAssert.IsTrue(VortexPhysics.BandWeight(4f, 1f) == 1f && VortexPhysics.BandWeight(40f, 1f) == 1f, "all of it from four pixels");
			float previous = 0f;
			for (float pixels = 0f; pixels < 6f; pixels += 0.1f)
			{
				float weight = VortexPhysics.BandWeight(pixels, 1f);
				LogAssert.IsTrue(weight >= previous, $"fading in with width, not out: {weight:0.00} at {pixels:0.0} px");
				previous = weight;
			}
		}

		[Test]
		public void TheInflowsRollsSwingItsCondensationLevelByTensOfMetres()
		{
			PlanetAir planet = PlanetAir.Earthlike;
			AirColumn column = AirColumn.Of(planet, 300f, 0.7f, -0.2f, 0.5f);
			float swing = VortexPhysics.CondensationSwing(column, planet);
			// Weckwerth et al.'s 1.9 K of dew point less their 0.5 K of warmth, at 125 m a degree: ±90 m.
			Assert.That(swing, Is.InRange(60f, 130f), $"about ±90 m of condensation level across the rolls, got {swing:0} m");
			// A world that pulls less cools its rising air more slowly, so the same moisture is more lift.
			PlanetAir light = planet;
			light.Gravity = 5.5f;
			float lighter = VortexPhysics.CondensationSwing(AirColumn.Of(light, 300f, 0.7f, -0.2f, 0.5f), light);
			float expected = planet.Gravity / light.Gravity;
			LogAssert.IsTrue(System.Math.Abs(lighter / swing - expected) < 0.05f * expected, $"as 1/g: {lighter / swing:0.00}, not {expected:0.00}");
		}

		[Test]
		public void ARopeIsBandedDeeperForItsSizeThanAWedge()
		{
			PlanetAir planet = PlanetAir.Earthlike;
			float swing = VortexPhysics.CondensationSwing(AirColumn.Of(planet, 300f, 0.7f, -0.2f, 0.5f), planet);
			VortexPhysics.Form rope = Grown(35f, 60f, 700f, Gravity);
			VortexPhysics.Form wedge = Grown(90f, 250f, 300f, Gravity);
			// At the core's edge a metre of lift is R/(V²/g) of a metre of radius, so a band moves the wall
			// by a share swing/(V²/g) of the funnel's radius: bold on a short rope, faint on a deep wedge.
			float ropeShare = swing / rope.FunnelDepth;
			float wedgeShare = swing / wedge.FunnelDepth;
			LogAssert.IsTrue(ropeShare > 3f * wedgeShare, $"a rope's ledges {ropeShare:P0} of its radius, a wedge's {wedgeShare:P0}");
			// And an octave swings the level as the cube root of its size, never past the rolls' own swing.
			float coarse = VortexPhysics.BandSwing(swing, 100f, 3, 3000f);
			float fine = VortexPhysics.BandSwing(swing, 100f, 24, 3000f);
			LogAssert.IsTrue(System.Math.Abs(coarse / fine - 2f) < 1e-3f, $"eight times the size, twice the swing: {coarse / fine:0.000}");
			LogAssert.IsTrue(VortexPhysics.BandSwing(swing, 1e5f, 3, 3000f) == swing, "and never more than the rolls' own");
		}

		[Test]
		public void ABandIsALongRampAndASharpCliff()
		{
			const int samples = 3600;
			float sum = 0f, most = float.MinValue, least = float.MaxValue;
			int rising = 0;
			float previous = VortexPhysics.RampCliff(-2f * Mathf.PI / samples);
			for (int i = 0; i < samples; i++)
			{
				float value = VortexPhysics.RampCliff(2f * Mathf.PI * i / samples);
				sum += value;
				most = Mathf.Max(most, value);
				least = Mathf.Min(least, value);
				rising += value > previous ? 1 : 0;
				previous = value;
			}
			LogAssert.IsTrue(System.Math.Abs(sum / samples) < 1e-3f, $"no mean: the bands swing the lift either way of the rolls' mean, got {sum / samples:0.0000}");
			LogAssert.IsTrue(System.Math.Abs(most - 1f) < 1e-3f && System.Math.Abs(least + 1f) < 1e-3f, $"from −1 to 1: {least:0.000} to {most:0.000}");
			LogAssert.IsTrue(rising < samples / 4, $"the cliff is under a quarter of the band: {100f * rising / samples:0}%");
			LogAssert.IsTrue(VortexPhysics.StriationStarts(0) == 3 && VortexPhysics.StriationStarts(VortexPhysics.StriationOctaves - 1) == 31, "3 to 31 round the funnel");
		}

		// ── Where the bands survive, and how they are lit ─────────────────────

		[Test]
		public void TheBandsLiveOnlyWhereTheSpinHoldsTheStreamlinesApart()
		{
			// The viscous core the Rankine vortex stands in for: stable as a solid body inside, B = 2 at the
			// wind's peak, through the critical quarter a little way out, and nothing left of it beyond.
			float atCore = VortexPhysics.RotationalRichardson(1f);
			LogAssert.IsTrue(System.Math.Abs(atCore - 2f) < 0.05f, $"B = 2 at the core's edge, got {atCore:0.000}");
			float critical = -1f, previous = float.MaxValue;
			for (float x = 0.3f; x < 3f; x += 0.01f)
			{
				float b = VortexPhysics.RotationalRichardson(x);
				LogAssert.IsTrue(b <= previous, $"the spin's hold only weakens outward: {b:0.000} at {x:0.00} R after {previous:0.000}");
				if (critical < 0f && b < 0.25f)
				{
					critical = x;
				}
				previous = b;
			}
			Assert.That(critical, Is.InRange(1.4f, 1.6f), "the shear can mix past about 1.5 core radii");
			LogAssert.IsTrue(System.Math.Abs(critical - VortexPhysics.StriationsWholeWithin) < 0.05f,
				$"the bands are whole out to where B falls through ¼ ({critical:0.00} R), not {VortexPhysics.StriationsWholeWithin} R");
			float circulation = 1f - Mathf.Exp(-VortexPhysics.BurgersRottAlpha * VortexPhysics.StriationsGoneBy * VortexPhysics.StriationsGoneBy);
			LogAssert.IsTrue(circulation > 0.99f, $"and gone where the vortex has its whole circulation: {circulation:P1}");

			LogAssert.IsTrue(VortexPhysics.StriationsKept(Core * 0.5f, Core) == 1f && VortexPhysics.StriationsKept(Core * 1.5f, Core) == 1f, "whole inside");
			LogAssert.IsTrue(VortexPhysics.StriationsKept(Core * 2f, Core) == 0f && VortexPhysics.StriationsKept(Core * 5f, Core) == 0f, "none outside");
			float last = 1f;
			for (float x = 0f; x < 3f; x += 0.02f)
			{
				float kept = VortexPhysics.StriationsKept(x * Core, Core);
				LogAssert.IsTrue(kept <= last, $"fading outward, never back: {kept:0.00} at {x:0.00} R");
				last = kept;
			}
		}

		/// <summary>The furthest out the dampest air at its boldest could condense this far under the base, m.</summary>
		private static float DampestReach(in VortexPhysics.Form form, float below, float swing, float rolls, float gravity, bool kept)
		{
			float furthest = 0f;
			float step = form.CoreRadius / 100f;
			for (float r = step; r < 10f * form.CoreRadius; r += step)
			{
				float damp = 0f;
				for (int octave = 0; octave < VortexPhysics.StriationOctaves; octave++)
				{
					damp += VortexPhysics.BandSwing(swing, r, VortexPhysics.StriationStarts(octave), rolls);
				}
				// The grain makes a band up to half again as bold (FishVortex.hlsl's lerp(0.5, 1.5, grain)).
				damp *= 1.5f * (kept ? VortexPhysics.StriationsKept(r, form.CoreRadius) : 1f);
				if (VortexPhysics.DeficitLift(r, form.CoreRadius, form.PeakWind, gravity) + damp >= below)
				{
					furthest = r;
				}
			}
			return furthest;
		}

		[Test]
		public void TheBandsNeverFanTheFlareOutIntoSpokes()
		{
			PlanetAir earth = PlanetAir.Earthlike;
			PlanetAir arthis = earth;
			arthis.Gravity = 5.52f;
			AirColumn column = AirColumn.Of(earth, 300f, 0.7f, -0.2f, 0.5f);
			AirColumn arthisColumn = AirColumn.Of(arthis, 300f, 0.7f, -0.2f, 0.5f);
			var cases = new (string name, VortexPhysics.Form form, float gravity, float swing)[]
			{
				("rope", Grown(35f, 60f, 700f, Gravity), Gravity, VortexPhysics.CondensationSwing(column, earth)),
				("tornado", Grown(70f, 160f, 900f, Gravity), Gravity, VortexPhysics.CondensationSwing(column, earth)),
				("wedge", Grown(90f, 250f, 300f, Gravity), Gravity, VortexPhysics.CondensationSwing(column, earth)),
				("roping out", RopingOut(), Gravity, VortexPhysics.CondensationSwing(column, earth)),
				// The probe's tornado-close: a supercell's tornado on Arthis, whose swing is nearly twice ours.
				("Arthis", Grown(78f, 212f, 985f, 5.52f), 5.52f, VortexPhysics.CondensationSwing(arthisColumn, arthis)),
			};
			const float rolls = 3000f;
			foreach (var (name, form, gravity, swing) in cases)
			{
				float gone = VortexPhysics.StriationsGoneBy * form.CoreRadius;
				float reach = Mathf.Min(form.FunnelDepth, form.Top);
				for (float share = 0.025f; share < 1f; share += 0.025f)
				{
					float below = share * reach;
					float wall = VortexPhysics.CondensationRadius(below, form.CoreRadius, form.PeakWind, gravity);
					float furthest = DampestReach(form, below, swing, rolls, gravity, true);
					// A step's slack: the scan is a hundredth of a core radius.
					LogAssert.IsTrue(furthest <= Mathf.Max(wall, gone) + 0.011f * form.CoreRadius,
						$"{name}: {below:0} m under the base the dampest band reaches {furthest / form.CoreRadius:0.00} R, past both the wall ({wall / form.CoreRadius:0.00} R) and 2 R");
				}
				// Where the funnel flares into the wall cloud the bands used to carry its edge out as far as
				// anything looked: the spokes.
				float nearBase = 0.05f * reach;
				float unbound = DampestReach(form, nearBase, swing, rolls, gravity, false);
				float smooth = VortexPhysics.CondensationRadius(nearBase, form.CoreRadius, form.PeakWind, gravity);
				LogAssert.IsTrue(unbound > 1.5f * smooth, $"{name}: unbound, the damp bands reached {unbound / form.CoreRadius:0.0} R against the wall's {smooth / form.CoreRadius:0.0} R");
			}
		}

		[Test]
		public void ALedgesSlopesAreItsProfilesAndItsPressuresDerivatives()
		{
			const float h = 1e-3f;
			for (float phase = -3f; phase < 3f; phase += 0.037f)
			{
				float numeric = (VortexPhysics.RampCliff(phase + h) - VortexPhysics.RampCliff(phase - h)) / (2f * h);
				float slope = VortexPhysics.RampCliffSlope(phase);
				LogAssert.IsTrue(System.Math.Abs(numeric - slope) < 0.02f * System.Math.Max(1f, System.Math.Abs(slope)),
					$"the band's slope at {phase:0.000} is {numeric:0.000}, not {slope:0.000}");
			}
			LogAssert.IsTrue(VortexPhysics.RampCliffSlope(0f) > 5f && VortexPhysics.RampCliffSlope(Mathf.PI) < 0f,
				"steeply up the cliff, gently down the ramp");
			for (float x = 0.05f; x < 4f; x += 0.05f)
			{
				float r = x * Core;
				const float dr = 0.5f;
				float numeric = (VortexPhysics.DeficitLift(r + dr, Core, Wind, Gravity) - VortexPhysics.DeficitLift(r - dr, Core, Wind, Gravity)) / (2f * dr);
				float slope = VortexPhysics.DeficitSlope(r, Core, Wind, Gravity);
				LogAssert.IsTrue(slope < 0f && System.Math.Abs(numeric - slope) < 0.01f * System.Math.Abs(slope) + 1e-3f,
					$"the lift falls outward at {numeric:0.0000} m/m at {x:0.00} R, not {slope:0.0000}");
			}
		}

		[Test]
		public void AFaceIsLitByTheSkyItTurnsTo()
		{
			const float storm = 15f * Mathf.Deg2Rad, open = 0.5f * Mathf.PI;
			foreach (float edge in new[] { storm, 0.4f, open })
			{
				foreach (float ground in new[] { 0f, 0.1f, 0.3f })
				{
					LogAssert.IsTrue(System.Math.Abs(VortexPhysics.SkyOnFace(0f, edge, ground) - 1f) < 1e-4f, "an upright face is the measure");
					LogAssert.IsTrue(VortexPhysics.SkyOnFace(-0.6f, edge, ground) < VortexPhysics.SkyOnFace(0f, edge, ground),
						"a face turned down to the ground gets less");
					LogAssert.IsTrue(System.Math.Abs(VortexPhysics.SkyOnFace(-0.5f * Mathf.PI, edge, ground) - 2f * ground) < 1e-3f,
						"one facing straight down, only the ground's light, from the whole of the ground");
				}
			}
			// Under an even open sky a face turned up sees all of it: twice an upright face's.
			Assert.That(VortexPhysics.SkyOnFace(0.5f * Mathf.PI, open, 0f), Is.InRange(1.8f, 2.3f), "the open sky's zenith");
			// Under a storm the sky is a ring low round the horizon: turned up to the dark base, a face gets less than upright.
			LogAssert.IsTrue(VortexPhysics.SkyOnFace(1f, storm, 0.1f) < 1f, "turned up to the storm's base");
			LogAssert.IsTrue(VortexPhysics.SkyOnFace(1f, open, 0.1f) > 1f, "and more than upright in the open");
			LogAssert.IsTrue(System.Math.Abs(VortexPhysics.SkyRing(0f, 0.3f) - 2f * Mathf.Cos(0.3f)) < 1e-4f, "an upright face sees a ring as 2·cos e");
		}

		[Test]
		public void ALedgesCliffIsShadedOnlyWhenAPixelSpansIt()
		{
			LogAssert.IsTrue(VortexPhysics.CliffWeight(6.5f, 1f) == 0f, "a cliff a pixel wide or less draws no shading: it would only sparkle");
			LogAssert.IsTrue(VortexPhysics.CliffWeight(14f, 1f) == 1f, "two pixels across, all of it");
			VortexPhysics.Form form = Grown(70f, 160f, 900f, Gravity);
			float below = 0.5f * Mathf.Min(form.FunnelDepth, form.Top);
			float wall = VortexPhysics.CondensationRadius(below, form.CoreRadius, form.PeakWind, Gravity);
			float rate = HelixRateOf(form, below, Gravity);
			float previous = float.MaxValue;
			for (int octave = 0; octave < VortexPhysics.StriationOctaves; octave++)
			{
				float spacing = VortexPhysics.BandSpacing(VortexPhysics.StriationStarts(octave), wall, rate);
				float weight = VortexPhysics.CliffWeight(spacing, PixelAtThreeKilometres);
				LogAssert.IsTrue(weight <= previous, $"a finer octave's cliff never shades more than a coarser's: {weight:0.00} after {previous:0.00}");
				LogAssert.IsTrue(weight <= VortexPhysics.BandWeight(spacing, PixelAtThreeKilometres), "and never where its band is not drawn");
				previous = weight;
			}
			float coarse = VortexPhysics.BandSpacing(VortexPhysics.StriationStarts(0), wall, rate);
			LogAssert.IsTrue(VortexPhysics.CliffWeight(coarse, PixelAtThreeKilometres) == 1f, $"at 3 km the broad ledges, {coarse:0} m apart, are shaded whole");
			LogAssert.IsTrue(previous < 1f, "and the finest are not");
		}
	}
}
