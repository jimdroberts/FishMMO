using System;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// Abstract base class for UI Toolkit controls that are bound to a player character.
	/// Mirrors the lifecycle of <see cref="UICharacterControl"/> but derives from
	/// <see cref="UITKControl"/> rather than the UGUI-backed <see cref="UIControl"/>.
	/// </summary>
	public abstract class UITKCharacterControl : UITKControl
	{
		/// <summary>
		/// Event invoked (after pre-set) when a character is associated with this control.
		/// </summary>
		public Action<IPlayerCharacter> OnSetCharacter;

		/// <summary>
		/// Event invoked (before character is cleared) when the character association is removed.
		/// </summary>
		public Action OnUnsetCharacter;

		/// <summary>
		/// The player character currently associated with this control.
		/// </summary>
		public IPlayerCharacter Character { get; private set; }

		/// <summary>
		/// Clears the character reference when this control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			Character = null;
		}

		/// <summary>
		/// Invoked before <see cref="Character"/> is assigned. Override to unsubscribe
		/// from events on the outgoing character.
		/// </summary>
		public virtual void OnPreSetCharacter() { }

		/// <summary>
		/// Invoked immediately after <see cref="Character"/> is assigned. Override to
		/// subscribe to events on the incoming character and refresh the UI.
		/// </summary>
		public virtual void OnPostSetCharacter() { }

		/// <summary>
		/// Associates a player character with this control, invoking pre/post callbacks
		/// and the <see cref="OnSetCharacter"/> event.
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
		/// Invoked before <see cref="Character"/> is cleared. Override to perform
		/// any pre-unset cleanup.
		/// </summary>
		public virtual void OnPreUnsetCharacter() { }

		/// <summary>
		/// Invoked immediately after <see cref="Character"/> is cleared. Override to
		/// perform any post-unset cleanup.
		/// </summary>
		public virtual void OnPostUnsetCharacter() { }

		/// <summary>
		/// Removes the player character association, invoking pre/post callbacks and
		/// the <see cref="OnUnsetCharacter"/> event.
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
