using System.Net;
using System.Net.Sockets;
using HomeEasy.Protocol;

namespace HomeEasy.Protocol.Tests;

public class AcStateTests
{
    // Real frames captured from CH-S12FTXN-E2wf on 2026-11-15.
    private const string Heat21 = "AAAA12000A0A000C0500D40000000019050000007D";
    private const string Heat22Cmd = "AAAA12010A0A000C0600D40000000019050000007F";

    [Fact]
    public void Decodes_real_status_frame()
    {
        var s = new AcState(Convert.FromHexString(Heat21));
        Assert.True(s.Power);
        Assert.Equal(AcMode.Heat, s.Mode);
        Assert.Equal(21, s.SetpointCelsius);
        Assert.Equal(25.5, s.IndoorCelsius, 1);
        Assert.Equal(FanSetting.Auto, s.Fan);
        Assert.True(s.Display);
        Assert.True(s.Health);
        Assert.True(s.Aux);
        Assert.False(s.Eco);
    }

    [Fact]
    public void Setpoint_change_produces_same_command_as_original_app_protocol()
    {
        var s = new AcState(Convert.FromHexString(Heat21));
        s.SetpointCelsius = 22;
        Assert.Equal(Heat22Cmd, Convert.ToHexString(s.ToCommand()));
    }

    [Fact]
    public void Status_request_checksum_is_valid()
    {
        var req = Convert.FromHexString("AAAA12A00A0A00000000000000000000000000001A");
        Assert.Equal(req[^1], AcState.Checksum(req));
    }

    [Theory]
    [InlineData(FanSetting.Turbo)]
    [InlineData(FanSetting.Quiet)]
    [InlineData(FanSetting.L4)]
    public void Fan_setting_round_trips_and_is_exclusive(FanSetting fan)
    {
        var s = new AcState(Convert.FromHexString(Heat21)) { Fan = fan };
        var back = new AcState(s.ToCommand());
        Assert.Equal(fan, back.Fan);
        Assert.False(back.Turbo && back.Quiet);
    }

    [Fact]
    public void Power_mode_and_swing_round_trip_without_touching_other_fields()
    {
        var s = new AcState(Convert.FromHexString(Heat21))
        {
            Power = false, Mode = AcMode.Cool, VerticalSwing = VerticalSwing.Swing, HorizontalSwing = HorizontalSwing.SwingWide,
        };
        var back = new AcState(s.ToCommand());
        Assert.False(back.Power);
        Assert.Equal(AcMode.Cool, back.Mode);
        Assert.Equal(VerticalSwing.Swing, back.VerticalSwing);
        Assert.Equal(HorizontalSwing.SwingWide, back.HorizontalSwing);
        Assert.Equal(21, back.SetpointCelsius);
        Assert.True(back.Display);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(31)]
    public void Setpoint_out_of_range_throws(int t) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AcState(Convert.FromHexString(Heat21)).SetpointCelsius = t);

    [Fact]
    public void Off_timer_matches_real_command_and_decodes_countdown()
    {
        // Real: enabling a 2-minute off timer on a running AC.
        var s = new AcState(Convert.FromHexString("AAAA12000A0A000C0501D40000003C1905000000BA"));
        s.SetOffTimer(2);
        Assert.Equal("AAAA12010A0A000C0501F48000023C19050000005D", Convert.ToHexString(s.ToCommand()));

        var countdown = new AcState(Convert.FromHexString("AAAA12000A0A000C0501F48000013C19050000005B"));
        Assert.True(countdown.OffTimerEnabled);
        Assert.Equal(1, countdown.OffTimerMinutes);
    }

    [Fact]
    public void Off_timer_long_duration_round_trips_and_cancel_clears()
    {
        var s = new AcState(Convert.FromHexString(Heat21));
        s.SetOffTimer(600);
        var back = new AcState(s.ToCommand());
        Assert.Equal(600, back.OffTimerMinutes);
        Assert.True(back.OffTimerEnabled);

        back.SetOffTimer(null);
        Assert.False(back.OffTimerEnabled);
        Assert.Equal(0, back.OffTimerMinutes);
    }

    [Fact]
    public void Parses_real_discovery_reply()
    {
        var ok = Discovery.TryParseReply(Convert.FromHexString("AAAA0C0301020208BC200002A000EE"), IPAddress.Parse("192.168.0.5"), out var d);
        Assert.True(ok);
        Assert.Equal("08bc200002a0", d!.Mac);
        Assert.Equal((byte)1, d.Industry);
        Assert.Equal((byte)2, d.Vendor);
        Assert.Equal((byte)2, d.Type);
    }
}

public class AcClientTests
{
    [Fact]
    public async Task Client_sends_status_request_and_parses_reply_from_fake_device()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var c = await listener.AcceptTcpClientAsync();
            var s = c.GetStream();
            var buf = new byte[21];
            await s.ReadExactlyAsync(buf);
            Assert.Equal("AAAA12A00A0A00000000000000000000000000001A", Convert.ToHexString(buf));
            var reply = Convert.FromHexString("AAAA12000A0A000C0500D40000000019050000007D");
            await s.WriteAsync(reply.AsMemory(0, 7));
            await s.WriteAsync(reply.AsMemory(7));
        });

        await using var client = new AcClient("127.0.0.1", port);
        var state = await client.GetStatusAsync();
        await server;
        listener.Stop();
        Assert.Equal(21, state.SetpointCelsius);
    }
}
