using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// The texture arrays the grass and detail scatter atlases (GrassTerrainAtlas, DetailScatterAtlas) gather their terrains'
	/// maps into, slice by slice with <see cref="Graphics.CopyTexture(Texture, int, int, Texture, int, int)"/>.
	/// </summary>
	/// <remarks>
	/// GPU-only: a render-texture array has no copy in system memory and nothing to upload. A Texture2DArray did both, at
	/// every rebuild (a terrain taken on): tens of MB allocated, staged through Vulkan upload buffers and freed again a few
	/// frames later, once per terrain through the first half-minute of play, each a 50–130 ms hitch (StartupTimeline,
	/// 2026-10-13: VKUploadBuffer.Allocate and LargeAllocation.Free in every one). Where a format cannot be a render texture,
	/// a Texture2DArray whose system-memory copy is dropped at once.
	/// </remarks>
	public static class TerrainAtlasArrays
	{
		public static Texture NewArray(int resolution, int slices, GraphicsFormat format, string name)
		{
			resolution = Mathf.Max(1, resolution);
			slices = Mathf.Max(1, slices);
			if (SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render))
			{
				var descriptor = new RenderTextureDescriptor(resolution, resolution, format, GraphicsFormat.None)
				{
					dimension = TextureDimension.Tex2DArray,
					volumeDepth = slices,
					msaaSamples = 1,
					useMipMap = false,
					autoGenerateMips = false,
				};
				var texture = new RenderTexture(descriptor)
				{
					name = name,
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp,
					hideFlags = HideFlags.DontSave,
				};
				texture.Create();
				return texture;
			}
			var array = new Texture2DArray(resolution, resolution, slices, format, TextureCreationFlags.DontInitializePixels)
			{
				name = name,
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave,
			};
			array.Apply(false, true);
			return array;
		}

		/// <summary>The linear format a TextureFormat's texture is created with (the atlases' maps are all linear data).</summary>
		public static GraphicsFormat Linear(TextureFormat format) => GraphicsFormatUtility.GetGraphicsFormat(format, false);
	}
}
