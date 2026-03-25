using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization.Metadata;

namespace NekoGraph.Cli;

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public static readonly JsonSerializerOptions NodeWrite = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };
}
