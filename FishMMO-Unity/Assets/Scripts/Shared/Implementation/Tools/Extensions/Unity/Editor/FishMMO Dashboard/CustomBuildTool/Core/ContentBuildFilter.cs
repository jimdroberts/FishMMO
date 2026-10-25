#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.CustomBuildTool.Core
{
	public static class ContentBuildFilter
	{
		/// <summary>The same group policy must be applied before collecting generated dependencies.</summary>
		public static bool IncludesGroup(string name, IReadOnlyList<string> exclusions)
		{
			if (exclusions == null) return true;
			foreach (string exclusion in exclusions)
			{
				if (!string.IsNullOrEmpty(exclusion) &&
					name.IndexOf(exclusion, StringComparison.OrdinalIgnoreCase) >= 0) return false;
			}
			return true;
		}

		/// <summary>
		/// Rendering-only generated assets need not be explicit server bundle roots.
		/// Meshes, prefabs, terrain and gameplay ScriptableObjects must remain reachable.
		/// This does not delete implicit dependencies of retained gameplay assets.
		/// </summary>
		public static bool IsRenderingOnly(Type type) => type != null &&
			(typeof(Material).IsAssignableFrom(type) || typeof(Texture).IsAssignableFrom(type) ||
			 typeof(Shader).IsAssignableFrom(type) || typeof(ComputeShader).IsAssignableFrom(type) ||
			 typeof(ShaderVariantCollection).IsAssignableFrom(type) || typeof(TerrainLayer).IsAssignableFrom(type));
	}
}
#endif
