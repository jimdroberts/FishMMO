namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for objects that support caching with a unique identifier and cache management methods.
	/// </summary>
	public interface ICachedObject
	{
		/// <summary>
		/// Gets the unique identifier for this cached object.
		/// </summary>
		int ID { get; }

		/// <summary>
		/// Adds the object to a cache, using the provided object name as a key or reference.
		/// </summary>
		/// <param name="objectName">The name or key to associate with the cached object.</param>
		void AddToCache(string objectName);

		/// <summary>
		/// Removes the object from the cache.
		/// </summary>
		void RemoveFromCache();
	}
}