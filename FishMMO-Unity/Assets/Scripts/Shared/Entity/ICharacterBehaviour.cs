namespace FishMMO.Shared
{
	public interface ICharacterBehaviour
	{
		Character Character { get; }
		bool Initialized { get; }
		void InitializeOnce(Character character);
		void OnStartCharacter();
		void OnStopCharacter();
	}
}