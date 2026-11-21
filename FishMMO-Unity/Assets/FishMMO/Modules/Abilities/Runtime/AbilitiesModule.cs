using FishMMO.Core;
using FishMMO.Shared;

[assembly: GameModule(typeof(FishMMO.Shared.AbilitiesModule))]
// The controllers are parts every such character gets as it wakes (2026-11-21); no prefab authors them.
[assembly: FishMMO.Shared.CharacterPart(typeof(FishMMO.Shared.AbilityController), typeof(FishMMO.Shared.Core.ICharacter))]
[assembly: FishMMO.Shared.CharacterPart(typeof(FishMMO.Shared.CooldownController), typeof(FishMMO.Shared.Core.ICharacter))]

namespace FishMMO.Shared
{
	/// <summary>The Abilities module (2026-11-20). See the module's README.</summary>
	public sealed class AbilitiesModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "abilities";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
