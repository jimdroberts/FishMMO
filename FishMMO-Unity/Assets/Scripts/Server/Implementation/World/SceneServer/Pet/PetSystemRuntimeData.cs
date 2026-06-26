using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for pet ingress protection.
	/// </summary>
	public class PetSystemRuntimeData : RuntimeDataContainer, IPetSystemRuntimeData
	{
		/// <summary>
		/// Ingress guard for rate-limiting pet control requests per connection.
		/// </summary>
		public IngressGuard IngressGuard { get; private set; }

		/// <summary>
		/// Initializes the pet system runtime data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			IngressGuard = new IngressGuard();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all pet runtime data.
		/// </summary>
		public override void Clear()
		{
			IngressGuard?.Clear();
		}

		/// <summary>
		/// Deinitializes the pet runtime data container.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
		}
	}
}