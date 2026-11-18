#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The colour each biome's ground reads as from far off: what the backdrop past a scene's edge
	/// is painted with, so the horizon carries the same biomes as the ground underfoot.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A texture's mean, not its detail.</b> The backdrop is seen from kilometres away, where a
	/// terrain texture has already mipped down to its average colour; painting the average is
	/// therefore exactly what the real ground would look like there, at a fraction of the cost.
	/// </para>
	/// <para>
	/// <b>Committed art only.</b> The backdrop's texture is written beside the scene and committed,
	/// so it is coloured from the committed layers and never from anything in <c>Assets/LOCAL</c>:
	/// a backdrop baked on one machine must not differ from the same scene baked on another.
	/// </para>
	/// </remarks>
	public sealed class BiomeGroundColours
	{
		/// <summary>Rock for a biome with no cliff layer: the same grey the plain bands use.</summary>
		private static readonly Color FallbackCliff = GeneratedTerrainLayers.Colours[GeneratedTerrainLayers.Rock];

		private readonly SceneBiomeField field;
		private readonly Color[] ground;
		private readonly Color[] cliff;
		private readonly CliffTextureLayer[] cliffLayer;
		private readonly float[] weights;
		private static readonly Dictionary<Texture, Color> means = new Dictionary<Texture, Color>();

		/// <summary>Forgets every average taken, so a bake after the art changed reads it again.</summary>
		public static void ClearCache()
		{
			means.Clear();
		}

		/// <summary>
		/// A biome's ground colour from its own main art. False when it has none, so a caller can
		/// fall back to something better than a placeholder's flat colour.
		/// </summary>
		public static bool TryMainColour(BiomeTemplate biome, Func<TerrainTextureLayer, TerrainLayer> resolve, out Color colour)
		{
			colour = default;
			TerrainLayer main = biome != null && biome.MainTextureLayer != null && biome.MainTextureLayer.HasAlbedo
				? resolve(biome.MainTextureLayer)
				: null;
			if (main == null || main.diffuseTexture == null)
			{
				return false;
			}
			colour = Mean(main.diffuseTexture);
			return true;
		}

		/// <summary>
		/// A biome's bare-rock colour: the mean of its first cliff layer that has art, else the grey the
		/// plain bands use for rock — the same choice the per-scene colours make.
		/// </summary>
		/// <remarks>For the globe bake, which shows rock through a biome's ground where the slope is steep.</remarks>
		public static Color CliffColour(BiomeTemplate biome, Func<TerrainTextureLayer, TerrainLayer> resolve)
		{
			if (biome != null && biome.CliffTextureLayers != null)
			{
				foreach (CliffTextureLayer layer in biome.CliffTextureLayers)
				{
					TerrainLayer resolved = layer != null && layer.HasAlbedo ? resolve(layer) : null;
					if (resolved != null)
					{
						return Mean(resolved.diffuseTexture);
					}
				}
			}
			return FallbackCliff;
		}

		/// <param name="field">The biomes over the area to colour.</param>
		/// <param name="resolve">The terrain layer a texture layer draws with (committed art).</param>
		/// <param name="placeholder">The stand-in for a biome with no main art.</param>
		public BiomeGroundColours(SceneBiomeField field, Func<TerrainTextureLayer, TerrainLayer> resolve, Func<BiomeTemplate, TerrainLayer> placeholder)
		{
			this.field = field ?? throw new ArgumentNullException(nameof(field));
			int count = field.Biomes.Count;
			ground = new Color[count];
			cliff = new Color[count];
			cliffLayer = new CliffTextureLayer[count];
			weights = new float[Mathf.Max(1, count)];

			for (int b = 0; b < count; b++)
			{
				BiomeTemplate biome = field.Biomes[b];
				TerrainLayer main = biome.MainTextureLayer != null && biome.MainTextureLayer.HasAlbedo ? resolve(biome.MainTextureLayer) : null;
				if (main == null && placeholder != null)
				{
					main = placeholder(biome);
				}
				ground[b] = main != null ? Mean(main.diffuseTexture) : GeneratedTerrainLayers.Colours[GeneratedTerrainLayers.Grass];

				cliff[b] = FallbackCliff;
				if (biome.CliffTextureLayers != null)
				{
					foreach (CliffTextureLayer layer in biome.CliffTextureLayers)
					{
						TerrainLayer resolved = layer != null && layer.HasAlbedo ? resolve(layer) : null;
						if (resolved != null)
						{
							cliff[b] = Mean(resolved.diffuseTexture);
							cliffLayer[b] = layer;
							break;
						}
					}
				}
			}
		}

		/// <summary>The ground's colour at a world position.</summary>
		/// <param name="steepnessDegrees">The slope there.</param>
		/// <param name="normalizedHeight">The biome system's height there, for a cliff layer's height band.</param>
		public Color At(float worldX, float worldZ, float steepnessDegrees, float normalizedHeight)
		{
			field.WeightsAt(worldX, worldZ, weights);
			Color colour = Color.black;
			float total = 0f;
			for (int b = 0; b < field.Biomes.Count; b++)
			{
				float w = weights[b];
				if (w <= 1e-4f)
				{
					continue;
				}
				// A biome's own cliff curve when it has one; the plain bands' rock ramp otherwise.
				float steep = cliffLayer[b] != null
					? cliffLayer[b].GetCliffWeight(steepnessDegrees, normalizedHeight)
					: Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(28f, 48f, steepnessDegrees));
				colour += Color.Lerp(ground[b], cliff[b], steep) * w;
				total += w;
			}
			if (total <= 1e-4f)
			{
				return GeneratedTerrainLayers.Colours[GeneratedTerrainLayers.Grass];
			}
			colour /= total;
			colour.a = 1f;
			return colour;
		}

		/// <summary>A texture's average colour, read through the GPU so it need not be readable.</summary>
		private static Color Mean(Texture texture)
		{
			if (texture == null)
			{
				return GeneratedTerrainLayers.Colours[GeneratedTerrainLayers.Grass];
			}
			if (means.TryGetValue(texture, out Color cached))
			{
				return cached;
			}

			// A readable texture answers from its smallest mip, and a batch editor started with
			// -nographics has no GPU to read through, so there it is the only answer there is.
			if (texture is Texture2D readable && readable.isReadable)
			{
				Color[] mip = readable.GetPixels(Mathf.Max(0, readable.mipmapCount - 1));
				Color total = Color.black;
				for (int i = 0; i < mip.Length; i++)
				{
					total += mip[i];
				}
				Color average = mip.Length > 0 ? total / mip.Length : GeneratedTerrainLayers.Colours[GeneratedTerrainLayers.Grass];
				average.a = 1f;
				means[texture] = average;
				return average;
			}
			if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
			{
				Debug.LogWarning($"[Scene generator] No graphics device to average '{texture.name}' through; the backdrop paints it as plain grass.");
				Color plain = GeneratedTerrainLayers.Colours[GeneratedTerrainLayers.Grass];
				means[texture] = plain;
				return plain;
			}

			/* Halved repeatedly rather than squeezed in one blit: a single bilinear blit to a few
			 * texels samples four source texels per output and ignores the rest, so its "mean"
			 * is whatever four points it happened to land on. */
			const int Size = 8;
			RenderTexture previous = RenderTexture.active;
			int w = Mathf.Max(Size, texture.width), h = Mathf.Max(Size, texture.height);
			RenderTexture source = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
			Graphics.Blit(texture, source);
			while (w > Size || h > Size)
			{
				w = Mathf.Max(Size, w / 2);
				h = Mathf.Max(Size, h / 2);
				RenderTexture half = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
				half.filterMode = FilterMode.Bilinear;
				source.filterMode = FilterMode.Bilinear;
				Graphics.Blit(source, half);
				RenderTexture.ReleaseTemporary(source);
				source = half;
			}

			var read = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
			RenderTexture.active = source;
			read.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
			read.Apply(false, false);
			RenderTexture.active = previous;
			RenderTexture.ReleaseTemporary(source);

			Color sum = Color.black;
			Color[] pixels = read.GetPixels();
			for (int i = 0; i < pixels.Length; i++)
			{
				sum += pixels[i];
			}
			UnityEngine.Object.DestroyImmediate(read);
			Color mean = pixels.Length > 0 ? sum / pixels.Length : GeneratedTerrainLayers.Colours[GeneratedTerrainLayers.Grass];
			mean.a = 1f;
			means[texture] = mean;
			return mean;
		}
	}
}
#endif
