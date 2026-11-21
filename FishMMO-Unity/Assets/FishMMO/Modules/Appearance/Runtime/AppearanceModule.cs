using FishMMO.Core;
using FishMMO.Shared;
using FishMMO.Shared.Core;

[assembly: GameModule(typeof(FishMMO.Shared.AppearanceModule))]
[assembly: CharacterPart(typeof(BodyVisibilityManager), typeof(IPlayerCharacter))]
[assembly: CharacterPart(typeof(CharacterAppearanceManager), typeof(IPlayerCharacter))]
[assembly: CharacterPart(typeof(EquipmentVisualController), typeof(IPlayerCharacter))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Appearance module (2026-11-20): how a player's body looks. Race proportions and blend shapes
	/// (<see cref="CharacterAppearanceManager"/>), body regions hidden under armour (<see cref="BodyVisibilityManager"/>) and the
	/// equipment drawn on the body (<see cref="EquipmentVisualController"/>), each added to every player as a character part.
	/// </summary>
	/// <remarks>
	/// The core keeps the model load and its ready signal (<see cref="IModelReadyHandler"/>), the data item templates carry
	/// (<see cref="BodyRegion"/>, <see cref="BlendShapeProfile"/>), the mesh-binding helpers the character select preview uses
	/// (<see cref="EquipmentVisualAttach"/>, <see cref="SkeletonBinder"/>), and the carried light. Without this module a
	/// player is drawn as its race model is authored: no proportions, nothing hidden, no equipment on the body.
	/// </remarks>
	public sealed class AppearanceModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "appearance";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
