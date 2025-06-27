using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server
{
	[CreateAssetMenu(fileName = "New Despawn WorldItem Action", menuName = "FishMMO/Actions/WorldItem/Despawn", order = 0)]
	public class DespawnWorldItemAction : BaseAction
	{
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (!eventData.TryGet(out InteractableEventData interactableEventData))
			{
				Log.Error("DespawnWorldItemAction: Missing InteractableEventData.");
				return;
			}

			WorldItem worldItem = interactableEventData.Interactable as WorldItem;

			if (worldItem == null)
			{
				Log.Warning("DespawnWorldItemAction: Invalid data for despawn.");
				return;
			}

			worldItem.Despawn();
			Log.Debug("WorldItem despawned via action.");
		}
	}
}