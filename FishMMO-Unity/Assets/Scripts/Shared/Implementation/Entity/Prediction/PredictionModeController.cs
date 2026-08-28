using FishNet.Component.Transforming;
using FishNet.Object;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// How this character is presented to everyone except its owner.
	/// </summary>
	public enum PredictionMode : byte
	{
		/// <summary>
		/// Observers interpolate a <see cref="NetworkTransform"/>; the owner alone predicts.
		/// </summary>
		/// <remarks>
		/// The open-world default. Roughly six times cheaper per observed peer in combat and twelve
		/// times cheaper idle, because neither the replicate relay nor the reconcile relay reaches
		/// observers at all. Peers render behind the server by the interpolation buffer plus their
		/// latency, which lag compensation corrects for at hit resolution.
		/// </remarks>
		Interpolated = 0,

		/// <summary>
		/// Observers receive the owner's input stream and simulate the character themselves.
		/// </summary>
		/// <remarks>
		/// Exact peer positions with no interpolation delay, at roughly 2610 B/s per observed peer
		/// against 409 B/s. Affordable only for small bounded sets — an arena, a duel, a boss room —
		/// and never as a scene-wide setting at the population this project targets.
		/// </remarks>
		Forwarded = 1,
	}

	/// <summary>
	/// Switches a character between interpolated and forwarded spectating at runtime.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Both halves have to move together, which is the reason this exists rather than callers
	/// poking <see cref="NetworkObject.SetStateForwarding"/> directly. Leaving the
	/// <see cref="NetworkTransform"/> enabled while forwarding is on pays for position twice — once
	/// through the relayed input stream and again through the transform. Disabling it while
	/// forwarding is off is worse: observers would receive nothing at all and the character would
	/// stand still for everyone but its owner while continuing to deal and take damage.
	/// </para>
	/// <para>
	/// <b>Server authority.</b> Only the server may change the mode, because only the server decides
	/// what it sends. The mode is mirrored to clients through
	/// <see cref="PredictionModeBroadcast"/> so they can enable or disable their own transform
	/// component to match; a client that never receives it simply keeps interpolating, which is the
	/// safe direction to fail.
	/// </para>
	/// <para>
	/// <b>Switch on a boundary, not mid-fight.</b> Changing modes changes how a character's position
	/// reaches observers, and the two paths do not hand off smoothly: an observer that was
	/// simulating a peer from its inputs and then starts interpolating a transform will see a small
	/// discontinuity as the interpolation buffer fills. Entering an arena or a loading boundary is
	/// the right moment; the middle of a duel is not.
	/// </para>
	/// </remarks>
	[RequireComponent(typeof(NetworkObject))]
	public class PredictionModeController : NetworkBehaviour
	{
		/// <summary>Mode applied when this character spawns.</summary>
		[Tooltip("Mode applied on spawn. Interpolated is the open-world default.")]
		[SerializeField]
		private PredictionMode defaultMode = PredictionMode.Interpolated;

		private NetworkTransform cachedNetworkTransform;
		private bool networkTransformResolved;

		/// <summary>
		/// The transform this character uses when interpolated, resolved on first use.
		/// </summary>
		/// <remarks>
		/// Resolved lazily rather than cached in <c>Awake</c>. FishNet's IL post-processor rewrites
		/// <c>Awake</c> on <see cref="NetworkBehaviour"/> subclasses to inject its own
		/// initialisation, so a hand-written one is not a reliable place to cache anything that
		/// other code — or a test — might need before it runs.
		/// </remarks>
		private NetworkTransform NetworkTransform
		{
			get
			{
				if (!networkTransformResolved)
				{
					cachedNetworkTransform = GetComponent<NetworkTransform>();
					networkTransformResolved = true;
				}
				return cachedNetworkTransform;
			}
		}

		/// <summary>The mode currently applied.</summary>
		public PredictionMode Mode { get; private set; } = PredictionMode.Interpolated;

		public override void OnStartServer()
		{
			base.OnStartServer();
			ApplyMode(defaultMode, true);
		}

		public override void OnStartClient()
		{
			base.OnStartClient();
			RegisterModeBroadcast(base.NetworkManager);
		}

		/// <summary>
		/// Replays the current mode to a client that starts observing this character later.
		/// </summary>
		/// <remarks>
		/// The mode broadcast in <see cref="ApplyMode"/> goes to whoever is observing at the moment
		/// the mode CHANGES; a client that walks into range of a forwarded arena character
		/// afterwards would otherwise never hear about it, keep its own NetworkTransform enabled
		/// against a server transform that sends nothing, and fight the forwarded simulation. This
		/// is the replay the old buffered-RPC pattern provided implicitly. Skipped for the default
		/// mode, which is what every client assumes until told otherwise.
		/// </remarks>
		public override void OnSpawnServer(FishNet.Connection.NetworkConnection connection)
		{
			base.OnSpawnServer(connection);

			if (Mode == PredictionMode.Interpolated || base.NetworkManager == null || base.NetworkObject == null)
			{
				return;
			}

			base.NetworkManager.ServerManager.Broadcast(connection, new PredictionModeBroadcast
			{
				CharacterObjectID = base.NetworkObject.ObjectId,
				Mode = (byte)Mode,
			}, true, FishNet.Transporting.Channel.Reliable);
		}

		/// <summary>
		/// Sets this character's presentation mode. Server only.
		/// </summary>
		/// <param name="mode">Mode to apply.</param>
		public void SetMode(PredictionMode mode)
		{
			/* Every NetworkBehaviour convenience property — IsServerStarted, IsSpawned, IsOwner —
			 * dereferences the NetworkObject cache, and that cache is null until the object spawns.
			 * So asking an unspawned component whether it is the server throws rather than answering
			 * false. NetworkObject itself is the one accessor that returns the cache instead of
			 * reading through it, which makes it the only safe thing to test first. */
			if (base.NetworkObject == null || !base.NetworkObject.IsServerStarted)
			{
				FishMMO.Logging.Log.Warning("PredictionModeController",
					$"SetMode called for '{gameObject.name}' off the server, or before it spawned. " +
					"Only the server decides what it sends, so this has no effect.");
				return;
			}

			/* Mode switches belong on boundaries — an arena gate, a loading screen — because the
			 * two position paths do not hand off smoothly: observers either wait for an
			 * interpolation buffer to refill or warm-start a simulation from an interpolated
			 * pose. That is a design constraint the compiler cannot enforce, so a switch during
			 * live combat is called out here instead of discovered as a visual glitch report.
			 * The switch still applies — the server may have a reason — but never silently. */
			if (mode != Mode &&
				TryGetComponent(out FishMMO.Shared.Core.ICharacter character) &&
				character.TryGet(out FishMMO.Shared.Core.ICharacterDamageController damageController) &&
				damageController.IsInCombat)
			{
				FishMMO.Logging.Log.Warning("PredictionModeController",
					$"SetMode({mode}) on '{gameObject.name}' while it is IN COMBAT. Observers will " +
					"see a position discontinuity as the transform and prediction paths hand off. " +
					"Switch presentation modes on a boundary (arena entry, teleport, load) instead.");
			}

			ApplyMode(mode, false);
		}

		/// <summary>Applies a mode locally and tells observers about it.</summary>
		/// <param name="mode">Mode to apply.</param>
		/// <param name="force">Apply even when the mode is unchanged, for the initial spawn.</param>
		private void ApplyMode(PredictionMode mode, bool force)
		{
			if (!force && mode == Mode)
			{
				return;
			}

			Mode = mode;
			bool forwarding = mode == PredictionMode.Forwarded;

			if (base.NetworkObject != null)
			{
				base.NetworkObject.SetStateForwarding(forwarding);
			}

			/* The transform is the position path for interpolated observers and redundant for
			 * forwarded ones, so it is enabled in exactly the inverse of forwarding. */
			if (NetworkTransform != null)
			{
				NetworkTransform.enabled = !forwarding;
			}

			if (base.IsServerStarted && base.NetworkManager != null && base.NetworkObject != null)
			{
				base.NetworkManager.ServerManager.Broadcast(base.NetworkObject, new PredictionModeBroadcast
				{
					CharacterObjectID = base.NetworkObject.ObjectId,
					Mode = (byte)mode,
				}, true, FishNet.Transporting.Channel.Reliable);
			}
		}

		/// <summary>Applies a mode received from the server, without re-broadcasting it.</summary>
		private void ApplyModeFromServer(PredictionMode mode)
		{
			Mode = mode;
			if (NetworkTransform != null)
			{
				NetworkTransform.enabled = mode != PredictionMode.Forwarded;
			}
		}

		/// <summary>True once this client has registered the shared mode handler.</summary>
		private static bool modeBroadcastRegistered;

		/// <summary>Registers the shared mode handler for this client.</summary>
		internal static void RegisterModeBroadcast(FishNet.Managing.NetworkManager networkManager)
		{
			if (modeBroadcastRegistered || networkManager == null)
			{
				return;
			}
			networkManager.ClientManager.RegisterBroadcast<PredictionModeBroadcast>(OnModeBroadcast);
			modeBroadcastRegistered = true;
		}

		/// <summary>Applies a mode broadcast to whichever character it names.</summary>
		private static void OnModeBroadcast(PredictionModeBroadcast msg, FishNet.Transporting.Channel channel)
		{
			FishNet.Managing.NetworkManager nm = FishNet.InstanceFinder.NetworkManager;
			if (nm == null || nm.ClientManager == null || nm.IsServerStarted)
			{
				return;
			}
			if (!nm.ClientManager.Objects.Spawned.TryGetValue(msg.CharacterObjectID, out NetworkObject nob) ||
				nob == null)
			{
				return;
			}

			PredictionModeController controller = nob.GetComponent<PredictionModeController>();
			controller?.ApplyModeFromServer((PredictionMode)msg.Mode);
		}
	}
}
