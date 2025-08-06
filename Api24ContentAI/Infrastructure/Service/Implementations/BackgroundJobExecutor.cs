using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;



namespace Api24ContentAI.Infrastructure.Service.Implementations {
    public static class BackgroundJobExecutor 
    {
        private static ILogger? _logger;

        public static void Initialize(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("BackgroundJobExecutor");
        }

        public static void Run(Func<Task> backgroundTask)
        {
            Task.Run(async () =>
                    {
                    try
                    {
                    await backgroundTask();
                    }
                    catch (Exception ex)
                    {
                    _logger?.LogError(ex, "Background job failed");
                    }
                    });
        }
    }
}
