using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public interface IFriendController : ICharacterBehaviour
	{
		event Action<long, bool> OnAddFriend;
		event Action<long> OnRemoveFriend;
		HashSet<long> Friends { get; }
		void AddFriend(long friendID);
	}
}