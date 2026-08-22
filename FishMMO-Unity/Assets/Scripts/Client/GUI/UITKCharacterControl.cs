using System;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// Abstract base class for UI Toolkit controls that are bound to a player character.
	/// Extends <see cref="UITKControl"/> with the character set/clear lifecycle that
	/// <see cref="UIManager"/> drives on world entry and exit.
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
		/// Runs the character-unset path when this control is destroyed.
		/// </summary>
		/// <remarks>
		/// The unset path, not a bare null assignment. Panels drop their subscriptions to the
		/// character's events in <see cref="OnPreUnsetCharacter"/>, and clearing the field behind
		/// their back skipped every one of them. The character outlives the panel — it is the panel
		/// that is destroyed on a scene change, not the character — so those still-live events kept
		/// invoking handlers on a destroyed MonoBehaviour: the panel is leaked by its own
		/// subscriptions, and the first handler to touch a Unity object throws
		/// MissingReferenceException from inside the character's event dispatch.
		/// </remarks>
		public override void OnDestroying()
		{
			if (Character != null)
			{
				UnsetCharacter();
			}
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
		/// Re-applies the character once the visual tree exists.
		/// </summary>
		/// <remarks>
		/// World entry calls <see cref="UIManager.SetCharacter"/> for every control at once, and
		/// a control that starts hidden has no visual tree until something shows it — so
		/// <see cref="OnPostSetCharacter"/> can run before <c>OnStarting</c> has cached any
		/// elements. Overrides that write into those elements would then dereference null, and
		/// even a null-safe one would leave the panel showing nothing, because the only call
		/// that had data already happened. Running it again here is what makes the two orders
		/// equivalent.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			if (Character != null)
			{
				/* Pre before Post, exactly as SetCharacter does it. This runs again whenever the
				 * visual tree is rebuilt, and OnPostSetCharacter subscribes to character events —
				 * without the matching unsubscribe first, every rebuild would add another
				 * subscription and the handlers would run once more each time. */
				OnPreSetCharacter();
				OnPostSetCharacter();
			}
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
