using System;
using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// A character's guild membership, as the rest of the game reads it: which guild, who is in it, and the guild text
	/// the chat shows.
	/// </summary>
	/// <remarks>
	/// The core contract of the Social module's guild. Read-only: chat, the map, nameplates,
	/// housing access, the harm rule and observer streaming ask "which guild?" and "same guild?", and none of them change
	/// it. Joining, ranks, the roster window and the guild system are the module's (<c>IGuildMembership</c>). Without
	/// the module no character has this, and every one of those reads finds no guild.
	/// </remarks>
	public interface IGuildController : ICharacterBehaviour
	{
		/// <summary>Client: a character's guild ID was read or changed (its nameplate shows the guild's name).</summary>
		static Action<long, IPlayerCharacter> OnReadID;

		/// <summary>The guild's ID; 0 when the character is in no guild.</summary>
		long ID { get; }

		/// <summary>Client: a member was added to the local character's roster, or their row changed (character ID).</summary>
		event Action<long> OnGuildMemberAdded;

		/// <summary>Client: the complete set of member IDs, so a listener can drop anyone not in it.</summary>
		event Action<HashSet<long>> OnValidateGuildMembers;

		/// <summary>Client: a member left the local character's guild.</summary>
		event Action<long> OnRemoveGuildMember;

		/// <summary>Client: the local character left its guild.</summary>
		event Action OnLeaveGuild;

		/// <summary>Client: the guild's info arrived (guild ID, name, description, message of the day).</summary>
		event Action<long, string, string, string> OnReceiveGuildInfo;

		/// <summary>The guild the cached info is about; 0 until it arrives.</summary>
		long InfoGuildID { get; }

		/// <summary>The cached message of the day (the chat shows it on login).</summary>
		string MessageOfTheDay { get; }
	}
}
