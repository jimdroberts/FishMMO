using UnityEngine;
using FishMMO.Shared.Celestial;

namespace FishMMO.Client
{
	/// <summary>
	/// Generates the star field: a cubemap of a few thousand stars with real-looking brightness
	/// and colour spreads, from a seed. Built on each client, in the solar system's own fixed frame;
	/// the sky turns it for the body and the place it is seen from. rgb is the light; alpha is each
	/// star's own twinkle phase (see <see cref="Splat"/>).
	/// </summary>
	/// <remarks>
	/// Every client builds its own copy, so the same seed has to give the same stars on every one
	/// of them, on every platform, for good. The numbers come from the project's own hash and not
	/// from <see cref="System.Random"/>: that one happens to agree between Mono and IL2CPP today,
	/// but what a seed produces is nowhere promised, and a sky that two players are meant to be able
	/// to point at together is not the place to rely on a habit. The stars are drawn in the same
	/// order whatever the size, so a lower quality tier is the same sky at a coarser resolution.
	/// </remarks>
	public static class StarfieldBuilder
	{
		public const int DefaultStars = 8000;

		/// <summary>A black-body-ish colour for a temperature in kelvin.</summary>
		public static Color ColorOf(float kelvin) => StarBody.TemperatureColor(kelvin);

		/// <summary>The cubemap face and pixel a direction lands on.</summary>
		public static void FaceOf(Vector3 d, int size, out CubemapFace face, out float x, out float y)
		{
			Vector3 a = new Vector3(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z));
			float sc, tc, ma;
			if (a.x >= a.y && a.x >= a.z)
			{
				ma = a.x;
				if (d.x > 0f) { face = CubemapFace.PositiveX; sc = -d.z; tc = -d.y; }
				else { face = CubemapFace.NegativeX; sc = d.z; tc = -d.y; }
			}
			else if (a.y >= a.z)
			{
				ma = a.y;
				if (d.y > 0f) { face = CubemapFace.PositiveY; sc = d.x; tc = d.z; }
				else { face = CubemapFace.NegativeY; sc = d.x; tc = -d.z; }
			}
			else
			{
				ma = a.z;
				if (d.z > 0f) { face = CubemapFace.PositiveZ; sc = d.x; tc = -d.y; }
				else { face = CubemapFace.NegativeZ; sc = -d.x; tc = -d.y; }
			}
			x = (sc / ma + 1f) * 0.5f * size;
			y = (tc / ma + 1f) * 0.5f * size;
		}

		/// <summary>The n-th number, 0..1, of the sequence a seed gives. Stateless, so it cannot drift.</summary>
		private static float Draw(uint seed, uint index) => SkySchedule.Unit(SkySchedule.Hash(seed ^ 0x57A25EEDu, index));

		/// <summary>
		/// A star's twinkle phase, 0..1. From a sequence of its own, so adding it moved no star: the
		/// four numbers a star's place and colour come from are where they always were.
		/// </summary>
		private static float Phase(uint seed, int star) => SkySchedule.Unit(SkySchedule.Hash(seed ^ 0x7D1A5C3Bu, (uint)star));

		/// <param name="keepReadable">
		/// Keep the CPU-side copy of the pixels. False in the game, where the starfield is uploaded
		/// once and never read back, and the copy is pure waste — a 1024 cubemap is 24 MB of it. True
		/// for anything that needs to look at the result: a test checking that a seed is
		/// reproducible, or an editor tool previewing a sky.
		/// </param>
		public static Cubemap Build(int size, uint seed, int stars = DefaultStars, bool keepReadable = false)
		{
			size = Mathf.ClosestPowerOfTwo(Mathf.Clamp(size, 64, 2048));
			var faces = new Color32[6][];
			for (int f = 0; f < 6; f++)
			{
				faces[f] = new Color32[size * size];
				for (int i = 0; i < faces[f].Length; i++)
				{
					faces[f][i] = new Color32(0, 0, 0, 0);
				}
			}
			for (int s = 0; s < stars; s++)
			{
				// Four numbers to a star, by its own index: a star's place and colour do not depend on
				// how many came before it, so changing the count adds stars without moving any.
				uint at = (uint)s * 4u;
				float z = Draw(seed, at) * 2f - 1f;
				float angle = Draw(seed, at + 1u) * Mathf.PI * 2f;
				float r = Mathf.Sqrt(1f - z * z);
				var direction = new Vector3(r * Mathf.Cos(angle), r * Mathf.Sin(angle), z);
				// Most stars are faint; a few are bright.
				float brightness = Mathf.Pow(Draw(seed, at + 2u), 6f) * 0.9f + 0.1f;
				Color color = ColorOf(Mathf.Lerp(2800f, 11000f, Draw(seed, at + 3u))) * brightness;
				FaceOf(direction, size, out CubemapFace face, out float px, out float py);
				Splat(faces[(int)face], size, px, py, color, brightness > 0.6f ? 1.2f : 0.7f, (byte)Mathf.RoundToInt(Phase(seed, s) * 255f));
			}
			var cubemap = new Cubemap(size, TextureFormat.RGBA32, false)
			{
				name = "Starfield",
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave,
			};
			for (int f = 0; f < 6; f++)
			{
				var colors = new Color[size * size];
				for (int i = 0; i < colors.Length; i++)
				{
					colors[i] = faces[f][i];
				}
				cubemap.SetPixels(colors, (CubemapFace)f);
			}
			// makeNoLongerReadable: the CPU copy goes unless somebody asked to keep it.
			cubemap.Apply(false, !keepReadable);
			return cubemap;
		}

		/// <param name="phase">
		/// The star's twinkle phase, written to alpha (the sky reads it there; the colour is rgb). It
		/// goes one pixel further out than the light does, so the four texels a bilinear sample blends
		/// anywhere on the star all hold the same phase and the whole star twinkles as one. A phase
		/// read from a grid in the sky instead split a star lying across a cell edge into two halves
		/// that flickered apart. Where two stars overlap, the pixel keeps the phase of the one that
		/// lights it more.
		/// </param>
		private static void Splat(Color32[] pixels, int size, float x, float y, Color color, float radius, byte phase)
		{
			int x0 = Mathf.FloorToInt(x - radius) - 1, x1 = Mathf.CeilToInt(x + radius) + 1;
			int y0 = Mathf.FloorToInt(y - radius) - 1, y1 = Mathf.CeilToInt(y + radius) + 1;
			for (int py = y0; py <= y1; py++)
			{
				if (py < 0 || py >= size) continue;
				for (int px = x0; px <= x1; px++)
				{
					if (px < 0 || px >= size) continue;
					float d = Vector2.Distance(new Vector2(px + 0.5f, py + 0.5f), new Vector2(x, y));
					float w = Mathf.Exp(-d * d / (radius * radius * 0.5f));
					int r = 0, g = 0, b = 0;
					if (w >= 0.02f)
					{
						r = Mathf.RoundToInt(color.r * w * 255f);
						g = Mathf.RoundToInt(color.g * w * 255f);
						b = Mathf.RoundToInt(color.b * w * 255f);
					}
					int i = py * size + px;
					Color32 old = pixels[i];
					int oldLight = old.r + old.g + old.b;
					bool owns = oldLight == 0 || r + g + b > oldLight;
					pixels[i] = new Color32(
						(byte)Mathf.Min(255, old.r + r),
						(byte)Mathf.Min(255, old.g + g),
						(byte)Mathf.Min(255, old.b + b),
						owns ? phase : old.a);
				}
			}
		}
	}
}
