using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FishMMO.Database.Npgsql.Monitoring.Metrics
{
	/// <summary>
	/// EF Core interceptor that tracks database connection open/close events.
	/// This is used to approximate Npgsql pool utilization by measuring how many DbConnections
	/// are currently open (checked out from the pool).
	/// </summary>
	public sealed class ConnectionMetricsInterceptor : DbConnectionInterceptor
	{
		private readonly ConnectionPoolMetrics metrics;

		/// <summary>
		/// Initializes a new instance of the <see cref="ConnectionMetricsInterceptor"/> class.
		/// </summary>
		/// <param name="metrics">Metrics sink to update.</param>
		public ConnectionMetricsInterceptor(ConnectionPoolMetrics metrics)
		{
			this.metrics = metrics;
		}

		public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
		{
			metrics?.RecordConnectionOpened();
			base.ConnectionOpened(connection, eventData);
		}

		public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
		{
			metrics?.RecordConnectionOpened();
			return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
		}

		public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData)
		{
			metrics?.RecordConnectionClosed();
			base.ConnectionClosed(connection, eventData);
		}

		public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
		{
			metrics?.RecordConnectionClosed();
			return base.ConnectionClosedAsync(connection, eventData);
		}
	}
}