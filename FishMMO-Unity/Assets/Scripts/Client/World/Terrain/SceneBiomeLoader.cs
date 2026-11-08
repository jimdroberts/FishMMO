using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// Loads, on the client, the biome templates a loaded world scene's biome map names, and only those, for as long
	/// as a scene that names them is loaded.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A biome template references its whole art set (terrain layers, detail, herb and cave-mouth prefabs): loading
	/// every template at boot loaded all generated biome art, about 2 GB, before the menu, which a WebGL client
	/// (4 GB heap) cannot afford. The templates are therefore in their own group and label
	/// (<see cref="Constants.SharedBiomesLabel"/>), which the client does not preload; the server preloads it.
	/// </para>
	/// <para>
	/// A scene names its biomes only by ID (<see cref="SceneBiomeMap.BiomeIDs"/>): the scenes' own references to
	/// biome templates sit on EditorOnly objects and never reach a build. A template's ID is the deterministic hash
	/// of its type name and asset name, and its address is its asset name, so the biome locations (listed once by
	/// label, which downloads nothing) map back to IDs without loading a template. A loaded template is added to the
	/// <see cref="CachedScriptableObject{T}"/> cache, which registers it in <see cref="BiomeRegistry"/>; the systems
	/// that use biomes rebuild on <see cref="BiomeRegistry.Changed"/>. It is removed and released when the last scene
	/// that names it unloads.
	/// </para>
	/// </remarks>
	public static class SceneBiomeLoader
	{
		private sealed class LoadedBiome
		{
			public AsyncOperationHandle<BiomeTemplate> Handle;
			public int Users;
			public bool Cached;
			// Holds the loading screen until the template (and the art it references) has arrived.
			public SceneContentLoads.Load Content;
		}

		/// <summary>Biome ID → its location, once listed; null until the listing completes.</summary>
		private static Dictionary<int, IResourceLocation> locationsByID;
		private static bool listing;
		private static readonly List<Action> afterListing = new List<Action>();

		private static readonly Dictionary<int, LoadedBiome> loaded = new Dictionary<int, LoadedBiome>();
		private static readonly Dictionary<SceneHandle, List<int>> idsByScene = new Dictionary<SceneHandle, List<int>>();
		private static readonly HashSet<int> reportedMissing = new HashSet<int>();
		private static bool hooked;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			locationsByID = null;
			listing = false;
			afterListing.Clear();
			loaded.Clear();
			idsByScene.Clear();
			reportedMissing.Clear();
			hooked = false;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (hooked || !Application.isPlaying)
			{
				return;
			}
			hooked = true;
			SceneManager.sceneLoaded += OnSceneLoaded;
			SceneManager.sceneUnloaded += OnSceneUnloaded;
		}

		/// <summary>The cache ID a template with this asset name gets (CachedScriptableObject.AddToCache).</summary>
		public static int BiomeID(string assetName) => (nameof(BiomeTemplate) + assetName).GetDeterministicHashCode();

		private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (!WorldSceneSettings.TryGetForScene(scene, out WorldSceneSettings settings))
			{
				return;
			}
			SceneBiomeMap map = settings.BiomeMap;
			if (map == null || map.BiomeIDs == null)
			{
				return;
			}
			var ids = new List<int>();
			var seen = new HashSet<int>();
			foreach (int id in map.BiomeIDs)
			{
				if (id != 0 && seen.Add(id))
				{
					ids.Add(id);
				}
			}
			if (ids.Count == 0)
			{
				return;
			}
			SceneHandle handle = scene.handle;
			idsByScene[handle] = ids;
			// Spans the listing; each biome then holds the loading screen with a load of its own (Acquire).
			SceneContentLoads.Load listingLoad = SceneContentLoads.Begin("biomes");
			WhenListed(() =>
			{
				try
				{
					// The scene may have unloaded while the locations were being listed.
					if (idsByScene.TryGetValue(handle, out List<int> stillWanted) && ReferenceEquals(stillWanted, ids))
					{
						foreach (int id in ids)
						{
							Acquire(id);
						}
					}
				}
				finally
				{
					listingLoad.End();
				}
			});
		}

		private static void OnSceneUnloaded(Scene scene)
		{
			if (!idsByScene.TryGetValue(scene.handle, out List<int> ids))
			{
				return;
			}
			idsByScene.Remove(scene.handle);
			if (locationsByID == null)
			{
				// Never acquired: the listing had not completed.
				return;
			}
			foreach (int id in ids)
			{
				Release(id);
			}
		}

		private static void WhenListed(Action then)
		{
			if (locationsByID != null)
			{
				then();
				return;
			}
			afterListing.Add(then);
			if (listing)
			{
				return;
			}
			listing = true;
			AsyncOperationHandle<IList<IResourceLocation>> list =
				Addressables.LoadResourceLocationsAsync(Constants.SharedBiomesLabel, typeof(BiomeTemplate));
			list.Completed += op =>
			{
				var byID = new Dictionary<int, IResourceLocation>();
				if (op.Status == AsyncOperationStatus.Succeeded && op.Result != null)
				{
					foreach (IResourceLocation location in op.Result)
					{
						byID[BiomeID(location.PrimaryKey)] = location;
					}
				}
				else
				{
					Debug.LogError($"[Scene biomes] Could not list the '{Constants.SharedBiomesLabel}' biome templates: {op.OperationException?.Message}. No biome is loaded.");
				}
				Addressables.Release(op);
				locationsByID = byID;
				listing = false;
				var pending = new List<Action>(afterListing);
				afterListing.Clear();
				foreach (Action action in pending)
				{
					action();
				}
			};
		}

		private static void Acquire(int id)
		{
			if (loaded.TryGetValue(id, out LoadedBiome biome))
			{
				biome.Users++;
				return;
			}
			if (!locationsByID.TryGetValue(id, out IResourceLocation location))
			{
				if (reportedMissing.Add(id))
				{
					Debug.LogWarning($"[Scene biomes] The biome map names biome ID {id}, which no '{Constants.SharedBiomesLabel}' template has.");
				}
				return;
			}
			biome = new LoadedBiome { Users = 1, Handle = Addressables.LoadAssetAsync<BiomeTemplate>(location), Content = SceneContentLoads.Begin("biomes") };
			biome.Content.Track(biome.Handle);
			loaded[id] = biome;
			biome.Handle.Completed += op =>
			{
				biome.Content.End();
				// Released before it finished: nothing to register.
				if (biome.Users <= 0 || op.Status != AsyncOperationStatus.Succeeded || op.Result == null)
				{
					if (op.Status != AsyncOperationStatus.Succeeded)
					{
						Debug.LogError($"[Scene biomes] Loading biome '{location.PrimaryKey}' failed: {op.OperationException?.Message}");
					}
					return;
				}
				op.Result.AddToCache(op.Result.name);
				biome.Cached = true;
			};
		}

		private static void Release(int id)
		{
			if (!loaded.TryGetValue(id, out LoadedBiome biome) || --biome.Users > 0)
			{
				return;
			}
			loaded.Remove(id);
			biome.Content?.End();
			if (biome.Cached && biome.Handle.IsValid() && biome.Handle.Result != null)
			{
				biome.Handle.Result.RemoveFromCache();
			}
			if (biome.Handle.IsValid())
			{
				Addressables.Release(biome.Handle);
			}
		}
	}
}
