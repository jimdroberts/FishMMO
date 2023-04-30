using System.Collections.Generic;
using System;
using UnityEngine;

public abstract class CachedScriptableObject<T> : ScriptableObject where T : CachedScriptableObject<T>
{
	public int ID;

	private static Dictionary<int, T> cache = null;
	public static Dictionary<int, T> Cache
	{
		get
		{
			if (cache == null)
			{
				T[] resources = Resources.LoadAll<T>("");
				cache = new Dictionary<int, T>();
				if (resources.Length > 0)
				{
					foreach (T resource in resources)
					{
						resource.ID = resource.name.GetDeterministicHashCode();

						if (!cache.ContainsKey(resource.ID))
						{
							cache.Add(resource.ID, resource);
							Debug.Log("[" + DateTime.UtcNow + "] Resource: " + typeof(T).Name + "[" + resource.name + " ID:" + resource.ID + "] - Loaded");
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
	}

	public static void UnloadCache()
	{
		foreach (T resource in new List<T>(cache.Values))
		{
			Debug.Log("[" + DateTime.UtcNow + "] Resource: " + typeof(T).Name + "[" + resource.name + " ID:" + resource.ID + "] - Unloaded");

			Resources.UnloadAsset(resource);
			cache.Remove(resource.ID);
		}
		
	}
}