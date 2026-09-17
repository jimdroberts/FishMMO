using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Client;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The client's weather presentation, minus the GPU: fog laid over region fog, which kinds are
	/// drawn and how they fall, the precipitation mesh, the sky occlusion map, audio levels and
	/// the shader globals.
	/// </summary>
	[TestFixture]
	public class WeatherPresentationTests
	{
		private static WeatherFrame Frame(params (WeatherChannel channel, float value)[] values)
		{
			var frame = new WeatherFrame();
			foreach (var (channel, value) in values)
			{
				frame[channel] = value;
			}
			return frame;
		}

		private WeatherRenderProfile profile;

		[SetUp]
		public void SetUp()
		{
			profile = ScriptableObject.CreateInstance<WeatherRenderProfile>();
		}

		[TearDown]
		public void TearDown()
		{
			Object.DestroyImmediate(profile);
		}

		// ── Fog ──

		[Test]
		public void NoWeatherLeavesTheRegionFogAlone()
		{
			var region = new FogState { Enabled = true, Mode = FogMode.Linear, Color = Color.red, Density = 0.01f, StartDistance = 20f, EndDistance = 300f };
			FogState result = FogComposer.Compose(region, 0f, Color.blue, 0.05f, 40f);
			LogAssert.AreEqual(region.Color, result.Color);
			LogAssert.AreEqual(region.EndDistance, result.EndDistance);
			LogAssert.AreEqual(region.Density, result.Density);
		}

		[Test]
		public void WeatherThickensRegionFogAndPullsItsColour()
		{
			var region = new FogState { Enabled = true, Mode = FogMode.ExponentialSquared, Color = Color.red, Density = 0.01f, StartDistance = 20f, EndDistance = 300f };
			FogState result = FogComposer.Compose(region, 0.5f, Color.blue, 0.06f, 40f);
			LogAssert.IsTrue(result.Enabled);
			LogAssert.AreEqual(FogMode.ExponentialSquared, result.Mode, "the region's fog mode is kept");
			Assert.That(result.Density, Is.EqualTo(0.04f).Within(1e-5f));
			Assert.That(result.Color.b, Is.EqualTo(0.5f).Within(1e-4f));
			Assert.That(result.EndDistance, Is.EqualTo(170f).Within(1e-3f));
			LogAssert.IsTrue(result.StartDistance <= result.EndDistance);
		}

		[Test]
		public void WeatherBringsItsOwnFogWhereTheRegionHasNone()
		{
			var region = new FogState { Enabled = false, Mode = FogMode.Linear, Color = Color.red, Density = 0f };
			FogState result = FogComposer.Compose(region, 0.8f, Color.gray, 0.05f, 40f);
			LogAssert.IsTrue(result.Enabled);
			LogAssert.AreEqual(FogMode.ExponentialSquared, result.Mode);
			LogAssert.AreEqual(Color.gray, result.Color);
			Assert.That(result.Density, Is.EqualTo(0.04f).Within(1e-5f));
			LogAssert.IsFalse(FogComposer.Compose(region, 0f, Color.gray, 0.05f, 40f).Enabled, "and takes it away again");
		}

		[Test]
		public void FogComesFromTheFogChannelAndFromWhatFalls()
		{
			Assert.That(WeatherFogPresenter.Amount(new WeatherFrame()), Is.EqualTo(0f));
			Assert.That(WeatherFogPresenter.Amount(Frame((WeatherChannel.FogDensity, 0.5f))), Is.EqualTo(0.5f).Within(1e-5f));
			float sand = WeatherFogPresenter.Amount(Frame((WeatherChannel.Precipitation, 1f), (WeatherChannel.SandWeight, 1f)));
			float rain = WeatherFogPresenter.Amount(Frame((WeatherChannel.Precipitation, 1f), (WeatherChannel.RainWeight, 1f)));
			LogAssert.IsTrue(sand > rain, $"a sandstorm hides more than rain ({sand:0.00} vs {rain:0.00})");
			LogAssert.IsTrue(sand <= 1f);
		}

		[Test]
		public void FogTakesTheColourOfWhatFalls()
		{
			Color mist = WeatherFogPresenter.ColorOf(Frame((WeatherChannel.FogDensity, 1f)), profile);
			LogAssert.AreEqual(profile.MistColor, mist);
			Color sand = WeatherFogPresenter.ColorOf(Frame((WeatherChannel.Precipitation, 1f), (WeatherChannel.SandWeight, 1f)), profile);
			LogAssert.IsTrue(sand.r > sand.b, "sand fog is warm");
			Color ash = WeatherFogPresenter.ColorOf(Frame((WeatherChannel.Precipitation, 1f), (WeatherChannel.AshWeight, 1f)), profile);
			LogAssert.IsTrue(ash.grayscale < mist.grayscale, "ash fog is darker than mist");
		}

		// ── Precipitation ──

		[Test]
		public void TheStrongestKindsAreDrawnFirstAndAtMostThree()
		{
			var drawn = new List<(WeatherChannel, float)>();
			PrecipitationField.Choose(Frame((WeatherChannel.Precipitation, 1f), (WeatherChannel.RainWeight, 0.1f), (WeatherChannel.SnowWeight, 0.4f),
				(WeatherChannel.HailWeight, 0.2f), (WeatherChannel.AshWeight, 0.2f), (WeatherChannel.SandWeight, 0.1f)), drawn);
			LogAssert.AreEqual(PrecipitationField.MaxKindsAtOnce, drawn.Count);
			LogAssert.AreEqual(WeatherChannel.SnowWeight, drawn[0].Item1);
			Assert.That(drawn[0].Item2, Is.EqualTo(0.4f).Within(1e-5f));

			PrecipitationField.Choose(Frame((WeatherChannel.RainWeight, 1f)), drawn);
			LogAssert.AreEqual(0, drawn.Count, "nothing falls without precipitation");
		}

		[Test]
		public void TheWindPushesWhatFalls()
		{
			PrecipitationLook sand = PrecipitationLook.Sand();
			PrecipitationLook rain = PrecipitationLook.Rain();
			WeatherFrame east = Frame((WeatherChannel.WindSpeed, 0.5f), (WeatherChannel.WindHeading, 90f));
			Vector3 sandFall = PrecipitationField.FallVelocity(east, sand, 0f);
			Vector3 rainFall = PrecipitationField.FallVelocity(east, rain, 0f);
			LogAssert.IsTrue(sandFall.x > 0f && Mathf.Abs(sandFall.z) < 1e-3f, "a wind toward 90° blows toward +X");
			LogAssert.IsTrue(sandFall.x > rainFall.x, "sand follows the wind more than rain");
			LogAssert.IsTrue(rainFall.y < sandFall.y, "rain falls faster");
			LogAssert.IsTrue(PrecipitationField.FallVelocity(new WeatherFrame(), rain, 0f).y < 0f, "everything falls down");
		}

		[Test]
		public void TheFieldMeshSharesOnePositionPerQuad()
		{
			Mesh mesh = PrecipitationField.BuildMesh(100, 7);
			try
			{
				LogAssert.AreEqual(400, mesh.vertexCount);
				LogAssert.AreEqual(IndexFormat.UInt16, mesh.indexFormat);
				var positions = new List<Vector3>();
				mesh.GetVertices(positions);
				var randoms = new List<Vector4>();
				mesh.GetUVs(1, randoms);
				var thresholds = new HashSet<float>();
				for (int q = 0; q < 100; q++)
				{
					for (int c = 1; c < 4; c++)
					{
						LogAssert.AreEqual(positions[q * 4], positions[q * 4 + c], "a quad's corners share its particle position");
						LogAssert.AreEqual(randoms[q * 4], randoms[q * 4 + c]);
					}
					thresholds.Add(randoms[q * 4].x);
				}
				LogAssert.AreEqual(100, thresholds.Count, "each particle has its own show threshold, so density is exact");
			}
			finally
			{
				Object.DestroyImmediate(mesh);
			}

			Mesh big = PrecipitationField.BuildMesh(20000);
			try
			{
				LogAssert.AreEqual(IndexFormat.UInt32, big.indexFormat, "80 000 vertices need 32-bit indices");
			}
			finally
			{
				Object.DestroyImmediate(big);
			}
		}

		// ── Sky occlusion ──

		[Test]
		public void TheSkyMapKnowsWhatIsOverhead()
		{
			var map = new SkyOcclusionMap();
			try
			{
				map.Allocate(4, 2f);
				var heights = new float[16];
				for (int i = 0; i < 16; i++)
				{
					heights[i] = -1f;
				}
				heights[1 * 4 + 2] = 6f; // a roof over the texel at x 2, z 1
				map.Publish(heights, new Vector2(10f, 20f));
				LogAssert.IsTrue(map.IsValid);
				// The map spans x 6..14 and z 16..24; texel (2, 1) covers x 10..12, z 18..20.
				LogAssert.IsTrue(map.TryGetHeight(11f, 19f, out float roof));
				Assert.That(roof, Is.EqualTo(6f));
				LogAssert.IsTrue(map.IsCovered(new Vector3(11f, 1.7f, 19f)), "under the roof");
				LogAssert.IsFalse(map.IsCovered(new Vector3(11f, 7f, 19f)), "above the roof");
				LogAssert.IsFalse(map.IsCovered(new Vector3(7f, 1.7f, 17f)), "open ground");
				LogAssert.IsFalse(map.TryGetHeight(100f, 19f, out _), "off the map");

				// The texture stores 16 bits across R and G: decode it the way the shader does.
				Color32 texel = map.Texture.GetPixels32()[1 * 4 + 2];
				float encoded = texel.r / 255f * (255f / 256f) + texel.g / 255f * (1f / 256f);
				Assert.That(-1f + encoded * 7f, Is.EqualTo(6f).Within(0.01f));

				map.Invalidate();
				LogAssert.IsFalse(map.IsCovered(new Vector3(11f, 1.7f, 19f)), "an invalid map covers nothing");
			}
			finally
			{
				map.Dispose();
			}
		}

		// ── Audio ──

		[Test]
		public void LoopsFollowTheWeather()
		{
			var dry = new WeatherFrame();
			foreach (WeatherAudioCue cue in (WeatherAudioCue[])System.Enum.GetValues(typeof(WeatherAudioCue)))
			{
				LogAssert.AreEqual(0f, WeatherAudioPresenter.TargetVolume(cue, dry, 0f), $"{cue} is silent in calm, dry weather");
			}

			WeatherFrame drizzle = Frame((WeatherChannel.Precipitation, 0.2f), (WeatherChannel.RainWeight, 1f));
			WeatherFrame downpour = Frame((WeatherChannel.Precipitation, 1f), (WeatherChannel.RainWeight, 1f));
			LogAssert.IsTrue(WeatherAudioPresenter.TargetVolume(WeatherAudioCue.RainLight, drizzle, 0f) > WeatherAudioPresenter.TargetVolume(WeatherAudioCue.RainHeavy, drizzle, 0f));
			LogAssert.IsTrue(WeatherAudioPresenter.TargetVolume(WeatherAudioCue.RainHeavy, downpour, 0f) > WeatherAudioPresenter.TargetVolume(WeatherAudioCue.RainLight, downpour, 0f));

			float outside = WeatherAudioPresenter.TargetVolume(WeatherAudioCue.RainHeavy, downpour, 0f);
			float inside = WeatherAudioPresenter.TargetVolume(WeatherAudioCue.RainHeavy, downpour, 1f);
			LogAssert.IsTrue(inside < outside, "rain is quieter indoors");
			LogAssert.IsTrue(WeatherAudioPresenter.TargetVolume(WeatherAudioCue.RainOnRoof, downpour, 1f) > 0.5f, "and drums on the roof");
			LogAssert.AreEqual(0f, WeatherAudioPresenter.TargetVolume(WeatherAudioCue.RainOnRoof, downpour, 0f));

			WeatherFrame gale = Frame((WeatherChannel.WindSpeed, 0.9f));
			LogAssert.IsTrue(WeatherAudioPresenter.TargetVolume(WeatherAudioCue.WindStrong, gale, 0f) > WeatherAudioPresenter.TargetVolume(WeatherAudioCue.Wind, gale, 0f));
		}

		[Test]
		public void TheCueTableListsEveryCueWithoutClips()
		{
			var audio = ScriptableObject.CreateInstance<WeatherAudioProfile>();
			try
			{
				foreach (WeatherAudioCue cue in (WeatherAudioCue[])System.Enum.GetValues(typeof(WeatherAudioCue)))
				{
					WeatherAudioEntry entry = audio.Find(cue);
					LogAssert.IsNotNull(entry, $"{cue} has a slot");
					LogAssert.IsNull(entry.Clip, "slots start empty");
				}
				LogAssert.IsFalse(WeatherAudioProfile.IsLoop(WeatherAudioCue.ThunderNear));
				LogAssert.IsTrue(WeatherAudioProfile.IsLoop(WeatherAudioCue.RainHeavy));
			}
			finally
			{
				Object.DestroyImmediate(audio);
			}
		}

		// ── Profile and globals ──

		[Test]
		public void EachQualityLevelHasItsOwnBudget()
		{
			LogAssert.AreSame(profile.Performant, profile.TierFor(0));
			LogAssert.AreSame(profile.Balanced, profile.TierFor(1));
			LogAssert.AreSame(profile.HighFidelity, profile.TierFor(2));
			LogAssert.AreSame(profile.HighFidelity, profile.TierFor(5));
			LogAssert.IsTrue(profile.Performant.Particles < profile.Balanced.Particles && profile.Balanced.Particles < profile.HighFidelity.Particles);
			LogAssert.IsTrue(profile.Performant.OcclusionResolution < profile.HighFidelity.OcclusionResolution);
			LogAssert.AreSame(profile.Sand, profile.LookOf(WeatherChannel.SandWeight));
			LogAssert.AreSame(profile.Rain, profile.LookOf(WeatherChannel.RainWeight));
		}

		[Test]
		public void TheShaderGlobalsCarryTheFrame()
		{
			WeatherFrame frame = Frame((WeatherChannel.CloudCover, 0.7f), (WeatherChannel.Precipitation, 0.4f), (WeatherChannel.WindSpeed, 0.5f),
				(WeatherChannel.WindHeading, 180f), (WeatherChannel.FogDensity, 0.3f), (WeatherChannel.Aurora, 0.2f));
			var cover = new WeatherCover { Snow = 0.1f, Wet = 0.2f };
			WeatherShaderGlobals.Apply(frame, cover, -0.4f, 1f, 12f, 0f);
			try
			{
				Vector4 cloud = Shader.GetGlobalVector(WeatherShaderGlobals.Cloud);
				Vector4 wind = Shader.GetGlobalVector(WeatherShaderGlobals.Wind);
				Vector4 misc = Shader.GetGlobalVector(WeatherShaderGlobals.Misc);
				Assert.That(cloud.x, Is.EqualTo(0.7f).Within(1e-5f));
				Assert.That(Shader.GetGlobalVector(WeatherShaderGlobals.Precip).x, Is.EqualTo(0.4f).Within(1e-5f));
				Assert.That(wind.y, Is.EqualTo(-1f).Within(1e-4f), "heading 180° blows toward −Z");
				Assert.That(wind.z, Is.EqualTo(0.5f).Within(1e-5f));
				Assert.That(Shader.GetGlobalVector(WeatherShaderGlobals.Cover).y, Is.EqualTo(0.2f).Within(1e-5f));
				Assert.That(misc.y, Is.EqualTo(-0.4f).Within(1e-5f));
				Assert.That(misc.z, Is.EqualTo(1f));
			}
			finally
			{
				WeatherShaderGlobals.Clear();
			}
			Assert.That(Shader.GetGlobalVector(WeatherShaderGlobals.Precip).x, Is.EqualTo(0f));
		}
	}
}
