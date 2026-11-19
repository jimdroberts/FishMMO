using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Water stacked over one place (2026-10-21, Moron 2: "Weird swimming bug ... Seems like the cave water is generating incorrect
	/// colliders for swimming depth or something?"): a cave's stream at 491 m with a lava tube's river crossing over it on the level
	/// 177 m above, and a lake on the hill over both. The height a swimmer, a footstep or a camera stands at decides which water is
	/// its own: the highest whose floor lies under it. Asked with no height, the highest over the place answers, as before.
	/// </summary>
	[TestFixture]
	public class StackedWaterTests
	{
		private GameObject host;
		private SceneHydrology hydrology;
		private SceneWaterBodies bodies;

		private const float Stream = 491.3f, Lava = 670.1f, Lake = 681f, LakeFloor = 660f;

		[SetUp]
		public void SetUp()
		{
			hydrology = ScriptableObject.CreateInstance<SceneHydrology>();
			// The cave's stream, running east along z = 0; the lava tube's river running north across it at x = 0.
			hydrology.Rivers.Add(River(0, new Vector3(-60f, Stream, 0f), new Vector3(60f, Stream, 0f), 1.2f, 0.5f, false));
			hydrology.Rivers.Add(River(1, new Vector3(0f, Lava, -60f), new Vector3(0f, Lava, 60f), 2.6f, 1.2f, true));
			// The lake on the hill, over the east half of both, its floor known; another west of them, written before floors were kept.
			hydrology.Lakes.Add(Square(0, Lake, LakeFloor, new Vector2(20f, -40f)));
			hydrology.Lakes.Add(Square(1, Lake, float.NegativeInfinity, new Vector2(-100f, -40f)));
			host = new GameObject("Stacked Water Test");
			bodies = host.AddComponent<SceneWaterBodies>();
			bodies.Hydrology = hydrology;
			bodies.Build();
			WaterQuery.RegisterInland(host.scene, bodies);
		}

		[TearDown]
		public void TearDown()
		{
			WaterQuery.UnregisterInland(host.scene, bodies);
			SurfaceWater.Unregister(bodies);
			Object.DestroyImmediate(host);
			Object.DestroyImmediate(hydrology);
		}

		private static SceneHydrology.River River(int id, Vector3 from, Vector3 to, float width, float depth, bool lava)
		{
			int n = 25;
			var river = new SceneHydrology.River
			{
				Id = id,
				Perennial = true,
				Lava = lava,
				Points = new Vector3[n],
				Bed = new float[n],
				Width = new float[n],
				Depth = new float[n],
				Discharge = new float[n],
				Speed = new float[n],
				Reach = new byte[n],
			};
			for (int i = 0; i < n; i++)
			{
				river.Points[i] = Vector3.Lerp(from, to, i / (float)(n - 1));
				river.Width[i] = width;
				river.Depth[i] = depth;
				river.Bed[i] = river.Points[i].y - depth;
				river.Speed[i] = 1f;
			}
			return river;
		}

		/// <summary>A 40 m square lake from <paramref name="corner"/>.</summary>
		private static SceneHydrology.Lake Square(int id, float level, float floor, Vector2 corner)
		{
			var lake = new SceneHydrology.Lake { Id = id, Level = level, SpillLevel = level, Floor = floor, Bounds = new Rect(corner, new Vector2(40f, 80f)), MaskOrigin = corner, MaskCell = 4f, MaskWidth = 10, MaskHeight = 20 };
			lake.Mask = new byte[(10 * 20 + 7) / 8];
			for (int bit = 0; bit < 200; bit++)
			{
				lake.Mask[bit >> 3] |= (byte)(1 << (bit & 7));
			}
			return lake;
		}

		[Test]
		public void ASwimmerInTheCaveStreamIsInTheStream_NotUnderTheLavaOnTheLevelAbove()
		{
			// Where the lava tube's river crosses over the stream (the reported place), at the stream's bed.
			Assert.That(bodies.TryGetSurface(0f, Stream - 0.4f, 0f, out float level, out bool lava), Is.True);
			Assert.That(level, Is.EqualTo(Stream).Within(0.01f), "the stream's own surface");
			Assert.That(lava, Is.False);
			WaterSample sample = WaterQuery.Sample(host.scene, 0f, Stream - 0.4f, 0f, 0u);
			Assert.That(sample.Body, Is.EqualTo(WaterBody.River));
			Assert.That(sample.Surface, Is.EqualTo(Stream).Within(0.01f));
			Assert.That(sample.DepthOf(Stream - 0.4f), Is.LessThan(1f), "wading, not 177 m under");
			Assert.That(bodies.IsUnder(new Vector3(0f, Stream - 0.2f, 0f)), Is.True, "under the stream");
			Assert.That(bodies.IsUnder(new Vector3(0f, Stream + 1.5f, 0f)), Is.False, "a camera in the cave over the stream is in its air");
		}

		[Test]
		public void TheLavaAndTheLakeAreStillTheirOwnLevels()
		{
			Assert.That(bodies.TryGetSurface(0f, Lava - 0.5f, -30f, out float level, out bool lava), Is.True);
			Assert.That(level, Is.EqualTo(Lava).Within(0.01f));
			Assert.That(lava, Is.True, "in the lava tube's river");
			Assert.That(WaterQuery.Sample(host.scene, 0f, Lava - 0.5f, -30f, 0u).Body, Is.EqualTo(WaterBody.Lava));
			// On the hill, over both: the lake.
			Assert.That(bodies.TryGetSurface(30f, Lake - 2f, 0f, out level, out lava), Is.True);
			Assert.That(level, Is.EqualTo(Lake));
			Assert.That(lava, Is.False);
			// In the cave under the lake (its floor 20 m over the stream): the stream.
			Assert.That(bodies.TryGetSurface(30f, Stream - 0.4f, 0f, out level, out _), Is.True);
			Assert.That(level, Is.EqualTo(Stream).Within(0.01f));
			// In the cave's air beside the stream, under the lake: no water at all.
			Assert.That(bodies.TryGetSurface(30f, Stream + 2f, 20f, out _, out _), Is.False);
		}

		[Test]
		public void WithNoHeightTheHighestStillAnswers_AndALakeWithNoFloorReachesDown()
		{
			Assert.That(bodies.TryGetSurface(30f, 0f, out float level, out _), Is.True);
			Assert.That(level, Is.EqualTo(Lake), "the highest over the place, as before");
			Assert.That(WaterQuery.Sample(host.scene, 30f, 0f, 0u).Surface, Is.EqualTo(Lake));
			// A lake written before floors were kept answers for any height, as every lake did.
			Assert.That(bodies.TryGetSurface(-70f, Stream - 0.4f, 0f, out level, out _), Is.True);
			Assert.That(level, Is.EqualTo(Lake));
		}
	}
}
