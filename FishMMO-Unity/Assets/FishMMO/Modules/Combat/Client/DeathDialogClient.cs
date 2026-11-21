using System;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// The Combat module's death notice on the client (2026-11-20): the server's <see cref="DeathBroadcast"/> opens the
	/// death dialog, a player who arrives in the world dead gets it too, and if the dialog cannot be found within its grace
	/// period the character is respawned at its bind point rather than left stranded. Moved out of the core's Client.
	/// </summary>
	public sealed class DeathDialogClient : IClientService
	{
		/// <summary>The service's id.</summary>
		public const string ServiceId = "combat.death";

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Register()
		{
			ClientServices.Register(ServiceId, () => new DeathDialogClient());
		}

		private NetworkManager networkManager;

		/// <summary>The local player's character while it is in the world.</summary>
		private IPlayerCharacter localCharacter;

		/// <summary>The client, found once, for its connection state (the fallback respawn checks it).</summary>
		private Client client;

		private bool IsConnectionReady
		{
			get
			{
				if (client == null)
				{
					client = UnityEngine.Object.FindAnyObjectByType<Client>();
				}
				return client != null && client.IsConnectionReady();
			}
		}

		/// <inheritdoc />
		public void Initialize(NetworkManager manager)
		{
			networkManager = manager;
			networkManager?.ClientManager.RegisterBroadcast<DeathBroadcast>(OnDeathBroadcast);
			IPlayerCharacter.OnStartLocalClient += OnLocalCharacterStarted;
			IPlayerCharacter.OnStopLocalClient += OnLocalCharacterStopped;
		}

		/// <inheritdoc />
		public void Tick(float deltaTime)
		{
			TickDeathDialogFallback();
		}

		/// <inheritdoc />
		public void Clear()
		{
			// The character this was armed for is gone (despawn, scene transfer, logout). Letting it survive would respawn
			// whatever character comes next at its bind point.
			CancelDeathDialogFallback();
		}

		/// <inheritdoc />
		public void Shutdown()
		{
			networkManager?.ClientManager.UnregisterBroadcast<DeathBroadcast>(OnDeathBroadcast);
			IPlayerCharacter.OnStartLocalClient -= OnLocalCharacterStarted;
			IPlayerCharacter.OnStopLocalClient -= OnLocalCharacterStopped;
			CancelDeathDialogFallback();
			localCharacter = null;
			networkManager = null;
		}

		private void OnLocalCharacterStarted(IPlayerCharacter character)
		{
			localCharacter = character;
			try
			{
				EnsureDeadCharacterHasAWayOut(character);
			}
			catch (Exception ex)
			{
				Log.Error("DeathDialogClient", "OnLocalCharacterStarted: dead-character check failed.", ex);
			}
		}

		private void OnLocalCharacterStopped(IPlayerCharacter character)
		{
			localCharacter = null;
			CancelDeathDialogFallback();
		}

		/// <summary>
		/// Handles a death broadcast by showing the death dialog UI.
		/// </summary>
		/// <param name="msg">The death broadcast message.</param>
		/// <param name="ch">The network channel.</param>
		private void OnDeathBroadcast(DeathBroadcast msg, Channel ch)
		{
			try
			{
				if (TryShowDeathDialog())
				{
					return;
				}

				/* Do NOT respawn from here. Sending the player to their bind point the instant
				 * one lookup misses would take the decision away from them — including a
				 * resurrect that another player may already be casting — and it is not
				 * reversible once the character has left its corpse.
				 *
				 * A miss here is far more likely to be a timing artefact than a missing build:
				 * the world GUI scene that carries the dialog is loaded per world entry, so a
				 * death notification can in principle be handled a moment before that scene's
				 * controls have registered. Arm a grace period that keeps retrying instead, and
				 * let Update decide. */
				ArmDeathDialogFallback();
			}
			catch (Exception ex)
			{
				Log.Error("DeathDialogClient", $"OnDeathBroadcast: {ex}");
			}
		}

		/// <summary>
		/// Shows the death dialog if it is currently registered.
		/// </summary>
		/// <returns><c>true</c> when the dialog was found and shown.</returns>
		private bool TryShowDeathDialog()
		{
			if (!UIManager.TryGetTK("UIDeathDialog", out UITKDeathDialog deathDialog))
			{
				return false;
			}

			// Idempotent: the control also opens itself when handed a dead character, and
			// showing an already-visible dialog only refreshes its message.
			deathDialog.ShowDeathDialog();
			return true;
		}

		/// <summary>True while waiting for the death dialog to appear before giving up on it.</summary>
		private bool deathDialogFallbackPending;

		/// <summary>Unscaled time after which the dialog is treated as genuinely absent.</summary>
		private float deathDialogFallbackDeadline;

		/// <summary>Unscaled time of the next attempt to find the dialog.</summary>
		private float nextDeathDialogRetryTime;

		/// <summary>
		/// How long to keep looking for the death dialog before falling back to an automatic
		/// respawn.
		/// </summary>
		/// <remarks>
		/// Long enough to cover the dialog's scene registering after the death notification is
		/// handled, short enough that a genuinely missing dialog does not leave the player
		/// staring at a corpse wondering whether the game has stopped responding.
		/// </remarks>
		private const float DeathDialogGraceSeconds = 5.0f;

		/// <summary>Seconds between attempts to find the dialog during the grace period.</summary>
		private const float DeathDialogRetryIntervalSeconds = 0.25f;

		/// <summary>
		/// Starts the grace period during which the death dialog may still appear.
		/// </summary>
		private void ArmDeathDialogFallback()
		{
			if (this.deathDialogFallbackPending)
			{
				return;
			}

			this.deathDialogFallbackPending = true;
			this.deathDialogFallbackDeadline = Time.unscaledTime + DeathDialogGraceSeconds;
			this.nextDeathDialogRetryTime = Time.unscaledTime + DeathDialogRetryIntervalSeconds;

			Log.Warning("DeathDialogClient",
				"Character is dead but no death dialog is registered under 'UITKDeathDialog' yet; " +
				$"waiting up to {DeathDialogGraceSeconds:F0}s for it before falling back to an automatic respawn.");
		}

		/// <summary>
		/// Cancels a pending fallback. Called whenever the dead state it was armed for no longer
		/// applies.
		/// </summary>
		private void CancelDeathDialogFallback()
		{
			this.deathDialogFallbackPending = false;
			this.deathDialogFallbackDeadline = 0f;
			this.nextDeathDialogRetryTime = 0f;
		}

		/// <summary>
		/// Retries the death dialog during its grace period and, only once that expires,
		/// respawns so the character is not stranded.
		/// </summary>
		private void TickDeathDialogFallback()
		{
			if (!this.deathDialogFallbackPending)
			{
				return;
			}

			float now = Time.unscaledTime;
			if (now >= this.nextDeathDialogRetryTime)
			{
				this.nextDeathDialogRetryTime = now + DeathDialogRetryIntervalSeconds;

				if (TryShowDeathDialog())
				{
					// The dialog turned up. The player chooses from here.
					CancelDeathDialogFallback();
					return;
				}
			}

			if (now < this.deathDialogFallbackDeadline)
			{
				return;
			}

			CancelDeathDialogFallback();

			/* Re-verify before acting. The grace period is long enough for the situation to have
			 * changed underneath us — another player's resurrect can land, or the character can
			 * be despawned by a scene transfer — and respawning then would relocate a player who
			 * is fine, or who is already somewhere else. By this point the client has certainly
			 * reconciled health, so IsAlive is trustworthy here in a way it would not be in the
			 * first instants after death. */
			if (this.localCharacter == null)
			{
				Log.Debug("DeathDialogClient", "Death dialog fallback abandoned: the character is no longer spawned locally.");
				return;
			}

			if (this.localCharacter.TryGet(out ICharacterDamageController damageController) &&
				damageController.IsAlive)
			{
				Log.Debug("DeathDialogClient", "Death dialog fallback abandoned: the character is alive again.");
				return;
			}

			Log.Error("DeathDialogClient",
				$"No death dialog registered under 'UITKDeathDialog' after {DeathDialogGraceSeconds:F0}s. " +
				"The respawn and resurrect controls are unreachable, so this character would be stranded dead. " +
				"Check that the UITKDeathDialog object exists in the world GUI scene, is active, and that its " +
				"GameObject is named 'UITKDeathDialog'.");

			RequestFallbackRespawn("the death dialog never appeared");
		}

		/// <summary>
		/// Earliest unscaled time at which another fallback respawn may be sent.
		/// </summary>
		private float nextFallbackRespawnTime;

		/// <summary>
		/// Minimum seconds between fallback respawn requests.
		/// </summary>
		/// <remarks>
		/// Longer than the server's two-second respawn ingress debounce, so a repeated death
		/// notification cannot produce a burst the server would only throw away.
		/// </remarks>
		private const float FallbackRespawnCooldownSeconds = 3.0f;

		/// <summary>
		/// Asks the server to respawn this character at its bind point without going through
		/// the death dialog.
		/// </summary>
		/// <remarks>
		/// A last resort for the case where the dialog cannot be shown at all. Being dead is
		/// only an intermediate state if something can end it, and every route out of it runs
		/// through a dialog that is missing here — so the choice is between taking the decision
		/// away from the player and leaving the character permanently unplayable. Respawning at
		/// the bind point is the option the player would have had anyway; the resurrect they
		/// forfeit is speculative, and only offered while they are still lying there.
		/// <para>
		/// This is a degraded path that should never run in a correct build, which is why the
		/// caller logs an error explaining the wiring fault before reaching it.
		/// </para>
		/// </remarks>
		/// <param name="reason">Why the fallback was needed, for diagnostics.</param>
		private void RequestFallbackRespawn(string reason)
		{
			if (Time.unscaledTime < this.nextFallbackRespawnTime)
			{
				return;
			}

			if (!IsConnectionReady)
			{
				Log.Warning("DeathDialogClient",
					$"Cannot send a fallback respawn ({reason}): the connection is not ready. " +
					"The character stays dead until the connection recovers.");
				return;
			}

			this.nextFallbackRespawnTime = Time.unscaledTime + FallbackRespawnCooldownSeconds;

			Log.Warning("DeathDialogClient",
				$"Automatically respawning at the bind point because {reason}. " +
				"The player was not offered the choice; fix the death dialog wiring.");

			Client.Broadcast(new RespawnAtBindPointBroadcast(), Channel.Reliable);
		}

		/// <summary>
		/// Guarantees a character that entered the world already dead can act on it.
		/// </summary>
		/// <remarks>
		/// A character can arrive dead in two ordinary ways: logging in on a corpse, and being
		/// transferred between scene servers while dead. In both, <c>Flags</c> carries
		/// <see cref="CharacterFlags.IsDead"/> in the spawn payload, so the state is known here
		/// without waiting on any message.
		/// <para>
		/// <see cref="UITKDeathDialog"/> opens itself from the same state when
		/// <c>UIManager.SetCharacter</c> hands it the character, which is the normal path and
		/// runs a moment before this. This exists for the case where that control is not
		/// present at all: the server's own re-sent <c>DeathBroadcast</c> would reach
		/// <see cref="OnDeathBroadcast"/> and trigger the same fallback, but only if it arrives
		/// — and it is sent before the character spawns, so it depends on the world GUI scene
		/// already being loaded. Checking the spawned character's state does not.
		/// </para>
		/// </remarks>
		/// <param name="c">The local player character that just started.</param>
		private void EnsureDeadCharacterHasAWayOut(IPlayerCharacter c)
		{
			if (c == null || !c.IsFlagged(CharacterFlags.IsDead))
			{
				return;
			}

			if (TryShowDeathDialog())
			{
				return;
			}

			// Same rule as OnDeathBroadcast: never respawn on a single missed lookup. Give the
			// dialog its grace period first — this path in particular runs during world entry,
			// which is exactly when its scene may still be settling.
			ArmDeathDialogFallback();
		}
	}
}
