#if UNITY_EDITOR
using System;
using System.Reflection;

namespace FishMMO.Shared.CustomBuildTool.Core
{
	/// <summary>Configure WASM optimization without requiring the Web module on native-only build hosts.</summary>
	public static class WebAssemblyCodeOptimization
	{
		private static PropertyInfo Setting => Type.GetType("UnityEditor.WebGL.UserBuildSettings, UnityEditor.WebGL.Extensions")
			?.GetProperty("codeOptimization", BindingFlags.Static | BindingFlags.Public);

		public static string Current
		{
			get => Setting?.GetValue(null)?.ToString();
			set
			{
				if (value == null) return;
				var setting = Setting ?? throw new InvalidOperationException("Install Unity Web build support before configuring WASM optimization.");
				setting.SetValue(null, Enum.Parse(setting.PropertyType, value));
			}
		}
	}
}
#endif
