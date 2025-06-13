using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlServer2PlantUml.Models;
using SqlServer2PlantUml.Services;
using System.CommandLine;
using System.Text.Json;

namespace SqlServer2PlantUml;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Create service collection and configure DI
        var services = new ServiceCollection();
        ConfigureServices(services);

        await using var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            // Create root command
            var rootCommand = CreateRootCommand(serviceProvider);
            
            // Execute command
            return await rootCommand.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unhandled error occurred");
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddTransient<DatabaseSchemaService>();
        services.AddTransient<PlantUmlGeneratorService>();
    }

    private static RootCommand CreateRootCommand(ServiceProvider serviceProvider)
    {
        var rootCommand = new RootCommand("SQL Server to PlantUML - Generate PlantUML diagrams from SQL Server databases");

        // Connection string option
        var connectionStringOption = new Option<string>(
            aliases: ["--connection-string", "-c"],
            description: "SQL Server connection string")
        {
            IsRequired = true
        };
        rootCommand.AddOption(connectionStringOption);        // Output file option
        var outputFileOption = new Option<FileInfo>(
            aliases: ["--output", "-o"],
            description: "Output file path for the PlantUML diagram")
        {
            IsRequired = true
        };
        outputFileOption.AddValidator(result =>
        {
            var value = result.GetValueForOption(outputFileOption);
            if (value != null)
            {
                var extension = Path.GetExtension(value.FullName).ToLowerInvariant();
                if (extension != ".puml" && extension != ".plantuml" && extension != ".pu")
                {
                    result.ErrorMessage = "Output file must have a PlantUML extension (.puml, .plantuml, or .pu)";
                }
            }
        });
        rootCommand.AddOption(outputFileOption);

        // Diagram type option
        var diagramTypeOption = new Option<string>(
            aliases: ["--type", "-t"],
            description: "Type of diagram to generate",
            getDefaultValue: () => "entity")
        {
            IsRequired = false
        };
        diagramTypeOption.AddValidator(result =>
        {
            var value = result.GetValueForOption(diagramTypeOption);
            if (value != null && !new[] { "entity", "class" }.Contains(value.ToLower()))
            {
                result.ErrorMessage = "Diagram type must be either 'entity' or 'class'";
            }
        });
        rootCommand.AddOption(diagramTypeOption);

        // Configuration file option
        var configFileOption = new Option<FileInfo?>(
            aliases: ["--config", "-cfg"],
            description: "Configuration file path (JSON format)")
        {
            IsRequired = false
        };
        rootCommand.AddOption(configFileOption);

        // Include schemas option
        var includeSchemasOption = new Option<string[]>(
            aliases: ["--include-schemas"],
            description: "Schemas to include (comma-separated)")
        {
            IsRequired = false,
            AllowMultipleArgumentsPerToken = true
        };
        rootCommand.AddOption(includeSchemasOption);

        // Exclude schemas option
        var excludeSchemasOption = new Option<string[]>(
            aliases: ["--exclude-schemas"],
            description: "Schemas to exclude (comma-separated)")
        {
            IsRequired = false,
            AllowMultipleArgumentsPerToken = true
        };
        rootCommand.AddOption(excludeSchemasOption);

        // Exclude tables option
        var excludeTablesOption = new Option<string[]>(
            aliases: ["--exclude-tables"],
            description: "Table names to exclude (comma-separated, supports wildcards like *temp*, __*)")
        {
            IsRequired = false,
            AllowMultipleArgumentsPerToken = true
        };
        rootCommand.AddOption(excludeTablesOption);

        // Max tables option
        var maxTablesOption = new Option<int>(
            aliases: ["--max-tables"],
            description: "Maximum number of tables to include (0 for unlimited)",
            getDefaultValue: () => 0)
        {
            IsRequired = false
        };
        rootCommand.AddOption(maxTablesOption);

        // Include data types option
        var includeDataTypesOption = new Option<bool>(
            aliases: ["--include-data-types"],
            description: "Include column data types in the diagram",
            getDefaultValue: () => true)
        {
            IsRequired = false
        };
        rootCommand.AddOption(includeDataTypesOption);

        // Include relationships option
        var includeRelationshipsOption = new Option<bool>(
            aliases: ["--include-relationships"],
            description: "Include foreign key relationships in the diagram",
            getDefaultValue: () => true)
        {
            IsRequired = false
        };
        rootCommand.AddOption(includeRelationshipsOption);

        // Include indexes option
        var includeIndexesOption = new Option<bool>(
            aliases: ["--include-indexes"],
            description: "Include indexes in the diagram",
            getDefaultValue: () => false)
        {
            IsRequired = false
        };
        rootCommand.AddOption(includeIndexesOption);

        // Theme option
        var themeOption = new Option<string?>(
            aliases: ["--theme"],
            description: "PlantUML theme to use")
        {
            IsRequired = false
        };
        rootCommand.AddOption(themeOption);

        // Verbose option
        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Enable verbose logging",
            getDefaultValue: () => false)
        {
            IsRequired = false
        };
        rootCommand.AddOption(verboseOption);

        // Set command handler
        rootCommand.SetHandler(async (context) =>
        {
            var connectionString = context.ParseResult.GetValueForOption(connectionStringOption)!;
            var outputFile = context.ParseResult.GetValueForOption(outputFileOption)!;
            var diagramType = context.ParseResult.GetValueForOption(diagramTypeOption)!;
            var configFile = context.ParseResult.GetValueForOption(configFileOption);
            var includeSchemas = context.ParseResult.GetValueForOption(includeSchemasOption) ?? [];
            var excludeSchemas = context.ParseResult.GetValueForOption(excludeSchemasOption) ?? [];
            var excludeTables = context.ParseResult.GetValueForOption(excludeTablesOption) ?? [];
            var maxTables = context.ParseResult.GetValueForOption(maxTablesOption);
            var includeDataTypes = context.ParseResult.GetValueForOption(includeDataTypesOption);
            var includeRelationships = context.ParseResult.GetValueForOption(includeRelationshipsOption);
            var includeIndexes = context.ParseResult.GetValueForOption(includeIndexesOption);
            var theme = context.ParseResult.GetValueForOption(themeOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);            // Configure logging level
            if (verbose)
            {
                var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                // Create a new logger with debug level for verbose output
                var debugLogger = loggerFactory.CreateLogger<Program>();
                debugLogger.LogDebug("Verbose logging enabled");
            }

            await ExecuteGenerateCommand(
                serviceProvider,
                connectionString,
                outputFile,
                diagramType,
                configFile,
                includeSchemas,
                excludeSchemas,
                excludeTables,
                maxTables,
                includeDataTypes,
                includeRelationships,
                includeIndexes,
                theme,
                context.GetCancellationToken());
        });

        return rootCommand;
    }

    private static async Task ExecuteGenerateCommand(
        ServiceProvider serviceProvider,
        string connectionString,
        FileInfo outputFile,
        string diagramType,
        FileInfo? configFile,
        string[] includeSchemas,
        string[] excludeSchemas,
        string[] excludeTables,
        int maxTables,
        bool includeDataTypes,
        bool includeRelationships,
        bool includeIndexes,
        string? theme,
        CancellationToken cancellationToken)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        var schemaService = serviceProvider.GetRequiredService<DatabaseSchemaService>();
        var plantUmlService = serviceProvider.GetRequiredService<PlantUmlGeneratorService>();

        try
        {
            // Load configuration
            var options = await LoadOptionsAsync(configFile, logger);

            // Apply command line overrides
            ApplyCommandLineOptions(options, includeSchemas, excludeSchemas, excludeTables, maxTables, 
                includeDataTypes, includeRelationships, includeIndexes, theme);

            logger.LogInformation("Connecting to database and extracting schema...");
            
            // Extract schema
            var schema = await schemaService.ExtractSchemaAsync(connectionString, options, cancellationToken);
            
            logger.LogInformation("Generating PlantUML diagram...");
            
            // Generate PlantUML
            var plantUmlContent = diagramType.ToLower() switch
            {
                "entity" => plantUmlService.GeneratePlantUml(schema, options),
                "class" => plantUmlService.GenerateClassDiagram(schema, options),
                _ => throw new ArgumentException($"Unknown diagram type: {diagramType}")
            };            // Ensure output directory exists
            var outputPath = outputFile.FullName;
            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
            {
                logger.LogInformation("Creating output directory: {OutputDirectory}", outputDirectory);
                Directory.CreateDirectory(outputDirectory);
            }

            // Write to file
            await File.WriteAllTextAsync(outputPath, plantUmlContent, cancellationToken);
            
            logger.LogInformation("PlantUML diagram generated successfully: {OutputPath}", outputPath);
            Console.WriteLine($"PlantUML diagram generated: {outputPath}");
            Console.WriteLine($"Tables processed: {schema.Tables.Count}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating PlantUML diagram");
            throw;
        }
    }

    private static async Task<PlantUmlOptions> LoadOptionsAsync(FileInfo? configFile, ILogger logger)
    {
        if (configFile == null || !configFile.Exists)
        {
            return new PlantUmlOptions();
        }

        try
        {
            logger.LogInformation("Loading configuration from {ConfigFile}", configFile.FullName);
            var json = await File.ReadAllTextAsync(configFile.FullName);
            var options = JsonSerializer.Deserialize<PlantUmlOptions>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });
            return options ?? new PlantUmlOptions();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error loading configuration file, using defaults");
            return new PlantUmlOptions();
        }
    }

    private static void ApplyCommandLineOptions(
        PlantUmlOptions options,
        string[] includeSchemas,
        string[] excludeSchemas,
        string[] excludeTables,
        int maxTables,
        bool includeDataTypes,
        bool includeRelationships,
        bool includeIndexes,
        string? theme)
    {
        if (includeSchemas.Length > 0)
        {
            options.IncludeSchemas = includeSchemas.ToList();
        }

        if (excludeSchemas.Length > 0)
        {
            options.ExcludeSchemas = excludeSchemas.ToList();
        }

        if (excludeTables.Length > 0)
        {
            options.ExcludeTables = excludeTables.ToList();
        }

        if (maxTables > 0)
        {
            options.MaxTables = maxTables;
        }

        options.IncludeDataTypes = includeDataTypes;
        options.IncludeRelationships = includeRelationships;
        options.IncludeIndexes = includeIndexes;

        if (!string.IsNullOrEmpty(theme))
        {
            options.Theme = theme;
        }
    }
}
