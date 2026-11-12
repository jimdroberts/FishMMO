namespace FishNet.Transporting.WebTransport
{
	/// <summary>
	/// How the client's most recent connection attempt went, as far as the transport can see.
	/// </summary>
	/// <remarks>
	/// Kept after the connection stops, until the next attempt starts, so a panel handling the
	/// Stopped event can still read it.
	/// <para>
	/// A connection that never opens is distinguished from one that opened without the server
	/// ever sending anything. The second case is not a network fault. Security software that
	/// intercepts encrypted traffic on the player's own machine can complete the handshake
	/// locally and then never forward the game's messages. Its tell is an open time shorter
	/// than any round trip to a real server.
	/// </para>
	/// </remarks>
	public readonly struct ClientConnectDiagnosis
	{
		/// <summary>True once a connection attempt has been started in this process.</summary>
		public readonly bool Attempted;

		/// <summary>True when the session opened.</summary>
		public readonly bool Opened;

		/// <summary>Milliseconds from starting the attempt to the session opening; -1 if it did not open.</summary>
		public readonly double OpenMilliseconds;

		/// <summary>True when anything at all arrived from the server during the session.</summary>
		public readonly bool ServerDataReceived;

		/// <summary>The failure the transport reported, if any (browser error text, or a native error name).</summary>
		public readonly string Failure;

		/// <summary>True when the native transport reported a TLS or certificate failure.</summary>
		public readonly bool TlsFailure;

		public ClientConnectDiagnosis(bool attempted, bool opened, double openMilliseconds,
			bool serverDataReceived, string failure, bool tlsFailure)
		{
			Attempted = attempted;
			Opened = opened;
			OpenMilliseconds = openMilliseconds;
			ServerDataReceived = serverDataReceived;
			Failure = failure;
			TlsFailure = tlsFailure;
		}

		/// <inheritdoc/>
		public override string ToString()
		{
			return $"attempted={Attempted} opened={Opened} openMs={OpenMilliseconds:0.0} " +
				$"serverData={ServerDataReceived} tls={TlsFailure} failure={Failure ?? "none"}";
		}
	}
}
