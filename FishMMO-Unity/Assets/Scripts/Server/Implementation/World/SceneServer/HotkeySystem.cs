using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using System.Collections.Generic;

namespace FishMMO.Server.Implementation.SceneServer
{
	public class HotkeySystem : ServerBehaviour
	{
		/// <summary>
		/// Initializes the hotkey system, registering broadcast handlers for hotkey set and hotkey set multiple requests.
		/// </summary>
		public override void InitializeOnce()
		{
			if (ServerManager != null &&
				Server != null)
			{
				Server.NetworkWrapper.RegisterBroadcast<HotkeySetBroadcast>(OnServerHotkeySetBroadcastReceived, true);
				Server.NetworkWrapper.RegisterBroadcast<HotkeySetMultipleBroadcast>(OnServerHotkeySetMultipleBroadcastReceived, true);
			}
			else
			{
				enabled = false;
			}
		}

		/// <summary>
		/// Cleans up the hotkey system, unregistering broadcast handlers.
		/// </summary>
		public override void Destroying()
		{
			if (ServerManager != null &&
				Server != null)
			{
				Server.NetworkWrapper.UnregisterBroadcast<HotkeySetBroadcast>(OnServerHotkeySetBroadcastReceived);
				Server.NetworkWrapper.UnregisterBroadcast<HotkeySetMultipleBroadcast>(OnServerHotkeySetMultipleBroadcastReceived);
			}
		}

		/// <summary>
		/// Handles broadcast to set a single hotkey for a player character.
		/// Validates the hotkey list and slot, then updates the hotkey data for the specified slot.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="msg">HotkeySetBroadcast message containing hotkey data.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerHotkeySetBroadcastReceived(NetworkConnection conn, HotkeySetBroadcast msg, Channel channel)
		{
			if (Server.CoreServer.NpgsqlDbContextFactory == null)
			{
				return;
			}
			if (conn.FirstObject == null)
			{
				return;
			}
			IPlayerCharacter playerCharacter = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (playerCharacter == null)
			{
				return;
			}
			// Validate the hotkey list exists
			if (playerCharacter.Hotkeys == null)
			{
				playerCharacter.Hotkeys = new List<HotkeyData>();
				for (int i = 0; i < Constants.Configuration.MaximumPlayerHotkeys; ++i)
				{
					playerCharacter.Hotkeys.Add(new HotkeyData()
					{
						Slot = i,
					});
				}
			}
			// Validate the hotkey slot
			if (playerCharacter.Hotkeys.Count < msg.HotkeyData.Slot ||
				msg.HotkeyData.Slot < 0)
			{
				return;
			}
			HotkeyData hotkeyData = new HotkeyData()
			{
				Type = msg.HotkeyData.Type,
				Slot = msg.HotkeyData.Slot,
				ReferenceID = msg.HotkeyData.ReferenceID,
			};
			playerCharacter.Hotkeys[msg.HotkeyData.Slot] = hotkeyData;
		}

		/// <summary>
		/// Handles broadcast to set multiple hotkeys for a player character.
		/// Iterates through each hotkey message, validates the hotkey list and slot, then updates the hotkey data for each slot.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="msg">HotkeySetMultipleBroadcast message containing multiple hotkey data entries.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerHotkeySetMultipleBroadcastReceived(NetworkConnection conn, HotkeySetMultipleBroadcast msg, Channel channel)
		{
			if (Server.CoreServer.NpgsqlDbContextFactory == null)
			{
				return;
			}
			if (conn.FirstObject == null)
			{
				return;
			}
			IPlayerCharacter playerCharacter = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (playerCharacter == null)
			{
				return;
			}
			foreach (HotkeySetBroadcast subMsg in msg.Hotkeys)
			{
				// Validate the hotkey list exists
				if (playerCharacter.Hotkeys == null)
				{
					playerCharacter.Hotkeys = new List<HotkeyData>();
					for (int i = 0; i < Constants.Configuration.MaximumPlayerHotkeys; ++i)
					{
						playerCharacter.Hotkeys.Add(new HotkeyData()
						{
							Slot = i,
						});
					}
				}
				// Validate the hotkey slot
				if (playerCharacter.Hotkeys.Count < subMsg.HotkeyData.Slot ||
					subMsg.HotkeyData.Slot < 0)
				{
					return;
				}
				HotkeyData hotkeyData = new HotkeyData()
				{
					Type = subMsg.HotkeyData.Type,
					Slot = subMsg.HotkeyData.Slot,
					ReferenceID = subMsg.HotkeyData.ReferenceID,
				};
				playerCharacter.Hotkeys[subMsg.HotkeyData.Slot] = hotkeyData;
			}
		}
	}
}