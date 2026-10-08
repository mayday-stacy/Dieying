using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Composa.App.Mcp;

/// <summary>
/// Resources: what the tools report, reachable by URI for a client that attaches context rather than calling tools.
/// <c>composa://documents</c> is the document list, <c>composa://documents/{number}</c> describes one document and
/// <c>composa://documents/{number}/image</c> is its render as a PNG.
/// </summary>
public sealed partial class ComposaTools
{
    [McpServerResource(UriTemplate = "composa://documents", Name = "documents", Title = "Open documents", MimeType = "text/plain")]
    [Description("The documents open in " + AppInfo.Name + ", numbered as their tabs are.")]
    public async Task<TextResourceContents> DocumentsResource(RequestContext<ReadResourceRequestParams> context) =>
        new() { Uri = context.Params!.Uri!, MimeType = "text/plain", Text = await ListDocuments() };

    [McpServerResource(UriTemplate = "composa://documents/{number}", Name = "document", Title = "A document's layers", MimeType = "text/plain")]
    [Description("The canvas and the layer stack of the document with that tab number, top layer first.")]
    public async Task<TextResourceContents> DocumentResource(RequestContext<ReadResourceRequestParams> context, string number) =>
        new() { Uri = context.Params!.Uri!, MimeType = "text/plain", Text = await DescribeDocument(Number(number)) };

    [McpServerResource(UriTemplate = "composa://documents/{number}/image", Name = "document_image", Title = "A document as an image", MimeType = "image/png")]
    [Description("The document with that tab number as it looks now, flattened to a PNG at most 1024 px on its longest side.")]
    public async Task<BlobResourceContents> DocumentImageResource(RequestContext<ReadResourceRequestParams> context, string number)
    {
        var png = await OnUi(() => Png(Session(Number(number)), 1024).Png);
        return BlobResourceContents.FromBytes(png, context.Params!.Uri!, "image/png");
    }

    private static int Number(string number) =>
        int.TryParse(number, out var parsed) ? parsed : throw new McpException($"\"{number}\" is not a document number; list_documents shows them.");
}
