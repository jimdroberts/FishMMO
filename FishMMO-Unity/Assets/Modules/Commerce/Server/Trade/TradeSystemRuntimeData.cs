using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for <see cref="TradeSystem"/>: the per-connection ingress guard.
	/// </summary>
	public class TradeSystemRuntimeData : RuntimeDataContainer, ITradeSystemRuntimeData
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
		}
	}
}
