using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The lava that runs along a scene's molten landform lines and stands in its lava moats (2026-10-25, phase I2r "molten ground",
	/// decision 65 (c) and 95): which lines the cut stores (<see cref="FromLandforms"/>, on <see cref="SceneBiomeMap.LavaLines"/>), and
	/// the ribbon a client draws along each with the lava material (<see cref="Ribbon"/>), laid on the carved ground. Pure: the same
	/// lines and the same ribbon for every player; presentation only (the ground was carved by the landform, nothing collides).
	/// </summary>
	public static class BiomeLavaChannels
	{
		/// <summary>The share of a carved channel's half-width the lava fills.</summary>
		public const float ChannelShare = 0.5f;
		/// <summary>The share of a tongue's half-width its central channel's lava takes when the tongue names no crown.</summary>
		public const float TongueShare = 0.15f;
		/// <summary>The narrowest lava ribbon drawn, m (half-width).</summary>
		public const float MinimumHalf = 0.4f;
		/// <summary>How high over the ground under its middle a ribbon is laid, m.</summary>
		public const float LiftMetres = 0.12f;
		/// <summary>How far apart a ribbon's rows are along its line, m (at most).</summary>
		public const float RowMetres = 2f;
		/// <summary>The most rows one ribbon has (a very long line is drawn coarser rather than longer).</summary>
		public const int MaximumRows = 2048;
		/// <summary>The share of a lava moat's width (its level floor and the bank's foot) the lava fills (MoatShaper.FloorShare and a little more).</summary>
		public const float MoatShare = 0.3f;
		/// <summary>The segments a lava moat's ring is drawn in.</summary>
		public const int MoatSpokes = 72;
		/// <summary>A lava moat's flag in its stored moat shape (MoatShaper values[4]: 0 dry, 1 water, this lava).</summary>
		public const float MoatLavaFlag = 2f;

		/// <summary>
		/// The lava lines of a scene's landforms: every segment of every site whose entry is <see cref="BiomeLandform.Molten"/>, half
		/// as wide as its channel (<see cref="ChannelShare"/>); a site with no segments (a tongue) its axis, centre ± half its length,
		/// its central channel wide (its crown's share of its half-width, else <see cref="TongueShare"/>). Empty when none is molten.
		/// </summary>
		public static List<VentLine> FromLandforms(IReadOnlyList<BiomeLandformSite> sites)
		{
			var lines = new List<VentLine>();
			if (sites == null)
			{
				return lines;
			}
			foreach (BiomeLandformSite site in sites)
			{
				if (!site.Form.Molten)
				{
					continue;
				}
				if (site.Segments != null && site.Segments.Count > 0)
				{
					foreach (BiomeLandformSegment segment in site.Segments)
					{
						lines.Add(new VentLine
						{
							A = segment.A,
							B = segment.B,
							AlongA = segment.AlongA,
							LineLength = Mathf.Max(segment.LineLength, segment.AlongB),
							Half = Mathf.Max(MinimumHalf, segment.Half * ChannelShare),
							Seed = site.Seed,
							BiomeID = site.BiomeID,
							Layout = BiomeVentLayout.Grid,
						});
					}
					continue;
				}
				float length = Mathf.Max(site.HalfLength, site.Radius);
				if (length <= 0f)
				{
					continue;
				}
				var axis = new Vector2(Mathf.Cos(site.Yaw * Mathf.Deg2Rad), Mathf.Sin(site.Yaw * Mathf.Deg2Rad));
				float share = site.Form.Crown > 0f ? site.Form.Crown : TongueShare;
				lines.Add(new VentLine
				{
					A = site.Centre - axis * length,
					B = site.Centre + axis * length,
					AlongA = 0f,
					LineLength = 2f * length,
					Half = Mathf.Max(MinimumHalf, Mathf.Max(site.HalfWidth, site.Radius) * share),
					Seed = site.Seed,
					BiomeID = site.BiomeID,
					Layout = BiomeVentLayout.Grid,
				});
			}
			return lines;
		}

		/// <summary>
		/// A lava ribbon along one stored line, laid on the ground (<paramref name="ground"/> world y at world xz; NaN off the ground):
		/// rows every <see cref="RowMetres"/> or less, each two vertices across the line at its half-width, <see cref="LiftMetres"/>
		/// over the ground under the row's middle and never under the ground at either edge. Into the lists: positions (world),
		/// frames (metres along the whole line, metres across: −half … +half), indices. False when the line is a point or off the ground.
		/// </summary>
		public static bool Ribbon(in VentLine line, Func<float, float, float> ground, List<Vector3> positions, List<Vector2> frames, List<int> indices)
		{
			Vector2 d = line.B - line.A;
			float length = d.magnitude;
			if (length < 1e-3f || ground == null)
			{
				return false;
			}
			Vector2 along = d / length;
			var across = new Vector2(-along.y, along.x);
			int rows = Mathf.Clamp(Mathf.CeilToInt(length / RowMetres), 1, MaximumRows) + 1;
			float half = Mathf.Max(MinimumHalf, line.Half);
			int first = positions.Count;
			for (int r = 0; r < rows; r++)
			{
				float t = r / (float)(rows - 1);
				Vector2 c = line.A + d * t;
				Vector2 left = c - across * half, right = c + across * half;
				float mid = ground(c.x, c.y);
				if (float.IsNaN(mid))
				{
					positions.RemoveRange(first, positions.Count - first);
					frames.RemoveRange(first, frames.Count - first);
					return false;
				}
				float gl = ground(left.x, left.y), gr = ground(right.x, right.y);
				float y = mid + LiftMetres;
				if (!float.IsNaN(gl))
				{
					y = Mathf.Max(y, gl + 0.02f);
				}
				if (!float.IsNaN(gr))
				{
					y = Mathf.Max(y, gr + 0.02f);
				}
				// A channel's lava stands level across it: the higher bank's foot never shows through.
				y = Mathf.Min(y, mid + Mathf.Max(LiftMetres, 0.5f * half));
				float metres = line.AlongA + length * t;
				positions.Add(new Vector3(left.x, y, left.y));
				positions.Add(new Vector3(right.x, y, right.y));
				frames.Add(new Vector2(metres, -half));
				frames.Add(new Vector2(metres, half));
			}
			for (int r = 0; r + 1 < rows; r++)
			{
				int a = first + 2 * r;
				indices.Add(a); indices.Add(a + 2); indices.Add(a + 1);
				indices.Add(a + 1); indices.Add(a + 2); indices.Add(a + 3);
			}
			return true;
		}

		/// <summary>
		/// The lava lines of a lava moat (its stored ring: centre, inner bank reach per bearing round it, the widest inner reach and the
		/// ditch's width): one segment per pair of neighbouring bearings round its middle, the lava <see cref="MoatShare"/> of the width.
		/// </summary>
		public static List<VentLine> MoatLines(Vector3 centre, float[] innerReach, float innerMost, float width, uint seed, int biomeID)
		{
			var lines = new List<VentLine>();
			const int spokes = MoatSpokes;
			float half = Mathf.Max(MinimumHalf, 0.5f * MoatShare * width);
			float along = 0f;
			Vector2 c = new Vector2(centre.x, centre.z);
			Vector2 Point(int i)
			{
				// Round the ring's middle, the inner bank's reach per bearing as the moat shaper measures it (x sin, z cos).
				float a = (i % spokes) * Mathf.PI * 2f / spokes;
				var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
				float inner = PointOfInterestOutline.Reach(innerReach, innerMost, dir.x, dir.y);
				return c + dir * (inner + 0.5f * width);
			}
			float total = 0f;
			for (int i = 0; i < spokes; i++)
			{
				total += Vector2.Distance(Point(i), Point(i + 1));
			}
			for (int i = 0; i < spokes; i++)
			{
				Vector2 a = Point(i), b = Point(i + 1);
				lines.Add(new VentLine { A = a, B = b, AlongA = along, LineLength = total, Half = half, Seed = seed, BiomeID = biomeID, Layout = BiomeVentLayout.Grid });
				along += Vector2.Distance(a, b);
			}
			return lines;
		}
	}
}
