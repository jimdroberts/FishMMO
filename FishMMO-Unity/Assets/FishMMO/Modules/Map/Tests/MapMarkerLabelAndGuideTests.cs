using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// What the maps write beside a marker, and the guide the world map chooses for the minimap.
	/// </summary>
	public class MapMarkerLabelAndGuideTests
	{
		/// <summary>Fixtures built by the tests, torn down afterwards.</summary>
		private readonly List<GameObject> hosts = new List<GameObject>();

		[TearDown]
		public void TearDown()
		{
			MapGuide.Clear();
			for (int i = 0; i < hosts.Count; ++i)
			{
				if (hosts[i] == null)
				{
					continue;
				}
				MapMarker marker = hosts[i].GetComponent<MapMarker>();
				if (marker != null)
				{
					MapMarkerRegistry.Unregister(marker);
				}
				Object.DestroyImmediate(hosts[i]);
			}
			hosts.Clear();
		}

		/// <summary>
		/// A marker on an NPC, enabled the way the runtime enables it so it has found its character and
		/// interactable (edit mode does not run <c>OnEnable</c> by itself).
		/// </summary>
		/// <param name="name">The NPC's name.</param>
		/// <param name="title">Its interactable's title, or null for an NPC without one.</param>
		/// <param name="authoredLabel">A static label left on the prefab, as a clone inherits one.</param>
		private MapMarker NewNpc(string name, string title, string authoredLabel = null)
		{
			GameObject host = new GameObject(name);
			hosts.Add(host);
			host.AddComponent<ProbeCharacter>();
			if (title != null)
			{
				host.AddComponent<ProbeInteractable>().ProbeTitle = title;
			}
			MapMarker marker = host.AddComponent<MapMarker>();
			marker.Label = authoredLabel;
			Enable(marker);
			return marker;
		}

		/// <summary>A marker on a fixture: no character.</summary>
		private MapMarker NewFixture(string label, string hoverText = null)
		{
			GameObject host = new GameObject("fixture");
			hosts.Add(host);
			MapMarker marker = host.AddComponent<MapMarker>();
			marker.Label = label;
			marker.HoverText = hoverText;
			Enable(marker);
			return marker;
		}

		private static void Enable(MapMarker marker)
		{
			MethodInfo onEnable = typeof(MapMarker).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.IsNotNull(onEnable, "MapMarker.OnEnable is where the marker finds its character; the test needs it.");
			onEnable.Invoke(marker, null);
		}

		// ── Labels ──────────────────────────────────────────────────

		[Test]
		public void Npc_WithATitle_IsLabelledWithNameAndTitle()
		{
			MapMarker banker = NewNpc("Bramwell", "Banker");

			Assert.AreEqual("Bramwell <Banker>", MapMarkerFilter.ResolveLabel(banker, true, true));
			Assert.AreEqual("Banker", MapMarkerFilter.ResolveLabel(banker, false, true));
			Assert.AreEqual("Bramwell", MapMarkerFilter.ResolveLabel(banker, true, false));
			Assert.IsNull(MapMarkerFilter.ResolveLabel(banker, false, false));
		}

		[Test]
		public void Npc_IgnoresAStaticLabelItsPrefabInherited()
		{
			// A simulated player cloned from the banker kept "Bank"; it has no role, so it has no title.
			MapMarker simulated = NewNpc("Aldric Stonewell", null, "Bank");

			Assert.AreEqual("Aldric Stonewell", MapMarkerFilter.ResolveLabel(simulated, true, true));
			Assert.IsNull(MapMarkerFilter.ResolveLabel(simulated, false, true), "Titles only: an NPC without a role has nothing to show.");
			Assert.AreEqual("Aldric Stonewell", MapMarkerFilter.ResolveTooltip(simulated));
		}

		[Test]
		public void Npc_Tooltip_NamesItInFull()
		{
			MapMarker banker = NewNpc("Bramwell", "Banker");
			Assert.AreEqual("Bramwell <Banker>", MapMarkerFilter.ResolveTooltip(banker));
		}

		[Test]
		public void Npc_Label_FollowsARename()
		{
			MapMarker npc = NewNpc("a townsperson", "Merchant");
			Assert.AreEqual("a townsperson <Merchant>", MapMarkerFilter.ResolveLabel(npc, true, true));

			// The naming system renames a generated NPC after it wakes.
			npc.gameObject.name = "Wynn Ashgrove";
			Assert.AreEqual("Wynn Ashgrove <Merchant>", MapMarkerFilter.ResolveLabel(npc, true, true));
		}

		[Test]
		public void Npc_CombinedLabel_IsKeptBetweenRefreshes()
		{
			MapMarker banker = NewNpc("Bramwell", "Banker");
			string first = MapMarkerFilter.ResolveLabel(banker, true, true);
			string second = MapMarkerFilter.ResolveLabel(banker, true, true);
			Assert.AreSame(first, second, "The map asks ten times a second; an unchanged label must not be rebuilt.");
		}

		[Test]
		public void Fixture_KeepsItsAuthoredLabel()
		{
			MapMarker entrance = NewFixture("The Sunken Vault");
			Assert.AreEqual("The Sunken Vault", MapMarkerFilter.ResolveLabel(entrance, true, true));
			Assert.AreEqual("The Sunken Vault", MapMarkerFilter.ResolveTooltip(entrance));
		}

		[Test]
		public void Fixture_WithHoverTextOnly_HasNoLabelButATooltip()
		{
			// A plot for sale: a square on the map that says what it costs only when hovered.
			MapMarker plot = NewFixture(null, "Plot for sale (2,800 gold)");
			Assert.IsNull(MapMarkerFilter.ResolveLabel(plot, true, true));
			Assert.AreEqual("Plot for sale (2,800 gold)", MapMarkerFilter.ResolveTooltip(plot));
		}

		// ── Guide ───────────────────────────────────────────────────

		[Test]
		public void Guide_NeverTargetsThePlayersOwnMarker()
		{
			MapMarkerSnapshot self = new MapMarkerSnapshot() { Type = MapMarkerType.Self };
			Assert.IsFalse(MapGuide.Set(in self, "Scene"));
			Assert.IsFalse(MapGuide.HasTarget);
		}

		[Test]
		public void Guide_Note_IsMatchedByIdentityInItsSceneOnly()
		{
			MapMarkerSnapshot note = new MapMarkerSnapshot()
			{
				Type = MapMarkerType.Note,
				NoteID = 42,
				Position = new Vector3(10.0f, 0.0f, 20.0f),
				Label = "Herbs",
			};
			Assert.IsTrue(MapGuide.Set(in note, "Moron 2"));
			Assert.AreEqual("Herbs", MapGuide.Name);

			Assert.IsTrue(MapGuide.IsTarget(in note, "Moron 2"));
			Assert.IsFalse(MapGuide.IsTarget(in note, "Moron 1"), "Another scene's note 42 is a different note.");

			MapMarkerSnapshot other = note;
			other.NoteID = 43;
			Assert.IsFalse(MapGuide.IsTarget(in other, "Moron 2"));
		}

		[Test]
		public void Guide_Place_IsMatchedByTypeAndPosition()
		{
			MapMarkerSnapshot shrine = new MapMarkerSnapshot()
			{
				Type = MapMarkerType.Shrine,
				Position = new Vector3(100.0f, 5.0f, -40.0f),
				Tooltip = "Mossy Shrine\nAn old stone shrine.",
			};
			Assert.IsTrue(MapGuide.Set(in shrine, "Scene"));
			Assert.AreEqual("Mossy Shrine", MapGuide.Name, "Unlabelled at this zoom: named from the tooltip's first line.");
			Assert.IsTrue(MapGuide.IsTarget(in shrine, "Scene"));

			MapMarkerSnapshot elsewhere = shrine;
			elsewhere.Position += new Vector3(30.0f, 0.0f, 0.0f);
			Assert.IsFalse(MapGuide.IsTarget(in elsewhere, "Scene"));

			MapMarkerSnapshot waypointThere = shrine;
			waypointThere.IsWaypoint = true;
			Assert.IsFalse(MapGuide.IsTarget(in waypointThere, "Scene"));
		}

		[Test]
		public void Guide_Live_IsMatchedByItsMarker_AndLetGoWhenItLeavesTheRegistry()
		{
			MapMarker npc = NewNpc("Bramwell", "Banker");
			MapMarkerSnapshot snapshot = new MapMarkerSnapshot()
			{
				Source = npc,
				Type = MapMarkerType.Service,
				Position = new Vector3(1.0f, 0.0f, 2.0f),
				TracksSource = true,
				Label = "Bramwell <Banker>",
			};
			Assert.IsTrue(MapGuide.Set(in snapshot, "Scene"));
			Assert.IsTrue(MapGuide.IsLive);
			Assert.IsTrue(MapGuide.IsTarget(in snapshot, "Scene"));

			// Despawned or pooled: a pooled object comes back as another creature, so the marker is let go.
			MapMarkerRegistry.Unregister(npc);
			Assert.IsTrue(MapGuide.HasTarget, "The target stays, at the last place it was seen.");
			Assert.IsFalse(MapGuide.IsLive);
			Assert.IsTrue(MapGuide.IsLastKnown);
			Assert.IsFalse(MapGuide.IsTarget(in snapshot, "Scene"), "The pooled marker is not the target any more.");
		}

		[Test]
		public void Guide_ClearsOnASceneChange_ButNotOnTheSameScene()
		{
			MapMarkerSnapshot place = new MapMarkerSnapshot() { Type = MapMarkerType.Tower, Position = Vector3.one };
			MapGuide.Set(in place, "Moron 2");

			MapGuide.OnSceneChanged("Moron 2");
			Assert.IsTrue(MapGuide.HasTarget);

			MapGuide.OnSceneChanged("Moron 1");
			Assert.IsFalse(MapGuide.HasTarget);
		}

		[Test]
		public void Guide_NoteTarget_ClearsWhenTheNoteIsRemoved()
		{
			MapMarkerSnapshot note = new MapMarkerSnapshot() { Type = MapMarkerType.Note, NoteID = 7 };
			MapGuide.Set(in note, "Scene");

			MapGuide.OnNotesChanged(id => id == 7);
			Assert.IsTrue(MapGuide.HasTarget);

			MapGuide.OnNotesChanged(id => false);
			Assert.IsFalse(MapGuide.HasTarget);
		}

		[Test]
		public void Guide_UnnamedMarker_IsNamedAfterItsType()
		{
			MapMarkerSnapshot plot = new MapMarkerSnapshot() { Type = MapMarkerType.HousingPlot };
			Assert.AreEqual("Housing Plot", MapGuide.DescribeSnapshot(in plot));
		}

		// ── Probes ──────────────────────────────────────────────────

		/// <summary>A character the marker can find on its object; its name is the GameObject's, as an NPC's is.</summary>
		private sealed class ProbeCharacter : MonoBehaviour, ICharacter
		{
			public long ID { get; set; }
			public string Name => name;
			public Transform Transform => transform;
			public GameObject GameObject => gameObject;
			public Collider Collider { get; set; }
			public FishNet.Connection.NetworkConnection Owner => null;
			public FishNet.Object.NetworkObject NetworkObject => null;
			public FishNet.Managing.Predicting.PredictionManager PredictionManager => null;
			public HashSet<FishNet.Connection.NetworkConnection> Observers { get; } = new HashSet<FishNet.Connection.NetworkConnection>();
			public bool IsTeleporting => false;
			public bool IsSpawned => true;
			public int Flags { get; set; }
			public Nameplate CharacterNameplate { get; set; }
			public Transform MeshRoot => null;
#if !UNITY_SERVER
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex) { }
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex, CharacterGender gender) { }
#endif
			public void EnableFlags(CharacterFlags flags) => Flags |= (int)flags;
			public void DisableFlags(CharacterFlags flags) => Flags &= ~(int)flags;
			public bool IsFlagged(CharacterFlags flags) => (Flags & (int)flags) != 0;
			public void RegisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour { control = null; return false; }
			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}

		/// <summary>An interactable with a title, as a banker or merchant has.</summary>
		private sealed class ProbeInteractable : MonoBehaviour, IInteractable
		{
			public string ProbeTitle;
			public long ID { get; set; }
			public GameObject GameObject => gameObject;
			public Transform Transform => transform;
			public string Name => name;
			public string Title => ProbeTitle;
			public Color TitleColor => Color.white;
			public bool InRange(Transform other) => true;
			public bool CanInteract(IPlayerCharacter character) => true;
			public double InteractRateLimit => 0.0;
			public bool TryConsumeInteractRateLimit(IPlayerCharacter character) => true;
			public List<Trigger> OnInteractTriggers { get; } = new List<Trigger>();
			public bool ExecuteOnInteract(EventData eventData) => false;
		}
	}
}
