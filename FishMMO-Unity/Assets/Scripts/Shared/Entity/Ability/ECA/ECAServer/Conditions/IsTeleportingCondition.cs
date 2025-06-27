using FishMMO.Shared;
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
                Log.Warning("IsNotTeleportingCondition: Initiator is not an IPlayerCharacter.");
                return false;
            }

            if (character.IsTeleporting)
            {
                Log.Debug($"IsNotTeleportingCondition: Character {character.Name} is already teleporting.");
                return true;
            }
            return true;
        }
    }
}