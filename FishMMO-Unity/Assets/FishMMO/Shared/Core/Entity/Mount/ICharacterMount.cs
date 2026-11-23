namespace FishMMO.Shared.Core
{
	/// <summary>
	/// How the mount a character rides changes the way it moves (issue #109). Read by the movement controller
	/// (KCCController) on every simulated and replayed tick; zero or default in a field means "as on foot".
	/// </summary>
	/// <remarks>
	/// <b>Derived, never sent.</b> Riding is a buff (the Mounts module's mount buff), and the buff set is reconciled state on the
	/// owner and the server alike, so the motor reads the same motion on both peers at the same tick without a field of its
	/// own on the wire. A flying mount's flight is derived too: it flies whenever it is not standing on stable ground.
	/// </remarks>
	public struct MountMotion
	{
		/// <summary>Multiplies the rider's run and sprint speed (1 = on foot).</summary>
		public float GroundSpeedMultiplier;
		/// <summary>Multiplies how HIGH a jump goes (the take-off speed by its square root); 1 or 0 = on foot.</summary>
		public float JumpHeightMultiplier;
		/// <summary>Metres per second a jump adds along the way the rider is steering (a frog's long leap); 0 for none.</summary>
		public float LeapSpeed;
		/// <summary>The steepest slope, degrees, the mount walks up as ground (a llama's); 0 keeps the rider's own.</summary>
		public float MaxSlopeDegrees;
		/// <summary>Flies whenever it is off stable ground: Jump takes off, held Jump climbs, Crouch descends.</summary>
		public bool Flies;
		/// <summary>Flying speed, metres per second.</summary>
		public float FlySpeed;
		/// <summary>Sprinting in flight multiplies the speed by this (it costs twice the stamina).</summary>
		public float FlySprintMultiplier;
		/// <summary>Stamina a second of flight costs; out of stamina the mount glides down.</summary>
		public float FlightStaminaPerSecond;
		/// <summary>How fast a gliding mount (out of stamina) sinks, metres per second.</summary>
		public float GlideSinkSpeed;
		/// <summary>The share of <see cref="FlySpeed"/> a gliding mount keeps going forward.</summary>
		public float GlideSpeedShare;

		/// <summary>The jump take-off speed multiplier for <see cref="JumpHeightMultiplier"/> (height goes with speed squared).</summary>
		public float JumpSpeedMultiplier => JumpHeightMultiplier > 0f ? (float)System.Math.Sqrt(JumpHeightMultiplier) : 1f;
	}

	/// <summary>
	/// What the core asks of a character's mount (the Mounts module's controller implements it): whether it rides one, how
	/// the mount moves, and getting off.
	/// </summary>
	public interface ICharacterMount : ICharacterBehaviour
	{
		/// <summary>True while the character rides a mount.</summary>
		bool IsMounted { get; }

		/// <summary>The ridden mount's motion; false (and default) on foot.</summary>
		bool TryGetMotion(out MountMotion motion);

		/// <summary>
		/// Gets off. Called from inside the movement step (swimming, taking hold of a ladder), where the owner and the server
		/// reach it on the same tick, and by the server for its own reasons (a hit, a roof overhead). Does nothing on foot.
		/// </summary>
		void Dismount();
	}
}
