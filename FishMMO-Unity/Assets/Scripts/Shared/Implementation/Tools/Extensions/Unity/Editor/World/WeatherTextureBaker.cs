#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Generates the weather textures from a seed: the precipitation atlas and a tileable noise
	/// texture. Same seed, same pixels.
	/// </summary>
	/// <remarks>
	/// The atlas is 512 × 1024: four 128-pixel tiles across and eight rows down. Row 0 is rain
	/// streaks, 1 snowflakes, 2 hailstones, 3 ash and embers, 4 sand grit; rows 5–7 are spare.
	/// Tiles are white with the shape in alpha, so a tint colours them — all but the hailstones,
	/// whose RGB carries the ice itself: milky where it is full of air, clear at its rim, lit.
	/// </remarks>
	public static class WeatherTextureBaker
	{
		public const string Folder = "Assets/Prefabs/Client/Weather/Textures";
		public const string AtlasPath = Folder + "/Precipitation Atlas.png";
		public const string NoisePath = Folder + "/Weather Noise.png";
		public const int Tile = 128;
		public const int Columns = 4;
		public const int Rows = 8;
		public const int DefaultSeed = 238;

		[DashboardTool(DashboardToolAttribute.Weather, "Bake weather textures", Section = "Content", Order = 1,
			Tooltip = "Writes the precipitation atlas and the weather noise texture into " + Folder + " from the default seed.")]
		public static void BakeFromDashboard()
		{
			Bake(Folder, DefaultSeed);
			Debug.Log($"[Weather textures] Wrote {AtlasPath} and {NoisePath}.");
		}

		/// <summary>Writes both textures into a folder and imports them.</summary>
		public static void Bake(string folder, int seed)
		{
			WorldEditorAssets.EnsureFolder(folder);
			string atlas = folder + "/Precipitation Atlas.png";
			string noise = folder + "/Weather Noise.png";
			File.WriteAllBytes(atlas, EncodeAndDestroy(BuildAtlas(seed)));
			File.WriteAllBytes(noise, EncodeAndDestroy(BuildNoise(seed, 256)));
			AssetDatabase.ImportAsset(atlas, ImportAssetOptions.ForceUpdate);
			AssetDatabase.ImportAsset(noise, ImportAssetOptions.ForceUpdate);
			Configure(atlas, TextureWrapMode.Clamp, true, true);
			Configure(noise, TextureWrapMode.Repeat, false, false);
		}

		private static byte[] EncodeAndDestroy(Texture2D texture)
		{
			byte[] png = texture.EncodeToPNG();
			UnityEngine.Object.DestroyImmediate(texture);
			return png;
		}

		private static void Configure(string path, TextureWrapMode wrap, bool alpha, bool sRGB)
		{
			if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
			{
				return;
			}
			importer.textureType = TextureImporterType.Default;
			importer.wrapMode = wrap;
			importer.alphaIsTransparency = alpha;
			importer.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
			importer.sRGBTexture = sRGB;
			importer.mipmapEnabled = true;
			importer.npotScale = TextureImporterNPOTScale.None;
			importer.textureCompression = TextureImporterCompression.CompressedHQ;
			importer.SaveAndReimport();
			WorldEditorAssets.RegisterAddressable(AssetDatabase.LoadAssetAtPath<Texture2D>(path), WorldEditorAssets.ClientStaticGroup);
		}

		// ── Atlas ──

		public static Texture2D BuildAtlas(int seed)
		{
			int width = Tile * Columns, height = Tile * Rows;
			var pixels = new Color32[width * height];
			for (int i = 0; i < pixels.Length; i++)
			{
				pixels[i] = new Color32(255, 255, 255, 0);
			}
			var random = new System.Random(seed);
			for (int column = 0; column < Columns; column++)
			{
				DrawTile(pixels, width, height, 0, column, (u, v) => Streak(u, v), random);
				int flakeSeed = random.Next();
				DrawTile(pixels, width, height, 1, column, (u, v) => Flake(u, v, flakeSeed), random);
				// Drawn and not used: the hailstones are the column's own now, but every row after
				// this one is seeded from the same stream, and skipping the draw would change them all.
				random.NextDouble();
				int stone = column;
				DrawColourTile(pixels, width, height, 2, column, (u, v) => Hail(u, v, stone));
				int blobSeed = random.Next();
				DrawTile(pixels, width, height, 3, column, (u, v) => Blob(u, v, blobSeed), random);
				int gritSeed = random.Next();
				DrawTile(pixels, width, height, 4, column, (u, v) => Grit(u, v, gritSeed), random);
			}
			var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
			texture.SetPixels32(pixels);
			texture.Apply();
			return texture;
		}

		/// <summary>Fills one tile. Row 0 is at the top of the image; texture rows count from the bottom.</summary>
		private static void DrawTile(Color32[] pixels, int width, int height, int row, int column, Func<float, float, float> alpha, System.Random random)
		{
			int top = height - (row + 1) * Tile;
			const int margin = 2;
			for (int y = margin; y < Tile - margin; y++)
			{
				for (int x = margin; x < Tile - margin; x++)
				{
					float u = (x + 0.5f) / Tile, v = (y + 0.5f) / Tile;
					float a = Mathf.Clamp01(alpha(u, v));
					pixels[(top + y) * width + column * Tile + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
				}
			}
		}

		/// <summary>Fills one tile with colour as well as shape.</summary>
		private static void DrawColourTile(Color32[] pixels, int width, int height, int row, int column, Func<float, float, Color> colour)
		{
			int top = height - (row + 1) * Tile;
			const int margin = 2;
			for (int y = margin; y < Tile - margin; y++)
			{
				for (int x = margin; x < Tile - margin; x++)
				{
					float u = (x + 0.5f) / Tile, v = (y + 0.5f) / Tile;
					Color c = colour(u, v);
					pixels[(top + y) * width + column * Tile + x] = new Color32(
						(byte)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f),
						(byte)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f),
						(byte)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f),
						(byte)Mathf.RoundToInt(Mathf.Clamp01(c.a) * 255f));
				}
			}
		}

		/// <summary>A thin vertical line that tapers at both ends.</summary>
		public static float Streak(float u, float v)
		{
			float across = Mathf.Exp(-Mathf.Pow((u - 0.5f) / 0.06f, 2f));
			float along = Mathf.Pow(Mathf.Sin(Mathf.PI * v), 0.6f) * Mathf.Lerp(0.55f, 1f, v);
			return across * along;
		}

		/// <summary>A six-armed flake with side branches and a soft core.</summary>
		public static float Flake(float u, float v, int seed)
		{
			var random = new System.Random(seed);
			float armLength = 0.3f + (float)random.NextDouble() * 0.12f;
			float branchAt = 0.35f + (float)random.NextDouble() * 0.3f;
			float branchLength = 0.08f + (float)random.NextDouble() * 0.08f;
			float twist = (float)random.NextDouble() * Mathf.PI / 3f;

			float x = u - 0.5f, y = v - 0.5f;
			float r = Mathf.Sqrt(x * x + y * y);
			float angle = Mathf.Atan2(y, x) + twist;
			float sector = Mathf.Repeat(angle, Mathf.PI / 3f) - Mathf.PI / 6f;
			float along = r * Mathf.Cos(sector);
			float offArm = Mathf.Abs(r * Mathf.Sin(sector));
			float arm = along <= armLength ? Mathf.Exp(-Mathf.Pow(offArm / 0.012f, 2f)) : 0f;
			// Branches leave the arm at 60°.
			float bx = along - armLength * branchAt;
			float branchDistance = Mathf.Abs(offArm - bx * Mathf.Tan(Mathf.PI / 3f));
			float branch = bx > 0f && bx < branchLength ? Mathf.Exp(-Mathf.Pow(branchDistance / 0.012f, 2f)) : 0f;
			float core = Mathf.Exp(-Mathf.Pow(r / 0.05f, 2f));
			float halo = Mathf.Exp(-Mathf.Pow(r / 0.25f, 2f)) * 0.15f;
			return Mathf.Max(Mathf.Max(arm, branch), Mathf.Max(core, halo));
		}

		/// <summary>
		/// A hailstone as it looks: an ice ball grown in shells, milky where the water froze fast
		/// and trapped its air, clear where it froze slowly, a rim of clear ice outside, lit from
		/// above with a glint. Four stones across the row.
		/// </summary>
		/// <remarks>
		/// <list type="number">
		/// <item>A pea: small, nearly round, two shells.</item>
		/// <item>An oblate stone, a little flattened, three shells.</item>
		/// <item>A big stone grown lumpy: knobs where it collided with and froze onto others.</item>
		/// <item>Graupel: a small rounded cone of rime, opaque and matt all through.</item>
		/// </list>
		/// RGB is the ice and alpha its outline and clarity. The stone is lit as a sphere from the
		/// upper left and in front, so a camera-facing sprite reads as a ball at any size.
		/// </remarks>
		public static Color Hail(float u, float v, int stone)
		{
			float x = u - 0.5f, y = v - 0.5f;
			float r = Mathf.Sqrt(x * x + y * y);
			float theta = Mathf.Atan2(y, x);
			int kind = ((stone % 4) + 4) % 4;
			bool graupel = kind == 3;

			// The outline: an ellipse, turned, roughened by the stone's own lumps.
			float rx, ry, turn, lumps, shells;
			switch (kind)
			{
				case 0:
					rx = 0.22f; ry = 0.21f; turn = 0.3f; shells = 2f;
					lumps = 0.02f * Mathf.Sin(3f * theta + 0.4f) + 0.015f * Mathf.Sin(7f * theta + 1.1f);
					break;
				case 1:
					rx = 0.31f; ry = 0.23f; turn = 0.35f; shells = 3f;
					lumps = 0.025f * Mathf.Sin(4f * theta + 2f) + 0.015f * Mathf.Sin(9f * theta);
					break;
				case 2:
					rx = 0.31f; ry = 0.29f; turn = -0.5f; shells = 4f;
					// Rounded knobs, not points: what froze on is a droplet or a smaller stone.
					lumps = 0.035f * Mathf.Sin(5f * theta + 0.7f) + 0.025f * Mathf.Sin(8f * theta + 2.3f)
						+ 0.07f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(7f * theta + 1f)), 3f);
					break;
				default:
					// A rounded cone, its point up, its surface rough with rime.
					rx = 0.18f; ry = 0.18f; turn = 0f; shells = 0f;
					float up = Mathf.Max(0f, Mathf.Sin(theta));
					lumps = 0.55f * Mathf.Pow(up, 5f) + 0.03f * Mathf.Sin(13f * theta + 0.5f) + 0.02f * Mathf.Sin(21f * theta);
					break;
			}
			float c = Mathf.Cos(theta - turn), s = Mathf.Sin(theta - turn);
			float ellipse = rx * ry / Mathf.Sqrt(ry * ry * c * c + rx * rx * s * s);
			float radius = ellipse * (1f + lumps);
			float t = r / Mathf.Max(1e-4f, radius);
			if (t >= 1.05f)
			{
				return new Color(1f, 1f, 1f, 0f);
			}
			// A pixel's worth of soft edge, so it is not jagged at any mip.
			float edge = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.95f, 1.03f, t));

			// Lit as a ball: the normal from where on the disc the point is.
			float nx = x / Mathf.Max(1e-4f, radius), ny = y / Mathf.Max(1e-4f, radius);
			float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Min(1f, t) * Mathf.Min(1f, t)));
			var normal = new Vector3(nx, ny, nz).normalized;
			var toLight = new Vector3(-0.45f, 0.55f, 0.70f).normalized;
			float diffuse = Mathf.Max(0f, Vector3.Dot(normal, toLight));
			Vector3 halfway = (toLight + Vector3.forward).normalized;
			float glint = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(normal, halfway)), graupel ? 8f : 60f) * (graupel ? 0.12f : 0.9f);

			// The shells: milky where it froze fast, clear between; and outside them a clear rim.
			// Grown round the stone as a whole, so they follow its bulk and not every knob.
			float inner = Mathf.Min(1f, r / Mathf.Max(1e-4f, ellipse * (1f + 0.3f * lumps)));
			float shell = shells > 0f ? 0.5f + 0.5f * Mathf.Cos(inner * Mathf.PI * shells) : 1f;
			float rim = graupel ? 0f : Mathf.InverseLerp(0.8f, 1f, t);
			float clear = graupel ? 0f : Mathf.Clamp01((1f - shell) * 0.35f + rim);
			var milk = new Color(0.94f, 0.96f, 0.98f);
			var ice = new Color(0.70f, 0.78f, 0.86f);
			Color body = Color.Lerp(milk, ice, clear) * (0.62f + 0.38f * diffuse);
			// Light that went through the clear ice shows as a faint bright rim on the far side.
			float through = graupel ? 0f : Mathf.Pow(Mathf.Min(1f, t), 6f) * Mathf.Max(0f, -(nx * toLight.x + ny * toLight.y)) * 0.15f;
			float opacity = graupel ? 1f : Mathf.Lerp(Mathf.Lerp(0.88f, 1f, shell), 0.55f, rim);
			return new Color(body.r + glint + through, body.g + glint + through, body.b + glint + through, edge * opacity);
		}

		/// <summary>A ragged soft blob for ash and embers.</summary>
		public static float Blob(float u, float v, int seed)
		{
			float x = u - 0.5f, y = v - 0.5f;
			float r = Mathf.Sqrt(x * x + y * y);
			float angle = Mathf.Atan2(y, x);
			float s = seed % 1000 * 0.013f;
			float radius = 0.22f + 0.07f * Mathf.Sin(angle * 3f + s) + 0.04f * Mathf.Sin(angle * 7f + s * 2.1f);
			return Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(radius * 0.4f, radius, r)) * 0.95f;
		}

		/// <summary>A few small specks.</summary>
		public static float Grit(float u, float v, int seed)
		{
			var random = new System.Random(seed);
			int count = 3 + random.Next(5);
			float best = 0f;
			for (int i = 0; i < count; i++)
			{
				float cx = 0.2f + (float)random.NextDouble() * 0.6f;
				float cy = 0.2f + (float)random.NextDouble() * 0.6f;
				float size = 0.03f + (float)random.NextDouble() * 0.05f;
				float d = Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy));
				best = Mathf.Max(best, Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(size * 0.5f, size, d)));
			}
			return best;
		}

		// ── Noise ──

		/// <summary>Tileable value-noise fBm: R, G and B at base frequencies 4, 8 and 16.</summary>
		public static Texture2D BuildNoise(int seed, int size)
		{
			var pixels = new Color32[size * size];
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float u = x / (float)size, v = y / (float)size;
					byte r = (byte)Mathf.RoundToInt(Fbm(u, v, 4, seed) * 255f);
					byte g = (byte)Mathf.RoundToInt(Fbm(u, v, 8, seed + 17) * 255f);
					byte b = (byte)Mathf.RoundToInt(Fbm(u, v, 16, seed + 31) * 255f);
					pixels[y * size + x] = new Color32(r, g, b, 255);
				}
			}
			var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
			texture.SetPixels32(pixels);
			texture.Apply();
			return texture;
		}

		/// <summary>Four octaves of periodic value noise, 0..1, tiling at u, v = 1.</summary>
		public static float Fbm(float u, float v, int frequency, int seed)
		{
			float sum = 0f, weight = 0.5f, total = 0f;
			for (int octave = 0; octave < 4; octave++)
			{
				int period = frequency << octave;
				sum += weight * ValueNoise(u * period, v * period, period, seed + octave * 101);
				total += weight;
				weight *= 0.5f;
			}
			return Mathf.Clamp01(sum / total);
		}

		private static float ValueNoise(float x, float y, int period, int seed)
		{
			int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
			float fx = x - x0, fy = y - y0;
			fx = fx * fx * (3f - 2f * fx);
			fy = fy * fy * (3f - 2f * fy);
			float a = Hash(x0, y0, period, seed), b = Hash(x0 + 1, y0, period, seed);
			float c = Hash(x0, y0 + 1, period, seed), d = Hash(x0 + 1, y0 + 1, period, seed);
			return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
		}

		private static float Hash(int x, int y, int period, int seed)
		{
			x = ((x % period) + period) % period;
			y = ((y % period) + period) % period;
			unchecked
			{
				uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144665);
				h = (h ^ (h >> 13)) * 1274126177u;
				h ^= h >> 16;
				return (h & 0xffffff) / (float)0xffffff;
			}
		}
	}
}
#endif
