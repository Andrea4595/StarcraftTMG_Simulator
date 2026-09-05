using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace TmgBoard
{
    /// <summary>GIF89a 인코더를 처음부터 구현(2026-09-05, 리플레이 GIF 내보내기용
    /// — 이 프로젝트엔 기존 GIF 인프라가 전혀 없었다). BoardManager와 완전히
    /// 무관한 순수 데이터-in/바이트-out 유틸리티.
    ///
    /// 팔레트는 median-cut으로 프레임들의 실제 색 분포에 맞춰 뽑는다
    /// (2026-09-05, 고정 6x6x6=216색 격자 팔레트에서 교체 — 사용자가 GIF에서
    /// 텍스트가 뭉개져 보인다고 보고했는데, 격자 팔레트는 텍스트 안티에일리싱
    /// 가장자리처럼 미묘한 중간색이 많이 쓰이는 곳에 특히 약하다. GIF의
    /// LZW 압축 자체는 무손실이라 여기서 손댈 게 없고, 색이 깎이는 지점은
    /// 팔레트 256색으로 줄이는 이 양자화 단계뿐이다). 픽셀→팔레트 인덱스
    /// 매핑은 색공간을 5비트/채널(32768칸)로 낮춘 룩업 테이블을 팔레트당
    /// 한 번만 만들어서 픽셀마다는 테이블 조회만 한다(팔레트 자체는 원본
    /// 정밀도로 만들고, 어느 팔레트색이 가장 가까운지 찾는 단계만 근사—
    /// 프레임이 여러 장이고 픽셀도 수백만 개라 매 픽셀마다 256색 전체와
    /// 직접 거리 비교하면 감당 못 할 정도로 느려진다).</summary>
    internal static class GifEncoder
    {
        private const int PaletteSize = 256;
        private const int LookupBitsPerChannel = 5;
        private const int LookupLevels = 1 << LookupBitsPerChannel; // 32.
        private const int LookupShift = 8 - LookupBitsPerChannel; // 3.

        internal readonly struct Frame
        {
            /// <summary>위→아래(top-down) 행 순서, width*height 길이. Unity의
            /// Texture2D.GetPixels32()는 아래→위(bottom-up)라 호출부에서 반드시
            /// 뒤집어 넘겨야 한다.</summary>
            public readonly Color32[] Pixels;
            public readonly int DelayCentiseconds;

            public Frame(Color32[] pixels, int delayCentiseconds)
            {
                Pixels = pixels;
                DelayCentiseconds = delayCentiseconds;
            }
        }

        internal static byte[] Encode(int width, int height, IReadOnlyList<Frame> frames)
        {
            var palette = BuildPalette(frames);
            var lookup = BuildNearestLookup(palette);

            using var stream = new MemoryStream();
            WriteHeader(stream);
            WriteLogicalScreenDescriptor(stream, width, height);
            WriteGlobalColorTable(stream, palette);
            WriteNetscapeLoopExtension(stream);

            foreach (var frame in frames)
            {
                WriteGraphicControlExtension(stream, frame.DelayCentiseconds);
                WriteImageDescriptor(stream, width, height);
                WriteImageData(stream, width, height, frame.Pixels, lookup);
            }

            stream.WriteByte(0x3B); // 트레일러.
            return stream.ToArray();
        }

        // ── 팔레트(median-cut) ───────────────────────────────────────────

        private struct HistEntry
        {
            public byte R, G, B;
            public int Count;
        }

        /// <summary>모든 프레임의 픽셀에서 (색, 등장 횟수) 히스토그램을 뽑고,
        /// median-cut으로 최대 256개의 대표색으로 뭉친다. 프레임이 많고
        /// 해상도가 커서 픽셀 전부를 다 훑으면 느리므로 간격을 두고
        /// 샘플링한다 — 팔레트 대표성엔 거의 영향 없고, 실행 중 몇 초 이상
        /// 멈추는 걸 피할 수 있다.</summary>
        private static Color32[] BuildPalette(IReadOnlyList<Frame> frames)
        {
            const int SampleStride = 3;
            var histogram = new Dictionary<int, int>();
            foreach (var frame in frames)
            {
                var pixels = frame.Pixels;
                for (int i = 0; i < pixels.Length; i += SampleStride)
                {
                    var c = pixels[i];
                    int key = (c.r << 16) | (c.g << 8) | c.b;
                    histogram.TryGetValue(key, out int count);
                    histogram[key] = count + 1;
                }
            }

            if (histogram.Count == 0)
            {
                return new[] { new Color32(0, 0, 0, 255) };
            }

            var entries = new HistEntry[histogram.Count];
            int idx = 0;
            foreach (var kv in histogram)
            {
                entries[idx++] = new HistEntry
                {
                    R = (byte)((kv.Key >> 16) & 0xFF),
                    G = (byte)((kv.Key >> 8) & 0xFF),
                    B = (byte)(kv.Key & 0xFF),
                    Count = kv.Value,
                };
            }

            // (start, end, weight) — entries 배열 자체를 계속 정렬/분할해
            // 재사용한다(분할마다 새 리스트를 만들지 않음).
            var boxes = new List<(int Start, int End, long Weight)>
            {
                (0, entries.Length, SumWeight(entries, 0, entries.Length)),
            };

            while (boxes.Count < PaletteSize)
            {
                int splitIndex = FindLargestSplittableBox(boxes);
                if (splitIndex < 0)
                {
                    break; // 더 나눌 수 있는(고유 색이 2개 이상인) 박스가 없음.
                }

                var box = boxes[splitIndex];
                int channel = WidestChannel(entries, box.Start, box.End);
                SortRangeByChannel(entries, box.Start, box.End, channel);

                long half = box.Weight / 2;
                long running = 0;
                int mid = box.Start + 1;
                for (int i = box.Start; i < box.End; i++)
                {
                    running += entries[i].Count;
                    if (running >= half)
                    {
                        mid = i + 1;
                        break;
                    }
                }
                mid = Mathf.Clamp(mid, box.Start + 1, box.End - 1);

                long leftWeight = SumWeight(entries, box.Start, mid);
                boxes[splitIndex] = (box.Start, mid, leftWeight);
                boxes.Add((mid, box.End, box.Weight - leftWeight));
            }

            var palette = new Color32[boxes.Count];
            for (int i = 0; i < boxes.Count; i++)
            {
                palette[i] = AverageColor(entries, boxes[i].Start, boxes[i].End);
            }
            return palette;
        }

        private static long SumWeight(HistEntry[] entries, int start, int end)
        {
            long sum = 0;
            for (int i = start; i < end; i++)
            {
                sum += entries[i].Count;
            }
            return sum;
        }

        private static int FindLargestSplittableBox(List<(int Start, int End, long Weight)> boxes)
        {
            int best = -1;
            long bestWeight = -1;
            for (int i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                if (b.End - b.Start <= 1)
                {
                    continue; // 색이 하나뿐 — 더 못 나눔.
                }
                if (b.Weight > bestWeight)
                {
                    bestWeight = b.Weight;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>박스 안 색들 중 R/G/B 중 값의 범위(max-min)가 가장 큰 채널을
        /// 고른다 — median-cut이 늘 그 축으로 나누는 이유는, 색이 가장 넓게
        /// 퍼진 방향으로 잘라야 두 조각이 각자 더 뭉쳐진(비슷한 색끼리)
        /// 상태가 되기 때문.</summary>
        private static int WidestChannel(HistEntry[] entries, int start, int end)
        {
            byte rMin = 255, rMax = 0, gMin = 255, gMax = 0, bMin = 255, bMax = 0;
            for (int i = start; i < end; i++)
            {
                var e = entries[i];
                if (e.R < rMin) rMin = e.R;
                if (e.R > rMax) rMax = e.R;
                if (e.G < gMin) gMin = e.G;
                if (e.G > gMax) gMax = e.G;
                if (e.B < bMin) bMin = e.B;
                if (e.B > bMax) bMax = e.B;
            }
            int rRange = rMax - rMin;
            int gRange = gMax - gMin;
            int bRange = bMax - bMin;
            if (rRange >= gRange && rRange >= bRange)
            {
                return 0;
            }
            return gRange >= bRange ? 1 : 2;
        }

        private static void SortRangeByChannel(HistEntry[] entries, int start, int end, int channel)
        {
            Array.Sort(entries, start, end - start, Comparer<HistEntry>.Create((a, b) =>
            {
                int av = channel == 0 ? a.R : channel == 1 ? a.G : a.B;
                int bv = channel == 0 ? b.R : channel == 1 ? b.G : b.B;
                return av.CompareTo(bv);
            }));
        }

        private static Color32 AverageColor(HistEntry[] entries, int start, int end)
        {
            long r = 0, g = 0, b = 0, count = 0;
            for (int i = start; i < end; i++)
            {
                var e = entries[i];
                r += (long)e.R * e.Count;
                g += (long)e.G * e.Count;
                b += (long)e.B * e.Count;
                count += e.Count;
            }
            if (count == 0)
            {
                return new Color32(0, 0, 0, 255);
            }
            return new Color32((byte)(r / count), (byte)(g / count), (byte)(b / count), 255);
        }

        /// <summary>실제 24비트 색을 5비트/채널(32768칸)로 낮춘 키로 미리
        /// 양자화해, 그 키에 대해 가장 가까운 팔레트 색의 인덱스를 한 번씩만
        /// 계산해둔 표. 픽셀마다는 이 표를 조회만 하면 되므로(색공간을
        /// 낮춘 키 계산 + 배열 인덱싱, O(1)) 수백만 픽셀을 매 팔레트 색과
        /// 직접 비교하는 것보다 훨씬 빠르다.</summary>
        private static byte[] BuildNearestLookup(Color32[] palette)
        {
            var lookup = new byte[LookupLevels * LookupLevels * LookupLevels];
            for (int r5 = 0; r5 < LookupLevels; r5++)
            {
                int r8 = (r5 << LookupShift) | (1 << (LookupShift - 1));
                for (int g5 = 0; g5 < LookupLevels; g5++)
                {
                    int g8 = (g5 << LookupShift) | (1 << (LookupShift - 1));
                    for (int b5 = 0; b5 < LookupLevels; b5++)
                    {
                        int b8 = (b5 << LookupShift) | (1 << (LookupShift - 1));
                        int best = 0;
                        int bestDist = int.MaxValue;
                        for (int p = 0; p < palette.Length; p++)
                        {
                            int dr = r8 - palette[p].r;
                            int dg = g8 - palette[p].g;
                            int db = b8 - palette[p].b;
                            int dist = dr * dr + dg * dg + db * db;
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                best = p;
                            }
                        }
                        int key = (r5 << (LookupBitsPerChannel * 2)) | (g5 << LookupBitsPerChannel) | b5;
                        lookup[key] = (byte)best;
                    }
                }
            }
            return lookup;
        }

        private static void WriteHeader(Stream s)
        {
            var sig = Encoding.ASCII.GetBytes("GIF89a");
            s.Write(sig, 0, sig.Length);
        }

        private static void WriteLogicalScreenDescriptor(Stream s, int width, int height)
        {
            WriteUInt16LE(s, (ushort)width);
            WriteUInt16LE(s, (ushort)height);
            // 전역 컬러 테이블 있음(1), 색 해상도 8bit(111), 정렬 안 됨(0), 테이블
            // 크기 2^(7+1)=256(111).
            s.WriteByte(0b1_111_0_111);
            s.WriteByte(0); // 배경색 인덱스.
            s.WriteByte(0); // 픽셀 비율(미사용).
        }

        private static void WriteGlobalColorTable(Stream s, Color32[] palette)
        {
            for (int i = 0; i < PaletteSize; i++)
            {
                if (i < palette.Length)
                {
                    s.WriteByte(palette[i].r);
                    s.WriteByte(palette[i].g);
                    s.WriteByte(palette[i].b);
                }
                else
                {
                    s.WriteByte(0);
                    s.WriteByte(0);
                    s.WriteByte(0);
                }
            }
        }

        private static void WriteNetscapeLoopExtension(Stream s)
        {
            s.WriteByte(0x21); // 확장 도입자.
            s.WriteByte(0xFF); // 애플리케이션 확장 라벨.
            s.WriteByte(11); // 블록 크기.
            var appId = Encoding.ASCII.GetBytes("NETSCAPE2.0");
            s.Write(appId, 0, appId.Length);
            s.WriteByte(3); // 서브블록 크기.
            s.WriteByte(1); // 서브블록 id(루프 카운트).
            WriteUInt16LE(s, 0); // 0 = 무한 반복.
            s.WriteByte(0); // 블록 종료.
        }

        private static void WriteGraphicControlExtension(Stream s, int delayCentiseconds)
        {
            s.WriteByte(0x21);
            s.WriteByte(0xF9);
            s.WriteByte(4);
            s.WriteByte(0x00); // 투명색 없음, disposal 미지정.
            WriteUInt16LE(s, (ushort)Mathf.Clamp(delayCentiseconds, 0, ushort.MaxValue));
            s.WriteByte(0); // 투명색 인덱스(미사용).
            s.WriteByte(0); // 블록 종료.
        }

        private static void WriteImageDescriptor(Stream s, int width, int height)
        {
            s.WriteByte(0x2C); // 이미지 구분자.
            WriteUInt16LE(s, 0); // left.
            WriteUInt16LE(s, 0); // top.
            WriteUInt16LE(s, (ushort)width);
            WriteUInt16LE(s, (ushort)height);
            s.WriteByte(0x00); // 로컬 컬러 테이블 없음, 인터레이스 안 함.
        }

        private static void WriteImageData(Stream s, int width, int height, Color32[] pixels, byte[] nearestLookup)
        {
            var indices = new byte[width * height];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = QuantizeToPaletteIndex(pixels[i], nearestLookup);
            }

            const int minCodeSize = 8; // 256색 팔레트 -> LZW 최소 코드 크기 8.
            s.WriteByte((byte)minCodeSize);

            var lzwBytes = LzwEncodeReference(indices, minCodeSize);
            foreach (var b in lzwBytes)
            {
                s.WriteByte(b);
            }
            s.WriteByte(0x00); // 블록 종료.
        }

        private static byte QuantizeToPaletteIndex(Color32 c, byte[] nearestLookup)
        {
            int r5 = c.r >> LookupShift;
            int g5 = c.g >> LookupShift;
            int b5 = c.b >> LookupShift;
            int key = (r5 << (LookupBitsPerChannel * 2)) | (g5 << LookupBitsPerChannel) | b5;
            return nearestLookup[key];
        }

        // ── LZW(GIFCOMPR.C 기반) ─────────────────────────────────────────
        // 직접 재도출한 두 차례의 자체 LZW 구현이 전부 손상된 파일을
        // 만들어서(2026-09-05, 사용자 확인) — GIF89a 스펙 Appendix F에 실린
        // 원조 참고 구현(Unix `compress` 유틸리티 기반 GIFCOMPR.C, Kevin
        // Weiner의 Java AnimatedGifEncoder/LZWEncoder를 거쳐 수많은 GIF
        // 인코더의 공통 뿌리가 된 그 코드)을 최대한 충실히 그대로 옮겼다.
        // 해시 테이블 기반 사전(Dictionary가 아니라 open-addressing 배열)과
        // output()의 코드 폭 증가 판정 순서(현재 코드를 emit한 "다음에"
        // free_ent를 검사)까지 원본 그대로 유지 — 직접 재해석해서 바꾸면
        // 또 같은 종류의 버그가 날 위험이 있어 그대로 옮기는 쪽을 택했다.
        private const int LzwBits = 12;
        private const int LzwHashSize = 5003; // compress(1)이 쓰던 소수(80% 점유율 기준).
        private static readonly int[] LzwMasks =
        {
            0x0000, 0x0001, 0x0003, 0x0007, 0x000F, 0x001F, 0x003F, 0x007F,
            0x00FF, 0x01FF, 0x03FF, 0x07FF, 0x0FFF, 0x1FFF, 0x3FFF, 0x7FFF, 0xFFFF,
        };

        /// <summary>colorDepth(팔레트 인덱스가 필요로 하는 비트 수, 이 파일에서는
        /// 항상 8)를 받아 픽셀 인덱스 배열을 GIF 서브블록까지 이미 포장된
        /// 바이트 목록으로 압축한다 — 호출부는 이 결과 뒤에 블록 종료
        /// (0x00) 한 바이트만 더 붙이면 된다.</summary>
        private static List<byte> LzwEncodeReference(byte[] pixels, int colorDepth)
        {
            const int Eof = -1;
            int initCodeSize = Mathf.Max(2, colorDepth);

            int[] htab = new int[LzwHashSize];
            int[] codetab = new int[LzwHashSize];
            int hsize = LzwHashSize;
            int maxbits = LzwBits;
            int maxmaxcode = 1 << LzwBits;

            int gInitBits = initCodeSize + 1;
            int clearCode = 1 << initCodeSize;
            int eofCode = clearCode + 1;
            int freeEnt = clearCode + 2;
            bool clearFlag = false;
            int nBits = gInitBits;
            int maxcode = (1 << nBits) - 1;

            int curAccum = 0;
            int curBits = 0;

            var output = new List<byte>();
            var charBuf = new byte[254];
            int aCount = 0;

            void FlushChar()
            {
                if (aCount > 0)
                {
                    output.Add((byte)aCount);
                    for (int i = 0; i < aCount; i++)
                    {
                        output.Add(charBuf[i]);
                    }
                    aCount = 0;
                }
            }

            void CharOut(byte c)
            {
                charBuf[aCount++] = c;
                if (aCount >= charBuf.Length)
                {
                    FlushChar();
                }
            }

            void Output(int code)
            {
                curAccum &= LzwMasks[curBits];
                if (curBits > 0)
                {
                    curAccum |= code << curBits;
                }
                else
                {
                    curAccum = code;
                }
                curBits += nBits;
                while (curBits >= 8)
                {
                    CharOut((byte)(curAccum & 0xFF));
                    curAccum >>= 8;
                    curBits -= 8;
                }

                // 원본과 동일한 순서: 코드를 emit한 "다음"에 free_ent를 검사한다
                // (이번에 새로 추가되는 사전 항목 자체는 아직 free_ent에
                // 반영되기 전 값으로 판정됨 — 이 순서를 바꾸면 인코더/디코더
                // 사전 크기 계산이 어긋난다).
                if (freeEnt > maxcode || clearFlag)
                {
                    if (clearFlag)
                    {
                        nBits = gInitBits;
                        maxcode = (1 << nBits) - 1;
                        clearFlag = false;
                    }
                    else
                    {
                        nBits++;
                        maxcode = nBits == maxbits ? maxmaxcode : (1 << nBits) - 1;
                    }
                }

                if (code == eofCode)
                {
                    while (curBits > 0)
                    {
                        CharOut((byte)(curAccum & 0xFF));
                        curAccum >>= 8;
                        curBits -= 8;
                    }
                    FlushChar();
                }
            }

            void ClearHash()
            {
                for (int i = 0; i < hsize; i++)
                {
                    htab[i] = -1;
                }
            }

            void ClearBlock()
            {
                ClearHash();
                freeEnt = clearCode + 2;
                clearFlag = true;
                Output(clearCode);
            }

            int cursor = 0;
            int NextPixel() => cursor < pixels.Length ? pixels[cursor++] & 0xFF : Eof;

            int ent = NextPixel();

            int hshift = 0;
            for (int fcode = hsize; fcode < 65536; fcode *= 2)
            {
                hshift++;
            }
            hshift = 8 - hshift;

            ClearHash();
            Output(clearCode);

            int c;
            while ((c = NextPixel()) != Eof)
            {
                int fcode = (c << maxbits) + ent;
                int i = (c << hshift) ^ ent;
                bool found = false;

                if (htab[i] == fcode)
                {
                    ent = codetab[i];
                    found = true;
                }
                else if (htab[i] >= 0)
                {
                    int disp = i == 0 ? 1 : hsize - i;
                    do
                    {
                        i -= disp;
                        if (i < 0)
                        {
                            i += hsize;
                        }
                        if (htab[i] == fcode)
                        {
                            ent = codetab[i];
                            found = true;
                            break;
                        }
                    } while (htab[i] >= 0);
                }

                if (found)
                {
                    continue;
                }

                Output(ent);
                ent = c;
                if (freeEnt < maxmaxcode)
                {
                    codetab[i] = freeEnt++;
                    htab[i] = fcode;
                }
                else
                {
                    ClearBlock();
                }
            }

            Output(ent);
            Output(eofCode);

            return output;
        }

        private static void WriteUInt16LE(Stream s, ushort value)
        {
            s.WriteByte((byte)(value & 0xFF));
            s.WriteByte((byte)((value >> 8) & 0xFF));
        }
    }
}
