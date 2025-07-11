using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server
{
    [CreateAssetMenu(fileName = "NPCFaceCharacterAction", menuName = "FishMMO/Actions/NPC/Face Character", order = 0)]
    public class NPCFaceCharacterAction : BaseAction
    {
        public override void Execute(ICharacter initiator, EventData eventData)
        {
            if (!eventData.TryGet(out InteractableEventData interactableEventData))
            {
                Log.Error("NPCFaceCharacterAction", "Missing InteractableEventData.");
                return;
            }

            // The 'interactable' in this context is the NPC itself (the crafter)
            IInteractable npcInteractable = interactableEventData.Interactable;
            IPlayerCharacter character = initiator as IPlayerCharacter;
            InteractableSystem serverInstance = interactableEventData.ServerInstance;

            if (character == null || npcInteractable == null || serverInstance == null)
            {
                Log.Warning("NPCFaceCharacterAction", "Invalid character, NPC interactable, or server instance.");
                return;
            }

            // Call the existing method on the InteractableSystem
            serverInstance.OnInteractNPC(character, npcInteractable);
            Log.Debug("NPCFaceCharacterAction", $"NPC {npcInteractable.GetType().Name} made to face character {character.Name}.");
        }
    }
}