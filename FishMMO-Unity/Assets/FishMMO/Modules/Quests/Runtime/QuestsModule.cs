using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.QuestsModule))]
// Every player character carries its quest log (added at wake, not authored on the prefab).
[assembly: FishMMO.Shared.CharacterPart(typeof(FishMMO.Shared.QuestController), typeof(FishMMO.Shared.Core.IPlayerCharacter))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Quests module: quest templates, a player's quest log, the quest system, quest givers and the quest ECA
	/// actions and conditions. Achievements stay in the core (every system records accomplishments). See the README.
	/// </summary>
	public sealed class QuestsModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "quests";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
