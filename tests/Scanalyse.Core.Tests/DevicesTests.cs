using Scanalyse.Core.Devices;

namespace Scanalyse.Core.Tests;

public class DevicesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Frame_is_delivered_after_silence_and_keeps_suffix()
    {
        var a = new FrameAssembler(TimeSpan.FromMilliseconds(80));
        Assert.Null(a.Append("0123"u8, T0));
        Assert.Null(a.Append("45\r\n"u8, T0.AddMilliseconds(5)));
        Assert.Null(a.Poll(T0.AddMilliseconds(50)));
        Assert.Equal("012345\r\n"u8.ToArray(), a.Poll(T0.AddMilliseconds(90)));
        Assert.False(a.HasPending);
    }

    [Fact]
    public void Late_bytes_close_the_previous_frame()
    {
        var a = new FrameAssembler(TimeSpan.FromMilliseconds(80));
        a.Append("AAA"u8, T0);
        Assert.Equal("AAA"u8.ToArray(), a.Append("BBB"u8, T0.AddMilliseconds(500)));
        Assert.Equal("BBB"u8.ToArray(), a.Flush());
    }

    [Fact]
    public void Oversized_frame_is_cut()
    {
        var a = new FrameAssembler(TimeSpan.FromMilliseconds(80), maxLength: 4);
        Assert.Equal("ABCDE"u8.ToArray(), a.Append("ABCDE"u8, T0));
    }

    [Fact]
    public void Decode_auto_falls_back_to_latin1()
    {
        Assert.Equal("é", RawData.Decode([0xC3, 0xA9]));
        Assert.Equal("é", RawData.Decode([0xE9]));
        Assert.Equal("Ã©", RawData.Decode([0xC3, 0xA9], TextDecoding.Latin1));
    }

    [Fact]
    public void Hex_dump()
    {
        Assert.Equal("30 31 1D 0D", RawData.ToHex([0x30, 0x31, 0x1D, 0x0D]));
        Assert.Equal("41 42 …", RawData.ToHex("ABC"u8, max: 2));
        Assert.Equal(string.Empty, RawData.ToHex([]));
    }

    [Theory]
    [InlineData(@"USB\VID_05E0&PID_1701\S/N", "Zebra (Symbol)")]
    [InlineData(@"\\?\HID#VID_0C2E&PID_0B61#7&1", "Honeywell (Metrologic)")]
    [InlineData(@"USB\VID_1A86&PID_7523\5&2", "Adaptateur USB-série CH340")]
    public void Vendor_is_recognized(string id, string name) =>
        Assert.Equal(name, UsbVendors.Identify(id)?.Name);

    [Fact]
    public void Unknown_vendor_is_null()
    {
        Assert.Null(UsbVendors.Identify(@"ACPI\PNP0501\1"));
        Assert.Equal(0x1234, UsbVendors.ParseVendorId("USB\\VID_1234&PID_0001"));
    }
}
