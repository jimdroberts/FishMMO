using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer.Character
{
	/// <summary>
	/// Runtime data container for character system periodic save gates.
	/// </summary>
	public class CharacterSystemRuntimeData : RuntimeDataContainer, ICharacterSystemRuntimeData
	{
		public int SaveInFlight { get; set; }

		public override ServerComponentInitializationStatus InitializeOnce()
		{
			SaveInFlight = 0;
			return ServerComponentInitializationStatus.Initialized;
		}

		public override void Clear()
		{
			SaveInFlight = 0;
		}

		public override void Deinitialize()
		{
			Clear();
		}
	}
}