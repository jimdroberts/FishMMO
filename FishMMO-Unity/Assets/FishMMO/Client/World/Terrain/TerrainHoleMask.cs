using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// A terrain's painted holes (<see cref="TerrainData.GetHoles"/>) as the detail renderers read them: the blade grass
	/// (folded into its water line, <see cref="GrassTerrain.Freeboard"/>), the GPU detail scatter (a mask slice beside its
	/// heights; every mesh kind since the CPU chunk renderer went, 2026-10-27). Unity's own detail drawing is off on every
	/// terrain they drive, so nothing else keeps a plant out of a cave mouth: the detail layers under a hole still hold
	/// whatever the biome scatter painted before the hole was cut, and the 2 m density texels and scatter blocks reach
	/// past the hole's edge even where they do not.
	/// </summary>
	/// <remarks>
	/// One cell per heightmap cell (<see cref="TerrainData.holesResolution"/> = heightmap resolution − 1), cell (x, z)
	/// spanning heightmap samples x..x+1 and z..z+1; the terrain shader clips a whole texel (ClipHoles), so a point test
	/// of the cell under a root matches the drawn edge.
	/// </remarks>
	public sealed class TerrainHoleMask
	{
		/// <summary>Rows read per <see cref="ReadStrip"/>, as the builders read their other layers.</summary>
		public const int StripRows = 64;

		/// <summary>Cells per side.</summary>
		public readonly int Resolution;

		/// <summary>True where the ground is solid, row z first (<see cref="TerrainData.GetHoles"/>'s sense).</summary>
		private readonly bool[] solid;
		private int rowsRead;

		/// <summary>True once any cell read so far is a hole.</summary>
		public bool Any { get; private set; }

		/// <summary>True once every row has been read.</summary>
		public bool Complete => rowsRead >= Resolution;

		/// <summary>One cell's side, metres (the terrain's width over <see cref="Resolution"/>).</summary>
		public readonly float CellMetres;

		/// <summary>
		/// How far past a hole's edge no plant roots, metres (2026-10-27, Moron 2: "the ground details don't overlap the hole
		/// itself"): the 2 m density texels and scatter blocks spread what the cells beside an opening hold up to its edge, and a
		/// blade, a bush or a scattered stone rooted on that edge leans or spreads out over the opening. The cut clears the detail
		/// cells round every cave opening wider still (PointOfInterestCaveOpeningClearance.DetailMargin); this is the plants' own
		/// margin at every hole, which also keeps them out from under a canyon wall's or a cloud island's rim of rock.
		/// </summary>
		public const float PlantClearMetres = 1.5f;

		public TerrainHoleMask(TerrainData data)
		{
			Resolution = Mathf.Max(1, data.holesResolution);
			solid = new bool[Resolution * Resolution];
			CellMetres = data.size.x / Resolution;
		}

		/// <summary>
		/// Counts every cell within <see cref="PlantClearMetres"/> of a hole as one (in place, once every row is read): the mask
		/// the plant builders read (the blade grass and the GPU detail scatter), never the terrain's own holes,
		/// which the clouds and the ambient life read through a mask of their own.
		/// </summary>
		public void KeepPlantsClear() => KeepClear(solid, Resolution, CellMetres, PlantClearMetres);

		/// <summary>
		/// Turns every solid cell whose square comes within <paramref name="metres"/> of a hole cell's square into a hole, in place
		/// (<paramref name="solid"/> row z first, true solid; the holes as they were before the call decide, so it never spreads
		/// further). Pure.
		/// </summary>
		public static void KeepClear(bool[] solid, int resolution, float cellMetres, float metres)
		{
			if (solid == null || resolution <= 0 || cellMetres <= 0f || metres <= 0f)
			{
				return;
			}
			int reach = Mathf.CeilToInt(metres / cellMetres);
			float most = metres * metres;
			var holes = new System.Collections.Generic.List<int>();
			for (int i = 0; i < solid.Length && i < resolution * resolution; i++)
			{
				if (!solid[i])
				{
					holes.Add(i);
				}
			}
			foreach (int hole in holes)
			{
				int hx = hole % resolution, hz = hole / resolution;
				for (int dz = -reach; dz <= reach; dz++)
				{
					int z = hz + dz;
					if (z < 0 || z >= resolution)
					{
						continue;
					}
					// The gap between the two cells' squares, metres.
					float gz = Mathf.Max(0, Mathf.Abs(dz) - 1) * cellMetres;
					for (int dx = -reach; dx <= reach; dx++)
					{
						int x = hx + dx;
						if (x < 0 || x >= resolution)
						{
							continue;
						}
						float gx = Mathf.Max(0, Mathf.Abs(dx) - 1) * cellMetres;
						if (gx * gx + gz * gz <= most)
						{
							solid[z * resolution + x] = false;
						}
					}
				}
			}
		}

		/// <summary>The whole mask at once; null when the terrain has no hole.</summary>
		public static TerrainHoleMask Read(TerrainData data)
		{
			if (data == null)
			{
				return null;
			}
			var mask = new TerrainHoleMask(data);
			while (!mask.Complete)
			{
				mask.ReadStrip(data);
			}
			return mask.Any ? mask : null;
		}

		/// <summary><see cref="Read"/> with the plants' margin round every hole (<see cref="KeepPlantsClear"/>); null when the terrain has no hole.</summary>
		public static TerrainHoleMask ReadForPlants(TerrainData data)
		{
			TerrainHoleMask mask = Read(data);
			mask?.KeepPlantsClear();
			return mask;
		}

		/// <summary>Reads the next <see cref="StripRows"/> rows.</summary>
		public void ReadStrip(TerrainData data)
		{
			int rows = Mathf.Min(StripRows, Resolution - rowsRead);
			if (rows <= 0)
			{
				return;
			}
			bool[,] strip = data.GetHoles(0, rowsRead, Resolution, rows);
			for (int r = 0; r < rows; r++)
			{
				int at = (rowsRead + r) * Resolution;
				for (int x = 0; x < Resolution; x++)
				{
					bool s = strip[r, x];
					solid[at + x] = s;
					Any |= !s;
				}
			}
			rowsRead += rows;
		}

		/// <summary>Whether cell (x, z) is a hole (clamped to the terrain).</summary>
		public bool HoleAtCell(int x, int z)
		{
			x = Mathf.Clamp(x, 0, Resolution - 1);
			z = Mathf.Clamp(z, 0, Resolution - 1);
			return !solid[z * Resolution + x];
		}

		/// <summary>Whether the point at normalised terrain coordinates (0..1 across each side) lies in a hole.</summary>
		public bool HoleAt(float nx, float nz) => HoleAtCell(Mathf.FloorToInt(nx * Resolution), Mathf.FloorToInt(nz * Resolution));

		/// <summary>
		/// Whether heightmap sample (x, z) is a corner of any hole cell: the samples whose water line the grass pulls
		/// under so its bilinear zero falls on the hole's edge.
		/// </summary>
		public bool HoleAtSample(int x, int z)
		{
			bool left = x > 0, right = x < Resolution, below = z > 0, above = z < Resolution;
			return left && below && HoleAtCell(x - 1, z - 1)
				|| right && below && HoleAtCell(x, z - 1)
				|| left && above && HoleAtCell(x - 1, z)
				|| right && above && HoleAtCell(x, z);
		}

		/// <summary>
		/// The mask as R8 texels at the heightmap's resolution (cell (x, z) at texel (x, z), 0 a hole, 255 solid; the last
		/// row and column solid), the detail scatter's slice.
		/// </summary>
		public byte[] ToTexels(int heightResolution)
		{
			var texels = new byte[heightResolution * heightResolution];
			for (int z = 0; z < heightResolution; z++)
			{
				for (int x = 0; x < heightResolution; x++)
				{
					texels[z * heightResolution + x] = x < Resolution && z < Resolution && !solid[z * Resolution + x] ? (byte)0 : (byte)255;
				}
			}
			return texels;
		}
	}
}
