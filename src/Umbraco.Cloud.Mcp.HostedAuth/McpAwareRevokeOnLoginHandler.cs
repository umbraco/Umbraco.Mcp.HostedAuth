using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace Umbraco.Cloud.Mcp.HostedAuth;

/// <summary>
/// Replacement for the built-in
/// <c>RevokeUserAuthenticationTokensNotificationHandler</c> on the
/// <see cref="UserLoginSuccessNotification"/> path. It enforces the same
/// single-session behaviour (revoke a user's OpenIddict tokens on login when
/// concurrent logins are disabled) but spares tokens issued to the configured
/// MCP clients, so a backoffice login no longer severs the user's hosted MCP
/// session.
/// </summary>
/// <remarks>
/// The built-in handler's <c>UserSaved</c> / <c>UserDeleted</c> registrations
/// are left intact, so disabling or deleting a user still revokes their MCP
/// tokens — the kill-switch is preserved.
/// </remarks>
public sealed class McpAwareRevokeOnLoginHandler
    : INotificationAsyncHandler<UserLoginSuccessNotification>
{
    private readonly IUserService _userService;
    private readonly IOpenIddictTokenManager _tokenManager;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly ProtectedMcpApplicationStore _protectedApplications;
    private readonly SecuritySettings _securitySettings;
    private readonly ILogger<McpAwareRevokeOnLoginHandler> _logger;

    public McpAwareRevokeOnLoginHandler(
        IUserService userService,
        IOpenIddictTokenManager tokenManager,
        IOpenIddictApplicationManager applicationManager,
        ProtectedMcpApplicationStore protectedApplications,
        IOptions<SecuritySettings> securitySettings,
        ILogger<McpAwareRevokeOnLoginHandler> logger)
    {
        _userService = userService;
        _tokenManager = tokenManager;
        _applicationManager = applicationManager;
        _protectedApplications = protectedApplications;
        _securitySettings = securitySettings.Value;
        _logger = logger;
    }

    public async Task HandleAsync(
        UserLoginSuccessNotification notification,
        CancellationToken cancellationToken)
    {
        // Mirror the built-in gate: only enforce single-session when concurrent
        // logins are disabled. This is what SecuritySettings.GetUserAllowConcurrentLogins()
        // computes (user-specific value, falling back to the general flag).
        bool allowConcurrentLogins =
            _securitySettings.UserAllowConcurrentLogins ?? _securitySettings.AllowConcurrentLogins;
        if (allowConcurrentLogins)
        {
            return;
        }

        IUser? user = await FindUserAsync(notification.AffectedUserId);
        if (user is null)
        {
            return;
        }

        IReadOnlySet<string> protectedApplicationIds =
            await _protectedApplications.GetProtectedApplicationIdsAsync(_applicationManager, cancellationToken);

        int revoked = 0;
        int spared = 0;
        await foreach (object token in _tokenManager
            .FindBySubjectAsync(user.Key.ToString(), cancellationToken)
            .WithCancellation(cancellationToken))
        {
            string? applicationId = await _tokenManager.GetApplicationIdAsync(token, cancellationToken);
            if (applicationId is not null && protectedApplicationIds.Contains(applicationId))
            {
                spared++;
                continue;
            }

            await _tokenManager.DeleteAsync(token, cancellationToken);
            revoked++;
        }

        if (spared > 0)
        {
            _logger.LogDebug(
                "[HostedMcp] Login revoke for user {UserKey}: revoked {Revoked} token(s), spared {Spared} MCP token(s).",
                user.Key, revoked, spared);
        }
    }

    // Mirrors the built-in handler's FindUserFromString: AffectedUserId may be
    // an integer id or a Guid key.
    private async Task<IUser?> FindUserAsync(string? affectedUserId)
    {
        if (string.IsNullOrWhiteSpace(affectedUserId))
        {
            return null;
        }

        if (int.TryParse(affectedUserId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intId))
        {
            return _userService.GetUserById(intId);
        }

        if (Guid.TryParse(affectedUserId, out Guid key))
        {
            return await _userService.GetAsync(key);
        }

        return null;
    }
}
