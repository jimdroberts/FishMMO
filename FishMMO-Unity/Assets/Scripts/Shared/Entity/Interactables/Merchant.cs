#if UNITY_SERVER
using FishNet.Transporting;
#endif
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

		public override bool OnInteract(Character character)
		{
			if (Template == null ||
				!base.OnInteract(character))
			{
				return false;
			}

#if UNITY_SERVER
			character.Owner.Broadcast(new MerchantBroadcast()
			{
				id = Template.ID,
			}, true, Channel.Reliable);
#endif
			return true;
		}
	}
}