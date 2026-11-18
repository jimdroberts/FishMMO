#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class CaveSystem
	{
		// ── Where the air is (2026-10-21) ─────────────────

		/// <summary>How near the void a rock vertex lies to be on an inner face (one seen from inside), m.</summary>
		private const float InnerFaceBand = 0.6f;

		/// <summary>
		/// The cave's columns (<see cref="CaveEnclosure"/>) from its rock as the cut ships it (<paramref name="pieces"/>: each
		/// piece's positions in the system's frame and its triangles): per world column, the lowest and highest of the inner
		/// faces over it (every face whose corners all lie on the void's skin, so the shell's outer faces, a shell's thickness
		/// into the hill, are left out), and how shut in from the daylight it is (<see cref="ClosureAt"/>).
		/// </summary>
		public CaveEnclosure EnclosureOf(IEnumerable<(IList<Vector3> Positions, IList<int> Triangles)> pieces)
		{
			Bounds e = Extent;
			e.center += Origin;
			e.Expand(8f);
			int x0 = CaveEnclosure.ColumnOf(e.min.x), z0 = CaveEnclosure.ColumnOf(e.min.z);
			int width = CaveEnclosure.ColumnOf(e.max.x) - x0 + 1, depth = CaveEnclosure.ColumnOf(e.max.z) - z0 + 1;
			var low = new float[width * depth];
			var high = new float[width * depth];
			for (int i = 0; i < low.Length; i++)
			{
				low[i] = float.MaxValue;
				high[i] = float.MinValue;
			}
			float band = Mathf.Min(InnerFaceBand, 0.5f * ShellDeep);
			var inner = new List<bool>();
			foreach ((IList<Vector3> positions, IList<int> triangles) in pieces)
			{
				if (positions == null || triangles == null)
				{
					continue;
				}
				inner.Clear();
				for (int v = 0; v < positions.Count; v++)
				{
					// The void is negative in the air: an inner face's corners lie on its skin, an outer face's a shell's thickness into the rock.
					inner.Add(Void(positions[v]) < band);
				}
				for (int t = 0; t + 2 < triangles.Count; t += 3)
				{
					int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
					if (!inner[a] || !inner[b] || !inner[c])
					{
						continue;
					}
					Vector3 pa = positions[a] + Origin, pb = positions[b] + Origin, pc = positions[c] + Origin;
					float yMin = Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y)), yMax = Mathf.Max(pa.y, Mathf.Max(pb.y, pc.y));
					int cx0 = CaveEnclosure.ColumnOf(Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x))) - x0;
					int cx1 = CaveEnclosure.ColumnOf(Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x))) - x0;
					int cz0 = CaveEnclosure.ColumnOf(Mathf.Min(pa.z, Mathf.Min(pb.z, pc.z))) - z0;
					int cz1 = CaveEnclosure.ColumnOf(Mathf.Max(pa.z, Mathf.Max(pb.z, pc.z))) - z0;
					for (int z = Mathf.Max(0, cz0); z <= Mathf.Min(depth - 1, cz1); z++)
					{
						for (int x = Mathf.Max(0, cx0); x <= Mathf.Min(width - 1, cx1); x++)
						{
							int i = z * width + x;
							low[i] = Mathf.Min(low[i], yMin);
							high[i] = Mathf.Max(high[i], yMax);
						}
					}
				}
			}

			// Trimmed to the columns that hold rock.
			int minX = width, maxX = -1, minZ = depth, maxZ = -1;
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					if (low[z * width + x] <= high[z * width + x])
					{
						minX = Mathf.Min(minX, x);
						maxX = Mathf.Max(maxX, x);
						minZ = Mathf.Min(minZ, z);
						maxZ = Mathf.Max(maxZ, z);
					}
				}
			}
			if (maxX < 0)
			{
				return new CaveEnclosure(x0, z0, 0, 0);
			}
			var columns = new CaveEnclosure(x0 + minX, z0 + minZ, maxX - minX + 1, maxZ - minZ + 1);
			for (int z = minZ; z <= maxZ; z++)
			{
				for (int x = minX; x <= maxX; x++)
				{
					int i = z * width + x;
					if (low[i] <= high[i])
					{
						float cx = (x0 + x + 0.5f) * CaveEnclosure.Cell, cz = (z0 + z + 0.5f) * CaveEnclosure.Cell;
						columns.Set(x0 + x, z0 + z, low[i], high[i], ClosureAt(cx, cz, low[i], high[i]));
					}
				}
			}
			return columns;
		}

		/// <summary>
		/// How shut in from the daylight a column is (world x, z and its span), 0..1: 0 near a mouth, rising to 1 past where its
		/// roof may still be thin (<see cref="CaveMouth.Zone"/>) and a little more; 0 under a skylight and its rim, rising to 1 a
		/// few metres off it. There the darkness is as it was, by how far under the ground a surface lies, and the sky an
		/// opening lets in (FishSkylights.hlsl).
		/// </summary>
		public float ClosureAt(float x, float z, float floor, float roof)
		{
			float shut = 1f;
			foreach (CaveMouth m in Mouths)
			{
				Vector3 at = m.Floor + Origin;
				float w;
				if (m.Kind == CaveMouthKind.Skylight)
				{
					float across = new Vector2(x - at.x, z - at.z).magnitude;
					float r = 1.5f * m.Radius;
					w = Smooth(r + 1f, r + 9f, across);
				}
				else
				{
					float d = (new Vector3(x, Mathf.Clamp(at.y, floor, roof), z) - at).magnitude;
					w = Smooth(m.Radius + 2f, m.Zone + 2f * m.Radius + 12f, d);
				}
				shut = Mathf.Min(shut, w);
			}
			return shut;
		}

		private static float Smooth(float a, float b, float x)
		{
			float t = Mathf.Clamp01((x - a) / Mathf.Max(1e-3f, b - a));
			return t * t * (3f - 2f * t);
		}
	}
}
#endif
