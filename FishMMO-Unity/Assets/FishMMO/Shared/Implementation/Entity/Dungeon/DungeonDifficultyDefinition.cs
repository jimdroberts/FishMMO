using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// One difficulty a dungeon can be run at: what it takes to get in, what it does to the
	/// dungeon, and what it pays for the trouble.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A plain serializable class rather than an enum, because dungeons do not agree on how many
	/// difficulties they have or what those difficulties mean. A short introductory dungeon may
	/// offer only one; a raid may offer five, with the top one banning resurrection outright.
	/// Every dungeon declares its own list in <see cref="DungeonTemplate.Difficulties"/> and an
	/// instance records the <em>index</em> it was opened at, so the name and the rules are always
	/// read back from the same dungeon that defined them.
	/// </para>
	/// <para>
	/// <b>Every difficulty is a trade.</b> The fields are deliberately grouped into requirements,
	/// detriments and benefits, and <see cref="BuildRulesSummary"/> renders all three into the
	/// text the dungeon finder shows — from these values, not from a hand-written blurb that can
	/// drift away from them. An author who makes a difficulty harder without paying for it will
	/// see exactly that in the panel.
	/// </para>
	/// </remarks>
	[Serializable]
	public class DungeonDifficultyDefinition
	{
		/// <summary>Display name of this difficulty, e.g. "Normal" or "Hardcore".</summary>
		[Tooltip("Display name shown on the difficulty tab, e.g. Normal or Hardcore.")]
		public string Name = "Normal";

		/// <summary>
		/// Optional flavour text shown above the generated rules summary.
		/// </summary>
		/// <remarks>
		/// Flavour only. The rules the player is actually agreeing to are generated from the
		/// fields below, so a note left stale by a later balance change cannot misrepresent them.
		/// </remarks>
		[Tooltip("Optional flavour text. The rules list underneath is generated from the values below.")]
		[TextArea(2, 4)]
		public string Description;

		// ── Requirements ────────────────────────────────────────────────────

		/// <summary>
		/// Smallest party permitted, counting the character entering. 1 allows a solo run.
		/// </summary>
		/// <remarks>
		/// Checked against the party roster rather than against who is currently inside, so a
		/// dungeon that requires a group cannot be entered by the one member who arrives first
		/// and then abandoned to a solo player.
		/// </remarks>
		[Header("Requirements")]
		[Tooltip("Smallest party permitted, counting the entering character. 1 allows solo.")]
		[Min(1)]
		public int MinimumPartySize = 1;

		/// <summary>
		/// Capacity of an instance at this difficulty, or 0 to use the scene's own MaxClients.
		/// </summary>
		[Tooltip("Instance capacity at this difficulty. 0 uses the scene's own MaxClients.")]
		[Min(0)]
		public int MaximumPlayers;

		// ── Group finder ────────────────────────────────────────────────────

		/// <summary>
		/// Whether Find Group is offered at this difficulty.
		/// </summary>
		/// <remarks>
		/// Off for content that should only be run by groups who chose each other — a difficulty
		/// whose first death ends a character's run is a poor thing to be matched into with
		/// strangers. Browsing and joining open runs at the entrance is unaffected; only the
		/// automatic queue is.
		/// </remarks>
		[Header("Group Finder")]
		[Tooltip("Whether Find Group is offered at this difficulty. Browsing and joining open runs is unaffected.")]
		public bool GroupFinderEnabled = true;

		/// <summary>
		/// Players the group finder gathers before opening a run, or 0 to fill to capacity.
		/// </summary>
		/// <remarks>
		/// Never below <see cref="MinimumPartySize"/> — the finder cannot form a group the dungeon
		/// would refuse — and never below two, because a group of one is not a group. See
		/// <see cref="GroupFinderRules.ResolveGroupSize"/> for the exact resolution.
		/// </remarks>
		[Tooltip("Players the group finder waits for before opening a run. 0 fills to capacity.")]
		[Min(0)]
		public int GroupFinderSize;

		// ── Detriments ──────────────────────────────────────────────────────

		/// <summary>
		/// Multiplier applied to every resource attribute of NPCs spawned inside — health, and
		/// whatever else a build treats as a resource.
		/// </summary>
		/// <remarks>
		/// The tankiness lever, and separate from <see cref="EnemyAttributeScalars"/> because
		/// resource attributes are the one group the code can identify on its own. Everything else
		/// has to be named.
		/// </remarks>
		[Header("Detriments")]
		[Tooltip("Multiplier applied to NPC health and other resource attributes inside the instance.")]
		[Min(0.01f)]
		public float EnemyResourceMultiplier = 1.0f;

		/// <summary>
		/// Named NPC attributes to scale inside the instance, and by how much.
		/// </summary>
		/// <remarks>
		/// Deliberately a list of named templates rather than a fixed "enemy damage" figure. There
		/// is no built-in notion of which attribute represents damage — that is a decision each
		/// build makes when it authors its attribute templates — so a fixed field would have had
		/// to guess at one, and would have been wrong for any build that split damage across
		/// several attributes or called it something else.
		/// <para>
		/// Naming them also makes the rules summary say what actually changes: "Attack Power +40%"
		/// rather than a generic figure the player has to take on trust.
		/// </para>
		/// </remarks>
		[Tooltip("NPC attributes to scale inside the instance, e.g. an attack power attribute.")]
		public List<DungeonAttributeScalar> EnemyAttributeScalars = new List<DungeonAttributeScalar>();

		/// <summary>
		/// Deaths a character may suffer inside before being removed from the instance. 0 is
		/// unlimited.
		/// </summary>
		/// <remarks>
		/// 1 is the "one death" rule: the first death ends that character's run and returns them
		/// to the open world. It removes only the character who died — the instance and everybody
		/// else in it carry on, because ending a group's run over one member's mistake is a
		/// harsher rule than any dungeon here is trying to express.
		/// </remarks>
		[Tooltip("Deaths allowed per character before removal from the instance. 0 is unlimited.")]
		[Min(0)]
		public int LivesPerCharacter;

		/// <summary>Whether characters may be resurrected inside the instance.</summary>
		[Tooltip("Whether characters may be resurrected inside the instance.")]
		public bool AllowResurrection = true;

		/// <summary>
		/// Lifetime of an instance at this difficulty in minutes, or 0 for the server default.
		/// </summary>
		[Tooltip("Instance lifetime in minutes. 0 uses the server's MaxInstanceLifetimeMinutes.")]
		[Min(0)]
		public int LifetimeMinutes;

		// ── Benefits ────────────────────────────────────────────────────────

		/// <summary>Multiplier applied to how many item stacks a corpse inside drops.</summary>
		[Header("Benefits")]
		[Tooltip("Multiplier applied to the number of item stacks corpses drop inside the instance.")]
		[Min(0.01f)]
		public float LootQuantityMultiplier = 1.0f;

		/// <summary>Multiplier applied to the currency corpses inside drop.</summary>
		[Tooltip("Multiplier applied to the currency corpses drop inside the instance.")]
		[Min(0.01f)]
		public float CurrencyMultiplier = 1.0f;

		/// <summary>
		/// Capacity of an instance at this difficulty, resolved against the scene's own limit.
		/// </summary>
		/// <param name="sceneMaxClients">The scene's declared MaxClients.</param>
		/// <returns>The capacity to enforce, never below 1.</returns>
		public int ResolveCapacity(int sceneMaxClients)
		{
			int capacity = MaximumPlayers > 0 ? MaximumPlayers : sceneMaxClients;
			return capacity < 1 ? 1 : capacity;
		}

		/// <summary>
		/// Renders this difficulty's requirements, detriments and benefits as display lines.
		/// </summary>
		/// <remarks>
		/// Generated rather than authored so the panel can never describe a ruleset the server is
		/// not enforcing. Lines that say nothing — a multiplier of exactly 1, a requirement of 0 —
		/// are omitted, so a difficulty that changes little produces a short list rather than a
		/// wall of "no change".
		/// </remarks>
		/// <returns>One line per rule, newline separated. Empty when nothing differs from default.</returns>
		public string BuildRulesSummary()
		{
			StringBuilder sb = new StringBuilder(256);

			if (MinimumPartySize > 1)
			{
				AppendLine(sb, $"Requires a party of {MinimumPartySize} or more.");
			}
			if (MaximumPlayers > 0)
			{
				AppendLine(sb, $"Holds up to {MaximumPlayers} players.");
			}

			/* The finder's group size is only stated when the author pinned it, because the
			 * default — fill to capacity — depends on the scene's MaxClients, which this class
			 * cannot see. The panel's Find Group button is disabled outright when the finder is
			 * off, so that case needs no line. */
			if (GroupFinderEnabled && GroupFinderSize > 0)
			{
				AppendLine(sb, $"Find Group forms parties of {GroupFinderSize}.");
			}

			AppendMultiplier(sb, EnemyResourceMultiplier, "Enemy health");

			if (EnemyAttributeScalars != null)
			{
				for (int i = 0; i < EnemyAttributeScalars.Count; ++i)
				{
					DungeonAttributeScalar scalar = EnemyAttributeScalars[i];
					if (scalar == null || scalar.Template == null)
					{
						continue;
					}
					AppendMultiplier(sb, scalar.Multiplier, "Enemy " + scalar.Template.Name.ToLowerInvariant());
				}
			}

			if (LivesPerCharacter == 1)
			{
				AppendLine(sb, "One death: dying removes you from the dungeon.");
			}
			else if (LivesPerCharacter > 1)
			{
				AppendLine(sb, $"{LivesPerCharacter} deaths: the {Ordinal(LivesPerCharacter)} removes you from the dungeon.");
			}

			if (!AllowResurrection)
			{
				AppendLine(sb, "No resurrection inside the dungeon.");
			}

			if (LifetimeMinutes > 0)
			{
				AppendLine(sb, $"Closes {LifetimeMinutes} minutes after it opens.");
			}

			AppendMultiplier(sb, LootQuantityMultiplier, "Loot");
			AppendMultiplier(sb, CurrencyMultiplier, "Currency");

			return sb.ToString();
		}

		/// <summary>
		/// Appends one "X +50%" / "X -20%" line, or nothing at all when the multiplier is 1.
		/// </summary>
		private static void AppendMultiplier(StringBuilder sb, float multiplier, string label)
		{
			// Exactly 1 is the common case and says nothing worth a line. The epsilon absorbs the
			// float noise an inspector-entered value carries, so 1.0 never renders as "+0%".
			if (multiplier > 0.999f && multiplier < 1.001f)
			{
				return;
			}

			int percent = Mathf.RoundToInt((multiplier - 1.0f) * 100.0f);
			if (percent == 0)
			{
				return;
			}

			AppendLine(sb, percent > 0 ? $"{label} +{percent}%." : $"{label} {percent}%.");
		}

		private static void AppendLine(StringBuilder sb, string line)
		{
			if (sb.Length > 0)
			{
				sb.Append('\n');
			}
			sb.Append("• ").Append(line);
		}

		/// <summary>
		/// Ordinal word for a small life count, so the summary reads as prose.
		/// </summary>
		private static string Ordinal(int value)
		{
			switch (value)
			{
				case 2: return "second";
				case 3: return "third";
				case 4: return "fourth";
				case 5: return "fifth";
				default: return value + "th";
			}
		}
	}
}
