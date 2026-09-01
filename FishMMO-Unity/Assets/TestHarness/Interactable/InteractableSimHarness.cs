using System.Collections;
using System.Collections.Generic;
using FishMMO.Server;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishNet.Managing;
using UnityEngine;

namespace FishMMO.TestHarness
{
	/// <summary>
	/// End-to-end interaction simulation: a real server spawns real interactable NPCs and a real
	/// player character, and a scripted walker drives the exact server-side interact chain the
	/// live game runs — <c>CanAct</c>, scene-object resolution by ID, <c>InteractableResolver</c>,
	/// <c>CanInteract</c>, the rate limit, and <c>ExecuteOnInteract</c> — against cases whose
	/// answers are known in advance.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Written for the live report "can't interact with NPCs on the latest update". Every step
	/// of that chain refuses in a way a player experiences identically (nothing happens), so the
	/// sim asserts each refusal separately: in range must succeed, out of range must fail, a
	/// second interact inside the debounce must fail, a corpse must fail, an unregistered id must
	/// fail, and a character that cannot act must fail. A regression that breaks interaction
	/// moves one of those counters.
	/// </para>
	/// <para>
	/// The one thing it does not reproduce is the broadcast transport and the world-scene-details
	/// cache lookup that fronts the handler, both of which need a connected client and a database.
	/// It starts where <c>InteractableSystem</c> has a character and an id, which is where every
	/// silent refusal lives.
	/// </para>
	/// </remarks>
	public sealed class InteractableSimHarness : MonoBehaviour
	{
		[Tooltip("Shared manifest — supplies the NetworkManager prefab and template cache.")]
		public CombatSimManifest Manifest;

		[Tooltip("Interactable NPC prefab (a banker: no combat, pure interaction).")]
		public GameObject InteractablePrefab;

		[Tooltip("Player character prefab used as the interactor.")]
		public GameObject PlayerPrefab;

		[Tooltip("Tugboat listen port — nothing connects.")]
		public ushort Port = 7798;

		[Tooltip("Seconds between scripted interaction attempts.")]
		public float StepInterval = 0.35f;

		private readonly SimServer server = new SimServer();
		private NetworkManager networkManager;
		private IPlayerCharacter interactor;
		private Transform interactorTransform;
		private readonly List<NPC> interactables = new List<NPC>();

		/// <summary>One scripted probe: what to try, and what the chain must answer.</summary>
		private struct Case
		{
			public string Name;
			public bool ExpectSuccess;
			public int Passed;
			public int Failed;
		}

		private Case[] cases;
		private int caseCursor;
		private long attempts;
		private string lastOutcome = "";
		private bool ready;
		private float clock;

		private const int InRangeCase = 0;
		private const int OutOfRangeCase = 1;
		private const int RateLimitedCase = 2;
		private const int CorpseCase = 3;
		private const int UnknownIdCase = 4;
		private const int CannotActCase = 5;

		private IEnumerator Start()
		{
#if UNITY_EDITOR
			if (Manifest == null)
			{
				Manifest = UnityEditor.AssetDatabase.LoadAssetAtPath<CombatSimManifest>(
					"Assets/TestHarness/Combat/CombatSimManifest.asset");
			}
			if (InteractablePrefab == null)
			{
				InteractablePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
					"Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Banker/HumanBanker.prefab");
			}
			if (PlayerPrefab == null)
			{
				PlayerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
					"Assets/Prefabs/Shared/Entity/PlayableCharacters/Human.prefab");
			}
#endif
			if (InteractablePrefab == null || PlayerPrefab == null)
			{
				Debug.LogError("[InteractableSim] Missing interactable or player prefab.");
				yield break;
			}

			BuildGround();

			yield return server.Boot(Manifest, transform, Port);
			networkManager = server.NetworkManager;
			if (networkManager == null)
			{
				yield break;
			}

			// Two bankers: one the walker stands next to, one it never approaches.
			interactables.Add(SpawnInteractable(new Vector3(0f, 0f, 0f), "NearBanker"));
			interactables.Add(SpawnInteractable(new Vector3(25f, 0f, 0f), "FarBanker"));

			GameObject playerGo = server.Spawn(PlayerPrefab, new Vector3(0f, 0f, -1.5f), Quaternion.identity,
				gameObject.scene, configure: go => go.name = "SimInteractor");
			interactor = playerGo.GetComponent<IPlayerCharacter>();
			interactorTransform = playerGo.transform;
			if (interactor == null)
			{
				Debug.LogError("[InteractableSim] Player prefab carries no IPlayerCharacter.");
				yield break;
			}
			// A character the server considers actionable: loaded, alive, not teleporting. In
			// production CharacterSystem sets this after the DB load completes.
			interactor.EnableFlags(CharacterFlags.IsLoaded);

			cases = new[]
			{
				new Case { Name = "in range → interacts", ExpectSuccess = true },
				new Case { Name = "out of range → refused", ExpectSuccess = false },
				new Case { Name = "inside debounce → refused", ExpectSuccess = false },
				new Case { Name = "corpse → refused", ExpectSuccess = false },
				new Case { Name = "unregistered id → refused", ExpectSuccess = false },
				new Case { Name = "cannot act → refused", ExpectSuccess = false },
			};

			ready = true;
		}

		private void OnDestroy()
		{
			server.Shutdown();
		}

		private void BuildGround()
		{
			GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
			ground.name = "Ground";
			ground.transform.SetParent(transform, false);
			ground.transform.position = new Vector3(0f, -0.5f, 0f);
			ground.transform.localScale = new Vector3(60f, 1f, 30f);
		}

		private NPC SpawnInteractable(Vector3 position, string name)
		{
			GameObject clone = server.Spawn(InteractablePrefab, position, Quaternion.identity,
				gameObject.scene, configure: go =>
				{
					go.name = name;
					AIController ai = go.GetComponent<AIController>();
					if (ai != null)
					{
						ai.LodSettings = null;
					}
				},
				afterActivate: go =>
				{
					AIController ai = go.GetComponent<AIController>();
					if (ai != null)
					{
						ai.Initialize(position);
					}
				});
			return clone.GetComponent<NPC>();
		}

		private void Update()
		{
			if (!ready)
			{
				return;
			}
			clock += Time.deltaTime;
			if (clock < StepInterval)
			{
				return;
			}
			clock = 0f;
			RunNextCase();
		}

		/// <summary>Runs one scripted probe and scores it against its expected answer.</summary>
		private void RunNextCase()
		{
			int index = caseCursor % cases.Length;
			caseCursor++;
			attempts++;

			NPC near = interactables[0];
			NPC far = interactables[1];
			bool result;

			/* The id a CLIENT would send. A banker NPC registers TWO scene objects — the Banker
			 * component and the NPC's own lootable corpse — and Resolve(ISceneObject) is
			 * identity-first, so sending the NPC's id asks to loot it (refused while alive).
			 * The client's target resolution picks which component the player MEANS via
			 * InteractableResolver.Resolve(GameObject) — living banker → the Banker, corpse →
			 * the loot — and sends that component's id; the harness does the same. */
			long nearId = MeantId(near);
			long farId = MeantId(far);

			string refusedAt = null;
			switch (index)
			{
				case InRangeCase:
					// Stand next to the banker, clear the debounce, interact.
					interactorTransform.position = near.Transform.position + new Vector3(0f, 0f, -1.5f);
					interactor.NextInteractTime = System.DateTime.MinValue;
					result = TryInteract(nearId, out refusedAt);
					break;

				case OutOfRangeCase:
					interactor.NextInteractTime = System.DateTime.MinValue;
					result = TryInteract(farId, out refusedAt);
					break;

				case RateLimitedCase:
					/* Two interacts back to back. The first must succeed and the second must not:
					 * the debounce is consumed by TryConsumeInteractRateLimit, deliberately after
					 * CanInteract answers, so that asking the question does not spend the budget. */
					interactorTransform.position = near.Transform.position + new Vector3(0f, 0f, -1.5f);
					interactor.NextInteractTime = System.DateTime.MinValue;
					bool first = TryInteract(nearId, out string firstRefusedAt);
					result = TryInteract(nearId, out refusedAt);
					if (!first)
					{
						Debug.LogError("[InteractableSim] The rate-limit case could not even get its FIRST " +
							$"interact through — refused at: {firstRefusedAt}.");
					}
					break;

				case CorpseCase:
					// Corpse the far banker, then interact from point blank.
					interactorTransform.position = far.Transform.position + new Vector3(0f, 0f, -1.5f);
					interactor.NextInteractTime = System.DateTime.MinValue;
					far.Despawn();
					result = TryInteract(farId, out refusedAt);
					break;

				case UnknownIdCase:
					interactor.NextInteractTime = System.DateTime.MinValue;
					result = TryInteract(long.MaxValue);
					break;

				default:
					// A character that cannot act: the gate that fronts every state-mutating
					// handler, and the one whose refusal is invisible to the player.
					interactorTransform.position = near.Transform.position + new Vector3(0f, 0f, -1.5f);
					interactor.NextInteractTime = System.DateTime.MinValue;
					interactor.DisableFlags(CharacterFlags.IsLoaded);
					result = TryInteract(nearId, out refusedAt);
					interactor.EnableFlags(CharacterFlags.IsLoaded);
					break;
			}

			if (result == cases[index].ExpectSuccess)
			{
				cases[index].Passed++;
			}
			else
			{
				cases[index].Failed++;
				Debug.LogError($"[InteractableSim] '{cases[index].Name}' — expected " +
					$"{(cases[index].ExpectSuccess ? "success" : "refusal")}, got " +
					$"{(result ? "success" : "refusal")}" +
					(refusedAt != null ? $" (refused at: {refusedAt})" : "") + ".");
			}
			lastOutcome = $"{cases[index].Name}: {(result ? "interacted" : "refused")}";
		}

		/// <summary>The scene-object id a client targeting this NPC would send — the id of the
		/// component <c>InteractableResolver</c> says the player means.</summary>
		private static long MeantId(NPC npc)
		{
			IInteractable meant = InteractableResolver.Resolve(npc.GameObject);
			return meant is ISceneObject sceneObject ? sceneObject.ID : npc.ID;
		}

		/// <summary>
		/// The server's interact chain, run against a scene-object id exactly as
		/// <c>InteractableSystem.OnServerInteractableBroadcastReceived</c> does once it holds a
		/// character and an id — same calls, same order, same short-circuits.
		/// </summary>
		private bool TryInteract(long sceneObjectID)
		{
			return TryInteract(sceneObjectID, out _);
		}

		private bool TryInteract(long sceneObjectID, out string refusedAt)
		{
			if (!CharacterStateValidation.CanAct(interactor))
			{
				refusedAt = $"CanAct (flags 0x{interactor.Flags:X}, teleporting {interactor.IsTeleporting})";
				return false;
			}
			if (!SceneObject.Objects.TryGetValue(sceneObjectID, out ISceneObject sceneObject))
			{
				refusedAt = $"registry (id {sceneObjectID} not registered; {SceneObject.Objects.Count} objects known)";
				return false;
			}
			if (sceneObject.GameObject == null || !sceneObject.GameObject.activeInHierarchy)
			{
				refusedAt = "liveness (scene object destroyed or pooled-inactive)";
				return false;
			}
			if (sceneObject.GameObject.scene.handle != interactor.GameObject.scene.handle)
			{
				refusedAt = $"scene handle ({sceneObject.GameObject.scene.name} vs {interactor.GameObject.scene.name})";
				return false;
			}
			IInteractable interactable = InteractableResolver.Resolve(sceneObject);
			if (interactable == null)
			{
				refusedAt = "resolve (scene object carries no interactable / corpse rule)";
				return false;
			}
			if (!interactable.CanInteract(interactor))
			{
				refusedAt = $"CanInteract (corpse {InteractableResolver.IsCorpse(interactable.GameObject)}, " +
					$"inRange {interactable.InRange(interactor.Transform)})";
				return false;
			}
			if (!interactable.TryConsumeInteractRateLimit(interactor))
			{
				refusedAt = "rate limit";
				return false;
			}
			interactable.ExecuteOnInteract(new PlayerInteractionEventData(interactor, interactable));
			refusedAt = null;
			return true;
		}

		private void OnGUI()
		{
			GUILayout.BeginArea(new Rect(10, 10, 460, 300), GUI.skin.box);
			GUILayout.Label("<b>Interaction Simulation — server chain, scripted probes</b>", Rich());
			if (!ready)
			{
				GUILayout.Label("starting server / spawning…");
				GUILayout.EndArea();
				return;
			}
			GUILayout.Label($"attempts: {attempts}   last: {lastOutcome}");
			GUILayout.Space(4);
			bool allPass = true;
			bool anyRun = false;
			foreach (Case probe in cases)
			{
				GUILayout.Label($"{probe.Name}  —  {probe.Passed} ok / {probe.Failed} wrong");
				allPass &= probe.Failed == 0;
				anyRun |= probe.Passed + probe.Failed > 0;
			}
			GUI.color = !anyRun ? Color.yellow : (allPass ? Color.green : Color.red);
			GUILayout.Label(!anyRun ? "<b>warming up…</b>" : (allPass ? "<b>PASS</b>" : "<b>FAIL</b>"), Rich());
			GUI.color = Color.white;
			GUILayout.EndArea();
		}

		private static GUIStyle Rich()
		{
			return new GUIStyle(GUI.skin.label) { richText = true };
		}

		// ── Accessors for the PlayMode suite ────────────────────────────────────────

		public bool Ready => ready;
		public long Attempts => attempts;

		public int TotalWrong
		{
			get
			{
				int wrong = 0;
				if (cases != null)
				{
					foreach (Case probe in cases)
					{
						wrong += probe.Failed;
					}
				}
				return wrong;
			}
		}

		public int CasesCovered
		{
			get
			{
				int covered = 0;
				if (cases != null)
				{
					foreach (Case probe in cases)
					{
						if (probe.Passed + probe.Failed > 0) covered++;
					}
				}
				return covered;
			}
		}

		public int CaseCount => cases?.Length ?? 0;
	}
}
