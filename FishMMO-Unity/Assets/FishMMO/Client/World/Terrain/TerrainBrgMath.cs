using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The pure half of the GPU-driven draws' BatchRendererGroup path (TerrainGpuRenderer.Brg.cs): the batch buffer's layout
	/// as DOTS instancing reads it, the packed visible index FishTerrainInstancing.compute writes (FishPackFade), and the
	/// matrices FishDotsInstances stores — kept here, free of engine objects, so tests can pin the GPU's rules.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The batch buffer</b> (one per renderer, a raw buffer, bytes): [0, 64) zero — what every property whose metadata is 0
	/// reads (URP's light map scale, the probe occlusion…); <see cref="ContactOnAddress"/> and <see cref="FloraOnAddress"/> the
	/// renderer's two per-batch constants; then unity_ObjectToWorld for every resident instance (48 bytes each, column-major
	/// float3x4) from <see cref="ObjectToWorldAddress"/>, then unity_WorldToObject likewise from
	/// <see cref="WorldToObjectAddress"/>. The previous-frame matrices point at the same two arrays: nothing drawn here moves.
	/// </para>
	/// <para>
	/// <b>The visible index</b> (UnityDOTSInstancing.hlsl SetupDOTSVisibleInstancingData, with the draw's
	/// LODCrossFadeValuePacked flag): the resident index in the low 24 bits and the LOD fade as a signed byte in the high 8,
	/// which the shader reads back as byte / 127 (FishLodFade.hlsl). The byte is the middle of the fade's step of the 4×4
	/// pattern, never just the nearest byte, so the dither keeps exactly the pixels the float fade kept.
	/// </para>
	/// </remarks>
	public static class TerrainBrgMath
	{
		/// <summary>Zero bytes at the head of the batch buffer, for the properties nothing sets (metadata 0 reads address 0).</summary>
		public const int HeaderBytes = 64;
		/// <summary>The renderer's <c>_FishContactOn</c> as the batch constant <c>_FishBatchContactOn</c> (FishIndirectInstancing.hlsl).</summary>
		public const int ContactOnAddress = 64;
		/// <summary>The renderer's <c>_FishFloraOn</c> as the batch constant <c>_FishBatchFloraOn</c>.</summary>
		public const int FloraOnAddress = 68;
		/// <summary>Where unity_ObjectToWorld's array starts (16-byte aligned, after the header and the constants).</summary>
		public const int ObjectToWorldAddress = 80;
		/// <summary>One float3x4: twelve floats.</summary>
		public const int MatrixBytes = 48;
		/// <summary>The metadata bit that makes a property per instance (UnityDOTSInstancing.hlsl kPerInstanceDataBit).</summary>
		public const uint PerInstanceBit = 0x80000000u;
		/// <summary>Instances a packed visible index can name: 24 bits.</summary>
		public const int MaxInstances = 1 << 24;
		/// <summary>The steps of FishLodFade.hlsl's pattern (FishSurface.hlsl FishDitherThreshold: a 4×4 Bayer matrix, thresholds k/16).</summary>
		public const int FadeSteps = 16;

		/// <summary>Where unity_WorldToObject's array starts for a buffer of <paramref name="capacity"/> instances.</summary>
		public static int WorldToObjectAddress(int capacity) => ObjectToWorldAddress + MatrixBytes * Mathf.Max(0, capacity);

		/// <summary>The batch buffer's length in 4-byte words for <paramref name="capacity"/> instances.</summary>
		public static int BufferWords(int capacity) => (ObjectToWorldAddress + 2 * MatrixBytes * Mathf.Max(0, capacity)) / 4;

		/// <summary>A per-instance property's metadata: its array's address with <see cref="PerInstanceBit"/>.</summary>
		public static uint PerInstance(int address) => PerInstanceBit | (uint)address;

		/// <summary>The buffer's first <see cref="ObjectToWorldAddress"/> bytes as words: zero but for the two constants.</summary>
		public static uint[] Header(float contactOn, float floraOn)
		{
			var words = new uint[ObjectToWorldAddress / 4];
			words[ContactOnAddress / 4] = FloatBits(contactOn);
			words[FloraOnAddress / 4] = FloatBits(floraOn);
			return words;
		}

		private static uint FloatBits(float f) => (uint)System.BitConverter.SingleToInt32Bits(f);

		/// <summary>
		/// The fade's signed byte, shifted into the top of a visible index (FishTerrainInstancing.compute FishPackFade): 0 for
		/// 0 (fully drawn); otherwise the byte at the middle of the fade's pattern step, negated for a negative fade.
		/// </summary>
		public static uint PackLodFade(float fade)
		{
			int q = FadeByte(fade);
			return ((uint)q & 0xFFu) << 24;
		}

		/// <summary>The signed byte itself (−127..127).</summary>
		public static int FadeByte(float fade)
		{
			if (fade == 0f)
			{
				return 0;
			}
			float steps = Mathf.Ceil(FadeSteps * Mathf.Min(Mathf.Abs(fade), 1f));
			int q = (int)System.Math.Round((steps - 0.5) * (127.0 / FadeSteps));
			return fade < 0f ? -q : q;
		}

		/// <summary>A visible entry as DOTS instancing reads it: the resident index (under <see cref="MaxInstances"/>) and its fade.</summary>
		public static uint PackVisible(uint index, float fade) => (index & 0xFFFFFFu) | PackLodFade(fade);

		/// <summary>The fade the shader reads back from a packed entry (GetDOTSInstanceCrossfadeSnorm8 / 127).</summary>
		public static float UnpackLodFade(uint packed) => (sbyte)(packed >> 24) / 127f;

		/// <summary>The resident index of a packed entry.</summary>
		public static uint UnpackIndex(uint packed) => packed & 0xFFFFFFu;

		/// <summary>FishLodFade.hlsl FishLodFadeKeeps: whether a pixel at <paramref name="threshold"/> survives <paramref name="fade"/>.</summary>
		public static bool Keeps(float fade, float threshold)
		{
			bool inShare = threshold < Mathf.Abs(fade);
			return fade == 0f || (fade > 0f) == inShare;
		}

		/// <summary>The twelve floats DOTS instancing loads for an instance's object-to-world matrix (column by column, the translation last).</summary>
		public static void ObjectToWorld(in FishInstance i, float[] into)
		{
			Columns(i.Row0, i.Row1, i.Row2, into);
		}

		/// <summary>
		/// The twelve floats of its inverse, as FishDotsInstances computes it (and FishIndirectInstancing.hlsl
		/// FishIndirectInverseAffine): row k of inverse(R·S) is column k over its squared length, the translation −that·t.
		/// </summary>
		public static void WorldToObject(in FishInstance i, float[] into)
		{
			Vector3 c0 = new Vector3(i.Row0.x, i.Row1.x, i.Row2.x);
			Vector3 c1 = new Vector3(i.Row0.y, i.Row1.y, i.Row2.y);
			Vector3 c2 = new Vector3(i.Row0.z, i.Row1.z, i.Row2.z);
			Vector3 i0 = c0 / Vector3.Dot(c0, c0);
			Vector3 i1 = c1 / Vector3.Dot(c1, c1);
			Vector3 i2 = c2 / Vector3.Dot(c2, c2);
			var t = new Vector3(i.Row0.w, i.Row1.w, i.Row2.w);
			Columns(new Vector4(i0.x, i0.y, i0.z, -Vector3.Dot(i0, t)), new Vector4(i1.x, i1.y, i1.z, -Vector3.Dot(i1, t)),
				new Vector4(i2.x, i2.y, i2.z, -Vector3.Dot(i2, t)), into);
		}

		private static void Columns(Vector4 r0, Vector4 r1, Vector4 r2, float[] into)
		{
			for (int j = 0; j < 4; j++)
			{
				into[3 * j] = r0[j];
				into[3 * j + 1] = r1[j];
				into[3 * j + 2] = r2[j];
			}
		}

		/// <summary>The row-major 3×4 that UnityDOTSInstancing.hlsl LoadDOTSInstancedData_float4x4_from_float3x4 rebuilds from twelve floats.</summary>
		public static Matrix4x4 Load3x4(float[] f)
		{
			var m = Matrix4x4.identity;
			m.SetRow(0, new Vector4(f[0], f[3], f[6], f[9]));
			m.SetRow(1, new Vector4(f[1], f[4], f[7], f[10]));
			m.SetRow(2, new Vector4(f[2], f[5], f[8], f[11]));
			return m;
		}

		/// <summary>A light's draw command visibility: every split (cascade) it culls for, at least one.</summary>
		public static ushort SplitMask(int splits) => (ushort)((1 << Mathf.Clamp(splits, 1, 16)) - 1);
	}
}
