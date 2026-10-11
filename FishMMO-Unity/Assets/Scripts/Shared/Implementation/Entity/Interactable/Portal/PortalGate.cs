using FishNet.Connection;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// The one place a portal use is decided: open → travel; closed and the player qualifies →
	/// activate, then travel; otherwise refuse with a toast. Server only.
	/// </summary>
	/// <remarks>
	/// <para>Called by <see cref="TeleportAction"/> (a <see cref="Teleporter"/> interaction) and
	/// <see cref="SceneTeleporter"/> (walking into a scene portal) just before they move the player.
	/// An object without a <see cref="PortalActivation"/> is not gated at all, so every teleporter
	/// that existed before portals keeps working unchanged.</para>
	/// <para>Activation IS the use: there is no separate "activate" interaction. A player who meets
	/// a condition walks straight through, and the portal stays open for whoever its scope says.
	/// Every refusal answers the owner with the general <see cref="ToastBroadcast"/>, the same path
	/// every other teleport refusal takes.</para>
	/// </remarks>
	public static class PortalGate
	{
		private const string LOG = "PortalGate";

		/// <summary>Told to a player whose record the server has not finished reading.</summary>
		public const string NotReadyMessage = "The portal does not answer yet. Try again in a moment.";

		/// <summary>
		/// Decides a use of the portal on <paramref name="portalObject"/>, activating it when the
		/// player qualifies.
		/// </summary>
		/// <param name="player">The player using it.</param>
		/// <param name="portalObject">The teleporter's GameObject.</param>
		/// <param name="eventData">The interaction's event data, if any; conditions read it.</param>
		/// <returns>True when the player may travel.</returns>
		public static bool TryPass(IPlayerCharacter player, GameObject portalObject, EventData eventData = null)
		{
			if (portalObject == null || !portalObject.TryGetComponent(out PortalActivation portal) || !portal.enabled)
			{
				return true;
			}
			return TryPass(player, portal, eventData, PortalActivationStore.Current);
		}

		/// <summary>See <see cref="TryPass(IPlayerCharacter, GameObject, EventData)"/>.</summary>
		public static bool TryPass(IPlayerCharacter player, PortalActivation portal, EventData eventData, PortalActivationStore store)
		{
			if (portal == null)
			{
				return true;
			}
			if (player == null || store == null)
			{
				return false;
			}

			PortalActivationVerdict verdict = Use(player, portal, eventData, store, out string message);
			if (!string.IsNullOrEmpty(message))
			{
				bool passed = verdict == PortalActivationVerdict.Open || verdict == PortalActivationVerdict.Activate || verdict == PortalActivationVerdict.ActivateWithKey;
				Toast(player, message, passed ? ToastSeverity.Success : ToastSeverity.Warning);
			}
			return verdict == PortalActivationVerdict.Open ||
				verdict == PortalActivationVerdict.Activate ||
				verdict == PortalActivationVerdict.ActivateWithKey;
		}

		/// <summary>
		/// The use itself, without the network: decides, activates when the verdict says so (spending
		/// the key first when the key opened it and the portal spends keys), and says what the player
		/// should be told. <see cref="TryPass(IPlayerCharacter, PortalActivation, EventData, PortalActivationStore)"/>
		/// sends that line; tests read it.
		/// </summary>
		/// <param name="character">The character using the portal.</param>
		/// <param name="portal">The portal.</param>
		/// <param name="eventData">Conditions read it; may be null.</param>
		/// <param name="store">The activation store.</param>
		/// <param name="message">What to tell the player, or null for nothing.</param>
		/// <returns>
		/// The verdict that applied. An <see cref="PortalActivationVerdict.ActivateWithKey"/> whose key
		/// could not be spent comes back as <see cref="PortalActivationVerdict.Locked"/>.
		/// </returns>
		public static PortalActivationVerdict Use(ICharacter character, PortalActivation portal, EventData eventData, PortalActivationStore store, out string message)
		{
			message = null;
			if (character == null || portal == null || store == null)
			{
				return PortalActivationVerdict.Locked;
			}

			string sceneName = portal.SceneName;
			int index = portal.PortalIndex;
			long now = store.NowUnixMs;
			bool? active = PortalActivationRules.IsActive(portal.Scope,
				store.IsCharacterActive(character.ID, sceneName, index),
				store.WorldState(sceneName, index),
				now);

			// Conditions are evaluated only when they can matter, and the key only when no condition passed.
			bool conditionMet = false;
			bool keyHeld = false;
			if (active == false)
			{
				conditionMet = portal.AnyConditionMet(character, eventData);
				if (!conditionMet)
				{
					keyHeld = portal.HoldsKey(character);
				}
			}

			PortalActivationVerdict verdict = PortalActivationRules.Decide(active, portal.HasRequirements, conditionMet, keyHeld);
			switch (verdict)
			{
				case PortalActivationVerdict.NotReady:
					bool world = PortalActivationRules.IsWorldScope(portal.Scope);
					store.RequestLoad(world ? 0L : character.ID, world ? sceneName : null);
					message = NotReadyMessage;
					break;

				case PortalActivationVerdict.Locked:
					message = portal.LockedMessage;
					break;

				case PortalActivationVerdict.ActivateWithKey:
					if (portal.ConsumeKey && !TrySpendKey(character, portal, eventData))
					{
						message = "The key will not leave your pack.";
						return PortalActivationVerdict.Locked;
					}
					message = Activate(character, portal, store, now);
					break;

				case PortalActivationVerdict.Activate:
					message = Activate(character, portal, store, now);
					break;
			}
			return verdict;
		}

		/// <summary>Records the activation for whoever the scope names. Returns the line for the player.</summary>
		private static string Activate(ICharacter character, PortalActivation portal, PortalActivationStore store, long now)
		{
			if (portal.Scope == PortalActivationScope.PerCharacter)
			{
				store.ActivateForCharacter(character.ID, portal.SceneName, portal.PortalIndex);
				return "The portal awakens for you.";
			}

			PortalWorldState opening = PortalActivationRules.WorldStateFor(portal.Scope, portal.DurationSeconds, now);
			store.ActivateForWorld(portal.SceneName, portal.PortalIndex, opening);
			Log.Debug(LOG, $"'{portal.SceneName}' portal {portal.PortalIndex} opened for the world ({opening}) by {character.ID}.");
			return portal.Scope == PortalActivationScope.WorldPermanent
				? "The portal awakens, and will stay open for all."
				: $"The portal awakens for all, for {FormatDuration(portal.DurationSeconds)}.";
		}

		/// <summary>
		/// Spends one key through <see cref="RemoveItemAction"/>, which already persists the change and
		/// tells the owner. Checked by count, because that action reports nothing.
		/// </summary>
		private static bool TrySpendKey(ICharacter character, PortalActivation portal, EventData eventData)
		{
			if (portal.Key == null || !character.TryGet(out IInventoryController inventory))
			{
				return false;
			}
			int before = inventory.GetItemCount(portal.Key);
			new RemoveItemAction() { ItemTemplate = portal.Key, Amount = 1 }.Execute(character, eventData);
			return inventory.GetItemCount(portal.Key) < before;
		}

		/// <summary>"2 hours", "45 minutes", "30 seconds".</summary>
		public static string FormatDuration(float seconds)
		{
			if (seconds >= 7200.0f)
			{
				return $"{Mathf.RoundToInt(seconds / 3600.0f)} hours";
			}
			if (seconds >= 120.0f)
			{
				return $"{Mathf.RoundToInt(seconds / 60.0f)} minutes";
			}
			return $"{Mathf.RoundToInt(seconds)} seconds";
		}

		private static void Toast(IPlayerCharacter player, string text, ToastSeverity severity)
		{
			NetworkConnection owner = player?.Owner;
			if (owner == null || !owner.IsActive)
			{
				return;
			}
			owner.Broadcast(new ToastBroadcast() { Text = text, Severity = severity });
		}
	}
}
