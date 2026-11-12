using Mavka.Mobile;
using QRCoder;

if (args.Length == 3 && args[0] == "invite")
{
    // Keep invitation JSON private on the desktop until it expires.
    if (File.Exists(args[2]) || File.Exists(args[2] + ".json"))
        throw new IOException("Не перезаписуй чинне QR-запрошення.");
    string json = QrPairingProtocol.Generate(args[1], DateTimeOffset.UtcNow);
    File.WriteAllText(args[2] + ".json", json);
    using var generator = new QRCodeGenerator();
    using var qrData = generator.CreateQrCode(json, QRCodeGenerator.ECCLevel.Q);
    var png = new PngByteQRCode(qrData).GetGraphic(8);
    File.WriteAllBytes(args[2], png);
    Console.WriteLine("QR дійсний 10 хвилин. Показуй його лише біля ГОЛОВНОЇ МАВКИ.");
    Console.WriteLine("Ключ запрошення зберігається у " + args[2] + ".json");
}
else if (args.Length == 4 && args[0] == "ack")
{
    var invitation = QrPairingProtocol.ParseInvitation(
        File.ReadAllText(args[1]), DateTimeOffset.UtcNow);
    var reply = QrPairingProtocol.CreateAck(invitation, args[2]);
    if (File.Exists(args[3])) throw new IOException("Файл відповіді вже існує.");
    File.WriteAllText(args[3], reply);
    Console.WriteLine("Надішли текст JSON у звичайному листі з пошти ГОЛОВНОЇ МАВКИ " +
        "на адресу мобільного користувача. Не відправляй секрет QR.");
}
else
{
    Console.WriteLine("dotnet run --project tools/QrPairingTool -- invite MAIN@GMAIL.COM invitation.png");
    Console.WriteLine("dotnet run --project tools/QrPairingTool -- ack invitation.png.json CLIENT_ID reply.json");
    Environment.ExitCode = 2;
}
