using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class BaseCondition : CachedScriptableObject<BaseCondition>, ICachedObject, ICondition
	{
		[TextArea]
		public string ConditionDescription = "";

		public abstract bool Evaluate(ICharacter initiator, EventData eventData = null);
	}
}