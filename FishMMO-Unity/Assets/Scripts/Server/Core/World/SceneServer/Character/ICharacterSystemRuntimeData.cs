namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for character-system periodic gates.
	/// </summary>
	public interface ICharacterSystemRuntimeData : IRuntimeDataContainer
	{
		int SaveInFlight { get; set; }
	}
}
