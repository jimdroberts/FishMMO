using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Archetype Condition", menuName = "FishMMO/Conditions/Archetype Condition", order = 1)]
	public class ArchetypeCondition : BaseCondition
	{
		public ArchetypeTemplate ArchetypeTemplate;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			if (initiator == null)
			{
				Debug.LogWarning($"Character does not exist.");
				return false;
			}
			if (!initiator.TryGet(out IArchetypeController archetypeController))
			{
				Debug.LogWarning($"Character does not have an IArchetypeController.");
				return false;
			}

			return archetypeController.Template.ID == ArchetypeTemplate.ID;
		}
	}
}