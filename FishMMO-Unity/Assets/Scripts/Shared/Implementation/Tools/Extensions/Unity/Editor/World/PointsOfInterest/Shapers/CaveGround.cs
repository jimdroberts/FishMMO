#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Which cave a site is carved as: its proportions, its walk and its stone.</summary>
	public enum CaveForm : byte
	{
		/// <summary>A walk-in cave system, small to large: deep, its rooms and caverns joined by passages.</summary>
		Cave = 0,
		/// <summary>A short passage opening into a broad chamber.</summary>
		Grotto = 1,
		/// <summary>A cave opened by the waves: its floor at the waterline, the sea running in.</summary>
		SeaCave = 2,
		/// <summary>A cave in ice: smoother walls, the ice's own material.</summary>
		IceCave = 3,
		/// <summary>A drained lava conduit: long, low, wide, nearly level, basalt.</summary>
		LavaTube = 4,
		/// <summary>A worked mine: a straight, level adit into the hill, drifts off it at right angles, stopes where the ore was.</summary>
		Mine = 5,
	}

	/// <summary>
	/// The ground over a cave: a heightfield patch sampled once on the main thread (terrain queries are main-thread
	/// only) and read bilinearly from the mesher's worker threads.
	/// </summary>
	public sealed class CaveGround
	{
		public readonly float OriginX, OriginZ, Spacing;
		public readonly int Width, Depth;
		public readonly float[] Heights;

		public CaveGround(float originX, float originZ, float spacing, int width, int depth, float[] heights)
		{
			OriginX = originX;
			OriginZ = originZ;
			Spacing = spacing;
			Width = width;
			Depth = depth;
			Heights = heights;
		}

		/// <summary>
		/// The ground over [minX, maxX] × [minZ, maxZ], sampled on a grid of <paramref name="spacing"/> whose lines pass
		/// through (<paramref name="alignX"/>, <paramref name="alignZ"/>): the terrain's own samples when those are its
		/// heightmap's, so reading it back bilinearly is the terrain's own interpolation.
		/// </summary>
		public static CaveGround Sample(Func<float, float, float> ground, float minX, float minZ, float maxX, float maxZ, float spacing,
			float alignX = 0f, float alignZ = 0f)
		{
			spacing = Mathf.Max(0.05f, spacing);
			int i0 = Mathf.FloorToInt((minX - alignX) / spacing), i1 = Mathf.CeilToInt((maxX - alignX) / spacing);
			int k0 = Mathf.FloorToInt((minZ - alignZ) / spacing), k1 = Mathf.CeilToInt((maxZ - alignZ) / spacing);
			int width = Mathf.Max(2, i1 - i0 + 1), depth = Mathf.Max(2, k1 - k0 + 1);
			float ox = alignX + i0 * spacing, oz = alignZ + k0 * spacing;
			var heights = new float[width * depth];
			for (int k = 0; k < depth; k++)
			{
				for (int i = 0; i < width; i++)
				{
					heights[k * width + i] = ground(ox + i * spacing, oz + k * spacing);
				}
			}
			return new CaveGround(ox, oz, spacing, width, depth, heights);
		}

		/// <summary>The ground at (x, z), world metres, bilinear; clamped to the patch's edge outside it.</summary>
		public float At(float x, float z)
		{
			float u = Mathf.Clamp((x - OriginX) / Spacing, 0f, Width - 1.0001f);
			float v = Mathf.Clamp((z - OriginZ) / Spacing, 0f, Depth - 1.0001f);
			int i = (int)u, k = (int)v;
			float fu = u - i, fv = v - k;
			int a = k * Width + i;
			float h0 = Heights[a] + (Heights[a + 1] - Heights[a]) * fu;
			float h1 = Heights[a + Width] + (Heights[a + Width + 1] - Heights[a + Width]) * fu;
			return h0 + (h1 - h0) * fv;
		}

		public bool Contains(float x, float z, float margin)
			=> x >= OriginX + margin && z >= OriginZ + margin && x <= OriginX + (Width - 1) * Spacing - margin && z <= OriginZ + (Depth - 1) * Spacing - margin;
	}

	/// <summary>One point of a cave's centre line, in the cave's frame (metres from its mouth's floor).</summary>
	public struct CaveNode
	{
		public Vector3 Centre;
		public float Radius;
		/// <summary>The floor's height under this point (the tube is cut flat there so it can be walked).</summary>
		public float Floor;
		/// <summary>Distance along the centre line from the mouth, metres.</summary>
		public float Along;
		/// <summary>
		/// How much taller than the system's own section the bore stands here (a canyon passage's slot); 0 or 1 is its own.
		/// </summary>
		public float Tall;

		/// <summary><see cref="Tall"/> with 0 read as 1.</summary>
		public float Height => Tall > 0f ? Tall : 1f;
	}
}
#endif
