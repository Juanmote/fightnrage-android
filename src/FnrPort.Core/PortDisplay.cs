using System.IO;
using System.IO.Compression;
using SebaGames.Core;
using SebaGames.Framework;

namespace FnrPort;

/// <summary>IDisplayEngine (réplica de OTKDisplayEngine).</summary>
public class PortDisplay : IDisplayEngine
{
    PortSurface mMain;
    public IDrawableSurface MainSurface => mMain;

    internal void Initialize(int w, int h) => mMain = PortSurface.CreateNew(w, h);
    internal void Dispose() => mMain?.Dispose();

    public IDrawableSurface CreateSurface(int aWidth, int aHeight) => PortSurface.CreateNew(aWidth, aHeight);

    public IBitmap CreateBitmap(int aWidth, int aHeight)
    {
        var px = new byte[aWidth * aHeight * 4];
        for (int i = 0; i < px.Length; i++) px[i] = 255;
        return PortTexture.FromRgba(aWidth, aHeight, px);
    }

    public IBitmap CreateBitmap(string aFile)
    {
        aFile = PathIndexer.GetPath(aFile);
        using Stream fs = File.OpenRead(aFile);
        if (PathIndexer.IsCompressed(aFile))
        {
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            return PortTexture.FromStream(gz);
        }
        return PortTexture.FromStream(fs);
    }

    // El original devuelve false, pero los shaders se usan igualmente.
    public bool IsShaderAvailable() => false;

    public IShader CreateShader(string aFile)
    {
        aFile = PathIndexer.GetPath(aFile);
        return new PortShader(PathIndexer.ReadAllText(aFile), Path.GetFileName(aFile));
    }
}
