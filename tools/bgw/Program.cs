// bgw: converts a 16-bit PCM wav into FFXI's BGMStream (.bgw) music format, and back.
//
//   bgw encode <in.wav> <out.bgw> --id 108 [--loop <seconds>] [--frame 16]
//   bgw decode <in.bgw> <out.wav>
//
// The format, as read by the game and documented by vgmstream's bgw.c:
//   0x00 "BGMStream\0\0\0"   0x0c codec (0 = PS-ADPCM)   0x10 file size   0x14 track id
//   0x18 frames per channel   0x1c loop start frame + 1 (0 = no loop)
//   0x20/0x24 two words whose sum & 0x7fffffff is the sample rate
//   0x28 data offset (0x30)   0x2c 0x64   0x2d 0x10   0x2e channels   0x2f samples per frame
// Data: per channel, frames of (samples/2 + 1) bytes, interleaved channel by channel:
//   byte 0 = (coefficient set << 4) | shift, then nibbles, low nibble first.
//   sample = ((nibble << 12) sign-extended) >> shift + (c0*h1 + c1*h2) >> 6, clamped to 16 bits.
using System;
using System.IO;
using System.Text;

static class Program
{
    static readonly int[,] Coefs = { { 0, 0 }, { 60, 0 }, { 115, -52 }, { 98, -55 }, { 122, -60 } };

    static int Main(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("bgw encode <in.wav> <out.bgw> --id N [--loop seconds] [--frame 16]\nbgw decode <in.bgw> <out.wav>"); return 2; }
        try
        {
            if (args[0] == "encode") return Encode(args);
            if (args[0] == "decode") return Decode(args);
            Console.Error.WriteLine("unknown command " + args[0]); return 2;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }

    // ---------------------------------------------------------------- encode

    static int Encode(string[] args)
    {
        var inPath = args[1]; var outPath = args[2];
        int id = 0, samplesPerFrame = 16; double loopSeconds = -1;
        for (var i = 3; i < args.Length; i++)
        {
            if (args[i] == "--id" && i + 1 < args.Length) id = int.Parse(args[++i]);
            else if (args[i] == "--loop" && i + 1 < args.Length) loopSeconds = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            else if (args[i] == "--frame" && i + 1 < args.Length) samplesPerFrame = int.Parse(args[++i]);
        }
        if (id <= 0) throw new Exception("--id is the game's track number, for example 108 for the title screen.");
        if (samplesPerFrame < 2 || samplesPerFrame > 128 || samplesPerFrame % 2 != 0) throw new Exception("--frame must be even, 2 to 128.");

        int rate, channels; short[][] pcm;
        ReadWav(inPath, out rate, out channels, out pcm);

        // whole frames only; pad the tail with silence
        var frames = (pcm[0].Length + samplesPerFrame - 1) / samplesPerFrame;
        var bytesPerFrame = samplesPerFrame / 2 + 1;
        var data = new byte[frames * bytesPerFrame * channels];

        for (var ch = 0; ch < channels; ch++)
        {
            int h1 = 0, h2 = 0;
            var src = pcm[ch];
            for (var f = 0; f < frames; f++)
            {
                var frameOffset = (f * channels + ch) * bytesPerFrame;
                EncodeFrame(src, f * samplesPerFrame, samplesPerFrame, ref h1, ref h2, data, frameOffset);
            }
        }

        int loopStart = 0;
        if (loopSeconds >= 0)
        {
            var loopFrame = (int)Math.Round(loopSeconds * rate / samplesPerFrame);
            if (loopFrame < 0) loopFrame = 0;
            if (loopFrame >= frames) loopFrame = 0;
            loopStart = loopFrame + 1;   // the header stores frame index + 1; 0 means play once
        }

        var header = new byte[0x30];
        Encoding.ASCII.GetBytes("BGMStream").CopyTo(header, 0);
        Put32(header, 0x0c, 0);
        Put32(header, 0x10, (uint)(header.Length + data.Length));
        Put32(header, 0x14, (uint)id);
        Put32(header, 0x18, (uint)frames);
        Put32(header, 0x1c, (uint)loopStart);
        // the sample rate is stored as two words that add up to it; the top bit is ignored
        var seed = 0x5A5A5A5Au;
        Put32(header, 0x20, seed);
        Put32(header, 0x24, unchecked((uint)rate - seed));
        Put32(header, 0x28, 0x30);
        header[0x2c] = 0x64; header[0x2d] = 0x10; header[0x2e] = (byte)channels; header[0x2f] = (byte)samplesPerFrame;

        using (var o = File.Create(outPath)) { o.Write(header, 0, header.Length); o.Write(data, 0, data.Length); }
        Console.WriteLine($"{Path.GetFileName(outPath)}: id {id}, {rate} Hz, {channels} ch, {frames} frames of {samplesPerFrame}, {(double)pcm[0].Length / rate:F1} s, loop " + (loopStart > 0 ? $"at frame {loopStart - 1} ({(loopStart - 1) * (double)samplesPerFrame / rate:F2} s)" : "off"));
        return 0;
    }

    /// <summary>Pick the coefficient set and shift that reproduce this frame with the least error, then write it.</summary>
    static void EncodeFrame(short[] src, int start, int n, ref int hist1, ref int hist2, byte[] dst, int at)
    {
        long bestErr = long.MaxValue; int bestCoef = 0, bestShift = 0; int bestH1 = hist1, bestH2 = hist2;
        var bestNibbles = new byte[n];
        var nibbles = new byte[n];

        for (var c = 0; c < 5; c++)
        {
            for (var shift = 0; shift <= 12; shift++)
            {
                int h1 = hist1, h2 = hist2; long err = 0;
                for (var i = 0; i < n; i++)
                {
                    var s = start + i < src.Length ? src[start + i] : 0;
                    var predict = (Coefs[c, 0] * h1 + Coefs[c, 1] * h2) >> 6;
                    var residual = s - predict;
                    // quantise: the nibble is scaled by << 12 >> shift, so one step is 1 << (12 - shift)
                    var stepShift = 12 - shift;
                    var q = (residual + (residual >= 0 ? (1 << stepShift) >> 1 : -((1 << stepShift) >> 1))) >> stepShift;
                    if (q > 7) q = 7; if (q < -8) q = -8;
                    nibbles[i] = (byte)(q & 0xf);
                    var decoded = (((q << 12) & 0xf000) << 16 >> 16) >> shift;   // sign-extend 16 bits then scale
                    decoded += predict;
                    if (decoded > 32767) decoded = 32767; if (decoded < -32768) decoded = -32768;
                    var d = decoded - s; err += (long)d * d;
                    h2 = h1; h1 = decoded;
                    if (err >= bestErr) break;
                }
                if (err < bestErr) { bestErr = err; bestCoef = c; bestShift = shift; bestH1 = h1; bestH2 = h2; Array.Copy(nibbles, bestNibbles, n); }
            }
        }

        dst[at] = (byte)((bestCoef << 4) | bestShift);
        for (var i = 0; i < n; i += 2) dst[at + 1 + i / 2] = (byte)(bestNibbles[i] | (bestNibbles[i + 1] << 4));
        hist1 = bestH1; hist2 = bestH2;
    }

    // ---------------------------------------------------------------- decode

    static int Decode(string[] args)
    {
        var b = File.ReadAllBytes(args[1]);
        if (Encoding.ASCII.GetString(b, 0, 9) != "BGMStream") throw new Exception("not a BGMStream file");
        var codec = Get32(b, 0x0c);
        if (codec != 0) throw new Exception("codec " + codec + " is not PS-ADPCM; only codec 0 is handled");
        var frames = (int)Get32(b, 0x18);
        var loopStart = (int)Get32(b, 0x1c);
        var rate = (int)((Get32(b, 0x20) + Get32(b, 0x24)) & 0x7fffffff);
        var start = (int)Get32(b, 0x28);
        int channels = b[0x2e], spf = b[0x2f];
        var bpf = spf / 2 + 1;
        var pcm = new short[channels][];
        for (var ch = 0; ch < channels; ch++)
        {
            pcm[ch] = new short[frames * spf];
            int h1 = 0, h2 = 0;
            for (var f = 0; f < frames; f++)
            {
                var at = start + (f * channels + ch) * bpf;
                if (at + bpf > b.Length) break;
                int coef = (b[at] >> 4) & 0xf, shift = b[at] & 0xf;
                if (coef > 4) coef = 0; if (shift > 12) shift = 9;
                for (var i = 0; i < spf; i++)
                {
                    var nib = (i & 1) == 1 ? (b[at + 1 + i / 2] >> 4) & 0xf : b[at + 1 + i / 2] & 0xf;
                    var s = (((nib << 12) & 0xf000) << 16 >> 16) >> shift;
                    s += (Coefs[coef, 0] * h1 + Coefs[coef, 1] * h2) >> 6;
                    if (s > 32767) s = 32767; if (s < -32768) s = -32768;
                    pcm[ch][f * spf + i] = (short)s; h2 = h1; h1 = s;
                }
            }
        }
        WriteWav(args[2], rate, channels, pcm);
        Console.WriteLine($"{Path.GetFileName(args[1])}: id {Get32(b, 0x14)}, {rate} Hz, {channels} ch, {frames} frames of {spf}, {(double)frames * spf / rate:F1} s, loop " + (loopStart > 0 ? $"frame {loopStart - 1}" : "off"));
        return 0;
    }

    // ---------------------------------------------------------------- wav

    static void ReadWav(string path, out int rate, out int channels, out short[][] pcm)
    {
        var b = File.ReadAllBytes(path);
        if (Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WAVE") throw new Exception("not a wav file");
        int pos = 12, fmtChannels = 0, fmtRate = 0, fmtBits = 0, fmtTag = 0, dataAt = -1, dataLen = 0;
        while (pos + 8 <= b.Length)
        {
            var tag = Encoding.ASCII.GetString(b, pos, 4); var len = (int)Get32(b, pos + 4);
            if (tag == "fmt ") { fmtTag = b[pos + 8] | (b[pos + 9] << 8); fmtChannels = b[pos + 10] | (b[pos + 11] << 8); fmtRate = (int)Get32(b, pos + 12); fmtBits = b[pos + 22] | (b[pos + 23] << 8); }
            else if (tag == "data") { dataAt = pos + 8; dataLen = Math.Min(len, b.Length - dataAt); break; }
            pos += 8 + len + (len & 1);
        }
        if (dataAt < 0 || (fmtTag != 1 && fmtTag != 0xFFFE) || fmtBits != 16) throw new Exception("the wav must be 16-bit PCM (ffmpeg: -sample_fmt s16)");
        rate = fmtRate; channels = fmtChannels;
        var n = dataLen / (2 * channels);
        pcm = new short[channels][];
        for (var ch = 0; ch < channels; ch++) pcm[ch] = new short[n];
        for (var i = 0; i < n; i++) for (var ch = 0; ch < channels; ch++) pcm[ch][i] = (short)(b[dataAt + (i * channels + ch) * 2] | (b[dataAt + (i * channels + ch) * 2 + 1] << 8));
    }

    static void WriteWav(string path, int rate, int channels, short[][] pcm)
    {
        var n = pcm[0].Length; var dataLen = n * channels * 2;
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataLen); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataLen);
            for (var i = 0; i < n; i++) for (var ch = 0; ch < channels; ch++) w.Write(pcm[ch][i]);
        }
    }

    static uint Get32(byte[] b, int at) => (uint)(b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24));
    static void Put32(byte[] b, int at, uint v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24); }
}
