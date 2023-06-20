using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;


var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

var host = new HostBuilder()

    .ConfigureFunctionsWorkerDefaults()
    .Build();

host.Run();
