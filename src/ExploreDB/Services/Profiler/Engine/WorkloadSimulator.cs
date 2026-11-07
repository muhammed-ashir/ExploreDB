using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ExploreDB.Services.Profiler.Analytics;
using ExploreDB.Services.Profiler.Models;

namespace ExploreDB.Services.Profiler.Engine;

/// <summary>
/// Generates realistic synthetic database workloads (SELECT, UPDATE, RPC, Slow Queries, Deadlocks, Errors)
/// for Demo / Simulation mode without requiring active SQL Server connections or elevated permissions.
/// </summary>
public sealed class WorkloadSimulator
{
    private static long _sequenceId = 50000;
    private readonly Random _rand = new();

    private readonly (string Sql, string Db, long MinUs, long MaxUs, long MinReads, long MaxReads)[] _queryTemplates = new (string, string, long, long, long, long)[]
    {
        ("SELECT o.OrderID, o.OrderDate, o.SubTotal, c.FirstName, c.LastName\nFROM sales.Orders o\nINNER JOIN sales.Customers c ON o.CustomerID = c.CustomerID\nWHERE o.CustomerID = {id} AND o.Status = 'Completed'\nORDER BY o.OrderDate DESC;", "ExploreDB_Lite", 1500L, 45000L, 120L, 850L),
        ("SELECT ProductID, ProductName, BasePrice, CategoryID\nFROM inventory.Products\nWHERE CategoryID = {id} AND BasePrice > 25.00;", "ExploreDB_Lite", 500L, 8000L, 15L, 60L),
        ("UPDATE sales.Orders\nSET Status = 'Shipped', ModifiedDate = GETDATE()\nWHERE OrderID = {id};", "ExploreDB_Lite", 3000L, 25000L, 40L, 180L),
        ("SELECT c.StateCode, COUNT(*) AS TotalCustomers, AVG(c.Balance) AS AvgBalance\nFROM sales.Customers c\nGROUP BY c.StateCode\nHAVING COUNT(*) > 5\nORDER BY TotalCustomers DESC;", "ExploreDB_Lite", 12000L, 120000L, 450L, 4200L),
        ("SELECT LogID, EventType, EventDescription, LoggedAt\nFROM audit.ActivityLogs\nWHERE LoggedAt >= DATEADD(HOUR, -24, GETDATE())\nORDER BY LoggedAt DESC;", "ExploreDB_Lite", 4000L, 32000L, 85L, 310L),
        // Slow bottleneck query
        ("SELECT p.ProductID, p.ProductName, SUM(oi.Quantity) AS TotalSold, SUM(oi.Quantity * oi.UnitPrice) AS TotalRevenue\nFROM inventory.Products p\nCROSS JOIN sales.Orders o\nINNER JOIN sales.OrderItems oi ON oi.ProductID = p.ProductID\nGROUP BY p.ProductID, p.ProductName\nORDER BY TotalRevenue DESC;", "ExploreDB_Lite", 320000L, 1250000L, 8500L, 34000L)
    };

    private readonly (string Proc, string Db, long MinUs, long MaxUs)[] _rpcTemplates = new (string, string, long, long)[]
    {
        ("EXEC sales.sp_CreateQuickOrder @CustomerID = {id}, @ShippingAddress = '100 Tech Park, Suite 4', @Notes = 'Rush dispatch';", "ExploreDB_Lite", 15000L, 85000L),
        ("EXEC inventory.sp_AdjustStockLevel @ProductID = {id}, @QuantityDelta = 10, @WarehouseID = 1;", "ExploreDB_Lite", 4000L, 18000L),
        ("EXEC dbo.sp_GetCustomerMetrics @CustomerID = {id}, @IncludeArchived = 0;", "ExploreDB_Lite", 6000L, 42000L)
    };

    private const string SampleDeadlockXml = @"<deadlock>
  <victim-list>
    <victimProcess id=""process1405a39cb88"" />
  </victim-list>
  <process-list>
    <process id=""process1405a39cb88"" taskpriority=""0"" logused=""1024"" waitresource=""KEY: 7:72057594047365120 (a857034b0dc2)"" waittime=""2410"" ownerId=""892401"" transactionname=""user_transaction"">
      <executionStack>
        <frame procname=""sales.Orders_UpdateStatus"" line=""14"" stmtstart=""412"" sqlhandle=""0x03000700a1"" />
      </executionStack>
      <inputbuf>UPDATE sales.Orders SET Status = 'Processing' WHERE OrderID = 1042;</inputbuf>
    </process>
    <process id=""process1405a3a1448"" taskpriority=""0"" logused=""2048"" waitresource=""KEY: 7:72057594047365120 (d839201f921a)"" waittime=""2395"" ownerId=""892402"" transactionname=""user_transaction"">
      <executionStack>
        <frame procname=""inventory.Stock_Reserve"" line=""28"" stmtstart=""890"" sqlhandle=""0x03000700b2"" />
      </executionStack>
      <inputbuf>UPDATE inventory.Stock SET QuantityOnHand = QuantityOnHand - 1 WHERE ProductID = 8;</inputbuf>
    </process>
  </process-list>
</deadlock>";

    public async Task StartSimulationAsync(ChannelWriter<TraceEvent> writer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                int delayMs = _rand.Next(60, 280);
                await Task.Delay(delayMs, ct);

                int roll = _rand.Next(100);
                TraceEvent evt;

                if (roll < 65)
                {
                    // SQL Batch Completed
                    evt = CreateSqlBatchEvent();
                }
                else if (roll < 85)
                {
                    // Stored Procedure (RPC)
                    evt = CreateRpcEvent();
                }
                else if (roll < 95)
                {
                    // Error Reported
                    evt = CreateErrorEvent();
                }
                else
                {
                    // Deadlock or Attention
                    evt = roll % 2 == 0 ? CreateDeadlockEvent() : CreateAttentionEvent();
                }

                writer.TryWrite(evt);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Suppress loop error
            }
        }
    }

    private TraceEvent CreateSqlBatchEvent()
    {
        var template = _queryTemplates[_rand.Next(_queryTemplates.Length)];
        int id = _rand.Next(1, 500);
        string sql = template.Sql.Replace("{id}", id.ToString());
        string norm = QueryNormalizer.Normalize(sql);
        string hash = QueryNormalizer.ComputeHash(norm);

        long durUs = _rand.Next((int)template.MinUs, (int)template.MaxUs);
        long cpuUs = (long)(durUs * _rand.NextDouble() * 0.85);
        long reads = _rand.Next((int)template.MinReads, (int)template.MaxReads);

        return new TraceEvent
        {
            Id = Interlocked.Increment(ref _sequenceId),
            Timestamp = DateTimeOffset.Now,
            Name = "sql_batch_completed",
            EventType = TraceEventType.SqlBatchCompleted,
            DurationMicroseconds = durUs,
            CpuMicroseconds = cpuUs,
            LogicalReads = reads,
            PhysicalReads = reads > 1000 ? _rand.Next(5, 50) : 0,
            Writes = sql.StartsWith("UPDATE") ? _rand.Next(1, 5) : 0,
            RowCount = sql.StartsWith("UPDATE") ? 1 : _rand.Next(1, 50),
            TextData = sql,
            NormalizedText = norm,
            QueryHash = hash,
            DatabaseName = template.Db,
            Spid = _rand.Next(51, 80),
            ClientAppName = "ExploreDB (Demo Simulation)",
            ClientHostName = Environment.MachineName,
            NtUserName = Environment.UserName,
            TransactionId = _rand.Next(1000000, 9999999)
        };
    }

    private TraceEvent CreateRpcEvent()
    {
        var template = _rpcTemplates[_rand.Next(_rpcTemplates.Length)];
        int id = _rand.Next(1, 500);
        string sql = template.Proc.Replace("{id}", id.ToString());
        string norm = QueryNormalizer.Normalize(sql);
        string hash = QueryNormalizer.ComputeHash(norm);

        long durUs = _rand.Next((int)template.MinUs, (int)template.MaxUs);
        long cpuUs = (long)(durUs * 0.7);

        return new TraceEvent
        {
            Id = Interlocked.Increment(ref _sequenceId),
            Timestamp = DateTimeOffset.Now,
            Name = "rpc_completed",
            EventType = TraceEventType.RpcCompleted,
            DurationMicroseconds = durUs,
            CpuMicroseconds = cpuUs,
            LogicalReads = _rand.Next(20, 450),
            Writes = _rand.Next(1, 10),
            RowCount = 1,
            TextData = sql,
            NormalizedText = norm,
            QueryHash = hash,
            DatabaseName = template.Db,
            Spid = _rand.Next(51, 80),
            ClientAppName = "ExploreDB (Demo Simulation)",
            ClientHostName = Environment.MachineName,
            NtUserName = Environment.UserName
        };
    }

    private TraceEvent CreateErrorEvent()
    {
        var errors = new[]
        {
            (Num: 2627, Sev: 14, Msg: "Violation of UNIQUE KEY constraint 'UQ_Customers_Email'. Cannot insert duplicate key in object 'sales.Customers'."),
            (Num: 547, Sev: 16, Msg: "The INSERT statement conflicted with the FOREIGN KEY constraint 'FK_Orders_Customers'."),
            (Num: 208, Sev: 16, Msg: "Invalid object name 'dbo.TemporaryStagingTable'."),
            (Num: 8152, Sev: 16, Msg: "String or binary data would be truncated in table 'sales.Customers', column 'Phone'.")
        };

        var err = errors[_rand.Next(errors.Length)];
        return new TraceEvent
        {
            Id = Interlocked.Increment(ref _sequenceId),
            Timestamp = DateTimeOffset.Now,
            Name = "error_reported",
            EventType = TraceEventType.ErrorReported,
            ErrorNumber = err.Num,
            Severity = err.Sev,
            State = 1,
            ErrorMessage = err.Msg,
            TextData = err.Msg,
            DatabaseName = "ExploreDB_Lite",
            Spid = _rand.Next(51, 80),
            ClientAppName = "ExploreDB (Demo Simulation)",
            ClientHostName = Environment.MachineName,
            NtUserName = Environment.UserName
        };
    }

    private TraceEvent CreateDeadlockEvent()
    {
        return new TraceEvent
        {
            Id = Interlocked.Increment(ref _sequenceId),
            Timestamp = DateTimeOffset.Now,
            Name = "xml_deadlock_report",
            EventType = TraceEventType.Deadlock,
            DurationMicroseconds = 2410000,
            CpuMicroseconds = 12000,
            LogicalReads = 85,
            DeadlockXml = SampleDeadlockXml,
            TextData = "[Deadlock Victim: Process 1405a39cb88 on sales.Orders]",
            DatabaseName = "ExploreDB_Lite",
            Spid = _rand.Next(51, 80),
            ClientAppName = "ExploreDB (Demo Simulation)",
            ClientHostName = Environment.MachineName,
            NtUserName = Environment.UserName
        };
    }

    private TraceEvent CreateAttentionEvent()
    {
        return new TraceEvent
        {
            Id = Interlocked.Increment(ref _sequenceId),
            Timestamp = DateTimeOffset.Now,
            Name = "attention",
            EventType = TraceEventType.Attention,
            DurationMicroseconds = 30000000, // 30 sec timeout
            CpuMicroseconds = 1250000,
            LogicalReads = 95000,
            TextData = "/* Query execution cancelled by client (Command Timeout exceeded 30s) */\nSELECT * FROM sales.Orders o CROSS JOIN sales.Customers c;",
            NormalizedText = "SELECT * FROM sales.Orders o CROSS JOIN sales.Customers c;",
            QueryHash = "ATTENTION_TIMEOUT_01",
            DatabaseName = "ExploreDB_Lite",
            Spid = _rand.Next(51, 80),
            ClientAppName = "ExploreDB (Demo Simulation)",
            ClientHostName = Environment.MachineName,
            NtUserName = Environment.UserName
        };
    }
}
