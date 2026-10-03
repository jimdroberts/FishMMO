using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The numbers one terrain layer contributes to the array terrain shader, besides its textures.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A plain value rather than a <see cref="TerrainLayer"/> because the layer whose numbers the
	/// ground draws with is not always one a scene may reference: when LOCAL art overrides a layer,
	/// its tiling and remaps come from an asset under <c>Assets/LOCAL</c>. These values are therefore
	/// copied into the baked <see cref="TerrainArraySet"/> (build output) and never into the scene.
	/// </para>
	/// <para>
	/// The packing mirrors what Unity's terrain engine hands URP's TerrainLit, so a layer looks the
	/// same in both shaders: the diffuse remap's maximum is a tint (URP ignores the minimum), the
	/// mask remap becomes an offset (the minimum) and a scale (maximum minus minimum), and a layer
	/// whose albedo carries smoothness in its alpha uses a smoothness multiplier of one.
	/// </para>
	/// </remarks>
	public struct TerrainArrayLayerParams
	{
		/// <summary>The most layers one scene's arrays hold: eight control maps of four channels.</summary>
		public const int MaximumLayers = 32;

		/// <summary>How many float4 arrays the shader reads per layer.</summary>
		public const int VectorsPerLayer = 5;

		public Vector2 TileSize;
		public Vector2 TileOffset;
		public float NormalScale;
		public float Metallic;
		public float Smoothness;
		public Vector4 DiffuseRemapMax;
		public Vector4 MaskRemapMin;
		public Vector4 MaskRemapMax;
		/// <summary>True when the layer has a mask map, so metallic, AO and smoothness come from it.</summary>
		public bool HasMask;
		/// <summary>True when the albedo's alpha holds smoothness.</summary>
		public bool AlbedoHasAlpha;

		/// <summary>The defaults of a fresh <see cref="TerrainLayer"/>: what a layer with no settings draws as.</summary>
		public static TerrainArrayLayerParams Default => new TerrainArrayLayerParams
		{
			TileSize = new Vector2(15f, 15f),
			TileOffset = Vector2.zero,
			NormalScale = 1f,
			Metallic = 0f,
			Smoothness = 0f,
			DiffuseRemapMax = Vector4.one,
			MaskRemapMin = Vector4.zero,
			MaskRemapMax = Vector4.one,
		};

		/// <summary>Copies a terrain layer's numbers. The two flags describe the textures the arrays end up holding, which may not be the layer's own.</summary>
		public static TerrainArrayLayerParams From(TerrainLayer layer, bool hasMask, bool albedoHasAlpha)
		{
			if (layer == null)
			{
				TerrainArrayLayerParams fallback = Default;
				fallback.HasMask = hasMask;
				fallback.AlbedoHasAlpha = albedoHasAlpha;
				return fallback;
			}
			return new TerrainArrayLayerParams
			{
				TileSize = layer.tileSize,
				TileOffset = layer.tileOffset,
				NormalScale = layer.normalScale,
				Metallic = layer.metallic,
				Smoothness = layer.smoothness,
				DiffuseRemapMax = layer.diffuseRemapMax,
				MaskRemapMin = layer.maskMapRemapMin,
				MaskRemapMax = layer.maskMapRemapMax,
				HasMask = hasMask,
				AlbedoHasAlpha = albedoHasAlpha,
			};
		}

		/// <summary>
		/// Writes this layer into slot <paramref name="index"/> of the five shader arrays.
		/// </summary>
		/// <remarks>
		/// <list type="bullet">
		/// <item><c>st</c>: 1/tile size, offset/tile size — applied to WORLD x/z, so a texture runs straight across tile seams.</item>
		/// <item><c>tint</c>: diffuse remap max rgb, normal scale in w.</item>
		/// <item><c>maskOffset</c>, <c>maskScale</c>: the mask remap as sample * scale + offset.</item>
		/// <item><c>surface</c>: metallic, smoothness multiplier, has-mask flag, 0.</item>
		/// </list>
		/// A tile size of zero or less would divide by zero; it is clamped to a millimetre, which
		/// shows as obvious noise rather than a NaN that blacks out the ground.
		/// </remarks>
		public void Pack(int index, Vector4[] st, Vector4[] tint, Vector4[] maskOffset, Vector4[] maskScale, Vector4[] surface)
		{
			float sx = Mathf.Max(1e-3f, TileSize.x);
			float sy = Mathf.Max(1e-3f, TileSize.y);
			st[index] = new Vector4(1f / sx, 1f / sy, TileOffset.x / sx, TileOffset.y / sy);
			tint[index] = new Vector4(DiffuseRemapMax.x, DiffuseRemapMax.y, DiffuseRemapMax.z, NormalScale);
			maskOffset[index] = MaskRemapMin;
			maskScale[index] = MaskRemapMax - MaskRemapMin;
			surface[index] = new Vector4(Metallic, AlbedoHasAlpha ? 1f : Smoothness, HasMask ? 1f : 0f, 0f);
		}

		/// <summary>Reads slot <paramref name="index"/> back out of the five arrays: the inverse of <see cref="Pack"/>, for tests and the inspector.</summary>
		public static TerrainArrayLayerParams Unpack(int index, Vector4[] st, Vector4[] tint, Vector4[] maskOffset, Vector4[] maskScale, Vector4[] surface)
		{
			Vector4 s = st[index];
			var size = new Vector2(1f / s.x, 1f / s.y);
			return new TerrainArrayLayerParams
			{
				TileSize = size,
				TileOffset = new Vector2(s.z * size.x, s.w * size.y),
				NormalScale = tint[index].w,
				DiffuseRemapMax = new Vector4(tint[index].x, tint[index].y, tint[index].z, 1f),
				MaskRemapMin = maskOffset[index],
				MaskRemapMax = maskOffset[index] + maskScale[index],
				Metallic = surface[index].x,
				// The packed multiplier cannot tell "1 because the alpha holds it" from "a slider at 1",
				// so it comes back as the slider and the flag as false.
				Smoothness = surface[index].y,
				HasMask = surface[index].z > 0.5f,
				AlbedoHasAlpha = false,
			};
		}

		/// <summary>Five arrays of <see cref="MaximumLayers"/>, with every unused slot holding a neutral layer.</summary>
		/// <remarks>
		/// Always the full length: a material property array's size is fixed by the first array set on
		/// it, so a scene that grows from 4 layers to 12 must not have been sized at 4.
		/// </remarks>
		public static void Allocate(out Vector4[] st, out Vector4[] tint, out Vector4[] maskOffset, out Vector4[] maskScale, out Vector4[] surface)
		{
			st = new Vector4[MaximumLayers];
			tint = new Vector4[MaximumLayers];
			maskOffset = new Vector4[MaximumLayers];
			maskScale = new Vector4[MaximumLayers];
			surface = new Vector4[MaximumLayers];
			TerrainArrayLayerParams neutral = Default;
			for (int i = 0; i < MaximumLayers; i++)
			{
				neutral.Pack(i, st, tint, maskOffset, maskScale, surface);
			}
		}
	}
}
