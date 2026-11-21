using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Character broadcast helpers: non-DB data broadcasts (inventory, bank), and targeted character broadcasts.
	/// </summary>
	public partial class CharacterSystem
	{
		/// <summary>
		/// Sends non-database character data (inventory, bank) to the owner. Module data (abilities, achievements, quests, hotkeys, …) is sent by the module's own system.
		/// Must be called on the main thread.
		/// </summary>
		private void SendNonDbCharacterData(IPlayerCharacter character)
		{
			if (character == null || character.Owner == null || !character.Owner.IsActive)
			{
				return;
			}

			#region InventoryItems
			if (character.TryGet(out IInventoryController inventoryController))
			{
				List<InventorySetItemBroadcast> itemBroadcasts = new List<InventorySetItemBroadcast>();

				foreach (Item item in inventoryController.Items)
				{
					// just in case..
					if (item == null)
					{
						continue;
					}
					// create the new item broadcast
					itemBroadcasts.Add(new InventorySetItemBroadcast()
					{
						InstanceID = item.ID,
						TemplateID = item.Template.ID,
						Slot = item.Slot,
						Seed = item.IsGenerated ? item.Generator.Seed : 0,
						StackSize = item.IsStackable ? item.Stackable.Amount : 0,
					});
				}

				// tell the client they have items
				if (itemBroadcasts.Count > 0)
				{
					Server.NetworkWrapper.Broadcast(character.Owner, new InventorySetMultipleItemsBroadcast()
					{
						Items = itemBroadcasts.ToArray(),
					}, true, Channel.Reliable);
				}
			}
			#endregion

			#region BankItems
			if (character.TryGet(out IBankController bankController))
			{
				List<BankSetItemBroadcast> itemBroadcasts = new List<BankSetItemBroadcast>();

				foreach (Item item in bankController.Items)
				{
					// just in case..
					if (item == null)
					{
						continue;
					}
					// create the new item broadcast
					itemBroadcasts.Add(new BankSetItemBroadcast()
					{
						InstanceID = item.ID,
						TemplateID = item.Template.ID,
						Slot = item.Slot,
						Seed = item.IsGenerated ? item.Generator.Seed : 0,
						StackSize = item.IsStackable ? item.Stackable.Amount : 0,
					});
				}

				// tell the client they have items
				if (itemBroadcasts.Count > 0)
				{
					Server.NetworkWrapper.Broadcast(character.Owner, new BankSetMultipleItemsBroadcast()
					{
						Items = itemBroadcasts.ToArray(),
					}, true, Channel.Reliable);
				}
			}
			#endregion
		}

		/// <summary>
		/// Allows sending a broadcast to a specific character by their character name.
		/// Returns true if the broadcast was sent successfully, false otherwise.
		/// </summary>
		/// <typeparam name="T">Type of broadcast message.</typeparam>
		/// <param name="characterName">Name of the character to send to.</param>
		/// <param name="msg">Broadcast message to send.</param>
		public bool SendBroadcastToCharacter<T>(string characterName, T msg) where T : struct, IBroadcast
		{
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				mappingData.CharactersByLowerCaseName.TryGetValue(characterName.ToLowerInvariant(), out var character))
			{
				Server.NetworkWrapper.Broadcast(character.Owner, msg);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Allows sending a broadcast to a specific character by their character ID.
		/// Returns true if the broadcast was sent successfully, false otherwise.
		/// </summary>
		/// <typeparam name="T">Type of broadcast message.</typeparam>
		/// <param name="characterID">ID of the character to send to.</param>
		/// <param name="msg">Broadcast message to send.</param>
		public bool SendBroadcastToCharacter<T>(long characterID, T msg) where T : struct, IBroadcast
		{
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				mappingData.CharactersByID.TryGetValue(characterID, out var character))
			{
				Server.NetworkWrapper.Broadcast(character.Owner, msg);
				return true;
			}
			return false;
		}
	}
}