using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The part of a scene a character has explored, as a grid of chunks over the scene's bounds.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A chunk is either explored or it is not, and walking into one explores the whole of it. That
	/// is the entire model: there is no coverage value, no radius, and no falloff. What the player
	/// gets is a map that opens up a block at a time as they travel, and a percentage that means
	/// exactly what it says — chunks visited out of chunks in the scene.
	/// </para>
	/// <para>
	/// <b>This replaced a per-cell radial reveal</b> that stored a coverage byte for every four
	/// metres of ground: a thousand-metre scene was a 277 by 277 grid, seventy-seven thousand bytes
	/// held in memory, gzipped on every save, and uploaded to a texture through a dirty-rectangle
	/// tracker. The percentage it produced moved by about one point per sixty metres walked, which
	/// read as a readout that never changed. The same scene is nine chunks square here: eighty-one
	/// bytes, no compression, no dirty rectangle, and every chunk entered is a visible step.
	/// </para>
	/// <para>
	/// <b>Chunk size is a scene's own business.</b> It comes from the map definition, falling back
	/// to <c>FogOfWarDefaults.ChunkSize</c>, so a cramped dungeon can be divided more finely than
	/// open country without anything else changing. It is baked into the saved file and a change
	/// discards the old one — a chunk index means a different piece of ground at a different size.
	/// </para>
	/// </remarks>
	public sealed class FogOfWarMap
	{
		/// <summary>Chunk state meaning the character has never been here.</summary>
		public const byte Unexplored = 0;

		/// <summary>Chunk state meaning the character has entered this chunk.</summary>
		public const byte Explored = 1;

		/// <summary>The world rectangle, on the XZ plane, this map covers.</summary>
		public Rect WorldRect { get; }

		/// <summary>The length of one chunk's side, in world metres.</summary>
		public float ChunkSize { get; }

		/// <summary>Number of chunks along the X axis.</summary>
		public int ChunksX { get; }

		/// <summary>Number of chunks along the Z axis.</summary>
		public int ChunksZ { get; }

		/// <summary>
		/// The world rectangle the chunk grid actually spans.
		/// </summary>
		/// <remarks>
		/// <b>Not <see cref="WorldRect"/>, and the difference is visible.</b> The grid rounds up to
		/// whole chunks, so it always covers the scene's rectangle and usually overhangs it — by up
		/// to one chunk short of a full one on each axis. Anything mapping the chunk grid onto
		/// screen space has to use this rectangle: treating the texture as covering
		/// <see cref="WorldRect"/> stretches it by the overhang, which at four-metre cells was a
		/// rounding error nobody could see and at chunk sizes is a third of a chunk of drift by the
		/// far edge — the fog visibly out of step with the ground it describes.
		/// </remarks>
		public Rect GridRect { get; }

		/// <summary>
		/// The chunk grid, row-major from the rectangle's minimum corner.
		/// </summary>
		/// <remarks>
		/// Exposed because the store writes it verbatim — one byte per chunk, which for any real
		/// scene is smaller than the header describing it.
		/// </remarks>
		public byte[] Chunks { get; }

		/// <summary>How many chunks have been explored.</summary>
		/// <remarks>
		/// Kept as the grid is written rather than counted on demand. That is what lets
		/// <see cref="ExploredFraction"/> be a division instead of a walk, and it is why this class
		/// no longer needs a cached fraction and a dirty flag to guard it.
		/// </remarks>
		public int ExploredChunkCount { get; private set; }

		/// <summary>Total number of chunks in the scene.</summary>
		public int ChunkCount => Chunks.Length;

		/// <summary>True when the map has changed since it was last written to disk.</summary>
		public bool IsDirty { get; private set; }

		/// <summary>True when the texture no longer matches the grid.</summary>
		private bool textureDirty = true;

		/// <summary>The fog texture, built on demand and rebuilt whenever a chunk changes.</summary>
		private Texture2D texture;

		/// <summary>Scratch buffer for the texture upload, sized to the grid and allocated once.</summary>
		private Color32[] pixelBuffer;

		/// <summary>
		/// Builds an entirely unexplored map over a world rectangle.
		/// </summary>
		/// <param name="worldRect">The world rectangle to cover, on the XZ plane.</param>
		/// <param name="chunkSize">Length of one chunk's side in world metres.</param>
		public FogOfWarMap(Rect worldRect, float chunkSize)
		{
			WorldRect = worldRect;
			ChunkSize = Mathf.Max(1.0f, chunkSize);

			/* Ceiling, and at least one. A rectangle that is not a whole number of chunks across
			 * must be covered by the grid rather than cropped by it, or the last few metres of a
			 * zone can never be explored and the map keeps a permanent unexplored stripe down one
			 * edge that no amount of walking removes. */
			ChunksX = Mathf.Max(1, Mathf.CeilToInt(worldRect.width / ChunkSize));
			ChunksZ = Mathf.Max(1, Mathf.CeilToInt(worldRect.height / ChunkSize));

			GridRect = new Rect(worldRect.xMin, worldRect.yMin, ChunksX * ChunkSize, ChunksZ * ChunkSize);

			// Unexplored is zero, which is what a new array already holds.
			Chunks = new byte[ChunksX * ChunksZ];
		}

		/// <summary>
		/// Rebuilds a map from chunks loaded off disk.
		/// </summary>
		/// <param name="worldRect">The world rectangle the chunks cover.</param>
		/// <param name="chunkSize">Length of one chunk's side in world metres.</param>
		/// <param name="chunks">The chunk data. Must be exactly the grid's size.</param>
		/// <returns>The loaded map, or null when the data does not match the grid.</returns>
		/// <remarks>
		/// <para>
		/// Returns null rather than resizing. A chunk array of the wrong length means the scene's
		/// bounds or chunk size changed since the file was written, and stretching old data across
		/// new bounds would put the player's explored ground in the wrong place — worse than
		/// starting again, because it looks plausible.
		/// </para>
		/// <para>
		/// Every byte is normalised to one of the two states rather than trusted. The file is
		/// signed, so this is not defending against tampering; it is making sure a future writer
		/// that stores something else in this array cannot leave a chunk that is neither explored
		/// nor unexplored, which nothing downstream has a meaning for.
		/// </para>
		/// </remarks>
		public static FogOfWarMap FromChunks(Rect worldRect, float chunkSize, byte[] chunks)
		{
			FogOfWarMap map = new FogOfWarMap(worldRect, chunkSize);
			if (chunks == null || chunks.Length != map.Chunks.Length)
			{
				return null;
			}

			int explored = 0;
			for (int i = 0; i < chunks.Length; ++i)
			{
				bool isExplored = chunks[i] != Unexplored;
				map.Chunks[i] = isExplored ? Explored : Unexplored;
				if (isExplored)
				{
					++explored;
				}
			}

			map.ExploredChunkCount = explored;
			return map;
		}

		/// <summary>
		/// Explores the chunk containing a world position.
		/// </summary>
		/// <param name="worldPosition">Where the character is, in world space.</param>
		/// <returns>True when this entered a chunk that had not been explored before.</returns>
		/// <remarks>
		/// Returning false is the ordinary case and says nothing is wrong: a character standing
		/// still, or crossing ground it has already walked, re-enters the same explored chunk
		/// several times a second. A position outside the grid also returns false — see
		/// <c>ClientMapSystem</c>, which reports that case rather than letting it pass unnoticed.
		/// </remarks>
		public bool Reveal(Vector3 worldPosition)
		{
			if (!TryGetChunk(worldPosition, out int x, out int z))
			{
				return false;
			}

			int index = (z * ChunksX) + x;
			if (Chunks[index] == Explored)
			{
				return false;
			}

			Chunks[index] = Explored;
			++ExploredChunkCount;
			IsDirty = true;
			textureDirty = true;
			return true;
		}

		/// <summary>
		/// Explores one chunk by its grid coordinates.
		/// </summary>
		/// <param name="chunkX">The chunk's X index.</param>
		/// <param name="chunkZ">The chunk's Z index.</param>
		/// <returns>True when that chunk had not been explored before.</returns>
		/// <remarks>
		/// The grain everything else here is built on, and the one to reach for when something
		/// other than a character's feet decides a chunk is known — a map fragment that names the
		/// chunks it fills in, a quest that opens up the valley it sends you to, a trigger volume
		/// at a vista. Out-of-range coordinates are ignored rather than throwing: a scene's grid
		/// changes size when its bounds are re-derived, and content that named a chunk beyond the
		/// edge should be inert, not fatal.
		/// </remarks>
		public bool RevealChunk(int chunkX, int chunkZ)
		{
			if (chunkX < 0 || chunkX >= ChunksX || chunkZ < 0 || chunkZ >= ChunksZ)
			{
				return false;
			}

			int index = (chunkZ * ChunksX) + chunkX;
			if (Chunks[index] == Explored)
			{
				return false;
			}

			Chunks[index] = Explored;
			++ExploredChunkCount;
			IsDirty = true;
			textureDirty = true;
			return true;
		}

		/// <summary>
		/// Explores every chunk that a world-space rectangle touches.
		/// </summary>
		/// <param name="worldArea">The rectangle, on the XZ plane, in world metres.</param>
		/// <returns>How many chunks this explored that were not explored already.</returns>
		/// <remarks>
		/// Touching, not covering. A rectangle that clips the corner of a chunk explores that whole
		/// chunk, because a chunk is the smallest thing this map can describe — asking for half of
		/// one is not a question it can answer, and rounding the other way would let an area
		/// smaller than a chunk reveal nothing at all.
		/// </remarks>
		public int RevealArea(Rect worldArea)
		{
			int minX = Mathf.FloorToInt((worldArea.xMin - WorldRect.xMin) / ChunkSize);
			int maxX = Mathf.FloorToInt((worldArea.xMax - WorldRect.xMin) / ChunkSize);
			int minZ = Mathf.FloorToInt((worldArea.yMin - WorldRect.yMin) / ChunkSize);
			int maxZ = Mathf.FloorToInt((worldArea.yMax - WorldRect.yMin) / ChunkSize);

			minX = Mathf.Max(0, minX);
			minZ = Mathf.Max(0, minZ);
			maxX = Mathf.Min(ChunksX - 1, maxX);
			maxZ = Mathf.Min(ChunksZ - 1, maxZ);

			int revealed = 0;
			for (int z = minZ; z <= maxZ; ++z)
			{
				for (int x = minX; x <= maxX; ++x)
				{
					if (RevealChunk(x, z))
					{
						++revealed;
					}
				}
			}

			return revealed;
		}

		/// <summary>
		/// Explores every chunk within a radius of a world position.
		/// </summary>
		/// <param name="worldCenter">Centre of the area, in world space.</param>
		/// <param name="radius">Radius in world metres. Zero or less explores nothing.</param>
		/// <returns>How many chunks this explored that were not explored already.</returns>
		/// <remarks>
		/// The shape a consumable naturally wants — "reveals the land within five hundred metres".
		/// A chunk counts when the circle reaches any part of it, so the result is a disc rounded
		/// outwards to the chunk grid rather than a square.
		/// </remarks>
		public int RevealAround(Vector3 worldCenter, float radius)
		{
			if (radius <= 0.0f)
			{
				return 0;
			}

			Rect bounds = new Rect(worldCenter.x - radius, worldCenter.z - radius, radius * 2.0f, radius * 2.0f);

			int minX = Mathf.Max(0, Mathf.FloorToInt((bounds.xMin - WorldRect.xMin) / ChunkSize));
			int maxX = Mathf.Min(ChunksX - 1, Mathf.FloorToInt((bounds.xMax - WorldRect.xMin) / ChunkSize));
			int minZ = Mathf.Max(0, Mathf.FloorToInt((bounds.yMin - WorldRect.yMin) / ChunkSize));
			int maxZ = Mathf.Min(ChunksZ - 1, Mathf.FloorToInt((bounds.yMax - WorldRect.yMin) / ChunkSize));

			float radiusSquared = radius * radius;
			int revealed = 0;

			for (int z = minZ; z <= maxZ; ++z)
			{
				float chunkMinZ = WorldRect.yMin + (z * ChunkSize);
				// Nearest point of this chunk to the centre, on each axis independently.
				float nearestZ = Mathf.Clamp(worldCenter.z, chunkMinZ, chunkMinZ + ChunkSize);
				float dz = nearestZ - worldCenter.z;

				for (int x = minX; x <= maxX; ++x)
				{
					float chunkMinX = WorldRect.xMin + (x * ChunkSize);
					float nearestX = Mathf.Clamp(worldCenter.x, chunkMinX, chunkMinX + ChunkSize);
					float dx = nearestX - worldCenter.x;

					if ((dx * dx) + (dz * dz) > radiusSquared)
					{
						continue;
					}

					if (RevealChunk(x, z))
					{
						++revealed;
					}
				}
			}

			return revealed;
		}

		/// <summary>
		/// Explores the entire map.
		/// </summary>
		/// <remarks>
		/// For scenes whose definition turns fog off, and for the map baker's preview. Cheaper and
		/// clearer than special-casing "no fog" at every draw site.
		/// </remarks>
		public void RevealAll()
		{
			for (int i = 0; i < Chunks.Length; ++i)
			{
				Chunks[i] = Explored;
			}

			ExploredChunkCount = Chunks.Length;
			IsDirty = true;
			textureDirty = true;
		}

		/// <summary>
		/// Whether a world position has been explored.
		/// </summary>
		/// <param name="worldPosition">The position to test.</param>
		/// <returns>True when the chunk holding the position has been entered.</returns>
		/// <remarks>
		/// A position outside the grid reads as explored. Off-map is not a place the player can
		/// discover, and reporting it as fogged would hide every marker that sits just outside a
		/// scene's derived bounds — which, since those bounds are a boundary volume plus padding,
		/// includes things a level designer legitimately put at the edge.
		/// </remarks>
		public bool IsDiscovered(Vector3 worldPosition)
		{
			if (!TryGetChunk(worldPosition, out int x, out int z))
			{
				return true;
			}

			return Chunks[(z * ChunksX) + x] == Explored;
		}

		/// <summary>
		/// The fraction of the scene that has been explored.
		/// </summary>
		/// <returns>Zero to one: explored chunks over total chunks.</returns>
		public float ExploredFraction()
		{
			return ExploredChunkCount / (float)Chunks.Length;
		}

		/// <summary>
		/// The chunk containing a world position.
		/// </summary>
		/// <param name="worldPosition">The position to convert.</param>
		/// <param name="x">The chunk's X index.</param>
		/// <param name="z">The chunk's Z index.</param>
		/// <returns>True when the position is inside the grid.</returns>
		public bool TryGetChunk(Vector3 worldPosition, out int x, out int z)
		{
			x = Mathf.FloorToInt((worldPosition.x - WorldRect.xMin) / ChunkSize);
			z = Mathf.FloorToInt((worldPosition.z - WorldRect.yMin) / ChunkSize);
			return x >= 0 && x < ChunksX && z >= 0 && z < ChunksZ;
		}

		/// <summary>
		/// Clears the dirty flag after the map has been written to disk.
		/// </summary>
		public void ClearDirty()
		{
			IsDirty = false;
		}

		/// <summary>
		/// The fog as a texture, alpha carrying the fog, rebuilt only when a chunk has changed.
		/// </summary>
		/// <returns>The fog texture. Never null once the map exists.</returns>
		/// <remarks>
		/// <para>RGBA rather than a single-channel format because UI Toolkit multiplies the sampled
		/// texel by the vertex colour: leaving the colour channels at white lets the fog be tinted
		/// to whatever suits the theme, where a single-channel texture would force it to be black.
		/// </para>
		/// <para><b>Point sampling, and that matters.</b> A chunk either has been visited or has
		/// not, and the edge between the two is a real boundary the player can walk across — so it
		/// is drawn as one. Interpolating would smear each chunk into its neighbours and produce a
		/// soft blob that no longer lines up with the ground it describes.</para>
		/// <para>The whole grid is uploaded on any change. It is a few hundred texels for a scene,
		/// so the dirty-rectangle bookkeeping the per-cell version needed buys nothing here.</para>
		/// </remarks>
		public Texture2D GetTexture()
		{
			if (texture == null)
			{
				texture = new Texture2D(ChunksX, ChunksZ, TextureFormat.RGBA32, false, true)
				{
					name = "FogOfWar",
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp,
					hideFlags = HideFlags.HideAndDontSave,
				};

				// A fresh texture holds nothing, so the grid has to go up regardless.
				textureDirty = true;
			}

			if (textureDirty)
			{
				if (pixelBuffer == null)
				{
					pixelBuffer = new Color32[Chunks.Length];
				}

				for (int i = 0; i < Chunks.Length; ++i)
				{
					pixelBuffer[i] = new Color32(255, 255, 255, Chunks[i] == Explored ? (byte)0 : (byte)255);
				}

				texture.SetPixels32(pixelBuffer);
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
