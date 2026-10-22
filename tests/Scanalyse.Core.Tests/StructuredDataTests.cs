using Scanalyse.Core.Content;

namespace Scanalyse.Core.Tests;

public class StructuredDataTests
{
    [Fact]
    public void Iso15434_transport_message_is_split_into_formats_and_fields()
    {
        var v = StructuredData.Parse("[)><RS>01<GS>02<GS>0044470<GS>901<GS>898<GS>XR630094657248<GS>GEOP<GS><GS>1/1<GS>N<GS>7 Bis rue Augustin<US>Fresnel<RS>07<GS>G03<GS>0<RS><EOT>")!;
        Assert.NotNull(v.Envelope);
        Assert.Equal(2, v.Sections.Count);
        Assert.True(v.Sections[0].Title.StartsWith("Format 01", StringComparison.Ordinal));
        Assert.Equal("0044470", v.Sections[0].Fields[1].Value);
        Assert.Equal("7 Bis rue Augustin|Fresnel", string.Join("|", v.Sections[0].Fields[^1].Parts));
        Assert.True(v.Sections[1].Title.StartsWith("Format 07", StringComparison.Ordinal));
        Assert.Contains(v.Remarks, r => r.Contains("vide"));
    }

    [Fact]
    public void Missing_eot_is_reported()
    {
        var v = StructuredData.Parse("[)><RS>06<GS>1PABC<GS>SXYZ")!;
        Assert.Contains(v.Remarks, r => r.Contains("EOT"));
        Assert.Equal("1PABC", v.Sections[0].Fields[0].Value);
        Assert.Equal("1P", v.Sections[0].Fields[0].Identifier);
        Assert.Equal("numéro de série", v.Sections[0].Fields[1].IdentifierName);
    }

    [Fact]
    public void Plain_text_has_no_structure()
    {
        Assert.Null(StructuredData.Parse("XXRAN261701571"));
        var v = StructuredData.Parse("A<GS>B")!;
        Assert.Null(v.Envelope);
        Assert.Equal(2, v.Sections[0].Fields.Count);
    }
}
