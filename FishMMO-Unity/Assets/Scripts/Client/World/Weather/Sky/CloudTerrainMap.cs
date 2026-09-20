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
			// Blurred twice: it is the mountain that turns the air, not the rock on it.
			Blur(heights, scratch);
			Blur(scratch, heights);
			for (int y = 0; y < Resolution; y++)
			{
				int up = Mathf.Min(Resolution - 1, y + 1), down = Mathf.Max(0, y - 1);
				for (int x = 0; x < Resolution; x++)
				{
					int right = Mathf.Min(Resolution - 1, x + 1), left = Mathf.Max(0, x - 1);
					float dx = (heights[y * Resolution + right] - heights[y * Resolution + left]) / ((right - left) * texel);
					float dz = (heights[up * Resolution + x] - heights[down * Resolution + x]) / ((up - down) * texel);
					pixels[y * Resolution + x] = new Color(heights[y * Resolution + x], dx, dz, 1f);
				}
			}
			texture.SetPixels(pixels);
			texture.Apply(false, false);
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
