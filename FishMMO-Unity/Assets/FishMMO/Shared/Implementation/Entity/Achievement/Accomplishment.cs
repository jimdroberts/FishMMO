using FishMMO.Core;
using FishMMO.Shared.Core;

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
		public readonly CachedScriptableObject Template;

		/// <summary>How much progress it is worth.</summary>
		public readonly uint Amount;

		/// <summary>Builds the fact.</summary>
		public Accomplishment(ICharacter character, CachedScriptableObject template, uint amount)
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
		public static void Record(ICharacter character, CachedScriptableObject template, uint amount = 1)
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
}
