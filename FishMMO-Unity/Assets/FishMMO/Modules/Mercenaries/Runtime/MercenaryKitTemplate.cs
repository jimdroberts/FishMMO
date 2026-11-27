using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A mercenary's preset kit (issue #135): what it is (Warrior, Archer, Wizard, Healer...), what it costs to hire, the
	/// NPC that is hired, and the abilities it fights with. A hired mercenary knows exactly the kit's abilities and they
	/// cannot be changed: nothing teaches a mercenary anything else.
	/// </summary>
	/// <remarks>
	/// One prefab per kit. The prefab's AI archetype (the server's brain catalogue) decides how it fights, and the module's
	/// builder sets it to the matching pet archetype, so a mercenary follows, guards and supports its owner by the pet
	/// rules (<c>PetIdleState</c>, <c>PetSupport</c>). Stats are fixed per kit (the prefab's attribute database and bonus);
	/// only the gear its owner keeps for it raises them.
	/// </remarks>
	[CreateAssetMenu(fileName = "New Mercenary Kit", menuName = "FishMMO/Mercenaries/Mercenary Kit", order = 1)]
	public class MercenaryKitTemplate : CachedScriptableObject<MercenaryKitTemplate>, ICachedObject
	{
		[Tooltip("The kit's name, as players read it: Warrior, Archer, Wizard, Healer.")]
		public string DisplayName;

		[Tooltip("One line about how it fights, shown when hiring and in the Mercenary window.")]
		[TextArea(1, 3)]
		public string Description;

		[Tooltip("What hiring costs, in currency. Paid once: returning a resting mercenary is free.")]
		public long HireCost = 500;

		[Tooltip("The NPC that is hired: a Mercenary prefab, spawned at its owner's side.")]
		public NetworkObject Prefab;

		[Tooltip("The abilities it fights with. Fixed: a hired mercenary learns these and nothing else.")]
		[TemplateOf("AbilityTemplate")]
		public List<CachedScriptableObject> Abilities = new List<CachedScriptableObject>();

		/// <summary>The kit's name, or the asset's when none is set.</summary>
		public string Label => string.IsNullOrWhiteSpace(DisplayName) ? name : DisplayName;
	}
}
