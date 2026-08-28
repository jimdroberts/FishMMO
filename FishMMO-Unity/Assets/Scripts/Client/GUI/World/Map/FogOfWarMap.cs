using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The part of a scene a character has explored, as a grid of coverage values over the scene's
	/// map rectangle.
	/// </summary>
	/// <remarks>
	/// <para><b>Coverage, not a bit.</b> Each cell holds how much fog is left over it — 255 for
	/// never visited, 0 for fully explored — rather than a single explored flag. A bit per cell
	/// is a quarter of the memory and produces a hard checkerboard edge wherever the player walked,
	/// because the smallest unit of reveal is then a whole cell. Storing coverage lets a reveal
	/// write a radial falloff, so the boundary between explored and unexplored is a soft ring at
	/// the edge of the player's sight rather than a staircase of squares, and it costs a byte per
	/// 16 square metres of world.</para>
	///
	/// <para><b>Reveal only ever lowers a value.</b> Fog does not come back. That makes the grid
	/// monotonic, which is what allows the overlapping reveals of a player walking in circles to
	/// be applied in any order without the result depending on the order — and means a partial
	/// save can only ever lose progress, never invent it.</para>
	///
	/// <para><b>It is not authoritative.</b> This lives on the player's machine and is never sent
	/// to the server. Anything that matters — Cartography experience, a reward for full
	/// exploration — must be decided by the server from where the character actually walked, which
	/// it already knows. See <see cref="FogOfWarStore"/> for what the signature on the file does
	/// and does not buy.</para>
	/// </remarks>
	public sealed class FogOfWarMap
	{
		/// <summary>Fog value meaning the cell has never been seen.</summary>
		public const byte Unexplored = 255;

		/// <summary>Fog value meaning the cell is fully explored.</summary>
		public const byte Explored = 0;

		/// <summary>
		/// Fraction of the reveal radius that is revealed completely, the rest being the falloff.
		/// </summary>
		/// <remarks>
		/// A falloff that starts at the centre would leave the ground under the player's feet
		/// half fogged, which looks like a rendering fault rather than a soft edge.
		/// </remarks>
		private const float SolidFraction = 0.65f;

		/// <summary>The world rectangle the grid covers, on the XZ plane.</summary>
		public Rect WorldRect { get; }

		/// <summary>Size of one cell in world metres.</summary>
		public float CellSize { get; }

		/// <summary>Number of cells across the X axis.</summary>
		public int CellsX { get; }

		/// <summary>Number of cells across the Z axis.</summary>
		public int CellsZ { get; }

		/// <summary>Fog coverage per cell, row-major with Z as the row.</summary>
		public byte[] Cells { get; }

		/// <summary>Whether anything has changed since the last time the flag was cleared.</summary>
		public bool IsDirty { get; private set; }

		/// <summary>Whether the texture needs rebuilding from <see cref="Cells"/>.</summary>
		private bool textureDirty = true;

		/// <summary>Lowest X cell index changed since the texture was last uploaded.</summary>
		private int dirtyMinX;

		/// <summary>Lowest Z cell index changed since the texture was last uploaded.</summary>
		private int dirtyMinZ;

		/// <summary>Highest X cell index changed since the texture was last uploaded.</summary>
		private int dirtyMaxX;

		/// <summary>Highest Z cell index changed since the texture was last uploaded.</summary>
		private int dirtyMaxZ;

		/// <summary>The texture handed to the UI, alpha carrying the fog coverage.</summary>
		private Texture2D texture;

		/// <summary>
		/// Scratch the cells are expanded into before being uploaded, kept for the map's lifetime.
		/// </summary>
		/// <remarks>
		/// Allocated once. The grid is rebuilt up to four times a second while the character is
		/// walking, and a 2 km scene at four-metre cells is a 500 by 500 texture — so allocating
		/// this per rebuild is a megabyte of garbage several times a second, for as long as the
		/// player is moving. Held rather than pooled because it is exactly as large as the grid and
		/// dies with it.
		/// </remarks>
		private Color32[] pixelBuffer;

		/// <summary>Cached result of <see cref="ExploredFraction"/>.</summary>
		private float cachedFraction;

		/// <summary>Whether <see cref="cachedFraction"/> needs recomputing.</summary>
		private bool fractionDirty = true;

		/// <summary>
		/// Builds an entirely unexplored map over a world rectangle.
		/// </summary>
		/// <param name="worldRect">The world rectangle to cover, on the XZ plane.</param>
		/// <param name="cellSize">Size of one cell in world metres.</param>
		public FogOfWarMap(Rect worldRect, float cellSize)
		{
			WorldRect = worldRect;
			CellSize = Mathf.Max(0.5f, cellSize);

			/* Ceiling, and at least one. A rectangle that is not a whole number of cells across
			 * must be covered by the grid rather than cropped by it, or the last few metres of a
			 * zone can never be explored and the map keeps a permanent unexplored stripe down one
			 * edge that no amount of walking removes. */
			CellsX = Mathf.Max(1, Mathf.CeilToInt(worldRect.width / CellSize));
			CellsZ = Mathf.Max(1, Mathf.CeilToInt(worldRect.height / CellSize));

			Cells = new byte[CellsX * CellsZ];
			for (int i = 0; i < Cells.Length; ++i)
			{
				Cells[i] = Unexplored;
			}
		}

		/// <summary>
		/// Rebuilds a map from cells loaded off disk.
		/// </summary>
		/// <param name="worldRect">The world rectangle the cells cover.</param>
		/// <param name="cellSize">Size of one cell in world metres.</param>
		/// <param name="cells">The cell data. Must be exactly the grid's size.</param>
		/// <returns>The loaded map, or null when the data does not match the grid.</returns>
		/// <remarks>
		/// Returns null rather than resizing. A cell array of the wrong length means the scene's
		/// bounds or cell size changed since the file was written, and stretching old data across
		/// new bounds would put the player's explored ground in the wrong place — worse than
		/// starting again, because it looks plausible.
		/// </remarks>
		public static FogOfWarMap FromCells(Rect worldRect, float cellSize, byte[] cells)
		{
			FogOfWarMap map = new FogOfWarMap(worldRect, cellSize);
			if (cells == null || cells.Length != map.Cells.Length)
			{
				return null;
			}

			System.Array.Copy(cells, map.Cells, cells.Length);
			return map;
		}

		/// <summary>
		/// Marks the area around a world position as explored.
		/// </summary>
		/// <param name="worldPosition">Centre of the reveal, in world space.</param>
		/// <param name="radius">Radius of the reveal in world metres.</param>
		/// <returns>True when at least one cell changed.</returns>
		public bool Reveal(Vector3 worldPosition, float radius)
		{
			if (radius <= 0.0f)
			{
				return false;
			}

			float localX = worldPosition.x - WorldRect.xMin;
			float localZ = worldPosition.z - WorldRect.yMin;

			int minX = Mathf.Max(0, Mathf.FloorToInt((localX - radius) / CellSize));
			int maxX = Mathf.Min(CellsX - 1, Mathf.CeilToInt((localX + radius) / CellSize));
			int minZ = Mathf.Max(0, Mathf.FloorToInt((localZ - radius) / CellSize));
			int maxZ = Mathf.Min(CellsZ - 1, Mathf.CeilToInt((localZ + radius) / CellSize));

			if (minX > maxX || minZ > maxZ)
			{
				return false;
			}

			float solidRadius = radius * SolidFraction;
			float falloffRange = Mathf.Max(0.0001f, radius - solidRadius);
			bool changed = false;

			int touchedMinX = int.MaxValue;
			int touchedMinZ = int.MaxValue;
			int touchedMaxX = int.MinValue;
			int touchedMaxZ = int.MinValue;

			for (int z = minZ; z <= maxZ; ++z)
			{
				float cellCenterZ = (z + 0.5f) * CellSize;
				float dz = cellCenterZ - localZ;
				int row = z * CellsX;

				for (int x = minX; x <= maxX; ++x)
				{
					float cellCenterX = (x + 0.5f) * CellSize;
					float dx = cellCenterX - localX;

					float distance = Mathf.Sqrt((dx * dx) + (dz * dz));
					if (distance > radius)
					{
						continue;
					}

					float remaining = distance <= solidRadius
						? 0.0f
						: (distance - solidRadius) / falloffRange;

					byte value = (byte)Mathf.RoundToInt(Mathf.Clamp01(remaining) * 255.0f);

					int index = row + x;
					if (value < Cells[index])
					{
						Cells[index] = value;
						changed = true;

						if (x < touchedMinX) { touchedMinX = x; }
						if (x > touchedMaxX) { touchedMaxX = x; }
						if (z < touchedMinZ) { touchedMinZ = z; }
						if (z > touchedMaxZ) { touchedMaxZ = z; }
					}
				}
			}

			if (changed)
			{
				IsDirty = true;
				fractionDirty = true;
				MarkTextureRegionDirty(touchedMinX, touchedMinZ, touchedMaxX, touchedMaxZ);
			}

			return changed;
		}

		/// <summary>
		/// Reveals the entire map.
		/// </summary>
		/// <remarks>
		/// For scenes whose definition turns fog off, and for the map baker's preview. Cheaper and
		/// clearer than special-casing "no fog" at every draw site.
		/// </remarks>
		public void RevealAll()
		{
			for (int i = 0; i < Cells.Length; ++i)
			{
				Cells[i] = Explored;
			}
			IsDirty = true;
			fractionDirty = true;
			MarkTextureRegionDirty(0, 0, CellsX - 1, CellsZ - 1);
		}

		/// <summary>
		/// Widens the block of cells the texture upload has to cover.
		/// </summary>
		/// <param name="minX">Lowest X cell index that changed.</param>
		/// <param name="minZ">Lowest Z cell index that changed.</param>
		/// <param name="maxX">Highest X cell index that changed.</param>
		/// <param name="maxZ">Highest Z cell index that changed.</param>
		/// <remarks>
		/// A rectangle rather than a list of cells, because that is what
		/// <c>Texture2D.SetPixels32</c> takes. The union of two distant reveals is a large
		/// rectangle covering mostly unchanged cells, but reveals arrive four times a second from a
		/// character who has moved a few metres — so in practice the block is the disc that was just
		/// revealed, about a thousand pixels, instead of the quarter of a million a full upload
		/// costs.
		/// </remarks>
		private void MarkTextureRegionDirty(int minX, int minZ, int maxX, int maxZ)
		{
			if (maxX < minX || maxZ < minZ)
			{
				return;
			}

			if (!textureDirty)
			{
				dirtyMinX = minX;
				dirtyMinZ = minZ;
				dirtyMaxX = maxX;
				dirtyMaxZ = maxZ;
				textureDirty = true;
				return;
			}

			dirtyMinX = Mathf.Min(dirtyMinX, minX);
			dirtyMinZ = Mathf.Min(dirtyMinZ, minZ);
			dirtyMaxX = Mathf.Max(dirtyMaxX, maxX);
			dirtyMaxZ = Mathf.Max(dirtyMaxZ, maxZ);
		}

		/// <summary>
		/// How explored a world position is.
		/// </summary>
		/// <param name="worldPosition">The position to test.</param>
		/// <returns>Zero for fully fogged, one for fully explored.</returns>
		/// <remarks>
		/// A position outside the grid reads as explored. Off-map is not a place the player can
		/// discover, and reporting it as fogged would hide every marker that sits just outside a
		/// scene's derived bounds — which, since those bounds are a boundary volume plus padding,
		/// includes things a level designer legitimately put at the edge.
		/// </remarks>
		public float ExploredAt(Vector3 worldPosition)
		{
			if (!TryGetCell(worldPosition, out int x, out int z))
			{
				return 1.0f;
			}

			return 1.0f - (Cells[(z * CellsX) + x] / 255.0f);
		}

		/// <summary>
		/// Whether a world position counts as discovered for the purposes of hiding markers.
		/// </summary>
		/// <param name="worldPosition">The position to test.</param>
		/// <returns>True when the position is more than half explored.</returns>
		public bool IsDiscovered(Vector3 worldPosition)
		{
			return ExploredAt(worldPosition) > 0.5f;
		}

		/// <summary>
		/// The fraction of the whole map that has been explored.
		/// </summary>
		/// <returns>Zero to one.</returns>
		/// <remarks>
		/// Walks every cell, so it is for a panel refreshing a progress readout rather than for a
		/// per-frame caller. At 4 metre cells a two-kilometre scene is a quarter of a million
		/// bytes, which is a fraction of a millisecond but not free.
		/// </remarks>
		public float ExploredFraction()
		{
			if (!fractionDirty)
			{
				return cachedFraction;
			}

			long total = 0;
			for (int i = 0; i < Cells.Length; ++i)
			{
				total += 255 - Cells[i];
			}

			cachedFraction = (float)(total / (255.0 * Cells.Length));
			fractionDirty = false;
			return cachedFraction;
		}

		/// <summary>
		/// The grid cell containing a world position.
		/// </summary>
		/// <param name="worldPosition">The position to convert.</param>
		/// <param name="x">The cell's X index.</param>
		/// <param name="z">The cell's Z index.</param>
		/// <returns>True when the position is inside the grid.</returns>
		public bool TryGetCell(Vector3 worldPosition, out int x, out int z)
		{
			x = Mathf.FloorToInt((worldPosition.x - WorldRect.xMin) / CellSize);
			z = Mathf.FloorToInt((worldPosition.z - WorldRect.yMin) / CellSize);
			return x >= 0 && x < CellsX && z >= 0 && z < CellsZ;
		}

		/// <summary>
		/// Clears the dirty flag after the map has been written to disk.
		/// </summary>
		public void ClearDirty()
		{
			IsDirty = false;
		}

		/// <summary>
		/// The fog as a texture, alpha carrying coverage, rebuilt only when the grid has changed.
		/// </summary>
		/// <returns>The fog texture. Never null once the map exists.</returns>
		/// <remarks>
		/// <para>RGBA rather than a single-channel format because UI Toolkit multiplies the sampled
		/// texel by the vertex colour: leaving the colour channels at white lets the fog be tinted
		/// to whatever suits the theme, where a single-channel texture would force it to be black.
		/// </para>
		/// <para>Bilinear, and that matters. At four metres a cell is a tenth of the minimap's
		/// width, so point sampling would show the grid itself; interpolation between cells, on
		/// top of the radial falloff the reveal already writes, is what makes the edge read as
		/// mist rather than as tiling.</para>
		/// </remarks>
		public Texture2D GetTexture()
		{
			if (texture == null)
			{
				texture = new Texture2D(CellsX, CellsZ, TextureFormat.RGBA32, false, true)
				{
					name = "FogOfWar",
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
					hideFlags = HideFlags.HideAndDontSave,
				};

				// A fresh texture holds nothing, so the whole grid has to go up regardless.
				MarkTextureRegionDirty(0, 0, CellsX - 1, CellsZ - 1);
			}

			if (textureDirty)
			{
				if (pixelBuffer == null)
				{
					pixelBuffer = new Color32[Cells.Length];
				}

				int blockWidth = (dirtyMaxX - dirtyMinX) + 1;
				int blockHeight = (dirtyMaxZ - dirtyMinZ) + 1;

				int destination = 0;
				for (int z = dirtyMinZ; z <= dirtyMaxZ; ++z)
				{
					int row = z * CellsX;
					for (int x = dirtyMinX; x <= dirtyMaxX; ++x)
					{
						pixelBuffer[destination++] = new Color32(255, 255, 255, Cells[row + x]);
					}
				}

				texture.SetPixels32(dirtyMinX, dirtyMinZ, blockWidth, blockHeight, pixelBuffer);
				texture.Apply(false, false);
				textureDirty = false;
			}

			return texture;
		}

		/// <summary>
		/// Destroys the texture.
		/// </summary>
		public void ReleaseTexture()
		{
			if (texture != null)
			{
				Object.Destroy(texture);
				texture = null;
				textureDirty = true;
			}
		}
	}
}
