using System.Text;

namespace Trimix.Core.Connectors.Push;

/// <summary>
/// A small, dependency-free QR encoder covering exactly what the diver-mediated MySSI export needs:
/// <b>byte mode</b>, <b>error-correction level M</b>, <b>versions 1–10</b>, with full data+ECC block
/// interleaving, the eight standard data masks (lowest-penalty chosen per ISO/IEC 18004), and format/version
/// information. It is intentionally not a general-purpose library — it exists behind <see cref="IQrEncoder"/>
/// so a vetted QR NuGet can replace it later without any call-site change. The algorithm follows the public
/// ISO/IEC 18004 reference (finder/timing/alignment placement, Reed–Solomon over GF(256) with primitive
/// 0x11D, BCH format/version bits, zig-zag codeword placement, penalty-scored masking).
///
/// <para>The SSI payload (<see cref="SsiQrPayloadBuilder"/>) is a short ASCII blob, so version 1–3 is the
/// normal case; the 1–10 range leaves generous headroom. A payload too large for version 10-M throws.</para>
/// </summary>
public sealed class BasicQrEncoder : IQrEncoder
{
    private const int MinVersion = 1;
    private const int MaxVersion = 10;

    // ── Per-version ECC-M characteristics (ISO/IEC 18004, versions 1–10) ─────────────────────────────
    // Total data codewords available to the payload (before ECC), indexed [version-1].
    private static readonly int[] DataCodewordsM =
        [16, 28, 44, 64, 86, 108, 124, 154, 182, 216];

    // ECC codewords per block, indexed [version-1].
    private static readonly int[] EccPerBlockM =
        [10, 16, 26, 18, 24, 16, 18, 22, 22, 26];

    // Block layout per version as (blockCount, dataCodewordsPerBlock) groups.
    private static readonly (int Count, int DataWords)[][] BlocksM =
    [
        [(1, 16)],
        [(1, 28)],
        [(1, 44)],
        [(2, 32)],
        [(2, 43)],
        [(4, 27)],
        [(4, 31)],
        [(2, 38), (2, 39)],
        [(3, 36), (2, 37)],
        [(4, 43), (1, 44)],
    ];

    // Alignment-pattern centre coordinates per version (empty for version 1).
    private static readonly int[][] AlignPositions =
    [
        [],
        [6, 18],
        [6, 22],
        [6, 26],
        [6, 30],
        [6, 34],
        [6, 22, 38],
        [6, 24, 42],
        [6, 26, 46],
        [6, 28, 50],
    ];

    // GF(256) log / antilog tables (primitive polynomial 0x11D, generator 2).
    private static readonly byte[] GfExp = new byte[512];
    private static readonly byte[] GfLog = new byte[256];

    static BasicQrEncoder()
    {
        var x = 1;
        for (var i = 0; i < 255; i++)
        {
            GfExp[i] = (byte)x;
            GfLog[x] = (byte)i;
            x <<= 1;
            if ((x & 0x100) != 0)
            {
                x ^= 0x11D;
            }
        }

        for (var i = 255; i < 512; i++)
        {
            GfExp[i] = GfExp[i - 255];
        }
    }

    public QrMatrix Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Byte mode uses the payload's raw bytes. The SSI blob is ASCII; UTF-8 is byte-identical for it and is
        // the de-facto reader default for any incidental non-ASCII.
        var data = Encoding.UTF8.GetBytes(text);
        var version = ChooseVersion(data.Length);

        var codewords = BuildCodewords(data, version);
        var interleaved = InterleaveWithEcc(codewords, version);

        return new Symbol(version).Render(interleaved);
    }

    /// <summary>Smallest 1–10 version whose ECC-M byte-mode data capacity holds the payload; throws if none.</summary>
    private static int ChooseVersion(int byteCount)
    {
        for (var v = MinVersion; v <= MaxVersion; v++)
        {
            var countBits = CharCountBits(v);
            var neededBits = 4 + countBits + (byteCount * 8);
            if (neededBits <= DataCodewordsM[v - 1] * 8)
            {
                return v;
            }
        }

        throw new ArgumentException(
            $"Payload of {byteCount} bytes exceeds the encoder's version-10 / EC-M capacity.", nameof(byteCount));
    }

    /// <summary>Byte-mode character-count indicator width: 8 bits for versions 1–9, 16 bits for 10+.</summary>
    private static int CharCountBits(int version) => version <= 9 ? 8 : 16;

    /// <summary>Builds the padded data-codeword stream (mode + count + bytes + terminator + pad bytes).</summary>
    private static byte[] BuildCodewords(byte[] data, int version)
    {
        var capacityWords = DataCodewordsM[version - 1];
        var bits = new BitBuffer();

        bits.Append(0b0100, 4);                          // byte-mode indicator
        bits.Append(data.Length, CharCountBits(version)); // character count
        foreach (var b in data)
        {
            bits.Append(b, 8);
        }

        // Terminator: up to four 0 bits, fewer if we are already at capacity.
        var capacityBits = capacityWords * 8;
        bits.Append(0, Math.Min(4, capacityBits - bits.Length));

        // Pad to a byte boundary, then fill with the standard alternating pad bytes.
        bits.Append(0, (8 - (bits.Length % 8)) % 8);
        var padToggle = true;
        while (bits.Length < capacityBits)
        {
            bits.Append(padToggle ? 0xEC : 0x11, 8);
            padToggle = !padToggle;
        }

        return bits.ToBytes();
    }

    /// <summary>Splits data into blocks, computes Reed–Solomon ECC per block, and interleaves per the spec.</summary>
    private static byte[] InterleaveWithEcc(byte[] data, int version)
    {
        var ecLen = EccPerBlockM[version - 1];
        var layout = BlocksM[version - 1];

        var dataBlocks = new List<byte[]>();
        var ecBlocks = new List<byte[]>();
        var offset = 0;
        foreach (var (count, words) in layout)
        {
            for (var i = 0; i < count; i++)
            {
                var block = data[offset..(offset + words)];
                offset += words;
                dataBlocks.Add(block);
                ecBlocks.Add(ReedSolomon(block, ecLen));
            }
        }

        var result = new List<byte>();

        // Interleave data codewords column-by-column across blocks (short blocks contribute fewer columns).
        var maxData = dataBlocks.Max(b => b.Length);
        for (var col = 0; col < maxData; col++)
        {
            foreach (var block in dataBlocks)
            {
                if (col < block.Length)
                {
                    result.Add(block[col]);
                }
            }
        }

        // Then interleave the (uniform-length) ECC codewords.
        for (var col = 0; col < ecLen; col++)
        {
            foreach (var block in ecBlocks)
            {
                result.Add(block[col]);
            }
        }

        return [.. result];
    }

    /// <summary>Reed–Solomon error-correction codewords for one data block (GF(256), primitive 0x11D).</summary>
    private static byte[] ReedSolomon(byte[] data, int ecLen)
    {
        var generator = GeneratorPolynomial(ecLen);
        var residual = new byte[ecLen];

        foreach (var b in data)
        {
            var factor = (byte)(b ^ residual[0]);
            Array.Copy(residual, 1, residual, 0, ecLen - 1);
            residual[ecLen - 1] = 0;
            for (var i = 0; i < ecLen; i++)
            {
                residual[i] ^= GfMul(generator[i], factor);
            }
        }

        return residual;
    }

    /// <summary>Generator polynomial coefficients for a degree-<paramref name="ecLen"/> RS code.</summary>
    private static byte[] GeneratorPolynomial(int ecLen)
    {
        var poly = new byte[ecLen];
        poly[ecLen - 1] = 1;
        var root = 1;
        for (var i = 0; i < ecLen; i++)
        {
            for (var j = 0; j < ecLen; j++)
            {
                poly[j] = GfMul(poly[j], (byte)root);
                if (j + 1 < ecLen)
                {
                    poly[j] ^= poly[j + 1];
                }
            }

            root = GfExp[GfLog[(byte)root] + 1];
        }

        return poly;
    }

    private static byte GfMul(byte a, byte b) =>
        a == 0 || b == 0 ? (byte)0 : GfExp[GfLog[a] + GfLog[b]];

    /// <summary>Grows a bit stream MSB-first and packs it into codeword bytes.</summary>
    private sealed class BitBuffer
    {
        private readonly List<bool> _bits = [];

        public int Length => _bits.Count;

        public void Append(int value, int bitCount)
        {
            for (var i = bitCount - 1; i >= 0; i--)
            {
                _bits.Add(((value >> i) & 1) != 0);
            }
        }

        public byte[] ToBytes()
        {
            var bytes = new byte[_bits.Count / 8];
            for (var i = 0; i < _bits.Count; i++)
            {
                if (_bits[i])
                {
                    bytes[i / 8] |= (byte)(1 << (7 - (i % 8)));
                }
            }

            return bytes;
        }
    }

    /// <summary>
    /// Builds the module grid for one version: function patterns, data placement, penalty-scored masking, and
    /// format/version information. Coordinates are <c>(x, y)</c> with <c>_modules[y, x]</c> row-major.
    /// </summary>
    private sealed class Symbol(int version)
    {
        private const int EcLevelFormatBits = 0; // level M
        private readonly int _size = (version * 4) + 17;
        private bool[,] _modules = null!;
        private bool[,] _function = null!;

        public QrMatrix Render(byte[] codewords)
        {
            _modules = new bool[_size, _size];
            _function = new bool[_size, _size];

            DrawFunctionPatterns();
            DrawCodewords(codewords);

            // Pick the lowest-penalty mask (ISO/IEC 18004 §7.8.3): apply, score, un-apply.
            var bestMask = 0;
            var bestPenalty = int.MaxValue;
            for (var mask = 0; mask < 8; mask++)
            {
                ApplyMask(mask);
                DrawFormatBits(mask);
                var penalty = PenaltyScore();
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    bestMask = mask;
                }

                ApplyMask(mask); // XOR is self-inverse — restore.
            }

            ApplyMask(bestMask);
            DrawFormatBits(bestMask);

            return new QrMatrix(_size, _modules);
        }

        private void SetFunction(int x, int y, bool dark)
        {
            _modules[y, x] = dark;
            _function[y, x] = true;
        }

        private void DrawFunctionPatterns()
        {
            // Timing patterns (row 6 and column 6).
            for (var i = 0; i < _size; i++)
            {
                SetFunction(6, i, i % 2 == 0);
                SetFunction(i, 6, i % 2 == 0);
            }

            // Three finder patterns + their separators (centres at the three non-BR corners).
            DrawFinder(3, 3);
            DrawFinder(_size - 4, 3);
            DrawFinder(3, _size - 4);

            // Alignment patterns at every centre-pair except where they collide with a finder.
            var positions = AlignPositions[version - 1];
            var last = positions.Length - 1;
            for (var i = 0; i < positions.Length; i++)
            {
                for (var j = 0; j < positions.Length; j++)
                {
                    var onFinder = (i == 0 && j == 0) || (i == 0 && j == last) || (i == last && j == 0);
                    if (!onFinder)
                    {
                        DrawAlignment(positions[i], positions[j]);
                    }
                }
            }

            // Reserve format (dummy) + version areas so data placement skips them.
            DrawFormatBits(0);
            DrawVersionBits();
        }

        private void DrawFinder(int cx, int cy)
        {
            for (var dy = -4; dy <= 4; dy++)
            {
                for (var dx = -4; dx <= 4; dx++)
                {
                    var x = cx + dx;
                    var y = cy + dy;
                    if (x < 0 || y < 0 || x >= _size || y >= _size)
                    {
                        continue;
                    }

                    var dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    SetFunction(x, y, dist != 2 && dist != 4);
                }
            }
        }

        private void DrawAlignment(int cx, int cy)
        {
            for (var dy = -2; dy <= 2; dy++)
            {
                for (var dx = -2; dx <= 2; dx++)
                {
                    SetFunction(cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            }
        }

        /// <summary>Draws both copies of the 15-bit BCH format information for level M + <paramref name="mask"/>.</summary>
        private void DrawFormatBits(int mask)
        {
            var data = (EcLevelFormatBits << 3) | mask; // 5 bits
            var rem = data;
            for (var i = 0; i < 10; i++)
            {
                rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            }

            var bits = ((data << 10) | rem) ^ 0x5412; // 15 bits

            // Copy 1 — around the top-left finder.
            for (var i = 0; i <= 5; i++)
            {
                SetFunction(8, i, GetBit(bits, i));
            }

            SetFunction(8, 7, GetBit(bits, 6));
            SetFunction(8, 8, GetBit(bits, 7));
            SetFunction(7, 8, GetBit(bits, 8));
            for (var i = 9; i < 15; i++)
            {
                SetFunction(14 - i, 8, GetBit(bits, i));
            }

            // Copy 2 — split along the top-right and bottom-left finders.
            for (var i = 0; i < 8; i++)
            {
                SetFunction(_size - 1 - i, 8, GetBit(bits, i));
            }

            for (var i = 8; i < 15; i++)
            {
                SetFunction(8, _size - 15 + i, GetBit(bits, i));
            }

            SetFunction(8, _size - 8, true); // the always-dark module
        }

        /// <summary>Draws the two 18-bit version-information blocks (versions 7+ only).</summary>
        private void DrawVersionBits()
        {
            if (version < 7)
            {
                return;
            }

            var rem = version;
            for (var i = 0; i < 12; i++)
            {
                rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            }

            var bits = (version << 12) | rem; // 18 bits

            for (var i = 0; i < 18; i++)
            {
                var bit = GetBit(bits, i);
                var a = _size - 11 + (i % 3);
                var b = i / 3;
                SetFunction(a, b, bit);
                SetFunction(b, a, bit);
            }
        }

        /// <summary>Places the interleaved codeword bitstream in the boustrophedon (zig-zag) order.</summary>
        private void DrawCodewords(byte[] data)
        {
            var bit = 0;
            var totalBits = data.Length * 8;

            for (var right = _size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                {
                    right = 5; // the vertical timing column is skipped
                }

                for (var vert = 0; vert < _size; vert++)
                {
                    for (var j = 0; j < 2; j++)
                    {
                        var x = right - j;
                        var upward = ((right + 1) & 2) == 0;
                        var y = upward ? _size - 1 - vert : vert;
                        if (!_function[y, x] && bit < totalBits)
                        {
                            _modules[y, x] = ((data[bit >> 3] >> (7 - (bit & 7))) & 1) != 0;
                            bit++;
                        }
                    }
                }
            }
        }

        private void ApplyMask(int mask)
        {
            for (var y = 0; y < _size; y++)
            {
                for (var x = 0; x < _size; x++)
                {
                    if (_function[y, x])
                    {
                        continue;
                    }

                    var invert = mask switch
                    {
                        0 => (x + y) % 2 == 0,
                        1 => y % 2 == 0,
                        2 => x % 3 == 0,
                        3 => (x + y) % 3 == 0,
                        4 => ((x / 3) + (y / 2)) % 2 == 0,
                        5 => (x * y % 2) + (x * y % 3) == 0,
                        6 => (((x * y) % 2) + ((x * y) % 3)) % 2 == 0,
                        7 => ((((x + y) % 2) + ((x * y) % 3)) % 2) == 0,
                        _ => false,
                    };

                    if (invert)
                    {
                        _modules[y, x] = !_modules[y, x];
                    }
                }
            }
        }

        // Penalty constants (ISO/IEC 18004 §7.8.3.1).
        private const int N1 = 3;
        private const int N2 = 3;
        private const int N3 = 40;
        private const int N4 = 10;

        private int PenaltyScore()
        {
            var score = 0;

            // Rule 1 — runs of ≥5 same-colour modules in each row and column.
            for (var y = 0; y < _size; y++)
            {
                score += LineRunPenalty(x => _modules[y, x]);
            }

            for (var x = 0; x < _size; x++)
            {
                score += LineRunPenalty(y => _modules[y, x]);
            }

            // Rule 2 — every 2×2 block of a single colour.
            for (var y = 0; y < _size - 1; y++)
            {
                for (var x = 0; x < _size - 1; x++)
                {
                    var c = _modules[y, x];
                    if (c == _modules[y, x + 1] && c == _modules[y + 1, x] && c == _modules[y + 1, x + 1])
                    {
                        score += N2;
                    }
                }
            }

            // Rule 3 — finder-like 1:1:3:1:1 patterns (with 4 light modules) in rows and columns.
            for (var y = 0; y < _size; y++)
            {
                for (var x = 0; x < _size; x++)
                {
                    if (x + 6 < _size && HasFinderRun(dx => _modules[y, x + dx]))
                    {
                        score += N3;
                    }

                    if (y + 6 < _size && HasFinderRun(dy => _modules[y + dy, x]))
                    {
                        score += N3;
                    }
                }
            }

            // Rule 4 — deviation of the dark-module proportion from 50%.
            var dark = 0;
            foreach (var m in _modules)
            {
                if (m)
                {
                    dark++;
                }
            }

            var total = _size * _size;
            var percent = dark * 100 / total;
            var deviation = Math.Abs(percent - 50);
            score += (deviation + 4) / 5 * N4;

            return score;
        }

        private int LineRunPenalty(Func<int, bool> at)
        {
            var score = 0;
            var runColor = at(0);
            var runLength = 1;
            for (var i = 1; i < _size; i++)
            {
                var c = at(i);
                if (c == runColor)
                {
                    runLength++;
                    if (runLength == 5)
                    {
                        score += N1;
                    }
                    else if (runLength > 5)
                    {
                        score++;
                    }
                }
                else
                {
                    runColor = c;
                    runLength = 1;
                }
            }

            return score;
        }

        // The finder-like sequence is dark:light:dark:dark:dark:light:dark (1:1:3:1:1).
        private static readonly bool[] FinderRun = [true, false, true, true, true, false, true];

        private static bool HasFinderRun(Func<int, bool> at)
        {
            for (var i = 0; i < 7; i++)
            {
                if (at(i) != FinderRun[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool GetBit(int value, int index) => ((value >> index) & 1) != 0;
    }
}
