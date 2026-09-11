using FishMMO.Database.Data;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Whether a character's stored row may be written from the panel.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A live character's row is not authoritative. Its state lives in the owning scene
	/// server's memory, and the persistence pass writes that memory over the row — name
	/// included. So an edit made here while a server holds the lease is not merely racy, it is
	/// reliably lost on the next save.
	/// </para>
	/// <para>
	/// The lease is the test, not the online flag: a server that crashed leaves
	/// <c>session_state = Online</c> behind, and waiting for that to be cleaned up by hand
	/// would lock the character out of the editor forever. An expired lease is no lease.
	/// </para>
	/// </remarks>
	public sealed class CharacterEditLock
	{
		/// <summary>Whether the panel may write to this row.</summary>
		public bool Editable { get; init; }

		/// <summary>Why not: <c>offline</c>, <c>lease</c> or <c>deleted</c>.</summary>
		public string Reason { get; init; } = "offline";

		/// <summary>Human-readable explanation, shown to the operator as-is.</summary>
		public string Message { get; init; } = "";

		/// <summary>The scene server holding the lease, when one does.</summary>
		public long OwnerServerId { get; init; }

		/// <summary>When the lease lapses, when one is held.</summary>
		public DateTime? LeaseExpiresUtc { get; init; }

		/// <summary>Computes the lock for a character.</summary>
		public static CharacterEditLock For(CharacterAdminData character)
		{
			if (character.Deleted)
			{
				return new CharacterEditLock
				{
					Editable = false,
					Reason = "deleted",
					Message = "This character is deleted. Restore it before editing; it can still be renamed.",
				};
			}

			// CharacterSessionState.Online is 1. The lease has to still be running for the
			// state to mean anything.
			bool leaseLive = character.SessionState == 1 && character.SessionLeaseExpiresUtc > DateTime.UtcNow;
			if (leaseLive)
			{
				return new CharacterEditLock
				{
					Editable = false,
					Reason = "lease",
					OwnerServerId = character.SessionOwnerServerID,
					LeaseExpiresUtc = character.SessionLeaseExpiresUtc,
					Message =
						"This character holds a live session lease. Its state lives in the owning scene " +
						"server's memory and would overwrite anything written here on the next save.",
				};
			}

			return new CharacterEditLock
			{
				Editable = true,
				Reason = "offline",
				Message = "Offline and safe to edit.",
			};
		}
	}
}
