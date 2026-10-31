using System.Globalization;
using System.Text;

namespace TutorManagement.Modules.Finance.Domain.Tuition;

/// <summary>The EMVCo text of a NAPAS VietQR code with the amount and transfer content already filled in.</summary>
public static class VietQr
{
    private const string NapasGuid = "A000000727";
    private const string AccountTransferService = "QRIBFTTA";
    private const int MaxContentLength = 25;

    public static string Build(string bankBin, string accountNumber, long amount, string content)
    {
        if (amount <= 0 || content.Length is 0 or > MaxContentLength || !content.All(char.IsAscii))
            throw new ArgumentException("The amount or transfer content cannot be carried by a VietQR code.");
        var beneficiary = Field("00", bankBin) + Field("01", accountNumber);
        var merchant = Field("00", NapasGuid) + Field("01", beneficiary) + Field("02", AccountTransferService);
        var payload = new StringBuilder()
            .Append(Field("00", "01"))
            // 12: dynamic code, the banking app takes the amount from it.
            .Append(Field("01", "12"))
            .Append(Field("38", merchant))
            .Append(Field("53", "704"))
            .Append(Field("54", amount.ToString(CultureInfo.InvariantCulture)))
            .Append(Field("58", "VN"))
            .Append(Field("62", Field("08", content)))
            .Append("6304")
            .ToString();
        return payload + Crc16(payload).ToString("X4", CultureInfo.InvariantCulture);
    }

    /// <summary>CRC-16/CCITT-FALSE, the checksum EMVCo requires over everything up to and including "6304".</summary>
    public static ushort Crc16(string text)
    {
        ushort crc = 0xFFFF;
        foreach (var value in Encoding.ASCII.GetBytes(text))
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }

    private static string Field(string id, string value) =>
        string.Create(CultureInfo.InvariantCulture, $"{id}{value.Length:00}{value}");
}
