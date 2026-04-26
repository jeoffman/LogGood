using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Collections.Immutable;

namespace LogGood.Test
{

    /// <summary>
    /// Not sure if I am a fan of these "markup" style tests ... I don't think that I am
    /// </summary>
    public class LogGoodUnitTests
    {
        [Fact]
        public async Task YourLogsAreAllGood()
        {
            var testCode = @"
using Microsoft.Extensions.Logging;

class Program
{
    private ILogger _logger;

    public void DoStuff()
    {
        _logger.LogWarning(123, ""HAS EventId!""); // Its good
        _logger.LogError(125, ""Good Logging!"");   // This one is fine?
    }
}";

            await new CSharpAnalyzerTest<LogGoodAnalyzer, DefaultVerifier>
            {
                TestCode = testCode,
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80.AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.Extensions.Logging.Abstractions", "8.0.0"))),
            }.RunAsync();
        }

        [Fact]
        public async Task FlagsMissingEventIdOne()
        {
            var testCode = @"
using Microsoft.Extensions.Logging;

class Program
{
    private ILogger _logger;

    public void DoStuff()
    {
        _logger.[|LogInformation|](""Missing EventId!""); // Should trigger diagnostic
    }
}";
            CSharpAnalyzerTest<LogGoodAnalyzer, DefaultVerifier> test = new CSharpAnalyzerTest<LogGoodAnalyzer, DefaultVerifier>
            {
                MarkupOptions = MarkupOptions.UseFirstDescriptor,
                TestCode = testCode,
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80.AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.Extensions.Logging.Abstractions", "8.0.0"))),
            };

            await test.RunAsync();
        }


        [Fact]
        public async Task FlagsMissingEventIdTwo()
        {
            var testCode = @"
using Microsoft.Extensions.Logging;

class Program
{
    private ILogger _logger;

    public void DoStuff()
    {
        _logger.[|LogInformation|](""Missing EventId!""); // Should trigger diagnostic = MISSING
        _logger.LogWarning(123, ""HAS EventId!""); // still good
        _logger.LogError(125, ""Good Logging!"");   // This one is fine?
    }
}";

            var expected = new DiagnosticResult("LOG002", DiagnosticSeverity.Warning)
                .WithSpan(9, 18, 9, 31)
                .WithArguments("LogInformation");

            await new CSharpAnalyzerTest<LogGoodAnalyzer, DefaultVerifier>
            {
                MarkupOptions = MarkupOptions.UseFirstDescriptor,
                TestCode = testCode,
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80.AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.Extensions.Logging.Abstractions", "8.0.0"))),
                //ExpectedDiagnostics = { expected }
            }.RunAsync();
        }


        [Fact]
        public async Task LoggingDelegateCheckMissingEventId()
        {
            var testCode = @"using Microsoft.Extensions.Logging;

public partial class LoggerSearch
{
    readonly ILogger<LoggerSearch> _logger;

    public LoggerSearch(ILogger<LoggerSearch> logger)
    {
        _logger = logger;
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = ""Log has Event ID"")]
    public static partial void LogProcessingStart(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = ""Missing EventId but itemId={ItemId}"")]
    public static partial void {|GLG003:LogMissingEvent|}(ILogger logger, int itemId);

    public void Scan(string[] args)
    {
        LogProcessingStart(_logger);
        LogMissingEvent(_logger, 446);
    }

    public static partial void LogProcessingStart(global::Microsoft.Extensions.Logging.ILogger logger) { }
    public static partial void {|GLG003:LogMissingEvent|}(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Int32 itemId) { }
}";

            await new CSharpAnalyzerTest<LogGoodAnalyzer, DefaultVerifier>
            {
                TestCode = testCode,
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80.AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.Extensions.Logging.Abstractions", "8.0.0"))),
            }.RunAsync();
        }
    }
}
