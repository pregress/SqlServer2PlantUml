using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlServer2PlantUml.Models;
using System.Text.RegularExpressions;
using Dapper;

namespace SqlServer2PlantUml.Services;

public class DatabaseSchemaService
{
    private readonly ILogger<DatabaseSchemaService> _logger;

    public DatabaseSchemaService(ILogger<DatabaseSchemaService> logger)
    {
        _logger = logger;
    }

    public async Task<DatabaseSchema> ExtractSchemaAsync(string connectionString, PlantUmlOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Extracting database schema...");
            var schema = new DatabaseSchema();

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            // Get database name
            var databaseName = connection.Database;
            _logger.LogInformation("Connected to database: {DatabaseName}", databaseName);

            // Extract tables
            var tables = await ExtractTablesAsync(connection, options);

            // Limit tables if specified
            if (options.MaxTables > 0)
            {
                tables = tables.Take(options.MaxTables).ToList();
            }

            schema.Tables = tables;

            // Extract columns for each table
            foreach (var table in schema.Tables)
            {
                await ExtractColumnsAsync(connection, table);
                cancellationToken.ThrowIfCancellationRequested();
            }

            // Extract relationships if requested
            if (options.IncludeRelationships)
            {
                await ExtractRelationshipsAsync(connection, schema);
            }

            // Extract indexes if requested
            if (options.IncludeIndexes)
            {
                foreach (var table in schema.Tables)
                {
                    await ExtractIndexesAsync(connection, table);
                }
            }

            _logger.LogInformation("Successfully extracted schema for {TableCount} tables", schema.Tables.Count);
            return schema;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting database schema");
            throw;
        }
    }

    private async Task<List<DatabaseTable>> ExtractTablesAsync(SqlConnection connection, PlantUmlOptions options)
    {
        var sql = @"
            SELECT 
                t.TABLE_SCHEMA as SchemaName,
                t.TABLE_NAME as TableName,
                t.TABLE_TYPE as TableType
            FROM INFORMATION_SCHEMA.TABLES t
            WHERE t.TABLE_TYPE = 'BASE TABLE'";

        var whereClauses = new List<string>();
        var parameters = new DynamicParameters();

        // Add schema filtering
        if (options.IncludeSchemas.Any())
        {
            whereClauses.Add("t.TABLE_SCHEMA IN @IncludeSchemas");
            parameters.Add("IncludeSchemas", options.IncludeSchemas);
        }

        if (options.ExcludeSchemas.Any())
        {
            whereClauses.Add("t.TABLE_SCHEMA NOT IN @ExcludeSchemas");
            parameters.Add("ExcludeSchemas", options.ExcludeSchemas);
        }

        if (whereClauses.Any())
        {
            sql += " AND " + string.Join(" AND ", whereClauses);
        }

        sql += " ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME"; var tableData = await connection.QueryAsync<TableInfo>(sql, parameters);

        var tables = tableData.Select(t => new DatabaseTable
        {
            Schema = t.SchemaName,
            Name = t.TableName,
            Type = t.TableType,
            Columns = [],
            Indexes = []
        }).ToList();

        // Apply table name filtering
        tables = tables.Where(table => ShouldIncludeTable(table.Name, options)).ToList();

        return tables;
    }

    private async Task ExtractColumnsAsync(SqlConnection connection, DatabaseTable table)
    {
        const string sql = @"
            SELECT 
                c.COLUMN_NAME as ColumnName,
                c.DATA_TYPE as DataType,
                c.IS_NULLABLE as IsNullable,
                c.COLUMN_DEFAULT as DefaultValue,
                c.CHARACTER_MAXIMUM_LENGTH as MaxLength,
                c.NUMERIC_PRECISION as NumericPrecision,
                c.NUMERIC_SCALE as NumericScale,
                CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END as IsPrimaryKey,
                CASE WHEN fk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END as IsForeignKey,
                fk.REFERENCED_TABLE_SCHEMA as ReferencedSchema,
                fk.REFERENCED_TABLE_NAME as ReferencedTable,
                fk.REFERENCED_COLUMN_NAME as ReferencedColumn,
                c.ORDINAL_POSITION as OrdinalPosition
            FROM INFORMATION_SCHEMA.COLUMNS c
            LEFT JOIN (
                SELECT ku.TABLE_SCHEMA, ku.TABLE_NAME, ku.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                    ON tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                    AND tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                    AND tc.TABLE_SCHEMA = ku.TABLE_SCHEMA
                    AND tc.TABLE_NAME = ku.TABLE_NAME
            ) pk ON c.TABLE_SCHEMA = pk.TABLE_SCHEMA 
                AND c.TABLE_NAME = pk.TABLE_NAME 
                AND c.COLUMN_NAME = pk.COLUMN_NAME
            LEFT JOIN (
                SELECT 
                    ku.TABLE_SCHEMA,
                    ku.TABLE_NAME,
                    ku.COLUMN_NAME,
                    ccu.TABLE_SCHEMA as REFERENCED_TABLE_SCHEMA,
                    ccu.TABLE_NAME as REFERENCED_TABLE_NAME,
                    ccu.COLUMN_NAME as REFERENCED_COLUMN_NAME
                FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
                INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                    ON rc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                    AND rc.CONSTRAINT_SCHEMA = ku.CONSTRAINT_SCHEMA
                INNER JOIN INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE ccu
                    ON rc.UNIQUE_CONSTRAINT_NAME = ccu.CONSTRAINT_NAME
                    AND rc.UNIQUE_CONSTRAINT_SCHEMA = ccu.CONSTRAINT_SCHEMA
            ) fk ON c.TABLE_SCHEMA = fk.TABLE_SCHEMA 
                AND c.TABLE_NAME = fk.TABLE_NAME 
                AND c.COLUMN_NAME = fk.COLUMN_NAME
            WHERE c.TABLE_SCHEMA = @Schema AND c.TABLE_NAME = @TableName
            ORDER BY c.ORDINAL_POSITION";

        var parameters = new { table.Schema, TableName = table.Name };
        var columnData = await connection.QueryAsync<ColumnInfo>(sql, parameters);

        table.Columns = columnData.Select(c => new DatabaseColumn
        {
            Name = c.ColumnName,
            DataType = c.DataType,
            IsNullable = c.IsNullable == "YES",
            DefaultValue = c.DefaultValue,
            MaxLength = c.MaxLength,
            NumericPrecision = c.NumericPrecision,
            NumericScale = c.NumericScale,
            IsPrimaryKey = c.IsPrimaryKey == 1,
            IsForeignKey = c.IsForeignKey == 1,
            ReferencedSchema = c.ReferencedSchema,
            ReferencedTable = c.ReferencedTable,
            ReferencedColumn = c.ReferencedColumn,
            OrdinalPosition = c.OrdinalPosition
        }).ToList();
    }

    private async Task ExtractRelationshipsAsync(SqlConnection connection, DatabaseSchema schema)
    {
        const string sql = @"
            SELECT 
                rc.CONSTRAINT_NAME as ConstraintName,
                ku.TABLE_SCHEMA as FromSchema,
                ku.TABLE_NAME as FromTable,
                ku.COLUMN_NAME as FromColumn,
                ccu.TABLE_SCHEMA as ToSchema,
                ccu.TABLE_NAME as ToTable,
                ccu.COLUMN_NAME as ToColumn
            FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
            INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                ON rc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                AND rc.CONSTRAINT_SCHEMA = ku.CONSTRAINT_SCHEMA
            INNER JOIN INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE ccu
                ON rc.UNIQUE_CONSTRAINT_NAME = ccu.CONSTRAINT_NAME
                AND rc.UNIQUE_CONSTRAINT_SCHEMA = ccu.CONSTRAINT_SCHEMA
            ORDER BY ku.TABLE_SCHEMA, ku.TABLE_NAME, ku.COLUMN_NAME";

        var relationshipData = await connection.QueryAsync<RelationshipInfo>(sql);

        schema.Relationships = relationshipData.Select(r => new DatabaseRelationship
        {
            ConstraintName = r.ConstraintName,
            FromSchema = r.FromSchema,
            FromTable = r.FromTable,
            FromColumn = r.FromColumn,
            ToSchema = r.ToSchema,
            ToTable = r.ToTable,
            ToColumn = r.ToColumn
        }).ToList();
    }

    private async Task ExtractIndexesAsync(SqlConnection connection, DatabaseTable table)
    {
        const string sql = @"
            SELECT 
                i.name as IndexName,
                i.type_desc as IndexType,
                i.is_unique as IsUnique,
                i.is_primary_key as IsPrimaryKey,
                STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) as Columns
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            INNER JOIN sys.tables t ON i.object_id = t.object_id
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @Schema AND t.name = @TableName
            GROUP BY i.name, i.type_desc, i.is_unique, i.is_primary_key
            ORDER BY i.name";

        var parameters = new { table.Schema, TableName = table.Name };
        var indexData = await connection.QueryAsync<IndexInfo>(sql, parameters);

        table.Indexes = indexData.Select(i => new DatabaseIndex
        {
            Name = i.IndexName,
            Type = i.IndexType,
            IsUnique = i.IsUnique,
            IsPrimaryKey = i.IsPrimaryKey,
            Columns = i.Columns?.Split(", ").ToList() ?? []
        }).ToList();
    }

    // Helper classes for Dapper mapping
    private class TableInfo
    {
        public string SchemaName { get; init; } = string.Empty;
        public string TableName { get; init; } = string.Empty;
        public string TableType { get; init; } = string.Empty;
    }

    private class ColumnInfo
    {
        public string ColumnName { get; init; } = string.Empty;
        public string DataType { get; init; } = string.Empty;
        public string IsNullable { get; init; } = string.Empty;
        public string? DefaultValue { get; init; }
        public int? MaxLength { get; init; }
        public int? NumericPrecision { get; init; }
        public int? NumericScale { get; init; }
        public int IsPrimaryKey { get; init; }
        public int IsForeignKey { get; init; }
        public string? ReferencedSchema { get; init; }
        public string? ReferencedTable { get; init; }
        public string? ReferencedColumn { get; init; }
        public int OrdinalPosition { get; init; }
    }

    private class RelationshipInfo
    {
        public string ConstraintName { get; init; } = string.Empty;
        public string FromSchema { get; init; } = string.Empty;
        public string FromTable { get; init; } = string.Empty;
        public string FromColumn { get; init; } = string.Empty;
        public string ToSchema { get; init; } = string.Empty;
        public string ToTable { get; init; } = string.Empty;
        public string ToColumn { get; init; } = string.Empty;
    }

    private class IndexInfo
    {
        public string IndexName { get; init; } = string.Empty;
        public string IndexType { get; init; } = string.Empty;
        public bool IsUnique { get; init; }
        public bool IsPrimaryKey { get; init; }
        public string? Columns { get; init; }
    }

    private bool ShouldIncludeTable(string tableName, PlantUmlOptions options)
    {
        // Check exact exclude table names (supports simple wildcards)
        if (options.ExcludeTables.Any())
        {
            foreach (var excludeTable in options.ExcludeTables)
            {
                if (IsTableNameMatch(tableName, excludeTable))
                {
                    return false;
                }
            }
        }        // Check exclude table patterns (regex)
        if (options.ExcludeTablePatterns.Any())
        {
            foreach (var pattern in options.ExcludeTablePatterns)
            {
                try
                {
                    if (Regex.IsMatch(tableName, pattern, RegexOptions.IgnoreCase))
                    {
                        return false;
                    }
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning("Invalid regex pattern '{Pattern}': {Message}", pattern, ex.Message);
                }
            }
        }

        // Check include table patterns (if specified)
        if (options.IncludeTablePatterns.Any())
        {
            foreach (var pattern in options.IncludeTablePatterns)
            {
                try
                {
                    if (Regex.IsMatch(tableName, pattern, RegexOptions.IgnoreCase))
                    {
                        return true;
                    }
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning("Invalid regex pattern '{Pattern}': {Message}", pattern, ex.Message);
                }
            }
            // If include patterns are specified but none match, exclude the table
            return false;
        }

        return true;
    }
    private bool IsTableNameMatch(string tableName, string pattern)
    {
        // Convert simple wildcards to regex
        // Support * as wildcard (any characters) and ? as single character
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$"; 
        try
        {
            return Regex.IsMatch(tableName, regexPattern, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Invalid wildcard pattern '{Pattern}': {Message}", pattern, ex.Message);
            // Fall back to exact match
            return string.Equals(tableName, pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}
