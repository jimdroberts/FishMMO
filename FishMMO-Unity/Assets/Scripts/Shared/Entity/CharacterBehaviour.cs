using FishNet.Object;

namespace FishMMO.Shared
{
	/// <summary>
	/// Simple NetworkBehaviour type that stores a character reference and injects itself into the Character Behaviour mapping.
	/// </summary>
	public abstract class CharacterBehaviour : NetworkBehaviour, ICharacterBehaviour
	{
		public ICharacter Character { get; protected set; }
		public IPlayerCharacter PlayerCharacter { get; protected set; }
		public bool Initialized { get; private set; }

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

		public virtual void InitializeOnce() { }

		protected void Awake()
		{
			OnAwake();
		}

		/// <summary>
		/// Called before InitializeOnce, character will not be set yet.
		/// </summary>
		public virtual void OnAwake() { }

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

		public virtual void OnDestroying() { }

		/// <summary>
		/// Called after Character.OnStartClient. Use this for your Local Client.
		/// </summary>
		public virtual void OnStartCharacter() { }

		/// <summary>
		/// Called right before Character.OnStopClient. Use this for your Local Client.
		/// </summary>
		public virtual void OnStopCharacter() { }
	}
}