using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TestApp
{
    class LoggerSearch
    {
        readonly ILogger<LoggerSearch> _logger;

        public LoggerSearch(ILogger<LoggerSearch> logger)
        {
            _logger = logger;
        }

        public async Task Scan(string[] args)
        {
            string rootPath = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();

            _logger.LogInformation(123, "Has duplicate");
            _logger.LogError(123, "Has duplicate");
            _logger.LogInformation(125, "No duplicate");
            _logger.LogInformation("Missing EventId");
            _logger.LogInformation(126, "Scanning for .sln files in: {RootPath}", rootPath);

            var solutionFiles = Directory.EnumerateFiles(rootPath, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateDirectories(rootPath)
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*.sln", SearchOption.TopDirectoryOnly)))
            .Concat(Directory.EnumerateDirectories(rootPath)
                .SelectMany(dir => Directory.EnumerateDirectories(dir))
                .SelectMany(subdir => Directory.EnumerateFiles(subdir, "*.sln", SearchOption.TopDirectoryOnly)));

            foreach (string solutionPath in solutionFiles)
            {
                _logger.LogInformation(127, "Scanning: {solutionPath}", solutionPath);
                MSBuildWorkspace workspace = MSBuildWorkspace.Create();
                Solution solution = await workspace.OpenSolutionAsync(solutionPath);

                Dictionary<int, List<Location>> eventIdMap = new Dictionary<int, List<Location>>();
                List<Location> missingEventIds = new List<Location>();

                foreach (Project project in solution.Projects)
                {
                    if (project.FilePath != null && project.FilePath.Contains(@"\tests\", StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (Microsoft.CodeAnalysis.Document document in project.Documents)
                    {
                        SyntaxNode? root = await document.GetSyntaxRootAsync();
                        SemanticModel? semanticModel = await document.GetSemanticModelAsync();

                        if (semanticModel == null)
                            continue;
                        if (root == null)
                            continue;

                        IEnumerable<InvocationExpressionSyntax> invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
                        foreach (InvocationExpressionSyntax invocation in invocations)
                        {
                            IMethodSymbol? symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;

                            if (symbol == null)
                                continue;

                            if ((symbol.ContainingType != null && symbol.ContainingType.Name.Contains("ILogger")) ||
                                    (symbol.ReceiverType != null && (symbol.ReceiverType.Name.Contains("ILogger") || symbol.ReceiverType.AllInterfaces.Any(i => i.Name == "ILogger"))))
                            {
                                SeparatedSyntaxList<ArgumentSyntax> argsList = invocation.ArgumentList.Arguments;
                                if (argsList.Count == 0)
                                    continue;

                                ArgumentSyntax firstArg = argsList[0];
                                Optional<object?> constValue = semanticModel.GetConstantValue(firstArg.Expression);

                                if (constValue.HasValue && constValue.Value is int eventId)
                                {
                                    if (!eventIdMap.ContainsKey(eventId))
                                        eventIdMap[eventId] = new List<Location>();

                                    eventIdMap[eventId].Add(invocation.GetLocation());
                                }
                                else
                                {
                                    missingEventIds.Add(invocation.GetLocation());
                                }
                            }
                        }
                    }
                }

                if (eventIdMap.Count > 0)
                {
                    _logger.LogInformation($"    Duplicate EventIds = {eventIdMap.Count}");
                    foreach (KeyValuePair<int, List<Location>> kvp in eventIdMap.Where(kvp => kvp.Value.Count > 1))
                    {
                        _logger.LogInformation($"    EventId {kvp.Key} used at:");
                        foreach (Location? loc in kvp.Value)
                            _logger.LogInformation($"    {loc.GetLineSpan()}");
                    }
                }

                if (missingEventIds.Count > 0)
                {
                    _logger.LogInformation($"    Missing EventIds = {missingEventIds.Count}");
                    //foreach (Location loc in missingEventIds)
                    //    _logger.LogInformation($"  {loc.GetLineSpan()}");
                }
            }
        }
    }
}


