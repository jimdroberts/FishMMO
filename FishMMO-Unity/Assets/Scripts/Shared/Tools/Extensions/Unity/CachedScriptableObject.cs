using System.Collections.Generic;
using System;
using UnityEngine;

public abstract class CachedScriptableObject<T> : ScriptableObject where T : CachedScriptableObject<T>
{
	public int ID;

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