using FishNet;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Utility.Extension;
using FishNet.Utility.Performance;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace FishMMO.Shared
{
	public class CustomObjectPool : ObjectPool
	{
		#region Public.
		/// <summary>
		/// Cache for pooled NetworkObjects.
		/// </summary>
		public IReadOnlyCollection<Dictionary<int, Stack<NetworkObject>>> Cache => _cache;
		private List<Dictionary<int, Stack<NetworkObject>>> _cache = new List<Dictionary<int, Stack<NetworkObject>>>();
		#endregion

		#region Serialized.
		/// <summary>
		/// True if to use object pooling.
		/// </summary>
		[Tooltip("True if to use object pooling.")]
		[SerializeField]
		private bool _enabled = true;
		#endregion

		#region Private.
		/// <summary>
		/// Current count of the cache collection.
		/// </summary>
		private int _cacheCount = 0;
		#endregion

		/// <summary>
		/// Returns an object that has been stored with a collectionID of 0. A new object will be created if no stored objects are available.
		/// </summary>
		/// <param name="prefabID">PrefabID of the object to return.</param>
		/// <param name="asServer">True if being called on the server side.</param>
		/// <returns></returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)] //Remove on 2024/01/01.
#pragma warning disable CS0672 // Member overrides obsolete member
		public override NetworkObject RetrieveObject(int prefabID, bool asServer)
#pragma warning restore CS0672 // Member overrides obsolete member
		{
			return RetrieveObject(prefabID, 0, asServer);
		}

		/// <summary>
		/// Returns an object that has been stored. A new object will be created if no stored objects are available.
		/// </summary>
		/// <param name="prefabId">PrefabId of the object to return.</param>
		/// <param name="collectionID">CollectionID of the prefab.</param>
		/// <param name="asServer">True if being called on the server side.</param>
		/// <returns></returns>
		public override NetworkObject RetrieveObject(int prefabID, ushort collectionID, bool asServer)
		{
			PrefabObjects po = base.NetworkManager.GetPrefabObjects<PrefabObjects>(collectionID, false);
			//Quick exit/normal retrieval when not using pooling.
			if (!_enabled)
			{
				NetworkObject prefab = po.GetObject(asServer, prefabID);
				NetworkObject instance = Instantiate(prefab);
				instance.gameObject.SetActive(false);
				return instance;
			}

			Stack<NetworkObject> cache = GetOrCreateCache(collectionID, prefabID);
			NetworkObject nob;
			//Iterate until nob is populated just in case cache entries have been destroyed.
			do
			{
				if (cache.Count == 0)
				{
					NetworkObject prefab = po.GetObject(asServer, prefabID);
					/* A null nob should never be returned from spawnables. This means something
                     * else broke, likely unrelated to the object pool. */
					nob = Instantiate(prefab);
					//Can break instantly since we know nob is not null.
					break;
				}
				else
				{
					nob = cache.Pop();
				}

			} while (nob == null);

			IPooledResettable[] pooledResettables = nob.gameObject.GetComponents<IPooledResettable>();
			if (pooledResettables != null)
			{
				for (int i = 0; i < pooledResettables.Length; ++i)
				{
					pooledResettables[i].OnPooledReset();
				}
			}

			// ensure the object is deactivated until we are ready
			nob.gameObject.SetActive(false);
			return nob;
		}
		/// <summary>
		/// Stores an object into the pool.
		/// </summary>
		/// <param name="instantiated">Object to store.</param>
		/// <param name="asServer">True if being called on the server side.</param>
		/// <returns></returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public override void StoreObject(NetworkObject instantiated, bool asServer)
		{
			instantiated.gameObject.SetActive(false);
			
			//Pooling is not enabled.
			if (!_enabled)
			{
				Destroy(instantiated.gameObject);
				return;
			}

			instantiated.ResetForObjectPool();
			Stack<NetworkObject> cache = GetOrCreateCache(instantiated.SpawnableCollectionId, instantiated.PrefabId);
			cache.Push(instantiated);
		}

		/// <summary>
		/// Instantiates a number of objects and adds them to the pool.
		/// </summary>
		/// <param name="prefab">Prefab to cache.</param>
		/// <param name="count">Quantity to spawn.</param>
		/// <param name="asServer">True if storing prefabs for the server collection. This is only applicable when using DualPrefabObjects.</param>
		public override void CacheObjects(NetworkObject prefab, int count, bool asServer)
		{
			if (!_enabled)
				return;
			if (count <= 0)
				return;
			if (prefab == null)
				return;
			if (prefab.PrefabId == NetworkObject.UNSET_PREFABID_VALUE)
			{
				InstanceFinder.NetworkManager.LogError($"Pefab {prefab.name} has an invalid prefabId and cannot be cached.");
				return;
			}

			Stack<NetworkObject> cache = GetOrCreateCache(prefab.SpawnableCollectionId, prefab.PrefabId);
			for (int i = 0; i < count; i++)
			{
				NetworkObject nob = Instantiate(prefab);
				nob.gameObject.SetActive(false);
				cache.Push(nob);
			}
		}

		/// <summary>
		/// Clears pools destroying objects for all collectionIDs
		/// </summary>
		public void ClearPool()
		{
			int count = _cache.Count;
			for (int i = 0; i < count; i++)
				ClearPool(i);
		}

		/// <summary>
		/// Clears a pool destroying objects for collectionID.
		/// </summary>
		/// <param name="collectionID">CollectionID to clear for.</param>
		public void ClearPool(int collectionID)
		{
			if (collectionID >= _cacheCount)
				return;

			Dictionary<int, Stack<NetworkObject>> dict = _cache[collectionID];
			foreach (Stack<NetworkObject> item in dict.Values)
			{
				while (item.Count > 0)
				{
					NetworkObject nob = item.Pop();
					if (nob != null)
						Destroy(nob.gameObject);
				}
			}

			dict.Clear();
		}


		/// <summary>
		/// Gets a cache for an id or creates one if does not exist.
		/// </summary>
		/// <param name="prefabID"></param>
		/// <returns></returns>
		private Stack<NetworkObject> GetOrCreateCache(int collectionID, int prefabID)
		{
			if (collectionID >= _cacheCount)
			{
				//Add more to the cache.
				while (_cache.Count <= collectionID)
				{
					Dictionary<int, Stack<NetworkObject>> dict = new Dictionary<int, Stack<NetworkObject>>();
					_cache.Add(dict);
				}
				_cacheCount = collectionID;
			}

			Dictionary<int, Stack<NetworkObject>> dictionary = _cache[collectionID];
			Stack<NetworkObject> cache;
			//No cache for prefabID yet, make one.
			if (!dictionary.TryGetValueIL2CPP(prefabID, out cache))
			{
				cache = new Stack<NetworkObject>();
				dictionary[prefabID] = cache;
			}
			return cache;
		}
	}
}