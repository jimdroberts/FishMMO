#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>How a jointed cliff section is built.</summary>
	public struct CliffStyle
	{
		/// <summary>Section size: along the wall, into it, and the tallest column, metres.</summary>
		public float Length, Depth, Height;
		/// <summary>Spacing of the vertical joints that part the columns, metres (along, across).</summary>
		public Vector2 JointSpacing;
		/// <summary>Half-width of an open joint, metres.</summary>
		public float JointGap;
		/// <summary>Bed thickness range, metres.</summary>
		public Vector2 BedThickness;
		/// <summary>How far a soft bed is weathered back behind the hard ones, metres (min..max).</summary>
		public Vector2 BedRecess;
		/// <summary>Edge rounding of the columns, metres.</summary>
		public float Bevel;
		/// <summary>How much column heights vary (0 all the profile height … 1 wildly), and how the profile falls toward the front.</summary>
		public float HeightVar, FrontDrop;
		/// <summary>Tilt of a column's top, degrees.</summary>
		public float TopTilt;
		/// <summary>Broad weathering relief and fine relief, metres.</summary>
		public float Weather, Detail;
		/// <summary>Share of front-row columns missing (bays between pillars).</summary>
		public float Gaps;
		/// <summary>How deep a joint crack runs before it meets the solid core, metres.</summary>
		public float CrackDepth;
		/// <summary>Secondary joints (detail only): spacing (along, across), crack half-width at the surface, depth, and slab setback, metres.</summary>
		public Vector2 SubSpacing;
		public float SubWidth, SubDepth, SubStep;
		/// <summary>How far the top of the wall leans out over its front, metres (overhangs).</summary>
		public float Lean;
		/// <summary>Chance of an arch through the section, and its size as a share of the height.</summary>
		public float ArchChance, ArchHeight;

		/// <summary>A continuous canyon wall: broad joints, coarse beds, big slabs, leaning out at the top.</summary>
		public static CliffStyle Massif => new CliffStyle
		{
			Length = 36f, Depth = 12f, Height = 26f,
			JointSpacing = new Vector2(6.5f, 4.5f), JointGap = 0.07f,
			BedThickness = new Vector2(1.6f, 3.6f), BedRecess = new Vector2(0f, 0.7f),
			Bevel = 0.4f, HeightVar = 0.3f, FrontDrop = 0.35f, TopTilt = 5f,
			Weather = 0.35f, Detail = 0.04f, Gaps = 0.1f, CrackDepth = 1.6f,
			SubSpacing = new Vector2(1.7f, 1.3f), SubWidth = 0.03f, SubDepth = 0.25f, SubStep = 0.12f,
			Lean = 2.2f, ArchChance = 0.5f, ArchHeight = 0.42f,
		};

		/// <summary>A footing bluff: the massif at two thirds of its size, for bands of 10 m and more.</summary>
		public static CliffStyle Bluff => new CliffStyle
		{
			Length = 24f, Depth = 9f, Height = 18f,
			JointSpacing = new Vector2(5.2f, 3.8f), JointGap = 0.065f,
			BedThickness = new Vector2(1.2f, 3.0f), BedRecess = new Vector2(0f, 0.55f),
			Bevel = 0.32f, HeightVar = 0.3f, FrontDrop = 0.38f, TopTilt = 5f,
			Weather = 0.26f, Detail = 0.035f, Gaps = 0.12f, CrackDepth = 1.3f,
			SubSpacing = new Vector2(1.3f, 1.0f), SubWidth = 0.024f, SubDepth = 0.18f, SubStep = 0.09f,
			Lean = 1.6f, ArchChance = 0.4f, ArchHeight = 0.42f,
		};

		/// <summary>A wall section: columns of a few metres, a few bays, a little lean.</summary>
		public static CliffStyle Wall => new CliffStyle
		{
			Length = 14f, Depth = 6f, Height = 11f,
			JointSpacing = new Vector2(4.0f, 3.0f), JointGap = 0.06f,
			BedThickness = new Vector2(0.8f, 2.2f), BedRecess = new Vector2(0f, 0.35f),
			Bevel = 0.2f, HeightVar = 0.3f, FrontDrop = 0.4f, TopTilt = 5f,
			Weather = 0.14f, Detail = 0.025f, Gaps = 0.15f, CrackDepth = 0.9f,
			SubSpacing = new Vector2(0.9f, 0.7f), SubWidth = 0.018f, SubDepth = 0.12f, SubStep = 0.06f,
			Lean = 0.8f, ArchChance = 0.3f, ArchHeight = 0.4f,
		};

		/// <summary>Tall pillars standing apart: many bays, uneven heights, often an arch.</summary>
		public static CliffStyle Pillars => new CliffStyle
		{
			Length = 9f, Depth = 6f, Height = 16f,
			JointSpacing = new Vector2(3.0f, 3.0f), JointGap = 0.08f,
			BedThickness = new Vector2(1.0f, 2.6f), BedRecess = new Vector2(0f, 0.45f),
			Bevel = 0.25f, HeightVar = 0.5f, FrontDrop = 0.25f, TopTilt = 7f,
			Weather = 0.18f, Detail = 0.03f, Gaps = 0.4f, CrackDepth = 1.2f,
			SubSpacing = new Vector2(0.9f, 0.7f), SubWidth = 0.018f, SubDepth = 0.12f, SubStep = 0.06f,
			Lean = 1.0f, ArchChance = 0.6f, ArchHeight = 0.45f,
		};

		/// <summary>A small jointed outcrop for crevices and the upper face: two or three columns, thin beds, no arch.</summary>
		public static CliffStyle Block => new CliffStyle
		{
			Length = 6f, Depth = 4.5f, Height = 6f,
			JointSpacing = new Vector2(2.2f, 2.0f), JointGap = 0.04f,
			BedThickness = new Vector2(0.5f, 1.2f), BedRecess = new Vector2(0f, 0.2f),
			Bevel = 0.12f, HeightVar = 0.25f, FrontDrop = 0.3f, TopTilt = 6f,
			Weather = 0.08f, Detail = 0.015f, Gaps = 0.1f, CrackDepth = 0.4f,
			SubSpacing = new Vector2(0.6f, 0.5f), SubWidth = 0.012f, SubDepth = 0.08f, SubStep = 0.04f,
			Lean = 0.4f,
		};

		/// <summary>Shelves: wide flat-topped blocks stepping down toward the front, beds showing as ledges.</summary>
		public static CliffStyle Ledges => new CliffStyle
		{
			Length = 12f, Depth = 9f, Height = 5f,
			JointSpacing = new Vector2(4.5f, 3.5f), JointGap = 0.05f,
			BedThickness = new Vector2(0.5f, 1.1f), BedRecess = new Vector2(0f, 0.35f),
			Bevel = 0.16f, HeightVar = 0.15f, FrontDrop = 0.55f, TopTilt = 2.5f,
			Weather = 0.1f, Detail = 0.02f, Gaps = 0.08f, CrackDepth = 0.5f,
			SubSpacing = new Vector2(0.9f, 0.7f), SubWidth = 0.018f, SubDepth = 0.12f, SubStep = 0.06f,
		};

		/// <summary>
		/// A cliff's sheer backdrop, about 14 m tall: one continuous wall (even tops, no bays) that the planner stands
		/// at the foot and scales to the cliff's own height, the clutter in front of it. The three scarps are three
		/// heights, so the vertical scale (which stretches beds and texture) stays near 1. Weathering and bed
		/// recess are half the massif's: on a wall this tall the broad weathering bumps bridged the recessed beds'
		/// grooves into tunnels (genus 10–17; 0–3 at these values).
		/// </summary>
		public static CliffStyle ScarpLow => new CliffStyle
		{
			Length = 18f, Depth = 9f, Height = 14f,
			JointSpacing = new Vector2(4.0f, 3.0f), JointGap = 0.06f,
			BedThickness = new Vector2(0.9f, 2.4f), BedRecess = new Vector2(0f, 0.25f),
			Bevel = 0.22f, HeightVar = 0.15f, FrontDrop = 0.1f, TopTilt = 4f,
			Weather = 0.1f, Detail = 0.025f, CrackDepth = 1.0f,
			SubSpacing = new Vector2(1.0f, 0.8f), SubWidth = 0.02f, SubDepth = 0.14f, SubStep = 0.07f,
			Lean = 0.8f, ArchChance = 0.1f, ArchHeight = 0.35f,
		};

		/// <summary>The sheer backdrop about 26 m tall (<see cref="ScarpLow"/>).</summary>
		public static CliffStyle Scarp => new CliffStyle
		{
			Length = 24f, Depth = 12f, Height = 26f,
			JointSpacing = new Vector2(5.0f, 3.8f), JointGap = 0.065f,
			BedThickness = new Vector2(1.2f, 3.0f), BedRecess = new Vector2(0f, 0.3f),
			Bevel = 0.3f, HeightVar = 0.15f, FrontDrop = 0.1f, TopTilt = 4f,
			Weather = 0.12f, Detail = 0.035f, CrackDepth = 1.3f,
			SubSpacing = new Vector2(1.3f, 1.0f), SubWidth = 0.024f, SubDepth = 0.18f, SubStep = 0.09f,
			Lean = 1.6f, ArchChance = 0.15f, ArchHeight = 0.35f,
		};

		/// <summary>The sheer backdrop about 42 m tall (<see cref="ScarpLow"/>).</summary>
		public static CliffStyle ScarpHigh => new CliffStyle
		{
			Length = 30f, Depth = 16f, Height = 42f,
			JointSpacing = new Vector2(6.0f, 4.5f), JointGap = 0.07f,
			BedThickness = new Vector2(1.6f, 3.6f), BedRecess = new Vector2(0f, 0.35f),
			Bevel = 0.4f, HeightVar = 0.15f, FrontDrop = 0.1f, TopTilt = 4f,
			Weather = 0.15f, Detail = 0.04f, CrackDepth = 1.6f,
			SubSpacing = new Vector2(1.7f, 1.3f), SubWidth = 0.03f, SubDepth = 0.25f, SubStep = 0.12f,
			Lean = 2.4f, ArchChance = 0.2f, ArchHeight = 0.35f,
		};

		/// <summary>Talus: a fallen block of the wall about 3 m across, one or two columns of a few thin beds.</summary>
		public static CliffStyle Rubble => new CliffStyle
		{
			Length = 2.4f, Depth = 1.9f, Height = 1.5f,
			JointSpacing = new Vector2(1.5f, 1.3f), JointGap = 0.03f,
			BedThickness = new Vector2(0.35f, 0.8f), BedRecess = new Vector2(0f, 0.08f),
			Bevel = 0.07f, HeightVar = 0.15f, FrontDrop = 0.1f, TopTilt = 8f,
			Weather = 0.04f, Detail = 0.01f, CrackDepth = 0.25f,
			SubSpacing = new Vector2(0.45f, 0.4f), SubWidth = 0.008f, SubDepth = 0.05f, SubStep = 0.025f,
		};

		/// <summary>Talus: a big fallen block about 6–7 m across, a few columns and beds.</summary>
		public static CliffStyle Rockfall => new CliffStyle
		{
			Length = 5.2f, Depth = 4.0f, Height = 3.0f,
			JointSpacing = new Vector2(2.8f, 2.4f), JointGap = 0.04f,
			BedThickness = new Vector2(0.5f, 1.2f), BedRecess = new Vector2(0f, 0.15f),
			Bevel = 0.12f, HeightVar = 0.15f, FrontDrop = 0.1f, TopTilt = 8f,
			Weather = 0.07f, Detail = 0.015f, CrackDepth = 0.4f,
			SubSpacing = new Vector2(0.6f, 0.5f), SubWidth = 0.012f, SubDepth = 0.08f, SubStep = 0.04f,
		};
	}

	/// <summary>
	/// A jointed cliff section as one solid: columns parted by two sets of vertical joints (the Voronoi cells of a
	/// jittered grid, each cell's walls the bisectors with its neighbours, shared, so neighbours meet in a crack),
	/// each capped at its own height under a falling profile, every edge rounded; beds of differing hardness, the
	/// soft ones weathered back into ledges and overhangs; the upper wall leaning out; sometimes an arch; broad and
	/// fine weathering relief over all. Its front faces −z.
	/// </summary>
	/// <remarks>
	/// Two fields. <see cref="Evaluate"/> is the rock as it is: open joints cut only as deep as the crack depth, then
	/// meeting the core (so the wall stays one rock with cracks in it, not loose columns), secondary joints and fine
	/// relief. <see cref="EvaluateLow"/> is what the game mesh is cut from: joints closed (overlapping, since a
	/// touching pair leaves a zero sheet inside the rock) and smoothly unioned, no fine relief — features thinner than
	/// the meshing grid break surface extraction and cannot be carried by a 1,400-triangle section anyway.
	/// </remarks>
	public sealed class CliffSection
	{
		private const float Tau = Mathf.PI * 2f;

		public CliffStyle S;
		/// <summary>The <see cref="CliffSections"/> style name it was built from (null for a style made by hand).</summary>
		public string Style;
		public int Seed;
		/// <summary>Everything the solid can reach, the floor included: the box to mesh.</summary>
		public Bounds Extent;
		/// <summary>The meshing cell the game mesh will be cut at: the smooth union and bed easing must span a few of them.</summary>
		public float MeshCell = 0.06f;
		/// <summary>The solid stops at y = <see cref="Floor"/> (below the ground it stands on).</summary>
		public float Floor = -0.3f;
		/// <summary>Columns as planes (n·x ≤ w) with open joints, and the same with every joint closed.</summary>
		public readonly List<Vector4[]> Columns = new List<Vector4[]>();
		public readonly List<Vector4[]> ClosedColumns = new List<Vector4[]>();
		private readonly List<Bounds> boxes = new List<Bounds>();
		private float[] bedTop, bedRecess;
		private Vector3 jointX = Vector3.right, jointZ = Vector3.forward;
		private bool arch;
		private float archX, archRx, archRy;

		public float Evaluate(Vector3 p) => Evaluate(p, true);

		public float EvaluateLow(Vector3 p) => Evaluate(p, false);

		public Vector3 Gradient(Vector3 p, float h)
		{
			return new Vector3(
				Evaluate(p + new Vector3(h, 0, 0)) - Evaluate(p - new Vector3(h, 0, 0)),
				Evaluate(p + new Vector3(0, h, 0)) - Evaluate(p - new Vector3(0, h, 0)),
				Evaluate(p + new Vector3(0, 0, h)) - Evaluate(p - new Vector3(0, 0, h))) / (2f * h);
		}

		private float Evaluate(Vector3 p, bool fine)
		{
			// The upper wall leans out over its front (−z): sample the upright wall further back as height rises.
			float lt = Mathf.Clamp01(p.y / Mathf.Max(0.1f, S.Height));
			p += jointZ * (S.Lean * lt * lt);
			float f;
			if (fine)
			{
				f = Mathf.Min(Union(p, Columns, false), Union(p, ClosedColumns, true) + S.CrackDepth);
				f = SubJoints(p, f);
			}
			else
			{
				f = Union(p, ClosedColumns, true);
			}
			// A soft bed is worn back unevenly along the face: deep in places, flush in others.
			f += Recess(p.y) * Mathf.Clamp01(0.55f + 1.4f * ProceduralNoise.Fbm3(new Vector3(p.x, p.y * 0.3f, p.z) / 2.5f, 2, 0.5f, Seed + 7));
			f -= S.Weather * ProceduralNoise.Fbm3(new Vector3(p.x, p.y * 0.6f, p.z) / 3f, 3, 0.5f, Seed + 3) * 1.8f;
			f -= S.Weather * ProceduralNoise.Fbm3(new Vector3(p.x, p.y * 0.8f, p.z) / 1.2f, 2, 0.5f, Seed + 5) * 0.8f;
			if (arch)
			{
				// An irregular elliptical tunnel through the section from front to back, from the ground up.
				float x = Vector3.Dot(p, jointX) - archX;
				float e = Mathf.Sqrt((x / archRx) * (x / archRx) + (p.y / archRy) * (p.y / archRy)) - 1f;
				float d = e * Mathf.Min(archRx, archRy) + 0.35f * ProceduralNoise.Fbm3(p / 1.4f, 2, 0.5f, Seed + 11);
				f = Mathf.Max(f, -d);
			}
			if (fine) f -= S.Detail * ProceduralNoise.Fbm3(p / 0.35f, 3, 0.5f, Seed + 4);
			return Mathf.Max(f, Floor - p.y);
		}

		/// <summary>
		/// Secondary joints: a finer, leaning set of vertical cracks (2D Voronoi borders in the joint frame, F2 − F1)
		/// that splits each face into narrow slabs, each set back by its own amount; a crack is widest at the surface
		/// and closes a little way in.
		/// </summary>
		private float SubJoints(Vector3 p, float f)
		{
			if (f > 0.3f || f < -S.SubDepth) return f;
			float u = Vector3.Dot(p, jointX) / S.SubSpacing.x, w = Vector3.Dot(p, jointZ) / S.SubSpacing.y;
			// A slight lean with height, so slabs are not plumb.
			u += p.y * 0.06f / S.SubSpacing.x;
			int cu = Mathf.FloorToInt(u), cw = Mathf.FloorToInt(w);
			float d1 = float.MaxValue, d2 = float.MaxValue;
			uint near = 0;
			for (int j = -1; j <= 1; j++)
				for (int i = -1; i <= 1; i++)
				{
					uint h = ProceduralNoise.Hash(cu + i, cw + j, Seed + 29);
					float su = cu + i + 0.15f + 0.7f * ProceduralNoise.ToUnit(h);
					float sw = cw + j + 0.15f + 0.7f * ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0x9e3779b9u));
					float du = (u - su) * S.SubSpacing.x, dw = (w - sw) * S.SubSpacing.y;
					float d = Mathf.Sqrt(du * du + dw * dw);
					if (d < d1) { d2 = d1; d1 = d; near = h; } else if (d < d2) d2 = d;
				}
			float border = (d2 - d1) * 0.5f;
			// The step eases in over a few centimetres either side of the border.
			float inset = S.SubStep * ProceduralNoise.ToUnit(ProceduralNoise.Mix(near ^ 0x85ebca6bu));
			float g = f + inset * Mathf.Clamp01(border / 0.04f);
			float width = S.SubWidth * Mathf.Clamp01(1f + f / S.SubDepth);
			return Mathf.Max(g, width - border);
		}

		/// <summary>The union of columns: hard (cracks meet in a V) or smooth (sub-cell creases filled for meshing).</summary>
		private float Union(Vector3 p, List<Vector4[]> columns, bool smooth)
		{
			float blend = Mathf.Max(0.15f, 2.5f * MeshCell);
			float f = float.MaxValue;
			for (int c = 0; c < columns.Count; c++)
			{
				Bounds b = boxes[c];
				float dx = Mathf.Max(b.min.x - p.x, p.x - b.max.x), dy = Mathf.Max(b.min.y - p.y, p.y - b.max.y), dz = Mathf.Max(b.min.z - p.z, p.z - b.max.z);
				if (Mathf.Max(dx, Mathf.Max(dy, dz)) > f + (smooth ? blend : 0f)) continue;
				// The convex column: a smooth maximum of its planes, which rounds every edge by the bevel.
				Vector4[] planes = columns[c];
				float g = planes[0].x * p.x + planes[0].y * p.y + planes[0].z * p.z - planes[0].w;
				for (int i = 1; i < planes.Length; i++)
				{
					float h = planes[i].x * p.x + planes[i].y * p.y + planes[i].z * p.z - planes[i].w;
					float t = Mathf.Clamp01(0.5f + 0.5f * (g - h) / S.Bevel);
					g = h + (g - h) * t + S.Bevel * t * (1f - t);
				}
				if (!smooth || f == float.MaxValue) { if (g < f) f = g; continue; }
				float u = Mathf.Clamp01(0.5f + 0.5f * (g - f) / blend);
				f = g + (f - g) * u - blend * u * (1f - u);
			}
			return f;
		}

		/// <summary>How far the bed at height y is weathered back, eased across each bed boundary.</summary>
		private float Recess(float y)
		{
			if (bedTop == null || bedTop.Length == 0) return 0f;
			int i = 0;
			while (i < bedTop.Length - 1 && y > bedTop[i]) i++;
			float r = bedRecess[i];
			float ease = Mathf.Max(0.18f, 1.5f * MeshCell);
			if (i > 0)
			{
				float t = Mathf.Clamp01((y - bedTop[i - 1]) / ease);
				r = Mathf.Lerp(Mathf.Lerp(bedRecess[i - 1], r, 0.5f), r, t * t * (3f - 2f * t));
			}
			if (i < bedTop.Length - 1)
			{
				float t = Mathf.Clamp01((bedTop[i] - y) / ease);
				r = Mathf.Lerp(Mathf.Lerp(bedRecess[i + 1], r, 0.5f), r, t * t * (3f - 2f * t));
			}
			return r;
		}

		/// <summary>A section of a style. Pure: the same style and seed give the same solid.</summary>
		public static CliffSection Build(CliffStyle st, int seed)
		{
			var rng = new DeterministicRNG(seed);
			var solid = new CliffSection { S = st, Seed = seed };
			// The joint frame: the wall's own small yaw.
			float yaw = rng.Range(-10f, 10f) * Mathf.Deg2Rad;
			var ax = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
			var az = new Vector3(-Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
			solid.jointX = ax;
			solid.jointZ = az;
			// Column sites: a jittered grid two rings wider than the section, so every kept cell is bounded by neighbours.
			int nx = Mathf.CeilToInt(st.Length / st.JointSpacing.x) + 4, nz = Mathf.CeilToInt(st.Depth / st.JointSpacing.y) + 4;
			var sites = new List<Vector2>();
			for (int j = 0; j < nz; j++)
				for (int i = 0; i < nx; i++)
				{
					float u = (i - (nx - 1) * 0.5f) * st.JointSpacing.x + rng.Range(-0.32f, 0.32f) * st.JointSpacing.x;
					float w = (j - (nz - 1) * 0.5f) * st.JointSpacing.y + rng.Range(-0.32f, 0.32f) * st.JointSpacing.y;
					sites.Add(new Vector2(u, w));
				}
			// Beds, bottom up, each with its own hardness: mostly hard, about one in five soft and weathered well back.
			var tops = new List<float>();
			var recess = new List<float>();
			float y0 = -0.3f;
			while (y0 < st.Height + 1f)
			{
				y0 += rng.Range(st.BedThickness.x, st.BedThickness.y);
				tops.Add(y0);
				float span = st.BedRecess.y - st.BedRecess.x;
				recess.Add(rng.NextFloat() < 0.22f ? rng.Range(st.BedRecess.x + span * 0.5f, st.BedRecess.y) : rng.Range(st.BedRecess.x, st.BedRecess.x + span * 0.25f));
			}
			solid.bedTop = tops.ToArray();
			solid.bedRecess = recess.ToArray();

			float halfL = st.Length * 0.5f, halfD = st.Depth * 0.5f;
			float spacing = Mathf.Max(st.JointSpacing.x, st.JointSpacing.y);
			for (int s = 0; s < sites.Count; s++)
			{
				Vector2 c = sites[s];
				if (Mathf.Abs(c.x) > halfL || Mathf.Abs(c.y) > halfD) continue;
				// Back (+z) rows stand tallest; the front falls away; front columns go missing for bays.
				float back = (c.y + halfD) / Mathf.Max(0.01f, st.Depth);
				if (back < 0.3f && rng.NextFloat() < st.Gaps) continue;
				// Heights from a smooth field over the footprint (neighbours alike, the skyline rolling), plus a little each.
				float field = 0.5f + 0.9f * ProceduralNoise.Fbm3(new Vector3(c.x, 0f, c.y) / (2.2f * spacing), 2, 0.5f, seed + 41);
				float hgt = st.Height * Mathf.Lerp(1f - st.FrontDrop, 1f, back) * (1f - st.HeightVar * Mathf.Clamp01(field)) * rng.Range(0.94f, 1.04f);
				hgt = Mathf.Max(0.8f, hgt);
				var planes = new List<Vector4>();
				var closed = new List<Vector4>();
				for (int o = 0; o < sites.Count; o++)
				{
					if (o == s) continue;
					Vector2 d = sites[o] - c;
					if (d.sqrMagnitude > 4f * spacing * spacing) continue;
					// The shared joint between the two sites, leaning a little (the same lean seen from either side).
					int lo = Mathf.Min(s, o), hi = Mathf.Max(s, o);
					uint hsh = ProceduralNoise.Mix((uint)(lo * 7919 + hi * 104729 + seed));
					float lean = (ProceduralNoise.ToUnit(hsh) - 0.5f) * 0.4f;
					float gap = st.JointGap * (0.6f + 1.2f * ProceduralNoise.ToUnit(ProceduralNoise.Mix(hsh ^ 0x27d4eb2fu)));
					Vector2 n2 = d.normalized;
					var n = new Vector3(n2.x, lean * (s == lo ? 1f : -1f), n2.y).normalized;
					Vector2 mid = (sites[o] + c) * 0.5f;
					// World-space normal and a point on the joint; the open plane half a gap toward this column, the
					// closed twin past the bisector by the bevel. A touching pair leaves a zero sheet inside the rock,
					// and anything less than the bevel leaves the rounded corners where three columns meet as vertical
					// channels: hidden tunnels (genus 5–32 a section) that no simplification can remove.
					Vector3 nw = (ax * n.x + az * n.z + Vector3.up * n.y).normalized;
					Vector3 pw = ax * mid.x + az * mid.y + Vector3.up * (hgt * 0.5f);
					planes.Add(new Vector4(nw.x, nw.y, nw.z, Vector3.Dot(nw, pw) - gap));
					closed.Add(new Vector4(nw.x, nw.y, nw.z, Vector3.Dot(nw, pw) + Mathf.Max(0.04f, st.Bevel)));
				}
				// The top, tilted a little; the bottom below the floor.
				Vector3 topN = Tilted(rng.Range(0f, st.TopTilt), rng.NextFloat() * Tau);
				Vector3 cw = ax * c.x + az * c.y;
				planes.Add(new Vector4(topN.x, topN.y, topN.z, Vector3.Dot(topN, cw + Vector3.up * hgt)));
				planes.Add(new Vector4(0f, -1f, 0f, 1f));
				int jointCount = planes.Count - 2;
				if (!PolytopeBounds(planes, out Bounds bounds)) continue;
				// A corner or two chopped off the top.
				int chops = hgt > 4f ? rng.Next(3) : rng.Next(2);
				for (int k = 0; k < chops; k++)
				{
					float a = rng.NextFloat() * Tau;
					var n = new Vector3(Mathf.Cos(a), rng.Range(0.4f, 1.6f), Mathf.Sin(a)).normalized;
					Vector3 top = cw + Vector3.up * hgt;
					float reach = Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.9f;
					Vector3 at = top + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * reach * rng.Range(0.35f, 0.75f);
					planes.Add(new Vector4(n.x, n.y, n.z, Vector3.Dot(n, at)));
				}
				if (!PolytopeBounds(planes, out bounds)) continue;
				bounds.Expand(st.Bevel * 2f + 0.05f);
				for (int k = jointCount; k < planes.Count; k++) closed.Add(planes[k]);
				solid.ClosedColumns.Add(closed.ToArray());
				solid.Columns.Add(planes.ToArray());
				solid.boxes.Add(bounds);
			}
			if (rng.NextFloat() < st.ArchChance)
			{
				solid.arch = true;
				solid.archX = rng.Range(-0.25f, 0.25f) * st.Length;
				solid.archRx = rng.Range(0.12f, 0.2f) * st.Length;
				solid.archRy = st.ArchHeight * st.Height * rng.Range(0.85f, 1.1f);
			}
			var ext = new Bounds(Vector3.zero, Vector3.zero);
			foreach (Bounds b in solid.boxes) ext.Encapsulate(b);
			ext.Encapsulate(ext.min - az * (st.Lean + 0.5f));
			ext.Expand(st.Weather * 2f + 0.3f);
			ext.Encapsulate(new Vector3(ext.center.x, solid.Floor - 0.1f, ext.center.z));
			solid.Extent = ext;
			return solid;
		}

		/// <summary>A unit direction tilted <paramref name="tiltDegrees"/> from up toward azimuth <paramref name="azimuth"/> (radians).</summary>
		private static Vector3 Tilted(float tiltDegrees, float azimuth)
		{
			float t = tiltDegrees * Mathf.Deg2Rad;
			return new Vector3(Mathf.Sin(t) * Mathf.Cos(azimuth), Mathf.Cos(t), Mathf.Sin(t) * Mathf.Sin(azimuth));
		}

		// ── Polytope bounds ───────────────────────────────────────────

		/// <summary>The bounds of a convex polytope given as planes (n·x ≤ w): each plane's face clipped by all the others. False if they do not close it.</summary>
		private static bool PolytopeBounds(List<Vector4> planes, out Bounds bounds)
		{
			bounds = default;
			int count = planes.Count;
			double extent = 0.01;
			var normals = new D3[count];
			for (int i = 0; i < count; i++)
			{
				normals[i] = new D3(planes[i].x, planes[i].y, planes[i].z).Normalized;
				extent = Math.Max(extent, Math.Abs(planes[i].w));
			}
			double square = extent * 8.0;
			int faces = 0;
			bool any = false;
			for (int i = 0; i < count; i++)
			{
				D3 n = normals[i];
				D3 helper = Math.Abs(n.Y) < 0.9 ? new D3(0, 1, 0) : new D3(1, 0, 0);
				D3 u = D3.Cross(helper, n).Normalized;
				D3 v = D3.Cross(n, u);
				D3 c = n * planes[i].w;
				var poly = new List<D3> { c - u * square - v * square, c + u * square - v * square, c + u * square + v * square, c - u * square + v * square };
				for (int j = 0; j < count && poly.Count >= 3; j++)
				{
					if (j != i) poly = Clip(poly, normals[j], planes[j].w);
				}
				if (poly.Count < 3) continue;
				faces++;
				foreach (D3 p in poly)
				{
					// A corner on the starting square: the planes leave the solid open on that side.
					if (Math.Abs(p.X) > extent * 4 || Math.Abs(p.Y) > extent * 4 || Math.Abs(p.Z) > extent * 4) return false;
					if (!any) { bounds = new Bounds(p.ToVector3(), Vector3.zero); any = true; }
					else bounds.Encapsulate(p.ToVector3());
				}
			}
			return faces >= 4;
		}

		private static List<D3> Clip(List<D3> poly, D3 n, double d)
		{
			var result = new List<D3>(poly.Count + 1);
			for (int i = 0; i < poly.Count; i++)
			{
				D3 a = poly[i], b = poly[(i + 1) % poly.Count];
				double da = n.Dot(a) - d, db = n.Dot(b) - d;
				if (da <= 0.0) result.Add(a);
				if ((da <= 0.0) != (db <= 0.0)) result.Add(a + (b - a) * (da / (da - db)));
			}
			return result;
		}

		/// <summary>Double-precision vector for clipping.</summary>
		private readonly struct D3
		{
			public readonly double X, Y, Z;

			public D3(double x, double y, double z)
			{
				X = x;
				Y = y;
				Z = z;
			}

			public static D3 operator +(D3 a, D3 b) => new D3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
			public static D3 operator -(D3 a, D3 b) => new D3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
			public static D3 operator *(D3 a, double s) => new D3(a.X * s, a.Y * s, a.Z * s);
			public double Dot(D3 b) => X * b.X + Y * b.Y + Z * b.Z;
			public static D3 Cross(D3 a, D3 b) => new D3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
			public D3 Normalized => this * (1.0 / Math.Max(1e-300, Math.Sqrt(Dot(this))));
			public Vector3 ToVector3() => new Vector3((float)X, (float)Y, (float)Z);
		}
	}

	/// <summary>
	/// The cliff section catalogue: every style, its variants, and the game levels of each — closed, validated meshes
	/// cut from <see cref="CliffSection.EvaluateLow"/>, front facing −z, centred on the origin in x and z with the
	/// ground at y = 0. They wear a tiling rock material (no baked maps); the last level is the collider.
	/// </summary>
	public static class CliffSections
	{
		/// <summary>The styles: the face's clutter, largest first; the talus's fallen blocks (<see cref="IsDebris"/>); the sheer backdrops by height.</summary>
		public static readonly string[] Styles = { "Massif", "Bluff", "Wall", "Pillars", "Block", "Ledges", "Rubble", "Rockfall", "ScarpLow", "Scarp", "ScarpHigh" };
		/// <summary>Variants per style: a cliff is read section by section, and two of one mesh side by side read as a stamp.</summary>
		public const int VariantCount = 4;
		/// <summary>Triangle budget of each level of a face section.</summary>
		public static readonly int[] LodTriangles = { 1400, 600, 250 };
		/// <summary>Triangle budget of each level of a fallen block: a talus holds three times as many rocks as the face, at a fifth of the size.</summary>
		public static readonly int[] DebrisLodTriangles = { 500, 220, 90 };
		/// <summary>Levels of detail every style has.</summary>
		public const int LevelCount = 3;

		/// <summary>True for the talus styles (fallen blocks), false for the face's.</summary>
		public static bool IsDebris(string style) => style == "Rubble" || style == "Rockfall";

		/// <summary>A style's triangle budget per level.</summary>
		public static int[] LodTrianglesOf(string style) => IsDebris(style) ? DebrisLodTriangles : LodTriangles;
		/// <summary>Faces meeting at more than this keep a hard normal edge.</summary>
		public const float CreaseDegrees = 35f;

		public static CliffStyle StyleOf(string style)
		{
			switch (style)
			{
				case "Massif": return CliffStyle.Massif;
				case "Bluff": return CliffStyle.Bluff;
				case "Wall": return CliffStyle.Wall;
				case "Pillars": return CliffStyle.Pillars;
				case "Block": return CliffStyle.Block;
				case "Ledges": return CliffStyle.Ledges;
				case "Rubble": return CliffStyle.Rubble;
				case "Rockfall": return CliffStyle.Rockfall;
				case "ScarpLow": return CliffStyle.ScarpLow;
				case "Scarp": return CliffStyle.Scarp;
				case "ScarpHigh": return CliffStyle.ScarpHigh;
				default: throw new ArgumentException("No cliff section style " + style, nameof(style));
			}
		}

		/// <summary>
		/// A variant's style: variant 0 as declared; every later one 0.88–1.12 times as long, 0.9–1.1 as deep and
		/// 0.85–1.2 as tall (joint and bed spacing kept, so it holds more or fewer columns, not bigger ones), from a
		/// hash of the style and variant. Two seeds at one size read as one rock turned.
		/// </summary>
		public static CliffStyle StyleOf(string style, int variant)
		{
			CliffStyle st = StyleOf(style);
			if (variant <= 0)
			{
				return st;
			}
			uint h = ProceduralNoise.Hash(variant, 7, ProceduralNoise.SeedFor("CliffSectionProportions/" + style, 0x5c1f));
			st.Length *= Mathf.Lerp(0.88f, 1.12f, ProceduralNoise.ToUnit(h));
			h = ProceduralNoise.Mix(h);
			st.Depth *= Mathf.Lerp(0.9f, 1.1f, ProceduralNoise.ToUnit(h));
			h = ProceduralNoise.Mix(h);
			st.Height *= Mathf.Lerp(0.85f, 1.2f, ProceduralNoise.ToUnit(h));
			return st;
		}

		public static string MeshName(string style, int variant, int lod) => $"CliffSection_{style}_{variant}_LOD{lod}";

		public static IEnumerable<(string Style, int Variant, int Lod)> AllMeshes()
		{
			foreach (string style in Styles)
				for (int v = 0; v < VariantCount; v++)
					for (int lod = 0; lod < LevelCount; lod++)
						yield return (style, v, lod);
		}

		public static int VariantSeed(string style, int variant, int seed) => ProceduralNoise.SeedFor($"CliffSection/{style}/{variant}", seed);

		/// <summary>The solid of a variant, its meshing cell set (about 180 cells along its longest side).</summary>
		public static CliffSection Solid(string style, int variant, int seed)
		{
			CliffSection solid = CliffSection.Build(StyleOf(style, variant), VariantSeed(style, variant, seed));
			solid.Style = style;
			Vector3 size = solid.Extent.size;
			solid.MeshCell = Mathf.Max(0.06f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)) / 180f);
			return solid;
		}

		/// <summary>
		/// Every level of a variant, validated (see <see cref="ProceduralSurfaceNets.BuildLevels"/>; throws rather than
		/// return a broken mesh), centred in x and z on the full-detail level.
		/// </summary>
		public static MeshBuilder[] BuildMeshes(string style, int variant, int seed, out string report)
		{
			return BuildMeshes(Solid(style, variant, seed), out _, out report);
		}

		/// <summary>The levels of a solid; <paramref name="centre"/> is what was taken off (mesh point + centre = solid point).</summary>
		public static MeshBuilder[] BuildMeshes(CliffSection solid, out Vector3 centre, out string report)
		{
			MeshBuilder[] levels = ProceduralSurfaceNets.BuildLevels(solid.EvaluateLow, solid.Extent, solid.MeshCell, LodTrianglesOf(solid.Style), CreaseDegrees, out report);
			Bounds b = levels[0].Bounds;
			centre = new Vector3(b.center.x, 0f, b.center.z);
			foreach (MeshBuilder level in levels)
			{
				for (int i = 0; i < level.VertexCount; i++) level.Positions[i] -= centre;
			}
			return levels;
		}
	}
}
#endif
