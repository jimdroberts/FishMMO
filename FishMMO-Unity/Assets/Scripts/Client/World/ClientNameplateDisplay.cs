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
	/// <para><b>Why this is not part of the label layer.</b> <see cref="UITKWorldLabelLayer"/>
	/// only ever sees labels whose GameObject is active — an inactive <see cref="WorldLabel"/>
	/// never registers. Nameplates are authored inactive on every character prefab and the target
	/// frame flips them on and off around the current target, so "show the ones in range" has to
	/// be decided at the character, by something that can see the characters whose labels are
	/// currently off. That is this class, walking <c>BaseCharacter.ClientCharacters</c>.</para>
	///
	/// <para><b>Ownership of a label's active state is split three ways</b>, and the split is
	/// what keeps the three from fighting:</para>
	/// <list type="bullet">
	/// <item><description><see cref="UITKTarget"/> turns the current target's labels on, and asks
	/// <see cref="ShouldStayVisible"/> before turning them off again on clear. Without that ask,
	/// untargeting an NPC standing next to the player blinked its nameplate off for one sweep
	/// interval before this class put it back.</description></item>
	/// <item><description><see cref="UITKPetControl"/> turns the player's own pet's labels on;
	/// the rule here agrees (an owned character that is not the player is always kept), so a
	/// sweep never undoes it.</description></item>
	/// <item><description>This class owns everything else: NPCs and other players by their two
	/// ranges, the local player by the own-name toggle. Any further "keep this name up" rule —
	/// party members, guild mates — belongs in <see cref="Decide"/> rather than in a second
	/// writer that would have to be reconciled with it.</description></item>
	/// </list>
	///
	/// <para><b>Cost.</b> One dictionary walk every <see cref="SweepIntervalSeconds"/>, a squared
	/// distance per NPC, and a <c>SetActive</c> only on a transition. The walk is deliberately not
	/// per frame: a nameplate appearing a fifth of a second after the player crosses the range is
	/// invisible, and the walk touches every character in view. Distances use a small hysteresis
	/// (<see cref="ExitRangeMultiplier"/>) so a character idling exactly on the boundary does not
	/// toggle every sweep.</para>
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
		/// <returns>True to leave the labels on; false to let the target frame turn them off.</returns>
		/// <remarks>
		/// Answered from the same rule the sweep applies, so the target frame and the sweep can
		/// never disagree about a character that is both untargeted and in range. Without a live
		/// display the answer falls back to the pre-range behaviour: the player's own labels stay,
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

			/* The labels are up right now — the frame is asking whether to take them down — so
			 * the exit distance applies, exactly as it would on the next sweep. */
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
		/// <param name="wasVisible">Whether the nameplate is currently up, which selects the exit distance.</param>
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
		/// Applies the rule to every character the client knows.
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

				WorldLabel nameLabel = character.CharacterNameLabel;
				if (nameLabel == null)
				{
					// Nothing to show: a character without an authored nameplate.
					continue;
				}

				bool isPlayer = character is IPlayerCharacter;
				bool isOwner = character.NetworkObject != null && character.NetworkObject.IsOwner;

				bool wasVisible = nameLabel.gameObject.activeSelf;
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

				if (visible == wasVisible)
				{
					continue;
				}

				if (visible && !isOwner)
				{
					/* Tint on the way up, the way the target frame does, so a hostile that walks
					 * into range reads as hostile before it is ever targeted. Once per transition
					 * rather than per sweep: the frame recolours on targeting anyway, and a
					 * standing relationship does not change while a nameplate is up. */
					nameLabel.color = AllianceColor(character);
				}

				nameLabel.gameObject.SetActive(visible);

				WorldLabel guildLabel = character.CharacterGuildLabel;
				if (guildLabel != null)
				{
					guildLabel.gameObject.SetActive(visible);
				}
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
