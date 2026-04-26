using Microsoft.CodeAnalysis;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace LogGood
{
    public class PerCompilationState
    {
        public ConcurrentDictionary<int, List<Location>> EventIdLocations { get; private set; } = new();
    }
}
