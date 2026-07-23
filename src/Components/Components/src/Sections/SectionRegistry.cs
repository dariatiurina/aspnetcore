// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Components.Sections;

internal sealed partial class SectionRegistry(ILoggerFactory? loggerFactory)
{
    private readonly Dictionary<object, SectionOutlet> _subscribersByIdentifier = new();
    private readonly Dictionary<object, List<SectionContent>> _providersByIdentifier = new();

    private readonly ILogger? _logger = loggerFactory?.CreateLogger("Microsoft.AspNetCore.Components.Sections.SectionRegistry");

    private HashSet<object>? _identifiersPendingOrphanCheck;

    // Kept as separate "already logged" sets so a section that changes problem type still emits the new diagnostic. Concrete case: during streaming SSR a SectionOutlet first renders orphaned (its SectionContent hasn't streamed in yet), then the SectionContent streams in under a different render mode; the mismatch warning must still fire even though the orphan was already logged.
    private HashSet<object>? _mismatchLoggedIdentifiers;

    private HashSet<object>? _orphanLoggedIdentifiers;

    public void AddProvider(object identifier, SectionContent provider, bool isDefaultProvider)
    {
        if (!_providersByIdentifier.TryGetValue(identifier, out var providers))
        {
            providers = new();
            _providersByIdentifier.Add(identifier, providers);
        }

        if (isDefaultProvider)
        {
            providers.Insert(0, provider);
        }
        else
        {
            providers.Add(provider);
        }

        MarkForOrphanCheck(identifier);
    }

    public void RemoveProvider(object identifier, SectionContent provider)
    {
        if (!_providersByIdentifier.TryGetValue(identifier, out var providers))
        {
            throw new InvalidOperationException($"There are no content providers with the given section ID '{identifier}'.");
        }

        var index = providers.LastIndexOf(provider);

        if (index < 0)
        {
            throw new InvalidOperationException($"The provider was not found in the providers list of the given section ID '{identifier}'.");
        }

        providers.RemoveAt(index);

        if (index == providers.Count)
        {
            // We just removed the most recently added provider, meaning we need to change
            // the current content to that of second most recently added provider.
            var contentProvider = GetCurrentProviderContentOrDefault(providers);
            NotifyContentChangedForSubscriber(identifier, contentProvider);
        }

        MarkForOrphanCheck(identifier);
    }

    public void Subscribe(object identifier, SectionOutlet subscriber)
    {
        if (_subscribersByIdentifier.ContainsKey(identifier))
        {
            throw new InvalidOperationException($"There is already a subscriber to the content with the given section ID '{identifier}'.");
        }

        // Notify the new subscriber with any existing content.
        var provider = GetCurrentProviderContentOrDefault(identifier);
        subscriber.ContentUpdated(provider);

        _subscribersByIdentifier.Add(identifier, subscriber);

        DetectRenderModeMismatch(identifier, subscriber, provider);
        MarkForOrphanCheck(identifier);
    }

    public void Unsubscribe(object identifier)
    {
        if (!_subscribersByIdentifier.Remove(identifier))
        {
            throw new InvalidOperationException($"The subscriber with the given section ID '{identifier}' is already unsubscribed.");
        }

        MarkForOrphanCheck(identifier);
    }

    public void NotifyContentProviderChanged(object identifier, SectionContent provider)
    {
        if (!_providersByIdentifier.TryGetValue(identifier, out var providers))
        {
            throw new InvalidOperationException($"There are no content providers with the given section ID '{identifier}'.");
        }

        // We only notify content changed for subscribers when the content of the
        // most recently added provider changes.
        if (providers.Count != 0 && providers[^1] == provider)
        {
            NotifyContentChangedForSubscriber(identifier, provider);

            // Option A: the active content for this section changed. If a matching outlet exists in a
            // different render mode, warn now.
            if (_subscribersByIdentifier.TryGetValue(identifier, out var subscriber))
            {
                DetectRenderModeMismatch(identifier, subscriber, provider);
            }
        }
    }

    /// <summary>
    /// Called by the renderer once a render batch has completed. Emits deferred diagnostics for
    /// SectionOutlet/SectionContent components that remain "orphaned" (Option B).
    /// </summary>
    public void OnRenderBatchCompleted()
    {
        if (_identifiersPendingOrphanCheck is not { Count: > 0 } pending)
        {
            return;
        }

        foreach (var identifier in pending)
        {
            EvaluateOrphanState(identifier);
        }

        pending.Clear();
    }

    private void MarkForOrphanCheck(object identifier)
    {
        if (_logger is null)
        {
            return;
        }

        (_identifiersPendingOrphanCheck ??= new()).Add(identifier);
    }

    private void EvaluateOrphanState(object identifier)
    {
        var hasSubscriber = _subscribersByIdentifier.ContainsKey(identifier);
        var hasProvider = _providersByIdentifier.TryGetValue(identifier, out var providers) && providers.Count != 0;

        // If one side is missing (or both were removed), any previously logged render mode mismatch is resolved.
        if (!(hasSubscriber && hasProvider))
        {
            _mismatchLoggedIdentifiers?.Remove(identifier);
        }

        if (hasSubscriber == hasProvider)
        {
            _orphanLoggedIdentifiers?.Remove(identifier);
            return;
        }

        if (!TryMarkOrphanLogged(identifier) || _logger is null)
        {
            return;
        }

        if (hasSubscriber)
        {
            Log.SectionOutletWithoutContent(_logger, DescribeIdentifier(identifier));
        }
        else
        {
            Log.SectionContentWithoutOutlet(_logger, DescribeIdentifier(identifier));
        }
    }

    private void DetectRenderModeMismatch(object identifier, SectionOutlet subscriber, SectionContent? provider)
    {
        if (_logger is null || provider is null)
        {
            return;
        }

        var outletRenderMode = subscriber.SectionRenderMode;
        var contentRenderMode = provider.SectionRenderMode;

        if (!RenderModesDiffer(outletRenderMode, contentRenderMode))
        {
            _mismatchLoggedIdentifiers?.Remove(identifier);
            return;
        }

        if ((_mismatchLoggedIdentifiers ??= new()).Add(identifier))
        {
            Log.SectionRenderModeMismatch(_logger, DescribeIdentifier(identifier), DescribeRenderMode(outletRenderMode), DescribeRenderMode(contentRenderMode));
        }
    }

    private bool TryMarkOrphanLogged(object identifier)
        => (_orphanLoggedIdentifiers ??= new()).Add(identifier);

    private static bool RenderModesDiffer(IComponentRenderMode? left, IComponentRenderMode? right)
        => left?.GetType() != right?.GetType();

    private static string DescribeRenderMode(IComponentRenderMode? renderMode)
        => renderMode is null ? "static server-side rendering" : renderMode.GetType().Name;

    private static string DescribeIdentifier(object identifier)
        => identifier as string ?? identifier.ToString() ?? "(unknown)";

    private static SectionContent? GetCurrentProviderContentOrDefault(List<SectionContent> providers)
        => providers.Count != 0
            ? providers[^1]
            : null;

    private SectionContent? GetCurrentProviderContentOrDefault(object identifier)
        => _providersByIdentifier.TryGetValue(identifier, out var existingList)
            ? GetCurrentProviderContentOrDefault(existingList)
            : null;

    private void NotifyContentChangedForSubscriber(object identifier, SectionContent? provider)
    {
        if (_subscribersByIdentifier.TryGetValue(identifier, out var subscriber))
        {
            subscriber.ContentUpdated(provider);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(1, LogLevel.Warning, "The section with ID '{SectionId}' has its SectionOutlet in render mode '{OutletRenderMode}' and its SectionContent in render mode '{ContentRenderMode}'. Sections cannot connect across render mode boundaries, so the outlet will not display this content once the components become interactive.", EventName = "SectionRenderModeMismatch")]
        public static partial void SectionRenderModeMismatch(ILogger logger, string sectionId, string outletRenderMode, string contentRenderMode);

        [LoggerMessage(2, LogLevel.Debug, "The section with ID '{SectionId}' has a SectionOutlet but no matching SectionContent.", EventName = "SectionOutletWithoutContent")]
        public static partial void SectionOutletWithoutContent(ILogger logger, string sectionId);

        [LoggerMessage(3, LogLevel.Debug, "The section with ID '{SectionId}' has a SectionContent but no matching SectionOutlet.", EventName = "SectionContentWithoutOutlet")]
        public static partial void SectionContentWithoutOutlet(ILogger logger, string sectionId);
    }
}
