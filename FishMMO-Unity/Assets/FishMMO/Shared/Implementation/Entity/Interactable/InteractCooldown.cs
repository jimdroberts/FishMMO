using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.InteractionModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// When a player may next use an interactable, on the player's Core entity. Seconds on the monotonic
	/// stopwatch; zero means "now".
	/// </summary>
	public struct InteractCooldown
	{
		/// <summary>Stopwatch seconds before which a new interaction is refused.</summary>
		public double NextAt;
	}

	/// <summary>Interaction as a module: gives every player its <see cref="InteractCooldown"/>.</summary>
	public sealed class InteractionModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "interaction";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
			builder.Archetype(Archetypes.Player).Add(new InteractCooldown());
		}
	}
}
