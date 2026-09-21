// Copyright (c) Microsoft Corporation.  All rights reserved.

using System;
using System.IO;
using System.Web;
using EFCachingProvider.Web;
using Xunit;

namespace EFCachingProvider.Tests
{
    public class AspNetCacheTests
    {
        // EFCachingProvider.Web is not strong-named (it does not import Common.targets), and the signed test
        // assembly cannot load an unsigned one: FileLoadException "A strongly-named assembly is required".
        private const string WebNotStrongNamed = "EFCachingProvider.Web is not strong-named, so the signed test assembly cannot load it";

        // HttpRuntime.Cache is process-wide: every test uses its own keys and entity-set names.
        private readonly string prefix = Guid.NewGuid().ToString("N");
        private readonly AspNetCache cache;

        public AspNetCacheTests()
        {
            var context = new HttpContext(new HttpRequest(string.Empty, "http://localhost/", string.Empty), new HttpResponse(TextWriter.Null));
            this.cache = new AspNetCache(context);
        }

        private string Key(string name)
        {
            return this.prefix + name;
        }

        [Fact(Skip = WebNotStrongNamed)]
        public void PutThenGet_ReturnsTheValue()
        {
            this.cache.PutItem(this.Key("k"), "v", new[] { this.Key("A") }, TimeSpan.Zero, DateTime.MaxValue);

            object value;
            Assert.True(this.cache.GetItem(this.Key("k"), out value));
            Assert.Equal("v", value);
        }

        [Fact(Skip = WebNotStrongNamed)]
        public void Get_UnknownKey_Misses()
        {
            object value;
            Assert.False(this.cache.GetItem(this.Key("none"), out value));
            Assert.Null(value);
        }

        [Fact(Skip = WebNotStrongNamed)]
        public void InvalidateItem_RemovesTheEntry()
        {
            this.cache.PutItem(this.Key("k"), "v", new string[0], TimeSpan.Zero, DateTime.MaxValue);

            this.cache.InvalidateItem(this.Key("k"));

            object value;
            Assert.False(this.cache.GetItem(this.Key("k"), out value));
        }

        [Fact(Skip = WebNotStrongNamed)]
        public void InvalidateSets_RemovesOnlyDependentEntries()
        {
            this.cache.PutItem(this.Key("a"), 1, new[] { this.Key("A") }, TimeSpan.Zero, DateTime.MaxValue);
            this.cache.PutItem(this.Key("b"), 2, new[] { this.Key("B") }, TimeSpan.Zero, DateTime.MaxValue);

            this.cache.InvalidateSets(new[] { this.Key("A") });

            object value;
            Assert.False(this.cache.GetItem(this.Key("a"), out value));
            Assert.True(this.cache.GetItem(this.Key("b"), out value));
        }

        [Fact(Skip = WebNotStrongNamed)]
        public void EntryPutAfterAnInvalidation_IsCachedAgain()
        {
            this.cache.PutItem(this.Key("a"), 1, new[] { this.Key("A") }, TimeSpan.Zero, DateTime.MaxValue);
            this.cache.InvalidateSets(new[] { this.Key("A") });

            this.cache.PutItem(this.Key("a"), 2, new[] { this.Key("A") }, TimeSpan.Zero, DateTime.MaxValue);

            object value;
            Assert.True(this.cache.GetItem(this.Key("a"), out value));
            Assert.Equal(2, value);
        }

        [Fact(Skip = WebNotStrongNamed)]
        public void SlidingAndAbsoluteExpiration_Together_StillCaches()
        {
            // ASP.NET rejects an item carrying both expirations; the ICache contract (see InMemoryCache) is
            // that a sliding expiration takes precedence, so the item must still be stored.
            this.cache.PutItem(this.Key("k"), "v", new string[0], TimeSpan.FromMinutes(5), DateTime.Now.AddHours(1));

            object value;
            Assert.True(this.cache.GetItem(this.Key("k"), out value));
        }

        [Fact(Skip = WebNotStrongNamed)]
        public void SlidingExpiration_Alone_Caches()
        {
            this.cache.PutItem(this.Key("k"), "v", new string[0], TimeSpan.FromMinutes(5), DateTime.MaxValue);

            object value;
            Assert.True(this.cache.GetItem(this.Key("k"), out value));
        }

        [Fact(Skip = WebNotStrongNamed)]
        public void AbsoluteExpirationInThePast_IsNotReturned()
        {
            this.cache.PutItem(this.Key("k"), "v", new string[0], TimeSpan.Zero, DateTime.Now.AddMinutes(-1));

            object value;
            Assert.False(this.cache.GetItem(this.Key("k"), out value));
        }

        [Fact(Skip = WebNotStrongNamed)]
        public void WithoutAnHttpContext_Throws()
        {
            object value;
            Assert.Throws<InvalidOperationException>(() => new AspNetCache().GetItem("k", out value));
        }
    }
}
