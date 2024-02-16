using FishMMO.Shared;
using System;

namespace FishMMO.Client
{
	public class UICharacterControl : UIControl
	{
		public Action<Character> OnSetCharacter;
		public Action OnUnsetCharacter;

		public Character Character { get; private set; }

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
			Character = null;
		}

		/// <summary>
		/// Invoked before Character is set.
		/// </summary>
		public virtual void OnPreSetCharacter()
		{
		}

		public virtual void SetCharacter(Character character)
		{
			OnPreSetCharacter();

			Character = character;

			OnSetCharacter?.Invoke(character);
		}

		public virtual void UnsetCharacter()
		{
			OnUnsetCharacter?.Invoke();

			Character = null;
		}
	}
}