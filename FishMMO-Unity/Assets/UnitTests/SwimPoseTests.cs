using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Weather;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// How a swimmer is drawn (<see cref="SwimPose"/>): head up at the surface, flat only under it, near the surface
	/// rather than a metre down, riding and tipping with the drawn waves; and where the World Sim figure sits
	/// (<see cref="SitRules"/>).
	/// </summary>
	/// <remarks>
	/// The geometry is the playable prefabs' (a 1.6 m capsule floated with its feet
	/// <see cref="Constants.Character.SwimFloatSubmersion"/> of its height under the still water) and the test human's
	/// rig (its head joint, the base of the skull, 1.559 m over its soles at rest).
	/// </remarks>
	[TestFixture]
	public class SwimPoseTests
	{
		private const float Capsule = 1.6f;
		private const float Still = 10f;
		/// <summary>Where the controller floats the feet: 1.2 m under the still water.</summary>
		private static float FloatFeet => Still - Constants.Character.SwimFloatSubmersion * Capsule;
		/// <summary>The test human's head joint over its soles at rest, metres (the rig's Head bone).</summary>
		private const float HeadJoint = 1.559f;

		[Test]
		public void AFloatingSwimmerIsAtTheSurface()
		{
			Assert.That(SwimPose.SurfaceShare(Still, FloatFeet, Capsule), Is.EqualTo(1f).Within(1e-5f));
			Assert.That(SwimPose.SurfaceShare(Still, FloatFeet + 0.3f, Capsule), Is.EqualTo(1f).Within(1e-5f),
				"bobbing above the float line is still at the surface");
		}

		[Test]
		public void ADiverWellUnderIsNotAtTheSurfaceAndTheShareFallsSmoothlyBetween()
		{
			Assert.That(SwimPose.SurfaceShare(Still, FloatFeet - 1f, Capsule), Is.EqualTo(0f).Within(1e-5f));
			float last = 1f;
			for (float depth = 0f; depth <= 1f; depth += 0.05f)
			{
				float share = SwimPose.SurfaceShare(Still, FloatFeet - depth, Capsule);
				Assert.That(share, Is.LessThanOrEqualTo(last + 1e-6f), $"deeper ({depth:F2} m) is never more at the surface");
				Assert.That(last - share, Is.LessThan(0.2f), $"no jump at {depth:F2} m: the figure would pop between poses");
				last = share;
			}
		}

		[Test]
		public void NoWaterIsNoSurface()
		{
			Assert.That(SwimPose.SurfaceShare(float.NaN, 0f, Capsule), Is.EqualTo(0f));
			Assert.That(SwimPose.SurfaceShare(float.NegativeInfinity, 0f, Capsule), Is.EqualTo(0f));
		}

		[Test]
		public void SwimmingAtTheSurfaceIsHeadUpNotFlat()
		{
			float lean = SwimPose.Lean(1f, 1f, SwimPose.AlongSwim(0f));
			Assert.That(lean, Is.EqualTo(SwimPose.SurfaceLean).Within(1e-4f));
			Assert.That(lean, Is.LessThan(70f), "at the surface it leans into a head-up stroke; lying flat (90) was the fault");
			Assert.That(lean, Is.GreaterThan(30f), "and it does lean into it, not swim standing up");
		}

		[Test]
		public void TreadingAtTheSurfaceIsAllButUpright()
		{
			Assert.That(SwimPose.Lean(1f, 0f, 0f), Is.EqualTo(SwimPose.TreadLean).Within(1e-4f));
			Assert.That(SwimPose.TreadLean, Is.LessThan(20f));
		}

		[Test]
		public void UnderTheSurfaceItLiesAlongItsSwim()
		{
			Assert.That(SwimPose.Lean(0f, 1f, SwimPose.AlongSwim(0f)), Is.EqualTo(90f).Within(1e-4f), "level: flat");
			Assert.That(SwimPose.AlongSwim(SwimPose.Elevation(new Vector3(0f, -1f, 1f))), Is.EqualTo(135f).Within(0.01f), "diving 45°: nose down past flat");
			Assert.That(SwimPose.AlongSwim(SwimPose.Elevation(new Vector3(0f, 1f, 1f))), Is.EqualTo(45f).Within(0.01f), "rising 45°: head up");
			Assert.That(SwimPose.AlongSwim(-90f), Is.EqualTo(170f), "straight down never turns it over");
			Assert.That(SwimPose.AlongSwim(90f), Is.EqualTo(10f), "straight up never stands it quite upright");
		}

		[Test]
		public void ElevationIsTheClimbOfTheSwim()
		{
			Assert.That(SwimPose.Elevation(new Vector3(2f, 0f, 0f)), Is.EqualTo(0f).Within(1e-4f));
			Assert.That(SwimPose.Elevation(new Vector3(0f, -1f, 1f)), Is.EqualTo(-45f).Within(0.01f));
			// Straight up is all but 90: the level speed is floored at a millimetre a second so a still swimmer has no NaN.
			Assert.That(SwimPose.Elevation(new Vector3(0f, 1f, 0f)), Is.EqualTo(90f).Within(0.1f));
		}

		[Test]
		public void HalfUnderTheLeanIsBetweenTheTwo()
		{
			float half = SwimPose.Lean(0.5f, 1f, 90f);
			Assert.That(half, Is.EqualTo((SwimPose.SurfaceLean + 90f) * 0.5f).Within(1e-3f));
		}

		[Test]
		public void ALevelSwimmerLiesJustUnderTheSurfaceNotAMetreDown()
		{
			// Lying flat, every point of the body's axis is at the pivot's height over its feet.
			float depth = Still - (FloatFeet + SwimPose.TurnedHeight(1.8f, 0f, 90f));
			Assert.That(depth, Is.InRange(0f, 0.2f), "turned about the chest, the body lies just under the still line");
			// Turned about its feet, as the game's controller once did, the whole body lay at foot depth.
			Assert.That(Still - FloatFeet, Is.GreaterThan(1f));
		}

		[Test]
		public void TurningAboutThePivotLeavesUprightAlone()
		{
			Assert.That(SwimPose.TurnedHeight(1.5f, 0.1f, 0f), Is.EqualTo(1.5f).Within(1e-5f));
			Assert.That(SwimPose.TurnedHeight(SwimPose.PivotMetres, 0f, 63f), Is.EqualTo(SwimPose.PivotMetres).Within(1e-5f));
			Assert.That(SwimPose.TurnedHeight(1.8f, 0f, 180f), Is.EqualTo(2f * SwimPose.PivotMetres - 1.8f).Within(1e-4f));
		}

		[Test]
		public void TreadingTheWaterStandsAtItsChin()
		{
			float feet = FloatFeet;
			float headAt = feet + SwimPose.TurnedHeight(HeadJoint, 0f, SwimPose.TreadLean);
			float lift = SwimPose.HeadLift(headAt, Still, 0f, 1f);
			Assert.That(headAt + lift, Is.EqualTo(Still + SwimPose.HeadAboveWater).Within(1e-4f), "the head joint just over the water");
			Assert.That(lift, Is.InRange(-0.45f, -0.15f),
				"lowered from the capsule's float line (head and shoulders out) to the water at the chin");
		}

		[Test]
		public void LeaningIntoTheStrokeKeepsTheHeadOut()
		{
			float feet = FloatFeet;
			// The head joint sits a little forward of the body's axis at rest.
			float headAt = feet + SwimPose.TurnedHeight(HeadJoint, 0.02f, SwimPose.SurfaceLean);
			float lift = SwimPose.HeadLift(headAt, Still, 0f, 1f);
			Assert.That(headAt + lift, Is.EqualTo(Still + SwimPose.HeadAboveWater).Within(1e-4f));
		}

		[Test]
		public void ItRidesTheWaves()
		{
			float headAt = FloatFeet + SwimPose.TurnedHeight(HeadJoint, 0f, SwimPose.SurfaceLean);
			float calm = SwimPose.HeadLift(headAt, Still, 0f, 1f);
			Assert.That(SwimPose.HeadLift(headAt, Still, 0.8f, 1f) - calm, Is.EqualTo(0.8f).Within(1e-4f), "a crest lifts it by its height");
			Assert.That(SwimPose.HeadLift(headAt, Still, -0.6f, 1f) - calm, Is.EqualTo(-0.6f).Within(1e-4f), "a trough drops it as deep");
			Assert.That(SwimPose.HeadLift(headAt, Still, 0.8f, 0f), Is.EqualTo(0f), "under the surface the capsule carries it, not the waves");
			Assert.That(SwimPose.HeadLift(headAt, float.NaN, 0.8f, 1f), Is.EqualTo(0f), "no water, no lift");
		}

		[Test]
		public void AWaveTipsItWithItsSlope()
		{
			SwimPose.WaveTilt(0.5f, -0.5f, 1.2f, 0f, 0f, 0.7f, 1f, out float pitch, out float roll);
			Assert.That(pitch, Is.LessThan(0f), "water rising ahead lifts its head");
			// Unity turns +X pitch nose down: forward (0, 0, 1) goes to (0, -sin p, cos p). A negative pitch points it up.
			Assert.That(-Mathf.Sin(pitch * Mathf.Deg2Rad), Is.GreaterThan(0f));
			Assert.That(roll, Is.EqualTo(0f).Within(1e-5f));

			SwimPose.WaveTilt(0f, 0f, 1.2f, 0.3f, -0.3f, 0.7f, 1f, out pitch, out roll);
			Assert.That(roll, Is.LessThan(0f), "water higher on its right rolls its right side up (Unity's +Z roll puts it down)");
			Assert.That(pitch, Is.EqualTo(0f).Within(1e-5f));
		}

		[Test]
		public void AWaveTipsItOnlyPartlyAndNeverOver()
		{
			SwimPose.WaveTilt(0.3f, -0.3f, 1.2f, 0f, 0f, 0.7f, 1f, out float pitch, out _);
			float slope = Mathf.Atan2(0.6f, 1.2f) * Mathf.Rad2Deg;
			Assert.That(-pitch, Is.EqualTo(slope * SwimPose.WaveTiltShare).Within(1e-3f), "it follows a share of the slope");
			SwimPose.WaveTilt(5f, -5f, 1.2f, 5f, -5f, 0.7f, 1f, out pitch, out float roll);
			Assert.That(pitch, Is.EqualTo(-SwimPose.MaxWaveTilt));
			Assert.That(roll, Is.EqualTo(-SwimPose.MaxWaveTilt));
			SwimPose.WaveTilt(5f, -5f, 1.2f, 5f, -5f, 0.7f, 0f, out pitch, out roll);
			Assert.That(pitch, Is.EqualTo(0f).Within(1e-6f), "under the surface the waves do not tip it");
			Assert.That(roll, Is.EqualTo(0f).Within(1e-6f));
		}

		// ── Sitting ──

		[Test]
		public void ABenchIsASeat()
		{
			// StructurePieces.Bench: the plank's top at 0.485 m, 0.34 deep.
			Assert.That(SitRules.IsSeat(0.485f, 0.485f, 1f), Is.True);
			Assert.That(SitRules.IsSeat(0.6f, 0.6f, 0.95f), Is.True, "a stool or a wall's coping");
		}

		[Test]
		public void TheGroundATableAndASlopeAreNot()
		{
			Assert.That(SitRules.IsSeat(0f, 0f, 1f), Is.False, "the ground is sat on as the ground");
			Assert.That(SitRules.IsSeat(0.75f, 0.75f, 1f), Is.False, "a table (0.72 m and up)");
			Assert.That(SitRules.IsSeat(0.2f, 0.2f, 1f), Is.False, "a kerb is too low to sit on as a seat");
			Assert.That(SitRules.IsSeat(0.42f, 0.52f, 0.95f), Is.False, "a slope rises across the seat's depth");
			Assert.That(SitRules.IsSeat(0.5f, 0.5f, 0.7f), Is.False, "a tilted top is not sat on");
		}

		[Test]
		public void NoEvenSlopeTheControllerCanStandOnReadsAsASeat()
		{
			// The seat search looks out to SitRules.Reach. To read seat-high there an even slope must climb MinSeatHeight
			// over that reach, and then it rises across the seat's depth by more than a seat may.
			float steepest = Mathf.Atan2(SitRules.MinSeatHeight, SitRules.Reach);
			float rise = Mathf.Tan(steepest) * SitRules.Depth;
			Assert.That(rise, Is.GreaterThan(SitRules.LevelTolerance));
		}

		// ── The drawn sea, as presentation asks for it ──

		private FakeWaveSea sea;
		private FakeLake lake;

		[TearDown]
		public void TearDown()
		{
			if (sea != null)
			{
				SurfaceWater.Unregister(sea);
				sea = null;
			}
			if (lake != null)
			{
				SurfaceWater.Unregister(lake);
				lake = null;
			}
		}

		[Test]
		public void TheWaveOffsetIsTheDrawnSeaOverItsStillLevel()
		{
			sea = new FakeWaveSea { Level = 2f, Wave = (x, z) => 0.5f * Mathf.Sin(x) };
			SurfaceWater.Register(sea);
			Assert.That(SurfaceWater.WaveOffsetAt(Mathf.PI * 0.5f, 0f), Is.EqualTo(0.5f).Within(1e-5f), "a crest");
			Assert.That(SurfaceWater.WaveOffsetAt(-Mathf.PI * 0.5f, 0f), Is.EqualTo(-0.5f).Within(1e-5f), "a trough");
		}

		[Test]
		public void ALakeStandingHigherHasNoSeaWaves()
		{
			sea = new FakeWaveSea { Level = 2f, Wave = (x, z) => 0.5f };
			lake = new FakeLake { Surface = 40f };
			SurfaceWater.Register(sea);
			SurfaceWater.Register(lake);
			Assert.That(SurfaceWater.WaveOffsetAt(0f, 0f), Is.EqualTo(0f), "a mountain lake does not heave with the sea below it");
			lake.Surface = 1f;
			Assert.That(SurfaceWater.WaveOffsetAt(0f, 0f), Is.EqualTo(0.5f).Within(1e-5f), "a lake under the sea's level is the sea there");
		}

		[Test]
		public void NoWavesWithoutAWavingSea()
		{
			Assert.That(SurfaceWater.WaveOffsetAt(0f, 0f), Is.EqualTo(0f), "no sea");
			sea = new FakeWaveSea { Level = 2f, Wave = (x, z) => float.NaN };
			SurfaceWater.Register(sea);
			Assert.That(SurfaceWater.WaveOffsetAt(0f, 0f), Is.EqualTo(0f), "a broken answer is no wave, not a figure at NaN");
		}

		private sealed class FakeWaveSea : SurfaceWater.IWaveSource
		{
			public float Level { get; set; }
			public System.Func<float, float, float> Wave;
			public float WaveHeight => 1f;
			public bool IsUnder(Vector3 point) => point.y < Level;
			public float SurfaceAt(float x, float z) => Level + Wave(x, z);
		}

		private sealed class FakeLake : SurfaceWater.IAreaSource
		{
			public float Surface;
			public float Level => float.NegativeInfinity;
			public float WaveHeight => 0f;
			public bool IsUnder(Vector3 point) => point.y < Surface;
			public bool TryGetSurface(float x, float z, out float level)
			{
				level = Surface;
				return true;
			}
			public Vector2 CurrentAt(float x, float z) => Vector2.zero;
		}

		// ── The test human's clips (source scan: run from the project root) ──

		private const string ModelPath = "Assets/Prefabs/Client/Models/TestHuman/TestHuman.fbx";
		private const string MigrationPath = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/CharacterModelMigration.cs";
		private const string ImportPath = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/TestHumanImport.cs";

		/// <summary>The takes in the FBX, in file order (Blender writes "&lt;armature&gt;|&lt;action&gt;").</summary>
		private static string[] Takes()
		{
			string text = Encoding.ASCII.GetString(File.ReadAllBytes(ModelPath));
			return Regex.Matches(text, @"TestHuman\|([0-9A-Za-z]+)").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToArray();
		}

		[Test]
		public void EveryClipTheBodyPlaysIsInTheModel()
		{
			string[] takes = Takes();
			string migration = File.ReadAllText(MigrationPath);
			string[] wanted = Regex.Matches(migration, @"Clip\(ModelPath, ""([A-Za-z]+)""\)").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
			Assert.That(wanted, Is.SupersetOf(new[] { "SwimSurface", "SwimUnder", "SwimTread", "SitGround", "SitSeat", "Cast", "Block", "Roll", "Death" }));
			foreach (string clip in wanted)
			{
				Assert.That(takes, Does.Contain(clip), $"{clip} is played but the model has no such take: re-export it (memory autorig/anim.py)");
			}
		}

		[Test]
		public void TheRestPoseIsTheModelsFirstTake()
		{
			// Unity takes the default pose, and builds the Humanoid avatar, from the first take's first frame.
			Assert.That(Takes().First(), Is.EqualTo("0Rest"));
		}

		[Test]
		public void TheCyclesLoopAndTheRetiredCrawlIsGone()
		{
			string import = File.ReadAllText(ImportPath);
			Match looping = Regex.Match(import, @"Looping = \{([^}]*)\}");
			Assert.That(looping.Success, Is.True);
			foreach (string clip in new[] { "SwimSurface", "SwimUnder", "SwimTread", "Climb", "SitGround", "SitSeat", "Block" })
			{
				Assert.That(looping.Groups[1].Value, Does.Contain($"\"{clip}\""), $"{clip} cycles");
			}
			Assert.That(Takes(), Has.No.Member("SwimCrawl"), "the face-down crawl at the surface was the fault; it is not shipped");
		}
	}
}
