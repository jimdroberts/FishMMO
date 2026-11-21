using FishMMO.Core;
using FishMMO.Shared;

[assembly: GameModule(typeof(FishMMO.Shared.BuffsModule))]
// The controllers are parts every such character gets as it wakes (2026-11-21); no prefab authors them.
[assembly: FishMMO.Shared.CharacterPart(typeof(FishMMO.Shared.BuffController), typeof(FishMMO.Shared.Core.ICharacter))]
[assembly: FishMMO.Shared.CharacterPart(typeof(FishMMO.Shared.BuffVolumeController), typeof(FishMMO.Shared.Core.IPlayerCharacter))]

namespace FishMMO.Shared
{
	/// <summary>The Buffs module (2026-11-20). See the module's README.</summary>
	public sealed class BuffsModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "buffs";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
