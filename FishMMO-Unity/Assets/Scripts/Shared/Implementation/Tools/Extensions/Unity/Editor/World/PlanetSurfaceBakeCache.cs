#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using Object = UnityEngine.Object;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Completed PNGs survive build-output cleanup; input and image hashes prevent stale or partial reuse.</summary>
	public sealed class PlanetSurfaceBakeCache
	{
		public const string DirectoryPath = "Library/FishMMO/PlanetSurfaces";
		private readonly string directory;
		[Serializable] private sealed class Stamp { public string input, image; }

		public PlanetSurfaceBakeCache(string directory = DirectoryPath) { this.directory = directory; }

		public static string SharedInputs(Action checkCancellation = null)
		{
			var text = new StringBuilder("planet-surface-v1|").Append(Application.unityVersion)
				.Append('|').Append(PlayerSettings.colorSpace).Append('|').Append(EditorUserBuildSettings.activeBuildTarget)
				.Append('|').Append(SystemInfo.graphicsDeviceType)
				.Append('|').Append(typeof(WorldBody).Assembly.ManifestModule.ModuleVersionId);
			foreach (string root in new[] {
				"Assets/Scripts/Shared/Implementation/Celestial",
				"Assets/Scripts/Shared/Implementation/Tools/Biomes",
				"Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World" })
				foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
				{
					checkCancellation?.Invoke();
					text.Append('|').Append(path).Append(':').Append(Digest(File.ReadAllBytes(path)));
				}
			var visited = new HashSet<int>();
			foreach (string guid in AssetDatabase.FindAssets("t:BiomeTemplate", new[] { "Assets" }).OrderBy(value => value, StringComparer.Ordinal))
			{
				checkCancellation?.Invoke();
				AppendObject(text, AssetDatabase.LoadAssetAtPath<BiomeTemplate>(AssetDatabase.GUIDToAssetPath(guid)), visited);
			}
			return Hash128.Compute(text.ToString()).ToString();
		}

		public static string InputHash(WorldBody body, int width, string sharedInputs)
		{
			var text = new StringBuilder(sharedInputs).Append('|').Append(width);
			var visited = new HashSet<int>();
			AppendObject(text, body, visited);
			AppendObject(text, SolarSystemProfile.Resolve(body), visited);
			return Hash128.Compute(text.ToString()).ToString();
		}

		private static string Identity(Object value)
		{
			if (value == null) return "null";
			return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long id) && !string.IsNullOrEmpty(guid)
				? guid + ":" + id : "memory:" + value.GetInstanceID();
		}

		private static void AppendObject(StringBuilder text, Object value, HashSet<int> visited)
		{
			text.Append('|').Append(Identity(value));
			if (value == null || !visited.Add(value.GetInstanceID())) return;
			string path = AssetDatabase.GetAssetPath(value);
			if (!(value is ScriptableObject) && !(value is TerrainLayer))
			{
				if (!string.IsNullOrEmpty(path)) text.Append(':').Append(AssetDatabase.GetAssetDependencyHash(path));
				if (value is Texture texture) text.Append(':').Append(texture.imageContentsHash);
				return;
			}
			// JsonUtility's instance references must not make persistent cache keys depend on the Editor session.
			string json = Regex.Replace(EditorJsonUtility.ToJson(value), "\\\"instanceID\\\"\\s*:\\s*(-?\\d+)",
				match => "\"instanceID\":\"" + Identity(EditorUtility.InstanceIDToObject(int.Parse(match.Groups[1].Value))) + "\"");
			text.Append(':').Append(json);
			using var serialized = new SerializedObject(value);
			var property = serialized.GetIterator();
			while (property.Next(true))
				if (property.propertyType == SerializedPropertyType.ObjectReference && property.name != "m_Script")
					AppendObject(text, property.objectReferenceValue, visited);
		}

		public string EntryPath(WorldBody body, int width) => Path.Combine(directory, Hash128.Compute(Identity(body) + ":" + width) + ".png");

		public bool TryRead(WorldBody body, int width, string input, out byte[] png)
		{
			png = null;
			string path = EntryPath(body, width);
			try
			{
				if (!File.Exists(path) || !File.Exists(path + ".json")) return false;
				var stamp = JsonUtility.FromJson<Stamp>(File.ReadAllText(path + ".json"));
				if (stamp == null || stamp.input != input) return false;
				byte[] bytes = File.ReadAllBytes(path);
				if (bytes.Length < 8 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71 || Digest(bytes) != stamp.image) return false;
				png = bytes;
				return true;
			}
			catch (IOException) { return false; }
			catch (ArgumentException) { return false; }
		}

		public void Store(WorldBody body, int width, string input, byte[] png)
		{
			string path = EntryPath(body, width);
			WriteAtomically(path, png);
			// The stamp is committed last; a crash between writes produces a cache miss, never a false hit.
			WriteAtomically(path + ".json", Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Stamp { input = input, image = Digest(png) })));
		}

		public static void WriteAtomically(string path, byte[] bytes)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				File.WriteAllBytes(temporary, bytes);
				if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
			}
			finally { if (File.Exists(temporary)) File.Delete(temporary); }
		}

		public static string Digest(byte[] bytes)
		{
			using var algorithm = SHA256.Create();
			return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "");
		}
	}
}
#endif
