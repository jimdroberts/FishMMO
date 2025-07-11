using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server
{
    [CreateAssetMenu(fileName = "SetCharacterBindPointAction", menuName = "FishMMO/Actions/Character/Set Bind Point", order = 0)]
    public class SetCharacterBindPointAction : BaseAction
    {
        public override void Execute(ICharacter initiator, EventData eventData)
        {
            // We already checked character validity in a condition, but a quick null check is harmless.
            IPlayerCharacter character = initiator as IPlayerCharacter;
            if (character == null || character.Motor == null || character.Motor.Transform == null)
            {
                Log.Warning("SetCharacterBindPointAction", "Invalid character for setting bind point.");
                return;
            }

            // Set the bind position and scene
            character.BindPosition = character.Motor.Transform.position;
            character.BindScene = character.SceneName;

            Log.Debug("SetCharacterBindPointAction", $"Character {character.Name} bind point set to: {character.BindPosition} in scene: {character.BindScene}.");
        }
    }
}