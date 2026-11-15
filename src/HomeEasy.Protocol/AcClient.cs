using System.Net.Sockets;

namespace HomeEasy.Protocol;

/// <summary>Direct LAN connection to the AC on TCP 12416. Thread-safe; one request at a time.</summary>
public sealed class AcClient : IAsyncDisposable
{
    public const int ControlPort = 12416;
    private static readonly byte[] StatusRequest = Convert.FromHexString("AAAA12A00A0A00000000000000000000000000001A");

    private readonly string _host;
    private readonly int _port;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private readonly List<byte> _buffer = new();

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    public AcState? LastState { get; private set; }

    public AcClient(string host, int port = ControlPort)
    {
        _host = host;
        _port = port;
    }

    public Task<AcState> GetStatusAsync(CancellationToken ct = default) => RequestAsync(StatusRequest, ct);

    public Task<AcState> SendAsync(AcState desired, CancellationToken ct = default) => RequestAsync(desired.ToCommand(), ct);

    /// <summary>Reads current state, applies <paramref name="change"/>, sends it and returns the new state.</summary>
    public async Task<AcState> UpdateAsync(Action<AcState> change, CancellationToken ct = default)
    {
        var state = (await GetStatusAsync(ct).ConfigureAwait(false)).Clone();
        change(state);
        return await SendAsync(state, ct).ConfigureAwait(false);
    }

    private async Task<AcState> RequestAsync(byte[] frame, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await EnsureConnectedAsync(ct).ConfigureAwait(false);
                    _buffer.Clear();
                    await _stream!.WriteAsync(frame, ct).ConfigureAwait(false);
                    LastState = await ReadFrameAsync(ct).ConfigureAwait(false);
                    return LastState;
                }
                catch (Exception ex) when (attempt < 3 && ex is IOException or SocketException or TimeoutException)
                {
                    Reset();
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_tcp is { Connected: true }) return;
        Reset();
        _tcp = new TcpClient { NoDelay = true };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        try { await _tcp.ConnectAsync(_host, _port, cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException($"Connect to {_host}:{_port} timed out"); }
        _stream = _tcp.GetStream();
    }

    private async Task<AcState> ReadFrameAsync(CancellationToken ct)
    {
        var chunk = new byte[256];
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        while (true)
        {
            int start = IndexOfHeader();
            if (start >= 0 && _buffer.Count - start >= AcState.FrameLength)
                return new AcState(_buffer.GetRange(start, AcState.FrameLength).ToArray());

            int n;
            try { n = await _stream!.ReadAsync(chunk, cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("AC did not reply"); }
            if (n == 0) throw new IOException("Connection closed by AC");
            _buffer.AddRange(chunk.AsSpan(0, n).ToArray());
        }
    }

    private int IndexOfHeader()
    {
        for (int i = 0; i + 2 < _buffer.Count; i++)
            if (_buffer[i] == 0xAA && _buffer[i + 1] == 0xAA && _buffer[i + 2] == 0x12) return i;
        return -1;
    }

    private void Reset()
    {
        _stream?.Dispose();
        _tcp?.Dispose();
        _stream = null;
        _tcp = null;
    }

    public ValueTask DisposeAsync()
    {
        Reset();
        _lock.Dispose();
        return ValueTask.CompletedTask;
    }
}
