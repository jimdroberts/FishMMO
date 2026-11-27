using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>The mercenary system's main-thread queue.</summary>
	public interface IMercenarySystemMainThreadQueueData : IMainThreadQueueData
	{
	}

	/// <summary>The mercenary system's runtime state: its request guard.</summary>
	public interface IMercenarySystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>Debounces each connection's mercenary requests.</summary>
		IngressGuard IngressGuard { get; }
	}
}

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>Main-thread queue for <see cref="MercenarySystem"/>'s database and party replies.</summary>
	public class MercenarySystemMainThreadQueueData : SystemMainThreadQueueData, IMercenarySystemMainThreadQueueData
	{
	}

	/// <summary>The mercenary system's request guard.</summary>
	public class MercenarySystemRuntimeData : RuntimeDataContainer, IMercenarySystemRuntimeData
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
