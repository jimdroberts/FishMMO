using System;

namespace FishMMO.Shared
{
	public class Bindstone : Interactable
	{
		public static event Action<IPlayerCharacter, Bindstone> OnBind;
		public override string Title { get { return ""; } }

		public override bool CanInteract(IPlayerCharacter character)
		{
			if (!base.CanInteract(character))
			{
				return false;
			}
			OnBind?.Invoke(character, this);
			return true;
		}
	}
}