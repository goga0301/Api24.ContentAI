using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Api24ContentAI
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }


        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(static webBuilder =>
                {
                    _ = webBuilder.UseStartup<Startup>();
                });
        }
    }
}
