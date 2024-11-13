using UnityEngine;

namespace FishMMO.Shared
{
	[RequireComponent(typeof(SceneObjectNamer))]
	public class Merchant : Interactable
	{
		public MerchantTemplate Template;

		private string title = "Merchant";

		public override string Title { get { return title; } }

        public override void OnStarting()
        {
			if (Template != null)
			{
				title = Template.Description;
			}
		}

		public override bool CanInteract(IPlayerCharacter character)
		{
			if (Template == null ||
				!base.CanInteract(character))
			{
				return false;
			}
			return true;
		}
	}
}