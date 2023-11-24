using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class CachedScriptableObject<T> : ScriptableObject where T : CachedScriptableObject<T>, ICachedObject
	{
		public int ID { get; private set; }

		private static Dictionary<Type, Dictionary<int, T>> resourceCache = new Dictionary<Type, Dictionary<int, T>>();

		/// <summary>
		/// Returns the cached object as type U or null if it cannot be found.
		/// </summary>
		public static U Get<U>(int id) where U : T
		{
			Dictionary<int, T> cache = LoadCache<U>();
			if (cache != null &&
				cache.TryGetValue(id, out T obj))
			{
				return obj as U;
			}
			return null;
		}

		/// <summary>
		/// Returns the cached objects as type List U or empty if nothing is found.
		/// </summary>
		public static List<ICachedObject> Get<U>(HashSet<int> ids) where U : T
		{
			List<ICachedObject> objects = new List<ICachedObject>();

			if (ids == null ||
				ids.Count < 1)
			{
				return objects;
			}
			
			Dictionary<int, T> cache = LoadCache<U>();
			if (cache != null &&
				cache.Count > 0)
			{
				foreach (int id in ids)
				{
					if (cache.TryGetValue(id, out T cached))
					{
						objects.Add(cached as U);
					}
				}
			}
			return objects;
		}

		/// <summary>
		/// Attempts to load and return cached objects of type U.
		/// </summary>
		public static Dictionary<int, T> LoadCache<U>() where U : T
		{
			Type t = typeof(U);
			if (!resourceCache.TryGetValue(t, out Dictionary<int, T> cache))
			{
				resourceCache.Add(t, cache = new Dictionary<int, T>());

				U[] resources = Resources.LoadAll<U>("");
				if (resources != null && resources.Length > 0)
				{
					foreach (U resource in resources)
					{
						resource.ID = resource.name.GetDeterministicHashCode();

						if (!cache.ContainsKey(resource.ID))
						{
							cache.Add(resource.ID, resource);
							Debug.Log("CachedScriptableObject: Loaded[" + t.Name + " " + resource.name + " ID:" + resource.ID + "]");
						}
						else
						{
							Resources.UnloadAsset(resource);
						}
					}
				}
			}
			return cache;
		}

		/// <summary>
		/// Attemps to unload cached objects of type U.
		/// </summary>
		public static void UnloadCache<U>() where U : T
		{
			Type t = typeof(U);
			if (resourceCache.TryGetValue(t, out Dictionary<int, T> cache))
			{
				foreach (U resource in new List<T>(cache.Values))
				{
					if (resource != null)
					{
						Debug.Log("CachedScriptableObject: Unloaded[" + t.Name + " " + resource.name + " ID:" + resource.ID + "]");
						Resources.UnloadAsset(resource);
					}
					cache.Remove(resource.ID);
				}
			}
		}
	}
}