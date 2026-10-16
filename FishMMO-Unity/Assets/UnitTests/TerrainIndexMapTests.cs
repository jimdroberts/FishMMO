using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using Random = System.Random;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The terrain's index map (TerrainIndexMap, FishTerrainIndexMap.hlsl, FishTerrainIndexBake.shader) against the rule it
	/// replaced: the array shader's per-pixel loop over every control map (FishTopLayers, kept here as <see cref="OldTopLayers"/>).
	/// Pure: no GPU, no assets.
	/// </summary>
	[TestFixture]
	public class TerrainIndexMapTests
	{
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders/";

		// ── The old rule, as the shader ran it ────────────────────────────────

		/// <summary>
		/// FishTopLayers before 2026-10-16: every control map sampled bilinearly at the pixel (texel-centred, clamped), every
		/// layer weighed in index order (a strictly greater weight moves a layer up), the fifth subtracted, normalised;
		/// nothing left draws layer 0.
		/// </summary>
		private static void OldTopLayers(Color32[][] controls, int width, int height, int layers, float u, float v, int[] ids, float[] weights)
		{
			float px = Mathf.Clamp01(u) * (width - 1), py = Mathf.Clamp01(v) * (height - 1);
			int x0 = Math.Min((int)Math.Floor(px), Math.Max(width - 2, 0));
			int y0 = Math.Min((int)Math.Floor(py), Math.Max(height - 2, 0));
			float fx = Mathf.Clamp01(px - x0), fy = Mathf.Clamp01(py - y0);
			int x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1);
			float t0 = 0f, t1 = 0f, t2 = 0f, t3 = 0f, fifth = 0f;
			int i0 = 0, i1 = 0, i2 = 0, i3 = 0;
			for (int l = 0; l < layers; l++)
			{
				float w = (1f - fx) * (1f - fy) * Byte(controls, l, y0 * width + x0) + fx * (1f - fy) * Byte(controls, l, y0 * width + x1)
					+ (1f - fx) * fy * Byte(controls, l, y1 * width + x0) + fx * fy * Byte(controls, l, y1 * width + x1);
				w /= 255f;
				bool b0 = w > t0, b1 = w > t1, b2 = w > t2, b3 = w > t3;
				fifth = b3 ? t3 : Math.Max(fifth, w);
				float o0 = t0, o1 = t1, o2 = t2, o3 = t3;
				int j0 = i0, j1 = i1, j2 = i2, j3 = i3;
				t3 = b2 ? o2 : (b3 ? w : o3); i3 = b2 ? j2 : (b3 ? l : j3);
				t2 = b1 ? o1 : (b2 ? w : o2); i2 = b1 ? j1 : (b2 ? l : j2);
				t1 = b0 ? o0 : (b1 ? w : o1); i1 = b0 ? j0 : (b1 ? l : j1);
				t0 = b0 ? w : o0; i0 = b0 ? l : j0;
			}
			TerrainIndexMap.Normalise(t0, t1, t2, t3, fifth, i0, i1, i2, i3, ids, weights);
		}

		private static int Byte(Color32[][] controls, int layer, int texel)
		{
			Color32 c = controls[layer >> 2][texel];
			return (layer & 3) == 0 ? c.r : (layer & 3) == 1 ? c.g : (layer & 3) == 2 ? c.b : c.a;
		}

		private static void SetByte(Color32[][] controls, int layer, int texel, byte value)
		{
			ref Color32 c = ref controls[layer >> 2][texel];
			switch (layer & 3)
			{
				case 0: c.r = value; break;
				case 1: c.g = value; break;
				case 2: c.b = value; break;
				default: c.a = value; break;
			}
		}

		/// <summary>
		/// Random alphamaps as Unity keeps them: each texel's weights summing to 255, spread over at most
		/// <paramref name="perTexel"/> layers of <paramref name="layers"/> (a few texels unpainted when <paramref name="blanks"/>).
		/// </summary>
		private static Color32[][] RandomControls(Random random, int width, int height, int layers, int perTexel, bool blanks = false, bool clustered = true)
		{
			int maps = (layers + 3) / 4;
			var controls = new Color32[maps][];
			for (int k = 0; k < maps; k++)
			{
				controls[k] = new Color32[width * height];
			}
			// Layers cluster as ground does (a texel's neighbours draw on the same few), so the 2 × 2 merge is exercised.
			int pool = Math.Min(layers, Math.Max(perTexel + 2, 6));
			int poolBase = random.Next(Math.Max(1, layers - pool + 1));
			for (int i = 0; i < width * height; i++)
			{
				if (blanks && random.NextDouble() < 0.05)
				{
					continue;
				}
				int count = 1 + random.Next(Math.Min(perTexel, layers));
				var chosen = new int[count];
				var cuts = new double[count];
				double sum = 0;
				for (int c = 0; c < count; c++)
				{
					int layer;
					do
					{
						layer = clustered ? poolBase + random.Next(pool) : random.Next(layers);
					}
					while (Array.IndexOf(chosen, layer, 0, c) >= 0);
					chosen[c] = layer;
					cuts[c] = random.NextDouble() + (random.NextDouble() < 0.2 ? 1.0 : 0.0);
					sum += cuts[c];
				}
				int left = 255;
				for (int c = 0; c < count; c++)
				{
					int b = c == count - 1 ? left : Math.Min(left, (int)Math.Round(cuts[c] / sum * 255.0));
					left -= b;
					SetByte(controls, chosen[c], i, (byte)b);
				}
			}
			return controls;
		}

		/// <summary>Random per-layer colours: what a blend of layers looks like, to compare two blends as pictures.</summary>
		private static Vector3[] Colours(Random random, int layers)
		{
			var colours = new Vector3[Math.Max(layers, TerrainIndexMap.Slots)];
			for (int l = 0; l < colours.Length; l++)
			{
				colours[l] = new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
			}
			return colours;
		}

		private static Vector3 Blend(int[] ids, float[] weights, Vector3[] colours)
		{
			Vector3 c = Vector3.zero;
			for (int k = 0; k < 4; k++)
			{
				c += colours[ids[k]] * weights[k];
			}
			return c;
		}

		private static int MaxPerTexel(Color32[][] controls, int texel, int layers)
		{
			int n = 0;
			for (int l = 0; l < layers; l++)
			{
				n += Byte(controls, l, texel) > 0 ? 1 : 0;
			}
			return n;
		}

		// ── The bake ──────────────────────────────────────────────────

		[Test]
		public void ATexelOfFourLayersOrFewerIsStoredAsPainted()
		{
			var weights = new byte[12];
			weights[7] = 100;
			weights[2] = 100;
			weights[10] = 55;
			TerrainIndexMap.BakeTexel(weights, 0, 12, out uint ids, out uint packed);
			Assert.That(TerrainIndexMap.IdOf(ids, 0), Is.EqualTo(2), "equal weights: the lower layer first, as the loop met them");
			Assert.That(TerrainIndexMap.IdOf(ids, 1), Is.EqualTo(7));
			Assert.That(TerrainIndexMap.IdOf(ids, 2), Is.EqualTo(10));
			Assert.That(TerrainIndexMap.WeightOf(packed, 0), Is.EqualTo(100));
			Assert.That(TerrainIndexMap.WeightOf(packed, 1), Is.EqualTo(100));
			Assert.That(TerrainIndexMap.WeightOf(packed, 2), Is.EqualTo(55));
			Assert.That(TerrainIndexMap.WeightOf(packed, 3), Is.EqualTo(0));
			Assert.That(TerrainIndexMap.IdOf(ids, 3), Is.EqualTo(0), "the empty slot holds the lowest layer of no weight");
		}

		[Test]
		public void AFifthLayerIsSubtractedFromTheFour()
		{
			byte[] weights = { 10, 90, 40, 60, 25, 30 };
			TerrainIndexMap.BakeTexel(weights, 0, weights.Length, out uint ids, out uint packed);
			int[] expectIds = { 1, 3, 2, 5 };
			int[] expectWeights = { 90 - 25, 60 - 25, 40 - 25, 30 - 25 };
			for (int s = 0; s < 4; s++)
			{
				Assert.That(TerrainIndexMap.IdOf(ids, s), Is.EqualTo(expectIds[s]), $"slot {s}");
				Assert.That(TerrainIndexMap.WeightOf(packed, s), Is.EqualTo(expectWeights[s]), $"slot {s}");
			}
		}

		[Test]
		public void ATexelsIdsAreAlwaysDistinct()
		{
			var random = new Random(4);
			foreach (int layers in new[] { 1, 2, 3, 4, 5, 9, 32, 33, 100, 256 })
			{
				for (int trial = 0; trial < 200; trial++)
				{
					var weights = new byte[layers];
					int nonZero = random.Next(Math.Min(layers, 6) + 1);
					for (int n = 0; n < nonZero; n++)
					{
						weights[random.Next(layers)] = (byte)random.Next(256);
					}
					TerrainIndexMap.BakeTexel(weights, 0, layers, out uint ids, out uint _);
					for (int a = 0; a < 4; a++)
					{
						for (int b = a + 1; b < 4; b++)
						{
							Assert.That(TerrainIndexMap.IdOf(ids, a), Is.Not.EqualTo(TerrainIndexMap.IdOf(ids, b)), $"{layers} layers, trial {trial}");
						}
					}
				}
			}
		}

		[Test]
		public void TheBakeMatchesASortOfEveryLayer()
		{
			var random = new Random(11);
			for (int trial = 0; trial < 2000; trial++)
			{
				int layers = 1 + random.Next(256);
				var weights = new byte[layers];
				for (int n = random.Next(9); n > 0; n--)
				{
					weights[random.Next(layers)] = (byte)random.Next(1, 256);
				}
				TerrainIndexMap.BakeTexel(weights, 0, layers, out uint ids, out uint packed);
				// Heaviest first, the lower layer between equals; the fifth (or 0) taken from each.
				int[] order = new int[Math.Max(layers, 4)];
				for (int i = 0; i < order.Length; i++)
				{
					order[i] = i;
				}
				int W(int l) => l < layers ? weights[l] : 0;
				Array.Sort(order, (a, b) => W(a) != W(b) ? W(b).CompareTo(W(a)) : a.CompareTo(b));
				int fifth = order.Length > 4 ? W(order[4]) : 0;
				for (int s = 0; s < 4; s++)
				{
					Assert.That(TerrainIndexMap.IdOf(ids, s), Is.EqualTo(order[s]));
					Assert.That(TerrainIndexMap.WeightOf(packed, s), Is.EqualTo(W(order[s]) - fifth));
				}
			}
		}

		// ── The filter against the old rule ───────────────────────────────

		/// <summary>
		/// Where every texel holds four layers or fewer — the ground the shader's own comment calls the rule ("a junction of
		/// two biomes is two mains, a cliff and perhaps a detail") — the index map draws exactly what the control maps drew:
		/// the same four layers in the same order with the same weights, at every point, for scenes of up to 32 layers (the
		/// old limit) and past it.
		/// </summary>
		[Test]
		public void UpToFourLayersATexelTheBlendIsTheOldOneExactly()
		{
			var random = new Random(1234);
			var oldIds = new int[4];
			var oldWeights = new float[4];
			var newIds = new int[4];
			var newWeights = new float[4];
			int compared = 0;
			foreach (int layers in new[] { 1, 3, 4, 7, 16, 24, 32, 64, 200, 256 })
			{
				for (int map = 0; map < 6; map++)
				{
					int width = 4 + random.Next(13), height = 4 + random.Next(13);
					Color32[][] controls = RandomControls(random, width, height, layers, perTexel: 4, blanks: map % 3 == 0);
					uint[] index = TerrainIndexMap.Bake(controls, width * height, layers);
					for (int p = 0; p < 400; p++)
					{
						// Texel centres, edges and the tile's own border included.
						float u = p % 10 == 0 ? random.Next(width) / (float)(width - 1) : (float)random.NextDouble() * 1.04f - 0.02f;
						float v = p % 10 == 0 ? random.Next(height) / (float)(height - 1) : (float)random.NextDouble() * 1.04f - 0.02f;
						OldTopLayers(controls, width, height, layers, u, v, oldIds, oldWeights);
						TerrainIndexMap.Filter(index, width, height, u, v, newIds, newWeights);
						for (int k = 0; k < 4; k++)
						{
							Assert.That(newWeights[k], Is.EqualTo(oldWeights[k]).Within(2e-6f), $"{layers} layers, slot {k} at ({u}, {v})");
							// A slot of no weight names whatever was met first; it draws nothing either way.
							if (oldWeights[k] > 1e-5f)
							{
								Assert.That(newIds[k], Is.EqualTo(oldIds[k]), $"{layers} layers, slot {k} at ({u}, {v})");
							}
						}
						compared++;
					}
				}
			}
			Console.WriteLine($"[Index map] {compared} points, at most four layers a texel: identical to the control-map rule.");
		}

		/// <summary>
		/// Ground of one mix over the four texels (a field painted evenly, however many layers): exactly the old blend, five
		/// layers or more included, because each texel already holds the old rule's answer.
		/// </summary>
		[Test]
		public void EvenGroundIsTheOldBlendExactlyHoweverManyLayers()
		{
			var random = new Random(77);
			var oldIds = new int[4];
			var oldWeights = new float[4];
			var newIds = new int[4];
			var newWeights = new float[4];
			for (int trial = 0; trial < 300; trial++)
			{
				int layers = 5 + random.Next(60);
				const int width = 2, height = 2;
				Color32[][] one = RandomControls(random, 1, 1, layers, perTexel: 8, clustered: false);
				var controls = new Color32[one.Length][];
				for (int k = 0; k < one.Length; k++)
				{
					controls[k] = new[] { one[k][0], one[k][0], one[k][0], one[k][0] };
				}
				uint[] index = TerrainIndexMap.Bake(controls, width * height, layers);
				float u = (float)random.NextDouble(), v = (float)random.NextDouble();
				OldTopLayers(controls, width, height, layers, u, v, oldIds, oldWeights);
				TerrainIndexMap.Filter(index, width, height, u, v, newIds, newWeights);
				for (int k = 0; k < 4; k++)
				{
					Assert.That(newWeights[k], Is.EqualTo(oldWeights[k]).Within(2e-6f), $"trial {trial}, slot {k}");
					if (oldWeights[k] > 1e-5f)
					{
						Assert.That(newIds[k], Is.EqualTo(oldIds[k]), $"trial {trial}, slot {k}");
					}
				}
			}
		}

		/// <summary>
		/// At a texel's centre the blend is the old one exactly, whatever the texel holds: the bake keeps the old rule's answer
		/// for every texel, and only between centres, next to a texel of five layers or more, can the two differ.
		/// </summary>
		[Test]
		public void AtEveryTexelCentreTheBlendIsTheOldOne()
		{
			var random = new Random(5);
			var oldIds = new int[4];
			var oldWeights = new float[4];
			var newIds = new int[4];
			var newWeights = new float[4];
			for (int map = 0; map < 20; map++)
			{
				int layers = 5 + random.Next(120);
				int width = 3 + random.Next(9), height = 3 + random.Next(9);
				Color32[][] controls = RandomControls(random, width, height, layers, perTexel: 8);
				uint[] index = TerrainIndexMap.Bake(controls, width * height, layers);
				for (int y = 0; y < height; y++)
				{
					for (int x = 0; x < width; x++)
					{
						float u = x / (float)(width - 1), v = y / (float)(height - 1);
						OldTopLayers(controls, width, height, layers, u, v, oldIds, oldWeights);
						TerrainIndexMap.Filter(index, width, height, u, v, newIds, newWeights);
						for (int k = 0; k < 4; k++)
						{
							Assert.That(newWeights[k], Is.EqualTo(oldWeights[k]).Within(2e-6f), $"texel ({x},{y}) slot {k}");
						}
					}
				}
			}
		}

		/// <summary>
		/// Busy ground (up to eight layers a texel, far more than the cut paints): how far the index map's picture strays from
		/// the control maps' between texel centres, measured on random colours per layer. Held to a loose bound and printed;
		/// both rules are continuous there, they weigh a texel's fifth layer differently (a layer fifth in one texel and in
		/// the four of its neighbour is counted from the neighbour only).
		/// </summary>
		[Test]
		public void BusyGroundStaysCloseToTheOldBlend()
		{
			foreach (int perTexel in new[] { 5, 6, 8 })
			{
				BusyGround(perTexel);
			}
		}

		private static void BusyGround(int perTexel)
		{
			var random = new Random(99 + perTexel);
			var oldIds = new int[4];
			var oldWeights = new float[4];
			var newIds = new int[4];
			var newWeights = new float[4];
			double sum = 0, worst = 0;
			int points = 0, busy = 0;
			for (int map = 0; map < 30; map++)
			{
				int layers = 8 + random.Next(100);
				int width = 8, height = 8;
				Color32[][] controls = RandomControls(random, width, height, layers, perTexel);
				Vector3[] colours = Colours(random, layers);
				uint[] index = TerrainIndexMap.Bake(controls, width * height, layers);
				for (int p = 0; p < 300; p++)
				{
					float u = (float)random.NextDouble(), v = (float)random.NextDouble();
					OldTopLayers(controls, width, height, layers, u, v, oldIds, oldWeights);
					TerrainIndexMap.Filter(index, width, height, u, v, newIds, newWeights);
					double d = (Blend(oldIds, oldWeights, colours) - Blend(newIds, newWeights, colours)).magnitude;
					sum += d;
					worst = Math.Max(worst, d);
					points++;
					int x = Mathf.Clamp(Mathf.RoundToInt(u * (width - 1)), 0, width - 1), y = Mathf.Clamp(Mathf.RoundToInt(v * (height - 1)), 0, height - 1);
					busy += MaxPerTexel(controls, y * width + x, layers) > 4 ? 1 : 0;
				}
			}
			Console.WriteLine($"[Index map] busy ground, up to {perTexel} layers a texel: {points} points ({busy} nearest a texel of 5+ layers), colour difference mean {sum / points:F4}, worst {worst:F4} (colours 0..1 per channel).");
			Assert.That(sum / points, Is.LessThan(0.1), $"up to {perTexel} layers a texel: on average the two pictures are close");
		}

		/// <summary>
		/// No seams: walking across every texel boundary (where the four texels read change), the blend moves only as far as
		/// the step — five layers a texel and more included. The fifth subtracted per texel is what keeps it so.
		/// </summary>
		[Test]
		public void TheBlendIsContinuousAcrossTexelBoundaries()
		{
			var random = new Random(31);
			var aIds = new int[4];
			var aWeights = new float[4];
			var bIds = new int[4];
			var bWeights = new float[4];
			const float step = 2e-5f;
			double worst = 0;
			int crossings = 0;
			for (int map = 0; map < 25; map++)
			{
				int layers = 4 + random.Next(80);
				int width = 6, height = 6;
				Color32[][] controls = RandomControls(random, width, height, layers, perTexel: 7);
				Vector3[] colours = Colours(random, layers);
				uint[] index = TerrainIndexMap.Bake(controls, width * height, layers);
				for (int edge = 1; edge < width - 1; edge++)
				{
					for (int p = 0; p < 20; p++)
					{
						float v = (float)random.NextDouble();
						float u = edge / (float)(width - 1);
						bool across = (p & 1) == 0;
						float ua = across ? u - step : v, va = across ? v : u - step;
						float ub = across ? u + step : v, vb = across ? v : u + step;
						TerrainIndexMap.Filter(index, width, height, ua, va, aIds, aWeights);
						TerrainIndexMap.Filter(index, width, height, ub, vb, bIds, bWeights);
						// Ground the old rule itself cannot hold steady (four layers all but tied with a fifth) is left out.
						if (Total(controls, width, height, layers, ua, va) < 0.05f)
						{
							continue;
						}
						double d = (Blend(aIds, aWeights, colours) - Blend(bIds, bWeights, colours)).magnitude;
						worst = Math.Max(worst, d);
						crossings++;
					}
				}
			}
			Console.WriteLine($"[Index map] {crossings} texel-boundary crossings of {2 * step} texel-widths: worst colour step {worst:E2}.");
			Assert.That(worst, Is.LessThan(0.01), "no jump where the texels change");
		}

		/// <summary>The old rule's weight left after the fifth is subtracted (before normalising): how well conditioned a point is.</summary>
		private static float Total(Color32[][] controls, int width, int height, int layers, float u, float v)
		{
			float px = Mathf.Clamp01(u) * (width - 1), py = Mathf.Clamp01(v) * (height - 1);
			int x0 = Math.Min((int)Math.Floor(px), width - 2), y0 = Math.Min((int)Math.Floor(py), height - 2);
			float fx = px - x0, fy = py - y0;
			var all = new float[layers];
			for (int l = 0; l < layers; l++)
			{
				all[l] = ((1f - fx) * (1f - fy) * Byte(controls, l, y0 * width + x0) + fx * (1f - fy) * Byte(controls, l, y0 * width + x0 + 1)
					+ (1f - fx) * fy * Byte(controls, l, (y0 + 1) * width + x0) + fx * fy * Byte(controls, l, (y0 + 1) * width + x0 + 1)) / 255f;
			}
			Array.Sort(all);
			Array.Reverse(all);
			float fifth = layers > 4 ? all[4] : 0f;
			float total = 0f;
			for (int k = 0; k < Math.Min(4, layers); k++)
			{
				total += Math.Max(0f, all[k] - fifth);
			}
			return total;
		}

		[Test]
		public void AnUnpaintedPointIsLayerZero()
		{
			var controls = new[] { new Color32[16] };
			uint[] index = TerrainIndexMap.Bake(controls, 16, 4);
			var ids = new int[4];
			var weights = new float[4];
			TerrainIndexMap.Filter(index, 4, 4, 0.37f, 0.61f, ids, weights);
			Assert.That(ids, Is.EqualTo(new[] { 0, 0, 0, 0 }));
			Assert.That(weights, Is.EqualTo(new[] { 1f, 0f, 0f, 0f }));
		}

		/// <summary>
		/// The grass's own surface layers (GrassMath.PackSurfaceLayers: the two strongest less the third, from the alphamaps)
		/// are what the index texel says: the blades' root colour and the ground under them choose by one rule, however many
		/// layers the scene has.
		/// </summary>
		[Test]
		public void TheGrassSurfaceLayersAgreeWithTheIndexTexel()
		{
			var random = new Random(8);
			for (int trial = 0; trial < 3000; trial++)
			{
				int layers = 1 + random.Next(256);
				var bytes = new byte[layers];
				for (int n = 1 + random.Next(7); n > 0; n--)
				{
					bytes[random.Next(layers)] = (byte)random.Next(1, 256);
				}
				var floats = new float[layers];
				for (int l = 0; l < layers; l++)
				{
					floats[l] = bytes[l] / 255f;
				}
				uint grass = FishMMO.Client.GrassMath.PackSurfaceLayers(floats, layers);
				TerrainIndexMap.BakeTexel(bytes, 0, layers, out uint ids, out uint packed);
				int s0 = TerrainIndexMap.WeightOf(packed, 0), s1 = TerrainIndexMap.WeightOf(packed, 1), s2 = TerrainIndexMap.WeightOf(packed, 2);
				Assert.That((int)(grass & 255u), Is.EqualTo(TerrainIndexMap.IdOf(ids, 0)), $"trial {trial}: strongest");
				if (s1 > s2)
				{
					Assert.That((int)(grass >> 8 & 255u), Is.EqualTo(TerrainIndexMap.IdOf(ids, 1)), $"trial {trial}: second");
				}
				float a = s0 - s2, b = s1 - s2;
				int share = a + b > 1e-5f ? Mathf.RoundToInt(a / (a + b) * 255f) : 255;
				Assert.That((int)(grass >> 16 & 255u), Is.EqualTo(share).Within(1), $"trial {trial}: the first's share");
			}
		}

		// ── The layer table ───────────────────────────────────────────

		[Test]
		public void TheLayerTableHoldsEachLayerInItsColumn()
		{
			const int count = 3;
			TerrainArrayLayerParams.Allocate(TerrainArrayLayerParams.TableLength(count), out Vector4[] st, out Vector4[] tint, out Vector4[] offset, out Vector4[] scale, out Vector4[] surface);
			var flora = new Vector4[st.Length];
			for (int l = 0; l < count; l++)
			{
				var p = TerrainArrayLayerParams.Default;
				p.TileSize = new Vector2(4f + l, 9f);
				p.DiffuseRemapMax = new Vector4(0.5f + 0.1f * l, 0.6f, 0.7f, 1f);
				p.NormalScale = 0.25f * (l + 1);
				p.MaskRemapMin = new Vector4(0.1f * l, 0f, 0f, 0f);
				p.HasMask = l == 1;
				p.Pack(l, st, tint, offset, scale, surface);
				flora[l] = new Vector4(0.2f * l, 0.1f, 0.3f, 0f);
			}
			float[] earth = { 5f, -1f, 7f };
			float[] stone = { 9f };
			Vector4[] table = TerrainLayerTable.Pack(count, st, tint, offset, scale, surface, flora, earth, null, stone);

			Assert.That(table.Length, Is.EqualTo(TerrainLayerTable.Width * TerrainLayerTable.Rows));
			Vector4 At(int layer, int row) => table[row * TerrainLayerTable.Width + layer];
			for (int l = 0; l < count; l++)
			{
				Assert.That(At(l, TerrainLayerTable.RowST), Is.EqualTo(st[l]), $"layer {l} tiling");
				Assert.That(At(l, TerrainLayerTable.RowTint), Is.EqualTo(tint[l]), $"layer {l} tint and normal scale");
				Assert.That(At(l, TerrainLayerTable.RowMaskOffset), Is.EqualTo(offset[l]));
				Assert.That(At(l, TerrainLayerTable.RowMaskScale), Is.EqualTo(scale[l]));
				Assert.That(At(l, TerrainLayerTable.RowSurface), Is.EqualTo(surface[l]));
				Assert.That(At(l, TerrainLayerTable.RowFlora), Is.EqualTo(flora[l]));
			}
			Assert.That(At(0, TerrainLayerTable.RowPaths), Is.EqualTo(new Vector4(5f, -1f, 9f, -1f)));
			Assert.That(At(2, TerrainLayerTable.RowPaths), Is.EqualTo(new Vector4(7f, -1f, -1f, -1f)), "short maps are -1 past their end");
			TerrainArrayLayerParams.Allocate(1, out Vector4[] neutral, out _, out _, out _, out Vector4[] neutralSurface);
			for (int l = count; l < TerrainLayerTable.Width; l++)
			{
				Assert.That(At(l, TerrainLayerTable.RowST), Is.EqualTo(neutral[0]), "past the scene's layers: a neutral layer, as the arrays' unused slots");
				Assert.That(At(l, TerrainLayerTable.RowSurface), Is.EqualTo(neutralSurface[0]));
				Assert.That(At(l, TerrainLayerTable.RowFlora), Is.EqualTo(Vector4.zero));
				Assert.That(At(l, TerrainLayerTable.RowPaths), Is.EqualTo(new Vector4(-1f, -1f, -1f, -1f)));
			}
			for (int l = 0; l < TerrainLayerTable.Width; l++)
			{
				Assert.That(At(l, TerrainLayerTable.RowBackdrop), Is.EqualTo(new Vector4(l, 0f, 0f, 0f)), "no backdrop channels named: channel c is layer c, as before");
			}
			int[] channels = { 3, 40, 41, 200 };
			Vector4[] remapped = TerrainLayerTable.Pack(count, st, tint, offset, scale, surface, flora, earth, null, stone, channels);
			for (int c = 0; c < channels.Length; c++)
			{
				Assert.That(remapped[TerrainLayerTable.RowBackdrop * TerrainLayerTable.Width + c].x, Is.EqualTo(channels[c]));
			}
			Assert.That(remapped[TerrainLayerTable.RowBackdrop * TerrainLayerTable.Width + 4].x, Is.EqualTo(4f), "past the named channels: itself");
		}

		/// <summary>
		/// A backdrop of a scene past 32 layers gives its 32 channels to the layers carrying the most weight past the edge, in
		/// layer order; a scene of 32 or fewer keeps channel c = layer c (null).
		/// </summary>
		[Test]
		public void ABackdropOfManyLayersKeepsItsHeaviestThirtyTwo()
		{
			Assert.That(SceneBackdropBuilder.ChannelLayersFor(new float[32], 32), Is.Null);
			var totals = new float[100];
			for (int l = 0; l < totals.Length; l++)
			{
				totals[l] = l % 3 == 0 ? 10f + l : 1f;
			}
			totals[50] = totals[51];   // a tie: the lower layer is kept first
			int[] chosen = SceneBackdropBuilder.ChannelLayersFor(totals, totals.Length);
			Assert.That(chosen.Length, Is.EqualTo(32));
			for (int c = 1; c < chosen.Length; c++)
			{
				Assert.That(chosen[c], Is.GreaterThan(chosen[c - 1]), "in layer order");
			}
			int[] heaviest = new int[100];
			for (int l = 0; l < 100; l++)
			{
				heaviest[l] = l;
			}
			Array.Sort(heaviest, (a, b) => totals[a] != totals[b] ? totals[b].CompareTo(totals[a]) : a.CompareTo(b));
			for (int k = 0; k < 32; k++)
			{
				Assert.That(chosen, Does.Contain(heaviest[k]));
			}
		}

		[Test]
		public void TheShadersReadTheTableRowsTheCodeWrites()
		{
			string table = File.ReadAllText(Shaders + "FishTerrainIndexMap.hlsl");
			Assert.That(table, Does.Contain($"#define FISH_LAYER_ROW_ST {TerrainLayerTable.RowST} "));
			Assert.That(table, Does.Contain($"#define FISH_LAYER_ROW_TINT {TerrainLayerTable.RowTint} "));
			Assert.That(table, Does.Contain($"#define FISH_LAYER_ROW_MASK_OFFSET {TerrainLayerTable.RowMaskOffset} "));
			Assert.That(table, Does.Contain($"#define FISH_LAYER_ROW_MASK_SCALE {TerrainLayerTable.RowMaskScale}\n").Or.Contain($"#define FISH_LAYER_ROW_MASK_SCALE {TerrainLayerTable.RowMaskScale}\r\n"));
			Assert.That(table, Does.Contain($"#define FISH_LAYER_ROW_SURFACE {TerrainLayerTable.RowSurface} "));
			Assert.That(table, Does.Contain($"#define FISH_LAYER_ROW_FLORA {TerrainLayerTable.RowFlora} "));
			Assert.That(table, Does.Contain($"#define FISH_LAYER_ROW_PATHS {TerrainLayerTable.RowPaths} "));
			Assert.That(table, Does.Contain($"#define FISH_LAYER_ROW_BACKDROP {TerrainLayerTable.RowBackdrop} "));
		}

		// ── Caps and agreement ────────────────────────────────────────

		[Test]
		public void EveryLayerCapIsTheIndexMaps()
		{
			Assert.That(TerrainIndexMap.MaximumLayers, Is.EqualTo(256), "a byte per id");
			Assert.That(TerrainArrayLayerParams.MaximumLayers, Is.EqualTo(TerrainIndexMap.MaximumLayers));
			Assert.That(SceneTerrainPalette.MaximumLayers, Is.EqualTo(TerrainIndexMap.MaximumLayers));
			Assert.That(TerrainLayerTable.Width, Is.EqualTo(TerrainIndexMap.MaximumLayers));
			Assert.That(FishMMO.Client.GroundMaterialMap.MaxLayers, Is.EqualTo(TerrainIndexMap.MaximumLayers));
			Assert.That(FishMMO.Client.GrassAlbedoTexels.HeaderUints, Is.EqualTo(TerrainIndexMap.MaximumLayers * 8));

			string input = File.ReadAllText(Shaders + "FishWeatherTerrainArrayInput.hlsl");
			Assert.That(input, Does.Contain($"#define FISH_TERRAIN_ARRAY_MAX_LAYERS {TerrainIndexMap.MaximumLayers}"));
			string ground = File.ReadAllText(Shaders + "FishGroundColour.hlsl");
			Assert.That(ground, Does.Contain($"#define FISH_GROUND_MAX_LAYERS {TerrainIndexMap.MaximumLayers} "));
			string grass = File.ReadAllText(Shaders + "FishGrassBlades.compute");
			Assert.That(grass, Does.Contain($"#define GRASS_ALBEDO_HEADER {FishMMO.Client.GrassAlbedoTexels.HeaderUints}u"));
		}

		[Test]
		public void ASetOfThirtyTwoLayersOrFewerKeepsItsLength()
		{
			Assert.That(TerrainArrayLayerParams.TableLength(1), Is.EqualTo(32));
			Assert.That(TerrainArrayLayerParams.TableLength(32), Is.EqualTo(32), "a set baked before the index map is the same set");
			Assert.That(TerrainArrayLayerParams.TableLength(33), Is.EqualTo(33));
			Assert.That(TerrainArrayLayerParams.TableLength(256), Is.EqualTo(256));
			Assert.That(TerrainArrayLayerParams.TableLength(400), Is.EqualTo(256));
			TerrainArrayLayerParams.Allocate(out Vector4[] legacy, out _, out _, out _, out _);
			Assert.That(legacy.Length, Is.EqualTo(TerrainArrayLayerParams.LegacyTableLength));
			TerrainArrayLayerParams.Allocate(140, out Vector4[] wide, out _, out _, out _, out _);
			Assert.That(wide.Length, Is.EqualTo(140));
			Assert.That(wide[139], Is.EqualTo(legacy[0]), "every slot neutral");
		}

		[Test]
		public void AFlipbookOfAnyLayerCountIsAtMostTheArrayDepthEveryApiHolds()
		{
			foreach (int size in new[] { 128, 256, 512, 1024 })
			{
				for (int count = 1; count <= TerrainArrayLayerParams.MaximumLayers; count++)
				{
					TerrainArrayBaker.Layout(count, size, out int columns, out int rows);
					Assert.That(columns * rows, Is.LessThanOrEqualTo(256), $"{count} at {size}: slices incl. padding (WebGPU/Vulkan/GLES guarantee 256)");
				}
			}
		}

		/// <summary>
		/// The arrays cost the same however many layers a scene has: up to 64 at 1024 (every scene today bakes as before), past
		/// that at 512, and never a flipbook past 16384 × 4096.
		/// </summary>
		[Test]
		public void ManyLayersBakeSmallerSlicesWithinOneBudget()
		{
			Assert.That(TerrainArrayBaker.SliceSizeFor(1024, 32), Is.EqualTo(1024), "32 layers: as before");
			Assert.That(TerrainArrayBaker.SliceSizeFor(1024, 64), Is.EqualTo(1024));
			Assert.That(TerrainArrayBaker.SliceSizeFor(1024, 65), Is.EqualTo(512));
			Assert.That(TerrainArrayBaker.SliceSizeFor(1024, 256), Is.EqualTo(512));
			Assert.That(TerrainArrayBaker.SliceSizeFor(300, 256), Is.EqualTo(512), "sources under the budget keep their size");
			Assert.That(TerrainArrayBaker.SliceSizeFor(8, 256), Is.EqualTo(TerrainArrayBaker.MinimumSliceSize));
			for (int count = 1; count <= TerrainArrayLayerParams.MaximumLayers; count++)
			{
				int size = TerrainArrayBaker.SliceSizeFor(4096, count);
				Assert.That((long)count * size * size, Is.LessThanOrEqualTo(TerrainArrayBaker.MaximumFlipbookTexels), $"{count} layers at {size}");
				TerrainArrayBaker.Layout(count, size, out int columns, out int rows);
				Assert.That(columns * size, Is.LessThanOrEqualTo(TerrainArrayBaker.MaximumImageEdge));
				Assert.That(rows * size, Is.LessThanOrEqualTo(TerrainArrayBaker.MaximumImageEdge));
			}
		}

		/// <summary>The terrain shaders read the index map and the table, and no per-layer uniform array is left to vary per draw.</summary>
		[Test]
		public void NoPerLayerUniformArraysAreLeft()
		{
			string input = File.ReadAllText(Shaders + "FishWeatherTerrainArrayInput.hlsl");
			foreach (string gone in new[] { "_FishLayerST[", "_FishLayerTint[", "_FishLayerMaskOffset[", "_FishLayerMaskScale[", "_FishLayerSurface[", "_FishLayerFlora[", "_FishPathEarthFor", "_FishPathRoadFor", "_FishPathStoneFor" })
			{
				Assert.That(input, Does.Not.Contain(gone), gone);
			}
			Assert.That(input, Does.Contain("Texture2D<uint2> _FishIndexMap;"));
			Assert.That(input, Does.Contain("Texture2D<float4> _FishLayerTable;"));
			string ground = File.ReadAllText(Shaders + "FishGroundColour.hlsl");
			Assert.That(ground, Does.Not.Contain("_FishGroundLayerST[").And.Not.Contain("_FishGroundControl"));
			Assert.That(ground, Does.Contain("Texture2DArray<uint2> _FishGroundIndex;"));

			string binder = File.ReadAllText("Assets/Scripts/Shared/Implementation/Entity/WorldSceneDetails/Terrain/TerrainArrayBinder.cs");
			Assert.That(binder, Does.Not.Contain("SetVectorArray"), "OpenGL: no large constant arrays varied per tile");
			string contact = File.ReadAllText("Assets/Scripts/Client/World/Terrain/GroundMaterialMap.cs");
			Assert.That(contact, Does.Not.Contain("SetGlobalVectorArray(Layer"));
		}

		/// <summary>The GPU bake is the C# bake: same tie rule, same empty slots, same packing (the editor also checks texels).</summary>
		[Test]
		public void TheBakeShaderIsTheCSharpBake()
		{
			string bake = File.ReadAllText(Shaders + "FishTerrainIndexBake.shader");
			Assert.That(bake, Does.Contain("Shader \"" + TerrainIndexMapBaker.ShaderName + "\""));
			Assert.That(bake, Does.Contain("HLSLPROGRAM\n            #pragma use_dxc vulkan").Or.Contain("HLSLPROGRAM\r\n            #pragma use_dxc vulkan"));
			Assert.That(bake, Does.Contain("float4 top = -1.0;"), "empty slots below every weight: zero-weight layers fill them in order");
			Assert.That(bake, Does.Contain("bool b0 = weight > top.x;"), "strictly greater: the lower layer stays ahead of an equal");
			Assert.That(bake, Does.Contain("uint4 w = (uint4)(top - max(fifth, 0.0));"));
			Assert.That(bake, Does.Contain("n.x | (n.y << 8) | (n.z << 16) | (n.w << 24)"));
			Assert.That(bake, Does.Contain("uint2 Frag(Varyings input) : SV_Target"));
			Assert.That(TerrainIndexMapBaker.Format, Is.EqualTo(UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32_UInt));
		}
	}
}
