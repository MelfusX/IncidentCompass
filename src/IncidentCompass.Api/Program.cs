using IncidentCompass.Api;
using IncidentCompass.Application;
using IncidentCompass.Infrastructure;
using IncidentCompass.Infrastructure.Intake;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApi(builder.Configuration, builder.Environment);

var app = builder.Build();

var validationExitCode = await TriageConfigurationValidationCommand.RunIfRequestedAsync(args, app.Services);
if (validationExitCode.HasValue)
{
    Environment.ExitCode = validationExitCode.Value;
    return;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.MapHealthChecks("/health");
app.MapApiV1();

app.Run();

public partial class Program;
