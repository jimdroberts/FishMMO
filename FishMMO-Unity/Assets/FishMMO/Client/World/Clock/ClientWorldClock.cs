using FishNet.Managing;
using FishNet.Transporting;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;

namespace FishMMO.Client
{
	/// <summary>
	/// The client's mirror of the world clock: adopts the scene server's anchor and reads it at FishNet's tick.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Core, so the world's time exists on the client whatever world content is installed: the sky and the weather run on
	/// it, and so do the sea, a mill's sails and the modules' own clocks. Nothing here predicts or decides anything.
	/// </para>
	/// <para>
	/// <b>The anchor belongs to one server.</b> It names a tick of the server it came from, and FishNet restarts the tick
	/// at 0 on a disconnect and counts the next server's ticks after — so a scene hop or a reconnect read the old server's
	/// anchor against the new server's tick, and the sky (and everything on the world's clock) was off by the two servers'
	/// difference in uptime, often days, until the new anchor came with the character. It is forgotten the moment the
	/// connection stops (<see cref="Reset"/>); without one the clock is this machine's UTC, a fraction of a second out at
	/// most, and the next server's anchor arrives on spawn.
	/// </para>
	/// </remarks>
	public sealed class ClientWorldClock
	{
		private NetworkManager networkManager;

		public void Initialize(NetworkManager manager)
		{
			networkManager = manager;
			if (networkManager == null)
			{
				return;
			}
			networkManager.ClientManager.RegisterBroadcast<WorldClockBroadcast>(OnClock);
			networkManager.ClientManager.OnClientConnectionState += OnConnectionState;
			WorldTick.Source = () => networkManager != null ? networkManager.TimeManager.Tick : 0u;
			// The tick the world's motion (waves, wind, fog banks) reads the shared clock at.
			WorldMotion.TimeManager = networkManager.TimeManager;
		}

		public void Shutdown()
		{
			if (networkManager != null)
			{
				networkManager.ClientManager.UnregisterBroadcast<WorldClockBroadcast>(OnClock);
				networkManager.ClientManager.OnClientConnectionState -= OnConnectionState;
			}
			Reset();
			WorldTick.Source = null;
			WorldMotion.TimeManager = null;
			networkManager = null;
		}

		/// <summary>Forgets the anchor: the connection to its server ended (see the remarks).</summary>
		public void Reset()
		{
			WorldClock.Shared.Reset();
		}

		private void OnConnectionState(ClientConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Stopped || args.ConnectionState == LocalConnectionState.Stopping)
			{
				Reset();
			}
		}

		private void OnClock(WorldClockBroadcast msg, Channel channel)
		{
			WorldClock clock = WorldClock.Shared;
			clock.TickDelta = networkManager != null ? networkManager.TimeManager.TickDelta : clock.TickDelta;
			bool first = !clock.HasAnchor;
			clock.Adopt(msg.Anchor, msg.Previous, msg.HasPrevious, msg.SlewTicks);
			if (first)
			{
				// Once per connection: what every viewer's sky and weather now run on, and at what pace.
				Log.Info("ClientWorldClock", $"World clock from the server: {WorldTimeText.Write(msg.Anchor.WorldSeconds, WorldEpoch.UnixSeconds)}, {WorldTimeText.Pace(msg.Anchor.Rate)}{(msg.Anchor.Verified ? string.Empty : " (unverified)")}.");
			}
		}
	}
}
