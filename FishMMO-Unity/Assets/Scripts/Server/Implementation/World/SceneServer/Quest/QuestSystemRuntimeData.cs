using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for quest system ingress protection.
	/// </summary>
	public class QuestSystemRuntimeData : RuntimeDataContainer, IQuestSystemRuntimeData
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
