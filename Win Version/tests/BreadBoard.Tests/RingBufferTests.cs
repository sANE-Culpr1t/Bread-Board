using BreadBoard.Audio.Dsp;

namespace BreadBoard.Tests;

public class RingBufferTests
{
    [Fact]
    public void Wraps_and_preserves_order()
    {
        var rb = new FloatRingBuffer(8);
        var outBuf = new float[8];
        for (var round = 0; round < 5; round++)
        {
            Assert.Equal(6, rb.Write([1, 2, 3, 4, 5, 6]));
            Assert.Equal(6, rb.Read(outBuf));
            Assert.Equal([1f, 2, 3, 4, 5, 6], outBuf[..6]);
        }
    }

    [Fact]
    public void Drops_excess_when_full_and_skip_discards()
    {
        var rb = new FloatRingBuffer(4);
        Assert.Equal(4, rb.Write([1, 2, 3, 4, 5]));
        rb.Skip(2);
        var o = new float[4];
        Assert.Equal(2, rb.Read(o));
        Assert.Equal([3f, 4], o[..2]);
    }
}
