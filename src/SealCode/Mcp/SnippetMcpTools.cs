using System.ComponentModel;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using SealCode.Hosting;
using SealCode.Security;

using Transport.Models;

namespace SealCode.Mcp;

/// <summary>Exposes the same database snippet editor as the super-admin UI.</summary>
[McpServerToolType]
internal sealed class SnippetMcpTools
{
    /// <summary>Lists editable questions, Markdown sources and the required revision.</summary>
    /// <param name="editor">The shared editor and cache publication service.</param>
    /// <param name="admins">The authenticated MCP identity.</param>
    /// <param name="cancellationToken">Cancels the database read.</param>
    /// <returns>The editable snapshot and concurrency revision.</returns>
    /// <exception cref="McpException">Editing is disabled or the caller is not a super admin.</exception>
    [McpServerTool(Name = "list_snippets", ReadOnly = true, UseStructuredContent = true),
     Description("Super admin only. Read all database snippet questions and examples with their catalog version. Requires enabled PostgreSql snippets.")]
    public static async Task<SnippetEditorCatalogResponse> ListAsync(SnippetAdministration editor, IAdminUserManager admins,
        CancellationToken cancellationToken)
    {
        EnsureAccess(editor, admins);
        var snapshot = (await editor.LoadAsync(cancellationToken).ConfigureAwait(false))!;
        return new(snapshot.UpdatedAt,
            [.. snapshot.Library.Categories.Select(SnippetCategoryResponse.From)]);
    }

    /// <summary>Publishes an existing question and immediately refreshes the application cache.</summary>
    /// <param name="editor">The shared editor and cache publication service.</param>
    /// <param name="admins">The authenticated MCP identity.</param>
    /// <param name="language">The existing category identifier from list_snippets.</param>
    /// <param name="number">The existing question number from list_snippets.</param>
    /// <param name="request">The complete question revision and expected catalog version.</param>
    /// <param name="cancellationToken">Cancels persistence before commit.</param>
    /// <returns>Publication confirmation after cache refresh.</returns>
    /// <exception cref="McpException">The caller is forbidden, input is invalid or the revision is stale.</exception>
    [McpServerTool(Name = "update_snippet", Destructive = true, UseStructuredContent = true),
     Description("Super admin only. Publish a complete existing question (topic, description and all ordered examples). Use ExpectedVersion from list_snippets. Concurrent edits are rejected. Refreshes the application cache immediately.")]
    public static async Task<SnippetPublicationResponse> UpdateAsync(SnippetAdministration editor, IAdminUserManager admins,
        string language, int number, UpdateSnippetRequest request, CancellationToken cancellationToken)
    {
        EnsureAccess(editor, admins);
        try
        {
            if (!await editor.PublishAsync(language, number, request, cancellationToken).ConfigureAwait(false))
            {
                throw new McpException("Catalog changed or question missing. Read list_snippets again before publishing.");
            }

            return new(true);
        }
        catch (ArgumentException)
        {
            throw new McpException("Invalid snippet revision. Supply a topic, description and 1–100 nonempty examples within content limits.");
        }
    }

    private static void EnsureAccess(SnippetAdministration editor, IAdminUserManager admins)
    {
        if (!editor.CanEdit(admins))
        {
            throw new McpException("Super admin authentication and enabled PostgreSql snippets are required.");
        }
    }
}
