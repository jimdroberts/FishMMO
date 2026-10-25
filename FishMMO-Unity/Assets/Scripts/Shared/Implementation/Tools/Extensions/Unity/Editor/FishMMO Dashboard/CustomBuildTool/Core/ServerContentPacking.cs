#if UNITY_EDITOR
using System.Collections.Generic;
using System;
using UnityEditor.Build.Content;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEditor.Build.Pipeline.WriteTypes;
using UnityEngine;

namespace FishMMO.Shared.CustomBuildTool.Core
{
	/// <summary>
	/// Remove GPU objects before native serialization. Shader callbacks run AFTER
	/// variant enumeration, which can exhaust Unity's native keyword hash table.
	/// Run after packing so built-in object extraction has finished using its layout.
	/// </summary>
	public static class ServerContentPacking
	{
		// Unlike generated visual roots, implicit textures/terrain layers can carry
		// CPU gameplay data. Retain them along with meshes and TerrainData.
		public static bool IsGpuProgramOrMaterial(Type type) => type != null &&
			(typeof(Shader).IsAssignableFrom(type) || typeof(ComputeShader).IsAssignableFrom(type) ||
			 typeof(ShaderVariantCollection).IsAssignableFrom(type) || typeof(Material).IsAssignableFrom(type));

		public static void StripRendering(IDependencyData dependencies, IWriteData writeData)
		{
			var rendering = new Dictionary<ObjectIdentifier, bool>();
			bool IsRendering(ObjectIdentifier id)
			{
				if (!rendering.TryGetValue(id, out bool strip))
				{
					var types = ContentBuildInterface.GetTypeForObjects(new[] { id });
					strip = types.Length != 0 && IsGpuProgramOrMaterial(types[0]);
					rendering.Add(id, strip);
				}
				return strip;
			}

			foreach (var asset in dependencies.AssetInfo.Values)
			{
				asset.includedObjects.RemoveAll(IsRendering);
				asset.referencedObjects.RemoveAll(IsRendering);
			}
			foreach (var objects in writeData.FileToObjects.Values)
				objects.RemoveAll(IsRendering);

			var retained = new HashSet<ObjectIdentifier>();
			foreach (var operation in writeData.WriteOperations)
			{
				operation.Command.serializeObjects.RemoveAll(info => IsRendering(info.serializationObject));
				foreach (var info in operation.Command.serializeObjects) retained.Add(info.serializationObject);
				if (operation is SceneBundleWriteOperation sceneBundle)
					sceneBundle.PreloadInfo.preloadObjects.RemoveAll(IsRendering);
				else if (operation is SceneDataWriteOperation sceneData)
					sceneData.PreloadInfo.preloadObjects.RemoveAll(IsRendering);
			}
			// Missing visual references should deserialize as null, never point to an
			// object ID removed from this or another bundle. Keep cross-bundle physics refs.
			var retainedObjects = new ObjectIdentifier[retained.Count];
			retained.CopyTo(retainedObjects);
			foreach (var operation in writeData.WriteOperations)
				operation.ReferenceMap.FilterToSubset(retainedObjects);
		}
	}
}
#endif
