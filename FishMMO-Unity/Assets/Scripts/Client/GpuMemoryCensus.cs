using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// What the client holds in GPU memory, as rows in the log: every loaded texture, render target and mesh, sized from
	/// its real format, dimensions, mips, slices and samples, then totalled by kind, by format and by name family, with
	/// the largest items listed. Logged 15, 45 and 90 s after a world scene loads (the biome art arrives in between).
	/// Search the log (the browser console on the web) for "[GPU census]".
	/// </summary>
	/// <remarks>
	/// <para>
	/// Off unless asked for, because it walks every loaded object: add <c>?gpumem</c> to the page URL on the web, pass
	/// <c>-gpumem</c> to a player, or set <c>FISHMMO_GPU_CENSUS=1</c>. A release build needs no development flag: the
	/// sizes are computed, not read from the profiler (which reports nothing outside development builds).
	/// </para>
	/// <para>
	/// What it cannot see: GraphicsBuffers and ComputeBuffers (Unity has no way to list them), the render pipeline's
	/// transient targets, and the browser's own overhead. Compare its total with the GPU process's real use (nvidia-smi);
	/// the gap is those. On the web it also logs the WebGPU device's features: without <c>texture-compression-bc</c>,
	/// Unity expands every DXT/BC texture to RGBA on upload, which the "by format" row then shows.
	/// </para>
	/// </remarks>
	public sealed class GpuMemoryCensus : MonoBehaviour
	{
		private static readonly float[] AfterWorldLoad = { 15f, 45f, 90f };
		private const int LargestShown = 30;
		private const int FamiliesShown = 25;
		private const int FormatsShown = 15;

		private static GpuMemoryCensus instance;
		private readonly List<(float At, string Reason)> due = new List<(float, string)>();

		/// <summary>True when this run asked for the census (URL, command line or environment).</summary>
		public static bool Enabled
		{
			get
			{
				if (Environment.GetEnvironmentVariable("FISHMMO_GPU_CENSUS") == "1")
				{
					return true;
				}
				foreach (string arg in Environment.GetCommandLineArgs())
				{
					if (string.Equals(arg, "-gpumem", StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
				string url = Application.absoluteURL;
				return !string.IsNullOrEmpty(url) && url.IndexOf("gpumem", StringComparison.OrdinalIgnoreCase) >= 0;
			}
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			instance = null;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (instance != null || !Application.isPlaying || !Enabled)
			{
				return;
			}
			var host = new GameObject("GPU Memory Census") { hideFlags = HideFlags.HideAndDontSave };
			DontDestroyOnLoad(host);
			instance = host.AddComponent<GpuMemoryCensus>();
			Debug.Log($"[GPU census] on. Device: {DescribeDevice()}");
		}

		private void OnEnable()
		{
			SceneManager.sceneLoaded += OnSceneLoaded;
		}

		private void OnDisable()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
		}

		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (!WorldSceneSettings.TryGetForScene(scene, out _))
			{
				return;
			}
			float now = Time.unscaledTime;
			foreach (float seconds in AfterWorldLoad)
			{
				due.Add((now + seconds, $"{scene.name} +{seconds:0} s"));
			}
		}

		private void Update()
		{
			for (int i = due.Count - 1; i >= 0; i--)
			{
				if (Time.unscaledTime >= due[i].At)
				{
					string reason = due[i].Reason;
					due.RemoveAt(i);
					Debug.Log(Report(reason));
				}
			}
		}

		private struct Item
		{
			public string Name;
			public string Kind;
			public string Shape;
			public string Format;
			public long Bytes;
		}

		/// <summary>The census of this moment, as one multi-line log message.</summary>
		public static string Report(string reason)
		{
			var items = new List<Item>(8192);
			long readableBytes = 0;
			int readable = 0;
			foreach (Texture texture in Resources.FindObjectsOfTypeAll<Texture>())
			{
				if (texture == null)
				{
					continue;
				}
				Item item = Measure(texture);
				if (item.Bytes <= 0)
				{
					continue;
				}
				items.Add(item);
				if (texture.isReadable && !(texture is RenderTexture))
				{
					readable++;
					readableBytes += item.Bytes;
				}
			}
			foreach (Mesh mesh in Resources.FindObjectsOfTypeAll<Mesh>())
			{
				if (mesh == null)
				{
					continue;
				}
				long bytes = MeshBytes(mesh);
				if (bytes > 0)
				{
					items.Add(new Item { Name = mesh.name, Kind = "Mesh", Shape = $"{mesh.vertexCount} verts", Format = "mesh", Bytes = bytes });
				}
			}

			var byKind = new Dictionary<string, (long Bytes, int Count)>();
			var byFormat = new Dictionary<string, (long Bytes, int Count)>();
			var byFamily = new Dictionary<string, (long Bytes, int Count)>();
			long total = 0;
			foreach (Item item in items)
			{
				total += item.Bytes;
				Add(byKind, item.Kind, item.Bytes);
				Add(byFormat, item.Format, item.Bytes);
				Add(byFamily, $"{item.Kind}: {Family(item.Name)}", item.Bytes);
			}
			items.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));

			var text = new StringBuilder(8192);
			text.Append($"[GPU census] {reason}: {Mb(total)} known in {items.Count} object(s) (buffers and the browser's own use are not counted)");
			text.Append($"\n  by kind: {Ranked(byKind, int.MaxValue)}");
			text.Append($"\n  by format: {Ranked(byFormat, FormatsShown)}");
			text.Append($"\n  readable textures (also a CPU copy in the heap): {readable}, {Mb(readableBytes)}");
			text.Append($"\n  formats: DXT5 {SystemInfo.SupportsTextureFormat(TextureFormat.DXT5)}, BC7 {SystemInfo.SupportsTextureFormat(TextureFormat.BC7)}, BC5 {SystemInfo.SupportsTextureFormat(TextureFormat.BC5)}; graphicsMemorySize {SystemInfo.graphicsMemorySize} MB");
			text.Append("\n  largest families:");
			foreach (KeyValuePair<string, (long Bytes, int Count)> family in Top(byFamily, FamiliesShown))
			{
				text.Append($"\n    {Mb(family.Value.Bytes),9} ×{family.Value.Count,-5} {family.Key}");
			}
			text.Append("\n  largest objects:");
			for (int i = 0; i < items.Count && i < LargestShown; i++)
			{
				Item item = items[i];
				text.Append($"\n    {Mb(item.Bytes),9}  {item.Kind,-16} {item.Shape,-22} {item.Format,-26} {(string.IsNullOrEmpty(item.Name) ? "(unnamed)" : item.Name)}");
			}
			return text.ToString();
		}

		private static Item Measure(Texture texture)
		{
			var item = new Item { Name = texture.name, Kind = texture.GetType().Name };
			int w = texture.width, h = texture.height;
			if (w <= 0 || h <= 0)
			{
				return item;
			}
			if (texture is RenderTexture rt)
			{
				if (!rt.IsCreated())
				{
					return item;
				}
				int slices = Mathf.Max(1, rt.volumeDepth);
				int samples = Mathf.Max(1, rt.antiAliasing);
				int mips = rt.useMipMap ? rt.mipmapCount : 1;
				long colour = 0;
				if (rt.graphicsFormat != GraphicsFormat.None)
				{
					colour = rt.dimension == UnityEngine.Rendering.TextureDimension.Tex3D
						? (long)GraphicsFormatUtility.ComputeMipChainSize(w, h, slices, rt.graphicsFormat, mips)
						: (long)GraphicsFormatUtility.ComputeMipChainSize(w, h, rt.graphicsFormat, mips) * slices;
					if (rt.dimension == UnityEngine.Rendering.TextureDimension.Cube)
					{
						colour *= 6;
					}
					// Multisampled colour keeps every sample, and a resolve target unless the samples are bound directly.
					colour = colour * samples + (samples > 1 && !rt.bindTextureMS ? colour : 0);
				}
				long depth = 0;
				if (rt.depthStencilFormat != GraphicsFormat.None)
				{
					depth = (long)w * h * GraphicsFormatUtility.GetBlockSize(rt.depthStencilFormat) * samples * (rt.dimension == UnityEngine.Rendering.TextureDimension.Tex3D ? 1 : slices);
				}
				item.Bytes = colour + depth;
				item.Kind = samples > 1 ? $"RenderTexture×{samples}" : "RenderTexture";
				item.Shape = slices > 1 ? $"{w}×{h}×{slices}" : $"{w}×{h}";
				item.Format = rt.graphicsFormat != GraphicsFormat.None ? rt.graphicsFormat.ToString() : rt.depthStencilFormat.ToString();
				return item;
			}
			GraphicsFormat format = texture.graphicsFormat;
			if (format == GraphicsFormat.None)
			{
				return item;
			}
			int mipCount = Mathf.Max(1, texture.mipmapCount);
			long slice = (long)GraphicsFormatUtility.ComputeMipChainSize(w, h, format, mipCount);
			switch (texture)
			{
				case Texture2DArray array:
					item.Bytes = slice * array.depth;
					item.Shape = $"{w}×{h}×{array.depth}";
					break;
				case Texture3D volume:
					item.Bytes = (long)GraphicsFormatUtility.ComputeMipChainSize(w, h, volume.depth, format, mipCount);
					item.Shape = $"{w}×{h}×{volume.depth}";
					break;
				case Cubemap _:
					item.Bytes = slice * 6;
					item.Shape = $"{w}² cube";
					break;
				case CubemapArray cubes:
					item.Bytes = slice * 6 * cubes.cubemapCount;
					item.Shape = $"{w}² cube×{cubes.cubemapCount}";
					break;
				default:
					item.Bytes = slice;
					item.Shape = $"{w}×{h}";
					break;
			}
			item.Format = format.ToString();
			return item;
		}

		private static long MeshBytes(Mesh mesh)
		{
			if (mesh.vertexCount <= 0)
			{
				return 0;
			}
			long bytes = 0;
			for (int stream = 0; stream < mesh.vertexBufferCount; stream++)
			{
				bytes += (long)mesh.vertexCount * mesh.GetVertexBufferStride(stream);
			}
			long indices = 0;
			for (int sub = 0; sub < mesh.subMeshCount; sub++)
			{
				indices += mesh.GetIndexCount(sub);
			}
			return bytes + indices * (mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32 ? 4 : 2);
		}

		private static readonly Regex Digits = new Regex(@"\d+", RegexOptions.Compiled);

		/// <summary>A name with its numbers folded, so tiles, slices and clones of one thing count together.</summary>
		private static string Family(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return "(unnamed)";
			}
			string folded = Digits.Replace(name, "#").Replace("(Clone)", string.Empty).Trim();
			return folded.Length > 60 ? folded.Substring(0, 60) : folded;
		}

		private static void Add(Dictionary<string, (long Bytes, int Count)> into, string key, long bytes)
		{
			into.TryGetValue(key, out (long Bytes, int Count) sum);
			into[key] = (sum.Bytes + bytes, sum.Count + 1);
		}

		private static List<KeyValuePair<string, (long Bytes, int Count)>> Top(Dictionary<string, (long Bytes, int Count)> from, int count)
		{
			var list = new List<KeyValuePair<string, (long Bytes, int Count)>>(from);
			list.Sort((a, b) => b.Value.Bytes.CompareTo(a.Value.Bytes));
			if (list.Count > count)
			{
				list.RemoveRange(count, list.Count - count);
			}
			return list;
		}

		private static string Ranked(Dictionary<string, (long Bytes, int Count)> from, int count)
		{
			var parts = new List<string>();
			foreach (KeyValuePair<string, (long Bytes, int Count)> pair in Top(from, count))
			{
				parts.Add($"{pair.Key} {Mb(pair.Value.Bytes)} ×{pair.Value.Count}");
			}
			return string.Join(", ", parts);
		}

		private static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):0.0} MB";

		/// <summary>The WebGPU device's features and limits on the web; the graphics API elsewhere.</summary>
		private static string DescribeDevice()
		{
#if UNITY_WEBGL && !UNITY_EDITOR
			return FishMMOWebGpuDescribe();
#else
			return $"{SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsMemorySize} MB";
#endif
		}

#if UNITY_WEBGL && !UNITY_EDITOR
		[DllImport("__Internal")]
		private static extern string FishMMOWebGpuDescribe();
#endif
	}
}
