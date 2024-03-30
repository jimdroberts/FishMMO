using FishNet.Object;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Simple NetworkBehaviour type that stores a character reference and injects itself into the Character Behaviour mapping.
	/// </summary>
	[RequireComponent(typeof(Character))]
	public abstract class CharacterBehaviour : NetworkBehaviour, ICharacterBehaviour
	{
		public Character Character { get; protected set; }
		public bool Initialized { get; private set; }

		public void InitializeOnce(Character character)
		{
			if (Initialized || character == null)
				return;

			Initialized = true;
			Character = character;
			Character.RegisterCharacterBehaviour(this);

			InitializeOnce();
		}

		public virtual void InitializeOnce() { }

		protected void Awake()
		{
			OnAwake();
		}

		public virtual void OnAwake() { }

		private void OnDestroy()
		{
			OnDestroying();
			if (Character != null)
			{
				Character.UnregisterCharacterBehaviour(this);
			}
			Character = null;
		}

		public virtual void OnDestroying() { }

		/// <summary>
		/// Called after Character.OnStartClient
		/// </summary>
		public virtual void OnStartCharacter() { }

		/// <summary>
		/// Called right before Character.OnStopClient
		/// </summary>
		public virtual void OnStopCharacter() { }
	}
}