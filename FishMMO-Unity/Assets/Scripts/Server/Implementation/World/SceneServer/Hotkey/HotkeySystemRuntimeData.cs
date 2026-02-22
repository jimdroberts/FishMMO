using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for hotkey ingress protection.
	/// </summary>
	public class HotkeySystemRuntimeData : RuntimeDataContainer, IHotkeySystemRuntimeData
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

		public override void Deinitialize()
		{
			Clear();
		}
	}
}