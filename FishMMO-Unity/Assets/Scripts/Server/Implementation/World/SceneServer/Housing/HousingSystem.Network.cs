using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Database.Data;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The wire: turning what a client asks for into the server-side housing operations.
	/// </summary>
	/// <remarks>
	/// Every handler here does the same three things before anything else — find the character
	/// behind the connection, check they are in a state to act, and resolve the plot they named to a
	/// foundation this server actually has loaded. None of those may be taken on trust: a connection
	/// can name any plot ID it likes, including one on a scene server on the other side of the
	/// cluster.
	///
	/// <para>Plots are named by database identity rather than by network object, because a
	/// foundation is a scene object and scene object identifiers are handed out fresh on every load.
	/// One would name a different foundation after a restart — see <see cref="PlotIdentity"/>.</para>
	/// </remarks>
	public partial class HousingSystem
	{
		/// <summary>
		/// Registers the client-facing housing broadcasts.
		/// </summary>
		private void RegisterHousingBroadcasts()
		{
			if (Server?.NetworkWrapper == null)
			{
				return;
			}

			Server.NetworkWrapper.RegisterBroadcast<HousingBeginBuildingBroadcast>(OnServerHousingBeginBuilding, true);
			Server.NetworkWrapper.RegisterBroadcast<HousingEndBuildingBroadcast>(OnServerHousingEndBuilding, true);
			Server.NetworkWrapper.RegisterBroadcast<HousingFinishBuildingBroadcast>(OnServerHousingFinishBuilding, true);
			Server.NetworkWrapper.RegisterBroadcast<HousingPlaceStructureBroadcast>(OnServerHousingPlaceStructure, true);
			Server.NetworkWrapper.RegisterBroadcast<HousingRemoveStructureBroadcast>(OnServerHousingRemoveStructure, true);
			Server.NetworkWrapper.RegisterBroadcast<HousingGrantAccessBroadcast>(OnServerHousingGrantAccess, true);
			Server.NetworkWrapper.RegisterBroadcast<HousingRevokeAccessBroadcast>(OnServerHousingRevokeAccess, true);
			Server.NetworkWrapper.RegisterBroadcast<HousingVaultRequestBroadcast>(OnServerHousingVaultRequest, true);
			Server.NetworkWrapper.RegisterBroadcast<HousingVaultRetrieveBroadcast>(OnServerHousingVaultRetrieve, true);
			Server.NetworkWrapper.RegisterBroadcast<HousingVaultForfeitBroadcast>(OnServerHousingVaultForfeit, true);
		}

		/// <summary>
		/// Releases the client-facing housing broadcasts.
		/// </summary>
		/// <remarks>
		/// Unconditional, like the foundation unsubscribe. A handler left registered on a torn-down
		/// scene server goes on answering requests against state that is no longer being maintained.
		/// </remarks>
		private void UnregisterHousingBroadcasts()
		{
			if (Server?.NetworkWrapper == null)
			{
				return;
			}

			Server.NetworkWrapper.UnregisterBroadcast<HousingBeginBuildingBroadcast>(OnServerHousingBeginBuilding);
			Server.NetworkWrapper.UnregisterBroadcast<HousingEndBuildingBroadcast>(OnServerHousingEndBuilding);
			Server.NetworkWrapper.UnregisterBroadcast<HousingFinishBuildingBroadcast>(OnServerHousingFinishBuilding);
			Server.NetworkWrapper.UnregisterBroadcast<HousingPlaceStructureBroadcast>(OnServerHousingPlaceStructure);
			Server.NetworkWrapper.UnregisterBroadcast<HousingRemoveStructureBroadcast>(OnServerHousingRemoveStructure);
			Server.NetworkWrapper.UnregisterBroadcast<HousingGrantAccessBroadcast>(OnServerHousingGrantAccess);
			Server.NetworkWrapper.UnregisterBroadcast<HousingRevokeAccessBroadcast>(OnServerHousingRevokeAccess);
			Server.NetworkWrapper.UnregisterBroadcast<HousingVaultRequestBroadcast>(OnServerHousingVaultRequest);
			Server.NetworkWrapper.UnregisterBroadcast<HousingVaultRetrieveBroadcast>(OnServerHousingVaultRetrieve);
			Server.NetworkWrapper.UnregisterBroadcast<HousingVaultForfeitBroadcast>(OnServerHousingVaultForfeit);
		}

		/// <summary>
		/// Finds the character behind a connection, if it is in a state to act.
		/// </summary>
		/// <remarks>
		/// <c>CanAct</c> is what keeps housing requests from arriving from a corpse, a character
		/// mid-teleport, or one that is being handed to another scene server — the same gate every
		/// other player-initiated system uses.
		/// </remarks>
		private static bool TryGetActingPlayer(NetworkConnection conn, out IPlayerCharacter player)
		{
			player = null;

			if (conn == null || conn.FirstObject == null)
			{
				return false;
			}

			player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
			{
				player = null;
				return false;
			}

			return true;
		}

		/// <summary>
		/// Finds the copy of a plot that the requesting character is actually standing in.
		/// </summary>
		/// <param name="player">The requesting character.</param>
		/// <param name="plotID">The plot they named.</param>
		/// <param name="foundation">The foundation in their own scene, when there is one.</param>
		/// <remarks>
		/// Their scene, not any scene. Channels are several loaded copies of one plot and this server
		/// may hold more than one of them; acting on whichever turned up first would let a player in
		/// channel one open a build session that locks the plot for everybody in channel two while
		/// they are nowhere near it.
		///
		/// <para>This is also the check that stops a client naming a plot at random. A plot ID that
		/// resolves to nothing in the sender's own scene is one they cannot see, and every request
		/// here is about a foundation the player is supposed to be standing at.</para>
		/// </remarks>
		private static bool TryResolveRequestedPlot(IPlayerCharacter player, long plotID, out PlotFoundation foundation)
		{
			foundation = null;

			if (player == null || plotID <= 0)
			{
				return false;
			}

			Transform transform = player.Transform;
			if (transform == null)
			{
				return false;
			}

			foreach (PlotFoundation candidate in PlotFoundation.Registry.ForScene(transform.gameObject.scene.handle))
			{
				if (candidate != null && candidate.PlotID == plotID)
				{
					foundation = candidate;
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Tells one client how a request went.
		/// </summary>
		private void SendHousingResult(NetworkConnection conn, long plotID, HousingResult result)
		{
			if (conn == null || !conn.IsActive || Server?.NetworkWrapper == null)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new HousingResultBroadcast
			{
				PlotID = plotID,
				Result = result,
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Opens a build session.
		/// </summary>
		private void OnServerHousingBeginBuilding(NetworkConnection conn, HousingBeginBuildingBroadcast msg, Channel channel)
		{
			if (!TryGetActingPlayer(conn, out IPlayerCharacter player))
			{
				return;
			}
			if (!IsHousingEnabled)
			{
				SendHousingResult(conn, msg.PlotID, HousingResult.Disabled);
				return;
			}
			if (!TryResolveRequestedPlot(player, msg.PlotID, out PlotFoundation foundation))
			{
				SendHousingResult(conn, msg.PlotID, HousingResult.UnknownPlot);
				return;
			}

			SendHousingResult(conn, msg.PlotID,
				TryBeginBuilding(player, foundation) ? HousingResult.Success : HousingResult.NotTheOwner);
		}

		/// <summary>
		/// Closes a build session.
		/// </summary>
		private void OnServerHousingEndBuilding(NetworkConnection conn, HousingEndBuildingBroadcast msg, Channel channel)
		{
			if (!TryGetActingPlayer(conn, out IPlayerCharacter player))
			{
				return;
			}
			if (!TryResolveRequestedPlot(player, msg.PlotID, out PlotFoundation foundation))
			{
				SendHousingResult(conn, msg.PlotID, HousingResult.UnknownPlot);
				return;
			}

			SendHousingResult(conn, msg.PlotID,
				TryEndBuilding(player, foundation) ? HousingResult.Success : HousingResult.NotTheOwner);
		}

		/// <summary>
		/// Declares a house finished.
		/// </summary>
		private void OnServerHousingFinishBuilding(NetworkConnection conn, HousingFinishBuildingBroadcast msg, Channel channel)
		{
			if (!TryGetActingPlayer(conn, out IPlayerCharacter player))
			{
				return;
			}
			if (!IsHousingEnabled)
			{
				SendHousingResult(conn, msg.PlotID, HousingResult.Disabled);
				return;
			}
			if (!TryResolveRequestedPlot(player, msg.PlotID, out PlotFoundation foundation))
			{
				SendHousingResult(conn, msg.PlotID, HousingResult.UnknownPlot);
				return;
			}

			if (foundation.State != PlotState.Building)
			{
				SendHousingResult(conn, msg.PlotID, HousingResult.WrongState);
				return;
			}

			SendHousingResult(conn, msg.PlotID,
				TryFinishBuilding(player, foundation) ? HousingResult.Success : HousingResult.NotTheOwner);
		}

		/// <summary>
		/// Places a structure.
		/// </summary>
		private void OnServerHousingPlaceStructure(NetworkConnection conn, HousingPlaceStructureBroadcast msg, Channel channel)
		{
			if (!TryGetActingPlayer(conn, out IPlayerCharacter player))
			{
				return;
			}
			if (!IsHousingEnabled)
			{
				SendHousingResult(conn, msg.PlotID, HousingResult.Disabled);
				return;
			}
			if (!TryResolveRequestedPlot(player, msg.PlotID, out PlotFoundation foundation))
			{
				SendHousingResult(conn, msg.PlotID, HousingResult.UnknownPlot);
				return;
			}

			PlaceStructure(conn, player, foundation, msg.TemplateID, msg.LocalPosition, msg.Yaw);
		}

		/// <summary>
		/// Removes a structure.
		/// </summary>
		private void OnServerHousingRemoveStructure(NetworkConnection conn, HousingRemoveStructureBroadcast msg, Channel channel)
		{
			if (!TryGetActingPlayer(conn, out IPlayerCharacter player))
			{
				return;
			}
			if (!IsHousingEnabled)
			{
				SendHousingResult(conn, msg.PlotID, HousingResult.Disabled);
				return;
			}
			if (!TryResolveRequestedPlot(player, msg.PlotID, out PlotFoundation foundation))
			{
				SendHousingResult(conn, msg.PlotID, HousingResult.UnknownPlot);
				return;
			}

			RemoveStructure(conn, player, foundation, msg.StructureID);
		}

		/// <summary>
		/// Grants somebody access.
		/// </summary>
		private void OnServerHousingGrantAccess(NetworkConnection conn, HousingGrantAccessBroadcast msg, Channel channel)
		{
			if (!TryGetActingPlayer(conn, out IPlayerCharacter player))
			{
				return;
			}
			if (!IsHousingEnabled)
			{
				SendHousingResult(conn, msg.PlotID, HousingResult.Disabled);
				return;
			}
			if (!TryResolveRequestedPlot(player, msg.PlotID, out PlotFoundation foundation))
			{
				SendHousingResult(conn, msg.PlotID, HousingResult.UnknownPlot);
				return;
			}

			/* Masked here, at the edge, before it reaches anything that reads it. The number came off
			 * the wire and may carry bits this build has no name for. */
			PlotPermission requested = PlotAccess.Sanitize(msg.Permissions);

			bool granted = TryGrantAccess(player, foundation, msg.CharacterID, requested);
			SendHousingResult(conn, msg.PlotID, granted ? HousingResult.Success : HousingResult.NotPermitted);

			if (granted)
			{
				SendAccessList(conn, foundation);
			}
		}

		/// <summary>
		/// Takes somebody's access away.
		/// </summary>
		private void OnServerHousingRevokeAccess(NetworkConnection conn, HousingRevokeAccessBroadcast msg, Channel channel)
		{
			if (!TryGetActingPlayer(conn, out IPlayerCharacter player))
			{
				return;
			}
			if (!IsHousingEnabled)
			{
				SendHousingResult(conn, msg.PlotID, HousingResult.Disabled);
				return;
			}
			if (!TryResolveRequestedPlot(player, msg.PlotID, out PlotFoundation foundation))
			{
				SendHousingResult(conn, msg.PlotID, HousingResult.UnknownPlot);
				return;
			}

			bool revoked = TryRevokeAccess(player, foundation, msg.CharacterID);
			SendHousingResult(conn, msg.PlotID, revoked ? HousingResult.Success : HousingResult.NotPermitted);

			if (revoked)
			{
				/* Evicted immediately rather than waiting for the sweep. The revoked friend may be
				 * standing in the house right now, and the owner who just shut them out should not
				 * watch them linger for half a second afterwards. */
				EvictTrespassers(foundation);
				SendAccessList(conn, foundation);
			}
		}

		/// <summary>
		/// Sends a plot's guest list to somebody entitled to see it.
		/// </summary>
		/// <remarks>
		/// Entitlement is re-checked here rather than assumed from the caller. This is the one place
		/// housing sends a list of names, and the list belongs to the owner: anyone who cannot invite
		/// has no business reading who else has been invited.
		/// </remarks>
		private void SendAccessList(NetworkConnection conn, PlotFoundation foundation)
		{
			if (conn == null || !conn.IsActive || foundation == null || Server?.NetworkWrapper == null)
			{
				return;
			}

			if (!TryGetActingPlayer(conn, out IPlayerCharacter player))
			{
				return;
			}

			if (!foundation.PermissionsFor(player.ID, GuildIDOf(player)).HasFlag(PlotPermission.InviteFriends))
			{
				return;
			}

			IReadOnlyDictionary<long, PlotPermission> grants = foundation.AccessGrants;
			HousingAccessEntry[] entries = new HousingAccessEntry[grants.Count];

			int i = 0;
			foreach (KeyValuePair<long, PlotPermission> grant in grants)
			{
				entries[i++] = new HousingAccessEntry
				{
					CharacterID = grant.Key,
					Permissions = (int)grant.Value,
				};
			}

			Server.NetworkWrapper.Broadcast(conn, new HousingAccessListBroadcast
			{
				PlotID = foundation.PlotID,
				Entries = entries,
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Sends the sender their house vault.
		/// </summary>
		private void OnServerHousingVaultRequest(NetworkConnection conn, HousingVaultRequestBroadcast msg, Channel channel)
		{
			if (!TryGetActingPlayer(conn, out IPlayerCharacter player))
			{
				return;
			}
			if (!IsHousingEnabled)
			{
				SendHousingResult(conn, 0, HousingResult.Disabled);
				return;
			}

			FetchVault(player, (entries, fees) => SendVault(conn, entries, fees));
		}

		/// <summary>
		/// Sends one character's vault contents, with today's fees.
		/// </summary>
		private void SendVault(NetworkConnection conn, List<PlotVaultData> entries, List<long> fees)
		{
			if (conn == null || !conn.IsActive || entries == null || fees == null || Server?.NetworkWrapper == null)
			{
				return;
			}

			int count = Mathf.Min(entries.Count, fees.Count);
			HousingVaultEntry[] wire = new HousingVaultEntry[count];

			for (int i = 0; i < count; ++i)
			{
				wire[i] = new HousingVaultEntry
				{
					VaultID = entries[i].ID,
					TemplateID = entries[i].TemplateID,
					Amount = entries[i].Amount,
					OriginalPlotID = entries[i].OriginalPlotID,
					Fee = fees[i],
				};
			}

			Server.NetworkWrapper.Broadcast(conn, new HousingVaultBroadcast { Entries = wire }, true, Channel.Reliable);
		}

		/// <summary>
		/// Buys one stack back out of the vault.
		/// </summary>
		private void OnServerHousingVaultRetrieve(NetworkConnection conn, HousingVaultRetrieveBroadcast msg, Channel channel)
		{
			if (!TryGetActingPlayer(conn, out IPlayerCharacter player))
			{
				return;
			}
			if (!IsHousingEnabled)
			{
				SendHousingResult(conn, 0, HousingResult.Disabled);
				return;
			}

			RetrieveFromVault(player, msg.VaultID);
		}

		/// <summary>
		/// Gives one stack up permanently.
		/// </summary>
		private void OnServerHousingVaultForfeit(NetworkConnection conn, HousingVaultForfeitBroadcast msg, Channel channel)
		{
			if (!TryGetActingPlayer(conn, out IPlayerCharacter player))
			{
				return;
			}
			if (!IsHousingEnabled)
			{
				SendHousingResult(conn, 0, HousingResult.Disabled);
				return;
			}

			ForfeitFromVault(player, msg.VaultID);
		}
	}
}
