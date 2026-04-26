using Microsoft.Extensions.Logging;

public partial class TTTtest
{
    readonly ILogger<TTTtest> _logger;

    public TTTtest(ILogger<TTTtest> logger)
    {
        _logger = logger;
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Log has Event ID")]
    public static partial void LogProcessingStart(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Missing EventId but itemId ={ItemId}")]
    public static partial void LogMissingEvent(ILogger logger, int itemId);

    public void Scan(string[] args)
    {
        LogProcessingStart(_logger);
        LogMissingEvent(_logger, 446);
    }
}
