using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Hit Count Condition", menuName = "FishMMO/Triggers/Conditions/Ability/Hit Count", order = 20)]
	public sealed class HitCountCondition : BaseCondition
	{
		[Tooltip("The HitCount value to compare against.")]
		public int RequiredHitCount;

		[Tooltip("The type of comparison to perform with the AbilityObject's HitCount.")]
		public HitCountComparisonType ComparisonType;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out AbilityCollisionEventData hitEventData))
			{
				AbilityObject abilityObject = hitEventData.AbilityObject;

				if (abilityObject == null)
				{
					Log.Warning("HitCountCondition", $"No AbilityObject found in AbilityHitEventData for initiator {initiator?.Name}.");
					return false;
				}

				// Perform the comparison based on the selected type
				switch (ComparisonType)
				{
					case HitCountComparisonType.GreaterThan:
						return abilityObject.HitCount > RequiredHitCount;
					case HitCountComparisonType.GreaterThanOrEqualTo:
						return abilityObject.HitCount >= RequiredHitCount;
					case HitCountComparisonType.LessThan:
						return abilityObject.HitCount < RequiredHitCount;
					case HitCountComparisonType.LessThanOrEqualTo:
						return abilityObject.HitCount <= RequiredHitCount;
					case HitCountComparisonType.EqualTo:
						return abilityObject.HitCount == RequiredHitCount;
					case HitCountComparisonType.NotEqualTo:
						return abilityObject.HitCount != RequiredHitCount;
					default:
						Log.Error("HitCountCondition", $"Unknown ComparisonType {ComparisonType}.");
						return false;
				}
			}
			else
			{
				// This condition only makes sense for AbilityHitEventData as it requires an AbilityObject
				Log.Warning("HitCountCondition", $"EventData is not of type AbilityHitEventData for initiator {initiator?.Name}.");
				return false;
			}
		}

		public override string GetFormattedDescription()
		{
			string comparison = ComparisonType.ToString().Replace("OrEqualTo", " or equal to").Replace("GreaterThan", "> ").Replace("LessThan", "< ").Replace("EqualTo", "==").Replace("NotEqualTo", "!=");
			return $"Ability HitCount must be {ComparisonType.ToString().Replace("OrEqualTo", " or equal to").Replace("GreaterThan", "> ").Replace("LessThan", "< ").Replace("EqualTo", "equal to").Replace("NotEqualTo", "not equal to")} {RequiredHitCount}.";
		}
	}
}