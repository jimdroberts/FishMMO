namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for character behaviour components that can be registered to a character.
	/// Defines lifecycle methods for initialization and client state changes.
	/// </summary>
	public interface ICharacterBehaviour
	{
		/// <summary>
		/// The character this behaviour is attached to.
		/// </summary>
		ICharacter Character { get; }
		/// <summary>
		/// True if this behaviour has been initialized for its character.
		/// </summary>
		bool Initialized { get; }
		/// <summary>
		/// Initializes the behaviour with the specified character. Called once per behaviour instance.
		/// </summary>
		/// <param name="character">The character to initialize for.</param>
		void InitializeOnce(ICharacter character);
		/// <summary>
		/// Called when the character starts on the client. Use for local client initialization.
		/// </summary>
		void OnStartCharacter();
		/// <summary>
		/// Called when the character stops on the client. Use for local client cleanup.
		/// </summary>
		void OnStopCharacter();
	}
}