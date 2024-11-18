using System;

namespace FishMMO.Shared
{
    public interface IPetController : ICharacterBehaviour
    {
        static Action<Pet> OnPetSummoned;
        static Action OnPetDestroyed;

        Pet Pet { get; set; }
    }
}