using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// <see cref="Shader.Find"/> that also finds shaders loaded from Addressable bundles.
	/// </summary>
	/// <remarks>
	/// <para>
	/// In a player, <see cref="Shader.Find"/> only knows the shaders built into the player (scenes in the build list,
	/// Resources, Always Included). The client's world content (terrain arrays, the weather profile, scenes) arrives
	/// in Addressable bundles, and its shaders were not found by name there even once loaded: on the web build
	/// (2026-11-08) the stored terrain index maps were not uploaded because a lookup by name returned null for the
	/// shader the loaded terrain set carried, and passing the set's reference fixed it. The editor and the benchmark
	/// players never showed it: there every shader is reachable by name.
	/// </para>
	/// <para>
	/// A miss falls back to the loaded shaders themselves (<see cref="Resources.FindObjectsOfTypeAll{T}"/>), every name
	/// indexed at once, at most once per <see cref="RescanSeconds"/> so a caller asking each frame for a shader not
	/// loaded yet does not walk every object each frame. Found shaders are cached; a destroyed one is looked up again.
	/// Prefer a serialized reference where there is one; this is for the lookups by name.
	/// </para>
	/// </remarks>
	public static class ShaderLookup
	{
		/// <summary>Least time between two walks of the loaded shaders.</summary>
		public const float RescanSeconds = 1f;

		private static readonly Dictionary<string, Shader> byName = new Dictionary<string, Shader>(StringComparer.Ordinal);
		private static float lastScan = float.NegativeInfinity;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			byName.Clear();
			lastScan = float.NegativeInfinity;
		}

		/// <summary>The shader named <paramref name="name"/>, built into the player or loaded from a bundle; null if neither.</summary>
		public static Shader Find(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}
			if (byName.TryGetValue(name, out Shader cached) && cached != null)
			{
				return cached;
			}
			Shader found = Shader.Find(name);
			if (found != null)
			{
				byName[name] = found;
				return found;
			}
			float now = Time.realtimeSinceStartup;
			if (now - lastScan < RescanSeconds)
			{
				return null;
			}
			lastScan = now;
			foreach (Shader shader in Resources.FindObjectsOfTypeAll<Shader>())
			{
				if (shader != null)
				{
					byName[shader.name] = shader;
				}
			}
			return byName.TryGetValue(name, out cached) && cached != null ? cached : null;
		}
	}
}
