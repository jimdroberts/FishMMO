using System.Runtime.InteropServices;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// One drawn animal as FishAmbientLife.hlsl and FishSeaLife.hlsl read it from their instance buffer
	/// (<c>_LifeInstances</c>): the object-to-world matrix's three top rows, then four per-instance vectors.
	/// </summary>
	/// <remarks>
	/// 112 bytes, seven float4s in this order (the shaders' structs are the same):
	/// <list type="bullet">
	/// <item><c>Row0..Row2</c>: the matrix's rows 0..2 (xyz the 3×3 row, w the translation); the fourth row is always 0 0 0 1.</item>
	/// <item>Ambient life: <c>A</c> _Anim, <c>B</c> _Tint, <c>C</c> _Look, <c>D</c> _Glow.</item>
	/// <item>Sea life: <c>A</c> _Anim, <c>B</c> _Tint, <c>C</c> _Surface, <c>D</c> unused (zero).</item>
	/// </list>
	/// </remarks>
	[StructLayout(LayoutKind.Sequential)]
	public struct LifeInstanceRecord
	{
		/// <summary>The record's size in bytes: the instance buffer's stride.</summary>
		public const int Stride = 7 * 16;

		public Vector4 Row0;
		public Vector4 Row1;
		public Vector4 Row2;
		public Vector4 A;
		public Vector4 B;
		public Vector4 C;
		public Vector4 D;
	}

	/// <summary>
	/// The instance buffers' packing for <see cref="AmbientLifeSystem"/> and <see cref="SeaLifeSystem"/>: every drawn animal of
	/// every kind in one buffer, each kind a run of it, drawn by one procedural draw per kind. Plain field arithmetic only, so
	/// it runs (and is tested) outside Unity.
	/// </summary>
	public static class LifeInstancePacking
	{
		/// <summary>
		/// Packs one instance: <paramref name="matrix"/> grown about its origin by <paramref name="grow"/> (the debug size scale;
		/// exactly the matrix when 1), and its four vectors.
		/// </summary>
		/// <remarks>
		/// The growth is <c>matrix * Matrix4x4.Scale(grow)</c>: the 3×3 columns times <paramref name="grow"/>, the
		/// translation kept. Multiplying a column by it gives the same floats the matrix product does (its other terms are
		/// zero).
		/// </remarks>
		public static LifeInstanceRecord Pack(in Matrix4x4 matrix, float grow, Vector4 a, Vector4 b, Vector4 c, Vector4 d)
		{
			return new LifeInstanceRecord
			{
				Row0 = new Vector4(matrix.m00 * grow, matrix.m01 * grow, matrix.m02 * grow, matrix.m03),
				Row1 = new Vector4(matrix.m10 * grow, matrix.m11 * grow, matrix.m12 * grow, matrix.m13),
				Row2 = new Vector4(matrix.m20 * grow, matrix.m21 * grow, matrix.m22 * grow, matrix.m23),
				A = a,
				B = b,
				C = c,
				D = d,
			};
		}

		/// <summary>The record's translation (where the animal is).</summary>
		public static Vector3 Origin(in LifeInstanceRecord record)
		{
			return new Vector3(record.Row0.w, record.Row1.w, record.Row2.w);
		}

		/// <summary>The record's largest column length: how many times its unit-sized mesh it is drawn.</summary>
		public static float Scale(in LifeInstanceRecord record)
		{
			float x = record.Row0.x * record.Row0.x + record.Row1.x * record.Row1.x + record.Row2.x * record.Row2.x;
			float y = record.Row0.y * record.Row0.y + record.Row1.y * record.Row1.y + record.Row2.y * record.Row2.y;
			float z = record.Row0.z * record.Row0.z + record.Row1.z * record.Row1.z + record.Row2.z * record.Row2.z;
			return Mathf.Sqrt(Mathf.Max(x, Mathf.Max(y, z)));
		}

		/// <summary>
		/// Widens the box <paramref name="min"/>..<paramref name="max"/> to hold the record's animal: a sphere of
		/// <paramref name="reach"/> (the mesh's own furthest point from its origin, at unit scale) times its scale about its origin.
		/// </summary>
		public static void Encapsulate(in LifeInstanceRecord record, float reach, ref Vector3 min, ref Vector3 max)
		{
			Vector3 at = Origin(record);
			float r = reach * Scale(record);
			min = new Vector3(Mathf.Min(min.x, at.x - r), Mathf.Min(min.y, at.y - r), Mathf.Min(min.z, at.z - r));
			max = new Vector3(Mathf.Max(max.x, at.x + r), Mathf.Max(max.y, at.y + r), Mathf.Max(max.z, at.z + r));
		}

		/// <summary>
		/// The furthest point of a mesh's bounds from its origin (<paramref name="centre"/> ± <paramref name="extents"/>), widened by
		/// <paramref name="slack"/> for what the vertex shader moves past the rest pose (wing beats, strides, tentacles).
		/// </summary>
		public static float Reach(Vector3 centre, Vector3 extents, float slack)
		{
			float x = Mathf.Abs(centre.x) + Mathf.Abs(extents.x);
			float y = Mathf.Abs(centre.y) + Mathf.Abs(extents.y);
			float z = Mathf.Abs(centre.z) + Mathf.Abs(extents.z);
			return Mathf.Sqrt(x * x + y * y + z * z) * slack;
		}

		/// <summary>
		/// The capacity to hold <paramref name="needed"/> records: <paramref name="capacity"/> itself when it does, else doubled
		/// until it does (at least <paramref name="minimum"/>), so a buffer grows a few times at most and never shrinks.
		/// </summary>
		public static int Grown(int capacity, int needed, int minimum)
		{
			if (needed <= capacity)
			{
				return capacity;
			}
			int grown = Mathf.Max(Mathf.Max(1, minimum), capacity);
			while (grown < needed)
			{
				grown *= 2;
			}
			return grown;
		}
	}
}
