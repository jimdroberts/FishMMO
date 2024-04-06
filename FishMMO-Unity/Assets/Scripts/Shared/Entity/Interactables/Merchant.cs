using UnityEngine;

namespace FishMMO.Shared
{
	public enum MerchantTabType : byte
	{
		None = 0,
		Ability,
		AbilityEvent,
		Item,
	}

	[RequireComponent(typeof(SceneObjectNamer))]
	public class Merchant : Interactable
	{
		public MerchantTemplate Template;

		private string title = "Merchant";

		public override string Title { get { return title; } }

		void Start()
		{
			if (Template != null)
			{
				title = Template.Description;
			}
		}

		public override bool CanInteract(Character character)
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