using FishMMO.Shared;
using FishNet.Transporting;
using UnityEngine;

namespace FishMMO.Server
{
    [CreateAssetMenu(fileName = "OpenAbilityCrafterUIAction", menuName = "FishMMO/Actions/Interactable/Open Ability Crafter UI", order = 0)]
    public class OpenAbilityCrafterUIAction : BaseAction
    {
        public override void Execute(ICharacter initiator, EventData eventData)
        {
            if (!eventData.TryGet(out InteractableEventData interactableEventData))
            {
                Log.Error("OpenAbilityCrafterUIAction: Missing InteractableEventData.");
                return;
            }

            IPlayerCharacter character = initiator as IPlayerCharacter;
            if (character == null || character.Owner == null || interactableEventData.SceneObject == null)
            {
                Log.Warning("OpenAbilityCrafterUIAction: Invalid character, owner, or scene object for broadcast.");
                return;
            }
            
            // Broadcast the UI open request
            Server.Broadcast(character.Owner, new AbilityCrafterBroadcast()
            {
                InteractableID = interactableEventData.SceneObject.ID,
            }, true, Channel.Reliable);

            Log.Debug($"AbilityCrafter UI broadcasted to {character.Name} for InteractableID: {interactableEventData.SceneObject.ID}");
        }
    }
}