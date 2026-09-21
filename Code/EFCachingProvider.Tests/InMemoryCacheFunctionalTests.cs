// Copyright (c) Microsoft Corporation.  All rights reserved.

using System;
using System.Collections.Generic;
using EFCachingProvider.Caching;
using Xunit;

namespace EFCachingProvider.Tests
{
    public class InMemoryCacheFunctionalTests
    {
        private static readonly DateTime Now = new DateTime(2020, 1, 1, 12, 0, 0);

        private static InMemoryCache NewCache(int maxItems = int.MaxValue)
        {
            return new InMemoryCache(maxItems) { GetCurrentDate = () => Now };
        }

        [Fact]
        public void PutItem_SnapshotsTheDependencyList()
        {
            // The caller owns the list; changing it afterwards must not change what the entry depends on.
            var cache = NewCache();
            var sets = new List<string> { "A" };
            cache.PutItem("k", 1, sets, TimeSpan.Zero, DateTime.MaxValue);
            sets.Add("B");

            cache.InvalidateItem("k");

            object value;
            Assert.False(cache.GetItem("k", out value));
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void PutItem_ModifyingTheDependencyListLater_DoesNotBreakInvalidation()
        {
            var cache = NewCache();
            var sets = new List<string> { "A" };
            cache.PutItem("k", 1, sets, TimeSpan.Zero, DateTime.MaxValue);
            sets.Clear();

            cache.InvalidateSets(new[] { "A" });

            object value;
            Assert.False(cache.GetItem("k", out value));
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void PutItem_DuplicateDependencies_AreHandled()
        {
            var cache = NewCache();
            cache.PutItem("k", 1, new[] { "A", "A" }, TimeSpan.Zero, DateTime.MaxValue);

            cache.InvalidateItem("k");
            cache.PutItem("k", 2, new[] { "A" }, TimeSpan.Zero, DateTime.MaxValue);

            object value;
            Assert.True(cache.GetItem("k", out value));
            Assert.Equal(2, value);
        }

        [Fact]
        public void PutItem_NullKey_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => NewCache().PutItem(null, 1, new string[0], TimeSpan.Zero, DateTime.MaxValue));
        }

        [Fact]
        public void PutItem_NullDependencies_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => NewCache().PutItem("k", 1, null, TimeSpan.Zero, DateTime.MaxValue));
        }

        [Fact]
        public void InvalidateSets_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => NewCache().InvalidateSets(null));
        }

        [Fact]
        public void InvalidateItem_UnknownKey_IsANoOp()
        {
            var cache = NewCache();
            cache.PutItem("k", 1, new string[0], TimeSpan.Zero, DateTime.MaxValue);

            cache.InvalidateItem("nope");

            Assert.Equal(1, cache.Count);
            Assert.Equal(0, cache.CacheItemInvalidations);
        }

        [Fact]
        public void InvalidateSets_UnknownSet_IsANoOp()
        {
            var cache = NewCache();
            cache.PutItem("k", 1, new[] { "A" }, TimeSpan.Zero, DateTime.MaxValue);

            cache.InvalidateSets(new[] { "Z" });

            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void InvalidateSets_RemovesOnlyDependentEntries()
        {
            var cache = NewCache();
            cache.PutItem("a", 1, new[] { "A" }, TimeSpan.Zero, DateTime.MaxValue);
            cache.PutItem("ab", 2, new[] { "A", "B" }, TimeSpan.Zero, DateTime.MaxValue);
            cache.PutItem("b", 3, new[] { "B" }, TimeSpan.Zero, DateTime.MaxValue);

            cache.InvalidateSets(new[] { "A" });

            object value;
            Assert.False(cache.GetItem("a", out value));
            Assert.False(cache.GetItem("ab", out value));
            Assert.True(cache.GetItem("b", out value));
        }

        [Fact]
        public void InvalidatedEntry_LeavesAConsistentLruChain()
        {
            var cache = NewCache(2);
            cache.PutItem("a", 1, new string[0], TimeSpan.Zero, DateTime.MaxValue);
            cache.PutItem("b", 2, new string[0], TimeSpan.Zero, DateTime.MaxValue);
            cache.InvalidateItem("a");
            cache.PutItem("c", 3, new string[0], TimeSpan.Zero, DateTime.MaxValue);

            // "b" is now the least recently used: adding "d" evicts it, not "c".
            cache.PutItem("d", 4, new string[0], TimeSpan.Zero, DateTime.MaxValue);

            object value;
            Assert.False(cache.GetItem("b", out value));
            Assert.True(cache.GetItem("c", out value));
            Assert.True(cache.GetItem("d", out value));
            Assert.Equal(2, cache.Count);
        }

        [Fact]
        public void MaxItemsOne_KeepsOnlyTheNewestEntry()
        {
            var cache = NewCache(1);
            cache.PutItem("a", 1, new[] { "A" }, TimeSpan.Zero, DateTime.MaxValue);
            cache.PutItem("b", 2, new[] { "A" }, TimeSpan.Zero, DateTime.MaxValue);

            object value;
            Assert.False(cache.GetItem("a", out value));
            Assert.True(cache.GetItem("b", out value));
            Assert.Equal(1, cache.Count);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Constructor_NonPositiveMaxItems_Throws(int maxItems)
        {
            // A cache that can hold nothing used to fail with a NullReferenceException on the first PutItem.
            Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryCache(maxItems));
        }

        [Fact]
        public void GetItem_ExpiredEntry_IsRemovedAndCountedAsMiss()
        {
            DateTime now = Now;
            var cache = new InMemoryCache { GetCurrentDate = () => now };
            cache.PutItem("k", 1, new[] { "A" }, TimeSpan.Zero, Now.AddMinutes(1));
            cache.PutItem("other", 2, new[] { "A" }, TimeSpan.Zero, Now.AddMinutes(1));

            now = Now.AddMinutes(1);
            object value;
            Assert.False(cache.GetItem("k", out value));

            Assert.Null(value);
            Assert.Equal(1, cache.CacheMisses);
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void SlidingExpiration_IsExtendedByEachHit()
        {
            DateTime now = Now;
            var cache = new InMemoryCache { GetCurrentDate = () => now };
            cache.PutItem("k", 1, new string[0], TimeSpan.FromMinutes(10), DateTime.MaxValue);

            object value;
            now = Now.AddMinutes(9);
            Assert.True(cache.GetItem("k", out value));
            now = Now.AddMinutes(18);
            Assert.True(cache.GetItem("k", out value));
            now = Now.AddMinutes(28);
            Assert.False(cache.GetItem("k", out value));
        }

        [Fact]
        public void PutItem_NullValue_IsCachedAsAHit()
        {
            var cache = NewCache();
            cache.PutItem("k", null, new string[0], TimeSpan.Zero, DateTime.MaxValue);

            object value;
            Assert.True(cache.GetItem("k", out value));
            Assert.Null(value);
        }

        [Fact]
        public void Keys_AreCaseSensitive()
        {
            var cache = NewCache();
            cache.PutItem("Key", 1, new string[0], TimeSpan.Zero, DateTime.MaxValue);

            object value;
            Assert.False(cache.GetItem("key", out value));
        }
    }
}
