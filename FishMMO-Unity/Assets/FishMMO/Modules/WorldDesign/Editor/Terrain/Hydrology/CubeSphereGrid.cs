#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A whole sphere cut into nearly equal cells: a cube's six faces, each an N × N grid, pushed out
	/// onto the sphere with the equal-angle mapping. What planet-scale drainage runs on.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a cube and not latitude and longitude.</b> A latitude-longitude grid's cells shrink to
	/// slivers at the poles, so a river crossing one would take a thousand steps where it took one at
	/// the equator, and the eight-neighbour routing would favour east-west. The equal-angle cube keeps
	/// every cell within about 30% of every other's area and roughly square everywhere.
	/// </para>
	/// <para>
	/// <b>Neighbours across the seams.</b> A cell's eight neighbours are found by stepping one cell in
	/// its face's own coordinates and, past the face's edge, carrying the step on along the same great
	/// circle onto the next face (the equal-angle coordinate keeps going smoothly past ±45°) and asking
	/// which cell that direction falls in. The three cells at a cube corner meet at one point, so a
	/// corner cell has seven distinct neighbours rather than eight. Neighbour lists are symmetric.
	/// </para>
	/// <para>Pure arithmetic in double precision, no engine types: it runs and is tested outside Unity.</para>
	/// </remarks>
	public sealed class CubeSphereGrid
	{
		/// <summary>Cells along each face's edge.</summary>
		public readonly int N;
		/// <summary>Cells in all: 6 N².</summary>
		public readonly int Count;
		/// <summary>The sphere's radius, in metres.</summary>
		public readonly double RadiusMetres;

		/// <summary>Each cell's centre as a unit vector, three doubles per cell.</summary>
		public readonly double[] Centre;
		/// <summary>Each cell's area on the sphere, in m².</summary>
		public readonly double[] Area;
		/// <summary>Neighbour lists, compressed: cell c's are <see cref="Neighbours"/>[<see cref="NeighbourStart"/>[c] … NeighbourStart[c + 1]).</summary>
		public readonly int[] NeighbourStart;
		public readonly int[] Neighbours;
		/// <summary>Great-circle distance from each cell's centre to the matching neighbour's, in metres.</summary>
		public readonly float[] NeighbourMetres;

		// Each face: its outward normal, and the two axes its (u, v) run along. Right-handed, seen from outside.
		private static readonly double[,] Normal = { { 1, 0, 0 }, { -1, 0, 0 }, { 0, 1, 0 }, { 0, -1, 0 }, { 0, 0, 1 }, { 0, 0, -1 } };
		private static readonly double[,] AxisU = { { 0, 0, -1 }, { 0, 0, 1 }, { 1, 0, 0 }, { 1, 0, 0 }, { 1, 0, 0 }, { -1, 0, 0 } };
		private static readonly double[,] AxisV = { { 0, 1, 0 }, { 0, 1, 0 }, { 0, 0, -1 }, { 0, 0, 1 }, { 0, 1, 0 }, { 0, 1, 0 } };

		/// <summary>Builds the grid.</summary>
		/// <param name="n">Cells along a face's edge, at least 2.</param>
		/// <param name="radiusMetres">The sphere's radius.</param>
		public CubeSphereGrid(int n, double radiusMetres)
		{
			if (n < 2)
			{
				throw new ArgumentOutOfRangeException(nameof(n), "A face needs at least 2 cells a side.");
			}
			N = n;
			Count = 6 * n * n;
			RadiusMetres = radiusMetres;
			Centre = new double[Count * 3];
			Area = new double[Count];
			for (int c = 0; c < Count; c++)
			{
				Decompose(c, out int face, out int i, out int j);
				Direction(face, Angle(i + 0.5), Angle(j + 0.5), out Centre[c * 3], out Centre[c * 3 + 1], out Centre[c * 3 + 2]);
				Area[c] = CellArea(face, i, j) * radiusMetres * radiusMetres;
			}

			// Each cell's distinct neighbours, then every link made two-way, then flattened.
			var lists = new List<int>[Count];
			for (int c = 0; c < Count; c++)
			{
				Decompose(c, out int face, out int i, out int j);
				var found = new List<int>(8);
				for (int dj = -1; dj <= 1; dj++)
				{
					for (int di = -1; di <= 1; di++)
					{
						if (di == 0 && dj == 0)
						{
							continue;
						}
						int ni = i + di, nj = j + dj;
						int m;
						if (ni >= 0 && nj >= 0 && ni < n && nj < n)
						{
							m = Index(face, ni, nj);
						}
						else
						{
							Direction(face, Angle(ni + 0.5), Angle(nj + 0.5), out double x, out double y, out double z);
							m = CellOf(x, y, z);
						}
						if (m != c && !found.Contains(m))
						{
							found.Add(m);
						}
					}
				}
				lists[c] = found;
			}
			for (int c = 0; c < Count; c++)
			{
				foreach (int m in lists[c].ToArray())
				{
					if (!lists[m].Contains(c))
					{
						lists[m].Add(c);
					}
				}
			}
			NeighbourStart = new int[Count + 1];
			int total = 0;
			for (int c = 0; c < Count; c++)
			{
				NeighbourStart[c] = total;
				total += lists[c].Count;
			}
			NeighbourStart[Count] = total;
			Neighbours = new int[total];
			for (int c = 0; c < Count; c++)
			{
				lists[c].CopyTo(Neighbours, NeighbourStart[c]);
			}

			NeighbourMetres = new float[Neighbours.Length];
			for (int c = 0; c < Count; c++)
			{
				for (int k = NeighbourStart[c]; k < NeighbourStart[c + 1]; k++)
				{
					NeighbourMetres[k] = (float)(ArcBetween(c, Neighbours[k]) * radiusMetres);
				}
			}
		}

		/// <summary>The face size for cells of about <paramref name="cellMetres"/> on a sphere of <paramref name="radiusMetres"/>, capped.</summary>
		public static int FaceCellsFor(double radiusMetres, double cellMetres, int maximum)
		{
			// A face's edge spans a quarter of a great circle.
			int n = (int)Math.Ceiling(0.5 * Math.PI * radiusMetres / Math.Max(1.0, cellMetres));
			return Math.Max(2, Math.Min(maximum, n));
		}

		/// <summary>The cell a direction falls in. The direction need not be normalised.</summary>
		public int CellOf(double x, double y, double z)
		{
			double ax = Math.Abs(x), ay = Math.Abs(y), az = Math.Abs(z);
			int face;
			if (ax >= ay && ax >= az)
			{
				face = x >= 0.0 ? 0 : 1;
			}
			else if (ay >= az)
			{
				face = y >= 0.0 ? 2 : 3;
			}
			else
			{
				face = z >= 0.0 ? 4 : 5;
			}
			double major = x * Normal[face, 0] + y * Normal[face, 1] + z * Normal[face, 2];
			double u = (x * AxisU[face, 0] + y * AxisU[face, 1] + z * AxisU[face, 2]) / major;
			double v = (x * AxisV[face, 0] + y * AxisV[face, 1] + z * AxisV[face, 2]) / major;
			int i = Clamp((int)Math.Floor((Math.Atan(u) + Math.PI * 0.25) / (Math.PI * 0.5) * N), 0, N - 1);
			int j = Clamp((int)Math.Floor((Math.Atan(v) + Math.PI * 0.25) / (Math.PI * 0.5) * N), 0, N - 1);
			return Index(face, i, j);
		}

		/// <summary>Great-circle angle between two cells' centres, in radians.</summary>
		public double ArcBetween(int a, int b)
		{
			double dot = Centre[a * 3] * Centre[b * 3] + Centre[a * 3 + 1] * Centre[b * 3 + 1] + Centre[a * 3 + 2] * Centre[b * 3 + 2];
			return Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot)));
		}

		/// <summary>The face and the cell's column and row on it.</summary>
		public void Decompose(int cell, out int face, out int i, out int j)
		{
			face = cell / (N * N);
			int r = cell - face * N * N;
			j = r / N;
			i = r - j * N;
		}

		public int Index(int face, int i, int j) => face * N * N + j * N + i;

		/// <summary>Equal-angle coordinate of a position along a face, in cells, as the tangent the face plane is cut at.</summary>
		private double Angle(double cells) => Math.Tan(cells / N * Math.PI * 0.5 - Math.PI * 0.25);

		private static void Direction(int face, double u, double v, out double x, out double y, out double z)
		{
			x = Normal[face, 0] + u * AxisU[face, 0] + v * AxisV[face, 0];
			y = Normal[face, 1] + u * AxisU[face, 1] + v * AxisV[face, 1];
			z = Normal[face, 2] + u * AxisU[face, 2] + v * AxisV[face, 2];
			double length = Math.Sqrt(x * x + y * y + z * z);
			x /= length;
			y /= length;
			z /= length;
		}

		/// <summary>A cell's solid angle: its four corners as two spherical triangles.</summary>
		private double CellArea(int face, int i, int j)
		{
			Direction(face, Angle(i), Angle(j), out double ax, out double ay, out double az);
			Direction(face, Angle(i + 1), Angle(j), out double bx, out double by, out double bz);
			Direction(face, Angle(i + 1), Angle(j + 1), out double cx, out double cy, out double cz);
			Direction(face, Angle(i), Angle(j + 1), out double dx, out double dy, out double dz);
			return Triangle(ax, ay, az, bx, by, bz, cx, cy, cz) + Triangle(ax, ay, az, cx, cy, cz, dx, dy, dz);
		}

		/// <summary>Solid angle of a spherical triangle (Van Oosterom &amp; Strackee).</summary>
		private static double Triangle(double ax, double ay, double az, double bx, double by, double bz, double cx, double cy, double cz)
		{
			double triple = ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx);
			double denominator = 1.0 + (ax * bx + ay * by + az * bz) + (bx * cx + by * cy + bz * cz) + (cx * ax + cy * ay + cz * az);
			return Math.Abs(2.0 * Math.Atan2(triple, denominator));
		}

		private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
	}
}
#endif
