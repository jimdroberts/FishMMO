#if UNITY_SERVER
using FishNet.Transporting;
using System.Linq;
#endif
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class Merchant : Interactable
	{
		public List<AbilityTemplate> Abilities;
		public List<AbilityEvent> AbilityEvents;
		public List<BaseItemTemplate> Items;

		public override bool OnInteract(Character character)
		{
			if (!base.OnInteract(character))
			{
				return false;
			}

#if UNITY_SERVER
			character.Owner.Broadcast(new MerchantBroadcast()
			{
				InteractableID = ID,
				Abilities = Abilities.Select(a => a.ID).ToList(),
				AbilityEvents = AbilityEvents.Select(ae => ae.ID).ToList(),
				Items = Items.Select(i => i.ID).ToList(),
			}, true, Channel.Reliable);
#endif
			return true;
		}
	}
}