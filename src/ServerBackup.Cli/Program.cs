using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("serverbackup");
    config.SetApplicationVersion("0.1.0-dev");
});

return app.Run(args);
