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
	/// </para>
	/// <para>
	/// How the air goes ROUND the terrain, and where the rock is, are not here: they are worked out once
	/// for the whole scene (<see cref="CloudFlowField"/>), where this map is a window round the viewer.
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

		/// <summary>The highest ground in the window, barely smoothed (the map's a), m.</summary>
		public float HighestGround { get; private set; }
		/// <summary>
		/// The highest the ground stands as the pooled air feels it — smoothed over some 700 m (the
		/// map's r) — m. With <see cref="HighestGround"/>, what bounds how high a fog lying on this
		/// ground can reach anywhere in the window (FogLayerView.Shell).
		/// </summary>
		public float HighestPooled { get; private set; }
		/// <summary>
		/// The lowest the smoothed ground stands in the window, m: the land the air arrives at the high
		/// ground over. How high a mountain is to the air is how far it rises above this, not above the
		/// sea (_FishCloudFlow.z).
		/// </summary>
		public float Floor { get; private set; }

		private Texture2D texture;
		private float[] heights;
		private float[] scratch;
		private float[] smooth;
		/// <summary>Three passes of a six-texel box: a spread of about six and a half texels, some 700 m.</summary>
		private const int SmoothPasses = 3;
		private const int SmoothRadius = 6;
		private Color[] pixels;
		private Vector2 corner = new Vector2(float.NaN, float.NaN);
		private int terrainCount = int.MinValue;
		private bool any;

		/// <summary>Moves the window if the viewer has gone far enough, and publishes it.</summary>
		public void Update(Vector3 centre)
		{
			float texel = SizeMeters / Resolution;
			float snap = texel * SnapTexels;
			var wanted = new Vector2(
				Mathf.Round((centre.x - SizeMeters * 0.5f) / snap) * snap,
				Mathf.Round((centre.z - SizeMeters * 0.5f) / snap) * snap);
			TerrainSet terrains = TerrainSet.Current;
			if (texture == null || wanted != corner || terrains.Signature != terrainCount)
			{
				corner = wanted;
				terrainCount = terrains.Signature;
				Rebuild(terrains, texel);
			}
			Shader.SetGlobalTexture(TextureId, texture);
			Shader.SetGlobalVector(RectId, new Vector4(corner.x, corner.y, SizeMeters, any ? 1f : 0f));
		}

		private void Rebuild(TerrainSet terrains, float texel)
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
					float height = 0f;
					if (terrains.TryHeight(corner.x + (x + 0.5f) * texel, corner.y + (y + 0.5f) * texel, out float sampled))
					{
						height = Mathf.Max(0f, sampled);
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

			// The heights the fog's shell has to hold a fog's top over. The shader eases the map to sea
			// level at its edge, so sea level is always among what the ground can be.
			float highest = 0f, pooled = 0f, floor = 0f;
			if (any)
			{
				floor = float.MaxValue;
				for (int i = 0; i < heights.Length; i++)
				{
					highest = Mathf.Max(highest, heights[i]);
					pooled = Mathf.Max(pooled, smooth[i]);
					floor = Mathf.Min(floor, smooth[i]);
				}
			}
			HighestGround = highest;
			HighestPooled = pooled;
			Floor = floor;
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

	/// <summary>
	/// The scene's terrains and where each stands, read once and kept, for the sky's pictures of the ground
	/// (<see cref="CloudFlowField"/>, <see cref="MistGroundMap"/>) to sample heights from cheaply.
	/// </summary>
	/// <remarks>
	/// Asking each terrain where it is for every height read — <c>GetPosition</c>, <c>terrainData</c>,
	/// <c>terrainData.size</c>, all calls into the engine — cost three native calls per terrain per sample:
	/// four hundred thousand a frame over a scene of thirty-odd tiles while a map was being read, and frame
	/// drops with it. And <c>Terrain.activeTerrains</c> builds a new array on every read. So the set is read
	/// at most twice a second, its bounds kept as plain floats, and a height costs a few comparisons and the
	/// one <c>SampleHeight</c> of the terrain that holds the point.
	/// <para>
	/// <b>And the backdrop.</b> A generated scene's terrain is only its playable area; the mountains round it
	/// out to the horizon are the client-only backdrop (<see cref="FishMMO.Shared.SceneBackdrop"/>), rings
	/// of plain meshes. The clouds went round the terrain's peaks and straight through the backdrop's. So
	/// the backdrop's meshes — stored readable — are laid once onto a height grid of their own (each cell
	/// the highest vertex in it, which leaves out the skirts hanging under the rings' edges), and wherever
	/// no terrain covers a point its height is read from that: the ground the sky knows reaches the horizon.
	/// </para>
	/// </remarks>
	public sealed class TerrainSet
	{
		/// <summary>Frames between reads of the scene's terrains.</summary>
		private const int RefreshFrames = 30;

		private static TerrainSet current;
		private static int readFrame = int.MinValue;

		private Terrain[] terrains = new Terrain[0];
		// minX, minZ, maxX, maxZ, originY per terrain.
		private float[] bounds = new float[0];

		// The backdrop's ground: n × n cells from (x0, z0), each `cell` metres, NaN where nothing stands.
		private float[] backdrop;
		private int backdropN;
		private float backdropX0, backdropZ0, backdropCell;
		private int backdropSignature;
		/// <summary>The most cells across the backdrop's grid, and the finest a cell may be, m.</summary>
		private const int BackdropCells = 1024;
		private const float BackdropFinest = 25f;

		/// <summary>The terrains and where each piece stands, as one number: changes when the set does.</summary>
		public int Signature { get; private set; }
		/// <summary>How many terrains there are.</summary>
		public int Count => terrains.Length;
		/// <summary>The ground they cover together (x, z min and max), or zero size when there are none.</summary>
		public Rect Extent { get; private set; }

		/// <summary>The set as of this frame (or at most <see cref="RefreshFrames"/> ago).</summary>
		public static TerrainSet Current
		{
			get
			{
				int frame = Time.frameCount;
				if (current == null || frame - readFrame >= RefreshFrames || frame < readFrame)
				{
					current ??= new TerrainSet();
					current.Read();
					readFrame = frame;
				}
				return current;
			}
		}

		private void Read()
		{
			Terrain[] found = Terrain.activeTerrains;
			// (The backdrop is read below, when it changes.)
			int count = 0;
			foreach (Terrain t in found)
			{
				if (t != null && t.terrainData != null)
				{
					count++;
				}
			}
			if (terrains.Length != count)
			{
				terrains = new Terrain[count];
				bounds = new float[count * 5];
			}
			int k = 0;
			int hash = 17 + count;
			float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
			unchecked
			{
				foreach (Terrain t in found)
				{
					if (t == null)
					{
						continue;
					}
					TerrainData data = t.terrainData;
					if (data == null)
					{
						continue;
					}
					Vector3 origin = t.GetPosition();
					Vector3 size = data.size;
					terrains[k] = t;
					bounds[k * 5] = origin.x;
					bounds[k * 5 + 1] = origin.z;
					bounds[k * 5 + 2] = origin.x + size.x;
					bounds[k * 5 + 3] = origin.z + size.z;
					bounds[k * 5 + 4] = origin.y;
					minX = Mathf.Min(minX, origin.x);
					minZ = Mathf.Min(minZ, origin.z);
					maxX = Mathf.Max(maxX, origin.x + size.x);
					maxZ = Mathf.Max(maxZ, origin.z + size.z);
					hash = hash * 31 + t.GetHashCode();
					hash = hash * 31 + origin.GetHashCode();
					hash = hash * 31 + size.GetHashCode();
					k++;
				}
			}
			int backdropNow = BackdropSignature();
			if (backdropNow != backdropSignature)
			{
				backdropSignature = backdropNow;
				BuildBackdrop();
			}
			if (backdrop != null)
			{
				float span = backdropN * backdropCell;
				minX = Mathf.Min(minX, backdropX0);
				minZ = Mathf.Min(minZ, backdropZ0);
				maxX = Mathf.Max(maxX, backdropX0 + span);
				maxZ = Mathf.Max(maxZ, backdropZ0 + span);
			}
			Signature = unchecked(hash * 31 + backdropNow);
			Extent = count > 0 || backdrop != null ? Rect.MinMaxRect(minX, minZ, maxX, maxZ) : default;
		}

		private static int BackdropSignature()
		{
			int hash = 0;
			unchecked
			{
				foreach (FishMMO.Shared.SceneBackdrop b in FishMMO.Shared.SceneBackdrop.Active)
				{
					if (b != null)
					{
						hash = hash * 31 + b.GetHashCode();
						hash = hash * 31 + b.transform.position.GetHashCode();
					}
				}
			}
			return hash;
		}

		/// <summary>Lays the loaded backdrops' meshes onto the backdrop grid: the highest vertex in each cell.</summary>
		private void BuildBackdrop()
		{
			backdrop = null;
			var filters = new System.Collections.Generic.List<MeshFilter>();
			Bounds area = default;
			bool any = false;
			foreach (FishMMO.Shared.SceneBackdrop b in FishMMO.Shared.SceneBackdrop.Active)
			{
				if (b == null)
				{
					continue;
				}
				foreach (MeshFilter filter in b.GetComponentsInChildren<MeshFilter>())
				{
					Mesh mesh = filter.sharedMesh;
					if (mesh == null || !mesh.isReadable)
					{
						continue;
					}
					Bounds world = filter.TryGetComponent(out MeshRenderer renderer) ? renderer.bounds : new Bounds(filter.transform.TransformPoint(mesh.bounds.center), mesh.bounds.size);
					if (any) area.Encapsulate(world); else area = world;
					any = true;
					filters.Add(filter);
				}
			}
			if (!any)
			{
				return;
			}
			float span = Mathf.Max(area.size.x, area.size.z);
			float cell = Mathf.Max(BackdropFinest, span / BackdropCells);
			int n = Mathf.Max(2, Mathf.CeilToInt(span / cell));
			var grid = new float[n * n];
			for (int i = 0; i < grid.Length; i++)
			{
				grid[i] = float.NaN;
			}
			float x0 = area.min.x, z0 = area.min.z;
			foreach (MeshFilter filter in filters)
			{
				Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
				Vector3[] vertices = filter.sharedMesh.vertices;
				for (int v = 0; v < vertices.Length; v++)
				{
					Vector3 w = toWorld.MultiplyPoint3x4(vertices[v]);
					int x = (int)((w.x - x0) / cell), z = (int)((w.z - z0) / cell);
					if (x < 0 || z < 0 || x >= n || z >= n)
					{
						continue;
					}
					int i = z * n + x;
					if (float.IsNaN(grid[i]) || w.y > grid[i])
					{
						grid[i] = w.y;
					}
				}
			}
			// The outer rings' vertices stand further apart than a cell: fill the cells between from
			// their neighbours, a few passes (never the hole the scene's own terrain fills, which no
			// vertex is within reach of).
			var next = (float[])grid.Clone();
			for (int pass = 0; pass < 8; pass++)
			{
				bool filled = false;
				for (int z = 0; z < n; z++)
				{
					for (int x = 0; x < n; x++)
					{
						int i = z * n + x;
						if (!float.IsNaN(grid[i]))
						{
							continue;
						}
						float sum = 0f;
						int count = 0;
						if (x > 0 && !float.IsNaN(grid[i - 1])) { sum += grid[i - 1]; count++; }
						if (x < n - 1 && !float.IsNaN(grid[i + 1])) { sum += grid[i + 1]; count++; }
						if (z > 0 && !float.IsNaN(grid[i - n])) { sum += grid[i - n]; count++; }
						if (z < n - 1 && !float.IsNaN(grid[i + n])) { sum += grid[i + n]; count++; }
						if (count >= 2)
						{
							next[i] = sum / count;
							filled = true;
						}
					}
				}
				System.Array.Copy(next, grid, grid.Length);
				if (!filled)
				{
					break;
				}
			}
			backdrop = grid;
			backdropN = n;
			backdropX0 = x0;
			backdropZ0 = z0;
			backdropCell = cell;
		}

		/// <summary>The backdrop's ground at a point, bilinear over the cells that hold any; false off it.</summary>
		private bool TryBackdrop(float x, float z, out float height)
		{
			height = 0f;
			if (backdrop == null)
			{
				return false;
			}
			float u = (x - backdropX0) / backdropCell - 0.5f, v = (z - backdropZ0) / backdropCell - 0.5f;
			if (u < -0.5f || v < -0.5f || u > backdropN - 0.5f || v > backdropN - 0.5f)
			{
				return false;
			}
			u = Mathf.Clamp(u, 0f, backdropN - 1.001f);
			v = Mathf.Clamp(v, 0f, backdropN - 1.001f);
			int i = (int)u, j = (int)v;
			float fu = u - i, fv = v - j;
			float a = backdrop[j * backdropN + i], b = backdrop[j * backdropN + i + 1];
			float c = backdrop[(j + 1) * backdropN + i], d = backdrop[(j + 1) * backdropN + i + 1];
			float weight = 0f, sum = 0f;
			if (!float.IsNaN(a)) { float w = (1f - fu) * (1f - fv); sum += a * w; weight += w; }
			if (!float.IsNaN(b)) { float w = fu * (1f - fv); sum += b * w; weight += w; }
			if (!float.IsNaN(c)) { float w = (1f - fu) * fv; sum += c * w; weight += w; }
			if (!float.IsNaN(d)) { float w = fu * fv; sum += d * w; weight += w; }
			if (weight <= 1e-4f)
			{
				return false;
			}
			height = sum / weight;
			return true;
		}

		/// <summary>The highest ground at a point, m; false where no terrain covers it.</summary>
		public bool TryHeight(float x, float z, out float height)
		{
			height = float.NegativeInfinity;
			bool any = false;
			for (int k = 0; k < terrains.Length; k++)
			{
				int b = k * 5;
				if (x < bounds[b] || z < bounds[b + 1] || x > bounds[b + 2] || z > bounds[b + 3])
				{
					continue;
				}
				Terrain t = terrains[k];
				if (!t)
				{
					continue;
				}
				height = Mathf.Max(height, t.SampleHeight(new Vector3(x, 0f, z)) + bounds[b + 4]);
				any = true;
			}
			// Off the terrain: the backdrop's ground, where there is one.
			return any || TryBackdrop(x, z, out height);
		}

		/// <summary>The terrains themselves, for what needs more than heights (their trees).</summary>
		public Terrain[] Terrains => terrains;
	}
}
