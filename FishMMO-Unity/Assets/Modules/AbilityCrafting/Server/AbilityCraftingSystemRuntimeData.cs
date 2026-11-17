using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>The ability crafting system's request guard.</summary>
	public class AbilityCraftingSystemRuntimeData : RuntimeDataContainer, IAbilityCraftingSystemRuntimeData
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
