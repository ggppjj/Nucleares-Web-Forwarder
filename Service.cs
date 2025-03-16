using Makaretu.Dns;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

ushort port = 8786;
var serviceDiscovery = new ServiceDiscovery();
var serviceProfile = new ServiceProfile("Nucleares-Web-Forwarder", "_http._tcp", port);
serviceProfile.AddProperty("machine", Environment.MachineName);
serviceDiscovery.Advertise(serviceProfile);

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();
builder.Host.UseSystemd();
builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));

var app = builder.Build();

app.Lifetime.ApplicationStopping.Register(() => serviceDiscovery.Unadvertise(serviceProfile));

var httpClient = new HttpClient();

app.Run(async context =>
{
    var targetUri = new Uri(
        "http://localhost:8785" + context.Request.Path + context.Request.QueryString
    );

    var forwardRequest = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

    foreach (var header in context.Request.Headers)
    {
        // Skip the Host header to avoid conflicts
        if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            continue;

        if (!forwardRequest.Headers.TryAddWithoutValidation(header.Key, [.. header.Value]))
        {
            _ = forwardRequest.Content?.Headers.TryAddWithoutValidation(
                header.Key,
                [.. header.Value]
            );
        }
    }

    // Optionally, explicitly set the Host header for the target server
    forwardRequest.Headers.Host = $"{targetUri.Host}:{targetUri.Port}";

    if (context.Request.ContentLength > 0)
        forwardRequest.Content = new StreamContent(context.Request.Body);

    using var responseMessage = await httpClient.SendAsync(forwardRequest);

    context.Response.StatusCode = (int)responseMessage.StatusCode;
    foreach (var header in responseMessage.Headers)
        context.Response.Headers[header.Key] = header.Value.ToArray();

    foreach (var header in responseMessage.Content.Headers)
        context.Response.Headers[header.Key] = header.Value.ToArray();

    // Remove transfer-encoding header if present
    _ = context.Response.Headers.Remove("transfer-encoding");

    await responseMessage.Content.CopyToAsync(context.Response.Body);
});

app.Run();
