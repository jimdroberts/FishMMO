using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Audio
{
	/// <summary>
	/// The world's synthesised sound, minus the audio device: the recipes make sound (finite, in range, the same every
	/// time, loops without a seam), the grounds are named right, footfalls are found where feet come down, and thunder
	/// arrives, sounds and fades as it does through air.
	/// </summary>
	[TestFixture]
	public class WorldSoundTests
	{
		// ── The recipes ──

		[Test]
		public void EveryFootstepIsSoundInRangeAndTheSameEveryTime()
		{
			foreach (SurfaceKind kind in (SurfaceKind[])Enum.GetValues(typeof(SurfaceKind)))
			{
				foreach (bool landing in new[] { false, true })
				{
					float[] a = FootstepSounds.Step(kind, landing, 0);
					float[] b = FootstepSounds.Step(kind, landing, 0);
					Sound(a, $"{kind} {(landing ? "landing" : "step")}");
					LogAssert.IsTrue(Same(a, b), $"{kind} is made the same on every client");
					LogAssert.IsFalse(Same(a, FootstepSounds.Step(kind, landing, 1)), $"{kind}'s variants differ");
				}
			}
			Sound(FootstepSounds.Swim(false, 0), "stroke");
			Sound(FootstepSounds.Swim(true, 0), "plunge");
			Sound(FootstepSounds.Rung(0), "rung");
			Sound(FootstepSounds.Rustle(0), "rustle");
		}

		[Test]
		public void EachGroundKeepsItsRecordedBrightness()
		{
			// As the reference recordings have them: a step on sand or leaves is all hiss and crackle (centred at 3–4.5 kHz),
			// a step on stone or boards a low knock (centred near 350 Hz).
			float sand = Crossings(FootstepSounds.Step(SurfaceKind.Sand, false, 0));
			float leaves = Crossings(FootstepSounds.Step(SurfaceKind.Leaves, false, 0));
			float stone = Crossings(FootstepSounds.Step(SurfaceKind.Stone, false, 0));
			float wood = Crossings(FootstepSounds.Step(SurfaceKind.Wood, false, 0));
			LogAssert.IsTrue(sand > 2f * stone, $"sand ({sand:F0}/s) hisses far brighter than stone knocks ({stone:F0}/s)");
			LogAssert.IsTrue(leaves > 2f * wood, $"leaves ({leaves:F0}/s) crackle far brighter than boards ({wood:F0}/s)");
		}

		[Test]
		public void AStepTakesAsLongAsARecordedOne()
		{
			// The first recipes were a tenth of a second; recorded steps hold near their peak for 0.15–0.6 s.
			foreach (SurfaceKind kind in new[] { SurfaceKind.Grass, SurfaceKind.Gravel, SurfaceKind.Snow, SurfaceKind.Water })
			{
				float[] step = FootstepSounds.Step(kind, false, 0);
				LogAssert.IsTrue(Above(step, -20f) > 0.2f, $"{kind} lasts beyond 0.2 s within 20 dB of its peak ({Above(step, -20f):F2} s)");
			}
		}

		[Test]
		public void LoopsArePeriodic()
		{
			// Made in the frequency domain: the sample after the end is the one at the start, so no seam at all.
			foreach (AmbientBed bed in (AmbientBed[])Enum.GetValues(typeof(AmbientBed)))
			{
				float[] loop = AmbientSounds.Make(bed);
				LogAssert.AreEqual(AmbientSounds.LoopSamples, loop.Length, $"{bed} is one loop long");
			}
		}

		[Test]
		public void EveryLoopIsSoundAndSeamless()
		{
			int length = AmbientSounds.LoopSamples;
			foreach (WeatherAudioCue cue in (WeatherAudioCue[])Enum.GetValues(typeof(WeatherAudioCue)))
			{
				float[] loop = AmbientSounds.Make(cue);
				if (!AmbientSounds.Synthesised(cue))
				{
					LogAssert.IsNull(loop, $"{cue} is a one-shot: thunder is made per strike");
					continue;
				}
				Sound(loop, cue.ToString());
				LogAssert.AreEqual(length, loop.Length, $"{cue} is one loop long");
				Seamless(loop, cue.ToString());
			}
			foreach (AmbientBed bed in (AmbientBed[])Enum.GetValues(typeof(AmbientBed)))
			{
				float[] loop = AmbientSounds.Make(bed);
				Sound(loop, bed.ToString());
				Seamless(loop, bed.ToString());
			}
		}

		[Test]
		public void HeavyRainIsDenserThanLightRain()
		{
			float light = Rms(AmbientSounds.Make(WeatherAudioCue.RainLight));
			float heavy = Rms(AmbientSounds.Make(WeatherAudioCue.RainHeavy));
			LogAssert.IsTrue(heavy > light, $"a downpour ({heavy:F3}) is louder than a drizzle ({light:F3})");
		}

		[Test]
		public void TheLoopIsWhereTheClockSays()
		{
			AudioClip clip = null;
			int rate = SoundSynth.Rate;
			int samples = AmbientSounds.LoopSamples;
			// No clip: the arithmetic alone (SampleAt needs a clip's length and rate).
			LogAssert.AreEqual(0, ClockedLoop.SampleAt(clip, 12.0));
			double wrapped = FishMMO.Shared.Celestial.WorldMotion.Repeat(1234567.25 * rate, samples);
			LogAssert.IsTrue(wrapped >= 0 && wrapped < samples, "a world-clock moment falls inside the loop");
		}

		// ── The grounds ──

		[TestCase("Ground_Grass", SurfaceKind.Grass)]
		[TestCase("Ground_GrassMeadow", SurfaceKind.Grass)]
		[TestCase("Ground_Sandstone", SurfaceKind.Stone)]
		[TestCase("Ground_SandBeach", SurfaceKind.Sand)]
		[TestCase("Ground_PathMud", SurfaceKind.Mud)]
		[TestCase("Ground_PathLoam", SurfaceKind.Dirt)]
		[TestCase("Ground_PathSnow", SurfaceKind.Snow)]
		[TestCase("Ground_ForestFloor", SurfaceKind.Leaves)]
		[TestCase("Ground_NeedleLitter", SurfaceKind.Leaves)]
		[TestCase("Ground_Pebbles", SurfaceKind.Gravel)]
		[TestCase("Ground_SaltCrust", SurfaceKind.Gravel)]
		[TestCase("Ground_Flagstone", SurfaceKind.Stone)]
		[TestCase("Ground_CrackedEarth", SurfaceKind.Dirt)]
		[TestCase("Ground_Ice", SurfaceKind.Ice)]
		[TestCase("Cliff_Ice_8x8_T50_Start_V0_Collision", SurfaceKind.Ice)]
		[TestCase("Cliff_Massive_8x8_T70_Start_V0_Collision", SurfaceKind.Stone)]
		[TestCase("Tree_Oak_Collision", SurfaceKind.Wood)]
		[TestCase("Structure_TimberHouseSmall_0", SurfaceKind.Wood)]
		[TestCase("Structure_StoneHouse_3_Mossy", SurfaceKind.Stone)]
		[TestCase("Structure_RuinedTimberHouse_1", SurfaceKind.Wood)]
		[TestCase("Structure_StrawBed_0", SurfaceKind.Grass)]
		[TestCase("Structure_SkyDock_2", SurfaceKind.Wood)]
		public void NamesSayWhatTheGroundIs(string name, SurfaceKind expected)
		{
			LogAssert.IsTrue(GroundSurface.TryName(name, out SurfaceKind kind), $"{name} is known");
			LogAssert.AreEqual(expected, kind, name);
		}

		[TestCase("Structure_WashingLine_0")]
		[TestCase("Lattice")]
		public void APartOfAWordIsNotTheWord(string name)
		{
			// "WashingLine" holds "ash", "Lattice" holds "ice": neither is ash or ice underfoot.
			LogAssert.IsFalse(GroundSurface.TryName(name, out SurfaceKind kind), $"{name} read as {kind}");
		}

		[Test]
		public void EveryGeneratedGroundLayerIsKnown()
		{
			string folder = "Assets/Prefabs/Shared/Biomes/Generated/TerrainLayers";
			if (!Directory.Exists(folder))
			{
				Assert.Ignore("No generated terrain layers here.");
			}
			var unknown = new List<string>();
			foreach (string path in Directory.GetFiles(folder, "*.terrainlayer", SearchOption.AllDirectories))
			{
				string name = Path.GetFileNameWithoutExtension(path);
				if (!GroundSurface.TryName(name, out _))
				{
					unknown.Add(name);
				}
			}
			LogAssert.AreEqual(0, unknown.Count, "layers with no footstep: " + string.Join(", ", unknown));
		}

		// ── Footfalls ──

		[Test]
		public void AWalkStepsOnceAFootAndAlternates()
		{
			var tracker = new FootfallTracker();
			const float fps = 60f, stride = 0.95f, speed = 1.4f;
			var feet = new List<int>();
			for (int frame = 0; frame < 3 * fps; frame++)
			{
				float t = frame / fps;
				for (int foot = 0; foot < 2; foot++)
				{
					// Each foot swings up 15 cm for half the stride and stands for the other half.
					float phase = t * stride + foot * 0.5f;
					float gap = Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * phase)) * 0.15f;
					if (tracker.FootDown(foot, gap, speed, t))
					{
						feet.Add(foot);
					}
				}
			}
			LogAssert.IsTrue(feet.Count >= 4 && feet.Count <= 6, $"about two steps a stride over three seconds ({feet.Count})");
			for (int i = 1; i < feet.Count; i++)
			{
				LogAssert.AreNotEqual(feet[i - 1], feet[i], "left, right, left");
			}
		}

		[Test]
		public void StandingStillMakesNoSound()
		{
			var tracker = new FootfallTracker();
			for (int frame = 0; frame < 120; frame++)
			{
				float gap = frame % 20 < 10 ? 0.1f : 0f;
				LogAssert.IsFalse(tracker.FootDown(0, gap, 0f, frame / 60f), "a weight shift standing still is not a step");
				LogAssert.AreEqual(-1, tracker.Stride(0f, 0f, 1.8f));
			}
		}

		[Test]
		public void UnseenFeetStepAStrideApart()
		{
			var tracker = new FootfallTracker();
			int steps = 0;
			const float speed = 1.4f, dt = 1f / 60f;
			for (int frame = 0; frame < 600; frame++)
			{
				if (tracker.Stride(speed * dt, speed, 1.8f) >= 0)
				{
					steps++;
				}
			}
			float expected = speed * 10f / FootfallTracker.StepMetres(speed, 1.8f);
			LogAssert.IsTrue(Mathf.Abs(steps - expected) <= 1f, $"{steps} steps over 14 m, about {expected:F1}");
			LogAssert.IsTrue(FootfallTracker.StepMetres(4f, 1.8f) > FootfallTracker.StepMetres(1.4f, 1.8f), "a run steps further than a walk");
		}

		[Test]
		public void AFallLandsAndAHopDoesNot()
		{
			var tracker = new FootfallTracker();
			LogAssert.IsFalse(tracker.Landed(true, false, -6f, out _));
			LogAssert.IsTrue(tracker.Landed(false, true, 0f, out float speed), "down from a jump");
			LogAssert.AreEqual(6f, speed);
			LogAssert.IsFalse(tracker.Landed(true, false, -1f, out _));
			LogAssert.IsFalse(tracker.Landed(false, true, 0f, out _), "a step down a stair is a step");
		}

		[Test]
		public void FallingIntoWaterIsAPlunge()
		{
			var tracker = new FootfallTracker();
			tracker.Landed(true, false, -7f, out _);
			LogAssert.AreEqual((FootfallKind?)FootfallKind.Plunge, tracker.Swim(true, 1f, 0f, -7f, out float speed));
			LogAssert.IsTrue(speed >= 7f);
			int strokes = 0;
			for (int frame = 0; frame < 300; frame++)
			{
				if (tracker.Swim(true, 1f, 1.2f / 60f, 0f, out _) == FootfallKind.Stroke)
				{
					strokes++;
				}
			}
			LogAssert.AreEqual(5, strokes, "a stroke every 1.1 m over 6 m");
		}

		// ── Thunder ──

		[Test]
		public void ThunderArrivesFromTheNearestPieceAndRolls()
		{
			ThunderSynth.Result result = ThunderSynth.Render(Bolt(1000f, 1200f), Vector3.zero);
			LogAssert.IsTrue(result.NearestMetres >= 1000f && result.NearestMetres < 1010f, $"first heard from the foot of the bolt ({result.NearestMetres:F0} m)");
			float span = (result.FurthestMetres - result.NearestMetres) / SoundSynth.SpeedOfSound;
			LogAssert.IsTrue(result.Samples.Length >= span * ThunderSynth.ThunderRate, "it lasts as long as the channel's sound takes to pass");
			Sound(result.Samples, "thunder");
			LogAssert.IsTrue(Same(result.Samples, ThunderSynth.Render(Bolt(1000f, 1200f), Vector3.zero).Samples), "every client makes the same thunder");
		}

		[Test]
		public void FarThunderRumblesWhereNearThunderCracks()
		{
			float near = Crossings(ThunderSynth.Render(Bolt(300f, 1200f), Vector3.zero).Samples, ThunderSynth.ThunderRate);
			float far = Crossings(ThunderSynth.Render(Bolt(9000f, 1200f), Vector3.zero).Samples, ThunderSynth.ThunderRate);
			LogAssert.IsTrue(far < near, $"the air takes the highs off far thunder ({far:F0}/s against {near:F0}/s)");
		}

		[Test]
		public void ThunderFadesWithDistanceAndStopsBeyondEarshot()
		{
			float last = float.MaxValue;
			foreach (float metres in new[] { 400f, 1000f, 3000f, 8000f, 12000f })
			{
				float level = ThunderPresenter.Level(metres, 1f);
				LogAssert.IsTrue(level < last || metres == 400f, $"quieter at {metres} m");
				LogAssert.IsTrue(level > 0f, $"still heard at {metres} m");
				last = level;
			}
			LogAssert.AreEqual(0f, ThunderPresenter.Level(ThunderPresenter.AudibleMetres + 1f, 1f), "not heard past earshot");
			LogAssert.IsTrue(OneShotVoices.AirCutoff(5000f) < OneShotVoices.AirCutoff(500f), "the air dulls a long path more");
		}

		// ── Helpers ──

		private static ThunderSynth.Channel Bolt(float distance, float height)
		{
			var channel = new ThunderSynth.Channel { Seed = 1234u, Intensity = 1f };
			channel.Lines.Add(new[] { new Vector3(distance, 0f, 0f), new Vector3(distance, height, 0f) });
			return channel;
		}

		private static void Sound(float[] data, string what)
		{
			LogAssert.IsNotNull(data, what);
			LogAssert.IsTrue(data.Length > 0, $"{what} has samples");
			float peak = 0f;
			foreach (float x in data)
			{
				LogAssert.IsFalse(float.IsNaN(x) || float.IsInfinity(x), $"{what} is finite");
				peak = Mathf.Max(peak, Mathf.Abs(x));
			}
			LogAssert.IsTrue(peak <= 1f, $"{what} does not clip ({peak:F3})");
			LogAssert.IsTrue(Rms(data) > 1e-3f, $"{what} is not silent");
		}

		/// <summary>The step across the loop's end is no bigger than the steps inside it.</summary>
		private static void Seamless(float[] loop, string what)
		{
			double sum = 0.0;
			for (int i = 1; i < loop.Length; i++)
			{
				sum += Mathf.Abs(loop[i] - loop[i - 1]);
			}
			float mean = (float)(sum / (loop.Length - 1));
			float seam = Mathf.Abs(loop[0] - loop[loop.Length - 1]);
			LogAssert.IsTrue(seam <= 8f * mean + 1e-4f, $"{what} loops without a click ({seam:F4} against {mean:F4})");
		}

		/// <summary>Seconds a sound spends within <paramref name="db"/> of its loudest, by 10 ms windows.</summary>
		private static float Above(float[] data, float db)
		{
			int w = SoundSynth.Rate / 100;
			var levels = new List<float>();
			for (int i = 0; i + w <= data.Length; i += w)
			{
				double sum = 0.0;
				for (int j = i; j < i + w; j++)
				{
					sum += data[j] * data[j];
				}
				levels.Add((float)Math.Sqrt(sum / w));
			}
			float peak = 1e-9f;
			foreach (float l in levels)
			{
				peak = Mathf.Max(peak, l);
			}
			float floor = peak * Mathf.Pow(10f, db / 20f);
			int count = 0;
			foreach (float l in levels)
			{
				if (l >= floor)
				{
					count++;
				}
			}
			return count * 0.01f;
		}

		private static float Rms(float[] data)
		{
			double sum = 0.0;
			foreach (float x in data)
			{
				sum += x * x;
			}
			return (float)Math.Sqrt(sum / Math.Max(1, data.Length));
		}

		private static float Crossings(float[] data, int rate = SoundSynth.Rate)
		{
			// Over the loud part only: the tail's hush would weigh the same as the impact.
			float peak = 0f;
			foreach (float x in data)
			{
				peak = Mathf.Max(peak, Mathf.Abs(x));
			}
			int crossings = 0, counted = 0;
			for (int i = 1; i < data.Length; i++)
			{
				if (Mathf.Abs(data[i]) < 0.05f * peak && Mathf.Abs(data[i - 1]) < 0.05f * peak)
				{
					continue;
				}
				counted++;
				if (data[i - 1] < 0f != data[i] < 0f)
				{
					crossings++;
				}
			}
			return counted > 0 ? crossings * (float)rate / counted : 0f;
		}

		private static bool Same(float[] a, float[] b)
		{
			if (a.Length != b.Length)
			{
				return false;
			}
			for (int i = 0; i < a.Length; i++)
			{
				if (a[i] != b[i])
				{
					return false;
				}
			}
			return true;
		}
	}
}
