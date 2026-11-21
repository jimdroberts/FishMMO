using FishMMO.Core;
using FishMMO.Shared.Core;

[assembly: GameModule(typeof(FishMMO.Shared.AchievementModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// Achievements as a module: counts every <see cref="Accomplishment"/> on the server toward the character's
	/// achievement, so the features that cause progress never reach for the achievement system.
	/// </summary>
	/// <remarks>
	/// The signal and <see cref="Accomplishments.Record"/> stay in the core, with <see cref="AchievementTemplate"/> (content
	/// across the game names achievements); without this module a recorded accomplishment counts toward nothing.
	/// </remarks>
	public sealed class AchievementModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "achievements";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
			builder.OnSignal<Accomplishment>(SimulationSide.Server, Count);
		}

		private static void Count(World world, in Accomplishment accomplishment)
		{
			if (accomplishment.Character != null &&
				accomplishment.Character.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(accomplishment.Template as AchievementTemplate, accomplishment.Amount);
			}
		}
	}
}
