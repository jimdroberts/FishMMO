using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Transporting;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The "Plot for sale" markers on the minimap and the world map: one <see cref="MapMarker"/> of type <see cref="MapMarkerType.HousingPlot"/> per unclaimed plot of the
	/// character's scene, from the server's list (<see cref="HousingPlotsForSaleBroadcast"/>).
	/// </summary>
	/// <remarks>
	/// Asked for each time the local character starts (every scene entry), replaced whenever the server pushes a new list (a
	/// plot claimed or released), cleared when the character stops. The markers are plain objects of their own, not the
	/// plots: a plot's network object exists on a client only near it, and the map wants every plot of the scene.
	/// </remarks>
	public static class HousingPlotMarkers
	{
		private static GameObject root;
		private static readonly List<GameObject> markers = new List<GameObject>();
		/// <summary>The network manager the list's handler is registered with; a new one is registered again.</summary>
		private static FishNet.Managing.NetworkManager registeredWith;
		private static string sceneName;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void Install()
		{
			registeredWith = null;
			sceneName = null;
			root = null;
			markers.Clear();
			IPlayerCharacter.OnStartLocalClient -= OnStartLocal;
			IPlayerCharacter.OnStartLocalClient += OnStartLocal;
			IPlayerCharacter.OnStopLocalClient -= OnStopLocal;
			IPlayerCharacter.OnStopLocalClient += OnStopLocal;
		}

		private static void OnStartLocal(IPlayerCharacter character)
		{
			if (Client.NetworkManager == null || character == null)
			{
				return;
			}
			if (!ReferenceEquals(registeredWith, Client.NetworkManager))
			{
				Client.NetworkManager.ClientManager.RegisterBroadcast<HousingPlotsForSaleBroadcast>(OnPlotsForSale);
				registeredWith = Client.NetworkManager;
			}
			sceneName = character.CurrentSceneName();
			Clear();
			Client.Broadcast(new HousingPlotsForSaleRequestBroadcast(), Channel.Reliable);
		}

		private static void OnStopLocal(IPlayerCharacter character)
		{
			sceneName = null;
			Clear();
		}

		private static void OnPlotsForSale(HousingPlotsForSaleBroadcast msg, Channel channel)
		{
			// A list for a scene the character has since left is stale.
			if (string.IsNullOrEmpty(sceneName) || msg.SceneName != sceneName)
			{
				return;
			}
			Clear();
			if (msg.Plots == null || msg.Plots.Length == 0)
			{
				return;
			}
			if (root == null)
			{
				root = new GameObject("Housing Plot Markers");
				Object.DontDestroyOnLoad(root);
			}
			foreach (HousingPlotForSale plot in msg.Plots)
			{
				var marker = new GameObject("Plot for sale");
				marker.SetActive(false);
				marker.transform.SetParent(root.transform, false);
				marker.transform.position = plot.Position;
				MapMarker mapMarker = marker.AddComponent<MapMarker>();
				mapMarker.Type = MapMarkerType.HousingPlot;
				mapMarker.Visibility = MapMarkerVisibility.Always;
				mapMarker.Label = plot.Price > 0 ? $"Plot for sale ({plot.Price:N0} gold)" : "Plot for sale";
				mapMarker.ShowOnMinimap = true;
				mapMarker.ShowOnWorldMap = true;
				// Joins the registry with its fields set (MapMarker registers in OnEnable).
				marker.SetActive(true);
				markers.Add(marker);
			}
		}

		private static void Clear()
		{
			foreach (GameObject marker in markers)
			{
				if (marker != null)
				{
					Object.Destroy(marker);
				}
			}
			markers.Clear();
		}
	}
}
