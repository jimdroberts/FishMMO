using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for character inventory ingress protection.
	/// </summary>
	public class CharacterInventorySystemRuntimeData : RuntimeDataContainer, ICharacterInventorySystemRuntimeData
	{
		/// <summary>
		/// Ingress guard for debouncing and rate-limiting inventory, equipment, and bank operations.
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
		/// Clears all ingress guard entries.
		/// </summary>
		public override void Clear()
		{
			IngressGuard?.Clear();
		}

		/// <summary>
		/// Deinitializes the runtime data by clearing all state.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
		}
	}
}