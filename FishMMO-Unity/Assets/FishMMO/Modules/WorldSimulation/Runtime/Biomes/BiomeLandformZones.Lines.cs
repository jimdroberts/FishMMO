using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The headings the scatter's facing modes read off the stored landforms (2026-10-26, phase SCATTER-MODES, decision 34): the nearest
	/// stored line of a biome (decision 90: aligned along a landform line) and the axis of the landform under a point (decision 85: a
	/// tessera weave's strike). Pure, thread-safe once built, like the masks.
	/// </summary>
	public sealed partial class BiomeLandformZones
	{
		/// <summary>
		/// The heading (Unity's turn, radians: +Z along it) of the nearest segment of the biome's stored landform lines within
		/// <paramref name="reach"/> of (x, z) and inside the line's landform box; false when none is.
		/// </summary>
		public bool LineHeading(int biomeID, float x, float z, float reach, out float yaw)
		{
			yaw = 0f;
			if (!buckets.TryGetValue(KeyOf(x, z), out List<int> near))
			{
				return false;
			}
			var p = new Vector2(x, z);
			float best = reach * reach;
			bool found = false;
			foreach (int i in near)
			{
				BiomeLandformSite site = shapes[i].Site;
				if (site.BiomeID != biomeID || site.Segments == null)
				{
					continue;
				}
				foreach (BiomeLandformSegment seg in site.Segments)
				{
					Vector2 ab = seg.B - seg.A;
					float length2 = ab.sqrMagnitude;
					if (length2 < 1e-6f)
					{
						continue;
					}
					float t = Mathf.Clamp01(Vector2.Dot(p - seg.A, ab) / length2);
					float d2 = (seg.A + ab * t - p).sqrMagnitude;
					if (d2 <= best)
					{
						best = d2;
						yaw = BiomeScatterModes.YawOf(ab);
						found = true;
					}
				}
			}
			return found;
		}

		/// <summary>
		/// The axis heading (Unity's turn, radians) of the biome's landform whose box holds (x, z), the one whose centre is nearest when boxes
		/// overlap (a weave's fins run along its axis: its strike); false over none.
		/// </summary>
		public bool StrikeHeading(int biomeID, float x, float z, out float yaw)
		{
			yaw = 0f;
			if (!buckets.TryGetValue(KeyOf(x, z), out List<int> near))
			{
				return false;
			}
			float best = float.PositiveInfinity;
			bool found = false;
			foreach (int i in near)
			{
				BiomeLandformSite site = shapes[i].Site;
				Vector4 b = site.Bounds;
				if (site.BiomeID != biomeID || x < b.x || x > b.z || z < b.y || z > b.w)
				{
					continue;
				}
				float d2 = (site.Centre - new Vector2(x, z)).sqrMagnitude;
				if (d2 < best)
				{
					best = d2;
					yaw = BiomeScatterModes.YawOfLandform(site.Yaw);
					found = true;
				}
			}
			return found;
		}

		private static long KeyOf(float x, float z) => ((long)Mathf.FloorToInt(z / BucketMetres) << 32) ^ (uint)Mathf.FloorToInt(x / BucketMetres);
	}
}
