using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace LogGood
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class LogGoodAnalyzer : DiagnosticAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule1 = new DiagnosticDescriptor(
             id: "GLG001",
             title: "Logger usage issue",
             messageFormat: "ILogger '{methodName}': Missing EventId",
             category: "Logging",
             defaultSeverity: DiagnosticSeverity.Warning,
             isEnabledByDefault: true);
        public static readonly DiagnosticDescriptor Rule2 = new DiagnosticDescriptor(
             id: "GLG002",
             title: "Logger usage issue",
             messageFormat: "ILogger '{methodName}': Duplicate EventId {eventId}",
             category: "Logging",
             defaultSeverity: DiagnosticSeverity.Warning,
             isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule1, Rule2); } }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(compilationContext =>
            {
                var state = new PerCompilationState(); // <- new state per compilation
                compilationContext.RegisterSyntaxNodeAction( ctx => AnalyzeInvocation(ctx, state), SyntaxKind.InvocationExpression);
            });
        }

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, PerCompilationState perCompilationState)
        {
            var invocationExpr = (InvocationExpressionSyntax)context.Node;
            var memberAccessExpr = invocationExpr.Expression as MemberAccessExpressionSyntax;
            if (memberAccessExpr == null) return;

            var symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccessExpr);
            var methodSymbol = symbolInfo.Symbol as IMethodSymbol;
            if (methodSymbol == null) return;

            var loggerInterface = context.Compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.ILogger");
            if (loggerInterface == null) return;

            // Validate receiver is an ILogger
            if ((methodSymbol.ContainingType != null && methodSymbol.ContainingType.Name.Contains("ILogger")) ||
                (methodSymbol.ReceiverType != null && (methodSymbol.ReceiverType.Name.Contains("ILogger") || methodSymbol.ReceiverType.AllInterfaces.Any(i => i.Name == "ILogger"))))
            {
                // Check for missing EventId
                var firstArg = invocationExpr.ArgumentList.Arguments.FirstOrDefault();
                if (firstArg == null)
                {
                    ReportDiagnosticMissingEventId(context, memberAccessExpr.Name, methodSymbol.Name);
                    return;
                }
                // Also check for missing EventId
                var argType = context.SemanticModel.GetTypeInfo(firstArg.Expression).Type;
                var isIntOrEventId = argType?.SpecialType == SpecialType.System_Int32 || argType?.Name == "EventId";
                if (!isIntOrEventId)
                {
                    ReportDiagnosticMissingEventId(context, memberAccessExpr.Name, methodSymbol.Name);
                    return;
                }

                // Attempt to extract EventId value (only literal ints for now)
                var constantValue = context.SemanticModel.GetConstantValue(firstArg.Expression);
                if (constantValue.HasValue && constantValue.Value is int eventId)
                {
                    var seen = perCompilationState.EventIdLocations.GetOrAdd(eventId, _ => new());
                    seen.Add(memberAccessExpr.Name.GetLocation());
                    if (seen.Count > 1)
                        ReportDiagnosticDuplicateEventId(context, memberAccessExpr.Name, methodSymbol.Name, eventId);
                }
            }
        }

        private static void ReportDiagnosticMissingEventId(SyntaxNodeAnalysisContext context, SimpleNameSyntax locationNode, string methodName)
        {
            var diag = Diagnostic.Create(Rule1, locationNode.GetLocation(), [methodName]);
            context.ReportDiagnostic(diag);
        }

        private static void ReportDiagnosticDuplicateEventId(SyntaxNodeAnalysisContext context, SimpleNameSyntax locationNode, string methodName, int duplicateEventId)
        {
            var diag = Diagnostic.Create(Rule2, locationNode.GetLocation(), [methodName, duplicateEventId]);
            context.ReportDiagnostic(diag);
        }

    }

    public class PerCompilationState
    {
        public ConcurrentDictionary<int, List<Location>> EventIdLocations { get; private set; } = new();
    }
}
