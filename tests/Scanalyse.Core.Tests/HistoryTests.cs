using Scanalyse.Core.Content;
using Scanalyse.Core.History;

namespace Scanalyse.Core.Tests;

public class HistoryTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "scanalyse-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Entries_and_cases_survive_a_reload()
    {
        var folder = TempFolder();
        var store = new HistoryStore(folder);
        store.Load();
        var c = new ServiceCase { Client = "Entrepôt Nord", Reference = "INC-4521" };
        store.SaveCase(c);
        store.Add(new HistoryEntry { Kind = HistoryKind.ImageAnalysis, CaseId = c.Id, Source = "photo.jpg", Codes = [new("Data Matrix", "(01)03453120000011", "]d2", "Données GS1")] }, [1, 2, 3]);
        store.Add(new HistoryEntry { Kind = HistoryKind.Comparison, Expected = "ABC", Received = "ABC<CR>", Verdict = "Lecture conforme" });

        var again = new HistoryStore(folder);
        again.Load();
        Assert.Equal(2, again.Entries.Count);
        Assert.Equal(HistoryKind.Comparison, again.Entries[0].Kind);
        Assert.Equal("Entrepôt Nord · INC-4521", again.FindCase(c.Id)!.DisplayName);
        Assert.Equal("Non classé", again.Cases[0].DisplayName);
        Assert.NotNull(again.ImagePath(again.EntriesOf(c.Id).Single()));
        Directory.Delete(folder, true);
    }

    [Fact]
    public void Deleting_a_case_moves_its_entries_to_inbox()
    {
        var folder = TempFolder();
        var store = new HistoryStore(folder);
        store.Load();
        var c = new ServiceCase { Client = "Client A" };
        store.SaveCase(c);
        var e = store.Add(new HistoryEntry { CaseId = c.Id, Source = "x" });
        store.DeleteCase(c);
        Assert.Equal(ServiceCase.InboxId, store.Entries.Single(x => x.Id == e.Id).CaseId);
        Assert.Null(store.FindCase(c.Id));
        Directory.Delete(folder, true);
    }

    [Fact]
    public void Search_ignores_case_and_accents()
    {
        var e = new HistoryEntry { Source = "Caméra : Logitech", Codes = [new("EAN-13", "3453120000011", Kind: "Article (GTIN)")] };
        Assert.True(HistorySearch.Matches(e, "camera 345312"));
        Assert.False(HistorySearch.Matches(e, "pdf417"));
    }

    [Fact]
    public void Report_contains_case_summary_and_escapes_content()
    {
        var c = new ServiceCase { Client = "Client <B>", Reference = "T-1", Technician = "F. Martineau", Notes = "Ligne 1\nLigne 2" };
        var entries = new[]
        {
            new HistoryEntry { At = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero), Kind = HistoryKind.Comparison, Expected = "A<GS>B", Received = "A]B", Verdict = "Problème de lecture", Worst = Severity.Error,
                Findings = [new(Severity.Error, "Séparateur GS perdu", "La douchette remplace GS.", "Activer la transmission de GS")] },
            new HistoryEntry { At = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero), Kind = HistoryKind.ImageAnalysis, Codes = [new("QR Code", "https://exemple.fr")] },
        };
        var html = ReportBuilder.Build(c, entries, _ => null, new(2026, 10, 22, 10, 0, 0, TimeSpan.Zero), "0.7.0");
        Assert.Contains("Client &lt;B&gt; · T-1", html);
        Assert.Contains("A&lt;GS&gt;B", html);
        Assert.Contains("Ligne 1<br>Ligne 2", html);
        Assert.Contains("Séparateur GS perdu", html);
        Assert.Contains("À faire : Activer la transmission de GS", html);
        Assert.True(html.IndexOf("https://exemple.fr", StringComparison.Ordinal) < html.IndexOf("A&lt;GS&gt;B", StringComparison.Ordinal));
    }

    [Fact]
    public void Cases_are_one_file_each_and_grouped_by_client()
    {
        var folder = TempFolder();
        var store = new HistoryStore(folder);
        store.Load();
        var a = new ServiceCase { Client = "Entrepôt Nord", Reference = "T-1" };
        var b = new ServiceCase { Client = "entrepot nord ", Reference = "T-2" };
        store.SaveCase(a);
        store.SaveCase(b);
        store.SaveCase(new ServiceCase { Client = "Autre" });
        Assert.Equal(3, Directory.GetFiles(Path.Combine(folder, "Dossiers")).Length);
        Assert.Equal(2, store.CasesOfClient("ENTREPOT NORD").Count);
        Directory.Delete(folder, true);
    }

    [Fact]
    public void Legacy_single_cases_file_is_split()
    {
        var folder = TempFolder();
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "dossiers.json"), """[{"Id":"abc","Client":"Ancien"},{"Id":"inbox"}]""");
        var store = new HistoryStore(folder);
        store.Load();
        Assert.Equal("Ancien", store.FindCase("abc")!.Client);
        Assert.False(File.Exists(Path.Combine(folder, "dossiers.json")));
        Directory.Delete(folder, true);
    }

    [Fact]
    public void Client_report_groups_entries_by_case_with_code_details()
    {
        var a = new ServiceCase { Id = "a", Client = "Client X", Reference = "T-1" };
        var b = new ServiceCase { Id = "b", Client = "Client X", Reference = "T-2" };
        var entries = new[]
        {
            new HistoryEntry { CaseId = "a", At = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), Codes = [new("Data Matrix", "(01)03453120000011", "]d2", "Données GS1", "Lecture fiable", "good",
                [new("GTIN (article) AI 01", "03453120000011")], [new("Symbologie", "Data Matrix")], [new("warn", "Modules très petits")])] },
            new HistoryEntry { CaseId = "b", At = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero), Kind = HistoryKind.Comparison, Expected = "X", Received = "X" },
        };
        var html = ReportBuilder.Build(new ServiceCase { Client = "Client X", Reference = "Tous les dossiers" }, entries, _ => null, DateTimeOffset.Now, "1", [a, b]);
        Assert.Contains("Dossier T-1", html);
        Assert.Contains("Dossier T-2", html);
        Assert.True(html.IndexOf("Dossier T-1", StringComparison.Ordinal) < html.IndexOf("Dossier T-2", StringComparison.Ordinal));
        Assert.Contains("Ce que contient le code", html);
        Assert.Contains("Modules très petits", html);
    }
}
