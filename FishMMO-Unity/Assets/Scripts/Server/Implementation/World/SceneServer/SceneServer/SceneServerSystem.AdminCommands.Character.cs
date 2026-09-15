using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// <c>/admin</c> character state: health, life and death, immortality, and attributes.
	/// </summary>
	/// <remarks>
	/// Administrator only, for the same reason as the economy commands: each of these changes the
	/// game a player is playing. Online characters on this scene server only, acting through the
	/// same controllers gameplay does, so every change replicates and persists the usual way.
	/// </remarks>
	public partial class SceneServerSystem
	{
		/// <summary>Health a revive restores: enough to be full, as the bind-point respawn is.</summary>
		private const int OperatorReviveHealth = 999999;

		/// <summary>The character part of the <c>/admin</c> table.</summary>
		private IEnumerable<OperatorCommand> BuildAdminCharacterCommands()
		{
			return new List<OperatorCommand>()
			{
				new OperatorCommand
				{
					Name = "heal", Category = "Character",
					Summary = "Restores a living character's resources to full. Leave the character out for yourself.",
					Arguments = "character:Character?", RosterAction = true,
					Run = HealCharacter,
				},
				new OperatorCommand
				{
					Name = "revive", Aliases = new[] { "resurrect" }, Category = "Character",
					Summary = "Brings a dead character back at full health where they lie.",
					Arguments = "character:Character?", RosterAction = true,
					Run = ReviveCharacter,
				},
				new OperatorCommand
				{
					Name = "kill", Category = "Character",
					Summary = "Kills a character. Nobody is credited, so no reward, quest or standing moves.",
					Arguments = "character:Character?", RosterAction = true, Destructive = true,
					Run = KillCharacter,
				},
				new OperatorCommand
				{
					Name = "god", Aliases = new[] { "immortal" }, Category = "Character",
					Summary = "Toggles immortality. It lasts until the character next changes scene or loads.",
					Arguments = "character:Character?", RosterAction = true, Destructive = true,
					Run = ToggleImmortality,
				},
				new OperatorCommand
				{
					Name = "attr", Category = "Character",
					Summary = "Shows one of a character's attributes: base, final, and current for a resource.",
					Arguments = "character:Character;attribute:Text", RosterAction = true,
					Run = ReportAttribute,
				},
				new OperatorCommand
				{
					Name = "setattr", Category = "Character",
					Summary = "Sets the base value of one of a character's attributes. Currency is set with setgold.",
					Arguments = "character:Character?;value:Integer;attribute:Text", RosterAction = true, Destructive = true,
					Run = SetAttribute,
				},
			};
		}

		/// <summary>Restores a living character's resources.</summary>
		private void HealCharacter(IPlayerCharacter character, string arguments)
		{
			if (!TryResolveOptionalTarget(character, arguments, out IPlayerCharacter target, out _) ||
				!TryGetDamageController(character, target, out ICharacterDamageController damageController))
			{
				return;
			}
			if (target.IsFlagged(CharacterFlags.IsDead))
			{
				Reply(character, $"{target.CharacterName} is dead. Use /admin revive.");
				return;
			}

			damageController.CompleteHeal();
			LogCharacterAction(character, target, "healed");
			Reply(character, $"Healed {target.CharacterName}.");
		}

		/// <summary>Revives a dead character where they are.</summary>
		/// <remarks>
		/// The same two steps the bind-point respawn and an accepted resurrect take, in the same order:
		/// clear the dead flag, then revive through the damage controller, which restores health and
		/// clears the death animation. Nobody is credited as the resurrector.
		/// </remarks>
		private void ReviveCharacter(IPlayerCharacter character, string arguments)
		{
			if (!TryResolveOptionalTarget(character, arguments, out IPlayerCharacter target, out _) ||
				!TryGetDamageController(character, target, out ICharacterDamageController damageController))
			{
				return;
			}
			if (!target.IsFlagged(CharacterFlags.IsDead))
			{
				Reply(character, $"{target.CharacterName} is not dead.");
				return;
			}

			target.DisableFlags(CharacterFlags.IsDead);
			damageController.Revive(null, OperatorReviveHealth);

			LogCharacterAction(character, target, "revived");
			Reply(character, $"Revived {target.CharacterName}.");
			if (target.ID != character.ID)
			{
				Reply(target, "Staff have revived you.");
			}
		}

		/// <summary>Kills a character with no killer.</summary>
		/// <remarks>
		/// No killer, deliberately. Kill credit drives faction standing, quest objectives and
		/// achievements; crediting the administrator would hand them rewards for a moderation action.
		/// </remarks>
		private void KillCharacter(IPlayerCharacter character, string arguments)
		{
			if (!TryResolveOptionalTarget(character, arguments, out IPlayerCharacter target, out _) ||
				!TryGetDamageController(character, target, out ICharacterDamageController damageController))
			{
				return;
			}
			if (target.IsFlagged(CharacterFlags.IsDead))
			{
				Reply(character, $"{target.CharacterName} is already dead.");
				return;
			}
			if (damageController.Immortal)
			{
				Reply(character, $"{target.CharacterName} is immortal. /admin god toggles it.");
				return;
			}

			damageController.Kill(null);
			if (!target.IsFlagged(CharacterFlags.IsDead))
			{
				Reply(character, $"{target.CharacterName} could not be killed.");
				return;
			}

			LogCharacterAction(character, target, "killed");
			Reply(character, $"Killed {target.CharacterName}.");
		}

		/// <summary>Toggles immortality.</summary>
		/// <remarks>
		/// In memory only, and the acknowledgement says how long it lasts: the teleport path sets
		/// immortality for the transfer and the load path clears it, so a scene change or a fresh
		/// load ends it. That is the right lifetime for a moderation tool — immortality that
		/// persisted would be one forgotten command away from a permanently unkillable account.
		/// </remarks>
		private void ToggleImmortality(IPlayerCharacter character, string arguments)
		{
			if (!TryResolveOptionalTarget(character, arguments, out IPlayerCharacter target, out _) ||
				!TryGetDamageController(character, target, out ICharacterDamageController damageController))
			{
				return;
			}

			damageController.Immortal = !damageController.Immortal;
			LogCharacterAction(character, target, damageController.Immortal ? "made immortal" : "made mortal");
			Reply(character, damageController.Immortal
				? $"{target.CharacterName} is immortal until they next change scene or load."
				: $"{target.CharacterName} is mortal again.");
		}

		/// <summary>Shows one attribute.</summary>
		private void ReportAttribute(IPlayerCharacter character, string arguments)
		{
			string name = OperatorCommandParsing.SplitFirstWord(arguments, out string attributeText);
			if (name.Length == 0 || attributeText.Length == 0)
			{
				ReplyUsage(character, adminCommands, "attr");
				return;
			}
			/* Reads the target's attribute and changes nothing, so it is gated like the other
			 * read-only reports rather than by rank — see ReportWhere and ReportCharacterInfo. */
			if (!TryResolveTarget(character, name, out IPlayerCharacter target, StaffTargetRank.SkipOutranks) ||
				!TryFindAttributeTemplate(character, attributeText, out CharacterAttributeTemplate template) ||
				!target.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}

			if (attributeController.TryGetResourceAttribute(template, out CharacterResourceAttribute resource))
			{
				Reply(character, $"{target.CharacterName} {template.Name}: current {resource.CurrentValue.ToString("0.#", CultureInfo.InvariantCulture)} of {resource.FinalValue} (base {resource.Value}).");
				return;
			}
			if (attributeController.TryGetAttribute(template, out CharacterAttribute attribute))
			{
				Reply(character, $"{target.CharacterName} {template.Name}: {attribute.FinalValue} (base {attribute.Value}).");
				return;
			}
			Reply(character, $"{target.CharacterName} has no {template.Name}.");
		}

		/// <summary>Sets an attribute's base value and persists it.</summary>
		/// <remarks>
		/// Refuses the currency attribute. Setting a balance here would skip the economy ledger, and a
		/// balance that moved with no ledger row is exactly the unexplained change the ledger exists
		/// to rule out; setgold writes both.
		/// </remarks>
		private void SetAttribute(IPlayerCharacter character, string arguments)
		{
			if (!TryResolveOptionalTarget(character, arguments, out IPlayerCharacter target, out string rest))
			{
				return;
			}

			string valueText = OperatorCommandParsing.SplitFirstWord(rest, out string attributeText);
			if (!int.TryParse(valueText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ||
				attributeText.Length == 0)
			{
				ReplyUsage(character, adminCommands, "setattr");
				return;
			}
			if (!TryFindAttributeTemplate(character, attributeText, out CharacterAttributeTemplate template))
			{
				return;
			}
			if (currencyTemplate != null && template.ID == currencyTemplate.ID)
			{
				Reply(character, $"{template.Name} is currency; use /admin setgold so the economy ledger records it.");
				return;
			}
			if (!target.TryGet(out ICharacterAttributeController attributeController))
			{
				Reply(character, $"{target.CharacterName} has no attributes.");
				return;
			}

			CharacterAttribute attribute;
			float currentValue = 0f;
			if (attributeController.TryGetResourceAttribute(template, out CharacterResourceAttribute resource))
			{
				attribute = resource;
			}
			else if (!attributeController.TryGetAttribute(template, out attribute))
			{
				Reply(character, $"{target.CharacterName} has no {template.Name}.");
				return;
			}

			int before = attribute.Value;
			attribute.SetValue(value);
			if (resource != null)
			{
				currentValue = resource.CurrentValue;
			}
			bool queued = TryPersistOperatorAttribute(target, template.ID, attribute, currentValue);

			Log.Warning("SceneServerSystem",
				$"Administrator '{character.Account}' set '{target.CharacterName}' (id {target.ID}) {template.Name} from {before} to {value}.");

			Reply(character, $"{target.CharacterName} {template.Name}: base {before} to {value}, final {attribute.FinalValue}." +
				(queued ? string.Empty : " The save could not be queued; the next periodic save writes it."));
		}

		/// <summary>Resolves the damage controller of a target, answering the caller when it has none.</summary>
		private bool TryGetDamageController(IPlayerCharacter character, IPlayerCharacter target, out ICharacterDamageController damageController)
		{
			if (target.TryGet(out damageController))
			{
				return true;
			}
			Reply(character, $"{target.CharacterName} cannot take damage or be healed.");
			return false;
		}

		/// <summary>Finds an attribute template by id or by name.</summary>
		private bool TryFindAttributeTemplate(IPlayerCharacter character, string text, out CharacterAttributeTemplate template)
		{
			template = null;
			text = text.Trim();

			Dictionary<int, CharacterAttributeTemplate> cache = CharacterAttributeTemplate.GetCache<CharacterAttributeTemplate>();
			if (cache == null || cache.Count == 0)
			{
				Reply(character, "No attribute templates are loaded on this scene server.");
				return false;
			}

			if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int id) &&
				cache.TryGetValue(id, out template) && template != null)
			{
				return true;
			}

			template = cache.Values.FirstOrDefault(t => t != null &&
				(string.Equals(t.name, text, StringComparison.OrdinalIgnoreCase) ||
				 string.Equals(t.Name, text, StringComparison.OrdinalIgnoreCase)));
			if (template != null)
			{
				return true;
			}

			List<string> near = cache.Values
				.Where(t => t != null && t.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
				.Select(t => t.name)
				.OrderBy(n => n.Length)
				.Take(4)
				.ToList();
			Reply(character, near.Count > 0
				? $"No attribute named '{OperatorCommandParsing.Truncate(text, 32)}'. Did you mean: {string.Join(", ", near)}?"
				: $"No attribute named '{OperatorCommandParsing.Truncate(text, 32)}'.");
			return false;
		}

		/// <summary>Logs a character-state action with who did it to whom.</summary>
		private static void LogCharacterAction(IPlayerCharacter actor, IPlayerCharacter target, string verb)
		{
			Log.Warning("SceneServerSystem",
				$"Administrator '{actor.Account}' {verb} '{target.CharacterName}' (id {target.ID}) in '{target.CurrentSceneName()}'.");
		}
	}
}
