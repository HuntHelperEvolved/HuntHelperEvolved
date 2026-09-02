using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Numerics;

namespace HuntTrainRelay;

/// <summary>
/// Draws the spawn point dots, as 32x32 RGBA PNGs written to the plugin's own
/// config folder on first use.
///
/// They used to be four fixed base64 blobs — grey, blue, red and green — baked
/// into this file. They're generated now because the colours are configurable,
/// and a hardcoded image can't be recoloured without decoding it first. The
/// generated shape matches what the blobs drew: a circle inscribed in the tile,
/// solid through the middle, antialiased at the rim, with the corners fully
/// transparent.
///
/// Written to disk rather than handed over as a texture because KamiToolKit's
/// MapMarkerInfo picks its image by file path, and generating them means a
/// release still stays two files (dll + manifest) with nothing to go missing.
/// </summary>
public static class DotTextures
{
    /// <summary>
    /// Default tile size, which is what a spawn point dot is drawn at. The
    /// player's ring and its fill ask for something much larger, since they are
    /// stretched to the circle's width on screen rather than shown at icon size.
    /// </summary>
    public const int Size = 32;

    /// <summary>
    /// Supersampling factor per axis. 4 gives 16 samples a pixel, which is
    /// enough that the rim reads as smooth at the sizes these are drawn at.
    /// </summary>
    private const int Samples = 4;

    /// <summary>
    /// A dot in the given colour, as PNG bytes.
    ///
    /// Alpha is straight, not premultiplied: RGB stays constant across the
    /// whole tile and only the alpha channel falls off at the rim, which is
    /// how the original images were built.
    /// </summary>
    public static byte[] Render(Vector4 colour, int size = Size)
    {
        var r = ToByte(colour.X);
        var g = ToByte(colour.Y);
        var b = ToByte(colour.Z);
        var a = Math.Clamp(colour.W, 0f, 1f);

        var pixels = new byte[size * size * 4];

        // Radius half the tile, about its centre, so the circle spans the full
        // width edge to edge and leaves the corners empty.
        var centre = (size - 1) / 2f;
        var radius = size / 2f;
        var radiusSq = radius * radius;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var inside = 0;

                for (var sy = 0; sy < Samples; sy++)
                {
                    for (var sx = 0; sx < Samples; sx++)
                    {
                        // Sample at sub-pixel centres, so coverage is
                        // symmetric about the middle of the pixel.
                        var px = x + (sx + 0.5f) / Samples - 0.5f;
                        var py = y + (sy + 0.5f) / Samples - 0.5f;
                        var dx = px - centre;
                        var dy = py - centre;
                        if ((dx * dx) + (dy * dy) <= radiusSq)
                            inside++;
                    }
                }

                if (inside == 0)
                    continue;

                var coverage = inside / (float)(Samples * Samples);
                var offset = ((y * size) + x) * 4;
                pixels[offset + 0] = r;
                pixels[offset + 1] = g;
                pixels[offset + 2] = b;
                pixels[offset + 3] = ToByte(coverage * a);
            }
        }

        return EncodePng(size, size, pixels);
    }

    /// <summary>
    /// A ring outline, for the detection circle. Drawn at a larger resolution
    /// than the dots because it is stretched to the circle's full width on
    /// screen rather than shown at icon size.
    ///
    /// <paramref name="strokePixels"/> is the line width within this texture,
    /// so the on-screen thickness follows the map's zoom along with everything
    /// else.
    /// </summary>
    public static byte[] RenderRing(Vector4 colour, int size = 256, float strokePixels = 8f)
    {
        var r = ToByte(colour.X);
        var g = ToByte(colour.Y);
        var b = ToByte(colour.Z);
        var a = Math.Clamp(colour.W, 0f, 1f);

        var pixels = new byte[size * size * 4];

        var centre = (size - 1) / 2f;
        var outer = size / 2f;
        var inner = MathF.Max(0f, outer - strokePixels);
        var outerSq = outer * outer;
        var innerSq = inner * inner;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var inside = 0;

                for (var sy = 0; sy < Samples; sy++)
                {
                    for (var sx = 0; sx < Samples; sx++)
                    {
                        var px = x + (sx + 0.5f) / Samples - 0.5f;
                        var py = y + (sy + 0.5f) / Samples - 0.5f;
                        var dx = px - centre;
                        var dy = py - centre;
                        var dSq = (dx * dx) + (dy * dy);
                        if (dSq <= outerSq && dSq >= innerSq)
                            inside++;
                    }
                }

                if (inside == 0)
                    continue;

                var coverage = inside / (float)(Samples * Samples);
                var offset = ((y * size) + x) * 4;
                pixels[offset + 0] = r;
                pixels[offset + 1] = g;
                pixels[offset + 2] = b;
                pixels[offset + 3] = ToByte(coverage * a);
            }
        }

        return EncodePng(size, size, pixels);
    }

    /// <summary>
    /// A five-pointed star, for where the mark itself will appear.
    ///
    /// A different shape rather than a different colour on purpose: it belongs
    /// to the same event as the minion dots and reads better sharing their
    /// colour, so the shape is what says it is not one of them.
    ///
    /// Filled by the even-odd rule, which for a star drawn as ten alternating
    /// points gives the outline everyone draws by hand rather than a pentagon
    /// with spikes.
    /// </summary>
    public static byte[] RenderStar(Vector4 colour, int size = 128)
    {
        var r = ToByte(colour.X);
        var g = ToByte(colour.Y);
        var b = ToByte(colour.Z);
        var a = Math.Clamp(colour.W, 0f, 1f);

        var pixels = new byte[size * size * 4];

        var centre = (size - 1) / 2f;
        var outer = size / 2f;

        // The classic proportion; anything much larger loses the points.
        var inner = outer * 0.382f;

        var corners = new Vector2[10];
        for (var i = 0; i < 10; i++)
        {
            // Start at the top and alternate out, in, out...
            var angle = (-MathF.PI / 2f) + (i * MathF.PI / 5f);
            var radius = (i % 2) == 0 ? outer : inner;
            corners[i] = new Vector2(
                centre + (MathF.Cos(angle) * radius),
                centre + (MathF.Sin(angle) * radius));
        }

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var inside = 0;

                for (var sy = 0; sy < Samples; sy++)
                {
                    for (var sx = 0; sx < Samples; sx++)
                    {
                        var px = x + (sx + 0.5f) / Samples - 0.5f;
                        var py = y + (sy + 0.5f) / Samples - 0.5f;
                        if (InPolygon(corners, px, py))
                            inside++;
                    }
                }

                if (inside == 0)
                    continue;

                var coverage = inside / (float)(Samples * Samples);
                var offset = ((y * size) + x) * 4;
                pixels[offset + 0] = r;
                pixels[offset + 1] = g;
                pixels[offset + 2] = b;
                pixels[offset + 3] = ToByte(coverage * a);
            }
        }

        return EncodePng(size, size, pixels);
    }

    /// <summary>Even-odd point-in-polygon: counts crossings of a ray going right.</summary>
    private static bool InPolygon(Vector2[] corners, float x, float y)
    {
        var inside = false;

        for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
        {
            var a = corners[i];
            var b = corners[j];

            if ((a.Y > y) == (b.Y > y))
                continue;

            var crossingX = ((b.X - a.X) * (y - a.Y) / (b.Y - a.Y)) + a.X;
            if (x < crossingX)
                inside = !inside;
        }

        return inside;
    }

    /// <summary>
    /// A flat block of colour, for the projected path band. The band is a
    /// stretched quad, so the image only has to carry the colour — its shape
    /// comes from the node it is drawn into.
    ///
    /// Deliberately one uniform rectangle rather than a run of overlapping
    /// sprites: translucent shapes that overlap compound their alpha, so a band
    /// built that way would be blotched where the pieces met.
    /// </summary>
    public static byte[] RenderSolid(Vector4 colour, int size = 8)
    {
        var pixels = new byte[size * size * 4];
        var r = ToByte(colour.X);
        var g = ToByte(colour.Y);
        var b = ToByte(colour.Z);
        var alpha = ToByte(colour.W);

        for (var i = 0; i < size * size; i++)
        {
            pixels[(i * 4) + 0] = r;
            pixels[(i * 4) + 1] = g;
            pixels[(i * 4) + 2] = b;
            pixels[(i * 4) + 3] = alpha;
        }

        return EncodePng(size, size, pixels);
    }

    /// <summary>Six hex digits for a colour, for use in a file name.</summary>
    public static string HexOf(Vector4 colour) =>
        $"{ToByte(colour.X):x2}{ToByte(colour.Y):x2}{ToByte(colour.Z):x2}{ToByte(colour.W):x2}";

    private static byte ToByte(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    /// <summary>
    /// Minimal PNG writer: signature, IHDR, one IDAT and IEND. Hand-rolled
    /// rather than pulled in, because the only alternative on this target is
    /// another third-party dependency for what amounts to a header, a zlib
    /// stream and three CRCs.
    /// </summary>
    private static byte[] EncodePng(int width, int height, byte[] rgba)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;  // bit depth
        header[9] = 6;  // colour type: truecolour with alpha
        header[10] = 0; // deflate
        header[11] = 0; // adaptive filtering
        header[12] = 0; // no interlacing
        WriteChunk(output, "IHDR", header);

        // Each scanline is prefixed with its filter type. Zero — "none" —
        // keeps this trivial; deflate still does the real work.
        var stride = width * 4;
        var raw = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
        {
            raw[y * (stride + 1)] = 0;
            Array.Copy(rgba, y * stride, raw, (y * (stride + 1)) + 1, stride);
        }

        byte[] compressed;
        using (var buffer = new MemoryStream())
        {
            // ZLibStream, not DeflateStream: PNG wants the zlib wrapper
            // (RFC 1950) around the deflate data, not bare deflate.
            using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(raw, 0, raw.Length);
            compressed = buffer.ToArray();
        }

        WriteChunk(output, "IDAT", compressed);
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        var payload = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++)
            payload[i] = (byte)type[i];
        Array.Copy(data, 0, payload, 4, data.Length);
        stream.Write(payload);

        // The CRC covers the chunk type and its data, but not the length.
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(payload));
        stream.Write(crc);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var i = 0u; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data)
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
