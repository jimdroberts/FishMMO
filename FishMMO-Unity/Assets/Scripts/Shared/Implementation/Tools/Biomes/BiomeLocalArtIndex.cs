#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// Every <see cref="BiomeLocalArt"/> sidecar on this machine, by the biome it dresses.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Editor-only, and in the shared assembly rather than an editor one because two editor
	/// assemblies need it — the biome inspector (FishMMO.Shared.Biomes.Editor) and the terrain array
	/// baker (FishMMO.Shared.Tools.Editor) — and neither references the other.
	/// </para>
	/// <para>
	/// Built on first use and dropped whenever the project changes, so a sidecar created, deleted or
	/// edited outside the inspector is seen at the next look. Sidecars are found under
	/// <see cref="BiomeLocalArt.Folder"/> only: one elsewhere would be a LOCAL file outside LOCAL,
	/// which is exactly what the rule forbids.
	/// </para>
	/// </remarks>
	[InitializeOnLoad]
	public static class BiomeLocalArtIndex
	{
		private static Dictionary<BiomeTemplate, List<BiomeLocalArt>> byBiome;

		static BiomeLocalArtIndex()
		{
			EditorApplication.projectChanged += Invalidate;
		}

		/// <summary>Forgets the index; the next look rebuilds it.</summary>
		public static void Invalidate()
		{
			byBiome = null;
		}

		private static Dictionary<BiomeTemplate, List<BiomeLocalArt>> Index()
		{
			if (byBiome != null)
			{
				return byBiome;
			}
			byBiome = new Dictionary<BiomeTemplate, List<BiomeLocalArt>>();
			if (!AssetDatabase.IsValidFolder(BiomeLocalArt.Folder))
			{
				return byBiome;
			}
			var paths = new List<string>();
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(BiomeLocalArt), new[] { BiomeLocalArt.Folder }))
			{
				paths.Add(AssetDatabase.GUIDToAssetPath(guid));
			}
			// Path order, so with two sidecars for one biome the same one wins on every machine.
			paths.Sort(System.StringComparer.Ordinal);
			foreach (string path in paths)
			{
				var art = AssetDatabase.LoadAssetAtPath<BiomeLocalArt>(path);
				if (art == null || art.Biome == null)
				{
					continue;
				}
				if (!byBiome.TryGetValue(art.Biome, out List<BiomeLocalArt> list))
				{
					list = new List<BiomeLocalArt>();
					byBiome[art.Biome] = list;
				}
				list.Add(art);
			}
			return byBiome;
		}

		/// <summary>The biome's sidecar, or null. With several, the first by path.</summary>
		public static BiomeLocalArt For(BiomeTemplate biome)
		{
			return biome != null && Index().TryGetValue(biome, out List<BiomeLocalArt> list) && list.Count > 0 ? list[0] : null;
		}

		/// <summary>Every sidecar for a biome, first by path first. More than one is a mistake the inspector points out.</summary>
		public static IReadOnlyList<BiomeLocalArt> AllFor(BiomeTemplate biome)
		{
			return biome != null && Index().TryGetValue(biome, out List<BiomeLocalArt> list) ? list : (IReadOnlyList<BiomeLocalArt>)System.Array.Empty<BiomeLocalArt>();
		}

		/// <summary>
		/// The biome's override for one slot, or null: the first sidecar (by path) that has
		/// something in that slot.
		/// </summary>
		public static BiomeLocalArt.SlotOverride Find(BiomeTemplate biome, string slot)
		{
			foreach (BiomeLocalArt art in AllFor(biome))
			{
				BiomeLocalArt.SlotOverride entry = art.Find(slot);
				if (entry != null)
				{
					return entry;
				}
			}
			return null;
		}

		/// <summary>
		/// The biome's override for one spawn rule, or null: the first sidecar (by path) that has
		/// prefabs for it.
		/// </summary>
		public static BiomeLocalArt.RuleOverride FindRule(BiomeTemplate biome, string slot, PrefabSpawnRule rule)
		{
			foreach (BiomeLocalArt art in AllFor(biome))
			{
				BiomeLocalArt.RuleOverride entry = art.FindRule(slot, rule);
				if (entry != null)
				{
					return entry;
				}
			}
			return null;
		}

		/// <summary>
		/// The biome's sidecar, created under <see cref="BiomeLocalArt.Folder"/> on first use. The only
		/// write the LOCAL inspector makes: the template itself is never touched.
		/// </summary>
		public static BiomeLocalArt GetOrCreate(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return null;
			}
			BiomeLocalArt existing = For(biome);
			if (existing != null)
			{
				return existing;
			}
			EnsureFolder(BiomeLocalArt.Folder);
			var art = ScriptableObject.CreateInstance<BiomeLocalArt>();
			art.Biome = biome;
			string path = AssetDatabase.GenerateUniqueAssetPath($"{BiomeLocalArt.Folder}/{Sanitize(biome.name)} LOCAL Art.asset");
			art.name = Path.GetFileNameWithoutExtension(path);
			AssetDatabase.CreateAsset(art, path);
			AssetDatabase.SaveAssets();
			Invalidate();
			return art;
		}

		/// <summary>True when an asset lives under Assets/LOCAL (any capitalisation, as the gitignore matches it).</summary>
		public static bool IsLocal(Object asset)
		{
			return asset != null && IsLocalPath(AssetDatabase.GetAssetPath(asset));
		}

		/// <summary>True when a path is under Assets/LOCAL (any capitalisation, as the gitignore matches it).</summary>
		public static bool IsLocalPath(string path)
		{
			return !string.IsNullOrEmpty(path) && path.Replace('\\', '/').StartsWith("Assets/LOCAL", System.StringComparison.OrdinalIgnoreCase);
		}

		private static string Sanitize(string name)
		{
			foreach (char c in Path.GetInvalidFileNameChars())
			{
				name = name.Replace(c, '_');
			}
			return name;
		}

		private static void EnsureFolder(string folder)
		{
			if (AssetDatabase.IsValidFolder(folder))
			{
				return;
			}
			string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
			EnsureFolder(parent);
			AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
		}
	}
}
#endif
