using FishNet.Object.Prediction;
using FishNet.Transporting;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for controllers that participate in the unified character prediction pipeline.
	/// Implemented by subsystems (e.g., movement, abilities) that contribute to replicate/reconcile
	/// through <see cref="CharacterPredictionController"/>.
	/// </summary>
	public interface IPredictableController
	{
		/// <summary>
		/// Execution order for this controller during the prediction tick.
		/// Lower values run first. Movement should run before abilities so that
		/// camera/position state is fresh for ability aiming.
		/// </summary>
		int Order { get; }

		/// <summary>
		/// Populates this controller's portion of the unified input data (owner only).
		/// Called once per tick before <see cref="OnReplicate"/>.
		/// </summary>
		void PopulateInput(ref CharacterReplicateData input);

		/// <summary>
		/// Executes this controller's simulation for the current tick.
		/// May modify <paramref name="input"/> for observer prediction.
		/// </summary>
		void OnReplicate(ref CharacterReplicateData input, ReplicateState state, Channel channel);

		/// <summary>
		/// Writes this controller's authoritative state into the reconcile data (server only).
		/// Called once per tick after <see cref="OnReplicate"/>.
		/// </summary>
		void OnCreateReconcile(ref CharacterReconcileData reconcileData);

		/// <summary>
		/// Restores this controller's state from the server's authoritative reconcile data.
		/// </summary>
		void OnReconcile(CharacterReconcileData rd, Channel channel);
	}
}
