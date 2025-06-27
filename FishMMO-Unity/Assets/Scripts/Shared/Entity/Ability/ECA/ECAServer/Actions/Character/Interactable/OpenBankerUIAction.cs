using FishMMO.Shared;
using FishNet.Transporting;
using UnityEngine;

namespace FishMMO.Server
{
	[CreateAssetMenu(fileName = "New Open Banker UI Action", menuName = "FishMMO/Actions/Banker/Open UI", order = 0)]
	public class OpenBankerUIAction : BaseAction
	{
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (!eventData.TryGet(out InteractableEventData interactableEventData))
			{
				Log.Error("OpenBankerUIAction: Missing InteractableEventData.");
				return;
			}

			IPlayerCharacter character = initiator as IPlayerCharacter;
			if (character == null || character.Owner == null)
			{
				Log.Warning("OpenBankerUIAction: Invalid character or owner for broadcast.");
				return;
			}

			Server.Broadcast(character.Owner, new BankerBroadcast(), true, Channel.Reliable);
			Log.Debug($"Banker UI broadcasted to {character.Name}.");
		}
	}
}