using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace TestApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var services = new ServiceCollection();

            services.AddLogging(config => config.AddConsole());

            services.AddTransient<LoggerSearch>();

            var serviceProvider = services.BuildServiceProvider();

            var loggin = serviceProvider.GetRequiredService<LoggerSearch>();

            await loggin.Scan(args);
        }
    }
}
