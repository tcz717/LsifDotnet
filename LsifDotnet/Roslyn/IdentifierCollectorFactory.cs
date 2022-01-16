using Microsoft.Extensions.Logging;

namespace LsifDotnet.Roslyn;

public class IdentifierCollectorFactory
{
    public CSharpIdentifierCollector CreateInstance()
    {
        return new CSharpIdentifierCollector(LoggerFactory.CreateLogger<CSharpIdentifierCollector>());
    }

    public IdentifierCollectorFactory(ILoggerFactory loggerFactory)
    {
        LoggerFactory = loggerFactory;
    }

    public ILoggerFactory LoggerFactory { get; }
}