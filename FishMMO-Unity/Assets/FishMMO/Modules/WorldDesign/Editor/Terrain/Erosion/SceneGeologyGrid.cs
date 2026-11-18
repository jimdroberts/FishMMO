#if UNITY_EDITOR
using System;
using System.Threading.Tasks;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The planet's rock under every sample of a scene's height grid, so erosion can ask a cell's
	/// hardness at any height for a little arithmetic.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Few columns, many cells.</b> Working out a <see cref="GeologyColumn"/> means finding the
	/// province among 27 Voronoi cells and placing the beds' plane, too slow to do for millions of
	/// samples. Inside a province, though, a column's offset is the beds' plane plus a wander
	/// hundreds of metres across — so columns are worked out every <see cref="CoarseCells"/> samples
	/// and their offsets interpolated between, which is exact for the plane and close for the
	/// wander. Only where the four columns around a sample disagree about the province, a thin band
	/// along each contact, is the sample's own column worked out.
	/// </para>
	/// <para>Built in parallel; read-only and safe to share afterwards.</para>
	/// </remarks>
	public sealed class SceneGeologyGrid
	{
		/// <summary>Samples between the columns worked out in full.</summary>
		public const int CoarseCells = 8;

		private readonly GeologyColumn[] columns;
		private readonly int[] columnOf;
		private readonly float[] offset;

		/// <summary>How many samples needed their own column because a province edge crosses them.</summary>
		public readonly int ExactSamples;

		private SceneGeologyGrid(GeologyColumn[] columns, int[] columnOf, float[] offset, int exact)
		{
			this.columns = columns;
			this.columnOf = columnOf;
			this.offset = offset;
			ExactSamples = exact;
		}

		/// <summary>Hardness, 0 … 1, of the rock at a sample at an altitude in scene metres.</summary>
		public float HardnessAt(int sample, float altitudeMetres)
		{
			return columns[columnOf[sample]].HardnessAtStrata(altitudeMetres + offset[sample]);
		}

		/// <summary>Metres added to an altitude at a sample to place it in its rock's stack of beds (interpolated within a province).</summary>
		public float OffsetOf(int sample) => offset[sample];

		/// <summary>The rock under a sample (its offset is the column's own, not the interpolated one).</summary>
		public GeologyColumn ColumnOf(int sample) => columns[columnOf[sample]];

		/// <summary>Reads the rock under every sample of <paramref name="field"/>.</summary>
		/// <param name="columnAt">The column at a scene position (east, north): <see cref="SceneTerrainProcess.ColumnAt"/>. Must be safe to call from many threads.</param>
		public static SceneGeologyGrid Build(SceneHeightField field, Func<float, float, GeologyColumn> columnAt)
		{
			int width = field.Width, depth = field.Depth;
			int coarseWidth = (width - 1) / CoarseCells + 2;
			int coarseDepth = (depth - 1) / CoarseCells + 2;

			var coarse = new GeologyColumn[coarseWidth * coarseDepth];
			Parallel.For(0, coarseDepth, b =>
			{
				float north = field.NorthOf(Math.Min(b * CoarseCells, depth - 1));
				for (int a = 0; a < coarseWidth; a++)
				{
					coarse[b * coarseWidth + a] = columnAt(field.EastOf(Math.Min(a * CoarseCells, width - 1)), north);
				}
			});

			var columnOf = new int[width * depth];
			var offset = new float[width * depth];
			var exactPerRow = new int[depth];

			// Interpolated wherever the four columns around a sample are one province; marked otherwise.
			Parallel.For(0, depth, z =>
			{
				int b = z / CoarseCells;
				float tz = (z - b * CoarseCells) / (float)CoarseCells;
				int count = 0;
				for (int x = 0; x < width; x++)
				{
					int a = x / CoarseCells;
					float tx = (x - a * CoarseCells) / (float)CoarseCells;
					int i00 = b * coarseWidth + a, i10 = i00 + 1, i01 = i00 + coarseWidth, i11 = i01 + 1;
					int province = coarse[i00].Province;
					int i = z * width + x;
					if (coarse[i10].Province == province && coarse[i01].Province == province && coarse[i11].Province == province)
					{
						float south = coarse[i00].Offset + (coarse[i10].Offset - coarse[i00].Offset) * tx;
						float north = coarse[i01].Offset + (coarse[i11].Offset - coarse[i01].Offset) * tx;
						offset[i] = south + (north - south) * tz;
						columnOf[i] = i00;
					}
					else
					{
						columnOf[i] = -1;
						count++;
					}
				}
				exactPerRow[z] = count;
			});

			// Where a contact crosses: each such sample's own column, appended after the coarse ones.
			var rowStart = new int[depth];
			int exact = 0;
			for (int z = 0; z < depth; z++)
			{
				rowStart[z] = exact;
				exact += exactPerRow[z];
			}
			var columns = new GeologyColumn[coarse.Length + exact];
			Array.Copy(coarse, columns, coarse.Length);
			Parallel.For(0, depth, z =>
			{
				if (exactPerRow[z] == 0)
				{
					return;
				}
				int next = coarse.Length + rowStart[z];
				float north = field.NorthOf(z);
				for (int x = 0; x < width; x++)
				{
					int i = z * width + x;
					if (columnOf[i] >= 0)
					{
						continue;
					}
					GeologyColumn own = columnAt(field.EastOf(x), north);
					columns[next] = own;
					columnOf[i] = next;
					offset[i] = own.Offset;
					next++;
				}
			});
			return new SceneGeologyGrid(columns, columnOf, offset, exact);
		}
	}
}
#endif
