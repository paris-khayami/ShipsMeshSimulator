using System.Globalization;
using ShipsMeshSimulator.Ais;
using ShipsMeshSimulator.Ships;

// Always print numbers as 53.5467, even on a computer set to German (53,5467).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// Read the API key from a private file outside this project, so it can't be committed.
builder.Configuration.AddUserSecrets<Program>(optional: true);

// Copy the "AisStream" settings from appsettings.json into an AisStreamOptions object.
builder.Services.Configure<AisStreamOptions>(builder.Configuration.GetSection("AisStream"));
builder.Services.Configure<TrackingOptions>(builder.Configuration.GetSection("Tracking"));

// Singleton means the whole app shares one ShipTracker, so every part sees the same ships.
builder.Services.AddSingleton<ShipTracker>();

// Run the ship data client in the background for as long as the app is running.
builder.Services.AddHostedService<AisStreamClient>();
builder.Services.AddHostedService<ShipReporter>();

var app = builder.Build();

// The map will replace this page in Milestone 3.
app.MapGet("/", () => "Ship Mesh Simulator is running. A ship summary is printed in the terminal.");

app.Run();
