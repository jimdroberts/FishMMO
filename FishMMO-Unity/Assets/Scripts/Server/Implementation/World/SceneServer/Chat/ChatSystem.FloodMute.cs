using FishNet.Connection;
using UnityEngine;
using System.Collections.Generic;
using FishMMO.Server.Core;
using FishMMO.Shared.Core;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	// Flood mute: too many lines refused by the rate gate mute the sender for a few minutes.
	public partial class ChatSystem
	{
		/// <summary>
		/// Lines refused by the rate gate, within <see cref="floodMuteWindowSeconds"/>, that mute the sender.
		/// </summary>
		[Header("Flood Mute")]
		[Tooltip("Lines refused by the chat rate limit, within the window below, that mute the sender. Zero disables the flood mute.")]
		[SerializeField] private int floodMuteRefusalThreshold = 10;

		/// <summary>
		/// Seconds within which <see cref="floodMuteRefusalThreshold"/> refusals mute the sender.
		/// </summary>
		[Tooltip("Seconds within which that many refused lines mute the sender.")]
		[SerializeField] private float floodMuteWindowSeconds = 10.0f;

		/// <summary>
		/// Seconds a flooding sender is muted for.
		/// </summary>
		[Tooltip("Seconds a flooding sender is muted for. Flooding on while muted pushes the end out again.")]
		[SerializeField] private float floodMuteDurationSeconds = 180.0f;

		/// <summary>
		/// Seconds between sweeps of flood states that have nothing left to remember.
		/// </summary>
		private const float FloodMuteSweepIntervalSeconds = 30.0f;

		/// <summary>
		/// Flood state of every character refused by the rate gate recently, keyed by character ID.
		/// Main thread only.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>In memory, and deliberately so.</b> A flood mute is the rate limit escalating, not a
		/// moderation decision: nobody decided it, it lasts minutes, and it must not become a record
		/// against the account. Writing it to the mute columns the game master commands use would do
		/// exactly that, and would cost a database write that a flooding client triggers at will.
		/// </para>
		/// <para>
		/// <b>Kept apart from <c>IPlayerCharacter.ChatMutedUntilTicks</c>.</b> That field mirrors the
		/// stored mutes: the character load sets it from the rows, and every mute or unmute a game
		/// master issues re-reads the rows and overwrites it. A flood mute written there would be
		/// cut short by an unrelated moderation refresh, and would show in the staff console as a
		/// stored mute with nothing stored behind it.
		/// </para>
		/// <para>
		/// <b>Keyed by character ID, not held on the character.</b> An entry is made only for a
		/// sender the gate has refused, so the table is as small as the number of recent flooders;
		/// and it outlives the character object, so logging out and back in on this scene server does
		/// not lift the mute. Moving to another scene server does — the transfer costs more time than
		/// it saves, and the rate gate holds there regardless.
		/// </para>
		/// </remarks>
		private readonly Dictionary<long, ChatFloodMute.State> floodMuteStates = new Dictionary<long, ChatFloodMute.State>();

		/// <summary>Scratch list of spent keys for <see cref="OnPeriodicFloodMuteSweep"/>. Main thread only.</summary>
		private readonly List<long> floodMuteSweepScratch = new List<long>();

		/// <summary>
		/// Clamps the flood-mute settings and forgets every flood state. Called on initialise.
		/// </summary>
		private void InitializeFloodMute()
		{
			floodMuteRefusalThreshold = Mathf.Max(0, floodMuteRefusalThreshold);
			floodMuteWindowSeconds = Mathf.Max(0.1f, floodMuteWindowSeconds);
			floodMuteDurationSeconds = Mathf.Max(0.0f, floodMuteDurationSeconds);

			/* This object outlives a play session in the editor; a previous session's mutes are
			 * not this one's. */
			floodMuteStates.Clear();
			floodMuteSweepScratch.Clear();
		}

		/// <summary>
		/// Records one line the rate gate refused, and mutes the sender when that makes too many.
		/// Main thread (FishNet's receive callback).
		/// </summary>
		/// <param name="conn">The sender's connection, told when the mute starts.</param>
		/// <param name="sender">The sender.</param>
		private void RecordChatRateRefusal(NetworkConnection conn, IPlayerCharacter sender)
		{
			if (sender == null)
			{
				return;
			}

			double now = MonotonicClock.NowSeconds;
			floodMuteStates.TryGetValue(sender.ID, out ChatFloodMute.State state);

			ChatFloodMute.Outcome outcome = ChatFloodMute.RecordRefusal(ref state, now, floodMuteRefusalThreshold, floodMuteWindowSeconds, floodMuteDurationSeconds);
			if (outcome == ChatFloodMute.Outcome.Disabled)
			{
				return;
			}
			floodMuteStates[sender.ID] = state;

			/* Told once, when the mute starts. Every line refused after it is dropped by the gate as
			 * before — answering each would send a flooder one system line per line it floods — and
			 * the lines that do get through the gate are answered by TryRefuseFloodMuted, which says
			 * how long is left. */
			if (outcome == ChatFloodMute.Outcome.Muted)
			{
				OnSendSystemMessage(conn, ChatFloodMute.DescribeForPlayer(state.MutedUntilSeconds, now));
				Log.Info("ChatSystem",
					$"Character '{sender.CharacterName}' ({sender.ID}, account '{sender.Account}') is flood-muted for {floodMuteDurationSeconds:0}s: " +
					$"{floodMuteRefusalThreshold} lines refused by the chat rate limit within {floodMuteWindowSeconds:0.#}s. Nothing is recorded against the account.");
			}
		}

		/// <summary>
		/// Refuses a chat line from a flood-muted sender, telling them how long is left.
		/// </summary>
		/// <param name="conn">The sender's connection.</param>
		/// <param name="sender">The sender.</param>
		/// <returns>True when the line was refused.</returns>
		/// <remarks>
		/// Tested beside the stored mute, after commands: like any mute it silences chat and nothing
		/// else, so /helpme and /report still work. Nothing to look up for anybody never refused.
		/// </remarks>
		private bool TryRefuseFloodMuted(NetworkConnection conn, IPlayerCharacter sender)
		{
			if (floodMuteStates.Count < 1 ||
				!floodMuteStates.TryGetValue(sender.ID, out ChatFloodMute.State state))
			{
				return false;
			}

			double now = MonotonicClock.NowSeconds;
			if (!ChatFloodMute.IsMuted(state, now))
			{
				return false;
			}

			OnSendSystemMessage(conn, ChatFloodMute.DescribeForPlayer(state.MutedUntilSeconds, now));
			return true;
		}

		/// <summary>
		/// Drops flood states with nothing left to remember: no mute running and no window open.
		/// </summary>
		/// <param name="deltaTime">Elapsed seconds (unused).</param>
		private void OnPeriodicFloodMuteSweep(float deltaTime)
		{
			if (floodMuteStates.Count < 1)
			{
				return;
			}

			double now = MonotonicClock.NowSeconds;
			floodMuteSweepScratch.Clear();
			foreach (KeyValuePair<long, ChatFloodMute.State> entry in floodMuteStates)
			{
				if (ChatFloodMute.IsSpent(entry.Value, now, floodMuteWindowSeconds))
				{
					floodMuteSweepScratch.Add(entry.Key);
				}
			}
			for (int i = 0; i < floodMuteSweepScratch.Count; ++i)
			{
				floodMuteStates.Remove(floodMuteSweepScratch[i]);
			}
			floodMuteSweepScratch.Clear();
		}
	}
}
