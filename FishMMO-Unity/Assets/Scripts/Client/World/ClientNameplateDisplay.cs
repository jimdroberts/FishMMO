using System;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Decides which overhead nameplates are up without being targeted: every NPC and every
	/// other player inside their own chosen ranges, and the player's own. Driven from
	/// <c>Client.Update</c>.
	/// </summary>
	/// <remarks>
	/// <para><b>Why this is not part of the nameplate layer.</b> <see cref="UITKNameplateLayer"/>
	/// answers rendering questions — how far, how many, is it behind a wall. Whether a character
	/// deserves a nameplate at all is a gameplay question about who the viewer is and who they are
	/// looking at, and it has to be asked of every character in view, including the ones whose
	/// plates are currently hidden. That is this class, walking
	/// <c>BaseCharacter.ClientCharacters</c>.</para>
	///
	/// <para><b>Ownership of a plate's visibility is split three ways</b>, and the split is what
	/// keeps the three from fighting:</para>
	/// <list type="bullet">
	/// <item><description><see cref="UITKTarget"/> turns the current target's plate on, and asks
	/// <see cref="ShouldStayVisible"/> before turning it off again on clear. Without that ask,
	/// untargeting an NPC standing next to the player blinked its nameplate off for one sweep
	/// interval before this class put it back.</description></item>
	/// <item><description><see cref="UITKPetControl"/> turns the player's own pet's plate on;
	/// the rule here agrees (an owned character that is not the player is always kept), so a
	/// sweep never undoes it.</description></item>
	/// <item><description>This class owns everything else: NPCs and other players by their two
	/// ranges, the local player by the own-name toggle. Any further "keep this name up" rule —
	/// party members, guild mates — belongs in <see cref="Decide"/> rather than in a second
	/// writer that would have to be reconciled with it.</description></item>
	/// </list>
	///
	/// <para><b>Cost.</b> One dictionary walk every <see cref="SweepIntervalSeconds"/>, a squared
	/// distance per NPC, a standing colour per visible plate, and a visibility flip only on a
	/// transition. The walk is deliberately not per frame: a nameplate appearing a fifth of a
	/// second after the player crosses the range is invisible, and the walk touches every character
	/// in view. Distances use a small hysteresis (<see cref="ExitRangeMultiplier"/>) so a character
	/// idling exactly on the boundary does not toggle every sweep.</para>
	/// </remarks>
	public sealed class ClientNameplateDisplay
	{
		/// <summary>Seconds between sweeps of the client character registry.</summary>
		public const float SweepIntervalSeconds = 0.2f;

		/// <summary>
		/// A nameplate that is already up stays up until the character is this many times the
		/// range away. Five percent: a metre and a half at the default range, enough to absorb
		/// the jitter of two characters standing on the line and small enough to be unnoticed.
		/// </summary>
		public const float ExitRangeMultiplier = 1.05f;

		/// <summary>The live instance, so the target frame can consult the rule without a reference.</summary>
		private static ClientNameplateDisplay instance;

		/// <summary>The character whose position ranges are measured from, or null when not in the world.</summary>
		private IPlayerCharacter localCharacter;

		/// <summary>The local character's target controller, resolved once on world entry.</summary>
		private ITargetController targetController;

		/// <summary>Cached <see cref="ClientWorldLabelSettings.NpcNameRange"/>.</summary>
		private float npcNameRange = ClientWorldLabelSettings.DefaultNpcNameRange;

		/// <summary>Cached <see cref="ClientWorldLabelSettings.PlayerNameRange"/>.</summary>
		private float playerNameRange = ClientWorldLabelSettings.DefaultPlayerNameRange;

		/// <summary>Cached <see cref="ClientWorldLabelSettings.ShowOwnName"/>.</summary>
		private bool showOwnName = ClientWorldLabelSettings.DefaultShowOwnName;

		/// <summary>True while the cached settings above are stale.</summary>
		/// <remarks>
		/// Re-read on the next sweep rather than inside the settings event. Settings events can
		/// arrive before the configuration store exists (see <see cref="ClientCombatDisplay"/>), and
		/// a sweep is the one place that always has a live local character to apply them to.
		/// </remarks>
		private bool settingsDirty = true;

		/// <summary>Time at which the next sweep is due.</summary>
		private float nextSweepTime;

		/// <summary>Subscribes to world entry and settings changes. Call during client startup.</summary>
		public void Initialize()
		{
			instance = this;
			IPlayerCharacter.OnStartLocalClient += OnStartLocalClient;
			IPlayerCharacter.OnStopLocalClient += OnStopLocalClient;
			ClientWorldLabelSettings.OnChanged += OnSettingsChanged;
		}

		/// <summary>Unsubscribes. Call during client teardown.</summary>
		public void Shutdown()
		{
			IPlayerCharacter.OnStartLocalClient -= OnStartLocalClient;
			IPlayerCharacter.OnStopLocalClient -= OnStopLocalClient;
			ClientWorldLabelSettings.OnChanged -= OnSettingsChanged;
			localCharacter = null;
			targetController = null;
			if (instance == this)
			{
				instance = null;
			}
		}

		/// <summary>
		/// Runs one sweep when the interval has elapsed. Call once per frame.
		/// </summary>
		public void Tick()
		{
			if (localCharacter == null)
			{
				return;
			}

			float now = Time.unscaledTime;
			if (!settingsDirty && now < nextSweepTime)
			{
				return;
			}
			nextSweepTime = now + SweepIntervalSeconds;

			if (settingsDirty)
			{
				settingsDirty = false;
				npcNameRange = ClientWorldLabelSettings.NpcNameRange;
				playerNameRange = ClientWorldLabelSettings.PlayerNameRange;
				showOwnName = ClientWorldLabelSettings.ShowOwnName;
			}

			try
			{
				Sweep();
			}
			catch (Exception ex)
			{
				/* A nameplate sweep that throws must not take Client.Update down with it, and
				 * it must not spin: the interval above is already advanced, so a persistent
				 * fault logs five times a second at worst rather than every frame. */
				Log.Error("ClientNameplateDisplay", "Nameplate sweep failed.", ex);
			}
		}

		/// <summary>
		/// Whether a character's nameplate should stay up when the target frame stops framing it.
		/// </summary>
		/// <param name="character">The character being untargeted.</param>
		/// <returns>True to leave the plate up; false to let the target frame take it down.</returns>
		/// <remarks>
		/// Answered from the same rule the sweep applies, so the target frame and the sweep can
		/// never disagree about a character that is both untargeted and in range. Without a live
		/// display the answer falls back to the pre-range behaviour: the player's own plate stays,
		/// everything else goes.
		/// </remarks>
		public static bool ShouldStayVisible(ICharacter character)
		{
			if (character == null)
			{
				return false;
			}

			bool isPlayer = character is IPlayerCharacter;
			bool isOwner = character.NetworkObject != null && character.NetworkObject.IsOwner;

			ClientNameplateDisplay display = instance;
			if (display == null || display.localCharacter == null)
			{
				return isOwner;
			}

			/* The plate is up right now — the frame is asking whether to take it down — so the
			 * exit distance applies, exactly as it would on the next sweep. */
			return Decide(
				isPlayer,
				isOwner,
				isTargeted: false,
				wasVisible: true,
				sqrDistance: display.SqrDistanceTo(character),
				display.npcNameRange,
				display.playerNameRange,
				display.showOwnName);
		}

		/// <summary>
		/// The visibility rule, as arithmetic on facts about one character.
		/// </summary>
		/// <param name="isPlayer">Whether the character is a player character.</param>
		/// <param name="isOwner">Whether this client owns the character: the local player, or their pet.</param>
		/// <param name="isTargeted">Whether the character is the local player's current target — hovered, or pinned to the target frame.</param>
		/// <param name="wasVisible">Whether the plate is currently up, which selects the exit distance.</param>
		/// <param name="sqrDistance">Squared distance from the local player, in metres squared.</param>
		/// <param name="npcRange">The NPC nameplate range, in metres; zero or less means target only.</param>
		/// <param name="playerRange">The other-player nameplate range, in metres; zero or less means target only.</param>
		/// <param name="showOwnName">Whether the player's own nameplate stays up.</param>
		/// <returns>True when the nameplate should be up.</returns>
		/// <remarks>
		/// Static and free of Unity types so the truth table can be pinned by a plain unit test.
		/// </remarks>
		public static bool Decide(bool isPlayer, bool isOwner, bool isTargeted, bool wasVisible, float sqrDistance, float npcRange, float playerRange, bool showOwnName)
		{
			if (isPlayer && isOwner)
			{
				return showOwnName;
			}

			// The player's own pet: the pet control put its labels up and nothing takes them down.
			if (isOwner)
			{
				return true;
			}

			// The target frame owns the current target's labels, hovered or pinned.
			if (isTargeted)
			{
				return true;
			}

			float range = isPlayer ? playerRange : npcRange;
			if (range <= 0.0f || float.IsNaN(range))
			{
				return false;
			}

			float limit = wasVisible ? range * ExitRangeMultiplier : range;
			return sqrDistance <= limit * limit;
		}

		private void OnStartLocalClient(IPlayerCharacter character)
		{
			localCharacter = character;
			targetController = null;
			if (character != null)
			{
				character.TryGet(out targetController);
			}

			// Apply immediately: the player's own name should be up the frame they appear.
			settingsDirty = true;
			nextSweepTime = 0.0f;
		}

		private void OnStopLocalClient(IPlayerCharacter character)
		{
			if (ReferenceEquals(localCharacter, character) || localCharacter == null)
			{
				localCharacter = null;
				targetController = null;
			}
		}

		private void OnSettingsChanged()
		{
			settingsDirty = true;
		}

		/// <summary>
		/// Applies the rule to every character the client knows, and keeps the standing colour of
		/// the plates that are up in step with it.
		/// </summary>
		private void Sweep()
		{
			Transform currentTarget = targetController != null ? targetController.Current.Target : null;
			/* The pinned character counts as targeted for as long as the pin holds: its card is
			 * up and its nameplate belongs with it, whatever the pointer is doing. */
			Transform pinnedTarget = targetController != null ? targetController.PinnedTarget : null;

			foreach (ICharacter character in BaseCharacter.ClientCharacters.Values)
			{
				if (character == null)
				{
					continue;
				}

				Nameplate plate = character.CharacterNameplate;
				if (plate == null)
				{
					// Nothing to show: a character authored without a nameplate.
					continue;
				}

				bool isPlayer = character is IPlayerCharacter;
				bool isOwner = character.NetworkObject != null && character.NetworkObject.IsOwner;

				bool wasVisible = plate.Visible;
				bool isTargeted = (currentTarget != null && ReferenceEquals(currentTarget, character.Transform)) ||
					(pinnedTarget != null && ReferenceEquals(pinnedTarget, character.Transform));

				bool visible = Decide(
					isPlayer,
					isOwner,
					isTargeted,
					wasVisible,
					isOwner ? 0.0f : SqrDistanceTo(character),
					npcNameRange,
					playerNameRange,
					showOwnName);

				if (visible && !isOwner)
				{
					/* Refreshed every sweep rather than only on the way up, because standing
					 * changes while a plate is already up: a faction turns on the player, a guild
					 * mate logs in, an arena assigns teams. The plate ignores a write that does not
					 * change the colour, so a settled world pays a comparison. The owner's own
					 * plate is left uncoloured — a player's standing with themselves is not a fact
					 * about anything, and colouring it would tell them nothing. */
					plate.AllianceTint = AllianceColor(character);
				}

				if (visible == wasVisible)
				{
					continue;
				}

				plate.Visible = visible;
			}
		}

		/// <summary>Squared distance from the local player to a character, in metres squared.</summary>
		private float SqrDistanceTo(ICharacter character)
		{
			Transform origin = localCharacter?.Transform;
			Transform target = character.Transform;
			if (origin == null || target == null)
			{
				return float.PositiveInfinity;
			}
			return (target.position - origin.position).sqrMagnitude;
		}

		/// <summary>The faction standing colour the target frame would give this character, or white.</summary>
		private Color AllianceColor(ICharacter character)
		{
			if (localCharacter != null &&
				localCharacter.TryGet(out IFactionController ownFaction) &&
				character.TryGet(out IFactionController theirFaction))
			{
				return ownFaction.GetAllianceLevelColor(theirFaction);
			}
			return Color.white;
		}
	}
}
