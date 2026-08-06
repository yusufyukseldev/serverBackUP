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
    config.AddCommand<RestoreCommand>("restore").WithDescription("Bir snapshot'ı geri yükler.");
    config.AddCommand<VerifyCommand>("verify").WithDescription("Depo bütünlüğünü doğrular.");
    config.AddCommand<SnapshotsCommand>("snapshots").WithDescription("Snapshot listesini gösterir.");
    config.AddCommand<LsCommand>("ls").WithDescription("Bir snapshot içindeki dosyaları listeler.");
    config.AddCommand<DiffCommand>("diff").WithDescription("İki snapshot arasındaki farkları gösterir.");
});

return app.Run(args);
