using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace IncidentCompass.Infrastructure.Intake;

// Real Api/Worker hosts always register a concrete IHostEnvironment before AddInfrastructure runs
// (WebApplication.CreateBuilder / Host.CreateApplicationBuilder both do this), so
// IntakeSetup.AddIntakeInfrastructure's TryAddSingleton<IHostEnvironment> never reaches this type
// there. It only satisfies compositions that build a raw ServiceCollection without going through a
// real host builder (for example, HostCompositionTests' ValidateOnBuild scope-validation test) --
// those compositions have no reason to care about content-root resolution, so the current working
// directory is a safe, side-effect-free default.
internal sealed class FallbackHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;

    public string ApplicationName { get; set; } = typeof(FallbackHostEnvironment).Assembly.GetName().Name ?? string.Empty;

    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
