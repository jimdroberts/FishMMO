using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using System.Collections.Generic;

namespace FishMMO.Server
{
	public class HotkeySystem : ServerBehaviour
	{
		public override void InitializeOnce()
		{
			if (ServerManager != null &&
                Server != null)
			{
                Server.RegisterBroadcast<HotkeySetBroadcast>(OnServerHotkeySetBroadcastReceived, true);
                Server.RegisterBroadcast<HotkeySetMultipleBroadcast>(OnServerHotkeySetMultipleBroadcastReceived, true);
			}
			else
			{
				enabled = false;
			}
		}

		public override void Destroying()
		{
			if (ServerManager != null &&
                Server != null)
			{
                Server.UnregisterBroadcast<HotkeySetBroadcast>(OnServerHotkeySetBroadcastReceived);
                Server.UnregisterBroadcast<HotkeySetMultipleBroadcast>(OnServerHotkeySetMultipleBroadcastReceived);
			}
		}

        public void OnServerHotkeySetBroadcastReceived(NetworkConnection conn, HotkeySetBroadcast msg, Channel channel)
		{
			if (Server.NpgsqlDbContextFactory == null)
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

        public void OnServerHotkeySetMultipleBroadcastReceived(NetworkConnection conn, HotkeySetMultipleBroadcast msg, Channel channel)
		{
            if (Server.NpgsqlDbContextFactory == null)
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
				// Validate the hotkey slot
				if (playerCharacter.Hotkeys.Count < subMsg.HotkeyData.Slot ||
					subMsg.HotkeyData.Slot < 0)
				{
					continue;
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
