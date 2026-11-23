using FishMMO.Core;
using FishMMO.Shared;

[assembly: GameModule(typeof(FishMMO.Shared.PetsModule))]
// The controllers are parts every such character gets as it wakes; no prefab authors them.
[assembly: FishMMO.Shared.CharacterPart(typeof(FishMMO.Shared.PetController), typeof(FishMMO.Shared.Core.IPlayerCharacter))]

namespace FishMMO.Shared
{
	/// <summary>The Pets module. See the module's README.</summary>
	public sealed class PetsModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "pets";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
