namespace FishMMO.Server.Core.Account
{
	/// <summary>
	/// Unified authentication state for all server types.
	/// Replaces the former SrpState enum and implicit in-flight flags with a single
	/// atomic enum that tracks the full connection lifecycle.
	/// </summary>
	/// <remarks>
	/// <b>State machine:</b>
	/// <code>
	/// SRP flow:   None → Handshake → VerifyPending → WaitingForProof → ProofPending → SrpSuccess → Authenticated
	/// Token flow: None → Handshake → TokenPending → Authenticated
	/// </code>
	/// All transitions are performed atomically via <c>TryAdvanceAuthState</c> under the
	/// AccountManager lock. The enum values are intentionally ordered so that
	/// <c>state > AuthState.Handshake</c> means "auth is in progress."
	/// <para><b>DO NOT RENUMBER</b> — sweep logic and guard checks rely on ordinal comparisons.</para>
	/// </remarks>
	public enum AuthState : byte
	{
		/// <summary>
		/// No authentication state. Connection exists but handshake has not completed.
		/// </summary>
		None = 0,

		/// <summary>
		/// Handshake received. Encryption keys established.
		/// Next: VerifyPending (SRP) or TokenPending (token).
		/// </summary>
		Handshake = 1,

		/// <summary>
		/// SRP verify request enqueued for async worker processing.
		/// Replaces the former <c>verifyInFlightByClientId</c> dictionary.
		/// </summary>
		VerifyPending = 2,

		/// <summary>
		/// SRP verify completed, SRP data established. Waiting for client proof.
		/// Replaces the former <c>SrpState.SrpVerify</c> "ready for proof" semantics.
		/// </summary>
		WaitingForProof = 3,

		/// <summary>
		/// SRP proof request enqueued for async worker processing.
		/// Replaces the former <c>proofInFlightByClientId</c> dictionary.
		/// </summary>
		ProofPending = 4,

		/// <summary>
		/// SRP proof validated, login in progress.
		/// </summary>
		SrpSuccess = 5,

		/// <summary>
		/// Token auth request enqueued for async worker processing.
		/// Replaces the former <c>tokenInFlightByClientId</c> dictionary.
		/// </summary>
		TokenPending = 6,

		/// <summary>
		/// Fully authenticated. Terminal state.
		/// </summary>
		Authenticated = 7,
	}
}