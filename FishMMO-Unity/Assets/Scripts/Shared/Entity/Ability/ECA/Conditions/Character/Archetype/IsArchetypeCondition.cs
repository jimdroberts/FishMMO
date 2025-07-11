using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Is Archetype Condition", menuName = "FishMMO/Conditions/Is Archetype", order = 1)]
	public class IsArchetypeCondition : BaseCondition
	{
		public ArchetypeTemplate ArchetypeTemplate;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			if (initiator == null)
			{
				Log.Warning("IsArchetypeCondition", $"Character does not exist.");
				return false;
			}
			if (!initiator.TryGet(out IArchetypeController archetypeController))
			{
				Log.Warning("IsArchetypeCondition", $"Character does not have an IArchetypeController.");
				return false;
			}

			return archetypeController.Template.ID == ArchetypeTemplate.ID;
		}
	}
}