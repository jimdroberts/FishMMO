using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Runtime data container for interactable ingress protection and debounce tracking.
	/// </summary>
	public class InteractableSystemRuntimeData : RuntimeDataContainer, IInteractableSystemRuntimeData
	{
		/// <summary>
		/// Ingress guard instance for per-connection interaction debounce tracking.
		/// </summary>
		public IngressGuard IngressGuard { get; private set; }

		/// <summary>
		/// Initializes the runtime data container by creating a new <see cref="IngressGuard"/> instance.
		/// </summary>
		/// <returns>Initialization status indicating success.</returns>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			IngressGuard = new IngressGuard();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all tracked ingress guard entries.
		/// </summary>
		public override void Clear()
		{
			IngressGuard?.Clear();
		}

		/// <summary>
		/// Clears the ingress guard and releases the reference on deinitialization.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
			IngressGuard = null;
		}
	}
}