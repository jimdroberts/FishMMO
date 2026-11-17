using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.SocialModule))]
// Every player character carries its friend list (added at wake, not authored on the prefab).
[assembly: FishMMO.Shared.CharacterPart(typeof(FishMMO.Shared.FriendController), typeof(FishMMO.Shared.Core.IPlayerCharacter))]
// ...and its party membership (the core reads it as IPartyController).
[assembly: FishMMO.Shared.CharacterPart(typeof(FishMMO.Shared.PartyController), typeof(FishMMO.Shared.Core.IPlayerCharacter))]
// ...and its guild (the core reads it as IGuildController; observers get the guild ID in the spawn payload).
[assembly: FishMMO.Shared.CharacterPart(typeof(FishMMO.Shared.GuildController), typeof(FishMMO.Shared.Core.IPlayerCharacter))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Social module: friends, party and guild (moved 2026-11-16). See the module's README.
	/// </summary>
	public sealed class SocialModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "social";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
