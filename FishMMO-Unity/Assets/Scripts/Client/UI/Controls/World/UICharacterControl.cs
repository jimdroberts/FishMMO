using FishMMO.Shared;
using System;

namespace FishMMO.Client
{
	/// <summary>
	/// UICharacterControl is responsible for managing the player character's
	/// association with the UI, including setting and unsetting the character,
	/// and invoking the appropriate events and callbacks.
	/// </summary>
	public class UICharacterControl : UIControl
	{
		/// <summary>
		/// Event invoked when a character is set.
		/// </summary>
		public Action<IPlayerCharacter> OnSetCharacter;
		/// <summary>
		/// Event invoked when a character is unset.
		/// </summary>
		public Action OnUnsetCharacter;

		/// <summary>
		/// The current player character associated with this control.
		/// </summary>
		public IPlayerCharacter Character { get; private set; }

		/// <summary>
		/// Called when the UI control is starting. Override for initialization logic.
		/// </summary>
		public override void OnStarting()
		{
		}

		/// <summary>
		/// Called when the UI control is being destroyed. Clears the character reference.
		/// </summary>
		public override void OnDestroying()
		{
			Character = null;
		}

		/// <summary>
		/// Invoked before Character is set. Override for pre-set logic.
		/// </summary>
		public virtual void OnPreSetCharacter() { }

		/// <summary>
		/// Invoked immediately after Character is set. Override for post-set logic.
		/// </summary>
		public virtual void OnPostSetCharacter() { }

		/// <summary>
		/// Sets the character for this control, invoking pre/post set events and callbacks.
		/// </summary>
		/// <param name="character">The player character to associate.</param>
		public void SetCharacter(IPlayerCharacter character)
		{
			OnPreSetCharacter();

			Character = character;

			OnSetCharacter?.Invoke(character);

			OnPostSetCharacter();
		}

		/// <summary>
		/// Invoked before Character is unset. Override for pre-unset logic.
		/// </summary>
		public virtual void OnPreUnsetCharacter() { }

		/// <summary>
		/// Invoked immediately after Character is unset. Override for post-unset logic.
		/// </summary>
		public virtual void OnPostUnsetCharacter() { }

		/// <summary>
		/// Unsets the character for this control, invoking pre/post unset events and callbacks.
		/// </summary>
		public void UnsetCharacter()
		{
			OnPreUnsetCharacter();

			OnUnsetCharacter?.Invoke();

			Character = null;

			OnPostUnsetCharacter();
		}
	}
}