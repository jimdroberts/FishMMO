using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Archetype Condition", menuName = "FishMMO/Conditions/Archetype Condition", order = 1)]
	public class ArchetypeCondition : BaseCondition<IPlayerCharacter>
	{
		public ArchetypeTemplate ArchetypeTemplate;

		public override bool Evaluate(IPlayerCharacter playerCharacter)
		{
			if (playerCharacter == null)
			{
				Debug.LogWarning($"Player character does not exist.");
				return false;
			}
			if (!playerCharacter.TryGet(out IArchetypeController archetypeController))
			{
				Debug.LogWarning($"Player character {playerCharacter.CharacterName} does not have an IArchetypeController.");
				return false;
			}

			return true;//archetypeController.Template == ArchetypeTemplate;
		}
	}
}