using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// A coarse, smoothed picture of the ground round the viewer, for the clouds: how high it is and
	/// which way it rises.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The cloud field knew nothing about the land under it. Its bands are shells over sea level, so
	/// a mountain simply stood up into them; even the ground fog was measured from zero, which left
	/// a plateau above its own fog and gave a valley floor all of it. With this the sky can do the
	/// two things terrain does to weather: cloud gathers on the windward side of high ground, where
	/// the air is forced up the slope, and clears in its lee; and the fog lies on the ground that
	/// is actually there.
	/// </para>
	/// <para>
	/// Mountains here are the high points of Unity terrain heightmaps, so that is what is read. It
	/// is deliberately coarse and deliberately blurred: what bends an airflow is the mountain, not
	/// the boulder on it, and a rough picture is both truer to that and nearly free. It is rebuilt
	/// only when the viewer has travelled far enough for the window to need moving — a few thousand
	/// height lookups every few hundred metres — and read once per cloud sample, as one texture tap.
	/// The clouds are not cut away where the rock is; the depth buffer already hides what is inside
	/// the mountain, and carving it out would pay for a lookup to remove what cannot be seen.
	/// </para>
	/// </remarks>
	public sealed class CloudTerrainMap
	{
		public const int Resolution = 96;
		/// <summary>Ten kilometres: five either way, so a mountain three or four out is well inside it.</summary>
		public const float SizeMeters = 10000f;
		/// <summary>The window moves in steps of this many texels, so it is rebuilt every few hundred metres.</summary>
		private const int SnapTexels = 8;

		private static readonly int TextureId = Shader.PropertyToID("_FishCloudTerrain");
		private static readonly int RectId = Shader.PropertyToID("_FishCloudTerrainRect");

		private Texture2D texture;
		private float[] heights;
		private float[] scratch;
		private float[] smooth;
		/// <summary>Three passes of a six-texel box: a spread of about six and a half texels, some 700 m.</summary>
		private const int SmoothPasses = 3;
		private const int SmoothRadius = 6;
		private Color[] pixels;
		private Vector2 corner = new Vector2(float.NaN, float.NaN);
		private int terrainCount = -1;
		private bool any;

		/// <summary>Moves the window if the viewer has gone far enough, and publishes it.</summary>
		public void Update(Vector3 centre)
		{
			float texel = SizeMeters / Resolution;
			float snap = texel * SnapTexels;
			var wanted = new Vector2(
				Mathf.Round((centre.x - SizeMeters * 0.5f) / snap) * snap,
				Mathf.Round((centre.z - SizeMeters * 0.5f) / snap) * snap);
			Terrain[] terrains = Terrain.activeTerrains;
			if (texture == null || wanted != corner || terrains.Length != terrainCount)
			{
				corner = wanted;
				terrainCount = terrains.Length;
				Rebuild(terrains, texel);
			}
			Shader.SetGlobalTexture(TextureId, texture);
			Shader.SetGlobalVector(RectId, new Vector4(corner.x, corner.y, SizeMeters, any ? 1f : 0f));
		}

		private void Rebuild(Terrain[] terrains, float texel)
		{
			if (texture == null)
			{
				texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBAHalf, false, true)
				{
					name = "Cloud Terrain",
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
					hideFlags = HideFlags.DontSave,
				};
				heights = new float[Resolution * Resolution];
				scratch = new float[Resolution * Resolution];
				pixels = new Color[Resolution * Resolution];
			}
			any = false;
			for (int y = 0; y < Resolution; y++)
			{
				for (int x = 0; x < Resolution; x++)
				{
					var at = new Vector3(corner.x + (x + 0.5f) * texel, 0f, corner.y + (y + 0.5f) * texel);
					float height = 0f;
					for (int t = 0; t < terrains.Length; t++)
					{
						Terrain terrain = terrains[t];
						if (terrain == null || terrain.terrainData == null)
						{
							continue;
						}
						Vector3 origin = terrain.GetPosition();
						Vector3 size = terrain.terrainData.size;
						if (at.x < origin.x || at.z < origin.z || at.x > origin.x + size.x || at.z > origin.z + size.z)
						{
							continue;
						}
						height = Mathf.Max(height, terrain.SampleHeight(at) + origin.y);
						any = true;
					}
					heights[y * Resolution + x] = height;
				}
			}
			// Two pictures of the same ground. The fog lies on the ground that is there, so it gets
			// the terrain barely smoothed. The AIR is another matter: it is turned by the mountain,
			// not by the gullies on it, and everything that bends the cloud field — the lift, the
			// windward gathering, the turning aside — is read from the terrain smoothed over some
			// seven hundred metres. That is not a nicety. The cloud field is looked up at a position
			// these terms displace, and a displacement that changes faster than the distance it
			// moves things folds the field onto itself: read from a terrain smoothed over two
			// hundred metres, with every ridge in it, the sky overhead came out as concentric rings
			// round the mountain — the same strip of noise read again and again along each contour.
			Blur(heights, scratch);
			Blur(scratch, heights);
			smooth ??= new float[Resolution * Resolution];
			System.Array.Copy(heights, smooth, heights.Length);
			for (int pass = 0; pass < SmoothPasses; pass++)
			{
				BoxBlur(smooth, scratch, SmoothRadius);
			}
			for (int y = 0; y < Resolution; y++)
			{
				int up = Mathf.Min(Resolution - 1, y + 1), down = Mathf.Max(0, y - 1);
				for (int x = 0; x < Resolution; x++)
				{
					int right = Mathf.Min(Resolution - 1, x + 1), left = Mathf.Max(0, x - 1);
					float dx = (smooth[y * Resolution + right] - smooth[y * Resolution + left]) / ((right - left) * texel);
					float dz = (smooth[up * Resolution + x] - smooth[down * Resolution + x]) / ((up - down) * texel);
					// r the mountain as the air feels it, gb which way that rises, a the ground itself.
					pixels[y * Resolution + x] = new Color(smooth[y * Resolution + x], dx, dz, heights[y * Resolution + x]);
				}
			}
			texture.SetPixels(pixels);
			texture.Apply(false, false);
		}

		/// <summary>A box blur of the given radius, separable, in place (through a scratch buffer).</summary>
		private static void BoxBlur(float[] data, float[] scratch, int radius)
		{
			for (int y = 0; y < Resolution; y++)
			{
				for (int x = 0; x < Resolution; x++)
				{
					float sum = 0f;
					int count = 0;
					for (int o = -radius; o <= radius; o++)
					{
						int sx = x + o;
						if (sx >= 0 && sx < Resolution)
						{
							sum += data[y * Resolution + sx];
							count++;
						}
					}
					scratch[y * Resolution + x] = sum / count;
				}
			}
			for (int y = 0; y < Resolution; y++)
			{
				for (int x = 0; x < Resolution; x++)
				{
					float sum = 0f;
					int count = 0;
					for (int o = -radius; o <= radius; o++)
					{
						int sy = y + o;
						if (sy >= 0 && sy < Resolution)
						{
							sum += scratch[sy * Resolution + x];
							count++;
						}
					}
					data[y * Resolution + x] = sum / count;
				}
			}
		}

		private static void Blur(float[] from, float[] to)
		{
			for (int y = 0; y < Resolution; y++)
			{
				for (int x = 0; x < Resolution; x++)
				{
					float sum = 0f;
					int count = 0;
					for (int oy = -1; oy <= 1; oy++)
					{
						int sy = y + oy;
						if (sy < 0 || sy >= Resolution)
						{
							continue;
						}
						for (int ox = -1; ox <= 1; ox++)
						{
							int sx = x + ox;
							if (sx < 0 || sx >= Resolution)
							{
								continue;
							}
							sum += from[sy * Resolution + sx];
							count++;
						}
					}
					to[y * Resolution + x] = sum / count;
				}
			}
		}

		public void Dispose()
		{
			if (texture != null)
			{
				if (Application.isPlaying) Object.Destroy(texture); else Object.DestroyImmediate(texture);
				texture = null;
			}
			Shader.SetGlobalVector(RectId, Vector4.zero);
		}
	}
}
