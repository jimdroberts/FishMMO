#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Finds which project files reference which GUIDs, by reading them as text — what is on disk,
	/// not what the asset database has loaded.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why text, not <c>AssetDatabase.GetDependencies</c>.</b> The question is always about the
	/// files that will be committed: does any of them name a LOCAL asset, is a photo still referenced
	/// before it is deleted. The serialized text is what git carries and what another clone loads, and
	/// a dependency query only knows assets it has imported. Text-serialized assets (YAML) write a
	/// reference as <c>guid: …</c>; shader graphs and other JSON as <c>"guid":"…"</c> (sometimes
	/// escaped); .meta files carry remaps the same way. One pattern catches them all.
	/// </para>
	/// <para>
	/// File types that never hold a reference (images, audio, code, models) are skipped. Files that
	/// are binary (a NUL in their first 8 KB) are skipped too unless the caller asks for them
	/// (<c>binary: true</c>): then a Unity-serialized binary file — TerrainData is one, by
	/// <c>PreferBinarySerialization</c> — is searched for each GUID's 16-byte form
	/// (<see cref="ProceduralArtMigration.BinaryGuid"/>).
	/// </para>
	/// </remarks>
	public static class AssetGuidScan
	{
		private static readonly Regex GuidReference = new Regex(@"guid\W{0,6}([0-9a-fA-F]{32})", RegexOptions.CultureInvariant);
		private static readonly Regex MetaGuid = new Regex(@"^guid:\s*([0-9a-fA-F]{32})\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

		/// <summary>Extensions that never hold an asset reference; reading them would only cost time.</summary>
		private static readonly HashSet<string> NeverReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".png", ".jpg", ".jpeg", ".tga", ".psd", ".exr", ".hdr", ".tif", ".tiff", ".bmp", ".gif", ".webp",
			".fbx", ".obj", ".blend", ".dae", ".3ds", ".wav", ".ogg", ".mp3", ".aif", ".aiff", ".mp4", ".webm",
			".dll", ".so", ".dylib", ".a", ".jar", ".aar", ".bundle", ".pdb", ".mdb", ".zip", ".pdf",
			".cs", ".hlsl", ".shader", ".cginc", ".compute", ".glsl", ".ttf", ".otf", ".bytes", ".txt", ".md", ".json",
		};

		/// <summary>True for the gitignored procedural-art payload, which is build output like LOCAL is machine-local.</summary>
		public static bool IsPayloadPath(string path) => ProceduralArtPayload.IsPayload(path);

		/// <summary>True for anything generated (payload, wrappers, built terrain layers): gitignored build output.</summary>
		public static bool IsGeneratedPath(string path) => ProceduralArtPayload.IsGenerated(path);

		/// <summary>Unity-serialized files that may be binary, searched by bytes when asked.</summary>
		private static readonly HashSet<string> SerializedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".asset", ".unity", ".prefab", ".mat", ".terrainlayer", ".controller", ".overrideController", ".anim", ".playable", ".lighting",
		};

		/// <summary>
		/// The GUID declared by every .meta file under a folder (files and subfolders), as lowercase hex.
		/// </summary>
		public static HashSet<string> GuidsUnder(string folder)
		{
			var guids = new HashSet<string>(StringComparer.Ordinal);
			if (!Directory.Exists(folder))
			{
				return guids;
			}
			foreach (string meta in Directory.EnumerateFiles(folder, "*.meta", SearchOption.AllDirectories))
			{
				Match m = MetaGuid.Match(File.ReadAllText(meta));
				if (m.Success)
				{
					guids.Add(m.Groups[1].Value.ToLowerInvariant());
				}
			}
			return guids;
		}

		/// <summary>
		/// Every file under <c>Assets</c> (folders for which <paramref name="skip"/> is true left out,
		/// with everything in them) that references one of <paramref name="guids"/>, with the GUIDs it
		/// names. A file's own .meta declaring the GUID is not a reference and is not counted.
		/// </summary>
		/// <param name="root">The folder to scan; <c>Assets</c> unless a test scans a folder of its own.</param>
		/// <param name="binary">Also search binary Unity-serialized files for each GUID's binary form.</param>
		public static SortedDictionary<string, SortedSet<string>> FilesReferencing(ICollection<string> guids, Func<string, bool> skip, string root = "Assets", bool binary = false)
		{
			var result = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
			if (guids == null || guids.Count == 0)
			{
				return result;
			}
			var wanted = new HashSet<string>(StringComparer.Ordinal);
			foreach (string g in guids)
			{
				wanted.Add(g.ToLowerInvariant());
			}
			var binaryForms = new List<(byte[] bytes, string guid)>();
			if (binary)
			{
				foreach (string g in wanted)
				{
					binaryForms.Add((ProceduralArtMigration.BinaryGuid(g), g));
				}
			}
			var pending = new Stack<string>();
			pending.Push(root.Replace('\\', '/'));
			while (pending.Count > 0)
			{
				string folder = pending.Pop();
				foreach (string sub in Directory.GetDirectories(folder))
				{
					string path = sub.Replace('\\', '/');
					if (skip == null || !skip(path))
					{
						pending.Push(path);
					}
				}
				foreach (string file in Directory.GetFiles(folder))
				{
					string path = file.Replace('\\', '/');
					if (NeverReferences.Contains(Path.GetExtension(path)) || (skip != null && skip(path)))
					{
						continue;
					}
					string own = path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ? DeclaredGuid(path) : null;
					string text = ReadText(path);
					if (text == null)
					{
						if (binary && SerializedExtensions.Contains(Path.GetExtension(path)))
						{
							SearchBinary(path, binaryForms, result);
						}
						continue;
					}
					foreach (Match m in GuidReference.Matches(text))
					{
						string g = m.Groups[1].Value.ToLowerInvariant();
						if (wanted.Contains(g) && g != own)
						{
							if (!result.TryGetValue(path, out SortedSet<string> set))
							{
								set = new SortedSet<string>(StringComparer.Ordinal);
								result[path] = set;
							}
							set.Add(g);
						}
					}
				}
			}
			return result;
		}

		/// <summary>
		/// The skip rule for "what will be committed": Assets/LOCAL (any capitalisation, as the gitignore
		/// matches it) and everything generated (payload, wrappers, built terrain layers).
		/// </summary>
		public static bool IsMachineLocal(string path) => BiomeLocalArtIndex.IsLocalPath(path) || IsGeneratedPath(path) || IsPayloadPath(path + "/");

		/// <summary>Adds every GUID whose 16-byte form occurs in a binary file.</summary>
		private static void SearchBinary(string path, List<(byte[] bytes, string guid)> forms, SortedDictionary<string, SortedSet<string>> result)
		{
			byte[] data;
			try
			{
				data = File.ReadAllBytes(path);
			}
			catch (IOException)
			{
				return;
			}
			var byFirst = new Dictionary<byte, List<(byte[] bytes, string guid)>>();
			foreach ((byte[] bytes, string guid) form in forms)
			{
				if (!byFirst.TryGetValue(form.bytes[0], out List<(byte[] bytes, string guid)> list))
				{
					byFirst[form.bytes[0]] = list = new List<(byte[] bytes, string guid)>();
				}
				list.Add(form);
			}
			for (int i = 0; i + 16 <= data.Length; i++)
			{
				if (!byFirst.TryGetValue(data[i], out List<(byte[] bytes, string guid)> candidates))
				{
					continue;
				}
				foreach ((byte[] bytes, string guid) candidate in candidates)
				{
					int j = 1;
					while (j < 16 && data[i + j] == candidate.bytes[j])
					{
						j++;
					}
					if (j == 16)
					{
						if (!result.TryGetValue(path, out SortedSet<string> set))
						{
							set = new SortedSet<string>(StringComparer.Ordinal);
							result[path] = set;
						}
						set.Add(candidate.guid);
					}
				}
			}
		}

		private static string DeclaredGuid(string metaPath)
		{
			Match m = MetaGuid.Match(File.ReadAllText(metaPath));
			return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
		}

		private static string ReadText(string path)
		{
			byte[] bytes;
			try
			{
				bytes = File.ReadAllBytes(path);
			}
			catch (IOException)
			{
				return null;
			}
			int probe = Math.Min(bytes.Length, 8192);
			for (int i = 0; i < probe; i++)
			{
				if (bytes[i] == 0)
				{
					return null;
				}
			}
			return Encoding.UTF8.GetString(bytes);
		}
	}
}
#endif
