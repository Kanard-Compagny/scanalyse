using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace Scanalyse.Desktop;

/// <summary>Pages d'un PDF rendues en images par Windows (aucune bibliothèque à installer).</summary>
internal static class PdfPages
{
    /// <summary>Nombre maximal de pages analysées dans un même PDF.</summary>
    public const int MaxPages = 50;

    /// <summary>Rend chaque page en PNG, à environ 200 dpi (assez fin pour lire les petits codes).</summary>
    public static async Task<IReadOnlyList<byte[]>> RenderAsync(byte[] pdf)
    {
        using var input = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(input.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(pdf);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        input.Seek(0);
        var document = await PdfDocument.LoadFromStreamAsync(input);
        var pages = new List<byte[]>();
        for (uint i = 0; i < Math.Min(document.PageCount, (uint)MaxPages); i++)
        {
            using var page = document.GetPage(i);
            using var output = new InMemoryRandomAccessStream();
            // Taille de la page en unités de 1/96 pouce : × 2,1 ≈ 200 dpi, limité à 4 000 points de large.
            var width = (uint)Math.Min(4000, Math.Max(800, page.Size.Width * 2.1));
            await page.RenderToStreamAsync(output, new PdfPageRenderOptions { DestinationWidth = width });
            using var reader = new DataReader(output.GetInputStreamAt(0));
            await reader.LoadAsync((uint)output.Size);
            var bytes = new byte[output.Size];
            reader.ReadBytes(bytes);
            pages.Add(bytes);
        }

        return pages;
    }
}
