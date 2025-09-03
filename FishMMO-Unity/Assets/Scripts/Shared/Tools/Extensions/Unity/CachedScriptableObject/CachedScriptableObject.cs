using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for ScriptableObjects that need to be cached and retrieved quickly by ID.
	/// Objects are cached not only by their concrete type but also by their base types up to <see cref="CachedScriptableObject{T}"/>.
	/// This allows for flexible retrieval by specific derived types or broader base types.
	/// </summary>
	/// <typeparam name="T">The concrete type of the ScriptableObject inheriting from this class.
	/// Must also implement <see cref="ICachedObject"/>.</typeparam>
	public abstract class CachedScriptableObject<T> : ScriptableObject where T : CachedScriptableObject<T>, ICachedObject
	{
		/// <summary>
		/// The unique ID generated for this cached object instance.
		/// This ID is deterministic, meaning it will be the same across application runs for the same object's
		/// type name and provided unique object name (e.g., asset file name).
		/// </summary>
		public int ID { get; private set; }

		/// <summary>
		/// The static cache storage for all instances of <see cref="CachedScriptableObject{T}"/>.
		/// This is a nested dictionary:
		/// <list type="bullet">
		///     <item>
		///         <term>Outer Dictionary Key</term>
		///         <description>The <see cref="Type"/> of the ScriptableObject (or its relevant base type
		///         up the inheritance chain, until <see cref="CachedScriptableObject{T}"/> is reached).</description>
		///     </item>
		///     <item>
		///         <term>Inner Dictionary Key</term>
		///         <description>The unique deterministic ID of the cached object instance.</description>
		///     </item>
		///     <item>
		///         <term>Inner Dictionary Value</term>
		///         <description>The cached ScriptableObject instance itself, cast to type <typeparamref name="T"/>.</description>
		///     </item>
		/// </list>
		/// This dictionary is `readonly` to ensure it's initialized once and not reassigned.
		/// </summary>
		private static readonly Dictionary<Type, Dictionary<int, T>> resourceCache = new Dictionary<Type, Dictionary<int, T>>();

		/// <summary>
		/// Adds this ScriptableObject instance to the static cache.
		/// It registers the object under its concrete type and all relevant base types up the inheritance chain
		/// (until <see cref="CachedScriptableObject{T}"/> is reached).
		/// The <see cref="ID"/> is generated deterministically based on the object's concrete type name and the provided <paramref name="objectName"/>.
		/// </summary>
		/// <param name="objectName">A unique name for this specific object instance within its type,
		/// typically its asset file name. This is crucial for generating a consistent and unique ID.</param>
		public void AddToCache(string objectName)
		{
			// Get the concrete type of the current instance. This is the starting point for caching.
			Type currentType = this.GetType();

			// Generate a deterministic hash code for the ID.
			// This ensures that the same type name and objectName always produce the same ID across sessions.
			ID = (currentType.Name + objectName).GetDeterministicHashCode();

			// Iterate up the inheritance chain, caching the object under each relevant base type.
			// The loop continues as long as 'currentType' is not null, and it's not the generic
			// 'CachedScriptableObject<>' definition itself. This ensures caching for all derived types.
			while (currentType != null && (!currentType.IsGenericType || currentType.GetGenericTypeDefinition() != typeof(CachedScriptableObject<>)))
			{
				// Attempt to retrieve the inner dictionary for the 'currentType' from the main cache.
				if (!resourceCache.TryGetValue(currentType, out Dictionary<int, T> resources))
				{
					// If no inner dictionary exists for this specific type, create a new one
					// and add it to the main 'resourceCache'.
					resources = new Dictionary<int, T>();
					resourceCache.Add(currentType, resources);
				}

				// Add the current object to the inner dictionary using its generated ID.
				// 'this as T' is safe and guaranteed to succeed due to the generic type constraint
				// 'where T : CachedScriptableObject<T>'.
				resources[ID] = this as T;

				// Call the virtual OnLoad method. This provides an extensibility point for derived classes
				// to perform custom actions (e.g., logging, initialization) when an object is loaded into cache.
				OnLoad(currentType.Name, name, ID); // 'name' refers to ScriptableObject.name

				// Move up to the next base type in the hierarchy for the next iteration.
				currentType = currentType.BaseType;
			}

			// Log.Info("CachedScriptableObject", $"ID Set: {ID}");
		}

		/// <summary>
		/// Removes this ScriptableObject instance from the static cache.
		/// It removes the object from its concrete type's cache and all relevant base types
		/// up the inheritance chain, mirroring the <see cref="AddToCache(string)"/> process.
		/// </summary>
		public void RemoveFromCache()
		{
			// Get the concrete type of the current instance.
			Type currentType = this.GetType();

			// Iterate up the inheritance chain, removing the object from each relevant base type's cache.
			while (currentType != null && (!currentType.IsGenericType || currentType.GetGenericTypeDefinition() != typeof(CachedScriptableObject<>)))
			{
				// Attempt to retrieve the inner dictionary for the 'currentType' from the main cache.
				if (resourceCache.TryGetValue(currentType, out Dictionary<int, T> resources))
				{
					// Remove the object from the inner dictionary using its ID.
					resources.Remove(ID);

					// Call the virtual OnUnload method. This provides an extensibility point for derived classes
					// to perform custom actions (e.g., logging, cleanup) when an object is removed from cache.
					OnUnload(currentType.Name, name, ID); // 'name' refers to ScriptableObject.name

					// If the inner dictionary for this type becomes empty after removal,
					// remove that inner dictionary from the main 'resourceCache' to clean up.
					if (resources.Count == 0)
					{
						resourceCache.Remove(currentType);
					}
				}

				// Move up to the next base type in the hierarchy for the next iteration.
				currentType = currentType.BaseType;
			}

			// Log.Info("CachedScriptableObject", $"Removed: {ID}");
		}

		/// <summary>
		/// Virtual method called when a ScriptableObject is successfully added to the cache.
		/// Derived classes can override this method to add custom logging, initialization,
		/// or other specific logic that should occur upon caching.
		/// </summary>
		/// <param name="typeName">The name of the type (e.g., "RaceTemplate") under which the object is currently being cached.</param>
		/// <param name="resourceName">The name of the ScriptableObject asset (e.g., "HumanRace").</param>
		/// <param name="resourceID">The deterministic ID generated for the resource instance.</param>
		public virtual void OnLoad(string typeName, string resourceName, int resourceID)
		{
			Log.WritePartsToConsole(LogLevel.Info, "CachedScriptableObject", 35,
							 ("#00FF00", $"Loaded {typeName} "),
							 ("#FFFF00", $"'{resourceName}' "),
							 ("#00FFFF", $"ID: {resourceID}"));
		}

		/// <summary>
		/// Virtual method called when a ScriptableObject is successfully removed from the cache.
		/// Derived classes can override this method to add custom logging, cleanup,
		/// or other specific logic that should occur upon uncaching.
		/// </summary>
		/// <param name="typeName">The name of the type (e.g., "RaceTemplate") from which the object is being removed from cache.</param>
		/// <param name="resourceName">The name of the ScriptableObject asset (e.g., "HumanRace").</param>
		/// <param name="resourceID">The deterministic ID of the resource instance.</param>
		public virtual void OnUnload(string typeName, string resourceName, int resourceID)
		{
			Log.WritePartsToConsole(LogLevel.Info, "CachedScriptableObject", 35,
							 ("#FF0000", $"Unloaded {typeName} "),
							 ("#FFFF00", $"'{resourceName}' "),
							 ("#00FFFF", $"ID: {resourceID}"));
		}

		/// <summary>
		/// Retrieves a cached object by its ID, explicitly cast to type <typeparamref name="U"/>.
		/// </summary>
		/// <typeparam name="U">The specific type of the cached object to retrieve. Must inherit from <typeparamref name="T"/>.</typeparam>
		/// <param name="id">The deterministic ID of the object to retrieve.</param>
		/// <returns>The cached object of type <typeparamref name="U"/> if found; otherwise, null.</returns>
		public static U Get<U>(int id) where U : T
		{
			Type targetType = typeof(U);
			if (resourceCache.TryGetValue(targetType, out Dictionary<int, T> cache) &&
				cache.TryGetValue(id, out T obj))
			{
				// Perform the safe cast to U and return.
				return obj as U;
			}
			// Return null if the target type or ID is not found in the cache.
			return null;
		}

		/// <summary>
		/// Retrieves the first cached object of type <typeparamref name="U"/> found in the cache.
		/// This method should be used cautiously, as the "first" object is not guaranteed to be consistent
		/// across different runs or iterations if the underlying dictionary order changes.
		/// It is best suited for scenarios where you expect only one instance of a given type,
		/// or when the specific instance returned doesn't matter.
		/// </summary>
		/// <typeparam name="U">The specific type of the cached object to retrieve. Must inherit from <typeparamref name="T"/>.</typeparam>
		/// <returns>The first cached object of type <typeparamref name="U"/> if any are found; otherwise, null.</returns>
		public static U GetFirst<U>() where U : T
		{
			Type targetType = typeof(U);
			if (resourceCache.TryGetValue(targetType, out Dictionary<int, T> cache) &&
				cache != null && cache.Count > 0) // Check Count > 0 for explicit clarity.
			{
				// Use Linq's FirstOrDefault() to safely get the first value.
				// This ensures that if 'cache.Values' is empty, it returns null without error.
				return cache.Values.FirstOrDefault() as U;
			}
			return null;
		}

		/// <summary>
		/// Returns the entire dictionary of cached objects for a specific type <typeparamref name="U"/>.
		/// This method assumes that <typeparamref name="U"/> is the exact type stored as <typeparamref name="T"/>
		/// in the inner dictionary of the cache.
		/// </summary>
		/// <typeparam name="U">The specific type of objects whose cache should be retrieved.
		/// In your usage, this type is expected to be the same as the <typeparamref name="T"/> parameter of this class.</typeparam>
		/// <returns>A <see cref="Dictionary{TKey, TValue}"/> containing the cached objects of type <typeparamref name="U"/>
		/// indexed by their IDs, or null if no cache is found for that type.</returns>
		public static Dictionary<int, U> GetCache<U>() where U : T
		{
			Type t = typeof(U);
			if (resourceCache.TryGetValue(t, out Dictionary<int, T> cache) &&
				cache != null)
			{
				// This cast works when U is the exact same type as T (e.g., if CachedScriptableObject<RaceTemplate>
				// is used and you call GetCache<RaceTemplate>()).
				// If U were a derived type of T (e.g., if T was a base CharacterTemplate and U was HumanTemplate),
				// this direct cast would return null due to Dictionary's invariance in C#.
				return cache as Dictionary<int, U>;
			}
			return null;
		}

		/// <summary>
		/// Retrieves a list of cached objects of type <typeparamref name="U"/> corresponding to a given set of IDs.
		/// </summary>
		/// <typeparam name="U">The specific type of objects to retrieve. Must inherit from <typeparamref name="T"/>.</typeparam>
		/// <param name="ids">A <see cref="HashSet{T}"/> of integer IDs to retrieve.</param>
		/// <returns>A <see cref="List{T}"/> of cached objects of type <typeparamref name="U"/> that match the provided IDs.
		/// Returns an empty list if no IDs are provided, no cache for the type exists, or no objects match the IDs.</returns>
		public static List<ICachedObject> Get<U>(HashSet<int> ids) where U : T
		{
			// Always initialize the list to ensure a non-null return, simplifying client code.
			List<ICachedObject> objects = new List<ICachedObject>();

			// If no IDs are provided or the cache is not populated for this type, return an empty list immediately.
			if (ids == null || ids.Count == 0 || !resourceCache.TryGetValue(typeof(U), out Dictionary<int, T> cache) || cache.Count == 0)
			{
				return objects;
			}

			// Iterate through the provided IDs and try to retrieve each corresponding object from the cache.
			foreach (int id in ids)
			{
				if (cache.TryGetValue(id, out T cached))
				{
					// Add the found object (cast to U and then to ICachedObject) to the list.
					// The ICachedObject return type allows for a heterogeneous list of cached objects if needed.
					objects.Add(cached as U);
				}
			}
			return objects;
		}
	}
}