using Serilog;
using ServerBackup.Engine.Scheduling;
using ServerBackup.Service.Scheduling;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(AppContext.BaseDirectory, "logs", "serverbackup-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30));

builder.Services.Configure<ServerBackupOptions>(builder.Configuration.GetSection(ServerBackupOptions.SectionName));
builder.Services.AddSingleton<JobQueue>();
builder.Services.AddHostedService<BackupSchedulerService>();

var app = builder.Build();

app.MapGet("/", () => "ServerBackup service is running.");
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestampUtc = DateTimeOffset.UtcNow }));

app.Run();
