using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Npgsql.Monitoring.Metrics;
using FishMMO.Database.Npgsql.Monitoring.Diagnostics;

namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// Combined factory interface for creating NpgsqlDbContext instances with full monitoring capabilities.
	/// Extends both <see cref="IDbContextFactory"/> for core functionality and
	/// <see cref="IDbContextFactoryMonitoring"/> for monitoring features.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This interface provides backwards compatibility for consumers that need both
	/// context creation and monitoring capabilities. New consumers should prefer
	/// depending on the narrower interfaces:
	/// </para>
	/// <list type="bullet">
	/// <item><description><see cref="IDbContextFactory"/> - For core context creation only</description></item>
	/// <item><description><see cref="IDbContextFactoryMonitoring"/> - For monitoring access only</description></item>
	/// </list>
	/// <para>
	/// Implementations must be thread-safe and create new contexts per call.
	/// DbContext instances are short-lived and should not be pooled.
	/// </para>
	/// </remarks>
	public interface INpgsqlDbContextFactory : IDbContextFactory, IDbContextFactoryMonitoring
	{
	}
}