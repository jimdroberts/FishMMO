using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Main-thread queue for <see cref="TradeSystem"/>. Persistence outcomes that must touch
	/// live characters come back through here.
	/// </summary>
	public class TradeSystemMainThreadQueueData : SystemMainThreadQueueData, ITradeSystemMainThreadQueueData
	{
	}
}
