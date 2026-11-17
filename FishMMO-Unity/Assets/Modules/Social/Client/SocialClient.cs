using FishNet.Transporting;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// What the Social module adds to the client outside its own windows: "Add Friend" on every character's context
	/// menu (in the world, the guild roster and the party roster) and "Invite to Party" on a player in the world.
	/// </summary>
	public static class SocialClient
	{
		/// <summary>The "Add Friend" entry's id in <see cref="CharacterMenuEntries"/>.</summary>
		public const string AddFriendEntryId = "social.add-friend";

		/// <summary>Where "Add Friend" sits among module entries: first, where it always was.</summary>
		public const int AddFriendOrder = 100;

		/// <summary>The "Invite to Party" entry's id in <see cref="CharacterMenuEntries"/>.</summary>
		public const string InviteToPartyEntryId = "social.invite-to-party";

		/// <summary>Right after "Add Friend", where it always was.</summary>
		public const int InviteToPartyOrder = 110;

		/// <summary>Registers the module's client entries. Runs before the first scene loads; safe to run again.</summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void Install()
		{
			// Not on the friend list's own rows: they are friends already.
			CharacterMenuEntries.Register(AddFriendEntryId, "Add Friend", AddFriendOrder, AddFriend,
				target => target.Source != CharacterMenuSource.Friends);
			// On a player in the world only, as before: the roster menus never offered it.
			CharacterMenuEntries.Register(InviteToPartyEntryId, "Invite to Party", InviteToPartyOrder, InviteToParty,
				target => target.Source == CharacterMenuSource.World);
		}

		/// <summary>Asks the server to befriend a character; it refuses yourself, a full list and an unknown id.</summary>
		public static void AddFriend(CharacterMenuTarget target)
		{
			Client.Broadcast(new FriendAddNewBroadcast()
			{
				CharacterID = target.CharacterID,
			}, Channel.Reliable);
		}

		/// <summary>Invites a character to the local player's party (or to a new one); the server reads only the target.</summary>
		public static void InviteToParty(CharacterMenuTarget target)
		{
			Client.Broadcast(new PartyInviteBroadcast()
			{
				TargetCharacterID = target.CharacterID,
			}, Channel.Reliable);
		}
	}
}
