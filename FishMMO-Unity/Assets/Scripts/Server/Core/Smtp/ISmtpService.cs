using System.Threading.Tasks;

namespace FishMMO.Server.Core.Smtp
{
	/// <summary>
	/// Abstraction over SMTP email delivery. Implementations can use
	/// System.Net.Mail.SmtpClient, a third-party library, or an external API.
	/// </summary>
	public interface ISmtpService
	{
		/// <summary>
		/// Sends an email asynchronously.
		/// </summary>
		/// <param name="to">Recipient email address.</param>
		/// <param name="subject">Email subject line.</param>
		/// <param name="body">Email body (HTML or plain-text).</param>
		/// <returns>True if the email was accepted for delivery.</returns>
		Task<bool> SendEmailAsync(string to, string subject, string body);
	}
}
