// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Components.Rendering;

namespace Microsoft.AspNetCore.Components.Endpoints;

public class IsCacheableComponentTest
{
    [Fact]
    public void NoAttribute_IsCacheable()
    {
        Assert.True(CacheBoundaryService.IsCacheableComponent(typeof(ComponentBase), CacheVaryBy.None));
    }

    [Fact]
    public void Attribute_NoVaryBy_IsNotCacheable()
    {
        Assert.False(CacheBoundaryService.IsCacheableComponent(typeof(UnconditionalLiveCachedComponent), CacheVaryBy.None));
        Assert.False(CacheBoundaryService.IsCacheableComponent(typeof(UnconditionalLiveCachedComponent), CacheVaryBy.User));
    }

    [Fact]
    public void Attribute_Throw_ThrowsWhenNotCovered()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CacheBoundaryService.IsCacheableComponent(typeof(ThrowingComponent), CacheVaryBy.None));
    }

    [Fact]
    public void Attribute_VaryBy_NotCacheableWhenNotCovered_CacheableWhenCovered()
    {
        Assert.False(CacheBoundaryService.IsCacheableComponent(typeof(ConditionalLiveCachedComponent), CacheVaryBy.None));
        Assert.True(CacheBoundaryService.IsCacheableComponent(typeof(ConditionalLiveCachedComponent), CacheVaryBy.User));
    }

    [Fact]
    public void Attribute_MultipleVaryByFlags_RequiresFullMatch()
    {
        var partial = CacheVaryBy.User;
        var full = CacheVaryBy.User | CacheVaryBy.Query;

        Assert.False(CacheBoundaryService.IsCacheableComponent(typeof(MultiDimensionLiveCachedComponent), partial));
        Assert.True(CacheBoundaryService.IsCacheableComponent(typeof(MultiDimensionLiveCachedComponent), full));
    }

    [Fact]
    public void Attribute_Inherited_AppliesToSubclass()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CacheBoundaryService.IsCacheableComponent(typeof(DerivedThrowingComponent), CacheVaryBy.None));
    }

    [CacheBehavior(CacheBehavior.Rerender)]
    private class UnconditionalLiveCachedComponent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    }

    [CacheBehavior(CacheBehavior.Throw)]
    private class ThrowingComponent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    }

    private sealed class DerivedThrowingComponent : ThrowingComponent { }

    [CacheCondition(CacheVaryBy.User)]
    private class ConditionalLiveCachedComponent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    }

    [CacheCondition(CacheVaryBy.User | CacheVaryBy.Query)]
    private class MultiDimensionLiveCachedComponent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    }
}
