using System.Text.Json.Serialization;
using Hrdr.Api;
using Hrdr.Core;
using Hrdr.Core.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var configuredDataDir = builder.Configuration["Hrdr:DataDirectory"];
var dataDir = string.IsNullOrWhiteSpace(configuredDataDir)
    ? Path.Combine(builder.Environment.ContentRootPath, "data")
    : configuredDataDir;
Directory.CreateDirectory(dataDir);

var configuredCs = builder.Configuration.GetConnectionString("Default");
var connectionString = string.IsNullOrWhiteSpace(configuredCs)
    ? $"Data Source={Path.Combine(dataDir, "hrdr.db")}"
    : configuredCs;

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddHrdrCore(connectionString);
builder.Services.AddRazorPages();

builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithToolsFromAssembly();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HrdrDbContext>();
    await db.EnsureDatabaseAsync();
}

app.UseStaticFiles();
app.MapHrdrApi();
app.MapRazorPages();
app.MapMcp("/mcp");

app.Run();

public partial class Program;
