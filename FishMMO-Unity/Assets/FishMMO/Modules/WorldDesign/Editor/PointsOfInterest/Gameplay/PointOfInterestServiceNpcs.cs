#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;
using FishMMO.Shared.NameGeneration;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// One kind of service NPC (the banker, the general merchant, a smith, the stablemaster): what it is, where in a site's
	/// layout it stands, which building brings one, and whether every capital keeps one.
	/// </summary>
	public sealed class ServiceNpc
	{
		/// <summary>Names its spawners ("Weaponsmith" makes "&lt;site&gt; Weaponsmith Spawner") and the notes.</summary>
		public string Name;
		/// <summary>Its NPC prefabs for a site's race (<see cref="PointOfInterestServiceNpcs.Role"/>, <see cref="PointOfInterestServiceNpcs.Prefab"/>).</summary>
		public PointOfInterestServiceNpcs.Resolver Prefabs;
		/// <summary>Where it stands, most fitting first (<see cref="PointOfInterestNpcPosts"/>); none it can take leaves it where it was.</summary>
		public NpcPostRule[] Posts = Array.Empty<NpcPostRule>();
		/// <summary>The buildings (kit piece ids) that bring one: a site that lays one keeps this service (a smithy its smith).</summary>
		public string[] KeeperOf = Array.Empty<string>();
		/// <summary>Which sites a <see cref="KeeperOf"/> building brings one to; null for <see cref="PointOfInterestServiceNpcs.Keeps"/>.</summary>
		public Func<PointOfInterestRecord, bool> KeeperSites;
		/// <summary>Every capital keeps one.</summary>
		public bool AtCapital = true;
	}

	/// <summary>
	/// The service NPCs and where they stand: each where a town keeps it (a smith at the smithy, the stablemaster at the
	/// stables, the general merchant behind a stall on the market, the banker at the counting house, the ability crafter at the
	/// wizard's tower), every building that brings one keeping one, and every capital one of each.
	/// </summary>
	/// <remarks>
	/// <para><b>When.</b> The stage calls <see cref="Seat"/> after a site's template features and site builders have run, so every
	/// building is laid and every spawner the template made is there. It then:
	/// <list type="number">
	/// <item><description>finds the service spawners the template made (its Banker, Merchant and Crafter roles: a spawner whose
	/// every NPC is one service's);</description></item>
	/// <item><description>adds one for each service whose building the site laid (<see cref="ServiceNpc.KeeperOf"/>) and, at a
	/// capital, for each service it still lacks;</description></item>
	/// <item><description>stands each, in the order the services are registered, at the first free place its
	/// <see cref="ServiceNpc.Posts"/> offer. One the layout offers nothing for stays where its feature drew it (a mill's miller); one
	/// added here takes a seeded spot (<see cref="NpcSpawnerFeature.DrawSpot"/>);</description></item>
	/// <item><description>and gathers the townsfolk round the market, where a town's people are.</description></item>
	/// </list></para>
	/// <para><b>Every type.</b> A table role answers with every prefab its row lists, each its own NPC: a Merchant row listing
	/// a general merchant and a jeweller puts both in a capital.</para>
	/// <para><b>Modules.</b> A module registers its own (<see cref="Register"/>: the Mounts module's Stablemaster) from its
	/// editor assembly, so without the module no town sells its goods. A new spawner's fallback spot is seeded from the site and
	/// the prefab's name, so adding or removing a module never moves another's.</para>
	/// </remarks>
	public static class PointOfInterestServiceNpcs
	{
		/// <summary>The prefabs of a service for a site's race (null for none).</summary>
		public delegate IReadOnlyList<GameObject> Resolver(PointOfInterestSiteContext context, RaceTemplate race);

		private static readonly List<ServiceNpc> services = new List<ServiceNpc>();

		static PointOfInterestServiceNpcs()
		{
			Register(new ServiceNpc
			{
				Name = "Banker",
				Prefabs = Role(PointOfInterestNpcRole.Banker),
				Posts = new[] { NpcPostRule.AtBuilding("CountingHouse"), NpcPostRule.AtBuilding("MarketHall"), NpcPostRule.AtMarket() },
				KeeperOf = new[] { "CountingHouse" },
			});
			Register(new ServiceNpc
			{
				Name = "Merchant",
				Prefabs = Role(PointOfInterestNpcRole.Merchant),
				Posts = new[]
				{
					NpcPostRule.AtStall(), NpcPostRule.AtBuilding("MarketHall"),
					NpcPostRule.AtShop(StructureTrade.Chandler, StructureTrade.Ironmonger, StructureTrade.Cooper), NpcPostRule.AtMarket(),
				},
			});
			Register(new ServiceNpc
			{
				Name = "Crafter",
				Prefabs = Role(PointOfInterestNpcRole.Crafter),
				Posts = new[]
				{
					NpcPostRule.AtBuilding("WizardTower"), NpcPostRule.AtBuilding("Library", "Observatory"),
					NpcPostRule.AtShop(StructureTrade.Scribe, StructureTrade.Apothecary), NpcPostRule.AtBuilding("TownTemple", "Cathedral", "Church"),
					NpcPostRule.AtMarket(),
				},
				KeeperOf = new[] { "WizardTower" },
			});
			Register(new ServiceNpc
			{
				Name = "Weaponsmith",
				Prefabs = Role(PointOfInterestNpcRole.Weaponsmith),
				Posts = new[] { NpcPostRule.AtBuilding("Smithy"), NpcPostRule.AtShop(StructureTrade.Fletcher), NpcPostRule.AtBuilding("Workshop"), NpcPostRule.AtMarket() },
				KeeperOf = new[] { "Smithy" },
			});
			Register(new ServiceNpc
			{
				Name = "Armorsmith",
				Prefabs = Role(PointOfInterestNpcRole.Armorsmith),
				Posts = new[] { NpcPostRule.AtBuilding("Smithy"), NpcPostRule.AtShop(StructureTrade.Leatherworker), NpcPostRule.AtBuilding("Workshop"), NpcPostRule.AtMarket() },
				KeeperOf = new[] { "Smithy" },
				// A village's smith makes what a village needs; the armourer is a town's, a city's and a garrison's.
				KeeperSites = record => Keeps(record) && record.Kind != POIType.Village && record.Kind != POIType.StiltVillage
					&& record.Kind != POIType.Monastery && record.Kind != POIType.TradingPost,
			});
		}

		/// <summary>The registered services' names, core first, then modules in registration order.</summary>
		public static IReadOnlyList<string> Names
		{
			get
			{
				var names = new List<string>(services.Count);
				foreach (ServiceNpc service in services)
				{
					names.Add(service.Name);
				}
				return names;
			}
		}

		/// <summary>A registered service by name, or null.</summary>
		public static ServiceNpc Find(string name) => services.Find(s => string.Equals(s.Name, name, StringComparison.Ordinal));

		/// <summary>Adds (or replaces, by name) a service.</summary>
		public static void Register(ServiceNpc service)
		{
			if (service == null || string.IsNullOrWhiteSpace(service.Name) || service.Prefabs == null)
			{
				return;
			}
			int at = services.FindIndex(s => string.Equals(s.Name, service.Name, StringComparison.Ordinal));
			if (at >= 0)
			{
				services[at] = service;
			}
			else
			{
				services.Add(service);
			}
		}

		/// <summary>Removes a service by name.</summary>
		public static void Unregister(string name) => services.RemoveAll(s => string.Equals(s.Name, name, StringComparison.Ordinal));

		/// <summary>A service whose NPC is one prefab, whatever the site's race (a module's own NPC).</summary>
		public static Resolver Prefab(Func<GameObject> prefab) => (context, race) =>
		{
			GameObject loaded = prefab?.Invoke();
			return loaded != null ? new[] { loaded } : Array.Empty<GameObject>();
		};

		/// <summary>
		/// A service answered by the NPC table's row for a role and the site's race: every prefab the row lists. An authored
		/// table written before the role existed (the smiths) answers from the code defaults until "Build site NPCs" adds its row.
		/// </summary>
		public static Resolver Role(PointOfInterestNpcRole role) => (context, race) =>
		{
			List<PointOfInterestNpcChoice> choices = PointOfInterestNpcTable.LoadOrDefaults().Resolve(race, role);
			if (choices.Count == 0)
			{
				choices = Defaults().Resolve(race, role, context?.Notes);
			}
			var prefabs = new List<GameObject>();
			foreach (PointOfInterestNpcChoice choice in choices)
			{
				if (choice?.Prefab != null && !prefabs.Contains(choice.Prefab))
				{
					prefabs.Add(choice.Prefab);
				}
			}
			return prefabs;
		};

		private static PointOfInterestNpcTable defaults;

		[InitializeOnLoadMethod]
		private static void DropDefaultsOnChange() => EditorApplication.projectChanged += () => defaults = null;

		private static PointOfInterestNpcTable Defaults()
		{
			if (defaults == null)
			{
				defaults = ScriptableObject.CreateInstance<PointOfInterestNpcTable>();
				defaults.hideFlags = HideFlags.DontSave;
				defaults.FillDefaults();
			}
			return defaults;
		}

		/// <summary>Whether a site keeps service NPCs of its own: every place people live, and every stronghold with a ward; never a hostile one.</summary>
		public static bool Keeps(PointOfInterestRecord record)
		{
			return record != null && !record.Info.Has(PointOfInterestTraits.Hostile) && PointOfInterestStructureCaps.Applies(record.Kind);
		}

		/// <summary>Whether a site keeps one of every service: a capital that is not hostile.</summary>
		public static bool KeepsEvery(PointOfInterestRecord record)
		{
			return record != null && record.Kind == POIType.Capital && !record.Info.Has(PointOfInterestTraits.Hostile);
		}

		/// <summary>A service's spawner at a site: the spawner, whose it is, and whether this pass made it.</summary>
		private sealed class Seated
		{
			public ObjectSpawner Spawner;
			public ServiceNpc Service;
			public GameObject Prefab;
			public bool Added;
			public int Order;
		}

		/// <summary>
		/// Seats the site's service NPCs (see remarks): adds the ones its buildings and its rank call for and stands each at its
		/// place. Returns how many spawners it added.
		/// </summary>
		public static int Seat(PointOfInterestSiteContext context)
		{
			if (context?.Root == null || context.Record == null)
			{
				return 0;
			}
			RaceTemplate race = PointOfInterestFeatureKit.SiteRace(context);
			var posts = new PointOfInterestNpcPosts(context);

			// Each service's prefabs here, once.
			var prefabsOf = new Dictionary<ServiceNpc, IReadOnlyList<GameObject>>();
			foreach (ServiceNpc service in services)
			{
				IReadOnlyList<GameObject> prefabs;
				try
				{
					prefabs = service.Prefabs(context, race) ?? Array.Empty<GameObject>();
				}
				catch (Exception ex)
				{
					Debug.LogException(ex);
					PointOfInterestFeatureKit.Note(context, $"its {service.Name} service threw: {ex.Message}");
					prefabs = Array.Empty<GameObject>();
				}
				prefabsOf[service] = prefabs;
			}

			// 1. The service spawners already there (the template's roles).
			var seated = new List<Seated>();
			foreach (ObjectSpawner spawner in context.Root.GetComponentsInChildren<ObjectSpawner>(true))
			{
				if (TryServiceOf(spawner, prefabsOf, out ServiceNpc service, out GameObject prefab))
				{
					seated.Add(new Seated { Spawner = spawner, Service = service, Prefab = prefab });
				}
			}

			// 2. One for each building that brings one, and one of each at a capital: every prefab a service lists.
			int added = 0;
			bool every = KeepsEvery(context.Record);
			foreach (ServiceNpc service in services)
			{
				Func<PointOfInterestRecord, bool> keeperSites = service.KeeperSites ?? Keeps;
				bool kept = service.KeeperOf.Length > 0 && keeperSites(context.Record) && posts.Has(service.KeeperOf);
				if (!kept && !(every && service.AtCapital))
				{
					continue;
				}
				IReadOnlyList<GameObject> prefabs = prefabsOf[service];
				if (prefabs.Count == 0)
				{
					PointOfInterestFeatureKit.Note(context, $"should keep a {service.Name} but nothing answers it.");
					continue;
				}
				foreach (GameObject prefab in prefabs)
				{
					if (seated.Exists(s => s.Prefab == prefab) || prefab.GetComponent<NetworkObject>() == null)
					{
						continue;
					}
					string what = prefabs.Count > 1 ? $"{service.Name} {prefab.name}" : service.Name;
					var choices = new List<PointOfInterestNpcChoice> { new PointOfInterestNpcChoice { Prefab = prefab, Weight = 1.0f } };
					ObjectSpawner spawner = PointOfInterestFeatureKit.AddSpawner(context, PointOfInterestFeatureKit.UniqueName(context, $"{what} Spawner"),
						Vector2.zero, PointOfInterestFeatureKit.NpcSpawnables(choices, race), 1, 0.0f);
					seated.Add(new Seated { Spawner = spawner, Service = service, Prefab = prefab, Added = true });
					added++;
				}
			}

			// 3. Each at its place: services in registration order, the template's before the added, else as found (a stable
			// order, so a second pass stands each where the first did).
			for (int i = 0; i < seated.Count; i++)
			{
				seated[i].Order = i;
			}
			seated.Sort((a, b) =>
			{
				int byService = services.IndexOf(a.Service).CompareTo(services.IndexOf(b.Service));
				if (byService != 0)
				{
					return byService;
				}
				int byAdded = a.Added.CompareTo(b.Added);
				return byAdded != 0 ? byAdded : a.Order.CompareTo(b.Order);
			});
			foreach (Seated s in seated)
			{
				if (posts.TryClaim(s.Service.Posts, out NpcPost post))
				{
					s.Spawner.transform.SetPositionAndRotation(post.Position, post.Facing);
				}
				else if (s.Added)
				{
					PointOfInterestSiteContext seeded = PointOfInterestLinearFeatures.Copy(context);
					seeded.Random = new DeterministicRNG((int)PointOfInterestPlanner.Mix(unchecked((uint)context.Record.SiteSeed ^ Hash(s.Prefab.name))));
					Vector2 local = NpcSpawnerFeature.DrawSpot(seeded, onSpot: true);
					s.Spawner.transform.position = context.OnGround(local);
					if (s.Service.Posts.Length > 0)
					{
						PointOfInterestFeatureKit.Note(context, $"has no place for its {s.Service.Name} ({string.Join(", ", (object[])s.Service.Posts)}): stood on a drawn spot.");
					}
				}
				EditorUtility.SetDirty(s.Spawner);
			}

			// 4. The townsfolk round the market.
			GatherTownsfolk(context, posts);
			return added;
		}

		/// <summary>Moves the site's townsfolk spawners to the middle of its market, if it has one (their area kept).</summary>
		private static void GatherTownsfolk(PointOfInterestSiteContext context, PointOfInterestNpcPosts posts)
		{
			if (!posts.TryMarketMiddle(out Vector3 middle))
			{
				return;
			}
			string name = PointOfInterestFeatureKit.UniqueName(context, $"{PointOfInterestNpcRole.Townsfolk} Spawner");
			foreach (ObjectSpawner spawner in context.Root.GetComponentsInChildren<ObjectSpawner>(true))
			{
				if (spawner.name == name)
				{
					spawner.transform.position = middle;
					EditorUtility.SetDirty(spawner);
				}
			}
		}

		/// <summary>The service a spawner's NPCs all belong to (and its one prefab), if any.</summary>
		private static bool TryServiceOf(ObjectSpawner spawner, Dictionary<ServiceNpc, IReadOnlyList<GameObject>> prefabsOf, out ServiceNpc service, out GameObject prefab)
		{
			service = null;
			prefab = null;
			if (spawner?.Spawnables == null || spawner.Spawnables.Count == 0)
			{
				return false;
			}
			foreach (SpawnableSettings spawnable in spawner.Spawnables)
			{
				GameObject spawned = spawnable?.NetworkObject != null ? spawnable.NetworkObject.gameObject : null;
				ServiceNpc whose = null;
				foreach (KeyValuePair<ServiceNpc, IReadOnlyList<GameObject>> entry in prefabsOf)
				{
					if (spawned != null && Contains(entry.Value, spawned))
					{
						whose = entry.Key;
						break;
					}
				}
				if (whose == null || service != null && whose != service)
				{
					return false;
				}
				service = whose;
				prefab ??= spawned;
			}
			return service != null;
		}

		private static bool Contains(IReadOnlyList<GameObject> list, GameObject item)
		{
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i] == item)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>FNV-1a of a name: stable across runs and runtimes (string.GetHashCode is not promised to be).</summary>
		private static uint Hash(string text)
		{
			uint h = 2166136261u;
			foreach (char c in text ?? string.Empty)
			{
				h = unchecked((h ^ c) * 16777619u);
			}
			return h;
		}
	}
}
#endif
