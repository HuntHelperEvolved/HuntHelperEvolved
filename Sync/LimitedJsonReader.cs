using System.IO;
using Newtonsoft.Json;
namespace HuntHelperEvolved.Sync;

/// <summary>Bounds parsed-object overhead as well as the transport's wire bytes.</summary>
public sealed class LimitedJsonReader(TextReader input):JsonTextReader(input)
{
    private int _tokens;
    public override bool Read()
    {
        var result=base.Read();
        if(result && (++_tokens>250000 || Value is string text && text.Length>8192))
            throw new JsonReaderException("Sync JSON exceeds the parsing budget.");
        return result;
    }
}
