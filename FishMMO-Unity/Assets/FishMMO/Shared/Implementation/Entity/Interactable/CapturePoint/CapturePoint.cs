using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Serializing;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Capture point interactable for PvP or general objective capture.
	/// Tracks ownership, capture progress, and objective state.
	/// Fires <see cref="OnCaptured"/> when a player successfully captures the point.
	/// Configured via a <see cref="CapturePointTemplate"/> ScriptableObject asset.
	/// </summary>
	[RequireComponent(typeof(SceneObjectNamer))]
	public class CapturePoint : Interactable, ICapturePoint
	{
		/// <summary>
		/// Template defining capture parameters.
		/// </summary>
		public CapturePointTemplate Template;

		/// <summary>
		/// Achievement to increment when a player captures this point.
		/// </summary>
		public AchievementTemplate AchievementTemplate;

		/// <inheritdoc />
		CapturePointTemplate ICapturePoint.Template => Template;

		/// <inheritdoc />
		AchievementTemplate ICapturePoint.AchievementTemplate => AchievementTemplate;

		/// <summary>
		/// Character ID of the player who currently owns this capture point. 0 means neutral.
		/// </summary>
		public long OwnerCharacterID { get; set; }

		/// <summary>
		/// Current number of capture interactions applied toward the current capture attempt.
		/// </summary>
		public int CaptureProgress { get; set; }

		/// <summary>
		/// Character ID of the player currently attempting to capture this point.
		/// </summary>
		public long CapturingCharacterID { get; set; }

		/// <summary>
		/// Current objective state of this capture point.
		/// </summary>
		public ObjectiveState State { get; set; }

		private string title = "Capture Point";

		/// <summary>
		/// Display title shown above the capture point.
		/// </summary>
		public override string Title { get { return title; } }

		/// <summary>
		/// Title color for the capture point UI label. Uses olive for a distinct PvP aesthetic.
		/// </summary>
		public override Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.olive); } }

		public override void OnAwake()
		{
			base.OnAwake();

			if (Template != null)
			{
				title = Template.Name;
			}
		}

		public override bool CanInteract(IPlayerCharacter character)
		{
			if (Template == null ||
				!base.CanInteract(character))
			{
				return false;
			}

			/* You cannot capture a point you already own — but you CAN interact with one that
			 * somebody is currently taking from you, because interacting is how an owner breaks a
			 * rival's attempt. Refusing the owner outright, which is what this used to do, left
			 * the holder of a point as the one player in the world unable to defend it.
			 *
			 * The test is written against CaptureProgress rather than CapturingCharacterID
			 * because progress is replicated and the captor's ID is not, so this answers the same
			 * on the client as on the server. It needs no ID: a point resets its progress to zero
			 * the moment it is captured, so an owner seeing progress above zero is by definition
			 * watching somebody else's attempt. */
			if (OwnerCharacterID == character.ID &&
				CaptureProgress < 1)
			{
				return false;
			}

			return true;
		}

		/// <summary>
		/// Seconds left before an unfinished capture decays. Server-side.
		/// </summary>
		private float progressDecayTimer;

		/// <summary>
		/// True while this instance is subscribed to the server tick.
		/// </summary>
		private bool subscribedToTick;

		/// <summary>
		/// Registers the scene object and starts the decay clock.
		/// </summary>
		public override void OnStartServer()
		{
			base.OnStartServer();

			if (base.TimeManager != null && !subscribedToTick)
			{
				base.TimeManager.OnTick += CaptureDecayTick;
				subscribedToTick = true;
			}
		}

		/// <summary>
		/// Drops the decay clock when the server stops running this object.
		/// </summary>
		/// <remarks>
		/// <see cref="ResetState"/> covers the pooled despawn, but a capture point is normally
		/// placed in a scene rather than spawned, and a scene unload does not have to go through
		/// the pool. Both routes release through the same flag, so whichever arrives first wins and
		/// the second is a no-op.
		/// </remarks>
		public override void OnStopServer()
		{
			ReleaseDecayTick();

			base.OnStopServer();
		}

		/// <summary>
		/// Unsubscribes from the server tick if this instance holds a subscription.
		/// </summary>
		private void ReleaseDecayTick()
		{
			if (!subscribedToTick)
			{
				return;
			}
			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick -= CaptureDecayTick;
			}
			subscribedToTick = false;
		}

		/// <summary>
		/// Applies one capture interaction toward this point.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>A rival interrupts rather than inherits.</b> The previous version handed the running
		/// progress count to whoever touched the point last: it zeroed the count, then immediately
		/// set <see cref="CapturingCharacterID"/> to the newcomer and incremented, so an enemy
		/// arriving at a point someone else had almost taken simply continued from one. Worse, it
		/// set <see cref="ObjectiveState.Contested"/> and then overwrote it with
		/// <see cref="ObjectiveState.Capturing"/> before returning — the state never came to rest
		/// on Contested, so no observer could ever see a point as contested and the enum member
		/// was unreachable. An interrupting interaction now breaks the capture and stops there;
		/// the rival starts their own attempt on their next interaction.
		/// </para>
		/// <para>
		/// The interrupt branch only fires while there is live progress to break. Once progress
		/// reaches zero — by capture, by decay, or by an interrupt — the captor is cleared too, so
		/// a stale ID cannot make an uncontested point read as contested.
		/// </para>
		/// </remarks>
		/// <param name="characterID">The character attempting to capture.</param>
		/// <returns>True if capture completed, false if still in progress or contested.</returns>
		public bool ApplyCapture(long characterID)
		{
			// Public and on the interface, so it validates rather than trusting its caller.
			if (Template == null || characterID == 0)
			{
				return false;
			}

			// A rival breaks the capture in progress; they do not take it over.
			if (CaptureProgress > 0 &&
				CapturingCharacterID != 0 &&
				CapturingCharacterID != characterID)
			{
				CaptureProgress = 0;
				CapturingCharacterID = 0;

				/* Contested is a resting state with a timer on it, not an instant that is
				 * immediately overwritten. It settles back through the same decay clock an
				 * unfinished capture uses — and when decay is switched off there is nothing to
				 * move it on, so it settles immediately instead of resting Contested for the rest
				 * of the map's life. */
				if (Template.ProgressDecaySeconds > 0.0f)
				{
					progressDecayTimer = Template.ProgressDecaySeconds;
					SetState(ObjectiveState.Contested);
				}
				else
				{
					progressDecayTimer = 0.0f;
					SetState(OwnerCharacterID != 0 ? ObjectiveState.Captured : ObjectiveState.Neutral);
				}

				BroadcastCaptureState();
				return false;
			}

			/* The owner gains nothing by tapping their own point. Above, they were allowed
			 * through to break a rival's attempt; past that branch there is no attempt to break,
			 * so accruing progress here would let an owner re-capture a point they already hold —
			 * firing OnCaptured and the capture achievement once per InteractionsToCapture taps,
			 * indefinitely, on an objective nobody is contesting. */
			if (characterID == OwnerCharacterID)
			{
				return false;
			}

			CapturingCharacterID = characterID;
			CaptureProgress++;
			progressDecayTimer = Template.ProgressDecaySeconds;

			if (CaptureProgress >= Template.InteractionsToCapture)
			{
				OwnerCharacterID = characterID;
				CapturingCharacterID = 0;
				CaptureProgress = 0;
				progressDecayTimer = 0.0f;
				SetState(ObjectiveState.Captured);
				BroadcastCaptureState();
				ICapturePoint.OnCaptured?.Invoke(this, characterID);
				return true;
			}

			SetState(ObjectiveState.Capturing);
			BroadcastCaptureState();
			return false;
		}

		/// <summary>
		/// Expires an unfinished capture that nobody has worked on recently.
		/// </summary>
		private void CaptureDecayTick()
		{
			/* Driven by the timer alone, not by progress. An interrupt leaves progress at zero and
			 * the point resting on Contested, and that rest needs the same clock to move it back —
			 * gating this on progress meant a contested point stayed contested until somebody
			 * happened to interact with it again. */
			if (progressDecayTimer <= 0.0f)
			{
				return;
			}

			progressDecayTimer -= (float)base.TimeManager.TickDelta;
			if (progressDecayTimer > 0.0f)
			{
				return;
			}

			CaptureProgress = 0;
			CapturingCharacterID = 0;
			progressDecayTimer = 0.0f;

			// Back to whatever the point was before the attempt started.
			SetState(OwnerCharacterID != 0 ? ObjectiveState.Captured : ObjectiveState.Neutral);
			BroadcastCaptureState();
		}

		/// <summary>
		/// Pushes this point's state to everyone who can see it.
		/// </summary>
		/// <remarks>
		/// Sent to the observers of the capture point itself, not to the interacting player. A
		/// capture point is world state — the same reasoning <c>SwitchAction</c> documents for a
		/// door: everyone watching the objective needs to see it move, and the set of clients who
		/// can see the objective is not the set who can see whoever touched it. It is also why
		/// decay has to broadcast: nobody interacted, so there is no player to reply to.
		/// </remarks>
		private void BroadcastCaptureState()
		{
			if (!base.IsServerStarted ||
				Template == null ||
				base.NetworkObject == null ||
				!base.NetworkObject.IsSpawned)
			{
				return;
			}

			base.NetworkObject.Broadcast(new CapturePointUpdateBroadcast()
			{
				InteractableID = ID,
				TemplateID = Template.ID,
				OwnerCharacterID = OwnerCharacterID,
				State = State,
				CaptureProgress = CaptureProgress,
				InteractionsToCapture = Template.InteractionsToCapture,
			});
		}

		/// <summary>
		/// Sets the objective state and fires the <see cref="OnStateChanged"/> event.
		/// </summary>
		private void SetState(ObjectiveState newState)
		{
			if (State == newState)
			{
				return;
			}
			State = newState;
			ICapturePoint.OnStateChanged?.Invoke(this, newState);
		}

		/// <summary>
		/// Clears ownership and capture progress when this instance returns to the pool.
		/// </summary>
		/// <remarks>
		/// All four fields are per-life state. Without this a recycled capture point comes back
		/// still owned by whoever held it in its previous life — and <see cref="CanInteract"/>
		/// refuses the owner outright, so that player could never take it again.
		/// </remarks>
		/// <param name="asServer">True when the reset is for the server instance.</param>
		public override void ResetState(bool asServer)
		{
			// Dropped before the base call, to match the subscription taken in OnStartServer.
			ReleaseDecayTick();

			base.ResetState(asServer);

			OwnerCharacterID = 0;
			CapturingCharacterID = 0;
			CaptureProgress = 0;
			progressDecayTimer = 0.0f;
			State = ObjectiveState.Neutral;
		}

		/// <summary>
		/// Writes capture point state to the network payload.
		/// </summary>
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			base.WritePayload(connection, writer);
			writer.WriteInt64(OwnerCharacterID);
			writer.WriteUInt8Unpacked((byte)State);
			/* Progress travels too. CapturePointUpdateBroadcast carries it to clients who were
			 * already watching, but a client that starts observing a point mid-capture only gets
			 * the payload — without this it would draw a point sitting at zero while the state
			 * said Capturing, and would keep drawing it that way until the next interaction. */
			writer.WriteInt32(CaptureProgress);
		}

		/// <summary>
		/// Reads capture point state from the network payload.
		/// </summary>
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			base.ReadPayload(connection, reader);
			OwnerCharacterID = reader.ReadInt64();
			State = (ObjectiveState)reader.ReadUInt8Unpacked();
			CaptureProgress = reader.ReadInt32();
		}
	}
}