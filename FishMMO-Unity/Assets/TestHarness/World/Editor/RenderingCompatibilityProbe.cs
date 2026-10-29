#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using FishMMO.Shared;

namespace FishMMO.TestHarness.World.Editor
{
	/// <summary>Device capability and imported shader diagnostics, without opening or saving a scene.</summary>
	public static class RenderingCompatibilityProbe
	{
		public static void Run()
		{
			var lines = new System.Collections.Generic.List<string>();
			lines.Add($"Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsDeviceName}");
			foreach (GraphicsFormat format in new[] { GraphicsFormat.R32G32_UInt, GraphicsFormat.R16G16B16A16_UInt, GraphicsFormat.R32G32B32A32_UInt, GraphicsFormat.R32_UInt, GraphicsFormat.R8G8B8A8_UNorm })
			{
				lines.Add($"{format}: Sample={SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sample)}, Linear={SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Linear)}, Render={SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render)}, LoadStore={SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.LoadStore)}");
				Texture2D tex = null;
				try
				{
					tex = new Texture2D(2, 2, format, TextureCreationFlags.DontInitializePixels);
					lines.Add($"  Texture2D actual={tex.graphicsFormat}, size={tex.width}x{tex.height}");
				}
				catch (Exception e) { lines.Add("  Texture2D failed: " + e.Message); }
				finally { if (tex != null) UnityEngine.Object.DestroyImmediate(tex); }
			}
			lines.Add("Index CanUpload=" + TerrainIndexMapBaker.CanUpload() + ", CanBake=" + TerrainIndexMapBaker.CanBake(Shader.Find(TerrainIndexMapBaker.ShaderName)));
			var methods = typeof(ShaderUtil).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (var method in methods.Where(m => m.Name.Contains("SRPBatcher"))) lines.Add(method.ToString());
			var code = methods.FirstOrDefault(m => m.Name == "GetSRPBatcherCompatibilityCode");
			var reason = methods.FirstOrDefault(m => m.Name == "GetSRPBatcherCompatibilityIssueReason");
			foreach (string name in new[] { "FishMMO/Water/Inland Water", "FishMMO/Water/Waterfall", "FishMMO/Water/Waterfall Spray" })
			{
				Shader shader = Shader.Find(name);
				if (shader == null) { lines.Add(name + ": not found"); continue; }
				try
				{
					lines.Add($"{name}: supported={shader.isSupported}, compatibility={code?.Invoke(null, new object[] { shader, 0 })}, reason={reason?.Invoke(null, new object[] { shader, 0, (int)(code?.Invoke(null, new object[] { shader, 0 }) ?? 0) })}");
				}
				catch (Exception e) { lines.Add(name + ": " + e.Message); }
			}
			string output = Environment.GetEnvironmentVariable("FISHMMO_COMPAT_OUT") ?? "/tmp/fish-rendering-compat.txt";
			File.WriteAllLines(output, lines);
			Debug.Log(string.Join("\n", lines));
			EditorApplication.Exit(0);
		}
	}
}
#endif
