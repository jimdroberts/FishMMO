using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.ArchetypesModule))]
// Every player character carries its archetype (added at wake, not authored on the prefab).
[assembly: FishMMO.Shared.CharacterPart(typeof(FishMMO.Shared.ArchetypeController), typeof(FishMMO.Shared.Core.IPlayerCharacter))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Archetypes module: a player's archetype, its change triggers, its persistence and the archetype condition.
	/// <see cref="ArchetypeTemplate"/> stays in the core (NPC guilds list archetypes). An archetype grants what it grants
	/// through attributes and triggers, e.g. Pet Slots through an attribute modifier. See the module's README.
	/// </summary>
	public sealed class ArchetypesModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "archetypes";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
