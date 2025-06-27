using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server
{
	[CreateAssetMenu(fileName = "New Set LastInteractableID Action", menuName = "FishMMO/Actions/Banker/Set Last Interactable ID", order = 0)]
	public class SetBankLastInteractableAction : BaseAction
	{
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (!eventData.TryGet(out InteractableEventData interactableEventData))
			{
				Log.Error("SetBankLastInteractableAction: Missing InteractableEventData.");
				return;
			}

			if (interactableEventData.SceneObject == null)
			{
				Log.Warning("SetBankLastInteractableAction: Invalid scene object.");
				return;
			}

			if (initiator == null || !initiator.TryGet(out IBankController bankController))
			{
				Log.Error("SetBankLastInteractableAction: Missing BankController.");
				return;
			}

			bankController.LastInteractableID = interactableEventData.SceneObject.ID;
			Log.Debug($"Banker: Set LastInteractableID to {interactableEventData.SceneObject.ID} for {initiator?.Name}.");
		}
	}
}