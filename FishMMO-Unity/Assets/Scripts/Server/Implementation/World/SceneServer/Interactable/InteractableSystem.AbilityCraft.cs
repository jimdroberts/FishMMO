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
				return;
			}
			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			
			if (character == null ||
				!character.TryGet(out IAbilityController abilityController) ||
				!CharacterStateValidation.CanAct(character))
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				return;
			}

			try
			{

				// validate main ability exists
				AbilityTemplate mainAbility = AbilityTemplate.Get<AbilityTemplate>(msg.TemplateID);
				if (mainAbility == null)
				{
					return;
				}

				// Validate the scene the character is actually in — see CurrentSceneName.
				string currentScene = character.CurrentSceneName();
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(currentScene, out _))
				{
					Log.Debug("InteractableSystem", "Missing Scene:" + currentScene);
					return;
				}

				// validate scene object
				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
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
					return;
				}

				// validate the character can learn the ability
				if (!abilityController.KnowsAbility(mainAbility.ID) ||
					abilityController.KnowsLearnedAbility(mainAbility.ID) ||
					abilityController.KnownAbilities.Count >= maxAbilityCount)
				{
					return;
				}

				int price = mainAbility.Price;

				// validate eventIds if there are any...
				if (msg.Events != null)
				{
					// Defense-in-depth: cap event list size to prevent processing oversized payloads.
					if (msg.Events.Length > maxAbilityCraftEvents)
					{
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
							return;
						}
						validatedEvents.Add(id);

						/* The character must know the entry regardless of which cache it lives
						 * in. This check is what keeps the branch below from becoming a way to
						 * inject templates the player never learned. */
						if (!abilityController.KnowsAbilityEvent(id))
						{
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
								return;
							}
							hasTypeOverride = true;
							price += typeOverride.Price;
							continue;
						}

						// unknown ability event
						return;
					}
				}

				// do we have enough currency to purchase this?
				if (currencyTemplate == null)
				{
					Log.Debug("InteractableSystem", "currencyTemplate is null.");
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
					return;
				}

				/* Deduct, persist, then grant — the same ordering the merchant purchase path uses,
				 * and for the same reason. The deduction used to be in-memory only: nothing on this
				 * path ever enqueued a character attribute write, so the price of a crafted ability
				 * survived only until the next periodic character save and was lost outright if the
				 * server went down before it. TrySpend owns that ordering, and the refund when the
				 * write is refused, so it cannot drift from the paths that do the same thing. */
				if (!CharacterCurrency.TrySpend(character, currencyTemplate, price, () => TryPersistMerchantAttributes(character)))
				{
					Log.Warning("InteractableSystem", $"AbilityCraft: charge of {price} refused for CharID={character.ID}.");
					return;
				}

				Ability newAbility = LearnAbility(abilityController, mainAbility, new List<int>(msg.Events));
				if (newAbility == null)
				{
					// Nothing was learned, so put the money back and record the refund.
					CharacterCurrency.TryAdd(character, currencyTemplate, price);
					RecordCurrencyMovement(character.ID, price, CurrencyEscrowReason.AbilityCraft, absorbed: false);
					if (!TryPersistMerchantAttributes(character))
					{
						Log.Error("InteractableSystem", $"AbilityCraft: refund persist rejected for CharID={character.ID}; in-memory balance is correct but the DB holds the deduction.");
					}
					return;
				}

				RecordCurrencyMovement(character.ID, price, CurrencyEscrowReason.AbilityCraft, absorbed: true);

				AbilityAddBroadcast abilityAddBroadcast = new AbilityAddBroadcast()
				{
					ID = newAbility.ID,
					TemplateID = newAbility.Template.ID,
					Events = msg.Events,
				};

				Server.NetworkWrapper.Broadcast(conn, abilityAddBroadcast, true, Channel.Reliable);

				// Increment achievement for crafting an ability
				if (abilityCrafter.AchievementTemplate != null &&
					character.TryGet(out IAchievementController achievementController))
				{
					achievementController.Increment(abilityCrafter.AchievementTemplate, 1);
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Creates and learns a new crafted ability, then schedules asynchronous persistence.
		/// </summary>
		/// <param name="abilityController">Ability controller receiving the new ability.</param>
		/// <param name="abilityTemplate">Base ability template used for creation.</param>
		/// <param name="abilityEvents">Selected ability event identifiers to attach.</param>
		/// <returns>The created ability instance.</returns>
		public Ability LearnAbility(IAbilityController abilityController, AbilityTemplate abilityTemplate, List<int> abilityEvents)
		{
			Ability newAbility = new Ability(abilityTemplate, abilityEvents);

			// Fire-and-forget: persist the ability to the database
			long charID = abilityController.Character.ID;
			newAbility.Version++;
			var abilityData = new CharacterAbilityData(
				id: newAbility.ID,
				version: newAbility.Version,
				characterID: charID,
				templateID: newAbility.Template.ID,
				abilityEvents: abilityEvents,
				cooldown: 0f
			);
			if (!TryEnqueueAsyncWork(() => PersistAbilityAsync(abilityData), charID))
			{
				Log.Warning("InteractableSystem", $"LearnAbility: Async worker rejected learned-ability persist for CharID={charID}, AbilityID={newAbility.ID}.");
				return null;
			}

			abilityController.LearnAbility(newAbility);

			return newAbility;
		}

		/// <summary>
		/// Persists an ability to the database asynchronously.
		/// </summary>
		private async Task PersistAbilityAsync(CharacterAbilityData abilityData)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterAbilityService>(out var abilityService))
				{
					return;
				}

				DatabaseResult<long> result = await abilityService.PersistAsync(abilityData);
				if (!result.IsSuccess)
				{
					await Log.Warning("InteractableSystem", $"PersistAbilityAsync DB error: {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error persisting ability: {ex}");
			}
		}
	}
}