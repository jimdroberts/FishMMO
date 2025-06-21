using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class CachedScriptableObject<T> : ScriptableObject where T : CachedScriptableObject<T>, ICachedObject
	{
		public int ID { get; private set; }

		private static Dictionary<Type, Dictionary<int, T>> resourceCache = new Dictionary<Type, Dictionary<int, T>>();

		public void AddToCache(string objectName)
		{
			Type baseType = this.GetType();

			ID = (baseType.Name + objectName).GetDeterministicHashCode();

			while (baseType != null && (!baseType.IsGenericType || baseType.GetGenericTypeDefinition() != typeof(CachedScriptableObject<>)))
			{
				// Add the cached object to the resourceCache
				//Log.Info($"Resource '{objectName}' added to cache: {baseType.Name}");

				if (!resourceCache.TryGetValue(baseType, out Dictionary<int, T> resources))
				{
					resourceCache.Add(baseType, resources = new Dictionary<int, T>());
				}
				resources.Add(ID, this as T);

				OnLoad(baseType.Name, name, ID);

				// Move up the inheritance chain
				baseType = baseType.BaseType;
			}

			//Log.Info($"CachedScriptableObject ID Set: {ID}");
		}

		public void RemoveFromCache()
		{
			Type baseType = this.GetType();

			while (baseType != null && (!baseType.IsGenericType || baseType.GetGenericTypeDefinition() != typeof(CachedScriptableObject<>)))
			{
				// Remove the cached object from the resourceCache
				if (resourceCache.TryGetValue(baseType, out Dictionary<int, T> resources))
				{
					//Log.Info($"Resource '{objectName}' removed from cache: {baseType.Name}");
					resources.Remove(ID);

					OnUnload(baseType.Name, name, ID);

					if (resources.Count < 1)
					{
						resourceCache.Remove(baseType);
					}
				}

				// Move up the inheritance chain
				baseType = baseType.BaseType;
			}

			//Log.Info($"CachedScriptableObject Removed: {ID}");
		}

		public virtual void OnLoad(string typeName, string resourceName, int resourceID)
		{
			Log.WriteColored(LogLevel.Info, 35,
							("#00FF00", typeName + " "),
							("#FFFF00", resourceName + " "),
							("#00FFFF", "ID: " + resourceID));
		}

		public virtual void OnUnload(string typeName, string resourceName, int resourceID)
		{
			Log.WriteColored(LogLevel.Info, 35,
							("#00FF00", typeName + " "),
							("#FFFF00", resourceName + " "),
							("#00FFFF", "ID: " + resourceID));
		}

		/// <summary>
		/// Returns the cached object as type U or null if it cannot be found.
		/// </summary>
		public static U Get<U>(int id) where U : T
		{
			if (resourceCache == null)
			{
				return null;
			}

			Type t = typeof(U);
			if (resourceCache.TryGetValue(t, out Dictionary<int, T> cache) &&
				cache.TryGetValue(id, out T obj))
			{
				return obj as U;
			}
			return null;
		}

		/// <summary>
		/// Returns the first cached object as type U or null if nothing is found.
		/// </summary>
		public static U GetFirst<U>() where U : T
		{
			if (resourceCache == null)
			{
				return null;
			}

			Type t = typeof(U);
			if (resourceCache.TryGetValue(t, out Dictionary<int, T> cache) &&
				cache != null)
			{
				foreach (T obj in cache.Values)
				{
					return obj as U;
				}
			}
			return null;
		}

		/// <summary>
		/// Returns the cached objects as type Dictionary<ID, Template> or null if nothing is found.
		/// </summary>
		public static Dictionary<int, U> GetCache<U>() where U : T
		{
			if (resourceCache == null)
			{
				return null;
			}

			Type t = typeof(U);
			if (resourceCache.TryGetValue(t, out Dictionary<int, T> cache) &&
				cache != null)
			{
				return cache as Dictionary<int, U>;
			}
			return null;
		}

		/// <summary>
		/// Returns the cached objects as type List U or empty if nothing is found.
		/// </summary>
		public static List<ICachedObject> Get<U>(HashSet<int> ids) where U : T
		{
			if (resourceCache == null)
			{
				return null;
			}

			List<ICachedObject> objects = new List<ICachedObject>();

			if (ids == null ||
				ids.Count < 1)
			{
				return objects;
			}

			Type t = typeof(U);
			if (resourceCache.TryGetValue(t, out Dictionary<int, T> cache) &&
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
	}
}