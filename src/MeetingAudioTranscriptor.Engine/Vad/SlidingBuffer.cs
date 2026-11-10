// A growable window over a stream of floats addressed by absolute position, so the chunker holds only the part it still needs.
using System;

namespace MeetingAudioTranscriptor.Engine.Vad;

/// <summary>Holds the items of a stream from <see cref="Start"/> to <see cref="End"/>, appending at the end and discarding at the start.</summary>
/// <remarks>
/// Discarding moves an offset, and the held items are moved to the front of the array only when the
/// array would otherwise grow, so a long stream costs one copy per array-length of items rather
/// than one per discard.
/// </remarks>
internal sealed class SlidingBuffer
{
    private float[] _items;
    private int _head;
    private int _count;

    public SlidingBuffer(int initialCapacity)
    {
        _items = new float[Math.Max(16, initialCapacity)];
    }

    /// <summary>The absolute position of the first item held.</summary>
    public long Start { get; private set; }

    /// <summary>The absolute position just after the last item appended.</summary>
    public long End => Start + _count;

    /// <summary>The items held.</summary>
    public int Count => _count;

    /// <summary>The item at an absolute position, which must be held.</summary>
    public float this[long position] => _items[_head + checked((int)(position - Start))];

    /// <summary>Appends items at the end.</summary>
    public void Append(ReadOnlySpan<float> items)
    {
        EnsureRoom(items.Length);
        items.CopyTo(_items.AsSpan(_head + _count));
        _count += items.Length;
    }

    /// <summary>Appends one item at the end.</summary>
    public void Append(float item)
    {
        EnsureRoom(1);
        _items[_head + _count] = item;
        _count++;
    }

    /// <summary>Forgets every item before an absolute position; a position outside the held range is clamped.</summary>
    public void DiscardBefore(long position)
    {
        long clamped = Math.Clamp(position, Start, End);
        int drop = (int)(clamped - Start);
        _head += drop;
        _count -= drop;
        Start = clamped;
    }

    /// <summary>The held items from <paramref name="from"/> to <paramref name="to"/>, as absolute positions.</summary>
    public ReadOnlySpan<float> Slice(long from, long to)
    {
        if (from < Start || to > End || from > to)
        {
            throw new ArgumentOutOfRangeException(nameof(from), $"the range [{from}, {to}) is outside the held items [{Start}, {End}).");
        }

        return _items.AsSpan(_head + (int)(from - Start), (int)(to - from));
    }

    private void EnsureRoom(int extra)
    {
        if (_head + _count + extra <= _items.Length)
        {
            return;
        }

        float[] target = _count + extra <= _items.Length / 2 ? _items : new float[Math.Max(_items.Length * 2, _count + extra)];
        Array.Copy(_items, _head, target, 0, _count);
        _items = target;
        _head = 0;
    }
}
