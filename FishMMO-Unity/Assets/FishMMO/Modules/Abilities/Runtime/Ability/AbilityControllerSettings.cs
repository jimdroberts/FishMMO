using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// What every character's <see cref="AbilityController"/> reads its speed attributes from (2026-11-21): the attributes
	/// that shorten an attack, a cast and a cooldown. Each prefab used to author them on its own controller before the
	/// controller became a part the character adds at wake.
	/// </summary>
	/// <remarks>
	/// One asset, addressable with the shared permanent label, so it is in the cache on every peer before any character
	/// wakes. A character without one of these attributes is unaffected by it (<c>CalculateSpeedReduction</c> answers 1),
	/// which is how the town NPCs and pets that authored none behave the same with them set.
	/// </remarks>
	[CreateAssetMenu(fileName = "AbilityControllerSettings", menuName = "FishMMO/Abilities/Ability Controller Settings", order = 1)]
	public class AbilityControllerSettings : CachedScriptableObject<AbilityControllerSettings>, ICachedObject
	{
		[Tooltip("The attribute that shortens an attack ability's activation.")]
		public CharacterAttributeTemplate AttackSpeedReductionTemplate;

		[Tooltip("The attribute that shortens a cast.")]
		public CharacterAttributeTemplate CastSpeedReductionTemplate;

		[Tooltip("The attribute that shortens a cooldown.")]
		public CharacterAttributeTemplate CooldownReductionTemplate;

		/// <summary>The loaded asset, or null (an edit-mode test, a build without it).</summary>
		public static AbilityControllerSettings Active => GetFirst<AbilityControllerSettings>();

		/// <summary>Fills whichever of the controller's speed attributes are unset.</summary>
		public void ApplyTo(AbilityController controller)
		{
			if (controller == null)
			{
				return;
			}
			if (controller.AttackSpeedReductionTemplate == null) controller.AttackSpeedReductionTemplate = AttackSpeedReductionTemplate;
			if (controller.CastSpeedReductionTemplate == null) controller.CastSpeedReductionTemplate = CastSpeedReductionTemplate;
			if (controller.CooldownReductionTemplate == null) controller.CooldownReductionTemplate = CooldownReductionTemplate;
		}
	}
}
