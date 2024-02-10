using FishMMO.Shared;
using System;

namespace FishMMO.Client
{
	public class UICharacterControl : UIControl
	{
		public Action<Character> OnSetCharacter;

		public Character Character { get; private set; }

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
			Character = null;
		}

		public virtual void Show(Character character)
		{
			Character = character;
			Show();
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
	}
}