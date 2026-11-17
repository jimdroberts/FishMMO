using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>Main-thread queue for <see cref="StaffSystem"/>'s async replies.</summary>
	public class StaffSystemMainThreadQueueData : SystemMainThreadQueueData, IStaffSystemMainThreadQueueData
	{
	}
}
