using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Ability crafting: validates crafting requests, learns crafted abilities, and persists them to the database.
	/// </summary>
	public partial class InteractableSystem
	{
		/// <summary>
		/// Answers a craft request, whether it was accepted or refused.
		/// </summary>
		/// <remarks>
		/// Every exit from <see cref="OnServerAbilityCraftBroadcastReceived"/> goes through here.
		/// The handler used to answer a refusal with silence, which the crafting panel could only
		/// resolve by letting its submit lock time out — indistinguishable from a server that never
		/// replied, and the player is told nothing about why the craft did not happen. This is the
		/// same contract the merchant purchase path keeps with
		/// <c>MerchantPurchaseResultBroadcast</c>.
		/// </remarks>
		/// <param name="conn">Connection that made the request.</param>
		/// <param name="templateID">Template the request named.</param>
		/// <param name="failure">Why it was refused, or <see cref="AbilityCraftFailure.None"/>.</param>
		/// <param name="charged">Currency actually taken.</param>
		private static void SendCraftResult(NetworkConnection conn, int templateID,
			AbilityCraftFailure failure, long charged = 0)
		{
			if (conn == null)
			{
				return;
			}

			conn.Broadcast(new AbilityCraftResultBroadcast()
			{
				TemplateID = templateID,
				Success = failure == AbilityCraftFailure.None,
				Failure = failure,
				Charged = charged,
			});
		}

		/// <summary>
		/// Handles an incoming ability crafting request and validates cost, ownership, and selected events.
		/// </summary>
		/// <param name="conn">Requesting client connection.</param>
		/// <param name="msg">Ability crafting request payload.</param>
		/// <param name="channel">Transport channel used by FishNet.</param>
		public void OnServerAbilityCraftBroadcastReceived(NetworkConnection conn, AbilityCraftBroadcast msg, Channel channel)
		{
			if (conn == null)
			{
				return;
			}

			// validate connection character
			if (conn.FirstObject == null)
			{
				SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.Unavailable);
				return;
			}
			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();

			if (character == null ||
				!character.TryGet(out IAbilityController abilityController))
			{
				SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.Unavailable);
				return;
			}

			/* Resolved here rather than at the grant, so a server that cannot record a crafted
			 * ability refuses before the player is charged rather than after. */
			if (!Server.BehaviourRegistry.TryGet(out IAbilitySystem abilitySystem))
			{
				Log.Error("InteractableSystem", "OnServerAbilityCraftBroadcastReceived: IAbilitySystem is not registered; refusing every craft.");
				SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.Unavailable);
				return;
			}

			/* Its own reason. "You cannot do that right now" and "that crafter is gone" send the
			 * player to different places, and CanAct is the gate a dead or stunned character
			 * actually meets. */
			if (!CharacterStateValidation.CanAct(character))
			{
				SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.CannotAct);
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.Busy);
				return;
			}

			/* Set once the grant has taken the guard. From that point it is never this method's to
			 * release: an accepted grant releases it when its write lands, and a refused one has
			 * already been released by TryGrantAbility, which owns it on every path. Every refusal
			 * above leaves this false, which is what the finally is for. */
			bool guardHandedOff = false;
			try
			{

				// validate main ability exists
				AbilityTemplate mainAbility = AbilityTemplate.Get<AbilityTemplate>(msg.TemplateID);
				if (mainAbility == null)
				{
					SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.InvalidEntry);
					return;
				}

				// Validate the scene the character is actually in — see CurrentSceneName.
				string currentScene = character.CurrentSceneName();
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(currentScene, out _))
				{
					Log.Debug("InteractableSystem", "Missing Scene:" + currentScene);
					SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.Unavailable);
					return;
				}

				// validate scene object
				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
					SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.Unavailable);
					return;
				}

				/* The interactable must actually BE an ability crafter, and must accept the
				 * interaction.
				 *
				 * Neither was checked. The old pair asked only "is there an IInteractable here and
				 * am I in range of it", so any interactable in the world served as a crafting
				 * bench — a mailbox, a bindstone, a lore tablet, a corpse. The IAbilityCrafter cast
				 * further down existed solely to find an achievement template, so a request naming
				 * the wrong object crafted the ability anyway and merely skipped the achievement.
				 *
				 * CanInteract rather than InRange for the same reason as the merchant paths: it is
				 * where the corpse gate lives, and without it the crafter's body kept taking
				 * orders after the crafter was killed. */
				IInteractable interactable = InteractableResolver.Resolve(sceneObject);
				IAbilityCrafter abilityCrafter = interactable as IAbilityCrafter;
				if (abilityCrafter == null ||
					!interactable.CanInteract(character))
				{
					SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.Unavailable);
					return;
				}

				/* Split into three answers rather than one silent refusal. They are three
				 * different situations with three different remedies — buy the template, forget
				 * the ability you already crafted from it, or make room — and a player who is told
				 * nothing cannot tell which one they are in. */
				if (!abilityController.KnowsAbility(mainAbility.ID))
				{
					SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.NotKnown);
					return;
				}
				if (abilityController.KnowsLearnedAbility(mainAbility.ID))
				{
					SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.AlreadyCrafted);
					return;
				}
				if (abilityController.KnownAbilities.Count >= maxAbilityCount)
				{
					SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.AbilityLimit);
					return;
				}

				int price = mainAbility.Price;

				// validate eventIds if there are any...
				if (msg.Events != null)
				{
					// Defense-in-depth: cap event list size to prevent processing oversized payloads.
					if (msg.Events.Length > maxAbilityCraftEvents)
					{
						SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.InvalidEvents);
						return;
					}

					/* SERVER-AUTHORITATIVE SLOT LIMIT.
					 *
					 * The per-ability limit lives on the template as AdditionalEventSlots, and it
					 * was applied ONLY by the client (UITKAbilityCraft renders exactly that many
					 * event slots). The server checked nothing but the global payload cap, so a
					 * crafted AbilityCraftBroadcast could attach up to maxAbilityCraftEvents (32)
					 * events to an ability whose template allows zero — a permanent, persisted
					 * ability with 32 events' worth of aggregated damage, range and lifetime for
					 * the price of the events alone.
					 *
					 * maxAbilityCraftEvents stays as the payload cap; this is the game rule. */
					if (msg.Events.Length > mainAbility.AdditionalEventSlots)
					{
						Log.Debug("InteractableSystem",
							$"AbilityCraft: rejected {msg.Events.Length} events for template {mainAbility.ID} which allows {mainAbility.AdditionalEventSlots}.");
						SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.InvalidEvents);
						return;
					}

					HashSet<int> validatedEvents = new HashSet<int>();
					bool hasTypeOverride = false;
					for (int i = 0; i < msg.Events.Length; ++i)
					{
						int id = msg.Events[i];
						if (validatedEvents.Contains(id))
						{
							// duplicate events
							SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.InvalidEvents);
							return;
						}
						validatedEvents.Add(id);

						/* The character must know the entry regardless of which cache it lives
						 * in. This check is what keeps the branch below from becoming a way to
						 * inject templates the player never learned. */
						if (!abilityController.KnowsAbilityEvent(id))
						{
							SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.InvalidEvents);
							return;
						}

						AbilityEvent abilityEvent = AbilityEvent.Get<AbilityEvent>(id);
						if (abilityEvent != null)
						{
							price += abilityEvent.Price;
							continue;
						}

						/* Ability.Initialize also accepts an AbilityTypeOverrideEventType id here.
						 * It extends BaseAbilityTemplate rather than AbilityEvent, so it lives in
						 * a different cache and AbilityEvent.Get returns null for it. Resolve it
						 * explicitly, and allow AT MOST ONE: Ability.TypeOverride is a single
						 * field, so a second one silently wins and which one wins depends on
						 * payload order — a request the server should refuse outright rather than
						 * resolve arbitrarily. */
						BaseAbilityTemplate baseTemplate = BaseAbilityTemplate.Get<BaseAbilityTemplate>(id);
						if (baseTemplate is AbilityTypeOverrideEventType typeOverride)
						{
							if (hasTypeOverride)
							{
								Log.Debug("InteractableSystem", "AbilityCraft: rejected multiple ability-type override events.");
								SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.InvalidEvents);
								return;
							}
							hasTypeOverride = true;
							price += typeOverride.Price;
							continue;
						}

						// unknown ability event
						SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.InvalidEvents);
						return;
					}
				}

				// do we have enough currency to purchase this?
				if (currencyTemplate == null)
				{
					Log.Debug("InteractableSystem", "currencyTemplate is null.");
					SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.Unavailable);
					return;
				}
				/* Value, not FinalValue.
				 *
				 * AddValue below writes the BASE value; FinalValue is the base plus every modifier
				 * in force. Testing one and writing the other let a character carrying any
				 * currency-boosting buff craft against currency it did not have and go negative by
				 * exactly the size of the buff. The merchant purchase and ability-learning paths
				 * were already fixed for this; the crafting path was not. */
				if (!CharacterCurrency.TryGetBalance(character, currencyTemplate, out long balance) ||
					balance < price)
				{
					SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.InsufficientFunds);
					return;
				}

				/* The charge, taken by AbilitySystem on the main thread once the row exists and the
				 * crafter is still here — the same shape the merchant purchase path uses, and for the
				 * same reason. Charging before the grant, as this used to, left a real loss: a write
				 * that failed after the player had transferred or logged out could not be refunded,
				 * and the database held the deduction with no row to show for it. Settling on
				 * completion means nothing moves until both the row and the payer are there, and a
				 * grant that never lands never cost anything.
				 *
				 * TrySpend still owns the deduct-then-persist ordering and the in-memory refund when
				 * the attribute write is refused. The deduction used to be in-memory only: nothing on
				 * this path ever enqueued a character attribute write, so the price of a crafted
				 * ability survived only until the next periodic character save.
				 *
				 * Guarded on a positive price because TrySpend rejects a non-positive amount by
				 * contract — a free craft is not a spend, and calling it anyway would refuse every
				 * craft in the game: every ability and ability-event template ships at Price 0. A free
				 * craft therefore skips the persist as well; with nothing deducted there is nothing
				 * to write, so a refused write is no reason to refuse the ability.
				 *
				 * A refusal here is answered here — the balance moved between the affordability check
				 * above and now — and AbilitySystem revokes the row it was settling. */
				bool SettleCraft(IPlayerCharacter payer)
				{
					if (price <= 0 ||
						CharacterCurrency.TrySpend(payer, currencyTemplate, price, () => TryPersistMerchantAttributes(payer)))
					{
						return true;
					}
					Log.Warning("InteractableSystem", $"AbilityCraft: charge of {price} refused for CharID={payer.ID}.");
					SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.InsufficientFunds);
					return false;
				}

				/* Answers a grant that was refused before it was attempted or failed when it was
				 * written. Nothing to refund: the charge is settled only on a grant that landed. */
				void AnswerRejectedCraft(IPlayerCharacter _)
				{
					SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.PersistFailed);
				}

				/* The grant is handed to AbilitySystem rather than performed here.
				 *
				 * LearnAbility used to build the ability, learn it, and persist it fire-and-forget —
				 * so the ability entered KnownAbilities keyed on the -1 the constructor assigns, and
				 * the id the database returned was discarded. Every crafted ability in a session
				 * therefore landed under the same key and each new one replaced the last. The id is
				 * applied by the system before the learn; AbilitySystem records why it cannot be
				 * corrected afterwards the way the item layer corrects a slot.
				 *
				 * The charge, the success answer, the broadcasts and the achievement all move into
				 * the continuations, because a grant is now asynchronous where it was not: the
				 * ability exists only once its row does. The player is charged strictly after the
				 * row is persisted and shown the ability strictly after they are charged. */
				bool accepted = abilitySystem.TryGrantAbility(
					character,
					mainAbility,
					msg.Events,
					settle: SettleCraft,
					releaseGuard: () => EndIngressGuard(guardKey),
					onGranted: (grantedCharacter, _) =>
					{
						RecordCurrencyMovement(grantedCharacter.ID, price, CurrencyMovementReason.AbilityCraft, absorbed: true);

						/* The success answer. AbilityAddBroadcast, which is what grants the ability
						 * and is handled by the ability controller, has already gone out by the time
						 * this runs; this is what the crafting panel reads to release its submit lock
						 * and say what the craft cost. */
						SendCraftResult(conn, msg.TemplateID, AbilityCraftFailure.None, price);

						// Increment achievement for crafting an ability
						if (abilityCrafter.AchievementTemplate != null &&
							grantedCharacter.TryGet(out IAchievementController achievementController))
						{
							achievementController.Increment(abilityCrafter.AchievementTemplate, 1);
						}
					},
					onFailed: AnswerRejectedCraft);

				guardHandedOff = true;

				if (!accepted)
				{
					AnswerRejectedCraft(character);
				}
			}
			finally
			{
				if (!guardHandedOff)
				{
					EndIngressGuard(guardKey);
				}
			}
		}
	}
}