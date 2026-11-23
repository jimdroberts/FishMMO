using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: FishMMO.Server.Implementation.World.SceneServer.OperatorCommandProvider(typeof(FishMMO.Server.Implementation.World.SceneServer.MapOperatorCommands))]

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// <c>/gm reveal map|waypoints|sites|all</c>: hands the caller the scene they stand in as if they had walked all of it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The caller only, this scene only</b> (usable on GMs and admins, never on players; current scene, self only).
	/// There is no character argument, so the only character it can touch is the staff member typing it.
	/// </para>
	/// <para>
	/// <b>Map</b> records the whole scene as explored in the account's server-side fog (<c>MapFogSystem.RevealEverything</c>),
	/// then asks the caller's client to draw it (<see cref="MapRevealBroadcast"/>). <b>Waypoints</b> unlocks every waypoint registered in the scene through the waypoint controller,
	/// so they are saved and announced exactly as one found on foot. <b>Sites</b> marks every point of interest discovered
	/// in the character's record. Both credit the achievement the content counts them with: the <see cref="AchievementIncrementAction"/> that follows the unlock or discovery action in the waypoint's
	/// or site's own triggers, once per newly found one, so an already found one is never counted twice. Neither can be
	/// undone: unlock and discovery bits are only ever set.
	/// </para>
	/// </remarks>
	public sealed class MapOperatorCommands : OperatorCommandProvider
	{
		private static readonly string[] RevealHelp =
		{
			"reveal map — explores the whole map of the scene you are in (fog lifts, every point of interest shows)",
			"reveal waypoints — unlocks every waypoint in this scene for you, counting each new one toward its achievement",
			"reveal sites — discovers every point of interest in this scene for you, counting each new one toward its achievement",
			"reveal all — all three. Yourself only; unlocks and discoveries cannot be undone.",
		};

		/// <inheritdoc />
		public override IEnumerable<OperatorCommand> GameMasterCommands(IOperatorCommandHost host)
		{
			return new List<OperatorCommand>
			{
				new OperatorCommand
				{
					Name = "reveal", Category = "World",
					Summary = "Reveals this scene's map, waypoints or points of interest for yourself. /gm reveal help lists them.",
					Arguments = "what:Choice=map,waypoints,sites,all,help?",
					Destructive = true,
					Run = (character, arguments) => Reveal(host, character, arguments),
				},
			};
		}

		private static void Reveal(IOperatorCommandHost host, IPlayerCharacter character, string arguments)
		{
			string what = OperatorCommandParsing.SplitFirstWord(arguments, out _).ToLowerInvariant();
			if (character == null || character.GameObject == null)
			{
				return;
			}
			bool all = what == "all";
			if (!all && what != "map" && what != "waypoints" && what != "sites")
			{
				host.ReplyLines(character, RevealHelp);
				return;
			}
			Scene scene = character.GameObject.scene;
			var lines = new List<string>();
			if (all || what == "map")
			{
				lines.Add(RevealMap(character));
			}
			if (all || what == "waypoints")
			{
				lines.Add(RevealWaypoints(character, scene));
			}
			if (all || what == "sites")
			{
				lines.Add(RevealSites(character, scene));
			}
			host.ReplyLines(character, lines);
		}

		private static string RevealMap(IPlayerCharacter character)
		{
			NetworkConnection owner = character.Owner;
			if (owner == null || !owner.IsActive)
			{
				return "Map: you have no client to reveal it on.";
			}
			string sceneName = character.CurrentSceneName();
			// Recorded on the server (account-wide fog), then drawn on the client.
			MapFogSystem.Current?.RevealEverything(character);
			owner.Broadcast(new MapRevealBroadcast() { SceneName = sceneName });
			return $"Map: {sceneName} explored.";
		}

		private static string RevealWaypoints(IPlayerCharacter character, Scene scene)
		{
			if (!character.TryGet(out IWaypointController controller))
			{
				return "Waypoints: this character has no waypoint controller (is the Travel module installed?).";
			}
			var indices = new List<int>();
			WaypointRegistry.CollectIndices(scene.handle, indices);
			int unlocked = 0;
			var credits = new Credits();
			for (int i = 0; i < indices.Count; ++i)
			{
				if (!WaypointRegistry.TryGet(scene.handle, indices[i], out IWaypoint waypoint) || waypoint == null)
				{
					continue;
				}
				if (!controller.Unlock(waypoint.SceneName, waypoint.WaypointIndex))
				{
					continue;
				}
				++unlocked;
				if (waypoint is FishMMO.Shared.Interactable interactable)
				{
					credits.Add(interactable.OnInteractTriggers, character);
					credits.Add(InteractableTriggers.For(interactable.GetType()), character);
				}
				credits.Pay(character);
			}
			return $"Waypoints: {unlocked} newly unlocked of {indices.Count} here.{credits.Summary}";
		}

		private static string RevealSites(IPlayerCharacter character, Scene scene)
		{
			PortalActivationStore store = PortalActivationStore.Current;
			if (!store.IsCharacterLoaded(character.ID))
			{
				store.RequestLoad(character.ID, null);
				return "Sites: your discovery record is still loading; try again in a moment.";
			}
			var sites = new List<PointOfInterestDiscovery>();
			if (scene.IsValid())
			{
				foreach (GameObject root in scene.GetRootGameObjects())
				{
					sites.AddRange(root.GetComponentsInChildren<PointOfInterestDiscovery>(true));
				}
			}
			int discovered = 0;
			var credits = new Credits();
			for (int i = 0; i < sites.Count; ++i)
			{
				PointOfInterestDiscovery site = sites[i];
				if (site == null || !WaypointUnlockMask.IsValidIndex(site.SiteIndex) ||
					!store.ActivateForCharacter(character.ID, PointOfInterestDiscovery.RecordKey(site.gameObject.scene.name), site.SiteIndex))
				{
					continue;
				}
				++discovered;
				if (site.TryGetComponent(out Region region))
				{
					credits.Add(region.OnRegionEnter, character);
				}
				credits.Pay(character);
			}
			return $"Sites: {discovered} newly discovered of {sites.Count} here.{credits.Summary}";
		}

		/// <summary>
		/// The achievement increments a found waypoint or site is counted with: each <see cref="AchievementIncrementAction"/>
		/// that follows the unlock or discovery action in the same trigger. That action is abortable and stops the chain when
		/// the thing was already found, so what follows it is the "first time" credit.
		/// </summary>
		private sealed class Credits
		{
			private readonly List<(CachedScriptableObject Template, uint Amount)> pending = new List<(CachedScriptableObject, uint)>();
			private readonly Dictionary<string, uint> paid = new Dictionary<string, uint>();

			/// <summary>" Counted: Waypoints Discovered +3." or empty.</summary>
			public string Summary
			{
				get
				{
					if (paid.Count == 0)
					{
						return string.Empty;
					}
					var parts = new List<string>();
					foreach (KeyValuePair<string, uint> entry in paid)
					{
						parts.Add($"{entry.Key} +{entry.Value}");
					}
					return " Counted: " + string.Join(", ", parts) + ".";
				}
			}

			/// <summary>Queues the credits the triggers carry after their unlock or discovery action.</summary>
			public void Add(List<Trigger> triggers, ICharacter character)
			{
				if (triggers == null)
				{
					return;
				}
				for (int t = 0; t < triggers.Count; ++t)
				{
					List<BaseAction> actions = triggers[t] != null ? triggers[t].OnConditionsMetActions : null;
					if (actions == null)
					{
						continue;
					}
					bool afterFind = false;
					for (int a = 0; a < actions.Count; ++a)
					{
						BaseAction action = actions[a];
						// The find step: an abortable action that stops the chain when the thing was already found
						// (UnlockWaypointAction, DiscoverSiteAction). What follows it runs on a first find only.
						if (action is IAbortableAction && action.StopChainOnFailure)
						{
							afterFind = true;
							continue;
						}
						if (afterFind && action is AchievementIncrementAction increment && increment.AchievementTemplate != null)
						{
							pending.Add((increment.AchievementTemplate, AmountOf(increment, character)));
						}
					}
				}
			}

			/// <summary>Records the queued credits for the character and clears them.</summary>
			public void Pay(ICharacter character)
			{
				for (int i = 0; i < pending.Count; ++i)
				{
					(CachedScriptableObject template, uint amount) = pending[i];
					Accomplishments.Record(character, template, amount);
					paid.TryGetValue(template.name, out uint sum);
					paid[template.name] = sum + amount;
				}
				pending.Clear();
			}

			/// <summary>The increment's amount; a provider that needs the event it never gets here counts one.</summary>
			private static uint AmountOf(AchievementIncrementAction increment, ICharacter character)
			{
				try
				{
					int value = increment.AmountValue != null ? increment.AmountValue.GetValue(character, null) : 1;
					return value > 0 ? (uint)value : 0u;
				}
				catch (Exception)
				{
					return 1u;
				}
			}
		}
	}
}
