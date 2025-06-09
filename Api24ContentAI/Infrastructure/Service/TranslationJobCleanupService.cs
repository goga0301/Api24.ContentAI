using Api24ContentAI.Domain.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service
{
    public class TranslationJobCleanupService : BackgroundService
    {
        private readonly ILogger<TranslationJobCleanupService> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(2); // Run every 2 hours

        public TranslationJobCleanupService(
            ILogger<TranslationJobCleanupService> logger,
            IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Translation Job Cleanup Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PerformCleanup();
                    
                    // Wait for the next cleanup interval
                    await Task.Delay(CleanupInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    _logger.LogInformation("Translation Job Cleanup Service is stopping");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during translation job cleanup");
                    
                    // Wait a shorter time before retrying on error
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                }
            }
        }

        private async Task PerformCleanup()
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var translationJobService = scope.ServiceProvider.GetRequiredService<ITranslationJobService>();
                
                _logger.LogDebug("Starting translation job cleanup");
                await translationJobService.CleanupOldJobs();
                _logger.LogDebug("Translation job cleanup completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cleanup expired translation jobs");
                throw;
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Translation Job Cleanup Service is stopping");
            await base.StopAsync(cancellationToken);
        }
    }
} 