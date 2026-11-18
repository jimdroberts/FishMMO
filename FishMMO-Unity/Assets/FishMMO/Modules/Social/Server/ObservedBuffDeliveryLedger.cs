using System.Collections.Generic;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Which observed-buff set each party vitals RECIPIENT was last sent for each member it sees.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The vitals pump omits a member's buff array when it has not changed, and the client keeps
	/// the last array it was given (<c>PartyMemberVitalsEntry.BuffsChanged</c>). Whether an array
	/// may be omitted is therefore a question about what the RECIPIENT holds, not about the member
	/// described. The record used to be keyed by the described member alone and cleared only when
	/// that member disconnected, so anybody who had not been in the audience when a set was last
	/// sent never received it: a member joining the party, or walking into the scene where the
	/// others stand, saw an empty strip for somebody wearing a permanent buff for as long as that
	/// buff did not change. Timed buffs hid the defect, because their signature moves every second.
	/// </para>
	/// <para>
	/// The rule, per member described in one scene group's payload: the array goes out when ANY
	/// recipient in the group was not last sent this member's current signature. The payload is
	/// one message for the whole group, so everybody in it then receives the array, and everybody
	/// is recorded as holding it.
	/// </para>
	/// <para>
	/// A recipient is forgotten whenever its client may have dropped what it held: when it
	/// disconnects, and whenever the party pump delivers it a roster or evicts it, because the
	/// client rebuilds member rows from a roster change and a rebuilt row starts with no buffs.
	/// </para>
	/// <para>
	/// Plain C# with no Unity or network dependency, so the rule is testable on its own.
	/// Main-thread only, like the vitals pump that owns it.
	/// </para>
	/// </remarks>
	internal sealed class ObservedBuffDeliveryLedger
	{
		/// <summary>Per recipient: described member → the signature that recipient was last sent.</summary>
		private readonly Dictionary<long, Dictionary<long, int>> sentByRecipient = new Dictionary<long, Dictionary<long, int>>();

		/// <summary>Spare per-recipient maps, so a recipient being forgotten allocates nothing next time.</summary>
		private readonly Stack<Dictionary<long, int>> pool = new Stack<Dictionary<long, int>>();

		/// <summary>The number of recipients with a record. For tests and diagnostics.</summary>
		internal int RecipientCount => sentByRecipient.Count;

		/// <summary>
		/// True when any of <paramref name="recipientIDs"/> was not last sent <paramref name="signature"/>
		/// for <paramref name="describedID"/>.
		/// </summary>
		/// <param name="recipientIDs">Character IDs of everybody the payload will reach.</param>
		/// <param name="describedID">The member whose buffs are being described.</param>
		/// <param name="signature">The member's current buff-set signature.</param>
		/// <returns>True when the array must be included in the payload.</returns>
		internal bool NeedsSend(IReadOnlyList<long> recipientIDs, long describedID, int signature)
		{
			if (recipientIDs == null)
			{
				return false;
			}

			for (int i = 0; i < recipientIDs.Count; ++i)
			{
				if (!sentByRecipient.TryGetValue(recipientIDs[i], out Dictionary<long, int> sent) ||
					!sent.TryGetValue(describedID, out int last) ||
					last != signature)
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Records that every one of <paramref name="recipientIDs"/> now holds <paramref name="signature"/>
		/// for <paramref name="describedID"/>.
		/// </summary>
		/// <param name="recipientIDs">Character IDs of everybody the payload reaches.</param>
		/// <param name="describedID">The member whose buffs were sent.</param>
		/// <param name="signature">The signature of the set that was sent.</param>
		internal void MarkSent(IReadOnlyList<long> recipientIDs, long describedID, int signature)
		{
			if (recipientIDs == null)
			{
				return;
			}

			for (int i = 0; i < recipientIDs.Count; ++i)
			{
				long recipientID = recipientIDs[i];
				if (!sentByRecipient.TryGetValue(recipientID, out Dictionary<long, int> sent))
				{
					sent = pool.Count > 0 ? pool.Pop() : new Dictionary<long, int>();
					sentByRecipient.Add(recipientID, sent);
				}
				sent[describedID] = signature;
			}
		}

		/// <summary>
		/// Forgets everything a recipient was sent, so every member it sees is sent to it again.
		/// </summary>
		/// <param name="recipientID">The recipient's character ID.</param>
		internal void ForgetRecipient(long recipientID)
		{
			if (sentByRecipient.TryGetValue(recipientID, out Dictionary<long, int> sent))
			{
				sentByRecipient.Remove(recipientID);
				sent.Clear();
				pool.Push(sent);
			}
		}

		/// <summary>
		/// Forgets every recipient.
		/// </summary>
		internal void Clear()
		{
			foreach (KeyValuePair<long, Dictionary<long, int>> entry in sentByRecipient)
			{
				entry.Value.Clear();
				pool.Push(entry.Value);
			}
			sentByRecipient.Clear();
		}
	}
}
