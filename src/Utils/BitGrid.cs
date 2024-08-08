using System.Collections;
using IVec2 = RWCustom.IntVector2;

namespace JumpSlug;

/// <summary>
/// A two dimensional packed array of bits.
/// </summary>
public class BitGrid {
    private readonly BitArray _array;

    public int Width { get; }
    public int Height { get; }

    public BitGrid(int width, int height) {
        Width = width;
        Height = height;
        _array = new BitArray(width * height);
    }

    public bool this[int x, int y] {
        get => _array[y * Width + x];
        set {
            _array[y * Width + x] = value;
        }
    }

    public bool this[IVec2 pos] {
        get => _array[pos.y * Width + pos.x];
        set {
            _array[pos.y * Width + pos.x] = value;
        }
    }

    /// <summary>
    /// set every bit to zero.
    /// </summary>
    public void Reset() {
        _array.SetAll(false);
    }
}