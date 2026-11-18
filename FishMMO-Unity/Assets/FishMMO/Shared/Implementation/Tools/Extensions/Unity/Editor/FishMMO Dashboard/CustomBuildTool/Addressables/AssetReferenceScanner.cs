#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine.AddressableAssets;

namespace FishMMO.Shared.CustomBuildTool.Addressables
{
	/// <summary>
	/// Finds the assets that AssetReference fields point at. AssetDatabase.GetDependencies does not
	/// see them (an AssetReference stores a GUID string), so a target that is not addressable is not
	/// a build error, only an InvalidKeyException at run time.
	/// </summary>
	public static class AssetReferenceScanner
	{
		private static readonly Regex AssetGuid = new Regex(@"m_AssetGUID: ([0-9a-f]{32})", RegexOptions.Compiled);
		private static readonly Dictionary<Type, bool> TypeHoldsReferences = new Dictionary<Type, bool>();

		/// <summary>Referenced GUID → the first asset found referencing it.</summary>
		public static Dictionary<string, string> Scan(IEnumerable<string> assetPaths)
		{
			var found = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (string path in assetPaths)
			{
				if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
				if (IsText(path))
				{
					foreach (string line in File.ReadLines(path))
					{
						if (line.IndexOf("m_AssetGUID", StringComparison.Ordinal) < 0) continue;
						Match match = AssetGuid.Match(line);
						if (match.Success && !found.ContainsKey(match.Groups[1].Value)) found.Add(match.Groups[1].Value, path);
					}
				}
				else if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) && MayHoldReferences(AssetDatabase.GetMainAssetTypeAtPath(path)))
				{
					// Binary-serialized asset of a type that can hold an AssetReference: read it properly.
					foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
					{
						if (asset == null || !MayHoldReferences(asset.GetType())) continue;
						using (var serialized = new SerializedObject(asset))
						{
							SerializedProperty property = serialized.GetIterator();
							while (property.Next(true))
							{
								if (property.propertyType == SerializedPropertyType.String && property.name == "m_AssetGUID" &&
									!string.IsNullOrEmpty(property.stringValue) && !found.ContainsKey(property.stringValue))
									found.Add(property.stringValue, path);
							}
						}
					}
				}
			}
			return found;
		}

		private static bool IsText(string path)
		{
			try
			{
				using (var stream = File.OpenRead(path))
				{
					var header = new byte[5];
					return stream.Read(header, 0, 5) == 5 && header[0] == '%' && header[1] == 'Y' && header[2] == 'A' && header[3] == 'M' && header[4] == 'L';
				}
			}
			catch (IOException)
			{
				return false;
			}
		}

		/// <summary>True when a serialized instance of <paramref name="type"/> can contain an AssetReference.</summary>
		private static bool MayHoldReferences(Type type)
		{
			if (type == null || !typeof(UnityEngine.Object).IsAssignableFrom(type) ||
				!(typeof(UnityEngine.ScriptableObject).IsAssignableFrom(type) || typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(type)))
				return false;
			return Holds(type, new HashSet<Type>());
		}

		private static bool Holds(Type type, HashSet<Type> visiting)
		{
			if (TypeHoldsReferences.TryGetValue(type, out bool cached)) return cached;
			if (!visiting.Add(type)) return false;
			bool holds = false;
			for (Type current = type; current != null && current != typeof(object) && !holds; current = current.BaseType)
			{
				foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
				{
					Type fieldType = field.FieldType;
					if (fieldType.IsArray) fieldType = fieldType.GetElementType();
					else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>)) fieldType = fieldType.GetGenericArguments()[0];
					if (fieldType == null) continue;
					if (typeof(AssetReference).IsAssignableFrom(fieldType)) { holds = true; break; }
					// Nested [Serializable] classes and structs; Unity objects are separate references.
					if (!fieldType.IsPrimitive && !fieldType.IsEnum && fieldType != typeof(string) &&
						!typeof(UnityEngine.Object).IsAssignableFrom(fieldType) &&
						fieldType.IsDefined(typeof(SerializableAttribute), false) && Holds(fieldType, visiting))
					{
						holds = true;
						break;
					}
				}
			}
			visiting.Remove(type);
			TypeHoldsReferences[type] = holds;
			return holds;
		}
	}
}
#endif
