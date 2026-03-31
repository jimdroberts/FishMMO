using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Runtime data container for interactable ingress protection and debounce tracking.
	/// </summary>
	public class InteractableSystemRuntimeData : RuntimeDataContainer, IInteractableSystemRuntimeData
	{
		public IngressGuard IngressGuard { get; private set; }

		public override ServerComponentInitializationStatus InitializeOnce()
		{
			IngressGuard = new IngressGuard();
			return ServerComponentInitializationStatus.Initialized;
		}

		public override void Clear()
		{
			IngressGuard?.Clear();
		}

		protected override void OnDeinitialize()
		{
			Clear();
			IngressGuard = null;
		}
	}
}