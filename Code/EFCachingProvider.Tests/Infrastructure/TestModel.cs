// Copyright (c) Microsoft Corporation.  All rights reserved.

using System;
using System.Data.Common.CommandTrees;
using System.Globalization;
using System.Reflection;
using System.Data.Common.CommandTrees.ExpressionBuilder;
using System.Data.Mapping;
using System.Data.Metadata.Edm;
using System.IO;
using System.Linq;
using System.Xml;
using EFCachingProvider.Caching;
using EFTracingProvider;

namespace EFCachingProvider.Tests.Infrastructure
{
    /// <summary>
    /// A two-table EF model (Customers, Orders) built in-process, plus the command trees the tests need.
    /// Nothing here talks to a database: SqlClient is only asked for its provider manifest.
    /// </summary>
    internal static class TestModel
    {
        internal const string Csdl = @"<Schema Namespace='TestModel' Alias='Self' xmlns='http://schemas.microsoft.com/ado/2008/09/edm'>
  <EntityContainer Name='TestEntities'>
    <EntitySet Name='Customers' EntityType='TestModel.Customer' />
    <EntitySet Name='Orders' EntityType='TestModel.Order' />
  </EntityContainer>
  <EntityType Name='Customer'>
    <Key><PropertyRef Name='Id' /></Key>
    <Property Name='Id' Type='Int32' Nullable='false' />
    <Property Name='Name' Type='String' MaxLength='50' />
  </EntityType>
  <EntityType Name='Order'>
    <Key><PropertyRef Name='Id' /></Key>
    <Property Name='Id' Type='Int32' Nullable='false' />
    <Property Name='CustomerId' Type='Int32' Nullable='false' />
  </EntityType>
</Schema>";

        internal const string Ssdl = @"<Schema Namespace='TestModel.Store' Alias='Self' Provider='System.Data.SqlClient' ProviderManifestToken='2008' xmlns='http://schemas.microsoft.com/ado/2009/02/edm/ssdl'>
  <EntityContainer Name='TestModelStoreContainer'>
    <EntitySet Name='Customers' EntityType='TestModel.Store.Customers' Schema='dbo' />
    <EntitySet Name='Orders' EntityType='TestModel.Store.Orders' Schema='dbo' />
  </EntityContainer>
  <EntityType Name='Customers'>
    <Key><PropertyRef Name='Id' /></Key>
    <Property Name='Id' Type='int' Nullable='false' />
    <Property Name='Name' Type='nvarchar' MaxLength='50' />
  </EntityType>
  <EntityType Name='Orders'>
    <Key><PropertyRef Name='Id' /></Key>
    <Property Name='Id' Type='int' Nullable='false' />
    <Property Name='CustomerId' Type='int' Nullable='false' />
  </EntityType>
</Schema>";

        internal const string Msl = @"<Mapping Space='C-S' xmlns='http://schemas.microsoft.com/ado/2008/09/mapping/cs'>
  <EntityContainerMapping StorageEntityContainer='TestModelStoreContainer' CdmEntityContainer='TestEntities'>
    <EntitySetMapping Name='Customers'>
      <EntityTypeMapping TypeName='TestModel.Customer'>
        <MappingFragment StoreEntitySet='Customers'>
          <ScalarProperty Name='Id' ColumnName='Id' />
          <ScalarProperty Name='Name' ColumnName='Name' />
        </MappingFragment>
      </EntityTypeMapping>
    </EntitySetMapping>
    <EntitySetMapping Name='Orders'>
      <EntityTypeMapping TypeName='TestModel.Order'>
        <MappingFragment StoreEntitySet='Orders'>
          <ScalarProperty Name='Id' ColumnName='Id' />
          <ScalarProperty Name='CustomerId' ColumnName='CustomerId' />
        </MappingFragment>
      </EntityTypeMapping>
    </EntitySetMapping>
  </EntityContainerMapping>
</Mapping>";

        private static readonly Lazy<MetadataWorkspace> StoreWorkspace = new Lazy<MetadataWorkspace>(() =>
        {
            // Query trees are validated against the whole model, so all three spaces must be registered.
            var eic = new EdmItemCollection(new[] { XmlReader.Create(new StringReader(Csdl)) });
            var sic = new StoreItemCollection(new[] { XmlReader.Create(new StringReader(Ssdl)) });
            var msl = new StorageMappingItemCollection(eic, sic, new[] { XmlReader.Create(new StringReader(Msl)) });
            var workspace = new MetadataWorkspace();
            workspace.RegisterItemCollection(eic);
            workspace.RegisterItemCollection(sic);
            workspace.RegisterItemCollection(msl);
            return workspace;
        });

        internal static MetadataWorkspace Workspace
        {
            get { return StoreWorkspace.Value; }
        }

        internal static EntitySetBase Customers
        {
            get { return GetSet("Customers"); }
        }

        internal static EntitySetBase Orders
        {
            get { return GetSet("Orders"); }
        }

        /// <summary>
        /// Registers the wrapper providers. DbProviderFactories snapshots its table on first use, so this only
        /// takes effect before anything else asks for a factory - see <see cref="TestAssemblySetup"/>.
        /// </summary>
        internal static void RegisterProviders()
        {
            EFCachingProviderConfiguration.RegisterProvider();
            EFTracingProviderConfiguration.RegisterProvider();
        }

        /// <summary>Writes the model to a fresh directory and returns its path.</summary>
        internal static string WriteToDirectory()
        {
            string dir = Path.Combine(Path.GetTempPath(), "EFCachingProvider.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Model.csdl"), Csdl);
            File.WriteAllText(Path.Combine(dir, "Model.ssdl"), Ssdl);
            File.WriteAllText(Path.Combine(dir, "Model.msl"), Msl);
            return dir;
        }

        /// <summary><c>SELECT * FROM set</c>.</summary>
        internal static DbQueryCommandTree Query(EntitySetBase set)
        {
            return NewTree<DbQueryCommandTree>(Workspace, DataSpace.SSpace, set.Scan());
        }

        /// <summary><c>SELECT * FROM a CROSS JOIN b</c>.</summary>
        internal static DbQueryCommandTree CrossJoin(EntitySetBase a, EntitySetBase b)
        {
            return NewTree<DbQueryCommandTree>(
                Workspace,
                DataSpace.SSpace,
                DbExpressionBuilder.CrossJoin(new[] { a.Scan().BindAs("a"), b.Scan().BindAs("b") }));
        }

        /// <summary><c>SELECT storeFunction() FROM set</c>.</summary>
        internal static DbQueryCommandTree QueryCalling(EntitySetBase set, string storeFunctionName)
        {
            EdmFunction function = Workspace
                .GetItems<EdmFunction>(DataSpace.SSpace)
                .First(f => f.Name == storeFunctionName && f.Parameters.Count == 0);

            return NewTree<DbQueryCommandTree>(
                Workspace,
                DataSpace.SSpace,
                set.Scan().BindAs("x").Project(function.Invoke()));
        }

        /// <summary><c>DELETE FROM set</c>.</summary>
        internal static DbDeleteCommandTree Delete(EntitySetBase set)
        {
            return NewTree<DbDeleteCommandTree>(Workspace, DataSpace.SSpace, set.Scan().BindAs("t"), DbExpressionBuilder.True);
        }

        internal static EFCachingCommandDefinition Definition(DbCommandTree tree)
        {
            return new EFCachingCommandDefinition(null, tree);
        }

        /// <summary>A caching connection over <paramref name="store"/>, caching everything into a private cache.</summary>
        internal static EFCachingConnection CachingConnection(FakeDbConnection store, ICache cache)
        {
            return new EFCachingConnection(store) { Cache = cache, CachingPolicy = CachingPolicy.CacheAll };
        }

        internal static EFCachingCommand Command(EFCachingConnection connection, DbCommandTree tree, FakeDbCommand store, string text)
        {
            var command = new EFCachingCommand(store, Definition(tree));
            command.Connection = connection;
            command.CommandText = text;
            return command;
        }

        /// <summary>
        /// Command tree constructors are internal in the .NET Framework's EF (EF6 made them public), and there is
        /// no public factory, so reflection is the only way to build one without a live query pipeline.
        /// </summary>
        private static T NewTree<T>(params object[] args) where T : DbCommandTree
        {
            return (T)Activator.CreateInstance(
                typeof(T),
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                args,
                CultureInfo.InvariantCulture);
        }

        private static EntitySetBase GetSet(string name)
        {
            return Workspace.GetEntityContainer("TestModelStoreContainer", DataSpace.SSpace).BaseEntitySets[name];
        }
    }
}
