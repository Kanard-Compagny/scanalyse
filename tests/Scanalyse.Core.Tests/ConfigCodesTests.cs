using Scanalyse.Core.Scanners;

namespace Scanalyse.Core.Tests;

public class ConfigCodesTests
{
    [Fact]
    public void Honeywell_defalt_fait_123_modules()
    {
        // Mesure relevée sur un vrai code Honeywell : FNC3 + « DEFALT. » = 123 modules.
        Assert.Equal(123, Code128.Widths("DEFALT.", fnc3: true).Sum());
        Assert.Equal(112, Code128.Widths("DEFALT.", fnc3: false).Sum());
    }

    [Fact]
    public void Cle_datalogic_identique_a_celle_du_guide()
    {
        // Guide QuickScan : « $+$* » imprimé avec la clé « o » (valeur 79), « $+FJ2$- » avec la valeur 102.
        Assert.Equal(79, Code128.Symbols("$+$*", false)[^2]);
        Assert.Equal(102, Code128.Symbols("$+FJ2$-", false)[^2]);
        Assert.Equal(27, Code128.Symbols("$+", false)[^2]);
    }

    [Fact]
    public void Tous_les_codes_sont_encodables()
    {
        foreach (var c in ScannerConfigCodes.All.Where(c => !c.Matrix))
        {
            Assert.True(Code128.Widths(c.Data, c.Fnc3).Sum() > 30);
        }
    }

    [Fact]
    public void Codes_zebra_en_data_matrix()
    {
        var zebra = ScannerConfigCodes.All.Where(c => c.Brand == ScannerBrand.Zebra).ToList();
        Assert.True(zebra.Count >= 10);
        Assert.True(zebra.All(c => c.Matrix && c.Fnc3));
        Assert.Equal("92", zebra[0].Data);
    }
}
