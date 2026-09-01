using System.Collections;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Component.Prediction;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

namespace FishMMO.TestHarness
{
	/// <summary>
	/// End-to-end region simulation: real <see cref="Region"/> components with real
	/// <c>NetworkTrigger</c> colliders on a real server, and a scripted walker that crosses them
	/// on a fixed route so enter/stay/exit, nested-region ownership, and the region-owned
	/// attribute ledger can all be checked against known-correct answers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The three properties it pins are the ones a region bug actually breaks, and none of them
	/// are visible from a single crossing: <b>pairing</b> (every Enter is eventually matched by
	/// exactly one Exit — an unpaired Enter is a character stranded inside forever),
	/// <b>nesting</b> (while inside a child region the parent must not also own the character —
	/// the parent hands ownership down and takes it back on the way out), and <b>ledger
	/// symmetry</b> (an <see cref="ApplyRegionAttributeAction"/> contribution is keyed to
	/// <c>ModifierSource.Region</c> and must be fully released on exit, so a walker that has
	/// looped many times ends with exactly its base attribute value — the failure mode this
	/// guards is a bonus that accumulates once per lap, or once per stay tick).
	/// </para>
	/// <para>
	/// Regions are built at runtime rather than authored, so the scene file stays a one-object
	/// bootstrap and the geometry can never drift from the assertions written against it.
	/// </para>
	/// </remarks>
	public sealed class RegionSimHarness : MonoBehaviour
	{
		[Tooltip("Shared manifest — supplies the NetworkManager prefab and template cache.")]
		public CombatSimManifest Manifest;

		[Tooltip("Player character prefab used as the walker.")]
		public GameObject PlayerPrefab;

		[Tooltip("Attribute the outer region boosts while a character is inside it.")]
		public CharacterAttributeTemplate BoostedAttribute;

		[Tooltip("Tugboat listen port — nothing connects.")]
		public ushort Port = 7799;

		[Tooltip("Walker speed, metres per second.")]
		public float WalkSpeed = 6f;

		private readonly SimServer server = new SimServer();
		private NetworkManager networkManager;

		private Region outerRegion;
		private Region innerRegion;
		private Region separateRegion;
		private IPlayerCharacter walker;
		private Transform walkerTransform;
		private ICharacterAttributeController walkerAttributes;

		private const int RegionBoost = 25;

		/// <summary>The fixed patrol: outside → outer → inner (nested) → outer → outside → the
		/// separate region → outside, repeating. Every leg is a membership transition.</summary>
		private static readonly Vector3[] Route =
		{
			new Vector3(-18f, 0f, 0f),
			new Vector3(-6f, 0f, 0f),
			new Vector3(0f, 0f, 0f),
			new Vector3(-6f, 0f, 0f),
			new Vector3(-18f, 0f, 0f),
			new Vector3(16f, 0f, 0f),
			new Vector3(24f, 0f, 0f),
		};

		private int routeCursor;
		private bool ready;

		// ── Observed facts ──────────────────────────────────────────────────────────
		private readonly Dictionary<Region, int> enters = new Dictionary<Region, int>();
		private readonly Dictionary<Region, int> exits = new Dictionary<Region, int>();
		private int laps;
		private int nestingViolations;
		private int ledgerViolations;
		private int baseAttributeValue = -1;
		private int maxObservedAttribute;
		private string lastEvent = "";

		private IEnumerator Start()
		{
#if UNITY_EDITOR
			if (Manifest == null)
			{
				Manifest = UnityEditor.AssetDatabase.LoadAssetAtPath<CombatSimManifest>(
					"Assets/TestHarness/Combat/CombatSimManifest.asset");
			}
			if (PlayerPrefab == null)
			{
				PlayerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
					"Assets/Prefabs/Shared/Entity/PlayableCharacters/Human.prefab");
			}
			if (BoostedAttribute == null)
			{
				BoostedAttribute = UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterAttributeTemplate>(
					"Assets/Templates/Entity/CharacterAttributes/Speed/Attack Speed.asset");
			}
#endif
			if (PlayerPrefab == null)
			{
				Debug.LogError("[RegionSim] No player prefab.");
				yield break;
			}

			BuildGround();

			yield return server.Boot(Manifest, transform, Port);
			networkManager = server.NetworkManager;
			if (networkManager == null)
			{
				yield break;
			}

			/* The outer region carries the attribute boost; the inner sits wholly inside it and is
			 * registered as its child, which is what makes the parent yield ownership. The
			 * separate region is untouched by either and catches cross-talk. */
			outerRegion = BuildRegion("OuterRegion", new Vector3(-6f, 2f, 0f), new Vector3(16f, 6f, 12f),
				null, boost: true, new Color(0.2f, 0.6f, 1f, 0.15f));
			innerRegion = BuildRegion("InnerRegion", new Vector3(0f, 2f, 0f), new Vector3(5f, 6f, 5f),
				outerRegion, boost: false, new Color(1f, 0.75f, 0.2f, 0.2f));
			separateRegion = BuildRegion("SeparateRegion", new Vector3(20f, 2f, 0f), new Vector3(8f, 6f, 8f),
				null, boost: false, new Color(0.4f, 1f, 0.4f, 0.15f));

			GameObject walkerGo = server.Spawn(PlayerPrefab, Route[0], Quaternion.identity,
				gameObject.scene, configure: go => go.name = "RegionWalker");
			walker = walkerGo.GetComponent<IPlayerCharacter>();
			walkerTransform = walkerGo.transform;
			if (walker == null)
			{
				Debug.LogError("[RegionSim] Player prefab carries no IPlayerCharacter.");
				yield break;
			}
			walker.EnableFlags(CharacterFlags.IsLoaded);
			walker.TryGet(out walkerAttributes);

			// One frame for the spawn chain to seed attributes, then record the untouched baseline.
			yield return null;
			if (walkerAttributes != null && BoostedAttribute != null
				&& walkerAttributes.TryGetAttribute(BoostedAttribute, out CharacterAttribute attribute))
			{
				baseAttributeValue = attribute.FinalValue;
				maxObservedAttribute = baseAttributeValue;
			}

			ready = true;
		}

		private void OnDestroy()
		{
			// Regions are ROOT objects (scene-move rule), so the hierarchy teardown that removes
			// everything else does not reach them.
			foreach (Region region in new[] { innerRegion, separateRegion, outerRegion })
			{
				if (region != null)
				{
					Destroy(region.gameObject);
				}
			}
			server.Shutdown();
		}

		private void BuildGround()
		{
			GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
			ground.name = "Ground";
			ground.transform.SetParent(transform, false);
			ground.transform.position = new Vector3(0f, -0.5f, 0f);
			ground.transform.localScale = new Vector3(70f, 1f, 30f);
		}

		/// <summary>
		/// Builds one real region: BoxCollider (Region.Awake forces isTrigger), NetworkTrigger,
		/// NetworkObject, and — when <paramref name="boost"/> — an enter/exit trigger pair
		/// carrying the production <see cref="ApplyRegionAttributeAction"/>, so the ledger under
		/// test is the shipped one and not a stand-in.
		/// </summary>
		private Region BuildRegion(string name, Vector3 center, Vector3 size, Region parent, bool boost, Color tint)
		{
			/* Root, deliberately: ServerManager.Spawn moves the object into its target scene,
			 * and Unity only moves ROOT objects between scenes — a parented region makes FishNet
			 * log an error and skip the move. Cleanup does not rely on the hierarchy anyway; the
			 * server despawns everything it owns on StopConnection. */
			GameObject go = new GameObject(name);
			go.SetActive(false);
			go.transform.position = center;

			BoxCollider box = go.AddComponent<BoxCollider>();
			box.size = size;

			/* Explicit query layers. With the protected Layers field left at 0 a NetworkCollider
			 * derives its mask from the physics matrix row of its OWN layer — and Region.Awake
			 * moves the region onto IgnoreRaycast, whose row excludes the walker's layer here, so
			 * the trigger polls forever and sees nobody. Shipped region objects author Layers in
			 * the scene file; the harness authors it by reflection (protected field). */
			NetworkTrigger trigger = go.AddComponent<NetworkTrigger>();
			typeof(FishNet.Component.Prediction.NetworkColliderBase)
				.GetField("Layers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.SetValue(trigger, (LayerMask)~0);
			NetworkObject nob = go.AddComponent<NetworkObject>();

			Region region = go.AddComponent<Region>();
			region.Parent = parent;
			if (boost && BoostedAttribute != null)
			{
				/* A region-owned attribute contribution, authored the way content authors do.
				 * The removal is deliberately NOT wired here: Region.ReleaseAttributeContributions
				 * drops it on every path that ends membership, and that is exactly the guarantee
				 * this sim is here to check. */
				Trigger enterTrigger = ScriptableObject.CreateInstance<Trigger>();
				enterTrigger.name = name + " Enter Boost";
				enterTrigger.OnConditionsMetActions.Add(new ApplyRegionAttributeAction
				{
					Attribute = BoostedAttribute,
					Value = RegionBoost,
				});
				region.OnRegionEnter.Add(enterTrigger);
			}

			go.SetActive(true);
			networkManager.ServerManager.Spawn(nob, null, gameObject.scene);

			// A translucent shell so the regions are visible in the scene view.
			GameObject shell = GameObject.CreatePrimitive(PrimitiveType.Cube);
			Destroy(shell.GetComponent<Collider>());
			shell.name = name + " (shell)";
			shell.transform.SetParent(go.transform, false);
			shell.transform.localScale = size;
			Material material = new Material(Shader.Find("Sprites/Default"));
			material.color = tint;
			shell.GetComponent<Renderer>().sharedMaterial = material;

			enters[region] = 0;
			exits[region] = 0;
			return region;
		}

		private void Update()
		{
			if (!ready)
			{
				return;
			}

			// Walk the fixed route.
			Vector3 goal = Route[routeCursor];
			Vector3 here = walkerTransform.position;
			Vector3 planar = new Vector3(goal.x - here.x, 0f, goal.z - here.z);
			if (planar.magnitude < 0.15f)
			{
				routeCursor++;
				if (routeCursor >= Route.Length)
				{
					routeCursor = 0;
					laps++;
				}
			}
			else
			{
				walkerTransform.position = here + planar.normalized * (WalkSpeed * Time.deltaTime);
			}
			// Regions poll their colliders after the physics simulation; the walker is moved by
			// transform, so the physics state has to be pushed before the next query.
			Physics.SyncTransforms();

			ObserveMembership();
			ObserveLedger();
		}

		/// <summary>Counts Enter/Exit edges from <c>Region.Contains</c> and checks nesting.</summary>
		private readonly Dictionary<Region, bool> wasInside = new Dictionary<Region, bool>();

		private void ObserveMembership()
		{
			foreach (Region region in new[] { outerRegion, innerRegion, separateRegion })
			{
				bool inside = region.Contains(walker);
				wasInside.TryGetValue(region, out bool before);
				if (inside != before)
				{
					if (inside)
					{
						enters[region]++;
						lastEvent = $"ENTER {region.Name}";
					}
					else
					{
						exits[region]++;
						lastEvent = $"EXIT {region.Name}";
					}
					wasInside[region] = inside;
				}
			}

			/* Nesting: the child takes ownership, so the parent must not also hold the character.
			 * Both true at once means a character is a member of two regions on one point, and
			 * every per-region effect (fog, music, attribute) applies twice. */
			if (innerRegion.Contains(walker) && outerRegion.Contains(walker))
			{
				nestingViolations++;
			}
		}

		/// <summary>
		/// Checks the region-owned attribute ledger: inside the booster the attribute must read
		/// exactly base + boost (never more, however many laps have run), and outside it must read
		/// exactly base.
		/// </summary>
		private void ObserveLedger()
		{
			if (baseAttributeValue < 0 || walkerAttributes == null || BoostedAttribute == null
				|| !walkerAttributes.TryGetAttribute(BoostedAttribute, out CharacterAttribute attribute))
			{
				return;
			}

			int value = attribute.FinalValue;
			if (value > maxObservedAttribute)
			{
				maxObservedAttribute = value;
			}

			// Only judged when the walker is unambiguously outside every region, where the answer
			// is not a matter of timing: nothing may still be contributing.
			bool outsideEverything = !outerRegion.Contains(walker)
				&& !innerRegion.Contains(walker)
				&& !separateRegion.Contains(walker);
			if (outsideEverything && value != baseAttributeValue)
			{
				ledgerViolations++;
				Debug.LogError($"[RegionSim] Attribute ledger leaked: outside every region " +
					$"'{BoostedAttribute.name}' reads {value}, base is {baseAttributeValue}. A region " +
					"contribution was not released on exit.");
			}
			if (value > baseAttributeValue + RegionBoost)
			{
				ledgerViolations++;
				Debug.LogError($"[RegionSim] Attribute ledger accumulated: '{BoostedAttribute.name}' " +
					$"reads {value}, above base {baseAttributeValue} + one boost {RegionBoost}. " +
					"A region contribution is stacking instead of restating.");
			}
		}

		private void OnGUI()
		{
			GUILayout.BeginArea(new Rect(10, 10, 470, 320), GUI.skin.box);
			GUILayout.Label("<b>Region Simulation — enter / exit / nesting / ledger</b>", Rich());
			if (!ready)
			{
				GUILayout.Label("starting server / building regions…");
				GUILayout.EndArea();
				return;
			}
			GUILayout.Label($"laps: {laps}   last: {lastEvent}");
			foreach (Region region in new[] { outerRegion, innerRegion, separateRegion })
			{
				GUILayout.Label($"{region.Name}: inside={region.Contains(walker)}  " +
					$"enters={enters[region]}  exits={exits[region]}");
			}
			GUILayout.Label($"attribute: base {baseAttributeValue}, peak {maxObservedAttribute} " +
				$"(boost {RegionBoost})");
			GUILayout.Label($"nesting violations: {nestingViolations}   ledger violations: {ledgerViolations}");

			bool anyRun = laps > 0;
			bool pass = nestingViolations == 0 && ledgerViolations == 0 && PairingIsSound;
			GUI.color = !anyRun ? Color.yellow : (pass ? Color.green : Color.red);
			GUILayout.Label(!anyRun ? "<b>warming up…</b>" : (pass ? "<b>PASS</b>" : "<b>FAIL</b>"), Rich());
			GUI.color = Color.white;
			GUILayout.Label("Blue = outer (boosting) region, orange = nested child, green = separate.");
			GUILayout.EndArea();
		}

		private static GUIStyle Rich()
		{
			return new GUIStyle(GUI.skin.label) { richText = true };
		}

		// ── Accessors for the PlayMode suite ────────────────────────────────────────

		public bool Ready => ready;
		public int Laps => laps;
		public int NestingViolations => nestingViolations;
		public int LedgerViolations => ledgerViolations;
		public int BaseAttributeValue => baseAttributeValue;
		public int MaxObservedAttribute => maxObservedAttribute;

		public int EnterCount(int region) => enters[Pick(region)];
		public int ExitCount(int region) => exits[Pick(region)];

		private Region Pick(int index) =>
			index == 0 ? outerRegion : index == 1 ? innerRegion : separateRegion;

		/// <summary>
		/// True when every region's exits trail its enters by at most one (the one being the
		/// crossing currently in progress). An exit count that falls further behind means
		/// characters are being left inside.
		/// </summary>
		public bool PairingIsSound
		{
			get
			{
				foreach (KeyValuePair<Region, int> entry in enters)
				{
					int exited = exits[entry.Key];
					if (entry.Value - exited > 1 || exited > entry.Value)
					{
						return false;
					}
				}
				return true;
			}
		}

		/// <summary>Total enters across all three regions — proves the sim actually crossed things.</summary>
		public int TotalEnters
		{
			get
			{
				int total = 0;
				foreach (KeyValuePair<Region, int> entry in enters)
				{
					total += entry.Value;
				}
				return total;
			}
		}
	}
}
