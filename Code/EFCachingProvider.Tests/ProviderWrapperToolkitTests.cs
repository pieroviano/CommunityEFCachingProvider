// Copyright (c) Microsoft Corporation.  All rights reserved.

using System;
using System.Data;
using System.Data.Common;
using System.Data.EntityClient;
using System.Data.Metadata.Edm;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using EFCachingProvider.Caching;
using EFCachingProvider.Tests.Infrastructure;
using EFProviderWrapperToolkit;
using EFTracingProvider;
using Xunit;

namespace EFCachingProvider.Tests
{
    public class ProviderWrapperToolkitTests
    {
        private const string StoreConnectionString = "Data Source=.;Initial Catalog=Nowhere;Integrated Security=True";

        public ProviderWrapperToolkitTests()
        {
            TestModel.RegisterProviders();
        }

        private static EntityConnectionStringBuilder EntityConnectionString(string modelDirectory)
        {
            return new EntityConnectionStringBuilder
            {
                Metadata = string.Join("|", new[] { "Model.csdl", "Model.ssdl", "Model.msl" }.Select(f => Path.Combine(modelDirectory, f))),
                Provider = "System.Data.SqlClient",
                ProviderConnectionString = StoreConnectionString,
            };
        }

        [Fact]
        public void RegisterProvider_MakesTheFactoriesResolvable()
        {
            Assert.IsType<EFCachingProviderFactory>(DbProviderFactories.GetFactory("EFCachingProvider"));
            Assert.IsType<EFTracingProviderFactory>(DbProviderFactories.GetFactory("EFTracingProvider"));
        }

        [Fact]
        public void RegisterProvider_Twice_RegistersOnce()
        {
            TestModel.RegisterProviders();

            DataTable classes = DbProviderFactories.GetFactoryClasses();
            Assert.Single(classes.Select("InvariantName='EFCachingProvider'"));
        }

        [Theory]
        [InlineData(null, "x", typeof(object))]
        [InlineData("x", "", typeof(object))]
        [InlineData("x", "x", null)]
        public void RegisterProvider_InvalidArguments_Throw(string name, string invariantName, Type factoryType)
        {
            Assert.Throws<ArgumentNullException>(() => DbProviderFactoryBase.RegisterProvider(name, invariantName, factoryType));
        }

        [Fact]
        public void ConnectionString_WithWrappedProviderPrefix_CreatesThatProvidersConnection()
        {
            var connection = new EFCachingConnection();
            connection.ConnectionString = "wrappedProvider=System.Data.SqlClient;" + StoreConnectionString;

            Assert.IsType<SqlConnection>(connection.WrappedConnection);
            Assert.Equal("System.Data.SqlClient", connection.WrappedProviderInvariantName);
            Assert.Equal(StoreConnectionString, connection.WrappedConnection.ConnectionString);
            Assert.Equal("wrappedProvider=System.Data.SqlClient;" + StoreConnectionString, connection.ConnectionString);
            Assert.Equal("Nowhere", connection.Database);
            Assert.Equal(".", connection.DataSource);
            Assert.Equal(ConnectionState.Closed, connection.State);
        }

        [Fact]
        public void ConnectionString_PrefixIsCaseInsensitive()
        {
            var connection = new EFCachingConnection();
            connection.ConnectionString = "WRAPPEDPROVIDER=System.Data.SqlClient;" + StoreConnectionString;

            Assert.IsType<SqlConnection>(connection.WrappedConnection);
        }

        [Fact]
        public void ConnectionString_PrefixWithoutTerminator_Throws()
        {
            var connection = new EFCachingConnection();

            Assert.Throws<ArgumentException>(() => connection.ConnectionString = "wrappedProvider=System.Data.SqlClient");
        }

        [Fact]
        public void ConnectionString_WithoutPrefix_UsesTheConfiguredDefaultProvider()
        {
            string previous = EFCachingProviderConfiguration.DefaultWrappedProvider;
            EFCachingProviderConfiguration.DefaultWrappedProvider = "System.Data.SqlClient";
            try
            {
                var connection = new EFCachingConnection();
                connection.ConnectionString = StoreConnectionString;

                Assert.IsType<SqlConnection>(connection.WrappedConnection);
                Assert.Equal("System.Data.SqlClient", connection.WrappedProviderInvariantName);
            }
            finally
            {
                EFCachingProviderConfiguration.DefaultWrappedProvider = previous;
            }
        }

        [Fact]
        public void UnconfiguredConnection_BehavesLikeANewDbConnection()
        {
            // A fresh DbConnection is closed and has an empty connection string; disposing it is harmless.
            var connection = new EFCachingConnection();

            Assert.Equal(string.Empty, connection.ConnectionString);
            Assert.Equal(ConnectionState.Closed, connection.State);
            connection.Dispose();
        }

        [Fact]
        public void Dispose_DisposesTheWrappedConnection()
        {
            var store = new FakeDbConnection();
            bool disposed = false;
            store.Disposed += (s, e) => disposed = true;

            new EFCachingConnection(store).Dispose();

            Assert.True(disposed);
        }

        [Fact]
        public void ConnectionMembers_DelegateToTheWrappedConnection()
        {
            var store = new FakeDbConnection();
            var connection = new EFTracingConnection(store);

            connection.Open();
            Assert.Equal(ConnectionState.Open, connection.State);
            Assert.Equal("1.0", connection.ServerVersion);
            connection.Close();
            Assert.Equal(ConnectionState.Closed, store.State);
        }

        [Fact]
        public void BeginTransaction_WrapsTheStoreTransaction()
        {
            var store = new FakeDbConnection();
            var connection = new EFCachingConnection(store);

            using (var tx = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                var cachingTx = Assert.IsType<EFCachingTransaction>(tx);
                Assert.Same(connection, cachingTx.Connection);
                Assert.Equal(IsolationLevel.Serializable, cachingTx.IsolationLevel);
                cachingTx.Commit();
                Assert.True(((FakeDbTransaction)cachingTx.WrappedTransaction).Committed);
            }

            Assert.Equal(1, store.TransactionsStarted);
        }

        [Fact]
        public void UnwrapConnection_WalksTheWholeChain()
        {
            var caching = new EFCachingConnection(new FakeDbConnection());
            var tracing = new EFTracingConnection(caching);

            Assert.Same(caching, tracing.UnwrapConnection<EFCachingConnection>());
            Assert.Same(tracing, tracing.UnwrapConnection<EFTracingConnection>());

            EFTracingConnection missing;
            Assert.False(caching.TryUnwrapConnection(out missing));
            Assert.Throws<InvalidOperationException>(() => caching.UnwrapConnection<EFTracingConnection>());
        }

        [Fact]
        public void GetCache_And_SetCache_ReachTheCachingConnection()
        {
            var caching = new EFCachingConnection(new FakeDbConnection());
            var tracing = new EFTracingConnection(caching);
            var cache = new InMemoryCache();

            tracing.SetCache(cache);

            Assert.Same(cache, caching.Cache);
            Assert.Same(cache, tracing.GetCache());
        }

        [Fact]
        public void EntityConnection_ChainsTheWrappersInOrder()
        {
            string dir = TestModel.WriteToDirectory();

            EntityConnection ec = EntityConnectionWrapperUtils.EntityConnectionWithWrappersFromEntityConnectionString(
                EntityConnectionString(dir), "EFTracingProvider", "EFCachingProvider");

            var caching = Assert.IsType<EFCachingConnection>(ec.StoreConnection);
            var tracing = Assert.IsType<EFTracingConnection>(caching.WrappedConnection);
            Assert.IsType<SqlConnection>(tracing.WrappedConnection);
            Assert.Same(tracing, ec.UnwrapConnection<EFTracingConnection>());
        }

        [Fact]
        public void EntityConnection_WrappersKnowWhatTheyWrap()
        {
            // The name is what lets the connection string round-trip and the manifest token name its provider.
            string dir = TestModel.WriteToDirectory();

            EntityConnection ec = EntityConnectionWrapperUtils.EntityConnectionWithWrappersFromEntityConnectionString(
                EntityConnectionString(dir), "EFTracingProvider", "EFCachingProvider");

            var caching = (EFCachingConnection)ec.StoreConnection;
            var tracing = (EFTracingConnection)caching.WrappedConnection;
            Assert.Equal("EFTracingProvider", caching.WrappedProviderInvariantName);
            Assert.Equal("System.Data.SqlClient", tracing.WrappedProviderInvariantName);
            Assert.Equal(
                "wrappedProvider=EFTracingProvider;wrappedProvider=System.Data.SqlClient;" + StoreConnectionString,
                caching.ConnectionString);
        }

        [Fact]
        public void WrapperConnectionString_RoundTrips()
        {
            string dir = TestModel.WriteToDirectory();
            EntityConnection ec = EntityConnectionWrapperUtils.EntityConnectionWithWrappersFromEntityConnectionString(
                EntityConnectionString(dir), "EFTracingProvider", "EFCachingProvider");

            var copy = new EFCachingConnection { ConnectionString = ec.StoreConnection.ConnectionString };

            var tracing = Assert.IsType<EFTracingConnection>(copy.WrappedConnection);
            Assert.IsType<SqlConnection>(tracing.WrappedConnection);
            Assert.Equal(StoreConnectionString, tracing.WrappedConnection.ConnectionString);
        }

        [Fact]
        public void EntityConnection_MetadataIsRewrittenForTheOutermostWrapper()
        {
            string dir = TestModel.WriteToDirectory();
            EntityConnection ec = EntityConnectionWrapperUtils.EntityConnectionWithWrappersFromEntityConnectionString(
                EntityConnectionString(dir), "EFTracingProvider", "EFCachingProvider");

            var sic = (StoreItemCollection)ec.GetMetadataWorkspace().GetItemCollection(DataSpace.SSpace);

            Assert.NotNull(sic.GetEntityContainer("TestModelStoreContainer").BaseEntitySets["Customers"]);
            Assert.Same(
                ec.GetMetadataWorkspace(),
                EntityConnectionWrapperUtils.EntityConnectionWithWrappersFromEntityConnectionString(
                    EntityConnectionString(dir), "EFTracingProvider", "EFCachingProvider").GetMetadataWorkspace());
        }

        [Fact]
        public void EntityConnection_SameModelWithDifferentWrappers_GetsItsOwnMetadata()
        {
            // The memoized workspace carries the wrapper chain in its SSDL; sharing it between chains hands
            // one chain the other's provider.
            string dir = TestModel.WriteToDirectory();

            MetadataWorkspace tracingOnly = EntityConnectionWrapperUtils
                .EntityConnectionWithWrappersFromEntityConnectionString(EntityConnectionString(dir), "EFTracingProvider")
                .GetMetadataWorkspace();
            MetadataWorkspace cachingOnly = EntityConnectionWrapperUtils
                .EntityConnectionWithWrappersFromEntityConnectionString(EntityConnectionString(dir), "EFCachingProvider")
                .GetMetadataWorkspace();

            Assert.NotSame(tracingOnly, cachingOnly);
        }

        [Fact]
        public void EntityConnection_UnknownMetadataComponent_Throws()
        {
            var builder = new EntityConnectionStringBuilder
            {
                Metadata = Path.Combine(Path.GetTempPath(), "nothing.xyz"),
                Provider = "System.Data.SqlClient",
                ProviderConnectionString = StoreConnectionString,
            };

            Assert.Throws<NotSupportedException>(() =>
                EntityConnectionWrapperUtils.EntityConnectionWithWrappersFromEntityConnectionString(builder, "EFCachingProvider"));
        }

        [Fact]
        public void EntityConnection_TildePathOutsideAspNet_Throws()
        {
            var builder = new EntityConnectionStringBuilder
            {
                Metadata = "~/App_Data/Model.csdl",
                Provider = "System.Data.SqlClient",
                ProviderConnectionString = StoreConnectionString,
            };

            Assert.Throws<NotSupportedException>(() =>
                EntityConnectionWrapperUtils.EntityConnectionWithWrappersFromEntityConnectionString(builder, "EFCachingProvider"));
        }

        [Fact]
        public void EntityConnection_ModelDirectory_IsLoaded()
        {
            string dir = TestModel.WriteToDirectory();
            var builder = new EntityConnectionStringBuilder
            {
                Metadata = dir,
                Provider = "System.Data.SqlClient",
                ProviderConnectionString = StoreConnectionString,
            };

            EntityConnection ec = EntityConnectionWrapperUtils.EntityConnectionWithWrappersFromEntityConnectionString(builder, "EFCachingProvider");

            Assert.NotNull(ec.GetMetadataWorkspace().GetItemCollection(DataSpace.CSpace));
        }

        [Fact]
        public void CreateFromName_UnknownName_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                EntityConnectionWrapperUtils.CreateEntityConnectionWithWrappersFromName("name=NoSuchEntry", "EFCachingProvider"));
        }

        [Fact]
        public void ProviderManifest_ForAWrappedToken_WrapsTheStoreManifest()
        {
            DbProviderServices services = DbProviderServices.GetProviderServices(new EFCachingConnection(new FakeDbConnection()));

            var manifest = Assert.IsType<DbProviderManifestWrapper>(services.GetProviderManifest("System.Data.SqlClient;2008"));

            Assert.Equal("System.Data.SqlClient", manifest.WrappedProviderManifestInvariantName);
            Assert.Equal("SqlServer", manifest.NamespaceName);
            Assert.NotEmpty(manifest.GetStoreTypes());
        }

        [Fact]
        public void ProviderManifest_ChainedToken_UnwrapsEachLevel()
        {
            DbProviderServices services = DbProviderServices.GetProviderServices(new EFCachingConnection(new FakeDbConnection()));

            var outer = (DbProviderManifestWrapper)services.GetProviderManifest("EFTracingProvider;System.Data.SqlClient;2008");

            Assert.Equal("EFTracingProvider", outer.WrappedProviderManifestInvariantName);
            var inner = Assert.IsType<DbProviderManifestWrapper>(outer.WrappedProviderManifest);
            Assert.Equal("System.Data.SqlClient", inner.WrappedProviderManifestInvariantName);
        }

        [Fact]
        public void ProviderManifest_StoreSchemaDefinition_NamesTheWrapper()
        {
            DbProviderServices services = DbProviderServices.GetProviderServices(new EFCachingConnection(new FakeDbConnection()));
            DbProviderManifest manifest = services.GetProviderManifest("System.Data.SqlClient;2008");

            var ssdl = System.Xml.Linq.XElement.Load(manifest.GetInformation(DbProviderManifest.StoreSchemaDefinition));

            Assert.Equal("EFCachingProvider", (string)ssdl.Attribute("Provider"));
            Assert.StartsWith("System.Data.SqlClient;", (string)ssdl.Attribute("ProviderManifestToken"));
        }

        [Fact]
        public void ProviderManifest_EmptyToken_Throws()
        {
            DbProviderServices services = DbProviderServices.GetProviderServices(new EFCachingConnection(new FakeDbConnection()));

            // DbProviderServices.GetProviderManifest wraps whatever the provider throws in ProviderIncompatibleException.
            var thrown = Assert.Throws<ProviderIncompatibleException>(() => services.GetProviderManifest(string.Empty));
            Assert.IsType<ArgumentNullException>(thrown.InnerException);
        }
    }
}
