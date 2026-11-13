using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using NVorbis;

namespace FnrPort.Audio;

/// <summary>Lee un frame tras otro de un sonido (lo que consume cada voz del mezclador).</summary>
public abstract class FrameReader : IDisposable
{
    public abstract int Channels { get; }
    public abstract int Rate { get; }
    /// <summary>Siguiente frame. Devuelve false al terminar (si <paramref name="loop"/> es true, vuelve al punto de bucle).</summary>
    public abstract bool Next(bool loop, out float l, out float r);
    public virtual void Dispose() { }
}

/// <summary>Datos de un sonido: un efecto entero en memoria (WAV) o una música que se lee a trozos (OGG).</summary>
public abstract class AudioData : IDisposable
{
    public abstract FrameReader CreateReader();
    public virtual void Dispose() { }

    /// <summary>Lee un archivo del juego (los .dat pueden estar comprimidos con gzip).</summary>
    public static byte[] ReadGameFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
        {
            using var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            gz.CopyTo(ms);
            return ms.ToArray();
        }
        return bytes;
    }

    static bool IsGzip(string path)
    {
        using var fs = File.OpenRead(path);
        return fs.ReadByte() == 0x1f && fs.ReadByte() == 0x8b;
    }

    /// <summary>Efecto de sonido: se decodifica entero (WAV u OGG).</summary>
    public static AudioData LoadSound(string path)
    {
        var bytes = ReadGameFile(path);
        if (OggMusic.IsOgg(bytes)) return PcmClip.FromOgg(bytes);
        return PcmClip.FromWav(bytes);
    }

    /// <summary>Música: se decodifica sobre la marcha mientras suena.</summary>
    public static AudioData LoadMusic(string path)
    {
        if (IsGzip(path)) { var b = ReadGameFile(path); return OggMusic.IsOgg(b) ? new OggMusic(() => new MemoryStream(b)) : PcmClip.FromWav(b); }
        using (var fs = File.OpenRead(path))
        {
            var head = new byte[4];
            if (fs.Read(head, 0, 4) == 4 && !OggMusic.IsOgg(head)) return PcmClip.FromWav(File.ReadAllBytes(path));
        }
        return new OggMusic(() => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024));
    }
}

/// <summary>Sonido entero en memoria (float, 1 o 2 canales).</summary>
public sealed class PcmClip : AudioData
{
    public readonly float[] Data;
    public readonly int Channels, Rate, Frames;

    public PcmClip(float[] data, int channels, int rate)
    {
        Data = data; Channels = channels; Rate = rate; Frames = data.Length / channels;
    }

    public override FrameReader CreateReader() => new Reader(this);

    sealed class Reader : FrameReader
    {
        readonly PcmClip c; int mPos;
        public Reader(PcmClip clip) { c = clip; }
        public override int Channels => c.Channels;
        public override int Rate => c.Rate;
        public override bool Next(bool loop, out float l, out float r)
        {
            if (mPos >= c.Frames)
            {
                if (!loop || c.Frames == 0) { l = r = 0; return false; }
                mPos = 0;
            }
            if (c.Channels == 1) { l = r = c.Data[mPos]; }
            else { int i = mPos * c.Channels; l = c.Data[i]; r = c.Data[i + 1]; }
            mPos++;
            return true;
        }
    }

    /// <summary>WAV PCM 8/16/24/32 bits o float 32, cualquier número de canales (se queda con los dos primeros).</summary>
    public static PcmClip FromWav(byte[] b)
    {
        if (b.Length < 12 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F') throw new InvalidDataException("No es un WAV");
        int pos = 12, fmt = 0, ch = 0, rate = 0, bits = 0, dataOff = -1, dataLen = 0;
        while (pos + 8 <= b.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
            int size = BitConverter.ToInt32(b, pos + 4);
            int body = pos + 8;
            if (id == "fmt ")
            {
                fmt = BitConverter.ToUInt16(b, body); ch = BitConverter.ToUInt16(b, body + 2);
                rate = BitConverter.ToInt32(b, body + 4); bits = BitConverter.ToUInt16(b, body + 14);
                if (fmt == 0xFFFE && size >= 40) fmt = BitConverter.ToUInt16(b, body + 24); // WAVE_FORMAT_EXTENSIBLE
            }
            else if (id == "data")
            {
                dataOff = body; dataLen = Math.Min(size < 0 ? int.MaxValue : size, b.Length - body);
                break;
            }
            if (size < 0) break;
            pos = body + size + (size & 1);
        }
        if (dataOff < 0 || ch <= 0 || rate <= 0) throw new InvalidDataException("WAV sin datos");
        int bps = bits / 8;
        int frames = dataLen / (bps * ch);
        int outCh = Math.Min(ch, 2);
        var o = new float[frames * outCh];
        for (int f = 0; f < frames; f++)
        {
            for (int c = 0; c < outCh; c++)
            {
                int p = dataOff + (f * ch + c) * bps;
                float v;
                if (fmt == 3 && bits == 32) v = BitConverter.ToSingle(b, p);
                else if (fmt == 3 && bits == 64) v = (float)BitConverter.ToDouble(b, p);
                else switch (bits)
                {
                    case 8: v = (b[p] - 128) / 128f; break;
                    case 16: v = (short)(b[p] | (b[p + 1] << 8)) / 32768f; break;
                    case 24: v = ((b[p] << 8 | b[p + 1] << 16 | b[p + 2] << 24) >> 8) / 8388608f; break;
                    case 32: v = BitConverter.ToInt32(b, p) / 2147483648f; break;
                    default: throw new InvalidDataException("WAV de " + bits + " bits no soportado");
                }
                o[f * outCh + c] = v;
            }
        }
        return new PcmClip(o, outCh, rate);
    }

    public static PcmClip FromOgg(byte[] b)
    {
        using var vr = new VorbisReader(new MemoryStream(b), true);
        int ch = vr.Channels, outCh = Math.Min(ch, 2);
        var all = new System.Collections.Generic.List<float>();
        var buf = new float[4096 * ch];
        int n;
        while ((n = vr.ReadSamples(buf, 0, buf.Length)) > 0)
            for (int i = 0; i < n; i += ch)
                for (int c = 0; c < outCh; c++) all.Add(buf[i + c]);
        return new PcmClip(all.ToArray(), outCh, vr.SampleRate);
    }
}

/// <summary>
/// Música OGG que se decodifica mientras suena (NVorbis). Respeta los puntos de bucle de las
/// etiquetas, igual que el juego original con FMOD: LOOP_START/LOOP_END en milisegundos
/// o LOOPSTART/LOOPLENGTH en muestras.
/// </summary>
public sealed class OggMusic : AudioData
{
    readonly Func<Stream> mOpen;
    bool mTagsRead;
    long mLoopStartMs = -1, mLoopEndMs = -1, mLoopStartSmp = -1, mLoopLenSmp = -1;

    public OggMusic(Func<Stream> open) { mOpen = open; }

    public static bool IsOgg(byte[] b) => b.Length >= 4 && b[0] == 'O' && b[1] == 'g' && b[2] == 'g' && b[3] == 'S';

    public override FrameReader CreateReader()
    {
        var vr = new VorbisReader(mOpen(), true);
        if (!mTagsRead)
        {
            mTagsRead = true;
            mLoopStartMs = Tag(vr, "LOOP_START"); mLoopEndMs = Tag(vr, "LOOP_END");
            mLoopStartSmp = Tag(vr, "LOOPSTART"); mLoopLenSmp = Tag(vr, "LOOPLENGTH");
        }
        long start = 0, end = long.MaxValue;
        int rate = vr.SampleRate;
        if (mLoopStartMs >= 0 || mLoopEndMs >= 0)
        {
            if (mLoopStartMs >= 0) start = mLoopStartMs * rate / 1000;
            if (mLoopEndMs > 0) end = mLoopEndMs * rate / 1000 + 1;
        }
        else if (mLoopStartSmp >= 0)
        {
            start = mLoopStartSmp;
            if (mLoopLenSmp > 0) end = mLoopStartSmp + mLoopLenSmp;
        }
        if (end <= start) { start = 0; end = long.MaxValue; }
        return new Reader(vr, start, end);
    }

    static long Tag(VorbisReader vr, string key)
    {
        try
        {
            var s = vr.Tags.GetTagSingle(key);
            if (!string.IsNullOrEmpty(s) && long.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v)) return v;
        }
        catch { }
        return -1;
    }

    sealed class Reader : FrameReader
    {
        readonly VorbisReader mVr;
        readonly int mCh, mRate;
        readonly long mLoopStart, mLoopEnd;
        readonly float[] mBuf;
        int mCount, mIdx;
        long mPos;       // frame del siguiente bloque a decodificar
        bool mEof;

        public Reader(VorbisReader vr, long loopStart, long loopEnd)
        {
            mVr = vr; mCh = vr.Channels; mRate = vr.SampleRate;
            mLoopStart = loopStart; mLoopEnd = loopEnd;
            mBuf = new float[2048 * mCh];
        }

        public override int Channels => Math.Min(mCh, 2);
        public override int Rate => mRate;

        public override bool Next(bool loop, out float l, out float r)
        {
            if (mIdx >= mCount && !Fill(loop)) { l = r = 0; return false; }
            l = mBuf[mIdx];
            r = mCh > 1 ? mBuf[mIdx + 1] : l;
            mIdx += mCh;
            return true;
        }

        bool Fill(bool loop)
        {
            int n = Decode();
            if (n <= 0)
            {
                if (!loop) return false;
                try { mVr.SamplePosition = mLoopStart; mPos = mLoopStart; mEof = false; }
                catch (Exception ex) { Console.WriteLine("[FnrPort] Música: no se pudo volver al bucle: " + ex.Message); return false; }
                n = Decode();
                if (n <= 0) return false;
            }
            mCount = n * mCh; mIdx = 0;
            return true;
        }

        int Decode()
        {
            if (mEof) return 0;
            long remain = mLoopEnd - mPos;
            if (remain <= 0) return 0;
            int want = (int)Math.Min(mBuf.Length / mCh, remain);
            int got = mVr.ReadSamples(mBuf, 0, want * mCh) / mCh;
            if (got <= 0) { mEof = true; return 0; }
            mPos += got;
            return got;
        }

        public override void Dispose() => mVr.Dispose();
    }
}
