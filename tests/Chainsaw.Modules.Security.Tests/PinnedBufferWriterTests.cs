using System.Text.Json;

namespace Chainsaw.Modules.Security.Tests;

public sealed class PinnedBufferWriterTests
{
    [Fact]
    public void Json_larger_than_the_first_buffer_is_written_whole()
    {
        var text = new string('x', 100_000);
        using var buffer = new PinnedBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            JsonSerializer.Serialize(writer, new { Text = text });
        }

        Assert.Equal(text, JsonDocument.Parse(buffer.WrittenSpan.ToArray()).RootElement.GetProperty("Text").GetString());
    }

    [Fact]
    public void Dispose_clears_what_was_written()
    {
        var buffer = new PinnedBufferWriter();
        buffer.GetSpan(4)[..4].Fill(0xAB);
        buffer.Advance(4);
        var memory = buffer.GetMemory();

        buffer.Dispose();

        Assert.Equal(0, buffer.WrittenSpan.Length);
        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray<byte>(memory, out var segment));
        Assert.All(segment.Array!, value => Assert.Equal(0, value));
    }

    [Fact]
    public void Advancing_past_the_buffer_is_refused()
    {
        using var buffer = new PinnedBufferWriter();
        var room = buffer.GetSpan().Length;

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Advance(room + 1));
    }
}
