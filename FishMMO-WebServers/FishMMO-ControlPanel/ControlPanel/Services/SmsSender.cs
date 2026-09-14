namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Delivers one text message. The SMS counterpart of <see cref="ISmtpSender"/>.
	/// </summary>
	/// <remarks>
	/// Separate from the queue for the same reason the SMTP sender is: <see cref="SmsQueueDrainService"/>
	/// decides what to send and what to record, and this decides only how a message leaves.
	/// </remarks>
	public interface ISmsSender
	{
		/// <summary>Whether this deployment can deliver anything.</summary>
		bool IsConfigured { get; }

		/// <summary>One line naming where messages go, or why they go nowhere. Safe to log.</summary>
		string ConfigurationSummary { get; }

		/// <summary>Sends one message, returning whether it was accepted.</summary>
		Task<bool> SendAsync(string toPhone, string body, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// The only SMS "provider" there is: it writes the message to the log.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>No SMS gateway exists in this project yet.</b> This sender is what lets the phone
	/// verification flow be exercised end to end on a development machine: the code shows up in the
	/// panel's log. It logs the body, which carries a verification code — acceptable here precisely
	/// because this is a development stand-in, and the reason it is not configured in Production
	/// unless an operator names it explicitly.
	/// </para>
	/// <para>
	/// Configured when <c>Sms:Provider</c> is <c>log</c>, or — outside Production only — when no
	/// provider is named at all. In Production with no provider the drain logs once and idles, as the
	/// email drain does with no SMTP host, and queued messages wait for a real sender.
	/// </para>
	/// </remarks>
	public sealed class LoggingSmsSender : ISmsSender
	{
		private readonly ILogger<LoggingSmsSender> log;

		public LoggingSmsSender(IConfiguration configuration, IWebHostEnvironment environment, ILogger<LoggingSmsSender> log)
		{
			this.log = log;
			string provider = (configuration["Sms:Provider"] ?? string.Empty).Trim();

			if (provider.Equals("log", StringComparison.OrdinalIgnoreCase))
			{
				IsConfigured = true;
				ConfigurationSummary = environment.IsProduction()
					? "provider 'log' (explicit): messages, including verification codes, are written to the panel log and NOT delivered"
					: "provider 'log': messages are written to the panel log and not delivered";
			}
			else if (provider.Length == 0)
			{
				IsConfigured = !environment.IsProduction();
				ConfigurationSummary = IsConfigured
					? "no provider configured; development default 'log' writes messages to the panel log"
					: "no SMS provider is configured (Sms:Provider)";
			}
			else
			{
				IsConfigured = false;
				ConfigurationSummary = $"SMS provider '{provider}' is not supported by this build; only 'log' exists";
			}
		}

		/// <inheritdoc />
		public bool IsConfigured { get; }

		/// <inheritdoc />
		public string ConfigurationSummary { get; }

		/// <inheritdoc />
		public Task<bool> SendAsync(string toPhone, string body, CancellationToken cancellationToken = default)
		{
			if (!IsConfigured || string.IsNullOrWhiteSpace(toPhone))
			{
				return Task.FromResult(false);
			}
			log.LogInformation("SMS (not delivered, logging sender) to {Phone}: {Body}", toPhone, body);
			return Task.FromResult(true);
		}
	}
}
