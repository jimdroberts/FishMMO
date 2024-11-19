using System;

namespace FishMMO.Shared
{
    public interface IPetController : ICharacterBehaviour
    {
        static Action<Pet> OnPetSummoned;
        static Action OnPetDestroyed;

        PetAbilityTemplate PetAbilityTemplate { get; set; }
        Pet Pet { get; set; }
    }
}