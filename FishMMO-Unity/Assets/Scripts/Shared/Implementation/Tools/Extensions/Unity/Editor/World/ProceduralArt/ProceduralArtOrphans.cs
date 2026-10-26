#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The generated rock, stone, ice and cliff files on disk that no family makes any more (2026-10-26, phase I6f of the biome
	/// suite, decision 22): the shared <c>Boulder_{Round,Slab,Spire}_LOD*</c>, <c>Rocks_Small</c> and <c>Pebbles</c> meshes every
	/// material had before its own (I6b), a formation variant a lowered <see cref="RockVariantCounts"/> row drops, a shape or a
	/// stone set removed from its table. Listed only: nothing here deletes (the owner removes them by hand), because a scene
	/// may still reference a file until it is re-cut.
	/// </summary>
	/// <remarks>
	/// A file counts when it lies directly in one of the generated folders, its name starts with one of the rock families'
	/// prefixes below, and neither the payload (<see cref="ProceduralArtCatalogue.PayloadPaths"/>) nor the wrappers
	/// (<see cref="ProceduralArtCatalogue.WrapperPaths"/>) list it. Other families' files, and anything in a subfolder (a
	/// tool's own composed prefabs), are never looked at.
	/// </remarks>
	public static class ProceduralArtOrphans
	{
		/// <summary>Each generated folder and the name prefixes the rock families write into it.</summary>
		public static readonly (string Folder, string[] Prefixes)[] RockFolders =
		{
			(ProceduralArtCatalogue.MeshesFolder, new[] { "Boulder_", "Rocks_Small", "Pebbles", "Formation_", "Cliff", "IceBoulder_", "Serac_", "Iceberg_", "SeaIce_" }),
			(ProceduralArtCatalogue.TexturesFolder, new[] { "RockSurface_", "IceSurface_" }),
			(ProceduralArtCatalogue.MaterialsFolder, new[] { "Rock_", "Ice_" }),
			(ProceduralArtCatalogue.PrefabsFolder, new[] { "Boulder_", "Detail_Rocks_", "Detail_Pebbles_", "Formation_", "IceBoulder_", "Serac_", "Iceberg_", "SeaIce_" }),
		};

		/// <summary>
		/// The files of <paramref name="onDisk"/> (project paths, forward slashes; .meta files ignored) in a rock folder under a
		/// rock prefix that <paramref name="expected"/> does not hold, in ordinal order. Pure: the test runs it on made-up lists.
		/// </summary>
		public static List<string> Find(IEnumerable<string> onDisk, IEnumerable<string> expected)
		{
			var known = new HashSet<string>(expected, StringComparer.Ordinal);
			var orphans = new List<string>();
			foreach (string raw in onDisk)
			{
				string path = raw.Replace('\\', '/');
				if (path.EndsWith(".meta", StringComparison.Ordinal) || known.Contains(path) || !IsRockFile(path))
				{
					continue;
				}
				orphans.Add(path);
			}
			orphans.Sort(StringComparer.Ordinal);
			return orphans;
		}

		/// <summary>True when a path lies directly in a rock folder and its name starts with one of that folder's rock prefixes.</summary>
		public static bool IsRockFile(string path)
		{
			int slash = path.LastIndexOf('/');
			if (slash < 0)
			{
				return false;
			}
			string folder = path.Substring(0, slash), name = path.Substring(slash + 1);
			foreach ((string rockFolder, string[] prefixes) in RockFolders)
			{
				if (folder != rockFolder)
				{
					continue;
				}
				foreach (string prefix in prefixes)
				{
					if (name.StartsWith(prefix, StringComparison.Ordinal))
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>The rock families' orphans in this project's generated folders (read from disk, relative to the project root).</summary>
		public static List<string> RockOrphansOnDisk()
		{
			var onDisk = new List<string>();
			foreach ((string folder, string[] _) in RockFolders)
			{
				if (Directory.Exists(folder))
				{
					onDisk.AddRange(Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly));
				}
			}
			var expected = new List<string>(ProceduralArtCatalogue.PayloadPaths());
			expected.AddRange(ProceduralArtCatalogue.WrapperPaths());
			return Find(onDisk, expected);
		}
	}
}
#endif
