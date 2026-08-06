using ServerBackup.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("serverbackup");
    config.SetApplicationVersion("0.1.0-dev");

    config.AddBranch("repo", repo =>
    {
        repo.SetDescription("Depo yönetimi: oluşturma, bilgi, katalog onarımı.");
        repo.AddCommand<RepoInitCommand>("init").WithDescription("Yeni bir depo oluşturur.");
        repo.AddCommand<RepoInfoCommand>("info").WithDescription("Depo hakkında bilgi gösterir.");
        repo.AddCommand<RepoRebuildIndexCommand>("rebuild-index")
            .WithDescription("Katalogu pack dosyalarından yeniden oluşturur.");
    });

    config.AddCommand<BackupCommand>("backup").WithDescription("Bir veya daha fazla yolu yedekler.");
});

return app.Run(args);
