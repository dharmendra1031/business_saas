-- Disposable GitHub CI SQL Server only. Source and target names are fixed.
-- Run after all browser writes are finished. No production connection or overwrite.
USE master;
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_ID(N'BusinessSaasCi') IS NULL OR DB_ID(N'BusinessSaasCiRestore') IS NOT NULL
    THROW 51000, 'Expected existing CI source and unused restore destination.', 1;

IF NOT EXISTS (SELECT 1 FROM BusinessSaasCi.dbo.Businesses)
    OR NOT EXISTS (SELECT 1 FROM BusinessSaasCi.dbo.RetailCashSales)
    OR NOT EXISTS (SELECT 1 FROM BusinessSaasCi.dbo.RetailPurchaseOrders)
    OR NOT EXISTS (SELECT 1 FROM BusinessSaasCi.dbo.RetailStockMovements)
    OR NOT EXISTS (SELECT 1 FROM BusinessSaasCi.dbo.RetailStockTransferReceipts)
    OR NOT EXISTS (SELECT 1 FROM BusinessSaasCi.dbo.RetailCreditPayments)
    OR NOT EXISTS (SELECT 1 FROM BusinessSaasCi.dbo.RetailCashRegisterSessions WHERE ClosedAtUtc IS NOT NULL)
    THROW 51001, 'Required posted retail acceptance fixtures are missing.', 1;

DECLARE @started datetime2 = SYSUTCDATETIME();
DECLARE @data sysname, @log sysname;
IF (SELECT COUNT(*) FROM sys.master_files WHERE database_id = DB_ID(N'BusinessSaasCi')) <> 2
    THROW 51002, 'Unexpected CI database file layout.', 1;

SELECT @data = name FROM sys.master_files
WHERE database_id = DB_ID(N'BusinessSaasCi') AND type = 0;
SELECT @log = name FROM sys.master_files
WHERE database_id = DB_ID(N'BusinessSaasCi') AND type = 1;
IF @data IS NULL OR @log IS NULL
    THROW 51003, 'CI database logical file names not found.', 1;

BACKUP DATABASE [BusinessSaasCi]
TO DISK = N'/var/opt/mssql/data/BusinessSaasCi_rehearsal.bak'
WITH COPY_ONLY, CHECKSUM, INIT, STATS = 10;

RESTORE VERIFYONLY
FROM DISK = N'/var/opt/mssql/data/BusinessSaasCi_rehearsal.bak'
WITH CHECKSUM;

DECLARE @restoreStarted datetime2 = SYSUTCDATETIME();
DECLARE @restore nvarchar(max) =
    N'RESTORE DATABASE [BusinessSaasCiRestore]
    FROM DISK = N''/var/opt/mssql/data/BusinessSaasCi_rehearsal.bak''
    WITH MOVE N' + QUOTENAME(@data, '''') + N' TO N''/var/opt/mssql/data/BusinessSaasCiRestore.mdf'',
         MOVE N' + QUOTENAME(@log, '''') + N' TO N''/var/opt/mssql/data/BusinessSaasCiRestore_log.ldf'',
         CHECKSUM, RECOVERY, STATS = 10;';
EXEC sys.sp_executesql @restore;
DECLARE @restoreMs bigint = DATEDIFF_BIG(millisecond, @restoreStarted, SYSUTCDATETIME());

DBCC CHECKDB([BusinessSaasCiRestore]) WITH NO_INFOMSGS, ALL_ERRORMSGS;

-- Verify table inventory and every row in both directions. The EF schema has
-- primary keys; row counts plus symmetric EXCEPT detect missing/changed rows.
SELECT s.name AS SchemaName, t.name AS TableName INTO #SourceTables
FROM BusinessSaasCi.sys.tables t
JOIN BusinessSaasCi.sys.schemas s ON s.schema_id = t.schema_id
WHERE t.is_ms_shipped = 0;

SELECT s.name AS SchemaName, t.name AS TableName INTO #RestoredTables
FROM BusinessSaasCiRestore.sys.tables t
JOIN BusinessSaasCiRestore.sys.schemas s ON s.schema_id = t.schema_id
WHERE t.is_ms_shipped = 0;

IF EXISTS (SELECT * FROM #SourceTables EXCEPT SELECT * FROM #RestoredTables)
    OR EXISTS (SELECT * FROM #RestoredTables EXCEPT SELECT * FROM #SourceTables)
    THROW 51004, 'Restored table inventory does not match source.', 1;

DECLARE @schema sysname, @table sysname, @source nvarchar(776), @target nvarchar(776);
DECLARE @check nvarchar(max), @sourceCount bigint, @targetCount bigint, @different bit;
DECLARE @tablesChecked int = 0, @rowsChecked bigint = 0;
DECLARE tables CURSOR LOCAL FAST_FORWARD FOR
    SELECT SchemaName, TableName FROM #SourceTables ORDER BY SchemaName, TableName;
OPEN tables;
FETCH NEXT FROM tables INTO @schema, @table;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @source = N'[BusinessSaasCi].' + QUOTENAME(@schema) + N'.' + QUOTENAME(@table);
    SET @target = N'[BusinessSaasCiRestore].' + QUOTENAME(@schema) + N'.' + QUOTENAME(@table);
    SET @check = N'SELECT @a = COUNT_BIG(*) FROM ' + @source +
        N'; SELECT @b = COUNT_BIG(*) FROM ' + @target +
        N'; SET @different = CASE WHEN EXISTS (SELECT * FROM ' + @source +
        N' EXCEPT SELECT * FROM ' + @target +
        N') OR EXISTS (SELECT * FROM ' + @target +
        N' EXCEPT SELECT * FROM ' + @source + N') THEN 1 ELSE 0 END;';
    EXEC sys.sp_executesql @check,
        N'@a bigint OUTPUT, @b bigint OUTPUT, @different bit OUTPUT',
        @a = @sourceCount OUTPUT, @b = @targetCount OUTPUT, @different = @different OUTPUT;
    IF @sourceCount <> @targetCount OR @different = 1
    BEGIN
        DECLARE @message nvarchar(2048) = N'Restore mismatch: ' + QUOTENAME(@schema) + N'.' + QUOTENAME(@table);
        THROW 51005, @message, 1;
    END;
    SET @tablesChecked += 1;
    SET @rowsChecked += @sourceCount;
    FETCH NEXT FROM tables INTO @schema, @table;
END;
CLOSE tables;
DEALLOCATE tables;

SELECT @tablesChecked AS TablesVerified, @rowsChecked AS RowsVerified,
       @restoreMs AS RestoreMilliseconds,
       DATEDIFF_BIG(millisecond, @started, SYSUTCDATETIME()) AS RehearsalMilliseconds;
PRINT 'PASS CI full backup, checksum verification, separate restore, CHECKDB and exact table data comparison.';
