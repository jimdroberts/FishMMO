using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The plot panel: what a player is told when they walk up to a deed post, and the requests the
	/// panel sends back — buy at the quoted price, finish building, give the land up.
	/// </summary>
	/// <remarks>
	/// <para>Walking up to a post no longer buys anything. It used to: the interaction itself was the
	/// claim, so the interact key spent money with no price on screen. Now the post asks for a
	/// description (<see cref="PlotFoundation.Registry.OnPanelRequested"/>) and the purchase is its own
	/// request, carrying the price the panel showed, refused if that is no longer the price.</para>
	///
	/// <para>The description is read from the database, not just from the foundation: the owner's
	/// name, the rent dates and whether the asker already owns land somewhere are not held by any
	/// scene object. A read that fails still answers, with what the foundation knows.</para>
	/// </remarks>
	public partial class HousingSystem
	{
		/// <summary>How far past the post's interaction range a panel request is still honoured, metres.</summary>
		private const float PanelReachSlackMetres = 4f;

		private void RegisterPanelBroadcasts()
		{
			if (Server?.NetworkWrapper == null)
			{
				return;
			}
			Server.NetworkWrapper.RegisterBroadcast<HousingPlotInfoRequestBroadcast>(OnServerHousingPlotInfoRequest, true);
			Server.NetworkWrapper.RegisterBroadcast<HousingClaimBroadcast>(OnServerHousingClaim, true);
			Server.NetworkWrapper.RegisterBroadcast<HousingReleaseBroadcast>(OnServerHousingRelease, true);
		}

		private void UnregisterPanelBroadcasts()
		{
			if (Server?.NetworkWrapper == null)
			{
				return;
			}
			Server.NetworkWrapper.UnregisterBroadcast<HousingPlotInfoRequestBroadcast>(OnServerHousingPlotInfoRequest);
			Server.NetworkWrapper.UnregisterBroadcast<HousingClaimBroadcast>(OnServerHousingClaim);
			Server.NetworkWrapper.UnregisterBroadcast<HousingReleaseBroadcast>(OnServerHousingRelease);
		}

		/// <summary>
		/// A player interacted with a deed post.
		/// </summary>
		private void Registry_OnPanelRequested(IPlayerCharacter player, IPlotFoundation foundation)
		{
			NetworkConnection conn = player?.Owner;
			if (conn == null || foundation is not PlotFoundation plot)
			{
				return;
			}
			if (!IsHousingEnabled)
			{
				SendHousingResult(conn, plot.PlotID, HousingResult.Disabled);
				return;
			}
			if (plot.PlotID <= 0)
			{
				// The scene's plots have not been read back from the database yet.
				SendHousingResult(conn, 0, HousingResult.UnknownPlot);
				return;
			}
			SendPlotInfo(conn, player, plot);
		}

		private void OnServerHousingPlotInfoRequest(NetworkConnection conn, HousingPlotInfoRequestBroadcast msg, Channel channel)
		{
			if (!TryDebounceHousingRequest(conn, HousingOperation.PlotInfo))
			{
				return;
			}
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
			SendPlotInfo(conn, player, foundation);
		}

		private void OnServerHousingClaim(NetworkConnection conn, HousingClaimBroadcast msg, Channel channel)
		{
			if (!TryDebounceHousingRequest(conn, HousingOperation.Claim))
			{
				return;
			}
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
			if (!IsNearPost(player, foundation))
			{
				SendHousingResult(conn, msg.PlotID, HousingResult.TooFar);
				return;
			}
			if (msg.QuotedPrice != foundation.Price)
			{
				SendHousingResult(conn, msg.PlotID, HousingResult.PriceChanged);
				return;
			}

			HousingResult started = RequestClaim(conn, player, foundation);
			if (started != HousingResult.Success)
			{
				// Accepted claims are answered once the database has replied (AnswerClaim).
				SendHousingResult(conn, msg.PlotID, started);
			}
		}

		private void OnServerHousingRelease(NetworkConnection conn, HousingReleaseBroadcast msg, Channel channel)
		{
			if (!TryDebounceHousingRequest(conn, HousingOperation.Release))
			{
				return;
			}
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
			if (foundation.Owner.Type != PlotOwnerType.Character || foundation.Owner.ID != player.ID)
			{
				SendHousingResult(conn, msg.PlotID, HousingResult.NotTheOwner);
				return;
			}

			// A session cannot outlive the ownership it was opened under.
			TryEndBuilding(player, foundation);

			long plotID = foundation.PlotID;
			long characterID = player.ID;
			/* Bare land goes back to being a lot; land with something standing on it reads as a house
			 * somebody left, the same as a reclamation — its contents go to the vault either way. */
			PlotState releasedState = structuresByPlot.TryGetValue(plotID, out List<PlotStructureData> standing) && standing != null && standing.Count > 0
				? PlotState.Abandoned
				: PlotState.Empty;

			if (!TryEnqueueAsyncWork(() => ReleaseByOwnerAsync(conn, player, plotID, characterID, releasedState), characterID))
			{
				SendHousingResult(conn, plotID, HousingResult.Failed);
			}
		}

		/// <summary>
		/// Gives a plot up for its owner: the release, the vault move and the end of the guest list in
		/// one transaction (<see cref="TryReleaseIntoVaultAsync"/>), pinned to this owner.
		/// </summary>
		private async Task ReleaseByOwnerAsync(NetworkConnection conn, IPlayerCharacter player, long plotID, long characterID, PlotState releasedState)
		{
			if (!TryGetDbService(out IPlotService plotService))
			{
				SendHousingResultOnMainThread(conn, plotID, HousingResult.Failed);
				return;
			}

			DatabaseResult<List<PlotData>> read = await plotService.FetchByIdsAsync(new[] { plotID });
			if (!read.IsSuccess || read.Data == null || read.Data.Count != 1)
			{
				SendHousingResultOnMainThread(conn, plotID, HousingResult.Failed);
				return;
			}
			PlotData plot = read.Data[0];
			if (plot.OwnerCharacterID != characterID)
			{
				SendHousingResultOnMainThread(conn, plotID, HousingResult.NotTheOwner);
				return;
			}

			if (!await TryReleaseIntoVaultAsync(plotService, plot, releasedState))
			{
				SendHousingResultOnMainThread(conn, plotID, HousingResult.Failed);
				return;
			}

			Log.Debug("HousingSystem", $"CharID={characterID} gave up plot {plotID} ({releasedState}).");
			MarkPlotChanged(plotID);

			if (!TryEnqueueHousingMainThread(() =>
			{
				ForgetPlotContents(plotID);
				foreach (PlotFoundation copy in PlotFoundation.Registry.ForPlot(plotID))
				{
					copy.ApplyOwner(PlotOwner.None);
					copy.ApplyState(releasedState);
					copy.ApplyAccessGrants(new Dictionary<long, PlotPermission>());
				}
				SendHousingResult(conn, plotID, HousingResult.Success);
				if (TryResolveRequestedPlot(player, plotID, out PlotFoundation here))
				{
					SendPlotInfo(conn, player, here);
				}
			}))
			{
				Log.Warning("HousingSystem", $"Could not apply the release of plot {plotID} locally; the plot sync applies it.");
			}
		}

		/// <summary>
		/// Answers a claim on the main thread: the result, and on success the plot described again.
		/// </summary>
		private void AnswerClaim(NetworkConnection conn, IPlayerCharacter player, IPlotFoundation foundation, long plotID, HousingResult result)
		{
			if (conn == null)
			{
				return;
			}
			SendHousingResult(conn, plotID, result);
			if (result == HousingResult.Success && foundation is PlotFoundation plot && player != null)
			{
				SendPlotInfo(conn, player, plot);
			}
		}

		/// <summary>
		/// True when the player is still standing by the plot's deed post.
		/// </summary>
		/// <remarks>
		/// A little looser than the interaction range: the panel was opened at the post, and a player
		/// who stepped back while reading it should not be refused for that.
		/// </remarks>
		private static bool IsNearPost(IPlayerCharacter player, PlotFoundation foundation)
		{
			if (player?.Transform == null || foundation == null)
			{
				return false;
			}
			float reach = foundation.InteractionRange + PanelReachSlackMetres;
			return (foundation.transform.position - player.Transform.position).sqrMagnitude <= reach * reach;
		}

		/// <summary>
		/// Describes a plot to one player. What the foundation knows is gathered here; the owner's
		/// name, the rent dates and whether the asker already owns land are read from the database.
		/// </summary>
		private void SendPlotInfo(NetworkConnection conn, IPlayerCharacter player, PlotFoundation foundation)
		{
			if (conn == null || player == null || foundation == null || foundation.PlotID <= 0)
			{
				return;
			}

			PlotOwner owner = foundation.Owner;
			bool isOwner = owner.Type == PlotOwnerType.Character && owner.ID == player.ID;
			PlotPermission permissions = foundation.PermissionsFor(player.ID, GuildIDOf(player));
			Vector2 size = foundation.Dimensions;

			var info = new HousingPlotInfoBroadcast
			{
				PlotID = foundation.PlotID,
				State = (byte)foundation.State,
				Price = foundation.Price,
				Width = size.x,
				Depth = size.y,
				OwnerName = isOwner ? player.CharacterName : string.Empty,
				Relation = isOwner ? PlotRelation.Owner : foundation.GrantFor(player.ID) != PlotPermission.None ? PlotRelation.Guest : PlotRelation.None,
				Permissions = (int)permissions,
				CurrencyTemplateID = currencyTemplate != null ? currencyTemplate.ID : 0,
				RentPerPeriod = IsTaxEnabled ? taxPerPeriod : 0,
				RentPeriodHours = taxPeriodDays * 24f,
				RentGraceHours = taxGraceDays * 24f,
				OwnsLand = isOwner,
			};

			long plotID = foundation.PlotID;
			long askerID = player.ID;
			long ownerCharacterID = owner.Type == PlotOwnerType.Character ? owner.ID : 0;

			if (!TryEnqueueAsyncWork(() => CompletePlotInfoAsync(conn, info, plotID, askerID, ownerCharacterID, isOwner), askerID))
			{
				// Answer with what the foundation knows rather than not at all.
				Server.NetworkWrapper.Broadcast(conn, info, true, Channel.Reliable);
			}
		}

		private async Task CompletePlotInfoAsync(NetworkConnection conn, HousingPlotInfoBroadcast info, long plotID, long askerID, long ownerCharacterID, bool isOwner)
		{
			try
			{
				if (TryGetDbService(out IPlotService plotService))
				{
					if (!isOwner)
					{
						DatabaseResult<List<PlotData>> owned = await plotService.FetchByOwnerCharacterAsync(askerID);
						info.OwnsLand = owned.IsSuccess && owned.Data != null && owned.Data.Count > 0;
					}
					else
					{
						DatabaseResult<List<PlotData>> row = await plotService.FetchByIdsAsync(new[] { plotID });
						if (row.IsSuccess && row.Data != null && row.Data.Count == 1)
						{
							DateTime now = DateTime.UtcNow;
							PlotData plot = row.Data[0];
							if (plot.TaxDueUtc.HasValue && info.RentPerPeriod > 0)
							{
								info.RentDueInSeconds = (plot.TaxDueUtc.Value - now).TotalSeconds;
							}
							if (plot.TaxDelinquentSinceUtc.HasValue)
							{
								info.UnpaidForSeconds = Math.Max(1.0, (now - plot.TaxDelinquentSinceUtc.Value).TotalSeconds);
							}
						}
					}
				}

				if (!isOwner && ownerCharacterID > 0 && TryGetDbService(out ICharacterService characterService))
				{
					DatabaseResult<IReadOnlyList<CharacterNameData>> names = await characterService.FetchNamesAsync(new[] { ownerCharacterID });
					if (names.IsSuccess && names.Data != null && names.Data.Count > 0)
					{
						info.OwnerName = names.Data[0].Name ?? string.Empty;
					}
				}
			}
			catch (Exception ex)
			{
				Log.Warning("HousingSystem", $"Plot {plotID}'s description was sent without its database details: {ex.Message}");
			}

			if (!TryEnqueueHousingMainThread(() =>
			{
				if (conn != null && conn.IsActive && Server?.NetworkWrapper != null)
				{
					Server.NetworkWrapper.Broadcast(conn, info, true, Channel.Reliable);
				}
			}))
			{
				Log.Warning("HousingSystem", $"Could not send plot {plotID}'s description.");
			}
		}
	}
}
