using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Water
{
	/// <summary>
	/// Where the ground is molten: 1 where it lies below the lava's level, 0 where it stands above, over
	/// the scene's terrains — mipmapped, so one tap at the right level is the molten share of the ground
	/// within any reach.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What the lava's light and fumes need from the ground, and only that.</b> How much of what a
	/// surface faces is glowing, and whether there is lava under a patch of fume, are both "what share
	/// of the ground near here is molten" at some radius. A box-filtered mip chain answers that for
	/// every radius with one texture read; the radius picks the level.
	/// </para>
	/// <para>
	/// <b>Built from the heightmaps once, at load.</b> Each tile's heights are read with one
	/// <c>GetHeights</c> and interpolated here, as the shore field does — asking
	/// <c>GetInterpolatedHeight</c> per texel is what made the shore field slow. A power-of-two square
	/// of about four metres a texel (at most 1024²), so the mips halve evenly; a lake's shore needs no
	/// finer, because the light it casts is an average over metres anyway.
	/// </para>
	/// <para>
	/// Off every terrain the mask is molten: the surface's disc is lava wherever no ground covers it.
	/// The texture is not rebuilt when a terrain is sculpted — call <see cref="WaterSurface.RebuildLavaMask"/>.
	/// </para>
	/// </remarks>
	internal sealed class LavaMask
	{
		/// <summary>Metres a texel aims at; the largest scenes settle coarser at the cap.</summary>
		public const float TargetTexelMetres = 4f;

		/// <summary>The largest side, in texels. 1 MB of R8 with its mips.</summary>
		public const int MaximumResolution = 1024;

		/// <summary>The softness of the shoreline, in metres of height either side of the level.</summary>
		private const float ShoreSoftness = 0.5f;

		public Texture2D Texture;
		/// <summary>xy the world minimum, zw the size; square.</summary>
		public Vector4 Rect;
		public float TexelMetres;
		/// <summary>The level it was built against.</summary>
		public float Level;
		/// <summary>The terrains it was built from, to notice one arriving or leaving.</summary>
		public int TerrainCount;

		private struct Tile
		{
			public float X, Y, Z, Width, Height, Length;
			public int Samples;
			public float[,] Heights;

			/// <summary>Bilinear between heightmap samples, as <c>GetInterpolatedHeight</c> interpolates.</summary>
			public float GroundAt(float worldX, float worldZ)
			{
				float fx = Mathf.Clamp01((worldX - X) / Width) * (Samples - 1);
				float fz = Mathf.Clamp01((worldZ - Z) / Length) * (Samples - 1);
				int x0 = Mathf.Min((int)fx, Samples - 2);
				int z0 = Mathf.Min((int)fz, Samples - 2);
				float tx = fx - x0;
				float tz = fz - z0;
				float near = Heights[z0, x0] + (Heights[z0, x0 + 1] - Heights[z0, x0]) * tx;
				float far = Heights[z0 + 1, x0] + (Heights[z0 + 1, x0 + 1] - Heights[z0 + 1, x0]) * tx;
				return Y + (near + (far - near) * tz) * Height;
			}
		}

		/// <summary>The terrains of a scene that have ground to read.</summary>
		public static void Terrains(Scene scene, List<Terrain> into)
		{
			into.Clear();
			Terrain.GetActiveTerrains(into);
			into.RemoveAll(t => t == null || t.terrainData == null || t.terrainData.heightmapResolution < 2 || t.gameObject.scene != scene);
		}

		/// <summary>
		/// Builds (or rebuilds, into the same texture) the mask of a scene's terrains against a level.
		/// False when there are no terrains: then everything is molten, and the shader is told so.
		/// </summary>
		public bool Build(List<Terrain> terrains, float level)
		{
			TerrainCount = terrains.Count;
			Level = level;
			if (terrains.Count == 0)
			{
				return false;
			}

			var tiles = new Tile[terrains.Count];
			float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
			for (int i = 0; i < terrains.Count; i++)
			{
				TerrainData data = terrains[i].terrainData;
				Vector3 at = terrains[i].GetPosition();
				int samples = data.heightmapResolution;
				tiles[i] = new Tile
				{
					X = at.x, Y = at.y, Z = at.z,
					Width = data.size.x, Height = data.size.y, Length = data.size.z,
					Samples = samples,
					Heights = data.GetHeights(0, 0, samples, samples),
				};
				minX = Mathf.Min(minX, at.x);
				minZ = Mathf.Min(minZ, at.z);
				maxX = Mathf.Max(maxX, at.x + data.size.x);
				maxZ = Mathf.Max(maxZ, at.z + data.size.z);
			}

			float side = Mathf.Max(maxX - minX, maxZ - minZ);
			int resolution = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(side / TargetTexelMetres)), 16, MaximumResolution);
			TexelMetres = side / resolution;
			Rect = new Vector4(minX, minZ, side, side);

			var ground = new float[resolution * resolution];
			for (int i = 0; i < ground.Length; i++)
			{
				ground[i] = float.MinValue;
			}
			// Tile by tile over only the texels each covers; where tiles meet, the higher ground.
			foreach (Tile tile in tiles)
			{
				int x0 = Mathf.Clamp(Mathf.FloorToInt((tile.X - minX) / TexelMetres), 0, resolution - 1);
				int x1 = Mathf.Clamp(Mathf.CeilToInt((tile.X + tile.Width - minX) / TexelMetres), 0, resolution - 1);
				int z0 = Mathf.Clamp(Mathf.FloorToInt((tile.Z - minZ) / TexelMetres), 0, resolution - 1);
				int z1 = Mathf.Clamp(Mathf.CeilToInt((tile.Z + tile.Length - minZ) / TexelMetres), 0, resolution - 1);
				for (int z = z0; z <= z1; z++)
				{
					float wz = minZ + (z + 0.5f) * TexelMetres;
					if (wz < tile.Z || wz > tile.Z + tile.Length)
					{
						continue;
					}
					for (int x = x0; x <= x1; x++)
					{
						float wx = minX + (x + 0.5f) * TexelMetres;
						if (wx < tile.X || wx > tile.X + tile.Width)
						{
							continue;
						}
						int index = z * resolution + x;
						ground[index] = Mathf.Max(ground[index], tile.GroundAt(wx, wz));
					}
				}
			}

			var bytes = new byte[resolution * resolution];
			for (int i = 0; i < bytes.Length; i++)
			{
				// No ground under a texel: the disc's lava shows there.
				float molten = ground[i] == float.MinValue ? 1f : Mathf.Clamp01((level - ground[i]) / (2f * ShoreSoftness) + 0.5f);
				bytes[i] = (byte)Mathf.RoundToInt(molten * 255f);
			}

			/* Refilled in place where it can be: this runs from a camera callback, where Unity refuses
			 * DestroyImmediate. */
			if (Texture == null)
			{
				Texture = new Texture2D(resolution, resolution, TextureFormat.R8, true, true)
				{
					name = "Lava mask",
					hideFlags = HideFlags.HideAndDontSave,
					wrapMode = TextureWrapMode.Clamp,
					filterMode = FilterMode.Trilinear,
				};
			}
			else if (Texture.width != resolution)
			{
				Texture.Reinitialize(resolution, resolution, TextureFormat.R8, true);
			}
			Texture.SetPixelData(bytes, 0);
			// The mips are box-filtered averages: exactly "the molten share within this reach".
			Texture.Apply(true, false);
			return true;
		}
	}
}
