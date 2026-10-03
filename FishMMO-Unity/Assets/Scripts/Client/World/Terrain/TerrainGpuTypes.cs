using System.Runtime.InteropServices;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// One instance as the GPU path stores it (<c>FishInstance</c> in FishIndirectInstancing.hlsl and
	/// FishTerrainInstancing.compute): the object-to-world matrix's top three rows, 48 bytes. Always a
	/// T·R·S without shear, so the shader rebuilds the inverse from the column lengths.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct FishInstance
	{
		public const int Stride = 48;

		public Vector4 Row0, Row1, Row2;

		public static FishInstance From(in Matrix4x4 m)
		{
			return new FishInstance
			{
				Row0 = new Vector4(m.m00, m.m01, m.m02, m.m03),
				Row1 = new Vector4(m.m10, m.m11, m.m12, m.m13),
				Row2 = new Vector4(m.m20, m.m21, m.m22, m.m23),
			};
		}

		public Matrix4x4 ToMatrix()
		{
			var m = new Matrix4x4();
			m.SetRow(0, Row0);
			m.SetRow(1, Row1);
			m.SetRow(2, Row2);
			m.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
			return m;
		}

		public Vector3 Position => new Vector3(Row0.w, Row1.w, Row2.w);
	}

	/// <summary>One culled, visible entry (<c>FishVisible</c>): the instance's index in <c>_FishInstances</c> and its LOD fade. 8 bytes.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct FishVisible
	{
		public const int Stride = 8;

		public uint Index;
		public float LodFade;
	}

	/// <summary>
	/// One prototype as the cull kernel reads it (<c>FishModel</c> in FishTerrainInstancing.compute), 80 bytes:
	/// up to four levels' transition heights and fade bands, the LOD size and reference point, a bounding
	/// sphere in the root's space, the level count, the first of its slots (level × 2 + view) and which
	/// levels cast shadows.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct FishModelData
	{
		public const int Stride = 80;
		public const int MaxLevels = 4;

		public Vector4 Transitions;
		public Vector4 FadeWidths;
		public Vector3 LocalReference;
		public float Size;
		public Vector3 SphereCentre;
		public float SphereRadius;
		public uint LevelCount;
		public uint SlotBase;
		public uint ShadowMask;
		public uint Padding;
	}

	/// <summary>
	/// One cull work item (<c>FishWork</c>): up to 64 consecutive instances of one prototype in
	/// <c>_FishInstances</c>, the prototype, the terrain's draw distance and LOD bias multiplier, and the
	/// distance thinning (grass: full density to <see cref="ThinStart"/>, then a hashed, dither-faded share
	/// falling to <see cref="ThinKeep"/> at the draw distance; trees: ThinKeep 1, none). 32 bytes.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct FishWork
	{
		public const int Stride = 32;
		public const int MaxInstances = 64;

		public uint Start;
		public uint Count;
		public uint Model;
		public uint Padding;
		public float DrawDistance;
		public float LodBiasMultiplier;
		public float ThinStart;
		public float ThinKeep;
	}
}
