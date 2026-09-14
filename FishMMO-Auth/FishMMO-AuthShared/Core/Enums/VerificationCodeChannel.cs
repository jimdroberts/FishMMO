namespace FishMMO.Auth.Core
{
	/// <summary>
	/// Which contact channel a verification code was sent to, and so which stored code an
	/// <c>AccountVerifyBroadcast</c> is redeemed against.
	/// </summary>
	/// <remarks>
	/// <see cref="Email"/> is zero so a broadcast that names no channel means what every
	/// verification meant before SMS existed.
	/// </remarks>
	public enum VerificationCodeChannel : byte
	{
		/// <summary>The code emailed to the account address.</summary>
		Email = 0,
		/// <summary>The code sent by SMS to the account phone number.</summary>
		Sms = 1,
	}
}
