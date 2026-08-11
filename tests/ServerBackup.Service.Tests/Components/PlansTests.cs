using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ServerBackup.Data;
using ServerBackup.Data.Entities;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Retention;
using ServerBackup.Engine.Scheduling;
using ServerBackup.Service.Components.Pages;
using ServerBackup.Service.Scheduling;
using Xunit;

namespace ServerBackup.Service.Tests.Components;

/// <summary>
/// A plan used to be write-once: whatever was typed into the create dialog was
/// what it stayed. These cover the two ways out of that.
/// </summary>
public sealed class PlansTests : BunitContext, IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _repoPath = Path.Combine(Path.GetTempPath(), "sb-ui-plans-repo-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task Editing_a_plan_keeps_its_id_so_the_snapshots_it_took_stay_attached()
    {
        var planId = await SeedPlanAsync("Muhasebe — günlük", "0 3 * * *");
        var cut = RenderPlans();

        ClickLabelled(cut, "'Muhasebe — günlük' planını düzenle");
        cut.Find("#f-plan-name").Change("Muhasebe — gece");
        ClickText(cut, "Değişiklikleri kaydet");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("güncellendi"), TimeSpan.FromSeconds(30));

        var plans = await LoadPlansAsync();
        plans.Should().ContainSingle();
        plans[0].PlanId.Should().Be(planId, "a new id would orphan every snapshot this plan already took");
        plans[0].Name.Should().Be("Muhasebe — gece");
        plans[0].CronSchedule.Should().Be("0 3 * * *", "an untouched schedule must survive a rename");
    }

    [Fact]
    public async Task The_edit_dialog_reopens_a_saved_schedule_in_the_simple_form()
    {
        await SeedPlanAsync("Hafta içi", "0 8-20/2 * * 1,2,3,4,5");
        var cut = RenderPlans();

        ClickLabelled(cut, "'Hafta içi' planını düzenle");
        ClickText(cut, "Zamanlama");

        // Reverse-parsed into day/hour controls rather than dumped as raw cron.
        cut.Find("#f-plan-start").GetAttribute("value").Should().Be("8");
        cut.Find("#f-plan-end").GetAttribute("value").Should().Be("20");
        cut.Markup.Should().NotContain("f-plan-cron");
    }

    [Fact]
    public async Task A_hand_written_cron_expression_reopens_in_the_cron_box_untouched()
    {
        await SeedPlanAsync("Ayın biri", "0 3 1 * *");
        var cut = RenderPlans();

        ClickLabelled(cut, "'Ayın biri' planını düzenle");
        ClickText(cut, "Zamanlama");

        cut.Find("#f-plan-cron").GetAttribute("value").Should().Be("0 3 1 * *",
            "an expression the simple form cannot express must not be silently rewritten");
    }

    [Fact]
    public async Task Deleting_a_plan_is_confirmed_first_and_leaves_its_snapshots_behind()
    {
        var planId = await SeedPlanAsync("Geçici", "0 3 * * *");
        await SeedSnapshotAsync(planId);

        var cut = RenderPlans();
        ClickLabelled(cut, "'Geçici' planını sil");

        cut.Markup.Should().Contain("Snapshot'lar silinmez");

        ClickText(cut, "Planı sil");
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("silindi"), TimeSpan.FromSeconds(30));

        (await LoadPlansAsync()).Should().BeEmpty();

        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
        (await db.Snapshots.CountAsync()).Should().Be(1, "the backups this plan took are still restorable");
    }

    [Fact]
    public async Task Dismissing_the_delete_confirmation_changes_nothing()
    {
        await SeedPlanAsync("Geçici", "0 3 * * *");
        var cut = RenderPlans();

        ClickLabelled(cut, "'Geçici' planını sil");
        ClickText(cut, "Vazgeç");

        (await LoadPlansAsync()).Should().ContainSingle();
    }

    private IRenderedComponent<Plans> RenderPlans()
    {
        Services.AddSingleton(Options.Create(new ServerBackupOptions { Repositories = [_repoPath] }));
        Services.AddSingleton(new JobQueue());

        var cut = Render<Plans>();
        cut.WaitForAssertion(() => cut.Find("tbody tr"), TimeSpan.FromSeconds(30));
        return cut;
    }

    private async Task<string> SeedPlanAsync(string name, string cron)
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password);

        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));

        var planId = Guid.NewGuid().ToString("n");
        db.Plans.Add(new PlanEntity
        {
            PlanId = planId,
            Name = name,
            SourcePathsJson = JsonSerializer.Serialize(new[] { @"C:\Veri" }),
            CronSchedule = cron,
            RetentionPolicyJson = JsonSerializer.Serialize(new RetentionPolicy(KeepDaily: 7, KeepWeekly: 4, KeepMonthly: 12)),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return planId;
    }

    private async Task SeedSnapshotAsync(string planId)
    {
        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
        db.Snapshots.Add(new SnapshotEntity
        {
            SnapshotId = Guid.NewGuid().ToString("n"),
            PlanId = planId,
            RootTreeBlobId = new string('a', 64),
            StartedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<PlanEntity>> LoadPlansAsync()
    {
        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
        return await db.Plans.AsNoTracking().ToListAsync();
    }

    /// <summary>A Windows path in a CSS attribute selector reads as escape sequences, so match in C# instead.</summary>
    private static void ClickLabelled(IRenderedComponent<Plans> cut, string ariaLabel) =>
        cut.FindAll("button").Single(b => b.GetAttribute("aria-label") == ariaLabel).Click();

    private static void ClickText(IRenderedComponent<Plans> cut, string text) =>
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(text, StringComparison.Ordinal)).Click();

    public new void Dispose()
    {
        if (Directory.Exists(_repoPath))
        {
            Directory.Delete(_repoPath, recursive: true);
        }

        base.Dispose();
    }
}
