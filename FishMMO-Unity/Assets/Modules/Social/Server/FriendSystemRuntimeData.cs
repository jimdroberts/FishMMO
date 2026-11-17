using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for friend ingress protection.
	/// </summary>
	public class FriendSystemRuntimeData : RuntimeDataContainer, IFriendSystemRuntimeData
	{
		/// <summary>
		/// Ingress guard for debouncing and rate-limiting friend operation requests.
		/// </summary>
		public IngressGuard IngressGuard { get; private set; }

		/// <summary>
		/// Initializes the friend system runtime data, creating the ingress guard.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			IngressGuard = new IngressGuard();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears the ingress guard state.
		/// </summary>
		public override void Clear()
		{
			IngressGuard?.Clear();
		}

		/// <summary>
		/// Deinitializes the runtime data, clearing ingress guard state.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
		}
	}
}