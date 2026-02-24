using System.Threading;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer.Character
{
	/// <summary>
	/// Runtime data container for character system periodic save gates.
	/// </summary>
	public class CharacterSystemRuntimeData : RuntimeDataContainer, ICharacterSystemRuntimeData
	{
		private int saveInFlight;

		/// <inheritdoc/>
		public bool TryBeginSave()
		{
			return Interlocked.CompareExchange(ref saveInFlight, 1, 0) == 0;
		}

		/// <inheritdoc/>
		public void EndSave()
		{
			Interlocked.Exchange(ref saveInFlight, 0);
		}

		public override ServerComponentInitializationStatus InitializeOnce()
		{
			Interlocked.Exchange(ref saveInFlight, 0);
			return ServerComponentInitializationStatus.Initialized;
		}

		public override void Clear()
		{
			Interlocked.Exchange(ref saveInFlight, 0);
		}

		protected override void OnDeinitialize()
		{
			Clear();
		}
	}
}