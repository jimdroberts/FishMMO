using FishMMO.Core;
using FishMMO.Shared;
using FishMMO.Shared.Core;

[assembly: GameModule(typeof(FishMMO.Shared.CombatModule))]
// Every character's damage, healing, death and revive: players and NPCs alike. A part, not a prefab component.
[assembly: CharacterPart(typeof(CharacterDamageController), typeof(ICharacter))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Combat module: damage, healing, death and revive. Every character's
	/// <see cref="CharacterDamageController"/> (the rules: resistances, immortality, the combat timer, the refusals and the
	/// combat reports), the scene server's combat system (a death's consequences, respawning at the bind point, resurrect
	/// offers and their acceptance), and the client's combat text and death dialog.
	/// </summary>
	/// <remarks>
	/// The core keeps the contract (<see cref="ICharacterDamageController"/> and its static events, which abilities, buffs,
	/// AI, pets and the world listen to), the combat and death messages, the damage and resistance templates, the predicted
	/// combat events, and what a death touches that is the world's: an instance's lives, the combat-logout linger, the
	/// transfer to a bind point in another scene. Without the module nothing can be hurt, so nobody dies.
	/// </remarks>
	public sealed class CombatModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "combat";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
