using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;

namespace FishMMO.Water
{
	/// <summary>
	/// How far a scene's pack ice calms its sea, 0 (no swell gets through) … 1 (open water): a mask over the pack, read by
	/// the waves, the foam, the swash and the breakers on the GPU (<c>FishWaterIceCalm</c> in FishWaterSurf.hlsl) and by the
	/// water's heights on the CPU (<see cref="CalmAt"/>), so a floater and the drawn sea agree.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Pack ice damps a swell: a few floes' widths into close pack the sea under it is flat, and the leads between the
	/// floes are calm water. Built from the pack's floes (<see cref="PackIce.Floes"/>), each a disc, blurred over a few tens
	/// of metres so the calm comes on across the ice edge rather than at it.
	/// </para>
	/// <para>
	/// A separate factor from the open-sea map (<c>WaterShoreField</c>), never folded into it: the cut reads that map to
	/// decide where ice may lie, and an ice term in it would feed the ice back into its own placement.
	/// </para>
	/// </remarks>
	public static class WaterIceCalm
	{
		/// <summary>Texels along each side of the mask.</summary>
		public const int Resolution = 128;

		/// <summary>Open water kept about the pack in the mask, m: the calm fades out over it.</summary>
		public const float PadMetres = 80f;

		/// <summary>How far the calm spreads past the ice, in texels of blur.</summary>
		public const int BlurTexels = 3;

		/// <summary>What close pack leaves of the swell: almost none.</summary>
		public const float PackSwellShare = 0.05f;

		private static readonly int MaskId = Shader.PropertyToID("_FishWaterIceCalm");
		private static readonly int RectId = Shader.PropertyToID("_FishWaterIceCalmRect");

		private sealed class Mask
		{
			public PackIce Pack;
			public Rect Area;
			public float[] Calm;
			public Texture2D Texture;
		}

		private static readonly Dictionary<int, Mask> masks = new Dictionary<int, Mask>();
		private static bool listening;

		/// <summary>Builds (or drops) a scene's mask now and keeps it in step with its pack from here on.</summary>
		public static void Refresh(Scene scene)
		{
			if (!listening)
			{
				listening = true;
				PackIce.Changed += Refresh;
			}
			PackIce.TryGetForScene(scene, out PackIce pack);
			masks.TryGetValue(scene.handle, out Mask mask);
			if (mask != null && pack != null && ReferenceEquals(mask.Pack, pack))
			{
				Publish(mask);
				return;
			}
			if (mask != null)
			{
				Drop(scene, mask);
			}
			if (pack == null || pack.Floes == null || pack.Floes.Length == 0)
			{
				return;
			}
			mask = Build(pack);
			masks[scene.handle] = mask;
			Publish(mask);
		}

		/// <summary>How far the pack calms the sea at a point in a scene, 0 … 1; 1 off the pack or with none.</summary>
		public static float CalmAt(Scene scene, Vector2 xz)
		{
			if (!masks.TryGetValue(scene.handle, out Mask mask))
			{
				return 1f;
			}
			Rect a = mask.Area;
			float u = (xz.x - a.xMin) / a.width * Resolution - 0.5f;
			float v = (xz.y - a.yMin) / a.height * Resolution - 0.5f;
			if (u < -0.5f || v < -0.5f || u > Resolution - 0.5f || v > Resolution - 0.5f)
			{
				return 1f;
			}
			// Bilinear, as the shader's filtering reads it.
			int i = Mathf.Clamp(Mathf.FloorToInt(u), 0, Resolution - 2), j = Mathf.Clamp(Mathf.FloorToInt(v), 0, Resolution - 2);
			float fu = Mathf.Clamp01(u - i), fv = Mathf.Clamp01(v - j);
			float[] c = mask.Calm;
			int n = j * Resolution + i;
			return Mathf.Lerp(Mathf.Lerp(c[n], c[n + 1], fu), Mathf.Lerp(c[n + Resolution], c[n + Resolution + 1], fu), fv);
		}

		private static Mask Build(PackIce pack)
		{
			Rect area = pack.Area;
			area = Rect.MinMaxRect(area.xMin - PadMetres, area.yMin - PadMetres, area.xMax + PadMetres, area.yMax + PadMetres);
			var cover = new float[Resolution * Resolution];
			float tx = area.width / Resolution, tz = area.height / Resolution;
			foreach (PackIce.Floe floe in pack.Floes)
			{
				int i0 = Mathf.Max(0, Mathf.FloorToInt((floe.Centre.x - floe.Radius - area.xMin) / tx));
				int i1 = Mathf.Min(Resolution - 1, Mathf.FloorToInt((floe.Centre.x + floe.Radius - area.xMin) / tx));
				int j0 = Mathf.Max(0, Mathf.FloorToInt((floe.Centre.y - floe.Radius - area.yMin) / tz));
				int j1 = Mathf.Min(Resolution - 1, Mathf.FloorToInt((floe.Centre.y + floe.Radius - area.yMin) / tz));
				for (int j = j0; j <= j1; j++)
				{
					for (int i = i0; i <= i1; i++)
					{
						var p = new Vector2(area.xMin + (i + 0.5f) * tx, area.yMin + (j + 0.5f) * tz);
						if ((p - floe.Centre).sqrMagnitude <= floe.Radius * floe.Radius)
						{
							cover[j * Resolution + i] = 1f;
						}
					}
				}
			}
			Blur(cover);
			Blur(cover);
			var calm = new float[cover.Length];
			for (int k = 0; k < calm.Length; k++)
			{
				calm[k] = 1f - (1f - PackSwellShare) * Mathf.Clamp01(cover[k]);
			}
			var mask = new Mask { Pack = pack, Area = area, Calm = calm };
#if !UNITY_SERVER
			var texture = new Texture2D(Resolution, Resolution, TextureFormat.RHalf, false, true)
			{
				name = "Ice calm",
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
				hideFlags = HideFlags.HideAndDontSave,
			};
			var halves = new ushort[calm.Length];
			for (int k = 0; k < calm.Length; k++)
			{
				halves[k] = Mathf.FloatToHalf(calm[k]);
			}
			texture.SetPixelData(halves, 0);
			texture.Apply(false, true);
			mask.Texture = texture;
#endif
			return mask;
		}

		/// <summary>A box blur of <see cref="BlurTexels"/> each way, separable, in place.</summary>
		private static void Blur(float[] values)
		{
			var row = new float[Resolution];
			for (int pass = 0; pass < 2; pass++)
			{
				for (int line = 0; line < Resolution; line++)
				{
					for (int k = 0; k < Resolution; k++)
					{
						float sum = 0f;
						int count = 0;
						for (int d = -BlurTexels; d <= BlurTexels; d++)
						{
							int q = k + d;
							if (q < 0 || q >= Resolution)
							{
								continue;
							}
							sum += pass == 0 ? values[line * Resolution + q] : values[q * Resolution + line];
							count++;
						}
						row[k] = sum / count;
					}
					for (int k = 0; k < Resolution; k++)
					{
						if (pass == 0)
						{
							values[line * Resolution + k] = row[k];
						}
						else
						{
							values[k * Resolution + line] = row[k];
						}
					}
				}
			}
		}

		private static void Publish(Mask mask)
		{
			if (mask.Texture == null)
			{
				return;
			}
			Shader.SetGlobalTexture(MaskId, mask.Texture);
			Shader.SetGlobalVector(RectId, new Vector4(mask.Area.xMin, mask.Area.yMin, mask.Area.width, mask.Area.height));
		}

		private static void Drop(Scene scene, Mask mask)
		{
			masks.Remove(scene.handle);
			if (mask.Texture != null)
			{
				if (Application.isPlaying)
				{
					Object.Destroy(mask.Texture);
				}
				else
				{
					Object.DestroyImmediate(mask.Texture);
				}
			}
			Shader.SetGlobalVector(RectId, Vector4.zero);
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetOnLoad()
		{
			masks.Clear();
			if (listening)
			{
				PackIce.Changed -= Refresh;
				listening = false;
			}
		}
	}
}
