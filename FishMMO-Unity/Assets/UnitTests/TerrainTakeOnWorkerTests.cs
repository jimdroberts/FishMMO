using System;
using System.Collections.Generic;
using FishMMO.Client;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The terrain take-on builders' arithmetic moved off the main thread (GrassTerrain.Builder, DetailScatterTerrain.Builder,
	/// TerrainBuildQueue) gives bit for bit what the main thread gave: each test runs the old main-thread code (copied here as
	/// it was) and the new worker path over the same synthetic layers and compares every float by its bits. Then the queue's
	/// own contract (order kept, a fault stops it, a cancel skips the rest), and source pins for the per-group append counts
	/// and the rocks' shadow caster culling.
	/// </summary>
	public class TerrainTakeOnWorkerTests
	{
		private const int StripRows = 64;

		/// <summary>A synthetic detail layer: mostly empty, the rest 1..24 (the scatter's point-like painting).</summary>
		private static int[,] Layer(int res, int seed)
		{
			var random = new System.Random(seed);
			var cells = new int[res, res];
			for (int z = 0; z < res; z++)
			{
				for (int x = 0; x < res; x++)
				{
					cells[z, x] = random.NextDouble() < 0.18 ? random.Next(1, 25) : 0;
				}
			}
			return cells;
		}

		private static int[,] Strip(int[,] layer, int row, int rows, int res)
		{
			var strip = new int[rows, res];
			for (int z = 0; z < rows; z++)
			{
				for (int x = 0; x < res; x++)
				{
					strip[z, x] = layer[row + z, x];
				}
			}
			return strip;
		}

		private static void SameBits(float[] expected, float[] actual, string what)
		{
			LogAssert.AreEqual(expected.Length, actual.Length, $"{what}: length");
			for (int i = 0; i < expected.Length; i++)
			{
				if (BitConverter.SingleToInt32Bits(expected[i]) != BitConverter.SingleToInt32Bits(actual[i]))
				{
					LogAssert.Fail($"{what}: texel {i} was {expected[i]:R}, now {actual[i]:R}");
				}
			}
		}

		// ── The grass's density maps ─────────────────────────────────

		[Test]
		public void GrassDensity_BuiltOnWorkers_IsBitForBitTheMainThreadBuild()
		{
			const int res = 160, factor = 2, count = 3;
			int dres = (res + factor - 1) / factor;
			const float top = 16f;
			int[] layerChannels = { 0, 1, 0, 2, 1 };
			float[] heights = { 0.4f, 1.1f, 0.25f };
			var layers = new int[layerChannels.Length][,];
			for (int l = 0; l < layers.Length; l++)
			{
				layers[l] = Layer(res, 1000 + l);
			}
			int radius = 1;

			// The main thread's build, as it was: strips summed in order, the channels smoothed strip by strip, normalised, packed.
			float[][] old = NewChannels(count, dres);
			bool oldAny = false;
			for (int l = 0; l < layers.Length; l++)
			{
				for (int row = 0; row < res; row += StripRows)
				{
					int rows = Mathf.Min(StripRows, res - row);
					int[,] cells = Strip(layers[l], row, rows, res);
					float[] target = old[layerChannels[l]];
					float blockWeight = 1f / (factor * factor);
					for (int z = 0; z < rows; z++)
					{
						for (int x = 0; x < res; x++)
						{
							int v = cells[z, x];
							if (v > 0)
							{
								oldAny = true;
								target[((row + z) / factor) * dres + x / factor] += Mathf.Min(1f, v / top) * blockWeight;
							}
						}
					}
				}
			}
			var oldSums = new float[count][];
			for (int c = 0; c < count; c++)
			{
				oldSums[c] = (float[])old[c].Clone();
				OldSmooth(old[c], dres, radius);
			}
			for (int c = 0; c < count; c++)
			{
				GrassTerrain.Builder.Normalise(old[c]);
			}
			OldPackAndGrids(old, count, dres, heights, out Color32[][] oldMaps, out byte[] oldOccupied, out float[] oldCover, out byte[] oldSward);

			// The workers' build: the strips through the queue, then every channel at once.
			float[][] fresh = NewChannels(count, dres);
			bool any = false;
			var queue = new TerrainBuildQueue();
			for (int l = 0; l < layers.Length; l++)
			{
				for (int row = 0; row < res; row += StripRows)
				{
					int rows = Mathf.Min(StripRows, res - row);
					int[,] cells = Strip(layers[l], row, rows, res);
					float[] target = fresh[layerChannels[l]];
					int rowBase = row;
					queue.Enqueue(() =>
					{
						if (GrassTerrain.Builder.AddStrip(cells, rows, rowBase, res, factor, dres, top, target))
						{
							any = true;
						}
					});
				}
			}
			queue.Drain();
			LogAssert.IsNull(queue.Fault, "the queue faulted");
			LogAssert.IsTrue(queue.Idle, "the queue is not idle after draining");
			LogAssert.AreEqual(oldAny, any, "whether any grass was painted");
			// The sums on their own first (a fault there is easier to read before smoothing mixes the texels).
			for (int c = 0; c < count; c++)
			{
				SameBits(oldSums[c], fresh[c], $"channel {c} summed");
			}
			// Every channel a point channel, as the old build normalised them all.
			var points = new bool[count];
			Array.Fill(points, true);
			GrassTerrain.Builder.DensityResult built = GrassTerrain.Builder.BuildDensity(fresh, count, dres, radius, heights, points, true);

			for (int c = 0; c < count; c++)
			{
				SameBits(old[c], fresh[c], $"channel {c} smoothed and normalised");
			}
			LogAssert.AreEqual(oldMaps.Length, built.Maps.Length, "density maps");
			for (int m = 0; m < oldMaps.Length; m++)
			{
				for (int i = 0; i < oldMaps[m].Length; i++)
				{
					if (!oldMaps[m][i].Equals(built.Maps[m][i]))
					{
						LogAssert.Fail($"map {m} texel {i}: {oldMaps[m][i]} then, {built.Maps[m][i]} now");
					}
				}
			}
			SameBytes(oldOccupied, built.Occupied, "occupied");
			SameBytes(oldSward, built.Sward, "sward");
			SameBits(oldCover, built.Cover, "cover");
		}

		private static void SameBytes(byte[] expected, byte[] actual, string what)
		{
			LogAssert.AreEqual(expected.Length, actual.Length, $"{what}: length");
			for (int i = 0; i < expected.Length; i++)
			{
				if (expected[i] != actual[i])
				{
					LogAssert.Fail($"{what}: texel {i} was {expected[i]}, now {actual[i]}");
				}
			}
		}

		private static float[][] NewChannels(int count, int dres)
		{
			var channels = new float[count][];
			for (int c = 0; c < count; c++)
			{
				channels[c] = new float[dres * dres];
			}
			return channels;
		}

		/// <summary>The old GrassTerrain.Builder.SmoothStrip, run strip by strip over the four passes as Step ran it.</summary>
		private static void OldSmooth(float[] target, int dres, int radius)
		{
			var line = new float[dres];
			for (int pass = 0; pass < 4; pass++)
			{
				bool horizontal = (pass & 1) == 0;
				for (int smoothLine = 0; smoothLine < dres; smoothLine += StripRows)
				{
					int lines = Mathf.Min(StripRows, dres - smoothLine);
					for (int l = smoothLine; l < smoothLine + lines; l++)
					{
						for (int i = 0; i < dres; i++)
						{
							line[i] = target[horizontal ? l * dres + i : i * dres + l];
						}
						float sum = 0f;
						int n = 0;
						for (int i = 0; i < Mathf.Min(radius, dres); i++)
						{
							sum += line[i];
							n++;
						}
						for (int i = 0; i < dres; i++)
						{
							int add = i + radius, drop = i - radius - 1;
							if (add < dres)
							{
								sum += line[add];
								n++;
							}
							if (drop >= 0)
							{
								sum -= line[drop];
								n--;
							}
							target[horizontal ? l * dres + i : i * dres + l] = Mathf.Max(0f, sum / n);
						}
					}
				}
			}
		}

		/// <summary>The old Advance's texture step: maps packed, occupied, cover and sward, as it was.</summary>
		private static void OldPackAndGrids(float[][] density, int count, int dres, float[] channelHeights, out Color32[][] maps, out byte[] occupied, out float[] cover, out byte[] sward)
		{
			int mapCount = Mathf.Clamp((count + 3) / 4, 1, GrassTerrain.MaxDensityMaps);
			maps = new Color32[mapCount][];
			occupied = new byte[dres * dres];
			for (int m = 0; m < mapCount; m++)
			{
				int first = 4 * m;
				var pixels = new Color32[dres * dres];
				for (int i = 0; i < pixels.Length; i++)
				{
					byte B(int c) => first + c < count ? (byte)Mathf.Clamp(Mathf.RoundToInt(density[first + c][i] * 255f), 0, 255) : (byte)0;
					pixels[i] = new Color32(B(0), B(1), B(2), B(3));
				}
				maps[m] = pixels;
				for (int i = 0; i < occupied.Length; i++)
				{
					Color32 a = pixels[i];
					if (a.r > 1 || a.g > 1 || a.b > 1 || a.a > 1)
					{
						occupied[i] = 1;
					}
				}
			}
			cover = new float[dres * dres];
			for (int c = 0; c < count; c++)
			{
				float[] d = density[c];
				float h = channelHeights[c];
				for (int i = 0; i < cover.Length; i++)
				{
					cover[i] = Mathf.Max(cover[i], d[i] * h);
				}
			}
			sward = new byte[dres * dres];
			for (int i = 0; i < sward.Length; i++)
			{
				float sum = 0f;
				for (int c = 0; c < count; c++)
				{
					sum += density[c][i];
				}
				sward[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(sum) * 255f);
			}
		}

		[Test]
		public void GrassSurfaceLayers_PackedOnWorkers_AreTheMainThreadsPacking()
		{
			const int ares = 70, alayers = 5;
			var random = new System.Random(7);
			var maps = new float[ares, ares, alayers];
			for (int z = 0; z < ares; z++)
			{
				for (int x = 0; x < ares; x++)
				{
					for (int l = 0; l < alayers; l++)
					{
						maps[z, x, l] = (float)random.NextDouble() * (random.NextDouble() < 0.4 ? 1f : 0.05f);
					}
				}
			}
			var old = new Color32[ares * ares];
			var weights = new float[alayers];
			for (int z = 0; z < ares; z++)
			{
				for (int x = 0; x < ares; x++)
				{
					for (int l = 0; l < alayers; l++)
					{
						weights[l] = maps[z, x, l];
					}
					uint packed = GrassMath.PackSurfaceLayers(weights, alayers);
					old[z * ares + x] = new Color32((byte)packed, (byte)(packed >> 8), (byte)(packed >> 16), (byte)(packed >> 24));
				}
			}
			var fresh = new Color32[ares * ares];
			var queue = new TerrainBuildQueue();
			for (int row = 0; row < ares; row += StripRows)
			{
				int rows = Mathf.Min(StripRows, ares - row), rowBase = row;
				var strip = new float[rows, ares, alayers];
				for (int z = 0; z < rows; z++)
				{
					for (int x = 0; x < ares; x++)
					{
						for (int l = 0; l < alayers; l++)
						{
							strip[z, x, l] = maps[row + z, x, l];
						}
					}
				}
				queue.Enqueue(() => GrassTerrain.Builder.PackSurfaceStrip(strip, rows, rowBase, ares, alayers, fresh, new float[alayers]));
			}
			queue.Drain();
			for (int i = 0; i < old.Length; i++)
			{
				if (!old[i].Equals(fresh[i]))
				{
					LogAssert.Fail($"texel {i}: {old[i]} then, {fresh[i]} now");
				}
			}
		}

		// ── The detail scatter's block counts ────────────────────────

		[Test]
		public void ScatterBlocks_SummedOnWorkers_AreTheMainThreadsSums()
		{
			const int res = 150, factor = 2, blocks = (res + factor - 1) / factor;
			const float cellArea = 0.25f, density = 0.85f;
			foreach (bool coverageMode in new[] { false, true })
			{
				int[,] layer = Layer(res, coverageMode ? 31 : 37);
				var old = new float[blocks * blocks];
				double oldTotal = 0.0;
				bool oldAny = false;
				for (int row = 0; row < res; row += StripRows)
				{
					int rows = Mathf.Min(StripRows, res - row);
					int[,] cells = Strip(layer, row, rows, res);
					double total = 0.0;
					for (int z = 0; z < rows; z++)
					{
						int blockRow = (row + z) / factor * blocks;
						for (int x = 0; x < res; x++)
						{
							int v = cells[z, x];
							if (v <= 0)
							{
								continue;
							}
							float e = TerrainDetailMath.ExpectedInCell(v, coverageMode, 3.5f, cellArea, density);
							if (e > 0f)
							{
								old[blockRow + x / factor] += e;
								total += e;
								oldAny = true;
							}
						}
					}
					oldTotal += total;
				}

				var fresh = new float[blocks * blocks];
				double freshTotal = 0.0;
				bool any = false;
				var queue = new TerrainBuildQueue();
				for (int row = 0; row < res; row += StripRows)
				{
					int rows = Mathf.Min(StripRows, res - row), rowBase = row;
					int[,] cells = Strip(layer, row, rows, res);
					queue.Enqueue(() =>
					{
						bool found = false;
						freshTotal += DetailScatterTerrain.Builder.AddStrip(cells, rows, rowBase, res, factor, blocks, coverageMode, 3.5f, cellArea, density, fresh, ref found);
						any |= found;
					});
				}
				queue.Drain();
				LogAssert.AreEqual(oldAny, any, $"coverage {coverageMode}: whether anything counted");
				LogAssert.AreEqual(BitConverter.DoubleToInt64Bits(oldTotal), BitConverter.DoubleToInt64Bits(freshTotal), $"coverage {coverageMode}: the prototype's total {oldTotal:R} then, {freshTotal:R} now");
				SameBits(old, fresh, $"coverage {coverageMode}: blocks");
			}
		}

		// ── The queue ────────────────────────────────────────────────

		[Test]
		public void BuildQueue_RunsInOrder_StopsAtAFault_AndSkipsAfterCancel()
		{
			var queue = new TerrainBuildQueue();
			var order = new List<int>();
			for (int i = 0; i < 200; i++)
			{
				int n = i;
				queue.Enqueue(() => order.Add(n));
			}
			queue.Drain();
			LogAssert.AreEqual(200, order.Count, "every piece ran");
			for (int i = 0; i < order.Count; i++)
			{
				LogAssert.AreEqual(i, order[i], "the pieces ran in the order queued");
			}

			var faulty = new TerrainBuildQueue();
			bool after = false;
			faulty.Enqueue(() => throw new InvalidOperationException("strip"));
			faulty.Enqueue(() => after = true);
			faulty.Drain();
			LogAssert.IsTrue(faulty.Fault is InvalidOperationException, "the fault is kept");
			LogAssert.IsFalse(after, "a piece after the fault ran");
			LogAssert.IsTrue(faulty.Idle, "a faulted queue still drains");

			if (TerrainBuildQueue.Inline)
			{
				return;
			}
			// A cancel while a piece runs: the pieces after it are skipped.
			var cancelled = new TerrainBuildQueue();
			bool ran = false;
			var gate = new System.Threading.ManualResetEventSlim(false);
			cancelled.Enqueue(() => gate.Wait(5000));
			cancelled.Enqueue(() => ran = true);
			cancelled.Cancel();
			gate.Set();
			cancelled.Drain();
			LogAssert.IsFalse(ran, "a piece queued before the cancel ran after it");
		}

		// ── Source pins ──────────────────────────────────────────────

		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders/";

		[Test]
		public void TheAppends_AreCountedPerThreadGroup()
		{
			string grass = SourceScanPins.ReadCode(Shaders + "FishGrassBlades.compute");
			SourceScanPins.HoldsAndFires("grass generate", grass,
				s => SourceScanPins.InOrder(SourceScanPins.Body(s, "void FishGrassGenerate("), "InterlockedAdd(gsGrassCount[", "_GrassCounts.InterlockedAdd(", "GrassStore("),
				SourceScanPins.Replace("_GrassCounts.InterlockedAdd(thread.x * 4u, n, first);", "first = 0u;"),
				"the group's run never taken from the slot's counter");
			string cull = SourceScanPins.ReadCode(Shaders + "FishTerrainInstancing.compute");
			SourceScanPins.HoldsAndFires("instancing cull", cull,
				s => SourceScanPins.InOrder(SourceScanPins.Body(s, "void FishCull("), "InterlockedAdd(gsFishCount[", "_FishCounts.InterlockedAdd(", "FishStore("),
				SourceScanPins.Replace("_FishCounts.InterlockedAdd((slotBase + thread.x) * 4, n, first);", "first = 0;"),
				"the group's run never taken from the slot's counter");
		}

		[Test]
		public void TheRocksShadowCaster_CullsCascadesLikeTheTrees()
		{
			string wrappers = SourceScanPins.ReadCode(Shaders + "FishWeatherLitFadeWrappers.hlsl");
			SourceScanPins.HoldsAndFires("weather lit caster", wrappers,
				s => s.Contains("#if defined(FISHMMO_INDIRECT_INSTANCING_INCLUDED) && !defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)")
					? SourceScanPins.InOrder(SourceScanPins.Body(s, "Varyings FishShadowPassVertex("), "UNITY_SETUP_INSTANCE_ID(input);", "FishCasterCulled()", "return ShadowPassVertex(input);")
					: "the culling is no longer limited to the indirect copy's directional casters",
				SourceScanPins.Replace("if (FishCasterCulled())", "if (false)"),
				"the caster never asks whether it is culled");
			foreach (string shader in new[] { "FishWeatherLit.shader", "FishWeatherLitIndirect.shader" })
			{
				LogAssert.IsTrue(SourceScanPins.ReadSource(Shaders + shader).Contains("#pragma vertex FishShadowPassVertex"), $"{shader}: the shadow caster's vertex is not the wrapper");
			}
			LogAssert.IsTrue(SourceScanPins.ReadCode(Shaders + "FishVegetationPasses.hlsl").Contains("return FishCasterCulled();"), "the vegetation no longer shares the caster rule");
			string scatter = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Terrain/Scatter/DetailScatterRenderer.cs");
			LogAssert.IsTrue(scatter.Contains("SetVector(TerrainGpuRenderer.CasterSphereId"), "the scatter's draws carry no caster sphere: bushes are drawn into every cascade");
		}
	}
}
