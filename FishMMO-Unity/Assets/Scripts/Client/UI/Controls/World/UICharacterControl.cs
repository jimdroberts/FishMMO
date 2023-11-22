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
		}
	}
}