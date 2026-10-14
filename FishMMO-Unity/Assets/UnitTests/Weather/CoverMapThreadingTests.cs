using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Client;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The ground cover map works its storm-reached texels out on worker threads (<see cref="WeatherCoverMap.IntegrateTexel"/>),
	/// and the ground is the server's to the bit: so the threaded sums must be the serial ones exactly, and both the map's
	/// arithmetic as it ran on the main thread, step by step against the forcing cache. And the window moved in place
	/// (<see cref="WeatherCoverMap.Shift{T}"/>) must hold what the new arrays it replaced held.
	/// </summary>
	public class CoverMapThreadingTests
	{
		/// <summary>Half an hour past a generation's start, so the old generation's texels run its whole length.</summary>
		private const double Moment = GroundCover.GenerationSeconds * 120.0 + 1800.0;
		private const int Resolution = WeatherCoverMap.DefaultResolution;
		private const float Size = WeatherCoverMap.DefaultSizeMeters;

		/// <summary>A wet, unsettled scene with a storm someone started sitting over the map for hours, and the scene's own.</summary>
		private static WeatherTimeline Wet()
		{
			var wet = new AirOffsets { Humidity = 0.6f, Instability = 0.5f, Temperature = -2f };
			var timeline = new WeatherTimeline
			{
				SceneName = "Ground",
				Seed = 1234,
				SceneMode = WeatherSceneMode.Own,
				Air = new AirOffsetEntry { From = wet, To = wet, StartSeconds = 0, EndSeconds = 0 },
				Area = new Rect(-5000f, -5000f, 10000f, 10000f),
				Director = true,
				LatitudeDegrees = 40f,
			};
			timeline.Cells.Add(StormSchedule.NewCell(1, 99u, StormKind.Thunderstorm, Vector2.zero, 2500f, 0f, new Vector2(0.2f, 0.1f),
				4f * 3600f, Moment - 3.0 * 3600.0));
			return timeline;
		}

		/// <summary>The shared (storm-free) track from a generation's start to <paramref name="last"/>, as the map keeps it.</summary>
		private static List<CoverTrack> SharedTrack(WeatherTimeline timeline, long start, long last, DeepSnow deep)
		{
			var history = new List<CoverTrack>();
			CoverTrack track = GroundCover.Prior(GroundCover.ForcingAt(timeline, null, default(Scene), start));
			history.Add(track);
			for (long k = start + 1; k <= last; k++)
			{
				GroundCover.Forcing f = GroundCover.ForcingAt(timeline, null, default(Scene), k);
				track = GroundCover.Advance(track, GroundCover.OpenFrame(f), f, deep);
				history.Add(track);
			}
			return history;
		}

		/// <summary>The map's own arithmetic for one generation of one texel, as it ran on the main thread.</summary>
		private static void Reference(WeatherTimeline timeline, List<CoverTrack> history, long start, long step, long firstTouch,
			Vector3 position, DeepSnow deep, out CoverTrack at, out CoverTrack next)
		{
			long from = System.Math.Max(firstTouch - 1, start);
			CoverTrack track = history[(int)(from - start)];
			at = track;
			next = track;
			for (long k = from + 1; k <= step + 1; k++)
			{
				GroundCover.Forcing f = GroundCover.ForcingAt(timeline, null, default(Scene), k);
				track = GroundCover.Advance(track, GroundCover.FrameAt(f, position), f, deep);
				if (k == step)
				{
					at = track;
				}
				else if (k == step + 1)
				{
					next = track;
				}
			}
		}

		[Test]
		public void TheThreadedTexels_AreTheSerialOnes_AndTheMainThreadsToTheBit()
		{
			WeatherTimeline timeline = Wet();
			var deep = new DeepSnow { MaxMetres = 1f, Hours = 4f };
			long step = GroundCover.StepOf(Moment);
			long generation = GroundCover.GenerationOf(Moment);
			long youngStart = GroundCover.StartStep(generation);
			long oldStart = youngStart - GroundCover.StepsPerGeneration;
			List<CoverTrack> older = SharedTrack(timeline, oldStart, step + 1, deep);
			List<CoverTrack> younger = SharedTrack(timeline, youngStart, step + 1, deep);

			float texel = Size / Resolution;
			var origin = new Vector2Int(Mathf.RoundToInt(-Size * 0.5f / texel), Mathf.RoundToInt(-Size * 0.5f / texel));
			int n = Resolution * Resolution;
			var firstTouch = new long[n];
			var chosen = new List<int>();
			// A spread of texels over the window, each first reached at its own step of the ground's memory.
			for (int i = 0; i < n; i += 23)
			{
				chosen.Add(i);
				firstTouch[i] = oldStart + 1 + (i * 37L) % (step + 1 - oldStart);
			}
			int[] indices = chosen.ToArray();

			var forcing = new GroundCover.Forcing[0];
			long first = oldStart + 1;
			WeatherCoverMap.FetchForcing(timeline, null, default(Scene), first, step + 1, ref forcing);
			var work = new WeatherCoverMap.CoverWork
			{
				Step = step,
				Generation = generation,
				OldHistory = older,
				YoungHistory = younger,
				Forcing = forcing,
				ForcingBase = first,
				Deep = deep,
				Origin = origin,
				Resolution = Resolution,
				TexelMeters = texel,
			};

			var serial = new CoverTrack[4][];
			var threaded = new CoverTrack[4][];
			for (int t = 0; t < 4; t++)
			{
				serial[t] = new CoverTrack[n];
				threaded[t] = new CoverTrack[n];
			}
			WeatherCoverMap.IntegrateTexels(work, indices, indices.Length, firstTouch, serial[0], serial[1], serial[2], serial[3], false);
			WeatherCoverMap.IntegrateTexels(work, indices, indices.Length, firstTouch, threaded[0], threaded[1], threaded[2], threaded[3], true);

			int differ = 0, fromMainThread = 0, stormy = 0;
			string example = null;
			foreach (int i in indices)
			{
				int x = i % Resolution, y = i / Resolution;
				// The map's TexelCentre.
				var position = new Vector3((origin.x + x + 0.5f) * texel, 0f, (origin.y + y + 0.5f) * texel);
				Reference(timeline, older, oldStart, step, firstTouch[i], position, deep, out CoverTrack oldAt, out CoverTrack oldNext);
				Reference(timeline, younger, youngStart, step, firstTouch[i], position, deep, out CoverTrack youngAt, out CoverTrack youngNext);
				CoverTrack[] expected = { oldAt, oldNext, youngAt, youngNext };
				for (int t = 0; t < 4; t++)
				{
					if (!threaded[t][i].SameAs(serial[t][i]))
					{
						differ++;
						example ??= $"texel {i} track {t}: threaded wet {threaded[t][i].Cover.Wet:R} serial {serial[t][i].Cover.Wet:R}";
					}
					if (!serial[t][i].SameAs(expected[t]))
					{
						fromMainThread++;
						example ??= $"texel {i} track {t}: serial wet {serial[t][i].Cover.Wet:R} main thread {expected[t].Cover.Wet:R}";
					}
				}
				if (!oldNext.SameAs(older[(int)(step + 1 - oldStart)]))
				{
					stormy++;
				}
			}
			LogAssert.AreEqual(0, differ, example ?? "threaded and serial agree");
			LogAssert.AreEqual(0, fromMainThread, example ?? "and both agree with the main thread's arithmetic");
			// A control: the storm must have made some of these texels other than the shared ground, or nothing was tested.
			LogAssert.IsTrue(stormy > 0, $"{stormy} of {indices.Length} texels differ from the open air's ground");
		}

		/// <summary>
		/// The weather of each step made up here, without a scene: an unsettled, near-freezing air whose temperature and sun
		/// swing step to step, and two storms crossing the window — so the threaded sums can be run anywhere, a plain
		/// runner included (the test above needs the engine's scene queries).
		/// </summary>
		private static Dictionary<long, GroundCover.Forcing> MadeUpWeather(long first, long last)
		{
			var steps = new Dictionary<long, GroundCover.Forcing>();
			var air = new WeatherDriver.Synoptic { Pressure = 0.35f, Humidity = 0.85f, Temperature = -1f, Instability = 0.6f };
			PlanetAir planet = PlanetAir.Earthlike;
			var cells = new List<StormCell>
			{
				StormSchedule.NewCell(1, 7u, StormKind.Thunderstorm, new Vector2(-600f, 200f), 1800f, 0f, new Vector2(0.3f, -0.1f),
					6f * 3600f, first * GroundCover.StepSeconds - 600.0),
				StormSchedule.NewCell(2, 8u, StormKind.SquallLine, new Vector2(900f, -700f), 1400f, 0f, new Vector2(-0.2f, 0.25f),
					6f * 3600f, first * GroundCover.StepSeconds - 1200.0),
			};
			for (long k = first; k <= last; k++)
			{
				float temperature = -0.15f + 0.25f * Mathf.Sin(k * 0.05f);
				var background = new WeatherFrame();
				background[WeatherChannel.Precipitation] = 0.25f + 0.2f * Mathf.Sin(k * 0.11f);
				background[WeatherChannel.RainWeight] = 0.6f;
				background[WeatherChannel.SnowWeight] = 0.4f;
				background[WeatherChannel.CloudCover] = 0.8f;
				var sample = new WeatherSample
				{
					Frame = background,
					Background = background,
					Temperature = temperature,
					Air = air,
					OpenAir = air,
					Planet = planet,
					OpenColumn = AirColumn.Of(planet, 273f + temperature * 10f, air.Humidity, air.Pressure, air.Instability),
				};
				var f = new GroundCover.Forcing
				{
					Step = k,
					Seconds = k * GroundCover.StepSeconds,
					Around = sample,
					Temperature = temperature,
					Sunlight = (k / 30) % 2 == 0 ? 1f : 0f,
					HasWeather = true,
					Storms = new StormFrames(sample),
				};
				f.Cells.AddRange(cells);
				steps[k] = f;
			}
			return steps;
		}

		[Test]
		public void TheThreadedTexels_AreTheSerialOnes_UnderMadeUpWeather()
		{
			var deep = new DeepSnow { MaxMetres = 1f, Hours = 4f };
			long generation = 400;
			long youngStart = GroundCover.StartStep(generation);
			long oldStart = youngStart - GroundCover.StepsPerGeneration;
			long step = youngStart + 40;
			Dictionary<long, GroundCover.Forcing> weather = MadeUpWeather(oldStart, step + 1);

			List<CoverTrack> Shared(long start)
			{
				var history = new List<CoverTrack>();
				CoverTrack track = GroundCover.Prior(weather[start]);
				history.Add(track);
				for (long k = start + 1; k <= step + 1; k++)
				{
					track = GroundCover.Advance(track, GroundCover.OpenFrame(weather[k]), weather[k], deep);
					history.Add(track);
				}
				return history;
			}
			List<CoverTrack> older = Shared(oldStart);
			List<CoverTrack> younger = Shared(youngStart);

			float texel = Size / Resolution;
			var origin = new Vector2Int(-48, -48);
			int n = Resolution * Resolution;
			var firstTouch = new long[n];
			var chosen = new List<int>();
			for (int i = 0; i < n; i += 7)
			{
				chosen.Add(i);
				firstTouch[i] = oldStart + 1 + (i * 37L) % (step + 1 - oldStart);
			}
			int[] indices = chosen.ToArray();

			// Fetched as the map fetches them: the storms' own weather worked out before any worker reads it.
			long first = oldStart + 1;
			var forcing = new GroundCover.Forcing[step + 2 - first];
			for (long k = first; k <= step + 1; k++)
			{
				WeatherCoverMap.WarmStorms(weather[k]);
				forcing[k - first] = weather[k];
			}
			var work = new WeatherCoverMap.CoverWork
			{
				Step = step,
				Generation = generation,
				OldHistory = older,
				YoungHistory = younger,
				Forcing = forcing,
				ForcingBase = first,
				Deep = deep,
				Origin = origin,
				Resolution = Resolution,
				TexelMeters = texel,
			};
			var serial = new CoverTrack[4][];
			var threaded = new CoverTrack[4][];
			for (int t = 0; t < 4; t++)
			{
				serial[t] = new CoverTrack[n];
				threaded[t] = new CoverTrack[n];
			}
			WeatherCoverMap.IntegrateTexels(work, indices, indices.Length, firstTouch, serial[0], serial[1], serial[2], serial[3], false);
			// Twice threaded: the workers take the texels in a different order each time.
			for (int run = 0; run < 2; run++)
			{
				WeatherCoverMap.IntegrateTexels(work, indices, indices.Length, firstTouch, threaded[0], threaded[1], threaded[2], threaded[3], true);
				int differ = 0;
				string example = null;
				foreach (int i in indices)
				{
					for (int t = 0; t < 4; t++)
					{
						if (!threaded[t][i].SameAs(serial[t][i]))
						{
							differ++;
							example ??= $"texel {i} track {t}: threaded wet {threaded[t][i].Cover.Wet:R} snow {threaded[t][i].Cover.Snow:R}, " +
								$"serial wet {serial[t][i].Cover.Wet:R} snow {serial[t][i].Cover.Snow:R}";
						}
					}
				}
				LogAssert.AreEqual(0, differ, example ?? $"run {run}: threaded and serial agree");
			}

			int fromMainThread = 0, stormy = 0;
			string first2 = null;
			foreach (int i in indices)
			{
				int x = i % Resolution, y = i / Resolution;
				var position = new Vector3((origin.x + x + 0.5f) * texel, 0f, (origin.y + y + 0.5f) * texel);
				CoverTrack[] expected = new CoverTrack[4];
				for (int g = 0; g < 2; g++)
				{
					long start = g == 0 ? oldStart : youngStart;
					List<CoverTrack> history = g == 0 ? older : younger;
					long from = System.Math.Max(firstTouch[i] - 1, start);
					CoverTrack track = history[(int)(from - start)];
					CoverTrack at = track, next = track;
					for (long k = from + 1; k <= step + 1; k++)
					{
						track = GroundCover.Advance(track, GroundCover.FrameAt(weather[k], position), weather[k], deep);
						if (k == step) at = track;
						else if (k == step + 1) next = track;
					}
					expected[g * 2] = at;
					expected[g * 2 + 1] = next;
				}
				for (int t = 0; t < 4; t++)
				{
					if (!serial[t][i].SameAs(expected[t]))
					{
						fromMainThread++;
						first2 ??= $"texel {i} track {t}";
					}
				}
				if (!expected[1].SameAs(older[(int)(step + 1 - oldStart)]))
				{
					stormy++;
				}
			}
			LogAssert.AreEqual(0, fromMainThread, first2 ?? "the batch's sums are the map's own, step by step");
			LogAssert.IsTrue(stormy > 0, $"{stormy} of {indices.Length} texels differ from the open air's ground");
		}

		/// <summary>The window's move as it was: into new arrays.</summary>
		private static int[] MovedIntoNew(int[] cells, int n, int shiftX, int shiftY, int fill)
		{
			var moved = new int[n * n];
			for (int y = 0; y < n; y++)
			{
				for (int x = 0; x < n; x++)
				{
					int sx = x + shiftX, sy = y + shiftY;
					moved[y * n + x] = sx >= 0 && sy >= 0 && sx < n && sy < n ? cells[sy * n + sx] : fill;
				}
			}
			return moved;
		}

		[Test]
		public void TheWindowMovedInPlace_HoldsWhatTheNewArraysHeld()
		{
			const int n = 12;
			int checkedShifts = 0;
			for (int shiftY = -n; shiftY <= n; shiftY++)
			{
				for (int shiftX = -n; shiftX <= n; shiftX++)
				{
					var cells = new int[n * n];
					for (int i = 0; i < cells.Length; i++)
					{
						cells[i] = i + 1;
					}
					int[] expected = MovedIntoNew(cells, n, shiftX, shiftY, -1);
					WeatherCoverMap.Shift(cells, n, shiftX, shiftY, -1);
					for (int i = 0; i < cells.Length; i++)
					{
						if (cells[i] != expected[i])
						{
							Assert.Fail($"shift ({shiftX}, {shiftY}) cell {i}: {cells[i]} where the new array held {expected[i]}");
						}
					}
					checkedShifts++;
				}
			}
			LogAssert.AreEqual((2 * n + 1) * (2 * n + 1), checkedShifts);
		}
	}
}
