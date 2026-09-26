using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// <c>/admin</c> economy: reading, setting, granting and taking currency, and granting items.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Administrator only, and never to be moved under <c>/gm</c>.</b> Every command here creates
	/// or destroys value, which is the most exploitable thing an operator account can do; a test
	/// pins that no game master file calls anything declared in an administrator file.
	/// </para>
	/// <para>
	/// <b>Online characters on this scene server only.</b> The change goes through the live
	/// controllers and the ordinary write paths — the attribute save the merchant uses, the item
	/// grant quests and achievements use — so the player sees it at once and it persists the same way
	/// as any other change. Editing an offline character's rows would race whichever server next
	/// loads them.
	/// </para>
	/// <para>
	/// <b>Currency movements are written to the economy ledger</b> as
	/// <see cref="CurrencyMovementReason.AdminAdjustment"/>, beside the audit row the gate writes, so
	/// an economy investigation finds the adjustment where it looks for balance changes.
	/// </para>
	/// </remarks>
	public partial class SceneServerSystem
	{
		/// <summary>Most stacks a single <c>/admin giveitem</c> may create.</summary>
		private const uint MaxGrantedItemStacks = 20;

		[Header("Operator Commands")]
		[Tooltip("The currency attribute /admin gold, setgold, givegold and takegold act on. Use the same template the trade and merchant systems use.")]
		[SerializeField]
		private CharacterAttributeTemplate currencyTemplate;

		/// <summary>Which way a currency command moves the balance.</summary>
		private enum CurrencyChange
		{
			Set,
			Give,
			Take,
		}

		/// <summary>The economy part of the <c>/admin</c> table.</summary>
		private IEnumerable<OperatorCommand> BuildAdminEconomyCommands()
		{
			return new List<OperatorCommand>()
			{
				new OperatorCommand
				{
					Name = "gold", Aliases = new[] { "balance" }, Category = "Economy",
					Summary = "Shows a character's currency balance. Leave the character out for yourself.",
					Arguments = "character:Character?", RosterAction = true,
					Run = ReportCurrency,
				},
				new OperatorCommand
				{
					Name = "setgold", Category = "Economy",
					Summary = "Sets a character's currency balance. Recorded in the economy ledger.",
					Arguments = "character:Character?;amount:Integer", RosterAction = true, Destructive = true,
					Run = (c, a) => ChangeCurrency(c, a, CurrencyChange.Set),
				},
				new OperatorCommand
				{
					Name = "givegold", Category = "Economy",
					Summary = "Adds currency to a character. Recorded in the economy ledger.",
					Arguments = "character:Character?;amount:Integer", RosterAction = true, Destructive = true,
					Run = (c, a) => ChangeCurrency(c, a, CurrencyChange.Give),
				},
				new OperatorCommand
				{
					Name = "takegold", Category = "Economy",
					Summary = "Removes currency from a character, never below zero. Recorded in the economy ledger.",
					Arguments = "character:Character?;amount:Integer", RosterAction = true, Destructive = true,
					Run = (c, a) => ChangeCurrency(c, a, CurrencyChange.Take),
				},
				new OperatorCommand
				{
					Name = "giveitem", Category = "Economy",
					Summary = "Puts items into a character's inventory, by item name or template id.",
					Arguments = "character:Character?;amount:Integer;item:Text", RosterAction = true, Destructive = true,
					Run = GiveItem,
				},
			};
		}

		/// <summary>Shows a character's currency balance.</summary>
		private void ReportCurrency(IPlayerCharacter character, string arguments)
		{
			if (!TryGetCurrencyTemplate(character) ||
				// Reports a balance and changes nothing; read-only, so rank does not apply.
				!TryResolveOptionalTarget(character, arguments, out IPlayerCharacter target, out _, StaffTargetRank.SkipOutranks))
			{
				return;
			}

			if (!CharacterCurrency.TryGetBalance(target, currencyTemplate, out long balance))
			{
				Reply(character, $"{target.CharacterName} has no {currencyTemplate.Name}.");
				return;
			}
			Reply(character, $"{target.CharacterName} has {balance} {currencyTemplate.Name}.");
		}

		/// <summary>Sets, grants or takes currency.</summary>
		/// <remarks>
		/// The balance is the attribute's BASE value, as <see cref="CharacterCurrency"/> reads it. A
		/// buff that raises the final value is not money, and setting the final value would leave the
		/// balance wrong by exactly the buff the moment it expired.
		/// </remarks>
		private void ChangeCurrency(IPlayerCharacter character, string arguments, CurrencyChange change)
		{
			if (!TryGetCurrencyTemplate(character) ||
				!TryResolveOptionalTarget(character, arguments, out IPlayerCharacter target, out string rest))
			{
				return;
			}

			string commandName = change == CurrencyChange.Set ? "setgold" : change == CurrencyChange.Give ? "givegold" : "takegold";
			string amountText = OperatorCommandParsing.SplitFirstWord(rest, out _);
			if (!long.TryParse(amountText, NumberStyles.None, CultureInfo.InvariantCulture, out long amount) ||
				amount > int.MaxValue ||
				(change != CurrencyChange.Set && amount == 0))
			{
				ReplyUsage(character, adminCommands, commandName);
				return;
			}

			if (!target.TryGet(out ICharacterAttributeController attributeController) ||
				!attributeController.TryGetAttribute(currencyTemplate, out CharacterAttribute currency))
			{
				Reply(character, $"{target.CharacterName} has no {currencyTemplate.Name}.");
				return;
			}

			long before = currency.Value;
			long after;
			switch (change)
			{
				case CurrencyChange.Set: after = amount; break;
				case CurrencyChange.Give: after = Math.Min(int.MaxValue, before + amount); break;
				default: after = Math.Max(0, before - amount); break;
			}

			if (after == before)
			{
				Reply(character, $"{target.CharacterName} already has {before} {currencyTemplate.Name}; nothing changed.");
				return;
			}

			/* The write quotes the session claim this server holds for the target, captured before the
			 * balance moves. A resident character always holds one; without it the character is not
			 * ours to change — it is leaving or being evicted — so nothing is changed and the operator
			 * is told, rather than being told of a balance that could never be stored. */
			if (!TryCaptureSessionClaim(target.ID, out _))
			{
				Reply(character, $"{target.CharacterName}'s session is not held by this server any more; nothing changed.");
				return;
			}

			currency.SetValue((int)after);
			bool queued = TryPersistOperatorAttribute(target, currencyTemplate.ID, currency, 0f);

			long delta = after - before;
			RecordOperatorCurrencyMovement(target.ID, Math.Abs(delta), absorbed: delta < 0);

			Log.Warning("SceneServerSystem",
				$"Administrator '{character.Account}' changed '{target.CharacterName}' (id {target.ID}) {currencyTemplate.Name} from {before} to {after}.");

			Reply(character, $"{target.CharacterName}: {before} to {after} {currencyTemplate.Name}." +
				(queued ? string.Empty : " The persistence queue is saturated, so the save waits its turn behind the backlog; it is still written, but may land late."));
			if (target.ID != character.ID)
			{
				Reply(target, $"Staff adjusted your {currencyTemplate.Name}.");
			}
		}

		/// <summary>Grants items by name or template id, in as many stacks as the amount needs.</summary>
		private void GiveItem(IPlayerCharacter character, string arguments)
		{
			if (!TryResolveOptionalTarget(character, arguments, out IPlayerCharacter target, out string rest))
			{
				return;
			}

			string amountText = OperatorCommandParsing.SplitFirstWord(rest, out string itemText);
			if (!uint.TryParse(amountText, NumberStyles.None, CultureInfo.InvariantCulture, out uint amount) ||
				amount == 0 ||
				itemText.Length == 0)
			{
				ReplyUsage(character, adminCommands, "giveitem");
				return;
			}

			if (!TryFindItemTemplate(character, itemText, out BaseItemTemplate template))
			{
				return;
			}

			uint perStack = template.IsStackable ? Math.Max(1u, template.MaxStackSize) : 1u;
			ulong stacks = ((ulong)amount + perStack - 1) / perStack;
			if (stacks > MaxGrantedItemStacks)
			{
				Reply(character, $"That is {stacks} stacks of {template.Name}; one command may grant at most {MaxGrantedItemStacks * perStack}.");
				return;
			}

			if (!Server.BehaviourRegistry.TryGet(out ICharacterInventorySystem inventorySystem))
			{
				Reply(character, "The inventory system is unavailable.");
				return;
			}

			/* One grant per stack, through the same path quest and achievement rewards take, so each
			 * stack is placed, broadcast to the owner and persisted exactly as a reward would be. The
			 * loop stops at the first stack that does not fit and says how much did. */
			uint given = 0;
			while (given < amount)
			{
				uint chunk = Math.Min(perStack, amount - given);
				if (!inventorySystem.TryGrantItem(target, new Item(template, chunk), InventoryType.Inventory))
				{
					break;
				}
				given += chunk;
			}

			if (given == 0)
			{
				Reply(character, $"{template.Name} could not be added to {target.CharacterName}'s inventory; it may be full.");
				return;
			}

			Log.Warning("SceneServerSystem",
				$"Administrator '{character.Account}' granted {given} x '{template.Name}' (template {template.ID}) to '{target.CharacterName}' (id {target.ID}).");

			Reply(character, $"Gave {given} x {template.Name} to {target.CharacterName}." +
				(given < amount ? $" {amount - given} did not fit." : string.Empty));
			if (target.ID != character.ID)
			{
				Reply(target, $"Staff placed {given} x {template.Name} in your inventory.");
			}
		}

		/// <summary>True when a currency template is configured, telling the caller otherwise.</summary>
		private bool TryGetCurrencyTemplate(IPlayerCharacter character)
		{
			if (currencyTemplate != null)
			{
				return true;
			}
			Reply(character, "No currency template is set on the SceneServerSystem asset.");
			return false;
		}

		/// <summary>
		/// Finds an item template by id, then by exact name, and suggests near names when neither matches.
		/// </summary>
		private bool TryFindItemTemplate(IPlayerCharacter character, string text, out BaseItemTemplate template)
		{
			template = null;
			text = text.Trim();

			Dictionary<int, BaseItemTemplate> cache = BaseItemTemplate.GetCache<BaseItemTemplate>();
			if (cache == null || cache.Count == 0)
			{
				Reply(character, "No item templates are loaded on this scene server.");
				return false;
			}

			if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int id) &&
				cache.TryGetValue(id, out template) && template != null)
			{
				return true;
			}

			List<BaseItemTemplate> matches = cache.Values
				.Where(t => t != null && (string.Equals(t.name, text, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(t.Name, text, StringComparison.OrdinalIgnoreCase)))
				.Distinct()
				.ToList();

			if (matches.Count == 1)
			{
				template = matches[0];
				return true;
			}
			if (matches.Count > 1)
			{
				Reply(character, $"'{OperatorCommandParsing.Truncate(text, 32)}' names {matches.Count} items; give a template id: " +
					string.Join(", ", matches.Take(5).Select(m => m.ID.ToString(CultureInfo.InvariantCulture))));
				return false;
			}

			List<string> near = cache.Values
				.Where(t => t != null && t.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
				.Select(t => t.name)
				.OrderBy(n => n.Length)
				.Take(4)
				.ToList();
			Reply(character, near.Count > 0
				? $"No item named '{OperatorCommandParsing.Truncate(text, 32)}'. Did you mean: {string.Join(", ", near)}?"
				: $"No item named '{OperatorCommandParsing.Truncate(text, 32)}'.");
			return false;
		}

		/// <summary>
		/// Queues one attribute row for persistence after an operator changed it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>Version++</c> AND <c>MarkPersistPending</c>, together, as the merchant and guild paths
		/// do. The periodic save clears an attribute's dirty flag only when its confirmation quotes
		/// the version it stamped; a bump without the mark moves the attribute past a version an
		/// in-flight save is waiting on, and it stays dirty for the rest of the session.
		/// </para>
		/// <para>
		/// Ownership-gated: the row quotes the session claim this server holds for the character,
		/// captured here — in the command's own frame, which is the moment the value changed — and
		/// lands only while that claim is still held. A refusal means the target's session moved to
		/// another server (or ended) after the command ran; the adjustment is then lost with the
		/// character's eviction, and the log says so. A command that changes the value first asks for
		/// the claim itself, so it can refuse before anything moves (see <c>ChangeCurrency</c>); a
		/// resident target always holds one, so the no-claim branch below is a backstop.
		/// </para>
		/// </remarks>
		/// <returns>
		/// True when the write was admitted within the persistence queue's threshold; false when it
		/// was admitted over it, behind the backlog, or (the worker not running, i.e. teardown) went
		/// to <c>EnqueuePersistence</c>'s bounded fallback. It is written either way — the replies
		/// used to tell the operator it would wait for the next periodic save (issue #267).
		/// </returns>
		private bool TryPersistOperatorAttribute(IPlayerCharacter character, int templateID, CharacterAttribute attribute, float currentValue)
		{
			if (!TryCaptureSessionClaim(character.ID, out CharacterSessionLeaseData claim))
			{
				Log.Error("SceneServerSystem",
					$"Operator attribute save for CharID={character.ID} (template {templateID}) was not written: this server holds no session claim for the character.");
				// Not a queue problem, so the caller's "the queue is saturated" line would mislead.
				return true;
			}

			attribute.Version++;
			attribute.MarkPersistPending(attribute.Version);

			long characterID = character.ID;
			var dtos = new List<CharacterAttributeData>(1)
			{
				new CharacterAttributeData(
					id: 0,
					version: attribute.Version,
					characterID: characterID,
					templateID: templateID,
					value: attribute.Value,
					currentValue: currentValue),
			};

			return EnqueuePersistence(async () =>
			{
				try
				{
					if (!TryGetDbService(out ICharacterAttributeService attributeService))
					{
						await Log.Error("SceneServerSystem", "Operator attribute save: ICharacterAttributeService is unavailable.");
						return;
					}

					DatabaseResult<BulkWriteResult> result = await attributeService.PersistOwnedAsync(dtos, ClaimsOf(claim));
					await BulkWriteReporting.ReportAsync("SceneServerSystem", "Operator attribute save", result, $"CharID={characterID}");
					if (IsClaimRefusal(result) || (result.IsSuccess && result.Data.Unowned > 0))
					{
						await Log.Error("SceneServerSystem",
							$"Operator attribute save for CharID={characterID} (template {templateID}) was refused: this server no longer holds the character's session. The adjustment is lost with the character.");
					}
				}
				catch (Exception ex)
				{
					await Log.Error("SceneServerSystem", $"Operator attribute save failed (CharID={characterID}): {ex}");
				}
			}, characterID);
		}

		/// <summary>Records an operator's currency adjustment in the economy ledger. Fire-and-forget.</summary>
		private void RecordOperatorCurrencyMovement(long characterID, long amount, bool absorbed)
		{
			if (characterID <= 0 || amount <= 0)
			{
				return;
			}

			CurrencyMovementState state = absorbed ? CurrencyMovementState.Absorbed : CurrencyMovementState.Returned;
			EnqueuePersistence(async () =>
			{
				if (!TryGetDbService(out ICurrencyLedgerService ledgerService))
				{
					await Log.Warning("SceneServerSystem", $"Currency ledger: could not record operator adjustment of {amount} for CharID={characterID}: ICurrencyLedgerService unavailable.");
					return;
				}

				DatabaseResult record = await ledgerService.RecordAsync(characterID, amount, (int)CurrencyMovementReason.AdminAdjustment, (int)state);
				if (!record.IsSuccess)
				{
					await Log.Warning("SceneServerSystem", $"Currency ledger: could not record operator adjustment of {amount} for CharID={characterID}: [{record.ErrorCode}] {record.ErrorMessage}");
				}
			}, characterID);
		}
	}
}
