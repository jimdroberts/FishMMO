using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// How much of the ground round the camera stands under blade grass, for the ground's shaders: a world-aligned R8 map,
	/// 256 m at the density maps' own 2 m, published as <c>_FishSwardTex</c> with <c>_FishSwardRect</c> and
	/// <c>_FishSwardParams</c> (FishWeather.hlsl FishSwardShade).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> A blade is shaded toward its root (<see cref="GrassBladeSettings.RootOcclusion"/>: dense grass shades its
	/// own base), and the ground it stands in was not: the soil a hand's width from a root caught the whole sun and the whole
	/// sky, so between the blades the ground shone brighter than the grass growing out of it (2026-10-11). The ground under
	/// a sward now takes the roots' shade, by how closed the sward is, and lets it go where the blades let theirs go — in the
	/// far blend, where blades are lit flat as the ground they cover — so near and far agree.
	/// </para>
	/// <para>
	/// <b>Cheap.</b> Filled on the CPU from each grass terrain's <see cref="GrassTerrain.Sward"/> only when the camera has
	/// moved <see cref="RecentreMetres"/> or a terrain's grass was built or released; the shaders read it once a pixel through
	/// the cover map's sampler, because the ground's passes are near the limit of samplers.
	/// </para>
	/// </remarks>
	public static class GrassSwardMap
	{
		public const int Resolution = 128;
		public const float TexelMetres = 2f;
		public const float SizeMetres = Resolution * TexelMetres;
		/// <summary>How far the camera moves before the map is filled again round it.</summary>
		public const float RecentreMetres = 16f;

		private static readonly int TexId = Shader.PropertyToID("_FishSwardTex");
		private static readonly int RectId = Shader.PropertyToID("_FishSwardRect");
		private static readonly int ParamsId = Shader.PropertyToID("_FishSwardParams");

		private static Texture2D texture;
		private static byte[] pixels;
		private static Vector2 corner;
		private static bool filled;
		private static int filledFor = int.MinValue;
		private static float filledFull = -1f;

		/// <summary>
		/// Fills the map round <paramref name="eye"/> when it has moved or the grass changed, and publishes the shade the
		/// blades' settings call for. Called once a frame by <see cref="GrassBladeSystem"/> while it draws.
		/// </summary>
		public static void Update(GrassBladeSettings settings, float grassDistance, IReadOnlyList<GrassTerrain> terrains, Vector3 eye)
		{
			if (settings == null || terrains == null || settings.GroundShade <= 0f)
			{
				Clear();
				return;
			}
			Ensure();
			int fingerprint = Fingerprint(terrains);
			Vector2 wanted = new Vector2(
				Mathf.Floor((eye.x - SizeMetres * 0.5f) / TexelMetres) * TexelMetres,
				Mathf.Floor((eye.z - SizeMetres * 0.5f) / TexelMetres) * TexelMetres);
			// The sward closes as the blades thicken: at the coverage that grows the full field (FishGrassBlades.compute).
			float full = Mathf.Clamp(settings.FullCoverage, 0.05f, 1f);
			if (!filled || fingerprint != filledFor || full != filledFull || (wanted - corner).sqrMagnitude >= RecentreMetres * RecentreMetres)
			{
				Fill(terrains, wanted, full);
				corner = wanted;
				filledFor = fingerprint;
				filledFull = full;
				filled = true;
			}
			// The blades' own far blend (FishGrassBlades.hlsl): from FarBlendStart to the share of the grass distance.
			float start = settings.FarBlendStart;
			float end = Mathf.Max(start + 1f, grassDistance * settings.FarBlendEnd);
			Shader.SetGlobalTexture(TexId, texture);
			Shader.SetGlobalVector(RectId, new Vector4(corner.x, corner.y, 1f / SizeMetres, 1f / SizeMetres));
			// x the light the ground keeps under a closed sward: the roots' own, by how much of it the ground takes.
			float kept = Mathf.Lerp(1f, settings.RootOcclusion, Mathf.Clamp01(settings.GroundShade));
			Shader.SetGlobalVector(ParamsId, new Vector4(kept, start, end, 1f));
		}

		/// <summary>The ground is lit as open ground again (no blades drawn, or the shade turned off).</summary>
		public static void Clear()
		{
			Shader.SetGlobalVector(ParamsId, Vector4.zero);
			filled = false;
		}

		/// <summary>How much of the ground at a world point is under blades, as the map holds it (0 off it): for tests and the probes.</summary>
		public static float SampleAt(float x, float z)
		{
			if (!filled || pixels == null)
			{
				return 0f;
			}
			int ix = Mathf.FloorToInt((x - corner.x) / TexelMetres), iz = Mathf.FloorToInt((z - corner.y) / TexelMetres);
			return ix < 0 || iz < 0 || ix >= Resolution || iz >= Resolution ? 0f : pixels[iz * Resolution + ix] / 255f;
		}

		private static void Ensure()
		{
			if (texture != null)
			{
				return;
			}
			texture = new Texture2D(Resolution, Resolution, TextureFormat.R8, false, true)
			{
				name = "Grass Sward",
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave,
			};
			pixels = new byte[Resolution * Resolution];
		}

		/// <summary>The terrains and whether each has its sward yet, as one number: a terrain built or released changes it.</summary>
		private static int Fingerprint(IReadOnlyList<GrassTerrain> terrains)
		{
			unchecked
			{
				int h = 17 + terrains.Count;
				for (int i = 0; i < terrains.Count; i++)
				{
					GrassTerrain gt = terrains[i];
					h = h * 31 + (gt != null ? gt.GetHashCode() : 0);
					h = h * 31 + (gt != null && gt.Sward != null ? 1 : 0);
				}
				return h;
			}
		}

		private static void Fill(IReadOnlyList<GrassTerrain> terrains, Vector2 at, float full)
		{
			GrassTerrain last = null;
			for (int z = 0; z < Resolution; z++)
			{
				float wz = at.y + (z + 0.5f) * TexelMetres;
				for (int x = 0; x < Resolution; x++)
				{
					float wx = at.x + (x + 0.5f) * TexelMetres;
					// The terrain the last texel was on is the likeliest for this one.
					if (last == null || !Contains(last, wx, wz))
					{
						last = null;
						for (int i = 0; i < terrains.Count; i++)
						{
							if (terrains[i] != null && Contains(terrains[i], wx, wz))
							{
								last = terrains[i];
								break;
							}
						}
					}
					pixels[z * Resolution + x] = last != null ? (byte)Mathf.RoundToInt(Mathf.Min(1f, last.SwardAt(wx, wz) / full) * 255f) : (byte)0;
				}
			}
			texture.SetPixelData(pixels, 0);
			texture.Apply(false, false);
		}

		private static bool Contains(GrassTerrain gt, float x, float z) =>
			x >= gt.Origin.x && z >= gt.Origin.z && x <= gt.Origin.x + gt.Size.x && z <= gt.Origin.z + gt.Size.z;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			filled = false;
			filledFor = int.MinValue;
			filledFull = -1f;
		}
	}
}
