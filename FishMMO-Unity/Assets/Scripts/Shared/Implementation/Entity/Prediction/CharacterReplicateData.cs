using FishNet.CodeGenerating;
using FishNet.Object.Prediction;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Unified per-tick input data for all predicted character subsystems.
	/// Contains movement input (KCC) and ability activation input.
	/// Prediction works best when replicate data contains only input, not state.
	/// </summary>
	[UseGlobalCustomSerializer]
	public struct CharacterReplicateData : IReplicateData
	{
		// ── Movement input ──

		/// <summary>
		/// Forward movement axis value (W/S or up/down).
		/// </summary>
		public float MoveAxisForward;

		/// <summary>
		/// Right movement axis value (A/D or left/right).
		/// </summary>
		public float MoveAxisRight;

		/// <summary>
		/// Bitmask of movement flags (jump, crouch, sprint, etc).
		/// Each flag is a bit position defined by <see cref="KCCMoveFlags"/>.
		/// </summary>
		public int MoveFlags;

		/// <summary>
		/// Camera position for this input frame.
		/// </summary>
		public Vector3 CameraPosition;

		/// <summary>
		/// Camera rotation for this input frame.
		/// </summary>
		public Quaternion CameraRotation;

		// ── Ability input ──

		/// <summary>
		/// Flags representing the ability activation state.
		/// Bit positions defined by <see cref="AbilityActivationFlags"/>.
		/// </summary>
		public int ActivationFlags;

		/// <summary>
		/// The ID of the queued ability or consumable template.
		/// </summary>
		public long QueuedAbilityID;

		/// <summary>
		/// Returns the network tick for this replicate input as a <see cref="PredictionTick"/>.
		/// This is the only sanctioned way to produce a PredictionTick — use it as the
		/// currentTick argument when applying prediction-domain buff and cooldown state.
		/// </summary>
		public PredictionTick GetPredictionTick() => new PredictionTick(tick);

		private uint tick;

		/// <inheritdoc/>
		public void Dispose() { }

		/// <inheritdoc/>
		public uint GetTick() => tick;

		/// <inheritdoc/>
		public void SetTick(uint value) => tick = value;
	}
}