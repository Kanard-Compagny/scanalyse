using Scanalyse.Core.Text;

namespace Scanalyse.Core.Tests;

public class ControlCharactersTests
{
    [Fact]
    public void Visualize_ShowsControlCharactersByName()
    {
        Assert.Equal("0103453120000011<GS>21SN<CR><LF>", ControlCharacters.Visualize("0103453120000011\u001D21SN\r\n"));
    }

    [Fact]
    public void FromVisualized_IsTheInverseOfVisualize()
    {
        const string raw = "]d2\u001D0109506000134352\u001D10ABC\u0004";
        Assert.Equal(raw, ControlCharacters.FromVisualized(ControlCharacters.Visualize(raw)));
    }

    [Fact]
    public void FromVisualized_KeepsUnknownTags()
    {
        Assert.Equal("<ABC> et <b>", ControlCharacters.FromVisualized("<ABC> et <b>"));
    }

    [Fact]
    public void Find_ListsPositions()
    {
        var found = ControlCharacters.Find("A\u001DB\r");
        Assert.Equal(new[] { (1, '\u001D', "GS"), (3, '\r', "CR") }, found);
    }
}
