using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The "Plot for sale" map markers (2026-11-22, Jim: "We would like a Plot for sale map marker").
	/// </summary>
	/// <remarks>
	/// <para>
	/// The client cannot tell which plots are for sale on its own: a plot's state rides its own network object, and a client
	/// is sent only the plots near it. The server holds every plot of the scene, so it answers a client's request with the
	/// whole scene's unclaimed plots (position and price), and pushes the list again to everybody in the scene when one of
	/// them is claimed or released (<see cref="PlotFoundation.Registry.OnStateApplied"/>). The whole list each time: a scene
	/// holds tens of plots, a few hundred bytes.
	/// </para>
	/// <para>For sale = <see cref="PlotState.Empty"/>, with housing on. An abandoned plot is reclaimed by the sweep first.</para>
	/// </remarks>
	public partial class HousingSystem
	{
		private readonly List<HousingPlotForSale> forSaleScratch = new List<HousingPlotForSale>();

		private void RegisterPlotMarkerBroadcasts()
		{
			Server.NetworkWrapper.RegisterBroadcast<HousingPlotsForSaleRequestBroadcast>(OnServerPlotsForSaleRequest, true);
			PlotFoundation.Registry.OnStateApplied -= Registry_OnStateApplied;
			PlotFoundation.Registry.OnStateApplied += Registry_OnStateApplied;
		}

		private void UnregisterPlotMarkerBroadcasts()
		{
			Server?.NetworkWrapper?.UnregisterBroadcast<HousingPlotsForSaleRequestBroadcast>(OnServerPlotsForSaleRequest);
			PlotFoundation.Registry.OnStateApplied -= Registry_OnStateApplied;
		}

		private void OnServerPlotsForSaleRequest(NetworkConnection conn, HousingPlotsForSaleRequestBroadcast msg, Channel channel)
		{
			if (!TryDebounceHousingRequest(conn, HousingOperation.PlotsForSale) || !TryGetActingPlayer(conn, out IPlayerCharacter player))
			{
				return;
			}
			Transform transform = player.Transform;
			if (transform == null)
			{
				return;
			}
			Scene scene = transform.gameObject.scene;
			Server.NetworkWrapper.Broadcast(conn, ForSaleIn(scene), true, Channel.Reliable);
		}

		/// <summary>
		/// A plot changed hands: its scene's list goes again to everybody in the scene. Straight away: a claim or a release is
		/// one plot, and the startup sync that sets every plot at once runs before anybody is in the scene.
		/// </summary>
		private void Registry_OnStateApplied(PlotFoundation foundation)
		{
			if (foundation == null || !IsHousingEnabled || Server?.NetworkWrapper == null)
			{
				return;
			}
			Scene scene = foundation.gameObject.scene;
			if (!Server.NetworkWrapper.TryGetSceneConnections(scene, out HashSet<NetworkConnection> connections) || connections.Count == 0)
			{
				return;
			}
			HousingPlotsForSaleBroadcast list = ForSaleIn(scene);
			foreach (NetworkConnection conn in connections)
			{
				if (conn != null && conn.IsActive)
				{
					Server.NetworkWrapper.Broadcast(conn, list, true, Channel.Reliable);
				}
			}
		}

		/// <summary>The scene's plots for sale, as the markers need them. Empty with housing off or no plots.</summary>
		private HousingPlotsForSaleBroadcast ForSaleIn(Scene scene)
		{
			forSaleScratch.Clear();
			if (IsHousingEnabled && scene.IsValid())
			{
				IReadOnlyList<PlotFoundation> foundations = PlotFoundation.Registry.ForScene(scene.handle);
				for (int i = 0; foundations != null && i < foundations.Count; i++)
				{
					PlotFoundation foundation = foundations[i];
					if (foundation != null && foundation.State == PlotState.Empty)
					{
						forSaleScratch.Add(new HousingPlotForSale { Position = foundation.transform.position, Price = foundation.Price });
					}
				}
			}
			return new HousingPlotsForSaleBroadcast
			{
				SceneName = scene.IsValid() ? scene.name : string.Empty,
				Plots = forSaleScratch.ToArray(),
			};
		}
	}
}
