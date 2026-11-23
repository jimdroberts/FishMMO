using FishNet.Transporting;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>How a travel spell answers its caster: a toast to the owner, from the server.</summary>
	internal static class TravelSpellFeedback
	{
		/// <summary>Toasts <paramref name="text"/> to <paramref name="player"/>'s owner; nothing for empty text or no live connection.</summary>
		public static void Toast(IPlayerCharacter player, string text, ToastSeverity severity = ToastSeverity.Warning)
		{
			if (string.IsNullOrEmpty(text) || player?.Owner == null || !player.Owner.IsActive)
			{
				return;
			}
			player.Owner.Broadcast(new ToastBroadcast()
			{
				Text = text,
				Severity = severity,
			}, true, Channel.Reliable);
		}

		/// <summary>Whether <paramref name="player"/> stands in an arena match.</summary>
		public static bool InArena(IPlayerCharacter player)
		{
			return player?.GameObject != null && ArenaTeamRegistry.IsArena(player.GameObject.scene.handle);
		}
	}
}
