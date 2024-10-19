using System;
using UnityEngine;

namespace FishMMO.Shared
{
    public interface IPetController
    {
        event Action<GameObject> OnPetSummoned;
        event Action<GameObject> OnPetDestroyed;
    }
}