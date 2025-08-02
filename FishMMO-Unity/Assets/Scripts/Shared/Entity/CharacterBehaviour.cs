using FishNet.Object;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for character-related behaviours attached to networked characters.
	/// Handles initialization, registration, and lifecycle events for character behaviours.
	/// </summary>
	public abstract class CharacterBehaviour : NetworkBehaviour, ICharacterBehaviour
	{
		/// <summary>
		/// Reference to the character this behaviour is attached to.
		/// </summary>
		public ICharacter Character { get; protected set; }
		/// <summary>
		/// Reference to the player character, if applicable.
		/// </summary>
		public IPlayerCharacter PlayerCharacter { get; protected set; }
		/// <summary>
		/// True if this behaviour has been initialized for its character.
		/// </summary>
		public bool Initialized { get; private set; }

		/// <summary>
		/// Initializes this behaviour for the specified character, registers it, and calls custom initialization.
		/// Only runs once per behaviour instance.
		/// </summary>
		/// <param name="character">The character to initialize for.</param>
		public void InitializeOnce(ICharacter character)
		{
			if (Initialized || character == null)
				return;

			Initialized = true;
			Character = character;
			PlayerCharacter = character as IPlayerCharacter;
			Character.RegisterCharacterBehaviour(this);

			InitializeOnce();
		}

		/// <summary>
		/// Called after registration for custom initialization logic. Override in derived classes.
		/// </summary>
		public virtual void InitializeOnce() { }

		/// <summary>
		/// Unity Awake callback. Called before InitializeOnce; character reference will not be set yet.
		/// Use for early setup logic.
		/// </summary>
		protected void Awake()
		{
			OnAwake();
		}

		/// <summary>
		/// Called before InitializeOnce, character will not be set yet. Override for early setup.
		/// </summary>
		public virtual void OnAwake() { }

		/// <summary>
		/// Unity OnDestroy callback. Unregisters this behaviour from its character and calls custom cleanup.
		/// </summary>
		private void OnDestroy()
		{
			OnDestroying();
			if (Character != null)
			{
				Character.UnregisterCharacterBehaviour(this);
			}
			Character = null;
			PlayerCharacter = null;
		}

		/// <summary>
		/// Called during OnDestroy for custom cleanup logic. Override in derived classes.
		/// </summary>
		public virtual void OnDestroying() { }

		/// <summary>
		/// Called after Character.OnStartClient. Use this for local client initialization.
		/// </summary>
		public virtual void OnStartCharacter() { }

		/// <summary>
		/// Called right before Character.OnStopClient. Use this for local client cleanup.
		/// </summary>
		public virtual void OnStopCharacter() { }
	}
}