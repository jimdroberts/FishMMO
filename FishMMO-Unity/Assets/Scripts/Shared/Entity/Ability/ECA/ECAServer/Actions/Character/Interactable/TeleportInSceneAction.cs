using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server
{
    [CreateAssetMenu(fileName = "TeleportInSceneAction", menuName = "FishMMO/Actions/Teleporter/In-Scene Teleport", order = 0)]
    public class TeleportInSceneAction : BaseAction
    {
        public override void Execute(ICharacter initiator, EventData eventData)
        {
            if (!eventData.TryGet(out InteractableEventData interactableEventData))
            {
                Log.Error("TeleportInSceneAction: Missing InteractableEventData.");
                return;
            }

            IPlayerCharacter character = initiator as IPlayerCharacter;
            Teleporter teleporter = interactableEventData.Interactable as Teleporter;

            if (character == null || teleporter == null || teleporter.Target == null || character.Motor == null)
            {
                Log.Warning("TeleportInSceneAction: Invalid character, teleporter, or target for in-scene teleport.");
                return;
            }

            // Perform the in-scene teleport
            character.Motor.SetPositionAndRotationAndVelocity(teleporter.Target.position, teleporter.Target.rotation, Vector3.zero);
            Log.Debug($"Character {character.Name} teleported to {teleporter.Target.position} in current scene.");
        }
    }
}