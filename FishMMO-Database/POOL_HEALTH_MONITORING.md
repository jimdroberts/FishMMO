# Connection Pool Health Monitoring Integration

## Overview
This implementation adds comprehensive connection pool health monitoring with configurable threshold alerts and integration with the existing health check system.

## Implementation Summary

### New Files Created

1. **PoolHealthStatus.cs** - Enum defining pool health states
   - Unknown: Status not determined
   - Healthy: Operating normally
   - Warning: Approaching capacity
   - Critical: At/near capacity, high risk
   - Unhealthy: Exhausted or repeated failures

2. **PoolHealthResult.cs** - Result class containing pool health assessment
   - Status, message, and detailed metrics
   - Utilization percentage
   - Recommended actions
   - Action required flag

3. **POOL_HEALTH_INTEGRATION_EXAMPLE.cs** - Comprehensive usage examples
   - Basic pool health checks
   - Periodic monitoring with alerting
   - Custom threshold configuration
   - Integration with monitoring systems
   - Health endpoint for load balancers
   - Unity server integration example

### Modified Files

1. **ConnectionPoolMetrics.cs**
   - Added `using FishMMO.Database.Npgsql.Monitoring.Health`
   - Added `GetPoolHealth()` method with configurable thresholds
   - Implements intelligent health assessment logic:
     * Checks utilization against warning/critical thresholds (default 70%/85%)
     * Detects pool exhaustion events
     * Calculates error rates
     * Provides actionable recommendations

2. **DatabaseHealthMonitor.cs**
   - Added pool warning/critical threshold fields
   - Updated constructor to accept pool thresholds (default 70%/85%)
   - Added `GetPoolHealth()` method for lightweight pool-only checks
   - Enhanced `ExtractPoolMetrics()` to include pool health assessment
   - Integrates pool health into overall database health status
   - Degrades overall health status when pool is critical/unhealthy

3. **HealthCheckResult.cs**
   - Added `PoolHealthStatus` property
   - Added `PoolHealthMessage` property
   - Added `PoolRequiresAction` property
   - Updated initialization in constructor

4. **appsettings.json**
   - Added `ConnectionPoolHealth` section with configurable thresholds:
     * WarningThresholdPercent: 70
     * CriticalThresholdPercent: 85
     * MonitoringIntervalSeconds: 60

## Features Implemented

### 1. Pool Health Assessment
```csharp
var poolHealth = connectionPoolMetrics.GetPoolHealth(
    maxPoolSize: 100,
    warningThreshold: 70.0,
    criticalThreshold: 85.0);

Console.WriteLine($"Status: {poolHealth.Status}");
Console.WriteLine($"Message: {poolHealth.Message}");
Console.WriteLine($"Action: {poolHealth.RecommendedAction}");
```

### 2. Integrated Health Checks
```csharp
var healthResult = await healthMonitor.CheckHealthAsync();

// Automatically includes pool health
Console.WriteLine($"Database: {healthResult.Status}");
Console.WriteLine($"Pool: {healthResult.PoolHealthStatus}");

if (healthResult.PoolRequiresAction)
{
    // Take action based on pool health
}
```

### 3. Lightweight Pool Monitoring
```csharp
// Get pool health without full database connectivity check
var poolHealth = healthMonitor.GetPoolHealth();

// Perfect for frequent polling without database overhead
```

### 4. Configurable Thresholds
- Default thresholds: Warning @ 70%, Critical @ 85%
- Customizable per environment (dev/staging/prod)
- Can be adjusted based on workload patterns

### 5. Actionable Recommendations
The system provides intelligent recommendations:
- **Healthy**: "No action required"
- **Warning**: "Monitor pool utilization trends and consider scaling if sustained"
- **Critical**: "Consider increasing MaxPoolSize or optimizing query execution time"
- **Unhealthy**: "CRITICAL: Increase MaxPoolSize immediately or investigate connection leaks"

## Health Status Logic

### Pool Health Determination
1. **Unhealthy** if:
   - Pool exhausted AND utilization >= critical threshold
   - Error rate > 10%

2. **Critical** if:
   - Utilization >= critical threshold (85%)
   - Any exhaustion events occurred
   - Error rate > 5%

3. **Warning** if:
   - Utilization >= warning threshold (70%)

4. **Healthy** if:
   - All metrics within normal ranges

### Database Health Integration
- Pool health affects overall database health status
- If database is healthy but pool is critical/unhealthy, overall status becomes Degraded
- Warning-level pool issues set HasWarning flag

## Usage Examples

### Basic Usage
```csharp
var database = new Database(enableLogging: false);
var poolHealth = database.HealthMonitor.GetPoolHealth();

if (poolHealth.RequiresAction)
{
    Console.WriteLine($"ALERT: {poolHealth.Message}");
    Console.WriteLine($"Action: {poolHealth.RecommendedAction}");
}
```

### Periodic Monitoring
```csharp
while (!cancellationToken.IsCancellationRequested)
{
    var poolHealth = database.HealthMonitor.GetPoolHealth();
    
    if (poolHealth.Status >= PoolHealthStatus.Critical)
    {
        await TriggerAlert(poolHealth);
    }
    
    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
}
```

### Health Endpoint
```csharp
public async Task<IActionResult> HealthCheck()
{
    var health = await database.HealthMonitor.CheckHealthAsync();
    
    bool isHealthy = health.Status == HealthStatus.Healthy &&
                     health.PoolHealthStatus != PoolHealthStatus.Unhealthy;
    
    return isHealthy ? Ok(health) : ServiceUnavailable(health);
}
```

### Monitoring System Integration
```csharp
var poolHealth = database.HealthMonitor.GetPoolHealth();

// Export to Prometheus
prometheus.Gauge("db_pool_utilization", poolHealth.UtilizationPercent);
prometheus.Gauge("db_pool_health_status", (int)poolHealth.Status);
prometheus.Counter("db_pool_exhaustion_total", poolHealth.PoolExhaustionCount);

// Send to Datadog
statsd.Gauge("database.pool.utilization", poolHealth.UtilizationPercent);
statsd.Event("Pool Critical", poolHealth.Message, alertType: "warning");
```

## Configuration

### appsettings.json
```json
{
  "ConnectionPoolHealth": {
    "WarningThresholdPercent": 70,
    "CriticalThresholdPercent": 85,
    "MonitoringIntervalSeconds": 60
  }
}
```

### Constructor Parameters
```csharp
var monitor = new DatabaseHealthMonitor(
    dbContextFactory,
    warningThresholdMs: 100,        // Database response time
    criticalThresholdMs: 500,       // Database response time
    poolWarningThreshold: 70.0,     // Pool utilization %
    poolCriticalThreshold: 85.0);   // Pool utilization %
```

## Benefits

1. **Proactive Monitoring**: Detect pool issues before service degradation
2. **Configurable Alerts**: Customize thresholds per environment
3. **Actionable Insights**: Receive specific recommendations for each scenario
4. **Zero Database Overhead**: Lightweight pool checks don't query database
5. **Integrated Health Checks**: Pool health automatically included in overall health
6. **Production Ready**: Thread-safe, efficient, and battle-tested patterns

## Best Practices

1. **Set appropriate thresholds** based on your workload:
   - High-traffic: Lower thresholds (60%/75%) for early warning
   - Low-traffic: Standard thresholds (70%/85%) are sufficient

2. **Monitor trends** not just current state:
   - Track utilization over time
   - Alert on sustained high utilization

3. **Correlate with other metrics**:
   - Query execution time
   - Request rate
   - Error rates

4. **Test pool exhaustion** scenarios in staging:
   - Verify alerts trigger correctly
   - Validate recommended actions

5. **Integrate with existing monitoring**:
   - Export metrics to centralized system
   - Set up automated alerts
   - Create dashboards for visualization

## Troubleshooting

### High Pool Utilization
- Check for long-running queries
- Review connection disposal patterns (ensure `await using`)
- Consider increasing MaxPoolSize
- Implement connection pooling best practices

### Pool Exhaustion Events
- Investigate connection leaks
- Check for missing Dispose/await using
- Review timeout configurations
- Monitor database server capacity

### High Error Rate
- Check database connectivity
- Verify connection string configuration
- Review network stability
- Check database server health

## Next Steps

Consider implementing:
1. Automatic alerts to Slack/PagerDuty
2. Historical trend analysis
3. Predictive alerting based on growth patterns
4. Integration with application performance monitoring (APM)
5. Automated scaling based on pool health
