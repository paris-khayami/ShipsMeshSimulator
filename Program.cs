using System.Globalization;
using ShipsMeshSimulator.Ais;

// Always print numbers as 53.5467, even on a computer set to German (53,5467).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// Read the API key from a private file outside this project, so it can't be committed.
builder.Configuration.AddUserSecrets<Program>(optional: true);

// Copy the "AisStream" settings from appsettings.json into an AisStreamOptions object.
builder.Services.Configure<AisStreamOptions>(builder.Configuration.GetSection("AisStream"));

// Run the ship data client in the background for as long as the app is running.
builder.Services.AddHostedService<AisStreamClient>();

var app = builder.Build();

// The map will replace this page in Milestone 3.
app.MapGet("/", () => "Ship Mesh Simulator is running. Ship data is printed in the terminal.");

app.Run();
