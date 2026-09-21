// Copyright (c) Microsoft Corporation.  All rights reserved.

using System;
using System.Linq;
using EFCachingProvider.Caching;
using EFCachingProvider.Tests.Infrastructure;
using Xunit;

namespace EFCachingProvider.Tests
{
    public class EFCachingCommandDefinitionTests
    {
        [Fact]
        public void Query_IsCacheable_AndReportsItsEntitySet()
        {
            var definition = TestModel.Definition(TestModel.Query(TestModel.Customers));

            Assert.True(definition.IsCacheable());
            Assert.False(definition.IsModification);
            Assert.Equal(new[] { "Customers" }, definition.AffectedEntitySets.Select(s => s.Name));
        }

        [Fact]
        public void Join_ReportsEveryEntitySet()
        {
            var definition = TestModel.Definition(TestModel.CrossJoin(TestModel.Customers, TestModel.Orders));

            Assert.Equal(new[] { "Customers", "Orders" }, definition.AffectedEntitySets.Select(s => s.Name).OrderBy(n => n));
        }

        [Theory]
        [InlineData("GETDATE")]
        [InlineData("NEWID")]
        [InlineData("SYSDATETIME")]
        public void QueryCallingANonDeterministicFunction_IsNotCacheable(string function)
        {
            var definition = TestModel.Definition(TestModel.QueryCalling(TestModel.Customers, function));

            Assert.False(definition.IsCacheable());
        }

        [Fact]
        public void QueryCallingADeterministicFunction_IsCacheable()
        {
            var definition = TestModel.Definition(TestModel.QueryCalling(TestModel.Customers, "PI"));

            Assert.True(definition.IsCacheable());
        }

        [Fact]
        public void Delete_IsAModification_AndNotCacheable()
        {
            var definition = TestModel.Definition(TestModel.Delete(TestModel.Customers));

            Assert.True(definition.IsModification);
            Assert.False(definition.IsCacheable());
            Assert.Equal(new[] { "Customers" }, definition.AffectedEntitySets.Select(s => s.Name));
        }

        [Fact]
        public void NonCacheableFunctions_AreCaseInsensitive()
        {
            Assert.Contains("sqlserver.getdate", EFCachingCommandDefinition.NonCacheableFunctions);
        }

        [Fact]
        public void CustomPolicy_Default_CachesEverything()
        {
            var policy = new CustomCachingPolicy();

            Assert.True(policy.CanBeCached(TestModel.Definition(TestModel.Query(TestModel.Customers))));
        }

        [Fact]
        public void CustomPolicy_NonCacheableTable_Wins()
        {
            var policy = new CustomCachingPolicy();
            policy.CacheableTables.Add("Customers");
            policy.NonCacheableTables.Add("customers");

            Assert.False(policy.CanBeCached(TestModel.Definition(TestModel.Query(TestModel.Customers))));
        }

        [Fact]
        public void CustomPolicy_CacheableTables_RequireEveryTableToBeListed()
        {
            var policy = new CustomCachingPolicy();
            policy.CacheableTables.Add("Customers");

            Assert.True(policy.CanBeCached(TestModel.Definition(TestModel.Query(TestModel.Customers))));
            Assert.False(policy.CanBeCached(TestModel.Definition(TestModel.CrossJoin(TestModel.Customers, TestModel.Orders))));
        }

        [Fact]
        public void CustomPolicy_NullDefinition_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CustomCachingPolicy().CanBeCached(null));
        }

        [Fact]
        public void CustomPolicy_RowLimits_AreReported()
        {
            var policy = new CustomCachingPolicy { MinCacheableRows = 3, MaxCacheableRows = 7 };

            int min, max;
            policy.GetCacheableRows(null, out min, out max);

            Assert.Equal(3, min);
            Assert.Equal(7, max);
        }

        [Fact]
        public void BuiltInPolicies_HaveTheDocumentedBehaviour()
        {
            var definition = TestModel.Definition(TestModel.Query(TestModel.Customers));
            int min, max;
            TimeSpan sliding;
            DateTime absolute;

            Assert.True(CachingPolicy.CacheAll.CanBeCached(definition));
            CachingPolicy.CacheAll.GetCacheableRows(definition, out min, out max);
            Assert.Equal(0, min);
            Assert.Equal(int.MaxValue, max);

            Assert.False(CachingPolicy.NoCaching.CanBeCached(definition));

            CachingPolicy.CacheAll.GetExpirationTimeout(definition, out sliding, out absolute);
            Assert.Equal(TimeSpan.Zero, sliding);
            Assert.Equal(DateTime.MaxValue, absolute);
        }
    }
}
