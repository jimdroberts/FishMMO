using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// What a glow reads of the stored world round it (2026-10-25, I6i-b; decisions 34, 76, 90, 93): the nearest stored landform line
	/// (how far along it the glow stands, and the line's bearing doubled) and the nearest stored fountain vent (its seed and schedule).
	/// <see cref="None"/> (a scene that stores neither) is what every glow read before: a TRAIL along a fixed bearing, a KNEADING crack
	/// bearing from a smooth field, an EPISODE seeded by its 500 m cell.
	/// </summary>
	public struct SceneGlowPlace
	{
		/// <summary>True when a stored line is nearest: <see cref="Along"/>, <see cref="Cos2"/> and <see cref="Sin2"/> are its.</summary>
		public bool OnLine;
		/// <summary>Metres along the nearest line (plus the line's own phase, <see cref="SceneGlowPlaces.PhaseOf"/>).</summary>
		public float Along;
		/// <summary>The nearest line's bearing doubled: cos 2b and sin 2b (a crack has no sense, so b and b + π are one).</summary>
		public float Cos2, Sin2;
		/// <summary>True when a stored fountain vent is nearest: <see cref="VentSeed"/> and <see cref="VentEpisodes"/> are its.</summary>
		public bool AtVent;
		/// <summary>The nearest vent's timing seed (VentSite.TimingSeed): the glow plays its episodes.</summary>
		public uint VentSeed;
		/// <summary>The nearest vent's fountain episodes (its biome's; the volcanic default for none).</summary>
		public BiomeFountainEpisodes VentEpisodes;

		/// <summary>Nothing stored: every glow as before.</summary>
		public static SceneGlowPlace None => default;
	}

	/// <summary>
	/// One stretch of a stored landform line as the glows read it (2026-10-25, I6i-b): the GPU's <c>_FishGlowLines</c> (A, B) and
	/// <c>_FishGlowLineData</c> (along at A, along span to B, cos 2b, sin 2b), the site lights' the same numbers.
	/// </summary>
	public struct SceneGlowLine
	{
		public Vector2 A, B;
		/// <summary>Metres along its line at <see cref="A"/>, plus the line's own phase.</summary>
		public float Along;
		/// <summary>Metres along its line from <see cref="A"/> to <see cref="B"/> (a merged stretch keeps the line's own length).</summary>
		public float Span;
		/// <summary>Its bearing doubled (cos 2b, sin 2b), from its chord.</summary>
		public float Cos2, Sin2;
	}

	/// <summary>A stored fountain vent as the glows read it (2026-10-25, I6i-b): the GPU's <c>_FishGlowVents</c>.</summary>
	public struct SceneGlowVent
	{
		public Vector2 Position;
		/// <summary>Its timing seed (VentSite.TimingSeed).</summary>
		public uint Seed;
		/// <summary>Its fountain episodes (never empty: the volcanic default stands in for none).</summary>
		public BiomeFountainEpisodes Episodes;
	}

	/// <summary>
	/// A disc of the ground whose glows all read one biome's row of the glow table (2026-10-25, I6i-b): a site straddling an edge of
	/// the biome grid between biomes whose glows move differently reads its centre's biome everywhere in it, and a light standing by
	/// such an edge outside any site reads its own row within <see cref="SceneGlowPlaces.LightReach"/>, so a light and the stone that
	/// casts it (the GPU reads the stone's origin, the site lights the light's place) read the same row. The GPU's <c>_FishGlowAnchors</c>.
	/// </summary>
	public struct SceneGlowAnchor
	{
		public Vector2 Centre;
		public float Radius;
		/// <summary>The glow table's row (the biome's flora index) everything inside reads.</summary>
		public int Row;
	}

	/// <summary>
	/// The flora grid as the glow table reads it (FishGlowModes.hlsl FishGlowRowAt, GlowModeTable): each cell its biome's row, read
	/// unjittered; 0 outside the grid; a row past the table's last reads its last.
	/// </summary>
	public struct SceneGlowGrid
	{
		public byte[] Cells;
		public int Width, Height;
		/// <summary>(origin x, origin z, 1 / size x, 1 / size z): uv = (xz − origin) · rect.zw.</summary>
		public Vector4 Rect;
		/// <summary>The table's rows (the last a row past it reads).</summary>
		public int Rows;

		public bool Valid => Cells != null && Width > 0 && Height > 0 && Rect.z > 0f && Rows > 0;

		/// <summary>The row under a world xz, as the GPU reads it.</summary>
		public int RowAt(float x, float z)
		{
			if (!Valid)
			{
				return 0;
			}
			float u = (x - Rect.x) * Rect.z, v = (z - Rect.y) * Rect.w;
			if (u < 0f || v < 0f || u > 1f || v > 1f)
			{
				return 0;
			}
			int cx = Mathf.Clamp(Mathf.FloorToInt(u * Width), 0, Width - 1), cz = Mathf.Clamp(Mathf.FloorToInt(v * Height), 0, Height - 1);
			int index = cz * Width + cx;
			return Math.Min(index < Cells.Length ? Cells[index] : 0, Rows - 1);
		}

		/// <summary>Whether every row under a world box moves a glow as <paramref name="row"/> does (<paramref name="same"/>).</summary>
		public bool Uniform(float minX, float minZ, float maxX, float maxZ, int row, Func<int, int, bool> same)
		{
			if (!Valid)
			{
				return true;
			}
			float u0 = (minX - Rect.x) * Rect.z, u1 = (maxX - Rect.x) * Rect.z;
			float v0 = (minZ - Rect.y) * Rect.w, v1 = (maxZ - Rect.y) * Rect.w;
			// Off the grid every glow reads row 0.
			if ((u0 < 0f || v0 < 0f || u1 > 1f || v1 > 1f) && !same(row, 0))
			{
				return false;
			}
			if (u1 < 0f || v1 < 0f || u0 > 1f || v0 > 1f)
			{
				return true;
			}
			int x0 = Mathf.Clamp(Mathf.FloorToInt(u0 * Width), 0, Width - 1), x1 = Mathf.Clamp(Mathf.FloorToInt(u1 * Width), 0, Width - 1);
			int z0 = Mathf.Clamp(Mathf.FloorToInt(v0 * Height), 0, Height - 1), z1 = Mathf.Clamp(Mathf.FloorToInt(v1 * Height), 0, Height - 1);
			for (int z = z0; z <= z1; z++)
			{
				for (int x = x0; x <= x1; x++)
				{
					int index = z * Width + x;
					int r = Math.Min(index < Cells.Length ? Cells[index] : 0, Rows - 1);
					if (!same(row, r))
					{
						return false;
					}
				}
			}
			return true;
		}
	}

	/// <summary>
	/// The glows' stored places (2026-10-25, I6i-b; decision 34: the I6i stand-ins switched to the stored data): the scene's landform
	/// lines (<see cref="SceneBiomeMap.GlowLines"/>, else its vent lines), its fountain vents (VolcanicVents' Episode vents) and the
	/// discs that keep a light and its stone on one biome's row. Pure: one definition read by the site lights and, number for number,
	/// by the GPU (FishGlowModes.hlsl), so every player sees the same trail creep along the same rille.
	/// </summary>
	public static class SceneGlowPlaces
	{
		/// <summary>The lines, vents and anchors the GPU's arrays hold (FishGlowModes.hlsl).</summary>
		public const int MaxLines = 64, MaxVents = 16, MaxAnchors = 32;

		/// <summary>The most landform segments a map stores for the glows (the cut's <see cref="FromLandforms"/>).</summary>
		public const int MaxStored = 4096;

		/// <summary>How far round a light by a biome edge its stone may stand, metres: the ground there reads the light's row.</summary>
		public const float LightReach = 6f;

		/// <summary>The salt of a line's trail phase.</summary>
		public const uint PhaseSalt = 0x7A11u;

		/// <summary>
		/// Every stretch of every landform line of a scene (a rille's or channel's centreline, a ridge's crest; pure): the segments of
		/// each stored site in the sites' order, each run of segments from along 0 one line whose seed is the site's mixed with the run's
		/// index (as its vent lines' are, IceStripes.FromLandforms), of every biome; at most <see cref="MaxStored"/>.
		/// </summary>
		public static List<VentLine> FromLandforms(IReadOnlyList<BiomeLandformSite> sites)
		{
			var lines = new List<VentLine>();
			for (int s = 0; sites != null && s < sites.Count && lines.Count < MaxStored; s++)
			{
				BiomeLandformSite site = sites[s];
				if (site.Segments == null || site.Segments.Count == 0)
				{
					continue;
				}
				int run = -1;
				for (int k = 0; k < site.Segments.Count && lines.Count < MaxStored; k++)
				{
					BiomeLandformSegment seg = site.Segments[k];
					if (k == 0 || seg.AlongA <= 1e-4f)
					{
						run++;
					}
					lines.Add(new VentLine
					{
						A = seg.A,
						B = seg.B,
						AlongA = seg.AlongA,
						LineLength = Mathf.Max(1e-3f, seg.LineLength),
						Half = Mathf.Max(0.25f, seg.Half),
						Seed = IceStripes.Mix(site.Seed, (uint)run),
						BiomeID = site.BiomeID,
						Layout = BiomeVentLayout.Grid,
					});
				}
			}
			return lines;
		}

		/// <summary>A line's trail phase, metres (0..<see cref="SceneGlowModes.TrailMetres"/>): two lines' lights never creep in step.</summary>
		public static float PhaseOf(uint seed) => (float)TurbidityEpisodes.Unit(IceStripes.Mix(seed, PhaseSalt)) * SceneGlowModes.TrailMetres;

		/// <summary>A stretch from A to B, <paramref name="along"/> at A and <paramref name="span"/> metres of its line to B.</summary>
		public static SceneGlowLine Stretch(Vector2 a, Vector2 b, float along, float span)
		{
			float dx = b.x - a.x, dz = b.y - a.y;
			float length2 = dx * dx + dz * dz;
			return new SceneGlowLine
			{
				A = a,
				B = b,
				Along = along,
				Span = span,
				Cos2 = length2 > 0f ? (dx * dx - dz * dz) / length2 : 1f,
				Sin2 = length2 > 0f ? 2f * dx * dz / length2 : 0f,
			};
		}

		/// <summary>A stored line's stretch as the glows read it.</summary>
		public static SceneGlowLine LineOf(in VentLine line) => Stretch(line.A, line.B, line.AlongA + PhaseOf(line.Seed), Vector2.Distance(line.A, line.B));

		/// <summary>
		/// The stretches the glows read (pure): every segment (no pad) of a biome <paramref name="keep"/> keeps (null: all), runs of one
		/// line joined end to start; while more than <paramref name="cap"/>, each run's neighbouring stretches merged in pairs (along and
		/// span kept, the chord between), then the first <paramref name="cap"/>.
		/// </summary>
		public static List<SceneGlowLine> Choose(IReadOnlyList<VentLine> lines, Func<int, bool> keep, int cap = MaxLines)
		{
			var runs = new List<List<SceneGlowLine>>();
			List<SceneGlowLine> run = null;
			VentLine last = default;
			for (int i = 0; lines != null && i < lines.Count; i++)
			{
				VentLine line = lines[i];
				if (line.IsPad || keep != null && !keep(line.BiomeID))
				{
					run = null;
					continue;
				}
				if (run == null || last.Seed != line.Seed || (last.B - line.A).sqrMagnitude > 1e-4f)
				{
					run = new List<SceneGlowLine>();
					runs.Add(run);
				}
				run.Add(LineOf(in line));
				last = line;
			}
			int total = 0;
			foreach (List<SceneGlowLine> r in runs)
			{
				total += r.Count;
			}
			while (total > cap)
			{
				bool merged = false;
				total = 0;
				for (int r = 0; r < runs.Count; r++)
				{
					List<SceneGlowLine> from = runs[r];
					if (from.Count >= 2)
					{
						var halved = new List<SceneGlowLine>((from.Count + 1) / 2);
						for (int k = 0; k < from.Count; k += 2)
						{
							halved.Add(k + 1 < from.Count ? Stretch(from[k].A, from[k + 1].B, from[k].Along, from[k].Span + from[k + 1].Span) : from[k]);
						}
						runs[r] = halved;
						merged = true;
					}
					total += runs[r].Count;
				}
				if (!merged)
				{
					break;
				}
			}
			var chosen = new List<SceneGlowLine>(Math.Min(total, cap));
			foreach (List<SceneGlowLine> r in runs)
			{
				foreach (SceneGlowLine s in r)
				{
					if (chosen.Count < cap)
					{
						chosen.Add(s);
					}
				}
			}
			return chosen;
		}

		/// <summary>
		/// The fountain vents the glows read (pure): the scene's vents whose timing is the fountain episodes (BiomeVentTiming.Episode),
		/// in order, at most <paramref name="cap"/>; a vent's empty schedule is the volcanic default (as VentTiming's).
		/// </summary>
		public static List<SceneGlowVent> VentsOf(IReadOnlyList<VentSite> vents, int cap = MaxVents)
		{
			var chosen = new List<SceneGlowVent>();
			for (int i = 0; vents != null && i < vents.Count && chosen.Count < cap; i++)
			{
				VentSite v = vents[i];
				if (v.Timing != BiomeVentTiming.Episode)
				{
					continue;
				}
				chosen.Add(new SceneGlowVent { Position = v.Position, Seed = v.TimingSeed, Episodes = v.Episodes.IsEmpty ? BiomeFountainEpisodes.Volcanic : v.Episodes });
			}
			return chosen;
		}

		/// <summary>
		/// The nearest stretch to world (x, z) (the first of equals), as the GPU finds it: how far along its line the glow stands and its
		/// doubled bearing. False with none.
		/// </summary>
		public static bool NearestLine(IReadOnlyList<SceneGlowLine> lines, float x, float z, out float along, out float cos2, out float sin2)
		{
			along = 0f;
			cos2 = 1f;
			sin2 = 0f;
			int n = lines != null ? lines.Count : 0;
			if (n <= 0)
			{
				return false;
			}
			float best = float.MaxValue, bestT = 0f;
			int found = 0;
			for (int i = 0; i < n; i++)
			{
				SceneGlowLine s = lines[i];
				float dx = s.B.x - s.A.x, dz = s.B.y - s.A.y;
				float length2 = dx * dx + dz * dz;
				float t = length2 > 0f ? SceneGlowModes.Clamp01(((x - s.A.x) * dx + (z - s.A.y) * dz) / length2) : 0f;
				float qx = s.A.x + t * dx - x, qz = s.A.y + t * dz - z;
				float d = qx * qx + qz * qz;
				if (d < best)
				{
					best = d;
					found = i;
					bestT = t;
				}
			}
			SceneGlowLine nearest = lines[found];
			along = nearest.Along + bestT * nearest.Span;
			cos2 = nearest.Cos2;
			sin2 = nearest.Sin2;
			return true;
		}

		/// <summary>The nearest vent to world (x, z) (the first of equals), as the GPU finds it; −1 with none.</summary>
		public static int NearestVent(IReadOnlyList<SceneGlowVent> vents, float x, float z)
		{
			int n = vents != null ? vents.Count : 0, found = -1;
			float best = float.MaxValue;
			for (int i = 0; i < n; i++)
			{
				float dx = x - vents[i].Position.x, dz = z - vents[i].Position.y;
				float d = dx * dx + dz * dz;
				if (d < best)
				{
					best = d;
					found = i;
				}
			}
			return found;
		}

		/// <summary>What a glow at world (x, z) reads of the stored lines and vents (<see cref="SceneGlowPlace.None"/> with neither).</summary>
		public static SceneGlowPlace At(IReadOnlyList<SceneGlowLine> lines, IReadOnlyList<SceneGlowVent> vents, float x, float z)
		{
			var place = new SceneGlowPlace();
			place.OnLine = NearestLine(lines, x, z, out place.Along, out place.Cos2, out place.Sin2);
			int vent = NearestVent(vents, x, z);
			if (vent >= 0)
			{
				place.AtVent = true;
				place.VentSeed = vents[vent].Seed;
				place.VentEpisodes = vents[vent].Episodes;
			}
			return place;
		}

		/// <summary>
		/// The discs that keep a light and its stone on one row (pure; <see cref="SceneGlowAnchor"/>): first each site (x, z, radius)
		/// with a light inside that straddles rows moving a glow differently (<paramref name="same"/> false), the whole site its
		/// centre's row; then each light outside those sites with such a row within <see cref="LightReach"/>, the ground round it its
		/// own row. At most <paramref name="cap"/>, in that order. None where the rows agree: every glow reads its own ground as before.
		/// </summary>
		public static List<SceneGlowAnchor> Anchors(in SceneGlowGrid grid, IReadOnlyList<Vector3> sites, IReadOnlyList<Vector2> lights, Func<int, int, bool> same,
			int cap = MaxAnchors)
		{
			var anchors = new List<SceneGlowAnchor>();
			if (!grid.Valid || same == null)
			{
				return anchors;
			}
			for (int s = 0; sites != null && s < sites.Count && anchors.Count < cap; s++)
			{
				Vector3 site = sites[s];
				float r = site.z;
				if (!(r > 0f) || !AnyWithin(lights, site.x, site.y, r))
				{
					continue;
				}
				int row = grid.RowAt(site.x, site.y);
				if (!grid.Uniform(site.x - r, site.y - r, site.x + r, site.y + r, row, same))
				{
					anchors.Add(new SceneGlowAnchor { Centre = new Vector2(site.x, site.y), Radius = r, Row = row });
				}
			}
			for (int i = 0; lights != null && i < lights.Count && anchors.Count < cap; i++)
			{
				Vector2 light = lights[i];
				if (RowAt(anchors, light.x, light.y, -1) >= 0)
				{
					continue;
				}
				int row = grid.RowAt(light.x, light.y);
				if (!grid.Uniform(light.x - LightReach, light.y - LightReach, light.x + LightReach, light.y + LightReach, row, same))
				{
					anchors.Add(new SceneGlowAnchor { Centre = light, Radius = LightReach, Row = row });
				}
			}
			return anchors;
		}

		/// <summary>The row of the first disc holding world (x, z), as the GPU reads it; <paramref name="fallback"/> outside them all.</summary>
		public static int RowAt(IReadOnlyList<SceneGlowAnchor> anchors, float x, float z, int fallback)
		{
			for (int i = 0; anchors != null && i < anchors.Count; i++)
			{
				SceneGlowAnchor a = anchors[i];
				float dx = x - a.Centre.x, dz = z - a.Centre.y;
				if (dx * dx + dz * dz <= a.Radius * a.Radius)
				{
					return a.Row;
				}
			}
			return fallback;
		}

		private static bool AnyWithin(IReadOnlyList<Vector2> points, float x, float z, float r)
		{
			for (int i = 0; points != null && i < points.Count; i++)
			{
				float dx = points[i].x - x, dz = points[i].y - z;
				if (dx * dx + dz * dz <= r * r)
				{
					return true;
				}
			}
			return false;
		}
	}
}
