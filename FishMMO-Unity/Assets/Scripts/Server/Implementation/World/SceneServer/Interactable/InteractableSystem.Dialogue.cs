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
using System.Threading.Tasks;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Server-authoritative dialogue session management.
	/// Tracks active dialogue sessions per character, validates client choices,
	/// evaluates ECA conditions/actions, and maintains per-character choice bitmasks.
	/// All dictionaries are bounded to prevent unbounded memory growth.
	/// </summary>
	public partial class InteractableSystem
	{
		/// <summary>
		/// Maximum concurrent dialogue sessions. Prevents unbounded dictionary growth if sessions are never ended.
		/// </summary>
		private const int MaxActiveDialogueSessions = 2048;

		/// <summary>
		/// Maximum number of characters with cached dialogue choices. Prevents unbounded memory growth.
		/// </summary>
		private const int MaxCachedChoiceCharacters = 4096;
		/// <summary>
		/// Represents an active dialogue session for a single character.
		/// </summary>
		private class DialogueSession
		{
			/// <summary>The scene object ID of the dialogue interactable (0 for ECA-triggered).</summary>
			public long InteractableID;
			/// <summary>The CachedScriptableObject ID of the DialogueTemplate.</summary>
			public int TemplateID;
			/// <summary>The node the character is currently viewing.</summary>
			public int CurrentNodeId;
			/// <summary>Bitmask of choices made during this session.</summary>
			public short ChoicesMade;
		}

		/// <summary>
		/// Active dialogue sessions keyed by character ID.
		/// </summary>
		private readonly Dictionary<long, DialogueSession> activeDialogueSessions = new Dictionary<long, DialogueSession>();

		/// <summary>
		/// Choice index meaning "leave the conversation" rather than "take choice N".
		/// </summary>
		/// <remarks>
		/// A dialogue node whose choices are all filtered out — every one already taken, or every
		/// one's conditions unmet — leaves the client with no buttons at all, and the panel has no
		/// close of its own. Escaping out of it left the session open on the server, so the next
		/// interaction with anything was validated against a node the player had walked away from.
		/// This gives the client a way to say so. It reuses
		/// <see cref="DialogueChoiceBroadcast"/> rather than adding a message, because everything
		/// the leave path needs to validate — the session, the node, the range — is already
		/// carried on it.
		/// </remarks>
		public const int DialogueLeaveChoiceIndex = -1;

		/// <summary>
		/// Persistent per-character choice bitmasks keyed by character ID → template ID → bitmask.
		/// Only populated for templates with <see cref="DialogueTemplate.CacheDialogueChoices"/> enabled.
		/// </summary>
		/// <remarks>
		/// This is a <b>write-through cache over <c>character_dialogue_choices</c></b>, not the
		/// system of record. It is hydrated from the database when the character finishes loading
		/// and every bit set into it is merged back to the database immediately, so a restart, a
		/// transfer to another scene server, a logout or an eviction no longer hand a one-time
		/// dialogue reward back to the player.
		/// <para>
		/// A character is only usable for a caching template once <see cref="dialogueChoicesLoaded"/>
		/// contains it. Serving a dialogue from an unhydrated cache would read an empty mask, which
		/// is indistinguishable from "has taken nothing" — that is the exploit, so the interaction
		/// is refused instead and the fetch retried.
		/// </para>
		/// <para>
		/// Eviction only ever takes an entry belonging to a character that has already
		/// disconnected, and entries are dropped on disconnect anyway, so the sweep normally has
		/// nothing left to do.
		/// </para>
		/// </remarks>
		private readonly Dictionary<long, Dictionary<int, short>> characterDialogueChoices = new Dictionary<long, Dictionary<int, short>>();

		/// <summary>
		/// Characters whose dialogue choice masks have been hydrated from the database and are
		/// therefore authoritative in <see cref="characterDialogueChoices"/>.
		/// </summary>
		/// <remarks>
		/// Kept separate from <see cref="characterDialogueChoices"/> because a character with no
		/// stored masks has no entry there, and "loaded, nothing stored" and "not loaded yet" must
		/// not read the same — the first is safe to serve, the second lets a one-time reward be
		/// taken twice.
		/// </remarks>
		private readonly HashSet<long> dialogueChoicesLoaded = new HashSet<long>();

		/// <summary>
		/// Characters whose dialogue choice fetch is currently in flight, and the unscaled time it
		/// started at.
		/// </summary>
		/// <remarks>
		/// Stops a player who keeps clicking an NPC while the database is slow or down from
		/// queueing one fetch per click. The timestamp is what makes it self-healing: the worker
		/// thread must never touch this dictionary, so an entry whose hand-off back to the main
		/// thread was lost would otherwise block that character's dialogue for the rest of the
		/// session. An entry older than <see cref="DialogueChoiceLoadTimeoutSeconds"/> is treated
		/// as abandoned and a fresh fetch is allowed. Every mutation is main-thread only.
		/// </remarks>
		private readonly Dictionary<long, float> dialogueChoicesLoading = new Dictionary<long, float>();

		/// <summary>
		/// Seconds after which an in-flight dialogue choice fetch is assumed lost and may be retried.
		/// </summary>
		private const float DialogueChoiceLoadTimeoutSeconds = 30.0f;

		/// <summary>
		/// Character IDs currently connected to this scene server, as far as the dialogue cache is
		/// concerned. An entry here makes a character's cached choices ineligible for eviction.
		/// </summary>
		private readonly HashSet<long> connectedDialogueCharacters = new HashSet<long>();

		/// <summary>
		/// Marks a character as connected, so its cached dialogue choices cannot be evicted.
		/// </summary>
		/// <param name="characterID">The character that connected.</param>
		internal void RegisterDialogueCharacter(long characterID)
		{
			connectedDialogueCharacters.Add(characterID);
		}

		/// <summary>
		/// Drops a disconnected character's dialogue state.
		/// </summary>
		/// <param name="characterID">The character that disconnected.</param>
		/// <remarks>
		/// Both the session and the cached masks go. Every bit in the cache has already been
		/// merged into <c>character_dialogue_choices</c>, so nothing is lost by dropping it, and
		/// holding it past the disconnect buys only a table that fills up with players who left.
		/// </remarks>
		internal void ReleaseDialogueCharacter(long characterID)
		{
			connectedDialogueCharacters.Remove(characterID);
			activeDialogueSessions.Remove(characterID);
			characterDialogueChoices.Remove(characterID);
			dialogueChoicesLoaded.Remove(characterID);
			/* Deliberately NOT removing from dialogueChoicesLoading. A fetch is still in flight
			 * and its continuation is the only thing that clears the entry; dropping it here would
			 * let a reconnect start a second fetch whose result lands in the same dictionary as
			 * the first. The continuation checks the connected set and discards a result for a
			 * character that has gone. */
		}

		/// <summary>
		/// Records a character as connected once its data has finished loading, and hydrates its
		/// dialogue choice masks from the database.
		/// </summary>
		/// <param name="conn">The character's connection.</param>
		/// <param name="character">The loaded character.</param>
		private void CharacterSystem_OnDialogueCharacterLoaded(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character != null)
			{
				RegisterDialogueCharacter(character.ID);
				TryLoadDialogueChoices(character.ID);
			}
		}

		/// <summary>
		/// Starts an asynchronous fetch of a character's persisted dialogue choice masks.
		/// </summary>
		/// <param name="characterID">The character to hydrate.</param>
		/// <remarks>
		/// Called once when the character finishes loading, and again from
		/// <see cref="StartDialogueSessionInternal"/> whenever a caching dialogue is opened by a
		/// character that is still not hydrated. That second call is what makes a failed or
		/// dropped initial fetch self-healing: a transient database error would otherwise leave the
		/// character unable to use any caching dialogue for the rest of the session, and the only
		/// alternative — serving it from an empty mask — is the repeat-reward exploit itself.
		/// </remarks>
		private void TryLoadDialogueChoices(long characterID)
		{
			if (characterID <= 0 || dialogueChoicesLoaded.Contains(characterID))
			{
				return;
			}

			float now = UnityEngine.Time.unscaledTime;
			if (dialogueChoicesLoading.TryGetValue(characterID, out float startedAt) &&
				now - startedAt < DialogueChoiceLoadTimeoutSeconds)
			{
				return;
			}

			if (Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ICharacterDialogueChoiceService>(out var service))
			{
				Log.Warning("InteractableSystem", $"TryLoadDialogueChoices: ICharacterDialogueChoiceService unavailable; dialogue choice persistence is disabled for CharID={characterID}.");
				return;
			}

			dialogueChoicesLoading[characterID] = now;

			if (!TryEnqueueAsyncWork(() => LoadDialogueChoicesAsync(service, characterID), characterID))
			{
				// The worker refused the work outright, so no continuation will ever run for it.
				dialogueChoicesLoading.Remove(characterID);
			}
		}

		/// <summary>
		/// Fetches a character's dialogue choice masks and installs them on the main thread.
		/// </summary>
		private async Task LoadDialogueChoicesAsync(ICharacterDialogueChoiceService service, long characterID)
		{
			IReadOnlyList<CharacterDialogueChoiceData> rows = null;
			try
			{
				DatabaseResult<IReadOnlyList<CharacterDialogueChoiceData>> result = await service.FetchAsync(characterID);
				if (result.IsSuccess)
				{
					rows = result.Data;
				}
				else
				{
					await Log.Warning("InteractableSystem", $"LoadDialogueChoicesAsync DB error (CharID={characterID}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (System.Exception ex)
			{
				await Log.Error("InteractableSystem", $"LoadDialogueChoicesAsync failed (CharID={characterID}): {ex}");
			}

			/* The dictionaries are main-thread state, so the result has to be handed back rather
			 * than written from the worker. If the queue refuses the hand-off the character simply
			 * stays unhydrated and the next caching dialogue retries — which is the safe direction. */
			if (!TryEnqueueMainThread(() => ApplyLoadedDialogueChoices(characterID, rows)))
			{
				/* The in-flight marker is deliberately not cleared here — it is main-thread state
				 * and this is the worker. Its timestamp expires after
				 * DialogueChoiceLoadTimeoutSeconds and the next caching dialogue retries. */
				await Log.Warning("InteractableSystem", $"LoadDialogueChoicesAsync: main-thread queue rejected the dialogue choice hand-off for CharID={characterID}; it will be retried on the next dialogue.");
			}
		}

		/// <summary>
		/// Installs fetched dialogue choice masks into the cache. Main thread only.
		/// </summary>
		/// <param name="characterID">The character the masks belong to.</param>
		/// <param name="rows">The fetched masks, or null when the fetch failed.</param>
		private void ApplyLoadedDialogueChoices(long characterID, IReadOnlyList<CharacterDialogueChoiceData> rows)
		{
			dialogueChoicesLoading.Remove(characterID);

			if (rows == null)
			{
				// Failed fetch. Left unhydrated on purpose so the next caching dialogue retries
				// rather than being served a mask that says the player has taken nothing.
				return;
			}

			if (!connectedDialogueCharacters.Contains(characterID))
			{
				// The character left while the fetch was in flight. Installing the masks now would
				// re-populate the very table the disconnect hook just cleaned out.
				return;
			}

			if (!characterDialogueChoices.TryGetValue(characterID, out Dictionary<int, short> templateChoices))
			{
				templateChoices = new Dictionary<int, short>();
				characterDialogueChoices[characterID] = templateChoices;
			}

			for (int i = 0; i < rows.Count; ++i)
			{
				CharacterDialogueChoiceData row = rows[i];
				/* OR rather than assign. A choice taken between the fetch starting and this
				 * running is already in the cache and is not in the fetched row; overwriting would
				 * drop it, and the merge on the database side would then never see it again. */
				templateChoices.TryGetValue(row.TemplateID, out short existing);
				templateChoices[row.TemplateID] = (short)((ushort)existing | (ushort)row.Choices);
			}

			dialogueChoicesLoaded.Add(characterID);
		}

		/// <summary>
		/// Releases a disconnected character's dialogue session and cached choices.
		/// </summary>
		/// <param name="conn">The character's connection.</param>
		/// <param name="character">The character that disconnected.</param>
		private void CharacterSystem_OnDialogueCharacterDisconnected(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character != null)
			{
				ReleaseDialogueCharacter(character.ID);
			}
		}

		/// <summary>
		/// Starts a new dialogue session for the character from an interactable interaction.
		/// Evaluates the start node's conditions and sends <see cref="DialogueStartBroadcast"/> to the client.
		/// </summary>
		/// <param name="character">The player character starting the dialogue.</param>
		/// <param name="sceneObject">The scene object hosting the interactable.</param>
		/// <param name="dialogue">The dialogue interactable.</param>
		public void StartDialogueSession(IPlayerCharacter character, ISceneObject sceneObject, IDialogueInteractable dialogue)
		{
			StartDialogueSessionInternal(character, sceneObject != null ? sceneObject.ID : 0, dialogue.Template, dialogue);
		}

		/// <summary>
		/// Starts a new dialogue session triggered by an ECA action (no interactable required).
		/// </summary>
		/// <param name="character">The player character starting the dialogue.</param>
		/// <param name="template">The dialogue template to use.</param>
		public void StartECADialogueSession(IPlayerCharacter character, DialogueTemplate template)
		{
			StartDialogueSessionInternal(character, 0, template, null);
		}

		/// <summary>
		/// Core dialogue session startup. Evaluates start node conditions, creates the session,
		/// and broadcasts <see cref="DialogueStartBroadcast"/> to the client.
		/// </summary>
		private void StartDialogueSessionInternal(IPlayerCharacter character, long interactableID, DialogueTemplate template, IDialogueInteractable dialogue)
		{
			if (character == null || template == null)
			{
				return;
			}

			/* A caching template cannot be served from an unhydrated cache. GetCachedChoices would
			 * return 0, which reads identically to "this character has taken nothing" — so every
			 * one-time choice would be offered again, and this is precisely the repeat-reward
			 * exploit the persistence exists to close. Refuse, kick off (or retry) the fetch, and
			 * let the player's next click through. The window is the length of one indexed
			 * primary-key lookup, and it is almost always already closed by the time a player has
			 * walked from the spawn point to an NPC. */
			if (template.CacheDialogueChoices && !dialogueChoicesLoaded.Contains(character.ID))
			{
				TryLoadDialogueChoices(character.ID);
				Log.Debug("InteractableSystem", $"Dialogue '{template.Name}' caches choices but CharID={character.ID} is not hydrated yet; refusing the interaction rather than serving an empty mask.");
				return;
			}

			/* A caching template promises that each of its choices can be taken once, and the
			 * promise is kept by a bit in a short — sixteen bits, and no more. An author who adds a
			 * seventeenth choice gets no warning: GetChoiceBitIndex simply returns -1 for it and
			 * every choice after it, so there is no bit to set and no bit to test, and the choice's
			 * OnSelectActions — the ones that hand out items and currency — run again on every
			 * click for the rest of time. The per-choice handler now refuses an untrackable choice,
			 * but refusing them one at a time turns a content bug into a dialogue that silently
			 * does nothing halfway through. Refuse the whole conversation instead, loudly, so the
			 * template gets split or the tracking limit gets raised rather than quietly leaking. */
			if (template.CacheDialogueChoices &&
				template.GetTotalChoiceCount() > DialogueTemplate.MaxTrackedChoices)
			{
				Log.Error("InteractableSystem", $"Dialogue '{template.Name}' caches choices but has {template.GetTotalChoiceCount()} of them, and only the first {DialogueTemplate.MaxTrackedChoices} can be tracked. Every choice past that limit would be repeatable without end, so the dialogue is refused. Split the template or reduce its choice count.");
				return;
			}

			// End any existing session for this character
			EndDialogueSession(character);

			// Bounded capacity check
			if (activeDialogueSessions.Count >= MaxActiveDialogueSessions)
			{
				Log.Warning("InteractableSystem", $"Active dialogue sessions at capacity ({MaxActiveDialogueSessions}). Rejecting new session.");
				return;
			}

			DialogueNode startNode = template.GetNode(template.StartNodeId);
			if (startNode == null)
			{
				Log.Warning("InteractableSystem", $"Dialogue template '{template.Name}' has no valid start node ({template.StartNodeId}).");
				return;
			}

			// Evaluate start node conditions
			ICharacter speaker = dialogue != null ? dialogue.Transform.GetComponent<ICharacter>() : null;
			DialogueEventData eventData = new DialogueEventData(
				character,
				speaker,
				template.StartNodeId
			);

			if (!EvaluateConditions(startNode.Conditions, character, eventData))
			{
				Log.Debug("InteractableSystem", $"Start node conditions not met for dialogue '{template.Name}'.");
				return;
			}

			// Execute on-enter actions for the start node
			ExecuteActions(startNode.OnEnterActions, character, eventData);

			// Get cached choices for this character + template
			short cachedChoices = GetCachedChoices(character.ID, template.ID);

			// Create the session
			DialogueSession session = new DialogueSession
			{
				InteractableID = interactableID,
				TemplateID = template.ID,
				CurrentNodeId = template.StartNodeId,
				ChoicesMade = cachedChoices,
			};
			activeDialogueSessions[character.ID] = session;

			// Broadcast to client
			Server.NetworkWrapper.Broadcast(character.Owner, new DialogueStartBroadcast()
			{
				InteractableID = interactableID,
				TemplateID = template.ID,
				StartNodeId = template.StartNodeId,
				CachedChoices = cachedChoices,
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Handles a <see cref="DialogueChoiceBroadcast"/> from the client.
		/// Validates the session, range, node, choice conditions, executes actions, and advances the dialogue.
		/// </summary>
		private void OnServerDialogueChoiceBroadcastReceived(NetworkConnection conn, DialogueChoiceBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();if (character == null)
			{
				return;
			}
			
			if (!CharacterStateValidation.CanAct(character))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				return;
			}

			try
			{
				// Validate session exists
				if (!activeDialogueSessions.TryGetValue(character.ID, out DialogueSession session))
				{
					return;
				}

				// Validate node matches
				if (session.CurrentNodeId != msg.NodeId)
				{
					return;
				}

				// Resolve template
				DialogueTemplate template = DialogueTemplate.Get<DialogueTemplate>(session.TemplateID);
				if (template == null)
				{
					EndDialogueSession(character);
					return;
				}

				// Validate the scene the character is actually in — see CurrentSceneName.
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(character.CurrentSceneName(), out _))
				{
					EndDialogueSessionWithBroadcast(character);
					return;
				}

				// Range check (skip for ECA-triggered sessions with no interactable)
				if (session.InteractableID != 0)
				{
					if (!ValidateSceneObject(session.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
					{
						EndDialogueSessionWithBroadcast(character);
						return;
					}

					IInteractable interactable = sceneObject.GameObject.GetComponent<IInteractable>();
					if (interactable == null || !interactable.InRange(character.Transform))
					{
						EndDialogueSessionWithBroadcast(character);
						return;
					}
				}

				// Resolve current node
				DialogueNode currentNode = template.GetNode(session.CurrentNodeId);
				if (currentNode == null)
				{
					EndDialogueSessionWithBroadcast(character);
					return;
				}

				/* An explicit leave. A node whose choices are all filtered out gives the client no
				 * buttons and no close, and walking away used to leave this session open forever —
				 * every later dialogue message was then validated against a conversation the
				 * player had abandoned. Cached choices are flushed on the way out, exactly as a
				 * normal end-of-dialogue does. */
				if (msg.ChoiceIndex == DialogueLeaveChoiceIndex)
				{
					if (template.CacheDialogueChoices)
					{
						SetCachedChoices(character.ID, template.ID, session.ChoicesMade);
					}
					EndDialogueSessionWithBroadcast(character);
					return;
				}

				/* Choices is a serialized list and an authored node can legitimately have none, so
				 * it can be null. Reading .Count off it threw a NullReferenceException straight
				 * out of a network handler. */
				if (currentNode.Choices == null ||
					msg.ChoiceIndex < 0 ||
					msg.ChoiceIndex >= currentNode.Choices.Count)
				{
					return;
				}

				DialogueChoice choice = currentNode.Choices[msg.ChoiceIndex];
				if (choice == null)
				{
					return;
				}

				// Build event data for condition/action evaluation
				DialogueEventData eventData = new DialogueEventData(
					character,
					null,
					session.CurrentNodeId,
					msg.ChoiceIndex
				);

				// Evaluate choice conditions
				if (!EvaluateConditions(choice.Conditions, character, eventData))
				{
					Log.Debug("InteractableSystem", $"Choice {msg.ChoiceIndex} conditions not met at node {session.CurrentNodeId}.");
					return;
				}

				int bitIndex = template.GetChoiceBitIndex(session.CurrentNodeId, msg.ChoiceIndex);

				/* An untrackable choice on a caching template is refused outright.
				 *
				 * GetChoiceBitIndex returns -1 both for a choice that does not exist and for one
				 * that sits past the sixteenth in the template, where the short mask has run out of
				 * bits. The old code read that -1 as permission: the already-taken test was skipped
				 * because bitIndex was negative, and the bit-recording block below was skipped for
				 * the same reason, so nothing was ever written and nothing was ever checked. Every
				 * choice past the sixteenth on a caching template was therefore repeatable as fast
				 * as the one-second interaction debounce allowed, granting its OnSelectActions —
				 * items, currency — on each pass. StartDialogueSessionInternal now refuses such a
				 * template before the conversation opens; this is the second gate, for a template
				 * edited while a session was already live. */
				if (template.CacheDialogueChoices && bitIndex < 0)
				{
					Log.Warning("InteractableSystem", $"CharID={character.ID} selected choice {msg.ChoiceIndex} at node {session.CurrentNodeId} of caching dialogue '{template.Name}', which has no trackable bit. Refused — an untracked choice on a caching template can be repeated without limit.");
					EndDialogueSessionWithBroadcast(character);
					return;
				}

				bool alreadyTaken = bitIndex >= 0 &&
					(((ushort)session.ChoicesMade) & (ushort)(1 << bitIndex)) != 0;

				/* A one-time choice that has already been taken is refused HERE, on the server.
				 *
				 * This was the hole the persistence work did not actually close. The "already
				 * chosen" test lived only in UITKNPCDialogue, which skips rendering a button whose
				 * bit is set — so the rule was enforced by the client declining to ask. The server
				 * re-set the bit and re-ran OnSelectActions for every DialogueChoiceBroadcast it
				 * received, without ever looking at ChoicesMade. Any client that sent the message
				 * anyway — a patched build, or a replay of a captured packet — collected the
				 * reward again, as many times as it liked, without needing a restart, a transfer
				 * or a cache eviction to launder the state. Persisting the mask across those three
				 * vectors while leaving this one open would have stored the evidence of the
				 * exploit rather than preventing it.
				 *
				 * Silent by design: a legitimate client never sends this, so there is nothing to
				 * tell the player, and the session is left intact so a benign race (the player
				 * clicking twice before DialogueChoiceResultBroadcast lands) does not tear their
				 * conversation down. */
				if (template.CacheDialogueChoices && alreadyTaken)
				{
					Log.Debug("InteractableSystem", $"CharID={character.ID} re-selected an already-taken one-time choice ({msg.ChoiceIndex}) at node {session.CurrentNodeId} of '{template.Name}'. Refused.");
					return;
				}

				/* A choice that leads back to the node it was offered on is refused a second time
				 * within the same conversation, even when the template does not cache choices.
				 *
				 * CacheDialogueChoices defaults to false, and every repeat guard above is written
				 * against it, so a non-caching template had no repeat guard at all. That is fine
				 * for a choice that moves the conversation somewhere else — the player has to walk
				 * the tree back round to reach it again — but a choice whose NextNodeId is its own
				 * node redraws the same buttons the instant its actions finish. Clicking it in
				 * place re-ran OnExitActions, OnSelectActions and the node's OnEnterActions on
				 * every press, so a reward hung on any of the three became a faucet limited only by
				 * how fast the player could click.
				 *
				 * Scoped to the session mask rather than to persistence, because a non-caching
				 * template is explicitly saying its choices are not one-time. Leaving and
				 * re-entering the dialogue starts a fresh mask and offers the choice again, which
				 * is the authored behaviour; what is refused is only the unbounded loop inside one
				 * sitting. */
				if (!template.CacheDialogueChoices &&
					alreadyTaken &&
					choice.NextNodeId == session.CurrentNodeId)
				{
					Log.Debug("InteractableSystem", $"CharID={character.ID} re-selected self-referential choice ({msg.ChoiceIndex}) at node {session.CurrentNodeId} of '{template.Name}'. Refused for the remainder of this session.");
					return;
				}

				/* The bit is recorded and enqueued for persistence BEFORE the choice's actions run.
				 * The two failure directions are not symmetric: if the write lands and the reward
				 * action then fails, the player is out one reward — a transient loss they can
				 * report. If the reward is granted and the write is lost, the choice is offered
				 * again on the next login and the reward repeats indefinitely, which is the
				 * exploit. Ordering it this way makes the crash window fail in the recoverable
				 * direction. */
				if (bitIndex >= 0 && bitIndex < DialogueTemplate.MaxTrackedChoices)
				{
					// Cast to ushort to avoid sign-extension warnings
					session.ChoicesMade = (short)((ushort)session.ChoicesMade | (ushort)(1 << bitIndex));

					/* Persisted per choice rather than only at the end of the conversation. A
					 * disconnect, a crash or an alt-F4 partway through a dialogue would otherwise
					 * discard every bit set so far — and quitting the moment a reward lands is the
					 * cheapest way to farm one. */
					if (template.CacheDialogueChoices)
					{
						SetCachedChoices(character.ID, template.ID, session.ChoicesMade);
					}
				}

				// Execute on-exit actions for the current node
				ExecuteActions(currentNode.OnExitActions, character, eventData);

				// Execute on-select actions for the chosen choice
				ExecuteActions(choice.OnSelectActions, character, eventData);

				// Transition to next node or end dialogue
				if (choice.NextNodeId < 0)
				{
					// Persist choices if caching is enabled
					if (template.CacheDialogueChoices)
					{
						SetCachedChoices(character.ID, template.ID, session.ChoicesMade);
					}

					EndDialogueSessionWithBroadcast(character);
				}
				else
				{
					// Evaluate the next node
					DialogueNode nextNode = template.GetNode(choice.NextNodeId);
					if (nextNode == null)
					{
						if (template.CacheDialogueChoices)
						{
							SetCachedChoices(character.ID, template.ID, session.ChoicesMade);
						}
						EndDialogueSessionWithBroadcast(character);
						return;
					}

					DialogueEventData nextEventData = new DialogueEventData(
						character,
						null,
						choice.NextNodeId
					);

					if (!EvaluateConditions(nextNode.Conditions, character, nextEventData))
					{
						if (template.CacheDialogueChoices)
						{
							SetCachedChoices(character.ID, template.ID, session.ChoicesMade);
						}
						EndDialogueSessionWithBroadcast(character);
						return;
					}

					// Execute on-enter actions for the next node
					ExecuteActions(nextNode.OnEnterActions, character, nextEventData);

					// Advance session
					session.CurrentNodeId = choice.NextNodeId;

					// Send result to client
					Server.NetworkWrapper.Broadcast(character.Owner, new DialogueChoiceResultBroadcast()
					{
						NextNodeId = choice.NextNodeId,
						UpdatedChoices = session.ChoicesMade,
					}, true, Channel.Reliable);
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Ends the dialogue session for the specified character without sending a broadcast.
		/// </summary>
		/// <param name="character">The player character whose session to end.</param>
		public void EndDialogueSession(IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}
			activeDialogueSessions.Remove(character.ID);
		}

		/// <summary>
		/// Ends the dialogue session and sends a <see cref="DialogueEndBroadcast"/> to the client.
		/// </summary>
		private void EndDialogueSessionWithBroadcast(IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}
			activeDialogueSessions.Remove(character.ID);
			Server.NetworkWrapper.Broadcast(character.Owner, new DialogueEndBroadcast(), true, Channel.Reliable);
		}

		/// <summary>
		/// Gets the cached choice bitmask for a character and template combination.
		/// </summary>
		/// <param name="characterId">The character's ID.</param>
		/// <param name="templateId">The dialogue template's CachedScriptableObject ID.</param>
		/// <returns>The bitmask of previously made choices, or 0 if none.</returns>
		private short GetCachedChoices(long characterId, int templateId)
		{
			if (characterDialogueChoices.TryGetValue(characterId, out Dictionary<int, short> templateChoices) &&
				templateChoices.TryGetValue(templateId, out short choices))
			{
				return choices;
			}
			return 0;
		}

		/// <summary>
		/// Stores the cached choice bitmask for a character and template combination.
		/// </summary>
		/// <param name="characterId">The character's ID.</param>
		/// <param name="templateId">The dialogue template's CachedScriptableObject ID.</param>
		/// <param name="choices">The updated choice bitmask.</param>
		/// <remarks>
		/// Write-through: the cache is updated and the mask merged into
		/// <c>character_dialogue_choices</c> in the same call, so there is no window in which the
		/// server believes a one-time choice is spent but the database does not. The database write
		/// is enqueued even when the in-memory cache refuses the entry at capacity — the cache is
		/// an optimisation, the row is the fact.
		/// </remarks>
		private void SetCachedChoices(long characterId, int templateId, short choices)
		{
			PersistCachedChoices(characterId, templateId, choices);

			if (!characterDialogueChoices.TryGetValue(characterId, out Dictionary<int, short> templateChoices))
			{
				/* Bounded capacity check. The old version removed "one arbitrary entry", which is
				 * whichever the dictionary happened to enumerate first — potentially a player
				 * standing in front of the very NPC whose one-time reward they had just taken,
				 * who could then immediately take it again. Only entries belonging to characters
				 * that have already disconnected are eligible; if every entry belongs to a
				 * connected character the new one is refused instead, because losing state for
				 * the character just arriving is strictly less harmful than losing it for one
				 * already playing. */
				if (characterDialogueChoices.Count >= MaxCachedChoiceCharacters &&
					!TryEvictDisconnectedDialogueChoices())
				{
					Log.Warning("InteractableSystem", $"Cached dialogue choices at capacity ({MaxCachedChoiceCharacters}) and every entry belongs to a connected character. Not caching for CharID={characterId}.");
					return;
				}

				templateChoices = new Dictionary<int, short>();
				characterDialogueChoices[characterId] = templateChoices;
			}
			templateChoices[templateId] = choices;
		}

		/// <summary>
		/// Removes one cached entry belonging to a character that is no longer connected.
		/// </summary>
		/// <returns>True when an entry was freed.</returns>
		private bool TryEvictDisconnectedDialogueChoices()
		{
			long victim = 0;
			bool found = false;
			foreach (KeyValuePair<long, Dictionary<int, short>> kvp in characterDialogueChoices)
			{
				if (!connectedDialogueCharacters.Contains(kvp.Key))
				{
					victim = kvp.Key;
					found = true;
					break;
				}
			}

			if (found)
			{
				characterDialogueChoices.Remove(victim);
				/* The hydration marker has to go with the data. Leaving it behind would report the
				 * evicted character as loaded, and the next caching dialogue would then be served
				 * an empty mask as though it were authoritative — the exact failure the marker
				 * exists to prevent. */
				dialogueChoicesLoaded.Remove(victim);
			}
			return found;
		}

		/// <summary>
		/// Merges a choice bitmask into <c>character_dialogue_choices</c>.
		/// </summary>
		/// <param name="characterId">The character's ID.</param>
		/// <param name="templateId">The dialogue template's CachedScriptableObject ID.</param>
		/// <param name="choices">The mask to merge. Bits already stored are retained.</param>
		/// <remarks>
		/// Goes through <c>EnqueuePersistence</c> rather than <c>TryEnqueueAsyncWork</c>: the
		/// in-memory state has already been committed and the choice's reward is about to be
		/// granted, so a silently dropped write is the repeat-reward exploit reopening. The write
		/// itself is an OR-merge and therefore idempotent, which is what makes the queue's
		/// direct-thread-pool fallback safe to use here.
		/// </remarks>
		private void PersistCachedChoices(long characterId, int templateId, short choices)
		{
			if (characterId <= 0)
			{
				return;
			}

			if (Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ICharacterDialogueChoiceService>(out var service))
			{
				Log.Warning("InteractableSystem", $"PersistCachedChoices: ICharacterDialogueChoiceService unavailable; dialogue choices for CharID={characterId} were not persisted.");
				return;
			}

			CharacterDialogueChoiceData dto = new CharacterDialogueChoiceData(characterId, templateId, choices);
			EnqueuePersistence(() => PersistCachedChoicesAsync(service, dto), characterId);
		}

		/// <summary>
		/// Asynchronously merges one dialogue choice bitmask into the database.
		/// </summary>
		private async Task PersistCachedChoicesAsync(ICharacterDialogueChoiceService service, CharacterDialogueChoiceData dto)
		{
			try
			{
				DatabaseResult result = await service.MergeAsync(new[] { dto });
				if (!result.IsSuccess)
				{
					await Log.Warning("InteractableSystem", $"PersistCachedChoicesAsync DB error (CharID={dto.CharacterID}, TemplateID={dto.TemplateID}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (System.Exception ex)
			{
				await Log.Error("InteractableSystem", $"PersistCachedChoicesAsync failed (CharID={dto.CharacterID}, TemplateID={dto.TemplateID}): {ex}");
			}
		}

		/// <summary>
		/// Evaluates a list of ECA conditions. Returns true if all conditions pass or the list is null/empty.
		/// </summary>
		private bool EvaluateConditions(List<BaseCondition> conditions, ICharacter character, EventData eventData)
		{
			if (conditions == null)
			{
				return true;
			}

			for (int i = 0; i < conditions.Count; i++)
			{
				if (conditions[i] != null && !conditions[i].Evaluate(character, eventData))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Executes a list of ECA actions.
		/// </summary>
		private void ExecuteActions(List<BaseAction> actions, ICharacter character, EventData eventData)
		{
			if (actions == null)
			{
				return;
			}

			for (int i = 0; i < actions.Count; i++)
			{
				if (actions[i] != null)
				{
					actions[i].Execute(character, eventData);
				}
			}
		}

		/// <summary>
		/// Handles the <see cref="DisplayDialogueAction.OnServerDialogueRequested"/> static event.
		/// Resolves the initiator as a player character and starts an ECA-triggered dialogue session.
		/// </summary>
		private void OnDisplayDialogueActionRequested(ICharacter initiator, DialogueTemplate template)
		{
			if (initiator == null || template == null)
			{
				return;
			}

			IPlayerCharacter player = initiator as IPlayerCharacter;
			if (player == null)
			{
				return;
			}

			StartECADialogueSession(player, template);
		}
	}
}