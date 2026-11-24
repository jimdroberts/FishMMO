using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>The auction house system's main-thread queue.</summary>
	public interface IAuctionHouseSystemMainThreadQueueData : IMainThreadQueueData
	{
	}

	/// <summary>The auction house system's runtime state: its request guard.</summary>
	public interface IAuctionHouseSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>Debounces and serialises each connection's auction requests.</summary>
		IngressGuard IngressGuard { get; }
	}
}

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>Main-thread queue for <see cref="AuctionHouseSystem"/>'s async replies.</summary>
	public class AuctionHouseSystemMainThreadQueueData : SystemMainThreadQueueData, IAuctionHouseSystemMainThreadQueueData
	{
	}

	/// <summary>The auction house system's request guard.</summary>
	public class AuctionHouseSystemRuntimeData : RuntimeDataContainer, IAuctionHouseSystemRuntimeData
	{
		/// <inheritdoc />
		public IngressGuard IngressGuard { get; private set; }

		/// <inheritdoc />
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			IngressGuard = new IngressGuard();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc />
		public override void Clear()
		{
			IngressGuard?.Clear();
		}

		/// <inheritdoc />
		protected override void OnDeinitialize()
		{
			Clear();
			IngressGuard = null;
		}
	}
}
