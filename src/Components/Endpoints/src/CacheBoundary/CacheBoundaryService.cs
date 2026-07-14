// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components.HotReload;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Components.Endpoints;

internal sealed partial class CacheBoundaryService : IDisposable
{
    private static readonly object _inFlightResolutionsItemKey = new();

    private static readonly JsonSerializerOptions _jsonOptions = ServerComponentSerializationSettings.JsonSerializationOptions;
    private static readonly ComponentParametersTypeCache _parametersTypeCache = new();
    private static readonly ConcurrentDictionary<Type, (CacheBehaviorAttribute? Behavior, CacheConditionAttribute? Condition)> _liveComponentAttributeByType = new();
    private readonly ICacheBoundaryStore _store;
    private readonly ILogger<CacheView> _logger;

    static CacheBoundaryService()
    {
        if (HotReloadManager.IsSupported)
        {
            HotReloadManager.Default.OnDeltaApplied += _liveComponentAttributeByType.Clear;
        }
    }

    public CacheBoundaryService(ICacheBoundaryStore store, ILoggerFactory loggerFactory)
    {
        _store = store;
        _logger = loggerFactory.CreateLogger<CacheView>();

        if (HotReloadManager.IsSupported)
        {
            HotReloadManager.Default.OnDeltaApplied += _store.Clear;
        }
    }

    public void Dispose()
    {
        if (HotReloadManager.IsSupported)
        {
            HotReloadManager.Default.OnDeltaApplied -= _store.Clear;
        }
    }

    public static bool IsCacheableComponent(Type componentType, CacheVaryBy varyBy)
    {
        var (behaviorAttr, conditionAttr) = _liveComponentAttributeByType.GetOrAdd(componentType, static type =>
            (type.GetCustomAttribute<CacheBehaviorAttribute>(inherit: true),
             type.GetCustomAttribute<CacheConditionAttribute>(inherit: true)));

        if (behaviorAttr is null && conditionAttr is null)
        {
            return true;
        }

        var conditionVaryBy = conditionAttr?.VaryBy ?? CacheVaryBy.None;
        var varyByMatches = conditionVaryBy != CacheVaryBy.None && (conditionVaryBy & varyBy) == conditionVaryBy;

        if (behaviorAttr?.Behavior == CacheBehavior.Throw && !varyByMatches)
        {
            throw new InvalidOperationException(
                $"Component '{componentType.FullName}' cannot be used inside a CacheView because its output depends on per-request state ([CacheBehavior(CacheBehavior.Throw)]{(conditionVaryBy != CacheVaryBy.None ? $", [CacheCondition(CacheVaryBy.{conditionVaryBy})]" : "")}) that cannot be safely cached and replayed. " +
                (conditionVaryBy != CacheVaryBy.None
                    ? $"To fix this, configure the boundary to vary by {conditionVaryBy}, or move the component outside the CacheView."
                    : "To fix this, move the component outside the CacheView."));
        }

        return varyByMatches;
    }

    public async Task<CacheBoundaryRenderState?> PrepareAsync(CacheView boundary, HttpContext httpContext)
    {
        // Skip cache if method is not GET, caching is disabled, or the boundary is rendered inside a
        // streaming render context (not yet supported).
        if (!boundary.Enabled || !HttpMethods.IsGet(httpContext.Request.Method) || boundary.IsInStreamingContext)
        {
            return null;
        }

        var key = CacheBoundaryKeyResolver.ComputeKey(boundary, httpContext);
        var state = new CacheBoundaryRenderState(key, GetVaryBy(boundary))
        {
            Content = boundary.ChildContent,
        };

        var resolutions = GetInFlightResolutions(httpContext);
        if (resolutions.TryGetValue(key, out var existing))
        {
            if (!ReferenceEquals(existing.Owner, boundary))
            {
                throw new InvalidOperationException(
                    "Multiple CacheView components resolved to the same cache key. " +
                    "A CacheView was rendered more than once at the same position in the component tree (for example, a reusable component used multiple times, or a CacheView in a loop), so its output cannot be cached unambiguously. " +
                    $"Set a unique {nameof(CacheView.CacheKey)} on each CacheView so every boundary on the page has a distinct cache key.");
            }

            await ApplyDuplicateResolutionAsync(state, key, existing.Task);
            return state;
        }

        var resolution = new TaskCompletionSource<SerializedRenderFragment?>(TaskCreationOptions.RunContinuationsAsynchronously);
        resolutions[key] = (boundary, resolution.Task);

        await ResolveOrBeginCreateAsync(boundary, state, resolution, httpContext.RequestAborted);
        return state;
    }

    public static void ThrowIfNestedInsideCapturingBoundary(TextWriter output)
    {
        if (output is CacheBoundaryTextWriter { IsCapturing: true })
        {
            throw new InvalidOperationException(
                "A CacheView cannot be nested inside another CacheView. The inner boundary's output " +
                "would be frozen into the outer cache entry and replayed on later requests, which is unsafe for " +
                "per-request content such as antiforgery tokens, authentication-dependent output, or interactive " +
                "component markers. Move the CacheView so it is not nested inside another one.");
        }
    }

    public static bool TryBeginWrite(CacheBoundaryRenderState? state, CacheView boundary, TextWriter output, out TextWriter wrappedOutput)
    {
        if (state is { CaptureCompletion: not null })
        {
            var captureWriter = new CacheBoundaryTextWriter(output, state.VaryBy);
            captureWriter.StartCapture();
            state.ActiveWriter = captureWriter;
            wrappedOutput = captureWriter;
            return true;
        }

        if (output is not CacheBoundaryTextWriter)
        {
            var validationWriter = new CacheBoundaryTextWriter(output, GetVaryBy(boundary));
            validationWriter.StartValidation();
            wrappedOutput = validationWriter;
            return true;
        }

        wrappedOutput = output;
        return false;
    }

    public void EndCapture(CacheBoundaryRenderState? state, bool completed)
    {
        var writer = state?.ActiveWriter;
        if (state is null || writer is null)
        {
            return;
        }

        var completion = state.CaptureCompletion;
        var pending = state.PendingStoreTask;

        try
        {
            if (!completed)
            {
                completion?.TrySetCanceled();
                return;
            }
            writer.StopCapture();
            completion?.TrySetResult(writer.GetSerializedRenderFragment());
        }
        catch (Exception ex)
        {
            completion?.TrySetException(ex);
        }
        finally
        {
            state.ActiveWriter = null;
            state.CaptureCompletion = null;
            state.PendingStoreTask = null;
            if (pending is not null)
            {
                _ = ObserveCacheStorePersistAsync(state.Key, pending);
            }
        }
    }

    public void OnBoundaryDisposed(CacheBoundaryRenderState state)
    {
        var completion = state.CaptureCompletion;
        var pending = state.PendingStoreTask;

        if (completion is not null && !completion.Task.IsCompleted)
        {
            completion.TrySetCanceled();
        }

        state.ActiveWriter = null;
        state.CaptureCompletion = null;
        state.PendingStoreTask = null;

        // Cancelling CaptureCompletion above faults the creator's store factory. Observe the resulting
        // task so it does not surface as an unobserved task exception when the boundary is disposed
        // before EndCapture runs.
        if (pending is not null)
        {
            _ = ObserveCacheStorePersistAsync(state.Key, pending);
        }
    }

    public static CacheVaryBy GetVaryBy(CacheView boundary)
    {
        var result = CacheVaryBy.None;

        if (!string.IsNullOrEmpty(boundary.VaryByQuery))
        {
            result |= CacheVaryBy.Query;
        }

        if (!string.IsNullOrEmpty(boundary.VaryByRoute))
        {
            result |= CacheVaryBy.Route;
        }

        if (!string.IsNullOrEmpty(boundary.VaryByHeader))
        {
            result |= CacheVaryBy.Header;
        }

        if (!string.IsNullOrEmpty(boundary.VaryByCookie))
        {
            result |= CacheVaryBy.Cookie;
        }

        if (boundary.VaryByUser)
        {
            result |= CacheVaryBy.User;
        }

        if (boundary.VaryByCulture)
        {
            result |= CacheVaryBy.Culture;
        }

        return result;
    }

    // Reached only when the same CacheView instance re-renders within the request: it reuses the
    // result of its original in-flight resolution rather than creating a second cache entry.
    private async Task ApplyDuplicateResolutionAsync(CacheBoundaryRenderState state, string key, Task<SerializedRenderFragment?> resolution)
    {
        SerializedRenderFragment? cachedPayload;
        try
        {
            cachedPayload = await resolution;
        }
        catch (Exception ex)
        {
            Log.BoundaryReRenderFromFaultedResolution(_logger, key, ex);
            return;
        }

        if (cachedPayload is not null && DeserializeCachedContent(cachedPayload) is { } cachedContent)
        {
            state.IsCacheHit = true;
            state.Content = cachedContent;
        }
        else
        {
            Log.BoundaryReRenderingFresh(_logger, key);
        }
    }

    private async Task ResolveOrBeginCreateAsync(CacheView boundary, CacheBoundaryRenderState state, TaskCompletionSource<SerializedRenderFragment?> resolution, CancellationToken cancellationToken)
    {
        var captureCompletion = new TaskCompletionSource<SerializedRenderFragment>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.CaptureCompletion = captureCompletion;

        var options = new CacheStoreOptions
        {
            ExpiresAfter = boundary.ExpiresAfter,
            ExpiresOn = boundary.ExpiresOn,
            ExpiresSliding = boundary.ExpiresSliding,
        };

        try
        {
            var inflight = _store.GetOrCreateAsync(
                state.Key,
                async ct =>
                {
                    factoryStarted.TrySetResult();
                    return await captureCompletion.Task.WaitAsync(ct);
                },
                options,
                cancellationToken).AsTask();

            // Wait for whichever happens first: the cached value is available or our factory got invoked (we're the creator).
            var firstFinished = await Task.WhenAny(inflight, factoryStarted.Task);
            if (firstFinished == inflight)
            {
                // Cache hit: we are not the creator, so clear the capture reservation so TryBeginWrite does
                // not capture this boundary's output.
                state.CaptureCompletion = null;
                state.IsCacheHit = true;
                var cachedPayload = await inflight;
                state.Content = DeserializeCachedContent(cachedPayload) ?? boundary.ChildContent;
                resolution.TrySetResult(cachedPayload);
            }
            else
            {
                // We are the creator: record the in-flight store task so a re-render of this same boundary
                // reuses the result instead of creating a second entry.
                state.PendingStoreTask = inflight;
                resolution.TrySetResult(null);
            }
        }
        catch (Exception ex)
        {
            resolution.TrySetException(ex);
            throw;
        }
    }

    private RenderFragment? DeserializeCachedContent(SerializedRenderFragment? payload)
    {
        if (payload is null || payload.Nodes.Count == 0)
        {
            return null;
        }

        try
        {
            return RenderFragmentSerializer.Deserialize(payload.Nodes, _jsonOptions, _parametersTypeCache);
        }
        catch (Exception ex)
        {
            Log.RestoreFromCacheFailed(_logger, ex);
            return null;
        }
    }

    private async Task ObserveCacheStorePersistAsync(string key, Task<SerializedRenderFragment> pending)
    {
        try
        {
            await pending;
        }
        catch (OperationCanceledException)
        {
            // Request aborted while persisting; nothing to log.
        }
        catch (Exception ex)
        {
            Log.PersistFailed(_logger, key, ex);
        }
    }

    private static Dictionary<string, (CacheView Owner, Task<SerializedRenderFragment?> Task)> GetInFlightResolutions(HttpContext httpContext)
    {
        if (httpContext.Items[_inFlightResolutionsItemKey] is not Dictionary<string, (CacheView Owner, Task<SerializedRenderFragment?> Task)> resolutions)
        {
            resolutions = new Dictionary<string, (CacheView, Task<SerializedRenderFragment?>)>(StringComparer.Ordinal);
            httpContext.Items[_inFlightResolutionsItemKey] = resolutions;
        }

        return resolutions;
    }

    private static partial class Log
    {
        [LoggerMessage(1, LogLevel.Debug, "CacheView with cache key '{Key}' re-rendered within the same request but no cached content was available; rendering its child content fresh.", EventName = "BoundaryReRenderingFresh")]
        public static partial void BoundaryReRenderingFresh(ILogger logger, string key);

        [LoggerMessage(4, LogLevel.Debug, "CacheView with cache key '{Key}' re-rendered within the same request but its original resolution faulted; rendering its child content fresh.", EventName = "BoundaryReRenderFromFaultedResolution")]
        public static partial void BoundaryReRenderFromFaultedResolution(ILogger logger, string key, Exception exception);

        [LoggerMessage(2, LogLevel.Warning, "Failed to restore CacheView from cached data. Falling back to fresh render.", EventName = "RestoreFromCacheFailed")]
        public static partial void RestoreFromCacheFailed(ILogger logger, Exception exception);

        [LoggerMessage(3, LogLevel.Warning, "Failed to persist CacheView entry for key '{Key}'.", EventName = "PersistFailed")]
        public static partial void PersistFailed(ILogger logger, string key, Exception exception);
    }
}
