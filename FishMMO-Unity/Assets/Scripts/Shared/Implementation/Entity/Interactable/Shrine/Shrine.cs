using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Shrine interactable that applies buffs or heals health, mana, or both when a player interacts with it.
	/// Configured via a <see cref="ShrineTemplate"/> ScriptableObject asset.
	/// </summary>
	[RequireComponent(typeof(SceneObjectNamer))]
	public class Shrine : Interactable, IShrine
	{
		/// <summary>
		/// Template defining the shrine's healing and buff effects.
		/// </summary>
		public ShrineTemplate Template;

		/// <summary>
		/// Achievement to increment when a player uses this shrine.
		/// </summary>
		public AchievementTemplate AchievementTemplate;

		/// <inheritdoc />
		ShrineTemplate IShrine.Template => Template;

		/// <inheritdoc />
		AchievementTemplate IShrine.AchievementTemplate => AchievementTemplate;

		private string title = "Shrine";

		/// <summary>
		/// Display title shown above the shrine.
		/// </summary>
		public override string Title { get { return title; } }

		/// <summary>
		/// Title color for the shrine UI label.
		/// </summary>
		public override Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.teal); } }

		public override void OnAwake()
		{
			base.OnAwake();

			if (Template != null)
			{
				title = Template.Name;
			}
		}

		/// <summary>
		/// Per-character cooldown expiry, server-side only.
		/// </summary>
		/// <remarks>
		/// Never replicated and never persisted. A shrine is world state with no database row, so
		/// a server restart clears every cooldown — which is the same latitude respawn timers and
		/// gathering node charges already take.
		/// </remarks>
		private Dictionary<long, DateTime> nextUsableUtcByCharacter;

		/// <summary>
		/// Entry count past which a consume also sweeps expired entries.
		/// </summary>
		/// <remarks>
		/// The table is keyed by character ID and every character who ever touches this shrine
		/// adds one, so it needs a bound. Sweeping on write rather than on a timer keeps it free
		/// for the overwhelmingly common case of a shrine a handful of people use.
		/// </remarks>
		private const int CooldownSweepThreshold = 64;

		/// <inheritdoc />
		/// <remarks>
		/// Adds the cooldown to the base checks, and stays <b>pure</b> while doing it — the
		/// limiter is spent by <see cref="TryConsumeCooldown"/>, for the same reason
		/// <see cref="IInteractable.CanInteract"/> stopped spending the interact rate limit: this
		/// is asked by the client's input handler and by the server, and a question that answers
		/// by consuming a budget cannot be asked twice.
		/// <para>
		/// It answers differently on each peer by design. The cooldown table is server-side only,
		/// so a client always reads "ready" and sends the request; the server is where the refusal
		/// happens. That is the same split <see cref="NPC.CanInteract"/> uses for loot rights.
		/// </para>
		/// </remarks>
		public override bool CanInteract(IPlayerCharacter character)
		{
			if (Template == null ||
				!base.CanInteract(character))
			{
				return false;
			}

			if (GetRemainingCooldown(character.ID) > 0.0f)
			{
				return false;
			}

			return true;
		}

		/// <summary>
		/// Seconds remaining before this character may use the shrine again.
		/// </summary>
		/// <param name="characterID">The character to test.</param>
		/// <returns>Seconds remaining, or 0 when the shrine is ready.</returns>
		public float GetRemainingCooldown(long characterID)
		{
			if (nextUsableUtcByCharacter == null ||
				!nextUsableUtcByCharacter.TryGetValue(characterID, out DateTime nextUsableUtc))
			{
				return 0.0f;
			}

			double remaining = (nextUsableUtc - DateTime.UtcNow).TotalSeconds;
			return remaining > 0.0 ? (float)remaining : 0.0f;
		}

		/// <summary>
		/// Spends this character's cooldown, returning false when it is still running.
		/// </summary>
		/// <remarks>
		/// Server-side. The check and the write happen together so two requests arriving in one
		/// frame cannot both pass — the broadcast handler is main-thread, so this is sufficient
		/// without any further locking.
		/// </remarks>
		/// <param name="characterID">The character using the shrine.</param>
		/// <returns>True when the shrine was ready and the cooldown has now been started.</returns>
		public bool TryConsumeCooldown(long characterID)
		{
			float cooldown = Template != null ? Template.CooldownSeconds : 0.0f;
			if (cooldown <= 0.0f)
			{
				return true;
			}

			if (GetRemainingCooldown(characterID) > 0.0f)
			{
				return false;
			}

			nextUsableUtcByCharacter ??= new Dictionary<long, DateTime>();

			if (nextUsableUtcByCharacter.Count >= CooldownSweepThreshold)
			{
				SweepExpiredCooldowns();
			}

			nextUsableUtcByCharacter[characterID] = DateTime.UtcNow.AddSeconds(cooldown);
			return true;
		}

		/// <summary>
		/// Drops cooldown entries that have already expired.
		/// </summary>
		private void SweepExpiredCooldowns()
		{
			DateTime nowUtc = DateTime.UtcNow;
			List<long> expired = null;

			foreach (KeyValuePair<long, DateTime> pair in nextUsableUtcByCharacter)
			{
				if (pair.Value <= nowUtc)
				{
					(expired ??= new List<long>()).Add(pair.Key);
				}
			}

			if (expired == null)
			{
				return;
			}

			for (int i = 0; i < expired.Count; ++i)
			{
				nextUsableUtcByCharacter.Remove(expired[i]);
			}
		}

		/// <summary>
		/// Drops every cooldown when this instance returns to the pool.
		/// </summary>
		/// <remarks>
		/// Per-life state, like every other pooled interactable's. A recycled shrine that kept the
		/// previous occupant's table would refuse players who had never used <em>this</em> shrine.
		/// </remarks>
		/// <param name="asServer">True when the reset is for the server instance.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			nextUsableUtcByCharacter?.Clear();
		}
	}
}