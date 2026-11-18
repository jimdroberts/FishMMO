namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Shared base class for per-system main-thread queue runtime containers.
	/// Concrete system queue containers should inherit this type to keep declarations minimal.
	/// </summary>
	public abstract class SystemMainThreadQueueData : MainThreadQueueData
	{
	}
}