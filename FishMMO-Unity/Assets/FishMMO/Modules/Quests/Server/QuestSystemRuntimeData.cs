using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for quest system ingress protection.
	/// </summary>
	public class QuestSystemRuntimeData : RuntimeDataContainer, IQuestSystemRuntimeData
	{
		/// <summary>
		/// The ingress guard used for per-connection, per-operation rate limiting of quest requests.
		/// </summary>
		public IngressGuard IngressGuard { get; private set; }

		/// <summary>
		/// Initializes the runtime data, creating a new ingress guard instance.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			IngressGuard = new IngressGuard();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all tracked ingress guard state.
		/// </summary>
		public override void Clear()
		{
			IngressGuard?.Clear();
		}

		/// <summary>
		/// Deinitializes the runtime data, clearing state and releasing the ingress guard reference.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
			IngressGuard = null;
		}
	}
}
