using System.Globalization;
using Microsoft.Extensions.Options;
using ShipsMeshSimulator.Ais;
using ShipsMeshSimulator.Mesh;
using ShipsMeshSimulator.Ships;

// Always print numbers as 53.5467, even on a computer set to German (53,5467).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// Read the API key from a private file outside this project, so it can't be committed.
builder.Configuration.AddUserSecrets<Program>(optional: true);

// Copy settings from appsettings.json into these classes.
builder.Services.Configure<AisStreamOptions>(builder.Configuration.GetSection("AisStream"));
builder.Services.Configure<TrackingOptions>(builder.Configuration.GetSection("Tracking"));
builder.Services.Configure<MeshOptions>(builder.Configuration.GetSection("Mesh"));

// Singleton means the whole app shares one ShipTracker, so every part sees the same ships.
builder.Services.AddSingleton<ShipTracker>();
builder.Services.AddSingleton<MeshNetwork>();

// SignalR keeps a live connection open so the server can push updates to the browser.
builder.Services.AddSignalR();

// Run these in the background for as long as the app is running.
builder.Services.AddHostedService<AisStreamClient>();
builder.Services.AddHostedService<ShipBroadcaster>();
builder.Services.AddHostedService(services => services.GetRequiredService<MeshNetwork>());

var app = builder.Build();

// Serve the web page from the wwwroot folder, starting with index.html.
app.UseDefaultFiles();
app.UseStaticFiles();

// Tell the page which area to show. Only these four values are sent, never the API key.
app.MapGet("/api/map-area", (IOptions<AisStreamOptions> options) => new
{
    options.Value.MinLatitude,
    options.Value.MinLongitude,
    options.Value.MaxLatitude,
    options.Value.MaxLongitude
});

app.MapHub<ShipHub>("/hubs/ships");

app.Run();
