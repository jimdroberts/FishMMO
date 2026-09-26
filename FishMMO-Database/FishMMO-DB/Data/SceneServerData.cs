using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Scene server registration data transfer object.
	/// </summary>
	public struct SceneServerData
	{
		public readonly long ID;
		public readonly string Name;
		public readonly DateTime LastPulse;
		public readonly string Address;
		public readonly int Port;
		public readonly int CharacterCount;
		public readonly bool Locked;

		/// <summary>
		/// Seconds since <see cref="LastPulse"/>, measured by the database clock at the moment the
		/// row was read. Never negative.
		/// </summary>
		/// <remarks>
		/// The only form of the pulse a reader in another process can judge liveness by.
		/// <c>last_pulse</c> is stamped by the database clock, and the reader's own host clock
		/// may disagree with it by any amount: comparing <see cref="LastPulse"/> with
		/// <c>DateTime.UtcNow</c> measured the gap between two clocks as much as the age of the
		/// pulse, so a world server whose host ran a minute fast treated every scene server as
		/// dead, and one running a minute slow kept routing players to a crashed one. The age is
		/// taken inside the statement that reads the row, where both ends are the same clock.
		/// </remarks>
		public readonly double PulseAgeSeconds;

		public SceneServerData(long id, string name, DateTime lastPulse, string address, int port, int characterCount, bool locked, double pulseAgeSeconds)
		{
			ID = id;
			Name = name;
			LastPulse = lastPulse;
			Address = address;
			Port = port;
			CharacterCount = characterCount;
			Locked = locked;
			PulseAgeSeconds = pulseAgeSeconds > 0.0 ? pulseAgeSeconds : 0.0;
		}
	}
}
