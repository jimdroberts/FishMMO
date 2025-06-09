using UnityEngine;

namespace FishMMO.Shared
{
    public abstract class BaseCondition<T> : ScriptableObject, ICondition<T>
    {
        // Description can be useful for designers to understand what the condition does
        [TextArea]
        public string ConditionDescription = "";

        public abstract bool Evaluate(T target);
    }
}