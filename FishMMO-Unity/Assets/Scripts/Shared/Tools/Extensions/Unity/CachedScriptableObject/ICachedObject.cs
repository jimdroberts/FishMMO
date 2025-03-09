namespace FishMMO.Shared
{
	public interface ICachedObject
	{
		int ID { get; }

		void AddToCache(string objectName);
		void RemoveFromCache();
	}
}