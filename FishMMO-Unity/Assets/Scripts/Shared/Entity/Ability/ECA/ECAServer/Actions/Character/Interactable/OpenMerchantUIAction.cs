using FishMMO.Shared;
using FishNet.Transporting;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Server
{
    [CreateAssetMenu(fileName = "OpenMerchantUIAction", menuName = "FishMMO/Actions/Interactable/Open Merchant UI", order = 0)]
    public class OpenMerchantUIAction : BaseAction
    {
        public override void Execute(ICharacter initiator, EventData eventData)
        {
            if (!eventData.TryGet(out InteractableEventData interactableEventData))
            {
                Log.Error("OpenMerchantUIAction", "Missing InteractableEventData.");
                return;
            }

            // Ensure we have a valid player character and the merchant object
            IPlayerCharacter character = initiator as IPlayerCharacter;
            Merchant merchant = interactableEventData.Interactable as Merchant;

            if (character == null || character.Owner == null || merchant == null || merchant.Template == null || interactableEventData.SceneObject == null)
            {
                Log.Warning("OpenMerchantUIAction", "Invalid character, owner, merchant, template, or scene object for broadcast.");
                return;
            }
            
            // Broadcast the UI open request
            Server.Broadcast(character.Owner, new MerchantBroadcast()
            {
                InteractableID = interactableEventData.SceneObject.ID,
                TemplateID = merchant.Template.ID,
            }, true, Channel.Reliable);

            Log.Debug("OpenMerchantUIAction", $"Merchant UI broadcasted to {character.Name} for InteractableID: {interactableEventData.SceneObject.ID} (TemplateID: {merchant.Template.ID}).");
        }
    }
}