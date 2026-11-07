#if UNITY_EDITOR
using System.Collections.Generic;
using System;
using UnityEditor;
using UnityEditor.Build.Content;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEditor.Build.Pipeline.WriteTypes;
using UnityEngine;

namespace FishMMO.Shared.CustomBuildTool.Core
{
	/// <summary>
	/// Removes everything visual from server content before native serialization. Shader callbacks
	/// run AFTER variant enumeration, which can exhaust Unity's native keyword hash table. Runs
	/// after packing so built-in object extraction has finished using its layout.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The server draws nothing, so it ships no materials, GPU programs, sprites, fonts or image
	/// textures. Removing them here, and not only from the explicit groups, matters because packing
	/// has already copied every implicit dependency into each bundle that reaches it: look-array
	/// textures whose materials sat in a client-only group were copied into hundreds of server scene
	/// bundles (about 46 GB of a 49 GB server build on 2026-11-06).
	/// </para>
	/// <para>
	/// Kept: meshes (colliders, PropColliderStreamer), TerrainData with the control maps stored
	/// inside it (terrain-layer lookups such as TerrainIndexMap read them), TerrainLayers themselves
	/// (their identity; the image textures they point at are removed), animation, NavMesh and every
	/// gameplay ScriptableObject. A reference to a removed object deserializes as null.
	/// </para>
	/// </remarks>
	public static class ServerContentPacking
	{
		public static bool IsGpuProgramOrMaterial(Type type) => type != null &&
			(typeof(Shader).IsAssignableFrom(type) || typeof(ComputeShader).IsAssignableFrom(type) ||
			 typeof(ShaderVariantCollection).IsAssignableFrom(type) || typeof(Material).IsAssignableFrom(type));

		/// <summary>
		/// True for an object a server build removes. <paramref name="assetMainType"/> is the main
		/// type of the asset file the object lives in: a texture inside TerrainData is data the
		/// terrain needs, a texture anywhere else is an image.
		/// </summary>
		public static bool IsStrippedOnServer(Type objectType, Type assetMainType)
		{
			if (objectType == null) return false;
			if (IsGpuProgramOrMaterial(objectType)) return true;
			if (typeof(Sprite).IsAssignableFrom(objectType) || typeof(Font).IsAssignableFrom(objectType)) return true;
			return typeof(Texture).IsAssignableFrom(objectType) &&
				(assetMainType == null || !typeof(TerrainData).IsAssignableFrom(assetMainType));
		}

		/// <summary>
		/// True for an asset file the server build leaves out entirely: its main object is visual.
		/// Used by the build planner, which works per file rather than per object.
		/// </summary>
		public static bool IsVisualAsset(Type mainType) => IsStrippedOnServer(mainType, mainType);

		public static void StripRendering(IDependencyData dependencies, IWriteData writeData)
		{
			var mainTypes = new Dictionary<GUID, Type>();
			var stripped = new Dictionary<ObjectIdentifier, bool>();
			int removed = 0;
			bool IsRendering(ObjectIdentifier id)
			{
				if (!stripped.TryGetValue(id, out bool strip))
				{
					var types = ContentBuildInterface.GetTypeForObjects(new[] { id });
					Type type = types.Length != 0 ? types[0] : null;
					strip = IsGpuProgramOrMaterial(type);
					if (!strip && type != null)
					{
						if (!mainTypes.TryGetValue(id.guid, out Type main))
						{
							string path = AssetDatabase.GUIDToAssetPath(id.guid.ToString());
							main = string.IsNullOrEmpty(path) ? null : AssetDatabase.GetMainAssetTypeAtPath(path);
							mainTypes.Add(id.guid, main);
						}
						strip = IsStrippedOnServer(type, main);
					}
					if (strip) removed++;
					stripped.Add(id, strip);
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

			if (removed > 0)
				Debug.Log($"[ServerContentPacking] Removed {removed} visual object(s) (materials, shaders, textures, sprites, fonts) from server content.");
		}
	}
}
#endif
