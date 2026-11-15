namespace HomeEasy.Protocol;

public enum AcMode { Auto = 0, Cool = 1, Dry = 2, Fan = 3, Heat = 4 }

public enum FanSetting { Auto = 0, L1, L2, L3, L4, L5, L6, Quiet, Turbo }

public enum HorizontalSwing { Stop = 0, Swing = 1, Left = 2, LeftCenter = 3, Center = 4, RightCenter = 5, Right = 6, LeftRight = 12, SwingWide = 13 }

public enum VerticalSwing { Stop = 0, Swing = 1, Top = 2, TopCenter = 3, Center = 4, BottomCenter = 5, Bottom = 6 }

/// <summary>
/// 21-byte HomeEasy state frame. Payload byte N is at frame offset N+4; bit 0 is the MSB.
/// </summary>
public sealed class AcState
{
    public const int FrameLength = 21;
    private readonly byte[] _raw;

    public AcState(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != FrameLength || frame[0] != 0xAA || frame[1] != 0xAA)
            throw new ArgumentException($"Invalid frame: {Convert.ToHexString(frame)}", nameof(frame));
        _raw = frame.ToArray();
    }

    public ReadOnlySpan<byte> Raw => _raw;

    public AcState Clone() => new(_raw);

    public static byte Checksum(ReadOnlySpan<byte> frame)
    {
        int sum = 0;
        for (int i = 0; i < FrameLength - 1; i++) sum += frame[i];
        return (byte)sum;
    }

    public byte[] ToCommand()
    {
        var cmd = (byte[])_raw.Clone();
        cmd[3] = 0x01;
        cmd[^1] = Checksum(cmd);
        return cmd;
    }

    private int Get(int b, int bit, int count = 1)
    {
        int v = 0;
        for (int i = 0; i < count; i++)
        {
            int pos = (b + 4) * 8 + bit + i;
            v = (v << 1) | ((_raw[pos / 8] >> (7 - pos % 8)) & 1);
        }
        return v;
    }

    private void Set(int b, int bit, int count, int value)
    {
        for (int i = 0; i < count; i++)
        {
            int pos = (b + 4) * 8 + bit + i;
            byte mask = (byte)(1 << (7 - pos % 8));
            if (((value >> (count - 1 - i)) & 1) != 0) _raw[pos / 8] |= mask;
            else _raw[pos / 8] &= (byte)~mask;
        }
    }

    private void Set(int b, int bit, bool value) => Set(b, bit, 1, value ? 1 : 0);

    public bool Power { get => Get(3, 4) == 1; set => Set(3, 4, value); }
    public AcMode Mode { get => (AcMode)Get(3, 5, 3); set => Set(3, 5, 3, (int)value); }
    public int FanSpeed { get => Get(3, 1, 3); set => Set(3, 1, 3, Math.Clamp(value, 0, 6)); }
    public bool Turbo { get => Get(3, 0) == 1; set => Set(3, 0, value); }
    public bool Quiet { get => Get(4, 1) == 1; set => Set(4, 1, value); }
    public bool Fahrenheit => Get(4, 2) == 1;
    public HorizontalSwing HorizontalSwing { get => (HorizontalSwing)Get(5, 0, 4); set => Set(5, 0, 4, (int)value); }
    public VerticalSwing VerticalSwing { get => (VerticalSwing)Get(5, 4, 4); set => Set(5, 4, 4, (int)value); }
    public bool Display { get => Get(6, 0) == 1; set => Set(6, 0, value); }
    public bool Health { get => Get(6, 1) == 1; set => Set(6, 1, value); }
    /// <summary>Auxiliary heater in heat mode, drying in cool/dry mode.</summary>
    public bool Aux { get => Get(6, 3) == 1; set => Set(6, 3, value); }
    public bool Sleep { get => Get(6, 6) == 1; set => Set(6, 6, value); }
    public bool Eco { get => Get(6, 7) == 1; set => Set(6, 7, value); }
    public bool TimerFlag { get => Get(6, 2) == 1; set => Set(6, 2, value); }
    public bool OffTimerEnabled { get => Get(7, 0) == 1; set => Set(7, 0, value); }

    /// <summary>Minutes until the AC switches itself off; the AC counts this down once enabled.</summary>
    public int OffTimerMinutes
    {
        get => (Get(7, 1, 3) << 8) | Get(9, 0, 8);
        set
        {
            if (value is < 0 or > 1440) throw new ArgumentOutOfRangeException(nameof(value), "0..1440 minutes");
            Set(7, 1, 3, value >> 8);
            Set(9, 0, 8, value & 0xFF);
        }
    }

    /// <summary>Starts the AC's built-in off timer, or cancels it when <paramref name="minutes"/> is null.</summary>
    public void SetOffTimer(int? minutes)
    {
        OffTimerMinutes = minutes ?? 0;
        OffTimerEnabled = minutes is > 0;
        TimerFlag = minutes is > 0;
    }

    public int SetpointCelsius
    {
        get
        {
            int v = Get(4, 3, 5);
            if (v >= 16) v -= 16;
            return v + 16;
        }
        set
        {
            if (value is < 16 or > 30) throw new ArgumentOutOfRangeException(nameof(value), "16..30 °C");
            Set(4, 3, 5, value - 16);
        }
    }

    public double IndoorCelsius => Get(11, 0, 8) + Get(12, 0, 8) / 10.0;

    public FanSetting Fan
    {
        get => Turbo ? FanSetting.Turbo : Quiet ? FanSetting.Quiet : (FanSetting)FanSpeed;
        set
        {
            Turbo = value == FanSetting.Turbo;
            Quiet = value == FanSetting.Quiet;
            if (value is not (FanSetting.Turbo or FanSetting.Quiet)) FanSpeed = (int)value;
        }
    }

    public override string ToString() =>
        $"Power={(Power ? "On" : "Off")} Mode={Mode} Set={SetpointCelsius}°C Indoor={IndoorCelsius:0.0}°C Fan={Fan} " +
        $"H={HorizontalSwing} V={VerticalSwing} Eco={Eco} Sleep={Sleep} Display={Display} Health={Health} Aux={Aux}";
}
