using System;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	public partial class MercenarySystem
	{
		// ── Hire ──────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Hires the Mercenary the player spoke to. Checked here (in range, one at a time, the leader, can pay), then the
		/// seat is taken in the database (forming a party when the player has none), and only once it is held is the fee
		/// taken and the mercenary put at the player's side. A seat that cannot be paid for is given back.
		/// </summary>
		private void OnHire(NetworkConnection conn, MercenaryHireBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request))
			{
				return;
			}
			if (!TryBeginIngressGuard(conn, IngressOperation.Hire, out long guardKey))
			{
				SendResult(request.Character, MercenaryResult.Busy, null);
				return;
			}
			try
			{
				IPlayerCharacter character = request.Character;
				if (!TryGetController(character, out MercenaryController controller))
				{
					return;
				}
				if (controller.RequestInFlight)
				{
					SendResult(character, MercenaryResult.Busy, null);
					return;
				}
				if (!TryGetRecruiter(character, msg.InteractableID, out MercenaryRecruiter recruiter) || recruiter.Kit == null || recruiter.Kit.Prefab == null)
				{
					SendResult(character, MercenaryResult.NotAtRecruiter, null);
					return;
				}
				if (currencyTemplate == null)
				{
					SendResult(character, MercenaryResult.Failed, null);
					return;
				}

				// The Mercenary spoken to is the one hired: someone else's hire of them, still being answered, comes first.
				NPC recruiterNpc = recruiter.GetComponent<NPC>();
				if (recruiterNpc != null && recruitersBeingHired.Contains(recruiterNpc))
				{
					SendResult(character, MercenaryResult.RecruiterTaken, MercenaryRules.CleanName(recruiter.DisplayName));
					return;
				}

				MercenaryKitTemplate kit = recruiter.Kit;
				long cost = Math.Max(0L, kit.HireCost);
				long partyID = PartyOf(character, out PartyRank rank);
				CharacterCurrency.TryGetBalance(character, currencyTemplate, out long balance);
				MercenaryResult check = MercenaryRules.CheckHire(controller.HasMercenary, partyID, rank, balance, cost);
				string name = MercenaryRules.CleanName(recruiter.DisplayName);
				if (check != MercenaryResult.Done)
				{
					SendResult(character, check, name);
					return;
				}

				// The hired one IS this one: its name, its race and its face, and it steps out from where it stands.
				SceneObjectNamer namer = recruiter.GetComponent<SceneObjectNamer>();
				HireTerms terms = new HireTerms()
				{
					Kit = kit,
					Cost = cost,
					Name = name,
					AppearanceSeed = recruiterNpc != null ? recruiterNpc.AppearanceSeed : 0,
					NameState = namer != null ? namer.CaptureName() : default,
					RaceTemplateID = recruiterNpc != null && recruiterNpc.RaceTemplate != null ? recruiterNpc.RaceTemplate.ID : 0,
					Recruiter = recruiterNpc,
					RecruiterID = recruiterNpc != null ? recruiterNpc.ID : 0,
				};
				if (recruiterNpc != null)
				{
					recruitersBeingHired.Add(recruiterNpc);
				}
				BeginSeat(conn, character, controller, partyID, terms);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>What a hire agreed to, carried across the seat's round trip.</summary>
		private sealed class HireTerms
		{
			public MercenaryKitTemplate Kit;
			public long Cost;
			public string Name;
			public int AppearanceSeed;
			public SceneObjectNamer.NameState NameState;
			public int RaceTemplateID;

			/// <summary>The Mercenary hired (an NPC), held for the hire until it is answered; null for a return.</summary>
			public NPC Recruiter;

			/// <summary>Its scene object ID when the hire began: a pooled reuse by then is another NPC, not this one.</summary>
			public long RecruiterID;

			/// <summary>True for a resting mercenary coming back (free, its own name and face).</summary>
			public bool IsReturn;
		}

		/// <summary>
		/// Takes a party seat for a hire or a return, forming a party first when the player has none, then finishes on the
		/// main thread. One request at a time per player.
		/// </summary>
		private void BeginSeat(NetworkConnection conn, IPlayerCharacter character, MercenaryController controller, long partyID, HireTerms terms)
		{
			if (!Server.BehaviourRegistry.TryGet(out IPartySystem<NetworkConnection> partySystem))
			{
				// No party module: a mercenary needs a party seat, so there can be none.
				ReleaseRecruiter(terms);
				SendResult(character, MercenaryResult.Failed, terms.Name);
				return;
			}

			long characterID = character.ID;
			long worldServerID = character.WorldServerID;
			float healthPCT = character.TryGet(out ICharacterAttributeController attributes)
				? attributes.GetHealthResourceAttributeCurrentPercentage()
				: 1f;
			controller.RequestInFlight = true;

			bool queued = TryEnqueueAsyncWork(async () =>
			{
				long seatParty = partyID;
				PartyJoinOutcome outcome = PartyJoinOutcome.Failed;
				try
				{
					if (seatParty <= 0)
					{
						// Hiring forces a party: the player leads a new one, and the mercenary takes the second seat.
						seatParty = await partySystem.TryCreatePartyForInstanceAsync(conn, characterID, worldServerID, healthPCT);
					}
					if (seatParty > 0)
					{
						outcome = await partySystem.TrySeatMercenaryAsync(characterID, seatParty, terms.Kit.ID, terms.Name);
					}
				}
				catch (Exception ex)
				{
					await Log.Error(LOG, $"Seating a mercenary for character {characterID} failed: {ex}");
					outcome = PartyJoinOutcome.Failed;
				}

				long finalParty = seatParty;
				PartyJoinOutcome finalOutcome = outcome;
				if (!TryEnqueueMainThread<IMercenarySystemMainThreadQueueData>(() => FinishSeat(characterID, conn, finalParty, finalOutcome, terms)) &&
					finalOutcome == PartyJoinOutcome.Joined)
				{
					await partySystem.UnseatMercenaryAsync(characterID, finalParty);
				}
			}, characterID);

			if (!queued)
			{
				controller.RequestInFlight = false;
				ReleaseRecruiter(terms);
				SendResult(character, MercenaryResult.Busy, terms.Name);
			}
		}

		/// <summary>Lets other players hire the Mercenary this hire was holding (it was not taken, or it is gone).</summary>
		private void ReleaseRecruiter(HireTerms terms)
		{
			if (terms != null && terms.Recruiter != null)
			{
				recruitersBeingHired.Remove(terms.Recruiter);
			}
		}

		/// <summary>
		/// The Mercenary a hire is taking, when it is still the same one standing where it was spoken to: spawned, alive,
		/// and not a pooled reuse (its scene object ID unchanged).
		/// </summary>
		private static bool TryGetHiredRecruiter(HireTerms terms, out NPC recruiter)
		{
			recruiter = terms != null ? terms.Recruiter : null;
			return recruiter != null && recruiter.NetworkObject != null && recruiter.NetworkObject.IsSpawned &&
				recruiter.ID == terms.RecruiterID && !recruiter.IsFlagged(CharacterFlags.IsDead) && recruiter.GameObject.activeInHierarchy;
		}

		/// <summary>
		/// The seat's answer, on the main thread: refused, the player is told why; held, the fee is taken (a hire), the
		/// mercenary comes out and is saved. A seat the player is no longer here for, or cannot pay for, is freed again.
		/// </summary>
		private void FinishSeat(long characterID, NetworkConnection conn, long partyID, PartyJoinOutcome outcome, HireTerms terms)
		{
			// Decided here, on this frame, whatever the answer: nobody else can be waiting on the hold past it.
			ReleaseRecruiter(terms);
			if (!TryGetResidentCharacter(characterID, out IPlayerCharacter character) || character.Owner != conn ||
				!TryGetController(character, out MercenaryController controller))
			{
				if (outcome == PartyJoinOutcome.Joined)
				{
					FreeSeatLater(characterID, partyID);
				}
				return;
			}
			controller.RequestInFlight = false;

			if (outcome != PartyJoinOutcome.Joined)
			{
				MercenaryResult why = outcome == PartyJoinOutcome.Full ? MercenaryResult.PartyFull
					: outcome == PartyJoinOutcome.Refused ? MercenaryResult.PartyHasMercenary
					: MercenaryResult.Failed;
				SendResult(character, why, terms.Name);
				return;
			}

			if (!terms.IsReturn)
			{
				// Re-checked now that the seat is held: the purse may have moved, or another hire landed first.
				if (controller.HasMercenary)
				{
					FreeSeatLater(characterID, partyID);
					SendResult(character, MercenaryResult.AlreadyHaveOne, terms.Name);
					return;
				}
				if (terms.Cost > 0)
				{
					if (currencyTemplate == null ||
						!CharacterCurrency.TryGetBalance(character, currencyTemplate, out long balance) || balance < terms.Cost ||
						!CharacterCurrency.TrySpend(character, currencyTemplate, terms.Cost, () => TryPersistCharacterAttributes(character)))
					{
						FreeSeatLater(characterID, partyID);
						SendResult(character, MercenaryResult.CannotAfford, terms.Name);
						return;
					}
					RecordCurrencyMovement(characterID, terms.Cost, CurrencyMovementReason.MercenaryHire, absorbed: true);
				}

				controller.AppearanceSeed = terms.AppearanceSeed;
				controller.NameState = terms.NameState;
				controller.RaceTemplateID = terms.RaceTemplateID;
				controller.SavedResources = null;
				controller.SavedBuffs = null;
				controller.SetState(terms.Kit.ID, terms.Name, false, PetStance.Defensive);
			}
			else
			{
				if (!controller.HasMercenary || !controller.Resting)
				{
					FreeSeatLater(characterID, partyID);
					return;
				}
				controller.SetState(controller.KitID, controller.MercenaryName, false, controller.Stance);
			}

			controller.SeatedPartyID = partyID;
			controller.MarkDirty();

			// A hire takes the Mercenary spoken to: it steps out of their post as the one now following the player, and the
			// post's spawner sends a new one there in time. Gone meanwhile (or a return), it arrives at the player's side.
			Vector3? at = null;
			Quaternion facing = Quaternion.identity;
			NPC hired = null;
			if (!terms.IsReturn && TryGetHiredRecruiter(terms, out hired) && hired.GameObject.scene.handle == character.GameObject.scene.handle)
			{
				at = hired.Transform.position;
				facing = hired.Transform.rotation;
			}
			else
			{
				hired = null;
			}

			if (hired != null)
			{
				// Gone first, so the two never stand in one place: through its spawner (a respawn is scheduled), as a
				// corpse that decayed would go.
				hired.ReturnToPool();
			}
			if (SpawnMercenary(character, controller, terms.Kit, at, facing) == null)
			{
				// Kept as hired, resting: the kit's prefab is broken, which an administrator fixes; nothing is lost.
				controller.SeatedPartyID = 0;
				controller.SetState(controller.KitID, controller.MercenaryName, true, controller.Stance);
				FreeSeatLater(characterID, partyID);
				SendResult(character, MercenaryResult.Failed, terms.Name);
			}
			PersistRecord(character, controller);
			SendState(character, controller);
		}

		/// <summary>Frees a seat taken for a hire or return that did not go through.</summary>
		private void FreeSeatLater(long characterID, long partyID)
		{
			if (!Server.BehaviourRegistry.TryGet(out IPartySystem<NetworkConnection> partySystem))
			{
				return;
			}
			EnqueuePersistence(async () => await partySystem.UnseatMercenaryAsync(characterID, partyID), characterID);
		}

		// ── Rest, return, release ─────────────────────────────────────────────────────────

		/// <summary>Sends the mercenary to rest: it leaves the world and frees its party seat, so the party can take a player.</summary>
		private void OnDismiss(NetworkConnection conn, MercenaryDismissBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request))
			{
				return;
			}
			if (!TryBeginIngressGuard(conn, IngressOperation.Dismiss, out long guardKey))
			{
				return;
			}
			try
			{
				IPlayerCharacter character = request.Character;
				if (!TryGetController(character, out MercenaryController controller))
				{
					return;
				}
				if (controller.RequestInFlight)
				{
					SendResult(character, MercenaryResult.Busy, controller.MercenaryName);
					return;
				}
				if (!controller.HasMercenary)
				{
					SendResult(character, MercenaryResult.NoMercenary, null);
					return;
				}
				if (controller.Resting)
				{
					SendResult(character, MercenaryResult.Resting, controller.MercenaryName);
					return;
				}

				long seatedParty = controller.SeatedPartyID;
				controller.SeatedPartyID = 0;
				Rest(character, controller, live.TryGetValue(character.ID, out LiveEntry entry) ? entry.Mercenary : null, notifyLeftParty: false);
				FreeSeatLater(character.ID, seatedParty);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>Brings a resting mercenary back: free, but it needs a seat (the leader's to give), as a hire does.</summary>
		private void OnReturn(NetworkConnection conn, MercenaryReturnBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request))
			{
				return;
			}
			if (!TryBeginIngressGuard(conn, IngressOperation.Return, out long guardKey))
			{
				return;
			}
			try
			{
				IPlayerCharacter character = request.Character;
				if (!TryGetController(character, out MercenaryController controller))
				{
					return;
				}
				if (controller.RequestInFlight)
				{
					SendResult(character, MercenaryResult.Busy, controller.MercenaryName);
					return;
				}
				long partyID = PartyOf(character, out PartyRank rank);
				MercenaryResult check = MercenaryRules.CheckReturn(controller.HasMercenary, controller.Resting, partyID, rank);
				if (check != MercenaryResult.Done)
				{
					SendResult(character, check, controller.MercenaryName);
					return;
				}
				MercenaryKitTemplate kit = controller.Kit;
				if (kit == null || kit.Prefab == null)
				{
					SendResult(character, MercenaryResult.Failed, controller.MercenaryName);
					return;
				}
				BeginSeat(conn, character, controller, partyID, new HireTerms()
				{
					Kit = kit,
					Name = controller.MercenaryName,
					IsReturn = true,
				});
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>Lets the mercenary go for good: its record and its seat go; its gear is the player's and stays.</summary>
		private void OnRelease(NetworkConnection conn, MercenaryReleaseBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request))
			{
				return;
			}
			if (!TryBeginIngressGuard(conn, IngressOperation.Release, out long guardKey))
			{
				return;
			}
			try
			{
				IPlayerCharacter character = request.Character;
				if (!TryGetController(character, out MercenaryController controller))
				{
					return;
				}
				if (controller.RequestInFlight)
				{
					SendResult(character, MercenaryResult.Busy, controller.MercenaryName);
					return;
				}
				if (!controller.HasMercenary)
				{
					SendResult(character, MercenaryResult.NoMercenary, null);
					return;
				}
				if (live.TryGetValue(character.ID, out LiveEntry entry))
				{
					DespawnMercenary(character.ID, entry.Mercenary);
				}
				long seatedParty = controller.SeatedPartyID;
				controller.Clear();
				controller.MarkDirty();
				PersistRelease(character.ID, seatedParty);
				SendState(character, controller);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>Sets when the mercenary fights. Passive also breaks it off whatever it is doing.</summary>
		private void OnStance(NetworkConnection conn, MercenaryStanceBroadcast msg, Channel channel)
		{
			// Gated like a pet's stance: a player who cannot act gives no orders.
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request))
			{
				return;
			}
			if (!Enum.IsDefined(typeof(PetStance), msg.Stance) || !TryBeginIngressGuard(conn, IngressOperation.Stance, out long guardKey))
			{
				return;
			}
			try
			{
				IPlayerCharacter character = request.Character;
				if (!TryGetController(character, out MercenaryController controller) || !controller.HasMercenary)
				{
					return;
				}
				controller.SetState(controller.KitID, controller.MercenaryName, controller.Resting, msg.Stance);
				controller.MarkDirty();
				if (live.TryGetValue(character.ID, out LiveEntry entry) && entry.Mercenary != null)
				{
					entry.Mercenary.Stance = msg.Stance;
					if (msg.Stance == PetStance.Passive)
					{
						Recall(entry.Mercenary);
					}
				}
				SendState(character, controller);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		// ── Writes ────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Writes the player's mercenary now, on the player's persistence lane (in order with their saves). The periodic save
		/// writes it again if it changes; this is what makes a hire, a rest or a return survive a crash a moment later.
		/// </summary>
		private void PersistRecord(IPlayerCharacter character, MercenaryController controller)
		{
			CharacterMercenaryData data = MercenaryRecords.Capture(character.ID, controller,
				live.TryGetValue(character.ID, out LiveEntry entry) ? entry.Mercenary : null,
				Server.NetworkWrapper.NetworkManager.TimeManager);
			if (data == null)
			{
				return;
			}
			long characterID = character.ID;
			int changeCount = controller.ChangeCount;
			EnqueuePersistence(async () =>
			{
				if (!TryGetDbService(out ICharacterMercenaryService service))
				{
					return;
				}
				DatabaseResult result = await service.SaveAsync(data);
				if (!result.IsSuccess)
				{
					await Log.Warning(LOG, $"Saving the mercenary of character {characterID} failed: [{result.ErrorCode}] {result.ErrorMessage}; the next save tries again.");
					return;
				}
				TryEnqueueMainThread<IMercenarySystemMainThreadQueueData>(() => ClearDirtyIfUnchanged(characterID, changeCount));
			}, characterID);
		}

		/// <summary>Deletes the player's mercenary (released or dead) and frees its seat, on the player's lane.</summary>
		private void PersistRelease(long characterID, long seatedPartyID)
		{
			Server.BehaviourRegistry.TryGet(out IPartySystem<NetworkConnection> partySystem);
			int changeCount = TryGetResidentCharacter(characterID, out IPlayerCharacter character) && TryGetController(character, out MercenaryController controller)
				? controller.ChangeCount
				: 0;
			EnqueuePersistence(async () =>
			{
				if (TryGetDbService(out ICharacterMercenaryService service))
				{
					DatabaseResult result = await service.DeleteAsync(characterID);
					if (!result.IsSuccess)
					{
						await Log.Warning(LOG, $"Deleting the mercenary of character {characterID} failed: [{result.ErrorCode}] {result.ErrorMessage}; the next save tries again.");
					}
					else
					{
						TryEnqueueMainThread<IMercenarySystemMainThreadQueueData>(() => ClearDirtyIfUnchanged(characterID, changeCount));
					}
				}
				if (partySystem != null)
				{
					// Always: a seat may outlive a lost record (a mercenary out when the party was read), and freeing none is success.
					await partySystem.UnseatMercenaryAsync(characterID, seatedPartyID);
				}
			}, characterID);
		}

		private void ClearDirtyIfUnchanged(long characterID, int changeCount)
		{
			if (TryGetResidentCharacter(characterID, out IPlayerCharacter character) && TryGetController(character, out MercenaryController controller) &&
				controller.ChangeCount == changeCount)
			{
				controller.PersistenceDirty = false;
			}
		}

		// ── The Mercenary spoken to ───────────────────────────────────────────────────────

		/// <summary>The Mercenary <paramref name="interactableID"/>, when it is here, in the player's scene and in reach.</summary>
		private static bool TryGetRecruiter(IPlayerCharacter character, long interactableID, out MercenaryRecruiter recruiter)
		{
			recruiter = null;
			if (character == null || !SceneObject.Objects.TryGetValue(interactableID, out ISceneObject sceneObject))
			{
				return false;
			}
			GameObject gameObject = sceneObject.GameObject;
			if (gameObject == null || !gameObject.activeInHierarchy || gameObject.scene.handle != character.GameObject.scene.handle)
			{
				return false;
			}
			IInteractable interactable = InteractableResolver.Resolve(sceneObject);
			recruiter = interactable as MercenaryRecruiter;
			return recruiter != null && recruiter.CanInteract(character);
		}
	}
}
