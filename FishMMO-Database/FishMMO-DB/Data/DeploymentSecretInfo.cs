using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// What is known about a deployment secret, without the secret.
	/// </summary>
	/// <remarks>
	/// <b>There is deliberately no value on this type, and there must never be one.</b> It
	/// exists so that an operator can see which secrets a deployment has, how long they have
	/// been there, and whether anything required is missing — questions that can all be
	/// answered without the material itself. A field holding the value would put every key the
	/// shard depends on one serialization mistake away from a browser, and the panel has no
	/// use for it that would justify that.
	/// </remarks>
	public sealed class DeploymentSecretInfo
	{
		/// <summary>The row key, such as <c>totp_master_kek</c>.</summary>
		public string Key { get; set; }

		/// <summary>
		/// How many characters the stored value has.
		/// </summary>
		/// <remarks>
		/// A length is not the secret. It is here because a truncated or empty key is a real
		/// failure mode with no other symptom until something tries to use it.
		/// </remarks>
		public int ValueLength { get; set; }

		/// <summary>When it was first written.</summary>
		public DateTime CreatedUtc { get; set; }

		/// <summary>When it last changed, which is when it was last rotated.</summary>
		public DateTime UpdatedUtc { get; set; }
	}
}
