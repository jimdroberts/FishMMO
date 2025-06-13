using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class BaseCondition : ScriptableObject, ICondition
	{
		[TextArea]
		public string ConditionDescription = "";

		public abstract bool Evaluate(ICharacter initiator, EventData eventData = null);
	}
}