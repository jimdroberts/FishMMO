namespace FishMMO.Shared
{
	public interface ICharacterBehaviour
	{
		ICharacter Character { get; }
		bool Initialized { get; }
		void InitializeOnce(ICharacter character);
		void OnStartCharacter();
		void OnStopCharacter();
	}
}