using System;

namespace FishMMO.Shared
{
    public interface IPetController : ICharacterBehaviour
    {
        static event Action<Pet> OnPetSummoned;
        static event Action<Pet> OnPetDestroyed;

        Pet Pet { get; set; }
    }
}