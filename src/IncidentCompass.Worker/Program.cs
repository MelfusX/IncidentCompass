using IncidentCompass.Worker;
using IncidentCompass.Application;
using IncidentCompass.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddWorker();

var host = builder.Build();
host.Run();
