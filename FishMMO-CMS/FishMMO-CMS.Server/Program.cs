using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
	// Bundled defaults from FishMMO-Setup (copied to output directory).
	ContentRootPath = AppContext.BaseDirectory,
	Args = args,
});

// Working-directory overrides take precedence over bundled defaults.
string env = builder.Environment.EnvironmentName;
builder.Configuration.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"), optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), $"appsettings.{env}.json"), optional: true, reloadOnChange: true);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// TODO: Register FishMMO-Database services
// TODO: Register FishMMO-Auth services

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
