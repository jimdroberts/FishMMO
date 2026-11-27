using FishMMO.Core;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;

[assembly: GameModule(typeof(FishMMO.Shared.MercenariesModule))]
// Every player has a mercenary controller and a gear container; no prefab authors them.
[assembly: FishMMO.Shared.CharacterPart(typeof(FishMMO.Shared.MercenaryController), typeof(FishMMO.Shared.Core.IPlayerCharacter))]
[assembly: FishMMO.Shared.CharacterPart(typeof(FishMMO.Shared.MercenaryGearController), typeof(FishMMO.Shared.Core.IPlayerCharacter))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Mercenaries module (issue #135): sellswords for hire. A player speaks to a Mercenary in town and hires them for a
	/// fee; the mercenary follows, guards and fights by the pet rules with a preset kit (Warrior, Archer, Wizard, Healer),
	/// takes a seat in the player's party (forming one when there is none), and wears the gear the player keeps for it.
	/// One per player and one per party. It can be sent to rest to free the seat and brought back for free; if it dies it
	/// leaves, and the gear stays for the next hire. See the module's README.
	/// </summary>
	public sealed class MercenariesModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "mercenaries";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}

		/// <summary>
		/// The two process-wide seams: the gear container is saved and loaded with the character's items, and the party
		/// finds a member's seated mercenary (its vitals, its row) by the owner's ID.
		/// </summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void InstallSeams()
		{
			ModuleItemContainers.Register(InventoryType.MercenaryEquipment,
				character => character != null && character.TryGet(out IMercenaryGear gear) ? gear : null);
			PartySeatCompanions.Resolve = ownerID => Mercenary.GetByOwner(ownerID);
		}
	}
}
