namespace CoinsPayRust
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// A QR code of a text in byte mode, ported from Project Nayuki's QR Code generator
    /// (https://www.nayuki.io/page/qr-code-generator-library, MIT License, Copyright (c) Project Nayuki).
    /// Only what the payment window needs: byte mode, the smallest version, the best mask.
    /// </summary>
    public sealed class Qr
    {
        public enum Ecc { Low = 0, Medium = 1, Quartile = 2, High = 3 }

        private static readonly int[] FormatBits = { 1, 0, 3, 2 };

        private static readonly sbyte[,] EccCodewordsPerBlock =
        {
            { -1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
            { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 },
            { -1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
            { -1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
        };

        private static readonly sbyte[,] NumErrorCorrectionBlocks =
        {
            { -1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25 },
            { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 },
            { -1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68 },
            { -1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81 },
        };

        public readonly int Version;
        public readonly int Size;
        private readonly int ecl;
        private readonly bool[,] modules;
        private bool[,] isFunction;

        /// <summary>True for a dark module; x is the column, y the row, both from the top left.</summary>
        public bool this[int x, int y] => modules[y, x];

        /// <summary>The smallest code for the text at the given level or better (up to High while it fits).</summary>
        public static Qr Encode(string text, Ecc minimum = Ecc.Medium)
        {
            byte[] data = Encoding.UTF8.GetBytes(text ?? "");
            int version;
            int usedBits = 0;
            int level = (int)minimum;
            for (version = 1; ; version++)
            {
                usedBits = 4 + CharCountBits(version) + data.Length * 8;
                if (usedBits <= NumDataCodewords(version, level) * 8) break;
                if (version >= 40) throw new ArgumentException("the text is too long for a QR code");
            }
            for (int better = level + 1; better <= (int)Ecc.High; better++)
            {
                if (usedBits <= NumDataCodewords(version, better) * 8) level = better;
            }

            var bits = new List<bool>();
            AppendBits(bits, 0x4, 4);
            AppendBits(bits, data.Length, CharCountBits(version));
            foreach (byte b in data) AppendBits(bits, b, 8);
            int capacity = NumDataCodewords(version, level) * 8;
            AppendBits(bits, 0, Math.Min(4, capacity - bits.Count));
            AppendBits(bits, 0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) AppendBits(bits, pad, 8);

            var codewords = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));
            return new Qr(version, level, codewords);
        }

        private Qr(int version, int level, byte[] dataCodewords)
        {
            Version = version;
            Size = version * 4 + 17;
            ecl = level;
            modules = new bool[Size, Size];
            isFunction = new bool[Size, Size];
            DrawFunctionPatterns();
            DrawCodewords(AddEccAndInterleave(dataCodewords));

            int best = 0;
            int minPenalty = int.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                ApplyMask(mask);
                DrawFormatBits(mask);
                int penalty = PenaltyScore();
                if (penalty < minPenalty)
                {
                    best = mask;
                    minPenalty = penalty;
                }
                ApplyMask(mask);
            }
            ApplyMask(best);
            DrawFormatBits(best);
            isFunction = null;
        }

        private static int CharCountBits(int version) => version <= 9 ? 8 : 16;

        private static void AppendBits(List<bool> bits, int value, int length)
        {
            for (int i = length - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
        }

        private void DrawFunctionPatterns()
        {
            for (int i = 0; i < Size; i++)
            {
                SetFunction(6, i, i % 2 == 0);
                SetFunction(i, 6, i % 2 == 0);
            }
            DrawFinder(3, 3);
            DrawFinder(Size - 4, 3);
            DrawFinder(3, Size - 4);
            int[] align = AlignmentPositions();
            int n = align.Length;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (!(i == 0 && j == 0 || i == 0 && j == n - 1 || i == n - 1 && j == 0)) DrawAlignment(align[i], align[j]);
                }
            }
            DrawFormatBits(0);
            DrawVersion();
        }

        private void DrawFormatBits(int mask)
        {
            int data = FormatBits[ecl] << 3 | mask;
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;
            for (int i = 0; i <= 5; i++) SetFunction(8, i, Bit(bits, i));
            SetFunction(8, 7, Bit(bits, 6));
            SetFunction(8, 8, Bit(bits, 7));
            SetFunction(7, 8, Bit(bits, 8));
            for (int i = 9; i < 15; i++) SetFunction(14 - i, 8, Bit(bits, i));
            for (int i = 0; i < 8; i++) SetFunction(Size - 1 - i, 8, Bit(bits, i));
            for (int i = 8; i < 15; i++) SetFunction(8, Size - 15 + i, Bit(bits, i));
            SetFunction(8, Size - 8, true);
        }

        private void DrawVersion()
        {
            if (Version < 7) return;
            int rem = Version;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = Version << 12 | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = Bit(bits, i);
                int a = Size - 11 + i % 3;
                int b = i / 3;
                SetFunction(a, b, bit);
                SetFunction(b, a, bit);
            }
        }

        private void DrawFinder(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    int xx = x + dx, yy = y + dy;
                    if (0 <= xx && xx < Size && 0 <= yy && yy < Size) SetFunction(xx, yy, dist != 2 && dist != 4);
                }
            }
        }

        private void DrawAlignment(int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++) SetFunction(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        }

        private void SetFunction(int x, int y, bool dark)
        {
            modules[y, x] = dark;
            isFunction[y, x] = true;
        }

        private byte[] AddEccAndInterleave(byte[] data)
        {
            int numBlocks = NumErrorCorrectionBlocks[ecl, Version];
            int blockEccLen = EccCodewordsPerBlock[ecl, Version];
            int rawCodewords = NumRawDataModules(Version) / 8;
            int numShortBlocks = numBlocks - rawCodewords % numBlocks;
            int shortBlockLen = rawCodewords / numBlocks;

            var blocks = new List<byte[]>();
            byte[] divisor = ReedSolomonDivisor(blockEccLen);
            for (int i = 0, k = 0; i < numBlocks; i++)
            {
                int length = shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1);
                var dat = new byte[length];
                Array.Copy(data, k, dat, 0, length);
                k += length;
                var block = new byte[shortBlockLen + 1];
                Array.Copy(dat, block, length);
                byte[] ecc = ReedSolomonRemainder(dat, divisor);
                Array.Copy(ecc, 0, block, block.Length - blockEccLen, ecc.Length);
                blocks.Add(block);
            }

            var result = new byte[rawCodewords];
            for (int i = 0, k = 0; i < blocks[0].Length; i++)
            {
                for (int j = 0; j < blocks.Count; j++)
                {
                    if (i != shortBlockLen - blockEccLen || j >= numShortBlocks) result[k++] = blocks[j][i];
                }
            }
            return result;
        }

        private void DrawCodewords(byte[] data)
        {
            int i = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < Size; vert++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? Size - 1 - vert : vert;
                        if (!isFunction[y, x] && i < data.Length * 8)
                        {
                            modules[y, x] = Bit(data[i >> 3], 7 - (i & 7));
                            i++;
                        }
                    }
                }
            }
        }

        private void ApplyMask(int mask)
        {
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (x + y) % 2 == 0; break;
                        case 1: invert = y % 2 == 0; break;
                        case 2: invert = x % 3 == 0; break;
                        case 3: invert = (x + y) % 3 == 0; break;
                        case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                        case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    modules[y, x] ^= invert & !isFunction[y, x];
                }
            }
        }

        private int PenaltyScore()
        {
            const int n1 = 3, n2 = 3, n3 = 40, n4 = 10;
            int result = 0;
            var history = new int[7];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int a = 0; a < Size; a++)
                {
                    bool runColor = false;
                    int run = 0;
                    Array.Clear(history, 0, history.Length);
                    for (int b = 0; b < Size; b++)
                    {
                        bool dark = pass == 0 ? modules[a, b] : modules[b, a];
                        if (dark == runColor)
                        {
                            run++;
                            if (run == 5) result += n1;
                            else if (run > 5) result++;
                        }
                        else
                        {
                            AddHistory(run, history);
                            if (!runColor) result += CountPatterns(history) * n3;
                            runColor = dark;
                            run = 1;
                        }
                    }
                    result += TerminateAndCount(runColor, run, history) * n3;
                }
            }
            for (int y = 0; y < Size - 1; y++)
            {
                for (int x = 0; x < Size - 1; x++)
                {
                    bool c = modules[y, x];
                    if (c == modules[y, x + 1] && c == modules[y + 1, x] && c == modules[y + 1, x + 1]) result += n2;
                }
            }
            int darkCount = 0;
            foreach (bool m in modules) if (m) darkCount++;
            int total = Size * Size;
            int k = (Math.Abs(darkCount * 20 - total * 10) + total - 1) / total - 1;
            return result + k * n4;
        }

        private int CountPatterns(int[] h)
        {
            int n = h[1];
            bool core = n > 0 && h[2] == n && h[3] == n * 3 && h[4] == n && h[5] == n;
            return (core && h[0] >= n * 4 && h[6] >= n ? 1 : 0) + (core && h[6] >= n * 4 && h[0] >= n ? 1 : 0);
        }

        private int TerminateAndCount(bool runColor, int run, int[] history)
        {
            if (runColor)
            {
                AddHistory(run, history);
                run = 0;
            }
            run += Size;
            AddHistory(run, history);
            return CountPatterns(history);
        }

        private void AddHistory(int run, int[] history)
        {
            if (history[0] == 0) run += Size;
            Array.Copy(history, 0, history, 1, history.Length - 1);
            history[0] = run;
        }

        private int[] AlignmentPositions()
        {
            if (Version == 1) return new int[0];
            int count = Version / 7 + 2;
            int step = (Version * 8 + count * 3 + 5) / (count * 4 - 4) * 2;
            var result = new int[count];
            result[0] = 6;
            for (int i = count - 1, pos = Size - 7; i >= 1; i--, pos -= step) result[i] = pos;
            return result;
        }

        private static int NumRawDataModules(int version)
        {
            int result = (16 * version + 128) * version + 64;
            if (version >= 2)
            {
                int count = version / 7 + 2;
                result -= (25 * count - 10) * count - 55;
                if (version >= 7) result -= 36;
            }
            return result;
        }

        private static int NumDataCodewords(int version, int level) =>
            NumRawDataModules(version) / 8 - EccCodewordsPerBlock[level, version] * NumErrorCorrectionBlocks[level, version];

        private static byte[] ReedSolomonDivisor(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < result.Length; j++)
                {
                    result[j] = (byte)Multiply(result[j], root);
                    if (j + 1 < result.Length) result[j] ^= result[j + 1];
                }
                root = Multiply(root, 0x02);
            }
            return result;
        }

        private static byte[] ReedSolomonRemainder(byte[] data, byte[] divisor)
        {
            var result = new byte[divisor.Length];
            foreach (byte b in data)
            {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++) result[i] ^= (byte)Multiply(divisor[i], factor);
            }
            return result;
        }

        private static int Multiply(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z;
        }

        private static bool Bit(int x, int i) => ((x >> i) & 1) != 0;
    }
}
