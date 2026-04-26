using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Diagnostics;
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
             messageFormat: "ILogger '{0}': Missing EventId",
             category: "Logging",
             defaultSeverity: DiagnosticSeverity.Warning,
             isEnabledByDefault: true);
        public static readonly DiagnosticDescriptor Rule2 = new DiagnosticDescriptor(
             id: "GLG002",
             title: "Logger usage issue",
             messageFormat: "ILogger '{0}': Duplicate EventId {1}",
             category: "Logging",
             defaultSeverity: DiagnosticSeverity.Warning,
             isEnabledByDefault: true);
        public static readonly DiagnosticDescriptor Rule3 = new DiagnosticDescriptor(
            id: "GLG003",
            title: "Logger delegate missing EventId",
            messageFormat: "Logger delegate '{0}' is missing an EventId in LoggerMessageAttribute",
            category: "Logging",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule1, Rule2, Rule3); } }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(compilationContext =>
            {
                PerCompilationState state = new PerCompilationState(); // <- new state per compilation
                compilationContext.RegisterSyntaxNodeAction( ctx => AnalyzeInvocation(ctx, state), SyntaxKind.InvocationExpression);
                compilationContext.RegisterSymbolAction(ctx => AnalyzeLoggerMessageMethod(ctx, state), SymbolKind.Method);

                //compilationContext.RegisterSymbolAction(AnalyzeLoggerMessageMethod2, SymbolKind.Method);
                //if (!Debugger.IsAttached)
                //    Debugger.Launch();
            });
        }

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, PerCompilationState perCompilationState)
        {
            InvocationExpressionSyntax invocationExpr = (InvocationExpressionSyntax)context.Node;
            MemberAccessExpressionSyntax memberAccessExpr = invocationExpr.Expression as MemberAccessExpressionSyntax;
            if (memberAccessExpr == null) return;

            SymbolInfo symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccessExpr);
            IMethodSymbol methodSymbol = symbolInfo.Symbol as IMethodSymbol;
            if (methodSymbol == null) return;

            INamedTypeSymbol loggerInterface = context.Compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.ILogger");
            if (loggerInterface == null) return;

            // Validate receiver is an ILogger
            if ((methodSymbol.ContainingType != null && methodSymbol.ContainingType.Name.Contains("ILogger")) ||
                (methodSymbol.ReceiverType != null && (methodSymbol.ReceiverType.Name.Contains("ILogger") || methodSymbol.ReceiverType.AllInterfaces.Any(i => i.Name == "ILogger"))))
            {
                // Check for missing EventId
                ArgumentSyntax firstArg = invocationExpr.ArgumentList.Arguments.FirstOrDefault();
                if (firstArg == null)
                {
                    ReportDiagnosticMissingEventId(context, memberAccessExpr.Name, methodSymbol.Name);
                    return;
                }
                // Also check for missing EventId
                ITypeSymbol argType = context.SemanticModel.GetTypeInfo(firstArg.Expression).Type;
                bool isIntOrEventId = argType?.SpecialType == SpecialType.System_Int32 || argType?.Name == "EventId";
                if (!isIntOrEventId)
                {
                    ReportDiagnosticMissingEventId(context, memberAccessExpr.Name, methodSymbol.Name);
                    return;
                }

                // Attempt to extract EventId value (only literal ints for now)
                Optional<object> constantValue = context.SemanticModel.GetConstantValue(firstArg.Expression);
                if (constantValue.HasValue && constantValue.Value is int eventId)
                {
                    System.Collections.Generic.List<Location> seen = perCompilationState.EventIdLocations.GetOrAdd(eventId, _ => new());
                    seen.Add(memberAccessExpr.Name.GetLocation());
                    if (seen.Count > 1)
                    {
                        ReportDiagnosticDuplicateEventId(context, memberAccessExpr.Name, methodSymbol.Name, eventId);
                    }
                }
            }
        }

        private static void ReportDiagnosticMissingEventId(SyntaxNodeAnalysisContext context, SimpleNameSyntax locationNode, string methodName)
        {
            Diagnostic diag = Diagnostic.Create(Rule1, locationNode.GetLocation(), [methodName]);
            context.ReportDiagnostic(diag);
        }

        private static void ReportDiagnosticDuplicateEventId(SyntaxNodeAnalysisContext context, SimpleNameSyntax locationNode, string methodName, int duplicateEventId)
        {
            Diagnostic diag = Diagnostic.Create(Rule2, locationNode.GetLocation(), [methodName, duplicateEventId]);
            context.ReportDiagnostic(diag);
        }

        private static void AnalyzeLoggerMessageMethod2(SymbolAnalysisContext context)
        {
            ISymbol methodSymbol = context.Symbol;
            if (methodSymbol == null) return;

            if (methodSymbol.GetAttributes().Length == 0)
            {
                Debug.WriteLine($"No attributes found on method: {methodSymbol.Name}");
            }

            foreach (AttributeData attr in methodSymbol.GetAttributes())
            {
//throw new System.Exception("test JEOFF");

                INamedTypeSymbol attrClass = attr.AttributeClass;
                if (attrClass == null) continue;

                if (attrClass.ToDisplayString() == "Microsoft.Extensions.Logging.LoggerMessageAttribute")
                {
                    bool hasEventId = attr.NamedArguments.Any(kvp =>
                        kvp.Key == "EventId" && kvp.Value.Value is int id && id != 0);

                    if (!hasEventId)
                    {
                        MethodDeclarationSyntax methodDecl = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
                        if (methodDecl != null)
                        {
                            Location location = methodDecl.Identifier.GetLocation();
                            Diagnostic diagnostic = Diagnostic.Create(Rule3, location, methodSymbol.Name);
                            context.ReportDiagnostic(diagnostic);
                        }
                    }
                }
            }
        }
        
        private static void AnalyzeLoggerMessageMethod(SymbolAnalysisContext context, PerCompilationState perCompilationState)
        {
            ISymbol methodSymbol = context.Symbol;
            if (methodSymbol == null) return;

            foreach (AttributeData attr in methodSymbol.GetAttributes())
            {
//throw new System.Exception("test JEOFF");
                INamedTypeSymbol attrClass = attr.AttributeClass;
                if (attrClass == null) continue;

                if (attrClass.ToDisplayString() == "Microsoft.Extensions.Logging.LoggerMessageAttribute")
                {
                    bool hasEventId = attr.NamedArguments.Any(kvp =>
                        kvp.Key == "EventId" && kvp.Value.Value is int id && id != 0);

                    if (!hasEventId)
                    {
                        MethodDeclarationSyntax methodDecl = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
                        if (methodDecl != null)
                        {
                            Location location = methodDecl.Identifier.GetLocation();
                            Diagnostic diagnostic = Diagnostic.Create(Rule3, location, methodSymbol.Name);
                            context.ReportDiagnostic(diagnostic);
                        }
                    }
                }
            }
        }
    }
}
