using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>The duel system's main-thread queue.</summary>
	public interface IDuelSystemMainThreadQueueData : IMainThreadQueueData
	{
	}

	/// <summary>The duel system's runtime state: its request guard.</summary>
	public interface IDuelSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>Debounces each connection's duel requests.</summary>
		IngressGuard IngressGuard { get; }
	}
}

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>Main-thread queue for <see cref="DuelSystem"/>'s database replies.</summary>
	public class DuelSystemMainThreadQueueData : SystemMainThreadQueueData, IDuelSystemMainThreadQueueData
	{
	}

	/// <summary>The duel system's request guard.</summary>
	public class DuelSystemRuntimeData : RuntimeDataContainer, IDuelSystemRuntimeData
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
