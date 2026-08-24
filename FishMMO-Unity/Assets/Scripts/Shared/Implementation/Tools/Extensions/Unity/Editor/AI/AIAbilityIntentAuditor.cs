using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Prints what <see cref="AIAbilityClassifier"/> makes of every ability in the project.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Ability selection is now derived rather than configured: an archetype no longer names the
	/// abilities it should reach for, and instead asks what each ability in the creature's spellbook
	/// actually does. That is a large improvement in maintenance cost and a large reduction in
	/// visibility — the answer is computed at runtime inside an NPC's head, where nobody can see it.
	/// </para>
	/// <para>
	/// This makes it visible. Run it after authoring an ability to confirm the AI reads it the way
	/// it was meant, and to catch the one case classification cannot get right on its own: a buff
	/// whose beneficial-or-detrimental nature is inferred from the sign of the attributes it
	/// modifies. An ability listed here as Buff that was meant as a curse is fixed by setting
	/// <see cref="AbilityTemplate.IntentOverride"/>, and this report is how that gets noticed.
	/// </para>
	/// </remarks>
	public static class AIAbilityIntentAuditor
	{
		/// <summary>Log category.</summary>
		private const string LOG = "AIAbilityIntentAuditor";

		/// <summary>
		/// Reports the derived intent of every <see cref="AbilityTemplate"/> in the project.
		/// </summary>
		[MenuItem("FishMMO/AI/Audit Ability Intents", priority = 205)]
		public static void AuditAbilityIntents()
		{
			// Templates may have been edited since the last run; never report a stale answer.
			AIAbilityClassifier.ClearCache();

			string[] guids = AssetDatabase.FindAssets("t:AbilityTemplate");
			if (guids.Length == 0)
			{
				Debug.Log($"[{LOG}] No AbilityTemplate assets found.");
				return;
			}

			List<string> classified = new List<string>();
			List<string> unclassified = new List<string>();
			List<string> overridden = new List<string>();

			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				AbilityTemplate template = AssetDatabase.LoadAssetAtPath<AbilityTemplate>(path);
				if (template == null)
				{
					continue;
				}

				AIAbilityIntent intent = AIAbilityClassifier.Classify(template);

				if (template.IntentOverride != AIAbilityIntent.None)
				{
					overridden.Add($"  {template.name}: {intent} (forced by IntentOverride)");
					continue;
				}

				if (intent == AIAbilityIntent.None)
				{
					/* Not necessarily wrong — an ability may legitimately do nothing the AI needs to
					 * reason about. But it is also exactly what a mis-authored ECA graph looks like,
					 * and an NPC treats an unclassifiable ability as usable against an enemy, so it
					 * is worth listing separately rather than burying it in the main list. */
					unclassified.Add($"  {template.name} ({path})");
					continue;
				}

				classified.Add($"  {template.name}: {intent}");
			}

			classified.Sort();
			unclassified.Sort();
			overridden.Sort();

			StringBuilder report = new StringBuilder();
			report.AppendLine($"[{LOG}] {guids.Length} ability template(s) examined.");

			if (classified.Count > 0)
			{
				report.AppendLine($"\nClassified ({classified.Count}):");
				foreach (string line in classified) report.AppendLine(line);
			}

			if (overridden.Count > 0)
			{
				report.AppendLine($"\nManually overridden ({overridden.Count}):");
				foreach (string line in overridden) report.AppendLine(line);
			}

			if (unclassified.Count > 0)
			{
				report.AppendLine(
					$"\nNo recognisable intent ({unclassified.Count}). These are treated as usable " +
					"against an enemy. If one of them heals or buffs, its ECA graph is not saying so:");
				foreach (string line in unclassified) report.AppendLine(line);
			}

			if (unclassified.Count > 0)
			{
				Debug.LogWarning(report.ToString());
			}
			else
			{
				Debug.Log(report.ToString());
			}
		}
	}
}
