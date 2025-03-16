using System.Diagnostics;
using Makaretu.Dns;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

const ushort port = 8786;

//const string targetProcessName = "Nucleares";

var serviceDiscovery = new ServiceDiscovery();
var domainName = new DomainName("Nucleares-Web-Forwarder");
var serviceName = new DomainName("_http.tcp");
var serviceProfile = new ServiceProfile(domainName, serviceName, port);
serviceProfile.AddProperty("machine", Environment.MachineName);

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSystemd(); // Ensure proper logging integration with systemd
builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));

var app = builder.Build();
var httpClient = new HttpClient();

app.Lifetime.ApplicationStopping.Register(() => serviceDiscovery.Unadvertise(serviceProfile));

app.Run(async context =>
{
    var targetUri = new Uri(
        $"http://localhost:8785{context.Request.Path}{context.Request.QueryString}"
    );
    var forwardRequest = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

    foreach (var header in context.Request.Headers)
    {
        if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            continue;

        if (!forwardRequest.Headers.TryAddWithoutValidation(header.Key, [.. header.Value]))
            _ = forwardRequest.Content?.Headers.TryAddWithoutValidation(
                header.Key,
                [.. header.Value]
            );
    }

    forwardRequest.Headers.Host = $"{targetUri.Host}:{targetUri.Port}";

    if (context.Request.ContentLength > 0)
        forwardRequest.Content = new StreamContent(context.Request.Body);

    using var responseMessage = await httpClient.SendAsync(forwardRequest);
    context.Response.StatusCode = (int)responseMessage.StatusCode;

    foreach (var header in responseMessage.Headers)
        context.Response.Headers[header.Key] = header.Value.ToArray();

    foreach (var header in responseMessage.Content.Headers)
        context.Response.Headers[header.Key] = header.Value.ToArray();

    _ = context.Response.Headers.Remove("transfer-encoding");

    await responseMessage.Content.CopyToAsync(context.Response.Body);
});

app.Run();


//TODO: Fix linux process monitoring.
//await MonitorProcessAndRunServer();
//return;

//bool IsProcessRunning(string name)
//{
//    return Process.GetProcessesByName(name).Length > 0 ||
//           Process.GetProcessesByName(name.ToLower()).Length > 0;
//}

//void Log(string message)
//=> Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");


//async Task MonitorProcessAndRunServer()
//{
//    Console.WriteLine("Starting process monitor...");
//    Console.Out.Flush();
//    while (true)
//    {
//        if (IsProcessRunning(targetProcessName))
//        {
//            Log($"{targetProcessName} detected. Starting forwarder.");
//            serviceDiscovery.Advertise(serviceProfile);
//            await app.RunAsync();
//            serviceDiscovery.Unadvertise(serviceProfile);
//            Log($"{targetProcessName} stopped. Forwarder shutting down.");
//        }
//
//        await Task.Delay(5000); // Check every 5 seconds
//    }
//    // ReSharper disable once FunctionNeverReturns
//}
