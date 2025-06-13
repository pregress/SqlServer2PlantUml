using Microsoft.Extensions.Logging;
using SqlServer2PlantUml.Models;
using System.Text;

namespace SqlServer2PlantUml.Services;

/// <summary>
/// Service for generating PlantUML diagrams from database schema
/// </summary>
public class PlantUmlGeneratorService
{
    private readonly ILogger<PlantUmlGeneratorService> _logger;

    public PlantUmlGeneratorService(ILogger<PlantUmlGeneratorService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generates PlantUML entity relationship diagram from database schema
    /// </summary>
    /// <param name="schema">Database schema</param>
    /// <param name="options">PlantUML generation options</param>
    /// <returns>PlantUML diagram as string</returns>
    public string GeneratePlantUml(DatabaseSchema schema, PlantUmlOptions options)
    {
        try
        {
            _logger.LogInformation("Generating PlantUML diagram for {TableCount} tables", schema.Tables.Count);

            var sb = new StringBuilder();

            // Header
            sb.AppendLine("@startuml");
            sb.AppendLine($"' Generated on {schema.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"' Database: {schema.DatabaseName}");
            sb.AppendLine();

            // Theme and directives
            if (!string.IsNullOrEmpty(options.Theme))
            {
                sb.AppendLine($"!theme {options.Theme}");
                sb.AppendLine();
            }

            // Custom directives
            foreach (var directive in options.CustomDirectives)
            {
                sb.AppendLine(directive);
            }
            if (options.CustomDirectives.Count > 0)
            {
                sb.AppendLine();
            }

            // Default styling
            sb.AppendLine("skinparam linetype ortho");
            sb.AppendLine("skinparam roundcorner 5");
            sb.AppendLine("skinparam class {");
            sb.AppendLine("    BackgroundColor LightBlue");
            sb.AppendLine("    BorderColor DarkBlue");
            sb.AppendLine("    ArrowColor DarkBlue");
            sb.AppendLine("}");
            sb.AppendLine();

            // Generate entities
            GenerateEntities(sb, schema, options);

            // Generate relationships
            if (options.IncludeRelationships)
            {
                sb.AppendLine();
                sb.AppendLine("' Relationships");
                GenerateRelationships(sb, schema);
            }

            // Footer
            sb.AppendLine();
            sb.AppendLine("@enduml");

            var result = sb.ToString();
            _logger.LogInformation("Successfully generated PlantUML diagram");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PlantUML diagram");
            throw;
        }
    }

    private void GenerateEntities(StringBuilder sb, DatabaseSchema schema, PlantUmlOptions options)
    {
        foreach (var table in schema.Tables.OrderBy(t => t.Schema).ThenBy(t => t.Name))
        {
            var entityName = GetEntityName(table);
            
            sb.AppendLine($"entity \"{entityName}\" {{");

            // Table description
            if (options.IncludeDescriptions && !string.IsNullOrEmpty(table.Description))
            {
                sb.AppendLine($"  ' {table.Description}");
            }

            // Primary key columns first
            var primaryKeys = table.Columns.Where(c => c.IsPrimaryKey).OrderBy(c => c.OrdinalPosition).ToArray();
            foreach (var column in primaryKeys)
            {
                sb.AppendLine($"  * {GenerateColumnLine(column, options)}");
            }

            // Add separator if we have both PK and non-PK columns
            if (primaryKeys.Any() && table.Columns.Any(c => !c.IsPrimaryKey))
            {
                sb.AppendLine("  --");
            }

            // Non-primary key columns
            var otherColumns = table.Columns.Where(c => !c.IsPrimaryKey).OrderBy(c => c.OrdinalPosition);
            foreach (var column in otherColumns)
            {
                sb.AppendLine($"  {GenerateColumnLine(column, options)}");
            }

            // Indexes
            if (options.IncludeIndexes && table.Indexes.Any(i => !i.IsPrimaryKey))
            {
                sb.AppendLine("  --");
                sb.AppendLine("  ' Indexes:");
                foreach (var index in table.Indexes.Where(i => !i.IsPrimaryKey))
                {
                    var indexType = index.IsUnique ? "UNIQUE" : "INDEX";
                    sb.AppendLine($"  ' {indexType}: {string.Join(", ", index.Columns)}");
                }
            }

            sb.AppendLine("}");
            sb.AppendLine();
        }
    }

    private string GenerateColumnLine(DatabaseColumn column, PlantUmlOptions options)
    {
        var sb = new StringBuilder();
        
        // Column name
        sb.Append(column.Name);

        // Data type
        if (options.IncludeDataTypes)
        {
            sb.Append(" : ");
            sb.Append(FormatDataType(column));
        }

        // Nullable indicator
        if (!column.IsNullable && !column.IsPrimaryKey)
        {
            sb.Append(" <<NOT NULL>>");
        }

        // Identity indicator
        if (column.IsIdentity)
        {
            sb.Append(" <<IDENTITY>>");
        }

        // Foreign key indicator (will be determined by relationships)
        // This is handled in the relationship generation

        // Description
        if (options.IncludeDescriptions && !string.IsNullOrEmpty(column.Description))
        {
            sb.Append($" ' {column.Description}");
        }

        return sb.ToString();
    }    private string FormatDataType(DatabaseColumn column)
    {
        var dataType = column.DataType.ToUpper();

        return dataType switch
        {
            "VARCHAR" or "NVARCHAR" or "CHAR" or "NCHAR" when column.MaxLength.HasValue => 
                $"{dataType}({(column.MaxLength == -1 ? "MAX" : column.MaxLength.ToString())})",
            
            "DECIMAL" or "NUMERIC" when column.NumericPrecision.HasValue && column.NumericScale.HasValue => 
                $"{dataType}({column.NumericPrecision},{column.NumericScale})",
            
            "DECIMAL" or "NUMERIC" when column.Precision.HasValue && column.Scale.HasValue => 
                $"{dataType}({column.Precision},{column.Scale})",
            
            "FLOAT" when column.NumericPrecision.HasValue => 
                $"{dataType}({column.NumericPrecision})",
            
            "FLOAT" when column.Precision.HasValue => 
                $"{dataType}({column.Precision})",
            
            _ => dataType
        };
    }    private void GenerateRelationships(StringBuilder sb, DatabaseSchema schema)
    {
        var processedRelationships = new HashSet<string>();

        foreach (var relationship in schema.Relationships)
        {
            var fromTable = schema.Tables.FirstOrDefault(t => 
                t.Schema.Equals(relationship.FromSchema, StringComparison.OrdinalIgnoreCase) && 
                t.Name.Equals(relationship.FromTable, StringComparison.OrdinalIgnoreCase));

            var toTable = schema.Tables.FirstOrDefault(t => 
                t.Schema.Equals(relationship.ToSchema, StringComparison.OrdinalIgnoreCase) && 
                t.Name.Equals(relationship.ToTable, StringComparison.OrdinalIgnoreCase));

            if (fromTable == null || toTable == null)
            {
                continue; // One of the tables is not in the schema (might be filtered out)
            }

            var fromEntity = GetEntityName(fromTable);
            var toEntity = GetEntityName(toTable);
            
            // Create a unique relationship identifier to avoid duplicates
            var relationshipId = $"{fromEntity}->{toEntity}";
            if (processedRelationships.Contains(relationshipId))
            {
                continue;
            }

            processedRelationships.Add(relationshipId);

            // Check if the foreign key column is nullable for relationship notation
            var fromColumn = fromTable.Columns.FirstOrDefault(c => c.Name == relationship.FromColumn);
            var isNullable = fromColumn?.IsNullable == true;

            // Check if this is a one-to-one relationship (FK column is unique)
            var isOneToOne = fromTable.Indexes.Any(idx => 
                idx.IsUnique && 
                idx.Columns.Contains(relationship.FromColumn) &&
                idx.Columns.Count == 1);

            // Generate relationship using correct PlantUML syntax
            // Format: Entity1 ||--o{ Entity2 : relationship_label
            if (isOneToOne)
            {
                // One-to-one relationship
                if (isNullable)
                {
                    sb.AppendLine($"{toEntity} ||--o| {fromEntity} : {relationship.ToColumn}");
                }
                else
                {
                    sb.AppendLine($"{toEntity} ||--|| {fromEntity} : {relationship.ToColumn}");
                }
            }
            else
            {
                // One-to-many relationship (most common)
                if (isNullable)
                {
                    sb.AppendLine($"{toEntity} ||--o{{ {fromEntity} : {relationship.ToColumn}");
                }
                else
                {
                    sb.AppendLine($"{toEntity} ||--{{ {fromEntity} : {relationship.ToColumn}");
                }
            }
        }
    }

    private string GetEntityName(DatabaseTable table)
    {
        return table.Schema == "dbo" ? table.Name : $"{table.Schema}.{table.Name}";
    }

    /// <summary>
    /// Generates a simplified PlantUML class diagram (alternative format)
    /// </summary>
    /// <param name="schema">Database schema</param>
    /// <param name="options">PlantUML generation options</param>
    /// <returns>PlantUML class diagram as string</returns>
    public string GenerateClassDiagram(DatabaseSchema schema, PlantUmlOptions options)
    {
        try
        {
            _logger.LogInformation("Generating PlantUML class diagram for {TableCount} tables", schema.Tables.Count);

            var sb = new StringBuilder();

            // Header
            sb.AppendLine("@startuml");
            sb.AppendLine($"' Generated on {schema.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"' Database: {schema.DatabaseName}");
            sb.AppendLine();

            // Theme and directives
            if (!string.IsNullOrEmpty(options.Theme))
            {
                sb.AppendLine($"!theme {options.Theme}");
                sb.AppendLine();
            }

            // Generate classes
            foreach (var table in schema.Tables.OrderBy(t => t.Schema).ThenBy(t => t.Name))
            {
                var className = GetEntityName(table);
                
                sb.AppendLine($"class {className} {{");

                // Add columns as attributes
                foreach (var column in table.Columns.OrderBy(c => c.OrdinalPosition))
                {
                    var visibility = column.IsPrimaryKey ? "+" : "-";
                    var columnLine = $"  {visibility}{column.Name}";
                    
                    if (options.IncludeDataTypes)
                    {
                        columnLine += $" : {FormatDataType(column)}";
                    }

                    sb.AppendLine(columnLine);
                }

                sb.AppendLine("}");
                sb.AppendLine();
            }

            // Generate relationships
            if (options.IncludeRelationships)
            {
                GenerateClassRelationships(sb, schema);
            }

            // Footer
            sb.AppendLine("@enduml");

            var result = sb.ToString();
            _logger.LogInformation("Successfully generated PlantUML class diagram");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PlantUML class diagram");
            throw;
        }
    }    private void GenerateClassRelationships(StringBuilder sb, DatabaseSchema schema)
    {
        var processedRelationships = new HashSet<string>();

        foreach (var relationship in schema.Relationships)
        {
            var fromTable = schema.Tables.FirstOrDefault(t => 
                t.Schema.Equals(relationship.FromSchema, StringComparison.OrdinalIgnoreCase) && 
                t.Name.Equals(relationship.FromTable, StringComparison.OrdinalIgnoreCase));

            var toTable = schema.Tables.FirstOrDefault(t => 
                t.Schema.Equals(relationship.ToSchema, StringComparison.OrdinalIgnoreCase) && 
                t.Name.Equals(relationship.ToTable, StringComparison.OrdinalIgnoreCase));

            if (fromTable == null || toTable == null)
            {
                continue;
            }

            var fromClass = GetEntityName(fromTable);
            var toClass = GetEntityName(toTable);
            
            var relationshipId = $"{fromClass}->{toClass}";
            if (processedRelationships.Contains(relationshipId))
            {
                continue;
            }

            processedRelationships.Add(relationshipId);

            // Simple association for class diagrams
            sb.AppendLine($"{fromClass} --> {toClass}");
        }
    }
}
