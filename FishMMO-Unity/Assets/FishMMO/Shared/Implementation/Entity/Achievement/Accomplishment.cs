using FishMMO.Core;
using FishMMO.Shared.Core;

[assembly: GameModule(typeof(FishMMO.Shared.AchievementModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// A fact: a character did something content has tied to an achievement (created a guild, summoned a pet,
	/// bought from a merchant). Published as a signal by the feature where it happened; the achievements module
	/// counts it. The feature names the achievement its content was given and never the achievement system.
	/// </summary>
	public readonly struct Accomplishment
	{
		/// <summary>Who did it.</summary>
		public readonly ICharacter Character;

		/// <summary>The achievement the content ties to it.</summary>
		public readonly AchievementTemplate Template;

		/// <summary>How much progress it is worth.</summary>
		public readonly uint Amount;

		/// <summary>Builds the fact.</summary>
		public Accomplishment(ICharacter character, AchievementTemplate template, uint amount)
		{
			Character = character;
			Template = template;
			Amount = amount;
		}
	}

	/// <summary>Publishing <see cref="Accomplishment"/>s.</summary>
	public static class Accomplishments
	{
		/// <summary>
		/// States that <paramref name="character"/> made progress on <paramref name="template"/>, on the
		/// character's world, at once: whatever counts it has done so before this returns, so a save that follows
		/// carries it. Nothing happens when the template is null or no achievements module is installed.
		/// </summary>
		public static void Record(ICharacter character, AchievementTemplate template, uint amount = 1)
		{
			if (character == null || template == null || amount == 0)
			{
				return;
			}
			EntityLink link = character.CoreLink();
			if (!link.IsValid)
			{
				FishMMO.Logging.Log.Warning("Accomplishments", $"Character {character.ID} has no Core entity; progress on {template.name} was not recorded.");
				return;
			}
			link.World.Publish(new Accomplishment(character, template, amount));
		}
	}

	/// <summary>
	/// Achievements as a module: counts every <see cref="Accomplishment"/> on the server toward the character's
	/// achievement, so the features that cause progress never reach for the achievement system.
	/// </summary>
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
				achievementController.Increment(accomplishment.Template, accomplishment.Amount);
			}
		}
	}
}
