using UnityEngine;

namespace FishMMO.Shared
{
    public interface ICondition<T>
    {
        bool Evaluate(T target);
    }
}