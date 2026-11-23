using FishMMO.Core;
using FishMMO.Shared;
using FishMMO.Shared.Core;

[assembly: GameModule(typeof(FishMMO.Shared.ItemsModule))]
// The player's three item containers. Parts, not prefab components: removing the module leaves the player prefabs whole.
[assembly: CharacterPart(typeof(InventoryController), typeof(IPlayerCharacter))]
[assembly: CharacterPart(typeof(BankController), typeof(IPlayerCharacter))]
[assembly: CharacterPart(typeof(EquipmentController), typeof(IPlayerCharacter))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Items module: what a player does with items. The inventory, bank and equipment containers on the
	/// player (equipment predicted on the character's replicate), the scene server's inventory system (every move, split,
	/// equip, grant and exchange, and the item writes), and the inventory, bank and equipment windows.
	/// </summary>
	/// <remarks>
	/// The item data is the core's: <see cref="Item"/>, <see cref="ItemSlot"/>, the item templates and loot tables, the
	/// container contracts (<see cref="IInventoryController"/>, <see cref="IBankController"/>,
	/// <see cref="IEquipmentController"/>), the item broadcasts and the equipment replicate fields, the item ECA actions and
	/// conditions, the item rows and their load at login. Without the module a player has no containers, so the load has
	/// nowhere to put the rows and nothing is sent.
	/// </remarks>
	public sealed class ItemsModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "items";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
