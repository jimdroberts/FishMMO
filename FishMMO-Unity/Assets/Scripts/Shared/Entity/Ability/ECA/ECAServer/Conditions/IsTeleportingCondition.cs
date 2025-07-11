using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server
{
    [CreateAssetMenu(fileName = "IsTeleportingCondition", menuName = "FishMMO/Conditions/Character/Is Teleporting", order = 0)]
    public class IsTeleportingCondition : BaseCondition
    {
        public override bool Evaluate(ICharacter initiator, EventData eventData)
        {
            IPlayerCharacter character = initiator as IPlayerCharacter;
            if (character == null)
            {
                Log.Warning("IsTeleportingCondition", "Initiator is not an IPlayerCharacter.");
                return false;
            }

            if (character.IsTeleporting)
            {
                Log.Debug("IsTeleportingCondition", $"Character {character.Name} is already teleporting.");
                return true;
            }
            return true;
        }
    }
}