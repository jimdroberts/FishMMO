using FishMMO.Shared;
using FishNet.Transporting;
using UnityEngine;

namespace FishMMO.Server
{
    [CreateAssetMenu(fileName = "OpenDungeonFinderUIAction", menuName = "FishMMO/Actions/Interactable/Open Dungeon Finder UI", order = 0)]
    public class OpenDungeonFinderUIAction : BaseAction
    {
        public override void Execute(ICharacter initiator, EventData eventData)
        {
            if (!eventData.TryGet(out InteractableEventData interactableEventData))
            {
                Log.Error("OpenDungeonFinderUIAction: Missing InteractableEventData.");
                return;
            }

            // Ensure we have a valid player character and scene object to broadcast to/from
            IPlayerCharacter character = initiator as IPlayerCharacter;
            if (character == null || character.Owner == null || interactableEventData.SceneObject == null)
            {
                Log.Warning("OpenDungeonFinderUIAction: Invalid character, owner, or scene object for broadcast.");
                return;
            }
            
            // Broadcast the UI open request to the character's owner
            Server.Broadcast(character.Owner, new DungeonFinderBroadcast()
            {
                InteractableID = interactableEventData.SceneObject.ID,
            }, true, Channel.Reliable);

            Log.Debug($"Dungeon Finder UI broadcasted to {character.Name} for InteractableID: {interactableEventData.SceneObject.ID}.");
        }
    }
}