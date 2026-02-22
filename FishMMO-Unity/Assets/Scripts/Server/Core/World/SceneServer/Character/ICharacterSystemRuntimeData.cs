namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for character-system periodic gates.
	/// </summary>
	public interface ICharacterSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Atomically transitions the save gate from idle to in-flight.
		/// Returns true if this call won the race; false if a save is already in flight.
		/// </summary>
		bool TryBeginSave();

		/// <summary>
		/// Atomically transitions the save gate from in-flight back to idle.
		/// </summary>
		void EndSave();
	}
}