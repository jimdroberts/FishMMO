using System.Collections.Generic;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A player's mount (issue #109): a part every player character gets (<see cref="MountsModule"/>). It answers the core's
	/// <see cref="ICharacterMount"/> for the motor from the mount buff, gets its rider off when the rules say so, and on every
	/// client draws the mount under the rider.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One source of truth: the buff.</b> Riding is a <see cref="MountBuffTemplate"/> buff; this part holds nothing the buff
	/// set does not already say. The motor reads <see cref="TryGetMotion"/> each tick on the owner and the server (buffs are
	/// reconciled), and every client draws whatever mount buff it holds (observers materialise the server's buff list).
	/// </para>
	/// <para>
	/// <b>Getting off</b>. Predicted, inside the replicate on both peers: casting anything (the buff's
	/// BreaksOnAction, ended by the ability controller), pressing Mount again (<see cref="MountPressOverride"/>), deep water
	/// and ladders (the motor calls <see cref="Dismount"/>), the saddle coming off (the equipment's own predicted events). By
	/// the server, which the owner hears through the buff reconcile: a damaging hit, a roof or a cave overhead
	/// (<see cref="MountShelter"/>), a mount whose saddle is no longer worn. A death clears every buff anyway.
	/// </para>
	/// </remarks>
	public class MountController : CharacterPart, ICharacterMount
	{
		/// <summary>How often the server looks for a roof over a rider (and for a saddle under it), network ticks.</summary>
		public const int ServerCheckTicks = 8;

		/// <summary>Ticks after spawning before the server first looks (see <see cref="OnStartServer"/>).</summary>
		public const int ServerStartGraceTicks = 30;

		private IEquipmentController equipment;
		private bool serverTickHooked;
		private int serverCheckCountdown;

		// ── ICharacterMount ───────────────────────────────────────────────────

		/// <inheritdoc />
		public bool IsMounted => MountRules.ActiveMount(Character) != null;

		/// <inheritdoc />
		public bool TryGetMotion(out MountMotion motion)
		{
			MountBuffTemplate mount = MountRules.ActiveMount(Character);
			motion = mount != null ? mount.Motion : default;
			return mount != null;
		}

		/// <inheritdoc />
		/// <remarks>
		/// Only where buffs are simulated (the server and the owner): an observer's copy of the buff list is the server's to
		/// change, so a dismount there would only be put back by the next message.
		/// </remarks>
		public void Dismount()
		{
			if (Character == null || !Character.TryGet(out IBuffController buffs) || !buffs.SimulatesBuffEffects ||
				buffs.Buffs == null || buffs.Buffs.Count == 0)
			{
				return;
			}
			List<int> mounts = null;
			foreach (KeyValuePair<int, Buff> pair in buffs.Buffs)
			{
				if (pair.Value?.Template is MountBuffTemplate)
				{
					(mounts ??= new List<int>(1)).Add(pair.Key);
				}
			}
			if (mounts == null)
			{
				return;
			}
			for (int i = 0; i < mounts.Count; i++)
			{
				buffs.Remove(mounts[i]);
			}
		}

		// ── Lifecycle ─────────────────────────────────────────────────────────

		/// <inheritdoc />
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();
			MountsModule.EnsureHooks();
			if (Character != null && Character.TryGet(out equipment))
			{
				equipment.OnItemEquipped += Equipment_OnSaddleChanged;
				equipment.OnItemUnequipped += Equipment_OnSaddleChanged;
			}
		}

		/// <inheritdoc />
		public override void OnStopNetwork()
		{
			base.OnStopNetwork();
			if (equipment != null)
			{
				equipment.OnItemEquipped -= Equipment_OnSaddleChanged;
				equipment.OnItemUnequipped -= Equipment_OnSaddleChanged;
				equipment = null;
			}
			UnhookServerTick();
		}

		/// <inheritdoc />
		public override void OnStartServer()
		{
			base.OnStartServer();
			if (TimeManager != null && !serverTickHooked)
			{
				TimeManager.OnTick += Server_OnTick;
				serverTickHooked = true;
			}
			// A second's grace after spawning before the first look: a rider logging back in mounted is not got off by a
			// check that ran before its saddle was in its socket.
			serverCheckCountdown = ServerStartGraceTicks;
		}

		/// <inheritdoc />
		public override void OnStopServer()
		{
			base.OnStopServer();
			UnhookServerTick();
		}

		/// <inheritdoc />
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);
			serverCheckCountdown = 0;
#if !UNITY_SERVER
			HideMount();
#endif
		}

		/// <inheritdoc />
		public override void OnDestroying()
		{
			base.OnDestroying();
			UnhookServerTick();
		}

		private void UnhookServerTick()
		{
			if (serverTickHooked && TimeManager != null)
			{
				TimeManager.OnTick -= Server_OnTick;
			}
			serverTickHooked = false;
		}

		/// <summary>The saddle socket changed: a mount its saddle no longer names is got off (both peers, predicted).</summary>
		private void Equipment_OnSaddleChanged(Item item, ItemSlot slot)
		{
			if (slot != ItemSlot.MountSaddle)
			{
				return;
			}
			MountBuffTemplate riding = MountRules.ActiveMount(Character);
			if (riding != null && riding != MountRules.SaddleMount(equipment))
			{
				Dismount();
			}
		}

		/// <summary>The server's own reasons to get a rider off: a roof or a cave overhead, or a mount with no saddle for it.</summary>
		private void Server_OnTick()
		{
			if (--serverCheckCountdown > 0)
			{
				return;
			}
			serverCheckCountdown = ServerCheckTicks;
			MountBuffTemplate riding = MountRules.ActiveMount(Character);
			if (riding == null)
			{
				return;
			}
			if (riding != MountRules.SaddleMount(Character) || MountShelter.IsSheltered(Character))
			{
				Dismount();
			}
		}

#if !UNITY_SERVER
		// ── Drawing (every client) ───────────────────────────────────────────

		private static readonly int GaitId = Shader.PropertyToID("_WildGait");
		private static readonly int PoseId = Shader.PropertyToID("_WildPose");
		private static readonly int FlightId = Shader.PropertyToID("_WildFlight");
		private static readonly Dictionary<MountBuffTemplate, Mesh> meshes = new Dictionary<MountBuffTemplate, Mesh>();

		private MountBuffTemplate shown;
		private GameObject body;
		private MeshRenderer bodyRenderer;
		private MaterialPropertyBlock block;
		private CharacterAnimationController riderAnimation;
		private Transform labels;
		private Vector3 labelsRest;
		private Vector3 lastPosition;
		private bool hasLastPosition;
		private float speed;
		private float phase;
		private float wingPhase;
		private float airborne;
		private static int groundMask;

		/// <summary>The mount the rider is drawn on now (null on foot): for tests and probes.</summary>
		internal MountBuffTemplate ShownMount => shown;

		/// <summary>The drawn body (null on foot).</summary>
		internal GameObject ShownBody => body;

		private void LateUpdate()
		{
			if (!IsClientStarted)
			{
				return;
			}
			ShowMount(MountRules.ActiveMount(Character));
			if (body != null)
			{
				Animate(Time.deltaTime);
			}
		}

		/// <summary>Draws <paramref name="mount"/> under the rider (or nothing), the rider astride it.</summary>
		internal void ShowMount(MountBuffTemplate mount)
		{
			// A body under a mesh root that a new race model cleared is gone: drawn again.
			if (mount == shown && (mount == null || body != null))
			{
				return;
			}
			HideMount();
			if (mount == null || Character == null || Character.MeshRoot == null)
			{
				return;
			}
			shown = mount;
			Mesh mesh = MeshFor(mount);
			if (mesh != null && mount.Material != null)
			{
				body = new GameObject("MountBody");
				body.transform.SetParent(Character.MeshRoot, false);
				body.transform.localPosition = new Vector3(0f, 0f, mount.SaddleBack);
				body.AddComponent<MeshFilter>().sharedMesh = mesh;
				bodyRenderer = body.AddComponent<MeshRenderer>();
				bodyRenderer.sharedMaterial = mount.Material;
				bodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
				BaseCharacter.ApplyVisualLayer(body);
				block ??= new MaterialPropertyBlock();
			}
			if (Character.TryGet(out ICharacterAnimationController controller))
			{
				riderAnimation = controller as CharacterAnimationController;
			}
			riderAnimation?.SetRiding(true, mount.SaddleHeight);
			labels = FindLabels();
			if (labels != null)
			{
				labelsRest = labels.localPosition;
			}
			hasLastPosition = false;
		}

		/// <summary>Takes the mount away and stands the rider back on its feet.</summary>
		private void HideMount()
		{
			if (body != null)
			{
				Destroy(body);
			}
			body = null;
			bodyRenderer = null;
			if (shown != null)
			{
				riderAnimation?.SetRiding(false, 0f);
				if (labels != null)
				{
					labels.localPosition = labelsRest;
				}
			}
			labels = null;
			shown = null;
		}

		/// <summary>The character's name plate anchor (raised with the rider so the plate stays over its head).</summary>
		private Transform FindLabels()
		{
			if (Character?.MeshRoot == null)
			{
				return null;
			}
			foreach (Transform child in Character.MeshRoot)
			{
				if (child.name.Contains("Labels"))
				{
					return child;
				}
			}
			return null;
		}

		/// <summary>The mount's body mesh, built once per mount and shared.</summary>
		private static Mesh MeshFor(MountBuffTemplate mount)
		{
			if (meshes.TryGetValue(mount, out Mesh mesh) && mesh != null)
			{
				return mesh;
			}
			mesh = WildlifeBodyMesh.Build(mount.Body, mount.name);
			// Shared by every rider of the mount and never edited: the CPU copy can go.
			mesh.UploadMeshData(true);
			meshes[mount] = mesh;
			return mesh;
		}

		/// <summary>The mount's legs (and wings) for this frame, from how fast its rider really moves.</summary>
		private void Animate(float deltaTime)
		{
			if (deltaTime <= 0f || bodyRenderer == null)
			{
				return;
			}
			Vector3 position = Character.Transform.position;
			float vertical = 0f;
			if (hasLastPosition)
			{
				Vector3 moved = position - lastPosition;
				vertical = moved.y / deltaTime;
				moved.y = 0f;
				float measured = moved.magnitude / deltaTime;
				// A teleport is not a gallop.
				speed = measured > 40f ? 0f : Mathf.Lerp(speed, measured, 1f - Mathf.Exp(-12f * deltaTime));
			}
			lastPosition = position;
			hasLastPosition = true;

			if (labels != null && riderAnimation != null)
			{
				labels.localPosition = labelsRest + Vector3.up * Mathf.Max(0f, riderAnimation.FigureLift);
			}

			WildlifeBodySpec spec = shown.Body;
			bool flying = shown.Flies && IsAirborne(position, vertical);
			airborne = Mathf.MoveTowards(airborne, flying ? 1f : 0f, deltaTime * 3f);
			WildlifeGait gait = WildlifeGait.Evaluate(spec, shown.GaitRunSpeed, flying ? 0f : speed, 1f);
			phase = Mathf.Repeat(phase + gait.StepsPerSecond * Mathf.PI * 2f * deltaTime, Mathf.PI * 2f);
			float wag = Mathf.Sin(Time.time * (speed > 0.2f ? 9f : 3f)) * (speed > 0.2f ? 0.1f : 0.2f);
			// Wings: folded on the ground, beating in the air (WildlifeFlight's beat, drawn at the rider's own height).
			WildlifeFlight flight = WildlifeFlight.Evaluate(spec, shown.FlySpeed, Mathf.Max(speed, 6f), wingPhase, 0f, 0f);
			wingPhase = Mathf.Repeat(wingPhase + flight.BeatsPerSecond * Mathf.PI * 2f * deltaTime * airborne, Mathf.PI * 2f);
			float wingAngle = Mathf.Lerp(-0.35f, flight.WingAngle, airborne);
			if (!bodyRenderer.isVisible)
			{
				return;
			}
			float legSwing = spec.WingSpan > 0f ? gait.LegSwing * Mathf.Lerp(0.6f, 0.1f, airborne) : gait.LegSwing;
			block.SetVector(GaitId, new Vector4(phase, legSwing, gait.Gallop, gait.Bob));
			block.SetVector(PoseId, new Vector4(0f, 0f, wag, spec.Width * 0.5f));
			block.SetVector(FlightId, new Vector4(wingAngle, 0f, 0f, 0f));
			bodyRenderer.SetPropertyBlock(block);
		}

		/// <summary>Whether a flying mount is off the ground: its owner's motor says so, anyone else's is seen.</summary>
		private bool IsAirborne(Vector3 feet, float verticalSpeed)
		{
			if (PlayerCharacter != null && PlayerCharacter.CharacterController != null && IsOwner)
			{
				return PlayerCharacter.CharacterController.IsMountFlying;
			}
			if (Mathf.Abs(verticalSpeed) > 1f)
			{
				return true;
			}
			PhysicsScene physics = gameObject.scene.GetPhysicsScene();
			if (groundMask == 0)
			{
				int solid = LayerMask.GetMask("Default", "Ground");
				groundMask = solid != 0 ? solid : Physics.DefaultRaycastLayers;
			}
			return !physics.Raycast(feet + Vector3.up * 0.3f, Vector3.down, 0.6f, groundMask, QueryTriggerInteraction.Ignore);
		}
#endif
	}
}
