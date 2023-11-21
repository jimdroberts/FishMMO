using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UICharacterControl : UIControl
	{
		public Character Character { get; private set; }

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		public virtual void OnShow(Character character)
		{
			Character = character;
			OnShow();
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
		}
	}
}