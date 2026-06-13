// Drop-in replacement for HslCommunication.ModBus.ModbusTcpNet.
// Implements Modbus TCP FC=0x03 (Read Holding Registers) over raw TcpClient.
// Namespace and class names are intentionally identical so Form1.cs needs no changes.

using System.Net.Sockets;

namespace HslCommunication.ModBus;

public class OperateResult
{
    public bool   IsSuccess { get; init; }
    public string Message   { get; init; } = "";

    public static OperateResult CreateSuccessResult() =>
        new() { IsSuccess = true };

    public static OperateResult CreateFailedResult(string msg) =>
        new() { IsSuccess = false, Message = msg };
}

public class OperateResult<T> : OperateResult
{
    public T? Content { get; init; }

    public static OperateResult<T> CreateSuccessResult(T content) =>
        new() { IsSuccess = true, Content = content };

    public new static OperateResult<T> CreateFailedResult(string msg) =>
        new() { IsSuccess = false, Message = msg };
}

public sealed class ModbusTcpNet : IDisposable
{
    private readonly string _host;
    private readonly int    _port;
    private TcpClient?     _tcp;
    private NetworkStream? _stream;
    private ushort         _txId;

    public ModbusTcpNet(string host, int port = 502)
    {
        _host = host;
        _port = port;
    }

    // Connect to the Modbus TCP server.
    public OperateResult ConnectServer()
    {
        try
        {
            CloseConnection();
            _tcp = new TcpClient { ReceiveTimeout = 2000, SendTimeout = 2000 };
            _tcp.Connect(_host, _port);
            _stream = _tcp.GetStream();
            return OperateResult.CreateSuccessResult();
        }
        catch (Exception ex)
        {
            return OperateResult.CreateFailedResult(ex.Message);
        }
    }

    // Read `count` holding registers starting at the address parsed from `address`.
    // "1" maps to Modbus protocol address 0x0001 (register index 1).
    public OperateResult<ushort[]> ReadUInt16(string address, ushort count)
    {
        if (_stream == null)
            return OperateResult<ushort[]>.CreateFailedResult("Not connected");

        try
        {
            ushort startAddr = ushort.Parse(address);
            _txId++;

            // Build Modbus TCP request (12 bytes)
            byte[] req =
            [
                (byte)(_txId >> 8), (byte)(_txId & 0xFF),  // Transaction ID
                0x00, 0x00,                                  // Protocol ID
                0x00, 0x06,                                  // Length (6 bytes of PDU follow)
                0x01,                                        // Unit ID
                0x03,                                        // FC 0x03: Read Holding Registers
                (byte)(startAddr >> 8), (byte)(startAddr & 0xFF),  // Starting Address
                (byte)(count >> 8),     (byte)(count & 0xFF),      // Quantity
            ];

            _stream.Write(req, 0, req.Length);

            // Response: MBAP(7) + ByteCount(1) + Data(count*2)
            int  expectedLen = 9 + count * 2;
            byte[] resp      = new byte[expectedLen];
            int  received    = 0;

            while (received < expectedLen)
            {
                int n = _stream.Read(resp, received, expectedLen - received);
                if (n == 0) throw new IOException("Connection closed by server");
                received += n;
            }

            // Error response has high bit set in FC byte (e.g., 0x83)
            if ((resp[7] & 0x80) != 0)
                return OperateResult<ushort[]>.CreateFailedResult(
                    $"Modbus exception code: 0x{resp[8]:X2}");

            if (resp[7] != 0x03)
                return OperateResult<ushort[]>.CreateFailedResult(
                    $"Unexpected function code: 0x{resp[7]:X2}");

            var registers = new ushort[count];
            for (int i = 0; i < count; i++)
                registers[i] = (ushort)((resp[9 + i * 2] << 8) | resp[10 + i * 2]);

            return OperateResult<ushort[]>.CreateSuccessResult(registers);
        }
        catch (Exception ex)
        {
            CloseConnection();
            return OperateResult<ushort[]>.CreateFailedResult(ex.Message);
        }
    }

    private void CloseConnection()
    {
        _stream?.Dispose();
        _tcp?.Dispose();
        _stream = null;
        _tcp    = null;
    }

    public void Dispose() => CloseConnection();
}
