namespace Mavka.Mobile;
// MimeKit cannot allocate an unbounded decoded attachment before checking its length.
internal sealed class BoundedWriteStream(int maximum) : MemoryStream
{
    public override void Write(byte[] buffer, int offset, int count)
    { if (Length + count > maximum) throw new IOException("Вкладення перевищує дозволений розмір."); base.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer)
    { if (Length + buffer.Length > maximum) throw new IOException("Вкладення перевищує дозволений розмір."); base.Write(buffer); }
    public override void WriteByte(byte value)
    { if (Length >= maximum) throw new IOException("Вкладення перевищує дозволений розмір."); base.WriteByte(value); }
}
